# Domain Model Constitution: Unified Entity Pipeline Architecture

**Status**: PRESCRIPTIVE FOUNDATION - Architectural First Principles
**Date**: 2025-01-24
**Purpose**: Define the ontological domain model that implementation must conform to

---

## PREAMBLE: What This Document IS

This is not implementation guidance. This is **ontological prescription**—the first principles that define what the system IS at its essence.

V3 excavated what exists. This document prescribes **what must be**.

Every type signature, every bounded context boundary, every composition rule defined here is **law**. Implementation that violates these principles is **architecturally invalid**, regardless of whether it "works."

The compiler enforces type safety. This document enforces **ontological safety**.

---

## PART I: BOUNDED CONTEXTS & STRATEGIC DESIGN

### § 1. Core Domain: Entity Pipeline Execution

**Ubiquitous Language:**

- **Entity**: A table definition with attributes, indexes, relationships, and data
- **Module**: A logical grouping of entities (OutSystems concept)
- **Selection**: The act of choosing which entities participate in a pipeline invocation
- **Topological Order**: FK-dependency-safe ordering of entities for data emission
- **Emission**: Generation of SQL artifacts (DDL for schema, DML for data)
- **Transform**: Application of business logic to metadata or data
- **Insertion Strategy**: Semantics of how SQL modifies target database (INSERT, MERGE, etc.)

**Strategic Importance:** This is the **core domain**—the reason the system exists. All complexity lives here.

**Aggregate Roots:**

```fsharp
// The universal aggregate - owns entire entity model
type OsmModel = {
    Modules: ModuleModel list
    Sequences: SequenceModel list
    ExportedAtUtc: DateTime
    ExtendedProperties: Map<string, string>
}
with
    // Invariants protected by construction
    static member Create: modules:ModuleModel list -> sequences:SequenceModel list -> Result<OsmModel>

    // Domain behavior
    member FindEntity: moduleName:ModuleName -> entityName:EntityName -> Result<EntityModel>
    member FindModule: moduleName:ModuleName -> Result<ModuleModel>
```

```fsharp
// Module aggregate - owns entities within module boundary
type ModuleModel = {
    Name: ModuleName
    Entities: EntityModel list  // Aggregate owns entities
    IsSystemModule: bool
    IsActive: bool
    ExtendedProperties: Map<string, string>
}
with
    static member Create: name:ModuleName -> entities:EntityModel list -> Result<ModuleModel>

    // Invariants: at least one entity, unique logical names, unique physical names
    member ValidateUniqueNames: unit -> Result<unit>
```

```fsharp
// Entity aggregate - owns attributes, indexes, relationships
type EntityModel = {
    Name: EntityName
    PhysicalTableName: TableName
    Attributes: AttributeModel list  // Owned entities
    Indexes: IndexModel list
    Relationships: RelationshipModel list
    Triggers: TriggerModel list
    Metadata: EntityMetadata
    DataKind: DataKind  // Static vs Regular
}
with
    static member Create: name:EntityName -> physicalTableName:TableName -> attributes:AttributeModel list -> Result<EntityModel>

    // Domain behavior
    member FindAttribute: attributeName:AttributeName -> Result<AttributeModel>
    member PrimaryKey: unit -> Result<IndexModel>  // Invariant: must have PK
    member ForeignKeys: unit -> RelationshipModel list
```

**Entity (Child of Aggregates):**

```fsharp
type AttributeModel = {
    LogicalName: AttributeName
    ColumnName: ColumnName
    DataType: string
    Length: int option
    Decimals: int option
    IsMandatory: bool
    IsAutoNumber: bool
    DeleteRule: DeleteRule
    Description: string
    Reality: AttributeReality option  // On-disk metadata
    OnDisk: AttributeOnDiskMetadata option
}
with
    static member Create: logicalName:AttributeName -> columnName:ColumnName -> dataType:string -> Result<AttributeModel>
```

```fsharp
type IndexModel = {
    Name: IndexName option
    Columns: IndexColumnModel list
    IsUnique: bool
    IsPrimary: bool
    IsClustered: bool
    OnDisk: IndexOnDiskMetadata option
}

type RelationshipModel = {
    ViaAttribute: AttributeReference
    TargetEntity: EntityName
    TargetModule: ModuleName option
    ActualConstraints: RelationshipActualConstraint list  // On-disk FK constraints
}
```

**Value Objects (Identity-less, Immutable):**

```fsharp
// Name value objects (primitive obsession prevention)
type EntityName = private EntityName of string
    with
        static member Create: value:string -> Result<EntityName>
        member Value: string

type AttributeName = private AttributeName of string
type ModuleName = private ModuleName of string
type TableName = private TableName of string
type ColumnName = private ColumnName of string
// ... 11 total name types

// Coordinate value objects (location in model)
type EntityCoordinate = {
    Module: ModuleName
    Entity: EntityName
}

type AttributeCoordinate = {
    Module: ModuleName
    Entity: EntityName
    Attribute: AttributeName
}

type IndexCoordinate = {
    Module: ModuleName
    Entity: EntityName
    Index: IndexName option  // Primary key has no name
}

// Metadata value objects
type EntityMetadata = {
    Label: string
    Description: string
    DataOrigin: DataOrigin
}

type AttributeOnDiskMetadata = {
    Collation: string option
    IsComputed: bool
    ComputedDefinition: string option
    IsIdentity: bool
    IdentitySeed: int64 option
    IdentityIncrement: int64 option
    DefaultConstraint: AttributeOnDiskDefaultConstraint option
    CheckConstraints: AttributeOnDiskCheckConstraint list
}
```

**Domain Services (Stateless Operations on Aggregates):**

```fsharp
// Topological ordering (protected citizen for data pipelines)
module EntityDependencySorter =
    type SortOptions = {
        DeferJunctionTables: bool
        CircularDependencyOptions: CircularDependencyOptions option
    }

    type OrderingResult = {
        Tables: StaticEntityTableData list  // FK-safe order
        NodeCount: int
        EdgeCount: int
        CycleDetected: bool
        StronglyConnectedComponents: Set<TableKey> list option
        TopologicalOrderingApplied: bool
        Mode: OrderingMode
    }

    // Pure function - no side effects
    let sortByForeignKeys
        (tables: StaticEntityTableData list)
        (model: OsmModel)
        (namingOverrides: NamingOverrideOptions option)
        (options: SortOptions)
        : OrderingResult

// Module filtering (selection logic)
module ModuleFilter =
    let filter
        (model: OsmModel)
        (options: ModuleFilterOptions)
        : OsmModel  // Filtered model with selected modules/entities
```

---

### § 2. Supporting Domain: Profiling & Data Quality

**Ubiquitous Language:**

- **Profile**: Snapshot of data quality metrics for entities
- **Null Count**: Number of NULL values observed in a column
- **Orphan**: FK value with no corresponding PK in target table
- **Uniqueness Violation**: Duplicate values in unique constraint
- **Profiling Insight**: Domain-level observation about data quality

**Strategic Importance:** Supporting domain. Informs core domain decisions (nullability tightening, deferred FKs), but not the reason the system exists.

**Aggregate Root:**

```fsharp
type ProfileSnapshot = {
    Entities: EntityProfile list
    TakenAtUtc: DateTime
    DatabaseName: string
    EnvironmentName: string option
}
with
    static member Create: entities:EntityProfile list -> databaseName:string -> Result<ProfileSnapshot>
```

**Entities:**

```fsharp
type EntityProfile = {
    Coordinate: EntityCoordinate
    RowCount: int64
    Columns: ColumnProfile list
    UniqueCandidates: UniqueCandidateProfile list
    ForeignKeyRealities: ForeignKeyReality list
}

type ColumnProfile = {
    Coordinate: AttributeCoordinate
    NullCount: int64
    NullPercentage: decimal
    DistinctCount: int64 option
    MinValue: obj option
    MaxValue: obj option
    Samples: NullRowSample list  // For diagnostic investigation
}

type ForeignKeyReality = {
    SourceCoordinate: AttributeCoordinate
    TargetCoordinate: EntityCoordinate
    OrphanCount: int64
    OrphanPercentage: decimal
    Samples: ForeignKeyOrphanSample list
}
```

**Value Objects:**

```fsharp
type ProfilingInsight = {
    Coordinate: ProfilingInsightCoordinate
    Severity: InsightSeverity
    Category: InsightCategory
    Message: string
    Metadata: Map<string, string>
}

type ProfilingInsightCoordinate =
    | Entity of EntityCoordinate
    | Attribute of AttributeCoordinate
    | Index of IndexCoordinate
    | Relationship of AttributeCoordinate  // FK source
```

**Domain Services:**

```fsharp
module ProfilingInsightGenerator =
    // Generate insights from profile data
    let generate
        (profile: ProfileSnapshot)
        (model: OsmModel)
        : ProfilingInsight list
```

---

### § 3. Supporting Domain: Policy & Validation

**Ubiquitous Language:**

- **Tightening**: Making constraints stricter than model declares (e.g., NOT NULL when model says nullable)
- **Policy Decision**: Operator-approved decision to apply tightening
- **Opportunity**: Candidate for tightening based on profiling evidence
- **Validation Override**: Explicit operator approval to bypass validation warning

**Strategic Importance:** Supporting domain. Enables "cautious mode" where operator controls constraint changes.

**Aggregate Root:**

