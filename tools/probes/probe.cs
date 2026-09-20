// 一次性实机驱动探针（用完即删，见全局 skill §1.8）。
// 用法：unity command run_script --file <本文件> --entry Probe.<场景>
//   ⚠️ 入口是 void 且**立刻返回**（fire-and-forget）：run_script 的传输层 30 秒超时，
//   而一个场景要跑十几秒到几十秒。场景自己往游戏日志里写 "场景结束：<name>" 当完成标记。
//
// 按键注入必须走 Game.AttachInput 桩（验收表 E-11）：本机编辑器窗口最小化 + 管理员权限，
// OS 级注入在引擎 Tick 相位读不到。除按键这一层外，跑的都是原代码。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CloverEngine;
using SuperMario.Def;
using SuperMario.Module.Flow;
using SuperMario.Module.Player;
using UnityEngine;

public static class Probe
{
    // ★ 取证截图是**一次性产物**（skill §1.8 / `reference/workflow-and-standards.md`「临时文件」）：
    //   落 `<项目根>/.ai-tmp/screenshots/`，跟 `.ai-tmp/` 一起被 gitignore，⛔ 不进 `client/Assets/**`。
    //   原来写的是 "Assets/Screenshots" —— 197 张图进了游戏工程 = 6.4 MB，交付前才清理，
    //   还得回头改验收表 18 处引用（用户 2026-09-20 点名）。跑 Unity 时 cwd = client/ ⇒ 用 "../"。
    private const string ShotDir = "../.ai-tmp/screenshots";

    public static void L(string m) => Game.Logger.Info("Probe", m);

    // ───────────────────── 输入桩 ─────────────────────

    private sealed class Stub : IInputManager
    {
        private readonly IInputManager _real;
        private readonly HashSet<GameKey> _held = new HashSet<GameKey>();

        // ★ 边沿（GetKeyDown / GetKeyUp）语义必须与【真后端】一致：真管理器的
        //   `GetKeyDown` = `!IsLocked && _backend.GetKeyDown`，后端落到 Unity 的
        //   `Input.GetKeyDown` / `wasPressedThisFrame` —— **只在按下那一帧为 true**。
        //
        //   原实现把"按下过的键"永远留在 `_down` 里（只 Add、从不 Clear）⇒ 一次按下之后
        //   `GetKeyDown(该键)` **恒为 true**。后果（2026-09-18 实测，两个都是它）：
        //     · `AppFlow.TickResult` 每帧都判"按了 SPACE"⇒ `→ Result` 之后 **14 毫秒**就
        //       `CloseAll()` + 反复 `Scene.Load(Menu)`（日志里 `Loading scene: Menu` 每帧一条）
        //       ⇒ 结算面板被销毁、关卡会话被释放 ⇒ 连拍 6 秒全是纯色（登记为 E-17 的那个"缺陷"）。
        //     · `TickStage`/`TickPause` 每帧互相切换 ⇒ `→ Pause` 在 0.55 秒里刷 40 条，
        //       面板每帧被建又被销毁 ⇒ 抓拍/注册表查询随机落在有/无面板的那一帧（E-18）。
        //   修法：Hold 只写 pending，由**每帧的 Tick()** 提升成本帧边沿、下一帧清掉。
        //   `Game.Tick` 的顺序是 `Input.Tick()` → `Timer` → `Fsm.Tick()`（Game.cs:709-712），
        //   所以本帧提升的边沿本帧就能被状态机读到 —— 与真后端逐帧语义相同。
        private readonly HashSet<GameKey> _down = new HashSet<GameKey>();
        private readonly HashSet<GameKey> _up = new HashSet<GameKey>();
        private readonly HashSet<GameKey> _downPending = new HashSet<GameKey>();
        private readonly HashSet<GameKey> _upPending = new HashSet<GameKey>();

        public Stub(IInputManager real) { _real = real; }

        public void Hold(GameKey k, bool v)
        {
            if (v)
            {
                if (!_held.Contains(k)) _downPending.Add(k);
                _held.Add(k);
            }
            else
            {
                if (_held.Contains(k)) _upPending.Add(k);
                _held.Remove(k);
            }
            L($"PROBE key {k} {(v ? "down" : "up")}");
        }

        public InputState State => _real?.State;
        public bool Available => _real == null || _real.Available;
        public string BackendName => "Probe+" + (_real == null ? "None" : _real.BackendName);
        public bool IsLocked => _real != null && _real.IsLocked;
        public bool HasTouch => _real != null && _real.HasTouch;
        public void Lock() { _real?.Lock(); }
        public void Unlock() { _real?.Unlock(); }
        // `!IsLocked` 前置与真管理器对齐（Input.cs:780-782：锁定时"所有读取返回默认值"）。
        public bool GetKey(GameKey key)
            => !IsLocked && (_held.Contains(key) || (_real != null && _real.GetKey(key)));
        public bool GetKeyDown(GameKey key)
            => !IsLocked && (_down.Contains(key) || (_real != null && _real.GetKeyDown(key)));
        public bool GetKeyUp(GameKey key)
            => !IsLocked && (_up.Contains(key) || (_real != null && _real.GetKeyUp(key)));
        public bool GetMouseButton(int b) => _real != null && _real.GetMouseButton(b);
        public bool GetMouseButtonDown(int b) => _real != null && _real.GetMouseButtonDown(b);
        public bool GetMouseButtonUp(int b) => _real != null && _real.GetMouseButtonUp(b);
        public Vector3 MousePosition => _real?.MousePosition ?? Vector3.zero;
        public Vector2 MouseDelta => _real?.MouseDelta ?? Vector2.zero;
        public float GetAxis(string axis, bool raw = false) => _real != null ? _real.GetAxis(axis, raw) : 0f;
        public void OnMove(Action<Vector2> h) { }
        public void OffMove(Action<Vector2> h) { }
        public void OnSkill(int i, Action h) { }
        public void OffSkill(int i, Action h) { }
        public void OnJump(Action h) { }
        public void OffJump(Action h) { }
        public void OnDodge(Action h) { }
        public void OffDodge(Action h) { }
        public void OnInteract(Action h) { }
        public void OffInteract(Action h) { }
        public void EnsureEventSystem() { _real?.EnsureEventSystem(); }

        /// <summary>
        /// 每帧把 pending 边沿提升为"本帧边沿"、并清掉上一帧的边沿（见字段处的说明）。
        /// 同时把 Tick 转发给真管理器：不转发的话真后端的每帧状态刷新（失焦复位等）在探针期间是停的，
        /// 与"除按键这一层外跑的都是原代码"不符。
        /// </summary>
        public void Tick()
        {
            _down.Clear();
            _up.Clear();
            foreach (var k in _downPending) _down.Add(k);
            _downPending.Clear();
            foreach (var k in _upPending) _up.Add(k);
            _upPending.Clear();
            _real?.Tick();
        }
    }

    private static Stub _stub;
    private static IInputManager _real;

    private static void AttachStub()
    {
        _real = Game.Input;
        _stub = new Stub(_real);
        Game.AttachInput(_stub);
        L($"PROBE 输入桩已接管（真后端={(_real == null ? "null" : _real.BackendName)}）");
    }

    private static void DetachStub()
    {
        if (_real != null) Game.AttachInput(_real);
        L("PROBE 已还原真输入管理器");
    }

    // ───────────────────── 基础工具 ─────────────────────

    private static async Task Wait(float seconds)
    {
        var ms = (int)Math.Round(seconds * 1000.0);
        if (ms > 0) await Task.Delay(ms);
    }

    private static bool IsStage => Game.Fsm != null && Game.Fsm.Current == "Stage";

    private static string FsmNow => Game.Fsm == null ? "null" : Game.Fsm.Current;

