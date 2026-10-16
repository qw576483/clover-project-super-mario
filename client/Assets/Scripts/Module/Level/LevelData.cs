using System.Collections.Generic;
using System.Globalization;
using CloverEngine;

namespace SuperMario.Module.Level
{
    /// <summary>一个关卡瓦片（静态地形 / 背景装饰）。</summary>
    public sealed class LevelTile
    {
        public int X;
        public int Y;
        /// <summary>0 = 地形（参与碰撞），1 = 背景装饰（只画不挡）。</summary>
        public int Layer;
        public string Sprite;
    }

    /// <summary>关卡里摆放的一个实体（敌人 / 砖块 / 问号块）。</summary>
    public sealed class LevelEntity
    {
        /// <summary>格中心的 X（0.5 结尾，例如 80.5 表示第 80 格中心）。</summary>
        public float X;
        public float Y;
        /// <summary>预制体名，取值见 <see cref="Def.EntityKind"/> 的对应关系。</summary>
        public string Prefab;

        /// <summary>
        /// 垂直升降台的**行程 + 初速方向**是否随实体行一起给了。
        /// <para>
        /// `E &lt;x&gt; &lt;y&gt; MovingPlatform &lt;downStopY&gt; &lt;upStopY&gt; &lt;startDir&gt;`
        /// —— `x / y` = 原版 `Moving Platform Vertical Spawner` 的 **Spawn Pos**（台面出现的落点），
        /// 后三个数 = 同一个 spawner 的 `Down Stop` / `Up Stop`（**世界 y**）与 `directionY`
        /// （+1 向上 / −1 向下）。
        /// </para>
        /// <para>
        /// 出处（prefab 字段名 + clone 场景实例行号）写在 `Levels/World1-2.txt` 的文件头，
        /// 并由 `原版资源/解析/脚本/check_sections_12.py` 第 5 段从 prefab / 场景**重新解析出来比对**（可复跑）。
        /// </para>
        /// </summary>
        public bool HasPatrol;
        /// <summary>下止点的世界 y（= spawner 根的世界 y + `Down Stop` 的 local y）。</summary>
        public float DownStopY;
        /// <summary>上止点的世界 y（= spawner 根的世界 y + `Up Stop` 的 local y）。</summary>
        public float UpStopY;
        /// <summary>初速方向：+1 向上、−1 向下（= spawner 的 `directionY`）。</summary>
        public int StartDir;
    }

    /// <summary>
    /// 关卡数据：纯解析结果，不含任何 Unity 对象。
    /// <para>
    /// 刻意不解析成「二维数组」而是保留原始列表：关卡是**稀疏**的（803 个瓦片散在
    /// 223×13 的格子里，其中地面占了绝大多数），铺成满二维数组既浪费又掩盖了数据本身。
    /// 需要网格查询时由 <see cref="LevelModule"/> 建一次索引即可。
    /// </para>
    /// 数据来源见 <c>Resources/Levels/World1-1.txt</c> 的文件头注释。
    /// </summary>
    public sealed class LevelData
    {
        public readonly List<LevelTile> Tiles = new List<LevelTile>();
        public readonly List<LevelEntity> Entities = new List<LevelEntity>();

        public int MinTileX { get; private set; }
        public int MaxTileX { get; private set; }
        public int MinTileY { get; private set; }
        public int MaxTileY { get; private set; }

        /// <summary>玩家出生点（格坐标）。关卡文件里不写，由游戏固定放在第一格地面上方。</summary>
        public float SpawnX { get; private set; } = 2.5f;
        public float SpawnY { get; private set; }

        /// <summary>
        /// 关卡文件是否用 `# spawn &lt;x&gt; &lt;y&gt;` 指定了出生点。
        /// <para>原版每个场景都摆了一个 `Spawn Point` prefab（1-2 地表段是 (0.5,1.5)，正好在出管口里），
        /// 所以只要原版摆了，就必须按它出生 —— 用"最左 + 3.5 格"的通用规则会生在半空中。</para>
        /// </summary>
        public bool HasSpawn { get; private set; }

