namespace Projection.Adapters.Sql

// LINT-ALLOW-FILE: staleness fingerprint probe at the SQL boundary — terminal
//   SQL-text emission (String.Concat/Join/concat over typed encode-quoted
//   segments), a function-local mutable drain flag, and a keyed result
//   accumulator; the probe emits terminal text at the DB boundary (the
//   EvidenceFingerprint file it was extracted from carries the same posture).

open System
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// The TABLE-shaped staleness probe (the data-sink chapter, S8) — the
/// primitive `EvidenceFingerprint` (Catalog-shaped, the estate evidence
/// store's gate) and the sink's freshness gate (three literal ossys tables)
/// both stand on: per target, one `SELECT` of exact row count + canonical
/// `MAX(pk)` + an order-independent content checksum, `UNION ALL`-joined into
/// ONE round-trip. Extracted at the second consumer (the house threshold);
/// the estate caller's emitted SQL is byte-identical to what it emitted
/// before the extraction.
[<RequireQualifiedAccess>]
module TableFingerprint =

    /// One probed table, named RAW (unencoded) — the probe owns the one
    /// quoter (`SqlIdentifier.quote`, recon #8).
    type TableTarget =
        {
            Schema : string
            Table  : string
            /// The single-column primary key's RAW column name — `None`
            /// renders the `MAX(pk)` term as typed NULL (a composite or
            /// absent PK carries no single max).
            MaxPkColumn : string option
            /// The checksummable columns, RAW. `Some cols` renders
            /// `BINARY_CHECKSUM(col, …)` (`Some []` renders typed NULL —
            /// nothing checksummable); `None` renders the star form
            /// `BINARY_CHECKSUM(*)`, which itself skips noncomparable
            /// columns (xml/text/image) server-side — the form for a table
            /// probed WITHOUT a column inventory (the sink's ossys targets).
            ChecksumColumns : string list option
        }

    /// One target's reading. Absent server rows totalize to a zero reading —
    /// the safe direction (an absent row reads as movement, never as match).
    type TableReading =
        {
            Target   : TableTarget
            RowCount : int64
            MaxPk    : string option
            Content  : string option
        }

    // recon #8 — the one Core quoter (`SqlIdentifier.quote`, byte-verified
    // against ScriptDom's `Identifier.EncodeIdentifier`).
    let private encode = SqlIdentifier.quote

    /// The one-batch probe SQL. The reader correlates by ordinal, so
    /// `UNION ALL`'s engine-defined row order carries no meaning.
    let probeSql (targets: TableTarget list) : string =
        let selectFor (idx: int) (t: TableTarget) : string =
            let table = String.Join(".", [| encode t.Schema; encode t.Table |])
            let maxExpr =
                match t.MaxPkColumn with
                | Some pk -> String.Concat("CAST(MAX(", encode pk, ") AS NVARCHAR(128))")
                | None -> "CAST(NULL AS NVARCHAR(128))"
            let contentExpr =
                match t.ChecksumColumns with
                | Some [] -> "CAST(NULL AS NVARCHAR(32))"
                | Some cols ->
                    String.Concat(
                        "CAST(CHECKSUM_AGG(BINARY_CHECKSUM(", String.Join(", ", cols |> List.map encode), ")) AS NVARCHAR(32))")
                | None -> "CAST(CHECKSUM_AGG(BINARY_CHECKSUM(*)) AS NVARCHAR(32))"
            String.Concat("SELECT ", string idx, " AS [i], COUNT_BIG(*) AS [c], ", maxExpr, " AS [m], ", contentExpr, " AS [h] FROM ", table)
        targets |> List.mapi selectFor |> String.concat " UNION ALL "

    /// Probe every target in ONE round-trip. `benchLabel` and `failureCode`
    /// are the caller's — the estate keeps `estate.fingerprint.probe` /
    /// `estate.evidence.probeFailed`; the sink names its own. A probe failure
    /// is the caller's NAMED degradation (the estate re-profiles; the sink
    /// reads live) — freshness detection only ever degrades toward the wire.
    let probeWith
        (benchLabel: string)
        (failureCode: string)
        (cnn: SqlConnection)
        (targets: TableTarget list)
        : Task<Result<TableReading list>> =
        match targets with
        | [] -> Task.FromResult (Result.success [])
        | _ ->
            task {
                try
                    use _ = Bench.scope benchLabel
                    use cmd = cnn.CreateCommand()
                    cmd.CommandText <- probeSql targets
                    cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                    use! reader = cmd.ExecuteReaderAsync()
                    let readings = System.Collections.Generic.Dictionary<int, int64 * string option * string option>()
                    let mutable advanced = true
                    while advanced do
                        let! more = reader.ReadAsync()
                        advanced <- more
                        if more then
                            let idx = reader.GetInt32 0
                            let count = if reader.IsDBNull 1 then 0L else reader.GetInt64 1
                            let maxPk = if reader.IsDBNull 2 then None else Some (reader.GetString 2)
                            let content = if reader.IsDBNull 3 then None else Some (reader.GetString 3)
                            readings.[idx] <- (count, maxPk, content)
                    return
                        targets
                        |> List.mapi (fun idx t ->
                            match readings.TryGetValue idx with
                            | true, (count, maxPk, content) -> { Target = t; RowCount = count; MaxPk = maxPk; Content = content }
                            // Unreachable by construction (an aggregate SELECT
                            // always yields its row); kept total in the safe
                            // direction — an absent row reads as movement.
                            | false, _ -> { Target = t; RowCount = 0L; MaxPk = None; Content = None })
                        |> Result.success
                with ex ->
                    return
                        Result.failureOf
                            (ValidationError.create failureCode
                                (String.Concat("the fingerprint probe did not complete: ", ex.Message)))
            }
