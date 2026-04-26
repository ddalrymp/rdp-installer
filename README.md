# RDP RemoteApp Launcher & Installer

A lightweight Windows launcher that gives clients a **one-click, no-prompts** connection to a published RemoteApp via `mstsc.exe`. Supports **100+ users** via a templated config with `{ORGID}` and `{USERID}` placeholders — a single universal installer works for all organizations. Configuration is hosted as a single `config.json` on S3 and can be updated independently of the installer.

## Architecture

```
┌──────────────────────────┐         ┌──────────────────────────┐
│   S3 / Web Server        │         │   Client PC              │
│                          │  HTTPS  │                          │
│  config.json (template)  │◄────────│  RdpLauncher.exe         │
│  users/ORG1_U01.rdp      │         │   ├─ Load OrgId/UserId   │
│  users/ORG1_U02.rdp      │         │   ├─ Fetch config.json   │
│  users/...               │         │   ├─ Resolve templates   │
│  signing-cert.cer        │         │   ├─ Download .rdp file  │
│  RdpLauncherSetup.exe    │         │   └─ Launch mstsc.exe    │
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

### Config-Only Updates

To push config changes (server address, launcher version, connection settings) without re-signing RDP files or rebuilding the installer:

```powershell
cd server
.\Deploy-Config.ps1 -BucketName "your-bucket" -BucketPrefix "rdp"
```

Clients pick up changes on their next launch (within the configured cache TTL).

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

### 3. Build the Installer

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

### 4. Upload & Distribute the Installer

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
- Launcher `.exe` installed to `%LocalAppData%\RdpLauncher\`
- Signing certificate is imported into `CurrentUser\TrustedPublisher`
- Org ID and User ID are collected (installer page or `/ORGID` + `/USERID` params) and saved to registry
- Desktop shortcut and Start Menu entry are created
- Config URL is written to `HKCU\Software\RdpLauncher\ConfigUrl`
- RDP consent dialog suppression keys are set

**On each launch:**
1. Loads OrgId/UserId from registry; opens Settings if missing
2. Prompts for password if not saved (DPAPI-encrypted in registry)
3. Fetches `config.json` from the hosted endpoint (cached with TTL)
4. Resolves `{ORGID}` and `{USERID}` placeholders in config values
5. Downloads the user's `.rdp` file from the hosted endpoint (cached locally)
6. Launches `mstsc.exe` with the `.rdp` file
7. On success: saves password for next time
8. Cleans up temp files after the session ends

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
│   │   ├── ProcessLauncher.cs    # mstsc.exe launch orchestrator
│   │   ├── RdpFileManager.cs     # Download & cache .rdp files
│   │   ├── CertificateManager.cs # Import certs to TrustedPublisher
│   │   ├── UpdateChecker.cs      # Version comparison + update prompt
│   │   └── appsettings.json      # Default config URL + cache TTL
│   └── RdpLauncher.Tests/        # xUnit unit tests
├── installer/
│   ├── setup.iss                 # Inno Setup script (OrgId + UserId pages)
│   ├── Prepare-Installer.ps1    # Download signing-cert.cer from S3
│   ├── Deploy-Installer.ps1     # Upload compiled installer to S3
│   └── assets/                   # Icon and installer resources
├── server/
│   ├── config.json               # Config template ({ORGID}/{USERID} placeholders)
│   ├── Sign-RdpFiles.ps1         # Bulk-sign existing .rdp files
│   ├── Deploy-ToS3.ps1           # Upload RDP files, cert & config to S3
│   └── Deploy-Config.ps1         # Upload config.json only to S3
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
}
```

### Registry Override

The installer writes the config URL to `HKCU\Software\RdpLauncher\ConfigUrl`. This takes priority over `appsettings.json`, allowing per-machine overrides without modifying the binary.

## Requirements

- **Server**: Windows Server with RDS role, public CA-issued certificate, `rdpsign.exe`
- **Build**: .NET 8 SDK (Windows), Inno Setup 6
- **Client**: Windows 10/11, no admin rights required (installs to user profile)

## macOS Support (Planned)

A future phase will add macOS support via a `.pkg` installer + shell script using Microsoft Remote Desktop, reusing the same server-side config endpoint.

## Build Summary (Windows Server Quick Reference)

```powershell
# 1. Pull latest code
git pull

# 2. Edit config URL
notepad src\RdpLauncher\appsettings.json

# 3. Publish .NET app
dotnet publish src\RdpLauncher\RdpLauncher.csproj -c Release -o installer\publish

# 4. Download signing cert
cd installer
.\Prepare-Installer.ps1 -BucketName "your-bucket" -BucketPrefix "rdp"
cd ..

# 5. Compile installer
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss

# 6. Upload installer
cd installer
.\Deploy-Installer.ps1 -BucketName "your-bucket" -BucketPrefix "rdp"
```
