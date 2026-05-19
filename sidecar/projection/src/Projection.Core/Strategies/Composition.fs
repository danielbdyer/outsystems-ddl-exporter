namespace Projection.Core

/// Strategy composition primitives — the canonical place for
/// patterns that appear repeatedly across registered-intervention
/// pass drivers (DECISIONS 2026-05-13 — emergent primitives earn
/// their place through multi-consumer demand; threshold is
/// two consumers).
///
/// **Codification cash-out (DECISIONS 2026-05-11 + 2026-05-13).**
/// Session 8's composition-vocabulary sketch enumerated five
/// candidates: `fanOut`, `fallback`, `accumulate`, `wrap`, `lift`.
/// The deferred-decisions trigger fired at session 11's fourth
/// registered-intervention strategy (`CategoricalUniqueness`).
/// Empirical state at the trigger:
///
///   - `fanOut`: **four consumers** (NullabilityPass, UniqueIndexPass,
///     ForeignKeyPass, CategoricalUniquenessPass). Threshold met
///     by a wide margin. Codified here.
///   - `fallback`: **zero consumers**. No strategy falls back to
///     another. Deferred until a real consumer arrives.
///   - `accumulate`: **zero consumers**. No strategy aggregates
///     across other strategies. Deferred.
///   - `wrap`: **zero consumers**. No instrumented strategies.
///     Deferred.
///   - `lift`: **zero consumers**. No context translation needed.
///     Deferred.
///
/// The principle (DECISIONS 2026-05-13): codify what's earned;
/// defer what hasn't. The four deferrals are recorded for future
/// reference; if any of them ever ships its first consumer, the
/// extraction lands when the second consumer arrives, not the
/// first.
/// The canonical strategy-evaluator shape — a function from
/// `(interventionId, config, context, profile)` to a decision.
/// **Codified at session 11 commit 5** as the second
/// deferred-decisions cash-out from session 8 (DECISIONS 2026-05-11
/// — generic StrategyEvaluator alias deferred). The shape was
/// observed across three strategies at session 7 (Nullability,
/// UniqueIndex, ForeignKey); the fourth (CategoricalUniqueness,
/// session 11) fits exactly. Per the discipline (DECISIONS
/// 2026-05-13 — emergent primitives), the fourth empirical
/// confirmation earns the alias.
///
/// **What the alias does.** Names the canonical four-input shape
/// that every registered-intervention strategy's evaluate function
/// conforms to (sometimes via lambda adaptation when the rules
/// module's natural signature has an extra argument like
/// `ForeignKeyRules.evaluate`'s catalog parameter). The
/// `Composition.FanOutConfig.Evaluate` field is typed as this
/// alias; future strategy authors typing their evaluate function
/// against this alias get a compile-time check that the shape is
/// preserved.
///
/// **What the alias doesn't do.** It doesn't force every rules
/// module to change its signature. `ForeignKeyRules.evaluate`
/// continues to take `Catalog` as a separate argument (its natural
/// shape, given that FK decisions need cross-attribute reach for
/// target-kind lookup); the FanOutConfig.Evaluate lambda closes
/// over the catalog and adapts to the alias's shape. This honors
/// "uniform signature shape but variable arity context"
/// (DECISIONS 2026-05-11 — codification refinement 2): the alias
/// names the shape; the rules modules' natural signatures need
/// not all match it pointwise.
///
/// **What might force a future revision.** If a fifth strategy's
/// evaluate genuinely cannot adapt to this shape (e.g., needs an
/// asynchronous context, or returns multiple decisions per
/// invocation), the alias gets revisited. Per the codification
/// discipline (DECISIONS 2026-05-11 — empirical verdict on the
/// strategy layer), divergence is a tell, not a defeat.
type StrategyEvaluator<'context, 'config, 'decision> =
    string -> 'config -> 'context -> Profile -> 'decision


[<RequireQualifiedAccess>]
module Composition =

    /// Configuration for a `fanOut` invocation. Carries the
    /// strategy-specific functions the algebra needs:
    ///
    ///   - `InterventionFilter`: extract this strategy's interventions
    ///     from the policy's intervention registry. The closed
    ///     `TighteningIntervention` DU's per-variant filter helpers
    ///     fit this shape (e.g.,
    ///     `TighteningPolicy.nullabilityInterventions`).
    ///   - `SortedContexts`: enumerate the catalog's records of the
    ///     strategy's granularity (per-attribute, per-index,
    ///     per-reference, ...) in deterministic order. The four
    ///     existing strategies use SsKey ordering at every level.
    ///   - `Evaluate`: the strategy's typed seam, conforming to the
    ///     `StrategyEvaluator<'context, 'config, 'decision>` alias.
    ///     Strategies whose rules module exposes additional context
    ///     (e.g., `ForeignKeyRules.evaluate`'s catalog parameter)
    ///     close over it via a lambda when constructing the
    ///     FanOutConfig.
    ///   - `EmptyDecisionSet`: V2's strict-default value when no
    ///     interventions are registered. Returned wrapped in
    ///     `Lineage.ofValue` (no events; no work).
    ///   - `WrapDecisions`: build the strategy's decision-set value
    ///     from the accumulated decision list. Typically
    ///     `fun decisions -> { Decisions = decisions }`.
    ///   - `BuildEvent`: produce one `LineageEvent` per decision.
    ///     The strategy's pass driver supplies its own
    ///     `decisionEvent`-style helper.
    type FanOutConfig<'context, 'config, 'decision, 'decisionSet> = {
        InterventionFilter : TighteningPolicy -> (string * 'config) list
        SortedContexts     : Catalog -> 'context list
        Evaluate           : StrategyEvaluator<'context, 'config, 'decision>
        EmptyDecisionSet   : 'decisionSet
        WrapDecisions      : 'decision list -> 'decisionSet
        BuildEvent         : 'decision -> LineageEvent
    }

    /// Run a registered-intervention strategy via fan-out: for each
    /// (context × intervention) pair, call `evaluate` and accumulate
    /// the resulting decision; emit one `LineageEvent` per decision;
    /// wrap into the strategy's decision-set value.
    ///
    /// **Observable identity on empty policy** (DECISIONS 2026-05-09):
    /// when the intervention filter returns an empty list, the
    /// catalog is not enumerated; no decisions are produced; no
    /// events are emitted. The pure-default discipline is
    /// preserved.
    ///
    /// **Determinism.** Iteration follows the strategy's
    /// `SortedContexts` ordering (typically SsKey at every level)
    /// and the policy's intervention registration order. Same
    /// triple → same output (T1 extended).
    let fanOut
        (config: FanOutConfig<'context, 'config, 'decision, 'decisionSet>)
        (catalog: Catalog)
        (policy: Policy)
        (profile: Profile)
        : Lineage<'decisionSet> =
        use _ = Bench.scope "composition.fanOut"
        let interventions = config.InterventionFilter policy.Tightening
        if List.isEmpty interventions then
            // Observable identity — no decisions, no events.
            // Catalog and profile are not consulted.
            Lineage.ofValue config.EmptyDecisionSet
        else
            let decisions =
                [ for ctx in config.SortedContexts catalog do
                    for (interventionId, cfg) in interventions do
                        yield config.Evaluate interventionId cfg ctx profile ]
            let events = decisions |> List.map config.BuildEvent
            Lineage.tellMany events
                (Lineage.ofValue (config.WrapDecisions decisions))
