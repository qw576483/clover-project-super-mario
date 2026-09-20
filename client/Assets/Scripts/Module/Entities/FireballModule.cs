using System;
using System.Collections.Generic;
using CloverEngine;
using SuperMario.Core;
using SuperMario.Module.Audio;
using SuperMario.Module.Level;
using UnityEngine;

namespace SuperMario.Module.Entities
{
    /// <summary>一颗火球的对外接口（玩法层用它判碰撞）。</summary>
    public interface IFireball
    {
        Rect Bounds { get; }
        bool Dead { get; }
        /// <summary>撞到敌人后引爆（不伤害敌人，伤害由玩法层结算）。</summary>
        void Explode();
        void Clear();
    }

    /// <summary>火球门面。</summary>
    public interface IFireballs
    {
        IReadOnlyList<IFireball> Active { get; }
        int Count { get; }
        /// <summary>朝指定方向发射一颗。<paramref name="feetPos"/> 是马里奥脚底。</summary>
        void Fire(Vector2 feetPos, bool facingLeft);
        void Clear();
        void Reap();
        void Preload(Action onDone);
    }

    internal sealed class FireballModule : IFireballs
    {
        private readonly List<IFireball> _active = new List<IFireball>();
        public IReadOnlyList<IFireball> Active => _active;
        public int Count => _active.Count;

        private readonly ILevel _level;
        private readonly IAudio _audio;
        private readonly Transform _root;
        private readonly List<Sprite> _frames = new List<Sprite>();
        private readonly List<Sprite> _boomFrames = new List<Sprite>();
        private int _pending;

        public FireballModule(ILevel level, IAudio audio, Transform root)
        {
            _level = level;
            _audio = audio;
            _root = root;
        }

        public void Preload(Action onDone)
        {
            _pending = SpriteNames.Fireball.Length + SpriteNames.FireballBoom.Length;
            foreach (var f in SpriteNames.Fireball)
                Game.Res.LoadAsset<Sprite>(ResPaths.Enemy(f), s => { if (s != null) _frames.Add(s); Done(onDone); });
            foreach (var f in SpriteNames.FireballBoom)
                Game.Res.LoadAsset<Sprite>(ResPaths.Enemy(f), s => { if (s != null) _boomFrames.Add(s); Done(onDone); });
        }

        private void Done(Action onDone)
        {
            if (--_pending <= 0) onDone?.Invoke();
        }

        public void Fire(Vector2 feetPos, bool facingLeft)
        {
            // 同屏上限（原版是 2）：不加这个限制，按住连发会把屏幕糊满。
            if (_active.Count >= GameConst.MaxFireballs) return;

            var go = new GameObject("Fireball");
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 7;
            if (_frames.Count > 0) sr.sprite = _frames[0];

            var fb = go.AddComponent<Fireball>();
            fb.Init(_level, _audio, feetPos, facingLeft, _frames, _boomFrames, sr);
            _active.Add(fb);
            _audio?.PlaySfx(Sfx.Fireball);
        }

