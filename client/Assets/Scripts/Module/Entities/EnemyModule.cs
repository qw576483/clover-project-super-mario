using System.Collections.Generic;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Module.Audio;
using SuperMario.Module.Flow;
using SuperMario.Module.Level;
using UnityEngine;

namespace SuperMario.Module.Entities
{
    /// <summary>一个敌人的对外可见状态（玩法层判定用）。</summary>
    public interface IEnemy
    {
        Rect Bounds { get; }
        /// <summary>是否已死（死了就不参与判定，等它演完自己消失）。</summary>
        bool Dead { get; }
        /// <summary>演出结束，可以销毁了（门面据此回收）。</summary>
        bool Finished { get; }
        /// <summary>踩死 / 踩到壳上。</summary>
        void Stomp();
        /// <summary>
        /// <b>从上方踩它是否安全</b>。
        /// <para>
        /// 原版里绝大多数敌人踩上去是安全的（栗宝宝踩扁、乌龟缩壳），
        /// 但 <b>食人花是唯一例外</b>：跳上去照样受伤（它只能被火球 / 无敌星消灭）。
        /// 没有这个开关时，玩法层的"踩中"分支会把食人花当成安全踩 —— 白白送分且不受伤。
        /// </para>
        /// </summary>
        bool Stompable { get; }
        /// <summary>
        /// 从侧面碰到。<b>返回值 = 这次接触是否已经被敌人自己处理掉</b>：
        /// <c>true</c> ⇒ 不该伤到马里奥（例：踢到静止的龟壳）；<c>false</c> ⇒ 马里奥受伤。
        /// </summary>
        bool TouchFromSide();
        /// <summary>被火球 / 龟壳撞死：翻肚皮掉下去。</summary>
        void Flip();
        void Clear();
    }

    /// <summary>敌人门面。</summary>
    public interface IEnemies
    {
        IReadOnlyList<IEnemy> Active { get; }
        /// <summary>在指定世界坐标创建一个栗宝宝。</summary>
        void SpawnGoomba(Vector2 feetPos);
        /// <summary>在指定世界坐标创建一个乌龟（脚底对齐）。</summary>
        void SpawnKoopa(Vector2 feetPos);
        /// <summary>在指定世界坐标创建一个食人花（坐标 = 它根部所在格的中心）。</summary>
        void SpawnPiranha(Vector2 pos);
        void Clear();
    }

    internal sealed class EnemyModule : IEnemies
    {
        private readonly List<IEnemy> _active = new List<IEnemy>();
        public IReadOnlyList<IEnemy> Active => _active;

        private readonly ILevel _level;
        private readonly IAudio _audio;
        private readonly Transform _root;

        public EnemyModule(ILevel level, IAudio audio, Transform root)
        {
            _level = level;
            _audio = audio;
            _root = root;
        }

        // ───────── 为什么敌人**不进对象池**（引擎 G5 只要求"战斗内高频对象"入池）─────────
        //
        // G5 的原话是"战斗内 GameObject 一律走对象池"。本项目要判的是"这一件算不算战斗内高频物"：
        //   · 数量：一名敌人 = 关卡数据里的一行（`E <x> <y> Goomba`），1-1 整关几十个、
        //     同屏最多几个 —— 不是"一次攻击/一次事件就冒一批"的子弹 / 碎片 / 飘字那类；
        //   · 生命周期：**= 一整关**（踩死 / 被撞死是提前结束，不是"高频短命"）。
        // ⇒ 现造即可，入池反而要为一整关的敌人都留着实例。
        // 对照：本项目真正入池的是**升降台**（每 1.5 秒生成一台、离屏即销毁，一局几十上百次）——
        // 见 `MovingPlatformPool`。那边有明确的"高频 + 短命"两个判据。
        // （`new GameObject` + `AddComponent` 出现在这里属**判定后的保留**，不是漏掉。）
        public void SpawnGoomba(Vector2 feetPos)
        {
            var go = new GameObject("Goomba");
            go.transform.SetParent(_root, false);
            var e = go.AddComponent<Goomba>();
            e.Init(_level, _audio, feetPos);
            _active.Add(e);
        }

        public void SpawnKoopa(Vector2 feetPos)
        {
            var go = new GameObject("Koopa");
            go.transform.SetParent(_root, false);
            var e = go.AddComponent<Koopa>();
            e.Init(_level, _audio, feetPos);
            _active.Add(e);
        }

        public void SpawnPiranha(Vector2 pos)
        {
            var go = new GameObject("Piranha");
            go.transform.SetParent(_root, false);
            var e = go.AddComponent<Piranha>();
            e.Init(_level, _audio, pos);
            _active.Add(e);
        }

        public void Clear()
        {
            foreach (var e in _active) e.Clear();
            _active.Clear();
        }

