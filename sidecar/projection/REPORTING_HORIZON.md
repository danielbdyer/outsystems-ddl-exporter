# REPORTING_HORIZON — the operator's data-reporting roadmap

**Status:** composed 2026-06-04 (operator planning pass); **all four tiers'
cores shipped 2026-06-04** (commits: T1 after-verdict; T2 actionable digest;
T4 ledger + R6 gauge; T3 Spectre `--pretty` panel). Each tier carries a
documented follow-on (the deeper leg) — see its section. This document
governs **how V2 reports data to an operator** — the experience of running
an export and trusting the result. It sits between two existing surfaces:

**What shipped (2026-06-04):** structured `canary.*` verdict events + a rich
`runComplete` rollup (`transformSummary` / `rationaleHistogram`); the
`suggestedConfigDigest` (merged-by-path actionable to-do list in the verdict);
the `RunLedger` + `readiness` verb computing the R6 consecutive-green-canary
cutover gauge; and the Spectre channel-2 `--pretty` verdict panel (a derived
consumer, never a second emit surface). **Follow-ons:** `profile.probe.*` +
declined→knob suggestion *sources* (T2); the live progress-bar leg (T3);
per-environment ledgers + the `diff <runA> <runB>` verb (T4).

- **`docs/logging-format.md`** is the *contract* — the event envelope,
  the §7 code taxonomy, the §10 `runComplete` rollup, the §12
  `suggestedConfig` discipline, the §15 two-channel (NDJSON + Spectre)
  model. It is the **substrate**: what an event *is*.
- **`HORIZON.md`** holds the *atomic items* (`H-031` SuggestedConfig
  population, `H-032` `suggest-config` verb, `H-033` policy diff, `H-059`
  operator-facing report, `H-027` moments-to-manifest, Group VIII
  observability). It is the **parts bin**.

This document is the **assembly drawing**: it sequences those atoms into
a coherent operator experience, names the gaps between the *designed*
contract and what *fires today*, and tiers the work by operator value.

**Audience priority (operator-set, 2026-06-04):** **CI / automation** and
**the human at the terminal**, co-equal. The durable audit / SSIS-consumer
artifact surface (manifest, decision-log) already exists and is *not* the
headline here — the headline is the **verdict a human reads** and the
**stream a machine gates on**. Every deliverable below must serve both:
human-legible *and* `jq`-able / exit-code-able.

**Status vocabulary** (per `BACKLOG.md` / `HORIZON.md`):
`proposed` / `scheduled` / `in-flight` / `shipped` / `canceled`.

---

## §1 The frame — one substrate, many lenses; four operator moments

The discipline that makes this tractable: **one event stream, many
renderings.** Nothing is re-instrumented; every report is a *projection*
of data we already produce (the `LogSink` event stream, the transform
writers, the manifest, the bench rollup, the canary diff). A new report
is a new lens, never a new probe.

The reporting surface is organized around the four moments an operator
lives through:

| Moment | The operator's question | The surface that answers it |
|---|---|---|
| **① Before** | "Is my intent captured? What am I about to run?" | `config.*` reflection — toggles, modules, connection, policy version |
| **② During** | "Is it alive, healthy, when does it finish?" | the live channel — stage progress, ETA, a running health tick |
| **③ After** | "Did it work? What must I act on? Can I prove it?" | the **verdict** + the **actionable digest** + the **evidence trail** |
| **④ Across** | "Is this getting better? What changed since last time?" | the **ledger** — canary history, run-to-run diff, trends |

The lenses that render those moments:

| Lens | Primary consumer | Shape | Status |
|---|---|---|---|
| **NDJSON stream** | CI, `jq`, aggregators | the §3 envelope; grep by `category`/`code`/`ssKey` | shipped (channel 1) |
| **Terminal rollup** (`runComplete`) | human scrollback | verdict + histograms + artifacts + actionable count | **skeleton only** (Tier 1) |
| **Pretty TTY** (Spectre) | human, live | stage tree, progress bars, summary panel | parked (Tier 3) |
| **Artifacts** | downstream / audit | manifest, decision-log, opportunities, profile | manifest shipped; rest partial |
| **The ledger** | human + CI, across time | canary history, `diff <A> <B>`, trend, R6 gate | not built (Tier 4) |
| **The Q&A surface** | human, investigating | `suggest-config`, `explain <ssKey>`, `diff` | not built (Tier 2/4) |

