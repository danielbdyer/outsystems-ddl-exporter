# Entity Data Pipeline Unification: Ontology & Vision

## Status: DRAFT - Conceptual Exploration
**Date**: 2025-01-XX
**Purpose**: Establish shared ontological understanding before implementation planning

---

## 🎯 The Core Insight: Accidental Architecture

The codebase has evolved **four separate implementations** of what is fundamentally **the same conceptual operation**:

> **"Given a set of entities with data, fetch their metadata, order them by dependencies, and emit SQL scripts for deployment"**

### Current Fragmented Implementations

1. **Static Seeds** (`BuildSsdtStaticSeedStep`)
   - Scope: Entities where `DataKind = 'staticEntity'`
   - Emission: Monolithic or per-module
   - Insertion: MERGE (upsert on each deployment)
   - Topological Sort: Only static entities (scoped subset)

2. **Bootstrap** (`BuildSsdtBootstrapSnapshotStep`)
   - Scope: All entities with data (static + regular)
   - Emission: One monolithic file
   - Insertion: INSERT (one-time load)
   - Topological Sort: Static + regular entities

3. **DynamicData** (`BuildSsdtDynamicInsertStep`) - **DEPRECATED**
   - Scope: All entities with data
   - Emission: Per-entity files
   - Insertion: INSERT
   - Topological Sort: All entities
   - **Status**: Redundant with Bootstrap, misnamed, to be removed

4. **Supplemental Entities** (`SupplementalEntityLoader`)
   - Scope: Individual entities (e.g., ServiceCenter::User)
   - Mechanism: Load from JSON files
   - **Problem**: Not integrated into topological sort
   - **Status**: Workaround for missing "select one entity from module" primitive

### The Invariant Structure (Hidden Pattern)

All four implementations follow this pipeline:

```
[Entity Selection]
    ↓
[Metadata + Data Fetch]
    ↓
[Topological Sort by FK Dependencies]
    ↓
[SQL Script Emission]
    ↓
[Output Files]
```

**The variants are the parameters** passed to each stage, NOT the structure itself.

---

## 📐 The Three Orthogonal Dimensions

The fundamental operation can be **parameterized** across three independent dimensions:

### **Dimension 1: SCOPE** (What entities to process?)

This is not a binary "static vs. regular" - it's a **selection predicate** over the entity model.

**Conceptual Options**:
- **All entities from modules** (`modules: ["ModuleA", "ModuleB"]`)
- **Filtered entities per module** (`ModuleA: all, ServiceCenter: [User only]`)
- **Entities matching predicate** (`entity.IsStatic == true`)
- **Entities with available data** (`hasData(entity)`)
- **Custom combinations** (`static entities + ServiceCenter::User`)

**Current Problem**:
- Static Seeds: Hardcoded to `entity.IsStatic`
- Bootstrap: Hardcoded to `allEntitiesWithData`
- Supplemental: Separate mechanism entirely

**Vision**:
- One unified **EntitySelector** that can express any scope
- No special cases for "supplemental" - just another way to select entities

---

### **Dimension 2: EMISSION STRATEGY** (How to organize output?)

How should the sorted entities be written to disk?

**Options**:
1. **Monolithic**: One file containing all entities
   - Example: `Bootstrap/AllEntitiesIncludingStatic.bootstrap.sql`
   - Use case: Simple deployment, guaranteed correct order

2. **Multiple Files with .sqlproj Ordering**: Emit many files, reference them in topological order
   - Example: `ModuleA/Table1.sql`, `ModuleA/Table2.sql`, `StaticSeeds.sqlproj` (references files in sorted order)
   - Use case: Granular version control while preserving dependencies
   - **How it works**: Files can be organized however (per-module, per-entity), but .sqlproj lists them in topological order
   - **Key insight**: Organization is arbitrary, ORDER is defined by .sqlproj

**Per-Module Emission is NOT Supported**:
- You cannot sort "within each module independently"
- Topological sort MUST span all entities (cross-module dependencies exist!)
- But you CAN organize files by module, as long as .sqlproj orders them correctly

**Current State**:
- Static Seeds: Supports monolithic or per-module (per-module is problematic!)
- Bootstrap: Monolithic only (correct)
- DynamicData: Per-entity or single file (redundant, to be deleted)

