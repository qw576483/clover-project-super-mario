using System;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Def;
using SuperMario.Module.Audio;
using SuperMario.Module.Flow;
using SuperMario.Module.Level;
using UnityEngine;

namespace SuperMario.Module.Player
{
    /// <summary>
    /// 马里奥本体：输入 → 物理 → 碰撞 → 表现。
    ///
    /// <para><b>坐标约定</b>：<c>transform.position</c> 是**脚底中心**，不是包围盒中心。
    /// 因为马里奥的精灵是紧裁剪的（小形态 15x17、大形态 18x34 不等），只有"脚底对齐"
    /// 才能让所有帧站在同一个高度上；用中心对齐会出现跑步时人物上下抖一像素。
    /// 精灵轴心因此统一设为底部居中（见 SpriteImportPostprocessor）。</para>
    ///
    /// <para><b>碰撞</b>：逐轴解算（先 X 后 Y），每一步把包围盒压回格边界。
    /// 不做连续碰撞检测：最大下落速度被 <see cref="GameConst.MaxFallSpeed"/> 卡住，
    /// 单帧位移不会超过半格，不会穿透。</para>
    /// </summary>
    internal sealed class PlayerActor : MonoBehaviour
    {
        public Rect Bounds { get; private set; }
        public Vector2 Velocity => _vel;
        public bool Grounded { get; private set; }
        public bool ControlsEnabled { get; set; } = true;
        public bool Busy { get; private set; }
        public bool FacingLeft => _facingLeft;

        private PlayerModule _owner;
        private ILevel _level;
        private SpriteSet _sprites;
        private IAudio _audio;
        private Transform _spriteRoot;
        private SpriteRenderer _sr;

        private Vector2 _vel;
        private Vector2 _size = GameConst.SmallSize;   // 包围盒尺寸（格）
        private bool _facingLeft;
        private bool _crouching;                       // 是否蹲着（只有大 / 火形态能蹲）
        private float _runAnimTimer;
        private int _runFrame;

        // 过场状态
        private PlayerMotion _motion = PlayerMotion.Normal;
        private float _motionTimer;
        private float _motionParam;      // 过场参数：旗杆下滑阶段表示"杆底 y"
        private float _castleDoorX;      // 过场参数：走进城堡的终点 x（必须与 _motionParam 分开存）
        private PowerState _pendingPower;
        private bool _blinkOn;

        // 无敌帧（受伤后短暂无敌，原版是这样的）
        private float _invincibleTimer;

        // 无敌星（吃到星星后的长无敌：配色循环 + 碰谁杀谁）。
        // 与 _invincibleTimer 分开存：语义不同（那个只是"挨打后短暂免伤"，不能杀敌），
        // 合成一个字段会让"受击无敌"意外具备杀敌能力。
        private float _starTimer;

        /// <summary>是否处于无敌星状态。</summary>
        public bool StarOn => _starTimer > 0f;

        /// <summary>吃到无敌星（取较大值，重复吃会延长而不是重置）。</summary>
        public void GrantStar(float seconds) => _starTimer = Mathf.Max(_starTimer, seconds);

        /// <summary>是否处于「受击后的无敌帧」（原版挨打后会闪一会儿，这期间不再受伤）。</summary>
        public bool HurtInvincible => _invincibleTimer > 0f;

        /// <summary>给一段受击无敌帧（闪烁由 <see cref="TickBlink"/> 表现）。</summary>
        public void GrantHurtInvincibility(float seconds) =>
            _invincibleTimer = Mathf.Max(_invincibleTimer, seconds);

        public void Init(PlayerModule owner, ILevel level, SpriteSet sprites, IAudio audio)
        {
            _owner = owner;
            _level = level;
            _sprites = sprites;
            _audio = audio;

            var spriteGo = new GameObject("Sprite");
            _spriteRoot = spriteGo.transform;
            _spriteRoot.SetParent(transform, false);
            _sr = spriteGo.AddComponent<SpriteRenderer>();
            _sr.sortingOrder = SortingNormal;
        }

        // ───────────────────────── 精灵排序（进/出管时"被管子挡住"靠它）─────────────────────────
        //
        // 本工程的管子是**平铺的单层瓦片**（地形 sortingOrder = 0），而原版的管子是"后半 + 前半"
        // 两层、马里奥夹在中间 —— 所以在原版里马里奥进管时会被管口挡住，而本工程默认
        // （玩家 10 > 地形 0）会**画在管子前面**：
        //   ① 下管时人能看见自己"陷"进管子里（用户实测："人下管道为什么能看到人"）；
        //   ② 出管升起时人是**从地里/管子里画着走出来的**（用户实测："出管道一瞬间会被管道弹开"）。
        // 修法与食人花（`EnemyModule` 的 `Piranha`）同一条：管中移动期间把玩家画到**地形之下**
        // （−1：在地形 0 之后、背景 −20 之前），于是"没入管口以下的部分"自然被管子/地面挡住，
        // 露在管口之上的部分照常可见 —— 就是原版那个"从管口冒出来"的观感。
        /// <summary>正常时的排序：地形 0、背景 −20、实体 5~8 ⇒ 玩家画在它们之上。</summary>
        private const int SortingNormal = 10;
        /// <summary>管中移动期间的排序：与食人花同层（地形之下、背景之上）。</summary>
        private const int SortingBehindTerrain = -1;

        private void SetSorting(int order)
        {
            if (_sr != null) _sr.sortingOrder = order;
        }

        public void ResetTo(Vector2 feetPos, Vector2 size)
        {
            _size = size;
            transform.position = new Vector3(feetPos.x, feetPos.y, 0f);
            _vel = Vector2.zero;
            Grounded = false;
            Busy = false;
            _motion = PlayerMotion.Normal;
            ControlsEnabled = true;
            _invincibleTimer = 0f;
            _starTimer = 0f;
            _facingLeft = false;
            ResetJumpParams();
            RecomputeBounds();
            SetSprite(MarioAction.SmallIdle);
            // 进城堡那一下会把贴图关掉（人是"进门"了），重生时必须打开 ——
            // 否则死亡重来 / 下一关开局马里奥是隐形的，而且看起来像"关卡没生成"。
            if (_sr != null) _sr.enabled = true;
            // 排序也要复位：上一次可能是"管中移动"（画在地形之下），不复位会一直躲在管子后面。
            SetSorting(SortingNormal);
        }

        /// <summary>
        /// 原地传送（脚底位置）：进出传送管 / 换场落点用。
        /// <para>
        /// 与 <see cref="ResetTo"/> 的区别是**不动形态、不是"重开"**：<c>ResetTo</c> 会把
        /// 形态打回小马里奥并清掉无敌星 —— 那是死亡重生的语义，用来传送会莫名变小。
        /// </para>
        /// </summary>
        public void Teleport(Vector2 feetPos)
        {
            transform.position = new Vector3(feetPos.x, feetPos.y, 0f);
            _vel = Vector2.zero;
            Grounded = false;
            _motion = PlayerMotion.Normal;
            _motionTimer = 0f;
            _pipeRemaining = 0f;
            Busy = false;
            ControlsEnabled = true;
            _crouching = false;
            ResetJumpParams();
            ApplySizeNow();
            RecomputeBounds();
            if (_sr != null)
            {
                _sr.enabled = true;
                _sr.sprite = _sprites.Get(FormIdle(_owner != null ? _owner.Power : PowerState.Small));
            }
            SetSorting(SortingNormal);
        }

