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

## 🎛️ Configuration as Parameterization: The Unification Mechanism

The three dimensions aren't abstract concepts - they're **implemented via configuration primitives** that already exist in the codebase.

### The Configuration Primitives ✅ Already Exist

**Critical Insight**: The mechanism for parameterizing the unified pipeline is ALREADY BUILT. We don't need to design it from scratch.

| Dimension | Configuration Primitive | Location | What It Controls |
|-----------|------------------------|----------|------------------|
| **SCOPE** | `ModuleFilterOptions` | `src/Osm.Domain/Configuration/ModuleFilterOptions.cs` | Which entities to process |
| | `EntityFilters` ✅ | `ModuleFilterOptions.cs:32, 116-153` | **Per-module, per-entity selection** |
| | `IncludeSystemModules` | `ModuleFilterOptions.cs:28` | Include/exclude system modules |
| | `IncludeInactiveModules` | `ModuleFilterOptions.cs:30` | Include/exclude inactive modules |
| **EMISSION** | `EmissionStrategy` (implicit) | Via `GroupByModule`, `EmitMasterFile` flags | How to organize output files |
| | `NamingOverrideOptions` | `src/Osm.Domain/Configuration/NamingOverrideOptions.cs` | Table/column name remapping |
| **INSERTION** | `StaticSeedSynchronizationMode` | `src/Osm.Emission/Seeds/StaticSeedSqlBuilder.cs` | MERGE vs INSERT behavior |
| | `DynamicEntityInsertGenerationOptions` | `src/Osm.Emission/DynamicEntityInsertGenerator.cs` | Batch size, INSERT options |
| **CROSS-CUTTING** | `CircularDependencyOptions` | `src/Osm.Domain/Configuration/CircularDependencyOptions.cs` | Manual cycle resolution |
| | `TighteningOptions` | `src/Osm.Domain/Configuration/TighteningOptions.cs` | Nullability, validation overrides |
| | `ModuleValidationOverrides` | `src/Osm.Domain/Configuration/ModuleValidationOverrides.cs` | Per-module validation rules |

### EntityFilters: The Existing Primitive ✅ (Incomplete Implementation)

**Discovery**: `ModuleFilterOptions.EntityFilters` provides the exact capability described in the North Star vision.

**What Exists** (`ModuleFilterSectionReader.cs:65-78`):
```json
{
  "model": {
    "modules": [
      {
        "name": "ServiceCenter",
        "entities": ["User", "Tenant"]  // ← Per-entity selection!
      },
      {
        "name": "MyModule",
        "entities": []  // ← All entities from module
      }
    ]
  }
}
```

**What Works**:
- ✅ Configuration parsing (JSON → `ModuleFilterOptions`)
- ✅ Module filtering in `ModuleFilterRunner` (`PipelineBootstrapper.cs:119`)
- ✅ Entity list validation

**What's Incomplete** ✅ Verified:
- ⚠️ **SQL query filtering not implemented** - `extract-model` command doesn't respect EntityFilters
- ⚠️ **Validation issues cascade** - Filtering ServiceCenter to `["User"]` still validates ALL ServiceCenter entities
- ⚠️ **Supplemental mechanism still needed** - EntityFilters exists but not fully integrated end-to-end

**Unification Implication**:
- EntityFilters is the RIGHT primitive (correct API shape)
- BUT requires completion: SQL query generation, validation scope, full pipeline integration
- Supplemental is NOT redundant yet - it's a workaround for EntityFilters' incomplete implementation
- Once EntityFilters is complete, Supplemental becomes obsolete

### Configuration Flow: CLI → Pipeline

The configuration system demonstrates how parameterization translates to pipeline execution:

```
CLI Arguments / appsettings.json
        ↓
CliConfigurationLoader (parses JSON sections)
        ↓
BuildSsdtRequestAssembler (assembles pipeline request)
        ↓
ModuleFilterOptions + TighteningOptions + CircularDependencyOptions + ...
        ↓
PipelineBootstrapper (applies filters, loads model)
        ↓
BuildSsdtPipeline (executes stages with config-driven behavior)
```

**Key Files**:
- `CliConfiguration.cs` - Root configuration object
- `ModuleFilterSectionReader.cs` - Parses `model.modules` section
- `TighteningSectionReader.cs` - Parses `tightening` section
- `BuildSsdtRequestAssembler.cs` - Translates config → pipeline request
- `PipelineBootstrapper.cs` - Executes config-driven filtering/loading

