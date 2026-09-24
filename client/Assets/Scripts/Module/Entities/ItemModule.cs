using System;
using System.Collections.Generic;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Def;
using SuperMario.Module.Audio;
using SuperMario.Module.Level;
using UnityEngine;

namespace SuperMario.Module.Entities
{
    /// <summary>一个道具的对外接口。</summary>
    public interface IItem
    {
        Rect Bounds { get; }
        /// <summary>是否已被取走（取走后不再参与判定，等它演完消失）。</summary>
        bool Taken { get; }
        BlockContent Content { get; }
        /// <summary>被吃到时调（由玩法层调）。</summary>
        void Collect();
        void Clear();
    }

    /// <summary>道具门面。</summary>
    public interface IItems
    {
        IReadOnlyList<IItem> Active { get; }
        /// <summary>从方块里弹出一个道具。<paramref name="feetPos"/> 是方块上方一格的地面位置。</summary>
        void Spawn(BlockContent content, Vector2 feetPos);

        /// <summary>
        /// 在指定位置放一枚**静置的金币**（1-2 里空中那些浮着的金币）。
        /// 与"顶方块弹出来的金币"不同：它不动、也不消失，等着被吃掉。
        /// </summary>
        void SpawnCoin(Vector2 center);

        /// <summary>
        /// 在指定位置飘一行分数文字（原版"金币飞一下变成分数"那一幕，见 <see cref="ScorePopup"/>）。
        /// <para>
        /// <b>谁该飘、飘在哪 —— 全部照 clone 的 `AddScore(bonus, spawnPos)` 系列来</b>
        /// （触发点与位置逐条见 <see cref="ScorePopup"/> 的类注释；这里只列调用方）：
        /// ① 方块出币（问号块 / 多金币砖）= `BlockModule`，位置 = 砖上方；
        /// ② 踩敌 / 星撞 / 壳撞 / 火球 / 顶砖杀敌 = `GameplayModule`，位置 = **那只敌人**；
        /// ③ 吃药 / 吃花 / 吃星 = `GameplayModule`，位置 = **马里奥**（clone 就是 `mario.transform.position`）；
        /// ④ 旗杆那 5000 = `GameplayModule`，位置 = 马里奥（本工程自己的一笔分，原版 clone 旗杆不加分）。
        /// </para>
        /// <para>
        /// **不飘**的两种（照原版）：静置金币被吃到（clone `Coin.cs` 走无位置的 `AddCoin()`）、
        /// 顶碎砖块的 50 分（clone `RegularBrickBlock.cs:48` 也是无位置的 `AddScore(...)`）。
        /// </para>
        /// </summary>
        void SpawnScoreText(int points, Vector2 worldPos);

        /// <summary>
        /// 在指定位置飘一行**任意文字**（原版 `LevelManager.cs:461 CreateFloatingText(string, pos)`
        /// 的直译）—— 目前只有 1-UP 用得上（`"1UP"`，见 <see cref="SpawnScoreText"/> 的说明）。
        /// </summary>
        void SpawnText(string text, Vector2 worldPos);

        void Clear();
        /// <summary>清掉已演完的。</summary>
        void Reap();
        /// <summary>预加载贴图，加载完回调。</summary>
        void Preload(Action onDone);
    }

    internal sealed class ItemModule : IItems
    {
        private readonly List<IItem> _active = new List<IItem>();
        public IReadOnlyList<IItem> Active => _active;

        /// <summary>放一枚静置金币（1-2 空中那些浮着的金币，循环播动画等人来吃）。</summary>
        public void SpawnCoin(Vector2 center)
        {
            var go = new GameObject("Coin");
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            var c = go.AddComponent<Item.StaticCoin>();
            c.Init(center, _coinFrames, sr);
            _active.Add(c);
        }

        private readonly ILevel _level;
        private readonly IAudio _audio;
        private readonly Transform _root;

        /// <summary>场上正在飘的分数文字（原版"金币 → 分数"，见 <see cref="ScorePopup"/>）。</summary>
        private readonly List<ScorePopup> _popups = new List<ScorePopup>();

