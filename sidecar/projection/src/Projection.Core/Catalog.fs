namespace Projection.Core

// LINT-ALLOW-FILE: validation-error message construction in
// `Module.create` / `Catalog.create` / `Reference.attach` smart
// constructors uses `sprintf "...%A..."` to interpolate typed
// `SsKey` values. The `%A` formatter is F#'s closed-DU structural
// pretty-printer — the canonical "stringify a typed value" surface.
// Per `DECISIONS 2026-05-09 — Built-in obligation`, this is the
// allowed exception (no BCL alternative emits closed-DU
// pretty-printed text). Audit-deferred Tier-2 #14 names typed
// `Outcome.toDiagnosticString` per DU as the eventual home; the
// same shape would absorb these. Until then, `sprintf "%A"` IS
// the typed pretty-print.

/// Presentation name for a catalog node. Names are policy-transformed (A15)
/// and never participate in identity (A2). The DU prevents accidental
/// confusion with `SsKey`.
type Name = Name of string

/// Construction and inspection helpers for `Name`.
[<RequireQualifiedAccess>]
module Name =

    let private nameEmpty =
        ValidationError.create "name.empty" "A name cannot be blank."

    /// Build a `Name`. Rejects blank input.
    let create (value: string) : Result<Name> =
        if System.String.IsNullOrWhiteSpace value then Result.failureOf nameEmpty
        else Result.success (Name value)

    /// Project the underlying string. Presentation only.
    let value (Name v) : string = v


/// A9: closed three-way origin discriminant. Widen only when evidence forces
/// it (a fourth source category would mean another reader and another set
/// of axioms about its provenance).
type Origin =
    /// OutSystems-native kind originated within the platform.
    | OsNative
    /// External kind exposed via Integration Studio's IS-functor.
    | ExternalViaIntegrationStudio
    /// External kind exposed by direct reader, no IS step.
    | ExternalDirect

/// Typed structural display for `Origin`. Tag-only variants;
/// renders to the variant name.
[<RequireQualifiedAccess>]
module Origin =
    let toStructured (o: Origin) : StructuredString =
        match o with
        | OsNative -> StructuredString.tag "OsNative"
        | ExternalViaIntegrationStudio -> StructuredString.tag "ExternalViaIntegrationStudio"
        | ExternalDirect -> StructuredString.tag "ExternalDirect"

    let toDiagnosticString (o: Origin) : string =
        toStructured o |> StructuredString.render


/// One row of a Static kind's population. Populations live in the catalog
/// per A7; the unfold pass lifts them into type-level metadata for Pi.
/// `Identifier` is the row's stable SsKey; `Values` carries cell values
/// keyed by attribute name. Cell values are kept as strings here — the
/// projection's type-correspondence policy (A13) determines how they are
/// rendered on the surface.
type StaticRow = {
    Identifier : SsKey
    Values     : Map<Name, string>
}


/// SQL Server "extended property" — a named string annotation attached to
/// a schema object. V1 source: `sys.extended_properties` (carried as
/// `ExtendedPropertyDocument` in V1's JSON projection). Chapter A.0' slice
/// ζ — IR fidelity lift (L3-S9 extended-properties sub-axiom). Mirrors
/// V1's `Osm.Domain.Model.ExtendedProperty` (Name + nullable Value).
type ExtendedProperty = {
    Name  : string
    Value : string option
}

/// Construction helper for `ExtendedProperty`. Empty `Value` strings
/// normalize to `None` per V1 parity (`ExtendedProperty.Create` collapses
/// `{ Length: 0 }` to null).
[<RequireQualifiedAccess>]
module ExtendedProperty =

    let private nameEmpty =
        ValidationError.create
            "extendedProperty.name.empty"
            "Extended property name cannot be blank."

    /// Smart constructor (A39). Rejects blank names; normalizes empty
    /// values to `None`. Pillar-9 classification: DataIntent — the
    /// value is observed source-schema metadata, no operator opinion.
    let create (name: string) (value: string option) : Result<ExtendedProperty> =
        if System.String.IsNullOrWhiteSpace name then Result.failureOf nameEmpty
        else
            let normalized =
                match value with
                | Some v when System.String.IsNullOrEmpty v -> None
                | other -> other
            Result.success { Name = name; Value = normalized }


/// SQL Server CHECK constraint at the table level. V1 source:
/// `AttributeOnDiskCheckConstraint` (Name + Definition + IsNotTrusted).
/// Chapter A.0' slice ε — IR fidelity lift (L3-S8 CHECK sub-axiom).
/// Lives at `Kind.ColumnChecks` (table-scoped; a CHECK may span multiple
/// columns per the SQL Server semantic).
type ColumnCheck = {
    SsKey        : SsKey
    Name         : Name option
    Definition   : string
    IsNotTrusted : bool
}

[<RequireQualifiedAccess>]
module ColumnCheck =

    let private definitionEmpty =
        ValidationError.create
            "columnCheck.definition.empty"
            "CHECK constraint definition cannot be blank."

    /// Smart constructor (A39). Rejects blank definitions; the name
    /// is optional (V1 surfaces unnamed CHECK constraints).
    let create
        (ssKey: SsKey)
        (name: Name option)
        (definition: string)
        (isNotTrusted: bool)
        : Result<ColumnCheck> =
        if System.String.IsNullOrWhiteSpace definition then
            Result.failureOf definitionEmpty
        else
            Result.success
                { SsKey        = ssKey
                  Name         = name
                  Definition   = definition.Trim()
                  IsNotTrusted = isNotTrusted }


