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

2. **Bootstrap** (`BuildSsdtBootstrapSnapshotStep`) - **✅ DEMONSTRATES UNIFIED PATTERN**
   - Scope: All entities with data (static + regular + supplemental)
   - Emission: One monolithic file
   - Insertion: INSERT (one-time load, NonDestructive mode)
   - Topological Sort: **Global across ALL entities** (static + regular + supplemental)
   - **Advanced Features**: Enhanced cycle diagnostics, TopologicalOrderingValidator, CircularDependencyOptions support
   - **Key Insight**: This implementation already shows the unified pipeline pattern working correctly
   - **Location**: `src/Osm.Pipeline/Orchestration/BuildSsdtBootstrapSnapshotStep.cs`

3. **DynamicData** (`BuildSsdtDynamicInsertStep`) - **DEPRECATED**
   - Scope: All entities with data
   - Emission: Per-entity files
   - Insertion: INSERT
   - Topological Sort: All entities
   - **Status**: Redundant with Bootstrap, misnamed, to be removed

4. **Supplemental Entities** (`SupplementalEntityLoader`)
   - Scope: Individual entities (e.g., ServiceCenter::User)
   - Mechanism: Load from JSON files
   - **Status in StaticSeeds**: Not integrated into topological sort (fragmentation problem)
   - **Status in Bootstrap**: ✅ ALREADY INTEGRATED - see `BuildSsdtBootstrapSnapshotStep.cs:50-60`
   - **Future**: Workaround for missing "select one entity from module" primitive - to be replaced by EntitySelector
   - **Deprecation Path**: Once EntitySelector supports `Include("ServiceCenter", ["User"])`, this mechanism is obsolete

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

## 🧬 What a Pipeline IS

The pipeline is **composition**. Not "uses composition" - IS composition.

This truth is visible in `IBuildSsdtStep<TInput, TOutput>` (`IBuildSsdtStep.cs:7-10`):

```csharp
public interface IBuildSsdtStep<in TInput, TNextState>
{
    Task<Result<TNextState>> ExecuteAsync(TInput state, CancellationToken cancellationToken = default);
}
```

Each step declares what it receives and what it produces. Steps chain via `.BindAsync()` - monadic composition where the output type of step N must match the input type of step N+1. The compiler enforces this. Type errors are ontological impossibilities.

The pipeline is a series of state transformations: `PipelineInitialized → BootstrapCompleted → EvidenceCacheCompleted → ...` Each state carries forward everything from before, immutably, adding only what this step contributes.

This pattern already exists. Bootstrap demonstrates it. StaticSeeds uses it. The unification isn't creating this - it's recognizing that **the code already knew**.

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

**The Reification**: This dimension already exists as `ModuleFilterOptions` (`src/Osm.Domain/Configuration/ModuleFilterOptions.cs`). The abstraction and the concrete are the same shape.

Within it, `EntityFilters` - a dictionary mapping module names to entity lists - expresses "filtered entities per module" exactly:

```json
{
  "modules": [
    { "name": "ServiceCenter", "entities": ["User", "Tenant"] },
    { "name": "MyModule", "entities": [] }
  ]
}
```

This primitive exists. Config parsing works. Runtime access works. Someone already saw this shape.

**What's incomplete**: The wiring. SQL extraction ignores `EntityFilters`. Validation ignores it. Profiling ignores it. The API is correct; the integration is partial.

**Current Problem**:
- Static Seeds: Hardcoded to `entity.IsStatic`
- Bootstrap: Hardcoded to `allEntitiesWithData`
- Supplemental: Workaround because `EntityFilters` isn't fully wired

**The Path**: Complete `EntityFilters` (SQL queries, validation scope, profiling scope). Supplemental becomes obsolete. The primitive was always there.

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

