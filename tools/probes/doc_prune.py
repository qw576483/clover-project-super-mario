# -*- coding: utf-8 -*-
"""一次性：把 5 个已消除的登记项从「允许的差异」表里删掉（它们已搬进「已消除的登记项」表）。

只删「允许的差异」小节里以 `| E-4 |`…`| E-16 |` 开头的行；其余一行不动。
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
ROOT = r"clover-project-super-mario"
P = os.path.join(ROOT, u'策划', u'验收表.md')
DROP = {u'E-4', u'E-9', u'E-13', u'E-15', u'E-16'}

text = io.open(P, encoding='utf-8').read()
lines = text.split('\n')
# 定位「允许的差异」小节
start = next(i for i, l in enumerate(lines) if l.startswith(u'## ') and u'\u5141\u8bb8\u7684\u5dee\u5f02' in l)
kept, dropped = [], []
for i, l in enumerate(lines):
    if i > start:
        m = re.match(r'^\|\s*(E-\d+)\s*\|', l)
        if m and m.group(1) in DROP:
            dropped.append(m.group(1))
            continue
    kept.append(l)
io.open(P, 'w', encoding='utf-8', newline='\n').write('\n'.join(kept))
print(u'删除 %d 行：%s' % (len(dropped), dropped))