        /// <summary>显示 / 隐藏（进管中密室时主关卡的玩家要整块关掉：他的 Update 才会停）。</summary>
        public void SetVisible(bool visible)
        {
            if (gameObject != null) gameObject.SetActive(visible);
        }

        public void ApplyPower(PowerState power)
        {
            // 变小 / 变大都要把蹲下状态清掉：小马里奥蹲不下，大马里奥变身时也该站直。
            _crouching = false;
            ApplySizeNow();
            RecomputeBounds();
        }

        /// <summary>按当前形态 + 是否蹲下，把碰撞盒尺寸换过来。</summary>
        private void ApplySizeNow()
        {
            if (_crouching) { _size = GameConst.CrouchSize; return; }
            _size = _owner != null && _owner.Power == PowerState.Small ? GameConst.SmallSize : GameConst.BigSize;
        }

        // ───────────────────────── 过场 ─────────────────────────

        public void PlayTransform(PlayerMotion kind, PowerState target)
        {
            _motion = kind;
            _motionTimer = 0f;
            _pendingPower = target;
            Busy = true;
            ControlsEnabled = false;
            _vel.x = 0f;
        }

        public void PlayDeath(bool withAnimation)
        {
            _motion = PlayerMotion.Dead;
            _motionTimer = 0f;
            Busy = true;
            ControlsEnabled = false;
            if (withAnimation)
            {
                _vel = new Vector2(0f, GameConst.DeathHopVelocity);
                _sr.sprite = _sprites.Get(MarioAction.Dead);
            }
            else
            {
                _vel = Vector2.zero;
            }
        }

        public void StartFlagSlide(float poleBottomY, float castleDoorX)
        {
            _motion = PlayerMotion.FlagSlide;
            _motionTimer = 0f;
            _motionParam = poleBottomY;   // 本阶段这个字段表示 Y
            _castleDoorX = castleDoorX;   // 走城堡的终点 x 单独存 —— 见 TickCastleWalk 的注释
            Busy = true;
            ControlsEnabled = false;
            _vel = new Vector2(0f, -GameConst.FlagSlideSpeed);
            _flagHalfHeight = FlagHalfHeight();

            // 吸附到旗杆上再滑。旗杆底部有实心基座，马里奥跑过来时会被基座挡在杆的左边
            // （实测停在 x≈183.6，而杆在 184.5）—— 不吸附就会"贴着空气往下滑"。
            // 原版是抱住杆的左侧下滑，所以取杆中心再往左偏一点。
            if (_level != null)
                transform.position = new Vector3(_level.FlagpoleX - 0.35f, transform.position.y, 0f);

            // 滑杆时马里奥面朝右（原版如此）。
            _facingLeft = false;
            _sr.flipX = false;
            SetSprite(CurrentClimbFrame());
        }

        // ───────────────────────── 传送管 ─────────────────────────

        /// <summary>管中移动的方向与剩余距离（格）。</summary>
        private Vector2 _pipeDir;
        private float _pipeRemaining;

        /// <summary>到位后是否保持"冻住不动"（进管用：接下来由流程层换场，不该再跑物理）。</summary>
        private bool _pipeFreezeOnEnd;

        /// <summary>
        /// 进管过场：原地向下沉进管子。
        /// <para>
        /// <b>下沉距离 = 当前贴图的高度</b>（"沉到看不见"就正好是切场时机）——
        /// 不另写一个"下沉几格"的常数，那个数没有出处；
        /// 速度有出处（<c>GameConst.PipeMoveSpeed</c>，取 clone 的传送管脚本）。
        /// 姿势用蹲姿：原版参考工程 `PipeWarpDown.cs:42` 进管时调的就是 <c>AutomaticCrouch()</c>。
        /// </para>
        /// </summary>
        public void StartPipeSink()
        {
            var distance = CurrentSpriteHeight();   // 先量站姿贴图，量完再换蹲姿
            _crouching = true;
            ApplySizeNow();
            SetSprite(FormCrouch(_owner.Power));
            BeginPipeMove(new Vector2(0f, -1f), distance, freezeOnEnd: true);
        }

        /// <summary>
        /// 出管过场：从管子内部向上顶出，结束时脚底正好站在管口顶面 <paramref name="pipeTopFeet"/>。
        /// <para>起点 = 管口顶面往下一个"身体高度"。距离用**自己的贴图高度**而不是写死的格数：
        /// "升起几格"在原版载体里没有出处（E-10），有出处的只有速度。</para>
        /// </summary>
        public void StartPipeRise(Vector2 pipeTopFeet)
        {
            _crouching = false;
            ApplySizeNow();
            // 先定贴图，再按【这张贴图】量身高：起点与移动距离必须是同一个数，
            // 否则"顶出来"会停在管口上方或下方（差的就是两张贴图的高度差）。
            SetSprite(FormIdle(_owner.Power));
            var height = CurrentSpriteHeight();
            StartPipeRiseFrom(new Vector2(pipeTopFeet.x, pipeTopFeet.y - height), pipeTopFeet);
        }

        /// <summary>
        /// 出管过场（**起点由关卡数据给**）：从 <paramref name="fromFeet"/> 升到管口顶面
        /// <paramref name="pipeTopFeet"/>（脚底正好落在管顶）。
        /// <para>
        /// 与上面那个重载只差"起点从哪来"：1-2 地表段的出管口，原版在管口里摆了一个
        /// `Spawn Point @clone (0.5,1.5)`（出处 = 关卡文件的 `# spawn` 行），人是从那个点升上来的 ——
        /// 不是从"管顶往下量一个身体高度"那个位置。两个点都出自原版数据 ⇒ 距离照样不是编的。
        /// </para>
        /// </summary>
        public void StartPipeRiseFrom(Vector2 fromFeet, Vector2 pipeTopFeet)
        {
            _crouching = false;
            ApplySizeNow();
            _facingLeft = false;
            if (_sr != null) _sr.flipX = false;
            SetSprite(FormIdle(_owner.Power));
            transform.position = new Vector3(fromFeet.x, fromFeet.y, 0f);
            RecomputeBounds();
            BeginPipeMove(new Vector2(0f, 1f), Mathf.Max(0f, pipeTopFeet.y - fromFeet.y), freezeOnEnd: false);
            Game.Logger.Info("Player",
                $"出管升起：起点=({fromFeet.x:F2},{fromFeet.y:F2}) 终点=({pipeTopFeet.x:F2},{pipeTopFeet.y:F2})" +
                $"（距离 {Mathf.Max(0f, pipeTopFeet.y - fromFeet.y):F2} 格，速度 {GameConst.PipeMoveSpeed} 格/秒）");
        }

