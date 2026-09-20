# 本项目约定

## 工程形态

**纯单机**（用户明确指定）。因此：

- **不生成 `server/`**，不写任何服务端代码；
- 初始化只走 `Game.Launch(config)` + `CloverRes.Init(root)` + `CloverInput.Init()`，
  **不调 `CloverNet.Init`**、不查服务器环境；
- 交付说明首行标注「单机版（用户指定，不含服务端）」。

## 消息号段分配

**本项目不用消息号**（单机无网络）。此节保留占位：若将来加联机，按全局 skill 的分段重新分配。

## 命名

| 对象 | 约定 | 例 |
| --- | --- | --- |
| 程序集 | `SuperMario` / `SuperMario.Editor` / `SuperMario.Tests.PlayMode` | — |
| 命名空间 | `SuperMario.{层}`（`Core` / `Def` / `Module.X` / `UI` / `App`） | `SuperMario.Module.Player` |
| 模块门面 | `I{名}` 接口 + `{名}Module` 实现，实现 **internal** | `IPlayer` / `PlayerModule` |
| 面板 | `{名}Panel`，**一个文件一个类** | `HudPanel.cs` |
| 常量类 | 静态类 + `const`，**不写裸字面量** | `GameConst.TileSize` |
| 事件名 | 常量放 `Events`，命名法 `域.动作` | `Events.HudDirty` |
| 日志 | `Game.Logger.Info/Warn/Error(tag, msg)`，tag 用模块名 | `Game.Logger.Info("Level", ...)` |

## 目录边界

```
client/Assets/
├── Scripts/
│   ├── SuperMario.asmdef
│   ├── Def/          # 枚举与纯数据定义（无逻辑、无 Unity 依赖）
│   ├── Core/         # 常量 / 事件 / 场景名 / 资源路径（全项目唯一来源）
│   ├── Module/
│   │   ├── Level/    # 关卡数据解析 + 构建 + 碰撞查询
│   │   ├── Player/   # 玩家物理与表现
│   │   ├── Entities/ # 敌人 / 方块 / 道具 / 火球
│   │   ├── Gameplay/ # 编排层：跨模块判定都放这里
│   │   ├── Camera/   # 相机
│   │   ├── Score/    # 分数 / 金币 / 命数 / 时间
│   │   ├── Audio/    # 音频门面
│   │   └── Flow/     # 流程状态机 + 关卡会话 + 全局只读视图
│   ├── UI/           # 面板（一个文件一个 MonoBehaviour）
│   └── App/          # Bootstrap（全项目唯一手动挂载的脚本）
├── Editor/           # 编辑器工具（导入配置 / 工程生成器 / 冒烟自审）
├── Tests/PlayMode/   # 端到端冒烟测试
└── Resources/        # Sprites / Sound / Fonts / Levels / UI
```

**硬性**：

- **`Assets/Scripts/` 之外不许放业务代码**；`Resources/` 只放资源 + 关卡数据 + 面板预制体。
- **跨模块判定只许写在 `Module/Gameplay/`**。玩家/敌人/方块各自**不许**反向引用另外两个 ——
  踩敌人要同时用到玩家速度、敌人状态、分数、音效、玩家反弹，塞进任何一边都会造成循环依赖。
- **`App/` 只有 `Bootstrap` 一个文件，且 ≤ 200 行**。它只做启动编排，不写业务。

## 坐标与轴心（本项目最容易错的地方）

### 世界坐标

| 对象 | 约定 |
| --- | --- |
| **瓦片 (X, Y)** | 占世界矩形 `[X, X+1] × [Y, Y+1]`，中心在 `(X+0.5, Y+0.5)` |
| **角色（玩家 / 敌人 / 道具）** | `transform.position` = **脚底中心**，不是包围盒中心 |
| 关卡文件里的实体坐标 | 是**格中心**（`80.5` = 第 80 格中心），落在第 80 格 |

**为什么角色用脚底**：精灵是**紧裁剪**的（小马里奥 15x17、大马里奥 18x34 不等），
只有"脚底对齐"才能让所有帧站在同一高度上；用中心对齐会出现跑步时上下抖一像素。

### 精灵轴心

由 `SpriteImportPostprocessor` 按目录自动设：

| 目录 | 轴心 |
| --- | --- |
| `Sprites/Mario/`、`Sprites/Enemies/`、`Sprites/Items/` | **底部居中**（0.5, 0） |
| `Sprites/Tiles/`、`Sprites/Scenery/`、`Sprites/Particles/`、`Sprites/Flagpole/`、`Sprites/Castle/` | 居中 |

