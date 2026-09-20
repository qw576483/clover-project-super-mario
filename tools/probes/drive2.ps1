# One-off driver (deleted after use, skill 1.8): enter Play ONCE, then run several probe scenes in the
# SAME Play session, waiting for each scene's own done marker. Same idea as the older run.ps1 but with
# two fixes that the older scripts got wrong:
#   1) every `unity` call carries --project-path: two editors are open on this machine
#      (clover-project-diablo2 + this project), and editor discovery is cwd-relative (skill gate 2b).
#   2) the "wait for the menu" step looks at the log TAIL (bytes appended since this run started), not at
#      the whole file. The old scripts matched a HISTORICAL "-> Menu" line, so the wait returned instantly
#      and `boot` (which needs to start outside Boot to get its full 1.8 s on screen) could be dispatched
#      while the boot flow was still in Boot.
# ASCII-only on purpose: PS 5.1 parses a BOM-less .ps1 as ANSI, so CJK literals silently break -match.
param(
    [Parameter(Mandatory = $true)][string]$Scene,
    [int]$SceneTimeout = 240,
    [int]$PlayWaitSec = 60,
    [int]$MenuWaitSec = 60,
    [switch]$ReusePlay
)
$ErrorActionPreference = 'Continue'
$proj  = 'C:\Work\Server\full-dev\clover-project-super-mario\client'
$probe = 'C:\Work\Server\full-dev\clover-project-super-mario\tools\probes\probe.cs'
$log   = Join-Path $proj ('Logs\' + (Get-Date -Format 'yyyy-MM-dd') + '.log')
$menuMark  = ([char]0x2192) + ' Menu'                                                    # "-> Menu"
$doneMark  = ([char[]]@(0x573A,0x666F,0x7ED3,0x675F,0xFF1A) -join '')                    # "scene end:"
$startMark = ([char[]]@(0x573A,0x666F,0x5F00,0x59CB,0xFF1A) -join '')                    # "scene start:"
Set-Location $proj

function Get-PlayMode {
    $j = unity command editor_status --project-path $proj --format json --no-banner 2>&1 | Out-String
    if ($j -match '"status"\s*:\s*"([a-zA-Z]+)"') { return $Matches[1] }
    if ($j -match 'playMode[^a-zA-Z]+([a-zA-Z]+)') { return $Matches[1] }
    return 'unknown'
}
function Get-EngineAlive {
    $j = unity command eval --code 'return CloverEngine.Game.IsRunning;' --project-path $proj --format json --no-banner 2>&1 | Out-String
    return ($j -match '"result"\s*:\s*true')
}
function LogText {
    if (-not (Test-Path $log)) { return '' }
    try {
        $fs = New-Object System.IO.FileStream($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
        $t = $sr.ReadToEnd(); $sr.Close(); $fs.Close()
        return $t
    } catch { Write-Output ('WARN log read failed: ' + $_.Exception.Message); return '' }
}
function Tail([int]$from) {
    $t = LogText
    if ($t.Length -le $from) { return '' }
    return $t.Substring($from)
}
function Stop-Play {
    if ((Get-PlayMode) -eq 'playing') {
        unity command editor_stop --project-path $proj --no-banner 2>&1 | Out-Null
        for ($i = 0; $i -lt 30; $i++) { Start-Sleep -Seconds 1; if ((Get-PlayMode) -ne 'playing') { return } }
    }
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
Write-Output ("PREFLIGHT OK: scenes=" + ($scenes -join ',') + " log=" + $log)

$ok = $false
if ($ReusePlay) {
    # Re-dispatch into the Play session that is ALREADY running: no new Play entry (skill 2 item 3:
    # reuse the driver / reuse the session before opening another one).
    $ok = Get-EngineAlive
    Write-Output ("REUSE_PLAY=$ok playMode=" + (Get-PlayMode))
} else {
    # ---- fresh Play session ----
    Stop-Play
    unity command editor_play --project-path $proj --no-banner 2>&1 | Out-Null
    for ($i = 0; $i -lt [int]($PlayWaitSec / 2); $i++) { Start-Sleep -Seconds 2; if ((Get-PlayMode) -eq 'playing') { break } }
    for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Seconds 2; if (Get-EngineAlive) { $ok = $true; break } }
    Write-Output ("ENGINE_ALIVE=$ok playMode=" + (Get-PlayMode))
}
if (-not $ok) { Write-Output 'ABORT: engine never came alive'; exit 1 }

# ---- run the scenes back to back, one Play session ----
$off = (LogText).Length
foreach ($s in $scenes) {
    # wait for a FRESH "-> Menu" in the tail (the game boots into the menu by itself)
    $w = 0
    while ($w -lt $MenuWaitSec) {
        if ((Tail $off) -match [regex]::Escape($menuMark)) { break }
        Start-Sleep -Seconds 1; $w++
    }
    Write-Output ("---- scene: $s (fresh menu marker after $w s) ----")
    $off = (LogText).Length
    $entry = "Probe." + $entryOf[$s.ToLower()]
    $resp = (unity command run_script --file $probe --entry $entry --project-path $proj --no-banner 2>&1 | Out-String)
    if ($resp -match 'Entry Point Not Found') { Write-Output ("DISPATCH_FAILED $s : entry not found"); continue }
    $started = $false
    for ($i = 0; $i -lt 8; $i++) {
        Start-Sleep -Seconds 2
        if ((Tail $off) -match [regex]::Escape($startMark + $s)) { $started = $true; break }
    }
    if (-not $started) {
        Write-Output ("DISPATCH_FAILED $s : no start marker within 16s")
        # 修：原来对"压平后的字符串"用 $resp.Length 做边界，长度不一致时会抛 Substring 越界，
        # 于是真正有用的 run_script 报错（脚本编译错误等）被吞掉、只看到一句 PS 异常。
        $flat = ($resp -replace "\s+", ' ')
        Write-Output ("  raw: [" + $flat.Length + "] " + $flat.Substring(0, [Math]::Min(400, $flat.Length)))
        continue
    }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $done = $false
    while ($sw.Elapsed.TotalSeconds -lt $SceneTimeout) {
        Start-Sleep -Seconds 2
        if ((Tail $off) -match [regex]::Escape($doneMark + $s)) { $done = $true; break }
    }
    Write-Output ("SCENE_DONE=" + $s + " ok=" + $done + " sec=" + [int]$sw.Elapsed.TotalSeconds)
    $line = (Tail $off) -split "`n"
    $errs = @($line | Where-Object { $_ -match '\[Error\]' })
    Write-Output ("SCENE_ERRORS=" + $s + " " + $errs.Count)
    foreach ($e in $errs) { Write-Output ("   " + $e.Trim()) }
}
Write-Output 'ALL_SCENES_DISPATCHED'
