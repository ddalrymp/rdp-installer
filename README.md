# RDP RemoteApp Launcher & Installer

A lightweight Windows launcher that gives clients a **one-click, no-prompts** connection to a published RemoteApp. Uses **FreeRDP** as the primary client (with mstsc.exe fallback) to eliminate Windows 11 24H2 consent dialogs. Supports **100+ users** via a templated config with `{ORGID}` and `{USERID}` placeholders — a single universal installer works for all organizations.

## Architecture

```
┌──────────────────────────┐         ┌──────────────────────────┐
│   S3 / Web Server        │         │   Client PC              │
│                          │  HTTPS  │                          │
│  config.json (template)  │◄────────│  RdpLauncher.exe         │
│  users/ORG1_U01.rdp      │         │   ├─ Load OrgId/UserId   │
│  users/ORG1_U02.rdp      │         │   ├─ Fetch config.json   │
│  users/...               │         │   ├─ Resolve templates   │
│  signing-cert.cer        │         │   ├─ Launch sdl3-freerdp │
│  RdpLauncherSetup.exe    │         │   └─ (mstsc fallback)    │
└──────────────────────────┘         └──────────────────────────┘
```

## Quick Start

### 1. Server-Side: Sign & Upload RDP Files

Run this on **each** RDS server where your `.rdp` files live:

1. **Generate your `.rdp` files** using your existing creation script (one per user, e.g., `ORG1_U01.rdp`, `ORG1_U02.rdp`, etc.)

2. **Sign them in bulk:**

```powershell
cd server

.\Sign-RdpFiles.ps1 `
    -InputDir "C:\Path\To\Your\RdpFiles" `
    -CertThumbprint "YOUR_CERT_THUMBPRINT" `
    -BaseUrl "https://your-bucket.s3.amazonaws.com/rdp"
```

This signs all `.rdp` files, exports the signing certificate, and updates `config.json`. Output goes to `./output/`.

3. **Upload to S3:**

```powershell
.\Deploy-ToS3.ps1 -BucketName "your-bucket" -BucketPrefix "rdp"
```

> **Multiple servers?** Run steps 1-3 on each server. The `signing-cert.cer` and `config.json` are regenerated from the certificate each time — they are not server-specific.

### 2. Build the Launcher

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on your **Windows Server**:

```powershell
# Clone or pull the latest code on your build server
cd C:\Path\To\rdp-installer

# Update the config URL in appsettings.json to your hosted endpoint
notepad src\RdpLauncher\appsettings.json

# Publish the launcher (self-contained, single-file)
dotnet publish src\RdpLauncher\RdpLauncher.csproj -c Release -o installer\publish
```

### 3. Download FreeRDP

Downloads the version specified in `appsettings.json` from `pub.freerdp.com`:

```powershell
# Read version from appsettings.json
$version = (Get-Content src\RdpLauncher\appsettings.json | ConvertFrom-Json).FreeRdpVersion

# Download and extract to installer\freerdp\
New-Item -ItemType Directory -Path installer\freerdp -Force
$url = "https://pub.freerdp.com/releases/freerdp-$version.zip"
$zip = "$env:TEMP\freerdp-$version.zip"
Invoke-WebRequest -Uri $url -OutFile $zip
Expand-Archive -Path $zip -DestinationPath installer\freerdp -Force
Remove-Item $zip
```

See [docs/freerdp-version.md](docs/freerdp-version.md) for the version pinning checklist.

### 4. Build the Installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php):

1. **Download the signing certificate from S3** (so the installer can bundle it):

```powershell
cd installer
.\Prepare-Installer.ps1 -BucketName "your-bucket" -BucketPrefix "rdp"
```

2. Update `ConfigUrl` in `setup.iss` to your hosted config URL
3. Compile the installer:

```powershell
# Command-line build (Inno Setup must be in PATH or use full path)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
```

This produces `installer\Output\RdpLauncherSetup.exe`.

### 5. Upload & Distribute the Installer

```powershell
.\Deploy-Installer.ps1 -BucketName "your-bucket" -BucketPrefix "rdp"
```

Clients download the installer, run it once, enter their Org ID and User ID when prompted, then use the desktop shortcut to connect. Password is entered on first launch by the app itself.

**Silent install:**
```
RdpLauncherSetup.exe /SILENT /ORGID=ORG1 /USERID=U01
```

## How It Works

**On install:**
- Launcher `.exe` + FreeRDP binaries installed to `%LocalAppData%\RdpLauncher\`
- Signing certificate is imported into `CurrentUser\TrustedPublisher` (for mstsc fallback)
- Org ID and User ID are collected (installer page or `/ORGID` + `/USERID` params) and saved to registry
- Desktop shortcut and Start Menu entry are created
- Config URL is written to `HKCU\Software\RdpLauncher\ConfigUrl`
- RDP consent dialog suppression keys are set (for mstsc fallback)

**On each launch:**
1. Loads OrgId/UserId from registry; opens Settings if missing
2. Prompts for password if not saved (DPAPI-encrypted in registry)
3. Fetches `config.json` from the hosted endpoint (cached with TTL)
4. Resolves `{ORGID}` and `{USERID}` placeholders in config values
5. Launches `sdl3-freerdp.exe` with RAIL mode for true floating RemoteApp windows
6. Passes credentials via stdin (not visible in process list)
7. On success: saves password for next time
8. On FreeRDP failure (if `fallbackToMstsc` enabled): falls back to mstsc.exe with .rdp file
5. On FreeRDP failure (if fallback enabled): uses mstsc.exe with downloaded .rdp file
6. Cleans up temp files after the session ends

**On cert rotation:**
1. Re-run `Sign-RdpFiles.ps1` with the new certificate thumbprint
2. Upload updated files with `Deploy-ToS3.ps1`
3. Next client launch automatically picks up the changes — no reinstall needed

**Offline fallback:**
If the server is unreachable, the launcher uses the last cached config and `.rdp` file.

## Certificate Rotation Workflow

```
Certificate renewed on server
        │
        ▼
