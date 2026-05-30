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
- [dpi-awareness-manifest-custom-main.md](dpi-awareness-manifest-custom-main.md) — custom `Main` needs `PerMonitorV2` in app.manifest; without it, the bottom-right of the window goes dead to wheel/drag at >100% scale.
- [month-view-band-overlay-layout.md](month-view-band-overlay-layout.md) — month-grid multi-day bands can't be shared `Grid` rows; use per-cell reserve spacer + a band overlay, atomic per week, single height budget. Verified against gCal.
- [packaged-msix-aot-needs-sdk-resources-pri.md](packaged-msix-aot-needs-sdk-resources-pri.md) — packaged AOT MSIX crashes at startup (`0x802b000a` in combase/Xaml) when hand-packed; root cause is the `resources.pri` — a hand `makepri` PRI is insufficient, must use the SDK-generated one. Let the SDK build the payload, overlay the AOT exe.