        /// <summary>
        /// 侧向进管过场：向右**走进管口**（原版是"自动走进管口、被管口挡住才换场"）。
        /// <para>
        /// 原版出处（clone）：`PipeWarpSide.cs:29` 触发时 `mario.AutomaticWalk(mario.levelEntryWalkSpeedX)`
        /// 开始自动向右走（速度见 <c>GameConst.PipeEntryWalkSpeed</c>），`:37` 的 `OnCollisionEnter2D`
        /// 撞到管子本体才换场。
        /// </para>
        /// <para>
        /// ⛔ 走距**不能**是"走到管口面为止"：管口面（`BonusRoomExitFaceX` / `# side-exit`）正是
        /// 管口那 2 格**开口的左边沿**，走到那里人还整整齐齐站在管子外面 ——
        /// 用户实测的原话就是「出管道时候进管道效果没有，人就卡在管道外，然后操作不了」：
        /// 走距 0 ⇒ 过场一步没走（`TickPipeMove` 空转）⇒ 流程层等 3 秒兜底超时强制换场，
        /// 人在这 3 秒里既不动也不能操作。日志原文（2026-09-19 20:48:51.648）：
        /// `侧向进管：管口面 x=-4.00 … 当前右边缘 x=-4.00 ⇒ 走距 0.00 格` → `管中移动开始 距离=0.00`
        /// → 3.010 秒后 `[Warn] 管中过场超时（3 秒，OutOfRoom）—— 强制结束该过场`。
        /// </para>
        /// <para>
        /// 所以走距 = "从当前右边缘走到**管口内侧**为止"，管口宽度取
        /// <see cref="PipeWarpInfo.SidePipeMouthTiles"/>（= clone 预制体里管口那张图 `pipe_green_top_side`
        /// 的 `m_Size: {x: 2, y: 2}` ⇒ 2 格宽）；管中移动期间玩家画在地形之下（见 <see cref="SetSorting"/>），
        /// 于是"人走进管口"这一段是**看得见地没入管口里**，与原版一致。
        /// </para>
        /// </summary>
        public void StartPipeEnterSide()
        {
            _crouching = false;
            ApplySizeNow();
            _facingLeft = false;
            if (_sr != null) _sr.flipX = false;
            SetSprite(FormRun(_owner.Power, 0));
            RecomputeBounds();                       // 贴图/形态刚变过，量距离前先重算包围盒
            var faceX = SidePipeFaceX();
            var innerX = faceX + PipeWarpTable.Current.SidePipeMouthTiles;   // 管口内侧沿
            var distance = Mathf.Max(0f, innerX - Bounds.xMax);
            Game.Logger.Info("Player",
                $"侧向进管：管口面 x={faceX:F2}（{SidePipeFaceSource()}）、管口 {PipeWarpTable.Current.SidePipeMouthTiles} 格宽" +
                $" ⇒ 走到内侧 x={innerX:F2}；当前右边缘 x={Bounds.xMax:F2} ⇒ 走距 {distance:F2} 格");
            BeginPipeMove(new Vector2(1f, 0f), distance, freezeOnEnd: false, GameConst.PipeEntryWalkSpeed);
        }

        /// <summary>
        /// 本次侧向进管的**管口面**世界 x。两处侧向进管各有各的数据源，**都不在这里写数**：
        /// 密室（<see cref="StageContext.SubArea"/>）用 <see cref="PipeWarpTable"/>（元素表 §1.3 / §4.2 的
        /// 出口面 x），主关卡段用关卡数据声明的 `# side-exit`（= 原版整关海报上那根管的管口面）。
        /// </summary>
        private float SidePipeFaceX()
        {
            if (StageContext.SubArea) return PipeWarpTable.Current.BonusRoomExitFaceX;
            if (_level != null && _level.HasSideExit) return _level.SideExitFaceX;

            // 非预期分支必须留日志（§7）。取不到管口面时**原地不动**：宁可少走一步，
            // 也不要再把人送进实心格（E-24 就是这么来的）。
            Game.Logger.Warn("Player",
                "侧向进管过场：关卡数据没有 # side-exit、当前也不是密室 ⇒ 取不到管口面，走距取 0");
            return Bounds.xMax;
        }

        private string SidePipeFaceSource()
            => StageContext.SubArea ? "密室出口管：PipeWarpTable" : "关卡数据 # side-exit";

        private void BeginPipeMove(Vector2 dir, float distance, bool freezeOnEnd)
            => BeginPipeMove(dir, distance, freezeOnEnd, GameConst.PipeMoveSpeed);

        /// <summary>
        /// 开始管中移动。
        /// <para>
        /// <paramref name="speed"/> 只有侧向进管会用到（原版那一下是**自动行走**，速度是马里奥的
        /// `levelEntryWalkSpeedX` = <c>GameConst.PipeEntryWalkSpeed</c>，与"管子平台自己升降"的
        /// 2.5 格/秒不是同一个量）。默认值 = 进出管升降速度。
        /// </para>
        /// </summary>
        private void BeginPipeMove(Vector2 dir, float distance, bool freezeOnEnd, float speed)
        {
            _pipeDir = dir;
            _pipeRemaining = Mathf.Max(0f, distance);
            _pipeFreezeOnEnd = freezeOnEnd;
            _pipeSpeed = speed;
            _motion = PlayerMotion.PipeMove;
            _motionTimer = 0f;
            Busy = true;
            ControlsEnabled = false;
            _vel = Vector2.zero;
            Grounded = false;
            // 管中移动期间画在地形之下（"被管子挡住"，见 SortingNormal/SortingBehindTerrain 的说明）。
            SetSorting(SortingBehindTerrain);
            RecomputeBounds();
            _audio?.PlaySfx(Sfx.Pipe);
            Game.Logger.Info("Player",
                $"管中移动开始：dir=({_pipeDir.x:F0},{_pipeDir.y:F0}) 距离={_pipeRemaining:F2} 格 " +
                $"速度={_pipeSpeed} 格/秒");
        }

        /// <summary>本次管中移动的速度（格/秒）。</summary>
        private float _pipeSpeed = GameConst.PipeMoveSpeed;

        private void TickPipeMove(float dt)
        {
            if (_pipeRemaining > 0f)
            {
                var step = _pipeSpeed * dt;
                if (step >= _pipeRemaining) step = _pipeRemaining;
                _pipeRemaining -= step;
                transform.position += new Vector3(_pipeDir.x * step, _pipeDir.y * step, 0f);
                RecomputeBounds();

                // 还没走完：下一帧接着走（到位那一帧必须落到下面的收尾，不能在这里 return）。
                if (_pipeRemaining > 0f) return;
            }

            // ★ 收尾分支**必须**在"距离为 0"时也走到 —— 这是"侧向进管走距 0 ⇒ 流程层空等 3 秒"
            //   那个 bug 的根因：旧代码开头就是 `if (_pipeRemaining <= 0f) return;`，
            //   于是 0 距离的过场**永远不结束**：`Busy` 一直是 true、`ControlsEnabled` 一直是 false，
            //   流程层只能等兜底超时强制换场（人在这 3 秒里卡在管口外、操作不了）。
            //   日志原文见 StartPipeEnterSide 的注释（2026-09-19 20:48:51）。
            //   到位后停在原地等流程层换场 —— 不跑物理、不读输入（人此刻埋在管子里，
            //   一跑碰撞解算就会被管壁推出来，画面会看到"人从管子里弹出来一下"）。
            _pipeRemaining = 0f;
            Busy = false;
            if (_pipeFreezeOnEnd) return;   // 进管：等流程层切场（控制权也一并冻住）
            _motion = PlayerMotion.Normal;
            ControlsEnabled = true;
            SetSorting(SortingNormal);      // 出管/走进管口结束：画回地形之上（不再被管子挡）
            Game.Logger.Info("Player",
                $"管中移动结束：pos=({transform.position.x:F2},{transform.position.y:F2})");
        }

