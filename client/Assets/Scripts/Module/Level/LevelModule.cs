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

        /// <summary>关卡左边界（世界 x）。**唯一来源 = <see cref="TileWorld.MinX"/>**（构建期由关卡数据的 MinTileX 给）。</summary>
        public float MinWorldX => _world.MinX;

        /// <summary>关卡右边界（世界 x）。**唯一来源 = <see cref="TileWorld.MaxX"/>**（= 数据 MaxTileX + 1，格 (X,Y) 占 [X, X+1]）。</summary>
        public float MaxWorldX => _world.MaxX;

        /// <summary>地面顶面高度（世界 y）。**唯一来源 = <see cref="TileWorld.GroundTopY"/>**（构建期由 <see cref="ComputeGroundTop"/> 算）。</summary>
        public float GroundTopY => _world.GroundTopY;

        public float FlagpoleX { get; private set; } = 184.5f;

        /// <summary>
        /// 旗杆距关卡右边界的距离（格）。由 1-1 实测反推：右边界 210、旗杆 184.5 ⇒ 25.5。
        /// 用它推导比写死绝对坐标安全：换关卡时旗杆自动跟着到末尾。
        /// </summary>
        private const float FlagpoleInsetFromRight = 25.5f;

        /// <summary>
        /// 马里奥能"碰到"旗杆的 x —— 即旗杆所在格的**左边缘**（不是旗杆中心）。
        /// <para>
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

        /// <summary>
        /// 空间事实面（实心位图 + 移动托台顶高 + 世界边界）—— **收敛到引擎 <see cref="TileWorld"/>**。
        /// <para>
        /// 181-215 行（<c>HashSet&lt;int&gt;</c> 位图 + <c>Dictionary&lt;int,float&gt;</c> 托台 + <c>Key(tx,ty)</c>）
        /// 逐字下沉的。收下它是为了消掉那套 32 位键：`(tx &lt;&lt; 16) ^ (ty + 512)` 在 |tx| ≥ 2^15
        /// 或 ty 超出 [-512, 65022] 时会**键碰撞** ⇒ 误判实心（"明明没有砖却撞上了"，且不报错）。
        /// 引擎换成了 64 位双射键。
        /// </para>
        /// <para>
        /// 边界口径由引擎 <c>Runtime/Presentation/Map.cs:14-15</c> 划死：本类**只搬"空间事实"**
        /// （这一格实不实心 / 托台顶面在哪 / 世界到哪为止），**不搬位移解算** ——
        /// 「输入 → 位移 → 贴墙滑动」（用多大半径、几点采样、撞墙是停还是滑）属玩法手感，
        /// 留在 <c>PlayerActor</c> 与各实体自己的 <c>Update</c> 里。瓦片贴图绘制也留在本文件（<see cref="FinishBuild"/>）。
        /// </para>
        /// </summary>
        private readonly TileWorld _world = new TileWorld();

        private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
        private int _pendingSprites;

        public bool IsSolidTile(int tx, int ty) => _world.IsSolid(tx, ty);

        public void SetSolid(int tx, int ty, bool solid) => _world.SetSolid(tx, ty, solid);

        public void SetCarrier(int tx, int ty, float topY) => _world.SetCarrierTop(tx, ty, topY);
        public void ClearCarrier(int tx, int ty) => _world.ClearCarrierTop(tx, ty);
        public bool TryGetCarrierTop(int tx, int ty, out float topY) => _world.TryGetCarrierTop(tx, ty, out topY);

        /// <summary>
        /// 世界坐标 → 格：**必须** <see cref="Mathf.FloorToInt"/>（世界 x∈[X, X+1) 属于第 X 格）。
        /// 负数坐标下 <c>(int)</c> 是向零取整，会让 x=-0.5 落到第 0 格 —— 表现为「站在坑里也能踩到地」。
        /// 口径与实现都在引擎 <see cref="TileWorld.IsSolidAt"/>（本方法只是转发）。
        /// </summary>
        public bool IsSolidAt(float x, float y) => _world.IsSolidAt(x, y);

        /// <summary>
        /// 构建关卡。<paramref name="onDone"/> 在**所有贴图加载完之后**才回调 ——
        /// 提前回调会让第一帧出现一片空场景（读条条走完了但画面是白的）。
        /// </summary>
        public void Build(LevelData data, Action onDone)
        {
            Data = data;
            // 池化取舍（本文件全部 4 处 `new GameObject` 都是同一个结论）：**不进对象池**。
            //   判据是"高频短命" —— 池化只在"同一类对象一局里反复生成/销毁"时才划算；
            //   这里 ① `[Level]` 根 / ② `Background` / ③ `Terrain` 各 1 个，
            //   ④ 瓦片每个 1 个 —— **生命周期都 = 一整关**（回菜单时随 `Clear()` 整体销毁），
            //   一关内从不销毁重生。走池反而要多一层 key 注册 + 归还记账，收益为 0。
            //   真正高频短命的（火球 / 道具 / 砖块碎片）不在本文件，按各自模块评估。
            Root = new GameObject("[Level]");

            // 世界边界先给一次（**唯一来源 = TileWorld.SetBounds**）：
            //   · MinX  = 数据里最小的格 x（原版 1-1 是 -13，左侧那段预留延伸地面）；
            //   · MaxX  = 最大格 x + 1（格 (X,Y) 占世界 [X, X+1]，所以右边界要 +1）；
            //   · GroundTopY = ComputeGroundTop(data)（最厚那一层实心地形的上沿）。
            //   下面 FlagpoleX / CastleDoorX 都由 MaxWorldX（= _world.MaxX）推出来，所以必须先设。
            _world.SetBounds(data.MinTileX, data.MaxTileX + 1, ComputeGroundTop(data));

            // 实心位图：纯数据，不需要等贴图 ⇒ 碰撞查询立刻可用（贴图加载完才 FinishBuild）。
            var solidCount = 0;
            foreach (var t in data.Tiles)
            {
                if (t.Layer != 0) continue;
                _world.SetSolid(t.X, t.Y);
                solidCount++;
            }

            // 旗杆位置：优先用关卡文件里声明的（`# flagpole <格x>`），没有才按公式推。
            //
            // 为什么要有"声明的"这条路：公式是"距右边界 25.5 格"，那是从 **1-1** 反推的。
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
            // （不进对象池：一关各 1 个，生命周期 = 一整关，见 Build 开头的池化取舍。）
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
            // 判据是 BrickTilesAsEntities（= 地下关 **且不是**管中密室）：金币房的青砖是房间的墙，
            // 它**不**转成实体（StageSession 那边同一个判据），所以这里必须照常绘制。
            var skipUndergroundBricks = StageContext.BrickTilesAsEntities;

            foreach (var t in data.Tiles)
            {
                if (!_spriteCache.TryGetValue(t.Sprite, out var sp)) continue;
                if (skipUndergroundBricks && t.Layer == 0 && t.Sprite == SpriteNames.TileUndergroundBrick) continue;

                // 瓦片保持"现造"（不进对象池）：每个瓦片一关只建一次、随关卡整体销毁，
                // 生命周期 = 一整关 ⇒ 池化的复用价值为 0（见 Build 开头的池化取舍）。
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

        /// <summary>
        /// 销毁整关（回主菜单时调）。对象池与事件由调用方负责收。
        /// <para>
        /// <see cref="TileWorld.Clear"/> 只清**格子数据**（实心 + 托台），**不动**世界边界 ——
        /// 那是"世界事实"，下一次 <see cref="Build"/> 会由 <c>SetBounds</c> 重新给（引擎 TileWorld 的契约）。
        /// </para>
        /// </summary>
        public void Clear()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null;
            _world.Clear();
            _spriteCache.Clear();
            Ready = false;
        }
    }
}
