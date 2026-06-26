using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Meridian.Auth;
using Meridian.Diagnostics;
using Meridian.Models;
using Meridian.Services;
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

        SetupRsvp(ev);

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

    // ── RSVP (accept / decline / tentative) ─────────────────────────────────────

    // Guards against re-entrant clicks while a PATCH is in flight. The cache
    // updates optimistically so the toggle reflects the new state immediately;
    // we just don't want a second click racing the first.
    private bool _rsvpBusy;

    private void SetupRsvp(CalendarEvent ev)
    {
        // Feature unavailable (view model not wired) or the user can't respond
        // (not an attendee, or read-only calendar) → leave the row collapsed.
        if (EventActions.CanRespond is not { } canRespond
            || EventActions.Respond is null
            || !canRespond(ev))
            return;

        RsvpStack.Visibility = Visibility.Visible;
        ReflectRsvpState(EventActions.SelfAttendee(ev)?.ResponseStatus);
    }

    // Highlights the toggle matching the current responseStatus. "needsAction"
    // or anything unrecognized leaves all three unset.
    private void ReflectRsvpState(string? status)
    {
        RsvpYes.IsChecked   = status == EventActions.Accepted;
        RsvpNo.IsChecked    = status == EventActions.Declined;
        RsvpMaybe.IsChecked = status == EventActions.Tentative;
    }

    private void OnRsvpYesClick(object sender, RoutedEventArgs e) => _ = RespondAsync(EventActions.Accepted);
    private void OnRsvpNoClick(object sender, RoutedEventArgs e) => _ = RespondAsync(EventActions.Declined);
    private void OnRsvpMaybeClick(object sender, RoutedEventArgs e) => _ = RespondAsync(EventActions.Tentative);

    private async Task RespondAsync(string status)
    {
        if (_event is null || EventActions.Respond is not { } respond) return;
        if (_rsvpBusy) { ReflectRsvpState(EventActions.SelfAttendee(_event)?.ResponseStatus); return; }

        // Clicking the already-active choice is a no-op. Re-pin the toggles
        // (ToggleButton flips itself off on click) and skip the round-trip so
        // the buttons don't briefly gray out for nothing.
        var current = EventActions.SelfAttendee(_event)?.ResponseStatus;
        if (current == status) { ReflectRsvpState(current); return; }

        _rsvpBusy = true;
        RsvpError.Visibility = Visibility.Collapsed;
        // Pin the toggles to the chosen state immediately; the cache mutates the
        // model optimistically too, so this just keeps the UI consistent before
        // the await resumes (and avoids the toggle flicking off on click).
        ReflectRsvpState(status);
        SetRsvpEnabled(false);

        bool ok;
        try { ok = await respond(_event, status); }
        catch { ok = false; }

        SetRsvpEnabled(true);
        _rsvpBusy = false;

        if (!ok)
        {
            // The cache reverted its optimistic change; mirror that and show why.
            ReflectRsvpState(EventActions.SelfAttendee(_event)?.ResponseStatus);
            RsvpError.Visibility = Visibility.Visible;
        }
    }

    private void SetRsvpEnabled(bool enabled)
    {
        RsvpYes.IsEnabled = enabled;
        RsvpNo.IsEnabled = enabled;
        RsvpMaybe.IsEnabled = enabled;
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

    // At or below this count the guest list is drawn as full rows (avatar +
    // name + status). Above it we switch to a compact avatar strip.
    private const int GuestListMaxShown = 5;

    // Avatar-strip geometry. Avatars spread across the available width with a gap
    // when they fit; when they'd overflow they fan into an overlapping "stack"
    // (like a hand of cards), tightening only as much as needed. Past
    // GuestStripMax we render the first GuestStripMax and a trailing "+N" chip.
    private const int GuestStripMax = 16;
    private const double AvatarSize = 28;
    // Clear air between adjacent ring edges when there's room to spread. Counted
    // from ring edge to ring edge, so the per-avatar step adds the overhang.
    private const double AvatarGap = 4;
    private const double AvatarOverlap = 16;     // visible sliver when stacked
    private const double RingThickness = 2;      // colored outline around avatar

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
            parent.Children.Add(BuildGuestsAvatarStrip(ordered));
            return;
        }

        foreach (var a in ordered)
            parent.Children.Add(BuildAttendeeRow(a));
    }

    // Compact avatar strip for large guest lists. Up to GuestStripSpread avatars
    // sit side by side; beyond that they overlap into a stack. Past GuestStripMax
    // the overflow collapses into a trailing "+N" chip. Each avatar carries a
    // tooltip (name + RSVP status) and pops to the front of the z-order on hover.
    private FrameworkElement BuildGuestsAvatarStrip(IReadOnlyList<EventAttendee> guests)
    {
        var shown = guests.Count > GuestStripMax
            ? guests.Take(GuestStripMax).ToList()
            : guests;
        int overflow = guests.Count - shown.Count;

        var canvas = new Canvas
        {
            // Extra height for the ring that overhangs the bubble top and bottom.
            Height = AvatarSize + RingThickness * 2,
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // Background brush makes the whole strip hit-testable so pointer
            // moves between overlapping avatars stay responsive.
            Background = new SolidColorBrush(Colors.Transparent),
        };

        // The step between avatars depends on how much width we actually get,
        // which isn't known until layout. Lay out once the canvas has a real
        // width, and re-lay out if it changes (DPI, resize).
        void Layout()
        {
            double available = canvas.ActualWidth;
            if (available <= 0) return;
            LayoutAvatarStrip(canvas, shown, overflow, guests, available);
        }

        canvas.SizeChanged += (_, _) => Layout();
        canvas.Loaded += (_, _) => Layout();
        return canvas;
    }

    // Positions the avatars (and optional overflow chip) within the given width.
    // Avatars spread out with a gap when they fit; once they'd run past the edge
    // the step tightens into an overlap — only as much as needed — down to a
    // minimum sliver. This is what makes the stack "stretch" to fill the strip
    // instead of clumping at a fixed overlap.
    private void LayoutAvatarStrip(
        Canvas canvas,
        IReadOnlyList<EventAttendee> shown,
        int overflow,
        IReadOnlyList<EventAttendee> guests,
        double available)
    {
        canvas.Children.Clear();

        // Slots to place: the avatars plus, if present, the "+N" chip.
        int slots = shown.Count + (overflow > 0 ? 1 : 0);

        // Width the avatars occupy, leaving room for the ring overhang on both
        // ends. The last avatar contributes a full AvatarSize; the others only a
        // step. So: x = RingThickness + step*(slots-1) + AvatarSize + RingThickness.
        double usable = available - AvatarSize - RingThickness * 2;

        // Ideal step puts AvatarGap of clear air between ring edges: the bubble
        // plus the ring overhang on both this avatar's right and the next one's
        // left, plus the gap itself.
        double idealStep = AvatarSize + RingThickness * 2 + AvatarGap;  // no overlap
        double minStep = AvatarSize - AvatarOverlap;                    // tightest stack
        double step = slots <= 1 ? idealStep : usable / (slots - 1);
        step = Math.Clamp(step, minStep, idealStep);

        double x = RingThickness;
        for (int i = 0; i < shown.Count; i++)
        {
            var avatar = BuildStripAvatar(shown[i]);
            Canvas.SetLeft(avatar, x);
            Canvas.SetTop(avatar, RingThickness);
            // Leftmost on top so the stack fans like a hand of cards; hover
            // overrides this. Base z-indexes leave headroom above the count.
            Canvas.SetZIndex(avatar, shown.Count - i);
            canvas.Children.Add(avatar);
            x += step;
        }

        if (overflow > 0)
        {
            var more = BuildOverflowChip(overflow, guests.Skip(shown.Count));
            Canvas.SetLeft(more, x);
            Canvas.SetTop(more, RingThickness);
            Canvas.SetZIndex(more, 0);
            canvas.Children.Add(more);
        }
    }

    // A single avatar in the compact strip: the colored/photo bubble plus a
    // small status dot, a hover tooltip, and pointer handlers that raise it
    // above its neighbours while the cursor is over it.
    private FrameworkElement BuildStripAvatar(EventAttendee a)
    {
        var avatar = BuildAvatar(a, out var photoTarget);
        ResolveDirectoryPhoto(a, photoTarget);

        // Wrap so we can overlay a colored ring and the status dot without
        // disturbing the avatar's own 28×28 layout. The host is sized to the
        // bubble; the ring is drawn just outside it via a negative margin so it
        // doesn't eat into the face.
        var host = new Grid { Width = AvatarSize, Height = AvatarSize };

        // Colored ring around the avatar — a per-person hue (same hash as the
        // bubble fill) so each face gets its own outline, and stacked avatars
        // stay visually separated where they overlap.
        host.Children.Add(new Ellipse
        {
            Width = AvatarSize + RingThickness * 2,
            Height = AvatarSize + RingThickness * 2,
            Margin = new Thickness(-RingThickness),
            Stroke = new SolidColorBrush(ColorFromEmail(a.Email)),
            StrokeThickness = RingThickness,
            Fill = new SolidColorBrush(Colors.Transparent),
        });

        host.Children.Add(avatar);

        if (BuildStatusDot(a.ResponseStatus) is { } dot)
            host.Children.Add(dot);

        var tip = new ToolTip { Content = BuildAvatarTooltipText(a) };
        ToolTipService.SetToolTip(host, tip);
        // Patch the tooltip with the directory-resolved name once it arrives.
        ResolveDirectoryTooltip(a, tip);

        host.PointerEntered += (_, _) =>
        {
            _stripHoverPrev = Canvas.GetZIndex(host);
            Canvas.SetZIndex(host, 1000);
        };
        host.PointerExited += (_, _) => Canvas.SetZIndex(host, _stripHoverPrev);

        return host;
    }

    private int _stripHoverPrev;

    private FrameworkElement BuildOverflowChip(int count, IEnumerable<EventAttendee> rest)
    {
        var grid = new Grid { Width = AvatarSize, Height = AvatarSize };
        grid.Children.Add(new Ellipse
        {
            Width = AvatarSize,
            Height = AvatarSize,
            Fill = (Brush)Application.Current.Resources["SystemControlBackgroundBaseLowBrush"],
        });
        grid.Children.Add(new TextBlock
        {
            Text = $"+{count}",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var names = rest
            .Select(FormatAttendeeName)
            .Take(20)
            .ToList();
        var content = string.Join("\n", names);
        if (count > names.Count) content += $"\n… и ещё {count - names.Count}";
        ToolTipService.SetToolTip(grid, new ToolTip { Content = content });

        return grid;
    }

    // Small colored dot in the avatar's bottom-right corner reflecting the RSVP
    // response. "Needs action" gets no dot to keep undecided guests quiet.
    private static FrameworkElement? BuildStatusDot(string? status)
    {
        var color = status switch
        {
            "accepted"  => Color.FromArgb(255, 0x2E, 0x7D, 0x32),  // green
            "declined"  => Color.FromArgb(255, 0xC6, 0x28, 0x28),  // red
            "tentative" => Color.FromArgb(255, 0xEF, 0x6C, 0x00),  // orange
            _           => (Color?)null,
        };
        if (color is not { } c) return null;

        const double dot = 10;
        return new Border
        {
            Width = dot,
            Height = dot,
            CornerRadius = new CornerRadius(dot / 2),
            Background = new SolidColorBrush(c),
            // White ring so the dot reads against any avatar color.
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
    }

    private static string BuildAvatarTooltipText(EventAttendee a)
    {
        var name = FormatAttendeeName(a);
        var status = StatusWord(a.ResponseStatus);
        var extras = new List<string>();
        if (a.IsOrganizer) extras.Add("организатор");
        if (status is { Length: > 0 }) extras.Add(status);
        return extras.Count > 0 ? $"{name} — {string.Join(" · ", extras)}" : name;
    }

    private static string StatusWord(string? status) => status switch
    {
        "accepted"  => "да",
        "declined"  => "нет",
        "tentative" => "возможно",
        "needsAction" => "не ответил(а)",
        _ => "",
    };

    // Patches an avatar's tooltip with the directory-resolved display name once
    // it's available, mirroring ResolveDirectoryName for the full rows. Cache
    // hits apply synchronously; misses resolve in the background.
    private void ResolveDirectoryTooltip(EventAttendee a, ToolTip tip)
    {
        if (_event?.AccountEmail is not { Length: > 0 } accountEmail) return;
        if (string.IsNullOrWhiteSpace(a.Email)) return;

        var account = new AccountId(GoogleOAuthClient.ProviderName, accountEmail);

        if (DirectoryCache.TryGet(account, a.Email, out var cached))
        {
            ApplyTooltipName(a, tip, cached.DisplayName);
            return;
        }

        _ = ResolveTooltipInBackgroundAsync(account, a, tip);
    }

    private async Task ResolveTooltipInBackgroundAsync(AccountId account, EventAttendee a, ToolTip tip)
    {
        DirectoryPerson? person;
        try { person = await DirectoryCache.ResolveAsync(account, a.Email); }
        catch (AccountAuthExpiredException) { return; }
        catch { return; }

        if (person?.DisplayName is not { Length: > 0 } name) return;

        DispatcherQueue.TryEnqueue(() => ApplyTooltipName(a, tip, name));
    }

    private static void ApplyTooltipName(EventAttendee a, ToolTip tip, string? resolved)
    {
        if (string.IsNullOrWhiteSpace(resolved)) return;

        var status = StatusWord(a.ResponseStatus);
        var extras = new List<string>();
        if (a.IsOrganizer) extras.Add("организатор");
        if (status is { Length: > 0 }) extras.Add(status);

        var label = DecorateName(a, resolved!);
        tip.Content = extras.Count > 0 ? $"{label} — {string.Join(" · ", extras)}" : label;
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

        var avatar = BuildAvatar(a, out var photoTarget);
        Grid.SetColumn(avatar, 0);
        grid.Children.Add(avatar);

        // Try to paint the directory profile photo over the initial bubble.
        // Cache hit or download — resolves in the background; the colored
        // initial stays as the placeholder/fallback if there's no photo.
        ResolveDirectoryPhoto(a, photoTarget);

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

        // Pull a human name from the org directory for this attendee. Uses the
        // cache for instant hits; otherwise resolves in the background and
        // updates this row's name when it arrives. Photo data lands in the
        // cache too (rendered in a later stage).
        ResolveDirectoryName(a, name);

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
        return DecorateName(a, label);
    }

    // Applies the per-attendee decoration (currently the "(вы)" marker for the
    // signed-in user) to a bare label. Single source of truth shared by the
    // initial-paint formatter and the directory-resolved name patch.
    private static string DecorateName(EventAttendee a, string label) =>
        a.IsSelf ? $"{label} (вы)" : label;

    private static string DeriveNameFromEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }

    // Resolves the attendee's display name from the org directory and updates
    // the given TextBlock in place. Cache hits apply synchronously; misses
    // resolve in the background and patch the row when (and if) a name comes
    // back. Anything missing — no account, personal Gmail (no directory),
    // external guest, network hiccup — silently leaves the existing label.
    private void ResolveDirectoryName(EventAttendee a, TextBlock nameBlock)
    {
        if (_event?.AccountEmail is not { Length: > 0 } accountEmail) return;
        if (string.IsNullOrWhiteSpace(a.Email)) return;

        var account = new AccountId(GoogleOAuthClient.ProviderName, accountEmail);

        // Instant path: a fresh positive cache entry.
        if (DirectoryCache.TryGet(account, a.Email, out var cached))
        {
            ApplyResolvedName(a, nameBlock, cached.DisplayName);
            return;
        }

        // Background path: resolve, then patch on the UI thread. Fire-and-forget
        // by design — the row is already showing a usable fallback label.
        _ = ResolveInBackgroundAsync(account, a, nameBlock);
    }

    private async Task ResolveInBackgroundAsync(AccountId account, EventAttendee a, TextBlock nameBlock)
    {
        DirectoryPerson? person;
        try { person = await DirectoryCache.ResolveAsync(account, a.Email); }
        catch (AccountAuthExpiredException) { return; } // re-auth surfaced by sync paths
        catch { return; }

        if (person?.DisplayName is not { Length: > 0 } name) return;

        DispatcherQueue.TryEnqueue(() => ApplyResolvedName(a, nameBlock, name));
    }

    // Writes a directory-resolved name onto the row, only if it actually adds
    // information — never overwrite an existing label with a blank, and keep the
    // "(вы)" suffix for the signed-in user.
    private static void ApplyResolvedName(EventAttendee a, TextBlock nameBlock, string? resolved)
    {
        if (string.IsNullOrWhiteSpace(resolved)) return;
        nameBlock.Text = DecorateName(a, resolved);
    }

    // Renders the colored circle with the attendee's initial. Color is a
    // deterministic hash of the email so the same person looks the same
    // across re-renders and across events. The returned photoTarget is a
    // transparent overlay ellipse sized to the bubble; ResolveDirectoryPhoto
    // fills it with an ImageBrush once (and if) a profile photo loads, hiding
    // the initial underneath. No photo → it stays transparent and the initial
    // shows through.
    private static FrameworkElement BuildAvatar(EventAttendee a, out Ellipse photoTarget)
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
        photoTarget = new Ellipse { Width = 28, Height = 28 };
        grid.Children.Add(photoTarget);
        return grid;
    }

    // Loads the attendee's directory profile photo (from the per-URL disk cache
    // or a one-time download) and paints it onto the avatar's overlay ellipse.
    // Fire-and-forget: the row already shows the colored initial, so any miss —
    // no account, no photo, network error — silently leaves that in place.
    // Decoded profile photos, keyed by photo URL, kept alive for the process so
    // re-opening a flyout doesn't re-read the file and re-decode the JPEG every
    // time. BitmapImage is a UI-thread object — this cache is only ever touched
    // on the UI thread (ResolveDirectoryPhoto runs during row build; ApplyPhoto
    // runs via DispatcherQueue), so a plain Dictionary is safe without a lock.
    private static readonly Dictionary<string, Microsoft.UI.Xaml.Media.Imaging.BitmapImage> _photoBitmaps = [];

    private void ResolveDirectoryPhoto(EventAttendee a, Ellipse target)
    {
        if (_event?.AccountEmail is not { Length: > 0 } accountEmail) return;
        if (string.IsNullOrWhiteSpace(a.Email)) return;

        var account = new AccountId(GoogleOAuthClient.ProviderName, accountEmail);

        // Fully-warm path: name+URL already resolved and the bitmap already
        // decoded — paint synchronously during build, no flicker, no Task.
        if (DirectoryCache.TryGet(account, a.Email, out var cached)
            && cached.PhotoUrl is { Length: > 0 } url
            && _photoBitmaps.TryGetValue(url, out var bmp))
        {
            PaintPhoto(target, bmp);
            return;
        }

        _ = LoadPhotoAsync(account, a.Email, target);
    }

    private async Task LoadPhotoAsync(AccountId account, string email, Ellipse target)
    {
        // The photo URL is only known once the person is resolved. ResolveAsync
        // returns the cached entry instantly on a hit, or the shared in-flight
        // resolve otherwise — coalesced with the name path on this same row, so
        // both get the same result rather than racing.
        DirectoryPerson? person;
        try { person = await DirectoryCache.ResolveAsync(account, email); }
        catch { return; }

        var url = person?.PhotoUrl;
        if (string.IsNullOrWhiteSpace(url)) return;

        byte[]? bytes;
        try { bytes = await DirectoryPhotoCache.GetAsync(url); }
        catch { return; }
        if (bytes is not { Length: > 0 }) return;

        DispatcherQueue.TryEnqueue(() => ApplyPhoto(target, url, bytes));
    }

    // Decodes the photo bytes into a BitmapImage, caches it by URL, and paints
    // it onto the overlay ellipse. Runs on the UI thread (image decode and the
    // bitmap cache both require it). Any decode hiccup leaves the initial bubble.
    private static async void ApplyPhoto(Ellipse target, string url, byte[] bytes)
    {
        try
        {
            // Another row for the same person may have decoded it first.
            if (!_photoBitmaps.TryGetValue(url, out var bitmap))
            {
                bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                using var stream = new MemoryStream(bytes);
                await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                _photoBitmaps[url] = bitmap;
            }
            PaintPhoto(target, bitmap);
        }
        catch (Exception ex)
        {
            Log.Error("Directory", ex, "photo decode failed");
        }
    }

    private static void PaintPhoto(Ellipse target, Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmap) =>
        target.Fill = new ImageBrush
        {
            ImageSource = bitmap,
            Stretch = Stretch.UniformToFill,
        };

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
