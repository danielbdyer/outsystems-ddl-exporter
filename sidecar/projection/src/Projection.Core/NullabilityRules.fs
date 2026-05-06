namespace Projection.Core

/// Why a column was tightened to NOT NULL. Structured, not stringly-
/// typed — emitter consumers can pattern-match on the rationale to
/// surface user-facing messages, audit reports, etc. See DECISIONS
/// 2026-05-09 (NullabilityOutcome shape) for the V1↔masterwork choice
/// precedent this DU sets.
type NullabilityEvidence =
    /// Column is part of the primary key. Always tightens regardless
    /// of mode / budget / profile.
    | PrimaryKey
    /// Column is physically NOT NULL in the source schema. Always
    /// tightens; data already conforms.
    | PhysicallyNotNull
    /// The model declares the column mandatory and the profile is
    /// absent. Trusts logical schema in the absence of empirical
    /// evidence.
    | LogicalMandatoryNoProfile
    /// The model declares the column mandatory and the profile shows
    /// zero observed nulls.
    | LogicalMandatoryNoNulls of rowCount: int64
    /// The model declares the column mandatory and the profile shows
    /// nulls within the configured budget.
    | LogicalMandatoryWithinBudget of
        nullCount: int64 *
        rowCount: int64 *
        budget: decimal


/// Why a column stays nullable.
type KeepNullableReason =
    /// An override in the intervention's `Overrides` list directs
    /// nullability for this attribute. Bypasses the entire signal
    /// hierarchy; absolute.
    | OperatorOverride
    /// No structural or evidential signal fires. The column is
    /// neither a PK, nor physically NOT NULL, nor logically mandatory.
    | NoTighteningSignal
    /// Profile shows nulls exceeding the budget AND the intervention's
    /// `AllowMandatoryRelaxation` is true (the caller permits
    /// relaxation under evidence pressure).
    | RelaxedUnderEvidence of nullCount: int64 * rowCount: int64 * budget: decimal


/// The third state — model and data disagree, and the intervention is
/// not configured to silently relax. The decision lifts to the
/// operator.
type NullabilityConflict =
    /// Model declares the column mandatory; profile shows nulls
    /// exceeding the budget; intervention forbids relaxation
    /// (`AllowMandatoryRelaxation = false`).
    | MandatoryButHasNullsBeyondBudget of
        nullCount: int64 *
        rowCount: int64 *
        budget: decimal


/// The outcome of a single (attribute, intervention) decision. Every
/// variant carries structured rationale at the type level — the
/// lineage chain captures *why* without resorting to text.
///
/// `RequireQualifiedAccess` because `KeepNullable` clashes with
/// `OverrideAction.KeepNullable` (Policy.fs) — the override action
/// "direct the column to keep nullable" is conceptually distinct from
/// the decision outcome "the decision is to keep nullable", but both
/// are reasonable case names in their own context. Qualified access
/// keeps both readable.
[<RequireQualifiedAccess>]
type NullabilityOutcome =
    | EnforceNotNull of evidence: NullabilityEvidence
    | KeepNullable of reason: KeepNullableReason
    | RequireOperatorApproval of conflict: NullabilityConflict


/// One decision keyed to its attribute and the intervention that
/// produced it. The `InterventionId` lets audit consumers answer
/// "which intervention changed this column?" structurally.
type NullabilityDecision = {
    AttributeKey   : SsKey
    Outcome        : NullabilityOutcome
    InterventionId : string
}


/// The aggregate output of one or more interventions running over a
/// catalog. Empty when no interventions are registered (V2's
/// observable-identity-on-empty-policy commitment, DECISIONS
/// 2026-05-09).
type NullabilityDecisionSet = {
    Decisions : NullabilityDecision list
}


