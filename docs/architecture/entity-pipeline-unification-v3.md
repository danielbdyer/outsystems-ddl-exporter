# Entity Data Pipeline Unification: The Consolidated Architecture (V3)

**Status**: DEFINITIVE - Architectural Manifesto
**Date**: 2025-01-XX
**Scope**: The complete ontological and mechanical definition of the Unified Entity Pipeline.
**Purpose**: To crystallize the "accidental architecture" into an explicit, compiler-enforced law. This is not a proposal; it is the recognition of the system's emergent truth.

---

# 🏛️ PREAMBLE: THE ZENITH INSIGHT

We are not building "V3" to fix V1 and V2. We are building V3 because the codebase has already evolved a superior architecture in secret, hidden within `BuildSsdtBootstrapSnapshotStep`.

**The Revelation**:
The `BuildSsdtBootstrapSnapshotStep` (src/Osm.Pipeline/Orchestration/BuildSsdtBootstrapSnapshotStep.cs) is not merely a "bootstrap" tool. It is the **Unified Pipeline** in disguise.
1.  **Universal Scope**: It combines static, regular, and supplemental entities into a single set (lines 50-60).
2.  **Global Order**: It executes a global topological sort across *all* categories, respecting true FK semantics (lines 105-110).
3.  **Unified Emission**: It uses `StaticSeedSqlBuilder` (shared with StaticSeeds) to generate SQL (line 297).
4.  **Correctness**: It proves that cross-category dependencies (Regular → Static) must be respected.

**The Mandate**:
We stop inventing new pipelines. We **excavate** this pattern, strip it of its hardcoded parameters (Scope=`All`, Emission=`Monolithic`, Insertion=`Insert`), and elevate it to be the singular spine of the system.

---

# 🧬 PART I: THE ONTOLOGY (Composition as Law)

The pipeline is not a "runner" or a "processor." The pipeline **IS** composition. It is a lawful chain of immutable state transformations where the type system enforces ontology.

## § 1.1 The Monadic Spine

We adopt a functional core within our Domain-Driven structure. The pipeline is modeled as a series of `Bind` operations on a `Result<T>` monad.

**The Interface**:
```csharp
// src/Osm.Pipeline/Orchestration/IBuildSsdtStep.cs
public interface IBuildSsdtStep<in TInput, TNextState>
{
    Task<Result<TNextState>> ExecuteAsync(TInput state, CancellationToken token);
}
```

**The Composition**:
```csharp
// The V3 Spine
public async Task<Result<DeploymentResult>> Execute(PipelineContext initial)
{
    return await initial
        .Bind(ExtractModel)      // State 0: Context -> ExtractionCompleted
        .Bind(SelectEntities)    // State 1: Extraction -> SelectionCompleted
        .Bind(FetchSnapshot)     // State 2: Selection -> FetchCompleted
        .Bind(Transform)         // State 3: Fetch -> TransformCompleted
        .Bind(Order)             // State 4: Transform -> OrderCompleted
        .Bind(Emit)              // State 5: Order -> EmissionCompleted
        .Bind(Apply);            // State 6: Emission -> ApplicationCompleted
}
```

**Why this matters**:
- **Immutability**: Each step produces a *new* state object. `FetchCompleted` cannot be mutated into `OrderCompleted`; it must be *transformed*.
- **Linearity**: You cannot Emit (Stage 5) if you have not Ordered (Stage 4). The compiler forbids it because `Emit` demands `OrderCompleted` as input.
- **Short-Circuiting**: If `Fetch` fails, `Transform` never runs. The `Result<T>` monad handles the error path invisible to the happy path.

## § 1.2 The Three Orthogonal Dimensions

The universe of use cases is defined by a 3D coordinate space. Any specific verb is a point in this space.

### Dimension 1: SCOPE (Selection)
**"Which entities participate?"**
- **Primitive**: `EntitySelector`
- **Reification**: `ModuleFilterOptions` + `EntityFilters`
- **Coordinates**:
    - `AllEntities`: The union of all modules (Bootstrap, Full-Export).
    - `StaticOnly`: Entities where `DataKind=StaticEntity` (Legacy StaticSeeds).
    - `SingleModule`: All entities in "ModuleA".
    - `FilteredSubset`: Specific entities (e.g., `ServiceCenter::User`).