### Functional Composition: The Unification Strategy

**Pattern Discovery**: The codebase already uses functional composition for type-safe pipeline orchestration.

**`IBuildSsdtStep<TInput, TOutput>` Interface** (`IBuildSsdtStep.cs:7-10`):
```csharp
public interface IBuildSsdtStep<in TInput, TNextState>
{
    Task<Result<TNextState>> ExecuteAsync(TInput state, CancellationToken cancellationToken = default);
}
```

**Monadic Chaining** (`BuildSsdtPipeline.cs:83-95`):
```csharp
var finalStateResult = await _bootstrapStep
    .ExecuteAsync(initialized, cancellationToken)
    .BindAsync((bootstrap, token) => _evidenceCacheStep.ExecuteAsync(bootstrap, token), cancellationToken)
    .BindAsync((decisions, token) => _emissionStep.ExecuteAsync(decisions, token), cancellationToken)
    // ... chain continues
```

**Why This Matters**:
- ✅ **Type safety**: Each step's output type matches next step's input type
- ✅ **Composability**: Steps can be reordered, skipped, or replaced
- ✅ **Early termination**: `Result<T>` monad short-circuits on failure
- ✅ **Functional purity**: Each step is a pure transformation (input state → output state)

**Extraction Pattern**: Bootstrap demonstrates the correct pattern. To unify:
1. Extract the pattern (IBuildSsdtStep interface, monadic chaining)
2. Make each stage configurable (driven by config primitives above)
3. Compose stages differently for different use cases (StaticSeeds vs Bootstrap vs future pipelines)

---

## 🔧 Stage -1: Request Assembly (Configuration → Primitives)

**Purpose**: Translate CLI arguments and configuration files into first-class pipeline primitives.

**Location**: `BuildSsdtRequestAssembler.cs`

**What It Does**:
1. **Loads Configuration** - Parses `appsettings.json` sections
2. **Resolves Dependencies** - Cross-references between config sections (e.g., profiling + tightening)
3. **Validates Configuration** - Ensures config is well-formed before pipeline starts
4. **Constructs Pipeline Request** - Creates `BuildSsdtPipelineRequest` with all options populated

**Key Primitives Built**:
- `ModuleFilterOptions` (from `model.modules` section)
- `TighteningOptions` (from `tightening` section)
- `ResolvedSqlOptions` (connection string, timeouts)
- `CircularDependencyOptions` (from `circularDependencies` config file)
- `NamingOverrideOptions` (from `namingOverrides` section)
- `SmoBuildOptions` (formatting, naming conventions)
- `EvidenceCacheOptions` (cache invalidation metadata)

**State Progression**:
```
CliArguments + appsettings.json
        ↓
BuildSsdtRequestAssemblerContext (raw config)
        ↓
BuildSsdtRequestAssembly (validated pipeline request)
        ↓
BuildSsdtPipelineRequest (ready for execution)
```

**Critical Files**:
- `BuildSsdtRequestAssembler.cs:52-100` - Main assembly logic
- `ModuleFilterSectionReader.cs:9-150` - Parses module filters
- `TighteningSectionReader.cs` - Parses tightening options
- `CacheMetadataBuilder.cs` - Creates cache fingerprint
- `EvidenceCacheOptionsFactory.cs` - Builds cache options

**Why Stage -1**:
- Happens BEFORE any pipeline execution
- Configuration is the FIRST parameterization point
- Shape of config should mirror shape of unified pipeline primitives

---

## 📦 Stage 0: Pipeline Bootstrapping

**Purpose**: Unified entry point that prepares the execution environment for all pipeline implementations.

**Location**: `PipelineBootstrapper.cs:49-143`

**What It Does**:
1. **Model Loading** - Load `OsmModel` from file or inline
2. **Module Filtering** - Apply `ModuleFilterOptions` to select entities
3. **Supplemental Loading** - Load additional entities (e.g., ServiceCenter::User)
4. **Profiling** - Capture data quality metrics (null counts, FK orphans, uniqueness)

