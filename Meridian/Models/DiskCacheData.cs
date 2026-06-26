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

    // Reveal the diagnostic toast buttons (and any other dev-only affordances)
    // in the main window. Previously gated solely by a %APPDATA%\Meridian\
    // diag.enabled marker file; the file still works as an OR fallback, this is
    // the discoverable in-app switch. Off by default — it's a developer knob.
    public bool DebugFeaturesEnabled { get; set; } = false;

    // Master mute: when true, every toast is dropped — neither shown nor
    // scheduled. Exists so a debug build run alongside the installed Nightly
    // doesn't double-notify (reminders are scheduled "into the future", so even
    // a closed debug build's queued toasts would fire). Flipping it on also
    // clears our already-queued toasts so the OS won't deliver them later.
    public bool SuppressAllPopups { get; set; } = false;

    // Whether to register this app with the Windows notification platform
    // (AUMID registry block, toast-activator CLSID, Start Menu shortcut). On by
    // default. Turning it off stops re-registering on launch AND runs a cleanup
    // that removes everything a prior registration wrote — so a debug build can
    // bow out of the notification system entirely and leave the field to the
    // installed Nightly.
    public bool RegisterForNotifications { get; set; } = true;
}

[JsonSerializable(typeof(ViewStateData))]
[JsonSerializable(typeof(WindowStateData))]
[JsonSerializable(typeof(SettingsData))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class DiskCacheJsonContext : JsonSerializerContext { }
