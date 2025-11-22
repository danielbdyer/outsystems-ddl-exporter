# Static Seeds: Implementation Extraction Guide

**Purpose**: Guide for extracting Static Seeds logic into unified Entity Pipeline
**Current State**: Specialized pipeline for entities where `DataKind = 'staticEntity'`
**Future State**: Unified `EntityPipeline(scope: IsStatic, insertion: MERGE, emission: Monolithic)`
**Date**: 2025-01-XX

---

## ⚡ Quick Reference: What You Need from Static Seeds

When implementing the unified pipeline, you'll need:

| Pipeline Stage | What to Extract | Where to Find It | Why |
|----------------|-----------------|------------------|-----|
| **Stage 1: Selection** | `DataKind == "staticEntity"` filter | `StaticEntitySeedDefinitionBuilder` | Shows how to identify static entities |
| **Stage 2: Fetch** | SELECT + ORDER BY pattern | `SqlStaticEntityDataProvider` lines 309-329 | Deterministic data retrieval |
| **Stage 3: Transforms** | FK orphan detection | `StaticSeedForeignKeyPreflight.Analyze()` | Deferred FK constraint logic |
| **Stage 4: Sort** | Topological sort algorithm | `EntityDependencySorter.SortByForeignKeys()` | Must expand scope to ALL entities |
| **Stage 5: Emission** | MERGE statement template | `StaticSeedSqlBuilder.BuildBlock()` lines 32-260 | MERGE structure for InsertionStrategy |
| **Stage 5: Emission** | Drift detection pattern | `StaticSeedSqlBuilder.BuildBlock()` lines 86-100 | Optional validation before apply |
| **Stage 6: Insertion** | MERGE semantics | `StaticSeedSynchronizationMode` enum + SQL | INSERT+UPDATE+DELETE in one statement |

**Critical Realizations**:
1. **Static Seeds is NOT special** - it's just `EntityPipeline` with specific parameters
2. **MERGE logic is valuable** - Bootstrap/DynamicData use INSERT; preserve both
3. **Topological sort is BROKEN** - currently static-only; must expand to ALL entities
4. **Drift detection is unique** - preserve this as optional validation mode
5. **Per-module emission is problematic** - breaks topological order with cross-module FKs

---

## 🎯 What to Extract & Preserve

When building the unified pipeline, **extract these behaviors** from Static Seeds:

### ✅ MUST Preserve
1. **MERGE-based synchronization** (INSERT + UPDATE + DELETE in one statement)
2. **Drift detection** (ValidateThenApply mode - detect unexpected data changes before applying)
3. **FK preflight analysis** (orphan detection, ordering validation)
4. **Deterministic ordering** (alphabetical + topological for reproducibility)
5. **Per-module emission option** (though problematic, users rely on it)
6. **Module name collision handling** (sanitization + disambiguation)

### ⚠️ Transform (Don't Copy)
1. **Topological sort scoping** - Currently static-only; expand to ALL selected entities
2. **Data provider abstraction** - Merge into unified `DatabaseSnapshot.Fetch()`
3. **Hardcoded `DataKind` filter** - Replace with `EntitySelector.Where(e => e.IsStatic)`

### ❌ Discard (Scaffolding)
1. **Separate pipeline step** - Becomes parameterized `EntityPipeline` execution
2. **Static-specific state classes** - Unify with general entity pipeline state
3. **Duplicate topological sort** - One global sort replaces all category-specific sorts

---

## 📐 How Static Seeds Informs Each Pipeline Stage

### Stage 0: EXTRACT-MODEL
**No Static Seeds touchpoints** - uses shared model extraction

**For Unified Pipeline**: No changes needed

---

### Stage 1: ENTITY SELECTION

**Current Logic**: Hardcoded filter in `StaticEntitySeedDefinitionBuilder.Build()`
```csharp
// Current: Implicit filter
var staticEntities = model.Entities.Where(e => e.DataKind == "staticEntity")
```

