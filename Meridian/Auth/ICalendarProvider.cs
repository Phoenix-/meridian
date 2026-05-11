using Meridian.Models;
using Meridian.Services;

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

    // Full sync for a window. Returns events + a sync token bound to the window.
    Task<EventSyncResult> InitialSyncEventsAsync(
        AccountId id, string calendarId, DateTime from, DateTime to, CancellationToken ct = default);

    // Incremental sync using a previously-stored token. If the token expired
    // (HTTP 410), result.SyncTokenExpired == true and caller must re-init.
    Task<EventSyncResult> IncrementalSyncEventsAsync(
        AccountId id, string calendarId, string syncToken, CancellationToken ct = default);

    // The calendarId to use for the primary calendar of this account. For Google
    // it's the literal "primary"; placeholder for multi-calendar later.
    string PrimaryCalendarId { get; }

    // taskId -> reminder DateTime; returns empty dict if not supported.
    Task<Dictionary<string, DateTime>> GetTaskReminderTimesAsync(
        AccountId id, DateTime from, DateTime to, CancellationToken ct = default);

    Task<List<TaskItem>> GetTasksAsync(AccountId id, CancellationToken ct = default);
}
