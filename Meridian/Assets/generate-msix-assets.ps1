param(
    [string]$Svg = "$PSScriptRoot\icon.svg",
    [string]$OutDir = "$PSScriptRoot\msix"
)
# Generates the PNG asset set the MSIX Package.appxmanifest references, rendered
# from the same icon.svg the rest of the app uses. Same ImageMagick approach as
# generate-logo.ps1 (-density 384 so the rsvg delegate renders gradients/shadows
# clean, then downscale, -depth 8).
#
# Minimal viable set + scale-200 variants. MSIX picks the closest scale per the
# user's display DPI; shipping 100 + 200 covers the common cases. Tiles
# (Wide310x150, Large/Small) and SplashScreen are intentionally omitted for now —
# add later if we want richer Start tiles.
#
# Asset naming follows the MSIX scale convention: Name.scale-100.png etc. The
# manifest references the base name (e.g. Square150x150Logo.png) and the loader
# resolves the scale suffix automatically.

$assets = @(
    @{ Name = "Square44x44Logo";   Base = 44 },
    @{ Name = "Square150x150Logo"; Base = 150 },
    @{ Name = "StoreLogo";         Base = 50 }
)
$scales = @(100, 200)

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

foreach ($a in $assets) {
    foreach ($scale in $scales) {
        $px = [int]($a.Base * $scale / 100)
        $out = Join-Path $OutDir "$($a.Name).scale-$scale.png"
        magick -background none -density 384 $Svg -resize "${px}x${px}" -depth 8 $out
        Write-Host "Written: $out (${px}x${px})"
    }
    # Also emit the unsuffixed base name (= scale-100 size). makeappx validates
    # the literal filename in the manifest exists in the package; without a built
    # resource index for these assets, the manifest references the base name and
    # the scale-* variants are picked up at runtime where a PRI is present.
    $baseOut = Join-Path $OutDir "$($a.Name).png"
    magick -background none -density 384 $Svg -resize "$($a.Base)x$($a.Base)" -depth 8 $baseOut
    Write-Host "Written: $baseOut ($($a.Base)x$($a.Base))"
}

# target-size variants for Square44x44Logo. These are what Windows uses in app
# LISTS (Start menu "all apps", search, taskbar) — rendered as a plain icon, not
# a tile. The "_altform-unplated" copies tell Windows to draw the icon WITHOUT
# the grey tile backplate, so the icon sits transparently on the Start surface
# (this is why Visual Studio / Sublime show no grey square, while scale-only apps
# like Claude do). Same content, multiple pixel sizes Windows picks from per DPI.
$targetSizes = @(16, 24, 32, 48, 256)
foreach ($ts in $targetSizes) {
    $plated   = Join-Path $OutDir "Square44x44Logo.targetsize-$ts.png"
    $unplated = Join-Path $OutDir "Square44x44Logo.targetsize-${ts}_altform-unplated.png"
    magick -background none -density 384 $Svg -resize "${ts}x${ts}" -depth 8 $plated
    Copy-Item $plated $unplated -Force
    Write-Host "Written: $plated + altform-unplated (${ts}x${ts})"
}