        /// <summary>飘一行分数文字（点数由调用方给，这里只负责表现）。</summary>
        public void SpawnScoreText(int points, Vector2 worldPos)
        {
            // 原版只在 bonus > 0 时飘字（clone `LevelManager.cs:551-556` 的 `if (bonus > 0)`）。
            if (points <= 0) return;
            SpawnText(points.ToString(), worldPos);
        }

        /// <summary>飘一行任意文字（见 <see cref="IItems.SpawnText"/>）。</summary>
        public void SpawnText(string text, Vector2 worldPos)
        {
            if (string.IsNullOrEmpty(text)) return;
            _popups.Add(ScorePopup.Spawn(_root, text, worldPos));
            // 留痕：这行字是"看得见的表现"，验收时要能从日志里区分"该飘但没飘"与"飘了看不到"。
            Game.Logger.Info("Item", $"飘分：{text} @({worldPos.x:F2},{worldPos.y:F2})");
        }

        private Sprite _mushroom;
        private Sprite _oneUp;
        private readonly List<Sprite> _flowerFrames = new List<Sprite>();
        private readonly List<Sprite> _coinFrames = new List<Sprite>();
        private readonly List<Sprite> _starFrames = new List<Sprite>();
        private int _pending;

        public ItemModule(ILevel level, IAudio audio, Transform root)
        {
            _level = level;
            _audio = audio;
            _root = root;
        }

        public void Preload(Action onDone)
        {
            // 待加载清单先收集成一张表，_pending 直接取【清单长度】—— 不手写数字。
            //
            //     _pending = 2 + SpriteNames.FireFlower.Length + SpriteNames.Coin.Length;
            // 但下面实际只发了 1 张蘑菇 + 6 张花 + 4 张金币 = 11 次加载，那个 "2" 应该是 1。
            // 于是 --_pending 永远停在 1，onDone 永不触发，流程永久停在 Loading 读条屏，
            // 而且一条报错都没有 —— 看起来像"卡住"，其实是计数和代码不同步。
            // 让计数跟着清单长度走，这类错就写不出来了。
            var plan = new List<string>(2 + SpriteNames.FireFlower.Length
                                          + SpriteNames.Coin.Length + SpriteNames.Star.Length);
            plan.Add(SpriteNames.Mushroom);
            plan.Add(SpriteNames.OneUp);
            plan.AddRange(SpriteNames.FireFlower);
            plan.AddRange(SpriteNames.Coin);
            plan.AddRange(SpriteNames.Star);

            _pending = plan.Count;
            if (_pending <= 0)
            {
                onDone?.Invoke();
                return;
            }

            // 帧动画的顺序不能靠"回调先后"来定 —— 加载是异步的，谁先回来不一定。
            // 所以按清单里的下标写进固定位置，保证花/金币/星星的循环帧顺序始终正确
            // （直接 Add 的话顺序会随加载完成次序抖动，播放起来就是跳帧）。
            foreach (var name in plan)
            {
                var spriteName = name;
                var path = ResPaths.Item(spriteName);
                var isMushroom = spriteName == SpriteNames.Mushroom;
                var isOneUp = spriteName == SpriteNames.OneUp;
                var flowerIndex = System.Array.IndexOf(SpriteNames.FireFlower, spriteName);
                var coinIndex = System.Array.IndexOf(SpriteNames.Coin, spriteName);
                var starIndex = System.Array.IndexOf(SpriteNames.Star, spriteName);

                Game.Res.LoadAsset<Sprite>(path, s =>
                {
                    // 自检放在【回调里】：LoadAsset 是异步的，在 Preload 里同步判空必然为 null，
                    // 那样这条警告会【无条件】打出来（纯误报，会让人去查一个根本不存在的资源问题）。
                    if (s == null)
                    {
                        Game.Logger.Warn("Item", $"道具贴图未加载：{path}");
                        Done(onDone);
                        return;
                    }

                    if (isMushroom) _mushroom = s;
                    else if (isOneUp) _oneUp = s;
                    else if (flowerIndex >= 0) Put(_flowerFrames, flowerIndex, s);
                    else if (coinIndex >= 0) Put(_coinFrames, coinIndex, s);
                    else if (starIndex >= 0) Put(_starFrames, starIndex, s);

                    Done(onDone);
                });
            }
        }

