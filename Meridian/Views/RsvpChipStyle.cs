using Meridian.Models;
using Meridian.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace Meridian.Views;

// Shared rendering of the signed-in user's RSVP status onto an event chip /
// block. Both MonthEventChip (month + all-day rows) and WeekEventBlock (timed
// week/day blocks) feed their Root border + title/time text here so the visual
// language is identical across every view.
//
// Status → look (only when the user is actually an attendee, i.e. it's an
// invitation):
//   accepted / null  → normal filled chip (unchanged)
//   tentative        → muted fill (lower alpha)
//   declined         → muted fill + strikethrough title
//   needsAction      → outlined (transparent fill) + gently pulsing border;
//                      text is themed (the cell background shows through, so
//                      contrast must follow the app theme, not the event color)
internal static class RsvpChipStyle
{
    // Master switch for the needsAction border pulse. Parked as a const per the
    // settings-store plan until enough toggles accumulate to justify real UI.
    private const bool EnablePulse = true;

    private const double MutedAlpha = 0.40;   // declined / tentative fill alpha

    // The status string we treat as "you haven't responded yet". Google sends
    // "needsAction"; anything unrecognized (but non-empty) is treated the same.
    public const string NeedsAction = "needsAction";

    // One shared brush + one infinite storyboard, animated on the UI thread.
    // Every needsAction chip points its BorderBrush at this single instance, so
    // there is exactly ONE running animation no matter how many chips show it —
    // they all breathe in sync. Created lazily on first use.
    private static SolidColorBrush? _pulseBrush;
    private static Storyboard? _pulseStoryboard;

    // Returns the chip's response status if (and only if) the user is an
    // invitee — otherwise null. Tasks and events where the user isn't an
    // attendee get no RSVP styling. Call this when building EventChipData.
    public static string? StatusFor(CalendarEvent ev)
        => EventActions.SelfAttendee(ev)?.ResponseStatus;

    // Maps a status to whether it's the "not yet answered" state.
    public static bool IsNeedsAction(string? status)
        => !string.IsNullOrEmpty(status)
           && status != EventActions.Accepted
           && status != EventActions.Declined
           && status != EventActions.Tentative;

    // Applies the RSVP look to a chip's Root border and its text blocks.
    // `baseColor` is the event's calendar color; `fg` is the already-chosen
    // foreground for the filled state. Resets every property it might have
    // touched, because chips are pooled and reused across repaints.
    public static void Apply(
        string? status,
        Border root,
        Color baseColor,
        Color fg,
        params TextBlock[] textBlocks)
    {
        // ── reset to the default filled look ──────────────────────────────
        root.Background = new SolidColorBrush(baseColor);
        root.BorderBrush = null;
        root.BorderThickness = new Thickness(0);
        var fgBrush = new SolidColorBrush(fg);
        foreach (var tb in textBlocks)
        {
            tb.Foreground = fgBrush;
            tb.TextDecorations = Windows.UI.Text.TextDecorations.None;
            tb.Opacity = 1.0;
        }

        if (status is null || status == EventActions.Accepted)
            return;

        if (status == EventActions.Tentative)
        {
            root.Background = new SolidColorBrush(WithAlpha(baseColor, MutedAlpha));
            return;
        }

        if (status == EventActions.Declined)
        {
            root.Background = new SolidColorBrush(WithAlpha(baseColor, MutedAlpha));
            foreach (var tb in textBlocks)
                tb.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            return;
        }

        // needsAction (or any unrecognized status): outline, no fill, themed
        // text so it reads on both light and dark cell backgrounds. The border
        // is the shared pulsing accent brush (one animation for all such chips)
        // or a static accent solid when the pulse is disabled.
        root.Background = new SolidColorBrush(Colors.Transparent);
        root.BorderThickness = new Thickness(1.5);
        root.BorderBrush = EnablePulse
            ? PulseBrush()
            : new SolidColorBrush(Theme.AppColors.Accent);

        var themed = (SolidColorBrush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
        foreach (var tb in textBlocks)
            tb.Foreground = themed;
    }

    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

    // Lazily builds the one shared pulsing brush + its single infinite
    // storyboard. Every needsAction chip points its BorderBrush at this same
    // instance, so they all breathe in sync off ONE animation — animating
    // Opacity (cheap, UI-thread) rather than 30-50 separate color storyboards.
    private static SolidColorBrush PulseBrush()
    {
        if (_pulseBrush is null)
        {
            _pulseBrush = new SolidColorBrush(Theme.AppColors.Accent);

            var anim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.35,
                Duration = new Duration(System.TimeSpan.FromSeconds(1.8)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(anim, _pulseBrush);
            Storyboard.SetTargetProperty(anim, "Opacity");

            _pulseStoryboard = new Storyboard();
            _pulseStoryboard.Children.Add(anim);
            _pulseStoryboard.Begin();
        }
        return _pulseBrush;
    }
}
