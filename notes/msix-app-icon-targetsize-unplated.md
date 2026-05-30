# MSIX app icon: assets must reach the package, glyph sizing, no grey tile plate

Getting the packaged-app icon to look right in the Start menu took fixing three
separate things. All live in `Meridian/Assets/` + `Meridian/Assets/generate-msix-assets.ps1`
+ the staging step of `packaging/build-msix.ps1`.

1. **Assets weren't in the package → blank icon.** The pipeline builds the final
   package from the SDK-payload of a throwaway packaged build, then overlays the
   AOT exe (see [[packaged-msix-aot-needs-sdk-resources-pri]]). That SDK build does
   NOT copy `Assets/msix/*.png` into its payload (they aren't declared as Content),
   so `stage\Assets` was empty and the manifest's logo references resolved to
   nothing. Fix: `build-msix.ps1` copies the PNGs into `stage\Assets` explicitly.

2. **Glyph looked tiny next to system icons.** `icon.svg` drew the glyph with big
   internal padding inside its 256×256 viewBox, so it filled only ~72% of the
   frame while Edge/Store/etc fill ~95%. Fix: wrap the content in
   `<g transform="translate(-28.16,-28.16) scale(1.22)">` — scales ~1.22 about the
   canvas center (128,128). Geometry of the shapes is unchanged; only the SVG
   source is edited, then icon.ico / logo.png / the MSIX PNGs are regenerated from
   it. Measured empirically (rendered + alpha-bbox) that up to ~1.22 nothing
   clips; the glyph is symmetric (bbox 28..228, center 128) — an earlier "right
   edge clips" symptom was a mistake centering on 120 instead of 128, not real.

3. **Grey tile backplate behind the icon in Start (the "why is there a grey
   square" bug).** Apps that ship only `Square44x44Logo.scale-*` render in Start
   lists as a TILE, which Windows draws on a neutral plate — `BackgroundColor`
   only changes the plate color, it can't be made transparent (a Start tile is
   opaque by design; `BackgroundColor="transparent"` falls back to grey). Apps
   that show NO square (Visual Studio, Sublime) ship
   `Square44x44Logo.targetsize-{16,24,32,48,256}` plus `_altform-unplated` copies
   — Windows uses target-size assets in app LISTS, drawn as a plain icon, and the
   `altform-unplated` variant removes the plate. Fix: generate-msix-assets.ps1
   emits both the target-size and the unplated set. Verified: grey square gone,
   matches the unpackaged .lnk icon.

Note: editing icon.svg also changes the OAuth consent-screen logo.png (same
source). Acceptable for Google, but flag it if revisiting brand assets.
