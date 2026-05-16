# BACKLOG — Operational backlog for V2 cutover

**Status:** re-canonicalized 2026-05-16. Sibling to `V2_DRIVER.md`. The
operational ledger that interweaves V2_DRIVER's per-phase chapter
sequence with V1 inheritance opportunities (carbon-copy candidates and
shipped carbon-copies), cross-cutting infrastructure work, and the
risk register.

**Strategic relationship:**

- **`V2_DRIVER.md`** is the strategic destination — the *why* the
  cutover ladder bends toward V2-driver mode, the per-axis correctness
  stakes, the chapter ownership map. Slowest-rhythm strategic surface.
- **`BACKLOG.md`** (this document) is the operational ledger — *what
  is in flight, what is scheduled, what is blocked, what is shipped,
  and what is sunset*. Refreshed at every chapter close and at every
  V1 inheritance event (carbon-copy landing).
- Per-chapter pre-scope documents (`CHAPTER_<N>_PRESCOPE_*.md`,
  `CHAPTER_<N>_OPEN.md`) carry slice-level scope. The backlog
  cross-references; it does not duplicate.
- **`ADMIRE.md`** carries per-V1-component placement records, including
  per-component carbon-copy events. The backlog's V1 inheritance log
  is the cross-component operational view; ADMIRE entries are the
  per-component view.
- **`DECISIONS.md`** is the append-only resolved-questions log. The
  backlog cites DECISIONS by date + title.

This is the single document a contributor reads when they want to know
*what's next* in the V2 effort. The strategic *why* is in V2_DRIVER;
the operational *what and when* is here.

---

## Table of contents

