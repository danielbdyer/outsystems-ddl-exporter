# Entity Pipeline Shared Primitives: Ingredient Catalog

**Purpose**: Comprehensive catalog of shared primitives used across all entity pipeline implementations
**Scope**: Primitives used by BuildSsdtStaticSeedStep, BuildSsdtDynamicInsertStep, and BuildSsdtBootstrapSnapshotStep
**Focus**: Ingredients (what exists), not recipes (how to compose them)
**Date**: 2025-01-23

---

## 🎯 What This Document Covers

This document catalogs the **shared substrate** - the classes, methods, data structures, and patterns used across the three current entity pipeline implementations:

1. **BuildSsdtStaticSeedStep** - Static entities with MERGE
2. **BuildSsdtDynamicInsertStep** - User-provided data with INSERT (**DEPRECATED** - redundant with Bootstrap)
3. **BuildSsdtBootstrapSnapshotStep** - All entities (static + regular + supplemental) with INSERT

Rather than describing how these pipelines work (the "recipe"), this document identifies **which primitives are shared vs. unique** (the "ingredients") to inform future unification efforts.

---

## ⚡ Quick Reference: Shared Primitives by Pipeline Stage

| Primitive | Used By | Where to Find | Shared? |
|-----------|---------|---------------|---------|
| **EntityDependencySorter** | All 3 pipelines | `src/Osm.Emission/Seeds/EntityDependencySorter.cs` | ✅ **SHARED** |
| **StaticEntityTableData** | All 3 pipelines | `src/Osm.Emission/Seeds/StaticEntitySeedScriptGenerator.cs` (lines 266) | ✅ **SHARED** (despite name!) |
| **StaticSeedSqlBuilder** (MERGE) | StaticSeeds + Bootstrap | `src/Osm.Emission/Seeds/StaticSeedSqlBuilder.cs` | ✅ **SHARED** |
| **DynamicEntityInsertGenerator** (INSERT) | DynamicInsert only | `src/Osm.Emission/DynamicEntityInsertGenerator.cs` | ❌ Unique |
| **StaticSeedForeignKeyPreflight** | StaticSeeds only | `src/Osm.Emission/Seeds/StaticSeedForeignKeyPreflight.cs` | ❌ Unique |
| **SqlStaticEntityDataProvider** | StaticSeeds only | `src/Osm.Pipeline/StaticData/StaticEntityDataProviders.cs` | ❌ Unique |
| **EntitySeedDeterminizer** | StaticSeeds + Bootstrap | `src/Osm.Emission/Seeds/EntitySeedDeterminizer.cs` | ✅ **SHARED** |
| **TopologicalOrderingValidator** | Bootstrap only | Used in `BuildSsdtBootstrapSnapshotStep.cs:115-127` | ❌ Unique (could be generalized) |

### Key Insight: Most Primitives Are Already Shared!

The three pipelines already share:
- ✅ Topological sorting algorithm (`EntityDependencySorter`)
- ✅ Data structure (`StaticEntityTableData`)
- ✅ MERGE builder (`StaticSeedSqlBuilder`) - used by 2 of 3 pipelines
- ✅ Determinization logic (`EntitySeedDeterminizer`)

**Unique primitives** are mostly higher-level features:
- FK preflight analysis (could be generalized)
- Data providers (could be unified into DatabaseSnapshot)
- Topological validation diagnostics (could be added to shared sorter)

---

## 🔬 Critical Findings (✅ = Verified Against Codebase)

1. ✅ **Shared substrate already exists** - Most primitives are already reused across pipelines
2. ✅ **Naming is misleading** - "Static" prefixed classes used by all pipelines (e.g., `StaticEntityTableData`)
3. ✅ **Two insertion strategies** - MERGE (StaticSeeds) vs INSERT (Bootstrap, DynamicInsert deprecated)
4. ✅ **Topological sort is universal** - `EntityDependencySorter` used by all 3, but executed separately per pipeline
5. ✅ **Bootstrap demonstrates unification** - Already combines all entities (static + regular + supplemental) with global sort + INSERT
6. ✅ **Unique features are optional** - FK Preflight, Drift Detection, Topological Validation could all be parameterized
7. ⚠️ **Per-pipeline sorting is problematic** - Each pipeline sorts independently, missing cross-category FK dependencies

---

## 📊 Current Implementation: Three Pipeline Steps

