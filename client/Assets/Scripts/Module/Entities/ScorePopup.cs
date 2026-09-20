using SuperMario.UI;
using UnityEngine;
using UnityEngine.UI;

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
    /// ③ 预制体 <c>Assets/Prefabs/_managers/Floating Text Effect.prefab</c> —— 一个 TextMesh
    ///    （白色、锚点 MiddleCenter、<c>m_CharacterSize: 0.4</c>），挂动画控制器
    ///    <c>Animations/Misc/Anim UI Floating Text Effect.controller</c>，播的剪辑是
    ///    <c>Animations/Misc/UI Floating Text.anim</c>：**0 秒 y=0 → 0.5 秒 y=+3（线性、不淡出）**，
    ///    剪辑长度 0.5 秒；<c>FloatingTextEffect.cs</c> 只是把它画到 "Behind Enemy" 层。
    /// </para>
    /// <para>
    /// <b>本工程怎么落地</b>：用**世界坐标下的 uGUI Text**（<c>Canvas.renderMode = WorldSpace</c>），
    /// 字体与 HUD 同一份像素字。为什么不用 TextMesh：工程里所有文字都是 uGUI（HUD / 面板 / 署名），
    /// 再引一条 TextMesh 渲染路径就要多维护一套（字体材质、排序层、缩放口径都不一样），
    /// 而"金币分"这行字只有一处用处 —— 复用现有的那条路更省、也不会与 HUD 的像素口径漂移。
    /// </para>
    /// <para>
    /// ⛔ 数字不重写：上移距离与用时直接取上面那个剪辑（3 格 / 0.5 秒），生命周期 = 剪辑长度。
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
        /// 世界坐标里 1 格 = 多少**画布像素**。
        /// <para>
        /// 取 32 而不是 HUD 那种 16：字号 16 的字在画布上占 16 像素（见 <c>UIBuilder.Label</c> 的说明，
        /// 也是验收表 #5 量到的"16px 像素字"），而原版这行浮字是**半格高**（NES 上 8 像素 ÷ 16 像素一格）
        /// ⇒ 32 画布像素 = 1 格，16 像素字正好 0.5 格。
        /// </para>
        /// </summary>
        private const float PixelsPerTile = 32f;

        /// <summary>画在最上面（玩家 10、实体 5~8）—— 这行字是"结算提示"，不该被任何东西挡住。</summary>
        private const int SortingOrder = 12;

        private Vector3 _origin;
        private float _t;

        /// <summary>已经播完（由 <see cref="ItemModule.Reap"/> 回收）。</summary>
        public bool Finished { get; private set; }

        /// <summary>在 <paramref name="worldPos"/> 处生成一行分数文字（<paramref name="text"/> 由调用方给，本类不改数）。</summary>
        public static ScorePopup Spawn(Transform root, string text, Vector2 worldPos)
        {
            var go = new GameObject("ScorePopup");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = SortingOrder;

            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PixelsPerTile * 4f, PixelsPerTile);
            // 画布像素 → 世界格：整块缩放 1/32（见 PixelsPerTile 的说明）。
            rt.localScale = Vector3.one / PixelsPerTile;

            var textRt = UIBuilder.Node(rt, "Text", new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(PixelsPerTile * 4f, PixelsPerTile));
            var t = textRt.gameObject.AddComponent<Text>();
            if (UIBuilder.Font != null) t.font = UIBuilder.Font;
            t.text = text;
            t.fontSize = 16;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;

            var popup = go.AddComponent<ScorePopup>();
            popup._origin = go.transform.position;
            return popup;
        }

        private void Update()
        {
            if (Finished) return;

            _t += Time.deltaTime;
            var k = Mathf.Clamp01(_t / Life);
            transform.position = _origin + new Vector3(0f, RiseTiles * k, 0f);
            if (_t >= Life) Finished = true;
        }

        public void Clear()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
