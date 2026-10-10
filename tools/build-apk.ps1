# Builds the Android APK and checks what ended up inside it.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-apk.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-apk.ps1 -DataServerUrl http://192.168.1.20:8787
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-apk.ps1 -NoDataServer
#
# The addresses the phone should try are baked into the StreamingAssets copy of data/dev.json. Left
# alone, the default is this machine's address on the local network. 127.0.0.1 is never baked: the phone
# is a different machine and cannot reach it. A USB cable still works - forward the port with
# "adb reverse tcp:8787 tcp:8787" and type 127.0.0.1 into the app by hand, where a typed address is
# obeyed. The committed data/dev.json is never rewritten by a build.

[CmdletBinding()]
param(
    [string]$DataServerUrl = "",
    [int]$DataServerPort = 8787,
    [switch]$NoDataServer,
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

# The phone cannot reach the PC's 127.0.0.1, so the default has to be this machine's address on the
# network. Preferring the adapter that has a default gateway keeps virtual switches out of the answer.
function Get-LanAddress {
    try {
        $addresses = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
            Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" })
    } catch {
        return $null
    }

    foreach ($address in $addresses) {
        $config = Get-NetIPConfiguration -InterfaceIndex $address.InterfaceIndex -ErrorAction SilentlyContinue
        if ($config -and $config.IPv4DefaultGateway) { return $address.IPAddress }
    }

    return ($addresses | Select-Object -First 1 -ExpandProperty IPAddress)
}

$candidates = @()
if ($NoDataServer) {
    Write-Host "No data server: the app will run from the bundled files only."
} else {
    if ($DataServerUrl) {
        $candidates += $DataServerUrl
    } else {
        $lan = Get-LanAddress
        if (-not $lan) {
            throw "No local network address was found; pass -DataServerUrl, or -NoDataServer for a bundled-only build."
        }

        $candidates += "http://${lan}:$DataServerPort"
    }

    # 127.0.0.1 is deliberately not added. The phone is a different machine, so loopback is an address it
    # can never reach; shipping it only fills the settings panel and the failure list with something that
    # was never going to work. The app also ignores loopback addresses on a device, so a stale one in a
    # cached or served file cannot bring it back. A USB cable still works: forward the port with
    # "adb reverse tcp:<port> tcp:<port>" and type 127.0.0.1 into the app by hand.
    $candidates = @($candidates | Select-Object -Unique)
    Write-Host "The app will try: $($candidates -join ', ')"
}

& (Join-Path $PSScriptRoot "sync-data.ps1")
if ($LASTEXITCODE -ne 0) { throw "Copying the data files into StreamingAssets failed." }

# The addresses are handed to the build rather than written here, because the build syncs the data files
# into StreamingAssets itself and would overwrite anything written before it. Unity applies this variable
# straight after that sync, so what ships is decided in one place and cannot be lost in the middle.
$env:SMALLPOT_DATA_SERVER_URLS = ($candidates -join ",")

# The copy that ships gets the addresses above; it is put back to the committed default after the build,
# because the repository should not hold one machine's network address and the editor wants localhost.
$baked = Join-Path $root "Assets\StreamingAssets\data\dev.json"

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

# The addresses the APK needs are inside the APK now, so put the committed default back: the repository
# should not end up holding one machine's network address, and the editor still wants localhost.
Copy-Item (Join-Path $root "data\dev.json") $baked -Force
Write-Host "Restored the committed data/dev.json default into StreamingAssets."

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
Write-Host ("Size   : {0:N2} MB" -f ($file.Length / 1MB))
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
