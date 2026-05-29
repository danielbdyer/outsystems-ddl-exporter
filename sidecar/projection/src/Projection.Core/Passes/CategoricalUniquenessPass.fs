namespace Projection.Core.Passes

// LINT-ALLOW-FILE: pass-driver `opportunityEntry` builds
// operator-facing diagnostic message text via `sprintf`
// (multi-line prose with numeric / decimal interpolation —
// e.g., "Mandatory column has %d/%d nulls observed (budget %s)").
// Per audit Tier-3 SUBJ leave-and-document: human-readable
// diagnostic prose is the discipline's allowed exception (no
// typed BCL alternative produces equivalent message text).
// Outcome / KeepReason / Conflict / Evidence rendering retired
// to typed `StructuredString` via `Outcome.toDiagnosticString`
// (chapter 3.5 slice φ); only the prose remains here.

open Projection.Core

/// The CategoricalUniquenessPass — V2's first **distribution-aware**
/// strategy pass driver (ADMIRE.md 2026-05-13). Produces an
/// emitter-consumable `CategoricalUniquenessDecisionSet` per A32; the
/// catalog itself is unchanged — uniqueness suggestions are metadata
/// emitters and downstream strategies consume.
///
/// **Strategy-layer codification (DECISIONS 2026-05-11).** Fourth
/// instance of the registered-intervention sub-pattern; the pass
/// driver mirrors `NullabilityPass`, `UniqueIndexPass`, and
/// `ForeignKeyPass` in shape:
///
///   - **Algebra layer.** Registry iteration over registered
///     `CategoricalUniqueness` interventions, per-(attribute ×
///     intervention) fan-out, decision accumulation, lineage
///     emission.
///   - **Domain seam.** Calls into `CategoricalUniquenessRules.evaluate`;
///     the pass knows nothing about the signal hierarchy or the
///     evidence-vs-threshold logic.
///   - **Per-attribute granularity.** Same as `NullabilityPass`;
///     contrasts with `UniqueIndexPass` (per-index granularity in
///     the same conceptual domain).
///
/// **Observable identity on empty policy** (DECISIONS 2026-05-09):
/// an empty `TighteningPolicy.Interventions` (or one with no
/// CategoricalUniqueness variants) yields the empty decision set
/// with no lineage events. V2 takes no action on per-attribute
/// uniqueness inference unless explicitly directed.
///
/// **The deferred-decisions cash-out reaches here.** Session 11's
/// commits 4 and 5 evaluate the composition vocabulary (`fanOut`
/// candidate) and the generic `StrategyEvaluator` alias against the
/// now-four-instance evidence base. This pass driver is the fourth
/// data point.
[<RequireQualifiedAccess>]
module CategoricalUniquenessPass =

    /// Pass version. Bump when:
    /// - the lineage event detail format changes
    /// - the iteration order changes
    /// - the decision-collection semantics change
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "categoricalUniqueness"

    /// One lineage event per decision. `Annotated` because the pass
    /// produces a decision (a real transformation in the audit sense)
    /// rather than observing without changing — same convention as
    /// the three predecessors. Chapter-3.6 slice-β widened the
    /// payload to the typed
    /// `AnnotationDetail.CategoricalUniquenessDecision` variant.
    /// Pillar 9 (chapter A.4.7 slice α): categorical-uniqueness
    /// promotion strengthens uniqueness invariants on categorical
    /// attributes beyond source evidence per operator-supplied
    /// Tightening policy. Operator intent on the Tightening axis.
    /// Lands as registered overlay.
    let private classification : Classification = OperatorIntent Tightening

    let private decisionEvent (decision: CategoricalUniquenessDecision) : LineageEvent =
        { PassName       = passName
          PassVersion    = version
          SsKey          = decision.AttributeKey
          TransformKind  =
              Annotated (CategoricalUniquenessDecision (decision.InterventionId, decision.Outcome))
          Classification = classification }

    /// Sort the iteration source deterministically — kinds by `SsKey`,
    /// attributes by `SsKey` within each kind. Interventions are taken
    /// in registration order. The shape mirrors `sortedAttributes`
    /// from `NullabilityPass`; both delegate to `Catalog.kindContexts`
    /// (the named primitive that absorbed the four-pass duplication at
    /// the convergence).
    let private sortedAttributes (catalog: Catalog) : (Kind * Attribute) list =
        Catalog.kindContexts (fun k -> k.Attributes) (fun a -> a.SsKey) catalog

    /// Run the CategoricalUniquenessPass.
    ///
    /// **Observable identity on empty policy.** When no
    /// CategoricalUniqueness interventions are registered, the
    /// result is the empty decision set with an empty trail. No
    /// work is done; no events are emitted; the catalog is not
    /// consulted. V2's strict default holds for this strategy
    /// exactly as it does for the three predecessors.
    ///
    /// **Decision composition.** When interventions are registered,
    /// the pass emits one `CategoricalUniquenessDecision` per
    /// (attribute × intervention) pair, plus one `Annotated` lineage
    /// event per decision. Iteration order is deterministic: kinds
    /// by `SsKey`, attributes by `SsKey`, interventions by
    /// registration order.
    // Chapter A.4.7' slice η: `let run` is private; canonical surface is `CategoricalUniquenessPass.registered.Run`
    let private run (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<CategoricalUniquenessDecisionSet> =
        use _ = Bench.scope "passes.categoricalUniqueness"
        let fanOutConfig : Composition.FanOutConfig<Kind * Attribute, _, _, _> = {
            InterventionFilter = TighteningPolicy.categoricalUniquenessInterventions
            SortedContexts     = sortedAttributes
            Evaluate           = fun id cfg (_kind, attr) prof ->
                CategoricalUniquenessRules.evaluate id cfg attr prof
            EmptyDecisionSet   = CategoricalUniquenessRules.emptyDecisionSet
            WrapDecisions      = fun decisions -> { Decisions = decisions }
            BuildEvent         = decisionEvent
        }
        Composition.fanOut fanOutConfig catalog policy profile

    /// Chapter A.4.7 slice γ — factory. Captures the operator-supplied
    /// `Policy` + `Profile` in closure. Single `OperatorIntent
    /// Tightening` site — the Tightening policy promotes categorical
    /// uniqueness per operator opinion. The pass returns plain
    /// `Lineage<DecisionSet>` (no Diagnostics); the Run closure wraps
    /// via `Lineage.map Diagnostics.ofValue` to match the registry's
    /// canonical shape.
    let registered (policy: Policy) (profile: Profile) : RegisteredTransform<Catalog, CategoricalUniquenessDecisionSet> =
        { Name = passName
          Domain = Data
          StageBinding = Pass
          Sites =
            [ { SiteName = "tightenCategoricalUniqueness"
                Classification = classification
                Rationale = "Promote categorical uniqueness per operator-supplied Tightening policy. Profile evidence drives the empirical decisions; lands as Tightening-axis overlay." } ]
          Run = fun c -> run c policy profile |> Lineage.map Diagnostics.ofValue
          Status = Active }