/// SQL Server computed-column configuration. Carries the expression and
/// whether the computation is persisted. Chapter A.0' slice ε — IR
/// fidelity lift (L3-S7 computed-column sub-axiom). V1 source: V1's
/// model does not currently carry computed-column metadata at the
/// JSON-projection boundary; the V2 IR field is positioned for a
/// future DACPAC or rowset slice. Until populated, attributes carry
/// `Computed = None`.
type ComputedColumnConfig = {
    Expression  : string
    IsPersisted : bool
}

[<RequireQualifiedAccess>]
module ComputedColumnConfig =

    let private expressionEmpty =
        ValidationError.create
            "computedColumn.expression.empty"
            "Computed-column expression cannot be blank."

    let create (expression: string) (isPersisted: bool) : Result<ComputedColumnConfig> =
        if System.String.IsNullOrWhiteSpace expression then
            Result.failureOf expressionEmpty
        else
            Result.success
                { Expression  = expression.Trim()
                  IsPersisted = isPersisted }


/// SQL Server DML trigger. V1 source: V1's `TriggerModel` (Name +
/// IsDisabled + Definition) plus V1's JSON entity-level `triggers[]`
/// array. Chapter A.0' slice γ — IR fidelity lift (L3-S4 triggers
/// sub-axiom). Lives at `Kind.Triggers` per the domain semantic — a
/// trigger is owned by the table it fires on; the chapter open's
/// "Catalog.Triggers" planning shorthand is corrected to Kind-scoped
/// per pillar 8 (concept-shaped: a trigger IS a property of a kind).
type Trigger = {
    SsKey      : SsKey
    Name       : Name
    IsDisabled : bool
    Definition : string
}

[<RequireQualifiedAccess>]
module Trigger =

    let private definitionEmpty =
        ValidationError.create
            "trigger.definition.empty"
            "Trigger definition cannot be blank."

    /// Smart constructor (A39). Rejects blank definitions; V1's
    /// `TriggerModel.Create` enforces the same invariant.
    let create
        (ssKey: SsKey)
        (name: Name)
        (isDisabled: bool)
        (definition: string)
        : Result<Trigger> =
        if System.String.IsNullOrWhiteSpace definition then
            Result.failureOf definitionEmpty
        else
            Result.success
                { SsKey      = ssKey
                  Name       = name
                  IsDisabled = isDisabled
                  Definition = definition }


/// SQL Server SEQUENCE schema object. V1 source: V1's `SequenceModel`
/// (rich shape: schema/name/dataType/start/increment/min/max/cycle/cache).
/// Chapter A.0' slice δ — IR fidelity lift (L3-S5 sequences sub-axiom).
/// Top-level Catalog object (sequences are schema-scoped, not table-
/// scoped); `Catalog.Sequences : Sequence list`.
type SequenceCacheMode =
    /// No cache directive — server-default behavior.
    | Unspecified
    /// `CACHE n` cache size set.
    | Cache
    /// `NO CACHE` — caching disabled.
    | NoCache

type Sequence = {
    SsKey          : SsKey
    Name           : Name
    Schema         : string
    DataType       : string
    StartValue     : decimal option
    Increment      : decimal option
    Minimum        : decimal option
    Maximum        : decimal option
    IsCycleEnabled : bool
    CacheMode      : SequenceCacheMode
    CacheSize      : int option
}

[<RequireQualifiedAccess>]
module Sequence =

    let private schemaEmpty =
        ValidationError.create "sequence.schema.empty"
            "Sequence schema cannot be blank."
    let private dataTypeEmpty =
        ValidationError.create "sequence.dataType.empty"
            "Sequence data type cannot be blank."

    let create
        (ssKey: SsKey)
        (name: Name)
        (schema: string)
        (dataType: string)
        (startValue: decimal option)
        (increment: decimal option)
        (minimum: decimal option)
        (maximum: decimal option)
        (isCycleEnabled: bool)
        (cacheMode: SequenceCacheMode)
        (cacheSize: int option)
        : Result<Sequence> =
        if System.String.IsNullOrWhiteSpace schema then Result.failureOf schemaEmpty
        elif System.String.IsNullOrWhiteSpace dataType then Result.failureOf dataTypeEmpty
        else
            Result.success
                { SsKey          = ssKey
                  Name           = name
                  Schema         = schema.Trim()
                  DataType       = dataType.Trim()
                  StartValue     = startValue
                  Increment      = increment
                  Minimum        = minimum
                  Maximum        = maximum
                  IsCycleEnabled = isCycleEnabled
                  CacheMode      = cacheMode
                  CacheSize      = cacheSize }


/// SQL Server system-versioned temporal-table configuration. V1 source:
/// `TemporalTableMetadata` (HistorySchema/HistoryTable/PeriodStart/
/// PeriodEnd/Retention). Chapter A.0' slice η — IR fidelity lift,
/// closed-DU expansion (`ModalityMark.Temporal of TemporalConfig`).
/// The V1 model is rich; the V2 IR carries the operationally-relevant
/// subset: the period columns + the history-table coordinates + the
/// retention policy.
type TemporalRetentionUnit =
    | Days
    | Weeks
    | Months
    | Years

type TemporalRetention =
    /// `HISTORY_RETENTION_PERIOD = INFINITE`.
    | Infinite
    /// `HISTORY_RETENTION_PERIOD = <n> <unit>`.
    | Limited of value: int * unit: TemporalRetentionUnit

type TemporalConfig = {
    HistorySchema : string option
    HistoryTable  : string option
    PeriodStart   : Name option
    PeriodEnd     : Name option
    Retention     : TemporalRetention
}