        /// <summary>把精灵写到固定下标（回调可能乱序返回，所以先补空位再写）。</summary>
        private static void Put(List<Sprite> list, int index, Sprite s)
        {
            while (list.Count <= index) list.Add(null);
            list[index] = s;
        }

        private void Done(Action onDone)
        {
            if (--_pending <= 0) onDone?.Invoke();
        }

        public void Spawn(BlockContent content, Vector2 feetPos)
        {
            var go = new GameObject($"Item_{content}");
            go.transform.SetParent(_root, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 6;

            Item item;
            switch (content)
            {
                case BlockContent.Mushroom:
                    item = go.AddComponent<MovingItem>();
                    ((MovingItem)item).Init(_level, _audio, content, feetPos, _mushroom, sr);
                    break;

                // 1-UP 用原版自己的贴图（绿帽白点），不再拿超级蘑菇染色顶替。
                case BlockContent.OneUp:
                    item = go.AddComponent<MovingItem>();
                    ((MovingItem)item).Init(_level, _audio, content, feetPos, _oneUp, sr);
                    break;

                case BlockContent.FireFlower:
                    item = go.AddComponent<FlowerItem>();
                    ((FlowerItem)item).Init(_audio, feetPos, _flowerFrames, sr);
                    break;

                case BlockContent.Star:
                    item = go.AddComponent<StarItem>();
                    ((StarItem)item).Init(_level, _audio, feetPos, _starFrames, sr);
                    break;

                default:
                    item = go.AddComponent<CoinPop>();
                    ((CoinPop)item).Init(_audio, feetPos, _coinFrames, sr);
                    break;
            }

            _active.Add(item);
        }

        public void Reap()
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var it = _active[i];
                var finished = it is Item concrete && concrete.Finished;
                if (!finished) continue;
                it.Clear();
                _active.RemoveAt(i);
            }

            for (var i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                // Unity 伪 null：对象被别处销毁（换关）时也要把它从表里摘掉，否则越积越多。
                if (p == null) { _popups.RemoveAt(i); continue; }
                if (!p.Finished) continue;
                p.Clear();
                _popups.RemoveAt(i);
            }
        }