        /// <summary>当前贴图的**高度**（格）。取不到贴图时退回碰撞盒尺寸（不返回 0）。</summary>
        private float CurrentSpriteHeight()
        {
            var h = _sr != null && _sr.sprite != null ? _sr.sprite.bounds.size.y : 0f;
            return h > 0.01f ? h : _size.y;
        }
        // 注：旧代码里还有一个 CurrentSpriteWidth()（"侧向进管走距 = 自身贴图宽度"的出处），
        // 那个量写不出原版出处（E-24 的根因）⇒ 本轮已删；走距改成"到管口面为止"。

        public void BounceAfterStomp()
        {
            // ★ 只设竖直速度，**不**按"是否按住跳键"改数值。
            //
            // 出处 = clone `LevelManager.cs:343-348`：`velocity = new Vector2(velocity.x + bounce.x, bounce.y)`，
            // 15 是个常数；"按住 A 弹得更高"在原版里靠的是**重力变小**（上升且按住 A → `GravityJumpHeld`），
            // 本工程的 `JumpAndGravity` 已经是这套（`:611-613`）——
            // 这里再乘 1.25 是**重复加成**：按住时等于同一件事加了两遍，而不按的时候又太矮
            // （11.25 的顶点只有 0.74 格，连一只栗宝宝都过不去 ⇒ 踩到第一个就撞上第二个）。
            _vel.y = GameConst.StompBounce;
            Grounded = false;
        }

        public void BounceAfterBlockHit()
        {
            _vel.y = -GameConst.BlockBounce;   // 顶砖块是向下压一下（视觉上砖块弹起）
            Grounded = false;
        }

        // ───────────────────────── 每帧 ─────────────────────────

        private void Update()
        {
            if (_owner == null || _level == null) return;
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            // 单帧步长封顶：编辑器卡一下（比如重新编译）会给出一个巨大的 dt，
            // 那一下足以让马里奥穿过地面掉出关卡。夹紧是廉价的保险。
            if (dt > 0.05f) dt = 0.05f;

            if (_invincibleTimer > 0f) _invincibleTimer -= dt;
            if (_starTimer > 0f) _starTimer -= dt;

            switch (_motion)
            {
                case PlayerMotion.Normal:
                    TickNormal(dt);
                    break;
                case PlayerMotion.Growing:
                case PlayerMotion.Shrinking:
                    TickTransform(dt);
                    break;
                case PlayerMotion.Dead:
                    TickDead(dt);
                    break;
                case PlayerMotion.FlagSlide:
                    TickFlagSlide(dt);
                    break;
                case PlayerMotion.FlagWalk:
                    TickCastleWalk(dt);
                    break;
                case PlayerMotion.PipeMove:
                    TickPipeMove(dt);
                    break;
            }

            TickBlink();
        }

        private void TickNormal(float dt)
        {
            TickCrouch();

            var input = ReadInput();
            // 蹲下时不能走（原版规则）：把方向输入吃掉，靠摩擦自然停下。
            if (_crouching) input = 0f;
            var wantRun = Game.Input != null && Game.Input.GetKey(GameKey.LeftShift);

            MoveHorizontal(dt, input, wantRun);
            JumpAndGravity(dt);

            MoveAndCollide(dt);
            TickRunAnimation(dt);
        }

        // ───────────────────────── 物理（逐条照 clone 的玩家控制器）─────────────────────────
        //
        // 出处：`原版资源/参考工程/SMB-clone/Assets/Scripts/Mario.cs`
        //   · `:121-183` `FixedUpdate` 的水平分支（走 / 跑加速、松手减速、反向滑铲、空中加速）
        //   · `:186-212` 垂直分支（起跳取参数、按住 A 用上升重力）
        // 数值全部集中在 `GameConst` 里，本文件不写任何物理常数。

        /// <summary>当前这一次跳跃的"上升重力"（格/秒²）—— 起跳瞬间按速度档取。</summary>
        private float _jumpUpGravity = GameConst.GravityJumpHeld;
        /// <summary>当前这一次跳跃的"下落重力"（格/秒²）。</summary>
        private float _jumpDownGravity = GameConst.Gravity;
        /// <summary>把跳跃参数复位成"普通下落"（重生 / 传送后必须清掉上一跳那一档的重力）。</summary>
        private void ResetJumpParams()
        {
            _jumpUpGravity = GameConst.GravityJumpHeld;
            _jumpDownGravity = GameConst.Gravity;
            _speedBeforeJump = 0f;
            _runBeforeJump = false;
            _wasDashing = false;
        }
        /// <summary>起跳瞬间的水平速度 / 是否在跑（空中加速上限与反向减速度要看它）。</summary>
        private float _speedBeforeJump;
        private bool _runBeforeJump;
        /// <summary>本帧是否按着 Shift（起跳时记进 `_runBeforeJump`）。</summary>
        private bool _wasDashing;

        /// <summary>
        /// 水平运动。与原版逐条对齐的三点：
        /// ① 走速以下用**走加速**，到达走速后**只有按住 Shift 才继续加速**（原版 `:129-133`）；
        /// ② 反向且速度超过滑铲阈值 ⇒ 只减速、不加速，减到 0 才转身（原版 `:142-150` 的滑铲）；
        /// ③ 空中加速比地面小，且上限只有"起跳时就在跑"才放宽到跑速（原版 `:166-173`）。
        /// </summary>
        private void MoveHorizontal(float dt, float input, bool wantRun)
        {
            var moving = Mathf.Abs(input) > 0.01f;
            var dir = moving ? Mathf.Sign(input) : 0f;

            if (!moving)
            {
                // 松手减速：地面与空中用的是**同一个**减速度（clone `:138` 与 `:175` 都走 releaseDecelerationX）。
                _vel.x = Mathf.MoveTowards(_vel.x, 0f, GameConst.GroundFriction * dt);
                _wasDashing = false;
                return;
            }

            _facingLeft = input < 0f;

            if (Grounded)
            {
                // 滑铲：反向输入 + 速度超过 skidTurnaroundSpeedX ⇒ 只按滑铲减速度往下掉。
                if (Mathf.Abs(_vel.x) > GameConst.SkidThreshold && Mathf.Sign(_vel.x) != dir)
                {
                    _vel.x = Mathf.MoveTowards(_vel.x, 0f, GameConst.SkidDecel * dt);
                    return;
                }

                // 静止起步有一个最小速度（clone `:127-128` 的 minWalkSpeedX）。
                if (_vel.x == 0f) _vel.x = GameConst.MinWalkSpeed * dir;

                var speed = Mathf.Abs(_vel.x);
                var cap = wantRun ? GameConst.RunSpeed : GameConst.WalkSpeed;
                var accel = speed < GameConst.WalkSpeed
                    ? GameConst.GroundAccelWalk                       // 走速以下：走 / 跑共用走加速
                    : (wantRun ? GameConst.GroundAccelRun : 0f);      // 已到走速：只有跑才继续加
                if (accel > 0f) _vel.x = Mathf.MoveTowards(_vel.x, dir * cap, accel * dt);
                _wasDashing = wantRun;
                return;
            }

            // ── 空中 ──
            var airSpeed = Mathf.Abs(_vel.x);
            if (airSpeed > 0.01f && Mathf.Sign(_vel.x) != dir)
            {
                // 空中反向：只减速、朝向保持（clone `:179-182`）。
                _vel.x = Mathf.MoveTowards(_vel.x, 0f,
                    GameConst.AirTurnDecel(airSpeed, _speedBeforeJump) * dt);
                return;
            }
            if (_vel.x == 0f) _vel.x = GameConst.MinWalkSpeed * dir;
            var airCap = _runBeforeJump ? GameConst.RunSpeed : GameConst.WalkSpeed;
            var airAccel = airSpeed < GameConst.WalkSpeed ? GameConst.AirAccelWalk : GameConst.AirAccelRun;
            _vel.x = Mathf.MoveTowards(_vel.x, dir * airCap, airAccel * dt);
        }

