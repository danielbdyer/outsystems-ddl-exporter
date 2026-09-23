namespace Projection.Adapters.Sql

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

    /// The Catalog-shaped view of a kind for the table-shaped probe (the
    /// data-sink chapter S8 extraction — `TableFingerprint` owns the SQL and
    /// the round-trip; this caller owns the Kind → target projection, so the
    /// emitted SQL is byte-identical to the pre-extraction form).
    let private targetOf (kind: Kind) : TableFingerprint.TableTarget =
        { Schema = TableId.schemaText kind.Physical
          Table = TableId.tableText kind.Physical
          MaxPkColumn =
            match Kind.primaryKey kind with
            | [ pk ] -> Some (ColumnRealization.columnNameText pk.Column)
            | _ -> None
          ChecksumColumns =
            // XML columns are excluded (`BINARY_CHECKSUM` rejects an
            // explicitly-listed xml column, and a rejected column would fail
            // the whole batch); a kind left with no checksummable column
            // renders NULL and degrades to the row/PK signal.
            Some
                (kind.Attributes
                 |> List.choose (fun a ->
                     match a.SqlStorage with
                     | Some SqlStorageType.Xml -> None
                     | _ -> Some (ColumnRealization.columnNameText a.Column))) }

    /// Probe every kind's row count + `MAX(pk)` in ONE round-trip (Bench
    /// `estate.fingerprint.probe`). A probe failure is the caller's NAMED
    /// degradation — the estate falls back to live profiling, so the
    /// evidence stays fresh and only the pay-once saving is lost.
    let probe (cnn: SqlConnection) (kinds: Kind list) : Task<Result<FingerprintReading list>> =
        task {
            match! TableFingerprint.probeWith "estate.fingerprint.probe" "estate.evidence.probeFailed" cnn (kinds |> List.map targetOf) with
            | Error es -> return Result.failure es
            | Ok readings ->
                return
                    List.zip kinds readings
                    |> List.map (fun (kind, r) ->
                        { Kind = kind.SsKey; RowCount = r.RowCount; MaxPk = r.MaxPk; Content = r.Content })
                    |> Result.success
        }
