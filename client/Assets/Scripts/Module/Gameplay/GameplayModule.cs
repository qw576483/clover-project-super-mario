using System;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Def;
using SuperMario.Module.Audio;
using SuperMario.Module.Entities;
using SuperMario.Module.Flow;        // StageContext（传送管要按"这一局是哪关 / 是不是密室"分流）
using SuperMario.Module.Level;
using SuperMario.Module.Player;
using SuperMario.Module.Score;
using UnityEngine;

namespace SuperMario.Module.Gameplay
{
    /// <summary>
    /// 玩法编排：把玩家、敌人、方块、道具、火球之间的判定集中在一处。
    /// <para>
    /// 为什么不把这些判定写进各自的模块：踩敌人要同时用到玩家的速度方向、敌人的状态、
    /// 分数、音效、玩家的反弹 —— 把它塞进玩家或敌人任何一边，都会让那个模块反向依赖另外三个。
    /// 编排层是唯一同时看得见所有模块的地方，判定放这里依赖方向才是单向的。
    /// </para>
    /// 判定频率是每帧一次（不是固定步长）：这些判定都是"重叠即触发"，
    /// 没有连续性问题，用固定步长反而引入插值误差。
    /// </summary>
    public interface IGameplay
    {
        /// <summary>本帧是否触发了玩家死亡（流程层据此走复位/减命）。</summary>
        bool PendingDeath { get; }
        /// <summary>是否已到达旗杆（流程层据此走通关过场）。</summary>
        bool ReachedFlag { get; }

        /// <summary>
        /// 本帧玩家是否触发了传送管（进 / 出金币房）；取走后置回 <c>None</c>。
        /// <para>
        /// 分工与 <see cref="PendingDeath"/> / <see cref="ReachedFlag"/> 一致：
        /// 玩法层判"发生了什么"，换场（关卡会话 + 资源）由流程层执行。
        /// </para>
        /// </summary>
        PipeWarpKind ConsumePipeWarp();

        void Tick(float dt);
        /// <summary>复活后重置判定状态（不是重建关卡）。</summary>
        void ResetAfterDeath();
        /// <summary>
        /// 时间到 / 外部强制死亡：让玩家死并【进入死亡结算】。
        /// <para>
        /// 必须走这里，不能在外面直接调 <c>Player.Kill()</c>：死亡结算靠 <see cref="PendingDeath"/> 触发，
        /// 而 <c>Kill()</c> 只把玩家标记成死亡、不置这个标记 —— 结果就是"时间到 0 之后画面永远卡住、
        /// 不重来也不 GameOver"（实测踩过）。
        /// </para>
        /// </summary>
        void KillPlayerForcibly();
        void Clear();
    }

    internal sealed class GameplayModule : IGameplay
    {
        public bool PendingDeath { get; private set; }
        public bool ReachedFlag { get; private set; }

        private readonly ILevel _level;
        private readonly IPlayer _player;
        private readonly IEnemies _enemies;
        private readonly IBlocks _blocks;
        private readonly IItems _items;
        private readonly IFireballs _fireballs;
        private readonly IScore _score;
        private readonly IAudio _audio;
        private readonly Transform _root;
        private readonly EnemyModule _enemyModule;
        private readonly ItemModule _itemModule;
        private readonly FireballModule _fireballModule;

        /// <summary>连踩加分：同一跳里连续踩敌人，分数递增（100→200→400→…，原版规则）。</summary>
        private int _stompChain;
        private bool _wasGrounded = true;
        private float _deathDelay;

        /// <summary>本帧待交给流程层的传送管事件（见 <see cref="ConsumePipeWarp"/>）。</summary>
        private PipeWarpKind _pendingWarp = PipeWarpKind.None;

        /// <summary>上一帧结束时的脚底 y（判"是不是从上面踩下来的"用，见 <see cref="TickPlayerVsEnemies"/>）。</summary>
        private float _prevFeetY;

        public GameplayModule(ILevel level, IPlayer player, IEnemies enemies, IBlocks blocks,
                              IItems items, IFireballs fireballs, IScore score, IAudio audio, Transform root,
                              EnemyModule enemyModule, ItemModule itemModule, FireballModule fireballModule)
        {
            _level = level;
            _player = player;
            _enemies = enemies;
            _blocks = blocks;
            _items = items;
            _fireballs = fireballs;
            _score = score;
            _audio = audio;
            _root = root;
            _enemyModule = enemyModule;
            _itemModule = itemModule;
            _fireballModule = fireballModule;
        }

