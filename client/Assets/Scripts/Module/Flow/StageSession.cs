using System;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Def;
using SuperMario.Module.Audio;
using SuperMario.Module.CameraRig;
using SuperMario.Module.Entities;
using SuperMario.Module.Gameplay;
using SuperMario.Module.Level;
using SuperMario.Module.Player;
using SuperMario.Module.Score;
using UnityEngine;

namespace SuperMario.Module.Flow
{
    /// <summary>
    /// 一局关卡的会话：持有这一局里的所有模块，并负责它们的生死。
    /// <para>
    /// 抽出来单独一个类（而不是散在流程状态里）是因为"一局"有明确的起止：
    /// 进关时构建，死亡重进时**只重建玩家与实体**（关卡地形不重建），
    /// 退回主菜单时整体销毁。这三件事都需要一个统一的所有者，
    /// 否则很容易出现"回主菜单后旧关卡还在跑"这类泄漏。
    /// </para>
    /// </summary>
    internal sealed class StageSession
    {
        public bool Ready { get; private set; }
        public ILevel Level { get; private set; }
        public IPlayer Player { get; private set; }
        public IScore Score { get; private set; }
        public ICameraRig Camera { get; private set; }
        public IGameplay Gameplay { get; private set; }

        private Transform _entityRoot;
        private EnemyModule _enemies;
        private BlockModule _blocks;
        private ItemModule _items;
        private FireballModule _fireballs;
        private AudioModule _audio;
        private LevelData _data;

        private int _pendingPreloads;

        public IAudio Audio => _audio;

        /// <summary>
        /// 构建整局：先读关卡数据 → 建地形 → 预加载实体贴图 → 铺实体 → 生成玩家。
        /// <para>
        /// 顺序不能换：实体贴图要先加载完才好铺（否则第一帧方块是空白的），
        /// 而玩家必须在方块登记进实心位图之后才生成，否则他会掉穿脚下的方块。
        /// </para>
        /// </summary>
        /// <param name="sharedScore">
        /// 非 null 时**共用**这一份分数/金币/命数/时间（管中密室用）。
        /// <para>
        /// 进金币房不该重置时间、出来也不该把分数洗掉 —— 原版里密室只是一关之内的一个场景。
        /// </para>
        /// </param>
        public void Build(Action onReady, IScore sharedScore = null)
        {
            Ready = false;

            _audio = new AudioModule();
            // 没有给共享分数才新建（新建 = 开一关，时间恢复满）。
            Score = sharedScore ?? new ScoreModule();
            if (sharedScore == null) Score.ResetForLevel();

            var text = LoadLevelText();
            // 读【本局】关卡路径，而不是写死 1-1 —— 路径由流程层在进关前通过
            // StageContext.SetLevel 设好（1-1 通关后会换成 1-2）。
            _data = LevelData.Parse(text, StageContext.LevelPath);

            var levelModule = new LevelModule();
            Level = levelModule;

            _entityRoot = new GameObject("[Entities]").transform;

            levelModule.Build(_data, () =>
            {
                // 关卡地形就绪后再装实体与玩家。
                Camera = new CameraModule();
                _enemies = new EnemyModule(Level, _audio, _entityRoot);
                _items = new ItemModule(Level, _audio, _entityRoot);
                _fireballs = new FireballModule(Level, _audio, _entityRoot);
                _blocks = new BlockModule(Level, _audio, Score, _items, _enemies, _entityRoot);

                var playerModule = new PlayerModule(Level, _audio);
                Player = playerModule;

                // 三组预加载并行，全部完成才继续。
                _pendingPreloads = 3;
                Action step = () =>
                {
                    if (--_pendingPreloads > 0) return;
                    PopulateEntities();

                    // 地下关（1-2）的青色砖：关卡文件里它们是【地形瓦片】
                    // （789 块 WorldTileSprites_1），而地形瓦片只参与碰撞、顶不碎 ——
                    // 这里把它们换成 Brick 实体：实体自己会登记进实心位图（碰撞完全不变），
                    // 但可以被顶、被大马里奥顶碎（原版 1-2 的砖本来就是能打碎的，
                    // 打穿天花板才进得去隐藏区）。
                    // LevelModule 那边会跳过这些格的地形绘制，改由方块自己画（避免重影）。
                    SpawnUndergroundBricks();

                    SpawnPlayer(playerModule);
                    Gameplay = new GameplayModule(Level, Player, _enemies, _blocks, _items, _fireballs,
                        Score, _audio, _entityRoot, _enemies, _items, _fireballs);
                    Camera.Setup(Level, Player);

                    // 把这一局挂到全局视图上，HUD 之后从 StageContext 读数值。
                    // 关卡名取 StageContext.WorldLabel（HUD 上就是这一行），不再是写死的 "1-1"。
                    StageContext.Bind(Score, Player, Level, StageContext.WorldLabel);

                    Ready = true;
                    Game.Logger.Info("Flow", "关卡会话就绪");
                    onReady?.Invoke();
                };

                _blocks.Preload(step);
                _items.Preload(step);
                _fireballs.Preload(step);
            });
        }

