# V2 — Vision (Revision 2)

**Date:** 2026-05-08
**Supersedes:** much of `VISION.md` (revision 1, commit `2fb51ef`).
**Predecessor preserved:** `VISION.md` is retained as historical context per the append-only discipline. Read this document first when sequencing work.

This revision absorbs the four-subagent review (`VISION_REVIEW.md` + appendices A–H) and sharpens revision 1 along the lines that survived critique.

## What changed in revision 2

- **Cut the §"informational widening"** — platform-survival rhetoric, AI-agent substrate as a section, six-dimension Faker quality scoring, open-source contribution, recipes-as-Terraform. None had a forcing function or a cutover dividend (Appendix G).
- **Promoted drift detection to cutover-critical.** Read-side adapter pointed at four real DBs is the cutover's safety net, not post-cutover trajectory (Appendix C, Appendix G).
- **Sharpened V2's unique contribution vs. V1.** V2 emits a *sibling chorus* of synchronized projections — SSDT DDL (production deployment, promoted to Azure DevOps integration test), CDC-aware data inserts (`StaticSeeds` / `MigrationDependencies` / `Bootstrap`), DACPAC (fast iteration and baselining), refactor log, distributions. V1 hand-curates one surface; V2 emits all from a single `Catalog × Policy × Profile` triple. T11 is the cross-validation surface (R1 in `VISION_REVIEW.md`).
- **Added acceptance criteria** replacing unfalsifiable rhetoric ("sovereignty," "constitutive," "auditability is type-system-encoded").
- **Added cutover fallback ladder** (V1-only / V2-augmented / V2-driver) with T-30-day decision criterion (Appendix D §6).
- **Added the dogfood frame.** V2 verifies V1 starting *this week* via the existing `JsonEmitter` round-trip — before any new V2 emitter ships (Appendix F).
- **Re-sequenced chapter 3.** Read-side adapter promoted to 3.1 (it has two consumers from day one: V1 verification + drift detection); SnapshotRowsets becomes 3.2; DacpacEmitter 3.3; canary closure 3.4; RefactorLogEmitter 3.5 (Appendix F §7).
- **Named the type-system refactor** that turns T11 and A1 into type theorems: `ArtifactByKind<'element>` private DU + `SsKey` four-variant split + `CatalogDiff` (Appendix H).
- **Named the canary-as-property-test architecture** that replaces ~2000 lines of hand-curated integration tests with ~600 lines of FsCheck generators and predicates (Appendix E).

## What V2 uniquely contributes

V1 ships the cutover-relevant capabilities VISION.md revision 1 listed (extraction, two-phase inserts, FK reflow, multi-environment promotion). The skeptic was right to flag this (Appendix A §2). V2's load-bearing differentiator is **the sibling chorus + verification**, not displacement of any single V1 surface.