        /// <summary>
        /// 跳跃与重力（clone `Mario.cs:186-212` 的搬运）：
        /// ① 起跳瞬间按**当前水平速度**取一组参数（起跳初速 + 上升 / 下落两个重力）；
        /// ② "按得越久跳得越高"在原版里靠的就是**变重力**（上升且按住 A 用较小的重力）；
        /// ③ ⚠️ 原版**没有**"松手截断上升速度"这套机制，所以本工程也不再截断
        ///    （曾经有一个 `JumpCutVelocity`，那是本工程自己加的，已删）。
        /// </summary>
        private void JumpAndGravity(float dt)
        {
            if (ControlsEnabled && Game.Input != null && Game.Input.GetKeyDown(GameKey.Space) && Grounded)
            {
                var p = GameConst.JumpParamsFor(Mathf.Abs(_vel.x));
                _vel.y = p.Speed;
                _jumpUpGravity = p.UpGravity;
                _jumpDownGravity = p.DownGravity;
                _speedBeforeJump = Mathf.Abs(_vel.x);
                _runBeforeJump = _wasDashing;
                Grounded = false;
                // 原版小马里奥和大马里奥的跳跃音效是两个不同的采样。
                _audio?.PlaySfx(_owner.Power == PowerState.Small ? Sfx.JumpSmall : Sfx.Jump);
            }

            var jumpHeld = Game.Input != null && Game.Input.GetKey(GameKey.Space);
            var gravity = (_vel.y > 0f && jumpHeld) ? _jumpUpGravity : _jumpDownGravity;
            _vel.y -= gravity * dt;
            if (_vel.y < -GameConst.MaxFallSpeed) _vel.y = -GameConst.MaxFallSpeed;
        }

        private void TickTransform(float dt)
        {
            _motionTimer += dt;
            var total = _motion == PlayerMotion.Growing ? GameConst.GrowTime : GameConst.ShrinkTime;
            // 闪烁：在原形态与新形态之间快速交替（原版就是这么演的）。
            var on = Mathf.FloorToInt(_motionTimer / GameConst.GrowBlinkInterval) % 2 == 0;
            _blinkOn = on;
            _sr.sprite = on ? _sprites.Get(MarioAction.Grow0) : _sprites.Get(FormIdle(_pendingPower));

            if (_motionTimer >= total)
            {
                _motion = PlayerMotion.Normal;
                Busy = false;
                ControlsEnabled = true;
                ApplyPower(_pendingPower);
                _blinkOn = true;
                SetSprite(FormIdle(_pendingPower));
                Game.Logger.Info("Player", $"变身完成 → {_pendingPower}");
            }
        }

        private void TickDead(float dt)
        {
            _motionTimer += dt;
            // 死亡：先小跳一下，然后自由落体出屏幕。
            _vel.y -= GameConst.GravityJumpHeld * dt;
            transform.position += new Vector3(0f, _vel.y * dt, 0f);

            if (_motionTimer >= GameConst.DeathDuration || transform.position.y < -12f)
            {
                Busy = false;
                _motion = PlayerMotion.Normal;
            }
        }

        /// <summary>是否已报过"旗子没登记"（只报一次，别每帧刷屏）。</summary>
        private bool _flagWarned;

        /// <summary>
        /// 取旗子：读 <see cref="StageContext.Flag"/>（由建它的 LevelProps 登记）。
        /// <para>
        /// 踩过的坑（历史债 E-5）：这里原先是 <c>GameObject.Find("Flag")</c> 按名字找 ——
        /// 名字或层级一改就静默失效，只剩"旗子不动"这个看不出原因的观感问题。
        /// </para>
        /// </summary>
        /// <summary>
        /// 本次降旗用的"半个旗高" —— 旗子贴图是**居中轴心**，所以"底边落在杆底"要换算成
        /// "中心 = 杆底 + 半个高度"。见 <see cref="TickFlagSlide"/> 的说明。
        /// </summary>
        private float _flagHalfHeight = 0.5f;

        /// <summary>
        /// 旗子贴图的半个高度。取不到（贴图还没加载完 / 没登记旗子）时退化成 <b>0.5 格</b> ——
        /// 与原版 1×1 格的旗子一致（<c>SpriteNames.Flag</c> 是 16×16 切片）。
        /// </summary>
        private static float FlagHalfHeight()
        {
            var flag = StageContext.Flag;
            if (flag == null) return 0.5f;
            var sr = flag.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return 0.5f;
            return sr.sprite.bounds.extents.y;
        }

        private Transform EnsureFlag()
        {
            var flag = StageContext.Flag;
            if (flag == null && !_flagWarned)
            {
                _flagWarned = true;
                Game.Logger.Warn("Player", "没有登记旗子（StageContext.Flag 为空），降旗这一步会缺表现");
            }
            return flag;
        }

        private void TickFlagSlide(float dt)
        {
            // 沿杆下滑到杆底，然后自动转入"走进城堡"。
            var y = transform.position.y + _vel.y * dt;
            if (y <= _motionParam)
            {
                y = _motionParam;
                _motion = PlayerMotion.FlagWalk;
                _motionTimer = 0f;
                _facingLeft = false;
            }
            transform.position = new Vector3(transform.position.x, y, 0f);

            // 降旗：原版是"马里奥抓住杆的同时旗子一起降到杆底"。
            //
            // ★ 旗子的终点是**自己的底边落在杆底**，不是"中心落到杆底"。
            //   踩过的坑（用户实测 2026-09-20）：「1-1 时候 旗子好像会下降到旗子最下面 砖的下面」——
            //   原来写的是 `flag.position.y = y`（y = 马里奥脚底 = 杆底），而旗子贴图是**居中轴心**
            //   ⇒ 中心落到地面高度 ⇒ **下面半面沉到地面 / 基座砖以下**。
            //   原版的写法在 clone `FlagPole.cs:22-23`：`while (flag.position.y > flagStop.position.y)`
            //   —— 降旗有一个**专门的终点对象**（`Flag Stop`），不是"跟马里奥脚底同高"。
            //   本工程没有那个标记对象，所以按同一语义自己定：底边 = 杆底 ⇒ 中心 = 杆底 + 半个旗高。
            var flag = EnsureFlag();
            if (flag != null) flag.position = new Vector3(flag.position.x, y + _flagHalfHeight, 0f);
            RecomputeBounds();
            _sr.sprite = _sprites.Get(CurrentClimbFrame());
        }

