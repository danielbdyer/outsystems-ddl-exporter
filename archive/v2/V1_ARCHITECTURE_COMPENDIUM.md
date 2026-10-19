# V1 Architecture Compendium

Synthesis of "what V1 actually does" — distilled from the chapter 5 parity-audit wave (2026-05-17 → 2026-05-18; 23 slices; 185 matrix rows; 6 deep-audit agent transcripts). This document consolidates V1's internal architecture into a navigable reference so future V2 agents can understand V1 without re-reading every audit transcript.

V1 is the OutSystems DDL Exporter trunk in `/home/user/outsystems-ddl-exporter/src/` — **78461 LOC of C# across 11 projects + 2 SQL files (~2100 LOC)**. V2 is the F# sidecar in `/home/user/outsystems-ddl-exporter/sidecar/projection/`. V2 is editorial donor only (per `DECISIONS 2026-05-16 (later) — V2 self-containment`); the two never link at runtime.

**Reading order:** Section 1 names the project taxonomy. Sections 2–8 walk the architecture by audit cluster. Section 9 names the cross-cutting structural patterns V1 carries that V2 either preserves, refines, or sunsets. Section 10 names the V1 surfaces V2 does NOT inherit.

---

## 1. Project taxonomy

V1's 11 projects partition into six conceptual sections:

| Section | Projects | LOC | Role |
|---|---|---|---|
| **A. Ingest** | `Osm.Pipeline.SqlExtraction` (55 files), `Osm.Json` (47), `Osm.Domain` (63), `AdvancedSql` (2 SQL) | ~17.5K + 2.1K SQL | OSSYS source → in-memory model |
| **B. Analyze** | `Osm.Validation` (65), `Osm.Pipeline.Profiling` (28), `Osm.Pipeline.Evidence` (15), `Osm.Pipeline.Application` (21), `Osm.Pipeline.Mediation` (2) | ~12K | Validate / tighten / profile / orchestrate decisions |
| **C. Emit** | `Osm.Smo` (44), `Osm.Emission` (20), `Osm.Pipeline.StaticData` (1), `Osm.Pipeline.DynamicData` (7), `Osm.Pipeline.UatUsers` (23) | ~13K | Produce SSDT artifacts + data scripts |
| **D. Orchestrate** | `Osm.Pipeline.Orchestration` (53), `Osm.Pipeline.Configuration` (8), `Osm.Pipeline.Runtime` (8), `Osm.Pipeline.Sql` (7), `Osm.Pipeline.ModelIngestion` (6) | ~9K | Pipeline wiring |
| **E. Operate** | `Osm.Cli` (40), `Osm.LoadHarness` (6) | ~10K | Operator-facing surface + load testing |
| **F. Compare** | `Osm.Dmm` (8) | ~2.2K | Schema-diff machinery |

The architecture roughly follows a six-stage flow: **Ingest (A)** → **Analyze (B)** → **Emit (C)** → **Orchestrate (D, threads everything)** → **Operate (E, exposes via CLI)**. Section F (DMM diff) is an ad-hoc verification tool; structurally orthogonal.

---

## 2. Ingest — OSSYS source → V1 in-memory model

### 2.1 SqlExtraction (`Osm.Pipeline.SqlExtraction`; 55 files)

V1's catalog acquisition surface. Reads OSSYS metadata from a SQL Server hosting the OutSystems platform.

**Entry point.** `IOutsystemsMetadataReader` defines `ReadAsync(request, ct) → Task<Result<OutsystemsMetadataSnapshot>>`. Two implementations: `SqlClientOutsystemsMetadataReader` (live SQL Server) and `FixtureOutsystemsMetadataReader` (manifest-keyed JSON fixtures for offline testing).

