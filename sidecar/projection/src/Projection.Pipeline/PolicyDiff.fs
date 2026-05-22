namespace Projection.Pipeline

open Projection.Core

/// Axis-level comparison between two `Policy` values (H-033).
///
/// Each `PolicyAxisDiff<'a>` carries the before and after value for one
/// axis, plus a `Changed` flag. The aggregate `PolicyDiff` record collects
/// all five axes and provides `AnyChanged` for quick triage.
type PolicyAxisDiff<'a> = {
    Before  : 'a
    After   : 'a
    Changed : bool
}

/// Five-axis structural diff between two policies (H-033).
type PolicyDiff = {
    Selection    : PolicyAxisDiff<SelectionPolicy>
    Emission     : PolicyAxisDiff<EmissionPolicy>
    Insertion    : PolicyAxisDiff<InsertionPolicy>
    Tightening   : PolicyAxisDiff<TighteningPolicy>
    UserMatching : PolicyAxisDiff<UserMatchingStrategy>
    /// True iff at least one axis changed between `before` and `after`.
    AnyChanged   : bool
}

/// Per-Kind lineage-and-diagnostics delta between two pipeline runs under
/// different policies (H-033 full-projection variant).
///
/// `BeforeEvents` / `AfterEvents` are the lineage events for this SsKey
/// from the two runs. `BeforeDiagnostics` / `AfterDiagnostics` are the
/// diagnostic entries naming this SsKey from the two runs. A `KindDelta`
/// is emitted only when at least one of the four collections differs
/// between runs.
type KindDelta = {
    SsKey              : SsKey
    BeforeEvents       : LineageEvent list
    AfterEvents        : LineageEvent list
    BeforeDiagnostics  : DiagnosticEntry list
    AfterDiagnostics   : DiagnosticEntry list
}

/// Full-projection diff: structural axis diff plus per-Kind lineage and
/// diagnostic deltas observed by running the pipeline twice under the
/// two policies (H-033 full-projection variant).
type FullProjectionDiff = {
    /// Structural axis-level diff of the two `Policy` records.
    StructuralDiff : PolicyDiff
    /// Per-Kind deltas — only SsKeys with observable differences.
    /// Sorted by SsKey for deterministic output.
    KindDeltas     : KindDelta list
    /// SsKeys whose `LineageEvent` set differs between the two runs.
    /// Includes kinds added/removed under the new policy and kinds
    /// where any pass produced a different event shape.
    ChangedKinds   : SsKey list
}


