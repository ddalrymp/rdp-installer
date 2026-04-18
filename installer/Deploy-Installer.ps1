<#
.SYNOPSIS
    Uploads the built RdpLauncherSetup.exe to S3.

.DESCRIPTION
    Uploads the compiled installer to the same S3 bucket used for config and
    .rdp files, so clients (and the launcher's auto-update check) can download it.

    Run this after compiling setup.iss with Inno Setup.

.PARAMETER BucketName
    The S3 bucket name (e.g., "my-rdp-hosting-bucket").

.PARAMETER BucketPrefix
    The S3 key prefix / folder path (default: "rdp").

.PARAMETER InstallerPath
    Path to the compiled installer (default: Output/RdpLauncherSetup.exe relative to script dir).

.PARAMETER Profile
    Optional AWS CLI profile name.

.EXAMPLE
    .\Deploy-Installer.ps1 -BucketName "my-bucket"

.EXAMPLE
    .\Deploy-Installer.ps1 -BucketName "my-bucket" -InstallerPath "C:\build\RdpLauncherSetup.exe"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BucketName,

    [string]$BucketPrefix = "rdp",

    [string]$InstallerPath = (Join-Path $PSScriptRoot "Output\RdpLauncherSetup.exe"),

    [string]$Profile = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Verify AWS CLI is available
try {
    $null = & aws --version 2>&1
}
catch {
    Write-Error "AWS CLI is not installed or not in PATH. Install it from https://aws.amazon.com/cli/"
    exit 1
}

# Verify installer exists
if (-not (Test-Path $InstallerPath)) {
    Write-Error "Installer not found: $InstallerPath. Compile setup.iss with Inno Setup first."
    exit 1
}

$fileSize = [math]::Round((Get-Item $InstallerPath).Length / 1MB, 1)

# Build common AWS CLI args
$awsArgs = @()
if ($Profile -ne "") {
    $awsArgs += @("--profile", $Profile)
}

$s3Base = "s3://${BucketName}/${BucketPrefix}"
$s3Key = "$s3Base/RdpLauncherSetup.exe"

Write-Host "Uploading installer to $s3Key ($fileSize MB) ..." -ForegroundColor Cyan

$args = @("s3", "cp", $InstallerPath, $s3Key,
          "--content-type", "application/octet-stream",
          "--cache-control", "no-cache, no-store, must-revalidate")
$args += $awsArgs
& aws @args
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to upload installer."
    exit 1
}

Write-Host ""
Write-Host "=== Upload Complete ===" -ForegroundColor Green
Write-Host "  https://${BucketName}.s3.amazonaws.com/${BucketPrefix}/RdpLauncherSetup.exe"
Write-Host ""
