using UnityEngine;

namespace SuperMario.Core
{
    /// <summary>
    /// 游戏数值与物理常量。
    /// <para>
    /// 这些值是按原版 NES 的手感反推的：原版以「像素/帧」为单位（60fps），
    /// 这里统一换算成「格/秒」和「格/秒²」，一格 = 16 像素 = 一个瓦片。
    /// 换算系数就是 ×60（帧→秒）÷16（像素→格）= ×3.75。
    /// </para>
    /// 不写成配表：这些是**手感常量**，不是策划会逐条调的数值表；
    /// 真正会增长的数据（关卡）已经抽到了 Resources/Levels/World1-1.txt。
    /// </summary>
    public static class GameConst
    {
        // ---- 世界尺度 ----
        /// <summary>一个瓦片的边长（世界单位）。全项目唯一基准，其它尺寸都由它推出来。</summary>
        public const float TileSize = 1f;

        /// <summary>原版一屏 16x15 格；相机按这个比例定正交尺寸。</summary>
        public const int ScreenTilesX = 16;
        public const int ScreenTilesY = 15;

        // ═══════════════ 物理常量（**逐项来自 clone 的玩家控制器，一个都不自己定**）═══════════════
        //
        // 出处（三条，全部可读可复核）：
        //   [A] `原版资源/参考工程/SMB-clone/Assets/Scripts/Mario.cs`
        //       · `:30-37` 水平运动常量（min/walk/run 速度、加速、减速、滑铲）
        //       · `:90-104` `SetJumpParams()` —— **按当前速度选跳跃参数**的两张表
        //       · `:106-118` `SetMidairParams()` —— 空中加速 / 减速
        //   [B] 同工程 `Assets/Prefabs/_managers/Level Starter.prefab:871` `m_GravityScale: 5.3`
        //       （马里奥刚体的重力倍率；clone 在上升/下落时乘 `jumpUpGravity` / `jumpDownGravity`）
        //   [C] 同工程 `ProjectSettings/Physics2DSettings.asset:7` `m_Gravity: {x: 0, y: -9.81}`
        //       与 `ProjectSettings/TimeManager.asset` `Fixed Timestep: 0.02`
        //       ⇒ 1 格 = 1 世界单位（与本工程 `spritePixelsPerUnit = 16` 同一套换算）
        //
        // 换算（每条都能手算复核）：
        //   · **速度**：clone 直接把它写进 `rigidbody.velocity`，所以数值就是 格/秒
        //     （`maxWalkSpeedX = 5.86` ⇒ 5.86 格/秒）。
        //   · **重力**：clone 写的是 `gravityScale = normalGravity * jumpUpGravity`，
        //     真实的加速度 = `9.81 × 5.3 × 倍率`（例：1.64 ⇒ 85.27 格/秒²）。
        //   · **加速度**：clone 写的是「每个 FixedUpdate 步的增量」，除以 0.02 才是 格/秒²
        //     （例：`walkAccelerationX = 0.14` ⇒ 7.0 格/秒²）。
        //

        /// <summary>马里奥刚体的重力倍率 —— 出处 [B]。</summary>
        private const float GravityScale = 5.3f;
        /// <summary>Unity 2D 全局重力（格/秒²）—— 出处 [C]。</summary>
        private const float UnityGravity = 9.81f;
        /// <summary>clone 一个物理步的长度（秒）—— 出处 [C]，用来把"每步增量"换成 格/秒²。</summary>
        private const float FixedStep = 0.02f;

        /// <summary>把 clone 的重力倍率换成 格/秒²：<c>9.81 × 5.3 × 倍率</c>。</summary>
        private static float GravityOf(float scaleMultiplier) => UnityGravity * GravityScale * scaleMultiplier;

