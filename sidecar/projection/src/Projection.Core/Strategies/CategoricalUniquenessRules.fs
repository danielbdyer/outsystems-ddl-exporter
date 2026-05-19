namespace Projection.Core

/// Why a per-attribute distribution-driven uniqueness suggestion was
/// emitted. Structured at the type level per the
/// structured-rationale-DU convention (DECISIONS 2026-05-09;
/// strategy-layer codification, DECISIONS 2026-05-11).
///
/// Single variant for now — the only "yes" outcome is when every
/// observation in the Categorical distribution was distinct
/// (vocabulary fully covered, no repeats). New variants land if a
/// future consumer requires finer evidence (e.g.,
/// `NearlyDistinct of distinctCount * sampleSize * tolerance` for
/// noisy data).
type CategoricalUniquenessEvidence =
    /// `distinctCount = totalObservations` — every observed value is
    /// unique. The strongest V2 unique-candidate signal: stronger than
    /// V1's `HasDuplicate = false` because it requires the full
    /// vocabulary to be distinct, not merely no-duplicates-observed.
    /// Truncated distributions never produce this evidence — the
    /// strategy declines on truncation per the signal hierarchy.
    | EveryValueDistinct of distinctCount: int64 * totalObservations: int64


/// Why a per-attribute uniqueness suggestion was withheld.
/// `RequireQualifiedAccess` per the session-8 codification refinement 1
/// (case names like `EvidenceMissing` recur across strategies;
/// qualification disambiguates).
[<RequireQualifiedAccess>]
type CategoricalUniquenessKeepReason =
    /// No Categorical distribution evidence registered for this
    /// attribute. The strategy cannot infer without observations.
    | NoCategoricalEvidence
    /// Categorical evidence's probe outcome is not reliable
    /// (FallbackTimeout / Cancelled / TrustedConstraint /
    /// AmbiguousMapping). V2's collapsed-mode strict default
    /// declines to suggest on missing evidence.
    | EvidenceMissing
    /// `IsTruncated = true` — the probe capped the vocabulary at a
    /// configured limit; the observed `distinctCount` is a lower
    /// bound. Without the full vocabulary the inference would be
    /// unsafe; the strategy declines.
    | VocabularyTruncated
    /// `distinctCount < MinDistinctCountForUniqueness`. Vocabulary
    /// too small to merit a uniqueness suggestion — the caller's
    /// floor reports the gate.
    | DistinctCountBelowThreshold of
        distinctCount: int64 *
        threshold: int64
    /// `distinctCount < totalObservations` — repeats observed in the
    /// sample. Direct contradiction of the unique-candidate hypothesis.
    | DuplicatesObserved of
        distinctCount: int64 *
        totalObservations: int64


/// The outcome of a single (attribute, intervention) decision.
///
/// Binary, mirroring `UniqueIndexOutcome` and `ForeignKeyOutcome`.
/// V2's distribution-driven inference produces a "suggest or don't"
/// answer; downstream consumers (emitters, future strategies) can
/// correlate with the per-index `UniqueIndexDecisionSet` from the
/// existing `UniqueIndexPass` for stronger signals.
///
/// `RequireQualifiedAccess` because `SuggestUnique` and
/// `DoNotSuggest` are intuitive names that may clash with other DUs
/// as the algebra grows.
[<RequireQualifiedAccess>]
type CategoricalUniquenessOutcome =
    | SuggestUnique of evidence: CategoricalUniquenessEvidence
    | DoNotSuggest  of reason:   CategoricalUniquenessKeepReason


/// One decision keyed to its attribute and the intervention that
/// produced it. Mirrors `NullabilityDecision` in shape; key is
/// `AttributeKey` (per-attribute granularity, like Nullability and
/// unlike UniqueIndex's per-index granularity — the architectural
/// variation surfaced in the admire, ADMIRE.md 2026-05-13).
type CategoricalUniquenessDecision = {
    AttributeKey   : SsKey
    Outcome        : CategoricalUniquenessOutcome
    InterventionId : string
}


