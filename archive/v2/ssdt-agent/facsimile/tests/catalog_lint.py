#!/usr/bin/env python3
"""Catalog lint for facsimile/cases: grammar, references, and traceability coverage.

A prototype of `tests/run-cases.ps1 --lint`, which replaces it at gate G1 (ARCHITECTURE.md §10).
It parses every case in the grammar of cases/README.md and refuses:
  - a heading or field outside the grammar, a duplicate ID, a missing required field;
  - an unknown law, a case citing no law, a source that is not receipt|expected|verify, a receipt
    without a citation;
and reports coverage gaps:
  - a law with fewer than two cases;
  - an operation missing a standard situation without a `note: n/a <tag>` excuse;
  - a question kind with no corpus case, a stop kind with no case;
  - a Twin responsibility row (ARCHITECTURE.md §2) no TWN case names as `row <n>`.
Exit 1 on any error or gap.

Usage: python3 catalog_lint.py <cases-dir> [--matrix <out.md>]
"""
import re, sys, os, collections, glob

LAWS = {f"F{i}" for i in range(1, 10)} | {f"H{i}" for i in range(1, 8)} | {f"P{i}" for i in range(1, 7)} \
     | {f"S{i}" for i in range(1, 7)} | {f"D{i}" for i in range(1, 7)} | {f"M{i}" for i in range(1, 5)} \
     | {f"E{i}" for i in range(1, 5)} | {f"A{i}" for i in range(1, 6)} | {f"T{i}" for i in range(1, 5)}
KINDS = {"pure", "sql", "corpus", "process", "manual"}
REQUIRED = {
    "pure": ["kind", "expect", "laws", "source"],
    "sql": ["kind", "expect", "laws", "source"],
    "corpus": ["kind", "env", "change", "expect", "laws", "source"],
    "process": ["kind", "expect", "laws", "source"],
    "manual": ["kind", "expect", "laws", "source"],
}
FIELDS = {"kind", "tags", "input", "env", "change", "setup", "action", "expect", "laws", "source", "note"}
QUESTION_KINDS = ["HasRows", "NotNull", "Fits", "Unique", "Orphan", "CheckFalse", "KeyLength", "Rebuild",
                  "Computed", "Temporal", "Scan", "Authored", "SeedDrift", "Unknown"]
STOPS = ["unreachable", "no-access", "missing-object", "stale-answers", "authored-script", "check-witness",
         "computed-witness", "planter-needs-row", "acceptance", "identity", "in-flight", "config", "toolchain",
         "platform", "options-unknown", "build-failed", "unknown-statement", "unclassified", "cdc",
         "base-unpublishable", "usage", "probe-failed", "answers-invalid", "witness-failed",
         "witness-not-synthetic", "identifier-unsafe", "interrupted", "internal"]
OPS = ["add-check", "add-default", "add-index", "add-mandatory", "add-optional", "add-unique", "archive-entity",
       "audit-columns", "change-delete-rule", "create-entity", "create-fk-clean", "create-fk-orphan",
       "create-static-seed", "define-pk", "delete-attribute", "delete-entity", "delete-seed-value", "drop-check",
       "drop-default", "drop-fk", "drop-index", "drop-pk", "drop-unique", "edit-seed", "extract-to-lookup",
       "identity-swap", "junction", "make-mandatory", "make-optional", "merge-tables", "modify-default",
       "modify-index", "move-attribute", "move-schema", "narrow", "rebuild-index", "rename-attribute",
       "rename-entity", "retype-explicit", "retype-implicit", "split-table", "temporal-convert", "temporal-new",
       "toggle-trust", "widen"]

HEAD = re.compile(r"^### +([A-Za-z0-9][A-Za-z0-9\-]*) +· +(.+?)\s*$")
FIELD = re.compile(r"^([a-z]+):\s?(.*)$")


