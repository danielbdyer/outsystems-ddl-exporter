# Chapter 4.1.B pre-scope — CDC-aware data triumvirate

**Pre-scope** for chapter 4.1.B — the CDC-aware data emission triumvirate (`StaticSeedsEmitter` / `MigrationDependenciesEmitter` / `BootstrapEmitter`), the `EmissionPolicy` DU, and per-table `CdcAwareness` configuration. SSDT DDL emission is the sibling slice 4.1.A, scoped separately. This is the **promoted-lane** data-emission surface — the artifact set that goes through Azure DevOps integration tests and against which the load-bearing redeploy-zero-CDC-record assertion fires.

---

## §1 Scope and value

Chapter 4.1.B delivers V2's data-emission half of the production-deployment chorus. V1's `Osm.Emission/Seeds/` and `Osm.Emission/PhasedDynamicEntityInsertGenerator.cs` already perform topologically-sorted two-phase MERGE inserts; V2 inherits this empirical foundation (per `VISION.md` §"V1's empirical foundation" and `ADMIRE.md` discipline) and re-expresses it as the algebraic pair `Catalog × Profile → Result<DataInsertScript ArtifactByKind, EmitError>`, with three named projection sites that share an FK-aware ordering, an `EmissionPolicy` dispatcher, and a CDC-aware MERGE shape.

The cutover stake (per `VISION.md` §"The forcing function"): "**CDC running in production with features depending on it; spurious change records would disrupt those features.**" V1's MERGE always issues UPDATE on match (verified at `/home/user/outsystems-ddl-exporter/src/Osm.Emission/Seeds/StaticSeedSqlBuilder.cs:237` — `WHEN MATCHED THEN UPDATE SET` unconditional), so a redeploy of identical content fires UPDATE → CDC capture-process emits a row → consuming features see a "change." V2 closes this by adding the change-detection predicate to `WHEN MATCHED` so identical-content redeploys are no-ops on tracked tables.

The load-bearing acceptance criterion is the **redeploy-zero-CDC-record assertion** (per `VISION_REVIEW.md` §R2 and Appendix B §B.1):

```
deploy → enable CDC → redeploy same artifact
   → cdc.fn_cdc_get_all_changes_<capture_instance>(...) returns ∅ on every tracked table
```

T1 byte-determinism alone does not deliver this; CDC noise comes from SQL Server's MERGE applying UPDATE, not from V2's emission. The composition is **T1 × idempotent-MERGE × correct change-detection-predicate**. The promoted-lane integration test owns the assertion; the F# core owns the emitter shape that makes the assertion provable.

---

## §2 The three emitters — roles and boundaries

The session-17 strategic frame (`DECISIONS.md` lines 4278–4312) names the three classes explicitly. **MigrationDependency is a policy choice, not a structural property of `Kind`** — the catalog does not carry "this kind is a migration dependency"; the policy does. This mirrors A18 amended (Policy is intent; Catalog is evidence) and is the sharp seam between the three emitters.

### 2.1 StaticSeedsEmitter

Consumes `Catalog` + the static populations the catalog already carries (per A7: `ModalityMark.Static of populations: StaticRow list`, `Catalog.fs:54`). Boundary: kinds whose `Modality` list contains a `Static _` mark. Emits idempotent MERGE statements from the catalog-resident populations.

```fsharp
[<RequireQualifiedAccess>]
module StaticSeedsEmitter =
    [<Literal>]
    let version : int = 1
    let emit
        (catalog: Catalog)
        (profile: Profile)
        : Result<ArtifactByKind<DataInsertScript>, EmitError>
```

`Profile` is consumed only for `CdcAwareness` (per §4 below: CDC-enabled flags are evidence about the deployed schema, not intent — they live in Profile, not Policy). A18 amended is preserved: no `Policy` parameter enters the emitter.

### 2.2 MigrationDependenciesEmitter

Consumes `Catalog` + a `MigrationDependencyContext` value carrying the operator-published legacy-domain rows. The context is *not* in the catalog (because per session-17 §"Three data-emission classes," migration-dependency status is operator intent), but it is *not* `Policy` either — it carries actual row data, not behavioral configuration. Resolve this by treating it as a Profile-shaped sibling input: the context is **environment-specific evidence** the migration team supplies, structurally indistinguishable from per-environment data the read-side might surface.

```fsharp
type MigrationDependencyRow = {
    KindKey   : SsKey
    Identifier: SsKey
    Values    : Map<Name, string>
}

type MigrationDependencyContext = {
    Rows : MigrationDependencyRow list
}
```

Source of truth: a new `Projection.Adapters.Migration.DependencyReader` adapter reading from a pickup directory the migration team publishes (NDJSON or CSV), parsed into `Result<MigrationDependencyContext>`. The adapter is at the boundary; the emitter is pure F# in Core.

```fsharp
[<RequireQualifiedAccess>]
module MigrationDependenciesEmitter =
    let emit
        (catalog: Catalog)
        (profile: Profile)
        (context: MigrationDependencyContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError>
```

