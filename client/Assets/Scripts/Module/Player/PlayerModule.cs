using System;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Def;
using SuperMario.Module.Level;
using UnityEngine;

namespace SuperMario.Module.Player
{
    /// <summary>玩家门面。外部（玩法/HUD/相机）只经过这里读状态。</summary>
    public interface IPlayer
    {
        Transform Transform { get; }
        /// <summary>当前碰撞盒（世界坐标）。</summary>
        Rect Bounds { get; }
        Vector2 Velocity { get; }
        PowerState Power { get; }
        bool Alive { get; }
        bool Grounded { get; }
        /// <summary>是否朝左（火球发射方向、贴图翻转都读它）。</summary>
        bool FacingLeft { get; }
        /// <summary>脚底中心的世界坐标（火球从胸口发出，由调用方在此基础上加偏移）。</summary>
        Vector2 FeetPosition { get; }
        /// <summary>过场（死亡 / 变身 / 通关滑杆）期间不接受操作。</summary>
        bool ControlsEnabled { get; set; }

        /// <summary>
        /// 生成 / 重生玩家（脚底世界坐标）。<paramref name="power"/> = 出生形态，默认小马里奥。
        /// <para>为什么形态要当参数传：原版**同一只马里奥跨段保留形态**（1-1→1-2、1-2 地下段→地表段、
        /// 进出管中密室），而每一"段"在本工程是一个新的 <c>StageSession</c> —— 不显式交接就会被打回 Small
        /// （登记项 E-13）。"死亡重开 / 新开一局"仍然传默认值 ⇒ 形态归零，与原版一致。</para>
        /// </summary>
        void Spawn(Vector2 worldPos, PowerState power = PowerState.Small);

        /// <summary>
        /// 原地传送（脚底世界坐标）：进出传送管、换场落点用。
        /// <para>与 <see cref="Spawn"/> 不同：**不动形态、不清无敌星** —— 那是"重开"的语义，用来传送会莫名变小。</para>
        /// </summary>
        void Teleport(Vector2 worldPos);

        /// <summary>显示 / 隐藏（进管中密室时主关卡的玩家要整块关掉，否则他的每帧物理还在跑）。</summary>
        void SetVisible(bool visible);

        /// <summary>进管过场：原地向下沉进管子（距离 = 自身贴图高度，速度见 <c>GameConst.PipeMoveSpeed</c>）。</summary>
        void StartPipeSink();

        /// <summary>出管过场：从管子内部向上顶出，结束时脚底正好站在 <paramref name="pipeTopFeet"/>。</summary>
        void StartPipeRise(Vector2 pipeTopFeet);

        /// <summary>
        /// 出管过场（起点由关卡数据给）：从 <paramref name="fromFeet"/> 升到管顶 <paramref name="pipeTopFeet"/>。
        /// <para>1-2 地表段的出管口要用它 —— 原版 Spawn Point 在管口里（见关卡文件 `# spawn` / `# pipe-rise`）。</para>
        /// </summary>
        void StartPipeRiseFrom(Vector2 fromFeet, Vector2 pipeTopFeet);

        /// <summary>侧向进管过场：向右走进管口（密室出口管用）。</summary>
        void StartPipeEnterSide();

        void Grow();
        void PowerUp(PowerState target);
        void TakeDamage();
        /// <summary>立刻死亡（掉坑 / 时间到）。</summary>
        void Kill(bool playAnimation = true);
        /// <summary>踩到敌人后的弹跳。</summary>
        void BounceAfterStomp();
        /// <summary>顶砖块后的反弹（向上撞到东西时用）。</summary>
        void BounceAfterBlockHit();
        /// <summary>
        /// 开始旗杆下滑；滑到底后自动走进城堡。
        /// <para><paramref name="castleDoorX"/> 是走进城堡的终点 —— 必须在起滑时就一起给，
        /// 因为"下滑"与"走进城堡"是同一个过场的两段，中间没有第二次调用的机会。</para>
        /// </summary>
        void StartFlagSlide(float poleBottomY, float castleDoorX);
        /// <summary>是否处于无敌星状态 —— 这期间碰到敌人是【杀敌】，不是受伤。</summary>
        bool StarInvincible { get; }
        /// <summary>吃到无敌星：一段时间内碰谁杀谁（时长见 <c>GameConst.StarInvincibleTime</c>）。</summary>
        void GrantStar();
        /// <summary>清空并销毁（回主菜单）。</summary>
        void Clear();

