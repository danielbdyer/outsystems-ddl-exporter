namespace Projection.Pipeline

// LINT-ALLOW-FILE-MUTATION: BCL SqlBulkCopy and SqlCommand mutable surfaces (DestinationTableName,
//   CommandText etc.). BCL forces the shape; mutation contained per-call.

open System
open System.Data
open System.Globalization
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Targets.SSDT

/// Bulk realization helpers for `Deploy.executeStream`. Per session-34
/// — the bulk path lives in the realization layer (not in Π); inverts
/// the IR's raw-string convention back to CLR types so SqlBulkCopy
/// can write rows in their native form. The parser is the dual of
/// `Render.formatSqlLiteral` and `ReadSide.formatRawValue`; together
/// the three round-trip a value through SQL → IR raw → SQL without
/// loss for V2's PrimitiveType vocabulary.
[<RequireQualifiedAccess>]
module Bulk =

    /// Map V2 `PrimitiveType` to the CLR type SqlBulkCopy uses when
    /// the source DataTable column carries that type. Aligns with
    /// SQL Server's default type coercions.
    let clrType (t: PrimitiveType) : Type =
        match t with
        | Integer  -> typeof<int64>
        | Decimal  -> typeof<decimal>
        | Boolean  -> typeof<bool>
        | DateTime -> typeof<DateTime>
        | Date     -> typeof<DateTime>
        | Time     -> typeof<TimeSpan>
        | Guid     -> typeof<Guid>
        | Text     -> typeof<string>
        | Binary   -> typeof<byte[]>

    /// Inverse of `Render.formatSqlLiteral` and the dual of
    /// `ReadSide.formatRawValue`: parse the IR raw cell back into
    /// the CLR object SqlBulkCopy expects. WP-3 (F11): NULL is
    /// carried out-of-band — `None` → `DBNull`; a `Some ""` `Text`
    /// value is a genuine empty string and writes as one (the 6.A.4
    /// tolerance `EmptyTextNormalizedToNull` is retired). A `Some ""`
    /// on a non-Text type falls into its parser, which throws the
    /// same loud `FormatException` any malformed raw does (NM-20).
    /// All format rules (DateTime / Date / Boolean canonical, Hex
    /// prefix) flow through `RawValueCodec` so the V2 raw-form
    /// contract has a single source of truth.
    let parseRaw (t: PrimitiveType) (raw: string option) : obj | null =
        match raw with
        | None -> box DBNull.Value
        | Some raw ->
            let inv = CultureInfo.InvariantCulture
            match t with
            | Integer ->
                box (Int64.Parse(raw, inv))
            | Decimal ->
                // WP-17(a) — a `float`/`real` column's G17/G9 raw can
                // carry an exponent beyond decimal's range (|x| ≳
                // 7.9E28 → `Decimal.Parse` OVERFLOWS, the S1 write
                // loss) or E-notation decimal cannot parse. Dispatch on
                // the shape: scientific notation parses as the exact
                // IEEE double (SqlBulkCopy converts to the float/real
                // column faithfully); plain digit runs keep the exact
                // decimal parse (decimal-family columns, and G17 forms
                // without exponent — decimal carries ≤28 significant
                // digits exactly, and the nearest-double conversion at
                // the column recovers the original IEEE value).
                if raw.IndexOfAny [| 'E'; 'e' |] >= 0 then
                    box (Double.Parse(raw, NumberStyles.Float, inv))
                else
                    box (Decimal.Parse(raw, inv))
            | Boolean ->
                box (RawValueCodec.parseBoolean raw)
            | DateTime ->
                // WP-17(b) — an offset-bearing raw (a `datetimeoffset`
                // column's faithful carriage) parses back to the exact
                // `DateTimeOffset`; SqlBulkCopy writes it to the
                // `datetimeoffset` column offset-intact. The offset-less
                // canonical form keeps its exact parse.
                if RawValueCodec.hasUtcOffset raw then
                    box (RawValueCodec.parseDateTimeOffset raw)
                else
                    box (DateTime.ParseExact(raw, RawValueCodec.DateTimeFormat, inv))
            | Date ->
                box (DateTime.ParseExact(raw, RawValueCodec.DateFormat, inv))
            | Time ->
                box (TimeSpan.Parse(raw, inv))
            | Guid ->
                box (Guid.Parse raw)
            | Text ->
                box raw
            | Binary ->
                box (Convert.FromHexString (RawValueCodec.stripHexPrefix raw))

    /// Parse one cell to the CLR object SqlBulkCopy expects, coalescing the
    /// nullable `parseRaw` result to a non-null `obj` (`DBNull` for the absent
    /// case) so the reader's row buffer stays `obj[]`.
    let private cellToObj (cv: CellValue) : obj =
        match parseRaw cv.Type cv.Raw with
        | null -> DBNull.Value :> obj
        | v    -> v

    /// The named contract for `CellValueDataReader`'s unused typed getters
    /// (SqlBulkCopy's streaming path calls `GetValue`, never these). Module-level
    /// so it generalizes to `unit -> 'a`.
    let private ns () : 'a = raise (NotSupportedException "CellValueDataReader: SqlBulkCopy uses GetValue; typed getters are unsupported")

    /// An `IDataReader` over a homogeneous batch of `CellValue` rows — the
    /// streaming source for `SqlBulkCopy.WriteToServerAsync`. Replaces the prior
    /// `DataTable`, which held EVERY row's `obj[]` + `DataRow` simultaneously
    /// (the whole batch resident a SECOND time, on top of the caller's
    /// `CellValue list list`). The reader parses ONE row at a time via
    /// `parseRaw`, so peak client memory per batch is O(1 row), not O(batch) —
    /// the reverse leg's per-row allocation hot-spot on the estate-scale load.
    /// SqlBulkCopy's streaming path uses `Read` / `FieldCount` / `GetValue` /
    /// `GetName` / `GetOrdinal` / `GetFieldType` / `IsDBNull`; the typed getters
    /// it never calls raise (the named contract, not a silent stub).
    type private CellValueDataReader(shape: CellValue list, rows: CellValue list list) =
        let cols = List.toArray shape
        let rowArr = List.toArray rows
        let mutable idx = -1
        let mutable current : obj[] = Array.empty
        let ordinalOf (name: string) : int =
            match cols |> Array.tryFindIndex (fun c -> c.Column = name) with
            | Some i -> i
            | None   -> raise (IndexOutOfRangeException name)
        interface IDataReader with
            member _.Read() : bool =
                idx <- idx + 1
                if idx < rowArr.Length then
                    current <- rowArr.[idx] |> List.map cellToObj |> List.toArray
                    true
                else false
            member _.NextResult() : bool = false
            member _.Depth : int = 0
            member _.IsClosed : bool = false
            member _.RecordsAffected : int = -1
            member _.Close() : unit = ()
            member _.GetSchemaTable() = null
        interface IDataRecord with
            member _.FieldCount : int = cols.Length
            member _.GetName(i: int) : string = cols.[i].Column
            member _.GetOrdinal(name: string) : int = ordinalOf name
            member _.GetFieldType(i: int) : Type = clrType cols.[i].Type
            member _.GetDataTypeName(i: int) : string = (clrType cols.[i].Type).Name
            member _.GetValue(i: int) : obj = current.[i]
            member _.IsDBNull(i: int) : bool = (current.[i] :? DBNull)
            member _.GetValues(values: obj[]) : int =
                let n = min values.Length current.Length
                Array.blit current 0 values 0 n
                n
            member _.Item with get (i: int) : obj = current.[i]
            member _.Item with get (name: string) : obj = current.[ordinalOf name]
            member _.GetBoolean(_: int) : bool = ns ()
            member _.GetByte(_: int) : byte = ns ()
            member _.GetBytes(_: int, _: int64, _: byte[] | null, _: int, _: int) : int64 = ns ()
            member _.GetChar(_: int) : char = ns ()
            member _.GetChars(_: int, _: int64, _: char[] | null, _: int, _: int) : int64 = ns ()
            member _.GetData(_: int) : IDataReader = ns ()
            member _.GetDateTime(_: int) : DateTime = ns ()
            member _.GetDecimal(_: int) : decimal = ns ()
            member _.GetDouble(_: int) : float = ns ()
            member _.GetFloat(_: int) : float32 = ns ()
            member _.GetGuid(_: int) : Guid = ns ()
            member _.GetInt16(_: int) : int16 = ns ()
            member _.GetInt32(_: int) : int = ns ()
            member _.GetInt64(_: int) : int64 = ns ()
            member _.GetString(_: int) : string = ns ()
        interface IDisposable with
            member _.Dispose() : unit = ()

    let private copyCore
        (cnn: SqlConnection)
        (destination: string)
        (opts: SqlBulkCopyOptions)
        (rows: CellValue list list)
        : Task<unit> =
        task {
            match rows with
            | [] -> return ()
            | shape :: _ ->
                use _ = Bench.scope "deploy.bulk.copyRows"
                let rowCount = List.length rows
                use bulk = new SqlBulkCopy(cnn, opts, null)
                bulk.DestinationTableName <- destination
                for c in shape do
                    bulk.ColumnMappings.Add(c.Column, c.Column) |> ignore
                bulk.BulkCopyTimeout <- 0
                bulk.BatchSize <- rowCount
                Bench.recordSample "deploy.bulk.copyRows.batchSize" (int64 rowCount)
                use reader = new CellValueDataReader(shape, rows)
                do! bulk.WriteToServerAsync(reader :> IDataReader)
        }

    /// Bulk-copy a homogeneous batch of `InsertRow` values into the
    /// target table. `KeepIdentity` is honored so source PKs survive
    /// across the round-trip; `KeepNulls` so `NULL` raws map to
    /// SQL `NULL` rather than column defaults.
    let copyRows
        (cnn: SqlConnection)
        (table: TableId)
        (rows: CellValue list list)
        : Task<unit> =
        copyCore cnn (Render.tableQualified table)
            (SqlBulkCopyOptions.KeepIdentity ||| SqlBulkCopyOptions.KeepNulls) rows

    /// Bulk-copy WITHOUT `KeepIdentity` — the Sink mints every identity
    /// value (the caller excludes the identity column from the cells).
    /// The `AssignedBySink` fast lane for a kind no FK targets: no remap
    /// consumer exists, so no capture is needed, and `KeepIdentity`'s
    /// implicit ALTER requirement is avoided — the lane fits the DML-only
    /// `grant: data` envelope.
    let copyRowsSinkMinted
        (cnn: SqlConnection)
        (table: TableId)
        (rows: CellValue list list)
        : Task<unit> =
        copyCore cnn (Render.tableQualified table) SqlBulkCopyOptions.KeepNulls rows

    /// Bulk-copy into a session-scoped (`#`) staging table — the
    /// surrogate-capture lane's transport (the staging table is created
    /// per chunk by `SELECT TOP 0 … INTO`, so the destination is a raw
    /// session-table name, not a catalog `TableId`). tempdb rights are
    /// implicit for every principal, so the lane fits the DML-only grant.
    let copyRowsSession
        (cnn: SqlConnection)
        (sessionTable: string)
        (rows: CellValue list list)
        : Task<unit> =
        copyCore cnn sessionTable SqlBulkCopyOptions.KeepNulls rows
