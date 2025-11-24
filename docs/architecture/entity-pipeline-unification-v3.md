# Entity Data Pipeline Unification: The Consolidated Architecture (V3)

**Status**: DRAFT - Execution Blueprint
**Date**: 2025-01-XX
**Purpose**: The definitive architectural convergence of the OutSystems Entity Pipeline. Synthesizes the ontological vision (V1) and implementation mechanics (V2) with the "zenith" insights of discovery-over-invention.

---

## 🌌 PART I: THE ONTOLOGY (Discovery, Not Invention)

We are not designing a new system. We are legitimizing the architecture that **already exists** in `BuildSsdtBootstrapSnapshotStep` and recognizing it as the universal pattern for all entity operations.

### § 1. The Core Abstraction: Pipeline as Composition

The pipeline is not a "runner" that executes steps. The pipeline **IS** composition—a lawful chain of immutable state transformations.

**The Monadic Spine**:
The pipeline is a state-transition spine where the type system enforces ontology.
```csharp
// The compiler validates meaning, not just correctness
Result<StateN> = StepN.Execute(StateN-1)
```

**The Invariant Flow**:
Every operation—whether extracting a model, building static seeds, or running a full export—traverses the same invariant stages. Variations are merely **parameterizations**, not structural deviations.

### § 2. The Three Orthogonal Dimensions

The universe of use cases is defined by a 3D coordinate space. Any specific verb (`extract-model`, `build-ssdt`, `uat-users`) is simply a point in this space.

#### Dimension 1: SCOPE (Selection)
**"Which entities participate?"**
- **Primitive**: `EntitySelector`
- **Coordinates**:
  - `AllEntities` (Bootstrap, Full-Export)
  - `StaticOnly` (Legacy StaticSeeds)
  - `SingleModule` (Dev workflow)
  - `FilteredSubset` (e.g., ServiceCenter::User)

#### Dimension 2: EMISSION (Artifacts)
**"What do we produce?"**
- **Primitive**: `EmissionStrategy`
- **Coordinates**:
  - `Schema` (DDL, .sqlproj)
  - `Data` (DML: INSERT/MERGE)
  - `Diagnostics` (Reports, logs)
  - `Combined` (All of the above)

#### Dimension 3: INSERTION (Application)
**"How does it touch the database?"**
- **Primitive**: `InsertionStrategy`
- **Coordinates**:
  - `SchemaOnly` (CREATE TABLE)
  - `Insert` (Append-only, Bootstrap)
  - `Merge` (Upsert, StaticSeeds)
  - `None` (Export/Verify only)

---

## 🏗️ PART II: THE STAGE ARCHITECTURE & TYPE-STATE LAW

The pipeline consists of 7 invariant stages. The "Skip Pattern" is not a hack; it is a first-class ontological concept handled by the type system.

### The Invariant Chain

1.  **Stage 0: EXTRACT** (Model Acquisition)
    - *Input*: Connection / Cache
    - *Output*: `OsmModel` (Metadata)
2.  **Stage 1: SELECT** (Scope Definition)
    - *Input*: `EntitySelector`
    - *Output*: `SelectedEntities`
3.  **Stage 2: FETCH** (World State Capture)
    - *Input*: `SelectedEntities`
    - *Output*: `DatabaseSnapshot` (Metadata + Stats + Data)
4.  **Stage 3: TRANSFORM** (Discovery & Logic)
    - *Input*: `DatabaseSnapshot`
    - *Output*: `TransformedEntities`, `TransformationContext`
5.  **Stage 4: ORDER** (Topological Sort)
    - *Input*: `TransformedEntities`
    - *Output*: `TopologicalOrder` (Global)
6.  **Stage 5: EMIT** (Artifact Generation)
    - *Input*: `TopologicalOrder`, `EmissionStrategy`
    - *Output*: `Artifacts` (SQL, Reports)
7.  **Stage 6: APPLY** (Execution)
    - *Input*: `Artifacts`, `InsertionStrategy`
    - *Output*: `DeploymentResult`

### The Skip Pattern (Lawful Partial Participation)

Not all verbs touch all stages. We model this via **Typed Phase Algebra**.

**The Constraint**: A stage can be skipped if and only if its output is not required by subsequent mandatory stages.

