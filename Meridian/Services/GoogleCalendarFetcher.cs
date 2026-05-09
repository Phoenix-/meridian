using Meridian.Auth;
using Meridian.Models;

namespace Meridian.Services;

public sealed class GoogleCalendarFetcher(AccountManager accounts, ProviderRegistry providers)
{
    public async Task<(List<CalendarEvent> events, Dictionary<string, List<TaskItem>> tasksByAccount)> FetchMonthAsync(YearMonth ym)
    {
        var from = ym.FirstDay();
        var to   = ym.FirstDayOfNext();

        var ids = accounts.Ids.ToList();

        var eventFetches   = ids.Select(id => providers.Get(id).GetEventsAsync(id, from, to));
        var reminderFetches = ids.Select(id => providers.Get(id).GetTaskReminderTimesAsync(id, from, to));
        var taskFetches    = ids.Select(async id => (id, tasks: await providers.Get(id).GetTasksAsync(id)));

        var allEvents    = (await Task.WhenAll(eventFetches)).SelectMany(x => x).ToList();
        var taskResults  = await Task.WhenAll(taskFetches);
        var reminderMaps = await Task.WhenAll(reminderFetches);

        var allReminders = reminderMaps.SelectMany(d => d).ToDictionary(kv => kv.Key, kv => kv.Value);

        var tasksByAccount = new Dictionary<string, List<TaskItem>>();
        foreach (var (id, tasks) in taskResults)
        {
            foreach (var t in tasks)
                if (t.Id != null && allReminders.TryGetValue(t.Id, out var rt))
                    t.ReminderTime = rt;
            tasksByAccount[id.Email] = tasks;
        }

        return (allEvents, tasksByAccount);
    }
}
