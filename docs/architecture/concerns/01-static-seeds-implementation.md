# Static Seeds Implementation: Touchpoints & Ingredients

**Concern**: Static entity seed data generation and synchronization
**Current State**: Specialized pipeline for entities where `DataKind = 'staticEntity'`
**Future State**: Unified `EntityPipeline` with parameterized scope
**Date**: 2025-01-XX

---

## 📋 Overview

Static Seeds handles:
- Entities where `entity.DataKind == "staticEntity"` (compile-time optimizations, enhanced enums)
- MERGE-based synchronization (upsert on deployment)
- Optional per-module organization
- Topological sorting (FK dependencies within static entities only)
- Drift detection (ValidateThenApply mode)

**Output**: `StaticEntities.seed.sql` (monolithic or per-module)

---

## 🏗️ Core Pipeline Orchestration

### Primary Step

**Class**: `BuildSsdtStaticSeedStep`
**File**: `src/Osm.Pipeline/Orchestration/BuildSsdtStaticSeedStep.cs` (287 lines)
**Interface**: `IBuildSsdtStep<SqlValidated, StaticSeedsGenerated>`
**Responsibility**: Orchestrates static seed generation within build pipeline

**Key Methods**:
- `ExecuteAsync(SqlValidated state, CancellationToken)` → `Result<StaticSeedsGenerated>` (lines 25-194)
  - Builds seed definitions from model
  - Fetches static entity data via provider
  - Normalizes data (deterministic ordering)
  - Sorts by FK dependencies (topological)
  - Runs FK preflight analysis
  - Generates SQL scripts (monolithic or per-module)
  - Records execution metadata to pipeline log

- `ResolveModuleDirectoryName(...)` (lines 196-231)
  - Handles module name collisions
  - Applies sanitization
  - Generates disambiguation suffixes

- `LogForeignKeyPreflight(...)` (lines 233-267)
  - Logs orphan FK detection
  - Logs ordering violations

**Dependencies**:
- `StaticEntitySeedScriptGenerator` (script generation)
- `IStaticDataProvider` (data fetching)

**Future State**: → `EntityPipeline.Execute(scope: EntitySelector.Where(e => e.IsStatic), insertion: MERGE)`

---

## 📊 Data Models

### Seed Table Definition

**Class**: `StaticEntitySeedTableDefinition`
**File**: `src/Osm.Emission/Seeds/StaticEntitySeedTableDefinition.cs`
**Purpose**: Metadata for static entity table (schema, columns, module)

**Properties**:
- `Schema` (string) - Database schema (e.g., "dbo")
- `PhysicalName` (string) - Physical table name
- `LogicalName` (string) - OutSystems entity name
- `EffectiveName` (string) - Target deployment name (may differ from physical)
- `Module` (string) - OutSystems module name
- `Columns` (ImmutableArray\<StaticEntitySeedColumn\>) - Column definitions

**Built By**: `StaticEntitySeedDefinitionBuilder.Build(model, namingOverrides)`

**Future State**: → Part of unified `EntityMetadata` in `DatabaseSnapshot`

---

### Static Entity Table Data

**Class**: `StaticEntityTableData`
**File**: `src/Osm.Emission/Seeds/StaticEntityTableData.cs`
**Purpose**: Combines definition + actual row data

**Properties**:
- `Definition` (StaticEntitySeedTableDefinition) - Table metadata
- `Rows` (ImmutableArray\<StaticEntityRow\>) - Actual data rows

**Creation**: `StaticEntityTableData.Create(definition, rows)`

**Future State**: → `EntityDataSet` in unified `DatabaseSnapshot`

---

### Static Entity Row

**Class**: `StaticEntityRow`
**File**: `src/Osm.Emission/Seeds/StaticEntityRow.cs`
**Purpose**: Single row of static entity data

**Properties**:
- `Values` (object?[]) - Column values in definition order

**Creation**: `StaticEntityRow.Create(values)`

**Future State**: → Standard row representation in `EntityDataSet`

