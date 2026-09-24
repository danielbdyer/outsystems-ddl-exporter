// The operator review page: one self-contained HTML file from one JSON spec.
//
//   node ci/review/build.js <spec.json> <out.html>
//
// Publish the result as a private Artifact with capabilities {"db": {}}. The page saves the operator's
// answers to the artifact's db, and the session reads them back with ArtifactData before acting:
//   decisions/d<id>          {id, choice (index or -1), option, notes, at}   a note without a choice sends it back
//   findings/<id>            {id, verdict: fix | wontfix | discuss | "", note, at}
//   reports/<actions.id>     the report-back form's fields; reports/<actions.id>-checks its ticks
//   gate/<gate.id>           {choice, option, notes, at}
// The page is a view; nothing on it is adopted until the session writes it into DECISIONS.md, VALUES.md,
// NEXT.md or the code.
//
// The spec (every section optional except heading):
//   id, title (the <title>: a name, 2-4 words), eyebrow [[label, value]], heading, dek, answer (markdown)
//   decisions [{id, when, title, question, current, refs [finding id suffixes], options [{k, d, c, rec}]}]
//   findings  [{id "<lens>-<ID>", severity blocker|major|minor|note, title, where, evidence, designSays,
//               recommendation, lens, skeptic}]
//   actions   {id, title, count, intro (markdown), steps [markdown], fields [{f, label, hint, wide, rows}],
//              checks [{o, label}]}
//   gate      {id, title, subtitle, cardTitle, options [{k, d, rec}]}
//   sections  [{id, title, count, md, fold}]   after the gate: what is solid, risks, not checked
//   colophon  markdown
const fs = require("fs");
const path = require("path");
const { esc, inline, render } = require("./mdlite");

const [specPath, outPath] = process.argv.slice(2);
if (!specPath || !outPath) { console.error("usage: node ci/review/build.js <spec.json> <out.html>"); process.exit(2); }
const spec = JSON.parse(fs.readFileSync(specPath, "utf8"));
const here = __dirname;
const css = fs.readFileSync(path.join(here, "app.css"), "utf8");
const client = fs.readFileSync(path.join(here, "app.client.js"), "utf8");

const decisions = spec.decisions || [], findings = spec.findings || [];
const count = s => findings.filter(f => f.severity === s).length;
const lenses = [...new Set(findings.map(f => f.lens))];
const data = JSON.stringify({ id: spec.id, decisions, findings, gate: spec.gate, actions: spec.actions ? { id: spec.actions.id } : null }).replace(/</g, "\\u003c");