---

## §2 Reality ledger — what fires vs. what's designed

The encouraging finding from the 2026-06-04 audit: **almost nothing here
is a rewrite.** The substrate landed (LogSink keystone, the transform
event surface, `runComplete`, bench, the manifest). The gap is between the
*designed* taxonomy (`logging-format.md` §7/§10/§12) and what *emits*.

**Fires today (verified):**
- `config.runStart` / `connectionResolved` / `validationFailed`
- `extract.started` / `completed` · `profile.started` / `completed` (coarse — endpoints only)
- `transform.registered` / `applied` / `declined` / `lineage` / `diagnostic` — **the full transform surface** (2026-06-04 E1–E4 work)
- `emit.*` (started / completed / json / distributions / suggestConfig)
- `summary.stageCompleted` + `summary.runComplete` (on every emitting verb, 2026-06-04)
- A rich **bench** rollup inside `runComplete.aggregates`
- A rich **manifest artifact** (`Coverage`, `PredicateCoverage`, `ColumnProfiles`, `DeploymentBatches`, `PolicyConflicts`, `AppliedTransforms`)

**Designed but does NOT fire (the gaps, by operator cost):**

| Gap | Operator cost | Contract ref | Tier |
|---|---|---|---|
| **`canary.*` events** — the canary computes the verdict but *prints prose* ("canary green"), no `diffEmpty`/`divergence`/`toleranceMatched`/`cdcSilent` | the #1 fidelity surface isn't machine-readable or ledger-able | §7.7 | **1** |
| **`runComplete` rich rollup** — today `stages:[]`, `artifacts:[]`, no `rationaleHistogram` / `probeOutcomeHistogram` / `transformSummary` | the "after" verdict is skeletal — no "why didn't this tighten" | §10 | **1** |
| **`profile.probe.*`** (`fallbackTimeout` / `ambiguousMapping`) + `suggestedConfig` | the highest-value *actionable* signals don't exist | §7.3, §12 | **2** |
| **`suggestedConfig` discipline** — `suggestedConfigEdits: 0` always | no actionable digest, no merged patch | §12, `H-031` | **2** |
| **Spectre channel-2 / progress / ETA** | no live "during" surface at all | §15 | **3** |
| **Cross-run ledger / `diff` / trend / R6 gate counter** | no "across runs" surface; the R6 cutover gate is conceptual | — / `H-033` | **4** |
| Fine-grained `config.toggleResolved`, `extract.module.parsed/warning`, `emit.<target>.statementProduced` | "before" reflection is partial | §7.1–7.5 | 1–2 |

**One sentence:** we have rich *data* (transform stream, manifest, bench)
but thin *reporting* — the terminal rollup is a skeleton, the canary
verdict is prose, and nothing is actionable yet.

---

## §3 The four tiers (the plan)

### Tier 1 — Make the run legible (the "after" verdict) · `shipped` (2026-06-04)

**Why.** This is the highest leverage for the lowest effort: it turns
every run's *result* from prose into a structured verdict, serving both
audiences at once (human reads the panel; CI gates the events). It
derives almost entirely from data we **already emit**.

**Deliverables.**
1. **Structured `canary.*` events** (§7.7). The wide canary already
   computes the `PhysicalSchemaDiff`; emit `canary.diffEmpty` (green) /
   `canary.divergence` (per unmatched diff, `error`-level, **fails the
   run**) / `canary.toleranceMatched` / `canary.cdcSilent` instead of (or
   alongside) the prose at `Program.fs runCanary`. Location:
   `Deploy.runWideCanary*` result → CLI verb projection via `LogSink`.
2. **Populate the real `runComplete` rollup** (§10). Today's payload is
   `eventCounts` + bench `aggregates` + empty `stages`/`artifacts`. Add,
   all derivable from the transform stream + manifest we already produce:
   - `stages` (durations per extract/profile/emit/deploy/canary)
   - `artifacts` (kind / path / bytes — from the written `Outputs`)
   - `transformSummary` (`registered` / `applied` / `declined` counts)
   - `rationaleHistogram` (group `transform.declined` by §8.1 rationale,
     top-N samples) — the "why didn't this tighten" surface
   - the §11 roll-up `aggregates` collapse on `(category, code, ssKey)`
