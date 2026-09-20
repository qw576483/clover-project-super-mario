# One-off driver (deleted after use, skill 1.8): make sure Play is up with the engine alive, then run one
# or more probe scenes in the SAME Play session and wait for each scene's done marker.
# ASCII-only on purpose: PS 5.1 parses a BOM-less .ps1 as ANSI, so CJK literals silently break -match.
param(
    [Parameter(Mandatory = $true)][string[]]$Scene,
    [int]$TimeoutSec = 200
)
$ErrorActionPreference = 'Continue'
$client = 'C:\Work\Server\full-dev\clover-project-super-mario\client'
$probe  = 'C:\Work\Server\full-dev\clover-project-super-mario\tools\probes\probe.cs'
# Take the log for TODAY (the game writes <yyyy-MM-dd>.log). A hard-coded date keeps reading
# yesterday's file the next day => the fresh markers are never seen => every scene burns its
# whole timeout doing nothing. So: newest yyyy-MM-dd.log in Logs.
# ASCII-only on purpose (see the note at the top of this file).
$log = (Get-ChildItem (Join-Path $client 'Logs') -Filter '????-??-??.log' |
        Sort-Object LastWriteTime | Select-Object -Last 1).FullName
if (-not $log) { Write-Output 'PREFLIGHT FAIL: no yyyy-MM-dd.log in Logs'; exit 3 }
$menuMark = ([char]0x2192) + ' Menu'                                                    # "-> Menu"
$doneMark = ([char[]]@(0x573A,0x666F,0x7ED3,0x675F,0xFF1A) -join '')                    # "scene end:"
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
function Stop-Play {
    unity command editor_stop --no-banner 2>&1 | Out-Null
    for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Seconds 1; if ((Get-PlayMode) -ne 'playing') { return } }
}
function LogText {
    # the game holds the log open: read with FileShare.ReadWrite, otherwise every read throws IOException
    if (-not (Test-Path $log)) { return '' }
    try {
        $fs = New-Object System.IO.FileStream($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
        $t = $sr.ReadToEnd()
        $sr.Close(); $fs.Close()
        return $t
    } catch { Write-Output ("WARN log read failed: " + $_.Exception.Message); return '' }
}

# ---- PREFLIGHT (seconds): everything checkable BEFORE the minute-long run ----
$rxo = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
$probeText = [System.IO.File]::ReadAllText($probe, [System.Text.Encoding]::UTF8)
$entryOf = @{}
foreach ($m in [regex]::Matches($probeText, 'public static void ([A-Za-z0-9_]+)\(\)\s*=>\s*Start\("([a-z0-9\-]+)"')) {
    $entryOf[$m.Groups[2].Value] = $m.Groups[1].Value
}
$declared = @($entryOf.Keys | Sort-Object)
Write-Output ("declared scenes in probe.cs: " + ($declared -join ','))
$scenes = @($Scene | ForEach-Object { $_ -split ',' } | Where-Object { $_ -ne '' })
$unknown = @($scenes | Where-Object { $declared -notcontains $_.ToLower() })
if ($unknown.Count -gt 0) { Write-Output ("PREFLIGHT FAIL: not declared in probe.cs -> " + ($unknown -join ',')); exit 2 }
$preLog = LogText
if ($preLog -eq '') { Write-Output 'PREFLIGHT FAIL: cannot read the game log'; exit 3 }
Write-Output ("PREFLIGHT OK: log readable (" + $preLog.Length + " chars); scenes=" + ($scenes -join ','))

# ---- make sure Play is running AND the engine is alive ----
$ok = $false
for ($attempt = 1; $attempt -le 4 -and -not $ok; $attempt++) {
    if ((Get-PlayMode) -ne 'playing') {
        Write-Output ("attempt $attempt : entering play mode")
        unity command editor_play --no-banner 2>&1 | Out-Null
        for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Seconds 2; if ((Get-PlayMode) -eq 'playing') { break } }
    }
    for ($i = 0; $i -lt 10; $i++) {
        Start-Sleep -Seconds 2
        if (Get-EngineAlive) { $ok = $true; break }
    }
    if ($ok) { break }
    Write-Output ("attempt $attempt : engine not alive -> recompile + restart play")
    unity command recompile --no-banner 2>&1 | Out-Null
    Start-Sleep -Seconds 8
    Stop-Play
    Start-Sleep -Seconds 3
}
Write-Output ("ENGINE_ALIVE=$ok  playMode=" + (Get-PlayMode))
if (-not $ok) { Write-Output 'ABORT: engine never came alive'; exit 1 }

# ---- wait for the boot flow to reach the main menu ----
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    if ((LogText) -match [regex]::Escape($menuMark)) { break }
}
Write-Output 'menu reached (or waited 30s)'

# ---- run the scenes back to back, one Play session ----
$startMark = ([char[]]@(0x573A,0x666F,0x5F00,0x59CB,0xFF1A) -join '')                    # "scene start:"
foreach ($s in $scenes) {
    $before = @([regex]::Matches((LogText), [regex]::Escape($doneMark + $s), $rxo)).Count
    Write-Output ("---- scene: $s (done markers before=$before) ----")
    $entry = "Probe." + $entryOf[$s.ToLower()]
    $resp = (unity command run_script --file $probe --entry $entry --no-banner 2>&1 | Out-String)
    if ($resp -match 'Entry Point Not Found') { Write-Output ("DISPATCH_FAILED $s : entry not found -> not waiting"); continue }

    # fail fast: the probe logs a start marker on entry. If it never shows up the scene never began,
    # so there is nothing to wait for -- abort in seconds instead of burning the whole timeout.
    $started = $false
    for ($i = 0; $i -lt 6; $i++) {
        Start-Sleep -Seconds 2
        if ((LogText) -match [regex]::Escape($startMark + $s)) { $started = $true; break }
    }
    if (-not $started) {
        Write-Output ("DISPATCH_FAILED $s : no start marker within 12s -> not waiting")
        Write-Output ("  raw: " + ($resp -replace "\s+", ' ').Substring(0, [Math]::Min(300, $resp.Length)))
        continue
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $done = $false
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        Start-Sleep -Seconds 2
        $after = @([regex]::Matches((LogText), [regex]::Escape($doneMark + $s), $rxo)).Count
        if ($after -gt $before) { $done = $true; break }
    }
    Write-Output ("SCENE_DONE=" + $s + " ok=" + $done + " sec=" + [int]$sw.Elapsed.TotalSeconds)
}
Write-Output 'ALL_SCENES_DISPATCHED'
