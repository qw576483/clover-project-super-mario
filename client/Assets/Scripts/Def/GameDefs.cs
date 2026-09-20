namespace SuperMario.Def
{
    /// <summary>
    /// 马里奥的三种形态。原版是「小 → 大 → 火」，受伤时逐级后退一级。
    /// </summary>
    public enum PowerState
    {
        Small = 0,
        Big = 1,
        Fire = 2,
    }

    /// <summary>
    /// 关卡瓦片的碰撞属性。
    /// </summary>
    public enum TileSolid
    {
        /// <summary>装饰物（云 / 山丘 / 灌木）：穿过去。</summary>
        Background = 0,

        /// <summary>地形（地面 / 砖 / 水管）：挡住。</summary>
        Solid = 1,
    }

    /// <summary>
    /// 实体种类。名字与关卡文件里写的预制体名一致，避免两处各起一套命名。
    /// </summary>
    public enum EntityKind
    {
        Goomba,
        Brick,
        QuestionBlock,
        QuestionBlockMushroom,

        /// <summary>装着 1-UP 的方块（1-2 里有，原版 1-1 的隐藏块也是）。</summary>
        QuestionBlockOneUp,

        /// <summary>
        /// 含 **★无敌星** 的砖：顶出无敌星，砖本身**不可打碎**（原版里"装着道具的砖"都顶不碎）。
        /// <para>出处：元素表 §B2-1 —— T(87,0) 原版是 `Brown Brick Block- Starman.prefab`
        /// （`World 1-1.unity:4633`）。</para>
        /// </summary>
        BrickStarman,

        /// <summary>
        /// **多金币砖**：连顶出币（上限见 <c>GameConst.MultiCoinBrickCoins</c>），币出完变暗块，同样顶不碎。
        /// <para>出处：元素表 §B2-2 —— T(80,0) 原版是 `Brown Brick Block- MultiCoin.prefab`（`:8945`）。</para>
        /// </summary>
        BrickMultiCoin,

        /// <summary>
        /// **隐形**的 1-UP 块：看不见但实心，从下方顶到才现形并弹出 1-UP 蘑菇。
        /// <para>出处：元素表 §B2-3 —— T(50,1) 原版是 `Hidden Question Block- Oneup`（`:627` + `:4930`）。</para>
        /// </summary>
        HiddenBoxOneUp,
    }

    /// <summary>
    /// 传送管的进出事件：由玩法层判定（站在管口按压 / 走进侧向管口），交给流程层执行换场。
    /// <para>
    /// 为什么要一个"判定 → 执行"的交接而不是在玩法层直接换场：换场要动关卡会话与资源，
    /// 那是流程层的职责；同样，流程层也不该每帧去读玩家的位置 —— 分工与
    /// <c>PendingDeath</c> / <c>ReachedFlag</c> 完全一致。
    /// </para>
    /// </summary>
    public enum PipeWarpKind
    {
        None = 0,

        /// <summary>主关卡 → 金币房（站在第 4 根水管顶上按 ↓）。</summary>
        EnterBonusRoom,

        /// <summary>金币房 → 主关卡（走进密室右侧的侧向管）。</summary>
        ExitBonusRoom,

        /// <summary>
        /// 主关卡 → **下一段**（1-2 地下段走右侧墙上的侧向管口 → 地表段 `World1-2-Surface`）。
        /// <para>与金币房那两条不同：这一步是往前走，**不回到原处**，所以流程层执行的是"切下一段"，
        /// 而不是"挂起主关卡 + 起一个子会话"。</para>
        /// </summary>
        EnterNextSection,
    }

    /// <summary>
    /// 顶砖块时弹出来的东西。
    /// </summary>
    public enum BlockContent
    {
        None = 0,
        Coin,
        Mushroom,
        FireFlower,
        OneUp,

        /// <summary>无敌星：吃到后一段时间内碰谁杀谁。</summary>
        Star,
    }

    /// <summary>
    /// 玩家当前的大状态。原版里死亡 / 变身 / 通关滑杆都是「不接受操作」的过场。
    /// </summary>
    public enum PlayerMotion
    {
        Normal,
        Growing,
        Shrinking,
        Dead,
        FlagSlide,
        FlagWalk,
        LevelClear,

        /// <summary>
        /// 传送管里的移动（进管下沉 / 出管顶出 / 侧向走进管口）。
        /// <para>
        /// 这一段**不做碰撞解算**：人是"插在管子里"的，位置写死更贴原版，也让"顶出高度"
        /// 与"管口顶面"精确对齐（走物理会被管壁推开）。
        /// </para>
        /// </summary>
        PipeMove,
    }
}