3. **The verdict line** — a single human-first summary line the CLI
   prints (under the pretty channel later) AND a machine-first
   `outcome` + meaningful **exit code** (0 / 5 canary-divergence /
   4 docker-down — already half-true; make it total).

**Acceptance.** A `full-export` (or `canary`) run's last NDJSON line is a
`summary.runComplete` whose payload carries a non-empty `stages`,
`artifacts`, `transformSummary`, and (when declines occurred)
`rationaleHistogram`; the canary verb emits `canary.diffEmpty` or
`canary.divergence`; exit codes are total. Property test: every run emits
exactly one `runComplete`; `transformSummary.registered` equals
`RegisteredAllTransforms.all` count minus strategies.

**Trigger.** Next slice. No dependency; pure derive-from-existing.

---

### Tier 2 — Make the run actionable (the "what do I do") · `shipped (core)` (2026-06-04)

**Why.** A verdict tells you *that* something needs attention; this tier
tells you *exactly what to change.* It elevates V1's single
naming-override template into the §12 first-class discipline: every
finding with an addressable knob carries the JSON path + value to apply.

**Deliverables.**
1. **`profile.probe.*` events** (§7.3) with `suggestedConfig` payloads —
   `fallbackTimeout` → suggest a higher `samplingCap`; `ambiguousMapping`
   → suggest the FK override. (Subsumes `H-031` SuggestedConfig
   population.)
2. **`transform.declined` → `suggestedConfig`** for the tip-able
   rationales (`NullBudgetEpsilon`, `UniquePolicyDisabled`,
   `ForeignKeyCreationDisabled`, `ForeignKeyNoCheckRecommended`) — §12.
