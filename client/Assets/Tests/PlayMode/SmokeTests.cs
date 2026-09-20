using System.Collections;
using System.IO;
using CloverEngine;
using NUnit.Framework;
using SuperMario.Core;
using SuperMario.Module.Flow;
using SuperMario.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SuperMario.Tests
{
    /// <summary>
    /// 端到端冒烟测试：真的把游戏从启动画面走到 1-1 关卡，并断言几条硬事实。
    /// <para>
    /// <b>为什么需要它</b>：编译通过只证明类型对得上。而这一类项目最容易出的错恰恰在运行期 ——
    /// 关卡文件没被读到（一片空场景）、马里奥出生点悬空（重力没生效）、方块没登记进实心位图
    /// （穿模）。这三条都不是编译器能发现的，但都是"打开就发现游戏不能玩"级别的问题。
    /// </para>
    /// <para>
    /// 顺路把关键画面截图到 <c>_smb_work/shots</c>：交付标准是"像原版 1-1"，
    /// 而像不像只能看画面（见 clover-engine skill 的模态自审要求）。
    /// </para>
    /// </summary>
    public sealed class SmokeTests
    {
        private const string ShotDir = "_smb_work/shots";

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Boot_Into_World11_And_PlayerStandsOnGround()
        {
            if (!Directory.Exists(ShotDir)) Directory.CreateDirectory(ShotDir);

            // ── 1. 启动：从 Boot 场景走真实流程 ──
            SceneManager.LoadScene(Scenes.Boot);
            yield return null;

            yield return WaitFor(() => Game.UI != null && Game.UI.IsOpen<MainMenuPanel>(),
                20f, "主菜单未出现（Bootstrap 没跑起来，或 Resources/UI/MainMenuPanel 预制体缺失）");
            yield return Shot("02-menu");

            // ── 2. 选 1 人开局 ──
            Game.Event.Emit(Events.CharChosen, 1);

            yield return WaitFor(() => StageContext.Player != null && StageContext.Score != null,
                90f, "关卡会话未就绪（World1-1.txt 读不到、读条卡住，或 803 个瓦片构建失败）");
            yield return Shot("03-loading");

            // 让相机与实体稳定下来，并给马里奥时间落地。
            yield return new WaitForSeconds(1.5f);
            yield return Shot("04-stage-start");

            // ── 3. 硬事实断言 ──
            var level = StageContext.Level;
            var player = StageContext.Player;
            var score = StageContext.Score;

            Assert.IsNotNull(level, "关卡未绑定到 StageContext");
            Assert.IsNotNull(player, "玩家未绑定到 StageContext");

            // 关卡范围：原版 1-1 从 x=-13 到 x=209（含左侧预留的延伸地面）。
            Assert.Greater(level.MaxWorldX, 200f, "关卡右边界过小，World1-1.txt 可能只读到了一部分");
            Assert.Less(level.MinWorldX, 0f, "关卡左边界不对");

            // 地面：地面顶部下方必须有一格实心，否则马里奥会直接掉下去。
            var groundTileY = Mathf.FloorToInt(level.GroundTopY) - 1;
            Assert.IsTrue(level.IsSolidTile(5, groundTileY),
                $"起点下方 ({5},{groundTileY}) 不是实心 —— 地面没建出来");
            var spawnTileX = Mathf.FloorToInt(player.Transform.position.x);
            Assert.IsTrue(level.IsSolidTile(spawnTileX, groundTileY),
                $"出生点 ({spawnTileX},{groundTileY}) 下方没有地面");

            // 马里奥必须已经站在地面上：脚底 y ≈ 地面顶。差得多说明重力/碰撞没生效。
            Assert.IsTrue(player.Grounded, "马里奥没有落地（重力或逐轴碰撞没生效）");
            Assert.AreEqual(level.GroundTopY, player.Transform.position.y, 0.05f,
                "马里奥脚底与地面顶不重合（出生点算错了，或碰撞把人物推到了别处）");

            // 玩家没掉出关卡、也没被立刻判定死亡。
            Assert.IsTrue(player.Alive, "马里奥开局就死了");

            // 初始数值：3 条命、400 秒。
            Assert.AreEqual(GameConst.StartLives, score.Lives, "初始命数不对");
            Assert.AreEqual(GameConst.LevelTime, score.TimeLeft, "初始时间不对");
            Assert.AreEqual(0, score.Points, "开局分数应该为 0");

            yield return new WaitForSeconds(1.5f);
            yield return Shot("05-stage-stable");

            // ── 4. 再跑一会儿，确认没有异常刷屏 ──
            // 这一段是"沉默的验证"：如果构建/每帧逻辑抛异常，Unity 的日志断言会在这里失败。
            yield return new WaitForSeconds(2f);

            Assert.IsTrue(player.Alive, "运行两秒后马里奥死了（可能被卡进地形或掉坑）");
            Assert.AreEqual(level.GroundTopY, player.Transform.position.y, 0.05f,
                "运行两秒后马里奥离开了地面（没输入的情况下他不应该动）");

            Debug.Log($"[Smoke] 通过：关卡范围 x[{level.MinWorldX},{level.MaxWorldX}]、" +
                      $"地面顶 {level.GroundTopY}、玩家 {player.Transform.position}、" +
                      $"命 {score.Lives}、时间 {score.TimeLeft}");
        }

        // ───────────────────────── 工具 ─────────────────────────

        private static IEnumerator WaitFor(System.Func<bool> condition, float timeoutSeconds, string failMessage)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail(failMessage);
                yield return null;
            }
        }

        private static IEnumerator Shot(string name)
        {
            // 用 ReadPixels 而不是 ScreenCapture.CaptureScreenshot：
            // 后者属于 UnityEngine.ScreenCaptureModule，本工程没引入那个模块；
            // 而 ReadPixels / EncodeToPNG 分别在 Core 与 ImageConversion（已引入）里。
            // 另外 ReadPixels 必须在帧末调用，否则会抓到上一次的缓冲。
            yield return new WaitForEndOfFrame();

            var w = Screen.width;
            var h = Screen.height;
            if (w <= 0 || h <= 0) { Debug.LogWarning($"[Smoke] 截图 {name} 跳过：屏幕尺寸为 {w}x{h}"); yield break; }

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes($"{ShotDir}/{name}.png", tex.EncodeToPNG());
            Object.Destroy(tex);

            Debug.Log($"[Smoke] 截图 {name}（{w}x{h}）");
            yield return null;
        }
    }
}
