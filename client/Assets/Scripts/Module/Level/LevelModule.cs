using System;
using System.Collections.Generic;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Module.Flow;
using UnityEngine;

namespace SuperMario.Module.Level
{
    /// <summary>
    /// 关卡门面：外部只经过这里问「这一格能不能站」。
    /// </summary>
    public interface ILevel
    {
        /// <summary>是否已构建完成（贴图都加载好了）。</summary>
        bool Ready { get; }

        /// <summary>原始数据（实体列表由 <c>EntityModule</c> 消费）。</summary>
        LevelData Data { get; }

        /// <summary>关卡根的 GameObject（回菜单时整体销毁）。</summary>
        GameObject Root { get; }

        /// <summary>世界坐标 (x, y) 处是否有实心地形。</summary>
        bool IsSolidAt(float x, float y);

        /// <summary>格坐标是否有实心地形。</summary>
        bool IsSolidTile(int tx, int ty);

        /// <summary>
        /// 运行期增删实心格。
        /// <para>
        /// 砖块 / 问号块是**实体**而不是地形瓦片（它们能被顶、被撞碎），但碰撞必须和地形走
        /// 同一套查询 —— 否则马里奥会从砖块里穿过去。所以实体在生成时把自己登记进这张位图，
        /// 碎裂时注销。不做这一步，就只能给实体单独写一遍碰撞，两套逻辑必然对不齐。
        /// </para>
        /// </summary>
        void SetSolid(int tx, int ty, bool solid);

        /// <summary>
        /// 登记一个"会移动的实心格"（移动平台）：<paramref name="topY"/> = 它台面的**真实**顶部 y。
        /// <para>
        /// 为什么要和 <see cref="SetSolid"/> 分开：实心位图只能表达"这一格挡不挡"，
        /// 格顶边恒为整数；而"脚踩在台面上的真实高度"必须精确到小数 ——
        /// 否则平台上升时马里奥会被<b>一格一格地弹起来</b>（先被平台穿过、再瞬移一格）。
        /// </para>
        /// </summary>
        void SetCarrier(int tx, int ty, float topY);

        /// <summary>注销一个移动平台格（平台移出该格 / 被销毁时）。</summary>
        void ClearCarrier(int tx, int ty);

        /// <summary>该格若是移动平台，取它台面的真实顶部 y（没有则返回 false）。</summary>
        bool TryGetCarrierTop(int tx, int ty, out float topY);

        /// <summary>关卡左边界（世界 x）。相机与左墙用它。</summary>
        float MinWorldX { get; }

        /// <summary>关卡右边界（世界 x）。</summary>
        float MaxWorldX { get; }

        /// <summary>地面顶部高度（世界 y）。出生点、旗杆、城堡都对齐它。</summary>
        float GroundTopY { get; }

        /// <summary>旗杆所在世界 x（用于**绘制**杆身，位于格的中央）。</summary>
        float FlagpoleX { get; }

        /// <summary>
        /// 通关判定的 x：马里奥能碰到旗杆的位置 = 旗杆所在格的**左边缘**。
        /// <para>不能用 <see cref="FlagpoleX"/>：旗杆底下的实心基座会把马里奥挡在左边缘，
        /// 用中心判定会永远差半格（实测导致这一关无法通关）。</para>
        /// </summary>
        float FlagpoleTouchX { get; }

        /// <summary>
        /// 城堡的中心 x（马里奥滑完旗杆后要走进城堡，走到这里为止）。
        /// <para>城堡与"走到哪"必须共用同一个数：两边分开写死，改一处必然漏一处，
        /// 表现就是"人停在城堡外面"或"提前消失"。</para>
        /// </summary>
        float CastleDoorX { get; }

        /// <summary>
        /// 本关有没有旗杆/城堡。没有（1-2 地下段）时：不建装饰、也不做通关判定。
        /// <para>原版 1-2 的旗杆在地表段里，地下段结尾是一根侧向管 —— 见 <see cref="HasSideExit"/>。</para>
        /// </summary>
        bool HasFlagpole { get; }

        /// <summary>出生点是否由关卡数据给出（原版每个场景都摆了 Spawn Point）。</summary>
        bool HasSpawn { get; }

        /// <summary>关卡数据给的出生点（脚底世界坐标）。</summary>
        float SpawnX { get; }
        float SpawnY { get; }