        public void ResetAfterDeath()
        {
            PendingDeath = false;
            ReachedFlag = false;
            _pendingWarp = PipeWarpKind.None;
            _stompChain = 0;
            _deathDelay = 0f;
        }

        /// <summary>取走本帧的传送管事件（没有则返回 <c>None</c>）。</summary>
        public PipeWarpKind ConsumePipeWarp()
        {
            var kind = _pendingWarp;
            _pendingWarp = PipeWarpKind.None;
            return kind;
        }

        public void Tick(float dt)
        {
            if (PendingDeath || ReachedFlag) return;

            TickPipeWarp();
            TickFlagpole();
            TickBlockHits();
            TickFireInput();
            TickPlayerVsEnemies();
            TickShellVsEnemies();
            TickPlayerVsItems();
            TickFireballs();
            TickPitDeath();

            _enemyModule.Reap();
            _itemModule.Reap();
            _fireballModule.Reap();

            // 落地就重置连踩链（一次跳跃内的连踩才算连击）。
            if (_player.Grounded && !_wasGrounded) _stompChain = 0;
            _wasGrounded = _player.Grounded;
            // 记下这一帧结束时的脚底：下一帧判"是不是从上面踩下来的"要用（见 TickPlayerVsEnemies）。
            _prevFeetY = _player.FeetPosition.y;
        }

        // ── 传送管（主关卡 ⇄ 金币房；1-1 与 1-2 各一根）──
        /// <summary>
        /// 传送管判定。坐标一律取 <see cref="PipeWarpTable"/>（出处见那张表），**不在这里写数**。
        /// <para>
        /// · 主关卡：站在进管那根水管**顶上**按 ↓ 就进管（原版是"向下进管"）；
        /// · 金币房：站在地面上右边缘顶到出口管左面就是出管（原版是走进去）。
        /// </para>
        /// <para>
        /// 两关共用这一份判定：进管口 / 密室出口面的坐标按**当前关卡**从表里取
        /// （1-1 是 x=43..44 顶面 1、1-2 是 x=100..101 顶面 3），不再给 1-2 另写一套。
        /// </para>
        /// </summary>
        private void TickPipeWarp()
        {
            if (_pendingWarp != PipeWarpKind.None) return;
            if (!_player.Alive || _player.Busy) return;   // 过场（含正在管中移动）期间不重复触发

            var warp = PipeWarpTable.Current;
            var feet = _player.FeetPosition;

            if (StageContext.SubArea)
            {
                // 密室出口：站在地面上 + 右边缘顶到侧向管口。
                if (!_player.Grounded) return;
                if (Mathf.Abs(feet.y - _level.GroundTopY) > 0.2f) return;
                if (_player.Bounds.xMax < warp.BonusRoomExitFaceX - 0.05f) return;

                _pendingWarp = PipeWarpKind.ExitBonusRoom;
                Game.Logger.Info("Pipe",
                    $"出管：密室出口管 xMax={_player.Bounds.xMax:F2} ≥ {warp.BonusRoomExitFaceX}（站在地面 y={feet.y:F1}）");
                return;
            }

            // 1-2 地下段：走到右侧墙上的侧向管口就进【下一段】（原版是走进管口 → 地表段 Castle Cut）。
            // 坐标由关卡数据声明（`# side-exit <格x> <y>`，值来自 clone 场景的 prefab 位置），不在这里写数。
            if (_level.HasSideExit
                && _player.Grounded
                && Mathf.Abs(feet.y - _level.SideExitY) <= 0.2f
                && _player.Bounds.xMax >= _level.SideExitFaceX - 0.05f)
            {
                _pendingWarp = PipeWarpKind.EnterNextSection;
                Game.Logger.Info("Pipe",
                    $"侧向管口：xMax={_player.Bounds.xMax:F2} ≥ {_level.SideExitFaceX}（脚下 y={feet.y:F1}）→ 下一段");
                return;
            }
            // 这里**不许**在"没走到管口"时提前 return：1-2 主关现在**同时**有侧向管口（通地表段）
            // 和一根通往金币房的下管（T x=100..101），而这段代码在"进密室"判定**之前**。
            // 早先写成 `if (HasSideExit) { if (不满足) return; ... }`，站在金币房那根管顶按 ↓ 毫无反应
            // 12-10「1-2 秘密金币房」因此变成**不可达**。改成"只在实际触发时才 return"。

            // 只有 1-1 与 1-2 各有一根通往密室的管子（1-2 的是 T x=100..101 那根 2x3 Down）。
            if (StageContext.LevelPath != ResPaths.Level11 && StageContext.LevelPath != ResPaths.Level12) return;
            if (!_player.Grounded) return;
            if (!DownPressed()) return;
            if (feet.x < warp.EntryPipeMinX || feet.x > warp.EntryPipeMaxX) return;
            if (Mathf.Abs(feet.y - warp.EntryPipeTopY) > 0.2f) return;

            _pendingWarp = PipeWarpKind.EnterBonusRoom;
            Game.Logger.Info("Pipe",
                $"进管：站在{warp.EntryPipeDesc}顶（T x={feet.x:F1}, y={feet.y:F1}）按下 ↓ → 目标 {warp.BonusLevelPath}");
        }

