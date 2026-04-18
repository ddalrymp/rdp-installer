<#
.SYNOPSIS
    Downloads signing-cert.cer from S3 in preparation for building the installer.

.DESCRIPTION
    Fetches the signing certificate (and optionally config.json for reference)
    from your S3 bucket so the Inno Setup installer can bundle them.

    Run this before compiling setup.iss.

.PARAMETER BucketName
    The S3 bucket name (e.g., "my-rdp-hosting-bucket").

.PARAMETER BucketPrefix
    The S3 key prefix / folder path (default: "rdp").

.PARAMETER OutputDir
    Local directory to download files into (default: current script directory).

.PARAMETER Profile
    Optional AWS CLI profile name.

.EXAMPLE
    .\Prepare-Installer.ps1 -BucketName "my-bucket"

.EXAMPLE
    .\Prepare-Installer.ps1 -BucketName "my-bucket" -BucketPrefix "rdp" -Profile "prod"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BucketName,

    [string]$BucketPrefix = "rdp",

    [string]$OutputDir = $PSScriptRoot,

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

# Build common AWS CLI args
$awsArgs = @()
if ($Profile -ne "") {
    $awsArgs += @("--profile", $Profile)
}

$s3Base = "s3://${BucketName}/${BucketPrefix}"

Write-Host "Downloading installer prerequisites from $s3Base ..." -ForegroundColor Cyan

# Download signing-cert.cer
$certDest = Join-Path $OutputDir "signing-cert.cer"
Write-Host "  signing-cert.cer -> $certDest" -ForegroundColor Gray
$args = @("s3", "cp", "$s3Base/signing-cert.cer", $certDest)
$args += $awsArgs
& aws @args
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to download signing-cert.cer from $s3Base/signing-cert.cer"
    exit 1
}

Write-Host ""
Write-Host "=== Download Complete ===" -ForegroundColor Green
Write-Host "  signing-cert.cer  -> $certDest"
Write-Host ""
Write-Host "You can now build the installer with Inno Setup." -ForegroundColor Yellow