/// The aggregate output of one or more CategoricalUniqueness
/// interventions running over a catalog. Empty when no interventions
/// are registered (the observable-identity-on-empty-policy commitment,
/// DECISIONS 2026-05-09).
type CategoricalUniquenessDecisionSet = {
    Decisions : CategoricalUniquenessDecision list
}


/// Domain rules for distribution-driven per-attribute uniqueness
/// inference — V2's first **distribution-aware strategy**
/// (ADMIRE.md 2026-05-13). The fourth registered-intervention
/// strategy under the codified strategy layer (DECISIONS 2026-05-11).
///
/// The pure pass (`CategoricalUniquenessPass`, session 11 commit 3)
/// walks the catalog's attributes and iterates over registered
/// CategoricalUniqueness interventions; per (attribute, intervention),
/// this module's `evaluate` returns one decision. The pass
/// accumulates decisions; the rules module decides each one.
///
/// **Strategy-layer codification (DECISIONS 2026-05-11).** Fourth
/// instance of the registered-intervention sub-pattern; the
/// codification prescribes the same shape as `NullabilityRules`,
/// `UniqueIndexRules`, and `ForeignKeyRules`:
///
///   - **Pure functions of IR fields.** No I/O, no mutable state.
///   - **Typed seam.** `evaluate` is the function the pass driver
///     calls into.
///   - **Structured rationale DUs** cover the decision space
///     exhaustively (Evidence, KeepReason, Outcome).
///   - **Module name advertises domain** — `CategoricalUniquenessRules`,
///     same `<Domain>Rules` suffix as the three predecessors.
///   - **Total decisions, named skips** (session 8 codification
///     refinement absorbed as fourth core prediction). Every
///     attribute receives a decision; "no evidence" surfaces as
///     `DoNotSuggest(NoCategoricalEvidence)`, not silent skip.
/// Typed structural display per CategoricalUniqueness DUs.
[<RequireQualifiedAccess>]
module CategoricalUniquenessEvidence =
    let toStructured (e: CategoricalUniquenessEvidence) : StructuredString =
        match e with
        | EveryValueDistinct (distinctCount, totalObservations) ->
            StructuredString.create "EveryValueDistinct"
                [
                    "distinctCount",     Inv.int64 distinctCount
                    "totalObservations", Inv.int64 totalObservations
                ]

    let toDiagnosticString (e: CategoricalUniquenessEvidence) : string =
        toStructured e |> StructuredString.render

[<RequireQualifiedAccess>]
module CategoricalUniquenessKeepReason =
    let toStructured (r: CategoricalUniquenessKeepReason) : StructuredString =
        match r with
        | CategoricalUniquenessKeepReason.NoCategoricalEvidence ->
            StructuredString.tag "NoCategoricalEvidence"
        | CategoricalUniquenessKeepReason.EvidenceMissing ->
            StructuredString.tag "EvidenceMissing"
        | CategoricalUniquenessKeepReason.VocabularyTruncated ->
            StructuredString.tag "VocabularyTruncated"
        | CategoricalUniquenessKeepReason.DistinctCountBelowThreshold (distinctCount, threshold) ->
            StructuredString.create "DistinctCountBelowThreshold"
                [
                    "distinctCount", Inv.int64 distinctCount
                    "threshold",     Inv.int64 threshold
                ]
        | CategoricalUniquenessKeepReason.DuplicatesObserved (distinctCount, totalObservations) ->
            StructuredString.create "DuplicatesObserved"
                [
                    "distinctCount",     Inv.int64 distinctCount
                    "totalObservations", Inv.int64 totalObservations
                ]

    let toDiagnosticString (r: CategoricalUniquenessKeepReason) : string =
        toStructured r |> StructuredString.render

[<RequireQualifiedAccess>]
module CategoricalUniquenessOutcome =
    let toStructured (o: CategoricalUniquenessOutcome) : StructuredString =
        match o with
        | CategoricalUniquenessOutcome.SuggestUnique e ->
            StructuredString.create "SuggestUnique"
                [ "evidence", CategoricalUniquenessEvidence.toDiagnosticString e ]
        | CategoricalUniquenessOutcome.DoNotSuggest r ->
            StructuredString.create "DoNotSuggest"
                [ "reason", CategoricalUniquenessKeepReason.toDiagnosticString r ]

    let toDiagnosticString (o: CategoricalUniquenessOutcome) : string =
        toStructured o |> StructuredString.render


