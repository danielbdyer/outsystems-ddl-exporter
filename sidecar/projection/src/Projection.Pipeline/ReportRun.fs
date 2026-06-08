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
        | Error e -> Error (sprintf "could not load the lifecycle store: %A" e)
        | Ok chain ->
            match fromChain chain with
            | Ok b    -> Ok b
            | Error e -> Error (sprintf "could not compute the change series: %A" e)

    /// Render the bundle as operator-facing lines (THE_VOICE register: stative;
    /// the norm surfaced as the minimality proof). One line per recorded edge.
    let render (bundle: ReportBundle) : string list =
        [ yield sprintf "Change report — timeline '%s' (%d episode(s) recorded)." bundle.Timeline bundle.EpisodeCount
          yield sprintf "Total schema churn since genesis: %d move(s)." bundle.PathLength
          yield ""
          if List.isEmpty bundle.Manifests then
              yield "No schema change recorded since genesis (the timeline holds a single episode)."
          else
              yield "Changes, oldest to newest:"
              for m in bundle.Manifests do
                  let c = m.Channels
                  yield sprintf "  %s -> %s   norm=%d  (+%d / -%d / ~%d kinds; %d CDC capture(s))"
                            (Version.label m.From.Version) (Version.label m.To.Version)
                            m.SchemaNorm c.AddedKinds c.RemovedKinds c.RenamedKinds m.CdcCaptureCount ]
