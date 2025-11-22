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
   - Emission: One file or per-entity
   - Insertion: MERGE (upsert on each deployment)
   - Topological Sort: Only static entities

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

2. **Per-Module**: One file per module (all entities in that module)
   - Example: `ModuleA/data.sql`, `ModuleB/data.sql`
   - Use case: Module-level organization
   - **Constraint**: Topological sort must still span ALL modules (can't sort within module only)

3. **Per-Entity**: One file per entity
   - Example: `ModuleA/Table1.sql`, `ModuleA/Table2.sql`
   - Use case: Granular version control
   - **Constraint**: Must be referenced in correct order (via `.sqlproj` or similar)

**Current Problem**:
- Static Seeds: Supports monolithic or per-entity (configurable)
- Bootstrap: Hardcoded to monolithic only
- DynamicData: Supports per-entity or single file (redundant)

**Vision**:
- Emission strategy is **independent** of scope and sort
- Chosen **after** topological sort completes
- Configurable per use case

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

## 🧩 The Hidden Fourth Dimension: DATA SOURCE

Where does the entity data come from?

**Current Sources**:
1. **Extraction from live database** (`SqlDynamicEntityDataProvider`)
   - Hits OSSYS_* tables + actual data tables
   - Used by: Bootstrap, DynamicData

2. **Inline data in model JSON** (static entity data embedded in model)
   - Stored in model files
   - Used by: Static Seeds

3. **Supplemental JSON files** (`ossys-user.json`)
   - Hand-crafted or extracted separately
   - Used by: Supplemental mechanism

4. **Profiling results** (metadata only, no row data)
   - Statistics like null counts, FK orphans
   - Used by: Profile validation

**The Convergence**: All sources produce the same shape: `StaticEntityTableData`

**Vision**:
- Data source is **abstracted**
- Pipeline doesn't care if data came from live DB, JSON file, or inline
- Enables "mix and match" (e.g., static entities from model + User from live DB)

---

## 🌟 The North Star Vision

### What Success Looks Like

A **unified Entity Data Pipeline** where:

```
EntityPipeline.Execute(
    scope: EntitySelector.FromModules(["ModuleA", "ModuleB"])
                         .Include("ServiceCenter", ["User"]),

    dataSource: MergedDataSource([
        InlineModelData,
        LiveDatabaseExtraction(connectionString)
    ]),

    emission: EmissionStrategy.Monolithic("output/bootstrap.sql"),

    insertion: InsertionStrategy.Insert(batchSize: 1000),

    sort: TopologicalSort.Global(
        circularDependencies: config.AllowedCycles
    )
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

Three separate callsites hitting OSSYS_* tables:

1. **Extract-Model** (`SqlModelExtractionService`)
   - Purpose: Build OsmModel (schema, relationships, indexes)
   - SQL: `outsystems_metadata_rowsets.sql`
   - What it gets: Entity structure, relationships, indexes
   - Frequency: Once per pipeline run

2. **Profile** (`SqlDataProfiler`)
   - Purpose: Gather statistics (null counts, FK orphans, uniqueness violations)
   - SQL: Multiple targeted queries
   - What it gets: Data quality metrics
   - Frequency: Once per pipeline run

3. **Export Data** (`SqlDynamicEntityDataProvider`)
   - Purpose: Extract actual row data
   - SQL: `SELECT * FROM [table]` queries
   - What it gets: Actual row data for each entity
   - Frequency: Once per entity

**The Problem**:
- Same tables (`ossys_Entity`, `ossys_Entity_Attr`, etc.) queried 3+ times
- Similar WHERE clauses (module filters, active entities)
- Different SELECT projections

### The Metadata Primitive

**Vision**: A single `MetadataSnapshot` that captures everything in one fetch:

```csharp
public sealed class MetadataSnapshot
{
    // Raw metadata (one fetch from OSSYS_*)
    public ImmutableArray<EntityStructure> Entities { get; }
    public ImmutableArray<RelationshipStructure> Relationships { get; }
    public ImmutableArray<IndexStructure> Indexes { get; }
    public ImmutableArray<TriggerStructure> Triggers { get; }

    // Data (if requested)
    public ImmutableDictionary<TableKey, EntityData> Data { get; }

    // Statistics (if requested)
    public ImmutableDictionary<TableKey, EntityStatistics> Statistics { get; }

    // Derived views (lazy, cached)
    public OsmModel ToModel() => /* project to model structure */;
    public ProfileSnapshot ToProfile() => /* project to profile */;
    public EntityDataSet ToDataSet() => /* project to data */;
}
```

**Fetch Options**:
```csharp
var snapshot = await MetadataFetcher.FetchAsync(
    selector: EntitySelector.FromModules(...),
    options: new FetchOptions
    {
        IncludeStructure = true,  // Always needed
        IncludeStatistics = true, // For profiling
        IncludeRowData = true,    // For export
    },
    cache: DiskCache("./cache/metadata")
);
```

**Benefits**:
- One SQL round-trip instead of 3+
- Cached to disk (reusable across runs)
- Different pipelines get different views of same data
- Invalidate cache when model changes

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
│  Stage 1: ENTITY SELECTION                          │
│  Input: EntitySelector configuration                │
│  Output: Set of entities to process                 │
│  Examples:                                          │
│    - All entities from [ModuleA, ModuleB]          │
│    - Static entities only                          │
│    - ServiceCenter::User + all from ModuleA        │
└─────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────┐
│  Stage 2: METADATA + DATA FETCH                     │
│  Input: Selected entities                           │
│  Operation: Fetch from MetadataSnapshot (cached)    │
│  Output: Complete entity metadata + data            │
│  Options:                                           │
│    - Include statistics? (for profiling)           │
│    - Include row data? (for export)                │
└─────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────┐
│  Stage 3: TOPOLOGICAL SORT                          │
│  Input: All selected entities (with FK metadata)    │
│  Operation: Build dependency graph, detect cycles   │
│  Output: Globally ordered list of entities          │
│  Handles:                                           │
│    - Cross-module dependencies                     │
│    - Circular dependencies (with config)           │
│    - Mixed static/regular entities                 │
└─────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────┐
│  Stage 4: EMISSION                                  │
│  Input: Sorted entities + emission strategy         │
│  Operation: Generate SQL scripts                    │
│  Output: Files organized by strategy                │
│  Strategies:                                        │
│    - Monolithic: One big file                      │
│    - Per-Module: One file per module               │
│    - Per-Entity: One file per entity + .sqlproj    │
└─────────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────────┐
│  Stage 5: INSERTION STRATEGY APPLICATION            │
│  Input: SQL scripts + insertion config              │
│  Operation: Format as INSERT, MERGE, etc.           │
│  Output: Deployment-ready SQL                       │
│  Options:                                           │
│    - INSERT (one-time load)                        │
│    - MERGE (upsert on redeploy)                    │
│    - Batch size, conflict handling                 │
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

## 🤔 Open Ontological Questions

### Q1: What IS an "entity with data"?

When we say "Bootstrap includes all entities with data", what does that mean?

**Option A**: Entity has static data defined in model JSON
**Option B**: Entity has rows in the live database
**Option C**: Entity is explicitly configured to include data
**Option D**: Some combination of above

**Current behavior**: Unclear, seems to be "entities where data was extracted"

**Proposed**: Make this explicit in EntitySelector:
```csharp
EntitySelector.FromModules(["ModuleA"])
              .WhereDataAvailable() // Only entities with data
```

---

### Q2: How do we unify "data sources"?

Entities can have data from:
- Inline in model JSON (static seeds)
- Extracted from live DB (bootstrap)
- Loaded from supplemental JSON files (ossys-user.json)

**Should these be**:
- **Option A**: Different pipelines (current state)
- **Option B**: Merged into one dataset before sorting
- **Option C**: Layered (static data as base, overlay live data)

**Proposed**: Merge into one dataset, with priority rules:
1. If entity has inline data, use that
2. Else if entity has extracted data, use that
3. Else if entity has supplemental data, use that

---

### Q3: What is the relationship between "static entity" and "has data"?

**Observation**:
- Static entities (`DataKind = 'staticEntity'`) are entities with reference/configuration data
- They typically have data defined in the model
- But regular entities can ALSO have data (extracted from live DB)

**Are these**:
- **Orthogonal**: Static-ness is a categorization, data availability is independent
- **Correlated**: Static entities always have data, regular entities usually don't
- **Definitional**: Static entities are DEFINED as "entities with data in model"

**Current code**: Treats them as separate concepts
**Proposed**: Keep them orthogonal, but provide convenience selectors

---

### Q4: How granular should EntitySelector be?

**Current**: Module-level selection with optional entity filter
**Proposed**: How flexible should this be?

**Option A - Simple**:
```csharp
EntitySelector.FromModules(["ModuleA", "ModuleB"])
```

**Option B - Per-Module Control**:
```csharp
EntitySelector
    .Include("ModuleA", all: true)
    .Include("ServiceCenter", only: ["User"])
```

**Option C - Predicate-Based**:
```csharp
EntitySelector
    .Where(e => e.IsStatic)
    .OrWhere(e => e.Module == "ServiceCenter" && e.Name == "User")
```

**Recommendation**: Start with Option B (per-module control), can evolve to Option C later if needed.

---

### Q5: Should topological sort be configurable per emission strategy?

**Current thought**: No - sort is always global, emission is just how we organize output

**But consider**:
- Per-module emission might WANT module-scoped sort (for module independence)
- But this breaks cross-module FK dependencies!

**Resolution**:
- Sort is ALWAYS global (correct semantics)
- Emission is ALWAYS a projection of the sorted list
- If per-module emission, we emit modules in dependency order, entities within each module in sorted order

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

- **EntitySelector**: Configuration for selecting which entities to include
- **EmissionStrategy**: How to organize output files (monolithic/per-module/per-entity)
- **InsertionStrategy**: How to apply data (INSERT/MERGE/etc.)
- **MetadataSnapshot**: Cached fetch of all metadata from OSSYS_* tables
- **AllEntities**: The union of all selected entities (static + regular + any others in scope)

---

## 🎬 Next Steps (Conceptual, Not Implementation)

### Before We Can Plan Execution

We need alignment on:

1. **The Three Dimensions**: Are Scope, Emission, Insertion the right ontology?
2. **The Unified Pipeline**: Does the 5-stage model make sense?
3. **Supplemental Elimination**: Agree that EntitySelector replaces supplemental concept?
4. **Topological Sort Scope**: Confirm it should ALWAYS span all selected entities?
5. **Metadata Primitive**: Is MetadataSnapshot the right abstraction?

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