**Extract → Unified**:
```csharp
// Future: Explicit EntitySelector
EntitySelector.Where(e => e.IsStatic)
// Or: EntitySelector.FromModules(...).Where(e => e.IsStatic)
```

**Touchpoints**:
- `StaticEntitySeedDefinitionBuilder` (line 36 in `BuildSsdtStaticSeedStep.cs`)
  - **Extract**: Entity filtering logic
  - **File**: Referenced but not shown in codebase survey
  - **Preserve**: The concept of filtering by `DataKind`
  - **Transform**: Make it a parameter, not hardcoded

---

### Stage 2: DATABASE SNAPSHOT FETCH

**Current Logic**: `SqlStaticEntityDataProvider` fetches data per-entity

**Class**: `SqlStaticEntityDataProvider`
**File**: `src/Osm.Pipeline/StaticData/StaticEntityDataProviders.cs` (lines 246-338)

**Key Method to Extract**:
```csharp
// Line 257-307: Data fetching pattern
public async Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
    IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
    CancellationToken cancellationToken)
{
    // For each entity:
    //   1. Build SELECT statement (lines 309-329)
    //   2. Execute query
    //   3. Normalize values (line 290: column.NormalizeValue)
    //   4. Package as StaticEntityTableData
}
```

**Extract for Unified Pipeline**:
- ✅ **Per-entity SELECT pattern** (line 309: `BuildSelectStatement`)
- ✅ **ORDER BY PK** logic (lines 317-327) - ensures deterministic row order
- ✅ **Value normalization** (line 290: `column.NormalizeValue()`)
- ✅ **Result packaging** (line 296: `StaticEntityTableData.Create()`)

**Merge Into**: `DatabaseSnapshot.Fetch()` in unified pipeline
- Use same SELECT + ORDER BY pattern
- Normalize values consistently
- Cache results (avoid triple-fetch)

**Supporting Determinization**:
- `EntitySeedDeterminizer.Normalize()` (lines 78 in `BuildSsdtStaticSeedStep.cs`)
- **Extract**: Alphabetical table sorting + within-table row sorting
- **File**: `src/Osm.Emission/Seeds/EntitySeedDeterminizer.cs`
- **Preserve**: Deterministic ordering for version control stability

---

### Stage 3: BUSINESS LOGIC TRANSFORMS

**Current Logic**: FK Preflight Analysis

**Class**: `StaticSeedForeignKeyPreflight`
**File**: `src/Osm.Emission/Seeds/StaticSeedForeignKeyPreflight.cs` (200+ lines)

**Key Method to Extract**:
```csharp
public static StaticSeedForeignKeyPreflightResult Analyze(
    IReadOnlyList<StaticEntityTableData> orderedData,
    OsmModel? model)
{
    // Detects:
    // 1. Orphaned FK values (child rows without parent)
    // 2. Ordering violations (child table before parent in sort)
}
```

**Extract for Unified Pipeline**:
- ✅ **Orphan FK detection** - Generalize to all entities (not just static)
- ✅ **Ordering validation** - Verify topological sort correctness
- ✅ **Issue reporting structure** - `StaticSeedForeignKeyIssue` model
  - Properties: `ChildSchema`, `ChildTable`, `ChildColumn`, `ReferencedSchema`, `ReferencedTable`, `ConstraintName`, `SampleOrphanValue`

**Becomes**: Part of "Deferred FK Constraints" transform (Stage 3)
- Detect orphans → Decide whether to add `WITH NOCHECK` or fail
- Integrate with UAT-users orphan detection (similar pattern)

**Logging Pattern to Preserve**:
- `LogForeignKeyPreflight()` (lines 233-267 in `BuildSsdtStaticSeedStep.cs`)
- Sample-based error reporting (top 5 issues)
- Metadata: orphan count, violation count

---

### Stage 4: TOPOLOGICAL SORT

**Current Logic**: `EntityDependencySorter` - Static entities only

**Class**: `EntityDependencySorter`
**File**: `src/Osm.Emission/Seeds/EntityDependencySorter.cs` (350+ lines)