---

### Seed Column Definition

**Class**: `StaticEntitySeedColumn`
**File**: `src/Osm.Emission/Seeds/StaticEntitySeedColumn.cs`
**Purpose**: Column metadata for seed generation

**Properties**:
- `ColumnName` (string) - Physical column name
- `EffectiveColumnName` (string) - Target deployment name
- `DataType` (string) - OutSystems data type (e.g., "Integer", "Text")
- `IsPrimaryKey` (bool) - PK membership
- `NormalizeValue(object?)` → object? - Type coercion/normalization

**Future State**: → Part of unified `ColumnMetadata`

---

## 🔧 SQL Generation

### Script Generator

**Class**: `StaticEntitySeedScriptGenerator`
**File**: `src/Osm.Emission/Seeds/StaticEntitySeedScriptGenerator.cs` (129 lines)
**Purpose**: Generates complete `.seed.sql` file content

**Key Methods**:
- `Generate(tables, synchronizationMode, model, validationOverrides)` → string (lines 28-68)
  - Sorts tables by FK dependencies
  - Builds SQL blocks for each table
  - Applies template wrapper

- `WriteAsync(path, tables, synchronizationMode, model, validationOverrides, cancellationToken)` (lines 77-98)
  - Writes generated SQL to disk
  - UTF-8 no BOM encoding

**Dependencies**:
- `StaticEntitySeedTemplateService` (template wrapper)
- `StaticSeedSqlBuilder` (per-table SQL blocks)

**Future State**: → Part of unified emission strategy in `Stage 5: EMISSION`

---

### SQL Block Builder

**Class**: `StaticSeedSqlBuilder`
**File**: `src/Osm.Emission/Seeds/StaticSeedSqlBuilder.cs` (431 lines)
**Purpose**: Generates MERGE statement for single table

**Key Methods**:
- `BuildBlock(tableData, synchronizationMode, validationOverrides)` → string (lines 32-260)
  - Generates header comments (module, entity, schema.table)
  - Emits MERGE statement (if data present)
  - Applies synchronization mode logic
  - Handles validation overrides

**Synchronization Modes**:
- `StaticSeedSynchronizationMode.ValidateThenApply`:
  - Drift detection via `EXCEPT` query
  - Throws error if data differs
  - Then runs MERGE

- `StaticSeedSynchronizationMode.Apply`:
  - Direct MERGE without validation

**MERGE Structure**:
```sql
MERGE INTO [schema].[table] AS Target
USING (VALUES (...), (...)) AS Source (col1, col2, ...)
ON Target.PK = Source.PK
WHEN MATCHED THEN UPDATE SET ...
WHEN NOT MATCHED BY TARGET THEN INSERT ...
WHEN NOT MATCHED BY SOURCE THEN DELETE;
```

**Dependencies**:
- `SqlLiteralFormatter` (value escaping)
- `IdentifierFormatter` (SQL identifier quoting)

**Future State**: → Unified `InsertionStrategy.Merge()` in `Stage 6: INSERTION STRATEGY`

---

### Template Service

**Class**: `StaticEntitySeedTemplateService`
**File**: `src/Osm.Emission/Seeds/StaticEntitySeedTemplateService.cs`
**Purpose**: Wraps SQL blocks with standard header/footer

**Key Methods**:
- `ApplyBlocks(sqlContent)` → string
  - Adds file header comments
  - Sets transaction context
  - Wraps content
  - Adds footer

**Output Template**:
```sql
-- Static Entity Seed Data
-- Generated: <timestamp>
-- ...
SET XACT_ABORT ON;
BEGIN TRANSACTION;

<sqlContent>

COMMIT TRANSACTION;
```

**Future State**: → Part of emission formatting in `Stage 5: EMISSION`

---

## 🔍 Topological Sorting & Dependencies

### Entity Dependency Sorter

**Class**: `EntityDependencySorter`
**File**: `src/Osm.Emission/Seeds/EntityDependencySorter.cs` (350+ lines)
**Purpose**: Topological sort by FK dependencies

