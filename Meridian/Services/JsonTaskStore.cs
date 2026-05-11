using System.Text.Json;
using Meridian.Models;

namespace Meridian.Services;

public sealed class JsonTaskStore : ITaskStore
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Meridian", "cache");

    private static string FilePath =>
        Path.Combine(CacheDir, "tasks.json");

    public TaskStoreData? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            using var stream = File.OpenRead(FilePath);
            var data = JsonSerializer.Deserialize(stream, StoreJsonContext.Default.TaskStoreData);
            if (data == null) return null;
            return DateTime.UtcNow - data.SavedAtUtc > MaxAge ? null : data;
        }
        catch { return null; }
    }

    public void Save(TaskStoreData data)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            data.SavedAtUtc = DateTime.UtcNow;
            using var stream = File.Create(FilePath);
            JsonSerializer.Serialize(stream, data, StoreJsonContext.Default.TaskStoreData);
        }
        catch { }
    }
}
