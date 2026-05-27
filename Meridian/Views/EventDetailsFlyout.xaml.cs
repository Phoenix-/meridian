using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Meridian.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace Meridian.Views;

public sealed partial class EventDetailsFlyout : UserControl
{
    // Control characters used to bracket extracted anchor placeholders while
    // we strip remaining HTML tags. Picked from the C0 range so they cannot
    // legitimately appear in event descriptions returned by Google.
    private const char AnchorOpen = '\u0001';
    private const char AnchorClose = '\u0002';

    private CalendarEvent? _event;
    private Flyout? _owningFlyout;

    public EventDetailsFlyout()
    {
        InitializeComponent();
    }

    public static void Show(FrameworkElement target, CalendarEvent ev)
    {
        var content = new EventDetailsFlyout();
        content.Bind(ev);

        var flyout = new Flyout
        {
            Content = content,
            XamlRoot = target.XamlRoot,
        };
        content._owningFlyout = flyout;
        flyout.ShowAt(target, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.Auto,
        });
    }

    private void Bind(CalendarEvent ev)
    {
        _event = ev;

        TitleText.Text = ev.Title;
        TimeText.Text = FormatTimeRange(ev);

        var calColor = ParseHex(ev.CalendarColor) ?? Colors.SteelBlue;
        ColorStripe.Background = new SolidColorBrush(calColor);
        CalendarDot.Fill = new SolidColorBrush(calColor);

        var calText = string.IsNullOrEmpty(ev.CalendarTitle) ? "" : ev.CalendarTitle;
        if (!string.IsNullOrEmpty(ev.AccountEmail))
            calText = string.IsNullOrEmpty(calText) ? ev.AccountEmail : $"{calText} · {ev.AccountEmail}";
        if (string.IsNullOrEmpty(calText))
            CalendarRow.Visibility = Visibility.Collapsed;
        else
            CalendarText.Text = calText;

        if (!string.IsNullOrWhiteSpace(ev.Description))
        {
            BuildDescription(ev.Description);
            DescriptionScroll.Visibility = Visibility.Visible;
        }

        if (ev.ReminderMinutes is { Count: > 0 })
        {
            RemindersStack.Visibility = Visibility.Visible;
            foreach (var m in ev.ReminderMinutes.OrderByDescending(m => m))
                RemindersStack.Children.Add(BuildReminderRow(m));
        }

        OpenInGoogleButton.Visibility = string.IsNullOrEmpty(ev.HtmlLink)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void OnOpenInGoogleClick(object sender, RoutedEventArgs e)
    {
        if (_event?.HtmlLink is not { } link) return;

        var url = link;
        if (!string.IsNullOrEmpty(_event.AccountEmail))
        {
            var sep = link.Contains('?') ? '&' : '?';
            url = $"{link}{sep}authuser={Uri.EscapeDataString(_event.AccountEmail)}";
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Launcher.LaunchUriAsync(uri);

        _owningFlyout?.Hide();
    }

    private static string FormatTimeRange(CalendarEvent ev)
    {
        if (ev.IsAllDay)
        {
            var oneDay = (ev.End - ev.Start).TotalDays <= 1.01;
            return oneDay
                ? $"Весь день, {ev.Start:dddd, d MMMM}"
                : $"Весь день, {ev.Start:d MMMM} – {ev.End.AddDays(-1):d MMMM}";
        }

        if (ev.Start.Date == ev.End.Date)
            return $"{ev.Start:dddd, d MMMM}, {ev.Start:HH:mm}–{ev.End:HH:mm}";

        return $"{ev.Start:d MMM HH:mm} – {ev.End:d MMM HH:mm}";
    }

    private static FrameworkElement BuildReminderRow(int minutes)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(new TextBlock
        {
            Text = "", // Segoe Fluent Icons: Ringer
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
        });
        sp.Children.Add(new TextBlock
        {
            Text = FormatReminder(minutes),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return sp;
    }

    private static string FormatReminder(int minutes)
    {
        if (minutes <= 0) return "В момент начала";
        if (minutes % (60 * 24 * 7) == 0)
        {
            var w = minutes / (60 * 24 * 7);
            return $"За {w} {Plural(w, "неделю", "недели", "недель")}";
        }
        if (minutes % (60 * 24) == 0)
        {
            var d = minutes / (60 * 24);
            return $"За {d} {Plural(d, "день", "дня", "дней")}";
        }
        if (minutes % 60 == 0)
        {
            var h = minutes / 60;
            return $"За {h} {Plural(h, "час", "часа", "часов")}";
        }
        return $"За {minutes} {Plural(minutes, "минуту", "минуты", "минут")}";
    }

    private static string Plural(int n, string one, string few, string many)
    {
        var mod100 = n % 100;
        if (mod100 is >= 11 and <= 14) return many;
        return (n % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }

    // ── Description rendering ──────────────────────────────────────────────────
    // Google event descriptions are a small HTML subset: <b>/<strong>, <i>/<em>,
    // <u>, <a href>, <br>, <p>. Sometimes the markup is double-encoded
    // ("&lt;b&gt;"), so we HTML-decode the whole string once up front, then run
    // a tiny tokenizer that walks tags and emits XAML inlines. A style stack
    // handles nesting (e.g. <strong><b>…</b></strong>). Bare URLs inside text
    // are auto-linked. There is no built-in WinRT HTML DOM parser that takes an
    // arbitrary string, so a hand-rolled tokenizer is the AOT-safe choice.

    private static readonly Regex UrlRegex = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase);

    [Flags]
    private enum TextStyle { None = 0, Bold = 1, Italic = 2, Underline = 4 }

    private void BuildDescription(string raw)
    {
        DescriptionText.Inlines.Clear();

        // Decode entities so double-encoded tags become real tags. Real tags
        // that were never encoded are unaffected.
        var html = WebUtility.HtmlDecode(raw);

        var style = TextStyle.None;
        string? linkHref = null;
        var text = new System.Text.StringBuilder();

        void FlushText()
        {
            if (text.Length == 0) return;
            EmitText(text.ToString(), style, linkHref);
            text.Clear();
        }

        int i = 0;
        while (i < html.Length)
        {
            char c = html[i];
            if (c != '<')
            {
                text.Append(c);
                i++;
                continue;
            }

            int close = html.IndexOf('>', i + 1);
            if (close < 0) { text.Append(html[i..]); break; }

            FlushText();
            var tag = html.Substring(i + 1, close - i - 1).Trim();
            i = close + 1;

            bool isEnd = tag.StartsWith('/');
            var name = (isEnd ? tag[1..] : tag).Split([' ', '\t', '\r', '\n', '/'], 2)[0].ToLowerInvariant();

            switch (name)
            {
                case "b" or "strong":
                    style = isEnd ? style & ~TextStyle.Bold : style | TextStyle.Bold; break;
                case "i" or "em":
                    style = isEnd ? style & ~TextStyle.Italic : style | TextStyle.Italic; break;
                case "u":
                    style = isEnd ? style & ~TextStyle.Underline : style | TextStyle.Underline; break;
                case "a":
                    linkHref = isEnd ? null : ExtractHref(tag); break;
                case "br":
                    DescriptionText.Inlines.Add(new LineBreak()); break;
                case "p" or "div":
                    if (isEnd && DescriptionText.Inlines.Count > 0)
                        DescriptionText.Inlines.Add(new LineBreak());
                    break;
                // ul/li/span/font/etc. — ignored, their text content still flows.
            }
        }
        FlushText();
    }

    private static string? ExtractHref(string tag)
    {
        var m = Regex.Match(tag, @"href\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    // Emits a text fragment, auto-linking bare URLs, applying the current style,
    // and wrapping in a Hyperlink when inside an <a href>.
    private void EmitText(string fragment, TextStyle style, string? linkHref)
    {
        if (linkHref is not null)
        {
            AddInline(StyleRun(fragment, style), linkHref);
            return;
        }

        int pos = 0;
        foreach (Match m in UrlRegex.Matches(fragment))
        {
            if (m.Index > pos)
                AddInline(StyleRun(fragment[pos..m.Index], style), null);
            AddInline(StyleRun(m.Value, style), m.Value);
            pos = m.Index + m.Length;
        }
        if (pos < fragment.Length)
            AddInline(StyleRun(fragment[pos..], style), null);
    }

    // Wraps a fragment in Bold/Italic/Underline spans per style flags. Returns
    // the outermost inline plus the inner span that text should sit in.
    private static (Inline Outer, Span Inner) StyleRun(string textValue, TextStyle style)
    {
        var run = new Run { Text = textValue };
        Span inner = new Span();
        inner.Inlines.Add(run);
        Inline outer = inner;

        if (style.HasFlag(TextStyle.Underline)) { var u = new Underline(); u.Inlines.Add(outer); outer = u; }
        if (style.HasFlag(TextStyle.Italic))    { var it = new Italic(); it.Inlines.Add(outer); outer = it; }
        if (style.HasFlag(TextStyle.Bold))      { var b = new Bold();   b.Inlines.Add(outer); outer = b; }

        return (outer, inner);
    }

    private void AddInline((Inline Outer, Span Inner) styled, string? linkHref)
    {
        if (linkHref is not null && Uri.TryCreate(linkHref, UriKind.Absolute, out var uri))
        {
            var link = new Hyperlink { NavigateUri = uri };
            link.Inlines.Add(styled.Outer);
            DescriptionText.Inlines.Add(link);
        }
        else
        {
            DescriptionText.Inlines.Add(styled.Outer);
        }
    }

    private static Color? ParseHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        var s = hex.TrimStart('#');
        if (s.Length != 6) return null;
        try
        {
            return Color.FromArgb(
                255,
                byte.Parse(s.Substring(0, 2), System.Globalization.NumberStyles.HexNumber),
                byte.Parse(s.Substring(2, 2), System.Globalization.NumberStyles.HexNumber),
                byte.Parse(s.Substring(4, 2), System.Globalization.NumberStyles.HexNumber));
        }
        catch { return null; }
    }
}
