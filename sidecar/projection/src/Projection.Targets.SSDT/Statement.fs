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
///
/// Slice 5.13.column-features-emit (chapter A.0' slice ε emit-side
/// closure) extends the record with `DefaultValue` + `DefaultName` +
/// `Computed`. The realization layer's `columnDefinition` builder
/// emits these as inline column constraints (`CONSTRAINT <name>
/// DEFAULT <expr>` and `AS <expr> [PERSISTED]`). When `Computed`
/// is `Some`, the column has no `Type` / `Length` / `Precision` /
/// `Scale` / `IsIdentity` material (the expression's result type
/// is server-inferred); the builder skips those clauses.
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
        /// Default-value expression as a typed `SqlLiteral` or
        /// `None`. Source: V1's `#Attr.DefaultValue` (logical default
        /// V1 carries) + `#ColumnReality.DefaultDefinition` (the
        /// deployed-target reality). The realization layer emits
        /// `[CONSTRAINT <name>] DEFAULT <literal>` for typed literals;
        /// expression-shaped defaults (e.g., `getutcdate()`) flow
        /// via raw-string pass-through at the realization boundary
        /// (matrix row 53; chapter A.0' slice ε emit closure).
        DefaultValue : SqlLiteral option
        /// Named-default-constraint identifier (V1's
        /// `DF_<table>_<column>` shape). Mirrors V1's deployed-
        /// target constraint identity so V2's CREATE TABLE
        /// round-trips against V1 emissions. `None` when the
        /// default carries no explicit name (V2's `CONSTRAINT`
        /// clause then omits the identifier and SQL Server
        /// auto-names the constraint).
        DefaultName : string option
        /// Computed column expression + persistence flag. When `Some`,
        /// the column is computed and the realization layer emits
        /// `[col] AS (expression) [PERSISTED]`; the `Type` / `Length` /
        /// `Precision` / `Scale` / `IsIdentity` / `Nullable` material
        /// is omitted (server-inferred from the expression). Slice
        /// 5.3.α.column-axis-deferral-closeout (LR4); V1 source:
        /// `CreateTableStatementBuilder.cs:362-365` (column.IsComputed
        /// + column.ComputedExpression).
        Computed : ComputedColumnConfig option
        /// The originating attribute's display name + SsKey root,
        /// preserved so `Render.toText` can keep the diffable-form
        /// trailing comment that the v1 emitter carried.
        Provenance : string
    }

/// Table-level CHECK constraint. V2's `Kind.ColumnChecks` IR (chapter
/// A.0' slice ε) carries one entry per unique constraint (table-
/// scoped — a CHECK can reference multiple columns; V1's per-column
/// rowset projection dedupes here). Slice 5.13.column-features-emit
/// wires these into V2's `Statement.CreateTable` via the new
/// `ColumnCheckDef list` slot.
///
/// `Definition` is the V1 reality string (e.g., `([Age] >= 0 AND
/// [Age] < 200)`). The realization layer parses via
/// `TSql160Parser.ParseBooleanExpression` and embeds the typed
/// `BooleanExpression` into ScriptDom's `CheckConstraintDefinition`.
type ColumnCheckDef =
    {
        Name         : string option
        Definition   : string
        IsNotTrusted : bool
    }

type ReferenceActionSql = NoActionSql | CascadeSql | SetNullSql

