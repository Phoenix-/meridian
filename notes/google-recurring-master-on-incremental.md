# Google Calendar incremental sync can return a recurring-series master

**Trap.** Initial sync uses `singleEvents=true`, so Google expands recurring
series into individual instances (each with an id like `<base>_20260518T...`).
The sync token from that response is supposed to keep the same expansion
behavior on incremental calls. In practice, when a **new recurring series is
created** in Google after we've stored a sync token, the next incremental
response returns the **master event** of that series — one item, base id with
no date suffix, carrying a `recurrence: ["RRULE:..."]` field — instead of the
expanded instances. Persisting this master as a regular upsert makes the
series appear in the UI as a single one-off event on its start date.

**Fix.** In `GoogleApiClient.PageSyncAsync` we detect items with a non-empty
`recurrence` array, skip them (so they don't poison the cache as one-offs),
and raise `EventSyncResult.MasterRecurrenceSeen`. `CalendarCache.DoSync`
treats that flag the same as a 410-expired sync token: re-run
`InitialSyncEventsAsync` for the year, which forces the server to expand the
series into instances again.

**Code:** `Meridian/Services/GoogleApiClient.cs` (EventDto.Recurrence,
EventSyncResult.MasterRecurrenceSeen, PageSyncAsync skip) and
`Meridian/Services/CalendarCache.cs` (DoSync re-init branch).
