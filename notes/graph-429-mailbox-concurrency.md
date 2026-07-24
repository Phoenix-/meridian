# Graph: ~4 concurrent requests per mailbox, then 429

**Trap.** Outlook resources in Microsoft Graph allow only ~4 concurrent
requests per mailbox. `CalendarCache` fans sync out per (calendar, year), so
an ungated startup burst (3 calendars × 3 years + tasks + calendar list) trips
`429 Too Many Requests` on most of the streams instantly (seen live
2026-07-24). Google tolerates the same burst fine — this is Graph-specific.

**Fix.** All Graph requests go through `SendThrottledAsync`: a static
per-mailbox `SemaphoreSlim(3)` (3, not 4 — headroom for user actions like
RSVP) plus retry on 429 honoring the server's `Retry-After` header, max 4
attempts, then the error flows out as a transient `HttpRequestException` and
the next poll tick retries.

**Pointer.** `Meridian/Services/GraphApiClient.cs` — `_mailboxGates`,
`SendThrottledAsync`.
