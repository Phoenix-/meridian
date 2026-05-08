using Meridian.Models;

namespace Meridian.Services;

public enum CacheState { Empty, Loading, Fresh, Stale }

internal sealed class CachedMonth
{
    public CacheState State { get; set; } = CacheState.Empty;
    public List<CalendarEvent> Events { get; set; } = [];
    // Awaitable handle for in-flight fetches so late callers can join rather than re-fetch.
    public Task? LoadingTask { get; set; }
}

public sealed class CalendarCache
{
    // Months beyond this distance from the anchor are eligible for eviction.
    // Raise to retain more history without other changes.
    public int EvictionRadius { get; set; } = 2;

    // Fired on the thread that completed the fetch. Consumers must marshal to UI if needed.
    public event Action<IReadOnlyList<YearMonth>>? DataRefreshed;

    // Fired when the number of in-flight fetches changes (0 = idle).
    public event Action<int>? FetchingCountChanged;
    private int _activeFetches;

    private readonly Dictionary<YearMonth, CachedMonth> _months = [];
    private readonly Dictionary<string, List<TaskItem>> _tasksByAccount = [];

    // ── Public API ────────────────────────────────────────────────────────────

    /// Returns whatever is in the cache right now for [from, to) and kicks off
    /// background fetches for any missing or stale months in the sliding window.
    public CalendarSnapshot Request(DateTime from, DateTime to)
    {
        var anchor = YearMonth.FromDateTime(from);
        var needed = MonthsForRange(from, to).ToList();

        bool allReady = needed.All(ym =>
            _months.TryGetValue(ym, out var e) && e.State is CacheState.Fresh or CacheState.Stale);

        // Kick off missing months (fire-and-forget; DataRefreshed will notify when done).
        EnsureWindow(anchor);

        var (events, tasks) = Slice(from, to);
        bool isComplete = allReady && needed.All(ym =>
            _months.TryGetValue(ym, out var e) && e.State == CacheState.Fresh);

        string? error = null;
        foreach (var ym in needed)
            if (_months.TryGetValue(ym, out var e) && e.State == CacheState.Stale)
                error ??= $"Данные за {ym} могут быть устаревшими";

        return new CalendarSnapshot(events, tasks, isComplete, error);
    }

    // ── Internal fetch machinery ──────────────────────────────────────────────

    private Func<YearMonth, Task<(List<CalendarEvent>, Dictionary<string, List<TaskItem>>)>>? _fetcher;

    /// Wire up the actual Google API fetch. Called once by the owner (MainViewModel).
    internal void SetFetcher(Func<YearMonth, Task<(List<CalendarEvent>, Dictionary<string, List<TaskItem>>)>> fetcher) =>
        _fetcher = fetcher;

    private CachedMonth GetOrCreate(YearMonth key)
    {
        if (!_months.TryGetValue(key, out var entry))
            _months[key] = entry = new CachedMonth();
        return entry;
    }

    /// Fetches a single month and raises DataRefreshed when done.
    private async Task FetchMonthAsync(YearMonth ym)
    {
        var entry = GetOrCreate(ym);
        if (entry.State == CacheState.Loading)
        {
            if (entry.LoadingTask != null) await entry.LoadingTask;
            return;
        }

        entry.State = CacheState.Loading;
        FetchingCountChanged?.Invoke(++_activeFetches);
        entry.LoadingTask = DoFetch(ym, entry);
        try
        {
            await entry.LoadingTask;
        }
        catch { }
        finally
        {
            entry.LoadingTask = null;
            FetchingCountChanged?.Invoke(--_activeFetches);
        }

        DataRefreshed?.Invoke([ym]);
    }

    private async Task DoFetch(YearMonth ym, CachedMonth entry)
    {
        try
        {
            var (events, tasksByAccount) = await _fetcher!(ym);
            entry.Events = events;
            entry.State = CacheState.Fresh;
            foreach (var (email, tasks) in tasksByAccount)
                _tasksByAccount[email] = tasks;
        }
        catch
        {
            entry.State = CacheState.Stale;
            throw;
        }
    }

    private void EnsureWindow(YearMonth anchor)
    {
        MarkStaleOutside(anchor);

        for (int d = -EvictionRadius; d <= EvictionRadius; d++)
        {
            var ym = anchor.Add(d);
            var entry = GetOrCreate(ym);
            if (entry.State is CacheState.Empty or CacheState.Stale)
                _ = FetchMonthAsync(ym);
        }
    }

    private void MarkStaleOutside(YearMonth anchor)
    {
        foreach (var (ym, entry) in _months)
        {
            int dist = Math.Abs(ym.Year * 12 + ym.Month - anchor.Year * 12 - anchor.Month);
            if (dist > EvictionRadius && entry.State == CacheState.Fresh)
                entry.State = CacheState.Stale;
        }
    }

    // ── Slice ─────────────────────────────────────────────────────────────────

    private (List<CalendarEvent>, List<TaskItem>) Slice(DateTime from, DateTime to)
    {
        var events = new List<CalendarEvent>();

        foreach (var ym in MonthsForRange(from, to))
        {
            if (!_months.TryGetValue(ym, out var entry) ||
                entry.State is CacheState.Empty or CacheState.Loading)
                continue;

            foreach (var e in entry.Events)
                if (e.Start < to && e.End > from)
                    events.Add(e);
        }

        events.Sort((a, b) => a.Start.CompareTo(b.Start));

        var dateFrom = DateOnly.FromDateTime(from);
        var dateTo = DateOnly.FromDateTime(to.AddDays(-1));
        var tasks = _tasksByAccount.Values
            .SelectMany(t => t)
            .Where(t => t.Due == null || (t.Due >= dateFrom && t.Due <= dateTo))
            .ToList();

        return (events, tasks);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<YearMonth> MonthsForRange(DateTime from, DateTime to)
    {
        var ym = YearMonth.FromDateTime(from);
        var end = YearMonth.FromDateTime(to.AddDays(-1));
        for (; ym.CompareTo(end) <= 0; ym = ym.Add(1))
            yield return ym;
    }
}
