namespace Projection.Pipeline

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
    /// `ReadSide.formatRawValue`: parse the IR raw form back into
    /// the CLR object SqlBulkCopy expects. `""` → `DBNull`.
    let parseRaw (t: PrimitiveType) (raw: string) : obj | null =
        if raw = "" then box DBNull.Value
        else
            let inv = CultureInfo.InvariantCulture
            match t with
            | Integer ->
                box (Int64.Parse(raw, inv))
            | Decimal ->
                box (Decimal.Parse(raw, inv))
            | Boolean ->
                match raw.ToLowerInvariant() with
                | "true" | "1" -> box true
                | _ -> box false
            | DateTime ->
                box (DateTime.ParseExact(raw, "yyyy-MM-dd HH:mm:ss.fffffff", inv))
            | Date ->
                box (DateTime.ParseExact(raw, "yyyy-MM-dd", inv))
            | Time ->
                box (TimeSpan.Parse(raw, inv))
            | Guid ->
                box (Guid.Parse raw)
            | Text ->
                box raw
            | Binary ->
                let hex = if raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) then raw.Substring 2 else raw
                box (Convert.FromHexString hex)

    /// Build a `DataTable` matching the column shape of the first
    /// row in a batch. Subsequent rows must share the shape; the
    /// caller groups by shape before invoking.
    let private buildTable (shape: CellValue list) : DataTable =
        let dt = new DataTable()
        for c in shape do
            let col = new DataColumn(c.Column, clrType c.Type)
            col.AllowDBNull <- true
            dt.Columns.Add col
        dt

    let private fillTable (dt: DataTable) (rows: CellValue list list) : unit =
        for row in rows do
            let arr =
                row
                |> List.map (fun cv -> parseRaw cv.Type cv.Raw)
                |> List.toArray
            dt.Rows.Add arr |> ignore

    /// Bulk-copy a homogeneous batch of `InsertRow` values into the
    /// target table. `KeepIdentity` is honored so source PKs survive
    /// across the round-trip; `KeepNulls` so `NULL` raws map to
    /// SQL `NULL` rather than column defaults.
    let copyRows
        (cnn: SqlConnection)
        (table: TableId)
        (rows: CellValue list list)
        : Task<unit> =
        task {
            if List.isEmpty rows then return ()
            else
                use _ = Bench.scope "deploy.bulk.copyRows"
                let shape = List.head rows
                use dt = buildTable shape
                fillTable dt rows
                let opts =
                    SqlBulkCopyOptions.KeepIdentity
                    ||| SqlBulkCopyOptions.KeepNulls
                use bulk = new SqlBulkCopy(cnn, opts, null)
                bulk.DestinationTableName <- sprintf "[%s].[%s]" table.Schema table.Table
                for c in shape do
                    bulk.ColumnMappings.Add(c.Column, c.Column) |> ignore
                bulk.BulkCopyTimeout <- 0
                bulk.BatchSize <- rows.Length
                Bench.recordSample "deploy.bulk.copyRows.batchSize" (int64 rows.Length)
                do! bulk.WriteToServerAsync dt
        }
