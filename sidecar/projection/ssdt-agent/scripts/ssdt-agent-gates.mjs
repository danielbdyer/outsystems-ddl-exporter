#!/usr/bin/env node
// ssdt-agent truth gates — citations | register | mirror | packaging | estate | all.
//
// The CI face of the ssdt-agent tree's ratchet rule (ENABLEMENT_PROGRAM.md §5:
// no fix lands without the detector that would have caught its absence). Four
// gates, each independently runnable; exit 0 = clean, exit 1 = findings (each
// finding printed as `file: finding`).
//
//   citations  every cross-reference resolves:
//              R1 relative backticked paths (../x/y.md, trailing / allowed)
//              R2 bare backticked *.md basenames exist under the tree, the
//                 curriculum (handbook/ + ssdt-playbook/), or sidecar/projection/
//              R3 scenario ids cited by the rubrics exist in the prompt files
//              R4 skills/op/<slug> <-> sample-prs/<slug>.md stay 1:1
//   register   THE_RECORD.md §7's unambiguous banned tokens stay out of the
//              record surfaces (sample-prs/*.md, golden/*-pr.md, and each op
//              skill's "On the record" fragment); golden PRs stay agentless
//              (no first/second person outside code fences)
//   mirror     .github/PULL_REQUEST_TEMPLATE/schema-change.md carries every
//              canonical author-pr section (the template's own precedence note)
//   packaging  every source frontmatter holds the strict contract (name +
//              description; a description containing ": " must be a properly
//              escaped double-quoted scalar — the exact class a strict skill
//              loader refuses), and the generated .claude/ packages are in
//              sync (ssdt-agent-package.mjs check)
//   estate     the estate ledgers stay machine-readable (consistent table
//              rows), no in-flight phase sits past its window date, and
//              every refusal block after the separator carries its
//              disposition + proof artifact with no unfilled placeholders
//
// Node ≥ 18, zero dependencies. Run from anywhere; paths resolve from this
// file's location. Lives inside the tree (ssdt-agent/scripts/) so it travels
// with a vendored copy; these gates are the SOURCE MONOREPO's CI face — a
// vendored estate runs `ssdt-agent-package.mjs copilot-check` instead (see
// ../pipelines/ssdt-agent-check.yml).

import { readFileSync, existsSync, readdirSync, statSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { dirname, join, relative, basename, normalize, sep } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));  // <tree>/scripts
const TREE = dirname(HERE);                    // .../ssdt-agent
const PROJ = dirname(TREE);                    // sidecar/projection
const REPO = dirname(dirname(PROJ));           // repo root

const findings = [];
const find = (path, msg) => findings.push(`${relative(REPO, path)}: ${msg}`);
const read = (path) => readFileSync(path, "utf8");
const stripFences = (text) => text.replace(/```[\s\S]*?```/g, "");

function walk(dir, out = []) {
  if (!existsSync(dir)) return out;
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (statSync(p).isDirectory()) walk(p, out);
    else out.push(p);
  }
  return out;
}
const mdIn = (dir) => walk(dir).filter((p) => p.endsWith(".md"));

// --------------------------------------------------------------------------
// citations
// --------------------------------------------------------------------------

const REL_PATH = /`((?:\.\.\/)+[\w./ -]+?(?:\.(?:md|sql|xml|json|refactorlog)|\/))`/g;
const BARE_MD = /`([A-Za-z0-9][\w. -]*?\.md)`/g;
const SCENARIO_ID = /\b(?:REV|COL|TBL|KEY|IDX|CON|STA|STR|AUD|IDEM|CMP)-\d{2}[A-Z]?\b/g;
const SLASH_SHORTHAND = /\b(REV|COL)-(\d{2}[A-Z]?)\/(\d{2}[A-Z]?)\b/g;