        /// <summary>本帧头顶撞到的实心格（没有则 null）。玩法模块消费后要清掉。</summary>
        Vector2Int? ConsumeHeadHit();
        /// <summary>变身 / 死亡动画是否在播。</summary>
        bool Busy { get; }
    }

    /// <summary>
    /// 玩家模块：创建马里奥、驱动其物理与表现。
    /// <para>
    /// 物理自己解算（不走 Unity 2D 刚体）——平台跳跃要的是「逐轴、离散、可预测」的解算，
    /// 而不是连续求解出的接触流形：后者会让同一段跳在不同帧率下落到不同位置，
    /// 而这种手感偏差在原版复刻里是致命的。
    /// </para>
    /// </summary>
    internal sealed class PlayerModule : IPlayer
    {
        public Transform Transform => _actor != null ? _actor.transform : null;
        public Rect Bounds => _actor != null ? _actor.Bounds : new Rect();
        public Vector2 Velocity => _actor != null ? _actor.Velocity : Vector2.zero;
        public PowerState Power => _power;
        public bool Alive => _alive;
        public bool Grounded => _actor != null && _actor.Grounded;
        public bool FacingLeft => _actor != null && _actor.FacingLeft;
        public Vector2 FeetPosition => _actor != null
            ? new Vector2(_actor.transform.position.x, _actor.transform.position.y)
            : Vector2.zero;
        public bool ControlsEnabled
        {
            get => _actor != null && _actor.ControlsEnabled;
            set { if (_actor != null) _actor.ControlsEnabled = value; }
        }
        public bool Busy => _actor != null && _actor.Busy;

        private readonly ILevel _level;
        private readonly Audio.IAudio _audio;
        private PlayerActor _actor;
        private PowerState _power = PowerState.Small;
        private bool _alive = true;

        private readonly SpriteSet _sprites = new SpriteSet();

        public PlayerModule(ILevel level, Audio.IAudio audio)
        {
            _level = level;
            _audio = audio;
        }

        /// <summary>预先加载三套形态的精灵；加载完再生成角色，避免第一帧是个白块。</summary>
        public void Preload(Action onDone)
        {
            var wanted = new[]
            {
                MarioAction.SmallIdle, MarioAction.SmallRun0, MarioAction.SmallRun1, MarioAction.SmallRun2,
                MarioAction.SmallJump, MarioAction.SmallSkid, MarioAction.Dead,
                MarioAction.BigIdle, MarioAction.BigRun0, MarioAction.BigRun1, MarioAction.BigRun2,
                MarioAction.BigJump, MarioAction.BigSkid, MarioAction.BigCrouch,
                MarioAction.FireIdle, MarioAction.FireRun0, MarioAction.FireRun1, MarioAction.FireRun2,
                MarioAction.FireJump, MarioAction.FireSkid, MarioAction.FireCrouch,
                MarioAction.Grow0, MarioAction.Grow1,

                // ★ 旗杆下滑用的"抱杆"两帧。
                //
                // 漏了它们的后果是【马里奥整段下滑隐形】：滑杆时取的是
                // CurrentClimbFrame()，清单里没有 ⇒ SpriteSet.Get 返回 null 贴图 ⇒
                // 画面上"旗子在降、人不见了"，人要到城堡门口才重新出现
                // （实测就是这么被指出来的：以为旗子没带人走）。
                MarioAction.SmallClimb0, MarioAction.SmallClimb1,
                MarioAction.BigClimb0, MarioAction.BigClimb1,
                MarioAction.FireClimb0, MarioAction.FireClimb1,
            };
            _sprites.Load(wanted, onDone);
        }