        /// <summary>是否按下了"向下"（原版的进管键；小马里奥不能蹲，所以进管用按下而不是按住蹲）。</summary>
        private static bool DownPressed()
            => Game.Input != null
               && (Game.Input.GetKeyDown(GameKey.DownArrow) || Game.Input.GetKeyDown(GameKey.S));

        // ── 旗杆 ──
        private void TickFlagpole()
        {
            // 金币房没有旗杆：旗杆位置是由关卡长度推出来的（右边界的 25.5 格），
            // 密室只有 17 格宽 —— 用同一个公式会算出一根在关卡外面的旗杆，
            // 判定条件一进房就恒真（马里奥一出现就被判通关）。
            if (StageContext.SubArea) return;
            // 本关没有旗杆（1-2 地下段，见 LevelData.HasFlagpole）：不判定。
            // 这一步必须显式挡掉：否则拿"公式推出来的旗杆"当终点，过关时会把人送进墙里。
            if (!_level.HasFlagpole) return;

            // 判定用 FlagpoleTouchX（旗杆格的【左边缘】），不是 FlagpoleX（旗杆中心）。
            // 旗杆立在自己的实心基座上，马里奥右边缘最多只能顶到基座左边缘，
            // 用中心判定会永远差半格 —— 实测后果是这一关永远无法通关，且零报错。
            if (_player.Bounds.xMax < _level.FlagpoleTouchX) return;
            ReachedFlag = true;
            _score.Add(GameConst.ScoreFlag);
            // 抓杆那笔分要**在抓杆处**冒出来（用户点名要的表现）。
            // 位置取马里奥：原版 clone 的旗杆**压根不加分**（`MarioReachFlagPole()` 只停表 + 滑杆），
            // 所以这条没有 clone 位置可抄；照"分数冒在事件发生对象上"的惯例取马里奥本身。
            _items.SpawnScoreText(GameConst.ScoreFlag, _player.Bounds.center);
            _audio.PlaySfx(Sfx.Flagpole);
            _player.StartFlagSlide(_level.GroundTopY, _level.CastleDoorX);
            Game.Logger.Info("Gameplay", "到达旗杆，开始下滑");
        }

        // ── 发射火球 ──
        private void TickFireInput()
        {
            if (_player.Power != PowerState.Fire) return;
            if (!_player.Alive || _player.Busy) return;
            // 发射键 = W（用户指定）。
            // 实测表现就是"有火球但不知道怎么发"。W 和方向键同手可及，且本项目
            // 用不到"向上"（原版 SMB 也没有向上走），不会和移动冲突。
            if (Game.Input == null || !Game.Input.GetKeyDown(GameKey.W)) return;
            _fireballs.Fire(_player.FeetPosition, _player.FacingLeft);
        }

