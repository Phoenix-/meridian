using System.Text.Json;
using Meridian.Models;

namespace Meridian.Services;

internal static class DiskCache
{
    // Cached data older than this is ignored — force a fresh fetch.
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Meridian", "cache");

    private static string MonthPath(int year, int month) =>
        Path.Combine(CacheDir, $"{year}-{month:D2}.json");

    private static string TasksPath =>
        Path.Combine(CacheDir, "tasks.json");

    // ── Read ──────────────────────────────────────────────────────────────────

    public static MonthCacheData? ReadMonth(int year, int month)
    {
        try
        {
            var path = MonthPath(year, month);
            if (!File.Exists(path)) return null;

            using var stream = File.OpenRead(path);
            var data = JsonSerializer.Deserialize(stream, DiskCacheJsonContext.Default.MonthCacheData);
            if (data == null) return null;

            return DateTime.UtcNow - data.SavedAtUtc > MaxAge ? null : data;
        }
        catch { return null; }
    }

    public static TasksCacheData? ReadTasks()
    {
        try
        {
            var path = TasksPath;
            if (!File.Exists(path)) return null;

            using var stream = File.OpenRead(path);
            var data = JsonSerializer.Deserialize(stream, DiskCacheJsonContext.Default.TasksCacheData);
            if (data == null) return null;

            return DateTime.UtcNow - data.SavedAtUtc > MaxAge ? null : data;
        }
        catch { return null; }
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public static void WriteMonth(int year, int month, List<Meridian.Models.CalendarEvent> events)
    {
        try
        {
            EnsureDir();
            var data = new MonthCacheData
            {
                Year = year,
                Month = month,
                SavedAtUtc = DateTime.UtcNow,
                Events = events
            };
            using var stream = File.Create(MonthPath(year, month));
            JsonSerializer.Serialize(stream, data, DiskCacheJsonContext.Default.MonthCacheData);
        }
        catch { }
    }

    public static void WriteTasks(Dictionary<string, List<TaskItem>> tasksByAccount)
    {
        try
        {
            EnsureDir();
            var data = new TasksCacheData
            {
                SavedAtUtc = DateTime.UtcNow,
                TasksByAccount = tasksByAccount
            };
            using var stream = File.Create(TasksPath);
            JsonSerializer.Serialize(stream, data, DiskCacheJsonContext.Default.TasksCacheData);
        }
        catch { }
    }

    private static void EnsureDir() => Directory.CreateDirectory(CacheDir);
}
