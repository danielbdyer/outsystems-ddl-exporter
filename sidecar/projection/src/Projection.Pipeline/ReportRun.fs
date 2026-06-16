namespace Projection.Pipeline

open System.Text.Json.Nodes
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

    /// Surface a recorded Model Fidelity Report as operator-facing lines. The
    /// per-run `fidelity.json` artifact is read back (the codec inverse) and
    /// rendered as the count-first roll-up. `candidatePaths` are searched in
    /// order; the first readable + parseable `fidelity.json` wins. Empty list
    /// when none is found (the verb states the report is not recorded rather
    /// than fabricating one). The store load stays the only mandatory I/O; this
    /// is an additive, best-effort surfacing.
    let renderFidelity (candidatePaths: string list) : string list =
        candidatePaths
        |> List.tryPick (fun path ->
            try
                if System.IO.File.Exists path then
                    System.IO.File.ReadAllText path
                    |> ModelFidelity.fromJson
                    |> Option.map ModelFidelity.render
                else None
            with _ -> None)
        |> Option.defaultValue []

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

    // ----------------------------------------------------------------------
    // The machine lens (M18 / THE VECTOR §5.2) — the change report as a
    // structured, byte-deterministic JSON document, the sibling of `render`.
    // The engine treats human / machine as two lenses on one typed value; the
    // CDC-capture-count data norm (T15) is a real measured value that flowed
    // to `render` as prose ONLY. `toJson` gives the SSIS consumer the second
    // lens — a contract they can diff sprint-over-sprint, the engine's #1
    // fidelity surface made queryable. Typed-AST emission (`JsonNode`), not
    // string-building (pillar 1). Deterministic: the bundle's manifests are
    // already T1-ordered (oldest→newest), the residual is name-sorted, the
    // applied-transforms are pre-sorted at their source.
    //
    // `toJson` ONLY — there is no read-back consumer (`report` reads the
    // durable lifecycle STORE, not a recorded change-report). Per IR-grows-
    // under-evidence a `fromJson` inverse is deferred-with-trigger: a verb that
    // reads a recorded change-report back materializes. The `ModelFidelity`
    // codec (which DOES carry `fromJson`) earned its inverse from the `report`
    // verb's `fidelity.json` read-back; this surface has no such consumer yet.

    let private coordinateNode (c: EpisodeCoordinate) : JsonObject =
        let version = JsonObject()
        version.["ordinal"] <- JsonValue.Create (Version.ordinal c.Version)
        version.["label"] <- JsonValue.Create (Version.label c.Version)
        let o = JsonObject()
        o.["version"] <- version
        o.["environment"] <- JsonValue.Create (Environment.name c.Environment)
        o.["at"] <- JsonValue.Create (c.At.ToString("o", System.Globalization.CultureInfo.InvariantCulture))
        o

    /// All twenty orthogonal move-channels (π) of the displacement, named —
    /// the schema norm `‖δ‖` is their sum (T15), so a consumer can both read
    /// the total and attribute it to its channels.
    let private channelsNode (c: CatalogDiff.ChannelCounts) : JsonObject =
        let o = JsonObject()
        o.["renamedKinds"]      <- JsonValue.Create c.RenamedKinds
        o.["addedKinds"]        <- JsonValue.Create c.AddedKinds
        o.["removedKinds"]      <- JsonValue.Create c.RemovedKinds
        o.["changedKinds"]      <- JsonValue.Create c.ChangedKinds
        o.["addedAttributes"]   <- JsonValue.Create c.AddedAttributes
        o.["removedAttributes"] <- JsonValue.Create c.RemovedAttributes
        o.["renamedAttributes"] <- JsonValue.Create c.RenamedAttributes
        o.["changedAttributes"] <- JsonValue.Create c.ChangedAttributes
        o.["addedReferences"]   <- JsonValue.Create c.AddedReferences
        o.["removedReferences"] <- JsonValue.Create c.RemovedReferences
        o.["renamedReferences"] <- JsonValue.Create c.RenamedReferences
        o.["changedReferences"] <- JsonValue.Create c.ChangedReferences
        o.["addedIndexes"]      <- JsonValue.Create c.AddedIndexes
        o.["removedIndexes"]    <- JsonValue.Create c.RemovedIndexes
        o.["renamedIndexes"]    <- JsonValue.Create c.RenamedIndexes
        o.["changedIndexes"]    <- JsonValue.Create c.ChangedIndexes
        o.["addedSequences"]    <- JsonValue.Create c.AddedSequences
        o.["removedSequences"]  <- JsonValue.Create c.RemovedSequences
        o.["renamedSequences"]  <- JsonValue.Create c.RenamedSequences
        o.["changedSequences"]  <- JsonValue.Create c.ChangedSequences
        o

    let private manifestNode (m: ChangeManifest) : JsonObject =
        let o = JsonObject()
        o.["from"]            <- coordinateNode m.From
        o.["to"]              <- coordinateNode m.To
        o.["schemaNorm"]      <- JsonValue.Create m.SchemaNorm
        o.["cdcCaptureCount"] <- JsonValue.Create m.CdcCaptureCount
        // A `None` Decision anchor renders as an explicit JSON null (a stable
        // key the consumer can read), assigned through the nullable setter.
        (match m.RefactorLogRef with
         | Some r -> o.["refactorLogRef"] <- JsonValue.Create r
         | None   -> o.["refactorLogRef"] <- null)
        o.["channels"]        <- channelsNode m.Channels
        // The tolerance residual — the equivalence-up-to-quotient under which
        // this edge's displacement was deemed faithful (named, sorted).
        let residual = JsonArray()
        for d in m.ToleranceResidual do residual.Add(JsonValue.Create (ToleratedDivergence.name d))
        o.["toleranceResidual"] <- residual
        // The applied-transforms outcome — the per-artifact overlay enumeration
        // (`SsKey × OverlayAxis option`); the identity is the canonical
        // length-prefixed `SsKey.serialize` form (the same on-disk identity the
        // store uses), a `None` axis for a skeleton-only row.
        let applied = JsonArray()
        for (key, axis) in m.AppliedTransforms do
            let a = JsonObject()
            a.["ssKey"] <- JsonValue.Create (SsKey.serialize key)
            (match axis with
             | Some ax -> a.["axis"] <- JsonValue.Create (OverlayAxis.name ax)
             | None    -> a.["axis"] <- null)
            applied.Add(a)
        o.["appliedTransforms"] <- applied
        o

    /// Serialize the assembled change bundle to its structured document — the
    /// machine-read sibling of `render`. Counts pre-computed (path length, per-
    /// edge norm + channels) so a downstream reader gets the shape without
    /// re-deriving it from the store.
    let toJson (bundle: ReportBundle) : JsonObject =
        let root = JsonObject()
        root.["timeline"]     <- JsonValue.Create bundle.Timeline
        root.["episodeCount"] <- JsonValue.Create bundle.EpisodeCount
        root.["pathLength"]   <- JsonValue.Create bundle.PathLength
        let changes = JsonArray()
        for m in bundle.Manifests do changes.Add(manifestNode m)
        root.["changes"] <- changes
        root

    /// Render the bundle to a pretty-printed JSON string (the artifact body) —
    /// mirrors `ModelFidelity.toJsonString`, the sibling report codec.
    let toJsonString (bundle: ReportBundle) : string =
        let opts = System.Text.Json.JsonSerializerOptions(WriteIndented = true)
        (toJson bundle).ToJsonString(opts)
