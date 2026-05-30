<#
.SYNOPSIS
  Builds a Meridian MSIX containing the NativeAOT exe, via makeappx.

.DESCRIPTION
  Why this is shaped the way it is (learned the hard way — see
  notes/packaged-msix-aot-needs-sdk-resources-pri.md):

  A packaged WinUI app needs the SDK-generated package `resources.pri` (it merges
  the app's XBF with the WinUI control resource maps, keyed to the package
  identity). A hand-`makepri`'d PRI is NOT sufficient — the app crashes at startup
  in combase / Microsoft.ui.xaml.dll with 0x802b000a (RO_E_METADATA_NAME_NOT_FOUND)
  and no window appears. The NativeAOT publish does NOT produce a usable package
  PRI (its `Meridian.pri` is rooted at the assembly name, not the package identity,
  and omits the merged control resources).

  So we let the SDK build the COMPLETE packaged payload (manifest + correct
  resources.pri + the managed/projection assemblies) from a THROWAWAY copy of the
  project flipped to WindowsPackageType=MSIX — the real Meridian.csproj stays
  WindowsPackageType=None so F5 + the UI tests keep working unpackaged. Then we
  OVERLAY our NativeAOT Meridian.exe onto that payload and repack. AOT exe + SDK
  resources.pri = launches.

  NOT a .wapproj and NOT flipping the real csproj: the throwaway copy is built in a
  temp dir and never touches the working tree.

  Steps: AOT publish -> SDK-build throwaway packaged copy -> overlay AOT exe onto
  the SDK package payload -> makeappx pack -> (optional) dev-sign for local sideload.

.PARAMETER PublishProfile
  win-x64-release (AOT, default) or win-x64-r2r (Plan B1 — one-token swap).

.PARAMETER DevSign
  Create/reuse a self-signed dev cert (subject == manifest Publisher), trust it,
  and sign the package so Add-AppxPackage works locally. LOCAL/CI dev only —
  production signing is issue #7.

.PARAMETER SkipPublish
  Reuse an existing AOT publish dir (faster iteration on packaging itself).

.EXAMPLE
  ./packaging/build-msix.ps1 -DevSign
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64-release', 'win-x64-r2r')]
    [string]$PublishProfile = 'win-x64-release',
    [switch]$DevSign,
    [switch]$SkipPublish,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repoRoot    = Split-Path -Parent $PSScriptRoot
$proj        = Join-Path $repoRoot 'Meridian\Meridian.csproj'
$manifestSrc = Join-Path $PSScriptRoot 'Package.appxmanifest'
$assetsSrc   = Join-Path $repoRoot 'Meridian\Assets\msix'

$publishDir = switch ($PublishProfile) {
    'win-x64-release' { Join-Path $repoRoot 'Meridian\bin\publish\win-x64' }
    'win-x64-r2r'     { Join-Path $repoRoot 'Meridian\bin\publish\win-x64-r2r' }
}

$outDir   = Join-Path $repoRoot 'packaging\out'
$stageDir = Join-Path $outDir 'stage'
$msixPath = Join-Path $outDir 'Meridian.msix'

# Throwaway SDK-packaged build lives outside the repo so it can't disturb the tree.
# Prefer C:\Temp locally; fall back to the OS temp dir (CI runners may not have C:\Temp).
$tempRoot = if (Test-Path 'C:\Temp') { 'C:\Temp' } else { $env:TEMP }
$sdkPkgDir = Join-Path $tempRoot 'Meridian-msix-sdkbuild'