        /// <summary>走到这个侧向管口就切到下一段（原版 1-2 地下段 → 地表段）。</summary>
        bool HasSideExit { get; }
        float SideExitFaceX { get; }
        float SideExitY { get; }

        /// <summary>
        /// 开局要不要播"从出管口升起"过场；要的话 <see cref="PipeRiseX"/> / <see cref="PipeRiseY"/>
        /// 是**管顶站姿**的脚底坐标（起点 = <see cref="SpawnX"/> / <see cref="SpawnY"/>，即原版 Spawn Point）。
        /// <para>见 <see cref="LevelData.HasPipeRise"/>：1-2 地表段用它，别段为 false。</para>
        /// </summary>
        bool HasPipeRise { get; }
        float PipeRiseX { get; }
        float PipeRiseY { get; }
    }

    /// <summary>
    /// 关卡模块实现：把 <see cref="LevelData"/> 变成可玩场景，并提供 O(1) 的碰撞查询。
    /// <para>
    /// 碰撞用的是**自己维护的位图**，不是 Unity 的 Collider2D。理由：
    /// 平台跳跃需要「先把 X 走完再解 X 的碰撞、再走 Y 解 Y 的碰撞」这种逐轴解算，
    /// 而 2D 物理引擎给的是连续求解 —— 用引擎反而更难做出原版那种干脆的手感，
    /// 而且会引入浮点抖动。位图 + 逐轴解算是这类玩法的标准做法（也属业务自写范畴）。
    /// </para>
    /// </summary>
    internal sealed class LevelModule : ILevel
    {
        public bool Ready { get; private set; }
        public LevelData Data { get; private set; }
        public GameObject Root { get; private set; }
        public float MinWorldX { get; private set; }
        public float MaxWorldX { get; private set; }
        public float GroundTopY { get; private set; } = -3f;
        public float FlagpoleX { get; private set; } = 184.5f;

        /// <summary>
        /// 旗杆距关卡右边界的距离（格）。由 1-1 实测反推：右边界 210、旗杆 184.5 ⇒ 25.5。
        /// 用它推导比写死绝对坐标安全：换关卡时旗杆自动跟着到末尾。
        /// </summary>
        private const float FlagpoleInsetFromRight = 25.5f;

        /// <summary>
        /// 马里奥能"碰到"旗杆的 x —— 即旗杆所在格的**左边缘**（不是旗杆中心）。
        /// <para>
        /// 踩过的坑（症状：这一关永远无法通关）：旗杆立在自己的**实心基座方块**上，
        /// 马里奥向右跑会先被基座挡住，右边缘最多只能到基座左边缘（= 旗杆格左边缘）。
        /// 而触发判定若用旗杆中心 <see cref="FlagpoleX"/>（比左边缘靠右半格），
        /// 就永远差那半格 —— 实测他卡在 x=183.6（右边缘 183.975）不动整整 11 秒，
        /// 流程停在 Stage，既不通关也不死亡，一个字都不报。
        /// </para>
        /// <para><see cref="FlagpoleX"/> 仍用于<b>绘制</b>（杆身在格的中央）。</para>
        /// </summary>
        public float FlagpoleTouchX => FlagpoleX - 0.5f;

        /// <summary>城堡中心 x（同时也是"走进城堡"的终点）。<see cref="LevelProps"/> 用它摆城堡，玩家用它当终点。</summary>
        public float CastleDoorX { get; private set; }

        /// <inheritdoc />
        public bool HasFlagpole { get; private set; } = true;

        /// <inheritdoc />
        public bool HasSpawn { get; private set; }

        /// <inheritdoc />
        public float SpawnX { get; private set; }

        /// <inheritdoc />
        public float SpawnY { get; private set; }

        /// <inheritdoc />
        public bool HasSideExit { get; private set; }

        /// <inheritdoc />
        public float SideExitFaceX { get; private set; }

        /// <inheritdoc />
        public float SideExitY { get; private set; }

        /// <inheritdoc />
        public bool HasPipeRise { get; private set; }

        /// <inheritdoc />
        public float PipeRiseX { get; private set; }

        /// <inheritdoc />
        public float PipeRiseY { get; private set; }

        /// <summary>实心位图。存 HashSet 而不是二维数组：关卡稀疏（803/2900），省内存也省一次清空。</summary>
        private readonly HashSet<int> _solid = new HashSet<int>();

