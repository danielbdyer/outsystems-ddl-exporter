# Handoff — Incorporating the Voice into the rendered TTY

**The complete brief. One document, everything you need to get going.** This is the living
handoff for the operator-facing *voice* work on the `projection` tool. It is self-sufficient
to orient you and start you on the first wave; it also maps the full corpus — the register, the
surface, the North Star, the ontology, the change calculus, the isomorphism climb — so you know
exactly which document to open when you need depth. Read this in full, then read the referenced
materials it points you to. Branch: `claude/handoff-voice-implementation-PzpiS`.

> **A note on this document.** It was reified 2026-06-08 from the original 2026-06-06 anchor
> letter (preserved verbatim as Appendix B). Where the original described "an un-started build"
> on a 16-verb CLI, the machinery is now **built and proven** and the CLI is the **flows
> surface**; this brief is the current truth. Per the handoff discipline (CLAUDE.md), it is a
> **forward-looking letter**, not a status report — written to *you*, the agent picking this up.

---

## To the next agent

You're picking up a piece of work whose hard part is already done and whose remaining part is
easy to get wrong. The voice **machinery** — the seam that turns a coded event into operator
copy — is built, tested, and green. What's left is to **wire the register into the rendered
TTY**: take the raw `printf`/`%A` prose still scattered through the CLI and route it through the
voice, one surface at a time, so the instrument speaks in one exact register everywhere.

Here is the one thing to internalize before anything else: **this work looks mechanical and is
not.** Every raw render site is a place the instrument fails to *disappear* — where the operator
sees the apparatus (`%A`, `norm=`, `REFUSED`, a leaked exit code) instead of the truth the
engine proves about itself. If you initialize on the file:line list alone, you will produce
strings that pass the mechanical guard and miss the soul, and the operator *will* notice — the
register was settled with them one rule at a time, including corrections you'd otherwise
reintroduce by instinct. So read the *why* first (§2), hold the *register* (§3), and only then
execute the *waves* (§5). The bar is not "the guard passes." The bar is: *would a newcomer trust
this on sight, would a master read it as a glance, and does the instrument disappear behind it?*

That's the whole job. The rest of this document makes it concrete.

---

## 1 — The mission, in one paragraph

The `projection` engine proves a theorem about itself — that the database means exactly what the
model says, in both directions, provably. The **Voice** is the layer that lets a human *read*
that proof: it renders the deep truth as the plain finding a newcomer trusts on sight, with the
formal proof one level beneath for whoever has the question. The machinery exists and is proven
(`Voice.fs`, `Watch.fs`, `TtyRenderer.fs`, `Surface.fs`, the totality tests). Your job is pure
**rendering**: wire that machinery into every operator-facing output site in the CLI, replacing
raw prose with voiced surfaces, in the order `THE_VOICE_BACKLOG.md` specifies — no new engine, no
new events, no changed codes. Seven waves, highest-leverage first. Start at Wave 0.

---

## 2 — Why this exists (read this before you touch a string)

### The engine is one adjunction

The engine emits a logical Model to a physical Substrate (`Project`) and reads it back
(`Ingest`); the law it proves about itself is that reading back what it emitted returns exactly
the model (`Ingest ∘ Project = identity`, modulo named, declared erasures). *(This is the
engine's internal law — the one piece of algebra in this brief, here only because it is the
thing the Voice translates **from**; it is never shown to the operator.)* Everything the engine
does is a corollary: the canary is that law at runtime, drift is its failure surfaced, a
migration is the law applied to a delta. **Fidelity is not a property a human verifies; it is a
theorem the engine proves, continuously, about itself.** That is the North Star (`NORTH_STAR.md`).

### The Voice is the third layer

A self-proving instrument is worthless if the operator can't read the proof. So the instrument
has three cleanly-separable layers (`THE_INSTRUMENT.md`): **Structure** (`View` — *what* is
shown), **Style** (`Theme` — *how it looks*), and **Voice** (*how it speaks*). The Voice is your
layer. Its job is to take the theorem and say it as *"Verified. The database matches the model."*
The essence is the proof made kind; the proof is the essence made rigorous; **the surface never
changes between the newcomer and the master — only the velocity.** And the design goal is that
the instrument **disappears**: what remains is the schema's truth and the operator's hand on it.
Every raw render site is a seam where it fails to disappear. That is what each wave closes.

