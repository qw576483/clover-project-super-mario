param(
  # -Reshoot: print ONLY the scene list that has to be re-shot (computed from the same area map
  # the freshness check uses), then exit. The re-shoot driver accepts scenes from here and nowhere
  # else -- SKILL.md 1.13 item 6 / reference/verify-template.md 6b: the gate and the re-shoot must
  # share ONE data source, otherwise "the gate judges by cause while the re-shoot runs everything".
  [switch]$Reshoot
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fail = 0; $human = 0

function Say([string]$status, [string]$name, [string]$detail) {
  Write-Output ("{0,-11} {1}  {2}" -f $status, $name, $detail)
}

# ---------------------------------------------------------------------------
# Non-ASCII fragments are built from code points so this file stays ASCII-only
# (Windows PowerShell 5.1 parses a BOM-less .ps1 as ANSI => CJK in source breaks it).
# ---------------------------------------------------------------------------
$planDir  = Join-Path $root    (([char[]]@(0x7B56,0x5212) -join ''))                              # ce hua
$caseDir  = Join-Path $planDir (([char[]]@(0x7B56,0x5212,0x6848) -join ''))                        # ce hua an
$baseDir  = Join-Path $planDir (([char[]]@(0x57FA,0x7EBF,0x56FE) -join ''))                        # ji xian tu
$spec     = Join-Path $planDir ((([char[]]@(0x9A8C,0x6536,0x8868)) -join '') + '.md')              # yan shou biao
$refTable = Join-Path $planDir ((([char[]]@(0x5BF9,0x7167,0x8868)) -join '') + '.md')              # dui zhao biao
$assetDoc = Join-Path $planDir ((([char[]]@(0x7D20,0x6750,0x8C03,0x7814)) -join '') + '.md')       # su cai diao yan
$specDoc  = Join-Path $caseDir ((([char[]]@(0x53C2,0x8003,0x89C4,0x683C)) -join '') + '.md')       # can kao gui ge
$allowed  = ([char[]]@(0x5141,0x8BB8,0x7684,0x5DEE,0x5F02) -join '')                               # yun xu de cha yi
$srcDir   = Join-Path $root 'client\Assets\Scripts'
$shotDir  = Join-Path $root '.ai-tmp\screenshots'      # 取证截图是一次性产物（skill §1.8）：落 .ai-tmp，⛔ 不进 client/Assets/**

$cPass = ([char[]]@(0x4E00,0x81F4) -join '')            # yi zhi
$cUny  = ([char[]]@(0x672A,0x9A8C) -join '')            # wei yan
$cUdo  = ([char[]]@(0x672A,0x505A) -join '')            # wei zuo
$cNa   = ([char[]]@(0x4E0D,0x9002,0x7528) -join '')     # bu shi yong  (registered "not applicable")
$cWhy  = ([char[]]@(0x4E3A,0x4EC0,0x4E48) -join '')     # wei shen me
$cProv = ([char[]]@(0x51FA,0x5904) -join '')            # chu chu
$cWhen = ([char[]]@(0x4F55,0x65F6) -join '')            # he shi
$cHand = ([char[]]@(0x4EA4,0x63A5) -join '')            # jiao jie
$cProg = ([char[]]@(0x8FDB,0x5EA6) -join '')            # jin du
$cNumeric = ([char[]]@(0x6570,0x503C,0x7C7B) -join '')  # "shu zhi lei"   (numeric evidence class)
$cVisual  = ([char[]]@(0x8868,0x73B0,0x7C7B) -join '')  # "biao xian lei" (visual evidence class)

function Read-Utf8([string]$p) { [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) }

# 1) stray temp .cs files (global rule 1.8: .ai-tmp/test only, deleted after use)
$n = @(Get-ChildItem $root -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
       Where-Object { $_.FullName -match '\\(_dev|_assets_src|_assets_tmp|_probe|_smb_work)\\' }).Count
if ($n -eq 0) { Say 'PASS' 'stray-temp-files' '0 hits' }
else { $fail++; Say 'FAIL' 'stray-temp-files' "$n files under a stray temp dir" }

# 2) hard-rule hits in business source; every hit must be registered in the allowed-diff table
$specTxt = if (Test-Path $spec) { Read-Utf8 $spec } else { '' }
$allowTxt = ''
if ($specTxt.Contains($allowed)) { $allowTxt = $specTxt.Substring($specTxt.IndexOf($allowed)) }

$pat = 'Debug\.Log', 'Resources\.Load', 'PlayerPrefs', 'GameObject\.Find', 'FindObjectOfType', 'Instantiate\('
$hits = @(Get-ChildItem $srcDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
          Select-String -Pattern $pat -Encoding UTF8 |
          Where-Object { $_.Line -notmatch '^\s*(//|///|\*)' })
if ($hits.Count -eq 0) { Say 'PASS' 'hard-rules' '0 hits in client/Assets/Scripts' }
else {
  $unreg = @($hits | Where-Object { -not $allowTxt.Contains((Split-Path $_.Path -Leaf)) })
  if ($unreg.Count -eq 0) { Say 'PASS' 'hard-rules' "$($hits.Count) hits, all registered in allowed-diff" }
  else {
    $fail++; Say 'FAIL' 'hard-rules' "$($unreg.Count)/$($hits.Count) hits not registered in allowed-diff"
    $unreg | ForEach-Object { Write-Output ("            " + ($_.Path -replace [regex]::Escape($root), '') + ':' + $_.LineNumber + '  ' + $_.Line.Trim()) }
  }
}

# 3) acceptance table: summary row count must equal the body row count
if ($specTxt -ne '') {
  $cNa = ([char[]]@(0x4E0D,0x9002,0x7528) -join '')     # "bu shi yong" = not applicable (registered exception)
  $rows = @(($specTxt -split "`n") | Where-Object {
      $_ -match '^\|\s*(\d+|\d+-\d+)\s*\|' -and
      ($_.Contains($cPass) -or $_.Contains($cUny) -or $_.Contains($cUdo) -or $_.Contains($cNa)) }).Count
  $claim = [regex]::Match($specTxt, '([0-9]+)\s*' + [char]0x884C)
  if ($claim.Success) {
    $c = [int]$claim.Groups[1].Value
    if ($c -eq $rows) { Say 'PASS' 'acceptance-table' "summary $c == body $rows rows" }
    else { $fail++; Say 'FAIL' 'acceptance-table' "summary claims $c rows, body has $rows rows" }
  } else { $human++; Say 'HUMAN-ONLY' 'acceptance-table' "body has $rows rows; no summary number found" }
} else { $fail++; Say 'FAIL' 'acceptance-table-missing' $spec }

# 4) allowed-diff table exists, header carries the three required columns, every row has 4 cells
if ($allowTxt -eq '') { $fail++; Say 'FAIL' 'allowed-diff' 'section not found in acceptance table' }
else {
  $aLines = @(($allowTxt -split "`n") | ForEach-Object { $_.TrimEnd("`r") })
  $header = @($aLines | Where-Object { $_ -match '^\|' -and $_.Contains($cWhy) })[0]
  if (-not $header) { $fail++; Say 'FAIL' 'allowed-diff' 'no header row carrying the why column' }
  elseif (-not ($header.Contains($cProv) -and $header.Contains($cWhen))) {
    $fail++; Say 'FAIL' 'allowed-diff' 'header must carry why + provenance + when columns'
  } else {
    $hIdx = [array]::IndexOf($aLines, $header)
    $dataRows = @($aLines[($hIdx + 1)..($aLines.Count - 1)] |
                 Where-Object { $_ -match '^\|' -and $_ -notmatch '^\|[\s\-:|]+\|$' })
    if ($dataRows.Count -eq 0) { $fail++; Say 'FAIL' 'allowed-diff' 'no entry rows' }
    else {
      $bad = @($dataRows | Where-Object { ($_ -split '\|').Count -ne ($header -split '\|').Count })
      if ($bad.Count -eq 0) { Say 'PASS' 'allowed-diff' "$($dataRows.Count) entries, header has why/provenance/when" }
      else { $fail++; Say 'FAIL' 'allowed-diff' "$($bad.Count) entries do not match header column count" }
    }
  }
}

# 5) every screenshot referenced by the acceptance table exists
#    Scope: only ACCEPTANCE ROWS count as "cited" (a row starts with "| <id> |"). The historical /
#    "problems already fixed" tables are records of past runs, not evidence for a delivery row -- counting
#    them makes the freshness check fail on pictures that must NOT be re-shot (they document an old bug).
$citedSrc = (($specTxt -split "`n") | Where-Object { $_ -match '^\|\s*(\d+|\d+-\d+)\s*\|' }) -join "`n"
if ($specTxt -ne '' -and (Test-Path $shotDir)) {
  $names = @{}
  foreach ($m in [regex]::Matches($citedSrc, 'Screenshots/([A-Za-z0-9_\-]+\.png)')) { $names[$m.Groups[1].Value] = 1 }
  foreach ($m in [regex]::Matches($citedSrc, '(?<![A-Za-z0-9_\-/.])([A-Za-z0-9_\-]+\.png)')) { $names[$m.Groups[1].Value] = 1 }
  $have = @(Get-ChildItem $shotDir -Filter *.png -File | ForEach-Object { $_.Name })
  $missing = @($names.Keys | Where-Object { $have -notcontains $_ })
  if ($missing.Count -eq 0) { Say 'PASS' 'screenshot-refs' "$($names.Count) referenced png all present" }
  else { $fail++; Say 'FAIL' 'screenshot-refs' "$($missing.Count) missing: $($missing -join ', ')" }
} else { $human++; Say 'HUMAN-ONLY' 'screenshot-refs' 'acceptance table or screenshot dir missing' }

# 5b) the row's OWN evidence class decides what counts as "evidence" (SKILL.md 4 + verify-template item 12):
#       "shu zhi lei"   (numeric) = a runtime log line + an assertion; a file on disk is enough, NO screenshot;
#       "biao xian lei" (visual)  = ONE contact sheet (+ its cells are queryable in the sheet's index).
#     Why this exists (measured): the previous shape fed EVERY cited png into the freshness check, so a purely
#     numeric row went red because its ILLUSTRATIVE screenshot was old -- while its real judge is a log line.
#     That is how 39/63 rows went red while nothing was actually stale, i.e. the gate demanded a re-shoot of
#     screens no numeric criterion ever looks at (SKILL.md 2 item 5: what can be judged offline must not go to Play).
#     A row carrying BOTH classes is treated as visual (the stricter side).
$rowEv = @()
foreach ($ln in ($specTxt -split "`n")) {
  if ($ln -notmatch '^\|\s*(\d+|\d+-\d+)\s*\|') { continue }
  if (-not ($ln.Contains($cPass) -or $ln.Contains($cUny) -or $ln.Contains($cUdo) -or $ln.Contains($cNa))) { continue }
  $rid = $Matches[1]
  $isNum = $ln.Contains($cNumeric); $isVis = $ln.Contains($cVisual)
  $rp = @(); foreach ($m in [regex]::Matches($ln, '([A-Za-z0-9_\-]+\.png)')) { $rp += $m.Groups[1].Value }
  $ra = @(); foreach ($m in [regex]::Matches($ln, '([A-Za-z0-9_\-\.]+\.(?:py|tsv|json|log|out\.txt))')) { $ra += $m.Groups[1].Value }
  $rowEv += [pscustomobject]@{ Id = $rid; Vis = $isVis; Num = $isNum; Pngs = @($rp | Sort-Object -Unique); Arts = @($ra | Sort-Object -Unique) }
}
$visRowEv = @($rowEv | Where-Object { $_.Vis })
$numRowEv = @($rowEv | Where-Object { -not $_.Vis })
$visPngSrc = @{}    # freshness universe = pngs cited by a VISUAL row only
$numPngNames = @{}
foreach ($r in $visRowEv) { foreach ($p in $r.Pngs) { $visPngSrc[$p] = 1 } }
foreach ($r in $numRowEv) { foreach ($p in $r.Pngs) { $numPngNames[$p] = 1 } }

# 6) evidence freshness -- judged BY CAUSE, from ONE explicit area map (SKILL.md 1.11 check 6 +
#    reference/verify-template.md items 6 / 6b):
#      * invalidation scope = the ROWS the change can really influence (same module / same feature
#        chain / same screen) -- NOT "any file in the project changed, so throw the whole batch away";
#      * that map is the ONLY data source for the re-shoot scope as well: the scene column below is
#        what `verify.ps1 -Reshoot` prints, and the driver accepts scenes from there and nowhere else.
#    Why (measured): the previous shape used the newest .cs / level .txt of the WHOLE project as the
#    baseline for every shot, so a single level-data edit invalidated all 63 cited shots -- including
#    the credit line, which no level file can touch. One edit cost a whole batch of re-shoots.
#    Fields: n = area; d = files that area's screens depend on (a directory = the whole tree);
#            p = regex over the cited screenshot names (every cited shot must match EXACTLY ONE area --
#                an unmapped shot is a FAIL, so a brand new screenshot can never silently escape);
#            s = the probe scene(s) that produce those shots (the re-shoot scope).
#    Baseline of a shot = newest file among its area's d (a file cannot be finer than that: mtime is
#    the only signal we have, and over-approximating is the safe direction for a freshness check).
$srcNewest = Get-ChildItem $srcDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1
$lvlNewest = Get-ChildItem (Join-Path $root 'client\Assets\Resources\Levels') -Recurse -Filter *.txt -File -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1
$code = @($srcNewest, $lvlNewest) | Where-Object { $_ } | Sort-Object LastWriteTime -Descending | Select-Object -First 1

$areas = @(
  # -- UI: one panel -> its own screens (shared UI files only for the multi-panel contact sheets) --
  @{ n = 'ui-credit'; d = @('client\Assets\Scripts\UI\MainMenuPanel.cs', 'client\Assets\Scripts\UI\BootPanel.cs', 'client\Assets\Scripts\UI\UIBuilder.cs', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts'); p = '^(v2_boot|t2_menu_run1|t2_menu_run2|t3_top|credit-crop-menu|credit-crop-boot|contact-credit)\.png$'; s = 'credit' },
  @{ n = 'ui-pause'; d = @('client\Assets\Scripts\UI\PausePanel.cs', 'client\Assets\Scripts\UI\UIBuilder.cs', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts'); p = '^pause(-vol-a|-vol-b)?\.png$'; s = 'pause' },
  @{ n = 'ui-intro'; d = @('client\Assets\Scripts\UI\LoadingPanel.cs', 'client\Assets\Scripts\UI\UIBuilder.cs', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts'); p = '^intro_(card|lives_check)\.png$'; s = 'intro' },
  @{ n = 'ui-result'; d = @('client\Assets\Scripts\UI\ResultPanel.cs', 'client\Assets\Scripts\UI\HudPanel.cs', 'client\Assets\Scripts\UI\UIBuilder.cs', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts'); p = '^(sec12-j-result|walk12end-5-result)\.png$'; s = 'section12,walk12end' },
  @{ n = 'ui-hud'; d = @('client\Assets\Scripts\UI\HudPanel.cs', 'client\Assets\Scripts\UI\GameOverPanel.cs', 'client\Assets\Scripts\UI\UIBuilder.cs', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts'); p = '^(final_stage|p1_mid)\.png$'; s = 'reshoot' },
  @{ n = 'ui-contact-panels'; d = @('client\Assets\Scripts\UI', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts'); p = '^contact-panels\.png$'; s = 'pause,intro,walk12end' },
  # -- World 1-1 --
  @{ n = 'p11-flag'; d = @('client\Assets\Resources\Levels\World1-1.txt', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Level\LevelProps.cs', 'client\Assets\Resources\Sprites'); p = '^(flag_top|flag_bottom|CASTLE_WALK)\.png$'; s = 'flag' },
  @{ n = 'p11-blocks'; d = @('client\Assets\Resources\Levels\World1-1.txt', 'client\Assets\Scripts\Module\Entities\BlockModule.cs', 'client\Assets\Scripts\Module\Entities\ItemModule.cs', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs'); p = '^(12-blocks-group|13-starbrick-star|14-multicoin-after10|15-hidden-before|16-hidden-oneup)\.png$'; s = 'reshoot,headhit' },
  @{ n = 'p11-form'; d = @('client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Player\PlayerModule.cs', 'client\Assets\Scripts\Module\Entities\ItemModule.cs', 'client\Assets\Resources\Levels\World1-1.txt'); p = '^(crouch_fire|cr2_big|cr2_small)\.png$'; s = 'crouch' },
  @{ n = 'p11-enemy'; d = @('client\Assets\Scripts\Module\Entities\EnemyModule.cs', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Resources\Levels\World1-1.txt', 'client\Assets\Resources\Sprites'); p = '^(probe-koopa-1|probe-koopa-2|v4_stage|v4_stage2)\.png$'; s = 'goomba,koopa,reshoot' },
  @{ n = 'p11-coinroom'; d = @('client\Assets\Resources\Levels\World1-1.txt', 'client\Assets\Resources\Levels\World1-1-Underground.txt', 'client\Assets\Scripts\Module\Level\PipeWarpTable.cs', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Flow\AppFlow.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Flow\StageSession.cs', 'client\Assets\Resources\Sprites', 'client\Assets\Resources\Sound'); p = '^((2[0-5])-.*|contact-formcarry)\.png$'; s = 'coinroom,bigroom,reshoot' },
  @{ n = 'p11-scenery'; d = @('client\Assets\Resources\Levels\World1-1.txt', 'client\Assets\Scripts\Module\Level\LevelProps.cs', 'client\Assets\Resources\Sprites'); p = '^contact-1-1-scenery\.png$'; s = 'reshoot' },
  # -- World 1-2 (three sections: main underground corridor / surface / bonus room) --
  @{ n = 'p12-look'; d = @('client\Assets\Resources\Levels\World1-2.txt', 'client\Assets\Scripts\Module\Level\LevelModule.cs', 'client\Assets\Scripts\Module\Level\LevelData.cs', 'client\Assets\Scripts\Module\Level\LevelProps.cs', 'client\Assets\Resources\Sprites'); p = '^(s12_early|s12_stage)\.png$'; s = 'section12,level12' },
  @{ n = 'p12-side'; d = @('client\Assets\Resources\Levels\World1-2.txt', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Flow\AppFlow.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Level\LevelData.cs', 'client\Assets\Resources\Sprites'); p = '^sec12-(a|b|c|m1)-.*\.png$'; s = 'section12' },
  @{ n = 'p12-surface'; d = @('client\Assets\Resources\Levels\World1-2-Surface.txt', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Flow\AppFlow.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Level\LevelProps.cs', 'client\Assets\Scripts\Module\Flow\StageContext.cs', 'client\Assets\Resources\Sprites'); p = '^(sec12-(d|d1|e|f|g|h|i)-.*|walk12end-[0-4]-.*)\.png$'; s = 'section12,walk12end' },
  @{ n = 'p12-sections'; d = @('client\Assets\Resources\Levels\World1-2.txt', 'client\Assets\Resources\Levels\World1-2-Surface.txt', 'client\Assets\Resources\Levels\World1-2-Underground.txt', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Flow\AppFlow.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Resources\Sprites'); p = '^(contact-1-2-sections|contact-pipe-transition|contact-sidepipe)\.png$'; s = 'section12,mouth2' },
  @{ n = 'p12-coinroom'; d = @('client\Assets\Resources\Levels\World1-2.txt', 'client\Assets\Resources\Levels\World1-2-Underground.txt', 'client\Assets\Scripts\Module\Level\PipeWarpTable.cs', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Flow\AppFlow.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Flow\StageSession.cs', 'client\Assets\Scripts\Module\Flow\StageContext.cs', 'client\Assets\Resources\Sprites'); p = '^((2[6-9]|30)-.*|sec12-[kl]-mouth.*)\.png$'; s = 'coinroom12,mouth2' },
  @{ n = 'p12-enemy'; d = @('client\Assets\Scripts\Module\Entities\EnemyModule.cs', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Resources\Levels\World1-2.txt', 'client\Assets\Resources\Sprites'); p = '^probe-(koopa-shell|piranha).*\.png$'; s = 'koopa,piranha' },
  @{ n = 'p12-platform'; d = @('client\Assets\Scripts\Module\Entities\PlatformModule.cs', 'client\Assets\Scripts\Module\Flow\StageSession.cs', 'client\Assets\Resources\Levels\World1-2.txt'); p = '^contact-platform\.png$'; s = 'platform,platformspawn' },
  # -- aggregate sheets that serve several rows at once: deps = UNION of the areas whose frames they
  #    carry (a sheet is only as fresh as the biggest input it draws), scene list = union of the scenes
  #    that produce those frames. Added 2026-09-19 with the two sheets that give the previously
  #    index-less visual rows a queryable cell (SKILL.md 4 / verify-template items 12 + 18).
  @{ n = 'mix-1-1-basics'; d = @('client\Assets\Scripts\UI\BootPanel.cs', 'client\Assets\Scripts\UI\MainMenuPanel.cs', 'client\Assets\Scripts\UI\HudPanel.cs', 'client\Assets\Scripts\UI\UIBuilder.cs', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts', 'client\Assets\Resources\Levels\World1-1.txt', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Level\LevelProps.cs', 'client\Assets\Resources\Sprites'); p = '^contact-1-1-basics\.png$'; s = 'boot,menu,reshoot,flag,crouch' },
  @{ n = 'mix-1-2-look'; d = @('client\Assets\Resources\Levels\World1-2.txt', 'client\Assets\Scripts\Module\Level\LevelModule.cs', 'client\Assets\Scripts\Module\Level\LevelData.cs', 'client\Assets\Scripts\Module\Level\LevelProps.cs', 'client\Assets\Resources\Sprites'); p = '^contact-1-2-look\.png$'; s = 'level12' }
)

function Newest-AreaFile([string[]]$paths) {
  $files = @()
  foreach ($p in $paths) {
    $full = Join-Path $root $p
    if (-not (Test-Path $full)) { continue }
    $item = Get-Item $full
    if ($item.PSIsContainer) { $files += @(Get-ChildItem $full -Recurse -File -ErrorAction SilentlyContinue) }
    else { $files += $item }
  }
  $files = @($files | Where-Object { $_.Name -notlike '*.meta' -and $_.Name -notlike '*.import' })
  return ($files | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
}
function Get-AreaOf([string]$name) {
  foreach ($a in $areas) { if ($name -match $a.p) { return $a } }
  return $null
}
function Get-AreaByName([string]$name) {
  foreach ($a in $areas) { if ($a.n -eq $name) { return $a } }
  return $null
}

$stale = @(); $unmapped = @(); $staleScenes = @{}
if ((Test-Path $shotDir) -and $code) {
  # scope = pngs cited by a VISUAL row (5b). A numeric row's illustrative png is NOT judged here: its
  # evidence is a log line / assertion (SKILL.md 4), and failing it would be a false positive.
  $citedNames = @{}
  foreach ($k in $visPngSrc.Keys) { $citedNames[$k] = 1 }
  $shotFiles = @(Get-ChildItem $shotDir -Filter *.png -File)
  $cited = @($shotFiles | Where-Object { $citedNames.ContainsKey($_.Name) })
  foreach ($f in $cited) {
    $a = Get-AreaOf $f.Name
    if ($null -eq $a) { $unmapped += $f.Name; continue }
    $base = Newest-AreaFile $a.d
    if (-not $base) { $base = $code }    # area whose sources are all missing -> project-wide (strict)
    if ($f.LastWriteTime -lt $base.LastWriteTime) {
      $stale += ("{0} < {1} (area={2} scene={3})" -f $f.Name, $base.Name, $a.n, $a.s)
      foreach ($sc in ($a.s -split ',')) { if ($sc.Trim().Length -gt 0) { $staleScenes[$sc.Trim()] = 1 } }
    }
  }
  $uncited = $shotFiles.Count - $cited.Count
  $sceneList = (@($staleScenes.Keys) | Sort-Object) -join ','

  if ($Reshoot) {
    Write-Output ("stale cited shots: {0}; areas to re-shoot (scene=...):" -f $stale.Count)
    $stale | ForEach-Object { Write-Output ('            ' + $_) }
    Write-Output ("RESHOOT_SCENES=" + $sceneList)
    exit 0
  }

  if ($unmapped.Count -gt 0) {
    $fail++; Say 'FAIL' 'evidence-freshness-map' "$($unmapped.Count) cited screenshot(s) match no area in the map -> add a row to the area map (an unmapped shot must never silently escape)"
    $unmapped | ForEach-Object { Write-Output ('            ' + $_) }
  }
  if ($stale.Count -eq 0) {
    Say 'PASS' 'evidence-freshness' "visual rows = $($visRowEv.Count) ($($cited.Count) cited png) all newer than their own area's newest source; numeric rows = $($numRowEv.Count) judged by their runtime artifacts (see numeric-evidence below), their $($numPngNames.Count) png NOT judged (SKILL.md 4); $uncited uncited file(s) ignored"
  } else {
    $fail++; Say 'FAIL' 'evidence-freshness' "$($stale.Count)/$($cited.Count) cited screenshots of VISUAL rows older than a file in their OWN area -> re-shoot exactly these (scene=...): $sceneList"
    $stale | Select-Object -First 40 | ForEach-Object { Write-Output ('            ' + $_) }
  }
} else { $human++; Say 'HUMAN-ONLY' 'evidence-freshness' 'no screenshot dir or no source' }

# 6b) numeric rows: evidence = a runtime log line / an assertion output (a file on disk is enough -- SKILL.md 4).
#     Judge: every non-png artifact a numeric row cites must RESOLVE to a file. The rows whose evidence is a
#     past run's log line and nothing else are reported as HUMAN-ONLY, never FAIL: the line lives in
#     client/Logs/<day>.log, which rotates daily, so it cannot be re-checked from disk -- re-running that
#     area's scene is a decision a human makes (SKILL.md 1.11 item 11: a check that cries wolf is worse).
$artDirs = @($shotDir, (Join-Path $root '.ai-tmp\test'), $srcDir,
             (Join-Path $root ('client\Assets\Resources\Levels')),
             (Join-Path (Join-Path (Join-Path $root ([char[]]@(0x539F,0x7248,0x8D44,0x6E90) -join '')) ([char[]]@(0x89E3,0x6790) -join '')) ([char[]]@(0x811A,0x672C) -join '')),
             $planDir)
$unresolved = @()
foreach ($r in $numRowEv) {
  foreach ($a in $r.Arts) {
    $found = $false
    foreach ($d in $artDirs) {
      if ($found -or -not (Test-Path $d)) { continue }
      if (@(Get-ChildItem $d -Recurse -File -Filter $a -ErrorAction SilentlyContinue).Count -gt 0) { $found = $true }
    }
    if (-not $found) { $unresolved += ('row ' + $r.Id + ' cites an artifact that resolves to nothing: ' + $a) }
  }
}
$artCount = 0; foreach ($r in $numRowEv) { $artCount += @($r.Arts).Count }
if ($unresolved.Count -eq 0) {
  Say 'PASS' 'numeric-evidence' "$artCount artifact reference(s) from $($numRowEv.Count) numeric row(s) all resolve to a file on disk"
} else {
  $fail++; Say 'FAIL' 'numeric-evidence' "$($unresolved.Count) numeric-row evidence reference(s) point at nothing"
  $unresolved | ForEach-Object { Write-Output ('            ' + $_) }
}
$logOnly = @($numRowEv | Where-Object { @($_.Arts).Count -eq 0 })
if ($logOnly.Count -gt 0) {
  $human++
  Say 'HUMAN-ONLY' 'numeric-log-only' ("$($logOnly.Count) numeric row(s) rest on a runtime log line with no on-disk artifact (rows: " + (($logOnly | ForEach-Object { $_.Id }) -join ',') + ") -- a past run's line cannot be re-checked from disk (client/Logs/<day>.log rotates): re-run that area's scene on demand"
) }

# 7) one-click recheck entry (this script)
Say 'PASS' 'verify-entry' $PSCommandPath

# 8) reference table: original value (with provenance) / ours / delta
if (Test-Path $refTable) {
  $t = Read-Utf8 $refTable
  $r = @(($t -split "`n") | Where-Object { $_ -match '^\|\s*\S' -and $_ -notmatch '^\|[\s\-:|]+\|$' }).Count
  if ($r -ge 2) { Say 'PASS' 'reference-table' "$r rows" }
  else { $fail++; Say 'FAIL' 'reference-table' "only $r rows - table not filled in" }
} else { $fail++; Say 'FAIL' 'reference-table-missing' 'required by skill section 2' }

# 9) no handoff / progress documents
$bad = @(Get-ChildItem $root -Recurse -Filter *.md -File -ErrorAction SilentlyContinue |
         Where-Object {
           $_.FullName -notmatch '\\Library\\' -and
           ($_.Name -like 'NEXT*' -or $_.Name.Contains($cHand) -or $_.Name.Contains($cProg)) })
if ($bad.Count -eq 0) { Say 'PASS' 'no-handoff-docs' '' }
else { $fail++; Say 'FAIL' 'no-handoff-docs' (($bad | ForEach-Object { $_.FullName -replace [regex]::Escape($root), '' }) -join ', ') }

# 10) engine self-name credit -- judged on what is RENDERED, never on the source text (SKILL.md 1.6).
#     Why this was rewritten: the old check grepped the SOURCE for 'by clover-engine', so it stayed
#     green through both defects the user reported --
#       (a) the TITLE screen (the "home page" the player sees) had no credit line at all;
#       (b) the boot line's source text was right but the NES pixel font maps a-z onto the UPPERCASE
#           glyph shapes, so the screen showed 'BY CLOVER-ENGINE'.
#     New judge = the runtime UI node tree + the pixels of the running screen:
#       * the probe scene 'credit' (probe.cs) reads the label under MainMenuPanel / BootPanel,
#         its actual text / font / visibility / anchors, the font's real glyph boxes, and the
#         ink profile of the bottom strip of the live screen; it writes
#         .ai-tmp/screenshots/credit-render.txt.
#       * this check asserts every field of that report, case-sensitively, and requires BOTH screens
#         (menu + boot) -- a line that is merely present *somewhere* does not count.
#     Asserted per scope: the panel really is the owner of that screen (MainMenuPanel / BootPanel) and
#     is open; the label is found / active / visible; text == 'by clover-engine' (case-sensitive);
#     font == PressStart2P; the font's glyph boxes say a-z differs from A-Z and 'y' descends; the pixels
#     of the line's own rect carry ink, the run-height spread says lowercase, and the line sits
#     bottom-centre; the measured strip is saved as an image that exists.
#     FAILS ON PURPOSE when the credit is missing / uppercase: the probe also ships two reverse-self-check
#     entries ('credit-case' = text forced to 'BY CLOVER-ENGINE', 'credit-missing' = line hidden); each
#     writes a report whose header says tamper=<tag>, which this check rejects AND whose assertions also
#     fail on their own (verified 2026-09-19, see ce-hua acceptance table).
#     Re-run the probe after any UI/font change; a stale report is a FAIL (evidence freshness).
$credit = 'by clover-engine'
$repPath = Join-Path $shotDir 'credit-render.txt'
$report = ''
if (Test-Path $repPath) { $report = Read-Utf8 $repPath }

function Credit-Line([string]$txt, [string]$scope, [string]$key) {
  foreach ($ln in ($txt -split "`n")) {
    if ($ln.TrimEnd("`r").StartsWith("credit|$scope|") -and $ln.Contains($key)) { return $ln.TrimEnd("`r") }
  }
  return ''
}
function Credit-Grab([string]$line, [string]$pattern) {
  $m = [regex]::Match($line, $pattern)
  if ($m.Success) { return $m.Groups[1].Value }
  return ''
}
function Credit-Num([string]$line, [string]$pattern, [double]$default) {
  $m = [regex]::Match($line, $pattern)
  if ($m.Success) { return [double]$m.Groups[1].Value }
  return $default
}

if ($report -eq '') {
  $fail++; Say 'FAIL' 'engine-credit' "no runtime render report at .ai-tmp/screenshots/credit-render.txt (run probe scene 'credit'; it is written by measurement, not by hand)"
} else {
  $bad = @()
  $tamper = Credit-Grab $report 'tamper=([a-z]+)'
  if ($tamper -ne 'none') { $bad += "report header says tamper='$tamper' -- that is a reverse-self-check run, not a delivery measurement" }
  foreach ($scope in @('menu', 'boot')) {
    $head = Credit-Line $report $scope 'panel='
    $txtL = Credit-Line $report $scope 'text='
    $fntL = Credit-Line $report $scope 'font='
    $metL = Credit-Line $report $scope 'fontMetrics'
    $pxL  = Credit-Line $report $scope 'pixels'
    $ancL = Credit-Line $report $scope 'anchor'
    if ($head -eq '' -or $txtL -eq '' -or $fntL -eq '' -or $metL -eq '' -or $pxL -eq '') {
      $bad += "${scope}: report has no complete measurement block"; continue
    }
    foreach ($k in 'open=true', 'label=Signature', 'found=true', 'active=true', 'visible=true', 'exact=true') {
      if (-not $head.Contains($k)) { $bad += "${scope}: '$k' not satisfied -> $head" }
    }
    # each scope must be the PANEL THAT OWNS THAT SCREEN -- "some panel somewhere carries the string"
    # does not satisfy the rule, and neither does a label that sits on an inactive/other panel.
    $wantPanel = if ($scope -eq 'menu') { 'panel=MainMenuPanel' } else { 'panel=BootPanel' }
    if (-not $head.Contains($wantPanel)) { $bad += "${scope}: the measured label is not on $wantPanel -> $head" }
    # the text must be the required string EXACTLY (case-sensitive: 'BY clover-engine' must fail)
    $got = Credit-Grab $txtL 'text="([^"]*)"'
    if (-not ($got -ceq $credit)) { $bad += "${scope}: rendered text is '$got', required exactly '$credit'" }
    # the font must be the lowercase-capable one, and its a-z glyphs must NOT equal the A-Z shapes
    $fname = Credit-Grab $fntL 'font=([^ ]+)'
    if (-not ($fname -ceq 'PressStart2P')) { $bad += "${scope}: label font is '$fname', required 'PressStart2P' (the NES pixel font renders a-z as A-Z)" }
    $lower = Credit-Grab $metL 'lowercaseShaped=([a-z]+)'
    if ($lower -ne 'true') { $bad += "${scope}: font glyph boxes say lowercaseShaped=$lower (a-z must differ from A-Z and 'y' must descend) -> $metL" }
    # pixels of the live screen's bottom strip: a real lowercase line has x-height glyphs, so the top
    # rows of the ink band carry far less ink than the middle rows. All-uppercase shapes make every
    # row equally dense (ratio ~0.9); true lowercase is ~0.2. Require < 0.5.
    $ink = [int](Credit-Num $pxL 'ink=(\d+)' -1)
    $ratio = Credit-Num $pxL 'topRatio=([-\d.]+)' -1
    $gap = [int](Credit-Num $pxL 'bandBottomGapPx=(-?\d+)' -1)
    $short = [int](Credit-Num $pxL 'shortRuns=(\d+)' -1)
    $hmax = [int](Credit-Num $pxL 'hMax=(\d+)' -1)
    $off = [int](Credit-Num $pxL 'screenCenterOffsetPx=(-?\d+)' 9999)
    if ($ink -lt 20) { $bad += "${scope}: no/negligible ink on the line's own rect (ink=$ink) => nothing was rendered there -> $pxL" }
    # THE lowercase criterion, derived from the FONT FILE and not from our own render (skill 1.12 item 4):
    # .ai-tmp/test/font_predict.py walks the two candidate fonts at 16px and reports the per-glyph run
    # heights for 'by clover-engine'. The 8x8 pixel font actually used (Press Start, prstart.ttf) gives
    # [3, 11x8, 13, 13, 15, 15, 15] -> 10 of the 15 runs are >= 3px shorter than the tallest. The old
    # NES font of this project gives a single uniform 15px height for every letter (plus a 5px hyphen)
    # -> exactly 1 of 15. So shortRuns >= 6 separates "really lowercase" from "a-z drawn as A-Z"
    # by construction: run that script again to re-derive both numbers without touching the game.
    if ($short -lt 6) { $bad += "${scope}: only $short glyph run(s) are shorter than hMax-3 (hMax=$hmax) => the shapes on screen are NOT lowercase -> $pxL" }
    if ($ratio -lt 0 -or $ratio -ge 0.5) { $bad += "${scope}: ink band is uniformly dense (topRatio=$ratio >= 0.5) => the glyphs are NOT lowercase -> $pxL" }
    if ($gap -lt 0 -or $gap -gt 64) { $bad += "${scope}: credit line sits $gap px above the bottom edge (must be at the bottom, <= 64) -> $pxL" }
    if ($off -eq 9999) { $bad += "${scope}: no screen-centre offset was measured -> $pxL" }
    elseif ([Math]::Abs($off) -gt 4) { $bad += "${scope}: credit line is off-centre by $off px (must be bottom-CENTRE) -> $pxL" }
    $crop = Credit-Grab $pxL 'crop=([^ ]+)'
    if ($crop -eq '' -or $crop -eq 'none') { $bad += "${scope}: the measured strip was not saved as an image -> $pxL" }
    elseif (-not (Test-Path (Join-Path $shotDir $crop))) { $bad += "${scope}: evidence crop '$crop' is missing from .ai-tmp/screenshots -> $pxL" }
    # the title-screen line must be anchored to the bottom centre (not a fixed offset that can fall off)
    if ($scope -eq 'menu') {
      if (-not ($ancL.Contains('min=(0.50,0.00)') -and $ancL.Contains('max=(0.50,0.00)'))) {
        $bad += "menu: credit line is not anchored to the bottom centre -> $ancL"
      }
    }
  }
  if ($bad.Count -eq 0) {
    Say 'PASS' 'engine-credit' "rendered on BOTH screens (title screen $((Split-Path $repPath -Leaf)) scope 'menu' + boot screen scope 'boot'): text '$credit' exact (case-sensitive), font PressStart2P, lowercase glyphs in the pixels (shortRuns>=6 / topRatio<0.5), bottom-centre"
  } else {
    $fail++; Say 'FAIL' 'engine-credit' "$($bad.Count) assertion(s) failed on the RENDERED credit line"
    $bad | ForEach-Object { Write-Output ('            ' + $_) }
  }
  # the report is runtime evidence: it must be newer than the files that can change THAT LINE --
  # the area map's own ui-credit entry (menu/boot panels + UIBuilder + ResPaths + fonts).
  # It must NOT be compared against the project-wide newest file: a level-data or gameplay edit
  # cannot change the credit line, and doing exactly that produced the false FAIL reported by the
  # user ("World1-2.txt changed, so the signature row is invalid"). SKILL.md 1.11 check 6.
  if ((Test-Path $repPath) -and $code) {
    $ca = Get-AreaByName 'ui-credit'
    $newest = Newest-AreaFile $ca.d
    if (-not $newest) { $newest = $code }
    $rep = Get-Item $repPath
    if ($rep.LastWriteTime -lt $newest.LastWriteTime) {
      $fail++; Say 'FAIL' 'engine-credit-freshness' "report $($rep.LastWriteTime) is OLDER than $($newest.Name) $($newest.LastWriteTime) -> re-run probe scene 'credit' (area ui-credit)"
    } else {
      Say 'PASS' 'engine-credit-freshness' "report ($($rep.LastWriteTime)) newer than area ui-credit's newest source $($newest.Name) ($($newest.LastWriteTime))"
    }
  }
}

# 11) gate artifacts: baseline images / asset research doc / spec doc
$basePng = @(Get-ChildItem $baseDir -Recurse -Filter *.png -File -ErrorAction SilentlyContinue)
if ($basePng.Count -gt 0) { Say 'PASS' 'baseline-images' "$($basePng.Count) png" }
else { $fail++; Say 'FAIL' 'baseline-images' 'missing or empty' }
if (Test-Path $assetDoc) { Say 'PASS' 'asset-research-doc' '' } else { $fail++; Say 'FAIL' 'asset-research-doc-missing' 'gate 3 requires it' }
$specDocs = @(Get-ChildItem $caseDir -Filter *.md -File -ErrorAction SilentlyContinue)
if ($specDocs.Count -gt 0) { Say 'PASS' 'spec-doc' "$($specDocs.Count) file(s) in ce-hua-an" }
else { $fail++; Say 'FAIL' 'spec-doc-missing' 'gate 0 requires the form/scope doc under ce-hua-an' }

# 12) no *recent* async team-member sessions: that channel bypasses model:inherit, so the executor
#     would run on the default (auto) model instead of the main agent's model.
#     Scoped to a time window on purpose -- stale leftovers from earlier work must not raise a
#     false positive (a check that cries wolf is worse than no check).
$wsRoot = Split-Path $root -Parent
$teamCut = (Get-Date).AddHours(-24)
$teams = @(Get-ChildItem (Join-Path $wsRoot '.codebuddy\teams') -Directory -ErrorAction SilentlyContinue |
           Where-Object { $_.LastWriteTime -gt $teamCut })
if ($teams.Count -eq 0) { Say 'PASS' 'no-team-sessions' 'no async team session active within 24h' }
else { $fail++; Say 'FAIL' 'no-team-sessions' "$($teams.Count) session(s) active within 24h: $($teams.Name -join ', ') (executors must be plain sub-agents = same model as main agent)" }

# 13) project artifacts must not escape the project (skill 1.8: one-off outputs live in .ai-tmp/test/).
#     Check 1 only looks INSIDE the project, so anything written one level out (workspace root) or into
#     the host's session-artifact store was invisible -- a one-off report written there left every
#     check green. Detection = "name carries this project's identity" + "created in the last 24h".
#     Identity comes from the project directory name at run time, NOT from a hard-coded file name.
#     The time window is deliberate (same reason as 12): other projects' stale files must not raise a
#     false positive -- a check that cries wolf is worse than no check.
#     Other hosts: append their product dir to $brainZones; leaving it out narrows the check, it never
#     loosens the workspace-root half.
$wsRoot = Split-Path $root -Parent          # self-contained: do not depend on check 12 defining it
$rootName = Split-Path $root -Leaf
$projTokens = @($rootName)
if ($rootName.StartsWith('clover-project-')) { $projTokens += $rootName.Substring(15) }   # short name
$brainZones = @()
if ($env:APPDATA) {
  $brainZones = @(Get-ChildItem (Join-Path $env:APPDATA '*\User\globalStorage\*\brain') -Directory -ErrorAction SilentlyContinue |
                  ForEach-Object { $_.FullName })
}
$escCut = (Get-Date).AddHours(-24)
$esc = @()
foreach ($t in $projTokens) {
  $esc += @(Get-ChildItem $wsRoot -File -Filter ("*" + $t + "*") -ErrorAction SilentlyContinue |
            Where-Object { $_.CreationTime -gt $escCut })
  foreach ($z in $brainZones) {
    $esc += @(Get-ChildItem $z -Recurse -File -Filter ("*" + $t + "*") -ErrorAction SilentlyContinue |
              Where-Object { $_.CreationTime -gt $escCut })
  }
}
$esc = @($esc | Sort-Object FullName -Unique)
if ($esc.Count -eq 0) { Say 'PASS' 'no-escaped-artifacts' "workspace root + $($brainZones.Count) host artifact dir(s): 0 hit" }
else {
  $fail++; Say 'FAIL' 'no-escaped-artifacts' "$($esc.Count) project file(s) outside the project -> move into .ai-tmp/test/ (section 1.8)"
  $esc | ForEach-Object { Write-Output ('            ' + $_.FullName) }
}

# 12) acceptance table rows must carry an evidence CLASS (SKILL.md section 2, hard rule 3):
#     "shu zhi lei" (numeric) or "biao xian lei" (visual). The class is the ONLY criterion for
#     "does this row need a screenshot" -- without it the executor has to guess, and guesses either
#     shoot everything (an order of magnitude more expensive) or shoot nothing (numbers right, picture wrong).
#     Scope: only rows that carry a conclusion word. The "problems already fixed" / history tables also
#     start with "| <n> |" but they are records, not acceptance rows (same scoping as check 3).
if ($specTxt -ne '') {
  $noCat = @(($specTxt -split "`n") | Where-Object {
      $_ -match '^\|\s*(\d+|\d+-\d+)\s*\|' -and
      ($_.Contains($cPass) -or $_.Contains($cUny) -or $_.Contains($cUdo) -or $_.Contains($cNa)) -and
      -not ($_.Contains($cNumeric) -or $_.Contains($cVisual)) }).Count
  if ($noCat -eq 0) { Say 'PASS' 'row-category' 'every acceptance row carries a 数值类 / 表现类 class' }
  else { $fail++; Say 'FAIL' 'row-category' "$noCat acceptance row(s) without an evidence class (SKILL.md section 2 hard rule 3)" }
}

# 14) sampler self-check (SKILL.md 1.13 invariant 5): everything checkable in seconds BEFORE a minute-long
#     run. Every .ps1 under .ai-tmp: (a) parses with 0 syntax errors, (b) is NOT "non-ASCII without BOM" --
#     PS 5.1 reads a BOM-less .ps1 as ANSI, so CJK literals silently break -match, the done marker never
#     matches, and every run burns the full timeout. The driver scripts get their self-check here.
$tmpRoot = Join-Path $root '.ai-tmp'
$badPs = @()
if (Test-Path $tmpRoot) {
  foreach ($f in @(Get-ChildItem $tmpRoot -Recurse -Filter *.ps1 -File -ErrorAction SilentlyContinue)) {
    $b = [System.IO.File]::ReadAllBytes($f.FullName)
    $bom = ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
    $nonAscii = @($b | Where-Object { $_ -gt 127 }).Count
    if ((-not $bom) -and $nonAscii -gt 0) { $badPs += ($f.Name + " (ANSI trap: non-ASCII without BOM)") }
    $enc = [System.Text.Encoding]::UTF8
    if (-not $bom) { $enc = [System.Text.Encoding]::Default }     # parse it the way PS 5.1 will
    $text = $enc.GetString($b)
    if ($bom) { $text = $text.TrimStart([char]0xFEFF) }
    $psk = $null; $per = $null
    [void][System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$psk, [ref]$per)
    if (@($per).Count -gt 0) { $badPs += ($f.Name + " (" + @($per).Count + " syntax error)") }
  }
}
if ($badPs.Count -eq 0) { Say 'PASS' 'sampler-selfcheck' 'scripts under .ai-tmp: syntax OK, no ANSI trap' }
else { $fail++; Say 'FAIL' 'sampler-selfcheck' ($badPs -join '; ') }

# 15) implementation must be produced by a dispatched executor (SKILL.md section 5 gate + self-check 8):
#     reconcile the implementation files changed inside the task window against
#     .ai-tmp/test/dispatch-log.tsv. Why this exists: a main agent that implements by itself burns the
#     host's per-task model-request ceiling and stalls mid-way with a half-finished job. The rule was
#     already written down, but it was never tied to an action entry point, so violating it left no trace
#     -- this check turns "dispatch" into an artifact that must exist, and reconciles the two sides.
#     Criterion: every implementation file matches a dispatch row (file inside that row's declared scope
#     AND the row's time <= the file's mtime).
#     Scope is the task window only (24h) -- scanning all sources would flag every historical file.
#     (The whole client/Assets/Scripts tree is walked recursively; the template's "**/*.cs" glob does not
#      recurse under Get-ChildItem, so depth-3 files such as Module/Gameplay/*.cs would be missed.)
$logPath = Join-Path $root '.ai-tmp/test/dispatch-log.tsv'
$implCut = (Get-Date).AddHours(-24)
# The rule cannot judge work that predates it. This check was added AFTER the fact, and its first run
# correctly flagged 25 files the main agent had already edited by hand before the rule existed.
# A check that permanently reports a pre-rule violation is a false positive by construction (see
# check 11's lesson: keep every check inside its own time window -- a check that cries wolf is worse
# than no check), so files older than the rule's effective moment are excluded and reported as context.
# This is a scope fix, NOT a whitewash: the deviation itself is on the record as a comment row in
# dispatch-log.tsv, and any file touched after this moment must still reconcile.
#     The boundary is the moment the ledger itself was created (that IS when the gate started applying);
#     files older than it are pre-rule history, and the deviation is on the record in the ledger.
$implRuleFrom = [datetime]'2026-09-18T21:40:00'   # dispatch-log.tsv created / SKILL.md section 5 gate shipped
$implFiles = @()
$preRule = 0
foreach ($f in @(Get-ChildItem $srcDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue)) {
  if ($f.LastWriteTime -gt $implCut -and $f.LastWriteTime -gt $implRuleFrom) { $implFiles += $f }
  elseif ($f.LastWriteTime -gt $implCut) { $preRule++ }
}
foreach ($f in @(Get-ChildItem (Join-Path $root 'client\Assets\Resources\Levels') -Filter *.txt -File -ErrorAction SilentlyContinue)) {
  if ($f.LastWriteTime -gt $implCut -and $f.LastWriteTime -gt $implRuleFrom) { $implFiles += $f }
  elseif ($f.LastWriteTime -gt $implCut) { $preRule++ }
}
foreach ($ext in '*.csv', '*.xlsx') {
  $implFiles += @(Get-ChildItem $planDir -Filter $ext -File -ErrorAction SilentlyContinue |
                  Where-Object { $_.LastWriteTime -gt $implCut })
}
$implFiles = @($implFiles | Sort-Object FullName -Unique)
$dispatched = @()
if (Test-Path $logPath) {
  foreach ($line in @([System.IO.File]::ReadAllLines($logPath, [System.Text.Encoding]::UTF8))) {
    if ($line -match '^\s*#' -or $line.Trim().Length -eq 0) { continue }
    $c = $line -split "`t"
    if ($c.Count -ge 4) { $dispatched += [pscustomobject]@{ At = $c[0]; By = $c[1]; Task = $c[2]; Scope = $c[3] } }
  }
}
# A row is only valid INSIDE its own window: [row time, next row time). Without this, one early row with a
# broad scope (e.g. "client/Assets/Scripts") silently ratifies every later change in that tree -- which is
# exactly what happened on this project's first run of the check (a 21:40 row covered files touched at 02:15).
# Each dispatch therefore has to be recorded before the work it authorises, and can only cover that slice.
$dispatched = @($dispatched | Sort-Object { [datetime]$_.At })
for ($i = 0; $i -lt $dispatched.Count; $i++) {
  if ($i + 1 -lt $dispatched.Count) { $dispatched[$i] | Add-Member -NotePropertyName Until -NotePropertyValue ([datetime]$dispatched[$i + 1].At) -Force }
  else { $dispatched[$i] | Add-Member -NotePropertyName Until -NotePropertyValue ([datetime]'9999-01-01') -Force }
}
$orphan = @()
# An executor occasionally has to touch a carrier file the task book forgot to whitelist (e.g. the parser
# for a data format the task just extended). The main agent adjudicates those by name in the ledger:
#     # adjudicated: <relative path> -- <executor>(<task>) <when>; reason ...
# Such a file is reconciled-with-note instead of FAIL -- the point of this check is to make a
# main-agent write VISIBLE (and auditable), not to pretend it is impossible.
$adjudicated = @()
if (Test-Path $logPath) {
  # Two ledger forms are honoured (reference/verify-template.md item 15):
  #   # adjudicated: <path>[, <path>...]  -- a carrier file the task book forgot to authorise; the
  #                                        main agent names it in the ledger (stays on the record).
  #   # direct-fix:  <path>[, <path>...]  -- SKILL.md 5 "small fix" exception: a user-reported point
  #                                        defect, single file, <= 20 net lines, no new behaviour and
  #                                        no data-format change => the main agent may fix it directly
  #                                        and just records one line. It is NOT a laundering channel:
  #                                        multi-file / format / new-behaviour changes still need a
  #                                        dispatch row.
  # Both are "make the write VISIBLE", not "make it allowed": the check's point is that a main-agent
  # write cannot happen silently.
  # The annotation is cut off at " -- " / " - " (em dash) / ";" so a comma separated path list works.
  # The em dash is built from a code point: this file must stay ASCII-only (see the note at the top).
  $seps = @(' -- ', ';', (' ' + [char]0x2014 + ' '))
  foreach ($line in @([System.IO.File]::ReadAllLines($logPath, [System.Text.Encoding]::UTF8))) {
    if ($line -match '^\s*#\s*(?:adjudicated|direct-fix):\s*(.+)') {
      $rhs = $Matches[1]
      $cut = $rhs.Length
      foreach ($sep in $seps) {
        $i = $rhs.IndexOf($sep)
        if ($i -ge 0 -and $i -lt $cut) { $cut = $i }
      }
      foreach ($p in (($rhs.Substring(0, $cut)) -split ',')) {
        $q = $p.Trim().Replace('\', '/')
        if ($q.Length -gt 0) { $adjudicated += $q }
      }
    }
  }
}
$adjudicatedHits = @()
foreach ($f in $implFiles) {
  $rel = $f.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
  $hit = @($dispatched | Where-Object {
      $scopes = @($_.Scope -split '[,;]') | ForEach-Object { $_.Trim().Replace('\', '/') } | Where-Object { $_.Length -gt 0 }
      $inScope = @($scopes | Where-Object { $rel -like ($_ + '*') }).Count -gt 0
      $inScope -and ([datetime]$_.At) -le $f.LastWriteTime -and $f.LastWriteTime -lt $_.Until
    })
  if ($hit.Count -eq 0 -and $adjudicated -contains $rel) { $adjudicatedHits += $rel; continue }
  if ($hit.Count -eq 0) { $orphan += $rel }
}
if ($implFiles.Count -eq 0) { $human++; Say 'HUMAN-ONLY' 'impl-by-executor' "no implementation file changed after the rule's effective time ($implRuleFrom); $preRule file(s) older than it are excluded as pre-rule history" }
elseif ($orphan.Count -eq 0) {
  $extra = ''
  if ($adjudicatedHits.Count -gt 0) { $extra = "; $($adjudicatedHits.Count) adjudicated by name in the ledger ($($adjudicatedHits -join ', '))" }
  Say 'PASS' 'impl-by-executor' "$($implFiles.Count) implementation file(s) all reconcile with a dispatch row$extra"
}
else {
  $fail++; Say 'FAIL' 'impl-by-executor' "$($orphan.Count)/$($implFiles.Count) implementation file(s) have no dispatching record => the main agent implemented by itself (section 5)"
  $orphan | ForEach-Object { Write-Output ('            ' + $_) }
}

# ---------------------------------------------------------------------------
# 16) play-budget -- SKILL.md 2 item 2 + reference/verify-template.md item 16.
#     Every editor Play session is one row of <root>/.ai-tmp/test/play-log.tsv
#     (ISO time / executor / piece / why this chain was unavoidable). The ledger is ALSO the round boundary:
#     the LAST "# ROUND-START <iso>" comment splits HISTORY (diagnostic rounds run under the older rules --
#     every history row must carry the relaxation word, otherwise the accounting is incomplete/unauditable)
#     from THIS piece (rows <= 5). Why the gate exists: one Play session is minutes long, and the old rules
#     only asked nicely ("capture in one batch") with no check at all, so the ceiling was bypassed every time.
#     A project that HAS runtime evidence but NO ledger FAILs: without it the play count cannot be audited.
$playLog  = Join-Path $root '.ai-tmp\test\play-log.tsv'
$playBudget = 5
$cRelax = ([char[]]@(0x653E,0x5BBD) -join '')      # fang kuan = the explicit "relaxed" marker
$roundStart = $null
$playHist = @(); $playCur = @()
if (-not (Test-Path $playLog)) {
  $fail++; Say 'FAIL' 'play-budget' "no $playLog -- one line per editor Play session is required (the project has runtime evidence, so the play count must be auditable)"
} else {
  $lines = @([System.IO.File]::ReadAllLines($playLog, [System.Text.Encoding]::UTF8))
  $roundLine = -1
  for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*#\s*ROUND-START\s+(\d{4}-\d\d-\d\dT\d\d:\d\d)') { $roundStart = [datetime]$Matches[1]; $roundLine = $i }
  }
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line -match '^\s*#' -or $line.Trim().Length -eq 0) { continue }
    $c = $line -split "`t"
    $obj = [pscustomobject]@{ At = $c[0]; Scene = $(if ($c.Count -ge 3) { [string]$c[2] } else { '' }); Why = $(if ($c.Count -ge 4) { [string]$c[3] } else { '' }) }
    if ($i -lt $roundLine) { $playHist += $obj } else { $playCur += $obj }
  }
  $bad = @()
  foreach ($r in $playHist) { if (-not $r.Why.Contains($cRelax)) { $bad += ('history row without the relaxation marker: ' + $r.At + ' ' + $r.Scene) } }
  foreach ($r in @($playHist + $playCur)) { if ($r.Why.Trim().Length -lt 4) { $bad += ('row without a reason in column 4: ' + $r.At + ' ' + $r.Scene) } }
  if ($roundStart -eq $null) {
    $fail++; Say 'FAIL' 'play-budget' "no '# ROUND-START <iso>' boundary in play-log.tsv -- without it the per-piece budget (<= $playBudget) cannot be audited"
  } elseif ($bad.Count -gt 0) {
    $fail++; Say 'FAIL' 'play-budget' "$($bad.Count) ledger row(s) are unusable"
    $bad | Select-Object -First 10 | ForEach-Object { Write-Output ('            ' + $_) }
  } elseif ($playCur.Count -gt $playBudget) {
    $fail++; Say 'FAIL' 'play-budget' "this piece = $($playCur.Count) Play session(s) > budget $playBudget (SKILL.md 2 item 2) -- stop and report instead of expanding the re-shoot (SKILL.md 2 item 4)"
  } else {
    Say 'PASS' 'play-budget' "this piece = $($playCur.Count) / budget $playBudget Play session(s); history = $($playHist.Count) diagnostic row(s), all carrying the relaxation marker; round start = $($roundStart.ToString('MM-dd HH:mm'))"
  }
}
if ($roundStart -eq $null) { $roundStart = (Get-Date).AddHours(-6) }   # no ledger/marker => fall back to the template's 6h window

# 17) freeze-before-capture -- SKILL.md 2 item 4 ("capture freezes what was captured") +
#     reference/verify-template.md item 17. A contact sheet is DERIVED evidence: if a frame it draws is
#     newer than the sheet itself, the sheet shows an older state than the frames on disk -- i.e. it was
#     captured before the last re-shoot and must be rebuilt (only the affected sheet, not everything).
#     Scope note: the template's literal form (oldest new png in a 6h window = T0, no impl file after it)
#     was measured here and it is a false positive generator -- after a per-area re-shoot it flags 25 pngs
#     and 7 screens (one of which, res_wide.png, has no probe scene left at all). SKILL.md 2 item 4 says
#     "re-capture only the affected rows", so the freeze is judged per derived artifact, which is the scope
#     the rule actually needs; the historical rows of play-log.tsv carry the batch history.
$idxFiles = @()
if (Test-Path $shotDir) { $idxFiles = @(Get-ChildItem $shotDir -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*-index.tsv' -or $_.Name -like '*.index.tsv' }) }
$frozenBad = @(); $sheetSeen = 0
foreach ($ix in $idxFiles) {
  $sheetName = ($ix.Name -replace '(-index|\.index)\.tsv$', '') + '.png'
  $sheetPath = Join-Path $shotDir $sheetName
  if (-not (Test-Path $sheetPath)) { $frozenBad += ($ix.Name + ' -> its sheet ' + $sheetName + ' does not exist'); continue }
  $sheetSeen++
  $st = (Get-Item $sheetPath).LastWriteTime
  foreach ($line in @([System.IO.File]::ReadAllLines($ix.FullName, [System.Text.Encoding]::UTF8))) {
    if ($line.Trim().Length -eq 0 -or $line -match '^cell\t') { continue }
    $c = $line -split "`t"
    if ($c.Count -lt 4) { continue }
    # the 4th column is the frame name, sometimes with a suffix ("...(裁剪)" / "...（修前实机帧）"):
    # take the first file-looking token so both suffix styles are handled.
    $fm = [regex]::Match($c[3], '([A-Za-z0-9_\-\.]+\.(?:png|jpg|jpeg|gif))')
    if (-not $fm.Success) { continue }
    $frame = $fm.Groups[1].Value
    $fp = $null
    foreach ($d in @($shotDir, (Join-Path $root '.ai-tmp\test'), $baseDir)) { $t = Join-Path $d $frame; if (Test-Path $t) { $fp = $t; break } }
    if (-not $fp) { $frozenBad += ($sheetName + ': its index names a frame that resolves to nothing -> ' + $frame); continue }
    if ((Get-Item $fp).LastWriteTime -gt $st) { $frozenBad += ($sheetName + ' is OLDER than a frame in its own index: ' + $frame + ' (' + (Get-Item $fp).LastWriteTime.ToString('MM-dd HH:mm') + ') -> rebuild only this sheet') }
  }
}
if ($frozenBad.Count -eq 0) {
  Say 'PASS' 'freeze-before-capture' "$sheetSeen contact sheet(s): each is newer than every frame named in its own *-index.tsv (derived evidence postdates its inputs)"
} else {
  $fail++; Say 'FAIL' 'freeze-before-capture' "$($frozenBad.Count) derived-evidence problem(s) => rebuild exactly these sheets, then state which re-capture number this is (SKILL.md 2 item 4: 3rd time => stop and report)"
  $frozenBad | ForEach-Object { Write-Output ('            ' + $_) }
}

# 18) evidence-economy -- SKILL.md 2 item 1/T0 + reference/verify-template.md item 18.
#     ① every VISUAL row's cell must be queryable in some contact-sheet index (格号 <-> row id);
#     ② pngs added since the round start <= max(12, visual rows * 2)  (one screenshot per row is the violation);
#     ③ no scene may be captured >= 3 times in this piece (SKILL.md 2 item 4 => stop and report).
$cellRow = @{}
foreach ($ix in $idxFiles) {
  foreach ($line in @([System.IO.File]::ReadAllLines($ix.FullName, [System.Text.Encoding]::UTF8))) {
    if ($line.Trim().Length -eq 0 -or $line -match '^cell\t') { continue }
    $c = $line -split "`t"
    if ($c.Count -lt 2) { continue }
    foreach ($one in ($c[1] -split '[/,]')) {
      $id = $one.Trim().TrimStart('#').Trim()
      if ($id.Length -gt 0) { $cellRow[$id] = $ix.Name }
    }
  }
}
$noCell = @($visRowEv | Where-Object { -not $cellRow.ContainsKey($_.Id) })
$newShots = @()
if (Test-Path $shotDir) { $newShots = @(Get-ChildItem $shotDir -Filter *.png -File | Where-Object { $_.LastWriteTime -gt $roundStart }) }
$cap = [Math]::Max(12, $visRowEv.Count * 2)
$scCount = @{}
foreach ($r in $playCur) { if ($r.Scene.Trim().Length -eq 0) { continue }; if ($scCount.ContainsKey($r.Scene)) { $scCount[$r.Scene]++ } else { $scCount[$r.Scene] = 1 } }
$repScene = @($scCount.Keys | Where-Object { $scCount[$_] -ge 3 })
if ($noCell.Count -gt 0) {
  $fail++; Say 'FAIL' 'evidence-economy' "$($noCell.Count)/$($visRowEv.Count) visual row(s) have no cell in any contact-sheet index => their visual evidence is a raw frame nobody aggregates (add a cell + index row)"
  $noCell | ForEach-Object { Write-Output ('            row ' + $_.Id + ' (png: ' + (@($_.Pngs) -join ',') + ')') }
} elseif ($newShots.Count -gt $cap) {
  $fail++; Say 'FAIL' 'evidence-economy' "new png since round start = $($newShots.Count) > cap $cap (max(12, visual rows $($visRowEv.Count) * 2)) => one screenshot per row (SKILL.md 2 item 1 forbids it)"
} elseif ($repScene.Count -gt 0) {
  $fail++; Say 'FAIL' 'evidence-economy' ("scene(s) captured >= 3 times in this piece: " + ($repScene -join ',') + " -- stop and report (SKILL.md 2 item 4)")
} else {
  Say 'PASS' 'evidence-economy' "all $($visRowEv.Count) visual row(s) resolve to a contact-sheet cell; new png since round start = $($newShots.Count) / cap $cap; no scene captured >= 3 times in this piece"
}

Write-Output ''
Write-Output "===== summary: FAIL=$fail  HUMAN-ONLY=$human ====="
if ($fail -gt 0) { Write-Output 'FAIL present => the words "done / delivered / verified" are forbidden' }
exit $(if ($fail -gt 0) { 1 } else { 0 })
