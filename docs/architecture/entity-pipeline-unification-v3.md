# Entity Data Pipeline Unification: The Consolidated Architecture (V3)

**Status**: DEFINITIVE - Architectural Manifesto
**Date**: 2025-01-XX
**Purpose**: To crystallize the "accidental architecture" into an explicit, compiler-enforced law. This document is not a proposal; it is a recognition of the system's emergent truth.

---

## 🏛️ PART I: THE REVELATION (Discovery Over Invention)

We are not building a "V3" to fix V1 and V2. We are building V3 because the code *already knows* what it wants to be, and we finally have the language to name it.

**The Zenith Insight**:
`BuildSsdtBootstrapSnapshotStep` is not just a "bootstrap" tool. It is the **Unified Pipeline** in disguise.
- It combines static and regular entities (Scope).
- It executes a global topological sort across all categories (Order).
- It uses `StaticSeedSqlBuilder` to generate MERGE/INSERT statements (Emission/Insertion).
- It enforces Foreign Key correctness ignoring artificial boundaries.

**The Mandate**:
We stop inventing "new" pipelines. We **excavate** this pattern, strip it of its hardcoded parameters, and elevate it to the system's singular spine.

---

## 🧬 PART II: THE ONTOLOGY (Composition as Law)

The core abstraction is not "The Pipeline." The core abstraction is **Composition**.

### § 1. The Monadic Spine
The pipeline is a sequence of immutable state transitions. We do not "run" steps; we **bind** them.

**The Pattern**: Functional composition synthesized with Domain-Driven Design.
```csharp
// The Spine: A Lawful Chain of State
public async Task<Result<DeploymentResult>> Execute(PipelineContext initial)
{
    return await initial
        .Bind(ExtractModel)      // State 0 -> 1
        .Bind(SelectEntities)    // State 1 -> 2
        .Bind(FetchSnapshot)     // State 2 -> 3
        .Bind(Transform)         // State 3 -> 4
        .Bind(Order)             // State 4 -> 5
        .Bind(Emit)              // State 5 -> 6
        .Bind(Apply);            // State 6 -> 7
}
```
**Why this matters**: The compiler validates *meaning*. You cannot Emit (Stage 5) if you have not Ordered (Stage 4). You cannot Order if you have not Fetched (Stage 2). The ontology is enforced by the type system, not by runtime checks.

### § 2. The 3D Coordinate Space
Every verb in the system—`extract-model`, `build-ssdt`, `full-export`, `uat-users`—is simply a coordinate in a three-dimensional parameter space. Special cases are ontological errors.

1.  **SCOPE (Selection)**
    *   **Primitive**: `EntitySelector`
    *   **Question**: "Who participates?"
    *   **Coordinates**: `AllEntities`, `StaticOnly`, `SingleModule`, `Filtered(ServiceCenter::User)`

2.  **EMISSION (Artifacts)**
    *   **Primitive**: `EmissionStrategy`
    *   **Question**: "What is produced?"
    *   **Coordinates**: `Schema(DDL)`, `Data(DML)`, `Diagnostics(Audit)`, `Combined`

3.  **INSERTION (Semantics)**
    *   **Primitive**: `InsertionStrategy`
    *   **Question**: "How does it apply?"
    *   **Coordinates**: `SchemaOnly`, `Insert(Append)`, `Merge(Upsert)`, `None(Verify)`

---

## 📐 PART III: TYPED PHASE ALGEBRA (Lawful Skipping)

V2 introduced the linear stage chain. V3 introduces **Lawful Partial Participation**.

Not every verb touches every stage. `extract-model` stops at Stage 0. `build-ssdt` (schema) skips Stage 4 (Order). How do we model this without `null` checks and runtime exceptions?

**The Solution: Typed Phase Algebra**.
The pipeline state is not a monolith; it is an accumulator of **Evidence**.

```csharp
// The Context accumulates evidence, allowing lawful skips
public record PipelineContext(
    Evidence<OsmModel> Model,              // Required for Stage 1
    Evidence<DatabaseSnapshot> Snapshot,   // Required for Stage 3
    Evidence<TopologicalOrder> Order,      // Required for Data Emission
    Evidence<ArtifactManifest> Artifacts   // Required for Application
);
```

