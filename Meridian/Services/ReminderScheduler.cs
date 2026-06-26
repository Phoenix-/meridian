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
// Strategy: reconcile, not teardown-and-rebuild. Every pass compares "desired"
// (what events in the cache say we should have scheduled) against "actual"
// (what WNP currently holds in its scheduled queue), and only removes the
// extras and adds the missing. This costs the same as a full rebuild on the
// first pass but on subsequent passes it touches almost nothing — and, more
// importantly, it self-heals from drift: a toast that WNP silently dropped
// (storage wipe, account-data cleanup, OS update) gets re-added on the next
// pass instead of staying missing forever.
//
// Triggers:
//   * DataRefreshed from the cache — events changed, definitely need to look.
//   * A 5-minute timer — periodic drift check; cheap (one local WNP call).
//   * An initial Reschedule() after the host wires us up.
//
// Design choices:
//   * Popup reminders only. Email reminders are delivered by Google itself.
//   * All-day events are skipped: Google's "1 day before 09:00" default
//     would drown us in low-signal alerts on a typical calendar.
//   * The reconcile key (Tag) hashes event id + minutes + start + title — so
//     a moved or renamed event invalidates its old entry and re-schedules.
internal sealed class ReminderScheduler
{
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(48);
    // How long after fireAt we still consider an entry "desired". Set to the
    // toast ExpirationTime window (Start + 1h) so reconcile doesn't remove an
    // entry that WNP is about to deliver. Without this, a reconcile that
    // happens between fireAt-5s and fireAt would race the WNP firing and
    // sometimes win — silently swallowing the toast.
    private static readonly TimeSpan PastGrace = TimeSpan.FromHours(1);
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMinutes(5);
    // How far into the past we still surface a missed reminder. Wider than
    // PastGrace because the missed-reminder flow is for "laptop was closed
    // when this event fired" — useful for a few days, irrelevant after a week.
    private static readonly TimeSpan MissedWindow = TimeSpan.FromDays(7);
    // Only surface a missed reminder while the underlying event itself is
    // still "today" in the user's local calendar. Past that, the event has
    // come and gone — a catch-up toast is just noise. Bounded by event Start
    // (not fireAt) so a "-10min" reminder doesn't get a longer grace than a
    // "-0min" one for the same event.
    //
    // "Today" rolls over at 04:00 local, not midnight: an event at 23:55
    // missed at 00:01 should still surface. The window runs from the most
    // recent 04:00 boundary up to the next one.
    //
    // Example of the bug this prevents: a fresh incremental sync after a
    // long offline period drops a batch of last-week events into the cache;
    // none of them were ever MarkScheduled by this process, so they sail
    // past the WasScheduled filter and get aggregated into a single
    // "missed N reminders" toast — surfacing a week of irrelevant history
    // moments after a normal upcoming-event toast just fired.
    private static readonly TimeSpan DayBoundary = TimeSpan.FromHours(4);

    private static bool IsMissedFreshEnough(DateTime eventStart, DateTime now)
    {
        var todayStart = now.TimeOfDay >= DayBoundary
            ? now.Date + DayBoundary
            : now.Date.AddDays(-1) + DayBoundary;
        return eventStart >= todayStart;
    }
    // Maximum number of individual entries listed inside the summary toast
    // body. The full count still shows in the title.
    private const int MissedPreviewCount = 3;
    private const string GroupPrefix = "mrd-";
    private const string MissedGroup = "mrd-missed";

    private readonly CalendarCache _events;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private readonly DispatcherQueueTimer _flashTimer;
    private readonly MissedRemindersTracker _missed = new();
    // Last fireAt the flash timer was armed for. Recomputing on every
    // RescheduleCore pass and re-arming unconditionally would risk dropping
    // a flash if a pass briefly sees no desired entries; tracking it lets us
    // skip work when the next fireAt hasn't changed.
    private DateTime? _flashArmedFor;

