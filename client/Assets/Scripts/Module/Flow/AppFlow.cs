using System;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Def;
using SuperMario.Module.CameraRig;
using SuperMario.Module.Entities;
using SuperMario.Module.Gameplay;
using SuperMario.Module.Level;
using SuperMario.Module.Player;
using SuperMario.Module.Score;
using SuperMario.UI;
using UnityEngine;

namespace SuperMario.Module.Flow
{
    /// <summary>流程状态名。</summary>
    public static class FlowState
    {
        public const string Boot = "Boot";
        public const string Menu = "Menu";
        public const string CharSelect = "CharSelect";
        public const string Loading = "Loading";
        public const string Stage = "Stage";
        public const string Pause = "Pause";
        public const string Result = "Result";
        public const string GameOver = "GameOver";
    }

    /// <summary>
    /// 主流程编排。
    /// <para>
    /// 全项目**只有这里**调 <c>Game.Scene.Load</c> 与 <c>Game.UI.Open/CloseAll</c>。
    /// 面板之间不互相跳转（按钮只发事件，由这里决定去哪），这样流程就是一张可读的状态图，
    /// 而不是散在各面板里的跳转链 —— 后者一旦加一个"设置"页就会变成环，很难推理。
    /// </para>
    /// </summary>
    public sealed class AppFlow
    {
        private StageSession _session;

        /// <summary>
        /// 金币房（管中密室）会话。null = 当前在主关卡。
        /// <para>
        /// 同一时刻**只有一局在跑**：进密室时主关卡会话只冻结不销毁（<c>StageSession.Hide</c>），
        /// 由本字段区分"现在 Tick 谁"。
        /// </para>
        /// </summary>
        private StageSession _bonusSession;

        /// <summary>当前在跑的那一局（密室优先）。</summary>
        private StageSession ActiveSession => _bonusSession ?? _session;

        /// <summary>管中过场的剩余等待（秒，&lt;0 = 没有过场）；见 <see cref="WarpTransitionTimeout"/>。</summary>
        private float _warpWait = -1f;

        /// <summary>这次管中过场做完之后流程要接着干什么（见 <see cref="WarpPhase"/>）。</summary>
        private WarpPhase _warpPhase = WarpPhase.None;

        /// <summary>
        /// 管中过场的四种用途。它们共用同一个等待循环（<see cref="_warpWait"/> 计时 + <c>Player.Busy</c> 结束），
        /// 因为四者的"结束信号"完全一样（玩家的管中动作播完），**只有下一步不同** ——
        /// </summary>
        private enum WarpPhase
        {
            /// <summary>不在过场里。</summary>
            None,
            /// <summary>进密室：下沉浸完 → 建密室会话（<see cref="BeginBonusRoom"/>）。</summary>
            IntoRoom,
            /// <summary>出密室：侧向走进管口 → 销毁密室、回主关卡并从出场管升起。</summary>
            OutOfRoom,
            /// <summary>1-2 地下段：走进侧向管口 → 切到地表段。</summary>
            NextSection,
            /// <summary>只等动作播完（1-2 地表段开局的"从出管口升起"），完了接着正常 Tick。</summary>
            RiseOnly,
        }

        /// <summary>
        /// 管中过场的兜底超时（秒）。正常情况由 <c>Player.Busy</c> 变 false 结束
        /// （沉进管子 / 顶出管子 / 走进管口都是同一个信号），这个超时只是防止
        /// "玩家被销毁 / 状态没复位"时流程永久卡在过场里（卡住且无日志最难查）。
        /// </summary>
        private const float WarpTransitionTimeout = 3f;

        /// <summary>玩家槽位：双人模式各有各的分数 / 命数 / 形态。</summary>
        private sealed class Slot
        {
            public int Points;
            public int Coins;
            public int Lives = GameConst.StartLives;
            public PowerState Power = PowerState.Small;
        }

        private readonly Slot[] _slots = { new Slot(), new Slot() };
        private int _currentPlayer;
        private int _playerCount = 1;
        private float _deathTimer = -1f;
        private float _resultTimer = -1f;

        // ───────────────────────── 安装 ─────────────────────────

        public void Install()
        {
            var fsm = Game.Fsm;

            fsm.RegisterState(FlowState.Boot, EnterBoot, null, null);
            fsm.RegisterState(FlowState.Menu, EnterMenu, null, null);
            fsm.RegisterState(FlowState.CharSelect, EnterCharSelect, null, null);
            // Loading 的 OnExit 不是可选项：它在这一状态里把 timeScale 冻成 0（见 EnterLoading），
            // 离开时必须解冻，否则世界会一直停在"冻住"的状态（且 TickStage 照跑 ⇒ 人能动、敌人不动）。
            fsm.RegisterState(FlowState.Loading, EnterLoading, null, ExitLoading);
            fsm.RegisterState(FlowState.Stage, EnterStage, TickStage, ExitStage);
            fsm.RegisterState(FlowState.Pause, EnterPause, TickPause, ExitPause);
            fsm.RegisterState(FlowState.Result, EnterResult, TickResult, null);
            // GameOver 不需要 Tick：自动返回标题用 Timer.AfterUnscaled 排（见 EnterGameOver 的注释）。
            fsm.RegisterState(FlowState.GameOver, EnterGameOver, null, null);

            Game.Event.On(Events.StartNewGame, OnStartNewGame);
            Game.Event.On<int>(Events.CharChosen, OnCharChosen);
            Game.Event.On(Events.Resume, OnResume);
            Game.Event.On(Events.BackToMain, OnBackToMain);
            Game.Event.On(Events.RestartLevel, OnRestartLevel);
            Game.Event.On(Events.TimeUp, OnTimeUp);
            Game.Event.On(Events.HurryUp, OnHurryUp);

            Game.Logger.Info("Flow", "流程状态已安装");
        }

        public void Start() => Game.Fsm.Transition(FlowState.Boot);

        // ───────────────────────── Boot ─────────────────────────

        private void EnterBoot()
        {
            Game.Logger.Info("Flow", "→ Boot");

            // 启动期预热：把「必须同步拿到」的两样东西先装进引擎资源缓存 ——
            //   ① 像素字体：第一个面板就要用，取不到整块文字会掉回默认字体；
            //   ② 关卡文本：进关时要同步解析（StageSession.LoadLevelText）。
            // 引擎的 Game.Res 只有异步加载，所以这里"先 Preload、之后用 TryGet 同步取"，
            // 业务侧就不必绕开资源抽象去 Resources.Load 了。
            var warm = new System.Collections.Generic.List<string>
            {
                ResPaths.PixelFont, ResPaths.Level11, ResPaths.Level12,
                // 金币房（管中密室）的关卡文本：进管时要**同步**解析（StageSession.LoadLevelText），
                // 而游戏正跑着，这时候没有"再去异步加载一次"的机会 —— 所以两关的密室都要预热
                ResPaths.Level11Underground, ResPaths.Level12Underground,
                // 1-2 的地表段：它是"关卡列表中紧跟在 1-2 地下段后面的一项"，
                // 走侧向管口时流程会切过去 —— 那条路同样没有"再异步加载一次"的机会，所以也预热。
                ResPaths.Level12Surface,
            };

            Game.Res.Preload(warm, () =>
            {
                if (Game.Fsm.Current != FlowState.Boot) return;   // 已被打断就别再推进
                Game.Logger.Info("Flow", "启动预热完成（像素字体 + 关卡文本）");
                Game.UI.Open<BootPanel>();

                // 启动画面停留一会儿：既是给读条留时间，也是原版那种"Logo 一闪"的仪式感。
                // 用 Timer 而不是协程：Timer 归引擎管，切场景时统一清掉，不会留下悬空回调。
                Game.Timer.After(1.8f, () =>
                {
                    if (Game.Fsm.Current != FlowState.Boot) return;   // 已被打断就别再推进
                    Game.Scene.Load(Scenes.Menu, null, ShowMenuAfterSceneLoad);
                });
            });
        }

