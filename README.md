# RDP RemoteApp Launcher & Installer

A lightweight Windows launcher that gives clients a **one-click, no-prompts** connection to a published RemoteApp. Supports **100+ users** with per-user `.rdp` files — a single universal installer prompts for (or accepts) a user code, then fetches the correct signed `.rdp` file on each launch. Certificate rotations are handled automatically without reinstalling.

## Architecture

```
┌──────────────────────────┐         ┌──────────────────────────┐
│   S3 / Web Server        │         │   Client PC              │
│                          │  HTTPS  │                          │
│  config.json             │◄────────│  RdpLauncher.exe         │
│  users/ORG1_U01.rdp      │         │   ├─ Read user code      │
│  users/ORG1_U02.rdp      │         │   ├─ Fetch config.json   │
│  users/...               │         │   ├─ Download user .rdp  │
│  signing-cert.cer        │         │   ├─ Import cert         │
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

### 2. Build the Launcher

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on a **Windows** machine:

```powershell
# Update the config URL in appsettings.json first
dotnet publish src/RdpLauncher/RdpLauncher.csproj -c Release -o installer/publish
```

### 3. Build the Installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php):

1. **Download the signing certificate from S3** (so the installer can bundle it):

```powershell
cd installer
.\Prepare-Installer.ps1 -BucketName "your-bucket" -BucketPrefix "rdp"
```

2. Update `ConfigUrl` in `setup.iss` to your hosted config URL
3. Open `setup.iss` in Inno Setup and compile

This produces `installer/Output/RdpLauncherSetup.exe`.

### 4. Upload & Distribute the Installer

```powershell
.\Deploy-Installer.ps1 -BucketName "your-bucket" -BucketPrefix "rdp"
```

Clients download the installer, run it once, enter their user code when prompted, then use the desktop shortcut to connect.

**Silent install with user code:**
```
RdpLauncherSetup.exe /SILENT /USERCODE=ORG1_U01
```

## How It Works

**On install:**
- Launcher `.exe` is installed to `%LocalAppData%\RdpLauncher\`
- Signing certificate is imported into `CurrentUser\TrustedPublisher`
- User code is collected (installer page or `/USERCODE` param) and saved to registry
- Desktop shortcut and Start Menu entry are created
- Config URL is written to `HKCU\Software\RdpLauncher\ConfigUrl`

**On each launch:**
1. Reads user code from `HKCU\Software\RdpLauncher\UserCode` (prompts if missing)
2. Fetches `config.json` from the hosted endpoint
3. Compares the cert thumbprint with the cached version
4. If changed: downloads the new signed `.rdp` for this user (via URL pattern)
5. Imports the new cert to `TrustedPublisher` if needed
6. Copies `.rdp` to a temp file and launches `mstsc.exe`
7. Cleans up the temp file after the session ends

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
│   │   ├── LauncherForm.cs       # UI + orchestration
│   │   ├── ConfigService.cs      # Fetch & cache config.json
│   │   ├── RdpFileManager.cs     # Download & cache .rdp files
│   │   ├── CertificateManager.cs # Import certs to TrustedPublisher
│   │   ├── ProcessLauncher.cs    # Launch mstsc.exe and monitor
│   │   ├── UpdateChecker.cs      # Version comparison + update prompt
│   │   ├── UserCodePrompt.cs     # First-run user code input dialog
│   │   └── appsettings.json      # Default config URL
│   └── RdpLauncher.Tests/        # xUnit unit tests
├── installer/
│   ├── setup.iss                 # Inno Setup script (with user code page)
│   ├── Prepare-Installer.ps1    # Download signing-cert.cer from S3
│   ├── Deploy-Installer.ps1     # Upload compiled installer to S3
│   └── assets/                   # Icon and installer resources
├── server/
│   ├── config.json               # Config template (rdpFileUrlPattern)
│   ├── Sign-RdpFiles.ps1         # Bulk-sign existing .rdp files
│   └── Deploy-ToS3.ps1           # Upload to S3
└── docs/
    └── setup-guide.md            # End-user setup instructions
```

## Configuration

### appsettings.json

```json
{
  "ConfigUrl": "https://your-bucket.s3.amazonaws.com/rdp/config.json",
  "ConnectionId": "main-app",
  "AppDataFolder": "RdpLauncher"
}
```

### Registry Override

The installer writes the config URL to `HKCU\Software\RdpLauncher\ConfigUrl`. This takes priority over `appsettings.json`, allowing per-machine overrides without modifying the binary.

## Requirements

- **Server**: Windows Server with RDS role, public CA-issued certificate, `rdpsign.exe`
- **Build**: .NET 8 SDK (Windows), Inno Setup 6
- **Client**: Windows 10/11, no admin rights required (installs to user profile)

## macOS Support (Planned)

A future phase will add macOS support via a `.pkg` installer + shell script using FreeRDP (`xfreerdp`), reusing the same server-side config endpoint.
