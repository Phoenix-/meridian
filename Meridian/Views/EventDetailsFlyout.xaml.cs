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
using Microsoft.UI.Xaml.Shapes;
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

        if (!string.IsNullOrEmpty(ev.MeetJoinUrl))
        {
            MeetStack.Visibility = Visibility.Visible;
            MeetStack.Children.Add(BuildMeetBlock(ev.MeetJoinUrl));
        }

        if (!string.IsNullOrWhiteSpace(ev.Location))
        {
            LocationStack.Visibility = Visibility.Visible;
            BuildLocationBlock(LocationStack, ev.Location);
        }

        if (ev.Attendees is { Count: > 0 })
        {
            GuestsStack.Visibility = Visibility.Visible;
            BuildGuestsBlock(GuestsStack, ev.Attendees);
        }

        if (ev.Rooms is { Count: > 0 })
        {
            RoomsStack.Visibility = Visibility.Visible;
            BuildRoomsBlock(RoomsStack, ev.Rooms);
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

    // ── Meet / Guests / Rooms blocks ───────────────────────────────────────────

    // Threshold above which the guest list is replaced by a "too many to show"
    // note (mirrors Google Calendar Web behaviour on large meetings).
    private const int GuestListMaxShown = 5;

    private FrameworkElement BuildMeetBlock(string joinUrl)
    {
        var sp = new StackPanel { Spacing = 2 };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock
        {
            Text = "", // Video camera glyph
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.85,
        });
        var link = new HyperlinkButton
        {
            Content = "Присоединиться к Google Meet",
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (Uri.TryCreate(joinUrl, UriKind.Absolute, out var uri))
            link.NavigateUri = uri;
        row.Children.Add(link);
        sp.Children.Add(row);

        var host = TryGetHostAndPath(joinUrl);
        if (!string.IsNullOrEmpty(host))
        {
            sp.Children.Add(new TextBlock
            {
                Text = host,
                FontSize = 11,
                Margin = new Thickness(24, 0, 0, 0),
                Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                IsTextSelectionEnabled = true,
            });
        }
        return sp;
    }

    private void BuildLocationBlock(StackPanel parent, string location)
    {
        parent.Children.Add(new TextBlock
        {
            Text = "", // MapPin glyph
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Opacity = 0.85,
        });

        if (Uri.TryCreate(location, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            parent.Children.Add(new HyperlinkButton
            {
                Content = new TextBlock { Text = location, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
                NavigateUri = uri,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            parent.Children.Add(new TextBlock
            {
                Text = location,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }

    private static string? TryGetHostAndPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var path = uri.AbsolutePath.TrimEnd('/');
        return string.IsNullOrEmpty(path) ? uri.Host : $"{uri.Host}{path}";
    }

    private void BuildGuestsBlock(StackPanel parent, List<EventAttendee> guests)
    {
        var ordered = guests
            .OrderByDescending(a => a.IsSelf)
            .ThenByDescending(a => a.IsOrganizer)
            .ThenBy(a => a.DisplayName ?? a.Email, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        parent.Children.Add(BuildGuestsHeader(ordered));

        if (ordered.Count > GuestListMaxShown)
        {
            parent.Children.Add(new TextBlock
            {
                Text = "Слишком много участников, чтобы показать список",
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var a in ordered)
            parent.Children.Add(BuildAttendeeRow(a));
    }

    private static FrameworkElement BuildGuestsHeader(IReadOnlyList<EventAttendee> guests)
    {
        int accepted = 0, declined = 0, tentative = 0, needs = 0;
        foreach (var g in guests)
        {
            switch (g.ResponseStatus)
            {
                case "accepted":  accepted++; break;
                case "declined":  declined++; break;
                case "tentative": tentative++; break;
                default:          needs++;    break;
            }
        }

        var summary = new List<string>();
        if (accepted  > 0) summary.Add($"{accepted} — да");
        if (declined  > 0) summary.Add($"{declined} — нет");
        if (tentative > 0) summary.Add($"{tentative} — возможно");
        if (needs     > 0) summary.Add($"{needs} — не ответил(а)");

        var text = $"Гости ({guests.Count})";
        if (summary.Count > 0) text += " · " + string.Join(", ", summary);

        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
    }

    private FrameworkElement BuildAttendeeRow(EventAttendee a)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = BuildAvatar(a);
        Grid.SetColumn(avatar, 0);
        grid.Children.Add(avatar);

        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = FormatAttendeeName(a),
            FontSize = 13,
            FontWeight = a.IsSelf ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsTextSelectionEnabled = true,
        };
        middle.Children.Add(name);

        var subParts = new List<string>();
        if (a.IsOrganizer) subParts.Add("организатор");
        if (a.IsOptional) subParts.Add("необязательно");
        if (subParts.Count > 0)
        {
            middle.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", subParts),
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            });
        }
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);

        if (BuildStatusGlyph(a.ResponseStatus) is { } status)
        {
            Grid.SetColumn(status, 2);
            grid.Children.Add(status);
        }

        return grid;
    }

    private static string FormatAttendeeName(EventAttendee a)
    {
        var label = !string.IsNullOrWhiteSpace(a.DisplayName)
            ? a.DisplayName!
            : DeriveNameFromEmail(a.Email);
        return a.IsSelf ? $"{label} (вы)" : label;
    }

    private static string DeriveNameFromEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }

    // Renders the colored circle with the attendee's initial. Color is a
    // deterministic hash of the email so the same person looks the same
    // across re-renders and across events.
    private static FrameworkElement BuildAvatar(EventAttendee a)
    {
        var color = ColorFromEmail(a.Email);
        var initial = GetInitial(a.DisplayName, a.Email);
        var grid = new Grid
        {
            Width = 28,
            Height = 28,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(new Ellipse
        {
            Width = 28,
            Height = 28,
            Fill = new SolidColorBrush(color),
        });
        grid.Children.Add(new TextBlock
        {
            Text = initial,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return grid;
    }

    private static string GetInitial(string? displayName, string email)
    {
        var source = !string.IsNullOrWhiteSpace(displayName) ? displayName! : email;
        foreach (var ch in source)
            if (char.IsLetterOrDigit(ch))
                return char.ToUpperInvariant(ch).ToString();
        return "?";
    }

    // Palette borrowed loosely from Material's avatar colors — readable on
    // white text and visually distinct from each other.
    private static readonly Color[] AvatarPalette =
    [
        Color.FromArgb(255, 0xEF, 0x53, 0x50),  // red
        Color.FromArgb(255, 0xEC, 0x40, 0x7A),  // pink
        Color.FromArgb(255, 0xAB, 0x47, 0xBC),  // purple
        Color.FromArgb(255, 0x7E, 0x57, 0xC2),  // deep purple
        Color.FromArgb(255, 0x5C, 0x6B, 0xC0),  // indigo
        Color.FromArgb(255, 0x42, 0xA5, 0xF5),  // blue
        Color.FromArgb(255, 0x26, 0xA6, 0x9A),  // teal
        Color.FromArgb(255, 0x66, 0xBB, 0x6A),  // green
        Color.FromArgb(255, 0xFF, 0x70, 0x43),  // deep orange
        Color.FromArgb(255, 0x8D, 0x6E, 0x63),  // brown
    ];

    private static Color ColorFromEmail(string email)
    {
        var key = email.Trim().ToLowerInvariant();
        int hash = 0;
        foreach (var ch in key) hash = unchecked(hash * 31 + ch);
        var idx = (hash & 0x7FFFFFFF) % AvatarPalette.Length;
        return AvatarPalette[idx];
    }

    private static FrameworkElement? BuildStatusGlyph(string? status)
    {
        var (glyph, color) = status switch
        {
            "accepted"  => ("", Color.FromArgb(255, 0x2E, 0x7D, 0x32)),  // check, green
            "declined"  => ("", Color.FromArgb(255, 0xC6, 0x28, 0x28)),  // cross, red
            "tentative" => ("", Color.FromArgb(255, 0xEF, 0x6C, 0x00)),  // question mark, orange
            _           => (null, default(Color)),
        };
        if (glyph is null) return null;
        return new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 14,
            Foreground = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private void BuildRoomsBlock(StackPanel parent, List<EventAttendee> rooms)
    {
        parent.Children.Add(new TextBlock
        {
            Text = rooms.Count == 1 ? "Место" : $"Места ({rooms.Count})",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
        });
        foreach (var r in rooms)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = "", // MapPin glyph
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.8,
            });
            row.Children.Add(new TextBlock
            {
                Text = !string.IsNullOrWhiteSpace(r.DisplayName) ? r.DisplayName! : r.Email,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsTextSelectionEnabled = true,
            });
            parent.Children.Add(row);
        }
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
