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

    public Task<List<CalendarEvent>> GetEventsAsync(
        AccountId id, DateTime from, DateTime to, CancellationToken ct = default) =>
        new GoogleApiClient(id).GetEventsAsync("primary", from, to, ct);

    public Task<Dictionary<string, DateTime>> GetTaskReminderTimesAsync(
        AccountId id, DateTime from, DateTime to, CancellationToken ct = default) =>
        new GoogleApiClient(id).GetTaskReminderTimesAsync(from, to, ct);

    public Task<List<TaskItem>> GetTasksAsync(AccountId id, CancellationToken ct = default) =>
        new GoogleApiClient(id).GetTasksAsync(ct);
}
