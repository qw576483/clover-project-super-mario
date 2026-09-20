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
        private bool _bgmMuted;
        private bool _sfxMuted;

        public void PlaySfx(string clip)
        {
            if (_sfxMuted || string.IsNullOrEmpty(clip)) return;
            if (Game.Sound == null)
            {
                // 引擎没起（比如在编辑器里直接点开某个场景）时不静默：
                // 静默会让人以为"音效没做"，而实际上只是没走启动流程。
                Game.Logger.Warn("Audio", $"Game.Sound 未就绪，跳过音效 {clip}");
                return;
            }
            Game.Sound.PlaySFX(clip);
        }

        public void PlayBgm(string clip)
        {
            if (_bgmMuted || string.IsNullOrEmpty(clip)) return;
            if (Game.Sound == null) { Game.Logger.Warn("Audio", "Game.Sound 未就绪，跳过 BGM"); return; }
            Game.Sound.PlayBGM(clip);
        }

        public void StopBgm() => Game.Sound?.StopBGM(0.3f);

        public void StopAll() => Game.Sound?.StopAll();

        public float GetVolume(SoundGroup group) => Game.Sound?.GetVolume(group) ?? 1f;

        public void SetVolume(SoundGroup group, float volume)
        {
            Game.Sound?.SetVolume(group, volume);
        }

        public void SetMute(SoundGroup group, bool mute)
        {
            if (group == SoundGroup.BGM) _bgmMuted = mute; else _sfxMuted = mute;
            Game.Sound?.SetMute(group, mute);
        }

        public bool IsMuted(SoundGroup group) =>
            group == SoundGroup.BGM ? _bgmMuted : _sfxMuted;
    }
}
