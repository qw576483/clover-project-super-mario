# -*- coding: utf-8 -*-
"""标题屏 HUD 判据（离线、可复跑）：原版基线图上到底有没有 HUD？

判据链（判定权交给参考物，不靠"我觉得"）：
 ① `策划/基线图/nes-original-title-screen.png`（原版标题屏，256x224）逐行扫"非背景像素"，
    把屏幕分成若干"内容带"并打印每条带的 x/y 范围 —— 顶部那条带是不是 HUD？
 ② 工程里的 `Resources/Sprites/Title/TitleLogo.png`（207x112，由
    `原版资源/解析/脚本/title_logo_crop.py` 从①裁出）逐行扫同样的事 ——
    裁出来的块**自带**了哪些带？（⇒ 标题屏上看到的"HUD"是它带来的还是活面板画的）
 ③ 实机帧 `t2_menu_run2.png`（1096x500）：找顶部文字带与两处 ©，打印 x 范围与颜色 ——
    与 ② 的几何对得上就说明来源是 ②，而不是 HudPanel。

用法：python .ai-tmp/test/title_hud_check.py
输出：正文 + 放大图（6x NEAREST）落在 .ai-tmp/test/ 下（供人/多模态读）。
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, '.ai-tmp', 'test')
BG_TOL = 24
BASE = os.path.join(ROOT, '策划', '基线图', 'nes-original-title-screen.png')
RAWSRC = os.path.join(ROOT, '原版资源', '导出的png', 'nes-original-title-screen.png')
LOGO = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Title', 'TitleLogo.png')
FRAME = os.path.join(ROOT, 'client', 'Assets', 'Screenshots', 't2_menu_run2.png')


def load(p):
    im = Image.open(p).convert('RGB')
    return im, im.load(), im.size


def bg_of(im):
    counts = {}
    for c in im.getdata():
        counts[c] = counts.get(c, 0) + 1
    return max(counts.items(), key=lambda kv: kv[1])[0]


def bands(im, px, w, h, bg):
    """返回 [(y0,y1,x0,x1,非背景像素数)]：连续"有内容"的行段。"""
    def is_bg(c):
        return all(abs(c[i] - bg[i]) <= BG_TOL for i in range(3))

    rows = []
    for y in range(h):
        xs = [x for x in range(w) if not is_bg(px[x, y])]
        rows.append((len(xs), (min(xs), max(xs)) if xs else None))
    out, cur = [], None
    for y, (n, span) in enumerate(rows):
        if n > 0:
            if cur is None:
                cur = [y, y, span[0], span[1]]
            else:
                cur[1] = y
                cur[2] = min(cur[2], span[0])
                cur[3] = max(cur[3], span[1])
        elif cur is not None:
            out.append(tuple(cur))
            cur = None
    if cur is not None:
        out.append(tuple(cur))
    return out


def zoom(im, box, factor, name):
    c = im.crop(box)
    c = c.resize((c.width * factor, c.height * factor), Image.NEAREST)
    p = os.path.join(OUT, name)
    c.save(p)
    return p


def report(tag, path):
    im, px, (w, h) = load(path)
    bg = bg_of(im)
    print('%s  %s  %dx%d  背景 RGB%s' % (tag, os.path.basename(path), w, h, bg))
    bs = bands(im, px, w, h, bg)
    for (y0, y1, x0, x1) in bs:
        print('   band y=%3d..%3d (高%2d)  x=%3d..%3d (宽%3d)'
              % (y0, y1, y1 - y0 + 1, x0, x1, x1 - x0 + 1))
    return im, px, w, h, bg, bs


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    print('=== ① 原版基线图（策划/基线图）===')
    im, px, w, h, bg, bs = report('基线图', BASE)
    print('  基线图：顶部第一条带 y=%d..%d —— 放大图 %s'
          % (bs[0][0], bs[0][1], os.path.basename(zoom(im, (0, 0, w, min(26, h)), 6, 'th-base-top.png'))))
    print('=== ①b 同一份图的另一副本（原版资源/导出的png）===')
    im2, _, _ = load(RAWSRC)
    same = im2.size == (w, h) and list(im2.getdata()) == list(im.getdata())
    print('  与原版基线图逐像素相同 = %s（尺寸 %s）' % (same, im2.size))

    print('=== ② 工程里的 logo 块（由①裁出，207x112）===')
    lim, lpx, lw, lh, lbg, lbs = report('TitleLogo', LOGO)
    for (y0, y1, x0, x1) in lbs:
        print('   -> 放大 %s' % os.path.basename(
            zoom(lim, (0, max(0, y0 - 2), lw, min(lh, y1 + 3)), 8, 'th-logo-y%d.png' % y0)))

    print('=== ③ 实机帧 t2_menu_run2.png ===')
    f, fpx, fw, fh, fbg, fbs = report('实机帧', FRAME)
    for (y0, y1, x0, x1) in fbs[:6]:
        print('   -> 放大 %s' % os.path.basename(
            zoom(f, (max(0, x0 - 8), max(0, y0 - 6), min(fw, x1 + 8), min(fh, y1 + 6)), 3,
                 'th-frame-y%d.png' % y0)))

    print('=== 判据计算：顶部带的宽/相对位置 ===')
    if bs and fbs:
        bw = bs[0][3] - bs[0][2] + 1
        fw0 = fbs[0][3] - fbs[0][2] + 1
        print('  原版顶部带宽 %d px（占屏宽 %.3f）' % (bw, bw / float(w)))
        print('  实机顶部带宽 %d px（占屏宽 %.3f）' % (fw0, fw0 / float(fw)))
        print('  实机 logo 块带（y=%d..%d）宽 %d px' %
              (fbs[1][0], fbs[1][1], fbs[1][3] - fbs[1][2] + 1))
    print('---- 判据结论：原版标题屏顶部是否有一条独立的"内容带"(HUD) = %s'
          % ('是' if bs and bs[0][0] < 20 else '否'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
