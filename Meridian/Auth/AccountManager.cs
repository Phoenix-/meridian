using System.Collections.ObjectModel;

namespace Meridian.Auth;

public class AccountManager(ProviderRegistry providers)
{
    public ObservableCollection<AccountId> Accounts { get; } = [];

    public async Task LoadSavedAccountsAsync()
    {
        foreach (var provider in providers.All)
        {
            foreach (var id in provider.GetSavedAccounts())
            {
                if (Accounts.Contains(id)) continue;
                provider.LoadAccount(id);
                Accounts.Add(id);
            }
        }
        await Task.CompletedTask;
    }

    public async Task<AccountId> AddAccountAsync(string providerName, CancellationToken ct = default)
    {
        var id = await providers.Get(providerName).AddAccountAsync(ct);
        if (!Accounts.Contains(id))
            Accounts.Add(id);
        return id;
    }

    // Returns the freshly authorized AccountId. If the user picked a different
    // Google account in the browser (rare with login_hint, but possible), the
    // returned id may not equal the requested one — the caller decides whether
    // that counts as success or cancellation.
    public async Task<AccountId> ReauthenticateAccountAsync(AccountId id, CancellationToken ct = default)
    {
        var refreshed = await providers.Get(id).ReauthenticateAccountAsync(id, ct);
        if (!Accounts.Contains(refreshed))
            Accounts.Add(refreshed);
        return refreshed;
    }

    public async Task RemoveAccountAsync(AccountId id)
    {
        await providers.Get(id).RevokeAccountAsync(id);
        Accounts.Remove(id);
    }

    public IReadOnlyList<AccountId> Ids => Accounts;
}
