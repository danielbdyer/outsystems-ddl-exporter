#!/usr/bin/env python3
"""ssdt-agent truth gates — citations | register | mirror | all.

The CI face of the ssdt-agent tree's ratchet rule (ENABLEMENT_PROGRAM.md §5:
no fix lands without the detector that would have caught its absence). Three
gates, each independently runnable; exit 0 = clean, exit 1 = findings (each
finding printed as `file: finding`).

  citations  every cross-reference resolves:
             R1 relative backticked paths (../x/y.md, trailing / allowed)
             R2 bare backticked *.md basenames exist under the tree, the
                curriculum (handbook/ + ssdt-playbook/), or sidecar/projection/
             R3 scenario ids cited by the rubrics exist in the prompt files
             R4 skills/op/<slug> <-> sample-prs/<slug>.md stay 1:1
  register   THE_RECORD.md §7's unambiguous banned tokens stay out of the
             record surfaces (sample-prs/*.md, golden/*-pr.md, and each op
             skill's "On the record" fragment); golden PRs stay agentless
             (no first/second person outside code fences)
  mirror     .github/PULL_REQUEST_TEMPLATE/schema-change.md carries every
             canonical author-pr section (the template's own precedence note)

Run from anywhere; paths are resolved from this file's location.
"""

import os
import re
import sys
import glob

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(HERE)                       # sidecar/projection
REPO = os.path.dirname(os.path.dirname(PROJ))      # repo root
TREE = os.path.join(PROJ, "ssdt-agent")

findings = []


def find(path, msg):
    findings.append(f"{os.path.relpath(path, REPO)}: {msg}")


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def strip_fences(text):
    return re.sub(r"```.*?```", "", text, flags=re.S)


# --------------------------------------------------------------------------
# citations
# --------------------------------------------------------------------------

REL_PATH = re.compile(r"`((?:\.\./)+[\w./ -]+?(?:\.(?:md|sql|xml|json|refactorlog)|/))`")
BARE_MD = re.compile(r"`([A-Za-z0-9][\w. -]*?\.md)`")
SCENARIO_ID = re.compile(r"\b(REV|COL|TBL|KEY|IDX|CON|STA|STR|AUD|IDEM)-\d{2}[A-Z]?\b")


def gate_citations():
    tree_md = sorted(glob.glob(os.path.join(TREE, "**", "*.md"), recursive=True))

    # R1 — relative backticked paths resolve
    for p in tree_md:
        d = os.path.dirname(p)
        for m in REL_PATH.finditer(read(p)):
            target = os.path.normpath(os.path.join(d, m.group(1)))
            if not os.path.exists(target):
                find(p, f"relative path does not resolve: `{m.group(1)}`")

    # R2 — bare .md basenames exist somewhere legitimate
    known = set()
    for root in (TREE, os.path.join(REPO, "handbook"), os.path.join(REPO, "ssdt-playbook")):
        for f in glob.glob(os.path.join(root, "**", "*.md"), recursive=True):
            known.add(os.path.basename(f))
    for f in glob.glob(os.path.join(PROJ, "*.md")):   # sidecar/projection top level (THE_TWIN.md etc.)
        known.add(os.path.basename(f))
    for p in tree_md:
        for m in BARE_MD.finditer(read(p)):
            name = m.group(1)
            if "/" in name or "<" in name or "*" in name:
                continue
            if name not in known:
                find(p, f"cited filename exists nowhere (tree, handbook/, ssdt-playbook/, sidecar/projection/): `{name}`")

    # R3 — scenario ids cited by rubrics exist in the prompt files
    prompts = read(os.path.join(TREE, "self-test", "prompts.md"))
    review_prompts = read(os.path.join(TREE, "self-test", "review-prompts.md"))
    defined = set(SCENARIO_ID.findall(prompts + review_prompts) and
                  [m.group(0) for m in SCENARIO_ID.finditer(prompts + review_prompts)])
    for rubric in ("rubric.md", "review-rubric.md", "PROTOCOL.md"):
        p = os.path.join(TREE, "self-test", rubric)
        for m in SCENARIO_ID.finditer(read(p)):
            if m.group(0) not in defined:
                find(p, f"scenario id cited but defined nowhere in prompts: {m.group(0)}")
    # the shorthand "REV-01/06" style hides a second id behind a slash
    for rubric in ("rubric.md", "review-rubric.md"):
        p = os.path.join(TREE, "self-test", rubric)
        for m in re.finditer(r"\b(REV|COL)-(\d{2}[A-Z]?)/(\d{2}[A-Z]?)\b", read(p)):
            for suffix in (m.group(2), m.group(3)):
                sid = f"{m.group(1)}-{suffix}"
                if sid not in defined:
                    find(p, f"scenario id cited (slash shorthand) but defined nowhere: {sid}")

    # R4 — op catalog and sample-prs stay 1:1
    ops = {os.path.basename(os.path.dirname(p))
           for p in glob.glob(os.path.join(TREE, "skills", "op", "*", "SKILL.md"))}
    prs = {os.path.splitext(os.path.basename(p))[0]
           for p in glob.glob(os.path.join(TREE, "sample-prs", "*.md"))} - {"README"}
    for slug in sorted(ops - prs):
        find(os.path.join(TREE, "skills", "op", slug), "op has no sample-prs worked example")
    for slug in sorted(prs - ops):
        find(os.path.join(TREE, "sample-prs", slug + ".md"), "sample PR has no owning op skill")