/// Modality marks attached to a kind. A kind may carry multiple marks; the
/// representation is a list rather than a flag set so that payloaded marks
/// (Static) coexist cleanly with payload-free marks (TenantScoped,
/// SoftDeletable).
type ModalityMark =
    /// Schema-resident populations (A7).
    | Static of populations: StaticRow list
    /// Multi-tenant kind; rows are partitioned by the policy-defined tenant
    /// discriminator. The discriminator column itself is policy.
    | TenantScoped
    /// Logical deletes are represented in-row, not by physical removal.
    | SoftDeletable
    /// Stewardship marker (chapter 3.2 slice 4): the kind is owned by
    /// the V1 OutSystems platform rather than developer-authored.
    /// Sourced from V1's `ossys_Entity.Is_System` column (visible
    /// only through the rowset path; the JSON path drops the bit).
    /// Future consumers — emitters that exclude system tables from
    /// CREATE TABLE, passes that elide system-owned entities from
    /// FK reflow — walk `kind.Modality |> List.contains SystemOwned`.
    /// Payload-free per the codified ModalityMark pattern; mirrors
    /// `TenantScoped` / `SoftDeletable`.
    | SystemOwned
    /// SQL Server system-versioned temporal table. Chapter A.0' slice
    /// η — closed-DU widening. The payload carries the history-table
    /// coordinates + period columns + retention policy; emitters that
    /// understand temporal tables walk `kind.Modality` looking for
    /// the variant. Adheres to the closed-DU empirical-test
    /// discipline: pattern-match sites surface at compile time only.
    | Temporal of TemporalConfig

/// Typed structural display for `ModalityMark`. The `Static`
/// variant carries the population count (not the rows themselves)
/// — the count is the operator-relevant signal; the rows are
/// queried separately.
[<RequireQualifiedAccess>]
module ModalityMark =
    let toStructured (m: ModalityMark) : StructuredString =
        match m with
        | Static populations ->
            StructuredString.create "Static"
                [ "populations", Inv.int32 (List.length populations) ]
        | TenantScoped -> StructuredString.tag "TenantScoped"
        | SoftDeletable -> StructuredString.tag "SoftDeletable"
        | SystemOwned -> StructuredString.tag "SystemOwned"
        | Temporal config ->
            let retentionTag =
                match config.Retention with
                | Infinite -> "Infinite"
                | Limited _ -> "Limited"
            StructuredString.create "Temporal"
                [ "retention", retentionTag ]

    let toDiagnosticString (m: ModalityMark) : string =
        toStructured m |> StructuredString.render


/// Per-kind physical realization. Per session-36 audit (Agents 1, 2,
/// 3 multi-axis): unified with the schema-coordinate value object
/// `TableId` (Coordinates.fs). Same shape (`{ Schema: string; Table:
/// string }`); existing `kind.Physical.Schema` / `.Table` field
/// access unchanged. Construction now flows through `TableId.create`
/// for non-blank invariants. Future: typed `SchemaName` / `TableName`
/// VOs once a consumer pays for the explicit projection.
type PhysicalRealization = TableId


/// Per-attribute (column-level) physical realization. Decoupled from
/// `Attribute` so that policy and unfold passes can rewrite physical
/// metadata without touching logical structure.
type ColumnRealization = {
    ColumnName : string
    IsNullable : bool
}


/// Reference action at the target side. Mirrored from the standard
/// foreign-key DELETE/UPDATE cascade vocabulary.
type ReferenceAction =
    | NoAction
    | Cascade
    | SetNull
    | Restrict


