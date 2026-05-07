namespace Projection.Core

open System

/// How a profiling probe executed. `TrustedConstraint` means the probe was
/// skipped because the database constraint was trusted; in that case the
/// observed counts are not reliable and downstream consumers must respect
/// the outcome (a V2 cleanup of V1's conflation between probe execution
/// and observed reality).
type ProbeOutcome =
    | Succeeded
    | FallbackTimeout
    | Cancelled
    | TrustedConstraint
    | AmbiguousMapping


/// Metadata about a profiling probe's execution. The probe's outcome
/// distinguishes "we observed X" from "we trusted Y instead of observing."
type ProbeStatus = {
    CapturedAtUtc : DateTimeOffset
    SampleSize    : int64
    Outcome       : ProbeOutcome
}

[<RequireQualifiedAccess>]
module ProbeStatus =

    let private negativeSampleSize =
        ValidationError.create
            "probeStatus.sampleSize.negative"
            "Probe sample size must be non-negative."

    /// Construct a `ProbeStatus`. `sampleSize` must be non-negative.
    let create
        (capturedAtUtc: DateTimeOffset)
        (sampleSize: int64)
        (outcome: ProbeOutcome)
        : Result<ProbeStatus> =
        if sampleSize < 0L then Result.failureOf negativeSampleSize
        else
            Result.success
                { CapturedAtUtc = capturedAtUtc
                  SampleSize    = sampleSize
                  Outcome       = outcome }

    /// True iff the probe ran and yielded reliable observations.
    let isReliable (p: ProbeStatus) : bool =
        match p.Outcome with
        | Succeeded -> true
        | _         -> false


/// Per-column data-quality observation. Keyed by the Attribute's `SsKey`
/// so consumers look up by identity (A4), not by physical coordinate. The
/// Profiling Adapter performs (schema, table, column) → `SsKey` resolution
/// at the boundary using the Catalog as a lookup.
type ColumnProfile = {
    /// Identity of the Attribute whose evidence this is.
    AttributeKey         : SsKey
    /// Total rows observed in the kind's table.
    RowCount             : int64
    /// Rows where this column was NULL.
    NullCount            : int64
    /// Probe metadata.
    NullCountProbeStatus : ProbeStatus
}

[<RequireQualifiedAccess>]
module ColumnProfile =

    /// Fraction of observed rows that were NULL. `None` when no rows were
    /// observed (degenerate case; consumers default to conservative
    /// behavior).
    let nullPercentage (p: ColumnProfile) : decimal option =
        if p.RowCount = 0L then None
        else Some (decimal p.NullCount / decimal p.RowCount)

    /// True iff every observed row was NULL.
    let isAllNull (p: ColumnProfile) : bool =
        p.RowCount > 0L && p.NullCount = p.RowCount

    /// True iff no observed row was NULL.
    let isZeroNull (p: ColumnProfile) : bool =
        p.NullCount = 0L


/// Single-column uniqueness probe. Keyed by the Attribute's `SsKey`.
type UniqueCandidateProfile = {
    AttributeKey : SsKey
    HasDuplicate : bool
    ProbeStatus  : ProbeStatus
}


/// Multi-column uniqueness probe. Keyed by the Kind's `SsKey` plus the
/// participating Attribute `SsKey`s.
///
/// V2 adds `ProbeStatus` here; V1's `CompositeUniqueCandidateProfile`
/// lacked it (we did not know *when* composite uniqueness was checked or
/// *if* the probe succeeded). The masterwork's prescription that evidence
/// must be auditable demands the field; the cost is one extra record
/// constructor argument.
type CompositeUniqueCandidateProfile = {
    KindKey       : SsKey
    AttributeKeys : SsKey list
    HasDuplicate  : bool
    ProbeStatus   : ProbeStatus
}


