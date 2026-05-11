using Meridian.Models;
using Meridian.Services;

namespace Meridian.Auth;

public sealed class GoogleCalendarProvider : ICalendarProvider
{
    public string ProviderName => GoogleOAuthClient.ProviderName;

    public IEnumerable<AccountId> GetSavedAccounts() =>
        TokenStore.GetSavedAccounts(ProviderName);

    public void LoadAccount(AccountId id)
    {
        if (TokenStore.Load(id) is null)
            throw new InvalidOperationException($"No saved token for {id}");
    }

    public async Task<AccountId> AddAccountAsync(CancellationToken ct = default) =>
        await GoogleOAuthClient.AuthorizeAsync(ct);

    public Task RevokeAccountAsync(AccountId id, CancellationToken ct = default) =>
        GoogleOAuthClient.RevokeAsync(id, ct);

    public string PrimaryCalendarId => "primary";

    public Task<EventSyncResult> InitialSyncEventsAsync(
        AccountId id, string calendarId, DateTime from, DateTime to, CancellationToken ct = default) =>
        new GoogleApiClient(id).InitialSyncEventsAsync(calendarId, from, to, ct);

    public Task<EventSyncResult> IncrementalSyncEventsAsync(
        AccountId id, string calendarId, string syncToken, CancellationToken ct = default) =>
        new GoogleApiClient(id).IncrementalSyncEventsAsync(calendarId, syncToken, ct);

    public Task<Dictionary<string, DateTime>> GetTaskReminderTimesAsync(
        AccountId id, DateTime from, DateTime to, CancellationToken ct = default) =>
        new GoogleApiClient(id).GetTaskReminderTimesAsync(from, to, ct);

    public Task<List<TaskItem>> GetTasksAsync(AccountId id, CancellationToken ct = default) =>
        new GoogleApiClient(id).GetTasksAsync(ct);
}
