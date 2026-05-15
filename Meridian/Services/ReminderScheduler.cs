using Meridian.Auth;
using Meridian.Diagnostics;
using Meridian.Models;
using Microsoft.UI.Dispatching;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Meridian.Services;

// Schedules Windows toast notifications for upcoming Calendar events via the
// platform's ScheduledToastNotification — survives app close (the WNP
// platform itself fires the toast at the requested time).
//
// Design choices:
//   * Popup reminders only. Email reminders are delivered by Google itself.
//   * All-day events are skipped: Google's "1 day before 09:00" default
//     would drown us in low-signal alerts on a typical calendar.
//   * Tag/Group derived from a stable per-event-instance key so a re-schedule
//     replaces an entry instead of duplicating it.
internal sealed class ReminderScheduler
{
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(48);
    private static readonly TimeSpan SkipPastMargin = TimeSpan.FromSeconds(5);
    private const string GroupPrefix = "mrd-";

    private readonly CalendarCache _events;
    private readonly DispatcherQueue _dispatcher;

    public ReminderScheduler(CalendarCache events, DispatcherQueue dispatcher)
    {
        _events = events;
        _dispatcher = dispatcher;
        _events.DataRefreshed += _ => Reschedule();
    }

    public void Reschedule() => _dispatcher.TryEnqueue(RescheduleCore);

    // Cancels every scheduled reminder belonging to this account. Called when
    // an account is removed so its reminders stop firing without disturbing
    // the others.
    public void DropAccount(AccountId account) =>
        _dispatcher.TryEnqueue(() => ClearScheduledGroup(GroupFor(account.Email)));

    private void RescheduleCore()
    {
        try
        {
            var now = DateTime.Now;
            var horizon = now + Horizon;
            var events = _events.SnapshotRange(now, horizon);

            var desiredByAccount =
                new Dictionary<string, List<(DateTime FireAt, CalendarEvent Event, int Minutes)>>();
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

                    if (!desiredByAccount.TryGetValue(e.AccountEmail, out var list))
                        desiredByAccount[e.AccountEmail] = list = [];
                    list.Add((fireAt, e, minutes));
                }
            }

            ToastNotifier notifier;
            try
            {
                notifier = ToastNotificationManager.CreateToastNotifier(ToastSetup.ResolvedAumid);
            }
            catch (Exception ex)
            {
                Log.Error("Toast", ex, "CreateToastNotifier");
                return;
            }

            int sched = 0, schedFail = 0;
            foreach (var (email, list) in desiredByAccount)
            {
                var group = GroupFor(email);
                ClearScheduledGroup(group, notifier);

                foreach (var (fireAt, ev, minutes) in list)
                {
                    try
                    {
                        var toast = BuildScheduledToast(ev, minutes, fireAt, group);
                        notifier.AddToSchedule(toast);
                        sched++;
                        Log.Write("Toast",
                            $"  + scheduled '{ev.Title}' fireAt={fireAt:yyyy-MM-dd HH:mm} (minus {minutes}min)");
                    }
                    catch (Exception ex)
                    {
                        schedFail++;
                        Log.Error("Toast", ex, $"AddToSchedule '{ev.Title}'");
                    }
                }
            }

            Log.Write("Toast",
                $"reschedule: events={total} allday={allDay} noRem={noRem} noEmail={noEmail} " +
                $"scheduled(ok={sched} fail={schedFail})");
        }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, "reschedule");
        }
    }

    private static ScheduledToastNotification BuildScheduledToast(
        CalendarEvent e, int minutes, DateTime fireAt, string group)
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

        return new ScheduledToastNotification(xml, new DateTimeOffset(fireAt))
        {
            Tag = TagFor(e, minutes),
            Group = group,
            ExpirationTime = new DateTimeOffset(e.Start.AddHours(1)),
        };
    }

    private static void ClearScheduledGroup(string group, ToastNotifier? notifier = null)
    {
        try
        {
            notifier ??= ToastNotificationManager.CreateToastNotifier(ToastSetup.ResolvedAumid);
            foreach (var st in notifier.GetScheduledToastNotifications())
            {
                if (st.Group == group)
                    notifier.RemoveFromSchedule(st);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, $"ClearScheduledGroup {group}");
        }
    }

    private static string GroupFor(string email) => GroupPrefix + Hash(email);

    private static string TagFor(CalendarEvent e, int minutes) =>
        Hash($"{e.Id}:{minutes}");

    // 32-bit FNV-1a, 8 hex chars. Tag and Group were historically capped at
    // 16 chars on early Win10 builds; an 8-char hash keeps headroom while
    // collisions are negligible at our scale.
    private static string Hash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var c in s)
            {
                h ^= c;
                h *= 16777619;
            }
            return h.ToString("x8");
        }
    }

    private static string Xml(string s) => System.Net.WebUtility.HtmlEncode(s);
}
