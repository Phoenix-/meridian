using Meridian.Models;

namespace Meridian.Views;

public interface ICalendarView
{
    (DateTime From, DateTime To) GetRange();
    void NavigatePrevious();
    void NavigateNext();
    void ApplySnapshot(CalendarSnapshot snapshot);
}
