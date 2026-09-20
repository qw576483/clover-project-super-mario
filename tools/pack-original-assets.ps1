# pack-original-assets.ps1 -- reproducible zip of the original-assets folder, for delivery over git-lfs.
#
# Rule source: skill `clover-engine` -> reference/asset-sources.md 8.2 (and rules-full.md 1.9 rule 9):
#   the original-assets folder stays OUT of git by default; only when the user explicitly asks to ship
#   the original material with the delivery do we pack it into <root>\<original-assets>.zip and commit it
#   through git-lfs. The zip stays at the repo root -- NEVER inside client/Assets/**.
#
# Reproducibility: entries are written in sorted order with a FIXED LastWriteTime, so packing the same
# folder twice yields the same file (and the same sha256) -- which is what makes the manifest checkable.
#
# Usage:
#   powershell -NoProfile -File tools/pack-original-assets.ps1
#   powershell -NoProfile -File tools/pack-original-assets.ps1 -Out D:\share\refs.zip
#
# ASCII-only on purpose (PowerShell 5.1 parses a BOM-less .ps1 as ANSI), so the Chinese folder name
# is built from code points instead of being written as a literal.

param([string]$Out = '')

$ErrorActionPreference = 'Stop'

# U+539F U+7248 U+8D44 U+6E90  ==  the original-assets folder name (four Chinese characters, per the skill:
# it must NOT be renamed or translated). Built from code points to keep this file pure ASCII.
$refName = [string][char]0x539F + [char]0x7248 + [char]0x8D44 + [char]0x6E90

$root = Split-Path $PSScriptRoot -Parent
$src  = Join-Path $root $refName
if (-not (Test-Path $src)) { Write-Output ('FAIL pack-original-assets: not found -> ' + $src); exit 1 }
if ([string]::IsNullOrWhiteSpace($Out)) { $Out = Join-Path $root ($refName + '.zip') }

# Never let the archive land inside the Unity project.
$assets = Join-Path $root 'client\Assets'
if ($Out.StartsWith($assets, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Output 'FAIL pack-original-assets: the zip must NOT be placed under client/Assets/**'
    exit 1
}

# Reference trees are source trees; skip machine-generated caches if any slipped in.
$skipDirs = @('.git', 'Library', 'Temp', 'obj', 'Logs', 'Build', 'Builds', 'UserSettings')
$all = @(Get-ChildItem $src -Recurse -File -Force | Where-Object {
    $rel = $_.FullName.Substring($src.Length + 1)
    $top = ($rel -split '\\')[0]
    -not ($skipDirs -contains $top)
})
$all = @($all | Sort-Object { $_.FullName.Substring($src.Length + 1) })

if ($all.Count -eq 0) { Write-Output 'FAIL pack-original-assets: nothing to pack'; exit 1 }

Write-Output ('pack-original-assets: {0} files, {1:N1} MB -> {2}' -f `
    $all.Count, (($all | Measure-Object Length -Sum).Sum / 1MB), $Out)

if (Test-Path $Out) { Remove-Item $Out -Force }

# PowerShell 5.1: ZipFile lives in ...FileSystem, but the ZipArchiveMode enum lives in System.IO.Compression.
# Loading only the first one fails with "Unable to find type [System.IO.Compression.ZipArchiveMode]"
# (measured 2026-09-20) -- load both.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($Out, [System.IO.Compression.ZipArchiveMode]::Create)
$fixed = [DateTimeOffset]::new(2020, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
try {
    foreach ($f in $all) {
        $rel = $f.FullName.Substring($src.Length + 1).Replace('\', '/')
        $e = $zip.CreateEntry($rel, [System.IO.Compression.CompressionLevel]::Optimal)
        $e.LastWriteTime = $fixed
        $os = $e.Open()
        $is = [IO.File]::OpenRead($f.FullName)
        try { $is.CopyTo($os) } finally { $is.Close(); $os.Close() }
    }
} finally {
    $zip.Dispose()
}

$info = Get-Item $Out
$sha  = (Get-FileHash $Out -Algorithm SHA256).Hash.ToLower()
Write-Output ('packed: {0:N1} MB' -f ($info.Length / 1MB))
Write-Output ('sha256: ' + $sha)
Write-Output 'next steps (skill reference/asset-sources.md 8.2):'
Write-Output '  git lfs install'
Write-Output "  '*.zip filter=lfs diff=lfs merge=lfs -text' | Add-Content .gitattributes"
Write-Output ('  git add .gitattributes "' + [IO.Path]::GetFileName($Out) + '"')
Write-Output '  # then record the sha256 above in the original-assets manifest (original assets / MANIFEST).'
