using CloverEngine;

namespace SuperMario.Module.Audio
{
    /// <summary>
    /// 音频门面。业务只经过这里出声，不直接碰 <c>Game.Sound</c>。
    /// <para>
    /// 为什么要包一层而不是到处写 <c>Game.Sound.PlaySFX("coin")</c>：
    /// ① 音效名有拼写风险，集中之后只可能错一处；
    /// ② "同一音效短时间内不要重复播"这类策略需要一个统一落点
    ///    （踩敌人时连着踩两只，原版是两声，但金币刷屏时会糊成噪音）。
    /// </para>
    /// </summary>
    public interface IAudio
    {
        void PlaySfx(string clip);
        void PlayBgm(string clip);
        void StopBgm();
        void StopAll();

        /// <summary>分组音量（0~1）。设置面板的"音效 / 音乐"滑杆直接调它。</summary>
        float GetVolume(SoundGroup group);
        void SetVolume(SoundGroup group, float volume);
        void SetMute(SoundGroup group, bool mute);
        bool IsMuted(SoundGroup group);
    }

    internal sealed class AudioModule : IAudio
    {
        // ⛔ 这里**刻意不再自己记一份静音状态**（原先是 `_bgmMuted` / `_sfxMuted` 两个字段）。
        //
        // 为什么：静音的真源是引擎那份表（`Game.Sound.SetMute` 写、播放时按它算音量）。
        // 自己再存一份，"两份状态"就必然有漂移的路径 —— 面板直接调 `Game.Sound.SetMute`、
        // 将来接引擎的设置存档、引擎侧别处改静音，都会让本地这份变旧，
        // 表现成"界面显示已静音但还有声音"（见 `ISoundManager.IsMuted` 的 XML）。
        // ⇒ 读就统一读引擎那份（同源状态）。

        public void PlaySfx(string clip)
        {
            if (string.IsNullOrEmpty(clip)) return;
            if (Game.Sound == null)
            {
                // 引擎没起（比如在编辑器里直接点开某个场景）时不静默：
                // 静默会让人以为"音效没做"，而实际上只是没走启动流程。
                Game.Logger.Warn("Audio", $"Game.Sound 未就绪，跳过音效 {clip}");
                return;
            }
            // 静音时不发起播放（与旧行为一致）：引擎那边即使播了也是 0 音量，
            // 但会白占一个音源、白加载一次音频 —— 所以这里照旧早退。
            if (Game.Sound.IsMuted(SoundGroup.SFX)) return;
            Game.Sound.PlaySFX(clip);
        }

        public void PlayBgm(string clip)
        {
            if (string.IsNullOrEmpty(clip)) return;
            if (Game.Sound == null) { Game.Logger.Warn("Audio", "Game.Sound 未就绪，跳过 BGM"); return; }
            if (Game.Sound.IsMuted(SoundGroup.BGM)) return;
            Game.Sound.PlayBGM(clip);
        }

        public void StopBgm() => Game.Sound?.StopBGM(0.3f);

        public void StopAll() => Game.Sound?.StopAll();

        public float GetVolume(SoundGroup group) => Game.Sound?.GetVolume(group) ?? 1f;

        public void SetVolume(SoundGroup group, float volume)
        {
            Game.Sound?.SetVolume(group, volume);
        }

        /// <summary>设置静音。**只写引擎那一份**（不再本地另存一份，见类顶部说明）。</summary>
        public void SetMute(SoundGroup group, bool mute) => Game.Sound?.SetMute(group, mute);

        /// <summary>
        /// 查询静音。读引擎那份同源状态（<see cref="ISoundManager.IsMuted"/>）。
        /// 引擎未就绪时返回 <c>false</c> —— 与旧实现（本地字段默认 false）同口径。
        /// </summary>
        public bool IsMuted(SoundGroup group) => Game.Sound != null && Game.Sound.IsMuted(group);
    }
}
