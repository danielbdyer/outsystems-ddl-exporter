# Chapter 4.2 close — User FK reflow (V2-driver KPI Phase 4)

**Sessions:** chapter-4.2 opened on `claude/chapter-4-ddd-improvements-XVCAM`; slices α → η shipped (commits `17930c2` → `08a75cf`). Joint commit arc with the canary-suite hang fix (`fafa8fd`), the typed-AST + Tier-3 deferrals cash-out across chapter 4.1.B closure, and the chapter 4.1.B close-ritual discharge (`c831229`).

This document discharges chapter 4.2's eight-item close ritual now that the structural slice arc α → η is end-to-end + cashes out A32 (passes may produce emitter-consumable values) at its scheduled cash-out site.

---

## Why this close

Per `V2_DRIVER.md` per-axis correctness stakes: the User-FK reflow axis is HIGH-stakes (production reports break or data loss when User remapping is incomplete). The cutover-window safety criterion is "every reflowed row's `CreatedBy` and `UpdatedBy` resolves to a real target-environment user — no orphan FKs to phantom users, no silently dropped attributions."

Chapter 4.2 ships the structural commitment that makes the criterion provable. The multi-environment commutativity property test (slice η; the chapter signature deliverable) operationalizes the proof.

---

## What shipped (slice arc α → η)

### Slice α — UserMatchingStrategy DU + identity types + Policy axis (`17930c2`)

- **`UserId` / `SourceUserId` / `TargetUserId` / `Email`** — value-object newtypes preventing orientation confusion at compile time. `Email.create` validates non-blank + normalizes via `Trim()` (V1 parity per `UserMatchingEngine.cs:84-86, 97`).
- **`UserMatchingStrategy` closed DU** with four variants per pre-scope §3 (`ByEmail` / `BySsKey` / `ManualOverride of Map<SourceUserId, TargetUserId>` / `FallbackToSystemUser of fallback × primary`). Recursive composition on the `primary` arm so `FallbackToSystemUser` structurally encodes the safety-net pattern.
- **`Policy` record extended** to five axes (`UserMatching : UserMatchingStrategy`). Record-extension closed-DU empirical-test holds: one literal-construction site updated (`Policy.empty`).

### Slice β — UserPopulation in Profile (`4678a76`)

