# Makes the release zip: dist\BreathingRoom-<version>-windows.zip with the exe, a short read-me and the license.
# Run through "build.cmd package" so the exe is fresh and the tests have passed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = ([regex]::Match((Get-Content "$root\src\AppInfo.cs" -Raw), 'Version = "([^"]+)"')).Groups[1].Value
if (-not $version) { throw "Could not read the version from src\AppInfo.cs" }

$name = "BreathingRoom-$version-windows"
$stage = Join-Path $root "dist\$name"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item "$root\bin\BreathingRoom.exe" $stage
Copy-Item "$root\LICENSE" "$stage\LICENSE.txt"
@"
Breathing Room $version
=======================

A desk cat that helps you notice your smoking. No rules, no lectures.

Start:     double-click BreathingRoom.exe (Windows 10 or 11, nothing to install).
           Windows may say it protected your PC because the app isn't signed:
           click "More info", then "Run anyway".
Log one:   press Ctrl+Alt+S when you light a cigarette.
Your data: %APPDATA%\BreathingRoom  (export or delete it from Settings)
Remove:    untick "Start with Windows" in Settings, quit from the tray icon,
           then delete BreathingRoom.exe (and the data folder, if you like).

Source, help and updates: see the project page on GitHub.
License: MIT (see LICENSE.txt). Cat recordings: CC0, by Joseph Sardin (BigSoundBank).
"@ | Set-Content -Encoding UTF8 "$stage\README.txt"

$zip = Join-Path $root "dist\$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Remove-Item $stage -Recurse -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
"$hash  $name.zip" | Set-Content -Encoding ASCII "$zip.sha256"
Write-Host "Packaged $zip"
Write-Host "SHA-256  $hash"
