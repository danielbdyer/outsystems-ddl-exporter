namespace Projection.Core

/// Per session-36 audit (Agents 1, 2, 3 multi-axis confirmation):
/// the schema-coordinate context. `TableId` was duplicated at three
/// sites — `PhysicalRealization` in `Catalog.fs`, the SSDT-local
/// `TableId` in `Statement.fs`, and four `(Schema, Table)` re-spellings
/// in `PhysicalSchema.fs` — plus `(string, string, string)` 6-tuples
/// in `Adapters.Sql/ReadSide.fs`.
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
/// to confuse with one another (or with a raw string).
///
/// **Stage 2 — CASHED for the logical-IR coordinate triad (2026-06-02 lift
/// slices 5a + 5b)**. `TableId.Schema` / `TableId.Table` /
/// `ColumnRealization.ColumnName` are now `SchemaName` / `TableName` /
/// `ColumnName` typed (the `string` fields retired). The logical-IR
/// coordinate identity is type-complete: the compiler refuses any
/// schema-vs-table-vs-column identifier confusion at every read-or-write
/// site touching `kind.Physical` or `attribute.Column`. Trigger fired:
/// the eight-axis F# best-practices audit
/// (`AUDIT_2026_06_02_FSHARP_EIGHT_AXIS_REDTEAM.md`) classified the
/// decorative-VOs-without-field-adoption pattern as the largest single
/// principle-debt in the codebase (`§4.2.1`); the lift cleared it for
/// the logical IR. Construction sites flow through `TableId.create` /
/// `ColumnRealization.create` (Result-returning); boundary code
/// unwraps via `TableId.schemaText` / `tableText` / `qualifiedParts` /
/// `ColumnRealization.columnNameText` helpers.
///
/// **Lift discipline learned (2026-06-02; documented for future
/// lifts)**: the F# compiler catches type-mix-up errors at *typed* call
/// sites but NOT at `String.Concat` / `String.Join` / `SqlParameter`
/// boundaries that accept `object` (these would silently call
/// `ToString` and emit `SchemaName "dbo"` instead of `dbo` at runtime,
/// or fail SQL parameter binding with "No mapping exists from type
/// Projection.Core.SchemaName to a known native type"). After every
/// future typed-VO field lift, **explicitly grep**:
///   - `String.Concat\|String.Join` with VO-bearing expressions
///   - `Parameters.AddWithValue` with VO-bearing arguments
/// and unwrap each. Slice 5a found one such site; slice 5b found two.
///
/// **Deliberate asymmetry (documented as a scope choice, not an
/// oversight)**: `PhysicalSchema`'s `PhysicalColumn` /
/// `LogicalNameBinding` / `PhysicalForeignKey` types still carry
/// `Schema`/`Table`/`Column` as `string`. Per the audit's late-stage
/// re-prioritization (2026-06-02 — the user observed development is
/// "most of the way through" so the compounding-type-safety case for
/// further lifts has shrunk), these types stay string-typed by design:
/// they're a *separate IR domain* (the physical-recovery comparison
/// surface, not the logical IR), and string-as-comparison-key is a
/// defensible boundary representation for "what SQL Server's catalog
/// reports back." `Sequence.Schema`/`DataType` are similarly deferred
/// (smaller, isolated, low symmetry payoff). The Stage 2 cash-out is
/// **logical-IR-complete; physical-comparison-domain-deliberate-defer**.
/// Trigger to revisit: an actual cross-domain identifier-confusion bug,
/// or the next major IR-shape pass that touches these types.

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

