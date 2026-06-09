using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Auth;
using Meridian.Diagnostics;

namespace Meridian.Services;

// One resolved (or known-absent) directory entry for an email. NotInDirectory
// marks a negative result — the email isn't in this account's org directory
// (external guest, or a personal-Gmail account with no directory at all). We
// keep negatives so we don't re-query them on every flyout open, but with a
// short TTL so a newly-added colleague eventually resolves.
public sealed class DirectoryPerson
{
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? PhotoUrl { get; set; }
    public bool NotInDirectory { get; set; }
    // When this entry was written, as UTC ticks. Used for TTL expiry. Stored as
    // ticks (not DateTime) to keep the on-disk JSON stable and culture-free.
    public long StoredAtUtcTicks { get; set; }
}

internal sealed class DirectoryCacheData
{
    public List<DirectoryPerson> People { get; set; } = [];
}

[JsonSerializable(typeof(DirectoryCacheData))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class DirectoryCacheJsonContext : JsonSerializerContext { }

// Resolves attendee emails to directory names/photos, backed by an in-memory
// map plus a per-account JSON file on disk. Lazy: nothing is fetched until a
// caller asks for a specific email. Designed as a static entry point because
// the project has no DI container — services are newed up directly (see
// GoogleOAuthClient, EventDetailsFlyout.Show). All disk IO is non-throwing;
// losing the cache only costs a re-fetch.
public static class DirectoryCache
{
    // Positive entries are stable (a person's name/photo rarely changes), so we
    // hold them a week. Negative entries expire fast so a freshly-onboarded
    // colleague — or a directory that came online after first login — resolves
    // without the user having to wait days.
    private static readonly TimeSpan PositiveTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(1);

    private static readonly Lock _gate = new();

    // account -> (lowercased email -> entry). Mirrors what's on disk.
    private static readonly Dictionary<AccountId, Dictionary<string, DirectoryPerson>> _byAccount = [];
    private static readonly HashSet<AccountId> _hydrated = [];

    // Emails currently being resolved over the network, so concurrent flyout
    // rows for the same person don't fire duplicate requests.
    private static readonly HashSet<(AccountId, string)> _inFlight = [];

    private static string DirForAccount(AccountId account) =>
        Path.Combine(AppPaths.Cache, "directory");

    private static string PathForAccount(AccountId account) =>
        Path.Combine(DirForAccount(account), account.ToDirectoryName() + ".json");

    // Synchronous memory-only lookup for instant first paint. Returns true only
    // for a *positive*, non-expired entry — a negative or stale entry reports
    // false so the caller falls back to its email-derived label and triggers a
    // background ResolveAsync.
    public static bool TryGet(AccountId account, string email, out DirectoryPerson person)
    {
        person = default!;
        if (string.IsNullOrWhiteSpace(email)) return false;
        var key = Normalize(email);

        lock (_gate)
        {
            Hydrate(account);
            if (_byAccount.TryGetValue(account, out var map)
                && map.TryGetValue(key, out var entry)
                && !IsExpired(entry)
                && !entry.NotInDirectory
                && !string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                person = entry;
                return true;
            }
        }
        return false;
    }

    // Resolves an email, hitting the directory only if there's no fresh cached
    // entry. Returns the resolved person (positive or negative marker), or null
    // if the lookup couldn't run (network/cancellation) — callers treat null as
    // "leave the current label alone".
    public static async Task<DirectoryPerson?> ResolveAsync(AccountId account, string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var key = Normalize(email);

        lock (_gate)
        {
            Hydrate(account);
            if (_byAccount.TryGetValue(account, out var map)
                && map.TryGetValue(key, out var cached)
                && !IsExpired(cached))
                return cached;

            // Coalesce concurrent resolves of the same email.
            if (!_inFlight.Add((account, key)))
                return null;
        }

        try
        {
            DirectoryPerson entry;
            try
            {
                var lookup = await new GoogleApiClient(account).SearchDirectoryPersonAsync(email, ct);
                entry = new DirectoryPerson
                {
                    Email = email,
                    DisplayName = lookup?.DisplayName,
                    PhotoUrl = lookup?.PhotoUrl,
                    NotInDirectory = lookup is null,
                    StoredAtUtcTicks = DateTime.UtcNow.Ticks,
                };
            }
            catch (AccountAuthExpiredException)
            {
                // Auth problems are not a directory miss — don't poison the
                // cache with a negative entry. Let the existing sync paths
                // surface re-auth; we just decline to resolve this time.
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error("Directory", ex, $"resolve failed for {email}");
                return null;
            }

            Store(account, key, entry);
            return entry;
        }
        finally
        {
            lock (_gate) _inFlight.Remove((account, key));
        }
    }

    // Drops in-memory and on-disk directory state for one account. Mirrors the
    // InvalidateAccount on CalendarListCache/TaskCache/CalendarCache so removing
    // (or re-authing) an account doesn't leave stale names/photos — or the
    // per-account JSON file — behind.
    public static void InvalidateAccount(AccountId account)
    {
        lock (_gate)
        {
            _byAccount.Remove(account);
            _hydrated.Remove(account);
            try { File.Delete(PathForAccount(account)); }
            catch (Exception ex) { Log.Error("Directory", ex, $"delete failed for {account}"); }
        }
    }

    // Drops all cached directory state (memory + disk). Pairs with the
    // InvalidateAll on the sibling caches for a full reset.
    public static void InvalidateAll()
    {
        lock (_gate)
        {
            foreach (var account in _byAccount.Keys.ToList())
            {
                try { File.Delete(PathForAccount(account)); }
                catch (Exception ex) { Log.Error("Directory", ex, $"delete failed for {account}"); }
            }
            _byAccount.Clear();
            _hydrated.Clear();
        }
    }

    private static void Store(AccountId account, string key, DirectoryPerson entry)
    {
        lock (_gate)
        {
            if (!_byAccount.TryGetValue(account, out var map))
                _byAccount[account] = map = [];
            map[key] = entry;
            SaveToDisk(account, map);
        }
    }

    private static bool IsExpired(DirectoryPerson e)
    {
        // Guard against out-of-range ticks from a corrupted/hand-edited cache
        // file: the DateTime(long, DateTimeKind) ctor throws for ticks < 0 or
        // > DateTime.MaxValue.Ticks, and this runs on the synchronous UI-thread
        // TryGet path during flyout build — an unguarded throw would crash it.
        // A bad timestamp is treated as expired so the entry is simply refetched.
        if (e.StoredAtUtcTicks < 0 || e.StoredAtUtcTicks > DateTime.MaxValue.Ticks)
            return true;
        var ttl = e.NotInDirectory ? NegativeTtl : PositiveTtl;
        return DateTime.UtcNow - new DateTime(e.StoredAtUtcTicks, DateTimeKind.Utc) > ttl;
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    // ── Disk ────────────────────────────────────────────────────────────────────

    private static void Hydrate(AccountId account)
    {
        if (!_hydrated.Add(account)) return;
        try
        {
            var path = PathForAccount(account);
            if (!File.Exists(path)) return;
            using var stream = File.OpenRead(path);
            var data = JsonSerializer.Deserialize(stream, DirectoryCacheJsonContext.Default.DirectoryCacheData);
            if (data?.People is null) return;

            var map = new Dictionary<string, DirectoryPerson>();
            foreach (var p in data.People)
                if (!string.IsNullOrWhiteSpace(p.Email))
                    map[Normalize(p.Email)] = p;
            _byAccount[account] = map;
        }
        catch (Exception ex)
        {
            Log.Error("Directory", ex, $"hydrate failed for {account}");
        }
    }

    private static void SaveToDisk(AccountId account, Dictionary<string, DirectoryPerson> map)
    {
        try
        {
            Directory.CreateDirectory(DirForAccount(account));
            var data = new DirectoryCacheData { People = [.. map.Values] };
            using var stream = File.Create(PathForAccount(account));
            JsonSerializer.Serialize(stream, data, DirectoryCacheJsonContext.Default.DirectoryCacheData);
        }
        catch (Exception ex)
        {
            Log.Error("Directory", ex, $"save failed for {account}");
        }
    }
}
