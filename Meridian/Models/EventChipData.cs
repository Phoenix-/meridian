using Windows.UI;

namespace Meridian.Models;

public sealed record EventChipData(
    string Title,
    Color Color,
    DateTime? StartTime,   // null for all-day events
    bool IsAllDay);