**Per-Module Emission EXISTS but is BROKEN** ✅ Verified in `BuildSsdtStaticSeedStep.cs:103-156`:
- Current implementation: `GroupByModule` option creates separate files per module
- **Problem**: Each module file doesn't coordinate FK ordering with other modules
- **Why it breaks**: Cross-module dependencies (e.g., Module B references Module A) are not respected
- **Root cause**: Files are organized by module boundary, but topological order is global
- **Correct approach**: You CAN organize files by module, as long as .sqlproj orders them correctly

**Current State**:
- Static Seeds: Supports monolithic or per-module (per-module is problematic!)
- Bootstrap: Monolithic only (correct)
- DynamicData: Per-entity or single file (redundant, to be deleted)

**The Reification**: Implicit in current code via `GroupByModule`, `EmitMasterFile` flags. Partially reified in `NamingOverrideOptions` (table/column remapping across environments). The full abstraction - `EmissionStrategy` - wants to exist but hasn't been extracted yet.

**Vision**: Make emission strategy explicit. Monolithic or .sqlproj-ordered. Independent of scope and sort. Chosen after topological sort completes.

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

**The Reification**: `StaticSeedSynchronizationMode` (`StaticSeedSqlBuilder.cs`) and `DynamicEntityInsertGenerationOptions` (`DynamicEntityInsertGenerator.cs`) are this dimension, fragmented. MERGE logic exists. INSERT logic exists. They're separate implementations, not parameter variants.

**Current Problem**:
- Static Seeds: Hardcoded to MERGE
- Bootstrap: Hardcoded to INSERT
- The mechanism exists; the abstraction doesn't

**Vision**: Unify as `InsertionStrategy`. Same entity set, same topological order, different insertion semantics via configuration.

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

### Bootstrap Shows Us the Way ✅

**Critical Insight**: `BuildSsdtBootstrapSnapshotStep` ALREADY demonstrates the unified pipeline pattern:
- ✅ Global entity selection (static + regular + supplemental)
- ✅ Global topological sort across all entity categories
- ✅ Cycle diagnostics with CircularDependencyOptions
- ✅ Monolithic emission preserving topological order
- ✅ Configurable insertion strategy (NonDestructive mode = INSERT)

**The Extraction Challenge**: Bootstrap is a **specific use case** (one-time INSERT of all data). We need to:
1. **Extract the patterns** Bootstrap demonstrates (global sort, cycle handling, unified entity selection)
2. **Create abstractions** that work for BOTH:
   - Bootstrap (keep as-is: global INSERT for first-time deployment)
   - StaticSeeds (needs same patterns: global sort, but MERGE instead of INSERT, filtered to static entities)
3. **Parameterize** the pipeline (scope, emission, insertion become configuration, not separate implementations)

**Anti-pattern**: Renaming everything to "Bootstrap" - that conflates the specific (Bootstrap use case) with the general (unified pipeline architecture).

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

### Current Problem (Fragmentation)

Topological sort happens **separately** in different contexts:

1. **Static Seeds**: Sorts only static entities (scoped to static-only) ✅ Verified in `BuildSsdtStaticSeedStep.cs:82-86`
2. **Bootstrap**: ✅ **CORRECT** - Sorts static + regular + supplemental entities globally (see `BuildSsdtBootstrapSnapshotStep.cs:105-110`)
3. **DynamicData**: Sorts all entities (redundant with Bootstrap, DEPRECATED)
4. **Supplemental in StaticSeeds context**: **NOT SORTED** ← This causes the fragmentation problem

**✅ Verified**: Supplementals ARE integrated in Bootstrap's global sort (see `BuildSsdtBootstrapSnapshotStep.cs:50-60`)
**Problem**: StaticSeeds doesn't include supplementals, causing FK violations when StaticSeeds is run independently

### Why This is Wrong

Foreign keys **don't respect entity categories**:
- A regular entity can reference a static entity
- A static entity can reference a regular entity
- ServiceCenter::User is referenced by entities in other modules

**If you sort separately**, you miss cross-category dependencies!

### The Correct Model ✅ ALREADY DEMONSTRATED IN BOOTSTRAP

**One unified dependency graph** spanning ALL selected entities:

```
Topological Sort Input:
    - All static entities in scope
    - All regular entities in scope
    - All supplemental entities in scope

Topological Sort Output:
    - One ordered list of ALL entities
    - Respects ALL FK dependencies (cross-category, cross-module)
    - Cycles resolved with full context (via CircularDependencyOptions)

Then Split for Emission:
    - Static seeds: Filter sorted list to static entities only
    - Bootstrap: Use entire sorted list ✅ THIS IS WHAT BOOTSTRAP DOES
```

**Key Insight**: Sort first with complete graph, THEN filter for emission strategies.

**✅ Bootstrap Proof**: `BuildSsdtBootstrapSnapshotStep.cs:57-60` shows the pattern:
```csharp
var allEntities = staticEntities
    .Concat(regularEntities)
    .Concat(supplementalEntities.Value)  // All categories unified
    .ToImmutableArray();
// Then sorted globally at line 105-110
```

**Extraction Pattern**: Bootstrap demonstrates the correct architecture; we need to:
1. Extract the pattern (global sort across all entity categories)
2. Create abstractions that work for both Bootstrap (specific use case) AND StaticSeeds (needs same pattern)
3. Make it configurable (scope, emission, insertion are parameters, not separate implementations)

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
│    - UAT-users discovery (FK catalog + orphan map) │
│      ↳ Application: Stage 5 (INSERT) or Stage 6 (UPDATE)
│  Principles:                                        │
│    - Model + data = source of truth                │
│    - App warns operator, requires explicit sign-off│
│    - NO automatic coercion (e.g., no auto NOT NULL)│
│    - Ordering matters (must preserve dependencies) │
│    - Some transforms discovered here, applied later│
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

## ⚠️ The Business Logic Transform Challenge ⚠️ HIGH RISK AREA

### Why This is Scary

Stage 3 (Business Logic Transforms) is **conceptual**, not prescriptive. We know this stage **exists** in the current codebase, but:

- ❌ We don't know ALL the transforms that happen
- ❌ We don't know the ORDERING dependencies between transforms
- ❌ We don't know all the EDGE CASES and special handling
- ✅ We know we need to **excavate**, not design from scratch

**⚠️ RISK ASSESSMENT**: Initial codebase exploration reveals **MORE embedded transforms than originally documented**. This area requires careful, systematic excavation before any refactoring begins.

### Excavation, Not Design

**Approach**: During implementation, we will:

1. **Discover transforms** as we encounter them in the codebase
2. **Document each one** when found (what, why, when, dependencies)
3. **Preserve existing behavior** - don't redesign, just consolidate
4. **Test extensively** - each transform needs regression coverage
5. **Make each transform independently runnable** (for debuggability)

**Anti-pattern**: Trying to enumerate all transforms upfront and design an abstraction around them.

### Known Examples (Non-Exhaustive) - Continuously Updated

These are transforms we **know exist**, but this list is incomplete and will be expanded during excavation:

