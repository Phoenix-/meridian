using Windows.UI;

namespace Meridian.Models;

public sealed record EventChipData(
    string Title,
    Color Color,
    Color? TextColor,      // optional foreground; auto-picked from Color if null
    DateTime? StartTime,   // null for all-day events
    bool IsAllDay);
