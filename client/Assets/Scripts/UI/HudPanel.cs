using CloverEngine;
using SuperMario.Core;
using SuperMario.Module.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace SuperMario.UI
{
    /// <summary>
    /// 游戏内 HUD。
    /// <para>
    /// 版式照抄原版上边栏：MARIO / 分数 在左，金币在中左，WORLD 1-1 在中间，TIME 在右。
    /// 数值订阅 <see cref="Events.HudDirty"/> 刷新 —— 不做逐帧轮询，
    /// 因为分数变化是离散事件，逐帧刷新只是白烧 CPU。
    /// </para>
    /// </summary>
    public sealed class HudPanel : UIPanel
    {
        private Text _score;
        private Text _coins;
        private Text _time;
        private Text _world;
        private bool _subscribed;

        private void Awake()
        {
            var bar = UIBuilder.Stretch(transform, "Bar");
            // 只占顶部一条，不铺满屏幕：铺满会挡住游戏画面，而且一个透明全屏 Image
            // 会吃掉所有点击（HUD 不需要交互，但挡到别人就是 bug）。
            var barRect = (RectTransform)bar;
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(0f, 130f);
            barRect.anchoredPosition = Vector2.zero;

            // 四栏的横向位置**照原版首屏逐像素量出来**（不是自己摆的）：
            //   原版基线 `策划/基线图/nes-original-1-1-first-screen.png`（256×240，16 px = 1 格）里，
            //   HUD 第二行（y=24..31）的墨迹列区间是 ——
            //     `000000`(分数) x=24..70 ｜ **金币图标 x=89..93（橙 252,152,56 + 黑描边，高 8）**
            //     ｜ `×00`(金币数) **x=97..118** ｜ `WORLD`/`1-1` x=144..182 ｜ `TIME`/`400` x=201..232
            //   ⇒ 归一化（÷256）后四栏的锚点分别是 **左 0.09375 / 左 0.34766(图标) + 0.37891(数字) /
            //     中 0.63672 / 右 0.91016**。
            //   用**归一化锚点**而不是像素偏移：本工程画面是 16:9、原版是 4:3，
            //   像素偏移只在 1920 宽下才等于这几个分数（换分辨率就错位）。
            //   量测：同一套量法也量我们自己的截图。
            //
            //   上面那两行区间（89..93 / 97..118）是同一天把基线图逐列扫墨迹扫出来的（脚本可复跑）。
            MakeLabel(bar, "MarioCap", "MARIO", 0.09375f, TextAnchor.MiddleLeft, new Vector2(0f, -28f));
            _score = MakeLabel(bar, "Score", "000000", 0.09375f, TextAnchor.MiddleLeft, new Vector2(0f, -66f));
            MakeCoinIcon(bar);
            _coins = MakeLabel(bar, "Coins", "×00", 97f / 256f, TextAnchor.MiddleLeft, new Vector2(0f, -66f));

            MakeLabel(bar, "WorldCap", "WORLD", 0.63672f, TextAnchor.MiddleCenter, new Vector2(0f, -28f));
            _world = MakeLabel(bar, "World", StageContext.WorldLabel, 0.63672f, TextAnchor.MiddleCenter, new Vector2(0f, -66f));

            MakeLabel(bar, "TimeCap", "TIME", 0.91016f, TextAnchor.MiddleRight, new Vector2(0f, -28f));
            _time = MakeLabel(bar, "Time", "400", 0.91016f, TextAnchor.MiddleRight, new Vector2(0f, -66f));

            _subscribed = true;
            Game.Event.On(Events.HudDirty, Refresh);
            Refresh();
        }

        /// <summary>
        /// 建 HUD 上的**金币图标**（原版 `×00` 左边那颗 8x8 金币）。
        /// <para>
        /// <b>位置</b>：原版基线图量出来的图标中心 —— 墨迹 x=89..93 ⇒ 中心 91 ⇒ 归一化 <c>91/256</c>；
        /// 纵向与 `Coins` 文字**同一中心**（文字框 top=−66、高 36 ⇒ 中心在栏顶下 84 像素）。
        /// </para>
        /// <para>
        /// <b>大小</b>：由"原版图标比字高 8/7"推出来（基线里图标 y=24..31 共 8 像素、数字 y=24..30 共 7 像素），
        /// 本工程 HUD 是 16px 像素字 × `localScale 1.6` = 25.6 像素高 ⇒ 图标 25.6 × 8/7 ≈ <b>29</b> 像素；
        /// 用 `preserveAspect` 保住原图那种"窄长金币"形。
        /// </para>
        /// <para>
        /// <b>贴图</b>：<c>ResPaths.Coin[3]</c>（`smb1_misc_sprites_81`，不透明盒 8x14 ⇒ 宽高比 0.57，
        /// 与基线那颗图标 5x8 = 0.625 最接近的一帧）；异步加载，拿不到就留空 —— HUD 不该被一张小图卡住。
        /// </para>
        /// </summary>
        private static void MakeCoinIcon(Transform parent)
        {
            var rt = UIBuilder.Node(parent, "CoinIcon", new Vector2(91f / 256f, 1f),
                new Vector2(0f, -84f), new Vector2(29f, 29f));
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            var path = ResPaths.Item(SpriteNames.Coin[3]);
            Game.Res.LoadAsset<Sprite>(path, s =>
            {
                if (s == null)
                {
                    Game.Logger.Warn("UI", $"HUD 金币图标贴图缺失：{path}");
                    return;
                }
                img.sprite = s;
            });
        }

        /// <summary>
        /// 在 bar 上按**归一化锚点**建一个 HUD 文字（锚点 = 该栏在原版首屏上的位置，
        /// 见 <see cref="Awake"/>；对齐方式与 pivot 由 <paramref name="align"/> 决定，
        /// 三者必须一致，否则文字会从错误的一侧排开）。
        /// </summary>
        private static Text MakeLabel(Transform parent, string name, string content, float anchorX,
                                      TextAnchor align, Vector2 pos)
        {
            var t = UIBuilder.Label(parent, name, content, 16, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.zero, Color.white);
            var rt = t.rectTransform;
            var anchor = new Vector2(anchorX, 1f);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            var pivotX = align == TextAnchor.MiddleLeft ? 0f : align == TextAnchor.MiddleRight ? 1f : 0.5f;
            rt.pivot = new Vector2(pivotX, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(320f, 36f);
            rt.localScale = Vector3.one * 1.6f;   // 16px 像素字放大一点更接近原版的视觉重量
            t.alignment = align;
            return t;
        }

        private void OnDestroy()
        {
            // 退出 / 编辑器停止播放时必须判空：销毁顺序是
            //   EngineRunner.OnApplicationQuit → Game.Shutdown()（把 Event 门面置为 null）
            //   → Unity 才销毁本面板 → 这里再解引用就是 NullReferenceException。
            //   此时事件总线已被 Shutdown 的 Event.OffAll() 清空，不解绑也不会残留订阅。
            //   （同族写法见 clover-project-diablo2 各面板的 Subscribe/Unsubscribe 判空。）
            if (_subscribed && Game.Event != null) Game.Event.Off(Events.HudDirty, Refresh);
        }

        private void Refresh()
        {
            var s = StageContext.Score;
            if (s == null) return;   // 关卡已释放：静默跳过，不要在销毁期抛异常
            if (_score != null) _score.text = s.Points.ToString("D6");
            if (_coins != null) _coins.text = "×" + s.Coins.ToString("D2");
            if (_time != null) _time.text = s.TimeLeft.ToString("D3");
            if (_world != null) _world.text = StageContext.WorldLabel;
        }

        /// <summary>时间剩 100 秒以内变红提醒（原版是变色 + hurry-up 音效）。</summary>
        public override void OnUpdate(float dt)
        {
            var s = StageContext.Score;
            if (s == null || _time == null) return;
            _time.color = s.TimeLeft <= 100 ? new Color(1f, 0.35f, 0.3f) : Color.white;
        }
    }
}