/// Foreign-key referential-integrity observation. Keyed by the
/// Reference's `SsKey`.
type ForeignKeyReality = {
    ReferenceKey : SsKey
    HasOrphan    : bool
    OrphanCount  : int64
    /// True when the database constraint has the NO CHECK flag (declared
    /// but not enforced); orphan counts under this flag may mean the
    /// constraint was never validated.
    IsNoCheck    : bool
    ProbeStatus  : ProbeStatus
}

[<RequireQualifiedAccess>]
module ForeignKeyReality =

    /// True iff the FK has no observed orphans.
    let isClean (r: ForeignKeyReality) : bool = not r.HasOrphan


/// Categorical value frequencies — for attributes whose values are
/// drawn from a small or moderate vocabulary. The first
/// `AttributeDistribution` variant; landed in session 9 commit 2 as
/// the foundational rich-profiling evidence type (ADMIRE.md
/// 2026-05-12). Captures observed distinct values with their
/// occurrence counts. Truncation is explicit (the probe may have
/// capped the vocabulary at a configured limit).
///
/// `Frequencies` is sorted alphabetically by value at the IR level;
/// the adapter sorts on parse so consumers see deterministic
/// ordering regardless of probe-result order. Determinism matters
/// for T1 byte-identity (the same enrichment must produce the same
/// emitter output).
type CategoricalDistribution = {
    /// Identity of the Attribute whose distribution this is.
    AttributeKey  : SsKey
    /// Observed values with their occurrence counts. Sorted
    /// alphabetically by value for determinism.
    Frequencies   : (string * int64) list
    /// Total distinct values observed (≥ Frequencies.Length when
    /// truncated; equal when not).
    DistinctCount : int64
    /// True iff the probe capped the vocabulary at a limit;
    /// `Frequencies` is then a prefix of the full distribution.
    IsTruncated   : bool
    /// Probe metadata.
    ProbeStatus   : ProbeStatus
}


/// Numeric percentile + range evidence — for attributes whose values
/// are drawn from a numeric domain (Integer / Decimal). The second
/// `AttributeDistribution` variant; landed in session 10 commit 2 per
/// the rich-profiling agenda (ADMIRE.md 2026-05-12).
///
/// Captures the value range (`Min`, `Max`) plus a fixed set of
/// percentiles (P25, P50, P75, P95, P99). Histograms-as-binned-counts
/// are deliberately **not** included; the percentile + range shape is
/// robust to long tails (real-world numeric data is rarely uniform)
/// and supports the next consumers (Faker-style synthesis bounding
/// the synthesis space, anomaly detection comparing observed values
/// against percentile cutoffs). Histogram-as-third-variant lands later
/// if a consumer demands it (ADMIRE.md 2026-05-12 — IR grows under
/// evidence).
///
/// **Structural-commitment-via-construction-validation**
/// (AXIOMS.md 2026-05-12 — recognized operational principle).
/// `NumericDistribution.create` enforces:
///
///   - **Monotonicity chain.** `Min ≤ P25 ≤ P50 ≤ P75 ≤ P95 ≤ P99 ≤ Max`.
///     Percentiles must be sorted (a degenerate distribution where
///     `Min = Max` collapses to all-equal, which is permitted; an
///     out-of-order percentile is rejected).
///   - **Sample size floor.** `SampleSize ≥ 5`. With fewer than five
///     observations, the percentiles are degenerate (one or more
///     percentile boundaries coincide with the same observation) and
///     the consumer cannot reason meaningfully about them. The floor
///     is empirical, not statistical sufficiency — it's the smallest
///     count that makes each percentile a distinct boundary.
///   - **Sample size non-negativity.** Required by `ProbeStatus`
///     already, but `NumericDistribution.SampleSize` is a separate
///     field carrying its own non-negativity check for clarity.
///
/// Every constructed `NumericDistribution` value satisfies these
/// contracts; consumers pattern-match without re-validating.
type NumericDistribution = {
    /// Identity of the Attribute whose distribution this is.
    AttributeKey : SsKey
    /// Smallest observed value.
    Min          : decimal
    /// 25th percentile (lower quartile).
    P25          : decimal
    /// 50th percentile (median).
    P50          : decimal
    /// 75th percentile (upper quartile).
    P75          : decimal
    /// 95th percentile.
    P95          : decimal
    /// 99th percentile.
    P99          : decimal
    /// Largest observed value.
    Max          : decimal
    /// Number of observations the percentiles were drawn from. Lets
    /// consumers reason about confidence — high `SampleSize` means
    /// the percentiles are stable estimates; low `SampleSize` means
    /// they may shift with additional observations.
    SampleSize   : int64
    /// Probe metadata.
    ProbeStatus  : ProbeStatus
}


