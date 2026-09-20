using CloverEngine;
using SuperMario.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SuperMario.UI
{
    /// <summary>
    /// 暂停面板：盖一层半透明黑 + PAUSE + 三个操作 + 两个音量滑杆。
    /// <para>层用 <see cref="UILayer.Popup"/>：它会自动把下层压暗并吃掉点击，避免"暂停了还能点 HUD"。</para>
    /// </summary>
    public sealed class PausePanel : UIPanel
    {
        public override UILayer Layer => UILayer.Popup;

        private void Awake()
        {
            UIBuilder.Panel(transform, "Mask", new Color(0f, 0f, 0f, 0.55f));

            UIBuilder.Label(transform, "Title", "PAUSE",
                48, TextAnchor.MiddleCenter, new Vector2(0f, 200f), new Vector2(900f, 90f), Color.white);

            UIBuilder.TextButton(transform, "Resume", "继续 (ESC)", 16,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(420f, 64f),
                UIBuilder.Brick, Color.white, () => Game.Event.Emit(Events.Resume));

            UIBuilder.TextButton(transform, "Restart", "重开本关", 16,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(420f, 64f),
                UIBuilder.Brick, Color.white, () => Game.Event.Emit(Events.RestartLevel));

            UIBuilder.TextButton(transform, "Back", "回主菜单", 16,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(420f, 64f),
                UIBuilder.Brick, Color.white, () =>
                {
                    Game.UI.Confirm("回主菜单", "当前进度不会保存。确定要回去吗？",
                        () => Game.Event.Emit(Events.BackToMain));
                });

            // 音量滑杆：设置项就这两个分组，放这里省掉一个独立设置面板。
            BuildVolumeSlider(transform, "音乐", SoundGroup.BGM, -190f);
            BuildVolumeSlider(transform, "音效", SoundGroup.SFX, -250f);
        }

        private static void BuildVolumeSlider(Transform parent, string label, SoundGroup group, float y)
        {
            var sliderGo = UIBuilder.Node(parent, $"Vol_{group}", new Vector2(0.5f, 0.5f),
                new Vector2(0f, y), new Vector2(420f, 30f));
            var slider = sliderGo.gameObject.AddComponent<Slider>();

            var bg = UIBuilder.Block(sliderGo, "BG", new Color(0.25f, 0.25f, 0.25f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 12f));

            // ★ Fill Area 这一层是 **uGUI Slider 的硬性结构要求**，不能省。
            //
            // 原理（`UnityEngine.UI.Slider.UpdateVisuals`）：滑块是按
            // `fillRect.anchorMax[axis] = normalizedValue` 来表现"填充了多少"的；
            // 而 anchorMax 是相对 **fillRect 的父节点**（Slider 里缓存成 `m_FillContainerRect`）解析的。
            // 原先 `Fill` 直接挂在 sliderGo（420x30 的整块）下、锚点又写成 (0.5,0.5)、sizeDelta=420x12
            // ⇒ ① 没有"和轨道同尺寸的容器"可供按比例收缩；② 锚点定死成 (0.5,0.5) 后
            // anchorMax.x 被写进去也只是把它当"锚在中心"，宽度完全不变。
            // 症状就是"两条音量条永远满格"（`pause.png` 可见），而代码零报错。
            var area = UIBuilder.Node(sliderGo, "Fill Area", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(420f, 12f));
            var fill = UIBuilder.Block(area, "Fill", UIBuilder.CoinGold,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 12f));
            var fillRt = fill.rectTransform;
            fillRt.anchorMin = Vector2.zero;              // 与 Fill Area 同尺寸起步
            fillRt.anchorMax = Vector2.one;               // Slide 每帧会改成 (value, 1)
            fillRt.sizeDelta = Vector2.zero;              // 宽度完全由锚点决定

            slider.targetGraphic = bg;
            slider.fillRect = fillRt;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = Game.Sound != null ? Game.Sound.GetVolume(group) : 1f;
            slider.onValueChanged.AddListener(v => Game.Sound?.SetVolume(group, v));

            // 标签放在滑杆【左侧之外】。
            // 踩过的坑：原先给 -260，但 Label 是"锚在父节点中心"的右对齐文案
            // （右边缘 = 锚点 x + 半宽 = -260+80 = -180），而滑杆左边缘在 -210
            // —— 于是"音乐 / 音效"两个字压在金色填充条上，糊成一片看不清。
            // 挪到 -320 后右边缘落在 -240，与滑杆左边缘留出 30 的间隙。
            UIBuilder.Label(parent, $"VolLabel_{group}", label, 16, TextAnchor.MiddleRight,
                new Vector2(-320f, y), new Vector2(160f, 40f), Color.white);
        }
    }
}
