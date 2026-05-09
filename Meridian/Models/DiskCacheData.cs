using System.Text.Json.Serialization;

namespace Meridian.Models;

public sealed class MonthCacheData
{
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public List<CalendarEvent> Events { get; set; } = [];
}

public sealed class TasksCacheData
{
    public DateTime SavedAtUtc { get; set; }
    // Key = account email
    public Dictionary<string, List<TaskItem>> TasksByAccount { get; set; } = [];
}

[JsonSerializable(typeof(MonthCacheData))]
[JsonSerializable(typeof(TasksCacheData))]
[JsonSerializable(typeof(List<CalendarEvent>))]
[JsonSerializable(typeof(List<TaskItem>))]
[JsonSerializable(typeof(Dictionary<string, List<TaskItem>>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class DiskCacheJsonContext : JsonSerializerContext { }