```fsharp
type PolicyDecisionSet = {
    Nullability: NullabilityDecision list
    ForeignKeys: ForeignKeyDecision list
    UniqueIndexes: UniqueIndexDecision list
    SynthesizedAtUtc: DateTime
}
with
    static member Create: decisions:TighteningDecisions -> Result<PolicyDecisionSet>
```

**Value Objects:**

```fsharp
type NullabilityDecision = {
    Coordinate: AttributeCoordinate
    ModelDeclaredMandatory: bool
    OnDiskNullable: bool
    ProfilingNullCount: int64
    Decision: NullabilityOutcome
    Rationale: string
    Risk: ChangeRisk
}

type NullabilityOutcome =
    | EnforceNotNull      // Apply NOT NULL (model + profiling agree)
    | KeepNullable        // Keep nullable (profiling shows NULLs)
    | RequireOperatorApproval  // Conflict - need explicit override

type ForeignKeyDecision = {
    Coordinate: AttributeCoordinate
    TargetEntity: EntityCoordinate
    OrphanCount: int64
    Decision: ForeignKeyOutcome
    Rationale: string
}

type ForeignKeyOutcome =
    | EnforceConstraint       // Normal FK (no orphans)
    | DeferConstraint         // WITH NOCHECK (orphans exist)
    | SkipConstraint          // Don't emit FK
```

**Domain Services:**

```fsharp
module NullabilityEvaluator =
    type EvaluationContext = {
        Model: OsmModel
        Profile: ProfileSnapshot
        Options: TighteningOptions
        Overrides: ValidationOverrideOptions
    }

    let evaluate
        (context: EvaluationContext)
        : NullabilityDecision list

module ForeignKeyEvaluator =
    let evaluate
        (model: OsmModel)
        (profile: ProfileSnapshot)
        (options: TighteningOptions)
        : ForeignKeyDecision list
```

---

### § 4. Supporting Domain: UAT User Migration

**Ubiquitous Language:**

- **User FK**: Foreign key column referencing User table
- **Orphan User ID**: QA user ID that doesn't exist in UAT roster
- **User Mapping**: QA user ID → UAT user ID transformation
- **Dual-Mode Transform**: Discovery (what to transform) + Application (when to transform)

**Strategic Importance:** Supporting domain. Enables QA→UAT data migration with user ID remapping.

**Aggregate Root:**

```fsharp
type UserRemappingContext = {
    ForeignKeyCatalog: UserForeignKeyCatalog
    QaInventory: UserInventory
    UatInventory: UserInventory
    Mapping: UserIdMapping
    Orphans: OrphanUserId list
    DiscoveredAtUtc: DateTime
}
with
    static member Discover
        (model: OsmModel)
        (qaUsers: UserInventory)
        (uatUsers: UserInventory)
        (mapping: UserIdMapping)
        : Result<UserRemappingContext>
```

**Value Objects:**

```fsharp
type UserForeignKeyCatalog = {
    Columns: UserForeignKeyColumn list
}

type UserForeignKeyColumn = {
    Coordinate: AttributeCoordinate
    TargetEntity: EntityCoordinate  // Always User table
}

type UserInventory = {
    Users: UserRecord list
    EnvironmentName: string
}

type UserRecord = {
    Id: int
    Username: string
    Email: string
    Name: string
    IsActive: bool
}

type UserIdMapping = {
    Mappings: Map<UserId, UserId>  // QA ID → UAT ID
    Rationales: Map<UserId, string>
}

type OrphanUserId = {
    QaUserId: UserId
    ObservedInTables: EntityCoordinate list
    HasMapping: bool
}
```

**Domain Services:**

```fsharp
module UserForeignKeyCatalogDiscoverer =
    // Discover all FK columns referencing User table
    let discover
        (model: OsmModel)
        : UserForeignKeyCatalog

module OrphanUserDetector =
    // Detect QA user IDs not in UAT roster
    let detect
        (catalog: UserForeignKeyCatalog)
        (data: DatabaseSnapshot)  // Actual row data
        (uatInventory: UserInventory)
        : OrphanUserId list

module UserMappingValidator =
    // Validate mapping completeness and correctness
    let validate
        (mapping: UserIdMapping)
        (qaInventory: UserInventory)
        (uatInventory: UserInventory)
        (orphans: OrphanUserId list)
        : Result<unit>
```

---

### § 5. Infrastructure Domain: Database Access

**Ubiquitous Language:**

- **Database Snapshot**: Unified fetch of metadata + statistics + data
- **Metadata**: Schema structure from OSSYS_* tables
- **Statistics**: Data quality metrics from sys.* and profiling queries
- **Row Data**: Actual table data from OSUSR_* tables

**Strategic Importance:** Infrastructure domain. Provides data to core domain.

**Repository Interfaces (Anti-Corruption Layer):**

```fsharp
// Read-only repository for OsmModel
type IOsmModelRepository =
    abstract member Load: filePath:string -> Async<Result<OsmModel>>
    abstract member ExtractFromDatabase: connectionString:string -> filters:ModuleFilterOptions -> Async<Result<OsmModel>>

// Unified snapshot repository (eliminates triple-fetch)
type IDatabaseSnapshotRepository =
    abstract member Fetch: FetchRequest -> Async<Result<DatabaseSnapshot>>
    abstract member LoadCached: cacheKey:string -> Async<Result<DatabaseSnapshot option>>
    abstract member SaveCache: snapshot:DatabaseSnapshot -> cacheKey:string -> Async<Result<unit>>

type FetchRequest = {
    ConnectionString: string
    EntitySelection: EntitySelector
    Options: FetchOptions
}

type FetchOptions = {
    IncludeStructure: bool     // OSSYS_* metadata
    IncludeStatistics: bool    // Profiling queries
    IncludeRowData: bool       // OSUSR_* data
}
```

**Domain Model (Infrastructure Aggregate):**

```fsharp
// The unified snapshot that eliminates triple-fetch
type DatabaseSnapshot = {
    // Metadata from OSSYS_* (extract-model)
    Entities: EntityStructure list
    Relationships: RelationshipStructure list
    Indexes: IndexStructure list
    Triggers: TriggerStructure list

    // Statistics from profiling (sys.* + OSSYS_*)
    Statistics: Map<TableKey, EntityStatistics>

    // Row data from OSUSR_*
    Data: Map<TableKey, EntityData>

    // Metadata
    FetchedAtUtc: DateTime
    DatabaseName: string
    SchemaChecksum: string  // For cache invalidation
}
with
    // Derived projections (for backward compatibility)
    member ToModel: unit -> OsmModel
    member ToProfile: unit -> ProfileSnapshot
    member ToDataSet: unit -> EntityDataSet
```

**Adapters:**

```fsharp
// SQL Server metadata adapter
type IOutsystemsMetadataReader =
    abstract member ReadMetadata
        : connectionString:string
        -> filters:ModuleFilterOptions
        -> Async<Result<OutsystemsMetadataSnapshot>>

// SQL Server profiling adapter
type IDataProfiler =
    abstract member ProfileEntities
        : connectionString:string
        -> model:OsmModel
        -> Async<Result<ProfileSnapshot>>

// SQL Server data extraction adapter
type IEntityDataProvider =
    abstract member ExtractData
        : connectionString:string
        -> model:OsmModel
        -> filters:ModuleFilterOptions
        -> Async<Result<EntityDataSet>>
```

---

### § 6. Emission Domain: Artifact Generation

**Ubiquitous Language:**

- **Artifact**: Output file (SQL script, JSON manifest, diagnostic report)
- **DDL**: Data Definition Language (CREATE TABLE, ALTER TABLE, etc.)
- **DML**: Data Manipulation Language (INSERT, MERGE, etc.)
- **Emission Strategy**: How artifacts are organized (monolithic, per-module, etc.)

**Strategic Importance:** Infrastructure domain. Converts core domain to external representation.

**Value Objects:**

```fsharp
type EmissionStrategy =
    | SchemaPerModule of SchemaEmissionConfig
    | DataMonolithic of DataEmissionConfig
    | DataPerModuleWithSqlProj of DataEmissionConfig
    | Diagnostics of DiagnosticEmissionConfig
    | Combined of EmissionStrategy list

and SchemaEmissionConfig = {
    Directory: string
    Ordering: DeterministicOrdering
}

and DataEmissionConfig = {
    Path: string
    Ordering: TopologicalOrdering
}

and DeterministicOrdering =
    | ModuleThenAlphabetical  // For schema (DDL order-independent)
    | Alphabetical

and TopologicalOrdering =
    | GlobalFkSafe  // For data (DML requires FK-safe order)

type InsertionStrategy =
    | SchemaOnly
    | Insert of InsertConfig
    | Merge of MergeConfig
    | TruncateAndInsert of InsertConfig
    | None  // Export-only
    | Combined of InsertionStrategy list

and InsertConfig = {
    BatchSize: int
    NonDestructive: bool
}

and MergeConfig = {
    On: MergeKey
    BatchSize: int
}

and MergeKey =
    | PrimaryKey
    | Columns of ColumnName list
```

**Domain Services:**

```fsharp
module ScriptGenerator =
    // Generate CREATE TABLE DDL
    let generateSchema
        (model: OsmModel)
        (decisions: PolicyDecisionSet)
        (namingOverrides: NamingOverrideOptions)
        : Map<EntityCoordinate, string>  // Entity → DDL script

    // Generate INSERT DML
    let generateInserts
        (data: EntityDataSet)
        (ordering: TopologicalOrdering)
        (config: InsertConfig)
        (transforms: TransformContext option)  // For UAT-Users
        : Map<EntityCoordinate, string>  // Entity → INSERT script

    // Generate MERGE DML
    let generateMerges
        (data: EntityDataSet)
        (ordering: TopologicalOrdering)
        (config: MergeConfig)
        : Map<EntityCoordinate, string>  // Entity → MERGE script
```