**Key Method to Extract**:
```csharp
public static EntityDependencyOrder SortByForeignKeys(
    IReadOnlyList<StaticEntityTableData> tables,
    OsmModel? model,
    NamingOverrides? namingOverrides = null,
    EntityDependencySortOptions? options = null)
{
    // 1. Build FK dependency graph
    // 2. Detect cycles
    // 3. Topological sort
    // 4. Fall back to alphabetical if cycles
}
```

**Extract for Unified Pipeline**:
- ✅ **Graph building algorithm** - Reuse for ALL entities (not just static)
- ✅ **Cycle detection** - Preserve this safety check
- ✅ **Alphabetical fallback** - When cycles prevent sort
- ✅ **Junction table deferral** - `DeferJunctionTables` option (lines 79-80 in `BuildSsdtStaticSeedStep.cs`)

**Result Model to Generalize**:
- `EntityDependencyOrder`:
  - `Tables` → `Entities` (broaden scope)
  - `TopologicalOrderingApplied` (bool)
  - `Mode` (Topological or Alphabetical)
  - Metadata: `NodeCount`, `EdgeCount`, `CycleDetected`

**Critical Issue to Fix**:
- **Current**: Sorts only static entities (misses cross-category FKs)
- **Unified**: MUST sort ALL selected entities together
- **Example**: Static entity references regular entity → current sort misses this

**Options to Preserve**:
- `EntityDependencySortOptions`:
  - `DeferJunctionTables` (bool) - Delay M:N join tables in sort

---

### Stage 5: EMISSION

**Current Logic**: Script generation with multiple strategies

**Classes to Extract**:

#### Script Generator
**Class**: `StaticEntitySeedScriptGenerator`
**File**: `src/Osm.Emission/Seeds/StaticEntitySeedScriptGenerator.cs` (129 lines)

**Key Pattern**:
```csharp
public string Generate(tables, synchronizationMode, model, validationOverrides)
{
    // 1. Sort tables (line 49)
    // 2. Build SQL blocks for each (line 64)
    // 3. Wrap in template (line 67)
}
```

**Extract**:
- ✅ **Block-based emission** - Generate SQL per table, concatenate
- ✅ **Template wrapper** - Header/footer with transaction
- ✅ **UTF-8 no BOM** encoding (line 15)

#### SQL Block Builder
**Class**: `StaticSeedSqlBuilder`
**File**: `src/Osm.Emission/Seeds/StaticSeedSqlBuilder.cs` (431 lines)

**Key Method** (lines 32-260):
```csharp
public string BuildBlock(
    StaticEntityTableData tableData,
    StaticSeedSynchronizationMode synchronizationMode,
    ModuleValidationOverrides? validationOverrides)
{
    // Generates:
    // - Header comments (module, entity, schema.table)
    // - MERGE statement (or drift check + MERGE)
}
```

**MERGE Template to Extract** (crucial for Stage 6):
```sql
MERGE INTO [schema].[table] AS Target
USING (VALUES (...), (...)) AS Source (col1, col2, ...)
ON Target.PK = Source.PK
WHEN MATCHED THEN UPDATE SET col1 = Source.col1, ...
WHEN NOT MATCHED BY TARGET THEN INSERT (col1, ...) VALUES (Source.col1, ...)
WHEN NOT MATCHED BY SOURCE THEN DELETE;
```

**Drift Detection Pattern to Preserve** (lines 86-100):
```sql
IF EXISTS (
    SELECT col1, col2 FROM (VALUES ...) AS Source
    EXCEPT
    SELECT col1, col2 FROM [schema].[table] AS Existing
)
BEGIN
    THROW 50000, 'Drift detected', 1;
END;
```

**Extract for Unified Pipeline**:
- ✅ **MERGE statement structure** - Core of `InsertionStrategy.Merge()`
- ✅ **Drift detection** - Optional validation mode
- ✅ **Header comment format** - Module/entity metadata
- ✅ **Validation override integration** - Config-driven tweaks

#### Template Service
**Class**: `StaticEntitySeedTemplateService`
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

