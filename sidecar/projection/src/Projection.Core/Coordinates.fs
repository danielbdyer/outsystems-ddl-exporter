namespace Projection.Core

/// Per session-36 audit (Agents 1, 2, 3 multi-axis confirmation):
/// the schema-coordinate context. `TableId` was duplicated at three
/// sites — `PhysicalRealization` in `Catalog.fs`, the SSDT-local
/// `TableId` in `Statement.fs`, and four `(Schema, Table)` re-spellings
/// in `PhysicalSchema.fs` — plus `(string, string, string)` 6-tuples
/// in `Adapters.Sql/{ProfileSnapshot,ProfileStatistics,ReadSide}.fs`.
/// Lifting the value object to Core gives every adapter / emitter a
/// shared coordinate vocabulary and centralizes the smart-constructor
/// invariants.
///
/// **Stage 1**: this module names the `TableId` shape with structural
/// identity (`{ Schema: string; Table: string }`) and a smart
/// constructor enforcing non-blank components. `PhysicalRealization`
/// becomes a type alias for `TableId`, so all `kind.Physical.Schema`
/// readers compile unchanged. The SSDT-local duplicate retires.
///
/// **Stage 2 (chapter 5 slice θ; partial cash-out 2026-05-11)**: the
/// typed `SchemaName` / `TableName` / `ColumnName` value objects land
/// as smart constructors below — distinct types the compiler refuses
/// to confuse with one another (or with a raw string). The
/// `PhysicalRealization` / `Column` record-field migration to the
/// typed VOs stays **deferred-with-trigger** at this slice: the typed
/// surface is opt-in for new code; existing `string`-field readers
/// keep compiling. **Trigger for the full migration**: a real bug
/// caught (schema-vs-table confusion at a boundary) OR adapter-ripple
/// cost dominated by safety win at the next adapter.

/// Shared constants for the typed schema-coordinate VOs. Kept in
/// its own module so the `[<Literal>]` can be referenced from each
/// of `SchemaName` / `TableName` / `ColumnName` smart constructors.
module internal CoordinatesLimits =
    /// Maximum length per SQL Server identifier limits
    /// (`https://learn.microsoft.com/en-us/sql/relational-databases/
    /// databases/database-identifiers`). Applies uniformly to schema /
    /// table / column names; bracket-quoted identifiers can carry
    /// almost any character but the byte-length cap is structural.
    [<Literal>]
    let SqlServerIdentifierMaxLength : int = 128

/// SQL Server schema-namespace name. Single-case DU wrapping a
/// validated string; the constructor `SchemaName.create` is the only
/// path to a value. Two `SchemaName`s are equal iff their underlying
/// strings match (structural equality on the wrapped value).
type SchemaName = private SchemaName of string

/// SQL Server table name (within a schema). Single-case DU wrapping a
/// validated string; mirrors `SchemaName` structurally; the compiler
/// refuses to substitute a `TableName` for a `SchemaName` (or for any
/// raw `string`).
type TableName = private TableName of string

/// SQL Server column name (within a table). Single-case DU wrapping a
/// validated string; mirrors `SchemaName` / `TableName` structurally;
/// the compiler refuses to substitute a `ColumnName` for a
/// `SchemaName` / `TableName` (or for any raw `string`).
type ColumnName = private ColumnName of string

/// Smart constructor + projection for `SchemaName`. Per the
/// structural-commitment-via-construction-validation principle:
/// blank / over-length input rejects at construction; downstream
/// consumers trust the value via `.value`.
[<RequireQualifiedAccess>]
module SchemaName =

    let private blank =
        ValidationError.create
            "schemaName.empty"
            "SchemaName cannot be blank."

    let private tooLong =
        ValidationError.create
            "schemaName.tooLong"
            "SchemaName exceeds the SQL Server identifier length limit (128 chars)."

    let create (raw: string) : Result<SchemaName> =
        if System.String.IsNullOrWhiteSpace raw then
            Result.failureOf blank
        elif raw.Length > CoordinatesLimits.SqlServerIdentifierMaxLength then
            Result.failureOf tooLong
        else
            Result.success (SchemaName raw)

    let value (SchemaName s) : string = s

/// Smart constructor + projection for `TableName`. Mirrors
/// `SchemaName` structurally; rejection reasons are
/// `tableName.empty` / `tableName.tooLong`.
[<RequireQualifiedAccess>]
module TableName =

    let private blank =
        ValidationError.create
            "tableName.empty"
            "TableName cannot be blank."

    let private tooLong =
        ValidationError.create
            "tableName.tooLong"
            "TableName exceeds the SQL Server identifier length limit (128 chars)."

    let create (raw: string) : Result<TableName> =
        if System.String.IsNullOrWhiteSpace raw then
            Result.failureOf blank
        elif raw.Length > CoordinatesLimits.SqlServerIdentifierMaxLength then
            Result.failureOf tooLong
        else
            Result.success (TableName raw)

    let value (TableName t) : string = t

