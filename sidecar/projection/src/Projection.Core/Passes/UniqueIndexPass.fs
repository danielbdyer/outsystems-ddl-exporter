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
    /// - the diagnostic-entry shape, code namespace, or message
    ///   templates change (consumers grep on Code; bumping the version
    ///   makes a behavior-preserving rename detectable)
    ///
    /// v1 — Lineage<UniqueIndexDecisionSet>; no diagnostic emission.
    /// v2 — Lineage<Diagnostics<UniqueIndexDecisionSet>>; emits one
    ///       Warning DiagnosticEntry per DoNotEnforce decision per
    ///       DECISIONS 2026-05-13 (pass return-type codification) and
    ///       activates the V1 OpportunityBuilder.TryCreate contract for
    ///       UniqueIndex.
    [<Literal>]
    let version : int = 2

    [<Literal>]
    let private passName : string = "uniqueIndex"

    /// One lineage event per decision. `Annotated` because the pass
    /// produces a decision (a real transformation in the audit sense)
    /// rather than observing without changing — same convention as
    /// `NullabilityPass`. Chapter-3.6 slice-β widened the payload to
    /// the typed `AnnotationDetail.UniqueIndexDecision` variant; the
    /// outcome flows through structurally for audit consumers.
    let private decisionEvent (decision: UniqueIndexDecision) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = decision.IndexKey
          TransformKind =
              Annotated (UniqueIndexDecision (decision.InterventionId, decision.Outcome)) }

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

    /// V1's `OpportunityBuilder.TryCreate` (UniqueIndex flavor) emits
    /// an Opportunity record for every decision that does not enforce
    /// uniqueness or that requires remediation. V2's binary outcome
    /// collapses both V1 cases into `DoNotEnforce`; the
    /// keep-reason variant carries the structural reason. This
    /// function is the V2-shaped equivalent: each `DoNotEnforce`
    /// decision produces one Warning `DiagnosticEntry`.
    ///
    /// `EnforceUnique(_)` decisions emit no diagnostic — V2's positive
    /// outcome is structurally clean; V1's "EnforceUnique +
    /// RequiresRemediation" combination has no V2 counterpart in the
    /// binary outcome (the remediation requirement collapses to one of
    /// the keep-reasons).
    ///
    /// Code namespace: `tightening.uniqueIndex.<reason>`. Top-prefix
    /// `tightening.*` routes to consumers caring about policy-driven
    /// decisions; sub-prefix `uniqueIndex.*` distinguishes from
    /// `nullability.*` and `foreignKey.*` (when those passes activate
    /// their own diagnostic emission per the same codification).
    let private opportunityEntry (decision: UniqueIndexDecision) : DiagnosticEntry option =
        match decision.Outcome with
        | UniqueIndexOutcome.EnforceUnique _ ->
            None
        | UniqueIndexOutcome.DoNotEnforce reason ->
            let code, message =
                match reason with
                | UniqueIndexKeepReason.PolicyDisabled ->
                    "tightening.uniqueIndex.policyDisabled",
                    "Unique index was not enforced. Enable policy support before enforcement can proceed."
                | UniqueIndexKeepReason.DataHasDuplicates ->
                    "tightening.uniqueIndex.duplicates",
                    "Unique index was not enforced. Resolve duplicate values before enforcement can proceed."
                | UniqueIndexKeepReason.EvidenceMissing ->
                    "tightening.uniqueIndex.evidenceMissing",
                    "Unique index was not enforced. Collect profiling evidence before enforcement can proceed."
                | UniqueIndexKeepReason.NoCandidateProfiled ->
                    "tightening.uniqueIndex.noCandidate",
                    "Unique index was not enforced. No profile candidate exists; collect profiling evidence before enforcement can proceed."
            Some {
                Source   = passName
                Severity = DiagnosticSeverity.Warning
                Code     = code
                Message  = message
                SsKey    = Some decision.IndexKey
                Metadata =
                    // Typed Outcome is structurally accessible via
                    // DecisionSet; metadata carries only the
                    // genuinely-string-typed intervention-id.
                    Map.ofList [
                        "interventionId", decision.InterventionId
                    ]
            }

    /// Run the UniqueIndexPass.
    ///
    /// **Observable identity on empty policy.** When no `UniqueIndex`
    /// interventions are registered, the result is the empty decision
    /// set with an empty trail and an empty diagnostic stream. No work
    /// is done; no events are emitted; no diagnostics are emitted; the
    /// catalog is not consulted. V2's strict default holds for the
    /// per-index granularity exactly as it does for the per-attribute
    /// granularity.
    ///
    /// **Decision composition.** When interventions are registered, the
    /// pass emits one `UniqueIndexDecision` per (index × intervention)
    /// pair, plus one `Annotated` lineage event per decision, plus one
    /// `Warning` `DiagnosticEntry` per `DoNotEnforce` decision (the
    /// V2-shaped equivalent of V1's `OpportunityBuilder.TryCreate`).
    /// Iteration order is deterministic: kinds by `SsKey`, indexes by
    /// `SsKey`, interventions by registration order. Diagnostic entries
    /// are emitted in the same order as the decisions that produced
    /// them (chronological per A24-equivalent for the dual writer).
    ///
    /// **Pass return-type codification (`DECISIONS 2026-05-13` —
    /// pass return-type codification).** `Lineage<Diagnostics<...>>`
    /// names the production: this pass produces decisions plus
    /// observer-relevant diagnostics, and the type signature names
    /// what the pass produces.
    let run (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<Diagnostics<UniqueIndexDecisionSet>> =
        use _ = Bench.scope "passes.uniqueIndex"
        let fanOutConfig : Composition.FanOutConfig<Kind * Index, _, _, _> = {
            InterventionFilter = TighteningPolicy.uniqueIndexInterventions
            SortedContexts     = sortedIndexes
            Evaluate           = fun id cfg (kind, index) prof ->
                UniqueIndexRules.evaluate id cfg kind index prof
            EmptyDecisionSet   = UniqueIndexRules.emptyDecisionSet
            WrapDecisions      = fun decisions -> { Decisions = decisions }
            BuildEvent         = decisionEvent
        }
        let lineage = Composition.fanOut fanOutConfig catalog policy profile
        let entries = lineage.Value.Decisions |> List.choose opportunityEntry
        lineage
        |> LineageDiagnostics.ofLineage
        |> LineageDiagnostics.tellDiagnostics entries

    /// Convenience accessor for tests and consumers that only care
    /// about the decision set (not the diagnostic stream). Domain-named
    /// shortcut for `LineageDiagnostics.payload`. Pattern: prefer
    /// `LineageDiagnostics.entries` when diagnostics matter,
    /// `decisionsOf` when only the decisions matter, and
    /// `LineageDiagnostics.payload` when no domain shortcut is
    /// available.
    let decisionsOf (result: Lineage<Diagnostics<UniqueIndexDecisionSet>>) : UniqueIndexDecisionSet =
        LineageDiagnostics.payload result
