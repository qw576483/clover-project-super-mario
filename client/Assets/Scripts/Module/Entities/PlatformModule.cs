using System.Collections.Generic;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Module.Flow;
using SuperMario.Module.Level;
using UnityEngine;

namespace SuperMario.Module.Entities
{
    /// <summary>移动平台门面。</summary>
    public interface IMovingPlatforms
    {
        /// <summary>
        /// 生成一台**升降台 spawner**（不是生成一个平台 —— 原版这一件就是 spawner）。
        /// <para>四个量全部来自**原版那一台 spawner**（一个都不在这里推）：</para>
        /// <list type="bullet">
        /// <item><paramref name="spawnPos"/> = `Spawn Pos`（台面出现的落点 = **台面中心**）；</item>
        /// <item><paramref name="downStopY"/> = `Down Stop`（世界 y）；</item>
        /// <item><paramref name="upStopY"/> = `Up Stop`（世界 y）；</item>
        /// <item><paramref name="startDir"/> = `directionY`（+1 向上 / −1 向下）。</item>
        /// </list>
        /// <para>出处（prefab 字段 + clone 场景实例行号）见 `Levels/World1-2.txt` 文件头；
        /// 复核脚本 `原版资源/解析/脚本/check_sections_12.py` 第 5 段。</para>
        /// </summary>
        void Spawn(Vector2 spawnPos, float downStopY, float upStopY, int startDir);

        /// <summary>销毁全部 spawner **以及它们生成的所有平台**（死亡重来 / 回菜单时调）。</summary>
        void Clear();
    }

    /// <summary>
    /// 升降台（原版 `Moving Platform Vertical Spawner`）。
    /// <para>
    /// <b>它是 spawner，不是平台</b>：原版这一件每 <b>1.5 秒</b>生成一台 `Moving Platform Vertical`，
    /// 平台自己在上下止点之间匀速跑一趟、**离屏即销毁**（`DestroyOutOfScreen`）。
    /// 所以 1-2 里"一直有台面在动"是这条生成链的产物，不是两台常驻平台在往返。
    /// </para>
    /// <list type="bullet">
    /// <item>生成周期 1.5 秒、初次 0.75 秒后 —— `Scripts/MovingPlatformVerticalSpawner.cs:14,23,39-47`；</item>
    /// <item>只在马里奥**水平距离 ≤ 40 格**时才生成 —— 同文件 `:15` `minDistanceToMove = 40`；</item>
    /// <item>离屏销毁（Unity `OnBecameInvisible`）—— `_common/DestroyOutOfScreen.cs:21-23`。</item>
    /// </list>
    /// <para>
    /// ⚠️ <b>判"离屏"用的是游戏相机</b>，不是 `Renderer.isVisible`：编辑器里 Scene 视图也是一台相机，
    /// 而 `isVisible` 是"任意相机可见"，会让平台在编辑器里永远不被销毁（只有真机才复现）。
    /// 判据与 `OnBecameInvisible` 同义（可见 → 不可见那一刻销毁），另加一条 30 秒兜底防泄漏。
    /// </para>
    /// </summary>
    internal sealed class MovingPlatformModule : IMovingPlatforms
    {
        private readonly ILevel _level;
        private readonly Transform _root;
        private readonly List<Spawner> _spawners = new List<Spawner>();

        public MovingPlatformModule(ILevel level, Transform root)
        {
            _level = level;
            _root = root;
        }

        public void Spawn(Vector2 spawnPos, float downStopY, float upStopY, int startDir)
        {
            var go = new GameObject("MovingPlatformSpawner");
            go.transform.SetParent(_root, false);
            var s = go.AddComponent<Spawner>();
            s.Init(_level, spawnPos, downStopY, upStopY, startDir);
            _spawners.Add(s);
        }

        public void Clear()
        {
            foreach (var s in _spawners)
            {
                if (s != null) s.Clear();
            }
            _spawners.Clear();
        }
    }

    /// <summary>
    /// 一台 spawner：马里奥在附近时每 1.5 秒生成一个平台（见类注释里的出处）。
    /// </summary>
    internal sealed class Spawner : MonoBehaviour
    {
        /// <summary>生成间隔（秒）—— 出处 `MovingPlatformVerticalSpawner.cs:14` `WaitBetweenSpawn = 1.5f`。</summary>
        private const float WaitBetweenSpawn = 1.5f;

        /// <summary>马里奥多远之内才会生成 —— 出处同文件 `:15` `minDistanceToMove = 40`。</summary>
        private const float MinDistanceToMove = 40f;

        private ILevel _level;
        private Vector2 _spawnPos;
        private float _downStopY;
        private float _upStopY;
        private int _dir;

