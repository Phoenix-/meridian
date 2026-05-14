using Meridian.Auth;
using Meridian.Diagnostics;
using Meridian.Models;
using Microsoft.UI.Dispatching;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Meridian.Services;

// Fires Windows toast notifications for upcoming Calendar events.
//
// We deliberately do NOT use ScheduledToastNotification: on Win11 26200 with
// our unpackaged registration, AddToSchedule silently drops toasts (the WNP
// platform receives the request but never dispatches it, with no error). The
// immediate Show() path works, so the scheduler keeps in-process timers
// (Task.Delay + Show()) and lives entirely inside the app's lifetime.
//
// Consequence: the app must be running for reminders to fire. Acceptable —
// Meridian needs to be running anyway to sync data and surface its UI, and
// we re-arm everything on startup via the cache's DataRefreshed event.
//
// Design choices:
//   * Popup reminders only. Email reminders are delivered by Google itself.
//   * All-day events are skipped: Google's "1 day before 09:00" default would
//     drown us in low-signal alerts on a typical calendar.
//   * Per-event timers are tracked by (account, eventId, minutes) so we can
//     cancel and reschedule on each refresh without firing duplicates.
internal sealed class ReminderScheduler
{
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(48);
    private static readonly TimeSpan SkipPastMargin = TimeSpan.FromSeconds(5);

    private readonly CalendarCache _events;
    private readonly DispatcherQueue _dispatcher;

    // Active timers keyed by a deterministic event-instance id so a refresh
    // that re-encounters the same reminder is a no-op rather than a duplicate.
    private readonly Dictionary<string, CancellationTokenSource> _pending = [];
    private readonly Lock _gate = new();

    public ReminderScheduler(CalendarCache events, DispatcherQueue dispatcher)
    {
        _events = events;
        _dispatcher = dispatcher;
        _events.DataRefreshed += _ => Reschedule();
    }

    public void Reschedule() => _dispatcher.TryEnqueue(RescheduleCore);

    // Cancels every reminder belonging to this account. Called when an account
    // is removed so its reminders stop firing without disturbing the others.
    public void DropAccount(AccountId account)
    {
        var prefix = account.Email + "|";
        lock (_gate)
        {
            foreach (var key in _pending.Keys.ToList())
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                _pending[key].Cancel();
                _pending.Remove(key);
            }
        }
    }

    private void RescheduleCore()
    {
        try
        {
            var now = DateTime.Now;
            var horizon = now + Horizon;
            var events = _events.SnapshotRange(now, horizon);

            // Collect the desired (key, fireAt, event, minutes) set for this tick.
            var desired = new Dictionary<string, (DateTime FireAt, CalendarEvent Event, int Minutes)>();
            int total = events.Count, allDay = 0, noRem = 0, noEmail = 0;

            foreach (var e in events)
            {
                if (e.IsAllDay) { allDay++; continue; }
                if (e.ReminderMinutes is not { Count: > 0 }) { noRem++; continue; }
                if (string.IsNullOrEmpty(e.AccountEmail)) { noEmail++; continue; }

                foreach (var minutes in e.ReminderMinutes)
                {
                    var fireAt = e.Start.AddMinutes(-minutes);
                    if (fireAt <= now + SkipPastMargin) continue;
                    if (fireAt > horizon) continue;
                    desired[KeyFor(e, minutes)] = (fireAt, e, minutes);
                }
            }

            int added = 0, kept = 0, cancelled = 0;
            lock (_gate)
            {
                // Cancel timers whose reminder is no longer desired (event
                // deleted, time shifted, etc.).
                foreach (var key in _pending.Keys.ToList())
                {
                    if (desired.ContainsKey(key)) continue;
                    _pending[key].Cancel();
                    _pending.Remove(key);
                    cancelled++;
                }

                // Arm timers for newly-desired reminders.
                foreach (var (key, value) in desired)
                {
                    if (_pending.ContainsKey(key)) { kept++; continue; }
                    var cts = new CancellationTokenSource();
                    _pending[key] = cts;
                    _ = ArmAsync(key, value.FireAt, value.Event, value.Minutes, cts.Token);
                    added++;
                }
            }

            Log.Write("Toast",
                $"reschedule: events={total} allday={allDay} noRem={noRem} noEmail={noEmail} " +
                $"added={added} kept={kept} cancelled={cancelled}");
        }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, "reschedule");
        }
    }

    private async Task ArmAsync(string key, DateTime fireAt, CalendarEvent e, int minutes, CancellationToken ct)
    {
        try
        {
            var delay = fireAt - DateTime.Now;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);

            if (ct.IsCancellationRequested) return;

            _dispatcher.TryEnqueue(() => FireToast(e, minutes));
        }
        catch (OperationCanceledException) { /* expected on reschedule */ }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, $"ArmAsync '{e.Title}'");
        }
        finally
        {
            lock (_gate)
            {
                // Only remove if still our timer — a re-schedule may have
                // replaced it with a fresh CTS.
                if (_pending.TryGetValue(key, out var current) && current.Token == ct)
                    _pending.Remove(key);
            }
        }
    }

    private static void FireToast(CalendarEvent e, int minutes)
    {
        try
        {
            var when = e.Start.ToString("HH:mm");
            var leadIn = minutes <= 0
                ? "сейчас"
                : minutes < 60
                    ? $"через {minutes} мин"
                    : minutes == 60
                        ? "через час"
                        : $"в {when}";

            var title = Xml(e.Title);
            var body  = Xml($"{leadIn} · {when}");
            // launch=date=YYYY-MM-DD is parsed by MeridianToastActivator on
            // click; MainWindow then navigates the active view to that day.
            var launch = Xml($"date={e.Start:yyyy-MM-dd}");

            var xml = new XmlDocument();
            xml.LoadXml(
                $"""
                <toast launch="{launch}">
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{title}</text>
                      <text>{body}</text>
                    </binding>
                  </visual>
                </toast>
                """);

            var notifier = ToastNotificationManager.CreateToastNotifier(ToastSetup.ResolvedAumid);
            notifier.Show(new ToastNotification(xml));
            Log.Write("Toast", $"fired: '{e.Title}' at {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, $"FireToast '{e.Title}'");
        }
    }

    // Stable per-reminder key. Combines account, event id, and minutes so a
    // single event with multiple reminders gets distinct timers.
    private static string KeyFor(CalendarEvent e, int minutes) =>
        $"{e.AccountEmail}|{e.Id}|{minutes}";

    private static string Xml(string s) => System.Net.WebUtility.HtmlEncode(s);
}
