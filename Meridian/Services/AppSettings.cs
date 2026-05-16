namespace Meridian.Services;

// Parking lot for user-tweakable knobs that don't yet have a settings UI.
// Each field is a `const` (or static readonly when const isn't possible) so
// callers reference it by name — when the settings screen lands, these
// migrate to backing-store properties without touching call sites.
//
// TODO(settings-ui): wire to a real per-user store once 3-4+ knobs justify it.
internal static class AppSettings
{
    // Flash the taskbar button in amber when a reminder fires (or a missed-
    // reminders summary surfaces) and the main window isn't already focused.
    // Stops on window activation or toast click.
    public const bool FlashTaskbarOnReminder = true;
}
