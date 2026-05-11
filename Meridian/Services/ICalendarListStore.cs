using Meridian.Auth;
using Meridian.Models;

namespace Meridian.Services;

// Persists the list of calendars for an account. Lists change rarely, so this
// is a one-file-per-account snapshot rather than a per-stream store.
public interface ICalendarListStore
{
    CalendarListData? Load(AccountId account);

    void Save(CalendarListData data);

    void Delete(AccountId account);
}