        private void TickCastleWalk(float dt)
        {
            _motionTimer += dt;
            var x = transform.position.x + GameConst.FlagWalkSpeed * dt;
            // 终点用 _castleDoorX，【不能】用 _motionParam ——
            // 实测踩过：_motionParam 在滑杆阶段存的是"杆底 Y"（约 -3），
            // 把它当 X 用会让 x >= -3 立刻成立，马里奥被瞬移到 x=-3，
            // "走进城堡"这一步等于没做（而且流程照样能走到结算，所以不会报错）。
            if (x >= _castleDoorX)
            {
                x = _castleDoorX;
                _motion = PlayerMotion.LevelClear;
                Busy = false;
                _audio?.PlaySfx(Sfx.LevelComplete);
                // 走进门 = 人就该【不见了】（原版：进城堡后马里奥消失，只留城堡）。
                // 踩过的坑：这里原先只是停下，于是马里奥站在城堡门口一动不动（实测被指出）。
                if (_sr != null) _sr.enabled = false;
                Game.Event.Emit(Events.LevelCleared);
            }
            transform.position = new Vector3(x, transform.position.y, 0f);
            RecomputeBounds();

            // 走进城堡【也要有走路动画】。
            // 踩过的坑：这里原来只写 `_sr.sprite = _sprites.Get(CurrentRunFrame())`，
            // 而 _runFrame 只在 TickRunAnimation 里推进 —— 那函数开头就是
            // `if (_motion != PlayerMotion.Normal) return;`，而过场期间 _motion 是 FlagWalk
            // ⇒ _runFrame 恒为同一个值 ⇒ 马里奥一路"滑"进城堡（实测被指出"没有行走动画"）。
            AdvanceRunFrame(dt, GameConst.FlagWalkSpeed);
            _sr.sprite = _sprites.Get(FormRun(_owner.Power, _runFrame));
            _sr.flipX = _facingLeft;
        }

        private void TickBlink()
        {
            if (_sr == null) return;

            // 无敌星：原版是让马里奥【整个配色循环】闪，不是半透明。
            // 这里用四个色调轮转近似 —— 原版是逐帧换调色板，要像素级还原得拿到调色板数据。
            if (_starTimer > 0f)
            {
                var phase = Mathf.FloorToInt(_starTimer * 12f) % 4;
                _sr.color = phase switch
                {
                    0 => Color.white,
                    1 => new Color(1f, 0.55f, 0.55f),
                    2 => new Color(0.6f, 1f, 0.6f),
                    _ => new Color(0.6f, 0.75f, 1f),
                };
                return;
            }

            // 恢复满色（星星刚结束、或受击无敌刚结束都要回到白色不透明）。
            if (_sr.color != Color.white) _sr.color = Color.white;

            // 无敌帧闪烁：受击后短暂半透明（原版是快速闪烁）。
            if (_invincibleTimer > 0f)
            {
                var on = Mathf.FloorToInt(_invincibleTimer * 20f) % 2 == 0;
                var c = _sr.color;
                c.a = on ? 0.35f : 1f;
                _sr.color = c;
            }
        }

        // ───────────────────────── 输入 ─────────────────────────

        private float ReadInput()
        {
            if (!ControlsEnabled || Game.Input == null) return 0f;
            var v = 0f;
            if (Game.Input.GetKey(GameKey.LeftArrow) || Game.Input.GetKey(GameKey.A)) v -= 1f;
            if (Game.Input.GetKey(GameKey.RightArrow) || Game.Input.GetKey(GameKey.D)) v += 1f;
            return Mathf.Clamp(v, -1f, 1f);
        }

        // ───────────────────────── 物理 ─────────────────────────

        private void RecomputeBounds()
        {
            var p = transform.position;
            Bounds = new Rect(p.x - _size.x * 0.5f, p.y, _size.x, _size.y);
        }

        private void MoveAndCollide(float dt)
        {
            var p = transform.position;

            // ---- X 轴 ----
            var dx = _vel.x * dt;
            if (Mathf.Abs(dx) > 0f)
            {
                var newX = p.x + dx;
                var rect = new Rect(newX - _size.x * 0.5f, p.y, _size.x, _size.y);
                var hit = false;
                foreach (var cell in Overlap(rect))
                {
                    if (!_level.IsSolidTile(cell.x, cell.y)) continue;
                    // ★ 移动平台格【不挡横移】：台面只有半格厚，原版里人是从侧面走进/跳上去的，
                    //   不是被一堵隐形墙拦住（那一格只是"格子里有台面"的近似登记，见 `Platform.RegisterCells`）。
                    //   用户 2026-09-19 实测症状：「跳不上去移动的平台」—— 根因就是这里把整格当墙。
                    if (_level.TryGetCarrierTop(cell.x, cell.y, out _)) continue;
                    newX = _vel.x > 0f ? cell.x - _size.x * 0.5f : cell.x + 1f + _size.x * 0.5f;
                    hit = true;
                    break;   // 一帧内只解一次：速度已被 MaxFallSpeed/走速 限制，不会连撞两格
                }
                if (hit) _vel.x = 0f;
                p.x = newX;
            }

            // ---- 左边界 ----
            // 原版马里奥【不能走出关卡左边缘】：关卡左侧之外没有地面，走出去就是无限掉死
            // （实测：一路往左能走到 x=-15.6，而 MinWorldX=-13，然后掉出关卡反复死亡）。
            // 相机本来就是"永不后退 + 夹在关卡边界内"，所以玩家左边界就取关卡左边缘。
            if (_level != null)
            {
                var minX = _level.MinWorldX + _size.x * 0.5f;
                if (p.x < minX)
                {
                    p.x = minX;
                    if (_vel.x < 0f) _vel.x = 0f;
                }
            }

            // ---- Y 轴 ----
            var dy = _vel.y * dt;
            var wasGrounded = Grounded;
            Grounded = false;
            if (Mathf.Abs(dy) > 0f)
            {
                // 解算前的脚底 / 头顶：用来判断"这一格到底是不是落点 / 顶棚"。
                var prevFeet = p.y;
                var prevHead = p.y + _size.y;
                var newY = p.y + dy;
                var rect = new Rect(p.x - _size.x * 0.5f, newY, _size.x, _size.y);
                var best = float.NaN;
                var bestCell = Vector2Int.zero;
                var anyOverlap = false;

                foreach (var cell in Overlap(rect))
                {
                    if (!_level.IsSolidTile(cell.x, cell.y)) continue;
                    anyOverlap = true;

                    // ★ 移动平台：落点取台面的【真实】顶部（小数），不是"这一格的顶边"。
                    //
                    // 踩过的坑（P1-9「上行托着马里奥平移」）：用格顶边的话，平台上升时
                    // 台面高度在玩家眼里恒为整数 —— 平台先从马里奥身上穿过去，跨格那一瞬间
                    // 再把他弹起来一格，看着像"被顶了一下"而不是"被托着走"。
                    // 登记/查询见 ILevel.SetCarrier / TryGetCarrierTop。
                    var isCarrier = _level.TryGetCarrierTop(cell.x, cell.y, out var ct);
                    var carrierTop = isCarrier ? ct : cell.y + 1f;

                    if (_vel.y > 0f)
                    {
                        // ★ 移动平台**不算顶棚**：半格厚的台面，人可以贴着它下面跳上去、从它中间穿过
                        //   （原版就是"跳上去"这条路；整格登记会让人在台面下方被一堵隐形天花板顶回来，
                        //   用户 2026-09-19 实测：「跳不上去移动的平台」）。
                        if (isCarrier) continue;

                        // 上升：只有"解算前头顶还在这一格底边之下"的格子才算顶棚。
                        // 否则说明人已经嵌在格子里了（见 MoveAndCollide 末尾的 Depenetrate），
                        // 那种情况绝不能再按"顶棚"处理 —— 那会把人往下按进地里。
                        if (prevHead > cell.y) continue;
                        if (float.IsNaN(best) || cell.y < best) { best = cell.y; bestCell = cell; }
                    }
                    else
                    {
                        // 下落：只有"解算前脚底已经在这一格顶面之上"的格子才算落点。
                        //
                        // ★ 移动平台：台面每帧都在动，拿"上一帧脚底 ≥ 台面顶"硬卡会漏判（台面上升时把人漏掉）
                        //   —— 但也**不能无条件吸附**：那样台面从人腰上扫过会把人生生拽上去
                        //   （用户实测「会被弹开」）。所以用"一帧内台面能升多少"当带宽（见
                        //   `GameConst.PlatformCatchBand` 的推导）：脚底离台面顶 ≤ 0.2 格 ⇒ 托住/落上去；
                        //   离得更远 ⇒ 忽略这一格（人从台面旁边/下面过去）。
                        if (isCarrier)
                        {
                            if (prevFeet < carrierTop - GameConst.PlatformCatchBand) continue;
                        }
                        else if (prevFeet < carrierTop) continue;

                        if (float.IsNaN(best) || carrierTop > best) { best = carrierTop; bestCell = cell; }
                    }
                }

                if (!float.IsNaN(best))
                {
                    if (_vel.y > 0f)
                    {
                        // 头顶撞到东西：脚停在**格的底边**（cell.y），记录格子给玩法模块（顶砖块 / 问号块）。
                        //
                        // ★ 脚要停在**格的底边**，不是格的顶边。
                        //   这里原来写的是 `carrierTop - _size.y`（= 格的**顶**边减身高）⇒ 马里奥被
                        //   直接摆到障碍【上方】——用户报的就是这个：「我顶问号/顶砖块/顶任何东西，
                        //   都会直接瞬移到障碍上方」。撞头顶时人必须**留在下方**。
                        newY = best - _size.y;
                        _headHit = bestCell;
                    }
                    else
                    {
                        newY = best;
                        Grounded = true;
                    }
                    _vel.y = 0f;
                }
                else if (anyOverlap)
                {
                    // 已经嵌在实心格里（上一帧就被塞进去、或被平台推的）：这一帧先别往更深处走。
                    // 真正把人弄出来由下面的 Depenetrate 负责。
                    newY = p.y;
                }
                p.y = newY;
            }

            if (Grounded && !wasGrounded)
            {
                // 落地音效交给玩法层按下落速度决定，这里不动（否则轻跳落地也会响）。
            }

            transform.position = p;
            RecomputeBounds();
            Depenetrate();
        }

