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
    /// 为什么是手动而不是批处理：项目在 <c>-batchmode</c> 下跑完 <c>-executeMethod</c> 就立即退出，
    /// 不会进入播放模式，所以这条路径只能从 GUI 菜单用。
    /// <b>自动化验证走 <c>Assets/Tests/PlayMode/SmokeTests.cs</c></b>（unity test 会正确驱动播放模式）。
    /// </para>
    /// <para>
    /// 轮询状态推进而不是等固定秒数：加载耗时随机器变化，按秒截会得到一堆空图。
    /// </para>
    /// </summary>
    public static class SmokeScreenshots
    {
        private const string OutDir = "C:/Work/Server/full-dev/_smb_work/shots";

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
            // 同 SmokeTests：避开未引入的 ScreenCaptureModule，走 ReadPixels + EncodeToPNG。
            var w = Screen.width;
            var h = Screen.height;
            if (w <= 0 || h <= 0) return;

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes($"{OutDir}/{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log($"[Smoke] 截图 {name}（{w}x{h}）");
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