/// Empirical evidence about an attribute's value distribution.
/// **First IR extension surfacing V1 absence as the gap**
/// (ADMIRE.md 2026-05-12 — V2-growth admire mode). Closed DU; new
/// variants land under "IR grows under evidence" as their first
/// consumers arrive.
///
/// Variants:
///
///   - `Categorical` (session 9): per-value frequency evidence for
///     attributes with small / moderate vocabularies. Truncation
///     contract: `IsTruncated = false ⇒ DistinctCount =
///     Frequencies.Length`.
///   - `Numeric` (session 10): percentile + range evidence for
///     numeric-domain attributes. Monotonicity contract:
///     `Min ≤ P25 ≤ P50 ≤ P75 ≤ P95 ≤ P99 ≤ Max`.
///
/// Future variants (Temporal, Joint) arrive in subsequent sessions.
[<RequireQualifiedAccess>]
type AttributeDistribution =
    | Categorical of CategoricalDistribution
    | Numeric     of NumericDistribution


[<RequireQualifiedAccess>]
module CategoricalDistribution =

    let private negativeDistinctCount =
        ValidationError.create
            "categoricalDistribution.distinctCount.negative"
            "DistinctCount must be non-negative."

    let private negativeFrequencyCount =
        ValidationError.create
            "categoricalDistribution.frequencyCount.negative"
            "Per-value occurrence counts must be non-negative."

    let private truncationContradiction =
        ValidationError.create
            "categoricalDistribution.truncation.contradiction"
            "DistinctCount cannot exceed Frequencies.Length when IsTruncated is false."

    /// Construct a `CategoricalDistribution`. Validates:
    ///   - `DistinctCount ≥ 0`
    ///   - every per-value count is non-negative
    ///   - `IsTruncated = false` ⇒ `DistinctCount = Frequencies.Length`
    ///     (full vocabulary observed; the truncation flag must agree)
    /// Sorts `Frequencies` alphabetically by value to enforce
    /// determinism at the IR level.
    let create
        (attributeKey: SsKey)
        (frequencies: (string * int64) list)
        (distinctCount: int64)
        (isTruncated: bool)
        (probeStatus: ProbeStatus)
        : Result<CategoricalDistribution> =
        if distinctCount < 0L then
            Result.failureOf negativeDistinctCount
        elif frequencies |> List.exists (fun (_, c) -> c < 0L) then
            Result.failureOf negativeFrequencyCount
        elif (not isTruncated) && distinctCount <> int64 (List.length frequencies) then
            Result.failureOf truncationContradiction
        else
            let sorted = frequencies |> List.sortBy fst
            Result.success
                { AttributeKey  = attributeKey
                  Frequencies   = sorted
                  DistinctCount = distinctCount
                  IsTruncated   = isTruncated
                  ProbeStatus   = probeStatus }

    /// Total observed value occurrences (sum of frequency counts).
    /// May be less than the kind's `RowCount` when the probe sampled
    /// a subset; consumers compare against `ProbeStatus.SampleSize`
    /// to interpret coverage.
    let totalObservations (d: CategoricalDistribution) : int64 =
        d.Frequencies |> List.sumBy snd

    /// True iff the distribution captures every distinct value the
    /// probe observed (no truncation).
    let isComplete (d: CategoricalDistribution) : bool =
        not d.IsTruncated


