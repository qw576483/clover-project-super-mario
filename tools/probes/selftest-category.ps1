# Self-test for the class-aware gate (SKILL.md 1.11: "a check that cries wolf is worse than no check" --
# so every new check needs a KNOWN-GOOD case (must stay green) and a KNOWN-BAD case (must go red)).
# It runs the REAL tools/verify.ps1, never a copy, and every scenario restores what it touched.
# Outputs: .ai-tmp/test/selftest-cat-<tag>.out.txt (the raw verify.ps1 output of that scenario).
$ErrorActionPreference = 'Stop'
$root   = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$verify = Join-Path $root 'tools\verify.ps1'
$shot   = Join-Path $root 'client\Assets\Screenshots'
$tmp    = $PSScriptRoot
$results = @()

function Run-Gate([string]$tag) {
  $out = Join-Path $tmp ("selftest-cat-$tag.out.txt")
  & powershell -NoProfile -ExecutionPolicy Bypass -File $verify 2>&1 | Out-File -Encoding utf8 $out
  $txt = [System.IO.File]::ReadAllText($out, [System.Text.Encoding]::UTF8)
  $m = [regex]::Match($txt, 'summary: FAIL=(\d+)\s+HUMAN-ONLY=(\d+)')
  return [pscustomobject]@{ Tag = $tag; Fail = [int]$m.Groups[1].Value; Human = [int]$m.Groups[2].Value; Text = $txt }
}
function Check([string]$name, [string]$expect, [bool]$ok, [string]$detail) {
  $script:results += [pscustomobject]@{ Name = $name; Expect = $expect; Ok = $ok; Detail = $detail }
  Write-Output ("[{0}] {1}  expect={2}  {3}" -f $(if ($ok) { 'OK  ' } else { 'BAD ' }), $name, $expect, $detail)
}

Write-Output '----- class-aware gate self-test (real tools/verify.ps1) -----'

# A) known-good: the tree as it stands must be green.
$r = Run-Gate 'A-green'
Check 'A as-is' 'FAIL=0' ($r.Fail -eq 0) ("FAIL=" + $r.Fail + " HUMAN-ONLY=" + $r.Human)

# B) known-bad: a VISUAL row's cited frame aged past its own area's newest source => evidence-freshness FAIL.
$pB = Join-Path $shot 'intro_card.png'
$oB = (Get-Item $pB).LastWriteTime
try {
  (Get-Item $pB).LastWriteTime = [datetime]'2026-09-19T11:48:41'      # just before LoadingPanel.cs (11:48:43)
  $r = Run-Gate 'B-visual-row-aged'
  Check 'B visual row aged' 'FAIL>=1 naming intro_card.png / area=ui-intro' `
    (($r.Fail -ge 1) -and ($r.Text -match 'intro_card\.png < ') -and ($r.Text -match 'area=ui-intro')) `
    ("FAIL=" + $r.Fail + "; line: " + ([regex]::Match($r.Text, 'intro_card\.png[^\r\n]*')).Value)
} finally { (Get-Item $pB).LastWriteTime = $oB }