        public void Clear()
        {
            foreach (var i in _active) i.Clear();
            _active.Clear();
            foreach (var p in _popups) if (p != null) p.Clear();
            _popups.Clear();
        }
    }

    /// <summary>道具基类：统一"从方块里冒出来"的出场动画。</summary>
    internal abstract class Item : MonoBehaviour, IItem
    {
        public Rect Bounds { get; protected set; }
        public bool Taken { get; protected set; }
        public BlockContent Content { get; protected set; }
        public bool Finished { get; protected set; }

        /// <summary>出场动画：从方块内部升到方块上方，用时会阻塞其它行为。</summary>
        protected float EmergeTimer = -1f;
        protected const float EmergeTime = 0.45f;
        protected Vector3 TargetPos;

        protected SpriteRenderer Sr;

        protected void RecomputeBounds(Vector2 size)
        {
            var p = transform.position;
            Bounds = new Rect(p.x - size.x * 0.5f, p.y, size.x, size.y);
        }

        /// <summary>
        /// 以 **transform 位置为矩形中心** 算碰撞盒（与上面"位置 = 底边"相反）。
        /// <para>
        /// 谁需要它：**静置金币**。它的位置来自关卡数据的「格子中心」（<c>StageSession</c> 里是
        /// <c>new Vector2(tx + 0.5f, ty + 0.5f)</c>），用"位置 = 底边"算就会整体高半格。
        /// </para>
        /// <para>
        /// `Block.CollectCoinOnTop` 用的是 `StandingOnTop`——要求金币**底边贴着砖顶**（±0.25 格），
        /// 而实际差了整整 <b>0.5 格</b> ⇒ 这条原版规则从未触发过（代码在、出处也有，就是判不到）。
        /// 顺带把"看起来在那、要跳高半格才吃得到"的对不齐也一起消掉。
        /// </para>
        /// <para>只给"按格子中心摆放"的道具用；按底边摆放的（蘑菇 / 星星 / 弹出金币）保持原样。</para>
        /// </summary>
        protected void RecomputeBoundsCentered(Vector2 size)
        {
            var p = transform.position;
            Bounds = new Rect(p.x - size.x * 0.5f, p.y - size.y * 0.5f, size.x, size.y);
        }

        /// <summary>
        /// 静置金币：原地不动、循环播放金币动画，等马里奥碰到它。
        /// 内容是 <see cref="BlockContent.Coin"/>，所以吃到后的加分/计数走玩法层现有那条路，
        /// 不需要为它单开一套逻辑。
        /// </summary>
        internal sealed class StaticCoin : Item
        {
            private static readonly Vector2 Size = new Vector2(0.7f, 0.7f);

            /// <summary>金币 0.11 s/帧。**逐字沿用**原手写节奏（`_animTimer >= 0.11f` 那个常量）。</summary>
            private const float FrameSeconds = 0.11f;

            /// <summary>
            /// 逐帧动画器（引擎 <see cref="SpriteFrameAnimator"/>）：帧表 + fps 给一次，切帧只有引擎那一份实现。
            /// 收敛掉本处自写的 `_frames` / `_animTimer` / `_frame` / `Sr.sprite = …` 四件套。
            /// </summary>
            private SpriteFrameAnimator _anim;

            public void Init(Vector2 center, List<Sprite> frames, SpriteRenderer sr)
            {
                Content = BlockContent.Coin;
                Sr = sr;
                Sr.sortingOrder = 3;                 // 在砖块之下、背景之上
                _anim = new SpriteFrameAnimator(sr);
                // 空帧表由引擎自己降频 Warn（"Sprite 还没加载完 / 图集路径不对"），本处不再自己判空。
                // 单帧也照给：引擎在单帧上循环 = 恒显第 0 帧，与原 `Count <= 1` 的早退等价。
                _anim.Play(frames.ToArray(), 1f / FrameSeconds);

                transform.position = new Vector3(center.x, center.y, 0f);
                TargetPos = transform.position;
                EmergeTimer = -1f;                    // 没有出场动画
                // 用"以位置为中心"的碰撞盒（不是"位置=底边"）：金币是按**格子中心**摆的，
                //   用底边算会整体高半格 ⇒ `Block.CollectCoinOnTop` 的"底边贴砖顶"判据永远差 0.5 格
                //   （见 RecomputeBoundsCentered 的说明）。视觉位置一个字都没动。
                RecomputeBoundsCentered(Size);
            }

            private void Update()
            {
                if (Taken || Finished) return;
                _anim?.Advance(Time.deltaTime);
            }

            public override void Collect()
            {
                if (Taken) return;
                Taken = true;
                Finished = true;
            }
        }

        // ───────── 逐格扫描（收敛到引擎 GridUtil）─────────
        //
        // PlayerActor / FireballModule / EnemyModule 三处**逐字相同** ⇒ 已下沉为
        // `CloverEngine.GridUtil`（出处与逐字复刻的口径见 `Runtime/Core/GridUtil.cs` 文件头：
        // `xMin = FloorToInt(r.xMin)`、`xMax = FloorToInt(r.xMax - 0.0001f)`、y 外层 / x 内层**升序**，
        // 那个 `- 0.0001f` 收边量即 `GridUtil.EdgeEpsilon`）。
        //
        // 用 `ForEach` 而**不是** `GridUtil.Enumerate`：后者是迭代器、每次调用都分配，
        //    而道具的 X/Y 解算是**每帧每只道具**都跑的热路径。`ForEach` 只在"委托已缓存"时不分配
        //    —— 引擎文件头 GC 写明「方法组写法在 Unity 的 C# 9 下每次转换也分配一个委托」
        //    ⇒ 委托存进 <see cref="_onScanTile"/>，状态存字段（回调不捕获局部变量）。
        //
        // 算法本身一字未动：仍是"X 先走完解 X、再走 Y 解 Y"、仍是"命中第一格就停"。

        /// <summary>缓存的逐格回调（热路径不分配，见上）。</summary>
        private Action<int, int> _onScanTile;

        private ILevel _scanLevel;
        private bool _scanHit;

        /// <summary>本次扫描命中的格（<see cref="ScanFirstSolid"/> 返回 <c>true</c> 时有效）。</summary>
        protected int HitTileX { get; private set; }
        protected int HitTileY { get; private set; }

        /// <summary>
        /// </summary>
        /// <returns><c>true</c> = 命中了一个实心格（格坐标见 <see cref="HitTileX"/> / <see cref="HitTileY"/>）。</returns>
        protected bool ScanFirstSolid(ILevel level, Rect r)
        {
            _scanLevel = level;
            _scanHit = false;
            HitTileX = 0;
            HitTileY = 0;
            _onScanTile ??= OnScanTile;               // 委托缓存到字段（引擎 GridUtil 文件头 ★ GC 的要求）
            GridUtil.ForEach(r, _onScanTile);
            return _scanHit;
        }

        private void OnScanTile(int tx, int ty)
        {
            if (_scanHit) return;                      // = 原 `break`
            if (!_scanLevel.IsSolidTile(tx, ty)) return;
            _scanHit = true;
            HitTileX = tx;
            HitTileY = ty;
        }

        public abstract void Collect();

        public virtual void Clear()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }

    /// <summary>会走路的道具（超级蘑菇 / 1-UP 蘑菇）。</summary>
    internal sealed class MovingItem : Item
    {
        private static readonly Vector2 Size = new Vector2(0.9f, 0.9f);

        private ILevel _level;
        private IAudio _audio;
        private Vector2 _vel;
        private float _dir = 1f;

        public void Init(ILevel level, IAudio audio, BlockContent content, Vector2 feetPos, Sprite sprite, SpriteRenderer sr)
        {
            _level = level;
            _audio = audio;
            Content = content;
            Sr = sr;
            Sr.sprite = sprite;
            // 这里不再有"1-UP 染色"那一行：原版有独立贴图（smb_items_sheet_14）。

            // 从方块里"长"出来：起点在方块内，终点在方块上方一格。
            transform.position = new Vector3(feetPos.x, feetPos.y - GameConst.TileSize, 0f);
            TargetPos = new Vector3(feetPos.x, feetPos.y, 0f);
            EmergeTimer = 0f;
            RecomputeBounds(Size);
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt > 0.05f) dt = 0.05f;

            if (Finished) return;

            if (EmergeTimer >= 0f)
            {
                EmergeTimer += dt;
                var t = Mathf.Clamp01(EmergeTimer / EmergeTime);
                transform.position = Vector3.Lerp(TargetPos - new Vector3(0f, GameConst.TileSize, 0f), TargetPos, t);
                RecomputeBounds(Size);
                if (t >= 1f) EmergeTimer = -1f;
                return;
            }

            // 出场后：向右走 + 受重力，撞墙掉头（原版蘑菇不会掉头，会一直顶着墙；
            // 但顶着墙会卡在墙里不走，所以这里选择掉头，行为更稳）。
            _vel.y -= GameConst.Gravity * dt;
            if (_vel.y < -GameConst.MaxFallSpeed) _vel.y = -GameConst.MaxFallSpeed;

            var dx = _dir * 2.5f * dt;
            var nx = transform.position.x + dx;
            if (ScanFirstSolid(_level, new Rect(nx - Size.x * 0.5f, transform.position.y, Size.x, Size.y)))
            {
                _dir = -_dir;
                nx = transform.position.x;
            }

            var ny = transform.position.y + _vel.y * dt;
            if (ScanFirstSolid(_level, new Rect(nx - Size.x * 0.5f, ny, Size.x, Size.y)))
            {
                if (_vel.y <= 0f) { ny = HitTileY + 1f; _vel.y = 0f; }
                else { ny = HitTileY - Size.y; _vel.y = 0f; }
            }

            transform.position = new Vector3(nx, ny, 0f);
            RecomputeBounds(Size);

            if (transform.position.y < -12f) Finished = true;
        }

        public override void Collect()
        {
            if (Taken) return;
            Taken = true;
            _audio?.PlaySfx(Content == BlockContent.OneUp ? Sfx.OneUp : Sfx.PowerUp);
            Finished = true;
        }

    }

    /// <summary>
    /// 无敌星：出场后<b>一跳一跳</b>往前走（原版的星星是弹跳移动的，不是像蘑菇那样平推），
    /// 同时做颜色循环动画。
    /// </summary>
    internal sealed class StarItem : Item
    {
        private static readonly Vector2 Size = new Vector2(0.9f, 0.9f);

        /// <summary>星星颜色循环 0.09 s/帧。**逐字沿用**原手写节奏（`_animTimer >= 0.09f`）。</summary>
        private const float FrameSeconds = 0.09f;

        private ILevel _level;
        private IAudio _audio;
        private Vector2 _vel;
        private float _dir = 1f;

        /// <summary>逐帧动画器（引擎 <see cref="SpriteFrameAnimator"/>）—— 收敛掉自写的 `_frames`/`_animTimer`/`_frame`。</summary>
        private SpriteFrameAnimator _anim;

        public void Init(ILevel level, IAudio audio, Vector2 feetPos, List<Sprite> frames, SpriteRenderer sr)
        {
            _level = level;
            _audio = audio;
            Content = BlockContent.Star;
            Sr = sr;
            _anim = new SpriteFrameAnimator(sr);
            _anim.Play(frames.ToArray(), 1f / FrameSeconds);

            transform.position = new Vector3(feetPos.x, feetPos.y - GameConst.TileSize, 0f);
            TargetPos = new Vector3(feetPos.x, feetPos.y, 0f);
            EmergeTimer = 0f;
            RecomputeBounds(Size);
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt > 0.05f) dt = 0.05f;
            if (Finished) return;

            if (EmergeTimer >= 0f)
            {
                EmergeTimer += dt;
                var t = Mathf.Clamp01(EmergeTimer / EmergeTime);
                transform.position = Vector3.Lerp(TargetPos - new Vector3(0f, GameConst.TileSize, 0f), TargetPos, t);
                RecomputeBounds(Size);
                if (t >= 1f) EmergeTimer = -1f;
                return;
            }

            // 颜色循环：原版的星星会闪不同颜色（贴图本身就是四帧不同配色）。
            _anim?.Advance(dt);

            _vel.y -= GameConst.Gravity * dt;
            if (_vel.y < -GameConst.MaxFallSpeed) _vel.y = -GameConst.MaxFallSpeed;

            var dx = _dir * 3f * dt;
            var nx = transform.position.x + dx;
            if (ScanFirstSolid(_level, new Rect(nx - Size.x * 0.5f, transform.position.y, Size.x, Size.y)))
            {
                _dir = -_dir;
                nx = transform.position.x;
            }

            var ny = transform.position.y + _vel.y * dt;
            if (ScanFirstSolid(_level, new Rect(nx - Size.x * 0.5f, ny, Size.x, Size.y)))
            {
                if (_vel.y <= 0f)
                {
                    ny = HitTileY + 1f;
                    _vel.y = GameConst.StarBounce;   // 落地即弹起
                }
                else { ny = HitTileY - Size.y; _vel.y = 0f; }
            }

            transform.position = new Vector3(nx, ny, 0f);
            RecomputeBounds(Size);

            if (transform.position.y < -12f) Finished = true;
        }

        public override void Collect()
        {
            if (Taken) return;
            Taken = true;
            _audio?.PlaySfx(Sfx.PowerUp);
            Finished = true;
        }
    }

    /// <summary>火焰花：原地不动，只做循环动画。</summary>
    internal sealed class FlowerItem : Item
    {
        private static readonly Vector2 Size = new Vector2(0.9f, 0.9f);

        /// <summary>火焰花循环 0.08 s/帧。**逐字沿用**原手写节奏（`_animTimer >= 0.08f`）。</summary>
        private const float FrameSeconds = 0.08f;

        private IAudio _audio;

        /// <summary>逐帧动画器（引擎 <see cref="SpriteFrameAnimator"/>）—— 收敛掉自写的 `_frames`/`_animTimer`/`_frame`。</summary>
        private SpriteFrameAnimator _anim;

        public void Init(IAudio audio, Vector2 feetPos, List<Sprite> frames, SpriteRenderer sr)
        {
            _audio = audio;
            Content = BlockContent.FireFlower;
            Sr = sr;
            _anim = new SpriteFrameAnimator(sr);
            _anim.Play(frames.ToArray(), 1f / FrameSeconds);

            transform.position = new Vector3(feetPos.x, feetPos.y - GameConst.TileSize, 0f);
            TargetPos = new Vector3(feetPos.x, feetPos.y, 0f);
            EmergeTimer = 0f;
            RecomputeBounds(Size);
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (Finished) return;

            if (EmergeTimer >= 0f)
            {
                EmergeTimer += dt;
                var t = Mathf.Clamp01(EmergeTimer / EmergeTime);
                transform.position = Vector3.Lerp(TargetPos - new Vector3(0f, GameConst.TileSize, 0f), TargetPos, t);
                RecomputeBounds(Size);
                if (t >= 1f) EmergeTimer = -1f;
                return;
            }

            _anim?.Advance(dt);
        }

        public override void Collect()
        {
            if (Taken) return;
            Taken = true;
            _audio?.PlaySfx(Sfx.PowerUp);
            Finished = true;
        }
    }

    /// <summary>从方块里蹦出来的金币：向上弹一段，落下后消失。</summary>
    internal sealed class CoinPop : Item
    {
        private static readonly Vector2 Size = new Vector2(0.6f, 0.9f);

        /// <summary>弹出金币旋转 0.06 s/帧。**逐字沿用**原手写节奏（`_animTimer >= 0.06f`）。</summary>
        private const float FrameSeconds = 0.06f;

        private IAudio _audio;
        private Vector2 _vel;
        private float _life = 1.2f;

        /// <summary>逐帧动画器（引擎 <see cref="SpriteFrameAnimator"/>）—— 收敛掉自写的 `_frames`/`_animTimer`/`_frame`。</summary>
        private SpriteFrameAnimator _anim;

        public void Init(IAudio audio, Vector2 feetPos, List<Sprite> frames, SpriteRenderer sr)
        {
            _audio = audio;
            Content = BlockContent.Coin;
            Sr = sr;
            _anim = new SpriteFrameAnimator(sr);
            _anim.Play(frames.ToArray(), 1f / FrameSeconds);

            transform.position = new Vector3(feetPos.x, feetPos.y, 0f);
            _vel = new Vector2(0f, 11f);
            TargetPos = transform.position;
            EmergeTimer = -1f;   // 金币不需要"长出"动画，直接蹦
            RecomputeBounds(Size);
            // 金币是立刻结算的（分数/金币数由方块那边加），这里只负责演。
            Taken = true;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            _life -= dt;
            if (_life <= 0f) { Finished = true; return; }

            _vel.y -= GameConst.Gravity * dt;
            transform.position += new Vector3(0f, _vel.y * dt, 0f);
            RecomputeBounds(Size);

            _anim?.Advance(dt);
        }

        public override void Collect() { }
    }
}
