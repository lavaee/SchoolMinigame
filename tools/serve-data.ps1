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
    # stderr is sent to null rather than into the pipeline: the first adb command of a session prints
    # "daemon not running; starting now" there, and with $ErrorActionPreference = "Stop" that notice would
    # end this script before the server ever started.
    $devices = @()
    try {
        # Ask for the daemon explicitly: the first adb command of a session starts it, and that first run
        # usually lists no devices at all.
        & $adb start-server 2>$null | Out-Null
        $devices = & $adb devices 2>$null | Select-String -Pattern "\tdevice$"
    } catch {
        $devices = @()
    }

    $serials = @($devices | ForEach-Object { ($_.Line -split "\s+")[0] } | Where-Object { $_ })

    if ($serials.Count -gt 0) {
        # Every attached device gets the forward: a headset and a phone can both be plugged in at once, and
        # without naming one, adb refuses the command outright ("more than one device/emulator").
        foreach ($serial in $serials) {
            & $adb -s $serial reverse "tcp:$Port" "tcp:$Port" 2>$null | Out-Null
        }

        Write-Host "Forwarded tcp:$Port to $($serials.Count) attached device(s) over USB (type http://127.0.0.1:$Port into the app by hand to use it)."
    } else {
        Write-Host "No device is attached, so the port was not forwarded."
    }
}

# A phone reaches this machine at its address on the network, not at 127.0.0.1, so work that address out
# first. Preferring the adapter that has a default gateway keeps virtual switches out of the answer: there
# are usually several of those and every one of them is an address a phone cannot use.
$lan = $null
$addresses = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" })
foreach ($address in $addresses) {
    $config = Get-NetIPConfiguration -InterfaceIndex $address.InterfaceIndex -ErrorAction SilentlyContinue
    if ($config -and $config.IPv4DefaultGateway) { $lan = $address.IPAddress; break }
}

if (-not $lan -and $addresses.Count -gt 0) { $lan = $addresses[0].IPAddress }

Write-Host ""
if ($lan) {
    Write-Host "A phone on the same network can use: http://${lan}:$Port"
} else {
    Write-Host "No network address was found, so a phone will need one typed into it by hand."
}

# The working copy is what a connected phone actually reads, and our own two files are not the employer's:
# they are this project's settings and the words for the screens it added, so a server started from an older
# checkout would serve an older copy of them and the app would be missing strings that exist in the
# repository. Align them before starting, and leave the employer's files alone.
#
# dev.json is the one file where the committed copy belongs to the developer rather than the phone: it
# points at 127.0.0.1, which is exactly the address a phone cannot reach. It is written with the network
# address in it instead, so a connected device is never handed a list it has to throw away.
$content = Join-Path $root "server\content"
if (Test-Path $content) {
    $extraSource = Join-Path $root "data\strings-extra.json"
    $extraWorking = Join-Path $content "strings-extra.json"
    if (Test-Path $extraSource) {
        $current = ""
        if (Test-Path $extraWorking) { $current = Get-Content $extraWorking -Raw }
        $wanted = Get-Content $extraSource -Raw
        if ($current -ne $wanted) {
            Copy-Item $extraSource $extraWorking -Force
            Write-Host "Copied data\strings-extra.json over the server's working copy, which had drifted from it."
        }
    }

    $devSource = Join-Path $root "data\dev.json"
    $devWorking = Join-Path $content "dev.json"
    if ((Test-Path $devSource) -and $lan) {
        $settings = Get-Content $devSource -Raw | ConvertFrom-Json
        $settings.dataServerUrl = "http://${lan}:$Port"

        # -Force, because the committed file may not carry this key at all and PowerShell will not create
        # it by assignment.
        $settings | Add-Member -NotePropertyName "dataServerUrls" -NotePropertyValue @("http://${lan}:$Port") -Force
        $wanted = ($settings | ConvertTo-Json)
        $current = ""
        if (Test-Path $devWorking) { $current = Get-Content $devWorking -Raw }
        if ($current.Trim() -ne $wanted.Trim()) {
            # Written without a byte-order mark: Set-Content -Encoding UTF8 adds one on PowerShell 5.1, and a
            # BOM in front of the opening brace is not valid JSON to the app or to the server's own check.
            [System.IO.File]::WriteAllText($devWorking, $wanted, (New-Object System.Text.UTF8Encoding($false)))
            Write-Host "Wrote the network address into the server's dev.json, which points at loopback otherwise."
        }
    }

    Write-Host "Press 'Push to clients' in the admin page to send any of the above to an app that is already open."
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
