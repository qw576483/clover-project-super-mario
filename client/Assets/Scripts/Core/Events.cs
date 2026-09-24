namespace SuperMario.Core
{
    /// <summary>
    /// 流程事件名常量 —— **唯一来源**。
    /// <para>
    /// 业务里禁止写裸字符串（<c>Game.Event.Emit("Flow.Start")</c> 这种）。
    /// 理由：改名时编译器帮不上忙，只能全局搜字符串，漏一处就是"点了没反应"。
    /// </para>
    /// 事件名的命名法是 <c>域.动作</c>，域用来分主语（Flow / Game / Hud）。
    /// </summary>
    public static class Events
    {
        // ---- 主菜单 → 流程 ----
        public const string StartNewGame = "Flow.StartNewGame";
        public const string ContinueGame = "Flow.ContinueGame";
        public const string OpenSettings = "Flow.OpenSettings";
        public const string QuitGame = "Flow.QuitGame";

        // ---- 选角 → 流程 ----
        // 参数是【玩家数】（1 或 2），不是"第几个角色" —— 流程里按
        // 正是"标题屏和选人屏重复选一遍玩家数"这个困惑的来源。
        public const string CharChosen = "Flow.CharChosen";     // 参数：int playerCount

        // ---- 游戏内 → 流程 ----
        public const string PauseOpened = "Flow.PauseOpened";
        public const string Resume = "Flow.Resume";
        public const string BackToMain = "Flow.BackToMain";
        public const string RestartLevel = "Flow.RestartLevel";

        // ---- 关卡 → 流程 ----
        public const string LevelCleared = "Flow.LevelCleared";   // 通关（旗杆流程走完）
        public const string PlayerDied = "Flow.PlayerDied";       // 死一次（还有命）
        public const string GameOver = "Flow.GameOver";           // 没命了

        // ---- 玩法 → HUD ----
        public const string HudDirty = "Hud.Dirty";               // 分数/金币/命数/时间 变了
        public const string TimeChanged = "Hud.TimeChanged";       // 参数：int 剩余秒

        // ---- 时间 ----
        public const string TimeUp = "Flow.TimeUp";               // 时间归零（等同死亡）
        public const string HurryUp = "Flow.HurryUp";             // 剩 100 秒

        // ---- 表现 ----
        public const string ScreenShake = "Fx.ScreenShake";        // 参数：float 强度
        public const string Toast = "Fx.Toast";                   // 参数：string 文案
    }
}
