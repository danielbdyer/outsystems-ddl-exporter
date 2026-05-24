# BACKLOG — Operational backlog for V2 cutover

**Status:** comprehensively re-canonicalized 2026-05-18 (post-chapter-5 audit-wave close). Sibling to `V2_DRIVER.md`. The operational ledger interweaving V2_DRIVER's per-phase chapter sequence with V1 parity audit (chapter 5 wave), parity cash-out implementation wave (Phase 5.13), path to T-30 green (Phase 8), V2-driver operator workflow (Phase 9), V1 sunset (Phase 10), cross-cutting infrastructure work, lead-up refactors, and the risk register.

**Current state as of 2026-05-18 (post-chapter-5 audit-wave close).** V2-driver critical-path Phases 1–7 + 5.0–5.12 all closed end-to-end. **Chapter 5 V1 Parity Audit Wave** (chapters 5.1–5.8) shipped 23 sequential slice commits + 185 matrix rows + 22 DECISIONS entries + 26 parity-test files + 3 synthesis documents (`V1_ARCHITECTURE_COMPENDIUM.md` + `V2_PATTERNS_COMPENDIUM.md` + `CUTOVER_READINESS_BRIEF.md`).

**V2-driver readiness (per CUTOVER_READINESS_BRIEF.md):**
- 🟢 V2-DRIVER-READY: SCHEMA + PIPELINE-ORCHESTRATION (per-pair flip eligible today)
- 🟡 V2-AUGMENTED: DATA (gating chapter 4.1.B) + IDENTITY (gating chapter 4.2 slice ε + UAT dry-run) + DIAGNOSTICS (soft gate; operator-tolerant) + OPERATOR-AFFORDANCE (per-verb gating)

**Three blocking deliverables for T-30 green:**
1. Chapter 4.1.B CDC-silence property test + global Phase1/Phase2 cross-emitter ordering (DATA axis)
2. Chapter 4.2 slice ε remaining matching strategies (IDENTITY axis)
3. ≥1 full UAT dry-run on real inventory CSVs (IDENTITY confirmation)

