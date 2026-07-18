namespace Twin.Core

open Projection.Core

/// THE TWIN — coordinates (THE_TWIN.md §identity).
///
/// The Twin context's identity is the **logical name path**: a table is
/// `schema.table`, a column is `schema.table.column`. The ejected SSDT
/// estate carries no SsKey bindings, so names are the only identity the
/// estate itself offers — and the Twin embraces that rather than working
/// around it: every durable Twin artifact (twin.json, evidence packs,
/// corrections, scenarios) speaks coordinates only. Engine identities
/// (`SsKey`) exist inside a single process run, bound at the
/// `TwinIdentity` seam, and never reach a Twin surface (THE_VOICE §2.1:
/// identity "never shown; resolves to the name").
///
/// The wrapped name VOs are the shared kernel's `SchemaName` /
/// `TableName` / `ColumnName` (Coordinates.fs) — same 128-char SQL
/// Server identifier invariants, same structural equality. Coordinate
/// equality for *matching* purposes is case-insensitive (SQL Server
/// default-collation semantics) via the normalized `key` projections,
/// mirroring `TableId.normalizedKey`.
type TableCoordinate = {
    Schema : SchemaName
    Table  : TableName
}

/// A column within a table coordinate.
type ColumnCoordinate = {
    Table  : TableCoordinate
    Column : ColumnName
}

/// Construction, parsing, and projection for `TableCoordinate`.
///
/// The dotted text form (`dbo.Customer`) is the config/artifact wire
/// shape. Parsing is strict and total: exactly two non-blank segments,
/// no bracket quoting, no embedded dots. A logical name that would need
/// quoting is a named refusal (`twin.coordinate.unsupported`), not a
/// guess — the ejected estate's logical names are plain identifiers, and
/// an exotic name should be visible at the config boundary, not
/// half-supported.
[<RequireQualifiedAccess>]
module TableCoordinate =

    let private malformed (text: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.coordinate.table.malformed"
            "A table coordinate is 'schema.table' — exactly two dot-separated segments, both non-blank."
            (Map.ofList [ "coordinate", Some text ])

    let private unsupported (text: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.coordinate.unsupported"
            "A coordinate segment cannot carry brackets or quotes. Use the plain logical identifier."
            (Map.ofList [ "coordinate", Some text ])

    let private hasQuoting (segment: string) : bool =
        segment.IndexOfAny [| '['; ']'; '"'; '\'' |] >= 0

    /// Build from already-validated typed names. Total.
    let fromTyped (schema: SchemaName) (table: TableName) : TableCoordinate =
        { Schema = schema; Table = table }

    /// Build from raw segment strings (blank / over-length refused with
    /// the kernel VO error codes).
    let create (schema: string) (table: string) : Result<TableCoordinate> =
        match SchemaName.create schema, TableName.create table with
        | Ok s, Ok t -> Result.success { Schema = s; Table = t }
        | sR, tR -> Result.failure (Result.errors sR @ Result.errors tR)

    /// Parse the dotted text form `schema.table`.
    let parse (text: string) : Result<TableCoordinate> =
        if System.String.IsNullOrWhiteSpace text then
            Result.failureOf (malformed text)
        else
            let segments = text.Split '.'
            if segments.Length <> 2 then Result.failureOf (malformed text)
            elif segments |> Array.exists hasQuoting then Result.failureOf (unsupported text)
            else create (segments.[0].Trim()) (segments.[1].Trim())

    /// The display text — `schema.table`, original casing preserved.
    let text (c: TableCoordinate) : string =
        System.String.Concat(SchemaName.value c.Schema, ".", TableName.value c.Table)  // LINT-ALLOW: terminal coordinate display text; the dotted form IS the wire shape, no AST

    /// The case-insensitive map key (SQL Server default-collation
    /// matching). Same recipe as the kernel's `TableId.normalizedKey`
    /// so both sides of any join agree on the format.
    let key (c: TableCoordinate) : string =
        TableId.normalizedKeyOf (SchemaName.value c.Schema) (TableName.value c.Table)

    /// The kernel `TableId` projection (implicit-current-database
    /// scope — the twin is a single database by construction).
    let toTableId (c: TableCoordinate) : TableId =
        TableId.fromTyped c.Schema c.Table

    /// The coordinate of a kernel `TableId` (catalog axis dropped — the
    /// twin never addresses across databases).
    let ofTableId (id: TableId) : TableCoordinate =
        { Schema = id.Schema; Table = id.Table }

/// Construction, parsing, and projection for `ColumnCoordinate`.
[<RequireQualifiedAccess>]
module ColumnCoordinate =

    let private malformed (text: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.coordinate.column.malformed"
            "A column coordinate is 'schema.table.column' — exactly three dot-separated segments, all non-blank."
            (Map.ofList [ "coordinate", Some text ])

    /// Build from a parsed table coordinate + raw column segment.
    let create (table: TableCoordinate) (column: string) : Result<ColumnCoordinate> =
        ColumnName.create column
        |> Result.map (fun c -> { Table = table; Column = c })

    /// Parse the dotted text form `schema.table.column`.
    let parse (text: string) : Result<ColumnCoordinate> =
        if System.String.IsNullOrWhiteSpace text then
            Result.failureOf (malformed text)
        else
            let segments = text.Split '.'
            if segments.Length <> 3 then Result.failureOf (malformed text)
            else
                TableCoordinate.create (segments.[0].Trim()) (segments.[1].Trim())
                |> Result.bind (fun t -> create t (segments.[2].Trim()))

    /// The display text — `schema.table.column`, original casing preserved.
    let text (c: ColumnCoordinate) : string =
        System.String.Concat(TableCoordinate.text c.Table, ".", ColumnName.value c.Column)  // LINT-ALLOW: terminal coordinate display text; the dotted form IS the wire shape, no AST

    /// The case-insensitive map key. Column segment lower-cased with the
    /// same invariant-culture recipe as the table key.
    let key (c: ColumnCoordinate) : string =
        System.String.Concat(TableCoordinate.key c.Table, ".", (ColumnName.value c.Column).ToLowerInvariant())  // LINT-ALLOW: terminal normalized comparison key (lowercased dotted path); the key IS a string, no AST