# --------------------------------------------------------------------------
# register
# --------------------------------------------------------------------------

BANNED = [
    (re.compile(r"\bMechanism [0-9]\b"), "numbered axis `Mechanism N`"),
    (re.compile(r"\bTier [0-9]\b"), "numbered axis `Tier N`"),
    (re.compile(r"\bblast radius\b", re.I), "banned nickname `blast radius`"),
    (re.compile(r"\bmagic line\b", re.I), "banned nickname `magic line`"),
    (re.compile(r"\bthe spine\b", re.I), "banned nickname `the spine`"),
    (re.compile(r"\bthe graduation\b", re.I), "banned nickname `the graduation`"),
    (re.compile(r"\bthe oracle\b", re.I), "banned nickname `the oracle`"),
    (re.compile(r"\bthe corpse\b", re.I), "banned nickname `the corpse`"),
    (re.compile(r"\bNaked Rename\b"), "retired trap name `Naked Rename` (say: a rename with no refactorlog entry)"),
    (re.compile(r"\bBLESS\b"), "ceremony verb `BLESS`"),
    (re.compile(r"\bHAND-BACK\b"), "ceremony verb `HAND-BACK`"),
    (re.compile(r"\bREFUSE-ESCALATE\b"), "ceremony verb `REFUSE-ESCALATE`"),
    (re.compile(r"catastroph", re.I), "drama (`catastrophe`)"),
    (re.compile(r"\bproving ground\b", re.I), "`proving ground` in a record (say: a disposable copy of Dev / the Twin)"),
]

PERSON = re.compile(r"\b([Ii]|[Ww]e|[Yy]ou|[Yy]our)\b")


def record_surfaces():
    for p in sorted(glob.glob(os.path.join(TREE, "sample-prs", "*.md"))):
        if os.path.basename(p) != "README.md":
            yield p, read(p)
    for p in sorted(glob.glob(os.path.join(TREE, "self-test", "golden", "*-pr.md"))):
        yield p, read(p)
    for p in sorted(glob.glob(os.path.join(TREE, "skills", "op", "*", "SKILL.md"))):
        text = read(p)
        m = re.search(r"^## On the record.*", text, re.M | re.S)
        if m:
            yield p, m.group(0)


def gate_register():
    for p, text in record_surfaces():
        scan = strip_fences(text)
        for pat, label in BANNED:
            m = pat.search(scan)
            if m:
                find(p, f"record register: {label} (`…{scan[max(0, m.start()-30):m.end()+20].strip()}…`)")
    for p in sorted(glob.glob(os.path.join(TREE, "self-test", "golden", "*-pr.md"))):
        scan = strip_fences(read(p))
        m = PERSON.search(scan)
        if m:
            find(p, f"golden PR is not agentless: `{m.group(0)}` at `…{scan[max(0, m.start()-40):m.end()+30].strip()}…`")


# --------------------------------------------------------------------------
# mirror
# --------------------------------------------------------------------------

CANONICAL_SECTIONS = [
    "Summary", "Review & release", "Changes", "Data remediation",
    "Deployment evidence", "Verification", "Rollback", "Not verified",
]


def gate_mirror():
    template = os.path.join(REPO, ".github", "PULL_REQUEST_TEMPLATE", "schema-change.md")
    if not os.path.exists(template):
        find(template, "schema-change PR template is missing")
        return
    heads = [h.strip() for h in re.findall(r"^## (.+)$", read(template), re.M)]
    for section in CANONICAL_SECTIONS:
        if not any(h == section or h.startswith(section + " ") or h.startswith(section + " —") for h in heads):
            find(template, f"template lost the canonical section `{section}` (author-pr/SKILL.md is the source of truth)")


# --------------------------------------------------------------------------

def main():
    which = sys.argv[1] if len(sys.argv) > 1 else "all"
    if which in ("citations", "all"):
        gate_citations()
    if which in ("register", "all"):
        gate_register()
    if which in ("mirror", "all"):
        gate_mirror()
    if findings:
        print(f"ssdt-agent gates ({which}): {len(findings)} finding(s)")
        for f in findings:
            print(f"  {f}")
        sys.exit(1)
    print(f"ssdt-agent gates ({which}): clean")


if __name__ == "__main__":
    main()