**The metadata snapshot.** `OutsystemsMetadataSnapshot` (in `IOutsystemsMetadataReader.cs`) carries **23 rowset DTOs**:
- **Structured rowsets (consumed by V2's OssysSql adapter):** Modules, Entities, Attributes, References, PhysicalTables
- **Physical-reflection rowsets (V2 NOT-MAPPED):** ColumnReality, ColumnChecks, PhysicalColumnsPresent, Indexes, IndexColumns, ForeignKeys, ForeignKeyColumns, AttributeForeignKeys, ForeignKeyAttributeMap, Triggers
- **JSON-aggregation rowsets (V2-SUNSET; produce `osm_model.json`):** ColumnCheckJson, ForeignKeyColumnsJson, ForeignKeyAttributeJson, AttributeJson, RelationshipJson, IndexJson, TriggerJson, ModuleJson
- **Envelope:** DatabaseName (from `connection.Database`)

**The SQL contract.** All rowsets come from one SQL script: `src/AdvancedSql/outsystems_metadata_rowsets.sql` (1184 LOC). V2 carbon-copies this verbatim per `DECISIONS 2026-05-16 (later)`.

**Production wiring.** `SqlClientOutsystemsMetadataReader` (~99 LOC) orchestrates via `IDbConnectionFactory.CreateOpenConnectionAsync()` + `IDbCommandExecutor.ExecuteReaderAsync()`. `MetadataSnapshotRunner` (~408 LOC) walks the 22 result sets via processor pattern, catching 3 distinct exception classes:
- `MetadataRowMappingException` — row-parse failure with friendly-context reconstruction
- `MetadataResultSetMissingException` — contract breach
- `DbException` — catch-all

Reads `SqlExecutionOptions.CommandTimeoutSeconds` (caller-tunable; ADO default 30s). **No Polly retry**; transient handling delegated to caller. Integrates `ITaskProgressAccessor` for per-processor progress ticks.

**Operator-debugging telemetry.** Three files compose V1's diagnostic dump surface:
- `Pipeline.Sql.SqlMetadataLog.cs` (~86 LOC) — in-memory observation accumulator (snapshot-on-success + errors-on-failure + per-request payloads)
- `Pipeline.SqlExtraction.MetadataRowSnapshot.cs` (~179 LOC) — last-row context on failure
- `Pipeline.SqlExtraction.SqlMetadataDiagnosticsWriter.cs` (~156 LOC) — JSON-dump emitter writing log to operator path

**Contract versioning.** `Pipeline.SqlExtraction.MetadataContractOverrides.cs` (~142 LOC) maintains `Dictionary<string, HashSet<string>>` of optional columns per rowset; loaded from `appsettings.json` (`metadataContract.optionalColumns`). Processors consult `IsColumnOptional` to tolerate NULL/missing columns across OutSystems versions.

**Snapshot validation.** `Pipeline.SqlExtraction.SnapshotValidator.cs` (~133 LOC) validates JSON shape pre-deserialization (per-module/-entity array presence + non-null checks); fail-fast.

### 2.2 JSON deserialization (`Osm.Json`; 47 files)

V1's `osm_model.json` parsing surface. The exported model travels JSON between extraction and emission.

**Entry point.** `ModelJsonDeserializer` — sealed multi-partial class (`Deserialize(Stream, options) → Result<OsmModel>`) with lazy-initialized `SharedPipeline`. Five mapper classes: `EntityDocumentMapper`, `RelationshipDocumentMapper`, `SequenceDocumentMapper`, `TriggerDocumentMapper` + extended-property mapper.

**Deduplication.** `IAttributeDeduplicator` interface + `AttributeDeduplicator` concrete (handles V1 JSON-projection artifact where multiple attribute rows for same logical/physical name appear when reference targets have multiple active/inactive versions). Uses `ReferenceEntityIsActive` to break ties. Emits warnings into collector via `IDuplicateWarningEmitter` when `AllowDuplicateAttributeLogicalNames` / `AllowDuplicateAttributeColumnNames` flags set.

**CIR schema validation.** `CirSchemaValidator` (static class) loads embedded `cir-v1.json` JSON Schema resource; validates root element pre-deserialization; fail-fast with error collection.

**Boolean coercion.** `BooleanAsZeroOneConverter` (custom `JsonConverter<bool>`) accepts 0/1 numbers, JSON booleans, or strings; registered on `ModelDocumentSerializerContext`.

**Profile snapshot I/O.** `ProfileSnapshotSerializer` + `ProfileSnapshotDeserializer` build isolated `ProfileSnapshot` domain object from JSON; extensive record types for JSON DTOs.

**Error metadata.** `DocumentPathContext` DU tracks JSON path during traversal; appended to errors via `ValidationError.WithMetadata("json.path", ...)`.

### 2.3 Domain aggregate-root model (`Osm.Domain/Model`; 63 files)

V1's in-memory domain model. **V2 deliberately did NOT inherit this** — V2 reconstructs `Catalog` from rowsets fresh.

**Module aggregate.** `ModuleModel.cs` enforces per-module non-empty Entity invariant + logical-name + case-insensitive physical-name uniqueness; `OsmModel.cs` is the catalog root; `OutSystemsInternalModel.cs` is platform-internal seeding.

**Entity aggregate.** `EntityModel.cs` + `EntityMetadata.cs` carry dual identity (`EntityId : int` local + `EntitySsKey : Guid?` optional), two binary flags (`IsSystemEntity` + `IsExternalEntity` — 4 implicit states), per-entity `Catalog : string?` field (database name for SMO qualified-name rendering), attribute/index/relationship/trigger collections, physical realization (`PhysicalName` + `Schema` + `Catalog`).

**Attribute aggregate (3-layer separation across 7 files).**
- **Logical (`AttributeModel.cs` + `AttributeMetadata.cs`):** LogicalName, ColumnName, DataType (string; free-form), defaults, IsMandatory, IsIdentifier, IsAutoNumber, Description, ExtendedProperties
- **Physical reality (`AttributeReality.cs`):** IsNullableInDatabase, HasNulls, HasDuplicates, HasOrphans, IsPresentButInactive (reflection statistics)
- **On-disk evidence (`AttributeOnDiskMetadata.cs` + `AttributeOnDiskCheckConstraint.cs` + `AttributeOnDiskDefaultConstraint.cs`):** SqlType, MaxLength, Precision, Collation, IsIdentity, IsComputed, ComputedDefinition, CHECK constraint arrays (per-attribute), DEFAULT-constraint envelope (Name + Definition + IsNotTrusted)
- **References:** `AttributeReference.cs` is an attribute-embedded optional 6-field record (IsReference, TargetEntityId, TargetEntity, TargetPhysicalName, DeleteRuleCode, HasDatabaseConstraint)

**Index aggregate (8 files).** `IndexModel.cs` + `IndexColumnModel.cs` + `IndexColumnDirection.cs` + `IndexKind.cs` (6-variant enum: PrimaryKey / UniqueConstraint / UniqueIndex / NonUniqueIndex / ClusteredIndex / NonClusteredIndex) + `IndexOnDiskMetadata.cs` (IsDisabled, IgnoreDuplicateKey, FillFactor, IsPadded, AllowRowLocks, AllowPageLocks, NoRecompute) + `IndexDataSpace.cs` + `IndexPartitionColumn.cs` + `IndexPartitionCompression.cs`.

**Relationship/FK 3-type split.** `RelationshipModel.cs` (logical edge) + `ForeignKeyModel.cs` (physical constraint with delete+update actions) + `RelationshipActualConstraint.cs` (reconciliation with per-column mapping + per-action NOCHECK state via empty action strings).

**Misc aggregates.** `SequenceModel.cs` (Schema, Name, DataType, StartValue, Increment, Minimum, Maximum, IsCycleEnabled, `SequenceCacheMode` 4-variant enum, CacheSize, ExtendedProperties), `TriggerModel.cs` (Name, IsDisabled, Definition), `ExtendedProperty.cs` (Name, Value), `TemporalRetentionPolicy.cs` (4-variant Kind enum + Value + Unit enum), `EntityMetadata.Temporal : TemporalTableMetadata`.

**Value objects (`Osm.Domain/ValueObjects`; 11 files).** Eleven naming VOs (`EntityName`, `ModuleName`, `TableName`, `ColumnName`, `AttributeName`, `SchemaName`, `IndexName`, `ForeignKeyName`, `SequenceName`, `TriggerName`) + `StringValidators.cs` shared validation. Each VO is a record struct with `Create: Result<Name>` validating via `StringValidators.RequiredIdentifier` (non-null + non-empty + trimmed).

### 2.4 AdvancedSql (`AdvancedSql`; 2 files)

`outsystems_metadata_rowsets.sql` (1184 LOC) and `outsystems_model_export.sql` (931 LOC). The rowsets script is the metadata reader's SQL (carbon-copied verbatim into V2); the export script is V1's JSON emitter (V2-SUNSET — V2 produces SSDT directly, never `osm_model.json`).

---

## 3. Analyze — Validation, Tightening, Profiling, Evidence

### 3.1 Validation & Tightening (`Osm.Validation`; 65 files)

V1's decision-making heart. Distributes across three sub-clusters:

**Signals cluster (`Tightening/Signals/`; 15 files / 680 LOC).** V1's combinator-style decision algebra. Abstract base class `NullabilitySignal` + recursive composition via `AllOfSignal` + `AnyOfSignal` + 2-valued `SignalEvaluation.Result : bool` + flat-string `Rationales : ImmutableArray<string>` accumulated via `CollectRationales()`. Root signal is an `AnyOfSignal` tree assembled by `NullabilitySignalFactory` from a per-mode `NullabilityModeDefinition`.

Eight atomic signals: `PrimaryKeySignal` (tighten if IsIdentifier; always-fire), `PhysicalNotNullSignal` (tighten if !IsNullable in physical schema), `MandatorySignal` (tighten if IsMandatory + profile evidence within budget; defers via Opportunity if beyond budget), `NullEvidenceSignal` (evidence-gating helper for Mandatory), `DefaultSignal` (telemetry-only — never causes tightening, structurally misleading), `RequiresEvidenceSignal` (higher-order combinator wrapping inner + evidence pair), `UniqueCleanSignal` (cross-axis; participates mode-dependently in nullability AnyOf), `ForeignKeySupportSignal` (cross-axis; same).

`NullabilityPolicyDefinition` carries policy DSL; `NullabilitySignalContext` is the context object signals consume; `SignalEvaluation.cs` (~94 LOC) is the evaluation engine.

**Tightening root (`Tightening/`; 40 files / 4478 LOC).** The decision-engine machinery underpinning the signals.

Decision engines (the big ones):
- `NullabilityEvaluator.cs` (~315 LOC) — consumes signal tree + per-attribute context + override list; produces `NullabilityDecision` records with `MakeNotNull : bool` + `RequiresRemediation : bool` + sorted-set rationale strings + opportunity-deferred metadata
- `ForeignKeyEvaluator.cs` (~243 LOC) — produces per-reference decisions with `(CreateConstraint : bool, ScriptWithNoCheck : bool)` 2-tuple shape; **known V1 bug:** `OpportunityBuilder.Add` silently skips some failure paths (missing-target FK references; pre-session-8 refinement); V2 corrects via exhaustive named keep-reasons
- `UniqueIndexDecisionStrategy.cs` (~316 LOC) — per-index decision logic
- `UniqueIndexDecisionOrchestrator.cs` (~74 LOC) — walks indexes + dispatches to strategy
- `UniqueIndexEvidenceAggregator.cs` (~254 LOC) — pre-computes 4 evidence sets (SingleColumnClean / SingleColumnDuplicates / CompositeClean / CompositeDuplicates) by walking the model once

Decision DTOs + supporting types: `NullabilityDecision.cs`, `ForeignKeyDecision.cs` (~24 LOC), `UniqueIndexDecision.cs` (~20 LOC), `TighteningDecisions.cs` (~15 LOC; root aggregate), `ColumnAnalysis.cs` + `ColumnAnalysisBuilder.cs` (per-column aggregate combining nullability + FK + unique-index + ChangeRisk + opportunity list — the canonical V1 emitter-facing surface), `ForeignKeyTargetIndex.cs` (~55 LOC; stateless FK target lookup helper), `ChangeRiskClassifier.cs` (~140 LOC; emits `RiskLevel` Low/Moderate/High for every decision via three classifier methods).

**Opportunities & reporting (`Tightening/`; ~12 files / ~1400 LOC).** Per-decision opportunity carriers + reporting:
- `Opportunity.cs` (~196 LOC) — Type + Title + Summary + Risk + Disposition + Category + Evidence + Rationales + EvidenceSummary + Columns
- `OpportunityBuilder.cs` (~62 LOC) — imperative accumulator (`TryCreate` per decision)
- `OpportunityMetrics.cs` (~47 LOC)
- `OpportunitiesReport.cs` + `ReportSummary.cs` — top-level columnar aggregates
- `PolicyDecisionReporter.cs` (~326 LOC) — the choreographer; walks decision dictionaries, builds per-axis reports, aggregates per-module rollups
- `PolicyDecisionSummaryFormatter.cs` (~439 LOC) — V1's biggest formatter; produces operator-facing per-bucket summary tables (6 buckets: Mandatory / ForeignKey / PrimaryKey / Unique / Physical / Remediation); mode-aware prose narration
- `PolicyDecisionSet.cs` (~44 LOC) — root decision-set aggregate
- `PolicyAnalysisResult.cs` (~21 LOC) — top-level result type
- `TighteningDiagnostic.cs` (~83 LOC) — tightening-specific diagnostic record
- `RemediationQueryBuilder.cs` (~73 LOC) — emits remediation SQL (3-option UPDATE/DELETE/SELECT) operators run to fix data before tightening
- `TighteningRationales.cs` (~31 LOC) — 30 `public const string` rationale labels for `HasRationale`-based bucket classification

**Validations subdirectory (small).** `ValidationFinding.cs` (record carrying OpportunityType + Title + Summary + Evidence + Rationales + Column? + Index? + Schema + Table + ConstraintName + Columns); `ValidationReport.cs` (bundles findings + TypeCounts map + GeneratedAtUtc).

### 3.2 Profiling (`Osm.Pipeline.Profiling`; 28 files / 6033 LOC)

V1's profile-acquisition surface. Big and structurally important — Profile is OperatorIntent evidence per A34.

**Orchestrator.** `SqlDataProfiler.cs` — `CaptureAsync()` is the live-probe orchestration: collects tables, loads metadata, builds plans, executes queries in parallel; concrete implementation of `IDataProfiler` for single-environment live SQL Server.

**Query builders (the probe surface).**
- `NullCountQueryBuilder.cs` — emits `SELECT SUM(CASE WHEN [col] IS NULL ...)` over sampled rows via `TOP (@SampleSize)` or full scan
- `UniqueCandidateQueryBuilder.cs` — emits per-candidate uniqueness check via `SELECT CandidateId, CASE WHEN EXISTS (GROUP BY ... HAVING COUNT(*) > 1) ...`; composite + single-column variants
- `ForeignKeyProbeQueryBuilder.cs` (~154 LOC) — two methods: `BuildRealityCommandText()` (per-FK orphan count via LEFT JOIN); `BuildMetadataCommandText()` (sys.foreign_keys TRUSTED / NO CHECK flags)
- `ForeignKeyOrphanSampleQueryBuilder.cs` — `SELECT TOP (@SampleLimit)` orphan rows with PK identifiers + orphan value + TotalOrphans count

**Sampling.** `TableSamplingPolicy.cs` (~61 LOC) — per-table heuristics: `ShouldSample()` (row count > threshold) + `GetSampleSize()` (min of sample-size config + row count + max-rows-per-table). Config in `SqlProfilerOptions.Sampling`.

**Plans.** `ProfilingPlans.cs` + `ProfilingPlanBuilder.cs` — explicit per-table plans carrying probe declarations; pre-probe orchestration with physical-coordinate resolution + metadata validation.

**Multi-environment.** `MultiTargetSqlDataProfiler.cs` — orchestrates parallel captures across dev/uat/prod; merges via worst-case aggregation (`MergeSnapshots`); consensus thresholding. `ProfilingEnvironmentSnapshot` per-environment result.

**Normalization + validation.** `ProfilingSnapshotNormalizer.cs` + `ProfilingStandardizationValidator.cs` — runtime invariant guards (row counts ≥ null counts; null percentages bounded; composite keys have >0 columns).

**Fixture.** `FixtureDataProfiler.cs` — offline-test fixture implementation; deserializes JSON `ProfileSnapshot` into in-memory value.

**Interface surface.** `IDataProfiler.cs` + `IDataProfilerFactory.cs` + `DataProfilerFactory.cs` + `IMultiEnvironmentProfiler.cs` + `ProfilingContracts.cs` + `MultiEnvironmentConstraintConsensus.cs` + `MultiEnvironmentProfileReport.cs` + `ProfilingProbePolicy.cs` + `ProfilingProbeResult.cs` + `ProfilingComparers.cs` + `ProfilingQueryExecutor.cs` + `EntityProfilingLookup.cs` + `ForeignKeyMappingResolver.cs` + `ColumnMetadata.cs` + `TableMetadataLoader.cs`.

### 3.3 Pipeline Evidence + Application + Mediation (`Osm.Pipeline.{Evidence,Application,Mediation}`; ~50 files)

V1's caching subsystem + application services + command dispatch.

**Evidence (`Pipeline.Evidence/`; 15 files).** Coherent caching subsystem:
- `EvidenceArtifactType.cs` — enum (Model / Profile / Dmm / Configuration)
- `EvidenceArtifactDescriptor.cs` — Type + SourcePath + Hash + Length + Extension
- `EvidenceCacheModels.cs` (~90 LOC) — 8 types: `EvidenceCacheArtifact` + `EvidenceCacheManifest` + `EvidenceCacheResult` + `EvidenceCacheModuleSelection` + `EvidenceCacheOutcome` + `EvidenceCacheInvalidationReason` (9-variant enum: ManifestMissing / ManifestInvalid / ManifestVersionMismatch / KeyMismatch / CommandMismatch / ManifestExpired / ModuleSelectionChanged / MetadataMismatch / ArtifactsMismatch / RefreshRequested) + `EvidenceCacheEvaluation` + `EvidenceCacheRequest`
- `EvidenceCacheService.cs` — facade orchestrating CacheRequestNormalizer + ManifestEvaluator + CacheEntryCreator
- `IEvidenceCacheService.cs` — `CacheAsync(request, ct) → Task<Result<EvidenceCacheResult>>`
- `ManifestEvaluator.cs` — `EvaluateAsync` with 9 validation checks (version + key + command + expiry + module-selection + metadata + artifacts + etc.)
- `EvidenceCacheWriter.cs` — WriteAsync copies artifacts + versioning + TTL policy + manifest JSON serialization

**Application (`Pipeline.Application/`; 21 files).** Application-service pattern.
- `IApplicationService<TInput, TResult>` interface (`Task<Result<'output>> RunAsync(TInput, CancellationToken)`)
- ~7 concrete services: `AnalyzeApplicationService`, `ExtractModelApplicationService`, `BuildSsdtApplicationService`, `FullExportApplicationService`, `CaptureProfileApplicationService`, `CompareWithDmmApplicationService`, `VerifyDataApplicationService` (50-250 LOC each)
- `PipelineRequestContextBuilder.cs` + `PipelineRequestContext.cs` (~250 LOC) — configuration assembly + context object carrying tightening options + module filter + SQL options + caching overrides + metadata logger + flush function

**Mediation (`Pipeline.Mediation/`; 2 files).** MediatR-style command pattern: `CommandDispatcher.cs` + `ICommand` / `ICommandHandler` interfaces (~80 LOC).

---

## 4. Emit — SSDT artifact production

### 4.1 SMO emission (`Osm.Smo`; 44 files / 7109 LOC)

V1's SCHEMA-axis cutover-fidelity engine. Built on `Microsoft.SqlServer.Management.Smo` — the SMO scripter library.

**Top-level orchestrator.** `SmoEntityEmitter.cs` — per-entity orchestrator constructing mutable SMO `Table` / `Column` / `Index` / `ForeignKey` objects, then calling `Table.Script()` to render text.

**Per-axis SMO builders.**
- `SmoTableBuilder.cs` — table-level construction
- `SmoColumnBuilder.cs` — column-level construction with nullability + identity + collation + defaults
- `SmoIndexBuilder.cs` — index construction
- `SmoForeignKeyBuilder.cs` (~111 LOC) — coordinates with 4 helpers: `ForeignKeyEvidenceResolver` (5-phase rule-matching), `ForeignKeyNameFactory.CreateEvidenceName`, `ForeignKeyColumnNormalizer.Normalize`, `ForeignKeyFallbackFactory`
- `SmoTriggerBuilder.cs` (~50 LOC) — extracts trigger definition; normalizes whitespace; skips encrypted triggers; sorts by name; emits `SmoTriggerDefinition` carrying raw T-SQL body

**Statement builders (ScriptDom-based at V1 too, but glue is SMO).**
- `CreateTableStatementBuilder.cs` (~490 LOC) — `BuildCreateTableStatement` constructs ScriptDom `CreateTableStatement`; column metadata (nullability + identity + collation + defaults + CHECK constraints + computed columns + column-name mapping via `ColumnReferenceRewriteVisitor`); PK logic (single-column inline; multi-column table-level); FK logic (inline trusted + ALTER TABLE for NOCHECK via string composition with LINT-ALLOW)
- `IndexScriptBuilder.cs` (~452 LOC) — `BuildCreateIndexStatement` handles columns + sort order + included columns + filter (via `ParsePredicate` using TSql150Parser) + options (FillFactor + PadIndex + IgnoreDupKey + StatisticsNoRecompute + AllowRowLocks + AllowPageLocks + DataCompression with partition-range collapse + FileGroup/PartitionScheme dataspace)
- `ExtendedPropertyScriptBuilder.cs` (~142 LOC) — emits `EXEC sys.sp_addextendedproperty` via string concatenation with `'` → `''` escaping

**Formatting + helpers.**
- `IdentifierFormatter.cs` — bracket-quoting per `QuoteType.SquareBracket`
- `ConstraintNameNormalizer.cs` — post-hoc rename mapping when table is overridden
- `ModuleNameSanitizer.cs` — module-name cleaning
- `IndexNameGenerator.cs` — index name construction
- `ExternalDatabaseTypeParser.cs` — parses external type strings
- `MsDescriptionResolver.cs` — `MS_Description` extended-property resolution
- `StatementBatchFormatter.cs` (~60 LOC) — GO-separator joining + `TrimEnd` per line

**Type mapping (4-file policy cluster).**
- `TypeMappingPolicy.cs` — resolves attribute data type via 3 paths (on-disk override + external DB type + attribute default)
- `TypeMappingRule.cs` + `TypeMappingRuleDefinition.cs` — rule shape
- `TypeMappingPolicyDefinition.cs` — policy shape
- `TypeMappingPolicyLoader.cs` — loads from JSON configuration + embedded defaults
- `TypeMappingKeyNormalizer.cs` — key normalization

**File writing.** `PerTableWriter.cs` (~99 LOC) — emits per-table to `Modules/<Module>/<Schema>.<Table>.sql`; `TableHeaderFactory.cs` (~55 LOC) — generates per-table SQL header `/* Source: ... LogicalName ... */`.

**Options.** `SmoBuildOptions.cs` + `SmoFormatOptions.cs` + `SmoRenameLens.cs` + `PerTableHeaderOptions.cs`.

**Context.** `SmoContext.cs` + `EntityEmissionContext.cs` + `EntityEmissionIndex.cs` + `TableArtifactSnapshotExtensions.cs` — emission-time context carriers.

### 4.2 SSDT orchestration (`Osm.Emission`; 20 files)

The layer between SMO emitter and the per-table file output.

**Top-level orchestrator.** `SsdtEmitter.cs` (~145 LOC) — coordinates planner → writer → manifest builder; directory structure setup; error handling.

**Planning.** `TableEmissionPlan.cs` (per-table emission plan record), `TableEmissionPlanner.cs` (~260 LOC; per-table planning with module directory layout + header factory + parallelism dispatch for Both/BareOnly/FullOnly modes), `TablePlanWriter.cs` + `ITablePlanWriter.cs` (~100 LOC; parallel file-write dispatcher with semaphore-bounded concurrency), `TableHeaderFactory.cs`.

**Topological ordering.** `EntityDependencySorter.cs` (~200+ LOC) — Kahn's algorithm + cycle detection (alphabetical fallback). `EntityDependencyOrderingModeExtensions.cs` (~15 LOC) — `Alphabetical / Topological / JunctionDeferred` mode utilities.

**Dynamic INSERT generation.** `DynamicEntityInsertGenerator.cs` (~790 LOC) — emits INSERT/MERGE for non-static runtime data with batch-size control + determinism. `PhasedDynamicEntityInsertGenerator.cs` (~150 LOC) — 2-phase cycle-breaking: Phase-1 INSERT (nullable FKs NULLed), Phase-2 UPDATE to populate.

**Static seed scripts.** `Seeds/EntitySeedDeterminizer.cs` (~40 LOC; sort by PK), `Seeds/StaticEntitySeedScriptGenerator.cs` (~95 LOC; orchestration), `Seeds/StaticEntitySeedTemplateService.cs` (~80 LOC; SQL template wrapping), `Seeds/StaticSeedForeignKeyPreflight.cs` (~80 LOC; FK validation + orphan detection + cross-module checks), `Seeds/StaticSeedSqlBuilder.cs` (~200+ LOC; MERGE construction with ON-clause + WHEN-NOT-MATCHED INSERT + WHEN-MATCHED UPDATE + drift detection).

**Formatting helpers.** `Formatting/SqlIdentifierFormatter.cs` (~30 LOC; bracket quoting + `]]` escape), `Formatting/SqlLiteralFormatter.cs` (~150 LOC; string escaping + null + numeric + type-specific quoting).

**Manifest.** `ManifestBuilder.cs` (~113 LOC) + `SsdtManifest.cs` (~91 LOC; 8-field shape: Tables + Options + PolicySummary + Emission + PreRemediation + Coverage + PredicateCoverage + Unsupported) + `SsdtPredicateCoverage.cs` (~49 LOC; two-section shape: Tables + PredicateCounts dict).

### 4.3 Static + dynamic data emission (`Pipeline.{StaticData,DynamicData}`)

Data-source abstraction + extraction.

**Static data (1 file).** `StaticData/StaticEntityDataProviders.cs` (~400 LOC) — static-data source abstraction with fixture loader (JSON) + SQL extractors.

**Dynamic data (7 files).** `DynamicData/SqlDynamicEntityDataProvider.cs` (~500 LOC) — dynamic-data extraction with per-module SQL queries + row batching + telemetry. Adjacent provider interfaces + supporting machinery.

### 4.4 UAT user reflow (`Osm.Pipeline.UatUsers`; 23 files)

V1's User-FK reflow surface — handles platform-user-identity remapping between dev/uat/prod.

**Core engine.** `UserMatchingEngine.cs` (~316 LOC) — 3 strategies (`CaseInsensitiveEmail` / `ExactAttribute` / `Regex`) + fallback (`RoundRobin` / `SingleTarget` / `Ignore`); per-strategy matching (`TryExactMatch` / `TryRegexMatch` ~192-250 LOC); lookup built once per execute (dictionary-keyed by attribute).

**Identity type.** `UserIdentifier.cs` (~155 LOC) — 3-variant discriminator (Numeric/Guid/Text; `FromString` / `FromDatabaseValue` factories).

**Context state.** `UatUsersContext.cs` (~302 LOC) — orchestration surface.

**Steps subdirectory.** Six steps in `Steps/` chained via `IPipelineStep<UatUsersContext>`: `LoadQaUserInventory` / `PrepareUserMap` / `AnalyzeForeignKeyValues` / `DiscoverUserFkCatalog` / `ApplyMatchingStrategy` / `LoadUatUserInventory`.

**Verification subdirectory.** `UatUsersVerifier.cs` (~128 LOC; orchestrator) + `FkCatalogCompletenessVerifier` + `TransformationMapVerifier` + `SqlSafetyAnalyzer`. `UatUsersVerificationContext` + `UatUsersVerificationReport.fs` (synthesize results).

**Pipeline runner.** `UatUsersPipelineRunner.cs` — sequential step orchestrator; mutable context mutated at each step.

---

## 5. Orchestrate — Pipeline wiring

### 5.1 BuildSsdt pipeline (`Osm.Pipeline.Orchestration`; 53 files / 9050 LOC)

V1's top-level pipeline. The biggest cluster in the entire codebase.

**Core orchestrator.** `BuildSsdtPipeline.cs` — imperative step-chaining: 12 sequential `.BindAsync()` calls; each step is `IBuildSsdtStep<TState, TNextState>` (binding step as named field via DI); ordering is source-coupled to field declaration.

**Per-step implementations (the 12+ Build*Step.cs files).**
- `BuildSsdtBootstrapStep.cs` + `BuildSsdtBootstrapSnapshotStep.cs` — bootstrap (load model + profile; capture snapshot for idempotent redeployment)
- `BuildSsdtEmissionStep.cs` — main emission (invokes `ISmoModelFactory.Create` + `ISsdtEmitter.EmitAsync`)
- `BuildSsdtDynamicInsertStep.cs` — dynamic INSERT generation
- `BuildSsdtEvidenceCacheStep.cs` — evidence caching
- `BuildSsdtPolicyDecisionStep.cs` — policy decisions
- `BuildSsdtPostDeploymentTemplateStep.cs` — `PostDeployment-Bootstrap.sql` template with guard logic
- `BuildSsdtSqlProjectStep.cs` — `.sqlproj` MSBuild file
- `BuildSsdtSqlValidationStep.cs` — SSDT validation via SMO + DacFx (using `SsdtSqlValidator.cs` + `SsdtSqlValidationSummary.cs`)
- `BuildSsdtStaticSeedStep.cs` — static seed emission
- `BuildSsdtTelemetryPackagingStep.cs` — telemetry packaging

**Adjacent pipelines.**
- `CaptureProfilePipeline.cs` — separate two-pass profile-capture pipeline; blocks BuildSsdtPipeline via callback
- `DmmComparePipeline.cs` + `DmmComparePipelineRequest.cs` — V1 SMO Model vs emitted SSDT comparison via DMM lenses; emit diff log

**Supporting machinery.**
- `BasicDataIntegrityChecker.cs` + `DataIntegrityVerificationReport.cs` — data-integrity check
- `EmissionCoverageCalculator.cs` — static method (`OsmModel + PolicyDecisionSet + SmoModel + SmoBuildOptions → EmissionCoverageResult`)
- `EmissionFingerprintCalculator.cs` — cryptographic hash of emission shape for round-trip assertions
- `EvidenceCacheCoordinator.cs` + `EvidenceCachePipelineOptions.cs` — pipeline-level caching (uses 9-variant `EvidenceCacheInvalidationReason`)
- `OpportunityLogWriter.cs` — opportunity logging
- `DmmDiffLogWriter.cs` — DMM diff logging
- `PipelineLogMetadataBuilder.cs` + `PipelineInsight.cs` — pipeline diagnostics with severity enum (Info/Advisory/Warning/Critical); per-insight code + affected objects + suggested action; rolled up in `PipelineExecutionLog`
- `ResolvedSqlOptions.cs` + `SupplementalModelOptions.cs` — options
- `SchemaDataApplier.cs` — stateless utility applying schema + static/dynamic seed data to target database; wraps SMO + SqlCommand
- `ModelExecutionScope.cs` + `ModelLoader.cs` — model loading scope
- `BootstrapPipelineContext.cs` — bootstrap context

**Request/Result.** `BuildSsdtPipelineRequest.cs` (14 fields) + `BuildSsdtPipelineResult.cs` (28 fields, immutable record) + intermediate state records per step (`PipelineInitialized`, `BootstrapCompleted`, `EvidenceCacheResult`, etc.; 18+ intermediate record types).

### 5.2 Configuration + Runtime + Mediation (~22 files)

Operator config + runtime verbs.

**Configuration (`Pipeline.Configuration/`; 8 files).** `appsettings.json` schema + operator-config carriers + validation.

**Runtime (`Pipeline.Runtime/`; 8 files).** Includes `Verbs/` subdir (6 files) with verb dispatch.

**ModelIngestion (`Pipeline.ModelIngestion/`; 6 files).** Model loading + ingestion.

**Sql (`Pipeline.Sql/`; 7 files).** SQL execution helpers + connection abstractions.

---

## 6. Operate — CLI + Load harness

### 6.1 CLI (`Osm.Cli`; 40 files / 8677 LOC)

V1's operator-facing surface. **12 operator-facing verbs** (plus a nested `policy explain` subcommand) wrapped in a sophisticated `System.CommandLine`-based option-binding infrastructure.

**Verbs (the operator-facing commands).**
- `AnalyzeCommandFactory.cs` — analyze (V1 model + profile → tightening analysis report)
- `BuildSsdtCommandFactory.cs` — build-ssdt (main SSDT emission)
- `DmmCompareCommandFactory.cs` — dmm-compare (SSDT bundle + DMM baseline → diff report)
- `ExtractModelCommandFactory.cs` — extract-model (OSSYS DB connection → V1 JSON model)
- `FullExportCommandFactory.cs` — full-export (orchestrates extraction → profiling → emission → load-harness replay)
- `InspectCommandFactory.cs` — inspect (V1 JSON model → summary counts)
- `PipelineCommandFactory.cs` — pipeline subcommand parent
- `PolicyCommandFactory.cs` — policy (with `explain` subcommand: decision report → formatted output)
- `ProfileCommandFactory.cs` — profile (V1 model + SQL connection → profile snapshot)
- `UatUsersCommandFactory.cs` + `UatUsersCommand.cs` — uat-users (model + UAT inventory → user-remapping artifacts)
- `VerifyDataCommandFactory.cs` — verify-data (model + source/target DB → data integrity report)

**Option-binding infrastructure.** 7 specialized binders (`ModuleFilterOptionBinder` / `CacheOptionBinder` / `SqlOptionBinder` / `TighteningOptionBinder` / `SchemaApplyOptionBinder` / `UatUsersOptionBinder` / `IVerbOptionExtension`) + `VerbOptionRegistry` + `VerbOptionsBuilder` + `VerbOptionDeclaration` + `VerbOverrideBindingContext` + `VerbBoundOptions`. Verbs compose via `.UseModuleFilter().UseSql().UseTightening()`.

**Global options.** `CliGlobalOptions.cs` — cross-verb config (config path + max parallelism); dependency-injected into every verb factory.

**Progress reporting.** `IProgressRunner.cs` + `SpectreConsoleProgressService.cs` — Spectre.Console TUI integration; wraps every verb run in progress bar (task descriptions + % complete + ETA).

**Console + reports.** `CommandConsole.cs` — abstraction (Write / WriteErrorLine / WriteTable / WriteErrors). `PipelineReportLauncher.cs` + `OpenReportVerbExtension.cs` — `--open-report` flag launches SSMS / Excel via ShellExecute.

**Adjacent.** `Program.cs` (CLI entry); `ICommandFactory.cs` + `ICommandOptionSource.cs` + `PolicyDecisionLinkBuilder.cs` + `ProfileSnapshotDebugFormatter.cs` + `FullExportLoadHarnessExtension.cs` + `UatUsersOptions.cs` + `UatUsersOptionOrigins.cs`.

### 6.2 Load harness (`Osm.LoadHarness`; 6 files / ~1300 LOC)

V1's script-replay + DB instrumentation tool.

**Orchestrator.** `LoadHarnessRunner.cs` — ExecuteAsync with script replay + batch splitting on GO; Stopwatch per batch.

**Options.** `LoadHarnessOptions.cs` — `SafeScriptPath` + `RemediationScriptPath` + `StaticSeedScriptPaths` + `DynamicInsertScriptPaths`.

**Result.** `ScriptReplayResult.cs` — per-batch timing + wait-stats delta + lock summary + index fragmentation.

**DMV queries.**
- `QueryWaitStatsAsync` (~lines 278-303) + delta calculation (~306-329) — wait stats from sys.dm_os_wait_stats
- `QueryLockSummaryAsync` (~331-357) — locks from sys.dm_tran_locks
- `QueryIndexFragmentationAsync` (~360-388) — fragmentation from sys.dm_db_index_physical_stats

**Report.** `LoadHarnessReport.cs` — startTime + endTime + script results + total duration.

---

## 7. Compare — DMM lens machinery (`Osm.Dmm`; 8 files / 2200 LOC)

V1's schema-diff machinery. **V2 sunsets entirely per slice 5.8.α; concept harvested as future `projection compare` CLI verb (matrix row 41).**

**Port.** `IDmmLens<TSource>` interface (9 LOC) — `Project(source) → Result<IReadOnlyList<DmmTable>>`.

**Three lens adapters.**
- `ScriptDomDmmLens.cs` (~619 LOC) — parses raw T-SQL via ScriptDom
- `SmoDmmLens.cs` (~310 LOC) — reads SMO model
- `SsdtProjectDmmLens.cs` (~276 LOC) — reads SSDT project files

**Comparator.** `DmmComparator.cs` (~690 LOC) — feature-gated structural diff over Columns / PrimaryKeys / Indexes / ForeignKeys via `DmmComparisonFeatures` flags.

**Specialized.** `SsdtTableLayoutComparator.cs` (~211 LOC) — table-layout-specific.

**DTOs.** `DmmModels.cs` (~72 LOC) — `DmmTable` / `DmmColumn` / `DmmIndex` / `DmmIndexColumn` / `DmmIndexOptions` / `DmmForeignKey` / `DmmForeignKeyColumn`.

**Feature flags.** `DmmComparisonFeatures.cs` (~14 LOC) — Columns | PrimaryKeys | Indexes | ForeignKeys.

---

## 8. Cross-section flow — How the parts connect

A typical V1 `build-ssdt` invocation:

```
CLI verb factory (BuildSsdtCommandFactory)
  ↓ resolves CliGlobalOptions + option binders + global config
  ↓
BuildSsdtApplicationService.RunAsync (input → pipeline → result)
  ↓
PipelineRequestContextBuilder (config assembly into PipelineRequestContext)
  ↓
BuildSsdtPipeline.HandleAsync (12 sequential .BindAsync() calls)
  ↓
  Step 1: BuildSsdtBootstrapStep — load model (Osm.Json.ModelJsonDeserializer) + profile
  ↓
  Step 2: BuildSsdtBootstrapSnapshotStep — capture snapshot for idempotent redeployment
  ↓
  Step 3: BuildSsdtEvidenceCacheStep — caching evaluation (EvidenceCacheService.CacheAsync)
  ↓
  Step 4: CaptureProfilePipeline (sub-pipeline) — Osm.Pipeline.Profiling.SqlDataProfiler
    ↓ Each table → TableSamplingPolicy → query builders (NullCount + UniqueCandidate + ForeignKeyProbe + ForeignKeyOrphanSample) → execute → ProfilingSnapshotNormalizer
  ↓
  Step 5: BuildSsdtPolicyDecisionStep — tightening decisions
    ↓ Osm.Validation.Tightening.NullabilityEvaluator (signal tree + override list)
    ↓ Osm.Validation.Tightening.ForeignKeyEvaluator
    ↓ Osm.Validation.Tightening.UniqueIndexDecisionStrategy + Orchestrator + EvidenceAggregator
    ↓ Each decision goes through ChangeRiskClassifier
    ↓ ColumnAnalysisBuilder aggregates per-column (NullabilityDecision + ForeignKeyDecision + UniqueIndexDecision + ChangeRisk + Opportunities)
    ↓ PolicyDecisionReporter walks decision dicts → produces ColumnDecisionReport + UniqueIndexDecisionReport + ForeignKeyDecisionReport + ModuleDecisionRollups
  ↓
  Step 6: BuildSsdtEmissionStep — main SSDT emission
    ↓ Osm.Smo.SmoModelFactory.Create → SMO Table/Column/Index/ForeignKey objects
    ↓ Osm.Smo.SmoEntityEmitter → CreateTableStatementBuilder + IndexScriptBuilder + ExtendedPropertyScriptBuilder
    ↓ Osm.Emission.SsdtEmitter → TableEmissionPlanner → TablePlanWriter (semaphore-bounded parallel writes)
    ↓ Per-table file at Modules/<Module>/<Schema>.<Table>.sql with header comment
  ↓
  Step 7: BuildSsdtStaticSeedStep — static seed scripts
    ↓ Osm.Emission.Seeds.EntityDependencySorter (Kahn + alpha fallback)
    ↓ Osm.Emission.Seeds.StaticEntitySeedScriptGenerator → StaticSeedSqlBuilder (MERGE construction)
    ↓ Osm.Emission.Seeds.StaticSeedForeignKeyPreflight (FK validation)
  ↓
  Step 8: BuildSsdtDynamicInsertStep — dynamic INSERT generation
    ↓ Osm.Emission.DynamicEntityInsertGenerator (~790 LOC) + PhasedDynamicEntityInsertGenerator (2-phase cycle-break)
  ↓
  Step 9: BuildSsdtPostDeploymentTemplateStep — PostDeployment-Bootstrap.sql with guard logic
  ↓
  Step 10: BuildSsdtSqlProjectStep — .sqlproj MSBuild file
  ↓
  Step 11: BuildSsdtSqlValidationStep — SsdtSqlValidator (SMO + DacFx integration)
  ↓
  Step 12: BuildSsdtTelemetryPackagingStep — package PipelineExecutionLog + diagnostics
  ↓
Manifest (Osm.Emission.ManifestBuilder.Build) → SsdtManifest JSON
  ↓
BuildSsdtPipelineResult (28 fields)
  ↓
CLI surfaces: SpectreConsoleProgressService updates + CommandConsole.WriteTable + OpenReportVerbExtension launches SSMS
```

The diagnostic surface (decisions + opportunities + insights) flows from `Pipeline.Insight` + `PolicyDecisionReporter` + `PolicyDecisionSummaryFormatter` into operator-facing prose reports (formatted via 6-bucket classification: Mandatory / ForeignKey / PrimaryKey / Unique / Physical / Remediation).

---

## 9. Cross-cutting structural patterns V1 carries

### 9.1 Imperative step-chaining

V1's hallmark architectural pattern. `BuildSsdtPipeline` chains `IBuildSsdtStep<TState, TNextState>` instances; each step is a DI-injected class with a state-typed `HandleAsync`. Per-step record types accumulate context. V2 inverts to **registry-driven composition** — see `DECISIONS 2026-05-18 (slice 5.6.α.orchestration)`.

### 9.2 Signal-combinator decision algebra

V1's tightening decisions flow through a recursive signal tree (`AllOfSignal` + `AnyOfSignal` combinators) with 2-valued result + string-rationale accumulation. The pattern recurs at the `NullabilityEvaluator` (~315 LOC) which assembles a per-mode signal tree at evaluation time. V2 replaces with **typed-strategy + closed-DU Outcome** — see `DECISIONS 2026-05-18 (slice 5.4.β.nullability)`.

### 9.3 Mutable accumulator + opportunity-record deferral

V1 uses `OpportunityBuilder.Add` as a mutable accumulator throughout the decision-engine machinery. Contested decisions (e.g., mandatory column with nulls beyond budget AND tightening forbidding silent relaxation) return `Result : false` but ALSO surface an `Opportunity` record via side channel with `Disposition.NeedsRemediation`. The decision is deferred via out-of-band metadata. V2 reifies the third state via **ternary Outcome variant `RequireOperatorApproval`** — see `DECISIONS 2026-05-18 (slice 5.4.β.nullability)`.

### 9.4 SMO + string-composed SQL

V1's emission uses SMO objects + `Table.Script()` for DDL rendering + string concatenation for ALTER TABLE / NOCHECK statements (with LINT-ALLOW comments at the concatenation sites). The pattern flows from V1's roots as a `SqlServerSmoExtensions` library. V2 replaces with **ScriptDom typed AST + pinned `Sql160ScriptGenerator`** — see `DECISIONS 2026-05-18 (slice 5.3.α.smo)`.

### 9.5 ApplicationService + MediatR command dispatch

V1's `IApplicationService<TInput, TResult>` interface + ~7 concrete services + MediatR-style `CommandDispatcher` + `ICommand` / `ICommandHandler` interfaces. Provides the contract surface between CLI args and pipeline orchestration. V2 trades interface-heavy dispatch for **typed-function pass signatures + functional composition** — see DECISIONS entries on V2 self-containment + object-expressions deferral.

### 9.6 Caching with manifest-validation evaluation

V1's `EvidenceCacheService` has a 9-validation-check `ManifestEvaluator` that decides whether to reuse a cached artifact. The 9-variant `EvidenceCacheInvalidationReason` enum decomposes the decision space exhaustively. V2 has **no equivalent caching layer in Core** — canonical output is `seq<Statement>` (Π); cache management is realization-layer policy per A35.

### 9.7 String-rationale accumulation + bucket classification

V1's decision diagnostics accumulate string rationales (`TighteningRationales.cs` const labels) into per-decision arrays; `PolicyDecisionSummaryFormatter` pattern-matches on these strings via `HasRationale` helper to classify decisions into 6 buckets (Mandatory / ForeignKey / PrimaryKey / Unique / Physical / Remediation). V2 replaces with **typed Outcome DUs carrying typed evidence per variant** — see `DECISIONS 2026-05-18 (slice 5.4.γ.opportunities)`.

### 9.8 DMV-based instrumentation

V1's `LoadHarness` queries 3 DMVs (sys.dm_os_wait_stats + sys.dm_tran_locks + sys.dm_db_index_physical_stats) per script-replay invocation to instrument perf/lock/fragmentation behavior. V2 has no DMV instrumentation — `Bench` surface covers timing per A24/A25; DMV is post-cutover operator-facing tool.

### 9.9 Spectre.Console TUI progress

V1's `IProgressRunner` + `SpectreConsoleProgressService` wraps every verb run in a TUI progress bar with task descriptions + % complete + ETA. V2 has no progress surface at launch; per chapter 5.1 cash-out, would hook V2's `Bench.snapshot()` per-iteration samples into a Spectre.Console renderer at CLI boundary.

### 9.10 11 naming value objects

V1's `Osm.Domain/ValueObjects/*.cs` carries 11 separate record-struct VOs (EntityName / ModuleName / TableName / ColumnName / AttributeName / SchemaName / IndexName / ForeignKeyName / SequenceName / TriggerName) — each a wrapper around a string with smart-constructor validation. V2 consolidates to **one load-bearing identity VO (SsKey 4-variant DU per A1) + Name VO (presentation) + Coordinates typed records** — see matrix row 180 + pillar 8.

### 9.11 SMO scripter + DMM 3-lens schema-diff

V1's verification approach uses SMO-rendered DDL vs DMM lens comparisons for fidelity proof. The 3-lens machinery (`ScriptDomDmmLens` + `SmoDmmLens` + `SsdtProjectDmmLens`) lets operators diff arbitrary schema sources. V2 sunsets per slice 5.8.α; V2's load-bearing fidelity gate is **the canary's `PhysicalSchema` round-trip diff** — see matrix rows 40-41 + chapter 3.1 close.

---

## 10. V1 surfaces NOT carried forward by V2

Per the matrix ⚫ V1-SUNSET rows. V2 deliberately does not inherit:

| V1 surface | Matrix row | Reason |
|---|---|---|
| JSON-aggregation rowsets (8 rowsets) | 13, 21, 22, 24-28 | V2 emits SSDT directly; `osm_model.json` emission path retires with V1 |
| `outsystems_model_export.sql` | 39 | Producer-side companion to above |
| `DmmCompare` pipeline | 134 | V2 canary subsumes; DMM lens machinery sunset |
| DMM lens machinery (8 files) | 40 | V2 canary `PhysicalSchema` diff replaces |
| `dmm-compare` CLI verb | 109 | Same |
| `CaptureProfilePipeline` | 133 | V2 adapter-input model per A34 |
| `DefaultSignal` (telemetry-only) | 67 | V2 omits as semantic noise; DEFAULT doesn't prevent NULL inserts |
| `TighteningRationales` string constants | 84 | V2 typed Outcome DUs subsume |
| `LoadHarness` DMV instrumentation (perf measurement piece) | 178 | V2 canary covers structural; DMV deferred to post-cutover operator tool |

Each sunset row carries a sunset rationale + migration impact + sunset timing (typically cutover+30 per `VISION.md` ladder). V1 stays warm as fallback through cutover+30 regardless.

---

## 11. Reading guide for future V2 agents

When you open a new V1-translation chapter:

1. **Read this compendium's section matching your audit scope** (e.g., section 3.1 for tightening; section 4.1 for SMO emission).
2. **Open the V1 source files named in your section** — the compendium's per-file line-count + role annotations let you target the read.
3. **Read the corresponding matrix rows** — Section 8 of this compendium lists row ranges per cluster; each row carries V1 source + V2 representation + Status + Notes.
4. **Read the relevant DECISIONS entry** if classification was DIVERGENCE — the rationale is where V2's "why" lives.
5. **Read the V2 source files named in the matrix Notes column** — these are the canonical V2 implementation references.

When you open a new V2 chapter (not V1-translation):

1. **Read the V2 Patterns Compendium** (`V2_PATTERNS_COMPENDIUM.md`) for the architectural patterns V2 prefers.
2. **Cross-check this compendium's Section 9** (cross-cutting structural patterns V1 carries) to understand what NOT to inherit (or what to deliberately invert).
3. **Read the Cutover Readiness Brief** (`CUTOVER_READINESS_BRIEF.md`) to understand the per-axis confidence state.

---

## 12. Closing

V1 is large (~78k LOC). Most of its architectural complexity flows from imperative-orchestration + SMO-based emission + interface-heavy dispatch + string-rationale composition — patterns that were idiomatic at V1's authoring time but that V2 has structurally replaced.

V1's lasting contributions to V2 are:
- The carbon-copied SQL contract (`outsystems_metadata_rowsets.sql`)
- The signal-architecture algebra (V2 reshapes to typed strategies; the algebra holds)
- The opportunity-record + ChangeRisk + ColumnAnalysis surface concepts (V2 reifies typed)
- The per-table file path convention (`Modules/<Module>/<Schema>.<Table>.sql`)
- The MERGE construction logic for static seeds (V2 ports to typed ScriptDom AST)
- The 6-bucket decision classification framing (V2 reshapes via outcome DUs)
- The User-FK reflow pattern (V2 ports to typed UserId + UserRemap)

What V1 carries that V2 deliberately replaces is documented per slice in the matrix + DECISIONS entries; what V1 carries that V2 inherits is documented in the V2 Patterns Compendium.

This compendium captures the "what V1 IS" — the V2 Patterns Compendium captures the "what V2 IS"; the Cutover Readiness Brief captures the "are we ready?"

— Compendium opened 2026-05-18 at chapter 5 audit-wave close.
