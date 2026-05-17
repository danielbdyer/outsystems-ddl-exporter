# BACKLOG — Operational backlog for V2 cutover

**Status:** re-canonicalized 2026-05-16; per-phase status refreshed
2026-05-17 (post-chapter-A.4.7' doc-refresh hygiene). Sibling to
`V2_DRIVER.md`. The operational ledger that interweaves V2_DRIVER's
per-phase chapter sequence with V1 inheritance opportunities (carbon-copy
candidates and shipped carbon-copies), cross-cutting infrastructure work,
and the risk register.

**Current state as of 2026-05-17 (post-chapter-4.6 close).** V2-driver
critical-path Phases 1–7 are all closed end-to-end. Chapters 4.4
(Manifest diagnostic fields) + 4.5 (Index IR fidelity) + **chapter 4.6
(forward-signal cleanup bundle)** closed 2026-05-17. **All four of
chapter 4.4's always-false PredicateName variants now lift to real
V2 IR evaluation** — `HasFilteredIndex` + `HasIncludedIndexColumns`
(chapter 4.5) plus `HasLogicalForeignKeyWithoutDbConstraint` +
`HasLogicalForeignKeyWithDbConstraint` (chapter 4.6 slice α). One of
four A.0' deferred concepts retired by chapter 4.6 slice β
(`IsPlatformAuto`). Chapter 4.5 silent-skip Q3 deferral closed by
chapter 4.6 slice γ (filter-parse Diagnostic helper). `PreRemediation`
stays empty per V2_DRIVER §154. See §VII Sequencing graph for the
current fan-out.

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
  - [Phase 6 — DACPAC dev-tooling (chapter 3.x; closed)](#phase-6--dacpac-dev-tooling-chapter-3x-closed-under-reframe)
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
                  │  (as of 2026-05-17 post-chapter-4.4) │
                  │  Phases 1–7 + 5.5 SHIPPED end-to-end │
                  │  Pillar 9 + supreme operating        │
                  │  disciplines in place                │
                  │  Registry load-bearing for execution │
                  │  (chapter A.4.7' close)              │
                  │  Manifest diagnostic fields retired  │
                  │  (chapter 4.4 close)                 │
                  └────────────────┬─────────────────────┘
                                   │
                                   ▼
                  ┌──────────────────────────────────────┐
                  │  V2-driver KPI structural surface    │
                  │  operationally complete              │
                  │  CDC silence ✓  Schema ✓  Identity ✓ │
                  │  Diagnostics ✓  DACPAC ✓  Rowsets ✓  │
                  │  Manifest diagnostic fields ✓        │
                  └────────────────┬─────────────────────┘
                                   │
              ┌────────────────────┴────────────────────┐
              │                                         │
              ▼                                         ▼
┌──────────────────────┐                ┌──────────────────────┐
│ Deferred-with-       │                │ Phase 8 —            │
│ trigger queue        │                │ pragmatic close      │
│ (per-chapter         │                │ (consumer-           │
│  close docs)         │                │  pressure-driven)    │
│                      │                │                      │
│ Module.ExtProps      │                │ F# Analyzers SDK     │
│ Sequence emit        │                │ Coordinates St.2     │
│ Statement DU         │                │ Hex port lifts       │
│  MERGE/UPDATE        │                │ V1 sunset plan       │
│ Slice 4.3.δ/ε       │                │ Cutover runbook      │
│ Slice 3.x.ε/ζ       │                │                      │
│ 4.2 OSSYS user-K     │                │ RemediationEmitter   │
│ 4.2 CSV adapter      │                │  (V2_DRIVER §154)    │
│ PredicateName 4-var  │                │                      │
│ Unsupported widen    │                │                      │
└──────────┬───────────┘                └──────────┬───────────┘
           │                                       │
           └───────────────────────────────────────┘
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

**Parallelization notes (as of 2026-05-17):**

- The V2-driver critical-path Phases (1–5 + 7) are all closed. The
  load-bearing chapter dependencies that motivated the parallel-vs-
  sequential consideration (chapter 4.1.B slice ζ depending on chapter
  4.2's `UserRemapContext`) are all resolved in shipped state.
- **Chapter 4.4 (Manifest diagnostic fields)** is the largest piece of
  named pending V2_DRIVER work; it's structurally independent of every
  other lane. ManifestEmitter currently emits `Coverage` /
  `PredicateCoverage` / `PreRemediation` / `Unsupported` as
  `null` / defaults; the chapter fills them under per-axis property
  test coverage.
- **Deferred-with-trigger** items are consumer-pressure-driven; they
  reopen when a real workflow demands them (Module.ExtendedProperties
  emission gated on V1 confirmation; Sequence emission gated on V1
  fixture surfacing; Statement DU MERGE/UPDATE promotion gated on
  third consumer; etc.).
- **Phase 8** (pragmatic close + cutover+30 gate) opens at the
  cutover-15 to cutover+30 window per the T-30 / T-15 fallback ladder
  gates discipline.

**Remaining estimate as of 2026-05-17:** chapter 4.4 is the next named
substantive chapter (~1-2 weeks at session cadence). Deferred-with-trigger
items add up to ~1-2 sessions each as triggers fire. Phase 8 timing is
cutover-relative.

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