    /// <summary>等状态机到达 Stage 且玩家/关卡就绪。</summary>
    private static async Task<bool> WaitStage(float timeout = 25f)
    {
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            if (IsStage && StageContext.Player != null && StageContext.Level != null && StageContext.Level.Ready)
                return true;
            await Task.Delay(120);
        }
        L($"PROBE 警告：等待进入关卡超时（{timeout}s），当前状态={FsmNow}");
        return false;
    }

    private static void Shot(string name)
    {
        var path = ShotDir + "/" + name + ".png";
        try
        {
            System.IO.Directory.CreateDirectory(ShotDir);   // 目录不存在时 WriteAllBytes 直接抛异常
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex == null) { L($"PROBE 截图失败（纹理为 null）：{path}"); return; }
            var bytes = tex.EncodeToPNG();
            var tw = tex.width; var th = tex.height;
            UnityEngine.Object.Destroy(tex);
            File.WriteAllBytes(path, bytes);
            // 机位一并落盘：离线脚本要靠 (camX, camY, 正交半高) 才能把像素换算回世界格
            // （判定由脚本做，见 skill §1.12 第 1 条）。
            var c = Camera.main;
            L($"PROBE shot {path} 字节={bytes.Length} 分辨率={tw}x{th} " +
              $"机位={(c == null ? "无相机" : $"x={c.transform.position.x:F2} y={c.transform.position.y:F2} 正交半高={c.orthographicSize:F2}")}");
        }
        catch (Exception e)
        {
            L($"PROBE 截图异常：{path} {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>整屏是不是单色（用于识别"面板还没画出来"的退化帧）。</summary>
    private static bool IsFlat(Texture2D tex)
    {
        var c0 = tex.GetPixel(0, 0);
        var sx = Mathf.Max(1, tex.width / 20);
        var sy = Mathf.Max(1, tex.height / 20);
        for (var y = 0; y < tex.height; y += sy)
            for (var x = 0; x < tex.width; x += sx)
            {
                var c = tex.GetPixel(x, y);
                if (Mathf.Abs(c.r - c0.r) + Mathf.Abs(c.g - c0.g) + Mathf.Abs(c.b - c0.b) > 0.02f) return false;
            }
        return true;
    }

    /// <summary>
    /// 全屏颜色统计（唯一色数 / 主色 / 主色占比）。
    /// <para>
    /// 为什么不用 <see cref="IsFlat"/> 判"面板到底画没画"：它是 20x20 的**稀疏网格**采样，
    /// 字号 48 的文字只有约 25 像素高、笔画宽几像素 —— 网格很可能整个跳过文字，
    /// 于是"黑底 + 一行字"的结算屏会被判成"退化帧"（本工程结算屏本来就是 0.95 黑底，见 ResultPanel.Awake）。
    /// 这里扫全图，判据是**唯一色数**：纯色帧 = 1，有内容的帧远大于 1。
    /// ⚠️ 这里只是探针侧的**粗判**（决定要不要重拍）；正式判定由离线脚本对 PNG 做（§1.12 第 1 条）。
    /// </para>
    /// </summary>
    private static void ColorStats(Texture2D tex, out int unique, out Color32 modal, out float modalFrac)
    {
        var px = tex.GetPixels32();
        var hist = new Dictionary<int, int>();
        for (var i = 0; i < px.Length; i++)
        {
            var key = (px[i].r << 16) | (px[i].g << 8) | px[i].b;
            hist.TryGetValue(key, out var n);
            hist[key] = n + 1;
        }
        var best = 0;
        var bestKey = 0;
        foreach (var kv in hist)
            if (kv.Value > best) { best = kv.Value; bestKey = kv.Key; }
        unique = hist.Count;
        modal = new Color32((byte)((bestKey >> 16) & 0xFF), (byte)((bestKey >> 8) & 0xFF), (byte)(bestKey & 0xFF), 255);
        modalFrac = px.Length == 0 ? 0f : (float)best / px.Length;
    }

    /// <summary>
    /// 截图，并以"**唯一色数 ≥ <paramref name="minUnique"/>**"为完成判据重试
    /// （纯色帧 = 面板还没画出来的退化帧，见 <see cref="ColorStats"/>）。
    /// </summary>
    private static async Task<bool> ShotOk(string name, int minUnique = 3, float timeout = 6f)
    {
        var path = ShotDir + "/" + name + ".png";
        var t0 = Time.realtimeSinceStartup;
        var tries = 0;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            tries++;
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) { L($"PROBE 截图失败（纹理为 null）：{path}"); return false; }
                ColorStats(tex, out var unique, out var modal, out var frac);
                var bytes = tex.EncodeToPNG();
                UnityEngine.Object.Destroy(tex);
                File.WriteAllBytes(path, bytes);
                L($"PROBE shot {path} 字节={bytes.Length} 唯一色={unique} 主色=({modal.r},{modal.g},{modal.b}) " +
                  $"主色占比={frac:F4} 退化帧={unique < minUnique} 第{tries}次");
                if (unique >= minUnique) return true;
            }
            catch (Exception e)
            {
                L($"PROBE 截图异常：{path} {e.GetType().Name}: {e.Message}");
                return false;
            }
            await Wait(0.5f);
        }
        L($"PROBE 警告：{name} 在 {timeout} 秒内始终退化（唯一色 < {minUnique}）—— 面板可能真的没画");
        return false;
    }

    /// <summary>
    /// 截图，并**确认不是退化帧**：切场景/冷加载后的头几帧会是"整屏单色"（面板还没画），
    /// 直接当证据就等于把空帧记成"一致"。实测踩过：会话内**第一次**进菜单拍到单色，
    /// 第二次进菜单才有内容。所以这里单色就等 0.5 秒重拍，直到有内容或超时。
    /// </summary>
    private static async Task<bool> ShotSolid(string name, float timeout = 6f)
    {
        var path = ShotDir + "/" + name + ".png";
        var t0 = Time.realtimeSinceStartup;
        var tries = 0;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            tries++;
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) { L($"PROBE 截图失败（纹理为 null）：{path}"); return false; }
                var flat = IsFlat(tex);
                var bytes = tex.EncodeToPNG();
                UnityEngine.Object.Destroy(tex);
                File.WriteAllBytes(path, bytes);
                L($"PROBE shot {path} 字节={bytes.Length} 退化帧={flat} 第{tries}次");
                if (!flat) return true;
            }
            catch (Exception e)
            {
                L($"PROBE 截图异常：{path} {e.GetType().Name}: {e.Message}");
                return false;
            }
            await Wait(0.5f);
        }
        L($"PROBE 警告：{name} 在 {timeout} 秒内一直是退化帧（面板可能真的没画）");
        return false;
    }

    // ───────────────────── 出图闸门 + 运行时读数（2026-09-19 任务书-修取证缺陷）─────────────────────
    //
    // 为什么加（两处实测缺陷，都不是"拍歪了"，是"拍了不该拍的屏"）：
    //   ① `menu` 场景那次在 `fsm=Loading` 时启动 ⇒ 拍出 12 张**入场卡**当标题屏，并且**覆盖**了
    //      原来的好帧（旧口径只看"是不是单色"，入场卡有字 ⇒ 判"非退化" ⇒ 照样落盘）。
    //   ② `reshoot` 场景里马里奥被乌龟撞死 ⇒ 整关重开 ⇒ 后面 5 张图是重开后的画面（机位 x=0.00）。
    // 所以出图前先判**状态/机位/读数**（由调用方给），不过就**一个字节都不写** —— 坏帧再也覆盖不了好帧。

    /// <summary>HUD 四栏的**运行时**读数（离线判据"HUD 读数与行一致"的来源）。
    /// 读活节点树上的 Text，不是"我记得设成了几"。面板没开 ⇒ 明确写出来（标题屏本来就没有 HUD 面板）。</summary>
    private static string HudReadout()
    {
        var p = Game.UI.Get<SuperMario.UI.HudPanel>();
        if (p == null || p.Root == null) return "HUD=-（HudPanel 没开）";
        var sb = new System.Text.StringBuilder();
        foreach (var n in new[] { "MarioCap", "Score", "Coins", "WorldCap", "World", "TimeCap", "Time" })
        {
            var tr = p.Root.transform.Find("Bar/" + n);
            var t = tr == null ? null : tr.GetComponent<UnityEngine.UI.Text>();
            sb.Append(n + "=" + (t == null ? "缺" : "\"" + t.text + "\"") + " ");
        }
        return "HUD " + sb.ToString().TrimEnd();
    }

    private static string HudNode(string node)
    {
        var p = Game.UI.Get<SuperMario.UI.HudPanel>();
        if (p == null || p.Root == null) return "-";
        var tr = p.Root.transform.Find("Bar/" + node);
        var t = tr == null ? null : tr.GetComponent<UnityEngine.UI.Text>();
        return t == null ? "-" : t.text;
    }

    private static bool StageAlive()
        => FsmNow == "Stage" && StageContext.Player != null && StageContext.Player.Alive && StageContext.Level != null;

    private static float PlayerX() => StageContext.Player == null ? 9999f : StageContext.Player.FeetPosition.x;

    private static bool NearX(float x, float tol = 1.5f) => Mathf.Abs(PlayerX() - x) <= tol;

    /// <summary>标题屏就绪判据：fsm=Menu 且关卡会话已释放（12:28 那次就是缺这条闸门）。</summary>
    private static bool MenuGate()
        => FsmNow == "Menu" && StageContext.Level == null && StageContext.Player == null;

    /// <summary>
    /// **出图闸门**：① 调用方给的 <paramref name="ok"/>（状态 / 机位 / HUD 读数）；
    /// ② 唯一色 ≥ <paramref name="minUnique"/>（防退化帧）。两条都过才写盘；
    /// 不过 ⇒ 不写盘，并把"为什么拒"打进日志（拒了就要能看见，不许静默）。
    /// </summary>
    private static async Task<bool> ShotGated(string name, string tag, Func<bool> ok, int minUnique = 3)
    {
        var path = ShotDir + "/" + name + ".png";
        var p = StageContext.Player;
        var c = Camera.main;
        var cam = c == null
            ? "无相机"
            : $"x={c.transform.position.x:F2} y={c.transform.position.y:F2} 正交半高={c.orthographicSize:F2}";
        var gate = ok();
        L($"PROBE gate[{tag}] {name}: fsm={FsmNow} level={StageContext.LevelPath} 玩家={PosStr(p)} " +
          $"存活={(p != null && p.Alive)} 机位={cam} {HudReadout()} ⇒ " +
          (gate ? "放行" : "拒绝出图（状态/机位/读数对不上，图会假 ⇒ 一个字节都不写）"));
        if (!gate) return false;
        var t0 = Time.realtimeSinceStartup;
        var tries = 0;
        while (Time.realtimeSinceStartup - t0 < 6f)
        {
            tries++;
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) { L($"PROBE 截图失败（纹理为 null）：{path}"); return false; }
                var tw = tex.width; var th = tex.height;
                ColorStats(tex, out var unique, out var modal, out var frac);
                var bytes = tex.EncodeToPNG();
                UnityEngine.Object.Destroy(tex);
                if (unique >= minUnique)
                {
                    File.WriteAllBytes(path, bytes);
                    L($"PROBE shot {path} 字节={bytes.Length} 分辨率={tw}x{th} 唯一色={unique} " +
                      $"主色=({modal.r},{modal.g},{modal.b}) 主色占比={frac:F4} 机位={cam} 第{tries}次");
                    return true;
                }
                L($"PROBE shot(不写盘) {path} 唯一色={unique} < {minUnique} ⇒ 退化帧（坏帧不覆盖好帧）第{tries}次");
            }
            catch (Exception e)
            {
                L($"PROBE 截图异常：{path} {e.GetType().Name}: {e.Message}");
                return false;
            }
            await Wait(0.4f);
        }
        L($"PROBE 警告：{name} 在 6 秒内始终退化（唯一色 < {minUnique}）⇒ 未写盘（旧文件保持原样）");
        return false;
    }

    /// <summary>
    /// 把马里奥摆回**地面**并等到"站在地上"再返回。每次起跳前都要重摆 —— 一跳之后人会落到
    /// 砖排顶上（实测 18:32：脚 y=5.15），后面几次就变成"在砖顶上跳空气"，块一次没顶到
    /// （那一批的 `14-multicoin-after10.png` 因此是假图：币恒 2）。
    /// </summary>
    private static async Task TeleportGround(float x, string tag)
    {
        var lv = StageContext.Level;
        var y = lv == null ? -3f : lv.GroundTopY;
        for (var i = 1; i <= 3; i++)
        {
            Teleport(x, y);
            await Wait(0.6f);
            var p = StageContext.Player;
            if (p != null && p.Alive && p.Grounded && Mathf.Abs(p.FeetPosition.x - x) < 0.3f) return;
            L($"PROBE 重摆[{tag}] 第 {i} 次没到位：脚={PosStr(StageContext.Player)} " +
              $"grounded={(p != null && p.Grounded)} alive={(p != null && p.Alive)}");
        }
    }

    /// <summary>
    /// 把 <paramref name="x"/> 附近的栗宝宝**直接清掉**（`IEnemy.Flip()` = 撞飞 ⇒ 立刻 Dead、不参与判定），
    /// 返回清掉的个数。
    /// <para>
    /// ⚠️ 为什么不用旧的 <see cref="ClearGoombasNear"/>：那条路是"把马里奥摆到栗宝宝**头上**去踩"，
    /// 踩的过程中他自己会贴到别的敌人 ⇒ 实测 2026-09-19 17:07：清场时被降级 Big→Small、两秒后**死亡**，
    /// 于是 #48 的连顶直接崩掉（币只到 2）。这里改成"人不动、敌人被撞飞"，一个接触都不发生。
    /// </para>
    /// </summary>
    private static int KillGoombasNear(float x, float range)
    {
        var n = 0;
        foreach (var mb in AllEnemies())
        {
            if (mb == null) continue;
            if (!mb.gameObject.name.StartsWith("Goomba", StringComparison.Ordinal)) continue;   // 乌龟要留着（#46）
            if (!(mb is SuperMario.Module.Entities.IEnemy e) || e.Dead) continue;
            if (Mathf.Abs(P(mb).x - x) > range) continue;
            e.Flip();
            n++;
        }
        L($"PROBE 清场：撞飞 {n} 只栗宝宝（x∈[{x - range:F1},{x + range:F1}]，人不接触）");
        return n;
    }

    /// <summary>场上有没有"无敌星道具"（`StarItem` 是 internal 类 ⇒ 按类型名找）。</summary>
    private static bool StarItemExists()
    {
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            if (mb != null && mb.GetType().Name == "StarItem") return true;
        return false;
    }

    /// <summary>
    /// 在 <paramref name="want"/> 附近找一格"**脚下有地板**、身上没有实心格"的落点并返回它的 x。
    /// <para>
    /// 为什么必须有：`Teleport(x, y)` 只写坐标、不看地形 —— 摆到坑的上方，人**直接掉出世界**
    /// （实测 2026-09-19 17:01：`tp → (87.94,-3.00)` 之后立刻 `死亡`，随后 fsm=Loading，后面几张图全废）。
    /// 1-1 的地板顶 = y=-3 ⇒ 实心格在 y=-4（离线查法：`.ai-tmp/test/ground_gaps_11.py`）。
    /// 判据用关卡自己的实心位图（`ILevel.IsSolidTile`），不是我们的猜测。
    /// </para>
    /// </summary>
    private static float SafeGroundX(float want, float y)
    {
        var lv = StageContext.Level;
        if (lv == null) return want;
        var cy = Mathf.FloorToInt(y);
        for (var d = 0; d <= 8; d++)
            foreach (var s in new[] { 1f, -1f })
            {
                var x = want + d * s;
                var cx = Mathf.FloorToInt(x);
                if (lv.IsSolidTile(cx, cy - 1) && !lv.IsSolidTile(cx, cy) && !lv.IsSolidTile(cx, cy + 1)) return x;
            }
        L($"PROBE 警告：SafeGroundX 在 {want:F2} 附近 8 格内没找到可靠落点 ⇒ 原样用 {want:F2}（脚下可能是坑）");
        return want;
    }

    /// <summary>
    /// 等到"关卡 + 玩家都处于可拍状态"（死了 / 正在重开就等它重开完）。**要等就说明出了意外**
    /// （正常路径不该死），所以等待这件事本身也打进日志 —— 非预期分支必须留痕（skill §7）。
    /// </summary>
    private static async Task<bool> EnsureStage(string tag)
    {
        if (StageAlive()) return true;
        L($"PROBE 警告[{tag}]：关卡/玩家不在可拍状态（fsm={FsmNow} 玩家={PosStr(StageContext.Player)} " +
          $"存活={(StageContext.Player != null && StageContext.Player.Alive)} 命={StageContext.Score?.Lives}）⇒ 等它重开完");
        for (var i = 0; i < 60 && !StageAlive(); i++) await Wait(0.5f);
        L($"PROBE 等关卡[{tag}] 结束：fsm={FsmNow} 玩家={PosStr(StageContext.Player)} 存活={(StageContext.Player != null && StageContext.Player.Alive)}");
        return StageAlive();
    }

    /// <summary>
    /// 抢「面板 / 入场卡**确实画在屏幕上**」的帧：判据 = **深色像素占比 ≥ minDark**
    /// （结算屏 = 0.95 黑遮罩、入场卡 = 纯黑底）。
    /// <para>
    /// ⚠️ 为什么不能用 <see cref="ShotSolid"/>：它用 <see cref="IsFlat"/> 的 20x20 **稀疏网格**判"空帧"，
    /// 而"黑底 + 一行字"的卡/结算屏正好会被整个网格跳过 ⇒ 被判成空帧 ⇒ 它继续重拍 ——
    /// **每次都覆盖同名文件**，最后留下的是"卡已经过去、关卡画面出来了"的那一帧。
    /// 实测：`intro_card.png` / `intro_lives_check.png` 一直都是**关卡画面**，表里却写着"黑底 + WORLD 1-1 + ×3"。
    /// 换成"大面积深色"这个判据后，它和"面板画没画"是一一对应的，且第一次就命中（不会覆盖掉正确帧）。
    /// </para>
    /// </summary>
    private static async Task<bool> ShotPanel(string name, float minDark = 0.5f, float timeout = 6f)
    {
        var path = ShotDir + "/" + name + ".png";
        var t0 = Time.realtimeSinceStartup;
        var tries = 0;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            tries++;
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) { L($"PROBE 截图失败（纹理为 null）：{path}"); return false; }
                var px = tex.GetPixels32();
                var dark = 0;
                for (var i = 0; i < px.Length; i++)
                    if (px[i].r + px[i].g + px[i].b < 120) dark++;
                var frac = px.Length == 0 ? 0f : (float)dark / px.Length;
                var bytes = tex.EncodeToPNG();
                UnityEngine.Object.Destroy(tex);
                if (frac >= minDark)
                {
                    File.WriteAllBytes(path, bytes);
                    L($"PROBE shot {path} 字节={bytes.Length} 深色占比={frac:F3} 达标=True 第{tries}次");
                    return true;
                }
                L($"PROBE shot(跳过) {path} 字节={bytes.Length} 深色占比={frac:F3} 达标=False 第{tries}次");
            }
            catch (Exception e)
            {
                L($"PROBE 截图异常：{path} {e.GetType().Name}: {e.Message}");
                return false;
            }
            await Wait(0.2f);
        }
        L($"PROBE 警告：{name} 在 {timeout} 秒内没有出现「大面积深色」⇒ 面板/入场卡可能真的没画");
        return false;
    }

    private static string PosStr(IPlayer p) =>
        p == null ? "null" : $"({p.FeetPosition.x:F2},{p.FeetPosition.y:F2})";

    /// <summary>
    /// 玩家碰撞盒（**内缩 0.05**）有没有压到实心格 —— 即"身处实心格"。
    /// <para>
    /// 内缩是必须的：站在地面上时脚底正好等于地面顶边，"盒子是否与实心格相交"会把
    /// 【贴着面站】算成【嵌在里面】（假阳性）。判据用的是关卡自己的实心位图
    /// （<c>ILevel.IsSolidTile</c>），不是我们的猜测。
    /// </para>
    /// </summary>
    private static bool InSolid()
    {
        var p = StageContext.Player;
        var lv = StageContext.Level;
        if (p == null || lv == null) return false;
        var b = p.Bounds;
        const float eps = 0.05f;
        var x0 = Mathf.FloorToInt(b.xMin + eps);
        var x1 = Mathf.FloorToInt(b.xMax - eps);
        var y0 = Mathf.FloorToInt(b.yMin + eps);
        var y1 = Mathf.FloorToInt(b.yMax - eps);
        for (var x = x0; x <= x1; x++)
            for (var y = y0; y <= y1; y++)
                if (lv.IsSolidTile(x, y)) return true;
        return false;
    }

    private static void State(string tag)
    {
        var p = StageContext.Player;
        var s = StageContext.Score;
        var lv = StageContext.Level;
        var pb = p == null ? "null" : $"[{p.Bounds.xMin:F2},{p.Bounds.xMax:F2}]x[{p.Bounds.yMin:F2},{p.Bounds.yMax:F2}]";
        L($"PROBE state[{tag}] stage={IsStage} fsm={FsmNow} level={StageContext.LevelPath} sub={StageContext.SubArea} " +
          $"under={StageContext.Underground} 分={s?.Points} 币={s?.Coins} 命={s?.Lives} 时={s?.TimeLeft} | " +
          $"pos={PosStr(p)} vel={(p == null ? "null" : $"({p.Velocity.x:F2},{p.Velocity.y:F2})")} box={pb} " +
          $"grounded={(p != null && p.Grounded)} alive={(p != null && p.Alive)} busy={(p != null && p.Busy)} " +
          $"power={(p == null ? "?" : p.Power.ToString())} star={(p != null && p.StarInvincible)} " +
          $"groundTop={(lv == null ? 0f : lv.GroundTopY)} worldLabel={StageContext.WorldLabel} " +
          $"身处实心格={InSolid()} sideExit=({(lv == null || !lv.HasSideExit ? "无" : $"{lv.SideExitFaceX},{lv.SideExitY}")})");
    }

    /// <summary>
    /// 读**真实在播的 AudioSource**（不问业务层"你刚才让我放了什么"）：clip 名 / isPlaying / loop / 音量。
    /// 验收表 #30 与 E-9（密室地下主题）的判据就是这一行 —— 引擎的 SoundManager 内部持有这些音源，
    /// 所以用 FindObjectsByType 把它们捞出来读。
    /// </summary>
    private static string BgmState()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
        {
            if (s == null || s.clip == null) continue;
            sb.Append($"[{s.clip.name} playing={s.isPlaying} loop={s.loop} vol={s.volume:F2}] ");
        }
        return sb.Length == 0 ? "（无带 clip 的音源）" : sb.ToString().TrimEnd();
    }

    private static List<MonoBehaviour> AllEnemies()
    {
        var list = new List<MonoBehaviour>();
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb == null) continue;
            if (mb is SuperMario.Module.Entities.IEnemy) list.Add(mb);
        }
        return list;
    }

    private static void DumpEnemies(string tag)
    {
        var lines = new List<string>();
        foreach (var mb in AllEnemies())
        {
            if (!(mb is SuperMario.Module.Entities.IEnemy e)) continue;
            var t = mb.transform;
            var b = e.Bounds;
            lines.Add($"{mb.gameObject.name}@{t.position.x:F2}/({t.position.y:F2}) " +
                      $"box=[{b.xMin:F2},{b.xMax:F2}]x[{b.yMin:F2},{b.yMax:F2}] dead={e.Dead}");
        }
        L($"PROBE enemies[{tag}] 共 {lines.Count} 个：{string.Join(" | ", lines)}");
    }

    private static void DumpArt(string tag, GameObject go)
    {
        if (go == null) { L($"PROBE art[{tag}] 对象为空"); return; }
        var lines = new List<string>();
        lines.Add($"根={go.name} active={go.activeInHierarchy} worldPos={go.transform.position}");
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            var sr = t.GetComponent<SpriteRenderer>();
            var extra = sr == null
                ? "无SpriteRenderer"
                : $"sprite={(sr.sprite == null ? "null" : sr.sprite.name)} " +
                  $"spriteSize={(sr.sprite == null ? Vector2.zero : (Vector2)sr.sprite.bounds.size)} " +
                  $"order={sr.sortingOrder} enabled={sr.enabled} flipX={sr.flipX} flipY={sr.flipY} " +
                  $"worldCenter={(sr.sprite == null ? Vector3.zero : sr.bounds.center)} " +
                  $"worldSize={(sr.sprite == null ? Vector3.zero : sr.bounds.size)}";
            lines.Add($"[{t.name} local={t.localPosition} {extra}]");
        }
        L($"PROBE art[{tag}] {string.Join(" ", lines)}");
    }

    private static MonoBehaviour FindEnemy(string prefix, float nearX)
    {
        MonoBehaviour best = null;
        var bestD = float.MaxValue;
        foreach (var mb in AllEnemies())
        {
            if (!mb.gameObject.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var d = Mathf.Abs(mb.transform.position.x - nearX);
            if (d < bestD) { bestD = d; best = mb; }
        }
        return best;
    }

    private static IEnumerable<MonoBehaviour> AllByName(string name)
    {
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb != null && mb.gameObject.name == name) yield return mb;
        }
    }

    private static MonoBehaviour FindByName(string name, float nearX)
    {
        MonoBehaviour best = null;
        var bestD = float.MaxValue;
        foreach (var mb in AllByName(name))
        {
            var d = Mathf.Abs(mb.transform.position.x - nearX);
            if (d < bestD) { bestD = d; best = mb; }
        }
        return best;
    }

    private static Vector3 P(MonoBehaviour mb) => mb == null ? Vector3.zero : mb.transform.position;

    private static void Teleport(float x, float y)
    {
        var p = StageContext.Player;
        if (p == null) { L("PROBE 传送失败：玩家不存在"); return; }
        p.Teleport(new Vector2(x, y));
        L($"PROBE tp → ({x:F2},{y:F2})");
    }

    private static void CallFlow(string method, params object[] args)
    {
        var boots = UnityEngine.Object.FindObjectsByType<SuperMario.App.Bootstrap>(FindObjectsInactive.Include);
        if (boots == null || boots.Length == 0) { L("PROBE 反射失败：找不到 Bootstrap"); return; }
        var f = typeof(SuperMario.App.Bootstrap).GetField("_flow", BindingFlags.NonPublic | BindingFlags.Instance);
        var flow = f?.GetValue(boots[0]);
        if (flow == null) { L($"PROBE 反射失败：拿不到 AppFlow（要调 {method}）"); return; }
        var m = flow.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) { L($"PROBE 反射失败：AppFlow 上没有 {method}"); return; }

        // ★ 目标方法带【可选参数】时（例如 `NextLevel(bool silent = false)`），
        //   反射传空数组会抛 TargetParameterCountException（实测 2026-09-20：改了签名之后
        //   `Enter12()` 立刻在这里炸，而报错只写"参数个数不匹配"，看着像脚手架坏了）。
        //   这里按声明把默认值补上，签名再变也不会哑掉。
        var ps = m.GetParameters();
        if (args.Length != ps.Length)
        {
            if (args.Length != 0)
            {
                L($"PROBE 反射失败：{method} 参数个数不匹配（给了 {args.Length}、要 {ps.Length}）");
                return;
            }
            var filled = new object[ps.Length];
            for (var i = 0; i < ps.Length; i++)
                filled[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
            args = filled;
        }
        m.Invoke(flow, args);
        L($"PROBE 已调用 AppFlow.{method}（{ps.Length} 个参数）");
    }

    // ───────────────────── 场景包装（fire-and-forget）─────────────────────

    private static void Start(string name, Func<Task> body)
    {
        L($"PROBE ==== 场景开始：{name} ====");
        // 截图/报告目录先建好：`File.WriteAllBytes` / `WriteAllText` **不会**自建目录，
        // 目录不在就抛异常（而异常只进"场景异常"日志、不算 [Error]，驱动会当成 0 错误通过）。
        System.IO.Directory.CreateDirectory(ShotDir);
        var task = Run(name, body);
    }

    private static async Task Run(string name, Func<Task> body)
    {
        AttachStub();
        try
        {
            await body();
        }
        catch (Exception e)
        {
            L($"PROBE 场景异常：{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            DetachStub();
            L($"PROBE ==== 场景结束：{name} ====");
        }
    }

    // ───────────────────── 进关 ─────────────────────

    /// <summary>先回到主菜单（场景可重入：不在 Play 里重开也能连着跑多个场景）。</summary>
    private static async Task ToMenu()
    {
        var t0 = Time.realtimeSinceStartup;
        // Boot 会自己走到 Menu（约 7 秒），先等它。
        while (FsmNow == "Boot" && Time.realtimeSinceStartup - t0 < 15f) await Task.Delay(150);
        if (FsmNow == "Menu") { L("PROBE 已在主菜单"); return; }
        Game.Event.Emit(SuperMario.Core.Events.BackToMain);
        L($"PROBE 已发 BackToMain（当时 fsm={FsmNow}）");
        while (FsmNow != "Menu" && Time.realtimeSinceStartup - t0 < 25f) await Task.Delay(150);
        L($"PROBE 回到主菜单：fsm={FsmNow}");
    }

    /// <summary>
    /// 回主菜单并确认**菜单场景真的加载了**。
    /// 只切 FSM 状态（Transition/CallFlow）不会加载 Menu 场景 ⇒ 截出来是一张空白图（实测 sha 全同）。
    /// 所以走真实路径 BackToMain，并以"关卡会话已释放（Level/Player 为 null）"作为就绪判据。
    /// </summary>
    private static async Task MenuReady()
    {
        Game.Event.Emit(SuperMario.Core.Events.BackToMain);
        for (var i = 0; i < 60 && FsmNow != "Menu"; i++) await Wait(0.2f);
        for (var i = 0; i < 50; i++)
        {
            await Wait(0.2f);
            if (StageContext.Level == null && StageContext.Player == null) break;
        }
        await Wait(2.0f);   // 场景切换后要留够帧数再截：实测 0.8 秒会拍到只有一种颜色的空帧
        L($"PROBE MenuReady fsm={FsmNow} level={(StageContext.Level == null ? "null" : StageContext.LevelPath)} " +
          $"player={(StageContext.Player == null ? "null" : "有")}");
    }

    /// <summary>
    /// 场景收尾：**把"死亡 → 重开本关"的等待走完，再回主菜单** —— 一个 Play 会话里连跑多个场景时必须这么做。
    /// <para>
    /// 两个都是实测踩出来的（2026-09-19 17:40，日志可复算）：
    /// <list type="number">
    /// <item>`AppFlow._deathTimer` 只在 `HandleDeathResolved()` 里复位（`AppFlow.cs:691`），而
    ///       `PendingDeath` 还没结算就发 `BackToMain` ⇒ 这个计时器**留给下一个场景**，下一个场景跑到一半
    ///       突然 `还剩余 N 条命，重开本关`、会话被释放 ⇒ 之后的抓拍是"没有会话的空屏"
    ///       （`sec12-b/c/m1` 三张就是这么被 12742 字节、机位 `x=0.00 y=0.00` 的空帧覆盖掉的）。
    ///       铁证：那一刻起 `_deathTimer` 只在 Stage 帧里递减，而 section12 的 Stage 时长
    ///       `(39.147→41.144 之间的 Loading 不算)` 恰好凑满 3.0 秒 = `DeathDuration + 0.4`。</item>
    /// <item>场景结束时若把人留在 1-2 出生点附近，**这个场景自己的**敌人会在两个场景的间隙里把他撞死
    ///       （`马里奥死亡` 落在下一个场景的窗口内，实测 17:39:54.626）⇒ 收尾必须回主菜单，别把活局留给下一位。</item>
    /// </list>
    /// </para>
    /// </summary>
    private static async Task TailToMenu(string tag)
    {
        for (var i = 0; i < 80; i++)
        {
            var q = StageContext.Player;
            if (FsmNow == "Menu" || q == null || q.Alive) break;
            await Wait(0.25f);
        }
        L($"PROBE {tag} 收尾：死亡结算已走完（fsm={FsmNow} 玩家={PosStr(StageContext.Player)} " +
          $"命={StageContext.Score?.Lives}）⇒ 发 BackToMain");
        Game.Event.Emit(SuperMario.Core.Events.BackToMain);
        for (var i = 0; i < 80 && FsmNow != "Menu"; i++) await Wait(0.25f);
        L($"PROBE {tag} 收尾：回主菜单 fsm={FsmNow} " +
          $"关卡={(StageContext.Level == null ? "null（会话已释放）" : StageContext.LevelPath)}");
    }

    /// <summary>#12-12 的摆位判据：**1-2 主关** + 人活着 + 站在 (x,y) 附近（出图闸门用它）。</summary>
    private static bool SideGate(float x, float y)
    {
        var p = StageContext.Player;
        return StageAlive() && (StageContext.LevelPath ?? "") == "Levels/World1-2"
               && p != null && Mathf.Abs(p.FeetPosition.x - x) <= 1.0f
               && Mathf.Abs(p.FeetPosition.y - y) <= 1.5f;
    }

    /// <summary>
    /// 把马里奥**稳到** 1-2 主关的 (x,y)：先等"关卡是 1-2 + 人活着"，再摆位、再确认；
    /// 中途被"开局死亡 → 重开"打断就再来一轮（最多 6 轮）。
    /// 出处：1-2 出生点开局必被撞死是既存缺陷（验收表「取证缺陷登记」⑧），探针只能容忍它、不能假设它不存在。
    /// </summary>
    private static async Task<bool> Settle12(float x, float y, string tag)
    {
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            for (var i = 0; i < 60; i++)
            {
                var p = StageContext.Player;
                if (FsmNow == "Stage" && (StageContext.LevelPath ?? "").Contains("World1-2")
                    && p != null && p.Alive) break;
                await Wait(0.2f);
            }
            Teleport(x, y);
            await Wait(0.6f);
            if (SideGate(x, y))
            {
                L($"PROBE {tag} 就位（第 {attempt} 次）pos={PosStr(StageContext.Player)}");
                return true;
            }
            L($"PROBE {tag} 第 {attempt} 次没稳住（fsm={FsmNow} 关卡={StageContext.LevelPath} " +
              $"玩家={PosStr(StageContext.Player)}）⇒ 等它重开完再来");
        }
        L($"PROBE 警告：{tag} 6 轮都没稳住 ⇒ 后面那几张图会被出图闸门拦下（不写盘）");
        return false;
    }

    private static async Task EnterGame()
    {
        // **不要**反复发 CharChosen：实测连发会把流程按在 Loading 上（每次 CharChosen 都重开一次关卡加载）。
        await ToMenu();
        Game.Event.Emit(SuperMario.Core.Events.CharChosen, 1);
        L("PROBE 已发 CharChosen(1)");
        await WaitStage(25f);
        await Wait(0.6f);
    }

    private static async Task Enter12()
    {
        await EnterGame();
        State("1-1");
        CallFlow("NextLevel");
        await Wait(1.2f);
        await WaitStage(25f);
        await Wait(0.8f);
        State("1-2");
    }

    // ───────────────────── 场景入口 ─────────────────────

    public static void Survey() => Start("survey", SurveyBody);
    public static void Platform() => Start("platform", PlatformBody);
    public static void Koopa() => Start("koopa", KoopaBody);
    public static void Piranha() => Start("piranha", PiranhaBody);
    public static void Reshoot() => Start("reshoot", ReshootBody);
    /// <summary>补采入口：只重采 #49 星砖 / #46 绿龟 / #48 多金币砖（见 <see cref="BlockBumpPart"/> 的说明）。</summary>
    public static void BlockBumps() => Start("blockbumps", BlockBumpsBody);

    // ───────────────────── 通用动作 ─────────────────────

    /// <summary>按一次跳（按住 <paramref name="hold"/> 秒再松 —— 松得早跳得低）。</summary>
    private static async Task Jump(float hold = 0.34f)
    {
        _stub.Hold(GameKey.Space, true);
        await Wait(hold);
        _stub.Hold(GameKey.Space, false);
    }

    /// <summary>把指定 x 附近的栗宝宝逐个踩掉（免得它们在长动作里撞死马里奥）。</summary>
    private static async Task ClearGoombasNear(float x, float range)
    {
        for (var guard = 0; guard < 6; guard++)
        {
            MonoBehaviour target = null;
            foreach (var mb in AllEnemies())
            {
                if (!mb.gameObject.name.StartsWith("Goomba", StringComparison.Ordinal)) continue;
                if (mb is SuperMario.Module.Entities.IEnemy e && e.Dead) continue;
                if (Mathf.Abs(P(mb).x - x) > range) continue;
                target = mb;
                break;
            }
            if (target == null) return;
            L($"PROBE 先踩掉 x={P(target).x:F2} 的栗宝宝（清场）");
            Teleport(P(target).x, P(target).y + 2.0f);
            await Wait(1.0f);
        }
    }

    /// <summary>把场上所有方块（Block_x_y 命名的对象）按格坐标转储一行。</summary>
    private static void DumpBlocks(string tag)
    {
        var rows = new List<string>();
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb == null) continue;
            var n = mb.gameObject.name;
            if (!n.StartsWith("Block_", StringComparison.Ordinal)) continue;
            var p = mb.transform.position;
            rows.Add($"{n}@格({Mathf.FloorToInt(p.x)},{Mathf.FloorToInt(p.y)})");
        }
        rows.Sort(StringComparer.Ordinal);
        L($"PROBE blocks[{tag}] 共 {rows.Count} 个：{string.Join(" ", rows)}");
    }

    private static void SolidProbe(string tag, params Vector2Int[] cells)
    {
        var lv = StageContext.Level;
        var parts = new List<string>();
        foreach (var c in cells)
            parts.Add($"solid ({c.x},{c.y})={(lv != null && lv.IsSolidTile(c.x, c.y))}");
        L($"PROBE solid[{tag}] {string.Join("  ", parts)}");
    }

    // ───────────────────── 场景：几何普查 ─────────────────────

    private static async Task SurveyBody()
    {
        await Enter12();
        foreach (var pair in new[]
                 {
                     new KeyValuePair<string, float>("Koopa", 38f),
                     new KeyValuePair<string, float>("Goomba", 10f),
                     new KeyValuePair<string, float>("Piranha", 106f),
                     new KeyValuePair<string, float>("MovingPlatform", 152f),
                 })
        {
            var mb = FindEnemy(pair.Key, pair.Value) ?? FindByName(pair.Key, pair.Value);
            DumpArt(pair.Key, mb?.gameObject);
        }
        DumpEnemies("普查");
        var plat = FindByName("MovingPlatform", 152f);
        if (plat != null)
        {
            Teleport(P(plat).x, P(plat).y + 1.5f);
            await Wait(1.5f);
            State("站在平台上");
            DumpArt("平台（马里奥站上去后）", plat.gameObject);
            Shot("probe-survey-platform");
        }
    }

    // ───────────────────── 场景：移动平台（12-9）用的工具 ─────────────────────

    /// <summary>读一个私有字段（<c>Platform</c> 是 internal，探针只能按名字取）。</summary>
    private static object FieldOf(object o, string name, bool stat = false)
    {
        if (o == null) return null;
        var fi = o.GetType().GetField(name,
            BindingFlags.NonPublic | BindingFlags.Public | (stat ? BindingFlags.Static : BindingFlags.Instance));
        return fi == null ? null : fi.GetValue(stat ? null : o);
    }

    private static float FieldF(object o, string name, float dflt = float.NaN, bool stat = false)
    {
        var v = FieldOf(o, name, stat);
        return v == null ? dflt : Convert.ToSingle(v);
    }

    /// <summary>一行把平台的**配置行程**打出来（判据 = 与 prefab 值对照，见 策划/对照表.md）。</summary>
    private static string PlatInfo(MonoBehaviour mb)
    {
        if (mb == null) return "组件=null";
        var dn = FieldF(mb, "_downStopY");
        var up = FieldF(mb, "_upStopY");
        var dir = FieldF(mb, "_dir");
        var spd = FieldF(mb, "Speed", float.NaN, true);
        return $"配置[下止点={dn:F1} 上止点={up:F1} 行程={up - dn:F1} 格 初速方向={dir:F0} 速度={spd:F2} 格/秒]";
    }

    /// <summary>
    /// 等"离 <paramref name="x"/> 最近的那台升降台"走到 <paramref name="targetY"/> 附近（±tol），
    /// 返回那一刻抓住的对象（超时返回 null）。**每次轮询重新找** —— spawner 会不断生成/销毁平台，
    /// 抓着旧引用会等到一个已销毁的对象上（实测会白等满超时）。
    /// </summary>
    private static async Task<MonoBehaviour> WaitPlatformNearY(float x, float targetY, float tol, float timeout)
    {
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            var mb = FindByName("MovingPlatform", x);
            if (mb != null && Mathf.Abs(P(mb).y - targetY) <= tol) return mb;
            await Wait(0.04f);
        }
        return null;
    }

    /// <summary>等平台的 y 走到 <paramref name="target"/> 附近（±tol）。</summary>
    private static async Task<bool> WaitNearY(MonoBehaviour mb, float target, float tol, float timeout)
    {
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            if (Mathf.Abs(P(mb).y - target) <= tol) return true;
            await Wait(0.04f);
        }
        return false;
    }

    // ───────────────────── 场景：移动平台（12-9）─────────────────────
    //
    // 判据（数值类）：**运行时读到的行程**（组件里的 `_downStopY` / `_upStopY` + 实测 y 的 min/max）
    //   vs **prefab 真值**（`Moving Platform Vertical Spawner.prefab:98/:111` 的 +16.5 / −6.5 ⇒ 23 格）。
    //
    // 「上止点 / 下止点」两格（表现类）为什么必须用诊断机位：
    //   1-2 的相机纵向取景是 `camY = groundTop − 2 + 正交半高`（`CameraModule.cs:132-134`），
    //   ⇒ **画面下沿恒为 groundTop − 2 = −2**，而画面高度 2×7.5 = 15 格 ⇒ 正常玩法可见带
    //   只有 **y ∈ [−2, 13]**。两个止点 **−6.5 / +16.5 都在可见带之外** —— 也就是说
    //   **正常玩法根本看不到它停在止点上**（原版也是这个观感：它是"从画面外进来、再出去"的
    //   传送带，靠 `DestroyOutOfScreen` 在离屏时销毁）。
    //   所以这两格取**诊断机位**：用游戏自己的暂停状态（`FlowState.Pause`）把世界冻住
    //   （`TickStage` 不再跑 ⇒ `CameraModule.Tick` 不再覆盖相机、`Time.timeScale = 0` ⇒ 平台停在原地），
    //   再把相机摆到该止点、拍完立刻恢复（正交半高与相机位置都还原）。**游戏代码一行未改。**

    /// <summary>
    /// 诊断机位抓拍：暂停 → 摆相机 → 拍 → 恢复（详见调用处的说明）。
    /// <para>相机位置是**外面摆的**，所以画面上"平台所在的世界 y"能由
    /// `(camY, 正交半高, 帧高)` 反解出来 —— 判定留给离线脚本做（skill §1.12 第 1 条）。</para>
    /// </summary>
    private static async Task DiagShot(string name, float camX, float camY, float ortho)
    {
        // 引擎 `Fsm.Transition` 是**同步**的（`Fsm.cs:137-141` → `SwitchTo` → `ApplySwitch` 内联），
        // 所以这一行返回时 `_current` 已经是 Pause、`EnterPause` 里的 `Time.timeScale = 0` 也已生效。
        Game.Fsm.Transition(FlowState.Pause);
        Game.UI.Close<SuperMario.UI.PausePanel>();      // 别把暂停面板拍进取证帧
        var cam = Camera.main;
        var o0 = cam == null ? 7.5f : cam.orthographicSize;
        var p0 = cam == null ? Vector3.zero : cam.transform.position;
        if (cam != null)
        {
            cam.orthographicSize = ortho;
            cam.transform.position = new Vector3(camX, camY, p0.z);
        }
        L($"PROBE 诊断机位：fsm={FsmNow} timeScale={Time.timeScale} 目标 camY={camY:F2} 正交半高={ortho:F2} " +
          $"进入时平台 y={P(FindByName("MovingPlatform", 152f)).y:F3}");
        await Wait(0.25f);                              // 等这一机位渲染出来（暂停中世界是冻的，等待无副作用）
        // ★ 冻结 y 必须在**拍之前那一瞬间**再读一次：进这一机位之前世界还在跑（平台 3 格/秒），
        //   而且 spawner 会换台 —— 用"进入时"的读数配"拍到的另一台"就是上一次 PL2 判 FAIL 的原因。
        var plat = FindByName("MovingPlatform", 152f);
        // spawner 是"每 1.5 秒一台"的传送带 ⇒ 同一条 x 上可能同时有 2~3 台（相差 4.5 格）⇒
        // 日志里必须报**全部**在场平台的 y，判据才说得清"帧里那一台是哪一个"（实测 PL2 就栽在这）。
        var ys = new List<string>();
        foreach (var mb in AllByName("MovingPlatform"))
        {
            if (mb != null) ys.Add($"{P(mb).y:F3}");
        }
        L($"PROBE 平台冻结在 y={P(plat).y:F3}（最近那一台；x={P(plat).x:F2}）");
        L($"PROBE 平台清单 y=[{string.Join(", ", ys)}]（这一帧里一共有 {ys.Count} 台）");
        Shot(name);
        if (cam != null) { cam.orthographicSize = o0; cam.transform.position = p0; }
        Game.UI.Close<SuperMario.UI.PausePanel>();
        Game.Fsm.Transition(FlowState.Stage);
        L($"PROBE 诊断帧 {name} 拍完并恢复（fsm={FsmNow} timeScale={Time.timeScale} 正交半高={(cam == null ? -1f : cam.orthographicSize):F2}）");
    }

    private static async Task PlatformBody()
    {
        await Enter12();
        // ⚠️ 平台是 spawner 生成的，而 spawner **只在马里奥水平 40 格内才生成**
        //    （出处 `MovingPlatformVerticalSpawner.cs:15`）⇒ 先站到它旁边，否则一台都不会出现
        //    （实测：不挪马里奥时本场景 4 秒就空跑结束）。
        Teleport(145f, 0f);
        await Wait(2.0f);
        var plA = FindByName("MovingPlatform", 152f);
        var plB = FindByName("MovingPlatform", 137f);
        if (plA == null) { L("PROBE 找不到移动平台（x≈152）—— spawner 没生成？"); return; }

        L($"PROBE 平台A(近 x=152) pos={P(plA)} {PlatInfo(plA)}");
        L($"PROBE 平台B(近 x=137) pos={(plB == null ? "null" : P(plB).ToString())} {PlatInfo(plB)}");
        var dnA = FieldF(plA, "_downStopY");
        var upA = FieldF(plA, "_upStopY");
        var dnB = FieldF(plB, "_downStopY");
        var upB = FieldF(plB, "_upStopY");
        L($"PROBE 行程对照表(运行时 vs prefab)：平台A {dnA:F1}..{upA:F1} = {upA - dnA:F1} 格；" +
          $"平台B {dnB:F1}..{upB:F1} = {upB - dnB:F1} 格；prefab = -6.5..16.5 = 23 格");

        // ── ① 带人：等一台走到画面中段（y≈2，±0.3），再把马里奥精确放到它**台面顶**上 ──
        //   ⚠️ 台面是 3 格宽 × **半格厚**、transform 在台面中心 ⇒ 脚底 = 中心 + 0.25 格。
        //   ⚠️ 不能"传送到它当前的位置"：它以 3 格/秒上升，0.8 秒后就走了 2.4 格（实测会踩空）。
        var pr = await WaitPlatformNearY(152.8f, 2f, 0.3f, 25f);
        if (pr != null)
        {
            Teleport(P(pr).x, P(pr).y + 0.25f);
            await Wait(0.6f);
            State("落到平台后");
            DumpArt("平台", pr.gameObject);
            for (var i = 0; i < 8; i++)
            {
                var mb = FindByName("MovingPlatform", 152.8f);
                var m = StageContext.Player;
                if (m == null || mb == null) break;
                var pl = P(mb);
                L($"PROBE ride t={i * 0.2f:F1} platY={pl.y:F3} marioY={m.FeetPosition.y:F3} " +
                  $"差值={m.FeetPosition.y - pl.y:F3} grounded={m.Grounded}");
                await Wait(0.2f);
            }
            Shot("probe-platform-ride");
        }
        else L("PROBE 等不到画面中段的平台（跳过骑乘那一张）");

        // ── ② 换到坑边（x=150 有地面，脱离平台免得被带出画面）看整条行程 ──
        Teleport(150.5f, 0f);
        await Wait(0.6f);
        State("站到坑边（观察位）");

        // ── ③ 速度 + **一台的生命周期**采样 ──
        // ⚠️ 现在是 spawner：一台平台从出现点升到画面外就被销毁，**不会**在 [−6.5, 16.5] 之间往返 ——
        //    所以"整周期 min/max"这个口径已经不成立（旧的 22.9 格跨度是"常驻平台往返"时代的读数）。
        //    新口径：跟住"离 x=152.8 最近的那一台"，量它的速度；它被销毁时（引用变成另一个对象）
        //    把上一台的 min/max 报出来 = **一台的实际行程**。
        var slopes = new List<string>();
        var dtPrev = Time.realtimeSinceStartup;
        var yPrev = 0f;
        var curLo = float.MaxValue;
        var curHi = float.MinValue;
        var curRef = (MonoBehaviour)null;
        for (var i = 0; i < 90; i++)
        {
            var mb = FindByName("MovingPlatform", 152.8f);
            var now = Time.realtimeSinceStartup;
            if (mb != curRef)
            {
                if (curRef != null && curHi > -100f)
                    L($"PROBE sweep 上一台（x≈152.8）生命结束：y∈[{curLo:F2}, {curHi:F2}]（跨度 {curHi - curLo:F1} 格）" +
                      " —— speak 出现点 → 离屏销毁");
                curRef = mb;
                curLo = float.MaxValue;
                curHi = float.MinValue;
                yPrev = 0f;
                dtPrev = now;
                await Wait(0.2f);
                continue;
            }
            var y = P(mb).y;
            var dt = now - dtPrev;
            if (dt > 0.05f && Mathf.Abs(y - yPrev) > 0.02f && slopes.Count < 6)
                slopes.Add($"{Mathf.Abs(y - yPrev) / dt:F2}");
            yPrev = y; dtPrev = now;
            if (y < curLo) curLo = y;
            if (y > curHi) curHi = y;
            if (i % 20 == 0) L($"PROBE sweep t={i * 0.2f:F1} platY={y:F3} 方向={FieldF(mb, "_dir"):F0} " +
                              $"min={curLo:F3} max={curHi:F3}");
            await Wait(0.2f);
        }
        L($"PROBE 速度实测（格/秒）={string.Join(" ", slopes)}（prefab: `absSpeed 0.05`/帧 ×60fps = 3.00）");

        // ── ④ 上/下止点各一张（诊断机位，见方法头部的说明）──
        // ⚠️ 现在这一台是**spawner**：平台每 1.5 秒生成一台、离屏即销毁（原版行为）。
        //    所以不能抓着"某一个平台对象"等它走到止点（它可能先被销毁）——
        //    每次轮询都**重新找最近的那一台**。
        var plDn = await WaitPlatformNearY(152.8f, dnA, 0.2f, 25f);
        L($"PROBE 下止点就位={(plDn != null)} platY={(plDn == null ? -999f : P(plDn).y):F3}（目标 {dnA:F1}）");
        await DiagShot("probe-platform-a", 152.8f, dnA + 2.0f, 6f);

        var plUp = await WaitPlatformNearY(152.8f, upA, 0.2f, 25f);
        L($"PROBE 上止点就位={(plUp != null)} platY={(plUp == null ? -999f : P(plUp).y):F3}（目标 {upA:F1}）");
        await DiagShot("probe-platform-b", 152.8f, upA - 2.0f, 6f);

        State("平台场景结束");
    }

    // ───────────────────── 场景：乌龟（12-7）─────────────────────

    private static async Task KoopaBody()
    {
        await Enter12();
        var k1 = FindEnemy("Koopa", 38f);
        if (k1 == null) { L("PROBE 找不到乌龟"); return; }
        MonoBehaviour k2 = null;
        foreach (var mb in AllEnemies())
        {
            if (ReferenceEquals(mb, k1)) continue;
            if (!mb.gameObject.name.StartsWith("Koopa", StringComparison.Ordinal)) continue;
            if (k2 == null || Mathf.Abs(P(mb).x - P(k1).x) < Mathf.Abs(P(k2).x - P(k1).x)) k2 = mb;
        }
        var k2Name = k2 == null ? "无" : $"{k2.gameObject.name}@{P(k2).x:F2}";
        L($"PROBE 乌龟#1 起点={P(k1)} 乌龟#2={k2Name}");
        DumpArt("乌龟-走路", k1.gameObject);
        Shot("probe-koopa-walk-a");
        var wx0 = P(k1).x;
        await Wait(1.2f);
        var wx1 = P(k1).x;
        L($"PROBE 走路位移 wx0={wx0:F3} wx1={wx1:F3} dx={wx1 - wx0:F3}（1.2 秒）");
        Shot("probe-koopa-walk-b");
        // 保持镜头不动：相机跟着马里奥，所以两帧里马里奥必须还在原地
        State("走路采样后");
        DumpEnemies("走路后");

        // 踩成壳：把马里奥放到乌龟正上方，让重力把它踩下去
        Teleport(P(k1).x, P(k1).y + 2.0f);
        await Wait(1.6f);
        L($"PROBE 踩后 乌龟#1={P(k1)}");
        State("踩后");
        DumpArt("乌龟-踩成壳", k1.gameObject);
        Shot("probe-koopa-shell-still");

        // 静止的壳：量位移（等 1.2 秒）
        var sx0 = P(k1).x;
        await Wait(1.2f);
        var sx1 = P(k1).x;
        L($"PROBE 静止壳位移 sx0={sx0:F3} sx1={sx1:F3} dx={sx1 - sx0:F4}（1.2 秒）");

        // 踢壳：从左侧走过去撞它 → 壳被踢向右（远离马里奥）
        Teleport(sx1 - 1.5f, -2f);
        await Wait(0.8f);
        State("踢壳前");
        _stub.Hold(GameKey.RightArrow, true);
        await Wait(0.45f);
        _stub.Hold(GameKey.RightArrow, false);
        L($"PROBE 踢壳后立刻 shell={P(k1)} 乌龟#2={P(k2)}");
        for (var i = 0; i < 14; i++)
        {
            var dead2 = k2 is SuperMario.Module.Entities.IEnemy e2 && e2.Dead;
            L($"PROBE 壳滑行 t={i * 0.15:F2} shell={P(k1)} shellBox={(k1 is SuperMario.Module.Entities.IEnemy s1 ? s1.Bounds.ToString() : "?")} " +
              $"乌龟#2={P(k2)} 乌龟#2死={dead2} 马里奥={PosStr(StageContext.Player)} 命={StageContext.Score?.Lives} alive={StageContext.Player?.Alive}");
            if (i == 2) Shot("probe-koopa-shell-kicked");
            if (i == 8) Shot("probe-koopa-shell-hit");
            await Wait(0.15f);
        }
        DumpEnemies("踢壳后");
        State("踢壳后");
    }

    // ───────────────────── 场景：食人花（12-8）─────────────────────

    /// <summary>每朵花"伸出到最高"时碰撞盒的 yMax（按 x 记）。探针只用它判"它出来了"，不引用内部常数。</summary>
    private static readonly Dictionary<float, float> _piranhaTop = new Dictionary<float, float>();

    private static void NotePiranhaTop(MonoBehaviour ph)
    {
        if (!(ph is SuperMario.Module.Entities.IEnemy e)) return;
        var k = P(ph).x;
        var top = e.Bounds.yMax;
        if (!_piranhaTop.TryGetValue(k, out var m) || top > m) _piranhaTop[k] = top;
    }

    /// <summary>先观察一个完整伸缩周期，记下"完全伸出"时的盒顶（不预设数值）。</summary>
    private static async Task LearnPiranhaTop(MonoBehaviour ph, float seconds)
    {
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            NotePiranhaTop(ph);
            await Wait(0.1f);
        }
        _piranhaTop.TryGetValue(P(ph).x, out var m);
        L($"PROBE 学到食人花 x={P(ph).x:F2} 完全伸出时盒顶 y={m:F3}");
    }

    /// <summary>等它伸出来（盒顶接近学习到的最高值）。</summary>
    private static async Task<bool> WaitPiranhaOut(MonoBehaviour ph, float timeout = 9f)
    {
        if (!(ph is SuperMario.Module.Entities.IEnemy e)) return false;
        if (!_piranhaTop.TryGetValue(P(ph).x, out var top)) return false;
        var target = top - 0.1f;
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            if (e.Bounds.yMax >= target) return true;
            await Wait(0.1f);
        }
        return false;
    }

    private static async Task PiranhaBody()
    {
        await Enter12();
        var ph = FindEnemy("Piranha", 106f);
        if (ph == null) { L("PROBE 找不到食人花"); return; }
        DumpArt("食人花", ph.gameObject);
        // 站在 3 格之外（> HideDistance 2），看它自然伸缩
        Teleport(P(ph).x - 3.0f, 0f);
        L("PROBE 马里奥站远处（距花 3.0 格 > 2），观察自然伸缩");

        var shotOut = false;
        var shotIn = false;
        var yPrev = float.NaN;
        var hold = 0;
        for (var i = 0; i < 90; i++)
        {
            var y = P(ph).y;
            var b = ph is SuperMario.Module.Entities.IEnemy pe ? pe.Bounds : new Rect();
            NotePiranhaTop(ph);
            L($"PROBE piranha t={i * 0.15:F2} 底边y={y:F3} box=[{b.yMin:F2},{b.yMax:F2}]高={b.height:F2}");
            if (!float.IsNaN(yPrev) && Mathf.Abs(y - yPrev) < 0.02f) hold++; else hold = 0;
            if (hold == 2)
            {
                if (b.height > 1.4f && !shotOut) { shotOut = true; Shot("probe-piranha-out"); L($"PROBE 判定：完全伸出（底边 y={y:F3}，盒顶 {b.yMax:F3}）"); }
                if (b.height < 0.01f && !shotIn) { shotIn = true; Shot("probe-piranha-in"); L($"PROBE 判定：完全缩回（底边 y={y:F3}）"); }
            }
            yPrev = y;
            // 跑满一个完整伸缩周期（5 秒 = 升1 + 停1.5 + 降1 + 停1.5 秒），
            // 这样"最高点"是真的学到手了，而不是碰巧某一次的采样。
            if (i >= 44) break;
            await Wait(0.15f);
        }
        _piranhaTop.TryGetValue(P(ph).x, out var phTop);
        L($"PROBE 学习到 x={P(ph).x:F2} 的盒顶={phTop:F3}");
        State("伸缩观察后");

        // 靠近：站到 2 格以内 → 它停在下止点不再伸出（原版规则）
        L("PROBE 把马里奥挪到距花 1.5 格（< 2）");
        Teleport(P(ph).x - 1.5f, 0f);
        for (var i = 0; i < 40; i++)
        {
            L($"PROBE 靠近中 t={i * 0.15:F2} 底边y={P(ph).y:F3} 马里奥={PosStr(StageContext.Player)}");
            if (i == 12) Shot("probe-piranha-near");
            await Wait(0.15f);
        }
        State("靠近后");

        // ── 火球杀：另一朵花（x=100.5 那根 3 格高管子）──
        // 站位要点：站在管子【右半格】（x+1.0）⇒ 与花盒 x∈[x-0.5,x+0.5] 不重叠（不会撞死）；
        // 高度取"管口顶面下方半格多"（y=2.4）⇒ 火球从脚底 +0.6 发出，正好落在花的盒子里
        // （站在管口顶面上反而打不到：花的盒顶只比管口高约 0.2 格，火球会比它高一点点）。
        var ph2 = FindEnemy("Piranha", 100f);
        L($"PROBE 换一朵花（x={(ph2 == null ? 0f : P(ph2).x):F2}）做火球测试");
        Teleport(P(ph2).x + 3.0f, 0f);
        StageContext.Player.PowerUp(PowerState.Fire);
        L("PROBE 已给马里奥火形态");
        await Wait(1.0f);
        await LearnPiranhaTop(ph2, 5.5f);
        if (await WaitPiranhaOut(ph2))
        {
            var pt = _piranhaTop[P(ph2).x];
            L($"PROBE 花已伸出（盒顶 {pt:F2}）；把马里奥放到它右侧半格的胸高（y=2.4）后朝左发火球");
            Teleport(P(ph2).x + 1.0f, 2.4f);
            await Wait(0.05f);
            _stub.Hold(GameKey.LeftArrow, true);
            await Wait(0.08f);
            _stub.Hold(GameKey.LeftArrow, false);
            State("发火球前");
            Shot("probe-piranha-fireball-before");
            _stub.Hold(GameKey.W, true);
            await Wait(0.2f);
            _stub.Hold(GameKey.W, false);
            for (var i = 0; i < 12; i++)
            {
                var dead = ph2 is SuperMario.Module.Entities.IEnemy e2 && e2.Dead;
                L($"PROBE 火球后 t={i * 0.1:F2} 花死={dead} 花底边y={P(ph2).y:F3} 马里奥={PosStr(StageContext.Player)} alive={StageContext.Player?.Alive}");
                if (i == 2) Shot("probe-piranha-fireball-after");
                await Wait(0.1f);
            }
        }
        else L("PROBE 警告：这朵花一直没伸出来，火球测试跳过");

        // ── 踩它受伤：从上方落到花身上（原版规则）──
        if (await WaitPiranhaOut(ph))
        {
            L("PROBE 花已伸出，把马里奥放到它正上方让它落下去（应受伤，不该有'踩中敌人'）");
            Teleport(P(ph).x, 5.0f);
            for (var i = 0; i < 16; i++)
            {
                L($"PROBE 踩花后 t={i * 0.1:F2} 马里奥={PosStr(StageContext.Player)} alive={StageContext.Player?.Alive} " +
                  $"命={StageContext.Score?.Lives} 分={StageContext.Score?.Points}");
                if (i == 3) Shot("probe-piranha-stomp");
                await Wait(0.1f);
            }
        }
        State("踩花后");
    }

    // ───────────────────── 场景：1-1 五张过期证据重拍（验收表 #45~#49）─────────────────────

    /// <summary>补采入口的实体（`reshoot` 的尾段单独跑一次；为什么另开一个入口见 <see cref="BlockBumpPart"/>）。</summary>
    private static async Task BlockBumpsBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        await BlockBumpPart();
        await TailToMenu("blockbumps");
    }

    private static async Task ReshootBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        State("1-1 开拍前");

        // ── #45 第一组方块逐格对齐：运行时方块转储 + 实心抽查 + 截图 ──
        DumpBlocks("#45 第一组方块（1-1 全场）");
        SolidProbe("#45", new Vector2Int(-14, -5), new Vector2Int(-14, -4),
            new Vector2Int(2, 0), new Vector2Int(6, 0), new Vector2Int(7, 0),
            new Vector2Int(50, 1), new Vector2Int(80, 0), new Vector2Int(87, 0));
        var data = StageContext.Level?.Data;
        L($"PROBE level[#45] 瓦片={data?.Tiles.Count} 实体={data?.Entities.Count} " +
          $"范围 x[{data?.MinTileX},{data?.MaxTileX}] 地面顶={StageContext.Level?.GroundTopY}");
        // ⚠️ 拍装饰帧之前先把**画面里的栗宝宝**Flip 掉：原版整关海报上没有任何精灵，
        //    而 1-1 最左那只栗宝宝会一路向左走到 x≈-1 的那座山丘上（本轮实测：它把山丘挡了一格，
        //    脚本量"绿色列区间"时那一段的中心偏 0.33 格 ⇒ #54 的逐段比对差 0.72 格、判 FAIL）。
        //    这一帧要证的是**装饰**（#53/#54），敌人的判据在 #9/#10/#46 —— 所以清掉它们是对的判据准备，
        //    不是"挑有利证据"：清掉的是**会挡判据的精灵**，其余地形/装饰一个没动。
        var cleared = 0;
        foreach (var mb in AllEnemies())
        {
            if (!mb.gameObject.name.StartsWith("Goomba", StringComparison.Ordinal)) continue;
            if (P(mb).x > 24f) continue;                       // 只清画面窗口内的（乌龟/远处的栗宝宝不动）
            if (mb is SuperMario.Module.Entities.IEnemy en && !en.Dead) { en.Flip(); cleared++; }
        }
        L($"PROBE 装饰帧清场：Flip 掉窗口内 {cleared} 只栗宝宝（原版海报上没有精灵；乌龟与 x>24 的不动）");
        await Wait(0.3f);
        Teleport(6.5f, -3f);
        await Wait(0.8f);
        State("#45 站位");
        await ShotGated("12-blocks-group", "#45 第一组方块 / #53·#54 修后实机帧",
            () => StageAlive() && NearX(6.5f));

        // ── 变大（后面 47/48/49 三个块都在 y=0/1，小马里奥头顶刚好差一点）──
        StageContext.Player.PowerUp(PowerState.Big);
        await Wait(1.0f);
        State("#47 变大后");
        DumpArt("马里奥-大", StageContext.Player.Transform?.gameObject);

        // ── #47 隐形 1-UP 块 (50,1)：顶到之前看不见，顶到才现形并弹 1-UP ──
        await EnsureStage("#47");
        await TeleportGround(50.5f, "#47");
        State("#47 顶之前");
        await ShotGated("15-hidden-before", "#47 隐形 1-UP 块·顶之前（块隐形、人站在它下方）",
            () => StageAlive() && NearX(50.5f));
        await Jump(0.4f);
        await Wait(0.55f);
        State("#47 顶之后");
        await ShotGated("16-hidden-oneup", "#47 顶到后（块现形 + 弹出 1-UP）",
            () => StageAlive() && NearX(50.5f, 2.0f));
        await Wait(0.4f);
        DumpBlocks("#47 之后（找 (50,1)）");

        // 剩下三块（#49 星砖 / #46 绿龟 / #48 多金币砖）抽成 BlockBumpPart —— 单独一个入口也能只重采这三行：
        // 采集即冻结（skill §2 第 4 条）：#45 的 12-blocks-group 与 #47 的两帧**已经拍好并冻结**，
        // 重跑整个 reshoot 会把它们再拍一遍（同一行采到第 3 次），所以补采走 `Probe.BlockBumps`。
        await BlockBumpPart();
    }

    /// <summary>
    /// #49 星砖 / #46 绿龟 / #48 多金币砖 的采集（`reshoot` 的尾段，也是 `blockbumps` 的全部）。
    /// <para>为什么不放在 `reshoot` 里固定顺序跑：这三行的帧在 2026-09-19 17:01/17:07 两次都没拍成
    /// （第一次人死在乌龟那一步、第二次星砖没吃到星 + 清场时被撞死），需要补采；而 #45/#47 的帧
    /// 已经拍好 ⇒ 按"只重采受影响的行"另开一个入口，不把已冻结的帧再拍一遍。</para>
    /// </summary>
    private static async Task BlockBumpPart()
    {
        await EnsureStage("blockbumps 起点");
        if (StageContext.Player.Power != PowerState.Big)
        {
            StageContext.Player.PowerUp(PowerState.Big);   // 单独跑这个入口时也要够高（小马里奥头顶差一点）
            await Wait(1.0f);
        }
        State("补采起点");
        // 这一段要跑 ~25 秒，中间人要停在砖下站/跳十几次 —— 1-1 这一段有栗宝宝以 ~1.2 格/秒往左走，
        // 实测 2026-09-19 17:07/17:12 两次都是**被栗宝宝撞死**（17:07 在清场时被降级→死；17:12 在第 5 次
        // 连顶时死，币只到 4）。所以先把 x∈[40,120] 的栗宝宝**撞飞**（`IEnemy.Flip()`：人不接触、不移动，
        // 只把会来撞人的那几只清掉）—— 这三行的判据是砖块行为 + HUD 读数，与敌人无关；
        // 乌龟**不清**（#46 要它活着走路）。清场这件事写进联络图的格上注。
        KillGoombasNear(80f, 40f);

        // ── #46 1-1 里的绿龟（走路 + 位移数据）──
        var koopa = FindEnemy("Koopa", 93f);
        var ko = koopa as SuperMario.Module.Entities.IEnemy;
        if (koopa == null || ko == null || ko.Dead) L("PROBE 警告：找不到（活的）乌龟");

        // ── #49 星砖 (87,0)：顶出★ **并真吃到** ──
        // ★ 这一段**挪到 #46 之后**再跑，而且判据不再接受"★在画面里"这种弱化版（任务书-收尾三项 第 2 条）。
        //   两个理由：
        //   ① 次序：#49 一旦真吃到星，马里奥就有 10 秒"碰谁杀谁"（`StarInvincible`）—— 而 #46 要那只
        //      乌龟**活着走路**并量位移 ⇒ 先量 #46，再吃星（原次序是先 #49 后 #46，那时人吃不到星所以没暴露）。
        //   ② 判据：见下面 `#49 星砖` 段的注释（站到★必经路径上等它走过来）。
        // 这里只留一句提示，真正的动作在 #46 之后。
        L("PROBE #49 排在 #46 之后执行（先量乌龟，再吃星；见本段注释）");

        // ── #46 绿龟：人站到乌龟右侧、脚下有地板的那一格，站 1.3 秒看它走远 ──
        if (koopa == null || ko == null || ko.Dead)
        {
            L("PROBE #46 跳过：没有活乌龟");
        }
        else
        {
            // ① 落点必须是"脚下有地板、身上没有实心格"的格：`Teleport` 只写坐标，摆到坑上方会直接
            //    掉出世界（实测 2026-09-19 17:01 摆到 x=87.94 立刻 `死亡`）。1-1 的地板顶 = y=-3，
            //    实心格在 y=-4（离线查法 `.ai-tmp/test/ground_gaps_11.py`）。
            // ② 人在乌龟**右**侧 5 格：它向左走，离人越来越远（原来摆左侧 3 格 = 迎头撞上）。
            // ③ 站之前先把附近的栗宝宝撞飞（人不动、不接触）—— 否则站 1.3 秒必被撞死。
            var mx = SafeGroundX(P(koopa).x + 5.0f, -3f);
            KillGoombasNear(mx, 10f);
            Teleport(mx, -3f);
            await Wait(0.8f);
            var k0 = P(koopa).x;
            L($"PROBE koopa[#46] 落点 x={mx:F2}（SafeGroundX 查过脚下有地板）乌龟起点 x={k0:F2} " +
              $"玩家={PosStr(StageContext.Player)} 存活={(StageContext.Player != null && StageContext.Player.Alive)} " +
              $"star={(StageContext.Player != null && StageContext.Player.StarInvincible)}");
            await ShotGated("probe-koopa-1", "#46 绿龟走路（第 1 帧，人在它右侧）",
                () => StageAlive() && koopa != null && !ko.Dead && NearX(mx, 2.0f));
            await Wait(1.3f);
            var k1 = P(koopa).x;
            L($"PROBE koopa[#46] 1.3 秒后 x={k1:F2} 位移={k1 - k0:F3}");
            await ShotGated("probe-koopa-2", "#46 绿龟走路（第 2 帧）",
                () => StageAlive() && koopa != null && !ko.Dead && NearX(mx, 2.0f));
            await EnsureStage("#46 之后");
        }

        // ── #49 星砖 (87,0)：顶出★ **并真吃到**（判据 = `吃到无敌星`，⛔ 不接受"★在画面里"）──
        // 为什么这样写（逐条都是实测踩出来的）：
        //   ① 判据的语义（任务书-收尾三项 第 2 条）：#49 = "顶出★ **并吃到**" ⇒ 断言必须是
        //      `[Block] 无敌星砖 (87,0)：顶出★` **且** `[Player] 吃到无敌星：10 秒内碰谁杀谁` 两条。
        //   ② 上一版拍"★刚弹出来那一瞬"的理由是"人不追就吃不到" —— 那是**探针走位不对**，
        //      不是判据该让步。★ 的移动逐项有出处（`Module/Entities/ItemModule.cs` 的 `StarItem.Update`）：
        //      `_dir = 1`（向右）、水平 **3 格/秒**、落地按 `GameConst.StarBounce`(11.25) 弹起
        //      ⇒ 它是"沿地面向右跳着走"，**不会回头**。所以正确做法是**站到它必经路径上不动**等它来。
        //   ③ 路径上不能有活敌人：先量完 #46（乌龟要活着走路），再把 1-1 那只乌龟撞飞 ——
        //      否则吃到星之后马里奥 10 秒"碰谁杀谁"，会把乌龟顺手撞死（#46 的判据就废了）。
        await EnsureStage("#49");
        L($"PROBE #49 先清路：撞飞 {FlipEnemies("Koopa")} 只乌龟（#46 已量完，之后人不该再碰敌人）");
        await TeleportGround(87.5f, "#49");
        await Jump(0.4f);
        L($"PROBE #49 顶星砖：starItem={StarItemExists()} " +
          $"power={(StageContext.Player == null ? "?" : StageContext.Player.Power.ToString())} " +
          $"（上一行 `[Block] 无敌星砖 (87,0)：顶出★` 是砖自己的日志）");
        // ★ 从砖上弹出后沿地面向右走 3 格/秒 ⇒ 站到它右边 5 格的地面上等（不作任何按键）。
        var eatX = SafeGroundX(92.5f, -3f);
        Teleport(eatX, -3f);
        L($"PROBE #49 站到★的必经路径上等它：x={eatX:F2}（★ 从砖 (87.5,·) 向右跳着走，3 格/秒）");
        var starEaten = false;
        for (var i = 0; i < 80; i++)
        {
            await Wait(0.1f);
            var p1 = StageContext.Player;
            var got = p1 != null && p1.StarInvincible;
            if (got || i % 10 == 0)
                L($"PROBE #49 等★ t={i * 0.1f:F2}s 人在x={PlayerX():F2} starItem={StarItemExists()} " +
                  $"star={got} alive={(p1 != null && p1.Alive)}");
            if (got) { starEaten = true; break; }
            if (p1 == null || !p1.Alive) { L("PROBE #49 等人时马里奥死了 ⇒ 停止等★"); break; }
        }
        if (starEaten)
            L($"PROBE #49 判据：`无敌星砖 (87,0)：顶出★` **且 `吃到无敌星`**（star=True，人在 x={PlayerX():F2}）⇒ PASS");
        else
            L("PROBE 警告：#49 8 秒内没吃到★（star 仍为 False）⇒ 13-starbrick-star 不写盘（不许拿\"★在画面里\"充数）");
        await ShotGated("13-starbrick-star", "#49 星砖：顶出★并**真吃到**（star=True）",
            () => { var p2 = StageContext.Player; return p2 != null && p2.Alive && p2.StarInvincible; });
        State("#49 吃到星之后");

        // ── #48 多金币砖 (80,0)：连顶 11 次（前 10 次出币、第 11 次不再出）──
        await EnsureStage("#48");
        // ⚠️ 用"撞飞"而不是旧的 `ClearGoombasNear`（后者把人摆到栗宝宝头上踩，过程中自己会贴到别的敌人：
        //    实测 2026-09-19 17:07 清场时被降级 Big→Small、两秒后死亡 ⇒ #48 崩掉）。连顶要 16 秒，
        //    这 16 秒里身边不能有活敌人（半径 40：连顶期间从更右走过来的也在路上就清了）。
        KillGoombasNear(80.5f, 40f);
        // 连顶要 16 秒：小马里奥一下就没，大马里奥被撞一次只是降级 ⇒ 先补成大的（形态与本行判据无关，
        // 只是让"被撞一次"不等于"当场死亡"；见联络图格上注）。
        if (StageContext.Player != null && StageContext.Player.Power != PowerState.Big)
        {
            StageContext.Player.PowerUp(PowerState.Big);
            L("PROBE #48 前补形态：Small ⇒ Big（连顶 16 秒，避免一击致死）");
            await Wait(0.8f);
        }
        State("#48 开始连顶前");
        for (var i = 1; i <= 11; i++)
        {
            // 每次顶之前都重新摆回地面（见 TeleportGround 的注释：一跳之后人会落到砖排顶上）
            await EnsureStage($"#48 第 {i} 次");
            await TeleportGround(80.5f, $"#48 第 {i} 次");
            var p1 = StageContext.Player;
            if (p1 == null || !p1.Alive || FsmNow != "Stage")
            {
                L($"PROBE #48 第 {i} 次顶：状态不对（fsm={FsmNow} 玩家={PosStr(p1)}）⇒ 停止连顶");
                break;
            }
            L($"PROBE multicoin 第 {i} 次顶（顶前 币={StageContext.Score?.Coins} 分={StageContext.Score?.Points} " +
              $"脚={PosStr(p1)} grounded={p1.Grounded}）");
            await Jump(0.4f);
            await Wait(0.45f);
            L($"PROBE multicoin 第 {i} 次后 币={StageContext.Score?.Coins} 分={StageContext.Score?.Points} " +
              $"马里奥={PosStr(StageContext.Player)} alive={StageContext.Player?.Alive}");
        }
        State("#48 连顶后");
        // 判据闸门：机位（= 玩家的 x，取景必须含 T(80,0)）**且** HUD 的金币读数 = ×10
        // （#48 的断言就是"末态 金币=10"；读数对不上就不许落盘 —— 旧口径那张正是取景在关卡起点、HUD ×00）。
        await ShotGated("14-multicoin-after10", "#48 连顶后（机位含 T(80,0) + HUD 币读数为 ×10）",
            () => StageAlive() && NearX(80.5f) && HudNode("Coins") == "×10");
        DumpBlocks("#48 之后（找 (80,0)）");
    }

    /// <summary>
    /// 转储终点装饰（旗杆杆身 / 顶球 / 旗 / 城堡）。
    /// ⚠️ 它们是 <c>LevelProps</c> 用 <c>new GameObject(...)</c> 建的**裸对象**（没有 MonoBehaviour），
    /// 所以 <c>FindByName</c> 那条路找不到 —— 必须按 Transform 找。
    /// </summary>
    private static void DumpProps(string tag)
    {
        var names = new[] { "Flagpole", "FlagpoleTop", "Flag", "Castle" };
        var found = 0;
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t == null) continue;
            var n = t.gameObject.name;
            if (System.Array.IndexOf(names, n) < 0) continue;
            var sr = t.GetComponent<SpriteRenderer>();
            var sp = sr == null ? null : sr.sprite;
            found++;
            L($"PROBE props[{tag}] {n} pos=({t.position.x:F2},{t.position.y:F2}) " +
              $"sprite={(sp == null ? "null" : sp.name)} size={(sp == null ? Vector2.zero : (Vector2)sp.bounds.size)} " +
              $"enabled={(sr != null && sr.enabled)}");
        }
        L($"PROBE props[{tag}] 共 {found} 件");
    }

    // ═══════════ 2026-09-18 追加：补齐"过期证据"需要的状态（走真实流程入口，不自己造状态）═══════════

    // ═══════════ 场景：1-2 末尾审计（用户报：悬空 / 横管方向 / 过关穿墙）═══════════

    // ⚠️ 已停用：这个场景断言的是【旧设计】——它按 `lv.FlagpoleTouchX` 摆位走"过关段"，
    //    而 1-2 地下段现在**没有旗杆**（旗杆搬到了地表段，见 LevelData.HasFlagpole）⇒
    //    它算出来的位置指向一个不存在的终点，跑出来的任何结论都是假的（假证据源）。
    //    1-2 结尾的现行断言在 `Section12`。留着这行只为保留 diff 线索，⛔ 别再跑它。
    // public static void Audit12() => Start("audit12", Audit12Body);

    /// <summary>
    /// 场景：**顶砖块不许瞬移**（用户原话：「我顶问号/顶砖块/顶任何东西，都会直接瞬移到障碍上方」）。
    /// <para>病根在 <c>PlayerActor.cs</c> 的撞头分支：原来写 `newY = carrierTop - _size.y`，
    /// 那是"障碍格的**顶边**减身高"⇒ 把人摆到障碍**上方**；正确值是 `cell.y - _size.y`（脚停在障碍格底边）。</para>
    /// <para>判据（数值，不用截图）：起跳顶到方块后，**脚底 y 必须始终 ≤ 方块格的底边**（即人留在下方），
    /// 且任何采样点都不得站在实心格内；同时方块要被顶到（位置有位移 = 顶砖动画播了，证明这脚确实顶上了）。</para>
    /// </summary>
    public static void HeadHit() => Start("headhit", HeadHitBody);

    private static async Task HeadHitBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        // 先把按键全部松开：`_stub` 的按键状态是**跨场景**保留的，上一轮跑完如果留着"跳键按下"，
        // 这一场一开始马里奥就会自己跳（实测：起跳点被算到半空中，方块位移恒 0）。
        _stub.Hold(GameKey.RightArrow, false);
        _stub.Hold(GameKey.LeftArrow, false);
        _stub.Hold(GameKey.Space, false);
        await Wait(0.4f);
        var lv = StageContext.Level;

        // 收集候选方块，算出"它下方第几格才有地板"（d）—— d 越小越容易跳上去顶到。
        // 1-1 的问号块悬在地面上方约 4 格，所以不能要求"正下方紧挨着就是地板"（那样一个都选不出来）。
        // 优先问号块（`Block_QuestionBlock_*`）：它**永远不会碎**、必定播顶砖动画 ⇒ 用它判"这脚真顶到了"最干净。
        var cands = new List<(GameObject go, Vector2Int cell, int d, int surfaceTop, string name)>();
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb == null) continue;
            var nm = mb.gameObject.name;
            if (!nm.StartsWith("Block_", StringComparison.Ordinal)) continue;
            // ★ 格坐标**从名字里解析**：`Block_{kind}_{x}_{y}`（BlockModule 里就是这么起的）。
            //   ⛔ 不要用 `FloorToInt(transform.position)` 反推 —— 方块贴图有"居中轴心"和"底部轴心"两种
            //   （见 BlockModule 的对齐注释），中心轴心时 position.y = 格中心 ⇒ 反推会**偏一格**。
            //   实测踩过：把 `Block_..._64_0`（格 0）反推成格 -1，于是拿"底边 -1"去判"应停在 -0.75"，
            //   把一次**完全正确**的顶砖判成了 FAIL（假 FAIL，同样是假证据）。
            var segs = nm.Split('_');
            if (segs.Length < 4) continue;
            if (!int.TryParse(segs[segs.Length - 2], out var bx) ||
                !int.TryParse(segs[segs.Length - 1], out var by)) continue;
            var c = new Vector2Int(bx, by);
            if (lv.IsSolidTile(c.x, c.y - 1)) continue;        // 紧贴下方实心：站不进去，跳过
            var d = 0;
            var sy = c.y - 1;
            while (d < 6 && !lv.IsSolidTile(c.x, sy)) { d++; sy--; }
            if (d >= 6) continue;                              // 这一列底下没有地板
            // 地板要够宽（左右各一格也实心）：否则是坑边孤砖，站上去很容易掉出去
            // ⇒ 马里奥死亡、整关重开，后面的尝试全部作废（实测踩过）。
            if (!lv.IsSolidTile(c.x - 1, sy) || !lv.IsSolidTile(c.x + 1, sy)) continue;
            cands.Add((mb.gameObject, c, d, sy + 1, nm));      // surfaceTop = 地板格的顶边
        }
        // 问号块排前面（同组内按 d 升序），其余方块兜底。
        cands.Sort((a, b) =>
        {
            var qa = a.name.Contains("QuestionBlock") ? 0 : 1;
            var qb = b.name.Contains("QuestionBlock") ? 0 : 1;
            return qa != qb ? qa.CompareTo(qb) : a.d.CompareTo(b.d);
        });
        if (cands.Count == 0) { L("PROBE headhit 全关卡找不到可站到下方的方块，放弃（不是通过）"); return; }
        L($"PROBE headhit 候选方块 {cands.Count} 个（问号块优先，再按离地板距离）：" +
          string.Join(" ", cands.ConvertAll(a => $"{a.name}({a.cell.x},{a.cell.y})d={a.d}{(lv.IsSolidTile(a.cell.x, a.cell.y) ? "" : "★非实心")}")));

        var pass = false; string verdict = "没试过";
        for (var k = 0; k < Mathf.Min(4, cands.Count); k++)
        {
            // ⚠️ 候选是**开场时**抓的快照：之前几次尝试里马里奥可能被撞死 / 掉坑 ⇒ 整关重开 ⇒
            //    方块 GameObject 全部重建，旧引用已被销毁（实测：读 `pick.transform` 抛 MissingReference）。
            if (cands[k].go == null) { verdict += $" [{cands[k].name} 已随重开销毁，跳过]"; continue; }
            var pick = cands[k].go;
            var c0 = cands[k].cell;
            var yBlockBottom = c0.y;                       // 方块格的底边 = 脚能到的最高处
            var floorY = cands[k].surfaceTop - 1;          // 他脚下的地板格

            // 左侧要有 4 格跑道：**站着跳顶不到 ? 块**（实测站立跳顶点比方块低 0.76 格），
            // 原版也是"跑起来跳更高"；用户的复现路径就是跑过去顶一下。
            var runOk = true;
            for (var kk = 1; kk <= 4; kk++) if (!lv.IsSolidTile(c0.x - kk, floorY)) runOk = false;
            if (!runOk) { verdict += $" [{cands[k].name} 左侧没跑道]"; continue; }

            var bump0 = pick.transform.position.y;
            var tx = c0.x - 3.5f;
            var ty = cands[k].surfaceTop;

            Teleport(tx, ty);
            await Wait(0.6f);
            var p0 = StageContext.Player;
            if (p0 == null) { L("PROBE headhit 马里奥没了"); return; }

            // ★ 先确认"就位"再起跑 —— 否则这一跳可能打的是空气（实测：上一次尝试掉出世界死了、
            //   整关重开，人被放回出生点 x=-10.5，后面的采样全是"在空地上跳"，方块位移恒为 0，
            //   而结论照样会写出来 = 假证据）。
            var at = p0.FeetPosition;
            var placed = Mathf.Abs(at.x - tx) < 0.6f && Mathf.Abs(at.y - ty) < 0.9f && p0.Grounded;
            L($"PROBE headhit 第{k + 1}次尝试：{cands[k].name}@格({c0.x},{c0.y}) 跑道起点=({tx:F2},{ty:F2}) " +
              $"实际脚=({at.x:F3},{at.y:F3}) grounded={p0.Grounded} ⇒ {(placed ? "就位" : "没就位，跳过这个候选")}");
            if (!placed) { verdict += $" [{cands[k].name} 没就位]"; continue; }

            var maxFeet = float.MinValue;
            var maxBump = 0f;
            var inside = 0;
            var samples = 0;
            // ① 先跑：跑到方块前 1.8 格才起跳（实测教训：一开始就按住跳 ⇒ 人在跑道起点就跳完了，
            //    等跑到方块下方时已经落地 ⇒ 全程脚贴地，方块位移恒 0，白白跑一轮）。
            _stub.Hold(GameKey.RightArrow, true);
            for (var w = 0; w < 60; w++)
            {
                await Wait(0.02f);
                var pw = StageContext.Player;
                if (pw == null) break;
                if (pw.FeetPosition.x >= c0.x - 1.8f) break;
            }
            // ② 再起跳（先松一下再按，保证游戏收到的是"按下"这个动作，而不是一直按着）
            _stub.Hold(GameKey.Space, false);
            await Wait(0.04f);
            _stub.Hold(GameKey.Space, true);
            var pj = StageContext.Player;
            L($"PROBE headhit 起跳点 x={(pj == null ? "-" : pj.FeetPosition.x.ToString("F2"))} 脚y={(pj == null ? "-" : pj.FeetPosition.y.ToString("F3"))} " +
              $"（方块在 x={c0.x + 0.5f:F1}）");
            for (var i = 0; i < 100; i++)                  // 2 秒
            {
                await Wait(0.02f);
                var p = StageContext.Player;
                if (p == null) { L("PROBE headhit 马里奥没了"); break; }
                var f = p.FeetPosition;
                var bd = 0f;
                if (pick != null) { bd = Mathf.Abs(pick.transform.position.y - bump0); if (bd > maxBump) maxBump = bd; }
                // ★ 只统计"人正在方块正下方"的那几帧 —— 这才是"顶到它"的判据窗口。
                if (Mathf.Abs(f.x - (c0.x + 0.5f)) <= 0.8f)
                {
                    samples++;
                    if (f.y > maxFeet) maxFeet = f.y;
                    var inSolid = lv.IsSolidTile(Mathf.FloorToInt(f.x), Mathf.FloorToInt(f.y));
                    if (inSolid) inside++;
                    L($"PROBE headhit 过块 f={i:D2} 脚=({f.x:F2},{f.y:F3}) 格{Mathf.FloorToInt(f.y)} " +
                      $"实心={inSolid} 方块位移={bd:F4}");
                }
                if (FsmNow != "Stage") break;
            }
            _stub.Hold(GameKey.RightArrow, false);
            _stub.Hold(GameKey.Space, false);
            await Wait(0.5f);

            var okStay = samples > 0 && maxFeet <= yBlockBottom + 0.001f;   // 人留在方块下方
            var okNoClip = inside == 0;                                    // 全程没站进实心格
            var okHit = maxBump > 0.01f;                                   // 方块确实被顶了（动画位移）
            if (samples == 0) { verdict += $" [{cands[k].name} 人没到过方块下方（采样 0 帧）]"; continue; }
            verdict += $" [{cands[k].name}({c0.x},{c0.y}) 过块帧={samples} 最高脚y={maxFeet:F3} 方块底边={yBlockBottom} " +
                       $"顶到={okHit} 位移={maxBump:F4} 进实心格={inside}" +
                       $" ⇒ {(okStay && okNoClip && okHit ? "PASS" : (okHit ? "FAIL(瞬移到障碍上方)" : "未顶到"))}]";
            if (okHit) { pass = okStay && okNoClip; break; }   // 以"真顶到"的那次为准
        }
        L($"PROBE headhit 结论：{(pass ? "PASS" : "FAIL")}{verdict}  fsm={FsmNow}");
    }

    private static async Task Audit12Body()
    {
        await Enter12();
        if (!IsStage) { L("PROBE 没进到 1-2，放弃"); return; }
        var lv = StageContext.Level;
        var data = lv?.Data;
        L($"PROBE audit12 关卡范围 x[{data?.MinTileX},{data?.MaxTileX}] groundTop={lv.GroundTopY} " +
          $"旗杆X={lv.FlagpoleX} 触发X={lv.FlagpoleTouchX} 城堡门X={lv.CastleDoorX}");

        L("PROBE audit12 ---- 敌人贴地检查（视觉底边 vs 地面）----");
        foreach (var e in AllEnemies())
        {
            if (e == null) continue;
            var tr = e.transform;
            var sr = tr.GetComponentInChildren<SpriteRenderer>();
            var visBottom = 0f; var visTop = 0f; var sp = "null";
            if (sr != null && sr.sprite != null)
            {
                sp = sr.sprite.name;
                var b = sr.sprite.bounds;
                visBottom = sr.transform.position.y + b.min.y * sr.transform.lossyScale.y;
                visTop = sr.transform.position.y + b.max.y * sr.transform.lossyScale.y;
            }
            var feetY = tr.position.y;
            var cx = Mathf.FloorToInt(tr.position.x);
            var cy = Mathf.FloorToInt(feetY);
            L($"PROBE audit12 {e.GetType().Name}@{tr.position.x:F2} 根Y={feetY:F3} 视觉底={visBottom:F3} 视觉顶={visTop:F3} " +
              $"sprite={sp} 本格({cx},{cy})solid={lv.IsSolidTile(cx, cy)} 下格({cx},{cy - 1})solid={lv.IsSolidTile(cx, cy - 1)}");
        }

        L("PROBE audit12 ---- 过关段：逐点查「身位是否在实心格内」----");
        Teleport(lv.FlagpoleTouchX - 2f, lv.GroundTopY + 2f);
        await Wait(1.0f);
        State("过关段起点");
        Shot("audit12-walk-0");
        _stub.Hold(GameKey.RightArrow, true);
        for (var i = 0; i < 70; i++)
        {
            await Wait(0.2f);
            var p = StageContext.Player;
            if (p == null) { L("PROBE audit12 马里奥没了"); break; }
            var pos = p.FeetPosition;
            var cx = Mathf.FloorToInt(pos.x);
            var cy = Mathf.FloorToInt(pos.y);
            var inside = "";
            for (var dy = 0; dy <= 1; dy++)
                if (lv.IsSolidTile(cx, cy + dy)) inside += $"({cx},{cy + dy})";
            var ahead = lv.IsSolidTile(cx + 1, cy) ? "前格(" + (cx + 1) + "," + cy + ")实心" : "";
            L($"PROBE audit12 t={i * 0.2:F1} x={pos.x:F2} y={pos.y:F2} grounded={p.Grounded} 身处实心格=[{inside}] {ahead}");
            if (i == 6) { State("过关段中"); Shot("audit12-walk-1"); }
            if (i == 20) { State("过关段末"); Shot("audit12-walk-2"); }
            if (FsmNow == "Result" || FsmNow == "Loading") break;
        }
        _stub.Hold(GameKey.RightArrow, false);
        await Wait(0.5f);
        State("过关段结束");
        Shot("audit12-walk-3");
        L($"PROBE audit12 结束 fsm={FsmNow}");
    }

    // ═══════════ 场景：1-2 末尾（墙顶 → 侧向管口 → 地表段 → 旗杆 → 城堡）═══════════

    public static void Walk12End() => Start("walk12end", Walk12EndBody);

    private static async Task Walk12EndBody()
    {
        await Enter12();
        if (!IsStage) { L("PROBE 没进到 1-2，放弃"); return; }
        var lv = StageContext.Level;
        L($"PROBE walk12end 关卡={StageContext.LevelPath} 有旗杆={lv.HasFlagpole} " +
          $"侧向管口=({lv.SideExitFaceX},{lv.SideExitY}) 旗杆X={lv.FlagpoleX} 城堡X={lv.CastleDoorX}");

        Teleport(160f, 3f);            // 站到结尾那堵墙的墙顶上（墙 x=157..173,y=0..2）
        await Wait(0.8f);
        State("墙顶");
        Shot("walk12end-0-walltop");
        _stub.Hold(GameKey.RightArrow, true);
        for (var i = 0; i < 40; i++)
        {
            await Wait(0.25f);
            if (StageContext.LevelPath == "Levels/World1-2-Surface") break;
            var q = StageContext.Player;
            if (q != null)
                L($"PROBE walk12end(地下) t={i * 0.25:F1} x={q.FeetPosition.x:F2} y={q.FeetPosition.y:F2} fsm={FsmNow}");
        }
        _stub.Hold(GameKey.RightArrow, false);
        L($"PROBE walk12end 切换后关卡={StageContext.LevelPath} fsm={FsmNow}");

        // 等地表段就绪（Loading → Stage）
        for (var i = 0; i < 60 && FsmNow != "Stage"; i++) await Wait(0.25f);
        var p0 = StageContext.Player;
        var sl = StageContext.Level;
        L($"PROBE walk12end 地表段就位 fsm={FsmNow} HasSpawn={sl.HasSpawn} Spawn=({sl.SpawnX},{sl.SpawnY}) " +
          $"groundTop={sl.GroundTopY} 旗杆X={sl.FlagpoleX} 有旗杆={sl.HasFlagpole} 城堡X={sl.CastleDoorX}");
        L($"PROBE walk12end 地表段玩家={(p0 == null ? "-" : $"({p0.FeetPosition.x:F2},{p0.FeetPosition.y:F2}) busy={p0.Busy} alive={p0.Alive}")}");
        State("地表段出管");
        // 切段后头几帧 Game 视图是单色：这里必须 ShotSolid（实测直接 Shot 得到 12706 字节空帧）。
        await ShotSolid("walk12end-1-surface-spawn");
        for (var i = 0; i < 12; i++)
        {
            await Wait(0.1f);
            var q0 = StageContext.Player;
            if (q0 == null) { L("PROBE walk12end 地表段玩家没了"); break; }
            L($"PROBE walk12end 出生后 t={i * 0.1:F1} pos=({q0.FeetPosition.x:F2},{q0.FeetPosition.y:F2}) " +
              $"busy={q0.Busy} grounded={q0.Grounded} alive={q0.Alive} fsm={FsmNow}");
        }

        // ⚠️ 地表段 x=2..10 是原版那座 1..8 级台阶（`Brown Marble Finish 1.prefab`）。
        // 马里奥不能"走"上 1 格高的坎（同 SMB），所以这里必须边右走边跳 —— 只按右会卡在第二级。
        _stub.Hold(GameKey.RightArrow, true);
        for (var i = 0; i < 170; i++)
        {
            var q0 = StageContext.Player;
            var onStairs = q0 != null && q0.FeetPosition.x < 11.2f;      // 台阶跨 x=2..10
            if (onStairs && i % 3 == 0) _stub.Hold(GameKey.Space, true);
            if (onStairs && i % 3 == 2) _stub.Hold(GameKey.Space, false);
            await Wait(0.2f);
            var p = StageContext.Player;
            if (p != null && i % 5 == 0)
                L($"PROBE walk12end(地表) t={i * 0.2:F1} x={p.FeetPosition.x:F2} y={p.FeetPosition.y:F2} " +
                  $"busy={p.Busy} grounded={p.Grounded} fsm={FsmNow}");
            // ⚠️ 顺序要紧：Result 一进关卡会话就拆了，Player 变 null ——
            // 先判结算再判空，否则永远拍不到结算屏（实测踩过）。
            if (FsmNow == "Result")
            {
                State("结算");
                // 权威判据：注册表 / 实例数 / 递归节点树（含文字内容——面板上写的就是这些 TEXT）
                DumpPanels("结算");
                // 判"面板真的画在画面上"：结算屏 = 0.95 黑遮罩 ⇒ 用"大面积深色"当完成判据。
                // （原先用 ShotOk 的"唯一色 ≥ 3"——天空本身就是几百种颜色，所以面板还没画上来
                //   它就立刻接受了，实测拍到过"只有关卡、没有结算屏"的帧）
                await ShotPanel("walk12end-5-result");
                break;
            }
            if (p == null) { L("PROBE walk12end 地表段马里奥没了"); break; }
            if (p.FeetPosition.x > 11.2f) _stub.Hold(GameKey.Space, false);
            if (i == 6) { State("地表段行进（台阶上）"); Shot("walk12end-2-surface"); }
            if (i == 26) { State("旗杆"); Shot("walk12end-3-flag"); }
            if (i == 50) { State("城堡"); Shot("walk12end-4-castle"); }
        }
        _stub.Hold(GameKey.Space, false);
        _stub.Hold(GameKey.RightArrow, false);
        L($"PROBE walk12end 结束 fsm={FsmNow} 关卡={StageContext.LevelPath}");
    }

    // ═══════════ 场景：1-2 收口的外观证据（A1 地下段结尾三张 / A3 地表段全段）═══════════
    //
    // 坐标出处（一个数都不定）：
    //   地下段结尾：`World1-2.txt` 的 `# side-exit 163 3`（= 原版整关海报上那根
    //     `Warp Green Pipe Side Short` 的管口面；落格口径见该文件头）；墙顶 y=3（墙 x=157..173,y=0..2）。
    //   地表段：`World1-2-Surface.txt` 的 `# spawn 1 2` / `# flagpole 19 0` / `# castle 25 2`。
    //   ⚠️ 下面这些摆位坐标一律**从关卡数据取**（`lv.SideExitFaceX`），⛔ 不在探针里写死 ——
    //      写死过一次的后果：管口从 165 挪到 163 后，探针把马里奥摆进了管口**内部**（身处实心格）。

    public static void Section12() => Start("section12", Section12Body);

    private static async Task Section12Body()
    {
        await Enter12();
        if (!IsStage) { L("PROBE 没进到 1-2，放弃"); return; }
        var lv = StageContext.Level;
        L($"PROBE section12 关卡={StageContext.LevelPath} 有旗杆={lv.HasFlagpole} " +
          $"侧向管口=({lv.SideExitFaceX},{lv.SideExitY}) 范围 x[{lv.Data?.MinTileX},{lv.Data?.MaxTileX}]");

        var faceX = lv.SideExitFaceX;      // 163（关卡数据给的管口面）
        var faceY = lv.SideExitY;          // 3（站在墙顶那一行）
        L($"PROBE section12 管口面=({faceX},{faceY}) ⇒ A1 摆位用 faceX-3/faceX-2/faceX-1");

        // ── A1：站在墙顶 ──
        // ★ 摆位改成 `Settle12` + 出图改成 `ShotGated`（2026-09-19 17:40 实测：这三张曾被"没有会话的空屏"
        //   —— 12742 字节、机位 x=0.00 y=0.00 —— 覆盖掉，根因见 `TailToMenu` 的注释）。
        await Settle12(faceX - 3f, faceY, "#12-12 A1 墙顶");
        State("A1 墙顶");
        await ShotGated("sec12-a-wall", "#12-12 A1 墙顶（1-2 主关 + 活着 + 站在墙顶）",
            () => SideGate(faceX - 3f, faceY));

        // ── A1：管口前 1 格（**直接摆位，不走过去**）──
        // ⚠️ 这里原来是"按住右走、到位就松"。物理换成原版之后**它不安全了**：
        //    松键后的滑行距离 = v²/(2·减速度)，原版是 5.86²/(2×12.5) ≈ 1.37 格（旧参数只有 0.42 格），
        //    而轮询每 0.1 秒一跳（最坏再超 0.59 格）⇒ 实测总位移超过 2 格、右边缘顶到 165
        //    ⇒ **在拍"管口前"之前就换段了**（整场戏因此全拍在地表段、马里奥还被传到关卡外掉下去）。
        //    → 位置必须**精确设定**，不能用"走 + 等惯性"。
        await Settle12(faceX - 2f, faceY, "#12-12 A1 管口前");
        State("A1 管口前");
        await ShotGated("sec12-b-pipefront", "#12-12 A1 管口前（1-2 主关 + 活着 + 站在管口前 1 格）",
            () => SideGate(faceX - 2f, faceY));

        // ── A1：贴着管口（右边缘 ≈ faceX−0.6 < faceX ⇒ 仍在原地下一段）──
        // 同理：直接摆位（摆位后速度为 0，不会再滑）。
        await Settle12(faceX - 1f, faceY, "#12-12 A1 贴着管口");
        State("A1 贴着管口");
        await ShotGated("sec12-c-enter", "#12-12 A1 贴着管口（1-2 主关 + 活着 + 右边缘 ≈ 管口面−0.6）",
            () => SideGate(faceX - 1f, faceY));

        // ── A1：走进管口（右边缘一顶到 faceX 就换段）──
        // 现在是**两段过场**：① 自动走进侧向管口（StartPipeEnterSide）② 地表段从出管口升起。
        // 两段都要留帧，所以这里边等边抓：
        //
        // ★ 采样步长 0.05 → 0.005 秒（E-24 消除后必须的改动）：过场第 1 段的走距现在是
        //   "到管口面为止" = 0.05 格 / 2.5 格每秒 = **0.02 秒**，用 0.05 秒的点采样**必然漏掉它**
        //   ⇒ 判据（过场期间不得身处实心格）就永远只有"没采到"，等于没验。
        //   所以：只要 busy 就**每个采样都记**，最后把命中次数算成 PASS/FAIL（§1.12 第 1 条）。
        _stub.Hold(GameKey.RightArrow, true);
        var shotSide = false;
        var nearSamples = 0; var nearSolid = 0; var busySamples = 0;
        var nearMaxRight = float.MinValue; var nearMinRight = float.MaxValue;
        for (var i = 0; i < 800; i++)
        {
            await Wait(0.002f);
            var p = StageContext.Player;
            if (p == null) { L("PROBE section12 马里奥没了"); break; }
            // 采样窗口 = 「管口前 1 格」一直到换段：**整段都逐次采样**（2 毫秒 ≈ 每帧）。
            // 这一步之所以要这么密：过场第 1 段的走距现在只有 0.05 格 / 2.5 格每秒 = 0.02 秒，
            // 0.05 秒的点采样必然漏掉它 ⇒ 判据（过场期间不得身处实心格）就永远只有"没采到"。
            var inSide = StageContext.LevelPath == "Levels/World1-2" && p.FeetPosition.x >= faceX - 1f;
            if (inSide)
            {
                nearSamples++;
                if (p.Busy) busySamples++;
                var bad = InSolid();          // `solid` = 碰撞盒内缩 0.05 后是否压到实心格
                if (bad) nearSolid++;
                if (p.Bounds.xMax > nearMaxRight) nearMaxRight = p.Bounds.xMax;
                if (p.Bounds.xMax < nearMinRight) nearMinRight = p.Bounds.xMax;
                if (nearSamples <= 5 || bad || p.Bounds.xMax >= faceX - 0.01f)
                    L($"PROBE section12 管口前逐采样 {nearSamples} x={p.FeetPosition.x:F3} " +
                      $"box=[{p.Bounds.xMin:F3},{p.Bounds.xMax:F3}] busy={p.Busy} solid={bad} fsm={FsmNow}");
            }
            // ★ 加"还有没有活会话"的守卫（2026-09-19 17:40 实测：这一张也被 12742 字节的空帧覆盖过）——
            //   判据 = 活关卡 + 人活着 + 相机已经跟到结尾区（没有会话时相机停在 x=0.00）。
            if (!shotSide && p.Busy && StageContext.LevelPath == "Levels/World1-2" && p.Alive
                && Camera.main != null && Camera.main.transform.position.x > 100f)
            {
                shotSide = true;
                L($"PROBE section12 走进侧向管口 pos=({p.FeetPosition.x:F2},{p.FeetPosition.y:F2}) busy=True");
                State("A1 走进侧向管口（过场第 1 段）");
                Shot("sec12-m1-sideenter");
            }
            if (StageContext.LevelPath == "Levels/World1-2-Surface") break;
        }
        _stub.Hold(GameKey.RightArrow, false);
        // 判据（脚本算，不吃"我看没进去"）：采样窗口内每一次采样都不得身处实心格，
        // 且右边缘不得越过管口面（0.001 格容差 = 浮点）。
        L($"PROBE section12 管口前逐采样 判据：采样 {nearSamples} 次（其中过场 {busySamples} 次，2 毫秒/次），" +
          $"身处实心格命中 {nearSolid} 次，右边缘 [{nearMinRight:F3},{nearMaxRight:F3}]，管口面 {faceX} " +
          $"⇒ {(nearSamples > 0 && nearSolid == 0 && nearMaxRight <= faceX + 0.001f ? "PASS" : "FAIL")}");
        L($"PROBE section12 切段后 关卡={StageContext.LevelPath} fsm={FsmNow}");

        // ── A3：地表段就位 —— 先抓"升起中"那一帧（0.02 秒轮询；这一段只有 0.5 格 / 2.5 格每秒 = 0.2 秒），
        //       抓到了再拍"站在管顶"。抓不到会打一条警告（不假装拍到了）。──
        var shotRise = false;
        for (var i = 0; i < 800 && !shotRise; i++)
        {
            var q = StageContext.Player;
            if (FsmNow == "Stage" && q != null && q.Busy)
            {
                shotRise = true;
                // 等一下再拍：刚刚切段时相机还在上一段的取景上（升起过场期间相机才归位），
                // 立刻拍会得到单色空帧（实测 12706 字节）。等 0.06 秒，马里奥仍在升起中（全程 0.2 秒）。
                await Wait(0.06f);
                var q2 = StageContext.Player;
                L($"PROBE section12 升起中 第 {i} 次轮询(0.02s) pos=({(q2 == null ? "-" : PosStr(q2))}) " +
                  $"busy={(q2 != null && q2.Busy)}");
                State("A3 从出管口升起中（过场第 2 段）");
                Shot("sec12-d1-rise");
                break;
            }
            await Wait(0.02f);
        }
        if (!shotRise) L("PROBE 警告：16 秒窗口内没抓到「升起中」的帧（Busy 一直没为真）");
        for (var i = 0; i < 80 && FsmNow != "Stage"; i++) await Wait(0.25f);
        await Wait(0.8f);
        var sl = StageContext.Level;
        L($"PROBE section12 地表段就位 fsm={FsmNow} 出生=({sl.SpawnX},{sl.SpawnY}) " +
          $"升起终点=({sl.PipeRiseX},{sl.PipeRiseY}) hasRise={sl.HasPipeRise} groundTop={sl.GroundTopY} " +
          $"旗杆X={sl.FlagpoleX} 城堡X={sl.CastleDoorX} 瓦片={sl.Data?.Tiles.Count}");
        var p1 = StageContext.Player;
        L($"PROBE section12 站在管顶 pos={(p1 == null ? "-" : PosStr(p1))} busy={(p1 != null && p1.Busy)} " +
          $"grounded={(p1 != null && p1.Grounded)}");
        // 地表段的 BGM 是主世界主题（clone `World 1-2 - Castle Cut.unity` 引用 01-main-theme-overworld）
        L($"PROBE bgm[1-2 地表段]：{BgmState()}");
        State("A3 站在出管口顶");
        Shot("sec12-d-surface-spawn");

        // 走下管顶 → 站在台阶前
        _stub.Hold(GameKey.RightArrow, true);
        for (var i = 0; i < 30; i++)
        {
            await Wait(0.1f);
            var p = StageContext.Player;
            if (p == null) break;
            if (p.FeetPosition.x >= 1.7f && p.Grounded) break;
        }
        _stub.Hold(GameKey.RightArrow, false);
        await Wait(0.5f);
        State("A3 台阶前");
        Shot("sec12-e-stairbase");

        // 边右走边跳，上台阶（1..8 级）
        _stub.Hold(GameKey.RightArrow, true);
        for (var i = 0; i < 90; i++)
        {
            var q0 = StageContext.Player;
            var onStairs = q0 != null && q0.FeetPosition.x < 11.2f;
            if (onStairs && i % 3 == 0) _stub.Hold(GameKey.Space, true);
            if (onStairs && i % 3 == 2) _stub.Hold(GameKey.Space, false);
            await Wait(0.2f);
            var p = StageContext.Player;
            if (p == null) break;
            if (i % 3 == 0)
                L($"PROBE section12 上台阶 t={i * 0.2:F1} x={p.FeetPosition.x:F2} y={p.FeetPosition.y:F2} grounded={p.Grounded}");
            if (i == 4) { State("A3 台阶中段"); Shot("sec12-f-stairs"); }
            if (p.FeetPosition.x > 12.0f) break;
        }
        _stub.Hold(GameKey.Space, false);
        await Wait(0.8f);
        State("A3 台阶顶");
        Shot("sec12-g-stairtop");
        _stub.Hold(GameKey.RightArrow, false);

        // 继续右走到旗杆 → 城堡 → 结算
        var shotFlag = false;
        var shotCastle = false;
        _stub.Hold(GameKey.RightArrow, true);
        for (var i = 0; i < 120; i++)
        {
            await Wait(0.2f);
            // ⚠️ Result 一进就有人会问 StageContext.Player == null（关卡会话已拆），
            // 所以"结算"判定必须在判空之前 —— 否则永远拍不到结算屏（实测踩过）。
            // ⚠️ ShotSolid：进 Result 的头几帧面板还没画（实测直接 Shot 得到 10541 字节的**纯蓝天**空帧，
            //    与"结算屏"完全无关）。重试到画面有内容为止。
            if (FsmNow == "Result") { State("A3 结算"); await ShotPanel("sec12-j-result"); break; }
            var p = StageContext.Player;
            if (p == null) { L("PROBE section12 尾声：玩家已释放，结束"); break; }
            if (i % 5 == 0)
                L($"PROBE section12 尾声 t={i * 0.2:F1} x={p.FeetPosition.x:F2} y={p.FeetPosition.y:F2} busy={p.Busy} fsm={FsmNow}");
            if (!shotFlag && p.FeetPosition.x > 17.5f) { shotFlag = true; State("A3 旗杆"); Shot("sec12-h-flag"); }
            if (!shotCastle && p.FeetPosition.x > 24f) { shotCastle = true; State("A3 城堡"); Shot("sec12-i-castle"); }
        }
        _stub.Hold(GameKey.RightArrow, false);
        State("section12 结束");
        await TailToMenu("section12");
    }

    // ───────────────────── 场景：关卡指令解析诊断 ─────────────────────

    public static void ParseCheck() => Start("parsecheck", ParseCheckBody);

    private static async Task ParseCheckBody()
    {
        await Enter12();
        if (!IsStage) { L("PROBE 没进到 1-2，放弃"); return; }
        var lv = StageContext.Level;
        L($"PROBE parsecheck 游戏里看到的：有旗杆={lv.HasFlagpole} 侧向管口={lv.HasSideExit} " +
          $"faceX={lv.SideExitFaceX} y={lv.SideExitY} 有出生点={lv.HasSpawn}");
        var path = Application.dataPath + "/Resources/Levels/World1-2.txt";
        L($"PROBE parsecheck 磁盘文件存在={System.IO.File.Exists(path)}");
        if (System.IO.File.Exists(path))
        {
            var txt = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
            L($"PROBE parsecheck 磁盘文件长度={txt.Length} 含 '# no-flagpole'={txt.Contains("# no-flagpole")} " +
              $"含 '# side-exit 163 3'={txt.Contains("# side-exit 163 3")}");
            foreach (var raw in txt.Split('\n'))
            {
                var t2 = raw.Trim();
                if (t2.StartsWith("# no-flagpole") || t2.StartsWith("# side-exit") || t2.StartsWith("# spawn") ||
                    t2.StartsWith("# flagpole") || t2.StartsWith("# castle"))
                    L($"PROBE parsecheck 磁盘指令 |{t2}|");
            }
        }
        await Wait(0.1f);
    }

    // ───────────────────── 场景：UI 诊断（面板到底画没画）─────────────────────

    public static void UiDiag() => Start("uidiag", UiDiagBody);

    private static async Task UiDiagBody()
    {
        await MenuReady();
        await Wait(1.5f);
        L($"PROBE ui fsm={FsmNow}");
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        L($"PROBE ui 画布数={canvases.Length}");
        foreach (var cv in canvases)
        {
            L($"PROBE ui canvas[{cv.name}] active={cv.gameObject.activeInHierarchy} enabled={cv.enabled} " +
              $"mode={cv.renderMode} order={cv.sortingOrder} scale={cv.scaleFactor} cam={(cv.worldCamera == null ? "null" : cv.worldCamera.name)} children={cv.transform.childCount}");
            for (var i = 0; i < cv.transform.childCount; i++)
            {
                var ch = cv.transform.GetChild(i);
                var rt = ch as RectTransform;
                L($"PROBE ui   child[{i}] {ch.name} active={ch.gameObject.activeInHierarchy} children={ch.childCount} " +
                  $"size={(rt == null ? Vector2.zero : rt.sizeDelta)} pos={(rt == null ? Vector2.zero : rt.anchoredPosition)}");
            }
        }
        var bgN = 0;
        foreach (var img in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Image>(FindObjectsInactive.Include))
        {
            if (img.gameObject.name != "BG") continue;
            bgN++;
            var rt = img.rectTransform;
            L($"PROBE ui BG#{bgN} color={img.color} size={rt.sizeDelta} anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} " +
              $"active={img.gameObject.activeInHierarchy} enabled={img.enabled}");
        }
        L($"PROBE ui BG 数={bgN}");
        // 直接问：预制体能加载吗？组件在吗？Open 之后有实例吗？
        var prefab = Resources.Load<GameObject>("UI/MainMenuPanel");
        var hasComp = prefab != null && prefab.GetComponent<SuperMario.UI.MainMenuPanel>() != null;
        L($"PROBE ui prefab=UI/MainMenuPanel -> {(prefab == null ? "null" : prefab.name)} 组件={(hasComp ? "有" : "缺")}");
        var got = Game.UI.Get<SuperMario.UI.MainMenuPanel>();
        var isOpen = Game.UI.IsOpen<SuperMario.UI.MainMenuPanel>();
        var stale = "?";
        if (got != null) stale = (got.gameObject == null ? "是（假 null，已被销毁）" : "否");
        L($"PROBE ui 注册表：IsOpen={isOpen} Get={(got == null ? "null" : got.GetType().Name)} 已销毁={stale} " +
          $"场景里实例数={CountByName("MainMenuPanel")}");
        L($"PROBE ui 打开前 MainMenuPanel 实例数={CountByName("MainMenuPanel")}");
        Game.UI.Open<SuperMario.UI.MainMenuPanel>();
        await Wait(1.0f);
        L($"PROBE ui 打开后 MainMenuPanel 实例数={CountByName("MainMenuPanel")}");
        // 递归转储（一层看不出问题：面板可能是"活着但内容没建"）
        foreach (var cv in canvases)
        {
            DumpNode(cv.transform, "  ", 0);
        }
        await ShotSolid("ui_diag");
    }

    private static int CountByName(string prefix)
    {
        var n = 0;
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t != null && t.gameObject.name.StartsWith(prefix, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>
    /// 面板"到底在不在画面上"的权威判据（三条一起看，任一条都能独立否掉"没画出来"）：
    /// ① 注册表 <c>IsOpen&lt;T&gt;()</c> / <c>Get&lt;T&gt;()</c>；② 场景内实例数；③ 递归节点树（含 TEXT 内容与层级）。
    /// </summary>
    private static void DumpPanel<T>(string tag, string name) where T : class, CloverEngine.IUIPanel
    {
        var open = Game.UI.IsOpen<T>();
        var got = Game.UI.Get<T>();
        var layer = got == null ? "?" : got.Layer.ToString();
        L($"PROBE panel[{tag}] {name}: IsOpen={open} Get={(got == null ? "null" : "有")} " +
          $"场景内实例数={CountByName(name)} 层={layer} active={LayerActive(layer)}");
        if (got != null && got.Root != null) DumpNode(got.Root.transform, "  ", 0);
    }

    /// <summary>该层节点在层级里是否激活（面板挂得上但父层被关也一样看不见，所以要一起打）。</summary>
    private static string LayerActive(string layerName)
    {
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t != null && t.gameObject.name == layerName && t.parent != null && t.parent.gameObject.name == "[UI]")
                return t.gameObject.activeInHierarchy.ToString();
        return "无该层";
    }

    private static void DumpPanels(string tag)
    {
        DumpPanel<SuperMario.UI.ResultPanel>(tag, "ResultPanel");
        DumpPanel<SuperMario.UI.PausePanel>(tag, "PausePanel");
        DumpPanel<SuperMario.UI.GameOverPanel>(tag, "GameOverPanel");
    }

    // ── E-19 音量条：数值 ↔ 画面互相印证（只报数不算证据，必须和截图里的宽度对得上）──
    private static void SetVol(string nodeName, float v)
    {
        var pp = Game.UI.Get<SuperMario.UI.PausePanel>();
        var t = pp == null ? null : pp.transform.Find(nodeName);
        var sl = t == null ? null : t.GetComponent<UnityEngine.UI.Slider>();
        if (sl == null) { L($"PROBE vol：找不到 {nodeName} 的 Slider"); return; }
        sl.value = v;
        L($"PROBE vol 设 {nodeName}.value={v:F2}");
    }

    private static void VolReport(string tag)
    {
        var pp = Game.UI.Get<SuperMario.UI.PausePanel>();
        if (pp == null) { L($"PROBE vol[{tag}]：PausePanel 为 null"); return; }
        foreach (var grp in new[] { "BGM", "SFX" })
        {
            var t = pp.transform.Find("Vol_" + grp);
            var sl = t == null ? null : t.GetComponent<UnityEngine.UI.Slider>();
            var area = pp.transform.Find("Vol_" + grp + "/Fill Area") as RectTransform;
            var fill = pp.transform.Find("Vol_" + grp + "/Fill Area/Fill") as RectTransform;
            var g = grp == "BGM" ? SoundGroup.BGM : SoundGroup.SFX;
            var vol = Game.Sound == null ? -1f : Game.Sound.GetVolume(g);
            var fw = fill == null ? -1f : fill.rect.width;
            var aw = area == null ? -1f : area.rect.width;
            L($"PROBE vol[{tag}] {grp}：value={(sl == null ? -1f : sl.value):F3} " +
              $"填充宽={fw:F1}px / 轨道宽={aw:F1}px（比={(aw > 0f ? fw / aw : -1f):F3}） " +
              $"anchorMax={(fill == null ? Vector2.zero : fill.anchorMax)} 音源音量={vol:F3}");
        }
    }

    private static void DumpNode(Transform t, string indent, int depth)
    {
        if (depth > 3) return;
        var rt = t as RectTransform;
        var img = t.GetComponent<UnityEngine.UI.Image>();
        var txt = t.GetComponent<UnityEngine.UI.Text>();
        var extra = "";
        if (img != null) extra += $" IMG(color={img.color} sprite={(img.sprite == null ? "null" : img.sprite.name)} enabled={img.enabled})";
        if (txt != null) extra += $" TXT(\"{(txt.text == null ? "" : txt.text.Substring(0, Mathf.Min(12, txt.text.Length)))}\" color={txt.color} font={(txt.font == null ? "null" : txt.font.name)})";
        L($"PROBE ui{indent}{t.gameObject.name} active={t.gameObject.activeSelf} " +
          $"size={(rt == null ? Vector2.zero : rt.sizeDelta)} pos={(rt == null ? Vector2.zero : rt.anchoredPosition)}{extra}");
        for (var i = 0; i < t.childCount; i++) DumpNode(t.GetChild(i), indent + "  ", depth + 1);
    }

    // ───────────────────── 场景：启动画面 / 标题屏 ─────────────────────

    public static void Boot() => Start("boot", BootBody);
    public static void Menu() => Start("menu", MenuBody);

    private static async Task BootBody()
    {
        // Boot 只在启动瞬间约 1.8 秒；探针起来时通常已过 ⇒ 走 AppFlow 的真实入口重进一次
        Game.Fsm.Transition(FlowState.Boot);
        await Wait(0.8f);
        L($"PROBE 已切 Boot 状态：fsm={FsmNow}");
        L($"PROBE boot fsm={FsmNow} timeScale={Time.timeScale}");
        // 闸门：这一张要证的是**启动画面**，状态不在 Boot 就会拍到别的屏（见 ShotGated 的说明）
        await ShotGated("v2_boot", "#1 启动画面", () => FsmNow == "Boot");
        await Wait(1.4f);
        L($"PROBE boot 之后 fsm={FsmNow}");
    }

    private static async Task MenuBody()
    {
        await MenuReady();
        State("菜单第 1 次");
        // ⚠️ 这一场 2026-09-19 12:28 出过事：场景在 `fsm=Loading` 时启动 ⇒ 拍出 12 张**入场卡**
        //    当标题屏，并覆盖掉原来的好帧。出图前必须有 `MenuGate()`（fsm=Menu + 关卡会话已释放）。
        await ShotGated("t2_menu_run1", "#2 标题屏（连续两次进 Play 的第 1 次）", MenuGate);
        await ShotGated("t3_top", "#2 标题屏（同一屏的第 2 张）", MenuGate);
        // 同一次会话里再进一次菜单：域重载静态残留会在这里露出来（两次字节应一致）
        Game.Event.Emit(SuperMario.Core.Events.CharChosen, 1);
        await WaitStage(25f);
        Game.Event.Emit(SuperMario.Core.Events.BackToMain);
        for (var i = 0; i < 90 && FsmNow != "Menu"; i++) await Wait(0.2f);
        await Wait(0.8f);
        State("菜单第 2 次");
        await ShotGated("t2_menu_run2", "#2 标题屏（同一会话里第 2 次回菜单）", MenuGate);
    }

    // ───────────────────── 场景：引擎署名（§1.6）─────────────────────
    //
    // 为什么要有这一场：闸门 `engine-credit` 以前只 grep **源码字符串**，而实际出的两个缺陷
    // 恰恰是"源码对、画面错" —— ① 标题屏根本没有这一行；② 启动画面那行的源码文本是对的，
    // 但字体只有大写字形 ⇒ 画面上是 `BY CLOVER-ENGINE`。所以判据换成**运行时 UI 节点树上读**：
    // 面板 / 节点 / 实际 text / 实际字体 / 是否激活 / 锚点，落成
    // `Assets/Screenshots/credit-render.txt`，由 `tools/verify.ps1` 逐字断言。
    //
    // 三个入口（第三个、第四个是**反向自检**：证明闸门抓得住，不是只证明"现在是绿的"）：
    //   credit         正常测 —— 交付用的报告（头一行写 tamper=none）
    //   credit-case    自检①：标题屏那行临时改成全大写 ⇒ 闸门必须变红
    //   credit-missing 自检②：标题屏那行临时隐藏     ⇒ 闸门必须变红
    public static void Credit() => Start("credit", () => CreditBody(CreditTamper.None));
    public static void CreditCase() => Start("credit-case", () => CreditBody(CreditTamper.Uppercase));
    public static void CreditMissing() => Start("credit-missing", () => CreditBody(CreditTamper.Missing));

    private enum CreditTamper { None, Uppercase, Missing }

    private const string CreditReport = "../.ai-tmp/screenshots/credit-render.txt";

    /// <summary>§1.6 要求逐字（含大小写）。这里是**判据侧的字面量** —— 故意不从业务常量取：
    /// 判据自己写死期望值，才不会跟着被验对象的改动一起漂（skill §1.12 第 1、4 条）。</summary>
    private const string CreditWant = "by clover-engine";

    /// <summary>本次是不是"反向自检"跑（空 = 正式测量）。决定裁块落盘的位置与文件名 ——
    /// 正式测量落 `.ai-tmp/screenshots/`，自检落 `.ai-tmp/test/`（见 CreditShot 里的说明）。</summary>
    private static string CreditTamperTag = "";

    private static UnityEngine.UI.Text CreditLabelOf<T>(string childName) where T : class, CloverEngine.IUIPanel
    {
        var p = Game.UI.Get<T>();
        if (p == null || p.Root == null) return null;
        var tr = p.Root.transform.Find(childName);
        return tr == null ? null : tr.GetComponent<UnityEngine.UI.Text>();
    }

    private static async Task AppendCredit(System.Text.StringBuilder sb, string scope, string panelName,
                                           UnityEngine.UI.Text t, bool panelOpen)
    {
        var found = t != null;
        var active = found && t.gameObject.activeInHierarchy;
        var txt = found ? (t.text ?? "") : "";
        var fontName = found && t.font != null ? t.font.name : "null";
        var size = found ? t.fontSize : 0;
        var col = found ? t.color : Color.clear;
        var amin = found ? t.rectTransform.anchorMin : Vector2.zero;
        var amax = found ? t.rectTransform.anchorMax : Vector2.zero;
        var apos = found ? t.rectTransform.anchoredPosition : Vector2.zero;
        var piv = found ? t.rectTransform.pivot : Vector2.zero;
        var visible = found && t.enabled && active && t.color.a > 0.05f;
        var exact = txt == CreditWant;

        sb.Append($"credit|{scope}|panel={panelName} open={(panelOpen ? "true" : "false")} " +
                  $"label=Signature found={(found ? "true" : "false")} active={(active ? "true" : "false")} " +
                  $"visible={(visible ? "true" : "false")} exact={(exact ? "true" : "false")}\n");
        sb.Append($"credit|{scope}|text=\"{txt}\" len={txt.Length} wantLen={CreditWant.Length}\n");
        sb.Append($"credit|{scope}|font={fontName} wantFont=PressStart2P size={size} " +
                  $"color=({col.r:F2},{col.g:F2},{col.b:F2},{col.a:F2})\n");
        sb.Append($"credit|{scope}|anchor min=({amin.x:F2},{amin.y:F2}) max=({amax.x:F2},{amax.y:F2}) " +
                  $"pivot=({piv.x:F2},{piv.y:F2}) pos=({apos.x:F1},{apos.y:F1})\n");
        sb.Append($"credit|{scope}|fontMetrics {DescribeGlyphs(t)}\n");
        var px = await ShotBandFacts(scope, t);
        sb.Append($"credit|{scope}|pixels {px}\n");
    }

    /// <summary>
    /// 抓拍**重试包装**：`CaptureScreenshotAsTexture` 偶尔会抓到"这一行还没画上去"的那一帧
    /// （实测：启动画面第一次抓拍 ink=0，而节点树里那一行是 active/visible 的）。
    /// 判据是"像素里必须有墨"，所以取不到墨就重抓到上限，并把**墨最多的那一张**作为结果 ——
    /// 而不是把空帧当成"没有署名"（那会把工具缺陷误判成游戏缺陷，也会把游戏缺陷掩盖掉）。
    /// </summary>
    private static async Task<string> ShotBandFacts(string scope, UnityEngine.UI.Text t)
    {
        var best = "";
        var bestInk = -1.0;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            var s = CreditShot(scope, t);
            var ink = NumOf(s, "ink=", -1.0);
            if (ink > bestInk) { bestInk = ink; best = s; }
            if (ink >= 20.0) return s + $" attempt={attempt}";
            await Wait(0.35f);
        }
        L($"PROBE credit 警告：{scope} 那一行在 8 次抓拍里都没有墨迹（最大 ink={bestInk}）");
        return best + " attempt=8";
    }

    /// <summary>从测量串里取一个数值（找不到/解析失败 ⇒ 回默认值）。</summary>
    private static double NumOf(string s, string key, double def)
    {
        if (s == null) return def;
        var i = s.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return def;
        var j = i + key.Length;
        var k = j;
        while (k < s.Length && (char.IsDigit(s[k]) || s[k] == '-' || s[k] == '.' || s[k] == '+')) k++;
        double v;
        return double.TryParse(s.Substring(j, k - j), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out v) ? v : def;
    }

    /// <summary>
    /// 字体**字形**判据（判据要写进脚本，不许写"我看是小写"）：
    /// 逐字形比 a-z 与 A-Z 的轮廓盒 —— 本工程原来那份 NES 像素字体里两者**逐个相等**
    /// （实测 `b`/`B` 都是 (0,0,896,896)）⇒ 它就是"把小写画成大写"的根源。
    /// 现在这份署名专用字体必须**不相等**，且 `y` 必须**下探到基线以下**（真下伸部）。
    /// </summary>
    private static string DescribeGlyphs(UnityEngine.UI.Text t)
    {
        if (t == null || t.font == null) return "unavailable";
        try
        {
            t.font.RequestCharactersInTexture("bByY", t.fontSize, t.fontStyle);
            var b = Box(t, 'b'); var bb = Box(t, 'B'); var y = Box(t, 'y'); var yy = Box(t, 'Y');
            var differ = b != bb || y != yy;
            var descends = GlyphMinY(t, 'y') < 0;
            return $"b={b} B={bb} y={y} Y={yy} lowercaseShaped={(differ && descends ? "true" : "false")} " +
                   $"lowerVsUpperDiffer={(differ ? "true" : "false")} yDescendsPx={GlyphMinY(t, 'y')}";
        }
        catch (Exception e)
        {
            return $"unavailable({e.GetType().Name})";
        }
    }

    private static CharacterInfo Info(UnityEngine.UI.Text t, char c)
    {
        CharacterInfo ci;
        t.font.GetCharacterInfo(c, out ci, t.fontSize, t.fontStyle);
        return ci;
    }

    private static string Box(UnityEngine.UI.Text t, char c)
    {
        var i = Info(t, c);
        return $"({i.minX},{i.minY},{i.maxX},{i.maxY})";
    }

    private static int GlyphMinY(UnityEngine.UI.Text t, char c) => Info(t, c).minY;

    /// <summary>本行在**屏幕**上的矩形（像素，原点左下）——由 GetWorldCorners 换到屏幕坐标，
    /// 这样"截哪一块""离底多少""是否居中"都不靠猜（判据交给量出来的数）。</summary>
    private static bool ScreenRectOf(UnityEngine.UI.Text t, out Rect r)
    {
        r = new Rect(0, 0, 0, 0);
        if (t == null) return false;
        var canvas = t.canvas;
        var cam = (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : canvas.worldCamera;
        var corners = new Vector3[4];
        t.rectTransform.GetWorldCorners(corners);
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        for (var i = 0; i < 4; i++)
        {
            var sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            min = Vector2.Min(min, sp); max = Vector2.Max(max, sp);
        }
        r = new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        return r.width >= 4f && r.height >= 4f;
    }

    /// <summary>
    /// **渲染像素**判据 —— 先把**这一行自己的矩形**从活画面上裁下来（不猜坐标），再按"行墨迹剖面"判大小写：
    ///   · 真小写：x 高字母（c o v e r n …）比升部字母（b l）**矮 4 像素** ⇒
    ///     墨迹带的**顶 3 行**只有 b/l/i/g 那几个字的墨 ⇒ 顶行墨量远小于中段行；
    ///   · 只有大写字形：每个字的轮廓盒一样高（本工程旧字体实测 896/1024 em，逐个相同）⇒
    ///     顶行与中段行墨量几乎相同（≈0.9）。
    /// 裁下来的那块**存成图**（`credit-crop-<scope>.png`）：既是证据，也让离线脚本能独立复核同一批像素。
    /// ⚠️ 不能用"画面底部 1/4"当范围 —— 标题屏底部还有 `TOP- 005000`，会把两行混成一条带（实测踩过）。
    /// </summary>
    private static string CreditShot(string scope, UnityEngine.UI.Text t)
    {
        try
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex == null) return "unavailable(capture null)";
            var W = tex.width; var H = tex.height;
            // ★ 范围 = **屏幕最底部那一条**（不是标签自己的矩形）：
            //   判据本身就是"署名必须贴在底边"，所以范围就该是底边这一条。
            //   ⛔ 别再用"换算出标签矩形"那条路 —— 实测 `WorldToScreenPoint` 给出过 yMin=375 的错矩形，
            //   把屏幕顶部的 HUD 当成了署名带（ink=26286、hMax=95）⇒ 闸门在**画面对的情况下**报了 4 条 FAIL。
            //   教训：判据的范围要**与判据本身同义**，别经过一层容易错的换算。
            const int Strip = 64;
            var x0 = 0; var y0 = 0;
            var x1 = W; var y1 = Mathf.Min(H, Strip);
            var cw = x1 - x0; var ch = y1 - y0;
            var px = tex.GetPixels(x0, y0, cw, ch);          // 索引 = (row*cw + col)，row 0 = 这一块的**下**边
            // 交付用的裁块落 `Assets/Screenshots/`（它是**实机渲染像素**的证据）；
            // 反向自检那几次的裁块落 `.ai-tmp/test/` 并带 `-tamper-<tag>` 后缀 ——
            // 自检是为了证明"闸门抓得住"，但**不许**把"故意改坏"的那张图留在交付目录里。
            var cropName = "credit-crop-" + scope + (CreditTamperTag == "" ? "" : "-tamper-" + CreditTamperTag) + ".png";
            var cropDir = CreditTamperTag == "" ? ShotDir : "../.ai-tmp/test";
            try
            {
                var crop = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                crop.SetPixels(px);
                crop.Apply();
                File.WriteAllBytes(cropDir + "/" + cropName, crop.EncodeToPNG());
                UnityEngine.Object.Destroy(crop);
            }
            catch (Exception e) { L($"PROBE credit 裁块落盘失败：{e.GetType().Name}: {e.Message}"); }
            UnityEngine.Object.Destroy(tex);

            // 底色 = 这一块里出现最多的颜色（标题屏=浅蓝紫 / 启动画面=黑）
            var hist = new Dictionary<int, int>();
            for (var i = 0; i < px.Length; i++)
            {
                var k = Key(px[i]);
                hist.TryGetValue(k, out var n);
                hist[k] = n + 1;
            }
            var bgKey = 0; var bgN = -1;
            foreach (var kv in hist) if (kv.Value > bgN) { bgN = kv.Value; bgKey = kv.Key; }
            var bg = ColorFromKey(bgKey);

            // 墨迹行剖面（先按行统计，用来找"最下面那一条连续墨带"）
            var rowInk = new int[ch];
            for (var r = 0; r < ch; r++)
                for (var c = 0; c < cw; c++)
                    if (IsInk(px[r * cw + c], bg)) rowInk[r]++;

            // ★ 只取**最下面那一条**连续墨带：署名是最底那行；
            //   把整条 64px 混在一起会把同一屏里的 `TOP- 005000` 也算进来（那条行高不同 ⇒ 小写判据失效）。
            // ⚠️ 行号方向：GetPixels 的 row 0 = 这一块的**下**边 ⇒ 行号越大越靠上。
            var rBot = -1; var rTop = -1;
            for (var r = 0; r < ch; r++)
            {
                if (rowInk[r] >= 3) { if (rBot < 0) rBot = r; rTop = r; }
                else if (rBot >= 0) break;
            }
            var stripInfo = $"strip=bottom{Strip}px crop={cropName} cropSize=({cw},{ch}) screen=({W},{H}) " +
                            $"bg=({(int)(bg.r * 255)},{(int)(bg.g * 255)},{(int)(bg.b * 255)})";
            if (rBot < 0)
                return $"ink=0 runs=0 hMax=0 shortRuns=0 topRatio=-1 bandRows=(0,0) bandBottomGapPx=-1 " +
                       $"screenCenterOffsetPx=-1 {stripInfo}";

            // 这一条带内的墨迹与列剖面（列的行跨度**只在带内算** —— 得到的就是这个字形的实际高度）
            var ink = 0;
            for (var r = rBot; r <= rTop; r++) ink += rowInk[r];
            var colTop = new int[cw]; var colBot = new int[cw];
            for (var c = 0; c < cw; c++) { colTop[c] = -1; colBot[c] = -1; }
            var inkMinX = cw; var inkMaxX = -1;
            for (var c = 0; c < cw; c++)
                for (var r = rBot; r <= rTop; r++)
                {
                    if (!IsInk(px[r * cw + c], bg)) continue;
                    if (colTop[c] < 0 || r > colTop[c]) colTop[c] = r;
                    if (colBot[c] < 0 || r < colBot[c]) colBot[c] = r;
                    if (c < inkMinX) inkMinX = c;
                    if (c > inkMaxX) inkMaxX = c;
                }

            var rMid = (rTop + rBot) / 2;
            double top3 = 0, mid3 = 0;
            for (var r = rTop; r > Mathf.Max(rTop - 3, -1); r--) top3 += rowInk[r];
            for (var r = Mathf.Max(rBot, rMid - 1); r <= Mathf.Min(rMid + 1, ch - 1); r++) mid3 += rowInk[r];
            var topRatio = mid3 <= 0 ? -1.0 : top3 / mid3;

            // 单字形（列段）高度：段内墨的行跨度
            var runs = new List<int>();
            var curTop = -1; var curBot = -1; var curN = 0;
            for (var c = 0; c <= cw; c++)
            {
                var has = c < cw && colTop[c] >= 0;
                if (has)
                {
                    if (curTop < 0) { curTop = colTop[c]; curBot = colBot[c]; }
                    else { curTop = Mathf.Max(curTop, colTop[c]); curBot = Mathf.Min(curBot, colBot[c]); }
                    curN++;
                }
                else if (curN > 0) { runs.Add(curTop - curBot + 1); curTop = -1; curBot = -1; curN = 0; }
            }
            var hMax = 0; foreach (var v in runs) hMax = Mathf.Max(hMax, v);
            // "矮字形"判据必须**与画布缩放无关**：画布 scale≈0.5 时 16px 的字在屏上只有 7px 高，
            // 用绝对差（hMax-3）会一个都算不上（实测 shortRuns=1 ⇒ 明明是正确的小写却被判"不像小写"）。
            // 改成比例：x 高字母 ≤ 0.8 × 升部字母高（'by clover-engine' 里 10/15 个字母是 x 高）。
            var shortRuns = 0; foreach (var v in runs) if (v * 5 <= hMax * 4) shortRuns++;
            if (runs.Count > 0 && hMax <= 4) shortRuns = -1;   // 字太小 ⇒ 比例判据不可靠，交给闸门判"不可判"
            var centerOff = inkMaxX < 0 ? -1 : Mathf.RoundToInt((inkMinX + inkMaxX) * 0.5f - W * 0.5f);
            return $"ink={ink} runs={runs.Count} hMax={hMax} shortRuns={shortRuns} " +
                   $"topRatio={topRatio:F3} bandRows=({rBot},{rTop}) bandBottomGapPx={rBot} " +
                   $"screenCenterOffsetPx={centerOff} {stripInfo}";
        }
        catch (Exception e)
        {
            return $"unavailable({e.GetType().Name}:{e.Message})";
        }
    }

    private static bool IsInk(Color c, Color bg)
        => Mathf.Max(Mathf.Abs(c.r - bg.r), Mathf.Max(Mathf.Abs(c.g - bg.g), Mathf.Abs(c.b - bg.b))) > 0.25f;

    private static int Key(Color c)
        => ((int)(c.r * 255f) << 16) | ((int)(c.g * 255f) << 8) | (int)(c.b * 255f);

    private static Color ColorFromKey(int k)
        => new Color(((k >> 16) & 255) / 255f, ((k >> 8) & 255) / 255f, (k & 255) / 255f);

    private static async Task CreditBody(CreditTamper tamper)
    {
        CreditTamperTag = tamper == CreditTamper.None ? "" : tamper.ToString().ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        sb.Append("tamper=" + (CreditTamperTag == "" ? "none" : CreditTamperTag) + "\n");

        await MenuReady();
        await Wait(1.2f);

        var menuLabel = CreditLabelOf<SuperMario.UI.MainMenuPanel>("Signature");
        if (menuLabel == null) L("PROBE credit 警告：标题屏上找不到 Signature 节点");
        else if (tamper == CreditTamper.Uppercase)
        {
            menuLabel.text = "BY CLOVER-ENGINE";
            L("PROBE credit 反向自检：标题屏那行文本已临时改成 BY CLOVER-ENGINE");
        }
        else if (tamper == CreditTamper.Missing)
        {
            menuLabel.gameObject.SetActive(false);
            L("PROBE credit 反向自检：标题屏那行已临时隐藏");
        }
        await Wait(0.8f);
        await AppendCredit(sb, "menu", "MainMenuPanel", menuLabel, Game.UI.IsOpen<SuperMario.UI.MainMenuPanel>());

        // 启动画面那行：同一份报告里一起测 —— 闸门要求**两处都过**（§1.6：首页是硬要求，
        // 启动画面也要能指到实机证据）。
        Game.Fsm.Transition(FlowState.Boot);
        await Wait(1.4f);
        var bootLabel = CreditLabelOf<SuperMario.UI.BootPanel>("Signature");
        if (bootLabel == null) L("PROBE credit 警告：启动画面上找不到 Signature 节点");
        await AppendCredit(sb, "boot", "BootPanel", bootLabel, Game.UI.IsOpen<SuperMario.UI.BootPanel>());

        File.WriteAllText(CreditReport, sb.ToString(), new System.Text.UTF8Encoding(false));
        if (CreditTamperTag != "")
        {
            // 自检那一份另存到 .ai-tmp/test/：报告本体永远是"最后一次跑"的结果，
            // 而反向自检要看的是"改坏之后闸门怎么报"，两份不能互相覆盖。
            try
            {
                File.WriteAllText("../.ai-tmp/test/credit-render-tamper-" + CreditTamperTag + ".txt",
                    sb.ToString(), new System.Text.UTF8Encoding(false));
            }
            catch (Exception e) { L("PROBE credit 自检报告副本落盘失败：" + e.GetType().Name + ": " + e.Message); }
        }
        L("PROBE credit 报告已写入 " + CreditReport + "（tamper=" + tamper + "）");
        foreach (var raw in sb.ToString().Split('\n'))
            if (raw.Trim().Length > 0) L("PROBE " + raw.Trim());
    }

    // ───────────────────── 场景：入场卡（含死亡重来的命数）─────────────────────

    public static void Intro() => Start("intro", IntroBody);

    private static async Task IntroBody()
    {
        await ToMenu();
        Game.Event.Emit(SuperMario.Core.Events.CharChosen, 1);
        var t0 = Time.realtimeSinceStartup;
        while (FsmNow != "Loading" && Time.realtimeSinceStartup - t0 < 20f) await Wait(0.1f);
        await Wait(0.7f);
        L($"PROBE intro 入场卡 fsm={FsmNow} 命={StageContext.Score?.Lives}");
        // 入场卡 = 纯黑底 + WORLD 1-1 + ×命数 ⇒ 判据是"大面积深色"，不是"不是单色"
        await ShotPanel("intro_card");
        await WaitStage(25f);
        // 掉坑死一次 → 重来会再过一次入场卡，此时卡上命数应为剩余命数
        var px = StageContext.Player.FeetPosition.x;
        Teleport(px, -25f);
        await Wait(3.5f);
        var t1 = Time.realtimeSinceStartup;
        while (FsmNow != "Loading" && Time.realtimeSinceStartup - t1 < 25f) await Wait(0.1f);
        await Wait(0.7f);
        L($"PROBE intro 重来入场卡 fsm={FsmNow} 命={StageContext.Score?.Lives}");
        // ⚠️ 必须用"大面积深色"判据（ShotPanel）：入场卡是黑底，用 ShotSolid 的稀疏网格判空帧
        //    会把它判成空帧并**继续覆盖同名文件**，最后留下的是关卡画面（实测踩过）。
        await ShotPanel("intro_lives_check");
        await WaitStage(25f);
    }

    // ───────────────────── 场景：蹲下三态 ─────────────────────

    public static void Crouch() => Start("crouch", CrouchBody);

    private static async Task CrouchBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        _stub.Hold(GameKey.DownArrow, true);
        await Wait(0.8f);
        State("小形态按蹲");
        Shot("cr2_small");
        _stub.Hold(GameKey.DownArrow, false);
        await Wait(0.4f);

        StageContext.Player.PowerUp(PowerState.Big);
        await Wait(1.2f);
        _stub.Hold(GameKey.DownArrow, true);
        await Wait(0.8f);
        State("大形态蹲下");
        Shot("cr2_big");
        _stub.Hold(GameKey.DownArrow, false);
        await Wait(0.4f);

        StageContext.Player.PowerUp(PowerState.Fire);
        await Wait(1.2f);
        _stub.Hold(GameKey.DownArrow, true);
        await Wait(0.8f);
        State("火形态蹲下");
        Shot("crouch_fire");
        _stub.Hold(GameKey.DownArrow, false);
        await Wait(0.4f);
        State("蹲下三态结束");
    }

    // ───────────────────── 场景：栗宝宝位移 + HUD 整屏 ─────────────────────

    public static void Goomba() => Start("goomba", GoombaBody);

    private static async Task GoombaBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        var g = FindEnemy("Goomba", 10f);
        if (g == null) { L("PROBE 找不到栗宝宝"); return; }
        Teleport(P(g).x - 3.5f, -3f);
        await Wait(0.8f);
        L($"PROBE goomba x0={P(g).x:F3} 马里奥={PosStr(StageContext.Player)}");
        Shot("v4_stage");
        await Wait(1.5f);
        L($"PROBE goomba x1={P(g).x:F3} 位移={P(g).x - 0f:F3}");
        Shot("v4_stage2");
        State("HUD 整屏");
        Shot("final_stage");
    }

    /// <summary>
    /// 「跳上移动平台」——**真跳**，不是把人摆上去。
    /// <para>用户 2026-09-19 点名：「你是直接放上去测试的吗？不是跳上的吗？」
    /// —— 既有的 `platform` 场景确实是 `Teleport(台面顶)`（直接摆），没走"跳"这条路，所以
    /// 「跳不上去」这个 bug 一直没被测到。这个入口改成：站到平台那一列的地面上 → 等台面出现在
    /// 头顶 1.5~4 格 → **真按跳键**（`Jump()`）→ 逐帧读"脚底 − 台面中心"看是否落在台面上并跟着走。</para>
    /// </summary>
    public static void PlatJump() => Start("platjump", PlatJumpBody);

    private static async Task PlatJumpBody()
    {
        await Enter12();
        if (!IsStage) { L("PROBE 没进到 1-2，放弃"); return; }

        // ⚠️ 必须站在**平台 3 格宽之外**（台面 x 跨度 = 列心 ±1.5）：站在正下方会被台面一路托上去
        //    （第一版就是这样：25 秒里人被托到 y=12.7 的上层走廊，等不到"台面在头顶"那一刻）。
        //    站外侧 + 向右起跳 = 玩家真正做的动作。
        var standX = SafeGroundX(150.0f, 0f);
        Teleport(standX, 0f);
        await Wait(0.8f);
        L($"PROBE 站位 x={standX:F2} y=0 马里奥脚底={StageContext.Player.FeetPosition.y:F2} " +
          $"（平台列 x=152.80，台面跨 151.3..154.3 ⇒ 横向差 {Mathf.Abs(standX - 152.8f):F2} 格）");

        MonoBehaviour plat = null;
        for (var i = 0; i < 260; i++)
        {
            plat = FindByName("MovingPlatform", 152.8f);
            if (plat != null)
            {
                var dy = P(plat).y - StageContext.Player.FeetPosition.y;
                if (dy > 1.0f && dy < 3.0f) break;      // 台面在头顶 1~3 格：跳得上去的高度
            }
            await Wait(0.05f);
        }
        if (plat == null) { L("PROBE 等不到合适的台面（x≈152.8）"); return; }
        L($"PROBE 台面就位 platY={P(plat).y:F2} 距人脚底 {P(plat).y - StageContext.Player.FeetPosition.y:F2} 格 ⇒ 真跳");

        _stub.Hold(GameKey.RightArrow, true);   // 向右助跑，与玩家同一动作
        await Wait(0.12f);
        await Jump(0.34f);                      // ★ 真按键（与玩家同一条路）
        await Wait(0.25f);
        _stub.Hold(GameKey.RightArrow, false);
        for (var i = 0; i < 10; i++)
        {
            await Wait(0.12f);
            var m = StageContext.Player;
            var p = FindByName("MovingPlatform", 152.8f);
            var py = p == null ? -99f : P(p).y;
            L($"PROBE jump t={(i + 1) * 0.12f:F2} platY={py:F2} marioFeet={m.FeetPosition.y:F2} " +
              $"差={m.FeetPosition.y - py:F2} grounded={m.Grounded} alive={m.Alive}");
        }
        Shot("platjump");
        State("跳跃测试后");
        await Wait(0.8f);
    }

    /// <summary>
    /// 顶砖两条规则的取证（用户 2026-09-20）：
    /// A. 砖顶的**敌人** ⇒ 顶一下翻飞（在 1-1 用从地面跳得到的 ? 块测，`Block.HitFromBelow` 对
    ///    所有块类型都调 `KillEnemiesOnTop`，出处 clone `_common/CollectibleBlock.cs:38-46`）；
    /// B. 砖顶的**金币** ⇒ 弹出金币动画 + 飘分 + 计币（全工程只有 **1-2 金币房**同时有砖和金币：
    ///    `World1-2-Underground Brick=1 Coin=17`；1-1 金币房的墙/平台是瓦片，不是可顶的块）。
    /// </summary>
    public static void BrickCoin() => Start("brickcoin", BrickCoinBody);

    /// <summary>反射读 Block 组件的私有 _kind / Tile（块类型不在任何公开 API 上，探针专用）。</summary>
    private static string BlockKind(MonoBehaviour mb)
        => mb.GetType()
             .GetField("_kind", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
             ?.GetValue(mb)?.ToString();

    private static Vector2Int BlockTile(MonoBehaviour mb)
    {
        // ★ 从**对象名**解析，别去反射 Tile —— 它是属性不是字段，反射拿到 null 会静默返回 (0,0)，
        //   于是"把马里奥送到块下面"变成送到 x=0.5 顶空气，A/B 全成假阴性（实测 2026-09-20）。
        //   名字形如 `Block_<Kind>_<x>_<y>`（BlockModule 建块时就叫这个）。
        var parts = mb.gameObject.name.Split('_');
        if (parts.Length >= 4
            && int.TryParse(parts[parts.Length - 2], out var x)
            && int.TryParse(parts[parts.Length - 1], out var y))
            return new Vector2Int(x, y);
        return Vector2Int.zero;
    }

    private static MonoBehaviour FindBlock(string kind, out Vector2Int cell)
    {
        cell = Vector2Int.zero;
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb == null || !mb.gameObject.name.StartsWith("Block_", StringComparison.Ordinal)) continue;
            if (BlockKind(mb) != kind) continue;
            cell = BlockTile(mb);
            return mb;
        }
        return null;
    }

    private static MonoBehaviour FindItem(string typeName)
    {
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            if (mb != null && mb.GetType().Name == typeName) return mb;
        return null;
    }

    private static int CountItem(string typeName)
    {
        var n = 0;
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            if (mb != null && mb.GetType().Name == typeName) n++;
        return n;
    }

    // 注：不把"向下找地板"抽成带 ILevel 参数的辅助函数 —— 探针文件里没有 ILevel 的 using，
    // 显式写类型名会让整段脚本编译失败（实测：run_script 返回空、连"场景开始"都没打）。
    // 逻辑就地写在场景体里，用 var 推断类型。

    private static async Task BrickCoinBody()
    {
        // ── A：1-1 从地面跳得到的 ? 块，砖顶摆一只栗宝宝，顶砖看它是否翻飞 ──
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        var lv1 = StageContext.Level;

        // 只挑"从地面跳得到"的块：块底面（= 格底 y）距地面顶 ≤ 5 格（跳起约 4.6 格、头顶高出脚底 0.75）。
        MonoBehaviour box = null;
        var boxCell = Vector2Int.zero;
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb == null || !mb.gameObject.name.StartsWith("Block_", StringComparison.Ordinal)) continue;
            var kind = BlockKind(mb);
            if (kind != "QuestionBlockMushroom" && kind != "QuestionBlock") continue;
            var c = BlockTile(mb);
            if (c.y > lv1.GroundTopY + 5f) continue;
            box = mb;
            boxCell = c;
            break;
        }
        L($"PROBE A 选中块：{(box == null ? "无" : box.gameObject.name)} 格({boxCell.x},{boxCell.y}) 地面顶={lv1.GroundTopY:F1}");

        if (box != null)
        {
            await ClearGoombasNear(boxCell.x + 0.5f, 9f);
            Teleport(boxCell.x + 0.5f, lv1.GroundTopY);
            await Wait(0.5f);

            MonoBehaviour foe = null;
            foreach (var mb in AllEnemies())
            {
                if (!mb.gameObject.name.StartsWith("Goomba", StringComparison.Ordinal)) continue;
                if (mb is SuperMario.Module.Entities.IEnemy e && e.Dead) continue;
                foe = mb;
                break;
            }
            var pts0 = StageContext.Score.Points;
            if (foe == null) L("PROBE A 场上没有可用的栗宝宝 ⇒ A 无法测");
            else
            {
                _stub.Hold(GameKey.Space, true);                 // 起跳
                // ★ 起跳到顶到砖这段时间里**持续**把它按在砖顶：栗宝宝自己会走（0.5 秒能走约 0.5 格），
                //   只摆一次的话，顶到砖那一刻它可能已走出这一格 ⇒ `StandingOnTop` 判不到（假阴性）。
                for (var i = 0; i < 14; i++)
                {
                    foe.transform.position = new Vector3(boxCell.x + 0.5f, boxCell.y + 1f, 0f);
                    if (i == 0)
                        L($"PROBE A 已把它按在砖顶 ({boxCell.x + 0.5f},{boxCell.y + 1f})，当前死={((SuperMario.Module.Entities.IEnemy)foe).Dead}");
                    await Wait(0.05f);
                }
                _stub.Hold(GameKey.Space, false);
                await Wait(0.4f);
                var dead = foe is SuperMario.Module.Entities.IEnemy fe && fe.Dead;
                L($"PROBE A 结果：敌人 Dead={dead}（应 True）、分 {pts0}→{StageContext.Score.Points}" +
                  $"（+{StageContext.Score.Points - pts0}，应含顶砖杀敌的 100）");
                Shot("brickcoin-a-enemy");
            }
        }

        // ── B：1-2 金币房（唯一"砖 + 金币"同框的关卡）—— 顶砖收走砖顶那枚金币 ──
        await Enter12();
        if (!IsStage) { L("PROBE 没进到 1-2 ⇒ B 放弃"); return; }
        Teleport(100.5f, 3.0f);                                  // 1-2 通密室那根管子（T x=100..101、顶面 3）
        await Wait(0.8f);
        _stub.Hold(GameKey.DownArrow, true);
        await Wait(0.35f);
        _stub.Hold(GameKey.DownArrow, false);
        // ★ 判"进没进金币房"要用 SubArea，**不能**用 Underground —— 1-2 主关本身就是地下关
        //   （under 恒为 true），拿它当条件会立刻通过，于是"在金币房里顶砖"实际是在 1-2 主关里顶砖
        //   （2026-09-20 就是这么测出一堆假阴性的）。
        for (var i = 0; i < 80 && !StageContext.SubArea; i++) await Wait(0.2f);
        await Wait(1.0f);
        L($"PROBE B 进密室：sub={StageContext.SubArea} under={StageContext.Underground} 关卡={StageContext.LevelPath}");
        if (!StageContext.SubArea) { L("PROBE B 没进到金币房 ⇒ B 放弃"); return; }

        var lv = StageContext.Level;
        var brick = FindBlock("Brick", out var cell);
        var coin = FindItem("StaticCoin");
        L($"PROBE B 房间内：砖={(brick == null ? "无" : brick.gameObject.name)} 格({cell.x},{cell.y})、" +
          $"静置金币={(coin == null ? "无" : "有")}（共 {CountItem("StaticCoin")} 枚）、地面顶={lv.GroundTopY:F1}");
        if (brick == null || coin == null) { L("PROBE B 缺砖或缺金币 ⇒ B 放弃"); return; }

        coin.transform.position = new Vector3(cell.x + 0.5f, cell.y + 1.5f, 0f);   // 砖顶那一格的中心
        await Wait(0.15f);
        // 砖下方第一块实心格的顶面 = 马里奥能站着跳起来顶到这块砖的位置
        var stand = lv.GroundTopY;
        for (var y = cell.y - 1; y > cell.y - 25; y--)
            if (lv.IsSolidTile(cell.x, y)) { stand = y + 1f; break; }
        var p0 = StageContext.Score.Points;
        var c0 = StageContext.Score.Coins;
        L($"PROBE B 金币已摆到砖顶上方；砖下可站地面顶={stand:F1}（砖底面 y={cell.y}，差 {cell.y - stand:F1} 格）");
        Teleport(cell.x + 0.5f, stand);
        await Wait(0.5f);
        await Jump(0.5f);
        await Wait(0.9f);
        Shot("brickcoin-b-coin");
        L($"PROBE B 结果：分 {p0}→{StageContext.Score.Points}（+{StageContext.Score.Points - p0}，应 200）、" +
          $"币 {c0}→{StageContext.Score.Coins}（应 +1）、CoinPop={CountItem("CoinPop")}（>0 = 走了弹出金币动画）");
        State("顶砖两条");
        await Wait(0.6f);
    }

    /// <summary>
    /// 1-1 第一个蘑菇块（`E 7.5 0.5 MysteryBoxMushroom`）顶出蘑菇，之后**逐 0.15 秒记录蘑菇位置**，
    /// 看它是停在地面顶面还是钻进地板里（用户 2026-09-20：「蘑菇从砖上掉到地板里面了，吃不到」）。
    /// </summary>
    public static void MushroomDrop() => Start("mushroomdrop", MushroomDropBody);

    private static async Task MushroomDropBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        var lv = StageContext.Level;
        L($"PROBE mushroomdrop 关卡={StageContext.LevelPath} 地面顶={lv.GroundTopY:F1}");

        // 1-1 的三个蘑菇块：x=7.5 / 64.5 / 95.5（数据 `E <x> 0.5|4.5 MysteryBoxMushroom`）。
        // 95.5 那个在 y=4.5（上层砖排），从地面跳不到，这里测前两个。
        foreach (var boxX in new[] { 7.5f, 64.5f })
        {
            L($"PROBE ======== 试蘑菇块 x={boxX} ========");
            await ClearGoombasNear(boxX, 7f);
            Teleport(boxX, lv.GroundTopY);
            await Wait(0.5f);
            DumpBlocks($"顶前 x={boxX}");
            await Jump(0.5f);
            await Wait(0.5f);

            for (var i = 0; i < 14; i++)
            {
                await Wait(0.15f);
                var found = false;
                foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                {
                    if (mb == null) continue;
                    if (mb.GetType().Name != "MovingItem") continue;
                    found = true;
                    var pos = mb.transform.position;
                    var cx = Mathf.FloorToInt(pos.x);
                    var cy = Mathf.FloorToInt(pos.y);
                    L($"PROBE 蘑菇(x={boxX}) t={i * 0.15f:F1} x={pos.x:F2} y={pos.y:F2} " +
                      $"格({cx},{cy}) 该格实心={lv.IsSolidTile(cx, cy)} " +
                      $"下一格({cx},{cy - 1}) 实心={lv.IsSolidTile(cx, cy - 1)}");
                    break;
                }
                if (!found) L($"PROBE 蘑菇(x={boxX}) t={i * 0.15f:F1} 场上找不到 MovingItem");
            }
            Shot($"mushroomdrop-{boxX}");
        }
        State("蘑菇落定");
        await Wait(0.5f);
    }

    /// <summary>
    /// 通关收尾三件事的取证（用户 2026-09-20 报点）：
    /// ① 降旗到底时**底边**是否停在杆底（不该沉到基座砖下面）；
    /// ② 结算换分时 HUD 的「时间在减 / 分在涨」是否**同步**（直接读 UI 文字，不看推演）；
    /// ③ 旗杆那一格有没有基座砖（1-1 有、1-2 地表段原先漏了）。
    /// </summary>
    public static void FlagCheck() => Start("flagcheck", FlagCheckBody);

    /// <summary>按名字读一个 UI 文字（HUD 的 Score / Time 等）。</summary>
    private static string HudText(string name)
    {
        foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Include))
            if (t != null && t.gameObject.name == name) return t.text;
        return "?";
    }

    private static async Task FlagCheckBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        var lv = StageContext.Level;
        L($"PROBE 关卡 {StageContext.LevelPath}：旗杆 x={lv.FlagpoleX:F2} 触碰线={lv.FlagpoleTouchX:F2} " +
          $"地面顶={lv.GroundTopY:F1} 有旗杆={lv.HasFlagpole}");

        // ③ 基座砖：旗杆整数格 + 地面顶那一格必须是实心
        var bx = Mathf.FloorToInt(lv.FlagpoleX);
        var by = Mathf.RoundToInt(lv.GroundTopY);
        L($"PROBE 基座查：格({bx},{by}) 实心={lv.IsSolidTile(bx, by)}（1-1 应为 True）");

        // 直接送到旗杆触碰线上 ⇒ 下一帧 TickFlagpole 就判定通关并起滑
        Teleport(lv.FlagpoleX + 0.6f, lv.GroundTopY);
        await Wait(0.25f);
        State("抓到旗杆");

        // ① 下滑期间看旗子底边（应当收敛到杆底 = 地面顶，不低于它）
        for (var i = 0; i < 10; i++)
        {
            var f = StageContext.Flag;
            var p = StageContext.Player;
            if (f != null)
            {
                var sr = f.GetComponent<SpriteRenderer>();
                var half = sr != null && sr.sprite != null ? sr.sprite.bounds.extents.y : 0.5f;
                L($"PROBE 降旗 t={i * 0.2f:F1} 旗中心y={f.position.y:F3} 旗底边y={f.position.y - half:F3} " +
                  $"(杆底={lv.GroundTopY:F1}) 马里奥脚底={p.FeetPosition.y:F3}");
            }
            if (p.FeetPosition.y <= lv.GroundTopY + 0.01f) break;
            await Wait(0.2f);
        }

        // ② 从"抓杆"到"换分结束"整段采样：每 0.5 秒读一次 HUD 的分数与时间文字 + 马里奥走位
        L($"PROBE 走位参照：城堡门 x={lv.CastleDoorX:F2}（走城堡速度 2.5 格/秒）");
        for (var i = 0; i < 40; i++)
        {
            var p2 = StageContext.Player;
            L($"PROBE 结算 t={i * 0.5f:F1} HUD分={HudText("Score")} HUD时={HudText("Time")} " +
              $"(真值 Points={StageContext.Score.Points} TimeLeft={StageContext.Score.TimeLeft}) " +
              $"马里奥x={(p2 == null ? -999f : p2.FeetPosition.x):F2} busy={(p2 != null && p2.Busy)}");
            if (StageContext.Score.TimeLeft <= 0 && i > 2) break;
            await Wait(0.5f);
        }
        Shot("flagcheck");
        State("结算后");
        await Wait(1.0f);
    }

    /// <summary>
    /// 落在**并排两只**栗宝宝的接缝上：取证"踩一只不该被另一只撞死"
    /// （用户 2026-09-19 的报点：「1-2 两个板栗并排走，我踩了第二个，还是会碰第一个死」）。
    /// 判据：落点后马里奥仍 alive 且形态没变，且两只都死（原版是"一脚踩死并排两只"）。
    /// </summary>
    public static void PairStomp() => Start("pairstomp", PairStompBody);

    private static async Task PairStompBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }

        // 随便取两只栗宝宝，自己把它们摆成"并排"——不依赖关卡里是否正好有挨着的两只，
        // 几何上与用户遇到的情形（成对走）完全一致。
        var list = new List<MonoBehaviour>();
        foreach (var mb in AllEnemies())
        {
            if (!mb.gameObject.name.StartsWith("Goomba", StringComparison.Ordinal)) continue;
            if (mb is SuperMario.Module.Entities.IEnemy e && e.Dead) continue;
            list.Add(mb);
            if (list.Count == 2) break;
        }
        if (list.Count < 2) { L($"PROBE 栗宝宝不足两只（{list.Count}）"); return; }
        var a = list[0];
        var b = list[1];

        // ⚠️ 摆敌人与落马里奥之间**不留站桩时间**：出生点旁边就有一对迎面走来的栗宝宝，
        //    上一版在这里等 0.4 秒，马里奥在 `EnterGame` 后 0.5 秒就被撞死了（`马里奥死亡` 早于摆放）。
        const float groundY = -3f;
        const float bx = 12f;
        a.transform.position = new Vector3(bx, groundY, 0f);
        b.transform.position = new Vector3(bx + 1f, groundY, 0f);
        var pre = StageContext.Player;
        L($"PROBE 并排两只就位：A x={P(a).x:F2}、B x={P(b).x:F2}；落前 alive={pre.Alive} 形态={pre.Power}");

        Teleport(bx + 0.5f, groundY + 2f);     // 落在**接缝正上方**
        await Wait(0.7f);
        Shot("pairstomp");

        var p = StageContext.Player;
        var aDead = a is SuperMario.Module.Entities.IEnemy ea && ea.Dead;
        var bDead = b is SuperMario.Module.Entities.IEnemy eb && eb.Dead;
        L($"PROBE 结果：alive={p.Alive} 形态={p.Power} A死={aDead} B死={bDead}");
        State("接缝落点");
        await Wait(1.2f);
    }

    /// <summary>
    /// 取证"顶砖时砖上面那些东西怎么办"（用户 2026-09-19 的回忆点，出处 clone `RegularBrickBlock.cs:22-40`）：
    ///   ① 砖上有敌人 ⇒ 顶死（翻飞 +100）
    ///   ② 砖上有金币 ⇒ 收走（+1 枚 +200，砖上方 2 格弹出新金币，旧金币消失）
    /// 做法：挑一块**普通砖**（金币那条只对普通砖生效）→ 摆上栗宝宝与金币 → 人到砖下跳一下。
    /// </summary>
    public static void BlockTop() => Start("blocktop", BlockTopBody);

    /// <summary>反射拿活会话的道具模块（`Bootstrap._flow` → `AppFlow._session` → `StageSession._items`）。</summary>
    private static SuperMario.Module.Entities.IItems GetItemsModule()
    {
        var boots = UnityEngine.Object.FindObjectsByType<SuperMario.App.Bootstrap>(FindObjectsInactive.Include);
        if (boots == null || boots.Length == 0) return null;
        var flow = typeof(SuperMario.App.Bootstrap)
            .GetField("_flow", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(boots[0]);
        var sess = flow?.GetType()
            .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(flow);
        return sess?.GetType()
            .GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(sess)
            as SuperMario.Module.Entities.IItems;
    }

    private static async Task BlockTopBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }

        // ── 挑砖：普通砖（EntityKind.Brick），且左右至少有一格也是方块（栗宝宝要能站在上面走一下）──
        var all = new List<MonoBehaviour>();
        var names = new HashSet<string>();
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb == null) continue;
            if (!mb.gameObject.name.StartsWith("Block_", StringComparison.Ordinal)) continue;
            all.Add(mb);
            names.Add(mb.gameObject.name);
        }
        // 名字格式实测是 `Block_{Kind}_{x}_{y}`（例：`Block_Brick_115_0`）⇒ 取**最后两段**当格坐标，
        // 邻格判定用 "x_y" 字符串集合（与前缀无关，robust）。
        var cells = new HashSet<string>();
        foreach (var mb in all)
        {
            var ps = mb.gameObject.name.Split('_');
            if (ps.Length >= 2) cells.Add(ps[ps.Length - 2] + "_" + ps[ps.Length - 1]);
        }

        MonoBehaviour brick = null;
        var cell = new Vector2Int(0, 0);
        foreach (var mb in all)
        {
            var kf = mb.GetType().GetField("_kind", BindingFlags.NonPublic | BindingFlags.Instance);
            if (kf == null || kf.GetValue(mb)?.ToString() != "Brick") continue;
            var parts = mb.gameObject.name.Split('_');
            if (parts.Length < 2 || !int.TryParse(parts[parts.Length - 2], out var x)
                                || !int.TryParse(parts[parts.Length - 1], out var y)) continue;
            if (y != 0) continue;   // 只挑 y=0 那一排：从地面(y=-3)跳得够，y=4 那排够不到
            if (!cells.Contains((x - 1) + "_" + y) && !cells.Contains((x + 1) + "_" + y)) continue;
            cell = new Vector2Int(x, y);
            brick = mb;
            break;
        }
        if (brick == null) { L($"PROBE 找不到「y=0 成排的普通砖」（方块共 {all.Count} 个）"); return; }
        L($"PROBE 选中普通砖 {brick.gameObject.name}（格 {cell.x},{cell.y}）");

        // ── 站位：先清掉附近的栗宝宝（它俩会来撞人），再站到砖正下方 ──
        var standX = cell.x + 0.5f;
        KillGoombasNear(standX, 7f);
        Teleport(SafeGroundX(standX, -3f), -3f);
        await Wait(0.6f);
        if (StageContext.Player.Power != PowerState.Big)
        {
            StageContext.Player.PowerUp(PowerState.Big);   // 顶砖要够高（与 `blockbumps` 同一理由）
            await Wait(1.0f);
        }

        // ── 站回砖正下方（清场 + 顶砖前把位置坐稳），再**立刻**摆东西 + 跳 ──
        Teleport(SafeGroundX(standX, -3f), -3f);
        await Wait(0.3f);

        var g = FindEnemy("Goomba", 60f);
        if (g != null) g.transform.position = new Vector3(standX + 0.3f, cell.y + 1f, 0f);
        var items = GetItemsModule();
        // ⚠️ 实测：`SpawnCoin(center)` 的盒子是**底边 = 传入的 y**（日志盒子 (115.15,1.35)-(115.85,2.05)）
        //    ⇒ 要"坐在砖顶面"就得传 `cell.y + 1`（我第一版传 +1.35，盒子底高了 0.35 ⇒ 判据差 0.35 > 0.25 容差，
        //    所以金币那条没触发 —— 是探针摆错，不是规则没接）。
        if (items != null) items.SpawnCoin(new Vector2(standX, cell.y + 1f));
        else L("PROBE 反射拿不到 ItemModule ⇒ 金币那条测不了");
        await Wait(0.05f);

        // 诊断：把砖顶附近的东西报出来（栗宝宝会往左走，时序不对就会走离这一格）
        foreach (var e in AllEnemies())
            if (Mathf.Abs(P(e).x - standX) < 2.5f)
                L($"PROBE 诊断·敌人 {e.gameObject.name} pos=({P(e).x:F2},{P(e).y:F2}) 死={(e is SuperMario.Module.Entities.IEnemy ie && ie.Dead)}");
        if (items != null)
            foreach (var it in items.Active)
                if (Mathf.Abs(it.Bounds.center.x - standX) < 2.5f)
                    L($"PROBE 诊断·道具 {it.Content} box=({it.Bounds.xMin:F2},{it.Bounds.yMin:F2})-({it.Bounds.xMax:F2},{it.Bounds.yMax:F2}) taken={it.Taken}");

        var s0 = StageContext.Score;
        var p0 = s0.Points;      // ⚠️ 存**数值**：IScore 是引用，存对象的话"顶后"读到的还是同一个对象（差恒为 0）
        var c0 = s0.Coins;
        L($"PROBE 砖顶就位：栗宝宝={(g != null)} 金币模块={(items != null)}；顶前 分={p0} 币={c0}");
        State("顶砖前");

        // ── 跳起来顶这块砖 ──
        await Jump(0.34f);
        await Wait(0.6f);
        Shot("blocktop");

        var s1 = StageContext.Score;
        var gDead = g is SuperMario.Module.Entities.IEnemy ge && ge.Dead;
        L($"PROBE 顶后：分={s1.Points}(+{s1.Points - p0}) 币={s1.Coins}(+{s1.Coins - c0}) 栗宝宝死={gDead}");
        State("顶砖后");
        await Wait(1.0f);
    }

    public static void StompText() => Start("stomptext", StompTextBody);

    private static async Task StompTextBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        var g = FindEnemy("Goomba", 40f);
        if (g == null) { L("PROBE 找不到栗宝宝"); return; }
        L($"PROBE 踩敌前：goomba x={P(g).x:F2} y={P(g).y:F2}");
        Teleport(P(g).x, P(g).y + 2.0f);
        await Wait(0.34f);                 // 落体 ~0.22 s 踩中 ⇒ 此刻那行字约 0.12 s 大
        Shot("stomp-popup");
        State("踩中瞬间");
        await Wait(0.4f);
        Shot("stomp-popup-2");
        await Wait(1.0f);
    }

    // ───────────────────── 场景：旗杆 → 城堡 → 结算 ─────────────────────

    public static void Flag() => Start("flag", FlagBody);

    private static async Task FlagBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        Teleport(181.5f, -3f);
        await Wait(0.8f);
        await ClearGoombasNear(182f, 6f);
        DumpProps("1-1 终点");
        L("PROBE 旗杆前就位，按住右");
        _stub.Hold(GameKey.RightArrow, true);
        await Wait(1.2f);
        _stub.Hold(GameKey.RightArrow, false);
        await Wait(0.6f);
        State("下滑中");
        Shot("flag_top");
        for (var i = 0; i < 80; i++)
        {
            await Wait(0.2f);
            var p = StageContext.Player;
            if (i % 10 == 0) L($"PROBE 下滑 x={(p == null ? 0f : p.FeetPosition.x):F2} y={(p == null ? 0f : p.FeetPosition.y):F2} fsm={FsmNow}");
            if (p != null && p.Grounded) break;
        }
        await Wait(1.2f);
        State("下滑结束");
        Shot("flag_bottom");
        // 走进城堡：x 单调 ↑（约 184→198）的窗口里截 CASTLE_WALK
        var shotCastle = false;
        for (var i = 0; i < 120; i++)
        {
            await Wait(0.25f);
            var p = StageContext.Player;
            var px = p == null ? 0f : p.FeetPosition.x;
            if (i % 4 == 0) L($"PROBE 城堡 x={px:F2} fsm={FsmNow}");
            if (!shotCastle && px > 192f && FsmNow == "Stage")
            {
                shotCastle = true;
                State("走进城堡");
                Shot("CASTLE_WALK");
            }
            if (FsmNow == "Result" || FsmNow == "Loading") break;
        }
        if (!shotCastle) L("PROBE 警告：没抓到城堡行走窗口（CASTLE_WALK 未重拍）");
        // 结算屏**只有最后一关**才出：1-1 之后还有 1-2 ⇒ 不应出现 Result（实测：关卡通过 →2.5 秒→ 直接进 1-2）
        await Wait(1.0f);
        if (FsmNow == "Result")
        {
            L($"PROBE 结算屏出现 fsm={FsmNow} 分={StageContext.Score?.Points}");
            Shot("result");
        }
        else
        {
            L($"PROBE 本关不是最后一关 ⇒ 无结算屏（fsm={FsmNow}）—— 结算只在 WORLD 1-2 通关后出现（见 12-6）");
        }
    }

    // ═══════════ 场景：两个真 bug 一链两用（E-27 回调竞态 + 形态跨段保留）═══════════
    //
    // 为什么合成一条链：进 Play 要记账（skill §2 第 2 条 / 任务书-两个真bug 的 Play 预算 = 2），
    // 两件事都只需要"一条真实的用户路径"，所以合在一次会话的一条链里跑。
    //
    // ① **E-27 那条路径**（标题屏 Logo 的异步加载回调晚到）：
    //    面板一打开就切走（面板被 `Game.UI.CloseAll()` 销毁、场景切到 Stage01），而
    //    `Game.Res.LoadAsset<Sprite>` 的回调是**异步**的 —— 晚到时回调里捕获的 `logo` 已经是
    //    "已销毁的 Image" ⇒ 修前抛 MissingReference，被引擎包成一条
    //    `[Error] [Resource] 加载回调异常（Sprites/Title/TitleLogo）`，对照实机原文见
    //    `client/Logs/2026-09-19.log` 17:39:11.665（那次是 driver 把探针派在 Boot 阶段、
    //    探针 9 毫秒后就发了 CharChosen ⇒ 回调必然还在途）。
    //    ⚠️ 复现前提 = 那次加载**未命中缓存**：命中缓存时引擎是**同步**回调
    //    （`ResourceManager.cs:175-184`），根本不会晚到。所以本场景先把缓存里那一条挤掉
    //    （`Release` 把引用计数降到 0，`UnloadAll` 只淘汰计数 ≤ 0 的条目），再走**真实入口**
    //    `BackToMain` 重新加载菜单场景 —— 面板重开、重新发一次异步加载。
    //    日志打印 `TryGet(TitleLogo)=null ⇒ 加载在途` 作为"前提真的成立"的凭据（不许假设）。
    // ② **形态跨段保留**：1-1 吃蘑菇变大 → 走到旗杆通关 → 进 1-2 读 `power=`（数值类判据）。
    //    修前 `EnterLoading` 会把 `NextLevel` 刚设好的形态擦掉 ⇒ 进 1-2 读到 `Small`。
    //    ⚠️ `UnloadAll` 会把启动期 Preload 的**关卡文本**一起淘汰 ⇒ 中间走一次 `Boot`
    //    （`EnterBoot` 会重新 Preload）再进关，否则 `StageSession.LoadLevelText` 会报"关卡文本未就绪"。
    public static void CrossSeg() => Start("crossseg", CrossSegBody);

    /// <summary>
    /// 读资源缓存里那条记录的引用计数（只用于**证据/诊断**：淘汰要 Release 到计数 ≤ 0 才会发生，
    /// 把计数打进日志才能算清"到底要 Release 几次"）。拿不到就返回 -1，不影响场景本体。
    /// </summary>
    private static int RefCountOf(string path)
    {
        try
        {
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var fld = Game.Res.GetType().GetField("_cache", bf);
            var dict = fld == null ? null : (System.Collections.IDictionary)fld.GetValue(Game.Res);
            if (dict == null || !dict.Contains(path)) return -1;
            var rf = dict[path].GetType().GetField("RefCount");
            return rf == null ? -1 : (int)rf.GetValue(dict[path]);
        }
        catch (Exception) { return -1; }
    }

    private static async Task CrossSegBody()
    {
        // ── ① E-27：面板刚打开就切走（走的是真实入口 CharChosen）──
        var logoPath = SuperMario.Core.ResPaths.Title(SuperMario.Core.SpriteNames.TitleLogo);
        var cached = Game.Res.TryGet<Sprite>(logoPath);
        L($"PROBE E27 前置：TryGet({logoPath})={(cached == null ? "null" : "已缓存")}" +
          "（已缓存 ⇒ 引擎同步回调 ⇒ 复现不出，先把它从缓存里挤出去）");
        // ⚠️ 淘汰必须**把引用计数降到 0**：`UnloadAll` 只淘汰计数 ≤ 0 的条目，而面板每开一次就
        //    `RefCount++`（面板自己不 Release）⇒ 要 Release 到真被淘汰为止。
        //    实测（2026-09-19 18:37 / 18:40）：只 Release 一次、甚至 8 次，都砍不到 0
        //    （批量场景里面板开过的次数就是引用计数）⇒ 这里循环到真被淘汰（并把计数打进日志，便于复算）。
        var refBefore = RefCountOf(logoPath);
        var evictedAt = 0;
        for (var pass = 1; pass <= 32; pass++)
        {
            Game.Res.Release(logoPath);
            Game.Res.UnloadAll();
            if (Game.Res.TryGet<Sprite>(logoPath) == null) { evictedAt = pass; break; }
        }
        L($"PROBE E27 前置：Release + UnloadAll 之后 TryGet(TitleLogo)=" +
          $"{(Game.Res.TryGet<Sprite>(logoPath) == null ? "null ⇒ 未缓存" : "仍非 null（淘汰失败）")}" +
          $"（淘汰前 RefCount={refBefore}，第 {evictedAt} 轮淘汰）");
        if (evictedAt == 0) L("PROBE 警告：TitleLogo 淘汰失败 ⇒ 下面的竞态复现不出（命中缓存时引擎同步回调）");
        // ⚠️ `UnloadAll` 顺带把启动期 Preload 的像素字体与 5 份关卡文本也淘汰了（它们计数为 0）。
        //    不补回来的话，下面那次 CharChosen 会打出两条**属于探针的** Error
        //    （`关卡文本未就绪` / `关卡数据为空`）⇒ 把要数的那个 E-27 窗口搅浑 —— 所以按
        //    `AppFlow.EnterBoot` 的同一份预热清单立刻补回来（清单与那里逐项一致）。
        var warm = new List<string>
        {
            SuperMario.Core.ResPaths.PixelFont,
            SuperMario.Core.ResPaths.Level11, SuperMario.Core.ResPaths.Level12,
            SuperMario.Core.ResPaths.Level11Underground, SuperMario.Core.ResPaths.Level12Underground,
            SuperMario.Core.ResPaths.Level12Surface,
        };
        var warmed = false;
        Game.Res.Preload(warm, () => warmed = true);
        for (var i = 0; i < 300 && !warmed; i++) await Wait(0.05f);
        L($"PROBE E27 前置：重新 Preload {warm.Count} 项（像素字体 + 关卡文本）完成={warmed}");
        await Wait(0.2f);

        Game.Event.Emit(SuperMario.Core.Events.BackToMain);   // 真实入口：重新加载菜单场景 + 重开面板
        var t0 = Time.realtimeSinceStartup;
        while (!Game.UI.IsOpen<SuperMario.UI.MainMenuPanel>() && Time.realtimeSinceStartup - t0 < 15f)
            await Wait(0.01f);
        var inFlight = Game.Res.TryGet<Sprite>(logoPath) == null;
        L($"PROBE E27 复现：面板已打开 fsm={FsmNow} open={Game.UI.IsOpen<SuperMario.UI.MainMenuPanel>()}；" +
          $"TryGet(TitleLogo)={(inFlight ? "null ⇒ 加载在途" : "已缓存 ⇒ 这次复现不出")}");
        _stub.Hold(GameKey.RightArrow, false);                   // 别把上一场残留的按键带进这一场
        Game.Event.Emit(SuperMario.Core.Events.CharChosen, 1);   // = 用户按空格：面板此刻被销毁
        L("PROBE E27 复现：已发 CharChosen(1)（面板此刻被 CloseAll 销毁，回调还没回来）");
        await Wait(3.0f);                                        // 修前那条 Error 在 0.42 秒后到达
        L($"PROBE E27 复现：3 秒窗口结束 fsm={FsmNow}（这一段窗口里的错误行由 scene_errors.py 数）");

        // ── ② 形态跨段：1-1 吃蘑菇变大 → 走到旗杆通关 → 进 1-2 ──
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        await Wait(0.5f);
        StageContext.Player.PowerUp(PowerState.Big);   // 与顶蘑菇同一条链（探针不自己改字段）
        await Wait(2.5f);                              // 长大动画（GameConst.GrowTime）放完
        State("跨段-1-1 变大后");

        Teleport(181.5f, -3f);                          // 1-1 旗杆前（与 flag 场景同一取位）
        await Wait(0.8f);
        await ClearGoombasNear(182f, 6f);
        L("PROBE 跨段：旗杆前就位，按住右");
        _stub.Hold(GameKey.RightArrow, true);
        await Wait(1.2f);
        _stub.Hold(GameKey.RightArrow, false);
        await Wait(0.6f);
        var t1 = Time.realtimeSinceStartup;
        while (FsmNow == "Stage" && Time.realtimeSinceStartup - t1 < 25f) await Wait(0.2f);
        L($"PROBE 跨段：1-1 通关离开 Stage fsm={FsmNow}");
        await WaitStage(30f);
        await Wait(0.6f);
        State("跨段-进 1-2");
        L($"PROBE 跨段进 1-2：power={(StageContext.Player == null ? "?" : StageContext.Player.Power.ToString())} " +
          $"box={PosStr(StageContext.Player)} level={StageContext.LevelPath}");

        // 表现类证据：画面里马里奥得**真的**是大只（大马里奥碰撞盒高 1.5，小马里奥 0.75）
        await ShotGated("carry12-big", "#E-13 跨段形态保留（1-1 → 1-2）",
            () => StageAlive() && (StageContext.LevelPath ?? "").Contains("World1-2")
                  && StageContext.Player != null && StageContext.Player.Power == PowerState.Big);
        await TailToMenu("crossseg");
    }

    // ═══════════ 场景：死亡重来 ⇒ 形态归零（原版 LevelManager.cs:316）═══════════
    //
    // 为什么必须有这一条（任务书-死亡链复验）：上一片把"清形态"从 `AppFlow.EnterLoading` 搬到了
    // 两个真正的"新一局"入口（`OnCharChosen` 新开一局 / `ReloadStageWithIntro` 死亡重来·换手）——
    // 搬家的目的是让**跨段带形态**生效（表体 #57 已实测通过），但同一处改动必须同时保证
    // **死亡重来回到小马里奥**（原版行为）。后者是本片唯一要补的断言。
    //
    // 原版出处（clone `原版资源/参考工程/SMB-clone` 逐行核对）：
    //   · `LevelManager.cs:316 MarioRespawn` 里那句 `marioSize = 0` —— 只有**死亡**才归零；
    //   · 换关走 `LevelManager.cs:403-408 LoadNewLevel` → `GameStateManager.cs:53-57 ConfigNewLevel`
    //     （只重置时间 / hurryUp / 出生点，一个字节都不动 `marioSize`）⇒ 换段保留、死亡归零，两件事分开。
    //
    // 判据（数值类：运行时读**活对象**的 `power` 与碰撞盒，⛔ 不截图，见 skill §4）＝ 三个读数：
    //   ① 死前 `power=Big` —— 前提成立，否则下面两条是**空断言**（这条也要打进日志）；
    //   ② 掉坑死一次 → 重来后 `power=Small`（★ 本片要补的那一处断言）；
    //   ③ 再变大 → 走真实入口新开一局（`BackToMain` → `CharChosen`）→ `power=Small`。
    //     ⚠️ ③ 必须**先变大**再新开一局：否则整条链上恒 Small，"新开一局也清形态"根本判不出来。
    public static void DeathPower() => Start("deathpower", DeathPowerBody);

    private static async Task DeathPowerBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        await Wait(0.6f);

        // ── ① 吃蘑菇变大（走与顶蘑菇同一条链；探针不自己改私有字段）──
        StageContext.Player.PowerUp(PowerState.Big);
        await Wait(2.5f);                                   // 长大动画 GameConst.GrowTime 放完
        State("死亡链-①变大后");
        var big = StageContext.Player == null ? (PowerState?)null : StageContext.Player.Power;
        L($"PROBE deathpower 前提：变大后 power={(big == null ? "null" : big.ToString())}" +
          "（必须是 Big，否则②③是空断言）");

        // ── ② 掉坑死一次（真实死亡链路：掉出关卡 → 死亡结算 → 入场卡 → 重来）──
        //     ⚠️ 与 `physics` 场景同一手法：先等它离开 Stage，再等它回到 Stage（见那里的注释）。
        var livesBefore = StageContext.Score == null ? -1 : StageContext.Score.Lives;
        Teleport(StageContext.Player == null ? 100f : StageContext.Player.FeetPosition.x, -25f);
        L($"PROBE deathpower 已把马里奥丢出关卡（死亡前 命={livesBefore}）");
        for (var i = 0; i < 100 && FsmNow == "Stage"; i++) await Wait(0.2f);      // 等它离开 Stage
        for (var i = 0; i < 200 && FsmNow != "Stage"; i++) await Wait(0.2f);      // 等重来完 / 回到关卡
        await WaitStage(25f);
        await Wait(0.6f);
        State("死亡链-②重来后");
        var afterDeath = StageContext.Player == null ? (PowerState?)null : StageContext.Player.Power;
        L($"PROBE deathpower 判据②：掉坑死一次重来后 power={(afterDeath == null ? "null（玩家不存在）" : afterDeath.ToString())}" +
          $"（原版 LevelManager.cs:316 那句 marioSize = 0 ⇒ 必须 Small）；命={StageContext.Score?.Lives} " +
          $"box={PosStr(StageContext.Player)} ⇒ {(afterDeath == PowerState.Small ? "PASS" : "FAIL")}");

        // ── ③ 新开一局也是 Small（同一个 Play 会话里再读一行，⛔ 不为它再进一次 Play）──
        if (StageContext.Player == null || !StageContext.Player.Alive)
        {
            L("PROBE deathpower 判据③跳过：重来后玩家不可用（② 已 FAIL，先修那一条）");
        }
        else
        {
            StageContext.Player.PowerUp(PowerState.Big);
            await Wait(2.5f);
            var preNew = StageContext.Player.Power;
            L($"PROBE deathpower 判据③前提：新开一局前 power={preNew}（必须是 Big）");
            Game.Event.Emit(SuperMario.Core.Events.BackToMain);            // 真实入口，不是直接切 FSM
            for (var i = 0; i < 80 && FsmNow != "Menu"; i++) await Wait(0.25f);
            Game.Event.Emit(SuperMario.Core.Events.CharChosen, 1);
            L($"PROBE deathpower 已发 CharChosen(1)（新开一局；发之前 fsm={FsmNow}）");
            await WaitStage(25f);
            await Wait(0.6f);
            State("死亡链-③新开一局");
            var fresh = StageContext.Player == null ? (PowerState?)null : StageContext.Player.Power;
            L($"PROBE deathpower 判据③：新开一局（1-1）power={(fresh == null ? "null（玩家不存在）" : fresh.ToString())}" +
              $"（前提 power={preNew} ⇒ 归零可见）⇒ " +
              $"{(fresh == PowerState.Small && preNew == PowerState.Big ? "PASS" : "FAIL")}");
        }
        await TailToMenu("deathpower");
    }

    // ───────────────────── 场景：GameOver ─────────────────────

    public static void GameOver() => Start("gameover", GameOverBody);

    private static async Task GameOverBody()
    {
        await EnterGame();
        // ⚠️ 必须**一次死亡走完再发下一次**：`Emit(TimeUp)` 是异步的（置 PendingDeath），
        //    连发 5 次只有第一次会结算 —— 死亡期间 FSM 仍是 Stage，紧接着的第二次 emit 被吞掉。
        //    实测：连发 5 次只死 1 次（`还剩余 2 条命，重开本关`），16 秒后 fsm 还是 Stage，
        //    拍出来的 p1_mid 是**关卡画面**而不是 GameOver 屏。
        //    正确节奏：等 FSM **离开** Stage（进 Loading）→ 再等它回到 Stage（或进 GameOver）。
        for (var i = 0; i < 5; i++)
        {
            if (FsmNow == "GameOver") break;
            L($"PROBE gameover 第 {i + 1} 次时间到（命={StageContext.Score?.Lives} 分={StageContext.Score?.Points}）");
            Game.Event.Emit(SuperMario.Core.Events.TimeUp);
            for (var k = 0; k < 50 && FsmNow == "Stage"; k++) await Wait(0.2f);            // 等它离开 Stage
            for (var k = 0; k < 150 && FsmNow != "Stage" && FsmNow != "GameOver"; k++) await Wait(0.2f);  // 等复活 / GameOver
            if (FsmNow == "Stage") await WaitStage(25f);
            L($"PROBE gameover 第 {i + 1} 次之后 fsm={FsmNow} 命={StageContext.Score?.Lives}");
        }
        for (var k = 0; k < 60 && FsmNow != "GameOver"; k++) await Wait(0.25f);
        await Wait(0.8f);
        L($"PROBE gameover fsm={FsmNow}");
        Shot("p1_mid");
    }

    // ───────────────────── 场景：暂停菜单 ─────────────────────

    public static void Pause() => Start("pause", PauseBody);

    private static async Task PauseBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        _stub.Hold(GameKey.Escape, true);
        await Wait(0.15f);
        _stub.Hold(GameKey.Escape, false);
        for (var i = 0; i < 50 && FsmNow != "Pause"; i++) await Wait(0.15f);
        await Wait(0.3f);
        L($"PROBE pause 进状态后 0.3 秒：fsm={FsmNow} timeScale={Time.timeScale}");
        await Wait(1.3f);
        L($"PROBE pause 进状态后 1.6 秒：fsm={FsmNow} timeScale={Time.timeScale}");
        // 面板到底在不在（决定"截图里没有面板"是画不出来还是注册表里就没有）——别靠猜。
        var pp = Game.UI.Get<SuperMario.UI.PausePanel>();
        L($"PROBE pause 面板：IsOpen={Game.UI.IsOpen<SuperMario.UI.PausePanel>()} " +
          $"Get={(pp == null ? "null" : "有")} 场景内实例数={CountByName("PausePanel")}");
        foreach (var cv in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            DumpNode(cv.transform, "  ", 0);
        // 暂停面板也是"进状态后才画"：ShotOk 按唯一色数重试，避免拍到还没画面板的那一帧
        await ShotOk("pause");
        // ── E-19：音量条必须"真的会动"（登记项：Fill 是裸 Image ⇒ 恒满格）──
        // 判据 = 「Slider.value / 填充条实际渲染宽度 / 音源音量」三者互相对得上，
        // 且两条滑块在不同 value 下宽度**肉眼可见不同**（同一张图里就能比）。
        await Wait(0.3f);
        VolReport("默认(1.0)");
        await ShotOk("pause-vol-a");
        SetVol("Vol_BGM", 0.3f);
        await Wait(0.4f);
        VolReport("BGM=0.3");
        await ShotOk("pause-vol-b");
        SetVol("Vol_BGM", 1f);
        await Wait(0.3f);
        VolReport("还原(1.0)");
        // 再确认一次：抓拍之后面板仍然在（判"面板每帧被建又销毁"这种抖动）
        await Wait(0.5f);
        L($"PROBE pause 抓拍后 0.5 秒：fsm={FsmNow} IsOpen={Game.UI.IsOpen<SuperMario.UI.PausePanel>()} " +
          $"场景内实例数={CountByName("PausePanel")}");
        _stub.Hold(GameKey.Escape, true);
        await Wait(0.15f);
        _stub.Hold(GameKey.Escape, false);
        for (var i = 0; i < 50 && FsmNow != "Stage"; i++) await Wait(0.15f);
        L($"PROBE pause 退出 fsm={FsmNow} timeScale={Time.timeScale}");
    }

    // ───────────────────── 场景：1-2 入场（标题屏 + 地下关）─────────────────────

    public static void Level12() => Start("level12", Level12Body);

    private static async Task Level12Body()
    {
        await MenuReady();
        State("标题屏（1-2 会话）");
        // 闸门同 menu 场景（这一张也是**标题屏**，状态不在 Menu 就会拍到入场卡）
        await ShotGated("s12_stage", "#12-4 标题屏（1-2 会话开始时拍的）", MenuGate);
        Game.Event.Emit(SuperMario.Core.Events.CharChosen, 1);
        await WaitStage(25f);
        await Wait(0.8f);
        // 1-1 主关的 BGM（应为主世界主题）
        L($"PROBE bgm[1-1]：{BgmState()}");
        CallFlow("NextLevel");
        for (var i = 0; i < 90 && FsmNow != "Stage"; i++) await Wait(0.2f);
        await WaitStage(25f);
        State("1-2 入场");
        // 1-2 是地下关：BGM 必须是地下主题（clone `World 1-2.unity` 引用的就是 02-underworld.mp3）
        L($"PROBE bgm[1-2 地下段]：{BgmState()}");
        // ⚠️ 这一张要**抢**：1-2 出生点 (0.5,0) 的栗宝宝约 2~3 秒就能撞死他（既存缺陷，已登记：本轮只登记不修）。
        //    做法 = ① 不再额外等空闲时间；② 每 50 毫秒查一次"fsm=Stage + 是 1-2 地下 + 相机已进关卡（x≥12）"，
        //    一满足就先把 timeScale 冻住（不给敌人时间）、出图、再恢复。旧口径"等到 fsm=Stage 再等 0.8 秒"
        //    实测会正好落在死亡重开之后（2026-09-19 17:01：闸门读到 fsm=Loading）。
        var earlyDone = false;
        for (var attempt = 1; attempt <= 3 && !earlyDone; attempt++)
        {
            for (var i = 0; i < 300 && !earlyDone; i++)
            {
                var cam = Camera.main;
                var ready = StageAlive() && (StageContext.LevelPath ?? "").Contains("World1-2")
                            && StageContext.Underground && cam != null && cam.transform.position.x >= 12f;
                if (ready)
                {
                    Time.timeScale = 0f;
                    earlyDone = await ShotGated("s12_early", $"#12-2 1-2 地下段外观（第 {attempt} 次抢拍）",
                        () => StageAlive() && (StageContext.LevelPath ?? "").Contains("World1-2") && StageContext.Underground);
                    Time.timeScale = 1f;
                }
                await Wait(0.05f);
            }
            if (!earlyDone)
            {
                L($"PROBE #12-2 第 {attempt} 次没抢到（fsm={FsmNow} 玩家={PosStr(StageContext.Player)} " +
                  $"命={StageContext.Score?.Lives}）⇒ 等它重开完再试");
                await EnsureStage("#12-2 重试");
            }
        }
        if (!earlyDone) L("PROBE 警告：#12-2 三次都没抢到可拍窗口 —— s12_early 保持旧文件（未写盘）");
        // ★ 收尾必须回主菜单：这一场结束时人正站在 1-2 出生点，敌人几秒后就会把他撞死，
        //   而那条 `马里奥死亡` 会落到**下一个场景**的窗口里（实测 17:39:54.626，把 mouth2 整场搞乱）。
        await TailToMenu("level12");
    }

    // ───────────────────── 场景：#3 1-2 出生点"站着不动"能活多久 ─────────────────────

    public static void SpawnSafety() => Start("spawnsafety", SpawnSafetyBody);

    /// <summary>场上离马里奥最近的那只活敌人的 x（诊断"是谁在逼近"；没敌人写 <c>-</c>）。</summary>
    private static string NearestEnemyX()
    {
        var px = PlayerX();
        var best = float.MaxValue;
        var s = "-";
        foreach (var mb in AllEnemies())
        {
            if (mb == null) continue;
            if (!(mb is SuperMario.Module.Entities.IEnemy e) || e.Dead) continue;
            var d = Mathf.Abs(P(mb).x - px);
            if (d < best) { best = d; s = $"{mb.gameObject.name}@{P(mb).x:F2}(距 {d:F2})"; }
        }
        return s;
    }

    /// <summary>
    /// #3（任务书-收尾三项）：从 1-2 出生点**不作任何按键**能活多久 —— 数值类证据（运行时日志行）。
    /// <para>
    /// 记**两个参照点**（两者的差 = 入场卡期间世界仍在跑的那一段，见验收表 #12-2 的注）：
    /// <list type="bullet">
    /// <item><c>t=0</c> = 关卡数据建好、<c>StageContext.LevelPath</c> 变成 1-2 的那一帧
    ///       （与游戏日志的 <c>马里奥已就位</c> 同一时刻 ±1 帧）—— 这也是**本关敌人开始走**的时刻，
    ///       与原版"人一出现、敌人就开始走"同口径 ⇒ 用它和原版的距离（13/14 格 ⇒ 4.9 秒）对比；</item>
    /// <item><c>→ Stage</c> = 玩家第一次能操作的那一帧（入场卡放完）⇒ 玩家**体感**的开局。</item>
    /// </list>
    /// 判据只读活对象（<c>IPlayer.Alive</c> / <c>IScore.Lives</c> / 敌人的 <c>Bounds</c>），不看常量。
    /// </para>
    /// </summary>
    private static async Task SpawnSafetyBody()
    {
        await MenuReady();
        Game.Event.Emit(SuperMario.Core.Events.CharChosen, 1);
        await WaitStage(25f);
        L("PROBE spawnsafety 1-1 就绪 ⇒ 真实流程入口 NextLevel 切到 1-2（之后一个键都不按）");
        CallFlow("NextLevel");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tBuild = -1.0;
        var tStage = -1.0;
        var tDead = -1.0;
        for (var i = 0; i < 700 && tDead < 0; i++)
        {
            var p = StageContext.Player;
            var lp = StageContext.LevelPath ?? "-";
            if (tBuild < 0 && lp.Contains("World1-2"))
            {
                tBuild = sw.Elapsed.TotalSeconds;
                L($"PROBE spawnsafety t=0.00 出生点就位：关卡={lp} 玩家={PosStr(p)}");
            }
            if (tBuild >= 0 && tStage < 0 && IsStage)
            {
                tStage = sw.Elapsed.TotalSeconds;
                L($"PROBE spawnsafety t={tStage - tBuild:F2} → Stage（玩家第一次能操作）玩家={PosStr(p)} " +
                  $"附近敌人={EnemyDumpNear(PlayerX(), 20f)}");
            }
            if (tBuild >= 0 && p != null && !p.Alive)
            {
                tDead = sw.Elapsed.TotalSeconds;
                L($"PROBE spawnsafety t={tDead - tBuild:F2} 马里奥死亡（从 →Stage 起 {tDead - tStage:F2} 秒）" +
                  $" 命={StageContext.Score?.Lives} 死时附近敌人={EnemyDumpNear(PlayerX(), 20f)}");
                break;
            }
            if (tBuild >= 0 && i % 2 == 0)
                L($"PROBE spawnsafety t={sw.Elapsed.TotalSeconds - tBuild:F2}（→Stage 起 " +
                  $"{(tStage < 0 ? 0 : sw.Elapsed.TotalSeconds - tStage):F2}）站着不动：fsm={FsmNow} 关卡={lp} " +
                  $"玩家={PosStr(p)} alive={(p != null && p.Alive)} 命={StageContext.Score?.Lives} " +
                  $"最近敌人={NearestEnemyX()}");
            await Wait(0.2f);
        }
        var total = (tDead >= 0 ? tDead : sw.Elapsed.TotalSeconds) - tBuild;
        var fromStage = (tDead >= 0 ? tDead : sw.Elapsed.TotalSeconds) - (tStage < 0 ? tBuild : tStage);
        var card = (tStage < 0 ? 0 : tStage - tBuild);
        var okSpawn = tDead < 0 || total >= 4f;
        var okStage = tDead < 0 || fromStage >= 4f;
        L($"PROBE spawnsafety 判据：出生点(t=0) 起 {total:F2} 秒{(tDead >= 0 ? "死亡" : "仍存活")}" +
          $"（其中入场卡 {card:F2} 秒玩家不能动）= 出生点口径 ≥4 秒 ⇒ {(okSpawn ? "PASS" : "FAIL")}；" +
          $"玩家可控(→Stage) 起 {fromStage:F2} 秒{(tDead >= 0 ? "死亡" : "仍存活")} ⇒ " +
          $"玩家体感口径 ≥4 秒 ⇒ {(okStage ? "PASS" : "FAIL")}");
        // 收尾：**先等死亡结算走完**再回主菜单（`TailToMenu` 的注释里写了"不等会怎样"——
        // 实测就是它把 section12 的 sec12-b/c/m1 三张图变成空帧的），别把这个活局留给下一个场景。
        await TailToMenu("spawnsafety");
    }

    // ───────────────────── 场景：1-1 金币房（进管 / 房间 / 出管）─────────────────────

    public static void CoinRoom() => Start("coinroom", CoinRoomBody);

    private static async Task CoinRoomBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }
        L($"PROBE bgm[主关 1-1]：{BgmState()}");
        Teleport(44.5f, 1.0f);
        await Wait(0.8f);
        State("站在第 4 根水管顶");
        Shot("20-pipe-stand");
        _stub.Hold(GameKey.DownArrow, true);
        await Wait(0.35f);
        _stub.Hold(GameKey.DownArrow, false);
        for (var i = 0; i < 80; i++)
        {
            await Wait(0.2f);
            if (i % 5 == 0) L($"PROBE 进管 t={i * 0.2:F1} fsm={FsmNow} under={StageContext.Underground} sub={StageContext.SubArea} 马里奥={PosStr(StageContext.Player)}");
            if (StageContext.Underground) break;
        }
        await Wait(1.0f);
        State("密室内");
        // E-9 的判据：密室里的 BGM 必须是地下主题（clone 的 `World 1-1 - Underground.unity`
        // 引用的就是 `02-underworld.mp3`），而不是静音、也不是主世界主题。
        L($"PROBE bgm[密室内]：{BgmState()}");
        Shot("21-coinroom-enter");
        StageContext.Player.PowerUp(PowerState.Big);
        await Wait(2.5f);
        State("密室落地后（吃币）");
        Shot("22-coinroom-floor");
        // 金币平台顶面 y=0，从地面（y=-3）要跳 3 格才上得去；上平台后向右扫币。
        // ⚠️ 右移必须在 x > -5.5 处停住：再往右就踩进出口管（T x=-4..-1）会提前出管（实测踩过）
        for (var i = 0; i < 5; i++)
        {
            if (!StageContext.Underground) { L("PROBE 警告：已经不在密室里（提前出管），停止扫币"); break; }
            _stub.Hold(GameKey.RightArrow, true);
            await Jump(0.5f);
            await Wait(0.5f);
            L($"PROBE 上平台第 {i + 1} 次 马里奥={PosStr(StageContext.Player)} 币={StageContext.Score?.Coins} 分={StageContext.Score?.Points} under={StageContext.Underground}");
            var px2 = StageContext.Player == null ? 0f : StageContext.Player.FeetPosition.x;
            if (px2 > -5.5f) _stub.Hold(GameKey.RightArrow, false);
        }
        _stub.Hold(GameKey.RightArrow, false);
        await Wait(0.8f);
        L($"PROBE 扫币后 币={StageContext.Score?.Coins} 分={StageContext.Score?.Points} 时={StageContext.Score?.TimeLeft}");
        State("密室金币平台");
        Shot("23-coinroom-platform");
        // 出口管 T x=-4..-1, y=-3..-2：走到管口再按下 ↓
        for (var i = 0; i < 50; i++)
        {
            var p = StageContext.Player;
            if (p != null && p.FeetPosition.x > -3.6f) break;
            _stub.Hold(GameKey.RightArrow, true);
            await Wait(0.2f);
        }
        _stub.Hold(GameKey.RightArrow, false);
        await Wait(0.5f);
        L($"PROBE 出口管前 马里奥={PosStr(StageContext.Player)} 币={StageContext.Score?.Coins}");
        _stub.Hold(GameKey.DownArrow, true);
        await Wait(0.35f);
        _stub.Hold(GameKey.DownArrow, false);
        for (var i = 0; i < 80; i++)
        {
            await Wait(0.2f);
            if (i % 5 == 0) L($"PROBE 出管 t={i * 0.2:F1} under={StageContext.Underground} sub={StageContext.SubArea} 马里奥={PosStr(StageContext.Player)}");
            if (!StageContext.Underground) break;
        }
        await Wait(0.8f);
        State("回主关（出管后）");
        L($"PROBE bgm[回主关]：{BgmState()}");
        Shot("24-coinroom-exit");
        await Wait(1.4f);
        State("回主关（稳定）");
        Shot("25-back-in-main");
        L($"PROBE 出管完成 币={StageContext.Score?.Coins} 分={StageContext.Score?.Points} 时={StageContext.Score?.TimeLeft}");
    }

    // ───────────────────── 场景：1-2 秘密金币房（进管 / 房间 / 出管）─────────────────────

    public static void CoinRoom12() => Start("coinroom12", CoinRoom12Body);

    // ═══════════ 场景：大马里奥跨段保留形态（登记项 E-13）═══════════
    //
    // 判据（原版行为）：**同一只马里奥** —— 大马里奥进密室，密室里还是大、出来仍然是大。
    // 数值证据 = State() 行里的 `power=`（三处：主关 / 密室内 / 出管后）；
    // 表现证据 = 三张截图（大马里奥体型 = 碰撞盒 0.9x1.8 左右，小马里奥是 0.7x1.0）。
    // 进/出管走的是原版那套（站管顶按下 ↓ 进、密室走到出口管口），不是直接改状态。

    public static void BigRoom() => Start("bigroom", BigRoomBody);

    private static async Task BigRoomBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }

        // 先变成大马里奥（走 PlayerModule.PowerUp，与顶蘑菇同一条链 —— 探针里不自己改字段）。
        StageContext.Player.PowerUp(PowerState.Big);
        await Wait(2.5f);       // 长大动画（GameConst.GrowTime）放完
        State("主关-大马里奥（进密室前）");
        Shot("e13-a-big-main");

        // 站到第 4 根水管（T x=43..44）顶 → 按下 ↓ 进密室。
        Teleport(44.5f, 1.0f);
        await Wait(0.8f);
        _stub.Hold(GameKey.DownArrow, true);
        await Wait(0.35f);
        _stub.Hold(GameKey.DownArrow, false);
        for (var i = 0; i < 80; i++)
        {
            await Wait(0.2f);
            if (StageContext.Underground) break;     // 密室（underground=true）
        }
        await Wait(1.0f);
        State("密室内-形态应仍为 Big");
        L($"PROBE bigroom 密室内 power={(StageContext.Player == null ? "?" : StageContext.Player.Power.ToString())} " +
          $"box={PosStr(StageContext.Player)}");
        Shot("e13-b-big-in-room");

        // 走到出口管口（T x=-4..-1）再按下 ↓ 出管。
        for (var i = 0; i < 60; i++)
        {
            var p = StageContext.Player;
            if (p != null && p.FeetPosition.x > -3.6f) break;
            _stub.Hold(GameKey.RightArrow, true);
            await Wait(0.2f);
        }
        _stub.Hold(GameKey.RightArrow, false);
        await Wait(0.5f);
        _stub.Hold(GameKey.DownArrow, true);
        await Wait(0.35f);
        _stub.Hold(GameKey.DownArrow, false);
        for (var i = 0; i < 80; i++)
        {
            await Wait(0.2f);
            if (!StageContext.Underground) break;    // 回到主关
        }
        await Wait(1.2f);
        State("出管后-形态应仍为 Big");
        L($"PROBE bigroom 出管后 power={(StageContext.Player == null ? "?" : StageContext.Player.Power.ToString())} " +
          $"box={PosStr(StageContext.Player)}");
        Shot("e13-c-big-after");
    }

    /// <summary>
    /// 按住右，直到吃到的金币数达到 <paramref name="targetCoins"/>（或脚底 x 到 <paramref name="stopX"/>
    /// / 步数用尽）为止。
    /// <para>
    /// 用"币数到了就停"而不是"走到某个 x 就停"：走路有惯性（4.7 格/秒 + 减速度），
    /// 按 x 停会在松键后又滑出 0.4 格 —— 实测第一版就是这样滑到 xMax=4.89，
    /// 距出管判定线 4.95 只剩 0.06 格（差一点就提前出管）。
    /// 两个兜底都留着：币吃到就停、x 到线就停。
    /// </para>
    /// </summary>
    private static async Task<float> WalkRightUntilCoins(int targetCoins, float stopX, int maxSteps)
    {
        for (var i = 0; i < maxSteps; i++)
        {
            var p = StageContext.Player;
            var s = StageContext.Score;
            if (s != null && s.Coins >= targetCoins) break;
            if (p == null || p.FeetPosition.x >= stopX) break;
            _stub.Hold(GameKey.RightArrow, true);
            await Wait(0.2f);
        }
        _stub.Hold(GameKey.RightArrow, false);
        return StageContext.Player == null ? 0f : StageContext.Player.FeetPosition.x;
    }

    /// <summary>
    /// 1-2 的「进管 → 秘密金币房 → 出管回 1-2」，与 <see cref="CoinRoomBody"/>（1-1）走**同一条**链路。
    /// <para>
    /// 坐标全部有出处（这里一个数都不定）：
    /// · 进管管口 = `client/Assets/Resources/Levels/World1-2.txt` 的 `T 100 0..2` / `T 101 0..2`
    ///   （元素表 §4.1 表第 1 行：`Warp Green Pipe 2x3 Down.prefab` @ clone (100.5,0)，
    ///   出处 `World 1-2.unity:11125`）⇒ 管跨 x∈[100,102]、管口顶面 = 瓦片 y=2 的上沿 = **3**
    ///   ⇒ 站中间 x=101。
    /// · 房间格坐标 = 元素表 §4.2（与 `World1-2-Underground.txt` 同一套）：
    ///   地面顶面 y=0、中墙（y=3 一整排，顶面 y=4）上摆 8 枚币、地面摆 9 枚币（共 17 枚）、
    ///   出口侧向管管口 2 格在 x=5,6。
    /// </para>
    /// </summary>
    private static async Task CoinRoom12Body()
    {
        await Enter12();
        if (!IsStage) { L("PROBE 没进到 1-2，放弃"); return; }
        State("1-2 主关（进管前）");

        // ⚠️ 先清场：1-2 的起点附近有两只栗宝宝（`E 13 0` / `E 14 1`），它们**一路向左走**，
        //    4~5 秒就走到出生点 (0.5,0) 把马里奥撞死 —— 实测本场景因此整场报废
        //    （`马里奥死亡` → 重开 → 再死 → GameOver，26~30 五张图全废）。
        //    用游戏自己的 `IEnemy.Flip()` 清掉，不改代码、也不影响要验证的进管链路。
        var cleared = FlipEnemies("Goomba");
        L($"PROBE coinroom12 先清掉 {cleared} 只栗宝宝（免得它们走到出生点撞死马里奥）");
        await Wait(0.5f);

        // ⚠️ 进管那根管子上就有一朵食人花（x=100.5）。它伸出来的时候把马里奥直接传到管顶
        // 会被它顶死（实测：传送后 3 毫秒 `马里奥死亡`，整个场景报废）。
        // 所以先等它缩回管里再站上去 —— 玩家在原版里也是等它缩回去才下去。
        var phIn = FindEnemy("Piranha", 100f);
        if (phIn != null)
        {
            for (var i = 0; i < 120; i++)
            {
                var bb = phIn is SuperMario.Module.Entities.IEnemy pe0 ? pe0.Bounds : new Rect(0f, 0f, 0f, 0f);
                if (bb.height < 0.15f) { L($"PROBE coinroom12 进管口的花已缩回（等了 {i * 0.15f:F1}s）"); break; }
                await Wait(0.15f);
            }
        }
        else L("PROBE coinroom12 警告：找不到进管口的花（继续，可能被打掉了）");

        // 站在进管管口顶面（§4.1：管跨 [100,102]、顶面 3）。
        Teleport(101f, 3f);
        await Wait(0.8f);
        State("站在 1-2 进管管口");
        Shot("26-pipe12-stand");

        _stub.Hold(GameKey.DownArrow, true);
        await Wait(0.35f);
        _stub.Hold(GameKey.DownArrow, false);
        for (var i = 0; i < 100; i++)
        {
            await Wait(0.2f);
            if (i % 5 == 0) L($"PROBE 进管 t={i * 0.2:F1} fsm={FsmNow} sub={StageContext.SubArea} under={StageContext.Underground} 马里奥={PosStr(StageContext.Player)}");
            // ⚠️ 判据只能用 SubArea：1-2 主关**本来就是**地下关（under=True 恒成立），
            //    用 Underground 判"进没进密室"会一进来就为真（1-1 那边可以用，因为它地上）。
            if (StageContext.SubArea) break;
        }
        await Wait(1.0f);
        State("密室内（1-2 金币房）");
        Shot("27-coinroom12-enter");

        StageContext.Player.PowerUp(PowerState.Big);
        await Wait(1.5f);

        // ── 地面那一排币（§4.2：y=0、x=-5..3，共 9 枚）──
        // 吃到 9 枚就停；兜底 stopX=4.2 —— 出口管口左表面在 x=5，右边缘一顶到就自动出管
        //（1-1 踩过同类坑：扫币扫到管口，提前出管）。
        var x1 = await WalkRightUntilCoins(9, 4.2f, 60);
        await Wait(0.8f);
        L($"PROBE 扫地面币后 币={StageContext.Score?.Coins} 分={StageContext.Score?.Points} 时={StageContext.Score?.TimeLeft} 马里奥={PosStr(StageContext.Player)} 停在x={x1:F2}");
        State("密室地面金币区");
        Shot("28-coinroom12-floor");

        // ── 中墙顶那一排币（§4.2：y=4、x=-4..3，共 8 枚）──
        // 墙顶在 y=4，从地面跳要 4 格（小马里奥满跳峰值 4.68 格、贴天花只剩 3 格余量，
        // 靠跳不可靠），所以探针直接把马里奥放到墙顶左端再向右走 ——
        // 吃币走的仍是游戏自己的碰撞/拾取逻辑，这里只是省掉"跳上去"那一步。
        Teleport(-4.6f, 4f);
        await Wait(0.6f);
        // 吃到全房间 17 枚（§4.2）为止；兜底 stopX=4.2（墙顶到 x=4 就是多金币砖，本来就挡住）。
        var x2 = await WalkRightUntilCoins(17, 4.2f, 60);
        await Wait(0.8f);
        L($"PROBE 扫中墙顶币后 币={StageContext.Score?.Coins} 分={StageContext.Score?.Points} 时={StageContext.Score?.TimeLeft} 马里奥={PosStr(StageContext.Player)} 停在x={x2:F2}");

        // ── 从墙顶左侧走下来回地面（墙跨 x=-5..5，走出 -5 就掉回地面 y=0）──
        for (var i = 0; i < 60; i++)
        {
            var p = StageContext.Player;
            if (p == null || p.FeetPosition.y < 0.5f) break;
            _stub.Hold(GameKey.LeftArrow, true);
            await Wait(0.2f);
        }
        _stub.Hold(GameKey.LeftArrow, false);
        await Wait(0.6f);
        State("走回地面（准备出管）");

        // ── 向右走进出口管口：玩法层判"右边缘顶到 x=5"自动出管（原版就是走进去、不用按键）──
        for (var i = 0; i < 60; i++)
        {
            if (!StageContext.SubArea) break;
            _stub.Hold(GameKey.RightArrow, true);
            await Wait(0.2f);
        }
        _stub.Hold(GameKey.RightArrow, false);
        for (var i = 0; i < 100; i++)
        {
            await Wait(0.2f);
            if (i % 5 == 0) L($"PROBE 出管 t={i * 0.2:F1} sub={StageContext.SubArea} 马里奥={PosStr(StageContext.Player)}");
            if (!StageContext.SubArea) break;
        }
        await Wait(0.8f);
        State("回 1-2 主关（出管后）");
        Shot("29-coinroom12-exit");
        await Wait(1.6f);
        State("回 1-2 主关（稳定）");
        Shot("30-back-in-12");
        L($"PROBE 出管完成 币={StageContext.Score?.Coins} 分={StageContext.Score?.Points} 时={StageContext.Score?.TimeLeft}");
    }

    // ═══════════ 场景：两间密室的出口侧向管口（A2：与原版 misc-3.gif 并排）═══════════
    //
    // 为什么删掉了原来的 Flag12 场景：它按"1-2 主关有旗杆"写（Teleport 到 `lv.FlagpoleTouchX`）。
    // 而 1-2 拆成两段后**主关没有旗杆**（`World1-2.txt` 的 `# no-flagpole`），`GameplayModule`
    // 对 `!HasFlagpole` 直接 return ⇒ 那个场景会一直走到 140 步上限（35 秒）也不会通关，是个"必然
    // 白等"的陷阱。1-2 的通关/结算已由 `Section12` / `Walk12End` 覆盖（走地表段的旗杆）。
    //
    // 坐标出处（这里一个数都不定）：
    //   · 1-1 密室：元素表 §3.2（clone `Warp Green Pipe Side Long.prefab` world=(7.5,0.5)
    //     cells=x=5..8,y=0..10 ⇒ 本工程格 T x=-4..-1）⇒ 管口面 x=-4
    //     （`PipeWarpTable.BonusRoomExitFaceX`）；管口 2 格 = `World1-1-Underground.txt` 的
    //     `T -4 -3 _44` / `T -4 -2 _31`（中段 `T -3 -3 _45` / `T -3 -2 _32`）。
    //   · 1-2 密室：元素表 §4.2 的出口侧向管管口 2 格在 x=5,6 ⇒ 管口面 x=5
    //     （`PipeWarpTable.Level12`）；管口 2 格 = `World1-2-Underground.txt` 的
    //     `T 5 0 _44` / `T 5 1 _31`（中段 `T 6 0 _45` / `T 6 1 _32`）。
    //   · 判据：管口是"朝左的开口"（贴图 = 原版 `misc-3.gif` 的 `pipe_green_top_side`/`pipe_green_mid`），
    //     不是立管管身 `_18/_19`。

    public static void Mouth2() => Start("mouth2", Mouth2Body);

    private static async Task Mouth2Body()
    {
        // ── 1-1 金币房 ──
        await EnterGame();
        if (!IsStage) { L("PROBE mouth2 没进到 1-1，放弃"); return; }
        Teleport(44.5f, 1.0f);
        await Wait(0.8f);
        _stub.Hold(GameKey.DownArrow, true);
        await Wait(0.35f);
        _stub.Hold(GameKey.DownArrow, false);
        for (var i = 0; i < 80 && !StageContext.Underground; i++) await Wait(0.2f);
        await Wait(1.0f);
        L($"PROBE mouth2 1-1 密室就位 under={StageContext.Underground} 关卡={StageContext.LevelPath} " +
          $"玩家={PosStr(StageContext.Player)}");
        // 站在出口管口正前方：管口面 x=-4、地面顶面 y=-3。
        // ⚠️ 站位只能取 x∈[-5.625,-4.375]：左边 x<-5.625 时碰撞盒会压到平台砖 (-7,-3)（x∈[-7,-6)）
        //    被碰撞解算**弹到 -13.38**（实测：第一版取 -6.4，截出来的图是房间左半边，完全没照到管口）；
        //    右边 x>-4.375 时右边缘一顶到 x=-4 就自动出管。**不作任何按键**（走路会滑出去）。
        Teleport(-5.0f, -3f);
        await Wait(0.8f);
        State("A2 1-1 密室出口管口");
        var q11 = StageContext.Player;
        if (FsmNow == "Stage" && StageContext.Underground && q11 != null && q11.Alive)
            Shot("sec12-k-mouth11");
        else
            L($"PROBE 拒绝出图 sec12-k-mouth11：fsm={FsmNow} under={StageContext.Underground} " +
              $"player={(q11 == null ? "null" : (q11.Alive ? "活着" : "死了"))}");

        // 退出 1-1 密室：右边缘顶到管口面 x=-4 就自动出管（玩法层判定，不用按键）
        for (var i = 0; i < 60; i++)
        {
            var p = StageContext.Player;
            if (p == null || p.Bounds.xMax >= -4f) break;
            _stub.Hold(GameKey.RightArrow, true);
            await Wait(0.2f);
        }
        _stub.Hold(GameKey.RightArrow, false);
        for (var i = 0; i < 80 && StageContext.Underground; i++) await Wait(0.2f);
        await Wait(1.0f);
        L($"PROBE mouth2 出 1-1 密室 关卡={StageContext.LevelPath} under={StageContext.Underground} " +
          $"玩家={PosStr(StageContext.Player)}");

        // ── 切到 1-2 主关（走真实流程入口 NextLevel）──
        CallFlow("NextLevel");
        for (var i = 0; i < 120 && FsmNow != "Stage"; i++) await Wait(0.25f);
        await WaitStage(25f);
        await Wait(0.8f);
        L($"PROBE mouth2 到 1-2 主关 fsm={FsmNow} 关卡={StageContext.LevelPath}");

        // ── 1-2 金币房 ──
        // ⚠️ 2026-09-19：**先离开出生点，再等食人花**。
        //    上一版是"先等花缩回（2.1 秒）再传送"，而实测（`client/Logs/2026-09-19.log` 14:12:41）
        //    马里奥在 1-2 出生点 (0.5,0) 上、**不给任何按键**也会在进关 ~2.9 秒后死亡
        //    （日志里没有"掉出关卡"/"强制死亡"，即 `TickPlayerVsEnemies -> TakeDamage`
        //     —— 那条路径**不打印是哪个敌人**，见 GameplayModule.cs:302）。结果整套戏在死亡/重开
        //     /GameOver 里跑完，最后那张 `sec12-l-mouth12.png` 拍到了**主菜单**（坏图）。
        //    ⇒ 进 1-2 后立刻传送到管顶（离开那个危险点），离开前先 dump 一次附近敌人留证据。
        // ★ 两个"开局就死"的坑（都在 2026-09-19 这一轮实测到，日志里有）：
        //   ① 出生点 (0.5,0)：原版那只从 x≈13 走过来的栗宝宝 **~3 秒**就撞到人（不带任何按键也死）。
        //   ② 进管口顶 (101,3)：`Teal Piranha` 在 x=100.5、伸出时占 y=2.19..3.19 ⇒ 站在管顶就重叠
        //      （实测：`tp → (101.00,3.00)` 之后 **4 毫秒**就 `马里奥死亡`）。
        //   ⇒ 顺序必须是：先离开出生点 → 等花缩回 → 再摆到管顶 + 立刻按 ↓。
        Teleport(30f, 0f);
        await Wait(0.8f);
        L($"PROBE mouth2 1-2 出生点附近敌人：{EnemyDumpNear(0.5f, 8f)}");
        var ph = FindEnemy("Piranha", 100f);
        var flowerDown = false;
        for (var i = 0; i < 160; i++)
        {
            var bb = ph is SuperMario.Module.Entities.IEnemy pe0 ? pe0.Bounds : new Rect(0f, 0f, 0f, 0f);
            if (bb.height < 0.15f) { L($"PROBE mouth2 进管口的花已缩回（等了 {i * 0.15f:F1}s）"); flowerDown = true; break; }
            await Wait(0.15f);
        }
        if (!flowerDown) L("PROBE 警告：进管口那根管子上的食人花 24 秒内一直没缩回");
        Teleport(101f, 3f);
        await Wait(0.2f);
        State("1-2 站在进管口顶");
        _stub.Hold(GameKey.DownArrow, true);
        await Wait(0.35f);
        _stub.Hold(GameKey.DownArrow, false);
        for (var i = 0; i < 100 && !StageContext.SubArea; i++) await Wait(0.2f);
        await Wait(1.0f);
        L($"PROBE mouth2 1-2 密室就位 sub={StageContext.SubArea} 关卡={StageContext.LevelPath} " +
          $"玩家={PosStr(StageContext.Player)} alive={(StageContext.Player != null && StageContext.Player.Alive)}");
        if (!StageContext.SubArea)
            L($"PROBE 警告：按 ↓ 之后没进到 1-2 密室 —— fsm={FsmNow} 关卡={StageContext.LevelPath} " +
              $"存活={(StageContext.Player != null && StageContext.Player.Alive)}（后面那张图会假，已被出图闸门拦下）");
        // 站在出口管口正前方（管口面 x=5、地面顶面 y=0）。同样**不作按键** —— 走路滑出去就提前出管。
        Teleport(3.0f, 0f);
        await Wait(0.8f);
        State("A2 1-2 密室出口管口");
        // ★ 出图闸门：**只有**"真在 1-2 密室且活着"才写这张图。上一版的坏图（标题屏）
        //   就是因为没这道闸门 —— 世界已经回到 Menu 了还照拍。
        var q = StageContext.Player;
        if (FsmNow == "Stage" && StageContext.SubArea && q != null && q.Alive)
            Shot("sec12-l-mouth12");
        else
            L($"PROBE 拒绝出图 sec12-l-mouth12：fsm={FsmNow} sub={StageContext.SubArea} " +
              $"player={(q == null ? "null" : (q.Alive ? "活着" : "死了"))}（图会假，故不写盘）");
        State("mouth2 结束");
        await TailToMenu("mouth2");
    }

    /// <summary>把 x 附近 <paramref name="range"/> 格内的敌人列一条（诊断"到底是谁把马里奥弄死的"）。</summary>
    private static string EnemyDumpNear(float x, float range)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var mb in AllEnemies())
        {
            if (!(mb is SuperMario.Module.Entities.IEnemy e)) continue;
            var b = e.Bounds;
            if (Mathf.Abs(b.center.x - x) > range) continue;
            sb.Append($"[{mb.gameObject.name} x={b.center.x:F2} y={b.center.y:F2} " +
                      $"box=({b.xMin:F2}..{b.xMax:F2},{b.yMin:F2}..{b.yMax:F2}) dead={e.Dead} stompable={e.Stompable}] ");
        }
        return sb.Length == 0 ? "（附近无敌人）" : sb.ToString().TrimEnd();
    }

    // ═══════════ 2026-09-19 追加：物理数值（对照表 四、）═══════════
    //
    // 判据（数值类）：**运行时从对象上读到的值** vs **clone 的真值**
    //   · 水平速度 / 竖直速度 ← `IPlayer.Velocity`（活对象）
    //   · 重力 ← dv / d(**游戏时间**)（`Time.time` 差，而不是真实时间，避免掉帧带来的假斜率）
    //   · 敌人速度 ← 敌人 `transform.position` 在**游戏时间**里的位移
    //   · 关卡时间 ← `StageContext.Score.TimeLeft`
    //   · 台面尺寸 ← 台面 `SpriteRenderer.sprite.bounds.size`
    // ⛔ 这里**不引用任何 `GameConst` 常量**：读数必须来自活对象，代码常量改了也不影响读数。

    public static void Physics() => Start("physics", PhysicsBody);

    /// <summary>按住方向键（可带 Shift）跑到速度不再上升为止，返回稳态 |vx| 与到达它用的游戏时间。</summary>
    private static async Task<(float v, float t)> RunUntilSteady(GameKey dir, bool dash, float timeout)
    {
        _stub.Hold(dir, true);
        if (dash) _stub.Hold(GameKey.LeftShift, true);
        var prev = float.NaN;
        var best = 0f;
        var tAtBest = 0f;
        var tJump = Time.time;
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            await Wait(0.08f);
            var p = StageContext.Player;
            if (p == null) break;
            var v = Mathf.Abs(p.Velocity.x);
            if (v > best + 0.002f) { best = v; tAtBest = Time.time - tJump; }
            if (!float.IsNaN(prev) && Mathf.Abs(v - prev) < 0.004f && v > 0.5f) break;
            prev = v;
        }
        _stub.Hold(dir, false);
        if (dash) _stub.Hold(GameKey.LeftShift, false);
        return (best, tAtBest);
    }

    /// <summary>把马里奥停稳（松掉所有键 + 等摩擦把速度吃掉）。</summary>
    private static async Task Stop()
    {
        _stub.Hold(GameKey.RightArrow, false);
        _stub.Hold(GameKey.LeftArrow, false);
        _stub.Hold(GameKey.LeftShift, false);
        await Wait(1.2f);
    }

    /// <summary>
    /// 量一个敌人的行走速度（位移 ÷ 游戏时间）。**不动马里奥** —— 见调用点的说明。
    /// </summary>
    private static async Task MeasureEnemySpeed(string prefix, float nearX, string label, string cite, float seconds)
    {
        var mb = FindEnemy(prefix, nearX);
        if (mb == null) { L($"PROBE phys {label}：找不到（跳过着一条）"); return; }
        var x1 = P(mb).x; var t1 = Time.time;
        L($"PROBE phys {label}：起点 x={x1:F2}（游戏时间 {t1:F2}），窗口 {seconds:F1} 秒");
        await Wait(seconds);
        if (mb == null) { L($"PROBE phys {label}：采样期间对象被销毁（关卡重开？）⇒ 这次读数作废"); return; }
        var x2 = P(mb).x; var t2 = Time.time;
        L($"PROBE phys {label}速度(运行时) = {Mathf.Abs(x2 - x1) / Mathf.Max(0.0001f, t2 - t1):F3} 格/秒" +
          $"（x {x1:F2}→{x2:F2}，Δt={t2 - t1:F3}s）｜ {cite}");
    }

    private static float Min(List<float> v)
    {
        if (v.Count == 0) return float.NaN;
        var m = float.MaxValue;
        foreach (var f in v) if (f < m) m = f;
        return m;
    }

    private static float Max(List<float> v)
    {
        if (v.Count == 0) return float.NaN;
        var m = float.MinValue;
        foreach (var f in v) if (f > m) m = f;
        return m;
    }

    private static float Median(List<float> v)
    {
        if (v.Count == 0) return float.NaN;
        var s = new List<float>(v);
        s.Sort();
        var m = s.Count / 2;
        return s.Count % 2 == 1 ? s[m] : (s[m - 1] + s[m]) * 0.5f;
    }

    /// <summary>最小二乘拟合 v = slope·t + intercept（只用采样出来的数，不引用任何常量）。</summary>
    private static void FitLine(List<float> ts, List<float> vs, out float slope, out float intercept)
    {
        slope = float.NaN; intercept = float.NaN;
        var n = Mathf.Min(ts.Count, vs.Count);
        if (n < 2) return;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (var i = 0; i < n; i++)
        {
            sx += ts[i]; sy += vs[i]; sxx += (double)ts[i] * ts[i]; sxy += (double)ts[i] * vs[i];
        }
        var den = n * sxx - sx * sx;
        if (Math.Abs(den) < 1e-9) return;
        slope = (float)((n * sxy - sx * sy) / den);
        intercept = (float)((sy - slope * sx) / n);
    }

    /// <summary>
    /// 量火球的四个量（本轮新增）：**水平速度 / 初速竖直分量 / 撞地反弹速度 / 火球自己的重力**。
    /// <para>
    /// 判据 = clone `Assets/Scripts/MarioFireball.cs:8` `absVelocity = new Vector2(20, 11)`：
    /// `:21` 初速 = `(directionX*absVelocity.x, -absVelocity.y)`（⇒ 水平 20、竖直**向下** 11）、
    /// `:50` 撞地面反弹 = `+absVelocity.y`（⇒ 向上 11）；重力出处 = `Mario Fireball.prefab:64` `m_GravityScale: 5.5`。
    /// </para>
    /// <para>
    /// 四条纪律（前三条沿用 <see cref="MeasureEnemySpeed"/> 的）：
    /// ① 场地 = 1-1 的 x=100..138 无坑直道；
    /// ② 量的是**活对象**（场上那个 `Fireball` 物体的 transform）的**位移 ÷ 游戏时间**，不读代码常量；
    /// ③ 每帧判引用还在不在（撞墙 / 寿命到会销毁 ⇒ 那种读数作废）；
    /// ④ **马里奥站在半空里发**（脚底 y=9，离地 12 格）—— 站在地上发时火球 0.03 秒就落地，
    ///    采样器（`Task.Delay` 最小粒度约 15ms）根本抓不到"下落段"，实测第一次跑就是这样采到一段
    ///    已经在上升的曲线（首区间 +5.5）。从半空发能采到 0.17 秒的干净下落段（受落速上限夹断前）。
    /// </para>
    /// <para>
    /// 两条速度都用**逐区间速度的最小二乘拟合**，再外推到关键时刻（不是拿某个采样点充数）：
    /// 下落段直线外推到**按键那一帧**（= 出生时刻）⇒ 初速；上升段直线外推到**反弹点那一帧** ⇒ 反弹速度。
    /// 水平分量取逐区间速度的**中位数**（撞墙瞬间会把它拉成 0，中位数不受影响），并报最小/最大以证"恒速"。
    /// 重力由上升段拟合的斜率给出，另用"反弹后峰值高 = v²/(2g)"交叉印证。
    /// </para>
    /// </summary>
    private static async Task MeasureFireball()
    {
        Teleport(100f, -3f);
        await Wait(0.8f);
        StageContext.Player.PowerUp(PowerState.Fire);
        L($"PROBE fb 已给火形态（power={StageContext.Player.Power}；出生点应 = 脚底 +(0.4,0.6)）");
        await Wait(1.2f);            // 等变大动画走完：Busy 期间 `TickFireballChecks` 直接 return
        Teleport(100f, 9f);          // 半空（下落中）发火球 ⇒ 下落段够长
        await Wait(0.02f);

        var ts = new List<float>();
        var xs = new List<float>();
        var ys = new List<float>();
        var tPress = 0f;
        var spawn = new Vector3();
        var pressed = false;
        var t0 = Time.time;
        while (Time.time - t0 < 1.4f && ts.Count < 200)
        {
            if (!pressed) { tPress = Time.time; _stub.Hold(GameKey.W, true); pressed = true; }
            var f = FindByName("Fireball", 100f);
            if (f != null)
            {
                if (ts.Count == 0) { spawn = P(f); _stub.Hold(GameKey.W, false); L($"PROBE fb 出生点 = ({spawn.x:F2},{spawn.y:F2})，按键 t={tPress:F3}"); }
                ts.Add(Time.time); xs.Add(P(f).x); ys.Add(P(f).y);
            }
            await Wait(0.008f);
        }
        _stub.Hold(GameKey.W, false);
        if (xs.Count < 8) { L($"PROBE fb 采样点太少（{xs.Count}）⇒ 这条读数作废"); return; }

        // 速度用**窗口**算（约 35 毫秒一窗），不用"相邻两点"：
        //   编辑器帧长 33ms，而 `Task.Delay` 最小粒度约 11ms ⇒ 相邻两点常常落在同一帧里，
        //   逐区间速度会变成 0,0,3v,0,0,3v 的锯齿（第一次跑就是这么得到假斜率 +10.37 的）。
        //   窗口速度 = 位移 ÷ 窗长，等于**该窗内的真实平均速度**（与帧相位无关），时间戳取窗中心。
        var vt = new List<float>();
        var vx = new List<float>();
        var vy = new List<float>();
        var wt0 = new List<float>();
        var wt1 = new List<float>();
        const float win = 0.035f;
        for (var i = 0; i + 1 < ts.Count; i++)
        {
            var j = i + 1;
            while (j + 1 < ts.Count && ts[j] - ts[i] < win) j++;
            var dt = ts[j] - ts[i];
            if (dt < 0.02f || dt > 0.12f) continue;
            vt.Add((ts[i] + ts[j]) * 0.5f);
            wt0.Add(ts[i]); wt1.Add(ts[j]);
            vx.Add((xs[j] - xs[i]) / dt);
            vy.Add((ys[j] - ys[i]) / dt);
        }
        if (vy.Count == 0) { L($"PROBE fb 速度窗口一个都算不出（采样 {xs.Count} 点）⇒ 这条读数作废"); return; }
        L($"PROBE fb 首个速度窗口（窗中心在出生后 {vt[0] - tPress:F3}s）= vx {vx[0]:F3} / vy {vy[0]:F3} 格/秒" +
          "（直接读数，不依赖任何拟合）");
        L($"PROBE fb 水平速度(运行时, 逐区间中位数) = {Median(vx):F3} 格/秒" +
          $"（区间 {vx.Count} 个，最小 {Min(vx):F3} / 最大 {Max(vx):F3} ⇒ 恒速）" +
          " ｜ clone `MarioFireball.cs:8` absVelocity.x = 20（`:26` 每帧重写 ⇒ 不减速）");

        // 撞地反弹 = y 的第一个局部最低点
        var lo = -1;
        for (var i = 1; i + 1 < ys.Count; i++)
        {
            if (ys[i] <= ys[i - 1] && ys[i] < ys[i + 1]) { lo = i; break; }
        }
        if (lo < 1) { L("PROBE fb 采样窗口里没抓到撞地点（火球也许在落地前就撞墙了）⇒ 只报水平速度"); return; }

        // 下落段：反弹点之前的区间，且 |vy| < 20（> 20 已被 `MaxFallSpeed` 夹住，斜率不真）
        // 分段**必须按"整窗落在哪一段"判**，不能按窗中心 —— 窗宽 35ms，跨过撞地点的那个窗
        //   会把"下落 + 反弹"平均成一个假速度（上一版就是这么把下落段斜率拉成 -1.74、上升段 0 点的）。
        var dt_ = new List<float>(); var dv_ = new List<float>();
        for (var i = 0; i < vt.Count; i++)
        {
            if (wt1[i] > ts[lo]) continue;                 // 整窗都在撞地点之前 = 下落段
            if (Mathf.Abs(vy[i]) >= 22f) continue;         // 已被 `MaxFallSpeed`(24) 夹住的窗不要（斜率不真）
            dt_.Add(vt[i]); dv_.Add(vy[i]);
        }
        // 上升段：整窗都在撞地点之后、且速度为正的连续段
        var ut = new List<float>(); var uv = new List<float>();
        for (var i = 0; i < vt.Count; i++)
        {
            if (wt0[i] < ts[lo]) continue;
            if (vy[i] <= 0f) break;
            ut.Add(vt[i]); uv.Add(vy[i]);
        }

        float gDown = float.NaN, launch = float.NaN, gUp = float.NaN, bounce = float.NaN;
        if (dt_.Count >= 3)
        {
            FitLine(dt_, dv_, out var sd, out var bd);
            gDown = -sd;
            launch = sd * tPress + bd;
            L($"PROBE fb【下落段拟合】{dt_.Count} 点：重力 = {gDown:F2} 格/秒²、外推到按键帧 t={tPress:F3} 的初速 = " +
              $"{launch:F3} 格/秒（**负 = 向下**）｜ clone `MarioFireball.cs:21` 初速 = -absVelocity.y = -11、" +
              "重力见 `Mario Fireball.prefab:64` 5.5 × -9.81 = 53.96");
        }
        else L($"PROBE fb 下落段只有 {dt_.Count} 个区间（不足 3）⇒ 初速这一条本次没采到（出生点离地太近？）");
        if (ut.Count >= 3)
        {
            FitLine(ut, uv, out var su, out var bu);
            gUp = -su;
            bounce = su * ts[lo] + bu;
            L($"PROBE fb【上升段拟合】{ut.Count} 点：重力 = {gUp:F2} 格/秒²、外推到撞地帧 t={ts[lo]:F3} 的反弹速度 = " +
              $"{bounce:F3} 格/秒（**正 = 向上**）｜ clone `MarioFireball.cs:50` = +absVelocity.y = +11");
        }
        else L($"PROBE fb 上升段只有 {ut.Count} 个区间（不足 3）⇒ 反弹速度这一条本次没采到");

        var peak = ys[lo];
        for (var i = lo; i < ys.Count; i++) if (ys[i] > peak) peak = ys[i];
        var h = peak - ys[lo];
        L($"PROBE fb 撞地点 y={ys[lo]:F3}（t={ts[lo] - ts[0]:F3}s，采样 {xs.Count} 点）⇒ 反弹后峰值高 = {h:F3} 格" +
          $"｜ 交叉印证：若反弹速度为 11，则 h = 11²/(2g) ⇒ 反推重力 = {121f / Mathf.Max(0.0001f, 2f * h):F2} 格/秒²" +
          $"（拟合给出的重力是 {gUp:F2}；clone 火球 = 9.81×5.5 = 53.96，马里奥的是 9.81×5.3×1.64 = 85.27）");

        // 联合交叉印证：落差 H + 初速(拟合) + 重力(拟合) + 落速上限 ⇒ 推出落地时刻，与实测撞地时刻比。
        //   （落速上限 24 是本项目新增，见 E-22；这一段同时验证了"初速向下 11"与"重力 54"两个量。）
        if (!float.IsNaN(gDown) && !float.IsNaN(launch) && gDown > 5f)
        {
            var vMax = SuperMario.Core.GameConst.MaxFallSpeed;
            var v0 = -launch;                       // 向下为正
            var H = spawn.y - ys[lo];
            var tClamp = v0 < vMax ? (vMax - v0) / gDown : 0f;
            var dClamp = v0 * tClamp + 0.5f * gDown * tClamp * tClamp;
            var T = H > dClamp ? tClamp + (H - dClamp) / vMax : Mathf.Sqrt(2f * H / gDown);
            L($"PROBE fb 联合印证（落差 {H:F2} 格 + 初速 {launch:F2} + 重力 {gDown:F2} + 落速上限 {vMax}）" +
              $"⇒ 推出落地 t={T:F3}s，实测撞地 t={ts[lo] - ts[0]:F3}s（差 {Mathf.Abs(T - (ts[lo] - ts[0])):F3}s）");
        }
    }

    /// <summary>把名字以 <paramref name="prefix"/> 开头的敌人全部 Flip 掉（空串 = 全部），返回个数。</summary>
    private static int FlipEnemies(string prefix)
    {
        var n = 0;
        foreach (var mb in AllEnemies())
        {
            if (prefix.Length > 0 && !mb.gameObject.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (mb is SuperMario.Module.Entities.IEnemy en && !en.Dead) { en.Flip(); n++; }
        }
        return n;
    }

    private static async Task PhysicsBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE 没进到关卡，放弃"); return; }

        // ── 关卡时间：进关时（**先读，再挪马里奥** —— 晚 0.6 秒就多掉一格时间）──
        //    注意：探针接上时关卡已经跑了一小会儿（`EnterGame` 要等入场卡 + 读条），
        //    所以这里读到的是 400 减去已过的 tick 数（每 0.4 秒掉 1，出处 `LevelManager.cs:127`）。
        var t0 = Time.time;
        L($"PROBE phys 关卡时间(进关首次读到) = {StageContext.Score?.TimeLeft}" +
          "（clone `GameStateManager.cs:46` ConfigNewGame: timeLeft = 400.5f，HUD 取整显示 400）");
        L($"PROBE phys 场景起点 x={StageContext.Player?.FeetPosition.x:F2} t={t0:F2}");

        // ⚠️ 先把马里奥挪到**安全的落脚点**：4 格高管道的管顶（x=137, y=1）。
        //    为什么必须挪 —— 1-1 的栗宝宝**会一路向左走到关卡起点**（26.5 那只在 14 秒后正好
        //    走到出生点 -10.5），实测把马里奥留在出生点量 5 秒，第 5.1 秒就被撞死、
        //    整关重开、抓到的敌人引用全部失效（读数变成 0.00）。管顶栗宝宝爬不上来。
        Teleport(137f, 1f);
        await Wait(0.6f);

        // ── 先量敌人（趁它们还活着）：栗宝宝 / 乌龟 / 龟壳 ──
        //   ⚠️ 三条纪律（都是第一次跑踩出来的）：
        //   ① **不要把马里奥传送到敌人旁边**去量 —— 栗宝宝会把他撞死（实测：传过去 4 毫秒后
        //      `马里奥死亡`，随后整关重开、刚抓到的敌人引用全部失效 ⇒ 读数变成 0.00）。
        //      敌人从关卡一起铺开、一直自己走（`GameplayModule.Tick` 只 Reap，不做距离剔除），
        //      所以站在关卡起点量它们完全没问题。
        //   ② 量的是**位移 ÷ 游戏时间**，窗口取 5 秒：编辑器掉帧会让"瞬时时速"偏小
        //      （实测 1.2 秒窗口量出 1.90 而不是 2.5）。
        //   ③ 每次读之前都要判引用还在不在（关卡一重开，敌人对象就被销毁了）。
        await MeasureEnemySpeed("Goomba", 100f, "栗宝宝",
            "clone `Brown Goomba.prefab:139` Speed.x = 2.5", 5f);

        // ── 乌龟：1-1 那只从 93.5 一路向左走，**8 秒左右会掉进 72-74 那个坑**，
        //    所以探针起来时它可能已经没了（实测跑过两次：一次还在、一次已经掉了）。
        //    掉了就**主动重开一关**（走真实死亡链路，不是作弊）再量 —— 重开后它回到 93.5。
        var k = FindEnemy("Koopa", 93f);
        if (k == null)
        {
            L("PROBE phys 乌龟已经走掉（1-1 那只 8 秒左右掉进 72-74 的坑）⇒ 走真实死亡链路重开一关再量");
            Teleport(StageContext.Player?.FeetPosition.x ?? 100f, -25f);
            for (var i = 0; i < 120 && FsmNow == "Stage"; i++) await Wait(0.2f);
            for (var i = 0; i < 200 && FsmNow != "Stage"; i++) await Wait(0.2f);
            await WaitStage(25f);
            await Wait(0.4f);
            Teleport(137f, 1f);
            await Wait(0.5f);
            k = FindEnemy("Koopa", 93f);
            L($"PROBE phys 重开后乌龟 = {(k == null ? "还是没有" : P(k).ToString())}");
        }
        if (k != null)
        {
            var kx1 = P(k).x; var kt1 = Time.time;
            await Wait(2.0f);
            if (k != null)
            {
                var kx2 = P(k).x; var kt2 = Time.time;
                L($"PROBE phys 乌龟走路速度(运行时) = {Mathf.Abs(kx2 - kx1) / Mathf.Max(0.0001f, kt2 - kt1):F3} 格/秒" +
                  $"（x {kx1:F2}→{kx2:F2}，Δt={kt2 - kt1:F3}s）｜ clone `Green Koopa.prefab:166` Speed.x = 2.5" +
                  "（窗口只有 2 秒：再长它就掉坑了）");
            }
            else L("PROBE phys 乌龟走路：采样期间对象被销毁 ⇒ 读数作废");
        }
        else L("PROBE phys 乌龟走路：跳过着一条");

        // ── 清掉所有栗宝宝（只留乌龟），免得后面踩壳时被撞死 ──
        var killed = FlipEnemies("Goomba");
        L($"PROBE phys 先清掉 {killed} 只栗宝宝（留乌龟做壳速实验）");
        await Wait(0.6f);

        // ── 龟壳滑行速度：踩成壳 → 踢出去 → 量位移 ──
        //   ⚠️ 两次实测踩出来的坑：
        //   ① 踩下去之后**必须马上把马里奥挪走** —— 他会弹起来再落到壳上，那一下就变成
        //      "踩静止的壳 = 踢出去"（第二个 `踩中敌人 +200`），壳被朝【左】踢，而 1-1 那只
        //      乌龟离 72-74 的坑只有 4 格 ⇒ 壳一滚进坑就被销毁（实测 `pos=已销毁`）。
        //   ② 判"已经变成壳"用**碰撞盒高度**（走路 1.5 格 / 壳 0.9 格），这是活对象读数。
        if (k != null)
        {
            Teleport(P(k).x, P(k).y + 2f);
            for (var i = 0; i < 60; i++)
            {
                if (k == null) break;
                var b = k is SuperMario.Module.Entities.IEnemy ke ? ke.Bounds : new Rect();
                if (b.height < 1.2f) break;          // 已缩成壳
                await Wait(0.05f);
            }
            L($"PROBE phys 踩成壳：pos={(k == null ? "已销毁" : P(k).ToString())} " +
              $"盒高={(k is SuperMario.Module.Entities.IEnemy ke2 ? ke2.Bounds.height : -1f):F2}（走路 1.5 / 壳 0.9）");
            if (k != null)
            {
                // 站到壳的【左边】，右走碰它 ⇒ 朝右踢走（远离 72-74 的坑）
                Teleport(P(k).x - 1.5f, -3f);
                await Wait(0.5f);
                _stub.Hold(GameKey.RightArrow, true);
                await Wait(0.35f);
                _stub.Hold(GameKey.RightArrow, false);
                if (k != null)
                {
                    var sx1 = P(k).x; var tc = Time.time;
                    await Wait(1.5f);
                    if (k != null)
                    {
                        var sx2 = P(k).x; var td = Time.time;
                        L($"PROBE phys 龟壳滑行速度(运行时) = {Mathf.Abs(sx2 - sx1) / Mathf.Max(0.0001f, td - tc):F3} 格/秒" +
                          $"（x {sx1:F2}→{sx2:F2}，Δt={td - tc:F3}s）｜ clone `KoopaShell.cs:12` rollSpeedX = 7");
                    }
                    else L("PROBE phys 龟壳实验：采样期间对象被销毁 ⇒ 读数作废");
                }
            }
        }
        else L("PROBE phys 找不到乌龟（跳过龟壳那一条）");

        // ── 完全清场（用游戏自己的接口 `IEnemy.Flip()`，不改代码）──
        var killed2 = FlipEnemies("");
        L($"PROBE phys 已清场：再 Flip 掉 {killed2} 个敌人（量玩家物理时不再有干扰）");
        await Wait(2.0f);

        // ── 玩家水平速度：1-1 地面 x=75..138 是最长的一段无坑直道，从 x=100 起量（前方 39 格无障碍）──
        Teleport(100f, -3f);
        await Wait(0.8f);
        var (walk, walkT) = await RunUntilSteady(GameKey.RightArrow, false, 4f);
        L($"PROBE phys 走速(运行时, 稳态) = {walk:F3} 格/秒（用时 {walkT:F2}s）" +
          " ｜ clone `Mario.cs:36` maxWalkSpeedX = 5.86");
        await Stop();
        Teleport(100f, -3f);
        await Wait(0.8f);
        var (run, runT) = await RunUntilSteady(GameKey.RightArrow, true, 5f);
        L($"PROBE phys 跑速(运行时, 稳态) = {run:F3} 格/秒（用时 {runT:F2}s）" +
          " ｜ clone `Mario.cs:37` maxRunSpeedX = 9.61");
        await Stop();

        // ── 竖直：站定起跳（全程按住 Space），采一整趟抛物线再算 ──
        Teleport(100f, -3f);
        await Wait(1.0f);
        var yStart = StageContext.Player?.FeetPosition.y ?? 0f;
        _stub.Hold(GameKey.Space, true);
        var samples = new List<(float t, float y, float vy)>();
        var tJump0 = Time.time;
        var f0 = Time.frameCount;
        while (Time.time - tJump0 < 3.0f)
        {
            var p = StageContext.Player;
            if (p == null) break;
            samples.Add((Time.time, p.FeetPosition.y, p.Velocity.y));
            if (samples.Count > 8 && p.Grounded) break;
            await Wait(0.008f);
        }
        _stub.Hold(GameKey.Space, false);
        var frames = Time.frameCount - f0;
        // 计算（全部只用采样出来的数，不引用常量）
        var v0 = float.MinValue;
        var peak = yStart;
        float tUpA = 0f, vUpA = 0f, tUpB = 0f, vUpB = 0f, tDnA = 0f, vDnA = 0f, tDnB = 0f, vDnB = 0f;
        foreach (var s in samples)
        {
            if (s.vy > v0) v0 = s.vy;
            if (s.y > peak) peak = s.y;
            if (s.vy > 5f) { if (tUpA == 0f) { tUpA = s.t; vUpA = s.vy; } tUpB = s.t; vUpB = s.vy; }
            // ⚠️ 下落这一段只取 **|vy| < 20** 的采样：`MaxFallSpeed = 24` 会把末速夹住，
            //    夹住之后的斜率是假的（实测按 |vy| < 20 取之前，量出 72.22 而不是 85.27）。
            if (s.vy < -5f && s.vy > -20f) { if (tDnA == 0f) { tDnA = s.t; vDnA = s.vy; } tDnB = s.t; vDnB = s.vy; }
        }
        var gUp = (vUpA - vUpB) / Mathf.Max(0.0001f, tUpB - tUpA);
        var gDown = (vDnA - vDnB) / Mathf.Max(0.0001f, tDnB - tDnA);
        L($"PROBE phys 站定起跳：起跳初速(运行时, vy 峰值) = {v0:F2} 格/秒 ｜ clone `Mario.cs:92` jumpSpeedY = 15");
        L($"PROBE phys 上升重力(运行时, 按住 Space) = {gUp:F2} 格/秒²（vy {vUpA:F2}→{vUpB:F2}，" +
          $"Δt={tUpB - tUpA:F3}s）｜ clone = 9.81×5.3×0.47 = {9.81f * 5.3f * 0.47f:F2}");
        L($"PROBE phys 下落重力(运行时) = {gDown:F2} 格/秒²（vy {vDnA:F2}→{vDnB:F2}，Δt={tDnB - tDnA:F3}s，" +
          $"只在 |vy| < 20 的区间取，避开落速上限）｜ clone = 9.81×5.3×1.64 = {9.81f * 5.3f * 1.64f:F2}");
        L($"PROBE phys 满按跳峰值高度 = {peak - yStart:F3} 格（起跳点 y={yStart:F3}；采样 {samples.Count} 点 / {frames} 帧）");

        // ── 短按跳（0.06 秒）：证明"松手早 ⇒ 跳得低"（原版的可变跳跃高度）──
        await Wait(1.2f);
        Teleport(100f, -3f);
        await Wait(1.0f);
        var yStart2 = StageContext.Player?.FeetPosition.y ?? 0f;
        _stub.Hold(GameKey.Space, true);
        await Wait(0.06f);
        _stub.Hold(GameKey.Space, false);
        var peak2 = yStart2;
        var t2 = Time.time;
        while (Time.time - t2 < 2.5f)
        {
            var p = StageContext.Player;
            if (p == null) break;
            if (p.FeetPosition.y > peak2) peak2 = p.FeetPosition.y;
            if (p.Grounded && Time.time - t2 > 0.3f) break;
            await Wait(0.008f);
        }
        L($"PROBE phys 短按跳峰值高度 = {peak2 - yStart2:F3} 格（松手 0.06 秒）—— 应明显低于满按");

        // ── 起跳初速：奔跑中起跳（应取到 18.75 那一档）──
        Teleport(100f, -3f);
        await Wait(0.8f);
        _stub.Hold(GameKey.RightArrow, true);
        _stub.Hold(GameKey.LeftShift, true);
        await Wait(1.6f);
        var vBeforeJump = Mathf.Abs(StageContext.Player?.Velocity.x ?? 0f);
        _stub.Hold(GameKey.Space, true);
        var v0r = 0f;
        var tr0 = Time.time;
        while (Time.time - tr0 < 0.6f)
        {
            var p = StageContext.Player;
            if (p != null && p.Velocity.y > v0r) v0r = p.Velocity.y;
            await Wait(0.008f);
        }
        _stub.Hold(GameKey.Space, false);
        _stub.Hold(GameKey.RightArrow, false);
        _stub.Hold(GameKey.LeftShift, false);
        L($"PROBE phys 跑跳：起跳前 |vx| = {vBeforeJump:F2}（应 ≥ 8.67 才进奔跑档）｜ vy 峰值 = {v0r:F2} 格/秒" +
          " ｜ clone `Mario.cs:100` jumpSpeedY = 18.75");
        await Wait(2.5f);
        await Stop();

        // ── 火球（本轮新增）：水平 20 / 竖直 11（向下）/ 反弹 11 / 火球自己的重力 5.5×9.81 ──
        //    放在最后：给火形态会让马里奥变大，前面那些读数（走/跑/跳）都用小形态量完再动。
        await MeasureFireball();

        // ── 死亡后重开的时间 ──
        var before = StageContext.Score?.TimeLeft ?? -1;
        Teleport(StageContext.Player?.FeetPosition.x ?? 100f, -25f);
        L($"PROBE phys 死亡前 TimeLeft={before}；已把马里奥丢出关卡");
        for (var i = 0; i < 100 && FsmNow == "Stage"; i++) await Wait(0.2f);
        for (var i = 0; i < 200 && FsmNow != "Stage"; i++) await Wait(0.2f);
        await WaitStage(25f);
        L($"PROBE phys 死亡重开后 TimeLeft = {StageContext.Score?.TimeLeft}" +
          "（clone `GameStateManager.cs:59-62` ConfigReplayedLevel: timeLeft = 400.5f）");
        State("物理场景结束");
    }

    // ───────────────────── 场景：升降台 spawner（E-21）─────────────────────

    public static void PlatformSpawn() => Start("platformspawn", PlatformSpawnBody);

    /// <summary>场上（活着的）升降台个数。</summary>
    private static int CountPlatforms()
    {
        var n = 0;
        foreach (var mb in AllByName("MovingPlatform"))
        {
            if (mb != null && mb.transform.position.y > -50f) n++;
        }
        return n;
    }

    private static async Task PlatformSpawnBody()
    {
        await Enter12();
        if (!IsStage) { L("PROBE 没进到 1-2，放弃"); return; }

        // ⚠️ 1-2 是地下关：地面占 y=−2..−1 ⇒ **地面顶面 = 0**（不是 1-1 的 −3）。
        const float Ground = 0f;

        // ── ① spawner 就位（x 必须等于原版 spawner 的世界 x）──
        var spawners = new List<MonoBehaviour>();
        foreach (var mb in AllByName("MovingPlatformSpawner")) spawners.Add(mb);
        L($"PROBE plat spawner 个数 = {spawners.Count}");
        foreach (var s in spawners)
        {
            L($"PROBE plat spawner @ x={s.transform.position.x:F3} y={s.transform.position.y:F3} " +
              $"（原版 clone `World 1-2.unity` 两个实例 root x = 152.8 / 137.8）");
        }

        // ── ③ x 落点：把马里奥挪到 spawner 附近（40 格内才会生成）──
        Teleport(145f, Ground);
        await Wait(0.6f);

        // ── 记录平台数量随时间的曲线（生成节奏 = 1.5 秒/台）──
        var t0 = Time.time;
        for (var i = 0; i < 120; i++)
        {
            var n = CountPlatforms();
            if (i % 3 == 0 || n > 0)
                L($"PROBE plat t={Time.time - t0:F2} 场上平台数={n}");
            if (i == 0) await Wait(0.2f); else await Wait(0.25f);
            if (Time.time - t0 > 12f) break;
        }

        // ── ② 台面尺寸：读活对象上的 SpriteRenderer ──
        var plat = FindByName("MovingPlatform", 152f);
        if (plat == null) { L("PROBE plat 一直没生成平台 —— 检查 spawner 的触发条件"); return; }
        DumpArt("升降台", plat.gameObject);
        var art = plat.transform.Find("Art");
        var sr = art == null ? null : art.GetComponent<SpriteRenderer>();
        var sz = sr != null && sr.sprite != null ? (Vector2)sr.sprite.bounds.size : Vector2.zero;
        L($"PROBE plat 台面（运行时读数）= {sz.x:F2} × {sz.y:F2} 格，贴图={sr?.sprite?.name} " +
          $"｜ clone `Moving Platform Vertical.prefab:121` m_Size {{x: 3, y: 0.5}}");
        L($"PROBE plat 平台中心 x（运行时）= {P(plat).x:F3} ｜ clone 实例 root x = 152.8");

        // ── 三态截图 ①：出现点上方的诊断机位（生成态）──
        await DiagShot("probe-plat-spawn", P(plat).x, -4f + 3f, 6f);
        await Wait(0.5f);

        // ── 三态截图 ②：正常机位 + 站在台面上（上升态）──
        // 平台以 3 格/秒上升 ⇒ 不能"先传送到它当前位置"（0.9 秒后它已经走了 2.7 格）。
        // 所以先等它走到可见带中段（y≈8，±0.25 格），再精确落在它台面上。
        Teleport(145f, Ground);
        await Wait(0.5f);
        MonoBehaviour pl = null;
        for (var i = 0; i < 400; i++)
        {
            pl = FindByName("MovingPlatform", 152f);
            if (pl != null && Mathf.Abs(P(pl).y - 8f) <= 0.3f) break;
            await Wait(0.05f);
        }
        if (pl != null)
        {
            Teleport(P(pl).x, P(pl).y + 0.25f);      // 台面顶面 = 中心 + 0.25 格
            await Wait(0.9f);
            var now = FindByName("MovingPlatform", 152f);
            State("站在升降台上");
            L($"PROBE plat 骑乘：平台中心 y={(now == null ? -999f : P(now).y):F2} " +
              $"马里奥脚底 y={StageContext.Player?.FeetPosition.y:F2} grounded={StageContext.Player?.Grounded} " +
              $"（差值应 ≈ 0.25 格 = 半格厚的台面顶面）");
            Shot("probe-plat-rise");
        }
        else L("PROBE plat 等不到可见带中的平台（跳过着一张）");

        // ── 三态截图 ③：等它离屏销毁，正常机位拍"画面里没有平台" ──
        Teleport(145f, Ground);
        var seen = 0; var tWait = Time.realtimeSinceStartup;
        var lastCount = CountPlatforms();
        while (Time.realtimeSinceStartup - tWait < 25f)
        {
            await Wait(0.25f);
            var n = CountPlatforms();
            if (n > seen) seen = n;
            if (n < lastCount)
            {
                L($"PROBE plat 平台数下降：{lastCount} → {n}（离屏销毁；销毁行见离线脚本判据）t={Time.time - t0:F2}");
                lastCount = n;
                if (n == 0) break;
            }
            else lastCount = n;
        }
        await Wait(0.3f);
        L($"PROBE plat 三态收尾：曾同时在场的平台最多 {seen} 个，当前 {CountPlatforms()} 个");
        State("离屏销毁后");
        Shot("probe-plat-gone");
    }

    // ───────────────────── 场景：管道五处表现（2026-09-19 用户报）─────────────────────
    //
    // 用户原话（5 条）：
    //   ① 「金币我记得 飞一小会就会变成分数」
    //   ② 「现在人下管道，为什么能看到人？？？不应该被管道盖住吗？」
    //   ③ 「人出管道时候 进管道效果没有，人就卡在管道外，然后操作不了（金币房 横向管道）」
    //   ④ 「管道没把食人花完全挡住」
    //   ⑤ 「出管道一瞬间会被管道弹开」
    //
    // 判据（数值 + 画面**双证据**；⛔ 不靠"我觉得像"）：
    //   ② 数值 = 管中移动期间玩家精灵 `order` 必须是 −1（地形 0 之下、与食人花同层）；
    //      画面 = `pipefix-a-sink.png`（下潜途中：只露管口以上那半截，下面被管子挡住）。
    //   ③ 数值 = `侧向进管 … 走距` ≈ 管口宽（2 格）；之后**没有** `管中过场超时` 兜底；
    //      触发→离开密室 ≤ 1.5 秒（修前 3.01 秒）；画面 = `pipefix-b-enter.png`（走进管口、没入管里）。
    //   ⑤ 数值 = 升起 + 落位后逐 0.05 秒采"相邻两步位移"：升起段 ≈ 速度×步长（有位移是对的），
    //      **落位后必须 ≈ 0**（弹开就会看到一个大位移）；画面 = `pipefix-d-rise.png`（升起中）。
    //   ④ 数值 = 每朵花的 x 必须等于它那根管子的中线（`SolidSpanAt` 从关卡实心位图量）；
    //      画面 = `pipefix-e-piranha.png`（缩回态：管口外一个像素都不该有花 —— 由离线像素脚本判）。
    //   ① 数值 = 顶多金币砖后场上出现 `ScorePopup` 且其 Text 逐字 = "200"；画面 = `pipefix-f-coinpop.png`。

    public static void PipeBugs() => Start("pipebugs", PipeBugsBody);

    private static async Task PipeBugsBody()
    {
        await EnterGame();
        if (!IsStage) { L("PROBE pipebugs 没进到 1-1，放弃"); return; }
        await EnsureStage("pipebugs");

        // ═══ ① 金币 → 分数：顶多金币砖 T(80,0)（走游戏自己的 IBlock.HitFromBelow，与真跳顶同一条链）═══
        KillGoombasNear(80f, 14f);
        await TeleportGround(80.5f, "coinpop");
        // ⚠️ 相机是"平滑跟随 + **永不后退**"（`CameraModule` 的 `_maxReachedX`）：刚传送完相机还在路上
        //    （上一轮实测：传送后 152 ms 抓帧，人在 80.5 而相机只到 x=46.68 ⇒ 帧里根本没有人和飘字）。
        //    必须先等它跟上来再顶砖，否则这一条永远拍成空机位。
        await Wait(2.0f);
        L($"PROBE #1 等相机跟上：{CamReadout()}");
        var blk = GameObject.Find("Block_BrickMultiCoin_80_0");
        if (blk == null)
        {
            L("PROBE #1 找不到多金币砖 Block_BrickMultiCoin_80_0（关卡数据改过？这一条作废）");
        }
        else
        {
            var ib = blk.GetComponent<SuperMario.Module.Entities.IBlock>();
            L($"PROBE #1 顶砖前 币={StageContext.Score?.Coins} 分={StageContext.Score?.Points} 飘字={ScorePopupCount()}");
            ib.HitFromBelow(true);
            await Wait(0.05f);
            L($"PROBE #1 顶砖后 币={StageContext.Score?.Coins} 分={StageContext.Score?.Points} " +
              $"飘字={ScorePopupCount()} 读数[{ScorePopupReadout()}]");
            await ShotGated("pipefix-f-coinpop", "金币→分数", () => ScorePopupCount() > 0);
            var tPop = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tPop < 1.5f && ScorePopupCount() > 0) await Wait(0.1f);
            L($"PROBE #1 收尾：飘字={ScorePopupCount()}（原版那行字只活 0.5 秒 ⇒ 应已回收）" +
              $" 用时={Time.realtimeSinceStartup - tPop:F2}s");
        }

        // ═══ ② 下管：管中移动期间玩家必须画到地形之下（−1），且画面里"没入管口"的那截看不见 ═══
        StageContext.Player.PowerUp(PowerState.Big);   // 用户那次就是大马里奥（下潜 2.13 格，好取"半截"帧）
        await Wait(2.6f);                              // 长大动画（GameConst.GrowTime）
        await EnsureStage("pipebugs-进管");
        Teleport(44.5f, 1.0f);
        await Wait(0.8f);
        State("站在第 4 根水管顶");
        L($"PROBE #2 管前：power={(StageContext.Player == null ? "?" : StageContext.Player.Power.ToString())} " +
          $"{PlayerSpriteOrder()}（小形态下潜 1.06 格 / 大形态 2.13 格 ⇒ 抓帧必须取「刚进管」那一瞬）");
        _stub.Hold(GameKey.DownArrow, true);
        await Wait(0.2f);
        _stub.Hold(GameKey.DownArrow, false);
        var shotSink = false;
        for (var i = 0; i < 12; i++)
        {
            await Wait(0.07f);
            var pNow = StageContext.Player;
            L($"PROBE #2 下潜 t={(i + 1) * 0.07:F2}s {PlayerSpriteOrder()} 马里奥={PosStr(pNow)} " +
              $"busy={(pNow != null && pNow.Busy)} sub={StageContext.SubArea}");
            // 抓帧取**第一个"正在管中移动"的采样**（既有小形态 0.42 秒、也有大形态 0.85 秒的余量），
            // 不能固定取 i==3：小形态那一次 i==2 就已经换到金币房了（上一轮 ② 画面侧就是这么丢的）。
            // 这一格必须用**诊断机位**：进管前刚在 x=80.5 待过，而相机永不后退 ⇒ 自然机位永远回不到 44.5；
            // DiagShot 自己暂停世界（`Fsm.Transition(Pause)` + timeScale=0）再摆相机，
            // 顺带把"下潜到一半"这一瞬冻住 —— 正好是要取证的那一帧。
            if (!shotSink && !StageContext.Underground && pNow != null && pNow.Busy)
            {
                shotSink = true;
                await DiagShot("pipefix-a-sink", 44.5f, 2.5f, 7.5f);
            }
            if (StageContext.Underground) break;   // 已经换到金币房
        }
        if (!shotSink) L("PROBE 警告：② 抓帧没落地（采样期间就已经换场）—— 下一轮要把下潜距离拉长");
        for (var i = 0; i < 80 && !StageContext.SubArea; i++) await Wait(0.2f);
        await Wait(0.8f);
        State("密室内");

        // ═══ ③ 出管（金币房横向管）：走进管口 + 换场必须快（修前是 3 秒兜底超时）═══
        //
        // ⚠️ 站位只能像 `Mouth2` 那样**先摆到管口正前方**（`Teleport(-5.0f,-3f)`）再按右键：
        //    金币房里有一块 3 格高的平台砖 T x=−13..−7 / y=−3..−1，从出生点 (-15) 裸按右键会被它
        //    的左面挡在 x=−13.38（上一轮实测就是这个：`马里奥=(-13.38,-3.00) 右边缘=-13.00`，永远到不了管口）。
        //    摆位后**不作任何跳/走**，只用短促的右键把右边缘送到管口面 x=−4。
        Teleport(-5.0f, -3f);
        await Wait(0.8f);
        L($"PROBE #3 就位管口前：马里奥={PosStr(StageContext.Player)} {CamReadout()}");
        for (var i = 0; i < 60; i++)
        {
            var p = StageContext.Player;
            if (p == null || !p.Alive) break;
            if (p.Bounds.xMax >= -4.1f) break;     // 管口面 = x=−4（PipeWarpTable）
            _stub.Hold(GameKey.RightArrow, true);
            await Wait(0.12f);
            _stub.Hold(GameKey.RightArrow, false);
        }
        _stub.Hold(GameKey.RightArrow, false);
        var pAtMouth = StageContext.Player;
        L($"PROBE #3 走到管口前：马里奥={PosStr(pAtMouth)} " +
          $"右边缘={(pAtMouth == null ? 0f : pAtMouth.Bounds.xMax):F2}（管口面 x=−4；差 ≤0.05 就会触发更换场）");
        var tEnter = Time.realtimeSinceStartup;
        var shotEnter = false;
        var leaveSec = -1f;
        for (var i = 0; i < 300; i++)
        {
            await Wait(0.05f);
            var p = StageContext.Player;
            if (StageContext.SubArea && !shotEnter && p != null && p.Busy)
            {
                shotEnter = true;
                await ShotGated("pipefix-b-enter", "走进管口", () => true);
            }
            if (!StageContext.SubArea) { leaveSec = Time.realtimeSinceStartup - tEnter; break; }
        }
        L($"PROBE #3 触发→离开密室用时 {leaveSec:F2} 秒（修前 = 3.01 秒兜底超时 + [Warn] 管中过场超时；" +
          $"修后应 ≈ 走距 ÷ 3.05 格/秒 ≈ 0.6 秒）");
        await EnsureStage("pipebugs-出管");

        // ═══ ⑤ 出管升起：逐 0.05 秒采"相邻两步位移"；落位后必须 ≈0 ═══
        var xPrev = float.NaN; var yPrev = float.NaN;
        var shotRise = false;
        var moves = new List<string>();
        for (var i = 0; i < 60; i++)
        {
            await Wait(0.05f);
            var p = StageContext.Player;
            if (p == null) break;
            var x = p.FeetPosition.x; var y = p.FeetPosition.y;
            var dx = float.IsNaN(xPrev) ? 0f : Mathf.Abs(x - xPrev);
            var dy = float.IsNaN(yPrev) ? 0f : Mathf.Abs(y - yPrev);
            moves.Add($"({x:F2},{y:F2})Δ{dx + dy:F2}{(p.Busy ? "*" : "")}");
            if (!shotRise && p.Busy)
            {
                shotRise = true;
                // 出管后 `Resume()` → `Camera.Setup` 会 `_snap=true` ⇒ 下一帧相机直接归位到玩家身上，
                // 所以这里用自然机位就行（闸门再确认一次"相机确实在玩家身上"）。
                await ShotGated("pipefix-d-rise", "出管升起", CamNearPlayer);
            }
            xPrev = x; yPrev = y;
            if (!p.Busy && i >= 8) break;
        }
        var tail = moves.Count <= 4 ? moves : moves.GetRange(moves.Count - 4, 4);
        L($"PROBE #5 出管升起逐帧位移（* = 仍在过场）：{string.Join(" ", moves)}");
        L($"PROBE #5 落位后最后 4 次读数：{string.Join(" ", tail)}（位移都是 0.00 ⇒ 没有被管子弹开）");
        State("出管落位后");

        // ═══ ④ 食人花被管子完全挡住：1-2 地下段有 3 朵 ═══
        CallFlow("NextLevel");
        await Wait(1.2f);
        await WaitStage(25f);
        await Wait(0.8f);
        if (!IsStage) { L("PROBE #4 没进到 1-2，这一条作废（其余 4 条不受影响）"); return; }
        KillGoombasNear(104f, 12f);
        // 站在两根管子中间：与两朵花的水平距离都 > 2 格 ⇒ 它们会停在管里（缩回态）
        Teleport(104f, 0f);
        await Wait(1.6f);
        State("1-2（食人花取证机位）");
        // ① 先在**伸出态**量每朵花那根管子的中线（缩回态时 `SolidSpanAt` 落进地板行 ⇒ 量到 17 格宽的地板，
        //    上一轮就是这么误导的）；② 再等全部缩回、抓缩回态那一帧（"管口外不该有花"的判据在那一帧）。
        await WaitPiranhaOutThenDump();
        await ShotWhenPiranhaIn("pipefix-e-piranha");
    }

    /// <summary>场上活着的分数飘字个数（`ScorePopup` 是 internal ⇒ 按类型名找）。</summary>
    private static int ScorePopupCount()
    {
        var n = 0;
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            if (mb != null && mb.GetType().Name == "ScorePopup") n++;
        return n;
    }

    /// <summary>飘字的运行时读数：世界坐标 + 那行 Text 的文本/字体/字号（"数字 + 画面要对得上"）。</summary>
    private static string ScorePopupReadout()
    {
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb == null || mb.GetType().Name != "ScorePopup") continue;
            var t = mb.GetComponentInChildren<UnityEngine.UI.Text>(true);
            return $"pos=({mb.transform.position.x:F2},{mb.transform.position.y:F2}) " +
                   $"scale={mb.transform.localScale.x:F4} " +
                   $"text={(t == null ? "缺" : "\"" + t.text + "\"")} " +
                   $"font={(t == null || t.font == null ? "缺" : t.font.name)} " +
                   $"fontSize={(t == null ? 0 : t.fontSize)} 字号像素={(t == null ? 0f : t.fontSize * mb.transform.localScale.x):F3} 格";
        }
        return "无";
    }

    /// <summary>玩家精灵的排序值（判据：管中移动期间必须是 −1）。</summary>
    private static int PlayerSpriteOrderValue()
    {
        var p = StageContext.Player;
        if (p == null) return 999;
        var sr = p.Transform.GetComponentInChildren<SpriteRenderer>(true);
        return sr == null ? 999 : sr.sortingOrder;
    }

    private static string PlayerSpriteOrder() => $"sprite.order={PlayerSpriteOrderValue()}（正常 10 / 管中 −1）";

    /// <summary>
    /// 相机读数 —— 判据要能回答"人到底在不在帧里"（可见半宽 = 正交半高 × 宽高比，由相机真实参数算，不写死）。
    /// <para>上一轮两条画面侧判据作废就是因为没这一行：人 80.5 / 相机 46.68，帧里什么都没有。</para>
    /// </summary>
    private static string CamReadout()
    {
        var c = Camera.main;
        if (c == null) return "无相机";
        var px = StageContext.Player == null ? 0f : StageContext.Player.FeetPosition.x;
        var halfW = c.orthographicSize * c.aspect;
        var dx = Mathf.Abs(c.transform.position.x - px);
        return $"camX={c.transform.position.x:F2} camY={c.transform.position.y:F2} 正交半高={c.orthographicSize:F2} " +
               $"可见半宽={halfW:F2} 玩家x={px:F2} 与相机差={dx:F2} 在帧内={dx <= halfW}";
    }

    /// <summary>相机是否已经跟到玩家身上（出图闸门用 —— 相机会"永不后退"，回不了头的机位拍出来是空的）。</summary>
    private static bool CamNearPlayer()
    {
        var c = Camera.main;
        var p = StageContext.Player;
        if (c == null || p == null) return false;
        var halfW = c.orthographicSize * c.aspect;
        return Mathf.Abs(c.transform.position.x - p.FeetPosition.x) <= halfW;
    }

    /// <summary>
    /// 以 (<paramref name="x"/>,<paramref name="y"/>) 为参照，从关卡**自己的实心位图**量出左右连续实心列区间
    /// （用来回答"那朵花所在的管子有多宽、中线在哪"—— 判据不能靠我记的坐标）。
    /// </summary>
    private static string SolidSpanAt(float x, float y)
    {
        var lv = StageContext.Level;
        if (lv == null) return "无关卡";
        var cy = Mathf.FloorToInt(y);
        var cx = Mathf.FloorToInt(x);
        var solidHere = lv.IsSolidTile(cx, cy);
        var l = cx; while (l > cx - 8 && lv.IsSolidTile(l - 1, cy)) l--;
        var r = cx; while (r < cx + 8 && lv.IsSolidTile(r + 1, cy)) r++;
        var width = r - l + 1;
        return $"该行实心列 [{l},{r}] 宽 {width} 格 中线 x={l + width * 0.5f:F1}（参照格实心={solidHere}，行 y={cy}）";
    }

    /// <summary>把场上所有食人花的位置/包围盒与它那根管子的中线一次打出来（判据 = 两者相等）。</summary>
    private static void DumpPiranhas()
    {
        var n = 0;
        foreach (var mb in AllEnemies())
        {
            if (mb == null) continue;
            if (!mb.gameObject.name.StartsWith("Piranha", StringComparison.Ordinal)) continue;
            var p = P(mb);
            var b = mb is SuperMario.Module.Entities.IEnemy e ? e.Bounds : new Rect();
            n++;
            L($"PROBE #4 食人花 x={p.x:F2} 底边y={p.y:F2} box=[{b.xMin:F2},{b.xMax:F2}]x[{b.yMin:F2},{b.yMax:F2}] " +
              $"{SolidSpanAt(p.x, p.y + 0.7f)}");
        }
        L($"PROBE #4 场上食人花 {n} 朵（每朵的 x 必须等于它那根管子的中线 x）");
    }

    /// <summary>
    /// 等某一朵花完全伸出（包围盒高 &gt; 1.4）再打一次中线读数。
    /// <para>
    /// ⚠️ 为什么必须等"伸出态"：`SolidSpanAt(p.x, p.y + 0.7f)` 的参照行是"花底边往上 0.7 格"——
    /// 缩回时花底在管子下部甚至落进地板行，量到的是**地板**（实测 17 格宽），读数会把判据带偏。
    /// </para>
    /// </summary>
    private static async Task<bool> WaitPiranhaOutThenDump(float timeout = 14f)
    {
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            foreach (var mb in AllEnemies())
            {
                if (mb == null) continue;
                if (!mb.gameObject.name.StartsWith("Piranha", StringComparison.Ordinal)) continue;
                if (mb is SuperMario.Module.Entities.IEnemy e && e.Bounds.height > 1.4f)
                {
                    L($"PROBE #4 等到伸出态（用时 {Time.realtimeSinceStartup - t0:F2}s）⇒ 量每朵花的管子中线");
                    DumpPiranhas();
                    return true;
                }
            }
            await Wait(0.15f);
        }
        L($"PROBE 警告：{timeout} 秒内没有花完全伸出 ⇒ 中线读数未取（缩回态量到的是地板行）");
        return false;
    }

    /// <summary>等到**所有**食人花都缩回管里（包围盒高度 ≈ 0）再抓帧 —— 缩回态的遮挡判据就在这一帧。</summary>
    private static async Task<bool> ShotWhenPiranhaIn(string name, float timeout = 15f)
    {
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout)
        {
            var any = false; var allIn = true;
            foreach (var mb in AllEnemies())
            {
                if (mb == null) continue;
                if (!mb.gameObject.name.StartsWith("Piranha", StringComparison.Ordinal)) continue;
                any = true;
                if (mb is SuperMario.Module.Entities.IEnemy e && e.Bounds.height > 0.01f) { allIn = false; break; }
            }
            if (any && allIn)
            {
                L($"PROBE #4 三朵花都缩回管里（用时 {Time.realtimeSinceStartup - t0:F2}s）⇒ 抓帧");
                await ShotGated(name, "食人花缩回", () => true);
                return true;
            }
            await Wait(0.15f);
        }
        L($"PROBE 警告：{name} 在 {timeout} 秒内等不到「全部缩回」⇒ 未出图");
        return false;
    }
}
