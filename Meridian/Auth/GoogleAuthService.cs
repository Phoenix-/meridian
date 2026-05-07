using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Tasks.v1;
using Google.Apis.Util.Store;

namespace Meridian.Auth;

public class GoogleAuthService
{
    public static readonly string[] Scopes =
    [
        CalendarService.Scope.CalendarReadonly,
        TasksService.Scope.TasksReadonly,
    ];

    private static readonly string TokensRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Meridian", "tokens");

    // Авторизует новый аккаунт, всегда показывая окно выбора аккаунта.
    public static async Task<UserCredential> AddAccountAsync(CancellationToken ct = default)
    {
        var secrets = new ClientSecrets { ClientId = GoogleSecrets.ClientId, ClientSecret = GoogleSecrets.ClientSecret };
        var tempId = Guid.NewGuid().ToString();
        var tempStore = new FileDataStore(Path.Combine(TokensRoot, tempId), fullPath: true);

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets, Scopes, "user", ct, tempStore,
            new PromptSelectAccountCodeReceiver());

        var email = await ResolveEmailAsync(credential, ct);
        var finalPath = Path.Combine(TokensRoot, email);

        // Переносим токен в папку с именем аккаунта
        if (Directory.Exists(Path.Combine(TokensRoot, tempId)))
        {
            if (Directory.Exists(finalPath))
                Directory.Delete(finalPath, recursive: true);
            Directory.Move(Path.Combine(TokensRoot, tempId), finalPath);
        }

        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets, Scopes, "user", ct,
            new FileDataStore(finalPath, fullPath: true));
    }

    // Тихо восстанавливает credential из сохранённого токена.
    public static async Task<UserCredential> LoadAccountAsync(string email, CancellationToken ct = default)
    {
        var secrets = new ClientSecrets { ClientId = GoogleSecrets.ClientId, ClientSecret = GoogleSecrets.ClientSecret };
        var path = Path.Combine(TokensRoot, email);
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets, Scopes, "user", ct,
            new FileDataStore(path, fullPath: true));
    }

    public static async Task RevokeAccountAsync(string email)
    {
        var credential = await LoadAccountAsync(email);
        await credential.RevokeTokenAsync(CancellationToken.None);
        var path = Path.Combine(TokensRoot, email);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    public static IEnumerable<string> GetSavedAccounts()
    {
        if (!Directory.Exists(TokensRoot)) return [];
        return Directory.GetDirectories(TokensRoot).Select(Path.GetFileName).OfType<string>();
    }

    private static async Task<string> ResolveEmailAsync(UserCredential credential, CancellationToken ct)
    {
        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential.Token.AccessToken);
        var resp = await http.GetStringAsync("https://www.googleapis.com/oauth2/v3/userinfo", ct);
        var doc = System.Text.Json.JsonDocument.Parse(resp);
        return doc.RootElement.GetProperty("email").GetString() ?? credential.UserId;
    }
}
