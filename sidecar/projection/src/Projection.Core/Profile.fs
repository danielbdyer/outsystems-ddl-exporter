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


/// Empirical evidence aggregate. The third substantive input to
/// `Project = Π ∘ E` per the V2-amended A6. Independent of Catalog and
/// Policy per A34: this record references no Catalog or Policy types,
/// and its keys are opaque `SsKey`s that semantically refer to catalog
/// nodes without structurally depending on them.
///
/// `Profile.empty` is a first-class value; use cases that consume no
/// evidence (extract-model, schema-only) pass it as a no-op input.
type Profile = {
    Columns                   : ColumnProfile list
    UniqueCandidates          : UniqueCandidateProfile list
    CompositeUniqueCandidates : CompositeUniqueCandidateProfile list
    ForeignKeys               : ForeignKeyReality list
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
    }

    /// True iff the profile contains no observations of any kind.
    let isEmpty (p: Profile) : bool =
        List.isEmpty p.Columns
        && List.isEmpty p.UniqueCandidates
        && List.isEmpty p.CompositeUniqueCandidates
        && List.isEmpty p.ForeignKeys

    /// Look up a column profile by attribute identity. `None` if absent.
    let tryFindColumn (attributeKey: SsKey) (p: Profile) : ColumnProfile option =
        p.Columns |> List.tryFind (fun c -> c.AttributeKey = attributeKey)

    /// Look up a foreign-key reality by reference identity. `None` if absent.
    let tryFindForeignKey (referenceKey: SsKey) (p: Profile) : ForeignKeyReality option =
        p.ForeignKeys |> List.tryFind (fun fk -> fk.ReferenceKey = referenceKey)

    /// Look up a single-column uniqueness probe by attribute identity.
    let tryFindUnique (attributeKey: SsKey) (p: Profile) : UniqueCandidateProfile option =
        p.UniqueCandidates |> List.tryFind (fun u -> u.AttributeKey = attributeKey)