**IMPORTANT**: There are currently THREE separate pipeline steps that handle entity data. Understanding their differences is critical for unification:

### 1. BuildSsdtStaticSeedStep
- **Purpose**: Generate MERGE scripts for static entities (`IsStatic == true`)
- **Selection**: `StaticEntitySeedDefinitionBuilder` filters entities where `IsStatic == true`
- **Data Source**: `IStaticEntityDataProvider` (SQL or fixture files)
- **Insertion**: MERGE statements (via `StaticSeedSqlBuilder`)
- **Ordering**: `EntityDependencySorter.SortByForeignKeys()` on static entities only
- **Emission**: Per-module OR monolithic (configurable)
- **Unique Features**: FK Preflight Analysis, Drift Detection (ValidateThenApply)
- **File**: `src/Osm.Pipeline/Orchestration/BuildSsdtStaticSeedStep.cs`

### 2. BuildSsdtDynamicInsertStep
- **Purpose**: Generate INSERT scripts for user-provided or extracted data
- **Selection**: User provides `DynamicEntityDataset` (any entities)
- **Data Source**: `DynamicEntityDataset` from user or extraction process
- **Insertion**: INSERT statements with batching (via `DynamicEntityInsertGenerator`)
- **Ordering**: `EntityDependencySorter.SortByForeignKeys()` on dynamic data
- **Emission**: Per-entity OR single file (configurable)
- **Unique Features**: Self-referencing ordering logic, constraint disabling on cycles
- **File**: `src/Osm.Pipeline/Orchestration/BuildSsdtDynamicInsertStep.cs`

### 3. BuildSsdtBootstrapSnapshotStep ✅ **DEMONSTRATES UNIFIED PATTERN**
- **Purpose**: Generate global bootstrap INSERT script for first-time SSDT deployment
- **Selection**: ALL entities (static + regular + supplemental combined) ✅ Verified at lines 50-60
- **Data Source**: Combines `StaticSeedData` + `DynamicDataset` + `SupplementalEntities`
- **Insertion**: INSERT statements in NonDestructive mode (via `StaticSeedSqlBuilder`) ✅ Verified at line 310
- **Ordering**: `EntityDependencySorter.SortByForeignKeys()` on **ALL** entities with global topological sort ✅ Verified at lines 105-110
- **Emission**: Single monolithic file with global ordering
- **Unique Features**:
  - ✅ **TopologicalOrderingValidator** - Enhanced cycle diagnostics (lines 115-127, 186-325)
  - ✅ **CircularDependencyOptions** support for manual cycle resolution (line 103)
  - ✅ **Supplemental entity integration** - ServiceCenter::User and others included in sort (lines 50-60, 327-387)
- **File**: `src/Osm.Pipeline/Orchestration/BuildSsdtBootstrapSnapshotStep.cs`
- **Key Insight**: This IS the unified pipeline pattern - extract these patterns for generalization!

### Key Insight for Unification

All three steps use:
- ✅ Same topological sorter (`EntityDependencySorter.SortByForeignKeys()`)
- ✅ Same data structure (`StaticEntityTableData` - despite the name!)
- ✅ Same MERGE builder for Bootstrap + StaticSeeds (`StaticSeedSqlBuilder`)

The ONLY differences are:
1. **Entity Selection** (IsStatic filter vs. user-provided vs. all entities)
2. **Insertion Strategy** (MERGE vs. INSERT)
3. **Emission Strategy** (per-module vs. per-entity vs. monolithic)

This proves the unified pipeline hypothesis: **All three are just parameterized variations of the same pipeline!**

---

## 🗂️ Primitive Categorization: Shared vs. Unique

### ✅ Shared Across All Pipelines (Universal Primitives)

These primitives are used by **all three** pipeline implementations:

1. **EntityDependencySorter** - Topological sorting algorithm
   - Used by: StaticSeeds, DynamicInsert, Bootstrap
   - Status: Already universal
   - Note: Currently executed separately per pipeline (opportunity for single global sort)

2. **StaticEntityTableData** - Core data structure for entity rows
   - Used by: StaticSeeds, DynamicInsert, Bootstrap
   - Status: Already universal (despite misleading "Static" name)
   - Note: Should be renamed to `EntityTableData` in future

