namespace Projection.Core

/// Why an FK constraint was elected for creation. Structured, not
/// stringly-typed — emitter consumers pattern-match on the rationale
/// to surface user-facing messages and audit reports. Mirrors
/// `NullabilityEvidence` and `UniqueIndexEvidence` in shape; the
/// strategy-layer codification (DECISIONS 2026-05-11) prescribes
/// this DU as the registered-intervention sub-pattern's signature.
type ForeignKeyEvidence =
    /// The database already enforces this FK constraint (V1's
    /// `HasDatabaseConstraint = true` path). Trusted regardless of
    /// profile evidence — the constraint exists; V2 records it.
    | DatabaseConstraintPresent
    /// Profile probe succeeded; no orphans observed; the FK is
    /// eligible under cross-schema gates and the
    /// `EnableCreation` toggle.
    | NoEvidenceObstacle of probeRowCount: int64
    /// V1's Cautious-mode workaround: orphans or Ignore-rule
    /// observed, but the caller has `AllowNoCheckCreation = true`
    /// (and `EnableCreation = true`). Constraint created with the
    /// NoCheck flag; row-validation deferred.
    ///
    /// Folding `ScriptWithNoCheck=true` into the EnforceConstraint
    /// evidence variant captures V1's
    /// `(CreateConstraint=true, ScriptWithNoCheck=true)` two-boolean
    /// shape without inflating the outcome to ternary — the binary
    /// outcome's "did we make a constraint?" question still has a
    /// binary answer; the evidence variant carries the modifier.
    | ScriptWithNoCheck of orphanCount: int64
    /// A RELAXATION-ONLY intervention (DECISIONS 2026-07-15, the estate
    /// A6 amendment) states no opinion for this reference: the declared
    /// shape emits untouched. Identity at emission — the decision lands
    /// in neither `DropFk` nor `NoCheckFk`.
    | DeclaredShapeCarried


/// Why an FK constraint stays un-enforced.
type ForeignKeyKeepReason =
    /// `EnableCreation = false`. The intervention's gate disables
    /// FK creation entirely. **No domain reasoning consulted** —
    /// this is the gate the caller chose; the algebra reports the
    /// gate.
    | PolicyDisabled
    /// Profile observed orphans and `AllowNoCheckCreation = false`.
    | DataHasOrphans of orphanCount: int64
    /// `AllowCrossSchema = false` and the FK crosses schema
    /// boundaries (source kind's schema ≠ target kind's schema).
    | CrossSchemaBlocked
    /// The FK crosses catalog (database) boundaries. **Currently
    /// unreachable** — V2's IR does not model catalog names; reserved
    /// as a DU variant pending the IR refinement (ADMIRE.md 2026-05-11).
    /// The inert `AllowCrossCatalog` config toggle was removed at WP-1d
    /// (DECISIONS 2026-07-16); when the IR grows a catalog field the gate
    /// — and its config knob — return as real, consulted values.
    | CrossCatalogBlocked
    /// Delete rule resolves to `Ignore`. **Currently unreachable** —
    /// V2's `Reference.OnDelete` is a closed DU with no `Ignore` variant
    /// and cannot be missing (`isIgnoreRule` is hardcoded `false`). The
    /// inert `TreatMissingDeleteRuleAsIgnore` config toggle was removed at
    /// WP-1d; the no-FK-for-Ignore semantics land with WP-1c.
    | DeleteRuleIgnored
    /// Profile probe did not succeed (FallbackTimeout / Cancelled /
    /// AmbiguousMapping); evidence is missing. V2's collapsed-mode
    /// strict default declines to enforce on missing evidence.
    | EvidenceMissing
    /// The FK's target kind is absent from the catalog. V1 silently
    /// skips; V2 reports the absence explicitly so the audit chain
    /// has a reason for the missing constraint.
    | MissingTarget
    /// The operator's explicit per-reference posture override
    /// (DECISIONS 2026-07-15, the estate A6 amendment — the interim
    /// untrack). Absolute, outranking even the source-backed-constraint
    /// carve-out: the posture deliberately targets relationships the
    /// agreed shape carries, and the reopen probe retires it at zero.
    | OperatorUntracked


