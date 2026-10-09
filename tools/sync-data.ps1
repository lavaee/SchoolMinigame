# Mirrors the authoritative repo-root data/ directory into Assets/StreamingAssets/data/,
# which is the copy Unity bundles inside the APK (the offline fallback floor).
#
# Run this before every build. tools/build-apk.ps1 calls it automatically.
#
# Usage:  pwsh -File tools/sync-data.ps1

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $repoRoot 'data'
$targetDir = Join-Path $repoRoot 'Assets\StreamingAssets\data'

if (-not (Test-Path $sourceDir)) { throw "Data directory not found: $sourceDir" }
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

$files = Get-ChildItem -Path $sourceDir -Filter *.json -File
if ($files.Count -eq 0) { throw "No JSON data files found in $sourceDir" }

foreach ($file in $files) {
    Copy-Item $file.FullName -Destination (Join-Path $targetDir $file.Name) -Force
    Write-Host ("synced  {0}" -f $file.Name) -ForegroundColor Green
}

# The bundled floor must be the employer's data, so surface any drift loudly.
& (Join-Path $PSScriptRoot 'verify-data.ps1')
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host 'WARNING: the bundled fallback no longer matches the employer revision.' -ForegroundColor Yellow
}

Write-Host ''
Write-Host ("Synced {0} file(s) to Assets/StreamingAssets/data" -f $files.Count)
