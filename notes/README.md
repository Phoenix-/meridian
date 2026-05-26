# Notes

Gotchas and non-obvious behaviors discovered while working on Meridian.
One file per trap. Keep entries short: the trap, the fix, and a pointer into
the code so future-you can find the guard.

## Index

- [google-recurring-master-on-incremental.md](google-recurring-master-on-incremental.md) — incremental sync returns a recurring-series master instead of expanded instances; force initial re-sync when seen.
- [titlebar-passthrough-clamp.md](titlebar-passthrough-clamp.md) — custom-titlebar passthrough rects must be clamped to `WinW − RightInset` or they kill system caption buttons after taskbar-restore.
- [aot-visualtreehelper-before-layout.md](aot-visualtreehelper-before-layout.md) — `VisualTreeHelper` walks return null in NativeAOT before first layout; name elements in XAML instead of searching for them.
- [flyout-showat-needs-xamlroot-in-canvas.md](flyout-showat-needs-xamlroot-in-canvas.md) — `Flyout/MenuFlyout.ShowAt` throws `ArgumentException` for targets inside a `Canvas`; set `XamlRoot` from the target first.
- [no-winrt-html-dom-parser.md](no-winrt-html-dom-parser.md) — no built-in WinRT HTML DOM parser for arbitrary strings (`Windows.Data.Html` is text-only); tokenize the tag subset by hand.
