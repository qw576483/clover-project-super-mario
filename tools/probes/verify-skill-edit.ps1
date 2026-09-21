$ErrorActionPreference = 'Stop'
# One-off harness: checks whether the skill edit is correct. Lives in .ai-tmp/test and is deleted after use.
# Round 3. Round 1's 4 FAIL were harness bugs; round 2's single FAIL (V10) was real: the template and the
# shipped project gate had drifted (check label + a hidden dependency on another check's variable). Both fixed.
$repo = 'clover-ai-skill'
# Host install copy: derived from the user profile, never a hard-coded user name.
$inst = Join-Path $env:USERPROFILE '.codebuddy\skills\ai-skill'
$proj = 'clover-project-super-mario'
$tmp  = Join-Path $proj '.ai-tmp\test'
# This script lives in <repo>\tools\probes\, so the repo root is two levels up.
$gitRoot = Join-Path $PSScriptRoot '..\..'
$fail = 0
function V($ok, $name, $detail) {
  if ($ok) { Write-Output ("PASS  {0,-30} {1}" -f $name, $detail) }
  else { Write-Output ("FAIL  {0,-30} {1}" -f $name, $detail); $script:fail++ }
}
function Info($name, $detail) { Write-Output ("INFO  {0,-30} {1}" -f $name, $detail) }
function ReadU8([string]$p) { [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) }

$skill = ReadU8 (Join-Path $repo 'SKILL.md')
$tpl   = ReadU8 (Join-Path $repo 'reference\verify-template.md')
$tplLF = $tpl -replace "`r", ''

# CJK fragments built from code points so this file stays ASCII-only (PS 5.1 parses BOM-less .ps1 as ANSI)
$cJob  = ([char[]]@(0x5DE5,0x7A0B,0x5916,0x4EA7,0x7269) -join '')           # gong cheng wai chan wu
$cNote = ([char[]]@(0x7B2C,0x0020,0x0031,0x0033,0x0020,0x6761) -join '')     # di 13 tiao
$cItem = ([char[]]@(0x7B2C,0x0020,0x0031,0x0033,0x0020,0x9879) -join '')     # di 13 xiang

Write-Output '--- V1  two skill copies identical ----------------------------'
foreach ($f in 'SKILL.md', 'reference\verify-template.md') {
  $a = (Get-FileHash (Join-Path $repo $f) -Algorithm SHA256).Hash
  $b = (Get-FileHash (Join-Path $inst $f) -Algorithm SHA256).Hash
  V ($a -eq $b) ('V1-copy ' + $f) ('sha256 ' + $a.Substring(0, 12))
}
$ca = @(Get-ChildItem $repo -Recurse -File).Count
$cb = @(Get-ChildItem $inst -Recurse -File).Count
V ($ca -eq $cb) 'V1-copy file-count' ("repo=$ca installed=$cb")

Write-Output '--- V2  committed content is what was written ---------------'
$s1 = (git -C $gitRoot --no-pager show --stat --format='%h|%s' 2f981f67) -join ' ; '
$s2 = (git -C $gitRoot --no-pager show --stat --format='%h|%s' 2416f132) -join ' ; '
Info 'V2-commit-skill' $s1
Info 'V2-commit-gate'  $s2
V ($s1 -match '2 files changed' -and $s1 -match 'SKILL\.md' -and $s1 -match 'verify-template\.md') 'V2-skill-commit-scope' 'exactly the 2 skill files'
V ($s2 -match 'verify\.ps1' -and $s2 -match 'gitignore') 'V2-gate-commit-scope' '.gitignore + verify.ps1'

Write-Output '--- V3  markdown still well-formed (CR-safe) ----------------'
foreach ($pair in @(@('SKILL.md', $skill), @('verify-template.md', $tpl))) {
  $n = @([regex]::Matches($pair[1], '(?m)^```')).Count
  V (($n % 2) -eq 0) ('V3-fences ' + $pair[0]) ("$n fence markers (even => every block closed)")
}
$hdr   = ([regex]::Match($tplLF, '(?m)^\| # \| .*\|$')).Value
$row13 = ([regex]::Match($tplLF, '(?m)^\|\s*13\s*\|.*\|$')).Value
$cols  = ($hdr -split '\|').Count
V ($hdr -ne '' -and $row13 -ne '') 'V3-row13-exists' 'check-13 row present in the mandatory-check table'
V ((($row13 -split '\|').Count) -eq $cols) 'V3-row13-columns' ("row cells=" + ($row13 -split '\|').Count + " header cells=$cols")

