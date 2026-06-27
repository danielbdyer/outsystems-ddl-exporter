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
    /// Pillar 9 (chapter A.4.7 slice α): nullability tightening
    /// strengthens NOT NULL invariants beyond source evidence per
    /// operator-supplied Tightening policy (Cautious / Aggressive /
    /// Disabled). Operator intent on the Tightening axis. Lands as
    /// registered overlay.
    let private classification : Classification = OperatorIntent Tightening

    let private decisionEvent (decision: NullabilityDecision) : LineageEvent =
        LineageEvent.forPass passName version classification decision.AttributeKey
            (Annotated (NullabilityDecision (decision.InterventionId, decision.Outcome)))

    /// Sort the iteration source deterministically — kinds by `SsKey`,
    /// attributes by `SsKey` within each kind. Interventions are taken
    /// in registration order (the caller chose the order; the pass
    /// preserves it).
    let private sortedAttributes (catalog: Catalog) : (Kind * Attribute) list =
        Catalog.kindContexts (fun k -> k.Attributes) (fun a -> a.SsKey) catalog

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
    /// H-029 — derive `coefficientOfVariation` from the attribute's
    /// numeric distribution when present. CV (σ/μ) is the dimensionless
    /// ratio of spread to central tendency; a high CV signals that the
    /// column's non-null values vary widely (sparse / anomalous nulls),
    /// whereas a low CV indicates uniform values (structurally-intended
    /// nullable column). Surfaces in Metadata as `"cv"` so operators
    /// and downstream dashboard consumers can distinguish the two
    /// regimes without re-running the profiler.
    let private cvMetadata
        (attributeKey: SsKey)
        (profile: Profile)
        (baseMetadata: Map<string, string>)
        : Map<string, string> =
        match Profile.tryFindNumeric attributeKey profile with
        | None -> baseMetadata
        | Some dist ->
            match NumericDistribution.coefficientOfVariation dist with
            | None    -> baseMetadata
            | Some cv ->
                baseMetadata
                |> Map.add "cv" (cv.ToString("G4", System.Globalization.CultureInfo.InvariantCulture))

    let private opportunityEntry (profile: Profile) (decision: NullabilityDecision) : DiagnosticEntry option =
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
                Severity = DiagnosticSeverity.Warning
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
                // duplicate of structured data). The `cv` entry is
                // profile-derived (H-029) — not a duplicate of the
                // decision's typed outcome; it is new evidence about
                // the column's value distribution shape.
                Metadata =
                    Map.ofList [ "interventionId", decision.InterventionId ]
                    |> cvMetadata decision.AttributeKey profile
                SuggestedConfig = None
            }
        | NullabilityOutcome.RequireOperatorApproval (MandatoryButHasNullsBeyondBudget (nulls, rows, budget)) ->
            Some {
                Source   = passName
                Severity = DiagnosticSeverity.Warning
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
                // duplicate of structured data). The `cv` entry is
                // profile-derived (H-029) — see cvMetadata above.
                Metadata =
                    Map.ofList [ "interventionId", decision.InterventionId ]
                    |> cvMetadata decision.AttributeKey profile
                SuggestedConfig =
                    // Ceiling to 4 decimal places: tightest budget that
                    // makes observed <= allowed, turning the outcome to
                    // EnforceNotNull(LogicalMandatoryWithinBudget).
                    // To keep the column nullable instead, the operator
                    // should set allowMandatoryRelaxation: true.
                    let frac =
                        if rows = 0L then 1.0m
                        else decimal nulls / decimal rows
                    let suggested =
                        System.Math.Ceiling(frac * 10000m) / 10000m
                    Some {
                        Path  = sprintf "$.tightening.interventions[?(@.id==\"%s\")].nullBudget" decision.InterventionId
                        Value = suggested.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
                        Note  = Some "Raises nullBudget to the observed null fraction; tightening then proceeds under LogicalMandatoryWithinBudget. To keep the column nullable, set allowMandatoryRelaxation: true instead."
                    }
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
    // Chapter A.4.7' slice η: `let run` is private; canonical surface is `NullabilityPass.registered.Run`
    let private run (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<Diagnostics<NullabilityDecisionSet>> =
        use _ = Bench.scope "passes.nullability"
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
        // Per-attribute distribution surfaces under
        // `pass.nullability.attribute` — one Bench sample per
        // decision opportunity-evaluation iteration (decision-count
        // = attributes × interventions; the per-iteration tail
        // bounds the diagnostic-mapping cost).
        let entries =
            lineage.Value.Decisions
            |> Bench.iterMap "pass.nullability.attribute" (opportunityEntry profile)
            |> List.choose id
        lineageDiagnostics {
            let! value = lineage
            do! LineageDiagnostics.writeDiagnostics entries
            return value
        }

    /// Convenience accessor for tests and consumers that only care
    /// about the decision set (not the diagnostic stream). Domain-
    /// named shortcut for `LineageDiagnostics.payload`. Pattern:
    /// prefer `LineageDiagnostics.entries` when diagnostics matter,
    /// `decisionsOf` when only the decisions matter, and
    /// `LineageDiagnostics.payload` when no domain shortcut is
    /// available. Mirrors `UniqueIndexPass.decisionsOf`.
    let decisionsOf (result: Lineage<Diagnostics<NullabilityDecisionSet>>) : NullabilityDecisionSet =
        LineageDiagnostics.payload result

    /// Chapter A.4.7 slice γ — factory. Captures the operator-supplied
    /// `Policy` + `Profile` in a closure; the Run signature reduces to
    /// `Catalog -> Lineage<Diagnostics<NullabilityDecisionSet>>`. Single
    /// `OperatorIntent Tightening` site — the Tightening policy
    /// strengthens NOT NULL invariants beyond source evidence per
    /// operator opinion.
    let registered (policy: Policy) (profile: Profile) : RegisteredTransform<Catalog, NullabilityDecisionSet> =
        { Name = passName
          Domain = Data
          StageBinding = Pass
          Sites =
            [ { SiteName = "tightenNullability"
                Classification = classification
                Rationale = "Strengthen attribute NOT NULL invariants beyond source evidence per operator-supplied Tightening policy (Cautious / Aggressive / Disabled). Operator-supplied policy + profile evidence drive the decisions; lands as Tightening-axis overlay." } ]
          Run = fun c -> run c policy profile
          Status = Active }