        /// <summary>清掉已经演完的敌人（每帧由玩法层调一次）。</summary>
        public void Reap()
        {
            // 走接口上的 Finished，而不是 `is Goomba g` ——
            // 敌人演完了却永远不回收，数量只增不减（而且不报错）。
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                if (!_active[i].Finished) continue;
                _active[i].Clear();
                _active.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// 栗宝宝（Goomba）。
    /// <para>
    /// 行为按原版：向左匀速走、撞墙掉头、**会走出平台掉下去**（原版确实会掉，
    /// 不会在边缘停住 —— 那是后来的作品才有的"聪明"敌人）。
    /// </para>
    /// 物理同样走 <see cref="ILevel"/> 的格子位图，与马里奥共一套空间事实。
    /// </summary>
    internal sealed class Goomba : MonoBehaviour, IEnemy
    {
        public Rect Bounds { get; private set; }
        public bool Dead { get; private set; }
        public bool Finished { get; private set; }

        private static readonly Vector2 Size = new Vector2(0.85f, 0.9f);

        private ILevel _level;
        private IAudio _audio;
        private SpriteRenderer _sr;
        private Sprite _walkSprite;
        private Sprite _flatSprite;

        private float _dir = -1f;
        private float _vy;
        private bool _flipped;
        private float _flatTimer;
        private float _animTimer;
        private bool _animFlip;

        public void Init(ILevel level, IAudio audio, Vector2 feetPos)
        {
            _level = level;
            _audio = audio;
            transform.position = new Vector3(feetPos.x, feetPos.y, 0f);
            _sr = gameObject.AddComponent<SpriteRenderer>();
            _sr.sortingOrder = 5;
            RecomputeBounds();

            // 名字与目录必须和 SpriteNames 里登记的一致，别在这里手写。
            //
            // 这两个文件根本不存在（栗宝宝不是 smb_enemies_sheet 里的切片，而是
            // smb1_misc_sprites 这张"混合表"里的 385 / 399 号，导入时按表名落在 Items/ 下）。
            // 后果不是报错了事：加载回调不来，栗宝宝就是一个【没有贴图的空白 SpriteRenderer】，
            // 画面上一排隐形敌人，撞上去才掉血 —— 比直接崩还难查。
            // 统一走 SpriteNames 常量，改名时只有一处要改。
            Game.Res.LoadAsset<Sprite>(ResPaths.Item(SpriteNames.GoombaWalk), s =>
            {
                _walkSprite = s;
                _sr.sprite = s;
                if (s == null)
                    Game.Logger.Error("Enemy", $"栗宝宝贴图加载失败：{ResPaths.Item(SpriteNames.GoombaWalk)}");
            });
            Game.Res.LoadAsset<Sprite>(ResPaths.Item(SpriteNames.GoombaFlat), s => _flatSprite = s);
        }

        private void RecomputeBounds()
        {
            var p = transform.position;
            Bounds = new Rect(p.x - Size.x * 0.5f, p.y, Size.x, Size.y);
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt > 0.05f) dt = 0.05f;

            if (Dead)
            {
                if (_flipped)
                {
                    // 翻肚皮：只受重力，落地后停止，然后淡出。
                    _vy -= GameConst.Gravity * dt;
                    var p = transform.position + new Vector3(0f, _vy * dt, 0f);
                    if (p.y < -10f) { Finished = true; return; }
                    transform.position = p;
                    RecomputeBounds();
                    return;
                }

                // 被踩扁：静止一小会儿再消失（原版是压成一片然后消失）。
                _flatTimer -= dt;
                if (_flatTimer <= 0f) Finished = true;
                return;
            }

            // 走过去掉下平台：不做边缘检测，原版就是这么"傻"。
            _vy -= GameConst.Gravity * dt;
            if (_vy < -GameConst.MaxFallSpeed) _vy = -GameConst.MaxFallSpeed;

            // 2.5 格/秒 出处 = clone `Prefabs/Enemies/Brown Goomba.prefab:139` `Speed: {x: 2.5, y: 0}`
            // （见 `GameConst.GoombaSpeed`）。
            var dx = _dir * GameConst.GoombaSpeed * dt;

            // X
            var nx = transform.position.x + dx;
            var rect = new Rect(nx - Size.x * 0.5f, transform.position.y, Size.x, Size.y);
            foreach (var c in Overlap(rect))
            {
                if (!_level.IsSolidTile(c.x, c.y)) continue;
                _dir = -_dir;
                nx = transform.position.x;
                break;
            }

            // Y
            var ny = transform.position.y + _vy * dt;
            var vrect = new Rect(nx - Size.x * 0.5f, ny, Size.x, Size.y);
            foreach (var c in Overlap(vrect))
            {
                if (!_level.IsSolidTile(c.x, c.y)) continue;
                if (_vy <= 0f) { ny = c.y + 1f; _vy = 0f; }
                else { ny = c.y - Size.y; _vy = 0f; }
                break;
            }

            transform.position = new Vector3(nx, ny, 0f);
            RecomputeBounds();

            // 走路的"两帧"用水平翻转实现（原版素材包里栗宝宝只有一个行走姿势）。
            _animTimer += dt;
            if (_animTimer >= 0.18f)
            {
                _animTimer = 0f;
                _animFlip = !_animFlip;
                if (_sr != null) _sr.flipX = _animFlip;
            }

            if (transform.position.y < -12f) Finished = true;
        }

        public void Stomp()
        {
            if (Dead) return;
            Dead = true;
            _flatTimer = 0.35f;
            _vy = 0f;
            if (_sr != null)
            {
                _sr.sprite = _flatSprite != null ? _flatSprite : _walkSprite;
                _sr.flipX = false;
            }
            _audio?.PlaySfx(Sfx.Stomp);
        }

        public void Flip()
        {
            if (Dead) return;
            Dead = true;
            _flipped = true;
            _vy = 6f;                       // 被撞飞一点再落下
            if (_sr != null) _sr.flipY = true;
            _audio?.PlaySfx(Sfx.Break);
        }

        public void Clear()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        /// <summary>栗宝宝踩上去是安全的（原版踩扁）。</summary>
        public bool Stompable => true;

        /// <summary>栗宝宝从侧面碰过来就是受伤，不存在"被它处理掉"的情况。</summary>
        public bool TouchFromSide() => false;

        private static IEnumerable<Vector2Int> Overlap(Rect r) => EnemyGrid.Overlap(r);
    }

