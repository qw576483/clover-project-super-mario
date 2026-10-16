using CloverEngine;
using SuperMario.Core;

namespace SuperMario.Module.Score
{
    /// <summary>
    /// 分数 / 金币 / 命数 / 时间。HUD 要读的全部数值都在这里。
    /// <para>
    /// 之所以把四样塞进一个模块：它们的变化都会让 HUD 变脏，且**同一条链上一起变**
    /// （吃金币 → 分数 +200 且金币 +1；死一次 → 命数 -1 且时间重置）。
    /// 拆成四个模块只会让"改一处要记得通知另一处"的坑变多。
    /// </para>
    /// </summary>
    public interface IScore
    {
        int Points { get; }
        int Coins { get; }
        int Lives { get; }
        int TimeLeft { get; }

        /// <summary>加分数（内部会发 HUD 刷新事件）。</summary>
        void Add(int points);

        /// <summary>吃一枚金币：满 100 枚换一条命（原版规则）。</summary>
        void AddCoin();

        /// <summary>加一条命（1-UP 蘑菇）。</summary>
        void AddLife();

        /// <summary>掉一条命，返回是否还有剩余。</summary>
        bool LoseLife();

        /// <summary>每帧推进计时。<paramref name="dt"/> 为秒。</summary>
        void TickTime(float dt);

        /// <summary>
        /// 通关结算：把**一个剩余时间单位**兑现成分数（原版"剩余时间换分"）。
        /// 返回是否还有剩余单位（false = 已经兑完）。
        /// <para>
        /// 为什么不复用 <see cref="TickTime"/>：那条是"游戏中的时间流逝"，走到 0 会广播
        /// <c>Events.TimeUp</c>（= 判死）。结算是把剩下的时间**兑现**，绝不能触发 TimeUp。
        /// </para>
        /// </summary>
        bool TallyTimeUnit(int pointsPerUnit);

        /// <summary>重开一关：时间恢复满，分数不动（原版死一次保留分数）。</summary>
        void ResetForLevel();

        /// <summary>新开一局：全部归零。</summary>
        void ResetAll();

        /// <summary>
        /// 覆盖当前数值。
        /// <para>双人模式换手时用：两位玩家各有各的分数与命数，
        /// 换手就是把这组数值灌回来（不是重新开一局）。</para>
        /// </summary>
        void Restore(int points, int coins, int lives);
    }

    internal sealed class ScoreModule : IScore
    {
        public int Points { get; private set; }
        public int Coins { get; private set; }
        public int Lives { get; private set; }
        public int TimeLeft { get; private set; }

        private float _timeAccumulator;
        private bool _hurryPlayed;

        public ScoreModule()
        {
            ResetAll();
        }

        public void Add(int points)
        {
            if (points <= 0) return;
            Points += points;
            Game.Event.Emit(Events.HudDirty);
        }

        public void AddCoin()
        {
            Coins++;
            if (Coins >= 100)
            {
                Coins -= 100;
                AddLife();
            }
            Game.Event.Emit(Events.HudDirty);
        }

        public void AddLife()
        {
            Lives++;
            Game.Event.Emit(Events.HudDirty);
        }

        /// <summary>通关结算：兑 1 个时间单位（见 <see cref="IScore.TallyTimeUnit"/>）。</summary>
        public bool TallyTimeUnit(int pointsPerUnit)
        {
            if (TimeLeft <= 0) return false;
            TimeLeft--;
            Points += pointsPerUnit;
            Game.Event.Emit(Events.HudDirty);   // HUD 同步看到"时间在减、分在涨"
            return TimeLeft > 0;
        }

        public bool LoseLife()
        {
            Lives--;
            Game.Event.Emit(Events.HudDirty);
            return Lives > 0;
        }

        public void TickTime(float dt)
        {
            if (TimeLeft <= 0) return;

            _timeAccumulator += dt;
            while (_timeAccumulator >= GameConst.TimeTickInterval)
            {
                _timeAccumulator -= GameConst.TimeTickInterval;
                TimeLeft--;
                Game.Event.Emit(Events.TimeChanged, TimeLeft);

                // 剩 100 秒时催一下（原版会放 hurry-up 音效并换快节奏 BGM）。
                if (!_hurryPlayed && TimeLeft == 100)
                {
                    _hurryPlayed = true;
                    Game.Event.Emit(Events.HurryUp);
                }

                if (TimeLeft <= 0)
                {
                    Game.Event.Emit(Events.TimeUp);
                    break;
                }
            }
            Game.Event.Emit(Events.HudDirty);
        }

        public void ResetForLevel()
        {
            TimeLeft = GameConst.LevelTime;
            _timeAccumulator = 0f;
            _hurryPlayed = false;
            Game.Event.Emit(Events.HudDirty);
        }

        public void ResetAll()
        {
            Points = 0;
            Coins = 0;
            Lives = GameConst.StartLives;
            TimeLeft = GameConst.LevelTime;
            _timeAccumulator = 0f;
            _hurryPlayed = false;
            Game.Event.Emit(Events.HudDirty);
        }

        public void Restore(int points, int coins, int lives)
        {
            Points = points;
            Coins = coins;
            Lives = lives;
            TimeLeft = GameConst.LevelTime;
            _timeAccumulator = 0f;
            _hurryPlayed = false;
            Game.Event.Emit(Events.HudDirty);
        }
    }
}