        /// <summary>倒计时。初值 = 间隔的一半 —— 出处 `:23` `timer = WaitBetweenSpawn / 2;`。</summary>
        private float _timer;

        private readonly List<Platform> _live = new List<Platform>();
        private bool _idleLogged;

        public void Init(ILevel level, Vector2 spawnPos, float downStopY, float upStopY, int startDir)
        {
            _level = level;
            _spawnPos = spawnPos;
            _downStopY = downStopY;
            _upStopY = upStopY;
            _dir = startDir >= 0 ? 1 : -1;
            _timer = WaitBetweenSpawn * 0.5f;
            transform.position = new Vector3(spawnPos.x, spawnPos.y, 0f);
            Game.Logger.Info("Platform",
                $"升降台 spawner 就位：x={spawnPos.x:F2} 出现点 y={spawnPos.y:F2} 行程 {downStopY:F1}..{upStopY:F1} " +
                $"初速方向 {_dir:+0;-0} 生成周期 {WaitBetweenSpawn:F1} 秒（马里奥 {MinDistanceToMove:F0} 格内才生成）");
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (dt > 0.05f) dt = 0.05f;

            // 已经被销毁的平台（离屏）先从名单里摘掉。
            for (var i = _live.Count - 1; i >= 0; i--)
            {
                if (_live[i] == null) _live.RemoveAt(i);
            }

            var px = StageContext.Player != null ? StageContext.Player.FeetPosition.x : float.MaxValue;
            var near = Mathf.Abs(px - _spawnPos.x) <= MinDistanceToMove;
            if (!near)
            {
                // 原版 `isMoving = false` 时倒计时**冻结**（只是不生成，不重置）。
                if (!_idleLogged)
                {
                    _idleLogged = true;
                    Game.Logger.Info("Platform",
                        $"升降台 spawner 待机（马里奥水平距离 {Mathf.Abs(px - _spawnPos.x):F1} > {MinDistanceToMove:F0} 格）");
                }
                return;
            }
            _idleLogged = false;

            _timer -= dt;
            if (_timer > 0f) return;
            _timer = WaitBetweenSpawn;

            var go = new GameObject("MovingPlatform");
            go.transform.SetParent(transform.parent, false);
            var p = go.AddComponent<Platform>();
            p.Init(_level, _spawnPos, _downStopY, _upStopY, _dir);
            _live.Add(p);
            Game.Logger.Info("Platform",
                $"spawner 生成升降台 #{_live.Count}：x={_spawnPos.x:F2} 出现点 y={_spawnPos.y:F2} " +
                $"（每 {WaitBetweenSpawn:F1} 秒一台，离屏即销毁）");
        }

        public void Clear()
        {
            foreach (var p in _live)
            {
                if (p != null) p.Clear();
            }
            _live.Clear();
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }

    /// <summary>
    /// 一个升降台：在**原版给定的**下止点与上止点之间匀速跑，离屏即销毁。
    /// <para>
    /// <b>每一个量全部有出处（一个都不编）</b> —— 值的来源一律是 clone 的
    /// `Prefabs/Platforms/Moving Platform Vertical Spawner.prefab`（+ 场景实例覆写）
    /// 与它生成用的 `Prefabs/Platforms/Moving Platform Vertical.prefab`：
    /// </para>
    /// <list type="bullet">
    /// <item><b>上/下止点</b>：由 spawner 的 `Up Stop` / `Down Stop` 两个子物体的 local y 决定
    /// （`Moving Platform Vertical Spawner.prefab:98` = **+16.5**、`:111` = **−6.5**），
    /// 加上 spawner 根与父容器 `Moving Platform Spawners` 的世界 y（都是 0）⇒
    /// 1-2 两台共用同一条带 **y ∈ [−6.5, +16.5]，行程 23 格**。</item>
    /// <item><b>速度</b>：`Moving Platform Vertical.prefab:172` 的 `absSpeed: 0.05`（单位/帧）
    /// ⇒ ×60fps = **3 格/秒**（与食人花同一套 `PatrolVertical.Update`）。</item>
    /// <item><b>止点停留</b>：同文件 `:175` / `:176` 的 `waitAtUpStop` / `waitAtDownStop` 都是 **0**
    /// ⇒ 到止点立刻反向，不停留。</item>
    /// <item><b>台面尺寸</b>：同文件 `:121` / `:227` 的 `m_Size: {x: 3, y: 0.5}` ——
    /// **3 格宽 × 半格厚**；贴图 = `:222` 引用的 `misc-3.gif` 的 `moving_platform_6`
    /// （rect x=143,y=1201, 48×8 px，脚本 `原版资源/解析/脚本/gen_platform_sprite.py` 可复跑）。
    /// 台面的 transform 位置就是**台面中心**（prefab 里精灵 pivot = 0.5/0.5），
    /// 所以 `Up/Down Stop` 与 `Spawn Pos` 的 y 都是**中心**的 y。</item>
    /// </list>
    /// <para>
    /// ⚠️ <b>并入数据、不再写死在代码里</b>：行程与初速方向随关卡实体行走
    /// （`E &lt;x&gt; &lt;y&gt; MovingPlatform &lt;downStopY&gt; &lt;upStopY&gt; &lt;startDir&gt;`），
    /// 见 <see cref="LevelEntity.HasPatrol"/>。所以这个类里**没有任何"行程"常量** ——
    /// 换关卡/换 spawner 只改数据，不动代码。
    /// </para>
    /// </summary>
    internal sealed class Platform : MonoBehaviour
    {
        /// <summary>速度（格/秒）。出处 = `Moving Platform Vertical.prefab:172` `absSpeed: 0.05` 单位/帧 × 60fps。</summary>
        private const float Speed = 0.05f * 60f;
        /// <summary>止点停留（秒）。出处 = `Moving Platform Vertical.prefab:175/176`，两个 wait 都是 0。</summary>
        private const float WaitAtStop = 0f;
        /// <summary>台面宽度（格）。出处 = `Moving Platform Vertical.prefab:121` `m_Size.x = 3`。</summary>
        private const float Width = 3f;
        /// <summary>台面厚度（格）。出处 = 同处 `m_Size.y = 0.5`。</summary>
        private const float Thickness = 0.5f;