Write-Output '--- V4  anchors resolve (skill -> template -> skeleton) ------'
V ($skill.Contains($cJob) -and $skill.Contains('verify-template.md')) 'V4-skill-points-to-template' 'SKILL.md names the gate item + the template file'
V ($skill.Contains($cItem)) 'V4-anchor-number-matches' 'SKILL.md cites the item number the table actually uses (13)'
V ($tpl.Contains($cNote)) 'V4-template-has-note13' 'design-constraint note present'
$fences = [regex]::Matches($tpl, '(?s)```powershell(.*?)```')
$skel = $fences[$fences.Count - 1].Groups[1].Value      # LAST powershell fence = the skeleton
V ($skel.Contains('# 13)') -and $skel.Contains($cJob)) 'V4-skeleton-has-code13' 'skeleton carries the check-13 code'

Write-Output '--- V5  skill stays project-agnostic (anonymity) ------------'
foreach ($pair in @(@('SKILL.md', $skill), @('verify-template.md', $tpl))) {
  $hits = @([regex]::Matches($pair[1], 'super-mario|diablo2|cs16|CodeBuddy', 'IgnoreCase')).Count
  V ($hits -eq 0) ('V5-anon ' + $pair[0]) ("$hits project/host-specific string(s)")
}

Write-Output '--- V6  code parses as PowerShell --------------------------'
$sk = $null; $errs = $null
[void][System.Management.Automation.Language.Parser]::ParseInput($skel, [ref]$sk, [ref]$errs)
V (@($errs).Count -eq 0) 'V6-skeleton-syntax' ("$(@($errs).Count) parse error(s) in the template skeleton")
$sk2 = $null; $errs2 = $null
[void][System.Management.Automation.Language.Parser]::ParseInput((ReadU8 (Join-Path $proj 'tools\verify.ps1')), [ref]$sk2, [ref]$errs2)
V (@($errs2).Count -eq 0) 'V6-project-gate-syntax' ("$(@($errs2).Count) parse error(s) in the project gate")

Write-Output '--- V7  project gate regression ----------------------------'
$o = @(powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $proj 'tools\verify.ps1') 2>&1)
$line = @($o | Select-String 'no-escaped-artifacts')
V ($line.Count -gt 0 -and $line.Line -match 'PASS') 'V7-gate-item-present' ($line.Line.Trim())
Info 'V7-gate-summary' (@($o | Select-String 'summary:').Line -join '')