The signature carries one Catalog × Profile-shaped sibling (the context is sibling evidence, not Policy — the A18 denial holds).

### 2.3 BootstrapEmitter

Consumes `Catalog` + `Profile` (environment-specific user populations and other empirical evidence) + a `UserRemapContext` produced by chapter 4.2's discovery pass. `UserRemapContext` is canonical per A32 (`AXIOMS.md` §A32: "discovery is one E-pass producing a UserRemapContext value"). Bootstrap emits inserts for system users, default policies, and any remaining-by-policy kinds whose data is not in StaticSeeds or MigrationDependencies.

```fsharp
type UserRemapContext = Map<SsKey, Map<SourceUserId, TargetUserId>>

[<RequireQualifiedAccess>]
module BootstrapEmitter =
    let emit
        (catalog: Catalog)
        (profile: Profile)
        (userRemap: UserRemapContext)
        : Result<ArtifactByKind<DataInsertScript>, EmitError>
```

`UserRemapContext` is consumed at row-emission time to rewrite `CreatedBy`/`UpdatedBy` columns environment-by-environment. Until chapter 4.2 lands, the BootstrapEmitter slice ships with `UserRemapContext = Map.empty` as a pass-through (no rewrites), and the integration tests use a fixture context.

### 2.4 The shape of `DataInsertScript`

Argued: a structured value, not a raw string. Reason: the dispatcher composes outputs in topological order across all three emitters (§5), so the emitters must surface their orderable units. T-SQL is the rendering of the value, not the value.

```fsharp
type DataInsertRow = {
    KindKey       : SsKey            // for ordering / FK lookup
    Identifier    : SsKey            // row identity (PK seed)
    Values        : Map<Name, SqlLiteral>
    DeferredFkSet : Set<Name>        // columns NULLed in phase 1 for cycle-breaking
}

type DataInsertScript = {
    Phase1Merges  : DataInsertRow list   // INSERT-or-NoOp MERGE
    Phase2Updates : DataInsertRow list   // UPDATE to populate deferred FKs
    Rendered      : string               // T-SQL, deterministic, GO-batched
}
```

The dispatcher uses `Phase1Merges` and `Phase2Updates` to interleave the three emitters' rows under a global topological order before concatenating `Rendered` strings. Per A33, both phases run in `TopologicalOrder` (data emission). The structured form survives DECISIONS 2026-05-13 — Discrete-rationale DUs absorb continuous evidence: rather than carry a `RawSql` flag, the shape itself names the orderable units.

`ArtifactByKind<DataInsertScript>` (per `VISION_REVIEW.md` §R7 and `VISION.md` §"The algebraic core") enforces T11 at the type level: every catalog kind that *participates* under the emitter's `EmissionPolicy` is in the keyset. Kinds not participating produce an explicit `Skipped of reason: SkipReason` artifact, never silent absence.

---

## §3 `EmissionPolicy` DU — concrete F# design

### 3.1 The naming collision (load-bearing)

`Projection.Core/Policy.fs:21` already has a record type named `EmissionPolicy`:

```fsharp
type EmissionPolicy = {
    EmitSchema      : bool
    EmitData        : bool
    EmitDiagnostics : bool
}
```

This is the existing Emission axis from A12 amended (2026-05-09): *which artifact families* a projection emits. The new `EmissionPolicy` DU named in `VISION.md` §"Chapter 4 plan §4.1" and `DECISIONS.md` lines 4298–4308 controls *which composition* of data emitters fires when `EmitData = true`. They are not the same axis.

Two clean dispositions; argue for the second:

- **(a)** Rename the new DU to `BootstrapComposition` (matches the term `DECISIONS.md` line 4301 already uses), keep `EmissionPolicy` as the record. **Cost:** the VISION's `EmissionPolicy of AllRemaining | AllExceptStatic | AllData` framing diverges from the implementation name.
- **(b)** Promote `EmissionPolicy` to a richer shape: keep the three booleans but add a `Composition` field; rename the existing record to `EmissionAxis` if needed, or keep `EmissionPolicy` as the umbrella. **Recommended.**

```fsharp
type DataComposition =
    /// Default. Bootstrap emits everything not in StaticSeeds and not in MigrationDependencies.
    | AllRemaining
    /// Skip Static (already populated upstream); MigrationDependencies + Bootstrap fire.
    | AllExceptStatic
    /// Bootstrap emits everything (Static, Migration, and remaining) — schema-suppressed full data refresh.
    | AllData

type EmissionPolicy = {
    EmitSchema      : bool
    EmitData        : bool
    EmitDiagnostics : bool
    DataComposition : DataComposition   // new field; default AllRemaining
}
```

This preserves the four-axis A12 amendment, lands the new DU at the meaningful inflection point (per the discrete-rationale-DU discipline), and keeps the `EmissionPolicy` name VISION uses. The field is added under "IR grows under evidence" — the second consumer for the Emission axis (StaticSeeds emitter) is the trigger.

### 3.2 What each variant means for which emitter fires

