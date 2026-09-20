# Self-test for the new evidence-freshness check (SKILL.md 1.11: "a check that only ever reports red
# is worse than no check" -- prove it can report green, and prove it catches a known defect).
#
# Method (one area, three runs of the real gate; nothing is invented):
#   area ui-intro  deps = UI/LoadingPanel.cs + UIBuilder + ResPaths + Fonts;  shots = intro_card.png ...
#   run 1  as-is                                    -> expect: intro_card.png NOT in the stale list (green)
#   run 2  intro_card.png aged 2s past its own dep  -> expect: exactly that row flagged (red)
#   run 3  mtime restored                           -> expect: green again
# ASCII-only on purpose (PS 5.1 parses a BOM-less .ps1 as ANSI).
param([string]$Root = 'c:/Work/Server/full-dev/clover-project-super-mario')
$ErrorActionPreference = 'Stop'
$verify = Join-Path $Root 'tools/verify.ps1'
$shot = Join-Path $Root 'client\Assets\Screenshots\intro_card.png'
$dep = Join-Path $Root 'client\Assets\Scripts\UI\LoadingPanel.cs'
$want = 'intro_card\.png < LoadingPanel\.cs \(area=ui-intro scene=intro\)'

function Run-Gate([string]$tag) {
  $txt = & powershell -NoProfile -ExecutionPolicy Bypass -File $verify 2>&1 | Out-String
  [System.IO.File]::WriteAllText((Join-Path $Root ('.ai-tmp/test/selftest-' + $tag + '.out.txt')), $txt, [System.Text.Encoding]::UTF8)
  return $txt
}

$orig = (Get-Item $shot).LastWriteTime
$depT = (Get-Item $dep).LastWriteTime
Write-Output ("area ui-intro: intro_card.png = " + $orig.ToString('HH:mm:ss') + " ; newest dep LoadingPanel.cs = " + $depT.ToString('HH:mm:ss'))

$r1 = Run-Gate 'good'
$hit1 = ($r1 -match $want)
Write-Output ("[1] as-is                      stale row for intro_card : " + $hit1 + "   (expect False)")

(Get-Item $shot).LastWriteTime = $depT.AddSeconds(-2)
Write-Output ("    aged intro_card.png to  " + (Get-Item $shot).LastWriteTime.ToString('HH:mm:ss') + " (2s past its own dep)")
$r2 = Run-Gate 'bad'
$hit2 = ($r2 -match $want)
$sum2 = [regex]::Match($r2, 'summary: FAIL=\d+').Value
Write-Output ("[2] known-bad (aged 2s)        stale row for intro_card : " + $hit2 + "   (expect True)  " + $sum2)

(Get-Item $shot).LastWriteTime = $orig
$r3 = Run-Gate 'restored'
$hit3 = ($r3 -match $want)
Write-Output ("[3] restored                   stale row for intro_card : " + $hit3 + "   (expect False)")

$ok = ((-not $hit1) -and $hit2 -and (-not $hit3))
Write-Output ("----- self-test: " + $(if ($ok) { 'PASS' } else { 'FAIL' }) + " (green works + known defect is caught) -----")
exit $(if ($ok) { 0 } else { 1 })
