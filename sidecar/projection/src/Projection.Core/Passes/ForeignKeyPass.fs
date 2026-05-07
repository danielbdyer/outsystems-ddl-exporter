namespace Projection.Core.Passes

open Projection.Core

/// The ForeignKeyPass — V1 `ForeignKeyEvaluator` migrated as a pure
/// F# pass producing an emitter-consumable
/// `ForeignKeyDecisionSet` (per A32). The catalog itself is unchanged
/// — FK decisions are metadata that emitters consume; the catalog's
/// structural truth (`Reference.OnDelete`, `Reference.TargetKind`)
/// stays the source.
///
/// **Strategy-layer codification (DECISIONS 2026-05-11).** Third
/// instance of the registered-intervention sub-pattern; the pass
/// driver mirrors `NullabilityPass` and `UniqueIndexPass` exactly:
///
///   - **Algebra layer.** Registry iteration over registered
///     `ForeignKey` interventions, per-(reference × intervention)
///     fan-out, decision accumulation, lineage emission.
///   - **Domain seam.** Calls into `ForeignKeyRules.evaluate`; the
///     pass knows nothing about cross-schema gates, orphan handling,
///     or NoCheck logic.
///   - **Per-reference granularity.** The third structural granularity
///     after per-attribute (Nullability) and per-index (UniqueIndex);
///     the closed-DU `TighteningIntervention` seam handles all three
///     naturally.
///
/// **Observable identity on empty policy** (DECISIONS 2026-05-09):
/// an empty `TighteningPolicy.Interventions` (or one with no
/// ForeignKey variants) yields the empty `ForeignKeyDecisionSet`
/// with no lineage events. V2 takes no action on FK constraints
/// unless explicitly directed.
[<RequireQualifiedAccess>]
module ForeignKeyPass =

    /// Pass version. Bump when:
    /// - the lineage event detail format changes
    /// - the iteration order changes
    /// - the decision-collection semantics change
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "foreignKey"

    /// Format the outcome for the lineage event's `Annotated` detail.
    /// The full structured `ForeignKeyOutcome` lives in
    /// `ForeignKeyDecisionSet.Decisions`; the trail carries a
    /// human-readable summary so audit consumers can grep for outcome
    /// categories without parsing decisions.
    let private outcomeLabel (outcome: ForeignKeyOutcome) : string =
        match outcome with
        | ForeignKeyOutcome.EnforceConstraint evidence ->
            sprintf "EnforceConstraint(%A)" evidence
        | ForeignKeyOutcome.DoNotEnforce reason ->
            sprintf "DoNotEnforce(%A)" reason

    /// One lineage event per decision. `Annotated` because the pass
    /// produces a decision (a real transformation in the audit sense)
    /// rather than observing without changing — same convention as
    /// `NullabilityPass` and `UniqueIndexPass`.
    let private decisionEvent (decision: ForeignKeyDecision) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = decision.ReferenceKey
          TransformKind =
              Annotated
                  (sprintf "%s -> %s"
                      decision.InterventionId
                      (outcomeLabel decision.Outcome)) }

    /// Sort the iteration source deterministically — kinds by `SsKey`,
    /// references by `SsKey` within each kind. Interventions are taken
    /// in registration order (the caller chose the order; the pass
    /// preserves it). The shape mirrors `sortedAttributes` /
    /// `sortedIndexes`; only the inner field changes.
    let private sortedReferences (catalog: Catalog) : (Kind * Reference) list =
        catalog.Modules
        |> List.collect (fun m -> m.Kinds)
        |> List.sortBy (fun k -> k.SsKey)
        |> List.collect (fun k ->
            k.References
            |> List.sortBy (fun r -> r.SsKey)
            |> List.map (fun r -> k, r))

    /// Run the ForeignKeyPass.
    ///
    /// **Observable identity on empty policy.** When no `ForeignKey`
    /// interventions are registered, the result is the empty decision
    /// set with an empty trail. No work is done; no events are emitted;
    /// the catalog is not consulted. V2's strict default holds for the
    /// per-reference granularity exactly as it does for per-attribute
    /// and per-index.
    ///
    /// **Decision composition.** When interventions are registered, the
    /// pass emits one `ForeignKeyDecision` per (reference × intervention)
    /// pair, plus one `Annotated` lineage event per decision.
    /// Iteration order is deterministic: kinds by `SsKey`, references
    /// by `SsKey`, interventions by registration order.
    let run (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<ForeignKeyDecisionSet> =
        // ForeignKey's evaluate takes the catalog as an additional
        // input (cross-attribute reach for target-kind lookup, schema
        // comparison). The closure captures it from the enclosing
        // scope; the FanOutConfig sees the uniform 4-arg shape.
        let fanOutConfig : Composition.FanOutConfig<Kind * Reference, _, _, _> = {
            InterventionFilter = TighteningPolicy.foreignKeyInterventions
            SortedContexts     = sortedReferences
            Evaluate           = fun id cfg (kind, reference) prof ->
                ForeignKeyRules.evaluate id cfg kind reference catalog prof
            EmptyDecisionSet   = ForeignKeyRules.emptyDecisionSet
            WrapDecisions      = fun decisions -> { Decisions = decisions }
            BuildEvent         = decisionEvent
        }
        Composition.fanOut fanOutConfig catalog policy profile
