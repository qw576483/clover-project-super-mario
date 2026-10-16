# -*- coding: utf-8 -*-
"""对刚重采的截图做**确定性**统计（§1.12：判定交给脚本）：
 * 唯一色数 / 主色 / 主色占比  -> 判"退化帧"
 * 深色（0.95 黑遮罩）占比     -> 判结算/暂停面板**真的画在画面上了**
 * 装饰色带（山/灌木绿、云白）在天空行的列区间 -> 判 1-2 地表段的装饰可见
 * 音量条：在填充条所在的行带上量"金色像素"的**列宽**（两态对比）
"""
import io, os, re, sys
import numpy as np
from PIL import Image

ROOT = r"clover-project-super-mario"
SHOTS = os.path.join(ROOT, "client", "Assets", "Screenshots")
OUT = os.path.join(ROOT, ".ai-tmp/test/shot_stats.out.txt")
GREEN = np.array([28, 132, 20])       # 我方装饰绿（山/灌木）
GOLD = np.array([255, 200, 60])       # 音量条填充色（UIBuilder.CoinGold 附近）


def load(name):
    return np.array(Image.open(os.path.join(SHOTS, name)).convert("RGB"), dtype=np.int16)


def near(a, c, tol=40):
    return (np.abs(a - c).sum(axis=2) <= tol)


def main():
    f = io.open(OUT, "w", encoding="utf-8")
    names = sorted(n for n in os.listdir(SHOTS) if n.endswith(".png"))
    f.write(u"# 截图统计（%d 张）\n" % len(names))
    for n in names:
        try:
            a = load(n)
        except Exception as e:
            f.write(u"%-34s 读取失败 %s\n" % (n, e))
            continue
        h, w = a.shape[:2]
        flat = a.reshape(-1, 3)
        key = (flat[:, 0].astype(np.int32) << 16) | (flat[:, 1].astype(np.int32) << 8) | flat[:, 2]
        vals, cnt = np.unique(key, return_counts=True)
        order = np.argsort(-cnt)
        modal = int(vals[order[0]])
        modal_rgb = ((modal >> 16) & 255, (modal >> 8) & 255, modal & 255)
        dark = float((flat.sum(axis=1) < 120).mean())
        f.write(u"%-34s %4dx%-4d 唯一色=%-5d 主色=%-16s 占比=%.3f 暗像素=%.3f\n"
                % (n, w, h, len(vals), str(modal_rgb), cnt[order[0]] / len(key), dark))
    f.write(u"\n# 1-2 地表段装饰可见性（绿 = 山/灌木；云 = 近白且非 HUD 区域）\n")
    for n in [u"walk12end-1-surface-spawn.png", u"walk12end-2-surface.png", u"walk12end-3-flag.png",
              u"sec12-d-surface-spawn.png", u"sec12-e-stairbase.png", u"sec12-f-stairs.png",
              u"sec12-g-stairtop.png", u"sec12-h-flag.png", u"sec12-i-castle.png", u"sec12-j-result.png"]:
        if not os.path.exists(os.path.join(SHOTS, n)):
            f.write(u"%-34s 不存在\n" % n)
            continue
        a = load(n)
        h, w = a.shape[:2]
        body = a[int(h * 0.15):int(h * 0.80), :]     # 去掉 HUD 与底部
        g = near(body, GREEN, 60)
        green_px = int(g.sum())
        cols = np.where(g.any(axis=0))[0]
        runs = []
        if len(cols):
            s = cols[0]
            for i in range(1, len(cols)):
                if cols[i] != cols[i - 1] + 1:
                    if cols[i - 1] - s >= 3:
                        runs.append((int(s), int(cols[i - 1])))
                    s = cols[i]
            if cols[-1] - s >= 3:
                runs.append((int(s), int(cols[-1])))
        f.write(u"%-34s 绿像素=%-6d 绿列区间(px)=%s\n" % (n, green_px, runs[:12]))
    f.write(u"\n# 音量条：黄金填充色的列宽（同一条 y 带内）\n")
    for n in [u"pause.png", u"pause-vol-a.png", u"pause-vol-b.png"]:
        p = os.path.join(SHOTS, n)
        if not os.path.exists(p):
            f.write(u"%-34s 不存在\n" % n)
            continue
        a = load(n)
        h, w = a.shape[:2]
        g = near(a, GOLD, 90)
        rows = g.sum(axis=1)
        best = [(int(r), int(rows[r])) for r in np.argsort(-rows)[:3]]
        f.write(u"%-34s 画面=%dx%d 金色像素总数=%-6d 最宽三行=(行,宽)=%s\n" % (n, w, h, int(g.sum()), best))
        for r, _ in best[:2]:
            cols = np.where(g[r])[0]
            if len(cols):
                f.write(u"        行 %d：列 %d..%d（跨度 %d px）\n" % (r, cols.min(), cols.max(), cols.max() - cols.min() + 1))
    f.close()
    print("wrote shot_stats.out.txt")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