        // ── 顶砖块 ──
        private void TickBlockHits()
        {
            var hit = _player.ConsumeHeadHit();
            if (hit == null) return;

            var block = _blocks.At(hit.Value);
            if (block == null) return;

            // 这里【不要】再写"顶死砖上的敌人 / 收走砖上的金币"那两条规则 ——
            //    它们已经在 `Block.HitFromBelow`（`BlockModule.cs`）里实现了，那里连 clone 出处都有：
            //      · 敌人 = `RegularBrickBlock.cs:31-34` / `_common/CollectibleBlock.cs:38-46`
            //      · 金币 = `RegularBrickBlock.cs:36-40`（在砖上方 2 格弹出金币动画 + 飘分 + 音效 + 计币）
            //    结果 **抢在 Block 之前** 把它标成 Taken ⇒ Block 那边 `if (it.Taken) continue;` 整段跳过
            //    ⇒ 金币"直接消失、没有弹出动画也没有飘分"（用户原话）。**同一件事只能有一处实现。**
            block.HitFromBelow(_player.Power != PowerState.Small);
            // 大形态撞碎砖块时不给反弹（方块都没了，还弹一下会很怪）。
            if (block.Solid) _player.BounceAfterBlockHit();
        }

        // ── 玩家 × 敌人 ──
        private void TickPlayerVsEnemies()
        {
            var pb = _player.Bounds;
            // 本帧有没有踩中过敌人 —— 用来把"弹跳"推迟到循环之后（见方法末尾的长注释）。
            var stompedAny = false;
            foreach (var e in _enemies.Active)
            {
                if (e.Dead) continue;
                var eb = e.Bounds;
                if (!pb.Overlaps(eb)) continue;

                if (!_player.Alive) return;

                // 无敌星：碰谁杀谁 —— 不需要"踩"，从侧面撞也算（原版如此）。
                // 这条判定必须放在"踩 / 受伤"【之前】：无敌期间碰到敌人不该受伤。
                if (_player.StarInvincible)
                {
                    e.Flip();                       // 原版被星星撞死的敌人是翻飞出去的
                    AddKillScore("无敌星撞飞敌人", e.Bounds.center);
                    continue;
                }

                // 判定"踩"：玩家在下落，且（脚底高于敌人中心 **或** 上一帧脚底已经在敌人顶面之上）⇒ 从上方踩中。
                // 用中心而不是"完全在敌人上方"，是为了让擦边踩也能成立 ——
                // 严格判定会让原版那种"贴着边踩下去"的操作失效。
                //
                // 后半条"上一帧脚底在敌人顶面之上"是**从上面下来**的兜底：单帧最大下落 0.4 格
                //   （`MaxFallSpeed` 24 格/秒 ÷ 60 帧），快速下落时一帧就可能从"还没接触"直接落进敌人身体里，
                //   只看当前帧的脚底位置会把它判成"侧面撞" ⇒ **明明踩到了却受伤**。
                //   用户实测的原话：「你的怪物是不是碰撞太严苛了…只要在上面差不多就能踩到」。
                //   判据用"上一帧脚底 ≥ 敌人顶面"（不含魔数容差）：上一帧人还在敌人头顶之上，
                //   这一帧才重叠 —— 那就是从上面下来的。
                //
                var cameFromAbove = _prevFeetY >= eb.yMax;
                var stomping = _player.Velocity.y <= 0.1f
                               && (pb.y > eb.center.y || cameFromAbove)
                               && e.Stompable;

                if (stomping)
                {
                    e.Stomp();
                    // 不在这里弹：见循环末尾的说明（弹跳会把 `Velocity.y` 抬成正值，
                    //   从而污染**同帧**后面那只敌人的"是不是踩"判定）。
                    stompedAny = true;
                    AddKillScore("踩中敌人", e.Bounds.center);
                }
                else
                {
                    // 先问敌人："这次侧面接触你自己处理掉了吗？"
                    //
                    // TakeDamage()，而 Koopa.TouchFromSide() 的语义恰恰是"静止的壳被碰 = 踢出去，
                    // 不该伤马里奥" —— 那个方法写好了却【从来没有人调】（又是"定义了没接线"）。
                    if (e.TouchFromSide()) continue;

                    _player.TakeDamage();
                    if (!_player.Alive)
                    {
                        BeginDeath();
                        return;
                    }
                }
            }

            // 踩敌的弹跳必须在**整个敌人循环跑完之后**再给 —— 不能在循环中间给。
            //
            //   `BounceAfterStomp()` 会把竖直速度设成 +15（向上），而本循环判"是不是踩"的第一条判据
            //   就是 `Velocity.y <= 0.1`（在下落中）—— 于是循环里**排在后面**的那只敌人立刻被算成
            //   "上升中侧撞" ⇒ `TakeDamage()`（小马里奥 = 直接死）。
            //   马里奥横跨两只敌人接缝落下时，一帧内必然同时重叠两只 ⇒ 稳定复现。
            //   `22:39:03.615 [Gameplay] 踩中敌人，+100（连击 1）` + `[Player] 受伤降级 → Small`
            //   `22:39:49.630 [Gameplay] 踩中敌人，+100` + `.631 [Player] 受伤降级 → Small`
            // 挪到循环之后：一帧内重叠的敌人**都按"从上面踩"判**（原版就是"一脚踩死并排两只"），
            // 弹跳只给一次，连击照常递进（100 → 200）。
            if (stompedAny) _player.BounceAfterStomp();
        }

