using Meridian.Diagnostics;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Meridian.Services;

// Lightweight self-test helper: fires a toast after a delay using the same
// Show()-based path the real ReminderScheduler uses. Wired to a title-bar
// button so we can verify the toast pipeline (and the click → window
// activation flow) without juggling a real Google Calendar event.
internal static class ToastTester
{
    public static void FireIn(TimeSpan delay)
    {
        var fireAt = DateTime.Now + delay;
        Log.Write("Toast", $"test: armed, fires at {fireAt:HH:mm:ss}");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);
                var xml = new XmlDocument();
                // launch=date=YYYY-MM-DD lets us exercise OnToastInvoked's
                // date-routing branch too — a click should bring the window
                // forward and navigate to today's date.
                var launch = $"date={DateTime.Today:yyyy-MM-dd}";
                xml.LoadXml(
                    $"""
                    <toast launch="{System.Net.WebUtility.HtmlEncode(launch)}">
                      <visual>
                        <binding template="ToastGeneric">
                          <text>Meridian test</text>
                          <text>Тестовый тост в {DateTime.Now:HH:mm:ss}</text>
                        </binding>
                      </visual>
                    </toast>
                    """);

                var notifier = ToastNotificationManager.CreateToastNotifier(ToastSetup.ResolvedAumid);
                notifier.Show(new ToastNotification(xml));
                Log.Write("Toast", "test: Show() returned");
            }
            catch (Exception ex)
            {
                Log.Error("Toast", ex, "ToastTester.FireIn");
            }
        });
    }
}