**State Progression**:
```
BuildSsdtPipelineRequest
        ↓
ModelLoader → OsmModel (loaded from disk/inline)
        ↓
ModuleFilterRunner → OsmModel (filtered by modules)
        ↓
SupplementalLoader → OsmModel + SupplementalEntities
        ↓
ProfilerRunner → ProfileSnapshot (data quality metrics)
        ↓
PipelineBootstrapContext (ready for Stage 1)
```

**Critical Primitives**:
- `ModelLoader` - Handles model ingestion (file → `OsmModel`)
- `ModuleFilterRunner` - Applies `ModuleFilterOptions.EntityFilters` ⚠️ (incomplete)
- `SupplementalLoader` - Loads entities from JSON files (workaround until EntityFilters complete)
- `ProfilerRunner` - Coordinates profiling execution

**Profiling Integration** ✅ Critical for Stage 3:
- Profiling happens HERE (Stage 0), not during transforms
- Results stored in `ProfileSnapshot`
- Used by Stage 3 transforms (nullability tightening, deferred FKs, UAT-users detection)
- **Ordering matters**: Profile must run before transforms that depend on it

**Why Stage 0**:
- ALL pipelines (StaticSeeds, Bootstrap, future unified) start here
- This is where configuration becomes runtime state
- Output (`PipelineBootstrapContext`) is the common foundation

**Relationship to Extract-Model**:
- Extract-model is a SEPARATE verb/pipeline (`ExtractModelPipeline.cs`)
- Stage 0 LOADS an already-extracted model (from file or inline)
- **Future vision**: Integrate extract-model as a pre-Stage 0 step with caching
  - Extract model → cache to disk → Stage 0 loads cached model
  - Invalidate cache on schema change (checksum-based)

---

## 🧩 Stage 1: Entity Selection

**Purpose**: Determine which entities from the model will be processed by the pipeline.

**Input**: `OsmModel` (filtered by modules from Stage 0)
**Output**: Set of entities to process

### Conceptual Operation

This is not a binary "static vs. regular" - it's a **selection predicate** over the entity model.

**Conceptual Options**:
- **All entities from modules** (`modules: ["ModuleA", "ModuleB"]`)
- **Filtered entities per module** (`ModuleA: all, ServiceCenter: [User only]`)
- **Entities matching predicate** (`entity.IsStatic == true`)
- **Entities with available data** (`hasData(entity)`)
- **Custom combinations** (`static entities + ServiceCenter::User`)

### Current Implementations

1. **Static Seeds**: Hardcoded to `entity.IsStatic && entity.IsActive`
   - Location: `StaticEntitySeedDefinitionBuilder.Build()`
   - Scope: Only static entities

2. **Bootstrap**: Hardcoded to `allEntitiesWithData`
   - Combines: Static entities + Regular entities + Supplemental entities
   - Scope: Everything with data
   - ✅ Shows correct pattern (global selection)

3. **DynamicData** (DEPRECATED): User-provided dataset
   - No selection - entities explicitly provided by user

### Existing Primitive: EntityFilters ⚠️ (Incomplete)

**Discovery**: The primitive EXISTS but isn't fully implemented.

**Configuration** (`ModuleFilterOptions.EntityFilters`):
```csharp
public ImmutableDictionary<string, ModuleEntityFilterOptions> EntityFilters { get; }
```

**JSON Shape** (`ModuleFilterSectionReader.cs:65-78`):
```json
{
  "modules": [
    {
      "name": "ServiceCenter",
      "entities": ["User", "Tenant"]  // ← Per-entity selection
    }
  ]
}
```

**What Works**:
- ✅ Config parsing → `ModuleFilterOptions.EntityFilters`
- ✅ Runtime access via `ModuleFilterOptions.EntityFilters.TryGetValue(moduleName, out var filter)`

**What's Missing** ✅ Verified:
- ⚠️ **SQL query filtering** - `extract-model` doesn't respect EntityFilters when querying `OSSYS_*` tables
- ⚠️ **Validation scope** - Filtering `ServiceCenter` to `["User"]` still validates ALL ServiceCenter entities
- ⚠️ **Consistent application** - EntityFilters applied in some places (module filtering), ignored in others (SQL extraction)

**Gap Analysis**:
```
Where EntityFilters WORKS:
  ✅ ModuleFilterRunner (Stage 0) - filters in-memory OsmModel

Where EntityFilters DOESN'T WORK:
  ❌ SqlModelExtractionService - SQL queries fetch ALL entities from module
  ❌ Validation - validates ALL entities even when filtered
  ❌ Profiling - profiles ALL entities even when filtered
```