/// Domain rules for nullability tightening — the V1
/// `NullabilityEvaluator` migration's domain layer per the
/// algebra/domain split (DECISIONS 2026-05-09 — Algebra/domain split
/// pattern). Pure functions of the IR fields; no I/O; no mutable
/// state.
///
/// The pure pass (`NullabilityPass`) walks the catalog and iterates
/// over registered Nullability interventions; per (attribute,
/// intervention), this module's `evaluate` returns a single
/// `NullabilityDecision`. The pass accumulates decisions; the rules
/// module decides each one.
[<RequireQualifiedAccess>]
module NullabilityRules =

    /// Empty decision set — V2's strict default when no interventions
    /// are registered. No decisions, no rationale, no
    /// non-action-as-action.
    let emptyDecisionSet : NullabilityDecisionSet = { Decisions = [] }

    // -----------------------------------------------------------------------
    // Decider — the per-attribute rule. Pure function of the
    // intervention's config + the IR context.
    //
    // Order of evaluation matches V1's signal hierarchy:
    //  1. Operator override (absolute; bypasses everything).
    //  2. Primary-key (structural; no profile needed).
    //  3. Physical-NOT-NULL (structural; no profile needed).
    //  4. Logical-Mandatory + profile evidence:
    //     - profile absent  ⇒ EnforceNotNull(LogicalMandatoryNoProfile).
    //     - profile shows zero nulls ⇒ EnforceNotNull(LogicalMandatoryNoNulls).
    //     - profile shows nulls within budget ⇒
    //       EnforceNotNull(LogicalMandatoryWithinBudget).
    //     - profile shows nulls beyond budget:
    //       - if AllowMandatoryRelaxation ⇒ KeepNullable(RelaxedUnderEvidence).
    //       - else ⇒ RequireOperatorApproval(MandatoryButHasNullsBeyondBudget).
    //  5. Otherwise ⇒ KeepNullable(NoTighteningSignal).
    // -----------------------------------------------------------------------

    /// Decide a single (attribute, intervention) pair.
    let evaluate
        (interventionId: string)
        (config: NullabilityTighteningConfig)
        (attribute: Attribute)
        (profile: Profile)
        : NullabilityDecision =

        let mkDecision outcome : NullabilityDecision =
            { AttributeKey   = attribute.SsKey
              Outcome        = outcome
              InterventionId = interventionId }

        // Profile parameter is consumed once IsMandatory lands on
        // Attribute (the planned IR refinement that lets the mandatory
        // branch fire). Until then, the profile is referenced only to
        // keep the function's signature stable across that refinement.
        ignore profile

        // 1. Operator override — absolute.
        if NullabilityTighteningConfig.shouldKeepNullable attribute.SsKey config then
            mkDecision (NullabilityOutcome.KeepNullable OperatorOverride)
        // 2. Primary key — structural.
        elif attribute.IsPrimaryKey then
            mkDecision (NullabilityOutcome.EnforceNotNull PrimaryKey)
        // 3. Physical NOT NULL — structural.
        elif not attribute.Column.IsNullable then
            mkDecision (NullabilityOutcome.EnforceNotNull PhysicallyNotNull)
        else
            // The remaining decisions depend on the logical-mandatory
            // flag and (if mandatory) profile evidence. V2 currently
            // has no IsMandatory field on Attribute; that's a planned
            // IR refinement landing under "IR grows under evidence"
            // when a real V1 fixture surfaces a mandatory column V2
            // cannot represent. For now, a physically nullable, non-PK
            // attribute without an override has no mandatory marker —
            // KeepNullable.
            //
            // When IsMandatory lands on Attribute, the branches below
            // wire in. The logic is preserved here as commented
            // pseudocode so the migration is transparent:
            //
            //   match Profile.tryFindColumn attribute.SsKey profile with
            //   | None ->
            //       mkDecision (NullabilityOutcome.EnforceNotNull
            //                       LogicalMandatoryNoProfile)
            //   | Some col ->
            //       let allowed = decimal col.RowCount * config.NullBudget
            //       let observed = decimal col.NullCount
            //       if col.NullCount = 0L then
            //           mkDecision (NullabilityOutcome.EnforceNotNull
            //                          (LogicalMandatoryNoNulls col.RowCount))
            //       elif observed <= allowed then
            //           mkDecision (NullabilityOutcome.EnforceNotNull
            //                          (LogicalMandatoryWithinBudget
            //                              (col.NullCount, col.RowCount, config.NullBudget)))
            //       elif config.AllowMandatoryRelaxation then
            //           mkDecision (NullabilityOutcome.KeepNullable
            //                          (RelaxedUnderEvidence
            //                              (col.NullCount, col.RowCount, config.NullBudget)))
            //       else
            //           mkDecision (NullabilityOutcome.RequireOperatorApproval
            //                          (MandatoryButHasNullsBeyondBudget
            //                              (col.NullCount, col.RowCount, config.NullBudget)))
            mkDecision (NullabilityOutcome.KeepNullable NoTighteningSignal)


    // -----------------------------------------------------------------------
    // Convenience for callers exploring the rationale on a decision.
    // -----------------------------------------------------------------------

    /// True iff the decision tightens the column to NOT NULL.
    let enforces (decision: NullabilityDecision) : bool =
        match decision.Outcome with
        | NullabilityOutcome.EnforceNotNull _ -> true
        | _                                   -> false

    /// True iff the decision lifts to operator approval.
    let requiresApproval (decision: NullabilityDecision) : bool =
        match decision.Outcome with
        | NullabilityOutcome.RequireOperatorApproval _ -> true
        | _                                            -> false