const nav = [];
const out = [];
if (spec.answer) {
  nav.push(`<a href="#answer">The answer</a>`);
  out.push(`<section class="sec" id="answer"><div class="sec-head"><h2>The answer</h2>${findings.length ? `<span class="sec-count">${findings.length} findings · ${count("blocker")} blocker · ${count("major")} major</span>` : ""}</div><div class="prose">${render(spec.answer)}</div></section>`);
}
if (decisions.length) {
  nav.push(`<a href="#decisions-sec">Decisions<span class="c" id="j-dec"></span></a>`);
  out.push(`<section class="sec open-band" id="decisions-sec"><div class="sec-head"><h2>Decisions for you</h2><span class="sec-count">${decisions.length} · ordered by when each is needed</span></div>
<p class="sec-intro">Each is a choice only you should make. The recommended option carries its reason. Pick one, or write your own answer in the note.</p><div id="decisions"></div></section>`);
}
if (findings.length) {
  nav.push(`<a href="#findings-sec">Findings<span class="c" id="j-find"></span></a>`);
  out.push(`<section class="sec" id="findings-sec"><div class="sec-head"><h2>Findings</h2><span class="sec-count">${findings.length} · most severe first</span></div>
<p class="sec-intro">Mark what the next wave should fix, what it should leave, and what we should talk about. A finding a decision settles says so and links to it.</p>
<div class="filters" role="group" aria-label="Filter findings">
  <div class="fgroup"><span>severity</span>${["blocker", "major", "minor", "note"].map(s => `<button type="button" class="chip" data-sev="${s}" aria-pressed="true">${s} ${count(s)}</button>`).join("")}</div>
  <div class="fgroup"><span>status</span><button type="button" class="chip" data-status="all" aria-pressed="true">all</button><button type="button" class="chip" data-status="open" aria-pressed="false">untriaged</button><button type="button" class="chip" data-status="triaged" aria-pressed="false">triaged</button></div>
  <div class="fgroup"><select id="flens" aria-label="Lens"><option value="all">every lens</option>${lenses.map(l => `<option value="${esc(l)}">${esc(l)}</option>`).join("")}</select><input type="text" id="fq" placeholder="Search findings" aria-label="Search findings"></div>
  <button type="button" class="bulk" id="bulk" data-n="0" hidden>Mark untriaged as fix</button><span class="fcount" id="fcount"></span>
</div><div class="stack" id="findings"></div></section>`);
}
if (spec.actions) {
  const a = spec.actions;
  const field = x => `<label${x.wide ? ' class="wide"' : ""}>${esc(x.label)}${x.hint ? ` <small>${esc(x.hint)}</small>` : ""}${x.rows
    ? `<textarea id="af-${esc(x.f)}" data-f="${esc(x.f)}" rows="${x.rows}"></textarea>` : `<input type="text" id="af-${esc(x.f)}" data-f="${esc(x.f)}">`}</label>`;
  nav.push(`<a href="#actions-sec">${esc(a.title)}</a>`);
  out.push(`<section class="sec" id="actions-sec"><div class="sec-head"><h2>${esc(a.title)}</h2>${a.count ? `<span class="sec-count">${esc(a.count)}</span>` : ""}</div>
${a.intro ? `<div class="sec-intro prose">${render(a.intro)}</div>` : ""}
${(a.steps || []).length ? `<ol class="steps">${a.steps.map(s => `<li><span>${inline(s)}</span></li>`).join("")}</ol>` : ""}
<div class="form-grid" id="act-form">${(a.fields || []).map(field).join("")}
${(a.checks || []).length ? `<div class="wide checks" id="act-checks">${a.checks.map(c => `<label><input type="checkbox" data-o="${esc(c.o)}"> ${inline(c.label)}</label>`).join("")}</div>` : ""}
<span class="saved wide" id="act-saved" aria-live="polite"></span></div></section>`);
}
if (spec.gate) {
  const g = spec.gate;
  nav.push(`<a href="#gate-sec">${esc(g.nav || "Gate")}</a>`);
  out.push(`<section class="sec open-band" id="gate-sec"><div class="sec-head"><h2>${esc(g.title)}</h2>${g.subtitle ? `<span class="sec-count">${esc(g.subtitle)}</span>` : ""}</div>
<article class="card" id="gate-card" data-state="open"><div class="card-head"><span class="cid">${esc(g.id)}</span><h3 class="ctitle">${esc(g.cardTitle || g.title)}</h3><span class="pill">awaiting you</span></div>
<div class="opt-bar" id="gate-opts" role="group" aria-label="${esc(g.title)}"></div>
<div class="note-wrap"><label for="gate-note">Conditions or instructions for the next step</label><textarea id="gate-note" rows="3"></textarea><span class="saved" aria-live="polite"></span></div></article></section>`);
}
for (const s of spec.sections || []) {
  nav.push(`<a href="#${esc(s.id)}">${esc(s.title)}</a>`);
  const body = `<div class="prose">${render(s.md)}</div>`;
  out.push(`<section class="sec" id="${esc(s.id)}"><div class="sec-head"><h2>${esc(s.title)}</h2>${s.count ? `<span class="sec-count">${esc(s.count)}</span>` : ""}</div>${s.fold ? `<details class="fold"><summary>${esc(s.fold)}</summary>${body}</details>` : body}</section>`);
}

const tal = (id, cls, n, l) => `<div class="tal${cls}" id="${id}"><span class="n">${n}</span><span class="l">${l}</span></div>`;
const tallies = [
  decisions.length ? tal("t-open", " open", decisions.length, "decisions open") : "",
  findings.length ? tal("t-untri", " open", findings.length, "findings to triage") + tal("t-fix", "", 0, "marked fix") + tal("t-disc", "", 0, "to discuss") : "",
  spec.gate ? tal("t-gate", "", "—", esc(spec.gate.tally || "ruling")) : "",
].join("");

const page = `<title>${esc(spec.title || spec.heading)}</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500;600&family=IBM+Plex+Sans+Condensed:wght@500;600;700&family=IBM+Plex+Sans:ital,wght@0,400;0,500;0,600;1,400&display=swap">
<style>
${css}
</style>
<div class="shell">
<header class="masthead">
  ${(spec.eyebrow || []).length ? `<div class="eyebrow">${spec.eyebrow.map(([k, v]) => `<span>${esc(k)} <b>${esc(v)}</b></span>`).join("")}</div>` : ""}
  <h1>${esc(spec.heading)}</h1>
  ${spec.dek ? `<p class="dek">${inline(spec.dek)}</p>` : ""}
  <div class="tally"><div class="tally-row">${tallies}</div>
    <div class="tally-note"><span class="live-note">Answers save as you make them, and the session reads them back. On a decision, <kbd>1</kbd> <kbd>2</kbd> <kbd>3</kbd> pick an option; on a finding, <kbd>F</kbd> fix · <kbd>W</kbd> won't fix · <kbd>D</kbd> discuss, and <kbd>Enter</kbd> opens it. A note without a choice sends a decision back to the session.</span>
    <span id="offline-note">This view cannot save answers; it is showing the page read-only. Reply in the session instead.</span></div>
  </div>
</header>
<nav class="jump" aria-label="Sections">${nav.join("")}</nav>
${out.join("\n")}
${spec.colophon ? `<footer class="colophon">${render(spec.colophon)}</footer>` : ""}
</div>
<script type="application/json" id="review-data">${data}</script>
<script>
${client}
</script>
`;
fs.writeFileSync(outPath, page);
console.log(`wrote ${outPath}: ${page.length} bytes, ${decisions.length} decisions, ${findings.length} findings`);
