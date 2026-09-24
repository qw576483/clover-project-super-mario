using CloverEngine;
using SuperMario.Core;
using SuperMario.Module.Flow;
using SuperMario.Module.Level;
using SuperMario.Module.Player;
using UnityEngine;

namespace SuperMario.Module.CameraRig
{
    /// <summary>相机门面。</summary>
    public interface ICameraRig
    {
        Camera Camera { get; }
        /// <summary>绑定玩家与关卡边界。</summary>
        void Setup(ILevel level, IPlayer player);
        void Tick(float dt);
        /// <summary>屏幕抖动（顶碎砖块、踩敌人时用）。</summary>
        void Shake(float strength);
        /// <summary>过场期间锁住横向推进（旗杆滑落时相机会停住）。</summary>
        void LockX(float x);
        void Clear();
    }

    /// <summary>
    /// 横版相机。
    /// <para>
    /// 三条规则直接照抄原版，缺一条手感就不对：
    /// ① <b>永不后退</b> —— 玩家往回走时相机不动，否则会产生"地图在动"的晕眩感；
    /// ② 玩家走到屏幕<b>中偏左</b>才推进（<see cref="GameConst.CameraLeadX"/>），
    ///    给前方留出视线空间，这是马里奥能提前看到敌人的前提；
    /// ③ 纵向只做<b>有限跟随</b>：马里奥跳起来不该把整个画面拽上去。
    /// </para>
    /// </summary>
    internal sealed class CameraModule : ICameraRig
    {
        public Camera Camera { get; private set; }

        private ILevel _level;
        private IPlayer _player;
        private float _maxReachedX;
        private bool _locked;
        private float _lockedX;
        /// <summary>下一帧是否【直接归位】。进关第一帧必须瞬移到位，否则画面会从上一次的位置"甩"过来。</summary>
        private bool _snap;

        public void Setup(ILevel level, IPlayer player)
        {
            _level = level;
            _player = player;

            if (Camera == null)
            {
                // 取相机走**引擎门面** `Game.Camera.Main`，不再自己 `Camera.main`。
                //
                // 后来改成 `Camera.main`（MainCamera 标签），但那是**绕过门面**的一次静态 tag 查找：
                // 拿到的可能是场景里第二台相机 —— 于是"引擎震屏作用于 A、这里取景算的是 B"。
                //
                // 未就绪时 `Main` 返回 **null**（引擎侧已限频留痕，同 key 5s 一条）⇒ 这里必须判空（G9）。
                Camera = Game.Camera != null ? Game.Camera.Main : null;
                if (Camera == null)
                {
                    Game.Logger.Warn("Camera", "引擎相机门面取不到相机（场景里没有 MainCamera 标签的相机），运行时新建一个（检查场景生成器）");
                    var go = new GameObject("Main Camera");
                    go.tag = "MainCamera";
                    Camera = go.AddComponent<Camera>();
                }
            }

            // 这几项【每局都要重设】，不能只在"新建相机"时设一次。
            // 相机是复用的、背景色还是上一关的天空蓝 —— 地下关变成"蓝底青砖"。
            Camera.orthographic = true;
            // 正交尺寸 = 半高（格）。16:9 下横向能看到 16/9*15 ≈ 26 格，
            // 比原版 4:3 的 16 格宽，所以视野更友好。
            Camera.orthographicSize = GameConst.ScreenTilesY * 0.5f;
            // 地上关：SMB 天空蓝；地下关（1-2）：纯黑。
            // 参考画面 _assets_tmp/SMB-clone/Screenshots/world1-2.jpg 里背景就是全黑。
            Camera.backgroundColor = StageContext.Underground
                ? Color.black
                : new Color(0.36f, 0.58f, 0.99f);
            Camera.clearFlags = CameraClearFlags.SolidColor;

            _maxReachedX = float.MinValue;
            _locked = false;
            // 下一帧【直接归位】，不要平滑 —— 见 Tick 里的注释。
            _snap = true;
            Game.Logger.Info("Camera", $"相机就绪：正交尺寸 {Camera.orthographicSize}");
        }

        public void LockX(float x)
        {
            _locked = true;
            _lockedX = x;
        }

        /// <summary>
        /// 屏幕抖动。**抖动的实现收敛到引擎** <see cref="ICameraManager.Shake"/>（强度随时间线性衰减 +
        /// 圆内随机偏移），本项目只保留"强度 → (时长, 最大偏移)"这一步换算：
        /// <list type="bullet">
        /// <item>最大偏移：旧实现每轴 <c>Random.Range(-1,1) * _shake * 0.2</c> ⇒ 每轴极值 = <c>strength × 0.2</c>
        /// （引擎的 <c>intensity</c> 就是"最大偏移"，口径一致）。</item>
        /// </list>
        /// <para>未就绪（引擎没起）时不静默：留一条 Warn（否则"该抖没抖"会被当成震屏坏了）。</para>
        /// </summary>
        public void Shake(float strength)
        {
            if (Game.Camera == null)
            {
                Game.Logger.Warn("Camera", $"Game.Camera 未就绪，跳过震屏（强度 {strength:F2}）");
                return;
            }
            Game.Camera.Shake(ShakeSeconds, strength * ShakeOffsetScale);
        }