        /// <summary>
        /// 移动平台格 → 台面真实顶部 y。与 <see cref="_solid"/> 共用同一套 <see cref="Key"/>。
        /// 数量极少（1-2 只有 2 个平台），用小字典即可。
        /// </summary>
        private readonly Dictionary<int, float> _carriers = new Dictionary<int, float>();

        private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
        private int _pendingSprites;

        /// <summary>位图键：把 (tx, ty) 压成一个 int。ty 先偏到非负区间，避免负数位移出符号位。</summary>
        private static int Key(int tx, int ty) => (tx << 16) ^ (ty + 512);

        public bool IsSolidTile(int tx, int ty) => _solid.Contains(Key(tx, ty));

        public void SetSolid(int tx, int ty, bool solid)
        {
            var k = Key(tx, ty);
            if (solid) _solid.Add(k);
            else _solid.Remove(k);
        }

        public void SetCarrier(int tx, int ty, float topY) => _carriers[Key(tx, ty)] = topY;
        public void ClearCarrier(int tx, int ty) => _carriers.Remove(Key(tx, ty));
        public bool TryGetCarrierTop(int tx, int ty, out float topY) => _carriers.TryGetValue(Key(tx, ty), out topY);

        public bool IsSolidAt(float x, float y)
        {
            // 世界 → 格：向下取整（世界 x∈[X, X+1) 属于第 X 格）。
            // 必须用 FloorToInt 而不是 (int)：负数坐标下 (int) 是向零取整，
            // 会让 x=-0.5 落到第 0 格，表现为「站在坑里也能踩到地」。
            return IsSolidTile(Mathf.FloorToInt(x), Mathf.FloorToInt(y));
        }

        /// <summary>
        /// 构建关卡。<paramref name="onDone"/> 在**所有贴图加载完之后**才回调 ——
        /// 提前回调会让第一帧出现一片空场景（读条条走完了但画面是白的）。
        /// </summary>
        public void Build(LevelData data, Action onDone)
        {
            Data = data;
            Root = new GameObject("[Level]");

            // 实心位图：先建好（纯数据，不需要等贴图），这样碰撞查询立刻可用。
            var solidCount = 0;
            foreach (var t in data.Tiles)
            {
                if (t.Layer != 0) continue;
                _solid.Add(Key(t.X, t.Y));
                solidCount++;
            }

            MinWorldX = data.MinTileX;
            MaxWorldX = data.MaxTileX + 1;
            GroundTopY = ComputeGroundTop(data);

            // ★ 旗杆位置：优先用关卡文件里声明的（`# flagpole <格x>`），没有才按公式推。
            //
            // 为什么要有"声明的"这条路：公式是"距右边界 25.5 格"，那是从 **1-1** 反推的。
            // 1-2 原来也走这条公式，结果算出 x=166.5 —— 而按原版真值，那里是地下段结尾的
            // 实心墙（墙 x=157..173,y=0..2）+ 侧向管；旗杆因此被插进墙里，过关时
            // 人被摆进实心墙、脚下还没地板（x=152..160 是空洞），直接掉出世界。
            // 原版 1-2 的旗杆在【地表段】（clone `World 1-2 - Castle Cut.unity`，Goal Post x=19），
            // 所以它由地表段的关卡文件声明，地下段则写 `# no-flagpole`。
            HasFlagpole = data.HasFlagpole;
            FlagpoleX = float.IsNaN(data.FlagpoleCellX)
                ? MaxWorldX - FlagpoleInsetFromRight
                : data.FlagpoleCellX + 0.5f;
            CastleDoorX = float.IsNaN(data.CastleCenterX)
                ? MaxWorldX - 12f
                : data.CastleCenterX;

            HasSpawn = data.HasSpawn;
            SpawnX = data.SpawnX;
            SpawnY = data.SpawnY;
            HasSideExit = data.HasSideExit;
            SideExitFaceX = data.SideExitFaceX;
            SideExitY = data.SideExitY;
            HasPipeRise = data.HasPipeRise;
            PipeRiseX = data.PipeRiseX;
            PipeRiseY = data.PipeRiseY;

            // 背景与地形各挂一个父节点：方便整体开关（比如"只看碰撞"的调试），也少两万个层级节点。
            var bgRoot = new GameObject("Background").transform;
            bgRoot.SetParent(Root.transform, false);
            var terraRoot = new GameObject("Terrain").transform;
            terraRoot.SetParent(Root.transform, false);
            // 背景要在地形后面：SpriteRenderer 用 sortingOrder 排序，不依赖层级顺序。
            var bgOrder = -20;

            var distinct = new HashSet<string>();
            foreach (var t in data.Tiles) distinct.Add(t.Sprite);

            _pendingSprites = distinct.Count;
            if (_pendingSprites == 0)
            {
                FinishBuild(bgRoot, terraRoot, data, onDone);
                return;
            }

            foreach (var spriteName in distinct)
            {
                var path = ResPaths.Tile(spriteName);
                var isScenery = spriteName.StartsWith("SceneryTileSprites", StringComparison.Ordinal);
                if (isScenery) path = ResPaths.Scenery(spriteName);

                Game.Res.LoadAsset<Sprite>(path, sp =>
                {
                    if (sp == null)
                    {
                        // 贴图缺失会让那一类瓦片整片消失 —— 必须吵，否则只会看到"关卡缺了一块"。
                        Game.Logger.Error("Level", $"瓦片贴图加载失败：{path}（该类瓦片将不可见）");
                    }
                    else
                    {
                        _spriteCache[spriteName] = sp;
                    }

                    _pendingSprites--;
                    if (_pendingSprites <= 0) FinishBuild(bgRoot, terraRoot, data, onDone);
                });
            }

            Game.Logger.Info("Level",
                $"关卡构建中：实心格 {solidCount}、不同贴图 {distinct.Count}、范围 x[{MinWorldX},{MaxWorldX}]");
        }

