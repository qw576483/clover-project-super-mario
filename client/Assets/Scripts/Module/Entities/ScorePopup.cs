using CloverEngine;
using SuperMario.Core;
using UnityEngine;

namespace SuperMario.Module.Entities
{
    /// <summary>
    /// 吃到金币时从方块位置飘起来的那行分数（"200"）—— 原版"金币飞一下 **变成** 分数"的那一幕。
    /// <para>
    /// <b>出处（原版 clone，一条链）</b>：
    /// ① 触发点在 <c>Assets/Scripts/BlockCoin.cs:11</c> —— 方块弹出的那枚金币在 <c>Start()</c> 里就
    ///    <c>t_LevelManager.AddCoin (transform.position + Vector3.down)</c>；
    /// ② <c>Assets/Scripts/LevelManager.cs:535-557</c> —— 带位置的 <c>AddCoin(pos)</c> 走
    ///    <c>AddScore(coinBonus, pos)</c>，后者 <c>:555</c> 调 <c>CreateFloatingText(bonus.ToString(), pos)</c>
    ///    （值就是金币分 = 本工程的 <c>GameConst.ScoreCoin</c> = 200）；
    /// ③ 预制作 <c>Assets/Prefabs/_managers/Floating Text Effect.prefab</c> —— 一个 TextMesh
    ///    （白色、锚点 MiddleCenter、<c>m_CharacterSize: 0.4</c>），挂动画控制器
    ///    <c>Animations/Misc/Anim UI Floating Text Effect.controller</c>，播的剪辑是
    ///    <c>Animations/Misc/UI Floating Text.anim</c>：**0 秒 y=0 → 0.5 秒 y=+3（线性、不淡出）**，
    ///    剪辑长度 0.5 秒。
    /// </para>
    /// <para>
    /// <b>本类现在只是"一行飘字的生命周期句柄"</b>：画面由**引擎**的飘字层负责
    /// （<see cref="IUIManager.FloatText"/> ⇒ <c>Runtime/Presentation/UIWidgets.cs</c> 的 <c>FloatTextLayer</c>）。
    /// 本类不再自建 <c>WorldSpace</c> Canvas / Text —— 那是"每个项目都要重写一遍、且每行飘字一个新 Canvas"
    /// 的重复实现（引擎已有屏幕空间飘字层，含节点池与相机投影）。
    /// </para>
    /// <para>
    /// 为什么要留一个**空的** GameObject 作为句柄（而不是干脆不要这个类）：
    /// 调用方 <see cref="ItemModule"/> 的契约是"<c>Spawn</c> 回一个带 <c>Finished</c> / <c>Clear</c> 的对象、
    /// 由 <c>Reap</c> 回收"，取证脚本 <c>tools/probes/probe.cs</c> 的 <c>ScorePopupCount()</c> /
    /// <c>ScorePopupReadout()</c> 也按"场上活着的 <c>ScorePopup</c> 组件"读读数（E-19 那条链）。
    /// 这个可观测事实保留下来；画面一个字都不由它画。
    /// </para>
    /// <para>
    /// 数字不重写：上移距离与用时直接取上面那个剪辑（3 格 / 0.5 秒），生命周期 = 剪辑长度。
    /// 不做淡出（剪辑里没有 alpha 曲线）。
    /// </para>
    /// </summary>
    internal sealed class ScorePopup : MonoBehaviour
    {
        /// <summary>上移距离（格）。出处 = clone `UI Floating Text.anim` 的末帧 `y: 3`。</summary>
        private const float RiseTiles = 3f;

        /// <summary>存活时长（秒）。出处 = 同一份剪辑的 `m_StopTime: 0.5`。</summary>
        private const float Life = 0.5f;

        /// <summary>
        /// 上移距离换算成**世界单位**（引擎的 <c>riseWorld</c> 一律是世界单位，由相机投影算屏幕位移）。
        /// 依据：本项目 1 格 = <c>GameConst.TileSize</c> = **1** 世界单位
        /// （`spritePixelsPerUnit = 16`、一个瓦片边长 = 1 世界单位，见项目级 `conventions.md` §像素基准
        /// 与 `GameConst.TileSize` 的注释）⇒ 3 格 = 3 世界单位。
        /// 不要在这里写常量 3：换算必须跟着 `GameConst.TileSize` 走，否则改世界尺度时这里会静默错。
        /// </summary>
        private static readonly float RiseWorld = RiseTiles * GameConst.TileSize;

        private float _t;

        /// <summary>已经播完（由 <see cref="ItemModule.Reap"/> 回收）。</summary>
        public bool Finished { get; private set; }

        /// <summary>在 <paramref name="worldPos"/> 处生成一行分数文字（<paramref name="text"/> 由调用方给，本类不改数）。</summary>
        public static ScorePopup Spawn(Transform root, string text, Vector2 worldPos)
        {
            var world = new Vector3(worldPos.x, worldPos.y, 0f);

            // 画面交给引擎：**0.5 秒 / 直线上升 3 格 / 不淡出**（出处见类注释里的那份剪辑）。
            //   duration 必须显式传 0.5 —— 引擎默认 1.2s；
            //   fade 必须显式传 false —— 引擎默认 true（alpha = 1 - t² 淡出），而原版剪辑里没有 alpha 曲线；
            //   riseWorld 用世界单位（不传的话引擎沿用旧的"屏幕升距 70 画布单位"，与关卡尺度对不上）。
            if (Game.UI != null)
            {
                Game.UI.FloatText(world, text, Color.white, duration: Life, riseWorld: RiseWorld, fade: false);
            }
            else
            {
                // 非预期分支（引擎没起就进了关卡）：静默会让"该飘的字没飘"看起来像飘字坏了，必须留痕。
                Game.Logger.Warn("Item", $"Game.UI 未就绪，飘字未显示：{text}");
            }

            // 句柄节点（**不画任何东西**，见类注释"为什么要留空 GameObject"）。
            var go = new GameObject("ScorePopup");
            go.transform.SetParent(root, false);
            // 位置 = 这条飘字对应的世界坐标：引擎按同一个世界坐标投影出落点，
            // 这里存一份只为"这条飘字出现在哪"可被读取（探针按 transform.position 报读数），不参与渲染。
            go.transform.position = world;
            return go.AddComponent<ScorePopup>();
        }

        private void Update()
        {
            if (Finished) return;

            // 与引擎飘字层用**同一把时钟**：那层用 `Time.unscaledDeltaTime` 推进（暂停 / 结算屏
            // timeScale = 0 时仍要走完，否则飘字永不消失），这里跟着它走，否则暂停时会出现
            // "字已经没了、句柄还活着"（<see cref="ItemModule.Reap"/> 就会把它一直留在表里）。
            _t += Time.unscaledDeltaTime;
            if (_t >= Life) Finished = true;
        }

        /// <summary>回收句柄节点（画面由引擎自己回收，这里没有第二个节点要清）。</summary>
        public void Clear()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
