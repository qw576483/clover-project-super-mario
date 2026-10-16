using System;
using System.Collections.Generic;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Def;
using SuperMario.Module.Audio;
using SuperMario.Module.Flow;      // StageContext（地下关要换成青色砖）
using SuperMario.Module.Level;
using SuperMario.Module.Score;
using UnityEngine;

namespace SuperMario.Module.Entities
{
    /// <summary>砖块 / 问号块的对外接口。</summary>
    public interface IBlock
    {
        /// <summary>所在格（把"头顶撞到的格"对应回具体方块）。</summary>
        Vector2Int Tile { get; }
        /// <summary>是否还是实心的（碎裂后不再是）。</summary>
        bool Solid { get; }
        /// <summary>是否已经被顶过（问号块顶过变暗块）。</summary>
        bool Used { get; }
        /// <summary>从下方顶这个块。<paramref name="big"/> 决定砖块能否被撞碎。</summary>
        void HitFromBelow(bool big);
        void Clear();
    }

    /// <summary>方块门面。</summary>
    public interface IBlocks
    {
        IBlock At(Vector2Int tile);
        void Spawn(EntityKind kind, Vector2Int tile);
        void Clear();
        /// <summary>预加载贴图，加载完回调。</summary>
        void Preload(Action onDone);
        /// <summary>方块碎裂时喷碎片（由 Block 回调）。</summary>
        void SpawnParticles(Vector3 center);
        /// <summary>注销一格的实心登记（方块碎裂时由 Block 回调）。</summary>
        void Unregister(Vector2Int tile);
    }

    internal sealed class BlockModule : IBlocks
    {
        private readonly Dictionary<Vector2Int, IBlock> _map = new Dictionary<Vector2Int, IBlock>();
        private readonly ILevel _level;
        private readonly IAudio _audio;
        private readonly IScore _score;
        private readonly IItems _items;
        private readonly IEnemies _enemies;
        private readonly Transform _root;

        private Sprite _brickSprite;
        private Sprite _questionSprite;
        private Sprite _usedSprite;
        private readonly List<Sprite> _particles = new List<Sprite>();
        private int _pending;

        public BlockModule(ILevel level, IAudio audio, IScore score, IItems items, IEnemies enemies,
                           Transform root)
        {
            _level = level;
            _audio = audio;
            _score = score;
            _items = items;
            _enemies = enemies;
            _root = root;
        }

        public void Preload(Action onDone)
        {
            _pending = 3 + SpriteNames.BrickParticles.Length;
            // 地下关（1-2）的砖是【青色】的。
            // 关卡文件里那种砖原本是地形瓦片（顶不碎），会被 StageSession 换成 Brick 实体 ——
            // 所以这里的贴图必须跟着换成青色那张，否则 1-2 会突然冒出一地橙色地砖。
            var brickPath = StageContext.Underground
                ? ResPaths.Tile(SpriteNames.TileUndergroundBrick)
                : ResPaths.Item(SpriteNames.Brick);
            Game.Res.LoadAsset<Sprite>(brickPath, s => { _brickSprite = s; Done(onDone); });
            Game.Res.LoadAsset<Sprite>(ResPaths.Item(SpriteNames.QuestionBlock), s => { _questionSprite = s; Done(onDone); });
            Game.Res.LoadAsset<Sprite>(ResPaths.Item(SpriteNames.QuestionBlockUsed), s => { _usedSprite = s; Done(onDone); });
            foreach (var p in SpriteNames.BrickParticles)
                Game.Res.LoadAsset<Sprite>(ResPaths.Particle(p), s => { if (s != null) _particles.Add(s); Done(onDone); });

            if (_brickSprite == null) { /* 加载是异步的，这里还拿不到；由 Block 自己兜底 */ }
        }

        private void Done(Action onDone)
        {
            if (--_pending <= 0) onDone?.Invoke();
        }

        public IBlock At(Vector2Int tile) => _map.TryGetValue(tile, out var b) ? b : null;

