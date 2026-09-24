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
            // 宿主行为（后台运行 / 重复驱动器守卫 / AudioListener 归属）**全部下发给引擎** ——
            //   必须在 Game.Launch **之前**：DuplicateInstanceGuard / OwnAudioListener 是
            //   "创建宿主时执行一次"的动作，而宿主由 Launch 里的 EngineRunner.Ensure 创建 ——
            //   宿主已存在时才配会记一条 Warn 且**不生效**（Runtime/Core/EngineRunner.cs 的 Configure）。
            //
            //      验证时（AI 用 CLI 驱动编辑器、或人切去看别的窗口）窗口一失焦 Time.frameCount 就冻住
            //      —— 实测两分钟只走 2 帧；现象极具误导性（启动画面的 1.8 秒定时器永不触发、流程卡死在
            //      Boot、日志停在 "→ Boot" 之后再无输出），看着像状态机坏了，实际游戏代码一行问题都没有。
            //   ② DuplicateInstanceGuard：引擎**驱动器**去重（两个驱动器 = Game.Tick 每帧跑两遍，
            //      位移 / 计时全部加倍，而且不报任何错）。注意它管的是引擎宿主；本类自己的 `_launched`
            //      管的是"不要再 Install 一套 AppFlow" —— 两者层次不同，都要留。
            //   ③ OwnAudioListener：监听器由宿主（DontDestroyOnLoad）**独占** —— 否则切场景就丢，
            //      Unity 每帧刷一条 "There are no audio listeners in the scene"（实测把 Editor.log
            //      刷到 74MB，并把真正的问题全部淹掉）。
            //      代价（引擎 XML 已写明）：监听器落在原点上的宿主上 ⇒ **3D 音效衰减按原点算**。
            //      本项目判定【可用】：音效全部走 `AudioModule` → `Game.Sound.PlaySFX`（2D，spatialBlend = 0），
            //      全工程**没有任何 PlaySFXAt 调用**（判据：`grep -rn "PlaySFXAt" client/Assets` → 0 命中；
            //      同一条 grep 也跑在 `.ai-tmp/test/sink-c-takeover-selfcheck.ps1` 里）
            //      ⇒ 没有按位置衰减的音效，监听器在原点不影响听感。
            Game.ConfigureHost(new EngineHostOptions
            {
                RunInBackground = true,
                DuplicateInstanceGuard = true,
                OwnAudioListener = true,
            });

            // 跨场景常驻：Bootstrap 只应存在一份。
            DontDestroyOnLoad(gameObject);

            if (_launched)
            {
                // 后到的实例直接自毁，避免出现第二个流程状态机（见 _launched 的说明）。
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
            // `Game.ConfigureHost(OwnAudioListener = true)` 在**创建宿主时**交给引擎
            // （宿主上挂一个、并把场景里其它的禁掉，每个留一条 Info）—— 本项目不再自己找、
            // 也不再自己 `FindObjectsByType<AudioListener>()` 扫全场景。

            Game.Logger.Info("Boot", "Super Mario Bros. 启动中…");

            _flow = new AppFlow();
            _flow.Install();

            _flow.Start();
        }

    }
}
