# UI smoke test: runs the built app in test mode against throwaway data, clicks through every
# screen, and checks that the data and windows behave and that nothing was written to errors.log.
#   powershell -ExecutionPolicy Bypass -File scripts\ui-smoke.ps1
# Needs an interactive desktop (it opens real windows for a minute).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'bin\BreathingRoom.exe'
if (-not (Test-Path $exe)) { throw "Build first: build.cmd" }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$script:failures = 0
function Check($ok, $name) { if ($ok) { Write-Host "  ok    $name" } else { Write-Host "  FAIL  $name" -ForegroundColor Red; $script:failures++ } }

$dir = Join-Path $env:TEMP ("br-smoke-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $dir | Out-Null
$env:BR_DATA_DIR = $dir
function Send($cmd, $wait = 1200) { Set-Content (Join-Path $dir 'command.txt') $cmd; Start-Sleep -Milliseconds $wait }
function Data { Get-Content (Join-Path $dir 'data.json') -Raw | ConvertFrom-Json }
# Only windows of the copy under test, so a Breathing Room you have running is never touched.
function Win($title) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $title)),
        (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $script:p.Id)))
    $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}
function CloseWin($title) { $w = Win $title; if ($w) { $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -Milliseconds 500; $true } else { $false } }
function Texts($w) { $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name } }

Write-Host "First run"
$script:p = Start-Process $exe -PassThru
$p = $script:p
Start-Sleep -Seconds 3
$welcome = Win 'Breathing Room'
Check ($welcome -ne $null) 'welcome window opens on first run'
$go = $welcome.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, "Let's go")))
$go.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 2
Check ((Data).Welcomed -eq $true) 'welcome is remembered'

Write-Host "Logging"
Send 'log'; Send 'log'; Send 'log'
Check (@((Data).Smokes).Count -eq 3) 'three logs saved'
Send 'retro'
Send 'insights' 1800
$ins = Win "How's it going"
Check ($ins -ne $null) 'insights opens'
Send 'log' 1500
$t = @(Texts $ins); $i = [array]::IndexOf($t, 'Today')
Check ($i -ge 0 -and $t[$i + 1] -eq '4') 'insights updates live after a log'
Check (CloseWin "How's it going") 'insights closes'

Write-Host "Bubble"
function Click($label) {
    $b = Win 'Breathing Room bubble'
    if (-not $b) { return $false }
    $btn = $b.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $label)))
    if (-not $btn) { return $false }
    $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); $true
}
Start-Sleep -Seconds 13   # let the last log's bubble close on its own
$before = @((Data).Smokes).Count
Send 'missed' 900
Check (Click 'Yeah, I did') 'slip question answers in the bubble'
Start-Sleep -Milliseconds 700
Check (Click '~10 min ago') 'follow-up "when was it?" answers'
Start-Sleep -Milliseconds 900
Check (@((Data).Smokes).Count -eq $before + 1) 'a forgotten one is added from the bubble'

Write-Host "Cat"
Send 'menu'; Send 'breathe' 2500; Send 'missed'; Send 'back'; Send 'later' 2500
Send 'soundinfo' 1500
$info = Get-Content (Join-Path $dir 'soundinfo.txt') -Raw
Check ($info -match 'all loadable: True') 'all embedded recordings load'
Check ($info -notmatch 'synth') 'no synthesized sounds'

Write-Host "Windows"
foreach ($pair in @(@('settings', 'Breathing Room settings'), @('voice', "Mochi's voice"), @('about', 'About Breathing Room'), @('look', "Mochi's look"), @('wave', 'Ride the wave'))) {
    Send $pair[0] 1500
    Check (CloseWin $pair[1]) ($pair[1] + ' opens and closes')
}

Write-Host "Play"
foreach ($g in @('fetch:living', 'laser:garden', 'treats:night', 'bubbles:cafe', 'mouse:cafe', 'hide:garden', 'flight:living', 'run:night', 'cafe:cafe')) {
    Send "play:$g" 2500
    Check (CloseWin 'Play') "play $g opens and closes"
}

Write-Host "Pictures"
$snaps = Join-Path $dir 'snaps'
Send "snap:$snaps" 20000
Check (@(Get-ChildItem $snaps -Filter *.png).Count -ge 18) 'README pictures render'

Write-Host "Friendship"
Send 'friend:12' 1200
Check ((Data).Friendship -ge 12) 'friendship points are saved'

Write-Host "Idle cost"
Start-Sleep -Seconds 11   # the friendship level-up above opens a 10 s bubble; measure after it closes
$p.Refresh(); $before = $p.TotalProcessorTime
Start-Sleep -Seconds 10
$p.Refresh(); $used = ($p.TotalProcessorTime - $before).TotalMilliseconds
Check ($used -lt 1500) ("idle CPU over 10 s: {0:0} ms" -f $used)

Send 'quit' 500
Check ($p.WaitForExit(5000)) 'quits cleanly'
$log = Join-Path $dir 'errors.log'
if (Test-Path $log) { Get-Content $log | Write-Host }
Check (-not (Test-Path $log)) 'no errors logged'
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue

if ($script:failures -gt 0) { Write-Host "$($script:failures) check(s) failed" -ForegroundColor Red; exit 1 }
Write-Host "All UI checks passed" -ForegroundColor Green
