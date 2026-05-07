using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;

namespace Meridian.Auth;

// Wraps LocalServerCodeReceiver and forces account picker + offline access on every auth.
public class PromptSelectAccountCodeReceiver : ICodeReceiver
{
    private readonly LocalServerCodeReceiver _inner = new();

    public string RedirectUri => _inner.RedirectUri;

    public Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(
        AuthorizationCodeRequestUrl url, CancellationToken ct)
    {
        if (url is GoogleAuthorizationCodeRequestUrl googleUrl)
        {
            googleUrl.Prompt = "select_account consent";
            googleUrl.AccessType = "offline";
        }
        return _inner.ReceiveCodeAsync(url, ct);
    }
}
