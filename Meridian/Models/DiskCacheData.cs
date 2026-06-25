using System.Text.Json.Serialization;

namespace Meridian.Models;

public sealed class ViewStateData
{
    // "Day", "Week", "Month"
    public string View { get; set; } = "Day";
    public DateTime Date { get; set; } = DateTime.Today;
    // Scroll focus time-of-day as TimeSpan.Ticks. Null for Month or when not yet known.
    public long? FocusTimeTicks { get; set; }
}

public sealed class WindowStateData
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool Maximized { get; set; }
}

// User-tweakable knobs surfaced by the settings window. Each property carries
// its shipped default so an absent file (or a field missing from an older
// file) keeps the prior behaviour — both flags default to true, matching the
// old AppSettings consts they replaced.
public sealed class SettingsData
{
    // Flash the taskbar button in amber when a reminder fires and the main
    // window isn't focused.
    public bool FlashTaskbarOnReminder { get; set; } = true;

    // Paint a coloured dot on the taskbar button while a timed event is in
    // progress, in the calendar's colour.
    public bool ShowOngoingEventOverlay { get; set; } = true;
}

[JsonSerializable(typeof(ViewStateData))]
[JsonSerializable(typeof(WindowStateData))]
[JsonSerializable(typeof(SettingsData))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class DiskCacheJsonContext : JsonSerializerContext { }