        private const float ShakeSeconds = 1f / 3f;

        private const float ShakeOffsetScale = 0.2f;

        public void Tick(float dt)
        {
            if (Camera == null || _player == null || _player.Transform == null) return;

            var targetX = _locked
                ? _lockedX
                : _player.Transform.position.x + GameConst.CameraLeadX;

            // 永不后退：取历史最大值。
            if (targetX > _maxReachedX) _maxReachedX = targetX;

            var halfW = Camera.orthographicSize * Camera.aspect;
            // 夹在关卡边界内：左边不能露出关卡外的空白，右边不能越过城堡。
            var minX = _level.MinWorldX + halfW;
            var maxX = Mathf.Max(minX, _level.MaxWorldX - halfW);
            // 关卡比视野还窄（管中密室：17 格 < 一屏 26.7 格）时【居中】显示。
            // 不这么做的话，两个边界会被夹成同一个值（贴着关卡左边界），房间就偏在画面最左边，
            // 右边空一大片背景 —— 而原版密室是整屏一间的。此时"相机永不后退"不适用（房间只有一屏）。
            var camX = maxX > minX
                ? Mathf.Clamp(_maxReachedX, minX, maxX)
                : (_level.MinWorldX + _level.MaxWorldX) * 0.5f;

            // 纵向取景：原版 15 行是【固定分配】的 —— 最下 2 行地面、最上 2 行状态栏、
            // 中间为玩法区。所以把屏幕【底边】对准地面底边，地面就自然坐在画面最下方，
            // 下方不会露出多余的空蓝天，顶部也刚好空出约 2 行给 HUD。
            //
            // 地面被顶到画面 2/3 处，下方露出近 3 格的空白蓝天，顶部的云又正好压在
            // HUD 的 WORLD / TIME 文字上，看着像"UI 和场景打架"，其实是取景偏了。
            var groundY = _level.GroundTopY;
            const float GroundThickness = 2f;        // 地面厚度（格）：原版固定 2 行
            var camY = groundY - GroundThickness + Camera.orthographicSize;
            var playerY = _player.Transform.position.y;
            // 只在玩家真的顶到画面顶端时才开始上跟（0.9 而非 0.55：1-1 这种单屏高的关卡
            // 永远不触发，镜头不会因为一次跳跃就晃）。
            var upper = camY + Camera.orthographicSize * 0.9f;
            if (playerY > upper) camY += (playerY - upper) * 0.6f;

            var want = new Vector3(camX, camY, -10f);

            // 进关第一帧【直接归位】。
            //
            // Main Camera，进关时它还停在【上一次的位置】（编辑器里的视角 / 上一关的城堡门口 /
            // 菜单），而这里每帧只用平滑系数往目标位置靠 —— 于是开局那零点几秒，画面是从别处
            // 一路"飞"到马里奥身上的。过场期间（旗杆、结算）才需要平滑跟随；开局需要的是"就在那儿"。
            var t = _snap
                ? 1f
                : (GameConst.CameraSmooth <= 0f
                    ? 1f
                    : 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, GameConst.CameraSmooth)));
            _snap = false;
            var pos = Vector3.Lerp(Camera.transform.position, want, t);

            Camera.transform.position = pos;

            // 让引擎的震屏围绕【本帧刚写下的机位】发生，而不是围绕它自己缓存的旧基准。
            //
            // 引擎 `CameraManager` 把"基准位置"缓存在 `_basePos` 里，**只在 `_posInited == false` 那一帧**
            // 重新读相机当前位置（`Follow` / `Unfollow` 会把 `_posInited` 置回 false），
            // 之后震屏写的是 `_basePos + 偏移`（`Runtime/Presentation/Camera.cs` 的 Tick）。
            // 本项目不走 `Follow`（取景规则是"永不后退 + 中偏左推进"，引擎的等速跟随表达不了），
            // ⇒ 不刷新基准的话，第一次震屏会把镜头瞬移到"引擎首帧那台相机的位置"（菜单/上一关的机位）。
            // `Unfollow()` 只写两个字段（`_target = null` / `_posInited = false`），每帧调用的代价可忽略。
            if (Game.Camera != null) Game.Camera.Unfollow();
        }

        public void Clear()
        {
            _level = null;
            _player = null;
            // 震屏没有本地状态要清：状态在引擎那份（`Game.Camera`）。
            _locked = false;
        }
    }
}
