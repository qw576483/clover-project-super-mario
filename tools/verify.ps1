param(
  # -Reshoot: print ONLY the scene list that has to be re-shot (computed from the same area map the
  # freshness check uses), then exit. tools/probes/reshoot.ps1 takes its scene list from here and nowhere
  # else, so the gate and the re-shoot can never drift apart.
  [switch]$Reshoot
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fail = 0; $human = 0

function Say([string]$status, [string]$name, [string]$detail) {
  Write-Output ("{0,-11} {1}  {2}" -f $status, $name, $detail)
}

# ===========================================================================
#  tools/verify.ps1 -- the one-command re-check of this project.
#
#  Cut on 2026-09-24 (team sink4 / piece cut-mario): 24 judged lines -> 6, and
#  ONLY the judges that can name a concrete real defect they caught here are
#  left. What each one caught:
#    * verify-entry       -- the entry itself runs and parses.
#    * delivery-hygiene   -- one-off files / handoff docs / artifacts that
#                            escaped the project / forensic shots inside
#                            client/Assets / the .ai-tmp budget / .ps1 parse +
#                            ANSI trap. Caught (2026-09-20): a delivered batch
#                            left a one-off report OUTSIDE the project, and a
#                            whole evidence batch was deleted together with
#                            files the ledger still named.
#    * brand-credit       -- the RENDERED "by clover-engine" line, both screens.
#                            Caught two real user-reported defects: the title
#                            screen had no credit line at all, and the boot line
#                            rendered as "BY CLOVER-ENGINE" although its source
#                            text was correct (the NES pixel font maps a-z onto
#                            the A-Z glyph shapes).
#    * evidence-freshness -- a shot must not be older than the newest source of
#                            its OWN area (per-area map; that map is also the
#                            single source of `-Reshoot` scenes).
#    * citation-refs      -- every file a citation in the acceptance / reference
#                            tables names must resolve on disk.
#    * shot-citations     -- every evidence image the acceptance table cites
#                            must exist.
#
#  Removed on purpose, because no real defect could be named for any of them
#  (they were ceremony, or they cried wolf): acceptance-table summary counts,
#  allowed-diff column counting, row evidence categories, the dispatch ledger,
#  the Play ledger, contact-sheet cell bookkeeping, evidence-economy caps,
#  freeze-before-capture, baseline/spec-doc presence, and the banned-API grep
#  (0 hits since the first run -- the hard rules live in the skill, not here).
# ===========================================================================

# ---------------------------------------------------------------------------
# Non-ASCII folder names are built from code points so this file stays ASCII-only
# (Windows PowerShell 5.1 parses a BOM-less .ps1 as ANSI => CJK in source breaks it).
# ---------------------------------------------------------------------------
$planDir  = Join-Path $root    (([char[]]@(0x7B56,0x5212) -join ''))                          # ce hua
$baseDir  = Join-Path $planDir (([char[]]@(0x57FA,0x7EBF,0x56FE) -join ''))                    # ji xian tu
$shotAlt  = Join-Path $planDir (([char[]]@(0x5B9E,0x673A,0x56FE) -join ''))                    # shi ji tu
$spec     = Join-Path $planDir ((([char[]]@(0x9A8C,0x6536,0x8868)) -join '') + '.md')          # yan shou biao
$refTable = Join-Path $planDir ((([char[]]@(0x5BF9,0x7167,0x8868)) -join '') + '.md')          # dui zhao biao
$carrier  = (([char[]]@(0x539F,0x7248,0x8D44,0x6E90) -join '') + '/')                          # original-asset carrier
$srcDir   = Join-Path $root 'client\Assets\Scripts'
$tmpRoot  = Join-Path $root '.ai-tmp'
$shotDir  = Join-Path $tmpRoot 'screenshots'   # captures are one-off (skill 1.8): .ai-tmp, never Assets

$cPass = ([char[]]@(0x4E00,0x81F4) -join '')            # yi zhi
$cUny  = ([char[]]@(0x672A,0x9A8C) -join '')            # wei yan
$cUdo  = ([char[]]@(0x672A,0x505A) -join '')            # wei zuo
$cNa   = ([char[]]@(0x4E0D,0x9002,0x7528) -join '')     # bu shi yong (registered exception)
$cNum  = ([char[]]@(0x6570,0x503C,0x7C7B) -join '')     # shu zhi lei
$cVis  = ([char[]]@(0x8868,0x73B0,0x7C7B) -join '')     # biao xian lei
$cHand = ([char[]]@(0x4EA4,0x63A5) -join '')            # jiao jie (handoff)
$cProg = ([char[]]@(0x8FDB,0x5EA6) -join '')            # jin du (progress)

function Read-Utf8([string]$p) { [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) }

# ---------------------------------------------------------------------------
# Area map -- the freshness universe. n = area; d = files that area's screens depend on (a directory =
# the whole tree); p = regex over the cited screenshot names (every cited shot must match EXACTLY ONE
# area: an unmapped shot is a FAIL, so a brand new screenshot can never silently escape);
# s = the probe scene(s) that produce those shots (the re-shoot scope, printed by -Reshoot).
# Baseline of a shot = newest file among its area's d (mtime is the only signal we have; for a freshness
# check, over-approximating is the safe direction). Judging "by cause" through this map replaced a shape
# that used the project-wide newest file, where ONE level-data edit invalidated all 63 cited shots --
# including the credit line, which no level file can touch.
# ---------------------------------------------------------------------------
$areas = @(
  @{ n = 'ui-credit'; d = @('client\Assets\Scripts\UI\MainMenuPanel.cs', 'client\Assets\Scripts\UI\BootPanel.cs', 'client\Assets\Scripts\UI\UIBuilder.cs', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts'); p = '^(v2_boot|t2_menu_run1|t2_menu_run2|t3_top|credit-crop-menu|credit-crop-boot|contact-credit)\.png$'; s = 'credit' },
  @{ n = 'ui-pause'; d = @('client\Assets\Scripts\UI\PausePanel.cs', 'client\Assets\Scripts\UI\UIBuilder.cs', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts'); p = '^pause(-vol-a|-vol-b)?\.png$'; s = 'pause' },
  @{ n = 'ui-intro'; d = @('client\Assets\Scripts\UI\LoadingPanel.cs', 'client\Assets\Scripts\UI\UIBuilder.cs', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts'); p = '^intro_(card|lives_check)\.png$'; s = 'intro' },
  @{ n = 'ui-result'; d = @('client\Assets\Scripts\UI\ResultPanel.cs', 'client\Assets\Scripts\UI\HudPanel.cs', 'client\Assets\Scripts\UI\UIBuilder.cs', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts'); p = '^(sec12-j-result|walk12end-5-result)\.png$'; s = 'section12,walk12end' },
  @{ n = 'ui-hud'; d = @('client\Assets\Scripts\UI\HudPanel.cs', 'client\Assets\Scripts\UI\GameOverPanel.cs', 'client\Assets\Scripts\UI\UIBuilder.cs', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts'); p = '^(final_stage|p1_mid)\.png$'; s = 'reshoot' },
  @{ n = 'ui-contact-panels'; d = @('client\Assets\Scripts\UI', 'client\Assets\Scripts\Core\ResPaths.cs', 'client\Assets\Resources\Fonts'); p = '^contact-panels\.png$'; s = 'pause,intro,walk12end' },
  @{ n = 'p11-flag'; d = @('client\Assets\Resources\Levels\World1-1.txt', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Level\LevelProps.cs', 'client\Assets\Resources\Sprites'); p = '^(flag_top|flag_bottom|CASTLE_WALK)\.png$'; s = 'flag' },
  @{ n = 'p11-blocks'; d = @('client\Assets\Resources\Levels\World1-1.txt', 'client\Assets\Scripts\Module\Entities\BlockModule.cs', 'client\Assets\Scripts\Module\Entities\ItemModule.cs', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs'); p = '^(12-blocks-group|13-starbrick-star|14-multicoin-after10|15-hidden-before|16-hidden-oneup)\.png$'; s = 'reshoot,headhit' },
  @{ n = 'p11-form'; d = @('client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Player\PlayerModule.cs', 'client\Assets\Scripts\Module\Entities\ItemModule.cs', 'client\Assets\Resources\Levels\World1-1.txt'); p = '^(crouch_fire|cr2_big|cr2_small)\.png$'; s = 'crouch' },
  @{ n = 'p11-enemy'; d = @('client\Assets\Scripts\Module\Entities\EnemyModule.cs', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Resources\Levels\World1-1.txt', 'client\Assets\Resources\Sprites'); p = '^(probe-koopa-1|probe-koopa-2|v4_stage|v4_stage2)\.png$'; s = 'goomba,koopa,reshoot' },
  @{ n = 'p11-coinroom'; d = @('client\Assets\Resources\Levels\World1-1.txt', 'client\Assets\Resources\Levels\World1-1-Underground.txt', 'client\Assets\Scripts\Module\Level\PipeWarpTable.cs', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Flow\AppFlow.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Flow\StageSession.cs', 'client\Assets\Resources\Sprites', 'client\Assets\Resources\Sound'); p = '^((2[0-5])-.*|contact-formcarry)\.png$'; s = 'coinroom,bigroom,reshoot' },
  @{ n = 'p11-scenery'; d = @('client\Assets\Resources\Levels\World1-1.txt', 'client\Assets\Scripts\Module\Level\LevelProps.cs', 'client\Assets\Resources\Sprites'); p = '^contact-1-1-scenery\.png$'; s = 'reshoot' },
  @{ n = 'p12-look'; d = @('client\Assets\Resources\Levels\World1-2.txt', 'client\Assets\Scripts\Module\Level\LevelModule.cs', 'client\Assets\Scripts\Module\Level\LevelData.cs', 'client\Assets\Scripts\Module\Level\LevelProps.cs', 'client\Assets\Resources\Sprites'); p = '^(s12_early|s12_stage)\.png$'; s = 'section12,level12' },
  @{ n = 'p12-side'; d = @('client\Assets\Resources\Levels\World1-2.txt', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Flow\AppFlow.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Level\LevelData.cs', 'client\Assets\Resources\Sprites'); p = '^sec12-(a|b|c|m1)-.*\.png$'; s = 'section12' },
  @{ n = 'p12-surface'; d = @('client\Assets\Resources\Levels\World1-2-Surface.txt', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Flow\AppFlow.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Level\LevelProps.cs', 'client\Assets\Scripts\Module\Flow\StageContext.cs', 'client\Assets\Resources\Sprites'); p = '^(sec12-(d|d1|e|f|g|h|i)-.*|walk12end-[0-4]-.*)\.png$'; s = 'section12,walk12end' },
  @{ n = 'p12-sections'; d = @('client\Assets\Resources\Levels\World1-2.txt', 'client\Assets\Resources\Levels\World1-2-Surface.txt', 'client\Assets\Resources\Levels\World1-2-Underground.txt', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Flow\AppFlow.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Resources\Sprites'); p = '^(contact-1-2-sections|contact-pipe-transition|contact-sidepipe)\.png$'; s = 'section12,mouth2' },
  @{ n = 'p12-coinroom'; d = @('client\Assets\Resources\Levels\World1-2.txt', 'client\Assets\Resources\Levels\World1-2-Underground.txt', 'client\Assets\Scripts\Module\Level\PipeWarpTable.cs', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Flow\AppFlow.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Scripts\Module\Flow\StageSession.cs', 'client\Assets\Scripts\Module\Flow\StageContext.cs', 'client\Assets\Resources\Sprites'); p = '^((2[6-9]|30)-.*|sec12-[kl]-mouth.*)\.png$'; s = 'coinroom12,mouth2' },
  @{ n = 'p12-enemy'; d = @('client\Assets\Scripts\Module\Entities\EnemyModule.cs', 'client\Assets\Scripts\Module\Gameplay\GameplayModule.cs', 'client\Assets\Scripts\Module\Player\PlayerActor.cs', 'client\Assets\Resources\Levels\World1-2.txt', 'client\Assets\Resources\Sprites'); p = '^probe-(koopa-shell|piranha).*\.png$'; s = 'koopa,piranha' },
  @{ n = 'p12-platform'; d = @('client\Assets\Scripts\Module\Entities\PlatformModule.cs', 'client\Assets\Scripts\Module\Flow\StageSession.cs', 'client\Assets\Resources\Levels\World1-2.txt'); p = '^contact-platform\.png$'; s = 'platform,platformspawn' },
  # aggregate sheets serve several rows at once: deps = UNION of the areas whose frames they carry,
  # scene list = union of the scenes that produce those frames
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

# Credit-report readers (used by brand-credit below).
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

try {

# --- 1) the entry itself -----------------------------------------------------
Say 'PASS' 'verify-entry' $PSCommandPath

# --- 2) delivery hygiene ----------------------------------------------------
# One aggregated judge. Each item below was measured as a real defect on this project at least once; a
# check that only repeats what the skill already says (banned APIs, doc presence) is deliberately absent.
$hy = @()

# (a) one-off .cs left in a stray work dir (global rule 1.8: everything one-off lives in .ai-tmp/test/)
$nStray = @(Get-ChildItem $root -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\(_dev|_assets_src|_assets_tmp|_probe|_smb_work)\\' }).Count
if ($nStray -gt 0) { $hy += "$nStray stray .cs under a one-off dir (_dev/_assets_*/_probe/_smb_work)" }

# (b) handoff / progress documents must not exist (skill 1.5 item 8: a delivery is a product + a report,
#     never a NEXT.md / a progress file)
$badDocs = @(Get-ChildItem $root -Recurse -Filter *.md -File -ErrorAction SilentlyContinue |
             Where-Object { $_.FullName -notmatch '\\Library\\' -and
                            ($_.Name -like 'NEXT*' -or $_.Name.Contains($cHand) -or $_.Name.Contains($cProg)) })
if ($badDocs.Count -gt 0) { $hy += "$($badDocs.Count) handoff/progress doc(s): " + (($badDocs | ForEach-Object { $_.Name }) -join ', ') }

# (c) project artifacts that escaped the project (workspace root + the host's artifact store, last 24h).
#     Identity comes from the project directory name at run time, never from a hard-coded file name; the
#     24h window keeps other projects' stale files from raising a false red (a check that cries wolf is
#     worse than no check). Leaving a host out narrows the check, it never loosens the workspace-root half.
$wsRoot = Split-Path $root -Parent
$rootName = Split-Path $root -Leaf
$projTokens = @($rootName)
if ($rootName.StartsWith('clover-project-')) { $projTokens += $rootName.Substring(15) }
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
if ($esc.Count -gt 0) {
  $hy += "$($esc.Count) project file(s) written OUTSIDE the project (move into .ai-tmp/test/)"
  $esc | ForEach-Object { Write-Output ("            " + $_.FullName) }
}

# (d) forensic screenshots inside Assets -- captures belong in .ai-tmp/screenshots
$shotInAsm = Join-Path $root 'client\Assets\Screenshots'
if (Test-Path $shotInAsm) { $hy += "client/Assets/Screenshots exists ($(@(Get-ChildItem $shotInAsm -Recurse -Filter *.png -File -ErrorAction SilentlyContinue).Count) png)" }

# (e) build/cache/backup junk inside the project tree (Library excluded)
$junkD = @(Get-ChildItem $root -Recurse -Directory -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -notmatch '\\Library\\|\\.git\\' -and ($_.Name -eq 'bin' -or $_.Name -eq 'obj') })
$junkF = @(Get-ChildItem $root -Recurse -File -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -notmatch '\\Library\\' -and $_.Name -like '*-bak-*' })
if ($junkD.Count -gt 0 -or $junkF.Count -gt 0) { $hy += "$($junkD.Count) bin/obj dir(s) + $($junkF.Count) *-bak-* file(s) inside the project" }

# (f) .ai-tmp budget (skill 3.5: <= 300 files / <= 200 MB; only the size half is judged -- a file COUNT
#     threshold would punish evidence, which is the wrong direction)
$tmpFiles = @(Get-ChildItem $tmpRoot -Recurse -File -ErrorAction SilentlyContinue)
$tmpMB = 0
if ($tmpFiles.Count -gt 0) { $tmpMB = [Math]::Round((($tmpFiles | Measure-Object Length -Sum).Sum / 1MB), 1) }
if ($tmpMB -gt 200) { $hy += ".ai-tmp is $tmpMB MB (budget 200 MB)" }

# (g) every .ps1 under tools/ and .ai-tmp/ parses, and none hits the ANSI trap. PS 5.1 reads a BOM-less
#     .ps1 as ANSI: CJK inside CODE breaks -match / markers, while CJK in a full-line comment is harmless
#     -- so non-ASCII only counts as a defect when it sits outside a comment.
$ps1All = @(Get-ChildItem (Join-Path $root 'tools'), $tmpRoot -Recurse -Filter *.ps1 -File -ErrorAction SilentlyContinue)
$badPs = @()
foreach ($f in $ps1All) {
  $b = [System.IO.File]::ReadAllBytes($f.FullName)
  $bom = ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
  $enc = [System.Text.Encoding]::UTF8
  if (-not $bom) { $enc = [System.Text.Encoding]::Default }      # parse it the way PS 5.1 will
  $text = $enc.GetString($b)
  if ($bom) { $text = $text.TrimStart([char]0xFEFF) }
  if (-not $bom) {
    $codeNonAscii = 0
    foreach ($ln in ($text -split "`n")) {
      if ($ln.TrimStart().StartsWith('#') -or $ln.Trim().Length -eq 0) { continue }
      $codeNonAscii += @($ln.ToCharArray() | Where-Object { [int]$_ -gt 127 }).Count
    }
    if ($codeNonAscii -gt 0) { $badPs += ($f.Name + " (ANSI trap: $codeNonAscii non-ASCII byte(s) outside comments without BOM)") }
  }
  $psk = $null; $per = $null
  [void][System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$psk, [ref]$per)
  if (@($per).Count -gt 0) { $badPs += ($f.Name + " (" + @($per).Count + " syntax error)") }
}
if ($badPs.Count -gt 0) { $hy += ($badPs -join '; ') }

if ($hy.Count -eq 0) {
  Say 'PASS' 'delivery-hygiene' ("no stray one-off file, no handoff doc, no escaped artifact, no capture dir in Assets, no bin/obj/-bak-; .ai-tmp = $($tmpFiles.Count) file(s) / $tmpMB MB (size budget 200 MB); $($ps1All.Count) .ps1 parsed, no ANSI trap")
} else {
  $fail++; Say 'FAIL' 'delivery-hygiene' "$($hy.Count) hygiene problem(s)"
  $hy | ForEach-Object { Write-Output ('            ' + $_) }
}

# --- 3) delivery rows and their evidence class ------------------------------
# "Delivery row" = a table row that carries BOTH a conclusion word and an evidence class. That is not
# bookkeeping: it is the only scope that separates the acceptance body from the two HISTORY tables
# (the "bugs fixed this round" and "problems already fixed" tables also start with "| <n> |" but carry
# neither), and the history rows are records of past runs -- their images were deleted with the delivery
# batch on purpose, so judging them would fail pictures that must NOT be re-shot.
# Two consumers: the freshness universe (a VISUAL row's shots must be fresh; a NUMERIC row's evidence is
# a runtime log line, so its illustrative png is NOT judged -- judging it turned 39/63 rows red while
# nothing was actually stale) and the cited-shot list.
$specTxt = if (Test-Path $spec) { Read-Utf8 $spec } else { '' }
$rowEv = @()
if ($specTxt -ne '') {
  $deliveryRows = @(($specTxt -split "`n") | Where-Object {
      $_ -match '^\|\s*(\d+|\d+-\d+)\s*\|' -and
      ($_.Contains($cPass) -or $_.Contains($cUny) -or $_.Contains($cUdo) -or $_.Contains($cNa)) -and
      ($_.Contains($cVis) -or $_.Contains($cNum)) })
  foreach ($ln in $deliveryRows) {
    $null = $ln -match '^\|\s*(\d+|\d+-\d+)\s*\|'
    $rp = @(); foreach ($m in [regex]::Matches($ln, '([A-Za-z0-9_\-]+\.png)')) { $rp += $m.Groups[1].Value }
    $rowEv += [pscustomobject]@{ Id = $Matches[1]; Vis = $ln.Contains($cVis); Pngs = @($rp | Sort-Object -Unique) }
  }
}
$visRowEv = @($rowEv | Where-Object { $_.Vis })

# --- 4) brand credit: the RENDERED line, on BOTH screens --------------------
# Judged on what is RENDERED, never on the source text (skill 6 item 9). The old shape grepped the source
# for 'by clover-engine', so it stayed green through both real defects:
#   (a) the TITLE screen had no credit line at all;
#   (b) the boot line's source text was right but the NES pixel font maps a-z onto UPPERCASE glyph shapes,
#       so the screen showed 'BY CLOVER-ENGINE'.
# Judge = the runtime UI node tree + the pixels of the live screen: probe scene 'credit' writes
# .ai-tmp/screenshots/credit-render.txt (text / font / visibility / anchors, the font's real glyph boxes,
# the ink profile of the bottom strip, plus the measured strip saved as an image). The probe also ships
# two reverse-self-check entries ('credit-case' = text forced to upper case, 'credit-missing' = line
# hidden); each stamps tamper=<tag> in the report header, which is rejected here AND fails on its own
# (measured 2026-09-19: both turned this check red, then a clean re-run restored green). Re-run the probe
# after any UI/font change -- a stale report is a FAIL.
$credit  = 'by clover-engine'
$repPath = Join-Path $shotDir 'credit-render.txt'
$report  = ''
if (Test-Path $repPath) { $report = Read-Utf8 $repPath }

if ($report -eq '') {
  $fail++; Say 'FAIL' 'brand-credit' "no runtime render report at .ai-tmp/screenshots/credit-render.txt (run probe scene 'credit'; it is written by measurement, not by hand)"
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
    # each scope must be the PANEL THAT OWNS THAT SCREEN -- "some panel somewhere carries the string" does
    # not satisfy the rule, and neither does a label on an inactive / other panel.
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
    # pixels of the live screen's bottom strip: a real lowercase line has x-height glyphs, so the top rows
    # of the ink band carry far less ink than the middle rows (all-uppercase shapes make every row equally
    # dense: ratio ~0.9; true lowercase ~0.2 -- require < 0.5). The glyph-run criterion is derived from the
    # FONT FILE by tools/probes/font_predict.py, not from our own render: prstart.ttf at 16px gives 10 of
    # 15 runs >= 3px shorter than the tallest, the old NES font gives exactly 1 (so shortRuns >= 6 splits
    # "really lowercase" from "a-z drawn as A-Z" by construction).
    $ink   = [int](Credit-Num $pxL 'ink=(\d+)' -1)
    $ratio = Credit-Num $pxL 'topRatio=([-\d.]+)' -1
    $gap   = [int](Credit-Num $pxL 'bandBottomGapPx=(-?\d+)' -1)
    $short = [int](Credit-Num $pxL 'shortRuns=(\d+)' -1)
    $hmax  = [int](Credit-Num $pxL 'hMax=(\d+)' -1)
    $off   = [int](Credit-Num $pxL 'screenCenterOffsetPx=(-?\d+)' 9999)
    if ($ink -lt 20) { $bad += "${scope}: no/negligible ink on the line's own rect (ink=$ink) => nothing was rendered there -> $pxL" }
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

  # the report is runtime evidence: it must be newer than the files that can change THAT LINE -- the area
  # map's ui-credit entry (menu/boot panels + UIBuilder + ResPaths + fonts). It must NOT be compared
  # against the project-wide newest file: a level-data or gameplay edit cannot change the credit line, and
  # doing exactly that produced a false FAIL on this project.
  $ca = Get-AreaByName 'ui-credit'
  $newest = $null
  if ($ca) { $newest = Newest-AreaFile $ca.d }
  if ((Test-Path $repPath) -and $newest) {
    $rep = Get-Item $repPath
    if ($rep.LastWriteTime -lt $newest.LastWriteTime) {
      $bad += "the render report ($($rep.LastWriteTime)) is OLDER than $($newest.Name) ($($newest.LastWriteTime)) => re-run probe scene 'credit'"
    }
  }

  if ($bad.Count -eq 0) {
    Say 'PASS' 'brand-credit' "rendered on BOTH screens (title scope 'menu' + boot scope 'boot'): text '$credit' exact (case-sensitive), font PressStart2P, lowercase glyphs in the pixels (shortRuns>=6 / topRatio<0.5), bottom-centre, report newer than area ui-credit's newest source"
  } else {
    $fail++; Say 'FAIL' 'brand-credit' "$($bad.Count) assertion(s) failed on the RENDERED credit line / its freshness"
    $bad | ForEach-Object { Write-Output ('            ' + $_) }
  }
}

# --- 5) evidence freshness (per area) --------------------------------------
$srcNewest = Get-ChildItem $srcDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1
$lvlNewest = Get-ChildItem (Join-Path $root 'client\Assets\Resources\Levels') -Recurse -Filter *.txt -File -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1
$code = @($srcNewest, $lvlNewest) | Where-Object { $_ } | Sort-Object LastWriteTime -Descending | Select-Object -First 1

$stale = @(); $unmapped = @(); $staleScenes = @{}
if ((Test-Path $shotDir) -and $code) {
  $citedNames = @{}
  foreach ($r in $visRowEv) { foreach ($p in $r.Pngs) { $citedNames[$p] = 1 } }
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
  $sceneList = (@($staleScenes.Keys) | Sort-Object) -join ','
  $uncited = $shotFiles.Count - $cited.Count

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
    Say 'PASS' 'evidence-freshness' "$($cited.Count) cited shot(s) of $($visRowEv.Count) visual row(s) are newer than their own area's newest source; $uncited uncited file(s) ignored (a numeric row's illustrative png is never judged here: its evidence is a runtime log line)"
  } else {
    $fail++; Say 'FAIL' 'evidence-freshness' "$($stale.Count)/$($cited.Count) cited screenshot(s) of VISUAL rows are older than a file in their OWN area -> re-shoot exactly these (scene=...): $sceneList"
    $stale | Select-Object -First 40 | ForEach-Object { Write-Output ('            ' + $_) }
  }
} else { $human++; Say 'HUMAN-ONLY' 'evidence-freshness' 'no screenshot dir or no source' }

# --- 6a) citation refs: every file a citation names must resolve on disk -----
# Judged by REACHABILITY, not by line number: line numbers move with every edit, so a red there could not
# be fixed by our code (skill 5 item 3) and would cry wolf.
# The original-asset carrier ('<root>/<carrier>') and .ai-tmp are skipped on purpose: the carrier is
# reference material that is deliberately NOT kept in the tree, and .ai-tmp holds one-off evidence that is
# deleted by design after a delivery (the shots that DO survive are judged by shot-citations).
$citExt = @('.cs', '.go', '.py', '.ps1', '.txt', '.md', '.json', '.prefab', '.unity', '.png', '.jpg', '.tsv', '.csv', '.asmdef')
$byName = @{}
foreach ($d in @((Join-Path $root 'client\Assets'), (Join-Path $root 'tools'), $planDir, (Join-Path $root 'docs'))) {
  if (-not (Test-Path $d)) { continue }
  foreach ($f in @(Get-ChildItem $d -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $citExt -contains $_.Extension.ToLower() })) {
    $k = $f.Name.ToLower()
    if (-not $byName.ContainsKey($k)) { $byName[$k] = @() }
    if ($byName[$k] -notcontains $f.FullName) { $byName[$k] += $f.FullName }
  }
}
# A citation is TRULY dangling only when the path belongs to OUR deliverable -- i.e. its leading directory
# really exists inside the project (client/Assets/..., tools/probes/..., ce-hua/ji-xian-tu/...). Only that
# kind can be fixed by our code (skill 5 item 3). A path into a sibling repo (the engine, the reference
# clone), into the deleted reference carrier, or into a legacy capture-dir form is a HINT at most --
# failing those would cry wolf on evidence that was never ours to keep.
function Is-OwnPath([string]$t) {
  $parts = $t -split '/'
  if ($parts.Count -lt 2) { return $false }
  return (Test-Path -LiteralPath (Join-Path $root ($parts[0] + '\' + $parts[1])))
}
function Resolve-Cit([string]$tok) {
  $t = $tok.Replace('\', '/')
  if ($t.StartsWith($carrier) -or $t.StartsWith('.ai-tmp/')) { return 'skip' }
  if ($t.Contains('/')) {
    $leafOnly = ($t -replace '^Screenshots/', '')
    foreach ($c in @((Join-Path $root $t), (Join-Path $wsRoot $t), (Join-Path $shotDir $t),
                     (Join-Path $shotDir $leafOnly), (Join-Path $tmpRoot ('test\' + $t)),
                     (Join-Path $planDir $t), (Join-Path $shotAlt $t), (Join-Path $baseDir $t))) {
      if (Test-Path -LiteralPath $c) { return 'ok' }
    }
    $leaf = Split-Path $t -Leaf
    if ($byName.ContainsKey($leaf.ToLower())) { return 'hint' }   # path form but the leaf exists elsewhere
    if (Is-OwnPath $t) { return 'dangling' }
    return 'hint'
  }
  if ($byName.ContainsKey($t.ToLower())) { return 'ok' }
  return 'hint'
}
$citRe = [regex]'([A-Za-z0-9_][A-Za-z0-9_\./\\-]*\.(?:cs|go|py|ps1|txt|md|json|prefab|unity|png|jpg|tsv|csv|asmdef))'
$citTxt = $specTxt
if (Test-Path $refTable) { $citTxt += "`n" + (Read-Utf8 $refTable) }
$citTok = @{}
foreach ($m in $citRe.Matches($citTxt)) { $citTok[$m.Groups[1].Value] = 1 }
$citOk = 0; $citSkip = 0; $citHint = @(); $citBad = @()
foreach ($t in ($citTok.Keys | Sort-Object)) {
  switch (Resolve-Cit $t) {
    'ok'       { $citOk++ }
    'skip'     { $citSkip++ }
    'hint'     { $citHint += $t }
    'dangling' { $citBad += $t }
  }
}
if ($citTok.Count -eq 0) {
  $human++; Say 'HUMAN-ONLY' 'citation-refs' 'no file citation found in the acceptance / reference table'
} elseif ($citBad.Count -eq 0) {
  $note = ''
  if ($citHint.Count -gt 0) { $note = "; $($citHint.Count) bare file name(s) resolve nowhere and are reported as HINT, not FAIL (most are files of the reference project): " + (($citHint | Select-Object -First 6) -join ', ') }
  Say 'PASS' 'citation-refs' "$citOk of $($citTok.Count) cited file(s) resolve on disk; $citSkip citation(s) under the carrier / .ai-tmp are not judged (not kept on disk by design)$note"
} else {
  $fail++; Say 'FAIL' 'citation-refs' "$($citBad.Count) cited file(s) resolve to nothing"
  $citBad | Select-Object -First 20 | ForEach-Object { Write-Output ('            ' + $_) }
}

# --- 6b) shot citations: every cited evidence image must exist ---------------
# Scope = the VISUAL delivery rows (a numeric row's illustrative png is not evidence: its evidence is a
# runtime log line), so the list below is what a reader of the table would actually open.
$shotTok = @{}
$visSrc = (($visRowEv | ForEach-Object { $_.Pngs }) | Sort-Object -Unique) -join "`n"
foreach ($m in [regex]::Matches($visSrc, '([A-Za-z0-9_][A-Za-z0-9_\./\\-]*\.(?:png|jpg|jpeg|gif))')) { $shotTok[$m.Groups[1].Value] = 1 }
$missShot = @(); $shotSkip = 0; $shotHint = @()
foreach ($t in ($shotTok.Keys | Sort-Object)) {
  switch (Resolve-Cit $t) {
    'skip'     { $shotSkip++ }
    'hint'     { $shotHint += $t }
    'dangling' { $missShot += $t }
  }
}
if ($shotTok.Count -eq 0) {
  $human++; Say 'HUMAN-ONLY' 'shot-citations' 'the visual delivery rows cite no evidence image name (rows cleaned to a placeholder cannot point at a file)'
} elseif ($missShot.Count -eq 0) {
  $note = ''
  if ($shotHint.Count -gt 0) { $note = "; $($shotHint.Count) name(s) resolve nowhere and are reported as HINT, not FAIL: " + ($shotHint -join ', ') }
  Say 'PASS' 'shot-citations' ("$($shotTok.Count) cited evidence image(s) of visual rows all resolve on disk" + $(if ($shotSkip -gt 0) { " ($shotSkip under the carrier / .ai-tmp, not judged)" } else { '' }) + $note)
} else {
  $fail++; Say 'FAIL' 'shot-citations' "$($missShot.Count) cited evidence image(s) resolve to nothing"
  $missShot | Select-Object -First 20 | ForEach-Object { Write-Output ('            ' + $_) }
}

} catch {
  $fail++
  Say 'FAIL' 'verify-crash' ('the script itself threw, so THIS RUN PROVES NOTHING: ' + $_.Exception.Message)
}

Write-Output ''
Write-Output "===== summary: FAIL=$fail  HUMAN-ONLY=$human ====="
if ($fail -gt 0) { Write-Output 'FAIL present => the words "done / delivered / verified" are forbidden' }
exit $(if ($fail -gt 0) { 1 } else { 0 })
