using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Meridian.Auth;

internal class TokenResponse
{
    [JsonPropertyName("access_token")]  public string? AccessToken  { get; set; }
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")]    public int     ExpiresIn    { get; set; }
    [JsonPropertyName("id_token")]      public string? IdToken      { get; set; }
}

internal class UserinfoResponse
{
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("sub")]   public string? Sub   { get; set; }
}

// Claims read from an id_token payload (Microsoft flow resolves the account
// email from these instead of a userinfo round-trip).
internal class IdTokenClaims
{
    [JsonPropertyName("email")]              public string? Email             { get; set; }
    [JsonPropertyName("preferred_username")] public string? PreferredUsername { get; set; }
}

[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(UserinfoResponse))]
[JsonSerializable(typeof(IdTokenClaims))]
internal partial class OAuthJsonContext : JsonSerializerContext { }

public static class GoogleOAuthClient
{
    internal const string ProviderName = "google";

    private const string TokenEndpoint    = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint   = "https://oauth2.googleapis.com/revoke";
    private const string UserinfoEndpoint = "https://www.googleapis.com/oauth2/v3/userinfo";
    private const string AuthEndpoint     = "https://accounts.google.com/o/oauth2/v2/auth";

    private static readonly string[] Scopes =
    [
        // Full read-write, requested up front (ahead of read-write UI) so that
        // when create/edit features land we don't force every user to re-consent.
        // Safe while unverified; both are `sensitive`, not `restricted` (no CASA).
        // See gh issue #8 for the verification plan.
        "https://www.googleapis.com/auth/calendar",
        "https://www.googleapis.com/auth/tasks",
        // Read-only org-directory access for resolving attendee names/photos
        // from the Workspace directory (People API searchDirectoryPeople).
        // Sensitive, not restricted (no CASA). Adding it changes the consent
        // set, so existing users get re-prompted once — the typed re-auth path
        // (AccountAuthExpiredException + "!" badge) already covers that.
        "https://www.googleapis.com/auth/directory.readonly",
        "email", "profile",
    ];

    private static readonly HttpClient _http = new();

    // Runs full PKCE browser flow, saves token, returns AccountId.
    // When loginHint is non-null Google pre-selects that account and skips the
    // chooser — used for re-auth ("session expired for X, re-login") so the
    // user doesn't have to spot their own email in the picker.
    public static async Task<AccountId> AuthorizeAsync(string? loginHint = null, CancellationToken ct = default)
    {
        var verifier    = OAuthLoopback.GenerateCodeVerifier();
        var challenge   = OAuthLoopback.GenerateCodeChallenge(verifier);

        using var listener = new HttpListener();
        var port        = OAuthLoopback.FindFreePort();
        var redirectUri = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var state   = OAuthLoopback.GenerateState();
        var authUrl = BuildAuthUrl(challenge, state, redirectUri, loginHint);
        OAuthLoopback.OpenBrowser(authUrl);

        var code  = await OAuthLoopback.WaitForCodeAsync(listener, state, ct);
        var token = await ExchangeCodeAsync(code, verifier, redirectUri, ct);
        var email = await ResolveEmailAsync(token.AccessToken, ct);

        var id = new AccountId(ProviderName, email);
        TokenStore.Save(id, token);
        return id;
    }

    // Returns a valid (possibly refreshed) access token for the given account.
    public static async Task<string> GetAccessTokenAsync(AccountId id, CancellationToken ct = default)
    {
        var token = TokenStore.Load(id)
            ?? throw new AccountAuthExpiredException(id, "no saved token");

        if (!token.IsExpired) return token.AccessToken;

        var refreshed = await RefreshAsync(id, token.RefreshToken, ct);
        TokenStore.Save(id, refreshed);
        return refreshed.AccessToken;
    }

    public static async Task RevokeAsync(AccountId id, CancellationToken ct = default)
    {
        var token = TokenStore.Load(id);
        if (token is null) return;

        await _http.PostAsync(RevokeEndpoint,
            new FormUrlEncodedContent([new("token", token.RefreshToken)]), ct);

        TokenStore.Delete(id);
    }