### Where the why-behind-the-why lives

You don't need these to start, but when you want the full depth, they are the foundation the
voice rests on:

- **The target** — `THE_USE_CASE_ONTOLOGY.md` (the masterwork index): the closed alphabet of
  change-moves (Add/Remove/Rename/Reshape/Reidentify/Move/Accumulate), every operator workflow
  as an ordered chain (the nine "proteins"), and the laws. This is *what the voice describes*.
- **The change calculus** — `WAVE_6_ONTOLOGY.md` (the moves, grounded in SQL-Server mechanics),
  `WAVE_6_ALGEBRA.md` (State as a torsor over Delta; the change-measure as the norm; the Project
  square commutes), `WAVE_6_MORPHOLOGY.md` (the as-is, read from the codebase). These name the
  structures the operator copy translates to plain words.
- **The isomorphism climb** — `NORTH_STAR.md` §1/§3 (the L1→L2→L3 ladder; the six totalities),
  `AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md` (the substantiation audit), and
  `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md`. This is *why fidelity is the soul* and why the proofs
  (§6 of the register) are the most important surfaces you'll voice.
- **Where the code stands** — `DEBRIEF_2026_06_02_ISOMORPHISM_CLIMB_AND_BACKLOG.md` (the
  current-state ledger). The engine already proves `migrate`/`transfer`/verify end to end; the
  voice and the surfaces are what remain.

---

## 3 — How the instrument speaks (the register)

This is the law for every string you write. The source is `THE_VOICE.md` — **read it in full and
keep it open while you work.** Below is the operable distillation; it is not a substitute for the
source.

### The twelve rules (the floor)

1. No pronouns (no *I/we/you/your*). 2. Direction by imperative (`Grant ALTER, then retry.`).
3. Legible statement, formal substantiation beneath — no symbolic shorthand on the lead.
4. Verdicts are findings, asserted. 5. The true verb (`Drops·Deletes·Narrows` — no euphemism, no
drama). 6. Gentle and direct, never colloquial. 7. Neutral reference to the estate (never
*your*). 8. Ground every claim in its evidence (no "X, not Y" antithesis tic). 9. Order by real
structure. 10. Name the exact referent. 11. Concrete definite subjects. 12. Stative, agentless
(`Model read complete`, not `Read the model`; gerunds-in-progress like `Reading the model`
excepted).

### The banned list (§2.2)

Pronouns · the antithesis tic (*verified, not assumed*) · euphemism (*cleaned up*) · drama
(*destroy, fatal, blast radius*) · figurative terms (*dig, jewel, green hush*) · **system-shout
as a lead** (`REFUSED/ERROR/FAILED`) · **algebra/jargon on any operator line** · colloquialism ·
leaked internals (`OS_KIND_*`, raw `SsKey`, file paths where a name belongs, **exit codes on the
statement line**) · negation-as-headline.

### The legibility axiom (the correction the operator drew, hold it hard)

