using CloverEngine;
using SuperMario.Core;
using SuperMario.Module.Flow;
using UnityEngine;

namespace SuperMario.App
{
    /// <summary>
    /// 进程入口：全项目**唯一**手动挂载的驱动脚本。
    /// <para>
    /// 只有这一个 MonoBehaviour 需要美术/策划在场景里拖 —— 其余一切（UI、场景、音频、输入）
    /// 都由 <c>Game.Launch</c> 建好并常驻。这样"漏挂某个脚本"这类问题在结构上就不可能发生。
    /// </para>
    /// <para>
    /// 启动顺序是**有依赖的**，不能换：
    /// ① Game.Launch 建立门面与各管理器（没有它 Game.Res / Game.UI 都是 null）；
    /// ② CloverRes.Init 挂资源根（Res 在 Launch 里是 null，必须再挂一次）；
    /// ③ CloverInput.Init 建 EventSystem（不建则 UI 能开但按钮点不动）；
    /// ④ 装流程状态，再切到 Boot。
    /// </para>
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        [Header("日志")]
        [Tooltip("日志目录（相对 StreamingAssets 或绝对路径）。留空用引擎默认。")]
        [SerializeField] private string logDir = "";

        [Header("存档")]
        [Tooltip("设置文件目录。留空用引擎默认。")]
        [SerializeField] private string settingDir = "";

        [Header("调试")]
        [Tooltip("勾选后跳过启动画面直进关卡（改玩法时省两秒）。")]
        [SerializeField] private bool skipBoot;

        /// <summary>
        /// 已启动标记。
        /// <para>
        /// 必须自己记：Unity 在切场景时会重建 Bootstrap，而 <c>Game.Launch</c> 内部只打一条警告
        /// 就返回 —— 那样第二个 Bootstrap 会拿到一个"活着但没走完初始化"的门面，
        /// 然后各自 Install 一套流程状态，出现两套状态机同时跑。
        /// </para>
        /// </summary>
        private static bool _launched;

        private AppFlow _flow;

        private void Awake()
        {
            // ★ 必须开：Unity 默认【编辑器窗口失焦就停止推进帧循环】。
            //
            // 踩过的坑：跑自动化验证时（AI 用 CLI 驱动编辑器、或者人切去看别的窗口），
            // 窗口一失焦，Time.frameCount 就冻住 —— 实测两分钟只走了 2 帧。
            // 现象极具误导性：启动画面的 1.8 秒定时器永远不触发、流程卡死在 Boot、
            // 日志停在 "→ Boot" 之后再无输出，看起来像"状态机坏了"或"场景加载回调丢了"，
            // 实际游戏代码一行问题都没有。切回窗口它立刻自己跑起来。
            // 开了这个之后，失焦也照常 tick，行为可预期，也才能截图取证。
            Application.runInBackground = true;

            // 跨场景常驻：Bootstrap 只应存在一份。
            DontDestroyOnLoad(gameObject);

            if (_launched)
            {
                // 后到的实例直接自毁，避免出现第二个流程状态机。
                Destroy(gameObject);
                return;
            }
            _launched = true;

            var config = new GameConfig
            {
                // ResourceRoot 留空 = 以 Resources 为根。
                // 之所以不用自定义根：单机项目没有分包需求，多一层目录只是多一个出错点。
                ResourceRoot = "",
                LogDir = string.IsNullOrEmpty(logDir) ? null : logDir,
                SettingDir = string.IsNullOrEmpty(settingDir) ? null : settingDir,
            };

            Game.Launch(config);
            CloverRes.Init(config.ResourceRoot);
            CloverInput.Init();
            EnsureAudioListener();

            Game.Logger.Info("Boot", "Super Mario Bros. 启动中…");

            _flow = new AppFlow();
            _flow.Install();

            _flow.Start();
        }

        /// <summary>
        /// 保证有一个 AudioListener，且**挂在本对象上**。
        /// <para>
        /// 为什么必须挂 Bootstrap 自己而不是场景相机：Bootstrap 是 <c>DontDestroyOnLoad</c>，
        /// 而场景相机会随 <c>Game.Scene.Load</c> 被销毁。挂相机上的话，Boot → Menu 一切场景就没了，
        /// 之后每帧刷一条 "There are no audio listeners in the scene" ——
        /// 实测把 Editor.log 刷到 74MB，并且把真正的问题全部淹掉。
        /// </para>
        /// <para>
        /// 判据只看"自己身上有没有"，不看场景里有没有：场景里那个本来就活不过切场景，
        /// 拿它当"已存在"的判据正是上面那个 bug 的成因。
        /// </para>
        /// </summary>
        private void EnsureAudioListener()
        {
            if (GetComponent<AudioListener>() == null) gameObject.AddComponent<AudioListener>();

            // 场景自带监听器时会出现两个，Unity 会警告且声音行为未定义 —— 把别人的关掉。
            // （用 FindObjectsByType 而不是 FindFirstObjectByType：这里要处理"全部"。）
            foreach (var other in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                if (other.gameObject == gameObject) continue;
                other.enabled = false;
                Game.Logger.Info("Boot", $"场景里已有 AudioListener（{other.gameObject.name}），已禁用以避免重复");
            }
        }
    }
}
