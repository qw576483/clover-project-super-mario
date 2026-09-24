using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace SuperMario.UI
{
    /// <summary>
    /// 代码搭 uGUI 的助手。
    /// <para>
    /// 面板内容全部**用代码生成**，预制体只是一个挂载点（见 ProjectBuilder 生成的那批空预制体）。
    /// 取舍理由：这个项目的 UI 是"复刻 NES 画面"——大量纯色块、等宽像素字、精确对齐，
    /// 用编辑器手拖反而更难对齐；代码里写坐标能用常量约束（比如 HUD 一律 8 的倍数），
    /// 而且改版式不用打开 Unity。
    /// </para>
    /// </summary>
    internal static class UIBuilder
    {
        private static Font _font;
        private static bool _fontWarned;

        /// <summary>
        /// 取 NES 像素字体。
        /// <para>
        /// 走引擎资源缓存（<see cref="IResourceManager.TryGet{T}"/>）：由 <c>AppFlow.EnterBoot</c>
        /// 在启动期 <c>Preload</c> 进来，这里只做**同步取** ——
        /// 不再绕开资源模块去 <c>Resources.Load</c>（那是"业务自己找存储"，换资源后端就失效）。
        /// </para>
        /// <para>
        /// 取不到时回落引擎内置字体，并且**不缓存这个回落结果**：预热有可能比第一个面板晚一步，
        /// 一旦把回落值缓存下来，"暂时没准备好"就被固化成"永远用默认字体"了。
        /// </para>
        /// </summary>
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;

                _font = Game.Res?.TryGet<Font>(Core.ResPaths.PixelFont);
                if (_font != null) return _font;

                if (!_fontWarned)
                {
                    _fontWarned = true;
                    Game.Logger.Warn("UI",
                        $"像素字体未就绪：{Core.ResPaths.PixelFont}（应由启动期 Preload 装入），暂时用引擎内置字体");
                }
                return UIFactory.DefaultFont();
            }
        }

        /// <summary>
        /// 建**引擎署名那一行**（`by clover-engine`）—— 全项目**唯一**入口。
        /// <para>
        /// **建件收敛到引擎** <see cref="UIFactory.CreateCreditLabel"/>：它把"贴父节点底部居中"
        /// 钉死为底部锚点（左上角锚点 + 大负 y 放底部元素会随 CanvasScaler 掉出屏幕外，实测过），
        /// 三段式（含字号 / 底距 / 文案默认值）都在那里。
        /// </para>
        /// <para>
        /// 节点名恒为 <c>Signature</c>：闸门 `engine-credit`（`tools/verify.ps1`）按这个名字在
        /// MainMenuPanel / BootPanel 上读**运行时**的实际文本与字体，改名即判据失效。
        /// </para>
        /// <para>
        /// 本文件只负责**取字体**这一段项目特有的逻辑（三级，**任何一级都不会用回像素字体**）：
        /// ① 引擎资源缓存里已有小写字体（同步取，正常情况）；
        /// ② 没有 ⇒ 立刻异步装一次，**先用引擎内置字体顶着**（内置字体有小写字形，
        ///    渲染出来仍是小写，只是字形不是像素风；这一帧的观感差异可忽略），并在此留一条 Warn；
        ///    会因为字体名对不上而**变红**，不会静默退化成"看起来还行"。
        /// </para>
        /// <para>
        /// 为什么不能用 <see cref="Font"/>（本项目像素字体）：那份 NES 像素字体里 a-z 与 A-Z
        /// （出处见 <see cref="Core.ResPaths.CreditFont"/>）。
        /// </para>
        /// </summary>
        public static Text CreditLabel(Transform parent, Color color)
        {
            // ① 同步取（正常情况启动期已 Preload）。
            var font = _creditFont ?? Game.Res?.TryGet<Font>(Core.ResPaths.CreditFont);
            if (font != null) _creditFont = font;
            // ② 未驻留：异步装一次；本次先传 null（引擎用内置字体并**限频 Warn 一次**，见 CreateCreditLabel）。
            else RequestCreditFont();

            var t = UIFactory.CreateCreditLabel(parent, font, CreditFontSize, CreditBottomOffset, CreditText);
            t.name = "Signature";
            t.color = color;
            // 面板是"打开一次建一次"，异步装回来的字体要换到**当前这次**已经建好的那行上。
            _lastCreditLabel = t;
            return t;
        }

        /// <summary>署名行字号。出处 = 原启动画面 / 标题屏两处各自写的 16。</summary>
        private const int CreditFontSize = 16;

        private const float CreditBottomOffset = 16f;

        private static Font _creditFont;
        private static bool _creditFontWarned;
        private static bool _creditLoadRequested;
        /// <summary>最近建出来的那行署名。异步装配回来时要把它也换过来（面板是"开一次建一次"）。</summary>
        private static Text _lastCreditLabel;

        /// <summary>
        /// 异步装一次署名字体（只发一次请求）。装回来后把**当前那行**也换过来。
        /// 未装上时先用引擎内置字体顶着（有小写字形 ⇒ 画面上仍是小写，只是字形不是像素风）。
        /// </summary>
        private static void RequestCreditFont()
        {
            if (_creditLoadRequested || Game.Res == null)
            {
                if (!_creditFontWarned)
                {
                    _creditFontWarned = true;
                    Game.Logger.Warn("UI", $"署名字体未驻留：{Core.ResPaths.CreditFont}，本次先用引擎内置字体（小写）");
                }
                return;
            }

            _creditLoadRequested = true;
            Game.Res.LoadAsset<Font>(Core.ResPaths.CreditFont, f =>
            {
                if (f == null)
                {
                    Game.Logger.Error("UI",
                        $"署名字体未加载：{Core.ResPaths.CreditFont} —— 那行字会掉回引擎内置字体（仍是小写，但字形不是像素风）");
                    return;
                }
                _creditFont = f;
                // 面板是"打开一次建一次"，所以要把**当前这次**已经建好的那行也换过来。
                if (_lastCreditLabel != null) _lastCreditLabel.font = f;
            });
        }

        public const string CreditText = "by clover-engine";

        // ───────── 本文件保留的"薄包装"是什么、为什么还留着 ─────────
        //
        // 建件（节点 / 纯色块 / 文字 / 按钮）**全部**转发引擎 `CloverEngine.UIFactory`
        // （`Runtime/Presentation/UIWidgets.cs` + `UIWidgetControls.cs`），本项目不再自己写
        //
        // 只留三样**项目内容**在这里，引擎按设计**不含**它们（见 UIWidgetControls.cs 文件头
        // 「没有下沉：配色 / 文案 / 字号档位都是业务取值」）：
        //   ① NES 像素字体（`Font` 属性：预热 + 同步取 + 回落 + 留痕）；
        //   ② 字号吸附到 16 的整数倍（`SnapFontSize`）；
        //   ③ 像素色板（SkyBlue / TitleBg / Brick / CoinGold / Black —— 全是"复刻 NES 画面"的实测色）。

        /// <summary>建一个铺满父节点的容器（默认用于面板根）。委托 <see cref="UIFactory.CreateNode"/>。</summary>
        public static RectTransform Stretch(Transform parent, string name)
            => UIFactory.CreateNode(name, parent);

        /// <summary>
        /// 建一个锚定在某个点上的容器（坐标相对锚点）。
        /// <para>两条路都走引擎：锚点取<b>父节点中心</b>时用 <see cref="UIFactory.CreateCentered"/>，
        /// 其余用 <see cref="UIFactory.CreateNode"/> + <see cref="UIFactory.Place"/>。</para>
        /// </summary>
        public static RectTransform Node(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            if (anchor == new Vector2(0.5f, 0.5f))
                return UIFactory.CreateCentered(name, parent, size, pos);

            var rt = UIFactory.CreateNode(name, parent);
            UIFactory.Place(rt, anchor, new Vector2(0.5f, 0.5f), pos, size);
            return rt;
        }

        /// <summary>建一张纯色图（背景 / 分隔线 / 遮罩）。委托 <see cref="UIFactory.CreatePanel"/>。</summary>
        public static Image Panel(Transform parent, string name, Color color)
            // raycastTarget 传 true：保持与"新建 Image 的默认值"一致，不改变既有行为。
            => UIFactory.CreatePanel(name, parent, color, true);

        /// <summary>
        /// 建一块纯色块（指定位置与大小）。底板由引擎建（<see cref="UIFactory.CreatePanel"/>）、
        /// 定位由引擎算（<see cref="UIFactory.Place"/>）—— 本项目只决定"什么颜色、放在哪"。
        /// </summary>
        public static Image Block(Transform parent, string name, Color color, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var img = UIFactory.CreatePanel(name, parent, color, true);
            UIFactory.Place(img.rectTransform, anchor, new Vector2(0.5f, 0.5f), pos, size);
            return img;
        }

        /// <summary>
        /// 像素字号吸附：像素字体只有在 **16 的整数倍**放大时才不糊（见 <see cref="Label"/> 的说明）。
        /// 不足 16 按 16 处理，其余向下取整到 16 的倍数。
        /// <para>**这是本文件刻意保留的薄包装之一**（引擎不掌握本项目的字体档位，见文件顶部说明）。</para>
        /// </summary>
        private static int SnapFontSize(int size) => size < 16 ? 16 : (size / 16) * 16;

        /// <summary>
        /// 建一段文字。
        /// <para>
        /// 关键参数是 <paramref name="scale"/> 与 <paramref name="size"/>：
        /// NES 像素字体只有在**整数倍放大**且 <c>FontSize=16</c> 的整数倍时才不糊，
        /// 所以这里强制把字号向上取到 16 的倍数，并关掉抗锯齿式的 resize。
        /// </para>
        /// </summary>
        public static Text Label(Transform parent, string name, string content, int size,
                                 TextAnchor anchor, Vector2 anchorPos, Vector2 boxSize, Color color)
        {
            // 锚点一律取【父节点中心】，不跟着 TextAnchor 走。
            //
            // 右对齐锚右边。面板根节点只有 100x100 时看不出问题；一旦根节点铺满屏幕
            // （那才是它本该有的样子），左对齐文案的锚点就变成了【屏幕左边缘】，
            // 坐标为负的选项（如 -180）被整段推出屏幕外 —— 菜单三行选项全部消失，
            // 而同屏的标题（居中）和光标都在，非常容易被误判成"那几个控件没做"。
            // 受影响的坐标一律是"以为在屏幕中央附近"的那些，所以四个面板同时中招
            // （标题菜单 / 选人 / 暂停的音量标签 / 读条的命数）。
            //
            // 锚在中心还有个本质好处：坐标从此是"相对屏幕中心"，与父节点尺寸解耦 ——
            // 面板铺满、换分辨率、改布局都不会让整块版式跑掉。
            // 文字在框内怎么摆由 TextAnchor 决定（MiddleLeft 就是"从框左边开始写"），
            // 不需要再动锚点。HudPanel 那种要贴屏幕角落的，自己在建完之后覆写 anchor。
            // 文字节点由**引擎**建（`UIFactory.CreateText` 是引擎里唯一建 uGUI Text 的地方，
            // 文字渲染挂钩 TextHooks 不会被绕过），本项目在这之上覆写三处"像素口径"：
            //   ① 字体：换成 NES 像素字体（引擎默认是内置字体）；
            //   ② 字号：吸附到 16 的整数倍（见 SnapFontSize）；
            //   ③ 不换行（引擎默认 Wrap；本项目文案都是单行、且靠溢出显示）。
            var t = UIFactory.CreateText(name, parent, content, SnapFontSize(size), anchor, color);
            if (Font != null) t.font = Font;
            t.fontSize = SnapFontSize(size);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.supportRichText = true;
            UIFactory.Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchorPos, boxSize);
            return t;
        }

        /// <summary>
        /// 建一个按钮（纯色底 + 居中文字）。
        /// <para>底板 / Button 组件 / 居中文字一次由引擎建好（<see cref="UIFactory.CreateButton"/>），
        /// 本项目只覆盖三处以回到既有口径：定位锚点、悬停/按下的亮度差反馈、文字换像素字体与字号吸附。</para>
        /// </summary>
        public static Button TextButton(Transform parent, string name, string content, int size,
                                        Vector2 anchor, Vector2 pos, Vector2 size2, Color bg, Color fg,
                                        System.Action onClick)
        {
            var img = UIFactory.CreateButton(name, parent, content, size2, Vector2.zero, bg, onClick);
            UIFactory.Place(img.rectTransform, anchor, new Vector2(0.5f, 0.5f), pos, size2);

            var btn = img.GetComponent<Button>();
            // 悬停/按下的反馈用亮度差表现：像素风不适合做缩放或渐变动画。
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            btn.colors = colors;

            // 引擎建的文字（内置字体 / 26 号 / 浅色）必须换成本项目口径：像素字体 + 字号吸附。
            var label = img.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.gameObject.name = "Text";
                if (Font != null) label.font = Font;
                label.fontSize = SnapFontSize(size);
                label.color = fg;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.supportRichText = true;
            }
            else
            {
                // 非预期分支（引擎改了 CreateButton 的层级就会走到）：按钮能点但没字，必须留痕。
                Game.Logger.Warn("UI", $"TextButton({name}) 没有取到文字节点，按钮文字不会显示");
            }

            return btn;
        }

        /// <summary>SMB 的天空蓝（所有外景画面的底色）。</summary>
        public static readonly Color SkyBlue = new Color(0.36f, 0.58f, 0.99f);

        /// <summary>标题屏底色 = 原版 NES 标题屏的**实测主色**（占屏 55%）。</summary>
        /// <para>
        /// 出处：原版标题屏截图 `原版资源/导出的png/nes-original-title-screen.png`（256x224）的主色统计
        /// `RGB(146,144,255)`；复现命令 `python 原版资源/解析/脚本/title_bg_compare.py`（该脚本同时会
        /// 报"本工程 vs 原版"的每通道最大差）。
        /// </para>
        /// <para>
        /// </para>
        public static readonly Color TitleBg = new Color(146f / 255f, 144f / 255f, 1f);

        /// <summary>地面砖的橙棕色（UI 强调色，和游戏里看到的砖一致）。</summary>
        public static readonly Color Brick = new Color(0.80f, 0.36f, 0.11f);

        /// <summary>问号块的金黄色。</summary>
        public static readonly Color CoinGold = new Color(0.98f, 0.75f, 0.18f);

        /// <summary>纯黑（NES 背景）。</summary>
        public static readonly Color Black = new Color(0f, 0f, 0f, 1f);
    }
}
