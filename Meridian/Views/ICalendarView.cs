using Meridian.Models;

namespace Meridian.Views;

public interface ICalendarView
{
    (DateTime From, DateTime To) GetRange();
    DateTime GetCurrentDate();
    string GetLabel();
    void NavigatePrevious();
    void NavigateNext();
    void NavigateToToday();
    void ApplySnapshot(CalendarSnapshot snapshot);
}