/// The outcome of a single (reference, intervention) decision.
///
/// Binary, mirroring `UniqueIndexOutcome` — V1's
/// `(CreateConstraint, ScriptWithNoCheck)` two-boolean shape collapses
/// into a binary outcome with structured evidence (DECISIONS
/// 2026-05-09 — V2 picks based on what serves the algebra).
///
/// `RequireQualifiedAccess` because `EnforceConstraint` and
/// `DoNotEnforce` are intuitive names that may clash with other DUs
/// as the algebra grows.
[<RequireQualifiedAccess>]
type ForeignKeyOutcome =
    | EnforceConstraint of evidence: ForeignKeyEvidence
    | DoNotEnforce      of reason:   ForeignKeyKeepReason


/// One decision keyed to its reference and the intervention that
/// produced it. Mirrors `NullabilityDecision` and
/// `UniqueIndexDecision` in shape; the key is `ReferenceKey` because
/// FK decisions are per-reference (the third granularity, after
/// per-attribute and per-index).
type ForeignKeyDecision = {
    ReferenceKey   : SsKey
    Outcome        : ForeignKeyOutcome
    InterventionId : string
}


/// The aggregate output of one or more ForeignKey interventions
/// running over a catalog. Empty when no interventions are registered
/// (the observable-identity-on-empty-policy commitment, DECISIONS
/// 2026-05-09).
type ForeignKeyDecisionSet = {
    Decisions : ForeignKeyDecision list
}


/// Domain rules for foreign-key tightening — the V1
/// `ForeignKeyEvaluator` migration's domain layer per the
/// algebra/domain split (DECISIONS 2026-05-09 — Algebra/domain split
/// pattern). Pure functions of the IR fields; no I/O; no mutable
/// state.
///
/// The pure pass (`ForeignKeyPass`) walks the catalog's references
/// and iterates over registered ForeignKey interventions; per
/// (reference, intervention), this module's `evaluate` returns one
/// `ForeignKeyDecision`. The pass accumulates decisions; the rules
/// module decides each one.
///
/// **Strategy-layer codification (DECISIONS 2026-05-11).** This
/// module is the third instance of the registered-intervention
/// sub-pattern; the codification prescribes the same shape as
/// `NullabilityRules` and `UniqueIndexRules`:
///
///   - **Pure functions of IR fields.** No I/O, no mutable state.
///   - **Typed seam.** `evaluate` is the function the pass driver
///     calls into.
///   - **Structured rationale DUs** cover the decision space
///     exhaustively (the three DUs above: Evidence, KeepReason,
///     Outcome).
///   - **Module name advertises domain** — `ForeignKeyRules`,
///     same `<Domain>Rules` suffix as the two predecessors.
/// Typed structural display per ForeignKey DUs.
[<RequireQualifiedAccess>]
module ForeignKeyEvidence =
    let toStructured (e: ForeignKeyEvidence) : StructuredString =
        match e with
        | DatabaseConstraintPresent -> StructuredString.tag "DatabaseConstraintPresent"
        | NoEvidenceObstacle probeRowCount ->
            StructuredString.create "NoEvidenceObstacle"
                [ "probeRowCount", Inv.int64 probeRowCount ]
        | ScriptWithNoCheck orphanCount ->
            StructuredString.create "ScriptWithNoCheck"
                [ "orphanCount", Inv.int64 orphanCount ]
        | DeclaredShapeCarried -> StructuredString.tag "DeclaredShapeCarried"

    let toDiagnosticString (e: ForeignKeyEvidence) : string =
        toStructured e |> StructuredString.render