**The Principled Skip**:
*   **Schema Emission** skips **Stage 4 (Order)**.
    *   *Ontological Reason*: DDL is declarative. `CREATE TABLE` order is semantically irrelevant to the database engine (FKs are constraints, not imperatives).
    *   *Constraint*: We still enforce **Deterministic Order** (e.g., Alphabetical) for diff-ability, but we do not pay the cost of Topological Sort.
*   **Data Emission** cannot skip **Stage 4**.
    *   *Ontological Reason*: DML is imperative. `INSERT` order is semantically vital.
    *   *Compiler Check*: `EmissionStrategy.Data` accepts `Evidence<TopologicalOrder>`. `EmissionStrategy.Schema` accepts `Evidence<OsmModel>`.

---

## 🛡️ PART IV: PROTECTED CITIZENS (Invariants)

These are the load-bearing walls. If these move, the building collapses.

### 1. The Global Topological Sort
**The Truth**: Foreign Keys do not care about your "Static" vs "Regular" distinction.
**The Law**: For any Data Emission, the Topological Sort must be **GLOBAL**.
*   *Correction*: `StaticSeeds` currently sorts only its own subset. This is incorrect. It must sort the *entire* graph (including Regular entities it references), and *then* filter the output.
*   *Mechanism*: `Order(AllSelected)` -> `Filter(IsStatic)` -> `Emit`.

### 2. The DatabaseSnapshot (Truth Substrate)
**The Truth**: The world state must be atomic.
**The Law**: One Fetch. One Snapshot.
*   *Correction*: Eliminate the triple-fetch (Metadata vs Stats vs Data).
*   *Mechanism*: A unified `DatabaseSnapshot` primitive that captures `OSSYS` metadata, `sys` profiling stats, and `OSUSR` data in a coordinated read. This snapshot is the *only* source of truth for Stages 3-6.

### 3. EntityFilters Wiring
**The Truth**: Selection happens at the source, not the sink.
**The Law**: `EntitySelector` drives the Fetch queries.
*   *Correction*: "Supplemental Entities" are a symptom of broken filtering. Once `EntitySelector` is wired into the `SqlModelExtractionService` and `SqlDataProfiler`, the concept of "Supplemental" evaporates. Service Center Users are just `EntitySelector.Include("ServiceCenter", "User")`.

---

## 🧪 PART V: THE TRANSFORM PRAXIS (Stage 3)

Stage 3 (Business Logic) is the "Highest Risk Seam." V2 named the risk; V3 solves it with a **Praxis**.

### The Taxonomy: Discovery vs. Application
We decouple *knowing* from *doing*.

1.  **Discovery Transforms (Stage 3)**
    *   *Role*: The Analyst.
    *   *Input*: `DatabaseSnapshot`.
    *   *Output*: `TransformationContext` (Policies, Maps, Decisions).
    *   *Examples*: `UatUsersDiscovery` (Building the ID map), `NullabilityEvaluator` (Deciding on NOT NULL).
    *   *Invariant*: **Read-Only / Additive**. Never mutates the Snapshot.

2.  **Application Transforms (Stages 5 & 6)**
    *   *Role*: The Enforcer.
    *   *Input*: `TransformationContext` + `EmissionStrategy`.
    *   *Output*: Modified SQL.
    *   *Examples*: `UatUsersApply` (Swapping IDs in INSERT values), `TypeMappingPolicy` (Changing `int` to `bigint` in DDL).

### The Transform Registry
We do not allow "hidden" logic. All transforms must be registered in a central **Transform Registry**.

*   **Registration**: Explicit dependency graph (`[UatUsersDiscovery] -> requires [DatabaseSnapshot]`).
*   **Coverage Proof**: A test suite that fails if the Registry does not match the execution plan.
*   **Excavation**: We do not refactor Stage 3; we **excavate** it. We grep, trace, and move logic into the Registry one by one.

---

## 📡 PART VI: DIAGNOSTICS (The 3 Planes)

