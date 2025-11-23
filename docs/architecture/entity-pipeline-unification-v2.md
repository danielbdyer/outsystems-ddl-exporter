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

This section provides rich detail on each stage: what happens, what's required, what's optional, and how different use cases participate.

---

### § 4. Stage 0: Extract-Model (OPTIONAL - Can be Manual or Automatic)

**Purpose**: Retrieve database metadata (schema, relationships, indexes) from live database.

**What Happens:**
- Query OSSYS_* system tables (OSSYS_ENTITY, OSSYS_ENTITY_ATTR, OSSYS_RELATIONSHIP, OSSYS_INDEX)
- Parse OutSystems metamodel into OsmModel structure
- Cache to disk (`.cache/model.json` or user-specified path)
- Checksum-based invalidation (re-extract when schema changes)

**Protected Citizens:**
- SqlModelExtractionService (queries OSSYS_* tables)
- OsmModel (metamodel structure)

**Optional Elements:**
- Caching (can skip cache, always extract fresh)
- Offline mode (user provides pre-extracted model via `--model` flag)

**Configuration Inputs:**
- `--connection-string` (live extraction) OR `--model` (offline mode)
- `--output` (cache location)

**Output State:**
- `ExtractionCompleted` (contains OsmModel + extraction metadata)

**Use Case Participation:**
- **extract-model verb**: This IS the primary operation (extract + emit model JSON)
- **build-ssdt**: Can auto-extract (Stage 0) or accept pre-extracted model (`--model` override)
- **full-export**: Can auto-extract (Stage 0) or accept pre-extracted model
- **Offline scenarios**: Skip Stage 0, provide pre-extracted model

**Current Status:**
- Currently manual (user runs `extract-model` then passes `--model` to other verbs)
- Vector 5 (§17): Integrate as automatic Stage 0 with caching

**Partial Participation Example:**
```
extract-model:  Stage 0 (FULL) → Emit → Done
build-ssdt:     Stage 0 (auto) → Selection → Fetch → ... (or skip Stage 0 if --model provided)
```

---

### § 5. Stage 1: Selection

**Purpose**: Determine which entities participate in this pipeline invocation.

**What Happens:**
- Evaluate selection criteria (all modules, static-only, filtered per-module)
- Apply EntitySelector predicate to full entity catalog
- Produce set of selected entities (may be empty)
- Selection always happens (even if "select all" is implicit)

**Protected Citizens:**
- EntitySelector (currently fragmented as ModuleFilterOptions)
- Selection must produce a set (even if empty set is valid)

**Optional Elements:**
- Filtering per-module (EntityFilters) - currently not wired
- Predicate-based selection (e.g., `Where(e => e.IsStatic)`)

**Configuration Inputs:**
- `ModuleFilterOptions` (which modules to include/exclude)
- `EntityFilters` (per-module entity lists) - currently incomplete
- Implicit selection (e.g., Bootstrap selects "all with data", StaticSeeds selects "static only")

**Output State:**
- `SelectionCompleted` (contains selected entity set + selection criteria)

**Use Case Participation:**
- **extract-model**: Select ALL modules (need full schema)
- **build-ssdt schema**: Select ALL modules (emit schema for all entities)
- **build-ssdt data** (StaticSeeds): Select WHERE(IsStatic) (only static entities)
- **Bootstrap**: Select AllWithData (static + regular entities that have data)
- **full-export**: Select ALL modules (schema + data for everything)

**Current Status:**
- Selection criteria hardcoded in each implementation
- EntityFilters exists but not wired (Vector 1, §13)

**Participation Matrix:**

| Use Case | Selection Criteria |
|----------|-------------------|
| extract-model | AllModules |
| build-ssdt schema | AllModules |
| build-ssdt data (StaticSeeds) | Where(IsStatic) |
| Bootstrap | AllWithData |
| full-export | AllModules |

---

### § 6. Stage 2: Fetch

**Purpose**: Retrieve metadata, statistics, and/or data for selected entities from database.

**What Happens:**
- Query OSSYS_* tables for metadata (if not already extracted in Stage 0)
- Query sys.* tables for statistics (profiling: null counts, FK orphans)
- Query OSUSR_* tables for data (`SELECT * FROM [table]`)
- Construct DatabaseSnapshot (unified fetch result)
- Cache to disk (optional, for performance)

