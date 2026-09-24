namespace Projection.Pipeline

open Projection.Core

/// The **row-plane override seam** — the publish-time `Map<SsKey, StaticRow list>`
/// analog of `EmissionSeam` (the post-chain `Catalog → Catalog` seam). Every
/// operator-declared override that rewrites row DATA in flight between
/// acquisition and the data composers lives here as ONE bound, registered entry,
/// so `registered ⇔ executed` holds for the seam by construction — the same
/// discipline E1/E2 (`Compose.emitSteps` / `Compose.readStep`) and E5
/// (`EmissionSeam`) established for the emit / read / post-chain stages.
///
/// **Why this seam exists (the blind spot it closes).** Approved data
/// corrections were registered as metadata (`ApprovedDataCorrections.registered‐
/// Metadata`, spliced into `RegisteredAllTransforms.all`) but EXECUTED by a bare
/// call in the Pipeline extract stage — the exact F2/F3 shape `EmissionSeam` was
/// created to retire: a transform whose registration and execution were not
/// bound to one source, so the totality proof covered the registration without
/// witnessing the execution. Routing the correction application through this one
/// seam fixes it structurally: `apply` folds exactly the registered `overrides`,
/// and `metadata` / `executedNames` project from the SAME list — so a row-plane
/// override added here is BOTH executed (by `apply`) and registered (in
/// `metadata`, wired into `RegisteredAllTransforms.all`) by construction, never
/// one without the other.
///
/// **Scope.** This seam holds row-DATA rewrites (value corrections, row
/// exclusions). Bridge-ROW supply rides the existing `migrationDependencies`
/// lane, and the schema-plane FK retarget rides the `DecisionOverlay` seam — each
/// plane keeps its own homogeneous fold (no boxing across heterogeneous carriers,
/// per the `Binding.fs` rationale). The seam is `Config`-driven (the corrections
/// are operator config, not catalog-derived), which is why it is a
/// between-stages seam rather than a pass-chain step.
[<RequireQualifiedAccess>]
module DataCorrectionSeam =

    /// One publish-time row-plane override: its registry metadata paired with the
    /// pure `Config`-driven transform it executes. The transform sees the config,
    /// the (possibly already-overridden) catalog, and the row map, and returns the
    /// updated catalog + rows + the receipts it produced. The pairing is the
    /// load-bearing invariant — metadata and transform travel together, so neither
    /// can drift from the other.
    type private RowOverride =
        { Metadata  : RegisteredTransformMetadata
          Transform : Config.Config
                          -> Catalog
                          -> Map<SsKey, StaticRow list>
                          -> Result<Catalog * Map<SsKey, StaticRow list> * DataCorrectionReceipt list> }

    /// Approved inline data corrections — the operator-approved value/membership
    /// rewrites (`emission.dataCorrections`). Applies on the TWO-PHASE schedule
    /// (the whole row map in hand): the pure engine (`ApprovedDataCorrections.apply`)
    /// produces the corrected rows + receipts; the static-lane populations are
    /// re-grafted onto the catalog (StaticSeedsEmitter reads them from there), and
    /// the bootstrap map keeps only the kinds it originally carried so a corrected
    /// static kind is not emitted by BOTH lanes (T11 keyset agreement preserved).
    /// Empty corrections ⇒ identity (byte-identical); a named refusal surfaces as
    /// the seam's failure. Body lifted verbatim from the former hand-wired
    /// `applyDataCorrectionsTwoPhase`.
    let private approvedDataCorrections : RowOverride =
        { Metadata = ApprovedDataCorrections.registeredMetadata
          Transform =
            fun cfg catalog rows ->
                if List.isEmpty cfg.Emission.DataCorrections then Result.success (catalog, rows, [])
                else
                    ApprovedDataCorrections.apply catalog cfg.Emission.DataCorrections rows
                    |> Result.map (fun outcome ->
                        let correctedCatalog = Hydration.graftStaticPopulations outcome.CorrectedRows catalog
                        let bootstrapMap = outcome.CorrectedRows |> Map.filter (fun k _ -> Map.containsKey k rows)
                        (correctedCatalog, bootstrapMap, outcome.Receipts)) }

    /// The registered row-plane overrides, in application order. The SINGLE source
    /// `apply` / `metadata` / `executedNames` all project from. A future row-plane
    /// override is added here (covered by the bidirectional totality test), not as
    /// a bare call somewhere in the Pipeline.
    let private overrides : RowOverride list = [ approvedDataCorrections ]

    /// Apply every registered row-plane override in order, threading the catalog +
    /// rows and accumulating the receipts each produces. Empty corrections ⇒
    /// identity (`apply cfg c rows = Ok (c, rows, [])`). FAIL-CLOSED: the first
    /// override's named refusal short-circuits the fold.
    let apply
        (cfg: Config.Config)
        (catalog: Catalog)
        (rows: Map<SsKey, StaticRow list>)
        : Result<Catalog * Map<SsKey, StaticRow list> * DataCorrectionReceipt list> =
        let folder
            (state: Result<Catalog * Map<SsKey, StaticRow list> * DataCorrectionReceipt list>)
            (o: RowOverride)
            : Result<Catalog * Map<SsKey, StaticRow list> * DataCorrectionReceipt list> =
            match state with
            | Error e -> Error e
            | Ok (cat, rws, acc) ->
                o.Transform cfg cat rws
                |> Result.map (fun (cat', rws', receipts) -> (cat', rws', acc @ receipts))
        overrides |> List.fold folder (Result.success (catalog, rows, []))

    /// The seam's registry metadata — projected from the SAME `overrides` that
    /// `apply` executes, so `registered ⇔ executed` holds for the seam by
    /// construction (the E1 discipline). Spliced into `RegisteredAllTransforms.all`.
    let metadata : RegisteredTransformMetadata list =
        overrides |> List.map (fun o -> o.Metadata)

    /// The executed override names — the bidirectional test pairs these against
    /// `metadata` (the seam's own closure) and against `RegisteredAllTransforms.all`
    /// (the wiring), the two halves of the seam's totality guarantee.
    let executedNames : string list =
        overrides |> List.map (fun o -> o.Metadata.Name)