    /// <summary>
    /// 敌人与地形做格子碰撞时共用的"矩形覆盖了哪些格"。
    /// <para>提升到文件级：原先它是 <c>Goomba</c> 的私有方法，新增乌龟时复制一份就会出现两套边界处理
    /// （而且 <c>-0.0001f</c> 这种细节很容易只改一处）。</para>
    /// </summary>
    internal static class EnemyGrid
    {
        public static IEnumerable<Vector2Int> Overlap(Rect r)
        {
            var x0 = Mathf.FloorToInt(r.xMin);
            var x1 = Mathf.FloorToInt(r.xMax - 0.0001f);
            var y0 = Mathf.FloorToInt(r.yMin);
            var y1 = Mathf.FloorToInt(r.yMax - 0.0001f);
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    yield return new Vector2Int(x, y);
        }
    }

    /// <summary>
    /// 乌龟（Koopa）。行为按原版：
    /// <list type="bullet">
    /// <item>走路时匀速走、撞墙掉头、会走出平台掉下去；</item>
    /// <item>被踩 ⇒ **缩进壳里静止**（不是死）；</item>
    /// <item>从侧面碰静止的壳 ⇒ **踢出去**（壳开始滑行，碰到别的敌人会把它们撞飞）；</item>
    /// <item>滑行的壳被踩 ⇒ 停住；滑行的壳从侧面碰到马里奥 ⇒ 受伤；</item>
    /// <item>被火球 / 无敌星撞 ⇒ 翻肚皮掉下去（真死）。</item>
    /// </list>
    /// </summary>
    internal sealed class Koopa : MonoBehaviour, IEnemy, IMovingShell
    {
        private enum Mode { Walk, ShellStill, ShellMoving, Flipped }

        public Rect Bounds { get; private set; }
        public bool Dead => _mode == Mode.Flipped;
        public bool Finished { get; private set; }
        /// <summary>是否正在滑行 —— 玩法层据此让壳撞飞别的敌人。</summary>
        public bool IsMovingShell => _mode == Mode.ShellMoving;

        // 尺寸按贴图实测（贴图换了，盒子必须一起改，否则"踩不到头"）：
        //   走路帧 38/40 的内容高 24 像素 = 1.5 格 —— 原版乌龟就是 1.5 格高，比小马里奥高一头；
        //   壳 42 的内容高约 14 像素 ≈ 0.9 格。
        // 之前那版暗色乌龟贴图只有 17 像素高，盒子才写成 1.05；换回正确贴图后必须放大。
        private static readonly Vector2 WalkSize = new Vector2(0.9f, 1.5f);
        private static readonly Vector2 ShellSize = new Vector2(0.9f, 0.9f);

        private ILevel _level;
        private IAudio _audio;
        private SpriteRenderer _sr;
        private Sprite _walkSprite;
        private Sprite _walkSprite2;         // 第二条腿
        private Sprite _shellSprite;

        /// <summary>自转轴心（放在贴图中心高度）与它下面的贴图节点 —— 见 Init 的注释。</summary>
        private Transform _pivot;
        private Transform _art;
        /// <summary>滑行时壳的自转角度（度）。</summary>
        private float _spin;

        private Mode _mode = Mode.Walk;
        private float _dir = -1f;
        private float _vy;
        private float _animTimer;
        private int _walkFrame;              // 0/1：走路是【换图】，不是左右翻
        private float _shellStillTimer;      // 静止的壳过一会儿自动复活（原版也是）

