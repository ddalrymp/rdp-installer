<#
.SYNOPSIS
    Uploads signed RDP files and certificate to an S3 bucket.

.DESCRIPTION
    Uploads the users/ directory of signed .rdp files and the signing certificate
    to the specified S3 bucket path using the AWS CLI. Use Deploy-Config.ps1
    separately to upload config.json.

.PARAMETER BucketName
    The S3 bucket name (e.g., "my-rdp-hosting-bucket").

.PARAMETER BucketPrefix
    The S3 key prefix / folder path (default: "rdp").

.PARAMETER OutputDir
    Local directory containing the generated files (default: "./output").

.PARAMETER Profile
    Optional AWS CLI profile name.

.EXAMPLE
    .\Deploy-ToS3.ps1 -BucketName "my-bucket" -BucketPrefix "rdp"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BucketName,

    [string]$BucketPrefix = "rdp",

    [string]$OutputDir = "./output",

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

# Verify signing certificate exists
$certPath = Join-Path $OutputDir "signing-cert.cer"
if (-not (Test-Path $certPath)) {
    Write-Error "Required file not found: $certPath. Run Sign-RdpFiles.ps1 first."
    exit 1
}

$usersDir = Join-Path $OutputDir "users"
if (-not (Test-Path $usersDir)) {
    Write-Error "users/ directory not found in $OutputDir. Run Sign-RdpFiles.ps1 first."
    exit 1
}

$rdpCount = @(Get-ChildItem -Path $usersDir -Filter "*.rdp" -File).Count
if ($rdpCount -eq 0) {
    Write-Error "No .rdp files found in $usersDir."
    exit 1
}

# Build common AWS CLI args
$awsArgs = @()
if ($Profile -ne "") {
    $awsArgs += @("--profile", $Profile)
}

$s3Base = "s3://${BucketName}/${BucketPrefix}"

Write-Host "Uploading files to $s3Base ..." -ForegroundColor Cyan

# Upload signing-cert.cer
Write-Host "  Uploading signing-cert.cer" -ForegroundColor Gray
$args = @("s3", "cp", (Join-Path $OutputDir "signing-cert.cer"), "$s3Base/signing-cert.cer",
          "--content-type", "application/x-x509-ca-cert",
          "--cache-control", "no-cache, no-store, must-revalidate")
$args += $awsArgs
& aws @args
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to upload signing-cert.cer"; exit 1 }

# Upload each .rdp file individually (avoids needing s3:ListBucket permission)
Write-Host "  Uploading $rdpCount .rdp file(s) from users/ ..." -ForegroundColor Gray
$uploadFailed = 0
foreach ($rdpFile in (Get-ChildItem -Path $usersDir -Filter "*.rdp" -File)) {
    $s3Key = "$s3Base/users/$($rdpFile.Name)"
    $cpArgs = @("s3", "cp", $rdpFile.FullName, $s3Key,
                "--content-type", "application/x-rdp",
                "--cache-control", "no-cache, no-store, must-revalidate")
    $cpArgs += $awsArgs
    & aws @cpArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "  Failed to upload $($rdpFile.Name)"
        $uploadFailed++
    }
}
if ($uploadFailed -gt 0) {
    Write-Error "$uploadFailed file(s) failed to upload."
    exit 1
}

Write-Host ""
Write-Host "=== Upload Complete ===" -ForegroundColor Green
Write-Host "Files uploaded:"
Write-Host "  https://${BucketName}.s3.amazonaws.com/${BucketPrefix}/signing-cert.cer"
Write-Host "  https://${BucketName}.s3.amazonaws.com/${BucketPrefix}/users/ ($rdpCount files)"
Write-Host ""
Write-Host "Ensure the bucket policy allows public read access (or use CloudFront)." -ForegroundColor Yellow
