using Meridian.Auth;
using Meridian.Diagnostics;
using Meridian.Models;

namespace Meridian.Services;

// Keeps the per-account list of calendars in memory, backed by disk for fast
// startup. The list is refreshed from the server on demand (startup and
// account changes) — not on the 60s timer, since calendars change rarely.
public sealed class CalendarListCache
{
    public event Action? DataRefreshed;
    public event Action<AccountId>? AccountAuthExpired;

    private readonly AccountManager _accounts;
    private readonly ProviderRegistry _providers;
    private readonly ICalendarListStore _store;

    private readonly Dictionary<AccountId, List<CalendarInfo>> _byAccount = [];
    private readonly HashSet<AccountId> _hydrated = [];

    // Bumped on InvalidateAll. In-flight RefreshAllAsync checks this and bails
    // out before mutating the cache or writing to disk if invalidated mid-flight.
    private int _generation;

    public CalendarListCache(AccountManager accounts, ProviderRegistry providers, ICalendarListStore store)
    {
        _accounts = accounts;
        _providers = providers;
        _store = store;

        // Refresh whenever an account appears so the cache catches up after
        // LoadSavedAccountsAsync finishes (which happens after construction).
        _accounts.Accounts.CollectionChanged += (_, _) => RefreshAll();
    }

    /// Returns the cached list for an account. Empty if nothing is known yet —
    /// caller should treat that as "no calendars to sync until RefreshAll completes".
    public IReadOnlyList<CalendarInfo> GetFor(AccountId account)
    {
        HydrateFromDisk(account);
        return _byAccount.TryGetValue(account, out var list) ? list : [];
    }

    /// Fire-and-forget refresh of all accounts' calendar lists. On success,
    /// raises DataRefreshed once at the end.
    public void RefreshAll() => _ = RefreshAllAsync();

    public async Task RefreshAllAsync()
    {
        var startGen = _generation;
        var ids = _accounts.Ids.ToList();
        foreach (var id in ids) HydrateFromDisk(id);

        var fetches = ids.Select(async id =>
        {
            try { return (id, list: await _providers.Get(id).GetCalendarsAsync(id), expired: (AccountAuthExpiredException?)null); }
            catch (AccountAuthExpiredException ex) { return (id, list: (List<CalendarInfo>?)null, expired: ex); }
            catch (Exception ex)
            {
                Log.Error("Sync", ex, $"calendar list fetch failed for {id}");
                return (id, list: (List<CalendarInfo>?)null, expired: (AccountAuthExpiredException?)null);
            }
        });

        var results = await Task.WhenAll(fetches);

        if (startGen != _generation) return;

        bool changed = false;

        foreach (var (id, list, expired) in results)
        {
            if (expired is not null)
            {
                Log.Error("Sync", expired, $"auth expired for {id} (calendar list)");
                AccountAuthExpired?.Invoke(id);
                continue;
            }
            if (list is null) continue;
            _byAccount[id] = list;
            _store.Save(new CalendarListData
            {
                AccountId = id.ToString(),
                Calendars = list,
            });
            changed = true;
        }

        if (changed) DataRefreshed?.Invoke();
    }

    public void InvalidateAll()
    {
        _generation++;
        foreach (var account in _byAccount.Keys.ToList())
            _store.Delete(account);
        _byAccount.Clear();
        _hydrated.Clear();
    }

    /// Drops in-memory and on-disk state for one account only.
    public void InvalidateAccount(AccountId account)
    {
        _generation++;
        _store.Delete(account);
        _byAccount.Remove(account);
        _hydrated.Remove(account);
    }

    private void HydrateFromDisk(AccountId account)
    {
        if (!_hydrated.Add(account)) return;
        var data = _store.Load(account);
        if (data != null)
            _byAccount[account] = data.Calendars;
    }
}