function gateCitations() {
  // copilot-package/ is a generated bundle FOR the estate repo; its backticked paths are
  // estate-repo-root-relative (they resolve only after the tree is vendored there), so the
  // monorepo citations gate must not try to resolve them. copilot-check owns that bundle.
  const treeMd = mdIn(TREE).filter((p) => !p.includes(`${sep}copilot-package${sep}`)).sort();

  // R1 — relative backticked paths resolve
  for (const p of treeMd) {
    const d = dirname(p);
    for (const m of read(p).matchAll(REL_PATH)) {
      if (!existsSync(normalize(join(d, m[1])))) {
        find(p, `relative path does not resolve: \`${m[1]}\``);
      }
    }
  }

  // R2 — bare .md basenames exist somewhere legitimate
  const known = new Set();
  for (const root of [TREE, join(REPO, "handbook"), join(REPO, "ssdt-playbook")]) {
    for (const f of mdIn(root)) known.add(basename(f));
  }
  for (const name of readdirSync(PROJ)) {
    if (name.endsWith(".md")) known.add(name);   // sidecar/projection top level (THE_TWIN.md etc.)
  }
  for (const p of treeMd) {
    for (const m of read(p).matchAll(BARE_MD)) {
      const name = m[1];
      if (name.includes("/") || name.includes("<") || name.includes("*")) continue;
      if (!known.has(name)) {
        find(p, `cited filename exists nowhere (tree, handbook/, ssdt-playbook/, sidecar/projection/): \`${name}\``);
      }
    }
  }

  // R3 — scenario ids cited by rubrics exist in the prompt files
  const prompts = read(join(TREE, "self-test", "prompts.md"));
  const reviewPrompts = read(join(TREE, "self-test", "review-prompts.md"));
  const defined = new Set((prompts + reviewPrompts).match(SCENARIO_ID) ?? []);
  for (const rubric of ["rubric.md", "review-rubric.md", "PROTOCOL.md"]) {
    const p = join(TREE, "self-test", rubric);
    for (const id of read(p).match(SCENARIO_ID) ?? []) {
      if (!defined.has(id)) find(p, `scenario id cited but defined nowhere in prompts: ${id}`);
    }
  }
  // the shorthand "REV-01/06" style hides a second id behind a slash
  for (const rubric of ["rubric.md", "review-rubric.md"]) {
    const p = join(TREE, "self-test", rubric);
    for (const m of read(p).matchAll(SLASH_SHORTHAND)) {
      for (const suffix of [m[2], m[3]]) {
        const sid = `${m[1]}-${suffix}`;
        if (!defined.has(sid)) find(p, `scenario id cited (slash shorthand) but defined nowhere: ${sid}`);
      }
    }
  }

  // R4 — op catalog and sample-prs stay 1:1
  const opDir = join(TREE, "skills", "op");
  const ops = new Set(
    readdirSync(opDir).filter((d) => existsSync(join(opDir, d, "SKILL.md")))
  );
  const prs = new Set(
    readdirSync(join(TREE, "sample-prs"))
      .filter((f) => f.endsWith(".md") && f !== "README.md")
      .map((f) => f.replace(/\.md$/, ""))
  );
  for (const slug of [...ops].filter((s) => !prs.has(s)).sort()) {
    find(join(opDir, slug), "op has no sample-prs worked example");
  }
  for (const slug of [...prs].filter((s) => !ops.has(s)).sort()) {
    find(join(TREE, "sample-prs", slug + ".md"), "sample PR has no owning op skill");
  }
}

// --------------------------------------------------------------------------
// register
// --------------------------------------------------------------------------

const BANNED = [
  [/\bMechanism [0-9]\b/, "numbered axis `Mechanism N`"],
  [/\bTier [0-9]\b/, "numbered axis `Tier N`"],
  [/\bblast radius\b/i, "banned nickname `blast radius`"],
  [/\bmagic line\b/i, "banned nickname `magic line`"],
  [/\bthe spine\b/i, "banned nickname `the spine`"],
  [/\bthe graduation\b/i, "banned nickname `the graduation`"],
  [/\bthe oracle\b/i, "banned nickname `the oracle`"],
  [/\bthe corpse\b/i, "banned nickname `the corpse`"],
  [/\bNaked Rename\b/, "retired trap name `Naked Rename` (say: a rename with no refactorlog entry)"],
  [/\bBLESS\b/, "ceremony verb `BLESS`"],
  [/\bHAND-BACK\b/, "ceremony verb `HAND-BACK`"],
  [/\bREFUSE-ESCALATE\b/, "ceremony verb `REFUSE-ESCALATE`"],
  [/catastroph/i, "drama (`catastrophe`)"],
  [/\bproving ground\b/i, "`proving ground` in a record (say: a disposable copy of Dev / the Twin)"],
];

const PERSON = /\b([Ii]|[Ww]e|[Yy]ou|[Yy]our)\b/;

