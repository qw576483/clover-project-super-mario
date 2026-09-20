# -*- coding: utf-8 -*-
"""Delivery self-check (the two items tools/verify.ps1 does NOT cover, SKILL.md 1.11):

  1) "\u5f15\u7528\u8def\u5f84\u53ef\u8fbe"  -- every concrete file path cited by the delivery docs resolves on disk
     (a citation that points at nothing is worse than no citation).
  2) "\u9a8c\u6536\u8868\u96f6\u5f15\u7528\u96f6\u7a7a\u884c" -- every acceptance row's CONCLUSION cell is filled and
     contains neither "\u4e0d\u4e00\u81f4" nor "\u5f85\u9a8c / \u672a\u9a8c" (a row may carry the word inside its EVIDENCE
     text -- e.g. "\u4e0e\u6d77\u62a5\u540c\u683c\u4e0d\u4e00\u81f4 = 0" -- so only the conclusion column is judged).
     Plus: no blank line inside a table body (a blank line silently breaks the table in most renderers).

Exit code 0 = all PASS.
"""
import io, os, re, sys, glob

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
PLAN = chr(0x7B56) + chr(0x5212)
SPEC = os.path.join(ROOT, PLAN, chr(0x9A8C) + chr(0x6536) + chr(0x8868) + ".md")
REF = os.path.join(ROOT, PLAN, chr(0x5BF9) + chr(0x7167) + chr(0x8868) + ".md")
fails = []

# ---------- 1) cited paths resolve ----------
CITE = re.compile(r"((?:client|tools|\u539f\u7248\u8d44\u6e90|\u7b56\u5212|\.ai-tmp)/[A-Za-z0-9_./\u4e00-\u9fff\-]*"
                  r"\.(?:cs|py|ps1|md|txt|png|tsv|jpg|mp3|wav|ttf|prefab|unity|asset|gif|meta|json))")
seen = {}
for doc in (SPEC, REF):
    txt = io.open(doc, encoding="utf-8", errors="replace").read()
    for m in CITE.finditer(txt):
        p = m.group(1)
        seen.setdefault(p, 0)
        seen[p] += 1
missing = []
for p, n in sorted(seen.items()):
    full = os.path.join(ROOT, p.replace("/", os.sep))
    if not os.path.exists(full):
        # a path written relative to another root (e.g. "\u7b56\u5212/\u57fa\u7ebf\u56fe/..") is fine if its
        # basename exists somewhere under the project -- those are doc-relative mentions.
        hits = glob.glob(os.path.join(ROOT, "**", os.path.basename(p)), recursive=True)
        if not hits:
            missing.append((p, n))
print("[1] cited paths: %d distinct, %d unresolvable" % (len(seen), len(missing)))
for p, n in missing:
    print("    MISSING x%d  %s" % (n, p))
if missing:
    fails.append("cited-path")

# ---------- 2) acceptance table: conclusions filled, no "\u4e0d\u4e00\u81f4", no blank line in a table ----------
rows = 0
bad = []
blank_in_table = []
lines = io.open(SPEC, encoding="utf-8", errors="replace").read().split("\n")
BADW = [chr(0x4E0D) + chr(0x4E00) + chr(0x81F4),           # bu yi zhi
        chr(0x5F85) + chr(0x9A8C), chr(0x672A) + chr(0x9A8C), chr(0x5F85) + chr(0x8865)]   # dai yan / wei yan / dai bu
for i, ln in enumerate(lines):
    # A blank line between two table rows is an in-table blank -> breaks the table. A blank line
    # followed by a *new header* row (its next line is a |---| separator) is a legitimate table
    # separator -- two different tables must not be merged into one.
    if ln.strip() == "" and 0 < i < len(lines) - 1 and lines[i - 1].rstrip().startswith("|") \
       and lines[i + 1].strip().startswith("|"):
        nxt = lines[i + 2].strip() if i + 2 < len(lines) else ""
        if not re.match(r"^\|[\s\-:|]+\|$", nxt):
            blank_in_table.append(i + 1)
    m = re.match(r"^\|\s*(\d+|\d+-\d+)\s*\|", ln)
    if not m:
        continue
    cells = [c.strip() for c in ln.strip().strip("|").split("|")]
    if not any(c in (chr(0x4E00) + chr(0x81F4), chr(0x4E0D) + chr(0x9002) + chr(0x7528),
                     chr(0x672A) + chr(0x9A8C), chr(0x672A) + chr(0x505A)) for c in cells):
        continue
    rows += 1
    concl = cells[-2] if len(cells) >= 2 else ""
    cls = cells[-1]
    if concl.strip() == "":
        bad.append("row %s: EMPTY conclusion" % m.group(1))
    for w in BADW:
        if w in concl:
            bad.append("row %s: conclusion carries '%s' -> %s" % (m.group(1), w, concl[:60]))
    if chr(0x6570) + chr(0x503C) + chr(0x7C7B) not in cls and chr(0x8868) + chr(0x73B0) + chr(0x7C7B) not in cls:
        bad.append("row %s: no evidence class in %r" % (m.group(1), cls))
print("[2] acceptance rows judged: %d ; problems: %d ; blank lines inside a table: %d"
      % (rows, len(bad), len(blank_in_table)))
for b in bad:
    print("    " + b)
for i in blank_in_table:
    print("    blank line inside a table at line %d" % i)
if bad:
    fails.append("conclusion")
if blank_in_table:
    fails.append("blank-line")

print("===== selfcheck: %s =====" % ("FAIL " + ",".join(fails) if fails else "PASS"))
sys.exit(1 if fails else 0)
