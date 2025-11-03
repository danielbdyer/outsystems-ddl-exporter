# Domain-Driven Design Target State for the OutSystems DDL Exporter

## 1. Vision and Strategic Outcomes
The OutSystems DDL Exporter is converging toward a deterministic, fixture-first platform that transforms OutSystems logical metadata into SQL Server-ready DDL with rich policy telemetry. The target domain-driven design centers on autonomous bounded contexts that collaborate through immutable contracts, providing:

- **Traceable tightening policies** that capture rationale, evidence, and toggle provenance, ensuring every `NOT NULL`, UNIQUE, or FK decision is auditable and reproducible in CI, mirroring the guardrails around flaggable behavior and deterministic outputs.【F:architecture-guardrails.md†L1-L86】【F:tasks.md†L1-L115】
- **Fixture-first orchestration** that promotes parity between live extractions and curated baselines, enabling regression-proof refactors and future pipeline automation.【F:architecture-guardrails.md†L40-L86】
- **Composable emission and comparison services** that rely exclusively on SMO/ScriptDom surfaces, preserving the no-string-concatenation guarantee and preparing artifacts for SSDT workflows.【F:architecture-guardrails.md†L87-L110】【F:readme.md†L8-L63】

This blueprint asserts clean boundaries, explicit contracts, and rich telemetry so the platform can scale to 200+ entities while remaining policy-driven and automatable.

## 2. Bounded Context Overview
The solution settles around six primary bounded contexts, each mapped to an existing project and reinforced by the architectural guardrails:

1. **Domain Model Core (`Osm.Domain`)** – Owns canonical entities, value objects, configuration primitives, and result semantics. Aggregates here are immutable and validated at construction to prevent downstream drift.
2. **JSON Ingestion (`Osm.Json`)** – Projects external payloads (Advanced SQL, profiler snapshots, rename rules) into domain models. It applies schema validation and deterministic parsing.
3. **Profiling & Evidence (`Osm.Validation` & `Osm.Pipeline.SqlExtraction`)** – Harvests profiling signals, synthesizes column-level evidence, and produces telemetry-ready aggregates for policy evaluation.
4. **Tightening Policy (`Osm.Validation.Tightening`)** – Encapsulates decision engines, opportunity reporting, and toggle snapshots that drive `NOT NULL`, UNIQUE, and FK outcomes.
5. **Emission & Parity (`Osm.Smo`, `Osm.Dmm`)** – Materializes SMO graphs, emits SSDT-ready scripts, and compares outcomes against DMM snapshots to enforce parity.
6. **Pipeline Orchestration & CLI (`Osm.Pipeline`, `Osm.Cli`)** – Provides application services, configuration loaders, evidence caching, and user-facing commands while remaining consumers of upstream contexts.

Each context exposes contracts exclusively through records and enumerations listed in the appendices, enabling teams to evolve implementations without breaking cross-context expectations.

## 3. Context Map & Collaboration Rules
The context map aligns with the dependency guardrail (Domain → Json/Profiling → Validation → SMO/DMM → Pipeline → CLI) and is codified as follows:

- **Domain Core ↔ JSON Ingestion**: JSON translators hydrate domain aggregates (`ModuleModel`, `EntityModel`, `AttributeModel`) from external manifests, returning `Result<T>` instances for composable error handling.
- **Profiling ↔ Tightening Policy**: Profiling evidence feeds `ColumnProfile`, `UniqueCandidateProfile`, and `ForeignKeyReality` value objects consumed by `TighteningPolicy` analyzers. Telemetry flows back through `PolicyDecisionReport` and opportunity summaries.
- **Tightening Policy ↔ Emission**: The policy layer emits `PolicyDecisionSet`, `ColumnIdentity`, and `UniqueIndexDecision` contracts. Emission contexts ingest these to build SMO object graphs without re-running eligibility logic.
- **Emission ↔ Pipeline**: Emission results (`SmoEmissionResult`, `DmmComparePipelineResult`) bubble up to pipeline services, which persist manifests, caches, and CLI summaries.
- **Pipeline ↔ CLI**: Application services surface typed `record` results consumed by CLI commands. CLI modules are thin orchestrators that serialize/deserialize records and stream telemetry to operators.

These relationships ensure upstream contexts remain pure and deterministic while downstream contexts handle IO, caching, and presentation concerns.

## 4. Aggregates, Entities, and Value Objects
The target design codifies key aggregates per context:

- **Module Aggregate**: `ModuleModel` encapsulates module metadata, referencing entity collections, static data descriptors, and validation diagnostics. Value objects like `ModuleName`, `EntityName`, and `TableName` enforce identifier invariants.
- **Entity Aggregate**: `EntityModel` aggregates attributes, indexes, references, and policies. Supporting records (`AttributeModel`, `IndexModel`, `ForeignKeyModel`) capture OutSystems intent, while profiling overlays supply physical evidence.
- **Policy Aggregate**: `PolicyDecisionSet`, `NullabilityDecision`, `ForeignKeyDecision`, and `UniqueIndexDecision` combine to represent the tightening outcome. `ColumnIdentity` and `ColumnCoordinate` ensure column references remain unambiguous across modules and schemas.
- **Emission Aggregate**: `SmoEmissionResult` and allied records represent emitted artifacts, including per-table scripts, concatenated outputs, and metadata logs.
- **Cache Aggregate**: `EvidenceCacheResult`, `Reuse`, and `Rebuild` express cache evaluation states, allowing pipeline services to short-circuit expensive extractions while preserving audit trails.

Each aggregate is immutable, constructor-validated, and emits telemetry through dedicated records like `PolicyDecisionReport` and `OpportunitiesReport`.

## 5. Application Services and Cross-Cutting Policies
Application services (`BuildSsdtApplicationService`, `CaptureProfileApplicationService`, etc.) orchestrate context collaboration by:

- Resolving configuration through `CliConfiguration` and override records.
- Building `PipelineRequestContext` instances that inject module filters, SQL overrides, cache policies, and naming overrides.
- Sequencing extraction, profiling, tightening, emission, and parity checks while emitting structured artifacts (`PipelineDecisionReport`, `manifest.json`, cache manifests).

Cross-cutting policies include toggle resolution (`TighteningToggleSnapshot`), telemetry summarization (`PolicyDecisionSummaryFormatter`), and error handling via `Result<T>` wrappers.

## 6. Implementation Guardrails Reinforced
The target state enforces the standing architectural guardrails by:

- Maintaining one-directional project references and preventing infrastructure concerns from leaking into the domain core.【F:architecture-guardrails.md†L1-L27】
- Centralizing feature flags and toggle precedence to keep tightening behavior auditable and safe for incremental rollout.【F:architecture-guardrails.md†L27-L55】
- Preserving deterministic, fixture-first pipelines that can be replayed without live dependencies, aiding CI/CD adoption.【F:architecture-guardrails.md†L55-L86】
- Guaranteeing SMO/ScriptDom remain the sole DDL authorities while CLI layers focus on orchestration and observability.【F:architecture-guardrails.md†L87-L135】

## 7. Data Object Catalog (Records)
The following catalog enumerates every `record`-based data object in the solution, grouped by project and namespace. These signatures define the immutable contracts exchanged between contexts. Entries marked `private` remain internal to their declaring context yet influence serialization formats or telemetry payloads.

### Osm.Cli (records: 18)

<details>
<summary><code>Osm.Cli</code> (8)</summary>

* `private sealed record DecisionRow( string Anchor, string Module, string Schema, string Table, string Object, string Action, string Remediation, string Rationales);`
* `private sealed record ModuleSummary( string Module, int Tables, int Indexes, int ForeignKeys, int Columns, int TightenedColumns, int RemediationColumns, int UniqueEnforced, int UniqueRemediation, int ForeignKeysCreated);`
* `private sealed record ProfileColumnDebug( string Schema, string Table, string Column, bool IsNullablePhysical, bool IsComputed, bool IsPrimaryKey, bool IsUniqueKey, string? DefaultDefinition, long RowCount, long NullCount);`
* `private sealed record ProfileCompositeUniqueCandidateDebug( string Schema, string Table, IReadOnlyList<string> Columns, bool HasDuplicate);`
* `private sealed record ProfileForeignKeyDebug( string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn, bool HasDatabaseConstraint, bool HasOrphan, bool IsNoCheck);`
* `private sealed record ProfileSnapshotDebugDocument( IReadOnlyList<ProfileColumnDebug> Columns, IReadOnlyList<ProfileUniqueCandidateDebug> UniqueCandidates, IReadOnlyList<ProfileCompositeUniqueCandidateDebug> CompositeUniqueCandidates, IReadOnlyList<ProfileForeignKeyDebug> ForeignKeys);`
* `private sealed record ProfileUniqueCandidateDebug( string Schema, string Table, string Column, bool HasDuplicate);`
* `private sealed record ToggleEntry(string Key, string Value, string Source);`

</details>

<details>
<summary><code>Osm.Cli.Commands</code> (10)</summary>

* `private sealed record ModuleDecisionRollupDocument( int ColumnCount, int TightenedColumnCount, int RemediationColumnCount, int UniqueIndexCount, int UniqueIndexesEnforcedCount, int UniqueIndexesRequireRemediationCount, int ForeignKeyCount, int ForeignKeysCreatedCount, IReadOnlyDictionary<string, int>? ColumnRationales, IReadOnlyDictionary<string, int>? UniqueIndexRationales, IReadOnlyDictionary<string, int>? ForeignKeyRationales);`
* `private sealed record NamingOverrideTemplateRule( [property: JsonPropertyName("schema"), JsonPropertyOrder(0)] string Schema, [property: JsonPropertyName("table"), JsonPropertyOrder(1)] string Table, [property: JsonPropertyName("module"), JsonPropertyOrder(2)] string Module, [property: JsonPropertyName("entity"), JsonPropertyOrder(3)] string Entity, [property: JsonPropertyName("override"), JsonPropertyOrder(4)] string Override);`
* `private sealed record PolicyDecisionLogColumnDocument( string? Schema, string? Table, string? Column, bool MakeNotNull, bool RequiresRemediation, IReadOnlyList<string>? Rationales, string? Module);`
* `private sealed record PolicyDecisionLogDiagnosticCandidateDocument( string? Module, string? Schema, string? PhysicalName);`
* `private sealed record PolicyDecisionLogDiagnosticDocument( string? Code, string? Message, string? Severity, string? LogicalName, string? CanonicalModule, string? CanonicalSchema, string? CanonicalPhysicalName, IReadOnlyList<PolicyDecisionLogDiagnosticCandidateDocument>? Candidates, bool ResolvedByOverride);`
* `private sealed record PolicyDecisionLogDocument( IReadOnlyDictionary<string, int>? ColumnRationales, IReadOnlyDictionary<string, int>? UniqueIndexRationales, IReadOnlyDictionary<string, int>? ForeignKeyRationales, IReadOnlyDictionary<string, ModuleDecisionRollupDocument>? ModuleRollups, IReadOnlyDictionary<string, ToggleEntryDocument>? TogglePrecedence, IReadOnlyList<PolicyDecisionLogColumnDocument>? Columns, IReadOnlyList<PolicyDecisionLogUniqueIndexDocument>? UniqueIndexes, IReadOnlyList<PolicyDecisionLogForeignKeyDocument>? ForeignKeys, IReadOnlyList<PolicyDecisionLogDiagnosticDocument>? Diagnostics);`
* `private sealed record PolicyDecisionLogForeignKeyDocument( string? Schema, string? Table, string? Column, bool CreateConstraint, IReadOnlyList<string>? Rationales, string? Module);`
* `private sealed record PolicyDecisionLogUniqueIndexDocument( string? Schema, string? Table, string? Index, bool EnforceUnique, bool RequiresRemediation, IReadOnlyList<string>? Rationales, string? Module);`
* `private sealed record Result<T>(bool IsSuccess, T Value, string? Error) {`
* `private sealed record ToggleEntryDocument(object? Value, int Source);`

</details>

### Osm.Dmm (records: 13)

<details>
<summary><code>Osm.Dmm</code> (13)</summary>