---

## PART II: TACTICAL DESIGN - TYPE SYSTEM

### § 7. Functional Primitives (Foundation)

**Result Monad (Railway-Oriented Programming):**

```fsharp
type Result<'T> =
    | Success of 'T
    | Failure of ValidationError list

and ValidationError = {
    Code: string
    Message: string
    Metadata: Map<string, string>
}
with
    // Monadic operations
    static member Bind: ('T -> Result<'U>) -> Result<'T> -> Result<'U>
    static member Map: ('T -> 'U) -> Result<'T> -> Result<'U>
    static member Apply: Result<('T -> 'U)> -> Result<'T> -> Result<'U>

    // Async variants
    static member BindAsync: ('T -> Async<Result<'U>>) -> Async<Result<'T>> -> Async<Result<'U>>
    static member MapAsync: ('T -> Async<'U>) -> Async<Result<'T>> -> Async<Result<'U>>

    // Combinators
    static member Collect: Result<'T> list -> Result<'T list>  // All-or-nothing
    static member Traverse: ('T -> Result<'U>) -> 'T list -> Result<'U list>
```

**Option Monad (Explicit Null Handling):**

```fsharp
type Option<'T> =
    | Some of 'T
    | None
with
    // Monadic operations
    static member Bind: ('T -> Option<'U>) -> Option<'T> -> Option<'U>
    static member Map: ('T -> 'U) -> Option<'T> -> Option<'U>

    // Utilities
    member DefaultValue: 'T -> 'T
    member IsSome: bool
    member IsNone: bool

    // Unsafe (use sparingly)
    member Unwrap: unit -> 'T  // Throws if None
```

**Validation Applicative (Accumulate Errors):**

```fsharp
type Validation<'T> =
    | Valid of 'T
    | Invalid of ValidationError list
with
    // Applicative (not Monad - accumulates errors instead of short-circuiting)
    static member Apply: Validation<('T -> 'U)> -> Validation<'T> -> Validation<'U>
    static member Map: ('T -> 'U) -> Validation<'T> -> Validation<'U>
    static member Map2: ('T -> 'U -> 'V) -> Validation<'T> -> Validation<'U> -> Validation<'V>

    // Convert to Result (collapses errors)
    member ToResult: unit -> Result<'T>
```

**Immutable Collections:**

```fsharp
// Persistent, structural-sharing collections
type ImmutableList<'T> = 'T list  // F# list is already immutable
type ImmutableArray<'T> = System.Collections.Immutable.ImmutableArray<'T>
type ImmutableSet<'T> = Set<'T>
type ImmutableMap<'K, 'V> = Map<'K, 'V>
```

---

### § 8. Pipeline Composition Primitives

**Step Interface (Type-Safe State Transitions):**

```fsharp
// Core abstraction - every step is a state transformation
type IStep<'TInput, 'TOutput> =
    abstract member Execute: 'TInput -> CancellationToken -> Async<Result<'TOutput>>

// Composition operators
module Step =
    // Sequential composition (bind)
    let (>>=) (step1: IStep<'A, 'B>) (step2: IStep<'B, 'C>) : IStep<'A, 'C> =
        { new IStep<'A, 'C> with
            member _.Execute(input, ct) = async {
                let! result1 = step1.Execute(input, ct)
                match result1 with
                | Success output1 -> return! step2.Execute(output1, ct)
                | Failure errors -> return Failure errors
            }
        }

    // Parallel composition (when independent)
    let (<&>) (step1: IStep<'A, 'B>) (step2: IStep<'A, 'C>) : IStep<'A, ('B * 'C)> =
        { new IStep<'A, ('B * 'C)> with
            member _.Execute(input, ct) = async {
                let! result1 = step1.Execute(input, ct) |> Async.StartChild
                let! result2 = step2.Execute(input, ct) |> Async.StartChild
                let! r1 = result1
                let! r2 = result2
                match r1, r2 with
                | Success o1, Success o2 -> return Success (o1, o2)
                | Failure e1, Failure e2 -> return Failure (e1 @ e2)
                | Failure e, _ | _, Failure e -> return Failure e
            }
        }

    // Skip step (for partial participation)
    let skip<'T> : IStep<'T, 'T> =
        { new IStep<'T, 'T> with
            member _.Execute(input, _) = async { return Success input }
        }
```

---

### § 9. Multi-Spine State System (Partial Participation)

**Problem:** Current single-spine forces all pipelines through same state progression. Extract-model doesn't need BootstrapSnapshotGenerated.

**Solution:** Multiple state spines with shared envelope.

**Shared Base State:**

```fsharp
// Every pipeline state descends from this
type PipelineState = {
    Request: PipelineRequest
    Log: PipelineLog
    StartedAtUtc: DateTime
}

type PipelineRequest = {
    OutputDirectory: string
    Configuration: PipelineConfiguration
}

type PipelineLog = {
    Entries: PipelineLogEntry list
}
with
    member Record: code:string -> message:string -> metadata:Map<string, string> -> PipelineLog
```

**Spine 1: Minimal (Extract-Model Only):**

```fsharp
type ExtractionSpine =
    | ExtractionInitialized of PipelineState
    | ExtractionCompleted of ExtractionCompletedState

and ExtractionCompletedState = {
    Base: PipelineState
    Model: OsmModel
    Metrics: ExtractionMetrics
}
```

**Spine 2: Schema-Only (Modules Emission):**

```fsharp
type SchemaSpine =
    | SchemaInitialized of PipelineState
    | ModelLoaded of ModelLoadedState
    | ProfileCompleted of ProfileCompletedState
    | SchemaEmitted of SchemaEmittedState

and ModelLoadedState = {
    Base: PipelineState
    Model: OsmModel
}

and ProfileCompletedState = {
    Base: PipelineState
    Model: OsmModel
    Profile: ProfileSnapshot
}

and SchemaEmittedState = {
    Base: PipelineState
    Model: OsmModel
    Profile: ProfileSnapshot
    Decisions: PolicyDecisionSet
    SchemaArtifacts: SchemaArtifacts
}

and SchemaArtifacts = {
    Tables: Map<EntityCoordinate, string>  // DDL scripts
    SqlProjectPath: string
    Manifest: SsdtManifest
}
```

**Spine 3: Full Pipeline (Build-SSDT, Full-Export):**

```fsharp
type FullPipelineSpine =
    | FullPipelineInitialized of PipelineState
    | ModelLoaded of ModelLoadedState
    | ProfileCompleted of ProfileCompletedState
    | DecisionsSynthesized of DecisionsSynthesizedState
    | SchemaEmitted of SchemaEmittedWithDataState
    | DataEmitted of DataEmittedState
    | PipelineCompleted of PipelineCompletedState

and DecisionsSynthesizedState = {
    Base: PipelineState
    Model: OsmModel
    Profile: ProfileSnapshot
    Decisions: PolicyDecisionSet
    Opportunities: OpportunitiesReport
    Validations: ValidationReport
}

and SchemaEmittedWithDataState = {
    Base: PipelineState
    Model: OsmModel
    Profile: ProfileSnapshot
    Decisions: PolicyDecisionSet
    SchemaArtifacts: SchemaArtifacts
}

and DataEmittedState = {
    Base: PipelineState
    Model: OsmModel
    Profile: ProfileSnapshot
    Decisions: PolicyDecisionSet
    SchemaArtifacts: SchemaArtifacts option  // May skip schema
    DataArtifacts: DataArtifacts
}

and PipelineCompletedState = {
    Base: PipelineState
    Model: OsmModel
    Profile: ProfileSnapshot
    Decisions: PolicyDecisionSet
    SchemaArtifacts: SchemaArtifacts option
    DataArtifacts: DataArtifacts
    Diagnostics: DiagnosticArtifacts
}

and DataArtifacts = {
    StaticSeeds: string option  // Path to StaticSeeds.sql
    Bootstrap: string option    // Path to Bootstrap.sql
    DynamicData: string option  // Path to DynamicData.sql (UAT-transformed)
}

and DiagnosticArtifacts = {
    ProfilingReport: string
    ValidationResults: string
    CycleDiagnostics: string
    TransformExecutionLog: string
    PipelineManifest: string
}
```

**Type-Safe Pipeline Construction:**

```fsharp
// Extract-model pipeline (minimal spine)
type ExtractModelPipeline = IStep<ExtractionInitialized, ExtractionCompleted>

// Schema-only pipeline
type SchemaOnlyPipeline = IStep<SchemaInitialized, SchemaEmitted>

// Full pipeline
type FullPipeline = IStep<FullPipelineInitialized, PipelineCompleted>
```

**Key Property:** Each spine contains ONLY what that use case needs. No bloat. Compiler enforces this.

---

### § 10. Transform Taxonomy & Registry

**Transform Kinds:**

```fsharp
type TransformKind =
    | PureTransform       // Execute once, produce output
    | Discovery           // Produce semantic knowledge (Stage 3)
    | Application         // Enact semantic knowledge (Stage 5/6)
    | DualMode            // Discovery (Stage 3) + Application (Stage 5 or 6)

type StageBinding =
    | SingleStage of StageNumber
    | MultiStage of DiscoveryStage:StageNumber * ApplicationStage:StageNumber

and StageNumber =
    | Stage0  // Extract-Model
    | Stage1  // Selection
    | Stage2  // Fetch
    | Stage3  // Transform
    | Stage4  // Order
    | Stage5  // Emit
    | Stage6  // Apply
```