        /// <summary>
        /// 一组跳跃参数：起跳初速 + 上升（按住 A）/ 下落（松手）两个重力。
        /// <para>逐档取自 clone `Mario.cs:90-104` —— 原版的跳跃参数是**按起跳瞬间的水平速度**选的，
        /// 不是一套常数（跑着跳比站着跳高得多，这一条就是"跑跳"手感的来源）。</para>
        /// </summary>
        public readonly struct JumpParams
        {
            /// <summary>起跳初速（格/秒）。</summary>
            public readonly float Speed;
            /// <summary>上升且按住 A 时的重力（格/秒²）。</summary>
            public readonly float UpGravity;
            /// <summary>松手或下落时的重力（格/秒²）。</summary>
            public readonly float DownGravity;

            public JumpParams(float speed, float upGravity, float downGravity)
            {
                Speed = speed;
                UpGravity = upGravity;
                DownGravity = downGravity;
            }
        }

        /// <summary>
        /// 按水平速度取跳跃参数（clone `Mario.cs:91-103` 的逐条搬运）：
        /// <list type="bullet">
        /// <item>|vx| &lt; 3.75：起跳 **15**、上升重力 ×0.47、下落 ×1.64；</item>
        /// <item>|vx| &lt; 8.67：起跳 **15**、上升重力 ×0.44、下落 ×1.41；</item>
        /// <item>否则（奔跑）：起跳 **18.75**、上升重力 ×0.59、下落 ×2.11。</item>
        /// </list>
        /// <para>满按的峰值高度 = v²/(2·上升重力)：走跳 15²/(2×24.44) ≈ **4.6 格**，
        /// 跑跳 18.75²/(2×30.69) ≈ **5.7 格** —— 1-1 的 4 格高管道在这种高度下稳稳能上。</para>
        /// </summary>
        public static JumpParams JumpParamsFor(float speedX)
        {
            if (speedX < 3.75f) return new JumpParams(15f, GravityOf(0.47f), GravityOf(1.64f));
            if (speedX < 8.67f) return new JumpParams(15f, GravityOf(0.44f), GravityOf(1.41f));
            return new JumpParams(18.75f, GravityOf(0.59f), GravityOf(2.11f));
        }

        /// <summary>正常下落重力（格/秒²）= 9.81 × 5.3 × 1.64 ≈ 85.27。</summary>
        public static readonly float Gravity = GravityOf(1.64f);

        /// <summary>按住 A 键时的上升重力（格/秒²）= 9.81 × 5.3 × 0.47 ≈ 24.44：比下落小得多，这就是「按得越久跳得越高」的来源。</summary>
        public static readonly float GravityJumpHeld = GravityOf(0.47f);

        /// <summary>
        /// 最大下落速度（格/秒）。
        /// <para>
        /// **这一条是"本项目新增"，不是原版值**（clone 的 `Mario.cs` 里没有速度上限）
        /// —— 本工程的碰撞是**逐格解算**（`PlayerActor.MoveAndCollide`），单帧位移必须小于半格，
        /// 否则会穿透地面；24 格/秒 @60fps = 0.4 格，是这条保证的上限。
        /// 已登记在 `策划/验收表.md` 的「允许的差异」（E-22）。
        /// </para>
        /// </summary>
        public static readonly float MaxFallSpeed = 24f;

        // ---- 水平运动（clone `Mario.cs:30-37`，速度直取、加速度 ÷ 0.02）----

        /// <summary>走路速度上限（格/秒）—— clone `:36` `maxWalkSpeedX = 5.86`。</summary>
        public const float WalkSpeed = 5.86f;

        /// <summary>奔跑速度上限（格/秒）—— clone `:37` `maxRunSpeedX = 9.61`（按住 Shift）。</summary>
        public const float RunSpeed = 9.61f;

        /// <summary>从静止起步时的最小速度（格/秒）—— clone `:30` `minWalkSpeedX = .28`。</summary>
        public const float MinWalkSpeed = 0.28f;

        /// <summary>地面加速度·尚未达到走速时（格/秒²）= 0.14 ÷ 0.02 —— clone `:31`。</summary>
        public const float GroundAccelWalk = 7.0f;

