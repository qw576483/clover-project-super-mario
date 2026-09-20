using SuperMario.Core;
using UnityEngine;

namespace SuperMario.Module.Level
{
    /// <summary>
    /// **一关**的传送管数据：进管口 / 密室落点 / 密室出口面 / 从管里顶回主关的位置 / 密室关卡路径。
    /// <para>
    /// 为什么要抽成一个对象：这套数据现在**两关各有一份**（1-1 与 1-2），而判定
    /// （<c>GameplayModule.TickPipeWarp</c>）与换场（<c>AppFlow.EnterBonusRoom</c> 那四个方法）
    /// 必须**只有一份实现** —— 用"当前是哪一关"去查这张表，而不是给 1-2 再写一套进管机制。
    /// </para>
    /// <para>
    /// <b>每一个数都必须能指到出处</b>（元素表节号 + clone 场景行号，或本项目关卡文件的格），
    /// 见 <see cref="PipeWarpTable.Level11"/> / <see cref="PipeWarpTable.Level12"/> 构造时传的
    /// <c>source</c> 与各自的注释。
    /// </para>
    /// </summary>
    public sealed class PipeWarpInfo
    {
        /// <summary>这一关密室的关卡资源路径（进管的目标）。</summary>
        public readonly string BonusLevelPath;

        /// <summary>进管的那根管叫什么（只用于日志，便于对着元素表复核）。</summary>
        public readonly string EntryPipeDesc;

        /// <summary>进管管口的 x 范围（左含、右不含）：马里奥脚底 x 落在 [Min,Max) 才算站在这根管子上。</summary>
        public readonly float EntryPipeMinX;
        public readonly float EntryPipeMaxX;

        /// <summary>进管管口的顶面 y（马里奥脚底要落在这个高度上）。</summary>
        public readonly float EntryPipeTopY;

        /// <summary>进入密室后的落点（世界坐标 = 脚底）。</summary>
        public readonly Vector2 BonusRoomSpawn;

        /// <summary>密室出口管的左表面：马里奥右边缘到这个位置就是要出管了。</summary>
        public readonly float BonusRoomExitFaceX;

        /// <summary>
        /// 侧向管口有几格宽（从管口面往里的开口深度）。
        /// <para>
        /// <b>出处</b>：clone 预制体 <c>Assets/Prefabs/Pipes/Warp Green Pipe Side Short.prefab</c> ——
        /// 管口那张图 <c>pipe_green_top_side</c>（子物体 Transform `&4767583883616482`，local x = −2）
        /// 的 SpriteRenderer `m_Size: {x: 2, y: 2}` ⇒ **管口 2 格宽**（管口面 = 它的左沿）。
        /// 同一份 prefab 里 `Portal` 的触发器 `BoxCollider2D offset(-2.9,0) size(0.2,1.85)`、
        /// 根节点那圈墙体 `offset(0,3) size(1.8,8)` ⇒ 马里奥是从管口面（−3）**一路走到管身面（−0.9）**
        /// 才被挡住的 —— 也就是说原版确实是"走进管口里"再换场，而不是停在管口面上。
        /// </para>
        /// <para>
        /// 用途：侧向进管的走距 = "从当前右边缘走到管口内侧沿（管口面 + 本值）"。
        /// ⛔ 别拿"马里奥自身贴图宽度"当走距（那是凭空值，登记项 E-24 的根因），
        /// 也别停在管口面上（用户实测："进管道效果没有，人就卡在管道外"）。
        /// </para>
        /// </summary>
        public readonly float SidePipeMouthTiles;

        /// <summary>回到主关卡时顶出管口后的脚底位置（出场管管口顶面）。</summary>
        public readonly Vector2 ReturnPipeTopFeet;

        /// <summary>密室里一共几枚金币（只用于日志，便于对着元素表复核）。</summary>
        public readonly int BonusRoomCoins;

        /// <summary>出处（元素表节号 + clone 场景行号 / 本项目关卡文件）。</summary>
        public readonly string Source;

        public PipeWarpInfo(string bonusLevelPath, string entryPipeDesc,
            float entryPipeMinX, float entryPipeMaxX, float entryPipeTopY,
            Vector2 bonusRoomSpawn, float bonusRoomExitFaceX, Vector2 returnPipeTopFeet,
            int bonusRoomCoins, string source, float sidePipeMouthTiles = 2f)
        {
            BonusLevelPath = bonusLevelPath;
            EntryPipeDesc = entryPipeDesc;
            EntryPipeMinX = entryPipeMinX;
            EntryPipeMaxX = entryPipeMaxX;
            EntryPipeTopY = entryPipeTopY;
            BonusRoomSpawn = bonusRoomSpawn;
            BonusRoomExitFaceX = bonusRoomExitFaceX;
            ReturnPipeTopFeet = returnPipeTopFeet;
            BonusRoomCoins = bonusRoomCoins;
            Source = source;
            SidePipeMouthTiles = sidePipeMouthTiles;
        }
    }

