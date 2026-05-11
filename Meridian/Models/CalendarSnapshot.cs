namespace Meridian.Models;

public sealed record CalendarSnapshot(
    List<CalendarEvent> Events,
    List<TaskItem> Tasks,
    bool IsComplete,
    string? ErrorMessage = null
)
{
    /// Stable content hash over the events and tasks that overlap [from, to).
    /// Used by views to skip Rebuild when polling produced no visible changes.
    /// Hash is process-local — never persist it.
    public int ContentHash(DateTime from, DateTime to)
    {
        var hc = new HashCode();
        hc.Add(ErrorMessage);

        // Sort by Id so order of incoming lists doesn't affect the hash.
        foreach (var e in Events
            .Where(e => e.Start < to && e.End > from)
            .OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            hc.Add(e.Id);
            hc.Add(e.Title);
            hc.Add(e.Start);
            hc.Add(e.End);
            hc.Add(e.IsAllDay);
            hc.Add(e.Color);
            hc.Add(e.CalendarColor);
            hc.Add(e.CalendarTextColor);
            hc.Add(e.AccountEmail);
        }

        foreach (var t in Tasks
            .Where(t => TaskOverlaps(t, from, to))
            .OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            hc.Add(t.Id);
            hc.Add(t.Title);
            hc.Add(t.Due);
            hc.Add(t.ReminderTime);
            hc.Add(t.Completed);
            hc.Add(t.AccountEmail);
        }

        return hc.ToHashCode();
    }

    private static bool TaskOverlaps(TaskItem t, DateTime from, DateTime to)
    {
        if (t.ReminderTime is { } r) return r >= from && r < to;
        if (t.Due is { } d)
        {
            var dt = d.ToDateTime(TimeOnly.MinValue);
            return dt >= from.Date && dt < to;
        }
        return false;
    }
}
