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

        /// <summary>火球两帧循环 0.06 s/帧。**逐字沿用**原手写节奏（`_animTimer >= 0.06f`）。</summary>
        private const float FrameSeconds = 0.06f;

        private ILevel _level;
        private IAudio _audio;
        private SpriteRenderer _sr;
        private readonly List<Sprite> _boom = new List<Sprite>();

        /// <summary>
        /// 逐帧动画器（引擎 <see cref="SpriteFrameAnimator"/>）—— 收敛掉自写的
        /// </summary>
        private SpriteFrameAnimator _anim;

        private Vector2 _vel;
        private float _life = GameConst.FireballLife;
        private bool _exploding;
        private float _boomTimer;

        public void Init(ILevel level, IAudio audio, Vector2 feetPos, bool facingLeft,
                         List<Sprite> frames, List<Sprite> boomFrames, SpriteRenderer sr)
        {
            _level = level;
            _audio = audio;
            _sr = sr;
            _boom.AddRange(boomFrames);
            _anim = new SpriteFrameAnimator(sr);
            // 空帧表由引擎自己降频 Warn；单帧等价于"恒显第 0 帧"（原 `Count > 1` 的早退）。
            _anim.Play(frames.ToArray(), 1f / FrameSeconds);

            // 位置与初速都照 clone 写（不要在这里自己调参）：
            //   · 出生点 = clone `Mario.cs:247` `Instantiate(Fireball, FirePos.position, …)`，
            //     `FirePos` 是 Mario 的子节点、local = (±0.5, 1)（出处 `Prefabs/_managers/Level Starter.prefab:749`），
            //     我们这里留的是既有偏移 (±0.4, +0.6)（**未搬到 clone 的 FirePos**，登记在 E-22）。
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
            if (ScanFirstSolid(new Rect(nx - Size.x * 0.5f, transform.position.y - Size.y * 0.5f, Size.x, Size.y)))
            { Explode(); return; }

            // Y：落地反弹（这是火球能沿地面跳着前进的原因）。
            var ny = transform.position.y + _vel.y * dt;
            if (ScanFirstSolid(new Rect(nx - Size.x * 0.5f, ny - Size.y * 0.5f, Size.x, Size.y)))
            {
                if (_vel.y <= 0f)
                {
                    // 撞地面 ⇒ 反弹向上，速度 = `+absVelocity.y`（clone `MarioFireball.cs:50`）。
                    ny = _hitTileY + 1f + Size.y * 0.5f;
                    _vel.y = GameConst.FireballVelocityY;
                }
                else
                {
                    // 撞顶 ⇒ 压回向下，速度 = `-absVelocity.y`（clone `MarioFireball.cs:52`）。
                    ny = _hitTileY - Size.y * 0.5f;
                    _vel.y = -GameConst.FireballVelocityY;
                }
            }

            transform.position = new Vector3(nx, ny, 0f);
            RecomputeBounds();

            _anim?.Advance(dt);

            if (transform.position.y < -12f) Finished = true;
        }

        public void Explode()
        {
            if (_exploding || Finished) return;
            _exploding = true;
            Dead = true;
            _boomTimer = 0f;
            _vel = Vector2.zero;
            // 先停掉飞行帧动画：不停的话下一次 `Advance` 会把爆炸第 0 帧覆盖回火球贴图
            // （引擎 `Stop()` 保留当前帧的画面，只是不再推进）。
            _anim?.Stop();
            if (_boom.Count > 0 && _sr != null) _sr.sprite = _boom[0];
        }

        public void Clear()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        // ───────── 逐格扫描（收敛到引擎 GridUtil）─────────
        //
        // 与 PlayerActor / ItemModule / EnemyModule 三处**逐字相同** ⇒ 已下沉为
        // `CloverEngine.GridUtil`（出处与逐字复刻的口径见 `Runtime/Core/GridUtil.cs` 文件头：
        // `xMin = FloorToInt(r.xMin)`、`xMax = FloorToInt(r.xMax - 0.0001f)`、y 外层 / x 内层**升序**，
        // 那个 `- 0.0001f` 收边量即 `GridUtil.EdgeEpsilon`）。
        //
        // 用 `ForEach` 而不是迭代器 `GridUtil.Enumerate`（后者每次调用都分配），并且把委托
        //    **缓存到字段** —— 引擎文件头 GC 写明「方法组写法在 Unity 的 C# 9 下每次转换也分配一个
        //    委托」；状态也放字段 ⇒ 回调不捕获局部变量、整条火球热路径零分配。
        // 算法一字未动：仍是"X 撞墙即炸 / Y 落地反弹"，仍是"命中第一格就停"。

        /// <summary>缓存的逐格回调（热路径不分配，见上）。</summary>
        private Action<int, int> _onScanTile;

        private ILevel _scanLevel;
        private bool _scanHit;
        private int _hitTileX;
        private int _hitTileY;

        /// <summary>
        /// </summary>
        private bool ScanFirstSolid(Rect r)
        {
            _scanLevel = _level;
            _scanHit = false;
            _hitTileX = 0;
            _hitTileY = 0;
            _onScanTile ??= OnScanTile;                // 委托缓存到字段（引擎 GridUtil 文件头 ★ GC 的要求）
            GridUtil.ForEach(r, _onScanTile);
            return _scanHit;
        }

        private void OnScanTile(int tx, int ty)
        {
            if (_scanHit) return;                      // = 原 `break`
            if (!_scanLevel.IsSolidTile(tx, ty)) return;
            _scanHit = true;
            _hitTileX = tx;
            _hitTileY = ty;
        }
    }
}