/// A scalar attribute on a kind.
///
/// `IsPrimaryKey` flags whether this attribute participates in its kind's
/// primary key. Composite primary keys are expressed by flagging multiple
/// attributes on the same kind. This field was added under the discipline
/// "IR grows under evidence" (see DECISIONS.md): the V1 admire pass on
/// `EntitySeedDeterminizer` (see ADMIRE.md) needs PK columns to drive
/// row-order normalization, and the eventual `Projection.Targets.SSDT`
/// FK emitter needs PK columns to resolve target-side references.
///
/// `IsMandatory` flags whether the source model declares this attribute
/// as logically mandatory — V1's OutSystems model carries this as a
/// metadata flag distinct from physical NOT NULL. The flag drives
/// `NullabilityRules`'s mandatory signal hierarchy. Added under
/// "IR grows under evidence" (DECISIONS.md, 2026-05-10): the
/// `NullabilityEvaluator` end-to-end test (session 6) surfaced the gap;
/// V2 closes it here so the V1 mandatory branches can fire.
type Attribute = {
    SsKey        : SsKey
    Name         : Name
    Type         : PrimitiveType
    Column       : ColumnRealization
    IsPrimaryKey : bool
    IsMandatory  : bool
    /// NVARCHAR / VARCHAR / CHAR / NCHAR / VARBINARY / BINARY:
    /// the declared length. `None` for MAX (open-ended) or for
    /// types where length is not applicable (Integer / Boolean
    /// / DateTime / etc.). Per session-32 — ReadSide populates
    /// from `INFORMATION_SCHEMA.COLUMNS.CHARACTER_MAXIMUM_LENGTH`;
    /// `-1` from SQL Server (the MAX marker) maps to `None`.
    Length       : int option
    /// DECIMAL / NUMERIC: the declared precision. `None` for
    /// non-decimal types. ReadSide populates from
    /// `INFORMATION_SCHEMA.COLUMNS.NUMERIC_PRECISION`.
    Precision    : int option
    /// DECIMAL / NUMERIC: the declared scale. `None` for non-
    /// decimal types. ReadSide populates from
    /// `INFORMATION_SCHEMA.COLUMNS.NUMERIC_SCALE`.
    Scale        : int option
    /// IDENTITY column property (`INT NOT NULL IDENTITY(1,1)` →
    /// `IsIdentity = true`). Per session-32 — V2 IR carries the
    /// boolean; seed and increment values are deferred (always
    /// emit `IDENTITY(1,1)` when set, which matches the
    /// OutSystems convention). ReadSide reads from
    /// `sys.columns.is_identity` (1 → true).
    IsIdentity   : bool
    /// Operator-visible docstring carried from the V1 source
    /// `ossys_EntityAttr.Description` (rowset path) or the
    /// `description` JSON property (JSON path). `None` when the
    /// source omits the field. Chapter A.0' slice α — IR fidelity
    /// lift (L3-S9 descriptions sub-axiom). Emission lands when a
    /// consumer demands it (extended-properties DDL is chapter
    /// 4.1.A slice 8 territory; the `CommentMetadataUnreflected`
    /// Tolerance variant retires when both the IR carries AND the
    /// emitter emits).
    Description  : string option
    /// V1 lifecycle flag carried from `ossys_EntityAttr.Is_Active`
    /// (rowset path) or the `isActive` JSON property (JSON path).
    /// V1's SQL coerces missing/null source values to `true` per
    /// `outsystems_metadata_rowsets.sql:94, 116, 239`; the V2 adapter
    /// mirrors that semantic (absent JSON → `true`). Chapter A.0'
    /// slice β — IR fidelity lift (L3-S9 IsActive sub-axiom). The
    /// pre-slice-β session-21 adapter-boundary filter dropped
    /// `IsActive=false` attributes silently; slice β retires that
    /// disposition per pillar-9 harvest-dichotomy (filtering is
    /// `OperatorIntent`, mis-placed at the adapter which carries
    /// only `DataIntent`). Carriage-only in this slice — any
    /// Selection-axis filter pass that re-applies an inactive-records
    /// drop policy lands when a consumer demands it.
    IsActive     : bool
    /// SQL Server DEFAULT constraint expression for this column.
    /// `None` when the source omits a default. Chapter A.0' slice ε
    /// — IR fidelity lift (L3-S6 DEFAULT sub-axiom). Sourced from
    /// V1's JSON `attribute.default` (currently always `null` in
    /// V1 fixtures; the IR field is positioned for when V1 begins
    /// projecting defaults or when DACPAC adoption surfaces them).
    /// Carried as a typed `SqlLiteral` per pillar 1 (data-structure-
    /// oriented; the IR consumes typed values, not raw strings).
    DefaultValue : SqlLiteral option
    /// SQL Server computed-column configuration. `None` for
    /// non-computed columns. Chapter A.0' slice ε — IR fidelity lift
    /// (L3-S7 computed-column sub-axiom). V1's source JSON does not
    /// currently carry computed-column metadata; positioned for a
    /// future DACPAC or rowset slice.
    Computed     : ComputedColumnConfig option
    /// SQL Server `sys.extended_properties` annotations attached to
    /// this column. Empty when the source carries none. Chapter A.0'
    /// slice ζ — IR fidelity lift (L3-S9 extended-properties
    /// sub-axiom; attribute level).
    ExtendedProperties : ExtendedProperty list
    /// Prior attribute name carried from V1's `ossys_EntityAttr.OriginalName`
    /// (JSON `originalName`). Set when an attribute has been renamed in
    /// the model; `None` when no rename history exists. Carriage-only —
    /// any rename-aware consumer (refactor-log emitter, V1-parity
    /// migration paths) lands when triggered. Chapter 4.9 slice β —
    /// retires one of two A.0'-deferred-out-of-scope concepts under
    /// explicit principal-PO direction (`CHAPTER_4_9_OPEN.md` §Why).
    OriginalName : string option
    /// User-specified external SQL Server database type for external
    /// entities (V1's `ossys_EntityAttr.ExternalColumnType`; JSON
    /// `external_dbType`). `None` for OS-native entities and for
    /// external entities where V1 omits the override. V2's
    /// `PrimitiveType` abstraction (A13) remains canonical for
    /// emission; this field is fidelity carriage for round-trip
    /// reconstruction and future external-entity DDL paths.
    /// Chapter 4.9 slice β.
    ExternalDatabaseType : string option
}


/// A directional reference (A10). Symmetry, if needed by a target surface,
/// is introduced by the symmetric-closure pass and the resulting reference
/// carries a `Derived` SsKey with reason `"inverse"`.
///
/// **`IsUserFk` field added at chapter 4.2 slice ζ** — per
/// `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` §5: identifies references
/// whose target is the platform user kind, so data-emission siblings
/// (chapter 4.1.B MigrationDependenciesEmitter + BootstrapEmitter)
/// can rewrite `CreatedBy` / `UpdatedBy` column values via the
/// chapter 4.2 `UserRemapContext`. The OSSYS adapter resolves the
/// flag at construction (per the V1 reference
/// `ModelUserSchemaGraphFactory.GetSyntheticUserForeignKeys`); test
/// fixtures and non-user-FK adapter sites default to `false`.
type Reference = {
    SsKey           : SsKey
    Name            : Name
    SourceAttribute : SsKey
    TargetKind      : SsKey
    OnDelete        : ReferenceAction
    /// True iff this reference's `TargetKind` resolves to the
    /// platform user kind (the OSSYS-native users entity in V1's
    /// model). Set by the OSSYS adapter when it can identify the
    /// user kind via the platform's `extension_id` lookup; false
    /// otherwise. Slice η emitters consume this flag to gate User-
    /// FK column rewriting at row-emission time.
    IsUserFk        : bool
    /// True iff this reference is backed by a real SQL Server FK
    /// constraint (a `FOREIGN KEY ... REFERENCES` clause). V1's
    /// `hasDbConstraint` int-flag (COALESCE'd from
    /// `outsystems_model_export.sql:730` HasFK column). When false,
    /// the reference exists only at the OutSystems model level
    /// (logical-only FK); when true, a corresponding DB constraint
    /// materializes the reference at the storage layer.
    ///
    /// Chapter 4.6 slice α — IR fidelity lift retiring chapter 4.4's
    /// `HasLogicalForeignKeyWithoutDbConstraint` +
    /// `HasLogicalForeignKeyWithDbConstraint` always-false
    /// PredicateName variants.
    HasDbConstraint : bool
}