| Transform | What it does | Location | Config-driven? | Order-sensitive? |
|-----------|--------------|----------|----------------|------------------|
| **EntitySeedDeterminizer.Normalize** | ✅ Deterministic ordering/normalization of entity data | `BuildSsdtStaticSeedStep.cs:78` | No | Before sort |
| **StaticSeedForeignKeyPreflight.Analyze** | ✅ FK orphan detection and ordering violation detection | `BuildSsdtStaticSeedStep.cs:90` | No | After sort |
| **Module name collision handling** | ✅ Disambiguate duplicate module names with suffix (e.g., Module_2) | `BuildSsdtStaticSeedStep.cs:196-231` | No | Before emission |
| **Supplemental entity physical→logical remapping** | ✅ Transform ServiceCenter::ossys_User → ServiceCenter::User | `BuildSsdtBootstrapSnapshotStep.cs:338-361` | No | Before emission |
| **TopologicalOrderingValidator** | ✅ Enhanced cycle diagnostics with FK analysis, nullable detection, CASCADE warnings | `BuildSsdtBootstrapSnapshotStep.cs:115-127, 186-325` | Yes (CircularDependencyOptions) | After sort |
| **Deferred FK constraints (WITH NOCHECK)** | ✅ Emit FKs marked `IsNoCheck` as separate ALTER TABLE ADD CONSTRAINT WITH CHECK/NOCHECK statements | `CreateTableStatementBuilder.cs:190-195, 214-249` | Yes (profiling detects orphans) | After CREATE TABLE emission |
| **TypeMappingPolicy** | ✅ Complex data type transformation system: attribute types → SQL types, handles OnDisk overrides, external types, precision/scale | `TypeMappingPolicy.cs`, `TypeMappingRule.cs`, `config/type-mapping.default.json` | Yes (type-mapping.json) | During CREATE TABLE generation |
| **SmoNormalization.NormalizeSqlExpression** | ✅ Remove redundant outer parentheses from DEFAULT constraints and SQL expressions | `SmoNormalization.cs:17-49` | No | During emission |
| **CreateTableFormatter** | ✅ Format inline DEFAULT and CONSTRAINT definitions, normalize whitespace in CREATE TABLE statements | `CreateTableFormatter.cs:76-94` | Yes (SmoFormatOptions) | During emission |
| **NullabilityEvaluator** | ✅ Policy-driven nullability tightening: isMandatory + profiling data → NOT NULL recommendations | `NullabilityEvaluator.cs`, `TighteningPolicy.cs` | Yes (TighteningOptions, validation overrides) | During tightening analysis |
| **UAT-users transformation** | **Transform user FK values: QA IDs → UAT IDs (dual-mode, see §UAT-Users)** | See §UAT-Users | **Yes (inventories + mapping)** | **After FK discovery, before/during emission** |
| Column/table remapping (naming overrides) | ✅ Rename tables/columns across environments using effective names | `NamingOverrideOptions`, used throughout emission | Yes (naming overrides config) | Before emission |

**✅ Verified** = Found in codebase with file location and confirmed behavior
**Unknown unknowns**: There are almost certainly more transforms we haven't listed - **continuous excavation required**

### Additional Emission Aesthetics Discovered

These don't change data/structure but affect SQL formatting:

| Aesthetic Transform | What it does | Location |
|---------------------|--------------|----------|
| **ConstraintFormatter** | ✅ Format FK and PK constraint blocks, add WITH CHECK/NOCHECK annotations | `ConstraintFormatter.cs` |
| **Whitespace normalization** | ✅ Normalize tabs, indentation, and spacing in generated SQL | `CreateTableFormatter.cs`, `SmoNormalization.cs:7-15` |
| **Constraint naming resolution** | ✅ Resolve constraint names using logical vs physical table names | `IdentifierFormatter.ResolveConstraintName` |

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

## 🔄 UAT-Users: A Dual-Mode Transform

### What UAT-Users Solves

**Problem**: QA→UAT data promotion when user IDs differ between environments.

- QA database has users with IDs [100, 101, 102, ...]
- UAT database has users with IDs [200, 201, 202, ...]
- Tables have FK columns referencing User.Id (CreatedBy, UpdatedBy, AssignedTo, etc.)
- **Can't just copy data** - orphan FK violations (100 doesn't exist in UAT)

**Solution**: Transform user FK values during data migration.

### The Two Operating Modes

UAT-users is a **Stage 3 Business Logic Transform** that can be **applied in two different ways**:

#### Mode 1: Pre-Transformed INSERTs (Recommended - Stage 5 Application)

**Pipeline flow**:
```
Stage 0: EXTRACT-MODEL
Stage 1: ENTITY SELECTION
Stage 2: DATABASE SNAPSHOT FETCH
Stage 3: BUSINESS LOGIC TRANSFORMS
  ├─ UAT-Users Discovery:
  │    - Discover FK catalog (all columns referencing User.Id)
  │    - Load QA user inventory (from Service Center export)
  │    - Load UAT user inventory (from Service Center export)
  │    - Identify orphans (QA users not in UAT)
  │    - Load user mapping (orphan → UAT user)
  │    - Build TransformationContext
  └─ (other transforms...)
Stage 4: TOPOLOGICAL SORT
Stage 5: EMISSION
  └─ Apply TransformationContext during INSERT generation
      - For each user FK column, transform value: orphan ID → UAT ID
      - Generate INSERT scripts with UAT-ready values
Stage 6: INSERTION STRATEGY APPLICATION
```