- [I. Frame and operating principles](#i-frame-and-operating-principles)
- [II. Status vocabulary](#ii-status-vocabulary)
- [III. Per-phase backlog](#iii-per-phase-backlog)
  - [Phase 1 — Π port keystone (chapter 3.5)](#phase-1--π-port-keystone-chapter-35)
  - [Phase 2 — Schema-as-driver (chapter 4.1.A)](#phase-2--schema-as-driver-chapter-41a)
  - [Phase 3 — Data-as-driver (chapter 4.1.B)](#phase-3--data-as-driver-chapter-41b)
  - [Phase 3.1 — IR fidelity lifts (chapter A.0'; in flight)](#phase-31--ir-fidelity-lifts-chapter-a0-in-flight)
  - [Phase 4 — Identity-as-driver (chapter 4.2)](#phase-4--identity-as-driver-chapter-42)
  - [Phase 5 — Operational diagnostics (chapter 4.3)](#phase-5--operational-diagnostics-chapter-43)
  - [Phase 6 — DACPAC (chapter 3.x; conditional)](#phase-6--dacpac-chapter-3x-conditional)
  - [Phase 7 — SnapshotRowsets (chapter 3.2; closed)](#phase-7--snapshotrowsets-chapter-32-closed)
  - [Phase 8 — Pragmatic close](#phase-8--pragmatic-close)
- [IV. V1 inheritance log](#iv-v1-inheritance-log)
- [V. Cross-cutting infrastructure work](#v-cross-cutting-infrastructure-work)
- [VI. Risk register](#vi-risk-register)
- [VII. Sequencing graph](#vii-sequencing-graph)
- [VIII. Cutover gate condition](#viii-cutover-gate-condition)
- [IX. Lifecycle protocol and ownership](#ix-lifecycle-protocol-and-ownership)
- [X. Cross-references](#x-cross-references)

---

## I. Frame and operating principles

The backlog interweaves four dimensions in one document:

1. **Strategic chapters** (sourced from V2_DRIVER's chapter sequencing
   under V2-driver KPI). Each phase has a chapter identifier, scope
   statement, and gate criteria.
2. **Chapter slices** (the unit of execution at session cadence). Each
   pending slice has its scope, deliverables, witnesses, dependencies.
3. **V1 inheritance log** (the editorial inheritance ledger). Each
   carbon-copy event has its V1 source path, V2 location, date
   inherited, refactor status. The log is the canonical record of how
   V2 has absorbed V1.
4. **Cross-cutting infrastructure** (horizontal work that supports
   multiple chapters). Test infrastructure, fixture growth, F# helper
   refactors, documentation deltas at every chapter close.

Plus the risk register and the sequencing graph that tie everything
together.

**Operating principles:**

- **Append-mostly.** Items move through statuses; items rarely
  disappear. Cancellation requires a DECISIONS entry citing the
  rationale. Removal without rationale is forbidden.
- **Cross-references over duplication.** The backlog's job is to
  surface what's in flight, blocked, done — not to restate canonical
  commitments. When an item references a chapter pre-scope or a
  DECISIONS entry, the reference is the substantive content; the
  backlog row is the index.
- **Codify every event.** When V2 carbon-copies a V1 source into V2's
  tree, the V1 inheritance log gains a row in the same commit. When
  an item moves from `scheduled` to `in-flight`, the commit that
  started it is named. The backlog is the operational ledger.
- **Witness every claim.** Items declare exit criteria; the witnesses
  (property tests, equivalence relations, parse round-trips) are
  named. An item without a witness cannot move to `shipped`.
- **Per-phase reviews at chapter close.** Every chapter close walks
  this document, updates statuses, surfaces silent drift, refreshes
  cross-references. The chapter-close ritual gains a backlog-review
  dimension.

---

## II. Status vocabulary

Backlog items move through this fixed status set. Status transitions
are documented in commits; the transition itself is small (one line
change) but the audit trail compounds.

| Status | Meaning |
|---|---|
| `proposed` | Listed for consideration; no chapter has committed to it; can be discussed and refined without consequence |
| `scheduled` | A chapter has committed to it; the chapter's open or pre-scope document references it; estimated effort acknowledged |
| `blocked` | Scheduled but cannot start; the blocker is named (often another item by row index) |
| `in-flight` | Active work in the current session(s); the relevant commits are accumulating; the witness is not yet green |
| `shipped` | Work complete; witness green; the chapter close ritual has reviewed it |
| `sunset` | Item's purpose has ended; item retained for historical citation |
| `canceled` | Item explicitly canceled with DECISIONS rationale; item retained for historical citation |

A `blocked` item moves to `scheduled` when the blocker resolves; the
blocker reference is preserved as a citation but the status changes.

A `shipped` item rarely changes; if a regression breaks it, a new item
is added (not a status revert), citing the regression.

---

## III. Per-phase backlog

Each phase mirrors V2_DRIVER's chapter sequencing, expanded with the
chapter's pending slices, V1 inheritance opportunities, cross-cutting
work, and per-phase risks. Phases that have shipped are preserved for
historical reference; phases in flight or pending carry the operational
detail.

### Phase 1 — Π port keystone (chapter 3.5)

**Status:** substantially shipped. Chapter 3.5 closed 2026-05-09;
`Projection.Targets.SSDT.SsdtDdlEmitter`, JSON emitter, Distributions
emitter all surface typed `seq<Statement>` per A35; RefactorLog
round-trip + CatalogDiff smart constructor shipped.

**Strategic frame.** See V2_DRIVER.md Phase 1 + `CHAPTER_3_5_CLOSE.md`.
Π's output is a deterministic statement stream (A35); realization
layers (`Render.toText`, `Deploy.executeStream`) consume the stream
and choose their emission form; the algebra holds at the stream level.

**V1 inheritance opportunities:** none in this phase. Chapter 3.5's
emitters use ScriptDom's typed AST directly (the gold-standard library
per pillar 7); carbon-copying V1's text-based emission machinery
would violate the text-builder-as-first-instinct discipline.

**Cross-cutting work:** none chapter-3.5-specific; chapter 3.5's
contributions to the codebase (typed Statement DU, sibling-Π
commutativity T11 structurally encoded via `ArtifactByKind<'element>`,
writer-fidelity discipline) are inherited by subsequent chapters
without modification.

**Pending items:** none.

**Per-phase risks (residual):** sibling-Π drift if a future emitter
slips out of the `seq<Statement>` shape. Caught by structural-type
encoding (T11) at compile time. No active mitigation needed.

---

### Phase 2 — Schema-as-driver (chapter 4.1.A)

**Status:** substantially shipped (chapter 4.1.A close 2026-05-10).
SsdtDdlEmitter for production SSDT DDL emission; ManifestEmitter;
SsdtBundle composition; Tolerance taxonomy (M4); multi-environment
property test.

**Strategic frame.** V2_DRIVER.md Phase 2 + `CHAPTER_4_1_A_CLOSE.md`.
Production schema axis. The CDC-silence property test (Phase 3)
depends on this phase's SSDT emission being deterministic.

**V1 inheritance opportunities:** none in this phase. Pending slices
(cross-module FK, defaults, descriptions) are IR-shape concerns that
don't carbon-copy from V1 — V2's Catalog reconstruction from rowsets is
the source of truth.

**Pending slices (chapter 4.1.A close arc):**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| 6 | Cross-module FK IR refinement | property test asserts cross-module FK fixtures round-trip | deferred per Active deferrals index (`DECISIONS 2026-05-19`) |
| 7-default | `Attribute.Default : SqlLiteral option` + DEFAULT constraint emission | round-trip property | deferred per Active deferrals index (`DECISIONS 2026-05-11`) |
| 8 | `Kind.Description` + `Attribute.Description` + extended-properties emission | round-trip + per-property test | deferred per Active deferrals index (`DECISIONS 2026-05-11`) |

**Per-phase risks:** cross-module FK fixture surfacing the IR gap.
When chapter 4.1.A slice 6 trigger fires, the IR refinement opens as
a discrete slice.

---

### Phase 3 — Data-as-driver (chapter 4.1.B)

**Status:** α/β/γ shipped (chapter 4.1.B close 2026-05-11);
StaticSeedsEmitter v0, Profile.CdcAwareness + change-detection MERGE,
CDC-silence-on-idempotent-redeploy canary GREEN. Slices δ-θ pending.

**Strategic frame.** V2_DRIVER.md Phase 3. The CDC-silence property
test is the highest-leverage single deliverable in the entire chapter
sequence. Asserts zero records in `cdc.change_tables` after second
deploy on every CDC-tracked table at operator-reality canary scale.
See `CHAPTER_4_1_B_CLOSE.md` for shipped slices;
`CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md` for full pre-scope.

**Pending slices:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| δ | Two-phase insertion / DeferredFkSet refinement | property test asserts FK cycle resolution preserves order | scheduled |
| ε | MigrationDependenciesEmitter typed-AST adoption (full UPDATE shape) | round-trip; idempotent-redeploy | scheduled |
| ζ | BootstrapEmitter typed-AST adoption | round-trip; UserRemapContext slot wiring | scheduled (depends on Phase 4) |
| η | Partition assertion + per-partition CDC discovery | property test asserts partition coverage | scheduled |
| θ | Full data triumvirate close + chapter close ritual | every deferred slice closed; backlog refreshed | scheduled |

**V1 inheritance opportunities:**

- **CDC discovery plumbing** — V1's CDC-table reading logic (`DbTableChangeHelper` and related in `Osm.Pipeline.Cdc`) is a candidate carbon-copy if V2's slice η needs live CDC reads beyond fixture-driven property tests. Status: `proposed`. The chapter agent at slice η open decides whether to carbon-copy (verbatim or refactored) or to rewrite in F# using `Projection.Adapters.Sql.AsyncStream`. The F# rewrite is the default; carbon-copy is the fallback if the rewrite is materially harder.
- **MERGE generation** — V1's `PhasedDynamicEntityInsertGenerator` is a candidate carbon-copy if V2's MERGE typed-AST construction needs reference material. Status: `proposed`. V2's existing `StaticSeedsEmitter` uses `ScriptDomBuild.buildMergeStatement` directly per the chapter 4.1.B slice α cash-out; the typed-AST approach is preferred; carbon-copy is the fallback if a slice surfaces gaps in the typed-AST surface that V1's code addresses.
- **Static-seed FK ordering bug fix** — V1's `BuildSsdtStaticSeedStep.cs:82-86` sorts within static category only (cross-category FKs violate constraints); V2's `TopologicalOrderPass.SelfLoopPolicy` provides the correct global sort. This is a place where V2 has a *better* implementation than V1; no carbon-copy needed. V2's existing F# is the published form.

**Cross-cutting work in this phase:**

- CDC test fixture (per chapter pre-scope) — adds CDC-aware rows to
  the canary fixture so the CDC-silence property has real coverage.

**Per-phase risks:**

- *CDC-silence property failing on real production fixtures* — the
  highest-stakes risk of the entire cutover. Mitigation: chapter
  4.1.B slice θ runs the property at operator-reality canary scale
  (50K rows × 300 tables) per the perf-gate baseline. Disagreement
  blocks PR.
- *Carbon-copy of V1's MERGE generation* surfacing performance
  regression — V1's row-by-row patterns may not align with V2's
  streaming primitives. Mitigation: F# rewrite is the default; carbon-
  copy only if the slice agent finds the rewrite materially harder.

**Sequencing.** Independent of other phases. Estimated ~2-3 weeks at
session cadence (V2_DRIVER estimate; unchanged under the V2
self-containment posture).

---

### Phase 3.1 — IR fidelity lifts (chapter A.0'; CLOSED 2026-05-16)

**Status:** closed. All 9 slices (α + β + γ + δ + ε + ζ + η + θ + ι)
shipped. Ten L3 axioms advanced from Bucket D → Bucket A. Chapter-close
synthesis at `CHAPTER_A_0_PRIME_CLOSE.md`; chapter-open scope at
`CHAPTER_A_0_PRIME_OPEN.md` (preserved for historical reference).

**Strategic frame.** Each lift is structural commitment, not a feature
— the IR carries the evidence; emitter consumption lands downstream
per-consumer. Record-field extensions (closed-DU empirical-test
discipline holds — F# field-missing errors at literal sites only;
semantic interpretation untouched). Twin-path discipline holds (JSON +
rowset both populate every new field).

**All slices shipped:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α | `Kind.Description` + `Attribute.Description` (purely additive) | `DescriptionLiftTests.fs` (7 tests) | shipped 2026-05-15 |
| β | `Module.IsActive` + `Kind.IsActive` + `Attribute.IsActive` (carry-through; retire session-21 boundary filter); first pillar-9 worked example | `IsActiveCarryThroughTests.fs` (9 tests) + rework of 5 prior tests in `OsmRowsetReaderTests` / `OsmCatalogReaderDifferentialTests` | shipped 2026-05-16 |
| γ + δ + ε + ζ + η (XXXXL) | Five-slice IR-fidelity body: `Kind.Triggers` + `Trigger`; `Catalog.Sequences` + `Sequence`; `Attribute.DefaultValue` + `Computed` + `Kind.ColumnChecks`; `ExtendedProperties` × 4 levels; `ModalityMark.Temporal` DU widening | `IRFidelityLiftTests.fs` (22 tests) + `IRBuilders.fs` fixture-builder pattern + `Fixtures.fs` retrofitted | shipped 2026-05-16 |
| θ | `TableId.Catalog : string option` + JSON-path `db_catalog` pickup | 4 new tests in `IRFidelityLiftTests` | shipped 2026-05-16 |
| ι | IsExternal/Origin mapping audit + L3-Boundary-NoSilentDrop property test surface | `NoSilentDropTests.fs` (21 tests; 12 per-concept structural witnesses + 1 kitchen-sink JSON fixture + 8 Origin-audit tests) | shipped 2026-05-16 |

**Pending slices:** none. Chapter A.0' closes; see `CHAPTER_A_0_PRIME_CLOSE.md`.

**V1 inheritance opportunities:** none. The lifts are V2 IR extensions
under empirical pressure from V1's source shape; the V1 OSSYS adapter
already reads V1's projection. No carbon-copy event in this chapter.

**Cross-cutting work:**

- The A.4.7-prelude small slice (`LineageEvent.Classification` field)
  per `DECISIONS 2026-05-15 (late)` lands during or just after chapter
  close. Slices β and γ-η did not need it; chapter close gates may.
- **`IRBuilders.fs` fixture-builder pattern** (introduced 2026-05-16 at
  XXXXL close) — `mkAttribute` / `mkKind` / `mkModule` / `mkIndex` /
  `mkCatalog` with minimum-evidence DataIntent defaults. `Fixtures.fs`
  retrofitted as the worked example; the rest of the test surface is
  scheduled for retroactive sweep at chapter close (currently ~150
  record-literal sites carry field assignments explicitly; the sweep
  reduces blast radius for next-chapter field additions from 150 sites
  to 1). Pillar-8 / pillar-9 disciplines documented in
  `IRBuilders.fs` module-level docstring.

**Per-phase risks:**

- *Slice ζ blast radius* — ExtendedProperties on four IR levels is the
  widest record-extension in the chapter. Mitigation: mechanical-edits
  precedent from slices α/β (Python pass against FS0764 worklist).
- *Slice η DU widening* — `ModalityMark.Temporal` is the only DU-widening
  slice in the chapter. Mitigation: closed-DU empirical-test discipline
  (chapter 3.2 close generalization confirms record-extension and
  DU-widening have the same propagation profile).

**Sequencing.** Independent of Phases 3-5. Estimated 3-4 weeks total at
session cadence (V2 production cutover plan estimate); two slices
shipped at this baseline, 7 pending.

---

### Phase 4 — Identity-as-driver (chapter 4.2)

**Status:** not started. Pre-scope at
`CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`. Phase 4 of V2-driver KPI
critical path.

**Strategic frame.** V2_DRIVER.md Phase 4. User FK reflow across
environments. Every CreatedBy/UpdatedBy FK in target environment
must resolve to a valid target User. Per-strategy property tests
cover ByEmail, BySsKey, ManualOverride, FallbackToSystemUser.

**Pending slices:** TBD per chapter open (the pre-scope names α-η;
chapter open lists them).

**V1 inheritance opportunities:**

- **UserMatchingEngine** — V1's `Osm.Pipeline.UatUsers.UserMatchingEngine.Execute`
  implements the matching strategies. Status: `proposed` carbon-copy
  candidate. The matching algorithm is small (~400 LOC); F# rewrite
  via closed-DU strategy DU is the likely default (V2's algebraic
  posture favors F#); carbon-copy as fallback. The chapter agent
  decides at chapter open.

**Cross-cutting work:**

- User-FK equivalence fixture — covers ByEmail / BySsKey /
  ManualOverride / FallbackToSystemUser strategies and the
  disjointness invariant on `UserRemapContext.Mapping.Keys ∩
  Unmatched = ∅`.
- Multi-environment property test (T11 specialization) — same source
  population + ByEmail strategy against four distinct target
  populations yields four `UserRemapContext` values whose source-keyset
  agrees across all four.

**Per-phase risks:**

- *User-matching strategy DU exhaustiveness*. Mitigation: F#'s
  exhaustiveness checking; the closed DU's expansion is empirically
  tested.
- *Per-strategy property test coverage gap*. Mitigation: each strategy
  ships with its own property test; chapter close audits coverage.

**Sequencing.** Independent of other phases. Estimated ~1-1.5 weeks at
session cadence.

---

### Phase 5 — Operational diagnostics (chapter 4.3)

**Status:** not started. Pre-scope at
`CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md` Part 1. Phase 5 of
V2-driver KPI critical path.

**Strategic frame.** V2_DRIVER.md Phase 5. Three-channel Diagnostics
split (DecisionLogEmitter / OpportunitiesEmitter / ValidationsEmitter)
routing via Code-prefix table. See `CHAPTER_4_3_OPEN.md`.

**Pending slices:** TBD per chapter open.

**V1 inheritance opportunities:**

- **Diagnostic-finding generation** — V1's `SqlMetadataDiagnosticsWriter`
  and `BasicDataIntegrityChecker` emit `ValidationError` arrays. Status:
  `proposed` carbon-copy candidate. V2's `Diagnostics<T>` writer monad
  is the algebraic target; the carbon-copy would extract V1's specific
  finding-detection logic, refactored to emit V2-shaped `Finding`
  records. The chapter agent decides at chapter open.

**Cross-cutting work:**

- Per-channel routing property test — `Code` prefix → channel mapping
  is exhaustive; every diagnostic lands in exactly one channel.

**Per-phase risks:**

- *Routing correctness regression*. Mitigation: exhaustiveness property
  test asserts every emitted `Code` matches a prefix.

**Sequencing.** Independent of other phases. Estimated ~1.5-2 weeks at
session cadence.

---

### Phase 6 — DACPAC (chapter 3.x; conditional)

**Status:** not started; deploy-path-conditional. If operator's deploy
path requires DACPAC, this phase opens; otherwise defers indefinitely.

**Strategic frame.** V2_DRIVER.md Phase 6. DacFx adoption +
DacpacEmitter; A37 named erasure axes; T1 binary-normal-form amendment;
T11 sibling-Π commutativity at the structural level between SSDT and
DACPAC. See `CHAPTER_3_X_OPEN.md`.

**V1 inheritance opportunities (if pursued):**

- **DACPAC builder plumbing** — V1's `DacpacBuilder.CreateDacpac`
  wraps DacFx. Status: `proposed` carbon-copy candidate if the
  chapter opens. DacFx is C#-idiomatic; the carbon-copy would land
  in a new C# adapter project — `Projection.Adapters.Dac` — with
  museum-polish quality. Alternatively, V2 wraps DacFx directly from
  F# (the existing `Projection.Targets.SSDT` precedent for ScriptDom).
  The chapter agent decides at chapter open based on DACPAC fidelity
  requirements.
- **SMO emission plumbing** — V1's `SmoEntityEmitter` produces DDL via
  SMO. Status: `proposed` (only relevant if DACPAC parity requires
  SMO-style emission). The carbon-copy would land in
  `Projection.Adapters.OssysSql` or a new `Projection.Adapters.Smo`
  with museum-polish.

**Cross-cutting work:**

- A37 named erasure axes — Tolerance entries naming what DACPAC erases.
- T1 binary-normal-form amendment.

**Per-phase risks:**

- *SMO/DacFx lifecycle wrapping complexity*. Mitigation: the carbon-
  copy lands behind a museum-polish C# adapter; lifecycle stays
  scoped per call; F# adapter consumes via `Result<'a>`.
- *DacFx version compatibility with V2's existing pins*. Mitigation:
  V2's existing csproj pins are reviewed at chapter open.

**Conditional opening.** Phase 6 opens only if operator's deploy path
requires DACPAC. Independent of Phases 3-5; can run in parallel or
sequentially per chapter-cadence preference.

---

### Phase 7 — SnapshotRowsets (chapter 3.2; closed)

**Status:** shipped (chapter 3.2 close 2026-05-10). Five slices
shipped: SnapshotRowsets variant + RowsetBundle; reference rowsets;
EspaceKind activation; IsSystemEntity → ModalityMark.SystemOwned;
cross-source parity. A1's JSON-projection-lossiness bound structurally
resolved.

**Strategic frame.** V2_DRIVER.md Phase 7. See `CHAPTER_3_2_CLOSE.md`.

**V1 inheritance opportunities:** the OSSYS catalog producer (V1's
metadata-extraction chain — `outsystems_metadata_rowsets.sql` +
`MetadataSnapshotRunner` + `SnapshotJsonBuilder` + result-set processor
chain) is the highest-value carbon-copy candidate **but is not
consumed by chapter 3.2 itself**. The carbon-copy lands at the chapter
that introduces V2's live-SQL-extraction source variant (currently
deferred per Active deferrals — `LiveOssysConnection` variant
reserved in `CatalogReader.fs:58-63`).

**Pending items:** none open. Future class members (per-table column
structure rowset 6; check constraints rowset 7; triggers rowset 18)
remain deferred per Active deferrals index until fixture pressure
surfaces them.

---

### Phase 8 — Pragmatic close

**Status:** indefinite cadence; consumer-pressure-driven.

**Strategic frame.** V2_DRIVER.md Phase 8 + `CHAPTER_5_OPEN.md`.
F# Analyzers SDK custom analyzers; Coordinates Stage 2 typed VOs;
operator runbook; V1 sunset planning.

**Pending items:**

| Slice | Scope | Status |
|---|---|---|
| ν | F# Analyzers SDK custom analyzer (proof-of-concept rule) — generalizes `NoUnsafeTimeInCoreAnalyzer.fs` pattern | scheduled (consumer-pressure-driven) |
| θ | Coordinates Stage 2 typed `SchemaName` / `TableName` / `ColumnName` VOs | deferred (per `DECISIONS 2026-05-11`; empirical adapter-ripple justification required) |
| Operator runbook | Cutover-day procedures; V1 sunset checklist | scheduled (cutover-30 onward) |

**V1 inheritance opportunities:**

- **OSSYS catalog producer** — the highest-value carbon-copy candidate
  in the entire wave. Lands at the chapter that introduces V2's
  live-SQL-extraction source variant. The carbon-copy goes into a new
  `Projection.Adapters.OssysSql` C# project (museum-polish) and a
  vendored copy of `outsystems_metadata_rowsets.sql` (the SQL is the
  truth; copied verbatim). Status: `proposed`. Trigger: the chapter
  agent opens chapter 5+ slice for V2's live-SQL-extraction.

**Cross-cutting work:**

- Operator runbook section naming V1 sunset criteria (the three
  conditions — see § VIII below).

**V1-side sunset.** Per `CLAUDE.md` operating-disciplines table, V1
sunset is a condition, not a deadline. The three conditions are
formalized in § VIII.

**Per-phase risks:**

- *Sunset deferred indefinitely*. Mitigation: chapter close ritual at
  every Phase 1-5 close evaluates whether the cutover gate is reachable
  at expected cadence.

---

## IV. V1 inheritance log

This section is the canonical record of carbon-copy events — every V1
source file that has landed in V2's tree, with V1 source path, V2
location, date inherited, refactor status. Sibling to the per-component
ADMIRE entries: ADMIRE is the *per-V1-component* view; this log is the
*cross-component operational* view.

**Status: empty.** No V1 source has been carbon-copied into V2 as of
this writing (the V2 self-containment audible codified the discipline;
the first carbon-copy event lands at the chapter that wants V1's first
inheritance opportunity — most likely chapter 4.1.B slice η, chapter
4.2, chapter 3.x, or chapter 5+ depending on operator scheduling).

**Format for future entries:**

```
### YYYY-MM-DD — <V1 capability name> (chapter <chapter>; slice <slice>)

**V1 source paths (at the V1 head V2 inherited from):**
- `src/...`
- `src/...`

**V2 location:**
- `sidecar/projection/src/...` (or new C# adapter project name)

**Refactor status:** verbatim / partially refactored / fully refactored

**Carbon-copy summary:** one paragraph.

**File-header citation comments:** each carbon-copied file carries a
one-time citation comment naming this row.

**ADMIRE entry updated:** link to the ADMIRE entry for the V1 component.
```

**Anticipated carbon-copy candidates** (`proposed` status; await
chapter-agent decision):

| V1 capability | V1 source area | Anticipated V2 location | Anticipated refactor | Chapter |
|---|---|---|---|---|
| OSSYS catalog producer (SQL extraction chain) | `src/AdvancedSql/outsystems_metadata_rowsets.sql` + `src/Osm.Pipeline/SqlExtraction/*` | `Projection.Adapters.OssysSql` (new C# project, museum-polish) | partial — SQL verbatim, C# plumbing refactored for V2 vocabulary | Phase 8 / chapter 5+ live-SQL slice |
| UniqueIndex evidence aggregator | `src/Osm.Validation/Tightening/UniqueIndexEvidenceAggregator.cs` | extend `Projection.Core.Strategies.UniqueIndexRules` (F# rewrite) OR new C# adapter (carbon-copy) | F# rewrite preferred | (consumer-dependent) |
| User-matching engine | `src/Osm.Pipeline/UatUsers/UserMatchingEngine.cs` | `Projection.Core.Strategies.UserMatching` (F# rewrite likely) | F# rewrite preferred | chapter 4.2 |
| CDC discovery plumbing | `src/Osm.Pipeline/Cdc/*` | `Projection.Adapters.Sql` (F# rewrite via `AsyncStream`) OR new C# adapter (carbon-copy) | F# rewrite preferred | chapter 4.1.B slice η |
| DACPAC builder plumbing | `src/Osm.Smo/DacpacBuilder.cs` | `Projection.Adapters.Dac` (new C# project, museum-polish) OR F# wrapper | C# carbon-copy likely (DacFx C#-idiomatic) | Phase 6 (conditional) |
| Diagnostic-finding generation | `src/Osm.Pipeline/SqlExtraction/SqlMetadataDiagnosticsWriter.cs`, `src/Osm.Pipeline/Orchestration/BasicDataIntegrityChecker.cs` | extend `Projection.Core.Diagnostics` (F# rewrite) | F# rewrite preferred | Phase 5 |

**Anticipated non-candidates** (V1 capabilities V2 explicitly does NOT
plan to inherit — V2 has a better algebraic alternative; mental-model
trap; or no V2 consumer demand):

| V1 capability | V1 source | Reason | Where V2 lands instead |
|---|---|---|---|
| NullabilityEvaluator | `src/Osm.Validation/Tightening/NullabilityEvaluator.cs` | mode-bound policy front-to-back; no clean separation | V2's `Projection.Core.Strategies.NullabilityRules` (already shipped) |
| ForeignKeyEvaluator | `src/Osm.Validation/Tightening/ForeignKeyEvaluator.cs` | post-split evidence duplicates V2 IR fields | V2's `Projection.Core.Strategies.ForeignKeyRules` (already shipped) |
| EntityDependencySorter | `src/Osm.Emission/Seeds/EntityDependencySorter.cs` | V2 has the same algorithm parameterized via A40 harmonization | V2's `Projection.Core.Passes.TopologicalOrderPass.SelfLoopPolicy` (already shipped) |
| Seven scattered override-binding mechanisms | `src/Osm.Pipeline/Application/NamingOverridesBinder.cs` + six others | V1 mental-model trap; V2 has a single structured builder | V2's `OverrideBindingContext` (F# typed builder) |

The non-candidates are documented to make the editorial discipline
visible. V2 explicitly chooses not to inherit them; the choices are
deliberate, not omissions.

---

## V. Cross-cutting infrastructure work

Items that span multiple chapters; tracked here so the horizontal
visibility holds.

**Test infrastructure growth:**

| Test | Scope | Status |
|---|---|---|
| Per-strategy property tests | Each strategy (Nullability, UniqueIndex, ForeignKey, CategoricalUniqueness, UserMatching) has exhaustive property coverage | grows as strategies land |
| Multi-environment commutativity (T11 specialization) | Per chapter that introduces an environment-sensitive pass (chapter 4.2, possibly others) | grows as passes land |
| Carbon-copy equivalence tests | When a V1 carbon-copy lands, the test asserts V2's behavior matches V1's reference on shared fixtures | grows as carbon-copies land |

**Fixture growth:**

| Fixture | Scope | Status |
|---|---|---|
| CDC-aware canary rows | Chapter 4.1.B slice η scope | scheduled |
| User-FK fixtures (per strategy) | Chapter 4.2 scope | scheduled |
| Operator-reality canary growth | Per V2_DRIVER's canary-as-load-bearing-forcing-function discipline (CLAUDE.md operating disciplines) | ongoing |

**Documentation deltas at every chapter close:**

| Document | Update | Cadence |
|---|---|---|
| `ADMIRE.md` | Per-V1-component entry's carbon-copy log updated at every chapter close that touches the component | every chapter close |
| `HANDOFF.md` | Top entry refreshed at every chapter close | every chapter close |
| `DECISIONS.md` | Codifying entry for any new discipline; carbon-copy events cited | every chapter close |
| `BACKLOG.md` (this document) | Status transitions; new items added; canceled items annotated; V1 inheritance log rows added | every chapter close + every significant slice |
| `V2_DRIVER.md` | Chapter sequencing updates if a phase's scope shifts | rare (only on strategic reframe) |

---

## VI. Risk register

V2's wave-wide risks. Per-phase risks are listed in each phase's
section above; this register catches the horizontal ones.

### R1: CDC-silence property failing on production fixtures

- **Failure mode:** Chapter 4.1.B's CDC-silence-on-idempotent-redeploy
  property fails on a real fixture (rows appearing in
  `cdc.change_tables` on second deploy).
- **Early-warning signal:** Operator-reality canary failure at scale
  (50K rows × 300 tables); perf-gate or property-gate blocks PR.
- **Mitigation:** Chapter 4.1.B slice θ runs the property at canary
  scale; disagreement blocks PR. The property is V2-driver KPI's
  highest-stakes deliverable; the mitigation is structural.
- **Owner:** chapter 4.1.B agent.
- **Status:** active.

### R2: User-FK reflow correctness regression

- **Failure mode:** Chapter 4.2's UserFkReflowPass produces incorrect
  `UserRemapContext` (Mapping/Unmatched disjointness violated, missing
  source User keys, etc.).
- **Early-warning signal:** Per-strategy property tests fail; T11
  multi-environment commutativity fails.
- **Mitigation:** F#'s exhaustiveness on `UserMatchingStrategy` closed
  DU; smart-constructor invariant on `UserRemapContext`; chapter close
  audits per-strategy coverage.
- **Owner:** chapter 4.2 agent.
- **Status:** active when chapter 4.2 opens.

### R3: Diagnostics routing correctness

- **Failure mode:** A diagnostic emits a `Code` prefix not in the
  routing table; silent drop.
- **Early-warning signal:** Exhaustiveness property test fails.
- **Mitigation:** Per-channel routing property test asserts every
  emitted `Code` matches a prefix.
- **Owner:** chapter 4.3 agent.
- **Status:** active when chapter 4.3 opens.

### R4: V1 evolution divergence (post-carbon-copy)

- **Failure mode:** V1 maintainers fix a bug in V1's trunk source
  after V2 has carbon-copied; V2's copy continues to exhibit the bug.
- **Early-warning signal:** V1 maintainer reports the fix; V2 evaluates
  whether to carbon-copy again or document the divergence.
- **Mitigation:** Carbon-copy events are recorded in this BACKLOG +
  ADMIRE + file-header citations. V2 either carbon-copies again
  (citing the V1 fix in DECISIONS) or leaves the divergence (citing
  the divergence rationale in DECISIONS). The relationship between
  V1's trunk source and V2's carbon-copy is editorial.
- **Owner:** V2 chapter agent for the affected component.
- **Status:** active (no incidents through this audible).

### R5: Carbon-copy of V1 code surfacing V1's mental-model traps

- **Failure mode:** A carbon-copy lands verbatim; V1's mental-model
  traps (string-everywhere config, exception-driven control flow,
  mutation-as-default) leak into V2's tree and outlast the carbon-copy
  event.
- **Early-warning signal:** Code review at chapter close surfaces the
  traps; lint discipline flags suspicious patterns.
- **Mitigation:** Carbon-copy events declare refactor status
  (`verbatim` / `partially refactored` / `fully refactored`). `verbatim`
  status carries a follow-up commitment (a subsequent commit refactors
  to V2 idioms). The status is tracked in the V1 inheritance log; a
  verbatim copy that lingers as `verbatim` past chapter close is a
  drift signal.
- **Owner:** V2 chapter agent for the carbon-copy.
- **Status:** active.

### R6: V2's museum-polish C# layer drifting toward sprawl

- **Failure mode:** New C# adapter projects accumulate beyond what the
  language-role partition justifies (the layer was meant to be small
  and focused on irreducibly-C#-idiomatic libraries).
- **Early-warning signal:** Chapter close ritual surfaces a new C#
  project; reviewer asks whether the project is genuinely museum-
  polish-justified or whether F# rewrite is the better path.
- **Mitigation:** Every new C# adapter project requires DECISIONS
  rationale at chapter open. The default is F#; C# is the exception
  requiring explicit justification.
- **Owner:** chapter agent + operator review.
- **Status:** active.

### R7: Wave indefinite extension (cutover gate unreachable)

- **Failure mode:** Chapter sequence extends past V2-driver KPI's
  cutover estimate; V1 sunset deferred indefinitely.
- **Early-warning signal:** Chapter close ritual at every Phase 1-5
  close evaluates whether the cutover gate is reachable at expected
  cadence; V2_DRIVER's chapter sequencing surfaces slippage.
- **Mitigation:** The operator-side fallback ladder (R6 governance,
  T-30 / T-15 gates) determines whether V2-augmented becomes the
  sustained mode rather than V2-driver if the cadence demands.
- **Owner:** operator.
- **Status:** active.

---

## VII. Sequencing graph

```
                  ┌──────────────────────────────────────┐
                  │  V2 sidecar — current state          │
                  │  Phases 1, 2, 7 substantively shipped│
                  │  Pillar 9 + supreme operating        │
                  │  disciplines in place                │
                  └────────────────┬─────────────────────┘
                                   │
       ┌───────────────────────────┼───────────────────────────┐
       │                           │                           │
       ▼                           ▼                           ▼
┌──────────────────┐      ┌──────────────────┐        ┌──────────────────┐
│ Phase 3 — Data   │      │ Phase 4 — Identity│        │ Phase 5 — Diag.  │
│ (chapter 4.1.B)  │      │ (chapter 4.2)     │        │ (chapter 4.3)    │
│ ~2-3 weeks       │      │ ~1-1.5 weeks      │        │ ~1.5-2 weeks     │
│ CDC silence is   │      │ UserFkReflow      │        │ Three-channel    │
│ load-bearing     │      │ multi-env T11     │        │ routing          │
└────────┬─────────┘      └────────┬─────────┘        └────────┬─────────┘
         │                         │                            │
         │  (chapter 4.1.B slice ζ │                            │
         │   depends on Phase 4    │                            │
         │   UserRemapContext)     │                            │
         │                         │                            │
         └─────────────────────────┴────────────────────────────┘
                                   │
                                   ▼
                  ┌──────────────────────────────────────┐
                  │  V2-driver KPI critical path         │
                  │  operationally complete              │
                  └────────────────┬─────────────────────┘
                                   │
                                   ▼ (conditional)
                  ┌──────────────────────────────────────┐
                  │  Phase 6 — DACPAC (chapter 3.x)      │
                  │  ~2.5 weeks (if pursued)             │
                  │  Conditional on deploy path          │
                  └────────────────┬─────────────────────┘
                                   │
                                   ▼
                  ┌──────────────────────────────────────┐
                  │  Cutover (~late July 2026)           │
                  │  V2-driver or V2-augmented per pair  │
                  │  V1 stays warm through cutover+30    │
                  └────────────────┬─────────────────────┘
                                   │
                                   ▼
                  ┌──────────────────────────────────────┐
                  │  cutover+30 — Phase 8 / V1 sunset    │
                  │  Three-condition gate (§ VIII)       │
                  └──────────────────────────────────────┘
```

**Parallelization notes:**

- Phases 3, 4, 5 are independent at the chapter level. They can run in
  parallel or sequentially per chapter-cadence preference. The one
  dependency is chapter 4.1.B slice ζ (BootstrapEmitter), which
  depends on chapter 4.2's `UserRemapContext`.
- Phase 6 (DACPAC) is conditional; opens only if operator's deploy
  path requires DACPAC.
- Phase 8 (pragmatic close + cutover+30 gate) depends on every prior
  phase closing first; runs in the cutover-15 to cutover+30 window.

**Critical-path estimate:** ~5-6 weeks of focused work spread across
the operator's cutover timeline (assuming chapters 3, 4, 5 in
parallel where staffing permits; sequential cadence extends the
timeline).

---

## VIII. Cutover gate condition

V1 sunset begins administratively when three conditions hold:

### Condition 1: V2 production stability

V2 emissions in every environment for at least one full
schema-evolution cycle without canary divergence.

- **Witness:** R6 governance protocol (per `DECISIONS 2026-05-22`);
  N=10 consecutive green canary runs across the pair-environment
  matrix.
- **Owner:** operator + V2 chapter agent.
- **Status:** evaluated at cutover-day; held through cutover+30 soak.

### Condition 2: V2 self-containment verified

V2 has no runtime dependency on V1's trunk. The build is V1-
independent; no `ProjectReference` to any `Osm.*` csproj; no V1
assembly on V2's classpath.

- **Witness:** the `Projection.sln` and every fsproj/csproj show no
  ProjectReference to V1's trunk; the build succeeds without V1's
  csprojs present. Verified by construction at this audible's
  codification; held automatically through subsequent commits because
  the V2 self-containment commitment is operating-discipline tier.
- **Owner:** Bridge-wave-style architect / chapter agents; verified at
  every chapter close.
- **Status:** verified at the 2026-05-16 audible's codification.

### Condition 3: Operator sign-off

The operator has authorized V1 sunset.

- **Witness:** DECISIONS entry recording the sign-off, the date, the
  conditions verified.
- **Owner:** operator.
- **Status:** at operator's discretion when Conditions 1-2 hold.

When all three conditions hold, V1's trunk source can be archived. The
csprojs under `src/Osm.*` no longer need to build; the test projects
no longer need to run; the CI pipelines no longer need to exercise
V1's emission path. V1 sunset is administratively complete.

The cutover+30 gate is a condition, not a deadline event. The calendar
of cutover+30 is a guideline for when the conditions are *expected* to
hold; the conditions themselves are the gate.

---

## IX. Lifecycle protocol and ownership

### Backlog item lifecycle

```
proposed ───────────────────────► canceled (with DECISIONS rationale)
   │
   ▼
scheduled ─────► blocked (with named blocker)
   │                 │
   │                 ▼ (when blocker resolves)
   ▼            scheduled
in-flight
   │
   ▼
shipped ────────────────────────► sunset (when purpose ends)
```

Status transitions are documented in commits. The transition itself
is a one-line change (status field update); the audit trail compounds
across the chapter sequence. Status reverts are forbidden — a
regression on a `shipped` item is a new item, not a revert.

### Ownership

- **Chapter agent (per chapter):** owns the chapter's slice scope,
  the V1 inheritance opportunities the chapter consumes, the chapter's
  cross-cutting work, the chapter's per-phase risks. Per-phase
  authority.
- **Operator:** owns the cutover decision, the V2-driver-mode
  authorization per pair, the V1 sunset sign-off, the strategic
  scope decisions (e.g., whether Phase 6 opens).
- **V1 maintainer:** owns V1's trunk source, V1's evolution, V1's
  adoption of any V2 capabilities V2 chooses to surface (no V2-for-V1
  surface today). Independent of V2's chapter rhythm.

### Backlog refresh cadence

- **At every chapter close:** status transitions; new items added;
  canceled items annotated; cross-references resolved; V1 inheritance
  log rows added.
- **At every significant slice landing:** in-flight statuses updated;
  commit references attached.
- **At chapter open:** the chapter's items move from `scheduled` to
  `in-flight` for the slices it commits to.
- **At operator strategic reframe:** Phase scope shifts; V2_DRIVER
  amendments cascade into BACKLOG.

### Backlog drift detection

The chapter-close ritual (per `DECISIONS 2026-05-14`) gains a
backlog-review dimension:

- Walk every item with status `in-flight` and confirm the commits
  match.
- Walk every item with status `scheduled` and confirm the chapter
  open document references it.
- Walk every item with status `blocked` and confirm the blocker is
  still active.
- Walk the V1 inheritance log and confirm carbon-copy event rows
  match the codebase state.

Drift detection is the structural complement to the chapter-mid
audit (per `DECISIONS 2026-05-19`).

---

## X. Cross-references

### Strategic surfaces

- **`V2_DRIVER.md`** — destination KPI and chapter sequencing under
  V2-driver mode. The strategic *why*; this backlog is the
  operational *what and when*.
- **`VISION.md`, `SPINE.md`, `PLAYBOOK.md`, `STAGING.md`** — V2's
  strategic frame surfaces; cited at chapter scope.

### Chapter documents

- `CHAPTER_3_5_CLOSE.md` — Π port keystone (Phase 1).
- `CHAPTER_4_1_A_CLOSE.md` — Schema-as-driver (Phase 2).
- `CHAPTER_4_1_B_CLOSE.md` + `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md`
  — Data-as-driver (Phase 3).
- `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` — Identity-as-driver (Phase 4).
- `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md` — Diagnostics
  (Phase 5).
- `CHAPTER_3_X_OPEN.md` — DACPAC (Phase 6, conditional).
- `CHAPTER_3_2_CLOSE.md` — SnapshotRowsets (Phase 7, closed).
- `CHAPTER_5_OPEN.md` — Pragmatic close (Phase 8).
- `CHAPTER_A_0_PRIME_OPEN.md` — IR fidelity lifts (in flight; slice β
  resumes after the 2026-05-16 audible).

### Per-component records

- **`ADMIRE.md`** — every V1 component's V2 placement record. Per the
  2026-05-16 audible's format amendment, entries with placement
  "carbon-copied" carry a carbon-copy log naming the V1 source path,
  V2 location, date inherited, refactor status, and citation comment.

### Decision log

- **`DECISIONS.md`** — append-only. Cite by date + title:
  - `DECISIONS 2026-05-16 (later) — V2 self-containment + carbon-copy
    editorial inheritance (Bridge wave audible)` (the operative
    codification of the V2-as-self-contained discipline)
  - `DECISIONS 2026-05-16 — Bridge wave: V2 inherits from V1`
    (SUPERSEDED; preserved as historical record of the rejected
    direction)
  - `DECISIONS 2026-05-22 — R6: Split-brain governance rule`
  - `DECISIONS 2026-05-22 — T-30 / T-15 cutover fallback ladder gates`
  - `DECISIONS 2026-05-10 — V2-driver as destination KPI`
  - `DECISIONS 2026-05-15 (late) — Pillar 9: harvest-dichotomy`
  - `DECISIONS 2026-05-13 — IR grows under evidence, not speculation`

### Axiom surfaces

- **`AXIOMS.md`** — formal axiom set. A18 amended (Π never consumes
  Policy); A40 (Harmonization-via-parameterization); T1 (byte-for-byte
  determinism); T11 (Sibling-Π commutativity); A41 candidate
  (TransformRegistry totality).
- **`PRODUCT_AXIOMS.md`** — L3 product axioms; the operator's promise.

### Audit surfaces

- `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` — L1↔L2↔L3 coverage
  audit; per-axiom delivery matrix.
- `AUDIT_2026_05_DDD_HEXAGONAL_FP.md` — chapter-3.1 close five-agent
  audit; Tier 1/2/3/4 epistemic backlog.

### Production cutover plan

- **`V2_PRODUCTION_CUTOVER.md`** — Draft 3 plan. §13.X carries the
  V2 self-containment addendum.

---

This backlog is canonical. The integrated operational surface —
V2-driver chapter sequence + V1 inheritance log + cross-cutting
infrastructure + risk register — lives here.

Refreshed at every chapter close. Held under the chapter-close ritual.
Drift caught at chapter-mid audit. Append-mostly. Owned across the
chapter agent + operator + V1 maintainer triad.

Hold the spine.