/// The identifier-length budget for GENERATED constraint names
/// (reconciliation slice 3b, DECISIONS 2026-06-13; V1's
/// `ConstraintNameNormalizer` hash-truncation discipline, ported).
/// SQL Server caps identifiers at 128 chars; V2's synthesized names
/// (`FK_<Owner>_<Target>_<SourceColumn>`, `PK_<Kind>_<KeyColumn…>`) can
/// overflow on long logical names.
[<RequireQualifiedAccess>]
module IdentifierBudget =

    /// Fit a generated identifier into the 128-char budget.
    /// ≤128 passes through byte-identical (the overwhelmingly common
    /// case — T1's byte-stability claim is untouched for it). Over:
    /// truncate to 115 chars + `_` + the first 12 lowercase-hex chars
    /// of SHA-256(full name) = exactly 128. Deterministic (the hash is
    /// a pure function of the full un-truncated name), prefix-
    /// preserving (the readable head survives), and collision-safe in
    /// practice (two distinct over-budget names share a truncated head
    /// only if their first 115 chars agree, and then differ in the
    /// 48-bit hash of their full text).
    let fit (name: string) : string =
        if name.Length <= CoordinatesLimits.SqlServerIdentifierMaxLength then
            name
        else
            use sha = System.Security.Cryptography.SHA256.Create()
            let hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes name)
            let hex = System.Convert.ToHexString(hash, 0, 6).ToLowerInvariant()
            let keep = CoordinatesLimits.SqlServerIdentifierMaxLength - 1 - hex.Length
            System.String.Concat(name.Substring(0, keep), "_", hex)  // LINT-ALLOW: terminal identifier-budget composition; segments are the truncated head + the deterministic hash suffix; BCL String.Concat IS the use-case-specific library at this naming boundary

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
        /// V1's `db_catalog` (3-part SQL identifier prefix). `None` for the
        /// implicit-current-database scope. Not yet VO-typed: cross-database
        /// catalog names are rare in V2's source and would require a
        /// `CatalogName` VO (none currently defined). Defer-with-trigger:
        /// a real cross-database FK landing in a fixture or a
        /// schema/catalog-vs-other-name mix-up bug.
        Catalog : string option
        Schema  : SchemaName
        Table   : TableName
    }

