# Entity Data Pipeline Unification: Ontological Decomposition

**Status**: DRAFT - Architectural Vision
**Date**: 2025-01-XX
**Purpose**: Decompose the unified pipeline ontology to enable systematic implementation

---

## PART I: ONTOLOGY (What the Unified Pipeline IS)

### § 1. Identity

The pipeline is **composition**. Not "a thing that composes" but composition itself.

This truth manifests in `IBuildSsdtStep<TInput, TOutput>` (src/Osm.Pipeline/Orchestration/IBuildSsdtStep.cs:7-10):

```csharp
public interface IBuildSsdtStep<in TInput, TNextState>
{
    Task<Result<TNextState>> ExecuteAsync(TInput state, CancellationToken cancellationToken = default);
}
```

Each step declares what it receives and what it produces. Steps chain via `.BindAsync()` - monadic composition where the output type of step N must match the input type of step N+1. The compiler enforces this. Type errors are ontological impossibilities.

The pipeline is a series of state transformations:
```
PipelineInitialized → ExtractionCompleted → ProfileCompleted → BuildCompleted → ...
```

Each state carries forward everything from before, immutably, adding only what this step contributes. This pattern already exists throughout the codebase - BuildSsdtBootstrapSnapshotStep demonstrates it, BuildSsdtStaticSeedStep uses it, BuildSsdtPipeline.cs orchestrates it.

The unification isn't creating this pattern. The code already knew. We're recognizing what already exists.

---

### § 2. The Invariant Flow

What NEVER changes across any pipeline instance:

```
Selection → Fetch → Transform → Order → Emit → Apply
```

Every pipeline, regardless of parameters, flows through these stages. This is structural necessity, not implementation choice.

**Protected Citizens** (mandatory primitives within each stage):

**Stage 1: Selection**
- Must produce a set of entities (may be empty, but selection always happens)
- Protected: EntitySelector concept (currently fragmented as ModuleFilterOptions)

**Stage 2: Fetch**
- Must retrieve metadata + data from source
- Protected: DatabaseSnapshot (metadata from OSSYS_*, data from OSUSR_*)

**Stage 3: Transform**
- No universally protected citizens (all transforms are optional or conditional)
- Configuration-driven: TypeMappingPolicy, NullabilityEvaluator, UAT-Users, etc.