        /// <summary>地面加速度·已达走速且按住 Shift（格/秒²）= 0.21 ÷ 0.02 —— clone `:32`。</summary>
        public const float GroundAccelRun = 10.5f;

        /// <summary>松开方向键后的减速度（格/秒²）= 0.25 ÷ 0.02 —— clone `:33` `releaseDecelerationX`。</summary>
        public const float GroundFriction = 12.5f;

        /// <summary>空中加速度·尚未达到走速（格/秒²）= 0.14 ÷ 0.02 —— clone `:108`。</summary>
        public const float AirAccelWalk = 7.0f;

        /// <summary>空中加速度·已达走速（格/秒²）= 0.21 ÷ 0.02 —— clone `:115`。</summary>
        public const float AirAccelRun = 10.5f;

        /// <summary>空中松手减速（格/秒²）= 0.25 ÷ 0.02（clone 空中也走 `releaseDecelerationX`，`:175`）。</summary>
        public const float AirFriction = 12.5f;

        /// <summary>
        /// 空中反向时的减速度（格/秒²）—— clone `:109-116` 的三档：
        /// 速度 &lt; 5.86 且起跳前速度 &lt; 6.80 ⇒ 0.14；速度 &lt; 5.86 但起跳前更快 ⇒ 0.19；否则 0.21。
        /// </summary>
        public static float AirTurnDecel(float speedX, float speedBeforeJump) =>
            speedX < WalkSpeed
                ? (speedBeforeJump < 6.80f ? 7.0f : 9.5f)
                : 10.5f;

        /// <summary>滑铲（反向输入）减速度（格/秒²）= 0.5 ÷ 0.02 —— clone `:34` `skidDecelerationX`。</summary>
        public const float SkidDecel = 25f;

        /// <summary>触发滑铲所需的水平速度阈值（格/秒）—— clone `:35` `skidTurnaroundSpeedX = 3.5`。</summary>
        public const float SkidThreshold = 3.5f;

        /// <summary>
        /// 进关前那张"WORLD 1-1 / ×命数"入场卡的最短显示时间（秒）。
        /// <para>原版这张卡要停约 2 秒。关卡是纯本地加载，不补一个下限就会一闪而过。</para>
        /// </summary>
        public const float LevelIntroTime = 2f;

        /// <summary>
        /// 无敌星落地后的弹起初速（格/秒）—— 原版的星星是一跳一跳往前走的。
        /// <para>**本项目新增**：这个数值在本项目的两份权威载体（clone / rip 工程）里都没有对应字段，
        /// 是既有实现；出处待补，已登记在 `策划/验收表.md` 的「允许的差异」（E-22）。
        /// 数值保持原样（= 旧 `7.5 × 1.5`）。</para>
        /// </summary>
        public const float StarBounce = 11.25f;

        // ---- 传送管（1-1 第 4 根管 ⇄ 金币房）----
        //
        // 出处（都是原版参考工程 SMB-clone 的脚本常量，本表其余数值用的是同一条证据链）：
        //   `Assets/Scripts/PipeWarpDown.cs:11`  platformVelocityY = -0.05f（FixedUpdate 默认 0.02 秒）
        //   `Assets/Scripts/PipeWarpUp.cs:7`     platformVelocityY =  .05f
        // ⇒ 0.05 / 0.02 = **2.5 格/秒**。
        /// <summary>进/出管时马里奥没入、顶出管口的速度（格/秒）。移动距离由马里奥自身碰撞盒推出，不另定常数。</summary>
        public const float PipeMoveSpeed = 2.5f;

        /// <summary>
        /// 走出密室侧向管口时"被吸进去"的自动行走速度（格/秒）。
        /// <para>出处：clone `Assets/Scripts/PipeWarpSide.cs:29` 调 `mario.AutomaticWalk(mario.levelEntryWalkSpeedX)`，
        /// 而 `Assets/Scripts/Mario.cs:49` 写 `levelEntryWalkSpeedX = 3.05f`。</para>
        /// </summary>
        public const float PipeEntryWalkSpeed = 3.05f;