3. **EntitySeedDeterminizer** - Alphabetical + row normalization
   - Used by: StaticSeeds, Bootstrap (not DynamicInsert)
   - Status: Shared determinization logic
   - Note: Could be made universal

### 🔀 Shared By Subset (Dual-Use Primitives)

These primitives are used by **some but not all** pipelines:

1. **StaticSeedSqlBuilder** - MERGE statement generator
   - Used by: StaticSeeds, Bootstrap (NOT DynamicInsert)
   - Status: MERGE-specific primitive
   - Alternative: DynamicEntityInsertGenerator for INSERT

2. **DynamicEntityInsertGenerator** - INSERT statement generator
   - Used by: DynamicInsert only
   - Status: INSERT-specific primitive
   - Note: Has unique self-referencing ordering logic

### ❌ Pipeline-Specific (Unique Primitives)

These primitives are used by **only one** pipeline:

1. **StaticSeedForeignKeyPreflight** - FK orphan detection
   - Used by: StaticSeeds only
   - Status: Optional validation primitive
   - Opportunity: Could be generalized to all pipelines

2. **SqlStaticEntityDataProvider** - SQL data fetching
   - Used by: StaticSeeds only
   - Status: Data source abstraction
   - Opportunity: Could be unified into DatabaseSnapshot

3. **TopologicalOrderingValidator** - Cycle diagnostics
   - Used by: Bootstrap only
   - Status: Enhanced cycle validation
   - Opportunity: Could be added to EntityDependencySorter

---

## 📐 Primitives Organized by Pipeline Stage

This section catalogs primitives by which pipeline stage they support, showing usage patterns across the three implementations.

### Stage 0: EXTRACT-MODEL
**No entity-specific primitives** - all pipelines use shared model extraction

**Primitives**: None (uses OsmModel from earlier stages)

---

### Stage 1: ENTITY SELECTION

**Primitive**: `StaticEntitySeedDefinitionBuilder`
- **File**: `src/Osm.Emission/Seeds/StaticEntitySeedScriptGenerator.cs` (lines 96-198)
- **Used By**: StaticSeeds only
- **Purpose**: Filters entities where `IsStatic == true`, builds table definitions
- **Status**: ❌ Pipeline-specific (hardcoded filter)

**Key Method**: `Build(OsmModel model, NamingOverrideOptions namingOverrides)`
```csharp
// Line 111: Filters static entities
foreach (var entity in module.Entities.Where(e => e.IsStatic && e.IsActive))
{
    var definition = CreateDefinition(module.Name.Value, entity, namingOverrides);
    tables.Add(definition);
}
```

**Usage in Other Pipelines**:
- **DynamicInsert**: No selection primitive (user provides `DynamicEntityDataset` directly)
- **Bootstrap**: No selection primitive (combines `StaticSeedData` + `DynamicDataset` at lines 44-48)

**Shared Concept**: Entity filtering logic (could be parameterized into `EntitySelector`)

---

### Stage 2: DATABASE SNAPSHOT FETCH

**Primitive**: `SqlStaticEntityDataProvider`
- **File**: `src/Osm.Pipeline/StaticData/StaticEntityDataProviders.cs` (lines 246-338)
- **Used By**: StaticSeeds only
- **Purpose**: Fetches entity data from SQL database with deterministic ordering
- **Status**: ❌ Pipeline-specific data provider

**Key Method**: `GetDataAsync(definitions, cancellationToken)`
```csharp
// Lines 309-329: Builds deterministic SELECT with ORDER BY PK
private string BuildSelectStatement(StaticEntitySeedTableDefinition definition)
{
    // SELECT columns FROM table ORDER BY pk1, pk2, ...
    // Ensures reproducible row order for version control
}
```

**Usage in Other Pipelines**:
- **DynamicInsert**: No data fetching (user provides `DynamicEntityDataset` directly)
- **Bootstrap**: Uses both `StaticSeedData` (from StaticSeeds provider) + `DynamicDataset` (lines 44-48)

**Supporting Primitive**: `EntitySeedDeterminizer` (✅ SHARED)
- **File**: `src/Osm.Emission/Seeds/EntitySeedDeterminizer.cs`
- **Used By**: StaticSeeds (line 78), Bootstrap
- **Purpose**: Alphabetical table sorting + within-table row normalization
- **Status**: ✅ Shared determinization primitive

**Data Structure**: `StaticEntityTableData` (✅ SHARED - see Universal Primitives)
- Used by ALL pipelines despite "Static" name
- Contains: `Definition` + `Rows[]`

