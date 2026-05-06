namespace Projection.Core.Passes

open Projection.Core

/// The NullabilityPass — V1 `NullabilityEvaluator` migrated as a pure
/// F# pass producing an emitter-consumable `NullabilityDecisionSet`
/// (per A32). The catalog itself is unchanged — nullability decisions
/// are metadata that emitters consume; the catalog's structural truth
/// (`IsPrimaryKey`, `Column.IsNullable`) stays the source.
///
/// The pass is the *driver*: for each registered Nullability
/// intervention in the policy, walk every kind × attribute and apply
/// the per-attribute rule (`NullabilityRules.evaluate`). Per the
/// algebra/domain split (DECISIONS 2026-05-09), the algebra
/// (registry-iteration, accumulation, lineage emission) lives here;
/// the per-decision logic (signal hierarchy, threshold formula,
/// rationale composition) lives in `NullabilityRules`.
///
/// Structural commitment: **observable identity on empty policy**
/// (DECISIONS 2026-05-09). An empty `TighteningPolicy` yields the
/// empty `NullabilityDecisionSet` with no lineage events. V2 takes no
/// action on tightening unless explicitly directed.
[<RequireQualifiedAccess>]
module NullabilityPass =

    /// Pass version. Bump when:
    /// - the lineage event detail format changes
    /// - the iteration order changes
    /// - the decision-collection semantics change
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "nullability"

    /// Format the outcome for the lineage event's `Annotated` detail.
    /// The full structured `NullabilityOutcome` lives in
    /// `NullabilityDecisionSet.Decisions`; the trail carries a
    /// human-readable summary so audit consumers can grep for
    /// outcome categories without parsing decisions.
    let private outcomeLabel (outcome: NullabilityOutcome) : string =
        match outcome with
        | NullabilityOutcome.EnforceNotNull evidence ->
            sprintf "EnforceNotNull(%A)" evidence
        | NullabilityOutcome.KeepNullable reason ->
            sprintf "KeepNullable(%A)" reason
        | NullabilityOutcome.RequireOperatorApproval conflict ->
            sprintf "RequireOperatorApproval(%A)" conflict

    /// One lineage event per decision. `Annotated` because the pass
    /// produces a decision (a real transformation in the audit sense)
    /// rather than observing without changing.
    let private decisionEvent (decision: NullabilityDecision) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = decision.AttributeKey
          TransformKind =
              Annotated
                  (sprintf "%s -> %s"
                      decision.InterventionId
                      (outcomeLabel decision.Outcome)) }

    /// Sort the iteration source deterministically — kinds by `SsKey`,
    /// attributes by `SsKey` within each kind. Interventions are taken
    /// in registration order (the caller chose the order; the pass
    /// preserves it).
    let private sortedAttributes (catalog: Catalog) : (Kind * Attribute) list =
        catalog.Modules
        |> List.collect (fun m -> m.Kinds)
        |> List.sortBy (fun k -> k.SsKey)
        |> List.collect (fun k ->
            k.Attributes
            |> List.sortBy (fun a -> a.SsKey)
            |> List.map (fun a -> k, a))

    /// Run the NullabilityPass.
    ///
    /// **Observable identity on empty policy.** When the policy has no
    /// Nullability interventions registered, the result is the empty
    /// decision set with an empty trail. No work is done; no events are
    /// emitted; the catalog is not consulted. This is V2's strict
    /// default (DECISIONS 2026-05-09).
    ///
    /// **Decision composition.** When interventions are registered, the
    /// pass emits one `NullabilityDecision` per
    /// (attribute × intervention) pair, plus one `Annotated` lineage
    /// event per decision. Iteration order is deterministic: kinds by
    /// `SsKey`, attributes by `SsKey`, interventions by registration
    /// order.
    let run (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<NullabilityDecisionSet> =
        let interventions = TighteningPolicy.nullabilityInterventions policy.Tightening
        if List.isEmpty interventions then
            // Observable identity on empty policy — no decisions, no
            // events. The catalog is unread; the profile is unread.
            Lineage.ofValue NullabilityRules.emptyDecisionSet
        else
            let decisions =
                [ for (_kind, attribute) in sortedAttributes catalog do
                    for (interventionId, config) in interventions do
                        yield NullabilityRules.evaluate interventionId config attribute profile ]
            let events = decisions |> List.map decisionEvent
            Lineage.tellMany events
                (Lineage.ofValue { Decisions = decisions })