        /// <summary>
        /// 本关有没有旗杆/城堡。
        /// <para>默认 **有**（1-1 与旧关卡文件都不写这一项，行为不变）。
        /// 1-2 的地下段**没有** —— 原版的旗杆与城堡在后面的地表段（clone `World 1-2 - Castle Cut.unity`）里，
        /// 早先用"右边界 - 25.5"的公式硬推出一根旗杆塞进地下段，结果它正好插在结尾那堵墙里。</para>
        /// </summary>
        public bool HasFlagpole { get; private set; } = true;

        /// <summary>`# flagpole &lt;格x&gt;`：旗杆所在格（绘制中心 = 格 x + 0.5）。NaN = 没声明（沿用公式）。</summary>
        public float FlagpoleCellX { get; private set; } = float.NaN;

        /// <summary>`# castle &lt;中心x&gt;`：城堡中心（同时也是"走进城堡"的终点）。NaN = 没声明（沿用公式）。</summary>
        public float CastleCenterX { get; private set; } = float.NaN;

        /// <summary>
        /// `# side-exit &lt;格x&gt; &lt;y&gt;`：走到这个侧向管口就切到**下一段**。
        /// <para>原版 1-2 地下段的结尾没有旗杆，而是右侧墙上伸出来的一根侧向管 ——
        /// 走进去就出到地表段。所以它既不是"通关"，也不是"进密室"。</para>
        /// </summary>
        public bool HasSideExit { get; private set; }
        public float SideExitFaceX { get; private set; }
        public float SideExitY { get; private set; }

        /// <summary>
        /// `# pipe-rise &lt;x&gt; &lt;y&gt;`：本关开局要播"从出管口升起"过场时，**管顶站姿**的脚底坐标。
        /// <para>
        /// 原版 1-2 地表段的出管口就是这种：`Spawn Point @clone (0.5,1.5)` 摆在**管口里**，
        /// 人从那里升到管顶再接管操作（出处：原版城堡关位图 dump 的 `Spawn Point`；
        /// 管顶 = 出管那 4 格瓦片 `T 0 0 _18` / `T 1 0 _19` / `T 0 1 _5` / `T 1 1 _6` 的上沿 y=2）。
        /// 起点由 `# spawn` 给（就是原版那个 Spawn Point），本指令只声明终点。
        /// </para>
        /// <para>
        /// 显式声明而不是"代码里推一个"：起点与终点都出自原版数据 ⇒ 升起距离不是写死的格数（E-10 的口径）。
        /// 不声明这一行 = 不播升起过场（其余关都是直接落在出生点）。
        /// </para>
        /// </summary>
        public bool HasPipeRise { get; private set; }
        public float PipeRiseX { get; private set; }
        public float PipeRiseY { get; private set; }

        private bool _boundsInit;

        /// <summary>
        /// 解析关卡文本。**格式错误不抛异常**，而是打日志跳过该行 —— 一条坏行不该让整个关卡打不开，
        /// 但也绝不能静默（那样会得到"关卡少了一块却不知道为什么"）。
        /// </summary>
        public static LevelData Parse(string text, string sourceName)
        {
            var data = new LevelData();
            if (string.IsNullOrEmpty(text))
            {
                Game.Logger.Error("Level", $"关卡数据为空：{sourceName}");
                return data;
            }

            var lines = text.Split('\n');
            var bad = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line[0] == '#')
                {
                    ParseDirective(data, line, sourceName);
                    continue;
                }

