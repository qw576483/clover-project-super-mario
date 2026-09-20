using SuperMario.Def;
using SuperMario.Module.Level;
using SuperMario.Module.Player;
using SuperMario.Module.Score;

namespace SuperMario.Module.Flow
{
    /// <summary>
    /// 当前关卡会话的只读视图，给 UI 用。
    /// <para>
    /// <b>为什么需要它</b>：HUD 要知道分数，但它不该持有 <c>StageSession</c>（那会让 UI 依赖整个玩法层），
    /// 也不该自己去 <c>Game.UI.Open</c> 的参数里传（面板可能被重复打开、被延迟打开）。
    /// 这里只暴露两个接口，方向是「UI 读玩法」，不反向。
    /// </para>
    /// <para>
    /// <b>为什么是静态</b>：同一时刻只可能有一局（单机单关），这是**全局唯一事实**；
    /// 硬塞一个 DI 容器来传这一个指针，成本远大于收益。但访问面被压到两个属性，
    /// 并且会话销毁时会清空 —— 避免留下悬空引用（HUD 在关卡结束后仍可能收到一次刷新事件）。
    /// </para>
    /// </summary>
    public static class StageContext
    {
        /// <summary>当前局的分数 / 金币 / 命数 / 时间。无会话时为 null。</summary>
        public static IScore Score { get; private set; }

        /// <summary>当前局的玩家。无会话时为 null。</summary>
        public static IPlayer Player { get; private set; }

        /// <summary>当前局的关卡（空间事实）。无会话时为 null。</summary>
        public static ILevel Level { get; private set; }

        /// <summary>
        /// 旗杆上那面旗（由建它的 <c>LevelProps.Build</c> 登记）。马里奥滑杆时带着它一起降。
        /// <para>
        /// 为什么要在这里登记：原先 <c>PlayerActor</c> 用 <c>GameObject.Find("Flag")</c> 按名字找 ——
        /// 名字一改（或对象只在关卡的异步加载回调里建出来）就静默失效（历史债 E-5）。
        /// 改成"谁建谁登记、谁用谁读"，名字不再是契约。
        /// </para>
        /// </summary>
        public static UnityEngine.Transform Flag { get; private set; }

        /// <summary>登记旗子（由 <c>LevelProps</c> 在旗子建好后调用）。</summary>
        public static void SetFlag(UnityEngine.Transform flag) => Flag = flag;

        /// <summary>关卡名（HUD 上显示的 "WORLD 1-1"）。</summary>
        public static string WorldLabel { get; private set; } = "1-1";

        /// <summary>
        /// 本局要读的关卡数据路径（<c>Levels/World1-1</c> 这种，相对 Resources）。
        /// <para>
        /// <b>由流程层在进关前设置</b>（<c>AppFlow.SetLevel</c>），<c>StageSession</c> 只是消费者。
        /// 原先这条路径是写死在 StageSession 里的（只认 Level11），加第二关就没法走通了。
        /// </para>
        /// </summary>
        public static string LevelPath { get; private set; } = "Levels/World1-1";

        /// <summary>
        /// 本局是不是地下关（1-2 是）。只影响配色：地下关背景纯黑。
        /// <para>由流程层在进关前设置 —— 关卡"什么样子"是关卡自带的事实，不该散在各模块里各判一次。</para>
        /// </summary>
        public static bool Underground { get; private set; }

        /// <summary>
        /// 本局是不是**管中密室**（1-1 金币房）。
        /// <para>
        /// 与 <see cref="Underground"/> 分开：密室也是"黑底青砖"的地下配色，但它**不是一关** ——
        /// 没有旗杆 / 城堡（旗杆判定按关卡长度推，密室宽度只有 17 格，推出来的旗杆位置会让
        /// 马里奥一进房就触发通关），砖墙是房间结构**不许顶碎**，玩家落点也不由"关卡最左 + 3.5"决定。
        /// 这三条都由本标记关掉。
        /// </para>
        /// </summary>
        public static bool SubArea { get; private set; }