"Diagnostics" is not a stage. It is a **Hyperplane** cutting through the entire pipeline. V3 disentangles the three orthogonal channels:

1.  **Observability (The Cockpit)**
    *   *Nature*: Ephemeral, Live, Human-Centric.
    *   *Mechanism*: `Spectre.Console` Tasks.
    *   *Contract*: "Tell me you are alive and how fast you are going."

2.  **Auditability (The Receipts)**
    *   *Nature*: Durable, Structured, Compliance-Centric.
    *   *Mechanism*: `EmissionStrategy.Diagnostics` (CSV, JSON).
    *   *Contract*: "Prove that the decision to tighten `User.Email` to `NOT NULL` was based on valid evidence."

3.  **Debuggability (The Trace)**
    *   *Nature*: Verbose, Technical, Developer-Centric.
    *   *Mechanism*: Structured Logging (`ILogger`).
    *   *Contract*: "If you crash, tell me exactly which Transform failed and why."

**The Synthesis**: A single event (e.g., "Cycle Detected") is routed to all three planes:
*   *Observability*: A yellow warning icon on the console.
*   *Auditability*: An entry in `cycles.txt` with the cycle path.
*   *Debuggability*: A log entry with the full stack trace of the Sorter.

---

## 🚀 PART VII: THE EXECUTION RUNWAY (Vectors)

This is the ordered sequence of operations to materialize V3.

### Phase 1: The Foundation (Scoping & Truth)
1.  **Vector 0: Domain-Neutral Renaming**. Rename `StaticEntityTableData` → `EntityTableData`. Stop the semantic lie.
2.  **Vector 1: EntityFilters Wiring**. Wire `EntitySelector` into Fetch/Profile. Deprecate "Supplemental" logic (it dies here).
3.  **Vector 7: DatabaseSnapshot**. Implement the unified fetch. Solve Triple-Fetch. Cache results.

### Phase 2: The Spine (Ordering & Logic)
4.  **Vector 3: Global Topo Sort**. Enforce the global sort invariant for all data paths. Fix `StaticSeeds` correctness.
5.  **Transform Excavation**. Build the `TransformRegistry`. Move `TypeMapping`, `Nullability`, and `UatUsers` into the Discovery/Application taxonomy.

### Phase 3: The Output (Emission & Unification)
6.  **Vector 2: EmissionStrategy**. Unify Schema (`.sqlproj`) and Data (`.sql`) emitters. Fix per-module data brokenness (or deprecate).
7.  **Vector 4: InsertionStrategy**. Parameterize `Insert` vs `Merge`.
8.  **Vector 5: Extract-Model Integration**. Make `extract-model` a lawful Stage 0 participant.

---

## 🎯 PART VIII: THE MANIFESTO (The Final Form)

When V3 is complete, we do not describe the system as "a collection of scripts." We describe it as:

> **The OutSystems Entity Pipeline is a composable, type-safe state machine that transforms a raw Database Snapshot into deployment artifacts, strictly enforcing topological correctness for data while allowing deterministic flexibility for schema.**

The code will look like this (and the compiler will ensure it):

```csharp
// The V3 Invocation: Clean, Composable, Correct
await EntityPipeline.ExecuteAsync(
    context: new PipelineContext()
        .WithScope(EntitySelector.AllModules())
        .WithTransforms(TransformRegistry.Default),

    // The "Zenith" of Composition
    strategy: new PipelineStrategy(
        // Stage 0: Truth
        fetch: FetchStrategy.UnifiedSnapshot(cache: true),

        // Stage 4: Law
        order: OrderStrategy.GlobalTopological(),

        // Stage 5: Artifacts
        emit: EmissionStrategy.Combined(
            Schema: new SchemaEmission("Modules/", Order.Deterministic),
            Data:   new DataEmission("Bootstrap/", Order.Topological),
            Audit:  new DiagnosticEmission("Reports/")
        ),

        // Stage 6: Action
        apply: InsertionStrategy.Insert(NonDestructive)
    )
);
```

This is the architecture that was always waiting to be found.
