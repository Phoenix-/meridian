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
        try
        {
            var path = FilePath(account, calendarId, year);
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, StoreJsonContext.Default.YearCacheData);
        }
        catch { return null; }
    }

    public void Save(YearCacheData data)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            data.SavedAtUtc = DateTime.UtcNow;
            var account = AccountId.Parse(data.AccountId);
            using var stream = File.Create(FilePath(account, data.CalendarId, data.Year));
            JsonSerializer.Serialize(stream, data, StoreJsonContext.Default.YearCacheData);
        }
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
