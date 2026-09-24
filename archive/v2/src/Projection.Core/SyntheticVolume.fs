namespace Projection.Core

/// Derives a per-kind synthetic `VolumeTarget` from a `CentralityRanking`
/// (H-071) so structurally central kinds — join hubs, tables many others depend
/// on — receive proportionally more synthetic rows. Load-test data then mirrors
/// the schema's real shape (a few heavy hubs, a long tail of light satellites)
/// instead of a flat per-table count.
///
/// **Where it plugs in.** The output is a `Map<SsKey, VolumeTarget>` that feeds
/// the EXISTING `SyntheticConfig.VolumeByKind` seam at the composition layer
/// (`SyntheticLoadRun`). The pure Core generator `SyntheticData.generate` is
/// untouched and its signature unchanged — this is the analytics `CentralityRanking`
/// (previously computed but unconsumed) finally reaching a consumer, per
/// "carriers reify eagerly, verbs at the second consumer."
///
/// **Determinism (T1).** Pure `decimal` arithmetic over the ranking's already
/// `SsKey`-stable order; no clock, no float. Same ranking → same map.
///
/// **Identity-preserving by construction.** The baseline `scale` is folded into
/// every factor, so a flat schema (all scores equal), a zero `strength`, or an
/// empty ranking all yield `Multiplier scale` for every kind — arithmetically
/// identical to the global `Scale` path in `SyntheticData.rowCountFor`
/// (`observed × scale`). Weighting only ever *amplifies* central kinds; a
/// peripheral kind is never shrunk below its `scale` baseline.
[<RequireQualifiedAccess>]
module SyntheticVolume =

    /// Per-kind volume multipliers derived from centrality. For each scored kind:
    ///   `factor = min maxFactor (1 + strength × max(0, score/mean − 1))`
    ///   `VolumeTarget = Multiplier (scale × factor)`
    /// where `mean` is the ranking's average score. A kind at or below the mean
    /// gets `factor = 1` (the `scale` baseline, never less); a kind above the mean
    /// is boosted in proportion to how far above, capped at `maxFactor`. `strength`
    /// is the dimensionless boost gain (0 = off; 1 = a kind at 2× the mean score
    /// gets 2× its baseline volume, before the cap). `maxFactor` bounds the
    /// heaviest hub so a single dominant table cannot explode the row budget.
    ///
    /// `strength ≤ 0`, `maxFactor ≤ 1`, an empty ranking, or a degenerate
    /// (non-positive-mean) ranking all collapse to the `scale` baseline — the
    /// caller should skip the merge entirely when weighting is off, but the result
    /// is still identity-in-effect if it doesn't.
    let byCentrality
        (strength: decimal)
        (maxFactor: decimal)
        (scale: decimal)
        (ranking: CentralityRanking)
        : Map<SsKey, VolumeTarget> =
        match ranking.Scores with
        | [] -> Map.empty
        | scores ->
            let n = List.length scores
            let mean = (scores |> List.sumBy (fun s -> s.Score)) / decimal n
            let strengthClamped = max 0M strength
            let capFactor = max 1M maxFactor
            scores
            |> List.map (fun s ->
                let relative = if mean <= 0M then 1M else s.Score / mean
                let boost = strengthClamped * max 0M (relative - 1M)
                let factor = min capFactor (1M + boost)
                s.SsKey, VolumeTarget.Multiplier (scale * factor))
            |> Map.ofList

    /// Merge derived weighting UNDER an operator-supplied `VolumeByKind`: an
    /// explicit operator entry (from `--volume` or a `volume` correction) always
    /// wins; the derived weighting only fills kinds the operator left unspecified.
    /// Operator intent is never overridden by the heuristic — the same "blessed
    /// intent dominates" discipline `Correction.applyToConfig` follows.
    let mergeUnderOperator
        (operator: Map<SsKey, VolumeTarget>)
        (derived: Map<SsKey, VolumeTarget>)
        : Map<SsKey, VolumeTarget> =
        derived
        |> Map.fold (fun acc k v -> if Map.containsKey k acc then acc else Map.add k v acc) operator
