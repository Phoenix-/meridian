using Meridian.Auth;
using Meridian.Models;

namespace Meridian.Services;

// Persists per-(account, calendar, year) event streams plus their sync tokens.
// Operations are file-level, not row-level, mirroring the JSON implementation.
// A future SQLite implementation can satisfy the same contract.
public interface IEventStore
{
    YearCacheData? Load(AccountId account, string calendarId, int year);

    void Save(YearCacheData data);

    void Delete(AccountId account, string calendarId, int year);
}