**Unification Path**:
1. Complete EntityFilters implementation (SQL queries, validation, profiling)
2. Deprecate Supplemental mechanism (redundant once EntityFilters complete)
3. Use EntityFilters as THE primitive for entity selection

**Current Workaround**: Supplemental entities loaded via JSON files because EntityFilters incomplete

---

## 🌟 The North Star Vision

### What We've Discovered

The unified pipeline isn't a future vision - **most of it already exists**:

**Configuration Primitives** ✅ Already Built:
- `ModuleFilterOptions` (scope control)
- `EntityFilters` (per-entity selection - incomplete implementation)
- `CircularDependencyOptions` (cycle resolution)
- `TighteningOptions` (transform configuration)
- `NamingOverrideOptions` (naming transforms)

**Orchestration Pattern** ✅ Already Built:
- `IBuildSsdtStep<TInput, TOutput>` interface
- Monadic chaining (`.BindAsync()`)
- Immutable state progression
- Type-safe composition

**Shared Primitives** ✅ Already Built:
- `EntityDependencySorter` (universal topological sort)
- `StaticEntityTableData` (universal data structure)
- `PipelineBootstrapper` (unified entry point)
- `BuildSsdtRequestAssembler` (config → primitives translation)

**Bootstrap Pattern** ✅ Already Demonstrated:
- Global entity selection (static + regular + supplemental)
- Global topological sort across all categories
- Configurable insertion (INSERT vs MERGE)
- Cycle diagnostics via `TopologicalOrderingValidator`

### What Success Looks Like

**Conceptual API** (what the unified pipeline SHOULD feel like):

```csharp
EntityPipeline.Execute(
    // Stage -1: Configuration
    config: new PipelineConfiguration
    {
        // Stage 0: Entity selection (driven by EntityFilters - needs completion)
        Scope = EntityFilters.ForModules(["ModuleA", "ModuleB"])
                            .Include("ServiceCenter", ["User"])  // ← EntityFilters API
                            .Where(e => e.IsStatic),  // ← Optional predicate

        // Stage 5: Emission strategy
        Emission = EmissionStrategy.Monolithic("output/seeds.sql"),
        // OR: EmissionStrategy.PerModule(outputDir, ".sqlproj ordering")

        // Stage 6: Insertion strategy
        Insertion = InsertionStrategy.Merge(conflictResolution: MergeOnPrimaryKey),
        // OR: InsertionStrategy.Insert(NonDestructive)

        // Stage 4: Topological sort (always global)
        CircularDependencies = CircularDependencyOptions.FromFile("cycles.json"),

        // Stage 3: Business logic transforms (profiling-informed)
        Transforms = new TransformOptions
        {
            Tightening = TighteningOptions.FromFile("tightening.json"),
            NamingOverrides = NamingOverrideOptions.FromFile("naming.json"),
            UatUsers = UatUsersOptions.FromInventories("qa-users.json", "uat-users.json")
        }
    },

    // Execution context
    connectionString: "Server=...",
    outputDirectory: "output/",
    cancellationToken: ct
);
```

**Key Characteristics**:
- ✅ **Composable** - Mix and match configurations for different use cases
- ✅ **Type-safe** - Compile-time guarantees via `IBuildSsdtStep` pattern
- ✅ **Functional** - Immutable configuration, pure transformations
- ✅ **Observable** - State progression visible via monadic chaining

### What Gets Unified

| Current Implementation | Becomes | Configuration |
|------------------------|---------|---------------|
| **StaticSeedStep** | `EntityPipeline.Execute()` | `Scope = IsStatic, Insertion = MERGE` |
| **BootstrapSnapshot** | `EntityPipeline.Execute()` | `Scope = All, Insertion = INSERT` |
| **DynamicData** | ❌ **DELETE** | Redundant with configurable scope |
| **Supplemental** | Part of `EntityFilters` | `Include("ServiceCenter", ["User"])` once EntityFilters complete |

### What Gets Completed

**EntityFilters** (exists but incomplete):
1. ✅ **Already works**: Config parsing, in-memory filtering
2. ⚠️ **Needs work**: SQL query filtering, validation scope, profiling scope
3. 🎯 **End state**: Fully integrated per-entity selection across entire pipeline