        /// <summary>"解算后仍嵌在实心格里"这条 Warn 只报一次（每帧报会把日志刷爆，但完全不报就查不出来）。</summary>
        private bool _depenLogged;

        /// <summary>
        /// 兜底脱困：碰撞解算完若包围盒**仍压在实心格上**，按"位移最小"的方向把人推出来。
        /// <para>
        /// <b>为什么必须有这一层</b>（用户实测）："跳下来落地的瞬间会被卡到砖块里，然后一直往左传送"。
        /// 根因不是解算方向写错，而是**人先被塞进了实心格**：一旦人在格子里，X 轴解算每帧都会
        /// 把他往同一侧甩一次 ⇒ 看起来就是"一直往左输送"。所以这里保证一个不变式：
        /// <b>MoveAndCollide 结束时，包围盒绝不与实心格相交</b>。
        /// </para>
        /// <para>
        /// 挑"最小位移"而不是"按速度方向推"：脱困时速度方向已经没有意义了（发生这件事本身就说明
        /// 上一帧的解算漏了），最小位移能保证人被推开最少、且优先落回可站的面（向上推的代价最小）。
        /// </para>
        /// </summary>
        private void Depenetrate()
        {
            if (_level == null) return;

            // 最多迭代 4 次：脱困一次之后可能又压到相邻格（角落），一般 1 次就干净。
            for (var guard = 0; guard < 4; guard++)
            {
                var b = Bounds;
                var push = Vector2.zero;
                var bestDepth = float.MaxValue;
                foreach (var cell in Overlap(b))
                {
                    if (!_level.IsSolidTile(cell.x, cell.y)) continue;

                    // ★ 移动平台（载具）格要单独处理 —— **站在台面上不算"嵌进实心格"**。
                    //
                    // 踩过的坑（用户实测 2026-09-19：「上下移动的台阶站不上去，会被弹开」+
                    // 「上下的不知道为什么会混在一起」，同局日志 23:11:22/25 连出
                    // `[Warn] 碰撞兜底脱困 4 次仍有重叠`）：平台登记的是**整格实心**
                    // （格底边比台面真实顶面低最多 1 格，见 `Platform.RegisterCells`），
                    // 而人站在台面上时脚底 = `carrierTop`，本来就落在**这一格内部** ⇒ 这里
                    // 每帧都判"嵌格"，按最小位移把人推到**格子的顶边**（= 台面以上 0.4 格）；
                    // 下一帧台面又升上来、Y 解算再把人按回 `carrierTop` ⇒
                    // **弹起→按回→弹起** 的死循环（观感就是"站不上去、被弹开"，而且每帧都报重叠）。
                    //
                    // 判据（2026-09-19 二次修正）：移动平台格**一律跳过**。
                    //
                    // 第一版是"脚底在台面顶面之上就跳过、否则往台面顶推" —— 那会把人从台面**下面**
                    // 顶到台面上去（人贴着台面下方跳过去会被拽上来）。既然台面是"半格厚、可从下方穿过、
                    // 只能从上面落上去"的（见 MoveAndCollide 的三条分支），这里就不该参与脱困：
                    // 站着时脚底 = carrierTop 本来就在格内（不是嵌格），而从下面穿过时更不该被推。
                    if (_level.TryGetCarrierTop(cell.x, cell.y, out _)) continue;

                    var left = b.xMax - cell.x;                 // 往左推这么多
                    var right = cell.x + 1f - b.xMin;           // 往右推这么多
                    var up = cell.y + 1f - b.yMin;              // 往上推这么多
                    var down = b.yMax - cell.y;                 // 往下推这么多
                    var d = Mathf.Min(Mathf.Min(left, right), Mathf.Min(up, down));
                    if (d >= bestDepth) continue;

                    bestDepth = d;
                    push = Mathf.Approximately(d, up) ? new Vector2(0f, up)
                         : Mathf.Approximately(d, left) ? new Vector2(-left, 0f)
                         : Mathf.Approximately(d, right) ? new Vector2(right, 0f)
                         : new Vector2(0f, -down);
                }

                if (bestDepth == float.MaxValue) return;        // 干净了

                transform.position += new Vector3(push.x, push.y, 0f);
                RecomputeBounds();

                if (!_depenLogged)
                {
                    _depenLogged = true;
                    Game.Logger.Warn("Player",
                        $"碰撞兜底脱困：解算后仍压在实心格上，按最小位移 {bestDepth:F3} 格推开 " +
                        $"（push=({push.x:F3},{push.y:F3})，解算前脚底={b.y:F2}）。" +
                        "出现这一行说明前面某一帧把人塞进了格子 —— 把这一行连同前后日志一起报出来");
                }
            }

            Game.Logger.Warn("Player",
                "碰撞兜底脱困 4 次仍有重叠（可能存在比单格更大的实心区域）—— 位置可能不可信");
        }