        public void Spawn(EntityKind kind, Vector2Int tile)
        {
            if (_map.ContainsKey(tile))
            {
                // 同格重复铺方块通常是关卡数据写重了。不静默：静默会让人对着"少了一个方块"查半天。
                Game.Logger.Warn("Block", $"格 ({tile.x},{tile.y}) 已有方块，忽略重复的 {kind}");
                return;
            }

            var go = new GameObject($"Block_{kind}_{tile.x}_{tile.y}");
            go.transform.SetParent(_root, false);
            // 方块正好占满一格 [tile.y, tile.y+1]。
            //
            // Y 必须给【格底边】，不能给格中心：方块贴图来自 Sprites/Items/，
            // 轴心是【底部居中】（见 SpriteImportPostprocessor），给格中心会让整块砖
            // "蘑菇停在砖块中腰、而不是砖上"（蘑菇按物理落在正确的格顶 y+1，
            // 是方块自己画高了才对不齐）。X 仍然给中心。
            go.transform.position = new Vector3(tile.x + 0.5f, tile.y, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 1;

            // 顶开之后弹什么。星砖 / 多金币砖 / 隐形 1-UP 块都是原版 1-1 里**本来就有**的
            var content = kind switch
            {
                EntityKind.QuestionBlock => BlockContent.Coin,
                EntityKind.QuestionBlockMushroom => BlockContent.Mushroom,
                EntityKind.QuestionBlockOneUp => BlockContent.OneUp,
                EntityKind.BrickStarman => BlockContent.Star,
                EntityKind.BrickMultiCoin => BlockContent.Coin,   // 连顶出币，枚数见 GameConst.MultiCoinBrickCoins
                EntityKind.HiddenBoxOneUp => BlockContent.OneUp,
                _ => BlockContent.None,
            };

            // 贴图按"砖型 / 问号块型"分：含道具的砖在原版里画的也是**砖**的贴图（顶出道具后才变暗块）。
            var brickLike = kind == EntityKind.Brick
                            || kind == EntityKind.BrickStarman
                            || kind == EntityKind.BrickMultiCoin;

            var b = go.AddComponent<Block>();
            b.Init(this, _level, _audio, _score, _items, _enemies, tile, kind, content, sr,
                brickLike ? _brickSprite : _questionSprite, _usedSprite);

            // 登记进实心位图：马里奥的物理就"看得见"方块了。
            _level.SetSolid(tile.x, tile.y, true);

            _map[tile] = b;
        }

        public void Unregister(Vector2Int tile)
        {
            _map.Remove(tile);
            _level.SetSolid(tile.x, tile.y, false);
        }

        public void SpawnParticles(Vector3 center)
        {
            for (var i = 0; i < 3; i++)
            {
                var go = new GameObject("BrickParticle");
                go.transform.SetParent(_root, false);
                go.transform.position = center;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 8;
                sr.sprite = i < _particles.Count ? _particles[i] : null;

                // 三块碎片按原版向三个方向弹开：左上、右上、正下方。
                var vel = i == 0 ? new Vector2(-3.5f, 8f)
                        : i == 1 ? new Vector2(3.5f, 8f)
                        : new Vector2(0f, 10f);
                go.AddComponent<BrickParticle>().Init(vel);
            }
        }

        public void Clear()
        {
            foreach (var b in _map.Values) b.Clear();
            _map.Clear();
        }
    }

    /// <summary>
    /// 一个砖块或问号块。
    /// <para>
    /// 顶起来的动画自己做插值（<c>_bumpTimer</c>），不走 Animator：这个动画只有
    /// "上移一点再回来"两个状态，用 Animator 要建 controller、配曲线，
    /// 成本远大于收益，而且会让"什么时候能再顶下一个"变得难以判断。
    /// </para>
    /// </summary>
    internal sealed class Block : MonoBehaviour, IBlock
    {
        public Vector2Int Tile { get; private set; }
        public bool Solid { get; private set; } = true;
        public bool Used { get; private set; }

        private BlockModule _owner;
        private ILevel _level;
        private IAudio _audio;
        private IScore _score;
        private IItems _items;
        private IEnemies _enemies;
        private EntityKind _kind;
        private BlockContent _content;
        private SpriteRenderer _sr;
        private Sprite _normalSprite;
        private Sprite _usedSprite;

