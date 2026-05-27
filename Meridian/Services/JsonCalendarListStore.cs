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
            CalendarListData? data;
            using (var stream = File.OpenRead(path))
                data = JsonSerializer.Deserialize(stream, StoreJsonContext.Default.CalendarListData);
            if (data == null) return null;
            if (data.SchemaHash != CalendarListData.CurrentSchemaHash)
            {
                // Drop the stale-shaped file so we don't re-deserialize it on
                // every load until a successful sync rewrites it.
                TryDelete(path);
                return null;
            }
            return data;
        }
        catch { return null; }
    }

    public void Save(CalendarListData data)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            data.SavedAtUtc = DateTime.UtcNow;
            data.SchemaHash = CalendarListData.CurrentSchemaHash;
            var account = AccountId.Parse(data.AccountId);
            using var stream = File.Create(FilePath(account));
            JsonSerializer.Serialize(stream, data, StoreJsonContext.Default.CalendarListData);
        }
        catch { }
    }

    public void Delete(AccountId account) => TryDelete(FilePath(account));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
