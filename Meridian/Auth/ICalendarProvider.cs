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

    // Re-runs the browser flow for an existing account (used when the saved
    // refresh token can no longer be used). Returns the AccountId of the
    // freshly authorized account — must match the supplied id; if Google
    // returns a different account the caller treats it as cancelled.
    Task<AccountId> ReauthenticateAccountAsync(AccountId id, CancellationToken ct = default);

    Task RevokeAccountAsync(AccountId id, CancellationToken ct = default);

    // ── Data ──────────────────────────────────────────────────────────────────

    // Full sync for a window. Returns events + a sync token bound to the window.
    // defaultPopupMinutes is the owning calendar's default popup reminders; the
    // provider uses it to resolve event.reminders.useDefault into concrete minutes.
    Task<EventSyncResult> InitialSyncEventsAsync(
        AccountId id, string calendarId, DateTime from, DateTime to,
        IReadOnlyList<int>? defaultPopupMinutes = null, CancellationToken ct = default);

    // Incremental sync using a previously-stored token. If the token expired
    // (HTTP 410), result.SyncTokenExpired == true and caller must re-init.
    Task<EventSyncResult> IncrementalSyncEventsAsync(
        AccountId id, string calendarId, string syncToken,
        IReadOnlyList<int>? defaultPopupMinutes = null, CancellationToken ct = default);

    // All calendars (own + shared + subscribed) visible to this account.
    Task<List<CalendarInfo>> GetCalendarsAsync(AccountId id, CancellationToken ct = default);

    Task<List<TaskItem>> GetTasksAsync(AccountId id, CancellationToken ct = default);
}