**Extraction Steps**:
1. Extract `IBuildSsdtStep` pattern from Bootstrap → configurable steps
2. Complete EntityFilters implementation (SQL, validation, profiling)
3. Parameterize StaticSeeds to use EntityFilters instead of hardcoded `IsStatic`
4. Deprecate Supplemental mechanism (redundant once EntityFilters complete)
5. Delete DynamicData (fully redundant)

### What Stays the Same

**Post-Emission Steps** - Already generic:
- `.sqlproj` generation
- SQL validation
- PostDeployment guards
- Telemetry packaging

**Business Logic Transforms** - Preserve existing behavior:
- Extract from current implementations (excavation, not design)
- Make independently runnable (observability)
- Drive via configuration (TighteningOptions, NamingOverrideOptions, etc.)

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

## 📋 Post-Emission Steps (Orthogonal to Entity Pipeline)

**Purpose**: Steps that happen AFTER entity data SQL generation but are part of the full deployment pipeline.

**Key Insight**: These steps are **orthogonal** to entity pipeline unification - they consume the output of Stage 6 but don't affect entity processing logic.

### Current Steps (`BuildSsdtPipeline.cs:88-94`)

**State Progression**:
```
... → StaticSeeds → DynamicInserts → BootstrapSnapshot →
    BuildSsdtSqlProjectStep →
    BuildSsdtPostDeploymentTemplateStep →
    BuildSsdtSqlValidationStep →
    BuildSsdtTelemetryPackagingStep
```

1. **BuildSsdtSqlProjectStep** (`BuildSsdtSqlProjectStep.cs`)
   - **Purpose**: Generate `.sqlproj` file that references all emitted SQL files
   - **Input**: List of emitted SQL file paths
   - **Output**: `.sqlproj` XML with ordered `<Build Include="..." />` entries
   - **Why it matters**: .sqlproj defines execution order for SSDT deployment

2. **BuildSsdtSqlValidationStep** (`BuildSsdtSqlValidationStep.cs`)
   - **Purpose**: Validate generated SQL syntax using SQL Server parser
   - **Input**: Emitted SQL files
   - **Output**: Validation report (syntax errors, warnings)
   - **Why it matters**: Catch SQL errors before deployment attempt

3. **BuildSsdtPostDeploymentTemplateStep** (`BuildSsdtPostDeploymentTemplateStep.cs`)
   - **Purpose**: Generate PostDeployment.sql with one-time execution guards
   - **Input**: List of seed/data scripts
   - **Output**: PostDeployment.sql that checks if data already exists
   - **Why it matters**: Prevents re-running bootstrap INSERTs on existing data

4. **BuildSsdtTelemetryPackagingStep** (`BuildSsdtTelemetryPackagingStep.cs`)
   - **Purpose**: Package execution metadata (fingerprints, timing, warnings)
   - **Input**: Pipeline execution log
   - **Output**: Telemetry JSON artifacts
   - **Why it matters**: Diagnostics, reproducibility, auditing

### Unification Implication