        // ───────────────────────── Menu ─────────────────────────

        private void EnterMenu()
        {
            Game.Logger.Info("Flow", "→ Menu");
            Game.Input.Unlock();
            Game.Sound.PlayBGM(Bgm.Overworld);
            Game.UI.Open<MainMenuPanel>();
        }

        /// <summary>
        /// Menu 场景加载完成后的收尾：清掉旧面板，再把菜单显示出来。
        /// <para>
        /// **不能只写 <c>Game.Fsm.Transition(FlowState.Menu)</c>**：FSM 对"切到当前所在的同一个状态"
        /// 是 **no-op**，而这一行前面刚 <c>CloseAll()</c> 把面板清掉了 ⇒ 结果是
        /// "菜单场景在、面板没了"的**空白标题屏**，而且**一条日志都没有**。
        /// 实测踩过：连续跑场景时（或任何"已经在 Menu 又触发一次回主菜单"的路径）第二次进菜单就变空白，
        /// 查了半天才知道是这里 —— 所以同状态时要**自己把面板开回来**。
        /// </para>
        /// </summary>
        private void ShowMenuAfterSceneLoad()
        {
            Game.UI.CloseAll();
            if (Game.Fsm.Current == FlowState.Menu)
            {
                Game.Logger.Info("Flow", "已在 Menu 状态 ⇒ 直接重开菜单面板（Transition 同状态是 no-op）");
                Game.UI.Open<MainMenuPanel>();
                return;
            }
            Game.Fsm.Transition(FlowState.Menu);
        }

        // ───────────────────────── CharSelect ─────────────────────────

        private void EnterCharSelect()
        {
            Game.Logger.Info("Flow", "→ CharSelect");
            Game.UI.Open<CharSelectPanel>();
        }

        /// <summary>
        /// 标题屏点了「1 PLAYER GAME / 2 PLAYER GAME」。
        /// <para>只切到选人屏，不直接进关 —— 人数在那边再确认一次（原版的两步流程）。</para>
        /// </summary>
        private void OnStartNewGame()
        {
            Game.Fsm.Transition(FlowState.CharSelect);
        }

        private void OnCharChosen(int count)
        {
            _playerCount = Mathf.Clamp(count, 1, 2);
            _currentPlayer = 0;
            for (var i = 0; i < _slots.Length; i++)
            {
                _slots[i].Points = 0;
                _slots[i].Coins = 0;
                _slots[i].Lives = GameConst.StartLives;
                _slots[i].Power = PowerState.Small;
            }
            Game.Logger.Info("Flow", $"玩家数选择：{_playerCount}P");

            // 新开一局 = 从第 1 关（1-1）开始，形态打回小马里奥
            // （原版就是"新开一局才归零"：`GameStateManager.cs:41-51 ConfigNewGame()` 里的 `marioSize = 0`。
            StageContext.SetSpawnPower(null);
            SetLevel(0);

            // 先清掉可能还在跑的旧会话。
            //
            // 开局（正常流程走不到，但选人屏/调试路径能走到），场景切换会销毁旧会话的 GameObject，
            // 而旧会话的模块还会被 Tick 一帧，于是抛
            // `SpriteRenderer has been destroyed but you are still trying to access it`
            // （异常在 Fsm 的 OnTick 里被吞成一条 Error，游戏看起来只是"莫名不动"）。
            DisposeSessions();

            Game.UI.CloseAll();
            Game.Scene.Load(Scenes.Stage01, null, () =>
            {
                Game.Fsm.Transition(FlowState.Loading);
            });
        }

        // ───────────────────────── Loading ─────────────────────────

        /// <summary>
        /// 本局入场卡开始的时刻 —— 用来保证它至少显示够 <see cref="GameConst.LevelIntroTime"/>。
        /// <para>取的是 <c>Time.unscaledTime</c>：入场卡期间 <c>timeScale = 0</c>（见 <see cref="EnterLoading"/>），
        /// <c>Time.time</c> 根本不前进，拿它算就永远是"还没开始等"。</para>
        /// </summary>
        private float _loadingStart;

        /// <summary>
        /// 第几次 Loading（每次进 <see cref="EnterLoading"/> 自增）。
        /// <para>看门狗是**一次性定时器**，过期后只看 FSM 是不是 Loading 会把"上一次的 Loading"
        /// 和"这一次的"混为一谈 ⇒ 误报（见注册处的实测记录）。拿这个号认领即可。</para>
        /// </summary>
        private int _loadingToken;

        /// <summary>
        /// 本次 Loading 是"同一关内换段"（1-2 地下段 → 地表段）：**不放入场卡**、也不等卡的停留时间。
        /// 由 <see cref="NextLevel"/> 的 <c>silent</c> 参数置位、在 <see cref="EnterLoading"/> 里消费并清掉。
        /// </summary>
        private bool _silentSwap;

        /// <summary>本次通关结算是否已经开始逐单位换分（每次进关重置，见 <see cref="EnterLoading"/>）。</summary>
        private bool _tallyStarted;

        /// <summary>
        /// 通关结算收尾停留（秒）：时间兑完分之后停多久才进下一关 / 结算屏。
        /// <para> 这个 2.5 是**原来那句 `_resultTimer = 2.5f` 的值**，不是音乐长度 ——
        /// `IAudio` 目前没暴露 clip 时长；等暴露了就换成 `LevelComplete.wav` 的真实长度
        /// （原版 `Castle.cs:26 LoadNewLevel(sceneName, levelCompleteMusic.length)` 用的正是音乐长度）。</para>
        /// <para> 起它**不再等于"音乐还要放多久"**：通关音乐已改到「时间倒计时开始」那一刻播
        /// （见 <see cref="TickStage"/> 的结算段），而倒计时本身要跑 <c>TimeLeft × TallyInterval</c> 秒，
        /// 所以音乐早就放完了 —— 这里纯粹是收尾停留。</para>
        /// </summary>
        private const float ResultMusicWait = 2.5f;

        /// <summary>
        /// 通关结算里"每兑 1 个时间单位"的间隔（秒）。
        /// <para>400 个单位 ≈ 8 秒。这个数是**观感口径、非 clone 出处**（clone 没有换分实现），
        /// 与 <see cref="GameConst.ScorePerTime"/> 一起登记在「允许的差异」；嫌快/嫌慢只改这一个常量。</para>
        /// </summary>
        private const float TallyInterval = 0.02f;

        /// <summary>换分用的时间累加器（见 <see cref="TallyInterval"/>）。</summary>
        private float _tallyAcc;

        /// <summary>
        /// 入场卡的看门狗（秒，**真实时间**）：超过它还在 Loading 就说明某个加载回调没来。
        /// <para>为什么是 unscaled：卡期间 <c>timeScale = 0</c>，受它影响的定时器（含
        /// <c>LevelProps</c> 里那个 5 秒超时）在这段时间<b>永不触发</b>；少了这条，
        /// "资源路径写错"会退化成"黑底读条屏永不消失、一条日志都没有"。</para>
        /// </summary>
        private const float LoadingWatchdogSeconds = 12f;