* `private sealed record TableMetadata( IReadOnlyDictionary<string, int> ColumnOrder, IReadOnlyList<string> PrimaryKeyColumns);`
* `private sealed record TableNamingMetadata(string Module, string LogicalName);`
* `public sealed record DmmColumn( string Name, string DataType, bool IsNullable, string? DefaultExpression, string? Collation, string? Description);`
* `public sealed record DmmComparisonResult( bool IsMatch, IReadOnlyList<DmmDifference> ModelDifferences, IReadOnlyList<DmmDifference> SsdtDifferences) {`
* `public sealed record DmmDifference( string Schema, string Table, string Property, string? Column = null, string? Index = null, string? ForeignKey = null, string? Expected = null, string? Actual = null, string? ArtifactPath = null);`
* `public sealed record DmmForeignKey( string Name, IReadOnlyList<DmmForeignKeyColumn> Columns, string ReferencedSchema, string ReferencedTable, string DeleteAction, bool IsNotTrusted);`
* `public sealed record DmmForeignKeyColumn(string Column, string ReferencedColumn);`
* `public sealed record DmmIndex( string Name, bool IsUnique, IReadOnlyList<DmmIndexColumn> KeyColumns, IReadOnlyList<DmmIndexColumn> IncludedColumns, string? FilterDefinition, bool IsDisabled, DmmIndexOptions Options);`
* `public sealed record DmmIndexColumn(string Name, bool IsDescending);`
* `public sealed record DmmIndexOptions( bool? PadIndex, int? FillFactor, bool? IgnoreDuplicateKey, bool? AllowRowLocks, bool? AllowPageLocks, bool? StatisticsNoRecompute);`
* `public sealed record DmmTable( string Schema, string Name, IReadOnlyList<DmmColumn> Columns, IReadOnlyList<string> PrimaryKeyColumns, IReadOnlyList<DmmIndex> Indexes, IReadOnlyList<DmmForeignKey> ForeignKeys, string? Description);`
* `public sealed record SmoDmmLensRequest(SmoModel Model, SmoBuildOptions Options);`
* `public sealed record SsdtTableLayoutComparisonResult( bool IsMatch, IReadOnlyList<DmmDifference> ModelDifferences, IReadOnlyList<DmmDifference> SsdtDifferences);`

</details>

### Osm.Domain (records: 82)

<details>
<summary><code>Osm.Domain.Abstractions</code> (1)</summary>

* `public readonly record struct ValidationError {`

</details>

<details>
<summary><code>Osm.Domain.Configuration</code> (16)</summary>

* `public sealed record EmissionOptions {`
* `public sealed record EntityOverrideDefinition(bool AppliesToAll, ImmutableHashSet<string> Entities) {`
* `public sealed record ForeignKeyOptions {`
* `public sealed record MockingOptions {`
* `public sealed record ModuleEntityFilterOptions {`
* `public sealed record ModuleFilterOptions {`
* `public sealed record ModuleValidationOverrideConfiguration( IReadOnlyList<string> AllowMissingPrimaryKey, bool AllowMissingPrimaryKeyForAll, IReadOnlyList<string> AllowMissingSchema, bool AllowMissingSchemaForAll) {`
* `public sealed record ModuleValidationOverrideDefinition( EntityOverrideDefinition AllowMissingPrimaryKey, EntityOverrideDefinition AllowMissingSchema) {`
* `public sealed record NamingOverrideOptions {`
* `public sealed record NamingOverrideRule( SchemaName? Schema, TableName? PhysicalName, ModuleName? Module, EntityName? LogicalName, TableName Target) {`
* `public sealed record PolicyOptions {`
* `public sealed record RemediationOptions {`
* `public sealed record RemediationSentinelOptions {`
* `public sealed record StaticSeedOptions {`
* `public sealed record TighteningOptions {`
* `public sealed record UniquenessOptions {`

</details>

<details>
<summary><code>Osm.Domain.Model</code> (27)</summary>

* `private readonly record struct DuplicateAttributeGroup( string Key, ImmutableArray<string> LogicalNames, ImmutableArray<string> ColumnNames);`
* `public sealed record AttributeMetadata( string? Description, ImmutableArray<ExtendedProperty> ExtendedProperties) {`
* `public sealed record AttributeModel( AttributeName LogicalName, ColumnName ColumnName, string? OriginalName, string DataType, int? Length, int? Precision, int? Scale, string? DefaultValue, bool IsMandatory, bool IsIdentifier, bool IsAutoNumber, bool IsActive, AttributeReference Reference, string? ExternalDatabaseType, AttributeReality Reality, AttributeMetadata Metadata, AttributeOnDiskMetadata OnDisk) {`
* `public sealed record AttributeOnDiskCheckConstraint(string? Name, string Definition, bool IsNotTrusted) {`
* `public sealed record AttributeOnDiskDefaultConstraint(string? Name, string Definition, bool IsNotTrusted) {`
* `public sealed record AttributeOnDiskMetadata( bool? IsNullable, string? SqlType, int? MaxLength, int? Precision, int? Scale, string? Collation, bool? IsIdentity, bool? IsComputed, string? ComputedDefinition, string? DefaultDefinition, AttributeOnDiskDefaultConstraint? DefaultConstraint, ImmutableArray<AttributeOnDiskCheckConstraint> CheckConstraints) {`
* `public sealed record AttributeReality( bool? IsNullableInDatabase, bool? HasNulls, bool? HasDuplicates, bool? HasOrphans, bool IsPresentButInactive) {`
* `public sealed record AttributeReference( bool IsReference, int? TargetEntityId, EntityName? TargetEntity, TableName? TargetPhysicalName, string? DeleteRuleCode, bool HasDatabaseConstraint) {`
* `public sealed record EntityMetadata( string? Description, ImmutableArray<ExtendedProperty> ExtendedProperties, TemporalTableMetadata Temporal) {`
* `public sealed record EntityModel( ModuleName Module, EntityName LogicalName, TableName PhysicalName, SchemaName Schema, string? Catalog, bool IsStatic, bool IsExternal, bool IsActive, ImmutableArray<AttributeModel> Attributes, ImmutableArray<IndexModel> Indexes, ImmutableArray<RelationshipModel> Relationships, ImmutableArray<TriggerModel> Triggers, EntityMetadata Metadata) {`
* `public sealed record ExtendedProperty(string Name, string? Value) {`
* `public sealed record ForeignKeyModel( ForeignKeyName Name, ModuleName TargetModule, EntityName TargetEntity, ImmutableArray<ColumnName> FromColumns, ImmutableArray<ColumnName> ToColumns, string DeleteRule, string UpdateRule) {`
* `public sealed record IndexColumnModel( AttributeName Attribute, ColumnName Column, int Ordinal, bool IsIncluded, IndexColumnDirection Direction) {`
* `public sealed record IndexDataSpace(string Name, string Type) {`
* `public sealed record IndexModel( IndexName Name, bool IsUnique, bool IsPrimary, bool IsPlatformAuto, ImmutableArray<IndexColumnModel> Columns, IndexOnDiskMetadata OnDisk, ImmutableArray<ExtendedProperty> ExtendedProperties) {`
* `public sealed record IndexOnDiskMetadata( IndexKind Kind, bool IsDisabled, bool IsPadded, int? FillFactor, bool IgnoreDuplicateKey, bool AllowRowLocks, bool AllowPageLocks, bool NoRecomputeStatistics, string? FilterDefinition, IndexDataSpace? DataSpace, ImmutableArray<IndexPartitionColumn> PartitionColumns, ImmutableArray<IndexPartitionCompression> DataCompression) {`
* `public sealed record IndexPartitionColumn(ColumnName Column, int Ordinal) {`
* `public sealed record IndexPartitionCompression(int PartitionNumber, string Compression) {`
* `public sealed record ModuleModel( ModuleName Name, bool IsSystemModule, bool IsActive, ImmutableArray<EntityModel> Entities, ImmutableArray<ExtendedProperty> ExtendedProperties) {`
* `public sealed record OsmModel( DateTime ExportedAtUtc, ImmutableArray<ModuleModel> Modules, ImmutableArray<SequenceModel> Sequences, ImmutableArray<ExtendedProperty> ExtendedProperties) {`
* `public sealed record RelationshipActualConstraint( string Name, string ReferencedSchema, string ReferencedTable, string OnDeleteAction, string OnUpdateAction, ImmutableArray<RelationshipActualConstraintColumn> Columns) {`
* `public sealed record RelationshipActualConstraintColumn( string OwnerColumn, string OwnerAttribute, string ReferencedColumn, string ReferencedAttribute, int Ordinal) {`
* `public sealed record RelationshipModel( AttributeName ViaAttribute, EntityName TargetEntity, TableName TargetPhysicalName, string DeleteRuleCode, bool HasDatabaseConstraint, ImmutableArray<RelationshipActualConstraint> ActualConstraints) {`
* `public sealed record SequenceModel( SchemaName Schema, SequenceName Name, string DataType, decimal? StartValue, decimal? Increment, decimal? Minimum, decimal? Maximum, bool IsCycleEnabled, SequenceCacheMode CacheMode, int? CacheSize, ImmutableArray<ExtendedProperty> ExtendedProperties) {`
* `public sealed record TemporalRetentionPolicy( TemporalRetentionKind Kind, int? Value, TemporalRetentionUnit Unit) {`
* `public sealed record TemporalTableMetadata( TemporalTableType Type, SchemaName? HistorySchema, TableName? HistoryTable, ColumnName? PeriodStartColumn, ColumnName? PeriodEndColumn, TemporalRetentionPolicy RetentionPolicy, ImmutableArray<ExtendedProperty> ExtendedProperties) {`
* `public sealed record TriggerModel( TriggerName Name, bool IsDisabled, string Definition) {`

</details>

<details>
<summary><code>Osm.Domain.Model.Artifacts</code> (18)</summary>

* `public sealed record EntityEmissionSnapshot( TableArtifactIdentity Identity, ImmutableArray<TableColumnSnapshot> Columns, ImmutableArray<TableIndexSnapshot> Indexes, ImmutableArray<TableForeignKeySnapshot> ForeignKeys, ImmutableArray<TableTriggerSnapshot> Triggers, TableArtifactMetadata Metadata) {`
* `public sealed record TableArtifactEmissionMetadata( string TableName, string? ManifestPath, ImmutableArray<string> IndexNames, ImmutableArray<string> ForeignKeyNames, bool IncludesExtendedProperties) {`
* `public sealed record TableArtifactIdentity( string Module, string OriginalModule, string Schema, string Name, string LogicalName, string? Catalog) {`
* `public sealed record TableArtifactMetadata(string? Description) {`
* `public sealed record TableArtifactProfilingMetadata(long? RowCount) {`
* `public sealed record TableArtifactSnapshot( TableArtifactIdentity Identity, ImmutableArray<TableColumnSnapshot> Columns, ImmutableArray<TableIndexSnapshot> Indexes, ImmutableArray<TableForeignKeySnapshot> ForeignKeys, ImmutableArray<TableTriggerSnapshot> Triggers, TableArtifactMetadata Metadata, TableArtifactProfilingMetadata? Profiling = null, TableArtifactEmissionMetadata? Emission = null) {`
* `public sealed record TableCheckConstraintSnapshot(string? Name, string Expression, bool IsNotTrusted) {`
* `public sealed record TableColumnSnapshot( string PhysicalName, string Name, string LogicalName, TableColumnTypeSnapshot DataType, bool Nullable, bool IsIdentity, int IdentitySeed, int IdentityIncrement, bool IsComputed, string? ComputedExpression, string? DefaultExpression, string? Collation, string? Description, TableDefaultConstraintSnapshot? DefaultConstraint, ImmutableArray<TableCheckConstraintSnapshot> CheckConstraints) {`
* `public sealed record TableColumnTypeSnapshot( string SqlType, string? Name, string? Schema, int? MaximumLength, int? NumericPrecision, int? NumericScale) {`
* `public sealed record TableDefaultConstraintSnapshot(string? Name, string Expression, bool IsNotTrusted) {`
* `public sealed record TableForeignKeySnapshot( string Name, ImmutableArray<string> Columns, string ReferencedModule, string ReferencedTable, string ReferencedSchema, ImmutableArray<string> ReferencedColumns, string ReferencedLogicalTable, string DeleteAction, bool IsNoCheck) {`
* `public sealed record TableIndexColumnSnapshot(string Name, int Ordinal, bool IsIncluded, bool IsDescending) {`
* `public sealed record TableIndexCompressionSnapshot(int PartitionNumber, string Compression) {`
* `public sealed record TableIndexDataSpaceSnapshot(string Name, string Type) {`
* `public sealed record TableIndexMetadataSnapshot( bool IsDisabled, bool IsPadded, int? FillFactor, bool IgnoreDuplicateKey, bool AllowRowLocks, bool AllowPageLocks, bool StatisticsNoRecompute, string? FilterDefinition, TableIndexDataSpaceSnapshot? DataSpace, ImmutableArray<TableIndexPartitionColumnSnapshot> PartitionColumns, ImmutableArray<TableIndexCompressionSnapshot> DataCompression) {`
* `public sealed record TableIndexPartitionColumnSnapshot(string Name, int Ordinal) {`
* `public sealed record TableIndexSnapshot( string Name, bool IsUnique, bool IsPrimaryKey, bool IsPlatformAuto, string? Description, ImmutableArray<TableIndexColumnSnapshot> Columns, TableIndexMetadataSnapshot Metadata) {`
* `public sealed record TableTriggerSnapshot( string Name, string Schema, string Table, bool IsDisabled, string Definition) {`