        /// <summary>1-2 的升降台（在 <c>PopulateEntities</c> 里随关卡一起创建）。</summary>
        private MovingPlatformModule _platforms;

        /// <summary>
        /// 地下关（1-2）的青砖：把地形瓦片换成 Brick 实体（详见调用点的注释）。
        /// <para>
        /// 判据是 <c>StageContext.BrickTilesAsEntities</c>（地下关且不是管中密室）——
        /// 金币房的青砖是房间的墙，转成实体会让人把墙顶碎走出去。
        /// </para>
        /// </summary>
        private void SpawnUndergroundBricks()
        {
            if (!StageContext.BrickTilesAsEntities || _data == null) return;

            var count = 0;
            foreach (var t in _data.Tiles)
            {
                if (t.Layer != 0) continue;
                if (t.Sprite != SpriteNames.TileUndergroundBrick) continue;
                _blocks.Spawn(EntityKind.Brick, new Vector2Int(t.X, t.Y));
                count++;
            }
            Game.Logger.Info("Flow", $"地下关青砖转为实体砖块：{count} 块（可顶碎）");
        }

        private static string LoadLevelText()
        {
            // 走引擎资源缓存同步取（由 AppFlow.EnterBoot 启动期 Preload 进来）。
            // 之前这里是 Resources.Load —— 绕开了 Game.Res 的缓存/根前缀/卸载策略，
            // 属于"为了同步读一行而自己找存储"。引擎补齐 TryGet<T> 后就不需要绕了。
            var ta = Game.Res?.TryGet<TextAsset>(StageContext.LevelPath);
            if (ta == null)
            {
                // 关卡文件缺失等于这一局没法玩。这里必须吵到无法忽视。
                Game.Logger.Error("Flow",
                    $"关卡文本未就绪：{StageContext.LevelPath}（应由启动期 Preload 装入）—— 将得到一个空关卡");
                return string.Empty;
            }
            return ta.text;
        }

