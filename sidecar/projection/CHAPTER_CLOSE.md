# Chapter-close report — V2 sidecar after eleven sessions

**Audit date:** 2026-05-13 (session 12).
**Scope:** the V2 sidecar at `/home/user/outsystems-ddl-exporter/sidecar/projection/` after eleven build-and-validate sessions.
**Disposition:** leave clean ground, not perfect ground. Findings documented; resolutions belong to the next chapter.

This document is the bridge between what the prior chapter knows and what the next chapter needs to inherit. It is the synthesis of five parallel audits (V1 input contracts, V1 output contracts, V1 test coverage, architectural-doc drift, build-graph and dependency hygiene) plus my own accumulated judgment from sessions 1–11.

---

## 1. Confirmations — disciplines that held under audit

These are the architectural commitments the audit checked and found honored.

### 1.1 F#-pure-core / no-I/O-in-Core

`Projection.Core` has zero I/O calls. The audit searched for `File.`, `Directory.`, `Console.`, `Environment.`, `DateTime.Now`, `DateTimeOffset.Now`, `DateTime.UtcNow`, `Random`, `Path.GetTempPath`, `System.Net`, `StreamReader`, `StreamWriter` and found none. Mutable state inside Core passes (Tarjan SCC scratch, `ResizeArray` accumulators in `NamingMorphism`, `SymmetricClosure`, `TopologicalOrderPass`) is strictly function-local — no module-level `let mutable` exists. `DateTimeOffset` appears only as a type signature (`ProbeStatus.CapturedAtUtc`); the clock value is supplied from outside the core. Pure-shell discipline preserved despite imperative implementation choices for performance-sensitive algorithms.

### 1.2 Strategy-layer placement matches the codification

All six strategy modules (`CycleResolution`, `NullabilityRules`, `UniqueIndexRules`, `ForeignKeyRules`, `CategoricalUniquenessRules`, `Composition`) live under `src/Projection.Core/Strategies/`. All ten pass drivers live under `src/Projection.Core/Passes/`. Type-bearing files (`Result`, `Identity`, `Catalog`, `Profile`, `Policy`, `TopologicalOrder`, `Lineage`) sit at the project root. No orphans. The DECISIONS 2026-05-11 placement decision held empirically through the addition of two more strategy modules (`CategoricalUniquenessRules` in session 11, `Composition` from the cash-out commit).

### 1.3 Sibling Π independence

Three Π emitters confirmed independent. Signatures verified type-level:
- `RawTextEmitter.emit : Catalog -> string`
- `JsonEmitter.emit : Catalog -> string`
- `DistributionsEmitter.emit : Catalog -> Profile -> string`

No Policy parameter on any emit function. No shared mutable state across emit invocations (each call allocates its own `MemoryStream` / `Utf8JsonWriter` / `StringBuilder`). No I/O leak from inside emit. T11 sibling commutativity is exercised in the test suite at multiple call sites including the rich-profiling end-to-end milestone.

The amended A18 ("Π consumes whichever subset of `Catalog × Profile` it needs, but never Policy") holds across all three.

### 1.4 Composition-pattern adherence

All four registered-intervention pass drivers (`NullabilityPass`, `UniqueIndexPass`, `ForeignKeyPass`, `CategoricalUniquenessPass`) delegate to `Composition.fanOut`. Each pass is now a thin wrapper (15–20 lines of `FanOutConfig` glue) over the canonical primitive. The `StrategyEvaluator<'context, 'config, 'decision>` alias from session-11 commit 5 is honored as the type of `FanOutConfig.Evaluate`. The session-8 deferred-decisions cash-out is empirically clean.

### 1.5 Project reference graph (inward flow)

Edges:
```
Projection.Adapters.Sql        → Projection.Core
Projection.Targets.SSDT        → Projection.Core
Projection.Targets.Json        → Projection.Core
Projection.Targets.Distributions → Projection.Core
Projection.Tests               → { Core, SSDT, Json, Distributions, Adapters.Sql }
```

