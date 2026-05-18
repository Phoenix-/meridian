using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Diagnostics;

namespace Meridian.Services;

// Persistent state for the reminder pipeline that must survive app restarts.
// Two collections, one file:
//
//   * ShownAt: event-keys we've already surfaced as a missed-reminder catch-up
//     toast. Prevents re-surfacing the same event on the next reconcile pass.
//
//   * ScheduledFireAt: per-reminder tags we've ever handed to WNP via
//     AddToSchedule, keyed by fireAt. Used to distinguish "WNP delivered this
//     and cleared it from its queue" from "we never knew about this event in
//     time". Without this, every successful delivery races a follow-up
//     reconcile that re-classifies the just-fired reminder as missed.
//
// Both collections are pruned by retention windows so the file doesn't grow
// without bound. Sizing: at ~30 reminders/day and an 8-day retention, the
// file settles around ~15-20 KB — small enough to load on every reconcile.
internal sealed class MissedRemindersTracker
{
    // Missed-toast dedupe lives only as long as it might still be relevant.
    private static readonly TimeSpan ShownRetention = TimeSpan.FromDays(1);

    // Scheduled-tag memory must outlive MissedWindow in ReminderScheduler so
    // an old fireAt being mistaken for "never scheduled" can be ruled out by
    // the tracker. A small head-room past 7 days keeps us safe from edge
    // cases (clock skew, last-minute timezone shifts).
    private static readonly TimeSpan ScheduledRetention = TimeSpan.FromDays(8);

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Meridian", "missed-reminders.json");

    private Dictionary<string, DateTime> _shownAt;
    private Dictionary<string, DateTime> _scheduledFireAt;
    private bool _dirty;

    public MissedRemindersTracker()
    {
        var data = Load();
        _shownAt = data.ShownAt ?? new(StringComparer.Ordinal);
        _scheduledFireAt = data.ScheduledFireAt ?? new(StringComparer.Ordinal);
        Prune(DateTime.UtcNow);
    }

    // ── Shown (missed-toast dedupe) ───────────────────────────────────────────

    public bool WasShown(string eventKey) => _shownAt.ContainsKey(eventKey);

    // MarkShown is rare (one call per missed-summary toast), so it flushes
    // synchronously. MarkScheduled is the high-frequency path that batches.
    public void MarkShown(IEnumerable<string> eventKeys)
    {
        var now = DateTime.UtcNow;
        foreach (var key in eventKeys)
            _shownAt[key] = now;
        _dirty = true;
        Flush();
    }

    // ── Scheduled (delivered-vs-never-known disambiguation) ───────────────────

    // True iff we've ever called AddToSchedule for this tag — i.e. the
    // reminder previously made it into WNP's queue. Absence in WNP's
    // current queue then means "delivered and cleared", not "never known".
    public bool WasScheduled(string tag) => _scheduledFireAt.ContainsKey(tag);

    // Does NOT flush — callers schedule in batches inside a reconcile pass;
    // Flush() is invoked once at the end to coalesce the writes.
    public void MarkScheduled(string tag, DateTime fireAt)
    {
        _scheduledFireAt[tag] = fireAt;
        _dirty = true;
    }

    // Persists pending changes. Cheap if nothing's dirty.
    public void Flush()
    {
        if (!_dirty) return;
        Prune(DateTime.UtcNow);
        Save();
        _dirty = false;
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void Prune(DateTime nowUtc)
    {
        var shownCutoff = nowUtc - ShownRetention;
        foreach (var k in _shownAt.Where(kv => kv.Value < shownCutoff).Select(kv => kv.Key).ToList())
            _shownAt.Remove(k);

        // Scheduled entries are pruned by their fireAt — once fireAt is far
        // enough in the past, no MissedWindow check can still reach it, so
        // the memory is redundant.
        var schedCutoff = nowUtc - ScheduledRetention;
        foreach (var k in _scheduledFireAt.Where(kv => kv.Value < schedCutoff).Select(kv => kv.Key).ToList())
            _scheduledFireAt.Remove(k);
    }

    private static MissedRemindersData Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new MissedRemindersData();
            using var stream = File.OpenRead(StorePath);
            return JsonSerializer.Deserialize(stream, MissedRemindersJsonContext.Default.MissedRemindersData)
                ?? new MissedRemindersData();
        }
        catch (Exception ex)
        {
            Log.Error("Toast", ex, "MissedRemindersTracker.Load");
            return new MissedRemindersData();
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
                new MissedRemindersData
                {
                    ShownAt = _shownAt,
                    ScheduledFireAt = _scheduledFireAt,
                },
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
    public Dictionary<string, DateTime>? ShownAt { get; set; }
    public Dictionary<string, DateTime>? ScheduledFireAt { get; set; }
}

[JsonSerializable(typeof(MissedRemindersData))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class MissedRemindersJsonContext : JsonSerializerContext { }