/// Smart constructors and projections for `TableId`. Per the
/// codebase's structural-commitment-via-construction-validation
/// principle (`AXIOMS.md` operational principle): blank schema or
/// blank table is rejected at construction; downstream consumers
/// trust the value.
[<RequireQualifiedAccess>]
module TableId =

    /// Boundary contract: translate SchemaName-level / TableName-level
    /// error codes into TableId-level error codes so consumers triaging
    /// errors by code (e.g., `RenameBinding.fromConfig` asserting
    /// `tableId.schema.empty`) see the outer-context error vocabulary.
    /// The inner-VO codes (`schemaName.empty` / `tableName.tooLong`) are
    /// the structural truth at the SchemaName/TableName layer; the outer
    /// codes name the TableId context the construction failed in.
    let private translateSchemaErrors (es: ValidationError list) : ValidationError list =
        es |> List.map (fun e ->
            match e.Code with
            | "schemaName.empty" -> { e with Code = "tableId.schema.empty"; Message = "TableId schema cannot be blank or whitespace." }
            | "schemaName.tooLong" -> { e with Code = "tableId.schema.tooLong" }
            | _ -> e)

    let private translateTableErrors (es: ValidationError list) : ValidationError list =
        es |> List.map (fun e ->
            match e.Code with
            | "tableName.empty" -> { e with Code = "tableId.table.empty"; Message = "TableId table cannot be blank or whitespace." }
            | "tableName.tooLong" -> { e with Code = "tableId.table.tooLong" }
            | _ -> e)

    /// Build a `TableId` from raw strings. Aggregates validation errors
    /// from `SchemaName.create` and `TableName.create` (blank /
    /// over-length), re-coded into the TableId error vocabulary
    /// (`tableId.schema.empty` / `tableId.table.empty` /
    /// `tableId.schema.tooLong` / `tableId.table.tooLong`); `Catalog`
    /// defaults to `None` (implicit-current-database scope; V1's
    /// `db_catalog: null` parity).
    let create (schema: string) (table: string) : Result<TableId> =
        match SchemaName.create schema, TableName.create table with
        | Ok s, Ok t -> Result.success { Catalog = None; Schema = s; Table = t }
        | Error es, Ok _ -> Result.failure (translateSchemaErrors es)
        | Ok _, Error et -> Result.failure (translateTableErrors et)
        | Error es, Error et -> Result.failure (translateSchemaErrors es @ translateTableErrors et)

    /// Build a `TableId` that carries an explicit catalog coordinate.
    /// Chapter A.0' slice θ — used by future adapters that surface
    /// cross-database FK targets (DACPAC, OData) or live-SQL paths
    /// that read `INFORMATION_SCHEMA.TABLES.TABLE_CATALOG`. Empty /
    /// whitespace `catalog` is rejected; pass `TableId.create` for
    /// the implicit-current-database scope instead.
    let createWithCatalog (catalog: string) (schema: string) (table: string) : Result<TableId> =
        let catalogErrors =
            Validation.nonBlank "tableId.catalog.empty" "TableId catalog, when present, cannot be blank or whitespace." catalog
        match SchemaName.create schema, TableName.create table, catalogErrors with
        | Ok s, Ok t, [] -> Result.success { Catalog = Some catalog; Schema = s; Table = t }
        | sR, tR, cErrs ->
            let sErrs = match sR with Error es -> translateSchemaErrors es | Ok _ -> []
            let tErrs = match tR with Error et -> translateTableErrors et | Ok _ -> []
            Result.failure (cErrs @ sErrs @ tErrs)

    /// Build a `TableId` from already-validated `SchemaName` / `TableName`
    /// values. Boundary helper for code that has already constructed the
    /// typed names (avoids the round-trip-through-string that
    /// `create` would force). Total — no validation needed since the
    /// inputs are already typed.
    let fromTyped (schema: SchemaName) (table: TableName) : TableId =
        { Catalog = None; Schema = schema; Table = table }

    /// Build a `TableId` with an explicit `Catalog` from already-validated
    /// typed names + raw catalog string. Catalog still goes through
    /// `Validation.nonBlank` (no `CatalogName` VO yet); returns `Result`
    /// because the catalog string may fail validation.
    let fromTypedWithCatalog (catalog: string) (schema: SchemaName) (table: TableName) : Result<TableId> =
        let errors =
            Validation.nonBlank "tableId.catalog.empty" "TableId catalog, when present, cannot be blank or whitespace." catalog
        if List.isEmpty errors then
            Result.success { Catalog = Some catalog; Schema = schema; Table = table }
        else
            Result.failure errors

    /// PL-3 (S23) — the DEPLOY-target projection: the same coordinates
    /// with the source catalog stripped (data-lane statements address the
    /// sink's current database, never a cross-catalog name). Named once;
    /// the data lane previously rebuilt this record inline at four sites.
    let withoutCatalog (t: TableId) : TableId =
        { Schema = t.Schema; Table = t.Table; Catalog = None }

    /// Boundary helper — pre-unwrapped schema text. Use at adapter /
    /// emitter / diagnostic-formatting boundaries that need the raw
    /// identifier string (SQL identifier encoding, sprintf "%s",
    /// map lookups keyed on `(string, string)` tuples).
    let schemaText (t: TableId) : string = SchemaName.value t.Schema

    /// Boundary helper — pre-unwrapped table text. See `schemaText`.
    let tableText (t: TableId) : string = TableName.value t.Table

    /// Boundary helper — `(schema, table)` pair as raw strings for
    /// adapter sites that need both at once (SQL identifier
    /// encoding, qualified-name formatting, map lookups).
    let qualifiedParts (t: TableId) : string * string = (schemaText t, tableText t)

    /// The case-insensitive `schema.table` MAP KEY — lower-cased, dot-joined.
    /// The single definition site for the normalized key used to match a
    /// `TableId` against a name read from SQL Server (`sys.foreign_keys` etc.),
    /// so both sides agree on the format. `normalizedKeyOf` is the same recipe
    /// over raw schema/table strings (the catalog-less reflected side).
    let normalizedKeyOf (schema: string) (table: string) : string =
        System.String.Concat(schema.ToLowerInvariant(), ".", table.ToLowerInvariant())  // LINT-ALLOW: terminal normalized-key string (schema.table lowercased); the comparison key IS a string, no AST

    /// The normalized `schema.table` key for a `TableId`. See `normalizedKeyOf`.
    let normalizedKey (t: TableId) : string =
        normalizedKeyOf (schemaText t) (tableText t)

    /// Does this id's table name equal `name` under SQL Server's
    /// default-collation semantics (case-insensitive)? The one name for
    /// the physical-table-identifier comparison: a raw `=` is the latent
    /// bug (`CONSTELLATION_BACKLOG.md` plane N3 — `TransferSpec` already
    /// compares case-insensitively; `CatalogResolution` had drifted to
    /// case-sensitive `=`, silently failing to resolve a differently-cased
    /// operator ref to a table SQL Server treats as the same).
    let tableTextEquals (name: string) (t: TableId) : bool =
        System.String.Equals(tableText t, name, System.StringComparison.OrdinalIgnoreCase)

    /// Does this id's schema name equal `name` under default-collation
    /// (case-insensitive) semantics? Companion to `tableTextEquals`.
    let schemaTextEquals (name: string) (t: TableId) : bool =
        System.String.Equals(schemaText t, name, System.StringComparison.OrdinalIgnoreCase)

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