        public void Spawn(Vector2 worldPos, PowerState power = PowerState.Small)
        {
            if (_actor == null)
            {
                var go = new GameObject("[Mario]");
                _actor = go.AddComponent<PlayerActor>();
                _actor.Init(this, _level, _sprites, _audio);
            }

            _alive = true;
            _power = power;
            // 碰撞盒先按小马里奥建，再 ApplyPower —— 大 / 火形态的盒子由 ApplyPower 换过来，
            // 顺序反了会先按大盒子解算一帧（穿天花板）。
            _actor.ResetTo(worldPos, GameConst.SmallSize);
            _actor.ApplyPower(_power);
            Game.Logger.Info("Player",
                $"马里奥已就位：pos=({worldPos.x:F1}, {worldPos.y:F1}) 形态={_power}");
        }

        public void Teleport(Vector2 worldPos)
        {
            if (_actor == null)
            {
                // 玩家还没生成就要求传送 = 调用顺序错了（换场落点必须在 Spawn 之后给）。
                Game.Logger.Error("Player", $"传送失败：玩家尚未生成（目标 {worldPos}）");
                return;
            }
            _actor.Teleport(worldPos);
            Game.Logger.Info("Player", $"马里奥传送 → ({worldPos.x:F1}, {worldPos.y:F1})，形态={_power}");
        }

        public void SetVisible(bool visible)
        {
            if (_actor == null)
            {
                if (!visible) Game.Logger.Warn("Player", "SetVisible(false)：玩家还不存在，忽略");
                return;
            }
            _actor.SetVisible(visible);
        }

        public void StartPipeSink()
        {
            if (_actor == null) { Game.Logger.Error("Player", "进管失败：玩家尚未生成"); return; }
            _actor.StartPipeSink();
        }

        public void StartPipeRise(Vector2 pipeTopFeet)
        {
            if (_actor == null) { Game.Logger.Error("Player", "出管失败：玩家尚未生成"); return; }
            _actor.StartPipeRise(pipeTopFeet);
        }

        public void StartPipeRiseFrom(Vector2 fromFeet, Vector2 pipeTopFeet)
        {
            if (_actor == null) { Game.Logger.Error("Player", "出管失败：玩家尚未生成"); return; }
            _actor.StartPipeRiseFrom(fromFeet, pipeTopFeet);
        }

        public void StartPipeEnterSide()
        {
            if (_actor == null) { Game.Logger.Error("Player", "侧向进管失败：玩家尚未生成"); return; }
            _actor.StartPipeEnterSide();
        }

        public void Grow() => PowerUp(_power == PowerState.Small ? PowerState.Big : PowerState.Fire);

        public void PowerUp(PowerState target)
        {
            if (!_alive || _actor == null) return;
            if (target <= _power) return;

            // 小 → 大是"长大"过场；大 → 火只闪一下（原版就是这样，不做两次长大动画）。
            var growing = _power == PowerState.Small;
            _power = target;
            _actor.PlayTransform(growing ? PlayerMotion.Growing : PlayerMotion.Growing, _power);
        }

        public void TakeDamage()
        {
            if (!_alive || _actor == null) return;

            // ★ 受击后的无敌帧：这段时间内【不再受伤】。
            //
            // 踩过的坑（症状是"变大后撞一下栗宝宝直接死"）：原先没有任何无敌帧，
            // 而敌人碰撞判定是【每帧】跑的 —— 马里奥和栗宝宝重叠期间，TakeDamage 会
            // 被逐帧调用：第 1 帧 Big→Small、第 2 帧 Small→Kill。玩家看到的是一次撞击
            // 瞬间死亡，完全不像"受伤降级"。
            // 原版挨打后会闪大约 1.5~2 秒（GameConst.HitInvincibleTime）。
            // 注意：无敌帧只挡【受伤】，踩敌人照常有效（判定在敌人循环里，不经过这里）。
            if (_actor.HurtInvincible) return;
            _actor.GrantHurtInvincibility(GameConst.HitInvincibleTime);

            if (_power == PowerState.Small)
            {
                Kill();
                return;
            }

            _power = _power == PowerState.Fire ? PowerState.Big : PowerState.Small;
            _actor.PlayTransform(PlayerMotion.Shrinking, _power);

            // ★ 受伤（掉能力）音效：用户 2026-09-20 点名「受伤没有音效」。
            //   走 `Sfx.PowerDown`（= 与进管共用的那个采样，出处见 `Core/ResPaths.cs`）。
            //   ⚠️ 只在这一支响 —— 小马里奥挨打走的是上面的 `Kill()`，那一声是 `Sfx.Death`（在 AppFlow 放），
            //   两处都放就会"又掉能力又死"两声叠在一起。
            _audio?.PlaySfx(Sfx.PowerDown);
            Game.Logger.Info("Player", $"受伤降级 → {_power}（音效 {Sfx.PowerDown}）");
        }

