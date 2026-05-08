using Google.Apis.Auth.OAuth2;
using Meridian.Auth;
using Meridian.Models;

namespace Meridian.Services;

public sealed class GoogleCalendarFetcher(AccountManager accounts)
{
    public async Task<(List<CalendarEvent> events, Dictionary<string, List<TaskItem>> tasksByAccount)> FetchMonthAsync(YearMonth ym)
    {
        var from = ym.FirstDay();
        var to = ym.FirstDayOfNext();

        var calendarServices = accounts.Credentials
            .Select(kv => (email: kv.Key, svc: new GoogleCalendarService(kv.Value)))
            .ToList();

        var eventFetches = calendarServices.Select(x => x.svc.GetEventsAsync(from, to, x.email));
        var taskFetches = accounts.Credentials.Select(async kv =>
            (email: kv.Key, tasks: await new GoogleTasksService(kv.Value).GetTasksAsync(kv.Key)));
        var reminderFetches = calendarServices.Select(x => x.svc.GetTaskReminderTimesAsync(from, to));

        var allEvents = (await Task.WhenAll(eventFetches)).SelectMany(x => x).ToList();
        var taskResults = await Task.WhenAll(taskFetches);
        var reminderMaps = await Task.WhenAll(reminderFetches);

        var allReminders = reminderMaps.SelectMany(d => d).ToDictionary(kv => kv.Key, kv => kv.Value);

        var tasksByAccount = new Dictionary<string, List<TaskItem>>();
        foreach (var (email, tasks) in taskResults)
        {
            foreach (var t in tasks)
                if (t.Id != null && allReminders.TryGetValue(t.Id, out var rt))
                    t.ReminderTime = rt;
            tasksByAccount[email] = tasks;
        }

        return (allEvents, tasksByAccount);
    }
}
