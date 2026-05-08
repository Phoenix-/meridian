namespace Meridian.Models;

public sealed record CalendarSnapshot(
    List<CalendarEvent> Events,
    List<TaskItem> Tasks,
    bool IsComplete,
    string? ErrorMessage = null
);