        private void PopulateEntities()
        {
            // 平台要往关卡位图里登记实心格，所以在关卡建好之后才能创建。
            _platforms = new MovingPlatformModule(Level, _entityRoot);

            var goomba = 0; var brick = 0; var box = 0; var boxMushroom = 0;
            var koopa = 0; var piranha = 0; var coin = 0; var boxOneUp = 0; var platform = 0;
            var star = 0; var multiCoin = 0; var hiddenOneUp = 0;
            foreach (var e in _data.Entities)
            {
                // 实体位置是格子中心（80.5 表示第 80 格）。方块占满整格，
                // 所以格坐标就是 floor(中心)。用 FloorToInt 而不是 RoundToInt：
                // 中心 80.5 属于第 80 格，四舍五入会得到 81，整排方块就会错位一格。
                var tx = Mathf.FloorToInt(e.X);
                var ty = Mathf.FloorToInt(e.Y);

                switch (e.Prefab)
                {
                    case "Goomba":
                        // 栗宝宝的脚底 = 它所在格的底边。
                        _enemies.SpawnGoomba(new Vector2(tx + 0.5f, ty));
                        goomba++;
                        break;
                    case "Brick":
                        _blocks.Spawn(EntityKind.Brick, new Vector2Int(tx, ty));
                        brick++;
                        break;
                    case "MysteryBox":
                        _blocks.Spawn(EntityKind.QuestionBlock, new Vector2Int(tx, ty));
                        box++;
                        break;
                    case "MysteryBoxMushroom":
                        _blocks.Spawn(EntityKind.QuestionBlockMushroom, new Vector2Int(tx, ty));
                        boxMushroom++;
                        break;
                    case "MysteryBoxOneUp":
                        _blocks.Spawn(EntityKind.QuestionBlockOneUp, new Vector2Int(tx, ty));
                        boxOneUp++;
                        break;
                    case "BrickStarman":
                        _blocks.Spawn(EntityKind.BrickStarman, new Vector2Int(tx, ty));
                        star++;
                        break;
                    case "BrickMultiCoin":
                        _blocks.Spawn(EntityKind.BrickMultiCoin, new Vector2Int(tx, ty));
                        multiCoin++;
                        break;
                    case "HiddenBoxOneUp":
                        _blocks.Spawn(EntityKind.HiddenBoxOneUp, new Vector2Int(tx, ty));
                        hiddenOneUp++;
                        break;
                    case "Koopa":
                        _enemies.SpawnKoopa(new Vector2(tx + 0.5f, ty));
                        koopa++;
                        break;
                    case "Piranha":
                        // 食人花长在**管口正中间**（它自己在格内上下伸缩）。
                        //
                        // x 不是 `tx + 0.5`（"所在格中心"）—— 食人花所在的管子是**跨 2 格**的
                        // （数据里的格 x 是管子左边那一格，见 `E 0.5 0` 对应 `T 0/1 …`），
                        // 而 clone 里食人花与它那根管子摆在**同一个 x**（都取 prefab 的 root x），
                        // 也就是**管子的中线**。本工程的格把 2 格宽管的中线落在**格边界**上，
                        // 所以中线的世界 x = `tx + 1`。
                        // 实测后果（旧写法 `tx + 0.5`）：人偏在管子左半边 —— 管口左缘会露出食人花
                        // 一条约 2 像素的边（用户实测："管道没把食人花完全挡住"），
                        // 而且它伸出来的时候也不在管口中央。
                        _enemies.SpawnPiranha(new Vector2(tx + 1f, ty + 0.5f));
                        piranha++;
                        break;
                    case "Coin":
                        _items.SpawnCoin(new Vector2(tx + 0.5f, ty + 0.5f));
                        coin++;
                        break;
                    case "MovingPlatform":
                        // 行程必须随数据给（prefab 的 Up/Down Stop + 实例的 Spawn Pos / directionY，
                        // 出处见 Levels/World1-2.txt 文件头）。缺了就【不生成】并报 Error ——
                        if (!e.HasPatrol)
                        {
                            Game.Logger.Error("Flow",
                                $"移动平台缺少行程参数，已跳过该实例：E {e.X} {e.Y} MovingPlatform —— " +
                                "应写成 `E <x> <y> MovingPlatform <downStopY> <upStopY> <startDir>`" +
                                "（值取自 clone 的 Moving Platform Vertical Spawner，见关卡文件头）");
                            break;
                        }
                        // 这里**不能**像别的实体那样按格心 `floor(x)+0.5` 落位：
                        // 原版这一件的 `Spawn Pos` 是 **clone 场景的世界坐标**（1-1 是 `(152.8, −4)`、
                        // 1-2 是 `(137.8, 14)`，出处 `World 1-2.unity:1588/:9629` 的实例覆写），
                        // 它的 x 本来就不在格心上 —— 落格心会差 **0.3 格**（对照表 E-21 ③）。
                        // 所以升降台直接用数据里的原始 x/y（= spawner 的世界坐标 = 台面中心）。
                        _platforms.Spawn(new Vector2(e.X, e.Y), e.DownStopY, e.UpStopY, e.StartDir);
                        platform++;
                        break;
                    default:
                        Game.Logger.Warn("Flow", $"关卡里出现未知实体类型：{e.Prefab}");
                        break;
                }
            }

            Game.Logger.Info("Flow",
                $"实体已铺开：栗宝宝 {goomba}、乌龟 {koopa}、食人花 {piranha}、金币 {coin}、" +
                $"砖块 {brick}、问号块 {box}、道具块 {boxMushroom}、1-UP 块 {boxOneUp}、移动平台 {platform}、" +
                $"★无敌星砖 {star}、多金币砖 {multiCoin}、隐形 1-UP 块 {hiddenOneUp}");
        }

        private void SpawnPlayer(PlayerModule playerModule)
        {
            playerModule.Preload(() =>
            {
                // 出生点：关卡最左的可站地面上方两格。
                // 不用关卡文件里的 Mario 实体位置（那个位置是原版教程关摆的，换关卡就不对了）。
                // 出生点：管中密室用落点覆盖（元素表给的），关卡文件声明了 `# spawn` 的用它
                // （原版每个场景都摆了 Spawn Point —— 1-2 地表段是 (0.5,1.5)，正好在出管口里），
                // 其余才回到"关卡最左 + 3.5 格"的通用规则。
                var pos = StageContext.SpawnOverride
                          ?? (Level.HasSpawn
                              ? new Vector2(Level.SpawnX, Level.SpawnY)
                              : new Vector2(Level.MinWorldX + 3.5f, Level.GroundTopY));
                // 形态跟着"上一段"走（原版同一只马里奥）；没有交接时就是小马里奥。
                // 见 StageContext.SpawnPower（1-1→1-2、1-2 两段之间、进出管中密室都要保留）。
                playerModule.Spawn(pos, StageContext.SpawnPower ?? PowerState.Small);
                if (StageContext.SpawnOverride.HasValue)
                    // 由 PipeWarpTable 按当前关卡给出（各那一套的 Source 里带着出处）。
                    Game.Logger.Info("Flow",
                        $"使用落点覆盖：({pos.x:F1},{pos.y:F1})（管中密室落点，见 PipeWarpTable：{PipeWarpTable.Current.Source}）");
            });
        }

