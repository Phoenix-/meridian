using Meridian.Auth;
using Meridian.Diagnostics;
using Meridian.Models;
using Meridian.Views;
using Microsoft.UI.Dispatching;
using Windows.UI;

namespace Meridian.Services;

// Drives the taskbar overlay icon: while a timed (non-all-day) event is in
// progress (Start ≤ now < End), paints a small dot in its calendar colour on
// the taskbar button. Snaps off when the event ends.
//
// Independent from ReminderScheduler: that one fires *at* reminder time, this
// one signals "you are in a meeting now". The two together give a complete
// taskbar story — amber flash at the reminder, coloured dot during the event.
//
// Multi-event policy: when several timed events overlap right now, the one
// ending soonest wins. That matches the priority Reminder uses for "most
// urgent next thing" and stays stable across reconcile passes (End time
// doesn't drift the way Start does once an event is underway).
//
// Update cadence: a 30-second DispatcherQueueTimer is enough — we never need
// sub-minute precision for "is the meeting still on", and the cache's
// DataRefreshed event covers the case where an event was added, moved, or
// cancelled between ticks.
internal sealed class OngoingEventIndicator
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly CalendarCache _events;
    private readonly AccountManager _accounts;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    // Tracked so we only call SetCircle when the colour actually changes —
    // taskbar overlay updates are cheap but not free, and the icon flickers
    // briefly if reapplied at high frequency.
    private Color? _currentColor;
    // Per-account colour assignment, identical to the one DayView/WeekView
    // build on the fly — cached here so the overlay colour matches what the
    // user sees in the grid when an event has no per-calendar colour.
    private readonly Dictionary<string, int> _accountIndex = new(StringComparer.Ordinal);

    public OngoingEventIndicator(CalendarCache events, AccountManager accounts, DispatcherQueue dispatcher)
    {
        _events = events;
        _accounts = accounts;
        _dispatcher = dispatcher;

        _events.DataRefreshed += _ => Refresh();

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TickInterval;
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Apply();
        _timer.Start();

        // First pass — paint immediately if there's already a meeting running
        // when the app starts (e.g. opened mid-call after a crash).
        Refresh();
    }

    public void Refresh() => _dispatcher.TryEnqueue(Apply);

    private void Apply()
    {
        // Const-typed gate (see AppSettings.FlashTaskbarOnReminder for the
        // same pattern) — suppress the unreachable-code warning locally.
#pragma warning disable CS0162
        if (!AppSettings.ShowOngoingEventOverlay)
        {
            SetColor(null);
            return;
        }
#pragma warning restore CS0162

        try
        {
            var now = DateTime.Now;
            // Look back 1 day to catch a long event whose Start is yesterday
            // but End is still in the future. SnapshotRange filters by event
            // *window* overlap, so this is enough — no need to enumerate
            // every loaded year.
            var events = _events.SnapshotRange(now.AddDays(-1), now.AddMinutes(1));

            CalendarEvent? best = null;
            foreach (var e in events)
            {
                if (e.IsAllDay) continue;
                if (e.Start > now || e.End <= now) continue;
                if (best is null || e.End < best.End) best = e;
            }

            if (best is null)
            {
                SetColor(null);
                return;
            }

            var color = EventColorPicker.Pick(best, BuildAccountIndex());
            SetColor(color);
        }
        catch (Exception ex)
        {
            Log.Error("Overlay", ex, "Apply");
        }
    }

    private void SetColor(Color? color)
    {
        if (ColorEquals(_currentColor, color)) return;
        _currentColor = color;
        TaskbarOverlay.SetCircle(color);
    }

    // Rebuilds the email→palette-index map every tick. Cheap (one pass over
    // the account list) and lets a freshly-added account get its colour
    // immediately rather than after a restart.
    private Dictionary<string, int> BuildAccountIndex()
    {
        _accountIndex.Clear();
        int i = 0;
        foreach (var acc in _accounts.Accounts)
            _accountIndex[acc.Email] = i++;
        return _accountIndex;
    }

    private static bool ColorEquals(Color? a, Color? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.Value.A == b.Value.A && a.Value.R == b.Value.R
            && a.Value.G == b.Value.G && a.Value.B == b.Value.B;
    }
}
