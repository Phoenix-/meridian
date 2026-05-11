namespace Meridian.Models;

public sealed class CalendarInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    // Hex background color from Google (e.g. "#7986cb"), nullable when missing.
    public string? BackgroundColor { get; set; }
    // Hex foreground/text color paired with BackgroundColor by Google. Usually
    // a darker/more saturated variant that reads cleanly against the bg.
    public string? ForegroundColor { get; set; }
    // True when the user has the calendar enabled in Google Calendar Web.
    // Meridian honors this directly; calendars with Selected=false are skipped.
    public bool Selected { get; set; }
    public string AccessRole { get; set; } = "";
}
