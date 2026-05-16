using Meridian.Auth;
using Meridian.Diagnostics;
using Meridian.Models;

namespace Meridian.Services;

// Tasks are kept globally per account (no time-window slicing). Refresh wholly
// replaces the snapshot — no incremental sync in this iteration.
public sealed class TaskCache
{
    public event Action? DataRefreshed;
    public event Action? FetchingChanged;
    // Mirrors CalendarCache.AccountAuthExpired — Tasks API answers 401/403 on
    // the same expired tokens, so we report from here too.
    public event Action<AccountId>? AccountAuthExpired;

    public bool IsFetching => _activeFetches > 0;

    private readonly HashSet<AccountId> _authExpired = [];

    public bool IsAuthExpired(AccountId account) => _authExpired.Contains(account);

    public void ClearAuthExpired(AccountId account) => _authExpired.Remove(account);

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
        bool succeeded = false;
        try
        {
            await _loadingTask;
            succeeded = true;
        }
        catch (Exception ex)
        {
            Log.Error("Sync", ex, "tasks fetch failed");
        }
        finally
        {
            _loadingTask = null;
            _activeFetches--;
            FetchingChanged?.Invoke();
        }
        if (succeeded && startGen == _generation)
            DataRefreshed?.Invoke();
    }

    private async Task DoFetch(int startGen)
    {
        // Per-account try/catch so one expired account doesn't tank the whole
        // batch: we still want fresh tasks for the healthy accounts.
        var ids = _accounts.Ids.Where(id => !_authExpired.Contains(id)).ToList();
        var taskFetches = ids.Select(async id =>
        {
            try { return (id, tasks: await _providers.Get(id).GetTasksAsync(id), expired: (AccountAuthExpiredException?)null); }
            catch (AccountAuthExpiredException ex) { return (id, tasks: (List<TaskItem>?)null, expired: ex); }
        });
        var results = await Task.WhenAll(taskFetches);

        if (startGen != _generation) return;

        foreach (var (id, _, expired) in results)
        {
            if (expired is null) continue;
            if (_authExpired.Add(id))
            {
                Log.Error("Sync", expired, $"auth expired for {id} (tasks)");
                AccountAuthExpired?.Invoke(id);
            }
        }

        // Replace only the accounts we successfully fetched; leave previously
        // cached snapshots for expired accounts in place so the UI doesn't
        // visibly lose tasks the second a token goes bad.
        foreach (var (id, tasks, expired) in results)
        {
            if (expired is not null || tasks is null) continue;
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
