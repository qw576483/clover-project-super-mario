using CloverEngine;
using SuperMario.Core;
using SuperMario.Module.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace SuperMario.UI
{
    /// <summary>
    /// 选人屏。原版是"1 PLAYER / 2 PLAYER"再确认一次，这里保持同样的两步流程
    /// （标题屏选一次、这里再确认一次），因为它给了玩家一个反悔的机会。
    /// </summary>
    public sealed class CharSelectPanel : UIPanel
    {
        private int _index;
        private Text[] _items;
        private readonly string[] _labels = { "1 PLAYER", "2 PLAYERS" };

        /// <summary>未选中项的前缀，宽度与 "▶ " 相同（光标是拼进文字里的，见 Refresh）。</summary>
        private const string CursorOff = "  ";

        private void Awake()
        {
            UIBuilder.Panel(transform, "BG", UIBuilder.SkyBlue).raycastTarget = false;

            UIBuilder.Label(transform, "Title", "SELECT PLAYERS",
                48, TextAnchor.MiddleCenter, new Vector2(0f, 180f), new Vector2(1400f, 100f), Color.white);

            // 光标作为文字前缀拼进选项文本（理由同 MainMenuPanel：独立光标的 x 很难对准）。
            _items = new Text[_labels.Length];
            for (var i = 0; i < _labels.Length; i++)
            {
                _items[i] = UIBuilder.Label(transform, $"Item{i}", _labels[i],
                    32, TextAnchor.MiddleLeft, new Vector2(-160f, 20f - i * 80f), new Vector2(600f, 60f), Color.white);
            }

            // 画一个马里奥头像当预览：直接用游戏里的精灵，保证和进关后是同一张脸。
            var icon = UIBuilder.Node(transform, "MarioIcon", new Vector2(0.5f, 0.5f),
                new Vector2(260f, 0f), new Vector2(96f, 96f));
            var img = icon.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            Game.Res.LoadAsset<Sprite>(ResPaths.Mario(MarioAction.SmallIdle), sp =>
            {
                if (sp != null && img != null) { img.sprite = sp; img.preserveAspect = true; }
            });

            UIBuilder.Label(transform, "Hint", "↑ ↓ 选择     ENTER / SPACE 开始     ESC 返回",
                16, TextAnchor.MiddleCenter, new Vector2(0f, -240f), new Vector2(1200f, 40f),
                new Color(1f, 1f, 1f, 0.85f));

            Refresh();
        }

        public override void OnUpdate(float dt)
        {
            if (Game.Input == null) return;

            var up = Game.Input.GetKeyDown(GameKey.UpArrow) || Game.Input.GetKeyDown(GameKey.W);
            var down = Game.Input.GetKeyDown(GameKey.DownArrow) || Game.Input.GetKeyDown(GameKey.S);
            if (up) { _index = (_index + _labels.Length - 1) % _labels.Length; Refresh(); }
            if (down) { _index = (_index + 1) % _labels.Length; Refresh(); }

            if (Game.Input.GetKeyDown(GameKey.Escape))
            {
                // 只发事件，由 AppFlow 决定去哪（面板之间不互相跳转）。
                // 「正在执行这段代码的自己」销毁掉，之后还继续调 UI/流程 API ——
                // 属于在已销毁组件上继续跑逻辑，行为不可预期。
                Game.Event.Emit(Events.BackToMain);
                return;
            }

            if (Game.Input.GetKeyDown(GameKey.Space) || Game.Input.GetKeyDown(GameKey.Enter))
            {
                Game.Event.Emit(Events.CharChosen, _index + 1);
            }
        }

        private void Refresh()
        {
            if (_items == null) return;
            for (var i = 0; i < _items.Length; i++)
            {
                if (_items[i] == null) continue;
                _items[i].text = (i == _index ? "▶ " : CursorOff) + _labels[i];
            }
        }
    }
}
