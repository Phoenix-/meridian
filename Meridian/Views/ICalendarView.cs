using Meridian.Models;
using Meridian.ViewModels;

namespace Meridian.Views;

// Navigation parameter passed to ContentFrame.Navigate for all calendar views.
// FocusTime is the time-of-day the target view should center its scroll on
// (null = let the view fall back to its default heuristic; ignored by MonthView).
public sealed record CalendarNavParam(MainViewModel ViewModel, DateTime Date, TimeSpan? FocusTime);

public interface ICalendarView
{
    (DateTime From, DateTime To) GetRange();
    DateTime GetCurrentDate();
    // Time-of-day at the center of the visible scroll area, or null for views that don't have a time axis (Month).
    TimeSpan? GetFocusTime();
    string GetLabel();
    void NavigatePrevious();
    void NavigateNext();
    void NavigateToToday();
    void ApplySnapshot(CalendarSnapshot snapshot);
}
