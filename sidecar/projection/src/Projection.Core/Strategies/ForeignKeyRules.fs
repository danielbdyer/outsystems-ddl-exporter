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
    /// `AllowCrossCatalog = false` and the FK crosses catalog
    /// (database) boundaries. **Currently unreachable** — V2's
    /// IR does not model catalog names; reserved as a DU variant
    /// pending the IR refinement (ADMIRE.md 2026-05-11).
    | CrossCatalogBlocked
    /// Delete rule = `Ignore` (or missing + `TreatMissingDeleteRuleAsIgnore`
    /// would be true, but V2's `Reference.OnDelete` cannot be
    /// missing). V1 does not enforce these by default.
    | DeleteRuleIgnored
    /// Profile probe did not succeed (FallbackTimeout / Cancelled /
    /// AmbiguousMapping); evidence is missing. V2's collapsed-mode
    /// strict default declines to enforce on missing evidence.
    | EvidenceMissing
    /// The FK's target kind is absent from the catalog. V1 silently
    /// skips; V2 reports the absence explicitly so the audit chain
    /// has a reason for the missing constraint.
    | MissingTarget


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

    /// V1's `IsIgnoreRule` predicate, in V2 form. V2's
    /// `Reference.OnDelete` is a closed DU and cannot be missing;
    /// V1's `TreatMissingDeleteRuleAsIgnore` toggle is preserved on
    /// the config for V1 parity but currently unreachable from V2's
    /// IR. The predicate fires only on explicit `OnDelete` values
    /// V1 treats as "Ignore"; V2's `OnDelete` DU has no `Ignore`
    /// variant (V1's "Ignore" is V2's `NoAction` semantically — the
    /// DB does nothing; the application enforces). When the V1↔V2
    /// adapter lands, V1's "Ignore" maps to a synthetic IR field;
    /// for now the rule is unreachable from synthetic catalogs.
    ///
    /// Returns `false` for every variant of V2's current `OnDelete`
    /// DU; documented for completeness so the V1 parity is visible.
    let private isIgnoreRule (_action: ReferenceAction) (_config: ForeignKeyTighteningConfig) : bool =
        false

    /// True iff the FK crosses schema boundaries. Reads the source
    /// kind's `Physical.Schema` and the target kind's
    /// `Physical.Schema`; treats them as case-insensitive
    /// equivalents (V1's `SchemaEquals`, ForeignKeyEvaluator.cs:231).
    let private crossesSchema (sourceKind: Kind) (targetKind: Kind) : bool =
        not (System.String.Equals(
                sourceKind.Physical.Schema,
                targetKind.Physical.Schema,
                System.StringComparison.OrdinalIgnoreCase))

    /// Decide a single (reference, intervention) pair.
    ///
    /// Order of evaluation (V1 signal hierarchy, ForeignKeyEvaluator.cs:83):
    ///   1. PolicyDisabled — `EnableCreation = false`. Gate the
    ///      caller chose; reported without further reasoning.
    ///   2. MissingTarget — target kind absent from catalog. V2
    ///      surfaces explicitly; V1 silently skips.
    ///   3. DatabaseConstraintPresent — V1's `HasDatabaseConstraint = true`
    ///      maps to V2's `Profile.ForeignKeys[ref].IsNoCheck`-aware
    ///      probe state. V2's `ForeignKeyReality` doesn't carry a
    ///      direct `HasDatabaseConstraint` flag; the closest semantic
    ///      is "probe succeeded against an existing constraint."
    ///      Until the V1↔V2 adapter wires this through, V2 derives
    ///      it from `ProbeStatus.Outcome = TrustedConstraint` (the
    ///      probe was skipped because the DB constraint was trusted —
    ///      that *is* the V1 signal). Reading
    ///      `Profile.ForeignKeys[ref]` and matching on the outcome.
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

        // 1. EnableCreation gate. The algebra reports the gate.
        if not config.EnableCreation then
            mkDecision (ForeignKeyOutcome.DoNotEnforce PolicyDisabled)
        else
            // 2. Target kind must exist in the catalog.
            match Catalog.tryFindKind reference.TargetKind catalog with
            | None ->
                mkDecision (ForeignKeyOutcome.DoNotEnforce MissingTarget)
            | Some targetKind ->
                // 3. Profile-driven decision on existing-constraint
                //    + orphan signals. V2's TrustedConstraint probe
                //    outcome maps to V1's HasDatabaseConstraint=true:
                //    the DB constraint was trusted, so it exists.
                let realityOpt = Profile.tryFindForeignKey reference.SsKey profile
                match realityOpt with
                | Some reality when reality.ProbeStatus.Outcome = TrustedConstraint ->
                    // The DB already enforces this — trust the source.
                    mkDecision
                        (ForeignKeyOutcome.EnforceConstraint
                            DatabaseConstraintPresent)
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
                        // Cross-catalog rule is unreachable today —
                        // V2's IR has no catalog field. The DU
                        // variant is reserved; pattern-match
                        // exhaustiveness keeps the shape ready.
                        elif isIgnoreRule reference.OnDelete config then
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
