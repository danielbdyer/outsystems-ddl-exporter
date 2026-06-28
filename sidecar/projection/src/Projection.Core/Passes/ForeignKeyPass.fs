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
    /// v3 — iteration domain narrows to deployable references
    ///       (DECISIONS 2026-06-12 — reconciliation slice 1):
    ///       symmetric-closure inverses are logical-only edges and
    ///       never enter FK decisions, so the spurious per-inverse
    ///       `evidenceMissing` warnings (and the accidental-DropFk
    ///       suppression they fed) disappear.
    [<Literal>]
    let version : int = 3

    [<Literal>]
    let private passName : string = "foreignKey"

    /// One lineage event per decision. `Annotated` because the pass
    /// produces a decision (a real transformation in the audit sense)
    /// rather than observing without changing — same convention as
    /// `NullabilityPass` and `UniqueIndexPass`. Chapter-3.6 slice-β
    /// widened the payload to the typed `AnnotationDetail.
    /// ForeignKeyDecision` variant.
    /// Pillar 9 (chapter A.4.7 slice α): foreign-key enforcement
    /// strengthens FK invariants beyond source evidence per
    /// operator-supplied Tightening policy. Operator intent on the
    /// Tightening axis. Lands as registered overlay.
    let private classification : Classification = OperatorIntent Tightening

    let private decisionEvent (decision: ForeignKeyDecision) : LineageEvent =
        LineageEvent.forPass passName version classification decision.ReferenceKey
            (Annotated (ForeignKeyDecision (decision.InterventionId, decision.Outcome)))

    /// Sort the iteration source deterministically — kinds by `SsKey`,
    /// references by `SsKey` within each kind. Interventions are taken
    /// in registration order (the caller chose the order; the pass
    /// preserves it). The shape mirrors `sortedAttributes` /
    /// `sortedIndexes`; only the inner field changes.
    /// Deployable references only (v3): symmetric-closure inverses are
    /// navigation/ordering edges and never enter the FK decision
    /// domain — neither as decisions nor as per-reference diagnostics.
    let private sortedReferences (catalog: Catalog) : (Kind * Reference) list =
        Catalog.kindContexts
            (fun k -> k.References |> List.filter Reference.isDeployable)
            (fun r -> r.SsKey)
            catalog

    /// H-024 — enrich `baseMetadata` with FK cardinality evidence when
    /// present. `ForeignKeyCardinality.ChildCountDistribution.Moments.Mean`
    /// is the average number of child rows per parent — a proxy for
    /// relationship density. High mean: the FK is heavily used (dense
    /// relationship); low mean: few children per parent (sparse). Surfaces
    /// as `"meanChildCount"` in Metadata so downstream dashboard consumers
    /// can distinguish these regimes without re-running the profiler.
    let private cardinalityMetadata
        (referenceKey: SsKey)
        (profile: Profile)
        (baseMetadata: Map<string, string>)
        : Map<string, string> =
        match Profile.tryFindForeignKeyCardinality referenceKey profile with
        | None -> baseMetadata
        | Some card ->
            match card.ChildCountDistribution.Moments with
            | None -> baseMetadata
            | Some moments ->
                baseMetadata
                |> Map.add "meanChildCount"
                       (moments.Mean.ToString("G4", System.Globalization.CultureInfo.InvariantCulture))

    /// H-024 — when FK cardinality evidence shows a dense relationship
    /// (`meanChildCount >= 1.0`) but policy has disabled creation,
    /// suggest enabling `enableCreation` via a config edit. The operator
    /// may have disabled creation during initial rollout; cardinality
    /// evidence that the FK is actively used is a signal to re-enable.
    let private cardinalitySuggestedConfig
        (interventionId: string)
        (referenceKey: SsKey)
        (profile: Profile)
        : SuggestedConfig option =
        match Profile.tryFindForeignKeyCardinality referenceKey profile with
        | None -> None
        | Some card ->
            match card.ChildCountDistribution.Moments with
            | None -> None
            | Some moments when moments.Mean >= 1.0m ->
                Some {
                    Path  = sprintf "$.tightening.interventions[?(@.id==\"%s\")].enableCreation" interventionId
                    Value = "true"
                    Note  = Some (sprintf "FK cardinality evidence shows %.4G average child rows per parent; relationship appears active. Re-enable to enforce the constraint." moments.Mean)
                }
            | Some _ -> None

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
    let private opportunityEntry (profile: Profile) (decision: ForeignKeyDecision) : DiagnosticEntry option =
        let baseMetadata =
            Map.ofList [ "interventionId", decision.InterventionId ]
            |> cardinalityMetadata decision.ReferenceKey profile
        let mkEntry severity code message suggestedConfig =
            { Source          = passName
              Severity        = severity
              Code            = code
              Message         = message
              SsKey           = Some decision.ReferenceKey
              Metadata        = baseMetadata
              SuggestedConfig = suggestedConfig }

        match decision.Outcome with
        | ForeignKeyOutcome.EnforceConstraint DatabaseConstraintPresent ->
            None
        | ForeignKeyOutcome.EnforceConstraint (NoEvidenceObstacle _) ->
            None
        | ForeignKeyOutcome.EnforceConstraint (ScriptWithNoCheck orphanCount) ->
            // Ok-with-caveat: V2's NoCheck-mode workaround.
            Some (mkEntry
                    DiagnosticSeverity.Warning
                    "tightening.foreignKey.scriptWithNoCheck"
                    (sprintf
                        "Foreign-key constraint scripted with NOCHECK because %d orphan row(s) were observed and operator policy allows it. Row-validation is deferred; remediate orphan rows before re-enabling enforcement."
                        orphanCount)
                    None)
        | ForeignKeyOutcome.DoNotEnforce PolicyDisabled ->
            // H-024: if cardinality shows active use, suggest re-enabling.
            Some (mkEntry
                    DiagnosticSeverity.Warning
                    "tightening.foreignKey.policyDisabled"
                    (Message.foreignKeyNotCreated
                        "Enable policy support before enforcement can proceed.")
                    (cardinalitySuggestedConfig decision.InterventionId decision.ReferenceKey profile))
        | ForeignKeyOutcome.DoNotEnforce (DataHasOrphans orphanCount) ->
            Some (mkEntry
                    DiagnosticSeverity.Warning
                    "tightening.foreignKey.dataHasOrphans"
                    (Message.foreignKeyNotCreated
                        (sprintf
                            "Profile observed %d orphan row(s); remediate the data or enable AllowNoCheckCreation before enforcement can proceed."
                            orphanCount))
                    None)
        | ForeignKeyOutcome.DoNotEnforce CrossSchemaBlocked ->
            Some (mkEntry
                    DiagnosticSeverity.Warning
                    "tightening.foreignKey.crossSchemaBlocked"
                    (Message.foreignKeyNotCreated
                        "The reference crosses schema boundaries and AllowCrossSchema is disabled.")
                    None)
        | ForeignKeyOutcome.DoNotEnforce CrossCatalogBlocked ->
            // Reserved DU variant; unreachable from V2 fixtures
            // today (V2's IR has no Catalog field on Reference).
            Some (mkEntry
                    DiagnosticSeverity.Warning
                    "tightening.foreignKey.crossCatalogBlocked"
                    (Message.foreignKeyNotCreated
                        "The reference crosses catalog boundaries and AllowCrossCatalog is disabled.")
                    None)
        | ForeignKeyOutcome.DoNotEnforce DeleteRuleIgnored ->
            // Currently unreachable from V2 fixtures (see V1↔V2 mapping
            // note in the session-13 Skip stub for this contract).
            Some (mkEntry
                    DiagnosticSeverity.Warning
                    "tightening.foreignKey.deleteRuleIgnored"
                    (Message.foreignKeyNotCreated
                        "The reference's delete rule resolved to Ignore.")
                    None)
        | ForeignKeyOutcome.DoNotEnforce EvidenceMissing ->
            Some (mkEntry
                    DiagnosticSeverity.Warning
                    "tightening.foreignKey.evidenceMissing"
                    (Message.foreignKeyNotCreated
                        "Profile probe did not succeed reliably; collect evidence before enforcement can proceed.")
                    None)
        | ForeignKeyOutcome.DoNotEnforce MissingTarget ->
            // V2's audit dividend (session-8 refinement 3): V1
            // silently skipped references to missing targets; V2
            // surfaces the absence explicitly.
            Some (mkEntry
                    DiagnosticSeverity.Warning
                    "tightening.foreignKey.missingTarget"
                    (Message.foreignKeyNotCreated
                        "The reference's target kind is absent from the catalog.")
                    None)

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
    // Chapter A.4.7' slice η: `let run` is private; canonical surface is `ForeignKeyPass.registered.Run`
    let private run (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<Diagnostics<ForeignKeyDecisionSet>> =
        use _ = Bench.scope "passes.foreignKey"
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
        // recon #12 — `fanOut` + the decision→diagnostic tail, now one primitive.
        // Per-reference distribution surfaces under `pass.fk.reference` (one Bench
        // sample per decision-evaluation iteration; decision-count = references ×
        // interventions; heterogeneous-emission cost varies across outcomes).
        Composition.fanOutWithDiagnostics
            fanOutConfig "pass.fk.reference"
            (fun (ds: ForeignKeyDecisionSet) -> ds.Decisions) (opportunityEntry profile)
            catalog policy profile

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

    /// Chapter A.4.7 slice γ — factory. Captures the operator-supplied
    /// `Policy` + `Profile` in closure. Single `OperatorIntent
    /// Tightening` site — the Tightening policy enforces FK
    /// invariants beyond source evidence per operator opinion.
    let registered (policy: Policy) (profile: Profile) : RegisteredTransform<Catalog, ForeignKeyDecisionSet> =
        { Name = passName
          Domain = Schema
          StageBinding = Pass
          Sites =
            [ { SiteName = "tightenForeignKey"
                Classification = classification
                Rationale = "Enforce foreign-key invariants per operator-supplied Tightening policy. Profile evidence (orphan-row probes) drives the empirical decisions; lands as Tightening-axis overlay." } ]
          Run = fun c -> run c policy profile
          Status = Active }
