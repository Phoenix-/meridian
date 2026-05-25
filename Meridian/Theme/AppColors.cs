using Windows.UI;

namespace Meridian.Theme;

// Single source of truth for app palette. Calendar/event colors that come
// from Google are NOT here — those belong to per-calendar data.
internal static class AppColors
{
    // Per-account fallback palette (cycles through this list when an event
    // has no calendar-supplied color). Same five colors used for event
    // chips, week blocks, day blocks, month bands.
    public static readonly Color[] EventPalette =
    [
        Color.FromArgb(255, 26, 115, 232),   // Google blue
        Color.FromArgb(255, 52, 168, 83),    // Google green
        Color.FromArgb(255, 234, 67, 53),    // Google red
        Color.FromArgb(255, 251, 188, 4),    // Google yellow
        Color.FromArgb(255, 103, 58, 183),   // purple
    ];

    // Solid color for task chips (no Google data backing tasks' colors).
    public static readonly Color Task = Color.FromArgb(255, 70, 130, 180);

    // Brand accent — "today" circle, day-name highlight, today-column tint.
    public static readonly Color Accent = Color.FromArgb(255, 26, 115, 232);

    // Tints of Accent used for today's background wash. Alphas chosen
    // per-view to match the layout density (Day: full column, Week: per-day
    // column, MonthRow: full week cell).
    public static readonly Color AccentWashDay     = Color.FromArgb(10, 26, 115, 232);
    public static readonly Color AccentWashWeek    = Color.FromArgb(12, 26, 115, 232);
    public static readonly Color AccentWashMonth   = Color.FromArgb(15, 26, 115, 232);

    // "Now" marker line + dot + ongoing-event outline. Same hue is also
    // used for the expired-session badge/icon — both are "attention"
    // signals, kept unified deliberately.
    public static readonly Color Now      = Color.FromArgb(255, 234, 67, 53);
    public static readonly Color NowFaint = Color.FromArgb(80, 234, 67, 53);
}