        /// <summary>
        /// 多金币砖能连顶出几枚金币。
        /// </summary>
        public const int MultiCoinBrickCoins = 10;

        // ---- 尺寸（马里奥的碰撞盒，以格为单位）----
        /// <summary>小马里奥碰撞盒尺寸（宽 × 高）。</summary>
        public static readonly Vector2 SmallSize = new Vector2(0.75f, 0.75f);

        /// <summary>大 / 火马里奥碰撞盒尺寸（原版是 16x32 像素 = 1x2 格）。</summary>
        public static readonly Vector2 BigSize = new Vector2(0.75f, 1.5f);

        /// <summary>
        /// 蹲下时的碰撞盒尺寸（原版大马里奥蹲下是 16x16 = 1x1 格，高度减半）。
        /// <para>小马里奥蹲不下（原版就没有这个动作），所以只有大 / 火形态用它。</para>
        /// </summary>
        public static readonly Vector2 CrouchSize = new Vector2(0.75f, 0.75f);

        // ---- 生死与得分 ----

        /// <summary>
        /// 挨打后的无敌时长（秒）—— 这段时间内**不再受伤**（踩敌人照常有效）。
        /// <para>
        /// <b>出处</b>：clone <c>LevelManager.cs:25</c> <c>MarioInvinciblePowerdownDuration = 2</c>；
        /// 用法见同文件 <c>:209</c> <c>isInvincible() => isInvinciblePowerdown || isInvincibleStarman</c>
        /// （`Mario.cs:413` 的受伤判定就是 <c>if (!isInvincible())</c>），置真 / 置假在
        /// <c>:242-246</c>（缩身动画播完 ⇒ `true`，等 <c>2</c> 秒 ⇒ `false`）。
        /// </para>
        /// <para>
        /// 间隔 <b>1.622 秒</b> —— 按原版的 2 秒本该免伤，按 1.6 秒则刚好过期 22 毫秒。
        /// </para>
        /// <para>
        /// 备注（表现层的本项目写法）：这段无敌在画面上表现为**半透明闪烁**
        /// （<c>PlayerActor.TickBlink</c> 用同一个计时器驱动）—— 原版这 2 秒没有额外闪烁，
        /// 闪烁来自缩身动画本身；这里让两者同起同落，玩家才能"看到自己的无敌还剩多久"。
        /// </para>
        /// </summary>
        public const float HitInvincibleTime = 2f;
        public const float StarInvincibleTime = 10f;

        /// <summary>掉出关卡下边界多少格判定死亡。</summary>
        public const float DeathPitY = -6f;

        /// <summary>
        /// 判"人是不是落在移动平台台面上"的容差带（格）—— 见 <c>PlayerActor.MoveAndCollide</c> 的 Y 轴下落分支。
        /// <para>
        /// <b>推导</b>（不是拍的）：平台最快 3 格/秒（`MovingPlatformVertical.prefab` 的 `absSpeed: 0.05`
        /// × 60fps，见 `PlatformModule.Platform.Speed`），而单帧步长被夹在 0.05 秒
        /// （`PlayerActor.Tick`：`if (dt > 0.05f) dt = 0.05f;`）⇒ 一帧里台面最多升 <b>0.15 格</b>。
        /// 所以"上一帧脚底离台面顶不超过 0.15 格"就一定是**被台面托着 / 正要落上去**，
        /// 而不是"台面从人腰上扫过去"——后者不该把人吸上去。这里取 0.15 + 0.05 余量 = 0.2。
        /// </para>
        /// </summary>
        public const float PlatformCatchBand = 0.2f;

        public const int ScoreCoin = 200;
        public const int ScoreStomp = 100;
        public const int ScorePowerup = 1000;
        public const int ScoreBrickBreak = 50;
        public const int ScoreFlag = 5000;