Write-Output '--- V8  item-13 code: both branches, run VERBATIM -----------'
$lines = $tplLF -split "`n"
$i0 = -1; for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^# 13\)') { $i0 = $i; break } }
$i1 = -1; for ($i = $i0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^Write-Output ''") { $i1 = $i - 1; break } }
$block = (($lines[$i0..$i1]) -join "`n").Trim()
V ($i0 -ge 0 -and $i1 -gt $i0) 'V8-block-extracted' ("template lines " + ($i0 + 1) + ".." + ($i1 + 1) + ", verbatim")

$fxRoot = Join-Path $tmp 'gate-fixture\AppData'
$fx     = Join-Path $fxRoot 'CloverHost\User\globalStorage\some-ext\brain'
New-Item -ItemType Directory -Path $fx -Force | Out-Null
$fxFile = Join-Path $fx 'super-mario-escaped-probe.md'
$root = $proj
function Say([string]$status, [string]$name, [string]$detail) { Write-Output ("{0,-11} {1}  {2}" -f $status, $name, $detail) }
$saveApp = $env:APPDATA
$env:APPDATA = $fxRoot

Set-Content -LiteralPath $fxFile -Value 'probe' -Encoding ASCII
$oFail = @(& ([scriptblock]::Create($block)))
V (@($oFail | Select-String 'FAIL').Count -gt 0 -and @($oFail | Select-String 'super-mario-escaped-probe').Count -gt 0) 'V8a-FAIL-branch' (@($oFail | Select-String 'no-escaped-artifacts').Line.Trim())

Remove-Item -LiteralPath $fxFile -Force
$oPass = @(& ([scriptblock]::Create($block)))
V (@($oPass | Select-String 'PASS').Count -gt 0 -and @($oPass | Select-String 'FAIL').Count -eq 0) 'V8b-PASS-branch' (@($oPass | Select-String 'no-escaped-artifacts').Line.Trim())
V (($oPass -join ' ') -match '\+ 1 ') 'V8c-zone-discovery' 'PASS wording reports exactly 1 discovered host dir (= the fixture)'

$env:APPDATA = $saveApp

Write-Output '--- V9  real host store, read-only control ------------------'
$realZone = Join-Path $saveApp 'CodeBuddy CN\User\globalStorage'
$hit = @(Get-ChildItem $realZone -Recurse -File -Filter '*super-mario*' -ErrorAction SilentlyContinue |
         Where-Object { $_.CreationTime -gt (Get-Date).AddHours(-24) })
if ($hit.Count -gt 0) { V $true 'V9-real-host-control' ("$($hit.Count) identity-named file(s) in the real host store (read-only)") }
else { Info 'V9-real-host-control' 'no trace left in the real host store' }
$hit | ForEach-Object { Write-Output ("        " + $_.FullName) }

Write-Output '--- V10 template skeleton == shipped gate (logic only) ------'
# Compare CODE, not prose: strip comments, then blank out string literals so wording may differ.
function Norm([string]$text) {
  $out = @()
  foreach ($ln in ($text -split "`r?`n")) {
    $s = $ln.Trim()
    if ($s -eq '' -or $s.StartsWith('#')) { continue }
    $sb = New-Object System.Text.StringBuilder
    $q = [char]0
    foreach ($ch in $s.ToCharArray()) {
      if ($q -eq [char]0 -and ($ch -eq "'" -or $ch -eq '"')) { $q = $ch }
      elseif ($q -ne [char]0 -and $ch -eq $q) { $q = [char]0 }
      elseif ($q -eq [char]0 -and $ch -eq '#') { break }
      [void]$sb.Append($ch)
    }
    $code = $sb.ToString().Trim()
    $code = [regex]::Replace($code, "'[^']*'", "'str'")
    $code = [regex]::Replace($code, '"[^"]*"', '"str"')
    if ($code -ne '') { $out += $code }
  }
  return ($out -join "`n")
}
$projTxt = ReadU8 (Join-Path $proj 'tools\verify.ps1')
$pl = $projTxt -split "`r?`n"
$p0 = -1; for ($i = 0; $i -lt $pl.Count; $i++) { if ($pl[$i] -match '^# 13\)') { $p0 = $i; break } }
$p1 = -1; for ($i = $p0; $i -lt $pl.Count; $i++) { if ($pl[$i] -match "^Write-Output ''") { $p1 = $i - 1; break } }
$la = Norm $block; $lb = Norm (($pl[$p0..$p1]) -join "`n")
V ($la -eq $lb) 'V10-template-equals-shipped' ("code lines: template=" + (($la -split "`n").Count) + " project=" + (($lb -split "`n").Count))
if ($la -ne $lb) {
  Compare-Object ($la -split "`n") ($lb -split "`n") | ForEach-Object { Write-Output ("        " + $_.SideIndicator + " " + $_.InputObject) }
}

Write-Output '--- V11 the new label is the same in code and in prose -------'
# Scope: only the label THIS edit introduces. The other bullets in the effects section are records of
# an earlier run, not code labels, so they are reported for context and not judged here.
$codeLabels = @([regex]::Matches($skel, "Say '(?:PASS|FAIL|HUMAN-ONLY)'\s+'([^']+)'") | ForEach-Object { $_.Groups[1].Value })
$cited = @([regex]::Matches($tpl, '`FAIL ([A-Za-z0-9\-\.]+)`') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$newLabel = 'no-escaped-artifacts'
$inCode = $codeLabels -contains $newLabel
$inDoc = $tpl.Contains('`FAIL ' + $newLabel + '`')
$inSkill = $skill.Contains($newLabel)
V ($inCode -and $inDoc -and $inSkill) 'V11-new-label-consistent' ("skeleton=$inCode doc=$inDoc SKILL.md=$inSkill")
$others = @($cited | Where-Object { $_ -ne $newLabel -and $codeLabels -notcontains $_ })
Info 'V11-context' ("other labels cited in the effects section (earlier-run records, NOT this edit): " + ($others -join ','))

Write-Output ''
Write-Output ("===== harness summary: FAIL=$fail =====")
exit $(if ($fail -gt 0) { 1 } else { 0 })
