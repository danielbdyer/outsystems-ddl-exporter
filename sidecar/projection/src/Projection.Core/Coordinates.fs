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
/// **Stage 2 (deferred)**: typed `SchemaName` / `TableName` /
/// `ColumnName` value objects so the compiler refuses to confuse a
/// schema with a table or a column. Triggers when a real bug would
/// have been caught (or when the cost of the explicit `value`
/// projections is exceeded by the safety win at the next adapter).

/// Schema-table coordinate. The composite identity for a kind's
/// physical realization. Two `TableId`s are equal iff their schema
/// and table strings match exactly.
type TableId =
    {
        Schema : string
        Table : string
    }

/// Smart constructors and projections for `TableId`. Per the
/// codebase's structural-commitment-via-construction-validation
/// principle (`AXIOMS.md` operational principle): blank schema or
/// blank table is rejected at construction; downstream consumers
/// trust the value.
[<RequireQualifiedAccess>]
module TableId =

    let private schemaBlank =
        ValidationError.create
            "tableId.schema.empty"
            "TableId schema cannot be blank."

    let private tableBlank =
        ValidationError.create
            "tableId.table.empty"
            "TableId table cannot be blank."

    /// Build a `TableId` from raw strings. Rejects blanks; aggregates
    /// errors when both fields are blank.
    let create (schema: string) (table: string) : Result<TableId> =
        let schemaErr =
            if System.String.IsNullOrWhiteSpace schema then [ schemaBlank ] else []
        let tableErr =
            if System.String.IsNullOrWhiteSpace table then [ tableBlank ] else []
        match schemaErr @ tableErr with
        | [] -> Result.success { Schema = schema; Table = table }
        | errs -> Result.failure errs

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