        /// <summary>
        /// <para>
        /// <b>出处说明</b>：这条**在 clone 里没有对应实现**（clone 的 `MarioCompleteLevel()` 只
        /// `timerPaused = true; ChangeMusic(levelCompleteMusic)`，旗杆不加分、时间也不换分），
        /// 兑换节奏 = **一帧兑 1 个单位**（`AppFlow` 的结算段），即 379 个单位约 6 秒，
        /// 与"哗哗往上跳"的观感一致。
        /// </para>
        /// </summary>
        public const int ScorePerTime = 50;

        /// <summary>初始命数与每关时间（原版 1-1 是 400）。</summary>
        public const int StartLives = 3;
        public const int LevelTime = 400;

        /// <summary>时间流逝速度：原版大约每 0.4 秒掉 1。</summary>
        public const float TimeTickInterval = 0.4f;

        // ---- 敌人工况（速度逐项来自 clone 的敌人 prefab / 脚本，见各条出处）----

        /// <summary>
        /// 栗宝宝行走速度（格/秒）。
        /// <para>出处：clone `Prefabs/Enemies/Brown Goomba.prefab:139` `Speed: {x: 2.5, y: 0}`，
        /// 由 `Scripts/_common/MoveAndFlip.cs:51` 直接写进 `rigidbody.velocity` ⇒ 2.5 格/秒。</para>
        /// </summary>
        public const float GoombaSpeed = 2.5f;

        /// <summary>
        /// 乌龟（走路）速度（格/秒）。
        /// <para>出处：clone `Prefabs/Enemies/Green Koopa.prefab:166` `Speed: {x: 2.5, y: 0}`
        /// （与栗宝宝同一个 `MoveAndFlip`，值也一样）。</para>
        /// </summary>
        public const float KoopaWalkSpeed = 2.5f;

        /// <summary>
        /// 被踢出去的龟壳滑行速度（格/秒）。
        /// <para>出处：clone `Scripts/KoopaShell.cs:12` `rollSpeedX = 7`，由 `:50` 写进
        /// `rigidbody.velocity` ⇒ 7 格/秒。</para>
        /// </summary>
        public const float ShellRollSpeed = 7f;

        /// <summary>
        /// 踩死敌人后马里奥获得的弹跳初速（格/秒）。
        /// <para>
        /// <b>出处</b>：clone <c>Assets/Prefabs/_managers/Level Starter.prefab:1704</c>
        /// <c>stompBounceVelocity: {x: 0, y: 15}</c>；用法见 <c>LevelManager.cs:343-348 MarioStompEnemy()</c>：
        /// <c>mario_Rigidbody2D.velocity = new Vector2(velocity.x + bounce.x, bounce.y)</c>
        /// —— 也就是**直接把竖直速度设成 15**（与站立起跳同一档的起跳初速，见 <see cref="JumpParamsFor"/>）。
        /// </para>
        /// <para>
        /// 「踩到第一个时候不会弹起来一小块，感觉直接碰到第二个了，没有自动连踩」——
        /// 11.25 在普通下落重力下的顶点只有 <b>0.74 格</b>，连一只 1 格高的栗宝宝都过不去；
        /// 15 则是 <b>1.32 格</b>（松手，重力 85.27）/ <b>4.60 格</b>（按住 A，重力 24.44）。
        /// </para>
        /// </summary>
        public const float StompBounce = 15f;

        /// <summary>
        /// 顶砖块时马里奥向上的小反弹（格/秒）。
        /// <para>**本项目新增**：出处待补，登记在「允许的差异」E-22。数值保持原样（= 旧 `3.5 × 1.5`）。</para>
        /// </summary>
        public const float BlockBounce = 5.25f;
        /// <summary>砖块/问号块被顶起来的高度与回落时间。</summary>
        public const float BlockBumpHeight = 0.35f;
        public const float BlockBumpTime = 0.18f;

