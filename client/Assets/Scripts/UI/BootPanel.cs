using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace SuperMario.UI
{
    /// <summary>
    /// 启动画面：黑底 + 大标题 + 版权行，停 1.8 秒。
    /// <para>照抄原版"开机第一屏"的信息层级：标题最大、版权最小、没有别的元素。</para>
    /// <para>
    /// <b>本文件只有一个类</b>（每个面板一个文件）：一个 .cs 里放多个 MonoBehaviour 时，
    /// Unity 只会给其中一个分配 <c>fileID: 11500000</c>，其余类型的引用 ID 是另一套值。
    /// 于是"预制体里的 m_Script"就很容易写成空引用，运行时表现为
    /// <c>Component X not found on prefab</c>、面板永远打不开 —— 而且预制体文件看起来是"有组件的"。
    /// </para>
    /// </summary>
    public sealed class BootPanel : UIPanel
    {
        /// <summary>始终盖在最上层，别的面板不该盖住它。</summary>
        public override UILayer Layer => UILayer.System;

        private void Awake()
        {
            var bg = UIBuilder.Panel(transform, "BG", UIBuilder.Black);
            bg.raycastTarget = false;

            UIBuilder.Label(transform, "Title", "SUPER MARIO BROS.",
                64, TextAnchor.MiddleCenter, new Vector2(0f, 60f), new Vector2(1600f, 120f), Color.white);

            UIBuilder.Label(transform, "Copy", "© 1985 NINTENDO    FAN REMAKE FOR STUDY",
                16, TextAnchor.MiddleCenter, new Vector2(0f, -160f), new Vector2(1400f, 40f),
                new Color(0.6f, 0.6f, 0.6f));

            // `by clover-engine` —— 居底居中、字号小、颜色低调，不抢画面。
            //
            // 面板的"半高"是 CanvasScaler 按当前画面比例算出来的，不是恒定的 540 ——
            // 实测 1096x500 的 Game 视图下画布半高只有约 486，y=-500 直接被推出屏幕外，
            // 截图里根本看不到这一行（第一次就是这么漏掉的）。
            // ⇒ 贴底这一段现在由引擎的 `UIFactory.CreateCreditLabel` 负责（它把底部锚点钉死），
            //   本项目只经 `UIBuilder.CreditLabel` 给颜色与字体；三段式（本面板 / 标题屏）共用那一个入口。
            //
            // 是 `clover-engine`（全大写只允许用于常量/环境变量），大写 + 空格的形式不合规，
            // 且留着会出现两个引擎自称。
            //
            // 字体必须真有小写字形（**不是**本项目的 NES 像素字体）：那份像素字体里 a-z 与 A-Z
            // 是**同一套字形** ⇒ 源码文本明明是 `by clover-engine`，画面上却是 `BY CLOVER-ENGINE`
            // `UIBuilder.CreditLabel` 统一取（出处见 `Core.ResPaths.CreditFont`）。
            UIBuilder.CreditLabel(transform, Color.white);
        }
    }
}
