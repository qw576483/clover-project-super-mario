using CloverEngine;
using UnityEngine;

namespace SuperMario.UI
{
    /// <summary>GameOver 画面。停 4 秒后由流程切回标题。</summary>
    public sealed class GameOverPanel : UIPanel
    {
        public override UILayer Layer => UILayer.System;

        private void Awake()
        {
            UIBuilder.Panel(transform, "BG", UIBuilder.Black).raycastTarget = false;

            UIBuilder.Label(transform, "Title", "GAME OVER",
                64, TextAnchor.MiddleCenter, new Vector2(0f, 40f), new Vector2(1400f, 120f),
                new Color(0.9f, 0.25f, 0.2f));

            UIBuilder.Label(transform, "Hint", "即将返回标题画面…",
                16, TextAnchor.MiddleCenter, new Vector2(0f, -120f), new Vector2(1000f, 40f),
                new Color(0.7f, 0.7f, 0.7f));
        }
    }
}