        /// <summary>
        /// 本局是第几关（0 = 1-1，1 = 1-2）。1-1 通关进 1-2；1-2 通关就是通关成功。
        /// <para>关卡路径与 HUD 上的名字都从这里推出来，避免"关号"与"路径"两处各写一份。</para>
        /// </summary>
        private int _levelIndex;

        /// <summary>
        /// 本作的分段：1-1 → 1-2 地下段 → 1-2 地表段（原版 clone 的 `World 1-2 - Castle Cut.unity`）。
        /// <para>为什么 1-2 是两项：原版 1-2 本来就是两段 —— 地下段走到底进右侧墙上的侧向管，
        /// 出到地表段，再走"出管 → 旗杆 → 城堡"。把旗杆/城堡硬塞进地下段（早先的做法）会让
        /// 旗杆落在地下段结尾那堵实心墙里（公式推出 x=166.5，而真值是墙 + 地板空洞），
        /// 过关时人被摆进墙里、脚下没有地板，直接掉出世界 —— 用户实测到的"穿墙 + 人飘着"。</para>
        /// <para>拆开之后各段自己声明自己的数据：地下段 `# no-flagpole` + `# side-exit`，
        /// 地表段 `# flagpole` / `# castle` / `# spawn`（全部取自 clone 场景里的对象坐标）。</para>
        /// <para>HUD 上两段都显示 `1-2`（原版就是这样）。</para>
        /// </summary>
        private static readonly string[] LevelPaths =
            { ResPaths.Level11, ResPaths.Level12, ResPaths.Level12Surface };
        private static readonly string[] LevelLabels = { "1-1", "1-2", "1-2" };
        /// <summary>1-2 的地下段是地下关（背景纯黑、砖是青色的）；1-1 与 1-2 地表段不是。</summary>
        private static readonly bool[] LevelUnderground = { false, true, false };

        /// <summary>
        /// 本段该放哪首 BGM。**按 clone 场景引用了哪首 mp3 定**（不是我们挑的，可复跑核对：
        /// 拿 mp3 的 guid 去 `.unity` 里 grep —— `World 1-2.unity` → `02-underworld`、
        /// `World 1-2 - Castle Cut.unity` → `01-main-theme-overworld`、`World 1-1.unity` → overworld，
        /// 两间密室场景 → `02-underworld`）。正好与 <see cref="LevelUnderground"/> 一一对应。
        /// </summary>
        private static string BgmFor(int index)
            => LevelUnderground[Mathf.Clamp(index, 0, LevelUnderground.Length - 1)]
                ? Bgm.Underworld
                : Bgm.Overworld;

        /// <summary>切到第 index 关（只改"本局是哪一关"，真正的加载在 EnterLoading 里做）。</summary>
        private void SetLevel(int index)
        {
            _levelIndex = Mathf.Clamp(index, 0, LevelPaths.Length - 1);
            StageContext.SetLevel(LevelPaths[_levelIndex], LevelLabels[_levelIndex], LevelUnderground[_levelIndex]);
            Game.Logger.Info("Flow",
                $"本局关卡：WORLD {LevelLabels[_levelIndex]}（{LevelPaths[_levelIndex]}，" +
                $"{(LevelUnderground[_levelIndex] ? "地下" : "地上")}）");
        }