type ForeignKeyDef =
    {
        Name : string
        SourceColumn : string
        Target : TableId
        TargetColumn : string
        OnDelete : ReferenceActionSql
        /// Optional ON UPDATE referential action (slice
        /// 5.13.fk-features-emit; matrix row 58). `None` = unstated;
        /// ScriptDom omits the ON UPDATE clause (server-default NO
        /// ACTION applies). `Some action` = explicit clause.
        OnUpdate : ReferenceActionSql option
        /// Whether the FK constraint is TRUSTED at the deployed
        /// target. `true` (V1 default) = no special handling. `false`
        /// = the realization layer emits a sibling
        /// `Statement.AlterTableNoCheckConstraint` after CREATE TABLE
        /// so the FK round-trips against a deployed target carrying
        /// NOCHECK'd constraints. Slice 5.13.fk-features-emit (matrix
        /// row 59).
        IsConstraintTrusted : bool
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
/// Per-column sort direction at the realization layer. Mirrors
/// `IndexColumnDirection` from `Projection.Core.Catalog` — exists as a
/// separate type because realization-layer types are name-shaped
/// (string column name) rather than identity-shaped (SsKey).
/// Chapter 4.9 slice γ.
type IndexDefColumnDirection =
    | Ascending
    | Descending

/// One key column within an `IndexDef.Columns` ordered list at the
/// realization layer (column NAME, not SsKey). Chapter 4.9 slice γ —
/// record-modification from the prior `string list` shape.
type IndexDefColumn =
    {
        Name : string
        Direction : IndexDefColumnDirection
    }

/// SQL Server `DATA_COMPRESSION` levels at the realization layer.
/// Mirrors `Projection.Core.DataCompressionLevel` (the IR-side DU)
/// at the SSDT boundary; the SsdtDdlEmitter maps one to the other.
/// Slice 5.13.index-features-emit (matrix row 56).
type IndexDataCompressionSql =
    | NoneCompressionSql
    | RowCompressionSql
    | PageCompressionSql

/// Realization-layer mirror of `Catalog.DataSpace`. Slice
/// A.4.7'-prelude.row56-dataspace (LR7). The emitter consumes this
/// closed DU to build ScriptDom's `FileGroupOrPartitionScheme` —
/// both variants share that ScriptDom type modulo the partition-
/// column list, so the realization-layer DU is the cleaner seam
/// than passing the variants directly.
type IndexDataSpaceSql =
    /// Index resides on a named filegroup. Emitted as `ON [name]`.
    | FilegroupDataSpaceSql of name: string
    /// Index uses a partition scheme keyed by named partition
    /// columns. Emitted as `ON [name]([col1], [col2], ...)`.
    | PartitionSchemeDataSpaceSql of name: string * columns: string list

type IndexDef =
    {
        Name : string
        Table : TableId
        Columns : IndexDefColumn list
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
        /// Chapter 4.8 slice β — SQL Server on-disk index options.
        /// Mirrors V1's `IndexOnDiskMetadata` fields. Emitted via
        /// ScriptDom's `CreateIndexStatement.IndexOptions` list when
        /// at least one option deviates from default.
        FillFactor : int option
        IsPadded : bool
        AllowRowLocks : bool
        AllowPageLocks : bool
        NoRecomputeStatistics : bool
        /// Slice 5.13.index-features-emit (matrix row 55) —
        /// `IGNORE_DUP_KEY = ON` when `true`. ScriptDom's
        /// `IndexStateOption` with `IndexOptionKind.IgnoreDupKey`.
        IgnoreDuplicateKey : bool
        /// Slice 5.13.index-features-emit (matrix row 55) — when
        /// `true`, the realization layer emits a post-CREATE-INDEX
        /// `Statement.AlterIndexDisable` so the index lands in
        /// disabled state. CREATE INDEX always lands enabled; DISABLE
        /// is a separate ALTER statement (ScriptDom doesn't model
        /// the disable state as a CREATE-INDEX clause).
        IsDisabled : bool
        /// Slice 5.13.index-features-emit (matrix row 56) —
        /// `DATA_COMPRESSION = NONE | ROW | PAGE` when populated.
        /// `None` omits the option from the WITH clause (server-
        /// default applies).
        DataCompression : IndexDataCompressionSql option
        /// Slice A.4.7'-prelude.row56-dataspace (LR7 closure) —
        /// realization-layer mirror of `Index.DataSpace`. `None`
        /// omits the `ON` clause; `Some` populates ScriptDom's
        /// `CreateIndexStatement.OnFileGroupOrPartitionScheme`
        /// (filegroup name + optional partition-column list).
        DataSpace : IndexDataSpaceSql option
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

/// What object owns an extended-property attachment in SQL Server's
/// `sp_addextendedproperty` `@level0…@level2` taxonomy. Chapter 4.9
/// slice ε — collapses the prior `TableId × ExtendedPropertyTarget`
/// 2-tuple into a single concept-shaped DU that admits SCHEMA-level
/// (Module-owned) extended properties naturally. The variants name
/// *what* the owner IS:
///
///   - `SchemaProperty` — `@level0type=SCHEMA, @level0name=<schema>`
///     only. Module-level extended property (V1 emits these for
///     module-bearing schemas).
///   - `TableProperty` — `@level0type=SCHEMA, @level1type=TABLE`.
///     Kind-level extended property (e.g., `MS_Description` on a Kind).
///   - `ColumnProperty` — `+ @level2type=COLUMN`. Attribute-level
///     extended property (e.g., `MS_Description` on an Attribute).
///   - `IndexProperty` — `+ @level2type=INDEX`. Index-level extended
///     property (chapter A.0' slice ζ carriage; V1 emits these for
///     index descriptions when present).
type ExtendedPropertyOwner =
    /// Module-level (SCHEMA-level) extended property. Chapter 4.9
    /// slice ε.
    | SchemaProperty of schema: string
    /// Kind-level (TABLE-level) extended property.
    | TableProperty of tableId: TableId
    /// Attribute-level (COLUMN-level) extended property.
    | ColumnProperty of tableId: TableId * columnName: string
    /// Index-level extended property.
    | IndexProperty of tableId: TableId * indexName: string

type Statement =
    | Blank
    | Comment of text: string
    /// Slice D.2.c — `GO` batch separator. Sqlcmd directive (not T-SQL
    /// syntax); recognised by `BatchSplitter` so deploy paths split at
    /// this boundary into separate `ExecuteNonQueryAsync` round-trips.
    /// Mirrors V1's per-statement-group emission convention (every
    /// top-level statement followed by a blank line + `GO` + blank
    /// line). `ScriptDomBuild.buildStatement` returns `None` for this
    /// variant (sqlcmd directives have no ScriptDom AST equivalent);
    /// `Render.toSql` handles it directly.
    | BatchSeparator
    | CreateTable of TableId * ColumnDef list * PrimaryKeyDef option * ForeignKeyDef list * ColumnCheckDef list * TemporalConfig option
    /// Chapter 4.1.A slice 3: CREATE INDEX statement for non-PK
    /// indexes. PK-marked indexes are inlined in CREATE TABLE per
    /// V1 convention; the SsdtDdlEmitter filters them before
    /// emission.
    | CreateIndex of IndexDef
    | InsertRow of TableId * CellValue list
    | SetIdentityInsert of TableId * enabled: bool
    /// `EXEC sys.sp_addextendedproperty` call attaching a named
    /// property + (optional) value to a schema object. Consumes
    /// `Module.ExtendedProperties` (SCHEMA-level; chapter 4.9 slice ε),
    /// `Kind.Description` + `Kind.ExtendedProperties` (TABLE-level),
    /// `Attribute.Description` + `Attribute.ExtendedProperties`
    /// (COLUMN-level), and `Index.ExtendedProperties` (INDEX-level).
    | SetExtendedProperty of
        owner: ExtendedPropertyOwner *
        propertyName: string *
        propertyValue: string option
    /// `ALTER TABLE <table> WITH NOCHECK CHECK CONSTRAINT <fk>` —
    /// preserves a deployed target's FK trust state when V1's
    /// `#FkReality.IsNoCheck = 1` flips `Reference.IsConstraintTrusted`
    /// to `false`. Emitted by `SsdtDdlEmitter` after the CREATE TABLE
    /// statement of the owning kind, once per untrusted FK. Slice
    /// 5.13.fk-features-emit (matrix row 59).
    | AlterTableNoCheckConstraint of table: TableId * constraintName: string
    /// `ALTER INDEX <name> ON <table> DISABLE` — preserves a
    /// deployed target's index disable state when V1's
    /// `IndexOnDiskMetadata.IsDisabled = true`. Emitted by
    /// `SsdtDdlEmitter` after the CREATE INDEX statement of the
    /// owning index, once per disabled non-PK index. PK-marked
    /// indexes are inlined in CREATE TABLE and are not disabled
    /// (V1's invariant — primary keys are always enforced). Slice
    /// 5.13.index-features-emit (matrix row 55).
    | AlterIndexDisable of table: TableId * indexName: string
    /// `CREATE TRIGGER` statement. Emitted by `SsdtDdlEmitter` after a
    /// kind's extended property statements. Carries the raw T-SQL
    /// `Trigger.Definition` string; `ScriptDomBuild.buildStatement`
    /// parses it via `tryParseTriggerBody` and returns `None` on
    /// failure (silently dropping unparseable definitions).
    /// H-019 (Cluster A — Close the loops).
    | CreateTrigger of definition: string
    /// `ALTER TABLE <table> DISABLE TRIGGER <name>`. Emitted by
    /// `SsdtDdlEmitter` after the owning `CreateTrigger` when
    /// `Trigger.IsDisabled = true`. Preserves the deployed target's
    /// trigger-disabled state across emit → deploy → readback.
    /// ScriptDom builds via `AlterTableTriggerModificationStatement`
    /// with `TriggerEnforcement.Disable`. Slice D.2.d (chapter D's
    /// emission-aesthetics arc).
    | AlterTableDisableTrigger of table: TableId * triggerName: string
    /// `CREATE SEQUENCE` statement. Emitted before table creation in
    /// the catalog-wide statement stream (sequences are schema objects
    /// referenced by DEFAULT constraints). Carries the V2 `Sequence`
    /// IR record; `ScriptDomBuild.buildStatement` delegates to
    /// `buildCreateSequence`. H-020 (Cluster A — Close the loops).
    | CreateSequence of seq: Sequence
