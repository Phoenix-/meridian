namespace Meridian.Auth;

// Thrown when a saved token can no longer be used to talk to the provider —
// refresh_token rejected (Google's 7-day expiry on Testing-status apps,
// revoked consent, deleted account) or the access_token endpoints answered
// 401/403 in a way that won't fix itself by retrying.
//
// Caches catch this specifically: the account is marked expired and its
// streams stop being synced until the user re-authorizes. Without this
// signal, an unrecoverable auth failure looks like a transient HTTP error
// and re-fires every 60-second poll tick (and every DataRefreshed → Request
// → EnsureYear cycle), spamming HttpRequestException without making any
// progress.
public sealed class AccountAuthExpiredException(AccountId account, string reason, Exception? inner = null)
    : Exception($"Auth expired for {account}: {reason}", inner)
{
    public AccountId Account { get; } = account;
    public string Reason { get; } = reason;
}