**Protected Citizens:**
- DatabaseSnapshot (unified metadata + statistics + data)
- Fetching must happen (even if cached/partial)

**Optional Elements:**
- Partial fetch (IncludeStructure, IncludeStatistics, IncludeRowData flags)
- Caching (DiskCache for DatabaseSnapshot)
- Profiling (can skip statistics if not needed)

**Configuration Inputs:**
- `--connection-string` (database to query)
- `FetchOptions` (what to fetch: structure/statistics/data)
- `--cache` (cache location)

**Output State:**
- `FetchCompleted` (contains DatabaseSnapshot + fetch metrics)

**Use Case Participation:**
- **extract-model**: Fetch ONLY metadata (OSSYS_*), no statistics, no data
- **build-ssdt schema**: Fetch metadata + statistics (for profiling), no data
- **build-ssdt data** (StaticSeeds): Fetch metadata + statistics + data (for static entities)
- **Bootstrap**: Fetch metadata + statistics + data (for all entities with data)
- **full-export**: Fetch metadata + statistics + data (complete fetch)

**Current Status:**
- Triple-fetch redundancy (OSSYS_* queried 2-3 times)
- No DatabaseSnapshot abstraction (Vector 7, §24)

**Partial Fetch Matrix:**

| Use Case | Metadata (OSSYS_*) | Statistics (sys.*) | Data (OSUSR_*) |
|----------|-------------------|-------------------|----------------|
| extract-model | ✓ | ✗ | ✗ |
| build-ssdt schema | ✓ | ✓ | ✗ |
| build-ssdt data | ✓ | ✓ | ✓ (static only) |
| Bootstrap | ✓ | ✓ | ✓ (all with data) |
| full-export | ✓ | ✓ | ✓ (all entities) |

---

### § 7. Stage 3: Transform

**Purpose**: Apply business logic transformations to fetched data/metadata before emission.

**What Happens:**
- Run configured transforms in dependency order
- Each transform: `Input State → Transformed State`
- Transforms can modify data (UAT-Users), metadata (TypeMappingPolicy), or both
- No universally protected citizens (all transforms optional or conditional)

**Protected Citizens:**
- **NONE** - All transforms are optional or configuration-driven
- Transform composition engine (ensures ordering constraints respected)

**Optional Elements:**
- EntitySeedDeterminizer (normalize data)
- TypeMappingPolicy (data type transformations)
- NullabilityEvaluator (tightening recommendations)
- UAT-Users Discovery (FK catalog + orphan detection)
- Module name collision handling
- Supplemental physical→logical remapping

**Configuration Inputs:**
- `config.Transforms` (which transforms enabled)
- Transform-specific config (TighteningOptions, CircularDependencyOptions, UAT-Users inventories)

**Output State:**
- `TransformCompleted` (contains transformed metadata + data + transform execution log)

**Use Case Participation:**
- **extract-model**: Minimal transforms (just metadata normalization)
- **build-ssdt schema**: Type mapping, nullability evaluation (affects DDL generation)
- **build-ssdt data** (StaticSeeds): Full transform set (determinization, FK preflight, etc.)
- **Bootstrap**: Full transform set + TopologicalOrderingValidator
- **full-export with UAT-Users**: Full transform set + UAT-Users Discovery

**Current Status:**
- Transforms scattered across implementations (not consolidated)
- Ordering dependencies unclear (Vector 6, §18 - comprehensive excavation required)

**Transform Participation Matrix:**

| Transform | extract-model | build-ssdt schema | build-ssdt data | Bootstrap | full-export+UAT |
|-----------|--------------|------------------|----------------|-----------|----------------|
| EntitySeedDeterminizer | ✗ | ✗ | ✓ | ✓ | ✓ |
| TypeMappingPolicy | ✗ | ✓ | ✓ | ✓ | ✓ |
| NullabilityEvaluator | ✗ | ✓ | ✓ | ✓ | ✓ |
| UAT-Users Discovery | ✗ | ✗ | ✗ | ✗ | ✓ |
| Module name collision | ✗ | ✓ | ✓ | ✓ | ✓ |

---

### § 8. Stage 4: Order

**Purpose**: Topologically sort entities by foreign key dependencies to ensure correct emission order.