</details>

<details>
<summary><code>Osm.Domain.Model.Emission</code> (1)</summary>

* `public sealed record EntityEmissionSnapshot( string ModuleName, EntityModel Entity, ImmutableArray<AttributeModel> EmittableAttributes, ImmutableArray<AttributeModel> IdentifierAttributes, IReadOnlyDictionary<string, AttributeModel> AttributeLookup, AttributeModel? ActiveIdentifier, AttributeModel? FallbackIdentifier) {`

</details>

<details>
<summary><code>Osm.Domain.Profiling</code> (9)</summary>

* `public sealed record ColumnProfile( SchemaName Schema, TableName Table, ColumnName Column, bool IsNullablePhysical, bool IsComputed, bool IsPrimaryKey, bool IsUniqueKey, string? DefaultDefinition, long RowCount, long NullCount, ProfilingProbeStatus NullCountStatus) {`
* `public sealed record CompositeUniqueCandidateProfile( SchemaName Schema, TableName Table, ImmutableArray<ColumnName> Columns, bool HasDuplicate) {`
* `public sealed record ForeignKeyReality( ForeignKeyReference Reference, bool HasOrphan, bool IsNoCheck, ProfilingProbeStatus ProbeStatus) {`
* `public sealed record ForeignKeyReference( SchemaName FromSchema, TableName FromTable, ColumnName FromColumn, SchemaName ToSchema, TableName ToTable, ColumnName ToColumn, bool HasDatabaseConstraint) {`
* `public sealed record ProfileSnapshot( ImmutableArray<ColumnProfile> Columns, ImmutableArray<UniqueCandidateProfile> UniqueCandidates, ImmutableArray<CompositeUniqueCandidateProfile> CompositeUniqueCandidates, ImmutableArray<ForeignKeyReality> ForeignKeys) {`
* `public sealed record ProfilingInsight( ProfilingInsightSeverity Severity, ProfilingInsightCategory Category, string Message, ProfilingInsightCoordinate? Coordinate) {`
* `public sealed record ProfilingInsightCoordinate( SchemaName Schema, TableName Table, ColumnName? Column, SchemaName? RelatedSchema, TableName? RelatedTable, ColumnName? RelatedColumn) {`
* `public sealed record ProfilingProbeStatus( DateTimeOffset CapturedAtUtc, long SampleSize, ProfilingProbeOutcome Outcome) {`
* `public sealed record UniqueCandidateProfile( SchemaName Schema, TableName Table, ColumnName Column, bool HasDuplicate, ProfilingProbeStatus ProbeStatus) {`

</details>

<details>
<summary><code>Osm.Domain.ValueObjects</code> (10)</summary>

* `public readonly record struct AttributeName(string Value) {`
* `public readonly record struct ColumnName(string Value) {`
* `public readonly record struct EntityName(string Value) {`
* `public readonly record struct ForeignKeyName(string Value) {`
* `public readonly record struct IndexName(string Value) {`
* `public readonly record struct ModuleName(string Value) {`
* `public readonly record struct SchemaName(string Value) {`
* `public readonly record struct SequenceName(string Value) {`
* `public readonly record struct TableName(string Value) {`
* `public readonly record struct TriggerName(string Value) {`

</details>

### Osm.Emission (records: 15)

<details>
<summary><code>Osm.Emission</code> (11)</summary>

* `public sealed record CoverageBreakdown(int Emitted, int Total, decimal Percentage) {`
* `public sealed record PreRemediationManifestEntry( string Module, string Table, string TableFile, string Hash);`
* `public sealed record PredicateCoverageEntry( string Module, string Schema, string Table, IReadOnlyList<string> Predicates) {`
* `public sealed record SsdtCoverageSummary( CoverageBreakdown Tables, CoverageBreakdown Columns, CoverageBreakdown Constraints) {`
* `public sealed record SsdtEmissionMetadata(string Algorithm, string Hash);`
* `public sealed record SsdtManifest( IReadOnlyList<TableManifestEntry> Tables, SsdtManifestOptions Options, SsdtPolicySummary? PolicySummary, SsdtEmissionMetadata Emission, IReadOnlyList<PreRemediationManifestEntry> PreRemediation, SsdtCoverageSummary Coverage, SsdtPredicateCoverage PredicateCoverage, IReadOnlyList<string> Unsupported);`
* `public sealed record SsdtManifestOptions( bool IncludePlatformAutoIndexes, bool EmitBareTableOnly, bool SanitizeModuleNames, int ModuleParallelism);`
* `public sealed record SsdtPolicySummary( int ColumnCount, int TightenedColumnCount, int RemediationColumnCount, int UniqueIndexCount, int UniqueIndexesEnforcedCount, int UniqueIndexesRequireRemediationCount, int ForeignKeyCount, int ForeignKeysCreatedCount, IReadOnlyDictionary<string, int> ColumnRationales, IReadOnlyDictionary<string, int> UniqueIndexRationales, IReadOnlyDictionary<string, int> ForeignKeyRationales, IReadOnlyDictionary<string, ModuleDecisionRollup> ModuleRollups, IReadOnlyDictionary<string, ToggleExportValue> TogglePrecedence);`
* `public sealed record SsdtPredicateCoverage( IReadOnlyList<PredicateCoverageEntry> Tables, IReadOnlyDictionary<string, int> PredicateCounts) {`
* `public sealed record TableEmissionPlan( TableArtifactSnapshot Snapshot, string Path, string Script);`
* `public sealed record TableManifestEntry( string Module, string Schema, string Table, string TableFile, IReadOnlyList<string> Indexes, IReadOnlyList<string> ForeignKeys, bool IncludesExtendedProperties);`

</details>

<details>
<summary><code>Osm.Emission.Seeds</code> (4)</summary>

* `public sealed record StaticEntityRow(ImmutableArray<object?> Values) {`
* `public sealed record StaticEntitySeedColumn( string LogicalName, string ColumnName, string EmissionName, string DataType, int? Length, int? Precision, int? Scale, bool IsPrimaryKey, bool IsIdentity) {`
* `public sealed record StaticEntitySeedTableDefinition( string Module, string LogicalName, string Schema, string PhysicalName, string EffectiveName, ImmutableArray<StaticEntitySeedColumn> Columns) {`
* `public sealed record StaticEntityTableData(StaticEntitySeedTableDefinition Definition, ImmutableArray<StaticEntityRow> Rows) {`

</details>

### Osm.Json (records: 56)

<details>
<summary><code>Osm.Json</code> (38)</summary>

* `internal sealed record AttributeCheckConstraintDocument {`
* `internal sealed record AttributeDefaultConstraintDocument {`
* `internal sealed record AttributeDocument {`
* `internal sealed record AttributeMetaDocument {`
* `internal sealed record AttributeOnDiskDocument {`
* `internal sealed record AttributeRealityDocument {`
* `internal sealed record EntityDocument {`
* `internal sealed record EntityMetaDocument {`
* `internal sealed record ExtendedPropertyDocument {`
* `internal sealed record IndexColumnDocument {`
* `internal sealed record IndexDataSpaceDocument {`
* `internal sealed record IndexDocument {`
* `internal sealed record IndexPartitionColumnDocument {`
* `internal sealed record IndexPartitionCompressionDocument {`
* `internal sealed record ModelDocument {`
* `internal sealed record ModuleDocument {`
* `internal sealed record RelationshipConstraintColumnDocument {`
* `internal sealed record RelationshipConstraintDocument {`
* `internal sealed record RelationshipDocument {`
* `internal sealed record SequenceDocument {`
* `internal sealed record TemporalDocument {`
* `internal sealed record TemporalHistoryDocument {`
* `internal sealed record TemporalRetentionDocument {`
* `internal sealed record TriggerDocument {`
* `private sealed record ColumnDocument {`
* `private sealed record ColumnDocument {`
* `private sealed record CompositeUniqueCandidateDocument {`
* `private sealed record CompositeUniqueCandidateDocument {`
* `private sealed record ForeignKeyDocument {`
* `private sealed record ForeignKeyDocument {`
* `private sealed record ForeignKeyReferenceDocument {`
* `private sealed record ForeignKeyReferenceDocument {`
* `private sealed record ProfileSnapshotDocument {`
* `private sealed record ProfileSnapshotDocument {`
* `private sealed record ProfilingProbeStatusDocument {`
* `private sealed record ProfilingProbeStatusDocument {`
* `private sealed record UniqueCandidateDocument {`
* `private sealed record UniqueCandidateDocument {`

</details>

<details>
<summary><code>Osm.Json.Configuration</code> (13)</summary>

* `private sealed record EmissionDocument {`
* `private sealed record EntityOverrideDocument {`
* `private sealed record ForeignKeysDocument {`
* `private sealed record MockingDocument {`
* `private sealed record NamingOverrideRuleDocument {`
* `private sealed record NamingOverridesDocument {`
* `private sealed record PolicyDocument {`
* `private sealed record RemediationDocument {`
* `private sealed record RemediationSentinelsDocument {`
* `private sealed record StaticSeedsDocument {`
* `private sealed record TableOverrideDocument {`
* `private sealed record TighteningOptionsDocument {`
* `private sealed record UniquenessDocument {`

</details>

<details>
<summary><code>Osm.Json.Deserialization</code> (5)</summary>

* `internal readonly record struct DocumentPathContext(string Value) {`
* `internal readonly record struct DuplicateAllowance(bool AllowLogicalNames, bool AllowColumnNames);`
* `internal readonly record struct HelperResult<T>(Result<T> Result, MapContext Context) {`
* `internal readonly record struct MapContext( ModuleName ModuleName, EntityName LogicalName, EntityDocument Document, DocumentPathContext Path, string? SerializedPayload) {`
* `internal sealed record ModelDocumentPipelineResult( bool SchemaIsValid, ImmutableArray<ModuleModel> Modules, ImmutableArray<SequenceModel> Sequences, ImmutableArray<ExtendedProperty> ExtendedProperties);`

</details>

### Osm.Pipeline (records: 185)

<details>
<summary><code>Osm.Pipeline.Application</code> (24)</summary>

