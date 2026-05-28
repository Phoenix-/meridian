using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.UiTests.Fixtures;

// Mirrors Meridian.Services.DiskCache.ViewStateData on disk. Three fields,
// camelCase JSON — duplicated locally so the test project doesn't have to
// take a ProjectReference on Meridian (which would drag in its source
// generator + secrets-generation build step).
//
// If the schema in DiskCache.cs ever changes, update this DTO to match.
internal sealed class ViewState
{
    [JsonPropertyName("view")]
    public string View { get; set; } = "Day";

    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; set; }

    [JsonPropertyName("focusTimeTicks")]
    public long? FocusTimeTicks { get; set; }

    // Write a viewstate.json to the given path (which lives under a per-test
    // isolated data dir). Creates the cache directory if needed.
    public static void Write(string path, string view, DateTime date)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var state = new ViewState
        {
            View = view,
            Date = new DateTimeOffset(date.Date, TimeZoneInfo.Local.GetUtcOffset(date)),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(state));
    }
}