**Transform Registry:**

```fsharp
type TransformRegistry = {
    Transforms: RegisteredTransform list
}
with
    // Query transforms
    member GetByKind: TransformKind -> RegisteredTransform list
    member GetByStage: StageNumber -> RegisteredTransform list
    member GetMandatory: unit -> RegisteredTransform list
    member GetOptional: unit -> RegisteredTransform list

    // Validate registry (detect circular dependencies)
    member Validate: unit -> Result<unit>

and RegisteredTransform = {
    Name: string
    Kind: TransformKind
    Stage: StageBinding
    IsProtectedCitizen: bool  // Mandatory?
    OrderingConstraints: OrderingConstraints
    EnabledPredicate: PipelineConfiguration -> bool
    Implementation: ITransform
}

and OrderingConstraints = {
    MustRunBefore: string list  // Transform names
    MustRunAfter: string list
}

and ITransform =
    abstract member Execute: TransformInput -> Async<Result<TransformOutput>>

and TransformInput =
    | ModelTransform of OsmModel
    | DataTransform of EntityDataSet
    | ProfileTransform of ProfileSnapshot
    | CombinedTransform of OsmModel * EntityDataSet * ProfileSnapshot

and TransformOutput =
    | ModelOutput of OsmModel
    | DataOutput of EntityDataSet
    | ProfileOutput of ProfileSnapshot
    | ContextOutput of TransformationContext  // For dual-mode (e.g., UAT-Users)
```

**Transform Composition Engine:**

```fsharp
module TransformComposer =
    type CompositionResult = {
        Ordered: RegisteredTransform list  // Topologically sorted
        ParallelGroups: RegisteredTransform list list  // Independent transforms grouped
        ExecutionPlan: ExecutionNode list
    }

    and ExecutionNode =
        | Sequential of RegisteredTransform
        | Parallel of RegisteredTransform list

    // Compose transforms respecting ordering constraints
    let compose
        (registry: TransformRegistry)
        (config: PipelineConfiguration)
        : Result<CompositionResult>

    // Detect circular dependencies
    let detectCycles
        (transforms: RegisteredTransform list)
        : RegisteredTransform list list option  // SCCs if cycles exist

    // Execute transforms in order
    let execute
        (plan: ExecutionNode list)
        (input: TransformInput)
        : Async<Result<TransformOutput>>
```

**Example Transform Registrations:**

```fsharp
let registry = TransformRegistry.Create [
    // Protected citizen (mandatory)
    {
        Name = "DatabaseSnapshot.Fetch"
        Kind = PureTransform
        Stage = SingleStage Stage2
        IsProtectedCitizen = true
        OrderingConstraints = { MustRunBefore = ["*"]; MustRunAfter = [] }
        EnabledPredicate = (fun _ -> true)
        Implementation = DatabaseSnapshotFetcher()
    }

    // Data pipeline protected citizen
    {
        Name = "EntityDependencySorter"
        Kind = PureTransform
        Stage = SingleStage Stage4
        IsProtectedCitizen = true  // For data pipelines only
        OrderingConstraints = { MustRunBefore = ["Emit"; "Apply"]; MustRunAfter = ["Fetch"; "Transform"] }
        EnabledPredicate = (fun config -> config.EmitData)
        Implementation = EntityDependencySorter()
    }

    // Dual-mode transform (UAT-Users)
    {
        Name = "UAT-Users"
        Kind = DualMode
        Stage = MultiStage(DiscoveryStage = Stage3, ApplicationStage = Stage5)
        IsProtectedCitizen = false
        OrderingConstraints = {
            MustRunBefore = []  // Discovery before application (implicit in MultiStage)
            MustRunAfter = ["FK catalog discovery"]
        }
        EnabledPredicate = (fun config -> config.EnableUatUsers)
        Implementation = UatUsersTransform()
    }

    // Optional transform
    {
        Name = "EntitySeedDeterminizer"
        Kind = PureTransform
        Stage = SingleStage Stage3
        IsProtectedCitizen = false
        OrderingConstraints = {
            MustRunBefore = ["EntityDependencySorter"]
            MustRunAfter = ["Fetch"]
        }
        EnabledPredicate = (fun config -> config.EmitStaticSeeds || config.EmitBootstrap)
        Implementation = EntitySeedDeterminizer()
    }
]
```

---

### § 11. Diagnostic Channels (Orthogonal Observability)

**Three-Audience Model:**

```fsharp
type DiagnosticChannels = {
    Operator: IOperatorChannel      // Real-time (Spectre.Console)
    Auditor: IAuditorChannel        // Persistent artifacts
    Developer: IDeveloperChannel    // Debug logging
}

// Operator channel (real-time progress, warnings, errors)
type IOperatorChannel =
    abstract member BeginStage: stageName:string -> totalSteps:int -> unit
    abstract member CompleteStage: stageName:string -> duration:TimeSpan -> unit
    abstract member UpdateProgress: completed:int -> total:int -> currentTask:string -> unit
    abstract member Warning: message:string -> unit
    abstract member Error: message:string -> unit
    abstract member DisplayTable<'T>: data:'T list -> formatting:TableFormatting -> unit

// Auditor channel (persistent compliance artifacts)
type IAuditorChannel =
    abstract member RecordStageMetrics: stageName:string -> duration:TimeSpan -> metadata:Map<string, obj> -> unit
    abstract member RecordTransformWarning: transformName:string -> message:string -> unit
    abstract member RecordValidationFinding: finding:ValidationFinding -> unit

    // Artifact emission (called at Stage 5)
    abstract member WriteProfilingReport: path:string -> Async<Result<unit>>
    abstract member WriteValidationResults: path:string -> Async<Result<unit>>
    abstract member WriteCycleDiagnostics: path:string -> Async<Result<unit>>
    abstract member WriteTransformExecutionLog: path:string -> Async<Result<unit>>
    abstract member WritePipelineManifest: path:string -> Async<Result<unit>>

// Developer channel (debugging, performance)
type IDeveloperChannel =
    abstract member LogStageBegin: stageName:string -> metadata:Map<string, string> -> unit
    abstract member LogStageComplete: stageName:string -> duration:TimeSpan -> metadata:Map<string, string> -> unit
    abstract member LogTransformExecution: transformName:string -> duration:TimeSpan -> entitiesAffected:int -> unit
    abstract member LogSqlQuery: query:string -> duration:TimeSpan -> rowCount:int -> unit
    abstract member LogTopologicalSorting: nodes:int -> edges:int -> cycles:int -> sccs:int -> unit
    abstract member Trace: message:string -> metadata:Map<string, obj> -> unit
```

**Diagnostic Emitter (Taps All Channels):**

```fsharp
type DiagnosticEmitter(channels: DiagnosticChannels) =
    // Tapped at stage boundaries
    member _.EmitStageBegin(stageName: string) =
        channels.Operator.BeginStage(stageName, 0)
        channels.Developer.LogStageBegin(stageName, Map.empty)

    member _.EmitStageComplete(stageName: string, duration: TimeSpan) =
        channels.Operator.CompleteStage(stageName, duration)
        channels.Developer.LogStageComplete(stageName, duration, Map.empty)
        channels.Auditor.RecordStageMetrics(stageName, duration, Map.empty)

    // Tapped during transform execution
    member _.EmitTransformWarning(transformName: string, message: string) =
        channels.Operator.Warning($"{transformName}: {message}")
        channels.Developer.Trace($"Transform warning: {transformName}", Map.ofList ["message", box message])
        channels.Auditor.RecordTransformWarning(transformName, message)

    // Artifact emission (called at Stage 5)
    member _.EmitDiagnosticArtifacts(directory: string) = async {
        do! channels.Auditor.WriteProfilingReport($"{directory}/profiling-report.csv")
        do! channels.Auditor.WriteValidationResults($"{directory}/validation-results.log")
        do! channels.Auditor.WriteCycleDiagnostics($"{directory}/cycles.txt")
        do! channels.Auditor.WriteTransformExecutionLog($"{directory}/transform-execution.log")
        do! channels.Auditor.WritePipelineManifest($"{directory}/pipeline-manifest.json")
    }
```

**Integration into Pipeline:**

```fsharp
// Every step receives diagnostic emitter
type IStep<'TInput, 'TOutput> =
    abstract member Execute: input:'TInput -> diagnostics:DiagnosticEmitter -> ct:CancellationToken -> Async<Result<'TOutput>>

// Example step implementation
type FetchDatabaseSnapshotStep(repository: IDatabaseSnapshotRepository) =
    interface IStep<ModelLoadedState, ProfileCompletedState> with
        member _.Execute(state, diagnostics, ct) = async {
            diagnostics.EmitStageBegin("Fetch")
            let startTime = DateTime.UtcNow

            let! snapshotResult = repository.Fetch {
                ConnectionString = state.Base.Request.Configuration.ConnectionString
                EntitySelection = state.Base.Request.Configuration.EntitySelector
                Options = { IncludeStructure = true; IncludeStatistics = true; IncludeRowData = false }
            }

            let duration = DateTime.UtcNow - startTime
            diagnostics.EmitStageComplete("Fetch", duration)

            match snapshotResult with
            | Success snapshot ->
                return Success {
                    Base = state.Base
                    Model = snapshot.ToModel()
                    Profile = snapshot.ToProfile()
                }
            | Failure errors -> return Failure errors
        }
```

