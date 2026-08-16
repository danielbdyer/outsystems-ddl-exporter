namespace Projection.Core

/// Why a unique index was tightened to enforce uniqueness. Structured,
/// not stringly-typed — emitter consumers pattern-match on the rationale
/// (DECISIONS 2026-05-09 — NullabilityOutcome shape).
type UniqueIndexEvidence =
    /// The catalog already declares this index unique
    /// (`Index.IsUnique = true`). Trusted regardless of profile evidence
    /// — the source of truth is the source.
    | AlreadyUnique
    /// The single-column unique candidate's profile shows no observed
    /// duplicates and the probe succeeded.
    | SingleColumnNoDuplicates of probeRowCount: int64
    /// The composite unique candidate's profile shows no observed
    /// duplicates and the probe succeeded.
    | CompositeNoDuplicates


/// Why a unique index stays un-enforced.
type UniqueIndexKeepReason =
    /// `EnforceSingleColumnUnique = false` (the index is single-column)
    /// or `EnforceMultiColumnUnique = false` (composite). The
    /// intervention's policy disables enforcement for this index's
    /// column-count category. **No domain reasoning consulted** — this
    /// is the gate the caller chose; the algebra reports the gate.
    | PolicyDisabled
    /// Profile evidence shows duplicates in the candidate's data —
    /// enforcing uniqueness would violate existing rows.
    | DataHasDuplicates
    /// Profile probe did not succeed (FallbackTimeout / Cancelled /
    /// AmbiguousMapping); evidence is missing. V2 collapsed-mode
    /// default: do not tighten on missing evidence (DECISIONS
    /// 2026-05-09).
    | EvidenceMissing
    /// No profile candidate exists for this index — the source's
    /// uniqueness intent is declared but not empirically tested.
    /// Treated as missing evidence; tightening withheld.
    | NoCandidateProfiled
    /// The index is NOT declared unique, but its data shows no duplicates
    /// — a valid promotion CANDIDATE. Enforcement is withheld because the
    /// intervention runs advise-only (`ApplyProfilePromotions = false`, the
    /// operator default): the dev team's declared indexes are authoritative,
    /// so a profile-driven tightening is surfaced as advice and NOT applied
    /// unless the operator opts in with `applyUniquePromotions: true`
    /// (operator directive 2026-07-18). align-II.3 (a4-4): the advisory
    /// CARRIES the evidence that fed it — the adoption lever anchors to
    /// what the operator actually reviewed, and a later adoption against
    /// different data is detectable rather than silent.
    | PromotionAdvisedNotApplied of evidence: UniqueIndexEvidence
    /// The index was a valid promotion candidate and the OPERATOR REFUSED
    /// it by name (align-II.3: a per-index `RefusePromotion` override) —
    /// distinct from advise-pending: this is a recorded per-subject
    /// ruling, not an un-adjudicated advisory.
    | PromotionRefusedByOperator of evidence: UniqueIndexEvidence


/// The outcome of a single (index, intervention) decision.
///
/// Binary, not ternary — V1's `UniqueIndexDecisionOrchestrator` has no
/// `RequireOperatorApproval` state (per ADMIRE.md 2026-05-10). V2
/// inherits the binary form and adds structured rationale at the type
/// level per the V1↔masterwork principle (DECISIONS 2026-05-09): V2
/// picks based on what serves the algebra, not by inheritance from
/// one source.
///
/// `RequireQualifiedAccess` for the same reason `NullabilityOutcome` is
/// — `EnforceUnique` and `DoNotEnforce` are intuitive names that may
/// clash with other DUs as the algebra grows.
[<RequireQualifiedAccess>]
type UniqueIndexOutcome =
    | EnforceUnique of evidence: UniqueIndexEvidence
    | DoNotEnforce of reason: UniqueIndexKeepReason


