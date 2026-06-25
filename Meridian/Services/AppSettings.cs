namespace Meridian.Services;

// Named accessors for user-tweakable knobs. Call sites reference these by name;
// the values are backed by SettingsStore (settings.json), so a change made in
// the settings window takes effect on the next read without a restart.
//
// This indirection keeps the call sites (TaskbarFlasher, OngoingEventIndicator)
// oblivious to the storage mechanism — they just read a flag.
internal static class AppSettings
{
    // Flash the taskbar button in amber when a reminder fires (or a missed-
    // reminders summary surfaces) and the main window isn't already focused.
    // Stops on window activation or toast click.
    public static bool FlashTaskbarOnReminder => SettingsStore.FlashTaskbarOnReminder;

    // Paint a coloured dot on the taskbar button while a timed event is in
    // progress, in the calendar's colour. Complements the reminder flash —
    // the flash fires *at* reminder time, the overlay shows event *state*.
    public static bool ShowOngoingEventOverlay => SettingsStore.ShowOngoingEventOverlay;
}
