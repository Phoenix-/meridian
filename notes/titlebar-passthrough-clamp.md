# Title-bar passthrough must be clamped to the system caption band

**Trap.** With `ExtendsContentIntoTitleBar = true` and a custom drag region set
via `SetTitleBar(AppTitleBar)`, we carve our own button strips back out as
`NonClientRegionKind.Passthrough` so the chrome doesn't eat their clicks. If
that passthrough rect happens to cross into the system caption-button band
(min/max/close), those system buttons go dead — no hover, no click — until
something forces a non-client recalc (a manual resize or move).

**Repro.** Click the taskbar icon to restore from minimized at >100% DPI. The
minimize button and the left half of maximize stop responding. Resizing the
window by a pixel revives them.

**Cause.** After taskbar-restore, `AppWindow.Changed` fires twice in quick
succession: first with `TitleBar.RightInset` still at the iconified value
(~60 DIP), then again once the real caption strip has been laid out
(~136 DIP). Between the two events, XAML can run a layout pass against the
narrow inset and place `RightButtons` at an X that's correct for inset=60. If
`UpdateTitleBarPassthrough` runs in that gap, the rect we hand to
`SetRegionRects` lands ~52 DIP under the system buttons — exactly one caption
button plus half of the next.

**Fix.** Clamp the passthrough rect's right edge to
`AppWindow.Size.Width − AppWindow.TitleBar.RightInset` before submitting it.
The clamp is cheap, runs every time, and makes the bug structurally
impossible regardless of whether XAML's layout pass has caught up. See
`AddPassthroughRect` and `UpdateTitleBarPassthrough` in `MainWindow.xaml.cs`.

**Why not "wait for layout".** Tried that path (re-apply on
`DidVisibilityChange`, on iconic→normal edge, with `SWP_FRAMECHANGED`, with a
size-nudge). None worked: by the time our hook ran, layout had already
landed but `RightInset` had moved underneath us, so the snapshot was always
stale. The clamp sidesteps the race entirely.
