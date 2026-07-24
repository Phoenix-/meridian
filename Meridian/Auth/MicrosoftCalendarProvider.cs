using Meridian.Models;
using Meridian.Services;

namespace Meridian.Auth;

public sealed class MicrosoftCalendarProvider : ICalendarProvider
{
    public string ProviderName => MicrosoftOAuthClient.ProviderName;

    public IEnumerable<AccountId> GetSavedAccounts() =>
        TokenStore.GetSavedAccounts(ProviderName);

    public void LoadAccount(AccountId id)
    {
        if (TokenStore.Load(id) is null)
            throw new InvalidOperationException($"No saved token for {id}");
    }

    public async Task<AccountId> AddAccountAsync(CancellationToken ct = default) =>
        await MicrosoftOAuthClient.AuthorizeAsync(ct: ct);

    public async Task<AccountId> ReauthenticateAccountAsync(AccountId id, CancellationToken ct = default) =>
        await MicrosoftOAuthClient.AuthorizeAsync(loginHint: id.Email, ct: ct);

    public Task RevokeAccountAsync(AccountId id, CancellationToken ct = default) =>
        MicrosoftOAuthClient.RevokeAsync(id, ct);

    public Task<EventSyncResult> InitialSyncEventsAsync(
        AccountId id, string calendarId, DateTime from, DateTime to,
        IReadOnlyList<int>? defaultPopupMinutes = null, CancellationToken ct = default) =>
        new GraphApiClient(id).InitialSyncEventsAsync(calendarId, from, to, defaultPopupMinutes, ct);

    public Task<EventSyncResult> IncrementalSyncEventsAsync(
        AccountId id, string calendarId, string syncToken,
        IReadOnlyList<int>? defaultPopupMinutes = null, CancellationToken ct = default) =>
        new GraphApiClient(id).IncrementalSyncEventsAsync(calendarId, syncToken, defaultPopupMinutes, ct);

    public Task<List<CalendarInfo>> GetCalendarsAsync(AccountId id, CancellationToken ct = default) =>
        new GraphApiClient(id).GetCalendarsAsync(ct);

    public Task<List<TaskItem>> GetTasksAsync(AccountId id, CancellationToken ct = default) =>
        new GraphApiClient(id).GetTasksAsync(ct);

    public Task SetMyResponseStatusAsync(
        AccountId id, string calendarId, string eventId,
        IReadOnlyList<EventAttendee> guests, IReadOnlyList<EventAttendee>? rooms,
        string status, CancellationToken ct = default) =>
        new GraphApiClient(id).SetMyResponseStatusAsync(calendarId, eventId, guests, rooms, status, ct);
}
