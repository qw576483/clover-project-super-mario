using CloverEngine;
using SuperMario.Core;
using SuperMario.Module.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace SuperMario.UI
{
    /// <summary>
    /// 进关读条屏（原版的 "WORLD 1-1 + 小马里奥 × 命数"）。
    /// <para>它同时承担两个职责：遮挡加载过程、以及交代"这是哪一关、还剩几条命"。</para>
    /// </summary>
    public sealed class LoadingPanel : UIPanel
    {
        public override UILayer Layer => UILayer.System;

        /// <summary>命数文本 —— 真正的值由 <see cref="OnOpen"/> 的入参决定。</summary>
        private Text _lives;

        /// <summary>入场卡上那一行 `WORLD &lt;n&gt;`。每次 <see cref="OnOpen"/> 都按当前关卡刷一遍。</summary>
        private Text _world;

        private void Awake()
        {
            UIBuilder.Panel(transform, "BG", UIBuilder.Black).raycastTarget = false;

            UIBuilder.Label(transform, "World", "WORLD " + StageContext.WorldLabel,
                32, TextAnchor.MiddleCenter, new Vector2(0f, 120f), new Vector2(900f, 60f), Color.white);

            var icon = UIBuilder.Node(transform, "Icon", new Vector2(0.5f, 0.5f),
                new Vector2(-60f, 0f), new Vector2(48f, 48f));
            var img = icon.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            Game.Res.LoadAsset<Sprite>(ResPaths.Mario(MarioAction.SmallIdle), sp =>
            {
                if (sp != null && img != null) { img.sprite = sp; img.preserveAspect = true; }
            });

            UIBuilder.Label(transform, "X", "×", 32, TextAnchor.MiddleCenter,
                new Vector2(20f, 0f), new Vector2(60f, 60f), Color.white);

            // 先按默认命数建出来，真正的值在 OnOpen 里刷（见那边的注释：这里读不到正确的命数）。
            _lives = UIBuilder.Label(transform, "Lives", GameConst.StartLives.ToString(), 32,
                TextAnchor.MiddleLeft, new Vector2(90f, 0f), new Vector2(120f, 60f), Color.white);
        }

        /// <summary>
        /// 入参 = 本关剩余命数（由 <c>AppFlow.EnterLoading</c> 传入）。
        /// <para>
        /// 踩过的坑：原来在 <see cref="Awake"/> 里读 <c>StageContext.Score.Lives</c>。
        /// 但"打开面板"发生在"新会话 Build 完并 Restore 分数"【之前】—— 那一刻 StageContext
        /// 还没绑定，于是回退到 <c>StartLives</c>：<b>死亡重来时这张卡永远显示 ×3，
        /// 而不是真实的剩余命数</b>。首次进关完全看不出问题（新游戏本来就是 3 条命），所以藏得很深。
        /// 命数是"调用方本来就知道的事实"，直接当参数传进来最可靠。
        /// </para>
        /// </summary>
        public override void OnOpen(object param)
        {
            var lives = param as int?
                        ?? (StageContext.Score != null ? StageContext.Score.Lives : GameConst.StartLives);
            if (_lives != null) _lives.text = lives.ToString();

            // ★ 关卡名必须**每次都刷**：这张卡是复用的同一个面板实例，而 `Awake` 只在第一次构建时跑过 ——
            //   用户报的 bug 就是"1-1 通关切到 1-2，黑屏上还写着 WORLD 1-1"（那一行原本是写死的字面量）。
            if (_world != null) _world.text = "WORLD " + StageContext.WorldLabel;

            // 没拿到参数 = 调用方漏传，必须留痕（否则又会退化成"显示 ×3 但没人知道为什么"）。
            if (!(param is int))
                Game.Logger.Warn("UI", $"读条屏没收到命数参数（param={(param == null ? "null" : param.ToString())}），" +
                                       $"回退到 {lives}");
        }
    }
}