---

## PART III: CONVERGENT EXECUTION MODEL

### § 12. The Unified Pipeline (All Use Cases as Parameterizations)

**Core Abstraction:**

```fsharp
type EntityPipeline = {
    Spine: PipelineSpine
    Source: DataSource
    Selector: EntitySelector
    EmissionStrategy: EmissionStrategy
    InsertionStrategy: InsertionStrategy
    Transforms: TransformRegistry
    Configuration: PipelineConfiguration
}
with
    member Execute: ct:CancellationToken -> Async<Result<PipelineResult>>

and PipelineSpine =
    | MinimalSpine of ExtractionSpine
    | SchemaSpine of SchemaSpine
    | FullSpine of FullPipelineSpine

and DataSource =
    | Database of connectionString:string
    | CachedSnapshot of cacheKey:string
    | PreExtractedModel of filePath:string

and EntitySelector =
    | AllModules
    | FilteredModules of ModuleFilterOptions
    | Predicate of (EntityModel -> bool)
    | AllWithData  // Entities with row count > 0
    | Custom of (OsmModel -> EntityModel list)
with
    // Combinators
    member Intersect: EntitySelector -> EntitySelector
    member Union: EntitySelector -> EntitySelector
    member Except: EntitySelector -> EntitySelector
```

**Use Case 1: Extract-Model**

```fsharp
let extractModelPipeline = {
    Spine = MinimalSpine ExtractionInitialized
    Source = Database connectionString
    Selector = AllModules
    EmissionStrategy = Diagnostics {
        Directory = "./out"
        Artifacts = [ModelJson; ExtractionLog]
    }
    InsertionStrategy = None  // Export-only
    Transforms = TransformRegistry.Empty  // No transforms needed
    Configuration = {
        OutputDirectory = "./out"
        EntitySelector = AllModules
        // ... minimal config
    }
}

let! result = extractModelPipeline.Execute(ct)
// Result: ExtractionCompleted { Model, Metrics }
```

**Use Case 2: Schema-Only (SSDT Modules)**

```fsharp
let schemaPipeline = {
    Spine = SchemaSpine SchemaInitialized
    Source = Database connectionString
    Selector = AllModules
    EmissionStrategy = Combined [
        SchemaPerModule {
            Directory = "./out/Modules"
            Ordering = ModuleThenAlphabetical
        }
        Diagnostics {
            Directory = "./out/Diagnostics"
            Artifacts = [ProfilingReport; ValidationResults]
        }
    ]
    InsertionStrategy = SchemaOnly
    Transforms = TransformRegistry.Create [
        TypeMappingPolicy
        NullabilityEvaluator
        DeferredForeignKeyDetector
    ]
    Configuration = {
        OutputDirectory = "./out"
        EntitySelector = AllModules
        TighteningOptions = cautious
        // ...
    }
}

let! result = schemaPipeline.Execute(ct)
// Result: SchemaEmitted { SchemaArtifacts, Decisions, Profile }
```

**Use Case 3: Static Seeds Data**

```fsharp
let staticSeedsPipeline = {
    Spine = FullSpine FullPipelineInitialized
    Source = Database connectionString
    Selector = Predicate (fun entity -> entity.DataKind = StaticEntity)
    EmissionStrategy = Combined [
        DataMonolithic {
            Path = "./out/StaticSeeds/StaticSeeds.sql"
            Ordering = GlobalFkSafe  // Global sort, filter to static for emission
        }
        Diagnostics {
            Directory = "./out/Diagnostics"
            Artifacts = [ProfilingReport; CycleDiagnostics]
        }
    ]
    InsertionStrategy = Merge {
        On = PrimaryKey
        BatchSize = 1000
    }
    Transforms = TransformRegistry.Create [
        EntitySeedDeterminizer
        EntityDependencySorter  // Global topological sort
        StaticSeedForeignKeyPreflight
    ]
    Configuration = {
        OutputDirectory = "./out"
        EntitySelector = FilteredModules { ... }
        CircularDependencyOptions = Some allowedCycles
        // ...
    }
}

let! result = staticSeedsPipeline.Execute(ct)
// Result: PipelineCompleted { DataArtifacts = { StaticSeeds = Some path }, ... }
```

**Use Case 4: Bootstrap (All Entities, INSERT)**

```fsharp
let bootstrapPipeline = {
    Spine = FullSpine FullPipelineInitialized
    Source = Database connectionString
    Selector = AllWithData  // Static + regular entities with data
    EmissionStrategy = Combined [
        DataMonolithic {
            Path = "./out/Bootstrap/Bootstrap.sql"
            Ordering = GlobalFkSafe
        }
        Diagnostics {
            Directory = "./out/Diagnostics"
            Artifacts = [ProfilingReport; CycleDiagnostics; TransformExecutionLog]
        }
    ]
    InsertionStrategy = Insert {
        BatchSize = 1000
        NonDestructive = true
    }
    Transforms = TransformRegistry.Create [
        EntitySeedDeterminizer
        EntityDependencySorter
        TopologicalOrderingValidator  // Enhanced cycle diagnostics
    ]
    Configuration = {
        OutputDirectory = "./out"
        EntitySelector = AllModules
        CircularDependencyOptions = Some allowedCycles
        // ...
    }
}

let! result = bootstrapPipeline.Execute(ct)
// Result: PipelineCompleted { DataArtifacts = { Bootstrap = Some path }, ... }
```

**Use Case 5: Full-Export (Schema + StaticSeeds + Bootstrap)**

```fsharp
let fullExportPipeline = {
    Spine = FullSpine FullPipelineInitialized
    Source = Database connectionString
    Selector = AllModules
    EmissionStrategy = Combined [
        SchemaPerModule {
            Directory = "./out/Modules"
            Ordering = ModuleThenAlphabetical
        }
        DataMonolithic {
            Path = "./out/StaticSeeds/StaticSeeds.sql"
            Ordering = GlobalFkSafe
        }
        DataMonolithic {
            Path = "./out/Bootstrap/Bootstrap.sql"
            Ordering = GlobalFkSafe
        }
        Diagnostics {
            Directory = "./out/Diagnostics"
            Artifacts = [ProfilingReport; ValidationResults; CycleDiagnostics; PipelineManifest]
        }
    ]
    InsertionStrategy = Combined [
        SchemaOnly
        Merge { On = PrimaryKey; BatchSize = 1000 }  // StaticSeeds
        Insert { BatchSize = 1000; NonDestructive = true }  // Bootstrap
    ]
    Transforms = TransformRegistry.Create [
        // All transforms
        TypeMappingPolicy
        NullabilityEvaluator
        EntitySeedDeterminizer
        EntityDependencySorter
        TopologicalOrderingValidator
        DeferredForeignKeyDetector
    ]
    Configuration = {
        OutputDirectory = "./out"
        EntitySelector = AllModules
        TighteningOptions = cautious
        CircularDependencyOptions = Some allowedCycles
        // ...
    }
}

let! result = fullExportPipeline.Execute(ct)
// Result: PipelineCompleted {
//     SchemaArtifacts = Some { ... },
//     DataArtifacts = { StaticSeeds = Some ...; Bootstrap = Some ... },
//     Diagnostics = { ... }
// }
```

**Use Case 6: Full-Export with UAT-Users (Dual-Mode Transform)**

```fsharp
let uatExportPipeline = {
    Spine = FullSpine FullPipelineInitialized
    Source = Database connectionString
    Selector = AllModules
    EmissionStrategy = Combined [
        SchemaPerModule { Directory = "./out/Modules"; Ordering = ModuleThenAlphabetical }
        DataMonolithic { Path = "./out/StaticSeeds/StaticSeeds.sql"; Ordering = GlobalFkSafe }
        DataMonolithic { Path = "./out/DynamicData/DynamicData.sql"; Ordering = GlobalFkSafe }  // UAT-transformed
        Diagnostics { Directory = "./out/Diagnostics"; Artifacts = [All] }
    ]
    InsertionStrategy = Combined [
        SchemaOnly
        Merge { On = PrimaryKey; BatchSize = 1000 }
        Insert { BatchSize = 1000; NonDestructive = true }
    ]
    Transforms = TransformRegistry.Create [
        // Standard transforms
        TypeMappingPolicy
        NullabilityEvaluator
        EntitySeedDeterminizer
        EntityDependencySorter

        // UAT-Users dual-mode transform
        {
            Name = "UAT-Users"
            Kind = DualMode
            Stage = MultiStage(DiscoveryStage = Stage3, ApplicationStage = Stage5)
            IsProtectedCitizen = false
            OrderingConstraints = { MustRunBefore = []; MustRunAfter = ["FK catalog discovery"] }
            EnabledPredicate = (fun config -> config.EnableUatUsers)
            Implementation = UatUsersTransform {
                QaInventory = "./qa_users.csv"
                UatInventory = "./uat_users.csv"
                Mapping = "./user_map.csv"
                Mode = PreTransformedInserts  // Stage 5 application
            }
        }
    ]
    Configuration = {
        OutputDirectory = "./out"
        EntitySelector = AllModules
        EnableUatUsers = true
        UatUsersConfig = Some {
            QaUserInventoryPath = "./qa_users.csv"
            UatUserInventoryPath = "./uat_users.csv"
            UserMappingPath = "./user_map.csv"
        }
        // ...
    }
}

let! result = uatExportPipeline.Execute(ct)
// Result: PipelineCompleted {
//     SchemaArtifacts = Some { ... },
//     DataArtifacts = {
//         StaticSeeds = Some ...;
//         DynamicData = Some ...  // Pre-transformed user FKs (UAT-ready)
//     },
//     Diagnostics = { ... }
// }
```