* `internal sealed record NamingOverridesRequest(BuildSsdtOverrides Overrides, INamingOverridesBinder Binder);`
* `internal sealed record PipelineRequestContext( CliConfiguration Configuration, string? ConfigPath, TighteningOptions Tightening, ModuleFilterOptions ModuleFilter, ResolvedSqlOptions SqlOptions, TypeMappingPolicy TypeMappingPolicy, SupplementalModelOptions SupplementalModels, NamingOverrideOptions? NamingOverrides, CacheOptionsOverrides CacheOverrides, string? SqlMetadataOutputPath, SqlMetadataLog? SqlMetadataLog, Func<CancellationToken, Task> FlushMetadataAsync) {`
* `internal sealed record PipelineRequestContextBuilderRequest( CliConfigurationContext ConfigurationContext, ModuleFilterOverrides? ModuleFilterOverrides, SqlOptionsOverrides? SqlOptionsOverrides, CacheOptionsOverrides? CacheOptionsOverrides, string? SqlMetadataOutputPath, NamingOverridesRequest? NamingOverrides);`
* `public sealed record AnalyzeApplicationInput( CliConfigurationContext ConfigurationContext, AnalyzeOverrides Overrides);`
* `public sealed record AnalyzeApplicationResult( TighteningAnalysisPipelineResult PipelineResult, string OutputDirectory, string ModelPath, string ProfilePath);`
* `public sealed record AnalyzeOverrides( string? ModelPath, string? ProfilePath, string? OutputDirectory);`
* `public sealed record BuildSsdtApplicationInput( CliConfigurationContext ConfigurationContext, BuildSsdtOverrides Overrides, ModuleFilterOverrides ModuleFilter, SqlOptionsOverrides Sql, CacheOptionsOverrides Cache);`
* `public sealed record BuildSsdtApplicationResult( BuildSsdtPipelineResult PipelineResult, string ProfilerProvider, string? ProfilePath, string OutputDirectory, string ModelPath, bool ModelWasExtracted, ImmutableArray<string> ModelExtractionWarnings);`
* `public sealed record BuildSsdtOverrides( string? ModelPath, string? ProfilePath, string? OutputDirectory, string? ProfilerProvider, string? StaticDataPath, string? RenameOverrides, int? MaxDegreeOfParallelism, string? SqlMetadataOutputPath);`
* `public sealed record BuildSsdtRequestAssemblerContext( CliConfiguration Configuration, BuildSsdtOverrides Overrides, ModuleFilterOptions ModuleFilter, ResolvedSqlOptions SqlOptions, TighteningOptions TighteningOptions, TypeMappingPolicy TypeMappingPolicy, SmoBuildOptions SmoOptions, string ModelPath, string OutputDirectory, IStaticEntityDataProvider? StaticDataProvider, CacheOptionsOverrides CacheOverrides, string? ConfigPath, SqlMetadataLog? SqlMetadataLog);`
* `public sealed record BuildSsdtRequestAssembly( BuildSsdtPipelineRequest Request, string ProfilerProvider, string? ProfilePath, string OutputDirectory);`
* `public sealed record CacheOptionsOverrides(string? Root, bool? Refresh);`
* `public sealed record CaptureProfileApplicationInput( CliConfigurationContext ConfigurationContext, CaptureProfileOverrides Overrides, ModuleFilterOverrides ModuleFilter, SqlOptionsOverrides Sql);`
* `public sealed record CaptureProfileApplicationResult( CaptureProfilePipelineResult PipelineResult, string OutputDirectory, string ModelPath, string ProfilerProvider, string? FixtureProfilePath);`
* `public sealed record CaptureProfileOverrides( string? ModelPath, string? OutputDirectory, string? ProfilerProvider, string? ProfilePath, string? SqlMetadataOutputPath);`
* `public sealed record CompareWithDmmApplicationInput( CliConfigurationContext ConfigurationContext, CompareWithDmmOverrides Overrides, ModuleFilterOverrides ModuleFilter, SqlOptionsOverrides Sql, CacheOptionsOverrides Cache);`
* `public sealed record CompareWithDmmApplicationResult( DmmComparePipelineResult PipelineResult, string DiffOutputPath);`
* `public sealed record CompareWithDmmOverrides( string? ModelPath, string? ProfilePath, string? DmmPath, string? OutputDirectory, int? MaxDegreeOfParallelism);`
* `public sealed record ExtractModelApplicationInput( CliConfigurationContext ConfigurationContext, ExtractModelOverrides Overrides, SqlOptionsOverrides Sql);`
* `public sealed record ExtractModelApplicationResult( ModelExtractionResult ExtractionResult, string OutputPath);`
* `public sealed record ExtractModelOverrides( IReadOnlyList<string>? Modules, bool? IncludeSystemModules, bool? OnlyActiveAttributes, string? OutputPath, string? MockAdvancedSqlManifest, string? SqlMetadataOutputPath);`
* `public sealed record ModelResolutionResult( string ModelPath, bool WasExtracted, ImmutableArray<string> Warnings);`
* `public sealed record ModuleFilterOverrides( IReadOnlyList<string> Modules, bool? IncludeSystemModules, bool? IncludeInactiveModules, IReadOnlyList<string> AllowMissingPrimaryKey, IReadOnlyList<string> AllowMissingSchema);`
* `public sealed record SqlOptionsOverrides( string? ConnectionString, int? CommandTimeoutSeconds, long? SamplingThreshold, int? SamplingSize, SqlAuthenticationMethod? AuthenticationMethod, bool? TrustServerCertificate, string? ApplicationName, string? AccessToken);`

</details>

<details>
<summary><code>Osm.Pipeline.Configuration</code> (13)</summary>

* `internal readonly record struct ModuleFilterSectionReadResult(string? ModelPath, ModuleFilterConfiguration ModuleFilter, bool HasValue) {`
* `internal readonly record struct TighteningSectionReadResult(bool IsLegacyDocument, TighteningOptions? Options);`
* `public sealed record CacheConfiguration(string? Root, bool? Refresh, int? TimeToLiveSeconds) {`
* `public sealed record CliConfiguration( TighteningOptions Tightening, string? ModelPath, string? ProfilePath, string? DmmPath, CacheConfiguration Cache, ProfilerConfiguration Profiler, SqlConfiguration Sql, ModuleFilterConfiguration ModuleFilter, TypeMappingConfiguration TypeMapping, SupplementalModelConfiguration SupplementalModels) {`
* `public sealed record CliConfigurationContext(CliConfiguration Configuration, string? ConfigPath);`
* `public sealed record MetadataContractConfiguration( IReadOnlyDictionary<string, IReadOnlyList<string>> OptionalColumns) {`
* `public sealed record ModuleFilterConfiguration( IReadOnlyList<string> Modules, bool? IncludeSystemModules, bool? IncludeInactiveModules, IReadOnlyDictionary<string, IReadOnlyList<string>> EntityFilters, IReadOnlyDictionary<string, ModuleValidationOverrideConfiguration> ValidationOverrides) {`
* `public sealed record ProfilerConfiguration(string? Provider, string? ProfilePath, string? MockFolder) {`
* `public sealed record SqlAuthenticationConfiguration( SqlAuthenticationMethod? Method, bool? TrustServerCertificate, string? ApplicationName, string? AccessToken) {`
* `public sealed record SqlConfiguration( string? ConnectionString, int? CommandTimeoutSeconds, SqlSamplingConfiguration Sampling, SqlAuthenticationConfiguration Authentication, MetadataContractConfiguration MetadataContract) {`
* `public sealed record SqlSamplingConfiguration(long? RowSamplingThreshold, int? SampleSize) {`
* `public sealed record SupplementalModelConfiguration( bool? IncludeUsers, IReadOnlyList<string> Paths) {`
* `public sealed record TypeMappingConfiguration( string? Path, TypeMappingRuleDefinition? Default, IReadOnlyDictionary<string, TypeMappingRuleDefinition> Overrides) {`

</details>

<details>
<summary><code>Osm.Pipeline.Evidence</code> (12)</summary>

* `internal abstract record CacheEvaluationResult {`
* `internal sealed record CacheRequestContext( string NormalizedRootDirectory, string CacheDirectory, string Command, string Key, IReadOnlyDictionary<string, string?> Metadata, IReadOnlyCollection<EvidenceArtifactDescriptor> Descriptors, EvidenceCacheModuleSelection ModuleSelection, bool Refresh);`
* `internal sealed record EvidenceArtifactDescriptor( EvidenceArtifactType Type, string SourcePath, string Hash, long Length, string Extension);`
* `internal sealed record ManifestEvaluation( EvidenceCacheOutcome Outcome, EvidenceCacheInvalidationReason Reason, EvidenceCacheManifest? Manifest, IReadOnlyDictionary<string, string?> Metadata);`
* `public sealed record EvidenceCacheArtifact( EvidenceArtifactType Type, string OriginalPath, string RelativePath, string Hash, long Length);`
* `public sealed record EvidenceCacheEvaluation( EvidenceCacheOutcome Outcome, EvidenceCacheInvalidationReason Reason, DateTimeOffset EvaluatedAtUtc, IReadOnlyDictionary<string, string?> Metadata);`
* `public sealed record EvidenceCacheManifest( string Version, string Key, string Command, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastValidatedAtUtc, DateTimeOffset? ExpiresAtUtc, EvidenceCacheModuleSelection? ModuleSelection, IReadOnlyDictionary<string, string?> Metadata, IReadOnlyList<EvidenceCacheArtifact> Artifacts);`
* `public sealed record EvidenceCacheModuleSelection( bool IncludeSystemModules, bool IncludeInactiveModules, int ModuleCount, string? ModulesHash, IReadOnlyList<string> Modules) {`
* `public sealed record EvidenceCacheRequest( string RootDirectory, string Command, string? ModelPath, string? ProfilePath, string? DmmPath, string? ConfigPath, IReadOnlyDictionary<string, string?> Metadata, bool Refresh);`
* `public sealed record EvidenceCacheResult( string CacheDirectory, EvidenceCacheManifest Manifest, EvidenceCacheEvaluation Evaluation);`
* `public sealed record Invalidate( EvidenceCacheInvalidationReason Reason, IReadOnlyDictionary<string, string?> Metadata) : CacheEvaluationResult;`
* `public sealed record Reuse(EvidenceCacheResult Result) : CacheEvaluationResult;`

</details>

<details>
<summary><code>Osm.Pipeline.ModelIngestion</code> (1)</summary>

* `public sealed record ModelIngestionOptions( ModuleValidationOverrides ValidationOverrides, string? MissingSchemaFallback) {`

</details>

<details>
<summary><code>Osm.Pipeline.Orchestration</code> (75)</summary>

