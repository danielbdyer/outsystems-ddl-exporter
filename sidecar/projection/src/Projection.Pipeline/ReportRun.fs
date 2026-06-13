namespace Projection.Pipeline

open Projection.Core

// THE_CLI.md §8 / F4 — the migration-team change bundle. `report <flow>` reads
// the flow's target durable timeline (its `store`) and renders the recorded
// `ChangeManifest` series: what changed since the last sealed schema episode,
// each edge's norm `‖δ‖` surfaced as the minimality proof. The substrate is
// `LifecycleStore` (durable) + `ChangeManifest` (the per-edge displacement);
// this runner composes them (mirrors `EjectRun.fromStore`). The store load is
// the only I/O; `fromChain` and `render` are pure.

[<RequireQualifiedAccess>]
module ReportRun =

    /// The assembled bundle: the timeline name, how many episodes it holds, the
    /// per-edge change manifests (oldest → newest), and the total schema churn.
    type ReportBundle =
        {
            Timeline     : string
            EpisodeCount : int
            Manifests    : ChangeManifest list
            PathLength   : int
        }

    /// Build the bundle from a loaded lifecycle (pure).
    let fromChain (chain: EpisodicLifecycle) : Result<ReportBundle, EmitError> =
        match ChangeManifest.series chain, ChangeManifest.pathLength chain with
        | Ok manifests, Ok norm ->
            Ok { Timeline     = Timeline.name (EpisodicLifecycle.timeline chain)
                 EpisodeCount = List.length (EpisodicLifecycle.episodes chain)
                 Manifests    = manifests
                 PathLength   = norm }
        | Error e, _ -> Error e
        | _, Error e -> Error e

    /// Load the durable timeline at `path` and build the bundle (the operator
    /// entry). Fail-closed: a malformed store or non-composable edge → string.
    let fromStore (path: string) : Result<ReportBundle, string> =
        match LifecycleStore.load path with
        | Error e -> Error (LifecycleStore.describe e)
        | Ok chain ->
            match fromChain chain with
            | Ok b    -> Ok b
            | Error _ -> Error "the change series could not be computed from the timeline."

    /// Render the bundle as operator-facing lines (THE_VOICE register: stative;
    /// the norm surfaced as the minimality proof). One line per recorded edge.
    let render (bundle: ReportBundle) : string list =
        [ yield sprintf "Change report — timeline '%s', %d episode(s) recorded." bundle.Timeline bundle.EpisodeCount
          yield sprintf "Total changes recorded since genesis: %d." bundle.PathLength
          yield ""
          if List.isEmpty bundle.Manifests then
              yield "No schema change recorded since genesis — the timeline holds a single episode."
          else
              yield "Changes, oldest to newest:"
              for m in bundle.Manifests do
                  let c = m.Channels
                  yield sprintf "  %s → %s · %d schema change(s) (%d added · %d dropped · %d renamed) · %d row(s) captured"
                            (Version.label m.From.Version) (Version.label m.To.Version)
                            m.SchemaNorm c.AddedKinds c.RemovedKinds c.RenamedKinds m.CdcCaptureCount
                  // NM-32 — the change-accounting "under what equivalence was
                  // this accepted?" plane. The tolerance residual names the
                  // divergences this edge's canary accepted; the applied-
                  // transforms count is the per-artifact overlay enumeration. Both
                  // are surfaced only when non-empty (a strict, skeleton-only edge
                  // adds no provenance line — silence is the faithful case).
                  if not (List.isEmpty m.ToleranceResidual) then
                      let names = m.ToleranceResidual |> List.map ToleratedDivergence.name |> String.concat ", "
                      yield sprintf "      accepted under tolerance: %s" names
                  if not (List.isEmpty m.AppliedTransforms) then
                      yield sprintf "      applied transforms recorded: %d overlay row(s)" (List.length m.AppliedTransforms) ]