**Every rendered string — statement *and* substantiation — must be legible to a technical
layperson** (a developer or DBA who knows databases but nothing of *this engine's* internals).
The boundary translates at **every depth** (`THE_VOICE.md` §2.1, "translate, always"; the
calibration `‖δ‖ norm ▲ 3` → `Total changes: 3` holds *anywhere*, not just the lead). The line
the operator drew, exactly:

- **Out — algebra and erudite jargon, never rendered:** `‖δ‖ ∅ ∘ ≈`, norm, residual, commuting
  square, isomorphism, torsor, quotient. Recast in plain/domain terms (*rows changed · nothing
  left over · the database matches the model · one difference remains*).
- **In — domain concreteness and purposeful technicality:** CDC, ALTER/ADD/DROP, idempotent,
  index, column, schema, the exit code. A DBA knows them; they sharpen the finding, not obscure
  it.

The substantiation is the statement *made rigorous in plain words*, never the statement trailed
by its glyphs. The only raw machine token beneath is the exit code or a diagnostic code.

### The bar above the guard

`VoiceTotalityTests` (the banned-list guard) is necessary, not sufficient. It catches a pronoun,
a shout, a `%A`. It cannot tell you whether the line is true, grounded, or kind. A line is done
when: one register serves both velocities; the essence is on top with the proof one plain level
beneath; the finding is asserted with grounded authority; it ends on the next move; and the
instrument disappears. **The felt arc is the target, not the string** (`THE_VOICE.md` §7) — when
you voice a surface you place one frame in the operator's composed experience; read §11 (off→on)
and §7 before you write. The full scene-by-scene surface is `THE_STORYBOARD.md` (nine acts × six
streams × positive/negative/edge; §8 is the P-6 worked proof at per-string fidelity — your
granularity target).

---

## 4 — Where you are in the codebase

### The CLI is the flows surface

The CLI re-grounded (twice) into **flows**: `projection <flow> [--go] [--fresh] [--allow-drops]`,
dispatched through one `runPlan` over a `PlanAction` DU in `src/Projection.Cli/Program.fs` (~1876
lines). Read `THE_CLI.md` for the surface; it is a sibling of the voice docs and already writes
in the register. `projection` with no args lists the flows; the first token is a flow name unless
it's one of the small secondary verbs (`check`/`explain`/`seal`/`report`/`init`).

### The machinery you're inheriting (built, tested, green)

- **`src/Projection.Cli/Voice.fs`** — the catalog. `all` / `lookup` / `toSurface` / `verdict` /
  `errorFrame` / `errorSurface` / `errorsSurface` / `gateStatement` / `gateSurface` / `stageName`.
  `Copy = { Code; DocSection; Statement; Substantiation; Action }`; the hybrid shape (typed
  `toView` for payload-shaped moves/gates/proofs; a code-keyed catalog for flat codes).
- **`src/Projection.Cli/TtyRenderer.fs`** — the renderers. `renderSummary` / `renderReadinessBoard`
  / `renderAnswer` / `renderVoicedError` / `renderErrorsTo` / **`renderGate`** (total over the
  closed `Preflight.GateLabel` DU — built, tested, **and uncalled**; this is Wave 1's drop-in).
- **`src/Projection.Cli/Watch.fs`** — the streaming stage board with the **minimum-dwell floor**
  (`PROJECTION_WATCH_DWELL_MS`, default 120 ms — a minimum inter-frame interval so events don't
  flash past perception).
- **`src/Projection.Cli/Surface.fs`** — `Surface = { Statement; Substantiation; Action }` (the
  essence / proof / move assembly every surface composes through).
- **`src/Projection.Cli/Comparison.fs`** — the statement-first change panel (`renderCatalogDiff` /
  `renderCatalogChange`), proven by `runDiff`; the substrate for the §9 minimality proof.
- **`tests/Projection.Tests/VoiceTotalityTests.fs`** — the `code ⇔ copy` totality (every in-scope
  LIVE code has copy; every copy maps to an emittable code), the gate⇔copy totality (the
  closed-DU analog), and the **mechanical banned-list guard** over every voiced surface.

### What's voiced vs raw

Six sink families render in register today (the summary panel, the readiness board, the
catalog-diff answer, the `Refused`/parse/model errors, and every `printErrors` *body*). **Almost
every executor's success narration and nearly every refusal is still raw prose.** The complete
file:line inventory — voiced vs raw, mapped to act + register section, with the renderer for each
gap — is `THE_VOICE_BACKLOG.md` (§3). The build-map's architecture, code inventory, and test
blast-radius live in `THE_VOICE_BUILD_MAP.md` (§1/§2/§8 are still accurate; its §6 execution layer
is superseded by the backlog).

### Running the tests

`scripts/test.sh fast` is your inner loop (the pure pool, ~2870 tests). **Never** run the pure
and Docker pools concurrently (OOM on this host — see CLAUDE.md). The voice work is pure-pool;
you won't need Docker for it.

---

## 5 — The work: the seven waves

The full map with worked **Today → In register** examples per wave is `THE_VOICE_BACKLOG.md`.
Execute in order; each wave is independently shippable, pure pool green at each step. In brief:

- **Wave 0 — stop the live breaches.** Four `%A` raw-DU dumps (`Program.fs:1167,1279,575/579,1752`)
  and four system-shout leads (`"canary RED"`, `"DRIFT DETECTED"`, `"verification FAILED"`,
  `"FAILED self-verification"`) are on the operator surface *today*. Smallest fixes, highest
  integrity cost.
- **Wave 1 — voice the gates (highest leverage).** Wire the **idle** `renderGate` / `gateSurface`
  into every migrate/transfer/synthetic pre-flight refusal. Zero new infra; the renderer is total
  over `Preflight.GateLabel` and tested; `Preflight.classify` already maps code → label. *This is
  the single highest-value item.*
- **Wave 2 — the §9 minimality proof.** The preview + report lead with `norm=` today; voice them
  statement-first reusing `Comparison.renderCatalogDiff` (proven by `runDiff`).
- **Wave 3 — the §6 proofs** (canary / drift / verify-data). The soul; route the prose through the
  existing §6 copy, demote the raw diff into a disclosure.
- **Wave 4 — §13 lifecycle** (success narration + delete the redundant raw headers above voiced
  error bodies).
- **Wave 5 — the §4 move surfaces** (transfer report, `explain` long tail).
- **Wave 6 — the synthetic/capture surface** (newest; voiced last, under evidence).

---

## 6 — What "done" looks like

Don't guess at the finished shape — it's drawn. **`THE_VOICE_BACKLOG.md` Appendix A** renders six
complete operator surfaces at full zoom: the migrate preview at three zooms (essence → dug-in →
at scale), the gate in full, the live run frame-by-frame, the proofs essence-then-dig, the
timeline/ladder, and arrival/setup — each with the positive, negative, and edge outcomes, in the
`Theme` glyphs, holding the legibility axiom at every depth. Build toward those; derive the exact
strings from `THE_VOICE.md`.

---

## 7 — The disciplines that bind

- **Codes never change, only copy.** The NDJSON event contract (`LogSink`/`EventProjection`/
  `Config`) is the machine channel — DO-NOT-BREAK. This is a pure rendering project; no event
  moves.
- **Derive, never invent** (`THE_VOICE.md` §15). Every string comes from a move (§4), gate (§5),
  proof (§6), error (§10), or config (§14) example. New operator word? Add it to the lexicon
  (§2.1) first, then use it.
- **Declare-at-site, harvest-centrally; `code ⇔ copy` + gate⇔copy totality.** `Voice.all` is the
  harvest; the totality tests are the sibling of the registry's `registered ⇔ executed`. Every new
  flat code enters the in-scope set as it lands; a new `GateLabel` without §5 copy fails the build.
- **IR grows under evidence.** Voice what an executor actually emits; never author copy for a
  surface that doesn't render yet.
- **Pure-Core holds.** Operator prose never enters `Projection.Core`; `View`/`Surface`/`Voice` live
  in `Projection.Cli` (the resolved Core-purity sub-call: the 1:1 projection-layer companion).
- **Commit to `claude/handoff-voice-implementation-PzpiS`.** Don't open a PR unless asked. Record
  resolved questions in `DECISIONS.md` (append-only).

---

## 8 — The one open decision (resolve it in Wave 1, then record it)

The `--go` / `PROJECTION_ALLOW_EXECUTE` **intent gate** (the two-gate consent model — `--go`
states intent, `ALLOW_EXECUTE` arms the live write) has no `Preflight.GateLabel` variant. Two
ways: add a `Preflight.IntentNotStated` variant (keeps closed-DU totality covering it), **or**
voice it through a flat `gate.intent` `errorSurface` code. **Recommendation: the flat code** — the
intent gate is a CLI-surface consent concern, not an engine pre-flight, so it needn't enter the
engine's closed DU. Settle it when you wire Wave 1; write the choice into `DECISIONS.md`.

---

## 9 — Reading order (≈45–60 min to be fully oriented)

| # | Document | Why · what to take | Time |
|---|---|---|---|
| 1 | **`NORTH_STAR.md`** | Why the engine exists — fidelity as a theorem it proves about itself. §1 (the bullseye) + §2 (one law, every capability a corollary). | 10 min |
| 2 | **`THE_INSTRUMENT.md`** | What the operator experiences — the three layers; the newcomer *is* the power user; the instrument disappears. The Voice is the third layer. | 8 min |
| 3 | **`THE_VOICE.md`** (in full) | The register — §1 twelve rules, §2.2 banned list, §3 verdicts, §4 moves, §5 gates, §6 proofs (the soul), §7 the felt arc, §10 errors, §11 off→on, §14 config, §15 how to derive. **Keep it open as you work.** | 15 min |
| 4 | **`THE_VOICE_BACKLOG.md`** | Your execution map — §0/§1 (the North Star + what "done" means), §2 (the register in one screen), §3 (voiced-vs-raw inventory), §4 (the seven waves with worked examples), **Appendix A** (the finished surfaces at full zoom). | 15 min |
| 5 | **`THE_STORYBOARD.md`** | The surface scene-by-scene — skim §1–§3 (acts + streams + the shot list), read §8 (the P-6 worked proof, your granularity target). | 8 min |
| 6 | **`THE_CLI.md`** | The flows surface you render on — the command shape, the config layers, §9 (the norm/minimality surface). | 6 min |

Then skim the `DECISIONS.md` entries dated `2026-06-08` (the voice-backlog reification, the
legibility axiom, the full-zoom surfaces) and the CLAUDE.md "Operator-facing voice register" row.

---

## 10 — The full document map (open these when you need depth)

| Document | What it is | When to open it |
|---|---|---|
| `THE_VOICE.md` | **The register** — the law for every string. | Always open while writing copy. |
| `THE_INSTRUMENT.md` | The future-state vision — essence + dig; the three layers. | For the *why* of the experience. |
| `NORTH_STAR.md` | The apex — fidelity as a theorem; the adjunction; the L1→L2→L3 ladder. | For the *why* of the engine. |
| `THE_STORYBOARD.md` | The surface — nine acts × six streams × outcomes; §8 P-6 worked proof. | When designing a surface's shape. |
| `THE_VOICE_BACKLOG.md` | **The execution map** — voiced/raw inventory, seven waves, Appendix A full-zoom surfaces. | Your day-to-day work surface. |
| `THE_VOICE_BUILD_MAP.md` | The architecture (§1), code inventory (§2), test blast-radius (§8). §6 execution layer is superseded by the backlog. | For the event spine + UPDATE/DO-NOT-BREAK test split. |
| `THE_VOICE_INTEGRATION.md` | The original build plan (slices 0–5, locked decisions). | For the slice history / locked decisions. |
| `THE_CLI.md` | The flows surface — command shape, config, the norm surface (§9). | To understand where copy lands. |
| `THE_USE_CASE_ONTOLOGY.md` | **The target** (ontology) — the change-move alphabet, the nine proteins, the laws. | For what the voice describes. |
| `WAVE_6_ONTOLOGY/ALGEBRA/MORPHOLOGY.md` | **The change calculus** (Wave 6) — the moves, the torsor/norm, the as-is. | For the structures the copy translates *from*. |
| `AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md` · `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md` | **The isomorphism** substantiation — why fidelity is the soul. | For the depth behind the §6 proofs. |
| `DEBRIEF_2026_06_02_ISOMORPHISM_CLIMB_AND_BACKLOG.md` | The current-state ledger — what the engine proves today. | For "what's actually LIVE." |
| `THE_SYNTHETIC_DATA_DESIGN.md` · `V1_INPUT_DEPRECATION.md` | Adjacent built work (the synthetic flow; live-OSSYS-primary read) — the surfaces of Wave 6. | When you reach the synthetic surface. |
| `DECISIONS.md` | Append-only resolved-questions log; the `2026-06-08` voice entries. | Before re-opening any settled choice; after settling one. |
| `CLAUDE.md` | Codebase navigation, operating disciplines, the test-runner rules. | For anything about the codebase itself. |

---

## 11 — Your first session, concretely

1. Read §2–§3 of this brief and `THE_VOICE.md` §1 + §2.2 + §6 + §11. Internalize the legibility
   axiom (algebra out, domain in).
2. Open `THE_VOICE_BACKLOG.md`; read §0–§2 and Appendix A so you've *seen* what done looks like.
3. Confirm the tree is green: `scripts/test.sh fast`.
4. **Do Wave 0.** Kill the four `%A` dumps and four shout leads — surgical, high-integrity, a clean
   first commit that proves the loop (edit → `test.sh fast` → commit).
5. **Do Wave 1.** Wire `renderGate` into the pre-flight refusals; resolve the intent-gate decision
   (§8) and record it in `DECISIONS.md`. This is where the leverage is.
6. From there, follow the backlog wave by wave. Re-read `THE_VOICE.md` §11 (off→on) whenever a line
   feels off — it usually is.

The machinery is whole. The surface is the work, and it's mapped to the line. Hold the register at
every depth, build toward the surfaces in Appendix A, and let the instrument disappear.

— *the outgoing agent, 2026-06-08*

---

## Appendix A — The locked register decisions (do not re-open without the operator)

These were settled with the operator one rule (or correction) at a time. Re-deriving them by
instinct is the failure mode; hold them.

- **The register** is authoritative / scientific / mature / humble, under *evidential literalism*
  — the twelve rules (§3 above). Rule 12 (stative, agentless) and the legibility axiom (algebra
  out, domain in) are the two most-often-reintroduced violations.
- **"self-check" → "round-trip verification"** (the "self" reintroduced a subject).
- **"dig"** is retired from the voice vocabulary → "the statement" / "the substantiation" / "Show
  detail". The vision thesis ("one essence, infinitely diggable") is kept; the figurative word is
  not.
- **Catalog shape is hybrid** — typed `toView` for payload-shaped moves/gates/proofs; a code-keyed
  declarative catalog for flat lifecycle/error/config codes.
- **Placement is declare-at-site / harvest-centrally** (the `TransformRegistry` pattern); voice has
  **no runtime write side**.
- **Core-purity** resolved to the 1:1 projection-layer companion — prose lives in `Projection.Cli`,
  never `Projection.Core`.
- **The legibility axiom** (the 2026-06-08 correction): every rendered string, statement and
  substantiation alike, is legible to a technical layperson; the boundary translates at every
  depth; algebra/erudite jargon never renders, domain technicality does.

---

## Appendix B — The original 2026-06-06 anchor letter (preserved verbatim)

*This is the letter as first written, when the build was un-started on the 16-verb CLI. The
machinery has since been built and the CLI re-grounded to flows; the register it describes is
unchanged and still authoritative. Preserved for provenance.*

> **To the next agent picking up the dynamic-display / voice work.** You're inheriting a complete,
> locked anchor set for how the instrument speaks and what it shows — and an un-started build. Your
> job is to execute the build the anchor set specifies, in the order it specifies, *without
> re-litigating the register* — it was settled with the operator one rule at a time, including
> several corrections you would otherwise re-introduce by instinct.
>
> **Where you are in the spine.** The estate has a target (`THE_USE_CASE_ONTOLOGY.md`), a proven
> engine (`migrate` / `transfer` / verify are LIVE — the engine already *proves* a migration end to
> end), and a settled voice and surface design. What is unbuilt is almost entirely the voice and
> the surfaces: the words, the streaming Watch, the record line, the timeline / ladder, the depth
> layer.
>
> **Read, in order:** `THE_VOICE.md` (the register — the twelve rules §1 + banned list §2.2 are
> non-negotiable; derive, don't invent), `THE_STORYBOARD.md` (the surface scene by scene; §7
> build-readiness; §8 the P-6 worked proof), `THE_VOICE_INTEGRATION.md` (the build plan).
>
> **What's decided — do not re-open without the operator:** the register (authoritative /
> scientific / mature / humble, evidential literalism, the twelve rules; rule 12 stative-agentless;
> "self-check" → "round-trip verification"); hybrid catalog shape; declare-at-site / harvest-
> centrally placement (no runtime write side); streaming Watch from the start; "dig" retired from
> the vocabulary (the thesis kept).
>
> **Disciplines while you build:** the twelve rules over every string (the discipline *is* the
> product); declare-at-site, harvest-centrally (`code ⇔ copy` totality is the sibling of
> `registered ⇔ executed`); IR grows under evidence (voice what's emitted, not latent surfaces).
>
> The anchor is solid. Build inside it, and keep the voice exact.