def parse(path):
    cases, errors = [], []
    lines = open(path, encoding="utf-8").read().splitlines()
    i = 0
    while i < len(lines):
        m = HEAD.match(lines[i])
        if not m:
            if lines[i].startswith("### "):
                errors.append(f"{os.path.basename(path)}:{i+1}: heading not in grammar: {lines[i][:80]}")
            i += 1
            continue
        cid, title, line_no = m.group(1), m.group(2), i + 1
        j = i + 1
        while j < len(lines) and lines[j].strip() == "":
            j += 1
        if j >= len(lines) or lines[j].strip() != "```case":
            errors.append(f"{os.path.basename(path)}:{line_no}: {cid}: no ```case block after heading")
            i += 1
            continue
        k = j + 1
        body = []
        while k < len(lines) and lines[k].strip() != "```":
            body.append(lines[k])
            k += 1
        fields, cur = {}, None
        for b in body:
            fm = FIELD.match(b)
            if fm and not b.startswith(" ") and fm.group(1) in FIELDS:
                cur = fm.group(1)
                if cur in fields:
                    errors.append(f"{os.path.basename(path)}:{line_no}: {cid}: duplicate field {cur}")
                fields[cur] = fm.group(2).strip()
            elif fm and not b.startswith(" "):
                errors.append(f"{os.path.basename(path)}:{line_no}: {cid}: unknown field '{fm.group(1)}'")
                cur = None
            elif cur and (b.startswith(" ") or b.strip() == ""):
                fields[cur] += "\n" + b.strip()
            elif b.strip():
                errors.append(f"{os.path.basename(path)}:{line_no}: {cid}: stray line: {b[:60]}")
        cases.append({"id": cid, "title": title, "file": os.path.basename(path), "line": line_no, **fields})
        i = k + 1
    return cases, errors


