namespace Projection.Core

// The position/timeline value objects the episodic chain is built on
// (`EpisodicLifecycle`, `Episode.fs`). The schema-only `Lifecycle`/
// `CatalogSnapshot` twin that once lived beside them was deleted at
// align-III.5 — the durable multi-plane `Episode` grain subsumed it
// (its laws ported onto `EpisodicLifecycle` in `LifecycleTests.fs`).

/// An ordinal position in a timeline's history, paired with a human
/// label. Per PRJ001 the position is an **ordinal, not a clock** — Core
/// holds no time. The ordinal totally orders the episodes in an
/// `EpisodicLifecycle`; the label is presentation (a SemVer string, a
/// deploy tag). Smart constructor per the structural-commitment-via-
/// construction-validation principle.
type Version = private Version of ordinal: int * label: string

[<RequireQualifiedAccess>]
module Version =

    let private ordinalNegative =
        ValidationError.create "version.ordinal.negative" "Version ordinal must be non-negative."

    let create (ordinal: int) (label: string) : Result<Version> =
        let ordinalErrors = if ordinal < 0 then [ ordinalNegative ] else []
        let labelErrors = Validation.nonBlank "version.label.empty" "Version label must be provided." label
        match ordinalErrors @ labelErrors with
        | [] -> Result.success (Version (ordinal, label))
        | es -> Result.failure es

    let ordinal (Version (o, _)) : int = o
    let label (Version (_, l)) : string = l

/// The named history along which a `Catalog` evolves (e.g. "dev",
/// "uat"). Per L3-L3 (per-timeline independence) each `EpisodicLifecycle`
/// carries exactly one `Timeline`; histories on distinct timelines are
/// independent by construction.
type Timeline = private Timeline of string

[<RequireQualifiedAccess>]
module Timeline =

    let create (name: string) : Result<Timeline> =
        match Validation.nonBlank "timeline.name.empty" "Timeline name must be provided." name with
        | [] -> Result.success (Timeline name)
        | es -> Result.failure es

    let name (Timeline n) : string = n
