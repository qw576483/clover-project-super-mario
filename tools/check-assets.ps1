# check-assets.ps1 -- gate: every file under client/Assets/Resources must be REFERENCED somewhere.
#
# Rule source: skill `clover-engine` -> reference/rules-full.md 1.9 rule 6/7 (and reference/asset-sources.md 8.1):
#   "pulling assets into the project is pull-on-demand, never bulk" -- and a rule that is not a gate rots.
#
# Why this exists (measured 2026-09-20, clover-project-super-mario):
#   three original sprite sheets were sliced wholesale into Assets/Resources/Sprites/** ->
#   953 resource files, 795 of them referenced by NOTHING (Items 95%, Enemies 88%, Tiles 80%, Scenery 72%).
#   Only 1.26 MB of bytes, but the file count and the "which slice is which tile" guessing were the real cost.
#
# Usage:
#   powershell -NoProfile -File tools/check-assets.ps1          # gate: unreferenced => FAIL (exit 1)
#   powershell -NoProfile -File tools/check-assets.ps1 -Warn    # report only (for cleaning up an existing project)
#
# Reference scope: client/Assets/Scripts/**/*.cs + client/Assets/Resources/Levels/*.txt
#   (a file name counts as used if it appears anywhere in those texts; sprite names are string literals
#    in Core/ResPaths.cs / SpriteNames and in the level data "T <x> <y> <layer> <sprite>" lines).
#
# ASCII-only on purpose (PowerShell 5.1 parses a BOM-less .ps1 as ANSI).

param([switch]$Warn)

$ErrorActionPreference = 'Stop'

$root   = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'client\Assets'
$res    = Join-Path $assets 'Resources'
$scripts = Join-Path $assets 'Scripts'

if (-not (Test-Path $res))     { Write-Output 'FAIL check-assets: client/Assets/Resources not found'; exit 1 }
if (-not (Test-Path $scripts)) { Write-Output 'FAIL check-assets: client/Assets/Scripts not found'; exit 1 }

# ---- reference corpus -----------------------------------------------------------------
$parts = New-Object System.Collections.Generic.List[string]
foreach ($f in (Get-ChildItem $scripts -Recurse -Include *.cs)) { $parts.Add((Get-Content $f.FullName -Raw -Encoding UTF8)) }
$levels = Join-Path $res 'Levels'
if (Test-Path $levels) {
    foreach ($f in (Get-ChildItem $levels -Filter *.txt)) { $parts.Add((Get-Content $f.FullName -Raw -Encoding UTF8)) }
}
$blob = ($parts -join "`n")

# ---- scan -----------------------------------------------------------------------------
$files  = @(Get-ChildItem $res -Recurse -File | Where-Object { $_.Extension -ne '.meta' })
$unused = @()
foreach ($f in $files) {
    $n = [IO.Path]::GetFileNameWithoutExtension($f.Name)
    if ($blob -notmatch [regex]::Escape($n)) { $unused += $f }
}

$bytes = 0
foreach ($u in $unused) { $bytes += $u.Length }
Write-Output ('check-assets: {0} resource files, {1} UNREFERENCED ({2:N0} KB)' -f $files.Count, $unused.Count, ($bytes / 1KB))

if ($unused.Count -gt 0) {
    Write-Output '  by folder:'
    $unused | Group-Object DirectoryName | Sort-Object Name | ForEach-Object {
        Write-Output ('    {0}  x{1}' -f $_.Name.Replace($res, ''), $_.Count)
    }
    if ($Warn) { Write-Output 'WARN check-assets: unreferenced assets exist (warn mode, not a gate)'; exit 0 }
    Write-Output 'FAIL check-assets: move them out of Assets (e.g. under .ai-tmp/, which is gitignored)'
    Write-Output '                   rule: reference/rules-full.md 1.9-6/7 -- pull on demand, never bulk.'
    exit 1
}

Write-Output 'PASS check-assets'
exit 0