/// Per-column sort direction within an `Index`. Chapter 4.9 slice γ
/// — retires the third A.0'-deferred concept (`IndexColumnDirection`).
/// SQL Server emits the keyword DESC after a column name when the
/// direction is `Descending`; `Ascending` is the default and
/// ScriptDom's `SortOrder.NotSpecified` carries it (matching V1's
/// `IndexScriptBuilder` convention which sets `SortOrder` only on
/// descending columns).
type IndexColumnDirection =
    | Ascending
    | Descending

/// One key column within an `Index.Columns` ordered list. Carries
/// the participating `Attribute` SsKey + the per-column sort
/// direction. Chapter 4.9 slice γ — record-modification of the prior
/// `SsKey list` shape. Included columns (covering indexes) stay on
/// `Index.IncludedColumns : SsKey list` — non-key columns carry no
/// direction in SQL Server.
type IndexColumn = {
    Attribute : SsKey
    Direction : IndexColumnDirection
}

/// A schema-level index on a kind. Carries identity, name, the
/// participating attribute SsKeys (in declaration order; composite
/// indexes have multiple), `IsUnique` (does the source treat this index
/// as a uniqueness constraint), and `IsPrimaryKey` (is this the kind's
/// primary-key index — V1 treats the PK as a unique index, but V2
/// distinguishes them at the structural level).
///
/// Added under "IR grows under evidence" (DECISIONS.md, 2026-05-10):
/// the V1 `UniqueIndexDecisionOrchestrator` admire (ADMIRE.md
/// 2026-05-10) requires per-index decisions; the synthetic milestone
/// covered PK + FK only and didn't model unique indexes. V2's
/// `UniqueIndexRules` + `UniqueIndexPass` (session 7 commit 5) consume
/// this field; emitters that render `CREATE UNIQUE INDEX` walk it.
///
/// V1 `Index.Columns` includes both key columns and "included"
/// (non-key) columns; V2's `Columns` carries only key columns. The
/// V1↔V2 adapter (when it lands) drops included columns at the
/// boundary per the 2026-05-10 vestigial-fields convention.
type Index = {
    SsKey        : SsKey
    Name         : Name
    /// Key columns in declaration order. Each entry carries the
    /// attribute SsKey + per-column sort direction. Chapter 4.9
    /// slice γ — record-modification from `SsKey list` to
    /// `IndexColumn list`.
    Columns      : IndexColumn list
    IsUnique     : bool
    IsPrimaryKey : bool
    /// SQL Server `sys.extended_properties` annotations attached to
    /// this index. Empty when the source carries none. Chapter A.0'
    /// slice ζ — IR fidelity lift (L3-S9 extended-properties
    /// sub-axiom; index level).
    ExtendedProperties : ExtendedProperty list
    /// SQL Server filter predicate for filtered indexes. Carried as
    /// the raw V1 filter-definition string (mirrors V1's
    /// `IndexOnDiskMetadata.FilterDefinition`); parsed at emit time
    /// via `TSql160Parser.ParseExpression` into ScriptDom's
    /// `BooleanExpression`. `None` for unfiltered indexes (V1
    /// default).
    ///
    /// Chapter 4.5 slice α — IR fidelity lift retiring chapter 4.4's
    /// `HasFilteredIndex` always-false PredicateName variant. Source:
    /// V1's JSON `index.filterDefinition` projection (rowset path
    /// reads `sys.indexes.filter_definition`).
    Filter       : string option
    /// SQL Server INCLUDE columns for covering indexes. V1's
    /// `IndexColumnModel.IsIncluded = true` entries land here at the
    /// adapter boundary (the V2-pre-chapter-4.5 adapter dropped them
    /// per the documented ADMIRE divergence; slice β retires the drop).
    /// Empty for indexes without included columns (V1 default).
    ///
    /// Chapter 4.5 slice β — IR fidelity lift retiring chapter 4.4's
    /// `HasIncludedIndexColumns` always-false PredicateName variant.
    /// Source: V1's JSON `index.columns[]` entries with
    /// `isIncluded: true` (rowset path: index-columns rowset).
    /// Ordered by V1 `ordinal` field at the adapter boundary
    /// (same shape as `Columns` key-column ordering).
    IncludedColumns : SsKey list
    /// True iff this index is OutSystems-platform-auto-generated (V1
    /// `IndexModel.IsPlatformAuto`). Auto indexes are emitted by the
    /// V1 platform's index-creation logic; V2 inherits via the
    /// adapter. Used by future emitters to gate whether to include
    /// platform-auto indexes in the SSDT bundle (operator-toggle).
    ///
    /// Chapter 4.6 slice β — IR fidelity lift retiring one of four
    /// A.0' deferred concepts (OriginalName / ExternalDatabaseType /
    /// IndexColumnDirection / IsPlatformAuto). Source: V1's JSON
    /// `index.isPlatformAuto` projection.
    IsPlatformAuto : bool
    /// SQL Server `FILLFACTOR` index option (per-index allocation
    /// density 1-100). `None` = server default. Mirrors V1's
    /// `IndexOnDiskMetadata.FillFactor`. Chapter 4.8 slice β.
    FillFactor : int option
    /// SQL Server `PAD_INDEX` option. `false` (V1 default) = OFF;
    /// `true` = ON (apply FILLFACTOR to non-leaf intermediate pages).
    /// Mirrors V1's `IndexOnDiskMetadata.IsPadded`. Chapter 4.8 slice β.
    IsPadded : bool
    /// SQL Server `ALLOW_ROW_LOCKS` option. `true` (V1 default) = ON.
    /// Mirrors V1's `IndexOnDiskMetadata.AllowRowLocks`. Chapter 4.8 slice β.
    AllowRowLocks : bool
    /// SQL Server `ALLOW_PAGE_LOCKS` option. `true` (V1 default) = ON.
    /// Mirrors V1's `IndexOnDiskMetadata.AllowPageLocks`. Chapter 4.8 slice β.
    AllowPageLocks : bool
    /// SQL Server `STATISTICS_NORECOMPUTE` option. `false` (V1 default)
    /// = OFF (auto-update enabled). Mirrors V1's
    /// `IndexOnDiskMetadata.NoRecomputeStatistics`. Chapter 4.8 slice β.
    NoRecomputeStatistics : bool
}


