# Chapter 4.1.B close — CDC-aware data triumvirate (V2-driver KPI Phase 3)

**Sessions:** chapter 4.1.B in-flight (slices α/β/γ; covered in `CHAPTER_4_1_A_CLOSE.md`'s joint ritual) **plus** the slice arc δ → ι/κ shipped on branch `claude/chapter-4-ddd-improvements-XVCAM` (this session arc; commits `23c9d76` → `340eb15`). The chapter signature deliverable (CDC-silence canary, slice γ) shipped at the joint chapter-4.1.A close arc; this document discharges chapter 4.1.B's own eight-item close ritual now that the structural slice arc α → κ is end-to-end.

---

## Why this close

Per `V2_DRIVER.md` per-axis correctness stakes table: **CDC-silence on idempotent redeploy is the highest-leverage single deliverable in the entire chapter sequence.** Slice γ proved it operationally (`CdcSilenceTests` + `sys.sp_cdc_scan` Agent-less synchronous capture). The slice arc δ → κ closed the chapter's structural data-axis surface: every kind in the catalog flows through one of three sibling-Π emitters (Static / Migration / Bootstrap), composed under a typed `DataComposition` policy DU, with cycle-correct two-phase deploy and globally-ordered Phase-1-then-Phase-2 emission.

The data-axis verification depth gate is green for the V2-driver KPI's highest-stakes axis.

---

## What shipped (slice arc δ → κ)

### Slice δ — two-phase insertion / cycle-breaking (`23c9d76`)

The V2-driver KPI's highest-leverage primitive on the data axis. V1 reference: `Osm.Emission/PhasedDynamicEntityInsertGenerator.cs:88-148` + `IdentifyNullableFKColumns:184`.

- **`DataInsertRow.DeferredFkSet : Set<Name>`** — concept-shaped per pillar 8; identity-of-deferral = `(KindKey, Identifier, column-name)`; same set drives Phase-1 NULL substitution and Phase-2 SET clause.
- **`ScriptDomBuild.buildUpdateStatement` + `UpdateBuildArgs`** — typed AST UPDATE builder per Tier-3 hard requirement (`DECISIONS 2026-05-10 — text-builder-as-first-instinct`). MERGE precedent template; `BooleanComparisonExpression` for WHERE-clause equality.
- **`StaticSeedsEmitter` cycle-aware dispatch** — `cycleMembersOf` derives `Set<SsKey>` from `TopologicalOrder.Cycles`; `deferredColumns` mirrors V1's `(in-cycle ∧ nullable)` predicate; NOT-NULL FKs in cycle are NOT deferred (NULLing would violate the constraint).
- **`TopologicalOrderPass` v3 → v4 self-loop detection** — audit-during-validation cash-out; the v3 comment "Self-loops would require explicit detection — adds when a real fixture surfaces them" anticipated exactly this fixture. Single-node SCCs whose sole member has a self-edge now appear in `Cycles`.

### Canary suite hang fix (`fafa8fd`)

Surfaced during slice-δ verification: full canary filter (`Canary|CdcSilence|EndToEnd|RichProfilingEndToEnd`) was hanging indefinitely. Binary search isolated `CdcSilenceTests` running parallel with other Docker classes against the warm container — CDC's instance-wide effects (`master.sys.databases.is_cdc_enabled` flips + `sp_cdc_scan` instance locks) deadlocked against concurrent CREATE/DROP DATABASE.

- **Layer 1 (broad)** — `[<Collection("Docker-SqlServer")>]` on the 5 Docker-touching test modules + `CollectionDefinition` marker type in new `TestCollections.fs`. xUnit serializes the Docker classes; pure-F# tests still parallelize (~840 tests in ~5s).
- **Layer 2 (structural)** — `Deploy.useEphemeralContainer` (new public function) bypasses the warm-env shortcut. `CdcSilenceTests` now spins its own dedicated SQL Server. CDC infrastructure never touches the warm container's `master`.

Net: full canary suite 24-42s (was hanging at >180s).

### Slice η — DataEmissionComposer + EmissionPolicy.DataComposition DU (`44c4871`)

The dispatch layer for the data triumvirate.

- **`DataComposition` closed DU** on `Projection.Core.Policy` — `AllRemaining` (default; promoted-lane), `AllExceptStatic`, `AllData`. Sibling field on existing `EmissionPolicy` record per pre-scope §3.1 option (b) — preserves the four-axis A12 amendment.
- **`DataEmissionComposer.compose` + `composeWithLineage`** — A18 amended preserved structurally (emitters cannot type-check with a Policy parameter; only the composer reads Policy). Hoisted `TopologicalOrderPass` invocation; lineage trail propagation per writer-fidelity discipline.
- **`StaticSeedsEmitter.emitWithTopo`** — composer-facing entry point that takes the hoisted `TopologicalOrder`. Two-consumer threshold met (standalone `emit` + composer-facing `emitWithTopo`).

### Slice ε — MigrationDependenciesEmitter (`0aa3761`)

Cashes out the Tier-3 hard-requirement Active deferral.

- **`MigrationDependencyRow` + `MigrationDependencyContext`** — operator-published legacy-domain rows arrive as a Profile-shaped sibling input (NOT Policy — A18 amended). Adapter at the I/O boundary (NDJSON / CSV pickup directory) deferred until ingestion consumer demand surfaces; MVP consumers construct the context programmatically.
- **`MigrationDependenciesEmitter`** — same architectural shape as `StaticSeedsEmitter` (Tier-3 cash-out: typed AST is the gold standard). Cycle-aware Phase-1/Phase-2 dispatch via the same `deferredColumns` predicate. CDC-aware MERGE shape per `Profile.CdcAwareness` (slice β parity).
- **`Kind.tryFindAttribute` lift to Core** — slice-δ improvement #5 cash-out at the second-consumer threshold (StaticSeedsEmitter + MigrationDependenciesEmitter both look up source attributes by SsKey; chapter 4.2's `UserFkReflowPass` will be the third).
- **`DataEmissionComposer.composeWithMigration`** — sibling to `compose` for callers supplying programmatic migration rows.

### Slices ζ + θ — BootstrapEmitter + composer partition assertion (`9544006`)

Closes the structural data-triumvirate signature.

- **`UserRemapContext = Map<SsKey, Map<int64, int64>>`** — placeholder shape for chapter 4.2's `UserFkReflowPass`. Slice ζ MVP defaults to `Map.empty` pass-through.
- **`BootstrapEmitter v0`** — structural stub returning empty no-op artifact for every kind (T11 preserved). Gives chapters 4.2/4.3 a fixed insertion point; the partition assertion can ask Bootstrap for its coverage rather than silently assuming it.
- **`EmitError.OverlappingEmitterCoverage of (SsKey * emitters: string list)`** — new EmitError variant; surfaces when two or more sibling emitters both produce populated output for the same kind under the same `DataComposition` (e.g., a kind appearing both in `Modality.Static` AND in the migration team's pickup channel under `AllRemaining`).
- **Composer's `unionSiblings` rewritten** — replaced left-biased `pickFirstNonEmpty` with overlap-detection. Walks kinds in catalog order so the diagnostic is deterministic (same input → same first-overlap kind reported).
- **`composeFull`** — explicit-both-contexts entry; chapter 4.2 wiring point.

### Slices ι + κ — multi-kind cycle global-phase reification + typed Values lift (`340eb15`)

The chapter's structural improvement-surface backlog from slice δ post-commit.

- **`DataInsertScript.RenderedPhase1` + `RenderedPhase2` split** (slice ι) — per-kind text splits into MERGE-only + UPDATE-only; per-kind `Rendered` remains the kind-local concatenation correct for self-FK cycles.
- **`DataEmissionComposer.composeRendered` + `composeRenderedFull`** (slice ι) — produces a single globally-ordered GO-batched T-SQL string where ALL Phase-1 MERGEs across all kinds (in topological order) precede ANY Phase-2 UPDATE. The structural cash-out of slice-δ improvement #2 (multi-kind cycles deploy correctly only when the global Phase-1-then-Phase-2 boundary is preserved).
- **`DataInsertRow.Values : Map<Name, SqlLiteral>` typed lift** (slice κ; pillar 1 strengthening) — pre-κ raw-string `Map<Name, string>` required per-render `SqlLiteral.ofRaw` re-conversion; post-κ the typed shape carries through. Construction-time projection happens once at row construction in the emitter (where the kind's `Attribute` list is in scope); renderers consume the typed shape directly.

---

## Eight-item chapter-close ritual

Per `DECISIONS 2026-05-14 — Chapter-close ritual` + the V1-envelope-walk amendment (session 25).

### 1. Active deferrals scan

| Deferral | Status |
|---|---|
| **MigrationDependenciesEmitter + BootstrapEmitter typed-AST adoption** | ✅ **Cashed out** at slice ε (MERGE typed AST) + slice ζ (Bootstrap stub structural). Chapter 4.1.B's hard-requirement Tier-3 deferral is closed. |
| **Cross-module FK IR refinement** | Untriggered (chapter 4.1.B fixtures don't surface multi-module data) |
| Composition primitives `fallback` / `accumulate` / `wrap` / `lift` | Untriggered |
| `RequireQualifiedAccess` retrofit on KeepReason DUs | Untriggered |
| Strategy registry mechanism | Untriggered |
| `ICatalogReader` interface lift | Untriggered |
| Faker emitter | Untriggered |
| Three-channel Diagnostics split | Untriggered (chapter 4.3 territory) |
| **DacFx adoption in DacpacEmitter** | Untriggered (chapter 3.x is conditional on deploy path) |

Two new deferrals codified at this close (see §below): **Statement DU MERGE/UPDATE promotion** + **Sort-vs-data deferral distinction**.

### 2. Contract-vs-implementation walk

The data triumvirate's contract per `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md` §1: `Catalog × Profile → Result<DataInsertScript ArtifactByKind, EmitError>` with three named projection sites sharing FK-aware ordering, an `EmissionPolicy` dispatcher, and a CDC-aware MERGE shape. **Every contract clause is implemented**: A18 amended (no Policy in emitters) holds at the type level; T11 keyset coverage holds across all three siblings; CDC-aware dispatch is per-kind via `Profile.CdcAwareness`; topological order is hoisted at the composer; partition assertion catches overlap.

The pre-scope's "global Phase-1 ⨄ Phase-2 interleave under composition" promise (§5.2) was deferred at slice η as "structurally enabled but not REIFIED"; **slice ι reified it** via `composeRendered`. Contract = implementation across the slice arc.

### 3. CLAUDE.md staleness check

`CLAUDE.md` operating-disciplines table is current. Two new entries warranted but not mandatory at this close (sort-vs-data deferral distinction is a refinement rather than a new discipline; Statement DU promotion is a deferred-with-trigger entry that lives in `DECISIONS.md` Active deferrals index, not the operating disciplines table).

### 4. README.md staleness check

README test count baseline becomes 893 non-canary (was 882 at chapter-3 close). Updates pending in this commit.

### 5. HANDOFF.md scope

New chapter-4.1.B prologue added at this close (this commit). Names load-bearing (composer dispatch shape; partition assertion; multi-kind global ordering) + deferred (NDJSON adapter for migration; Bootstrap row sources pending chapters 4.2/4.3) + cashed-out (every Tier-3 hard-requirement Active deferral for chapter 4.1.B).

### 6. Fresh-eye walk (cross-document drift)

- `KICKOFF.md` baseline test count refresh pending — was 882; now 893 non-canary + ~16 Docker-dependent canary.
- `V2_DRIVER.md` Phase 3 status: **closed** (was "in-flight; α/β/γ shipped; δ-θ pending").
- `BACKLOG.md` (forwarder) — no changes; still points at `V2_DRIVER.md`.

### 7. V1-input-envelope walk

V1's `PhasedDynamicEntityInsertGenerator.cs:88-148` + `StaticSeedSqlBuilder.cs:211-260` are the two empirical references. Both V1 surfaces inform V2 algebra:

- V1's `IdentifyNullableFKColumns:184` predicate (in-cycle + nullable) → V2's `deferredColumns` predicate (mirror).
- V1's MERGE shape (six clauses) → V2's `ScriptDomBuild.buildMergeStatement` typed AST (parity at the structural level; CDC-aware predicate is V2's load-bearing addition).
- V1's `EntityDependencySorter` cycle classification → V2's `CycleResolution.classify` (Weak/Cascade/Other) — but this is for SORT-edge breakability, NOT data-emission deferral. The two predicates diverge subtly on Cascade-nullable FKs (V2 defers them for data emit per V1's `IdentifyNullableFKColumns`, but doesn't break them for sort). **Codified at this close as the sort-vs-data deferral distinction** (see DECISIONS entry below).
- V1's `PhasedInsertScript` shape (Phase-1 + Phase-2 lists; flat string concatenation at the consumer) → V2's `DataInsertScript` shape (Phase-1 + Phase-2 typed `DataInsertRow` lists + per-kind `Rendered`/`RenderedPhase1`/`RenderedPhase2` text). V2's structural addition: per-kind text BOTH for self-FK self-completeness AND for global cross-kind Phase-1-then-Phase-2 reification.

### 8. AXIOMS.md amendment cash-out

No new AXIOMS amendments earned at chapter 4.1.B close. The `A18 amended` (Π consumes Catalog × Profile, never Policy) holds at the structural level for all three sibling emitters; T11 (sibling-Π keyset coverage) holds across all three. Existing axioms cover the chapter's algebraic claims.

---

## Test count

- **893 non-canary tests passing** (was 845 at session start; +48 across slice δ → κ)
- **~16 Docker-dependent canary tests** (skip-if-no-Docker gated; CDC-silence + RoundTrip + Deploy + EndToEnd subsets all green individually; canary suite no longer hangs after the layer-1 + layer-2 fix)
- **Lint clean** across 27 rules
- **Build clean** under `TreatWarningsAsErrors=true` everywhere

---

## What's load-bearing going forward

Chapter 4.1.B's structural commitments that future chapters inherit:

- **`DataEmissionComposer` dispatch shape** — chapter 4.2 plugs `UserFkReflowPass` into the established `UserRemapContext` shape; the composer's `composeRenderedFull` is the wired pipeline-integration entry.
- **`EmitError.OverlappingEmitterCoverage` partition assertion** — future emitters joining the data triumvirate (Faker, Snapshot Migration, etc.) earn their place by respecting the partition.
- **Hoisted-topo + Lineage propagation** — the composer's `composeWithLineage` is the writer-fidelity template for future composers.
- **Typed `DataInsertRow.Values : Map<Name, SqlLiteral>`** — pillar 1 holds at the row level; consumers query the typed value directly.
- **`composeRendered` global Phase-1-then-Phase-2 ordering** — multi-kind cycle-correct deploy is the structural commitment.

---

## What's deferred (with explicit triggers)

### Migration adapter (NDJSON / CSV pickup directory)

Per pre-scope §2.2 the adapter would read operator-published files into `MigrationDependencyContext`. **Deferred** at slice ε because no real ingestion path consumer surfaced. Trigger: a real migration-team workflow lands that requires file-format flexibility (NDJSON, CSV, or a different operator-supplied format).

### Bootstrap row sources (system users, default policies, profile-attached rows)

Per pre-scope §2.3 Bootstrap "emits inserts for system users, default policies, and any remaining-by-policy kinds whose data is not in StaticSeeds or MigrationDependencies." **Deferred** at slice ζ as the structural stub. Trigger: chapter 4.2 (`UserFkReflowPass`) populates `UserRemapContext` so Bootstrap can emit User FK rewrites; chapter 4.3 (Diagnostics) supplies any per-kind row sources from Profile evidence.

### Statement DU MERGE/UPDATE promotion

3 LINT-ALLOWs per data emitter (StaticSeedsEmitter + MigrationDependenciesEmitter) at terminal text concatenation boundaries arise because `Projection.Core.Statement` DU (used by `SsdtDdlEmitter` for CREATE TABLE / CREATE INDEX / INSERT etc.) doesn't include MERGE / UPDATE variants. **Deferred** because promoting `Statement` to include MERGE / UPDATE would force `ScriptDomBuild.buildStatement`'s exhaustive match across all SSDT consumers — a substantial cross-target refactor with no current consumer demand. **Trigger**: a third MERGE/UPDATE consumer lands (e.g., DacpacEmitter Phase-2 path, future Faker-style data emitter), at which point the LINT-ALLOW count justifies the typed-Statement promotion. See DECISIONS entry below.

### Sort-vs-data deferral predicate distinction

Slice δ uses `(in-cycle ∧ nullable)` for data-emission deferral (mirrors V1's `IdentifyNullableFKColumns`). `CycleResolution.classify` produces `(Weak / Cascade / Other)` for SORT-edge breakability (V1's `EntityDependencySorter`). The two predicates diverge on Cascade-nullable FKs: V2 defers them for data emission (V1 parity) but does NOT classify them as Weak for sort-breaking. **Codified at this close** so future emitters that consume cycle metadata explicitly choose the predicate that fits their semantic question. See DECISIONS entry below.

---

## What this close enables

- **Chapter 4.2 (`UserFkReflowPass`)** — the next critical-path chapter. `UserRemapContext` shape is established; `composeRenderedFull` is the pipeline entry; chapter 4.2 ships the discovery pass that populates `UserRemapContext` from cross-environment user inventory + matching strategy.
- **Chapter 4.3 (three-channel Diagnostics)** — Bootstrap can emit Diagnostics-channel-routed inserts once the chapter-4.3 channel split lands; the composer's existing dispatch tree absorbs without signature change.
- **Chapter 4.1.A slices 6/7/8** — independently deferred on chapter-3.2 SnapshotRowsets; their composition-time integration with the data triumvirate is now a clean signature (composer reads `Policy.Emission.EmitData` to short-circuit data emission when SSDT-only).

---

## Closing

Chapter 4.1.B was the V2-driver KPI's most consequential single chapter. The CDC-silence property is the property the cutover team most needs proven; this chapter shipped the structural commitment that makes it provable end-to-end (slice γ canary GREEN + slices δ → κ closing every structural gap).

The data-axis verification depth gate is green. Chapter 4.2 is the next critical-path move.

— Chapter 4.1.B closed (2026-05-11).