        private void EnterLoading()
        {
            // 本次是不是"同一关内换段"（见 NextLevel 的 silent 参数）：是则整段不放入场卡。
            var silent = _silentSwap;
            _silentSwap = false;
            Game.Logger.Info("Flow", silent ? "→ Loading（同一关内换段：跳过入场卡）" : "→ Loading");
            // 入场卡期间【世界不跑】—— 这是"与原版同形"的关键，不是停机动画。
            //
            // 原版那张卡是**另一个场景**：clone `Assets/Scripts/LevelManager.cs:407,433` 是
            // `LoadSceneDelay("Level Start Screen", …)` ⇒ **关卡场景在卡放完之后才加载**，
            // 所以卡期间场上一个实体都没有，人与敌人是**同时**起步的（1-2 开局 ≈4.9 秒可控）。
            //
            // 我们这张卡与关卡重叠，而实体挂在各自的 `MonoBehaviour.Update` 上
            // （`Module/Entities/EnemyModule.cs:179` 用的就是 `Time.deltaTime`）——
            // 不冻住的话卡那 2 秒世界照跑：实测两只栗宝宝从 `x=13.5/14.5` 走到 `8.32/9.32`
            // （2.5 格/秒 × 2.04 秒），恰好把玩家的可控时间吃掉 2 秒
            // （验收表 12-2 / 对照表 四「1-2 起点安全」）。
            //
            // 冻法 = `timeScale = 0`（与暂停 / 结算 / GameOver 完全是同一套机制，不新造开关）：
            // 实体 `Update` 拿到的 `dt` 变 0、2D 物理也不再步进；由 `ExitLoading` 在离开本状态时
            // 恢复成 1 —— 也就是 `→ Stage`（原版世界真正开始跑的那一帧）才恢复。
            //
            // 冻住之后 `Time.time` / `Time.deltaTime` **都不再前进**，所以本状态里所有的等待
            // 都必须走 unscaled 口径（引擎 Timer 的教训：`timeScale = 0` 时普通 `Timer.After` 永不触发）。
            Time.timeScale = 0f;
            _loadingStart = Time.unscaledTime;
            // 给这一次 Loading 发一个号：看门狗拿它认"我等的还是不是我那一次"（见下面注册处）。
            var watchToken = ++_loadingToken;
            // 新的一关：结算换分标记复位（旗杆那一段才会用到它）。
            _tallyStarted = false;
            // 进关卡一律清掉落点覆盖：它只属于"管中密室"（见 StageContext.SpawnOverride）。
            // 不清的话，从密室出来、死亡重来或进下一关时都会沿用密室的落点。
            StageContext.SetSpawnOverride(null);
            //
            // `NextLevel()`（1-1 通关 → 1-2、1-2 地下段 → 地表段）是在
            // `Game.Fsm.Transition(FlowState.Loading)` **之前**设的，而 Transition 是**同步**调用
            // ⇒ EnterLoading 紧接着把它清成 null ⇒ 进 1-2 实测：
            //   `18:31:23.069 [Flow] 跨段保留形态：Big` → `18:31:23.132 马里奥已就位 ... 形态=Small`
            //   → `PROBE state[跨段-进 1-2] ... power=Small box=[-0.38,0.38]x[0.00,0.75]`（小马里奥盒高 0.75）。
            //
            // 清形态的责任交给两个真正"新一局"的入口（那里才是这个语义）：
            //   · 新开一局 → `OnCharChosen`；· 死亡重来 / 换手 → `ReloadStageWithIntro`。
            //
            // 为什么换段必须保留（1:1 出处，clone `原版资源/参考工程/SMB-clone` 逐行核对）：
            //   · `Assets/Scripts/GameStateManager.cs:11` `public int marioSize;` 是**跨场景的全局**状态
            //     （`:21-28`：只有唯一实例 `DontDestroyOnLoad`，`ConfigNewGame()` 也只在那一刻调）；
            //   · 换关时 `LevelManager.cs:403-408 LoadNewLevel` 先 `SaveGameState()` 把**本关**的形态写回全局
            //     （`GameStateManager.cs:64-72`：`marioSize = t_LevelManager.marioSize;`），
            //     再做 `ConfigNewLevel()`（`GameStateManager.cs:53-57` —— **只重置时间 / hurryUp / 出生点，
            //     一个字节都不动 marioSize**）；
            //   · 下一关进场 `LevelManager.cs:87-89 Start` → `:114-121 RetrieveGameState`
            //     （`marioSize = t_GameStateManager.marioSize;`）逐字读回来。
            //   ⇒ 原版**换关一律保留**，只有**死亡 / 降级 / 新开一局**才归零
            //     （`LevelManager.cs:316 MarioRespawn`、`:306 MarioPowerDownCo`、`GameStateManager.cs:42`）。
            // 命数【必须当参数传】给读条屏：面板打开的时刻新会话还没 Build、StageContext 还是空的，
            // 面板自己读只会拿到默认值（死亡重来时就会把 ×1 显示成 ×3）。
            // 换段（silent）**不 open 这张卡**：卡上写的就是 "WORLD x-x / ×命数" ——
            //   这一段的耗时只剩"重建会话"的纯加载（实测 ~125 毫秒，见下面 Go() 的注释），
            //   观感是一次切镜，紧接着就是地表段自己的"从出管口升起"过场（`# pipe-rise`，原版同款）。
            //   注意 HUD 保持不变（两段都是 `1-2`），玩家不会觉得"又开了一关"。
            if (silent)
                Game.Logger.Info("Flow", "本段与上一段是同一关（侧向管接出来的）⇒ 跳过入场卡，直接进关");
            else
                Game.UI.Open<LoadingPanel>(_slots[_currentPlayer].Lives);

            // 看门狗（**真实时间**）：兜住**整段 Loading**（地形 / 实体 / 玩家 / 装饰全都算）。
            //
            //   旧注释写的是"`LevelProps` 那个 5 秒看门狗在卡期间不会触发" —— 那个 5 秒看门狗
            //   引擎契约：`LoadAsset` **失败也会回调 null**（`Runtime/Core/Contracts.cs:1033/1042`；
            //   实现 `Runtime/Resource/ResourceManager.cs:305-343` + `ResourceBackend.cs:233-259`）
            //   ⇒ 资源缺失一律由回调里的 `sp == null` 报 Error；**本看门狗只负责引擎级故障**
            //   （回调真的没到，例如加载在途被卡死）：它的判据是"`fsm` 仍是 Loading **且**
            //   token 未变"，也就是"确实还有回调没来"，与"路径写不写错"无关。
            Game.Timer.AfterUnscaled(LoadingWatchdogSeconds, () =>
            {
                // 看门狗必须绑定"**这一次** Loading"。它是一次性定时器，12 秒后一定会响，
                // 而只判 `fsm == Loading` 是不够的：若中途"死亡重来 / 换关"又进了一次 Loading，
                // 上一局的看门狗就会在**新的** Loading 里响，把一次正常（才 1.5 秒）的加载报成"卡住"。
                // Loading#1 起于 22:32:35.654，它的看门狗在 22:32:47.664 命中 Loading#2（起于 22:32:46.151）
                // ⇒ 误报一条 `[Error] 入场卡停留超过 12 秒仍未进关`，而 Loading#2 在 22:32:48.044 就 `→ Stage` 了。
                // 这条误报会污染"每个场景窗口 [Error] = 0"的验收判据（见验收表 E-20 的消除记录）。
                if (watchToken != _loadingToken) return;
                if (Game.Fsm.Current != FlowState.Loading) return;
                Game.Logger.Error("Flow",
                    $"入场卡停留超过 {LoadingWatchdogSeconds} 秒仍未进关（fsm=Loading）：" +
                    "说明某个加载回调**真的没到**（引擎契约是失败也会回调 null，见本方法上方注释）—— " +
                    "先看有没有并列的 `加载失败：<path>` 一行；只有本行而没有那一行，才是加载在途被卡死");
            });

            _session = new StageSession();
            _session.Build(() =>
            {
                // 用第一槽位的数值初始化（双人时换手再恢复另一个人的）。
                _session.Score.Restore(_slots[_currentPlayer].Points, _slots[_currentPlayer].Coins,
                    _slots[_currentPlayer].Lives);

                // 【不要】在这里再 Spawn 一次玩家。
                // 内部（由 SpawnPlayer 负责）**已经**生成过玩家了 —— 于是每局开局都生成两遍，
                // 日志里 "马里奥已就位" 连打两条，出生点公式也在两个文件里各写了一份
                // （MinWorldX + 3.5f）。Spawn 本身幂等所以画面看不出问题，
                // 但"谁负责生成玩家"这件事一旦有两个答案，改出生点就必然会漏改一处。
                // 职责归 StageSession：它在 Ready=true 之前生成，时机更靠前，也更安全。
                //
                // 旗杆 / 城堡挂在【关卡根】下（不是场景根）：进管中密室时整关会被冻结
                // （`StageSession.Hide` 关掉关卡根），挂在场景根上的话旗杆和城堡会留在画面上。
                LevelProps.Build(_session.Level, _session.Level?.Root != null ? _session.Level.Root.transform : null, () =>
                {
                    // 入场卡必须【看得见】再进关。
                    //
                    // —— 黑屏只闪一下，玩家读不到 "WORLD 1-1 / ×3"，观感是"硬切进关卡"。
                    // 原版这张卡要停约 2 秒。所以这里把"还差多久"补出来再切；
                    // 加载本身就慢的情况则不必再等（wait <= 0 直接进）。
                    // 用 **unscaled** 口径量、也用 **unscaled** 定时器等：本状态把 timeScale 冻成 0
                    // （见 EnterLoading），`Time.time` 不走、普通 `Timer.After` 也不会触发。
                    void Go()
                    {
                        if (Game.Fsm.Current != FlowState.Loading) return;   // 已被打断就别再推进
                        Game.UI.CloseAll();
                        Game.Fsm.Transition(FlowState.Stage);
                    }

                    // 换段（silent）：没有卡，就不必"把卡停够 LevelIntroTime"（那段等待只为让人读到卡）。
                    var wait = silent ? 0f : GameConst.LevelIntroTime - (Time.unscaledTime - _loadingStart);
                    if (wait <= 0f) Go();
                    else Game.Timer.AfterUnscaled(wait, Go);
                });
            });
        }

        // ───────────────────────── Stage ─────────────────────────

        private void EnterStage()
        {
            Game.Logger.Info("Flow", "→ Stage");
            Game.Input.Unlock();
            // BGM 按**这一段**是哪一段来选（地下段 / 密室 = 地下主题，地表段 = 主世界主题），
            // 出处 = clone 各场景引用了哪首 mp3（见 BgmFor 的注释），不是我们挑的。
            Game.Sound.PlayBGM(BgmFor(_levelIndex));
            Game.UI.Open<HudPanel>();
            Game.Event.On(Events.LevelCleared, OnLevelCleared);

            // ── 1-2 地表段开局的「从出管口升起」过场 ──
            // 原版在这一段的出管口里摆了一个 Spawn Point（`clone (0.5,1.5)`，见关卡文件的 `# spawn`），
            // 人从那个点升到管顶（`# pipe-rise 0.5 2`）才接管操作 —— 不是"直接出现在管顶"。
            var lv = _session?.Level;
            if (lv != null && lv.HasPipeRise && _session.Player != null)
            {
                var from = new Vector2(lv.SpawnX, lv.SpawnY);
                var to = new Vector2(lv.PipeRiseX, lv.PipeRiseY);
                // 先显式摆回出生点：Loading 这一段玩家物理照样在跑（PlayerActor 的 Update 不受状态机管），
                // 而出生点在实心管口里 ⇒ 位置已经被碰撞解算推走了。不摆回来，"升起点"就不是原版那个点。
                _session.Player.Teleport(from);
                _session.Player.StartPipeRiseFrom(from, to);
                _warpPhase = WarpPhase.RiseOnly;
                _warpWait = WarpTransitionTimeout;
            }
        }

