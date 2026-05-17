namespace Projection.Targets.SSDT

open Projection.Core

/// Π_SSDT's typed statement-stream form. Per session-34 — Π's
/// canonical output is a deterministic `seq<Statement>`; realization
/// layers (`Render.toText`, `Deploy.executeStream`) consume the
/// stream and choose their emission form. Bulk-vs-per-row deploy is
/// realization-layer policy, invisible to Π. The algebra (A18 / T1)
/// holds at the stream level: the same Catalog produces the same
/// statement sequence byte-for-byte, regardless of downstream
/// realization choice.
///
/// AXIOM scaffold (filed for chapter-3 close):
///   A35 — Π's output is a deterministic statement stream.
///   A36 — Bulk-vs-incremental is realization-layer policy.
///
/// Per session-36 audit: the SSDT-local `TableId` retired in favor
/// of the Core-resident `TableId` value object (`Coordinates.fs`).
/// The shape is identical; consumers that referenced
/// `Projection.Targets.SSDT.TableId` now resolve to the Core type
/// transparently via `open Projection.Core`.

/// IR-typed column declaration. The realization layer (`Render`)
/// converts `(Type, Length, Precision, Scale)` to its SQL type
/// expression, so emit-time and deploy-time agree by construction.
type ColumnDef =
    {
        Name : string
        Type : PrimitiveType
        Length : int option
        Precision : int option
        Scale : int option
        Nullable : bool
        IsIdentity : bool
        IsPrimaryKey : bool
        /// The originating attribute's display name + SsKey root,
        /// preserved so `Render.toText` can keep the diffable-form
        /// trailing comment that the v1 emitter carried.
        Provenance : string
    }

type ReferenceActionSql = NoActionSql | CascadeSql | SetNullSql

type ForeignKeyDef =
    {
        Name : string
        SourceColumn : string
        Target : TableId
        TargetColumn : string
        OnDelete : ReferenceActionSql
    }

type PrimaryKeyDef =
    {
        Name : string
        Columns : string list
    }

/// IR-typed CREATE INDEX declaration. Mirrors V2's `Index` in
/// `Catalog.fs` but with column NAMES (not SsKeys) at the realization
/// layer — the emitter resolves SsKey → ColumnName before building
/// the typed AST. Per chapter 4.1.A pre-scope §3 + slice 3:
/// `CREATE [UNIQUE] [NONCLUSTERED] INDEX [name] ON [Schema].[Table]
/// ([col1], [col2], ...)`. Composite indexes carry multiple columns;
/// PK-marked indexes are skipped at the emitter (PK is inlined in
/// CREATE TABLE per V1 convention).
type IndexDef =
    {
        Name : string
        Table : TableId
        Columns : string list
        IsUnique : bool
        /// Chapter 4.5 slice α — raw filter-definition string for
        /// filtered indexes. `None` for unfiltered (the V1 default).
        /// `ScriptDomBuild.buildCreateIndex` parses at emit time
        /// via `TSql160Parser.ParseBooleanExpression`.
        Filter : string option
        /// Chapter 4.5 slice β — INCLUDE column names for covering
        /// indexes. Empty for indexes without INCLUDE clause (V1
        /// default). Mirrors `Index.IncludedColumns` at the
        /// realization layer (SsKey → column name resolved by the
        /// emitter before reaching here).
        IncludedColumns : string list
    }

/// One column's value within an `InsertRow`. `Raw` is the V2 IR
/// contract: invariant-culture string, `""` denotes NULL. The
/// realization layer formats per `Type`.
type CellValue =
    {
        Column : string
        Type : PrimitiveType
        Raw : string
    }

/// What kind of database object an extended-property attachment
/// targets, per SQL Server's `sp_addextendedproperty` `@level2type`
/// + `@level2name` slot. Chapter 4.1.A slice 8 emits these for V2
/// IR's `Kind.Description` (table-level), `Attribute.Description` +
/// `Attribute.ExtendedProperties` (column-level), `Kind.
/// ExtendedProperties` (table-level), and `Index.ExtendedProperties`
/// (index-level).
///
/// The DU is concept-shaped per pillar 8 — each variant names *what*
/// the target IS in V1 / SQL Server's extended-property taxonomy.
type ExtendedPropertyTarget =
    /// `@level1type=TABLE, @level1name=<table>` only. Table-level
    /// extended property (e.g., `MS_Description` on a Kind).
    | TableExtendedProperty
    /// `@level1type=TABLE, @level1name=<table>, @level2type=COLUMN,
    /// @level2name=<column>`. Column-level extended property (e.g.,
    /// `MS_Description` on an Attribute).
    | ColumnExtendedProperty of columnName: string
    /// `@level1type=TABLE, @level1name=<table>, @level2type=INDEX,
    /// @level2name=<index>`. Index-level extended property
    /// (chapter A.0' slice ζ carriage; V1 emits these for index
    /// descriptions when present).
    | IndexExtendedProperty of indexName: string

type Statement =
    | Blank
    | Comment of text: string
    | CreateTable of TableId * ColumnDef list * PrimaryKeyDef option * ForeignKeyDef list
    /// Chapter 4.1.A slice 3: CREATE INDEX statement for non-PK
    /// indexes. PK-marked indexes are inlined in CREATE TABLE per
    /// V1 convention; the SsdtDdlEmitter filters them before
    /// emission.
    | CreateIndex of IndexDef
    | InsertRow of TableId * CellValue list
    | SetIdentityInsert of TableId * enabled: bool
    /// Chapter 4.1.A slice 8: `EXEC sys.sp_addextendedproperty` call
    /// attaching a named property + (optional) value to a schema
    /// object. Consumes chapter A.0' slice α's `Kind.Description` +
    /// `Attribute.Description` and slice ζ's `ExtendedProperties`
    /// lists (Kind / Attribute / Index). Retires the `Tolerance
    /// .CommentMetadataUnreflected` deferral when the emitter wires
    /// this variant into `kindToSsdtFile`.
    | SetExtendedProperty of
        tableId: TableId *
        target: ExtendedPropertyTarget *
        propertyName: string *
        propertyValue: string option