function* recordSurfaces() {
  for (const p of mdIn(join(TREE, "sample-prs")).sort()) {
    if (basename(p) !== "README.md") yield [p, read(p)];
  }
  for (const p of mdIn(join(TREE, "self-test", "golden")).sort()) {
    if (p.endsWith("-pr.md")) yield [p, read(p)];
  }
  const opDir = join(TREE, "skills", "op");
  for (const d of readdirSync(opDir).sort()) {
    const p = join(opDir, d, "SKILL.md");
    if (!existsSync(p)) continue;
    const m = /^## On the record[\s\S]*/m.exec(read(p));
    if (m) yield [p, m[0]];
  }
}

function gateRegister() {
  for (const [p, text] of recordSurfaces()) {
    const scan = stripFences(text);
    for (const [pat, label] of BANNED) {
      const m = pat.exec(scan);
      if (m) {
        const ctx = scan.slice(Math.max(0, m.index - 30), m.index + m[0].length + 20).trim();
        find(p, `record register: ${label} (\`…${ctx}…\`)`);
      }
    }
  }
  for (const p of mdIn(join(TREE, "self-test", "golden")).sort()) {
    if (!p.endsWith("-pr.md")) continue;
    const scan = stripFences(read(p));
    const m = PERSON.exec(scan);
    if (m) {
      const ctx = scan.slice(Math.max(0, m.index - 40), m.index + m[0].length + 30).trim();
      find(p, `golden PR is not agentless: \`${m[0]}\` at \`…${ctx}…\``);
    }
  }
}

// --------------------------------------------------------------------------
// mirror
// --------------------------------------------------------------------------

const CANONICAL_SECTIONS = [
  "Summary", "Review & release", "Changes", "Data remediation",
  "Deployment evidence", "Verification", "Rollback", "Not verified",
];

function gateMirror() {
  // Two mirrors of author-pr's ten sections: the monorepo's own PR template, and
  // the tree's canonical template the Copilot bundle is generated from.
  const templates = [
    join(REPO, ".github", "PULL_REQUEST_TEMPLATE", "schema-change.md"),
    join(TREE, "pr-template", "schema-change.md"),
  ];
  for (const template of templates) {
    if (!existsSync(template)) {
      find(template, "schema-change PR template is missing");
      continue;
    }
    const heads = [...read(template).matchAll(/^## (.+)$/gm)].map((m) => m[1].trim());
    for (const section of CANONICAL_SECTIONS) {
      const present = heads.some(
        (h) => h === section || h.startsWith(section + " ") || h.startsWith(section + " —")
      );
      if (!present) {
        find(template, `template lost the canonical section \`${section}\` (author-pr/SKILL.md is the source of truth)`);
      }
    }
  }
}

// --------------------------------------------------------------------------
// packaging
// --------------------------------------------------------------------------

const FRONTMATTER = /^---\n([\s\S]*?\n)---\n/;

// The strict frontmatter contract (tighter than a generic YAML load, aimed at
// the exact class a strict skill loader refuses): top-level `key: value`
// lines only; name + description present; an unquoted description must not
// contain ": " (the defect class found at O4); a quoted description must be
// one properly backslash-escaped double-quoted scalar spanning the value.
function frontmatterProblem(fm) {
  const entries = new Map();
  for (const line of fm.split("\n")) {
    if (line.trim() === "") continue;
    const m = /^([A-Za-z][\w-]*): (.+)$/.exec(line);
    if (!m) return `frontmatter line is not a top-level \`key: value\` pair: \`${line.slice(0, 60)}\``;
    entries.set(m[1], m[2]);
  }
  if (!entries.has("name")) return "frontmatter is missing `name`";
  if (!entries.has("description")) return "frontmatter is missing `description`";
  const d = entries.get("description");
  if (d.startsWith('"')) {
    if (!/^"(?:[^"\\]|\\.)*"$/.test(d)) {
      return "quoted description is not one properly escaped double-quoted scalar";
    }
  } else if (d.includes(": ")) {
    return 'unquoted description contains ": " — a strict loader refuses this; wrap the description in double quotes';
  }
  return null;
}