[<RequireQualifiedAccess>]
module NumericDistribution =

    let private sampleSizeBelowFloor =
        ValidationError.create
            "numericDistribution.sampleSize.belowFloor"
            "SampleSize must be at least 5 — fewer observations make percentile boundaries degenerate."

    let private sampleSizeNegative =
        ValidationError.create
            "numericDistribution.sampleSize.negative"
            "SampleSize must be non-negative."

    let private percentilesNonMonotonic =
        ValidationError.create
            "numericDistribution.percentiles.nonMonotonic"
            "Percentiles must be monotonically non-decreasing: Min <= P25 <= P50 <= P75 <= P95 <= P99 <= Max."

    /// The minimum sample size that makes every percentile a distinct
    /// boundary. Below this floor the percentiles coincide with each
    /// other and the consumer cannot reason meaningfully about them.
    [<Literal>]
    let sampleSizeFloor : int64 = 5L

    /// Construct a `NumericDistribution`. Validates the structural
    /// commitments documented on the type:
    ///
    ///   - `SampleSize ≥ 0`
    ///   - `SampleSize ≥ sampleSizeFloor` (= 5)
    ///   - `Min ≤ P25 ≤ P50 ≤ P75 ≤ P95 ≤ P99 ≤ Max`
    ///
    /// Returns `Result<NumericDistribution>`; every successful value
    /// satisfies the contract by construction. Consumers
    /// pattern-match without re-validating.
    let create
        (attributeKey: SsKey)
        (min: decimal)
        (p25: decimal)
        (p50: decimal)
        (p75: decimal)
        (p95: decimal)
        (p99: decimal)
        (max: decimal)
        (sampleSize: int64)
        (probeStatus: ProbeStatus)
        : Result<NumericDistribution> =
        if sampleSize < 0L then
            Result.failureOf sampleSizeNegative
        elif sampleSize < sampleSizeFloor then
            Result.failureOf sampleSizeBelowFloor
        elif not (min <= p25 && p25 <= p50 && p50 <= p75
                  && p75 <= p95 && p95 <= p99 && p99 <= max) then
            Result.failureOf percentilesNonMonotonic
        else
            Result.success
                { AttributeKey = attributeKey
                  Min          = min
                  P25          = p25
                  P50          = p50
                  P75          = p75
                  P95          = p95
                  P99          = p99
                  Max          = max
                  SampleSize   = sampleSize
                  ProbeStatus  = probeStatus }

    /// Inter-quartile range (P75 - P25). Convenience for consumers
    /// that reason about spread; pure derivation, no caching.
    let interQuartileRange (d: NumericDistribution) : decimal =
        d.P75 - d.P25

    /// Range of observed values (Max - Min). The full extent of the
    /// distribution; consumers compare against `interQuartileRange` to
    /// reason about tail-heaviness.
    let observedRange (d: NumericDistribution) : decimal =
        d.Max - d.Min

    /// True iff every percentile coincides with `Min` (or
    /// equivalently with `Max`, since the monotonicity contract
    /// holds). A degenerate distribution where every observation was
    /// the same value.
    let isDegenerate (d: NumericDistribution) : bool =
        d.Min = d.Max


/// Empirical evidence aggregate. The third substantive input to
/// `Project = Π ∘ E` per the V2-amended A6. Independent of Catalog and
/// Policy per A34: this record references no Catalog or Policy types,
/// and its keys are opaque `SsKey`s that semantically refer to catalog
/// nodes without structurally depending on them.
///
/// `Profile.empty` is a first-class value; use cases that consume no
/// evidence (extract-model, schema-only) pass it as a no-op input.
///
/// **Distributions field added session 9** (ADMIRE.md 2026-05-12) —
/// rich-profiling evidence V1 does not collect. Empty by default;
/// populated by the V2-only `ProfileStatistics` sibling adapter.
type Profile = {
    Columns                   : ColumnProfile list
    UniqueCandidates          : UniqueCandidateProfile list
    CompositeUniqueCandidates : CompositeUniqueCandidateProfile list
    ForeignKeys               : ForeignKeyReality list
    Distributions             : AttributeDistribution list
}

