using CloverEngine;
using SuperMario.Core;
using SuperMario.Module.Flow;
using UnityEngine;

namespace SuperMario.UI
{
    /// <summary>通关结算：COURSE CLEAR + 本局分数与金币。</summary>
    public sealed class ResultPanel : UIPanel
    {
        public override UILayer Layer => UILayer.Popup;

        private void Awake()
        {
            // 几乎全黑的底。原版通关后就是一屏黑的分数结算，不是"半透明盖在城堡上"——
            // 实测 0.6 太透，SCORE / COINS / 提示文字正好压在城堡的亮色砖上，糊得看不清。
            UIBuilder.Panel(transform, "Mask", new Color(0f, 0f, 0f, 0.95f));

            UIBuilder.Label(transform, "Title", "COURSE CLEAR!",
                48, TextAnchor.MiddleCenter, new Vector2(0f, 140f), new Vector2(1400f, 90f), UIBuilder.CoinGold);

            // 数值直接读当次会话的快照：结算面板是在关卡还活着时打开的，
            // 如果读晚一步（关卡已 Dispose）就会显示 0，所以这里当场取。
            var s = StageContext.Score;
            var score = s != null ? s.Points.ToString("D6") : "000000";
            var coins = s != null ? s.Coins : 0;

            // 顺手提交最高分 —— 标题屏底部的 "TOP- 000000" 读的就是它。
            // 放在这里（而不是标题屏）：标题屏只负责显示，写入点是"一局结束"。
            if (s != null && HighScore.TrySubmit(s.Points))
                Game.Logger.Info("Score", $"刷新最高分：{s.Points}");

            UIBuilder.Label(transform, "ScoreCap", "SCORE", 16, TextAnchor.MiddleCenter,
                new Vector2(-160f, 20f), new Vector2(300f, 40f), Color.white);
            UIBuilder.Label(transform, "Score", score, 16, TextAnchor.MiddleCenter,
                new Vector2(-160f, -30f), new Vector2(300f, 40f), Color.white);

            UIBuilder.Label(transform, "CoinCap", "COINS", 16, TextAnchor.MiddleCenter,
                new Vector2(160f, 20f), new Vector2(300f, 40f), Color.white);
            UIBuilder.Label(transform, "Coins", "×" + coins.ToString("D2"), 16, TextAnchor.MiddleCenter,
                new Vector2(160f, -30f), new Vector2(300f, 40f), Color.white);

            UIBuilder.Label(transform, "Hint", "按 SPACE / ENTER 继续",
                16, TextAnchor.MiddleCenter, new Vector2(0f, -160f), new Vector2(1000f, 40f),
                new Color(1f, 1f, 1f, 0.85f));
        }
    }
}
