using System;
using System.IO;
using UnityEngine;

namespace SuperMario.Core
{
    /// <summary>
    /// AI 取证产物目录（截图）解析 —— **全项目唯一来源**。
    /// <para>
    /// ⛔ <b>证据不许落工程树</b>（引擎 skill 的规定：一次性产物只放 <c>&lt;项目根&gt;/.ai-tmp/</c>，
    /// 截图放 <c>&lt;项目根&gt;/.ai-tmp/screenshots/</c>）。本项目原先有两处写死在工程内的输出目录
    /// —— <c>Editor/SmokeScreenshots.cs</c> 的 <c>_smb_work/shots</c> 与
    /// <c>Tests/PlayMode/SmokeTests.cs</c> 的 <c>_smb_work/shots</c> —— 跑一次就在工程树里留一堆 png。
    /// </para>
    /// <para>
    /// <b>&lt;项目根&gt; 的定义</b>：本仓库根目录（<c>client/</c> 的**父目录**），不是 Unity 工程目录。
    /// 解析：<c>Application.dataPath</c> = <c>&lt;项目根&gt;/client/Assets</c> ⇒ 上两级即项目根。
    /// </para>
    /// <para>
    /// <b>参数化</b>：环境变量 <see cref="ShotDirEnvVar"/>（<c>SMB_SHOT_DIR</c>）可整体覆盖输出目录
    /// —— 自动化脚本要把截图收到别处（或收进 CI 产物目录）时**不必改代码**。
    /// </para>
    /// </summary>
    public static class EvidencePaths
    {
        /// <summary>覆盖截图输出目录的环境变量名（值为绝对路径）。</summary>
        public const string ShotDirEnvVar = "SMB_SHOT_DIR";

        /// <summary>项目根下的证据目录名（与引擎 skill 的口径一致）。</summary>
        public const string TmpDirName = ".ai-tmp";

        /// <summary>证据目录下的截图子目录名。</summary>
        public const string ShotSubDirName = "screenshots";

        /// <summary>
        /// 本仓库根目录 = <c>Application.dataPath</c> 的上两级。
        /// <para>
        /// 解析不出来时逐级退回（<c>client/</c> → <c>dataPath</c>），**不抛异常** ——
        /// 宁可把图落在一个可诊断的路径上，也不要因为路径解析失败把冒烟测试打断。
        /// </para>
        /// </summary>
        public static string ProjectRoot
        {
            get
            {
                var clientDir = Directory.GetParent(Application.dataPath);
                var root = clientDir == null ? null : clientDir.Parent;
                return (root ?? clientDir ?? new DirectoryInfo(Application.dataPath)).FullName;
            }
        }

        /// <summary>
        /// 截图输出目录（绝对路径）。目录不存在时由
        /// <see cref="CloverEngine.Screenshot.CaptureToFile"/> 自动递归创建，调用方不必先建。
        /// </summary>
        public static string ShotDir
        {
            get
            {
                var overridden = Environment.GetEnvironmentVariable(ShotDirEnvVar);
                if (!string.IsNullOrEmpty(overridden)) return overridden;
                return Path.Combine(ProjectRoot, TmpDirName, ShotSubDirName);
            }
        }

        /// <summary>拼一个截图的绝对路径（<paramref name="name"/> 不带扩展名）。</summary>
        public static string Shot(string name) => Path.Combine(ShotDir, name + ".png");
    }
}