/// A kind: the schema-level entity type. Carries identity, name, origin,
/// modality marks, physical realization, attributes, references, and
/// indexes (A8).
type Kind = {
    SsKey       : SsKey
    Name        : Name
    Origin      : Origin
    Modality    : ModalityMark list
    Physical    : PhysicalRealization
    Attributes  : Attribute list
    References  : Reference list
    Indexes     : Index list
    /// Operator-visible docstring carried from the V1 source
    /// `ossys_Entity.Description` (rowset path) or the `description`
    /// JSON property (JSON path). `None` when the source omits the
    /// field. Chapter A.0' slice α — IR fidelity lift (L3-S9
    /// descriptions sub-axiom). Sibling to `Attribute.Description`;
    /// same operational semantics (carriage-only at this slice).
    Description : string option
    /// V1 lifecycle flag carried from `ossys_Entity.Is_Active`
    /// (rowset path) or the entity-level `isActive` JSON property
    /// (JSON path). Same default-true semantics as
    /// `Attribute.IsActive`. Chapter A.0' slice β — IR fidelity
    /// lift; retires the session-21 entity-level adapter-boundary
    /// filter at `parseKind`. Sibling to `Module.IsActive` and
    /// `Attribute.IsActive`; downstream emitters decide.
    IsActive    : bool
    /// SQL Server DML triggers attached to this kind. V1 source:
    /// JSON entity-level `triggers[]` array (carries name +
    /// isDisabled + definition). Chapter A.0' slice γ — IR fidelity
    /// lift (L3-S4 triggers sub-axiom). Empty when the source
    /// projects no triggers.
    Triggers    : Trigger list
    /// SQL Server CHECK constraints at the table level. V1 source:
    /// V1's `AttributeOnDiskCheckConstraint` (column-level CHECK
    /// constraints are exposed via the on-disk metadata channel,
    /// not the JSON-projection boundary today). Chapter A.0' slice ε
    /// — IR fidelity lift (L3-S8 CHECK sub-axiom). Kind-scoped
    /// because a CHECK can span multiple columns (SQL Server
    /// semantic). Empty when the source projects no CHECK
    /// constraints.
    ColumnChecks : ColumnCheck list
    /// SQL Server `sys.extended_properties` annotations attached to
    /// this kind. V1 source: V1's JSON entity-level
    /// `extendedProperties[]` array. Chapter A.0' slice ζ — IR
    /// fidelity lift (L3-S9 extended-properties sub-axiom; entity
    /// level).
    ExtendedProperties : ExtendedProperty list
}


/// A coproduct cell of the catalog (A11). Modules are disjoint by SsKey;
/// the projection respects the decomposition (T2).
type Module = {
    SsKey    : SsKey
    Name     : Name
    Kinds    : Kind list
    /// V1 lifecycle flag carried from `ossys_Espace.Is_Active`
    /// (rowset path; `ModuleRow.IsActive`) or the module-level
    /// `isActive` JSON property (JSON path). Same default-true
    /// semantics as `Kind.IsActive` / `Attribute.IsActive`.
    /// Chapter A.0' slice β — IR fidelity lift; retires the
    /// `parseRowsetBundle` module-level filter that previously
    /// dropped `IsActive=false` modules silently. The JSON path's
    /// `parseDocument` did not previously filter modules (Subagent
    /// #3's O2 finding on `module.isActive: false`); slice β adds
    /// JSON-path module-level read for parity.
    IsActive : bool
    /// SQL Server `sys.extended_properties` annotations attached to
    /// this module's schema. V1 does not currently project module-
    /// level extended properties; positioned for future expansion.
    /// Chapter A.0' slice ζ — IR fidelity lift (L3-S9 extended-
    /// properties sub-axiom; module level).
    ExtendedProperties : ExtendedProperty list
}


