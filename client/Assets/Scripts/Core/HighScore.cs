using CloverEngine;

namespace SuperMario.Core
{
    /// <summary>
    /// 最高分 —— 原版标题画面底部那行 <c>TOP- 000000</c>。
    /// <para>
    /// 走引擎的 <see cref="ISetting"/>（<c>Game.Setting</c>）：它是引擎统一的"跨退出保留"落点，
    /// 落盘成 JSON 文件。
    /// </para>
    /// <para>
    /// 换平台、换存储策略都不会跟着变，同一个工程里也多出第二套持久化路径。
    /// </para>
    /// <para>
    /// <b>注意</b>：切到 <c>Setting</c> 之后，旧 <c>PlayerPrefs</c> 里的记录会**丢一次**（换存储了）。
    /// 本项目处于开发期，最高分归零可接受；真要迁移就在 <see cref="Get"/> 里读一次旧键再写进来。
    /// </para>
    /// </summary>
    public static class HighScore
    {
        /// <summary>设置键名。集中在这里，避免字面量散落各处。</summary>
        private const string Key = "smb.topscore";

        /// <summary>当前最高分（没有记录时是 0）。</summary>
        public static int Get()
        {
            if (Game.Setting == null)
            {
                // Setting 由 Game.Launch 创建；正常流程不会走到这里。
                // 但不静默：否则表现是"最高分永远是 0"，查起来要命。
                Game.Logger?.Warn("Score", "Game.Setting 未就绪，最高分按 0 处理");
                return 0;
            }

            return Game.Setting.Get(Key, 0);
        }

        /// <summary>
        /// 提交一局分数；刷新了记录返回 true 并立刻落盘。
        /// <para>
        /// 只在真的刷新时才 <c>Save()</c> —— <c>Set</c> 只标脏，<c>Save</c> 是一次整文件写盘，
        /// 没必要每局都写。
        /// </para>
        /// </summary>
        public static bool TrySubmit(int points)
        {
            if (Game.Setting == null)
            {
                Game.Logger?.Warn("Score", "Game.Setting 未就绪，最高分未落盘");
                return false;
            }

            if (points <= Get()) return false;

            Game.Setting.Set(Key, points);
            Game.Setting.Save();
            return true;
        }
    }
}