        public void Init(ILevel level, IAudio audio, Vector2 feetPos)
        {
            _level = level;
            _audio = audio;
            transform.position = new Vector3(feetPos.x, feetPos.y, 0f);
            RecomputeBounds();

            // 贴图挂在【两级子节点】上，专门为了能绕"壳中心"自转：
            //      Koopa（脚底，物理/碰撞都读这个 transform）
            //       └─ Pivot（上移 = 贴图中心高度，转它 = 绕中心转）
            //           └─ Art（下移同样距离，SpriteRenderer 在这）
            // 这样转 Pivot 时可见位置一点不动，而 transform.position 始终是脚底 ——
            // 物理、落地对齐、Bounds 全都不受影响。
            // 直接转 Koopa 自己是不行的：贴图轴心在【底部居中】，转起来是绕脚底转，
            // 壳会像被甩出去一样划一个圈，看着比不转还怪。
            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(transform, false);
            _art = new GameObject("Art").transform;
            _art.SetParent(_pivot, false);
            _sr = _art.gameObject.AddComponent<SpriteRenderer>();
            _sr.sortingOrder = 5;
            AlignPivotToCenter();

            Game.Res.LoadAsset<Sprite>(ResPaths.Enemy(SpriteNames.KoopaWalk), s =>
            {
                _walkSprite = s;
                if (_mode == Mode.Walk) _sr.sprite = s;
                if (s == null) Game.Logger.Error("Enemy", $"乌龟贴图加载失败：{ResPaths.Enemy(SpriteNames.KoopaWalk)}");
                AlignPivotToCenter();   // 贴图高度决定轴心高度，加载完要重算
            });
            // 走路第二帧。取不到就退回第一帧（静态乌龟，总比整只消失好）。
            Game.Res.LoadAsset<Sprite>(ResPaths.Enemy(SpriteNames.KoopaWalk2), s =>
            {
                _walkSprite2 = s ?? _walkSprite;
                if (s == null) Game.Logger.Error("Enemy", $"乌龟第二帧加载失败：{ResPaths.Enemy(SpriteNames.KoopaWalk2)}");
            });
            Game.Res.LoadAsset<Sprite>(ResPaths.Enemy(SpriteNames.KoopaShell), s =>
            {
                _shellSprite = s;
                if (_mode != Mode.Walk) _sr.sprite = s;
                if (s == null) Game.Logger.Error("Enemy", $"龟壳贴图加载失败：{ResPaths.Enemy(SpriteNames.KoopaShell)}");
                AlignPivotToCenter();
            });
        }

        /// <summary>
        /// 把自转轴心挪到【当前贴图的中心高度】，贴图再反向挪回来 ——
        /// 于是"绕轴心转"等价于"绕贴图中心转"，可见位置不变。
        /// 站姿与壳的贴图高度不同，所以每次换形态 / 贴图加载完都要重算。
        /// </summary>
        private void AlignPivotToCenter()
        {
            if (_pivot == null || _art == null) return;
            var half = (_sr != null && _sr.sprite != null) ? _sr.sprite.bounds.size.y * 0.5f : 0.5f;
            _pivot.localPosition = new Vector3(0f, half, 0f);
            _art.localPosition = new Vector3(0f, -half, 0f);
        }

        private void RecomputeBounds()
        {
            var size = _mode == Mode.Walk ? WalkSize : ShellSize;
            var p = transform.position;
            Bounds = new Rect(p.x - size.x * 0.5f, p.y, size.x, size.y);
        }

        private void EnterShell(Mode mode)
        {
            _mode = mode;
            // 缩壳时体型变矮：把脚底对齐（原版踩下去就是从站立变成贴地）
            _vy = 0f;
            _shellStillTimer = 6f;
            if (_sr != null)
            {
                _sr.sprite = _shellSprite != null ? _shellSprite : _walkSprite;
                _sr.flipY = false;
                _sr.flipX = false;
            }
            // 停下来的壳不转：角度与轴心都复位（轴心要跟着"壳的贴图高度"重算）。
            _spin = 0f;
            if (_pivot != null) _pivot.localRotation = Quaternion.identity;
            AlignPivotToCenter();
            RecomputeBounds();
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt > 0.05f) dt = 0.05f;
            if (Finished) return;

            if (_mode == Mode.Flipped)
            {
                _vy -= GameConst.Gravity * dt;
                var p = transform.position + new Vector3(0f, _vy * dt, 0f);
                if (p.y < -10f) { Finished = true; return; }
                transform.position = p;
                RecomputeBounds();
                return;
            }

            // 静止的壳：过一会儿自己爬出来（原版 6 秒左右）
            if (_mode == Mode.ShellStill)
            {
                _shellStillTimer -= dt;
                if (_shellStillTimer <= 0f)
                {
                    _mode = Mode.Walk;
                    _dir = -1f;
                    if (_sr != null) _sr.sprite = _walkSprite;
                    RecomputeBounds();
                }
                ApplyGravityAndCollide(dt, 0f);
                return;
            }

            // 速度逐项有出处（`GameConst` 里带着 `文件:行`）：
            //   走路 = `Green Koopa.prefab:166` 的 `Speed: {x: 2.5, y: 0}`；
            //   滑行的壳 = `KoopaShell.cs:12` 的 `rollSpeedX = 7`。
            var speed = _mode == Mode.ShellMoving ? GameConst.ShellRollSpeed : GameConst.KoopaWalkSpeed;
            ApplyGravityAndCollide(dt, _dir * speed * dt);

            if (_mode == Mode.Walk)
            {
                // 走路：在【两张贴图】之间切换。
                //
                // 两个后果：① 乌龟看着像在原地"打转"，不像迈腿；
                // ② flipX 同时承担【朝向】职责，动画与朝向互相覆盖 ⇒ 朝哪边都乱。
                // 正确做法：**朝向**交给 flipX（贴图画的是朝左，往右走才翻），
                // **迈腿**交给换图。
                _animTimer += dt;
                if (_animTimer >= 0.16f)
                {
                    _animTimer = 0f;
                    _walkFrame ^= 1;
                }
                if (_sr != null)
                {
                    var s = _walkFrame == 0 ? _walkSprite : _walkSprite2;
                    if (s != null) _sr.sprite = s;
                    // 绿龟这两张贴图画的是【朝右】（实测头部像素的重心在右侧），
                    // 所以往【左】走才需要翻 —— 与之前那版暗色乌龟（朝左）刚好相反。
                    // 搞反了的表现是"乌龟倒着走"。
                    _sr.flipX = _dir < 0f;
                }
            }
            else if (_mode == Mode.ShellMoving)
            {
                // 被踢出去的壳【边滑边转】。
                // 每帧 720°（一秒两圈）：壳在滑行时"转起来"才看得出它是滚出去的，否则
                // 就是一只深色无头的壳贴地平移，看着很怪（实测就是这么被指出来的）。
                // 转的是 Pivot（壳中心），不是 Koopa 自己 —— 见 Init 的注释。
                _spin += 720f * dt;
                if (_spin >= 360f) _spin -= 360f;
                if (_pivot != null) _pivot.localRotation = Quaternion.Euler(0f, 0f, -_dir * _spin);
            }