                var p = line.Split(' ');
                if (p[0] == "T" && p.Length >= 5)
                {
                    if (int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
                        int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) &&
                        int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var layer))
                    {
                        data.Tiles.Add(new LevelTile { X = x, Y = y, Layer = layer, Sprite = p[4] });
                        data.Expand(x, y);
                    }
                    else { bad++; }
                }
                else if (p[0] == "E" && p.Length >= 4)
                {
                    if (float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ex) &&
                        float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ey))
                    {
                        var ent = new LevelEntity { X = ex, Y = ey, Prefab = p[3] };
                        // 升降台的可选尾巴：<downStopY> <upStopY> <startDir>（见 LevelEntity.HasPatrol）。
                        // 解析失败**不算坏行** —— 缺了就是"没给行程"，由 StageSession 拒绝生成该实例并报 Error
                        // （不许退回一个"看起来合理"的默认行程）。
                        if (p.Length >= 7 &&
                            float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var dsy) &&
                            float.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var usy) &&
                            int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sdir))
                        {
                            ent.HasPatrol = true;
                            ent.DownStopY = dsy;
                            ent.UpStopY = usy;
                            ent.StartDir = sdir;
                        }
                        data.Entities.Add(ent);
                    }
                    else { bad++; }
                }
                else
                {
                    bad++;
                }
            }

            if (bad > 0)
            {
                Game.Logger.Warn("Level", $"关卡 {sourceName} 有 {bad} 行格式非法，已跳过");
            }

            // 默认出生 y（= 关卡最低格）只在关卡文件**没**声明 `# spawn` 时兜底。
            // 无条件写会把解析出来的值覆盖掉 —— 实测：`# spawn 1 2` 被改成 (1,-2)，
            // 人一进这一关就生在实心地面里（然后被挤出去、掉出世界）。
            if (!data.HasSpawn) data.SpawnY = data.MinTileY;
            Game.Logger.Info("Level",
                $"关卡已解析 {sourceName}：瓦片 {data.Tiles.Count}、实体 {data.Entities.Count}、" +
                $"范围 x[{data.MinTileX},{data.MaxTileX}] y[{data.MinTileY},{data.MaxTileY}]");
            return data;
        }

        /// <summary>
        /// 解析 `#` 开头的指令行。**只认白名单**，其余一律当注释 ——
        /// 关卡文件里本来就有大量说明文字，不能因为"看不懂"就报错。
        /// <para>认的指令（都带出处，值来自原版场景，不是这里推的）：
        /// `# flagpole &lt;格x&gt;` / `# castle &lt;中心x&gt;` / `# spawn &lt;x&gt; &lt;y&gt;` /
        /// `# side-exit &lt;格x&gt; &lt;y&gt;` / `# pipe-rise &lt;x&gt; &lt;y&gt;` / `# no-flagpole`。</para>
        /// </summary>
        private static void ParseDirective(LevelData data, string line, string sourceName)
        {
            var p = line.TrimStart('#').Trim().Split(' ');
            if (p.Length == 0) return;
            switch (p[0])
            {
                case "flagpole" when p.Length >= 2 &&
                                     float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var fx):
                    data.HasFlagpole = true;
                    data.FlagpoleCellX = fx;
                    break;
                case "castle" when p.Length >= 2 &&
                                   float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cx):
                    data.CastleCenterX = cx;
                    break;
                case "spawn" when p.Length >= 3 &&
                                  float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var sx) &&
                                  float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sy):
                    data.HasSpawn = true;
                    data.SpawnX = sx;
                    data.SpawnY = sy;
                    break;
                case "side-exit" when p.Length >= 3 &&
                                     float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ex) &&
                                     float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ey):
                    data.HasSideExit = true;
                    data.SideExitFaceX = ex;
                    data.SideExitY = ey;
                    break;
                case "pipe-rise" when p.Length >= 3 &&
                                      float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rx) &&
                                      float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ry):
                    data.HasPipeRise = true;
                    data.PipeRiseX = rx;
                    data.PipeRiseY = ry;
                    break;
                case "no-flagpole":
                    data.HasFlagpole = false;
                    break;
                default:
                    // 普通注释：按原样忽略。写成 Warn 会把每个说明行都刷一遍，反而淹没真问题。
                    break;
            }
        }

        private void Expand(int x, int y)
        {
            if (!_boundsInit)
            {
                MinTileX = MaxTileX = x;
                MinTileY = MaxTileY = y;
                _boundsInit = true;
                return;
            }
            if (x < MinTileX) MinTileX = x;
            if (x > MaxTileX) MaxTileX = x;
            if (y < MinTileY) MinTileY = y;
            if (y > MaxTileY) MaxTileY = y;
        }
    }
}