**The Pattern:**

Every use case is:
```
[Spine] + [Source] + [Selector] + [EmissionStrategy] + [InsertionStrategy] + [Transforms] = Pipeline
```

Same invariant flow. Different parameters. No implementation forks.

---

### § 13. Stage Execution Model

**Stages are Functions (Pure, Testable):**

```fsharp
// Stage 0: Extract-Model (optional - integrated)
module Stage0_ExtractModel =
    type Input = {
        Source: DataSource
        Selector: EntitySelector
        Options: FetchOptions
    }

    type Output = {
        Snapshot: DatabaseSnapshot
        Metrics: ExtractionMetrics
    }

    let execute (input: Input) (diagnostics: DiagnosticEmitter) (ct: CancellationToken) : Async<Result<Output>> =
        async {
            diagnostics.EmitStageBegin("Extract-Model")
            // ... fetch logic
            diagnostics.EmitStageComplete("Extract-Model", duration)
            return Success { Snapshot = snapshot; Metrics = metrics }
        }

// Stage 1: Selection
module Stage1_Selection =
    type Input = {
        Model: OsmModel
        Selector: EntitySelector
    }

    type Output = {
        SelectedEntities: EntityModel list
        Criteria: SelectionCriteria
    }

    let execute (input: Input) (diagnostics: DiagnosticEmitter) (ct: CancellationToken) : Async<Result<Output>> =
        async {
            diagnostics.EmitStageBegin("Selection")

            let selected = match input.Selector with
                | AllModules -> input.Model.Modules |> List.collect (fun m -> m.Entities)
                | FilteredModules options -> applyModuleFilter input.Model options
                | Predicate pred -> input.Model.Modules |> List.collect (fun m -> m.Entities) |> List.filter pred
                | AllWithData -> failwith "Requires data snapshot"  // Error - needs Stage 2
                | Custom selector -> selector input.Model

            diagnostics.EmitStageComplete("Selection", duration)
            return Success { SelectedEntities = selected; Criteria = criteriaFrom input.Selector }
        }

// Stage 2: Fetch (unified database snapshot)
module Stage2_Fetch =
    type Input = {
        Source: DataSource
        SelectedEntities: EntityModel list
        Options: FetchOptions
    }

    type Output = {
        Snapshot: DatabaseSnapshot
        Metrics: FetchMetrics
    }

    let execute (input: Input) (diagnostics: DiagnosticEmitter) (ct: CancellationToken) : Async<Result<Output>> =
        async {
            diagnostics.EmitStageBegin("Fetch")

            // Unified fetch (eliminates triple-fetch)
            let! snapshot = match input.Source with
                | Database connString ->
                    DatabaseSnapshotRepository.Fetch {
                        ConnectionString = connString
                        EntitySelection = EntitySelector.FromEntities input.SelectedEntities
                        Options = input.Options
                    }
                | CachedSnapshot key ->
                    DatabaseSnapshotRepository.LoadCached key
                | PreExtractedModel filePath ->
                    // Load model JSON, create snapshot with no stats/data
                    loadModelAsSnapshot filePath

            diagnostics.EmitStageComplete("Fetch", duration)
            return snapshot |> Result.map (fun s -> { Snapshot = s; Metrics = metricsFrom s })
        }

// Stage 3: Transform (composable transforms)
module Stage3_Transform =
    type Input = {
        Snapshot: DatabaseSnapshot
        Registry: TransformRegistry
        Configuration: PipelineConfiguration
    }

    type Output = {
        TransformedSnapshot: DatabaseSnapshot
        TransformationContexts: Map<string, TransformationContext>  // For dual-mode (UAT-Users)
        ExecutionLog: TransformExecutionLog
    }

    let execute (input: Input) (diagnostics: DiagnosticEmitter) (ct: CancellationToken) : Async<Result<Output>> =
        async {
            diagnostics.EmitStageBegin("Transform")

            // Compose transforms (topological ordering by dependencies)
            let! compositionResult = TransformComposer.compose input.Registry input.Configuration

            match compositionResult with
            | Failure errors -> return Failure errors
            | Success plan ->
                // Execute transforms in order (parallel groups concurrently)
                let! executionResult = TransformComposer.execute plan.ExecutionPlan (CombinedTransform (input.Snapshot.ToModel(), input.Snapshot.ToDataSet(), input.Snapshot.ToProfile()))

                diagnostics.EmitStageComplete("Transform", duration)

                return executionResult |> Result.map (fun output -> {
                    TransformedSnapshot = applyOutputToSnapshot output input.Snapshot
                    TransformationContexts = extractContexts output  // UAT-Users context, etc.
                    ExecutionLog = plan.ExecutionLog
                })
        }

// Stage 4: Order (topological sort - data pipelines only)
module Stage4_Order =
    type Input = {
        Snapshot: DatabaseSnapshot
        Options: EntityDependencySortOptions
        CircularDependencyOptions: CircularDependencyOptions option
    }

    type Output = {
        TopologicalOrder: EntityModel list  // FK-safe order
        Cycles: CycleWarning list
        Metrics: OrderingMetrics
    }

    let execute (input: Input) (diagnostics: DiagnosticEmitter) (ct: CancellationToken) : Async<Result<Output>> =
        async {
            diagnostics.EmitStageBegin("Order")

            // Global topological sort (all selected entities, not scoped to category)
            let ordering = EntityDependencySorter.sortByForeignKeys
                input.Snapshot.ToDataSet().Tables
                (Some input.Snapshot.ToModel())
                None  // No naming overrides
                input.Options
                input.CircularDependencyOptions
                (Some diagnostics)  // Diagnostic collection

            // Validate ordering
            let validation = TopologicalOrderingValidator.validate
                ordering.Tables
                input.Snapshot.ToModel()
                input.CircularDependencyOptions

            diagnostics.EmitStageComplete("Order", duration)

            return Success {
                TopologicalOrder = ordering.Tables |> List.map (fun t -> findEntity t.Definition)
                Cycles = validation.Cycles
                Metrics = { Nodes = ordering.NodeCount; Edges = ordering.EdgeCount; ... }
            }
        }

// Stage 5: Emit (artifact generation)
module Stage5_Emit =
    type Input = {
        Snapshot: DatabaseSnapshot
        TopologicalOrder: EntityModel list option  // None for schema-only
        Decisions: PolicyDecisionSet
        EmissionStrategy: EmissionStrategy
        TransformationContexts: Map<string, TransformationContext>  // For dual-mode transforms
    }

    type Output = {
        SchemaArtifacts: SchemaArtifacts option
        DataArtifacts: DataArtifacts option
        Diagnostics: DiagnosticArtifacts
    }

    let execute (input: Input) (diagnostics: DiagnosticEmitter) (ct: CancellationToken) : Async<Result<Output>> =
        async {
            diagnostics.EmitStageBegin("Emit")

            // Parse emission strategy (compositional)
            let strategies = flattenEmissionStrategy input.EmissionStrategy

            let mutable schemaArtifacts = None
            let mutable dataArtifacts = None
            let mutable diagnosticArtifacts = None

            for strategy in strategies do
                match strategy with
                | SchemaPerModule config ->
                    let! schema = ScriptGenerator.generateSchema input.Snapshot.ToModel() input.Decisions config.Ordering
                    schemaArtifacts <- Some schema

                | DataMonolithic config ->
                    match input.TopologicalOrder with
                    | None -> return Failure [ValidationError.Create "Data emission requires topological order"]
                    | Some order ->
                        // Apply transformation contexts (e.g., UAT-Users pre-transformation)
                        let transformedData = applyTransformationContexts input.Snapshot.ToDataSet() input.TransformationContexts
                        let! data = ScriptGenerator.generateInserts transformedData order config
                        dataArtifacts <- Some data

                | Diagnostics config ->
                    diagnostics.EmitDiagnosticArtifacts(config.Directory)
                    diagnosticArtifacts <- Some { ... }

                | Combined strategies ->
                    // Recursively handle nested strategies
                    ()

            diagnostics.EmitStageComplete("Emit", duration)

            return Success {
                SchemaArtifacts = schemaArtifacts
                DataArtifacts = dataArtifacts
                Diagnostics = diagnosticArtifacts.Value
            }
        }

// Stage 6: Apply (insertion semantics)
module Stage6_Apply =
    type Input = {
        InsertionStrategy: InsertionStrategy
        Artifacts: EmittedArtifacts
    }

    type Output = {
        ApplicationResult: ApplicationResult option  // None if export-only
        EstimatedImpact: ImpactEstimate
    }

    let execute (input: Input) (diagnostics: DiagnosticEmitter) (ct: CancellationToken) : Async<Result<Output>> =
        async {
            diagnostics.EmitStageBegin("Apply")

            // Define semantics (don't necessarily execute)
            let impact = estimateImpact input.Artifacts input.InsertionStrategy

            // If deployment requested (future: --deploy flag), execute here
            let applicationResult = None  // Currently export-only

            diagnostics.EmitStageComplete("Apply", duration)

            return Success {
                ApplicationResult = applicationResult
                EstimatedImpact = impact
            }
        }
```

**Pipeline Orchestration (Compose Stages):**

