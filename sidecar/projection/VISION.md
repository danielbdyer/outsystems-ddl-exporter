# V2 — Vision

> **Documentation map.** This is the canonical vision document for V2 (the F# sidecar of the OutSystems DDL exporter). The current vision is the body of this file; the original revision-1 vision is preserved verbatim at the end as historical context. The four-subagent review that produced revision 2 — synthesis, reasoning resolutions R1–R8, and eight verbatim subagent reports (Appendices A–H) — lives in `VISION_REVIEW.md`. Tactical decisions live in `DECISIONS.md`; algebraic claims in `AXIOMS.md`; chapter-bridge context in `HANDOFF.md`; V1↔V2 placement in `ADMIRE.md`.
>
> **Companion strategic surfaces** (added 2026-05-08):
> - `V2_DRIVER.md` — destination KPI (V2-driver mode for the cutover) + operative backlog (chapter-by-chapter sequencing; ~375 items by chapter, status, disposition). **Codified 2026-05-10 chapter 3.7 sidebar; supersedes the prior `BACKLOG.md` which is now a forwarding pointer.** Read this before opening any chapter.
> - `PLAYBOOK.md` — technical guidance bridging vision to implementation; recurring patterns; decision trees; anti-patterns; per-chapter strategic notes.
> - `SPINE.md` — the deeper structural rendering: the system as a category; seven primitives; seven tessellating patterns; six structural inferences (sheaf, adjunction, Hom-set, quotient, continuation, tessellation instance); ten concrete leverage points.
> - `STAGING.md` — Stage 0 foundation phase: the work to ship *before* chapter 3.1 opens; twelve dependencies that compound across every subsequent chapter.

This document carries V2's strategic frame across context boundaries. Read it once at session-open. Re-read when sequencing decisions feel unmoored from the larger arc. Extend it when the work surfaces a strategic implication the current text doesn't yet carry; per the append-only discipline, prefer adding sections over rewriting.

## Contents

- [The forcing function](#the-forcing-function)
- [What V2 uniquely contributes](#what-v2-uniquely-contributes)
- [V1's empirical foundation](#v1s-empirical-foundation)
- [The algebraic core](#the-algebraic-core)
- [The verification loop](#the-verification-loop)
- [Acceptance criteria](#acceptance-criteria)
- [Cutover fallback ladder](#cutover-fallback-ladder)
- [The work-smarter strategy](#the-work-smarter-strategy)
- [Chapter 3 plan](#chapter-3-plan)
- [Chapter 4 plan — the production-deployment chorus](#chapter-4-plan--the-production-deployment-chorus)
- [What's deferred (with re-open triggers)](#whats-deferred-with-re-open-triggers)
- [What's cut](#whats-cut)
- [How to hold this vision](#how-to-hold-this-vision)
- [Closing](#closing)
- [Revision history](#revision-history)
- [Historical: revision 1 vision (preserved verbatim)](#historical-revision-1-vision-preserved-verbatim)

---

## The forcing function

A 300-table OutSystems 11 Reactive system on managed AWS, mid-development, facing an External Entities cutover. Every Entity and Static Entity will be swapped 1:1 — internal management ceded to OutSystems' platform replaced by external management against a team-managed on-prem SQL Server. Schema and data live outside OutSystems' database; Integration Studio declares the External Entities pointing back at the external schema. The platform consumes the swap as if nothing changed; underneath, the entire data plane has migrated.

Four environments — dev, qa, UAT, prod — each with on-prem SQL Server consumption via Azure DevOps PRs. CDC running in production with features depending on it; spurious change records would disrupt those features. User FKs (CreatedBy, UpdatedBy) wired through every entity, requiring environment-specific remapping when data is reflowed. Migration-team workflow that publishes legacy domain data into the on-prem database. RefactorLog records that need to survive across schema versions. Repeatable cadence — schema and data evolve continuously; the extraction gets re-run regularly.

If V2's emission is wrong: production data integrity corrupted; CDC-dependent features broken silently; rollback prohibitively expensive; partial cutover leaves hybrid state structurally hard to recover from.

V1 ships the cutover. V2 makes it verifiable, reversible, and repeatable past the cutover.

## What V2 uniquely contributes

V1 ships the cutover-relevant capabilities (extraction, two-phase inserts, FK reflow, multi-environment promotion). V2's load-bearing differentiator is **the sibling chorus + verification**, not displacement of any single V1 surface.

**Target artifacts (all sibling Π's of the same Catalog).** V2 emits multiple synchronized projections, each shaped for its consumer:

- **SSDT DDL** (per-table `.sql` shape, V1's existing format) — the **production deployment** surface. Promoted into the Azure DevOps integration-test lane and verified by the canary read-back before merge. V2's emitter is opinionated, deterministic, and refactor-log-aware.
- **CDC-aware data inserts** — `StaticSeedsEmitter` / `MigrationDependenciesEmitter` / `BootstrapEmitter` (chapter 4.1) produce data emissions that respect CDC-tracked tables: topologically-sorted two-phase insertion, redeploy-idempotent, and tagged so insert plans avoid spurious change records on tables where CDC is enabled. This is one of the load-bearing cutover demands; V2 makes it algebraic where V1 does it by hand.
- **DACPAC** — the **fast-iteration and baselining** surface. Declarative artifact for ephemeral-DB deploy via DacFx, used in canary tier-1/tier-2 property tests for sub-second-to-150ms feedback cycles. Refactor.log records ride alongside, communicating renames so DacFx incremental deploys ALTER rather than DROP+CREATE. The DACPAC is *not the only* deployment artifact — it is the artifact whose iteration cost is small enough to drive the canary's property-test surface.
- **Refactor log** (`RefactorLogEmitter`) — sibling Π over `CatalogDiff`. SSDT-native format consumed by both the SSDT and DACPAC paths.
- **Distributions** (`DistributionsEmitter`, shipped) — evidence projection consumed by tightening passes and operator diagnostics.

V1 emits one artifact class (SSDT scripts) hand-curated per environment; V2 emits the chorus from a single Catalog × Policy × Profile triple. T11 (sibling-Π commutativity) is the cross-validation surface: if SSDT and DACPAC disagree on a kind's shape, one of them has a bug. V2 also adds **verifiable correctness** (canary loop on whichever surface is deployed) and **partial-state remediation** (`RemediationEmitter` as a thin composition over `CatalogDiff`), neither of which V1 has and neither requires V1 changes.

**Verification posture, two lanes:**

- **Fast lane (DACPAC).** Tier-1 property tests run pure (no Docker) on the F# emitter's `TSqlModel` round-trip. Tier-2 property tests run against testcontainers ephemeral SQL Server. Sub-second to ~150ms per case. Drives most of the canary's coverage.
- **Promoted lane (SSDT + CDC-aware data inserts).** Once a slice is fast-lane-green, V2 emits the SSDT shape + CDC-aware data inserts and promotes the artifact into Azure DevOps's integration-test environment. The canary reads the deployed schema via the read-side adapter (chapter 3.1) and asserts equality-by-SsKey against the V2-expected Catalog. The redeploy-zero-ALTER-and-zero-CDC-record assertion runs on the live integration target — this is where CDC-safety is actually verified.

Both lanes use the same `Catalog × Policy × Profile` algebra and the same `Emitter<'element>` shape. The DACPAC is fast; the SSDT + data lane is real. The canary verifies both.

## V1's empirical foundation

V1 has been doing this work — extraction; specializations; opinionated formatting; topologically-sorted two-phase inserts; user FK reflow between environments; profile interventions on the data; standalone domain record injection for legacy migration teams; environment promotion via Azure DevOps PRs. V1 is not a failed predecessor; V1 is the empirical foundation V2 inherits. Every specialization V2 must support exists in V1 because V1 discovered it through the lived work of building it.

V1's correctness is implicit — checked by hand, verified by experience, trusted because nothing has badly failed yet. The cutover scales the stakes past what implicit correctness can carry. V2 makes the correctness explicit, verifiable, and indefinitely repeatable.

The relationship: V2 inherits V1's empirical foundation and codifies what V1 discovered into algebra. The boundary between V1 and V2 is data, not typed cross-references. ADMIRE.md tracks the extraction; entries transition through admiring → extracting → extracted as canary evidence accumulates (not differential tests alone).

## The algebraic core

V2 is a metadata projection compiler:

```
Project = Π ∘ E
```

Four inputs:
- **Catalog** — environment-invariant evidence (the schema; structural truth).
- **Policy** — environment-specific intent (FK reflow strategy; user-matching strategy; migration data overrides). The Policy DU gains a fifth axis in chapter 4: `UserMatching of UserMatchingStrategy`.
- **Profile** — environment-specific evidence (data shape; user populations; value distributions).
- **Lifecycle** — temporal evidence (rename history; version threading).

The structural commitments are **type theorems, not disciplines** (this is the revision-2 sharpening — see `VISION_REVIEW.md` Appendix H):

- **T1 (determinism).** `Project` is a pure function. Same triple → byte-identical text/JSON; model-equivalent (DacFx round-trip equality) DACPAC. The CDC-safety property is *T1 × DacFx idempotent-redeploy*, not T1 alone (R2 in `VISION_REVIEW.md`); the canary's redeploy-zero-ALTER assertion verifies the composition.
- **T11 (sibling-Π commutativity).** Encoded as `Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>, EmitError>` where `ArtifactByKind` is a private DU whose smart constructor enforces "every Catalog kind is in the keyset." T11 becomes a type-level obligation, not an `Assert.Contains` discipline.
- **A1 (identity-survives-rename).** Encoded as `SsKey = OssysOriginal of Guid | Synthesized of source*basis | DerivedFrom of parent*reason | V1Mapped of v1Guid*ns`. The current bound (JSON-path strips SSKey, V2 synthesizes from name fields) is type-visible: property tests that claim A1 accept only `OssysOriginal` (and roots of `DerivedFrom`); on `Synthesized`, the same property documents the bound.

These three encodings turn what revision 1 framed as algebra-as-discipline into algebra-as-type-system. The cost is one chapter of refactor; the payoff is structural — wide classes of bugs become impossible to write.

Lineage is carried via the writer monad: passes return `Lineage<'output>` for decisions only, `Lineage<Diagnostics<'output>>` for decisions plus observer-relevant findings.

## The verification loop

The canary loop is the load-bearing core:

```
emit catalog
  |> deploy ephemeralSqlServer
  |> readSide.toCatalog
  |> Catalog.equalBySsKey catalog
```

Architectural commitment: build it as a **property-test surface**, not as a handful of hand-written integration tests. FsCheck generates Catalogs (single-table, multi-table-no-FK, FK with cycles, indexes, composite PKs, cross-module refs, renames). Three test tiers:

- **Tier 1 — pure properties (no container).** T1 byte/model equality, T11 sibling-chorus agreement, T2 coproduct preservation, A18 policy-orthogonality. ~80% of axiomatic coverage. Sub-second per property. Runs in pre-commit.
- **Tier 2 — container-pooled deploy.** One `IClassFixture<SqlServerFixture>` per test class; fresh `dbName` per case (~150ms vs ~5s container start). Round-trip equality, idempotent-redeploy (CDC safety), rename-survives (A1), policy-orthogonal-on-deploy. ~30 cases per property. Runs in CI.
- **Tier 3 — full integration sample.** Hand-curated `[<Theory>]` capturing every shrunk failure as a permanent regression test. Runs nightly.

Predicate library (12 predicates listed in `VISION_REVIEW.md` Appendix E §3). Notable: **`idempotentRedeploy`** explicitly verifies the CDC-safety claim that revision 1 left implicit — deploy → redeploy same DACPAC → assert second deploy issued zero ALTERs.

## Acceptance criteria

V2 has earned its existence when, by the cutover-quarter close:

1. **Verifiable correctness.** The canary catches at least one real V1 (or V2) emitter bug before publication, with zero false negatives over the quarter. Tracked as a cumulative count.
2. **CDC safety.** The `idempotentRedeploy` property holds across all generated catalogs at tier-2 (≥30 cases × all generated shapes) and across the four real production schemas at tier-3 (one full redeploy per environment per cutover-week).
3. **Identity.** The `renameSurvives` property holds for every `OssysOriginal` SsKey across the four-environment four-rename test plan. Bound is documented for `Synthesized` SsKeys and tracked for closure when `SnapshotRowsets` lands.
4. **T11 structural.** Every emitter signature is `Emitter<'element>`. The substring `Assert.Contains` tests in `JsonEmitterTests.fs:96-105` and `RichProfilingEndToEndTests.fs:280` retire (the type proves what they assert).
5. **V1 sunset gate.** ADMIRE.md `extracted` status requires a green canary on the relevant component, not just a passing differential test. V1 sunset is deferred until all four environments have run on V2 emissions for one full schema-evolution cycle.

These replace the unfalsifiable claims revision 1 closed on ("V2 is the team's sovereignty," "constitutive," "auditability is type-system-encoded"). Each has a counterexample condition; each is tracked.

## Cutover fallback ladder

Three tiers, with a T-30-day decision criterion:

**V1-only path.** V1's existing pipeline runs the cutover. Trust mechanism is empirical correctness verified by experience. V2 ships post-cutover.

**V2-augmented path.** V1 drives the cutover; V2 runs the canary as a verification-only sidecar (V2 emits, deploys to ephemeral, reads back, compares to V1's emission and to V2's expected Catalog). Disagreement blocks the PR. **V2 owns no production write path.** This eliminates split-brain by construction (R6 in `VISION_REVIEW.md`).

**V2-driver path.** V2 emits SSDT DDL + CDC-aware data inserts (and DACPAC for fast iteration); V1 retired from cutover. Highest payoff; highest risk.

**Decision criterion at T-30 days.** V2-driver requires (a) chapter 3 closed with green canary on full 300-table Catalog; (b) chapter 4.1 (SSDT emitter + data triumvirate) shipping; (c) chapter 4.2 (user FK reflow) shipping; (d) ≥1 full UAT dry-run with cross-environment Profile/Policy pairs producing structurally-consistent artifacts. If any of (a)–(d) is yellow at T-30, drop to V2-augmented. If V2-augmented is unstable at T-15, drop to V1-only and ship V2 as post-cutover substrate. **Hard rule: V1 stays warm even on the V2-driver path until cutover+30 days.**

## The work-smarter strategy

Five compounding wins make full realization plausible in the cutover quarter rather than across multiple quarters:

1. **V2 verifies V1 starting today** (Appendix F). V2's `JsonEmitter` plus the existing `CatalogReader` already enable an `osm_model.json` round-trip diff against V1's persisted snapshot. No new code; ships this week. Step 2 (read-side adapter) gives full V2-augmented mode in chapter 3.1.
2. **Read-side adapter via `INFORMATION_SCHEMA` / `sys.*`, not DacFx Extract** (Appendix F §2). ~300–500 lines of F# replaces a chapter of DacFx integration. Two consumers from day one (canary + drift detection). DacpacEmitter is gated on this, not the inverse.
3. **Canary as property-test surface, not hand-curated integration** (Appendix E). ~600 lines of test code replaces ~2000+ lines of curated fixtures. Tier-1 pure properties cover most axioms with no Docker. Generators are bottom-up with structural well-formedness baked in (FK targets exist by construction; no filtering); shrinking is outermost-first (drop module → drop kind → drop reference → drop index → drop attribute).
4. **`ArtifactByKind<_>` type theorem** (Appendix H §4). T11 stops being substring-search discipline. Drift detection becomes pointwise per-SsKey diff (free given the read-side adapter). Partial-state remediation falls out as `dacpacEmitter (CatalogDiff.between deployed target)` — exactly the per-SsKey shape (R5 in `VISION_REVIEW.md`).
5. **Triangulation comparator: three Catalogs, two diffs** (Appendix F §6). `C_ossys` (V2's expected from OSSYS), `C_v1` (read-side from V1's deploy), `C_round` (V2 passes applied to `C_v1`). Pairwise diffs attribute every divergence to V1 / V2 / comparator. Solves the "V2 inherits V1 bugs" critique structurally.

Each of these is independently load-bearing and independently shippable.

## The deeper structure

`SPINE.md` (added 2026-05-08) renders the V2 system at deeper levels: the system *is* a category in the technical sense; the chapter pre-scopes are concrete morphism constructions. The structure tessellates across **seven patterns** (Π Emitter / Adapter / Pass / Render / Compare / Property / Diff) over **seven primitives** (SsKey-keyed Map / Writer-monad accumulation / Ordered linearization / Smart-constructor invariants / Origin tagging / Erasure declaration / Closed DUs with structured rationale).

Six structural inferences fall out:
- **The four-input projection is a sheaf over (time × environment).** R4 (multi-environment property) is literally the sheaf gluing condition. Four-environment cutover is one algebra applied to four `(Policy, Profile)` pairs — no separate code paths.
- **`emit` and `read-side` form an adjunction** modulo Π-erasure. The canary's fixpoint claim tests the adjunction's unit; drift detection is the unit's failure surfaced as a diff.
- **`CatalogDiff` is the morphism set** in catalog-evolution category. `RefactorLogEmitter` isn't special — it's just another Π whose evidence happens to be a morphism. The asymmetry was an illusion of names.
- **The fallback ladder is a quotient** on the projection's range. Three tiers = three CI configurations selecting which projection is authoritative — *not* three implementations. T-30 decision is a YAML edit.
- **Property tests are continuations.** The shrinker walks the morphism chain in reverse to the smallest counterexample.
- **Each chapter is one tessellation instance** of one pattern with one type variable. The slice list is the *implementation*; the pattern is the *contract*.

## Foundation phase (Stage 0)

Per `STAGING.md`, a foundation phase ships **before chapter 3.1 opens**. Stage 0 codifies the categorical structure as F# types so every subsequent chapter is a body implementation, not a co-derivation of the type signatures.

Twelve items across four tiers (~3,000 LOC):

- **Tier 1 (documentation):** AXIOMS amendment scaffolding; DECISIONS pre-chapter-3 governance burst (R6 split-brain rule, chapter sequencing, CLAUDE reading-order, T-30/T-15 fallback gates, Stage 0 commitment); ADMIRE/AXIOMS/CLAUDE currency checks; cross-references to SPINE/PLAYBOOK/STAGING.
- **Tier 2 (type keystone):** `Emitter<'element>`, `Adapter<'source, 'internal, 'error>`, `Pass<'output>`, `Render<'element, 'output>`, `Compare<'tolerance>`, `Property`, `Diff` as F# type aliases in `Projection.Core/Types.fs`.
- **Tier 3 (structural commitment):** `ArtifactByKind` + `SsKey` four-variant + `CatalogDiff` per `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md`.
- **Tier 4 (primitive support, parallel):** `Render` module skeletons; property combinator library; Tolerance taxonomy with named flag list; `config/default-tightening.json` port (BACKLOG CRITICAL gap); test support consolidation lifting V1's `tests/Osm.TestSupport/` patterns into F#; multi-environment generator skeleton.

Stage 0 pays back at chapter 3.3: every chapter beyond that is pure compounding. Drift detection ships as a CI cron job (no chapter); RemediationEmitter ships as a one-line composition; future emitters cost ~100 LOC each.

## Chapter 3 plan

**3.1 Read-side adapter + comparator + minimal `Projection.Pipeline`** (was 3.2). Adapter under `Projection.Adapters.Sql.ReadSide/`; comparator under `Projection.Core/Verification/CatalogEquivalence.fs`; pipeline shell under `Projection.Pipeline/` (C#) reusing `tests/Osm.TestSupport/SqlServerFixture.cs`'s testcontainers wiring. Ships V2-augmented mode against V1 (Appendix F §1).

**3.2 SnapshotRowsets adapter variant.** Resolves JSON-projection lossiness (`EspaceKind`, `IsSystemEntity`, `EntitySsKey`, etc.). Unblocks A1 for renames (`OssysOriginal` SsKeys become available). Cross-module FK rule (rule 16) tactical-completeness step lands inside slice 2 or 3.

**3.3 DacpacEmitter (fast-iteration surface)** (F# emits T-SQL strings; C# DacFx wrapper in `Projection.Targets.SSDT.Dacpac` C# project). T1 amends to "byte-determinism for text/JSON; content-determinism via DacFx model-API equality for binary." Byte-canonicalization is a post-pass in the C# wrapper; canary's compare layer goes through model API. This is the iteration target for the property-test surface — its 150ms-per-case feedback loop is what makes tier-2 properties cheap.

**3.4 Canary closure with property-test predicate library.** Tier-1 pure properties for T1/T11/T2/A18; tier-2 container-pooled deploy properties for round-trip + idempotent-redeploy + renameSurvives + policyOrthogonal against the DACPAC surface. The redeploy-zero-ALTER assertion lands here for the DACPAC fast lane; the SSDT/data-insert lane gets the same assertion in chapter 4.1's promoted-lane integration test.

**3.5 RefactorLogEmitter as Π over `CatalogDiff`** (Appendix H §6). UUIDv5 maps V1 SSKey Guids to V2 `V1Mapped` SsKeys. Refactor.log XML is SSDT-native format consumed by both the DACPAC and SSDT deploy paths.

**Drift-detection job** (promoted from post-cutover): read-side adapter pointed at four real DBs on a schedule, surfacing deltas as `Diagnostics` findings. Free given 3.1 lands.

## Chapter 4 plan — the production-deployment chorus

Chapter 3 closes with the DACPAC fast lane and the canary's property-test surface. Chapter 4 adds the **promoted lane**: the SSDT DDL emitter + CDC-aware data inserts that go through Azure DevOps integration tests for production deployment.

**4.1 SSDT DDL emitter (sibling Π) + CDC-aware data triumvirate.**
- `Projection.Targets.SSDT.DdlEmitter` (F# `Emitter<SsdtFile>` returning per-table content keyed by SsKey). Composes via `Render.toSsdtDirectory`; output shape matches V1's `<outDir>/Modules/<Module>/<Schema>.<Tbl>.sql` convention (Appendix F §1 names the seam). DACPAC and SSDT DDL are sibling Π's of the same Catalog — T11 cross-validates them.
- `StaticSeedsEmitter` → `MigrationDependenciesEmitter` → `BootstrapEmitter` with `EmissionPolicy` DU (`AllRemaining` default / `AllExceptStatic` / `AllData`). All three are CDC-aware: each consumes a `CdcAwareness` configuration (per-table CDC-enabled flag from the deployed schema's `sys.tables` / `cdc.change_tables`) and plans inserts to be redeploy-idempotent. The redeploy-zero-CDC-record assertion is the integration-test verification on the promoted lane.
- Promoted-lane integration test: emit SSDT + data inserts → deploy to Azure DevOps integration environment → canary read-back compares to V2-expected Catalog → redeploy → assert zero ALTERs and zero CDC records on tracked tables.

**4.2 User FK reflow as a real Policy axis + pass + sibling Π.** `UserMatchingStrategy = ByEmail | BySsKey | ManualOverride | FallbackToSystemUser`. `UserRemapContext = Map<SsKey, Map<SourceUserId, TargetUserId>>` produced by discovery pass, consumed by the data triumvirate at emission time.

**4.3 Operational diagnostics V2** (decision-log/opportunities/validations equivalents) consuming the existing `Diagnostics<'a>` writer. The three-channel split (operator/auditor/developer) deferred from chapter 2 fires here if the consumers diverge.

**4.4 `RemediationEmitter`** (composes read-side + DacpacEmitter or DdlEmitter over `CatalogDiff`). Partial-state recovery primitive for the failure mode revision 1 named but did not close (R5 in `VISION_REVIEW.md`).

## What's deferred (with re-open triggers)

Each item below is a named active deferral. Triggers belong as DECISIONS.md entries when they fire.

- **GraphQL schema emitter** — defer; trivially `Emitter<GraphqlTypeDef>` once a real consumer asks. Resolvers stay cut.
- **FakerEmitter (single dimension only)** — defer; gated on third evidence type, OR a real consumer demand for synthetic data.
- **Per-developer Docker SQL Server with Profile-shaped synthetic data** — defer; transitively gated on Faker.
- **Longitudinal Profile diffing** — defer; first real operator question requiring two persisted Profiles compared (e.g., "did P95 of X drift between QA and prod?") and DistributionsEmitter+grep can't answer it.
- **PR-canary CI hook** — defer; first post-cutover schema-evolution PR where operator wants pre-merge canary signal.
- **Per-module packaging** — defer; the maintainer's "flow metrics from code review app" use case becomes a real ask, not a hypothetical.
- **`SnapshotRowsets` operator-decided activation** — already trigger-fired (A1 bound for renames; chapter 3.2 sequence).

## What's cut

These leave the document. If they re-emerge, they re-enter via DECISIONS.md with explicit license/governance/scope commitments — not via aspiration.

- **"V2 outlives OutSystems" / Catalog as platform-survival.** Catalog source-agnosticism is already structural in the V1↔V2 vocabulary mapping (`README.md`); re-asserting it adds rhetoric, not obligation. A new platform adapter is the same shape as `Projection.Adapters.Osm`; nothing else V2 doesn't already plan to ship.
- **Six-dimension synthetic-data quality scoring.** Six adjectives ("relational, commutative, descriptive, heuristic, correlative, entropic"), zero metric definitions, zero consumer.
- **AI-agent substrate as a section.** The fact that `Json` and `RawText` emitters happen to be agent-legible is a corollary recorded in `DECISIONS.md`, not a vision pillar.
- **Open-source / community contribution.** Optional-with-the-option-emerges hedging concedes the item doesn't belong.
- **Recipes-as-Terraform beyond the canary's testcontainers compose file.** The compose file is a documented byproduct.
- **Playwright-plan refresh under CI/CD.** No forcing function; depends on the AI-agent substrate that's also cut.

## How to hold this vision

This document is strategic substrate. Tactical decisions live in `DECISIONS.md`; chapter-bridge context lives in `HANDOFF.md` and `CHAPTER_N_CLOSE.md`; algebraic claims live in `AXIOMS.md`; V1↔V2 placement lives in `ADMIRE.md`. This document carries the *why* behind all of those.

Consult it when:
- A sequencing decision feels unmoored from the larger arc.
- A new sibling Π is being scoped and the chorus needs reorienting.
- The work surfaces an implication that fits in this document's frame but isn't yet recorded.
- An audit asks "what is V2 for, ultimately?" and the answer needs to be load-bearing rather than aesthetic.

Extend it when:
- A new dimension of the vision earns its place through empirical demonstration. Append, don't rewrite. Append-only documentation discipline applies here as it does to the HANDOFF and CHAPTER_N_CLOSE letters.
- A new corollary falls out of the work that was not previously visible. Surface it briefly; cross-reference to the DECISIONS entry that resolved the underlying question.

Do not consult it when:
- The work is tactical (HANDOFF, DECISIONS, ADMIRE are the right surfaces).
- The decision is local to a slice (the slice's chapter-open document is the right surface).

The vision is the load-bearing structure that lets the chapters ahead support more weight than the ones behind. Hold it lightly when the work is tactical; hold it firmly when the work asks "is this still V2."

## Closing

V1 ships the cutover. V2 makes it verifiable, reversible, and repeatable, across two verification lanes — the DACPAC fast lane that drives the property-test surface, and the SSDT-DDL-plus-CDC-aware-data-inserts promoted lane that goes through Azure DevOps integration tests for production deployment. Three structural type theorems (T1 / T11 / A1 encoded as types, not disciplines), one property-test surface (FsCheck + tiered predicates), one read-side adapter (delivering value before any new emitter ships), one triangulation comparator, and a sibling chorus that cross-validates DACPAC against SSDT against the deployed schema are the load-bearing structure. Everything else is parking lot until a forcing function fires.

The five acceptance criteria above are testable. The fallback ladder is named. The drift-detection job covers continuing operations past the cutover. The chapter 3 sequence is concrete and starts delivering value in week one; the chapter 4 sequence promotes V2 from "verifies V1" to "emits production artifacts V1 cannot reach."

Hold the spine. Cut the rest.

— Recorded for the receiving agent.

---

## Revision history

### Revision 2 — 2026-05-08

Following the four-subagent review (`VISION_REVIEW.md`) and a follow-on work-smarter pass (Appendices E–H), revision 2:

- Cut the §"informational widening" and §"post-cutover trajectory" sections — platform-survival rhetoric, AI-agent substrate as a section, six-dimension Faker quality scoring, open-source contribution, recipes-as-Terraform. None had a forcing function or a cutover dividend.
- Promoted drift detection from post-cutover trajectory to cutover-critical (read-side adapter byproduct).
- Sharpened V2's unique contribution: V2 emits a sibling chorus (SSDT DDL, CDC-aware data inserts, DACPAC, refactor log, distributions) from a single Catalog × Policy × Profile triple. T11 cross-validates. (Initial draft framed V2 as DACPAC-only displacing V1's SQL scripts; corrected to two-lane verification posture per maintainer clarification.)
- Added acceptance criteria replacing unfalsifiable rhetoric ("sovereignty," "constitutive," "auditability is type-system-encoded").
- Added cutover fallback ladder (V1-only / V2-augmented / V2-driver) with T-30-day decision criterion.
- Added the dogfood frame: V2 verifies V1 starting now via the existing `JsonEmitter` round-trip — before any new V2 emitter ships.
- Re-sequenced chapter 3: read-side adapter promoted to 3.1 (V2-augmented mode shipping immediately); SnapshotRowsets 3.2; DacpacEmitter 3.3; canary closure 3.4; RefactorLogEmitter 3.5.
- Named the type-system refactor (`ArtifactByKind<'element>` + `SsKey` four-variant DU + `CatalogDiff`) that turns T11 and A1 into type theorems, not disciplines.
- Named the canary-as-property-test architecture (FsCheck + tiered predicates) that replaces ~2000 lines of curated tests with ~600.

### Revision 1 — 2026-05-08

Initial vision document. Preserved verbatim below at "Historical: revision 1 vision (preserved verbatim)."

---

## Historical: revision 1 vision (preserved verbatim)

> *The text below is the original VISION.md as committed at `2fb51ef`. It is preserved per the append-only discipline. The body of this document above is the canonical current vision; revision 1 is retained for historical context and to document the strategic frame that the four-subagent review pressure-tested.*

### The forcing function

A 300-table OutSystems 11 Reactive system on managed AWS, mid-development, facing an External Entities cutover. Every Entity and every Static Entity will be swapped 1:1 — internal management ceded to OutSystems' platform replaced by external management against a team-managed on-prem SQL Server. Schema and data live outside OutSystems' database. Integration Studio declares the External Entities pointing back at the external schema. The platform consumes the swap as if nothing changed; underneath, the entire data plane has migrated.

Four environments — dev, qa, UAT, prod — each with on-prem SQL Server consumption via Azure DevOps PRs. CDC running in production with features depending on it; spurious change records would disrupt those features. User FKs (CreatedBy, UpdatedBy) wired through every entity, requiring environment-specific remapping when data is reflowed (Dev → UAT with user-matching strategy). Migration-team workflow that publishes legacy domain data into the on-prem database. RefactorLog records that need to survive across schema versions. Repeatable cadence — schema and data evolve continuously; the extraction gets re-run regularly as the source of truth evolves.

If V2's emission is wrong: production data integrity corrupted; CDC-dependent features broken, possibly silently; rollback prohibitively expensive across environments and versions; operator trust gone. Worst case: cutover fails after partial completion, leaving the system in a hybrid state that's structurally hard to recover from.

This is what V2 must survive. The algebra is not aesthetic; it is the structural condition for the cutover being trustworthy.

### What V1 already does

V1 (the parent outsystems-ddl-exporter project) has been doing this work — extraction; specializations; opinionated formatting; topologically-sorted two-phase inserts; user FK reflow between environments; profile interventions on the data; standalone domain record injection for legacy migration teams; environment promotion via Azure DevOps PRs. V1 is not a failed predecessor. V1 is the empirical foundation V2 inherits. Every specialization V2 must support exists in V1 because V1 discovered it through the lived work of building it.

V1's correctness is implicit — checked by hand, verified by experience, trusted because nothing has badly failed yet. The cutover scales the stakes past what implicit correctness can carry. V2 makes the correctness explicit, verifiable, and indefinitely repeatable.

The relationship: V2 admires V1; V2 extracts from V1 under empirical pressure; V2 codifies what V1 discovered into algebra. The boundary between V1 and V2 is data, not typed cross-references. ADMIRE.md tracks the extraction; entries transition through admiring (researched) → extracting (in flight) → extracted (differential confirmed) as evidence accumulates.

### The algebraic core (revision 1)

V2 is a metadata projection compiler. The algebra:

```
Project = Π ∘ E
```

E is policy-driven enrichment; Π is structural projection. The composition is the projection.

The four inputs:
- **Catalog** — environment-invariant evidence (the schema; the structural truth)
- **Policy** — environment-specific intent (FK reflow strategy; user-matching strategy; migration data overrides)
- **Profile** — environment-specific evidence (what data lives there; what users exist; what value distributions appear)
- **Lifecycle** — temporal evidence (before, now, after; rename history; version threading)

Π is total: every Catalog kind produces a corresponding artifact element by SsKey root.
Π is pure: no I/O, no Policy reach, no temporal coupling (A18 amended).

The algebra is enforced by F#'s closed-DU + total-pattern-match disciplines plus the empirical-test discipline (adding a DU variant should produce exhaustiveness errors only at match sites; no caller reshaping outside the variant's module). This is not bureaucratic ceremony. It is the structural condition for the algebra holding under modification.

The canary loop closes the gap between artifact and reality. V2 emits artifacts; V2 deploys them to ephemeral docker SQL Server (testcontainers, version pinned to production); V2's read-side adapter reads the deployed schema back as a Catalog; V2 compares the round-tripped Catalog to the source by SsKey root. If the comparison fails, the artifact never publishes. T11's sibling-Π commutativity is the verification surface: every projection must mention every Catalog kind by SsKey root, and the read-side reconstruction must agree with all sibling emissions structurally.

Lineage is constitutive, not decorative. Every decision the system makes — which strategy fired, what evidence informed it, what the rationale was — is carried in the writer monad. Passes return `Lineage<'output>` for decisions only; `Lineage<Diagnostics<'output>>` for decisions plus observer-relevant findings. Auditability is type-system-encoded.

### The five demands and the algebraic moves that meet them (revision 1)

**1. Verifiable correctness.** Every projection must be a tested claim, not an asserted output. The algebraic move: the canary loop. emit → deploy → read-back → compare-by-SsKey. The pipeline becomes self-validating; the team has algebraic proof rather than empirical hope.

**2. Multi-environment consistency.** Four environments, same algebra, environment-specific Profile and Policy. The algebraic move: Catalog × Profile × Policy separation (A18 amended). Catalog is environment-invariant; Profile and Policy carry the environment-specific shaping. The same algebra runs against four different (Profile, Policy) pairs; four different artifacts emerge, all proven consistent structurally.

**3. CDC-safe idempotency.** The emission must not generate spurious change records. The algebraic move: T1 projection-language-normal-form. Bytes for text/JSON; loaded TSqlModel structure for binary/DACPAC; content-equality for data emissions. `decimal` as default for continuous statistical evidence (T1 byte-determinism requires it). Topologically-sorted two-phase insertion (V1 implements; V2 inherits and makes algebraic).

**4. Identity preservation.** RefactorLog records carry rename history across schema versions. The algebraic move: A1 (identity-survives-rename) plus UUIDv5 plus RefactorLogEmitter. SsKey identity is stable under attribute/entity/module rename; UUIDv5 deterministically maps V1's persistent identity space (entity SSKey Guids) to V2's. The forthcoming RefactorLogEmitter (sibling Π) generates rename records consumable by SQL Server's refactor log, by Integration Studio's external entity declarations, by GraphQL schema versioning, by anything else needing to track "this identity, formerly X, is now Y."

**5. Provenance and observability.** Every decision traceable. The algebraic move: writer-monad lineage carriage plus structured rationale DUs plus the canonical surfaces (AXIOMS.md, DECISIONS.md, ADMIRE.md, CLAUDE.md). The system carries its own audit trail as a first-class artifact.

### The sibling chorus (revision 1)

T11 sibling-Π commutativity is the chorus's structural backbone: every Π's output mentions every Catalog kind by SsKey root, in the projection language's normal form.

Currently shipped:
- RawTextEmitter (debug oracle; legible diffs; no DacFx dependency)
- JsonEmitter (structural snapshot; deterministic UTF-8 via Utf8JsonWriter)
- DistributionsEmitter (Profile-shaped statistics)

Forthcoming (chapter 3 onward):
- DacpacEmitter (deployment artifact; T1 amended for binary normal form)
- RefactorLogEmitter (rename history; identity propagation)
- StaticSeedsEmitter / MigrationDependenciesEmitter / BootstrapEmitter (data-emission triumvirate per session-17 strategic frame; composition policy via EmissionPolicy: AllRemaining default / AllExceptStatic / AllData)
- FakerEmitter (Profile-shaped synthesis; quality scoreable against Profile across the six metric dimensions: relational, commutative, descriptive, heuristic, correlative, entropic)
- GraphQL schema and resolver emitters (the isomorphism observation: same algebra projects to GraphQL targets)
- Post-Integration-Studio external entity declaration emitter (the cutover's downstream consumption surface)

The chorus is operational, not academic. Same Catalog, many forms, each form shaped by what its consumer needs. Cross-validation is implicit: if RawTextEmitter and DacpacEmitter disagree on attribute type rendering, one of them has a bug. The sibling structure is itself a verification surface.

### The temporal axis

A1 (identity-survives-rename) is the temporal axiom. SsKey identity is stable across schema evolution. The current bound is named in A1's session-23 forwarding pointer; SnapshotRowsets resolves the bound when its forcing function fires (real refactor.log consumer; real cross-version identity demand; EspaceKind activation; isSystemEntity activation).

When SnapshotRowsets lands and RefactorLog is implemented, V2 becomes a history-aware system. UUIDv5 deterministically maps V1 SSKeys to V2 identities; renames are first-class; cross-version comparison is possible. V2 is not a snapshot; V2 is a thread of snapshots that knows its own continuity.

The Lifecycle dimension reserved space for this from chapter 1. Catalog is "what." Policy is "intent." Profile is "shape." Lifecycle is "when." V2 holds all four.

### The informational widening (revision 1 — substantially cut in revision 2)

V2 is not only a build tool. V2 is the team's information layer over OutSystems.

Engineering teams running OutSystems typically do not have direct sovereignty over the structured information about their own application. The platform has more accurate, more queryable, more current metadata about the team's app than the team has direct access to — or rather, the team has to query the platform through limited interfaces to know what its own system contains. V2 inverts this. The Catalog is canonical, queryable, exportable, projectable, version-traceable. The team owns its own informational reality.

What follows from this:

The Catalog is platform-survival. If the team migrates off OutSystems entirely (different platform; in-house build; different SaaS), the Catalog persists. The Catalog represents the domain regardless of source. OutSystems is the current source for evidence; the Catalog is the truth; whatever comes after consumes it the same way. V2 outlives OutSystems' role in the team's architecture.

Profile is longitudinal evidence. Profile across time = data evolution. Profile across environments = drift. Profile across populations = behavioral signal. Profile becomes an analytical artifact, not just a Faker shaping input.

Synthetic data quality is algebraically scoreable. Faker's output measured against Profile across six metric dimensions. Quality becomes a number. Synthetic data isn't "fake"; it's Profile-faithful. V2 can self-evaluate its synthetic outputs and iterate to threshold.

AI agents are consumers, not just collaborators in construction. Playwright agents need domain understanding — that's a Catalog projection. Test agents need realistic data — Profile-shaped Faker. Code agents need to query — GraphQL endpoint emerging from the Catalog. Domain copilots need ontological grounding — the Catalog as substrate. V2 produces what AI agents need to operate intelligently against the team's domain.

V2 emits recipes, not just artifacts. Docker compose files for SQL Server stand-up. Provisioning scripts. Playwright test plan generation invocations. Synthetic data generation parameters tuned to Profile. V2 is closer to Terraform/Pulumi for infrastructure-as-code than to most schema-emitters. The team gets deployable working environments, not just deployable artifacts.

### The post-cutover trajectory (revision 1 — substantially cut in revision 2)

The cutover earns V2's existence. Post-cutover is where V2 becomes the substrate the team builds on:

- **Local development.** Per-developer Docker SQL Server with Profile-shaped synthetic data, queryable via local GraphQL, testable via local Playwright agents, completely disconnected from shared environments. Iteration speeds up; experimentation costs near-zero.
- **Schema evolution.** Repeatable across all four environments via the same machinery that drove the cutover. User FK reflow and migration-data injection workflows continue running through V2.
- **GraphQL endpoints.** Sibling Π emerging without rebuilds. Catalog plus new emitter delivers it. Packageable per module.
- **Drift detection.** Read-side adapter operating against deployed databases compares back to source Catalog continuously; drift surfaces as a Diagnostics finding rather than a production incident.
- **CI/CD substrate.** Every PR triggers canary; every Profile update refreshes Playwright test plans; every schema change propagates to GraphQL/faker/refactor logs.
- **Personal tooling.** V2 packageable per module means anyone with an OutSystems module and a question can point V2 at it. The maintainer's flow-metrics-from-the-code-review-app use case is canonical.
- **V1 sunset.** ADMIRE.md transitions every V1 component to extracted-and-verified. V1 becomes historical reference. V2 is the live system.
- **Possibly: community contribution.** If the abstractions hold up, V2 could open-source as a shared resource for any team running OutSystems with external-schema or analytical needs. Optional; the option emerges from the work whether or not it gets exercised.

### What V2 ultimately is (revision 1)

V2 is the team's sovereignty over its own metadata.

Full canonical access to the domain model, with self-validation, history-awareness, multi-target projection, multi-environment consistency, AI-agent legibility, and indefinite extensibility. The cutover is the moment the sovereignty earns its existence; everything after is the team operating from sovereignty rather than dependency.

The OutSystems platform is one source for the Catalog's evidence; the Catalog is the truth; V2 is the team's instrument for working with that truth across every surface that needs it — including surfaces that don't yet exist, for consumers that haven't been imagined yet.

The cutover is the load test. The information sovereignty is what V2 ultimately is. The trajectory afterward is compound interest on the sovereignty, paid back in capabilities the team didn't know it would have.

### Closing (revision 1)

V2 inherits empirical foundation from V1, faces the External Entities cutover as its forcing function, holds algebraic discipline as the structural condition for cutover trustworthiness, widens through the sibling chorus into informational sovereignty, and sustains the team's right to know what they have built across the trajectory beyond the cutover.

The codebase is the artifact. The cutover is the load test. The disciplines are the contribution. The collaboration pattern (V1 was AI-collaborative; V2 sustains the pattern across multi-instance Claude horizons) is the worked example.

Hold the spine.

— Recorded for the receiving agent. (Revision 1.)