            if (transform.position.y < -12f) Finished = true;
        }

        /// <summary>重力 + 与地形格子的碰撞。<paramref name="dx"/> = 这一帧的水平位移。</summary>
        private void ApplyGravityAndCollide(float dt, float dx)
        {
            var size = _mode == Mode.Walk ? WalkSize : ShellSize;
            _vy -= GameConst.Gravity * dt;
            if (_vy < -GameConst.MaxFallSpeed) _vy = -GameConst.MaxFallSpeed;

            var nx = transform.position.x + dx;
            var rect = new Rect(nx - size.x * 0.5f, transform.position.y, size.x, size.y);
            foreach (var c in EnemyGrid.Overlap(rect))
            {
                if (!_level.IsSolidTile(c.x, c.y)) continue;
                if (_mode == Mode.ShellMoving) _audio?.PlaySfx(Sfx.Bump);
                _dir = -_dir;
                nx = transform.position.x;
                break;
            }

            var ny = transform.position.y + _vy * dt;
            var vrect = new Rect(nx - size.x * 0.5f, ny, size.x, size.y);
            foreach (var c in EnemyGrid.Overlap(vrect))
            {
                if (!_level.IsSolidTile(c.x, c.y)) continue;
                if (_vy <= 0f) { ny = c.y + 1f; _vy = 0f; }
                else { ny = c.y - size.y; _vy = 0f; }
                break;
            }

            transform.position = new Vector3(nx, ny, 0f);
            RecomputeBounds();
        }

        public void Stomp()
        {
            if (_mode == Mode.Flipped) return;

            // 乌龟踩上去是安全的（原版：踩成壳）。
            switch (_mode)
            {
                case Mode.Walk:
                    // 踩下去 ⇒ 缩进壳里（不是死）
                    EnterShell(Mode.ShellStill);
                    _audio?.PlaySfx(Sfx.Stomp);
                    return;
                case Mode.ShellStill:
                    // 再踩一下 ⇒ 踢出去（滑行的方向 = 马里奥来的方向，这里用"远离马里奥"近似）
                    Kick(_dir);
                    return;
                default:
                    // 滑行中被踩 ⇒ 停住
                    EnterShell(Mode.ShellStill);
                    _audio?.PlaySfx(Sfx.Stomp);
                    return;
            }
        }

        /// <summary>乌龟踩上去是安全的（原版：踩成壳 / 踩停滑行的壳）。</summary>
        public bool Stompable => true;

        /// <summary>把壳踢出去（方向 = dir）。</summary>
        public void Kick(float dir)
        {
            _mode = Mode.ShellMoving;
            _dir = dir >= 0f ? 1f : -1f;
            if (_sr != null) _sr.flipX = _dir < 0f;   // 贴图朝右，往左滑行才翻（同走路那处）
            _audio?.PlaySfx(Sfx.Bump);
            AlignPivotToCenter();   // 自转轴心按当前贴图高度重算，转起来才是绕壳中心
            RecomputeBounds();
        }

        public bool TouchFromSide()
        {
            // 静止的壳：被从侧面碰 ⇒ 踢走，马里奥不受伤（原版如此）。
            // 踢的方向 = 远离马里奥（拿玩家位置比较，别写死）。
            if (_mode == Mode.ShellStill)
            {
                var px = StageContext.Player != null ? StageContext.Player.Bounds.center.x : transform.position.x;
                Kick(transform.position.x >= px ? 1f : -1f);
                return true;
            }

            // 走路的乌龟 / 滑行的壳 ⇒ 马里奥受伤
            return false;
        }

        public void Flip()
        {
            if (_mode == Mode.Flipped) return;
            _mode = Mode.Flipped;
            _vy = 6f;
            if (_sr != null)
            {
                // 被火球 / 龟壳撞死 = 整只【翻肚皮】（原版就是这样），用走路贴图竖着翻过来，
                // 不是换成壳。贴图轴心是【底部居中】，flipY 是绕着脚底镜的 ——
                // 不把位置抬起来的话，翻过来的身体会出现在地面【以下】（像陷进地里）。
                _sr.sprite = _walkSprite;
                _sr.flipY = true;
                transform.position += new Vector3(0f, WalkSize.y, 0f);
            }
            _audio?.PlaySfx(Sfx.Break);
        }

        public void Clear()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }

    /// <summary>滑行的龟壳停不下来时会撞死别的敌人 —— 由玩法层查询。</summary>
    internal interface IMovingShell
    {
        bool IsMovingShell { get; }
        Rect Bounds { get; }
    }

