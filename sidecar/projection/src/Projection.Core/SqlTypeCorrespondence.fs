namespace Projection.Core

/// Bounded context: SQL Server type correspondence (chapter-3.7
/// slice β; audit Tier-1 #8). Owns the round-trip pair
/// `PrimitiveType ↔ SQL Server DDL type vocabulary` as a single
/// algebraic surface.
///
/// Forward direction (Π's job): given a V2 `PrimitiveType`, name the
/// canonical SQL Server DDL base type (`INT`, `NVARCHAR`, `DECIMAL`,
/// ...). Consumed by `Projection.Targets.SSDT.Render.columnSqlType`
/// for SQL text emission and by
/// `Projection.Targets.SSDT.ScriptDomBuild.sqlDataTypeOption` as a
/// witness that the typed-AST option name agrees with the canonical
/// base name.
///
/// Inverse direction (adapter's job): given a SQL Server `DATA_TYPE`
/// string from `INFORMATION_SCHEMA.COLUMNS`, parse to V2 `PrimitiveType`.
/// Consumed by `Projection.Adapters.Sql.ReadSide.mapSqlType` (formerly
/// the inverse's home; now a thin alias).
///
/// **Why the lift:** the prior split — `Render.columnSqlType` (forward,
/// SSDT) plus `ReadSide.mapSqlType` (inverse, Sql adapter) — owned
/// independent per-`PrimitiveType` match expressions with no shared
/// ground truth. T1 byte-determinism rested on conventional inversion:
/// a developer adding a new `PrimitiveType` variant had to remember
/// to update both sites symmetrically. After this slice, the table
/// here is the single source of truth; `baseName` and `ofSqlDataType`
/// share the same closed-DU dispatch shape, and the round-trip
/// property `ofSqlDataType (baseName pt) = Result.success pt` is
/// asserted as a property test.
///
/// **Hexagonal placement:** Core hosts the table because `PrimitiveType`
/// is Core's vocabulary AND the SQL-string side is pure data (no I/O,
/// no time, no concurrency). Precedent: `RawValueCodec` (also
/// SQL-Server-specific raw-value conventions in Core, lifted at
/// chapter 3.6 from three sites). The audit named the bounded context
/// `Projection.TypeCorrespondence`; a separate project would be
/// warranted only if a non-SQL-Server target adapter materialized
/// (DacFx counts as same-target — both are SQL Server; OData / GraphQL
/// would trigger the lift to a sibling project).
///
/// **Big-O:** `baseName` is O(1) closed-DU dispatch. `ofSqlDataType`
/// is O(1) closed-string dispatch over a fixed-size match expression
/// (the SQL Server vocabulary is bounded by the supported
/// `INFORMATION_SCHEMA.DATA_TYPE` values). No allocation in the happy
/// path; one `ValidationError` allocation on the unknown-type fallback.
[<RequireQualifiedAccess>]
module SqlTypeCorrespondence =

    /// Canonical SQL Server DDL base-name for a V2 `PrimitiveType`.
    /// The "base name" is the type without parameterization —
    /// `Text` renders as `NVARCHAR` (or `NVARCHAR(N)` once Render
    /// adds the length parameter). Used as the round-trip witness
    /// for `ofSqlDataType` and as the canonical base for the
    /// `SqlDataTypeOption` name in ScriptDom emission.
    let baseName (typ: PrimitiveType) : string =
        match typ with
        | Integer  -> "INT"
        | Decimal  -> "DECIMAL"
        | Text     -> "NVARCHAR"
        | Boolean  -> "BIT"
        | DateTime -> "DATETIME2"
        | Date     -> "DATE"
        | Time     -> "TIME"
        | Binary   -> "VARBINARY"
        | Guid     -> "UNIQUEIDENTIFIER"

    /// Inverse classification: SQL Server `INFORMATION_SCHEMA.DATA_TYPE`
    /// value → V2 `PrimitiveType`. The SQL Server vocabulary is
    /// broader than V2's (`BIGINT` / `SMALLINT` / `TINYINT` all
    /// collapse to `Integer`; `MONEY` / `SMALLMONEY` collapse to
    /// `Decimal`; `IMAGE` / `BINARY` collapse to `Binary`); the
    /// match below names every alias the adapter recognizes.
    ///
    /// Returns `Error` for unknown types — surfaces an emitter-IR
    /// mismatch the canary's blocking semantic catches. M4's
    /// Tolerance taxonomy can elevate accepted-but-unmapped types
    /// to a tolerance flag.
    ///
    /// Round-trip: `ofSqlDataType (baseName pt) = Result.success pt`
    /// for every `PrimitiveType pt`. Asserted by property test in
    /// `tests/Projection.Tests/SqlTypeCorrespondenceTests.fs`.
    let ofSqlDataType (dataType: string) : Result<PrimitiveType> =
        match dataType.ToUpperInvariant() with
        | "INT" | "BIGINT" | "SMALLINT" | "TINYINT" ->
            Result.success Integer
        | "DECIMAL" | "NUMERIC" | "MONEY" | "SMALLMONEY" ->
            Result.success Decimal
        | "NVARCHAR" | "VARCHAR" | "CHAR" | "NCHAR" | "TEXT" | "NTEXT" ->
            Result.success Text
        | "BIT" ->
            Result.success Boolean
        | "DATETIME" | "DATETIME2" | "SMALLDATETIME" | "DATETIMEOFFSET" ->
            Result.success DateTime
        | "DATE" ->
            Result.success Date
        | "TIME" ->
            Result.success Time
        | "VARBINARY" | "BINARY" | "IMAGE" ->
            Result.success Binary
        | "UNIQUEIDENTIFIER" ->
            Result.success Guid
        | unknown ->
            // Operator-facing diagnostic message: typed segments via
            // `String.Concat`; no `sprintf`. Same observable string the
            // legacy `ReadSide.mapSqlType` produced.
            Result.failureOf (
                ValidationError.create
                    "sqlTypeCorrespondence.unknown"
                    (System.String.Concat(  // LINT-ALLOW: terminal diagnostic-text emission boundary; segments are typed (literal + bound `unknown`)
                        "INFORMATION_SCHEMA.DATA_TYPE = '",
                        unknown,
                        "' has no V2 PrimitiveType mapping. ",
                        "Either extend SqlTypeCorrespondence.ofSqlDataType ",
                        "or add a Tolerance flag (M4).")))

    /// Enumerate every `PrimitiveType` variant. Used by property tests
    /// to assert round-trip exhaustiveness without an FsCheck
    /// arbitrary; the closed-DU expansion empirical-test discipline
    /// (`DECISIONS 2026-05-13`) relies on `baseName` and `ofSqlDataType`
    /// staying total under variant-addition, and this enumeration is
    /// the test-time witness.
    let allPrimitives : PrimitiveType list =
        [ Integer; Decimal; Text; Boolean; DateTime; Date; Time; Binary; Guid ]