# ── locate SDK tools (makeappx/signtool) + MSBuild ────────────────────────────
function Find-SdkTool([string]$name) {
    $pkgRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
    if (-not (Test-Path $pkgRoot)) { throw "SDK BuildTools package not restored at $pkgRoot. Run a build/restore first." }
    $tool = Get-ChildItem $pkgRoot -Directory | Sort-Object Name -Descending |
        ForEach-Object { Get-ChildItem (Join-Path $_.FullName 'bin') -Recurse -Filter $name -ErrorAction SilentlyContinue } |
        Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
    if (-not $tool) { throw "$name not found under $pkgRoot" }
    return $tool.FullName
}
# Returns an array: the executable + any leading args (so `dotnet msbuild` works as a fallback).
# Single-project MSIX packaging needs full MSBuild; `dotnet msbuild` carries the same targets.
function Find-MSBuild {
    foreach ($edition in 'Community','Professional','Enterprise') {
        $p = "C:\Program Files\Microsoft Visual Studio\2022\$edition\MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $p) { return ,@($p) }
    }
    $found = Get-ChildItem 'C:\Program Files\Microsoft Visual Studio\2022' -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\Bin\\MSBuild.exe$' } | Select-Object -First 1 -Expand FullName
    if ($found) { return ,@($found) }
    # CI fallback: dotnet msbuild (the dotnet CLI is always present on the runner).
    return ,@('dotnet','msbuild')
}

# ── 1. AOT publish (the exe we actually ship) ─────────────────────────────────
if (-not $SkipPublish) {
    Write-Host "==> Publishing ($PublishProfile)..." -ForegroundColor Cyan
    dotnet publish $proj -p:PublishProfile=$PublishProfile
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}
$aotExe = Join-Path $publishDir 'Meridian.exe'
if (-not (Test-Path $aotExe)) { throw "Meridian.exe not found in $publishDir — publish did not produce expected output." }

# ── 2. SDK-build a throwaway packaged copy to obtain the correct payload ───────
# This produces the package's resources.pri + manifest + managed/projection
# assemblies that the AOT publish can't generate. The real csproj is untouched;
# we copy the project (sans bin/obj) to a temp dir and flip it to MSIX there.
Write-Host "==> SDK-building packaged payload (throwaway copy)..." -ForegroundColor Cyan
$tmpProjDir = Join-Path $sdkPkgDir 'proj'
if (Test-Path $sdkPkgDir) { Remove-Item $sdkPkgDir -Recurse -Force }
New-Item -ItemType Directory -Force $tmpProjDir | Out-Null
# Copy app project + sibling SchemaGen + client_secret (build-time inputs), no bin/obj.
robocopy (Join-Path $repoRoot 'Meridian') $tmpProjDir /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
robocopy (Join-Path $repoRoot 'Meridian.SchemaGen') (Join-Path $sdkPkgDir 'Meridian.SchemaGen') /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
Copy-Item (Join-Path $repoRoot 'client_secret.json') (Join-Path $sdkPkgDir 'client_secret.json') -Force -ErrorAction SilentlyContinue

# Drop our authored manifest + MSIX assets into the temp project for single-project tooling.
Copy-Item $manifestSrc (Join-Path $tmpProjDir 'Package.appxmanifest') -Force
Copy-Item (Join-Path $assetsSrc '*.png') (Join-Path $tmpProjDir 'Assets') -Force

# Flip the temp csproj to packaged (only in the copy). The real csproj stays None.
$tmpCsproj = Join-Path $tmpProjDir 'Meridian.csproj'
(Get-Content $tmpCsproj -Raw) -replace `
    '<WindowsPackageType>None</WindowsPackageType>', `
    "<WindowsPackageType>MSIX</WindowsPackageType>`n    <GenerateAppxPackageOnBuild>true</GenerateAppxPackageOnBuild>`n    <AppxPackageSigningEnabled>false</AppxPackageSigningEnabled>`n    <Platforms>x64</Platforms>`n    <AppxBundle>Never</AppxBundle>" |
    Set-Content $tmpCsproj -Encoding UTF8

$msbuild = Find-MSBuild   # array: exe [+ leading args]
$mbExe  = $msbuild[0]
$mbArgs = @(if ($msbuild.Count -gt 1) { $msbuild[1..($msbuild.Count-1)] }) +
          @($tmpCsproj, '/restore', '/p:Configuration=Release', '/p:Platform=x64', '/v:m')
& $mbExe @mbArgs
if ($LASTEXITCODE -ne 0) { throw "SDK packaged build failed ($LASTEXITCODE)" }

