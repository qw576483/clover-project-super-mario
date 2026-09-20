namespace SuperMario.Core
{
    /// <summary>
    /// Unity 关卡名常量。
    /// <para>
    /// 必须与 Build Settings 里的场景名逐字一致 —— 对不上时 <c>Game.Scene.Load</c> 会静默失败
    /// （只留一条错误日志，画面停在原地），所以不允许在别处写裸字符串。
    /// </para>
    /// </summary>
    public static class Scenes
    {
        /// <summary>启动画面（Logo + 版权字）。最轻，先加载。</summary>
        public const string Boot = "Boot";

        /// <summary>菜单场景：主菜单 / 选角 / 设置全在这一个场景里，靠面板切换，不切场景。</summary>
        public const string Menu = "Menu";

        /// <summary>游戏场景（World 1-1）。</summary>
        public const string Stage01 = "Stage01";
    }
}