**Result**: `DynamicData/**/*.dynamic.sql` files contain pre-transformed data
- No orphan IDs present
- All user FKs reference valid UAT users
- **Load directly to UAT** - no post-processing needed

**Benefits**:
- ✅ Faster (bulk INSERT vs row-by-row UPDATE)
- ✅ Simpler deployment (single operation, not load-then-transform)
- ✅ Atomic (no mid-execution inconsistency)
- ✅ Idempotent (reload from source scripts anytime)

#### Mode 2: UPDATE Scripts (Legacy/Verification - Stage 6 Application)

**Pipeline flow**:
```
Stage 0-4: (same as Mode 1)
Stage 5: EMISSION
  └─ Generate INSERT scripts WITHOUT transformation (QA IDs preserved)
Stage 6: INSERTION STRATEGY APPLICATION
  └─ Generate separate UPDATE script to transform in-place
      - 02_apply_user_remap.sql with CASE blocks
      - WHERE clauses target only orphan IDs
      - WHERE IS NOT NULL guards (preserve NULLs)
```

**Result**: Two-step deployment
1. Load INSERT scripts (contains QA user IDs)
2. Run UPDATE script (transforms QA IDs → UAT IDs in-place)

**Use Cases**:
- ✅ Legacy UAT database migration (data already loaded with QA IDs)
- ✅ Verification artifact (cross-validate transformation logic)
- ✅ Proof of correctness (compare INSERT vs UPDATE modes)

### Required Configuration

UAT-users requires **additional inputs** beyond standard pipeline config:

1. **QA User Inventory** (`--qa-user-inventory ./qa_users.csv`)
   - CSV export from Service Center: `Id, Username, EMail, Name, External_Id, Is_Active, Creation_Date, Last_Login`
   - Defines the source user roster (all QA users)

2. **UAT User Inventory** (`--uat-user-inventory ./uat_users.csv`)
   - Same schema as QA inventory
   - Defines the target user roster (approved UAT users)

3. **User Mapping** (`--user-map ./uat_user_map.csv`)
   - Defines orphan → target transformations
   - Format: `SourceUserId, TargetUserId, Rationale`
   - Can be auto-generated via matching strategies (case-insensitive email, regex, etc.)

### Where It Fits in Pipeline Unification

**Stage 3: Business Logic Transforms** (Discovery Phase)
- Discover FK catalog (which columns reference User table)
- Load inventories and mapping
- Validate mapping (source in QA, target in UAT, no duplicates)
- Build TransformationContext

**Application varies by mode**:
- **INSERT mode (recommended)**: Applied during Stage 5 (Emission)
- **UPDATE mode (legacy)**: Applied during Stage 6 (Insertion Strategy)

### Ordering Dependencies

**Must happen AFTER**:
- FK catalog discovery (need to know which columns to transform)
- Database snapshot fetch (need data to transform)

**Must happen BEFORE** (INSERT mode):
- INSERT script generation (transformations applied during emission)