        /// <summary>
        /// 关卡的每帧推进。
        /// <para>签名必须是 <c>Action&lt;float&gt;</c> —— 引擎的 RegisterState 就是这个形状；
        /// 无参版本会在编译期直接失败（不是运行时空转）。</para>
        /// </summary>
        private void TickStage(float dt)
        {
            // 当前在跑的是哪一局：主关卡，或者（进管之后）金币房。
            var active = ActiveSession;
            if (active == null || !active.Ready) return;

            // 暂停：原版是按 Start 键。
            if (Game.Input.GetKeyDown(GameKey.Escape))
            {
                Game.Fsm.Transition(FlowState.Pause);
                return;
            }

            // 管中过场（沉进管子 / 顶出管子 / 走进管口）：这一段只等玩家动作做完，
            // 不推进关卡逻辑（时间、敌人、相机都停），做完才真正换场。
            // "不推进关卡逻辑"这条对本段的食人花是**硬要求**：1-2 地表段的出管口那根管里就有花，
            //    原版在升起过场期间不会咬人（花的初相位是"缩在管里"、马里奥在 2 格内它不再伸出来），
            //    但停掉 Gameplay 是最硬的一层保险 —— 不管花的实现怎么改，过场期间都不做玩家×敌判定。
            if (_warpWait >= 0f)
            {
                _warpWait -= dt;
                // 升起过场（1-2 地表段开局）期间**相机必须照常归位**：
                // 这一段是刚 build 出来的（`StageSession.Build` 里的 `Camera.Setup` 只设背景与取景，
                // 真正的"跟住玩家"发生在 `Camera.Tick`），而这一段又挡掉了下面所有 Tick ⇒
                // 不补这一句，升起那 0.2 秒里相机停在上一段的取景上、画面是一片空白
                // （实测：截图 12706 字节 = 单色帧）。原版在马里奥冒头时取景就已经跟着他了。
                if (_warpPhase == WarpPhase.RiseOnly) active.Camera?.Tick(dt);
                var p = active.Player;
                if (p == null || !p.Busy || _warpWait <= 0f)
                {
                    var phase = _warpPhase;
                    if (p != null && p.Busy)
                        Game.Logger.Warn("Flow",
                            $"管中过场超时（{WarpTransitionTimeout} 秒，{phase}）—— 强制结束该过场");
                    _warpPhase = WarpPhase.None;
                    switch (phase)
                    {
                        case WarpPhase.IntoRoom: BeginBonusRoom(); break;
                        case WarpPhase.OutOfRoom: FinishExitBonusRoom(); break;
                        // 换段**不放入场卡**：HUD 上两段都是 `1-2`，原版这里也没有标题卡
                        // （它俩本来就是同一关，见 NextLevel 的 silent 说明）。
                        case WarpPhase.NextSection: NextLevel(silent: true); break;
                        // RiseOnly：升起播完就没事了 —— 必须在这里把 _warpWait 清掉，
                        // 否则下一帧又进这个分支、而 phase 已经是 None ⇒ 每帧空转、关卡永远不动。
                        default: _warpWait = -1f; break;
                    }
                }
                return;
            }

            // 摸到旗杆就**停表** —— 原版 `MarioReachFlagPole()`（clone `LevelManager.cs:597`）
            //   第一句就是 `timerPaused = true;`。不停的话，滑杆 + 走城堡 + 结算这十几秒里倒计时照走，
            //   兑完只加了 18800 分（= 376 单位），差的 19 个正好 = 7.5 秒 ÷ 0.4 秒/单位（正常倒计时速率）。
            if (!active.Player.Busy && !active.Gameplay.ReachedFlag) active.Score.TickTime(dt);
            active.Gameplay.Tick(dt);
            active.Camera.Tick(dt);

            // 传送管：玩法层判"站上管顶按了 ↓ / 走进了侧向管口"，换场由这里执行。
            var warp = active.Gameplay.ConsumePipeWarp();
            if (warp == PipeWarpKind.EnterBonusRoom) { EnterBonusRoom(); return; }
            if (warp == PipeWarpKind.ExitBonusRoom) { ExitBonusRoom(); return; }
            // 1-2 地下段 → 地表段：往前走一段，走的是"切下一段"的路（与"关卡通过 → 下一关"同一条），
            // 不挂起主关卡、不起子会话、也不回到原处。
            // 原版的过场是"自动向右走进管口"（clone `PipeWarpSide.cs:29` 触发了就
            // `mario.AutomaticWalk(levelEntryWalkSpeedX)`，走进了才 `LoadSceneCurrentLevelSetSpawnPipe`），
            // 所以这里先播侧向进管（工程已有的 `StartPipeEnterSide`），**走完再切段**。
            if (warp == PipeWarpKind.EnterNextSection)
            {
                Game.Logger.Info("Pipe", "侧向管口 → 1-2 地表段（先走进管口，走完再切段）");
                active.Player.StartPipeEnterSide();
                _warpPhase = WarpPhase.NextSection;
                _warpWait = WarpTransitionTimeout;
                return;
            }

            // 旗杆流程走完 → ① 剩余时间换分 ② 通关音乐 ③ 进下一关 / 结算屏。
            if (active.Gameplay.ReachedFlag && !active.Player.Busy && _resultTimer < 0f)
            {
                //   一帧兑 1 个单位（观感就是原版那种"哗哗往上跳"）、每单位 `GameConst.ScorePerTime` 分，
                //   每兑一次响一声 `Sfx.Beep`（原版的 tick 音）。`TallyTimeUnit` **不会**触发 TimeUp。
                if (!_tallyStarted)
                {
                    _tallyStarted = true;
                    Game.Sound.StopBGM(0.2f);
                    // ② 通关音乐：**全项目只在这一处放，且只放这一次** —— 时机 =「时间倒计时开始」。
                    active.Audio.PlaySfx(Sfx.LevelComplete);
                    Game.Logger.Info("Flow",
                        $"通关结算开始：剩余时间 {active.Score.TimeLeft} 个单位 × {GameConst.ScorePerTime} 分" +
                        $"（结算前分 {active.Score.Points}、币 {active.Score.Coins}）；" +
                        $"停 BGM，播放通关音乐 {Sfx.LevelComplete}（只此一次），逐单位换分");
                }

                // 兑换节奏**按时间**、不按帧：一帧兑 1 个单位时"结算多久"会随帧率变 ——
                //   33 秒还没兑完。改成按 `dt` 累积 ⇒ 前台/后台、高帧/低帧都是同一段时长。
                _tallyAcc += dt;
                var more = active.Score.TimeLeft > 0;
                while (_tallyAcc >= TallyInterval && more)
                {
                    _tallyAcc -= TallyInterval;
                    more = active.Score.TallyTimeUnit(GameConst.ScorePerTime);
                    active.Audio.PlaySfx(Sfx.Beep);
                }
                if (more) return;      // 还在跳：这一帧别往下走（E-20 的教训：分支里该 return 就 return）

                //   这一段只负责"停留一会再进下一关 / 结算屏"。
                Game.Logger.Info("Flow",
                    $"通关结算完成：分 {active.Score.Points}、时间已归零；" +
                    $"{ResultMusicWait} 秒后进下一关 / 结算屏（通关音乐已在倒计时开始那一刻播放，此处不重复）");
                _resultTimer = ResultMusicWait;          // ③ 停留一会再进下一关 / 结算屏
            }
            if (_resultTimer >= 0f)
            {
                _resultTimer -= dt;
                if (_resultTimer <= 0f)
                {
                    // 1-1 通关 → 直接进 1-2（原版中间不放 COURSE CLEAR 那一屏）；
                    // 最后一关（1-2）通关 → 结算屏，这就是"通关成功"。
                    if (_levelIndex < LevelPaths.Length - 1)
                    {
                        NextLevel();

                        //
                        // NextLevel() → DisposeSessions() → StageSession.Dispose() 会把
                        // `Level / Player / Score / Camera / Gameplay` **逐个置 null**（见 StageSession.cs
                        // 的 Dispose 末段），而本方法开头第 335 行的 `active = ActiveSession` 是**这一帧早期
                        // 取到的旧引用**。少了这个 return，执行就会继续往下走到本方法末段的
                        // `active.Gameplay.PendingDeath` ⇒ NullReferenceException，被引擎的状态机吞成
                        //   `[Error] [Fsm] OnTick error [Loading]: ... AppFlow.TickStage`
                        //
                        // 同一个动作在下面「侧向管口 → 1-2 地表段」（第 374 行 `NextLevel(); return;`）
                        // 本来就有 return，只有这一处漏了 —— 所以只有「1-1 通关 → 1-2」那条路径会抛。
                        // 实测对照：`flag`（走这条路径）每次都 1 条；`level12`（在 Tick 之外直接调
                        // NextLevel）、`section12`（走第 374 行那条）**0 条**。见验收表 E-20 的消除记录。
                        return;
                    }
                    Game.Fsm.Transition(FlowState.Result);
                }
            }

            // 死亡 → 等动画放完再决定重来还是 GameOver。
            if (active.Gameplay.PendingDeath && _deathTimer < 0f)
            {
                _deathTimer = GameConst.DeathDuration + 0.4f;
                Game.Sound.StopBGM(0.2f);
                active.Audio.PlaySfx(Sfx.Death);
            }
            if (_deathTimer >= 0f)
            {
                _deathTimer -= dt;
                if (_deathTimer <= 0f) HandleDeathResolved();
            }
        }