| Variant | StaticSeeds | MigrationDependencies | Bootstrap | Combined with `EmitSchema=true` |
|---|---|---|---|---|
| `AllRemaining` | fires | fires | fires (everything not above) | promoted-lane default — full SSDT + data |
| `AllExceptStatic` | skipped | fires | fires (excluding Static populations) | useful when Static already populated |
| `AllData` | fires | fires | fires (every row including Static) | data-only refresh; `EmitSchema=false` |

`EmitSchema = true / EmitData = false` short-circuits the data triumvirate entirely; `EmitData = true / EmitSchema = false` runs the triumvirate without DDL (the data-only refresh case).

### 3.3 Where the DU lives

`Projection.Core/Policy.fs`, alongside the existing `EmissionPolicy` record. Per VISION_REVIEW.md Appendix D §D.2: "EmissionPolicy DU lives in `Projection.Core/Policy.fs` because it is intent, not evidence." Per A18 amended: emitters cannot consume `Policy`. The DU dispatches *through composition*, not *inside* the emitter.

### 3.4 Composition layer

A new module `Projection.Targets.Data.DataEmissionComposer` (a thin `Projection.Targets.Data` project, sibling to `Projection.Targets.SSDT`) owns the dispatch:

```fsharp
[<RequireQualifiedAccess>]
module DataEmissionComposer =
    let compose
        (policy: Policy)               // Composer reads Policy; emitters do not
        (catalog: Catalog)
        (profile: Profile)
        (migration: MigrationDependencyContext)
        (userRemap: UserRemapContext)
        (order: TopologicalOrder)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        match policy.Emission.DataComposition with
        | AllRemaining ->
            let s = StaticSeedsEmitter.emit catalog profile
            let m = MigrationDependenciesEmitter.emit catalog profile migration
            let b = BootstrapEmitter.emit catalog profile userRemap
            ArtifactByKind.unionInTopoOrder order [s; m; b]
        | AllExceptStatic -> // s skipped
            ...
        | AllData -> // bootstrap covers all
            ...
```

The composer reads `Policy.Emission.DataComposition`; the emitters do not. A18 amended is preserved structurally — emitter signatures literally cannot type-check with a `Policy` parameter.

---

## §4 `CdcAwareness` configuration

### 4.1 Shape

```fsharp
/// Per-table CDC discovery evidence. `CdcEnabled` is the set of kinds whose
/// physical realization carries CDC capture; `CdcInstance` carries the
/// capture-instance name when V2 needs to emit it (e.g., for the
/// integration-test `cdc.fn_cdc_get_all_changes_<instance>` query).
type CdcAwareness = {
    CdcEnabled  : Set<SsKey>
    CdcInstance : Map<SsKey, string>
}

[<RequireQualifiedAccess>]
module CdcAwareness =
    let empty : CdcAwareness =
        { CdcEnabled = Set.empty; CdcInstance = Map.empty }
    let isEnabled (key: SsKey) (c: CdcAwareness) : bool =
        Set.contains key c.CdcEnabled
    let captureInstance (key: SsKey) (c: CdcAwareness) : string option =
        Map.tryFind key c.CdcInstance
```

### 4.2 Where it lives — Profile, not Policy

CDC-enabled status is **evidence the deployed schema carries**, not intent the operator supplies. Two reasons it is Profile-shaped:

1. **A34 (Profile is independent of Catalog and Policy).** CDC discovery does not reference Policy; CDC discovery is an empirical observation made against the deployed schema. The emitters that consume `CdcAwareness` are emitters that consume Profile evidence — `Catalog × Profile`, A18 amended, T11 sibling.
2. **The two-environment failure mode argues evidence not intent.** If `CdcEnabled` were on Policy, the operator would be declaring "this table is CDC-enabled" — but the operator does not own that fact in production; the cutover team enabled CDC and V2 must respect what *is*. Intent-shaped CDC declaration would let an out-of-date Policy generate a CDC-noise event by claiming a table is not tracked when it actually is.

The placement: `CdcAwareness` lives as a **sibling field on `Profile`**, alongside `Columns / UniqueCandidates / ForeignKeys / Distributions`:

```fsharp
type Profile = {
    Columns                   : ColumnProfile list
    UniqueCandidates          : UniqueCandidateProfile list
    CompositeUniqueCandidates : CompositeUniqueCandidateProfile list
    ForeignKeys               : ForeignKeyReality list
    Distributions             : AttributeDistribution list
    CdcAwareness              : CdcAwareness     // new — chapter 4.1.B
}
```

