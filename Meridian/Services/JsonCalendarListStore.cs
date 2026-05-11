using System.Text.Json;
using Meridian.Auth;
using Meridian.Models;

namespace Meridian.Services;

public sealed class JsonCalendarListStore : ICalendarListStore
{
    private static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Meridian", "cache");

    private static string FilePath(AccountId account) =>
        Path.Combine(CacheDir, $"calendars-{account.ToDirectoryName()}.json");

    public CalendarListData? Load(AccountId account)
    {
        try
        {
            var path = FilePath(account);
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, StoreJsonContext.Default.CalendarListData);
        }
        catch { return null; }
    }

    public void Save(CalendarListData data)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            data.SavedAtUtc = DateTime.UtcNow;
            var account = AccountId.Parse(data.AccountId);
            using var stream = File.Create(FilePath(account));
            JsonSerializer.Serialize(stream, data, StoreJsonContext.Default.CalendarListData);
        }
        catch { }
    }

    public void Delete(AccountId account)
    {
        try
        {
            var path = FilePath(account);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