# C) known-good (THE new semantics): a NUMERIC row's screenshot is NOT its evidence -- aging it must NOT
#    turn the gate red (its judge is a log line / assertion, SKILL.md 4).
$pC = Join-Path $shot '13-starbrick-star.png'
$oC = (Get-Item $pC).LastWriteTime
try {
  (Get-Item $pC).LastWriteTime = [datetime]'2020-01-01T00:00:00'
  $r = Run-Gate 'C-numeric-row-aged'
  $named = ($r.Text -match '13-starbrick-star')
  Check 'C numeric row aged' 'FAIL=0 (numeric png not judged)' (($r.Fail -eq 0) -and (-not $named)) `
    ("FAIL=" + $r.Fail + "; named in the report: " + $named)
} finally { (Get-Item $pC).LastWriteTime = $oC }

# D) known-bad: a frame newer than the sheet that draws it => freeze-before-capture FAIL.
$pD = Join-Path $shot 'probe-plat-spawn.png'
$oD = (Get-Item $pD).LastWriteTime
try {
  (Get-Item $pD).LastWriteTime = (Get-Date).AddHours(2)
  $r = Run-Gate 'D-derived-evidence-stale'
  Check 'D sheet older than its frame' 'FAIL>=1 naming contact-platform.png' `
    (($r.Fail -ge 1) -and ($r.Text -match 'contact-platform\.png is OLDER than a frame')) `
    ("FAIL=" + $r.Fail)
} finally { (Get-Item $pD).LastWriteTime = $oD }

# E) known-bad: >5 Play sessions in the current round => play-budget FAIL.
$ledger = Join-Path $tmp 'play-log.tsv'
$ledgerTxt = [System.IO.File]::ReadAllText($ledger, [System.Text.Encoding]::UTF8)
try {
  $add = ''
  for ($i = 1; $i -le 6; $i++) { $add += ("2026-09-19T16:59`tclover-impl`tselftest`tself-test row " + $i + "`r`n") }
  [System.IO.File]::AppendAllText($ledger, $add, [System.Text.Encoding]::UTF8)
  $r = Run-Gate 'E-play-budget-over'
  Check 'E 6 rows in this round' 'FAIL>=1 play-budget' `
    (($r.Fail -ge 1) -and ($r.Text -match 'play-budget  this piece = 6')) ("FAIL=" + $r.Fail)
} finally { [System.IO.File]::WriteAllText($ledger, $ledgerTxt, [System.Text.Encoding]::UTF8) }

# F) known-bad: a visual row loses its contact-sheet cell => evidence-economy FAIL.
$idxTsv = Join-Path $shot 'contact-1-2-look-index.tsv'
$bak = $idxTsv + '.selftest-bak'
Move-Item $idxTsv $bak
try {
  $r = Run-Gate 'F-no-cell'
  Check 'F visual row without a cell' 'FAIL>=1 evidence-economy' `
    (($r.Fail -ge 1) -and ($r.Text -match 'evidence-economy  [0-9]+/[0-9]+ visual row\(s\) have no cell')) ("FAIL=" + $r.Fail)
} finally { Move-Item $bak $idxTsv }

# G) known-bad: a ledger history row without the relaxation marker => play-budget FAIL (accounting must be auditable).
$ledgerTxt2 = [System.IO.File]::ReadAllText($ledger, [System.Text.Encoding]::UTF8)
try {
  $rel = ([char[]]@(0x653E, 0x5BBD) -join '')          # the relaxation marker itself
  [System.IO.File]::WriteAllText($ledger, ($ledgerTxt2 -replace $rel, 'x'), [System.Text.Encoding]::UTF8)
  $r = Run-Gate 'G-history-not-marked'
  Check 'G history row without marker' 'FAIL>=1 play-budget' `
    (($r.Fail -ge 1) -and ($r.Text -match 'history row without the relaxation marker')) ("FAIL=" + $r.Fail)
} finally { [System.IO.File]::WriteAllText($ledger, $ledgerTxt2, [System.Text.Encoding]::UTF8) }

# restore check: the tree must be green again
$r = Run-Gate 'H-restored'
Check 'H after restore' 'FAIL=0' ($r.Fail -eq 0) ("FAIL=" + $r.Fail)

Write-Output ''
$bad = @($results | Where-Object { -not $_.Ok })
if ($bad.Count -eq 0) {
  Write-Output ("----- self-test: PASS (" + $results.Count + " scenarios: green stays green, every known defect is caught) -----")
  exit 0
} else {
  Write-Output ("----- self-test: FAIL (" + $bad.Count + "/" + $results.Count + " scenarios behaved wrong) -----")
  $bad | ForEach-Object { Write-Output ("        " + $_.Name + "  expect=" + $_.Expect + "  got: " + $_.Detail) }
  exit 1
}