### Stage 6: INSERTION STRATEGY APPLICATION

**Current Logic**: MERGE synchronization modes

**Enum**: `StaticSeedSynchronizationMode`
**File**: `src/Osm.Domain/Configuration/StaticSeedSynchronizationMode.cs`

**Values**:
- `Apply` - Direct MERGE
- `ValidateThenApply` - Drift check, then MERGE

**Extract for Unified Pipeline**:
```csharp
// Future InsertionStrategy
InsertionStrategy.Merge(
    mode: MergeMode.Upsert,  // INSERT + UPDATE + DELETE
    validateFirst: true       // Drift detection
)

// vs.

InsertionStrategy.Insert(
    batchSize: 1000,
    mode: InsertMode.BulkInsert
)
```

**MERGE Characteristics to Preserve**:
1. **Three-way operation**: INSERT + UPDATE + DELETE in one statement
2. **PK-based matching**: `ON Target.PK = Source.PK`
3. **Idempotent**: Rerunning produces same result
4. **Atomic**: Single transaction
5. **Drift detection**: Optional pre-check via EXCEPT

---

## 🔍 Complete Touchpoint Reference

> **Note**: Below is the exhaustive catalog of all classes, methods, and files.
> Use this for finding existing code during extraction, not as code to copy directly.

### Orchestration (DO NOT COPY - Extract patterns only)

**Class**: `BuildSsdtStaticSeedStep`
**File**: `src/Osm.Pipeline/Orchestration/BuildSsdtStaticSeedStep.cs` (287 lines)

**What to Extract**:
- Lines 79-86: **Topological sort options pattern** (defer junction tables)
- Lines 90-91: **FK preflight analysis integration point**
- Lines 103-148: **Per-module emission logic** (if preserving)
- Lines 196-231: **Module name collision handling** (ResolveModuleDirectoryName)

**What NOT to Copy**:
- The step itself (becomes parameterized pipeline execution)
- State management (use unified pipeline state)
- Hardcoded sequence (use stage-based architecture)

---

## 📊 Data Models (Generalize for Unified Pipeline)

**Current Static-Specific Models** → **Future Generalized Models**

| Current Class | File | Generalize To | Key Insight |
|---------------|------|---------------|-------------|
| `StaticEntitySeedTableDefinition` | `src/Osm.Emission/Seeds/` | `EntityMetadata` | Remove "Static" prefix, same structure works for all entities |
| `StaticEntityTableData` | `src/Osm.Emission/Seeds/` | `EntityDataSet` | Metadata + rows pattern applies universally |
| `StaticEntityRow` | `src/Osm.Emission/Seeds/` | `EntityRow` or `object?[]` | Simple value array, no specialization needed |
| `StaticEntitySeedColumn` | `src/Osm.Emission/Seeds/` | `ColumnMetadata` | Column definition pattern reusable |

**Key Properties to Preserve**:
- `EffectiveName` vs `PhysicalName` (naming overrides support)
- `NormalizeValue()` method (type coercion for INSERT/MERGE generation)
- `IsPrimaryKey` flag (needed for MERGE ON clause)

**What to Avoid**:
- Don't create parallel model hierarchies (Static vs. Regular vs. Supplemental)
- Use one unified model structure, parameterize by `EntitySelector`

---

## 🧪 Test Infrastructure (Where to Find Examples)

**Unit Tests** - Pattern examples for unified pipeline:
- `StaticEntitySeedScriptGeneratorTests.cs` - Script generation patterns
- `EntityDependencySorterTests.cs` - Topological sort test cases
- `StaticSeedForeignKeyPreflightTests.cs` - FK orphan detection patterns

**Integration Tests** - End-to-end behavior to preserve:
- `StaticSeedScriptExecutionTests.cs` - MERGE execution against live database
- `SqlDynamicEntityDataProviderIntegrationTests.cs` - Data fetching patterns

**Use these tests to**:
1. Understand expected behaviors when extracting code
2. Build regression suite for unified pipeline
3. Verify MERGE semantics are preserved