/// One decision keyed to its index and the intervention that produced
/// it. Mirrors `NullabilityDecision` in shape; key is `IndexKey` rather
/// than `AttributeKey` because uniqueness is per-index (the structural
/// divergence captured in ADMIRE.md 2026-05-10).
type UniqueIndexDecision = {
    IndexKey       : SsKey
    Outcome        : UniqueIndexOutcome
    InterventionId : string
}


/// The aggregate output of one or more UniqueIndex interventions
/// running over a catalog. Empty when no interventions are registered
/// (the observable-identity-on-empty-policy commitment, DECISIONS
/// 2026-05-09).
type UniqueIndexDecisionSet = {
    Decisions : UniqueIndexDecision list
}


/// Domain rules for unique-index tightening — the V1
/// `UniqueIndexDecisionOrchestrator` migration's domain layer per the
/// algebra/domain split (DECISIONS 2026-05-09 — Algebra/domain split
/// pattern). Pure functions of the IR fields; no I/O; no mutable
/// state.
///
/// The pure pass (`UniqueIndexPass`) walks the catalog's indexes and
/// iterates over registered UniqueIndex interventions; per
/// (index, intervention), this module's `evaluate` returns one
/// `UniqueIndexDecision`. The pass accumulates decisions; the rules
/// module decides each one.
///
/// **Algebra/domain separation (per the session-7 reminder).** The
/// signal hierarchy below is **policy logic** — every decision in
/// Typed structural display per UniqueIndex DUs. Each variant
/// returns a typed `StructuredString`; punctuation lives in
/// `StructuredString.render`. Per the data-structure-oriented
/// discipline.
[<RequireQualifiedAccess>]
module UniqueIndexEvidence =
    let toStructured (e: UniqueIndexEvidence) : StructuredString =
        match e with
        | AlreadyUnique -> StructuredString.tag "AlreadyUnique"
        | SingleColumnNoDuplicates probeRowCount ->
            StructuredString.create "SingleColumnNoDuplicates"
                [ "probeRowCount", Inv.int64 probeRowCount ]
        | CompositeNoDuplicates -> StructuredString.tag "CompositeNoDuplicates"

    let toDiagnosticString (e: UniqueIndexEvidence) : string =
        toStructured e |> StructuredString.render

[<RequireQualifiedAccess>]
module UniqueIndexKeepReason =
    let toStructured (r: UniqueIndexKeepReason) : StructuredString =
        match r with
        | PolicyDisabled -> StructuredString.tag "PolicyDisabled"
        | DataHasDuplicates -> StructuredString.tag "DataHasDuplicates"
        | EvidenceMissing -> StructuredString.tag "EvidenceMissing"
        | NoCandidateProfiled -> StructuredString.tag "NoCandidateProfiled"
        | PromotionAdvisedNotApplied evidence ->
            StructuredString.create "PromotionAdvisedNotApplied"
                [ "evidence", UniqueIndexEvidence.toDiagnosticString evidence ]
        | PromotionRefusedByOperator evidence ->
            StructuredString.create "PromotionRefusedByOperator"
                [ "evidence", UniqueIndexEvidence.toDiagnosticString evidence ]

    let toDiagnosticString (r: UniqueIndexKeepReason) : string =
        toStructured r |> StructuredString.render

[<RequireQualifiedAccess>]
module UniqueIndexOutcome =
    let toStructured (o: UniqueIndexOutcome) : StructuredString =
        match o with
        | UniqueIndexOutcome.EnforceUnique e ->
            StructuredString.create "EnforceUnique"
                [ "evidence", UniqueIndexEvidence.toDiagnosticString e ]
        | UniqueIndexOutcome.DoNotEnforce r ->
            StructuredString.create "DoNotEnforce"
                [ "reason", UniqueIndexKeepReason.toDiagnosticString r ]

    let toDiagnosticString (o: UniqueIndexOutcome) : string =
        toStructured o |> StructuredString.render