### Dimension 2: EMISSION (Artifacts)
**"What do we produce?"**
- **Primitive**: `EmissionStrategy`
- **Coordinates**:
    - `Schema`: DDL (`CREATE TABLE`), organized by module, `.sqlproj`.
    - `Data`: DML (`INSERT`/`MERGE`), topologically ordered.
    - `Diagnostics`: CSV reports, logs, manifests.
    - `Combined`: Any combination of the above.

### Dimension 3: INSERTION (Semantics)
**"How does it apply?"**
- **Primitive**: `InsertionStrategy`
- **Coordinates**:
    - `SchemaOnly`: DDL application only.
    - `Insert`: Append-only (Bootstrap).
    - `Merge`: Upsert (StaticSeeds).
    - `None`: Export/Verify only.

---

# 📐 PART II: TYPED PHASE ALGEBRA (Lawful Skipping)

V2 introduced the linear chain. V3 introduces **Lawful Partial Participation**.

Not every verb touches every stage.
- `extract-model` stops at Stage 0.
- `build-ssdt` (schema) skips Stage 4 (Order).
- `uat-users` (standalone) starts at Stage 3 (Transform).

How do we model this without `null` checks? **Typed Phase Algebra**.

## § 2.1 The Evidence Pattern

The Pipeline State is not a monolith. It is an accumulator of **Evidence**.

```csharp
public record PipelineState(
    // Mandatory: The foundation
    OsmModel Model,

    // Evidence: Optional proofs of work
    Evidence<DatabaseSnapshot>? Snapshot,
    Evidence<TopologicalOrder>? Order,
    Evidence<TransformRegistry>? Transforms
);

public class Evidence<T>
{
    public T Value { get; }
    public DateTimeTimestamp Timestamp { get; }
    public Checksum Checksum { get; }
}
```

## § 2.2 The Skipping Laws

A stage can be skipped *if and only if* its output Evidence is not required by a subsequent mandatory stage.

**Case Study: Schema Emission**
- **Requires**: `Evidence<OsmModel>` (to generate CREATE TABLE).
- **Does NOT Require**: `Evidence<TopologicalOrder>` (DDL is declarative).
- **Outcome**: The `Order` stage can be skipped. The compiler allows `SchemaEmission` to consume `FetchCompleted` directly (or even `ExtractionCompleted`).

**Case Study: Data Emission**
- **Requires**: `Evidence<TopologicalOrder>` (to generate FK-safe INSERTs).
- **Outcome**: The `Order` stage is **mandatory**. The compiler *forbids* `DataEmission` from consuming a state that lacks `Evidence<TopologicalOrder>`.

---

# 🛡️ PART III: PROTECTED CITIZENS (The Invariants)

These are the non-negotiable laws. If these move, the architecture fails.

## § 3.1 The Global Topological Sort
**The Law**: For Data Emission, the sort is **ALWAYS GLOBAL**.

**The Receipt**:
- **Failure**: `StaticSeeds` currently sorts `staticEntities` only (`BuildSsdtStaticSeedStep.cs:82`). This misses dependencies where `Regular -> Static`.
- **Success**: `Bootstrap` sorts `allEntities` (`BuildSsdtBootstrapSnapshotStep.cs:105`).
- **V3 Mandate**: We *always* sort the global graph. For `StaticSeeds`, we sort everything, *then* filter the sorted list to keep only static entities. This preserves the relative order mandated by the global graph.

## § 3.2 The DatabaseSnapshot (Truth Substrate)
**The Law**: One Fetch. One Truth.

**The Problem (Triple-Fetch)**:
1. `Extract-Model` queries `OSSYS_ENTITY`.
2. `Profile` queries `OSSYS_ENTITY` + `sys.tables`.
3. `Export` queries `OSUSR_Data`.
Result: Race conditions, performance waste, disconnected state.

