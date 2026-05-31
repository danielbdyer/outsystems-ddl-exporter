namespace Projection.Core

/// An ordinal position in a timeline's history, paired with a human
/// label. Per PRJ001 the position is an **ordinal, not a clock** — Core
/// holds no time. The ordinal totally orders the snapshots in a
/// `Lifecycle`; the label is presentation (a SemVer string, a deploy
/// tag). Smart constructor per the structural-commitment-via-
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
/// "uat"). Per L3-L3 (per-timeline independence) each `Lifecycle`
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

/// A `Catalog` captured at a `Version` — one point in a `Lifecycle`'s
/// history.
type CatalogSnapshot = { Version: Version; Catalog: Catalog }

/// A monotone chain of `CatalogSnapshot`s along one `Timeline`. The
/// list head is C₀ (genesis, lowest ordinal); `append` keeps the chain
/// strictly increasing in ordinal (L3-L2, monotonic history).
///
/// Lifecycle is the **outer envelope** over `Project`, not a fourth
/// `ProjectionInput` field (A6 amended / A17: the inner kernel stays
/// `Catalog × Policy × Profile`). It maps over a *chain* of `Project`
/// invocations and feeds each per-edge `CatalogDiff` to the existing
/// `RefactorLogEmitter`.
type Lifecycle = private Lifecycle of LifecycleData

and LifecycleData =
    {
        Timeline  : Timeline
        Snapshots : CatalogSnapshot list
    }

[<RequireQualifiedAccess>]
module Lifecycle =

    /// Short-circuiting sequence over the Π-side `EmitError` algebra.
    /// `Result.collect` covers the `ValidationError list` arity; this is
    /// its `EmitError` peer, used to thread `CatalogDiff.between` across
    /// the evolution chain.
    let private sequenceEmit (results: Result<'a, EmitError> list) : Result<'a list, EmitError> =
        let rec loop acc remaining =
            match remaining with
            | []              -> Ok (List.rev acc)
            | Ok v :: rest    -> loop (v :: acc) rest
            | Error e :: _    -> Error e
        loop [] results

    /// Open a timeline at its genesis snapshot (C₀). Total — a single
    /// snapshot is trivially monotone.
    let genesis (timeline: Timeline) (snapshot: CatalogSnapshot) : Lifecycle =
        Lifecycle { Timeline = timeline; Snapshots = [ snapshot ] }

    let timeline (Lifecycle data) : Timeline = data.Timeline
    let snapshots (Lifecycle data) : CatalogSnapshot list = data.Snapshots

    /// The genesis snapshot C₀ (the chain head).
    let head (Lifecycle data) : CatalogSnapshot = List.head data.Snapshots

    /// The most recent snapshot (the chain tail).
    let latest (Lifecycle data) : CatalogSnapshot = List.last data.Snapshots

    /// Append the next snapshot, enforcing L3-L2 (monotonic history):
    /// the new version's ordinal must strictly exceed the latest. A
    /// non-monotone append fails rather than silently reordering — prior
    /// history is never altered.
    let append (snapshot: CatalogSnapshot) (lifecycle: Lifecycle) : Result<Lifecycle> =
        let (Lifecycle data) = lifecycle
        let lastOrdinal = Version.ordinal (List.last data.Snapshots).Version
        let nextOrdinal = Version.ordinal snapshot.Version
        if nextOrdinal > lastOrdinal then
            Result.success (Lifecycle { data with Snapshots = data.Snapshots @ [ snapshot ] })
        else
            Result.failureOf (
                ValidationError.createWithMetadata
                    "lifecycle.append.nonMonotonic"
                    "Appended version ordinal must strictly exceed the latest snapshot's ordinal."
                    (Map.ofList [ "nextOrdinal", Some (string nextOrdinal); "lastOrdinal", Some (string lastOrdinal) ]))

    /// The per-edge `CatalogDiff` chain: `[between C₀ C₁; between C₁ C₂;
    /// …]`. Folds `CatalogDiff.between` over consecutive snapshots — the
    /// formal substrate for refactor-log composition across time. A
    /// genesis-only lifecycle has no edges, so the chain is empty.
    /// Threads the Π-side `EmitError` (`between`'s error type).
    let evolutionChain (lifecycle: Lifecycle) : Result<CatalogDiff list, EmitError> =
        let (Lifecycle data) = lifecycle
        data.Snapshots
        |> List.pairwise
        |> List.map (fun (prior, next) -> CatalogDiff.between prior.Catalog next.Catalog)
        |> sequenceEmit

    /// L3-L1 (replayability) in materialized form: recover the `Catalog`
    /// stored at a `Version`. Lookup is by ordinal (the position's
    /// identity); an absent version fails. This is the exact *fetch* — it
    /// returns the stored snapshot byte-for-byte (including facets the diff
    /// does not capture: references, indexes, sequences). Its diff-fold peer
    /// is `reconstructLatest` (6.A.11 / H-007), which *derives* the catalog
    /// from the deltas and agrees with the fetch modulo the captured surface.
    let replayTo (version: Version) (lifecycle: Lifecycle) : Result<Catalog> =
        let (Lifecycle data) = lifecycle
        let target = Version.ordinal version
        match data.Snapshots |> List.tryFind (fun s -> Version.ordinal s.Version = target) with
        | Some s -> Result.success s.Catalog
        | None ->
            Result.failureOf (
                ValidationError.createWithMetadata
                    "lifecycle.version.notFound"
                    "No snapshot exists at the requested version."
                    (Map.ofList [ "ordinal", Some (string target) ]))

    /// 6.A.11 (H-007) — L3-L1 replayability as a real *reconstruction*, not a
    /// fetch: fold `CatalogDiff.applyDiff` over the evolution chain from
    /// genesis (C₀), rebuilding the latest snapshot's `Catalog` from the
    /// per-edge deltas. Where `replayTo` returns the stored snapshot,
    /// `reconstructLatest` *derives* it — the evolution algebra in action
    /// (`fold applyDiff C₀ [between C₀ C₁; …]`). The chain-level round-trip
    /// law: the reconstruction agrees with the stored latest snapshot modulo
    /// the diff's captured surface — `between (latest).Catalog
    /// (reconstructLatest) |> CatalogDiff.isEmpty`. A genesis-only lifecycle
    /// has no edges, so the fold reconstructs C₀ itself. Threads the Π-side
    /// `EmitError` (`between`'s error type).
    let reconstructLatest (lifecycle: Lifecycle) : Result<Catalog, EmitError> =
        let (Lifecycle data) = lifecycle
        let genesisCatalog = (List.head data.Snapshots).Catalog
        match evolutionChain lifecycle with
        | Ok diffs -> Ok (List.fold CatalogDiff.applyDiff genesisCatalog diffs)
        | Error e  -> Error e
