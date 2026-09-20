# -*- coding: utf-8 -*-
# One-off: predict the on-screen ink profile of the string "by clover-engine" at a given pixel size,
# straight from the FONT FILE (baseline = the font asset, skill 1.12 item 4 -- not our own product).
# Used to (a) pick the pixel criterion thresholds, (b) validate that the criterion discriminates.
import sys
from fontTools.ttLib import TTFont
from fontTools.pens.recordingPen import RecordingPen

TEXT = "by clover-engine"
SIZE = 16.0

FONTS = {
    "OLD uppercase-only": r"c:\Work\Server\full-dev\clover-project-super-mario\client\Assets\Resources\Fonts\SuperMarioNES.ttf",
    "NEW prstart (clone)": r"c:\Work\Server\full-dev\clover-project-super-mario\原版资源\参考工程\SMB-clone\Assets\Fonts\prstart.ttf",
}

for label, path in FONTS.items():
    f = TTFont(path, fontNumber=0)
    cmap = f.getBestCmap()
    gs = f.getGlyphSet()
    upm = f["head"].unitsPerEm
    scale = SIZE / upm
    print("=" * 96)
    print("%s  (%s)  unitsPerEm=%d  family=%s" % (label, path.split("\\")[-1], upm, f["name"].getDebugName(1)))
    rows = []
    for ch in TEXT:
        gname = cmap.get(ord(ch))
        if gname is None:
            rows.append((ch, None, None, None)); continue
        rp = RecordingPen(); gs[gname].draw(rp)
        xs, ys = [], []
        for op, args in rp.value:
            for p in args:
                if isinstance(p, tuple) and len(p) == 2:
                    xs.append(p[0]); ys.append(p[1])
        if not ys:
            rows.append((ch, gname, None, None)); continue
        ymin = round(min(ys) * scale); ymax = round(max(ys) * scale)
        # ink rows above the baseline: ymax..ymin (inclusive), height = ymax-ymin+1
        rows.append((ch, gname, ymin, ymax))
    print("  char  glyph            yMin(px) yMax(px)  height(px)")
    for ch, g, ymin, ymax in rows:
        h = (ymax - ymin + 1) if ymin is not None else None
        print("   %s     %-14s  %-7s  %-7s  %s" % ("' '" if ch == " " else " " + ch, g, ymin, ymax, h))
    hs = sorted((ymax - ymin + 1) for _, _, ymin, ymax in rows if ymin is not None)
    print("  glyph-run heights (px, sorted):", hs)
    if hs:
        hmax = max(hs)
        short = [h for h in hs if h <= hmax - 3]
        print("  predicted hMax=%d ; runs shorter than hMax-3: %d of %d" % (hmax, len(short), len(hs)))
        print("  => criterion 'count(runs with h <= hMax-3) >= 6' :", len(short) >= 6)
    # baseline consistency: how many runs sit on the modal bottom (yMin)?
    bots = [ymin for _, _, ymin, ymax in rows if ymin is not None]
    print("  glyph yMin values (baseline = 0):", bots)
