using Meridian.Auth;
using Meridian.Models;

namespace Meridian.Services;

// Tasks are kept globally per account (no time-window slicing). Refresh wholly
// replaces the snapshot — no incremental sync in this iteration.
public sealed class TaskCache
{
    public event Action? DataRefreshed;
    public event Action? FetchingChanged;

    public bool IsFetching => _activeFetches > 0;

    private readonly AccountManager _accounts;
    private readonly ProviderRegistry _providers;
    private readonly ITaskStore _store;

    private readonly Dictionary<string, List<TaskItem>> _tasksByAccount = [];
    private bool _loaded;
    private Task? _loadingTask;
    private int _activeFetches;

    // Bumped on InvalidateAll. In-flight DoFetch checks this and bails out
    // before mutating the cache or writing to disk if it was invalidated mid-flight.
    private int _generation;

    public TaskCache(AccountManager accounts, ProviderRegistry providers, ITaskStore store)
    {
        _accounts = accounts;
        _providers = providers;
        _store = store;
    }

    /// Returns tasks visible in [from, to). Triggers a one-shot background fetch
    /// the first time it's called; subsequent calls only re-fetch via RefreshAll.
    public List<TaskItem> Request(DateTime from, DateTime to)
    {
        EnsureLoaded();
        return Slice(from, to);
    }

    /// Re-fetch tasks from all accounts and replace the cached snapshot.
    public void RefreshAll() => _ = FetchAsync();

    public void InvalidateAll()
    {
        _generation++;
        _tasksByAccount.Clear();
        _loaded = false;
    }

    /// Drops in-memory state for one account. The on-disk blob is rewritten
    /// without that account on the next successful fetch; other accounts' tasks
    /// remain visible immediately.
    public void InvalidateAccount(AccountId account)
    {
        _generation++;
        _tasksByAccount.Remove(account.Email);
        // Persist the trimmed snapshot so a crash before the next fetch doesn't
        // resurrect the removed account's tasks from disk.
        _store.Save(new TaskStoreData
        {
            TasksByAccount = new Dictionary<string, List<TaskItem>>(_tasksByAccount),
        });
    }

    private void EnsureLoaded()
    {
        if (_loaded || _loadingTask != null) return;

        // Try disk first so the first paint isn't blank.
        var disk = _store.Load();
        if (disk != null)
        {
            foreach (var (email, tasks) in disk.TasksByAccount)
                _tasksByAccount[email] = tasks;
            _loaded = true;
        }
        _ = FetchAsync();
    }

    private async Task FetchAsync()
    {
        if (_loadingTask != null) { await _loadingTask; return; }
        var startGen = _generation;
        _loadingTask = DoFetch(startGen);
        _activeFetches++;
        FetchingChanged?.Invoke();
        try { await _loadingTask; }
        catch { }
        finally
        {
            _loadingTask = null;
            _activeFetches--;
            FetchingChanged?.Invoke();
        }
        if (startGen == _generation)
            DataRefreshed?.Invoke();
    }

    private async Task DoFetch(int startGen)
    {
        var ids = _accounts.Ids.ToList();
        var taskFetches = ids.Select(async id => (id, tasks: await _providers.Get(id).GetTasksAsync(id)));
        var taskResults = await Task.WhenAll(taskFetches);

        // Reminder times still come from the @tasks calendar via a long-window
        // GetTaskReminderTimesAsync. Keep the legacy fetch but with a wide window.
        var from = DateTime.UtcNow.AddYears(-1);
        var to = DateTime.UtcNow.AddYears(2);
        var reminderFetches = ids.Select(id => _providers.Get(id).GetTaskReminderTimesAsync(id, from, to));
        var reminderMaps = await Task.WhenAll(reminderFetches);
        var allReminders = reminderMaps.SelectMany(d => d).ToDictionary(kv => kv.Key, kv => kv.Value);

        if (startGen != _generation) return;

        _tasksByAccount.Clear();
        foreach (var (id, tasks) in taskResults)
        {
            foreach (var t in tasks)
                if (t.Id != null && allReminders.TryGetValue(t.Id, out var rt))
                    t.ReminderTime = rt;
            _tasksByAccount[id.Email] = tasks;
        }
        _loaded = true;

        _store.Save(new TaskStoreData
        {
            TasksByAccount = new Dictionary<string, List<TaskItem>>(_tasksByAccount),
        });
    }

    private List<TaskItem> Slice(DateTime from, DateTime to)
    {
        var dateFrom = DateOnly.FromDateTime(from);
        var dateTo = DateOnly.FromDateTime(to.AddDays(-1));
        return _tasksByAccount.Values
            .SelectMany(t => t)
            .Where(t => t.Due == null || (t.Due >= dateFrom && t.Due <= dateTo))
            .ToList();
    }
}
