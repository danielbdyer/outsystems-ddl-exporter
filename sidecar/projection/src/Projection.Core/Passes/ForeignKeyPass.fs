namespace Projection.Core.Passes

// LINT-ALLOW-FILE: pass-driver `%A` Outcome / KeepReason pretty-
// print is the F# closed-DU stringification surface for typed
// diagnostic strings. The audit (`Codebase determinism +
// non-built-in audit`, 2026-05-09 Lens-1 Tier-4 / Lens-2 acceptance)
// recommended typed `Outcome.toDiagnosticString` per DU as the
// follow-on; until that lands, `%A` is the F#-native pretty-
// printer (no typed alternative built into BCL or this codebase).
// Tracked at `HANDOFF.md` deferred-but-might-fire as "typed
// Outcome.toDiagnosticString".

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
    /// - the diagnostic-entry shape, code namespace, or message
    ///   templates change (consumers grep on Code; bumping the
    ///   version makes a behavior-preserving rename detectable)
    ///
    /// v1 — Lineage<ForeignKeyDecisionSet>; no diagnostic emission.
    /// v2 — Lineage<Diagnostics<ForeignKeyDecisionSet>>; emits one
    ///       Warning DiagnosticEntry per DoNotEnforce decision AND
    ///       per EnforceConstraint(ScriptWithNoCheck(_)) decision
    ///       (the success-with-caveat case where orphans were
    ///       observed but the operator allowed NoCheck creation).
    ///       Heterogeneous emission shape is the third real test of
    ///       the writer's codification (DECISIONS 2026-05-13 — Pass
    ///       return-type codification): the prior two consumers
    ///       (UniqueIndex, Nullability) emitted only on
    ///       failure-side variants of their outcome DUs;
    ///       ForeignKey emits on both failure-side keep-reasons
    ///       and one success-side caveat variant within a single
    ///       pass. Whether the writer absorbs the heterogeneity
    ///       cleanly is the substantive session-16 question.
    [<Literal>]
    let version : int = 2

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

    /// Map a `ForeignKeyDecision` to an opportunity-style diagnostic
    /// entry, or `None` if the decision is structurally clean (no
    /// observer-relevant caveat).
    ///
    /// **Heterogeneous emission shape (the session-16 test).** Unlike
    /// `UniqueIndexPass.opportunityEntry` and
    /// `NullabilityPass.opportunityEntry`, which emit only on
    /// failure-side variants (`DoNotEnforce` keep-reasons, with one
    /// audit-worthy `KeepNullable(RelaxedUnderEvidence)`), ForeignKey
    /// emits on both:
    ///
    ///   - **All `DoNotEnforce` keep-reasons** (mirroring the prior
    ///     two consumers' shape). V1's `OpportunityBuilder` exists
    ///     only for UniqueIndex; V2's keep-reason emission for
    ///     ForeignKey is V2-growth — the audit chain gains reasons
    ///     V1 lacked because V1 silently skipped (per session-8
    ///     refinement 3, "Total decisions, named skips").
    ///
    ///   - **One success-with-caveat** —
    ///     `EnforceConstraint(ScriptWithNoCheck(orphanCount))`. The
    ///     constraint *is* created; the diagnostic notes that
    ///     orphans were observed but tolerated under NoCheck. This
    ///     is the heterogeneous variant: a successful decision that
    ///     warrants observer attention.
    ///
    /// Code namespace: `tightening.foreignKey.<reason>`.
    let private opportunityEntry (decision: ForeignKeyDecision) : DiagnosticEntry option =
        let mkEntry severity code message =
            { Source   = passName
              Severity = severity
              Code     = code
              Message  = message
              SsKey    = Some decision.ReferenceKey
              Metadata =
                  Map.ofList [
                      "interventionId", decision.InterventionId
                      "outcome",        sprintf "%A" decision.Outcome
                  ] }

        match decision.Outcome with
        | ForeignKeyOutcome.EnforceConstraint DatabaseConstraintPresent ->
            None
        | ForeignKeyOutcome.EnforceConstraint (NoEvidenceObstacle _) ->
            None
        | ForeignKeyOutcome.EnforceConstraint (ScriptWithNoCheck orphanCount) ->
            // Success-with-caveat: V2's NoCheck-mode workaround.
            // V1's `(CreateConstraint=true, ScriptWithNoCheck=true)`
            // collapses to this evidence variant; the audit-trail
            // concern V1 surfaced via rationale strings is V2's
            // diagnostic emission.
            Some (mkEntry
                    Warning
                    "tightening.foreignKey.scriptWithNoCheck"
                    (sprintf
                        "Foreign-key constraint scripted with NOCHECK because %d orphan row(s) were observed and operator policy allows it. Row-validation is deferred; remediate orphan rows before re-enabling enforcement."
                        orphanCount))
        | ForeignKeyOutcome.DoNotEnforce PolicyDisabled ->
            Some (mkEntry
                    Warning
                    "tightening.foreignKey.policyDisabled"
                    "Foreign-key constraint was not created. Enable policy support before enforcement can proceed.")
        | ForeignKeyOutcome.DoNotEnforce (DataHasOrphans orphanCount) ->
            Some (mkEntry
                    Warning
                    "tightening.foreignKey.dataHasOrphans"
                    (sprintf
                        "Foreign-key constraint was not created. Profile observed %d orphan row(s); remediate the data or enable AllowNoCheckCreation before enforcement can proceed."
                        orphanCount))
        | ForeignKeyOutcome.DoNotEnforce CrossSchemaBlocked ->
            Some (mkEntry
                    Warning
                    "tightening.foreignKey.crossSchemaBlocked"
                    "Foreign-key constraint was not created. The reference crosses schema boundaries and AllowCrossSchema is disabled.")
        | ForeignKeyOutcome.DoNotEnforce CrossCatalogBlocked ->
            // Reserved DU variant; unreachable from V2 fixtures
            // today (V2's IR has no Catalog field on Reference).
            // Pattern-match completeness keeps the shape ready for
            // the IR refinement (ADMIRE.md 2026-05-11).
            Some (mkEntry
                    Warning
                    "tightening.foreignKey.crossCatalogBlocked"
                    "Foreign-key constraint was not created. The reference crosses catalog boundaries and AllowCrossCatalog is disabled.")
        | ForeignKeyOutcome.DoNotEnforce DeleteRuleIgnored ->
            // Currently unreachable from V2 fixtures (V2's
            // Reference.OnDelete DU has no Ignore variant; V1's
            // "Ignore" maps to V2's NoAction semantically).
            // Reserved-but-emit-when-reached so the audit chain is
            // ready when the V2 catalog reader synthesizes a
            // V1-equivalent representation. See session 13's Skip
            // stub on this contract for the V1↔V2 mapping note.
            Some (mkEntry
                    Warning
                    "tightening.foreignKey.deleteRuleIgnored"
                    "Foreign-key constraint was not created. The reference's delete rule resolved to Ignore.")
        | ForeignKeyOutcome.DoNotEnforce EvidenceMissing ->
            Some (mkEntry
                    Warning
                    "tightening.foreignKey.evidenceMissing"
                    "Foreign-key constraint was not created. Profile probe did not succeed reliably; collect evidence before enforcement can proceed.")
        | ForeignKeyOutcome.DoNotEnforce MissingTarget ->
            // V2's audit dividend (session-8 refinement 3): V1
            // silently skipped references to missing targets; V2
            // surfaces the absence explicitly.
            Some (mkEntry
                    Warning
                    "tightening.foreignKey.missingTarget"
                    "Foreign-key constraint was not created. The reference's target kind is absent from the catalog.")

    /// Run the ForeignKeyPass.
    ///
    /// **Observable identity on empty policy.** When no `ForeignKey`
    /// interventions are registered, the result is the empty decision
    /// set with an empty trail and an empty diagnostic stream. No
    /// work is done; no events are emitted; no diagnostics are
    /// emitted; the catalog is not consulted. V2's strict default
    /// holds for the per-reference granularity exactly as it does
    /// for per-attribute and per-index.
    ///
    /// **Decision composition.** When interventions are registered,
    /// the pass emits one `ForeignKeyDecision` per (reference ×
    /// intervention) pair, plus one `Annotated` lineage event per
    /// decision, plus one `Warning` `DiagnosticEntry` per outcome
    /// variant whose semantics warrant observer attention (every
    /// `DoNotEnforce` keep-reason and the
    /// `EnforceConstraint(ScriptWithNoCheck)` success-with-caveat
    /// variant). Iteration order is deterministic: kinds by
    /// `SsKey`, references by `SsKey`, interventions by
    /// registration order. Diagnostic entries follow decision order
    /// (chronological per A24-equivalent for the dual writer).
    ///
    /// **Pass return-type codification (`DECISIONS 2026-05-13`).**
    /// `Lineage<Diagnostics<...>>` names the production: this pass
    /// produces decisions plus observer-relevant diagnostics, and
    /// the type signature names what the pass produces. Same shape
    /// as `UniqueIndexPass.run` and `NullabilityPass.run`; this is
    /// the codification's third real test.
    let run (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<Diagnostics<ForeignKeyDecisionSet>> =
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
    /// available. Mirrors `UniqueIndexPass.decisionsOf` and
    /// `NullabilityPass.decisionsOf`.
    let decisionsOf (result: Lineage<Diagnostics<ForeignKeyDecisionSet>>) : ForeignKeyDecisionSet =
        LineageDiagnostics.payload result
