using Meridian.Models;

namespace Meridian.Services;

// Thin static bridge between the (static) event UI surfaces — the details
// flyout and the chip context menu — and the live CalendarCache that owns
// the data. The flyout/menu are shown via static helpers with no reference
// to the view model, so rather than thread the cache through every Show()/
// Apply() call, MainViewModel wires these delegates once at construction.
//
// CanRespond is a synchronous predicate used to decide whether to surface the
// Yes/No/Maybe affordance at all (the user is an attendee AND the owning
// calendar is writable). Respond performs the actual change: optimistic local
// update + PATCH + background sync, returning false if the server rejected it
// so the UI can revert. Both are null until MainViewModel is constructed; the
// UI treats null as "feature unavailable" and hides the affordance.
public static class EventActions
{
    public static Func<CalendarEvent, bool>? CanRespond { get; set; }

    public static Func<CalendarEvent, string, Task<bool>>? Respond { get; set; }

    // Response status values accepted by Google's Events API for an attendee.
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Tentative = "tentative";

    // The signed-in user's current response on an event, or null if they are
    // not in the attendee list (e.g. a non-guest organizer, or a personal
    // event with no guests). Used by both UI surfaces to highlight the active
    // choice and to gate visibility.
    public static EventAttendee? SelfAttendee(CalendarEvent ev)
    {
        if (ev.Attendees is not { Count: > 0 } guests) return null;
        foreach (var a in guests)
            if (a.IsSelf) return a;
        return null;
    }
}