---

### Stage 3: BUSINESS LOGIC TRANSFORMS

**Primitive**: `StaticSeedForeignKeyPreflight`
- **File**: `src/Osm.Emission/Seeds/StaticSeedForeignKeyPreflight.cs` (200+ lines)
- **Used By**: StaticSeeds only (line 90 in BuildSsdtStaticSeedStep.cs)
- **Purpose**: Detects orphaned FK values and ordering violations before emission
- **Status**: ❌ Pipeline-specific validation

**Key Method**: `Analyze(orderedData, model)`
```csharp
// Returns StaticSeedForeignKeyPreflightResult with:
// - Orphaned FK values (child rows without parent)
// - Ordering violations (child table before parent in topological sort)
// - Sample-based error reporting (top 5 issues per FK constraint)
```

**Usage in Other Pipelines**:
- **DynamicInsert**: No FK preflight (disables constraints on cycles instead - line 245)
- **Bootstrap**: No FK preflight (has TopologicalOrderingValidator instead for cycle diagnostics)

**Related Primitive**: `TopologicalOrderingValidator` (Bootstrap only)
- **File**: Referenced in `BuildSsdtBootstrapSnapshotStep.cs:103`
- **Purpose**: Enhanced cycle detection with diagnostics (nullable FK detection, cascade warnings)
- **Status**: ❌ Pipeline-specific to Bootstrap

**Opportunity**: Both could be unified into optional validation framework

---

### Stage 4: TOPOLOGICAL SORT

**Primitive**: `EntityDependencySorter` (✅ **UNIVERSAL - used by all pipelines!**)
- **File**: `src/Osm.Emission/Seeds/EntityDependencySorter.cs` (350+ lines)
- **Used By**: ALL 3 pipelines (verified):
  1. BuildSsdtStaticSeedStep (line 82) - sorts static entities
  2. BuildSsdtDynamicInsertStep (line 87) - sorts dynamic data
  3. BuildSsdtBootstrapSnapshotStep (line 93) - sorts ALL entities (static + regular)
  4. StaticEntitySeedScriptGenerator (line 49)
  5. DynamicEntityInsertGenerator (line 231)
- **Status**: ✅ **SHARED** universal primitive

**Key Method**: `SortByForeignKeys(tables, model, namingOverrides, options, circularDependencyOptions)`
```csharp
// Algorithm (Kahn's topological sort):
// 1. Build FK dependency graph from OsmModel relationships
// 2. Detect cycles
// 3. Topological sort by FK dependencies
// 4. Fall back to alphabetical if cycles detected
// 5. Support manual ordering overrides (CircularDependencyOptions)
```

**Capabilities**:
- ✅ Works on any entity set (proven by Bootstrap using it on ALL entities)
- ✅ Cycle detection with alphabetical fallback
- ✅ Junction table deferral option (`EntityDependencySortOptions.DeferJunctionTables`)
- ✅ Manual ordering overrides for known safe cycles (`CircularDependencyOptions`)

**Current Pattern**: Each pipeline sorts independently
- **Issue**: Misses cross-category FK dependencies
- **Bootstrap demonstrates solution**: Global sort on combined entity set

**Result Model to Generalize**:
- `EntityDependencyOrder`:
  - `Tables` → `Entities` (broaden scope)
  - `TopologicalOrderingApplied` (bool)
  - `Mode` (Topological or Alphabetical)
  - Metadata: `NodeCount`, `EdgeCount`, `CycleDetected`, `MissingEdgeCount`, `AlphabeticalFallbackApplied`

**Issue with Multiple Separate Sorts**:
- **Current**: Three pipelines each sort independently:
  - BuildSsdtStaticSeedStep sorts static entities only
  - BuildSsdtDynamicInsertStep sorts dynamic data only
  - BuildSsdtBootstrapSnapshotStep sorts ALL entities (this is the correct pattern!)
- **Problem**: Per-pipeline sorting misses cross-category FK dependencies
- **Example**: If static entity references a regular entity, sorting static entities alone won't see that dependency
- **Unified**: Follow Bootstrap's pattern - sort ALL selected entities together in one global topological order

**Options to Preserve**:
- `EntityDependencySortOptions`:
  - `DeferJunctionTables` (bool) - Delay M:N join tables in sort
