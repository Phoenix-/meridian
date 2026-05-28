# Month-view multi-day bands: overlay + per-cell reserve, atomic per week

## Trap

Multi-day all-day events ("bands") in the month grid can't be laid out as
shared `Grid` rows. A single `Grid` with 7 star columns forces every column to
the same `RowDefinition` heights, so you cannot give each day its own band-block
height — and Google Calendar reserves band height *per day, up to that day's own
highest lane*, not the week max. Trying to do it with shared lane rows gives one
of two bugs we hit:

- uniform lane rows → empty reserved slot under days that have fewer bands
  (a visible gap pushing chips down);
- `GridLength.Auto` lane rows → a label-less continuation band has 0 desired
  height and the row collapses, so the band vanishes on its second week.

## Fix

Two cooperating pieces, both in [Meridian/Views/WeekRowControl.xaml.cs](../Meridian/Views/WeekRowControl.xaml.cs):

1. **Per-cell reserve.** `WeekGrid` is just `Auto` (dates) + `Star` (body). Each
   `DayCellControl.BodyPanel` is a 2-row grid: a transparent `_bandReserve`
   spacer (`Auto`) whose height = `thisDayMaxLane * laneHeight`, then `ChipsStack`
   (`Star`). The spacer reserves exactly this day's own band height → chips land
   below the day's bands, no cross-week empty slot. See `DayCellControl.SetBandReserve`.

2. **Band overlay.** A separate 7-star-column `Grid` (`_bandOverlay`) sits on top
   of the body row. Each visible band is ONE `Border` with `Grid.SetColumn`/
   `ColumnSpan` (continuous bar) and `Margin.Top = lane * laneHeight`. Column X/width
   come from the overlay's own columns (NOT a `Canvas` with hand-computed pixels);
   only the vertical offset is a margin, and it's uniform `lane*laneHeight`, so the
   bar stays straight across the days it spans.

`laneHeight` is probed once from a real labeled band (`MeasureBandHeight`) — do
NOT hardcode it, or the label clips when the OS text size is enlarged.

## Atomic, single budget, fit in Relayout

`WeekRowControl.Relayout` (driven by the overlay's `SizeChanged` + a `Loaded`
dispatcher enqueue, guarded by `_lastBodyHeight` against re-entrancy) decides which
bands are visible. Rules that match Google Calendar:

- A band is **atomic**: shown in full across all its days, or hidden entirely into
  each covered day's "+N ещё". Never truncated per-day.
- **Single height budget**: bands and chips compete for the cell's body height.
  `maxBandRows = floor(bodyHeight/laneHeight) - 1` always leaves ≥1 chip row, so the
  timeline is never fully buried under bands (this also kills the "should I reserve a
  +N slot" circularity — the bottom is always reserved).
- **"+N ещё" is one number per day** = hidden bands covering that day + chips that
  didn't fit. Hidden bands are seeded into the cell via `SetBandReserve(reserve,
  baseHiddenCount)`, NOT added as duplicate per-day chips (doing that was the bug
  where a hidden band showed as a repeated chip in every column it crossed).

## Rule of thumb

The reserve spacer and the overlay band MUST agree on `laneHeight` and on lane
index. `Relayout` is the single source of truth: it computes reserve heights from
body height + lane indices only (never from chip fit), so the dependency graph is
acyclic and `SizeChanged` can't loop. Behavior was verified against real Google
Calendar screenshots — that's the spec, not a guess.
