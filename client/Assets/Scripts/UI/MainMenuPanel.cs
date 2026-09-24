using CloverEngine;
using SuperMario.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SuperMario.UI
{
    /// <summary>
    /// 标题画面。
    /// <para>
    /// 与原版的刻意差异（用户明确要求）：<b>不再有 <c>1 PLAYER GAME</c> / <c>2 PLAYER GAME</c> 选项</b>，
    /// 也**没有"选择"这一步** —— <b>按空格 / 回车直接进入游戏</b>。
    /// </para>
    /// <para>
    /// 因为"选择"没了，原版那两行选项空出来的位置必须给一条**操作提示** ——
    /// 否则标题屏上只有 logo / 版权 / <c>TOP-</c>，玩家看不出该按什么键就能开始
    /// （实测就是这么被指出来的）。提示用英文，和 HUD 上的 <c>WORLD</c> / <c>TIME</c> / <c>TOP-</c> 一致。
    /// </para>
    /// <para>
    /// 仍然**不处理方向键**：没有可选项，按了不该有任何变化。
    /// </para>
    /// </summary>
    public sealed class MainMenuPanel : UIPanel
    {
        /// <summary>操作提示文案（英文，与 HUD 文案风格一致）。</summary>
        private const string Hint = "PRESS SPACE OR ENTER TO START";

        /// <summary>
        /// 本面板是否还活着（<see cref="OnDestroy"/> 置 false）。
        /// <para>
        /// 只服务于一件事：<b>异步加载回调的守卫</b>（见 <c>Awake</c> 里那个 <c>LoadAsset</c> 的注释，
        /// 登记项 E-27）。为什么不能只判 Unity 的"伪 null"：伪 null 依赖对象被销毁的时机，
        /// 而"这个面板还在不在"是**我们自己的事实** —— 两个一起判，回调就不可能去碰一个已经没了的 Image。
        /// </para>
        /// </summary>
        private bool _alive = true;

        private void OnDestroy() => _alive = false;

        public override void OnOpen(object param)
        {
            if (Game.Sound != null) Game.Sound.PlayBGM(Bgm.Overworld);
        }

        private void Awake()
        {
            UIBuilder.Panel(transform, "BG", UIBuilder.TitleBg).raycastTarget = false;

            // ── 原版 logo 图形 ──
            // 出处：**原版标题屏截图**本身（`原版资源/导出的png/nes-original-title-screen.png`，256x224），
            // 用 `python 原版资源/解析/脚本/title_logo_crop.py` 从原版画面裁出 logo 块（207x112）覆盖
            // Resources/Sprites/Title/TitleLogo.png。
            // ⚠️ 原来那份是从**另一份复刻工程的标题图表**裁的：量色显示橙/粉都不是原版那一组
            //   （clone 橙 200,76,12 / 粉 252,188,176；原版橙 153,78,0 / 粉 255,204,197）。
            // 尺寸按原始像素比例 207:112 给，别让 Image 去拉伸变形（preserveAspect 也兜着）。
            var logoGo = UIBuilder.Node(transform, "Logo", new Vector2(0.5f, 0.5f),
                new Vector2(0f, 190f), new Vector2(880f, 476f));
            var logo = logoGo.gameObject.AddComponent<Image>();
            logo.raycastTarget = false;
            logo.preserveAspect = true;   // 万一把尺寸写错，也不会把 logo 压扁
            Game.Res.LoadAsset<Sprite>(ResPaths.Title(SpriteNames.TitleLogo), s =>
            {
                // ★ 守卫（登记项 E-27）：这个回调是**异步**的，而面板可能在它回来之前就被销毁了。
                //
                // 实机那条路径（`client/Logs/2026-09-19.log` 17:39:11.665，本片 18:31:02.702 复现）：
                //   标题屏刚打开（logo 首次加载、未命中缓存）→ 玩家立刻按空格 ⇒
                //   `Events.CharChosen` → `AppFlow.OnCharChosen` → `Game.UI.CloseAll()` 销毁本面板
                //   + `Scene.Load(Stage01)`；约 0.42 秒后回调才回来，此时 `logo` 已经是"已销毁的 Image"
                //   ⇒ `logo.sprite = s` 抛 MissingReferenceException，被引擎的回调包装（`ResourceManager.cs`
                //   的 `CompletePending`）吞成一条
                //     `[Error] [Resource] 加载回调异常（Sprites/Title/TitleLogo）`。
                //   —— 它不是"资源坏了"，是"回调晚到"，属**非预期分支**，所以这里既不能继续用，
                //   也不能默默吞掉：丢弃 + 留一条 Warn（§7）。
                //
                // 判据两样都判：① `_alive`（`OnDestroy` 置的标记：面板已销毁 / 已被切走）；
                //   ② `logo == null`（Unity 伪 null：Image 真的没了，兜住"标记还没置上"的时序）。
                if (!_alive || logo == null)
                {
                    Game.Logger.Warn("UI",
                        "标题 logo 加载回调晚到：面板已销毁/已切走，丢弃这次回调（登记项 E-27）");
                    return;
                }
                if (s == null)
                {
                    Game.Logger.Error("UI", $"标题 logo 未加载：{ResPaths.Title(SpriteNames.TitleLogo)}");
                    return;
                }
                logo.sprite = s;
            });

            // ── 版权行：⛔ **不要**在这里再画一行 `©1985 NINTENDO` ──
            //
            // 2026-09-19（任务书-修取证缺陷）实测：上面那块 `TitleLogo.png`（207x112，由
            // `原版资源/解析/脚本/title_logo_crop.py` 从**原版标题屏**裁出）的取景框正好覆盖
            // 原版那三条带 —— HUD 行 / 底板 / **版权行** —— 块最下面那 6 行就是原版的
            // `©1985 NINTENDO`，而且**相对尺寸就是原版的**（原版 版权行宽/底板宽 = 119/192 = 0.62；
            // 块里同比值，实测两处逐像素同字形）。
            //
            // 这里原先又画了一行 ⇒ 屏上出现**两处** ©（块里的粉色一处 ＋ 这行的白色一处，
            // 白的那行还多了个空格、字号也不对），与基线图 `策划/基线图/nes-original-title-screen.png`
            // （该屏只有一处 ©）不符 —— 判据脚本 `.ai-tmp/test/title_copyright_check.py`（逐带定位 +
            // 放大并排），删掉这一行即恢复 1:1。
            //
            // ⚠️ 副作用（写在明面上）：版权行现在**跟着 logo 块走**。若将来重裁那块图，
            //    必须把它一起裁进去，或者在这里把这一行重新补上。

            // ── 操作提示（原版那两行选项所在的位置）──
            //
            // 字号传 32：UIBuilder.Label 会把字号强制对齐到 16 的倍数
            // （`(size / 16) * 16`），所以传 24 实际是 16 —— 这里要的是和原版
            // `1 PLAYER GAME` 同级的字号（32），别照抄 TOP- 那行的 24。
            UIBuilder.Label(transform, "Hint", Hint,
                32, TextAnchor.MiddleCenter, new Vector2(0f, -200f), new Vector2(1200f, 60f), Color.white);

            // ── 最高分（原版：TOP- 000000）──
            UIBuilder.Label(transform, "TopCap", $"TOP- {HighScore.Get():D6}",
                24, TextAnchor.MiddleCenter, new Vector2(0f, -340f), new Vector2(600f, 40f), Color.white);

            // ── 引擎署名（全局 skill §1.6 硬要求：**首页**下方必须有一行 `by clover-engine`）──
            //
            // ⚠️ 这一行原先**只在启动画面有**，标题屏没有 —— 用户肉眼发现的那处缺陷
            // （"首页面没有 skill 要求的 by clover engine 好像"）。它必须在**玩家看到的那一屏**上。
            //
            // 版式与启动画面那行同一套（同字号阶 16 / 同色阶 0.45 / 同底距 16px）：
            // 静态子节点，**不跟随任何动画、不挂在任何"只有某个按钮出现时才可见"的容器里** ——
            // 标题屏一画出来它就在。
            //
            // ⚠️ 必须**锚到底边**，不能写固定的负 y：画布半高是 CanvasScaler 按当前画面比例算的，
            // 不是恒定 540（启动画面那处就是这么漏掉的，见 `BootPanel.cs` 的注释）。
            // ⇒ 贴底这一步现在由引擎的 `UIFactory.CreateCreditLabel` 负责（底部锚点钉死），
            //   三段式（本面板 / 启动画面）共用 `UIBuilder.CreditLabel` 这**一个**入口。
            //
            // 字体必须真有小写字形（不是本项目的 NES 像素字体）—— 用像素字体会渲染成 `BY CLOVER-ENGINE`。
            // 颜色 = **白色**（用户 2026-09-19 明说：「首页面 by clover engine 要变成白色的。不要黑色的」）。
            // ⛔ 别再用 0.45 的深灰：标题屏底色是浅蓝紫，深灰在它上面看起来就是"黑的"（用户原话）。
            UIBuilder.CreditLabel(transform, Color.white);
        }

        public override void OnUpdate(float dt)
        {
            if (Game.Input == null) return;

            // 标题屏没有任何可选项 ⇒ 只认这两个键，按下即进游戏。
            var confirm = Game.Input.GetKeyDown(GameKey.Space) || Game.Input.GetKeyDown(GameKey.Enter);
            if (!confirm) return;

            // 一步进游戏：把"玩家数"发出去即可（本项目单机单人，恒为 1）。
            //
            // 踩过的坑（记下来免得以后又改回去）：这里原先发的是 StartNewGame，
            // 它转到 CharSelect 状态 —— 于是"菜单里选 1P/2P"和"选人屏里再选一次玩家数"
            // 成了同一件事做两遍，而 `Events.CharChosen` 的 int 参数携带的本来就是【玩家数】
            // （原先注释错写成 playerIndex，已在 Events.cs 订正）。
            Game.Event.Emit<int>(Events.CharChosen, 1);
        }
    }
}
