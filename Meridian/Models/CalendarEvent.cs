namespace Meridian.Models;

[CacheSchema]
public class CalendarEvent
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    // Free-form location string from Google. Can be an address, a room name,
    // or just an URL. Rendered as text or a hyperlink in the details flyout
    // based on whether it parses as an absolute URL.
    public string? Location { get; set; }
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
    // Human-readable title of the owning calendar (e.g. "Work", "Family").
    // Stamped onto the event at sync time so the details flyout can show it
    // without resolving CalendarId against the calendar list.
    public string? CalendarTitle { get; set; }
    // Direct link to the event in Google Calendar Web (the `htmlLink` field
    // from the Events API). Used by the "Open in Google Calendar" action.
    public string? HtmlLink { get; set; }
    // Minutes-before-start at which a popup reminder should fire. Populated from
    // Google's event.reminders.overrides (method=popup) when useDefault is false,
    // and from the owning calendar's defaultReminders otherwise. Empty/null = no
    // reminder. Email-method reminders are intentionally ignored — Google itself
    // delivers those.
    public List<int>? ReminderMinutes { get; set; }
    // Join URL for a Google Meet video conference attached to this event. We
    // pick the entryPoint with entryPointType == "video"; phone/sip entries
    // are dropped. Null when the event has no conferenceData or no video entry.
    public string? MeetJoinUrl { get; set; }
    // People invited to the event. Resource attendees (rooms/equipment) are
    // split out into Rooms. Empty/null = no guests.
    public List<EventAttendee>? Attendees { get; set; }
    // Resource attendees (meeting rooms, equipment). Kept separate so the UI
    // can render them as their own section.
    public List<EventAttendee>? Rooms { get; set; }
}

[CacheSchema]
public class EventAttendee
{
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    // One of: "accepted", "declined", "tentative", "needsAction". Other values
    // (or null) render as needsAction.
    public string? ResponseStatus { get; set; }
    public bool IsOrganizer { get; set; }
    // True when this attendee entry represents the signed-in user's own
    // calendar copy. Used to bubble "You" to the top of the list.
    public bool IsSelf { get; set; }
    public bool IsOptional { get; set; }
}