**The V3 Solution**:
```csharp
public class DatabaseSnapshot
{
    public OsmModel Model { get; }           // Metadata
    public EntityStatistics Stats { get; }   // Profiling
    public EntityDataSet Data { get; }       // Rows
}
```
We fetch this *once* (Stage 2). All subsequent stages (Transform, Order, Emit) read from this immutable snapshot.

## § 3.3 EntityFilters Wiring (Scope at Source)
**The Law**: Selection happens at the Query, not the Filter.

**The Receipt**:
- **Current**: `SqlModelExtractionService` fetches all modules (`SELECT * FROM OSSYS_ESPACE`).
- **V3 Mandate**: Inject `EntitySelector` into the SQL generation.
- **Benefit**: Performance (don't fetch metadata for 500 modules if you only want 1).
- **Obsolescence**: The "Supplemental Entity" workaround (loading `ossys_User.json` manually) dies. We just include `ServiceCenter::User` in the Selector.

---

# 🏭 PART IV: THE STAGE ARCHITECTURE (Detailed)

The 7 stages of the Unified Pipeline.

## Stage 0: EXTRACT (Model Acquisition)
**Goal**: Obtain the Schema (`OsmModel`).
- **Input**: Connection String OR Cache Path.
- **Operation**:
  - Query `OSSYS` tables (Entity, Attribute, Relationship, Index).
  - OR deserialize `.cache/model.json`.
- **Output**: `Evidence<OsmModel>`.

## Stage 1: SELECT (Scope Definition)
**Goal**: Determine the working set.
- **Input**: `EntitySelector` (Config).
- **Operation**:
  - Apply Module filters (`Include/Exclude`).
  - Apply Entity filters (`ServiceCenter: [User]`).
  - Apply Predicates (`Where(IsStatic)`).
- **Output**: `SelectedEntities` (Set<EntityKey>).

## Stage 2: FETCH (World State Capture)
**Goal**: Hydrate the Snapshot.
- **Input**: `SelectedEntities`.
- **Operation**:
  - **Metadata**: (Already have from Stage 0).
  - **Stats**: Run `SELECT COUNT(*), COUNT(NULL)` on selected tables.
  - **Data**: Run `SELECT *` on selected tables (if Data Emission required).
- **Output**: `Evidence<DatabaseSnapshot>`.

## Stage 3: TRANSFORM (Discovery & Logic)
**Goal**: Apply Business Logic / Policies.
- **Input**: `DatabaseSnapshot`.
- **Operation**: Run registered **Discovery Transforms**.
  - `NullabilityEvaluator`: Check stats vs mandatory flag.
  - `UatUsersDiscovery`: Build ID mapping.
  - `TypeMappingPolicy`: Apply overrides.
- **Output**: `TransformationContext` (Policies, Decisions).

## Stage 4: ORDER (Topological Sort)
**Goal**: Establish Emission Order.
- **Input**: `DatabaseSnapshot` (Relationships).
- **Operation**:
  - Build Dependency Graph (Nodes=Entities, Edges=FKs).
  - Detect Cycles (SCCs).
  - Apply `CircularDependencyOptions` (Manual breaks).
  - Kahn's Algorithm.
- **Output**: `Evidence<TopologicalOrder>` (List<EntityKey>).

## Stage 5: EMIT (Artifact Generation)
**Goal**: Materialize Output.
- **Input**: `TopologicalOrder` + `EmissionStrategy` + `TransformationContext`.
- **Operation**:
  - **Schema**: Generate `CREATE TABLE`. (Skips Order).
  - **Data**: Generate `INSERT`/`MERGE`. (Uses Order).
    - Apply **Application Transforms** here (e.g., swap User IDs).
  - **Diagnostics**: Write reports.
- **Output**: `ArtifactManifest` (List of files).

## Stage 6: APPLY (Execution)
**Goal**: Touch the Database.
- **Input**: `ArtifactManifest` + `InsertionStrategy`.
- **Operation**:
  - `None`: Stop.
  - `SchemaOnly`: Run DDL.
  - `Insert`/`Merge`: Run DML scripts.
- **Output**: `DeploymentResult`.

---

# 🧪 PART V: THE TRANSFORM PRAXIS

Stage 3 is the "Danger Zone." We manage it with a strict Praxis.

## § 5.1 Taxonomy: Discovery vs. Application

We decouple *analysis* from *enforcement*.

| Feature | Discovery Transform (Stage 3) | Application Transform (Stage 5/6) |
| :--- | :--- | :--- |
| **Role** | The Analyst | The Enforcer |
| **Input** | Snapshot (Read-Only) | Context + Strategy |
| **Output** | `TransformationContext` | SQL / Artifacts |
| **Example** | `UatUsersDiscovery` (Find Orphans) | `UatUsersApply` (Remap IDs) |
| **Example** | `NullabilityEvaluator` (Suggest NOT NULL) | `TighteningEnforcer` (Emit NOT NULL) |

## § 5.2 The Transform Registry

No hidden logic. Every transform is registered.

```csharp
public class TransformRegistry
{
    public void Register<T>(T transform, TransformPhase phase, params Type[] dependencies);
}

// Usage
registry.Register(new UatUsersDiscovery(), Phase.Discovery, dependsOn: typeof(DatabaseSnapshot));
registry.Register(new NullabilityEvaluator(), Phase.Discovery, dependsOn: typeof(ProfileStats));
```

## § 5.3 The Excavation Method
We do not refactor Stage 3; we **excavate** it.
1.  **Identify**: Find hidden logic (e.g., `EntitySeedDeterminizer`).
2.  **Classify**: Is it Discovery or Application?
3.  **Lift**: Move code into a dedicated class implementing `IDiscoveryTransform` or `IApplicationTransform`.
4.  **Register**: Add to the Registry.
5.  **Test**: Assert that the Registry contains the transform and execution order is preserved.

**Known Fossils to Excavate**:
- `EntitySeedDeterminizer.Normalize`
- `Module name collision handling`
- `Supplemental physical->logical remapping`
- `TypeMappingPolicy`
- `NullabilityEvaluator`
- `StaticSeedForeignKeyPreflight`

---

# 📡 PART VI: DIAGNOSTICS (The Hyperplane)

Diagnostics is a hyperplane cutting through all stages. We model 3 orthogonal channels.

## Plane 1: Observability (Trust)
*   **Medium**: `Spectre.Console`.
*   **Content**: Live progress, "Alive" signals.
*   **Example**:
    ```
    [Stage 4] Ordering Entities...
       Nodes: 450
       Edges: 1200
       Cycles: 2 (Resolved)
    ```

## Plane 2: Auditability (Receipts)
*   **Medium**: `EmissionStrategy` Artifacts.
*   **Content**: Durable proof of correctness.
*   **Artifacts**:
    - `profiling-report.csv`: Row counts, nulls.
    - `validation-results.log`: Why we allowed a cycle.
    - `transform-manifest.json`: Which transforms ran and what they decided.

## Plane 3: Debuggability (Trace)
*   **Medium**: `ILogger` (Serilog).
*   **Content**: Stack traces, verbose logic flow.
*   **Example**: `[Debug] Cycle detected in SCC {User, Role}. Breaking edge User->Role based on Config.`

---

# 🚀 PART VII: THE EXECUTION RUNWAY (Vectors)

The implementation plan, strictly ordered by dependency and risk.

## Phase 1: Foundation (Scope & Truth)

### Vector 0: Domain-Neutral Renaming
*   **Goal**: Stop the semantic lie.
*   **Action**: Rename `StaticEntityTableData` → `EntityTableData`. Rename `StaticSeedSqlBuilder` → `EntitySqlBuilder`.
*   **Receipt**: `src/Osm.Emission/Seeds/StaticEntityTableData.cs`.

### Vector 1: EntityFilters Wiring
*   **Goal**: Precise Scope. Kill "Supplemental".
*   **Action**: Pass `EntitySelector` to `SqlModelExtractionService`. Update queries to filter by Module/Entity.
*   **Verification**: Extract *only* `ServiceCenter::User`.

### Vector 7: DatabaseSnapshot
*   **Goal**: Solve Triple-Fetch.
*   **Action**: Create `DatabaseSnapshot` class. Orchestrate single fetch in Stage 2.
*   **Dependency**: Vector 1 (need Selector to know what to fetch).

## Phase 2: The Spine (Order & Logic)

### Vector 3: Global Topo Sort
*   **Goal**: Correctness for Data Emission.
*   **Action**: Use `Bootstrap`'s global sort logic for *all* data pipelines.
*   **Fix**: `StaticSeeds` pipeline must sort Global, then Filter Static.
*   **Invariant**: `DataEmission` throws if `Order` is partial.

### Vector 6: Transform Excavation
*   **Goal**: Safe Business Logic.
*   **Action**: Implement `TransformRegistry`. Lift known transforms.
*   **Test**: `TransformRegistryTests` ensuring ordering.

## Phase 3: Unification (Emission)

### Vector 2: EmissionStrategy
*   **Goal**: Unified Output.
*   **Action**: Create `EmissionStrategy` abstract base. Implement `Schema`, `Data`, `Combined`.
*   **Fix**: Per-module Data emission is broken (no .sqlproj). Add .sqlproj generation for Data, or deprecate per-module Data.

### Vector 4: InsertionStrategy
*   **Goal**: Unified Application.
*   **Action**: Parameterize `Insert` (Bootstrap) vs `Merge` (StaticSeeds).

### Vector 5: Extract-Model Integration
*   **Goal**: UX Coherence.
*   **Action**: Wire `extract-model` as Stage 0.

---

# 💻 PART VIII: THE UNIFIED INVOCATION (Code)

The target state.

```csharp
// The V3 Definition
public class EntityPipeline
{
    public async Task<Result<DeploymentResult>> ExecuteAsync(PipelineOptions options)
    {
        // 1. Build Context (Scope + Transforms)
        var context = new PipelineContext()
            .WithScope(options.Selector)
            .WithTransforms(options.Registry);

        // 2. Execute Spine
        return await context
            // Stage 0: Truth
            .Bind(ctx => ExtractModel(ctx, options.Connection))

            // Stage 1: Select
            .Bind(SelectEntities)

            // Stage 2: Fetch (The Snapshot)
            .Bind(FetchSnapshot)

            // Stage 3: Transform (Discovery)
            .Bind(DiscoverTransforms)

            // Stage 4: Order (Global Invariant)
            .Bind(GlobalTopologicalSort)

            // Stage 5: Emit (Strategy Pattern)
            .Bind(ctx => EmitArtifacts(ctx, options.EmissionStrategy))

            // Stage 6: Apply (Strategy Pattern)
            .Bind(ctx => ApplyChanges(ctx, options.InsertionStrategy));
    }
}

// Usage: Full Export
var result = await pipeline.ExecuteAsync(new PipelineOptions {
    Selector = EntitySelector.AllModules(),
    EmissionStrategy = EmissionStrategy.Combined(
        Schema: new SchemaEmission("Modules/", Order.Deterministic),
        Data: new DataEmission("Bootstrap/", Order.Topological)
    ),
    InsertionStrategy = InsertionStrategy.Insert(NonDestructive),
    Registry = TransformRegistry.Default
});
```

---

# 📉 APPENDIX: DEPRECATIONS & MIGRATIONS

### Deprecations
*   **Supplemental Entity Loading**: Replaced by `EntitySelector.Include()`.
*   **DynamicData**: Replaced by `Bootstrap` (Unified Pipeline).
*   **StaticSeeds (Legacy Pipeline)**: Replaced by Unified Pipeline with `Scope=Static`, `Insertion=Merge`.

### Migrations
*   **Config**: Convert `ModuleFilterOptions` to `EntitySelector` configuration.
*   **Output**: `StaticSeeds` output remains byte-identical (verified by tests), but generation path changes.

---

**This is the architecture.** It is not an invention. It is the inevitable conclusion of the system's evolution.
