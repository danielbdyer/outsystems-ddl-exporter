namespace Projection.Core

/// The point at which an `Episode` was recorded — *where* (`Environment`, the
/// canonical cutover-rotation identity Dev→Qa→Uat reused from `Transfer.fs`),
/// *which version* (`Version`, the schema-plane ordinal), and *when* (`At`, a
/// boundary-supplied wall-clock). Core holds no clock: `At` is a value stamped
/// by the Pipeline (exactly as `ApprovalRecord.At` is), never read from
/// `DateTimeOffset.UtcNow` inside Core. The `(Environment × At)` cell is the
/// release-coordinate the premise's lattice indexes on; `Version` totally
/// orders the schema plane within one `Timeline`.
type EpisodeCoordinate =
    {
        Version     : Version
        Environment : Environment
        At          : System.DateTimeOffset
    }

[<RequireQualifiedAccess>]
module EpisodeCoordinate =

    let create (version: Version) (environment: Environment) (at: System.DateTimeOffset) : EpisodeCoordinate =
        { Version = version; Environment = environment; At = at }


/// The data plane's durable, *observable* form. Per `WAVE_6_ALGEBRA.md` §12.4,
/// the data δ is **substrate-fused** — its observable form across an episode
/// boundary is the realized **CDC capture series**, not a model-plane value. So
/// the durable data record is the capture count (`‖data δ‖` since the prior
/// episode) and an optional handle (the LSN / change-tracking anchor that lets
/// a later run resume the capture), NOT a serialized `Profile`. The full
/// `Profile` is a per-run *input* (substrate for tightening), co-recorded
/// in-memory on the `Episode` but never persisted — persisting it would be the
/// speculative-`RowDiff`-value trap §12.4 warns against.
type DataObservation =
    {
        CdcCaptureCount : int
        CdcHandle       : string option
    }

[<RequireQualifiedAccess>]
module DataObservation =

    /// No data movement observed (a schema-only episode, or genesis).
    let empty : DataObservation = { CdcCaptureCount = 0; CdcHandle = None }

    let create (captureCount: int) (handle: string option) : DataObservation =
        { CdcCaptureCount = captureCount; CdcHandle = handle }


/// A multi-plane snapshot at one `Version`: the point at which the calculus
/// integrates. Where `CatalogSnapshot` (`Lifecycle.fs`) is schema-only and
/// single-plane, an `Episode` *co-records* the five concerns at one release
/// coordinate — Schema (the `Catalog`), Data (the CDC observation), Identity
/// (carried inside the `Catalog`'s `SsKey`s), Time (the `Coordinate`), and the
/// emitted refactorlog reference (Decision). This co-recording is what makes
/// cross-concern recombination expressible (Identity in episode *i* × Data in
/// episode *j*) — the premise's lattice (`WAVE_6_MORPHOLOGY.md` §2).
///
/// `RefactorLogRef` is a *reference* (a digest / path to the emitted log), not
/// the log itself — the Decision plane's durable anchor.
type Episode =
    {
        Coordinate     : EpisodeCoordinate
        Schema         : Catalog
        Profile        : Profile
        RefactorLogRef : string option
        Data           : DataObservation
    }

[<RequireQualifiedAccess>]
module Episode =

    /// Co-record an episode at one coordinate. The minimal-evidence form
    /// (`Profile.empty`, no refactorlog, no data movement) is `ofSchema`.
    let create
        (coordinate: EpisodeCoordinate)
        (schema: Catalog)
        (profile: Profile)
        (refactorLogRef: string option)
        (data: DataObservation)
        : Episode =
        { Coordinate = coordinate
          Schema = schema
          Profile = profile
          RefactorLogRef = refactorLogRef
          Data = data }

    /// A schema-only episode (no profiling, no data movement, no refactorlog) —
    /// the genesis shape and the durable-faithful shape (see `durableProjection`).
    let ofSchema (coordinate: EpisodeCoordinate) (schema: Catalog) : Episode =
        create coordinate schema Profile.empty None DataObservation.empty

    let schema (e: Episode) : Catalog = e.Schema
    let profile (e: Episode) : Profile = e.Profile
    let coordinate (e: Episode) : EpisodeCoordinate = e.Coordinate
    let version (e: Episode) : Version = e.Coordinate.Version

    /// The durable projection: the in-memory `Profile` is dropped (reset to
    /// `Profile.empty`). The `LifecycleStore` persists this shape — schema +
    /// coordinate + refactorlog ref + data observation — because the statistical
    /// `Profile` is a per-run input, not durable provenance (§12.4). An episode
    /// loaded from the store equals its own `durableProjection`.
    let durableProjection (e: Episode) : Episode =
        { e with Profile = Profile.empty }