See § VII Sequencing graph for the current fan-out and Phase 8 for the structured path.

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
  - [Phase 3 — Data-as-driver (chapter 4.1.B; closed)](#phase-3--data-as-driver-chapter-41b-closed-2026-05-11)
  - [Phase 3.1 — IR fidelity lifts (chapter A.0'; closed)](#phase-31--ir-fidelity-lifts-chapter-a0-closed-2026-05-16)
  - [Phase 4 — Identity-as-driver (chapter 4.2; closed)](#phase-4--identity-as-driver-chapter-42-closed-2026-05-15)
  - [Phase 5 — Operational diagnostics (chapter 4.3; closed)](#phase-5--operational-diagnostics-chapter-43-closed-structural-slice-arc)
  - [Phase 5.5 — Manifest diagnostic fields (chapter 4.4; closed)](#phase-55--manifest-diagnostic-fields-chapter-44-closed-2026-05-17)
  - [Phase 5.6 — Index IR fidelity (chapter 4.5; closed)](#phase-56--index-ir-fidelity-chapter-45-closed-2026-05-17)
  - [Phase 5.7 — Forward-signal cleanup bundle (chapter 4.6; closed)](#phase-57--forward-signal-cleanup-bundle-chapter-46-closed-2026-05-17)
  - [Phase 5.8 — Refactor bundle + sibling-wrapper discipline (chapter 4.7; closed)](#phase-58--refactor-bundle--sibling-wrapper-discipline-chapter-47-closed-2026-05-17)
  - [Phase 5.9 — IRBuilders Attribute sweep + on-disk Index metadata + isPlatformAuto emitter toggle (chapter 4.8; closed)](#phase-59--irbuilders-attribute-sweep--on-disk-index-metadata--isplatformauto-emitter-toggle-chapter-48-closed-2026-05-17)
  - [Phase 5.10 — Big-batch forward-signal close-out + WithDiagnostics extensions (chapter 4.9; closed)](#phase-510--big-batch-forward-signal-close-out--withdiagnostics-extensions-chapter-49-closed-2026-05-17)
  - [Phase 5.11 — OSSYS catalog producer carbon-copy (chapter 5.0; closed)](#phase-511--ossys-catalog-producer-carbon-copy-chapter-50-closed-2026-05-17)
  - [Phase 5.12 — V1 Parity Audit Wave (chapter 5.1–5.8; AUDIT WAVE CLOSED 2026-05-18)](#phase-512--v1-parity-audit-wave-chapter-5158-audit-wave-closed-2026-05-18)
  - [Phase 5.13 — Parity cash-out implementation wave (in-flight; triggered per row)](#phase-513--parity-cash-out-implementation-wave-in-flight-triggered-per-row)
  - [Phase 6 — DACPAC dev-tooling (chapter 3.x; closed)](#phase-6--dacpac-dev-tooling-chapter-3x-closed-under-reframe)
  - [Phase 7 — SnapshotRowsets (chapter 3.2; closed)](#phase-7--snapshotrowsets-chapter-32-closed)
  - [Phase 8 — Path to T-30 green (the cutover-ladder gate)](#phase-8--path-to-t-30-green-the-cutover-ladder-gate)
  - [Phase 9 — V2-driver operator workflow (post-T-30; cutover-window operator UX)](#phase-9--v2-driver-operator-workflow-post-t-30-cutover-window-operator-ux)
  - [Phase 10 — V1 sunset (cutover+30 onward)](#phase-10--v1-sunset-cutover30-onward)
  - [Phase 11 — Transfer / bidirectional pipeline (epic; prescoped)](#phase-11--transfer--bidirectional-pipeline-epic-prescoped)
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

### Phase 3 — Data-as-driver (chapter 4.1.B; CLOSED 2026-05-11)

**Status:** closed end-to-end. All slices α through κ shipped:
StaticSeedsEmitter v0 (α); Profile.CdcAwareness + change-detection
MERGE (β); CDC-silence-on-idempotent-redeploy canary GREEN (γ); two-phase
insertion / cycle-breaking + DeferredFkSet + ScriptDomBuild.buildUpdateStatement
(δ); MigrationDependenciesEmitter (ε); BootstrapEmitter + UserRemapContext
placeholder (ζ); DataEmissionComposer + EmissionPolicy.DataComposition
DU (η); EmitError.OverlappingEmitterCoverage + partition assertion (θ);
multi-kind global Phase-1-then-Phase-2 reification + typed Values lift
(ι/κ). Tier-3 hard-requirement deferral cashed at slice ε
(`ScriptDomBuild.buildMergeStatement` adopted in MigrationDependenciesEmitter).

**Strategic frame.** V2_DRIVER.md Phase 3. The CDC-silence property
test is the highest-leverage single deliverable in the entire chapter
sequence. Asserts zero records in `cdc.change_tables` after second
deploy on every CDC-tracked table at operator-reality canary scale.
See `CHAPTER_4_1_B_CLOSE.md` for shipped slices;
`CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md` for full pre-scope.

**All slices shipped:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α | StaticSeedsEmitter v0 (V1-shape MERGE) | idempotent under repeat invocation; T1 byte-deterministic | shipped 2026-05-11 |
| β | Profile.CdcAwareness + change-detection MERGE predicate | per-kind dispatch on CdcAwareness | shipped 2026-05-11 |
| γ | CDC-silence-on-idempotent-redeploy canary (chapter signature) | `cdc.<schema>_<table>_CT` row count = 0 after redeploy | shipped 2026-05-11 |
| δ | Two-phase insertion / DeferredFkSet + ScriptDomBuild.buildUpdateStatement | property test asserts FK cycle resolution preserves order | shipped (chapter 4.1.B close arc) |
| ε | MigrationDependenciesEmitter typed-AST adoption (Tier-3 cash-out) | round-trip; idempotent-redeploy | shipped (chapter 4.1.B close arc) |
| ζ | BootstrapEmitter v0 + UserRemapContext slot | structural stub; T11 preserved | shipped (chapter 4.1.B close arc) |
| η | DataEmissionComposer + EmissionPolicy.DataComposition DU | composer dispatch; A18 amended preserved | shipped (chapter 4.1.B close arc) |
| θ | EmitError.OverlappingEmitterCoverage + partition assertion | composer asserts no two emitters cover the same kind | shipped (chapter 4.1.B close arc) |
| ι | DataInsertScript.RenderedPhase1/Phase2 split + composeRendered | global Phase-1-then-Phase-2 GO-batched output | shipped (chapter 4.1.B close arc) |
| κ | DataInsertRow.Values : Map<Name, SqlLiteral> typed lift | pillar 1 holds at the row level | shipped (chapter 4.1.B close arc) |

**V1 inheritance opportunities:**

- **CDC discovery plumbing** — V1's CDC-table reading logic (`DbTableChangeHelper` and related in `Osm.Pipeline.Cdc`) is a candidate carbon-copy if V2's slice η needs live CDC reads beyond fixture-driven property tests. Status: `proposed`. The chapter agent at slice η open decides whether to carbon-copy (verbatim or refactored) or to rewrite in F# using `Projection.Adapters.Sql.AsyncStream`. The F# rewrite is the default; carbon-copy is the fallback if the rewrite is materially harder.
- **MERGE generation** — V1's `PhasedDynamicEntityInsertGenerator` is a candidate carbon-copy if V2's MERGE typed-AST construction needs reference material. Status: `proposed`. V2's existing `StaticSeedsEmitter` uses `ScriptDomBuild.buildMergeStatement` directly per the chapter 4.1.B slice α cash-out; the typed-AST approach is preferred; carbon-copy is the fallback if a slice surfaces gaps in the typed-AST surface that V1's code addresses.
- **Static-seed FK ordering bug fix** — V1's `BuildSsdtStaticSeedStep.cs:82-86` sorts within static category only (cross-category FKs violate constraints); V2's `TopologicalOrderPass.SelfLoopPolicy` provides the correct global sort. This is a place where V2 has a *better* implementation than V1; no carbon-copy needed. V2's existing F# is the published form.

**Cross-cutting work in this phase:**

- CDC test fixture (per chapter pre-scope) — adds CDC-aware rows to
  the canary fixture so the CDC-silence property has real coverage.

**Post-close additions:**

- Slice 5.13.data-emission-registry (2026-05-18) — adds
  `RegisteredDataTransforms.all` (4 data-axis registry entries:
  composer + 3 emitters) per pillar 9. Closes matrix row 160's
  "NOT YET REIFIED" claim (which was stale at audit; the structural
  property held since slice ι; the missing piece was the
  cross-emitter property test). 13 new registry-classification
  tests + 2 new cross-emitter ordering tests.

**Per-phase residual risks (after close):**

- *CDC-silence property regressing on new fixtures* — caught structurally
  by `CdcSilenceTests` (Docker-gated canary). Mitigation: the test ships
  on the suite; a regression manifests as a red canary. No active drift
  signal as of close.

**Sequencing.** Closed end-to-end. See `CHAPTER_4_1_B_CLOSE.md` for the
full close synthesis.

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

### Phase 4 — Identity-as-driver (chapter 4.2; CLOSED 2026-05-15)

**Status:** closed end-to-end. Pre-scope at
`CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`. Close synthesis at
`CHAPTER_4_2_CLOSE.md`. A32 cashed out at this close (passes may
produce emitter-consumable values).

**All slices shipped:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α | UserMatchingStrategy DU + identity types (UserId / SourceUserId / TargetUserId / Email) + Policy axis extension | record-extension closed-DU empirical-test holds | shipped 2026-05-15 |
| β | UserPopulation on Profile + new `UserIdentity.fs` Core file | A34 orthogonality preserved | shipped 2026-05-15 |
| γ | UserRemap.fs in Core + UserRemapContext smart-constructor disjointness invariant | invariant tests green | shipped 2026-05-15 |
| δ | UserFkReflowPass.discover (ByEmail real) | `Lineage<Diagnostics<UserRemapContext>>` return shape | shipped 2026-05-15 |
| ε | Full strategy DU coverage (BySsKey + ManualOverride + FallbackToSystemUser) + lazy indexes | FallbackToSystemUser ⇒ `Set.isEmpty Unmatched` | shipped 2026-05-15 |
| ζ | `Reference.IsUserFk : bool` field + adapter resolution | record-extension across 23 literal sites | shipped 2026-05-15 |
| η | MigrationDependenciesEmitter user-FK column rewrite + multi-environment commutativity property (chapter signature) | source-keyset agreement across four target environments | shipped 2026-05-15 |

**V1 inheritance opportunities:**

- **UserMatchingEngine** — V1's `Osm.Pipeline.UatUsers.UserMatchingEngine.Execute`.
  F# rewrite landed at chapter 4.2 close per the algebraic-posture default;
  no carbon-copy event. The V1 source remains available as a reference oracle
  for future differential tests (see chapter 4.2 close §V1-input-envelope walk).

**Deferred-with-trigger (codified at close):**

- **OSSYS adapter User-kind identification surface** — chapter 4.2 ships every
  `Reference` with `IsUserFk = false` from the OSSYS adapter; real platform-user-kind
  identification requires `extension_id` lookup (per V1's
  `ModelUserSchemaGraphFactory.GetSyntheticUserForeignKeys`). Slice η's emitter
  integration is structurally complete; operationally a no-op until adapter
  resolves real User-FKs.
- **CSV adapter for `ManualOverride`** — pre-scope §3 names
  `Projection.Adapters.UserMap.UserMapLoader`. Slice ε ships `ManualOverride`
  consuming a programmatic `Map<SourceUserId, TargetUserId>`; the I/O adapter
  at the boundary is deferred until a real operator workflow demands file-format pickup.

---

### Phase 5 — Operational diagnostics (chapter 4.3; CLOSED structural slice arc)

**Status:** closed (structural slice arc α/β/γ). Pre-scope at
`CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md` Part 1. Close
synthesis at `CHAPTER_4_3_CLOSE.md`. Three-channel-deferral retired at
this close ("refuse the split; the three V1 artifacts ARE the channels").

**Structural slice arc shipped:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α | DecisionLogEmitter + new `Projection.Targets.OperationalDiagnostics` project + JsonNode typed seam at Π port | T11 keyset coverage; T1 byte-determinism | shipped |
| β | Routing primitive + OpportunitiesEmitter + shared `DiagnosticDocument` extracted at second-consumer threshold | single point of routing decision; chronological-order preserved within bucket | shipped |
| γ | ValidationsEmitter + Routing partition property (chapter signature) | every `DiagnosticEntry` routes to exactly one of three artifacts | shipped |

**Deferred-with-trigger (codified at close):**

- **Slice δ — CLI wire-up** — `osm emit --diagnostics` flag + Pipeline composition
  for the three emitters. Trigger: operator demand for one-command diagnostics
  emission.
- **Slice ε — V1 differential** — equivalence test against V1's existing
  `SqlMetadataDiagnosticsWriter` / `BasicDataIntegrityChecker` outputs on
  shared fixtures. Trigger: chapter that needs the cross-version diagnostic-fidelity
  evidence (likely chapter 5+ pragmatic close pre-cutover audit).

**V1 inheritance opportunities (still applicable for future slices):**

- **Diagnostic-finding generation** — V1's `SqlMetadataDiagnosticsWriter`
  and `BasicDataIntegrityChecker`. Status: `proposed` carbon-copy
  candidate for slice ε (V1 differential). F# rewrite preferred; carbon-copy
  as fallback if V1's specific finding-detection logic is materially harder
  to re-derive than to translate.

**Cross-cutting work:**

- Per-channel routing property test — `Code` prefix → channel mapping
  is exhaustive; every diagnostic lands in exactly one channel.

**Per-phase residual risks (after close):**

- *Routing correctness regression*. Caught structurally by the partition
  property test (slice γ).

**Sequencing.** Closed (structural slice arc). See `CHAPTER_4_3_CLOSE.md`
for the full close synthesis.

---

### Phase 5.5 — Manifest diagnostic fields (chapter 4.4; CLOSED 2026-05-17)

**Status:** closed end-to-end. Retires three of the four
`chapter 4.4 fills` deferrals codified in `ManifestEmitter.fs:32-33`
since chapter 4.1.A slice 9. Close synthesis at
`CHAPTER_4_4_CLOSE.md`; chapter-open at `CHAPTER_4_4_OPEN.md`.

**Strategic frame.** V2_DRIVER's per-axis correctness stakes places
operational-diagnostics as "Lower" stakes — this chapter ships an
operator-facing manifest surface, not a cutover-blocking property.
V2's `ManifestEmitter` (chapter 4.1.A slice 9) shipped with four
deferred fields emitting as `null` / empty arrays; this chapter
populates three from existing V2 evidence (Catalog + IR + Tolerance
taxonomy) and preserves the fourth (`PreRemediation`) per
V2_DRIVER §154's RemediationEmitter deferral.

**All slices shipped:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α | `CoverageBreakdown` + `CoverageSummary` + `Coverage.compute` | V1 percentage-rounding contract (AwayFromZero; total=0→100; emitted=0→0); T11 keyset coverage; 18 tests | shipped 2026-05-17 |
| β | `PredicateName` closed DU (16 V1 variants verbatim) + `PredicateCoverage.compute` | Per-table predicate satisfaction + PredicateCounts aggregation; closed-DU empirical-test catches missing arms; 14 tests | shipped 2026-05-17 |
| γ | `Unsupported.compute` renders `ToleratedDivergence.allKnown` as sorted strings | T1 byte-determinism; current-variant content audit; 7 tests | shipped 2026-05-17 |
| δ | V1 differential test (`ManifestV1DifferentialTests.fs`) + chapter-close eight-item ritual | PredicateName names verbatim; CoverageBreakdown rounding contract; SsdtManifest shape; documented divergences; 11 tests | shipped 2026-05-17 |

**Deferred-with-trigger (codified at close):**

- **PreRemediation field population** — per V2_DRIVER §154 (RemediationEmitter deferred to chapter 5+). Empty-array structurally correct until RemediationEmitter ships.
- **PredicateName 4 always-false variants** (HasFilteredIndex / HasIncludedIndexColumns / HasLogicalForeignKeyWithoutDbConstraint / HasLogicalForeignKeyWithDbConstraint) — V2 IR doesn't carry Filter expression / key-vs-included column split / logical-vs-physical Reference distinction. IR refinement triggers per docstring.
- **Unsupported per-divergence rationale** — current shape is `string list`; widens to typed record list if consumer demands per-divergence explanation strings.
- **V1↔V2 PredicateCounts JSON-shape divergence** — V2 emits sorted array of objects; V1 emits dict. Tolerance variant or shape-flip if byte-equality with V1 demanded.

**V1 inheritance opportunities:** none. The chapter mirrors V1's SsdtManifest reference types (SsdtManifest.cs + SsdtPredicateCoverage.cs + ManifestBuilder.cs) at the V2 IR layer; no carbon-copy event.

---

### Phase 5.6 — Index IR fidelity (chapter 4.5; CLOSED 2026-05-17)

**Status:** closed end-to-end. Retires 2 of chapter 4.4's 4 always-false
`PredicateName` variants by lifting V2's `Index` IR. Close synthesis
at `CHAPTER_4_5_CLOSE.md`; chapter-open at `CHAPTER_4_5_OPEN.md`.

**Strategic frame.** V2_DRIVER's per-axis correctness stakes places
this as Schema-axis work (High stakes; structural cutover-fidelity
weight). V1's `IndexOnDiskMetadata.FilterDefinition` +
`IndexColumnModel.IsIncluded` reference shapes mirrored at V2 IR;
the OSSYS adapter `isIncluded=true` drop divergence retires.

**Slices shipped:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α | `Index.Filter : string option` + `IndexDef.Filter` + ScriptDom WHERE emission via TSql160Parser + adapter pickup + HasFilteredIndex cash-out | 9 tests in `IndexFilterTests.fs` + cross-chapter cash-out in `ManifestPredicateCoverageTests.fs` | shipped 2026-05-17 |
| β | `Index.IncludedColumns : SsKey list` + `IndexDef.IncludedColumns` + ScriptDom INCLUDE emission + adapter partition-by-isIncluded + HasIncludedIndexColumns cash-out | 8 tests in `IndexIncludedColumnsTests.fs` + OsmCatalogReaderDifferentialTests EmailLower capture update | shipped 2026-05-17 |
| γ | V1 differential consolidation + chapter close ritual | 8-item ritual discharged; HANDOFF + BACKLOG + README updated | shipped 2026-05-17 |

**Deferred-with-trigger (codified at close):**

- **`HasLogicalForeignKey×DbConstraint` predicate pair** — V2's
  Reference doesn't carry logical-vs-physical distinction. Trigger:
  Tightening-decision-into-Reference flow.
- **`IndexColumnDirection`** (ASC/DESC per column) — record-modification
  rather than additive. Trigger: emission demands per-column sort
  direction (likely DACPAC adapter slice or fixture surfaces a non-ASC
  index).
- **`Index.IsPlatformAuto`** — adapter-derivable but no consumer demand.
- **On-disk rich metadata** (FillFactor / IsPadded / etc.) — out of
  V2-driver-correctness scope.
- **Filter-parse-failure Diagnostic emission** — currently silent-skip;
  trigger: real fixture surfaces a parse failure.

**V1 inheritance opportunities:** none. The chapter mirrors V1's
`IndexOnDiskMetadata` + `IndexColumnModel` shapes at the V2 IR layer.
No carbon-copy event.

---

### Phase 5.7 — Forward-signal cleanup bundle (chapter 4.6; CLOSED 2026-05-17)

**Status:** closed end-to-end. Retires three forward signals in a
single chapter: the last 2 chapter-4.4 always-false PredicateName
variants (slice α via Reference.HasDbConstraint lift); one of four
A.0' deferred concepts (slice β via Index.IsPlatformAuto lift); the
chapter 4.5 silent-skip Q3 deferral (slice γ via Diagnostics-aware
filter-parse helper). Close synthesis at `CHAPTER_4_6_CLOSE.md`;
chapter-open at `CHAPTER_4_6_OPEN.md`.

**Strategic frame.** Three independent additive cash-outs in a single
bundled chapter. Slice α has the largest leverage (closes the
chapter-4.4 PredicateName always-false audit). All-16-V1-aligned
PredicateName variants now evaluate against real V2 IR.

**Slices shipped:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α | `Reference.HasDbConstraint : bool` + adapter pickup (JSON + rowset + SymmetricClosure + ReadSide) + HasLogicalForeignKey×DbConstraint pair cash-out | 10 tests in `ReferenceHasDbConstraintTests.fs` + chapter-4.4 always-false test retired | shipped 2026-05-17 |
| β | `Index.IsPlatformAuto : bool` + adapter pickup | adapter pickup covered by existing OsmCatalogReaderDifferentialTests | shipped 2026-05-17 |
| γ | `ScriptDomBuild.tryParseFilterWithDiagnostics` Diagnostics-aware helper | 9 tests in `FilterParseDiagnosticTests.fs` | shipped 2026-05-17 |
| δ | V1 differential consolidation + chapter close ritual | 8-item ritual discharged; HANDOFF + BACKLOG + README updated | shipped 2026-05-17 |

**Deferred-with-trigger (codified at close):**

- **`isPlatformAuto` emitter consumption** (NEW) — IR carriage
  shipped at slice β; operator-toggle wiring deferred. Trigger:
  operator workflow demands filtering out platform-auto indexes.
- **Diagnostics-aware emitter signature** (NEW) — `tryParseFilterWithDiagnostics`
  available; `buildCreateIndex` wiring deferred. Trigger: downstream
  consumer needs filter-parse failures to surface in the manifest.
- **`IndexColumnDirection`** A.0' deferred concept — record-modification
  rather than additive.
- **`OriginalName` + `ExternalDatabaseType`** A.0' deferred concepts —
  untriggered.
- **On-disk rich Index metadata** — out of V2-driver-correctness scope.

**V1 inheritance opportunities:** none. The chapter mirrors V1's
`reference_hasDbConstraint` (outsystems_model_export.sql:730) +
`IndexModel.IsPlatformAuto` (IndexModel.cs:13) + `IndexScriptBuilder.
ParsePredicate` (IndexScriptBuilder.cs:403-419) at the V2 layer.
No carbon-copy event.

---

### Phase 5.8 — Refactor bundle + sibling-wrapper discipline (chapter 4.7; CLOSED 2026-05-17)

**Status:** closed end-to-end. Three preparatory refactors + an
unscheduled mid-flight tech-debt cleanup + discipline codification.
Close synthesis at `CHAPTER_4_7_CLOSE.md`; chapter-open at
`CHAPTER_4_7_OPEN.md`.

**Strategic frame.** Preparatory infrastructure for the chapter-4.6-
close shortlist's remaining items. Reduces per-future-IR-field touch
cost (slice γ); establishes Diagnostics-aware emitter contract (slice β);
codifies sibling-wrapper distinguishing test as discipline (cleanup).

**Slices shipped:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α | `getOptionalIntFlag` + `getOptionalBool` adapter primitives + 2 existing pattern usages migrated | retires the `match getIntFlag with Ok v -> v | Error _ -> default` boilerplate | shipped 2026-05-17 |
| β + fix-forward | Diagnostics-aware `buildCreateIndex` (canonical); silent-skip wrapper retired mid-flight after operator flagged | 7 tests in `BuildCreateIndexDiagnosticsTests.fs`; chapter 4.6 slice γ "Diagnostics-aware emitter signature" forward signal closed | shipped 2026-05-17 |
| Cleanup | `DataEmissionComposer.composeWithMigration` + `MigrationDependenciesEmitter.emitWithUserRemap` middle-tiers retired; 8 test call sites migrated | overdifferentiated middle-tier anti-pattern named + retired | shipped 2026-05-17 |
| Discipline | `DECISIONS 2026-05-17 (chapter 4.7 cleanup) — Sibling-wrapper discipline` + CLAUDE.md operating-disciplines row | distinguishing test ("hides info" vs "supplies default") + N+1 corollary codified | shipped 2026-05-17 |
| γ | `IRBuilders.mkReference` + Python sweep migrating 30 literals (9 Index + 21 Reference) across 15 test files | future IR-field-addition touch cost drops 85%+ for Index / Reference | shipped 2026-05-17 |
| δ | V1 differential N/A (no V1 surfaces consumed) + chapter close ritual | 8-item ritual discharged | shipped 2026-05-17 |

**Deferred-with-trigger (codified at close):**

- **Attribute / Kind / Module / Catalog literal sweep** — same Python
  pattern; trigger: time-budget for richer field-set migration.
- **WithDiagnostics emitter signature lift for other ScriptDomBuild
  builders** — trigger: a Diagnostics source emerges (CHECK constraint
  parse validation; extended-property name validation; MERGE expression
  parsing).
- **`UserFkReflowIntegrationTests.fs` Reference literal** — left
  unmigrated; hand-migration deferred.

**V1 inheritance opportunities:** none. The chapter operates at V2 layer.

---

### Phase 5.9 — IRBuilders Attribute sweep + on-disk Index metadata + isPlatformAuto emitter toggle (chapter 4.8; CLOSED 2026-05-17)

**Status:** closed. Three orthogonal cash-outs from the chapter 4.6
close shortlist. Close synthesis at `CHAPTER_4_8_CLOSE.md`; chapter-
open at `CHAPTER_4_8_OPEN.md`.

**Slices shipped:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α | Attribute IRBuilders sweep (108 literals; 21 test files) | baseline preserved post-migration; future Attribute additions ~2 sites | shipped 2026-05-17 |
| β | 5 additive Index fields (FillFactor / IsPadded / AllowRowLocks / AllowPageLocks / NoRecomputeStatistics) + ScriptDom IndexOptions emission | 8 tests in `IndexOnDiskMetadataTests.fs` | shipped 2026-05-17 |
| γ | `EmissionPolicy.IncludePlatformAutoIndexes` + `filterPlatformAutoIndexes` Catalog projection | 5 tests in `IsPlatformAutoEmitterToggleTests.fs` | shipped 2026-05-17 |
| δ | Chapter close ritual | 8-item ritual discharged | shipped 2026-05-17 |

**Deferred-with-trigger (codified at close):**

- **Kind / Module / Catalog IRBuilders sweep** — Python pass needs
  indentation-preserving rewrite (initial attempt triggered F# offside-
  rule failures on the 162 literal sites across these types). Trigger:
  agent willing to invest in the rewrite. Leverage if cashed: cheap
  future Kind/Module/Catalog field additions.
- **Composer/Pipeline wiring of `IncludePlatformAutoIndexes`** —
  toggle + filter primitive available; pipeline-wired auto-application
  deferred. Trigger: operator workflow demanding the toggle's effect
  applied to the SSDT bundle composition.

**V1 inheritance opportunities:** none. The chapter mirrors V1's
`IndexOnDiskMetadata` fields + `SsdtManifestOptions.IncludePlatformAuto
Indexes` at V2 IR / Policy layer. No carbon-copy event.

---

### Phase 5.10 — Big-batch forward-signal close-out + WithDiagnostics extensions (chapter 4.9; CLOSED 2026-05-17)

**Status:** closed. Six in-scope items at open (under explicit
principal-PO direction) — six retired (one partial; slice α' codified
as deferred-with-trigger). Close synthesis at `CHAPTER_4_9_CLOSE.md`;
chapter-open at `CHAPTER_4_9_OPEN.md`.

**Slices shipped:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α partial | IRBuilders Kind/Module/Catalog sweep (13 files / ~70 sites; 19 files deferred to α') | -104 net LOC; baseline preserved | shipped 2026-05-17 |
| β | `Attribute.OriginalName` + `Attribute.ExternalDatabaseType` IR lift | 7 tests in `OriginalNameAndExternalDbTypeLiftTests.fs`; 2 existing tests refreshed | shipped 2026-05-17 |
| γ | `IndexColumnDirection` DU + `IndexColumn` record; `Index.Columns` reshape; ScriptDom DESC emission | 5 tests in `IndexColumnDirectionTests.fs`; 5 test files refreshed | shipped 2026-05-17 |
| δ | `EmissionPolicy.filterPlatformAutoIndexes` wired into `Compose.project` | 2 tests in `IsPlatformAutoEmitterToggleTests.fs` (end-to-end through Compose.project) | shipped 2026-05-17 |
| ε | Multi-level `buildSetExtendedProperty` via `ExtendedPropertyOwner` DU; `Module.ExtendedProperties` emission | 6 tests in `ModuleExtendedPropertyEmissionTests.fs` | shipped 2026-05-17 |
| ζ | Diagnostics-bearing canonical signatures for 4 ScriptDom builders | 5 tests in `WithDiagnosticsBuildersTests.fs` | shipped 2026-05-17 |
| η | Chapter close ritual | 8-item ritual discharged | shipped 2026-05-17 |

**Deferred-with-trigger (codified at close):**

- **Slice α' — IRBuilders Kind/Module/Catalog sweep tail** (19 files;
  ~80 sites). Two Python-pass failure modes codified: (a) record
  literals inside multi-line list constructions where newline-separated
  elements collapsed into space-separated currying; (b) record literals
  nested inside `let`-bodies with inner `let` bindings + `if`-expressions
  where nested structure collapsed onto a single line breaking F#
  offside. Trigger: next IR-shape change to Kind/Module/Catalog that
  forces touching the deferred files.
- **PreRemediation / RemediationEmitter** — chapter 5+ territory; V2_DRIVER §154.
- **Sequence emission** — V1-fixture-gated; trigger unchanged from
  chapter 4.8.

**V1 inheritance opportunities:** none. The chapter mirrors V1's
`originalName` / `external_dbType` (Attribute JSON projections),
`direction` (per-column ASC/DESC; V1's `IndexDocumentMapper`),
`SortOrder` emission convention (V1's `IndexScriptBuilder`),
SCHEMA-level `sp_addextendedproperty` (V1's
`ExtendedPropertyScriptBuilder`). No carbon-copy event; mirrors at V2
layer only. **Phase 8 (chapter 5+) opens next** with the OSSYS catalog
producer carbon-copy — that's where carbon-copy fires.

---

### Phase 5.11 — OSSYS catalog producer carbon-copy (chapter 5.0; CLOSED 2026-05-17)

**Status:** closed end-to-end. The cutover-window pivot — V2 stands
on its own against an offline OSSYS source. Close synthesis at
`CHAPTER_5_0_CLOSE.md`; chapter-open at `CHAPTER_5_0_OPEN.md`.

**Slices shipped:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α | New `Projection.Adapters.OssysSql` F# project; `outsystems_metadata_rowsets.sql` carbon-copy verbatim (1184 LOC; byte-identical) | 4 tests in `MetadataExtractionSqlTests` + 1 gated parity test | shipped 2026-05-17 |
| β | `model.edge-case.seed.sql` carbon-copy with documented divergences (modern SQL Server compat); canary mockup donor | 4 tests in `MetadataExtractionSqlTests` | shipped 2026-05-17 |
| γ | F# `MetadataSnapshotRunner.runAsync` — executes SQL, walks 22 result sets, parses first 5 into typed records | absorbed in ε | shipped 2026-05-17 |
| δ | `toBundle` composer — JOIN logic (PhysicalTable → KindRow.DbSchema; Attribute.DeleteRule → Reference.DeleteRuleCode; RefEntityId pass-through) | absorbed in ε | shipped 2026-05-17 |
| ε | OSSYS extraction canary; `Deploy.withBootstrappedDatabase` primitive; cross-key-shape FK resolution fix; `parsePrimitiveType` extension | 5 canary tests in `OssysExtractionCanaryTests`; full chain Docker-gated | shipped 2026-05-17 |
| η | Chapter close ritual + `V1_PARITY_MATRIX.md` opened | 8-item ritual discharged | shipped 2026-05-17 |

**Deferred-with-trigger (codified at close):**

- **17 uncomposed V1 rowsets.** V2's `RowsetBundle` consumes 4; V1
  emits 22. The runner walks all 22 and skips 17. Per-rowset lift
  trigger: V2 IR consumer demands the field OR parity audit identifies
  drift.
- **Live `SqlClient` wiring (production-ops piece).** Runner is
  connection-agnostic; production wiring is supplying a real connection
  string. Connection-string discipline, retry semantics, integration
  test against real OSSYS instance — deferred as per-environment ops.

**V1 inheritance opportunities:** OSSYS catalog producer **shipped**
(was Phase 8 top entry). 3 carbon-copy events recorded in ADMIRE.md.
The next-highest-leverage V1 inheritance candidates surface via the
**Phase 5.12 parity audit wave**.

**V1-side audit findings:**

- **`OutsystemsIntegration` DB management** in V1's seed fixture
  conflicts with V2's per-run-database lifecycle; documented divergence.
- **`IGNORE_DUP_KEY = ON` on filtered index** in V1's seed is rejected
  by modern SQL Server; documented divergence.
- **Cross-module FK target SsKey-shape mismatch** in V2's prior
  `parseReferenceRowFor` (latent bug surfaced by canary): GUID-bearing
  target entities produced a synthesized-vs-OssysOriginal key shape
  mismatch breaking the danglingTarget invariant. Fixed at slice δ.
- **`parsePrimitiveType` coverage gap** (V2's prior implementation
  only covered Identifier + Text). Extended to 12 common types.

---

### Phase 5.12 — V1 Parity Audit Wave (chapter 5.1–5.8; AUDIT WAVE CLOSED 2026-05-18)

**Status:** audit wave closed at chapter 5.8. **185 matrix rows** across `V1_PARITY_MATRIX.md`; 23 sequential slice commits; 22 dated DECISIONS entries in 2026-05-{17,18}; 26 parity-test files; 3 synthesis documents (`V1_ARCHITECTURE_COMPENDIUM.md` + `V2_PATTERNS_COMPENDIUM.md` + `CUTOVER_READINESS_BRIEF.md`). Per-cluster sub-audits complete across Sections A (Ingest) / B (Analyze) / C (Emit) / D (Orchestrate) / E (Operate) / F (Compare).

**Strategic frame.** Per principal-PO direction at chapter 5.0 close — heavy V1 codebase audit to ensure maximal parity with formal representational coverage-state. The wave traded depth for breadth: each parity claim is independently verifiable, accumulates in a per-row append-only matrix, and produces an artifact that compounds.

**The six classification statuses** (per `V1_PARITY_MATRIX.md`):

| Status | Meaning |
|---|---|
| 🟢 PARITY | V2 produces equivalent output for the V1 capability. |
| 🔵 V2-EXTENSION | V2 carries the capability + adds structural strength. |
| 🟡 DIVERGENCE | V2 deliberately diverges; rationale documented in DECISIONS.md. |
| 🟠 NOT-MAPPED | V2 does not yet carry; named cash-out shape + concrete trigger. |
| 🔴 V1-BUG-CORRECTED | V2 fixes a V1 bug or unsafety. |
| ⚫ V1-SUNSET | V2 does not carry forward by intent; sunsets with V1 cutover+30. |

**Shipped slices (all 23 in chronological commit order):**

| Slice | V1 source | V2 representation | Matrix rows | Commits |
|---|---|---|---|---|
| 5.1.α | `IOutsystemsMetadataReader.cs` (23 DTOs) | OssysSql + CatalogReader inventory | 11–29 | `32e9b0f` |
| 5.1.ε | `SqlMetadataLog.cs` + `MetadataRowSnapshot.cs` + `SqlMetadataDiagnosticsWriter.cs` (operator-debugging telemetry) | NOT-MAPPED; CLI surface trigger | 30 | `ab82a01` |
| 5.1.β | `SnapshotValidator.cs` (JSON-shape validation) | V2 type system + A39 subsumes | 31 (⚫) | `efb46af` |
| 5.1.γ | `SqlClientOutsystemsMetadataReader.cs` + `MetadataSnapshotRunner.cs` (production wiring) | 5 axes: exception class / timeout / retry / contract / progress | 32–36 | `af15087` |
| 5.1.δ | `FixtureAdvancedSqlExecutor.cs` + `FixtureOutsystemsMetadataReader.cs` (offline fixture stack) | V2 SnapshotRowsets variant + IRBuilders | 37 (🟡) | `1dd341b` |
| 5.1.ζ | `MetadataContractOverrides.cs` (contract drift tolerance) | V2 ordinal readers + carbon-copied SQL pin | 38 (🟡) | `6216f43` |
| 5.1.σ | `AdvancedSql/outsystems_model_export.sql` (V1 JSON emitter SQL) | V2 emits SSDT directly | 39 (⚫) | `c385d7c` |
| 5.8.α | `Osm.Dmm/*` (DMM lens machinery; 8 files / 2200 LOC) | DROP + harvest `compare` CLI verb concept | 40–41 | `2a8e332` |
| 5.2.α.module | `ModuleModel.cs` + `OsmModel.cs` + `OutSystemsInternalModel.cs` | V2 Catalog/Module | 42–44 | `26631bc` |
| 5.2.α.entity | `EntityModel.cs` + `EntityMetadata.cs` | V2 Kind | 45–47 | `f3fb810` |
| 5.2.α.attribute | 7-file attribute cluster (3-layer separation) | V2 single Attribute + Kind.ColumnChecks | 48–53 | `c9891e1` |
| 5.2.α.index | 8-file index cluster | V2 Index record (chapter 4.5+ axes shipped) | 54–56 | `352bf90` |
| 5.2.α.relationship | RelationshipModel + ForeignKeyModel + RelationshipActualConstraint | V2 Reference (conflated) | 57–59 | `4fae6cc` |
| 5.2.α.misc | Sequence + Trigger + ExtendedProperty + TemporalRetention | V2 Catalog IR + row 23 amendment | 60–63 | `6fb3427` |
| 5.4.β.nullability | 15-file Signals/ cluster (~680 LOC) | V2 NullabilityRules + NullabilityPass + ternary outcome | 64–70 | `3cb2c5d` |
| 5.4.γ.evaluators | 11-file decision-engine + ColumnAnalysis | V2 Pass layer + per-axis decision sets | 71–76 | `8e6205d` |
| 5.4.γ.opportunities | ~12-file Opportunity + reporting + remediation surface | V2 Diagnostics + ManifestEmitter + per-pass contract | 77–84 | `fd74d63` |
| 5.4.δ.profiling | 28-file Pipeline/Profiling cluster | V2 Profile + ReadSide + ProfileSnapshot.attach | 85–94 | `05be5e7` |
| 5.5.α.manifest | SsdtManifest + SsdtPredicateCoverage + ManifestBuilder | V2 ManifestEmitter (chapter 4.4 close) | 95–104 | `171c65e` |
| 5.7.α.cli | 40-file Osm.Cli (12 V1 verbs vs 4 V2 verbs) | V2 minimal CLI posture | 105–119 | `3eb4619` |
| 5.3.α.smo | 44-file Osm.Smo (SMO scripter) | V2 ScriptDom typed-AST | 120–130 | `0884dac` |
| 5.6.α.orchestration | 53-file Pipeline/Orchestration + BuildSsdt | V2 registry-driven composition | 131–147 | `008c098` |
| 5.2.β.json | 47-file Osm.Json deserialization | V2 CatalogReader.SnapshotJson path | 148–155 | `a9efc86` |
| 5.5.β+γ+δ | Osm.Emission + Pipeline/StaticData + DynamicData (SSDT orchestration) | V2 sibling Π chorus + StaticSeedsEmitter | 156–165 | `fe38a7d` |
| 5.4.α+ε+ζ | ValidationFinding + Pipeline Evidence + Application + Mediation | V2 Diagnostics + functional composition | 166–173 | `39db66f` |
| omnibus | UAT users + LoadHarness + ValueObjects + CreateTable/IndexScript line-by-line | mixed | 174–185 | `1934948` |

**Classification distribution across 185 rows:**

| Status | Count | Notes |
|---|---|---|
| 🟢 PARITY | ~75 | V2 produces equivalent output (most where chapters 3-4 already shipped) |
| 🔵 V2-EXTENSION | ~35 | V2 structurally stronger (typed identity / closed DUs / typed evidence / functional composition) |
| 🟡 DIVERGENCE | ~25 | Deliberate; each has a DECISIONS entry or covers an existing one |
| 🟠 NOT-MAPPED | ~35 | Each with cash-out shape + dependencies + concrete trigger; tracked in Phase 5.13 below |
| 🔴 V1-BUG-CORRECTED | 2 | FK silent-skip (row 73); cross-key-shape FK resolution (row 9) |
| ⚫ V1-SUNSET | ~13 | V1-internal surfaces V2 doesn't replicate by design |

**Cadence rules (preserved across wave):**
- One slice per session arc (per the original discipline). Audit wave commitment held — 23 slice commits + 1 enrichment commit + 1 synthesis-docs commit.
- Single commit per slice with V1 source + V2 representation + classification + test in the message.
- Matrix updates are amendments (append-only at row level; status history amendments for reclassifications).
- Chapter-close ritual gains a "matrix coverage walk" item.

**Status history amendments shipped this wave:**
- Row 23 (`OutsystemsTriggerRow` — original 🟠 NOT-MAPPED per slice 5.1.α) → 🟢 PARITY (discovered by slice 5.2.α.misc; V2 Trigger IR shipped per chapter A.0' slice γ + matrix row 61).

**Trigger to re-open the wave:** any V1 cluster the audit didn't touch (e.g., V1 configuration JSON schemas; V1 telemetry / observability surfaces; V1 build/CI machinery). Append a row in the appropriate section of `V1_PARITY_MATRIX.md`.

---

### Phase 5.13 — Parity cash-out implementation wave (in-flight; triggered per row)

**Status:** opens 2026-05-18 immediately upon chapter 5 audit-wave close. Indefinite cadence — slices land as triggers fire per matrix-row Notes column.

**Strategic frame.** The matrix's 🟠 NOT-MAPPED + 🟡 DIVERGENCE-re-openable rows each carry a **cash-out shape** + **dependencies** + **acceptance criterion** + **trigger** in their Notes column. This phase converts each named cash-out into an actionable slice when the trigger fires.

**Priority axis: cutover-blocking.** Rows tagged "cutover-critical" or paired with R6 split-brain governance flip gates (per `CUTOVER_READINESS_BRIEF.md`) ship before rows tagged "post-cutover operator UX." Within each tier, slices are unordered priority-wise; pick what matches the session's capacity.

**Cutover-blocking cash-out slices (T-30 green path):**

| Slice | Matrix row | Cash-out shape | Trigger | Est. LOC |
|---|---|---|---|---|
| ~~5.13.transient-retry~~ | ~~34~~ | **SHIPPED 2026-05-18 (slice 5.13.production-wiring-classification).** Polly v8 `ResiliencePipeline` at `src/Projection.Adapters.OssysSql/Retry.fs`; 3 retries + exponential backoff + jitter; predicate `SqlException.Number ∈ {-2, -1, 4060, 18452, 40197, 40501, 40613}`; wraps `ExecuteReaderAsync`. Bundled with rows 32 + 35. | ~~V2 reads from cloud OSSYS (Azure SQL); cutover-critical~~ — closed | ~~150~~ |
| ~~5.13.exception-class~~ | ~~32~~ | **SHIPPED 2026-05-18 (slice 5.13.production-wiring-classification).** Closed-DU `MetadataExtractionError` (4 variants) at `src/Projection.Adapters.OssysSql/MetadataExtractionError.fs` + pure `classify` / `toValidationError` / `resultSetContractCheck`. Bundled with rows 34 + 35. | ~~V2 production CLI needs operator-distinguishable failure modes~~ — closed | ~~80~~ |
| ~~5.13.cdc-silence~~ | ~~(chapter 4.1.B)~~ | **SHIPPED 2026-05-18 (slice 5.13.cdc-silence-cross-emitter).** Cross-emitter property test (5 tests under `CdcSilenceCrossEmitterTests`, Docker-gated). Discovered + structurally fixed two compounding bugs: (a) Phase-1 MERGE's WHEN MATCHED UPDATE was touching deferred columns, overwriting them with NULL on idempotent redeploy; (b) Phase-2 UPDATE had no change-detection predicate, firing unconditionally on PK match. Fix: filter deferred from UpdColumns + add `UpdateBuildArgs.CdcAware` + `phase2DifferencePredicate`. V2 now structurally guarantees CDC silence; V1 leaks under the same workload. | ~~DATA axis V2-driver flip; chapter 4.1.B in flight~~ — closed | ~~400~~ |
| ~~5.13.user-matching~~ | ~~(chapter 4.2 slice ε)~~ | **SHIPPED 2026-05-18 (slice 5.13.identity-axis-closure).** All four DU variants (ByEmail, BySsKey, ManualOverride, FallbackToSystemUser) implementation had landed at chapter 4.2; the audit's "deferred" claim was stale. This slice closes the audit by adding 13 FsCheck property tests (totality + per-source diagnostic count + permutation invariance + idempotence + FallbackToSystemUser safety net) + cross-axis registry filters (`TransformRegistry.byDomain` + `byOverlayAxis`) + 8 cross-project registry-view tests. V1's Regex collapses to ManualOverride per Policy.fs pre-scope rationale. | ~~IDENTITY axis V2-driver flip; chapter 4.2 in flight~~ — closed | ~~600~~ |
| 5.13.uat-cli | 113 | `osm uat-users <model> <inventory-config> --out <dir>` CLI verb; pluggable matching strategies; CSV ingestion | UAT cutover dry-run needs CLI; bundles with chapter 4.2 closure | ~1500 |
| 5.13.verify-data | 114 | `osm verify-data <source> <target> <manifest> --report-out <path>` CLI verb; BasicDataIntegrityChecker port | Post-deploy verification phase; chapter 4.3+ | ~200 |

**High-leverage operator-UX cash-out slices (post-T-30; cutover-month):**

| Slice | Matrix row | Cash-out shape | Trigger | Est. LOC |
|---|---|---|---|---|
| 5.13.remediation-emitter | 83 | `RemediationEmitter` sibling Π consuming `Diagnostics<DecisionSet>`; emits `manifest.remediation.sql` with 3-option UPDATE/DELETE/SELECT per diagnostic | V2_DRIVER §154 chapter 5+ deferred; **operator UX degraded without it** | ~400 |
| 5.13.summary-formatter | 81 | `SummaryFormatter` consumer taking `Diagnostics<DecisionSet> × NullabilityMode`; produces per-bucket prose mirroring V1's 6-bucket classification | V2 CLI standardizes summary output format pre-cutover OR operator demands V1-compatible review surface | ~300 |
| 5.13.opportunities-report | 79 | `OpportunitiesReport` projection at `Projection.Targets.OperationalDiagnostics`; aggregates Diagnostics + DecisionSet → per-axis summary metrics | Operator dashboard demands per-axis rollup OR ManifestEmitter surface expansion | ~200 |
| 5.13.risk-classification | 76 | `Projection.Targets.OperationalDiagnostics.RiskClassification` module: `riskOf : NullabilityOutcome -> RiskLevel` + sibling functions; emitter-boundary placement per A36 | V2 emitter demands risk-stratified output (manifest / operator report / cutover dry-run) | ~150 |
| 5.13.osm-analyze | 111 | `osm analyze <model> [--profile <path>] [--policy <path>] --out <report-dir>` CLI verb; PassDriver + decision-log writer; no SSDT emission | Operators iterate on tightening policy pre-emission (typical pre-cutover workflow) | ~300 |
| 5.13.osm-policy-explain | 112 | `osm policy explain <decision-log.json> [--axis nullability\|fk\|unique] [--format table\|json]` CLI verb | Operators demand CLI-based policy drill-down for cutover dry-run reviews | ~300 |
| 5.13.osm-validate | 110 | `osm validate <model.json>` OR `osm validate --config <path>` CLI verb; wraps config validation + model ingestion | Operators demand pre-flight validation separate from full emit (CI health checks) | ~30 |
| 5.13.progress-tui | 36 + 118 | `SpectreProgressAdapter : IProgressRunner` wrapping `Bench.snapshot()` samples; CLI hook for long-running operations | Chapter 5.1 production CLI wiring + operator feedback on visibility | ~200 |
| 5.13.open-report | 115 | `osm deploy <manifest> --open-report` flag using cross-platform shim (`xdg-open` / `open` / `start`) | Operators demand integrated report-launching at deploy-time | ~150 |

**Lower-leverage cash-out slices (post-cutover; consumer-pressure-driven):**

| Slice | Matrix row | Cash-out shape | Trigger | Est. LOC |
|---|---|---|---|---|
| 5.13.live-profiler | 85–89 | `LiveProfiler` adapter in `Projection.Adapters.Sql`; `readProfileAsync : SqlConnection -> Catalog -> Task<Result<Profile>>` + 4 probe modules (NullCount / UniqueCandidate / FK-orphan-count / FK-orphan-sample) | Chapter 4.1.B § 4 or later — live SQL Server profile capture demanded | ~600 |
| 5.13.osm-profile | 108 | `osm profile <model> <connection-string> --out <profile.json>` CLI verb; wraps LiveProfiler | Operators demand profile-only execution for diagnostic/tuning iteration | ~30 |
| 5.13.osm-extract | 107 | `osm extract <connection-string> --modules <csv> --out <path>` CLI verb; wraps `MetadataSnapshotRunner.runAsync` | V2 production CLI surface ships; operators need extraction as CLI step | ~50 |
| ~~5.13.result-set-contract~~ | ~~35~~ | **SHIPPED 2026-05-18 (slice 5.13.production-wiring-classification).** `[<Literal>] let ExpectedResultSets = 23` (empirical, not the V1-doc'd 22 — the canary observes a leading validation projection V1 skipped) + post-loop `resultSetContractCheck` + `MetadataExtractionError.ResultSetMissing` variant + `adapter.ossysSql.resultSetContractBreach` code. Bundled with rows 32 + 34. | ~~V2 canary fails parity assertion tracing to SQL contract drift~~ — closed pre-emptively | ~~30~~ |
| ~~5.13.progress-callback~~ | ~~36~~ | **SHIPPED 2026-05-18 (slice 5.13.progress-callback).** `MetadataSnapshotRunner.ProgressObservation` record + `OnRowsetComplete` callback alias + three-arity `runAsyncWithProgress` entry point + `noOpProgress` default + two-arity `runAsync` convenience overload. Canary test asserts callback fires for every observed rowset. | ~~V2 ships CLI for full-catalog extraction (300 tables; multi-minute)~~ — closed pre-emptively per better-than-parity directive | ~~20~~ |
| 5.13.profile-merge | 92 | `Profile.merge : Profile -> Profile -> Profile` + commutative + associative property tests; consensus thresholding at orchestrator | Multi-environment risk scoring demanded (chapter 4.1.B or 4.2) | ~200 |
| 5.13.attribute-reality | 49 | `Profile.AttributeReality` record (IsNullableInDatabase + HasNulls + HasDuplicates + HasOrphans + IsPresentButInactive); thread through ReadSide | V2 Profile-layer surface needed by tightening / remediation consumer | ~150 |
| ~~5.13.column-reality~~ | ~~11~~ | **SHIPPED 2026-05-18 (slice 5.13.ossys-rowsets-cluster).** Typed `OssysColumnRealityRow` + ordinal mapper land at runner; IR consumer deferred to Profile.AttributeReality (row 49). | ~~V2 tightening / remediation decision needs source-side column reflection~~ — typed rowset lifted; IR integration gated | ~~100~~ |
| ~~5.13.column-checks-ir~~ | ~~12 + 50~~ | **SHIPPED 2026-05-18 (slice 5.13.ossys-rowsets-cluster).** Row 12 wired through `parseColumnCheckRowFor` into `Kind.ColumnChecks`; multi-column CHECK dedupe by ConstraintName. | ~~V2 IR refinement + emitter consumer (SSDT or DACPAC) demands CHECK constraints~~ — IR exists; rowset path now populates | ~~250~~ |
| ~~5.13.column-features-emit~~ | ~~12 + 53 + 182~~ | **SHIPPED 2026-05-18 (slice 5.13.column-features-emit).** Emit-side closure for chapter A.0' slice ε IR lifts: `Statement.ColumnDef` extended with `DefaultValue` + `DefaultName`; new `ColumnCheckDef` type; `Statement.CreateTable` constructor extended to carry CHECK list; `ScriptDomBuild.columnDefinition` emits `DefaultConstraintDefinition`; new `checkConstraint` builder parses via `TSql160Parser.ParseBooleanExpression`; `Render.fs` CREATE TABLE arm collapsed into ScriptDomGenerate delegation (single source of truth). 3 new canary tests assert DEFAULT + CHECK + T1 byte-determinism. | ~~Rows 12 + 53 + 182 close to 🟢 PARITY~~ — closed | ~~150~~ |
| ~~5.13.physical-columns-present~~ | ~~14~~ | **SHIPPED 2026-05-18 (slice 5.13.ossys-rowsets-cluster).** Typed `OssysPhysColsPresentRow` lifts at runner; orphan-attribute IR consumer deferred. | ~~V2 reports source-side orphan attributes~~ — typed rowset lifted; consumer gated | ~~50~~ |
| ~~5.13.ossys-indexes~~ | ~~15 + 16~~ | **SHIPPED 2026-05-18 (slice 5.13.ossys-rowsets-cluster).** Rows 15 + 16 wired through `parseIndexRowFor` into `Kind.Indexes` (key columns + included columns + sort direction + filter + index options); HumanAttr-vs-physical-name resolver matches V1's COALESCE shape. **Retires V2's structural dependence on V1's #IdxJson** (row 26 V1-SUNSET). | ~~V2 lifts structured rowset path for index reflection~~ — closed | ~~250~~ |
| ~~5.13.ossys-foreign-keys~~ | ~~17 + 18~~ | **SHIPPED 2026-05-18 (slice 5.13.ossys-rowsets-cluster).** Typed `OssysFkRealityRow` + `OssysFkColumnRow` lift at runner; Reference IR enrichment (OnUpdate per row 58; IsNoCheck per row 59) gated on those slices. | ~~V2 reports source-vs-target FK drift OR IsNoCheck flag feeds tightening~~ — typed rowset lifted; IR enrichment gated | ~~200~~ |
| ~~5.13.ossys-triggers~~ | ~~23~~ | **SHIPPED 2026-05-18 (slice 5.13.ossys-rowsets-cluster).** Row 23 wired through `parseTriggerRowFor` into `Kind.Triggers`; rows with blank Definition filter at adapter boundary. **Retires V2's structural dependence on V1's #TriggerJson** (row 27 V1-SUNSET). | ~~V2 trigger emission lands~~ — closed | ~~50~~ |
| ~~5.13.update-action~~ | ~~58~~ | **EMIT-SIDE SHIPPED 2026-05-18 (slice 5.13.fk-features-emit, paired with smart-constructor-lift).** `Reference.OnUpdate : ReferenceAction option` lifted; `ScriptDomBuild.foreignKeyConstraint` emits `cons.UpdateAction` when `Some action`. Adapter rowset-path JOIN (`OssysReferenceRow` ⊕ `OssysFkRealityRow.UpdateAction`) deferred — defaults `None` until a JOIN slice wires the per-FK-constraint metadata onto per-attribute references. | ~~V2 SSDT emission needs ON UPDATE referential actions~~ — emit-side closed; rowset wiring follow-up gated on operator-reality evidence | ~~100~~ 30 (adapter JOIN residual) |
| ~~5.13.nocheck-state~~ | ~~59~~ | **EMIT-SIDE SHIPPED 2026-05-18 (slice 5.13.fk-features-emit).** `Reference.IsConstraintTrusted : bool` lifted; new `Statement.AlterTableNoCheckConstraint` variant + ScriptDom builder via `AlterTableConstraintModificationStatement`; SsdtDdlEmitter yields one ALTER per untrusted FK after CREATE TABLE. Adapter rowset-path JOIN (`#FkReality.IsNoCheck`) deferred — same JOIN dependency as row 58. | ~~Deployed target carries WITH NOCHECK FK constraints V2 must round-trip~~ — emit-side closed; rowset wiring follow-up | ~~80~~ 30 (adapter JOIN residual) |
| ~~5.13.fk-reality-join~~ | ~~17 + 18 + 58 + 59~~ | **SHIPPED 2026-05-18 (slice 5.13.blind-spot-closure).** Adapter JOIN at `MetadataSnapshotRunner.toBundle` threads `OssysFkRealityRow.UpdateAction` + `IsNoCheck` onto `ReferenceRow` via the 3-step lookup chain (`AttrId → OssysFkColumnRow.ParentAttrId → FkObjectId → OssysFkRealityRow`); also wires the parallel `OssysAllIdxRow.IsDisabled / IgnoreDupKey / DataCompressionJson` onto the new `IndexRow` fields. `CatalogReader.ReferenceRow` + `IndexRow` extended; `parseReferenceRowFor` + `parseIndexRowFor` thread through to `Reference.create … with` / `Index.create … with`. Cluster-A1 cash-out complete. | ~~Deployed environment surfaces evidence~~ — emit + adapter closed | ~~150~~ |
| ~~5.13.emit-features-registry~~ | (blind-spot #1) | **SHIPPED 2026-05-18 (slice 5.13.blind-spot-closure).** `SsdtDdlEmitter.registeredMetadata : RegisteredTransformMetadata` with 11 classified Sites (one per V1-CreateTable + V1-CreateIndex feature V2 emits). `ManifestEmitter.build` prepends to `RegisteredTransforms.all` so totality-coverage scan reaches Emitter stage. 6 new `EmitterRegistrationsTests.fs` tests. | ~~Pillar 9 totality coverage expansion to Emitter stage~~ — closed | ~~50~~ |
| ~~5.13.render-stringbuilder-retirement~~ | (blind-spot #5 + #6) | **SHIPPED 2026-05-18 (slice 5.13.blind-spot-closure).** `Render.toSql`'s per-variant arms collapsed into a single `_ ->` default arm routing every SQL-bearing Statement through `ScriptDomBuild.buildStatement` + `ScriptDomGenerate.generateOne`. Four dead-weight helpers retired (`columnSqlType`, `formatSqlLiteral`, `actionSql`, `renderColumn`). Render.fs reduces to 4 public functions. | ~~Single source of truth via ScriptDom typed AST~~ — closed | ~~30~~ |
| 5.13.default-constraint | 53 | `Attribute.Default : DefaultConstraint option` (Name + Value + IsNotTrusted); migrate existing `DefaultValue` consumers. **DEFAULT clause emission shipped 2026-05-18 (slice 5.13.column-features-emit);** this remaining slice is the constraint-identity round-trip (named `DF_<Table>_<Column>` carried from V1 `#ColumnReality.DefaultConstraintName`). | Manifest emitter demands constraint identity OR DDL emitter round-trips V1 constraint names | ~80 (residual) |
| ~~5.13.index-disabled-igdupkey~~ | ~~55~~ | **EMIT-SIDE SHIPPED 2026-05-18 (slice 5.13.index-features-emit).** `Index.IgnoreDuplicateKey : bool` + `Index.IsDisabled : bool` lifted; `ScriptDomBuild.buildCreateIndex` emits `IGNORE_DUP_KEY = ON` via IndexStateOption; new `Statement.AlterIndexDisable` variant + builder via `AlterIndexStatement` with `AlterIndexType.Disable`; SsdtDdlEmitter yields one ALTER per disabled non-PK index after CREATE INDEX. Adapter rowset-path wiring (`#AllIdx.IsDisabled` + `IgnoreDuplicateKey`) deferred — same JOIN dependency pattern as rows 58 + 59. | ~~Deployed target carries disabled indexes OR IGNORE_DUP_KEY indexes V2 must round-trip~~ — emit-side closed; rowset wiring follow-up | ~~100~~ 30 (adapter wiring residual) |
| 5.13.index-data-compression | 56 (partial) | **EMIT-SIDE SHIPPED 2026-05-18 (slice 5.13.index-features-emit).** Single-value `Index.DataCompression : DataCompressionLevel option` (`None | Row | Page`); `ScriptDomBuild.buildCreateIndex` emits `DATA_COMPRESSION = <level>` via DataCompressionOption. Covers the dominant case (uniform compression across all partitions or no-partition). | Emit-side closed; rowset wiring follow-up | ~~adapter wiring residual | ~30 |
| 5.13.index-partition | 56 (residual) | `Index.DataSpace : DataSpace option` (closed DU: `Filegroup of name | PartitionScheme of name × columns`) + per-partition `PartitionCompression list`; adapter + emitter | Production OSSYS uses partitioned indexes V2 must preserve | ~250 |
| 5.13.evidence-cache | 135 | Cache adapter writing checkpointed Catalog/Policy decision-set JSON; consumer-driven | Operator-reality canary shows evidence-load as bottleneck OR chapter 4+ perf-optimization opens | ~400 |
| 5.13.sql-validator | 136 | `Validator` sibling Π consuming SSDT stream → `ValidationReport` at realization layer (not Core per A35/A36) | Realization layer demands validation feedback (CI/CD gate or interactive editor) | ~300 |
| 5.13.postdeploy-template | 140 | `PostDeployTemplateEmitter` sibling Π consuming SSDT statements + emitting template SQL with guard logic | Chapter 4.1 slice 9 opens OR post-deploy emitter consumer demand | ~200 |
| 5.13.sqlproj-realizer | 141 | `Render.toSqlProject` realizer consuming `ArtifactByKind<SsdtFile>` + emitting XML `.sqlproj` MSBuild file | V2 realization layer demands Visual Studio / Azure DevOps integration | ~200 |
| 5.13.compare-verb | 41 | `projection compare <left> <right>` CLI verb + closed-DU `DiffSource = LiveDb \| SsdtProject \| DacpacFile \| RawSql` + `Compare.run : DiffSource -> DiffSource -> Diagnostics<SchemaDiff>` + per-variant adapter | Operator workflow demands ad-hoc schema-diff outside canary's specific scope | ~500 |
| 5.13.option-binders | 116 | Carbon-copy V1's binder patterns to F# (`ModuleFilterOptionBinder` + `VerbOptionsBuilder` + per-axis binders) | CLI grows beyond 4 verbs with composable axes | ~500 |
| 5.13.global-options | 117 | `CliGlobalOptions` record in Program.fs; parse before verb dispatch; thread to each verb runner | Operators demand CLI-level global flags (`--log-level` / `--verbose` / `--quiet`) | ~50 |
| 5.13.dmv-instrumentation | 179 | Post-cutover operator-facing tool consuming Bench samples + DMV queries (WaitStats + Locks + IndexFragmentation) via `Projection.Adapters.Sql` DMV adapter | Chapter 5+ operator-facing post-deploy tools OR operator demands DMV-style observability | ~400 |

**Cadence rules (continue from Phase 5.12):**
- One slice per session arc (preserved).
- Slices may bundle co-dependent cash-outs (e.g., 5.13.transient-retry + 5.13.exception-class) when matrix Notes flag the dependency.
- Matrix amendments: when a slice ships, append a Status-history entry under the matrix-row's section.
- Slice commit message must name the matrix row(s) it cashes out.

**Trigger to retire a cash-out slot:** the matrix row's trigger condition is named "no longer expected" by principal-PO sign-off OR the underlying capability lands via a different path (e.g., V1 sunset retires a row's prerequisite).

---

### Phase 6 — DACPAC dev-tooling (chapter 3.x; CLOSED under reframe)

**Status:** closed under dev-tooling reframe. The chapter was originally
pre-scoped as deploy-path-conditional production-write; the operator
reframed at chapter open (2026-05-11) to dev-tooling consumption surface
(production deploy stays SSDT-style file deploy). Close synthesis at
`CHAPTER_3_X_CLOSE.md`.

**Structural slice arc shipped:**

| Slice | Scope | Witness | Status |
|---|---|---|---|
| α | DacpacEmitter v0 + chapter open + `Microsoft.SqlServer.DacFx` v162.x adoption (Tier-3 text-builder-as-first-instinct cash-out) | `DacpacEmitter.emit : Catalog -> Result<byte[]>`; A18 amended preserved | shipped |
| β | FK round-trip via DacFx | structural test on `sampleCatalog` Order→Customer FK | shipped |
| γ | Indexes round-trip via DacFx | DacFx's `Index.Unique` property preserved across round-trip | shipped |
| δ_dock | DockerImageEmitter (one-command dev stand-up via `docker pull` / `docker run`) | image-build canary green | shipped |

**Deferred-with-trigger (codified at close):**

- **Slice ε — modality marks → DACPAC comments / extended properties** —
  defers until consumer demand for DACPAC-level annotation surface.
- **Slice ζ — byte-determinism cash-out via post-hoc canonicalization** —
  defers until a downstream consumer compares `.dacpac` byte payloads
  across runs (DacFx's serialization is order-stable at the model level
  but the `.dacpac` ZIP-container byte layout is not deterministic).
- **Per-Catalog parameterization** — defers until a non-`sampleCatalog`
  consumer surfaces.

**V1 inheritance opportunities (still applicable):**

- **DACPAC builder plumbing** — V1's `DacpacBuilder.CreateDacpac`.
  F# wrapper landed (no C# subproject; pre-scope §6.2 bias yielded under
  empirical pressure). No carbon-copy event; V1 source remains a reference
  for slice ε / ζ if those surface.

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

### Phase 8 — Path to T-30 green (the cutover-ladder gate)

**Status:** in flight. The three blocking deliverables for T-30 green per `CUTOVER_READINESS_BRIEF.md` Section 4. T-30 green = R6 per-pair V2-driver flip eligible across all six axes (SCHEMA / DATA / IDENTITY / DIAGNOSTICS / OPERATOR-AFFORDANCE / PIPELINE-ORCHESTRATION).

**Current confidence per axis (as of chapter 5 audit-wave close):**

| Axis | Status | Blocking T-30 green? |
|---|---|---|
| SCHEMA | 🟢 V2-DRIVER-READY | No |
| DATA | 🟡 V2-AUGMENTED | Yes — chapter 4.1.B + global phase ordering |
| IDENTITY | 🟡 V2-AUGMENTED | Yes — chapter 4.2 slice ε + UAT dry-run |
| DIAGNOSTICS | 🟡 V2-AUGMENTED | Soft (operator-tolerant via DiagnosticEntry stream) |
| OPERATOR-AFFORDANCE | 🟡 V2-AUGMENTED | Yes — `osm uat-users` + `osm verify-data` |
| PIPELINE-ORCHESTRATION | 🟢 V2-DRIVER-READY | No |

**The three blocking deliverables:**

| # | Deliverable | Phase 5.13 slice owner | Status | Acceptance |
|---|---|---|---|---|
| 1 | ~~Chapter 4.1.B CDC-silence property test + global Phase1/Phase2 cross-emitter ordering~~ | ~~`5.13.cdc-silence`~~ | **SHIPPED 2026-05-18.** Cross-emitter ordering closed by slice 5.13.data-emission-registry; CDC silence closed by slice 5.13.cdc-silence-cross-emitter (which discovered + fixed two structural bugs — Phase-1 deferred-column UPDATE + Phase-2 unconditional UPDATE). | ~~Property test green on operator-reality canary; zero CDC events on idempotent redeploy~~ — 5 new property tests (C0 structural + C1-C4 live canary) pass under Docker-gated SQL Server CDC. |
| 2 | ~~Chapter 4.2 slice ε remaining matching strategies (BySsKey + Regex + ManualOverride + FallbackToSystemUser)~~ | ~~`5.13.user-matching`~~ | **SHIPPED 2026-05-18** by slice 5.13.identity-axis-closure | ~~Property test asserting symmetry of matched + unmatched diagnostics on shared fixtures~~ — 13 FsCheck properties (S1 totality + S2 per-source diagnostic count + S3 diagnostics cardinality + S4 permutation invariance + S5 idempotence + FallbackToSystemUser safety net across 3 primary strategies) cover the requirement |
| 3 | ≥1 full UAT dry-run on real inventory CSVs | (depends on slice ε + `5.13.uat-cli`) | Pending | Operator sign-off: zero unmatched orphans OR operator-acknowledged manual overrides |

**Recommended sequence:**

1. Open chapter 4.1.B slice η — global phase ordering across emitters (paired with cdc-silence property test). **Blocks DATA axis flip.**
2. Open chapter 4.2 slice ε — remaining matching strategies. **Blocks IDENTITY axis flip.**
3. Ship `5.13.uat-cli` (`osm uat-users`) — UAT cutover workflow needs CLI. **Required for UAT dry-run.**
4. Run UAT dry-run on real CSVs — final IDENTITY axis confirmation.
5. (Optional, recommended) Ship `5.13.remediation-emitter` (chapter 5+) and `5.13.summary-formatter` — improve DIAGNOSTICS axis operator UX before cutover-window operator review workflows engage.
6. (Optional, recommended) Ship `5.13.osm-validate` + `5.13.osm-analyze` — pre-flight + iterative-policy operator workflows.

**Per-phase risks (refresh):**

- *Chapter 4.1.B doesn't close before T-30* → V2-augmented mode (V1 drives, V2 verifies via canary) is fallback. DATA axis stays 🟡 until property test ships.
- *Chapter 4.2 slice ε delayed* → slice δ ByEmail covers dominant case; ManualOverride substituted via operator CSV (per matrix row 174).
- *UAT dry-run reveals unanticipated identity-matching gaps* → Tolerance variant + manual override CSV substitute; named in operator runbook.
- *RemediationEmitter doesn't ship before cutover* → DiagnosticEntry.Message + Metadata provide enough context for hand-written remediation SQL; fallback remediation doc substitutes.

---

### Phase 9 — V2-driver operator workflow (post-T-30; cutover-window operator UX)

**Status:** scheduled per (verb × workflow) gating. Opens when T-30 green achieved AND operator demand surfaces for specific operator-facing surfaces.

**Strategic frame.** V2's CLI is deliberately minimal at launch (per `DECISIONS 2026-05-18 (slice 5.7.α.cli) — V2 CLI deliberately minimal: production-deferred posture`). Operator-affordance verbs land as their workflow triggers fire. Total estimated cash-out: ~3780 LOC across 10 verbs + 3 emitters + 2 infrastructure pieces.

**Pending operator-UX cash-outs (per Phase 5.13 priority lists):**

| Slice | Trigger | Priority for cutover-window | Est. LOC |
|---|---|---|---|
| `5.13.remediation-emitter` | RemediationEmitter chapter 5+ (V2_DRIVER §154) | High (operator UX for mandatory-null-conflict) | 400 |
| `5.13.summary-formatter` | V2 CLI standardizes summary output OR operator demands V1-compatible review | High (operator review of cutover dry-run) | 300 |
| `5.13.opportunities-report` | Operator dashboard demands per-axis rollup | Med | 200 |
| `5.13.risk-classification` | V2 emitter demands risk-stratified output | Med | 150 |
| `5.13.osm-analyze` | Pre-emission policy iteration | High (cutover dry-run) | 300 |
| `5.13.osm-policy-explain` | CLI-based policy drill-down | Med | 300 |
| `5.13.osm-validate` | Pre-flight model validation in CI | Low (CI health checks) | 30 |
| `5.13.progress-tui` | Long-running operations need TUI | Med (operator visibility) | 200 |
| `5.13.open-report` | Integrated report-launching at deploy-time | Low | 150 |
| `5.13.osm-extract` + `5.13.osm-profile` | Production CLI extraction + profile-only execution | Med (cutover dry-run iteration) | 80 |

**Cross-cutting work:**
- Operator runbook section: per-axis V2-driver flip checklist; per-verb workflow recipes; tolerance escalation procedures
- Bench dashboard integration: hook `Bench.snapshot()` JSON into operator-facing tools (depends on slice `5.13.progress-tui`)
- F# Analyzers SDK custom analyzer (proof-of-concept rule) — generalizes `NoUnsafeTimeInCoreAnalyzer.fs` pattern; deferred per consumer-pressure
- Coordinates Stage 2 typed VOs — deferred per `DECISIONS 2026-05-11`; empirical adapter-ripple justification required

**Per-phase risks:**
- *Operator-UX cash-outs land late* → V2-driver-mode flip happens with degraded operator UX; canary + DiagnosticEntry stream cover the cutover-critical surface; UX improvements ship post-cutover
- *Bench-dashboard integration delayed* → operators read raw Bench JSON; UX suboptimal but cutover-functional

---

### Phase 10 — V1 sunset (cutover+30 onward)

**Status:** scheduled per cutover+30 gate. Three-condition gate (per § VIII below). Per `CLAUDE.md` operating-disciplines table, V1 sunset is a condition, not a deadline.

**Inventory of V1 surfaces to sunset (matrix-recorded; 13 ⚫ V1-SUNSET rows):**

| Matrix row | V1 surface | Migration impact | Sunset timing |
|---|---|---|---|
| 13, 21, 22, 24-28 | JSON-aggregation rowsets (`#AttrCheckJson` + `#FkColumnsJson` + `#FkAttrJson` + `#AttrJson` + `#RelJson` + `#IdxJson` + `#TriggerJson` + `#ModuleJson`) feeding `osm_model.json` emission | Zero V2 consumers; V2's SnapshotJson path consumes historical files but doesn't require V1 to keep emitting | cutover+30 |
| 39 | `AdvancedSql/outsystems_model_export.sql` (V1 JSON-emitter SQL) | Producer-side companion to JSON-aggregation rowsets | cutover+30 |
| 40 | Osm.Dmm lens machinery (8 files / 2200 LOC) | V1-side consumers (DACPAC build verification); future V2 `compare` verb harvests concept | cutover+30 |
| 67 | DefaultSignal (telemetry-only; structurally misleading) | Zero V2 consumers (V1 signal was telemetry-only) | cutover+30 |
| 84 | TighteningRationales string-constant module | V2 typed Outcome DUs subsume; bucket classification ports to outcome pattern-match | cutover+30 |
| 109 | dmm-compare CLI verb | Future `compare` verb reserves concept (matrix row 41) | cutover+30 |
| 133 | CaptureProfilePipeline (separate two-pass class) | V2 adapter-input model per A34 obviates | cutover+30 |
| 134 | DmmComparePipeline | Canary subsumes (companion to row 40) | cutover+30 |
| 178 | LoadHarnessRunner DMV instrumentation | DMV piece becomes post-cutover operator tool per matrix row 179 | cutover+30 |

**Sunset workflow:**
1. Verify R6 per-pair V2-driver flips have run N=10 consecutive green across all four environments
2. Verify ≥1 full schema-evolution cycle on V2 emissions per environment
3. Operator sign-off per § VIII condition 3
4. Mark V1 trunk archive-only; revoke build CI
5. Update each ⚫ V1-SUNSET matrix row with Status history amendment recording the sunset date
6. Update `ADMIRE.md` carbon-copy entries to "V1 sunset" status

**Per-phase risks:**
- *V1 sunset deferred indefinitely* → chapter close ritual evaluates ladder reachability; if V2-augmented mode persists past cutover+90, principal-PO decision on whether to extend V1 warm window or invest in remaining cash-outs
- *Post-cutover regression in V2 emissions* → V1 stays warm; operator falls back per environment; named in operator runbook

---

### Phase 11 — Transfer / bidirectional pipeline (epic; prescoped)

**Status:** prescoped; **slice 1 shipped**. Off the cutover ladder — a
preview/migration capability, not a cutover-gate dependency. Develops on
`claude/bidirectional-exporter-pipeline-4XN4Q`. Full prescope:
`PRESCOPE_TRANSFER.md`; governing decisions: `DECISIONS 2026-05-24`.

**What it is.** A *Transfer* loads row data from one database substrate into
another over one shared schema: `Ingestion(Source) → Projection(Sink)`. The
motivating instances: (a) the blank-UAT preview — ingest from the staging SQL
Server (re-bound as Source) and project into an OutSystems Cloud UAT database
(Sink); and (b) the **Dev→UAT User re-key** — load Dev data into UAT,
re-keying every User-FK from the Dev surrogate to the *pre-existing* UAT
surrogate (operator's headline case). It is the **H-050 adjunction extended
from schema to data across two substrates** — not a new pipeline, but a
re-source + re-sink of the existing direction-neutral two-phase plan (A35/A36).

**North Star.** The data-level extension of H-050:
`Ingestion(Projection(rows)) ≈ rows` up to named identity-remap tolerances,
earned by a **data-level canary** (the data analog of the schema canary).

**Three identity dispositions** (`IdentityDisposition`): `PreservedFromSource`
(source key written directly), `AssignedBySink` (sink mints a new key; capture
during insert), `ReconciledByRule` (match a pre-existing sink identity by
operator ruleset *before* insert — the User re-key). The `ReconciledByRule`
engine is **already built**: `UserFkReflowPass.discover` + `UserMatchingStrategy`
(`ByEmail`/`BySsKey`/`ManualOverride`/`FallbackToSystemUser`) + dual
`Profile.SourceUsers`/`TargetUsers`. See `DECISIONS 2026-05-24 (refinement)`.

**Deferral unification.** Phase 11 Slice C′ **subsumes** the previously
free-floating deferrals *"Multi-environment config (DEV/TEST/UAT/PROD) +
UAT-users"* (Phase 4 neighbourhood) and *"LiveOssysConnection variant"* — they
are the same convergent capability (the connection apparatus + live
dual-environment profiling).

**Slice queue:**

| Slice | Scope | Status |
|---|---|---|
| 1 | Vocabulary reification — `Transfer.fs` (`SubstrateRole`, `SourceKey`/`AssignedKey`, `IdentityDisposition` + `ofKind`, `SurrogateRemapContext`); 11 tests | ✅ shipped |
| A | `SchemaContract` on-disk persistence (`SsKey` + FK graph + physical-coordinate index); round-trip property test | 🔵 scheduled |
| B | Pure two-phase identity-aware Transfer plan + `Ingestion` row-stream adapter (reuse `ReadSide.readRowsStream`) | 🔵 scheduled |
| C | `PreservedFromSource` Projection-onto-Sink + Transfer orchestrator (dry-run) + **data-level canary** | 🔵 scheduled (highest-leverage) |
| C′ | **Connection apparatus** (`Environment`/`Substrate`/`TransferConnections`) + `ReconciledByRule` — live dual-environment user profiling → `UserFkReflowPass` → phase-2 reflow; `--user-map` CSV loader. **Unifies the multi-env + LiveOssysConnection deferrals.** Adds the `ReconciledByRule` DU variant | 🔵 scheduled (operator headline case) |
| D | `--execute` against UAT, gated behind R6 amendment, dry-run default, preview row cap, CDC-safety check; a real load mixes dispositions per kind | 🟡 gated (operator sign-off) |
| E | `AssignedBySink` — assigned-key capture (`OUTPUT`/correlation) feeding `SurrogateRemapContext`, catalog-wide FK re-point | ⚪ later chapter |

**Per-phase risks:**
- *Platform write surface (OPEN-2)* → whether the OutSystems Cloud UAT DB
  permits direct SQL writes to entity-backing tables is the single biggest
  external dependency; confirm before Slice D.
- *Connection apparatus scope (OPEN-7)* → environment count + platform/license
  concurrency (V1's "four connections, two concurrent") gates how rich
  `TransferConnections` must be; confirm before Slice C′.
- *R6 boundary* → the execute path is a new write path; stays UAT-preview +
  dry-run-default and requires the R6 amendment before Slice D ships.
- *Identity disposition mix* → a realistic UAT load mixes `PreservedFromSource`
  (business keys), `ReconciledByRule` (Users; Slice C′), and possibly
  `AssignedBySink` (platform-minted keys; Slice E) across kinds.

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

**Pillar-9 / TransformRegistry arc (chapter A.4.7 + chapter A.4.7'):**

| Chapter | Cash-out | Status |
|---|---|---|
| A.4.7 (CLOSED 2026-05-16) | `TransformRegistry` type-system + 18 classifications (1 adapter + 12 passes + 5 strategies) + 4 of 5 bidirectional property tests + filter helpers (`skeletonView` / `overlayView` / `overlayAxes`) + A41 (metadata totality). | shipped |
| A.4.7' (CLOSED 2026-05-17) | `ComposeState` + `PassChainAdapter` + `RegisteredTransforms.all` / `allChainSteps` / `skeletonChainSteps` + `Compose.project` registry-driven + `Compose.runSkeleton` + skeleton-purity true-execution + `TransformRegistry.digest` + `ManifestEmitter.registry.digest` + `osm emit --skeleton-only` CLI + `let run` private across 12 passes + 5th bidirectional property test (registry-digest round-trip) + A41 amended (execution totality). | shipped |
| forward-signal cash-outs | `applied-transforms` per-artifact manifest field; per-OverlayAxis CLI flags; `Policy.fs` ↔ `OverlayAxis` collapse; emitter-as-chain-step; adapter-as-chain-step; async-streaming compose. | deferred-with-trigger (per `CHAPTER_A_4_7_PRIME_CLOSE.md` forward signals) |

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
| `V1_PARITY_MATRIX.md` | Per-slice rows + Status-history amendments on reclassification | every parity-audit slice + every cash-out slice that retires a 🟠 NOT-MAPPED row |
| `V1_ARCHITECTURE_COMPENDIUM.md` | Per-cluster updates when V1 source changes structurally (rare; the V1 trunk is frozen during cutover window) | when V1 source changes detected |
| `V2_PATTERNS_COMPENDIUM.md` | New pattern entries when a slice codifies one; counter-pattern updates when a deferred pattern lands | every chapter close that earns a new pattern |
| `CUTOVER_READINESS_BRIEF.md` | Per-axis status refresh + blocking-deliverable status updates + risk-register refresh | every chapter close + every Phase 8 slice closure |

**Lead-up refactors — pre-cutover structural work earning a slot:**

These are not blocking T-30 green per se, but each earns a slice slot for hygiene before the workflows that depend on them become hot paths. Sequenced before the workflows that consume them; each is small (≤300 LOC).

| # | Refactor | Matrix row | Acceptance | Rationale | Est. LOC |
|---|---|---|---|---|---|
| ~~LR1~~ | ~~`Module.create` per-module non-empty Kind invariant~~ | ~~42~~ | **SHIPPED 2026-05-18 (slice 5.13.module-non-empty-invariant).** `Module.create` rejects empty `kinds` with `module.kinds.empty`; 2 new unit tests pass; one existing JSON-fixture test grew to match new invariant (validated the bug class LR1 prevents). Per A39. | ~~Closes a V1-parity gap; prevents ghost-module bug~~ — shipped | ~~5~~ |
| LR2 | `Catalog.foreignKeysByTargetKind : Map<SsKey, Reference list>` precomputed at construction (A39 invariant) | 75 | Bench-driven optimization showing FK-resolution is hot path → materialize Map<SsKey, Reference list> on `Catalog.create`; deprecate inline-closure lookup once consumers migrate | Performance optimization; defer until Bench surface flags FK-resolution as hot path; consumers transition from inline-closure to materialized-map | 80 |
| LR3 | `ScriptDomBuild.buildCreateTable` defer-candidates (~~column defaults~~ + ~~CHECK constraints~~ + computed columns + single-column PK inline optimization) | 12 + 53 + 182 (slice ζ residuals) | **PARTIALLY SHIPPED 2026-05-18 (slice 5.13.column-features-emit).** DEFAULT + CHECK clauses emit via `DefaultConstraintDefinition` + `CheckConstraintDefinition` typed-AST builders (rows 12 + 53 close to 🟢 PARITY). Remaining: computed columns (no V2 consumer pressure yet — `Attribute.Computed` populated by no rowset path or JSON adapter); single-column PK inline optimization (V1 67-77; today V2 always emits PK as table-level constraint). | V1's CreateTableStatementBuilder has these axes; V2 IR fields exist; emit-layer remaining bundle deferred per IR-grows-under-evidence | ~~200~~ 80 (remaining axes) |
| LR4 | `ScriptDomBuild.buildCreateIndex` defer-candidates (IgnoreDupKey + DataCompression with partition-range collapse + FileGroup/PartitionScheme dataspace) | 55 + 56 + 183 (slice ζ candidates) | Round-trip parity test on partitioned-index fixture covers all 3 axes | V1's IndexScriptBuilder has these axes; V2 IR fields exist (Index.DataSpace + Index.PartitionCompression + Index.IgnoreDuplicateKey would extend); cash-out: 250 LOC for the on-disk introspection bundle | 250 |
| LR5 | `Tolerance.IgnoreHeaderComments` retirement — emit V1-style `/* Source: ... LogicalName ... */` header in per-table SSDT files | 130 | Canary diff regenerates with header comments present; operator confirms header content matches V1 byte-for-byte | Tolerance variant exists today; retire when operator-facing report-launching depends on header comments for context | 50 |
| ~~LR6~~ | ~~`EmissionPolicy.IncludeTrustedIndicators` — flag for emitting `WITH NOCHECK` on FK constraints~~ | ~~59~~ | **EMIT-SIDE SHIPPED 2026-05-18 (slice 5.13.fk-features-emit).** Not as an EmissionPolicy flag — V2 emits `WITH NOCHECK` structurally when `Reference.IsConstraintTrusted = false`. Pillar 9 framing: trust state is `DataIntent` (preserves source reality), not `OperatorIntent` (Selection/Emission/Insertion/Tightening); no Policy axis needed. | ~~Deployed-target round-trip needs WITH NOCHECK preservation~~ — closed structurally | ~~80~~ |
| ~~LR7~~ | ~~`EmissionPolicy.IncludeUpdateActions` — flag for ON UPDATE referential actions~~ | ~~58~~ | **EMIT-SIDE SHIPPED 2026-05-18 (slice 5.13.fk-features-emit).** Same framing as LR6: ON UPDATE is `DataIntent` (source carries it), not Policy. `Reference.OnUpdate : ReferenceAction option`; emit by default when `Some action`. | ~~Modern T-SQL convention emits explicit ON UPDATE NO ACTION~~ — closed structurally | ~~80~~ |
| LR8 | `Decisions.unified-result` shape — minor refactor consolidating ad-hoc `Result<DecisionSet>` propagation across three sibling passes via a `runDecisionPass` helper (two-consumer threshold met at chapter A.4.7' close; third confirmed slice 5.4.γ.evaluators) | (cross-cutting; no specific row) | All three sibling passes (NullabilityPass / UniqueIndexPass / ForeignKeyPass) delegate to `runDecisionPass` helper; per-pass test coverage unchanged | Per `DECISIONS 2026-05-13 — Two-consumer threshold for emergent primitives`; codified after the fourth instance | 100 |
| LR9 | `ProfileSnapshot.attach` symmetry — V2-side renaming of `Projection.Adapters.Osm.ProfileSnapshot.attach` to better reflect adapter-vs-Core split (currently lives in adapter; could move to Core given the function is pure) | (cross-cutting) | F# tests + Bench surface confirm no perf regression; CatalogReader.parse + canary green | Cleaner module structure; hygiene-only | 30 |
| LR10 | Slice α' — IRBuilders Kind/Module/Catalog sweep tail (19 files; ~80 sites; carbon-copy from chapter 4.9 slice α partial) | (chapter 4.9 slice α' deferred per Phase 5.10 closure) | All sites use IRBuilders mkKind / mkModule / mkCatalog literals; no field-missing errors on the next Kind/Module/Catalog IR-shape change | Future IR-shape changes touch ~2 sites instead of ~80; per IRBuilders Python-pass discipline | 80 |

**Sequence for Phase 8 (T-30 green path) — lead-up refactors interleaved:**

```
Now ──┬─→ Open chapter 4.1.B slice η (global phase ordering + CDC-silence)
      │   └─→ Bundle LR3 + LR4 if emission layer is touched at slice ζ
      │
      ├─→ Open chapter 4.2 slice ε (remaining matching strategies)
      │   └─→ Bundle LR8 if a fourth-strategy slot opens
      │
      ├─→ Ship 5.13.transient-retry + 5.13.exception-class (paired)
      │   └─→ Bundle LR1 if Module.create is touched
      │
      ├─→ Ship 5.13.uat-cli (osm uat-users)
      │
      └─→ Run UAT dry-run ──→ T-30 green ──→ Phase 9 + Phase 10 open
```

The lead-up refactors interleave when their consuming chapters open; none are blockers in themselves.

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
                  │  (as of 2026-05-18 chapter 5 close)  │
                  │  Phases 1–7 + 5.0–5.12 SHIPPED       │
                  │  Pillar 9 + supreme disciplines      │
                  │  Registry load-bearing (A.4.7')      │
                  │  Manifest diagnostic fields (4.4)    │
                  │  V1 parity audit wave CLOSED         │
                  │  185 matrix rows + 22 DECISIONS      │
                  │  + V1 Architecture Compendium        │
                  │  + V2 Patterns Compendium            │
                  │  + Cutover Readiness Brief           │
                  └────────────────┬─────────────────────┘
                                   │
                                   ▼
                  ┌──────────────────────────────────────┐
                  │  Per-axis confidence (V2_DRIVER)     │
                  │  SCHEMA          🟢 V2-DRIVER-READY  │
                  │  PIPELINE-ORCH   🟢 V2-DRIVER-READY  │
                  │  DATA            🟡 V2-AUGMENTED     │
                  │  IDENTITY        🟡 V2-AUGMENTED     │
                  │  DIAGNOSTICS     🟡 V2-AUGMENTED     │
                  │  OPERATOR-AFF    🟡 V2-AUGMENTED     │
                  └────────────────┬─────────────────────┘
                                   │
              ┌────────────────────┴────────────────────┐
              │                                         │
              ▼                                         ▼
┌──────────────────────────┐            ┌──────────────────────────┐
│ Phase 5.13               │            │ Phase 8                  │
│ Parity cash-out wave     │            │ Path to T-30 green       │
│ (~35 NOT-MAPPED rows)    │            │ (3 blocking deliverables)│
│                          │            │                          │
│ Cutover-blocking (T-30): │            │ #1: ch 4.1.B CDC-silence │
│  • 5.13.transient-retry  │            │     + global Phase1/2    │
│    (cutover-critical)    │            │ #2: ch 4.2 slice ε       │
│  • 5.13.cdc-silence      │ ◄──gates── │     remaining matchers   │
│  • 5.13.user-matching    │            │ #3: UAT dry-run on real  │
│  • 5.13.uat-cli          │            │     inventory CSVs       │
│  • 5.13.verify-data      │            │                          │
│                          │            │ Lead-up refactors (LR1-  │
│ Operator-UX (post-T-30): │            │ LR10) interleave when    │
│  • 5.13.remediation-     │            │ their consuming chapters │
│    emitter (V2_DRIVER    │            │ open                     │
│    §154)                 │            └──────────┬───────────────┘
│  • 5.13.summary-formatter│                       │
│  • 5.13.osm-analyze etc. │                       │
│                          │                       │
│ Consumer-pressure:       │                       │
│  • 5.13.live-profiler    │                       │
│  • 5.13.osm-extract      │                       │
│  • OSSYS rowset lifts    │                       │
│  • RemediationEmitter    │                       │
│  • SqlValidator etc.     │                       │
└──────────┬───────────────┘                       │
           │                                       │
           └───────────────────┬───────────────────┘
                               │
                               ▼
                  ┌──────────────────────────────────────┐
                  │  T-30 green achieved                 │
                  │  R6 per-pair V2-driver flip eligible │
                  │  across all six axes                 │
                  │  N=10 consecutive green canary +     │
                  │  operator sign-off per pair          │
                  └────────────────┬─────────────────────┘
                                   │
                                   ▼
                  ┌──────────────────────────────────────┐
                  │  Phase 9 — V2-driver operator        │
                  │  workflow (post-T-30)                │
                  │  Operator-UX cash-outs ship per      │
                  │  workflow trigger                    │
                  │  ~3780 LOC across 10 verbs +         │
                  │  3 emitters + 2 infra pieces         │
                  └────────────────┬─────────────────────┘
                                   │
                                   ▼
                  ┌──────────────────────────────────────┐
                  │  Cutover (~late July 2026)           │
                  │  V2-driver per pair OR V2-augmented  │
                  │  if any pair stays 🟡                │
                  │  V1 stays warm through cutover+30    │
                  └────────────────┬─────────────────────┘
                                   │
                                   ▼
                  ┌──────────────────────────────────────┐
                  │  cutover+30 — Phase 10 / V1 sunset   │
                  │  Three-condition gate (§ VIII)       │
                  │  13 ⚫ V1-SUNSET matrix rows retire  │
                  └──────────────────────────────────────┘
```

**Parallelization notes (as of 2026-05-18 chapter 5 close):**

- **Phase 5.12 V1 Parity Audit Wave** closed at chapter 5.8. 185 matrix rows + 22 DECISIONS entries + 3 synthesis documents. The wave produced the structural answer to "how confident are we that V2 covers V1's surface?" — confidence is high, with named cash-out paths for every 🟠 NOT-MAPPED row.
- **Phase 5.13 (cash-out implementation wave)** opens immediately upon chapter 5 close. The wave's slices are independently triggered — pick what matches session capacity per the priority lists in Phase 5.13.
- **Phase 8 (Path to T-30 green)** is the structured pathway: three blocking deliverables + ~10 lead-up refactors. Sequence per the inline ASCII flow in Phase 8.
- **Phase 9 (V2-driver operator workflow)** opens when T-30 green achieved. Operator-UX cash-outs ship per workflow trigger; no single slice gates the chapter open — operator demand surfaces the trigger.
- **Phase 10 (V1 sunset)** opens at cutover+30 if the three-condition gate (§ VIII) holds.
- **Lead-up refactors LR1-LR10** are not blockers per se; each earns a slot when its consuming chapter opens.
- **Cross-chapter coordination remains:** chapter 4.1.B slice ζ (`5.13.cdc-silence` + global ordering) depends on UserRemapContext from chapter 4.2 slice ε for full operator-reality acceptance; lab-fixture acceptance ships independently.

**Remaining estimate as of 2026-05-18:**

- **Phase 8 (T-30 green path):** 3 blocking deliverables + 10 lead-up refactors. Estimate ~4-6 sessions for blocking deliverables (chapter 4.1.B slice η + chapter 4.2 slice ε + UAT dry-run) + ~3 sessions for highest-priority lead-up refactors (LR1 + LR3 + LR8). Total: ~7-9 sessions at session cadence.
- **Phase 9 (operator workflow):** ~10 cash-outs estimated at ~3780 LOC total. Estimate ~10-15 sessions as operator demand triggers fire.
- **Phase 5.13 cash-outs (consumer-pressure):** indefinite cadence; ~25 NOT-MAPPED rows with concrete cash-out shapes; each ships as trigger fires.

**Cutover timing:** late July 2026 per `VISION.md`. T-30 green target: early-mid July (assuming chapter 4.1.B + 4.2 sliceε close + UAT dry-run by then).

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

### V1 parity audit + synthesis surfaces (chapter 5 wave; 2026-05-17 → 2026-05-18)

- **`V1_PARITY_MATRIX.md`** — 185 rows; six classification statuses; per-row cash-out shape + dependencies + acceptance + trigger; status-history amendments for reclassifications. The structural answer to "how confident are we that V2 covers V1's surface?"
- **`V1_ARCHITECTURE_COMPENDIUM.md`** (563 lines) — consolidated "what V1 actually does" reference; per-section walks (Ingest / Analyze / Emit / Orchestrate / Operate / Compare); file inventories with purpose; end-to-end V1 build-ssdt invocation narrative; 11 cross-cutting structural patterns V1 carries; V1 surfaces V2 does NOT inherit. Audience: future V2 agents opening V1-translation chapters.
- **`V2_PATTERNS_COMPENDIUM.md`** (452 lines) — 26 architectural patterns in 5 sections (Foundational disciplines / Algebraic / Composition / Emission / Cutover-discipline); each with What / When / How V2 uses it / Worked example / Counter-pattern; quick-reference design table mapping tasks to patterns. Audience: contributors designing new V2 components.
- **`CUTOVER_READINESS_BRIEF.md`** (368 lines) — operator-facing per-axis confidence assessment (six axes; current status + shipped + gated + sunset + acceptance per axis); composite verdict; path to T-30 green; per-environment R6 progression; five open risks with mitigations. Audience: principal-PO + cutover ops.

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