        private Vector2Int? _headHit;

        public Vector2Int? ConsumeHeadHit()
        {
            var h = _headHit;
            _headHit = null;
            return h;
        }

        /// <summary>
        /// 枚举一个世界矩形覆盖到的所有格。
        /// 用 FloorToInt 而不是 (int) 转换：负数坐标下 (int) 向零取整会漏掉左边/下面那一格。
        /// </summary>
        private static System.Collections.Generic.IEnumerable<Vector2Int> Overlap(Rect r)
        {
            var x0 = Mathf.FloorToInt(r.xMin);
            var x1 = Mathf.FloorToInt(r.xMax - 0.0001f);
            var y0 = Mathf.FloorToInt(r.yMin);
            var y1 = Mathf.FloorToInt(r.yMax - 0.0001f);
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    yield return new Vector2Int(x, y);
                }
            }
        }

        // ───────────────────────── 表现 ─────────────────────────

        /// <summary>
        /// 蹲下的进入 / 退出判定（照原版规则）。
        /// <para>
        /// · 只有大 / 火形态能蹲 —— 小马里奥在原版里没有这个动作；
        /// · 必须站在地上：空中按 ↓ 不蹲（原版也不会在半空缩起来）；
        /// · 起身前要检查头顶空间 —— 蹲在 1 格高的洞里松开 ↓ 时，若直接站直，
        ///   碰撞盒会插进上面的实心格，人会卡住或穿模。
        /// </para>
        /// </summary>
        private void TickCrouch()
        {
            if (_owner == null || Game.Input == null) return;

            var want = ControlsEnabled
                       && _owner.Power != PowerState.Small
                       && Grounded
                       && (Game.Input.GetKey(GameKey.DownArrow) || Game.Input.GetKey(GameKey.S));

            if (want == _crouching) return;

            if (want) _crouching = true;
            else if (!HasHeadroom()) return;   // 头顶被压住：先继续蹲着
            else _crouching = false;

            ApplySizeNow();
            RecomputeBounds();
        }

        /// <summary>站起来后的碰撞盒会不会撞到实心格（蹲在矮洞里时据此判断能不能起身）。</summary>
        private bool HasHeadroom()
        {
            if (_level == null) return true;
            var stand = _owner != null && _owner.Power == PowerState.Small
                ? GameConst.SmallSize : GameConst.BigSize;
            var p = transform.position;
            var rect = new Rect(p.x - stand.x * 0.5f, p.y, stand.x, stand.y);
            foreach (var cell in Overlap(rect))
                if (_level.IsSolidTile(cell.x, cell.y)) return false;
            return true;
        }

        /// <summary>
        /// 跑动换帧：跑得越快帧切得越快（原版同款，速率跟速度挂钩）。
        /// <para>抽成函数是为了让"走进城堡"那段也能用 —— 它同样需要走路动画，
        /// 但走的是另一个 motion 分支，借不到 <see cref="TickRunAnimation"/>。</para>
        /// </summary>
        private void AdvanceRunFrame(float dt, float speed)
        {
            var interval = Mathf.Lerp(0.16f, 0.06f, Mathf.InverseLerp(1f, GameConst.RunSpeed, speed));
            _runAnimTimer += dt;
            if (_runAnimTimer >= interval)
            {
                _runAnimTimer = 0f;
                _runFrame = (_runFrame + 1) % 3;
            }
        }

        private void TickRunAnimation(float dt)
        {
            if (_motion != PlayerMotion.Normal) return;

            // 蹲下优先于其它状态：蹲着就是蹲着，不播跑 / 跳。
            if (_crouching)
            {
                _runFrame = 0;
                _sr.sprite = _sprites.Get(FormCrouch(_owner.Power));
                _sr.flipX = _facingLeft;
                return;
            }

            var speed = Mathf.Abs(_vel.x);
            var skidding = Grounded && speed > GameConst.SkidThreshold &&
                           ((_vel.x > 0f && _facingLeft) || (_vel.x < 0f && !_facingLeft));

            if (!Grounded)
            {
                _sr.sprite = _sprites.Get(FormJump(_owner.Power));
            }
            else if (skidding)
            {
                _sr.sprite = _sprites.Get(FormSkid(_owner.Power));
            }
            else if (speed > 0.15f)
            {
                AdvanceRunFrame(dt, speed);
                _sr.sprite = _sprites.Get(FormRun(_owner.Power, _runFrame));
            }
            else
            {
                _sr.sprite = _sprites.Get(FormIdle(_owner.Power));
                _runFrame = 0;
            }

            // 朝向：精灵统一画成朝右，向左时水平翻转。
            _sr.flipX = _facingLeft;
        }

        private void SetSprite(string action)
        {
            if (_sr != null) _sr.sprite = _sprites.Get(action);
        }

        private string CurrentIdleFrame() => FormIdle(_owner.Power);
        private string CurrentRunFrame() => FormRun(_owner.Power, _runFrame);
        private string CurrentClimbFrame() =>
            (Mathf.FloorToInt(Time.time * 8f) % 2 == 0) ? FormClimb0(_owner.Power) : FormClimb1(_owner.Power);

        private static string FormIdle(PowerState p) => p switch
        {
            PowerState.Small => MarioAction.SmallIdle,
            PowerState.Big => MarioAction.BigIdle,
            _ => MarioAction.FireIdle,
        };

        private static string FormJump(PowerState p) => p switch
        {
            PowerState.Small => MarioAction.SmallJump,
            PowerState.Big => MarioAction.BigJump,
            _ => MarioAction.FireJump,
        };

        private static string FormSkid(PowerState p) => p switch
        {
            PowerState.Small => MarioAction.SmallSkid,
            PowerState.Big => MarioAction.BigSkid,
            _ => MarioAction.FireSkid,
        };

        /// <summary>蹲下贴图。小马里奥没有蹲下动作，兜底回站姿。</summary>
        private static string FormCrouch(PowerState p) => p switch
        {
            PowerState.Big => MarioAction.BigCrouch,
            PowerState.Fire => MarioAction.FireCrouch,
            _ => MarioAction.SmallIdle,
        };

        private static string FormRun(PowerState p, int frame) => p switch
        {
            PowerState.Small => frame == 0 ? MarioAction.SmallRun0 : frame == 1 ? MarioAction.SmallRun1 : MarioAction.SmallRun2,
            PowerState.Big => frame == 0 ? MarioAction.BigRun0 : frame == 1 ? MarioAction.BigRun1 : MarioAction.BigRun2,
            _ => frame == 0 ? MarioAction.FireRun0 : frame == 1 ? MarioAction.FireRun1 : MarioAction.FireRun2,
        };

        private static string FormClimb0(PowerState p) => p switch
        {
            PowerState.Small => MarioAction.SmallClimb0,
            PowerState.Big => MarioAction.BigClimb0,
            _ => MarioAction.FireClimb0,
        };

        private static string FormClimb1(PowerState p) => p switch
        {
            PowerState.Small => MarioAction.SmallClimb1,
            PowerState.Big => MarioAction.BigClimb1,
            _ => MarioAction.FireClimb1,
        };
    }
}