3. **The `suggest-config <runId>` subcommand** (`H-032`) — consumes the
   event stream, merges every `suggestedConfig` into one config patch;
   `--apply` writes it. The `runComplete.suggestedConfigEdits` count is
   the high-leverage teaser ("you have 20 fixable findings; here's the
   patch").
4. **Severity-ranked, clustered, capped** finding presentation (§ slice-6
   discipline): sort by severity → cluster by axis → cap top-N per axis
   with overflow count. Never a wall of 1,000 lines.

**Acceptance.** A run against a source with a sampling-capped probe emits
`profile.probe.fallbackTimeout` carrying a `suggestedConfig`;
`suggest-config <runId>` produces a valid merged patch that, re-applied,
flips the corresponding decisions. Property tests: `suggestedConfig`
non-empty invariant on the mandated events; cluster-cap invariants under
FsCheck-noisy inputs.

**Trigger.** After Tier 1 (needs the rollup + a stable event surface to
merge from). The actionable digest is the payoff that makes the verdict
*useful*.

---

### Tier 3 — Make the run comfortable (the "during") · `shipped (core)` (2026-06-04)

**Why.** The only tier that's pure quality-of-life — but on a 300-table
profile or a bulk row-load, a live progress surface with ETA is the
difference between confidence and `ctrl-c`. This is the parked Spectre
channel-2 work; it is a *consumer* of the stream, never a second emit
surface (the V1 antipattern, banned by §13).

**Deliverables.**
1. **`TtyRenderer.fs`** (§15.3) — a Spectre.Console subscriber to the
   `LogSink` event stream, gated on `--pretty` AND
   `Console.IsErrorRedirected = false`. Renders the stage tree +
   per-stage progress + a final summary panel from
   `summary.stageCompleted` / `runComplete`.
2. **Progress + ETA** on the long legs — drive off the existing
   `Bench.streamProbe` / iterator-logging per-iteration samples
   (`OssysCliVerbsParityTests` row 118 cash-out: `SpectreProgressAdapter
   : IProgressRunner` over `Bench.snapshot()`).
3. **Channel discipline** — `--pretty` routes channel 1 (NDJSON) to
   `--json-out <path>` or suppresses it; **never both to the same TTY**.
   Default (no `--pretty`) stays clean NDJSON — CI's path is untouched.
4. Add the `Spectre.Console` + (existing) `Argu` NuGet refs **at
   `Projection.Cli` only** — never `Projection.Core` (no I/O in Core).

**Acceptance.** `projection full-export --config c.json --pretty` on a TTY
draws live progress + a summary panel; the same command piped to a file
yields clean NDJSON (no ANSI); `--json-out` separates the channels. The
grep-audit (§15.5) still passes — no raw `printfn` outside LogSink/TtyRenderer.

**Trigger.** Operator-pull (this is comfort, not correctness) OR
production CLI wiring (chapter 5.1). Independent of Tier 2; can land in
parallel once Tier 1's stage/summary events are rich.

---

### Tier 4 — Make runs compound (the "across") · `shipped (core)` (2026-06-04)

**Why.** This is the tier closest to **your cutover decision** and the
only one that's genuinely new design. A single run is a snapshot; cutover
readiness is a *trend*. The R6 governance gate ("N=10 consecutive green
canary runs + operator sign-off") is conceptual today — this tier makes
it a number you can read.

**Deliverables.**
1. **A run ledger** — persist each run's `runComplete` (it's already a
   single JSON artifact) into an append-only store keyed by
   `(environment, runId, ts)`. Bench already has a per-run snapshot
   precedent (`bench/<verb>/<ts>.json`).
2. **`diff <runA> <runB>`** (`H-033` policy diff, generalized) — what
   changed between two exports: tables added/removed, decisions that
   flipped, coverage delta, canary status delta. The `CatalogDiff` /
   `PhysicalSchemaDiff` machinery already exists; this is its
   operator-facing projection.
3. **The R6 cutover-readiness gauge** — a one-line verdict per
   environment: "UAT: 15/10 consecutive green canaries, 0 unmatched
   divergences, R6 gate: **ELIGIBLE**." Consumes the ledger's
   `canary.diffEmpty` / `canary.divergence` history.
4. **Trend surfaces** — warnings over time, coverage over time, profile
   duration creep (a perf-regression signal complementary to the bench
   gate).

**Acceptance.** After 10 runs, the ledger answers "how many consecutive
green canaries on UAT" and `diff run-9 run-10` shows the decision/coverage
delta. The R6 gauge reads ELIGIBLE only when the consecutive-green count
≥ threshold AND zero unmatched divergences.

**Trigger.** **Design this with the operator** — "what does cutover-
readiness reporting look like for *you*" is a judgment only you can
anchor. Highest design content; build after the per-run surfaces (Tiers
1–2) produce ledger-able events.

---

### Tier 5 — Polish: the Apple layer · `shipped (core)` (2026-06-04)

**Why.** The four tiers gave the plumbing; this gives the *soul*. The thesis:
**calm by default, infinite on demand** — a single-glance answer where every
glyph is a doorway to the full, machine-readable truth beneath. Seven
principles govern it: (1) the verdict is the interface; (2) color is meaning,
never decoration (a glyph always rides with color, so the signal survives a
colorblind reader / `NO_COLOR`); (3) every number is a doorway; (4) one
substrate, many lenses — auto-selected; (5) end with the next action;
(6) sensible by default, scriptable to the core; (7) subtract.

**Shipped (the high-leverage five).**
- **`Theme`** — the design-system module (glyphs, semantic color, the R6
  `meter`, `sparkline`s, canary-history dots). One visual language, every
  surface inherits it.
- **Calm by default** — auto-detect the channel (TTY → Spectre panel, pipe →
  NDJSON, no flag); the bench table demoted behind `-v` (still persisted).
- **The readiness board** — `readiness` leads with the hero answer, then the
  cutover meter + history dots + run totals.
- **Impact-ranked suggestions + `suggest-config --apply`** — the actionable
  to-do list, highest-leverage first; the panel names the single biggest
  lever and ends with the next action.
- **`explain <ssKey>`** — the drill-down: one node's full provenance
  (every transform + decision + finding + fix), rendered through the same
  projection the event stream uses.

**Follow-ons (the deeper legs).** The live progress-bar leg during a run
(Spectre live-display); a full-screen TUI (`inspect <runId>`); `--apply`
merging into the config *structure* (today it writes a flat `{path: value}`
patch); the `diff <runA> <runB>` verb; per-environment ledgers.

---

## §3.5 Masterful base primitives — the substrate under the outcomes · `shipped` (2026-06-04)

The five outcomes aren't five features — they're **projections of three
primitives**: a **Run** (the unit), a **Comparison** (the relation between
units), and a **View** (the lens onto either). Each was built as an *enriched
base that supports the outcomes without completing them*, with a
**discriminating predicate** (the input on which a naive version diverges) and
≥2 real consumers (so none is speculative). They also **compose** — and they
are the *activation of the codebase's own latent algebra* (the WAVE-6 torsor,
"one substrate, many lenses"), not new inventions.