def main():
    d = sys.argv[1]
    out = sys.argv[sys.argv.index("--matrix") + 1] if "--matrix" in sys.argv else None
    files = sorted(f for f in glob.glob(os.path.join(d, "*.md")) if not os.path.basename(f).startswith(("README", "90-")))
    cases, errors = [], []
    for f in files:
        c, e = parse(f)
        cases += c
        errors += e
    ids = collections.Counter(c["id"] for c in cases)
    for cid, n in ids.items():
        if n > 1:
            where = [f"{c['file']}:{c['line']}" for c in cases if c["id"] == cid]
            errors.append(f"duplicate id {cid}: {', '.join(where)}")
    law_cases = collections.defaultdict(list)
    src_counts = collections.Counter()
    kind_counts = collections.Counter()
    for c in cases:
        k = c.get("kind", "").split()[0] if c.get("kind") else ""
        kind_counts[k] += 1
        if k not in KINDS:
            errors.append(f"{c['file']}:{c['line']}: {c['id']}: bad kind '{c.get('kind')}'")
        else:
            for req in REQUIRED[k]:
                if not c.get(req):
                    errors.append(f"{c['file']}:{c['line']}: {c['id']}: missing field '{req}' for kind {k}")
            if k == "corpus" and "outcome" not in c.get("expect", ""):
                errors.append(f"{c['file']}:{c['line']}: {c['id']}: corpus case without an expect outcome")
        laws = re.findall(r"\b([FHPSDMEAT]\d)\b", c.get("laws", ""))
        if not laws:
            errors.append(f"{c['file']}:{c['line']}: {c['id']}: no law cited")
        for l in laws:
            if l not in LAWS:
                errors.append(f"{c['file']}:{c['line']}: {c['id']}: unknown law {l}")
            else:
                law_cases[l].append(c["id"])
        src = c.get("source", "")
        marker = src.split()[0].strip("·").lower() if src else ""
        if marker not in ("receipt", "expected", "verify"):
            errors.append(f"{c['file']}:{c['line']}: {c['id']}: source marker '{marker}' not receipt|expected|verify")
        else:
            src_counts[marker] += 1
            rest = src[len(marker):].strip(" ·")
            if marker == "receipt" and len(rest) < 4:
                errors.append(f"{c['file']}:{c['line']}: {c['id']}: receipt without citation")
    # coverage
    gaps = []
    for l in sorted(LAWS, key=lambda x: (x[0], int(x[1:]))):
        if len(set(law_cases[l])) < 2:
            gaps.append(f"law {l}: {len(set(law_cases[l]))} case(s)")
    op_tags = collections.defaultdict(set)
    op_cases = collections.defaultdict(list)
    for c in cases:
        m = re.match(r"OP-(.+)-\d+$", c["id"])
        if m:
            op = m.group(1)
            op_cases[op].append(c["id"])
            for t in re.split(r"[\s,·]+", c.get("tags", "")):
                if t:
                    op_tags[op].add(t.split(":")[0])
            for t in re.findall(r"n/a\s+([a-z]+)", c.get("note", "")):
                op_tags[op].add(t)
    for op in OPS:
        if op not in op_cases:
            gaps.append(f"op {op}: no cases")
            continue
        need = {"empty", "clean", "violating", "remedy", "edge"}
        missing = need - op_tags[op]
        if missing:
            gaps.append(f"op {op}: missing tags {sorted(missing)} ({len(op_cases[op])} cases)")
    for op in op_cases:
        if op not in OPS:
            errors.append(f"op cases for unknown op slug '{op}'")
    q_corpus = collections.defaultdict(list)
    for c in cases:
        if c.get("kind", "").startswith("corpus"):
            qs = c.get("expect", "")
            for q in QUESTION_KINDS:
                if re.search(rf"\b{q}\b", qs):
                    q_corpus[q].append(c["id"])
    for q in QUESTION_KINDS:
        if not q_corpus[q] and q not in ("Unknown",):
            gaps.append(f"question kind {q}: no corpus case")
    stop_cases = collections.defaultdict(list)
    for c in cases:
        for s in STOPS:
            if re.search(rf"stop\s+{re.escape(s)}\b", c.get("expect", "")):
                stop_cases[s].append(c["id"])
    for s in STOPS:
        if not stop_cases[s]:
            gaps.append(f"stop {s}: no case")
    twn_rows = collections.defaultdict(list)
    for c in cases:
        if c["id"].startswith("TWN"):
            text = c["title"] + " " + " ".join(str(v) for v in c.values())
            for n in re.findall(r"(?:row|#)\s?(\d{1,2})\b", text):
                if 1 <= int(n) <= 36:
                    twn_rows[int(n)].append(c["id"])
    for r in range(1, 37):
        if not twn_rows[r]:
            gaps.append(f"twin responsibility row {r}: no TWN case cites it")
    # report
    per_file = collections.Counter(c["file"] for c in cases)
    print(f"cases: {len(cases)}  files: {len(files)}")
    for f, n in sorted(per_file.items()):
        print(f"  {f}: {n}")
    print("kinds:", dict(kind_counts))
    print("sources:", dict(src_counts))
    print(f"errors: {len(errors)}")
    for e in errors[:200]:
        print("  E", e)
    print(f"coverage gaps: {len(gaps)}")
    for g in gaps:
        print("  G", g)
    if out:
        with open(out, "w", encoding="utf-8") as fh:
            fh.write("## Law → cases\n\n| Law | Cases | IDs |\n|---|---:|---|\n")
            for l in sorted(LAWS, key=lambda x: ("FHPSDMEAT".index(x[0]), int(x[1:]))):
                ids_ = sorted(set(law_cases[l]))
                fh.write(f"| {l} | {len(ids_)} | {' '.join(ids_[:40])}{' …' if len(ids_) > 40 else ''} |\n")
            fh.write("\n## Operation → cases (standard set)\n\n| Operation | Cases | Tags present |\n|---|---:|---|\n")
            for op in OPS:
                fh.write(f"| {op} | {len(op_cases[op])} | {' '.join(sorted(op_tags[op]))} |\n")
            fh.write("\n## Question kind → corpus cases\n\n| Kind | Corpus cases |\n|---|---|\n")
            for q in QUESTION_KINDS:
                ids_ = sorted(set(q_corpus[q]))
                fh.write(f"| {q} | {len(ids_)}: {' '.join(ids_[:25])}{' …' if len(ids_) > 25 else ''} |\n")
            fh.write("\n## Stop → cases\n\n| Stop | Cases |\n|---|---|\n")
            for s in STOPS:
                ids_ = sorted(set(stop_cases[s]))
                fh.write(f"| {s} | {len(ids_)}: {' '.join(ids_[:20])}{' …' if len(ids_) > 20 else ''} |\n")
            fh.write("\n## Twin responsibility (ARCH §2) → cases\n\n| Row | Cases |\n|---:|---|\n")
            for r in range(1, 37):
                ids_ = sorted(set(twn_rows[r]))
                fh.write(f"| {r} | {' '.join(ids_)} |\n")
    sys.exit(1 if errors or gaps else 0)


if __name__ == "__main__":
    main()