/// The whole catalog: a coproduct over modules.
type Catalog = {
    Modules : Module list
    /// SQL Server SEQUENCE schema objects. Top-level Catalog field
    /// because sequences are schema-scoped, not table-scoped (they
    /// can be referenced by multiple tables or by application code).
    /// Chapter A.0' slice δ — IR fidelity lift (L3-S5 sequences
    /// sub-axiom). V1's JSON projection does not currently surface
    /// sequences at the catalog boundary; populated empty until a
    /// future source (DACPAC adapter; expanded V1 rowsets) provides
    /// the evidence.
    Sequences : Sequence list
}


/// Identity-based equality and lookup helpers for catalog nodes (A4).
/// The default F# record `=` compares all fields, which is the right
/// operator for "did this pass change anything?" tests; these helpers are
/// the right operator for "is this the same node, structurally?" — that is,
/// for catalog-level identity.
[<RequireQualifiedAccess>]
module Kind =

    /// True when two kinds share the same SsKey, regardless of names,
    /// attribute orderings, modality marks, or any other field. Encodes
    /// A4 as a function: structural equality of kinds is by SsKey only.
    let byIdentity (a: Kind) (b: Kind) : bool = a.SsKey = b.SsKey

    /// The attributes flagged `IsPrimaryKey` on this kind, in the order
    /// they appear. May be empty for kinds without a declared PK; may
    /// contain multiple entries for composite-key kinds.
    let primaryKey (k: Kind) : Attribute list =
        k.Attributes |> List.filter (fun a -> a.IsPrimaryKey)

    /// The static-population rows attached to this kind via
    /// `Modality.Static`, or `[]` if the kind carries no static
    /// modality. Per A7 (static populations live in the catalog) +
    /// the modality projection in `ModalityMark`. Two-consumer
    /// extraction (StaticSeedsEmitter MERGE realization +
    /// StaticPopulationEmitter typed-stream realization); the
    /// projection lives in Core so both data-emission realizations
    /// share one source-of-truth.
    let staticPopulations (k: Kind) : StaticRow list =
        k.Modality
        |> List.tryPick (function
            | Static rows -> Some rows
            | _           -> None)
        |> Option.defaultValue []

    /// Find an attribute on the kind by SsKey (per A4 — identity-keyed
    /// lookup, never by name). Returns `None` if absent. Lifted to
    /// Core at chapter 4.1.B slice ε per the slice-δ improvement
    /// surface (#5): `StaticSeedsEmitter.deferredColumns` and
    /// `MigrationDependenciesEmitter`'s reference-resolution path
    /// both look up source attributes by SsKey through this lens; a
    /// third consumer at chapter 4.2's `UserFkReflowPass` is on the
    /// horizon. Two-consumer threshold met.
    let tryFindAttribute (ssKey: SsKey) (k: Kind) : Attribute option =
        k.Attributes |> List.tryFind (fun a -> a.SsKey = ssKey)


[<RequireQualifiedAccess>]
module Module =

    /// Find a kind in this module by SsKey. Returns `None` if absent. A4:
    /// lookup is by identity, never by name.
    let tryFindKind (ssKey: SsKey) (m: Module) : Kind option =
        m.Kinds |> List.tryFind (fun k -> k.SsKey = ssKey)

    /// Smart constructor enforcing the per-module aggregate invariants.
    /// Per session-36 audit (Agent 3 #10/#11): `Module` is an
    /// aggregate boundary; the per-module invariant is "Kind SsKeys
    /// disjoint within the module." Production-side construction
    /// (adapters, passes producing transformed catalogs) flows
    /// through `create`; passes that map over an existing catalog's
    /// modules and preserve invariants by construction can use
    /// record-literal updates (`{ m with Kinds = ... }`) since the
    /// invariant cannot be violated by a non-introducing transform.
    let create
        (ssKey: SsKey)
        (name: Name)
        (kinds: Kind list)
        (isActive: bool)
        (extendedProperties: ExtendedProperty list)
        : Result<Module> =
        // LR1 (slice 5.13.module-non-empty-invariant, matrix row 42):
        // per-module non-empty Kind invariant. V1's `ModuleModel.Create`
        // enforces this; V2 lifts the same axis per A39 (aggregate-root
        // smart-constructor invariants) + `DECISIONS 2026-05-18 (slice
        // 5.2.α.module)` path (a). Prevents a ghost-module class of bug
        // in transformation passes — a module with zero kinds is
        // semantically meaningless at every consumer (emitter / pass /
        // diagnostic) but was silently constructible.
        if List.isEmpty kinds then
            Result.failureOf (
                ValidationError.create
                    "module.kinds.empty"
                    (sprintf "Module %A must contain at least one Kind." ssKey))
        else
        let duplicates =
            kinds
            |> List.groupBy (fun k -> k.SsKey)
            |> List.filter (fun (_, ks) -> List.length ks > 1)
            |> List.map fst
        if not (List.isEmpty duplicates) then
            duplicates
            |> List.map (fun k ->
                ValidationError.create
                    "module.kinds.duplicateKey"
                    (sprintf
                        "Module %A has duplicate Kind SsKey %A; A11 (coproduct cell) requires disjoint kinds."
                        ssKey
                        k))
            |> Result.failure
        else
            Result.success
                { SsKey              = ssKey
                  Name               = name
                  Kinds              = kinds
                  IsActive           = isActive
                  ExtendedProperties = extendedProperties }