        // ───────────────────── 管中密室（1-1 / 1-2 的金币房）─────────────────────
        //
        // 两关走的是**同一条**链路：进管口与密室坐标由 PipeWarpTable 按当前关卡给出
        // （1-1 的进管在 T x=43..44、1-2 的在 T x=100..101），这里不判"是第几关"。

        /// <summary>本局这一关的传送管数据（进管口 / 密室落点 / 出口面 / 出管位置）。</summary>
        private static PipeWarpInfo Warp => PipeWarpTable.Current;

        /// <summary>
        /// 玩家在主关卡按下 ↓ 进了那根水管：先让他**沉进管子**（原版过场，见 GameplayModule
        /// 的 <c>TickPipeWarp</c> 判定），沉完再换场。
        /// </summary>
        private void EnterBonusRoom()
        {
            if (_bonusSession != null)
            {
                Game.Logger.Warn("Flow", "已经在金币房里，忽略重复的进管请求");
                return;
            }

            Game.Logger.Info("Pipe", $"→ 进管：{StageContext.LevelPath} → {Warp.BonusLevelPath}（{Warp.Source}）");

            // 进管时先把音乐停掉：clone `PipeWarpDown.cs:45` 进管时就是 musicSource.Stop()。
            // 密室里的地下主题在"密室会话就绪"那一刻起播（见 BeginBonusRoom）——
            // 素材出处 = clone `Assets/Sounds/02-underworld.mp3`（两间密室场景 + 1-2 地下段都引用它）。
            Game.Sound.StopBGM(0.2f);

            _session.Player.StartPipeSink();
            _warpPhase = WarpPhase.IntoRoom;
            _warpWait = WarpTransitionTimeout;
        }

        /// <summary>下沉结束：冻结主关卡、建金币房（两者共用同一份分数）。</summary>
        private void BeginBonusRoom()
        {
            _warpWait = -1f;

            // 先把这一关的传送管数据取出来：下面 SetLevel 会把 StageContext.LevelPath 换成密室路径。
            var warp = Warp;

            // 主关卡只冻结不销毁：出管回主关卡时，顶碎的砖、踩掉的敌人、位置都还在。
            _session.Hide();

            // 密室是"黑底青砖"（underground=true）+ 管中密室（subArea=true ⇒ 无旗杆/城堡、砖不可顶碎）。
            StageContext.SetLevel(warp.BonusLevelPath, LevelLabels[_levelIndex], true, true);
            StageContext.SetSpawnOverride(warp.BonusRoomSpawn);
            // 形态跟着进密室（原版是同一只马里奥：大马里奥进密室出来还是大马里奥；登记项 E-13）。
            var carry = _session.Player != null ? _session.Player.Power : PowerState.Small;
            StageContext.SetSpawnPower(carry);

            var sub = new StageSession();
            _bonusSession = sub;
            // 第三个参数：共用主关卡的分数/金币/命数/时间（原版在密室里时间照走、币照记）。
            sub.Build(() =>
            {
                if (!ReferenceEquals(_bonusSession, sub)) return;   // 过场中被别的流程打断（例如死亡重来）
                // 密室放地下主题 —— 出处 = clone `World 1-1 - Underground.unity` / `World 1-2 - Underground.unity`
                // 都引用了 `02-underworld.mp3`（原版密室里放的就是这一首）。
                Game.Sound.PlayBGM(Bgm.Underworld);
                Game.Logger.Info("Flow",
                    $"金币房已就绪：金币 {sub.Score.Coins}、分数 {sub.Score.Points}、时间 {sub.Score.TimeLeft}、" +
                    $"形态 {sub.Player?.Power}、BGM {Bgm.Underworld}" +
                    $"（{warp.Source}，房间共 {warp.BonusRoomCoins} 枚金币）");
            }, _session.Score);
        }

        /// <summary>玩家在密室里走进了侧向出口管：先播"走进去"，走完再换场。</summary>
        private void ExitBonusRoom()
        {
            if (_bonusSession == null) return;

            Game.Logger.Info("Pipe",
                $"→ 出管：{Warp.BonusLevelPath} → {LevelPaths[_levelIndex]}，" +
                $"出场管 T x={Warp.ReturnPipeTopFeet.x}（{Warp.Source}）");

            _bonusSession.Player.StartPipeEnterSide();
            _warpPhase = WarpPhase.OutOfRoom;
            _warpWait = WarpTransitionTimeout;
        }

        /// <summary>走出管口：销毁密室、恢复主关卡，玩家从出场管顶出来（原版是从管口冒头）。</summary>
        private void FinishExitBonusRoom()
        {
            _warpWait = -1f;

            _bonusSession.Dispose();
            _bonusSession = null;

            StageContext.SetLevel(LevelPaths[_levelIndex], LevelLabels[_levelIndex], LevelUnderground[_levelIndex]);
            StageContext.SetSpawnOverride(null);

            // 出管位置必须问**主关卡**那一套（此刻 StageContext.LevelPath 还是密室路径，
            //   虽然两者映射到同一套，但显式按主关卡取，将来多一个密室也不会错）。
            var top = PipeWarpTable.For(LevelPaths[_levelIndex]).ReturnPipeTopFeet;

            _session.Resume();
            _session.Player.StartPipeRise(top);

            // 回到主关卡就换回**这一段**的主题（1-1 → 主世界；1-2 地下段 → 地下）。
            Game.Sound.PlayBGM(BgmFor(_levelIndex));
            Game.Logger.Info("Pipe", $"出管完成：玩家出现在出场管顶 ({top.x}, {top.y})");
        }

