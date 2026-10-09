# Verifies that the four files supplied by the employer are still byte-identical.
#
# The brief requires that replacing data/*.json changes the game without a rebuild, and the
# evaluation may swap these files at any time. This script proves which revision the repo holds
# before a build, so "the bundled fallback is the employer's data" is a checkable claim rather
# than an assumption.
#
# Usage:  pwsh -File tools/verify-data.ps1
# Exit:   0 = all supplied files identical, 1 = at least one mismatch or missing.

[CmdletBinding()]
param(
    [switch] $Quiet
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dataDir = Join-Path $repoRoot 'data'

# SHA-256 of the files as supplied in "small-pot-challenge/data".
$expected = [ordered]@{
    'config.json'       = 'dfcd784e007706d0903ee9231329295ae562e0f6d6e87df1097bf9e3259e7cf6'
    'pairs.json'        = '3d05c36f54e8677b41f32181d997298dfe91011eecd5fa308168cfdeb96e19a9'
    'strings-base.json' = '693a2f631ad7d4ef8df9f16d1b2d356b99ecd856251373fcd049cb9a2f5b96d6'
    'strings.json'      = '03a608de6293e2b78409bd2b301fb89801823af9da6e55f423f0511ecf1010e5'
}

$failed = $false
foreach ($name in $expected.Keys) {
    $path = Join-Path $dataDir $name
    if (-not (Test-Path $path)) {
        Write-Host ("{0,-20} MISSING" -f $name) -ForegroundColor Red
        $failed = $true
        continue
    }

    $actual = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -eq $expected[$name]) {
        if (-not $Quiet) { Write-Host ("{0,-20} identical" -f $name) -ForegroundColor Green }
    }
    else {
        Write-Host ("{0,-20} CHANGED  expected {1}  actual {2}" -f $name, $expected[$name].Substring(0, 16), $actual.Substring(0, 16)) -ForegroundColor Yellow
        $failed = $true
    }
}

if ($failed) {
    Write-Host ''
    Write-Host 'At least one supplied data file differs from the employer revision.' -ForegroundColor Yellow
    Write-Host 'This is allowed (it is how the live data-reload demo is performed), but the'
    Write-Host 'bundled fallback shipped in the APK will no longer be the pristine revision.'
    exit 1
}

if (-not $Quiet) { Write-Host ''; Write-Host 'All supplied data files are byte-identical to the employer revision.' -ForegroundColor Green }
exit 0
