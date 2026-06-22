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
            // The signed-in user's RSVP status drives the chip's look (muted /
            // strikethrough / pulsing outline), so a change to it must dirty the
            // hash — otherwise responding from the flyout leaves the old chip
            // (e.g. a still-pulsing needsAction border) on screen until the next
            // navigation rebuilds the view.
            hc.Add(SelfResponseStatus(e));
        }

        foreach (var t in Tasks
            .Where(t => TaskOverlaps(t, from, to))
            .OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            hc.Add(t.Id);
            hc.Add(t.Title);
            hc.Add(t.Due);
            hc.Add(t.Completed);
            hc.Add(t.AccountEmail);
        }

        return hc.ToHashCode();
    }

    // The signed-in user's responseStatus on this event, or null when they are
    // not an attendee. Mirrors EventActions.SelfAttendee but kept inline so the
    // Models layer stays free of a Services dependency.
    private static string? SelfResponseStatus(CalendarEvent e)
    {
        if (e.Attendees is not { Count: > 0 } guests) return null;
        foreach (var a in guests)
            if (a.IsSelf) return a.ResponseStatus;
        return null;
    }

    private static bool TaskOverlaps(TaskItem t, DateTime from, DateTime to)
    {
        if (t.Due is { } d)
        {
            var dt = d.ToDateTime(TimeOnly.MinValue);
            return dt >= from.Date && dt < to;
        }
        return false;
    }
}
