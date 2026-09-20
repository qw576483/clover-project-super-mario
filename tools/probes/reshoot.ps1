# One-off driver (deleted after use, skill 1.8). Same idea as run.ps1 but gives EVERY scene its own
# fresh Play session: a scene that ends in Result / GameOver / Boot leaves the FSM in a state where the
# next scene's "BackToMain -> Menu" never lands, which silently produced blank (single-colour) frames.
# ASCII-only on purpose (PS 5.1 parses a BOM-less .ps1 as ANSI, so CJK literals silently break -match).
param(
    [Parameter(Mandatory = $true)][string[]]$Scene,
    [int]$SceneTimeout = 150,
    [int]$PlayWaitSec = 45
)
$ErrorActionPreference = 'Continue'
$client = 'client'
$probe  = 'tools\probes\probe.cs'
# NOTE: take the log of the CURRENT day - after midnight the file name changes and the old
# one is never appended to again (measured: every scene then "times out" while actually running).
$log    = Join-Path $client ('Logs\' + (Get-Date -Format 'yyyy-MM-dd') + '.log')
$menuMark = ([char]0x2192) + ' Menu'                                                   # "-> Menu"
$doneMark = ([char[]]@(0x573A,0x666F,0x7ED3,0x675F,0xFF1A) -join '')                   # "scene end:"
$startMark = ([char[]]@(0x573A,0x666F,0x5F00,0x59CB,0xFF1A) -join '')                  # "scene start:"
Set-Location $client

function Get-PlayMode {
    $j = unity command editor_status --format json --no-banner 2>&1 | Out-String
    if ($j -match 'playMode[^a-zA-Z]+([a-zA-Z]+)') { return $Matches[1] }
    return 'unknown'
}
function Get-EngineAlive {
    $j = unity command eval --code 'return CloverEngine.Game.IsRunning;' --format json --no-banner 2>&1 | Out-String
    return ($j -match '"result"\s*:\s*true')
}
function LogText {
    if (-not (Test-Path $log)) { return '' }
    try {
        $fs = New-Object System.IO.FileStream($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
        $t = $sr.ReadToEnd()
        $sr.Close(); $fs.Close()
        return $t
    } catch { Write-Output ("WARN log read failed: " + $_.Exception.Message); return '' }
}
function Stop-Play {
    if ((Get-PlayMode) -eq 'playing') {
        unity command editor_stop --no-banner 2>&1 | Out-Null
        for ($i = 0; $i -lt 25; $i++) { Start-Sleep -Seconds 1; if ((Get-PlayMode) -ne 'playing') { return } }
    }
}
function Start-FreshPlay {
    Stop-Play
    unity command editor_play --no-banner 2>&1 | Out-Null
    for ($i = 0; $i -lt [int]($PlayWaitSec / 2); $i++) { Start-Sleep -Seconds 2; if ((Get-PlayMode) -eq 'playing') { break } }
    $ok = $false
    for ($i = 0; $i -lt 15; $i++) { Start-Sleep -Seconds 2; if (Get-EngineAlive) { $ok = $true; break } }
    if (-not $ok) { Write-Output 'WARN engine not alive after fresh play'; return $false }
    for ($i = 0; $i -lt 30; $i++) { Start-Sleep -Seconds 1; if ((LogText) -match [regex]::Escape($menuMark)) { break } }
    return $true
}

# ---- PREFLIGHT (seconds): everything checkable BEFORE the minute-long runs ----
$rxo = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
$probeText = [System.IO.File]::ReadAllText($probe, [System.Text.Encoding]::UTF8)
$entryOf = @{}
foreach ($m in [regex]::Matches($probeText, 'public static void ([A-Za-z0-9_]+)\(\)\s*=>\s*Start\("([a-z0-9\-]+)"')) {
    $entryOf[$m.Groups[2].Value] = $m.Groups[1].Value
}
$scenes = @($Scene | ForEach-Object { $_ -split ',' } | Where-Object { $_ -ne '' })
$unknown = @($scenes | Where-Object { $entryOf.Keys -notcontains $_.ToLower() })
if ($unknown.Count -gt 0) { Write-Output ("PREFLIGHT FAIL: not declared in probe.cs -> " + ($unknown -join ',')); exit 2 }
if ((LogText) -eq '') { Write-Output 'PREFLIGHT FAIL: cannot read the game log'; exit 3 }
Write-Output ("PREFLIGHT OK: scenes=" + ($scenes -join ','))

foreach ($s in $scenes) {
    Write-Output ("---- fresh play for scene: $s ----")
    if (-not (Start-FreshPlay)) { Write-Output ("FRESH_PLAY_FAILED $s"); continue }
    $before = @([regex]::Matches((LogText), [regex]::Escape($doneMark + $s), $rxo)).Count
    $entry = "Probe." + $entryOf[$s.ToLower()]
    $resp = (unity command run_script --file $probe --entry $entry --no-banner 2>&1 | Out-String)
    if ($resp -match 'Entry Point Not Found') { Write-Output ("DISPATCH_FAILED $s : entry not found"); continue }
    $started = $false
    for ($i = 0; $i -lt 8; $i++) {
        Start-Sleep -Seconds 2
        if ((LogText) -match [regex]::Escape($startMark + $s)) { $started = $true; break }
    }
    if (-not $started) { Write-Output ("DISPATCH_FAILED $s : no start marker"); continue }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $done = $false
    while ($sw.Elapsed.TotalSeconds -lt $SceneTimeout) {
        Start-Sleep -Seconds 2
        $after = @([regex]::Matches((LogText), [regex]::Escape($doneMark + $s), $rxo)).Count
        if ($after -gt $before) { $done = $true; break }
    }
    Write-Output ("SCENE_DONE=" + $s + " ok=" + $done + " sec=" + [int]$sw.Elapsed.TotalSeconds)
}
Write-Output 'ALL_SCENES_DISPATCHED'