* `private sealed record CheckConstraintDocument(string? Name, string Expression, bool IsNotTrusted);`
* `private sealed record ColumnDocument( string Name, string LogicalName, DataTypeDocument DataType, bool Nullable, bool IsIdentity, int IdentitySeed, int IdentityIncrement, bool IsComputed, string? ComputedExpression, string? DefaultExpression, string? Collation, string? Description, DefaultConstraintDocument? DefaultConstraint, IReadOnlyList<CheckConstraintDocument> CheckConstraints, int Ordinal);`
* `private sealed record DataTypeDocument( string Name, string SqlDataType, int MaximumLength, int NumericPrecision, int NumericScale);`
* `private sealed record DecisionDocument( IReadOnlyList<NullabilityDecisionDocument> Nullability, IReadOnlyList<ForeignKeyDecisionDocument> ForeignKeys, IReadOnlyList<UniqueIndexDecisionDocument> UniqueIndexes, IReadOnlyList<DiagnosticDocument> Diagnostics);`
* `private sealed record DecisionLogPersistenceResult( string Path, IReadOnlyDictionary<string, string?> Metadata);`
* `private sealed record DefaultConstraintDocument(string? Name, string Expression, bool IsNotTrusted);`
* `private sealed record DiagnosticDocument( string Code, string Message, string Severity, string LogicalName, string CanonicalModule, string CanonicalSchema, string CanonicalPhysicalName, IReadOnlyList<DuplicateCandidateDocument> Candidates, bool ResolvedByOverride);`
* `private sealed record DmmDiffLog( bool IsMatch, string ModelPath, string ProfilePath, string DmmPath, DateTimeOffset GeneratedAtUtc, IReadOnlyList<DmmDifference> ModelDifferences, IReadOnlyList<DmmDifference> SsdtDifferences);`
* `private sealed record DomainEntitySnapshot(EntityEmissionSnapshot Snapshot) {`
* `private sealed record DuplicateCandidateDocument(string Module, string Schema, string PhysicalName);`
* `private sealed record EmissionFingerprintDocument( EmissionFingerprintOptions Options, IReadOnlyList<TableDocument> Tables, DecisionDocument Decisions);`
* `private sealed record EmissionFingerprintOptions( bool IncludePlatformAutoIndexes, bool EmitBareTableOnly, bool SanitizeModuleNames, int ModuleParallelism, string DefaultCatalog);`
* `private sealed record ForeignKeyDecisionDocument( string Schema, string Table, string Column, bool CreateConstraint, IReadOnlyList<string> Rationales);`
* `private sealed record ForeignKeyDocument( string Name, IReadOnlyList<string> Columns, string ReferencedModule, string ReferencedTable, string ReferencedSchema, IReadOnlyList<string> ReferencedColumns, string ReferencedLogicalTable, string DeleteAction, bool IsNoCheck);`
* `private sealed record IndexAnalysisResult(bool Succeeded, ImmutableArray<AttributeModel> ReferencedAttributes, string Reason) {`
* `private sealed record IndexColumnDocument(string Name, int Ordinal, bool IsIncluded, bool IsDescending);`
* `private sealed record IndexCompressionDocument(int PartitionNumber, string Compression);`
* `private sealed record IndexDataSpaceDocument(string Name, string Type);`
* `private sealed record IndexDocument( string Name, bool IsUnique, bool IsPrimaryKey, bool IsPlatformAuto, IReadOnlyList<IndexColumnDocument> Columns, IndexMetadataDocument Metadata);`
* `private sealed record IndexMetadataDocument( bool IsDisabled, bool IsPadded, int? FillFactor, bool IgnoreDuplicateKey, bool AllowRowLocks, bool AllowPageLocks, bool StatisticsNoRecompute, string? FilterDefinition, IndexDataSpaceDocument? DataSpace, IReadOnlyList<IndexPartitionColumnDocument> PartitionColumns, IReadOnlyList<IndexCompressionDocument> Compression);`
* `private sealed record IndexPartitionColumnDocument(string Name, int Ordinal);`
* `private sealed record NullabilityDecisionDocument( string Schema, string Table, string Column, bool MakeNotNull, bool RequiresRemediation, IReadOnlyList<string> Rationales);`
* `private sealed record OpportunityPersistenceResult( OpportunityArtifacts Artifacts, IReadOnlyDictionary<string, string?> Metadata);`
* `private sealed record PolicyDecisionLog( int ColumnCount, int TightenedColumnCount, int RemediationColumnCount, int UniqueIndexCount, int UniqueIndexesEnforcedCount, int UniqueIndexesRequireRemediationCount, int ForeignKeyCount, int ForeignKeysCreatedCount, IReadOnlyDictionary<string, int> ColumnRationales, IReadOnlyDictionary<string, int> UniqueIndexRationales, IReadOnlyDictionary<string, int> ForeignKeyRationales, IReadOnlyDictionary<string, ModuleDecisionRollup> ModuleRollups, IReadOnlyDictionary<string, ToggleExportValue> TogglePrecedence, IReadOnlyList<PolicyDecisionLogColumn> Columns, IReadOnlyList<PolicyDecisionLogUniqueIndex> UniqueIndexes, IReadOnlyList<PolicyDecisionLogForeignKey> ForeignKeys, IReadOnlyList<PolicyDecisionLogDiagnostic> Diagnostics, SsdtPredicateCoverage PredicateCoverage);`
* `private sealed record PolicyDecisionLogColumn( string Schema, string Table, string Column, bool MakeNotNull, bool RequiresRemediation, IReadOnlyList<string> Rationales, string Module);`
* `private sealed record PolicyDecisionLogDiagnostic( string LogicalName, string CanonicalModule, string CanonicalSchema, string CanonicalPhysicalName, string Code, string Message, string Severity, bool ResolvedByOverride, IReadOnlyList<PolicyDecisionLogDuplicateCandidate> Candidates);`
* `private sealed record PolicyDecisionLogDuplicateCandidate(string Module, string Schema, string PhysicalName);`
* `private sealed record PolicyDecisionLogForeignKey( string Schema, string Table, string Column, bool CreateConstraint, IReadOnlyList<string> Rationales, string Module);`
* `private sealed record PolicyDecisionLogUniqueIndex( string Schema, string Table, string Index, bool EnforceUnique, bool RequiresRemediation, IReadOnlyList<string> Rationales, string Module);`
* `private sealed record PredicateSnapshot(string Module, string Schema, string Table, ImmutableArray<string> Predicates);`
* `private sealed record SmoModelCreationResult( SmoModel Model, IReadOnlyDictionary<string, string?> Metadata);`
* `private sealed record SsdtEmissionResult( SsdtManifest Manifest, EmissionCoverageResult Coverage, IReadOnlyDictionary<string, string?> Metadata);`
* `private sealed record TableDocument( string Module, string OriginalModule, string Name, string Schema, string Catalog, string LogicalName, string? Description, IReadOnlyList<ColumnDocument> Columns, IReadOnlyList<IndexDocument> Indexes, IReadOnlyList<ForeignKeyDocument> ForeignKeys, IReadOnlyList<TriggerDocument> Triggers);`
* `private sealed record TriggerDocument( string Name, string Schema, string Table, bool IsDisabled, string Definition);`
* `private sealed record UniqueIndexDecisionDocument( string Schema, string Table, string Index, bool EnforceUnique, bool RequiresRemediation, IReadOnlyList<string> Rationales);`
* `public record BootstrapCompleted( BuildSsdtPipelineRequest Request, PipelineExecutionLogBuilder Log, PipelineBootstrapContext Bootstrap) : PipelineInitialized(Request, Log);`
* `public record DecisionsSynthesized( BuildSsdtPipelineRequest Request, PipelineExecutionLogBuilder Log, PipelineBootstrapContext Bootstrap, EvidenceCacheResult? EvidenceCache, PolicyDecisionSet Decisions, PolicyDecisionReport Report, OpportunitiesReport Opportunities, ImmutableArray<PipelineInsight> Insights) : EvidencePrepared(Request, Log, Bootstrap, EvidenceCache);`
* `public record EmissionReady( BuildSsdtPipelineRequest Request, PipelineExecutionLogBuilder Log, PipelineBootstrapContext Bootstrap, EvidenceCacheResult? EvidenceCache, PolicyDecisionSet Decisions, PolicyDecisionReport Report, OpportunitiesReport Opportunities, ImmutableArray<PipelineInsight> Insights, SsdtManifest Manifest, string DecisionLogPath, OpportunityArtifacts OpportunityArtifacts) : DecisionsSynthesized(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Insights);`
* `public record EvidencePrepared( BuildSsdtPipelineRequest Request, PipelineExecutionLogBuilder Log, PipelineBootstrapContext Bootstrap, EvidenceCacheResult? EvidenceCache) : BootstrapCompleted(Request, Log, Bootstrap);`
* `public record PipelineInitialized( BuildSsdtPipelineRequest Request, PipelineExecutionLogBuilder Log);`
* `public record SqlValidated( BuildSsdtPipelineRequest Request, PipelineExecutionLogBuilder Log, PipelineBootstrapContext Bootstrap, EvidenceCacheResult? EvidenceCache, PolicyDecisionSet Decisions, PolicyDecisionReport Report, OpportunitiesReport Opportunities, ImmutableArray<PipelineInsight> Insights, SsdtManifest Manifest, string DecisionLogPath, OpportunityArtifacts OpportunityArtifacts, SsdtSqlValidationSummary SqlValidation) : EmissionReady(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Insights, Manifest, DecisionLogPath, OpportunityArtifacts);`
* `public record StaticSeedsGenerated( BuildSsdtPipelineRequest Request, PipelineExecutionLogBuilder Log, PipelineBootstrapContext Bootstrap, EvidenceCacheResult? EvidenceCache, PolicyDecisionSet Decisions, PolicyDecisionReport Report, OpportunitiesReport Opportunities, ImmutableArray<PipelineInsight> Insights, SsdtManifest Manifest, string DecisionLogPath, OpportunityArtifacts OpportunityArtifacts, SsdtSqlValidationSummary SqlValidation, ImmutableArray<string> StaticSeedScriptPaths) : SqlValidated(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Insights, Manifest, DecisionLogPath, OpportunityArtifacts, SqlValidation);`
* `public record TelemetryPackaged( BuildSsdtPipelineRequest Request, PipelineExecutionLogBuilder Log, PipelineBootstrapContext Bootstrap, EvidenceCacheResult? EvidenceCache, PolicyDecisionSet Decisions, PolicyDecisionReport Report, OpportunitiesReport Opportunities, ImmutableArray<PipelineInsight> Insights, SsdtManifest Manifest, string DecisionLogPath, OpportunityArtifacts OpportunityArtifacts, SsdtSqlValidationSummary SqlValidation, ImmutableArray<string> StaticSeedScriptPaths, ImmutableArray<string> TelemetryPackagePaths) : StaticSeedsGenerated(Request, Log, Bootstrap, EvidenceCache, Decisions, Report, Opportunities, Insights, Manifest, DecisionLogPath, OpportunityArtifacts, SqlValidation, StaticSeedScriptPaths);`
* `public sealed record BuildSsdtPipelineRequest( ModelExecutionScope Scope, string OutputDirectory, string ProfilerProvider, EvidenceCachePipelineOptions? EvidenceCache, IStaticEntityDataProvider? StaticDataProvider, string? SeedOutputDirectoryHint, SqlMetadataLog? SqlMetadataLog = null) : ICommand<BuildSsdtPipelineResult>;`
* `public sealed record BuildSsdtPipelineResult( ProfileSnapshot Profile, ImmutableArray<ProfilingInsight> ProfilingInsights, PolicyDecisionReport DecisionReport, OpportunitiesReport Opportunities, SsdtManifest Manifest, ImmutableDictionary<string, ModuleManifestRollup> ModuleManifestRollups, ImmutableArray<PipelineInsight> PipelineInsights, string DecisionLogPath, string OpportunitiesPath, string SafeScriptPath, string SafeScript, string RemediationScriptPath, string RemediationScript, ImmutableArray<string> StaticSeedScriptPaths, ImmutableArray<string> TelemetryPackagePaths, SsdtSqlValidationSummary SqlValidation, EvidenceCacheResult? EvidenceCache, PipelineExecutionLog ExecutionLog, ImmutableArray<string> Warnings);`
* `public sealed record CaptureProfileInsight( string Severity, string Category, string Message, CaptureProfileInsightCoordinate? Coordinate);`
* `public sealed record CaptureProfileInsightCoordinate( string Schema, string Table, string? Column, string? RelatedSchema, string? RelatedTable, string? RelatedColumn);`
* `public sealed record CaptureProfileManifest( string ModelPath, string ProfilePath, string ProfilerProvider, CaptureProfileModuleSummary ModuleFilter, CaptureProfileSupplementalSummary SupplementalModels, CaptureProfileSnapshotSummary Snapshot, IReadOnlyList<CaptureProfileInsight> Insights, IReadOnlyList<string> Warnings, DateTimeOffset CapturedAtUtc);`
* `public sealed record CaptureProfileModuleSummary( bool HasFilter, IReadOnlyList<string> Modules, bool IncludeSystemModules, bool IncludeInactiveModules);`
* `public sealed record CaptureProfilePipelineRequest( ModelExecutionScope Scope, string ProfilerProvider, string OutputDirectory, string? FixtureProfilePath, SqlMetadataLog? SqlMetadataLog = null) : ICommand<CaptureProfilePipelineResult>;`
* `public sealed record CaptureProfilePipelineResult( ProfileSnapshot Profile, CaptureProfileManifest Manifest, string ProfilePath, string ManifestPath, ImmutableArray<ProfilingInsight> Insights, PipelineExecutionLog ExecutionLog, ImmutableArray<string> Warnings);`
* `public sealed record CaptureProfileSnapshotSummary( int ColumnCount, int UniqueCandidateCount, int CompositeUniqueCandidateCount, int ForeignKeyCount, int ModuleCount);`
* `public sealed record CaptureProfileSupplementalSummary( bool IncludeUsers, IReadOnlyList<string> Paths);`
* `public sealed record DmmComparePipelineRequest( ModelExecutionScope Scope, string DmmPath, string DiffOutputPath, EvidenceCachePipelineOptions? EvidenceCache) : ICommand<DmmComparePipelineResult>;`
* `public sealed record DmmComparePipelineResult( ProfileSnapshot Profile, DmmComparisonResult Comparison, string DiffArtifactPath, EvidenceCacheResult? EvidenceCache, PipelineExecutionLog ExecutionLog, ImmutableArray<string> Warnings);`
* `public sealed record EmissionCoverageResult( SsdtCoverageSummary Summary, ImmutableArray<string> Unsupported, SsdtPredicateCoverage PredicateCoverage);`
* `public sealed record EvidenceCachePipelineOptions( string? RootDirectory, bool Refresh, string Command, string ModelPath, string? ProfilePath, string? DmmPath, string? ConfigPath, IReadOnlyDictionary<string, string?>? Metadata);`
* `public sealed record ExtractModelPipelineRequest( ModelExtractionCommand Command, ResolvedSqlOptions SqlOptions, string? AdvancedSqlFixtureManifestPath, string? OutputPath, string? SqlMetadataOutputPath, SqlMetadataLog? SqlMetadataLog = null) : ICommand<ModelExtractionResult>;`
* `public sealed record ModelExecutionScope( string ModelPath, ModuleFilterOptions ModuleFilter, SupplementalModelOptions SupplementalModels, TighteningOptions TighteningOptions, ResolvedSqlOptions SqlOptions, SmoBuildOptions SmoOptions, TypeMappingPolicy TypeMappingPolicy, string? ProfilePath = null, string? BaselineProfilePath = null);`
* `public sealed record ModuleManifestRollup(int TableCount, int IndexCount, int ForeignKeyCount) {`
* `public sealed record OpportunityArtifacts( string ReportPath, string SafeScriptPath, string SafeScript, string RemediationScriptPath, string RemediationScript);`
* `public sealed record PipelineBootstrapContext( OsmModel FilteredModel, ImmutableArray<EntityModel> SupplementalEntities, ProfileSnapshot Profile, ImmutableArray<ProfilingInsight> Insights, ImmutableArray<string> Warnings);`
* `public sealed record PipelineBootstrapRequest( string ModelPath, ModuleFilterOptions ModuleFilter, SupplementalModelOptions SupplementalModels, PipelineBootstrapTelemetry Telemetry, Func<OsmModel, CancellationToken, Task<Result<ProfileCaptureResult>>> ProfileCaptureAsync);`
* `public sealed record PipelineBootstrapTelemetry( string RequestMessage, IReadOnlyDictionary<string, string?> RequestMetadata, string ProfilingStartMessage, IReadOnlyDictionary<string, string?> ProfilingStartMetadata, string ProfilingCompletedMessage);`
* `public sealed record PipelineInsight {`
* `public sealed record PipelineLogEntry( DateTimeOffset TimestampUtc, string Step, string Message, IReadOnlyDictionary<string, string?> Metadata);`
* `public sealed record ResolvedSqlOptions( string? ConnectionString, int? CommandTimeoutSeconds, SqlSamplingSettings Sampling, SqlAuthenticationSettings Authentication, MetadataContractOverrides MetadataContract);`
* `public sealed record SqlAuthenticationSettings( SqlAuthenticationMethod? Method, bool? TrustServerCertificate, string? ApplicationName, string? AccessToken);`
* `public sealed record SqlSamplingSettings(long? RowSamplingThreshold, int? SampleSize);`
* `public sealed record SsdtSqlValidationError( int Number, int State, int Severity, int Line, int Column, string Message) {`
* `public sealed record SsdtSqlValidationIssue( string Path, ImmutableArray<SsdtSqlValidationError> Errors) {`
* `public sealed record SsdtSqlValidationSummary( int TotalFiles, int FilesWithErrors, int ErrorCount, ImmutableArray<SsdtSqlValidationIssue> Issues) {`
* `public sealed record SupplementalModelOptions(bool IncludeUsers, IReadOnlyList<string> Paths) {`
* `public sealed record TighteningAnalysisPipelineRequest( ModelExecutionScope Scope, string OutputDirectory) : ICommand<TighteningAnalysisPipelineResult>;`
* `public sealed record TighteningAnalysisPipelineResult( PolicyDecisionReport Report, ProfileSnapshot Profile, ImmutableArray<string> SummaryLines, string SummaryPath, string DecisionLogPath, PipelineExecutionLog ExecutionLog, ImmutableArray<string> Warnings);`