[<RequireQualifiedAccess>]
module Catalog =

    /// Find a kind anywhere in the catalog by SsKey. Returns `None` if
    /// absent. A4: lookup is by identity, never by name.
    let tryFindKind (ssKey: SsKey) (c: Catalog) : Kind option =
        c.Modules |> List.tryPick (Module.tryFindKind ssKey)

    /// Find the module that owns a given kind by SsKey.
    let tryFindOwningModule (ssKey: SsKey) (c: Catalog) : Module option =
        c.Modules |> List.tryFind (fun m ->
            m.Kinds |> List.exists (fun k -> k.SsKey = ssKey))

    /// Enumerate all kinds across all modules.
    let allKinds (c: Catalog) : Kind list =
        c.Modules |> List.collect (fun m -> m.Kinds)

    /// Smart constructor enforcing the catalog-wide aggregate
    /// invariants. Per session-36 audit (Agent 3 #10/#12, Agent 1 #19):
    ///   1. Module SsKeys are disjoint (A11).
    ///   2. Kind SsKeys are disjoint across all modules.
    ///   3. Every `Reference.SourceAttribute` exists on its owning
    ///      `Kind.Attributes`.
    ///   4. Every `Reference.TargetKind` exists somewhere in the
    ///      catalog (no dangling FKs).
    ///   5. Every `Index.Columns` entry exists on its owning
    ///      `Kind.Attributes`.
    ///
    /// `tryFindKind`, `RawTextEmitter.fkDef`, and
    /// `PhysicalSchema.toPhysicalForeignKeys` previously each
    /// re-validated #3/#4 by silently dropping bad references. Per
    /// the discipline "invariants live with the type, not in the
    /// consumer" — flowing through `create` makes #1–#5 impossible
    /// to violate. Aggregates errors so a consumer sees every
    /// violation in one Result.
    let create (modules: Module list) (sequences: Sequence list) : Result<Catalog> =
        let moduleDupes =
            modules
            |> List.groupBy (fun m -> m.SsKey)
            |> List.filter (fun (_, ms) -> List.length ms > 1)
            |> List.map fst
            |> List.map (fun k ->
                ValidationError.create
                    "catalog.modules.duplicateKey"
                    (sprintf
                        "Catalog has duplicate Module SsKey %A; A11 requires disjoint modules."
                        k))

        let allKindList = modules |> List.collect (fun m -> m.Kinds)
        let kindDupes =
            allKindList
            |> List.groupBy (fun k -> k.SsKey)
            |> List.filter (fun (_, ks) -> List.length ks > 1)
            |> List.map fst
            |> List.map (fun k ->
                ValidationError.create
                    "catalog.kinds.duplicateKey"
                    (sprintf
                        "Catalog has Kind SsKey %A duplicated across modules; A4 requires Kind identity to be globally unique."
                        k))

        let kindKeySet =
            allKindList |> List.map (fun k -> k.SsKey) |> Set.ofList

        let referenceErrors =
            allKindList
            |> List.collect (fun k ->
                let attrKeys =
                    k.Attributes |> List.map (fun a -> a.SsKey) |> Set.ofList
                k.References
                |> List.collect (fun r ->
                    let danglingSource =
                        if Set.contains r.SourceAttribute attrKeys then []
                        else
                            [ ValidationError.create
                                "catalog.reference.danglingSource"
                                (sprintf
                                    "Reference %A on Kind %A has SourceAttribute %A absent from the kind's Attributes."
                                    r.SsKey k.SsKey r.SourceAttribute) ]
                    let danglingTarget =
                        if Set.contains r.TargetKind kindKeySet then []
                        else
                            [ ValidationError.create
                                "catalog.reference.danglingTarget"
                                (sprintf
                                    "Reference %A on Kind %A has TargetKind %A absent from the catalog."
                                    r.SsKey k.SsKey r.TargetKind) ]
                    danglingSource @ danglingTarget))

        let indexErrors =
            allKindList
            |> List.collect (fun k ->
                let attrKeys =
                    k.Attributes |> List.map (fun a -> a.SsKey) |> Set.ofList
                k.Indexes
                |> List.collect (fun idx ->
                    idx.Columns
                    |> List.choose (fun col ->
                        if Set.contains col.Attribute attrKeys then None
                        else
                            Some (ValidationError.create
                                "catalog.index.danglingColumn"
                                (sprintf
                                    "Index %A on Kind %A references column SsKey %A absent from the kind's Attributes."
                                    idx.SsKey k.SsKey col.Attribute)))))

        // Sequence SsKey disjointness (chapter A.0' slice δ). Sequences
        // are top-level Catalog objects; their SsKeys must be unique
        // across the catalog by A4. Disjointness from Kind SsKeys is
        // not currently enforced — sequences and kinds are different
        // schema-object kinds (SEQUENCE vs TABLE) and use disjoint
        // SsKey-synthesis prefixes (`OS_SEQ_*` vs `OS_KIND_*`), so
        // collisions are not structurally possible.
        let sequenceDupes =
            sequences
            |> List.groupBy (fun s -> s.SsKey)
            |> List.filter (fun (_, ss) -> List.length ss > 1)
            |> List.map fst
            |> List.map (fun k ->
                ValidationError.create
                    "catalog.sequences.duplicateKey"
                    (sprintf
                        "Catalog has duplicate Sequence SsKey %A; A4 requires unique sequence identity."
                        k))

        let allErrors =
            moduleDupes @ kindDupes @ referenceErrors @ indexErrors @ sequenceDupes

        if List.isEmpty allErrors then
            Result.success { Modules = modules; Sequences = sequences }
        else
            Result.failure allErrors
