# Directory photo cache: known tradeoffs

`DirectoryPhotoCache` (Stage 2 of the attendee-directory feature) caches profile
photo bytes on disk keyed by a SHA-256 of the photo URL, plus an in-memory
`BitmapImage` cache in `EventDetailsFlyout`. A code review flagged four design
tradeoffs that were **left in deliberately** — recording them so they aren't
"re-discovered" as bugs.

## 1. Stale photo can persist up to the name TTL (~30 days)

`DirectoryCache.PositiveTtl` is 30 days. A person's `PhotoUrl` is only re-fetched
when their directory entry re-resolves, i.e. after that TTL lapses. If a
colleague changes their photo, the old URL (and its cached bytes) keep serving
until then. Accepted: photos rarely change, and a month-stale avatar is cosmetic.
If this ever matters, shorten `PositiveTtl` or decouple photo refresh from it.

## 2. `PruneOld` is mtime-based and touch-on-read keeps live photos alive

`GetAsync` bumps the file's last-write time on every cache hit, and `PruneOld`
deletes only files untouched for `OrphanTtl` (30 days). Consequence: the sweep
only ever reaps photos nobody has opened in a month — it cannot evict a
still-rendered-but-outdated photo (see #1). This is intended: it's a
disk-space backstop, not a freshness mechanism. A reference-based sweep (delete
files whose hash isn't in any live `DirectoryPerson.PhotoUrl`) would be tighter
but adds coupling to `DirectoryCache`'s on-disk shape; not worth it yet.

## 3. Two separate in-flight coalescers

`DirectoryCache` (person resolve) and `DirectoryPhotoCache` (photo download) each
have their own `Dictionary<key, Task>` + `Lock` coalescer. They look similar but
aren't shared. Both now use the **producer-removes-the-key** pattern (the key is
removed only in the finally of the task that created it — NOT in every awaiter's
finally, which would let a slow awaiter evict a newer producer's entry and
re-fire the work). If a third coalescer ever appears, extract a shared helper.

## 4. Name and photo resolve on two paths per attendee row

Each row calls `DirectoryCache.ResolveAsync` twice — once for the name
(`ResolveDirectoryName`) and once for the photo (`ResolveDirectoryPhoto` →
`LoadPhotoAsync`). They're coalesced (same shared Task), so it's one network
call, but it's two awaits + two dispatcher hops. There's a synchronous warm path
in `ResolveDirectoryPhoto` (cached name + cached bitmap → paint during build, no
flicker), so the cost only shows on a cold/half-warm cache. A single
"resolve attendee → (name, photoBytes)" entry point would unify them; deferred
as not worth the churn.