**Key Methods**:
- `SortByForeignKeys(tables, model, namingOverrides, options)` → `EntityDependencyOrder` (static)
  - Builds FK dependency graph
  - Detects cycles
  - Performs topological sort
  - Falls back to alphabetical if cycles detected

**Output**:
- `EntityDependencyOrder`:
  - `Tables` (ImmutableArray\<StaticEntityTableData\>) - Sorted tables
  - `TopologicalOrderingApplied` (bool) - True if sort succeeded
  - `Mode` (EntityDependencyOrderingMode) - Topological or Alphabetical
  - `NodeCount`, `EdgeCount`, `MissingEdgeCount`, `CycleDetected`, etc.

**Options**:
- `EntityDependencySortOptions`:
  - `DeferJunctionTables` (bool) - Delay M:N join tables

**Current Scope**: Static entities only (subset of full model)

**Future State**: → Unified `Stage 4: TOPOLOGICAL SORT` spanning ALL selected entities

---

### Entity Ordering Mode

**Enum**: `EntityDependencyOrderingMode`
**File**: `src/Osm.Emission/Seeds/EntityDependencyOrderingMode.cs`

**Values**:
- `Alphabetical` - Fallback when cycles prevent topological sort
- `Topological` - FK-based dependency ordering

**Future State**: → Part of unified sort result

---

### FK Preflight Analysis

**Class**: `StaticSeedForeignKeyPreflight`
**File**: `src/Osm.Emission/Seeds/StaticSeedForeignKeyPreflight.cs` (200+ lines)
**Purpose**: Detects FK issues before emission

**Key Methods**:
- `Analyze(orderedData, model)` → `StaticSeedForeignKeyPreflightResult` (static)
  - Checks for orphaned FKs (child rows without parent)
  - Detects ordering violations (child before parent)

**Result Model**:
- `StaticSeedForeignKeyPreflightResult`:
  - `MissingParents` (ImmutableArray\<StaticSeedForeignKeyIssue\>) - Orphan FK values
  - `OrderingViolations` (ImmutableArray\<StaticSeedForeignKeyIssue\>) - Sort failures
  - `HasFindings` (bool) - Any issues detected

**Issue Model**:
- `StaticSeedForeignKeyIssue`:
  - `ChildSchema`, `ChildTable`, `ChildColumn`
  - `ReferencedSchema`, `ReferencedTable`, `ReferencedColumn`
  - `ConstraintName`
  - `SampleOrphanValue` (object?)

**Future State**: → Part of `Stage 3: BUSINESS LOGIC TRANSFORMS` (deferred FK handling)

---

## 🗂️ Data Providers

### Interface

**Interface**: `IStaticEntityDataProvider`
**File**: `src/Osm.Pipeline/Application/IStaticDataProviderFactory.cs`
**Purpose**: Abstraction for fetching static entity data

**Method**:
- `GetDataAsync(definitions, cancellationToken)` → `Task<Result<IReadOnlyList<StaticEntityTableData>>>`

**Implementations**:

#### SQL Provider

**Class**: `SqlStaticEntityDataProvider`
**File**: `src/Osm.Pipeline/StaticData/StaticEntityDataProviders.cs` (lines 246-338)
**Purpose**: Fetches data from live SQL Server database

**Strategy**:
- `SELECT * FROM [schema].[table] ORDER BY PK` for each definition
- Uses `SqlConnectionFactory`
- Configurable command timeout

**Future State**: → Part of `Stage 2: DATABASE SNAPSHOT FETCH`

#### Fixture Provider

**Class**: `FixtureStaticEntityDataProvider`
**File**: `src/Osm.Pipeline/StaticData/StaticEntityDataProviders.cs` (lines 11-244)
**Purpose**: Loads data from JSON fixture file (testing)

