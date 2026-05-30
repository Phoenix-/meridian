# Installing Meridian (MSIX)

> **These are development builds.** The package is signed with a **self-signed
> developer certificate**, not a commercial code-signing certificate. That means
> Windows does not trust it out of the box — you have to trust the certificate
> once, manually, before installing. You are choosing to trust a certificate a
> stranger on the internet generated; only do this if you trust the source of
> this build. A properly verified, trust-on-install version is tracked under
> issue #7.

## What you need

- `Meridian-*.msix` — the app package
- `Meridian-Dev.cer` — the **public** developer certificate (no private key; it
  is also embedded in the .msix, this is just a convenient copy)
- Windows 10 1809 (build 17763) or newer, x64
- Administrator rights **once** (to trust the certificate)

The package is self-contained — you do **not** need to install the Windows App
SDK Runtime separately.

## Install (first time)

Run these in an **elevated** PowerShell (Run as administrator), from the folder
where you downloaded the files:

```powershell
# 1. Trust the developer certificate (one time per machine).
Import-Certificate -FilePath .\Meridian-Dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# 2. Install the app.
Add-AppxPackage -Path .\Meridian-*.msix
```

Then launch **Meridian** from the Start menu.

> If you skip step 1, step 2 fails with `0x800B0109` ("A certificate chain
> processed, but terminated in a root certificate which is not trusted") or a
> "publisher could not be verified" error.

## Update to a newer build

The certificate stays trusted, so updates only need the install step. If the
version number changed, a plain install updates in place:

```powershell
Add-AppxPackage -Path .\Meridian-*.msix
```

If the version number is the **same** as what's installed (e.g. reinstalling the
same nightly), force it:

```powershell
Add-AppxPackage -Path .\Meridian-*.msix -ForceUpdateFromAnyVersion
```

## Uninstall

```powershell
Get-AppxPackage *Meridian* | Remove-AppxPackage
```

User data (accounts, cache) lives in
`%LOCALAPPDATA%\Packages\Phoenix.Meridian_*\LocalState` and is removed on
uninstall.

## Removing the trusted certificate (optional)

If you later want to stop trusting the dev certificate (elevated PowerShell):

```powershell
Get-ChildItem Cert:\LocalMachine\TrustedPeople |
  Where-Object { $_.Subject -eq 'CN=Meridian Dev (PLACEHOLDER)' } |
  Remove-Item
```
