namespace Projection.Core.Passes

open Projection.Core

/// The bridge-retarget DECISION pass (chapter: generic bridge retargeting). Reads
/// the resolved retargets the operator declared (`Policy.BridgeRetarget`, bound
/// from `overrides.bridgeRetargets`), evaluates each one's readiness against its
/// assembled evidence (`BridgeRetarget.decide`), and produces the map of CLEARED
/// retargets — `referenceKey → bridge-attribute key` — which the `ChainStep`
/// write-back lands on `ComposeState.BridgeRetargets`, whence
/// `DecisionOverlay.ofComposeState` flattens it into `RetargetFk` for the emitter
/// + round-trip comparator.
///
/// **Pure + fail-closed.** A retarget whose `Retargeting` verdict is `Blocked`
/// contributes NO map entry — its FK stays on the original parent. That is never
/// silent: one `Annotated` lineage event per declared retarget records whether it
/// `cleared` or was `blocked`, keyed by the reference. Empty policy ⇒ empty map +
/// no events (skeleton-pure, byte-identical emission).
///
/// **Classification.** `OperatorIntent Identity` (align-I.3) — a retarget rules
/// which target a reference resolves *through* (identity resolution), the same
/// axis `UserFkReflowPass` lands on; A50 designates `Policy.BridgeRetarget →
/// OverlayAxis.Identity`. `Domain = Schema` — the decision governs an emitted
/// FK constraint.
[<RequireQualifiedAccess>]
module BridgeRetargetPass =

    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "bridgeRetarget"

    let private classification : Classification = OperatorIntent OverlayAxis.Identity

    /// One `Annotated` lineage event per declared retarget, keyed by the
    /// reference SsKey — the FULL typed decision rides the trail
    /// (align-I.7: `AnnotationDetail.BridgeRetargetTrailDecision`
    /// replaced the flattened `Label` narration; the rendered diagnostic
    /// string is byte-identical, and applied/declined egress now sees
    /// the verdict). The trail records precisely which supplemental
    /// evidence each retarget cleared on, and precisely which data facts
    /// a still-blocked retarget is missing (annotate-don't-suppress).
    let private outcomeEvent (plan: BridgeRetargetPlan) (decision: BridgeRetargetDecision) : LineageEvent =
        LineageEvent.forPass passName version classification plan.ReferenceKey (Annotated (BridgeRetargetTrailDecision decision))

    let private run (_catalog: Catalog) (policy: Policy) (_profile: Profile) : Lineage<Diagnostics<Map<SsKey, SsKey> * BridgeRetargetDecision list>> =
        use _ = Bench.scope "passes.bridgeRetarget"
        let retargetMap, decisions = BridgeRetarget.decide policy.BridgeRetarget
        let events = List.map2 outcomeEvent policy.BridgeRetarget.Plans decisions
        // align-I.7: the decision SET rides the pass output beside the
        // cleared-only map, landing on ComposeState.BridgeRetargetDecisions
        // so downstream consumers read verdicts without re-parsing the trail.
        LineageDiagnostics.ofValue (retargetMap, decisions)
        |> Lineage.tellMany events

    /// The registered transform — captures `policy`/`profile` (the `Build` closure
    /// threads them in) and curries them into `run`, leaving `Catalog` as the
    /// runtime arrow input. The `ChainStep` supplies `ComposeState.withBridgeRetargets`
    /// as the write-back.
    let registered (policy: Policy) (profile: Profile) : RegisteredTransform<Catalog, Map<SsKey, SsKey> * BridgeRetargetDecision list> =
        { Name = passName
          Domain = Schema
          StageBinding = Pass
          Sites =
            [ { SiteName = "bridgeRetarget"
                Classification = classification
                Rationale = "Reroute a declared foreign key to resolve through a bridge attribute (Policy.BridgeRetarget) instead of its original parent's primary key, when the retarget's readiness (evidence-backed quality-control checks) clears. OperatorIntent Identity (align-I.3): the operator rules which identity each declared reference resolves through; a blocked or unproven retarget lands NO map entry (the FK stays on the parent), recorded as a `blocked` lineage event. Empty policy ⇒ empty retarget map (byte-identical emission)." } ]
          Run = fun c -> run c policy profile
          Status = Active }