        /// <summary>
        /// 撤掉金币房、恢复主关卡（不走"从管里出来"的过场）。
        /// <para>用在"在密室里死了"这类必须立刻回到主关卡的时刻；分数是共用的一份，所以不会丢。</para>
        /// </summary>
        private void DiscardBonusRoom()
        {
            if (_bonusSession == null) return;
            _warpWait = -1f;
            _bonusSession.Dispose();
            _bonusSession = null;

            StageContext.SetLevel(LevelPaths[_levelIndex], LevelLabels[_levelIndex], LevelUnderground[_levelIndex]);
            StageContext.SetSpawnOverride(null);
            _session?.Resume();
            Game.Logger.Warn("Flow", "金币房被撤掉（未走出管）：主关卡已恢复，分数/金币保留");
        }

        /// <summary>
        /// 离开入场卡：把世界的时间推进还回来（<see cref="EnterLoading"/> 冻的）。
        /// <para>正常路径就是 <c>→ Stage</c>（<see cref="EnterStage"/> 紧接着跑）；
        /// 被打断的路径（回主菜单）也走这里，所以解冻不会漏。</para>
        /// </summary>
        private void ExitLoading()
        {
            Time.timeScale = 1f;
        }

        private void ExitStage()
        {
            Game.Event.Off(Events.LevelCleared, OnLevelCleared);
        }

        /// <summary>
        /// 进下一关：换好本局关卡后回到 Loading，由它按新的 <c>StageContext.LevelPath</c>
        /// 重新 Build 一整局，并再放一次 "WORLD 1-2 / ×命数" 的入场卡。
        /// <para>
        /// <paramref name="silent"/> = <b>不放入场卡</b>，只给"同一关内的换段"用
        /// （1-2 地下段走侧向管口 → 1-2 地表段）。原版这两段本来就是**同一关**：一个场景里
        /// 一根侧管把马里奥送到右上的地表段，全程没有标题卡（HUD 上也一直是 `1-2`，见
        /// <see cref="LevelLabels"/>）。我们因为把两段拆成了两份关卡数据，必须重建一次会话 ——
        /// 但**表现上不能出现那张卡**。
        /// </para>
        /// <para>
        /// 黑屏的 1-2？」日志现场：`[Pipe] 侧向管口 → 1-2 地表段` 后紧跟
        /// `[Flow] 关卡通过 → 进入 WORLD 1-2` + `→ Loading` —— 走的就是"关卡通过"那条路。
        /// </para>
        /// </summary>
        private void NextLevel(bool silent = false)
        {
            // _resultTimer 必须重置成 -1：它此刻是 0（就是这次触发的），
            // 不复位的话下一关的 TickStage 每帧都会判定"通关倒计时到点"，反复重复跳关。
            _resultTimer = -1f;

            // 形态要在【销毁会话之前】读出来：DisposeSessions 会把 Player 置 null。
            // 原版同一只马里奥跨段（1-1 → 1-2 地下段 → 地表段），形态（大 / 火）一路保留；
            // 早先每一段都是一个新会话 ⇒ 每一次切段都悄悄打回小马里奥（登记项 E-13）。
            var carry = _session != null && _session.Player != null
                ? _session.Player.Power
                : PowerState.Small;

            // 换关必须把**这一段已经打到现在的**分数 / 金币 / 命数写回玩家槽位。
            //
            // `_slots[_currentPlayer]` 里的值，而整个工程只有**死亡路径**（`HandleDeathResolved`）写过这份值，
            // 换关路径从来没写 ⇒ 每次换关都把三项退回**开局值**：
            // 1-1 打到 5000 分 / 2 条命 → 进 1-2 时 `Score.Restore(0, 0, 3)` ⇒ HUD 变成 0 分 ×3
            // （旗杆那 5000 分正是这么"没了"的，看起来就像"踩旗子不加分"）。
            // 顺序也不能反：必须在 `DisposeSessions()`（把 `_session` 拆掉）**之前**读。
            if (_session != null && _session.Score != null)
            {
                _slots[_currentPlayer].Points = _session.Score.Points;
                _slots[_currentPlayer].Coins = _session.Score.Coins;
                _slots[_currentPlayer].Lives = _session.Score.Lives;
                Game.Logger.Info("Flow",
                    $"换关交接（写回玩家槽位）：分数 {_slots[_currentPlayer].Points}、金币 {_slots[_currentPlayer].Coins}、" +
                    $"命数 {_slots[_currentPlayer].Lives}、形态 {carry}");
            }

            DisposeSessions();

            SetLevel(_levelIndex + 1);
            // SetLevel 之后再设：EnterLoading 里有一句"清成 null"（新开一局 / 死亡重来的语义）。
            if (carry != PowerState.Small)
                Game.Logger.Info("Flow", $"跨段保留形态：{carry}（原版同一只马里奥，不因换段变小）");
            StageContext.SetSpawnPower(carry);
            // 本次 Loading 要不要放入场卡（见方法注释）：交给 EnterLoading 消费。
            _silentSwap = silent;
            Game.Logger.Info("Flow", silent
                ? $"同一关内换段（不放入场卡）→ WORLD {LevelLabels[_levelIndex]}（{LevelPaths[_levelIndex]}）"
                : $"关卡通过 → 进入 WORLD {LevelLabels[_levelIndex]}");

            Game.UI.CloseAll();
            Game.Fsm.Transition(FlowState.Loading);
        }

        private void OnLevelCleared()
        {
            Game.Logger.Info("Flow", "关卡通过");
            Game.Sound.StopBGM(0.2f);
        }

        // ───────────────────────── 死亡处理 ─────────────────────────

        /// <summary>
        /// 剩 100 秒：原版会喊一声 hurry-up（音乐也转快节奏）。
        /// <para>踩过的坑：<see cref="Events.HurryUp"/> 一直是"有人发、没人收" ——
        /// ScoreModule 到点发了事件，但全项目没有监听者，于是这声提示从来没响过，
        /// Sfx.HurryUp 与 hurryup.wav 都成了死资源。事件名定义了不等于接上了。</para>
        /// </summary>
        private void OnHurryUp()
        {
            Game.Logger.Info("Flow", "Hurry up（剩 100 秒）");
            if (_session?.Audio != null) _session.Audio.PlaySfx(Sfx.HurryUp);
            else Game.Sound?.PlaySFX(Sfx.HurryUp);
        }

        private void OnTimeUp()
        {
            if (_session == null) return;
            // 必须走 Gameplay 的强制死亡入口。
            // GameplayModule.PendingDeath 触发，而 Kill() 不置这个标记。于是时间到 0 之后
            // 画面【永远卡在死的那一帧】：不重来、也不 GameOver。用户原话："时间为 0 时候也不会重来"。
            _session.Gameplay.KillPlayerForcibly();
        }

        private void HandleDeathResolved()
        {
            _deathTimer = -1f;

            // 死在金币房里：先把密室撤掉、主关卡恢复（分数/金币是**共用**的一份，什么都没丢），
            // 再照常走"减命 → 重开本关"——后面的路径都以 `_session`（主关卡）为准。
            if (_bonusSession != null) DiscardBonusRoom();

            var hasLife = _session.Score.LoseLife();

            _slots[_currentPlayer].Points = _session.Score.Points;
            _slots[_currentPlayer].Coins = _session.Score.Coins;
            _slots[_currentPlayer].Lives = _session.Score.Lives;
            _slots[_currentPlayer].Power = PowerState.Small;   // 死一次形态归零（原版规则）

            if (!hasLife)
            {
                // 本玩家没命了：双人模式换手，单人直接 GameOver。
                if (_playerCount > 1 && OtherPlayerHasLives())
                {
                    SwitchPlayer();
                    return;
                }
                Game.Fsm.Transition(FlowState.GameOver);
                return;
            }

            Game.Logger.Info("Flow", $"还剩余 {_session.Score.Lives} 条命，重开本关（先过入场卡）");
            ReloadStageWithIntro();
        }