**Vision**:
- **Option A**: Monolithic (simple, guaranteed correct)
- **Option B**: Multiple files + .sqlproj (organizational flexibility with ordering guarantee)
- Emission strategy is **independent** of scope and sort
- Chosen **after** topological sort completes

---

### **Dimension 3: INSERTION STRATEGY** (How is data applied?)

How should the SQL scripts modify the target database?

**Options**:
1. **INSERT**:
   - Semantics: One-time data load (fails on duplicates)
   - Use case: Bootstrap, initial deployment
   - Performance: Fast for large datasets

2. **MERGE** (UPSERT):
   - Semantics: Insert or update based on primary key
   - Use case: Static seeds that refresh on each deployment
   - Performance: Slower than INSERT, but idempotent

3. **TRUNCATE + INSERT**:
   - Semantics: Clear table, then load
   - Use case: Complete data replacement
   - Performance: Fast, but destructive

**Parameters**:
- Batch size (e.g., 1000 rows per batch)
- Merge key (usually primary key)
- Conflict resolution strategy

**Current Problem**:
- Static Seeds: Hardcoded to MERGE
- Bootstrap: Hardcoded to INSERT
- No flexibility to change strategies

**Vision**:
- Insertion strategy is **configurable**
- Different use cases can use different strategies
- Same entity set can be emitted with INSERT (for bootstrap) OR MERGE (for refresh)

---

## 🧩 The Extract-Model Integration Problem

### Current State: Two-Step Manual Process

Users currently run **two separate commands**:

```bash
# Step 1: Extract model metadata from live database
$ osm extract-model --output model.extracted.json

# Step 2: Feed extracted model into build/export
$ osm build-ssdt --model model.extracted.json
$ osm full-export --model model.extracted.json
```

**Problems**:
- Manual orchestration required
- Model file passed around as artifact
- No caching or reuse between runs
- Extract-model feels like a separate concern, not integrated

### Where Entity Data Comes From

**There is only ONE data source: The live database.**

When we say "entity with data", we mean:
- The entity has a physical table in the database
- Data was extracted from that table via `SELECT * FROM [table]`
- No data is stored inline in model JSON (model JSON is **metadata only**)

**Confusion to clear up**:
- ❌ Model JSON does NOT contain row data
- ❌ Static seeds do NOT have "inline data"
- ✅ ALL data comes from database extraction
- ✅ Model JSON contains only structure (schema, relationships, indexes)

### Vision: Integrated Extract-Model

**Extract-model should be integrated into the pipeline**, not a separate manual step:

```bash
# ONE command, extract-model happens automatically
$ osm build-ssdt --connection-string "..."

# Model is cached as artifact, reused across runs
# User doesn't manually pass model.json around
```

**How it fits**:
- Extract-model becomes **Stage 0** of the unified pipeline
- Model cached to disk (e.g., `.cache/model-snapshot.json`)
- Subsequent runs reuse cache if database schema unchanged
- User can still provide pre-extracted model if desired (for offline scenarios)

---

## 🌟 The North Star Vision

### What Success Looks Like

A **unified Entity Data Pipeline** where:

```
EntityPipeline.Execute(
    connectionString: "Server=...",

    scope: EntitySelector.FromModules(["ModuleA", "ModuleB"])
                         .Include("ServiceCenter", ["User"])
                         .Where(e => e.IsStatic),  // For static seeds

    emission: EmissionStrategy.Monolithic("output/bootstrap.sql"),

    insertion: InsertionStrategy.Insert(batchSize: 1000),

    sort: TopologicalSort.Global(
        circularDependencies: config.AllowedCycles
    ),

    // Transform stage: Preserve existing business logic
    // NOTE: This is a CONCEPTUAL stage - exact shape TBD via excavation
    transform: config.BusinessLogicTransforms
)
```

### What Gets Eliminated

1. **No more "Supplemental" concept**
   - ServiceCenter::User is just an entity selected via `EntitySelector`
   - No separate loading mechanism
   - Automatically included in topological sort

2. **No more "DynamicData" folder**
   - Redundant with Bootstrap
   - Was a misnomer from day one

3. **No more triple-fetching metadata**
   - One fetch to OSSYS_* tables
   - Cached snapshot
   - Different pipelines consume different slices

