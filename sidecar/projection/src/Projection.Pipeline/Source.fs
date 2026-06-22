namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Osm
open Projection.Adapters.Sql

/// Frontier base — the capability-typed input boundary. A `Source` resolves a
/// `Catalog` from somewhere (a snapshot file, an inline JSON model, a live
/// connection), and **declares what it can do**. The discriminating predicate
/// lives in the type, exactly as `Comparison.Apply` does: a capability IS the
/// presence of its function — `AcquireProfile = Some f` iff the source can
/// profile; `None` for a snapshot. A consumer that needs profiling pattern-
/// matches `profile src` and finds `None` for a snapshot, so asking a static
/// model to profile is structurally impossible, not a runtime failure.
///
/// This SUPPORTS live OSSYS (a `Source` with the live capabilities) without
/// completing it — the live *adapter* is the verb; this is the port it plugs
/// into. `CatalogReader.SnapshotSource` is the seed it generalizes.
module Source =

    type Capability =
        | ReadCatalog
        | Profile
        | Live
        | Cdc

    type Source = {
        /// What this source is (`file:…`, `json:inline`, `live://…`).
        Identity       : string
        Capabilities   : Set<Capability>
        /// Always present — every source reads a catalog.
        ReadCatalog    : unit -> Task<Result<Catalog>>
        /// Present iff the source can profile (the capability, reified).
        AcquireProfile : (Catalog -> Task<Result<Profile>>) option
    }

    let private snapshot (identity: string) (src: CatalogReader.SnapshotSource) : Source =
        { Identity       = identity
          Capabilities   = Set.ofList [ ReadCatalog ]
          ReadCatalog    = (fun () -> CatalogReader.parse src)
          AcquireProfile = None }

    /// A static-model source from a snapshot file — reads only, no profiling.
    let ofFile (path: string) : Source =
        snapshot ("file:" + path) (CatalogReader.SnapshotFile path)

    /// A static-model source from an inline JSON model — reads only.
    let ofJson (json: string) : Source =
        snapshot "json:inline" (CatalogReader.SnapshotJson json)

    /// Resolve a `live:` operand string to a connection string. `env:VAR`
    /// reads the connection string from the environment (the operator's
    /// predominant, secret-safe form); anything else is treated as a raw
    /// connection string. A missing env var falls through to the raw value,
    /// so the connection open fails loudly rather than silently picking a
    /// wrong target.
    let private resolveConnString (conn: string) : string =
        if conn.StartsWith("env:") then
            match System.Environment.GetEnvironmentVariable(conn.Substring(4)) with
            | null | "" -> conn
            | v -> v
        elif conn.StartsWith("file:") then
            // The D9 file ref — the connection string is the file's trimmed
            // contents (out-of-band, gitignored). A read failure falls through
            // to the raw value so the open fails loudly, never silently wrong.
            let path = conn.Substring(5)
            try (System.IO.File.ReadAllText path).Trim() with _ -> conn
        else conn

    /// The live OSSYS adapter — the *verb* for the `Source` port. Reads the
    /// deployed catalog back via `ReadSide.read` (INFORMATION_SCHEMA → V2
    /// `Catalog`) and profiles the live data via `LiveProfiler.attach`. `conn`
    /// is a raw connection string or `env:VAR`. Carries ReadCatalog + Profile
    /// + Live capabilities. Each capability opens its own short-lived
    /// connection — the catalog read and the profile pass are independent
    /// operations and neither shares connection state.
    let ofLive (conn: string) : Source =
        let connStr = resolveConnString conn
        let openConnection () : Task<SqlConnection> =
            task {
                let c = new SqlConnection(connStr)
                do! c.OpenAsync()
                return c
            }
        // A connection open (or read) can throw — a malformed connection
        // string, an unreachable endpoint, a refused login. The `Source` port's
        // contract is `Task<Result<_>>`: a failure is a NAMED Error, never a
        // raw exception escaping the boundary (an escaped exception crashes the
        // CLI's `.GetAwaiter().GetResult()` instead of printing a clean refusal).
        // `guard` wraps a capability body so every failure surfaces as the typed
        // `source.live.connectionFailed` refusal.
        let guard (code: string) (body: unit -> Task<Result<'a>>) : Task<Result<'a>> =
            task {
                try return! body ()
                with ex ->
                    return
                        Result.failureOf
                            (ValidationError.create code
                                (System.String.Concat(
                                    "live source ", conn, ": ", ex.Message)))
            }
        { Identity       = "live:" + conn
          Capabilities   = Set.ofList [ ReadCatalog; Profile; Live ]
          ReadCatalog    =
            (fun () ->
                guard "source.live.connectionFailed" (fun () ->
                    task {
                        use! cnn = openConnection ()
                        return! ReadSide.read cnn
                    }))
          AcquireProfile =
            Some (fun catalog ->
                guard "source.live.profileFailed" (fun () ->
                    task {
                        use! cnn = openConnection ()
                        // `ReadSide.read` marks every reconstructed data-bearing
                        // kind `Modality.Static`, but `LiveProfiler` SKIPS static
                        // kinds (`captureEvidenceCacheWith` filters `not isStaticKind`)
                        // — so profiling a ReadSide-derived catalog as-is yields an
                        // EMPTY evidence cache and the dealbreaker section is silently
                        // never populated (the 4.4 trap). Strip the Static mark ONLY
                        // (preserving authored marks — the N2 over-erasure precedent),
                        // exactly as `Preflight.tighteningPreflight` and
                        // `DataIntegrityChecker` do before they profile a live read.
                        let profileCatalog = Catalog.stripStaticPopulations catalog
                        return! LiveProfiler.attach cnn profileCatalog Profile.empty
                    })) }

    /// The live OSSYS **model-read** adapter — the espace-safe sibling of
    /// `ofLive`. Where `ofLive` reads the deployed PHYSICAL schema via
    /// `ReadSide` (which SYNTHESIZES SsKeys from physical coordinates, so two
    /// environments' reads never align — `CatalogRendition.fs`), `ofOssys` reads
    /// the model from the OutSystems OSSYS metamodel via `LiveModelRead`,
    /// yielding the native GUID (`OssysOriginal`) `SsKey` at BOTH the kind and
    /// attribute grain (`OsmRowsetReaderTests` "WITH SsKey Guids"). That GUID is
    /// stable across environments (LifeTime preserves SS_KEY), so a
    /// cross-environment compare keyed on it is espace-safe
    /// (`CROSS_ENVIRONMENT_READINESS.md`). `conn` is a D9 connection reference
    /// (`env:<var>` / `file:<path>`). Carries ReadCatalog (the OSSYS model) +
    /// Profile (the live data — the readiness gate's dealbreaker evidence).
    let ofOssys (conn: string) : Source =
        { Identity       = "ossys:" + conn
          Capabilities   = Set.ofList [ ReadCatalog; Profile; Live ]
          ReadCatalog    =
            (fun () ->
                task {
                    try return! LiveModelRead.fromConnSpec conn
                    with ex ->
                        return
                            Result.failureOf
                                (ValidationError.create "source.ossys.readFailed"
                                    (System.String.Concat("ossys source ", conn, ": ", ex.Message)))
                })
          // The data-dealbreaker evidence for the readiness gate: profile the
          // env's live data (the OSUSR_* tables) under the OSSYS-read catalog.
          // The Static marks are stripped first (the 4.4 trap — `LiveProfiler`
          // skips static kinds, so a marked catalog profiles to an empty cache).
          AcquireProfile =
            Some (fun catalog ->
                task {
                    try
                        use cnn = new SqlConnection(resolveConnString conn)
                        do! cnn.OpenAsync()
                        let profileCatalog = Catalog.stripStaticPopulations catalog
                        return! LiveProfiler.attach cnn profileCatalog Profile.empty
                    with ex ->
                        return
                            Result.failureOf
                                (ValidationError.create "source.ossys.profileFailed"
                                    (System.String.Concat("ossys source ", conn, ": ", ex.Message)))
                }) }

    /// Enrich a source with the `Profile` capability (the live / profilable
    /// form). The capability is the presence of the function — cf.
    /// `Comparison.Apply`. The live-connection adapter calls this to declare
    /// it can profile.
    let withProfile (acquire: Catalog -> Task<Result<Profile>>) (s: Source) : Source =
        { s with
            Capabilities   = Set.add Profile s.Capabilities
            AcquireProfile = Some acquire }

    /// Declare an additional capability (e.g. `Live` / `Cdc`) on a source.
    let withCapability (c: Capability) (s: Source) : Source =
        { s with Capabilities = Set.add c s.Capabilities }

    let has (c: Capability) (s: Source) : bool = Set.contains c s.Capabilities
    let canProfile (s: Source) : bool = Option.isSome s.AcquireProfile

    let read (s: Source) : Task<Result<Catalog>> = s.ReadCatalog ()

    /// The profile action — `Some` exactly when the source can profile.
    let profile (s: Source) : (Catalog -> Task<Result<Profile>>) option = s.AcquireProfile