    public static async Task<string> ResolveEmailAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UserinfoEndpoint);
        req.Headers.Authorization = new("Bearer", accessToken);
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var info = await resp.Content.ReadFromJsonAsync(OAuthJsonContext.Default.UserinfoResponse, ct);
        return info?.Email ?? info?.Sub
            ?? throw new InvalidOperationException("Could not resolve email from userinfo");
    }

    private static async Task<OAuthToken> RefreshAsync(AccountId id, string refreshToken, CancellationToken ct)
    {
        var resp = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent([
            new("client_id",     GoogleSecrets.ClientId),
            new("client_secret", GoogleSecrets.ClientSecret),
            new("refresh_token", refreshToken),
            new("grant_type",    "refresh_token"),
        ]), ct);

        // Google answers with 400 + {"error":"invalid_grant"} when the refresh
        // token is no longer usable — most commonly the 7-day expiry on apps
        // still in Testing publishing status, but also revoked consent or a
        // deleted account. Surface this as a typed signal so the caches can
        // stop hammering and the UI can prompt for re-auth.
        if (!resp.IsSuccessStatusCode)
        {
            string body = "";
            try { body = await resp.Content.ReadAsStringAsync(ct); } catch { }
            if ((int)resp.StatusCode == 400 && body.Contains("invalid_grant", StringComparison.Ordinal))
                throw new AccountAuthExpiredException(id, $"refresh: invalid_grant ({body})");
            resp.EnsureSuccessStatusCode();
        }

        var tr = await resp.Content.ReadFromJsonAsync(OAuthJsonContext.Default.TokenResponse, ct)
            ?? throw new InvalidOperationException("Empty token response");

        return new OAuthToken
        {
            AccessToken  = tr.AccessToken ?? throw new InvalidOperationException("Missing access_token"),
            RefreshToken = refreshToken,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(tr.ExpiresIn),
        };
    }

    private static async Task<OAuthToken> ExchangeCodeAsync(
        string code, string verifier, string redirectUri, CancellationToken ct)
    {
        var resp = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent([
            new("code",          code),
            new("client_id",     GoogleSecrets.ClientId),
            new("client_secret", GoogleSecrets.ClientSecret),
            new("redirect_uri",  redirectUri),
            new("code_verifier", verifier),
            new("grant_type",    "authorization_code"),
        ]), ct);
        resp.EnsureSuccessStatusCode();

        var tr = await resp.Content.ReadFromJsonAsync(OAuthJsonContext.Default.TokenResponse, ct)
            ?? throw new InvalidOperationException("Empty token response");

        return new OAuthToken
        {
            AccessToken  = tr.AccessToken  ?? throw new InvalidOperationException("Missing access_token"),
            RefreshToken = tr.RefreshToken ?? throw new InvalidOperationException("Missing refresh_token"),
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(tr.ExpiresIn),
        };
    }

    private static string BuildAuthUrl(string challenge, string state, string redirectUri, string? loginHint)
    {
        var scope = Uri.EscapeDataString(string.Join(" ", Scopes));
        // With a login_hint we skip the account picker and only ask for consent
        // (which Google won't re-prompt for if the user already consented and
        // the refresh token simply expired — the browser round-trip stays fast).
        var prompt = loginHint is null ? "select_account%20consent" : "consent";

        var url = $"{AuthEndpoint}" +
                  $"?client_id={Uri.EscapeDataString(GoogleSecrets.ClientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  $"&response_type=code" +
                  $"&scope={scope}" +
                  $"&code_challenge={challenge}" +
                  $"&code_challenge_method=S256" +
                  $"&state={Uri.EscapeDataString(state)}" +
                  $"&access_type=offline" +
                  $"&prompt={prompt}";

        if (loginHint is not null)
            url += $"&login_hint={Uri.EscapeDataString(loginHint)}";

        return url;
    }
}