---

## 🗂️ Complete File Inventory

**Extraction Priority** (files to study when building unified pipeline):

### 🔴 Critical - Study First
1. `src/Osm.Emission/Seeds/EntityDependencySorter.cs` - Topological sort (expand scope)
2. `src/Osm.Emission/Seeds/StaticSeedSqlBuilder.cs` - MERGE template (preserve)
3. `src/Osm.Pipeline/StaticData/StaticEntityDataProviders.cs` - Data fetching (unify)
4. `src/Osm.Emission/Seeds/StaticSeedForeignKeyPreflight.cs` - FK analysis (generalize)

### 🟡 Important - Extract Patterns
5. `src/Osm.Emission/Seeds/StaticEntitySeedScriptGenerator.cs` - Emission orchestration
6. `src/Osm.Emission/Seeds/EntitySeedDeterminizer.cs` - Deterministic ordering
7. `src/Osm.Pipeline/Orchestration/BuildSsdtStaticSeedStep.cs` - Module collision handling (lines 196-231)

### 🟢 Reference - Supporting Details
8. `src/Osm.Emission/Seeds/StaticEntitySeedTemplateService.cs` - Template wrapper
9. `src/Osm.Domain/Configuration/StaticSeedSynchronizationMode.cs` - Enum values
10. `src/Osm.Domain/Configuration/TighteningOptions.cs` - `StaticSeedEmissionOptions` nested class

**Test Files** (use for regression coverage):
- `tests/Osm.Emission.Tests/StaticEntitySeedScriptGeneratorTests.cs`
- `tests/Osm.Emission.Tests/EntityDependencySorterTests.cs`
- `tests/Osm.Emission.Tests/StaticSeedForeignKeyPreflightTests.cs`
- `tests/Osm.Etl.Integration.Tests/StaticSeedScriptExecutionTests.cs`

---

## 🚀 Implementation Strategy: Extracting for Unified Pipeline

When building the unified pipeline, follow this extraction strategy:

### Phase 1: Understand & Document
1. **Read the 4 critical files** (see File Inventory above)
2. **Run existing tests** to understand expected behaviors
3. **Compare Static Seeds vs. Bootstrap** to identify common patterns
4. **Map current classes to future stages** (use Quick Reference table)

### Phase 2: Extract Reusable Logic
1. **EntityDependencySorter** → Generalize to work with ANY entity list (not just static)
   - Remove hardcoded static filter
   - Expand to handle cross-category FKs (static ↔ regular)
   - Preserve: Cycle detection, alphabetical fallback, junction table deferral

2. **StaticSeedSqlBuilder** → Extract MERGE template
   - Preserve: MERGE structure (INSERT+UPDATE+DELETE)
   - Preserve: Drift detection (EXCEPT query pattern)
   - Generalize: Don't hardcode "static", make it work for any entity

3. **SqlStaticEntityDataProvider** → Merge into DatabaseSnapshot
   - Preserve: SELECT + ORDER BY PK pattern
   - Preserve: Value normalization logic
   - Unify: One fetch for metadata + data (avoid triple-fetch)

4. **StaticSeedForeignKeyPreflight** → Generalize to all entities
   - Preserve: Orphan detection algorithm
   - Preserve: Sample-based error reporting
   - Expand: Work with UAT-users orphan detection (similar pattern)

### Phase 3: Build Unified Abstractions
1. **EntitySelector.Where(e => e.IsStatic)** replaces hardcoded filter
2. **InsertionStrategy.Merge()** encapsulates MERGE logic
3. **DatabaseSnapshot** unifies data fetching
4. **EmissionStrategy** supports both monolithic and per-module

