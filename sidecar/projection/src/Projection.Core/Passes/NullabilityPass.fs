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
    /// - the diagnostic-entry shape, code namespace, or message
    ///   templates change (consumers grep on Code; bumping the
    ///   version makes a behavior-preserving rename detectable)
    ///
    /// v1 — Lineage<NullabilityDecisionSet>; no diagnostic emission.
    /// v2 — Lineage<Diagnostics<NullabilityDecisionSet>>; emits one
    ///       Warning DiagnosticEntry per RequireOperatorApproval
    ///       decision and per KeepNullable(RelaxedUnderEvidence).
    ///       Activates V1 NullabilityEvaluator's opportunity-stream
    ///       contract (V1 #6/#7 from V1NullabilityParityTests) per
    ///       DECISIONS 2026-05-13 (pass return-type codification).
    [<Literal>]
    let version : int = 2

    [<Literal>]
    let private passName : string = "nullability"

    /// One lineage event per decision. `Annotated` because the pass
    /// produces a decision (a real transformation in the audit sense)
    /// rather than observing without changing. Chapter-3.6 slice-β
    /// widened the payload from `Annotated (interventionId + " -> " +
    /// outcomeLabel)` (string) to `Annotated (NullabilityDecision
    /// (interventionId, outcome))` (typed) — audit consumers
    /// pattern-match the structurally-preserved outcome directly,
    /// rather than substring-parsing a built name.
    let private decisionEvent (decision: NullabilityDecision) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = decision.AttributeKey
          TransformKind =
              Annotated (NullabilityDecision (decision.InterventionId, decision.Outcome)) }

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

    /// V1's `NullabilityEvaluator.Analyze` produces an opportunity
    /// record for every column whose decision needs operator
    /// remediation — V1 #6 (mandatory column with nulls beyond
    /// budget) is the canonical case; V1 #7 (non-mandatory column
    /// with no nulls) is the canonical *non*-case. V2's three-way
    /// outcome maps cleanly: `RequireOperatorApproval` is V1's
    /// remediation-opportunity case; `KeepNullable(RelaxedUnderEvidence)`
    /// is the audit-worthy case where the operator's intervention
    /// allowed relaxation but the evidence is informative; the other
    /// outcomes are not opportunities.
    ///
    /// Diagnostic mapping:
    ///   - `EnforceNotNull(_)`                          → None
    ///   - `KeepNullable(OperatorOverride)`             → None (operator chose this; not an opportunity)
    ///   - `KeepNullable(NoTighteningSignal)`           → None (V1 #7 — column is intentionally nullable)
    ///   - `KeepNullable(RelaxedUnderEvidence)`         → Warning (audit-worthy: relaxation chosen under evidence)
    ///   - `RequireOperatorApproval(_)`                 → Warning (V1 #6 — the canonical remediation opportunity)
    ///
    /// Code namespace: `tightening.nullability.<reason>`. Top-prefix
    /// `tightening.*` routes to consumers caring about policy-driven
    /// decisions; sub-prefix `nullability.*` distinguishes from
    /// `uniqueIndex.*` and `foreignKey.*` (when those passes activate
    /// their own diagnostic emission per the same codification).
    let private opportunityEntry (decision: NullabilityDecision) : DiagnosticEntry option =
        match decision.Outcome with
        | NullabilityOutcome.EnforceNotNull _ ->
            None
        | NullabilityOutcome.KeepNullable OperatorOverride ->
            None
        | NullabilityOutcome.KeepNullable NoTighteningSignal ->
            None
        | NullabilityOutcome.KeepNullable (RelaxedUnderEvidence (nulls, rows, budget)) ->
            Some {
                Source   = passName
                Severity = Warning
                Code     = "tightening.nullability.relaxedUnderEvidence"
                Message  =
                    sprintf
                        "Column was kept nullable under operator-allowed relaxation (%d/%d nulls observed; budget %s). The relaxation is policy-permitted; the evidence is informative."
                        nulls rows (budget.ToString(System.Globalization.CultureInfo.InvariantCulture))
                SsKey    = Some decision.AttributeKey
                // Per the data-structure-oriented discipline
                // (chapter 3.5 sidebar): the typed `Outcome` is
                // already structurally accessible via
                // `decisionsOf result |> .Decisions`; carrying a
                // string-encoded duplicate in Metadata violates
                // the discipline. Metadata carries only the
                // intervention-id (genuinely string-typed; not a
                // duplicate of structured data).
                Metadata =
                    Map.ofList [
                        "interventionId", decision.InterventionId
                    ]
            }
        | NullabilityOutcome.RequireOperatorApproval (MandatoryButHasNullsBeyondBudget (nulls, rows, budget)) ->
            Some {
                Source   = passName
                Severity = Warning
                Code     = "tightening.nullability.requireOperatorApproval"
                Message  =
                    sprintf
                        "Mandatory column has %d/%d nulls observed (budget %s). Tightening lifted to operator approval — remediate data or update the policy before applying NOT NULL."
                        nulls rows (budget.ToString(System.Globalization.CultureInfo.InvariantCulture))
                SsKey    = Some decision.AttributeKey
                // Per the data-structure-oriented discipline
                // (chapter 3.5 sidebar): the typed `Outcome` is
                // already structurally accessible via
                // `decisionsOf result |> .Decisions`; carrying a
                // string-encoded duplicate in Metadata violates
                // the discipline. Metadata carries only the
                // intervention-id (genuinely string-typed; not a
                // duplicate of structured data).
                Metadata =
                    Map.ofList [
                        "interventionId", decision.InterventionId
                    ]
            }

    /// Run the NullabilityPass via the canonical `Composition.fanOut`
    /// primitive (DECISIONS 2026-05-13 — composition vocabulary
    /// codification). The pass driver is now a thin wrapper that
    /// constructs the `FanOutConfig` and delegates iteration /
    /// accumulation / lineage discipline to the canonical primitive,
    /// then layers the diagnostic stream over the produced decision
    /// set per the pass return-type codification (DECISIONS
    /// 2026-05-13).
    ///
    /// **Observable identity on empty policy.** Preserved by
    /// `Composition.fanOut`; when no Nullability interventions are
    /// registered, the catalog is not consulted, no decisions are
    /// produced, no lineage events are emitted, no diagnostics are
    /// emitted.
    ///
    /// **Decision composition.** Preserved: one decision per
    /// (attribute × intervention); one `Annotated` event per
    /// decision; deterministic iteration (kinds by `SsKey`,
    /// attributes by `SsKey`, interventions by registration order).
    /// Plus one `Warning` `DiagnosticEntry` per `RequireOperatorApproval`
    /// or `KeepNullable(RelaxedUnderEvidence)` decision, in
    /// decision order (chronological per A24-equivalent for the dual
    /// writer).
    ///
    /// **Pass return-type codification.** `Lineage<Diagnostics<...>>`
    /// names the production: this pass produces decisions plus
    /// observer-relevant diagnostics, and the type signature names
    /// what the pass produces. Same shape as `UniqueIndexPass.run`
    /// (session 14 commit 5); session 15 commit 2 applies the
    /// codification to its second pass.
    let run (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<Diagnostics<NullabilityDecisionSet>> =
        let fanOutConfig : Composition.FanOutConfig<Kind * Attribute, _, _, _> = {
            InterventionFilter = TighteningPolicy.nullabilityInterventions
            SortedContexts     = sortedAttributes
            Evaluate           = fun id cfg (_kind, attr) prof ->
                NullabilityRules.evaluate id cfg attr prof
            EmptyDecisionSet   = NullabilityRules.emptyDecisionSet
            WrapDecisions      = fun decisions -> { Decisions = decisions }
            BuildEvent         = decisionEvent
        }
        let lineage = Composition.fanOut fanOutConfig catalog policy profile
        let entries = lineage.Value.Decisions |> List.choose opportunityEntry
        lineage
        |> LineageDiagnostics.ofLineage
        |> LineageDiagnostics.tellDiagnostics entries

    /// Convenience accessor for tests and consumers that only care
    /// about the decision set (not the diagnostic stream). Domain-
    /// named shortcut for `LineageDiagnostics.payload`. Pattern:
    /// prefer `LineageDiagnostics.entries` when diagnostics matter,
    /// `decisionsOf` when only the decisions matter, and
    /// `LineageDiagnostics.payload` when no domain shortcut is
    /// available. Mirrors `UniqueIndexPass.decisionsOf`.
    let decisionsOf (result: Lineage<Diagnostics<NullabilityDecisionSet>>) : NullabilityDecisionSet =
        LineageDiagnostics.payload result