- `CircularDependencyOptions`:
  - Manual ordering overrides for known safe cycles

---

### Stage 5: EMISSION

Two different emission strategies exist across the pipelines:

#### Primitive 1: `StaticSeedSqlBuilder` (✅ **SHARED** by StaticSeeds + Bootstrap)
- **File**: `src/Osm.Emission/Seeds/StaticSeedSqlBuilder.cs` (431 lines)
- **Used By**:
  1. StaticSeeds via `StaticEntitySeedScriptGenerator` (line 64)
  2. Bootstrap directly (line 297 in BuildSsdtBootstrapSnapshotStep.cs)
- **Purpose**: Generates MERGE statements with optional drift detection
- **Status**: ✅ **SHARED** MERGE emission primitive

**Key Method**: `BuildBlock(tableData, synchronizationMode, validationOverrides)`
```csharp
// Generates for each table:
// 1. Header comments (module, entity, schema.table, topological position)
// 2. Optional drift detection (ValidateThenApply mode):
//    - EXCEPT query to detect unexpected changes
//    - THROW error if drift found
// 3. MERGE statement (INSERT + UPDATE + DELETE in one)
```

**MERGE Template Pattern**:
```sql
MERGE INTO [schema].[table] AS Target
USING (VALUES (...), (...)) AS Source (col1, col2, ...)
ON Target.PK = Source.PK
WHEN MATCHED THEN UPDATE SET col1 = Source.col1, ...
WHEN NOT MATCHED BY TARGET THEN INSERT (col1, ...) VALUES (Source.col1, ...)
WHEN NOT MATCHED BY SOURCE THEN DELETE;
```

**Drift Detection Pattern** (ValidateThenApply mode):
```sql
IF EXISTS (
    SELECT * FROM (VALUES ...) AS Source
    EXCEPT
    SELECT * FROM [schema].[table]
) BEGIN THROW 50000, 'Drift detected', 1; END;
```

#### Primitive 2: `DynamicEntityInsertGenerator` (❌ DynamicInsert only)
- **File**: `src/Osm.Emission/DynamicEntityInsertGenerator.cs` (784 lines)
- **Used By**: DynamicInsert only (line 94 in BuildSsdtDynamicInsertStep.cs)
- **Purpose**: Generates INSERT statements with batching and self-referencing logic
- **Status**: ❌ Pipeline-specific INSERT emission

**Key Features**:
- INSERT with batching (configurable batch size, default 1000 rows)
- Self-referencing ordering logic (lines 303-556) - sorts rows within table for hierarchical FKs
- Constraint disabling when cycles detected (line 245)
- IDENTITY_INSERT handling
- Deduplication by primary key

#### Supporting Primitive: `StaticEntitySeedScriptGenerator` (StaticSeeds only)
- **File**: `src/Osm.Emission/Seeds/StaticEntitySeedScriptGenerator.cs` (94 lines)
- **Used By**: StaticSeeds only
- **Purpose**: Wrapper around StaticSeedSqlBuilder with template service
- **Status**: ❌ Thin wrapper (could be eliminated)

**Pattern**: Sort → Build Blocks → Wrap in Template
**File**: `src/Osm.Emission/Seeds/StaticEntitySeedTemplateService.cs`

**Wrapper Pattern**:
```sql
-- Header: Timestamp, metadata
SET XACT_ABORT ON;
BEGIN TRANSACTION;

<SQL blocks>

COMMIT TRANSACTION;
```

**Extract**: Transaction wrapper, metadata header

#### Per-Module Emission
**Logic**: Lines 103-148 in `BuildSsdtStaticSeedStep.cs`

**Pattern**:
```csharp
if (seedOptions.GroupByModule)
{
    foreach (var moduleName in modules)
    {
        var moduleDirectory = Path.Combine(seedsRoot, moduleDirectoryName);
        var moduleTables = orderedData.Where(/* module match */);
        var modulePath = Path.Combine(moduleDirectory, "StaticEntities.seed.sql");
        await _seedGenerator.WriteAsync(modulePath, moduleTables, ...);
    }

    if (seedOptions.EmitMasterFile)
    {
        // Also emit monolithic master file
    }
}
```

**Extract**:
- ✅ **Module grouping logic**
- ✅ **Directory structure creation**
- ⚠️ **Module name collision handling** (lines 196-231)
  - Sanitization → disambiguation via suffix