**These steps DON'T need unification** - they're already generic:
- ✅ They consume SQL files (don't care if from StaticSeeds, Bootstrap, or future unified pipeline)
- ✅ They're composable (can be included/excluded via pipeline configuration)
- ✅ They use the IBuildSsdtStep interface (already following orchestration pattern)

**Future consideration**: Treat as **plugins** - optional steps that can be enabled/disabled via configuration.

---

## ⚙️ Orchestration Mechanics: Functional Composition Pattern

**Purpose**: Understand HOW the pipeline stages compose together (the mechanism for unification).

### The IBuildSsdtStep Interface

**Definition** (`IBuildSsdtStep.cs:7-10`):
```csharp
public interface IBuildSsdtStep<in TInput, TNextState>
{
    Task<Result<TNextState>> ExecuteAsync(TInput state, CancellationToken cancellationToken = default);
}
```

**Key Properties**:
- **Generic over input AND output** - Each step declares its contract
- **Returns `Result<T>`** - Monad for error handling (success or failure, no exceptions)
- **Immutable state** - Each step produces NEW state, doesn't mutate input
- **Async** - Supports I/O-bound operations (database queries, file writes)

### State Progression Pattern

The pipeline is a **series of state transformations**:

```
PipelineInitialized
    ↓ (BuildSsdtBootstrapStep)
BootstrapCompleted
    ↓ (BuildSsdtEvidenceCacheStep)
EvidenceCacheCompleted
    ↓ (BuildSsdtPolicyDecisionStep)
PolicyDecisionCompleted
    ↓ (BuildSsdtEmissionStep)
EmissionCompleted
    ↓ (BuildSsdtStaticSeedStep)
StaticSeedsCompleted
    ↓ (BuildSsdtDynamicInsertStep)
DynamicInsertsCompleted
    ↓ (BuildSsdtBootstrapSnapshotStep)
BootstrapSnapshotCompleted
    ↓ (BuildSsdtPostDeploymentTemplateStep)
PostDeploymentCompleted
    ↓ (BuildSsdtTelemetryPackagingStep)
TelemetryPackagingCompleted
```

**Each state is a record**:
- Contains all data from previous stages (immutable accumulation)
- Adds new data from current stage
- Passed to next stage as input

**Example** (`BuildSsdtPipelineStates.cs`):
```csharp
public sealed record BootstrapCompleted(
    PipelineInitialized Initialized,  // ← Carries forward
    OsmModel FilteredModel,            // ← Added by bootstrap
    ProfileSnapshot Profile            // ← Added by bootstrap
);

public sealed record EvidenceCacheCompleted(
    BootstrapCompleted Bootstrap,      // ← Carries forward
    EvidenceCacheResult CacheResult    // ← Added by this stage
);
```

### Monadic Chaining (`BindAsync`)

**Implementation** (`BuildSsdtPipeline.cs:83-95`):
```csharp
var finalStateResult = await _bootstrapStep
    .ExecuteAsync(initialized, cancellationToken)
    .BindAsync((bootstrap, token) => _evidenceCacheStep.ExecuteAsync(bootstrap, token), cancellationToken)
    .BindAsync((policy, token) => _emissionStep.ExecuteAsync(policy, token), cancellationToken)
    // ... continues
    .ConfigureAwait(false);
```

**What `BindAsync` Does**:
- If previous step returns `Result.Success(value)` → calls next function with `value`
- If previous step returns `Result.Failure(errors)` → short-circuits, skips remaining steps
- Returns aggregated `Result<TFinal>` at the end

**Functional Programming Properties**:
- ✅ **Referential transparency** - Same input → same output
- ✅ **Composition** - Small functions compose into larger ones
- ✅ **Error propagation** - Failures bubble up automatically (no try/catch)
- ✅ **Type safety** - Compiler enforces correct state progression

### Why This Matters for Unification

**Bootstrap demonstrates the correct pattern**. To extract it:

1. **Identify shared stages**:
   - Stage 0 (Bootstrapping) - shared
   - Stage 1-6 (entity pipeline) - configurable
   - Post-emission steps - shared

2. **Parameterize the differences**:
   - Stage 1 (selection) - driven by `EntityFilters` config
   - Stage 5 (emission) - driven by `EmissionStrategy` config
   - Stage 6 (insertion) - driven by `InsertionMode` config

3. **Compose different pipelines**:
   ```csharp
   // StaticSeeds pipeline
   pipeline = bootstrapStep
       .BindAsync(StaticSeedsSelectionStep)  // ← filters to IsStatic
       .BindAsync(FetchDataStep)
       .BindAsync(TopologicalSortStep)
       .BindAsync(MergeEmissionStep);        // ← MERGE mode

   // Bootstrap pipeline
   pipeline = bootstrapStep
       .BindAsync(AllEntitiesSelectionStep)  // ← all entities
       .BindAsync(FetchDataStep)
       .BindAsync(TopologicalSortStep)
       .BindAsync(InsertEmissionStep);       // ← INSERT mode

   // Future unified pipeline
   pipeline = bootstrapStep
       .BindAsync(ConfigurableSelectionStep)  // ← driven by EntityFilters
       .BindAsync(FetchDataStep)
       .BindAsync(TopologicalSortStep)
       .BindAsync(ConfigurableEmissionStep);  // ← driven by config
   ```

**Key Insight**: The orchestration pattern ALREADY EXISTS. We don't need to design it - we need to **extract configurable steps** and **compose them via configuration**.

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