        /// <summary>
        /// 地形的青砖要不要换成"可顶碎的砖块实体"。
        /// <para>
        /// 只有 1-2 主关卡要（那里的玩法就是打穿天花板进隐藏区，见 <c>StageSession.SpawnUndergroundBricks</c>）；
        /// 金币房的青砖是房间的墙，顶碎了人就能走出房间外。
        /// </para>
        /// </summary>
        public static bool BrickTilesAsEntities => Underground && !SubArea;

        /// <summary>
        /// 玩家出生点覆盖（世界坐标 = 脚底）。
        /// <para>
        /// 默认出生点是"关卡最左 + 3.5 格、站在地面顶"（<c>StageSession.SpawnPlayer</c>），
        /// 那是主关卡（水平铺开、从左边进）的规则。管中密室的落点由元素表给定
        /// （§3.2「玩家落点 clone (-6,9)」），且正下方就是金币平台 —— 用默认规则会正好生在平台里。
        /// 用 null 表示"按默认规则"。
        /// </para>
        /// </summary>
        public static UnityEngine.Vector2? SpawnOverride { get; private set; }

        /// <summary>设置本局关卡（路径 + HUD 上的名字 + 是不是地下关 + 是不是管中密室）。由流程层在进关前调用。</summary>
        public static void SetLevel(string levelPath, string worldLabel, bool underground = false, bool subArea = false)
        {
            LevelPath = levelPath;
            WorldLabel = worldLabel;
            Underground = underground;
            SubArea = subArea;
        }

        /// <summary>设置出生点覆盖（进管中密室前调）；传 null 恢复默认规则。</summary>
        public static void SetSpawnOverride(UnityEngine.Vector2? feetPos) => SpawnOverride = feetPos;

        /// <summary>
        /// 下一局玩家出生时的**形态**（null = 小马里奥）。
        /// <para>
        /// 原版**同一只马里奥贯穿整局**：1-1 通关进 1-2、1-2 地下段走进侧向管出到地表段、
        /// 以及进出管中密室，形态（大 / 火）都保留；**只有死亡**才打回小马里奥。
        /// 本工程每一"段"都是一个独立的 <c>StageSession</c>（新的 <c>PlayerModule</c> ⇒ 默认 Small），
        /// 所以"跨段保留"必须显式把形态交接过去 —— 这就是这个字段的用途
        /// （原先没有它，实测症状是"大马里奥进金币房出来变小马里奥"，登记项 E-13）。
        /// </para>
        /// <para>
        /// 由流程层在进关前设：换段（<c>AppFlow.NextLevel</c>）与进密室（<c>AppFlow.BeginBonusRoom</c>）
        /// 传上一段的形态；新开一局（<c>AppFlow.OnCharChosen</c>）与死亡重来 / 换手
        /// （<c>AppFlow.ReloadStageWithIntro</c>）清成 null。
        /// </para>
        /// <para>
        /// ⛔ <b>不要放在 <c>AppFlow.EnterLoading</c> 里清</b>：那是换段的必经之路，而且跑在
        /// <c>NextLevel</c> 设值**之后**（<c>Fsm.Transition</c> 是同步的）⇒ 会把要带过去的形态擦掉。
        /// 实测症状：1-1 吃到蘑菇变大 → 通关 → 进 1-2 是 <c>Small</c>（2026-09-19，本片修掉）。
        /// </para>
        /// </summary>
        public static PowerState? SpawnPower { get; private set; }

        /// <summary>设置下一局的出生形态（<see cref="SpawnPower"/>）；传 null = 小马里奥。</summary>
        public static void SetSpawnPower(PowerState? power) => SpawnPower = power;

        public static void Bind(IScore score, IPlayer player, ILevel level, string worldLabel)
        {
            Score = score;
            Player = player;
            Level = level;
            WorldLabel = worldLabel;
        }

        public static void Unbind()
        {
            Score = null;
            Player = null;
            Level = null;
            // 旗子随关卡一起销毁：留着悬空引用会让下一局的滑杆去动一个已销毁的 Transform。
            Flag = null;
        }
    }
}
