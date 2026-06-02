namespace Projection.Core

/// The **change-manifest of δ** (`EXECUTION_PLAN.md` 6.H.4) — the emission-
/// integral of one displacement, the mixed partial `∂²κ/∂episode∂emission`.
/// Where the existing `SsdtManifest` records *state* (coverage / applied
/// transforms at a target), a `ChangeManifest` records *displacement*: what one
/// episode-edge **touched**. It answers the SSIS consumer's question — "what did
/// this sprint change?" — with the move counts (`ChannelCounts`, the schema-side
/// channel projection π), the norm `‖δ‖`, the Decision-plane anchor (the
/// refactorlog reference), and the realized data movement (the CDC capture
/// series `k`). It is computed *from* the diff and the two episodes' planes; it
/// is not a new emission path.
type ChangeManifest =
    {
        /// The edge's endpoints — `(Environment × Version × At)` at each end.
        From : EpisodeCoordinate
        To   : EpisodeCoordinate
        /// The per-channel schema move counts (π): the orthogonal channels of
        /// the displacement (renamed/added/removed kinds; added/removed/renamed/
        /// changed attributes).
        Channels : CatalogDiff.ChannelCounts
        /// The schema norm `‖δ‖` = the sum of the channel counts (T15). Zero iff
        /// the schema is unchanged across the edge (an idempotent redeploy).
        SchemaNorm : int
        /// The Decision-plane anchor: the `To` episode's emitted refactorlog
        /// reference (the rename evidence that preserved data across the edge).
        RefactorLogRef : string option
        /// The realized data movement across the edge — the CDC capture count of
        /// the `To` episode (`‖data δ‖`, the substrate-fused data norm, §12.4).
        CdcCaptureCount : int
    }

[<RequireQualifiedAccess>]
module ChangeManifest =

    /// The displacement manifest for one episode-edge: diff the two episodes'
    /// schemas (the same `CatalogDiff.between` the refactorlog/ALTER emitters
    /// consume), read off the channel counts + norm, and carry the `To`
    /// episode's Decision anchor + realized data movement. Threads the Π-side
    /// `EmitError` (`between`'s error type).
    let between (fromEpisode: Episode) (toEpisode: Episode) : Result<ChangeManifest, EmitError> =
        match CatalogDiff.between fromEpisode.Schema toEpisode.Schema with
        | Error e -> Error e
        | Ok diff ->
            Ok
                { From = fromEpisode.Coordinate
                  To = toEpisode.Coordinate
                  Channels = CatalogDiff.channelCounts diff
                  SchemaNorm = CatalogDiff.norm diff
                  RefactorLogRef = toEpisode.RefactorLogRef
                  CdcCaptureCount = toEpisode.Data.CdcCaptureCount }

    /// The change-manifest **series**: one manifest per edge across an
    /// `EpisodicLifecycle` — the sprint-by-sprint record of what each release
    /// touched. A genesis-only lifecycle has no edges, so the series is empty.
    let series (lifecycle: EpisodicLifecycle) : Result<ChangeManifest list, EmitError> =
        let rec loop acc remaining =
            match remaining with
            | a :: (b :: _ as rest) ->
                match between a b with
                | Ok m    -> loop (m :: acc) rest
                | Error e -> Error e
            | _ -> Ok (List.rev acc)
        loop [] (EpisodicLifecycle.episodes lifecycle)

    /// The aggregate displacement of a whole timeline — the integral of the
    /// series. `‖δ‖` summed over edges is the *path length* (total moves made,
    /// counting an add-then-remove as 2); compare with `EpisodicLifecycle.
    /// netSchemaDiff |> norm`, the *net* displacement (genesis → latest,
    /// counting an add-then-remove as 0). Their difference is the timeline's
    /// churn — work done that did not move the net position.
    let pathLength (lifecycle: EpisodicLifecycle) : Result<int, EmitError> =
        match series lifecycle with
        | Ok manifests -> Ok (manifests |> List.sumBy (fun m -> m.SchemaNorm))
        | Error e      -> Error e
