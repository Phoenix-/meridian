using System.Text.Json.Serialization;

namespace Meridian.Models;

// One year-slice of events for a single (account, calendarId) stream.
// SyncToken is opaque and tied to the window [WindowStartUtc, WindowEndUtc) used
// at initial sync. If the window changes, the token is invalid and a fresh
// initial sync is required.
//
// SchemaHash guards us against silently using stale on-disk events when the
// shape of a persisted field changes. It is derived at compile time from the
// properties of CalendarEvent + this wrapper (see CacheSchemaGenerator), so any
// add/remove/retype moves the hash automatically — no manual version bump to
// forget. The store treats a mismatch as a cache miss (file is also deleted)
// and the next sync rewrites it from scratch.
[CacheSchema]
public sealed class YearCacheData
{
    // The on-disk shape is the event shape plus this wrapper's own fields, so the
    // guard combines both hashes. Stored explicitly so a shape change invalidates
    // existing caches on startup.
    public static string CurrentSchemaHash => SchemaHashes.CalendarEvent + SchemaHashes.YearCacheData;

    public string? SchemaHash { get; set; }

    public string AccountId { get; set; } = "";   // "provider:email"
    public string CalendarId { get; set; } = "";
    public int Year { get; set; }

    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }

    public string? SyncToken { get; set; }
    public DateTime SavedAtUtc { get; set; }

    public List<CalendarEvent> Events { get; set; } = [];
}

[CacheSchema]
public sealed class TaskStoreData
{
    public static string CurrentSchemaHash => SchemaHashes.TaskItem + SchemaHashes.TaskStoreData;

    public string? SchemaHash { get; set; }
    public DateTime SavedAtUtc { get; set; }
    // Key = account email
    public Dictionary<string, List<TaskItem>> TasksByAccount { get; set; } = [];
}

[CacheSchema]
public sealed class CalendarListData
{
    public static string CurrentSchemaHash => SchemaHashes.CalendarInfo + SchemaHashes.CalendarListData;

    public string? SchemaHash { get; set; }
    public string AccountId { get; set; } = "";
    public DateTime SavedAtUtc { get; set; }
    public List<CalendarInfo> Calendars { get; set; } = [];
}

[JsonSerializable(typeof(YearCacheData))]
[JsonSerializable(typeof(TaskStoreData))]
[JsonSerializable(typeof(CalendarListData))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class StoreJsonContext : JsonSerializerContext { }
