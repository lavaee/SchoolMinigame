# Builds the folder that gets handed to somebody else.
#
# The content server normally reads the supplied files from data\ at the project root, which is where the
# brief puts them and where the editor build reads them from. A copy of the server that travels to another
# machine cannot rely on that, so this script gives it its own pristine content in default-data\ beside
# server.js, seeds a working copy from it, and copies the launcher, the editor page and the test suite.
# server.js prefers default-data\ whenever that folder exists, so the same file serves both layouts.
#
# It then proves the result stands alone: the package is copied to a temporary directory with no repository
# above it, started there on a spare port, and asked where its content came from. The answer has to be the
# package's own default-data\, or this fails.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\package-server.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\package-server.ps1 -IncludeNode $false -IncludeApk $false
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\package-server.ps1 -Zip Builds\Server.zip

[CmdletBinding()]
param(
    [string]$OutputDirectory = "Builds\SmallPot-Server",
    [string]$Zip = "Builds\SmallPot-Server.zip",
    [bool]$IncludeNode = $true,
    [bool]$IncludeApk = $true
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

function Resolve-UnderRoot([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return $Path }
    return (Join-Path $root $Path)
}

$serverDirectory = Join-Path $root "server"
$dataDirectory = Join-Path $root "data"
$output = Resolve-UnderRoot $OutputDirectory
$zipPath = Resolve-UnderRoot $Zip

if (-not (Test-Path (Join-Path $dataDirectory "config.json"))) {
    throw "data\config.json is missing. That folder is the supplied content and the source of this package."
}

# Node.js, which is the server's only dependency, and which this machine is asked for rather than assumed.
$node = "C:\Program Files\nodejs\node.exe"
if (-not (Test-Path $node)) {
    $found = Get-Command node -ErrorAction SilentlyContinue
    if ($found) { $node = $found.Source } else { $node = $null }
}
$nodeVersion = $null
$nodeBytes = 0
if ($node) {
    $nodeVersion = (& $node --version)
    $nodeBytes = (Get-Item $node).Length
}

Write-Host ""
Write-Host "Packaging the content server" -ForegroundColor Cyan
Write-Host ""

# 1. A clean folder to build in, so nothing from a previous run can be mistaken for this one.
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Path $output | Out-Null

# 2. The server, the suite that proves its protocol, the editor page and the launcher.
Copy-Item (Join-Path $serverDirectory "server.js") $output
Copy-Item (Join-Path $serverDirectory "test.js") $output
Copy-Item (Join-Path $serverDirectory "public") $output -Recurse
Copy-Item (Join-Path $serverDirectory "package\*") $output -Recurse

# 3. The supplied content, copied and then checked rather than assumed. This is the one place in the project
#    where those files exist twice, so it is the one place a check is worth having.
$defaults = Join-Path $output "default-data"
New-Item -ItemType Directory -Path $defaults | Out-Null
Copy-Item (Join-Path $dataDirectory "*.json") $defaults

$supplied = @(Get-ChildItem (Join-Path $dataDirectory "*.json"))
$copied = @(Get-ChildItem (Join-Path $defaults "*.json"))
$drifted = @()
foreach ($file in $supplied) {
    $copy = Join-Path $defaults $file.Name
    if (-not (Test-Path $copy)) {
        $drifted += "$($file.Name) (missing)"
    } elseif ((Get-FileHash $file.FullName).Hash -ne (Get-FileHash $copy).Hash) {
        $drifted += "$($file.Name) (different)"
    }
}
if ($copied.Count -ne $supplied.Count) { $drifted += "the file count differs" }
if ($drifted.Count -gt 0) { throw ("The packaged content does not match data\: " + ($drifted -join ", ")) }
Write-Host ("  content            : " + $supplied.Count + " files from data\, byte for byte")

# 4. A working copy, seeded from the pristine one, so the editor page is usable the moment it is opened.
$content = Join-Path $output "content"
New-Item -ItemType Directory -Path $content | Out-Null
Copy-Item (Join-Path $defaults "*.json") $content
Write-Host "  working copy       : seeded and ready to edit"

# 5. The game and Node.js, so the other machine needs nothing installed at all.
if ($IncludeApk) {
    $apk = Join-Path $root "Builds\SmallPot.apk"
    if (Test-Path $apk) {
        Copy-Item $apk (Join-Path $output "SmallPot.apk")
        Write-Host ("  the app            : SmallPot.apk, " + [math]::Round((Get-Item $apk).Length / 1MB, 1) + " MB")
    } else {
        Write-Host "  the app            : no APK at Builds\SmallPot.apk, so none was included" -ForegroundColor Yellow
    }
} else {
    Write-Host "  the app            : left out, as asked"
}

if ($IncludeNode -and $nodeVersion) {
    Copy-Item $node (Join-Path $output "node.exe")
    Write-Host ("  Node.js            : " + $nodeVersion + " bundled, " + [math]::Round($nodeBytes / 1MB, 1) + " MB")
} elseif ($IncludeNode) {
    Write-Host "  Node.js            : not found on this machine, so it was not bundled" -ForegroundColor Yellow
} else {
    Write-Host "  Node.js            : left out, as asked"
}

# 6. Prove it stands alone. The copy is put somewhere with nothing above it - no repository, no data\ - and
#    started there. Where it says its content came from is the check: if it still found this repository, the
#    package would break on the machine it is meant for.
$probe = Join-Path ([System.IO.Path]::GetTempPath()) ("smallpot-package-probe-" + [guid]::NewGuid().ToString("N"))
$probeProcess = $null
try {
    New-Item -ItemType Directory -Path $probe | Out-Null
    Copy-Item (Join-Path $output "*") $probe -Recurse

    if ($nodeVersion) {
        # A spare port, taken and released rather than guessed, so a server already running cannot collide.
        $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $port = $listener.LocalEndpoint.Port
        $listener.Stop()

        $probeNode = Join-Path $probe "node.exe"
        if (-not (Test-Path $probeNode)) { $probeNode = $node }

        $out = Join-Path $probe "probe-out.txt"
        $err = Join-Path $probe "probe-err.txt"
        $probeProcess = Start-Process -FilePath $probeNode `
            -ArgumentList @((Join-Path $probe "server.js"), $port) `
            -WorkingDirectory $probe -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $out -RedirectStandardError $err

        $state = $null
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Milliseconds 250
            try { $state = Invoke-RestMethod "http://127.0.0.1:$port/api/state" -TimeoutSec 3 } catch { $state = $null }
            if ($state) { break }
        }

        if (-not $state) {
            $said = (Get-Content $out -Raw -ErrorAction SilentlyContinue) + (Get-Content $err -Raw -ErrorAction SilentlyContinue)
            throw "The packaged server did not answer on port $port. It said:`n$said"
        }

        $ownCopy = Join-Path $probe "default-data"
        if ($state.directories.supplied -ne $ownCopy) {
            throw "The packaged server read its content from $($state.directories.supplied) instead of its own $ownCopy. It would not work on another machine."
        }
        if (-not $state.ok) { throw "The packaged server reported that its content is not valid." }

        Write-Host "  stands alone       : started from $probe" -ForegroundColor Green
        Write-Host "                       and served its own default-data (version $($state.version))"
    } else {
        Write-Host "  stands alone       : not checked, because there is no Node.js here to run it" -ForegroundColor Yellow
    }
} finally {
    if ($probeProcess -and -not $probeProcess.HasExited) {
        Stop-Process -Id $probeProcess.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 400
    Remove-Item $probe -Recurse -Force -ErrorAction SilentlyContinue
}

# 7. The zip, and the numbers to quote when handing it over.
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $output -DestinationPath $zipPath -CompressionLevel Optimal

$folderBytes = (Get-ChildItem $output -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ""
Write-Host "Folder : $output"
Write-Host ("         " + [math]::Round($folderBytes / 1MB, 1) + " MB, " + (Get-ChildItem $output -Recurse -File).Count + " files")
Write-Host "Zip    : $zipPath"
Write-Host ("         " + [math]::Round((Get-Item $zipPath).Length / 1MB, 1) + " MB")
Write-Host ("         SHA256 " + (Get-FileHash $zipPath).Hash)
Write-Host ""
Write-Host "On the other machine: unzip it anywhere and double-click start-server.cmd."
Write-Host "It prints the one address to type into the app."
