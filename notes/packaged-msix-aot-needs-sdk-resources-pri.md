# Packaged MSIX needs SDK-generated activatableClass manifest

**Trap:** A hand-authored `Package.appxmanifest` packed via `makeappx` over the
`dotnet publish` output produces an MSIX that installs fine but **crashes at
startup** inside `Microsoft.ui.xaml.dll` / `combase.dll` with exception code
`0xc000027b` (stowed) wrapping `0x802b000a` = `RO_E_METADATA_NAME_NOT_FOUND`. The
window never appears; `Program.Main` logs its first line then the process dies
before `App.OnLaunched` (so no managed exception is logged — the WinUI bootstrap
fast-fails).

**Why:** A *packaged* WinRT app resolves activatable classes ONLY through
manifest-declared `<Extension Category="windows.activatableClass.inProcessServer">`
entries. The registration-free/undocked activation that works when *unpackaged*
does NOT apply once packaged. The Windows App SDK normally auto-generates these
(hundreds of `<ActivatableClass>` entries for every `Microsoft.UI.Xaml.*` type)
into the final manifest at build. Hand-authoring the manifest + makeappx skips
that generation step, so combase can't find the XAML activation factories.
`WindowsAppSDKSelfContained=true` does NOT waive this — bundling the DLLs in the
payload still requires the manifest to declare where the classes live.

**Fix:** Don't hand-author the final manifest. Let the SDK generate the merged
manifest, then pack that. The MSBuild target `_GenerateCurrentProjectAppxManifest`
emits `obj/<Config>/<Platform>/<TFM>/win-x64/AppxManifest.xml` with all the
activatable-class entries merged in from our `Package.appxmanifest`. `build-msix.ps1`
runs that target and feeds the generated manifest to `makeappx` instead of the
hand-authored one. Our own XAML types (App, MainWindow, UserControls) are plain
C# and need NO entries — only the SDK's WinRT types do. NativeAOT doesn't change
this.

**Where the guard lives:** `packaging/build-msix.ps1` (harvests the generated
manifest), `packaging/Package.appxmanifest` (still the SOURCE — identity, visual
elements, toast activator — but no longer packed directly).

Diagnosing this: the managed log shows only the `start` line. The real error is
in the Windows Event Log (`Application Error` 1000 → faulting module
`Microsoft.ui.xaml.dll`) and the WER report (`combase.dll`, exception
`0x802b000a`). Always check Event Log + WER for a packaged WinUI startup crash —
the in-app log won't have it.

**WER gotcha (cost me a misdiagnosis):** F5/test runs of the Debug exe also crash
into WER with a DIFFERENT code (`0x8000ffff`), and "most recent WER report" can be
the Debug one, not the packaged one. ALWAYS filter WER by `AppPath` containing
`WindowsApps\Phoenix.Meridian` (the packaged install path) before reading the
exception code, or you'll chase the wrong error.

**RESOLVED (2026-05-30). Root cause: the package `resources.pri`.** Confirmed by
controlled isolation: a working package, with ONLY its `resources.pri` swapped for
a hand-`makepri`'d one, crashes again with `0x802b000a`. So the load-bearing
artifact is the SDK-generated `resources.pri`, nothing else.

Things that turned out NOT to be the cause (dead ends, don't repeat):
- **activatableClass entries** — the SDK-built WORKING manifest has ZERO of them
  (verified). Merging the 947 framework entries was wrong and unnecessary; remove it.
- **Missing CsWinRT projection DLLs** — the AOT publish strips all managed assemblies
  (`Microsoft.WinUI.dll`, `WinRT.Runtime.dll`, 28 `*.Projection.dll`, `Meridian.dll`).
  Adding them did NOT fix it; a working package with the SDK PRI runs fine without
  reconstructing them. The AOT exe has everything compiled in.
- **PRI index name** — yes, a hand `Meridian.pri` is rooted `Meridian` while the SDK
  one is rooted at the package identity `Phoenix.Meridian`; fixing just the name
  via `/IndexName` still crashed, because a hand `makepri new` over the publish dir
  does NOT reproduce the full resource map the SDK PRI contains (it captured only
  `App.xbf`/`MainWindow.xbf`, missing the merged WinUI control resources).

Why a hand `makepri` PRI is insufficient: the SDK's `GenerateProjectPriFile` /
packaging targets merge the app XBF resources WITH the WinUI/WinAppSDK control
resource maps into one package `resources.pri` keyed to the package identity. A
naive `makepri new /pr <publishdir>` can't reproducethat (and double-indexes the
SDK `.pri` files → PRI277 conflicts).

**Working recipe (what build-msix.ps1 now does):** let the SDK produce the complete
packaged payload + correct `resources.pri` + manifest (build a throwaway copy of the
project flipped to `WindowsPackageType=MSIX` + `GenerateAppxPackageOnBuild`, OR run
the packaging targets), then OVERLAY our NativeAOT `Meridian.exe` onto that payload
and `makeappx`. AOT exe + SDK-built `resources.pri` = launches. Main csproj stays
`WindowsPackageType=None` (F5/tests untouched); the packaged build happens in an
isolated copy.

Diagnosis method that cracked it: install the SDK single-project build (it RUNS),
then binary-diff its package against the hand-built one and bisect by swapping one
artifact at a time. The literature/deep-research did NOT find this — the empirical
package diff did.