**新增精灵目录时必须同步改这个后处理器**，否则新素材的轴心是错的（表现是浮空或陷地）。

### 像素基准

`spritePixelsPerUnit = 16`（16 像素 = 1 世界单位 = 1 个瓦片）。
**换素材时不能改这个值** —— 改了整个世界的尺度都会变。

## 关卡数据格式

`client/Assets/Resources/Levels/World1-1.txt`（纯文本，运行期解析）：

```
# 注释
T <x> <y> <layer> <spriteName>     # 瓦片；layer 0=地形(实心)，1=背景(穿透)
E <x> <y> <prefabName>             # 实体；坐标是格中心
```

- 数据来源：从原版 Unity 工程的 `.unity` 场景里**解出来的**（`m_Tiles` + `m_TileSpriteArray`），
  不是照图描的 —— 所以它可复查、可重跑。
- **`WorldTileSprites_*` 一律实心；`SceneryTileSprites_*` 一律背景**。
  碰撞就按这个前缀判定（`LevelModule.Build` 里写死的前提）。
- ⚠️ **导出脚本已不在工程里**：原 `_smb_work/`（`level.ps1` / `make-level.ps1`）在当前工程中**找不到**
  （全局 grep 0 命中）。⇒ 目前 `World1-1.txt` / `World1-2.txt` **无法重跑复现**，只能读现成数据。
  需要"可重跑"时必须先补回导出脚本（属缺规则/缺资产，已在体检报告中登记，⛔ 不许凭记忆重写一份）。

## 原版素材与资源闸门

- **素材来源唯一**：所有下载 / 解包 / 切片的原版素材只放 `<仓库根>/原版资源/`（已在 `.gitignore`；含 `清单.md`）。
  进工程**只复制被真正引用的那几个**：`Resources` 下每个资源文件都要能指到"谁引用它"
  （`Core/ResPaths.cs` 常量 / `Resources/Levels/*.txt` / 预制体字段）。⛔ **禁止整表 / 整包全量搬进 `Assets/**`**。
- **闸门**：`tools\check-assets.ps1`（本仓库实测 `159 files, 0 UNREFERENCED` ⇒ PASS）。
  有未引用文件 ⇒ **FAIL**；存量清理看数用 `-Warn`。
- **代价留痕（2026-09-20）**：原版素材表曾整片切片进 `Sprites/**` ⇒ `Resources` 里 953 个资源文件里
  **795 个无任何引用**（Items 95% / Enemies 88% / Tiles 80% / Scenery 72%），
  `smb1_misc_sprites_0..548` 全在而代码只用 **8 个**。
  未引用部分已移到 **`.ai-tmp/unused-resources/`**（按原路径保留，可整目录搬回；该目录已被 gitignore）。
- **提交体积**：`client/Library` 曾 **1769 MB**（Unity 导入缓存）+ `Logs` 17.9 MB + `Temp` 3.1 MB ⇒
  顶层 `.gitignore` 已忽略 `Library/ Temp/ obj/ Logs/ Build(s)/ UserSettings/ setting/ *.csproj *.sln` 等；
  **按忽略后估算提交体量 ≈ 23 MB**（Assets ~21 MB 含 Screenshots 6.4 MB / 策划 1.7 MB）。
- **要随交付给出原版素材**（**仅用户本轮明说时**）：`tools\pack-original-assets.ps1` → 仓库根 `原版资源.zip`
  （可复现：条目按路径排序 + 固定时间戳 ⇒ 同内容 sha256 相同、且脚本会跳过 `.git/Library/Temp/obj`），
  再按 `.gitattributes` 里的 `*.zip filter=lfs` 走 git-lfs 提交。
  ⛔ zip **只放仓库根**，不许进 `client/Assets/**`。出处 = 全局 skill `reference/asset-sources.md` §8.2 / `rules-full.md` §1.9-9。

## 与全局 skill 的差异

| 项 | 全局 skill 写法 | 本项目写法 | 原因 |
| --- | --- | --- | --- |
| 服务端 | 默认按联服处理 | **完全没有** | 用户明确指定单机 |
| 数据配表 | 同质化数据一律走配表 | **关卡走 `Resources/Levels/*.txt`，物理常量走 `GameConst`** | 关卡是**稀疏结构数据**不是同质化表；物理常量是手感参数不随版本增长 |
| 面板内容 | 预制体里拖好 | **预制体只是空壳，内容在代码里搭**（`UIBuilder`） | 复刻 NES 画面全是纯色块 + 像素字 + 精确对齐，代码里能用常量约束坐标 |