function gatePackaging() {
  const src = [
    ...walk(join(TREE, "skills")).filter((p) => basename(p) === "SKILL.md"),
    ...walk(join(TREE, "agents")).filter((p) => p.endsWith(".md")),
  ].sort();
  for (const p of src) {
    const m = FRONTMATTER.exec(read(p));
    if (!m) { find(p, "no frontmatter block"); continue; }
    const problem = frontmatterProblem(m[1]);
    if (problem) find(p, problem);
  }
  for (const [verb, root] of [["check", ".claude"], ["copilot-check", "sidecar/projection/ssdt-agent/copilot-package"]]) {
    const r = spawnSync(process.execPath, [join(HERE, "ssdt-agent-package.mjs"), verb], {
      encoding: "utf8",
    });
    if (r.status !== 0) {
      const lines = `${r.stdout ?? ""}${r.stderr ?? ""}`.trim().split("\n").slice(1);
      for (const line of lines) find(join(REPO, root), line.trim());
    }
  }
}

// --------------------------------------------------------------------------
// estate
// --------------------------------------------------------------------------

function tableRows(text) {
  // Pipe-table rows outside code fences, split into cells; separator rows dropped.
  return stripFences(text)
    .split("\n")
    .filter((l) => l.trim().startsWith("|"))
    .map((l) => l.trim().replace(/^\|/, "").replace(/\|$/, "").split("|").map((c) => c.trim()))
    .filter((cells) => !cells.every((c) => /^:?-+:?$/.test(c)));
}

function gateEstate() {
  const estate = join(TREE, "estate");
  if (!existsSync(estate)) { find(estate, "estate/ ledger directory is missing"); return; }

  // Ledger tables stay machine-readable: every row matches its header's width.
  for (const name of ["operations.md", "row-tiers.md", "in-flight.md"]) {
    const p = join(estate, name);
    if (!existsSync(p)) { find(p, "ledger file is missing"); continue; }
    const rows = tableRows(read(p));
    if (rows.length === 0) { find(p, "ledger has no header row"); continue; }
    const width = rows[0].length;
    rows.slice(1).forEach((cells, i) => {
      if (cells.length !== width) {
        find(p, `ledger row ${i + 1} has ${cells.length} cells; the header has ${width} — the ledger must stay machine-readable`);
      }
    });
  }

  // No in-flight phase sits past its window (column 7: `window closes`, YYYY-MM-DD; column 3
  // is `tables`, the machine-readable hold inflight-check.mjs enforces).
  const inflight = join(estate, "in-flight.md");
  if (existsSync(inflight)) {
    const today = new Date().toISOString().slice(0, 10);
    for (const cells of tableRows(read(inflight)).slice(1)) {
      const when = cells[6] ?? "";
      if (!/^\d{4}-\d{2}-\d{2}$/.test(when)) {
        find(inflight, `in-flight row \`${cells[0] ?? "?"}\`: window-closes is not YYYY-MM-DD (\`${when}\`)`);
      } else if (when < today) {
        find(inflight, `in-flight row \`${cells[0] ?? "?"}\`: window closed ${when} — advance the phase or consciously re-date it with the reviewer's sign-off`);
      }
    }
  }

  // Every refusal block after the separator is complete and placeholder-free.
  const refusals = join(estate, "refusals.md");
  if (existsSync(refusals)) {
    const after = read(refusals).split(/\n---\n/).slice(1).join("\n---\n");
    for (const m of after.matchAll(/```[\s\S]*?```/g)) {
      const block = m[0];
      if (!/^\s*LEDGER — /m.test(block)) continue;
      if (!/disposition:\s*\S/.test(block)) find(refusals, "refusal block is missing its `disposition:` line");
      if (!/proof artifact:\s*\S/.test(block)) find(refusals, "refusal block is missing its `proof artifact:` line — a risk without its artifact is not a named risk");
      if (block.includes("<")) find(refusals, "refusal block carries unfilled `<placeholder>` fields");
    }
  }
}

// --------------------------------------------------------------------------

function main() {
  const which = process.argv[2] ?? "all";
  if (which === "citations" || which === "all") gateCitations();
  if (which === "register" || which === "all") gateRegister();
  if (which === "mirror" || which === "all") gateMirror();
  if (which === "packaging" || which === "all") gatePackaging();
  if (which === "estate" || which === "all") gateEstate();
  if (findings.length) {
    console.log(`ssdt-agent gates (${which}): ${findings.length} finding(s)`);
    for (const f of findings) console.log(`  ${f}`);
    process.exit(1);
  }
  console.log(`ssdt-agent gates (${which}): clean`);
}

main();