        private void FinishBuild(Transform bgRoot, Transform terraRoot, LevelData data, Action onDone)
        {
            // 地下关（1-2）的青砖在关卡文件里是【地形瓦片】，而地形瓦片顶不碎 ——
            // StageSession 会把它们换成可顶碎的 Brick 实体，所以这里必须跳过绘制，
            // 否则同一格会画两遍：一块永远拆不掉的地形砖压在上面。
            //
            // ⚠️ 判据是 BrickTilesAsEntities（= 地下关 **且不是**管中密室）：金币房的青砖是房间的墙，
            // 它**不**转成实体（StageSession 那边同一个判据），所以这里必须照常绘制。
            var skipUndergroundBricks = StageContext.BrickTilesAsEntities;

            foreach (var t in data.Tiles)
            {
                if (!_spriteCache.TryGetValue(t.Sprite, out var sp)) continue;
                if (skipUndergroundBricks && t.Layer == 0 && t.Sprite == SpriteNames.TileUndergroundBrick) continue;

                var go = new GameObject(t.Sprite);
                go.transform.SetParent(t.Layer == 0 ? terraRoot : bgRoot, false);
                // 瓦片 (X,Y) 占世界 [X,X+1]×[Y,Y+1]，所以中心在 +0.5。
                go.transform.localPosition = new Vector3(t.X + 0.5f, t.Y + 0.5f, 0f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = t.Layer == 0 ? 0 : -20;
            }

            Ready = true;
            Game.Logger.Info("Level", $"关卡构建完成：{data.Tiles.Count} 个瓦片");
            onDone?.Invoke();
        }

        /// <summary>
        /// 地面顶部 = 最厚那一层实心地形的上沿。
        /// 不写死 -3：换关卡（1-2 / 地下关）时地面高度会变，写死就会让出生点埋进地里。
        /// </summary>
        private static float ComputeGroundTop(LevelData data)
        {
            var counts = new Dictionary<int, int>();
            foreach (var t in data.Tiles)
            {
                if (t.Layer != 0) continue;
                counts.TryGetValue(t.Y, out var c);
                counts[t.Y] = c + 1;
            }

            var bestY = int.MinValue;
            var bestCount = 0;
            foreach (var kv in counts)
            {
                // 最宽的那一行就是地面；同宽取更高的一行（防止把天花板当成地面）。
                if (kv.Value > bestCount || (kv.Value == bestCount && kv.Key > bestY))
                {
                    bestCount = kv.Value;
                    bestY = kv.Key;
                }
            }

            return bestY == int.MinValue ? -3f : bestY + 1f;
        }

        /// <summary>销毁整关（回主菜单时调）。对象池与事件由调用方负责收。</summary>
        public void Clear()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null;
            _solid.Clear();
            _carriers.Clear();
            _spriteCache.Clear();
            Ready = false;
        }
    }
}