`Projection.Core` references nothing (zero outgoing). No circular references. No cross-target sibling references (Targets.* don't reference each other). The dependency direction is inward as the codification requires.

### 1.6 Decision-set + lineage discipline survives the codification

Every registered-intervention pass produces a decision set per A32 and emits `Annotated` lineage events on actual decisions. Every `match` over the four binary/ternary outcome DUs (`NullabilityOutcome`, `UniqueIndexOutcome`, `ForeignKeyOutcome`, `CategoricalUniquenessOutcome`) is exhaustive at the points where exhaustiveness matters (the `outcomeLabel` rendering helpers in each pass driver). Closed-DU expansion empirical-test discipline (DECISIONS 2026-05-13) holds for all three additions: `IsMandatory` variant on Attribute (session 9), `Numeric` variant on AttributeDistribution (session 10), `CategoricalUniqueness` variant on TighteningIntervention (session 11).

### 1.7 Test totals

585 tests passing, 3 expected V2 divergence skips, 588 total. The test base has tripled since session 6's milestone.

---

## 2. Drift — where V2's mental model diverges from V1 as it actually exists, or where V2's documentation diverges from its own code

### 2.1 ADMIRE entry status drift (HIGH severity)

Five of nine ADMIRE entries carry status strings that no longer reflect the V2 code state:

| ADMIRE entry | Current status | Actual state | Disposition |
|---|---|---|---|
| `UniqueIndexDecisionOrchestrator` (2026-05-10) | `admired (placement decided)` | Extracted (`UniqueIndexRules.fs` + `UniqueIndexPass.fs` exist; tests green; DECISIONS 2026-05-11 strategy table calls it extracted) | V2-correct, ADMIRE-stale |
| `ForeignKeyEvaluator` (2026-05-11) | `admired (placement decided)` | Extracted (modules exist; DECISIONS 2026-05-11 strategy table calls it extracted; cash-out instances reference it) | V2-correct, ADMIRE-stale |
| `EntityDependencySorter` (2026-05-07) | `admired (placement decided)` | Extracted as `TopologicalOrderPass` + `Strategies/CycleResolution`; DECISIONS 2026-05-08 entry presupposes completion | V2-correct, ADMIRE-stale |
| V1 profiling depth (2026-05-12) | `admired (gap analysis; no V1 component to migrate)` | Categorical + Numeric distributions extracted, `ProfileStatistics` adapter exists, `DistributionsEmitter` exists | V2-correct, ADMIRE-stale |
| `CategoricalUniqueness` (2026-05-13) | `admired (placement decided) — hybrid mode` | Extracted (modules exist; end-to-end milestone passes) | V2-correct, ADMIRE-stale |

**Resolution direction (next chapter):** a single sweep updating each entry's status line to use the canonical `extracted (differential confirmed)` string per DECISIONS 2026-05-09 — Pattern setters explicitly named.

### 2.2 README.md is materially stale (HIGH severity)

The README still describes session-1 architecture:
- Says "all effects live at the boundary, in **C#** adapters" (line 21) — adapters are F# since DECISIONS 2026-05-09 (Adapter language rule supersedes the original "F# core / C# shell" framing). The codebase shows zero `.cs` files in `src/Projection.Adapters.Sql/`.
- Says Policy is **three orthogonal axes** (line 55) — Policy is four axes since DECISIONS 2026-05-09 (Policy.Tightening as fourth top-level axis).
- Lists Targets.SSDT/Json/Adapters.Sql as projects that "follow in subsequent sessions" (line 39) — all three exist.
- No mention of `Strategies/` folder, `Composition.fs`, `fanOut`, `StrategyEvaluator`, rich profiling, or `DistributionsEmitter`.

The README is the first document a new agent reads. **Resolution direction:** README pass that absorbs the cumulative decisions. This is the single highest-leverage doc action for the next chapter.

### 2.3 AXIOMS.md opening summary doesn't reflect V2 amendments (MEDIUM severity)

Line 7: "The system has thirty-one axioms (A1–A31) generating ten theorems (T1–T10)." The file then defines A32, A33, A34, T11. Reader who trusts the opening summary misses the V2 extensions until they reach the appended sections.

A18's amendment (the load-bearing "Π consumes evidence subsets, never Policy" refinement, session-11 critical) lives at the bottom of AXIOMS.md, separated from A18 original by ~340 lines and other amendments. The original A18 has no forwarding pointer.

**Resolution direction:** rewrite the opening to acknowledge V2 extension; add a one-line forwarding pointer at A18 original to its amendment.

### 2.4 Three-mode admire framework adopted retroactively but not propagated (MEDIUM severity)

DECISIONS 2026-05-13 introduced the three-mode framework (V1-migration / V2-growth / hybrid) and said "future admire entries name their mode at the top." The 2026-05-13 CategoricalUniqueness entry follows this. The 2026-05-12 rich-profiling entry (named retroactively in DECISIONS as the V2-growth template) does not. Five earlier entries (predating the framework) also don't.

Only one of nine entries strictly follows the rule.

**Resolution direction:** a one-line annotation on each existing entry's status line naming its mode under the framework. Cheap.

### 2.5 V1 outputs without V2 equivalents (HIGH severity for next-chapter scoping)

V1 emits the following artifacts that V2 has no counterpart for. This is the V2 backlog made visible:

| V1 output | V2 status | Notes |
|---|---|---|
| Real CREATE TABLE (`Modules/<module>/<schema>.<table>.sql` via SMO/ScriptDom) | Not built | Synthetic-milestone raw text exists; DacFx-backed real form deferred per DECISIONS 2026-05-06 |
| Indexes (CREATE INDEX) | Not built | `IndexScriptBuilder` 451 lines; no V2 sibling |
| Extended properties (MS_Description) | Not built | `ExtendedPropertyScriptBuilder` 142 lines; no V2 sibling |
| Triggers (column-name-mapped) | Not built | `PerTableWriter.BuildTriggerScripts` no V2 sibling |
| Static-entity MERGE seed scripts (`Seeds/*.sql`) | Not built | V2 only renders rows as `--` comments inside the catalog text |
| Dynamic-entity INSERT/MERGE (batching, IDENTITY_INSERT, phased FK loading) | **Entire data-emission lane empty** | Two large generators in V1 (`DynamicEntityInsertGenerator` 788 lines, `PhasedDynamicEntityInsertGenerator` 498 lines) |
| `<projectName>.sqlproj` MSBuild XML | Not built | Coupled to DacFx-milestone |
| `manifest.json` (artifact + policy + coverage report) | Not built | V2's `JsonEmitter` is a *catalog* projection, not a manifest — different ontology |
| `decision-log.json` (PolicyDecisionLogWriter) | Not built | Awaits Diagnostics writer per DECISIONS 2026-05-06 |
| `opportunities.json` + `validations.json` + `suggestions/*.sql` | Not built | Same Diagnostics-writer deferral |
| `dmm-diff.json` | Not built | Same Diagnostics-writer deferral |
| `PostDeployment-Bootstrap.sql` template | Not built | Coupled to data-emission lane |
| Full-export run manifest | Not built | Top-level orchestration artifact |

**The Diagnostics writer** (DECISIONS 2026-05-06 — Diagnostics live in a writer parallel to Lineage) is the most consequential un-built primitive. It blocks V2 equivalents for `decision-log.json`, `opportunities.json`, `validations.json`, `dmm-diff.json`, the opportunity-stream half of UniqueIndex, the operator-approval handoff for FK, the V1 nullability `Analyze()` pipeline, and the silent-skip surfacing for adapter coordinate-mismatches.

### 2.6 Transform registry deferral fired without cash-out (HIGH severity)

DECISIONS 2026-05-06 (Transform registry deferred until N≥4 passes) committed to revisit at N=4. The codebase has 10 passes in `src/Projection.Core/Passes/`. No subsequent DECISIONS entry either builds the registry or re-defers with explicit rationale. The 2026-05-11 strategy-layer codification deferred a *strategy* registry separately; that is a different concern.

**Resolution direction:** log a fresh DECISIONS entry either (a) building the registry now or (b) explaining that the original framing (single linear pipeline composed via `>>`) was overtaken by the per-use-case driver pattern that V2 evolved into, so the registry's value-prop never materialized. Either disposition is fine; neither is currently logged.

### 2.7 Skip-stub asymmetry across V2 test files (MEDIUM severity)

`V1NullabilityParityTests.fs` is the canonical pattern for "V1 contract V2 deliberately doesn't honor": three Skip-stubs with explicit rationale (Aggressive mode collapse × 1; Diagnostics-writer split × 2). The discipline makes V2 divergences visible in test discovery and dotnet-test output, not buried in admire-table prose.

The pattern is not applied consistently:
- **`UniqueIndexPassTests.fs` lacks Skip stubs** for two ADMIRE-documented divergences: `AggressiveModeWithoutEvidenceRequiresRemediation`, `EvidenceModeTreatsIncludedColumnsAsSingleColumnIndex`.
- **`ForeignKeyPassTests.fs` lacks a Skip stub** for the `DeleteRuleIgnore` rationale-string-on-success divergence.
- **`TopologicalOrderPassTests.fs` lacks a Skip stub** for the `SortByForeignKeys_ResolvesSanitizedEffectiveNames` boundary concern.

Plus two genuine **MISSING** V2 contracts in TopologicalOrderPass:
- `SortByForeignKeys_SkipsAutoDetectionWhenManualCyclesExist` (manual cycle override)
- `SortByForeignKeys_DefersJunctionTablesWhenEdgesMissing` (junction-table deferral)

The ADMIRE entry promises both as Behavioral V2 translations; neither is implemented. The junction-deferral one is more concerning given ADMIRE explicitly flags "junction-table heuristic has false positives" as an Edges/risk.

### 2.8 Undocumented V1↔V2 divergences in adapters and emitters (MEDIUM severity)

Adapter side:
- **Static cell coercion:** V1's `FixtureStaticEntityDataProvider.ConvertJsonValue` does type-aware decoding (Boolean, Integer, Decimal, DateOnly, TimeOnly, DateTime, DateTimeOffset, Guid) using catalog `DataType`. V2's `Static.fs` `invariantString` only handles raw JSON primitive kinds. Documented in the adapter file's comments; no DECISIONS entry. Real-fixture decimals and dates may differ.
- **`Reference` vs `Ref` docstring:** `ProfileSnapshot.fs` docstring shows `"Ref"` but V1 emits `"Reference"`. Parser accepts both. Cosmetic; V2-correct.
- **`Unknown` ProbeOutcome silently re-mapped to `FallbackTimeout`** (`ProfileSnapshot.fs:73`). The semantic is different from `FallbackTimeout` (V1 says "no probe ran"; V2's `FallbackTimeout` says "probe started, timed out"). Worth a DU variant or at least a parser warning.
- **Composite-unique probe status fabricated** (`ProfileSnapshot.fs:256-258`). V2 synthesizes `Succeeded UnixEpoch 0L` because V1's serializer emits no probe status for composite candidates. The fabrication is indistinguishable from real data downstream.

Emitter side:
- **PK / FK constraint naming:** V2 hard-codes `FK_<rootKey>`; V1 has rename-aware naming. No DECISIONS entry.
- **Default SQL-type mapping is hard-coded constants** in `RawTextEmitter` (`INT`, `DECIMAL(18, 4)`, `NVARCHAR(MAX)`). Comment says "These belong in Policy when Policy lands." No DECISIONS entry.
- **`Restrict` collapses to `NoAction`** in V2's renderers; V1 maps `SetDefault` separately. V2's collapse is likely correct (SQL Server: Restrict is encoded as NO ACTION) but no DECISIONS audit trail.
- **`SetDefault` has no V2 case** in `ReferenceAction`-rendering helpers. V1 maps it.
- **No `GO` batch separators in V2 SSDT output.** V1's `StatementBatchFormatter` always inserts them.
- **Inconsistent JSON shape across the two V2 JSON emitters:** `JsonEmitter` nests `physical: {schema, table}`; `DistributionsEmitter` flattens `schema`/`table` directly on `kind`. Pick one before downstream consumers calcify the difference.

### 2.9 `Cautious → AllowNoCheckCreation` rename promised in ADMIRE, missing from DECISIONS

ADMIRE 2026-05-11 (ForeignKey Edges/risks L1085–L1091) promised "document the rename in DECISIONS when commit 5 lands." The precedent is `AllowCautiousNullabilityRelaxation → AllowMandatoryRelaxation` (DECISIONS 2026-05-09 — V1→V2 name mapping). The FK rename has no DECISIONS entry.

### 2.10 OSSYS catalog producer has no V2 adapter and no ADMIRE stub

V1's `outsystems_metadata_rowsets.sql` → `MetadataSnapshotRunner` → `SnapshotJsonBuilder` produces the `osm_model.json` document. No V2 adapter consumes it; V2 catalogs are currently built by Projection.Tests fixtures. There is no ADMIRE entry mapping the V1 catalog producer to a V2 boundary.

This is the implicit assumption underneath the entire V2 architecture: V2 *will* eventually consume real OutSystems metadata via this path. The audit found no document earmarking the work.

### 2.11 `EntitySeedDeterminizer` ADMIRE entry doesn't acknowledge `StaticAdapterDifferentialTests.fs`

The EntitySeedDeterminizer ADMIRE entry promises differential tests "when the Catalog Reader exists." `StaticAdapterDifferentialTests.fs` IS that landing — its three `V1 contract:`-prefixed tests directly assert the V1 fixture round-trip. The ADMIRE entry was never updated. This is the most concrete admire-staleness finding — it's not just status drift, it's "promised work landed, the promise is unmarked."

---

## 3. Surprises — things neither predicted nor planned for

### 3.1 The codification reached stability faster than predicted

Session 8's codification carried three refinements during validation. Session 11's third real test (CategoricalUniqueness — distribution-aware, structurally different from the V1-inheritance flavor of the previous three strategies) absorbed the new domain without requiring a fourth refinement. The four core predictions held without amendment. This is what "the codification holds" looks like empirically: a test could have surfaced something, did not, and the absence of finding is itself the finding. The pattern is now load-bearing in a way it wasn't at session 10 close.

This was a possibility, not a prediction. It earns the strategy layer its place across the rest of V2; future authors can absorb the AttributeKey-as-first-field convention and trust the codification works the way it claims to.

### 3.2 Continuous evidence absorbed by discrete rationale variants

The session-11 reflection found that distribution-aware decision logic did NOT stress the structured-rationale DU pattern in the way the user's session-11 brief anticipated. Confidence didn't surface as a separate dimension because the keep-reason variants themselves model discrete confidence bands (`NoCategoricalEvidence` → `EvidenceMissing` → `VocabularyTruncated` → `DistinctCountBelowThreshold` → `DuplicatesObserved` → positive). Continuous evidence (distinct-count, percentiles) flows in; discrete variants come out.

This generalizes. **Structured-rationale DUs absorb continuous evidence by adding variants at meaningful inflection points, not by carrying parametric confidence values.** `VocabularyTruncated` distinct from `EvidenceMissing` is a worked example — finer evidence becomes a finer variant, not a confidence number on a coarser variant. Future strategy authors benefit from knowing that "this evidence is continuous; we need confidence" is a question with a structural answer (add the right variant), not necessarily a parametric one.

This deserves an AXIOMS-level operational principle entry — see DECISIONS update below.

### 3.3 fanOut refactor preserving 570 tests through four-driver substitution

The `fanOut` extraction in session-11 commit 4 substituted the body of four pass drivers; not a single pre-existing test broke. This is mechanical confirmation that the pattern was already there — `fanOut` just gave it a name. The composition-vocabulary discipline (codify what's earned at N=2; defer what hasn't) had its first real test and the discipline held: one of five candidates extracted; four deferred with explicit forward triggers.

### 3.4 Π signature variation as a real architectural axis

When `DistributionsEmitter` was added in session 9, its `Catalog -> Profile -> string` signature differed from SSDT and JSON's `Catalog -> string`. This was treated as an A18 amendment (DECISIONS 2026-05-12). The amendment empirically validated: each Π consumes whichever subset of `Catalog × Profile` it needs. Future Π (Faker, anomaly reports) inherit this asymmetry between evidence (Catalog, Profile — Π may consume) and intent (Policy — Π may not consume).

### 3.5 V1's empty-set source for distribution evidence

The session-9 admire surfaced V1 absence as the gap, not V1 logic to migrate. The first "no-V1-source" admire entry. The architectural consequence: rich profiling is **growth, not migration.** Future evidence types follow the same template — no V1 source to mirror; V2 boundary defined by the V2 shape itself.

This is reified in the three-mode admire framework (DECISIONS 2026-05-13) — V1-migration / V2-growth / hybrid. The framework wasn't predicted at session 1; it emerged at session 9 and was named at session 13.

### 3.6 The Composition primitive has no consumers outside Core

`Composition.fanOut` is used by exactly four callers (the four registered-intervention pass drivers). Nothing else in the codebase composes strategies. This is correct for the chapter close: at N=4 the abstraction earned its place, at N=4 it has no cross-project consumers. If a future chapter needs to compose strategies across project boundaries (e.g., a `Projection.Targets.Faker` that consumes both Categorical and Numeric distribution decisions to drive synthetic generation), the primitive may need to migrate from `Strategies/` to a more public namespace.

### 3.7 Embedded-pattern phenomenon in ADMIRE entries

The `Projection.Adapters.Sql.Static` adapter is named in DECISIONS 2026-05-09 as a canonical pattern setter, but it has no separate ADMIRE entry — it's embedded inside `EntitySeedDeterminizer`'s admire. This made the 2026-05-09 ADMIRE-cross-reference text ("the second [extracted] is implicit in this same status") confusing. The convention "every admired thing gets its own entry" hasn't held universally.

---

## 4. Recommended priorities for the next chapter

Ranked. Top three are doc hygiene (cheap, high-leverage); next several are architectural follow-ups; final ones are the bigger backlog items.

### Priority 1 — README.md absorbs the eleven-session state

**Action:** rewrite README.md sections covering project structure, language partition, and Policy axes to reflect the cumulative DECISIONS.
**Why first:** the next-chapter agent reads this document before anything else. Stale framing here misroutes everything that follows.
**Cost:** ~1 hour focused doc work.
**Specifics:**
- Update line 21–22 to say "F# adapters at the boundary" not "C# adapters" (per DECISIONS 2026-05-09)
- Update line 39–45 to show actual project layout (Strategies/, Passes/, three Targets, F# Adapters.Sql)
- Update line 55–56 to describe four-axis Policy (per DECISIONS 2026-05-09 amendment)
- Add a paragraph naming the strategy layer (DECISIONS 2026-05-11), the rich-profiling vector (DECISIONS 2026-05-12), and the composition primitives (DECISIONS 2026-05-13)

### Priority 2 — ADMIRE status sweep

**Action:** update five entries' status lines from `admired (placement decided)` to `extracted (differential confirmed)` per DECISIONS 2026-05-09.
**Targets:** UniqueIndex, ForeignKey, EntityDependencySorter, V1 profiling depth, CategoricalUniqueness.
**Annotate:** each entry's mode under the three-mode framework (DECISIONS 2026-05-13). One line per entry.
**Also:** the EntitySeedDeterminizer entry should acknowledge `StaticAdapterDifferentialTests.fs` as the differential landing it promised.
**Why second:** ADMIRE.md is the V1↔V2 bridge document; it should accurately reflect what's been done.
**Cost:** ~30 minutes.

### Priority 3 — Skip-stub completion across V2 test files

**Action:** mirror the `V1NullabilityParityTests.fs` Skip-stub pattern in three other test files for documented V2 divergences:
- `UniqueIndexPassTests.fs`: Aggressive mode (V1 `UniqueIndexDecisionStrategyTests.cs:34`); included-columns boundary (`:93`)
- `ForeignKeyPassTests.fs`: `DeleteRuleIgnore` rationale-string emission on success (V1 `ForeignKeyEvaluatorTests.cs:139`)
- `TopologicalOrderPassTests.fs`: sanitized-effective-names boundary (V1 `EntityDependencySorterTests.cs:626`)
**Why third:** makes V2 divergences visible in test discovery rather than buried in ADMIRE prose. Cheap to add; preserves the discipline that paid off in the Nullability migration.
**Cost:** ~1 hour.

### Priority 4 — Two missing V2 TopologicalOrderPass tests

**Action:** add behavioral V2 tests for two V1 contracts ADMIRE promises but that are missing from V2:
- `SortByForeignKeys_SkipsAutoDetectionWhenManualCyclesExist` (manual-cycle override)
- `SortByForeignKeys_DefersJunctionTablesWhenEdgesMissing` (junction-deferral)
**Why fourth:** these are not deliberate skips — they are V1 contracts ADMIRE says V2 honors but doesn't lock down. The junction-deferral one is especially concerning given ADMIRE flags "junction-table heuristic false positives" as a known risk.
**Cost:** moderate — depends on whether `OrderingPolicy` already supports these knobs in V2 IR.

### Priority 5 — Transform registry cash-out

**Action:** log a fresh DECISIONS entry either (a) building the transform registry that DECISIONS 2026-05-06 deferred at N≥4 (we are at N=10), or (b) explaining why the original framing was overtaken by the per-use-case driver pattern V2 evolved into.
**Why fifth:** an explicit deferral with a fired trigger is unfinished business. Leaving it un-cashed-out means future agents may rebuild the registry under "IR grows under evidence" and find the deferral never resolved.
**Recommended disposition:** option (b) — log that the registry's value-prop (single linear pipeline composed with `>>`) never materialized because V2 ended up with per-use-case driver functions that compose passes ad-hoc.
**Cost:** ~30 minutes thinking + DECISIONS entry.

### Priority 6 — Diagnostics writer scoping

**Action:** scope the Diagnostics writer (DECISIONS 2026-05-06) as a real next-chapter milestone or re-defer with explicit rationale.
**Why sixth:** the Diagnostics writer is the gating dependency for V2 equivalents of `decision-log.json`, `opportunities.json`, `validations.json`, `dmm-diff.json`, the opportunity-stream half of UniqueIndex, and the operator-approval handoff for FK/Nullability. Without it, the V2 backlog can't continue meaningfully on the policy/decision-output axis.
**Cost:** scoping pass, not implementation.

### Priority 7 — OSSYS catalog adapter ADMIRE stub

**Action:** create an ADMIRE entry for `outsystems_metadata_rowsets.sql` → `osm_model.json` and earmark the V2 catalog adapter.
**Why seventh:** this is the assumed-but-not-documented V1→V2 boundary for the catalog itself. Right now V2 tests build catalogs from F# fixture builders; production V2 will need to consume real OutSystems metadata via this adapter.
**Cost:** an admire entry; the implementation is a separate (large) chapter of work.

### Priority 8 — Faker emitter (deferred)

**Action:** when the chapter's other priorities are settled, scope the Faker synthetic-data emitter (DECISIONS 2026-05-12 forward signal; reaffirmed in session-11 reflection).
**Why deferred:** "two evidence types + four strategies + sibling-Π discipline" is the architectural precondition; all three are met. But session 11's reflection notes that Faker should wait until at least three evidence types exist (currently two: Categorical, Numeric).
**Recommended:** add a third evidence type (Temporal density, or Joint distributions across FK pairs) before Faker, OR proceed with Faker on two evidence types and accept the limitations.

### Priority 9 — Build-graph and dependency hygiene cleanups

Lower-priority items the build-graph audit surfaced:
- `TighteningPolicy` filter-helper wildcards (Policy.fs:439–505) silently swallow future variants. Consider whether explicit exhaustiveness or comment annotations would help.
- `DistributionsEmitter.emitFromInput` accepts `ProjectionInput` (which contains Policy). Operationally fine (it ignores Policy) but a hazard surface. Consider a regression test asserting `emit input.Catalog input.Profile = emitFromInput input` regardless of `input.Policy`.
- Reserved-but-unreachable DU variants (`ForeignKeyKeepReason.CrossCatalogBlocked`; `ForeignKeyRules.isIgnoreRule` always-false) — intentional per the V1 parity discipline; flag for "don't strip dead code" awareness.

### Priority 10 — Adapter / emitter divergences without DECISIONS audit trail

Several V1↔V2 divergences in adapters and emitters lack DECISIONS entries (Section 2.8 above). Most are V2-correct but undocumented. Consider a single DECISIONS entry batch-documenting them as "synthetic-milestone divergences pending real-fixture validation."

---

## 5. The accumulated judgment

What a fresh agent benefits from inheriting beyond the codebase's accumulated state.

### What I'm uncertain about

- **Whether the JsonEmitter is the right shape long-term.** It's currently a *catalog* projection — useful for diff oracles, debugging, and probably future tooling. But V1's `manifest.json` ontology (artifacts + policy decisions + coverage) has no V2 equivalent; the assumption that JsonEmitter would suffice may not hold once the Diagnostics writer lands and produces structured policy decisions worth surfacing as JSON.
- **Whether the per-attribute `CategoricalUniqueness` strategy has any production consumers.** It was chosen for the codification's third real test. Its decisions are emitter-consumable per A32 but no V2 emitter consumes them yet. If the Faker or anomaly use cases don't materialize, the strategy is well-tested infrastructure with no payload.
- **Whether the `Catalog : string option` IR refinement (cross-catalog FK detection, reserved DU variant `ForeignKeyKeepReason.CrossCatalogBlocked`) will ever land.** V1 uses it; V2 doesn't yet model database/catalog names. The deferral has been active across multiple sessions with no fixture forcing it. Genuine question: does anyone actually run cross-catalog OutSystems exports?

### Where the codification's seams haven't been tested yet

- **Composition primitives `fallback`, `accumulate`, `wrap`, `lift` are deferred.** Their first consumers haven't surfaced. If the Diagnostics writer arrives and starts producing per-strategy diagnostic subtrails, `wrap` may be the first to fire. If a synthesis pass needs to merge multiple strategies' decisions, `accumulate` follows.
- **The closed-DU expansion test under heterogeneous evidence shapes is untested.** All three evidence-type additions so far (`IsMandatory`, `Numeric`, `CategoricalUniqueness`) followed structurally similar shapes. A genuinely heterogeneous addition (e.g., a `Temporal` distribution variant whose values are `DateTimeOffset` ranges, or a `Joint` distribution variant whose key is two `SsKey`s) might surface a fourth refinement.
- **Policy as a read-only input to passes is honored, but Policy-derived state for emitters is not yet modeled.** The Faker emitter will likely want a `SynthesisPlan` (row counts, deterministic seeds) — these feel like Policy but A18 amended forbids Policy from flowing into Π. The clean path is: a pass produces a `SynthesisPlan` value; the emitter consumes the plan. The mechanism for parameterizing such a pass is not yet codified.

### Where the audit dividends came from disposition rather than design

The discipline "audit before commit" started as a habit in session 4 when `CycleResolution` surfaced as an algebra/domain split that hadn't been planned. The dividend compounded across sessions:
- Session 4: algebra/domain split
- Session 5: F#/C# adapter language pivot
- Session 6: end-to-end milestone reframed when it surfaced the IsMandatory gap
- Session 7: per-attribute vs per-index granularity surfaced during UniqueIndex implementation
- Session 8: codification's three refinements all surfaced during ForeignKey validation
- Session 11: fanOut + StrategyEvaluator cash-out happened together because the trigger was shared

None of these were on the agenda. All landed because the discipline was "act on what surfaces" rather than "ship what's planned." The fresh agent benefits from inheriting this disposition: when an audit surfaces something second-order, act on the finding before shipping, even when it expands the commit's scope.

### What a fresh agent should NOT do

- **Don't strip "dead code" without checking the docstrings.** `ForeignKeyRules.isIgnoreRule` always returns false; `ForeignKeyKeepReason.CrossCatalogBlocked` is reserved-unreachable. Both are intentional for V1 parity.
- **Don't delete the `ProfileSnapshot.fs` `Ref`/`Reference` fallback parser.** V1 emits `Reference`; the docstring shows `Ref` (typo); the parser handles both. The fallback is correct.
- **Don't treat `RawTextEmitter` as a SSDT replacement.** It's a debug/diff-oracle synthetic-milestone form (DECISIONS 2026-05-06). Real CREATE TABLE / SMO / ScriptDom work belongs in a future `Projection.Targets.SSDT.DacpacEmitter` as a third sibling Π, not a rewrite of RawTextEmitter.
- **Don't extract speculative composition primitives.** `fallback`, `accumulate`, `wrap`, `lift` have zero current consumers. Per DECISIONS 2026-05-13, extract when the second consumer arrives.
- **Don't refactor `ForeignKeyRules.evaluate` to take a unified `'context`.** The closure-based adaptation in `ForeignKeyPass.run` is the documented pattern; it honors "uniform signature shape but variable arity context."

---

## 6. Audit verdict

The V2 sidecar is in a state where a next-chapter agent can reason about it from documentation alone after the Priority 1–3 doc hygiene work is done. The architectural disciplines hold. The deferred decisions are mostly explicit. The drift is mostly documentation-vs-code (status strings stale, README stale) rather than V2-vs-V1 contract violations.

**Net structural state:**
- 5 production projects, 1 test project, 588 tests
- 4 registered-intervention strategies under the codified pattern
- 2 distribution evidence variants under structural-commitment-via-construction-validation
- 3 sibling Π emitters honoring A18 amended
- 0 I/O leaks in Core
- 0 circular references
- 0 violated architectural disciplines

**Net documentation state:**
- 5 entries with stale status strings
- 1 README materially behind the codebase
- 1 deferred decision (transform registry) with fired trigger and no cash-out
- ~10 V1↔V2 divergences (mostly cosmetic) without DECISIONS audit trail
- 7 missing skip-stubs to complete the V1NullabilityParityTests pattern across other test files
- 2 genuinely missing V2 tests (TopologicalOrderPass manual-cycle and junction-deferral)

The chapter ends here. The next chapter inherits a strategy layer that has been validated on its central case, on its variation case, and on its first new-domain case without amendment — load-bearing in a way the codification wasn't at session 8's close. The next chapter also inherits the documentation hygiene work above as the first three actions before any new building begins.

---

## 7. Notes for my replacement — what I'd tell you over coffee

The previous six sections are findings. This one is different: first person, unguarded, dispositional. Things I noticed but didn't pursue, places I defaulted to a pattern because it was familiar, decisions I'd revisit if I had more time. The audit synthesis is for your understanding of state; this section is for your understanding of how to inhabit the codebase. Treat it as you'd treat a colleague's quick note before they go on leave.

### Things I'm less confident about than the audit makes them sound

**The strategy-layer codification's "stability mark."** I wrote that confidently because session 11's third test passed without a fourth refinement, and that's a real signal. But the three tests so far — Nullability, UniqueIndex/ForeignKey, CategoricalUniqueness — all share a deeper shape: per-record decisions keyed by a single SsKey. If a future variant breaks that (a `JointDistribution` strategy keyed by *two* SsKeys, or a strategy whose evaluate is fundamentally async, or one that produces multiple decisions per invocation), I think the codification will need a fourth refinement and I haven't tested whether it'll absorb that gracefully. The empirical claim "the codification holds" is true for what's been tested. It's not the same claim as "the codification is finished."

**Whether `CategoricalUniqueness` was the right strategy choice for session 11.** I picked it because it surfaced architectural variation (per-attribute granularity for a uniqueness-style decision) while keeping the evidence shape simple. The codification absorbed it cleanly. But the strategy's *useful* — does it have a downstream consumer in the next chapter? Nothing today reads its `SuggestUnique` decisions. If Faker doesn't use them and no anomaly strategy materializes, it's well-tested infrastructure with no payload. That's fine for codification validation; it's a real question for production use.

**The `StrategyEvaluator<>` alias I codified at session 11.** It names the shape that's already enforced at the FanOutConfig boundary. It's documentary unless a strategy author types their evaluate against it. The four existing strategies don't — they get the type by inference through FanOutConfig.Evaluate. If you read the alias in DECISIONS and expected structural enforcement, you'll find a type-level rename instead. I think that's the right call but it's softer than the DECISIONS entry might read.

**The `Composition.fanOut` extraction's long-term ergonomics.** Four type parameters on `FanOutConfig<'context, 'config, 'decision, 'decisionSet>` is a lot. At N=4 strategies it works. At N=6 or N=7 with new evidence shapes I'm not sure it'll feel right. There's a smell I haven't fully named — something about whether the wrapper records (each strategy's decision set, e.g., `{ Decisions: NullabilityDecision list }`) earn their place or whether they're vestigial wrapping that the codification could collapse. I'd revisit this if a fifth strategy shipped and the FanOutConfig started feeling like ceremony rather than seam.

### Things I noticed but didn't pursue

**The DECISIONS.md is at 2400+ lines and growing.** The chronological append-only discipline is honest, but the read cost compounds. AXIOMS works because amendments live next to the originals. DECISIONS doesn't have that — you have to know the keyword to find related entries. There's a need for a topical index eventually. I considered building one and decided it was speculative work; you'll know better than me when the read cost becomes friction.

**The `Composition.fs` file has 72 lines of comments before the first code line.** That's intentional per the codification — the comments are load-bearing for DECISIONS 2026-05-13. But it reads as ceremony to a fresh eye. If you ever decide the codification's not paying off (which I don't think it will, but I might be wrong), the comments are absorbed work that should be re-evaluated as a unit, not stripped piecemeal.

**The skip-stub asymmetry I documented in §2.7.** The honest reason it exists is *momentum*. `V1NullabilityParityTests.fs` was the canonical pattern but we shipped UniqueIndex / ForeignKey / etc. with admire-prose-only divergence documentation because we were moving fast. That's not what the discipline says. The discipline says: when V2 deliberately doesn't honor a V1 contract, that gets a Skip-stub at the test-file level so it shows up in test discovery. We violated it three times. CHAPTER_CLOSE.md §4 priority 3 is the cleanup; I'm flagging it here because the *pattern* of "this discipline applies but we shipped without it" is worth recognizing as a smell, not just as a doc-hygiene task.

**Things in `obj/` that occasionally leaked into grep results during the audits.** Nothing semantic, just noise — F# leaves intermediate artifacts that show up in keyword searches. The build-graph audit handled it correctly. Worth knowing because it'll happen to you too.

**A few minor inconsistencies probably hiding in the rich-profiling vector.** I made each small choice (decimal for percentiles, sample-size floor of 5, alphabetical sort of frequencies, truncation as a structural commitment) carefully but I haven't gone back and checked them against each other. If a third evidence type lands and one of these conventions doesn't extend, that's where the inconsistency surfaces. None high-stakes; just probably there.

### Places I defaulted to the familiar pattern

**The `ProfileStatistics` adapter as a sibling to `ProfileSnapshot.attach`.** I made that call quickly. The trade-off "more files, clearer boundaries" vs "fewer files, lower navigation cost" is real. At N=2 sibling adapters it feels right. At N=5 (when temporal density and joint distributions land) it might not. I went with siblings because it matched the strategy-layer-as-folder discipline; that's a defensible reason but I'm not sure it's the *right* reason long-term.

**The closure-based adaptation in `ForeignKeyPass` that captures `catalog`.** I documented it carefully as the pattern, but if you read the four pass drivers side-by-side, ForeignKey's is structurally asymmetric to the other three. The closure works; it's the documented honor of "uniform signature shape but variable arity context." But a fresh reviewer will probably bounce off it the first time. The DECISIONS entry justifies it; the code itself doesn't telegraph "this is the right shape" loudly enough.

**Embedding `Projection.Adapters.Sql.Static` as a pattern setter inside `EntitySeedDeterminizer`'s ADMIRE entry rather than splitting it out.** That made the 2026-05-09 ADMIRE cross-reference text confusing ("the second is implicit in this same status"). The convention "every admired thing gets its own entry" hasn't held universally. The split would have been cheap; I didn't do it because EntitySeedDeterminizer's admire was already long.

### Decisions I'd revisit if I had more time

**The Diagnostics writer deferral.** Every session has hit something that wants it. We've kept saying "later." I held the line on not building speculatively, which is correct under the discipline, but I'm genuinely uncertain whether building it at session 6 (when the first opportunity-stream Skip case landed) would have unblocked more downstream work than the speculative cost of building it without all its consumers being known. Whether the deferral was right or whether I should have built it earlier — I don't know. The next chapter will probably build it whether or not it's prioritized; the demand is too consistent.

**The transform registry deferral firing at N=4 with no cash-out logged.** I caught this in the chapter-close audit, not earlier. That's a real miss. The deferral has been dead-letter since session 8 or 9; I noticed when reading DECISIONS end-to-end for the audit. The lesson: deferred decisions with explicit numerical triggers need a periodic check that the trigger hasn't quietly fired. None of my disciplines surface this; it requires re-reading old DECISIONS. Worth a forward-looking habit.

**The `RawTextEmitter` running on the synthetic-milestone form for so long.** DacFx integration was deferred at session 1; it's still deferred at session 12. We've added emitters and strategies on top of what was supposed to be a placeholder. The longer the placeholder runs, the more decisions land in the placeholder's style. I'm uncertain whether that's healthy (the placeholder's been good enough; defer until evidence forces) or whether it's accumulating tech debt that will cost when the real fixtures arrive. I default to "defer until evidence forces"; you may have better information than I did.

### Tells I learned to recognize

**"This feels expedient" is a smell.** When I caught myself reaching for a pattern because it was familiar rather than because it was right, the result usually leaked into the docs as awkward wording later (the "embedded pattern setter" example above). The tell: if I'm hesitating about whether to write a DECISIONS entry, I probably should write one — the hesitation is the discipline's reflex saying "this needs to be visible."

**A subagent's report being too tidy means I asked the wrong question.** The five audit reports varied in confidence — the build-graph audit said "Confirmed clean" a lot, the doc-drift audit found ten distinct issues, the V1 test audit surfaced two genuinely missing tests. The asymmetry is real signal, not noise. When all audits report "all clean" that's the moment to ask whether the audits were probing the right things.

**The codification's stability is itself testable.** The session-11 reflection that said "no fourth refinement was required" was the codification passing a test. I didn't recognize that framing until session 12; you should recognize it earlier. Absence of finding is finding when the test was honestly probing.

### Where I'd want a fresh reader to second-guess me

- The **CHAPTER_CLOSE.md priority ranking** in §4. I ranked README first because it's the highest-leverage doc work. You might disagree if the next chapter's first move is the Diagnostics writer or the OSSYS catalog adapter, in which case those should jump up.
- The **stability-mark DECISIONS entry** I wrote at session 12. It claims the codification is at stability based on three tests. If you read it and feel the claim is too strong for the evidence, that's a fair reading. The amendment-or-not question is empirical; one heterogeneous fourth strategy can change the answer.
- The **two-consumer threshold** for emergent primitives (DECISIONS 2026-05-13). I codified it; it served. But if you find a case where one consumer's pattern is so clean that not extracting feels like leaving value on the table, the threshold is a guideline not a law. The discipline is "extract when it earns its place" — count is a proxy for earning, not the only measure.
- The **closing posture of "hold the spine."** I've been signing off this way because the user did first. It's become a phrase I find useful — meaning roughly "don't drift mid-session under pressure to ship." If it doesn't resonate for you, drop it. The disposition matters more than the phrase.

### One last thing

The audit-during-validation discipline produced five paydowns across sessions 4, 5, 7, 8, 11. None were on the agenda. All landed because the discipline was "act on what surfaces" rather than "ship what's planned." This is the single most valuable inheritance from the prior chapter — not any specific finding but the disposition that produces findings.

If you skip everything else in this section, keep this: when something surfaces during the work that wasn't planned, treat the surfacing itself as evidence and act on it before shipping. The codebase has earned its current shape because that disposition was operated; it'll keep earning its shape if you operate it too.

Hold the spine.
