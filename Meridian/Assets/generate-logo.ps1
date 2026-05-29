param(
    [string]$Svg = "$PSScriptRoot\icon.svg",
    [string]$Png = "$PSScriptRoot\logo.png",
    [int]$Size = 120
)
# Google OAuth consent screen wants a square PNG/JPG/BMP, recommended 120x120, max 1 MB.
# We render the existing icon.svg at high res via ImageMagick (which uses the rsvg
# delegate, so gradients and drop shadows come out clean) and downscale to $Size.
magick -background none -density 384 $Svg -resize "${Size}x${Size}" -depth 8 $Png
Write-Host "Written: $Png (${Size}x${Size})"
