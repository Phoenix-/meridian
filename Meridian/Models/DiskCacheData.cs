using System.Text.Json.Serialization;

namespace Meridian.Models;

public sealed class ViewStateData
{
    // "Day", "Week", "Month"
    public string View { get; set; } = "Day";
    public DateTime Date { get; set; } = DateTime.Today;
}

public sealed class WindowStateData
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool Maximized { get; set; }
}

[JsonSerializable(typeof(ViewStateData))]
[JsonSerializable(typeof(WindowStateData))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class DiskCacheJsonContext : JsonSerializerContext { }