**Stage 4: Order**
- Protected: EntityDependencySorter (topological sort by FK dependencies)
- **Critical nuance**: Ordering applies ONLY to data emission pipelines
- **Schema emission** (SSDT Modules): Emits CREATE TABLE statements in fetch order within module folders, no topological ordering required (FK constraints don't impose emission order for DDL)

**Stage 5: Emit**
- Must produce artifacts (SQL files, diagnostic logs, profiling reports)
- Protected: Some artifact type must be emitted (schema-only, data-only, or combined valid)

**Stage 6: Apply**
- Defines semantics (INSERT/MERGE/schema-only/none)
- Protected: InsertionStrategy must be specified (even if "none" for export-only)

**Cross-cutting: Diagnostic Logging**
- Every stage emits diagnostics (progress, warnings, metrics)
- Both orthogonal (happens at every stage) and part of emission strategy (diagnostic artifacts)

**Partial Participation Nuance:**
Not all pipelines exercise every stage with equal weight:
- **extract-model**: Selection → Fetch → Emit(model + diagnostics), no Transform/Order/Apply
- **build-ssdt schema**: Selection → Fetch → Emit(schema + diagnostics), minimal Transform, no Order (schema emits in fetch order)
- **build-ssdt data** (StaticSeeds): Full flow with Order (topological sort required for data)
- **full-export**: Full flow with maximum artifact emission (schema + multiple data outputs + diagnostics)

---

### § 3. What Varies vs What Doesn't

**Invariants:**
- Type progression enforced by compiler (`IBuildSsdtStep<TIn, TOut>` chaining)
- State is immutable (each step produces new state, never mutates)
- Topological sort mandatory for data emission (FK dependencies are absolute)
- Diagnostic logging emitted at every stage

**Dimensions (Axes of Variation):**
- **WHICH entities** (selection: all modules, static-only, filtered per-module)
- **WHAT artifacts** (emission: schema SQL, data SQL, diagnostics - compositional)
- **HOW applied** (insertion: INSERT, MERGE, schema-only, none)

These three dimensions are **orthogonal** - changing one doesn't constrain the others. Every use case is a point in this 3D parameter space.

---

## PART II: STAGE DECOMPOSITION (The Invariant Flow in Detail)

**Stages are orthogonal to use cases.** Every pipeline flows through the same stages, but different use cases participate differently in each stage. Some stages are mandatory (protected citizens), some are optional, some are skipped entirely depending on the use case.

This section provides comprehensive detail on every constituent operation within each stage - going beyond the high-level stage names to reveal the actual work being performed at the operation level.

---

### Stage 0: Extract-Model (Optional - Database Metadata Extraction)

**Purpose**: Query OutSystems system tables (OSSYS_*) to retrieve complete database schema metadata.

**Sub-Operations:**

**0.1. Connection Establishment**
- Parse connection string
- Validate SQL Server connectivity
- Test permissions (must have SELECT on OSSYS_* tables)
- Establish connection pool

**0.2. Schema Discovery**
- Query `OSSYS_ENTITY` → Extract entity definitions (physical names, logical names, DataKind)
- Query `OSSYS_ENTITY_ATTR` → Extract attribute definitions (data types, nullability, length, precision)
- Query `OSSYS_RELATIONSHIP` → Extract foreign key relationships (source → target mappings)
- Query `OSSYS_INDEX` → Extract index definitions (columns, uniqueness, clustering)
- Query `OSSYS_TRIGGER` (if applicable) → Extract trigger definitions

**0.3. Metadata Parsing**
- Convert OutSystems metamodel (OSSYS_* tables) → OsmModel structure
- Resolve physical table names (`OSUSR_xxx_TableName` or `dbo.TableName`)
- Map attribute types (OutSystems types → SQL types via TypeMappingPolicy)
- Build relationship graph (FK source/target entity resolution)

**0.4. Model Validation**
- Validate referential integrity (all FK targets exist)
- Detect orphaned relationships (FK references non-existent entity)
- Validate index column references (all indexed columns exist)
- Check for duplicate physical names (collision detection)

**0.5. Caching (if enabled)**
- Compute schema checksum (hash of OSSYS_* row versions or structure)
- Serialize OsmModel → JSON
- Write to `.cache/model.json`
- Store checksum for future invalidation

**0.6. Output**
- `ExtractionCompleted` state containing:
  - `OsmModel` (complete schema structure)
  - `ExtractionMetrics` (query durations, row counts, validation warnings)

**Use Case Participation:**
- **extract-model verb**: PRIMARY operation (all sub-operations)
- **build-ssdt/full-export**: AUTO (can skip if `--model` provided, uses cache if valid)
- **Offline scenarios**: SKIP entirely (user provides pre-extracted model)

**Current Status:**
- Currently manual (user runs `extract-model`, then passes `--model` to other verbs)
- **Vector 5 (§17)**: Integrate as automatic Stage 0 with checksum-based caching

---

### Stage 1: Selection (Entity Scope Determination)

**Purpose**: Determine which entities participate in this pipeline invocation based on selection criteria.

**Sub-Operations:**

**1.1. Parse Selection Configuration**
- Read `ModuleFilterOptions` from config/CLI
- Read `EntityFilters` (if wired - currently incomplete)
- Parse include/exclude module lists
- Parse per-module entity filters (e.g., `ServiceCenter: [User]`)

**1.2. Build Entity Catalog**
- Extract all entities from OsmModel
- Group by module
- Annotate with metadata (IsStatic, HasData flag if known)

**1.3. Apply Module Filter**
- If include list specified → Select only included modules
- If exclude list specified → Remove excluded modules
- If neither → Select all modules (default)

**1.4. Apply Entity Filter (if wired)**
- For each module with `EntityFilters` specified:
  - If `entities: []` → Include all entities from module
  - If `entities: ["User", "Role"]` → Include only listed entities
- **Current**: EntityFilters exists but not wired (Stage 2 fetch ignores it)

**1.5. Apply Predicate Filter**
- If selection predicate specified (e.g., `Where(IsStatic)`):
  - Evaluate predicate against each entity
  - Filter entity set to matching entities
- Common predicates:
  - `Where(e => e.IsStatic)` → StaticSeeds use case
  - `AllWithData()` → Bootstrap use case (requires Stage 2 data check)

**1.6. Validate Selection**
- Check if selection is empty (warn if intentional, error if likely misconfiguration)
- Check for typos in module/entity names (fuzzy matching suggestions)
- Validate EntityFilter references (ensure specified entities exist in module)

**1.7. Output**
- `SelectionCompleted` state containing:
  - `SelectedEntities` (ImmutableHashSet<EntityKey>)
  - `SelectionCriteria` (what filters were applied)
  - Metrics (total entities, filtered entities, filter duration)

**Use Case Participation:**

| Use Case | Selection Criteria | Entity Count (typical) |
|----------|-------------------|----------------------|
| extract-model | AllModules | 300-500 entities |
| build-ssdt schema | AllModules | 300-500 entities |
| build-ssdt data (StaticSeeds) | Where(IsStatic) | 50-150 entities |
| Bootstrap | AllWithData | 300-500 entities |
| full-export | AllModules | 300-500 entities |

**Current Status:**
- Selection criteria hardcoded in each implementation (not parameterized)
- **Vector 1 (§13)**: Wire EntityFilters into all consumers (extraction, validation, profiling)

---

### Stage 2: Fetch (Database Snapshot Acquisition)

**Purpose**: Retrieve metadata, statistics, and/or data for selected entities from the database.

**This is the MOST COMPLEX fetch stage - has 3 major sub-stages with partial participation.**

---

#### Stage 2.1: Metadata Fetch (Structure Acquisition)

**Purpose**: Query OSSYS_* tables to retrieve schema metadata for selected entities.

**Sub-Operations:**

**2.1.1. If Stage 0 Executed → Skip**
- Metadata already available in `ExtractionCompleted.Model`
- Reuse existing OsmModel structure

**2.1.2. If Stage 0 Skipped (--model provided) → Load**
- Deserialize JSON model file → OsmModel
- Validate deserialization (checksum, schema version)
- No database query needed

**2.1.3. If Partial Fetch Needed → Query**
- Query OSSYS_* tables scoped to selected entities only
- **NOTE**: Current implementation always fetches all entities (EntityFilters not wired)
- Build OsmModel subset

**2.1.4. Relationship Graph Construction**
- Parse all foreign key relationships
- Build directed graph (entity A → entity B if A has FK to B)
- Identify self-referential FKs (entity → self)
- Detect circular FK chains (User ↔ Role, etc.)

**Output**: `Metadata` (OsmModel for selected entities)

---

#### Stage 2.2: Statistics Fetch (Profiling & Data Quality Metrics)

**Purpose**: Query `sys.*` system tables and OSSYS_* metadata to gather statistics for data quality analysis.

**Sub-Operations:**

**2.2.1. Row Count Profiling**
- For each selected entity:
  - Query `SELECT COUNT(*) FROM [table]`
  - Store row count (used for "has data" determination)

**2.2.2. Null Count Profiling**
- For each attribute marked `isMandatory` in model:
  - Query `SELECT COUNT(*) FROM [table] WHERE [column] IS NULL`
  - Detect violations (mandatory column with NULLs)
  - Log warnings for NullabilityEvaluator

**2.2.3. Foreign Key Orphan Detection**
- For each FK relationship:
  - Query `SELECT COUNT(*) FROM [sourceTable] WHERE [fkColumn] NOT IN (SELECT [pkColumn] FROM [targetTable])`
  - Detect orphaned FK values (references to non-existent rows)
  - Store orphan counts (used for deferred FK decisions)

**2.2.4. Uniqueness Violation Detection**
- For each unique index/constraint:
  - Query `SELECT [columns], COUNT(*) FROM [table] GROUP BY [columns] HAVING COUNT(*) > 1`
  - Detect duplicate values in unique constraints
  - Log violations for validation stage

**2.2.5. Data Type Overflow Detection (optional)**
- For each numeric column:
  - Query `SELECT MIN([column]), MAX([column]) FROM [table]`
  - Detect potential overflows if type mapping changes

**2.2.6. Statistics Aggregation**
- Collect all metrics into `EntityStatistics` per table
- Compute global statistics (total rows, total violations, etc.)

**Output**: `Statistics` (ImmutableDictionary<TableKey, EntityStatistics>)

**Current Status:**
- Profiling queries all entities (EntityFilters not wired)
- **Vector 1 (§13)**: Scope profiling queries to selected entities only

---

#### Stage 2.3: Data Fetch (Actual Row Data Extraction)

**Purpose**: Query OSUSR_* tables to extract actual row data for entities.

**Sub-Operations:**

**2.3.1. Determine Which Entities Have Data**
- From Stage 2.2 row count profiling:
  - If `COUNT(*) > 0` → Entity has data
  - If `COUNT(*) = 0` → Entity has no data (skip data fetch)

**2.3.2. For Each Entity With Data:**

**2.3.2.1. Build SELECT Query**
- Enumerate all columns in entity definition
- Apply naming overrides (logical name → physical name)
- Build: `SELECT [col1], [col2], ... FROM [schema].[table]`

**2.3.2.2. Execute Query**
- Execute with streaming (avoid loading all rows into memory)
- Read rows in batches (e.g., 10,000 rows at a time)
- Apply column type conversions (SQL type → C# type)

**2.3.2.3. Normalize Data**
- Convert `UNIQUEIDENTIFIER` → string (for deterministic serialization)
- Convert `DATETIME` → ISO-8601 string
- Handle NULLs (explicit null representation)
- Normalize whitespace (trim trailing spaces for CHAR columns)

**2.3.2.4. Store Row Data**
- Create `EntityData` structure:
  - `Rows`: ImmutableArray<ImmutableDictionary<string, object?>>
  - `Schema`: Column names and types
  - `RowCount`: Total rows fetched

**2.3.3. Handle Supplemental Entities (if applicable)**
- Query supplemental JSON files (e.g., `ossys-user.json`)
- Parse JSON → EntityData structure
- Merge with database-fetched data
- **NOTE**: Supplemental workaround for EntityFilters not being wired

**2.3.4. Data Aggregation**
- Collect all entity data into `ImmutableDictionary<TableKey, EntityData>`

**Output**: `Data` (row data for all selected entities with data)

**Current Status:**
- Data fetch queries all entities with data (EntityFilters not wired for scope)
- **Vector 1 (§13)**: Scope data fetch to selected entities only

---

#### Stage 2.4: DatabaseSnapshot Assembly

**Purpose**: Combine metadata, statistics, and data into unified snapshot structure.

**Sub-Operations:**

**2.4.1. Construct DatabaseSnapshot**
- Combine results from Stages 2.1, 2.2, 2.3:
  ```csharp
  public sealed class DatabaseSnapshot {
      public ImmutableArray<EntityStructure> Entities;        // From 2.1
      public ImmutableArray<RelationshipStructure> Relationships;  // From 2.1
      public ImmutableDictionary<TableKey, EntityStatistics> Statistics;  // From 2.2
      public ImmutableDictionary<TableKey, EntityData> Data;  // From 2.3
  }
  ```

**2.4.2. Cache Snapshot (if enabled)**
- Serialize DatabaseSnapshot → JSON
- Write to `.cache/database-snapshot.json`
- Store checksum (for invalidation)

**2.4.3. Output**
- `FetchCompleted` state containing:
  - `DatabaseSnapshot` (unified metadata + stats + data)
  - `FetchMetrics` (query counts, durations, rows fetched)

**Partial Fetch Matrix** (what gets fetched by use case):

| Use Case | Metadata | Statistics | Data |
|----------|----------|-----------|------|
| extract-model | ✓ (OSSYS_*) | ✗ | ✗ |
| build-ssdt schema | ✓ | ✓ (profiling) | ✗ |
| build-ssdt data | ✓ | ✓ | ✓ (static entities only) |
| Bootstrap | ✓ | ✓ | ✓ (all entities with data) |
| full-export | ✓ | ✓ | ✓ (all entities) |

**Current Status:**
- Triple-fetch redundancy (OSSYS_* queried 2-3 times in separate steps)
- No unified DatabaseSnapshot abstraction
- **Vector 7 (§24)**: Eliminate triple-fetch with DatabaseSnapshot primitive

---

### Stage 3: Transform (Business Logic Application)

**Purpose**: Apply business logic transformations to fetched data/metadata before emission.

**No universally protected citizens - all transforms are optional or configuration-driven.**

This stage contains **12+ distinct transforms** with complex ordering dependencies.

---

#### Stage 3.1: Pre-Sort Transforms (Execute Before Topological Sort)

These transforms modify data or metadata that affects dependency ordering.

---

**Transform 3.1.1: EntitySeedDeterminizer.Normalize**

**Purpose**: Ensure deterministic row ordering within each entity (stable sort for reproducibility).

**Location**: `BuildSsdtStaticSeedStep.cs:78`

**Sub-Operations:**
- For each entity's row data:
  - Sort rows by primary key (ascending)
  - If composite PK → Sort by PK columns in definition order
  - If no PK → Sort by all columns lexicographically (warning: non-deterministic)
- Normalize data types (e.g., trim whitespace, normalize case for certain types)

**Output**: Normalized `ImmutableArray<StaticEntityTableData>` with stable row ordering

**Configuration**: Not configurable (always runs for StaticSeeds, Bootstrap)

**Use Cases**: StaticSeeds, Bootstrap

---

**Transform 3.1.2: Module Name Collision Handling**

**Purpose**: Disambiguate duplicate module names with numeric suffixes.

**Location**: `BuildSsdtStaticSeedStep.cs:196-231`

**Sub-Operations:**
- Build set of all module names in selected entities
- For each module name:
  - Count occurrences (should be 1)
  - If > 1 occurrence → Append suffix: `ModuleName`, `ModuleName_2`, `ModuleName_3`, etc.
- Update entity definitions with disambiguated module names

**Output**: Entities with unique module names (no collisions)

**Configuration**: Not configurable (always runs when needed)

**Use Cases**: All use cases (schema + data)

---

**Transform 3.1.3: Supplemental Physical→Logical Name Remapping**

**Purpose**: Transform physical table names (e.g., `ossys_User`) → logical names (e.g., `User`).

**Location**: `BuildSsdtBootstrapSnapshotStep.cs:338-361`

**Sub-Operations:**
- For each supplemental entity:
  - Lookup logical name from supplemental config
  - Replace physical name in entity definition
  - Update FK references (if any FK points to supplemental entity)

**Output**: Entities with consistent naming (logical names only)

**Configuration**: Not configurable (only applies to supplemental entities)

**Use Cases**: Bootstrap (when supplementals present)

**Deprecation Note**: Supplemental workaround goes away when EntityFilters wired (Vector 1)

---

#### Stage 3.2: Type and Schema Transforms

These transforms modify data types, nullability, or schema structure.

---

**Transform 3.2.1: TypeMappingPolicy**

**Purpose**: Apply complex data type transformations (OutSystems types → SQL types with OnDisk overrides).

**Location**: `TypeMappingPolicy.cs`, `TypeMappingRule.cs`, `config/type-mapping.default.json`

**Sub-Operations:**
- Load type mapping rules from `type-mapping.json` (or defaults)
- For each attribute in each entity:
  - Lookup attribute type (e.g., `Money`, `Integer`, `Text`)
  - Apply mapping rule → SQL type (e.g., `Money` → `DECIMAL(18,2)` or `INT` depending on precision)
  - Handle OnDisk overrides (model says `INT`, database has `BIGINT` → keep BIGINT)
  - Handle external types (custom SQL types defined in config)
- Update entity definitions with mapped types

**Output**: Entities with SQL-ready data types

**Configuration**: `type-mapping.json` (configurable rules)

**Use Cases**: All schema emission (build-ssdt schema, full-export schema)

---

**Transform 3.2.2: NullabilityEvaluator**

**Purpose**: Policy-driven nullability tightening (recommend NOT NULL based on isMandatory + profiling data).

**Location**: `NullabilityEvaluator.cs`, `TighteningPolicy.cs`

**Sub-Operations:**
- Load TighteningOptions from config
- For each attribute marked `isMandatory` in model:
  - Check profiling statistics (any NULLs observed?)
  - If NULLs observed → WARNING (model says mandatory, data has NULLs)
    - Check validation overrides (operator explicitly approved NOT NULL?)
    - If approved → Tighten to NOT NULL (emit DDL with NOT NULL)
    - If not approved → Keep nullable (emit warning)
  - If no NULLs observed → RECOMMENDATION (safe to tighten to NOT NULL)
    - Check TighteningPolicy (auto-tighten enabled?)
    - If enabled → Tighten to NOT NULL
    - If disabled → Keep nullable, log recommendation

**Output**: Entities with tightened nullability (where safe and approved)

**Configuration**: `TighteningOptions`, `ValidationOverrides` (per-attribute approvals)

**Use Cases**: All schema emission

**Critical Principle**: NEVER auto-coerce. Always warn operator, require explicit approval.

---

**Transform 3.2.3: Deferred FK Constraint Detection**

**Purpose**: Detect FK constraints with orphaned data → Emit as deferred (WITH NOCHECK).

**Location**: `CreateTableStatementBuilder.cs:190-195, 214-249`

**Sub-Operations:**
- For each FK constraint:
  - Check profiling statistics (any orphans detected in Stage 2.2.3?)
  - If orphans > 0 → Mark FK as `IsNoCheck = true`
    - Emit FK as separate ALTER TABLE ADD CONSTRAINT WITH NOCHECK statement
    - Add comment: `-- WARNING: [N] orphaned rows detected`
  - If orphans == 0 → Emit FK inline in CREATE TABLE (normal)

**Output**: Entities with FK constraints marked for deferred emission

**Configuration**: Profiling results (data-driven decision)

**Use Cases**: All schema emission

---

#### Stage 3.3: UAT-Users Transform (Dual-Mode)

**Purpose**: Transform user FK values for QA→UAT data migration.

**This is the MOST COMPLEX transform - has discovery phase (Stage 3) and application phase (Stage 5 or 6).**

---

**Transform 3.3.1: UAT-Users Discovery (Stage 3 - Always Happens First)**

**Location**: UAT-users verb integration into full-export

**Sub-Operations:**

**3.3.1.1. FK Catalog Discovery**
- Scan all entities for FK relationships
- Identify all FK columns referencing User table:
  - `CreatedBy`, `UpdatedBy`, `AssignedTo`, `OwnerId`, etc.
- Build FK catalog: `[(SourceTable, SourceColumn, TargetTable="User", TargetColumn="Id")]`
- Deduplicate catalog (same column may appear in multiple tables)

**3.3.1.2. User Inventory Loading**
- Load QA user inventory (from `./qa_users.csv` - Service Center export)
  - Schema: `Id, Username, EMail, Name, External_Id, Is_Active, Creation_Date, Last_Login`
  - Parse CSV → `ImmutableArray<UserRecord>`
  - Validate: No duplicate IDs, all required fields present
- Load UAT user inventory (from `./uat_users.csv`)
  - Same schema
  - Parse CSV → `ImmutableArray<UserRecord>`
  - Validate: No duplicate IDs, all required fields present

**3.3.1.3. Orphan Detection**
- For each FK column in catalog:
  - Query distinct user IDs: `SELECT DISTINCT [fkColumn] FROM [table] WHERE [fkColumn] IS NOT NULL`
  - For each distinct user ID:
    - Check if ID exists in UAT user inventory
    - If NOT in UAT → Mark as orphan (QA user ID not in UAT roster)

**3.3.1.4. User Mapping Loading**
- Load user mapping (from `./uat_user_map.csv`)
  - Schema: `SourceUserId, TargetUserId, Rationale`
  - Parse CSV → `ImmutableDictionary<int, int>` (QA ID → UAT ID)
- Validate mapping:
  - All SourceUserIds must be in QA inventory
  - All TargetUserIds must be in UAT inventory
  - No duplicate SourceUserIds

**3.3.1.5. Matching Strategy (optional auto-mapping)**
- If matching strategy enabled (e.g., `case-insensitive-email`):
  - For each orphan without manual mapping:
    - Try to find UAT user by email match (case-insensitive)
    - If match found → Add to mapping (with rationale: "Auto-matched by email")
    - If no match → Fallback strategy (single user, round-robin, or leave unmapped)

**3.3.1.6. Mapping Validation**
- For each orphan:
  - Check if mapping exists
  - If missing mapping → ERROR (halt pipeline - operator must provide mapping)
- Log mapping statistics:
  - Total orphans, mapped count, unmapped count, auto-matched count, manual count

**3.3.1.7. Build TransformationContext**
- Create `TransformationContext`:
  - FK catalog (which columns to transform)
  - Orphan mapping (QA ID → UAT ID)
  - Inventories (for reference)

**Output**: `TransformationContext` (passed to Stage 5 or 6 for application)

**Configuration**: `--enable-uat-users`, `--qa-user-inventory`, `--uat-user-inventory`, `--user-map`

**Use Cases**: full-export with UAT-users enabled

---

**Transform 3.3.2: UAT-Users Application - INSERT Mode (Stage 5 - During Emission)**

**Purpose**: Transform user FK values DURING INSERT script generation (pre-transformed INSERTs).

**Location**: `DynamicEntityInsertGenerator` (M2.2 implementation)

**Sub-Operations:**
- For each entity being emitted:
  - Check if entity has user FK columns (lookup in FK catalog)
  - If yes:
    - For each row:
      - For each user FK column:
        - Read current value (QA user ID or NULL)
        - If NULL → Keep NULL (don't transform)
        - If value is orphan (in mapping) → Replace with UAT user ID
        - If value is not orphan → Keep as-is (already valid UAT user)
      - Emit INSERT statement with transformed values

**Output**: INSERT scripts with UAT-ready user FK values

**Use Cases**: full-export with UAT-users (INSERT mode - RECOMMENDED)

---

**Transform 3.3.3: UAT-Users Application - UPDATE Mode (Stage 6 - Post-Emission)**

**Purpose**: Generate UPDATE scripts to transform user FKs after data load (legacy/verification mode).

**Location**: Standalone `uat-users` verb

**Sub-Operations:**
- For each FK column in catalog:
  - Generate UPDATE statement:
    ```sql
    UPDATE [table]
    SET [fkColumn] = CASE
        WHEN [fkColumn] = [orphan1] THEN [uatUser1]
        WHEN [fkColumn] = [orphan2] THEN [uatUser2]
        ...
    END
    WHERE [fkColumn] IN ([orphan1], [orphan2], ...)
      AND [fkColumn] IS NOT NULL
    ```
- Emit to `02_apply_user_remap.sql`

**Output**: UPDATE script for post-load transformation

**Use Cases**: Legacy UAT migration (data already loaded with QA IDs), verification artifact

---

#### Stage 3.4: Post-Sort, Pre-Emit Transforms

These transforms execute after topological sort but before emission.

---

**Transform 3.4.1: StaticSeedForeignKeyPreflight.Analyze**

**Purpose**: FK orphan detection and ordering violation detection (data quality check).

**Location**: `BuildSsdtStaticSeedStep.cs:90`

**Sub-Operations:**
- For each FK relationship:
  - Check if target entity is in selected entity set
  - If target NOT selected → WARNING (FK will fail - missing referenced entity)
  - Check if any rows have FK values pointing to non-existent target rows
  - If orphans detected → WARNING (data quality issue)
- Log all violations for operator review

**Output**: Preflight report (warnings logged to diagnostics)

**Configuration**: Not configurable (always runs for StaticSeeds)

**Use Cases**: StaticSeeds

---

**Transform 3.4.2: TopologicalOrderingValidator**

**Purpose**: Enhanced cycle diagnostics with FK analysis, nullable detection, CASCADE warnings.

**Location**: `BuildSsdtBootstrapSnapshotStep.cs:115-127, 186-325`

**Sub-Operations:**
- For each detected cycle in topological sort:
  - Analyze cycle path (entity A → B → C → A)
  - For each FK in cycle:
    - Check if FK is nullable (can break cycle by setting NULL)
    - Check if FK has ON DELETE CASCADE (potential data loss risk)
    - Check if cycle is allowed (in CircularDependencyOptions)
  - If cycle allowed → Log informational message
  - If cycle NOT allowed → ERROR (halt pipeline)
- Generate diagnostic report:
  - Cycle paths (visualize: A → B → C → A)
  - Suggested resolutions (which FK to make nullable, etc.)

**Output**: Cycle diagnostics report (warnings/errors logged)

**Configuration**: `CircularDependencyOptions` (explicitly allowed cycles)

**Use Cases**: Bootstrap, full-export

**Current Status**: Only in Bootstrap, needs extraction for StaticSeeds (Vector 3, §15)

---

### Stage 4: Order (Topological Sort by FK Dependencies)

**Purpose**: Build dependency graph from FK relationships and produce globally ordered entity list.

**Protected Citizen (for data pipelines)**: EntityDependencySorter (mandatory when emitting data).

**Critical Nuance**: Schema emission (DDL) SKIPS this stage. Data emission (DML) PARTICIPATES.

---

#### Stage 4.1: Dependency Graph Construction

**Sub-Operations:**

**4.1.1. Extract FK Relationships**
- From DatabaseSnapshot (or OsmModel):
  - For each FK relationship:
    - Source entity, target entity
    - FK column name, target PK column name
    - Nullability, ON DELETE/UPDATE behavior

**4.1.2. Build Directed Graph**
- Create adjacency list:
  - For each entity:
    - List of entities it depends on (outgoing edges = FKs from this entity)
    - List of entities that depend on it (incoming edges = FKs to this entity)

**4.1.3. Handle Self-Referential FKs**
- Detect entity → self relationships (e.g., `Employee.ManagerId` → `Employee.Id`)
- Mark as self-referential (don't create cycle error)

**4.1.4. Apply Manual Ordering (CircularDependencyOptions)**
- Load CircularDependencyOptions from config
- For each manually ordered FK:
  - Override natural dependency (treat A → B as if B → A for ordering purposes)
  - Used to break cycles: User ↔ Role (manually order: Role before User)

---

#### Stage 4.2: Topological Sort Execution

**Sub-Operations:**

**4.2.1. Kahn's Algorithm (or DFS-based)**
- Initialize:
  - Compute in-degree for each entity (count of incoming FKs)
  - Create queue of entities with in-degree == 0 (no dependencies)
- While queue not empty:
  - Dequeue entity E
  - Add E to sorted list
  - For each entity D that depends on E:
    - Decrement in-degree of D
    - If in-degree of D == 0 → Enqueue D
- If sorted list length < entity count → Cycle detected

**4.2.2. Cycle Detection**
- If sorted list incomplete → Remaining entities form cycles
- Extract cycle paths:
  - Use DFS to find strongly connected components
  - Build cycle visualization (A → B → C → A)

**4.2.3. Defer Junction Tables (optional)**
- If `DeferJunctionTables` option enabled:
  - Detect junction tables (entities with only FK columns + PK)
  - Move junction tables to END of sorted list (after all referenced entities)
  - Reason: Junction tables can be loaded last without breaking FKs

**4.2.4. Alphabetical Fallback**
- If topological sort fails (cycles detected, not allowed):
  - Fall back to alphabetical ordering (warning: may cause FK violations)
  - OR halt pipeline (recommended)

---

#### Stage 4.3: Post-Sort Validation

**Sub-Operations:**

**4.3.1. Verify FK Ordering**
- For each FK relationship:
  - Check that target entity appears BEFORE source entity in sorted list
  - If not → ERROR (sort algorithm failure or cycle not handled)

**4.3.2. Log Ordering Metrics**
- Total entities sorted
- Topological ordering applied (yes/no)
- Cycles detected (count, paths)
- Manual orderings applied (count)

---

#### Stage 4.4: Output

- `OrderCompleted` state containing:
  - `TopologicalOrder` (ImmutableArray<EntityKey> in dependency-safe order)
  - `Cycles` (ImmutableArray<CycleWarning> with paths and suggestions)
  - `OrderingMode` (Topological, Alphabetical, or Manual)

**Participation Decision Tree:**

```
Is this a data emission pipeline (INSERT/MERGE)?
  ├─ YES → PARTICIPATE (Stage 4 mandatory - sort is required)
  └─ NO (schema emission only) → SKIP (emit in fetch order - DDL order-independent)
```

**Participation Matrix:**

| Use Case | Participates? | Sort Scope | Why |
|----------|--------------|------------|-----|
| extract-model | NO (SKIP) | N/A | No data, just metadata export |
| build-ssdt schema | NO (SKIP) | N/A | DDL order-independent |
| build-ssdt data (StaticSeeds) | YES | Static entities only (WRONG) | Should be global (Vector 3) |
| Bootstrap | YES | ALL entities (CORRECT) | Global sort across all categories |
| full-export | YES | ALL entities | Global sort (reused for multiple data outputs) |

**Critical Issue (Current):**
- StaticSeeds sorts only static entities (scoped sort)
- Misses cross-category FK dependencies (regular entity → static entity)
- **Vector 3 (§15)**: Unify sort scope to ALWAYS be global (all selected entities)

---

### Stage 5: Emit (Artifact Generation)

**Purpose**: Generate SQL artifacts (schema DDL, data DML, diagnostics) from ordered/transformed entities.

**Protected Citizen**: MUST emit something (cannot skip entirely).

**This is the MOST COMPLEX emission stage - has 3 major sub-stages (Schema, Data, Diagnostics).**

---

#### Stage 5.1: Schema Emission (DDL Generation)

**Purpose**: Generate CREATE TABLE statements for entity schema.

---

**Sub-Stage 5.1.1: Per-Entity DDL Generation**

**For Each Entity:**

**5.1.1.1. Build CREATE TABLE Statement**
- Table name (physical name with schema: `[dbo].[TableName]`)
- Column definitions:
  - For each attribute:
    - Column name (physical name)
    - Data type (from TypeMappingPolicy transform)
    - Nullability (from NullabilityEvaluator transform)
    - Default value (if specified)
    - Identity (if auto-increment)
- Primary key constraint:
  - Inline or separate ALTER TABLE statement
  - Naming: `PK_TableName` or unnamed
- Unique constraints:
  - For each unique index
  - Naming: `UQ_TableName_ColumnName` or unnamed
- Check constraints:
  - For each check constraint (if any)
- Foreign key constraints:
  - Inline or separate ALTER TABLE statement
  - WITH CHECK or WITH NOCHECK (from deferred FK transform)
  - ON DELETE/UPDATE behavior
  - Naming: `FK_SourceTable_TargetTable` or unnamed

**5.1.1.2. Format SQL**
- Apply CreateTableFormatter:
  - Indentation (tabs vs spaces, configurable)
  - Line breaks (column per line, constraints on separate lines)
  - Whitespace normalization
- Apply SmoNormalization:
  - Remove redundant parentheses from DEFAULT constraints
  - Normalize SQL expressions

**5.1.1.3. Add Comments**
- Header comment:
  - `-- Table: [LogicalName]`
  - `-- Module: [ModuleName]`
  - `-- Generated: [Timestamp]`
- Column comments (if metadata available):
  - `-- [ColumnName]: [Description]`

**Output (per entity)**: CREATE TABLE script (string)

---

**Sub-Stage 5.1.2: File Organization (Per-Module)**

**5.1.2.1. Group Entities by Module**
- For each module:
  - Collect all entities in module
  - Sort entities within module (alphabetical or fetch order)

**5.1.2.2. Create Module Directories**
- Create `Modules/[ModuleName]/` directory
- Handle module name collisions (from Transform 3.1.2)
- Sanitize module names (if configured): Remove spaces, special characters

**5.1.2.3. Write Entity Files**
- For each entity in module:
  - Write to `Modules/[ModuleName]/[EntityName].table.sql`
  - Use UTF-8 encoding, no BOM

**5.1.2.4. Generate .sqlproj File**
- Create `Modules.sqlproj` (MSBuild project file)
- Add references to all entity files:
  ```xml
  <Build Include="ModuleA/Entity1.table.sql" />
  <Build Include="ModuleA/Entity2.table.sql" />
  <Build Include="ModuleB/Entity1.table.sql" />
  ```
- Files listed in **module order, then entity order** (NOT topological - schema emission doesn't need FK ordering)

**Output**: Schema files (Modules/**/*.table.sql, Modules.sqlproj)

---

#### Stage 5.2: Data Emission (DML Generation)

**Purpose**: Generate INSERT/MERGE statements for entity data (topologically ordered).

---

**Sub-Stage 5.2.1: Per-Entity Data Script Generation**

**For Each Entity (in topological order from Stage 4):**

**5.2.1.1. Batch Data into Chunks**
- Divide rows into batches (default: 1000 rows per batch)
- Reason: SQL Server has limits (e.g., max 1000 rows in VALUES clause for some versions)

**5.2.1.2. Generate INSERT Statements (if InsertionStrategy = INSERT)**
- For each batch:
  - Build `INSERT INTO [table] ([col1], [col2], ...) VALUES`
  - For each row:
    - Format values:
      - Strings → `'escaped value'` (with quote escaping)
      - NULLs → `NULL`
      - GUIDs → `'guid-string'`
      - Dates → `'YYYY-MM-DD HH:MM:SS'`
    - Append `(val1, val2, ...),`
  - Remove trailing comma, add semicolon

**5.2.1.3. Generate MERGE Statements (if InsertionStrategy = MERGE)**
- For each batch:
  - Build `MERGE [table] AS target USING (VALUES ...) AS source ON target.PK = source.PK`
  - WHEN MATCHED → UPDATE SET ...
  - WHEN NOT MATCHED → INSERT VALUES ...
  - Semicolon terminator

**5.2.1.4. Apply UAT-Users Transform (INSERT mode - if enabled)**
- If TransformationContext present (from Stage 3.3.1):
  - For each row:
    - For each user FK column (from FK catalog):
      - Read current value
      - If value is orphan (in mapping) → Replace with UAT user ID
      - Emit transformed value in INSERT statement

**5.2.1.5. Add Comments**
- Header comment:
  - `-- Entity: [EntityName] ([RowCount] rows)`
  - `-- Module: [ModuleName]`
- Batch comment (if multiple batches):
  - `-- Batch 1/5 (rows 1-1000)`

**Output (per entity)**: INSERT/MERGE script (string)

---

**Sub-Stage 5.2.2: File Organization**

**Option A: Monolithic (Bootstrap, StaticSeeds default)**
- Concatenate all entity scripts into single file
- Entities appear in topological order (FK-safe)
- Write to `Bootstrap/Bootstrap.sql` or `StaticSeeds/StaticSeeds.sql`

**Option B: Per-Module (StaticSeeds optional - BROKEN)**
- Group entities by module
- For each module:
  - Write to `StaticSeeds/[ModuleName].seed.sql`
- **Problem**: No .sqlproj ordering, breaks cross-module FK dependencies
- **Fix (Vector 2, §14)**: Generate .sqlproj with topological ordering

**Option C: Per-Entity (future)**
- For each entity:
  - Write to `Data/[ModuleName]/[EntityName].data.sql`
- Generate .sqlproj with topological ordering
- NOT IMPLEMENTED YET

**Output**: Data files (Bootstrap.sql, StaticSeeds.sql, or per-module files)

---

#### Stage 5.3: Diagnostic Emission

**Purpose**: Generate profiling reports, validation logs, cycle diagnostics, transform execution logs.

---

**Sub-Stage 5.3.1: Profiling Report Generation**

**5.3.1.1. Collect Profiling Statistics** (from Stage 2.2)
- For each entity:
  - Row count
  - Null counts per column
  - FK orphan counts
  - Uniqueness violation counts

**5.3.1.2. Format as CSV**
- Columns: `Module, Entity, RowCount, NullViolations, FKOrphans, UniquenessViolations`
- Write to `Diagnostics/profiling-report.csv`

**5.3.1.3. Format as JSON** (optional, for automation)
- Structured JSON with nested objects per entity
- Write to `Diagnostics/profiling-report.json`

---

**Sub-Stage 5.3.2: Validation Results Log**

**5.3.2.1. Collect Validation Warnings/Errors**
- From NullabilityEvaluator: Mandatory columns with NULLs
- From StaticSeedForeignKeyPreflight: FK orphans, missing referenced entities
- From TopologicalOrderingValidator: Cycles, ordering issues

**5.3.2.2. Format as Structured Log**
- For each validation result:
  - Severity (INFO, WARNING, ERROR)
  - Message
  - Entity/column affected
  - Suggested resolution
- Write to `Diagnostics/validation-results.log`

---

**Sub-Stage 5.3.3: Cycle Diagnostics**

**5.3.3.1. Collect Cycle Warnings** (from Stage 4)
- For each detected cycle:
  - Cycle path (A → B → C → A)
  - FK details (which FKs form the cycle)
  - Nullable FKs (which could break cycle)
  - Manual ordering applied (from CircularDependencyOptions)

**5.3.3.2. Format as Text Report**
- Header: "Topological Ordering Diagnostics"
- For each cycle:
  - Visualize cycle path
  - List suggested resolutions
- Write to `Diagnostics/cycles.txt`

---

**Sub-Stage 5.3.4: Transform Execution Log**

**5.3.4.1. Collect Transform Metrics** (from Stage 3)
- For each transform that executed:
  - Transform name
  - Execution duration
  - Warnings emitted
  - Entities affected

**5.3.4.2. Format as Structured Log**
- Columns: `Transform, Duration, Warnings, EntitiesAffected`
- Write to `Diagnostics/transform-execution.log`

---

**Sub-Stage 5.3.5: Pipeline Manifest**

**5.3.5.1. Collect Pipeline Invocation Record**
- Input parameters:
  - Connection string (sanitized - no password)
  - Selection criteria
  - Emission strategy
  - Insertion strategy
  - Enabled transforms
- Output artifacts:
  - List of all generated files (paths)
  - Row counts per file
  - File sizes
- Execution metrics:
  - Total duration
  - Per-stage durations
  - Memory usage

**5.3.5.2. Format as JSON**
- Structured JSON for CI/CD integration
- Write to `Diagnostics/pipeline-manifest.json`

---

**Output**: Diagnostic artifacts (CSV, JSON, TXT, LOG files in Diagnostics/)

---

### Stage 6: Apply (Insertion Semantics Definition)

**Purpose**: Define how generated SQL modifies the target database (or specify export-only).

**Protected Citizen**: InsertionStrategy must be specified (even if "None" for export-only).

**NOTE**: This stage defines semantics, but doesn't necessarily execute deployment (execution is optional).

---

#### Stage 6.1: Insertion Strategy Selection

**Sub-Operations:**

**6.1.1. Parse InsertionStrategy from Config/CLI**
- Read `--insertion-strategy` flag or config
- Validate strategy:
  - SchemaOnly → Schema emission only (no data)
  - INSERT → One-time data load (NonDestructive mode)
  - MERGE → Upsert on PK (idempotent)
  - TRUNCATE+INSERT → Clear table then load
  - None → Export-only (no database modification)

**6.1.2. Validate Strategy Against Emitted Artifacts**
- If InsertionStrategy = INSERT but no data emitted → WARNING (nothing to insert)
- If InsertionStrategy = SchemaOnly but no schema emitted → ERROR (inconsistency)

---

#### Stage 6.2: Application Execution (Optional)

**Sub-Operations:**

**6.2.1. If InsertionStrategy = None → SKIP**
- No deployment, just export artifacts
- Log: "Export-only mode, no database modification"

**6.2.2. If InsertionStrategy != None → Execute (Optional)**
- **NOTE**: Current implementation generates scripts, doesn't auto-deploy
- User manually executes scripts against target database
- **Future**: Could add auto-deploy with `--deploy` flag

**6.2.3. Log Application Intent**
- Log which InsertionStrategy specified
- Log estimated impact:
  - Row count to insert/merge
  - Schema changes (tables created, columns altered)
  - Estimated deployment duration

---

#### Stage 6.3: Output

- `ApplicationCompleted` state containing:
  - `DeploymentResult` (null if export-only, or execution result if deployed)
  - `ApplicationMetrics` (estimated impact, actual impact if deployed)

**Application Matrix:**

| Use Case | InsertionStrategy | Execution |
|----------|------------------|-----------|
| extract-model | None | SKIP (export model.json) |
| build-ssdt schema | SchemaOnly | Optional (deploy DDL) |
| build-ssdt data (StaticSeeds) | MERGE (on PK, batch 1000) | Optional (deploy MERGE) |
| Bootstrap | INSERT (NonDestructive, batch 1000) | Optional (deploy INSERT) |
| full-export | Combined OR None | SKIP (export-only typical) |

---

### Cross-Stage State Flow (Immutable Composition)

Each stage produces a new immutable state that includes everything from previous stages:

```csharp
// Stage 0 Output
public record ExtractionCompleted(
    OsmModel Model,
    ExtractionMetrics Metrics
);

// Stage 1 Output (carries forward Stage 0)
public record SelectionCompleted(
    ExtractionCompleted Extraction,          // Carried forward
    ImmutableHashSet<EntityKey> SelectedEntities,
    SelectionCriteria Criteria
);

// Stage 2 Output (carries forward Stage 0 + 1)
public record FetchCompleted(
    SelectionCompleted Selection,            // Carried forward
    DatabaseSnapshot Snapshot,
    FetchMetrics Metrics
);

// Stage 3 Output (carries forward Stage 0 + 1 + 2)
public record TransformCompleted(
    FetchCompleted Fetch,                    // Carried forward
    ImmutableArray<Transform> AppliedTransforms,
    TransformExecutionLog Log
);

// Stage 4 Output (carries forward Stage 0 + 1 + 2 + 3)
public record OrderCompleted(
    TransformCompleted Transform,            // Carried forward
    ImmutableArray<EntityKey> TopologicalOrder,
    ImmutableArray<CycleWarning> Cycles
);

// Stage 5 Output (carries forward all previous)
public record EmissionCompleted(
    OrderCompleted Order,                    // Carried forward (or Transform if Stage 4 skipped)
    ImmutableArray<ArtifactPath> Artifacts,
    EmissionMetrics Metrics
);

// Stage 6 Output (carries forward all previous)
public record ApplicationCompleted(
    EmissionCompleted Emission,              // Carried forward
    DeploymentResult? Result,                // null if export-only
    ApplicationMetrics Metrics
);
```

**Key Insight**: State flows forward immutably. Each stage sees everything that came before. No hidden mutations, no backtracking. Compiler enforces this via type system.

---

### Stage Orchestration Patterns

**Pattern 1: Full Pipeline (full-export)**
```
Stage 0 (auto) → Stage 1 (AllModules) → Stage 2 (full fetch: metadata + stats + data) →
Stage 3 (all transforms) → Stage 4 (global topological sort) →
Stage 5 (emit: schema + StaticSeeds + Bootstrap + diagnostics) → Stage 6 (None - export-only)
```

**Pattern 2: Schema-Only (build-ssdt modules)**
```
Stage 0 (auto/manual) → Stage 1 (AllModules) → Stage 2 (partial fetch: metadata + stats only) →
Stage 3 (type mapping, nullability) → Stage 4 (SKIP - no data ordering) →
Stage 5 (emit: schema + diagnostics) → Stage 6 (SchemaOnly - optional deploy)
```

**Pattern 3: Data-Only (build-ssdt static seeds)**
```
Stage 0 (auto/manual) → Stage 1 (Where(IsStatic)) → Stage 2 (full fetch: metadata + stats + data) →
Stage 3 (determinizer, FK preflight) → Stage 4 (topological sort - WRONG: scoped, should be global) →
Stage 5 (emit: StaticSeeds data + diagnostics) → Stage 6 (MERGE - optional deploy)
```

**Pattern 4: Extract-Only (extract-model verb)**
```
Stage 0 (PRIMARY - this IS the verb) → Stage 1 (AllModules) → Stage 2 (partial fetch: metadata only) →
Stage 3 (minimal transforms) → Stage 4 (SKIP) → Stage 5 (emit: model.json + diagnostics) →
Stage 6 (None - export-only)
```

**Pattern 5: UAT Migration (full-export with UAT-Users)**
```
Stage 0 (auto) → Stage 1 (AllModules) → Stage 2 (full fetch) →
Stage 3 (all transforms + UAT-Users Discovery) → Stage 4 (global sort) →
Stage 5 (emit: schema + [StaticSeeds, Bootstrap with UAT-transformed user FKs] + diagnostics) →
Stage 6 (None - export-only, or Combined if deploying)
```

---

### Summary: Total Constituent Operations

**Stage 0**: 6 sub-operations (connection, discovery, parsing, validation, caching, output)

**Stage 1**: 7 sub-operations (parse config, build catalog, apply filters, validate, output)

**Stage 2**: 14+ sub-operations across 4 sub-stages (metadata, statistics, data, assembly)

**Stage 3**: 12+ distinct transforms, each with multiple sub-operations

**Stage 4**: 12 sub-operations across 4 sub-stages (graph construction, sort execution, validation, output)

**Stage 5**: 40+ sub-operations across 3 major sub-stages (schema, data, diagnostics)

**Stage 6**: 6 sub-operations (strategy selection, validation, execution, output)

**Total**: 100+ atomic constituent operations across the 7 major stages.

---
## PART III: DIMENSIONAL DECOMPOSITION (The Axes of Variation)

### § 12. Axis 1: Selection (SCOPE)

**Ontological Question**: "Which entities participate in this pipeline invocation?"

**The Primitive**: EntitySelector

**Reification Status**:
- `ModuleFilterOptions` exists (src/Osm.Domain/Configuration/ModuleFilterOptions.cs)
- `EntityFilters` property exists (maps module names to entity lists)
- Config parsing works (ModuleFilterSectionReader.cs:65-78)
- **Incomplete**: SQL extraction, validation, and profiling ignore EntityFilters

**Current Manifestations**:
- BuildSsdtBootstrapSnapshotStep: Selects ALL entities with data (static + regular)
- BuildSsdtStaticSeedStep: Selects WHERE(IsStatic)
- BuildSsdtModulesStep: Selects ALL entities for schema emission

**Completion Vector**: Wire EntityFilters into extraction/validation/profiling query scopes (§13)

---

### § 5. Axis 2: Emission (ARTIFACT GENERATION)

**Ontological Question**: "What artifacts are produced and how are they organized?"

**The Primitive**: EmissionStrategy

Emission is **compositional** - different verbs emit different combinations of artifacts:

**Artifact Types:**

1. **Schema Emission** (SSDT Modules folder)
   - CREATE TABLE statements per entity
   - Per-module organization: `Modules/ModuleName/EntityName.table.sql`
   - .sqlproj file references for build integration
   - Emits in fetch order (no topological ordering - DDL doesn't require FK-ordered emission)
   - Current: BuildSsdtModulesStep, CreateTableStatementBuilder

2. **Data Emission** (INSERT/MERGE scripts)
   - Topologically-ordered INSERT/MERGE statements (FK dependencies enforced)
   - Monolithic or per-file organization
   - Multiple data outputs possible: StaticSeeds.sql, Bootstrap.sql
   - Current: BuildSsdtStaticSeedStep (MERGE), BuildSsdtBootstrapSnapshotStep (INSERT)

3. **Diagnostic Emission** (cross-cutting concern)
   - Profiling reports (data quality metrics, null counts, FK orphans)
   - Validation results (constraint violations, warnings)
   - Cycle diagnostics (topological warnings, CircularDependencyOptions applied)
   - Transform execution logs (observability)

**Compositional Invocations** (current verbs as emission compositions):

- **extract-model**: `Emit(model + diagnostics)`
- **build-ssdt**: `Emit(schema + StaticSeeds + diagnostics)` - no bootstrap data
- **full-export**: `Emit(schema + StaticSeeds + Bootstrap + diagnostics)` - complete artifact set

**Reification Status**:
- Fragmented across GroupByModule flags, EmitMasterFile flags, separate schema/data pipelines
- Schema and data treated as completely separate implementations (no unified abstraction)
- Diagnostics emitted ad-hoc, not integrated into emission strategy

**Current Manifestations**:
- Schema emission: Per-module with .sqlproj (SSDT Modules)
- Data emission: Monolithic or broken per-module (StaticSeeds), monolithic only (Bootstrap)
- Diagnostics: Scattered across implementations

**Completion Vector**: Extract unified EmissionStrategy abstraction (§14), unify schema and data emission (§19)

---

### § 6. Axis 3: Insertion (APPLICATION SEMANTICS)

**Ontological Question**: "How does the generated SQL modify the target database?"

**The Primitive**: InsertionStrategy

**Modes:**

1. **Schema-only** - CREATE TABLE statements, no data
   - Use case: SSDT Modules (database schema deployment)
   - Target: Fresh database or schema upgrade

2. **INSERT** - One-time data load (fails on PK duplicates)
   - Use case: Bootstrap (initial deployment), UAT migration
   - Semantics: Fast bulk load, non-idempotent
   - Performance: Fastest for large datasets

3. **MERGE** - Upsert on primary key
   - Use case: Static seeds (refresh on each deployment)
   - Semantics: Insert new rows, update existing rows based on PK match
   - Performance: Slower than INSERT, but idempotent

4. **TRUNCATE + INSERT** - Clear table then load
   - Use case: Complete data replacement
   - Semantics: Destructive, then fast load
   - Performance: Fast, but loses existing data

5. **None** - Export/diagnostic only, no target database modification
   - Use case: Offline artifact generation, CI/CD validation
   - Semantics: Generate SQL scripts without deployment

**Parameters**:
- Batch size (e.g., 1000 rows per INSERT/MERGE batch)
- Merge key (usually primary key, configurable)
- Mode flags (NonDestructive for Bootstrap)

**Reification Status**:
- Fragmented: `StaticSeedSynchronizationMode` enum, `DynamicEntityInsertGenerationOptions` flags
- Schema-only handled separately (BuildSsdtModulesStep)
- No unified abstraction

**Current Manifestations**:
- BuildSsdtStaticSeedStep: MERGE (hardcoded)
- BuildSsdtBootstrapSnapshotStep: INSERT with NonDestructive mode (hardcoded)
- BuildSsdtModulesStep: Schema-only (implicit, no data)

**Completion Vector**: Parameterize InsertionStrategy (§16)

---

## PART III: PRIMITIVE CATALOG (The Building Blocks)

### § 7. Composition Primitives

**Core Abstractions:**
- `IBuildSsdtStep<TInput, TOutput>` - Atomic pipeline step interface
- `.BindAsync()` extension - Monadic step chaining
- `Result<T>` monad - Error handling with railway-oriented programming
- State types - `PipelineInitialized`, `ExtractionCompleted`, `ProfileCompleted`, `BootstrapCompleted`, etc.

**Location**: src/Osm.Pipeline/Orchestration/

**These are protected citizens** - every pipeline uses these abstractions for composition.

---

### § 8. Selection Primitives

**Existing:**
- `ModuleFilterOptions` (src/Osm.Domain/Configuration/ModuleFilterOptions.cs) - Exists, API correct
- `EntityFilters` property (ImmutableDictionary<string, ModuleEntityFilterOptions>) - Exists, not wired
- `ModuleFilterSectionReader` (src/Osm.Pipeline/Configuration/) - Config parsing works

**Needs Extraction:**
- `EntitySelector` - Unified selection API (currently fragmented across implementations)
- Selection predicates: `.AllModules()`, `.Where(e => e.IsStatic)`, `.AllWithData()`

**Completion State**: Primitive exists but incomplete integration (§13)

---

### § 9. Ordering Primitives

**Protected Citizens (always execute for data pipelines):**
- `EntityDependencySorter.SortByForeignKeys()` (src/Osm.Emission/Seeds/EntityDependencySorter.cs)
  - Universal topological sort
  - Used by BuildSsdtStaticSeedStep, BuildSsdtBootstrapSnapshotStep
  - Input: Entity set with FK metadata
  - Output: Globally ordered entity list

**Optional (configuration-driven):**
- `TopologicalOrderingValidator` (src/Osm.Pipeline/Orchestration/BuildSsdtBootstrapSnapshotStep.cs:186-325)
  - Enhanced cycle diagnostics
  - FK analysis, nullable detection, CASCADE warnings
  - Currently only in Bootstrap, needs extraction

- `CircularDependencyOptions` - Configuration for allowed cycles
  - Explicitly declared circular FK relationships
  - Validated during topological sort

**Critical Nuance:**
- Topological ordering mandatory for **data emission** (INSERT/MERGE scripts require FK-ordered statements)
- **Schema emission** (CREATE TABLE) emits in fetch order - no topological ordering required (FK constraints don't impose emission order for DDL)

---

### § 10. Transform Primitives

**Mandatory (Protected Citizens):**
- `DatabaseSnapshot.Fetch()` - Retrieve metadata + statistics + data
  - Protected: Every pipeline must fetch (even if cached)

**Optional/Configurable (Business Logic Transforms):**

*Pre-Sort Transforms:*
- `EntitySeedDeterminizer.Normalize` (BuildSsdtStaticSeedStep.cs:78) - Deterministic data ordering/normalization
- `Module name collision handling` (BuildSsdtStaticSeedStep.cs:196-231) - Disambiguate duplicate module names
- `Supplemental physical→logical remapping` (BuildSsdtBootstrapSnapshotStep.cs:338-361) - Transform table names

*Post-Sort, Pre-Emit Transforms:*
- `StaticSeedForeignKeyPreflight.Analyze` (BuildSsdtStaticSeedStep.cs:90) - FK orphan detection
- `TopologicalOrderingValidator` (BuildSsdtBootstrapSnapshotStep.cs:115-127) - Enhanced cycle diagnostics
- `TypeMappingPolicy` (src/Osm.Smo/TypeMappingPolicy.cs) - Data type transformations
- `NullabilityEvaluator` (src/Osm.Validation/Tightening/NullabilityEvaluator.cs) - Policy-driven nullability tightening
- `UAT-Users Discovery` - FK catalog discovery, orphan detection, mapping validation

*Emission-Time Transforms:*
- `UAT-Users Apply (INSERT mode)` - Pre-transform user FK values during INSERT generation
- `Deferred FK emission` (WITH NOCHECK) - Separate ALTER TABLE statements for FKs with orphans
- `CreateTableFormatter` (src/Osm.Smo/CreateTableFormatter.cs) - SQL formatting, whitespace normalization
- `SmoNormalization.NormalizeSqlExpression` (src/Osm.Smo/SmoNormalization.cs) - Remove redundant parentheses
- `ConstraintFormatter` - FK/PK constraint formatting

*Post-Emit Transforms (legacy/verification):*
- `UAT-Users Apply (UPDATE mode)` - Post-load UPDATE scripts (standalone uat-users verb)

**Detailed catalog with ordering dependencies in §18.**

---

### § 11. Emission Primitives

**Schema Emission:**
- `CreateTableStatementBuilder` (src/Osm.Smo/) - Generate CREATE TABLE DDL
- Per-module file organization (Modules/ModuleName/EntityName.table.sql)
- `.sqlproj` generation with module references

**Data Emission:**
- Batch INSERT generators (src/Osm.Emission/Seeds/)
- Batch MERGE generators (StaticSeedSqlBuilder)
- Monolithic file writer (single .sql file)
- Per-file organization (future: .sqlproj ordering for data)

**Diagnostic Emission:**
- Profiling report writers (CSV, JSON)
- Validation result logs (structured warnings/errors)
- Cycle diagnostic output (CircularDependencyOptions violations)
- Transform execution logs (observability, duration metrics)

**Operator Usability (Real-time + Persistent):**
- Spectre.Console progress bars (real-time, visible during execution)
- Console output formatting (user-facing feedback)
- Log file emission (persistent diagnostics)

---

## PART IV: COMPLETION VECTORS (From Fragmented to Unified)

**This is the execution blueprint - the most detailed section of this document.**

### § 12. Current Manifestations as Amino Acids

Each current implementation demonstrates part of the unified pattern. These are the building blocks we'll compose into the complete architecture.

**BuildSsdtBootstrapSnapshotStep** (src/Osm.Pipeline/Orchestration/BuildSsdtBootstrapSnapshotStep.cs):
- Demonstrates: Global sort across ALL entity categories (static + regular)
- Parameters: scope=AllWithData, emission=Monolithic-Data, insertion=INSERT
- Advanced features: TopologicalOrderingValidator, CircularDependencyOptions, NonDestructive mode
- Amino acids: Global entity selection, unified topological sort, enhanced cycle diagnostics

**BuildSsdtStaticSeedStep** (src/Osm.Pipeline/Orchestration/BuildSsdtStaticSeedStep.cs):
- Demonstrates: MERGE insertion, per-module emission attempt (broken - no .sqlproj ordering)
- Parameters: scope=Static, emission=Monolithic-or-PerModule-Data, insertion=MERGE
- Missing: Global sort (only sorts static entities), no TopologicalOrderingValidator
- Amino acids: MERGE generation, EntitySeedDeterminizer, StaticSeedForeignKeyPreflight, module name collision handling

**BuildSsdtModulesStep** (src/Osm.Pipeline/Orchestration/BuildSsdtModulesStep.cs):
- Demonstrates: Schema emission, per-module organization, .sqlproj generation
- Parameters: scope=ALL, emission=PerModule-Schema, insertion=SchemaOnly
- Emits in fetch order (no topological sort - DDL doesn't require FK-ordered emission)
- Amino acids: CREATE TABLE generation, per-module file organization, .sqlproj topological ordering

**Incidental Patterns** (barely worth mentioning):
- Supplemental entity loading: Workaround for EntityFilters not being wired; Bootstrap integrates it correctly, StaticSeeds doesn't
- DynamicData: Redundant with Bootstrap (INSERT of all entities), scheduled for deletion

---

### § 13. Vector 1: Complete EntityFilters Wiring

**Current State:**
- `EntityFilters` exists in `ModuleFilterOptions` (src/Osm.Domain/Configuration/ModuleFilterOptions.cs)
- Config parsing works (ModuleFilterSectionReader.cs:65-78)
- JSON schema: `{ "modules": [{ "name": "ServiceCenter", "entities": ["User"] }] }`

**The Gap:**
- SQL extraction queries ignore `EntityFilters` (fetch all entities from modules)
- Validation scope ignores it (validates all entities)
- Profiling scope ignores it (profiles all entities)

**Completion Work:**

1. **Wire into SqlModelExtractionService**
   - Modify OSSYS_* metadata queries to respect EntityFilters
   - If module has `entities: ["User"]`, only extract User metadata
   - If module has `entities: []`, extract all entities (current behavior)

2. **Wire into SqlDataProfiler**
   - Scope profiling queries to selected entities only
   - Skip profiling for entities not in EntityFilters selection

3. **Wire into Validation**
   - Scope validation to selected entities
   - Don't validate entities excluded by EntityFilters

4. **Test Single-Entity Selection**
   - Verify `Include("ServiceCenter", only: ["User"])` works end-to-end
   - Extract only User metadata, profile only User, emit only User schema/data

5. **Verify Supplemental Obsolescence**
   - Current Supplemental workaround (manual JSON files) becomes unnecessary
   - EntityFilters provides first-class single-entity selection

**Result:**
- EntityFilters fully functional across all pipeline stages
- Single-entity selection becomes first-class capability
- EntitySelector API emerges naturally from wired EntityFilters

**Ordering Dependencies**:
- Must complete before any pipeline execution (affects Stage 1/2)
- Foundational - other vectors depend on selection working correctly

**Complexity**: Medium (localized changes to query builders, well-defined scope)

---

### § 14. Vector 2: Extract EmissionStrategy Abstraction

**Current State:**

**Schema Emission:**
- BuildSsdtModulesStep: Per-module with .sqlproj
- Separate implementation from data emission
- Emits CREATE TABLE statements in fetch order

**Data Emission:**
- BuildSsdtBootstrapSnapshotStep: Monolithic only (hardcoded)
- BuildSsdtStaticSeedStep: Monolithic or per-module via `GroupByModule` flag
- Per-module data emission broken (no .sqlproj ordering, breaks cross-module FK dependencies)

**Diagnostic Emission:**
- Ad-hoc, scattered across implementations
- Profiling writes to separate output directory
- No unified abstraction for diagnostic artifacts

**The Problem:**
- Schema and data treated as completely separate pipelines (duplicate orchestration)
- No way to emit both schema and data in single invocation
- Diagnostic emission not integrated into emission strategy
- File organization logic duplicated

**Completion Work:**

1. **Recognize Schema/Data/Diagnostics as Emission Variants**
   - Different artifact types, same conceptual stage (Stage 5)
   - Emission is compositional: can emit schema-only, data-only, or combined

2. **Extract Unified EmissionStrategy Abstraction**
   ```csharp
   public abstract class EmissionStrategy
   {
       public static EmissionStrategy SchemaPerModule(string directory);
       public static EmissionStrategy DataMonolithic(string path);
       public static EmissionStrategy DataWithSqlProj(string directory); // Future: ordered per-module data
       public static EmissionStrategy DiagnosticsTo(string directory);
       public static EmissionStrategy Combined(
           EmissionStrategy? schema = null,
           EmissionStrategy? data = null,
           EmissionStrategy? diagnostics = null
       );
   }
   ```

3. **Unify File Organization Logic**
   - Consolidate duplicate per-module logic (BuildSsdtModulesStep vs BuildSsdtStaticSeedStep)
   - Single implementation of directory creation, file naming, .sqlproj generation

4. **Fix Broken Per-Module Data Emission**
   - StaticSeeds `GroupByModule` currently emits separate files per module
   - Problem: No .sqlproj ordering, breaks cross-module FK dependencies
   - Solution: Implement DataWithSqlProj that emits per-module files with topological .sqlproj ordering

5. **Integrate Diagnostic Emission**
   - Treat diagnostics as first-class emission artifact type
   - Profiling reports, validation logs, cycle warnings all go through EmissionStrategy

**Result:**
- Schema and data emission unified under same abstraction
- Can emit schema-only (build-ssdt modules), data-only (bootstrap), or combined (full-export)
- Per-module data emission works correctly (topological ordering via .sqlproj)
- Diagnostic emission integrated, not ad-hoc
- File organization logic consolidated (eliminate duplication)

**Ordering Dependencies**:
- Happens after topological sort (Stage 5)
- Independent of selection and insertion strategies

**Complexity**: High (touches multiple emission implementations, requires careful refactoring)

---

### § 15. Vector 3: Unify Topological Sort Scope

**Current State:**

**StaticSeeds** (BuildSsdtStaticSeedStep.cs:82-86):
- Sorts only static entities (scoped subset)
- `EntityDependencySorter.SortByForeignKeys(staticEntities)`
- Problem: Misses cross-category FK dependencies (regular → static, static → regular)

**Bootstrap** (BuildSsdtBootstrapSnapshotStep.cs:105-110):
- Sorts ALL entities (static + regular, global graph)
- `EntityDependencySorter.SortByForeignKeys(allEntities)` where allEntities = static ∪ regular
- Correct pattern - demonstrates global sort working

**SSDT Modules** (BuildSsdtModulesStep):
- No data emission, no topological sort required
- Emits CREATE TABLE in fetch order (DDL doesn't require FK-ordered emission)

**The Problem:**

Foreign keys don't respect entity categories:
- A regular entity can reference a static entity (e.g., User references Role where Role is static)
- A static entity can reference a regular entity (rare but possible)
- If you sort separately, you miss cross-category dependencies

**Example FK Violation** (current StaticSeeds):
```sql
-- StaticSeeds emits static entities only, sorted among themselves
INSERT INTO [StaticEntity1] VALUES (...);  -- References RegularEntity1 (FK violation - not emitted!)
INSERT INTO [StaticEntity2] VALUES (...);

-- RegularEntity1 is not in StaticSeeds scope, FK constraint fails on deployment
```

**Completion Work:**

1. **Extract TopologicalOrderingValidator**
   - Currently only in Bootstrap (BuildSsdtBootstrapSnapshotStep.cs:186-325)
   - Enhanced cycle diagnostics, FK analysis, nullable detection, CASCADE warnings
   - Make reusable across all data pipelines

2. **Establish Global Sort Invariant**
   - Sort ALWAYS spans ALL selected entities (never scoped to category)
   - StaticSeeds: Select static entities, but sort globally, then filter to static for emission
   - Bootstrap: Select all entities, sort globally, emit all (current behavior)

3. **Refactor StaticSeeds to Use Global Sort**
   ```csharp
   // Current (WRONG):
   var staticEntities = allEntities.Where(e => e.IsStatic);
   var sorted = EntityDependencySorter.SortByForeignKeys(staticEntities);  // ❌ Scoped sort

   // Correct (AFTER Vector 3):
   var selectedEntities = EntitySelector.Where(e => e.IsStatic);
   var allEntities = DatabaseSnapshot.GetAllEntities();  // Include regular entities for sort
   var globalSorted = EntityDependencySorter.SortByForeignKeys(allEntities);  // ✅ Global sort
   var staticSorted = globalSorted.Where(e => selectedEntities.Contains(e));  // Filter after sort
   ```

4. **Update SSDT Modules** (no change needed)
   - Schema emission doesn't require topological sort
   - CREATE TABLE can be emitted in any order (FK constraints are declarative, order-independent)
   - Current behavior (emit in fetch order) is correct

**Result:**
- Cross-category FK dependencies resolved correctly
- StaticSeeds can safely reference regular entities (no FK violations on deployment)
- Supplemental entities automatically included in global sort (if selected)
- One dependency graph, multiple emission strategies filter it

**Ordering Dependencies**:
- Mandatory Stage 4 for data pipelines
- Always happens after fetch, before data emission
- Schema emission (SSDT Modules) bypasses this stage

**Complexity**: Medium (requires StaticSeeds refactor, TopologicalOrderingValidator extraction)

---

### § 16. Vector 4: Parameterize InsertionStrategy

**Current State:**

**StaticSeeds**: MERGE hardcoded
- `StaticSeedSynchronizationMode` enum (src/Osm.Emission/Seeds/StaticSeedSqlBuilder.cs)
- Generates MERGE statements (INSERT or UPDATE based on PK match)

**Bootstrap**: INSERT hardcoded
- NonDestructive mode flag (prevents TRUNCATE)
- Generates INSERT statements only

**SSDT Modules**: Schema-only (implicit, no data insertion)
- Emits CREATE TABLE, no INSERT/MERGE

**The Problem:**
- Insertion strategy baked into separate implementations (not parameterized)
- Can't use StaticSeeds with INSERT (requires MERGE)
- Can't use Bootstrap with MERGE (requires INSERT)
- No abstraction for insertion semantics

**Completion Work:**

1. **Extract Unified InsertionStrategy Abstraction**
   ```csharp
   public abstract class InsertionStrategy
   {
       public static InsertionStrategy SchemaOnly();
       public static InsertionStrategy Insert(int batchSize = 1000, bool nonDestructive = false);
       public static InsertionStrategy Merge(MergeKey on, int batchSize = 1000);
       public static InsertionStrategy TruncateAndInsert(int batchSize = 1000);
       public static InsertionStrategy None();  // Export/diagnostics only
   }
   ```

2. **Unify Batch Generation Logic**
   - Consolidate INSERT generator (currently in BuildSsdtBootstrapSnapshotStep)
   - Consolidate MERGE generator (currently in StaticSeedSqlBuilder)
   - Single implementation, parameterized by InsertionStrategy

3. **Make Insertion Strategy Runtime Parameter**
   - Pipeline accepts InsertionStrategy configuration
   - Same topologically-sorted entities, different SQL generation based on strategy
   - No implementation fork (StaticSeeds vs Bootstrap)

4. **Support New Strategies**
   - TruncateAndInsert: `TRUNCATE TABLE; INSERT ...` (fast, destructive)
   - None: Generate scripts but don't apply (export/CI/CD validation)

**Result:**
- Same topologically-sorted entities, different application semantics via configuration
- Schema-only, data-only, or combined become configuration choices
- New insertion strategies trivial to add (just parameter variants)
- StaticSeeds and Bootstrap collapse into parameterizations of unified pipeline

**Ordering Dependencies**:
- Final stage (Stage 6), after emission
- Independent of selection, ordering, and emission strategies

**Complexity**: Medium (consolidate batch generators, parameterize SQL generation)

---

### § 17. Vector 5: Integrate Extract-Model as Stage 0

**Current State:**

Users manually run two separate commands:

```bash
# Step 1: Extract model metadata from live database
$ osm extract-model --connection-string "..." --output model.json

# Step 2: Feed extracted model into build/export
$ osm build-ssdt --model model.json
$ osm full-export --model model.json
```

**Problems:**
- Manual orchestration required
- Model file passed around as artifact (file path coordination)
- No caching or reuse between runs (re-extract every time)
- Extract-model feels separate, not integrated into pipeline

**Completion Work:**

1. **Extract-Model Becomes Automatic Stage 0**
   - Every pipeline invocation starts with extract-model (unless `--model` override provided)
   - User provides `--connection-string`, model extraction happens automatically
   - Single command invocation, no manual coordination

2. **DatabaseSnapshot Caching**
   - Cache extracted model to disk (`.cache/database-snapshot.json`)
   - Checksum-based invalidation (re-extract when schema changes)
   - Cache includes metadata (OSSYS_*) + statistics (profiling) + data (OSUSR_*)
   - Subsequent runs reuse cache if database schema unchanged (faster iteration)

3. **Offline Scenarios Still Supported**
   - `--model` flag override: Provide pre-extracted model for offline use
   - CI/CD can cache model artifacts between runs
   - Backward compatible with current manual workflow

4. **Full Pipeline Invocation**
   ```bash
   # NEW (automatic extract-model):
   $ osm build-ssdt --connection-string "..." --build-out ./out

   # Model extracted automatically, cached to .cache/database-snapshot.json
   # Reuse cache on subsequent runs if schema unchanged

   # Offline mode (legacy):
   $ osm build-ssdt --model ./model.json --build-out ./out
   ```

**Result:**
- One command execution, model management automatic
- Faster iteration (cache hits when schema unchanged)
- No manual artifact passing between commands
- Offline scenarios still supported via explicit `--model` override

**Ordering Dependencies**:
- Always Stage 0, before selection (provides input for all subsequent stages)
- Cache invalidation must happen before fetch

**Complexity**: Low-Medium (integrate existing extract-model into pipeline, add caching layer)

---

### § 18. Vector 6: Consolidate Transform Primitives

**This is the MOST COMPLEX vector - requires phased excavation approach.**

**Current State:**
- Transforms scattered across implementations (Bootstrap, StaticSeeds, Modules)
- Ordering dependencies unclear and undocumented
- Some transforms duplicated (e.g., module name collision handling)
- Cataloging incomplete - unknown unknowns exist

**The Risk:**
- Business logic embedded in implementations, not extracted
- Unknown ordering constraints (what must run before what?)
- Unknown edge cases and special handling
- Refactoring without complete catalog risks breaking existing behavior

**Phased Excavation Strategy:**

---

**Phase 1: Excavation (Discovery)**

Systematic audit of entire codebase for transform logic.

**Search Strategy:**
1. Grep for transformation patterns: normalize, remap, override, policy, evaluate, preflight
2. Read all `BuildSsdt*Step.cs` files line-by-line (transformations embedded in orchestration)
3. Examine all classes in src/Osm.Smo/, src/Osm.Validation/, src/Osm.Emission/
4. Review configuration classes (TighteningOptions, CircularDependencyOptions, TypeMappingPolicy)

**Documentation Template** (for each discovered transform):
```markdown
## Transform: [Name]

**What it does**: [Input → Output transformation]
**Configuration**: [Config-driven? Hardcoded? Optional?]
**Current Location**: [File path and line numbers]
**Ordering Requirements**: [Must run before X, must run after Y]
**Protected Citizen?**: [Mandatory or optional?]
```

---

**Phase 2: Catalog Construction**

Build comprehensive transform registry with explicit ordering dependencies.

**Transform Catalog** (current known transforms, continuously updated):

| Transform | Stage | Protected? | Config? | Must Run Before | Must Run After | Location |
|-----------|-------|------------|---------|-----------------|----------------|----------|
| **DatabaseSnapshot.Fetch** | 2 | **YES** | No | Everything | Selection | (mandatory) |
| **EntityDependencySorter** | 4 | **YES** (data only) | No | Emit, Apply | Fetch, Transform | EntityDependencySorter.cs |
| EntitySeedDeterminizer.Normalize | 3 | No | No | Sort | Fetch | BuildSsdtStaticSeedStep.cs:78 |
| Module name collision handling | 3 | No | No | Emit | Selection | BuildSsdtStaticSeedStep.cs:196-231 |
| Supplemental physical→logical remap | 3 | No | No | Emit | Fetch | BuildSsdtBootstrapSnapshotStep.cs:338-361 |
| TypeMappingPolicy | 3 | No | Yes (config) | Emit | Fetch | TypeMappingPolicy.cs |
| NullabilityEvaluator | 3 | No | Yes (TighteningOptions) | Emit | Profiling | NullabilityEvaluator.cs |
| UAT-Users Discovery | 3 | No | Yes (inventories) | UAT-Users Apply | FK catalog | (uat-users verb) |
| StaticSeedForeignKeyPreflight | 4 | No | No | Emit | Sort | BuildSsdtStaticSeedStep.cs:90 |
| TopologicalOrderingValidator | 4 | No | Yes (CircularDeps) | Emit | Sort | BuildSsdtBootstrapSnapshotStep.cs:186-325 |
| UAT-Users Apply (INSERT mode) | 5 | No | Yes (mapping) | - | UAT-Users Discovery | (full-export integration) |
| Deferred FK (WITH NOCHECK) | 5 | No | Yes (profiling) | - | CREATE TABLE | CreateTableStatementBuilder.cs:190-195 |
| CreateTableFormatter | 5 | No | Yes (SmoFormatOptions) | - | CREATE TABLE base | CreateTableFormatter.cs |
| SmoNormalization | 5 | No | No | - | SQL expr generation | SmoNormalization.cs |
| ConstraintFormatter | 5 | No | Yes | - | FK/PK generation | ConstraintFormatter.cs |
| UAT-Users Apply (UPDATE mode) | 6 | No | Yes (mapping) | - | Data emission | (standalone uat-users verb) |

**Unknown Unknowns:**
- Additional transforms likely exist (to be discovered during Phase 1)
- Ordering constraints may be more complex than documented
- Edge cases and special handling may emerge during excavation

---

**Phase 3: Extraction (Consolidation)**

Extract each transform into independently testable, composable units.

**For State-Transforming Transforms:**
```csharp
// Extract as IBuildSsdtStep implementation
public class EntitySeedDeterminizerStep : IBuildSsdtStep<FetchCompleted, NormalizedData>
{
    public async Task<Result<NormalizedData>> ExecuteAsync(
        FetchCompleted state,
        CancellationToken cancellationToken = default)
    {
        var normalized = EntitySeedDeterminizer.Normalize(state.EntityData);
        return new NormalizedData(state, normalized);
    }
}
```

**For Utility Transforms:**
```csharp
// Extract as standalone service
public interface IModuleNameCollisionResolver
{
    ImmutableArray<ModuleWithUniqueName> ResolveCollisions(
        ImmutableArray<Module> modules);
}
```

**Add Diagnostic Logging:**
- Each transform logs execution (start, duration, warnings)
- Observability for debugging (which transforms ran, in what order, how long)
- Structured logging for automation (JSON output for CI/CD)

**Build Regression Test Suite:**
- Each transform has dedicated unit tests
- Integration tests for ordering constraints
- Edge case coverage (circular dependencies, orphans, collisions)

---

**Phase 4: Registry & Composition Engine**

Create transform composition engine with ordering enforcement.

**Transform Registry:**
```csharp
public class TransformRegistry
{
    // Mandatory transforms (protected citizens) - always run
    public ImmutableArray<IBuildSsdtStep> MandatoryTransforms { get; }

    // Optional transforms - configuration-driven
    public ImmutableArray<RegisteredTransform> OptionalTransforms { get; }
}

public record RegisteredTransform(
    string Name,
    IBuildSsdtStep Step,
    OrderingConstraints Constraints,
    Func<Configuration, bool> EnabledPredicate
);

public record OrderingConstraints(
    ImmutableArray<string> MustRunBefore,
    ImmutableArray<string> MustRunAfter
);
```

**Composition Engine:**
```csharp
public class TransformComposer
{
    public Result<ImmutableArray<IBuildSsdtStep>> ComposeTransforms(
        Configuration config,
        TransformRegistry registry)
    {
        // 1. Select enabled transforms (mandatory + config-enabled optional)
        var enabled = SelectEnabledTransforms(config, registry);

        // 2. Validate ordering constraints (detect circular dependencies)
        var validation = ValidateOrdering(enabled);
        if (validation.HasErrors)
            return Result.Failure(validation.Errors);

        // 3. Topologically sort transforms by ordering constraints
        var ordered = TopologicalSort(enabled);

        // 4. Return composed pipeline
        return Result.Success(ordered);
    }
}
```

**Diagnostic Output:**
```
Transform Execution Plan:
  1. DatabaseSnapshot.Fetch (mandatory, 0ms)
  2. EntitySeedDeterminizer.Normalize (optional, enabled, 15ms)
  3. ModuleNameCollisionResolver (optional, enabled, 2ms)
  4. TypeMappingPolicy (optional, enabled, 120ms)
  5. EntityDependencySorter (mandatory, 450ms)
  6. TopologicalOrderingValidator (optional, enabled, 35ms)
  7. CreateTableFormatter (optional, enabled, 80ms)

Total transform time: 702ms
```

---

**Critical Principles (DO NOT VIOLATE):**

1. **Model + Data = Source of Truth**
   - Application does NOT invent data
   - Application does NOT silently fix inconsistencies
   - If data contradicts model, WARN operator and require explicit resolution

2. **Warn, Don't Auto-Fix**
   - If `isMandatory=true` but column is nullable → WARN operator
   - Operator provides explicit config approval for NOT NULL (TighteningOptions)
   - NEVER auto-coerce based on "no NULLs observed in data" (profiling is informational)

3. **Profiling is Informational, Not Prescriptive**
   - Profiling finds issues (null counts, FK orphans, uniqueness violations)
   - Operator decides resolution (WITH NOCHECK, fix data, override config)
   - "Cautious mode" prevents automatic coercion

4. **Preserve Debuggability**
   - Each transform independently toggleable (can disable for testing)
   - Each transform independently runnable (unit testable in isolation)
   - Transforms log execution (observability for debugging)

---

**Result:**

After completing all four phases:
- All transforms cataloged, understood, and documented
- Ordering explicit and enforced by composition engine
- No hidden business logic scattered in implementations
- Each transform independently testable and debuggable
- New transforms have clear integration points and ordering constraints
- Composition engine validates ordering, detects circular dependencies

**Ordering Dependencies**:
- Complex - transforms span Stages 2-6 with intricate dependencies (see catalog)
- Some transforms independent (can run in parallel or any order)
- Some transforms have strict ordering (must run before/after specific other transforms)

**Complexity**: **Very High** - This is the riskiest vector
- Requires systematic excavation (can't skip this phase)
- Unknown unknowns exist (more transforms will be discovered)
- Ordering constraints may be subtle (breaks if violated)
- Comprehensive testing required (regression coverage mandatory)

**Recommended Approach**: Dedicated excavation phase in execution plan, DO NOT rush

---

### § 19. Vector 7: Unify Schema and Data Emission

**Current State:**

**SSDT Modules** (BuildSsdtModulesStep):
- Emits CREATE TABLE statements (schema)
- Per-module organization (Modules/ModuleName/EntityName.table.sql)
- Generates .sqlproj file with module references
- Separate pipeline, completely independent from data emission

**Bootstrap/StaticSeeds** (BuildSsdtBootstrapSnapshotStep, BuildSsdtStaticSeedStep):
- Emit INSERT/MERGE statements (data)
- Monolithic or per-module organization
- Separate pipeline, completely independent from schema emission

**The Problem:**
- Treated as completely separate pipelines (different steps, no coordination)
- No way to emit both schema and data in single pipeline invocation
- Duplicate file organization logic (per-module handling in both Modules and StaticSeeds)
- Can't configure "schema + data" in one command

**Completion Work:**

1. **Recognize Schema and Data as Emission Variants**
   - Different artifact types (DDL vs DML), same conceptual stage (Stage 5: Emit)
   - Both are outputs from the same entity set, just different SQL generation

2. **Unified Pipeline Emits Multiple Artifact Types**
   - **Schema only**: SSDT Modules use case (DDL, no data)
   - **Data only**: Bootstrap/StaticSeeds use case (DML, no schema)
   - **Both**: New capability - full-export emits schema + data in one invocation

3. **EmissionStrategy Supports Multiple Outputs**
   ```csharp
   emit: EmissionStrategy.Combined(
       schema: PerModule("Modules/"),           // CREATE TABLE per module
       data: [
           Monolithic("StaticSeeds/StaticSeeds.sql"),  // MERGE static entities
           Monolithic("Bootstrap/Bootstrap.sql")       // INSERT all entities
       ],
       diagnostics: Directory("Diagnostics/")   // Profiling, validation logs
   )
   ```

4. **Consolidate File Organization Logic**
   - Single implementation of per-module directory creation
   - Single implementation of .sqlproj generation
   - Eliminate duplication between BuildSsdtModulesStep and BuildSsdtStaticSeedStep

5. **Support Compositional Invocations**
   - **extract-model**: `Emit(model + diagnostics)` - no schema, no data
   - **build-ssdt**: `Emit(schema + StaticSeeds + diagnostics)` - schema + StaticSeeds data, no bootstrap
   - **full-export**: `Emit(schema + StaticSeeds + Bootstrap + diagnostics)` - complete artifact set

**Result:**
- Schema and data pipelines unified under single abstraction
- Can emit schema-only, data-only, or both in single invocation
- Diagnostic emission integrated as first-class artifact type
- File organization logic consolidated (eliminate duplication)
- Compositional invocations natural (mix and match schema/data/diagnostics)

**Ordering Dependencies**:
- Stage 5 (Emit), after topological sort
- Sort provides ordering for data emission (INSERT/MERGE must be FK-ordered)
- Schema emission doesn't require sort (CREATE TABLE order-independent)

**Complexity**: Medium-High (requires unifying separate pipelines, careful coordination)

---

### § 20. Vector 8: UAT-Users Integration (Full-Export Mode)

**Current State:**

UAT-users exists as standalone verb (docs/verbs/uat-users.md):
- Discovers FK columns referencing User table
- Analyzes live data for orphan user IDs (QA users not in UAT roster)
- Generates UPDATE scripts to transform user FKs post-load (legacy mode)

**Recent Enhancement** (M2.2 - docs/implementation-specs/M2.2-insert-transformation-implementation.md):
- Pre-transformed INSERT generation (recommended mode)
- Transforms user FK values DURING INSERT generation (not post-load)
- Integrated into full-export via `--enable-uat-users` flag

**How UAT-Users Works (Full-Export Integration):**

**Stage 3: Transform (Discovery Phase)**
1. Discover FK catalog (which columns reference User.Id)
2. Load QA user inventory (./qa_users.csv - Service Center export)
3. Load UAT user inventory (./uat_users.csv - approved UAT roster)
4. Identify orphans (QA user IDs not in UAT roster)
5. Load user mapping (./uat_user_map.csv - orphan → UAT user)
6. Validate mapping (source in QA, target in UAT, no duplicates)
7. Build TransformationContext (passes mapping to INSERT generator)

**Stage 5: Emit (Application Phase - INSERT Mode)**
8. During DynamicEntityInsertGenerator execution:
   - For each user FK column, transform value: orphan ID → UAT ID
   - Generate INSERT scripts with UAT-ready values (pre-transformed)
9. Output: `DynamicData/**/*.dynamic.sql` contains UAT user IDs (not QA IDs)

**Stage 6: Apply (Alternative - UPDATE Mode, Legacy/Verification)**
10. Standalone uat-users verb generates UPDATE scripts
11. Use case: Legacy migration (transform existing UAT database with QA data)
12. Use case: Verification artifact (cross-validate transformation logic)

**The Dual-Mode Pattern:**
- **Discovery** (what to transform): Always Stage 3
- **Application** (when to transform): Stage 5 (INSERT mode, recommended) OR Stage 6 (UPDATE mode, legacy)

**Completion Work:**

This vector is largely complete (M2.2 implemented pre-transformed INSERTs). Remaining work:

1. **Ensure TransformationContext Wiring**
   - FullExportCoordinator runs uat-users discovery before build
   - TransformationContext passed to DynamicEntityInsertGenerator
   - Transformations applied in-memory during value emission

2. **Verify Pre-Transformed Output**
   - Manual inspection: `DynamicData/**/*.dynamic.sql` contains UAT user IDs
   - Automated verification: M2.3 (deferred, not critical path)

3. **Document Dual-Mode Support**
   - INSERT mode (recommended): Pre-transformed during emission (fast, atomic, idempotent)
   - UPDATE mode (legacy): Post-load transformation scripts (verification artifact)

**Result:**
- UAT migration as compositional transform (discovery + application)
- Pre-transformed INSERTs (recommended) - faster deployment, simpler rollback
- UPDATE scripts (legacy) - still available for verification or existing UAT databases
- Full-export integration complete

**Ordering Dependencies**:
- Discovery (Stage 3): After FK catalog available, before INSERT generation
- Application (Stage 5 INSERT mode): During INSERT emission
- Application (Stage 6 UPDATE mode): Post-emission, standalone

**Complexity**: Medium (integration work complete, verification deferred to M2.3)

---

### § 21. Additional Vectors (To Be Defined)

Additional completion vectors will emerge during implementation:

- **Vector 9**: Caching Strategy (DatabaseSnapshot invalidation, incremental updates)
- **Vector 10**: Parallel Execution (independent transforms run concurrently)
- **Vector 11**: Error Recovery (partial pipeline failures, resume from checkpoint)
- **Vector 12**: Diagnostic Enrichment (Spectre.Console integration, real-time progress)
- **Vector 13**: Configuration Schema Validation (JSON schema for cli.json, fail-fast on invalid config)

These vectors will be documented as implementation proceeds.

---

## PART V: THE UNIFIED INVOCATION (What Victory Looks Like)

### § 22. The Convergence

All current use cases become parameterizations of the unified pipeline. Same invariant flow, different dimensional parameters. No special cases, no implementation forks.

---

**Use Case 1: SSDT Schema Emission**
Replaces: `BuildSsdtModulesStep`

```csharp
await EntityPipeline.ExecuteAsync(
    source: DatabaseSnapshot.From(connectionString),
    select: EntitySelector.AllModules(),
    emit: EmissionStrategy.SchemaPerModule("out/Modules/"),
    apply: InsertionStrategy.SchemaOnly(),
    transforms: config.Transforms.Where(t => t.AppliesToSchema),
    options: config
);
```

**Artifacts Emitted:**
- `Modules/ModuleName/EntityName.table.sql` (CREATE TABLE per entity)
- `Modules.sqlproj` (module references)
- `Diagnostics/schema-emission.log`

**No topological sort** (schema emits in fetch order, DDL order-independent)

---

**Use Case 2: Static Seeds Data**
Replaces: `BuildSsdtStaticSeedStep`

```csharp
await EntityPipeline.ExecuteAsync(
    source: DatabaseSnapshot.From(connectionString),
    select: EntitySelector.Where(e => e.IsStatic),
    emit: EmissionStrategy.DataMonolithic("StaticSeeds/StaticSeeds.sql"),
    apply: InsertionStrategy.Merge(on: PrimaryKey, batchSize: 1000),
    transforms: config.Transforms.All(),
    options: config
);
```

**Artifacts Emitted:**
- `StaticSeeds/StaticSeeds.sql` (MERGE statements, FK-ordered)
- `Diagnostics/static-seeds-profile.csv`

**Topological sort required** (data emission must respect FK dependencies)

---

**Use Case 3: Bootstrap All Entities**
Replaces: `BuildSsdtBootstrapSnapshotStep`

```csharp
await EntityPipeline.ExecuteAsync(
    source: DatabaseSnapshot.From(connectionString),
    select: EntitySelector.AllWithData(),  // Static + regular (supplemental concept disappears)
    emit: EmissionStrategy.DataMonolithic("Bootstrap/Bootstrap.sql"),
    apply: InsertionStrategy.Insert(mode: NonDestructive, batchSize: 1000),
    transforms: config.Transforms.All(),
    options: config.CircularDependencies
);
```

**Artifacts Emitted:**
- `Bootstrap/Bootstrap.sql` (INSERT statements, globally FK-ordered)
- `Diagnostics/bootstrap-cycles.txt` (topological warnings)

**Topological sort required** (global sort across all entities)

---

**Use Case 4: build-ssdt**
Current verb: `build-ssdt` (schema + StaticSeeds, no bootstrap)

```csharp
await EntityPipeline.ExecuteAsync(
    source: DatabaseSnapshot.From(connectionString),
    select: EntitySelector.AllModules(),
    emit: EmissionStrategy.Combined(
        schema: PerModule("Modules/"),                      // SSDT schema
        data: Monolithic("StaticSeeds/StaticSeeds.sql"),    // StaticSeeds MERGE
        diagnostics: Directory("Diagnostics/")
    ),
    apply: InsertionStrategy.Combined(
        schema: SchemaOnly(),                               // CREATE TABLE (no data)
        data: Merge(on: PrimaryKey, batchSize: 1000)        // StaticSeeds MERGE
    ),
    transforms: config.Transforms.All(),
    options: config
);
```

**Artifacts Emitted:**
- `Modules/**/*.table.sql` (CREATE TABLE per entity, per module)
- `Modules.sqlproj` (module references)
- `StaticSeeds/StaticSeeds.sql` (MERGE static entities, FK-ordered)
- `Diagnostics/profiling-report.csv`

**Compositional emission**: Schema + StaticSeeds data, no Bootstrap data

---

**Use Case 5: full-export**
Current verb: `full-export` (schema + StaticSeeds + Bootstrap + diagnostics)

```csharp
await EntityPipeline.ExecuteAsync(
    source: DatabaseSnapshot.From(connectionString),
    select: EntitySelector.AllModules(),
    emit: EmissionStrategy.Combined(
        schema: PerModule("Modules/"),                      // SSDT schema
        data: [
            Monolithic("StaticSeeds/StaticSeeds.sql"),      // StaticSeeds MERGE
            Monolithic("Bootstrap/Bootstrap.sql")            // Bootstrap INSERT
        ],
        diagnostics: Directory("Diagnostics/")
    ),
    apply: InsertionStrategy.Combined(
        schema: SchemaOnly(),
        data: [
            Merge(on: PrimaryKey, batchSize: 1000),         // StaticSeeds
            Insert(mode: NonDestructive, batchSize: 1000)   // Bootstrap
        ]
    ),
    transforms: config.Transforms.All(),
    options: config
);
```

**Artifacts Emitted:**
- `Modules/**/*.table.sql` (CREATE TABLE)
- `Modules.sqlproj`
- `StaticSeeds/StaticSeeds.sql` (MERGE static entities)
- `Bootstrap/Bootstrap.sql` (INSERT all entities)
- `Diagnostics/profiling-report.csv`, `validation-results.log`, `cycles.txt`

**Compositional emission**: Schema + [StaticSeeds data, Bootstrap data] + diagnostics (complete artifact set)

---

**Use Case 6: full-export with UAT-Users**
Current verb: `full-export --enable-uat-users` (full-export + pre-transformed user FKs)

```csharp
await EntityPipeline.ExecuteAsync(
    source: DatabaseSnapshot.From(connectionString),
    select: EntitySelector.AllModules(),
    emit: EmissionStrategy.Combined(
        schema: PerModule("Modules/"),
        data: [
            Monolithic("StaticSeeds/StaticSeeds.sql"),
            Monolithic("DynamicData/DynamicData.sql")        // Pre-transformed (UAT-ready)
        ],
        diagnostics: Directory("Diagnostics/")
    ),
    apply: InsertionStrategy.Combined(
        schema: SchemaOnly(),
        data: [
            Merge(on: PrimaryKey, batchSize: 1000),
            Insert(mode: NonDestructive, batchSize: 1000)
        ]
    ),
    transforms: config.Transforms.All()
        .Include(new UatUsersTransform(
            qaInventory: "./qa_users.csv",
            uatInventory: "./uat_users.csv",
            mapping: "./user_map.csv",
            mode: TransformMode.PreTransformedInserts       // Stage 5 application
        )),
    options: config
);
```

**How UAT-Users Integrates:**
1. **Stage 3 (Discovery)**: FK catalog discovery, orphan detection, mapping validation
2. **Stage 5 (Application)**: During `DynamicEntityInsertGenerator`, transform user FK values (orphan ID → UAT ID)
3. **Output**: `DynamicData/**/*.dynamic.sql` contains UAT user IDs (pre-transformed)

**Artifacts Emitted:**
- `Modules/**/*.table.sql`
- `StaticSeeds/StaticSeeds.sql`
- `DynamicData/**/*.dynamic.sql` (pre-transformed user FKs - UAT-ready)
- `uat-users/01_preview.csv`, `02_apply_user_remap.sql` (verification artifacts)
- `Diagnostics/**`

**UAT Migration Workflow:**
1. Run full-export with `--enable-uat-users`
2. Load `DynamicData/**/*.dynamic.sql` directly to UAT database (no post-processing)
3. No UPDATE step needed (user FKs already transformed during INSERT generation)

**Legacy/Verification Mode** (UPDATE scripts):
- Standalone `uat-users` verb still available
- Generates `02_apply_user_remap.sql` (UPDATE statements for post-load transformation)
- Use case: Transform existing UAT database, or verification artifact

---

### § 23. The Pattern

Every use case follows the same structure:

```
[Source] + [Selection] + [Emission] + [Application] + [Transforms] = Pipeline Invocation
```

**No implementation forks:**
- BuildSsdtModulesStep → `EntityPipeline.Execute(schema-only params)`
- BuildSsdtStaticSeedStep → `EntityPipeline.Execute(static data params)`
- BuildSsdtBootstrapSnapshotStep → `EntityPipeline.Execute(bootstrap params)`
- FullExportCoordinator → `EntityPipeline.Execute(combined params)`

**Same invariant flow, different parameters.**

---

## PART VI: INTEGRATION MECHANICS

### § 24. DatabaseSnapshot Architecture

**The Problem:**

Triple-fetch redundancy - OSSYS_* tables queried multiple times with no coordination:

1. **Extract-Model** (SqlModelExtractionService)
   - Queries: OSSYS_ENTITY, OSSYS_ENTITY_ATTR, OSSYS_RELATIONSHIP, OSSYS_INDEX
   - Output: OsmModel (schema, relationships, indexes)

2. **Profile** (SqlDataProfiler)
   - Queries: OSSYS_* metadata + sys.* system tables
   - Output: EntityStatistics (null counts, FK orphans, uniqueness violations)

3. **Export Data** (SqlDynamicEntityDataProvider)
   - Queries: OSUSR_* data tables (`SELECT * FROM [table]`)
   - Output: EntityData (actual row data)

**No caching, no reuse** - each pipeline run re-fetches everything.

---

**The Solution: DatabaseSnapshot**

Unified fetch primitive that retrieves metadata + statistics + data in one coordinated operation:

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

    // Derived projections (lazy, cached)
    public OsmModel ToModel();              // For backward compatibility
    public ProfileSnapshot ToProfile();     // For profiling consumers
    public EntityDataSet ToDataSet();       // For data emission
}
```

**Fetch Options** (partial fetches for performance):

```csharp
var snapshot = await DatabaseFetcher.FetchAsync(
    connectionString: "...",
    selector: EntitySelector.FromModules(...),
    options: new FetchOptions
    {
        IncludeStructure = true,   // OSSYS_* metadata (extract-model)
        IncludeStatistics = true,  // Profiling metrics (sys.*)
        IncludeRowData = true      // OSUSR_* data (export)
    },
    cache: DiskCache(".cache/database-snapshot.json")
);
```

**Caching Strategy:**

1. **Cache to Disk** (`.cache/database-snapshot.json`)
   - Persists across runs (survives process restarts)
   - JSON serialization for human readability (debugging)

2. **Checksum-Based Invalidation**
   - Compute schema checksum (hash of OSSYS_* metadata)
   - Compare current checksum to cached checksum
   - Re-fetch if schema changed, reuse cache if unchanged

3. **Partial Invalidation** (future enhancement)
   - Metadata changed → re-fetch metadata, keep statistics/data if still valid
   - Data changed → re-fetch data, keep metadata/statistics

**Benefits:**
- OSSYS_* tables hit once instead of 2-3 times (eliminate redundancy)
- Coordinated fetch of metadata + statistics + data (single transaction possible)
- Cached to disk (faster iteration when schema unchanged)
- Different pipelines consume different slices of same snapshot (no re-fetch)
- Offline scenarios supported (load from cached snapshot, no database connection)

---

### § 25. Operator Usability as First-Class Citizen

**Diagnostic logging is both orthogonal (happens at every stage) and part of emission strategy (diagnostic artifacts).**

**Real-Time Feedback** (Spectre.Console Integration):

```
╭────────────────────────────────────────╮
│  OutSystems DDL Exporter v2.0          │
╰────────────────────────────────────────╯

[Stage 1/6] Selection
  ✓ Selected 247 entities across 12 modules (15ms)

[Stage 2/6] Fetch
  ✓ Metadata: 247 entities, 1,823 attributes (450ms)
  ✓ Statistics: Profiled 247 tables (2.1s)
  ✓ Data: Exported 1.2M rows (8.3s)

[Stage 3/6] Transform
  ✓ EntitySeedDeterminizer (15ms)
  ✓ TypeMappingPolicy (120ms)
  ⚠ NullabilityEvaluator: 3 warnings (see diagnostics)
  ✓ UAT-Users Discovery: 47 FK columns, 12 orphans (340ms)

[Stage 4/6] Order
  ✓ Topological sort: 247 entities ordered (450ms)
  ⚠ Circular dependency: User ↔ Role (allowed via config)

[Stage 5/6] Emit
  ✓ Schema: 247 tables → Modules/**/*.table.sql (1.2s)
  ✓ StaticSeeds: 89 entities → StaticSeeds.sql (680ms)
  ✓ Bootstrap: 247 entities → Bootstrap.sql (pre-transformed) (3.1s)
  ✓ Diagnostics: Profiling reports, validation logs (95ms)

[Stage 6/6] Apply
  ℹ Insertion strategy: INSERT (NonDestructive mode)
  ℹ Estimated impact: 1.2M rows, ~45 seconds deployment

╭────────────────────────────────────────╮
│  ✓ Pipeline Complete (16.8 seconds)   │
╰────────────────────────────────────────╯

Artifacts:
  • Modules/**/*.table.sql (247 files)
  • StaticSeeds/StaticSeeds.sql (89 entities, 1.8MB)
  • Bootstrap/Bootstrap.sql (247 entities, 47MB, UAT-ready)
  • Diagnostics/profiling-report.csv
  • Diagnostics/validation-results.log
```

**Persistent Diagnostic Artifacts:**

Emitted to `Diagnostics/` directory:

1. **profiling-report.csv**
   - Per-entity statistics: row count, null counts, FK orphans, uniqueness violations
   - Used for data quality analysis, tightening decisions

2. **validation-results.log**
   - Warnings: isMandatory but nullable, FK orphans, circular dependencies
   - Errors: Configuration validation failures

3. **cycles.txt**
   - Topological cycle warnings
   - Allowed cycles (CircularDependencyOptions)
   - Suggested resolutions

4. **transform-execution.log**
   - Per-transform execution time, warnings emitted
   - Observability for debugging transform ordering issues

5. **pipeline-manifest.json**
   - Full pipeline invocation record
   - Input parameters, output artifacts, execution duration
   - CI/CD integration (artifact tracking, automation)

**Dual Nature of Diagnostics:**

**Orthogonal** (happens at every stage):
- Real-time progress bars (Spectre.Console)
- Console warnings/errors (user-facing feedback)
- Structured logging (observability, debugging)

**Part of EmissionStrategy** (diagnostic artifacts):
- Profiling reports (CSV, JSON)
- Validation logs (structured warnings)
- Pipeline manifest (CI/CD integration)

**Both are first-class concerns** - operator usability is not an afterthought.

---

## APPENDIX A: ONTOLOGICAL CLARIFICATIONS

### A.1: What IS an "entity with data"?

An entity where data was extracted from its physical table in the live database.

**NOT:**
- ❌ Configured in model JSON (model JSON contains only metadata)
- ❌ Marked in OSSYS_* tables (no "hasData" flag)
- ❌ Explicitly declared by operator

**YES:**
- ✅ Entity has physical table in database
- ✅ Data extracted via `SELECT * FROM [table]`
- ✅ Row count > 0 after extraction

**Simple rule**: If `SELECT COUNT(*) FROM [table]` returns > 0, entity "has data".

---

### A.2: Where does entity data come from?

**There is only ONE data source: The live database.**

- ✅ ALL data comes from database extraction (OSUSR_* tables)
- ✅ Model JSON contains only metadata (structure, relationships, indexes)
- ❌ NO inline data in model JSON
- ❌ NO supplemental JSON files with row data (ossys-user.json to be deprecated)

---

### A.3: Static Entity vs. Has Data

**These are orthogonal concerns on different axes:**

**Static Entity**:
- Definition: `DataKind = 'staticEntity'` (OutSystems metamodel concept)
- Compile-time property (declared in model)
- Semantics: Enhanced enum, reference data, compile-time optimization

**Has Data**:
- Definition: Entity table contains rows in live database
- Runtime property (discovered during fetch)
- Semantics: Data available for export

**Independence:**
- A static entity might have 0 rows (newly created, not populated)
- A static entity might have 1000 rows (actively used reference data)
- A regular entity might have 0 rows (table created, no usage yet)
- A regular entity might have 1M rows (heavily used transactional data)

Both categories can have data or not. They're independent dimensions.

---

### A.4: Topological Sort Scope

**For data emission pipelines**: Sort is ALWAYS global across ALL selected entities.

**Never:**
- ❌ Scoped to static entities only
- ❌ Scoped to regular entities only
- ❌ Separate sorts per category

**Always:**
- ✅ One unified dependency graph spanning ALL selected entities
- ✅ Respects cross-category FK dependencies (regular → static, static → regular)
- ✅ Global topological order, then filter for emission if needed

**For schema emission (SSDT Modules):**
- No topological sort required
- CREATE TABLE emits in fetch order (DDL order-independent)
- FK constraints are declarative, emission order doesn't matter

---

## APPENDIX B: TERMINOLOGY

### Keep (Existing Terms)

- **Static Entity**: Entity where `DataKind = 'staticEntity'`
- **Regular Entity**: Entity where `DataKind != 'staticEntity'`
- **Module**: Logical grouping of entities (OutSystems concept)
- **Topological Sort**: Dependency-ordered list of entities (graph algorithm)
- **Bootstrap**: Deployment artifact containing INSERT scripts for all entities (first-time load)

### Deprecate (Remove from Vocabulary)

- ~~**Supplemental Entity**~~ → Becomes entity selection via EntitySelector (workaround for incomplete EntityFilters wiring)
- ~~**DynamicData**~~ → Delete entirely, redirect to unified pipeline (redundant with Bootstrap)
- ~~**Normal Entity**~~ → Never use this term (explicitly rejected)

### Introduce (New Abstractions)

- **EntitySelector**: Unified selection primitive (configuration for which entities to include)
- **EmissionStrategy**: Unified emission abstraction (schema/data/diagnostics composition)
- **InsertionStrategy**: Application semantics (INSERT/MERGE/schema-only/none)
- **DatabaseSnapshot**: Cached metadata + statistics + data (eliminates triple-fetch)
- **AllEntities**: Union of all selected entities across categories (static + regular)
- **BusinessLogicTransforms**: Stage 3 transforms with explicit ordering dependencies
- **Protected Citizens**: Mandatory primitives that always execute (DatabaseSnapshot, EntityDependencySorter)

---

**End of Document**

*This ontological decomposition provides the architectural foundation for systematic implementation. Each completion vector (§13-§21) can now be executed independently with clear scope, dependencies, and success criteria.*