- **`View`** (`src/Projection.Cli/View.fs`) — the renderable + queryable
  document. *Discriminator:* pretty / plain / json are projections of ONE
  value (the human and machine lenses can't drift). *Consumers:* the panel +
  board refactored onto it (output preserved); `--format`/`--query` fall out.
- **`Run`** (`src/Projection.Pipeline/Run.fs`) — the addressable,
  content-addressed run aggregate (verdict + the event stream as opaque NDJSON
  envelopes). *Discriminator:* `load (save run) = run`, and `inputDigest`
  depends only on inputs, not wall-clock. *Consumers:* persist / diff-of-runs /
  query / migrate-inputs; subsumes `RunLedger.LedgerRecord`.
- **`Comparison`** (`src/Projection.Cli/Comparison.fs`) — diff as a capability,
  unifying `CatalogDiff` (a torsor) and `PhysicalSchemaDiff` (a quotient).
  *Discriminator (in the type):* `Apply` is present iff the delta is replayable
  (`Some` for Catalog — Weyl-proven; `None` for PhysicalSchema). `Render`
  projects onto `View` (composes #3 with #1). *Consumers:* diff / drift /
  migrate.

---

## §3.6 The connector layer — Source / complete-Run / Ref · `shipped` (2026-06-05)

Working backwards from the five features revealed they're **a version-control
system for projections**: `Run` = a *commit*, `inputDigest` = the *SHA*,
`Comparison` = `diff`/`apply`, the ledger = `git log`, the canary = CI status.
The reframe names exactly what's missing to make it composable — the wires git
has and we didn't:

- **`Source`** (`src/Projection.Pipeline/Source.fs`) — the capability-typed
  input boundary (the *remote / working tree*). Resolves a Catalog from {file,
  json, live} and declares what it can do; a capability *is* the presence of
  its function (`AcquireProfile = Some` iff profilable — the same shape as
  `Comparison.Apply`), so asking a static model to profile fails at
  construction, not runtime. Unlocks live OSSYS.
- **complete-`Run`** — `Run` gained its *tree*: an `Artifacts` map (the output
  blobs, round-tripped) and `Run.capture` (the producer bridge from a live
  execution). A `runId` now resolves to *artifacts*, not just events — so it
  can be diffed/migrated-from offline, not only explained.
- **`Ref`** (`src/Projection.Pipeline/Ref.fs`) — **the keystone, the
  revision algebra**. A typed reference (`@runId` / file / `live:` / `json:`)
  that resolves to an operand, dispatching through `Source` (external) or the
  `Run`-store (`@runId`). A runId resolves to the *same* Catalog type as a
  file — that uniformity is the point: every future verb becomes `verb <ref>…`
  and they compose (`diff model.json @run-9`).

With the connector layer in place the five features (and their git-siblings —
drift = `status`, regression = `log -p`, rollback = `revert` via the torsor,
the build-cache = unchanged `inputDigest` ⇒ skip work) are thin: e.g.
`diff = resolveCatalog ×2 → Comparison.summary → View.write`.

**The temporal base — `RunHistory`** (`src/Projection.Pipeline/RunHistory.fs`,
`shipped 2026-06-05`) — the durable operator timeline: the chronological
sequence of persisted `Run`s. `trend` / `canaryHistory` / `readiness` all fall
out as `fold`/`map` over the one sequence — the history *is* the integral. It
is the durable realization the morphology named as missing (the FTC ran only
in-memory); distinct from `Core.Episode` (a single state-at-coordinate);
subsumes `RunLedger` (its index). With it the substrate is complete: the
forward use cases (evolve / migrate / investigate / promote) are all temporal,
and the navigational metaphor is a **timeline** — runs as points, the R6
streak as the trajectory toward eligible, trends as the shape of the path.

**What remains is the harvest, not the foundation:** the verbs
(`diff <ref> <ref>` · `migrate <ref> <ref>` · `explain @run <ssKey>` ·
`promote` · `certify`) and the dynamic display (the live progress leg + the
`inspect <runId>` TUI), each a thin `verb <ref>` on the substrate.

---

## §4 The dual-audience contract

Every deliverable serves **both** consumers; neither is an afterthought:

- **Human-first** (you at the terminal): the verdict line, the pretty
  panel (Tier 3), the clustered/capped digest, the `suggest-config`
  patch you review and accept, the R6 gauge.
- **Machine-first** (CI / automation): the same facts as NDJSON the
  same instant — gate-able `canary.divergence` events, a `jq`-able
  `runComplete` rollup, **total exit codes**, the `suggestedConfigEdits`
  count as a CI threshold.

The mechanism that guarantees both: **they are the same events.** The
human lens (Spectre) and the verdict line are *projections* of the
NDJSON stream the machine reads. There is no "human output" written
separately from "machine output" — that separation is precisely V1's
banned antipattern (§13.4 / §13.11). Channel split is by *rendering*
(`--pretty` gates the TTY lens), never by *content*.

---

## §5 Sequencing — the "when"

```
Tier 1 (after-verdict) ──────────────► the foundation; derive-from-existing
   │  rich runComplete + canary.* events + total exit codes
   ├──► Tier 2 (actionable) ─────────► needs a stable event surface to merge
   │       profile.probe.* + suggestedConfig + suggest-config verb
   ├──► Tier 3 (pretty TTY) ─────────► needs rich stage/summary events; else
   │       Spectre TtyRenderer + ETA       parallel to Tier 2
   └──► Tier 4 (ledger) ─────────────► needs ledger-able runComplete events;
           diff + R6 gauge + trends        design-with-operator
```

- **Tier 1 is the unlock.** Everything downstream consumes its enriched
  `runComplete` + `canary.*` events. It is also the cheapest (pure
  projection of existing data) and the most immediately valuable to both
  audiences.
- **Tiers 2 and 3 are parallelizable** once Tier 1 lands — Tier 2 is
  correctness/leverage, Tier 3 is comfort.
- **Tier 4 is last and most generative** — it needs runs to accumulate
  and needs your judgment on what cutover-readiness *means*.

---

## §6 The delightful edge (nice-to-haves, reserve)

Not scheduled; captured so they're not forgotten. Each is a small
projection on top of the tiers above:

- **"What would tighten if I…"** — a dry-run knob: "raise
  `NullBudgetEpsilon` to 0.02 → 12 more columns tighten, canary stays
  green." Decisions-as-preview; rides on Tier 2's `suggestedConfig`
  machinery run speculatively.
- **`explain <ssKey>`** — full provenance for one node: every decision,
  its rationale, the profile evidence that drove it. Rides on the
  transform `lineage`/`diagnostic` stream + manifest.
- **The cutover one-pager** — `H-059` operator-facing report, integrating
  topology + coverage + the R6 gauge into a single proof-carrying
  document.
- **Severity escalation** (§19.5) — if 1,000 `fallbackTimeout` warnings
  fire, surface the cumulative signal without burying the per-event
  detail. Re-open if the digest still feels noisy.

---

## §7 Cross-reference index

| This doc | Substrate (`logging-format.md`) | Atoms (`HORIZON.md`) | Code |
|---|---|---|---|
| Tier 1 verdict | §7.7 `canary.*`, §10 rollup, §11 collapse | Group VIII observability | `Deploy.runWideCanary*`, `Program.fs runCanary`, `LogSink`/`EventProjection` |
| Tier 2 actionable | §7.3 `profile.probe.*`, §12 `suggestedConfig` | `H-031`, `H-032` | `Program.fs` (`suggest-config` verb), profiling passes |
| Tier 3 pretty | §15 two-channel, §15.3 `TtyRenderer` | row 118 cash-out | new `TtyRenderer.fs`, `Projection.Cli.fsproj` |
| Tier 4 ledger | §10 `runComplete` (persisted) | `H-033` policy diff, `H-059` report | `CatalogDiff` / `PhysicalSchemaDiff`, bench-snapshot precedent |

**Governing disciplines** (do not violate without an amendment):
- One substrate, many lenses — never a second emit surface (§13.4 / §13.11).
- Additive taxonomy — new codes extend; renames carry a DECISIONS entry (§7).
- Every payload property earns its place — a grep target, a roll-up axis,
  or a config-edit signal (§ contract discipline summary).
- Channel split by rendering, never by content (§4 above).
