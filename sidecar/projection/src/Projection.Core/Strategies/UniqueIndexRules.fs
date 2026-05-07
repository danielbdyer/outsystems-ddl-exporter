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

    /// Look up profile evidence for a single-column unique candidate.
    /// Returns:
    ///   - `Some true`  if the probe succeeded and showed no duplicates.
    ///   - `Some false` if the probe succeeded and showed duplicates.
    ///   - `None`       if no probe succeeded (or no candidate
    ///                  profiled).
    let private singleColumnProbe (attributeKey: SsKey) (profile: Profile) : (bool * int64) option =
        match Profile.tryFindUnique attributeKey profile with
        | Some candidate when ProbeStatus.isReliable candidate.ProbeStatus ->
            // RowCount isn't on UniqueCandidateProfile in V2's IR;
            // the probe's SampleSize is the available proxy.
            Some (not candidate.HasDuplicate, candidate.ProbeStatus.SampleSize)
        | _ -> None

    /// Look up profile evidence for a composite unique candidate.
    /// V2's CompositeUniqueCandidateProfile carries `ProbeStatus`
    /// (a session-2 V2 fix closing a V1 gap; see DECISIONS).
    let private compositeProbe
        (kindKey: SsKey)
        (attributeKeys: SsKey list)
        (profile: Profile)
        : bool option =
        let attributeKeySet = Set.ofList attributeKeys
        let matching =
            profile.CompositeUniqueCandidates
            |> List.tryFind (fun c ->
                c.KindKey = kindKey
                && Set.ofList c.AttributeKeys = attributeKeySet)
        match matching with
        | Some candidate when ProbeStatus.isReliable candidate.ProbeStatus ->
            Some (not candidate.HasDuplicate)
        | _ -> None

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
    ///      - probe missing (no candidate profiled or probe failed)
    ///        ⇒ DoNotEnforce(EvidenceMissing | NoCandidateProfiled).
    let evaluate
        (interventionId: string)
        (config: UniqueIndexTighteningConfig)
        (kind: Kind)
        (index: Index)
        (profile: Profile)
        : UniqueIndexDecision =

        let mkDecision outcome : UniqueIndexDecision =
            { IndexKey       = index.SsKey
              Outcome        = outcome
              InterventionId = interventionId }

        // 1. Already unique in the catalog — structural, trusted.
        if index.IsUnique then
            mkDecision (UniqueIndexOutcome.EnforceUnique AlreadyUnique)
        // 2. Policy-disabled gate. Composite vs single-column toggle.
        else
            let toggle =
                if isComposite index then config.EnforceMultiColumnUnique
                else config.EnforceSingleColumnUnique
            if not toggle then
                mkDecision (UniqueIndexOutcome.DoNotEnforce PolicyDisabled)
            else
                // 3. Profile-driven decision.
                if isComposite index then
                    match compositeProbe kind.SsKey index.Columns profile with
                    | Some true ->
                        mkDecision (UniqueIndexOutcome.EnforceUnique CompositeNoDuplicates)
                    | Some false ->
                        mkDecision (UniqueIndexOutcome.DoNotEnforce DataHasDuplicates)
                    | None ->
                        mkDecision (UniqueIndexOutcome.DoNotEnforce NoCandidateProfiled)
                else
                    // Single-column path. The index has exactly one
                    // attribute (or zero — degenerate; treated as
                    // single-column with no probe).
                    match index.Columns with
                    | [] ->
                        mkDecision (UniqueIndexOutcome.DoNotEnforce NoCandidateProfiled)
                    | columnKey :: _ ->
                        match singleColumnProbe columnKey profile with
                        | Some (true, rowCount) ->
                            mkDecision
                                (UniqueIndexOutcome.EnforceUnique
                                    (SingleColumnNoDuplicates rowCount))
                        | Some (false, _) ->
                            mkDecision (UniqueIndexOutcome.DoNotEnforce DataHasDuplicates)
                        | None ->
                            mkDecision (UniqueIndexOutcome.DoNotEnforce NoCandidateProfiled)


    // -----------------------------------------------------------------------
    // Helpers for callers exploring the decision shape.
    // -----------------------------------------------------------------------

    /// True iff the decision enforces uniqueness on the index.
    let enforces (decision: UniqueIndexDecision) : bool =
        match decision.Outcome with
        | UniqueIndexOutcome.EnforceUnique _ -> true
        | UniqueIndexOutcome.DoNotEnforce _  -> false