        /// <summary>
        /// 兜底销毁时限（秒）：可见 → 不可见那一刻本应销毁；这一条只防"从来没进过画面"的泄漏
        /// （出处 `DestroyOutOfScreen.cs` 在 Unity 里也有同样情形：从未可见就永远不会触发回调）。
        /// </summary>
        private const float MaxLifetime = 30f;

        private ILevel _level;
        private SpriteRenderer _sr;
        private float _y;                 // **台面中心**的 y
        private float _downStopY;
        private float _upStopY;
        private int _dir;
        private float _wait;
        private float _age;
        private bool _wasVisible;

        /// <summary>已经把台面登记成实心格的格（x, y）—— 离开时按这张表注销。</summary>
        private readonly List<Vector2Int> _cells = new List<Vector2Int>();
        /// <summary>与 <see cref="_cells"/> 一一对应：这一格**原本就是实心**（地形）⇒ 注销时不能把它删掉。</summary>
        private readonly List<bool> _cellsPreexisting = new List<bool>();

        public void Init(ILevel level, Vector2 spawnPos, float downStopY, float upStopY, int startDir)
        {
            _level = level;
            _y = spawnPos.y;
            transform.position = new Vector3(spawnPos.x, _y, 0f);

            _downStopY = downStopY;
            _upStopY = upStopY;
            _dir = startDir >= 0 ? 1 : -1;

            // 台面贴图（3 格宽 × 半格厚）。prefab 的精灵 pivot 是【居中】，本工程 `Sprites/Platform/`
            // 走 `SpriteImportPostprocessor` 的"居中轴心"一档，所以直接挂在台面中心即可。
            var art = new GameObject("Art").transform;
            art.SetParent(transform, false);
            art.localPosition = Vector3.zero;
            _sr = art.gameObject.AddComponent<SpriteRenderer>();
            _sr.sortingOrder = 2;
            Game.Res.LoadAsset<Sprite>(ResPaths.Platform(SpriteNames.MovingPlatform), s =>
            {
                if (s == null)
                {
                    Game.Logger.Error("Platform",
                        $"升降台台面贴图加载失败：{ResPaths.Platform(SpriteNames.MovingPlatform)}");
                    return;
                }
                _sr.sprite = s;
            });

            RegisterCells();
            Game.Logger.Info("Platform",
                $"升降台出现：x={transform.position.x:F2} 中心 y={_y:F2}（台面 {Width:F1}×{Thickness:F1} 格，" +
                $"顶面 y={PlatformTopY:F2}）");
        }

        /// <summary>台面顶部的真实 y（小数）。马里奥站在上面时脚底就在这个高度。</summary>
        private float PlatformTopY => _y + Thickness * 0.5f;