    /// <summary>
    /// 食人花（Piranha）。行为按原版：
    /// <list type="bullet">
    /// <item>固定长在管口，**上下伸缩**（升 → 停 → 降 → 停），不水平移动；</item>
    /// <item>马里奥靠得太近 ⇒ **缩回管里 / 不再伸出来**（原版唯一的"躲"，所以站在管口等它不出来）；</item>
    /// <item>**不能踩**（踩上去马里奥受伤），消灭它要用火球 / 无敌星。</item>
    /// </list>
    /// <para>
    /// <b>几何与节奏全部来自原版参考工程</b>（不许自己定一个"合理值"）：
    /// <c>原版资源/参考工程/SMB-clone/Assets/Prefabs/Enemies/Teal Piranha.prefab</c> ——
    /// 根节点下挂三件：植物本体（<c>Piranha</c>）、<c>Up Stop</c>（local y=**+2**）、
    /// <c>Down Stop</c>（local y=**−1**）；巡逻脚本 <c>Assets/Scripts/_common/PatrolVertical.cs</c>
    /// 把植物本体在上下止点之间来回移动，参数写在同一份预制体上：
    /// <c>absSpeed: 0.05</c>（每帧，60fps ⇒ **3 格/秒**）、<c>waitAtUpStop: 1.5</c>、
    /// <c>waitAtDownStop: 1.5</c>；碰撞盒 <c>m_Size: {{x: 1, y: 1.5}}</c>；
    /// 躲避距离在 <c>Assets/Scripts/Piranha.cs:12</c>：<c>maxDistanceToMove = 2</c>。
    /// </para>
    /// <para>
    /// <b>竖直位置锚在管口上、而且是量出来的</b>：<c>MeasurePipeMouth</c> 从植物所在格往上一路查
    /// 关卡自己的实心位图，取最上面那格的上沿 = 管口顶面；伸出时植物**底边 = 管口**
    /// （整棵花都在管口之上），缩回 = 再往下 <c>TravelTiles</c>（= 预制体 Up Stop +2 − Down Stop −1）格。
    /// </para>
    /// <para>
    /// 只有头顶约 0.3 格露在外面，看着像"花不出来、只露出一截根"（用户实测）。
    /// 本工程精灵轴心是**底部居中**，所以 <c>transform.position.y</c> = 植物**底边**。
    /// </para>
    /// <para>
    /// <b>贴图要画在管砖后面</b>（sortingOrder = −1 &lt; 地形 0）：原版管子是"后半 + 前半"两层、
    /// 植物夹在中间，所以缩回去时被管口挡住；本工程管子是平铺的单层瓦片，只能靠"画在地形之后"
    /// 达到同样效果（伸出管口之后那一段本来就没有瓦片，照常可见）。
    /// </para>
    /// </summary>
    internal sealed class Piranha : MonoBehaviour, IEnemy
    {
        public Rect Bounds { get; private set; }
        public bool Dead { get; private set; }
        public bool Finished { get; private set; }

        /// <summary>碰撞盒尺寸：出处 = 预制体 <c>m_Size: {x: 1, y: 1.5}</c>。</summary>
        private static readonly Vector2 Size = new Vector2(1.0f, 1.5f);

        /// <summary>升 / 降各走完 3 格（Up Stop +2 → Down Stop −1）所需时间：3 格 ÷ 3 格/秒。</summary>
        private const float RiseTime = 1.0f;
        /// <summary>出处 = 预制体 <c>waitAtUpStop: 1.5</c>。</summary>
        private const float HoldOutTime = 1.5f;
        /// <summary>出处 = 预制体 <c>waitAtDownStop: 1.5</c>。</summary>
        private const float HoldInTime = 1.5f;
        /// <summary>出处 = <c>Piranha.cs:12</c> 的 <c>maxDistanceToMove = 2</c>。</summary>
        private const float HideDistance = 2f;

        /// <summary>预制体 <c>Up Stop</c> 的 local y（植物**中心**的止点）。</summary>
        private const float UpStopOffset = 2f;
        /// <summary>预制体 <c>Down Stop</c> 的 local y。</summary>
        private const float DownStopOffset = -1f;

        /// <summary>
        /// 伸缩行程（格）= Up Stop − Down Stop = **3**。
        /// <para>出处 = clone <c>Prefabs/Enemies/Teal Piranha.prefab</c> 的两个止点（+2 / −1）。
        /// 本工程只搬"行程"这一个量，锚点改锚到**管口顶面**（见 <see cref="RecomputeStops"/>）。</para>
        /// </summary>
        private const float TravelTiles = UpStopOffset - DownStopOffset;

        private ILevel _level;
        private IAudio _audio;
        private SpriteRenderer _sr;
        private Sprite _frame0;
        private Sprite _frame1;

        /// <summary>管口顶面（世界 y）——**量**出来的，见 <see cref="MeasurePipeMouth"/>。</summary>
        private float _mouthY;