</details>

<details>
<summary><code>Osm.Pipeline.Profiling</code> (6)</summary>

* `internal readonly record struct ProfilingProbeResult<T>(T Value, ProfilingProbeStatus Status);`
* `internal sealed record ColumnMetadata(bool IsNullable, bool IsComputed, bool IsPrimaryKey, string? DefaultDefinition);`
* `internal sealed record ForeignKeyPlan(string Key, string Column, string TargetSchema, string TargetTable, string TargetColumn);`
* `internal sealed record TableProfilingPlan( string Schema, string Table, long RowCount, ImmutableArray<string> Columns, ImmutableArray<UniqueCandidatePlan> UniqueCandidates, ImmutableArray<ForeignKeyPlan> ForeignKeys);`
* `internal sealed record TableProfilingResults( IReadOnlyDictionary<string, long> NullCounts, IReadOnlyDictionary<string, ProfilingProbeStatus> NullCountStatuses, IReadOnlyDictionary<string, bool> UniqueDuplicates, IReadOnlyDictionary<string, ProfilingProbeStatus> UniqueDuplicateStatuses, IReadOnlyDictionary<string, bool> ForeignKeys, IReadOnlyDictionary<string, ProfilingProbeStatus> ForeignKeyStatuses, IReadOnlyDictionary<string, bool> ForeignKeyIsNoCheck, IReadOnlyDictionary<string, ProfilingProbeStatus> ForeignKeyNoCheckStatuses) {`
* `internal sealed record UniqueCandidatePlan(string Key, ImmutableArray<string> Columns);`

</details>

<details>
<summary><code>Osm.Pipeline.Runtime</code> (2)</summary>

* `public sealed record PipelineArtifact(string Name, string Path, string? ContentType = null);`
* `public sealed record PipelineRun<TResult>( string Verb, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, Result<TResult> Outcome, IReadOnlyList<PipelineArtifact> Artifacts, IReadOnlyDictionary<string, string?> Metadata) : IPipelineRun {`

</details>

<details>
<summary><code>Osm.Pipeline.Runtime.Verbs</code> (10)</summary>

* `public sealed record AnalyzeVerbOptions {`
* `public sealed record AnalyzeVerbResult( CliConfigurationContext Configuration, AnalyzeApplicationResult ApplicationResult);`
* `public sealed record BuildSsdtVerbOptions {`
* `public sealed record BuildSsdtVerbResult( CliConfigurationContext Configuration, BuildSsdtApplicationResult ApplicationResult);`
* `public sealed record DmmCompareVerbOptions {`
* `public sealed record DmmCompareVerbResult( CliConfigurationContext Configuration, CompareWithDmmApplicationResult ApplicationResult);`
* `public sealed record ExtractModelVerbOptions {`
* `public sealed record ExtractModelVerbResult( CliConfigurationContext Configuration, ExtractModelApplicationResult ApplicationResult);`
* `public sealed record ProfileVerbOptions {`
* `public sealed record ProfileVerbResult( CliConfigurationContext Configuration, CaptureProfileApplicationResult ApplicationResult);`

</details>

<details>
<summary><code>Osm.Pipeline.Sql</code> (6)</summary>

* `internal sealed record SqlMetadataLogState( OutsystemsMetadataSnapshot? Snapshot, DateTimeOffset? ExportedAtUtc, string? DatabaseName, IReadOnlyList<ValidationError> Errors, MetadataRowSnapshot? FailureRowSnapshot, IReadOnlyList<SqlRequestLogEntry> Requests) {`
* `internal sealed record SqlRequestLogEntry(string Name, object? Payload);`
* `public sealed record SqlConnectionOptions( SqlAuthenticationMethod? AuthenticationMethod, bool? TrustServerCertificate, string? ApplicationName, string? AccessToken) {`
* `public sealed record SqlProfilerLimits {`
* `public sealed record SqlProfilerOptions {`
* `public sealed record SqlSamplingOptions(long RowCountSamplingThreshold, int SampleSize) {`

</details>

<details>
<summary><code>Osm.Pipeline.SqlExtraction</code> (29)</summary>

* `private sealed record FixtureEntry(string JsonPath, ImmutableArray<ModuleName> Modules);`
* `private sealed record FixtureEntry(string JsonPath, ImmutableArray<ModuleName> Modules);`
* `public sealed record AdvancedSqlMetadataResult( OutsystemsMetadataSnapshot Snapshot, IReadOnlyList<string> ModulesWithoutEntities, DateTimeOffset ExportedAtUtc, TimeSpan MetadataDuration);`
* `public sealed record ModelDeserializerOutcome( OsmModel Model, IReadOnlyList<string> Warnings, TimeSpan Duration);`
* `public sealed record OutsystemsAttributeHasFkRow(int AttrId, bool HasFk);`
* `public sealed record OutsystemsAttributeJsonRow(int EntityId, string? AttributesJson);`
* `public sealed record OutsystemsAttributeRow( int AttrId, int EntityId, string AttrName, Guid? AttrSsKey, string? DataType, int? Length, int? Precision, int? Scale, string? DefaultValue, bool IsMandatory, bool AttrIsActive, bool? IsAutoNumber, bool? IsIdentifier, int? RefEntityId, string? OriginalName, string? ExternalColumnType, string? DeleteRule, string? PhysicalColumnName, string? DatabaseColumnName, string? LegacyType, int? Decimals, string? OriginalType, string? AttrDescription);`
* `public sealed record OutsystemsColumnCheckJsonRow(int AttrId, string CheckJson);`
* `public sealed record OutsystemsColumnCheckRow(int AttrId, string ConstraintName, string Definition, bool IsNotTrusted);`
* `public sealed record OutsystemsColumnRealityRow( int AttrId, bool IsNullable, string SqlType, int? MaxLength, int? Precision, int? Scale, string? CollationName, bool IsIdentity, bool IsComputed, string? ComputedDefinition, string? DefaultConstraintName, string? DefaultDefinition, string PhysicalColumn);`
* `public sealed record OutsystemsEntityRow( int EntityId, string EntityName, string PhysicalTableName, int EspaceId, bool EntityIsActive, bool IsSystemEntity, bool IsExternalEntity, string? DataKind, Guid? PrimaryKeySsKey, Guid? EntitySsKey, string? EntityDescription);`
* `public sealed record OutsystemsForeignKeyAttrMapRow(int AttrId, int FkObjectId);`
* `public sealed record OutsystemsForeignKeyAttributeJsonRow(int AttrId, string ConstraintJson);`
* `public sealed record OutsystemsForeignKeyColumnRow( int EntityId, int FkObjectId, int Ordinal, string ParentColumn, string ReferencedColumn, int? ParentAttrId, string? ParentAttrName, int? ReferencedAttrId, string? ReferencedAttrName);`
* `public sealed record OutsystemsForeignKeyColumnsJsonRow(int FkObjectId, string ColumnsJson);`
* `public sealed record OutsystemsForeignKeyRow( int EntityId, int FkObjectId, string FkName, string DeleteAction, string UpdateAction, int ReferencedObjectId, int? ReferencedEntityId, string ReferencedSchema, string ReferencedTable, bool IsNoCheck);`
* `public sealed record OutsystemsIndexColumnRow( int EntityId, string IndexName, int Ordinal, string PhysicalColumn, bool IsIncluded, string? Direction, string? HumanAttr);`
* `public sealed record OutsystemsIndexJsonRow(int EntityId, string IndexesJson);`
* `public sealed record OutsystemsIndexRow( int EntityId, int ObjectId, int IndexId, string IndexName, bool IsUnique, bool IsPrimary, string Kind, string? FilterDefinition, bool IsDisabled, bool IsPadded, int? FillFactor, bool IgnoreDupKey, bool AllowRowLocks, bool AllowPageLocks, bool NoRecompute, string? DataSpaceName, string? DataSpaceType, string? PartitionColumnsJson, string? DataCompressionJson);`
* `public sealed record OutsystemsMetadataSnapshot( IReadOnlyList<OutsystemsModuleRow> Modules, IReadOnlyList<OutsystemsEntityRow> Entities, IReadOnlyList<OutsystemsAttributeRow> Attributes, IReadOnlyList<OutsystemsReferenceRow> References, IReadOnlyList<OutsystemsPhysicalTableRow> PhysicalTables, IReadOnlyList<OutsystemsColumnRealityRow> ColumnReality, IReadOnlyList<OutsystemsColumnCheckRow> ColumnChecks, IReadOnlyList<OutsystemsColumnCheckJsonRow> ColumnCheckJson, IReadOnlyList<OutsystemsPhysicalColumnPresenceRow> PhysicalColumnsPresent, IReadOnlyList<OutsystemsIndexRow> Indexes, IReadOnlyList<OutsystemsIndexColumnRow> IndexColumns, IReadOnlyList<OutsystemsForeignKeyRow> ForeignKeys, IReadOnlyList<OutsystemsForeignKeyColumnRow> ForeignKeyColumns, IReadOnlyList<OutsystemsForeignKeyAttrMapRow> ForeignKeyAttributeMap, IReadOnlyList<OutsystemsAttributeHasFkRow> AttributeForeignKeys, IReadOnlyList<OutsystemsForeignKeyColumnsJsonRow> ForeignKeyColumnsJson, IReadOnlyList<OutsystemsForeignKeyAttributeJsonRow> ForeignKeyAttributeJson, IReadOnlyList<OutsystemsTriggerRow> Triggers, IReadOnlyList<OutsystemsAttributeJsonRow> AttributeJson, IReadOnlyList<OutsystemsRelationshipJsonRow> RelationshipJson, IReadOnlyList<OutsystemsIndexJsonRow> IndexJson, IReadOnlyList<OutsystemsTriggerJsonRow> TriggerJson, IReadOnlyList<OutsystemsModuleJsonRow> ModuleJson, string DatabaseName) {`
* `public sealed record OutsystemsModuleJsonRow(string ModuleName, bool IsSystem, bool IsActive, string ModuleEntitiesJson);`
* `public sealed record OutsystemsModuleRow(int EspaceId, string EspaceName, bool IsSystemModule, bool ModuleIsActive, string? EspaceKind, Guid? EspaceSsKey);`
* `public sealed record OutsystemsPhysicalColumnPresenceRow(int AttrId);`
* `public sealed record OutsystemsPhysicalTableRow(int EntityId, string SchemaName, string TableName, int ObjectId);`
* `public sealed record OutsystemsReferenceRow(int AttrId, int? RefEntityId, string? RefEntityName, string? RefPhysicalName);`
* `public sealed record OutsystemsRelationshipJsonRow(int EntityId, string RelationshipsJson);`
* `public sealed record OutsystemsTriggerJsonRow(int EntityId, string TriggersJson);`
* `public sealed record OutsystemsTriggerRow(int EntityId, string TriggerName, bool IsDisabled, string TriggerDefinition);`
* `public sealed record SqlExecutionOptions(int? CommandTimeoutSeconds, SqlSamplingOptions Sampling) {`