- **Schema Emission**: Skips Stage 4 (Order).
  - *Why?* DDL constraints are declarative; emission order is irrelevant to correctness (though deterministic order is preserved for humans).
- **Extract-Only**: Skips Stage 3, 4, 5(Data), 6.
  - *Why?* Goal is Stage 0 artifact.

**Implementation**: The `PipelineContext` carries "Evidence" (Option types).
```csharp
class PipelineContext {
    Option<DatabaseSnapshot> Snapshot;
    Option<TopologicalOrder> Order;
    // ...
}
```
*Compiler Enforcement*: Data emission requires `Order.HasValue`. Schema emission does not.

---

## ⚡ PART III: CRITICAL INVARIANTS (Protected Citizens)

These are the non-negotiable laws of the unified architecture.

### 1. Global Topological Sort is Absolute for Data
**The Law**: Data emission (DML) **ALWAYS** requires a global topological sort across ALL selected entities.
- **Why**: Foreign Keys do not respect "Static" vs "Regular" categories.
- **Correction**: `StaticSeeds` currently sorts only static entities (WRONG). It must sort globally, then filter for emission.
- **Proof**: `Bootstrap` already does this correctly.

### 2. Schema vs. Data Ordering
**The Law**:
- **Data (DML)**: Must be topologically ordered (FK correctness).
- **Schema (DDL)**: Does not require topological order (Declarative).
- **Invariant**: Schema emission must still be **deterministic** (e.g., Alphabetical or Fetch Order) to ensure reproducible diffs.

### 3. DatabaseSnapshot as Truth Substrate
**The Law**: All data and metadata come from a single, coordinated `DatabaseSnapshot`.
- **Eliminates**: The "Triple-Fetch" problem (OSSYS queried 2-3 times).
- **Enables**: Consistent world-view across profiling, transformation, and emission.
- **Mechanism**: Fetch once, cache (optionally), project views for different consumers.

### 4. EntityFilters Wiring
**The Law**: Selection is applied at the **Source**.
- **Correction**: Currently `EntityFilters` exists in config but is ignored by Fetch/Profile queries.
- **Implication**: Wire it early. "Supplemental Entities" (the workaround) becomes obsolete.

---

## 🛠️ PART IV: THE TRANSFORM PRAXIS (Stage 3)

Stage 3 is the "Danger Zone" of business logic. V3 introduces a formal taxonomy and discovery praxis to manage this risk.

### Taxonomy: Discovery vs. Application

We split transforms into two distinct kinds:

1.  **Discovery Transforms** (Stage 3)
    - *Action*: Analyze state, produce knowledge/policy.
    - *Example*: `UatUsersDiscovery` (Find orphans, validate map), `NullabilityEvaluator` (Recommend NOT NULL).
    - *Output*: `TransformationContext` (e.g., a mapping dictionary, a policy decision).

2.  **Application Transforms** (Stage 5/6)
    - *Action*: Enact the knowledge during emission/insertion.
    - *Example*: `UatUsersApply` (Replace ID `100` with `200` during INSERT generation).
    - *Mechanism*: Strategies consume `TransformationContext`.

### The Excavation Praxis

We do not just "refactor" transforms; we **excavate and register** them.

1.  **Systematic Trace**: Grep/Find Usages for all logic altering entity state.
2.  **The Transform Registry**: A formal catalog of all active transforms with explicit ordering dependencies.
    - *Format*: `[Name] -> [DependsOn] -> [RunBefore]`
3.  **Coverage Proof**: Tests that fail if a registered transform is missing or reordered.
4.  **Continuous Discovery**: Treat Stage 3 as a controlled lane for adding new logic.

---

## 📡 PART V: DIAGNOSTICS AS ORTHOGONAL PLANES

Diagnostics is not a single output; it is three parallel channels tappable at *every* stage.

1.  **Observability (Trust)**
    - *Audience*: The Operator (interactive).
    - *Artifact*: Spectre.Console progress, live warnings, counts.
    - *Goal*: "Is it working? Is it stuck?"

2.  **Auditability (Receipts)**
    - *Audience*: The Auditor / CI Process.
    - *Artifact*: Durable files (`profiling-report.csv`, `validation-results.log`, `manifest.json`).
    - *Goal*: "Prove correctness. Why was this decision made?"