- **New file `Projection.Core/UserIdentity.fs`** (compiles before Profile.fs so Profile can carry the typed user-population fields). Identity types + user-population shapes co-located in the user-identity bounded context per pillar 8.
- **`UserAttributes<'id>` + `UserPopulation<'id>`** parameterized over identity orientation. Type system carries the source/target distinction at the population level (refines pre-scope §4's loose "or TargetUserId, contextually" framing into typed polymorphism).
- **`Profile.SourceUsers + Profile.TargetUsers`** as sibling fields to Columns / Distributions / CdcAwareness. A34 orthogonality preserved.

### Slice γ — UserRemap.fs in Core (`4678a76`)

- **`RemapDiagnostic` closed DU** with five variants per pre-scope §4 (`NoEmail` / `EmailDidNotMatch` / `SsKeyDidNotMatch` / `OverrideMissing` / `NoFallbackConfigured`).
- **`UserRemapContext` record** (Mapping + Unmatched + Diagnostics) replaces the chapter-4.1.B slice ζ placeholder (`Map<SsKey, Map<int64, int64>>`).
- **Smart constructor `UserRemapContext.create`** enforces the disjointness invariant (`Mapping.Keys ∩ Unmatched = ∅`) per the structural-commitment-via-construction-validation operational principle. Validation-style accumulation reports every overlapping source user.
- **Module accessors:** `empty`, `isFullyMapped`, `unmatchedCount`, `tryFindTarget`.

### Slice δ — UserFkReflowPass.discover (ByEmail) (`d2a091d`)

- **`UserFkReflowPass.discover` + `run`** return the canonical pass shape `Lineage<Diagnostics<UserRemapContext>>` per the writer-fidelity codification.
- **ByEmail real**; other strategies emit `userFkReflow.strategyNotYetImplemented` `Error` diagnostic per total-decisions discipline.
- **Per-source iteration** sorted by SsKey for T1 byte-determinism; email index built once per call (case-insensitive `OrdinalIgnoreCase` lookup); first-occurrence-wins on duplicate-email collision.

### Slice ε — Full strategy DU coverage (`a0e9807`)

- **`applyStrategy`** recursive walker: handles every `UserMatchingStrategy` variant. `FallbackToSystemUser` composes naturally via recursive `match`; nested fallback chains compose.
- **`BySsKey`** via SsKey-keyed target index; **`ManualOverride`** via direct `Map.tryFind`; **`FallbackToSystemUser`** structurally guarantees `Set.isEmpty Unmatched` (the safety-net catches every miss).
- **Lazy indexes** (F# `Lazy<'a>`): pure-`ManualOverride` strategies pay zero cost building email/SsKey indexes; the index materializes iff the strategy reaches a branch that consults it.

### Slice ζ — IsUserFk : bool on Reference (`693eb13`)

- **`Reference.IsUserFk : bool` field** added; record-extension closed-DU empirical-test held across **23 literal-construction sites** (4 production + 19 test fixtures across 13 files).
- **`SymmetricClosure` inherits** the flag on inverse references.
- **OSSYS adapter** defaults to `false` pending the OSSYS-platform user-kind identification surface (deferred at this close — see §below).
- **`Kind.tryFindAttribute`** (lifted to Core at slice ε) is the second consumer for User-FK column-name resolution.

### Slice η — UserRemapContext wiring + multi-environment property test (`08a75cf`)

- **`userFkColumnNames : Kind -> Set<Name>`** in `MigrationDependenciesEmitter`: resolves IsUserFk references to their source-attribute Names.
- **`rewriteUserFkColumns`**: per-row rewrite — parses raw source-id integer, looks up in `UserRemapContext.tryFindTarget`, substitutes target-id. Returns `None` (drops row) if any User-FK column unmatched (V1 "diagnostic + skip" parity).
- **`emitWithTopo` signature extended** with `userRemap: UserRemapContext`; backward-compatible `emit` defaults to `UserRemapContext.empty`; new `emitWithUserRemap` is the pipeline-integration entry.
- **Composer** threads `userRemap` to MigrationDependenciesEmitter dispatch.
- **StaticSeedsEmitter unchanged** per pre-scope §5 (static lookup data isn't user-attributed).
- **Multi-environment commutativity property** (the chapter signature deliverable): same source population + ByEmail strategy against four distinct target populations (Dev/QA/UAT/Prod-shaped) yields four `UserRemapContext` values whose source-keyset agrees across all four; smart-constructor invariant holds for each; per-environment differences live entirely in `TargetUserId` values.

---

## Eight-item chapter-close ritual

Per `DECISIONS 2026-05-14 — Chapter-close ritual` + the V1-envelope-walk amendment.

### 1. Active deferrals scan

| Deferral | Status |
|---|---|
| Composition primitives `fallback` / `accumulate` / `wrap` / `lift` | Untriggered |
| Statement DU MERGE/UPDATE promotion (chapter 4.1.B close) | Untriggered (third consumer hasn't surfaced) |
| Sort-vs-data deferral predicate distinction | **Reaffirmed** — chapter 4.2's `userFkColumnNames` (slice η) is the third cycle-metadata consumer kind; it asks neither sort-edge breakability nor in-cycle deferral but a sibling-third "which references mark cross-environment User identity?" question. The discipline holds: pick the predicate that fits the semantic question. |
| `ICatalogReader` interface lift | Untriggered |
| **OSSYS adapter User-kind identification surface** | **NEW deferral (codified at this close)** — chapter 4.2 ships every Reference with `IsUserFk = false` from the OSSYS adapter; the actual platform-user-kind identification requires `extension_id` lookup (per V1's `ModelUserSchemaGraphFactory.GetSyntheticUserForeignKeys`). Slice η's emitter integration is structurally complete (rewrites apply when a real User-FK surfaces) but operationally a no-op until the adapter resolves real User-FKs. See DECISIONS entry below. |
| **CSV adapter for `ManualOverride`** | **NEW deferral (codified at this close)** — pre-scope §3 names `Projection.Adapters.UserMap.UserMapLoader` (CSV: `SourceUserId,TargetUserId,Rationale`). Slice ε ships `ManualOverride` consuming a programmatic `Map<SourceUserId, TargetUserId>`; the I/O adapter at the boundary is deferred until a real operator workflow demands the file-format pickup path (mirrors the chapter 4.1.B slice ε NDJSON-adapter deferral). |
| Three-channel Diagnostics split | Untriggered (chapter 4.3 territory) |
| DacFx adoption in DacpacEmitter | Untriggered (chapter 3.x conditional) |

### 2. Contract-vs-implementation walk

The chapter contract per pre-scope §1: "V2's algebraic restatement of V1's UAT-Users pipeline" — output is a `UserRemapContext` value any data-emission Π can consume to rewrite user-FK columns at emission time without each Π re-implementing the matching logic. **Every contract clause is implemented**: `UserMatchingStrategy` DU covers all four V1-derived strategy shapes; `UserRemapContext` carries Mapping + Unmatched + Diagnostics with disjointness invariant; `UserFkReflowPass.discover` produces the typed value via writer-fidelity primitives; `MigrationDependenciesEmitter` consumes it via `Catalog × Profile × MigrationDependencyContext × UserRemapContext` (A18 amended preserved — never via Policy); the multi-environment commutativity property test proves T11 specialization for sibling-Π's commuting on the shared context.

### 3. CLAUDE.md staleness check

Operating-disciplines table current. No new disciplines warrant addition at this close (the existing pillar 1 / pillar 8 / writer-fidelity / two-consumer threshold / closed-DU expansion disciplines covered every slice).

### 4. README.md staleness check

Test baseline 963 non-canary (was 893 at chapter 4.1.B close). Update pending in this close commit.

### 5. HANDOFF.md scope

New chapter-4.2 prologue at this close (this commit). Names load-bearing (UserMatchingStrategy on Policy; UserPopulation on Profile; UserRemapContext in Core; UserFkReflowPass; IsUserFk on Reference; MigrationDeps User-FK rewrite) + deferred (OSSYS adapter User-kind identification; CSV adapter) + the V1-input-envelope walk pointers.

### 6. Fresh-eye walk (cross-document drift)

- `KICKOFF.md` baseline test count refresh pending — was 893; now 963 non-canary + ~16 Docker-dependent canary.
- `V2_DRIVER.md` Phase 4 status: **closed** (was "not-started (critical)").
- `BACKLOG.md` (forwarder) — no changes.

### 7. V1-input-envelope walk

V1's `UserMatchingEngine.Execute` (`src/Osm.Pipeline/UatUsers/UserMatchingEngine.cs:19-79`) + `UserMapLoader.Load` (CSV input shape) + `UserMatchingResult` (V1 output shape) are the three empirical references.

- V1's `CaseInsensitiveEmail` strategy → V2's `ByEmail` (OrdinalIgnoreCase + Trim parity).
- V1's `ExactAttribute` strategy → V2's `BySsKey` ONLY when V1's configured attribute IS SsKey; other attributes are deliberate divergence (Skip-stubbed at the V1 differential test layer per pre-scope §9).
- V1's `Regex` strategy → V2's `ManualOverride` (operator-supplied transformation; structurally indistinguishable for V2's algebraic purposes).
- V1's orthogonal `Ignore | SingleTarget | RoundRobin` fallback dimension → V2's `FallbackToSystemUser` variant (composition over orthogonality per pre-scope §3 rationale).
- V1's `UserMapLoader.cs` CSV format (`SourceUserId,TargetUserId,Rationale`) → boundary adapter deferred (see Active deferral above).
- V1's "diagnostic + skip" behavior (`UserMatchingResult.cs` + `EmitArtifactsStep.cs`) → V2's `MigrationDependenciesEmitter.rewriteUserFkColumns` (drops unmatched rows; diagnostic already emitted by `UserFkReflowPass.discover`).

The V1↔V2 differential test (per pre-scope §9; deferred — pending V1's existing `UserMatchingEngineTests.cs` fixture availability) is a sibling reachability item to chapter 4.2 close: the structural divergences are all explicitly named.

### 8. AXIOMS.md amendment cash-out — A32

**A32 cashed out at this close.** Per pre-scope §10:

A32 (originally codified 2026-05-06; "passes may produce values consumed by emitters") named the algebraic shape but had limited concrete instances. Chapter 3.1's `TopologicalOrderPass.runWith` was the first structurally-realized instance — the minimal pattern (single pass, multiple emitter consumers). Chapter 4.2 lands the full-shape instance: `UserFkReflowPass.discover` produces `UserRemapContext` via the dual-writer (`Lineage<Diagnostics<UserRemapContext>>`); sibling Π's consume the context to rewrite User-FK column values.

The property test "A32: discovered value visible to emitter" cashes out as the multi-environment commutativity property (slice η): four sibling Π's (one per target environment) commute on the shared source population's user-keyset; per-environment differences live entirely in the target-id substitutions.

The amendment body lives at `AXIOMS.md` (full text written in this close commit).

---

## Test count

- **963 non-canary tests passing** (was 893 at chapter 4.1.B close; **+70 across all of chapter 4.2**)
- **~16 Docker-dependent canary tests** (unchanged)
- **Lint clean** across 27 rules
- **Build clean** under `TreatWarningsAsErrors=true`

---

## What's load-bearing going forward

Chapter 4.2's structural commitments that future chapters inherit:

- **`Policy` is now a five-axis aggregate** (Selection / Emission / Insertion / Tightening / UserMatching). Future axis-additions follow the same record-extension pattern (closed-DU empirical-test discipline applied to records).
- **Profile carries typed per-environment user populations** (`UserPopulation<SourceUserId>` + `UserPopulation<TargetUserId>`). Future per-environment evidence (e.g., per-env CDC capture instances; per-env constraint posture) earns its place on Profile.
- **`UserRemapContext` smart-constructor disjointness invariant** holds for every value the discovery pass produces.
- **`Reference.IsUserFk : bool`** is the structural User-FK marker; emitters at chapter 4.3 (Diagnostics) + future chapters consume the typed flag rather than heuristic name detection.
- **A32 structurally realized** — the pass-produces-emitter-consumable-value pattern is now a wired template, not a scheduled axiom. Future passes producing context values inherit the writer-fidelity discipline (Lineage<Diagnostics<'a>> return shape).

---

## What's deferred (with explicit triggers)

### OSSYS adapter User-kind identification surface

Chapter 4.2 ships the IR refinement (`Reference.IsUserFk`) and the emitter integration (slice η rewrite at MigrationDependenciesEmitter). The OSSYS adapter at `Projection.Adapters.Osm.CatalogReader` currently sets `IsUserFk = false` for every Reference because resolving the platform-user-kind identity requires V1's `extension_id` lookup pattern (per `ModelUserSchemaGraphFactory.GetSyntheticUserForeignKeys`). **Trigger to cash out**: a real OSSYS-source-V2-target reflow workflow surfaces with User-FK columns operators need rewritten. At that point the OSSYS adapter gains a `userKindIdentity : Catalog -> SsKey option` resolution surface; references whose `TargetKind` matches the identified user kind get `IsUserFk = true`.

### CSV adapter for `ManualOverride`

Pre-scope §3 names `Projection.Adapters.UserMap.UserMapLoader` (CSV: `SourceUserId,TargetUserId,Rationale`). Slice ε ships `ManualOverride` consuming a programmatic `Map<SourceUserId, TargetUserId>`; the I/O adapter at the boundary is deferred. **Trigger**: a real operator workflow demands the file-format pickup path. Mirrors the chapter 4.1.B slice ε NDJSON-adapter deferral.

### V1↔V2 differential test

Per pre-scope §9: V1's `UserMatchingEngineTests.cs` fixture is the oracle. The differential test loads a representative scenario, runs both V1's `UserMatchingEngine.Execute` and V2's `UserFkReflowPass.discover`, projects to a common `Map<SourceUserId, TargetUserId option>` shape, and asserts equality (modulo deliberate divergences). **Trigger**: V1 fixture canonicalization at the trunk surface stabilizes — i.e., V1's `UserMatchingEngineTests.cs` becomes the canonical reference shape rather than an in-flight test surface.

### `SourceTag` value-object refactor of SsKey

Per pre-scope's "what this chapter does NOT do" list: the SourceTag refactor of `SsKey` is a separate slice arc that would land if cross-version `V1Mapped` derivation surfaces under real cutover demand. Today the existing four-variant `SsKey` (`OssysOriginal | Original | Synthesized | Derived`) covers chapter 4.2's needs. **Trigger**: a real cross-version reflow workflow with V1Mapped-derived target identities surfaces.

---

## What this close enables

- **Chapter 4.3 (three-channel Diagnostics split)** — the next critical-path chapter. The substrate is already shipped (the passes emit `Diagnostics<'a>` entries); chapter 4.3 is projection, not new algebra. Chapter 4.2's `Lineage<Diagnostics<UserRemapContext>>` shape inherits naturally — the `UserFkReflowPass` entries flow into the operator-channel split.
- **Chapter 4.1.A slices 6/7/8 (cross-module FKs / identity + defaults / extended properties)** — now-unblocked per chapter 3.2 SnapshotRowsets. Independent from chapter 4.2; ready to ship.
- **R4 multi-environment promotion property test** — independent forward-progress; uses M4 Tolerance taxonomy + chapter 4.2's multi-environment commutativity property as a worked precedent.

---

## Closing

Chapter 4.2 is the V2-driver KPI's User-identity verification depth. The cutover-window safety criterion (every reflowed row's `CreatedBy` / `UpdatedBy` resolves to a real target-environment user) is structurally provable: the multi-environment commutativity property (slice η) is the property the cutover team most needs proven, and it's proven structurally.

Chapter 4.2 closed (2026-05-11).
