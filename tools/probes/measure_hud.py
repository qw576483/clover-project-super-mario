# -*- coding: utf-8 -*-
"""HUD 四栏列位置：原版首屏 vs 我们的实机帧（对照表 三、）。一次性量测脚本（用完即删，skill 1.8）。

判据：HUD 带内把**白墨迹**按列聚类 → 得到四栏的归一化 左/中/右 区间。
  · 原版侧 = `策划/基线图/nes-original-1-1-first-screen.png`（256×240，HUD 带 = 顶部 34 px）
  · 我方侧 = `client/Assets/Screenshots/final_stage.png`（1096×500，HUD 带 = 顶部 130 px）

⚠️ 为什么这一项**不自动判 PASS**：把"字号的相对屏高"和"列的 x 分数"同时对齐需要**同画幅**，
   而本工程是 16:9、原版是 4:3 ⇒ 两项只能取其一（本工程取列分数）。另外天空里的**白云**也是白的，
   会污染"白墨迹聚类"（我方那一侧实测多出 1~2 个簇）⇒ 这项按 HUMAN-ONLY 上报，脚本只提供数字。
"""
import io
import os

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ORIG = os.path.join(ROOT, '策划', '基线图', 'nes-original-1-1-first-screen.png')
OURS = os.path.join(ROOT, 'client', 'Assets', 'Screenshots', 'final_stage.png')


def clusters(path, ymax):
    a = np.array(Image.open(path).convert('RGB'), dtype=np.int16)
    h, w = a.shape[:2]
    band = a[:ymax, :]
    r, g, b = band[:, :, 0], band[:, :, 1], band[:, :, 2]
    ink = (r > 200) & (g > 200) & (b > 200)
    n = ink.sum(axis=0)
    runs, s = [], None
    for x in range(w):
        on = n[x] > 0
        if on and s is None:
            s = x
        elif not on and s is not None:
            runs.append((s, x - 1))
            s = None
    if s is not None:
        runs.append((s, w - 1))
    merged = []
    for a1, b1 in runs:                      # 同一栏内的字母间隔 < 6px，合并
        if merged and a1 - merged[-1][1] <= 6:
            merged[-1] = (merged[-1][0], b1)
        else:
            merged.append((a1, b1))
    return w, h, [c for c in merged if c[1] - c[0] >= 3]


print(u'原版首屏 %s' % os.path.relpath(ORIG, ROOT))
w, h, cl = clusters(ORIG, 34)
print(u'  尺寸 %dx%d  HUD 白墨迹簇 = %s' % (w, h, cl))
for a, b in cl:
    print(u'    归一化 左 %.4f  中 %.4f  右 %.4f' % (a / w, (a + b) / 2.0 / w, b / w))

print(u'我方实机帧 %s' % os.path.relpath(OURS, ROOT))
w2, h2, cl2 = clusters(OURS, 130)
print(u'  尺寸 %dx%d  HUD 白墨迹簇 = %s（⚠️ 天空里的白云也会被算进来）' % (w2, h2, cl2))
for a, b in cl2:
    print(u'    归一化 左 %.4f  中 %.4f  右 %.4f' % (a / w2, (a + b) / 2.0 / w2, b / w2))

print(u'')
print(u'我们的**设计锚点**（HudPanel.cs，= 原版那四个区间的归一化值）：'
      u'0.09375 / 0.34766 / 0.63672（居中）/ 0.91016（右对齐）')
print(u'== 结论：列位置按分数对齐（设计基准即上列原版区间）；像素级复核 HUMAN-ONLY（16:9 vs 4:3 画幅差异，见验收表 #5 / E-22）==')
