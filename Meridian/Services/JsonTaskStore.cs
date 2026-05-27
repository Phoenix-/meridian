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
            TaskStoreData? data;
            using (var stream = File.OpenRead(FilePath))
                data = JsonSerializer.Deserialize(stream, StoreJsonContext.Default.TaskStoreData);
            if (data == null) return null;
            if (data.SchemaHash != TaskStoreData.CurrentSchemaHash)
            {
                // Drop the stale-shaped file so we don't re-deserialize it on
                // every load until a successful sync rewrites it.
                TryDelete(FilePath);
                return null;
            }
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
            data.SchemaHash = TaskStoreData.CurrentSchemaHash;
            using var stream = File.Create(FilePath);
            JsonSerializer.Serialize(stream, data, StoreJsonContext.Default.TaskStoreData);
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