        private float _hiddenY;      // 完全缩回（藏在管口下）
        private float _outY;         // 完全伸出
        private float _t;
        /// <summary>
        /// 0 上升 / 1 在外停 / 2 下降 / 3 在内停。
        /// <para>**初值必须是 3（在管内）**：原版的食人花开局都缩在管里、等一会儿才伸出来
        /// 实测后果：1-2 地表段从出管口升起时，管子里的花正好在外，人一露头就被咬死。</para>
        /// </summary>
        private int _phase = 3;
        private float _animTimer;
        private bool _frameFlip;
        private float _vy;
        private bool _flipped;
        /// <summary>"因马里奥靠近而停在管里"只报一次（别每帧刷屏）。</summary>
        private bool _nearHoldLogged;

        public void Init(ILevel level, IAudio audio, Vector2 pos)
        {
            _level = level;
            _audio = audio;
            _mouthY = MeasurePipeMouth(pos);

            RecomputeStops();
            transform.position = new Vector3(pos.x, _hiddenY, 0f);

            _sr = gameObject.AddComponent<SpriteRenderer>();
            // −1：画在地形瓦片（0）之后、背景（−20）之前 —— 缩回管里时被管口挡住（见类注释）。
            _sr.sortingOrder = -1;
            RecomputeBounds();
            Game.Logger.Info("Enemy",
                $"食人花就位：x={pos.x:F2}（数据格中心 y={pos.y:F2}；管口顶面 y={_mouthY:F2}；" +
                $"伸出到底边 y={_outY:F2}（= 管口 ⇒ 整棵花都在管口之上）、缩回到 y={_hiddenY:F2}；" +
                $"行程 {TravelTiles:F0} 格 = 预制体 Up Stop +2 − Down Stop −1；靠近阈值 {HideDistance} 格）");

            Game.Res.LoadAsset<Sprite>(ResPaths.Enemy(SpriteNames.Piranha0), s =>
            {
                _frame0 = s;
                if (s == null)
                {
                    Game.Logger.Error("Enemy", $"食人花贴图加载失败：{ResPaths.Enemy(SpriteNames.Piranha0)}");
                }
                else
                {
                    RecomputeBounds();
                    if (_sr != null && _sr.sprite == null) _sr.sprite = s;
                }
            });
            Game.Res.LoadAsset<Sprite>(ResPaths.Enemy(SpriteNames.Piranha1), s => _frame1 = s);
        }

        /// <summary>
        /// 量出**管口顶面**：从植物所在格那一列往上、实心格走完为止，取最上面那格的上沿。
        /// <para>
        /// 为什么必须量、不能记一个偏移：四朵花的数据格与管口眼下恰好都是"差 2 格"
        /// （`E 0.5 0` 配 `T y=0/1`、`E 106.5 2` 配 `T y=0/3`），但这一关的管长有 2/3/4 格三种，
        /// 哪天真加一朵花就是错的。这里用的是**关卡自己的实心位图**（`ILevel.IsSolidTile`）。
        /// </para>
        /// </summary>
        private float MeasurePipeMouth(Vector2 pos)
        {
            if (_level == null) return pos.y + 1.5f;      // 数据关系：管口 = 格中心 + 1.5 格
            var col = Mathf.FloorToInt(pos.x);
            var row = Mathf.FloorToInt(pos.y);
            if (!_level.IsSolidTile(col, row))
            {
                Game.Logger.Warn("Enemy",
                    $"食人花 ({pos.x:F2},{pos.y:F2})：所在格 ({col},{row}) 不是实心格 ⇒ 量不到管口，" +
                    $"按数据关系取 y={pos.y + 1.5f:F2}");
                return pos.y + 1.5f;
            }
            while (_level.IsSolidTile(col, row + 1)) row++;
            return row + 1f;                              // 最上面那一格的上沿
        }

        /// <summary>
        /// 伸出 / 缩回位置的**底边** y。
        /// <para>
        /// 那套算式把伸出位置压到管口**以下 1.31 格**（贴图 18×26 px = 1.125×1.625 格，比 1.5 格画布多 1 px 透明边），
        /// 结果只有头顶约 0.3 格露在管口外 —— 用户实测就是「花不会完全出来，只看到一截花根」
        /// （露出来的那一小截其实是两片花瓣的顶尖，看着像根茎）。
        /// </para>
        /// <para>
        /// 伸出时底边 = 管口（整棵花都在管口之上），
        /// 缩回 = 伸出位置往下 <see cref="TravelTiles"/> 格（行程仍取预制体的 Up−Down 止点）。
        /// </para>
        /// <para>
        /// <b>凭什么"伸出 = 底边落在管口"</b>：拿**原版自己的整关海报**
        /// <c>策划/基线图/nes-original-1-2-full-map.png</c> 逐根量过（三根管的食人花各量一次，
        /// 量法 = 找管口的绿色顶行 `(0,168,0)`，再找花本体 `(0,128,136)` 与橙色花根 `(200,76,12)`）：
        /// 三处结果**完全一致** —— 花根底边在管口顶面之上 **3 px = 0.19 格**、整棵花高 **22 px = 1.38 格**。
        /// 也就是说原版伸出态是"整棵花都在管口之上"（贴图自带 1 px 透明边，把底边放在管口上就落在这个公差里）。
        /// </para>
        /// </summary>
        private void RecomputeStops()
        {
            _outY = _mouthY;
            _hiddenY = _outY - TravelTiles;
        }

