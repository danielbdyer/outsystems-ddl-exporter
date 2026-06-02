namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql

/// 6.B.1 — the Decision↔Data pre-flight (orthogonality T-V). A `Decision`
/// (tightening) that makes a column NOT NULL on data that violates it would
/// make the two-phase load fail mid-write — `Transfer` carries no schema-vs-
/// data compatibility check, so today the incompatibility surfaces as a crash
/// half-way through the load. This surfaces the coupling as a NAMED, fail-loud
/// gate (`migrate.dataViolatesTightening`) BEFORE any write, given the
/// tightening overlay and a source-data null-count probe (`LiveProfiler`).
[<RequireQualifiedAccess>]
module Preflight =

    /// One column the tightening would break: it is tightened to NOT NULL
    /// (`DecisionOverlay.EnforceNotNull`) but the source data carries
    /// `NullCount` NULL rows.
    type TighteningViolation =
        {
            KindKey      : SsKey
            AttributeKey : SsKey
            NullCount    : int64
        }

    /// Pure: each `EnforceNotNull` column whose source data carries NULLs (the
    /// Sink would reject the NULL into the tightened NOT-NULL column at load
    /// time). Reads the LiveProfiler null-count evidence; no I/O. Deterministic
    /// — sorted by attribute identity (T1).
    let dataViolatesTightening
        (cache: EvidenceCache)
        (overlay: DecisionOverlay)
        : TighteningViolation list =
        cache.Kinds
        |> Map.toList
        |> List.collect (fun (kindKey, ck) ->
            ck.NullCounts
            |> Map.toList
            |> List.choose (fun (attrKey, nullCount) ->
                if nullCount > 0L && Set.contains attrKey overlay.EnforceNotNull then
                    Some { KindKey = kindKey; AttributeKey = attrKey; NullCount = nullCount }
                else None))
        |> List.sortBy (fun v -> SsKey.rootOriginal v.AttributeKey)

    /// Render the violations as the operator-facing refusal message.
    let private describe (violations: TighteningViolation list) : string =
        match violations with
        | [] -> "no tightening violations"
        | first :: _ ->
            sprintf
                "%d column(s) carry NULLs but a Decision tightens them to NOT NULL; the load would fail mid-write. First: attribute %s in kind %s has %d NULL row(s). Remediate the data or relax the tightening before executing."
                (List.length violations)
                (SsKey.rootOriginal first.AttributeKey)
                (SsKey.rootOriginal first.KindKey)
                first.NullCount

    /// Run the pre-flight against a live source: capture the per-attribute
    /// null-count evidence (read-only — safe before any write) and refuse with
    /// `migrate.dataViolatesTightening` if the overlay tightens any NULL-bearing
    /// column. A clean source returns `Ok ()`; the named refusal replaces the
    /// silent mid-load crash.
    let tighteningPreflight
        (cnn: SqlConnection)
        (catalog: Catalog)
        (overlay: DecisionOverlay)
        : Task<Result<unit>> =
        task {
            // `LiveProfiler` skips `Modality.Static` kinds — but `ReadSide`
            // marks every row-carrying reconstructed table Static, which would
            // skip exactly the kinds we need to probe. The pre-flight cares
            // about the LIVE source data, not the modeling classification, so
            // clear Modality before capture (it does not affect the SQL probe).
            let profileCatalog = catalog |> Catalog.mapKinds (fun k -> { k with Modality = [] })
            match! LiveProfiler.captureEvidenceCache cnn profileCatalog with
            | Error es -> return Result.failure es
            | Ok cache ->
                match dataViolatesTightening cache overlay with
                | [] -> return Ok ()
                | violations ->
                    return
                        Result.failureOf
                            (ValidationError.create "migrate.dataViolatesTightening" (describe violations))
        }
