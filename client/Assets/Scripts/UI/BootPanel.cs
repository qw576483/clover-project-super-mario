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
    /// 一个类一个文件能让 11500000 恒成立。见 经验.md §4.3。
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

            // ★ 引擎署名（全局 skill §1.6 硬要求）：做出来的游戏首页下方必须有**一行**
            // `by clover-engine` —— 居底居中、字号小、颜色低调，不抢画面。
            //
            // ⚠️ 必须**锚到底边**，不能只写一个固定的负 y（这里原来写的就是 y=-500）：
            // 面板的"半高"是 CanvasScaler 按当前画面比例算出来的，不是恒定的 540 ——
            // 实测 1096x500 的 Game 视图下画布半高只有约 486，y=-500 直接被推出屏幕外，
            // 截图里根本看不到这一行（第一次就是这么漏掉的）。锚到 (0.5, 0) 后，
            // 无论什么比例它都稳定贴在底边之上 16 像素。
            //
            // 注：这里原先那行 "CLOVER ENGINE  .  UNITY" 已删 —— §1.6 规定引擎自称必须**逐字**
            // 是 `clover-engine`（全大写只允许用于常量/环境变量），大写 + 空格的形式不合规，
            // 且留着会出现两个引擎自称。
            //
            // ⚠️ 字体必须走 `UIBuilder.CreditLabel`（**不是** `Label`）：本工程统一的 NES 像素字体
            // 里 a-z 与 A-Z 是**同一套字形** ⇒ 源码文本明明是 `by clover-engine`，画面上却是
            // `BY CLOVER-ENGINE`（用户肉眼发现的缺陷）。§1.6 的判据是"渲染出来的字逐字对"
            // ⇒ 这一行必须用一份真有小写字形的字体（出处见 `Core.ResPaths.CreditFont`）。
            var sig = UIBuilder.CreditLabel(transform, "Signature", UIBuilder.CreditText,
                16, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(800f, 32f),
                Color.white);
            var sigRt = sig.rectTransform;
            sigRt.anchorMin = new Vector2(0.5f, 0f);
            sigRt.anchorMax = new Vector2(0.5f, 0f);
            sigRt.pivot = new Vector2(0.5f, 0f);
            sigRt.anchoredPosition = new Vector2(0f, 16f);
        }
    }
}
