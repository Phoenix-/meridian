using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Meridian.Auth;

// Microsoft identity platform (v2.0) twin of GoogleOAuthClient. Public client:
// no client_secret anywhere, PKCE carries the proof. /common lets both work
// and personal Microsoft accounts sign in (the app registration allows both).
public static class MicrosoftOAuthClient
{
    internal const string ProviderName = "microsoft";

    private const string AuthEndpoint  = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
    private const string TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";

    private static readonly string[] Scopes =
    [
        // openid+profile+email put the account's email into the id_token so we
        // can build AccountId without a userinfo round-trip; offline_access is
        // what makes the token endpoint return a refresh_token at all.
        "openid", "profile", "email", "offline_access",
        // Read-write up front, same reasoning as the Google scopes: when
        // create/edit features land we don't force a re-consent.
        "https://graph.microsoft.com/Calendars.ReadWrite",
        "https://graph.microsoft.com/Tasks.ReadWrite",
    ];

    private static readonly HttpClient _http = new();

    // Runs the full PKCE browser flow, saves the token, returns AccountId.
    // loginHint pre-fills the account on re-auth, mirroring the Google client.
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
        var email = ResolveEmail(token.idToken);

        var id = new AccountId(ProviderName, email);
        TokenStore.Save(id, token.oauth);
        return id;
    }

    // One refresh in flight per account. A cold start fires a sync per
    // (calendar, year) in parallel, and Microsoft ROTATES refresh tokens —
    // two concurrent refreshes with the same stored token make the loser
    // invalid_grant, which would read as a bogus "session expired". (Google
    // doesn't need this: its refresh token is stable, a duplicate refresh is
    // merely wasted.)
    private static readonly ConcurrentDictionary<AccountId, SemaphoreSlim> _refreshLocks = new();

    // Returns a valid (possibly refreshed) access token for the given account.
    public static async Task<string> GetAccessTokenAsync(AccountId id, CancellationToken ct = default)
    {
        var token = TokenStore.Load(id)
            ?? throw new AccountAuthExpiredException(id, "no saved token");

        if (!token.IsExpired) return token.AccessToken;

        var gate = _refreshLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // The winner of the race has already refreshed and saved by the
            // time a queued caller gets here — re-read before refreshing.
            token = TokenStore.Load(id)
                ?? throw new AccountAuthExpiredException(id, "no saved token");
            if (!token.IsExpired) return token.AccessToken;

            var refreshed = await RefreshAsync(id, token.RefreshToken, ct);
            TokenStore.Save(id, refreshed);
            return refreshed.AccessToken;
        }
        finally { gate.Release(); }
    }

    // The v2.0 endpoint has no token-revocation API for public clients; the
    // user revokes consent at account.live.com/consent/Manage. Locally we just
    // drop the saved token.
    public static Task RevokeAsync(AccountId id, CancellationToken ct = default)
    {
        TokenStore.Delete(id);
        return Task.CompletedTask;
    }

    private static async Task<OAuthToken> RefreshAsync(AccountId id, string refreshToken, CancellationToken ct)
    {
        var resp = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent([
            new("client_id",     MicrosoftSecrets.ClientId),
            new("refresh_token", refreshToken),
            new("grant_type",    "refresh_token"),
            new("scope",         string.Join(" ", Scopes)),
        ]), ct);

        // invalid_grant == the refresh token can no longer be used (expired
        // 90-day sliding window, revoked consent, password change). Typed
        // signal so the caches stop hammering and the UI prompts for re-auth.
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
            // Microsoft rotates refresh tokens: each refresh response carries a
            // NEW one that restarts the 90-day sliding window. Persisting the
            // old token instead would eventually log the account out.
            RefreshToken = tr.RefreshToken ?? refreshToken,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(tr.ExpiresIn),
        };
    }

    private static async Task<(OAuthToken oauth, string idToken)> ExchangeCodeAsync(
        string code, string verifier, string redirectUri, CancellationToken ct)
    {
        var resp = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent([
            new("code",          code),
            new("client_id",     MicrosoftSecrets.ClientId),
            new("redirect_uri",  redirectUri),
            new("code_verifier", verifier),
            new("grant_type",    "authorization_code"),
            new("scope",         string.Join(" ", Scopes)),
        ]), ct);
        resp.EnsureSuccessStatusCode();

        var tr = await resp.Content.ReadFromJsonAsync(OAuthJsonContext.Default.TokenResponse, ct)
            ?? throw new InvalidOperationException("Empty token response");

        var oauth = new OAuthToken
        {
            AccessToken  = tr.AccessToken  ?? throw new InvalidOperationException("Missing access_token"),
            RefreshToken = tr.RefreshToken ?? throw new InvalidOperationException("Missing refresh_token"),
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(tr.ExpiresIn),
        };
        return (oauth, tr.IdToken ?? throw new InvalidOperationException("Missing id_token"));
    }

    // The id_token came straight from the token endpoint over TLS, so we read
    // its payload without signature validation. email is absent on some MSA
    // accounts; preferred_username is the login name (normally the email).
    private static string ResolveEmail(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2)
            throw new InvalidOperationException("Malformed id_token");

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload += (payload.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        var json = Convert.FromBase64String(payload);

        var claims = JsonSerializer.Deserialize(json, OAuthJsonContext.Default.IdTokenClaims);
        return claims?.Email ?? claims?.PreferredUsername
            ?? throw new InvalidOperationException("Could not resolve email from id_token");
    }

    private static string BuildAuthUrl(string challenge, string state, string redirectUri, string? loginHint)
    {
        var scope = Uri.EscapeDataString(string.Join(" ", Scopes));

        var url = $"{AuthEndpoint}" +
                  $"?client_id={Uri.EscapeDataString(MicrosoftSecrets.ClientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  $"&response_type=code" +
                  $"&scope={scope}" +
                  $"&code_challenge={challenge}" +
                  $"&code_challenge_method=S256" +
                  $"&state={Uri.EscapeDataString(state)}";

        // With a hint (re-auth) let SSO kick in silently where possible;
        // without one, always show the account picker so a second Microsoft
        // account can be added while the first is signed in to the browser.
        if (loginHint is not null)
            url += $"&login_hint={Uri.EscapeDataString(loginHint)}";
        else
            url += "&prompt=select_account";

        return url;
    }
}