**Target artifacts (all sibling Π's of the same Catalog).** V2 emits multiple synchronized projections, each shaped for its consumer:

- **SSDT DDL** (per-table `.sql` shape, V1's existing format) — the **production deployment** surface. Promoted into the Azure DevOps integration-test lane and verified by the canary read-back before merge. V2's emitter is opinionated, deterministic, and refactor-log-aware.
- **CDC-aware data inserts** — `StaticSeedsEmitter` / `MigrationDependenciesEmitter` / `BootstrapEmitter` (chapter 4.1) produce data emissions that respect CDC-tracked tables: topologically-sorted two-phase insertion, redeploy-idempotent, and tagged so insert plans avoid spurious change records on tables where CDC is enabled. This is one of the load-bearing cutover demands; V2 makes it algebraic where V1 does it by hand.
- **DACPAC** — the **fast-iteration and baselining** surface. Declarative artifact for ephemeral-DB deploy via DacFx, used in canary tier-1/tier-2 property tests for sub-second-to-150ms feedback cycles. Refactor.log records ride alongside, communicating renames so DacFx incremental deploys ALTER rather than DROP+CREATE. The DACPAC is *not the only* deployment artifact — it is the artifact whose iteration cost is small enough to drive the canary's property-test surface (Appendix E).
- **Refactor log** (`RefactorLogEmitter`) — sibling Π over `CatalogDiff`. SSDT-native format consumed by both the SSDT and DACPAC paths.
- **Distributions** (`DistributionsEmitter`, shipped) — evidence projection consumed by tightening passes and operator diagnostics.

V1 emits one artifact class (SSDT scripts) hand-curated per environment; V2 emits the chorus from a single Catalog × Policy × Profile triple. T11 (sibling-Π commutativity) is the cross-validation surface: if SSDT and DACPAC disagree on a kind's shape, one of them has a bug. V2 also adds **verifiable correctness** (canary loop on whichever surface is deployed) and **partial-state remediation** (`RemediationEmitter` as a thin composition over `CatalogDiff`), neither of which V1 has and neither requires V1 changes.

**Verification posture, two lanes:**

- **Fast lane (DACPAC).** Tier-1 property tests run pure (no Docker) on the F# emitter's `TSqlModel` round-trip. Tier-2 property tests run against testcontainers ephemeral SQL Server. Sub-second to ~150ms per case. Drives most of the canary's coverage.
- **Promoted lane (SSDT + CDC-aware data inserts).** Once a slice is fast-lane-green, V2 emits the SSDT shape + CDC-aware data inserts and promotes the artifact into Azure DevOps's integration-test environment. The canary reads the deployed schema via the read-side adapter (chapter 3.1) and asserts equality-by-SsKey against the V2-expected Catalog. The redeploy-zero-ALTER assertion runs on the live integration target — this is where CDC-safety is actually verified.

Both lanes use the same `Catalog × Policy × Profile` algebra and the same `Emitter<'element>` shape. The DACPAC is fast; the SSDT + data lane is real. The canary verifies both.

## The forcing function (compressed)

300-table OutSystems 11 Reactive system, mid-development. External Entities cutover: every Entity and Static Entity swapped 1:1 to external on-prem SQL Server. Four environments (dev/qa/UAT/prod), Azure DevOps PR promotion. CDC running in production. User FKs (CreatedBy/UpdatedBy) require environment-specific remapping. RefactorLog records must survive across schema versions. Repeatable cadence — schema and data evolve continuously.

Failure mode: production data integrity corrupted; CDC-dependent features broken silently; partial cutover leaves hybrid state structurally hard to recover from.

V1 ships the cutover. V2 makes it verifiable, reversible, and repeatable past the cutover.

## V1's empirical foundation (compressed)

V1 discovered the specializations, FK reflow, two-phase inserts, profile interventions, and migration-team workflows under lived production pressure. V2 inherits the empirical knowledge as data, not as typed cross-references. ADMIRE.md tracks the V1 → V2 extraction with three states (admiring → extracting → extracted) gated on canary evidence, not differential tests alone.

## The algebraic core

V2 is a metadata projection compiler:

```
Project = Π ∘ E
```

Four inputs:
- **Catalog** — environment-invariant evidence (the schema; structural truth).
- **Policy** — environment-specific intent (FK reflow strategy; user-matching strategy; migration data overrides). The Policy DU gains a fifth axis in chapter 4: `UserMatching of UserMatchingStrategy` (R3 in `VISION_REVIEW.md`).
- **Profile** — environment-specific evidence (data shape; user populations; value distributions).
- **Lifecycle** — temporal evidence (rename history; version threading).

The structural commitments are **type theorems, not disciplines** (this is the revision-2 sharpening — see Appendix H):

- **T1 (determinism).** `Project` is a pure function. Same triple → byte-identical text/JSON; model-equivalent (DacFx round-trip equality) DACPAC. The CDC-safety property is *T1 × DacFx idempotent-redeploy*, not T1 alone (R2 in `VISION_REVIEW.md`); the canary's redeploy-zero-ALTER assertion verifies the composition.
- **T11 (sibling-Π commutativity).** Encoded as `Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>, EmitError>` where `ArtifactByKind` is a private DU whose smart constructor enforces "every Catalog kind is in the keyset." T11 becomes a type-level obligation, not an `Assert.Contains` discipline.
- **A1 (identity-survives-rename).** Encoded as `SsKey = OssysOriginal of Guid | Synthesized of source*basis | DerivedFrom of parent*reason | V1Mapped of v1Guid*ns`. The current bound (JSON-path strips SSKey, V2 synthesizes from name fields) is type-visible: property tests that claim A1 accept only `OssysOriginal` (and roots of `DerivedFrom`); on `Synthesized`, the same property documents the bound.

These three encodings turn what revision 1 framed as algebra-as-discipline into algebra-as-type-system. The cost is one chapter of refactor (Appendix H §7); the payoff is structural — wide classes of bugs become impossible to write.

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

Predicate library (12 predicates listed in Appendix E §3). Notable: **`idempotentRedeploy`** explicitly verifies the CDC-safety claim that revision 1 left implicit — deploy → redeploy same DACPAC → assert second deploy issued zero ALTERs.

## Acceptance criteria

V2 has earned its existence when, by the cutover-quarter close:

1. **Verifiable correctness.** The canary catches at least one real V1 (or V2) emitter bug before publication, with zero false negatives over the quarter. Tracked as a cumulative count.
2. **CDC safety.** The `idempotentRedeploy` property holds across all generated catalogs at tier-2 (≥30 cases × all generated shapes) and across the four real production schemas at tier-3 (one full redeploy per environment per cutover-week).
3. **Identity.** The `renameSurvives` property holds for every `OssysOriginal` SsKey across the four-environment four-rename test plan. Bound is documented for `Synthesized` SsKeys and tracked for closure when `SnapshotRowsets` lands.
4. **T11 structural.** Every emitter signature is `Emitter<'element>`. The substring `Assert.Contains` tests in `JsonEmitterTests.fs:96-105` and `RichProfilingEndToEndTests.fs:280` retire (the type proves what they assert).
5. **V1 sunset gate.** ADMIRE.md `extracted` status requires a green canary on the relevant component, not just a passing differential test. V1 sunset is deferred until all four environments have run on V2 emissions for one full schema-evolution cycle.

These replace the unfalsifiable claims revision 1 closed on ("V2 is the team's sovereignty," "constitutive," "auditability is type-system-encoded"). Each has a counterexample condition; each is tracked.

## Cutover fallback ladder

Revision 1 named no fallback. Revision 2 names three tiers, with a T-30-day decision criterion:

**V1-only path.** V1's existing pipeline runs the cutover. Trust mechanism is empirical correctness verified by experience. V2 ships post-cutover.

**V2-augmented path.** V1 drives the cutover; V2 runs the canary as a verification-only sidecar (V2 emits, deploys to ephemeral, reads back, compares to V1's emission and to V2's expected Catalog). Disagreement blocks the PR. **V2 owns no production write path.** This eliminates split-brain by construction (R6 in `VISION_REVIEW.md`).

**V2-driver path.** V2 emits DACPAC + StaticSeeds + MigrationDependencies + Bootstrap; V1 retired from cutover. Highest payoff; highest risk.

**Decision criterion at T-30 days.** V2-driver requires (a) chapter 3 closed with green canary on full 300-table Catalog; (b) chapter 4.1 (data triumvirate) shipping; (c) chapter 4.2 (user FK reflow) shipping; (d) ≥1 full UAT dry-run with cross-environment Profile/Policy pairs producing structurally-consistent artifacts. If any of (a)–(d) is yellow at T-30, drop to V2-augmented. If V2-augmented is unstable at T-15, drop to V1-only and ship V2 as post-cutover substrate. **Hard rule: V1 stays warm even on the V2-driver path until cutover+30 days.**

## The work-smarter strategy

Five compounding wins make full realization plausible in the cutover quarter rather than across multiple quarters:

1. **V2 verifies V1 starting today** (Appendix F). V2's `JsonEmitter` plus the existing `CatalogReader` already enable a `osm_model.json` round-trip diff against V1's persisted snapshot. No new code; ships this week. Step 2 (read-side adapter) gives full V2-augmented mode in chapter 3.1.
2. **Read-side adapter via `INFORMATION_SCHEMA` / `sys.*`, not DacFx Extract** (Appendix F §2). ~300–500 lines of F# replaces a chapter of DacFx integration. Two consumers from day one (canary + drift detection). DacpacEmitter is gated on this, not the inverse.
3. **Canary as property-test surface, not hand-curated integration** (Appendix E). ~600 lines of test code replaces ~2000+ lines of curated fixtures. Tier-1 pure properties cover most axioms with no Docker. Generators are bottom-up with structural well-formedness baked in (FK targets exist by construction; no filtering); shrinking is outermost-first (drop module → drop kind → drop reference → drop index → drop attribute).
4. **`ArtifactByKind<_>` type theorem** (Appendix H §4). T11 stops being substring-search discipline. Drift detection becomes pointwise per-SsKey diff (free given the read-side adapter). Partial-state remediation falls out as `dacpacEmitter (CatalogDiff.between deployed target)` — exactly the per-SsKey shape (R5 in `VISION_REVIEW.md`).
5. **Triangulation comparator: three Catalogs, two diffs** (Appendix F §6). `C_ossys` (V2's expected from OSSYS), `C_v1` (read-side from V1's deploy), `C_round` (V2 passes applied to `C_v1`). Pairwise diffs attribute every divergence to V1 / V2 / comparator. Solves the "V2 inherits V1 bugs" critique structurally.

Each of these is independently load-bearing and independently shippable.

## Revised chapter 3 plan

**3.1 Read-side adapter + comparator + minimal `Projection.Pipeline`** (was 3.2). Adapter under `Projection.Adapters.Sql.ReadSide/`; comparator under `Projection.Core/Verification/CatalogEquivalence.fs`; pipeline shell under `Projection.Pipeline/` (C#) reusing `tests/Osm.TestSupport/SqlServerFixture.cs`'s testcontainers wiring. Ships V2-augmented mode against V1.

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

**4.4 `RemediationEmitter`** (composes read-side + DacpacEmitter or DdlEmitter over `CatalogDiff`). Partial-state recovery primitive for the failure mode VISION revision 1 named but did not close (R5 in `VISION_REVIEW.md`).

## What's deferred (compressed from the original five paragraphs)

- **Drift detection** — promoted to cutover-critical (chapter 3 byproduct).
- **GraphQL schema emitter** — defer-with-trigger; trivially `Emitter<GraphqlTypeDef>` once a real consumer asks.
- **Resolvers, FakerEmitter, per-developer Docker, longitudinal Profile diffing, per-module packaging, PR-canary CI hook** — defer-with-trigger as named active deferrals in `DECISIONS.md`.

## What's cut (no longer in vision)

- Platform-survival rhetoric ("V2 outlives OutSystems"). Catalog source-agnosticism is already structural in the V1↔V2 vocabulary mapping; re-asserting it adds rhetoric, not obligation.
- Six-dimension synthetic-data quality scoring. Six adjectives, zero metric definitions; if Faker ever lands, ship one dimension on demand.
- AI-agent substrate as a section. The fact that `Json` and `RawText` emitters happen to be agent-legible is a corollary recorded in `DECISIONS.md`, not a vision pillar.
- Open-source / community contribution. The original "Possibly... the option emerges" framing already conceded the item doesn't belong.
- Recipes-as-Terraform beyond the canary's testcontainers compose file (which is a documented byproduct, not a sub-product).

## Closing

V1 ships the cutover. V2 makes it verifiable, reversible, and repeatable, across two verification lanes — the DACPAC fast lane that drives the property-test surface, and the SSDT-DDL-plus-CDC-aware-data-inserts promoted lane that goes through Azure DevOps integration tests for production deployment. Three structural type theorems (T1 / T11 / A1 encoded as types, not disciplines), one property-test surface (FsCheck + tiered predicates), one read-side adapter (delivering value before any new emitter ships), one triangulation comparator, and a sibling chorus that cross-validates DACPAC against SSDT against the deployed schema are the load-bearing structure. Everything else is parking lot until a forcing function fires.

The five acceptance criteria above are testable. The fallback ladder is named. The drift-detection job covers continuing operations past the cutover. The chapter 3 sequence is concrete and starts delivering value in week one; the chapter 4 sequence promotes V2 from "verifies V1" to "emits production artifacts V1 cannot reach."

Hold the spine. Cut the rest.

— Recorded for the receiving agent. Revision 2.