[<RequireQualifiedAccess>]
module ForeignKeyKeepReason =
    let toStructured (r: ForeignKeyKeepReason) : StructuredString =
        match r with
        | ForeignKeyKeepReason.PolicyDisabled -> StructuredString.tag "PolicyDisabled"
        | ForeignKeyKeepReason.DataHasOrphans orphanCount ->
            StructuredString.create "DataHasOrphans"
                [ "orphanCount", Inv.int64 orphanCount ]
        | ForeignKeyKeepReason.CrossSchemaBlocked -> StructuredString.tag "CrossSchemaBlocked"
        | ForeignKeyKeepReason.CrossCatalogBlocked -> StructuredString.tag "CrossCatalogBlocked"
        | ForeignKeyKeepReason.DeleteRuleIgnored -> StructuredString.tag "DeleteRuleIgnored"
        | ForeignKeyKeepReason.EvidenceMissing -> StructuredString.tag "EvidenceMissing"
        | ForeignKeyKeepReason.MissingTarget -> StructuredString.tag "MissingTarget"
        | ForeignKeyKeepReason.OperatorUntracked -> StructuredString.tag "OperatorUntracked"

    let toDiagnosticString (r: ForeignKeyKeepReason) : string =
        toStructured r |> StructuredString.render

[<RequireQualifiedAccess>]
module ForeignKeyOutcome =
    let toStructured (o: ForeignKeyOutcome) : StructuredString =
        match o with
        | ForeignKeyOutcome.EnforceConstraint e ->
            StructuredString.create "EnforceConstraint"
                [ "evidence", ForeignKeyEvidence.toDiagnosticString e ]
        | ForeignKeyOutcome.DoNotEnforce r ->
            StructuredString.create "DoNotEnforce"
                [ "reason", ForeignKeyKeepReason.toDiagnosticString r ]

    let toDiagnosticString (o: ForeignKeyOutcome) : string =
        toStructured o |> StructuredString.render


