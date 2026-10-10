# Starts the data server and, when a phone is attached, forwards the port over USB.
#
# The server also hosts the content editor, so this is the one command needed before a live demo:
# open http://127.0.0.1:<port>/ to edit the working copy, and press "Push to clients" to make a
# running app reload.
#
# It prints the addresses a phone can use - the machine on the local network over Wi-Fi, and 127.0.0.1
# over USB - and checks the firewall rule that inbound connections need.
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

# adb reverse makes http://127.0.0.1:<port> reach this machine from the phone over USB. The app does not
# try loopback on a device - a phone is a different machine - so the address has to be typed into the app
# by hand; a typed address is obeyed.
$adb = Get-ChildItem "F:\Unity\Editors\2022.3.62f3\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" `
    -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName

if ($adb) {
    $devices = & $adb devices 2>&1 | Select-String -Pattern "\tdevice$"
    if ($devices) {
        & $adb reverse "tcp:$Port" "tcp:$Port" | Out-Null
        Write-Host "Forwarded tcp:$Port to the attached device (type http://127.0.0.1:$Port into the app to use it)."
    } else {
        Write-Host "No device is attached, so the port was not forwarded."
    }
}

# The working copy is what a connected phone actually reads, and our own two files are not the employer's:
# they are this project's settings and the words for the screens it added, so a server started from an
# older checkout would serve an older copy of them and the app would be missing strings that exist in the
# repository. Align them before starting, and leave the employer's files alone.
foreach ($name in @("dev.json", "strings-extra.json")) {
    $supplied = Join-Path $root "data\$name"
    $working = Join-Path $root "server\content\$name"
    if ((Test-Path $supplied) -and (Test-Path (Split-Path -Parent $working))) {
        $current = ""
        if (Test-Path $working) { $current = Get-Content $working -Raw }
        $wanted = Get-Content $supplied -Raw
        if ($current -ne $wanted) {
            Copy-Item $supplied $working -Force
            Write-Host "Copied data\$name over the server's working copy, which had drifted from it."
            Write-Host "  Press 'Push to clients' in the admin page to send it to an app that is already open."
        }
    }
}

# A phone reaches this machine at its address on the network, not at 127.0.0.1, so print the addresses
# worth typing into the app (the settings panel takes a server address) instead of leaving it to guesswork.
$addresses = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" })
if ($addresses) {
    Write-Host ""
    Write-Host "A phone on the same network can use:"
    foreach ($address in $addresses) {
        Write-Host "  http://$($address.IPAddress):$Port"
    }
}

# Windows Firewall blocks inbound connections by default; this is the usual reason a phone cannot reach
# the server even when both are on the same Wi-Fi. The rule is private-profile only, and it is added
# only when this script is already running as an administrator.
$ruleName = "Small Pot data server"
$rule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if (-not $rule) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow `
            -Protocol TCP -LocalPort $Port -Profile Private | Out-Null
        Write-Host "Added a firewall rule for inbound TCP $Port on private networks."
    } else {
        Write-Host ""
        Write-Host "If the phone cannot connect, allow the port once from an admin PowerShell:"
        Write-Host "  New-NetFirewallRule -DisplayName '$ruleName' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Private"
    }
}

Write-Host "Starting the data server; press Ctrl+C to stop."
Write-Host ""
& $node (Join-Path $root "server\serve.js") $Port
