using System;
using System.IO;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Module.Flow;
using SuperMario.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SuperMario.EditorTools
{
    /// <summary>
    /// 冒烟自审（**手动触发**）：在编辑器里点菜单跑一遍并截图。
    /// <para>
    /// ⚠️ <b>已被 <c>scripts/play-driver.ps1</c> 取代</b>（那条链才是自动化入口，能带参数、能收日志）；
    /// 本菜单项**保留**只为"手上没有脚本时也能人肉跑一遍"这个兜底场景。
    /// </para>
    /// <para>
    /// 为什么是手动而不是批处理：项目在 <c>-batchmode</c> 下跑完 <c>-executeMethod</c> 就立即退出，
    /// 不会进入播放模式，所以这条路径只能从 GUI 菜单用。
    /// <b>自动化验证走 <c>Assets/Tests/PlayMode/SmokeTests.cs</c></b>（unity test 会正确驱动播放模式）。
    /// </para>
    /// <para>
    /// 轮询状态推进而不是等固定秒数：加载耗时随机器变化，按秒截会得到一堆空图。
    /// </para>
    /// <para>
    /// ⛔ <b>截图目录已移出工程树</b>：输出到 <c>&lt;项目根&gt;/.ai-tmp/screenshots</c>
    /// （见 <see cref="SuperMario.Core.EvidencePaths"/>，可用环境变量 <c>SMB_SHOT_DIR</c> 覆盖）。
    /// 原先是工程内的 <c>_smb_work/shots</c> —— 跑一次就在工程树里留一堆 png（证据落进 <c>Assets/</c> 是违规产物）。
    /// </para>
    /// </summary>
    public static class SmokeScreenshots
    {
        /// <summary>截图输出目录（**绝对路径**，在工程树之外）。</summary>
        private static string OutDir => EvidencePaths.ShotDir;

        private static int _step;
        private static float _phaseEntered;
        private static bool _running;

        [MenuItem("Super Mario/冒烟自审（跑一遍并截图）", false, 100)]
        public static void Run()
        {
            if (_running) { Debug.LogWarning("[Smoke] 已在运行"); return; }
            if (Application.isPlaying) { Debug.LogWarning("[Smoke] 请先退出播放模式"); return; }

            if (!Directory.Exists(OutDir)) Directory.CreateDirectory(OutDir);
            foreach (var f in Directory.GetFiles(OutDir, "*.png")) File.Delete(f);

            _running = true;
            _step = 0;
            _phaseEntered = 0f;

            EditorSceneManager.OpenScene($"Assets/Scenes/{Scenes.Boot}.unity");
            EditorApplication.isPlaying = true;
            EditorApplication.update += Update;
            Debug.Log($"[Smoke] 开始，截图输出到 {OutDir}");
        }

        private static void Update()
        {
            if (!_running) return;
            if (!Application.isPlaying) { Stop(); return; }

            switch (_step)
            {
                case 0:
                    if (Elapsed() > 0.6f) { Shot("01-boot"); Next(); }
                    break;

                case 1:
                    if (Game.UI != null && Game.UI.IsOpen<MainMenuPanel>())
                    {
                        Shot("02-menu");
                        Game.Event.Emit(Events.CharChosen, 1);
                        Next();
                    }
                    else if (Elapsed() > 20f) Fail("主菜单没出来");
                    break;

                case 2:
                    if (StageContext.Player != null && StageContext.Score != null)
                    {
                        if (Game.UI.IsOpen<LoadingPanel>()) Shot("03-loading");
                        Next();
                    }
                    else if (Elapsed() > 60f) Fail("关卡会话没就绪");
                    break;

                case 3:
                    if (Elapsed() > 1.5f) { Shot("04-stage-start"); Next(); }
                    break;

                case 4:
                    if (Elapsed() > 2f)
                    {
                        Shot("05-stage-stable");
                        var s = StageContext.Score;
                        var p = StageContext.Player;
                        Debug.Log($"[Smoke] 分数={s?.Points} 金币={s?.Coins} 命={s?.Lives} 时间={s?.TimeLeft} " +
                                  $"玩家={p?.Transform?.position.ToString() ?? "null"} 着地={p?.Grounded} 存活={p?.Alive}");
                        Next();
                    }
                    break;

                case 5:
                    // CaptureScreenshot 是帧末写盘，立刻停会得到 0 字节的图。
                    if (Elapsed() > 1f) { Debug.Log("[Smoke] 完成"); Stop(); }
                    break;
            }
        }

        private static float Elapsed() => Time.realtimeSinceStartup - _phaseEntered;
        private static void Next() { _step++; _phaseEntered = Time.realtimeSinceStartup; }

        private static void Shot(string name)
        {
            // 收敛到引擎 `CloverEngine.Screenshot.CaptureToFile`（原来是手写
            // `Texture2D` + `ReadPixels` + `EncodeToPNG` + `DestroyImmediate` —— 与 SmokeTests 里那份逐字重复）。
            // 引擎版负责：目录不存在时递归创建、屏幕尺寸非法 / 编码失败时**返回 false + Error 留痕**
            // （手写版这几条失败分支全是静默的），并在 finally 里销毁临时纹理。
            // 调用时机未变：仍在 `EditorApplication.update` 这一拍里同帧读屏。
            var path = EvidencePaths.Shot(name);
            if (!Screenshot.CaptureToFile(path))
                Debug.LogWarning($"[Smoke] 截图 {name} 失败（详见 [Error] Screenshot 那一行）：{path}");
        }

        private static void Fail(string reason)
        {
            Debug.LogError($"[Smoke] 失败：{reason}");
            Stop();
        }

        private static void Stop()
        {
            _running = false;
            EditorApplication.update -= Update;
            EditorApplication.isPlaying = false;
        }
    }
}
