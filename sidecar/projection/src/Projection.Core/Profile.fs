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

    /// No-probe-ran shape — minimum-evidence default for smart
    /// constructors of Profile records. `CapturedAtUtc = MinValue`
    /// because Core has no clock (per the F# feature surface
    /// taxonomy — DateTime.Now is out of scope for Core); adapters
    /// at the boundary supply real timestamps when probes actually
    /// run. Extracted at chapter B.3 slice 3 cash-out per the
    /// two-consumer-threshold discipline (8 inlined sites → 1
    /// named primitive).
    let noProbeRun : ProbeStatus =
        { CapturedAtUtc = DateTimeOffset.MinValue
          SampleSize    = 0L
          Outcome       = Succeeded }

    /// Probe-ran-successfully shape parameterized on observed sample
    /// size. Used by LiveProfiler captures where `rowCount` is the
    /// number of rows examined. Sibling to `noProbeRun`; canonical
    /// for adapter sites where the probe completed.
    let observed (sampleSize: int64) : ProbeStatus =
        { CapturedAtUtc = DateTimeOffset.MinValue
          SampleSize    = sampleSize
          Outcome       = Succeeded }

    /// Probe-couldn't-execute shape — used when the target shape is
    /// structurally unmappable to the probe primitive (e.g.,
    /// composite-PK FK in slice B.3.1's per-Reference probe; a
    /// future composite-key extension cashes the deferral).
    /// `UniqueIndexRules.evaluate` + `ForeignKeyRules.evaluate`
    /// route AmbiguousMapping outcomes to `DoNotEnforce
    /// EvidenceMissing` — the conservative-safe behavior.
    let ambiguous : ProbeStatus =
        { CapturedAtUtc = DateTimeOffset.MinValue
          SampleSize    = 0L
          Outcome       = AmbiguousMapping }


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

    /// Smart constructor with the empirical-probe invariants:
    ///   * `RowCount ≥ 0`
    ///   * `NullCount ≥ 0`
    ///   * `NullCount ≤ RowCount`
    /// Per session-36 audit (Agent 3 #20) — `NullabilityRules`
    /// computes `NullCount / RowCount` without checking these
    /// invariants; constructing through `create` makes the
    /// strategy's preconditions structural.
    /// Invariant-culture `int64` projection. Per chapter 3.5 deep
    /// audit (2026-05-09): metadata values are typed `string`
    /// (`Map<string, string option>`); the projection from `int64`
    /// to its invariant-culture string form is the BCL `int64
    /// .ToString CultureInfo.InvariantCulture` primitive — the
    /// canonical numeric→string conversion. No concatenation.
    let private intInv (i: int64) : string =
        i.ToString System.Globalization.CultureInfo.InvariantCulture

    // Per-site analysis (chapter 3.5 deep audit, user's "hard line"
    // refinement): the prior implementation built the validation-
    // error message via `String.concat ""` — still string
    // concatenation by another name. The data-structure-oriented
    // alternative: the `ValidationError.Metadata` field
    // (`Map<string, string option>`) carries structured key-value
    // pairs; the `Message` text becomes a *static phrase* with no
    // value-interpolation. Operators reading the message see a
    // fixed sentence; programmatic consumers route by `Code` and
    // read structured values from `Metadata`. Zero concatenation.
    //
    // Trade-off: the message no longer inlines the offending value
    // ("RowCount must be ≥ 0; got -1." → "RowCount must be ≥ 0.").
    // Pattern-match consumers gain typed access; human readers can
    // still see the value via the metadata projection (e.g., a CLI
    // formatter that knows to display `Metadata["rowCount"]`
    // alongside the message).
    let create
        (attributeKey: SsKey)
        (rowCount: int64)
        (nullCount: int64)
        (probeStatus: ProbeStatus)
        : Result<ColumnProfile> =
        if rowCount < 0L then
            Result.failureOf
                (ValidationError.createWithMetadata
                    "columnProfile.rowCount.negative"
                    "RowCount must be ≥ 0."
                    (Map.ofList [
                        "rowCount", Some (intInv rowCount)
                    ]))
        elif nullCount < 0L then
            Result.failureOf
                (ValidationError.createWithMetadata
                    "columnProfile.nullCount.negative"
                    "NullCount must be ≥ 0."
                    (Map.ofList [
                        "nullCount", Some (intInv nullCount)
                    ]))
        elif nullCount > rowCount then
            Result.failureOf
                (ValidationError.createWithMetadata
                    "columnProfile.nullCount.exceedsRows"
                    "NullCount cannot exceed RowCount."
                    (Map.ofList [
                        "nullCount", Some (intInv nullCount)
                        "rowCount",  Some (intInv rowCount)
                    ]))
        else
            Result.success
                {
                    AttributeKey         = attributeKey
                    RowCount             = rowCount
                    NullCount            = nullCount
                    NullCountProbeStatus = probeStatus
                }

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