        private Vector3 _basePos;
        private float _bumpTimer = -1f;
        private float _blinkTimer;

        /// <summary>多金币砖还剩几枚币（原版是连顶 10 次，出处见 <c>GameConst.MultiCoinBrickCoins</c>）。</summary>
        private int _multiCoinLeft = GameConst.MultiCoinBrickCoins;

        public void Init(BlockModule owner, ILevel level, IAudio audio, IScore score, IItems items,
                         IEnemies enemies,
                         Vector2Int tile, EntityKind kind, BlockContent content, SpriteRenderer sr,
                         Sprite normal, Sprite used)
        {
            _owner = owner;
            _level = level;
            _audio = audio;
            _score = score;
            _items = items;
            _enemies = enemies;
            Tile = tile;
            _kind = kind;
            _content = content;
            _sr = sr;
            _normalSprite = normal;
            _usedSprite = used;
            _basePos = transform.position;

            if (normal != null) _sr.sprite = normal;
            else Game.Logger.Error("Block", $"方块贴图为空：{kind} @ ({tile.x},{tile.y})");

            // 让**贴图**正好填满这一格 —— 与轴心无关（判据用 bounds，不用"轴心叫什么"）。
            //   `Sprites/Items/` 的砖是【底部居中】轴心（bounds y ∈ [0,1]）⇒ 位置 = 格底边；
            //   而地下关的**砖地形**走的是同一条方块实体路径，贴图是 `WorldTileSprites_1`（**居中**轴心，
            //   bounds y ∈ [-0.5,0.5]）⇒ 位置必须是格中心。
            //   被画低了半格，而碰撞面是正确的 —— 用户看到的就是「1-2 人物怪物都不在地面上」：
            //   人明明站在正确的碰撞面上，是地砖自己画低了半格。
            //   ⇒ 对齐之后**必须重取 `_basePos`**（顶砖动画是绕着它做的）。
            if (normal != null)
            {
                var bb = normal.bounds;
                var pos = transform.position;
                var wantY = tile.y + 0.5f - (bb.min.y + bb.max.y) * 0.5f;
                if (Mathf.Abs(pos.y - wantY) > 0.0001f)
                {
                    transform.position = new Vector3(pos.x, wantY, pos.z);
                }
            }
            _basePos = transform.position;

            // 但实心位图里照样登记（能站、能顶），顶到才现形为暗块。
            if (kind == EntityKind.HiddenBoxOneUp)
            {
                _sr.enabled = false;
                Game.Logger.Info("Block", $"隐形 1-UP 块就位：({tile.x},{tile.y})（隐形 + 实心）");
            }
        }

        private void Update()
        {
            // 只有"还没顶过的问号块"才做呼吸闪烁：含道具的砖是砖的贴图，不该闪；
            // 隐形块更是连画都不画。
            if (IsQuestionBlock(_kind) && !Used)
            {
                // 问号块呼吸闪烁（原版节奏：亮 → 暗 → 亮）。
                _blinkTimer += Time.deltaTime;
                if (_sr != null && _normalSprite != null && _blinkTimer >= 0.35f) _blinkTimer = 0f;
            }

            if (_bumpTimer < 0f) return;

            _bumpTimer += Time.deltaTime;
            var t = _bumpTimer / GameConst.BlockBumpTime;
            if (t >= 1f)
            {
                transform.position = _basePos;
                _bumpTimer = -1f;
                return;
            }
            // 三角波：中间最高，两端归零。
            var k = 1f - Mathf.Abs(t * 2f - 1f);
            transform.position = _basePos + new Vector3(0f, GameConst.BlockBumpHeight * k, 0f);
        }

