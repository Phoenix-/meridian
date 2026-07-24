# Microsoft accounts rotate refresh tokens

**Trap.** Unlike Google (stable refresh token for the account's lifetime),
the Microsoft identity platform returns a NEW `refresh_token` with every
refresh, restarting the 90-day sliding window. Two consequences:

1. Persisting the old token instead of the rotated one eventually logs the
   account out (the old token stops working).
2. Concurrent refreshes with the same stored token are destructive: the loser
   gets `invalid_grant` → surfaces as a bogus "session expired" for a healthy
   account. A cold start fires a sync per (calendar, year) in parallel, so
   this race is the *common* case, not an edge case.

**Fix.** `MicrosoftOAuthClient.RefreshAsync` saves `tr.RefreshToken ??
refreshToken`; `GetAccessTokenAsync` single-flights the refresh through a
per-account `SemaphoreSlim(1,1)` with a re-read of the store after acquiring
the lock (queued callers see the winner's fresh token and skip the network).
Google's client deliberately has no such lock — a duplicate refresh there is
merely wasted, not harmful.

**Pointer.** `Meridian/Auth/MicrosoftOAuthClient.cs` — `_refreshLocks`,
`GetAccessTokenAsync`, `RefreshAsync`.
