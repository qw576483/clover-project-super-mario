# tools/probes —— 判据资产（**必须提交**）

> 规则出处：skill `clover-engine` → `reference/workflow-and-standards.md`「临时文件」/ `reference/game-delivery.md` §9.5 第 7 条。
> 判据一句话：**「删掉之后，还能不能重新判定同一件事？」不能 ⇒ 它就不是一次性产物** ⇒ 必须进仓库。
> ⛔ 反面教材（本项目实测，2026-09-20）：曾把 `probe.cs`（281 KB）+ Play 驱动按"一次性产物"删掉 ⇒
> **全部复验能力归零**，最后只能从回收站捞回来。一次性产物 ≠ 一切放在 `.ai-tmp/` 的东西。

## 里面是什么

| 文件 | 用途 |
|---|---|
| `probe.cs` | 探针场景库（入口形如 `Probe.FlagCheck`）。由引擎的 `run_script` **即时编译执行**，不在 Unity 工程里 |
| `drive2.ps1` | Play 驱动：一次进 Play，按场景名跑 `Probe.<Entry>`，等"场景开始 / 场景结束"标记 |
| `run.ps1` / `reshoot.ps1` | 早期驱动 / 重拍驱动 |
| `selftest-category.ps1` | `tools/verify.ps1` 的采样器自检（含"`.ps1` 必须纯 ASCII 或带 BOM"）|
| `selftest-freshness.ps1` | 证据新鲜度自检（证据必须比被验证文件新）|
| `verify-labels.ps1` / `verify-skill-edit.ps1` | 判据 / 文档类自检 |
| `measure_hud.py` | HUD 三栏列位置量法（验收表"量法"一栏引用的就是它）|
| `shot_stats.py` / `title_hud_check.py` / `title_copyright_check.py` | 截图统计 / 标题屏 HUD / 版权行 量法 |
| `font_predict.py` | 从字体文件推导字形高度（判定"画面上的 a-z 是不是真小写"）|
| `selfcheck_delivery.py` / `doc_prune.py` | 交付自检 / 文档清理 |

## 怎么跑

```powershell
# cwd 必须在 Unity 工程目录（client/），或者每条 unity 命令都带 --project-path
cd <项目根>\client
powershell -ExecutionPolicy Bypass -File ..\tools\probes\drive2.ps1 -Scene flagcheck -SceneTimeout 150
# 或直接让引擎跑探针里的某个入口：
unity command run_script --file <项目根>\tools\probes\probe.cs --entry Probe.FlagCheck --project-path <项目根>\client
```

截图与报告落 `<项目根>/.ai-tmp/screenshots/`（**一次性**，验收后即删，不进仓库）。

## 什么**不**放这里

一次性产物（不进仓库，只放 `.ai-tmp/`）：探针跑出来的截图 / 日志 / 台账 / 中间的 `tmp_*.py`、`_*.py`、
`poster_*.py` 之类只为某一次诊断写的小脚本 —— 判据不变：**删了不影响"重新判定同一件事"**。