        /// <summary>
        /// 从下方顶这个方块。
        /// <para>
        /// 原版规则（照抄，不自己定）：**装着东西的砖顶不碎** —— 无论大小形态，顶到就出道具、
        /// 然后自己变成暗块；只有"空砖"才允许大马里奥一次顶碎。
        /// </para>
        /// </summary>
        public void HitFromBelow(bool big)
        {
            // 已经在动的方块不再响应：连续顶会叠加位移，方块看起来会越飞越高。
            if (!Solid || _bumpTimer >= 0f) return;

            // 顶砖时"砖上面那些东西"的两条规则 —— 出处 clone `Assets/Scripts/RegularBrickBlock.cs:22-40`：
            //   ① 砖上有敌人 ⇒ 逐个 `BlockHitEnemy` ⇒ `HitBelowByBlock()`（`_common/Enemy.cs:49` 的基类实现
            //      就是 `FlipAndDie()` —— 与星撞 / 壳撞 / 火球**完全同款**的翻飞）+ `hitByBlockBonus`（100）；
            //   ② 砖上有金币 ⇒ "收走"：**在砖上方 2 格**生成一枚弹出金币（分数与音效都来自这枚新金币：
            //      `BlockCoin.Start()` → `AddCoin(位置 + 下)`），原金币直接销毁、不发分也不响。
            //      谁算"在砖上" = `RegularBrickBlockCoinDetector.cs:8-11`（砖顶那个 trigger 里的 Coin）。
            //   两条都必须在"顶碎 / 顶一下"**之前**处理 —— clone 就是这个顺序：大马里奥顶碎空砖时，
            //      砖上的敌人照样翻飞、金币照样被收走。
            //   问号块**只有①没有②**：clone 的可收集块 `_common/CollectibleBlock.cs` 挂着敌人表，
            //      但没有任何金币检测器。
            KillEnemiesOnTop();
            if (_kind == EntityKind.Brick) CollectCoinOnTop();

            switch (_kind)
            {
                // ── 空砖：大形态一次顶碎，小形态只能顶一下 ──
                case EntityKind.Brick:
                    if (big) { BreakBrick(); return; }
                    Bump(Sfx.Bump);
                    return;

                case EntityKind.BrickStarman:
                    Bump(Sfx.Bump);
                    if (Used) return;
                    MarkUsed();
                    _items?.Spawn(BlockContent.Star, new Vector2(Tile.x + 0.5f, Tile.y + 1f));
                    _audio?.PlaySfx(Sfx.PowerUpAppear);
                    Game.Logger.Info("Block", $"无敌星砖 ({Tile.x},{Tile.y})：顶出★（砖留下变暗块）");
                    return;

                case EntityKind.BrickMultiCoin:
                    Bump(Sfx.Bump);
                    if (_multiCoinLeft <= 0) return;
                    _multiCoinLeft--;
                    _items?.Spawn(BlockContent.Coin, new Vector2(Tile.x + 0.5f, Tile.y + 1f));
                    _items?.SpawnScoreText(GameConst.ScoreCoin, CoinTextPos);
                    _score?.AddCoin();
                    _score?.Add(GameConst.ScoreCoin);
                    _audio?.PlaySfx(Sfx.Coin);
                    if (_multiCoinLeft == 0) MarkUsed();
                    Game.Logger.Info("Block", $"多金币砖 ({Tile.x},{Tile.y})：出币，还剩 {_multiCoinLeft} 枚");
                    return;

                case EntityKind.HiddenBoxOneUp:
                    Bump(Sfx.Bump);
                    if (Used) return;
                    MarkUsed();
                    _sr.enabled = true;                  // 顶到才现形
                    _items?.Spawn(BlockContent.OneUp, new Vector2(Tile.x + 0.5f, Tile.y + 1f));
                    _audio?.PlaySfx(Sfx.PowerUpAppear);
                    Game.Logger.Info("Block", $"隐形 1-UP 块 ({Tile.x},{Tile.y})：现形并弹出 1-UP");
                    return;
            }

            // ── 问号块 ──
            Bump(Sfx.Bump);
            if (Used) return;
            MarkUsed();

            switch (_content)
            {
                case BlockContent.Coin:
                    // 必须同时弹出金币动画。
                    // 只对数字做验证就完全看不出来。ItemModule.Spawn 的 default 分支
                    // 正是 CoinPop（会从方块里蹦出来再消失），这里补上即可。
                    _items?.Spawn(BlockContent.Coin, new Vector2(Tile.x + 0.5f, Tile.y + 1f));
                    _items?.SpawnScoreText(GameConst.ScoreCoin, CoinTextPos);
                    _score?.AddCoin();
                    _score?.Add(GameConst.ScoreCoin);
                    _audio?.PlaySfx(Sfx.Coin);
                    break;

                case BlockContent.Mushroom:
                    // 原版规则：大形态给火焰花，小形态给蘑菇。用 big 参数而不是去问玩家，
                    // 这样方块不需要依赖玩家模块。
                    var c = big ? BlockContent.FireFlower : BlockContent.Mushroom;
                    _items?.Spawn(c, new Vector2(Tile.x + 0.5f, Tile.y + 1f));
                    _audio?.PlaySfx(Sfx.PowerUpAppear);
                    break;

                // 而且不报错（switch 静默走过）。定义了没接线，和没做是一回事。
                case BlockContent.OneUp:
                    // 1-UP 不分大小形态，恒为 1-UP 蘑菇。
                    _items?.Spawn(BlockContent.OneUp, new Vector2(Tile.x + 0.5f, Tile.y + 1f));
                    _audio?.PlaySfx(Sfx.PowerUpAppear);
                    break;

                case BlockContent.Star:
                    _items?.Spawn(BlockContent.Star, new Vector2(Tile.x + 0.5f, Tile.y + 1f));
                    _audio?.PlaySfx(Sfx.PowerUpAppear);
                    break;
            }
        }