[<RequireQualifiedAccess>]
module CategoricalUniquenessRules =

    /// Empty decision set — V2's strict default when no interventions
    /// are registered.
    let emptyDecisionSet : CategoricalUniquenessDecisionSet = { Decisions = [] }

    /// Decide a single (attribute, intervention) pair.
    ///
    /// The signal hierarchy (per ADMIRE.md 2026-05-13):
    ///   1. No Categorical evidence registered ⇒
    ///      `DoNotSuggest(NoCategoricalEvidence)`.
    ///   2. Probe unreliable ⇒
    ///      `DoNotSuggest(EvidenceMissing)`.
    ///   3. `IsTruncated = true` ⇒
    ///      `DoNotSuggest(VocabularyTruncated)`.
    ///   4. `distinctCount < MinDistinctCountForUniqueness` ⇒
    ///      `DoNotSuggest(DistinctCountBelowThreshold)`.
    ///   5. `distinctCount < totalObservations` ⇒
    ///      `DoNotSuggest(DuplicatesObserved)`.
    ///   6. Otherwise ⇒
    ///      `SuggestUnique(EveryValueDistinct)`.
    ///
    /// Total: every input combination yields one of the six
    /// outcomes. Per the "total decisions, named skips" core
    /// prediction (session 8 codification refinement 3).
    let evaluate
        (interventionId: string)
        (config: CategoricalUniquenessConfig)
        (attribute: Attribute)
        (profile: Profile)
        : CategoricalUniquenessDecision =
        use _ = Bench.scope "rules.categoricalUniqueness.evaluate"

        let mkDecision outcome : CategoricalUniquenessDecision =
            { AttributeKey   = attribute.SsKey
              Outcome        = outcome
              InterventionId = interventionId }

        match Profile.tryFindCategorical attribute.SsKey profile with
        | None ->
            // 1. No evidence at all.
            mkDecision
                (CategoricalUniquenessOutcome.DoNotSuggest
                    CategoricalUniquenessKeepReason.NoCategoricalEvidence)
        | Some cat when not (ProbeStatus.isReliable cat.ProbeStatus) ->
            // 2. Probe didn't succeed reliably.
            mkDecision
                (CategoricalUniquenessOutcome.DoNotSuggest
                    CategoricalUniquenessKeepReason.EvidenceMissing)
        | Some cat when cat.IsTruncated ->
            // 3. Vocabulary capped; full distinct count unknown.
            mkDecision
                (CategoricalUniquenessOutcome.DoNotSuggest
                    CategoricalUniquenessKeepReason.VocabularyTruncated)
        | Some cat when cat.DistinctCount < config.MinDistinctCountForUniqueness ->
            // 4. Vocabulary below the caller's floor.
            mkDecision
                (CategoricalUniquenessOutcome.DoNotSuggest
                    (CategoricalUniquenessKeepReason.DistinctCountBelowThreshold
                        (cat.DistinctCount, config.MinDistinctCountForUniqueness)))
        | Some cat ->
            // 5 & 6. Compare distinct count vs total observations.
            let totalObservations = CategoricalDistribution.totalObservations cat
            if cat.DistinctCount < totalObservations then
                mkDecision
                    (CategoricalUniquenessOutcome.DoNotSuggest
                        (CategoricalUniquenessKeepReason.DuplicatesObserved
                            (cat.DistinctCount, totalObservations)))
            else
                mkDecision
                    (CategoricalUniquenessOutcome.SuggestUnique
                        (EveryValueDistinct (cat.DistinctCount, totalObservations)))


    // -----------------------------------------------------------------------
    // Helpers for callers exploring the decision shape.
    // -----------------------------------------------------------------------

    /// True iff the decision suggests the attribute as a unique
    /// candidate. Convenience for emitters / consumers.
    let suggestsUnique (decision: CategoricalUniquenessDecision) : bool =
        match decision.Outcome with
        | CategoricalUniquenessOutcome.SuggestUnique _ -> true
        | CategoricalUniquenessOutcome.DoNotSuggest  _ -> false