```fsharp
module PipelineOrchestrator =
    // Full pipeline composition
    let executeFullPipeline
        (config: PipelineConfiguration)
        (diagnostics: DiagnosticEmitter)
        (ct: CancellationToken)
        : Async<Result<PipelineCompletedState>> =

        async {
            // Stage 0 (optional - integrated)
            let! stage0 = Stage0_ExtractModel.execute {
                Source = config.Source
                Selector = config.EntitySelector
                Options = { IncludeStructure = true; IncludeStatistics = true; IncludeRowData = true }
            } diagnostics ct

            // Railway-oriented composition (short-circuit on error)
            return!
                stage0
                |> Result.bindAsync (fun s0 ->
                    // Stage 1: Selection
                    Stage1_Selection.execute {
                        Model = s0.Snapshot.ToModel()
                        Selector = config.EntitySelector
                    } diagnostics ct
                )
                |> Result.bindAsync (fun s1 ->
                    // Stage 2: Fetch (may skip if Stage 0 fetched everything)
                    async { return Success s0.Snapshot }  // Already have snapshot
                )
                |> Result.bindAsync (fun s2 ->
                    // Stage 3: Transform
                    Stage3_Transform.execute {
                        Snapshot = s2
                        Registry = config.Transforms
                        Configuration = config
                    } diagnostics ct
                )
                |> Result.bindAsync (fun s3 ->
                    // Stage 4: Order (data pipelines only)
                    if requiresTopologicalSort config.EmissionStrategy then
                        Stage4_Order.execute {
                            Snapshot = s3.TransformedSnapshot
                            Options = config.SortOptions
                            CircularDependencyOptions = config.CircularDependencyOptions
                        } diagnostics ct
                    else
                        async { return Success { TopologicalOrder = []; Cycles = []; Metrics = OrderingMetrics.Skip } }
                )
                |> Result.bindAsync (fun s4 ->
                    // Stage 5: Emit
                    Stage5_Emit.execute {
                        Snapshot = s3.TransformedSnapshot
                        TopologicalOrder = Some s4.TopologicalOrder
                        Decisions = s3.Decisions  // From PolicyDecisionStep (transform)
                        EmissionStrategy = config.EmissionStrategy
                        TransformationContexts = s3.TransformationContexts
                    } diagnostics ct
                )
                |> Result.bindAsync (fun s5 ->
                    // Stage 6: Apply
                    Stage6_Apply.execute {
                        InsertionStrategy = config.InsertionStrategy
                        Artifacts = { Schema = s5.SchemaArtifacts; Data = s5.DataArtifacts }
                    } diagnostics ct
                )
                |> Result.map (fun s6 ->
                    // Final state
                    {
                        Base = { Request = config; Log = diagnostics.GetLog(); StartedAtUtc = startTime }
                        Model = s0.Snapshot.ToModel()
                        Profile = s0.Snapshot.ToProfile()
                        Decisions = s3.Decisions
                        SchemaArtifacts = s5.SchemaArtifacts
                        DataArtifacts = s5.DataArtifacts
                        Diagnostics = s5.Diagnostics
                    }
                )
        }
```

---

## PART IV: ARCHITECTURAL INVARIANTS (The Laws)

### § 14. Immutability Law

**LAW:** All domain objects are immutable. State changes produce new objects.

**Enforcement:**
- F# record types are immutable by default
- ImmutableArray, ImmutableList, ImmutableMap for collections
- No mutable fields in domain types

**Violations:**
```fsharp
// ILLEGAL: Mutable field
type EntityModel = {
    mutable Name: EntityName  // ❌ FORBIDDEN
}

// LEGAL: New object on change
type EntityModel = {
    Name: EntityName
}
with
    member this.WithName(newName: EntityName) =
        { this with Name = newName }  // ✅ Returns new instance
```

---

### § 15. Aggregate Boundary Law

**LAW:** Child entities cannot be accessed without going through aggregate root.

**Enforcement:**
- Child entities not exposed as public types outside aggregate module
- Aggregate root provides query methods (FindAttribute, FindIndex, etc.)
- No repository interfaces for child entities

**Violations:**
```fsharp
// ILLEGAL: Direct child entity access
let attribute = attributeRepository.FindById(id)  // ❌ No such repository

// LEGAL: Access through aggregate
let attribute = entityModel.FindAttribute(attributeName)  // ✅ Via aggregate
```

---

### § 16. Topological Order Law (Data Pipelines)

**LAW:** Data emission MUST use global topological sort across ALL selected entities.

**Enforcement:**
- EntityDependencySorter is protected citizen for data pipelines
- Compiler error if DataMonolithic emitted without Stage4_Order
- Test failure if scoped sort (static-only, regular-only) detected

**Violations:**
```fsharp
// ILLEGAL: Scoped sort (static entities only)
let staticOnly = entities |> List.filter (fun e -> e.DataKind = StaticEntity)
let sorted = EntityDependencySorter.sortByForeignKeys staticOnly model  // ❌ Misses cross-category FKs

// LEGAL: Global sort, filter after
let allEntities = entities  // ALL selected entities
let sorted = EntityDependencySorter.sortByForeignKeys allEntities model  // ✅ Global graph
let staticSorted = sorted |> List.filter (fun e -> e.DataKind = StaticEntity)  // ✅ Filter after sort
```

---

### § 17. Schema Ordering Law

**LAW:** Schema emission uses deterministic ordering (alphabetical), NOT topological.

**Rationale:** DDL is declarative. CREATE TABLE order doesn't matter. FK constraints use WITH NOCHECK for deferred validation.

**Enforcement:**
- SchemaPerModule emission uses DeterministicOrdering (not TopologicalOrdering)
- Type system prevents TopologicalOrdering in SchemaPerModule
- .sqlproj files list scripts alphabetically (reproducible diffs)

---

### § 18. Transform Ordering Law

**LAW:** Transforms execute in topologically sorted order by dependency constraints.

**Enforcement:**
- TransformComposer validates ordering (detects cycles)
- Registry construction fails if circular dependencies detected
- Test suite validates all transforms have explicit ordering constraints

**Violations:**
```fsharp
// ILLEGAL: Missing ordering constraints
{
    Name = "MyTransform"
    OrderingConstraints = { MustRunBefore = []; MustRunAfter = [] }  // ❌ Unordered
}

// LEGAL: Explicit constraints
{
    Name = "NullabilityEvaluator"
    OrderingConstraints = {
        MustRunBefore = ["Emit"]
        MustRunAfter = ["TypeMappingPolicy"; "Profiling"]  // ✅ Explicit dependencies
    }
}
```

---

### § 19. Error Handling Law

**LAW:** Domain operations return Result<T>, never throw exceptions.

**Rationale:** Railway-oriented programming. Errors are data, not control flow.

**Enforcement:**
- All factory methods return Result<T>
- All domain services return Result<T> or Async<Result<T>>
- Exceptions only for truly exceptional conditions (programmer errors, not domain errors)

**Violations:**
```fsharp
// ILLEGAL: Throwing exception for domain error
let create (name: string) =
    if String.IsNullOrEmpty(name) then
        raise (ArgumentException "Name cannot be empty")  // ❌ Exception for validation

// LEGAL: Returning Result
let create (name: string) : Result<EntityName> =
    if String.IsNullOrEmpty(name) then
        Failure [ValidationError.Create "Name cannot be empty"]  // ✅ Error as data
    else
        Success (EntityName name)
```

---

### § 20. Single Responsibility Law (States)

**LAW:** State types have at most 10 properties. Larger states must be decomposed.

**Rationale:** Prevent god objects. Each state should represent a cohesive concept.

**Enforcement:**
- Code review rejects states with >10 properties
- Extract sub-states into separate types if needed

**Example Refactoring:**
```fsharp
// BEFORE: God object (15 properties)
type PipelineCompletedState = {
    Model: OsmModel
    Profile: ProfileSnapshot
    Decisions: PolicyDecisionSet
    SchemaArtifacts: SchemaArtifacts option
    DataArtifacts: DataArtifacts
    Diagnostics: DiagnosticArtifacts
    Metrics: PipelineMetrics
    // ... 8 more properties  ❌ Too many
}

// AFTER: Decomposed
type PipelineCompletedState = {
    Base: PipelineState  // Request, Log, StartedAtUtc
    DomainState: DomainStateBundle  // Model, Profile, Decisions
    Artifacts: ArtifactBundle  // Schema, Data, Diagnostics
    Metrics: PipelineMetrics
}  // ✅ 4 properties, each conceptually cohesive
```

---

## PART V: IMPLEMENTATION GUIDANCE

### § 21. Migration Path from Current Codebase

**Phase 1: Foundation (Weeks 1-2)**

1. **Introduce Functional Primitives**
   - Add Option<T> type (currently missing)
   - Add Validation<T> applicative (for error accumulation)
   - Extend Result<T> with missing combinators (Traverse, Sequence)

2. **Extract Domain Services to Domain Namespace**
   - Move ModuleFilter from Pipeline to Domain
   - Move EntityDependencySorter to Domain.Services
   - Mark domain services with IDomainService interface

3. **Introduce Marker Interfaces**
   - IAggregateRoot (OsmModel, ModuleModel, EntityModel)
   - IEntity (AttributeModel, IndexModel, etc.)
   - IValueObject (all record types in ValueObjects namespace)

**Phase 2: Multi-Spine State System (Weeks 3-4)**

1. **Extract State Spines**
   - Create ExtractionSpine (minimal)
   - Create SchemaSpine (schema-only)
   - Keep FullPipelineSpine (current linear spine)

2. **Refactor Verbs to Use Appropriate Spine**
   - extract-model → ExtractionSpine
   - build-ssdt modules → SchemaSpine
   - build-ssdt, full-export → FullPipelineSpine