</details>

<details>
<summary><code>Osm.Pipeline.UatUsers</code> (7)</summary>

* `internal sealed record AllowedUserLoadResult( IReadOnlyCollection<long> UserIds, int SqlRowCount, int ListRowCount);`
* `internal sealed record PipelineStepRegistration<TContext>( IPipelineStep<TContext> Step, Func<TContext, bool>? Predicate);`
* `public readonly record struct UserFkColumn( string SchemaName, string TableName, string ColumnName, string ForeignKeyName);`
* `public readonly record struct UserMappingEntry(long SourceUserId, long? TargetUserId, string? Rationale);`
* `public sealed record ForeignKeyColumn(string ParentColumn, string ReferencedColumn);`
* `public sealed record ForeignKeyDefinition( string Name, ForeignKeyTable Parent, ForeignKeyTable Referenced, ImmutableArray<ForeignKeyColumn> Columns);`
* `public sealed record ForeignKeyTable(string Schema, string Table);`

</details>

### Osm.Smo (records: 25)

<details>
<summary><code>Osm.Smo</code> (25)</summary>

* `internal sealed record EntityEmissionContext( string ModuleName, EntityModel Entity, ImmutableArray<AttributeModel> EmittableAttributes, ImmutableArray<AttributeModel> IdentifierAttributes, IReadOnlyDictionary<string, AttributeModel> AttributeLookup, AttributeModel? ActiveIdentifier, AttributeModel? FallbackIdentifier) {`
* `internal sealed record ForeignKeyEvidenceMatch( ImmutableArray<string> OwnerColumns, ImmutableArray<string> ReferencedColumns, ImmutableArray<AttributeModel> OwnerAttributesForNaming, string? ProvidedConstraintName, string ReferencedTable, string ReferencedSchema, ForeignKeyAction DeleteAction, bool IsNoCheck);`
* `internal sealed record TypeMappingPolicyDefinition( TypeMappingRuleDefinition Default, IReadOnlyDictionary<string, TypeMappingRuleDefinition> AttributeMappings, IReadOnlyDictionary<string, TypeMappingRuleDefinition> OnDiskMappings, IReadOnlyDictionary<string, TypeMappingRuleDefinition> ExternalMappings) {`
* `public sealed record IndexNamingOptions( string PrimaryKeyPrefix, string UniqueIndexPrefix, string NonUniqueIndexPrefix, string ForeignKeyPrefix) {`
* `public sealed record PerTableHeaderItem(string Label, string Value) {`
* `public sealed record PerTableHeaderOptions( bool Enabled, string? Source, string? Profile, string? Decisions, string? FingerprintAlgorithm, string? FingerprintHash, ImmutableArray<PerTableHeaderItem> AdditionalItems) {`
* `public sealed record PerTableWriteResult( string EffectiveTableName, string Script, ImmutableArray<string> IndexNames, ImmutableArray<string> ForeignKeyNames, bool IncludesExtendedProperties);`
* `public sealed record SmoBuildOptions( string DefaultCatalogName, bool IncludePlatformAutoIndexes, bool EmitBareTableOnly, bool SanitizeModuleNames, int ModuleParallelism, NamingOverrideOptions NamingOverrides, SmoFormatOptions Format, PerTableHeaderOptions Header) {`
* `public sealed record SmoCheckConstraintDefinition(string? Name, string Expression, bool IsNotTrusted);`
* `public sealed record SmoColumnDefinition( string PhysicalName, string Name, string LogicalName, DataType DataType, bool Nullable, bool IsIdentity, int IdentitySeed, int IdentityIncrement, bool IsComputed, string? ComputedExpression, string? DefaultExpression, string? Collation, string? Description, SmoDefaultConstraintDefinition? DefaultConstraint, ImmutableArray<SmoCheckConstraintDefinition> CheckConstraints);`
* `public sealed record SmoDefaultConstraintDefinition(string? Name, string Expression, bool IsNotTrusted);`
* `public sealed record SmoForeignKeyDefinition( string Name, ImmutableArray<string> Columns, string ReferencedModule, string ReferencedTable, string ReferencedSchema, ImmutableArray<string> ReferencedColumns, string ReferencedLogicalTable, ForeignKeyAction DeleteAction, bool IsNoCheck);`
* `public sealed record SmoFormatOptions( IdentifierQuoteStrategy IdentifierQuoteStrategy, bool NormalizeWhitespace, IndexNamingOptions IndexNaming) {`
* `public sealed record SmoIndexColumnDefinition(string Name, int Ordinal, bool IsIncluded, bool IsDescending);`
* `public sealed record SmoIndexCompressionSetting(int PartitionNumber, string Compression);`
* `public sealed record SmoIndexDataSpace(string Name, string Type);`
* `public sealed record SmoIndexDefinition( string Name, bool IsUnique, bool IsPrimaryKey, bool IsPlatformAuto, string? Description, ImmutableArray<SmoIndexColumnDefinition> Columns, SmoIndexMetadata Metadata);`
* `public sealed record SmoIndexMetadata( bool IsDisabled, bool IsPadded, int? FillFactor, bool IgnoreDuplicateKey, bool AllowRowLocks, bool AllowPageLocks, bool StatisticsNoRecompute, string? FilterDefinition, SmoIndexDataSpace? DataSpace, ImmutableArray<SmoIndexPartitionColumn> PartitionColumns, ImmutableArray<SmoIndexCompressionSetting> DataCompression) {`
* `public sealed record SmoIndexPartitionColumn(string Name, int Ordinal);`
* `public sealed record SmoModel( ImmutableArray<SmoTableDefinition> Tables, ImmutableArray<TableArtifactSnapshot> Snapshots) {`
* `public sealed record SmoRenameLensRequest(SmoModel Model, NamingOverrideOptions NamingOverrides);`
* `public sealed record SmoRenameMapping( string Module, string OriginalModule, string Schema, string PhysicalName, string LogicalName, string EffectiveName);`
* `public sealed record SmoTableDefinition( string Module, string OriginalModule, string Name, string Schema, string Catalog, string LogicalName, string? Description, ImmutableArray<SmoColumnDefinition> Columns, ImmutableArray<SmoIndexDefinition> Indexes, ImmutableArray<SmoForeignKeyDefinition> ForeignKeys, ImmutableArray<SmoTriggerDefinition> Triggers);`
* `public sealed record SmoTriggerDefinition( string Name, string Schema, string Table, bool IsDisabled, string Definition);`
* `public sealed record TypeMappingRuleDefinition( TypeMappingStrategy Strategy, string? SqlType, int? FallbackLength, int? DefaultPrecision, int? DefaultScale, int? Scale, int? MaxLengthThreshold, TypeValueSource? LengthSource = null, TypeValueSource? PrecisionSource = null, TypeValueSource? ScaleSource = null, int? LengthParameterIndex = null, int? PrecisionParameterIndex = null, int? ScaleParameterIndex = null) {`

</details>

### Osm.Validation (records: 65)

<details>
<summary><code>Osm.Validation</code> (2)</summary>

* `public sealed record ModelValidationMessage(string Code, string Message, string Path, ValidationSeverity Severity) {`
* `public sealed record ModelValidationReport(ImmutableArray<ModelValidationMessage> Messages) {`

</details>

<details>
<summary><code>Osm.Validation.Tightening</code> (44)</summary>

