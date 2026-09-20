# readme-shots.ps1 -- copy the README screen captures out of the throwaway .ai-tmp batch and into the
# repo, so README.md references files that ship with the project.
#
# Why this indirection exists (skill clover-engine 1.8): the capture batch under .ai-tmp/screenshots is a
# ONE-OFF product and is deleted after acceptance, so README must never point at it. Probe scene
# `readme` (tools/probes/probe.cs, entry `Probe.Readme`) produces the four frames; this script is the
# single, re-runnable way to publish them into <root>\<ce-hua>\<shi-ji-tu>\.
#
# It FAILS (exit 1) when any source frame is missing: a half-published set would silently make README
# show fewer pictures than the section promises, and the failure would only be visible to a human eye.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/readme-shots.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/readme-shots.ps1 -Shots <dir>
#
# ASCII-only on purpose (PS 5.1 parses a BOM-less .ps1 as ANSI, so CJK literals silently break -match);
# the two Chinese directory names are built from code points.
param([string]$Shots = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if ([string]::IsNullOrWhiteSpace($Shots)) { $Shots = Join-Path $root '.ai-tmp\screenshots' }
$planDir = Join-Path $root    ([string][char]0x7B56 + [char]0x5212)                                  # ce hua
$destDir = Join-Path $planDir ([string][char]0x5B9E + [char]0x673A + [char]0x56FE)                    # shi ji tu

$map = @(
    @{ from = 'readme-title.png';             to = 'game-title.png';            what = 'title screen + (c)1985 NINTENDO copyright line + by clover-engine' },
    @{ from = 'readme-world1-1.png';          to = 'game-world1-1.png';         what = 'World 1-1 first screen' },
    @{ from = 'readme-world1-2.png';          to = 'game-world1-2.png';         what = 'World 1-2 underground section' },
    @{ from = 'readme-world1-2-surface.png';  to = 'game-world1-2-surface.png'; what = 'World 1-2 surface section (flagpole + castle)' }
)

if (-not (Test-Path $Shots)) { Write-Output ('FAIL readme-shots: capture dir not found -> ' + $Shots); exit 1 }
$missing = @()
foreach ($m in $map) { if (-not (Test-Path (Join-Path $Shots $m.from))) { $missing += $m.from } }
if ($missing.Count -gt 0) {
    Write-Output ('FAIL readme-shots: ' + $missing.Count + ' frame(s) missing; run probe scene "readme" first -> ' + ($missing -join ', '))
    exit 1
}

if (-not (Test-Path $destDir)) { [void](New-Item -ItemType Directory -Path $destDir) }
foreach ($m in $map) {
    $src = Join-Path $Shots $m.from
    $dst = Join-Path $destDir $m.to
    Copy-Item -LiteralPath $src -Destination $dst -Force
    $sha = (Get-FileHash $dst -Algorithm SHA256).Hash.ToLower()
    $len = (Get-Item $dst).Length
    Write-Output ('{0,-26} <- {1,-30} {2,8} B  sha256 {3}  [{4}]' -f $m.to, $m.from, $len, $sha.Substring(0, 12), $m.what)
}
Write-Output ('OK readme-shots: ' + $map.Count + ' frame(s) published to ' + $destDir)
