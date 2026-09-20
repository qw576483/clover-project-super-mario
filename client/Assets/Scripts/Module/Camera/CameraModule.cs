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
        private float _shake;
        private float _shakeDecay;
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
                // ★ 取场景相机走【MainCamera 标签】，不再按对象名 GameObject.Find("Main Camera")。
                //
                // 踩过的坑（历史债 E-5）：按名字找意味着"改个名/挪一层级就静默失效"，
                // 而且它还会误命中同名的非相机对象。标签由场景生成器（ProjectBuilder）打在
                // 相机上，是 Unity 自带约定，切场景后 Camera.main 会自动指向当前场景那台。
                Camera = Camera.main;
                if (Camera == null)
                {
                    Game.Logger.Warn("Camera", "场景里没有 MainCamera 标签的相机，运行时新建一个（检查场景生成器）");
                    var go = new GameObject("Main Camera");
                    go.tag = "MainCamera";
                    Camera = go.AddComponent<Camera>();
                }
            }

            // ★ 这几项【每局都要重设】，不能只在"新建相机"时设一次。
            // 踩过的坑：原来整段都塞在 `if (Camera == null)` 里，于是从 1-1 进 1-2 时
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
            // ★ 下一帧【直接归位】，不要平滑 —— 见 Tick 里的注释。
            _snap = true;
            Game.Logger.Info("Camera", $"相机就绪：正交尺寸 {Camera.orthographicSize}");
        }

        public void LockX(float x)
        {
            _locked = true;
            _lockedX = x;
        }

        public void Shake(float strength)
        {
            _shake = Mathf.Max(_shake, strength);
            _shakeDecay = 1f;
        }

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
            // 踩过的坑：原先写的是 groundY + orthoSize * 0.35，把相机抬得偏高 ——
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

            // ★ 进关第一帧【直接归位】。
            //
            // 踩过的坑（症状："游戏开始的时候画面从别的地方甩过来"）：相机对象是场景里的
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

            if (_shake > 0.001f)
            {
                _shakeDecay -= dt * 3f;
                if (_shakeDecay <= 0f) _shake = 0f;
                else
                {
                    pos.x += Random.Range(-1f, 1f) * _shake * 0.2f;
                    pos.y += Random.Range(-1f, 1f) * _shake * 0.2f;
                }
            }

            Camera.transform.position = pos;
        }

        public void Clear()
        {
            _level = null;
            _player = null;
            _shake = 0f;
            _locked = false;
        }
    }
}
