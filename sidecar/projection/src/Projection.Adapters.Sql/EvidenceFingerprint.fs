namespace Projection.Adapters.Sql

// LINT-ALLOW-FILE: estate fingerprint probe at the SQL boundary — terminal
//   SQL-text emission (String.Concat/Join/concat over typed encode-quoted
//   segments), a function-local mutable drain flag, and a keyed result
//   accumulator; the probe emits terminal text at the DB boundary (the
//   LiveProfiler file-header precedent).

open System
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// One kind's live staleness reading — the cheap movement signal
/// (`COUNT_BIG(*)` + `MAX(pk)`) the estate evidence store gates reuse on
/// (DECISIONS 2026-07-15, the estate chapter opens, entry 4). The schema-
/// shape half of the fingerprint is pure and computed by the caller from
/// the catalog; this probe reads only what SQL alone can answer.
type FingerprintReading =
    {
        Kind     : SsKey
        /// `COUNT_BIG(*)` at probe time — exact.
        RowCount : int64
        /// `MAX(pk)` rendered server-side as `NVARCHAR(128)`; `None` for a
        /// kind without a single-column primary key. A rendering that could
        /// drift across sessions (a date PK under a changed language
        /// setting) costs a spurious re-profile, never a stale reuse —
        /// movement detection degrades only in the safe direction.
        MaxPk    : string option
        /// `CHECKSUM_AGG(BINARY_CHECKSUM(<columns>))` over the kind's
        /// checksummable columns — the content-movement signal that closes the
        /// `(RowCount, MaxPk)` UPDATE-blindness (survival rule 14): an in-place
        /// UPDATE that changes a value but neither the row count nor the max PK
        /// still moves this term, so cached evidence is not reused over stale
        /// reality. `None` for a kind with no checksummable column (only an XML
        /// column, which `BINARY_CHECKSUM` rejects) — the term is dropped
        /// per-kind rather than failing the whole batch, so movement detection
        /// degrades only in the safe direction (that kind falls back to the
        /// row/PK signal). A checksum collision costs a stale reuse in the
        /// vanishingly-rare case; `--refresh` remains the override.
        Content  : string option
    }

[<RequireQualifiedAccess>]
module EvidenceFingerprint =

    // recon #8 — the one Core quoter (`SqlIdentifier.quote`, byte-verified
    // against ScriptDom's `Identifier.EncodeIdentifier`).
    let private encode = SqlIdentifier.quote

    /// The one-batch probe SQL: per kind, one `SELECT` of (ordinal, exact
    /// row count, canonical `MAX(pk)`), `UNION ALL`-joined — the whole
    /// estate's staleness question in a single round-trip. The reader
    /// correlates by ordinal, so `UNION ALL`'s engine-defined row order
    /// carries no meaning.
    let private probeSql (kinds: Kind list) : string =
        let selectFor (idx: int) (kind: Kind) : string =
            let table =
                String.Join(
                    ".",
                    [| encode (TableId.schemaText kind.Physical); encode (TableId.tableText kind.Physical) |])
            let maxExpr =
                match Kind.primaryKey kind with
                | [ pk ] ->
                    String.Concat("CAST(MAX(", encode (ColumnRealization.columnNameText pk.Column), ") AS NVARCHAR(128))")
                | _ -> "CAST(NULL AS NVARCHAR(128))"
            // The content-movement term: an order-independent aggregate of the
            // per-row binary checksums over every checksummable column. XML
            // columns are excluded (`BINARY_CHECKSUM` rejects them, and a
            // rejected column would fail the whole batch); a kind left with no
            // checksummable column emits NULL and degrades to the row/PK signal.
            let checksumCols =
                kind.Attributes
                |> List.choose (fun a ->
                    match a.SqlStorage with
                    | Some SqlStorageType.Xml -> None
                    | _ -> Some (encode (ColumnRealization.columnNameText a.Column)))
            let contentExpr =
                match checksumCols with
                | [] -> "CAST(NULL AS NVARCHAR(32))"
                | cols ->
                    String.Concat(
                        "CAST(CHECKSUM_AGG(BINARY_CHECKSUM(", String.Join(", ", cols), ")) AS NVARCHAR(32))")
            String.Concat("SELECT ", string idx, " AS [i], COUNT_BIG(*) AS [c], ", maxExpr, " AS [m], ", contentExpr, " AS [h] FROM ", table)
        kinds |> List.mapi selectFor |> String.concat " UNION ALL "

    /// Probe every kind's row count + `MAX(pk)` in ONE round-trip (Bench
    /// `estate.fingerprint.probe`). A probe failure is the caller's NAMED
    /// degradation — the estate falls back to live profiling, so the
    /// evidence stays fresh and only the pay-once saving is lost.
    let probe (cnn: SqlConnection) (kinds: Kind list) : Task<Result<FingerprintReading list>> =
        match kinds with
        | [] -> Task.FromResult (Result.success [])
        | _ ->
            task {
                try
                    use _ = Bench.scope "estate.fingerprint.probe"
                    use cmd = cnn.CreateCommand()
                    cmd.CommandText <- probeSql kinds
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
                        kinds
                        |> List.mapi (fun idx kind ->
                            match readings.TryGetValue idx with
                            | true, (count, maxPk, content) -> { Kind = kind.SsKey; RowCount = count; MaxPk = maxPk; Content = content }
                            // Unreachable by construction (an aggregate SELECT
                            // always yields its row); kept total in the safe
                            // direction — an absent row reads as movement.
                            | false, _ -> { Kind = kind.SsKey; RowCount = 0L; MaxPk = None; Content = None })
                        |> Result.success
                with ex ->
                    return
                        Result.failureOf
                            (ValidationError.create "estate.evidence.probeFailed"
                                (String.Concat("the fingerprint probe did not complete: ", ex.Message)))
            }
