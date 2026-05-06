namespace Projection.Core.Passes

open Projection.Core

/// The UniqueIndexPass — V1 `UniqueIndexDecisionOrchestrator` migrated
/// as a pure F# pass producing an emitter-consumable
/// `UniqueIndexDecisionSet` (per A32). The catalog itself is unchanged
/// — uniqueness decisions are metadata that emitters consume; the
/// catalog's structural truth (`Index.IsUnique`) stays the source.
///
/// **Algebra/domain separation (per the session-7 reminder).** This
/// module is the *algebra* layer: registry-iteration over registered
/// `UniqueIndex` interventions, walking each kind × index × intervention,
/// accumulating decisions, emitting lineage events. The per-decision
/// logic — which signals fire, which thresholds apply, what rationale
/// to attach — lives in `UniqueIndexRules`. The pass calls into the
/// rules module via the typed seam (`UniqueIndexRules.evaluate`); the
/// pass knows nothing about the policy reasoning.
///
/// **Per-index granularity (the structural divergence from
/// NullabilityPass).** NullabilityPass walks attributes; UniqueIndexPass
/// walks indexes. The closed-DU `TighteningIntervention` seam handles
/// this naturally: each pass filters to its own variant and uses its
/// own iteration shape. The dispatcher (the orchestrator that calls
/// both passes when both intervention types are registered) lives at
/// the call site, not inside either pass.
///
/// **Observable identity on empty policy** (DECISIONS 2026-05-09): an
/// empty `TighteningPolicy.Interventions` (or one with no UniqueIndex
/// variants) yields the empty `UniqueIndexDecisionSet` with no lineage
/// events. V2 takes no action on uniqueness unless explicitly directed.
[<RequireQualifiedAccess>]
module UniqueIndexPass =

    /// Pass version. Bump when:
    /// - the lineage event detail format changes
    /// - the iteration order changes
    /// - the decision-collection semantics change
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "uniqueIndex"

    /// Format the outcome for the lineage event's `Annotated` detail.
    /// The full structured `UniqueIndexOutcome` lives in
    /// `UniqueIndexDecisionSet.Decisions`; the trail carries a
    /// human-readable summary so audit consumers can grep for outcome
    /// categories without parsing decisions.
    let private outcomeLabel (outcome: UniqueIndexOutcome) : string =
        match outcome with
        | UniqueIndexOutcome.EnforceUnique evidence ->
            sprintf "EnforceUnique(%A)" evidence
        | UniqueIndexOutcome.DoNotEnforce reason ->
            sprintf "DoNotEnforce(%A)" reason

    /// One lineage event per decision. `Annotated` because the pass
    /// produces a decision (a real transformation in the audit sense)
    /// rather than observing without changing — same convention as
    /// `NullabilityPass`.
    let private decisionEvent (decision: UniqueIndexDecision) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = decision.IndexKey
          TransformKind =
              Annotated
                  (sprintf "%s -> %s"
                      decision.InterventionId
                      (outcomeLabel decision.Outcome)) }

    /// Sort the iteration source deterministically — kinds by `SsKey`,
    /// indexes by `SsKey` within each kind. Interventions are taken
    /// in registration order (the caller chose the order; the pass
    /// preserves it). This is the per-index analogue of
    /// `NullabilityPass.sortedAttributes`.
    let private sortedIndexes (catalog: Catalog) : (Kind * Index) list =
        catalog.Modules
        |> List.collect (fun m -> m.Kinds)
        |> List.sortBy (fun k -> k.SsKey)
        |> List.collect (fun k ->
            k.Indexes
            |> List.sortBy (fun ix -> ix.SsKey)
            |> List.map (fun ix -> k, ix))

    /// Run the UniqueIndexPass.
    ///
    /// **Observable identity on empty policy.** When no `UniqueIndex`
    /// interventions are registered, the result is the empty decision
    /// set with an empty trail. No work is done; no events are emitted;
    /// the catalog is not consulted. V2's strict default holds for the
    /// per-index granularity exactly as it does for the per-attribute
    /// granularity.
    ///
    /// **Decision composition.** When interventions are registered, the
    /// pass emits one `UniqueIndexDecision` per (index × intervention)
    /// pair, plus one `Annotated` lineage event per decision.
    /// Iteration order is deterministic: kinds by `SsKey`, indexes by
    /// `SsKey`, interventions by registration order.
    let run (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<UniqueIndexDecisionSet> =
        let interventions = TighteningPolicy.uniqueIndexInterventions policy.Tightening
        if List.isEmpty interventions then
            // Observable identity — no decisions, no events.
            Lineage.ofValue UniqueIndexRules.emptyDecisionSet
        else
            let decisions =
                [ for (kind, index) in sortedIndexes catalog do
                    for (interventionId, config) in interventions do
                        yield UniqueIndexRules.evaluate interventionId config kind index profile ]
            let events = decisions |> List.map decisionEvent
            Lineage.tellMany events
                (Lineage.ofValue { Decisions = decisions })