### Phase 4: Migrate Incrementally
1. **Keep Static Seeds working** during migration (don't break production)
2. **Run both pipelines in parallel** initially (old + new)
3. **Compare outputs** (diff generated SQL files)
4. **Deprecate old pipeline** only after unified pipeline proven
5. **Delete scaffolding** (BuildSsdtStaticSeedStep, etc.)

### Key Risks to Manage
⚠️ **Topological sort expansion** - Most critical change
- Current: Static entities only (misses cross-category FKs)
- Future: ALL selected entities together
- Risk: May discover new FK violations previously hidden
- Mitigation: Run FK preflight analysis first, warn operator

⚠️ **MERGE semantics preservation** - Must not break drift detection
- Test thoroughly with existing integration tests
- Verify DELETE behavior (WHEN NOT MATCHED BY SOURCE)
- Ensure idempotence (rerunning produces same result)

⚠️ **Per-module emission** - Known problem, but users rely on it
- Document limitations (breaks topological order)
- Recommend .sqlproj approach instead
- Provide migration path for existing users

---

## 📋 Cross-Reference: Related Concerns

When working on other concerns, refer back to Static Seeds for:

**From Entity Selection perspective**:
- Static Seeds shows `DataKind == "staticEntity"` filter pattern
- Example of per-module entity selection

**From Topological Sort perspective**:
- Static Seeds has working sort algorithm (but scoped wrong)
- Cycle detection + alphabetical fallback pattern

**From Database Snapshot perspective**:
- Static Seeds shows deterministic data fetching pattern
- Value normalization requirements

**From Emission Strategies perspective**:
- Static Seeds has both monolithic and per-module emission
- Module name collision handling

**From Insertion Strategies perspective**:
- Static Seeds is the ONLY place with MERGE logic
- Drift detection pattern unique to Static Seeds

**From Bootstrap perspective**:
- Bootstrap uses INSERT, Static Seeds uses MERGE
- Same pipeline structure, different parameters (proves unification thesis)

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

✅ **MERGE insertion works identically** - Existing SQL output byte-identical
✅ **Drift detection preserved** - ValidateThenApply mode still catches unexpected data changes
✅ **Topological sort expanded** - Now covers ALL entities (not just static)
✅ **FK preflight analysis works** - Orphan detection runs on broader scope
✅ **All existing tests pass** - Zero regression in functionality
✅ **Per-module emission optional** - Supported (with warnings about limitations)
✅ **Module collision handling** - Sanitization + disambiguation still works

**Definition of Done**:
1. Can run: `EntityPipeline(scope: EntitySelector.Where(e => e.IsStatic), insertion: MERGE)`
2. Output diff vs. old `BuildSsdtStaticSeedStep` shows only cosmetic differences
3. All StaticSeedScriptExecutionTests pass against unified pipeline
4. BuildSsdtStaticSeedStep deleted (no longer needed)

---

## 🔗 Related Documentation

- **Primary**: `docs/architecture/entity-pipeline-unification.md` - North Star vision
- **Next Concern**: `02-bootstrap-implementation.md` (similar pattern, INSERT instead of MERGE)
- **Overlapping**: Topological Sort (Stage 4), Database Snapshot Fetch (Stage 2)

**Implementation Specs** (when created):
- `M*.x-entity-selector.md` - Stage 1 implementation
- `M*.x-database-snapshot.md` - Stage 2 implementation
- `M*.x-topological-sort-unification.md` - Stage 4 expansion
- `M*.x-insertion-strategies.md` - Stage 6 (MERGE vs INSERT)

---

## 💡 Key Insights

1. **Static Seeds is proof of concept** - Shows that entity pipelines can be parameterized
2. **MERGE is valuable** - Only place with upsert logic, must preserve
3. **Topological sort is broken** - Scoped to static-only, misses cross-category FKs
4. **Drift detection is unique** - No other pipeline has this, worth preserving
5. **Per-module emission is problematic** - Breaks ordering, recommend deprecation
6. **Triple-fetch is wasteful** - Static Seeds fetches separately from Bootstrap/Profile
7. **Naming is scaffolding** - "Static" prefix everywhere, but nothing is inherently static-specific

**The Big Realization**: If you removed all "Static" prefixes and made the `DataKind` filter a parameter, this pipeline would work for ANY entity set. That's the unified pipeline.

---