3. **Validate Type Safety**
   - extract-model cannot depend on BootstrapSnapshotGenerated (compiler error)

**Phase 3: Transform Registry (Weeks 5-6)**

1. **Excavate All Transforms**
   - Systematic grep for transform patterns
   - Document each transform (name, kind, stage, ordering)
   - Build initial transform catalog

2. **Create Transform Registry**
   - Implement TransformRegistry type
   - Implement TransformComposer (topological ordering)
   - Add coverage tests (fail if unregistered transform)

3. **Register Existing Transforms**
   - EntitySeedDeterminizer, EntityDependencySorter, etc.
   - UAT-Users (dual-mode exemplar)

**Phase 4: DatabaseSnapshot Unification (Weeks 7-8)**

1. **Implement DatabaseSnapshot Type**
   - Metadata + Statistics + Data in one structure
   - Partial fetch options (IncludeStructure, IncludeStatistics, IncludeRowData)

2. **Eliminate Triple-Fetch**
   - Refactor SqlModelExtractionService, SqlDataProfiler, SqlDynamicEntityDataProvider
   - Share OSSYS_* query construction
   - Implement checksum-based caching

3. **Verify Performance**
   - OSSYS_* queried once (not 2-3x)
   - Cache hit makes second run 10x faster

**Phase 5: Emission Strategy Unification (Weeks 9-10)**

1. **Extract EmissionStrategy Abstraction**
   - SchemaPerModule, DataMonolithic, DataPerModuleWithSqlProj, Diagnostics, Combined

2. **Unify Schema and Data Emission**
   - Consolidate file organization logic
   - Type-enforce ordering regimes (DeterministicOrdering vs TopologicalOrdering)

3. **Implement Combined Emission**
   - full-export emits schema + data + diagnostics in single invocation

**Phase 6: Global Topological Sort (Weeks 11-12)**

1. **Extract TopologicalOrderingValidator from Bootstrap**
   - Make reusable across all data pipelines

2. **Refactor StaticSeeds to Use Global Sort**
   - Sort ALL selected entities (static + regular)
   - Filter to static after sort
   - Verify FK violations eliminated

3. **Add Regression Tests**
   - StaticEntity1 references RegularEntity1 → correct ordering maintained

**Phase 7: EntityFilters Wiring (Weeks 13-14)**

1. **Wire EntityFilters into SQL Extraction**
   - SqlClientOutsystemsMetadataReader adds WHERE clause filtering

2. **Wire EntityFilters into Profiling**
   - SqlDataProfiler scopes queries to selected entities

3. **Verify Supplemental Obsolescence**
   - EntitySelector.Include("ServiceCenter", ["User"]) works end-to-end
   - Delete SupplementalEntityLoader.cs

**Phase 8: Diagnostic Channels (Weeks 15-16)**

1. **Implement IOperatorChannel (Spectre.Console)**
2. **Implement IAuditorChannel (Persistent Artifacts)**
3. **Implement IDeveloperChannel (Debug Logging)**
4. **Integrate DiagnosticEmitter into All Steps**

**Phase 9: Unified Pipeline API (Weeks 17-18)**

1. **Implement EntityPipeline.Execute**
   - Parameterized by Spine, Source, Selector, EmissionStrategy, InsertionStrategy, Transforms

2. **Refactor Verbs to Use Unified API**
   - extract-model, build-ssdt, full-export all become parameter sets

3. **Validate: No Implementation Forks**
   - BuildSsdtModulesStep, BuildSsdtStaticSeedStep, BuildSsdtBootstrapSnapshotStep → deleted
   - Replaced by EntityPipeline.Execute with different configs

---

### § 22. Testing Strategy

**Unit Tests (Domain Logic):**

```fsharp
module EntityModelTests =
    [<Fact>]
    let ``Create rejects entity with no attributes`` () =
        let result = EntityModel.Create (EntityName "Test") (TableName "Test") []

        match result with
        | Failure errors ->
            Assert.Contains("at least one attribute", errors.[0].Message)
        | Success _ ->
            Assert.Fail("Should reject empty attributes")

    [<Fact>]
    let ``FindAttribute returns attribute by name`` () =
        let attribute = AttributeModel.Create (AttributeName "Id") (ColumnName "Id") "INTEGER" |> Result.unwrap
        let entity = EntityModel.Create (EntityName "User") (TableName "User") [attribute] |> Result.unwrap

        let found = entity.FindAttribute (AttributeName "Id")

        match found with
        | Success attr -> Assert.Equal(attribute, attr)
        | Failure _ -> Assert.Fail("Should find attribute")
```

**Integration Tests (Pipeline Stages):**

```fsharp
module Stage4_OrderTests =
    [<Fact>]
    let ``Global topological sort respects cross-category FK dependencies`` () =
        // Arrange
        let staticEntity = createStaticEntity "Role" [idAttribute]
        let regularEntity = createRegularEntity "User" [idAttribute; roleIdAttribute]  // FK to Role (static)
        let model = createModel [staticEntity; regularEntity]

        // Act
        let ordering = EntityDependencySorter.sortByForeignKeys [staticEntity; regularEntity] (Some model) None None None None

        // Assert
        let roleIndex = ordering.Tables |> List.findIndex (fun t -> t.Definition.Name = "Role")
        let userIndex = ordering.Tables |> List.findIndex (fun t -> t.Definition.Name = "User")

        Assert.True(roleIndex < userIndex, "Role (static) must come before User (regular) due to FK")
```

**Property-Based Tests (Invariants):**

```fsharp
module TopologicalSortProperties =
    open FsCheck.Xunit

    [<Property>]
    let ``Topological sort preserves all entities`` (entities: EntityModel list) =
        let model = createModelFrom entities
        let ordering = EntityDependencySorter.sortByForeignKeys entities (Some model) None None None None

        // Property: Output has same entities as input (no loss)
        Set.ofList ordering.Tables = Set.ofList entities

    [<Property>]
    let ``Topological sort places FK targets before sources`` (entities: EntityModel list) =
        let model = createModelFrom entities
        let ordering = EntityDependencySorter.sortByForeignKeys entities (Some model) None None None None

        // Property: For every FK, target appears before source in ordering
        let fks = extractAllForeignKeys entities
        fks |> List.forall (fun fk ->
            let sourceIndex = ordering.Tables |> List.findIndex (fun t -> t = fk.Source)
            let targetIndex = ordering.Tables |> List.findIndex (fun t -> t = fk.Target)
            targetIndex < sourceIndex
        )
```

**Acceptance Tests (End-to-End):**

```fsharp
module FullExportAcceptanceTests =
    [<Fact>]
    let ``Full-export produces all artifacts`` () =
        // Arrange
        let config = {
            Spine = FullSpine FullPipelineInitialized
            Source = Database testConnectionString
            Selector = AllModules
            EmissionStrategy = Combined [
                SchemaPerModule { Directory = "./test-out/Modules"; Ordering = ModuleThenAlphabetical }
                DataMonolithic { Path = "./test-out/StaticSeeds/StaticSeeds.sql"; Ordering = GlobalFkSafe }
                DataMonolithic { Path = "./test-out/Bootstrap/Bootstrap.sql"; Ordering = GlobalFkSafe }
                Diagnostics { Directory = "./test-out/Diagnostics"; Artifacts = [All] }
            ]
            InsertionStrategy = Combined [SchemaOnly; Merge { On = PrimaryKey; BatchSize = 1000 }; Insert { BatchSize = 1000; NonDestructive = true }]
            Transforms = TransformRegistry.Default
            Configuration = testConfiguration
        }

        // Act
        let result = EntityPipeline(config).Execute(CancellationToken.None) |> Async.RunSynchronously

        // Assert
        match result with
        | Success state ->
            Assert.True(File.Exists("./test-out/Modules/User/User.table.sql"))
            Assert.True(File.Exists("./test-out/StaticSeeds/StaticSeeds.sql"))
            Assert.True(File.Exists("./test-out/Bootstrap/Bootstrap.sql"))
            Assert.True(File.Exists("./test-out/Diagnostics/profiling-report.csv"))
            Assert.True(File.Exists("./test-out/Diagnostics/pipeline-manifest.json"))
        | Failure errors ->
            Assert.Fail($"Pipeline failed: {errors}")
```

---

## CONCLUSION: The Ontological Foundation

This document defines **what the system IS**, not how to build it.

**Key Principles:**

1. **Immutability**: All domain objects immutable. State changes produce new objects.
2. **Type Safety**: Compiler enforces correctness. Illegal states unrepresentable.
3. **Composition**: Pipelines ARE composition. Steps chain via monadic bind.
4. **Explicit Boundaries**: Bounded contexts have clear ubiquitous language.
5. **Partial Participation**: Multiple spines prevent type bloat.
6. **Transform Registry**: Unknown unknowns become catchable errors.
7. **Diagnostic Channels**: Orthogonal observability for three audiences.
8. **Parameterization**: Every use case is same flow with different parameters.

**The Laws:**

- Immutability Law (§14)
- Aggregate Boundary Law (§15)
- Topological Order Law (§16)
- Schema Ordering Law (§17)
- Transform Ordering Law (§18)
- Error Handling Law (§19)
- Single Responsibility Law (§20)

**Implementation violating these laws is architecturally invalid.**

The compiler enforces type safety. This constitution enforces **ontological safety**.

---

*End of Domain Model Constitution. The foundation is laid. Implementation may now begin.*