`Profile.empty` carries `CdcAwareness.empty`; the `A34` orthogonality property continues to hold (passes that don't read CDC produce identical output for `Profile.empty` and any populated CDC awareness).

### 4.3 Discovery — extension to the chapter 3.1 read-side adapter

The read-side adapter under `Projection.Adapters.Sql.ReadSide/` (chapter 3.1, V2-augmented mode) already produces a `Result<Catalog>` from a deployed SQL Server. Add a sibling function producing `Result<CdcAwareness>` from the same connection:

```sql
-- Discovery query (the read-side adapter wraps this)
SELECT
    SCHEMA_NAME(t.schema_id) AS schema_name,
    t.name AS table_name,
    ct.capture_instance
FROM sys.tables t
INNER JOIN cdc.change_tables ct
    ON ct.source_object_id = t.object_id;
```

(SQL Server's CDC feature is distinct from Change Tracking; `cdc.change_tables` is the capture-instance directory for the CDC feature. If the deployed environment uses Change Tracking instead, the equivalent join is `sys.change_tracking_tables ctt ON t.object_id = ctt.object_id` — but the cutover scenario per VISION explicitly names CDC, not CT, so chapter 4.1.B targets the CDC feature.)

The adapter resolves `(schema, table)` → `SsKey` against the catalog the read-side returned, so `CdcEnabled : Set<SsKey>` is keyed by identity (A4) at the boundary.

### 4.4 How it flows into the emitters

Each emitter consumes `Profile` and uses `Profile.CdcAwareness` to decide the MERGE shape per kind:

- If `CdcAwareness.isEnabled kindKey` is **false**, the emitter uses V1's MERGE shape (V1 already proven correct in trunk; the CDC-noise path is irrelevant).
- If **true**, the emitter uses the change-detection-predicate MERGE (§6 below). This is V2's load-bearing addition.

The dispatch is per-kind, not global, because four-environment migrations may have CDC enabled on a different subset per environment.

---

## §5 Topologically-sorted two-phase insertion

### 5.1 The A33 mandate

A33 says data emission uses `TopologicalOrder`, never `DeterministicOrder` (alphabetical-by-SsKey). The pass already exists at `Projection.Core/TopologicalOrder.fs` and produces `TopologicalOrder = { Mode; Order; Edges; MissingEdges; Cycles; Diagnostics }` (`Catalog.fs:69-76` exists; pass invocation lives elsewhere). The pass tolerates cycles by either falling back to `Alphabetical` (no longer FK-safe) or returning a `Topological` order that breaks cycles via the `CycleResolution` pass.

### 5.2 The two-phase pattern

V1's `PhasedDynamicEntityInsertGenerator.cs` is the canonical reference (`/home/user/outsystems-ddl-exporter/src/Osm.Emission/PhasedDynamicEntityInsertGenerator.cs:88-148`):

- **Phase 1.** For every kind in topological order, MERGE with the row's nullable FK columns set to `NULL` if the FK target is in the same SCC (cycle). For acyclic kinds, full MERGE.
- **Phase 2.** UPDATE the deferred FK columns once all rows from Phase 1 exist.

V2's mechanical translation: each emitter returns `DataInsertRow` records carrying `DeferredFkSet : Set<Name>`; the composer sorts them by `TopologicalOrder.Order`; the renderer concatenates `Phase1Merges` (in topo order) followed by `Phase2Updates` (in topo order).

### 5.3 Order across emitters

The composer interleaves *one* topological order across the union of `(StaticSeeds ∪ MigrationDependencies ∪ Bootstrap)` rows, keyed by `KindKey`. Two emitters cannot emit rows for the same kind under `AllRemaining` (StaticSeeds and Bootstrap partition by Static modality; MigrationDependencies is the policy-marked subset that overlaps neither). Under `AllData`, BootstrapEmitter is the sole emitter for Static kinds; the partition still holds.

The composer asserts the partition holds; violation surfaces as `EmitError.OverlappingEmitterCoverage of (SsKey * EmitterName list)`.

### 5.4 Cycle handling

`TopologicalOrder.Mode = JunctionDeferred` (existing) and the `Cycles : CycleDiagnostic list` field name the unresolved SCCs. Phase 1 ranges over unresolved-cycle kinds with their nullable FKs deferred; if an FK is non-nullable and lies inside a cycle, the row cannot be inserted in two phases — emit `EmitError.UnresolvableCycle of CycleDiagnostic` and the integration test fails fast. (Schema-level cycle resolution is chapter 2's `CycleResolution`; data-level cycles are a separate concern surfaced here.)

### 5.5 Self-referencing FKs

Common case: `employee.manager_id → employee`. Self-references are always solvable in two phases — Phase 1 inserts with `manager_id = NULL`, Phase 2 updates `manager_id` to the resolved value. The phase-2 UPDATE is always a no-op-on-redeploy because the value already matches.

---

## §6 Idempotent MERGE pattern for CDC-safety

### 6.1 V1's current pattern (insufficient)

`Osm.Emission/Seeds/StaticSeedSqlBuilder.cs:237`:
```sql
WHEN MATCHED THEN UPDATE SET
    Target.[Col1] = Source.[Col1],
    Target.[Col2] = Source.[Col2]
```
Unconditional. Every redeploy issues UPDATE on every row → CDC capture-process emits a change record per row → CDC-dependent features see a "change."

### 6.2 V2's pattern

```sql
MERGE INTO [dbo].[StaticTable] AS Target
USING
(
    VALUES
        (1, N'Active',   1),
        (2, N'Inactive', 0)
) AS Source ([Id], [Label], [IsEnabled])
    ON Target.[Id] = Source.[Id]
WHEN MATCHED AND
    (
        (Target.[Label]     <> Source.[Label]
            OR (Target.[Label]     IS NULL AND Source.[Label]     IS NOT NULL)
            OR (Target.[Label]     IS NOT NULL AND Source.[Label]     IS NULL))
     OR (Target.[IsEnabled] <> Source.[IsEnabled]
            OR (Target.[IsEnabled] IS NULL AND Source.[IsEnabled] IS NOT NULL)
            OR (Target.[IsEnabled] IS NOT NULL AND Source.[IsEnabled] IS NULL))
    ) THEN UPDATE SET
        Target.[Label]     = Source.[Label],
        Target.[IsEnabled] = Source.[IsEnabled]
WHEN NOT MATCHED THEN INSERT
    ([Id], [Label], [IsEnabled])
    VALUES
    (Source.[Id], Source.[Label], Source.[IsEnabled]);
GO
```

The change-detection predicate guards the UPDATE branch. The composite predicate per non-PK column is:

```
(Target.[Col] <> Source.[Col]
    OR (Target.[Col] IS NULL     AND Source.[Col] IS NOT NULL)
    OR (Target.[Col] IS NOT NULL AND Source.[Col] IS NULL))
```

The third and fourth disjuncts handle SQL Server's three-valued logic — `NULL <> NULL` evaluates UNKNOWN (not TRUE), so plain inequality misses null-state changes. The disjuncts make the predicate `value-different OR null-state-different`.

For redeploys with identical data, every disjunct evaluates FALSE; `WHEN MATCHED` does not fire; no UPDATE issues; no CDC record is emitted. **This is the load-bearing property the integration test verifies.**

### 6.3 When the predicate is omitted

For non-CDC tables (per `CdcAwareness.isEnabled` returning `false`), the emitter omits the change-detection predicate and uses V1's unconditional `WHEN MATCHED THEN UPDATE`. Reason: the predicate adds T-SQL surface area and a small runtime cost; on non-tracked tables the redeploy-UPDATE has no observable consequence, and matching V1's shape preserves byte-equivalence for trunk parity tests.

The decision is structural — driven by `CdcAwareness`, not a Policy flag. A four-environment cutover where dev has no CDC, qa has CDC on a subset, UAT and prod have CDC on the full set produces three distinct artifact shapes from a single Catalog × Policy × per-environment-Profile triple.

### 6.4 Identity-insert and DELETE branches

V1's `StaticSeedSqlBuilder` emits `SET IDENTITY_INSERT … ON` when the table has identity columns and `WHEN NOT MATCHED BY SOURCE THEN DELETE` under `StaticSeedSynchronizationMode.Authoritative`. V2 inherits both, gated identically. The DELETE branch fires CDC events on tracked tables when actual deletes occur — but for idempotent redeploys there is nothing to delete, so no events fire. The DELETE branch's CDC-safety is structurally automatic.

---

## §7 Slice-by-slice breakdown

Decomposing chapter 4.1.B into nine slices. Each slice is a single PR; tests run green at slice close. Sequencing respects two real dependencies: (a) `EmissionPolicy` DU must land before any emitter dispatches under it; (b) topological ordering must land before the two-phase pattern.

| # | Slice | Goal | Test | File(s) | LOC | Acceptance |
|---|---|---|---|---|---|---|
| 1 | `EmissionPolicy.DataComposition` DU + composer skeleton | Land the DU + dispatcher in `Policy.fs` and `DataEmissionComposer.fs`. Composer takes catalog/profile/order; routes through three stub emitters that return empty `ArtifactByKind`. | EmissionPolicy DU exhaustiveness; `Policy.empty` extends to `DataComposition = AllRemaining`. T1 on the empty composition. | `Projection.Core/Policy.fs` (extend); `src/Projection.Targets.Data/DataEmissionComposer.fs` (new project) | ~250 | Composer compiles; three stub emitters return `Skipped` for every kind; composer round-trips the empty input. |
| 2 | `CdcAwareness` discovery in read-side adapter | Add `CdcAwareness` type to `Profile.fs`; extend the chapter 3.1 read-side adapter with a `discoverCdc` function querying `cdc.change_tables`. Resolution from `(schema, table)` to `SsKey` at the boundary. | Adapter test against testcontainers SQL Server with CDC enabled on a fixture table; A34 orthogonality preserved. | `Projection.Core/Profile.fs` (extend); `Projection.Adapters.Sql.ReadSide/CdcDiscovery.fs` (new) | ~300 | `Profile.CdcAwareness` non-empty when adapter runs against a CDC-enabled table; `Profile.empty` carries `CdcAwareness.empty`. |
| 3 | `StaticSeedsEmitter` minimal slice | Single Static kind, no FKs, no CDC. Render V1-shape MERGE via `StaticRow` traversal. | Golden file vs. fixture catalog; T1 byte-equality; T11 partial — every Static kind in `ArtifactByKind` keyset. | `src/Projection.Targets.Data/StaticSeedsEmitter.fs` | ~400 | Round-trip determinism; output matches V1 byte-for-byte on the fixture. |
| 4 | Topological ordering across multiple Static kinds | Wire `TopologicalOrder.Order` into the composer; assert FK-respecting render across two-or-more Static kinds with cross-kind references. | A33 property: parent kind precedes child kind in render. `MissingEdges` non-empty surfaces an `EmitError`. | `DataEmissionComposer.fs` (extend); test fixtures | ~150 | Order respects every FK in the generated catalog. |
| 5 | Two-phase insertion for cyclic FKs | Self-referencing FK (`employee.manager_id → employee`) and 2-cycle FK pair. Phase 1 NULLs the deferred columns; Phase 2 issues UPDATEs. | Cycle fixture: deploy → assert phase-1 INSERT with NULL → assert phase-2 UPDATE populates. | `StaticSeedsEmitter.fs` (extend); `DataInsertScript` shape | ~400 | Cyclic catalog deploys cleanly; FKs resolve after phase 2. |
| 6 | **Idempotent MERGE pattern (the load-bearing slice)** | CDC-aware change-detection predicate; per-kind dispatch on `CdcAwareness.isEnabled`. | Tier-2 property `idempotentRedeploy` against testcontainers + `cdc.fn_cdc_get_all_changes_<instance>` returning empty after redeploy. Tier-1 pure: predicate text shape matches the spec for every column type and every NULL-permitting combination. | `StaticSeedsEmitter.fs` (extend with predicate generator) | ~350 | Redeploy produces zero CDC records on every tracked fixture table. |
| 7 | `MigrationDependenciesEmitter` skeleton + adapter | Pure-F# emitter consumes `MigrationDependencyContext`; adapter `Projection.Adapters.Migration.DependencyReader` reads NDJSON pickup directory. | Round-trip: NDJSON fixture → context → emitter → MERGE → deploy → read-back rows match input. | `src/Projection.Targets.Data/MigrationDependenciesEmitter.fs`; `src/Projection.Adapters.Migration/DependencyReader.fs` (new project) | ~500 | Migration rows emit in topological order alongside Static seeds; redeploy is CDC-zero. |
| 8 | `BootstrapEmitter` skeleton (Map.empty UserRemap) | Emit non-Static, non-Migration, policy-Selected kinds with environment Profile rows; `UserRemapContext = Map.empty` until chapter 4.2. | Composition test under `AllRemaining`: Static kinds covered by StaticSeeds, migration kinds by MigrationDependencies, remainder by Bootstrap. No overlap. | `src/Projection.Targets.Data/BootstrapEmitter.fs` | ~400 | Partition holds for fixture catalog; `EmitError.OverlappingEmitterCoverage` fires when seeded artificially. |
| 9 | Promoted-lane integration test wiring | C# orchestration in `Projection.Pipeline` (chapter 3.1 substrate): emit → DacFx deploy SSDT DDL → enable CDC on every kind → run V2 data emit → re-deploy → assert zero CDC records. | Tier-3 integration test: 50-table generated catalog, full triumvirate, CDC enabled on all tables. | `Projection.Pipeline/CanaryDataLane.cs` (extend) | ~600 | Integration test green; the cutover-blocking property is verified end-to-end. |

Approximate total: ~3,350 LOC product + ~5,500 LOC test (chapter 3 ratio target ~1:1.7, per `VISION_REVIEW.md` Appendix D §D.5).

---

## §8 Test strategy

### Tier-1 pure properties (no Docker)

- **T1 — same Catalog × Profile × Policy → byte-identical T-SQL.** Properties on `StaticSeedsEmitter.emit`, `MigrationDependenciesEmitter.emit`, `BootstrapEmitter.emit`, and the composer.
- **EmissionPolicy DU exhaustiveness.** Property test that perturbs `DataComposition` and asserts the composer's three branches fire as named.
- **Topological order respects FK dependency graph.** For every generated catalog, `(parent, child)` FK ⇒ `position parent < position child` in the composer's render.
- **CDC predicate shape.** Property: `forall column. predicate column = "(Target.[c] <> Source.[c] OR (Target.[c] IS NULL AND Source.[c] IS NOT NULL) OR (Target.[c] IS NOT NULL AND Source.[c] IS NULL))"`.
- **A18 amended.** Compile-time: emitter signatures cannot accept `Policy`. Verified by absence of `Policy` from any emit signature.
- **A33.** `DataEmissionComposer.compose` consumes `TopologicalOrder`, never `DeterministicOrder`. Type-level.
- **A34.** Passes that don't consume `Profile.CdcAwareness` produce identical output for `Profile.empty` and any populated CDC awareness — extends the existing A34 property.

### Tier-2 container-pooled deploy (testcontainers)

- **`idempotentRedeploy` × CDC.** Deploy → enable CDC via `sys.sp_cdc_enable_table` on every fixture kind → redeploy same artifact → assert `cdc.fn_cdc_get_all_changes_<capture_instance>(LSN_min, LSN_max, 'all')` returns empty for every capture instance. ~30 cases × generated catalogs.
- **Round-trip data.** Deploy → read back via the chapter 3.1 read-side adapter → row-by-row equality on `(KindKey, Identifier, Values)`.
- **Two-phase cycle.** Cyclic catalog deploys with FKs respected; Phase 2 UPDATEs populate the deferred columns.

### Tier-3 integration

- **50-table generated catalog with CDC on every table.** Full triumvirate against the full surface; redeploy-zero-CDC assertion holds for every kind.
- **Real four-environment Profile/Policy pairs.** Per-environment `CdcAwareness` differs; emit produces three distinct artifact shapes; redeploy on each is CDC-zero relative to that environment's tracked subset.

---

## §9 Promoted-lane integration test

The Azure DevOps pipeline step that proves V2's SSDT + data emission against a real CDC-enabled SQL Server.

**Harness.** `Projection.Pipeline.PromotedLaneCanary` (C#, chapter 3.1 substrate). Steps:

1. Generate a 50-table catalog from `Projection.Tests.Fixtures.GeneratedCatalog`.
2. Run V2 against the catalog → `(SsdtDirectory, DataInsertScript)`.
3. Spin up a testcontainers SQL Server (version-pinned to production per `DECISIONS.md` line 4354).
4. Deploy SSDT via DacFx `DacServices.Deploy` (chapter 4.1.A).
5. For every deployed kind, execute `sys.sp_cdc_enable_db`; for every table, `sys.sp_cdc_enable_table @source_schema, @source_name, @role_name = NULL`.
6. Capture the current LSN: `DECLARE @start_lsn = sys.fn_cdc_get_max_lsn();`.
7. Apply the `DataInsertScript` (the rendered T-SQL) — the **first** data emission. Each `Phase1Merges` row inserts; CDC capture-process emits "Insert" change records; this is expected.
8. Re-apply the **same** `DataInsertScript` — the **redeploy**.
9. Capture the post-redeploy LSN: `DECLARE @end_lsn = sys.fn_cdc_get_max_lsn();`.
10. For every capture instance, query `cdc.fn_cdc_get_all_changes_<capture_instance>(@start_lsn, @end_lsn, 'all')` filtering to `__$start_lsn > @start_lsn` (i.e., changes from the redeploy only).
11. **Assert: every capture instance returns zero rows.** Failure halts publication and surfaces the offending kind via the canary's `Diagnostics<'a>` writer (chapter 4.3).

**Failure-mode handling.** If the assertion fails, the canary records:
- The kind whose redeploy fired UPDATE.
- The row identifier (from `__$update_mask` and the CDC change row's PK).
- The MERGE statement V2 emitted.
The output points the operator at the column whose change-detection predicate is incorrect (typically a NULL-handling edge case on a column type the predicate-shape generator didn't cover).

---

## §10 Risks

- **CDC capture-instance management.** V2 does not enable CDC; the cutover team does. V2 must respect the existing capture-instance configuration. Risk: if the cutover team enables CDC with a non-default `@role_name` or `@filegroup_name`, V2's discovery must surface the capture instance's existing config rather than re-derive it. Mitigation: `CdcAwareness.CdcInstance : Map<SsKey, string>` carries the capture-instance name as discovered, never as inferred. The integration test parameterizes the capture-instance name on what `cdc.change_tables` returns.
- **Self-referencing FKs.** Common in `employee.manager_id → employee`, `category.parent_category_id → category`. The two-phase pattern always solves these. Risk surfaces only if the FK column is `NOT NULL` and self-referential — mathematically unsatisfiable. Mitigation: the chapter 2 cycle-resolution discipline marks the column nullable in the catalog, or the integration test fails fast with `EmitError.UnresolvableCycle`.
- **Cycle resolution at the data level.** Chapter 2's `CycleResolution` operates on the schema; data-level cycles (rows whose mandatory FKs reference each other transitively) are a separate concern. Mitigation: V2 shares chapter 2's cycle topology, but the data-level case where an SCC contains rows mutually referencing each other through `NOT NULL` FKs is rejected at emission time with a structural error. The migration team resolves these by either deferring one FK to nullable or by ordering legacy data so the cycle is broken.
- **Migration data quality.** Legacy rows from `MigrationDependencyContext` may violate V2's tightening rules (e.g., a row with `NULL` in a column V2's `NullabilityPass` decided to make NOT NULL). The opportunity stream surfaces these. Emitter graceful degradation: emit the rows as-is (T1 demands determinism); the deployment step fails with a constraint violation surfaced as a `Diagnostics` finding; the operator either relaxes the tightening intervention or fixes the legacy data. V2 does not silently mask the divergence.
- **Trunk parity divergence.** V2's CDC-aware MERGE differs from V1's MERGE on tracked tables. Differential tests against V1's output will surface a divergence. Mitigation: the divergence is named and intentional — record it in `ADMIRE.md` with the V2-growth template (per `DECISIONS.md` admire-spectrum entry); the differential test classifies the divergence as expected and asserts only on non-CDC tables.
- **Capture-instance latency.** SQL Server's CDC capture-process polls the transaction log on a configurable interval (default 5 seconds). The integration test must wait for the capture process to drain after the first deploy before issuing the redeploy, or the LSN window will conflate first-deploy and redeploy events. Mitigation: explicit `WAITFOR` plus `sys.sp_cdc_get_max_lsn()` polling in the harness step 7 → 8.
- **EmissionPolicy naming collision** (§3.1). Resolved by promotion of the existing record; risk is naming friction during the chapter open. Mitigation: slice 1's PR review carries the rename rationale.

---

## §11 Dependencies

- **`ArtifactByKind<'element>` type refactor.** Per `VISION_REVIEW.md` §R7 and Appendix H §7. The current emitters return `string`; T11 structural encoding requires the per-kind shape. The refactor is incremental over chapters 4–5; chapter 4.1.B can land its emitters with `Result<DataInsertScript ArtifactByKind, EmitError>` even if `RawTextEmitter` and `JsonEmitter` are still on the `Catalog -> string` shape (they migrate when the Appendix H §7 phase 2 lands).
- **Chapter 3.1 read-side adapter.** Required for `CdcAwareness` discovery (slice 2). Until 3.1 lands, slice 2 ships with a hand-coded fixture `CdcAwareness` value and the integration test (slice 9) is `Skip`-stubbed. Per the `Skip = "..."` discipline (CLAUDE.md programming-style §Tests).
- **Chapter 4.2 user FK reflow.** Provides `UserRemapContext` consumed by `BootstrapEmitter`. Until 4.2 lands, BootstrapEmitter ships with `UserRemapContext = Map.empty` (pass-through; no rewrites). Per `AXIOMS.md` A32: the discovery pass and the consumer Π are sibling concerns; the consumer can land first with a stub context.
- **Chapter 4.1.A SSDT DDL emitter.** Sibling slice; provides the DDL artifact the integration test deploys before issuing the data emit. Until 4.1.A lands, the integration test (slice 9) deploys via V1's emitted SSDT and runs V2's data emit against it — the V2-augmented mode of `VISION.md`'s fallback ladder.
- **`Projection.Pipeline` C# project.** Owns the canary orchestration. Already named in chapter 3.1 (per `DECISIONS.md` line 4326). Slice 9 extends; does not create.

---

## §12 Files inventory

**Created.**
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.Data/Projection.Targets.Data.fsproj`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.Data/DataInsertScript.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.Data/DataEmissionComposer.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.Data/StaticSeedsEmitter.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.Data/MigrationDependenciesEmitter.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.Data/BootstrapEmitter.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Adapters.Migration/Projection.Adapters.Migration.fsproj`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Adapters.Migration/DependencyReader.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Adapters.Sql.ReadSide/CdcDiscovery.fs` (within the chapter 3.1 project)
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/EmissionPolicyDataCompositionTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/StaticSeedsEmitterTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/MigrationDependenciesEmitterTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/BootstrapEmitterTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/DataEmissionComposerTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/CdcAwarenessTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/IdempotentRedeployCdcTests.fs` (tier-2)
- A C# slice in `Projection.Pipeline/PromotedLaneDataCanary.cs`

**Modified.**
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Policy.fs` — add `DataComposition` DU; extend `EmissionPolicy` record; module helpers.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Profile.fs` — add `CdcAwareness` type; extend `Profile` record; extend `Profile.empty` and `Profile.isEmpty`.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/AXIOMS.md` — A18 amendment update (no rule change; emitter list extends to data triumvirate); A33 reference unchanged; new A34 amendment for `CdcAwareness` orthogonality.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/DECISIONS.md` — append entries for: the `EmissionPolicy` record promotion vs. DU rename decision; `CdcAwareness` placement on Profile; the change-detection-predicate shape; the per-kind dispatch contract.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/ADMIRE.md` — entries for `StaticSeedsEmitter` (V1-migration mode; admire `Osm.Emission/Seeds/StaticSeedSqlBuilder.cs`), `MigrationDependenciesEmitter` (hybrid mode; V1 migration intake patterns + V2 algebraic emitter), `BootstrapEmitter` (V2-growth — V1 has no analog), `CdcAwareness` (V2-growth — V1 has no CDC-aware MERGE).
- `/home/user/outsystems-ddl-exporter/sidecar/projection/Projection.sln` — add three new projects (Data target, Migration adapter; ReadSide CDC discovery file is in the existing 3.1 project).
- `/home/user/outsystems-ddl-exporter/sidecar/projection/CLAUDE.md` — load-bearing-commitments section: add CDC-aware MERGE per-kind dispatch as a structural commitment.

---

## Critical Files for Implementation

- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Policy.fs
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Profile.fs
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Catalog.fs
- /home/user/outsystems-ddl-exporter/src/Osm.Emission/Seeds/StaticSeedSqlBuilder.cs
- /home/user/outsystems-ddl-exporter/src/Osm.Emission/PhasedDynamicEntityInsertGenerator.cs