Run Sign-RdpFiles.ps1 with new thumbprint
        │
        ▼
Run Deploy-ToS3.ps1 to upload
        │
        ▼
Clients auto-update on next launch ✓
```

## Project Structure

```
rdp-installer/
├── src/
│   ├── RdpLauncher/              # C# .NET 8 WinForms launcher
│   │   ├── Program.cs            # Entry point
│   │   ├── LauncherForm.cs       # UI + orchestration (gear icon, workflow)
│   │   ├── ConfigService.cs      # Fetch, cache & resolve config templates
│   │   ├── CredentialManager.cs  # DPAPI password storage, registry identity
│   │   ├── CredentialPrompt.cs   # Password input dialog
│   │   ├── SettingsForm.cs       # Settings UI (org/user/password)
│   │   ├── FreeRdpLauncher.cs    # sdl3-freerdp.exe RAIL launcher
│   │   ├── ProcessLauncher.cs    # FreeRDP → mstsc fallback orchestrator
│   │   ├── RdpFileManager.cs     # Download & cache .rdp files (fallback)
│   │   ├── CertificateManager.cs # Import certs to TrustedPublisher
│   │   ├── UpdateChecker.cs      # Version comparison + update prompt
│   │   └── appsettings.json      # Default config URL + cache TTL
│   └── RdpLauncher.Tests/        # xUnit unit tests
├── installer/
│   ├── setup.iss                 # Inno Setup script (OrgId + UserId pages)
│   ├── freerdp/                  # Bundled sdl3-freerdp.exe + DLLs (pinned version)
│   ├── Prepare-Installer.ps1    # Download signing-cert.cer from S3
│   ├── Deploy-Installer.ps1     # Upload compiled installer to S3
│   └── assets/                   # Icon and installer resources
├── server/
│   ├── config.json               # Config template ({ORGID}/{USERID} placeholders)
│   ├── Sign-RdpFiles.ps1         # Bulk-sign existing .rdp files
│   └── Deploy-ToS3.ps1           # Upload to S3
└── docs/
    ├── freerdp-version.md        # FreeRDP version pinning & update guide
    └── setup-guide.md            # End-user setup instructions
```

## Configuration

### appsettings.json

```json
{
  "ConfigUrl": "https://your-bucket.s3.amazonaws.com/rdp/config.json",
  "ConnectionId": "main-app",
  "ConfigCacheTtlMinutes": 60,
  "AppDataFolder": "RdpLauncher",
  "FreeRdpVersion": "3.24.2"
}
```

### Registry Override

The installer writes the config URL to `HKCU\Software\RdpLauncher\ConfigUrl`. This takes priority over `appsettings.json`, allowing per-machine overrides without modifying the binary.

## Requirements

- **Server**: Windows Server with RDS role, public CA-issued certificate, `rdpsign.exe`
- **Build**: .NET 8 SDK (Windows), Inno Setup 6, FreeRDP Windows x64 release
- **Client**: Windows 10/11, no admin rights required (installs to user profile)

## macOS Support (Planned)

A future phase will add macOS support via a `.pkg` installer + shell script using FreeRDP (`xfreerdp`), reusing the same server-side config endpoint.

## Build Summary (Windows Server Quick Reference)

```powershell
# 1. Pull latest code
git pull

# 2. Edit config URL
notepad src\RdpLauncher\appsettings.json

# 3. Publish .NET app
dotnet publish src\RdpLauncher\RdpLauncher.csproj -c Release -o installer\publish

# 4. Ensure FreeRDP binaries are in installer\freerdp\
$v = (Get-Content src\RdpLauncher\appsettings.json | ConvertFrom-Json).FreeRdpVersion
New-Item -ItemType Directory -Path installer\freerdp -Force
Invoke-WebRequest "https://pub.freerdp.com/releases/freerdp-$v.zip" -OutFile "$env:TEMP\freerdp.zip"
Expand-Archive "$env:TEMP\freerdp.zip" -DestinationPath installer\freerdp -Force
Remove-Item "$env:TEMP\freerdp.zip"

# 5. Download signing cert
cd installer
.\Prepare-Installer.ps1 -BucketName "your-bucket" -BucketPrefix "rdp"
cd ..

# 6. Compile installer
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss

# 7. Upload installer
cd installer
.\Deploy-Installer.ps1 -BucketName "your-bucket" -BucketPrefix "rdp"
```