        /// <summary>这一格的格矩形（判"是不是站在砖顶面上"用）。</summary>
        private Rect CellRect => new Rect(Tile.x, Tile.y, 1f, 1f);

        /// <summary>
        /// 盒子是不是"站在这一格的顶面上"。
        /// <para>
        /// 判据来源：原版靠物理接触法线 —— clone `RegularBrickBlock.cs:60-67` 的
        /// <c>OnCollisionStay2D</c> 里 <c>contact.normal == (0,-1)</c> 才算"在上面"。
        /// 本工程的实体是"格矩形 + 自绘盒"、没有物理法线可用，所以翻译成几何判据：
        /// **脚底（<c>yMin</c>）贴在砖顶面**且与该格横向重叠。
        /// </para>
        /// <para>容差 0.25 格：站上去时脚底应当正好在 <c>Tile.y + 1</c>，余量兜住"刚落地那一帧"。</para>
        /// </summary>
        private static bool StandingOnTop(Rect b, Rect cell)
            => b.xMax > cell.xMin && b.xMin < cell.xMax
               && Mathf.Abs(b.yMin - cell.yMax) <= 0.25f;

        /// <summary>
        /// 顶砖时把**站在砖上**的敌人翻飞 —— clone `RegularBrickBlock.cs:31-34`
        /// （<c>enemiesOnTop</c> 逐个 <c>BlockHitEnemy</c> ⇒ <c>HitBelowByBlock()</c> = `FlipAndDie()`）。
        /// <para>分值取 <c>hitByBlockBonus</c>：clone 里栗宝宝 / 乌龟 / 龟壳 / 飞龟**都是 100**
        /// （`Goomba.cs:15`、`Koopa.cs:12`、`KoopaShell.cs:31`、`KoopaWinged.cs:11`）= 本工程的
        /// <see cref="GameConst.ScoreStomp"/>。食人花 / 库巴是 0（它们本来也不站在砖上）。</para>
        /// </summary>
        private void KillEnemiesOnTop()
        {
            if (_enemies == null) return;
            var cell = CellRect;
            foreach (var e in _enemies.Active)
            {
                if (e.Dead) continue;
                if (!StandingOnTop(e.Bounds, cell)) continue;
                e.Flip();
                _score?.Add(GameConst.ScoreStomp);
                _items?.SpawnScoreText(GameConst.ScoreStomp, e.Bounds.center);
                Game.Logger.Info("Block",
                    $"顶砖撞飞砖上的敌人（格 {Tile.x},{Tile.y}，+{GameConst.ScoreStomp}）");
            }
        }