    /// <summary>
    /// 传送管坐标表（1-1 与 1-2 各一套）。
    /// <para>
    /// <b>这里的每一个数都出自《原版1-1与1-2元素表》，不许在别处自己定坐标</b>；
    /// 出处写成「元素表 §X + clone 场景行号 / 本项目关卡文件的行」，改坐标时对着元素表重新核对。
    /// </para>
    /// </summary>
    public static class PipeWarpTable
    {
        // ───────────────────────── 1-1（元素表 §1.3 / §3.2）─────────────────────────
        //
        // 进管的那根管：§1.3 #4 `Warp Green Pipe 2x4 Down.prefab`，clone world `(53,0)`
        //   ⇒ 本工程格 x=43..44, y=-3..0（二宽四高，坐在 y=-4 的地面上）；
        //   出处 `原版资源/参考工程/SMB-clone/Assets/Scenes/World 1-1.unity:3778`。
        //   管口**顶面** = 瓦片 y=0 的上沿 = EntryPipeTopY = 1。
        // 出场管：§1.3 #5 `Warp Green Pipe 2x2 Up.prefab`，clone `(158.5,0.5)`
        //   ⇒ 本工程格 x=149..150, y=-3..-2；出处 `World 1-1.unity:8838`。
        //   管口顶面 = 瓦片 y=-2 的上沿 = ReturnPipeTopFeet.y = -1；
        //   管子跨越 x=149..151，所以"从管里顶出来的位置"取 x=150（两格中间）。
        // 密室落点：§3.2「玩家落点 clone (-6,9)」⇒ 本工程格 (-15,6)（T = clone − (9,3)）。
        // 密室出口管：§3.2 的侧向管 cells=x=5..8,y=0..10 ⇒ 本工程格 x=-4..-1
        //   ⇒ 马里奥在主关卡式地向右走时，右边缘顶到的那一面是 x=-4。

        /// <summary>进管管口的 x 范围（左含、右不含）：马里奥脚底 x 落在 [43,45) 才算站在这根管子上。</summary>
        public const float EntryPipeMinX = 43f;
        public const float EntryPipeMaxX = 45f;

        /// <summary>进管管口的顶面 y（马里奥脚底要落在这个高度上）。</summary>
        public const float EntryPipeTopY = 1f;

        /// <summary>进入金币房后的落点（格中心 = 脚底坐标）。</summary>
        public static readonly Vector2 BonusRoomSpawn = new Vector2(-15f, 6f);

        /// <summary>密室出口管的左表面：马里奥右边缘到这个位置就是要出管了。</summary>
        public const float BonusRoomExitFaceX = -4f;

        /// <summary>回到主关卡时顶出管口后的脚底位置（出场管管口顶面）。</summary>
        public static readonly Vector2 ReturnPipeTopFeet = new Vector2(150f, -1f);

        /// <summary>
        /// 1-1 那一套。上面那六个公开常量/字段就是它的值 —— 这里**复用**它们构造，
        /// 不再抄第二份数字（两处各写一份必然会漂移）。
        /// </summary>
        public static readonly PipeWarpInfo Level11 = new PipeWarpInfo(
            ResPaths.Level11Underground, "第 4 根水管", EntryPipeMinX, EntryPipeMaxX, EntryPipeTopY,
            BonusRoomSpawn, BonusRoomExitFaceX, ReturnPipeTopFeet, 19,
            "元素表 §1.3 #4/#5（World 1-1.unity:3778 / :8838）、§3.2（19 枚金币）");

        // ───────────────────────── 1-2（元素表 §4.1 / §4.2）─────────────────────────
        //
        // 进管的那根管：§4.1 表第 1 行 `Warp Green Pipe 2x3 Down.prefab`，clone world `(100.5,0)`
        //   （modifications 把 sceneName 覆盖成了 `World 1-2 - Underground`，是**唯一**进管点），
        //   出处 `原版资源/参考工程/SMB-clone/Assets/Scenes/World 1-2.unity:11125`。
        //   它在 `client/Assets/Resources/Levels/World1-2.txt` 里的格是 **x=100..101, y=0..2**
        //   （2 宽 3 高、坐在 y=-1 地面上；同文件另两根管 106..107 高 4、112..113 高 2，
        //    正好对上 §4.1 的 2x4 装饰管与 2x2 出场管）⇒ 管口顶面 = 瓦片 y=2 的上沿 = 3。
        // 出场管（从密室顶出来的那根）：§4.1 表第 3 行 `Warp Green Pipe 2x2 Up.prefab`（挂在 `Spawn Pipes` 容器下），
        //   clone `(112.5,0.5)`，出处 `World 1-2.unity:22513`
        //   ⇒ `World1-2.txt` 的 x=112..113, y=0..1 ⇒ 管口顶面 = 瓦片 y=1 的上沿 = 2；
        //   管跨 x∈[112,114] ⇒ "从管里顶出来"取中间 x=113。
        // 密室落点：§4.2「玩家落点 clone (-6,9)」（本密室关卡文件就是 clone 格坐标）⇒ (-6,9)。
        // 密室出口管：§4.2 的出口侧向管管口 2 格在 x=5,6 ⇒ 右边缘顶到的那一面是 x=5。

        /// <summary>1-2 那一套（出处见上面那段注释）。</summary>
        public static readonly PipeWarpInfo Level12 = new PipeWarpInfo(
            ResPaths.Level12Underground, "通往密室的那根 2x3 Down 水管", 100f, 102f, 3f,
            new Vector2(-6f, 9f), 5f, new Vector2(113f, 2f), 17,
            "元素表 §4.1（World 1-2.unity:11125 / :22513）、§4.2（17 枚金币）");

        /// <summary>
        /// 按关卡资源路径取这一关的那一套。
        /// <para>
        /// **密室路径也要映射到同一套**：出管判定要读密室的出口面 x（1-1 密室 = −4、1-2 密室 = 5），
        /// 所以 `Levels/World1-1-Underground` → 1-1、`Levels/World1-2-Underground` → 1-2。
        /// </para>
        /// </summary>
        public static PipeWarpInfo For(string levelPath)
            => levelPath == ResPaths.Level12 || levelPath == ResPaths.Level12Underground
                ? Level12
                : Level11;

        /// <summary>当前这一局的那一套（<see cref="SuperMario.Module.Flow.StageContext.LevelPath"/> 说了算是哪一关）。</summary>
        public static PipeWarpInfo Current
            => For(SuperMario.Module.Flow.StageContext.LevelPath);
    }
}
