# Starts the content server for whoever received this folder.
#
# The server needs nothing but Node.js and the files beside it: the pristine content is in default-data/,
# the copy it actually serves is content/, and the editor page is in public/. This script works out this
# machine's address on the local network, writes that address into the working copy's dev.json - the
# pristine copy points at loopback, which is the one address a phone cannot use - and then starts the
# server in this window.
#
# Nothing here writes to default-data/. That is the supplied content, and content/ is what you edit.
#
#   Double-click start-server.cmd, or run one of these:
#   powershell -NoProfile -ExecutionPolicy Bypass -File start-server.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File start-server.ps1 -Port 8790

[CmdletBinding()]
param(
    [int]$Port = 8787
)

$ErrorActionPreference = "Stop"

$here = $PSScriptRoot
if (-not $here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }

Write-Host ""
Write-Host "Small Pot content server" -ForegroundColor Cyan

# Node.js: either the copy that travelled with this folder or one that is installed on the machine.
$node = Join-Path $here "node.exe"
if (-not (Test-Path $node)) {
    $found = Get-Command node -ErrorAction SilentlyContinue
    if ($found) { $node = $found.Source } else { $node = $null }
}

if (-not $node) {
    Write-Host ""
    Write-Host "Node.js was not found." -ForegroundColor Yellow
    Write-Host "Install the LTS build from https://nodejs.org and run this again. The server needs no packages"
    Write-Host "of its own - node.exe on its own is the whole dependency."
    Write-Host ""
    exit 1
}

Write-Host ("Node.js            : " + (& $node --version))
Write-Host ("Content            : " + (Join-Path $here "default-data") + " (read-only)")

# The address a phone can use. Preferring the adapter that has a default gateway keeps virtual switches out
# of the answer: there are usually several of those and every one of them is an address a phone cannot use.
$lan = $null
$addresses = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" })
foreach ($address in $addresses) {
    $config = Get-NetIPConfiguration -InterfaceIndex $address.InterfaceIndex -ErrorAction SilentlyContinue
    if ($config -and $config.IPv4DefaultGateway) { $lan = $address.IPAddress; break }
}
if (-not $lan -and $addresses.Count -gt 0) { $lan = $addresses[0].IPAddress }

# dev.json is the one file in the working copy whose value has to match this machine rather than the phone.
$devPristine = Join-Path $here "default-data\dev.json"
$devWorking = Join-Path $here "content\dev.json"
if ((Test-Path $devPristine) -and $lan) {
    $contentDirectory = Join-Path $here "content"
    if (-not (Test-Path $contentDirectory)) { New-Item -ItemType Directory -Path $contentDirectory | Out-Null }

    $settings = Get-Content $devPristine -Raw | ConvertFrom-Json
    $settings.dataServerUrl = "http://${lan}:$Port"

    # -Force, because the file may not carry this key at all and PowerShell will not create it by assignment.
    $settings | Add-Member -NotePropertyName "dataServerUrls" -NotePropertyValue @("http://${lan}:$Port") -Force

    $wanted = ($settings | ConvertTo-Json)
    $current = ""
    if (Test-Path $devWorking) { $current = Get-Content $devWorking -Raw }
    if ($current.Trim() -ne $wanted.Trim()) {
        # Written without a byte-order mark: Set-Content -Encoding UTF8 adds one on PowerShell 5.1, and a BOM
        # in front of the opening brace is not valid JSON to the app or to the server's own check.
        [System.IO.File]::WriteAllText($devWorking, $wanted, (New-Object System.Text.UTF8Encoding($false)))
    }
}

# Inbound connections need a firewall rule for this port. The port is what is looked for, not just the
# rule's name: a rule with the right name and a different port would leave a phone unable to connect while
# this script said nothing at all.
function Test-PortAllowed([int]$Candidate) {
    $filters = @(Get-NetFirewallRule -DisplayName "Small Pot content server" -ErrorAction SilentlyContinue |
        Get-NetFirewallPortFilter -ErrorAction SilentlyContinue)
    foreach ($filter in $filters) {
        if ($filter.LocalPort -eq "Any" -or $filter.LocalPort -eq "$Candidate") { return $true }
    }
    return $false
}

$allowed = Test-PortAllowed $Port
if (-not $allowed) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $administrator = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)

    if ($administrator) {
        try {
            New-NetFirewallRule -DisplayName "Small Pot content server" -Direction Inbound -Action Allow `
                -Protocol TCP -LocalPort $Port -Profile Private | Out-Null
            $allowed = Test-PortAllowed $Port
            if ($allowed) { Write-Host "Added the firewall rule for inbound port $Port." }
        } catch {
            $allowed = $false
        }
    }
}

Write-Host ""
if ($lan) {
    Write-Host "Type this address into the app, once:" -ForegroundColor Green
    Write-Host "  http://${lan}:$Port" -ForegroundColor Green
} else {
    Write-Host "No network address was found on this machine, so the app will need one typed in by hand."
}

if (-not $allowed) {
    Write-Host ""
    Write-Host "A phone on the same Wi-Fi also needs this port let through the firewall. Run this once in an"
    Write-Host "administrator PowerShell:"
    Write-Host "  New-NetFirewallRule -DisplayName 'Small Pot content server' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Private"
}

Write-Host ""
Write-Host "The editor, in a browser on this machine : http://127.0.0.1:$Port/"
Write-Host "Over a USB cable instead, with the phone attached: adb reverse tcp:$Port tcp:$Port"
Write-Host "  then type http://127.0.0.1:$Port into the app."
Write-Host ""
Write-Host "Press Ctrl+C to stop the server. Leave this window open while the app is in use."
Write-Host ""

Push-Location $here
try {
    & $node (Join-Path $here "server.js") $Port
} finally {
    Pop-Location
}
