# Custom Main needs explicit DPI-awareness in the manifest

## Trap

At >100% Windows display scale (repro at **125%**), the bottom-right
`~(1 − 1/scale)` of the window — about the rightmost & bottommost 20% at
125% — goes **dead to mouse-wheel scrolling and drag-and-drop**. The top-left
region works. Pointer *clicks* mostly still land, but wheel/drag hit-testing
is offset because the XAML island is rasterized in one coordinate space while
input is mapped in another. At 100% everything works, which is why this hid
for a long time and looked random. Symptom also showed as blurry text
(DWM bitmap-stretching a 100%-rendered window up to 125%).

This is the app-side manifestation of microsoft-ui-xaml#2101
("XAML Islands: mouse events scaled incorrectly on High-DPI displays").

## Cause

We use a **custom `Main`** ([Meridian/Program.cs](../Meridian/Program.cs),
`DISABLE_XAML_GENERATED_MAIN`). The XAML-generated `Main` would have
established Per-Monitor-V2 DPI awareness before the first HWND; our hand-written
one did not, and [app.manifest](../Meridian/app.manifest) had **no**
`dpiAwareness` section at all. So the process started System-DPI-aware,
DWM stretched the 100% surface to 125%, and the input hit-test rectangle was
the un-stretched (smaller) one anchored top-left — hence the dead bottom-right
band exactly proportional to the scale factor.

DPI awareness must be set **before any HWND is created** and can't be changed
afterward, so doing it from `Main` programmatically is fragile. The manifest is
read by the OS loader before any of our code runs — that's the robust place.

## Fix

Per-Monitor-V2 in the manifest, following the Microsoft docs sample exactly
(`dpiAware` first as the pre-1607 system-aware fallback, `dpiAwareness` second;
on Win10 1607+ the latter wins and the former is ignored):

```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
  </windowsSettings>
</application>
```

`<ApplicationManifest>app.manifest</ApplicationManifest>` in
[Meridian.csproj](../Meridian/Meridian.csproj) is what gets this embedded;
MSBuild merges our block into its generated manifest. Verify after build that
`obj/**/Manifests/app.manifest` still contains `PerMonitorV2`.

Docs: https://learn.microsoft.com/en-us/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process

## Rule of thumb

Any WinUI 3 desktop app with a custom entry point **must** declare DPI
awareness in the manifest — the framework won't do it for you. If hit-testing
or drag-drop misbehaves only at non-100% scale, suspect DPI awareness before
suspecting the control. Belt-and-suspenders fallback if the manifest ever
fails to apply: `SetProcessDpiAwarenessContext(-4)` as the very first line of
`Main`, before any window exists.