/// `evaluate` is a domain rule that V2 inherits from V1 (or refines
/// from V1). The rules ARE the intervention; they live here, named
/// explicitly, so the algebra layer (the pass driver) can iterate
/// without knowing how decisions are made. Future intervention
/// strategies (manual override sets, cascade-aware uniqueness, etc.)
/// land here as new `evaluate` variants or sibling rules modules —
/// never inside the pass driver.
[<RequireQualifiedAccess>]
module UniqueIndexRules =

    /// Empty decision set — V2's strict default when no interventions
    /// are registered.
    let emptyDecisionSet : UniqueIndexDecisionSet = { Decisions = [] }

    /// True iff the index has at least two key columns (composite). A
    /// single-key index is a single-column unique candidate.
    let private isComposite (index: Index) : bool =
        List.length index.Columns >= 2

    /// Look up profile evidence for a single-column unique candidate
    /// (align-II.4b: returns the collapse-free `ProbeReading` — the
    /// prior option folded "probe ran but was unreliable" into "no
    /// candidate profiled", making `EvidenceMissing` unproducible while
    /// its operator copy and consumers were live).
    let private singleColumnProbe (attributeKey: SsKey) (profile: Profile) : ProbeReading<bool * int64> =
        Profile.tryFindUnique attributeKey profile
        |> ProbeReading.ofCandidate
            (fun candidate -> candidate.ProbeStatus)
            // RowCount isn't on UniqueCandidateProfile in V2's IR;
            // the probe's SampleSize is the available proxy.
            (fun candidate -> not candidate.HasDuplicate, candidate.ProbeStatus.SampleSize)

    /// Look up profile evidence for a composite unique candidate.
    /// V2's CompositeUniqueCandidateProfile carries `ProbeStatus`
    /// (a session-2 V2 fix closing a V1 gap; see DECISIONS).
    let private compositeProbe
        (kindKey: SsKey)
        (attributeKeys: SsKey list)
        (profile: Profile)
        : ProbeReading<bool> =
        let attributeKeySet = Set.ofList attributeKeys
        let matching =
            profile.CompositeUniqueCandidates
            |> List.tryFind (fun c ->
                c.KindKey = kindKey
                && Set.ofList c.AttributeKeys = attributeKeySet)
        matching
        |> ProbeReading.ofCandidate
            (fun candidate -> candidate.ProbeStatus)
            (fun candidate -> not candidate.HasDuplicate)

    /// Decide a single (index, intervention) pair for the given kind.
    /// Order of evaluation:
    ///   1. AlreadyUnique — the catalog declares the index unique;
    ///      trusted regardless of profile.
    ///   2. PolicyDisabled — the intervention's toggle for this
    ///      column-count category is off.
    ///   3. Profile-driven decision:
    ///      - probe succeeded + duplicates absent ⇒ EnforceUnique.
    ///      - probe succeeded + duplicates present ⇒
    ///        DoNotEnforce(DataHasDuplicates).
    ///      - no candidate profiled ⇒ DoNotEnforce(NoCandidateProfiled)
    ///        ("run the profiler"); a probe RAN but was unreliable ⇒
    ///        DoNotEnforce(EvidenceMissing) ("your probe failed —
    ///        investigate"). align-II.4b made the second arm producible
    ///        — it had been dead vocabulary with live consumers.
    let evaluate
        (interventionId: string)
        (config: UniqueIndexTighteningConfig)
        (kind: Kind)
        (index: Index)
        (profile: Profile)
        : UniqueIndexDecision =
        use _ = Bench.scope "rules.uniqueIndex.evaluate"

        let mkDecision outcome : UniqueIndexDecision =
            { IndexKey       = index.SsKey
              Outcome        = outcome
              InterventionId = interventionId }

        // 1. Already unique in the catalog — structural, trusted.
        if IndexUniqueness.isUnique index.Uniqueness then
            mkDecision (UniqueIndexOutcome.EnforceUnique AlreadyUnique)
        // 2. Policy-disabled gate. Composite vs single-column toggle.
        else
            let toggle =
                if isComposite index then config.EnforceMultiColumnUnique
                else config.EnforceSingleColumnUnique
            if not toggle then
                mkDecision (UniqueIndexOutcome.DoNotEnforce PolicyDisabled)
            else
                // 3. Profile-driven decision. A no-duplicate candidate is a
                // PROMOTION beyond the source's declaration; it is APPLIED
                // (EnforceUnique) only when `ApplyProfilePromotions` is set,
                // else surfaced ADVISE-ONLY (`PromotionAdvisedNotApplied`) so
                // the dev team's declared indexes stay authoritative.
                // align-II.3: the per-index override is consulted FIRST
                // (the nullability hierarchy's step-1 shape) — adopting ONE
                // promotion while refusing another is expressible; the
                // blanket ApplyProfilePromotions flag is the fallback.
                let promoteOrAdvise (evidence: UniqueIndexEvidence) : UniqueIndexOutcome =
                    let overrideFor =
                        config.Overrides
                        |> List.tryFind (fun o -> o.IndexKey = index.SsKey)
                        |> Option.map (fun o -> o.Action)
                    match overrideFor with
                    | Some UniqueIndexOverrideAction.AdoptPromotion -> UniqueIndexOutcome.EnforceUnique evidence
                    | Some UniqueIndexOverrideAction.RefusePromotion -> UniqueIndexOutcome.DoNotEnforce (PromotionRefusedByOperator evidence)
                    | None ->
                        if config.ApplyProfilePromotions then UniqueIndexOutcome.EnforceUnique evidence
                        else UniqueIndexOutcome.DoNotEnforce (PromotionAdvisedNotApplied evidence)
                let columnKeys = index.Columns |> List.map (fun c -> c.Attribute)
                if isComposite index then
                    match compositeProbe kind.SsKey columnKeys profile with
                    | ProbeReading.Reliable true ->
                        mkDecision (promoteOrAdvise CompositeNoDuplicates)
                    | ProbeReading.Reliable false ->
                        mkDecision (UniqueIndexOutcome.DoNotEnforce DataHasDuplicates)
                    | ProbeReading.Unreliable _ ->
                        mkDecision (UniqueIndexOutcome.DoNotEnforce EvidenceMissing)
                    | ProbeReading.NotProfiled ->
                        mkDecision (UniqueIndexOutcome.DoNotEnforce NoCandidateProfiled)
                else
                    // Single-column path. The index has exactly one
                    // attribute (or zero — degenerate; treated as
                    // single-column with no probe).
                    match columnKeys with
                    | [] ->
                        mkDecision (UniqueIndexOutcome.DoNotEnforce NoCandidateProfiled)
                    | columnKey :: _ ->
                        match singleColumnProbe columnKey profile with
                        | ProbeReading.Reliable (true, rowCount) ->
                            mkDecision (promoteOrAdvise (SingleColumnNoDuplicates rowCount))
                        | ProbeReading.Reliable (false, _) ->
                            mkDecision (UniqueIndexOutcome.DoNotEnforce DataHasDuplicates)
                        | ProbeReading.Unreliable _ ->
                            mkDecision (UniqueIndexOutcome.DoNotEnforce EvidenceMissing)
                        | ProbeReading.NotProfiled ->
                            mkDecision (UniqueIndexOutcome.DoNotEnforce NoCandidateProfiled)


    // -----------------------------------------------------------------------
    // Helpers for callers exploring the decision shape.
    // -----------------------------------------------------------------------

    /// True iff the decision enforces uniqueness on the index.
    let enforces (decision: UniqueIndexDecision) : bool =
        match decision.Outcome with
        | UniqueIndexOutcome.EnforceUnique _ -> true
        | UniqueIndexOutcome.DoNotEnforce _  -> false
