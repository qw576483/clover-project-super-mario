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

        /// <summary>
        /// </summary>
        private static readonly Vector2 VolBarSize = new Vector2(420f, 12f);

        private static readonly Color VolTrackColor = new Color(0.25f, 0.25f, 0.25f);

        /// <summary>手柄宽度（画布单位）= 轨道高度（12 ⇒ 方块手柄，像素风）。</summary>
        private const float VolHandleWidth = 12f;

        private static void BuildVolumeSlider(Transform parent, string label, SoundGroup group, float y)
        {
            // 控件本身调引擎 `UIFactory.CreateSlider`（`Runtime/Presentation/UIWidgetControls.cs`）。
            // 本项目只保留两样**项目内容**：
            //   ① 像素色板（轨道深灰 / 已填金色）与手柄宽度 —— 引擎刻意不含任何项目配色
            //      （见该文件头「没有下沉：配色 / 文案 / 字号档位都是业务取值」）；
            //   ② "放在哪"—— 引擎的 CreateSlider 是**左上角锚点**定位（`CreateBoxRect`），
            //      本项目所有控件都是"相对父层中心偏移"的口径，故建好后把整块的锚点/轴心改回中心
            //      （只动这一个 rect，不动引擎内部搭好的 Fill / Handle 层级）。
            //
            // 不再自己搭"Fill Area / Fill"：uGUI Slider 的填充靠 `fillRect.anchorMax[axis]` 按比例
            //    变化，而 anchorMax 是相对 fillRect 的**父节点**（Slider 缓存的 m_FillContainerRect）解析的
            //
            // 节点名必须是 `Vol_{group}` 且是**本面板的直接子节点**：取证脚本
            //    `tools/probes/probe.cs` 的 `SetVol` / `VolReport` 按 `panel.transform.Find("Vol_BGM")`
            //    取它身上的 `Slider`（E-19「数值 ↔ 画面互相印证」）。改名或挪层 = 那条判据失效。
            var handleColors = ColorBlock.defaultColorBlock;
            handleColors.normalColor = Color.white;
            handleColors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            handleColors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            handleColors.selectedColor = Color.white;
            handleColors.colorMultiplier = 1f;
            handleColors.fadeDuration = 0.1f;

            var slider = UIFactory.CreateSlider(
                $"Vol_{group}", parent, Vector2.zero, VolBarSize,
                0f, 1f,
                Game.Sound != null ? Game.Sound.GetVolume(group) : 1f,
                v => Game.Sound?.SetVolume(group, v),
                new WidgetSliderStyle
                {
                    TrackColor = VolTrackColor,
                    FillColor = UIBuilder.CoinGold,
                    HandleColor = UIBuilder.CoinGold,
                    HandleColors = handleColors,
                    HandleWidth = VolHandleWidth,
                });

            UIFactory.Place((RectTransform)slider.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, y), VolBarSize);

            // 标签放在滑杆【左侧之外】。
            // （右边缘 = 锚点 x + 半宽 = -260+80 = -180），而滑杆左边缘在 -210
            // —— 于是"音乐 / 音效"两个字压在金色填充条上，糊成一片看不清。
            // 挪到 -320 后右边缘落在 -240，与滑杆左边缘留出 30 的间隙。
            UIBuilder.Label(parent, $"VolLabel_{group}", label, 16, TextAnchor.MiddleRight,
                new Vector2(-320f, y), new Vector2(160f, 40f), Color.white);
        }
    }
}
