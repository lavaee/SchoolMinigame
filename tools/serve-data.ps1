# Starts the data server and, when a phone is attached, forwards the port over USB.
#
# The server also hosts the content editor, so this is the one command needed before a live demo:
# open http://127.0.0.1:<port>/ to edit the working copy, and press "Push to clients" to make a
# running app reload.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\serve-data.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\serve-data.ps1 -Port 8787

[CmdletBinding()]
param(
    [int]$Port = 8787
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$node = "C:\Program Files\nodejs\node.exe"
if (-not (Test-Path $node)) {
    $node = (Get-Command node -ErrorAction SilentlyContinue).Source
}

if (-not $node) {
    throw "Node.js was not found; install it or edit this script to point at node.exe."
}

# adb reverse makes http://127.0.0.1:<port> work on the phone over USB, whatever the Wi-Fi is doing.
$adb = Get-ChildItem "F:\Unity\Editors\2022.3.62f3\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" `
    -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName

if ($adb) {
    $devices = & $adb devices 2>&1 | Select-String -Pattern "\tdevice$"
    if ($devices) {
        & $adb reverse "tcp:$Port" "tcp:$Port" | Out-Null
        Write-Host "Forwarded tcp:$Port to the attached device. Use http://127.0.0.1:$Port in the app."
    } else {
        Write-Host "No device is attached, so the port was not forwarded."
    }
}

Write-Host "Starting the data server; press Ctrl+C to stop."
Write-Host ""
& $node (Join-Path $root "server\serve.js") $Port
