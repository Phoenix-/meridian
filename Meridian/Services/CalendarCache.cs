using Meridian.Auth;
using Meridian.Models;

namespace Meridian.Services;

public enum YearStreamState { Empty, Loading, Fresh, Stale }

internal sealed class YearStream
{
    public YearStreamState State { get; set; } = YearStreamState.Empty;
    public string? SyncToken { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    // Keyed by event Id; cancelled events are removed.
    public Dictionary<string, CalendarEvent> Events { get; set; } = [];
    public Task? LoadingTask { get; set; }
}

public sealed class CalendarCache
{
    // Years inside [-WindowRadius, +WindowRadius] of the anchor are guaranteed
    // to be present. Years outside are loaded only when explicitly requested
    // (i.e. the user navigated into them).
    public int WindowRadius { get; set; } = 1;

    public event Action<IReadOnlyList<int>>? DataRefreshed;
    public event Action? FetchingChanged;

    public bool IsFetching => _activeFetches > 0;

    private readonly AccountManager _accounts;
    private readonly ProviderRegistry _providers;
    private readonly IEventStore _store;

    private readonly Dictionary<(AccountId Account, string CalId, int Year), YearStream> _streams = [];

    private int _activeFetches;

    public CalendarCache(AccountManager accounts, ProviderRegistry providers, IEventStore store)
    {
        _accounts = accounts;
        _providers = providers;
        _store = store;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// Returns events that fall within [from, to) using whatever is cached now.
    /// Triggers background sync for years that are missing or stale.
    public List<CalendarEvent> Request(DateTime from, DateTime to)
    {
        var anchorYear = from.Year;
        var needed = YearsForRange(from, to, anchorYear).ToList();

        foreach (var year in needed)
            EnsureYear(year);

        return Slice(from, to);
    }

    /// Forces incremental sync of every loaded year. Use for the Refresh button
    /// and the background timer. Cheap when nothing changed (one HTTP per stream
    /// returning an empty diff).
    public void RefreshAll()
    {
        // Snapshot keys to avoid mutation during iteration.
        var keys = _streams.Keys.ToList();
        foreach (var key in keys)
        {
            var stream = _streams[key];
            if (stream.State is YearStreamState.Loading) continue;
            _ = SyncStreamAsync(key.Account, key.CalId, key.Year, stream);
        }
    }

    /// Drops all in-memory state and on-disk cache. Use after adding or removing
    /// an account. Next Request() rebuilds from scratch with initial syncs.
    public void InvalidateAll()
    {
        foreach (var ((acc, cal, year), _) in _streams)
            _store.Delete(acc, cal, year);
        _streams.Clear();
    }

    // ── Year lifecycle ────────────────────────────────────────────────────────

    private void EnsureYear(int year)
    {
        foreach (var account in _accounts.Ids)
        {
            var provider = _providers.Get(account);
            var calId = provider.PrimaryCalendarId;
            var key = (account, calId, year);

            if (!_streams.TryGetValue(key, out var stream))
            {
                stream = LoadFromDisk(account, calId, year);
                _streams[key] = stream;
            }

            if (stream.State is YearStreamState.Empty or YearStreamState.Stale)
                _ = SyncStreamAsync(account, calId, year, stream);
        }
    }

    private YearStream LoadFromDisk(AccountId account, string calId, int year)
    {
        var stream = new YearStream();
        var data = _store.Load(account, calId, year);
        if (data == null) return stream;

        stream.SyncToken = data.SyncToken;
        stream.WindowStartUtc = data.WindowStartUtc;
        stream.WindowEndUtc = data.WindowEndUtc;
        foreach (var e in data.Events)
            stream.Events[e.Id] = e;
        // Disk-loaded streams are stale: we want an incremental sync to bring
        // them up to date before declaring "fresh".
        stream.State = YearStreamState.Stale;
        return stream;
    }

    private async Task SyncStreamAsync(AccountId account, string calId, int year, YearStream stream)
    {
        if (stream.State == YearStreamState.Loading)
        {
            if (stream.LoadingTask != null) await stream.LoadingTask;
            return;
        }

        stream.State = YearStreamState.Loading;
        _activeFetches++;
        FetchingChanged?.Invoke();
        stream.LoadingTask = DoSync(account, calId, year, stream);
        try { await stream.LoadingTask; }
        catch { /* state already set to Stale by DoSync */ }
        finally
        {
            stream.LoadingTask = null;
            _activeFetches--;
            FetchingChanged?.Invoke();
        }

        DataRefreshed?.Invoke([year]);
    }

    private async Task DoSync(AccountId account, string calId, int year, YearStream stream)
    {
        var provider = _providers.Get(account);
        var windowStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var windowEnd = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        try
        {
            EventSyncResult result;

            if (stream.SyncToken is { } token && stream.WindowStartUtc == windowStart && stream.WindowEndUtc == windowEnd)
            {
                result = await provider.IncrementalSyncEventsAsync(account, calId, token);
                if (result.SyncTokenExpired)
                {
                    // Token gone — fall back to initial.
                    result = await provider.InitialSyncEventsAsync(account, calId, windowStart, windowEnd);
                    stream.Events.Clear();
                }
            }
            else
            {
                result = await provider.InitialSyncEventsAsync(account, calId, windowStart, windowEnd);
                stream.Events.Clear();
            }

            foreach (var e in result.Upserts)
                stream.Events[e.Id] = e;
            foreach (var id in result.CancelledIds)
                stream.Events.Remove(id);

            // Only persist the new token; Google omits it on intermediate pages
            // but always emits it on the last page of a successful sync.
            if (result.NextSyncToken is not null)
                stream.SyncToken = result.NextSyncToken;
            stream.WindowStartUtc = windowStart;
            stream.WindowEndUtc = windowEnd;
            stream.State = YearStreamState.Fresh;

            _store.Save(new YearCacheData
            {
                AccountId = account.ToString(),
                CalendarId = calId,
                Year = year,
                WindowStartUtc = windowStart,
                WindowEndUtc = windowEnd,
                SyncToken = stream.SyncToken,
                Events = stream.Events.Values.ToList(),
            });
        }
        catch
        {
            stream.State = YearStreamState.Stale;
            throw;
        }
    }

    // ── Slice ─────────────────────────────────────────────────────────────────

    private List<CalendarEvent> Slice(DateTime from, DateTime to)
    {
        // Year streams overlap on event ids only for events that straddle a
        // year boundary (returned by both years' initial sync). Dedupe by
        // (AccountEmail, Id) — same email + id refers to the same event across
        // streams of one account.
        var seen = new HashSet<(string?, string)>();
        var result = new List<CalendarEvent>();

        foreach (var stream in _streams.Values)
        {
            foreach (var e in stream.Events.Values)
            {
                if (e.Start >= to || e.End <= from) continue;
                if (!seen.Add((e.AccountEmail, e.Id))) continue;
                result.Add(e);
            }
        }

        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IEnumerable<int> YearsForRange(DateTime from, DateTime to, int anchorYear)
    {
        var min = Math.Min(from.Year, anchorYear - WindowRadius);
        // to is exclusive; if it falls exactly on Jan 1, the prior year is the last one needed.
        var rangeEndYear = to == to.Date && to.Month == 1 && to.Day == 1 ? to.Year - 1 : to.Year;
        var max = Math.Max(rangeEndYear, anchorYear + WindowRadius);
        for (int y = min; y <= max; y++) yield return y;
    }
}