        // ───────────────────── 管中密室：主关卡的冻结与恢复 ─────────────────────

        /// <summary>隐藏期间保存下来的旗子 Transform（见 <see cref="Resume"/>）。</summary>
        private Transform _savedFlag;

        /// <summary>
        /// **冻结**本局（进管中密室时用）：整套对象关掉，但一个都不销毁。
        /// <para>
        /// 为什么必须留活口：原版进金币房再出来，主关卡的一切都还在原地 ——
        /// 顶碎的砖、踩掉的敌人、走过的位置。重新加载一遍关卡会让这些全部复原
        /// （而且马里奥会回到起点，可我们是要从远处的出场管出来）。
        /// </para>
        /// <para>
        /// 关掉 GameObject 同时会停掉挂在它们身上的 Update（方块顶起动画、敌人移动、玩家物理），
        /// 所以不需要另加"暂停"开关；流程层只要不再 Tick 这个会话即可（见 <c>AppFlow</c> 的活动会话）。
        /// </para>
        /// </summary>
        public void Hide()
        {
            // 旗子要单独记住：StageContext 在密室会话销毁时会被 Unbind（旗子置空），
            // 恢复时要把它还回去，否则第二次滑杆时旗子不动（`PlayerActor.EnsureFlag` 只剩一条 Warn）。
            _savedFlag = StageContext.Flag;

            if (Level != null && Level.Root != null) Level.Root.SetActive(false);
            if (_entityRoot != null) _entityRoot.gameObject.SetActive(false);
            Player?.SetVisible(false);
            if (Player != null) Player.ControlsEnabled = false;

            Game.Logger.Info("Flow", "主关卡已冻结（保留状态，进管中密室）");
        }

        /// <summary>
        /// 恢复本局（出管回主关卡时用）：对象重新打开、相机与全局视图复位。
        /// <para>
        /// 玩家的位置不在这里给：由流程层紧接着调 <c>StartPipeRise</c>（从管里顶出来），
        /// 位置与过场是同一件事，分两处写必然对不齐。
        /// </para>
        /// </summary>
        public void Resume()
        {
            if (Level != null && Level.Root != null) Level.Root.SetActive(true);
            if (_entityRoot != null) _entityRoot.gameObject.SetActive(true);
            Player?.SetVisible(true);
            if (Player != null) Player.ControlsEnabled = true;

            // 相机每局都要重设（背景色 + 取景 + 下一帧归位）：密室是黑底，直接复用会留在黑底上。
            Camera?.Setup(Level, Player);

            // 全局视图指回主关卡（密室会话 Dispose 时被 Unbind 过）。
            StageContext.Bind(Score, Player, Level, StageContext.WorldLabel);
            StageContext.SetFlag(_savedFlag);

            Game.Logger.Info("Flow", $"主关卡已恢复（玩家 {Player?.FeetPosition}）");
        }

        /// <summary>死亡后重来：重置玩家与所有动态实体，地形与分数保持。</summary>
        public void RestartAfterDeath()
        {
            _enemies?.Clear();
            _blocks?.Clear();
            _items?.Clear();
            _fireballs?.Clear();
            _platforms?.Clear();
            PopulateEntities();

            Player.Spawn(new Vector2(Level.MinWorldX + 3.5f, Level.GroundTopY));
            Score.ResetForLevel();
            Gameplay.ResetAfterDeath();
            Game.Logger.Info("Flow", "死亡重来：玩家与实体已复位");
        }

        /// <summary>整体销毁并释放（回主菜单）。</summary>
        public void Dispose()
        {
            _enemies?.Clear();
            _blocks?.Clear();
            _items?.Clear();
            _fireballs?.Clear();
            Camera?.Clear();
            Gameplay?.Clear();

            if (Player is PlayerModule pm) pm.Clear();
            if (Level is LevelModule lm) lm.Clear();
            if (_entityRoot != null) UnityEngine.Object.Destroy(_entityRoot.gameObject);

            _audio?.StopAll();

            // 先解绑再清空：HUD 可能在关卡结束后仍收到一次刷新事件，
            // 那时它手里还是旧的 StageContext 引用（解绑后读到 null，会安全地跳过刷新）。
            StageContext.Unbind();

            Level = null;
            Player = null;
            Score = null;
            Camera = null;
            Gameplay = null;
            Ready = false;
            Game.Logger.Info("Flow", "关卡会话已释放");
        }
    }
}