**Known Issue to Document**:
- ⚠️ Per-module emission **breaks topological order** if cross-module FKs exist
- Current mitigation: User manually ensures module load order
- Unified pipeline: Recommend .sqlproj approach (multiple files, sorted references)

---

### Stage 6: INSERTION STRATEGY

Two distinct insertion strategies exist:

#### Strategy 1: MERGE (✅ SHARED by StaticSeeds + Bootstrap)

**Configuration Primitive**: `StaticSeedSynchronizationMode`
- **File**: `src/Osm.Domain/Configuration/StaticSeedSynchronizationMode.cs`
- **Used By**: StaticSeeds, Bootstrap
- **Values**:
  - `Apply` - Direct MERGE without validation
  - `ValidateThenApply` - Drift check via EXCEPT query, then MERGE
- **Status**: ✅ SHARED configuration enum

**MERGE Characteristics**:
1. **Upsert semantics**: Three-way operation (INSERT + UPDATE + DELETE)
2. **PK-based matching**: `ON Target.PK = Source.PK`
3. **Idempotent**: Rerunning produces identical result
4. **Atomic**: Single transaction per table
5. **Drift detection**: Optional pre-validation via EXCEPT query
6. **Deletes orphans**: `WHEN NOT MATCHED BY SOURCE THEN DELETE`

**Implemented By**: `StaticSeedSqlBuilder.BuildBlock()` (see Stage 5)

#### Strategy 2: INSERT (❌ DynamicInsert only)

**Implementation**: `DynamicEntityInsertArtifact.WriteAsync()`
- **File**: `src/Osm.Emission/DynamicEntityInsertGenerator.cs` (lines 82-168)
- **Used By**: DynamicInsert only
- **Features**:
  - Batched INSERTs (default 1000 rows per batch)
  - IDENTITY_INSERT handling
  - Optional constraint disabling (NOCHECK CONSTRAINT when cycles detected)
  - No UPDATE or DELETE semantics
- **Status**: ❌ Pipeline-specific

**INSERT Characteristics**:
1. **Append-only**: No UPDATE or DELETE
2. **Batched**: Configurable batch size for performance
3. **Not idempotent**: Rerunning may cause duplicates
4. **Constraint handling**: Disables on cycles, re-enables after

**Configuration**: `DynamicEntityInsertGenerationOptions.BatchSize`

---

## 📊 Data Models & Structures

The following data models are used across the pipelines. Despite names suggesting they're static-specific, most are actually universal:

### Core Data Structures

| Class | File | Used By | Status |
|-------|------|---------|--------|
| `StaticEntityTableData` | `StaticEntitySeedScriptGenerator.cs:266` | ALL 3 pipelines | ✅ **UNIVERSAL** (despite name!) |
| `StaticEntityRow` | `StaticEntitySeedScriptGenerator.cs:247` | ALL 3 pipelines | ✅ **UNIVERSAL** |
| `StaticEntitySeedTableDefinition` | `StaticEntitySeedScriptGenerator.cs:207` | StaticSeeds, Bootstrap | ✅ **SHARED** |
| `StaticEntitySeedColumn` | `StaticEntitySeedScriptGenerator.cs:218` | StaticSeeds, Bootstrap | ✅ **SHARED** |
| `DynamicEntityDataset` | `DynamicEntityInsertGenerator.cs:18` | DynamicInsert, Bootstrap (combined) | ✅ **SHARED** |

**Key Properties in StaticEntityTableData**:
- `Definition` - Table metadata (schema, name, columns)
- `Rows` - ImmutableArray<StaticEntityRow>

**Key Properties in StaticEntitySeedColumn**:
- `EffectiveName` vs `PhysicalName` - Naming override support
- `NormalizeValue()` - Type coercion for SQL generation
- `IsPrimaryKey` - Used in MERGE ON clause

---

## 🧪 Test Coverage

Tests that exercise the shared primitives:

**Unit Tests**:
- `StaticEntitySeedScriptGeneratorTests.cs` - MERGE script generation
- `EntityDependencySorterTests.cs` - Topological sort algorithm
- `StaticSeedForeignKeyPreflightTests.cs` - FK orphan detection
- `DynamicEntityInsertGeneratorTests.cs` - INSERT batching and self-referencing