        // ── 滑行的龟壳 × 其他敌人 ──
        /// <summary>
        /// 被踢出去的壳撞到别的敌人 ⇒ 把对方撞飞（原版规则），壳自己继续滑。
        /// <para>
        /// 与 <c>Koopa.IsMovingShell</c> 早就写好了，接口注释还写着"由玩法层查询"，
        /// 但<b>全项目没有任何调用者</b> —— 典型的"定义了没接线"，而且零报错。
        /// </para>
        /// </summary>
        private void TickShellVsEnemies()
        {
            foreach (var shell in _enemies.Active)
            {
                if (!(shell is IMovingShell ms) || !ms.IsMovingShell) continue;

                foreach (var victim in _enemies.Active)
                {
                    if (ReferenceEquals(victim, shell)) continue;
                    if (victim.Dead) continue;
                    if (!ms.Bounds.Overlaps(victim.Bounds)) continue;

                    victim.Flip();
                    AddKillScore("龟壳撞飞敌人", victim.Bounds.center);
                }
            }
        }

        /// <summary>
        /// 击杀一个敌人的加分。原版的连击序列是
        /// <c>100 / 200 / 400 / 800 / 1000 / 2000 / 4000 / 8000 / 1-UP</c>，
        /// 而且<b>踩敌与无敌星撞飞共用同一条连击链</b>（原版就是一个计数器）。
        /// <para>
        /// <paramref name="worldPos"/> = **被杀那只敌人**的位置 —— 这一行分飘在那儿
        /// （clone `LevelManager.cs:347/354/361/367/374` 全是
        /// <c>AddScore(enemy.xxxBonus, enemy.gameObject.transform.position)</c>）。
        /// 本工程实体轴心是"底部居中"（见 <c>EnemyModule</c>），所以给**盒子中心** ——
        /// 与那行字在画面上居中于敌人的观感一致（预制体锚点是 MiddleCenter）。
        /// </para>
        /// </summary>
        private void AddKillScore(string reason, Vector2 worldPos)
        {
            _stompChain = Mathf.Min(_stompChain + 1, 9);

            if (_stompChain >= 9)
            {
                _score.AddLife();
                // 第 9 连击是 1-UP：那行字写 "1UP"（不是数字）——
                // 出处 clone `LevelManager.cs:518-522 AddLife(pos)` → `CreateFloatingText("1UP", pos)`。
                _items.SpawnText("1UP", worldPos);
                Game.Logger.Info("Gameplay", $"{reason}：1-UP（连击 {_stompChain}）");
                return;
            }

            var pts = _stompChain switch
            {
                1 => GameConst.ScoreStomp,
                2 => 200,
                3 => 400,
                4 => 800,
                5 => 1000,
                6 => 2000,
                7 => 4000,
                _ => 8000,
            };
            _score.Add(pts);
            _items.SpawnScoreText(pts, worldPos);
            Game.Logger.Info("Gameplay", $"{reason}，+{pts}（连击 {_stompChain}）");
        }