        public void Kill(bool playAnimation = true)
        {
            if (!_alive) return;
            _alive = false;
            _actor?.PlayDeath(playAnimation);
            Game.Logger.Info("Player", "马里奥死亡");
        }

        /// <summary>
        /// 吃到无敌星。
        /// <para>
        /// 踩过的坑：这个能力在 <c>GameConst.StarInvincibleTime</c> 里【早就定义好了时长】，
        /// 但全项目没有任何代码读它 —— 常量悬空 = 功能没做。所以这颗星不是"没验"，
        /// 是压根没接线。
        /// </para>
        /// </summary>
        public void GrantStar()
        {
            if (!_alive || _actor == null) return;
            _actor.GrantStar(GameConst.StarInvincibleTime);
            Game.Logger.Info("Player", $"吃到无敌星：{GameConst.StarInvincibleTime} 秒内碰谁杀谁");
        }

        public bool StarInvincible => _alive && _actor != null && _actor.StarOn;

        public void BounceAfterStomp() => _actor?.BounceAfterStomp();
        public void BounceAfterBlockHit() => _actor?.BounceAfterBlockHit();
        public void StartFlagSlide(float poleBottomY, float castleDoorX) => _actor?.StartFlagSlide(poleBottomY, castleDoorX);
        public Vector2Int? ConsumeHeadHit() => _actor?.ConsumeHeadHit();

        public void Clear()
        {
            if (_actor != null) UnityEngine.Object.Destroy(_actor.gameObject);
            _actor = null;
            _sprites.Clear();
            _alive = true;
            _power = PowerState.Small;
        }
    }

    /// <summary>
    /// 精灵集合：一次性批量加载 + 按名字取。
    /// <para>
    /// 背景加载是**没有同步版本**的（<c>Game.Res.LoadAsset</c> 只有异步），
    /// 所以想"随用随取"就必须预先攒齐 —— 这就是 do-not-create 前先 Preload 的原因。
    /// </para>
    /// </summary>
    internal sealed class SpriteSet
    {
        private readonly System.Collections.Generic.Dictionary<string, Sprite> _map =
            new System.Collections.Generic.Dictionary<string, Sprite>();
        private int _pending;

        /// <summary>已报过"缺失"的动作，避免每帧刷屏。</summary>
        private readonly System.Collections.Generic.HashSet<string> _warned =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>
        /// 取一张已加载的精灵。
        /// <para>
        /// 取不到时**只报一次** error —— 这是补的课：原来静默返回 <c>null</c>，
        /// 于是"忘了把某个动作放进 Preload 清单"的表现是<b>角色整段隐形</b>，
        /// 且零报错零日志（旗杆下滑没有 Climb 帧就是这么隐形的）。
        /// 报一次而不是每帧报，是因为它每帧都会被调用。
        /// </para>
        /// </summary>
        public Sprite Get(string action)
        {
            if (_map.TryGetValue(action, out var s) && s != null) return s;
            if (_warned.Add(action))
                Game.Logger.Error("Player", $"精灵未加载：{action}（查 PlayerModule.Preload 清单）—— 这一帧会隐形");
            return null;
        }

        public void Load(string[] actions, Action onDone)
        {
            _pending = actions.Length;
            if (_pending == 0) { onDone?.Invoke(); return; }

            foreach (var a in actions)
            {
                Game.Res.LoadAsset<Sprite>(ResPaths.Mario(a), sp =>
                {
                    if (sp != null) _map[a] = sp;
                    else Game.Logger.Error("Player", $"精灵缺失：{ResPaths.Mario(a)}");
                    if (--_pending <= 0) onDone?.Invoke();
                });
            }
        }

        public void Clear() => _map.Clear();
    }
}
