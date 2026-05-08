param(
    [string]$Svg = "$PSScriptRoot\icon.svg",
    [string]$Ico = "$PSScriptRoot\icon.ico"
)
magick -background none $Svg -define icon:auto-resize="256,48,32,16" $Ico
Write-Host "Written: $Ico"
