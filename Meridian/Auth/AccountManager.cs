using Google.Apis.Auth.OAuth2;
using System.Collections.ObjectModel;

namespace Meridian.Auth;

public class AccountManager
{
    private readonly Dictionary<string, UserCredential> _credentials = [];

    public ObservableCollection<string> Accounts { get; } = [];

    public async Task LoadSavedAccountsAsync()
    {
        foreach (var email in GoogleAuthService.GetSavedAccounts())
        {
            if (_credentials.ContainsKey(email)) continue;
            var credential = await GoogleAuthService.LoadAccountAsync(email);
            _credentials[email] = credential;
            Accounts.Add(email);
        }
    }

    public async Task<string> AddAccountAsync()
    {
        var credential = await GoogleAuthService.AddAccountAsync();
        var email = await ResolveEmailAsync(credential);

        _credentials[email] = credential;
        if (!Accounts.Contains(email))
            Accounts.Add(email);

        return email;
    }

    public async Task RemoveAccountAsync(string email)
    {
        await GoogleAuthService.RevokeAccountAsync(email);
        _credentials.Remove(email);
        Accounts.Remove(email);
    }

    public IReadOnlyDictionary<string, UserCredential> Credentials => _credentials;

    private static async Task<string> ResolveEmailAsync(UserCredential credential)
    {
        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential.Token.AccessToken);
        var resp = await http.GetStringAsync("https://www.googleapis.com/oauth2/v3/userinfo");
        var doc = System.Text.Json.JsonDocument.Parse(resp);
        return doc.RootElement.GetProperty("email").GetString() ?? credential.UserId;
    }
}