        /// <summary>
        /// 重新进关：**先过一遍入场卡（WORLD x-x / ×命数），再进关卡** —— 原版的死亡重来就是这个节奏。
        /// <para>
        /// 玩家看到的是"死完直接瞬移回起点"，和原版完全不像（用户原话："重来时候不是先会跳到
        /// 人数界面吗？然后再跳到游戏里？"）。
        /// </para>
        /// <para>
        /// 做法：重载关卡场景 → 走 <see cref="FlowState.Loading"/>；<c>EnterLoading</c> 会开
        /// <c>LoadingPanel</c>（就是那张卡）并新建会话，命数从 <c>_slots</c> 恢复，
        /// 所以卡上显示的是**扣命后**的正确值。
        /// </para>
        /// </summary>
        private void ReloadStageWithIntro()
        {
            // 死亡重来 / 换手：形态打回小马里奥（原版 `LevelManager.cs:312-339 MarioRespawn` 里那句
            StageContext.SetSpawnPower(null);
            Game.UI.CloseAll();
            Game.Sound.StopAll();
            DisposeSessions();
            Game.Scene.Load(Scenes.Stage01, null, () => Game.Fsm.Transition(FlowState.Loading));
        }

        /// <summary>
        /// 把两个会话都销毁（换关 / 回主菜单 / 结算 / GameOver 时用）。
        /// <para>顺序：先密室后主关卡 —— <c>Dispose</c> 会解绑 <c>StageContext</c> 并把全局视图置空，
        /// 反过来的话留到最后销毁的那个会把它自己（一个已死的会话）留在全局视图上。</para>
        /// </summary>
        private void DisposeSessions()
        {
            _warpWait = -1f;
            _bonusSession?.Dispose();
            _bonusSession = null;
            _session?.Dispose();
            _session = null;
        }

        private bool OtherPlayerHasLives()
        {
            var other = 1 - _currentPlayer;
            return _slots[other].Lives > 0;
        }

        private void SwitchPlayer()
        {
            _currentPlayer = 1 - _currentPlayer;
            Game.Logger.Info("Flow", $"换手：轮到玩家 {_currentPlayer + 1}（过入场卡后进关）");
            // 与单人重来一致：过入场卡再进关。命数/分数由 _slots[_currentPlayer] 恢复，
            // 所以这里不需要（也不该）再去 Restore 一次。
            ReloadStageWithIntro();
        }

        // ───────────────────────── Pause ─────────────────────────

        private void EnterPause()
        {
            Game.Logger.Info("Flow", "→ Pause");
            // "继续 (ESC)" 根本按不动 —— 因为 ESC 要通过 Game.Input 读，而 Lock 正是把它关掉。
            // 不锁也不会让马里奥乱跑：暂停期间状态机停在 Pause，TickStage 压根不执行，
            // 没有任何模块在读游戏输入；面板自身的点击走 EventSystem，不受影响。
            Time.timeScale = 0f;
            Game.Sound.PlaySFX(Sfx.Pause);
            Game.UI.Open<PausePanel>();
        }

        /// <summary>暂停中按 ESC 恢复（面板按钮写的就是"继续 (ESC)"，得真的能按）。</summary>
        private void TickPause(float dt)
        {
            if (Game.Input != null && Game.Input.GetKeyDown(GameKey.Escape)) OnResume();
        }

        private void ExitPause()
        {
            Time.timeScale = 1f;
            Game.Input.Unlock();
        }

        private void OnResume()
        {
            Game.UI.Close<PausePanel>();
            Game.Fsm.Transition(FlowState.Stage);
        }

        private void OnRestartLevel()
        {
            Time.timeScale = 1f;
            Game.Input.Unlock();
            Game.UI.CloseAll();
            if (_session != null)
            {
                _session.RestartAfterDeath();
                Game.Sound.PlayBGM(BgmFor(_levelIndex));
            }
            Game.Fsm.Transition(FlowState.Stage);
        }

        private void OnBackToMain()
        {
            Time.timeScale = 1f;
            Game.Input.Unlock();
            Game.UI.CloseAll();
            DisposeSessions();

            Game.Sound.StopAll();
            Game.Scene.Load(Scenes.Menu, null, ShowMenuAfterSceneLoad);
        }

        // ───────────────────────── Result / GameOver ─────────────────────────

        private void EnterResult()
        {
            Game.Logger.Info("Flow", "→ Result（关卡通过）");
            Time.timeScale = 0f;
            Game.UI.Open<ResultPanel>();

            // 这里【不能】Game.Input.Lock()。
            //
            // IsLocked 会让 GetKeyDown 直接返回 false，于是结算面板【永远按不动】，和 GameOver
            // 的卡死是同一类问题（在冻结状态下用了被冻结的东西）。
            // 冻结世界靠 timeScale=0 就够了：Result 状态下 TickStage 根本不会被调用。
        }

        private void TickResult(float dt)
        {
            // 结算面板上按确认键继续。
            if (Game.Input.GetKeyDown(GameKey.Space) || Game.Input.GetKeyDown(GameKey.Enter))
            {
                Time.timeScale = 1f;
                Game.Input.Unlock();
                Game.UI.CloseAll();

                if (_session != null)
                {
                    _slots[_currentPlayer].Points = _session.Score.Points;
                    _slots[_currentPlayer].Coins = _session.Score.Coins;
                }

                DisposeSessions();

                // 原版 1-1 通关后会进 1-2；这里只有一关可玩，所以回到标题。
                // 这是刻意的收尾（不是没做完）：单关 demo 回到标题是完整闭环。
                Game.Scene.Load(Scenes.Menu, null, () => Game.Fsm.Transition(FlowState.Menu));
            }
        }

        private void EnterGameOver()
        {
            Game.Logger.Info("Flow", "→ GameOver");
            Game.Input.Lock();
            Time.timeScale = 0f;
            Game.Sound.PlayBGM(Bgm.Overworld);
            Game.Sound.PlaySFX(Sfx.GameOver);
            Game.UI.Open<GameOverPanel>();

            // 这里必须用 Timer.AfterUnscaled，不能用 Timer.After。
            //
            // 而引擎的 Timer 是靠 Time.deltaTime 推进的 —— timeScale=0 时 deltaTime 恒为 0，
            // 普通 After 排出来的回调【永不执行】，画面就永久停在 GameOver
            // （实测卡了 30 秒以上、一条日志都没有）。
            // 引擎已为此补了不受 timeScale 影响的 AfterUnscaled。
            // 引擎改动影响所有项目，所以说明跟着**引擎代码**走，不放业务项目里。
            Game.Timer.AfterUnscaled(GameOverStaySeconds, () =>
            {
                Time.timeScale = 1f;
                Game.Input.Unlock();
                Game.UI.CloseAll();
                DisposeSessions();
                Game.Scene.Load(Scenes.Menu, null, () => Game.Fsm.Transition(FlowState.Menu));
            });
        }

        /// <summary>GameOver 画面停留时长（秒），到点自动回标题。</summary>
        private const float GameOverStaySeconds = 4f;
    }
}
