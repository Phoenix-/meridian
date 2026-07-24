# Graph delta: occurrences arrive as skeletons

**Trap.** In `calendarView/delta` responses, instances of a recurring series
come with only `id`, `start`/`end`, `type: "occurrence"` and `seriesMasterId` —
no subject, location, attendees or reminders. The display fields live on the
`seriesMaster` item in the same batch. Naively mapping occurrences produces
"(без названия)" events (seen live 2026-07-24); naively mapping the master
produces one bogus event at the series' first slot.

**Fix.** Two-pass mapping in `GraphApiClient.PageSyncAsync`: collect all pages,
build a `masters` dict from `type == "seriesMaster"` items (never emitted as
events), then backfill null fields of each occurrence from its master
(`BackfillFromMaster` — occurrence-own values win, which is what makes
`type: "exception"` instances keep their overrides). If the master is absent
from the batch (single instance moved → delta resends just that occurrence),
it is fetched once via `GET /me/events/{seriesMasterId}`.

**Pointer.** `Meridian/Services/GraphApiClient.cs` — `PageSyncAsync`,
`BackfillFromMaster`, `FetchEventAsync`.
