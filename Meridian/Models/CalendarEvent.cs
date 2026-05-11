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
    // Per-event color from Google's event palette (colorId 1..11). Optional.
    public string? Color { get; set; }
    // Hex background color of the owning calendar (e.g. "#7986cb"). Used as a
    // fallback when the event has no per-event color.
    public string? CalendarColor { get; set; }
    // Hex foreground color paired with CalendarColor by Google. When present
    // the UI uses it for chip text; otherwise it auto-picks black or white.
    public string? CalendarTextColor { get; set; }
    public string? AccountEmail { get; set; }
}
