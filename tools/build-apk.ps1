# Builds the Android APK and checks what ended up inside it.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-apk.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-apk.ps1 -DataServerUrl http://127.0.0.1:8787
#
# The optional data server URL is baked into data/dev.json, which is how the phone learns where to
# fetch fresh content from. Without it the game still runs entirely from the bundled files.

[CmdletBinding()]
param(
    [string]$DataServerUrl = "",
    [switch]$SkipDataChecks
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$unity = "F:\Unity\Editors\2022.3.62f3\Editor\Unity.exe"
$apk = Join-Path $root "Builds\SmallPot.apk"
$log = Join-Path $root "Logs\build-android.log"

if (-not (Test-Path $unity)) {
    throw "Unity was not found at $unity"
}

if (-not $SkipDataChecks) {
    & (Join-Path $PSScriptRoot "verify-data.ps1")
    if ($LASTEXITCODE -ne 0) { throw "The supplied data files do not match the originals." }
}

if ($DataServerUrl) {
    Write-Host "Baking data server URL: $DataServerUrl"
    $dev = @"
{
  "version": 1,
  "dataServerUrl": "$DataServerUrl",
  "showDataSourceInfo": true,
  "showDebugPanel": true
}
"@
    [System.IO.File]::WriteAllText(
        (Join-Path $root "data\dev.json"), $dev, (New-Object System.Text.UTF8Encoding($false)))
}

& (Join-Path $PSScriptRoot "sync-data.ps1")
if ($LASTEXITCODE -ne 0) { throw "Copying the data files into StreamingAssets failed." }

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $log) | Out-Null
if (Test-Path $apk) { Remove-Item $apk -Force }

Write-Host "Building the Android player (this takes a few minutes)..."

# Unity.exe is a GUI-subsystem binary, so a plain call can return before the editor has finished.
# Start-Process -Wait is the reliable way to wait for the build to actually complete.
$arguments = @(
    "-batchmode",
    "-quit",
    "-projectPath", $root,
    "-executeMethod", "SmallPot.EditorTools.BuildScript.BuildAndroid",
    "-logFile", $log
)

$process = Start-Process -FilePath $unity -ArgumentList $arguments -Wait -NoNewWindow -PassThru
Write-Host "Unity exited with code $($process.ExitCode)."

$errors = Select-String -Path $log -Pattern "error CS|Build completed with a result of 'Failed'" -ErrorAction SilentlyContinue
if ($errors) {
    $errors | ForEach-Object { Write-Host $_.Line.Trim() }
    throw "The build reported errors; see $log"
}

if (-not (Test-Path $apk)) {
    throw "No APK was produced; see $log"
}

$file = Get-Item $apk
$hash = (Get-FileHash $apk -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ""
Write-Host "APK    : $($file.FullName)"
Write-Host "Size   : {0:N2} MB" -f ($file.Length / 1MB)
Write-Host "SHA256 : $hash"

# What actually ended up in the manifest matters: cleartext HTTP has to be allowed for the optional
# data server, and the Unity launcher activity has to survive the custom manifest.
$aapt2 = Get-ChildItem "F:\Unity\Editors\2022.3.62f3\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\build-tools" `
    -Filter aapt2.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName

if ($aapt2) {
    Write-Host ""
    Write-Host "Manifest:"
    $badging = & $aapt2 dump badging $apk 2>&1
    $badging | Select-String -Pattern "^package|sdkVersion|targetSdkVersion|native-code|application-label" |
        ForEach-Object { "  " + $_.Line.Trim() }

    $tree = & $aapt2 dump xmltree $apk --file AndroidManifest.xml 2>&1
    $cleartext = ($tree | Select-String -Pattern "usesCleartextTraffic" | Select-Object -First 1)
    $activity = ($tree | Select-String -Pattern "com.unity3d.player.UnityPlayerActivity" | Select-Object -First 1)

    if ($cleartext) { Write-Host "  usesCleartextTraffic is present" } else { Write-Warning "usesCleartextTraffic is missing from the built manifest." }
    if ($activity) { Write-Host "  the Unity launcher activity is present" } else { Write-Warning "The launcher activity is missing from the built manifest." }
}

Write-Host ""
Write-Host "Install with: adb install -r `"$apk`""