        /// <summary>
        /// 把台面覆盖到的每一格登记成实心 + 登记台面真实顶高；先注销上一帧的那批格。
        /// <para>
        /// ⚠️ 只注销**我们自己加进去的**格：台面横跨 3 格，其中有可能压在**地形**上
        /// （例：x=152.8 的台面覆盖 151..154 格，而 151 格在 y=−2/−1 是地面）——
        /// 无脑 `SetSolid(false)` 会把地形删掉，平台走过后马里奥从地面掉下去（静默、无报错）。
        /// </para>
        /// </summary>
        private void RegisterCells()
        {
            for (var i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                if (!_cellsPreexisting[i]) _level.SetSolid(c.x, c.y, false);
                _level.ClearCarrier(c.x, c.y);
            }
            _cells.Clear();
            _cellsPreexisting.Clear();

            var cellY = Mathf.FloorToInt(PlatformTopY);
            var x0 = Mathf.FloorToInt(transform.position.x - Width * 0.5f);
            var x1 = Mathf.FloorToInt(transform.position.x + Width * 0.5f - 0.0001f);
            for (var tx = x0; tx <= x1; tx++)
            {
                _cellsPreexisting.Add(_level.IsSolidTile(tx, cellY));
                _cells.Add(new Vector2Int(tx, cellY));
                _level.SetSolid(tx, cellY, true);
                _level.SetCarrier(tx, cellY, PlatformTopY);
            }
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (dt > 0.05f) dt = 0.05f;
            _age += dt;

            // 原版 `PatrolVertical.Update`：先按当前方向移动，**撞到止点才反向**（waitAt* = 0 ⇒ 不停留）。
            // 这里把"到点"做成精确吸附 + 反向，语义与之一致（原版是判 `>= UpStop.y` 后由协程反向）。
            if (_wait > 0f)
            {
                _wait -= dt;
            }
            else
            {
                _y += Speed * dt * _dir;
                // ⚠️ 反向必须带上"**正朝着这个止点走**"（`_dir` 的符号）这个条件。
                // 只看 `_y >= 上止点` 的话，`dt == 0` 时（`Time.timeScale = 0`：暂停 / 结算屏）
                // `_y` 会**恰好停在**止点上，于是每帧都判"到点了"⇒ 反向 + 打日志每帧一次（实测踩过）。
                if (_dir > 0 && _y >= _upStopY) { _y = _upStopY; _dir = -1; _wait = WaitAtStop; LogStop(true); }
                else if (_dir < 0 && _y <= _downStopY) { _y = _downStopY; _dir = 1; _wait = WaitAtStop; LogStop(false); }
            }

            transform.position = new Vector3(transform.position.x, _y, 0f);
            RegisterCells();

            // ── 离屏即销毁（原版 `DestroyOutOfScreen.OnBecameInvisible`）──
            var onScreen = OnGameCamera();
            if (onScreen)
            {
                _wasVisible = true;
            }
            else if (_wasVisible)
            {
                Game.Logger.Info("Platform",
                    $"升降台离屏销毁：x={transform.position.x:F2} 中心 y={_y:F2}（可见过 → 现在不可见，" +
                    $"原版 DestroyOutOfScreen 的行为）");
                Clear();
            }
            else if (_age > MaxLifetime)
            {
                Game.Logger.Warn("Platform",
                    $"升降台 {MaxLifetime:F0} 秒都没进过画面，兜底销毁（x={transform.position.x:F2} y={_y:F2}）" +
                    " —— 原版靠 OnBecameInvisible，从未可见时那个回调不会触发，故这里补一条兜底");
                Clear();
            }
        }

        /// <summary>
        /// 台面是否落在**游戏相机**的取景框内（判据见类注释：不用 `Renderer.isVisible`）。
        /// </summary>
        private bool OnGameCamera()
        {
            var cam = Camera.main;
            if (cam == null) return true;      // 没有相机 ⇒ 不敢销毁（宁可留着）
            var halfH = cam.orthographicSize;
            var halfW = halfH * cam.aspect;
            var c = cam.transform.position;
            var left = transform.position.x - Width * 0.5f;
            var right = transform.position.x + Width * 0.5f;
            var bottom = _y - Thickness * 0.5f;
            return right > c.x - halfW && left < c.x + halfW
                && PlatformTopY > c.y - halfH && bottom < c.y + halfH;
        }

        /// <summary>到达止点（离散事件、频率低）—— 留一条，便于和原版行程对照。</summary>
        private void LogStop(bool up)
        {
            Game.Logger.Info("Platform",
                $"移动平台到达{(up ? "上" : "下")}止点 y={_y:F1}（行程 {_downStopY:F1}..{_upStopY:F1}，" +
                $"速度 {Speed:F2} 格/秒，止点停留 {WaitAtStop:F1} 秒）");
        }

        public void Clear()
        {
            if (_level != null)
            {
                for (var i = 0; i < _cells.Count; i++)
                {
                    var c = _cells[i];
                    if (!_cellsPreexisting[i]) _level.SetSolid(c.x, c.y, false);
                    _level.ClearCarrier(c.x, c.y);
                }
            }
            _cells.Clear();
            _cellsPreexisting.Clear();
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
