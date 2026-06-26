using Meridian.Diagnostics;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Meridian.Services;

// Lightweight self-test helpers for the toast pipeline.
//
// FireIn: in-process Task.Delay + Show() — verifies the immediate-toast path
//   that the real ReminderScheduler used to depend on. A pop here means
//   AUMID/COM/shortcut wiring is correct end-to-end.
//
// ScheduleIn: ScheduledToastNotification + AddToSchedule — verifies the
//   sanctioned "fire-at-time-T-even-if-app-is-closed" path. We've seen this
//   path silently drop toasts on Win11 26200 unpackaged builds even with full
//   registration; this button lets us re-check after each change without
//   creating a real Google Calendar event.
//
// Both buttons use distinct title prefixes ("[show]" / "[sched]") so an
// on-screen toast immediately tells us which mechanism delivered it.
internal static class ToastTester
{
    public static void FireIn(TimeSpan delay)
    {
        var fireAt = DateTime.Now + delay;
        Log.Write("Toast", $"test/show: armed, fires at {fireAt:HH:mm:ss}");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);
                var notifier = ToastSetup.TryCreateNotifier();
                if (notifier is null) { Log.Write("Toast", "test/show: popups muted, skipped"); return; }
                notifier.Show(new ToastNotification(BuildXml("[show] Meridian test")));
                Log.Write("Toast", "test/show: Show() returned");
                App.MainWindow?.DispatcherQueue.TryEnqueue(TaskbarFlasher.Start);
            }
            catch (Exception ex)
            {
                Log.Error("Toast", ex, "ToastTester.FireIn");
            }
        });
    }

    public static void ScheduleIn(TimeSpan delay)
    {
        var fireAt = DateTime.Now + delay;
        Log.Write("Toast", $"test/sched: registering, target {fireAt:HH:mm:ss}");

        try
        {
            var notifier = ToastSetup.TryCreateNotifier();
            if (notifier is null) { Log.Write("Toast", "test/sched: popups muted, skipped"); return; }
            var toast = new ScheduledToastNotification(
                BuildXml("[sched] Meridian test"), new DateTimeOffset(fireAt))
            {
                // Distinct tag from real reminders so a wholesale group-clear
                // for an account doesn't sweep this test toast away.
                Tag = "tester",
                Group = "mrd-tester",
                ExpirationTime = new DateTimeOffset(fireAt.AddMinutes(5)),
            };
            notifier.AddToSchedule(toast);
            Log.Write("Toast", "test/sched: AddToSchedule returned");

            // The test path bypasses ReminderScheduler (the whole point of
            // this button is to exercise WNP delivery in isolation), but
            // that also bypasses the flash arming the real scheduler does.
            // Fire a parallel local timer so the diagnostic button can
            // visually verify the taskbar-flash wiring too.
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay);
                    Log.Write("Toast", "test/sched: flash armed");
                    App.MainWindow?.DispatcherQueue.TryEnqueue(TaskbarFlasher.Start);
                }
                catch (Exception ex)
                {
                    Log.Error("Toast", ex, "ToastTester.ScheduleIn flash");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, "ToastTester.ScheduleIn");
        }
    }

    private static XmlDocument BuildXml(string title)
    {
        var xml = new XmlDocument();
        // launch=date=YYYY-MM-DD lets us exercise OnToastInvoked's date-routing
        // branch — a click should bring the window forward and navigate to
        // today's date.
        var launch = $"date={DateTime.Today:yyyy-MM-dd}";
        xml.LoadXml(
            $"""
            <toast activationType="foreground" launch="{System.Net.WebUtility.HtmlEncode(launch)}">
              <visual>
                <binding template="ToastGeneric">
                  <text>{System.Net.WebUtility.HtmlEncode(title)}</text>
                  <text>Сработал в {DateTime.Now:HH:mm:ss}</text>
                </binding>
              </visual>
            </toast>
            """);
        return xml;
    }
}