[<RequireQualifiedAccess>]
module ForeignKeyRules =

    /// Empty decision set — V2's strict default when no interventions
    /// are registered.
    let emptyDecisionSet : ForeignKeyDecisionSet = { Decisions = [] }

    /// V1's `IsIgnoreRule` predicate, in V2 form. V2's `OnDelete` DU has
    /// no `Ignore` variant (V1's "Ignore" is V2's `NoAction` semantically —
    /// the DB does nothing; the application enforces) and cannot be missing,
    /// so this predicate is unreachable from synthetic catalogs. The inert
    /// `TreatMissingDeleteRuleAsIgnore` config toggle it once consulted was
    /// removed at WP-1d (DECISIONS 2026-07-16); the no-FK-for-Ignore
    /// semantics arrive with the WP-1c posture. Returns `false` for every
    /// variant of V2's current `OnDelete` DU; kept as the named seam.
    let private isIgnoreRule (_action: ReferenceAction) : bool =
        false

    /// True iff the FK crosses schema boundaries. Reads the source
    /// kind's `Physical.Schema` and the target kind's
    /// `Physical.Schema`; treats them as case-insensitive
    /// equivalents (V1's `SchemaEquals`, ForeignKeyEvaluator.cs:231).
    let private crossesSchema (sourceKind: Kind) (targetKind: Kind) : bool =
        not (System.String.Equals(
                SchemaName.value sourceKind.Physical.Schema,
                SchemaName.value targetKind.Physical.Schema,
                System.StringComparison.OrdinalIgnoreCase))

    /// Decide a single (reference, intervention) pair.
    ///
    /// Order of evaluation (DECISIONS 2026-06-12 — reconciliation
    /// slice 1; restores V1's carve-out, ForeignKeyEvaluator.cs:124-145):
    ///   1. MissingTarget — target kind absent from catalog. A
    ///      reference whose target is outside this catalog
    ///      (module-scoped export; dangling edge) cannot emit a
    ///      deployable constraint regardless of source backing — the
    ///      suppression is structural. V2 surfaces explicitly; V1
    ///      silently skips. (Outranks PolicyDisabled: the structural
    ///      impossibility beats the chosen gate.)
    ///   2. DatabaseConstraintPresent — `reference.HasDbConstraint =
    ///      true` (V1's `HasDatabaseConstraint`, carried on the IR
    ///      since chapter 4.6 slice α). Enforced BEFORE and
    ///      REGARDLESS OF every remaining gate: `EnableCreation`
    ///      gates only NEW creation; orphan evidence never overrides
    ///      (physically-backed-with-orphans only arises under
    ///      NOCHECK, which `IsConstraintTrusted` realizes at the
    ///      emitter). The prior approximation — `ProbeStatus.Outcome
    ///      = TrustedConstraint` — had no producer anywhere (dead in
    ///      production) and is retired; the DU variant stays for
    ///      codec compatibility.
    ///   3. PolicyDisabled — `EnableCreation = false`. Gate the
    ///      caller chose; reported without further reasoning. New
    ///      constraints only, per the carve-out above.
    ///   4. Profile-driven decision:
    ///        - Probe missing or unreliable ⇒ EvidenceMissing
    ///          (V2 collapsed-mode strict default; V1 implicitly
    ///          falls through to `EnableCreation` gate). V2 surfaces
    ///          the missing evidence explicitly.
    ///        - Profile shows orphans + AllowNoCheckCreation=false ⇒
    ///          DoNotEnforce(DataHasOrphans).
    ///        - Profile shows orphans + AllowNoCheckCreation=true ⇒
    ///          EnforceConstraint(ScriptWithNoCheck).
    ///        - Profile clean + cross-schema gate fails ⇒
    ///          DoNotEnforce(CrossSchemaBlocked).
    ///        - Profile clean + cross-catalog gate fails ⇒
    ///          DoNotEnforce(CrossCatalogBlocked) (unreachable today
    ///          per IR-refinement deferral; reserved DU variant).
    ///        - Profile clean + DeleteRule = Ignore ⇒
    ///          DoNotEnforce(DeleteRuleIgnored).
    ///        - Profile clean + eligible ⇒
    ///          EnforceConstraint(NoEvidenceObstacle).
    let evaluate
        (interventionId: string)
        (config: ForeignKeyTighteningConfig)
        (sourceKind: Kind)
        (reference: Reference)
        (catalog: Catalog)
        (profile: Profile)
        : ForeignKeyDecision =
        use _ = Bench.scope "rules.foreignKey.evaluate"

        let mkDecision outcome : ForeignKeyDecision =
            { ReferenceKey   = reference.SsKey
              Outcome        = outcome
              InterventionId = interventionId }

        // 0. The operator's per-reference override + the intervention's
        //    direction (DECISIONS 2026-07-15, the estate A6 amendment).
        //    The override is absolute in BOTH directions — mirroring the
        //    nullability hierarchy's step 1 — and outranks even the
        //    source-backed carve-out below: the interim posture
        //    deliberately untracks relationships the agreed shape
        //    carries. A RELAXATION-ONLY intervention otherwise carries
        //    the declared shape untouched (identity at emission); the
        //    hierarchy below is the EvidenceDriven direction.
        if ForeignKeyTighteningConfig.shouldKeepUntracked reference.SsKey config then
            mkDecision (ForeignKeyOutcome.DoNotEnforce OperatorUntracked)
        elif config.Direction = TighteningDirection.RelaxationOnly then
            mkDecision (ForeignKeyOutcome.EnforceConstraint DeclaredShapeCarried)
        else

        // 1. Target kind must exist in the catalog. Structural
        //    impossibility outranks every gate: a scoped export cannot
        //    emit an FK to a kind outside the catalog, source-backed
        //    or not (the diagnostics layer reports the source-backed
        //    case HasDbConstraint-aware).
        match Catalog.tryFindKind reference.TargetKind catalog with
        | None ->
            mkDecision (ForeignKeyOutcome.DoNotEnforce MissingTarget)
        | Some targetKind ->
            // 2. V1's carve-out, restored (DECISIONS 2026-06-12): a
            //    reference backed by a real source-side DB constraint
            //    is enforced before and regardless of every remaining
            //    gate. Reads the IR flag directly; the dead
            //    TrustedConstraint probe approximation is retired.
            if Reference.hasDbConstraint reference then
                mkDecision
                    (ForeignKeyOutcome.EnforceConstraint
                        DatabaseConstraintPresent)
            // 3. EnableCreation gate — new constraints only. The
            //    algebra reports the gate.
            elif not config.EnableCreation then
                mkDecision (ForeignKeyOutcome.DoNotEnforce PolicyDisabled)
            else
                // 4. Profile-driven decision on orphan signals.
                let realityOpt = Profile.tryFindForeignKey reference.SsKey profile
                match realityOpt with
                | Some reality when ProbeStatus.isReliable reality.ProbeStatus ->
                    // Probe ran successfully; consult the result.
                    if reality.HasOrphan then
                        if config.AllowNoCheckCreation then
                            mkDecision
                                (ForeignKeyOutcome.EnforceConstraint
                                    (ScriptWithNoCheck reality.OrphanCount))
                        else
                            mkDecision
                                (ForeignKeyOutcome.DoNotEnforce
                                    (DataHasOrphans reality.OrphanCount))
                    else
                        // Clean profile. Now apply structural gates
                        // (cross-schema, cross-catalog, delete-rule).
                        // Order: cross-schema, cross-catalog,
                        // delete-rule, then NoEvidenceObstacle.
                        if not config.AllowCrossSchema && crossesSchema sourceKind targetKind then
                            mkDecision
                                (ForeignKeyOutcome.DoNotEnforce CrossSchemaBlocked)
                        // Cross-catalog rule is unreachable today — V2's IR
                        // has no catalog field (its inert `AllowCrossCatalog`
                        // config knob was removed at WP-1d; the reserved
                        // `CrossCatalogBlocked` DU variant stays).
                        elif isIgnoreRule reference.OnDelete then
                            mkDecision
                                (ForeignKeyOutcome.DoNotEnforce DeleteRuleIgnored)
                        else
                            mkDecision
                                (ForeignKeyOutcome.EnforceConstraint
                                    (NoEvidenceObstacle reality.ProbeStatus.SampleSize))
                | Some _ ->
                    // Probe outcome is unreliable (FallbackTimeout /
                    // Cancelled / AmbiguousMapping).
                    mkDecision
                        (ForeignKeyOutcome.DoNotEnforce EvidenceMissing)
                | None ->
                    // No probe at all. V2 collapsed-mode strict
                    // default: do not enforce on missing evidence.
                    mkDecision
                        (ForeignKeyOutcome.DoNotEnforce EvidenceMissing)


    // -----------------------------------------------------------------------
    // Helpers for callers exploring the decision shape.
    // -----------------------------------------------------------------------

    /// True iff the decision creates the FK constraint.
    let enforces (decision: ForeignKeyDecision) : bool =
        match decision.Outcome with
        | ForeignKeyOutcome.EnforceConstraint _ -> true
        | ForeignKeyOutcome.DoNotEnforce      _ -> false

    /// True iff the decision is an EnforceConstraint with the
    /// ScriptWithNoCheck evidence variant. Convenience for emitters
    /// that need to render `WITH NOCHECK` clauses.
    let scriptsWithNoCheck (decision: ForeignKeyDecision) : bool =
        match decision.Outcome with
        | ForeignKeyOutcome.EnforceConstraint (ScriptWithNoCheck _) -> true
        | _ -> false