/// Smart constructor + projection for `ColumnName`. Mirrors
/// `SchemaName` / `TableName` structurally; rejection reasons are
/// `columnName.empty` / `columnName.tooLong`.
[<RequireQualifiedAccess>]
module ColumnName =

    let private blank =
        ValidationError.create
            "columnName.empty"
            "ColumnName cannot be blank."

    let private tooLong =
        ValidationError.create
            "columnName.tooLong"
            "ColumnName exceeds the SQL Server identifier length limit (128 chars)."

    let create (raw: string) : Result<ColumnName> =
        if System.String.IsNullOrWhiteSpace raw then
            Result.failureOf blank
        elif raw.Length > CoordinatesLimits.SqlServerIdentifierMaxLength then
            Result.failureOf tooLong
        else
            Result.success (ColumnName raw)

    let value (ColumnName c) : string = c

/// Schema-table coordinate. The composite identity for a kind's
/// physical realization. Two `TableId`s are equal iff their catalog,
/// schema, and table strings match exactly.
///
/// **Chapter A.0' slice θ — Catalog extension (L3-S10 / L3-I10).**
/// `Catalog : string option` carries the V1 `db_catalog` field (the
/// 3-part SQL identifier prefix). `None` represents the
/// implicit-current-catalog form V1 projects today (most V2 sources
/// emit `db_catalog: null`); explicit cross-database references land
/// as `Some catalog`. Cross-database FK detection now has typed
/// support; pre-slice-θ the catalog axis silently degraded to the
/// implicit-current-database scope.
type TableId =
    {
        Catalog : string option
        Schema  : string
        Table   : string
    }

/// Smart constructors and projections for `TableId`. Per the
/// codebase's structural-commitment-via-construction-validation
/// principle (`AXIOMS.md` operational principle): blank schema or
/// blank table is rejected at construction; downstream consumers
/// trust the value.
[<RequireQualifiedAccess>]
module TableId =

    /// Build a `TableId` from raw strings. Rejects blanks; aggregates
    /// errors when fields are blank. `Catalog` defaults to `None`
    /// (implicit-current-database scope; V1's
    /// `db_catalog: null` parity).
    let create (schema: string) (table: string) : Result<TableId> =
        let errors =
            Validation.nonBlank "tableId.schema.empty" "TableId schema cannot be blank or whitespace." schema
            @ Validation.nonBlank "tableId.table.empty" "TableId table cannot be blank or whitespace." table
        if List.isEmpty errors then
            Result.success { Catalog = None; Schema = schema; Table = table }
        else
            Result.failure errors

    /// Build a `TableId` that carries an explicit catalog coordinate.
    /// Chapter A.0' slice θ — used by future adapters that surface
    /// cross-database FK targets (DACPAC, OData) or live-SQL paths
    /// that read `INFORMATION_SCHEMA.TABLES.TABLE_CATALOG`. Empty /
    /// whitespace `catalog` is rejected; pass `TableId.create` for
    /// the implicit-current-database scope instead.
    let createWithCatalog (catalog: string) (schema: string) (table: string) : Result<TableId> =
        let errors =
            Validation.nonBlank "tableId.catalog.empty" "TableId catalog, when present, cannot be blank or whitespace." catalog
            @ Validation.nonBlank "tableId.schema.empty" "TableId schema cannot be blank or whitespace." schema
            @ Validation.nonBlank "tableId.table.empty" "TableId table cannot be blank or whitespace." table
        if List.isEmpty errors then
            Result.success { Catalog = Some catalog; Schema = schema; Table = table }
        else
            Result.failure errors

    // Per chapter 3.5 deep audit (2026-05-09): the bracket-quoted
    // SQL identifier form `[schema].[table]` is a SQL-rendering
    // concern, not a Core concern. The canonical bracket-quoting
    // implementation is `Microsoft.SqlServer.TransactSql.ScriptDom`'s
    // `Identifier.EncodeIdentifier(string)` static method (the
    // use-case-specific library for SQL identifier handling). Per
    // hexagonal architecture, ScriptDom belongs in SSDT, not Core.
    //
    // The `qualified` helper retired from Core — `TableId` itself
    // remains as the structural value object; SQL rendering moves
    // to `Projection.Targets.SSDT/Render.tableQualified`, which
    // delegates to ScriptDom. Consumers (`RefactorLogEmitter`,
    // `Render`, `Bulk.copyRows`) call `Render.tableQualified`
    // directly.