    public ReminderScheduler(CalendarCache events, DispatcherQueue dispatcher)
    {
        _events = events;
        _dispatcher = dispatcher;
        _events.DataRefreshed += _ => Reschedule();

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = ReconcileInterval;
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => RescheduleCore();
        _timer.Start();

        // Single-shot timer; Interval is reset by ArmFlashTimer before each
        // Start(). We cannot rely on the toast firing to tell us anything —
        // WNP delivers it without notifying the process — so we mirror the
        // schedule locally and fire the taskbar flash on our own clock.
        _flashTimer = _dispatcher.CreateTimer();
        _flashTimer.IsRepeating = false;
        _flashTimer.Tick += (_, _) => OnFlashTimerFired();
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
            // Pull in past events too — the missed-reminder pass below needs
            // them. SnapshotRange uses inclusive-start, exclusive-end semantics
            // on event windows, so include anything that started in the last
            // MissedWindow.
            var events = _events.SnapshotRange(now - MissedWindow, horizon);

            // Nothing to schedule when popups are muted or we've de-registered
            // from the notification platform: in the muted case the user wants
            // silence; in the unregistered case WNP would silently drop every
            // Show()/AddToSchedule() against our now-unregistered AUMID — and,
            // worse, the missed-summary path would still call _missed.MarkShown()
            // for toasts that were never actually delivered, so they'd never
            // resurface once we re-register. Drain the queue, stop the flash
            // (it fires the taskbar flash independently of the toast, so a flash
            // already armed for a future fireAt would otherwise still go off),
            // and bail before the Show()/AddToSchedule()/MarkShown work below.
            if (AppSettings.SuppressAllPopups || !AppSettings.RegisterForNotifications)
            {
                ToastSetup.ClearAllScheduled();
                if (_flashArmedFor is not null)
                {
                    _flashTimer.Stop();
                    _flashArmedFor = null;
                }
                return;
            }

            // ── Read actual set from WNP (needed before classifying past
            //    fire-times: "WNP has it queued" → leave alone; "WNP doesn't"
            //    → it's missed). ──────────────────────────────────────────────
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

            var actual = new Dictionary<string, ScheduledToastNotification>(StringComparer.Ordinal);
            try
            {
                foreach (var st in notifier.GetScheduledToastNotifications())
                {
                    if (st.Group is null || !st.Group.StartsWith(GroupPrefix, StringComparison.Ordinal)) continue;
                    actual[st.Tag ?? ""] = st;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Toast", ex, "GetScheduledToastNotifications");
                return;
            }

            // ── Compute desired and missed sets ──────────────────────────────
            var desired = new Dictionary<string, DesiredEntry>(StringComparer.Ordinal);
            // Dedupe missed entries by event (not by reminder): a single event
            // with reminders at -10/-0 produces one catch-up toast, not two.
            // Pick the earliest-fireAt past reminder as the representative —
            // it carries the most signal ("you should have known 10 min ahead,
            // not just at start"). Per-event dedupe key matches the missed
            // tracker's storage key (event-stable, no minutes).
            var missedByEvent = new Dictionary<string, MissedEntry>(StringComparer.Ordinal);
            // Event-keys we've decided not to surface (too stale) but want to
            // silently mark shown so subsequent passes don't keep re-checking
            // them — and so they don't bundle with a future, fresh missed
            // reminder. Disjoint from missedByEvent by construction.
            var staleMissedKeys = new List<string>();
            int total = events.Count, allDay = 0, noRem = 0, noEmail = 0;

            foreach (var e in events)
            {
                if (e.IsAllDay) { allDay++; continue; }
                if (e.ReminderMinutes is not { Count: > 0 }) { noRem++; continue; }
                if (string.IsNullOrEmpty(e.AccountEmail)) { noEmail++; continue; }

                var eventKey = EventKey(e);

                foreach (var minutes in e.ReminderMinutes)
                {
                    var fireAt = e.Start.AddMinutes(-minutes);
                    if (fireAt > horizon) continue;

                    var tag = TagFor(e, minutes);

                    if (fireAt > now)
                    {
                        // Future fireAt — normal scheduling path. A future
                        // reminder for an event whose earlier reminder is
                        // already missed is unrelated: WNP will still deliver
                        // it on time.
                        desired[tag] = new DesiredEntry(e, minutes, fireAt, GroupFor(e.AccountEmail));
                        continue;
                    }

                    // Past fireAt.
                    if (actual.ContainsKey(tag) && fireAt > now - PastGrace)
                    {
                        // WNP has the toast queued and we're inside the grace
                        // window — leave it alone, WNP is about to (or just
                        // did) deliver. Keep it in desired so reconcile won't
                        // remove it.
                        desired[tag] = new DesiredEntry(e, minutes, fireAt, GroupFor(e.AccountEmail));
                        continue;
                    }

                    if (fireAt <= now - MissedWindow) continue;
                    if (_missed.WasShown(eventKey)) continue;

                    // If we ever handed this tag to WNP, "absent from actual"
                    // means "WNP delivered and cleaned up", not "we never
                    // knew about it". Without this check we'd duplicate every
                    // delivered toast as a missed-summary on the next pass.
                    if (_missed.WasScheduled(tag)) continue;

                    // Drop events that no longer fall on today's date — past
                    // that point, a catch-up toast is just noise. Mark shown
                    // so the next pass doesn't re-check (and so we don't
                    // aggregate them with a fresh missed reminder that DOES
                    // warrant a toast).
                    if (!IsMissedFreshEnough(e.Start, now))
                    {
                        staleMissedKeys.Add(eventKey);
                        continue;
                    }

                    // Pick the earliest unmet reminder as the event's
                    // representative — that's the one with the most lead-time
                    // signal value.
                    if (!missedByEvent.TryGetValue(eventKey, out var existing) || fireAt < existing.FireAt)
                        missedByEvent[eventKey] = new MissedEntry(eventKey, e, minutes, fireAt);
                }
            }

            var missed = missedByEvent.Values.ToList();

            // ── Diff and apply ───────────────────────────────────────────────
            int added = 0, removed = 0, kept = 0, failed = 0;

            foreach (var (tag, st) in actual)
            {
                if (desired.ContainsKey(tag)) continue;
                try { notifier.RemoveFromSchedule(st); removed++; }
                catch (Exception ex) { Log.Error("Toast", ex, $"RemoveFromSchedule tag={tag}"); }
            }

            foreach (var (tag, d) in desired)
            {
                if (actual.ContainsKey(tag)) { kept++; continue; }
                // If the entry is desired (within PastGrace) but already past
                // its fire time and not present in WNP's queue, WNP will
                // refuse to schedule it — skip to avoid log noise.
                if (d.FireAt <= now) continue;
                try
                {
                    var toast = BuildScheduledToast(d.Event, d.Minutes, d.FireAt, d.Group, tag);
                    notifier.AddToSchedule(toast);
                    _missed.MarkScheduled(tag, d.FireAt);
                    added++;
                    Log.Write("Toast",
                        $"  + scheduled '{d.Event.Title}' fireAt={d.FireAt:yyyy-MM-dd HH:mm} (minus {d.Minutes}min)");
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Error("Toast", ex, $"AddToSchedule '{d.Event.Title}'");
                }
            }

            // Quieter log line when nothing changed — periodic timer ticks would
            // otherwise spam the log every 5 minutes with no signal.
            if (added > 0 || removed > 0 || failed > 0)
            {
                Log.Write("Toast",
                    $"reconcile: events={total} allday={allDay} noRem={noRem} noEmail={noEmail} " +
                    $"desired={desired.Count} actual={actual.Count} " +
                    $"added={added} removed={removed} kept={kept} failed={failed}");
            }

            // ── Missed-reminder catch-up ─────────────────────────────────────
            // Reminders we only just learned about (sync was slow / app was
            // closed) but whose fire-time has already passed get aggregated
            // into a single "you missed N reminders" toast. The tracker
            // dedupes against future passes.
            if (missed.Count > 0)
                ShowMissedSummary(notifier, missed);

            // Suppress stale-but-otherwise-eligible missed candidates from
            // showing up on future passes. ShowMissedSummary already calls
            // MarkShown for the surfaced batch; this covers the silent drop.
            if (staleMissedKeys.Count > 0)
            {
                _missed.MarkShown(staleMissedKeys);
                Log.Write("Toast", $"missed: suppressed {staleMissedKeys.Count} stale candidates (start before today's 04:00)");
            }

            // Coalesced write for any MarkScheduled calls above.
            _missed.Flush();

            // Arm the taskbar flash for the next future fireAt. WNP fires
            // toasts itself but does not notify our process, so we mirror its
            // schedule on a local timer purely to drive the flash.
            ArmFlashTimer(desired, now);
        }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, "reschedule");
        }
    }

    // Picks the soonest future fireAt across all desired entries and arms
    // _flashTimer to fire then. Called at the end of every RescheduleCore so
    // the timer always reflects the latest desired set — events added, moved,
    // or removed since last pass shift the target accordingly.
    //
    // If the same fireAt is still the earliest, we skip the re-arm (avoids
    // a tiny window where Stop+Start could race a tick that was about to
    // fire). If desired is empty or all fireAt's are in the past, the timer
    // is stopped — RescheduleCore already classified those into either
    // PastGrace or missed.
    private void ArmFlashTimer(Dictionary<string, DesiredEntry> desired, DateTime now)
    {
        DateTime? next = null;
        foreach (var (_, d) in desired)
        {
            if (d.FireAt <= now) continue;
            if (next is null || d.FireAt < next) next = d.FireAt;
        }

        if (next is null)
        {
            if (_flashArmedFor is not null)
            {
                _flashTimer.Stop();
                _flashArmedFor = null;
            }
            return;
        }

        if (_flashArmedFor == next) return;

        var delay = next.Value - now;
        // Clamp to a small positive interval — DispatcherQueueTimer requires
        // a strictly positive Interval and we already filtered out past fireAts,
        // but the subtraction can land on near-zero for a fireAt that's about
        // to happen "right now".
        if (delay < TimeSpan.FromMilliseconds(50))
            delay = TimeSpan.FromMilliseconds(50);

        _flashTimer.Stop();
        _flashTimer.Interval = delay;
        _flashTimer.Start();
        _flashArmedFor = next;
    }

    private void OnFlashTimerFired()
    {
        _flashArmedFor = null;
        TaskbarFlasher.Start();
        // Pump a reconcile so the next fireAt gets armed. RescheduleCore is
        // cheap on the steady state (no diff, no logging).
        RescheduleCore();
    }

    private void ShowMissedSummary(ToastNotifier notifier, List<MissedEntry> missed)
    {
        // Most-recent-first — the freshest pass is the most likely to still
        // matter to the user. Preview is capped; the full count goes in the
        // headline.
        missed.Sort((a, b) => b.FireAt.CompareTo(a.FireAt));

        var title = missed.Count == 1
            ? $"Пропущенное напоминание: {missed[0].Event.Title}"
            : $"Пропущено {missed.Count} напоминаний";

        var bodyLines = new List<string>();
        foreach (var m in missed.Take(MissedPreviewCount))
            bodyLines.Add($"{m.FireAt:HH:mm} · {m.Event.Title}");
        if (missed.Count > MissedPreviewCount)
            bodyLines.Add($"…и ещё {missed.Count - MissedPreviewCount}");
        var body = string.Join('\n', bodyLines);

        // Launch arg: take the most-recent missed reminder's date so a click
        // jumps to that day in the calendar. Slight bias, but better than no
        // navigation at all for the aggregate case.
        var launchDate = missed[0].Event.Start;

        var xml = new XmlDocument();
        xml.LoadXml(
            $"""
            <toast launch="{Xml($"date={launchDate:yyyy-MM-dd}")}">
              <visual>
                <binding template="ToastGeneric">
                  <text>{Xml(title)}</text>
                  <text>{Xml(body)}</text>
                </binding>
              </visual>
            </toast>
            """);

        try
        {
            notifier.Show(new ToastNotification(xml)
            {
                Tag = "missed-" + DateTime.UtcNow.Ticks.ToString("x"),
                Group = MissedGroup,
            });
            _missed.MarkShown(missed.Select(m => m.Key));
            // Catch-up surfaced — flash the taskbar so a user who's been
            // away from the keyboard sees the persistent indicator even
            // after the toast itself slides away into Action Center.
            TaskbarFlasher.Start();
            Log.Write("Toast", $"missed: surfaced {missed.Count} reminders (preview: '{title}')");
        }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, "ShowMissedSummary");
        }
    }

    private readonly record struct DesiredEntry(
        CalendarEvent Event, int Minutes, DateTime FireAt, string Group);

    // Key is the per-event dedupe key (EventKey), not a per-reminder tag —
    // a single event surfaces once regardless of how many of its reminders
    // were missed.
    private readonly record struct MissedEntry(
        string Key, CalendarEvent Event, int Minutes, DateTime FireAt);

    private static ScheduledToastNotification BuildScheduledToast(
        CalendarEvent e, int minutes, DateTime fireAt, string group, string tag)
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
            Tag = tag,
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

    // Tag includes event-content fields that, when they change, should
    // invalidate the existing schedule entry — title (visible in the toast),
    // start (drives fireAt + the body's "at HH:mm"). minutes lives in the
    // key because Google may issue several reminders per event.
    private static string TagFor(CalendarEvent e, int minutes) =>
        Hash($"{e.Id}:{minutes}:{e.Start:O}:{e.Title}");

    // Per-event key for missed-reminder dedupe. Stable across the set of
    // reminders on an event — so re-running the catch-up after a tweak to
    // reminder offsets doesn't re-surface the same event.
    private static string EventKey(CalendarEvent e) =>
        Hash($"event:{e.Id}:{e.Start:O}:{e.Title}");

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