[<RequireQualifiedAccess>]
module Profile =

    /// The empty Profile. A valid input for any pass; passes that consume
    /// Profile must produce sensible behavior on `Profile.empty`.
    let empty : Profile = {
        Columns                   = []
        UniqueCandidates          = []
        CompositeUniqueCandidates = []
        ForeignKeys               = []
        Distributions             = []
    }

    /// True iff the profile contains no observations of any kind.
    let isEmpty (p: Profile) : bool =
        List.isEmpty p.Columns
        && List.isEmpty p.UniqueCandidates
        && List.isEmpty p.CompositeUniqueCandidates
        && List.isEmpty p.ForeignKeys
        && List.isEmpty p.Distributions

    /// Look up a column profile by attribute identity. `None` if absent.
    let tryFindColumn (attributeKey: SsKey) (p: Profile) : ColumnProfile option =
        p.Columns |> List.tryFind (fun c -> c.AttributeKey = attributeKey)

    /// Look up a foreign-key reality by reference identity. `None` if absent.
    let tryFindForeignKey (referenceKey: SsKey) (p: Profile) : ForeignKeyReality option =
        p.ForeignKeys |> List.tryFind (fun fk -> fk.ReferenceKey = referenceKey)

    /// Look up a single-column uniqueness probe by attribute identity.
    let tryFindUnique (attributeKey: SsKey) (p: Profile) : UniqueCandidateProfile option =
        p.UniqueCandidates |> List.tryFind (fun u -> u.AttributeKey = attributeKey)

    /// Look up a categorical distribution by attribute identity.
    /// Returns `None` if no distribution evidence is registered for
    /// the attribute, or if the registered evidence is a different
    /// variant (e.g., Numeric).
    let tryFindCategorical (attributeKey: SsKey) (p: Profile) : CategoricalDistribution option =
        p.Distributions
        |> List.tryPick (fun d ->
            match d with
            | AttributeDistribution.Categorical cat when cat.AttributeKey = attributeKey ->
                Some cat
            | AttributeDistribution.Categorical _ -> None
            | AttributeDistribution.Numeric _ -> None)

    /// Look up a numeric distribution by attribute identity. Returns
    /// `None` if no distribution evidence is registered for the
    /// attribute, or if the registered evidence is a different
    /// variant (e.g., Categorical).
    let tryFindNumeric (attributeKey: SsKey) (p: Profile) : NumericDistribution option =
        p.Distributions
        |> List.tryPick (fun d ->
            match d with
            | AttributeDistribution.Numeric num when num.AttributeKey = attributeKey ->
                Some num
            | AttributeDistribution.Numeric _ -> None
            | AttributeDistribution.Categorical _ -> None)

    /// Look up *any* distribution evidence for an attribute, regardless
    /// of variant. Returns the first registered distribution whose key
    /// matches; `None` if no distribution evidence at all. Useful for
    /// consumers (the Distributions emitter is the first) that need to
    /// render whatever evidence exists without committing to one
    /// variant.
    ///
    /// When an attribute carries multiple distribution variants (an
    /// unusual case but not prevented at the type level), the first
    /// match in `Distributions` order wins. Callers that care about
    /// a specific variant should use `tryFindCategorical` /
    /// `tryFindNumeric` instead.
    let private distributionKey (d: AttributeDistribution) : SsKey =
        match d with
        | AttributeDistribution.Categorical cat -> cat.AttributeKey
        | AttributeDistribution.Numeric num     -> num.AttributeKey

    let tryFindDistribution (attributeKey: SsKey) (p: Profile) : AttributeDistribution option =
        p.Distributions
        |> List.tryFind (fun d -> distributionKey d = attributeKey)
