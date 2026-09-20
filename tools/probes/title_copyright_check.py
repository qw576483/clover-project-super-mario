# -*- coding: utf-8 -*-
"""标题屏"两处 ©"取证（离线、可复跑）：把三处的 © 放大并排，判"哪一处来自 logo 块"。

① `Resources/Sprites/Title/TitleLogo.png` 顶部 16 行 / 底部 20 行（8x）—— 块里到底带了什么；
② 实机帧 `t2_menu_run2.png` 的"金色 ©"带（= 压在 logo 底板下沿那个）与"白色 ©"带（= 我们自己
   的 `MainMenuPanel` Copy 标签），逐像素比"字形宽高比 / 颜色"；
③ 原版基线图 `策划/基线图/nes-original-title-screen.png` 的 © 行（6x）。
输出：`.ai-tmp/test/th-cmp-*.png`（并排图，人/多模态读）。
用法：python .ai-tmp/test/title_copyright_check.py
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, '.ai-tmp', 'test')
BASE = os.path.join(ROOT, '策划', '基线图', 'nes-original-title-screen.png')
LOGO = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Title', 'TitleLogo.png')
FRAME = os.path.join(ROOT, 'client', 'Assets', 'Screenshots', 't2_menu_run2.png')


def z(im, box, f):
    c = im.crop(box)
    return c.resize((c.width * f, c.height * f), Image.NEAREST)


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    base = Image.open(BASE).convert('RGB')
    logo = Image.open(LOGO).convert('RGB')
    frame = Image.open(FRAME).convert('RGB')

    tiles = [
        ('1-base-HUD+plate  (y0..25, 6x)', 'base', z(base, (0, 0, 256, 26), 6)),
        ('2-logo-top16      (8x)', 'logotop', z(logo, (0, 0, 207, 16), 8)),
        ('3-logo-bottom20   (8x)', 'logobot', z(logo, (0, 92, 207, 112), 8)),
        ('4-frame-gold-copy (y248..278, 4x)', 'goldcopy', z(frame, (330, 248, 790, 278), 4)),
        ('5-frame-white-copy(y276..296, 4x)', 'whitecopy', z(frame, (330, 276, 790, 296), 4)),
    ]
    for name, slug, t in tiles:
        p = os.path.join(OUT, 'th-cmp-%s.png' % slug)
        t.save(p)
        print('%-40s -> %s  %dx%d' % (name, os.path.basename(p), t.width, t.height))

    # 并排（等高拼一张：原版 HUD 行 vs 实机顶部带）
    a = z(base, (24, 8, 231, 15), 6)          # 原版 HUD 行 207x7
    b = z(frame, (322, 30, 774, 45), 3)       # 实机顶部带 452x15
    h = max(a.height, b.height)
    canvas = Image.new('RGB', (a.width + b.width + 12, h), (0, 0, 0))
    canvas.paste(a, (0, 0))
    canvas.paste(b, (a.width + 12, 0))
    canvas = canvas.resize((canvas.width, h * 3), Image.NEAREST)
    p = os.path.join(OUT, 'th-cmp-side.png')
    canvas.save(p)
    print('并排（左=原版标题屏顶部 HUD 行 6x，右=实机标题屏顶部带 3x）-> %s %dx%d'
          % (os.path.basename(p), canvas.width, canvas.height))
    return 0


if __name__ == '__main__':
    sys.exit(main())