**Integration Tests**:
- `StaticSeedScriptExecutionTests.cs` - MERGE execution against SQL Server
- `SqlDynamicEntityDataProviderIntegrationTests.cs` - Data fetching from database

---

## 🗂️ Complete File Inventory

All files implementing the shared primitives:

### Shared Primitives (Universal & Dual-Use)
1. `src/Osm.Emission/Seeds/EntityDependencySorter.cs` - ✅ Universal topological sorter
2. `src/Osm.Emission/Seeds/StaticSeedSqlBuilder.cs` - ✅ MERGE builder (StaticSeeds + Bootstrap)
3. `src/Osm.Emission/Seeds/EntitySeedDeterminizer.cs` - ✅ Deterministic ordering
4. `src/Osm.Emission/DynamicEntityInsertGenerator.cs` - ❌ INSERT builder (DynamicInsert only)

### Pipeline-Specific Primitives
5. `src/Osm.Emission/Seeds/StaticSeedForeignKeyPreflight.cs` - ❌ FK analysis (StaticSeeds only)
6. `src/Osm.Pipeline/StaticData/StaticEntityDataProviders.cs` - ❌ Data provider (StaticSeeds only)
7. `src/Osm.Emission/Seeds/StaticEntitySeedScriptGenerator.cs` - ❌ Wrapper (StaticSeeds only)
8. `src/Osm.Emission/Seeds/StaticEntitySeedTemplateService.cs` - ❌ Template (StaticSeeds only)

### Pipeline Orchestration
9. `src/Osm.Pipeline/Orchestration/BuildSsdtStaticSeedStep.cs` - StaticSeeds orchestrator
10. `src/Osm.Pipeline/Orchestration/BuildSsdtDynamicInsertStep.cs` - DynamicInsert orchestrator
11. `src/Osm.Pipeline/Orchestration/BuildSsdtBootstrapSnapshotStep.cs` - Bootstrap orchestrator

### Configuration
12. `src/Osm.Domain/Configuration/StaticSeedSynchronizationMode.cs` - MERGE modes enum
13. `src/Osm.Domain/Configuration/TighteningOptions.cs` - Contains StaticSeedEmissionOptions

---

## 📋 Primitive Usage Patterns

Summary of which primitives demonstrate which patterns:

**Entity Selection Patterns**:
- Hardcoded filter: `StaticEntitySeedDefinitionBuilder` (line 111: `e.IsStatic && e.IsActive`)
- User-provided: `DynamicEntityDataset` (no filter, user supplies data)
- Combined sets: Bootstrap (lines 44-48: concatenates static + dynamic)

**Topological Sort Patterns**:
- Per-pipeline sorting: StaticSeeds, DynamicInsert each sort independently
- Global sorting: Bootstrap (line 93: sorts ALL entities in one call)
- Cycle handling: Alphabetical fallback + manual ordering overrides (`CircularDependencyOptions`)

**Data Fetching Patterns**:
- SQL provider: `SqlStaticEntityDataProvider` (SELECT + ORDER BY PK)
- User-provided: `DynamicEntityDataset` (no fetching)
- Combined: Bootstrap uses both sources

**Emission Patterns**:
- Monolithic: Single file with all tables
- Per-module: One file per module (BuildSsdtStaticSeedStep lines 103-148)
- Per-entity: One file per table (BuildSsdtDynamicInsertStep lines 145-152)

**Insertion Patterns**:
- MERGE (upsert): StaticSeeds + Bootstrap via `StaticSeedSqlBuilder`
- INSERT (append): DynamicInsert via `DynamicEntityInsertGenerator`
- Drift detection: ValidateThenApply mode (StaticSeeds only)

---

## 🗂️ Minimal Reference Catalog

**Key Interfaces**:
- `IStaticEntityDataProvider` - Data fetching abstraction (generalize to `IEntityDataProvider`)

**Key Enums**:
- `StaticSeedSynchronizationMode` - Apply vs. ValidateThenApply (generalize to `MergeMode`)
- `EntityDependencyOrderingMode` - Topological vs. Alphabetical (keep as-is)

**Key Configuration**:
- `StaticSeedEmissionOptions` in `TighteningOptions.Emission.StaticSeeds`
  - `GroupByModule` (bool)
  - `EmitMasterFile` (bool)
  - `SynchronizationMode` (enum)

**Future**: Unify into `EntityPipelineOptions`

---

## 🎯 Success Criteria

