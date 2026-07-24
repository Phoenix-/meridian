using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Auth;

// Provider-independent plumbing for the desktop PKCE flow: loopback listener,
// code/state handshake, PKCE material, browser launch. Endpoint URLs, scopes
// and token exchange stay in the per-provider clients (GoogleOAuthClient,
// MicrosoftOAuthClient).
internal static class OAuthLoopback
{
    internal static string GenerateCodeVerifier() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    internal static string GenerateCodeChallenge(string verifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    internal static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string GenerateState() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    internal static void OpenBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    internal static int FindFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // Serves the "you can close this tab" page and returns the auth code once a
    // request with the expected state arrives. Requests with a wrong state are
    // answered and ignored (stray probes to the loopback port).
    internal static async Task<string> WaitForCodeAsync(HttpListener listener, string expectedState, CancellationToken ct)
    {
        while (true)
        {
            var ctx   = await listener.GetContextAsync().WaitAsync(ct);
            var query = ctx.Request.QueryString;

            var html = "<html><body><script>window.close();</script><p>Авторизация завершена, можно закрыть вкладку.</p></body></html>"u8.ToArray();
            ctx.Response.ContentType     = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = html.Length;
            await ctx.Response.OutputStream.WriteAsync(html, ct);
            ctx.Response.Close();

            if (query["error"] is { } err)
                throw new InvalidOperationException($"OAuth error: {err}");

            if (query["state"] != expectedState) continue;

            return query["code"] ?? throw new InvalidOperationException("OAuth response missing code");
        }
    }
}
