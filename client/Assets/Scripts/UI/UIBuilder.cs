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
        /// 建**引擎署名那一行**（`by clover-engine`）。
        /// <para>
        /// 为什么单独一个入口：这一行的字体与全项目别处**不一样**，而且不能出错 ——
        /// 全局 skill §1.6 的判据是"渲染出来的字**逐字**对"（含大小写），
        /// 而项目统一的 NES 像素字体只有大写字形（见 <see cref="Core.ResPaths.CreditFont"/> 的说明），
        /// 用它就会渲染成 `BY CLOVER-ENGINE` ⇒ 那是本片要修掉的缺陷。
        /// </para>
        /// <para>
        /// 取字体的顺序（三级，**任何一级都不会用回像素字体**）：
        /// ① 引擎资源缓存里已有小写字体（同步取，正常情况）；
        /// ② 没有 ⇒ 立刻异步装一次，**先用引擎内置字体顶着**（内置字体有小写字形，
        ///    渲染出来仍是小写，只是字形不是像素风；这一帧的观感差异可忽略）；
        /// ③ 装配失败 ⇒ 打 Error（§7：非预期分支必须留日志），gate
        ///    （`tools/verify.ps1` 的 `engine-credit`）会因为字体名对不上而**变红**，
        ///    不会静默退化成"看起来还行"。
        /// </para>
        /// </summary>
        public static Text CreditLabel(Transform parent, string name, string content, int size,
                                       TextAnchor anchor, Vector2 anchorPos, Vector2 boxSize, Color color)
        {
            var t = Label(parent, name, content, size, anchor, anchorPos, boxSize, color);
            ApplyCreditFont(t);
            return t;
        }

        private static Font _creditFont;
        private static bool _creditFontWarned;
        private static bool _creditLoadRequested;
        /// <summary>最近建出来的那行署名。异步装配回来时要把它也换过来（面板是"开一次建一次"）。</summary>
        private static Text _lastCreditLabel;

        /// <summary>把署名行的字体装到 <paramref name="t"/> 上（见 <see cref="CreditLabel"/> 的三级顺序）。</summary>
        private static void ApplyCreditFont(Text t)
        {
            if (t == null) return;
            _lastCreditLabel = t;

            var cached = _creditFont ?? Game.Res?.TryGet<Font>(Core.ResPaths.CreditFont);
            if (cached != null)
            {
                _creditFont = cached;
                t.font = cached;
                return;
            }

            // ② 未驻留：异步装一次（只发一次请求；接不到就用内置字体顶着）。
            if (!_creditLoadRequested && Game.Res != null)
            {
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
            else if (!_creditFontWarned)
            {
                _creditFontWarned = true;
                Game.Logger.Warn("UI", $"署名字体未驻留：{Core.ResPaths.CreditFont}，本次先用引擎内置字体（小写）");
            }

            t.font = UIFactory.DefaultFont();
        }

        /// <summary>署名文案。**单一来源**：代码里别处不许再写这一串（§1.6 要求逐字）。</summary>
        public const string CreditText = "by clover-engine";

        // ───────── 下面这三个纯机械函数【委托引擎】 ─────────
        //
        // 引擎的 UIFactory（`CloverEngine.UIFactory`）本来就提供同一套构件（E2 起对业务公开），
        // 本项目不再自己维护"锚点 / 铺满"的计算 —— 那类重复实现正是踩坑高发区：
        // 本文件原先自己写 anchorMin/anchorMax，结果面板根节点只有 100x100 时看不出问题、
        // 一旦铺满屏幕就让左对齐文案飞出屏幕（详见 Label 的注释）。
        // 引擎的 UIFactory.Stretch 天生就是"铺满父节点"，用它能直接绕开那类坑。

        /// <summary>建一个铺满父节点的容器（默认用于面板根）。委托 <see cref="UIFactory.CreateNode"/>。</summary>
        public static RectTransform Stretch(Transform parent, string name)
            => UIFactory.CreateNode(name, parent);

        /// <summary>
        /// 建一个锚定在某个点上的容器（坐标相对锚点）。
        /// <para>锚点取<b>父节点中心</b>时委托 <see cref="UIFactory.CreateCentered"/>；
        /// 只有要贴父节点边角（HUD 那种）时才自己设锚点。</para>
        /// </summary>
        public static RectTransform Node(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            if (anchor == new Vector2(0.5f, 0.5f))
                return UIFactory.CreateCentered(name, parent, size, pos);

            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>建一张纯色图（背景 / 分隔线 / 遮罩）。委托 <see cref="UIFactory.CreatePanel"/>。</summary>
        public static Image Panel(Transform parent, string name, Color color)
            // raycastTarget 传 true：保持与"新建 Image 的默认值"一致，不改变既有行为。
            => UIFactory.CreatePanel(name, parent, color, true);

        /// <summary>建一块纯色块（指定位置与大小）。</summary>
        public static Image Block(Transform parent, string name, Color color, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = Node(parent, name, anchor, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

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
            // ★ 锚点一律取【父节点中心】，不跟着 TextAnchor 走。
            //
            // 踩过的坑（症状很误导）：原先按对齐方式平移锚点 —— 左对齐锚父节点左边、
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
            var rt = Node(parent, name, new Vector2(0.5f, 0.5f), anchorPos, boxSize);
            var t = rt.gameObject.AddComponent<Text>();
            if (Font != null) t.font = Font;
            t.text = content;
            // 像素字体要整数倍：把字号对齐到 16 的倍数（不足 16 的按 16 处理）。
            t.fontSize = size < 16 ? 16 : (size / 16) * 16;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>建一个按钮（纯色底 + 居中文字）。</summary>
        public static Button TextButton(Transform parent, string name, string content, int size,
                                        Vector2 anchor, Vector2 pos, Vector2 size2, Color bg, Color fg,
                                        System.Action onClick)
        {
            var img = Block(parent, name, bg, anchor, pos, size2);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            // 悬停/按下的反馈用亮度差表现：像素风不适合做缩放或渐变动画。
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            btn.colors = colors;

            Label(img.transform, "Text", content, size, TextAnchor.MiddleCenter,
                Vector2.zero, size2, fg);

            if (onClick != null) btn.onClick.AddListener(() => onClick());
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
        /// ⚠️ 原来这里用的是 <see cref="SkyBlue"/>（＝关卡天空色）—— 那是**照另一份复刻工程的菜单图**
        /// 配的（自审表早就警告过"拿 clone 的菜单当基准会做成 clone 的菜单"）。2026-09-18 按原版实测色订正。
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