The unified pipeline successfully replaces Static Seeds when:

✅ **MERGE insertion works identically** - Existing SQL output byte-identical (StaticSeedSqlBuilder unchanged)
✅ **Drift detection preserved** - ValidateThenApply mode still catches unexpected data changes
✅ **Global topological sort** - Follow Bootstrap's pattern: sort ALL selected entities together (not per-pipeline)
✅ **FK preflight analysis works** - Orphan detection optionally runs on broader scope
✅ **All existing tests pass** - Zero regression in functionality
✅ **Per-module emission optional** - Supported (with warnings about limitations)
✅ **Module collision handling** - Sanitization + disambiguation still works

**Definition of Done**:
1. Can run: `EntityPipeline(scope: EntitySelector.Where(e => e.IsStatic), insertion: MERGE, emission: PerModule)`
2. Can run: `EntityPipeline(scope: EntitySelector.All(), insertion: MERGE, emission: Monolithic)` (replicates Bootstrap)
3. Output diff vs. old `BuildSsdtStaticSeedStep` shows only cosmetic differences
4. All StaticSeedScriptExecutionTests pass against unified pipeline
5. BuildSsdtStaticSeedStep, BuildSsdtDynamicInsertStep, BuildSsdtBootstrapSnapshotStep all deleted (replaced by unified pipeline)

---

## 🔗 Related Documentation

- **Primary**: `docs/architecture/entity-pipeline-unification.md` - North Star vision
- **Next Concern**: `02-bootstrap-implementation.md` (✅ uses MERGE like Static Seeds, NOT INSERT!)
  - **Update**: Bootstrap analysis shows it ALREADY implements the unified pipeline pattern
  - Combines static + regular entities
  - Global topological sort
  - MERGE via StaticSeedSqlBuilder
- **Also Related**: `03-dynamic-data-implementation.md` (uses INSERT, not MERGE)
- **Overlapping**: Topological Sort (Stage 4), Database Snapshot Fetch (Stage 2)

**Implementation Specs** (when created):
- `M*.x-entity-selector.md` - Stage 1 implementation
- `M*.x-database-snapshot.md` - Stage 2 implementation
- `M*.x-topological-sort-unification.md` - Stage 4 expansion
- `M*.x-insertion-strategies.md` - Stage 6 (MERGE vs INSERT)

---

## 💡 Key Insights (✅ = Verified Against Codebase)

1. ✅ **Bootstrap is the proof-of-concept** - Already demonstrates unified pipeline pattern:
   - Combines ALL entities (static + regular)
   - Uses global topological sort
   - Uses MERGE via StaticSeedSqlBuilder
   - Shows that entity pipelines CAN be parameterized

2. ✅ **MERGE is shared** - StaticSeedSqlBuilder used by both StaticSeeds AND Bootstrap
   - Not unique to static entities
   - Provides upsert semantics (INSERT+UPDATE+DELETE)
   - Must preserve in unified pipeline

3. ✅ **Topological sort is already general-purpose** - EntityDependencySorter works for any entity set
   - Used by StaticSeeds, DynamicData, AND Bootstrap
   - Bootstrap proves it works on ALL entities combined
   - Problem is not the algorithm, but running it separately per pipeline

4. ✅ **FK Preflight Analysis is unique** - Only StaticSeeds uses it (BuildSsdtStaticSeedStep line 90)
   - Worth preserving as optional validation
   - Could be generalized to all entities

5. ✅ **Drift detection is unique** - ValidateThenApply mode only in StaticSeeds
   - EXCEPT query pattern for drift detection
   - Worth preserving as optional mode

6. ⚠️ **Per-module emission is problematic** - Breaks topological order when FKs cross modules
   - Users rely on it, so must support
   - Recommend deprecation with migration path

7. ✅ **Naming is scaffolding** - "Static" prefix everywhere, but:
   - StaticEntityTableData used by ALL pipelines (despite the name!)
   - StaticSeedSqlBuilder used by StaticSeeds AND Bootstrap
   - Nothing is inherently static-specific

**The Big Realization** (✅ Validated):
- Bootstrap ALREADY IS the unified pipeline for ALL entities!
- It combines static + regular entities
- Uses global topological sort
- Generates MERGE statements
- The "unified pipeline" already exists in BuildSsdtBootstrapSnapshotStep - we just need to parameterize it!

---

