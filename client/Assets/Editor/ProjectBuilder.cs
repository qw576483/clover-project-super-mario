using System.Collections.Generic;
using System.IO;
using CloverEngine;
using SuperMario.App;
using SuperMario.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperMario.EditorTools
{
    /// <summary>
    /// 一键生成工程里那批"机器生成"的资产：UI 面板预制体、三个场景、Build Settings。
    /// <para>
    /// <b>为什么用生成而不是手做</b>：面板预制体只是"挂一个组件"的壳（内容由面板自己
    /// 在 Awake 里搭，见 <c>UIBuilder</c>），手做 8 个反而慢且容易漏挂组件；
    /// 场景同理，只有一个 Bootstrap。这类"结构固定、无美术决策"的资产适合生成，
    /// 而**不做**生成的是真正有美术决策的东西（精灵、关卡数据）。
    /// </para>
    /// <para>重复执行是安全的：已存在的资产会被覆盖重建。</para>
    /// </summary>
    public static class ProjectBuilder
    {
        private const string UiDir = "Assets/Resources/UI";
        private const string SceneDir = "Assets/Scenes";

        /// <summary>全部面板类型。新增面板时**必须**在这里登记，否则 Resources/UI 下没有预制体，
        /// 运行时 Game.UI.Open 会报 "Panel prefab not found"。</summary>
        private static readonly System.Type[] Panels =
        {
            typeof(BootPanel),
            typeof(MainMenuPanel),
            typeof(CharSelectPanel),
            typeof(LoadingPanel),
            typeof(HudPanel),
            typeof(PausePanel),
            typeof(ResultPanel),
            typeof(GameOverPanel),
        };

        private static readonly string[] SceneNames =
        {
            SuperMario.Core.Scenes.Boot,
            SuperMario.Core.Scenes.Menu,
            SuperMario.Core.Scenes.Stage01,
        };

        [MenuItem("Super Mario/一键生成工程（预制体 + 场景）", false, 0)]
        public static void BuildAll()
        {
            BuildPanelPrefabs();
            BuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ProjectBuilder] 生成完成：面板预制体 + 3 个场景 + Build Settings");
        }

        [MenuItem("Super Mario/仅重建面板预制体", false, 20)]
        public static void BuildPanelPrefabs()
        {
            EnsureFolder(UiDir);

            // 前置：确认脚本资产已在 AssetDatabase 里。
            // 的 m_Script 会被写成 {fileID: 0}（MonoScript 尚未导入，引用解析不到），
            // 结果是预制体存在但组件是空壳 —— 运行时 UIManager 报 "Component X not found on
            // prefab"，所有面板都打不开。
            //
            // 这里刻意用【轻量】Refresh 而不是 ForceSynchronousImport：后者会重新导入整个
            // 工程（本项目 900+ 张精灵），一次跑十几分钟，实测会把批处理跑到超时。
            // 真正兜住这个坑的是下面每个面板的 TryGetScriptAsset 自检 —— 解析不到就报错跳过，
            // 而不是安静地生成一个废预制体。
            AssetDatabase.Refresh();

            foreach (var type in Panels)
            {
                var name = type.Name;
                var path = $"{UiDir}/{name}.prefab";

                var go = new GameObject(name, typeof(RectTransform));

                // 面板根节点必须【铺满父层】，否则整个 UI 都会缩到屏幕正中一小块。
                //
                // 若不在这里显式撑开，面板根就只有 100x100；而 UIBuilder 里所有
                // UIBuilder.Panel/Stretch 建的子节点都是 anchorMin(0,0)+anchorMax(1,1)
                // —— "铺满父节点"。父节点只有 100x100 时，它们就只铺成 100x100。
                // 表现为：Boot 的黑色背景变成标题后面一个 100x100 黑方块、
                // HUD 的整条顶栏挤成屏幕中央一个小黑块（MARIO/金币/时间全叠在一起）。
                //
                // 注意这个 bug 只在"预制体根节点"上出现：面板内容是在 Awake 里按
                // 显式坐标建的（Node/Block 都带 sizeDelta），所以标题、按钮这些
                // 反而看着正常 —— 于是很容易误判成"某几个元素排版错了"。
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;

                go.AddComponent(type);          // 面板组件是壳；内容在它自己的 Awake 里搭

                // 存之前自检一次：拿不到 MonoScript 说明真的还没导入，这时存下去就是废预制体。
                var mb = go.GetComponent(type) as MonoBehaviour;
                if (mb == null || !TryGetScriptAsset(mb, out _))
                {
                    Debug.LogError($"[ProjectBuilder] {name} 的脚本引用解析失败，预制体会是空壳 —— 已跳过。" +
                                   "请等脚本导入完成后重跑本项。");
                    Object.DestroyImmediate(go);
                    continue;
                }

                PrefabUtility.SaveAsPrefabAsset(go, path);
                Object.DestroyImmediate(go);

                Debug.Log($"[ProjectBuilder] 面板预制体：{path}");
            }
        }

        /// <summary>取组件对应的 MonoScript 资产；取不到返回 false（即"引用会写成 null"）。</summary>
        private static bool TryGetScriptAsset(MonoBehaviour mb, out MonoScript script)
        {
            script = MonoScript.FromMonoBehaviour(mb);
            return script != null;
        }

        [MenuItem("Super Mario/仅重建场景", false, 21)]
        public static void BuildScenes()
        {
            EnsureFolder(SceneDir);

            var previous = EditorSceneManager.GetActiveScene().path;

            foreach (var name in SceneNames)
            {
                // 每个场景都放一个 Bootstrap：
                //   - Boot 场景里它是真正的入口；
                //   - Menu / Stage01 里它负责"从编辑器直接点开这个场景也能跑"（否则一片空白）。
                // Bootstrap 内部有 _launched 守卫，重复的实例会自毁，不会出现两套状态机。
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var camGo = new GameObject("Main Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 7.5f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.36f, 0.58f, 0.99f);
                camGo.tag = "MainCamera";
                camGo.transform.position = new Vector3(0f, 0f, -10f);

                // 刻意【不】在这里挂 AudioListener：场景相机会随切场景被销毁，
                // 挂它上面会导致 Boot → Menu 之后每帧刷 "no audio listeners"。
                // 监听器由 Bootstrap（DontDestroyOnLoad）统一持有。见 Bootstrap.EnsureAudioListener。

                var bootGo = new GameObject("[Bootstrap]");
                bootGo.AddComponent<Bootstrap>();

                var path = $"{SceneDir}/{name}.unity";
                EditorSceneManager.SaveScene(scene, path);
                Debug.Log($"[ProjectBuilder] 场景：{path}");
            }

            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);

            ApplyBuildSettings();
        }

        private static void ApplyBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>();
            foreach (var name in SceneNames)
            {
                var path = $"{SceneDir}/{name}.unity";
                if (File.Exists(path)) list.Add(new EditorBuildSettingsScene(path, true));
                else Debug.LogError($"[ProjectBuilder] 场景缺失，无法加入 Build Settings：{path}");
            }
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log($"[ProjectBuilder] Build Settings：{list.Count} 个场景（Boot 为索引 0）");
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            var leaf = Path.GetFileName(assetPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
