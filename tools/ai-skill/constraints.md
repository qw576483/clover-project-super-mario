# 本项目约束与踩坑（只记本项目特有的）

> ⚠️ **本文件【不放引擎修复记录】**。引擎改动影响所有项目，记录在**引擎仓库**：
> `clover-client-unity-engine/修复记录.md`（E 编号 + 最小复现 + 自证）。
> 这里只记"本项目自己踩的坑"，以及"本项目依赖了哪个 E 修复"。

---

## #1 一个 `.cs` 里不许放多个 MonoBehaviour

预制体的 `m_Script` 会指向错的 fileID ⇒ **运行时面板打不开，且编译期不报错**。
一个类一个文件。

## #2 `AudioListener` 不许挂在场景相机上

切场景就丢 ⇒ 每帧刷屏，日志被刷到 74MB。挂在常驻物体上。

## #3 面板预制体：根节点必须铺满父层，锚点必须选中点

实测踩过两次（标题屏元素偏到左上、面板只有 100×100 缩在角落）：
- 面板预制体的**根 RectTransform** 必须 `anchorMin=0 / anchorMax=1 / pivot=0.5 / offsetMin=offsetMax=0`（铺满父层）；
- 子节点的锚点要显式选**居中**（`(0.5, 0.5)`），**不能沿用对齐方式派生的锚点** ——
  `MiddleCenter + anchoredPosition=(0,-150)` 在别的锚点下会被解释成完全不同的位置。
- 结论：**位置一律用"相对父层中心的偏移"表达**，不要靠猜。
- 注：引擎 `UIWidgets.Stretch(rt)` / `CreateCentered(...)` 就是干这个的，别自己再写一遍。

## #4 轴心 / 坐标约定（和引擎默认不同，容易搞混）

- 角色坐标 = **脚底**（不是中心）；
- 瓦片 `(X,Y)` 占世界 `[X,X+1] × [Y,Y+1]`；
- 精灵是**紧裁剪**的（尺寸不等于整格）。

## #5 死亡结算必须走 `GameplayModule` 的入口，不许在外面直接 `Player.Kill()`

死亡重来/GameOver 靠 `GameplayModule.PendingDeath` 触发；`Kill()` **不置这个标记** ⇒
表现是"画面永远卡在死的那一帧"。时间到（`Events.TimeUp`）就踩过这个坑。
统一走 `IGameplay.KillPlayerForcibly()`。

## #6 UI 面板里的"当前状态值"必须由调用方当参数传入

面板 `Awake` 里读 `StageContext.XXX` 会拿到**空值**（面板打开的时刻新会话还没 Build）⇒
静默回退到默认值。实测：死亡重来的入场卡把 `×1` 显示成 `×3`。
做法：`Game.UI.Open<LoadingPanel>(lives)` + 在 `OnOpen` 里刷 + **漏传参数时打 Warn**。

## #7 `CloseAll()` 之后**不能**用 `Transition(同一个状态)` 把面板开回来

`Game.UI.CloseAll()` 清掉面板，紧跟着 `Game.Fsm.Transition(FlowState.Menu)` —— 而 **FSM 对"切到当前所在的状态"是 no-op**。
于是"已经在 Menu 又触发一次回主菜单"的路径（连续跑场景、重复回主菜单）会变成：
**菜单场景在、面板没了**的**空白标题屏**，而且**一条日志都没有**（引擎的 `Open<T>` 在"注册表里已有该类型"时直接 `OnOpen` 返回，不新建、不报错）。

做法：统一走 `AppFlow.ShowMenuAfterSceneLoad()` —— 同状态时**自己 `Game.UI.Open<MainMenuPanel>()`** 并打一行日志。
**判据**：凡是"先清面板、再靠状态切换把界面带回来"的地方，都要先判 `Game.Fsm.Current`。

---

## 本项目依赖的引擎修复（E 编号）

| E | 内容 | 本项目受影响处 |
|---|---|---|
| **E1** | `Timer` 在 `timeScale = 0` 时永不触发 ⇒ 新增 `ITimer.AfterUnscaled` / `EveryUnscaled` | `AppFlow.EnterGameOver` 的"4 秒后回标题"必须用 `AfterUnscaled`（原来用普通 `After` ⇒ GameOver 屏永久卡死） |

细节见引擎仓库 `修复记录.md`。