3.  **Debuggability (Root Cause)**
    - *Audience*: The Developer.
    - *Artifact*: Verbose logs, stack traces.
    - *Goal*: "Why did it crash? What specifically happened?"

**EmissionStrategy** orchestrates the materialization of these channels.

---

## 🚀 PART VI: COMPLETION VECTORS (The Runway)

The execution sequence is ordered for correctness, risk management, and value delivery.

### 1. Vector 1: EntityFilters Wiring (The Scope Spine)
- **Task**: Wire `EntityFilters` config into `SqlModelExtractionService`, `SqlDataProfiler`, and `Validation`.
- **Value**: Enables precise selection. Makes "Supplemental" workaround obsolete.
- **Risk**: Low.

### 2. Vector 7: DatabaseSnapshot (The Truth Substrate)
- **Task**: Implement `DatabaseSnapshot` primitive. Consolidate metadata, stats, and data fetching.
- **Value**: Solves Triple-Fetch. Provides stable input for all subsequent vectors.
- **Risk**: Medium (Changes data loading patterns).

### 3. Vector 3: Global Topological Sort (The Correctness Invariant)
- **Task**: Enforce global sort for ALL data pipelines (including StaticSeeds).
- **Method**: Use `Bootstrap`'s sorting logic. Filter *after* sort for specific emission scopes.
- **Value**: Fixes cross-category FK violations.
- **Risk**: Medium (Changes output order for StaticSeeds).

### 4. Vector 2: EmissionStrategy (The Output Layer)
- **Task**: Unify Schema (`.sqlproj`) and Data (`.sql`) emission under one strategy.
- **Detail**: Fix broken per-module data emission (add `.sqlproj` ordering or deprecate).
- **Value**: Coherent output control.
- **Risk**: Medium.

### 5. Vector 4: InsertionStrategy (The Parameterization)
- **Task**: Extract `Insert` (Bootstrap) and `Merge` (StaticSeeds) into strategies.
- **Value**: Unifies the final execution step. Allows mixing strategies (e.g., Bootstrap with Merge?).
- **Risk**: Low.

### 6. Vector 5: Extract-Model Integration (The Verb)
- **Task**: Make `extract-model` Stage 0 of the pipeline. Support caching/offline mode.
- **Value**: UX coherence.
- **Risk**: Low.

### 7. Stage 3: The Transform Excavation (Continuous Lane)
- **Task**: Apply the "Discovery vs Application" taxonomy. Build the Registry.
- **Method**: Systematic grep. Test coverage for ordering.
- **Value**: Safety for business logic.
- **Risk**: High (Unknown unknowns).

### 8. Vector 0: Domain-Neutral Renaming (Cleanup)
- **Task**: Rename `StaticEntityTableData` -> `EntityTableData`, etc.
- **Value**: Code reflects reality.
- **Risk**: Low (Refactoring tool driven).

---

## 🎯 PART VII: THE UNIFIED INVOCATION (The Zenith)

When V3 is complete, the `full-export` verb—our most complex case—becomes a clean composition:

```csharp
await EntityPipeline.ExecuteAsync(
    // Stage 0: Extract/Cache
    source: DatabaseSnapshot.From(connectionString, cache: true),

    // Stage 1: Scope
    select: EntitySelector.AllModules(),

    // Stage 3: Transform (Discovery)
    transforms: Registry.Get(
        UatUsersDiscovery(inventories), // Builds context
        NullabilityEvaluator(policy)    // Recommends constraints
    ),

    // Stage 4: Order (Global)
    order: TopologicalSort.Global(),

    // Stage 5: Emit (Combined)
    emit: EmissionStrategy.Combined(
        SchemaPerModule("Modules/"),
        DataMonolithic("Bootstrap/Bootstrap.sql",
             // Stage 5 Apply: UAT Transformation happens here via context
             transform: UatUsersApply(TransformMode.PreTransformedInsert)),
        Diagnostics("Diagnostics/")
    ),

    // Stage 6: Apply (Insertion Semantics)
    insert: InsertionStrategy.Insert(NonDestructive)
);
```

This is not a new system. It is the system **as it was always meant to be**.