**JSON Format**:
```json
{
  "tables": [
    {
      "schema": "dbo",
      "table": "OSUSR_XXX_TableName",
      "rows": [
        { "Id": 1, "Label": "Value1" },
        { "Id": 2, "Label": "Value2" }
      ]
    }
  ]
}
```

**Future State**: → Test infrastructure for unified pipeline

---

### Data Provider Factory

**Interface**: `IStaticDataProviderFactory`
**File**: `src/Osm.Pipeline/Application/IStaticDataProviderFactory.cs`
**Purpose**: Creates appropriate provider based on CLI options

**Method**:
- `Create(request)` → `IStaticEntityDataProvider?`

**Future State**: → Part of unified data fetching strategy

---

## ⚙️ Configuration

### Synchronization Mode

**Enum**: `StaticSeedSynchronizationMode`
**File**: `src/Osm.Domain/Configuration/StaticSeedSynchronizationMode.cs`

**Values**:
- `Apply` - Direct MERGE without validation
- `ValidateThenApply` - Drift detection, then MERGE

**CLI Option**: `--static-seed-synchronization-mode <mode>`

**Future State**: → Part of `InsertionStrategy` configuration

---

### Seed Emission Options

**Class**: `StaticSeedEmissionOptions`
**File**: `src/Osm.Domain/Configuration/TighteningOptions.cs` (nested)
**Purpose**: Controls seed file organization

**Properties**:
- `GroupByModule` (bool) - Emit one file per module (default: false)
- `EmitMasterFile` (bool) - Emit monolithic file when `GroupByModule = true` (default: false)
- `SynchronizationMode` (StaticSeedSynchronizationMode) - MERGE strategy

**Access**: `request.Scope.TighteningOptions.Emission.StaticSeeds`

**Output Scenarios**:
1. `GroupByModule = false`: `BaselineSeeds/StaticEntities.seed.sql` (monolithic)
2. `GroupByModule = true, EmitMasterFile = false`: `BaselineSeeds/ModuleA/StaticEntities.seed.sql`, `BaselineSeeds/ModuleB/StaticEntities.seed.sql`
3. `GroupByModule = true, EmitMasterFile = true`: Per-module files PLUS master file

**Future State**: → `EmissionStrategy` configuration

---

### Pipeline Request

**Class**: `BuildSsdtPipelineRequest`
**File**: `src/Osm.Pipeline/Orchestration/BuildSsdtPipelineRequest.cs`

**Relevant Properties**:
- `StaticDataProvider` (IStaticEntityDataProvider?) - Data source for static entities
- `SeedOutputDirectoryHint` (string?) - Override default seed output path
- `DeferJunctionTables` (bool) - Delay M:N tables in topological sort

**Future State**: → Unified `EntityPipelineRequest`

---

### Pipeline State

**Class**: `StaticSeedsGenerated`
**File**: `src/Osm.Pipeline/Orchestration/BuildSsdtPipelineStates.cs`
**Purpose**: State after static seed generation step

**Properties**:
- `StaticSeedPaths` (ImmutableArray\<string\>) - Generated `.seed.sql` file paths
- `StaticSeedData` (ImmutableArray\<StaticEntityTableData\>) - Sorted table data
- `StaticSeedTopologicalOrderApplied` (bool) - Sort success
- `StaticSeedOrderingMode` (EntityDependencyOrderingMode) - Sort mode used

**Future State**: → Part of unified pipeline result

---

## 🛠️ Supporting Infrastructure

### Seed Determinizer

**Class**: `EntitySeedDeterminizer`
**File**: `src/Osm.Emission/Seeds/EntitySeedDeterminizer.cs`
**Purpose**: Ensures deterministic data ordering

**Key Methods**:
- `Normalize(tables)` → IReadOnlyList\<StaticEntityTableData\> (static)
  - Sorts tables alphabetically (schema.table)
  - Sorts rows within each table (PK columns first, then all columns)

**Why Needed**: Data from live database may have non-deterministic ordering (insertion order, index scans)

**Future State**: → Part of `Stage 2: DATABASE SNAPSHOT FETCH` normalization