        // ── 玩家 × 道具 ──
        private void TickPlayerVsItems()
        {
            var pb = _player.Bounds;
            foreach (var it in _items.Active)
            {
                if (it.Taken || it.Bounds.xMax <= it.Bounds.xMin) continue;
                if (!pb.Overlaps(it.Bounds)) continue;

                switch (it.Content)
                {
                    // 静置金币（金币房里那 19 枚、1-2 空中那些）。
                    // 金币消失、**分数与金币数都不动**（"吃了币但没涨"），而且零报错。
                    // 金币房的意义就是吃币，所以这条必须在。
                    // 它**不飘字**：原版静置金币走的是无位置的 `AddCoin()`（clone `Coin.cs`），
                    // 只有从方块里蹦出来的那枚才飘（那条在 `BlockModule` 里）。
                    case BlockContent.Coin:
                        _score.AddCoin();
                        _score.Add(GameConst.ScoreCoin);
                        _audio.PlaySfx(Sfx.Coin);
                        break;

                    case BlockContent.Mushroom:
                        _player.Grow();
                        _score.Add(GameConst.ScorePowerup);
                        // 那行 1000 飘在**马里奥身上**，不是道具上 —— 出处 clone
                        // `LevelManager.cs:200-260`：`MarioPowerUp()` 是
                        // `AddScore(powerupBonus, mario.transform.position)`（吃药 / 吃花 / 吃星三条同款）。
                        _items.SpawnScoreText(GameConst.ScorePowerup, _player.Bounds.center);
                        break;
                    case BlockContent.FireFlower:
                        _player.PowerUp(PowerState.Fire);
                        _score.Add(GameConst.ScorePowerup);
                        _items.SpawnScoreText(GameConst.ScorePowerup, _player.Bounds.center);
                        break;
                    case BlockContent.OneUp:
                        _score.AddLife();
                        // 1-UP 蘑菇的字飘在**蘑菇上方 2 格** —— 出处 clone `OneupMushroom.cs:21`
                        // `AddLife(gameObject.transform.position + Vector3.up * 2)`。
                        _items.SpawnText("1UP", it.Bounds.center + Vector2.up * 2f);
                        Game.Logger.Info("Gameplay", "吃到 1-UP 蘑菇，+1 命");
                        break;
                    case BlockContent.Star:
                        _player.GrantStar();
                        _score.Add(GameConst.ScorePowerup);
                        _items.SpawnScoreText(GameConst.ScorePowerup, _player.Bounds.center);
                        break;
                }
                it.Collect();
            }
        }

        // ── 火球 × 敌人 ──
        private void TickFireballs()
        {
            foreach (var fb in _fireballs.Active)
            {
                if (fb.Dead) continue;
                foreach (var e in _enemies.Active)
                {
                    if (e.Dead) continue;
                    if (!fb.Bounds.Overlaps(e.Bounds)) continue;
                    e.Flip();
                    _score.Add(GameConst.ScoreStomp);
                    // 火球杀敌也飘字，位置同样是那只敌人 —— 出处 clone `LevelManager.cs:374`
                    // `AddScore(enemy.fireballBonus, enemy.gameObject.transform.position)`。
                    _items.SpawnScoreText(GameConst.ScoreStomp, e.Bounds.center);
                    fb.Explode();
                    // "火球打中了"和"火球飞过去炸在墙上"。这里补一条（每次击杀一条，不刷屏）。
                    var who = e is Component c ? c.gameObject.name : e.GetType().Name;
                    Game.Logger.Info("Gameplay", $"火球击中 {who}（x={e.Bounds.center.x:F2} y={e.Bounds.center.y:F2}）→ 翻飞，+{GameConst.ScoreStomp}");
                    break;
                }
            }
        }

        private void TickPitDeath()
        {
            if (_player.Bounds.yMin > GameConst.DeathPitY) return;
            if (!_player.Alive) return;
            Game.Logger.Info("Gameplay", $"掉出关卡（y={_player.Bounds.yMin:F1}）");
            _player.Kill();
            BeginDeath();
        }

        private void BeginDeath()
        {
            if (PendingDeath) return;
            PendingDeath = true;
        }

        /// <summary>时间到（或外部强制死亡）：死 + 进入死亡结算。见接口注释。</summary>
        public void KillPlayerForcibly()
        {
            if (!_player.Alive) return;
            Game.Logger.Info("Gameplay", "强制死亡（时间到 / 外部触发）");
            _player.Kill();
            BeginDeath();
        }

        public void Clear()
        {
            PendingDeath = false;
            ReachedFlag = false;
            _pendingWarp = PipeWarpKind.None;
            _stompChain = 0;
            _wasGrounded = true;
        }
    }
}
