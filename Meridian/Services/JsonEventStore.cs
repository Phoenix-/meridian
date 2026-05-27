using System.Text.Json;
using Meridian.Auth;
using Meridian.Models;

namespace Meridian.Services;

public sealed class JsonEventStore : IEventStore
{
    private static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Meridian", "cache");

    private static string FilePath(AccountId account, string calendarId, int year) =>
        Path.Combine(CacheDir, $"cal-{account.ToDirectoryName()}-{Sanitize(calendarId)}-{year}.json");

    public YearCacheData? Load(AccountId account, string calendarId, int year)
    {
        var path = FilePath(account, calendarId, year);
        try
        {
            if (!File.Exists(path)) return null;
            YearCacheData? data;
            using (var stream = File.OpenRead(path))
                data = JsonSerializer.Deserialize(stream, StoreJsonContext.Default.YearCacheData);

            if (data is null) return null;
            if (data.SchemaHash != YearCacheData.CurrentSchemaHash)
            {
                // Stale schema — drop the file so the next sync writes a fresh
                // copy with the current shape. Old data could pretend to be
                // good (e.g. a field added after the cache was written stays
                // null forever because incremental sync never revisits it).
                TryDelete(path);
                return null;
            }
            return data;
        }
        catch
        {
            // Corrupt/unreadable file is the same as a cache miss; remove so
            // we don't keep hitting it on every load.
            TryDelete(path);
            return null;
        }
    }

    public void Save(YearCacheData data)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            data.SavedAtUtc = DateTime.UtcNow;
            data.SchemaHash = YearCacheData.CurrentSchemaHash;
            var account = AccountId.Parse(data.AccountId);
            using var stream = File.Create(FilePath(account, data.CalendarId, data.Year));
            JsonSerializer.Serialize(stream, data, StoreJsonContext.Default.YearCacheData);
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    public void Delete(AccountId account, string calendarId, int year)
    {
        try
        {
            var path = FilePath(account, calendarId, year);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static string Sanitize(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            buf[i] = char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_';
        }
        return new string(buf);
    }
}