**What Happens:**
- Build dependency graph from FK relationships
- Topological sort (Kahn's algorithm or DFS-based)
- Detect circular dependencies (validate against CircularDependencyOptions)
- Produce globally ordered entity list

**Protected Citizens:**
- **EntityDependencySorter** (for data emission pipelines - MANDATORY)
- Topological sort is NON-NEGOTIABLE when emitting data (FK dependencies are absolute)

**Critical Nuance:**
- **Data emission**: Ordering MANDATORY (INSERT/MERGE must be FK-ordered)
- **Schema emission**: Ordering SKIPPED (CREATE TABLE order-independent, emit in fetch order)

**Optional Elements:**
- TopologicalOrderingValidator (enhanced cycle diagnostics - currently Bootstrap only)
- CircularDependencyOptions (allowed cycles configuration)
- StaticSeedForeignKeyPreflight (orphan detection)

**Configuration Inputs:**
- `CircularDependencyOptions` (explicitly allowed cycles)
- FK metadata (from DatabaseSnapshot)

**Output State:**
- `OrderCompleted` (contains topologically sorted entity list + cycle warnings)

**Use Case Participation:**
- **extract-model**: SKIP (no ordering needed for metadata export)
- **build-ssdt schema**: SKIP (DDL order-independent, emit in fetch order)
- **build-ssdt data** (StaticSeeds): PARTICIPATE (topological sort MANDATORY for data)
- **Bootstrap**: PARTICIPATE (global topological sort across all entities)
- **full-export**: PARTICIPATE (sort once, use for both StaticSeeds and Bootstrap data)

**Current Status:**
- StaticSeeds sorts only static entities (incorrect - misses cross-category FKs)
- Bootstrap sorts globally (correct - Vector 3, §15)
- TopologicalOrderingValidator only in Bootstrap (needs extraction)

**Participation Matrix:**

| Use Case | Topological Sort? | Scope |
|----------|------------------|-------|
| extract-model | ✗ SKIP | N/A |
| build-ssdt schema | ✗ SKIP | N/A (emit in fetch order) |
| build-ssdt data (StaticSeeds) | ✓ PARTICIPATE | Static entities only (WRONG - needs global) |
| Bootstrap | ✓ PARTICIPATE | ALL entities (CORRECT - global sort) |
| full-export | ✓ PARTICIPATE | ALL entities (global sort) |

---

### § 9. Stage 5: Emit

**Purpose**: Generate SQL artifacts (schema DDL, data DML, diagnostics) from ordered/transformed entities.

**What Happens:**
- Generate CREATE TABLE statements (schema emission)
- Generate INSERT/MERGE statements (data emission, using topological order)
- Generate profiling reports, validation logs (diagnostic emission)
- Organize files (monolithic, per-module, per-entity)
- Generate .sqlproj (if per-module organization)

**Protected Citizens:**
- **Some artifact type must be emitted** (schema, data, or diagnostics)
- Cannot skip emission entirely (must produce output)

**Optional Elements:**
- Schema emission (CreateTableStatementBuilder)
- Data emission (INSERT/MERGE generators)
- Diagnostic emission (profiling reports, validation logs)
- File organization strategy (monolithic vs per-module vs per-file)

**Configuration Inputs:**
- `EmissionStrategy` (what to emit, how to organize)
- `SmoFormatOptions` (formatting preferences)
- Output paths (--build-out, --profile-out)

**Output State:**
- `EmissionCompleted` (contains artifact paths + emission metrics)

**Use Case Participation:**
- **extract-model**: Emit model JSON + diagnostics (no SQL)
- **build-ssdt schema**: Emit CREATE TABLE (per-module) + diagnostics
- **build-ssdt data** (StaticSeeds): Emit MERGE statements + diagnostics
- **Bootstrap**: Emit INSERT statements + diagnostics
- **full-export**: Emit schema + StaticSeeds + Bootstrap + diagnostics (compositional)

**Current Status:**
- Schema and data treated as separate pipelines (no unified abstraction)
- Diagnostic emission ad-hoc (not integrated into EmissionStrategy)
- Vector 2 (§14): Extract unified EmissionStrategy
- Vector 7 (§19): Unify schema and data emission

**Emission Matrix:**

| Use Case | Schema (DDL) | Data (DML) | Diagnostics |
|----------|-------------|-----------|-------------|
| extract-model | ✗ | ✗ | model.json |
| build-ssdt schema | ✓ (per-module) | ✗ | profiling |
| build-ssdt data | ✗ | ✓ MERGE (StaticSeeds) | profiling |
| Bootstrap | ✗ | ✓ INSERT (all entities) | cycles, profiling |
| full-export | ✓ (per-module) | ✓ MERGE + INSERT | comprehensive |

**Compositional Emission Examples:**

```
extract-model:  Emit(model + diagnostics)
build-ssdt:     Emit(schema + StaticSeeds + diagnostics)
full-export:    Emit(schema + [StaticSeeds, Bootstrap] + diagnostics)
```

---

### § 10. Stage 6: Apply

**Purpose**: Define how generated SQL modifies the target database (or if it's export-only).

**What Happens:**
- Specify insertion semantics (INSERT, MERGE, TRUNCATE+INSERT)
- Specify batch size (performance tuning)
- Specify mode flags (NonDestructive, idempotent, etc.)
- OR specify "none" (export-only, no target database modification)

**Protected Citizens:**
- **InsertionStrategy must be specified** (even if "none" for export-only)

**Optional Elements:**
- Actual deployment (can generate scripts without deploying)
- Batch size tuning (default: 1000 rows)
- Mode flags (NonDestructive, idempotent)

**Configuration Inputs:**
- `InsertionStrategy` (INSERT/MERGE/TRUNCATE+INSERT/schema-only/none)
- Batch size (--batch-size)
- Mode flags (--non-destructive)

**Output State:**
- `ApplicationCompleted` (contains deployment result or export confirmation)

**Use Case Participation:**
- **extract-model**: Apply = None (export model JSON, no database modification)
- **build-ssdt schema**: Apply = SchemaOnly (deploy DDL, no data)
- **build-ssdt data** (StaticSeeds): Apply = MERGE (upsert on PK)
- **Bootstrap**: Apply = INSERT NonDestructive (one-time load, fail on duplicates)
- **full-export**: Apply = Combined (schema-only + MERGE + INSERT) OR None (export-only)

**Current Status:**
- Insertion strategy hardcoded in separate implementations
- Vector 4 (§16): Parameterize InsertionStrategy

**Application Matrix:**

| Use Case | InsertionStrategy |
|----------|------------------|
| extract-model | None (export-only) |
| build-ssdt schema | SchemaOnly |
| build-ssdt data (StaticSeeds) | MERGE (on PK, batch 1000) |
| Bootstrap | INSERT (NonDestructive, batch 1000) |
| full-export | Combined OR None (export-only) |

---

### § 11. Cross-Cutting: Diagnostic Logging

**Diagnostic logging is BOTH orthogonal (happens at every stage) AND part of emission strategy (diagnostic artifacts).**

**Real-Time Diagnostics** (orthogonal - every stage):
- Spectre.Console progress bars (Stage N/6 with real-time feedback)
- Console warnings/errors (operator-facing, immediate feedback)
- Execution duration per stage (performance observability)

**Persistent Diagnostics** (part of emission - Stage 5):
- `profiling-report.csv` (data quality metrics)
- `validation-results.log` (warnings, errors)
- `cycles.txt` (topological diagnostics)
- `transform-execution.log` (transform durations, warnings)
- `pipeline-manifest.json` (full invocation record for CI/CD)

**Per-Stage Diagnostic Output:**

| Stage | Real-Time Output | Persistent Artifacts |
|-------|-----------------|---------------------|
| Stage 0: Extract | "Extracting metadata..." progress | model.json, extraction.log |
| Stage 1: Select | "Selected 247 entities" | selection-criteria.json |
| Stage 2: Fetch | "Fetched 1.2M rows in 8.3s" | fetch-metrics.json |
| Stage 3: Transform | "TypeMappingPolicy: 120ms" | transform-execution.log |
| Stage 4: Order | "Circular dependency: User ↔ Role" | cycles.txt |
| Stage 5: Emit | "Emitted 247 tables" | profiling-report.csv, validation-results.log |
| Stage 6: Apply | "Estimated impact: 1.2M rows" | deployment-summary.json |

**Dual Nature:**
- **Orthogonal**: Happens at every stage (real-time feedback, observability)
- **Emission**: Diagnostic artifacts generated in Stage 5 (persistent, CI/CD integration)

Both are first-class concerns - operator usability is not an afterthought.

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