---

### Module Name Sanitizer

**Class**: `ModuleNameSanitizer`
**File**: Referenced in `BuildSsdtStaticSeedStep.cs` (line 116)
**Purpose**: Sanitizes module names for filesystem use

**Why Needed**: OutSystems module names may contain characters invalid for folder names

**Future State**: → Part of emission formatting (`Stage 5: EMISSION`)

---

### Definition Builder

**Class**: `StaticEntitySeedDefinitionBuilder`
**File**: Referenced in `BuildSsdtStaticSeedStep.cs` (line 36)
**Method**: `Build(model, namingOverrides)` → ImmutableArray\<StaticEntitySeedTableDefinition\> (static)

**Purpose**: Extracts static entities from `OsmModel`

**Logic**:
- Filters entities where `entity.DataKind == "staticEntity"`
- Builds `StaticEntitySeedTableDefinition` for each
- Applies naming overrides

**Future State**: → Part of `Stage 1: ENTITY SELECTION` with `EntitySelector.Where(e => e.IsStatic)`

---

## 📍 Integration Points

### Build Pipeline

**Pipeline**: `BuildSsdtPipeline`
**File**: `src/Osm.Pipeline/Orchestration/BuildSsdtPipeline.cs`

**Step Sequence**:
1. Bootstrap Step → SqlValidated
2. **Static Seed Step** → StaticSeedsGenerated (← this concern)
3. Dynamic Insert Step → DynamicDataGenerated
4. Table Emission Step → TablesEmitted
5. SQL Validation Step → SqlValidated

**Future State**: → Unified `EntityPipeline` (no separate static seed step)

---

### Full Export

**Orchestrator**: `FullExportPipeline`
**File**: `src/Osm.Pipeline/Orchestration/FullExportPipeline.cs`

**Static Seed Integration**:
- Invokes `BuildSsdtPipeline` (which includes static seed step)
- Records seed paths in manifest
- Archives seed files

**Future State**: → Unified entity data generation

---

### Schema Apply

**Orchestrator**: `SchemaDataApplier`
**File**: `src/Osm.Pipeline/Orchestration/SchemaDataApplier.cs`

**Apply Logic**:
- Executes generated `.seed.sql` scripts against target database
- Handles `StaticSeedSynchronizationMode`:
  - `ValidateThenApply`: Throws if drift detected, then applies
  - `Apply`: Directly applies MERGE

**Future State**: → Unified data loading step

---

## 🧪 Test Infrastructure

### Key Test Files

**Unit Tests**:
- `tests/Osm.Emission.Tests/StaticEntitySeedScriptGeneratorTests.cs` - Script generation
- `tests/Osm.Emission.Tests/StaticSeedSqlBuilderValidationOverridesTests.cs` - MERGE logic
- `tests/Osm.Emission.Tests/StaticSeedForeignKeyPreflightTests.cs` - FK analysis
- `tests/Osm.Emission.Tests/EntityDependencySorterTests.cs` - Topological sort

**Integration Tests**:
- `tests/Osm.Etl.Integration.Tests/StaticSeedScriptExecutionTests.cs` - End-to-end execution
- `tests/Osm.Pipeline.Integration.Tests/SqlDynamicEntityDataProviderIntegrationTests.cs` - Data fetching

**Pipeline Tests**:
- `tests/Osm.Pipeline.Tests/BuildSsdtPipelineStepTests.cs` - Step execution
- `tests/Osm.Pipeline.Tests/BuildSsdtPipelineTests.cs` - Full pipeline

**Future State**: → Unified entity pipeline test coverage

---

## 🔮 Future State Mapping

### Current → Unified Pipeline