        /// <summary>
        /// 顶砖时"收走"砖上那枚金币 —— clone `RegularBrickBlock.cs:30-34`：
        /// <c>Instantiate(BlockCoin, transform.position + (0,2))</c> + <c>Destroy(coinOnTop)</c>。
        /// <para>只有**普通砖**有这条（问号块 / 含道具的砖在 clone 里没有金币检测器）。</para>
        /// </summary>
        private void CollectCoinOnTop()
        {
            if (_items == null) return;
            var cell = CellRect;
            foreach (var it in _items.Active)
            {
                if (it.Taken) continue;
                if (it.Content != BlockContent.Coin) continue;
                if (!StandingOnTop(it.Bounds, cell)) continue;

                it.Collect();   // 原版是 Destroy(coinOnTop)：那枚旧金币不发分、不响、直接没
                // 新金币在砖上方 2 格弹出 —— 分数与音效都由它这条链给（见 HitFromBelow 的注释②）。
                _items.Spawn(BlockContent.Coin, new Vector2(Tile.x + 0.5f, Tile.y + 2f));
                _items.SpawnScoreText(GameConst.ScoreCoin, new Vector2(Tile.x + 0.5f, Tile.y + 1f));
                _score?.AddCoin();
                _score?.Add(GameConst.ScoreCoin);
                _audio?.PlaySfx(Sfx.Coin);
                Game.Logger.Info("Block",
                    $"顶砖收走砖上的金币（格 {Tile.x},{Tile.y}，+1 枚、+{GameConst.ScoreCoin}）");
                return;   // 原版那块砖上只有一个 Coin Detector 槽位，一次只收一枚
            }
        }

        /// <summary>
        /// 出币时那行分数文字的**生成点**。
        /// <para>
        /// 出处 = clone `Assets/Scripts/BlockCoin.cs:11`：`AddCoin(transform.position + Vector3.down)`
        /// —— 金币本体的位置是方块上方一格（<c>Tile.y + 1</c>），再 <c>down</c> 一格 ⇒ 方块本身这一格。
        /// 不在这里另定一个"好看的位置"：那行字与原版必须落在同一个地方。
        /// </para>
        /// </summary>
        private Vector2 CoinTextPos => new Vector2(Tile.x + 0.5f, Tile.y);

        /// <summary>顶一下：音效 + 位移。</summary>
        private void Bump(string sfx)
        {
            _audio?.PlaySfx(sfx);
            _bumpTimer = 0f;
        }

        /// <summary>变成"已顶过"的暗块。</summary>
        private void MarkUsed()
        {
            Used = true;
            if (_sr != null && _usedSprite != null) _sr.sprite = _usedSprite;
        }

        /// <summary>砖块被顶碎。</summary>
        private void BreakBrick()
        {
            Solid = false;
            _owner.Unregister(Tile);
            // 碎片从方块的【视觉中心】迸出：transform 现在在格底边，所以要 +0.5。
            _owner.SpawnParticles(transform.position + new Vector3(0f, 0.5f, 0f));
            _audio?.PlaySfx(Sfx.Break);
            _score?.Add(GameConst.ScoreBrickBreak);
            Clear();
        }

        private static bool IsQuestionBlock(EntityKind kind)
            => kind == EntityKind.QuestionBlock
               || kind == EntityKind.QuestionBlockMushroom
               || kind == EntityKind.QuestionBlockOneUp;

        public void Clear()
        {
            Solid = false;
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }

    /// <summary>砖块碎片：抛物线飞出后消失。</summary>
    internal sealed class BrickParticle : MonoBehaviour
    {
        private Vector2 _vel;
        private float _life = 2f;
        private float _spin;

        public void Init(Vector2 velocity) => _vel = velocity;

        private void Update()
        {
            var dt = Time.deltaTime;
            _life -= dt;
            if (_life <= 0f) { Destroy(gameObject); return; }

            _vel.y -= GameConst.Gravity * dt;
            transform.position += new Vector3(_vel.x * dt, _vel.y * dt, 0f);
            _spin += 720f * dt;
            transform.rotation = Quaternion.Euler(0f, 0f, _spin);
        }
    }
}