/// A monotone chain of `Episode`s along one `Timeline` — the durable provenance
/// the calculus integrates over (`∂κ/∂(episode)`, `WAVE_6_ALGEBRA.md` §12.1).
/// The schema-plane FTC (`reconstructLatestSchema`, `netSchemaDiff`) is the same
/// `CatalogDiff` algebra `Lifecycle` runs, projected onto each episode's
/// `Schema`: an `EpisodicLifecycle` *is* a `Lifecycle` enriched with the data /
/// time / decision planes at each point.
type EpisodicLifecycle = private EpisodicLifecycle of EpisodicLifecycleData

and EpisodicLifecycleData =
    {
        Timeline : Timeline
        Episodes : Episode list
    }

[<RequireQualifiedAccess>]
module EpisodicLifecycle =

    /// Short-circuiting sequence over the Π-side `EmitError` algebra (mirrors
    /// `Lifecycle.sequenceEmit` — the schema-plane diff threads the same error).
    let private sequenceEmit (results: Result<'a, EmitError> list) : Result<'a list, EmitError> =
        let rec loop acc remaining =
            match remaining with
            | []           -> Ok (List.rev acc)
            | Ok v :: rest -> loop (v :: acc) rest
            | Error e :: _ -> Error e
        loop [] results

    /// Open a timeline at its genesis episode (E₀). Total — a single episode is
    /// trivially monotone.
    let genesis (timeline: Timeline) (episode: Episode) : EpisodicLifecycle =
        EpisodicLifecycle { Timeline = timeline; Episodes = [ episode ] }

    let timeline (EpisodicLifecycle data) : Timeline = data.Timeline
    let episodes (EpisodicLifecycle data) : Episode list = data.Episodes
    let head (EpisodicLifecycle data) : Episode = List.head data.Episodes
    let latest (EpisodicLifecycle data) : Episode = List.last data.Episodes

    /// Append the next episode, enforcing L3-L2 (monotonic history) on the
    /// schema-plane `Version` ordinal — the same rule `Lifecycle.append` holds.
    /// A non-monotone append fails rather than silently reordering.
    let append (episode: Episode) (lifecycle: EpisodicLifecycle) : Result<EpisodicLifecycle> =
        let (EpisodicLifecycle data) = lifecycle
        let lastOrdinal = Version.ordinal (Episode.version (List.last data.Episodes))
        let nextOrdinal = Version.ordinal (Episode.version episode)
        if nextOrdinal > lastOrdinal then
            Result.success (EpisodicLifecycle { data with Episodes = data.Episodes @ [ episode ] })
        else
            Result.failureOf (
                ValidationError.createWithMetadata
                    "episodicLifecycle.append.nonMonotonic"
                    "Appended episode version ordinal must strictly exceed the latest episode's ordinal."
                    (Map.ofList [ "nextOrdinal", Some (string nextOrdinal); "lastOrdinal", Some (string lastOrdinal) ]))

    /// The schema-plane diff chain `[between E₀.Schema E₁.Schema; …]` — the
    /// per-edge displacement along the timeline (the same fold `Lifecycle`
    /// runs, projected onto `Episode.Schema`). Genesis-only ⇒ empty chain.
    let schemaEvolutionChain (lifecycle: EpisodicLifecycle) : Result<CatalogDiff list, EmitError> =
        let (EpisodicLifecycle data) = lifecycle
        data.Episodes
        |> List.pairwise
        |> List.map (fun (prior, next) -> CatalogDiff.between prior.Schema next.Schema)
        |> sequenceEmit

    /// The FTC over the durable chain: `fold applyDiff E₀.Schema [δ₀; δ₁; …]`,
    /// reconstructing the latest schema from genesis + the per-edge deltas. The
    /// chain-level round-trip law: the reconstruction agrees with the stored
    /// latest schema modulo the diff's captured surface.
    let reconstructLatestSchema (lifecycle: EpisodicLifecycle) : Result<Catalog, EmitError> =
        let (EpisodicLifecycle data) = lifecycle
        let genesisSchema = (List.head data.Episodes).Schema
        match schemaEvolutionChain lifecycle with
        | Ok diffs -> Ok (List.fold CatalogDiff.applyDiff genesisSchema diffs)
        | Error e  -> Error e

    /// The net schema displacement genesis → latest (the integral ∫δ as a single
    /// delta; `Lifecycle.netDiff`'s episodic peer). `between E₀.Schema Eₙ.Schema`.
    let netSchemaDiff (lifecycle: EpisodicLifecycle) : Result<CatalogDiff, EmitError> =
        let (EpisodicLifecycle data) = lifecycle
        CatalogDiff.between (List.head data.Episodes).Schema (List.last data.Episodes).Schema