        // ---- 火球（**一个向量管三处**：水平速度 / 初速下落 / 反弹）----
        //
        // 出处：clone `Assets/Scripts/MarioFireball.cs:8`
        //   `private Vector2 absVelocity = new Vector2 (20, 11);`
        // clone 怎么用它（同一个向量，三处都指它）：
        //   `:21` 初速   `velocity = (directionX * absVelocity.x, -absVelocity.y)`  ⇒ 水平 20、竖直 **向下** 11
        //   `:26` 每帧   `velocity = (directionX * absVelocity.x, velocity.y)`     ⇒ 水平**恒速**（不减速）
        //   `:50` 撞地面 `velocity = (velocity.x, +absVelocity.y)`                 ⇒ 反弹向上 11
        //   `:52` 撞顶   `velocity = (velocity.x, -absVelocity.y)`                 ⇒ 压回向下 11
        // 单位：clone 直接把它写进 `rigidbody.velocity`，而 clone 的 1 世界单位 = 1 格
        //   （同一条证据链：`Mario.cs:36` `maxWalkSpeedX = 5.86` 与本工程量到的走速 5.86 格/秒一致）
        //   ⇒ 就是「格/秒」。
        //
        //   它也不是"减速度后的稳态"——本工程的火球水平分量本来就是恒速（`Fireball.Update` 每帧

        /// <summary>火球水平速度（格/秒，恒速）—— 出处 clone `MarioFireball.cs:8` `.x = 20`（`:21`/`:26` 写进 `velocity`）。</summary>
        public const float FireballSpeed = 20f;

        /// <summary>
        /// 火球竖直速度分量（格/秒）—— 出处 clone `MarioFireball.cs:8` `.y = 11`。
        /// <para>同一个分量兼任三处：初速下落（`:21` `-absVelocity.y`）、撞地反弹（`:50` `+absVelocity.y`）、
        /// 撞顶压回（`:52` `-absVelocity.y`）。原实现的三处各用各的数（`+2` / `12` / `0`）且都没出处。</para>
        /// </summary>
        public const float FireballVelocityY = 11f;

        /// <summary>火球预制体的重力倍率 —— 出处 clone `Assets/Prefabs/Player/Mario Fireball.prefab:64` `m_GravityScale: 5.5`
        /// （与马里奥的 <see cref="GravityScale"/> = 5.3 **不同**：火球是另一个预制体）。</summary>
        private const float FireballGravityScale = 5.5f;

        /// <summary>火球重力（格/秒²）= 9.81 × 5.5 ≈ 53.96 —— 出处见 <see cref="FireballGravityScale"/>。</summary>
        public static readonly float FireballGravity = UnityGravity * FireballGravityScale;

        /// <summary>
        /// 火球存活上限（秒）。
        /// <para>**本项目新增**：clone 的火球**没有寿命字段**（`MarioFireball.cs` 只在撞墙/撞敌人时
        /// `Explode()`，没有计时器、也没有离屏销毁），不加上限会一直弹到关卡尽头 ⇒ 本工程给 4 秒。
        /// 已登记在「允许的差异」E-22。</para>
        /// </summary>
        public const float FireballLife = 4f;
        /// <summary>同屏火球上限（原版是 2）。</summary>
        public const int MaxFireballs = 2;

        // ---- 变身动画 ----
        public const float GrowTime = 0.6f;
        public const float ShrinkTime = 0.6f;
        /// <summary>变身期间刷帧间隔（原版靠快速交替两种形态制造闪烁）。</summary>
        public const float GrowBlinkInterval = 0.06f;

        /// <summary>死亡动画：先原地弹起，再落出屏幕。</summary>
        public const float DeathHopVelocity = 15f;
        public const float DeathDuration = 2.6f;

        /// <summary>旗杆降落速度与走进城堡的速度。</summary>
        public const float FlagSlideSpeed = 5f;
        public const float FlagWalkSpeed = 2.5f;

        // ---- 摄像机 ----
        /// <summary>相机跟随时机：原版只在马里奥走到屏幕中偏左时才推进，且**永不后退**。</summary>
        public const float CameraLeadX = -1.5f;
        public const float CameraSmooth = 0.12f;

    }
}