[<RequireQualifiedAccess>]
module UniqueCandidateProfile =

    /// Minimum-evidence default. `HasDuplicate = false` (no
    /// duplicates observed); `ProbeStatus` defaults to the no-probe-
    /// ran shape. Adapters override via record-update. Mirrors
    /// `AttributeReality.create` + `ForeignKeyReality.create`
    /// precedent per the chapter B.3 slice 3 cash-out.
    let create (attributeKey: SsKey) : UniqueCandidateProfile =
        {
            AttributeKey = attributeKey
            HasDuplicate = false
            ProbeStatus  = ProbeStatus.noProbeRun
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

[<RequireQualifiedAccess>]
module CompositeUniqueCandidateProfile =

    /// Minimum-evidence default. `HasDuplicate = false` (no
    /// duplicates observed); `ProbeStatus` defaults to the no-probe-
    /// ran shape. Adapters override via record-update. Mirrors
    /// `AttributeReality.create` + `ForeignKeyReality.create` +
    /// `UniqueCandidateProfile.create` precedent per the chapter B.3
    /// slice 3 cash-out.
    let create
        (kindKey: SsKey)
        (attributeKeys: SsKey list)
        : CompositeUniqueCandidateProfile =
        {
            KindKey       = kindKey
            AttributeKeys = attributeKeys
            HasDuplicate  = false
            ProbeStatus   = ProbeStatus.noProbeRun
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

    /// Minimum-evidence default. All boolean fields default to `false`
    /// (no orphans observed; constraint trusted) and `OrphanCount =
    /// 0L`. `ProbeStatus` is the no-probe-ran shape (CapturedAtUtc =
    /// MinValue; SampleSize = 0L; Outcome = Succeeded). The
    /// LiveProfiler adapter overrides via record-update once a real
    /// probe completes; `ProfileSnapshot.attach` overrides from V1's
    /// JSON snapshot. Mirrors the `AttributeReality.create` shape per
    /// the chapter B.3 slice 1 cash-out.
    let create (referenceKey: SsKey) : ForeignKeyReality =
        {
            ReferenceKey = referenceKey
            HasOrphan    = false
            OrphanCount  = 0L
            IsNoCheck    = false
            ProbeStatus  = ProbeStatus.noProbeRun
        }


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
/// First two statistical moments of a numeric distribution — Mean
/// (first moment; central tendency) and population standard
/// deviation (square root of the second central moment; spread).
/// The two travel together as an algebraic unit per
/// `DECISIONS 2026-05-19 (slice B.3.5)` — when one is meaningful
/// without the other, V2 has the wrong abstraction. Lands as
/// `NumericDistribution.Moments : StatisticalMoments option`
/// because LiveProfiler can compute them via SQL Server
/// `AVG` + `STDEVP` aggregates, but V1-JSON snapshots (the prior
/// distribution source) don't carry them. The optional carries the
/// presence of the evidence; the smart constructor `withMoments`
/// validates the within-range invariant.
///
/// **Per pillar 9: DataIntent.** Statistical moments are
/// observational evidence (computed from deployed reality without
/// operator opinion); they belong in `Profile`, not in `Policy`.
///
/// **Foundational evidence for the deferred Faker emitter.** Per
/// `ADMIRE.md` (V1 numeric-histograms → Faker Π → synthetic numeric
/// generation) + `DECISIONS Active deferrals — Faker emitter
/// (synthetic-data Π)`: μ + σ are the canonical moments a
/// shape-preserving synthetic-data generator consumes alongside the
/// percentile shape. Slice B.3.5 closes the V2 evidence-capture
/// gap; Faker's gating condition (third evidence type lands OR
/// concrete consumer demand) compounds toward firing.
type StatisticalMoments = {
    /// Population mean (μ) — sum of observed values divided by sample size.
    Mean   : decimal
    /// Population standard deviation (σ) — square root of the average
    /// squared deviation from the mean. Non-negative by definition.
    StdDev : decimal
}

[<RequireQualifiedAccess>]
module StatisticalMoments =

    let private negativeStdDev =
        ValidationError.create
            "statisticalMoments.stdDev.negative"
            "StdDev must be non-negative."

    /// Construct a `StatisticalMoments`. Validates `StdDev ≥ 0`
    /// (the structural invariant — standard deviation of any
    /// observed distribution is non-negative by definition). `Mean`
    /// has no by-itself invariant; the within-range check
    /// (`Min ≤ Mean ≤ Max`) lives at `NumericDistribution.withMoments`
    /// because it requires the distribution's bounds.
    let create (mean: decimal) (stdDev: decimal) : Result<StatisticalMoments> =
        if stdDev < 0M then Result.failureOf negativeStdDev
        else Result.success { Mean = mean; StdDev = stdDev }


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
    /// First two statistical moments (Mean + StdDev). `None` when
    /// the source (V1-JSON snapshot) didn't carry them; `Some` when
    /// LiveProfiler probed them via `AVG` + `STDEVP` aggregates.
    /// Added at slice B.3.5; enriches the distribution beyond the
    /// percentile-shape with central-tendency + spread evidence
    /// foundational for shape-preserving synthetic data generation
    /// (the deferred Faker emitter consumes both this and the
    /// percentile field set).
    Moments      : StatisticalMoments option
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
                  Moments      = None
                  ProbeStatus  = probeStatus }

    let private meanOutOfRange =
        ValidationError.create
            "numericDistribution.mean.outOfRange"
            "Mean must lie within [Min, Max] — central tendency cannot fall outside the observed range."

    /// Enrich an existing distribution with `StatisticalMoments`.
    /// Validates the within-range invariant `Min ≤ Mean ≤ Max`
    /// (the by-itself `StdDev ≥ 0` invariant is enforced by
    /// `StatisticalMoments.create`). Returns a `NumericDistribution`
    /// with `Moments = Some moments`; the consumer chooses whether
    /// to read the moments alongside the percentile shape.
    ///
    /// Slice B.3.5 cash-out — LiveProfiler probes `AVG` + `STDEVP`
    /// per numeric attribute and threads the result through this
    /// enrichment. V1-JSON adapter path leaves `Moments = None`
    /// (V1's snapshot doesn't carry μ + σ).
    let withMoments
        (moments: StatisticalMoments)
        (dist: NumericDistribution)
        : Result<NumericDistribution> =
        if moments.Mean < dist.Min || moments.Mean > dist.Max then
            Result.failureOf meanOutOfRange
        else
            Result.success { dist with Moments = Some moments }

    /// Inter-quartile range (P75 - P25). Convenience for consumers
    /// that reason about spread; pure derivation, no caching.
    let interQuartileRange (d: NumericDistribution) : decimal =
        d.P75 - d.P25

    /// Range of observed values (Max - Min). The full extent of the
    /// distribution; consumers compare against `interQuartileRange` to
    /// reason about tail-heaviness.
    let observedRange (d: NumericDistribution) : decimal =
        d.Max - d.Min

    /// Coefficient of variation (σ / μ) — the dimensionless ratio of
    /// spread to central tendency. Common in synthetic-data quality
    /// scoring and anomaly detection. Returns `None` when moments
    /// are unavailable OR when Mean is zero (CV is undefined for
    /// zero-centered data). Per
    /// `DECISIONS 2026-05-19 (slice B.3.5)` — exposed as a pure
    /// derivation so consumers can reason about distribution shape
    /// without a separate IR field; lifts via `withMoments` evidence.
    let coefficientOfVariation (d: NumericDistribution) : decimal option =
        match d.Moments with
        | Some m when m.Mean <> 0M -> Some (m.StdDev / m.Mean)
        | _ -> None

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
///
/// **CdcAwareness field added at chapter 4.1.B slice β** — per
/// `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md` §4.2: CDC-enabled status
/// is *evidence the deployed schema carries*, not intent (operator
/// did not pick CDC; they enabled it on production via a separate
/// system). A34 holds: CdcAwareness lives on Profile, not Policy.

/// Per-table CDC discovery evidence consumed by data-emission
/// triumvirate emitters. `CdcEnabled` is the set of kinds whose
/// physical realization carries CDC capture; `CdcInstance` carries
/// the capture-instance name when an emitter needs to emit it
/// (e.g., for the integration-test `cdc.fn_cdc_get_all_changes_<
/// instance>` query in the slice-γ canary).
type CdcAwareness =
    {
        CdcEnabled  : Set<SsKey>
        CdcInstance : Map<SsKey, string>
    }

/// Operations on `CdcAwareness`. Per A34 (Profile-shape orthogonality):
/// `empty` is the no-evidence value; passes that don't read CDC
/// produce identical output for `empty` and any populated value.
[<RequireQualifiedAccess>]
module CdcAwareness =

    /// No CDC evidence. Equivalent to "the deployed schema has CDC
    /// enabled on zero kinds" — V2 emit takes the V1-shape MERGE on
    /// every kind (no change-detection predicate).
    let empty : CdcAwareness =
        { CdcEnabled = Set.empty; CdcInstance = Map.empty }

    /// True iff the kind's physical realization carries CDC capture.
    let isEnabled (key: SsKey) (c: CdcAwareness) : bool =
        Set.contains key c.CdcEnabled

    /// The capture-instance name for the kind, if any. Used by the
    /// chapter 4.1.B canary's `cdc.fn_cdc_get_all_changes_<instance>`
    /// query to verify the change-detection predicate's silence.
    let captureInstance (key: SsKey) (c: CdcAwareness) : string option =
        Map.tryFind key c.CdcInstance

    /// Construct from an enabled-key set and an instance map. The
    /// invariant — every key in `instances` should also be in
    /// `enabled` — is not enforced structurally; it is the adapter's
    /// responsibility (per the chapter-3.1 read-side adapter
    /// extension at slice γ).
    let create (enabled: Set<SsKey>) (instances: Map<SsKey, string>) : CdcAwareness =
        { CdcEnabled = enabled; CdcInstance = instances }

/// **SourceUsers / TargetUsers fields added at chapter 4.2 slice β** —
/// per `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` §4: per-environment user
/// populations are *empirical* evidence (what users actually exist in
/// each environment), not structural (the user kind exists once in
/// Catalog) and not intent (Policy.UserMatching carries the matching
/// strategy). A34 holds: passes that don't read user populations
/// produce identical output for `UserPopulation.empty` and any
/// populated value. Typed parameterization
/// (`UserPopulation<SourceUserId>` vs `UserPopulation<TargetUserId>`)
/// extends slice α's identity-orientation safety to the population
/// level.
/// Per-attribute deployed-target reflection — V2's mirror of V1's
/// `AttributeReality.cs`. Carries the five reflection axes the
/// LiveProfiler captures by probing the deployed SQL Server:
///
///   - `IsNullableInDatabase` — `sys.columns.is_nullable`. May differ
///     from `Attribute.Column.IsNullable` (which reflects the OS
///     logical model). The deployed reality is operator-meaningful
///     when remediation passes decide whether to enforce NOT NULL.
///   - `HasNulls` — `EXISTS (… WHERE col IS NULL)` evidence. Drives
///     `NullabilityRules`'s mandatory-relaxation decision.
///   - `HasDuplicates` — `EXISTS (… GROUP BY col HAVING COUNT > 1)`.
///     Drives `UniqueIndexRules`'s candidate-unique-index gating.
///   - `HasOrphans` — `EXISTS (FK source WHERE PK target absent)`.
///     Drives `ForeignKeyRules`'s constraint-enforcement decision.
///   - `IsPresentButInactive` — physical column exists at the
///     deployed target AND the OS logical attribute is `IsActive
///     = false`. Drives remediation-emitter guidance ("inactive
///     column with deployed data; consider preserving").
///
/// Per A18 amended + A34: `AttributeReality` is Profile-resident
/// evidence (observation, not policy). Tightening passes consume it
/// to refine their `Outcome` variants; emitters never read it
/// directly (Profile reaches emitters only via the
/// `EmitterWithProfile<'a>` wide signature).
///
/// Per pillar 9: this entire surface is DataIntent — V2's deployed-
/// target reflection has no operator-overlay role. The LiveProfiler
/// captures by probing; the rowset path (`OssysColumnRealityRow`)
/// surfaces a partial subset (`IsNullableInDatabase` only) when V1's
/// source-side adapter runs. The full live-probe payload comes from
/// `Projection.Adapters.Sql.LiveProfiler`.
///
/// Matrix row 49 cash-out (slice A.4.7'-prelude.live-profiler,
/// 2026-05-19). V1 source: `AttributeReality.cs` (5 reflection fields).
type AttributeReality = {
    /// Identity of the Attribute whose deployed reflection this is.
    /// Per A4 (identity-by-SsKey): consumers look up by SsKey, not
    /// by (schema, table, column) coordinate.
    AttributeKey         : SsKey
    /// Deployed-target nullability (`sys.columns.is_nullable`). `true`
    /// when the column is declared NULL at the deployed target.
    IsNullableInDatabase : bool
    /// Deployed evidence: at least one row carries NULL in this column.
    /// `false` when no row has NULL (the column behaves NOT NULL in
    /// practice regardless of its declared nullability).
    HasNulls             : bool
    /// Deployed evidence: at least one value appears in two or more
    /// rows. `false` when every observed value is distinct (the
    /// column behaves uniquely in practice).
    HasDuplicates        : bool
    /// Deployed evidence: at least one FK source row references a
    /// target PK value that doesn't exist at the deployed target.
    /// `false` when every FK source row resolves to a present
    /// target row. Computed per-Reference where the attribute
    /// participates as `SourceAttribute`; aggregated to the
    /// attribute level by `Profile.AttributeReality.create`.
    HasOrphans           : bool
    /// Deployed evidence: the physical column exists at the deployed
    /// target AND the corresponding OS logical attribute carries
    /// `IsActive = false`. `false` when either the column is absent
    /// (no deployed presence to flag) or the attribute is active
    /// (no inactive-state to flag).
    IsPresentButInactive : bool
}

[<RequireQualifiedAccess>]
module AttributeReality =

    /// Build an `AttributeReality` with minimum-evidence defaults.
    /// All bool fields default to `false` (the no-evidence-yet
    /// shape — the LiveProfiler hasn't probed; downstream consumers
    /// must treat absent reality as no-evidence rather than positive
    /// evidence). Required: `attributeKey`. Consumers override via
    /// record-update.
    let create (attributeKey: SsKey) : AttributeReality =
        {
            AttributeKey         = attributeKey
            IsNullableInDatabase = false
            HasNulls             = false
            HasDuplicates        = false
            HasOrphans           = false
            IsPresentButInactive = false
        }

/// Slice B.3.8 — FK fan-out cardinality. Per Reference, the
/// distribution of "how many children per parent." Computed by
/// grouping source FK values, counting per-value, then summarizing
/// the count distribution. Shape-preserving synthetic data uses
/// this to preserve real-world FK load skew (e.g., "Customer 42
/// has 1000 orders; most customers have 5"). Foundation evidence
/// for the deferred Faker emitter per `ADMIRE.md` joint-FK chain.
type ForeignKeyCardinality = {
    ReferenceKey           : SsKey
    /// Distribution of child-count-per-parent values. Min/Max
    /// bound the range; percentiles + Moments characterize the
    /// shape (uniform spread vs power-law clumping).
    ChildCountDistribution : NumericDistribution
}

[<RequireQualifiedAccess>]
module ForeignKeyCardinality =

    /// Build a `ForeignKeyCardinality` from a per-Reference child-
    /// count distribution. Returns `None` when there are fewer than
    /// 5 distinct parent values (the `NumericDistribution`
    /// SampleSize floor).
    let create (referenceKey: SsKey) (distribution: NumericDistribution) : ForeignKeyCardinality =
        { ReferenceKey = referenceKey; ChildCountDistribution = distribution }


/// Slice B.3.8 — FK selectivity / clumping. Per Reference, value-
/// frequency of the source FK column. Captures which target PK
/// values dominate the children (skew analysis), informing shape-
/// preserving synthetic FK distribution. Mirrors
/// `CategoricalDistribution` shape but keyed by Reference (the
/// source FK column is typically numeric; serializing to string
/// for tally bridges the type-heterogeneity).
type ForeignKeySelectivity = {
    ReferenceKey  : SsKey
    /// (Target-PK-value string, frequency-in-source-FK). Sorted
    /// count-DESC + value-ASC for determinism.
    Frequencies   : (string * int64) list
    DistinctCount : int64
    IsTruncated   : bool
    ProbeStatus   : ProbeStatus
}

[<RequireQualifiedAccess>]
module ForeignKeySelectivity =

    let private negativeDistinctCount =
        ValidationError.create
            "foreignKeySelectivity.distinctCount.negative"
            "DistinctCount must be non-negative."

    let private negativeFrequencyCount =
        ValidationError.create
            "foreignKeySelectivity.frequencyCount.negative"
            "Per-value frequency counts must be non-negative."

    let private truncationContradiction =
        ValidationError.create
            "foreignKeySelectivity.truncation.contradiction"
            "DistinctCount cannot exceed Frequencies.Length when IsTruncated is false."

    /// Construct a `ForeignKeySelectivity`. Same invariants as
    /// `CategoricalDistribution.create`: distinct count
    /// non-negative; per-value counts non-negative; truncation
    /// flag must agree with full-vocabulary state.
    let create
        (referenceKey: SsKey)
        (frequencies: (string * int64) list)
        (distinctCount: int64)
        (isTruncated: bool)
        (probeStatus: ProbeStatus)
        : Result<ForeignKeySelectivity> =
        if distinctCount < 0L then Result.failureOf negativeDistinctCount
        elif frequencies |> List.exists (fun (_, c) -> c < 0L) then
            Result.failureOf negativeFrequencyCount
        elif (not isTruncated) && distinctCount <> int64 (List.length frequencies) then
            Result.failureOf truncationContradiction
        else
            Result.success
                { ReferenceKey  = referenceKey
                  Frequencies   = frequencies
                  DistinctCount = distinctCount
                  IsTruncated   = isTruncated
                  ProbeStatus   = probeStatus }


/// Slice B.3.8 — multi-FK joint distribution. Per Kind with ≥2
/// References, co-occurrence counts across the kind's FK columns.
/// E.g., in `Orders` with FKs `(CustomerId, RegionId)`, the joint
/// frequencies reveal which customer+region pairs co-occur and
/// at what rate. Synthesizing FKs independently would lose this
/// coherence; the joint distribution preserves it. Foundation
/// evidence for the Faker emitter's "coherent synthetic data
/// across relationships" capability per `ADMIRE.md`.
type JointDistribution = {
    KindKey       : SsKey
    /// Ordered tuple of attribute keys participating in the joint.
    /// Order matters for tuple-key construction; the consumer
    /// reads the same order to interpret the serialized values.
    AttributeKeys : SsKey list
    /// (Serialized tuple key, frequency). Sorted count-DESC + key-ASC.
    Frequencies   : (string * int64) list
    DistinctCount : int64
    IsTruncated   : bool
    ProbeStatus   : ProbeStatus
}

[<RequireQualifiedAccess>]
module JointDistribution =

    let private negativeDistinctCount =
        ValidationError.create
            "jointDistribution.distinctCount.negative"
            "DistinctCount must be non-negative."

    let private negativeFrequencyCount =
        ValidationError.create
            "jointDistribution.frequencyCount.negative"
            "Per-tuple frequency counts must be non-negative."

    let private truncationContradiction =
        ValidationError.create
            "jointDistribution.truncation.contradiction"
            "DistinctCount cannot exceed Frequencies.Length when IsTruncated is false."

    let private degenerateTuple =
        ValidationError.create
            "jointDistribution.attributeKeys.tooFew"
            "JointDistribution must span at least 2 attribute keys; single-attribute distributions belong on AttributeDistribution."

    /// Construct a `JointDistribution`. Validates: distinct count
    /// non-negative; per-tuple counts non-negative; truncation flag
    /// agrees with vocabulary state; AttributeKeys length ≥ 2
    /// (single-attribute joints should use `CategoricalDistribution`).
    let create
        (kindKey: SsKey)
        (attributeKeys: SsKey list)
        (frequencies: (string * int64) list)
        (distinctCount: int64)
        (isTruncated: bool)
        (probeStatus: ProbeStatus)
        : Result<JointDistribution> =
        if List.length attributeKeys < 2 then Result.failureOf degenerateTuple
        elif distinctCount < 0L then Result.failureOf negativeDistinctCount
        elif frequencies |> List.exists (fun (_, c) -> c < 0L) then
            Result.failureOf negativeFrequencyCount
        elif (not isTruncated) && distinctCount <> int64 (List.length frequencies) then
            Result.failureOf truncationContradiction
        else
            Result.success
                { KindKey       = kindKey
                  AttributeKeys = attributeKeys
                  Frequencies   = frequencies
                  DistinctCount = distinctCount
                  IsTruncated   = isTruncated
                  ProbeStatus   = probeStatus }


type Profile = {
    Columns                   : ColumnProfile list
    UniqueCandidates          : UniqueCandidateProfile list
    CompositeUniqueCandidates : CompositeUniqueCandidateProfile list
    ForeignKeys               : ForeignKeyReality list
    Distributions             : AttributeDistribution list
    /// Per-attribute deployed-target reflection. Empty when the
    /// LiveProfiler hasn't run (chapter 4.1.B / chapter 5.4.δ
    /// trigger condition). Tightening passes consume this evidence
    /// to refine their decisions; emitters never read it directly.
    /// Slice A.4.7'-prelude.live-profiler (2026-05-19; matrix row 49
    /// cash-out).
    AttributeRealities        : AttributeReality list
    CdcAwareness              : CdcAwareness
    SourceUsers               : UserPopulation<SourceUserId>
    TargetUsers               : UserPopulation<TargetUserId>
    /// Slice B.3.8 — per-Reference fan-out cardinality (distribution
    /// of child-count-per-parent values). Shape-preserving synthetic
    /// data uses this to preserve FK load skew. Foundation evidence
    /// for the deferred Faker emitter.
    ForeignKeyCardinalities   : ForeignKeyCardinality list
    /// Slice B.3.8 — per-Reference selectivity / clumping
    /// (value-frequency over source FK column).
    ForeignKeySelectivities   : ForeignKeySelectivity list
    /// Slice B.3.8 — per-Kind joint distributions across the kind's
    /// FK columns (≥2). Preserves coherent FK co-occurrence for
    /// shape-preserving synthetic data.
    JointDistributions        : JointDistribution list
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
        ForeignKeyCardinalities   = []
        ForeignKeySelectivities   = []
        JointDistributions        = []
        AttributeRealities        = []
        CdcAwareness              = CdcAwareness.empty
        SourceUsers               = UserPopulation.empty
        TargetUsers               = UserPopulation.empty
    }

    /// True iff the profile contains no observations of any kind.
    let isEmpty (p: Profile) : bool =
        List.isEmpty p.Columns
        && List.isEmpty p.UniqueCandidates
        && List.isEmpty p.CompositeUniqueCandidates
        && List.isEmpty p.ForeignKeys
        && List.isEmpty p.Distributions
        && List.isEmpty p.ForeignKeyCardinalities
        && List.isEmpty p.ForeignKeySelectivities
        && List.isEmpty p.JointDistributions
        && Set.isEmpty p.CdcAwareness.CdcEnabled
        && Map.isEmpty p.CdcAwareness.CdcInstance
        && UserPopulation.isEmpty p.SourceUsers
        && UserPopulation.isEmpty p.TargetUsers

    /// Look up a column profile by attribute identity. `None` if absent.
    let tryFindColumn (attributeKey: SsKey) (p: Profile) : ColumnProfile option =
        p.Columns |> List.tryFind (fun c -> c.AttributeKey = attributeKey)

    /// Look up a foreign-key reality by reference identity. `None` if absent.
    let tryFindForeignKey (referenceKey: SsKey) (p: Profile) : ForeignKeyReality option =
        p.ForeignKeys |> List.tryFind (fun fk -> fk.ReferenceKey = referenceKey)

    /// Look up FK cardinality evidence by reference identity (H-024).
    /// Returns `None` when the LiveProfiler did not probe this reference
    /// or when `Profile.isEmpty` (the common non-profiled path).
    let tryFindForeignKeyCardinality (referenceKey: SsKey) (p: Profile) : ForeignKeyCardinality option =
        p.ForeignKeyCardinalities |> List.tryFind (fun c -> c.ReferenceKey = referenceKey)

    /// Look up FK selectivity evidence by reference identity (H-025).
    let tryFindForeignKeySelectivity (referenceKey: SsKey) (p: Profile) : ForeignKeySelectivity option =
        p.ForeignKeySelectivities |> List.tryFind (fun s -> s.ReferenceKey = referenceKey)

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

    // --------------------------------------------------------------------
    // Slice B.3.7 — multi-environment merge.
    //
    // `Profile.merge` combines two profiles into one carrying the
    // conservative (worst-case) observation across both. Used by the
    // multi-environment orchestrator (dev + UAT + prod evidence
    // unioned for cutover-risk scoring; per matrix row 92's V1
    // `MultiTargetSqlDataProfiler` shape).
    //
    // **Algebraic laws (FsCheck-property-tested):**
    //   - Commutative: `merge a b = merge b a`
    //   - Associative: `merge (merge a b) c = merge a (merge b c)`
    //   - Left identity: `merge Profile.empty p = p`
    //   - Right identity: `merge p Profile.empty = p`
    //
    // **Worst-case per-axis aggregation** (each operator is
    // independently commutative + associative):
    //
    // | Axis | Operator |
    // |---|---|
    // | `AttributeReality.HasNulls / HasDuplicates / HasOrphans / IsNullableInDatabase / IsPresentButInactive` | OR (any env observing the witness lifts the union) |
    // | `ColumnProfile.RowCount` | MAX (largest sample) |
    // | `ColumnProfile.NullCount` | MAX (worst-case null cardinality) |
    // | `UniqueCandidateProfile.HasDuplicate` | OR |
    // | `CompositeUniqueCandidateProfile.HasDuplicate` | OR |
    // | `ForeignKeyReality.HasOrphan` | OR |
    // | `ForeignKeyReality.OrphanCount` | MAX |
    // | `ForeignKeyReality.IsNoCheck` | OR |
    // | `NumericDistribution` / `CategoricalDistribution` | choose larger `SampleSize` (most evidence wins) |
    // | `ProbeStatus.SampleSize` | MAX |
    // | `ProbeStatus.Outcome` | `Succeeded` if either succeeded; else first non-success |
    // | `CdcAwareness` | Set.union over enabled; Map.union over instances |
    // | `UserPopulation` | merge by SourceUser/TargetUser keys |

    let private mergeProbeStatus (a: ProbeStatus) (b: ProbeStatus) : ProbeStatus =
        let outcome =
            match a.Outcome, b.Outcome with
            | Succeeded, _ | _, Succeeded -> Succeeded
            | other, _                    -> other
        { CapturedAtUtc = if a.CapturedAtUtc >= b.CapturedAtUtc then a.CapturedAtUtc else b.CapturedAtUtc
          SampleSize    = max a.SampleSize b.SampleSize
          Outcome       = outcome }

    let private mergeColumnProfile (a: ColumnProfile) (b: ColumnProfile) : ColumnProfile =
        { AttributeKey         = a.AttributeKey
          RowCount             = max a.RowCount b.RowCount
          NullCount            = max a.NullCount b.NullCount
          NullCountProbeStatus = mergeProbeStatus a.NullCountProbeStatus b.NullCountProbeStatus }

    let private mergeUniqueCandidate (a: UniqueCandidateProfile) (b: UniqueCandidateProfile) : UniqueCandidateProfile =
        { AttributeKey = a.AttributeKey
          HasDuplicate = a.HasDuplicate || b.HasDuplicate
          ProbeStatus  = mergeProbeStatus a.ProbeStatus b.ProbeStatus }

    let private mergeCompositeUniqueCandidate
        (a: CompositeUniqueCandidateProfile)
        (b: CompositeUniqueCandidateProfile)
        : CompositeUniqueCandidateProfile =
        { KindKey       = a.KindKey
          AttributeKeys = a.AttributeKeys
          HasDuplicate  = a.HasDuplicate || b.HasDuplicate
          ProbeStatus   = mergeProbeStatus a.ProbeStatus b.ProbeStatus }

    let private mergeForeignKeyReality (a: ForeignKeyReality) (b: ForeignKeyReality) : ForeignKeyReality =
        { ReferenceKey = a.ReferenceKey
          HasOrphan    = a.HasOrphan || b.HasOrphan
          OrphanCount  = max a.OrphanCount b.OrphanCount
          IsNoCheck    = a.IsNoCheck || b.IsNoCheck
          ProbeStatus  = mergeProbeStatus a.ProbeStatus b.ProbeStatus }

    let private mergeAttributeReality (a: AttributeReality) (b: AttributeReality) : AttributeReality =
        { AttributeKey         = a.AttributeKey
          IsNullableInDatabase = a.IsNullableInDatabase || b.IsNullableInDatabase
          HasNulls             = a.HasNulls || b.HasNulls
          HasDuplicates        = a.HasDuplicates || b.HasDuplicates
          HasOrphans           = a.HasOrphans || b.HasOrphans
          IsPresentButInactive = a.IsPresentButInactive || b.IsPresentButInactive }

    /// For numeric/categorical distributions, "more evidence wins."
    /// Tie-break is irrelevant for merge correctness (commutativity
    /// holds because if SampleSize equal AND values differ, the law
    /// only matters when consumers can distinguish — and the consumer
    /// reads `tryFindNumeric` which is positional within the list).
    /// We pick `b` on tie so swap(a, b) yields the same answer when
    /// a ≡ b (which is the only case commutativity matters here).
    let private mergeDistribution (a: AttributeDistribution) (b: AttributeDistribution) : AttributeDistribution =
        let sampleSize (d: AttributeDistribution) : int64 =
            match d with
            | AttributeDistribution.Numeric n     -> n.SampleSize
            | AttributeDistribution.Categorical c -> c.DistinctCount
        if sampleSize a >= sampleSize b then a else b

    /// Generic "merge two keyed lists by key" combinator. Used across
    /// per-attribute / per-reference / per-kind axes. Commutativity:
    /// `unionBy keyOf merge a b = unionBy keyOf merge b a` holds when
    /// `merge` is commutative AND `keyOf` is consistent. Associativity
    /// similarly inherits from `merge`'s associativity.
    let private unionBy
        (keyOf: 'a -> 'k)
        (merge: 'a -> 'a -> 'a)
        (xs: 'a list)
        (ys: 'a list)
        : 'a list when 'k : comparison =
        // Index ys for O(log n) lookup, then walk xs (preserving order),
        // then append any ys not matched. Final ordering: keys-in-xs
        // first (in their original order), then keys-only-in-ys.
        let yIndex =
            ys
            |> List.map (fun y -> keyOf y, y)
            |> Map.ofList
        let mutable consumedYKeys = Set.empty
        let mergedFromXs =
            xs
            |> List.map (fun x ->
                let key = keyOf x
                match Map.tryFind key yIndex with
                | Some y ->
                    consumedYKeys <- Set.add key consumedYKeys
                    merge x y
                | None -> x)
        let extraFromYs =
            ys
            |> List.filter (fun y -> not (Set.contains (keyOf y) consumedYKeys))
        mergedFromXs @ extraFromYs

    /// Composite key for `CompositeUniqueCandidateProfile` — combines
    /// KindKey + sorted-set-of-AttributeKeys for order-independent
    /// equality.
    let private compositeKey (c: CompositeUniqueCandidateProfile) : SsKey * Set<SsKey> =
        c.KindKey, Set.ofList c.AttributeKeys

    /// Distribution key — AttributeKey + variant tag (so a Numeric
    /// and Categorical distribution for the SAME attribute coexist
    /// rather than colliding).
    let private distributionMergeKey (d: AttributeDistribution) : SsKey * string =
        match d with
        | AttributeDistribution.Numeric n     -> n.AttributeKey, "Numeric"
        | AttributeDistribution.Categorical c -> c.AttributeKey, "Categorical"

    // Slice B.3.8 — merge helpers for FK correlation evidence.

    let private mergeFkCardinality
        (a: ForeignKeyCardinality)
        (b: ForeignKeyCardinality)
        : ForeignKeyCardinality =
        // Pick the entry with larger SampleSize (more evidence wins)
        // — commutative on a ≡ b; associative because pick-larger
        // is a monoid.
        if a.ChildCountDistribution.SampleSize >= b.ChildCountDistribution.SampleSize then a else b

    let private mergeFkSelectivity
        (a: ForeignKeySelectivity)
        (b: ForeignKeySelectivity)
        : ForeignKeySelectivity =
        if a.DistinctCount >= b.DistinctCount then a else b

    let private mergeJointDistribution
        (a: JointDistribution)
        (b: JointDistribution)
        : JointDistribution =
        if a.DistinctCount >= b.DistinctCount then a else b

    /// Joint-distribution merge key — KindKey + ordered AttributeKeys.
    let private jointKey (j: JointDistribution) : SsKey * SsKey list =
        j.KindKey, j.AttributeKeys

    /// Merge two profiles into a worst-case union per axis. Both
    /// inputs are valid Profile values; the result is a valid
    /// Profile value whose evidence is the conservative aggregation
    /// across both. Commutative + associative; `Profile.empty` is
    /// the identity. FsCheck property tests verify.
    let merge (a: Profile) (b: Profile) : Profile =
        {
            Columns                   =
                unionBy (fun c -> c.AttributeKey) mergeColumnProfile a.Columns b.Columns
            UniqueCandidates          =
                unionBy (fun u -> u.AttributeKey) mergeUniqueCandidate a.UniqueCandidates b.UniqueCandidates
            CompositeUniqueCandidates =
                unionBy compositeKey mergeCompositeUniqueCandidate
                    a.CompositeUniqueCandidates b.CompositeUniqueCandidates
            ForeignKeys               =
                unionBy (fun fk -> fk.ReferenceKey) mergeForeignKeyReality
                    a.ForeignKeys b.ForeignKeys
            Distributions             =
                unionBy distributionMergeKey mergeDistribution
                    a.Distributions b.Distributions
            AttributeRealities        =
                unionBy (fun ar -> ar.AttributeKey) mergeAttributeReality
                    a.AttributeRealities b.AttributeRealities
            CdcAwareness              =
                { CdcEnabled  = Set.union a.CdcAwareness.CdcEnabled b.CdcAwareness.CdcEnabled
                  CdcInstance =
                      // Map.union biases to b on conflict; safe for
                      // merge semantics since instance-name strings
                      // are catalog-equivalence-classed.
                      Map.fold (fun acc k v -> Map.add k v acc) a.CdcAwareness.CdcInstance b.CdcAwareness.CdcInstance }
            // User populations are sets keyed by id — union semantics.
            SourceUsers               = UserPopulation.union a.SourceUsers b.SourceUsers
            TargetUsers               = UserPopulation.union a.TargetUsers b.TargetUsers
            // Slice B.3.8 FK correlation axes:
            ForeignKeyCardinalities   =
                unionBy (fun c -> c.ReferenceKey) mergeFkCardinality
                    a.ForeignKeyCardinalities b.ForeignKeyCardinalities
            ForeignKeySelectivities   =
                unionBy (fun s -> s.ReferenceKey) mergeFkSelectivity
                    a.ForeignKeySelectivities b.ForeignKeySelectivities
            JointDistributions        =
                unionBy jointKey mergeJointDistribution
                    a.JointDistributions b.JointDistributions
        }
