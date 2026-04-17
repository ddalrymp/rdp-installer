<#
.SYNOPSIS
    Signs existing .rdp files in bulk and prepares them for upload.

.DESCRIPTION
    Takes a directory of existing .rdp files, signs each one using rdpsign.exe
    with the specified certificate, writes signed copies to an output directory,
    exports the signing certificate's public key, and updates config.json.

    This script does NOT generate .rdp files — it only signs existing ones.
    Your existing user-creation script handles .rdp generation.

.PARAMETER InputDir
    Directory containing the existing *.rdp files to sign.

.PARAMETER CertThumbprint
    The SHA256 thumbprint of the certificate to sign .rdp files with.
    The certificate must exist in the Local Machine or Current User certificate store.

.PARAMETER OutputDir
    Directory where signed files will be written (default: ./output).
    Signed .rdp files go into OutputDir/users/.

.PARAMETER BaseUrl
    The base URL where files will be hosted (for updating config.json URLs).

.EXAMPLE
    .\Sign-RdpFiles.ps1 -InputDir "C:\RdpFiles" `
        -CertThumbprint "AB12CD34..." `
        -BaseUrl "https://mybucket.s3.amazonaws.com/rdp"

.EXAMPLE
    .\Sign-RdpFiles.ps1 -InputDir "\\server\share\rdp" `
        -CertThumbprint "AB12CD34..." `
        -OutputDir "C:\output" `
        -BaseUrl "https://mybucket.s3.amazonaws.com/rdp"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputDir,

    [Parameter(Mandatory = $true)]
    [string]$CertThumbprint,

    [string]$OutputDir = "./output",

    [Parameter(Mandatory = $true)]
    [string]$BaseUrl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- Validate input directory ---

if (-not (Test-Path $InputDir)) {
    Write-Error "Input directory not found: $InputDir"
    exit 1
}

$rdpFiles = Get-ChildItem -Path $InputDir -Filter "*.rdp" -File
if ($rdpFiles.Count -eq 0) {
    Write-Error "No .rdp files found in: $InputDir"
    exit 1
}

Write-Host "Found $($rdpFiles.Count) .rdp file(s) in: $InputDir" -ForegroundColor Cyan

# --- Validate certificate ---

Write-Host "Looking up certificate with thumbprint: $CertThumbprint ..." -ForegroundColor Cyan

$cert = Get-ChildItem -Path "Cert:\LocalMachine\My" -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -eq $CertThumbprint }

if (-not $cert) {
    $cert = Get-ChildItem -Path "Cert:\CurrentUser\My" -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $CertThumbprint }
}

if (-not $cert) {
    Write-Error "Certificate with thumbprint '$CertThumbprint' not found in LocalMachine\My or CurrentUser\My stores."
    exit 1
}

Write-Host "  Found certificate: $($cert.Subject)" -ForegroundColor Green

# --- Prepare output directories ---

$usersOutputDir = Join-Path $OutputDir "users"
if (Test-Path $usersOutputDir) {
    Remove-Item -Path $usersOutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $usersOutputDir -Force | Out-Null

# --- Sign each .rdp file ---

Write-Host "Signing .rdp files..." -ForegroundColor Cyan

$rdpsignPath = "rdpsign.exe"
$successCount = 0
$failCount = 0

foreach ($rdpFile in $rdpFiles) {
    $destPath = Join-Path $usersOutputDir $rdpFile.Name

    # Copy to output directory first (rdpsign modifies the file in place)
    Copy-Item -Path $rdpFile.FullName -Destination $destPath -Force

    try {
        $output = & $rdpsignPath $destPath /sha256 $CertThumbprint 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "  FAILED: $($rdpFile.Name) — rdpsign exit code $LASTEXITCODE"
            Write-Warning "  Output: $output"
            Remove-Item -Path $destPath -Force -ErrorAction SilentlyContinue
            $failCount++
        }
        else {
            Write-Host "  Signed: $($rdpFile.Name)" -ForegroundColor Green
            $successCount++
        }
    }
    catch {
        Write-Warning "  FAILED: $($rdpFile.Name) — $_"
        Remove-Item -Path $destPath -Force -ErrorAction SilentlyContinue
        $failCount++
    }
}

if ($successCount -eq 0) {
    Write-Error "No .rdp files were signed successfully. Check that rdpsign.exe is available (requires RDS role or RSAT tools)."
    exit 1
}

# --- Export signing certificate public key ---

Write-Host "Exporting signing certificate public key..." -ForegroundColor Cyan

$certPath = Join-Path $OutputDir "signing-cert.cer"
$certBytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
[System.IO.File]::WriteAllBytes($certPath, $certBytes)
Write-Host "  Exported: $certPath" -ForegroundColor Green

# --- Update config.json ---

Write-Host "Updating config.json..." -ForegroundColor Cyan

$configTemplatePath = Join-Path $PSScriptRoot "config.json"
if (-not (Test-Path $configTemplatePath)) {
    Write-Error "config.json template not found at $configTemplatePath"
    exit 1
}

$config = Get-Content $configTemplatePath -Raw | ConvertFrom-Json

# Update the first connection entry
$config.connections[0].certThumbprint = $CertThumbprint
$config.connections[0].rdpFileUrlPattern = "$BaseUrl/users/{userId}.rdp"
$config.connections[0].signingCertUrl = "$BaseUrl/signing-cert.cer"
$config.updatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

$configOutputPath = Join-Path $OutputDir "config.json"
$config | ConvertTo-Json -Depth 10 | Out-File -FilePath $configOutputPath -Encoding UTF8
Write-Host "  Updated: $configOutputPath" -ForegroundColor Green

# --- Summary ---

Write-Host ""
Write-Host "=== Signing Complete ===" -ForegroundColor Green
Write-Host "  Signed: $successCount file(s)"
if ($failCount -gt 0) {
    Write-Host "  Failed: $failCount file(s)" -ForegroundColor Yellow
}
Write-Host "  Output: $OutputDir"
Write-Host "    users/     - Signed .rdp files"
Write-Host "    signing-cert.cer"
Write-Host "    config.json"
Write-Host ""
Write-Host "Next step: Run Deploy-ToS3.ps1 to upload these files." -ForegroundColor Yellow
