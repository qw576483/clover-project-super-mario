$ErrorActionPreference = 'Stop'
# One-off harness (round 4): every check LABEL in the template skeleton must be ASCII, and every label
# the prose cites must exist in the code. Deleted after use.
$repo = 'clover-ai-skill'
# Host install copy: derived from the user profile, never a hard-coded user name.
$inst = Join-Path $env:USERPROFILE '.codebuddy\skills\ai-skill'
$proj = 'clover-project-super-mario'
$fail = 0
function V($ok, $name, $detail) {
  if ($ok) { Write-Output ("PASS  {0,-30} {1}" -f $name, $detail) }
  else { Write-Output ("FAIL  {0,-30} {1}" -f $name, $detail); $script:fail++ }
}
function ReadU8([string]$p) { [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) }

$tpl = ReadU8 (Join-Path $repo 'reference\verify-template.md')
$skill = ReadU8 (Join-Path $repo 'SKILL.md')

Write-Output '--- V1  two skill copies identical ----------------------------'
foreach ($f in 'SKILL.md', 'reference\verify-template.md') {
  $a = (Get-FileHash (Join-Path $repo $f) -Algorithm SHA256).Hash
  $b = (Get-FileHash (Join-Path $inst $f) -Algorithm SHA256).Hash
  V ($a -eq $b) ('V1-copy ' + $f) ('sha256 ' + $a.Substring(0, 12))
}

Write-Output '--- V2  markdown intact ------------------------------------'
$n = @([regex]::Matches($tpl, '(?m)^```')).Count
V (($n % 2) -eq 0) 'V2-fences' "$n fence markers"

$fences = [regex]::Matches($tpl, '(?s)```powershell(.*?)```')
$skel = $fences[$fences.Count - 1].Groups[1].Value
$sk = $null; $errs = $null
[void][System.Management.Automation.Language.Parser]::ParseInput($skel, [ref]$sk, [ref]$errs)
V (@($errs).Count -eq 0) 'V2-skeleton-syntax' ("$(@($errs).Count) parse error(s)")
$sk2 = $null; $errs2 = $null
[void][System.Management.Automation.Language.Parser]::ParseInput((ReadU8 (Join-Path $proj 'tools\verify.ps1')), [ref]$sk2, [ref]$errs2)
V (@($errs2).Count -eq 0) 'V2-gate-syntax' ("$(@($errs2).Count) parse error(s)")

Write-Output '--- V3  every label in the skeleton is ASCII ----------------'
$labels = @([regex]::Matches($skel, "(?:PASS|FAIL|HUMAN-ONLY)'\)*\s+'([^']+)'") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$nonAscii = @($labels | Where-Object { $_ -match '[^\x00-\x7F]' })
V ($labels.Count -ge 8 -and $nonAscii.Count -eq 0) 'V3-labels-ascii' ("$($labels.Count) label(s), non-ASCII=[$($nonAscii -join ',')]")
Write-Output ("        labels: " + ($labels -join ', '))

Write-Output '--- V4  labels cited in prose exist in the code -------------'
$cited = @([regex]::Matches($tpl, '`(?:FAIL|PASS|HUMAN-ONLY) ([A-Za-z0-9\-\.]+)`') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$missing = @($cited | Where-Object { $labels -notcontains $_ })
V ($missing.Count -eq 0) 'V4-cited-labels-resolve' ("cited=[" + ($cited -join ',') + "] missing=[" + ($missing -join ',') + "]")

Write-Output '--- V5  both sides agree with the shipped gate --------------'
$gateTxt = ReadU8 (Join-Path $proj 'tools\verify.ps1')
$gateLabels = @([regex]::Matches($gateTxt, "Say '(?:PASS|FAIL|HUMAN-ONLY)'\s+'(no-escaped-artifacts)'") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$gateAll = @([regex]::Matches($gateTxt, "\s'([a-z0-9\-]+)'\s") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
V ($gateLabels -contains 'no-escaped-artifacts' -and $labels -contains 'no-escaped-artifacts') 'V5-item13-shared-label' 'template and shipped gate use the same label'
V ($skill.Contains('no-escaped-artifacts')) 'V5-skill-cites-label' 'SKILL.md cites the same label'
Write-Output ("        gate labels (ASCII, first 12): " + (@($gateAll | Select-Object -First 12) -join ', '))

Write-Output '--- V6  shipped gate regression ----------------------------'
$o = @(powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $proj 'tools\verify.ps1') 2>&1)
V (@($o | Select-String 'no-escaped-artifacts' | Where-Object { $_.Line -match 'PASS' }).Count -eq 1) 'V6-gate-item13-passes' (@($o | Select-String 'no-escaped-artifacts').Line.Trim())
Write-Output ("        " + (@($o | Select-String 'summary:').Line -join ''))

Write-Output ''
Write-Output ("===== harness summary: FAIL=$fail =====")
exit $(if ($fail -gt 0) { 1 } else { 0 })