# The SDK lays the full package payload (loose) here:
$sdkPayload = Join-Path $tmpProjDir 'bin\x64\Release\net10.0-windows10.0.19041.0\win-x64'
if (-not (Test-Path (Join-Path $sdkPayload 'resources.pri'))) {
    throw "SDK build did not produce resources.pri at $sdkPayload"
}

# ── 3. stage: SDK payload + OUR AOT exe overlaid ──────────────────────────────
Write-Host "==> Staging (SDK payload + AOT exe)..." -ForegroundColor Cyan
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force $stageDir | Out-Null
Copy-Item (Join-Path $sdkPayload '*') $stageDir -Recurse -Force
# Drop footprint files the SDK loose-layout includes but makeappx regenerates.
Remove-Item (Join-Path $stageDir 'AppxBlockMap.xml'), (Join-Path $stageDir 'AppxSignature.p7x') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $stageDir '[Content_Types].xml') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $stageDir 'Meridian.build.appxrecipe') -Force -ErrorAction SilentlyContinue
# Overlay the NativeAOT exe (the SDK build's exe is R2R/managed; ours is AOT).
Copy-Item $aotExe (Join-Path $stageDir 'Meridian.exe') -Force
Write-Host ("    overlaid AOT Meridian.exe ({0:N1} MB)" -f ((Get-Item $aotExe).Length/1MB))

$stagedManifest = Join-Path $stageDir 'AppxManifest.xml'
if ($Version) {
    # Match ONLY the Identity element's Version attribute:
    #  - -creplace (case-SENSITIVE): the XML declaration uses lowercase
    #    `version="1.0"`; matching it case-insensitively rewrites the <?xml ...?>
    #    line and makeappx rejects it ("Incorrect xml declaration syntax").
    #  - leading space discriminates capital-V `Version` from
    #    MinVersion/MaxVersionTested (no space before "Version").
    $xml = (Get-Content $stagedManifest -Raw) -creplace ' Version="[\d\.]+"', " Version=`"$Version`""
    # Write BOM-less UTF-8: Set-Content -Encoding UTF8 emits a BOM in some PS hosts,
    # which makeappx rejects ("Incorrect xml declaration syntax" at the <?xml line).
    [System.IO.File]::WriteAllText($stagedManifest, $xml, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "    version -> $Version"
}

# ── 4. pack ───────────────────────────────────────────────────────────────────
Write-Host "==> Packing MSIX..." -ForegroundColor Cyan
$makeappx = Find-SdkTool 'makeappx.exe'
& $makeappx pack /d $stageDir /p $msixPath /overwrite
if ($LASTEXITCODE -ne 0) { throw "makeappx failed ($LASTEXITCODE)" }
Write-Host "    packed: $msixPath"

# ── 5. dev sign (local sideload only — production signing is #7) ──────────────
if ($DevSign) {
    Write-Host "==> Dev-signing (LOCAL ONLY — not production)..." -ForegroundColor Yellow
    $subject = ([xml](Get-Content $manifestSrc)).Package.Identity.Publisher
    Write-Host "    cert subject: $subject"
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $subject `
            -KeyUsage DigitalSignature -CertStoreLocation Cert:\CurrentUser\My `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
        Write-Host "    created dev cert $($cert.Thumbprint)"
    } else { Write-Host "    reusing dev cert $($cert.Thumbprint)" }

    $cerPath = Join-Path $outDir 'Meridian-dev.cer'
    Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
    try {
        Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople -ErrorAction Stop | Out-Null
        Write-Host "    trusted in LocalMachine\TrustedPeople"
    } catch {
        Write-Warning "Could not import to LocalMachine\TrustedPeople (need admin). Import $cerPath manually before Add-AppxPackage."
    }
    $signtool = Find-SdkTool 'signtool.exe'
    & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $msixPath
    if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
    Write-Host "    signed."
}

Write-Host ""
Write-Host "Done: $msixPath" -ForegroundColor Green
if ($DevSign) {
    Write-Host "Install:   Add-AppxPackage '$msixPath'"
    Write-Host "Uninstall: Get-AppxPackage *Meridian* | Remove-AppxPackage"
} else {
    Write-Host "NOTE: unsigned — sign before Add-AppxPackage (re-run with -DevSign for local install)."
}