/// Policy diff construction and the `diffPolicy` lineage-bearing comparator (H-033).
[<RequireQualifiedAccess>]
module PolicyDiff =

    let private axisOf (before: 'a) (after: 'a) : PolicyAxisDiff<'a> =
        { Before = before; After = after; Changed = before <> after }

    /// Compute the five-axis structural diff between two policies. Pure;
    /// no side effects.
    let compare (before: Policy) (after: Policy) : PolicyDiff =
        let selection    = axisOf before.Selection    after.Selection
        let emission     = axisOf before.Emission     after.Emission
        let insertion    = axisOf before.Insertion    after.Insertion
        let tightening   = axisOf before.Tightening   after.Tightening
        let userMatching = axisOf before.UserMatching after.UserMatching
        { Selection    = selection
          Emission     = emission
          Insertion    = insertion
          Tightening   = tightening
          UserMatching = userMatching
          AnyChanged   =
              selection.Changed
           || emission.Changed
           || insertion.Changed
           || tightening.Changed
           || userMatching.Changed }

    /// Group lineage events by `SsKey`. Preserves chronological order
    /// within each group.
    let private eventsByKey (events: LineageEvent list) : Map<SsKey, LineageEvent list> =
        events
        |> List.fold (fun (acc: Map<SsKey, LineageEvent list>) e ->
            let existing = Map.tryFind e.SsKey acc |> Option.defaultValue []
            Map.add e.SsKey (existing @ [e]) acc) Map.empty

    /// Group diagnostics by their carried `SsKey`. Diagnostics with no
    /// SsKey are dropped (they are catalog-level observations, not per-
    /// Kind observations).
    let private diagnosticsByKey (entries: DiagnosticEntry list) : Map<SsKey, DiagnosticEntry list> =
        entries
        |> List.fold (fun (acc: Map<SsKey, DiagnosticEntry list>) d ->
            match d.SsKey with
            | None -> acc
            | Some k ->
                let existing = Map.tryFind k acc |> Option.defaultValue []
                Map.add k (existing @ [d]) acc) Map.empty

    /// Compute the per-Kind deltas between two pipeline runs.
    ///
    /// Compares each kind's lineage events and diagnostics across the
    /// two runs; emits a `KindDelta` for every SsKey where any of the
    /// four collections differs.
    let private kindDeltas
        (beforeEvents: LineageEvent list)
        (afterEvents: LineageEvent list)
        (beforeDiagnostics: DiagnosticEntry list)
        (afterDiagnostics: DiagnosticEntry list)
        : KindDelta list =
        let beforeEventMap = eventsByKey beforeEvents
        let afterEventMap  = eventsByKey afterEvents
        let beforeDiagMap  = diagnosticsByKey beforeDiagnostics
        let afterDiagMap   = diagnosticsByKey afterDiagnostics
        let keys =
            Set.union
                (Map.keys beforeEventMap |> Set.ofSeq)
                (Set.union
                    (Map.keys afterEventMap |> Set.ofSeq)
                    (Set.union
                        (Map.keys beforeDiagMap |> Set.ofSeq)
                        (Map.keys afterDiagMap  |> Set.ofSeq)))
        keys
        |> Set.toList
        |> List.sort
        |> List.choose (fun k ->
            let be = Map.tryFind k beforeEventMap |> Option.defaultValue []
            let ae = Map.tryFind k afterEventMap  |> Option.defaultValue []
            let bd = Map.tryFind k beforeDiagMap  |> Option.defaultValue []
            let ad = Map.tryFind k afterDiagMap   |> Option.defaultValue []
            if be = ae && bd = ad then None
            else
                Some
                    { SsKey             = k
                      BeforeEvents      = be
                      AfterEvents       = ae
                      BeforeDiagnostics = bd
                      AfterDiagnostics  = ad })

    /// Run the registered pipeline chain for the given policy + profile
    /// and return the `Lineage<Diagnostics<ComposeState>>`. Helper for
    /// `diffFullProjection`.
    let private runChainForPolicy
        (catalog: Catalog)
        (policy: Policy)
        (profile: Profile)
        : Lineage<Diagnostics<ComposeState>> =
        use _ = Bench.scope "policyDiff.runChain"
        let chain = RegisteredTransforms.allChainStepsFor policy profile
        PassChainAdapter.compose chain (ComposeState.initial catalog)

    /// Compute the five-axis policy diff and return it in a lineage carrier
    /// (H-033 structural variant). Synonym for legacy callers; new callers
    /// should prefer `diffFullProjection` for the per-Kind trail diff.
    ///
    /// The returned `Lineage<PolicyDiff>` has an empty trail — the structural
    /// diff operates at the policy-aggregate level, not at the per-Kind level.
    let diffPolicy
        (policyBefore: Policy)
        (policyAfter: Policy)
        : Lineage<PolicyDiff> =
        Lineage.ofValue (compare policyBefore policyAfter)

    /// Full-projection diff (H-033). Runs the registered pass chain twice —
    /// once with `policyBefore`, once with `policyAfter` — and joins the
    /// resulting lineage trails and diagnostics on `SsKey`. Returns the
    /// structural axis diff plus per-Kind `KindDelta`s for every kind whose
    /// observable lineage or diagnostics changed.
    ///
    /// **Lineage carrier.** The returned `Lineage` trail is the concatenation
    /// of the two runs' trails (after-run appended to before-run), so
    /// downstream consumers can audit both observations. The `Diagnostics`
    /// trail is similarly merged.
    ///
    /// **Pillar 9 fit.** Both runs are evidence-driven; the diff itself is
    /// a `DataIntent`-classified observation — it reports what the pipeline
    /// produced, not what the operator intended.
    let diffFullProjection
        (catalog: Catalog)
        (profile: Profile)
        (policyBefore: Policy)
        (policyAfter: Policy)
        : Lineage<FullProjectionDiff> =
        use _ = Bench.scope "policyDiff.diffFullProjection"
        let structural = compare policyBefore policyAfter
        let runBefore = runChainForPolicy catalog policyBefore profile
        let runAfter  = runChainForPolicy catalog policyAfter  profile
        let beforeEntries = LineageDiagnostics.entries runBefore
        let afterEntries  = LineageDiagnostics.entries runAfter
        let deltas =
            kindDeltas runBefore.Trail runAfter.Trail beforeEntries afterEntries
        let changedKinds = deltas |> List.map (fun d -> d.SsKey)
        let result =
            { StructuralDiff = structural
              KindDeltas     = deltas
              ChangedKinds   = changedKinds }
        { Value = result
          Trail = runBefore.Trail @ runAfter.Trail }
