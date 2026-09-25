// The operator review page's script (built into the page by ci/review/build.js). Answers save to the artifact's db:
// decisions/<d…>, findings/<id>, reports/<doc>, gate/<id>; the session reads them back with ArtifactData.
(() => {
  const DATA = JSON.parse(document.getElementById("review-data").textContent);
  const DECISIONS = DATA.decisions || [], FINDINGS = DATA.findings || [], GATE = DATA.gate || { id: "gate", options: [] };
  const GATE_DOC = "gate/" + (GATE.id || "gate"), ACT_DOC = "reports/" + ((DATA.actions && DATA.actions.id) || "actions");

  const esc = s => String(s == null ? "" : s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  // {{Term}} becomes a tappable term whose definition opens in place; an unknown term stays plain text.
  const GLOSSARY = DATA.glossary || {};
  const term = key => GLOSSARY[key]
    ? '<button type="button" class="ref" aria-expanded="false">' + esc(key) + '</button><span class="gloss" hidden><b>' + esc(key) + "</b> · " + inline(GLOSSARY[key]) + "</span>"
    : esc(key);
  function inline(t) {
    const codes = [];
    let s = String(t == null ? "" : t).replace(/`([^`]+)`/g, (_, c) => { codes.push(c); return "\u0000" + (codes.length - 1) + "\u0000"; });
    s = esc(s).replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
    s = s.replace(/\{\{([^{}]+)\}\}/g, (_, k) => term(k.replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, "\"").replace(/&amp;/g, "&")));
    return s.replace(/\u0000(\d+)\u0000/g, (_, i) => "<code>" + esc(codes[+i]) + "</code>");
  }
  const paras = t => String(t || "").split(/\n{2,}|\n(?=[-*] )/).map(p => "<p>" + inline(p.replace(/\n/g, " ")) + "</p>").join("");
  const docId = id => "d" + id.replace(/\./g, "-");
  const short = id => String(id).replace(/^[a-z]+-(?=[A-Z])/, ""); // a finding id is <area>-<ID>; people read the ID

  // Which decision settles a finding: a finding id appears in a decision's refs.
  const decidedBy = {};
  for (const d of DECISIONS) for (const r of d.refs) {
    const f = FINDINGS.find(x => short(x.id) === r);
    if (f) (decidedBy[f.id] = decidedBy[f.id] || []).push(d.id);
  }

  let store = null;
  const dState = {}, fState = {}, gState = {};
  let report = {}, ops = {};
  const expanded = new Set();
  const ls = {
    get(k, d) { try { const v = localStorage.getItem(k); return v == null ? d : JSON.parse(v); } catch (e) { return d; } },
    set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch (e) { /* storage refused: the filter just isn't remembered */ } },
  };
  const FKEY = "estate-review." + (DATA.id || "review") + ".filters";
  const filters = Object.assign({ sev: ["blocker", "major", "minor", "note"], area: "all", status: "all", q: "" }, ls.get(FKEY, {}));

  const readOnly = () => document.body.classList.add("read-only");
  const stamp = () => new Date().toISOString();
  const flash = (el, text) => { if (!el) return; el.textContent = text; clearTimeout(el._t); el._t = setTimeout(() => { el.textContent = ""; }, 2200); };
  const save = async (path, body, fb) => {
    if (!store) return;
    try { await store.doc(path).set(Object.assign({}, body, { at: stamp() })); flash(fb, "saved"); }
    catch (e) {
      if (e && (e.code === "not_granted" || e.code === "revoked" || e.code === "capability_disabled")) readOnly();
      else flash(fb, "not saved: " + (e && e.code ? e.code : "error"));
    }
  };

  const decided = id => { const s = dState[id]; return !!(s && typeof s.choice === "number" && s.choice >= 0); };
  const returned = id => { const s = dState[id]; return !!(s && !(s.choice >= 0) && s.notes && s.notes.trim()); };

  // ---------- decisions ----------
  const decisionCard = d => {
    const s = dState[d.id] || {};
    const choice = typeof s.choice === "number" ? s.choice : -1;
    const state = choice >= 0 ? "done" : returned(d.id) ? "returned" : "open";
    const pill = choice >= 0 ? "decided" : returned(d.id) ? "returned with a note" : "awaiting you";
    // A related finding is listed with its title, so the reference says what it points at.
    const refs = (d.refs || []).map(r => {
      const f = FINDINGS.find(x => short(x.id) === r);
      return f ? '<li><a href="#f-' + esc(f.id) + '" data-open="' + esc(f.id) + '">' + esc(r) + "</a>" + inline(f.title) + "</li>" : "";
    }).join("");
    const field = (label, text, cls) => text ? '<div class="field' + (cls ? " " + cls : "") + '"><span class="fl">' + label + '</span><div class="ft">' + paras(text) + "</div></div>" : "";
    // Situation and complication first, then the question, then the recommended answer and its reason, then the options.
    return '<article class="card" tabindex="0" id="' + docId(d.id) + '" data-kind="decision" data-id="' + esc(d.id) + '" data-state="' + state + '">'
      + '<div class="card-head"><span class="cid">' + esc(d.id) + '</span><h3 class="ctitle">' + inline(d.title) + '</h3><span class="pill">' + pill + "</span></div>"
      + field("Situation", d.situation) + field("Complication", d.complication) + field("Where the build stands", d.current)
      + (d.examplesHtml ? '<div class="field"><span class="fl">Worked examples</span><div class="ft prose">' + d.examplesHtml + "</div></div>" : "")
      + field("Question", d.question) + field("Recommendation", d.recommendation, "rec-field")
      + '<div class="opt-bar" role="group" aria-label="Options for ' + esc(d.id) + '">'
      + d.options.map((o, i) => '<button type="button" class="opt" data-id="' + esc(d.id) + '" data-i="' + i + '" aria-pressed="' + (choice === i) + '">'
        + '<span class="k">' + (i + 1) + "</span><span><b>" + inline(o.k) + (o.rec ? '<span class="rec">recommended</span>' : "") + "</b>"
        + (o.d ? '<span class="od">' + inline(o.d) + "</span>" : "") + (o.c ? '<span class="oc">' + inline(o.c) + "</span>" : "") + "</span></button>").join("")
      + "</div>"
      + (refs ? '<div class="field"><span class="fl">Related findings</span><ul class="refs">' + refs + "</ul></div>" : "")
      + '<div class="note-wrap"><label for="n-' + docId(d.id) + '">A note, a condition, or your own answer</label>'
      + '<textarea id="n-' + docId(d.id) + '" class="dnote" data-id="' + esc(d.id) + '" rows="2">' + esc(s.notes || "") + "</textarea>"
      + '<span class="saved" aria-live="polite"></span></div></article>';
  };
  const renderDecisions = () => {
    if (!document.getElementById("decisions")) return;
    const groups = [];
    for (const d of DECISIONS) { let g = groups.find(x => x.when === d.when); if (!g) groups.push(g = { when: d.when, items: [] }); g.items.push(d); }
    document.getElementById("decisions").innerHTML = groups.map(g =>
      '<div class="group-label">' + esc(g.when) + " · " + g.items.filter(d => !decided(d.id)).length + " open of " + g.items.length + "</div>"
      + '<div class="stack">' + g.items.map(decisionCard).join("") + "</div>").join("");
  };

  // ---------- findings ----------
  const shown = () => FINDINGS.filter(f => {
    const v = (fState[f.id] || {}).verdict || "";
    if (!filters.sev.includes(f.severity)) return false;
    if (filters.area !== "all" && f.area !== filters.area) return false;
    if (filters.status === "open" && v) return false;
    if (filters.status === "triaged" && !v) return false;
    if (filters.q) { const q = filters.q.toLowerCase(); if (![f.id, f.title, f.where, f.evidence, f.recommendation].join(" ").toLowerCase().includes(q)) return false; }
    return true;
  });
  const SEV_ORDER = { blocker: 0, major: 1, minor: 2, note: 3 };
  const VERDICTS = [["fix", "fix", "F"], ["wontfix", "won't fix", "W"], ["discuss", "discuss", "D"]];
  const findingRow = f => {
    const s = fState[f.id] || {};
    const v = s.verdict || "";
    const open = expanded.has(f.id);
    const by = decidedBy[f.id];
    return '<article class="frow" tabindex="0" id="f-' + esc(f.id) + '" data-kind="finding" data-id="' + esc(f.id) + '" data-v="' + v + '">'
      + '<div class="fhead" data-toggle="' + esc(f.id) + '" aria-expanded="' + open + '"><span class="fid">' + esc(short(f.id)) + "</span>"
      + '<span class="ftitle">' + inline(f.title) + "</span>"
      + '<span class="fmeta"><span class="sev sev-' + f.severity + '">' + f.severity + "</span>"
      + (v ? '<span class="vtag ' + v + '">' + (v === "wontfix" ? "won't fix" : v) + "</span>" : "")
      + '<span class="area">' + esc(f.area) + "</span></span></div>"
      + '<div class="fbody"' + (open ? "" : " hidden") + ">"
      + (f.situation ? '<div class="field"><span class="fl">Situation</span><div class="ft">' + paras(f.situation) + "</div></div>" : "")
      + '<div class="field"><span class="fl">Where</span><div class="ft">' + inline(f.where) + "</div></div>"
      + '<div class="field"><span class="fl">Evidence</span><div class="ft">' + paras(f.evidence) + "</div></div>"
      + (f.designSays ? '<div class="field"><span class="fl">What the design says</span><div class="ft">' + paras(f.designSays) + "</div></div>" : "")
      + '<div class="field"><span class="fl">Fix</span><div class="ft">' + paras(f.recommendation) + "</div></div>"
      + (f.skeptic ? '<div class="field"><span class="fl">Second review</span><div class="ft"><span class="verified">confirmed · </span>' + inline(f.skeptic) + "</div></div>" : "")
      + (by ? '<div class="decided">Settled by decision ' + by.map(id => '<a class="link" href="#' + docId(id) + '">' + esc(id) + "</a>").join(", ") + "; triage here only what an agent should do regardless.</div>" : "")
      + '<div class="actbar" role="group" aria-label="Triage ' + esc(short(f.id)) + '">'
      + VERDICTS.map(([key, label, k]) => '<button type="button" class="act ' + key + '" data-id="' + esc(f.id) + '" data-v="' + key + '" aria-pressed="' + (v === key) + '">' + label + ' <span class="k">' + k + "</span></button>").join("")
      + "</div>"
      + '<div class="note-wrap"><label for="fn-' + esc(f.id) + '">Note</label><textarea id="fn-' + esc(f.id) + '" class="fnote" data-id="' + esc(f.id) + '" rows="1">' + esc(s.note || "") + "</textarea>"
      + '<span class="saved" aria-live="polite"></span></div></div></article>';
  };
  const renderFindings = () => {
    if (!document.getElementById("findings")) return;
    const list = shown().sort((a, b) => SEV_ORDER[a.severity] - SEV_ORDER[b.severity]);
    document.getElementById("findings").innerHTML = list.length ? list.map(findingRow).join("") : '<p class="sec-intro">No finding matches these filters.</p>';
    document.getElementById("fcount").textContent = list.length + " of " + FINDINGS.length + " shown";
    for (const b of document.querySelectorAll("button.chip[data-sev]")) b.setAttribute("aria-pressed", String(filters.sev.includes(b.dataset.sev)));
    for (const b of document.querySelectorAll("button.chip[data-status]")) b.setAttribute("aria-pressed", String(filters.status === b.dataset.status));
    document.getElementById("farea").value = filters.area;
    const bulk = document.getElementById("bulk");
    const n = list.filter(f => !(fState[f.id] || {}).verdict).length;
    bulk.hidden = n === 0; bulk.classList.remove("arm"); bulk.textContent = "Mark the " + n + " untriaged shown as fix"; bulk.dataset.n = n;
  };

  // ---------- gate, the operator's actions, tally ----------
  const renderGate = () => {
    if (!document.getElementById("gate-opts")) return;
    const choice = typeof gState.choice === "number" ? gState.choice : -1;
    document.getElementById("gate-opts").innerHTML = GATE.options.map((o, i) =>
      '<button type="button" class="opt" data-gate="1" data-i="' + i + '" aria-pressed="' + (choice === i) + '"><span class="k">' + (i + 1) + "</span><span><b>" + inline(o.k)
      + (o.rec ? '<span class="rec">recommended</span>' : "") + '</b><span class="od">' + inline(o.d) + "</span></span></button>").join("");
    const card = document.getElementById("gate-card");
    card.dataset.state = choice >= 0 ? "done" : "open";
    card.querySelector(".pill").textContent = choice >= 0 ? "ruled" : "awaiting you";
  };
  const fillForm = () => {
    for (const el of document.querySelectorAll("#act-form [data-f]")) if (document.activeElement !== el) el.value = report[el.dataset.f] || "";
    for (const el of document.querySelectorAll("#act-checks [data-o]")) el.checked = !!ops[el.dataset.o];
  };
  const tally = () => {
    const open = DECISIONS.filter(d => !decided(d.id)).length;
    const tri = FINDINGS.filter(f => (fState[f.id] || {}).verdict).length;
    const count = v => FINDINGS.filter(f => (fState[f.id] || {}).verdict === v).length;
    const set = (id, n) => { const el = document.querySelector("#" + id + " .n"); if (el) el.textContent = String(n); };
    set("t-open", open); set("t-untri", FINDINGS.length - tri); set("t-fix", count("fix")); set("t-disc", count("discuss"));
    set("t-gate", typeof gState.choice === "number" && gState.choice >= 0 ? "✓" : "—");
    const el = id => document.getElementById(id) || { classList: { toggle() {} }, textContent: "" };
    el("t-open").classList.toggle("clear", open === 0);
    el("t-untri").classList.toggle("clear", tri === FINDINGS.length);
    el("j-dec").textContent = open ? String(open) : "";
    el("j-find").textContent = FINDINGS.length - tri ? String(FINDINGS.length - tri) : "";
  };
  const renderAll = () => { renderDecisions(); renderFindings(); renderGate(); fillForm(); tally(); };

  // ---------- interaction ----------
  const chooseDecision = async (id, i) => {
    const s = dState[id] || {};
    const next = s.choice === i ? -1 : i;
    dState[id] = Object.assign({}, s, { choice: next });
    const note = document.querySelector('textarea.dnote[data-id="' + id + '"]');
    if (note) dState[id].notes = note.value;
    renderDecisions(); tally();
    const card = document.getElementById(docId(id));
    if (card) card.focus({ preventScroll: true });
    await save("decisions/" + docId(id), { id, choice: next, option: next >= 0 ? DECISIONS.find(d => d.id === id).options[next].k : "", notes: dState[id].notes || "" }, card && card.querySelector(".saved"));
  };
  const triage = async (id, v) => {
    const s = fState[id] || {};
    const next = s.verdict === v ? "" : v;
    const note = document.querySelector('textarea.fnote[data-id="' + id + '"]');
    fState[id] = Object.assign({}, s, { verdict: next, note: note ? note.value : (s.note || "") });
    const row = document.getElementById("f-" + id);
    if (row) { row.outerHTML = findingRow(FINDINGS.find(f => f.id === id)); }
    const again = document.getElementById("f-" + id);
    if (again) again.focus({ preventScroll: true });
    tally();
    await save("findings/" + id, { id, verdict: next, note: fState[id].note }, again && again.querySelector(".saved"));
  };

  document.addEventListener("click", async e => {
    const ref = e.target.closest("button.ref");
    if (ref) { // a term opens its definition in place; nothing re-renders, so an unsaved note survives
      const gloss = ref.nextElementSibling, open = ref.getAttribute("aria-expanded") === "true";
      ref.setAttribute("aria-expanded", String(!open));
      if (gloss && gloss.classList.contains("gloss")) gloss.hidden = open;
      return;
    }
    const opt = e.target.closest("button.opt");
    if (opt && opt.dataset.gate) {
      const i = Number(opt.dataset.i);
      gState.choice = gState.choice === i ? -1 : i;
      renderGate(); tally();
      await save(GATE_DOC, { choice: gState.choice, option: gState.choice >= 0 ? GATE.options[gState.choice].k : "", notes: document.getElementById("gate-note").value }, document.querySelector("#gate-card .saved"));
      return;
    }
    if (opt) { await chooseDecision(opt.dataset.id, Number(opt.dataset.i)); return; }
    const act = e.target.closest("button.act");
    if (act) { await triage(act.dataset.id, act.dataset.v); return; }
    const head = e.target.closest(".fhead");
    if (head) {
      const id = head.dataset.toggle;
      if (expanded.has(id)) expanded.delete(id); else expanded.add(id);
      const body = head.nextElementSibling; body.hidden = !expanded.has(id); head.setAttribute("aria-expanded", String(expanded.has(id)));
      return;
    }
    const openRef = e.target.closest("a[data-open]");
    if (openRef) {
      const id = openRef.dataset.open;
      filters.sev = ["blocker", "major", "minor", "note"]; filters.area = "all"; filters.status = "all"; filters.q = "";
      document.getElementById("fq").value = ""; ls.set(FKEY, filters);
      expanded.add(id); renderFindings();
      return; // the anchor's own navigation scrolls to the row
    }
    const chip = e.target.closest("button.chip");
    if (chip && chip.dataset.sev) {
      const s = chip.dataset.sev;
      filters.sev = filters.sev.includes(s) ? filters.sev.filter(x => x !== s) : filters.sev.concat(s);
      ls.set(FKEY, filters); renderFindings(); return;
    }
    if (chip && chip.dataset.status) { filters.status = chip.dataset.status; ls.set(FKEY, filters); renderFindings(); return; }
    const bulk = e.target.closest("#bulk");
    if (bulk) {
      if (!bulk.classList.contains("arm")) { bulk.classList.add("arm"); bulk.textContent = "Confirm: mark " + bulk.dataset.n + " as fix"; return; }
      const targets = shown().filter(f => !(fState[f.id] || {}).verdict);
      for (const f of targets) fState[f.id] = Object.assign({}, fState[f.id] || {}, { verdict: "fix" });
      renderFindings(); tally();
      for (const f of targets) await save("findings/" + f.id, { id: f.id, verdict: "fix", note: fState[f.id].note || "" });
    }
  });
  document.addEventListener("change", async e => {
    const t = e.target;
    if (t.matches("textarea.dnote")) {
      const id = t.dataset.id; dState[id] = Object.assign({}, dState[id] || {}, { notes: t.value });
      const choice = typeof dState[id].choice === "number" ? dState[id].choice : -1;
      await save("decisions/" + docId(id), { id, choice, option: choice >= 0 ? DECISIONS.find(d => d.id === id).options[choice].k : "", notes: t.value }, t.parentElement.querySelector(".saved"));
      const card = document.getElementById(docId(id)); if (card) { card.dataset.state = decided(id) ? "done" : returned(id) ? "returned" : "open"; card.querySelector(".pill").textContent = decided(id) ? "decided" : returned(id) ? "returned with a note" : "awaiting you"; }
      return;
    }
    if (t.matches("textarea.fnote")) {
      const id = t.dataset.id; fState[id] = Object.assign({}, fState[id] || {}, { note: t.value });
      await save("findings/" + id, { id, verdict: fState[id].verdict || "", note: t.value }, t.parentElement.querySelector(".saved"));
      return;
    }
    if (t.id === "gate-note") { await save(GATE_DOC, { choice: typeof gState.choice === "number" ? gState.choice : -1, option: gState.choice >= 0 ? GATE.options[gState.choice].k : "", notes: t.value }, document.querySelector("#gate-card .saved")); gState.notes = t.value; return; }
    if (t.dataset.f) { report[t.dataset.f] = t.value; await save(ACT_DOC, report, document.getElementById("act-saved")); return; }
    if (t.dataset.o) { ops[t.dataset.o] = t.checked; await save(ACT_DOC + "-checks", ops, document.getElementById("act-saved")); return; }
    if (t.id === "farea") { filters.area = t.value; ls.set(FKEY, filters); renderFindings(); }
  });
  document.addEventListener("input", e => { if (e.target.id === "fq") { filters.q = e.target.value; ls.set(FKEY, filters); renderFindings(); } });
  document.addEventListener("keydown", e => {
    if (e.metaKey || e.ctrlKey || e.altKey) return;
    if (e.target && /^(TEXTAREA|INPUT|SELECT)$/.test(e.target.tagName)) return;
    const here = document.activeElement && document.activeElement.closest ? document.activeElement.closest(".card[data-kind], .frow") : null;
    const key = e.key.toLowerCase();
    if (here && here.dataset.kind === "finding") {
      const map = { f: "fix", w: "wontfix", d: "discuss" };
      if (map[key]) { e.preventDefault(); triage(here.dataset.id, map[key]); }
      else if (key === "enter" && e.target === here) { e.preventDefault(); here.querySelector(".fhead").click(); }
      return;
    }
    if (/^[1-9]$/.test(key)) {
      const id = here && here.dataset.kind === "decision" ? here.dataset.id : (DECISIONS.find(d => !decided(d.id)) || {}).id;
      if (!id) return;
      const d = DECISIONS.find(x => x.id === id);
      if (Number(key) <= d.options.length) { e.preventDefault(); chooseDecision(id, Number(key) - 1); }
    }
  });

  // ---------- load ----------
  if (document.getElementById("fq")) document.getElementById("fq").value = filters.q;
  renderAll();
  (async () => {
    const db = await (window.claude && window.claude.use ? window.claude.use("db") : Promise.resolve(null));
    if (!db) { readOnly(); return; }
    store = db;
    try {
      const [ds, fs, rs, gs] = await Promise.all([db.collection("decisions").get(), db.collection("findings").get(), db.collection("reports").get(), db.collection("gate").get()]);
      for (const doc of ds.docs) { const b = doc.data(); if (b && b.id) dState[b.id] = b; }
      for (const doc of fs.docs) { const b = doc.data(); if (b && b.id) fState[b.id] = b; }
      for (const doc of rs.docs) { if ("reports/" + doc.id === ACT_DOC) report = Object.assign({}, doc.data()); if ("reports/" + doc.id === ACT_DOC + "-checks") ops = Object.assign({}, doc.data()); }
      for (const doc of gs.docs) if ("gate/" + doc.id === GATE_DOC) Object.assign(gState, doc.data());
      if (document.getElementById("gate-note")) document.getElementById("gate-note").value = gState.notes || "";
      renderAll();
    } catch (e) { readOnly(); }
  })();
})();
