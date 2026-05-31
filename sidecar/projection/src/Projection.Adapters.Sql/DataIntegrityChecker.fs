namespace Projection.Adapters.Sql

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// One kind's exact row-count divergence between two deployments of the same
/// schema contract.
type RowCountDelta =
    {
        Kind   : SsKey
        Before : int64
        After  : int64
    }

/// One attribute's exact null-count divergence between two deployments.
type NullCountDelta =
    {
        Kind      : SsKey
        Attribute : SsKey
        Before    : int64
        After     : int64
    }

/// Post-deploy data-integrity diff between two deployments of the same schema
/// contract. Row-count + per-attribute null-count divergences are the
/// data-fidelity complement to the canary's structural (PhysicalSchema)
/// equivalence (slice 4.4). Only DIVERGENT entries are carried (`Before <>
/// After`); an empty report is a clean gate (`isClean`). `Warnings` carries
/// kinds present in one deployment's evidence but not the other's (schema
/// drift between the two, or a one-sided discovery failure).
type IntegrityReport =
    {
        RowCountDeltas  : RowCountDelta list
        NullCountDeltas : NullCountDelta list
        Warnings        : DiagnosticEntry list
    }

/// Post-deploy integrity gate. Captures each deployment's exact aggregate
/// evidence once (reusing `LiveProfiler`'s `RowCount` + per-attribute
/// `NullCounts` probes) and diffs the two caches in pure F#. The pure `diff`
/// is the testable core; `compare` adds the two-connection I/O orchestration.
[<RequireQualifiedAccess>]
module DataIntegrityChecker =

    /// `true` iff there is no divergence (no row/null deltas, no warnings).
    let isClean (report: IntegrityReport) : bool =
        List.isEmpty report.RowCountDeltas
        && List.isEmpty report.NullCountDeltas
        && List.isEmpty report.Warnings

    let private kindWarning (code: string) (message: string) (kindKey: SsKey) : DiagnosticEntry =
        { DiagnosticEntry.create "checker:dataIntegrity" DiagnosticSeverity.Warning code message
          with
            SsKey = Some kindKey }

    /// Pure diff of two evidence caches. Both are captured from the same
    /// Catalog contract, so their kind keysets should match; a kind present
    /// in one but not the other surfaces as a Warning. Row-count and
    /// per-attribute null-count divergences are carried only when they
    /// differ. Deltas + warnings are sorted by `SsKey` (T1 determinism).
    let diff (before: EvidenceCache) (after: EvidenceCache) : IntegrityReport =
        let kindKeys =
            Set.union (before.Kinds |> Map.toList |> List.map fst |> Set.ofList)
                      (after.Kinds  |> Map.toList |> List.map fst |> Set.ofList)
            |> Set.toList
            |> List.sort

        let rowDeltas, nullDeltas, warnings =
            kindKeys
            |> List.fold (fun (rows, nulls, warns) kindKey ->
                match Map.tryFind kindKey before.Kinds, Map.tryFind kindKey after.Kinds with
                | Some b, Some a ->
                    let rows =
                        if b.RowCount <> a.RowCount then
                            { Kind = kindKey; Before = b.RowCount; After = a.RowCount } :: rows
                        else rows
                    // Per-attribute null-count divergences — union of both sides'
                    // attribute keys so a column that lost / gained its NullCount
                    // entry still surfaces (defaulting the absent side to 0).
                    let attrKeys =
                        Set.union (b.NullCounts |> Map.toList |> List.map fst |> Set.ofList)
                                  (a.NullCounts |> Map.toList |> List.map fst |> Set.ofList)
                        |> Set.toList
                        |> List.sort
                    let nulls =
                        attrKeys
                        |> List.fold (fun acc attrKey ->
                            let bn = Map.tryFind attrKey b.NullCounts |> Option.defaultValue 0L
                            let an = Map.tryFind attrKey a.NullCounts |> Option.defaultValue 0L
                            if bn <> an then
                                { Kind = kindKey; Attribute = attrKey; Before = bn; After = an } :: acc
                            else acc) nulls
                    rows, nulls, warns
                | Some _, None ->
                    rows, nulls,
                    (kindWarning
                        "verifyData.kind.missingInAfter"
                        "Kind present in the before deployment's evidence but absent from the after deployment (schema drift or one-sided discovery failure)."
                        kindKey) :: warns
                | None, Some _ ->
                    rows, nulls,
                    (kindWarning
                        "verifyData.kind.missingInBefore"
                        "Kind present in the after deployment's evidence but absent from the before deployment (schema drift or one-sided discovery failure)."
                        kindKey) :: warns
                | None, None ->
                    // Unreachable — the key came from the union.
                    rows, nulls, warns) ([], [], [])

        {
            // Folds prepend; reverse-by-sort restores SsKey order.
            RowCountDeltas  = rowDeltas |> List.sortBy (fun d -> d.Kind)
            NullCountDeltas = nullDeltas |> List.sortBy (fun d -> d.Kind, d.Attribute)
            Warnings        = warnings |> List.sortBy (fun w -> w.SsKey)
        }

    /// verify-data verifies EVERY table's data (row + null counts),
    /// including lookup / static-population kinds. `LiveProfiler` skips
    /// `Modality.Static` kinds (their data is schema, emitted as seed
    /// INSERTs), and `ReadSide.read` marks every reconstructed table that
    /// carries rows as `Static` (an artifact of the canary's per-row
    /// PhysicalSchema reconstruction). Clearing the `Static` marking makes
    /// the contract fully profileable for the integrity gate without
    /// touching the shared profiler's behavior. SsKeys are preserved, so
    /// the cache keys still match the caller's catalog.
    let private profileable (catalog: Catalog) : Catalog =
        let clearStatic (k: Kind) : Kind =
            { k with
                Modality =
                    k.Modality
                    |> List.filter (function ModalityMark.Static _ -> false | _ -> true) }
        { catalog with
            Modules =
                catalog.Modules
                |> List.map (fun m -> { m with Kinds = m.Kinds |> List.map clearStatic }) }

    /// Capture both deployments' exact aggregate evidence and diff. The
    /// Catalog is the shared schema contract both deployments realize; the
    /// caller derives it (e.g. `ReadSide.read` against the before
    /// deployment). Reuses `LiveProfiler.captureEvidenceCache` (D9: the
    /// connections are supplied open, never a connection string in Core).
    let compare
        (before: SqlConnection)
        (after: SqlConnection)
        (catalog: Catalog)
        : Task<Result<IntegrityReport>> =
        let contract = profileable catalog
        task {
            let! beforeR = LiveProfiler.captureEvidenceCache before contract
            match beforeR with
            | Error es -> return Result.failure es
            | Ok beforeCache ->
                let! afterR = LiveProfiler.captureEvidenceCache after contract
                match afterR with
                | Error es -> return Result.failure es
                | Ok afterCache -> return Result.success (diff beforeCache afterCache)
        }