4. **No more fragmented topological sorts**
   - One unified graph spanning ALL selected entities
   - Static/regular/supplemental all sorted together
   - Cycles resolved once with full context

### What Gets Unified

| Current Name | Becomes | Configuration |
|--------------|---------|---------------|
| Static Seeds | `EntityPipeline` | `scope: Static, insertion: MERGE` |
| Bootstrap | `EntityPipeline` | `scope: AllWithData, insertion: INSERT, emission: Monolithic` |
| DynamicData | **DELETED** | (redundant) |
| Supplemental | **DELETED** | (use `EntitySelector.Include()`) |

---

## 🔬 The Triple-Fetch Redundancy Problem

### Current State

Three separate callsites hitting the database:

1. **Extract-Model** (`SqlModelExtractionService`)
   - Purpose: Build OsmModel (schema, relationships, indexes)
   - SQL: `outsystems_metadata_rowsets.sql`
   - Hits: `OSSYS_*` system tables (metadata)
   - What it gets: Entity structure, relationships, indexes
   - Frequency: Once per pipeline run (currently manual)

2. **Profile** (`SqlDataProfiler`)
   - Purpose: Gather statistics (null counts, FK orphans, uniqueness violations)
   - SQL: Multiple targeted queries
   - Hits: `OSSYS_*` metadata + `sys.*` system tables
   - What it gets: Data quality metrics
   - Frequency: Once per pipeline run

3. **Export Data** (`SqlDynamicEntityDataProvider`)
   - Purpose: Extract actual row data
   - SQL: `SELECT * FROM [table]` queries
   - Hits: `OSUSR_*` / `dbo.*` data tables
   - What it gets: Actual row data for each entity
   - Frequency: Once per entity

**The Problem**:
- `OSSYS_*` tables queried 2-3 times with similar WHERE clauses
- Model extraction and profiling both hit metadata tables
- No caching or reuse between operations

### The DatabaseSnapshot Primitive

**Vision**: A single `DatabaseSnapshot` that captures both metadata and data in one coordinated fetch:

```csharp
public sealed class DatabaseSnapshot
{
    // Metadata from OSSYS_* tables (extract-model output)
    public ImmutableArray<EntityStructure> Entities { get; }
    public ImmutableArray<RelationshipStructure> Relationships { get; }
    public ImmutableArray<IndexStructure> Indexes { get; }
    public ImmutableArray<TriggerStructure> Triggers { get; }

    // Statistics from profiling (sys.* + OSSYS_* analysis)
    public ImmutableDictionary<TableKey, EntityStatistics> Statistics { get; }

    // Actual row data from OSUSR_* tables
    public ImmutableDictionary<TableKey, EntityData> Data { get; }

    // Derived views (lazy, cached)
    public OsmModel ToModel() => /* project to model structure */;
    public ProfileSnapshot ToProfile() => /* project to profile */;
    public EntityDataSet ToDataSet() => /* project to data */;
}
```

**Fetch Options**:
```csharp
var snapshot = await DatabaseFetcher.FetchAsync(
    connectionString: "...",
    selector: EntitySelector.FromModules(...),
    options: new FetchOptions
    {
        IncludeStructure = true,  // Extract-model (OSSYS_* metadata)
        IncludeStatistics = true, // Profiling (data quality metrics)
        IncludeRowData = true,    // Export (actual OSUSR_* table data)
    },
    cache: DiskCache("./.cache/database-snapshot")
);
```

**Benefits**:
- `OSSYS_*` tables hit once instead of 2-3 times
- Coordinated fetch of metadata + statistics + data
- Cached to disk (reusable across runs, survives restarts)
- Different pipelines consume different slices of same snapshot
- Invalidate cache when database schema changes (checksum-based)

---

## 🎭 The "Supplemental" Problem: Missing Primitive

### Why Supplemental Exists

The user needs **ServiceCenter::User** in their deployment, but:
- ServiceCenter has 300 entities
- They only want User
- They want it to appear in a different target module
- The metadata is incomplete (missing incoming FK relationships)

**Current workaround**:
1. Hand-craft `ossys-user.json` with partial metadata
2. Load it via `SupplementalEntityLoader`
3. **Problem**: Not included in topological sort
4. **Problem**: Missing FK metadata causes violations

### The Real Problem

