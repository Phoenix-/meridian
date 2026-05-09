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

    public async Task RemoveAccountAsync(AccountId id)
    {
        await providers.Get(id).RevokeAccountAsync(id);
        Accounts.Remove(id);
    }

    public IReadOnlyList<AccountId> Ids => Accounts;
}
