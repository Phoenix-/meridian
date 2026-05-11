using System.Text.Json.Serialization;

namespace Meridian.Models;

// One year-slice of events for a single (account, calendarId) stream.
// SyncToken is opaque and tied to the window [WindowStartUtc, WindowEndUtc) used
// at initial sync. If the window changes, the token is invalid and a fresh
// initial sync is required.
public sealed class YearCacheData
{
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
