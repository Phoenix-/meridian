using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Diagnostics;

namespace Meridian.Services;

// Remembers which "missed reminder" tags we've already surfaced as a catch-up
// toast, so a subsequent sync doesn't re-show the same event. Persists across
// app restarts — the whole point of the missed-reminder flow is to survive a
// closed laptop, so the dedupe state must too.
//
// Entries are evicted after RetentionWindow; a reminder we surfaced more than
// a day ago is no longer relevant anyway and the file shouldn't grow forever.
internal sealed class MissedRemindersTracker
{
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(1);

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Meridian", "missed-reminders.json");

    private Dictionary<string, DateTime> _shownAt;

    public MissedRemindersTracker()
    {
        _shownAt = Load();
        Prune(DateTime.UtcNow);
    }

    public bool WasShown(string tag) => _shownAt.ContainsKey(tag);

    public void MarkShown(IEnumerable<string> tags)
    {
        var now = DateTime.UtcNow;
        foreach (var tag in tags)
            _shownAt[tag] = now;
        Prune(now);
        Save();
    }

    private void Prune(DateTime nowUtc)
    {
        var cutoff = nowUtc - RetentionWindow;
        var stale = _shownAt.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
        foreach (var k in stale) _shownAt.Remove(k);
    }

    private static Dictionary<string, DateTime> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new(StringComparer.Ordinal);
            using var stream = File.OpenRead(StorePath);
            var data = JsonSerializer.Deserialize(stream, MissedRemindersJsonContext.Default.MissedRemindersData);
            return data?.ShownAt is { } map
                ? new Dictionary<string, DateTime>(map, StringComparer.Ordinal)
                : new(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, "MissedRemindersTracker.Load");
            return new(StringComparer.Ordinal);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            using var stream = File.Create(StorePath);
            JsonSerializer.Serialize(
                stream,
                new MissedRemindersData { ShownAt = _shownAt },
                MissedRemindersJsonContext.Default.MissedRemindersData);
        }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, "MissedRemindersTracker.Save");
        }
    }
}

internal sealed class MissedRemindersData
{
    public Dictionary<string, DateTime> ShownAt { get; set; } = new();
}

[JsonSerializable(typeof(MissedRemindersData))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class MissedRemindersJsonContext : JsonSerializerContext { }
