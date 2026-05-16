using System.Text.Json;
using Meridian.Models;

namespace Meridian.Services;

// Persists window/view state. Event and task data live in JsonEventStore /
// JsonTaskStore — this class only handles UI preferences.
internal static class DiskCache
{
    private static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Meridian", "cache");

    private static string ViewStatePath =>
        Path.Combine(CacheDir, "viewstate.json");

    private static string WindowStatePath =>
        Path.Combine(CacheDir, "windowstate.json");

    public static ViewStateData? ReadViewState()
    {
        try
        {
            var path = ViewStatePath;
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, DiskCacheJsonContext.Default.ViewStateData);
        }
        catch { return null; }
    }

    public static void WriteViewState(string view, DateTime date, TimeSpan? focusTime)
    {
        try
        {
            EnsureDir();
            var data = new ViewStateData
            {
                View = view,
                Date = date,
                FocusTimeTicks = focusTime?.Ticks,
            };
            using var stream = File.Create(ViewStatePath);
            JsonSerializer.Serialize(stream, data, DiskCacheJsonContext.Default.ViewStateData);
        }
        catch { }
    }

    public static WindowStateData? ReadWindowState()
    {
        try
        {
            if (!File.Exists(WindowStatePath)) return null;
            using var stream = File.OpenRead(WindowStatePath);
            return JsonSerializer.Deserialize(stream, DiskCacheJsonContext.Default.WindowStateData);
        }
        catch { return null; }
    }

    public static void WriteWindowState(WindowStateData data)
    {
        try
        {
            EnsureDir();
            using var stream = File.Create(WindowStatePath);
            JsonSerializer.Serialize(stream, data, DiskCacheJsonContext.Default.WindowStateData);
        }
        catch { }
    }

    private static void EnsureDir() => Directory.CreateDirectory(CacheDir);
}