        private void RecomputeBounds()
        {
            var p = transform.position;
            // 只有"伸出管口之后"才有实体判定：缩在里面时不该撞到任何人。
            if (p.y <= _hiddenY + 0.05f)
            {
                Bounds = new Rect(p.x, p.y, 0f, 0f);
                return;
            }
            Bounds = new Rect(p.x - Size.x * 0.5f, p.y, Size.x, Size.y);
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt > 0.05f) dt = 0.05f;
            if (Finished) return;

            if (_flipped)
            {
                _vy -= GameConst.Gravity * dt;
                var p = transform.position + new Vector3(0f, _vy * dt, 0f);
                if (p.y < -10f) { Finished = true; return; }
                transform.position = p;
                RecomputeBounds();
                return;
            }

            _t += dt;

            // 马里奥靠得太近 ⇒ 缩回去 / 不再伸出来（原版行为；也是"不能站在管口等它"的原因）。
            // 判定只看**水平**距离 —— 出处 `Assets/Scripts/Piranha.cs:37`：`Mathf.Abs(mario.x - x) > 2`。
            var px = StageContext.Player != null ? StageContext.Player.Bounds.center.x : float.MaxValue;
            var gap = Mathf.Abs(px - transform.position.x);
            var near = gap < HideDistance;
            var phaseBefore = _phase;

            float y;
            switch (_phase)
            {
                case 0:   // 上升
                    y = Mathf.Lerp(_hiddenY, _outY, Mathf.Clamp01(_t / RiseTime));
                    if (_t >= RiseTime) { _phase = 1; _t = 0f; y = _outY; }
                    break;
                case 1:   // 停在外面
                    y = _outY;
                    if (_t >= HoldOutTime) { _phase = 2; _t = 0f; }
                    break;
                case 2:   // 下降
                    y = Mathf.Lerp(_outY, _hiddenY, Mathf.Clamp01(_t / RiseTime));
                    if (_t >= RiseTime) { _phase = 3; _t = 0f; y = _hiddenY; }
                    break;
                default:  // 停在里面：**马里奥在 2 格内就不再伸出来**（原版唯一的"躲"）
                    y = _hiddenY;
                    if (!near && _t >= HoldInTime) { _phase = 0; _t = 0f; }
                    break;
            }

            // 这里**不做**"马里奥一靠近就立刻把花按回去"。
            // 原版就是这么写的：靠近只影响"还能不能再伸出来"（`Piranha.cs:37` 设 canMove=true；
            // `:40` 在**下止点**且靠近时设 canMove=false）。已经伸在外面的那一轮照常走完 ——
            // 立刻缩回会让"站在管口等它出来"变成一个假规则，也让远程火球失去窗口。
            if (_phase == 3 && near && !_nearHoldLogged)
            {
                _nearHoldLogged = true;
                Game.Logger.Info("Enemy",
                    $"食人花停在管里不再伸出：马里奥水平间距 {gap:F2} < {HideDistance}（原版 Piranha.cs 的 maxDistanceToMove）");
            }
            else if (!near)
            {
                _nearHoldLogged = false;
            }

            transform.position = new Vector3(transform.position.x, y, 0f);
            RecomputeBounds();

            // 伸缩是"看得见的行为"：每次跨阶段（升/停/降/停）留一条日志。
            // 频率很低（约 1 秒一次），既能当验收证据，也不刷屏。
            if (_phase != phaseBefore)
            {
                var desc = _phase switch
                {
                    0 => "开始从管里伸出",
                    1 => "完全伸出管口",
                    2 => "开始缩回管里",
                    _ => "完全缩回（藏在管口下方）",
                };
                Game.Logger.Info("Enemy", $"食人花{desc}：x={transform.position.x:F2} 底边 y={y:F2}");
            }

            // 两帧交替（张嘴 / 闭嘴）
            _animTimer += dt;
            if (_animTimer >= 0.22f && _frame0 != null && _frame1 != null)
            {
                _animTimer = 0f;
                _frameFlip = !_frameFlip;
                _sr.sprite = _frameFlip ? _frame1 : _frame0;
            }
        }

        /// <summary>
        /// <b>食人花踩不死，踩上去只会让马里奥受伤（原版规则）</b>。
        /// <para>
        /// 所以它是全项目唯一 <see cref="Stompable"/> = <c>false</c> 的敌人 —— 玩法层的"踩中"分支
        /// 会跳过它、走"受伤"那条路，根本不会调到这里。这里保留"缩回管里"只是防守：
        /// 万一将来有人漏判 <see cref="Stompable"/>，也不该出现"踩上去啥事没有"。
        /// </para>
        /// </summary>
        public void Stomp()
        {
            if (Dead) return;
            Game.Logger.Warn("Enemy", "食人花被当作可踩敌人调用了 Stomp —— 玩法层应先生判 Stompable（原版踩它要受伤）");
            _phase = 2;
            _t = 0f;
            RecomputeBounds();
        }

        /// <summary>踩上去<b>不安全</b>：原版里跳上去照样受伤（它只能被火球 / 无敌星消灭）。</summary>
        public bool Stompable => false;

        /// <summary>从侧面撞到它就是受伤。</summary>
        public bool TouchFromSide() => false;

        public void Flip()
        {
            if (Dead) return;
            Dead = true;
            _flipped = true;
            _vy = 6f;
            if (_sr != null) _sr.flipY = true;
            _audio?.PlaySfx(Sfx.Break);
        }

        public void Clear()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
