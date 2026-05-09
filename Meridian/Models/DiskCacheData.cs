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

public sealed class ViewStateData
{
    // "Day", "Week", "Month"
    public string View { get; set; } = "Day";
    public DateTime Date { get; set; } = DateTime.Today;
}

[JsonSerializable(typeof(MonthCacheData))]
[JsonSerializable(typeof(TasksCacheData))]
[JsonSerializable(typeof(ViewStateData))]
[JsonSerializable(typeof(List<CalendarEvent>))]
[JsonSerializable(typeof(List<TaskItem>))]
[JsonSerializable(typeof(Dictionary<string, List<TaskItem>>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class DiskCacheJsonContext : JsonSerializerContext { }
