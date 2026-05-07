namespace Meridian.Models;

public class CalendarEvent
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsAllDay { get; set; }
    public string? CalendarId { get; set; }
    public string? Color { get; set; }
    public string? AccountEmail { get; set; }
}