* `internal readonly record struct EntityAttributeIndexEntry( EntityModel Entity, AttributeModel Attribute, ColumnCoordinate Coordinate);`
* `internal sealed record ColumnDecisionAggregation( ImmutableDictionary<ColumnCoordinate, NullabilityDecision> Nullability, ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> ForeignKeys, ImmutableDictionary<ColumnCoordinate, ColumnIdentity> ColumnIdentities, IReadOnlyDictionary<ColumnCoordinate, ColumnAnalysisBuilder> ColumnAnalyses);`
* `internal sealed record ForeignKeyMatrix( ImmutableArray<ForeignKeyPolicyDefinition> Definitions, ImmutableDictionary<ForeignKeyPolicyScenario, ForeignKeyPolicyDefinition> Lookup) {`
* `internal sealed record ForeignKeyPolicyDefinition( ForeignKeyPolicyScenario Scenario, string Description, bool DeleteRuleIsIgnore, bool HasOrphans, bool HasExistingConstraint, bool CrossSchema, bool CrossCatalog, bool EnableCreation, bool AllowCrossSchema, bool AllowCrossCatalog, bool ExpectCreate, ImmutableArray<string> Rationales);`
* `internal sealed record NullabilityMatrix( ImmutableDictionary<TighteningMode, NullabilityModeDefinition> Modes, ImmutableDictionary<NullabilitySignalKey, NullabilitySignalMetadata> Metadata) {`
* `internal sealed record NullabilityModeDefinition( TighteningMode Mode, string Code, string Description, ImmutableArray<NullabilitySignalDefinition> SignalDefinitions, bool EvidenceEmbeddedInRoot) {`
* `internal sealed record NullabilitySignalDefinition( NullabilitySignalKey Signal, NullabilitySignalParticipation Participation, bool RequiresEvidence, bool AddsRemediationWhenEvidenceMissing);`
* `internal sealed record NullabilitySignalMetadata( NullabilitySignalKey Signal, string Code, string Description, ImmutableArray<string> Rationales);`
* `internal sealed record UniqueIndexAggregation( ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> Decisions, ImmutableDictionary<IndexCoordinate, string> IndexModules);`
* `internal sealed record UniqueIndexDefinition( TighteningMode Mode, UniquePolicyScenario Scenario, UniqueIndexOutcome Outcome, ImmutableArray<string> Rationales);`
* `internal sealed record UniqueIndexMatrix( ImmutableArray<UniqueIndexDefinition> Definitions, ImmutableDictionary<(TighteningMode Mode, UniquePolicyScenario Scenario), UniqueIndexDefinition> Lookup) {`
* `internal sealed record UniqueIndexOutcome(bool EnforceUnique, RemediationDirective Remediation);`
* `private readonly record struct SummaryEntry(int Count, int Priority, string Message);`
* `private sealed record CompositeSignalSet( ISet<ColumnCoordinate> Clean, ISet<ColumnCoordinate> Duplicates, IReadOnlyDictionary<string, CompositeUniqueCandidateProfile> Lookup);`
* `private sealed record DuplicateCandidate(ModuleModel Module, EntityModel Entity);`
* `private sealed record DuplicateResolution(DuplicateCandidate Canonical, TighteningDiagnostic Diagnostic);`
* `private sealed record ForeignKeyEvaluation( ForeignKeyDecision Decision, bool HasOrphan, bool IgnoreRule, bool CrossSchemaBlocked, bool CrossCatalogBlocked);`
* `private sealed record UniqueIndexEvidence( bool HasProfile, bool HasEvidence, bool HasDuplicates, bool DataClean, SortedSet<string> Rationales);`
* `public readonly record struct ColumnCoordinate(SchemaName Schema, TableName Table, ColumnName Column) {`
* `public readonly record struct IndexCoordinate(SchemaName Schema, TableName Table, IndexName Index) {`
* `public sealed record ChangeRisk(RiskLevel Level, string Label, string Description) {`
* `public sealed record ColumnAnalysis( ColumnIdentity Identity, NullabilityDecision Nullability, ForeignKeyDecision? ForeignKey, ImmutableArray<UniqueIndexDecision> UniqueIndexes, ImmutableArray<Opportunity> Opportunities) {`
* `public sealed record ColumnDecisionReport( ColumnCoordinate Column, bool MakeNotNull, bool RequiresRemediation, ImmutableArray<string> Rationales) {`
* `public sealed record ColumnIdentity( ColumnCoordinate Coordinate, ModuleName Module, EntityName EntityLogicalName, TableName EntityPhysicalName, AttributeName AttributeLogicalName) {`
* `public sealed record EntityContext( EntityModel Entity, AttributeModel Attribute, ColumnIdentity Identity, ColumnProfile? ColumnProfile, UniqueCandidateProfile? UniqueProfile, ForeignKeyReality? ForeignKeyReality, EntityModel? ForeignKeyTarget, bool SingleColumnUniqueClean, bool SingleColumnUniqueHasDuplicates, bool CompositeUniqueClean, bool CompositeUniqueHasDuplicates) {`
* `public sealed record EntityLookupResolution( IReadOnlyDictionary<EntityName, EntityModel> Lookup, ImmutableArray<TighteningDiagnostic> Diagnostics);`
* `public sealed record ForeignKeyDecision( ColumnCoordinate Column, bool CreateConstraint, ImmutableArray<string> Rationales) {`
* `public sealed record ForeignKeyDecisionReport( ColumnCoordinate Column, bool CreateConstraint, ImmutableArray<string> Rationales) {`
* `public sealed record ModuleDecisionRollup( int ColumnCount, int TightenedColumnCount, int RemediationColumnCount, int UniqueIndexCount, int UniqueIndexesEnforcedCount, int UniqueIndexesRequireRemediationCount, int ForeignKeyCount, int ForeignKeysCreatedCount, ImmutableDictionary<string, int> ColumnRationales, ImmutableDictionary<string, int> UniqueIndexRationales, ImmutableDictionary<string, int> ForeignKeyRationales);`
* `public sealed record NullabilityDecision( ColumnCoordinate Column, bool MakeNotNull, bool RequiresRemediation, ImmutableArray<string> Rationales, SignalEvaluation? Trace) {`
* `public sealed record OpportunitiesReport( ImmutableArray<ColumnAnalysis> Columns, ReportSummary Summary) {`
* `public sealed record PolicyAnalysisResult(PolicyDecisionSet Decisions, OpportunitiesReport Report) {`
* `public sealed record PolicyDecisionReport( ImmutableArray<ColumnDecisionReport> Columns, ImmutableArray<UniqueIndexDecisionReport> UniqueIndexes, ImmutableArray<ForeignKeyDecisionReport> ForeignKeys, ImmutableDictionary<string, int> ColumnRationaleCounts, ImmutableDictionary<string, int> UniqueIndexRationaleCounts, ImmutableDictionary<string, int> ForeignKeyRationaleCounts, ImmutableArray<TighteningDiagnostic> Diagnostics, ImmutableDictionary<string, ModuleDecisionRollup> ModuleRollups, ImmutableDictionary<string, ToggleExportValue> TogglePrecedence, ImmutableDictionary<string, string> ColumnModules, ImmutableDictionary<string, string> IndexModules, TighteningToggleSnapshot Toggles) {`
* `public sealed record PolicyDecisionSet( ImmutableDictionary<ColumnCoordinate, NullabilityDecision> Nullability, ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> ForeignKeys, ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> UniqueIndexes, ImmutableArray<TighteningDiagnostic> Diagnostics, ImmutableDictionary<ColumnCoordinate, ColumnIdentity> ColumnIdentities, ImmutableDictionary<IndexCoordinate, string> IndexModules, TighteningToggleSnapshot Toggles) {`
* `public sealed record ReportSummary( int ColumnCount, int ColumnsWithOpportunities, OpportunityMetrics Metrics) {`
* `public sealed record TighteningDecisions( ImmutableDictionary<ColumnCoordinate, NullabilityDecision> Nullability, ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> ForeignKeys, ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> UniqueIndexes) {`
* `public sealed record TighteningDiagnostic( string Code, string Message, TighteningDiagnosticSeverity Severity, string LogicalName, string CanonicalModule, string CanonicalSchema, string CanonicalPhysicalName, ImmutableArray<TighteningDuplicateCandidate> Candidates, bool ResolvedByOverride);`
* `public sealed record TighteningDuplicateCandidate(string Module, string Schema, string PhysicalName);`
* `public sealed record TighteningToggleSnapshot( ToggleState<TighteningMode> Mode, ToggleState<double> NullBudget, ToggleState<bool> ForeignKeyCreationEnabled, ToggleState<bool> ForeignKeyCrossSchemaAllowed, ToggleState<bool> ForeignKeyCrossCatalogAllowed, ToggleState<bool> ForeignKeyTreatMissingDeleteRuleAsIgnore, ToggleState<bool> SingleColumnUniqueEnforced, ToggleState<bool> MultiColumnUniqueEnforced, ToggleState<bool> RemediationGeneratePreScripts, ToggleState<int> RemediationMaxRowsDefaultBackfill) {`
* `public sealed record ToggleExportValue(object? Value, ToggleSource Source) {`
* `public sealed record ToggleState<T>(T Value, ToggleSource Source);`
* `public sealed record UniqueIndexAnalysis( IndexCoordinate Index, UniqueIndexDecision Decision, ImmutableArray<string> Rationales, bool HasDuplicates, bool PhysicalReality, bool PolicyDisabled, bool HasEvidence, bool DataClean, ImmutableArray<ColumnCoordinate> Columns);`
* `public sealed record UniqueIndexDecision( IndexCoordinate Index, bool EnforceUnique, bool RequiresRemediation, ImmutableArray<string> Rationales) {`
* `public sealed record UniqueIndexDecisionReport( IndexCoordinate Index, bool EnforceUnique, bool RequiresRemediation, ImmutableArray<string> Rationales) {`

</details>

<details>
<summary><code>Osm.Validation.Tightening.Opportunities</code> (5)</summary>

* `public sealed record OpportunitiesReport( ImmutableArray<Opportunity> Opportunities, ImmutableDictionary<OpportunityDisposition, int> DispositionCounts, ImmutableDictionary<OpportunityType, int> TypeCounts, ImmutableDictionary<RiskLevel, int> RiskCounts, DateTimeOffset GeneratedAtUtc) {`
* `public sealed record Opportunity( OpportunityType Type, string Title, string Summary, ChangeRisk Risk, OpportunityDisposition Disposition, ImmutableArray<string> Evidence, ColumnCoordinate? Column, IndexCoordinate? Index, string? Schema, string? Table, string? ConstraintName, ImmutableArray<string> Statements, ImmutableArray<string> Rationales, OpportunityEvidenceSummary? EvidenceSummary, ImmutableArray<OpportunityColumn> Columns) {`
* `public sealed record OpportunityColumn( ColumnIdentity Identity, string DataType, string? SqlType, bool? PhysicalNullable, bool? PhysicalUnique, long? RowCount, long? NullCount, ProfilingProbeStatus? NullProbeStatus, bool? HasDuplicates, ProfilingProbeStatus? UniqueProbeStatus, bool? HasOrphans, bool? HasDatabaseConstraint, string? DeleteRule) {`
* `public sealed record OpportunityEvidenceSummary( bool RequiresRemediation, bool EvidenceAvailable, bool? DataClean, bool? HasDuplicates, bool? HasOrphans);`
* `public sealed record OpportunityMetrics( int Total, int LowRisk, int ModerateRisk, int HighRisk, int UnknownRisk) {`

</details>

<details>
<summary><code>Osm.Validation.Tightening.Signals</code> (14)</summary>

* `internal abstract record NullabilitySignal {`
* `internal readonly record struct NullabilitySignalContext( TighteningOptions Options, EntityModel Entity, AttributeModel Attribute, ColumnCoordinate Coordinate, ColumnProfile? ColumnProfile, UniqueCandidateProfile? UniqueProfile, ForeignKeyReality? ForeignKeyReality, EntityModel? ForeignKeyTarget, bool IsSingleUniqueClean, bool HasSingleUniqueDuplicates, bool IsCompositeUniqueClean, bool HasCompositeUniqueDuplicates) {`
* `internal sealed record AllOfSignal : NullabilitySignal {`
* `internal sealed record AnyOfSignal : NullabilitySignal {`
* `internal sealed record DefaultSignal() : NullabilitySignal("S7_DEFAULT_PRESENT", "Column has default value") {`
* `internal sealed record ForeignKeySupportSignal() : NullabilitySignal("S3_FK_SUPPORT", "Foreign key has enforced relationship or can be created safely") {`
* `internal sealed record MandatorySignal() : NullabilitySignal("S5_LOGICAL_MANDATORY", "Logical attribute is mandatory") {`
* `internal sealed record NullEvidenceSignal() : NullabilitySignal("D1_DATA_NO_NULLS", "Profiling evidence shows no NULL values within budget") {`
* `internal sealed record NullabilityPolicyDefinition( NullabilitySignal Root, NullabilitySignal Evidence, ImmutableHashSet<string> ConditionalSignalCodes, bool EvidenceEmbeddedInRoot, PrimaryKeySignal PrimaryKeySignal, PhysicalNotNullSignal PhysicalSignal, UniqueCleanSignal UniqueSignal, MandatorySignal MandatorySignal, DefaultSignal DefaultSignal, ForeignKeySupportSignal ForeignKeySignal) {`
* `internal sealed record PhysicalNotNullSignal() : NullabilitySignal("S2_DB_NOT_NULL", "Physical column is marked NOT NULL") {`
* `internal sealed record PrimaryKeySignal() : NullabilitySignal("S1_PK", "Column is OutSystems Identifier (PK)") {`
* `internal sealed record RequiresEvidenceSignal(NullabilitySignal Inner, NullabilitySignal Evidence) : NullabilitySignal( $"{Inner.Code}_REQUIRES_{Evidence.Code}", $"{Inner.Description} (requires {Evidence.Description})") {`
* `internal sealed record UniqueCleanSignal() : NullabilitySignal("S4_UNIQUE_CLEAN", "Unique index (single or composite) has no nulls or duplicates") {`
* `public sealed record SignalEvaluation( string Code, string Description, bool Result, ImmutableArray<string> Rationales, ImmutableArray<SignalEvaluation> Children) {`

</details>

## 8. Enumeration Catalog
Enumerations shape the finite state spaces that policies, configuration, and telemetry rely on. They remain stable contracts across contexts and are listed below for completeness.

### Osm.Dmm (enums: 1)

<details>
<summary><code>Osm.Dmm</code> (1)</summary>

* `public enum DmmComparisonFeatures {`

</details>

### Osm.Domain (enums: 7)

<details>
<summary><code>Osm.Domain.Configuration</code> (1)</summary>

* `public enum TighteningMode {`

</details>

<details>
<summary><code>Osm.Domain.Model</code> (4)</summary>

* `public enum IndexColumnDirection {`
* `public enum IndexKind {`
* `public enum SequenceCacheMode {`
* `public enum TemporalRetentionKind {`

</details>

<details>
<summary><code>Osm.Domain.Profiling</code> (2)</summary>

* `public enum ProfilingInsightSeverity {`
* `public enum ProfilingProbeOutcome {`

</details>

### Osm.Pipeline (enums: 3)

<details>
<summary><code>Osm.Pipeline.Evidence</code> (2)</summary>

* `public enum EvidenceArtifactType {`
* `public enum EvidenceCacheOutcome {`

</details>

<details>
<summary><code>Osm.Pipeline.Orchestration</code> (1)</summary>

* `public enum PipelineInsightSeverity {`

</details>

### Osm.Smo (enums: 3)

<details>
<summary><code>Osm.Smo</code> (3)</summary>

* `internal enum TypeResolutionSource {`
* `public enum IdentifierQuoteStrategy {`
* `public enum TypeMappingStrategy {`

</details>

### Osm.Validation (enums: 7)

<details>
<summary><code>Osm.Validation</code> (1)</summary>

* `public enum ValidationSeverity {`

</details>

<details>
<summary><code>Osm.Validation.Tightening</code> (5)</summary>

* `internal enum NullabilitySignalKey {`
* `private enum ColumnSummaryBucket {`
* `public enum RiskLevel {`
* `public enum TighteningDiagnosticSeverity {`
* `public enum ToggleSource {`

</details>

<details>
<summary><code>Osm.Validation.Tightening.Opportunities</code> (1)</summary>

* `public enum OpportunityType {`

</details>

## 9. Evolution Roadmap
To achieve this target state, the backlog prioritizes: completing the tightening policy matrix, expanding SMO parity tests, implementing advanced SQL extraction adapters, and institutionalizing CI quality gates. These workstreams ensure the contracts above remain trustworthy while the platform scales to enterprise workloads.【F:tasks.md†L5-L137】

## 10. References
- [Architecture Guardrails](../architecture-guardrails.md)
- [Backlog](../tasks.md)
- [README](../readme.md)

