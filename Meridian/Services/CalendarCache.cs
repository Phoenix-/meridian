using Meridian.Auth;
using Meridian.Diagnostics;
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
    // Raised once for every account whose token can't be refreshed. The view
    // model uses it to surface a "session expired" affordance. We keep firing
    // on every failure so callers can de-dupe with their own policy.
    public event Action<AccountId>? AccountAuthExpired;

    public bool IsFetching => _activeFetches > 0;

    // Accounts whose tokens are known-bad. They get skipped by EnsureYear /
    // RefreshAll so a single broken token doesn't burn the polling loop.
    // Cleared by ClearAuthExpired() after a successful re-auth.
    private readonly HashSet<AccountId> _authExpired = [];

    public bool IsAuthExpired(AccountId account) => _authExpired.Contains(account);

    public void ClearAuthExpired(AccountId account) => _authExpired.Remove(account);

    private readonly AccountManager _accounts;
    private readonly ProviderRegistry _providers;
    private readonly IEventStore _store;
    private readonly CalendarListCache _calendars;

    private readonly Dictionary<(AccountId Account, string CalId, int Year), YearStream> _streams = [];

    // Bumped on InvalidateAll. In-flight syncs started before the bump check
    // their captured generation against this and bail out before mutating state
    // or writing to disk — otherwise they'd silently restore the just-cleared
    // cache (e.g. after removing an account).
    private int _generation;

    private int _activeFetches;

    public CalendarCache(AccountManager accounts, ProviderRegistry providers, IEventStore store, CalendarListCache calendars)
    {
        _accounts = accounts;
        _providers = providers;
        _store = store;
        _calendars = calendars;
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

    /// Returns the deduped union of events from every loaded stream that
    /// overlaps [from, to). Used by the reminder scheduler to pick events in
    /// the near future without triggering year hydration.
    public List<CalendarEvent> SnapshotRange(DateTime from, DateTime to) => Slice(from, to);

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
            if (_authExpired.Contains(key.Account)) continue;
            // The calendar may have been removed or deselected since the stream
            // was loaded — fall back to a synthetic CalendarInfo to keep state
            // consistent if so (we just won't tag events with a fresh color).
            var info = LookupCalendar(key.Account, key.CalId)
                       ?? new CalendarInfo { Id = key.CalId };
            _ = SyncStreamAsync(key.Account, info, key.Year, stream);
        }
    }

    private CalendarInfo? LookupCalendar(AccountId account, string calId)
    {
        foreach (var c in _calendars.GetFor(account))
            if (c.Id == calId) return c;
        return null;
    }

    /// True when the signed-in user can RSVP to this event: they appear in the
    /// attendee list (so there's a response to set) AND the owning calendar is
    /// writable. Read-only calendars (accessRole reader/freeBusyReader) can't be
    /// patched, so we don't offer the affordance. Synchronous — used by the UI to
    /// decide whether to show Yes/No/Maybe at all.
    public bool CanRespond(CalendarEvent ev)
    {
        if (EventActions.SelfAttendee(ev) is null) return false;
        if (ev.AccountEmail is not { Length: > 0 } email || ev.CalendarId is not { Length: > 0 } calId)
            return false;
        if (ResolveAccount(email) is not { } account) return false;
        var cal = LookupCalendar(account, calId);
        return cal is not null && IsWritableRole(cal.AccessRole);
    }

    private AccountId? ResolveAccount(string email)
    {
        foreach (var a in _accounts.Ids)
            if (string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase))
                return a;
        return null;
    }

    private static bool IsWritableRole(string accessRole) =>
        accessRole is "owner" or "writer";

    /// Sets the signed-in user's RSVP on an event. Updates the local copy
    /// optimistically and fires DataRefreshed so the UI reflects the choice
    /// immediately, then PATCHes Google and kicks a background incremental sync
    /// to reconcile. On failure the optimistic change is reverted (and
    /// DataRefreshed fired again) and the method returns false so the UI can
    /// surface an error. Auth-expired is handled like the sync paths.
    public async Task<bool> SetMyResponseAsync(CalendarEvent ev, string status, CancellationToken ct = default)
    {
        if (EventActions.SelfAttendee(ev) is not { } self) return false;
        if (ev.AccountEmail is not { Length: > 0 } email || ev.CalendarId is not { Length: > 0 } calId)
            return false;
        if (ResolveAccount(email) is not { } account) return false;

        var previous = self.ResponseStatus;
        if (previous == status) return true; // no-op

        // Optimistic: mutate the live object the UI holds, then repaint. This
        // runs on the caller (UI) thread before the first await. The mutation
        // targets the same CalendarEvent instance Slice() hands the UI, so the
        // repaint shows the new status instantly. A concurrent DoSync could
        // replace this instance in the stream dictionary, dropping the
        // optimistic write — RefreshAll() below reconciles to the canonical
        // state regardless, so the worst case is a brief flicker, not stale UI.
        self.ResponseStatus = status;
        DataRefreshed?.Invoke([ev.Start.Year]);

        try
        {
            var provider = _providers.Get(account);
            await provider.SetMyResponseStatusAsync(
                account, calId, ev.Id, ev.Attendees ?? [], ev.Rooms, status, ct);
        }
        catch (AccountAuthExpiredException ex)
        {
            self.ResponseStatus = previous;
            DataRefreshed?.Invoke([ev.Start.Year]);
            if (_authExpired.Add(account))
            {
                Log.Error("Rsvp", ex, $"auth expired for {account}");
                AccountAuthExpired?.Invoke(account);
            }
            return false;
        }
        catch (Exception ex)
        {
            self.ResponseStatus = previous;
            DataRefreshed?.Invoke([ev.Start.Year]);
            Log.Error("Rsvp", ex, $"RSVP failed for {account} event={ev.Id}");
            return false;
        }

        // Pull the canonical state (and the organizer's view of our RSVP) on the
        // next incremental sync. Cheap when nothing else changed.
        RefreshAll();
        return true;
    }

    /// Drops all in-memory state and on-disk cache. Use after adding or removing
    /// an account. Next Request() rebuilds from scratch with initial syncs.
    public void InvalidateAll()
    {
        _generation++;
        foreach (var ((acc, cal, year), _) in _streams)
            _store.Delete(acc, cal, year);
        _streams.Clear();
    }

    /// Drops in-memory and on-disk state for one account only. Other accounts'
    /// streams stay intact, so the UI keeps showing their events without
    /// waiting for a full re-sync.
    public void InvalidateAccount(AccountId account)
    {
        _generation++;
        var toRemove = _streams.Keys.Where(k => k.Account == account).ToList();
        foreach (var key in toRemove)
        {
            _store.Delete(key.Account, key.CalId, key.Year);
            _streams.Remove(key);
        }
    }

    // ── Year lifecycle ────────────────────────────────────────────────────────

    private void EnsureYear(int year)
    {
        foreach (var account in _accounts.Ids)
        {
            if (_authExpired.Contains(account)) continue;

            foreach (var cal in _calendars.GetFor(account))
            {
                if (!cal.Selected) continue;

                var key = (account, cal.Id, year);
                if (!_streams.TryGetValue(key, out var stream))
                {
                    stream = LoadFromDisk(account, cal.Id, year);
                    _streams[key] = stream;
                }

                if (stream.State is YearStreamState.Empty or YearStreamState.Stale)
                    _ = SyncStreamAsync(account, cal, year, stream);
            }
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

    private async Task SyncStreamAsync(AccountId account, CalendarInfo cal, int year, YearStream stream)
    {
        if (stream.State == YearStreamState.Loading)
        {
            if (stream.LoadingTask != null) await stream.LoadingTask;
            return;
        }

        var startGen = _generation;
        stream.State = YearStreamState.Loading;
        _activeFetches++;
        FetchingChanged?.Invoke();
        bool succeeded = false;
        stream.LoadingTask = DoSync(account, cal, year, stream, startGen);
        try
        {
            await stream.LoadingTask;
            succeeded = true;
        }
        catch (AccountAuthExpiredException ex)
        {
            // Mark the whole account as expired so the polling loop and any
            // follow-up Request() calls skip its streams entirely. Without
            // this, a stale token sends us through stream → fail → DataRefreshed
            // → Request → EnsureYear → stream loop on every UI refresh.
            if (_authExpired.Add(account))
            {
                Log.Error("Sync", ex, $"auth expired for {account}");
                AccountAuthExpired?.Invoke(account);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Sync", ex, $"sync failed for {account} cal={cal.Id} year={year}");
        }
        finally
        {
            stream.LoadingTask = null;
            _activeFetches--;
            FetchingChanged?.Invoke();
        }

        // Only notify when we actually changed the snapshot. Firing DataRefreshed
        // on failure used to loop: subscribers re-Request, EnsureYear sees Stale,
        // launches a fresh sync that fails the same way.
        if (succeeded && startGen == _generation)
            DataRefreshed?.Invoke([year]);
    }

    private async Task DoSync(AccountId account, CalendarInfo cal, int year, YearStream stream, int startGen)
    {
        var provider = _providers.Get(account);
        var windowStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var windowEnd = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        try
        {
            EventSyncResult result;

            var defaults = cal.DefaultPopupReminderMinutes;

            if (stream.SyncToken is { } token && stream.WindowStartUtc == windowStart && stream.WindowEndUtc == windowEnd)
            {
                result = await provider.IncrementalSyncEventsAsync(account, cal.Id, token, defaults);
                if (result.SyncTokenExpired || result.MasterRecurrenceSeen)
                {
                    // SyncTokenExpired: server forgot our token (HTTP 410).
                    // MasterRecurrenceSeen: incremental returned a series master
                    // instead of expanded instances — only a fresh window query
                    // with singleEvents=true gets us the instances back.
                    result = await provider.InitialSyncEventsAsync(account, cal.Id, windowStart, windowEnd, defaults);
                    if (startGen != _generation) return;
                    stream.Events.Clear();
                }
            }
            else
            {
                result = await provider.InitialSyncEventsAsync(account, cal.Id, windowStart, windowEnd, defaults);
                if (startGen != _generation) return;
                stream.Events.Clear();
            }

            if (startGen != _generation) return;

            foreach (var e in result.Upserts)
            {
                // Provider returns events without knowing which calendar they
                // came from; we stamp it here for UI color/group logic.
                e.CalendarId = cal.Id;
                e.CalendarColor = cal.BackgroundColor;
                e.CalendarTextColor = cal.ForegroundColor;
                e.CalendarTitle = cal.Title;
                stream.Events[e.Id] = e;
            }
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
                CalendarId = cal.Id,
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
        // Year streams overlap on event ids when an event straddles a year
        // boundary (returned by both years' initial sync). Dedupe by
        // (AccountEmail, CalendarId, Id) — same triple across streams of one
        // account/calendar means it's the same event.
        var seen = new HashSet<(string?, string?, string)>();
        var result = new List<CalendarEvent>();

        // Snapshot the stream list and each stream's events before iterating.
        // DoSync continuations may mutate _streams[i].Events on the thread pool
        // (no SynchronizationContext capture), and reminder scheduling now
        // races with Refresh's UI-thread calls into Slice.
        var streamSnapshot = _streams.Values.ToArray();
        foreach (var stream in streamSnapshot)
        {
            var events = stream.Events.Values.ToArray();
            foreach (var e in events)
            {
                if (e.Start >= to || e.End <= from) continue;
                if (!seen.Add((e.AccountEmail, e.CalendarId, e.Id))) continue;
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
