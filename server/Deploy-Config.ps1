<#
.SYNOPSIS
    Uploads config.json to an S3 bucket.

.DESCRIPTION
    Uploads the server/config.json configuration file to S3 independently of
    the full RDP signing and deployment pipeline. Use this when you only need
    to push config changes (e.g., updated server address, new launcher version,
    connection settings) without re-signing RDP files or rebuilding the installer.

.PARAMETER BucketName
    The S3 bucket name (e.g., "my-rdp-hosting-bucket").

.PARAMETER BucketPrefix
    The S3 key prefix / folder path (default: "rdp").

.PARAMETER ConfigPath
    Path to the config.json file to upload (default: "./config.json").

.PARAMETER Profile
    Optional AWS CLI profile name.

.EXAMPLE
    .\Deploy-Config.ps1 -BucketName "my-bucket"

.EXAMPLE
    .\Deploy-Config.ps1 -BucketName "my-bucket" -BucketPrefix "rdp" -Profile "production"

.EXAMPLE
    .\Deploy-Config.ps1 -BucketName "my-bucket" -ConfigPath "C:\configs\config.json"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BucketName,

    [string]$BucketPrefix = "rdp",

    [string]$ConfigPath = "./config.json",

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

# Verify config.json exists
if (-not (Test-Path $ConfigPath)) {
    Write-Error "Config file not found: $ConfigPath"
    exit 1
}

# Validate JSON is well-formed
try {
    $null = Get-Content -Raw $ConfigPath | ConvertFrom-Json
}
catch {
    Write-Error "Invalid JSON in $ConfigPath — $($_.Exception.Message)"
    exit 1
}

# Build common AWS CLI args
$awsArgs = @()
if ($Profile -ne "") {
    $awsArgs += @("--profile", $Profile)
}

$s3Target = "s3://${BucketName}/${BucketPrefix}/config.json"

Write-Host "Uploading config.json to $s3Target ..." -ForegroundColor Cyan

$cpArgs = @("s3", "cp", $ConfigPath, $s3Target,
            "--content-type", "application/json",
            "--cache-control", "no-cache, no-store, must-revalidate")
$cpArgs += $awsArgs
& aws @cpArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to upload config.json to S3."
    exit 1
}

Write-Host ""
Write-Host "=== Config deployed ===" -ForegroundColor Green
Write-Host "  Source:  $ConfigPath"
Write-Host "  Target:  $s3Target"
Write-Host ""
Write-Host "Clients will pick up changes on their next launch (within ConfigCacheTtlMinutes)." -ForegroundColor Gray
