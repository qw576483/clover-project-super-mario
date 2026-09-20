#!/usr/bin/env python3
"""visual-diff -- side-by-side + deterministic pixel diff for the 1:1 loop.

Why this file exists (skill reference/visual-loop.md):
  "pixel-perfect is a property of the RENDERED output, not of the source".
  So the verdict must be a NUMBER produced by a deterministic diff -- never
  "I looked at it and it seems fine", and never an AI looking at pictures.
  AI may only triage/cluster what the diff found; the human makes the call.

Algorithm (equivalent to pixelmatch, per reference/deterministic-gates.md):
  per-pixel YIQ distance in YIQ space, threshold default 0.1 (= maxDelta 352.15).
  Anti-aliasing is NOT special-cased here (keep it honest: every pixel counts).

Usage:
  python tools/visual-diff.py <baseline.png> <ours.png> <out-dir> [--height 540]

Outputs into <out-dir>:
  side-by-side.png  baseline | ours at the same height (for human / multimodal review)
  diff-overlay.png  ours with differing pixels painted red
  delta.txt         the TODO list: diff ratio + per-column-band breakdown
"""

import sys
import os
from PIL import Image

MAX_DELTA = 35215.0  # full YIQ distance scale in pixelmatch


def yiq(px):
    r, g, b = px[0], px[1], px[2]
    y = 0.29889531 * r + 0.58662247 * g + 0.11448223 * b
    i = 0.59597799 * r - 0.27417610 * g - 0.32180189 * b
    q = 0.21147017 * r - 0.52261711 * g + 0.31114694 * b
    return y, i, q


def color_delta(a, b):
    ya, ia, qa = yiq(a)
    yb, ib, qb = yiq(b)
    dy, di, dq = ya - yb, ia - ib, qa - qb
    return 0.5053 * dy * dy + 0.299 * di * di + 0.1957 * dq * dq


def load_same_size(path, width, height):
    im = Image.open(path).convert("RGB")
    if im.size != (width, height):
        im = im.resize((width, height), Image.LANCZOS)
    return im


def main():
    args = [a for a in sys.argv[1:]]
    height = 540
    if "--height" in args:
        k = args.index("--height")
        height = int(args[k + 1])
        del args[k:k + 2]
    if len(args) != 3:
        print(__doc__)
        return 2
    base_path, ours_path, out_dir = args

    base0 = Image.open(base_path).convert("RGB")
    ours0 = Image.open(ours_path).convert("RGB")
    ratio = height / float(ours0.size[1])
    width = max(16, int(round(ours0.size[0] * ratio)))

    base = load_same_size(base_path, width, height)
    ours = load_same_size(ours_path, width, height)

    pb, po = base.load(), ours.load()
    overlay = ours.copy()
    pov = overlay.load()

    diff_count = 0
    band_hits = [0] * 8
    band_pixels = [0] * 8
    for y in range(height):
        for x in range(width):
            band = min(7, x * 8 // width)
            band_pixels[band] += 1
            if color_delta(pb[x, y], po[x, y]) > MAX_DELTA * 0.01:
                diff_count += 1
                band_hits[band] += 1
                pov[x, y] = (255, 0, 0)

    os.makedirs(out_dir, exist_ok=True)
    side = Image.new("RGB", (width * 2 + 8, height), (24, 24, 24))
    side.paste(base, (0, 0))
    side.paste(ours, (width + 8, 0))
    side.save(os.path.join(out_dir, "side-by-side.png"))
    overlay.save(os.path.join(out_dir, "diff-overlay.png"))

    total = float(width * height)
    ratio_pct = diff_count / total * 100.0
    lines = []
    lines.append("baseline : %s (%dx%d) -> normalized %dx%d" % (base_path, base0.size[0], base0.size[1], width, height))
    lines.append("ours     : %s (%dx%d)" % (ours_path, ours0.size[0], ours0.size[1]))
    lines.append("diff     : %d / %d pixels = %.3f%%  (verdict number)" % (diff_count, int(total), ratio_pct))
    lines.append("tolerance: 0.100%% (skill reference/visual-loop.md default maxDiffRatio <= 0.001)")
    lines.append("result   : %s" % ("PASS" if ratio_pct <= 0.1 else "FAIL -- feed the bands below back to the implementer"))
    lines.append("")
    lines.append("TODO (where the difference sits, left -> right):")
    for i in range(8):
        if band_hits[i] == 0:
            continue
        lines.append("  band %d/8  x[%4d..%4d)  %6d px  %.3f%% of band   <-- fix here"
                     % (i + 1, i * width // 8, (i + 1) * width // 8, band_hits[i],
                        band_hits[i] / float(band_pixels[i]) * 100.0))
    text = "\n".join(lines)
    with open(os.path.join(out_dir, "delta.txt"), "w", encoding="utf-8") as f:
        f.write(text + "\n")
    print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