| Current Concept | Unified Concept | Stage | Notes |
|-----------------|-----------------|-------|-------|
| `DataKind == "staticEntity"` | `EntitySelector.Where(e => e.IsStatic)` | Stage 1 | Scope dimension |
| `SqlStaticEntityDataProvider` | `DatabaseSnapshot.Fetch()` | Stage 2 | Data fetching |
| Static-only topological sort | Global topological sort | Stage 4 | Spans ALL entities |
| `StaticEntitySeedScriptGenerator` | Emission strategy | Stage 5 | SQL generation |
| `StaticSeedSynchronizationMode` | `InsertionStrategy.Merge()` | Stage 6 | MERGE vs INSERT |
| `GroupByModule` emission | `EmissionStrategy` config | Stage 5 | File organization |
| Per-module files | Multiple files + .sqlproj | Stage 5 | Emission variant |
| FK Preflight | Deferred FK detection | Stage 3 | Transform |
| Drift detection | Validation mode | Stage 6 | Optional check |

### What Gets Eliminated

- ❌ **Separate static seed step** → Unified entity pipeline
- ❌ **Static-only sorting** → Global sort across all entities
- ❌ **Hardcoded `DataKind` filter** → Configurable `EntitySelector`
- ❌ **Special static data provider** → Unified `DatabaseSnapshot`

### What Gets Unified

| Use Case | Current | Future |
|----------|---------|--------|
| Static Seeds | Specialized pipeline | `EntityPipeline(scope: IsStatic, insertion: MERGE)` |
| Bootstrap | Separate pipeline | `EntityPipeline(scope: All, insertion: INSERT)` |
| Supplemental | Workaround mechanism | `EntitySelector.Include("ServiceCenter", ["User"])` |

---

## 📝 Naming Conventions

### Classes

**Pattern**: `Static{Noun}{Verb}` or `{Verb}Static{Noun}`

**Examples**:
- `StaticEntitySeedScriptGenerator` (generator for static seed scripts)
- `StaticSeedSqlBuilder` (SQL builder for static seeds)
- `StaticEntityDataProvider` (provides static entity data)

**Future**: → Remove "Static" prefix, use unified naming

---

### File Naming

**Pattern**: `Static{Noun}.cs` or `{Noun}StaticSeed.cs`

**Location**: `src/Osm.Emission/Seeds/` (generation logic)
**Location**: `src/Osm.Pipeline/StaticData/` (data providers)
**Location**: `src/Osm.Pipeline/Orchestration/` (pipeline steps)

**Future**: → Reorganize by pipeline stage, not entity category

---

### Configuration Keys

**Pipeline Log Keys**:
- `staticData.seed.skipped` - No static entities found
- `staticData.seed.generated` - Scripts generated successfully
- `staticData.seed.preflight` - FK analysis results
- `staticData.seed.moduleNameRemapped` - Module name collision

**Future**: → Generalized entity pipeline log keys

---

## 🔗 Cross-References

**Related Concerns**:
- **Entity Selection** → How entities are filtered (`DataKind == "staticEntity"`)
- **Topological Sort** → Dependency ordering algorithm
- **Database Snapshot Fetch** → Data source (`SqlStaticEntityDataProvider`)
- **Emission Strategies** → File organization (`GroupByModule`)
- **Insertion Strategies** → MERGE vs INSERT
- **Bootstrap** → Overlapping concern (different scope, same structure)

**Documentation**:
- `docs/architecture/entity-pipeline-unification.md` - North Star vision
- `docs/verbs/build-ssdt.md` - CLI documentation

---

## 🎯 Key Takeaways

1. **Static Seeds is a specialization**, not a unique operation
2. **The pipeline structure is identical** to Bootstrap/DynamicData
3. **The only differences are parameters**: scope (static only), insertion (MERGE), emission (optional per-module)
4. **Topological sort is scoped**, missing cross-category dependencies
5. **FK preflight analysis** already exists, needs generalization
6. **Data fetching is redundant** with other pipelines (triple-fetch problem)
7. **Drift detection** is a valuable feature to preserve in unified pipeline
8. **Per-module emission** complicates deployment (breaks topological order)

**Unification Readiness**: ✅ HIGH - Clear mapping to unified pipeline stages exists
