using Meridian.Models;

namespace Meridian.Auth;

public interface ICalendarProvider
{
    string ProviderName { get; }

    // Returns all accounts saved on disk for this provider.
    IEnumerable<AccountId> GetSavedAccounts();

    // Validates a saved account is still usable (token exists). No network.
    void LoadAccount(AccountId id);

    // Opens browser / login UI, saves token, returns the new AccountId.
    Task<AccountId> AddAccountAsync(CancellationToken ct = default);

    Task RevokeAccountAsync(AccountId id, CancellationToken ct = default);

    // ── Data ──────────────────────────────────────────────────────────────────

    Task<List<CalendarEvent>> GetEventsAsync(
        AccountId id, DateTime from, DateTime to, CancellationToken ct = default);

    // taskId -> reminder DateTime; returns empty dict if not supported.
    Task<Dictionary<string, DateTime>> GetTaskReminderTimesAsync(
        AccountId id, DateTime from, DateTime to, CancellationToken ct = default);

    Task<List<TaskItem>> GetTasksAsync(AccountId id, CancellationToken ct = default);
}