**Independent of**:
- Other Stage 3 transforms (nullability, deferred FKs, type mappings)
- Topological sort (doesn't change entity ordering, just values)

### Full-Export Integration

```bash
# Pre-transformed INSERT mode (recommended)
dotnet run --project src/Osm.Cli -- full-export \
  --mock-advanced-sql tests/Fixtures/extraction/advanced-sql.manifest.json \
  --profile-out ./out/profiles \
  --build-out ./out/uat-export \
  --enable-uat-users \
  --uat-user-inventory ./uat_users.csv \
  --qa-user-inventory ./qa_users.csv \
  --user-map ./uat_user_map.csv

# Result: DynamicData/**/*.dynamic.sql contains UAT-ready data
# Just load to UAT - no UPDATE step needed
```

### Standalone Mode (UPDATE Generation)

```bash
# Standalone uat-users verb (for verification or legacy migration)
dotnet run --project src/Osm.Cli -- uat-users \
  --model ./_artifacts/model.json \
  --connection-string "Server=uat;Database=UAT;..." \
  --uat-user-inventory ./uat_users.csv \
  --qa-user-inventory ./qa_users.csv \
  --out ./uat-users-artifacts

# Result: 02_apply_user_remap.sql with UPDATE statements
# Use as verification artifact or apply to existing UAT database
```

### Implementation Status

**Documented in**: `docs/verbs/uat-users.md`, `docs/implementation-specs/M2.*`

**Current State**:
- ✅ UPDATE mode fully implemented (standalone `uat-users` verb)
- 🚧 INSERT mode in progress (M2.2 - transformation during INSERT generation)
- 🚧 Verification framework (M2.1, M2.3 - automated validation)
- 🚧 Integration tests (M2.4 - comprehensive edge case coverage)

**Key Architectural Insight**:
UAT-users reveals that **transforms can have multiple application strategies**. The *discovery* (what to transform) belongs in Stage 3, but the *application* (when to transform) can vary:
- Stage 5 application: Transform during emission (INSERT mode)
- Stage 6 application: Transform post-emission (UPDATE mode)

This pattern may apply to other transforms too (e.g., type mappings could be pre-computed or applied dynamically).

---

## 🎨 Emission Aesthetics vs. Business Logic Transforms

### The Distinction (Unclear Boundary)

During excavation, we've discovered operations that don't clearly fit "Business Logic Transforms" (Stage 3). They're more about **how we format SQL output** than **what data/structure to emit**.

**Examples of Emission Aesthetics**:
- CREATE TABLE formatting (tabs, indentation, spacing)
- FK constraints inline with ON DELETE/ON UPDATE clauses
- Show only non-default settings (collation, character encoding)
- Transform DEFAULT constraints to remove surrounding parentheses
- Naming conventions: defaults unnamed, PKs/FKs named
- Drop triggers with empty predicates (optimization)
- Index ordering: UNIQUE INDEXES before regular INDEXES
- Use logical names instead of physical names (human-readable)
- Inline referenced entity/attribute names into FK/PK/IX/UIX definitions (DBA-style)

### Open Question: Where Do These Belong?

**Option A: Stage 5 (Emission) concerns**
- These are formatting choices made during SQL script generation
- They don't change WHAT is emitted, just HOW it looks
- Stage 5 already handles file organization, why not also formatting?

**Option B: Each stage has optional "aesthetic transforms"**
- Not elevated to first-class Transforms (Stage 3)
- But each stage can have formatting/optimization logic
- Example: Stage 4 (sort) might reorder indexes, Stage 5 (emit) formats them

**Option C: Some are Transforms, some are Emission**
- Module name overrides → Transform (changes structure)
- Inline FK names → Emission aesthetic (just formatting)
- Hard to draw the line

### Current Thinking

**Not sure yet.** Need to discover more during excavation to see natural groupings.

**What we know**:
- Business Logic Transforms (Stage 3) = changes to data/structure/constraints
- Emission Aesthetics = formatting, naming, readability choices
- Some operations blur the line (module name overrides?)

**Strategy**: Document all operations we find, group them logically as patterns emerge.

### Building Critical Mass

As we discover more operations, we'll:
1. Document each one (what, why, where in codebase)
2. Tag as: Transform / Aesthetic / Unclear
3. Look for patterns in groupings
4. Let natural categories emerge (don't force premature abstraction)

This will help future agents/developers understand **where to put new logic** without violating the architecture.

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
- **BusinessLogicTransforms**: Stage for nullability tightening, deferred FKs, UAT-users (dual-mode), remapping

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
   - ✅ UAT-Users researched and integrated (dual-mode transform, see §UAT-Users)
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
