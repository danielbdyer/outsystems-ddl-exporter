namespace Projection.Core

/// The operator's evidence-tiering policy: how many rows of a kind's CELL
/// evidence the profiler may sample. Exactness is tiered, never silently
/// traded — under any cap, a kind's `RowCount` and `NullCounts` stay EXACT
/// (the discovery's aggregate query is uncapped); only the sampled `Values`
/// arrays truncate, downgrading distribution / duplicate / length evidence
/// from exhaustive to observed-sample (`ProbeStatus` carries the observed
/// size, so derivation consumers already express the downgrade).
///
/// Two axes: `DefaultMaxRows` applies to every kind without an override;
/// `Overrides` pins specific kinds (resolved to `SsKey` at bind time — the
/// logical `{module, entity}` pair is the config-facing form, espace-safe).
/// An explicit `None` override EXEMPTS a kind from the default cap (full
/// scan); a `Some n` override tightens or loosens it. A sampled kind is
/// excluded from single-scan derivation (`EvidenceCache.cachedKindOfRows`
/// requires full hydration for its exactness contract) and keeps the live
/// capped discovery — the partition is counted, never silent.
type SamplingPolicy = {
    /// Cap for every kind without an override; `None` = full scan.
    DefaultMaxRows : int option
    /// Per-kind pins. `Some cap` overrides the default; the map's value
    /// `None` names a full-scan exemption.
    Overrides      : Map<SsKey, int option>
}

[<RequireQualifiedAccess>]
module SamplingPolicy =

    /// No sampling anywhere — the exhaustive-evidence default.
    let fullScan : SamplingPolicy = {
        DefaultMaxRows = None
        Overrides      = Map.empty
    }

    /// One cap for every kind (no per-kind pins). `uniform None ≡ fullScan`.
    let uniform (cap: int option) : SamplingPolicy = {
        DefaultMaxRows = cap
        Overrides      = Map.empty
    }

    /// The cap that governs one kind: its override if pinned, else the
    /// default. Total.
    let capFor (kindKey: SsKey) (policy: SamplingPolicy) : int option =
        match Map.tryFind kindKey policy.Overrides with
        | Some cap -> cap
        | None     -> policy.DefaultMaxRows

    /// True iff the kind's cell evidence is sampled (any finite cap).
    let isSampled (kindKey: SsKey) (policy: SamplingPolicy) : bool =
        (capFor kindKey policy) |> Option.isSome

    /// True iff NO kind can be sampled under this policy — the gate the
    /// single-scan derivation partition uses to skip per-kind checks.
    let isFullScan (policy: SamplingPolicy) : bool =
        policy.DefaultMaxRows.IsNone
        && policy.Overrides |> Map.forall (fun _ cap -> cap.IsNone)