**There's no primitive to select a single entity from a module.**

The codebase thinks in terms of:
- Include entire module ✅
- Exclude entire module ✅
- Filter entities within a module... ⚠️ **exists but incomplete**

**EntityFilters already exist** in `ModuleFilterConfiguration`:
```csharp
IReadOnlyDictionary<string, IReadOnlyList<string>> EntityFilters
```

But:
- Not used for supplemental use case
- Doesn't support module remapping (ServiceCenter → MyModule)
- Doesn't trigger full metadata fetch for filtered entities

### The Solution (Conceptual)

**Enhance EntitySelector to be first-class**:

```csharp
var selector = EntitySelector.FromModules([
    new ModuleSelection("ModuleA", includeAll: true),
    new ModuleSelection("ModuleB", includeAll: true),
    new ModuleSelection("ServiceCenter", includeOnly: ["User"]),
])
.WithRemapping("ServiceCenter" → "MyCustomModule");
```

**What this enables**:
- No more "supplemental" concept - it's just entity selection
- Automatically included in topological sort (it's part of the selected entities)
- Full metadata fetch (because it's a selected entity from a module)
- Module remapping for deployment flexibility

---

## 🔗 Topological Sort: The Unified Graph

### Current Problem

Topological sort happens **separately** in different contexts:

1. **Static Seeds**: Sorts only static entities
2. **Bootstrap**: Sorts static + regular entities
3. **DynamicData**: Sorts all entities (redundant with Bootstrap)
4. **Supplemental**: **NOT SORTED AT ALL** ← This is the bug

**Result**: FK violations when User isn't inserted before tables with CreatedBy/UpdatedBy

### Why This is Wrong

Foreign keys **don't respect entity categories**:
- A regular entity can reference a static entity
- A static entity can reference a regular entity
- ServiceCenter::User is referenced by entities in other modules

**If you sort separately**, you miss cross-category dependencies!

### The Correct Model

**One unified dependency graph** spanning ALL selected entities:

```
Topological Sort Input:
    - All static entities in scope
    - All regular entities in scope
    - All supplemental entities in scope (currently broken)

Topological Sort Output:
    - One ordered list of ALL entities
    - Respects ALL FK dependencies (cross-category, cross-module)
    - Cycles resolved with full context

Then Split for Emission:
    - Static seeds: Filter sorted list to static entities only
    - Bootstrap: Use entire sorted list
```

**Key Insight**: Sort first with complete graph, THEN filter for emission strategies.

---

## 🏗️ Conceptual Architecture: The Unified Pipeline

### The Pipeline Stages

```
┌─────────────────────────────────────────────────────┐
│  Stage 0: EXTRACT-MODEL (Integrated)                │
│  Input: Connection string, module filters           │
│  Operation: Query OSSYS_* for metadata              │
│  Output: OsmModel (cached to disk)                  │
│  Cache: .cache/model-snapshot.json                  │
└─────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────┐
│  Stage 1: ENTITY SELECTION                          │
│  Input: EntitySelector configuration                │
│  Output: Set of entities to process                 │
│  Examples:                                          │
│    - All entities: EntitySelector.All()            │
│    - Static only: EntitySelector.Where(IsStatic)   │
│    - Specific: Include("ServiceCenter", ["User"]) │
└─────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────┐
│  Stage 2: DATABASE SNAPSHOT FETCH                   │
│  Input: Selected entities, connection string        │
│  Operation: Fetch from DatabaseSnapshot (cached)    │
│  Output: Metadata + statistics + row data           │
│  Sources:                                           │
│    - OSSYS_* metadata (if not cached from Stage 0) │
│    - sys.* statistics (profiling metrics)          │
│    - OSUSR_* data tables (actual rows)             │
└─────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────┐
│  Stage 3: BUSINESS LOGIC TRANSFORMS (Excavation)    │
│  Input: Raw database snapshot                       │
│  Operation: Apply business rules and corrections    │
│  Output: Transformed entity definitions              │
│  ⚠️  CAUTION: Exact transforms TBD via discovery   │
│  Known examples (non-exhaustive):                   │
│    - Nullability config (isMandatory → NOT NULL)   │
│    - Deferred FK constraints (WITH NOCHECK)        │
│    - Type mappings (money → INT precision)         │
│    - UAT-users generation (see M2.* docs)          │
│  Principles:                                        │
│    - Model + data = source of truth                │
│    - App warns operator, requires explicit sign-off│
│    - NO automatic coercion (e.g., no auto NOT NULL)│
│    - Ordering matters (must preserve dependencies) │
└─────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────┐
│  Stage 4: TOPOLOGICAL SORT                          │
│  Input: All selected entities (with FK metadata)    │
│  Operation: Build dependency graph, detect cycles   │
│  Output: Globally ordered list of entities          │
│  Scope: ALWAYS spans all selected entities          │
│  Handles:                                           │
│    - Cross-module dependencies                     │
│    - Circular dependencies (manual config)         │
│    - Mixed static/regular entities                 │
└─────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────┐
│  Stage 5: EMISSION                                  │
│  Input: Sorted entities + emission strategy         │
│  Operation: Generate SQL scripts                    │
│  Output: Files organized by strategy                │
│  Strategies:                                        │
│    - Monolithic: One file (preserves sort order)   │
│    - Multiple files + .sqlproj (references in order)│
│  Note: Per-module emission NOT supported            │
│        (breaks topological dependencies)            │
└─────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────┐
│  Stage 6: INSERTION STRATEGY APPLICATION            │
│  Input: SQL scripts + insertion config              │
│  Operation: Format as INSERT, MERGE, etc.           │
│  Output: Deployment-ready SQL                       │
│  Options:                                           │
│    - INSERT (one-time load)                        │
│    - MERGE (upsert on redeploy)                    │
│    - Batch size (default: 1000)                    │
└─────────────────────────────────────────────────────┘
```

### The Parameterization

**Every use case is this pipeline with different parameters**:

| Use Case | Scope | Emission | Insertion |
|----------|-------|----------|-----------|
| **Static Seeds** | `entity.IsStatic` | Monolithic or Per-Entity | MERGE on PK |
| **Bootstrap** | `allEntitiesWithData` | Monolithic | INSERT |
| **ServiceCenter::User** | `ServiceCenter: [User]` | Monolithic | INSERT |
| **Module Refresh** | `ModuleA: all` | Per-Module | MERGE |

**No special cases needed** - just different configurations of the same pipeline.

---

## ✅ Resolved Ontological Questions

### Q1: What IS an "entity with data"?

**ANSWER**: An entity where data was extracted from its physical table in the database.

- ✅ **Option B**: Entity has rows in the live database (extracted via `SELECT *`)
- ❌ Not Option A: No data is stored inline in model JSON
- ❌ Not Option C: Not explicitly configured

**Implementation**: When pipeline runs, it queries each entity's table. If rows exist, the entity "has data". Simple.

---

### Q2: Where does entity data come from?

**ANSWER**: There is only ONE data source - the live database.

- ✅ ALL data comes from `LiveDatabaseExtraction`
- ❌ NO inline data in model JSON (model JSON is metadata only)
- ❌ NO supplemental JSON files with data (ossys-user.json will be deprecated)

**Confusion cleared**: I was wrong about "merged data sources". There's just one source: the database.

---

### Q3: What is the relationship between "static entity" and "has data"?

**ANSWER**: They are orthogonal concerns.

- **Static Entity**: `DataKind = 'staticEntity'` (compile-time optimization, like enhanced enums)
- **Has Data**: Entity table contains rows (runtime state)
- **Independence**: Both static and regular entities CAN have data or not
  - A static entity might have 0 rows (just created)
  - A regular entity might have 1000 rows (actively used)

**Key insight**: Static-ness is a metamodel property. Data availability is a runtime property.

---

### Q4: How granular should EntitySelector be?

**ANSWER**: Option B (per-module control) + helper predicates

**Required API**:
```csharp
// Option B: Per-module control
EntitySelector
    .Include("ModuleA", all: true)
    .Include("ServiceCenter", only: ["User"])

// Plus helper predicates
EntitySelector.Where(e => e.IsStatic)  // For static seeds
EntitySelector.All()                    // For bootstrap, uat-users
```

**Use cases**:
- `All()` → Model extraction, table emission, bootstrap, uat-users
- `Where(IsStatic)` → Static seeds
- `Include("ServiceCenter", ["User"])` → Supplemental replacement

**Rationale**: Option C (full predicate-based) is overkill. Option B covers all current needs.

---

### Q5: Should topological sort be configurable per emission strategy?

**ANSWER**: No. Sort is ALWAYS global. Emission is just file organization.

**Two valid emission approaches**:
1. **Monolithic**: One file, entities in sorted order
2. **Multiple files + .sqlproj**: Files organized arbitrarily (per-module, per-entity), but .sqlproj lists them in sorted order

**Per-module sorting is NOT supported** (breaks cross-module FK dependencies).

**Example**:
```
Sorted order: [User, Post, Comment, Module1Table, Module2Table]

Emission Option 1 (Monolithic):
  bootstrap.sql → contains all 5 entities in order

Emission Option 2 (.sqlproj):
  ServiceCenter/User.sql
  ModuleA/Post.sql
  ModuleA/Comment.sql
  ModuleB/Module1Table.sql
  ModuleB/Module2Table.sql

  bootstrap.sqlproj → references files in sorted order:
    <Build Include="ServiceCenter/User.sql" />
    <Build Include="ModuleA/Post.sql" />
    <Build Include="ModuleA/Comment.sql" />
    <Build Include="ModuleB/Module1Table.sql" />
    <Build Include="ModuleB/Module2Table.sql" />
```

**Key insight**: File organization (directory structure) is cosmetic. Execution order (topological) is semantic.

---

## ⚠️ The Business Logic Transform Challenge

### Why This is Scary

Stage 3 (Business Logic Transforms) is **conceptual**, not prescriptive. We know this stage **exists** in the current codebase, but:

- ❌ We don't know ALL the transforms that happen
- ❌ We don't know the ORDERING dependencies between transforms
- ❌ We don't know all the EDGE CASES and special handling
- ✅ We know we need to **excavate**, not design from scratch

### Excavation, Not Design

**Approach**: During implementation, we will:

1. **Discover transforms** as we encounter them in the codebase
2. **Document each one** when found (what, why, when, dependencies)
3. **Preserve existing behavior** - don't redesign, just consolidate
4. **Test extensively** - each transform needs regression coverage
5. **Make each transform independently runnable** (for debuggability)

**Anti-pattern**: Trying to enumerate all transforms upfront and design an abstraction around them.

### Known Examples (Non-Exhaustive)

These are transforms we **know exist**, but this list is incomplete:

| Transform | What it does | Config-driven? | Order-sensitive? |
|-----------|--------------|----------------|------------------|
| Nullability tightening | isMandatory → NOT NULL (with operator approval) | Yes (validation overrides) | Unknown |
| Deferred FK constraints | Add WITH NOCHECK for orphaned FKs | Yes (profiling results) | After FK detection |
| Type mappings | Money → INT precision, numbers → different precision | Possibly | Unknown |
| UAT-users generation | Generate user data for UAT environments | Yes (M2.* docs) | Unknown |
| Column/table remapping | Rename tables/columns across environments | Yes (naming overrides) | Before emission |

**Unknown unknowns**: There are almost certainly more transforms we haven't listed.

### Critical Principles (DO NOT VIOLATE)

1. **Model + data = source of truth**
   - The application does NOT invent data
   - The application does NOT silently fix inconsistencies

2. **Warn, don't auto-fix**
   - If isMandatory but column is nullable → WARN operator
   - Operator provides explicit config approval for NOT NULL
   - ❌ NEVER auto-coerce based on "no NULLs observed in data"

3. **Profiling is informational, not prescriptive**
   - Profiling finds issues (null counts, FK orphans)
   - Operator decides how to handle issues (WITH NOCHECK, fix data, override config)
   - "Cautious mode" prevents automatic coercion

4. **Preserve debuggability**
   - Each transform should be independently toggleable
   - Each transform should be independently runnable (for testing)
   - Transforms should log what they're doing (observability)

### The Ordering Problem

**We don't know the full dependency graph of transforms.**

Example questions we can't answer yet:
- Must type mapping happen before nullability tightening?
- Must UAT-users happen before or after FK detection?
- Can column remapping happen in parallel with type mapping?

**Strategy**: During excavation, **document dependencies as we discover them**.

### Work Plan Implication

When we get to execution planning, we need a **dedicated phase**:

**"Phase X: Business Logic Transform Excavation"**
1. Audit codebase for all transform logic
2. Document each transform (inputs, outputs, config, dependencies)
3. Extract into isolated, testable units
4. Build regression test suite
5. Create transform registry with ordering metadata

This is a **risky, meticulous phase** - we can't rush it.

---

## 📝 Terminology Decisions

### What We Keep

- **Static Entity**: Entity where `DataKind = 'staticEntity'` (OutSystems metamodel concept)
- **Regular Entity**: Entity where `DataKind != 'staticEntity'`
- **Bootstrap**: The deployment artifact containing INSERT scripts for all entities (first-time load)
- **Module**: Logical grouping of entities (OutSystems concept)
- **Topological Sort**: Dependency-ordered list of entities (graph algorithm concept)

### What We Deprecate

- ~~**Supplemental Entity**~~: No longer a concept - just entities selected via EntitySelector
- ~~**DynamicData**~~: Misnomer, redundant with Bootstrap - DELETE
- ~~**Normal Entity**~~: Never use this term (user explicitly rejected it)

### What We Introduce

- **EntitySelector**: Configuration for selecting which entities to include (replaces supplemental mechanism)
- **EmissionStrategy**: How to organize output files (monolithic / multiple files + .sqlproj)
- **InsertionStrategy**: How to apply data (INSERT/MERGE/etc.)
- **DatabaseSnapshot**: Cached fetch of metadata (OSSYS_*) + statistics (sys.*) + data (OSUSR_*)
- **AllEntities**: The union of all selected entities (static + regular + any others in scope)
- **BusinessLogicTransforms**: Stage for nullability tightening, deferred FKs, UAT-users, remapping

---

## 🎬 Next Steps (Conceptual, Not Implementation)

### Before We Can Plan Execution

We need alignment on:

1. **The Three Dimensions + Transform**: ✅ ALIGNED (with nervous caveats on Transform) - Are Scope, Emission, Insertion the right core dimensions?
2. **The Unified Pipeline**: ✅ ALIGNED (conceptually) - Does the 7-stage model make sense?
3. **Supplemental Elimination**: ✅ ALIGNED - EntitySelector replaces supplemental concept
4. **Topological Sort Scope**: ✅ ALIGNED - ALWAYS spans all selected entities
5. **DatabaseSnapshot Primitive**: ✅ ALIGNED - Right abstraction (metadata + statistics + data)
6. **Extract-Model Integration**: ✅ ALIGNED - Stage 0 (automatic, cached), each step independently runnable
7. **Business Logic Transforms**: ⚠️ EXCAVATION REQUIRED
   - We agree Stage 3 exists conceptually
   - We agree on principles (model = truth, warn don't auto-fix, preserve debuggability)
   - We DON'T know all transforms yet (discovery during implementation)
   - We DON'T know ordering dependencies yet (document as we find them)
   - We WILL NOT redesign, only consolidate existing behavior
   - Dedicated excavation phase required in execution plan

### Once We Have Alignment

Then we can move to:
- Detailed design of EntitySelector API
- Migration path from current config to new config
- Phased implementation plan
- Test strategy

---

## 💭 Reflection: Why This Matters

The current codebase isn't "wrong" - it **evolved organically** as different use cases arose:
1. First: Static seeds (MERGE for reference data)
2. Then: Bootstrap (INSERT for initial load)
3. Then: DynamicData (attempt to generalize, but misfired)
4. Then: Supplemental (workaround for missing entity selection)

Each addition was **pragmatic** at the time. But now we see the **hidden structure**.

**Refactoring opportunity**: Extract the invariant, parameterize the variants.

**The payoff**:
- Delete ~1000 lines of redundant code
- Unify topological sort (fixes FK violations)
- Enable new use cases trivially (just different parameters)
- Make the system **comprehensible** (one mental model, not four)

---

## 🤝 Invitation to Iterate

This document captures my current understanding of the ontological structure.

**It is intentionally conceptual** - no class names, no line numbers, no "how to implement."

**Questions for you**:
1. Does this capture the convergence pattern you see?
2. Are the three dimensions the right orthogonal axes?
3. Is the North Star vision aligned with where you want to go?
4. What am I still missing or misunderstanding?

Let's refine this conceptual map until we have **shared clarity**, then we can think about execution.

---

*End of conceptual document. Ready to iterate.*