        public void Reap()
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i] is Fireball f && f.Finished)
                {
                    _active[i].Clear();
                    _active.RemoveAt(i);
                }
            }
        }

        public void Clear()
        {
            foreach (var f in _active) f.Clear();
            _active.Clear();
        }
    }

    /// <summary>
    /// 一颗火球。
    /// <para>
    /// 行为按原版：水平飞行、落地反弹、撞墙或撞敌人即炸、有存活时限。
    /// 撞击判定只做"撞墙"和"寿命"两种，撞敌人的判定交给玩法层 —— 因为那需要结算伤害与分数，
    /// 属于玩法规则而不是子弹物理。
    /// </para>
    /// </summary>
    internal sealed class Fireball : MonoBehaviour, IFireball
    {
        public Rect Bounds { get; private set; }
        public bool Dead { get; private set; }
        public bool Finished { get; private set; }

        private static readonly Vector2 Size = new Vector2(0.6f, 0.6f);

        private ILevel _level;
        private IAudio _audio;
        private SpriteRenderer _sr;
        private readonly List<Sprite> _frames = new List<Sprite>();
        private readonly List<Sprite> _boom = new List<Sprite>();

        private Vector2 _vel;
        private float _life = GameConst.FireballLife;
        private float _animTimer;
        private int _frame;
        private bool _exploding;
        private float _boomTimer;

        public void Init(ILevel level, IAudio audio, Vector2 feetPos, bool facingLeft,
                         List<Sprite> frames, List<Sprite> boomFrames, SpriteRenderer sr)
        {
            _level = level;
            _audio = audio;
            _sr = sr;
            _frames.AddRange(frames);
            _boom.AddRange(boomFrames);

            // 位置与初速都照 clone 写（不要在这里自己调参）：
            //   · 出生点 = clone `Mario.cs:247` `Instantiate(Fireball, FirePos.position, …)`，
            //     `FirePos` 是 Mario 的子节点、local = (±0.5, 1)（出处 `Prefabs/_managers/Level Starter.prefab:749`），
            //     ⚠️ 我们这里留的是既有偏移 (±0.4, +0.6)（**未搬到 clone 的 FirePos**，登记在 E-22）。
            //   · 初速竖直分量 = **向下** 11 —— clone `MarioFireball.cs:21` `(directionX*absVelocity.x, -absVelocity.y)`。
            transform.position = new Vector3(feetPos.x + (facingLeft ? -0.4f : 0.4f), feetPos.y + 0.6f, 0f);
            _vel = new Vector2((facingLeft ? -1f : 1f) * GameConst.FireballSpeed, -GameConst.FireballVelocityY);
            RecomputeBounds();
        }

        private void RecomputeBounds()
        {
            var p = transform.position;
            Bounds = new Rect(p.x - Size.x * 0.5f, p.y - Size.y * 0.5f, Size.x, Size.y);
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt > 0.05f) dt = 0.05f;
            if (Finished) return;

            if (_exploding)
            {
                _boomTimer += dt;
                var idx = Mathf.FloorToInt(_boomTimer / 0.05f);
                if (_boomTimer >= 0.15f || idx >= _boom.Count)
                {
                    Finished = true;
                    return;
                }
                if (_boom.Count > 0) _sr.sprite = _boom[Mathf.Clamp(idx, 0, _boom.Count - 1)];
                return;
            }

            _life -= dt;
            if (_life <= 0f) { Explode(); return; }

            // 火球走**自己那份**重力（clone `Mario Fireball.prefab:64` `m_GravityScale: 5.5`），
            // 不是马里奥的 5.3 —— 两个是不同预制体，混用会改掉火球的抛跳弧线。
            _vel.y -= GameConst.FireballGravity * dt;
            if (_vel.y < -GameConst.MaxFallSpeed) _vel.y = -GameConst.MaxFallSpeed;

            // X：撞墙就炸（原版火球撞墙会消失，不会反弹）。
            var nx = transform.position.x + _vel.x * dt;
            var hitWall = false;
            foreach (var c in Overlap(new Rect(nx - Size.x * 0.5f, transform.position.y - Size.y * 0.5f, Size.x, Size.y)))
            {
                if (!_level.IsSolidTile(c.x, c.y)) continue;
                hitWall = true;
                break;
            }
            if (hitWall) { Explode(); return; }

            // Y：落地反弹（这是火球能沿地面跳着前进的原因）。
            var ny = transform.position.y + _vel.y * dt;
            foreach (var c in Overlap(new Rect(nx - Size.x * 0.5f, ny - Size.y * 0.5f, Size.x, Size.y)))
            {
                if (!_level.IsSolidTile(c.x, c.y)) continue;
                if (_vel.y <= 0f)
                {
                    // 撞地面 ⇒ 反弹向上，速度 = `+absVelocity.y`（clone `MarioFireball.cs:50`）。
                    ny = c.y + 1f + Size.y * 0.5f;
                    _vel.y = GameConst.FireballVelocityY;
                }
                else
                {
                    // 撞顶 ⇒ 压回向下，速度 = `-absVelocity.y`（clone `MarioFireball.cs:52`）。
                    // ⚠️ 改前这里是 `0f`（"贴住顶"）—— 与原版"顶一下立刻往下"不一致，一并照 clone 改。
                    ny = c.y - Size.y * 0.5f;
                    _vel.y = -GameConst.FireballVelocityY;
                }
                break;
            }

            transform.position = new Vector3(nx, ny, 0f);
            RecomputeBounds();

            if (_frames.Count > 1)
            {
                _animTimer += dt;
                if (_animTimer >= 0.06f)
                {
                    _animTimer = 0f;
                    _frame = (_frame + 1) % _frames.Count;
                    _sr.sprite = _frames[_frame];
                }
            }

            if (transform.position.y < -12f) Finished = true;
        }

        public void Explode()
        {
            if (_exploding || Finished) return;
            _exploding = true;
            Dead = true;
            _boomTimer = 0f;
            _vel = Vector2.zero;
            if (_boom.Count > 0 && _sr != null) _sr.sprite = _boom[0];
        }

        public void Clear()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        private static IEnumerable<Vector2Int> Overlap(Rect r)
        {
            var x0 = Mathf.FloorToInt(r.xMin);
            var x1 = Mathf.FloorToInt(r.xMax - 0.0001f);
            var y0 = Mathf.FloorToInt(r.yMin);
            var y1 = Mathf.FloorToInt(r.yMax - 0.0001f);
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    yield return new Vector2Int(x, y);
        }
    }
}
