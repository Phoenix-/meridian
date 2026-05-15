using System.Text.Json.Serialization;

namespace Meridian.Models;

// One year-slice of events for a single (account, calendarId) stream.
// SyncToken is opaque and tied to the window [WindowStartUtc, WindowEndUtc) used
// at initial sync. If the window changes, the token is invalid and a fresh
// initial sync is required.
//
// SchemaVersion guards us against silently using stale on-disk events when the
// shape or fill-rules of a field change. The store treats a mismatch as a
// cache miss (the file is also deleted) and the next sync rewrites it from
// scratch. Bump when:
//   * a new field on CalendarEvent depends on data the previous build didn't
//     fetch (e.g. v2 added ReminderMinutes derived from calendar defaults +
//     the reminders block — old caches have it null for ~all events).
//   * an existing field changes semantics in a way old data can't be migrated.
public sealed class YearCacheData
{
    // Current schema (see comment above). Stored explicitly so a future
    // change to the constant invalidates existing caches on startup.
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; }

    public string AccountId { get; set; } = "";   // "provider:email"
    public string CalendarId { get; set; } = "";
    public int Year { get; set; }

    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }

    public string? SyncToken { get; set; }
    public DateTime SavedAtUtc { get; set; }

    public List<CalendarEvent> Events { get; set; } = [];
}

public sealed class TaskStoreData
{
    public DateTime SavedAtUtc { get; set; }
    // Key = account email
    public Dictionary<string, List<TaskItem>> TasksByAccount { get; set; } = [];
}

public sealed class CalendarListData
{
    public string AccountId { get; set; } = "";
    public DateTime SavedAtUtc { get; set; }
    public List<CalendarInfo> Calendars { get; set; } = [];
}

[JsonSerializable(typeof(YearCacheData))]
[JsonSerializable(typeof(TaskStoreData))]
[JsonSerializable(typeof(CalendarListData))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class StoreJsonContext : JsonSerializerContext { }
