namespace Projection.Pipeline

open System.IO
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql
open Projection.Adapters.Sql
open Projection.Targets.Json

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

    /// A static-model source from a FAITHFUL `CatalogCodec` snapshot file
    /// (`projection snapshot` output) — reads losslessly, no profiling. The
    /// codec round-trips the full IR (length / precision / identity / FK-trust /
    /// sequences), unlike the V1 `osm_model.json` reader, so a `diff` between two
    /// snapshots is precise. A read / decode failure is the named
    /// `source.snapshot.readFailed`.
    let ofSnapshot (path: string) : Source =
        { Identity       = "snapshot:" + path  // LINT-ALLOW: terminal Source-identity tag string at the resolution boundary; no use-case-specific AST
          Capabilities   = Set.ofList [ ReadCatalog ]
          ReadCatalog    =
            (fun () ->
                try Task.FromResult (CatalogCodec.deserialize (File.ReadAllText path))
                with ex ->
                    Task.FromResult (
                        Result.failureOf (
                            ValidationError.create "source.snapshot.readFailed"
                                (System.String.Concat("snapshot ", path, ": ", ex.Message)))))  // LINT-ALLOW: terminal error-message composition at the source-resolution IO boundary; BCL String.Concat is the right primitive
          AcquireProfile = None }

    /// Cheap discriminator: a `CatalogCodec` snapshot writes its top-level
    /// `codecVersion` marker FIRST (`CatalogCodec.wCatalog`), so it lands in the
    /// opening bytes; a V1 `osm_model.json` never carries it. Read only the head
    /// (no full parse) so the common V1 path pays almost nothing.
    let private looksLikeCodecSnapshot (path: string) : bool =
        try
            use fs = File.OpenRead path
            use sr = new StreamReader(fs)
            let buf = Array.zeroCreate<char> 256
            let n = sr.Read(buf, 0, buf.Length)
            System.String(buf, 0, n).Contains "\"codecVersion\""
        with _ -> false

    /// The conventional faithful-snapshot filename a bundle emits (kept in sync
    /// with `Pipeline.Compose.ArtifactPath.catalogSnapshot`; duplicated as a
    /// literal because `Source.fs` compiles before `Pipeline.fs`).
    let [<Literal>] private bundleSnapshotName = "catalog.snapshot.json"

    /// A static-model source from a snapshot file — or a publish DIRECTORY.
    /// **Directory-aware**: a path that is an existing directory resolves to the
    /// bundle's `catalog.snapshot.json` inside it, so `diff outA outB` (two
    /// full-export publish dirs) compares their faithful snapshots directly.
    /// **Faithful-aware**: a `CatalogCodec` snapshot (top-level `codecVersion`)
    /// reads losslessly through the codec; any other file keeps the V1
    /// `osm_model.json` reader. The marker never appears in a V1 snapshot, so
    /// this cannot mis-route one — and it transparently upgrades
    /// `diff a/ b/` (or `diff a.snapshot.json b.snapshot.json`) to a precise compare.
    let ofFile (path: string) : Source =
        let resolved =
            if Directory.Exists path then Path.Combine(path, bundleSnapshotName)
            else path
        if looksLikeCodecSnapshot resolved then ofSnapshot resolved
        else snapshot ("file:" + resolved) (CatalogReader.SnapshotFile resolved)  // LINT-ALLOW: terminal Source-identity tag string at the resolution boundary; no use-case-specific AST

    /// A static-model source from an inline JSON model — reads only.
    let ofJson (json: string) : Source =
        snapshot "json:inline" (CatalogReader.SnapshotJson json)

    /// Resolve a connection operand string to a connection string. `env:VAR`
    /// reads the connection string from the environment (the operator's
    /// predominant, secret-safe form); `file:PATH` reads the file's trimmed
    /// contents (the D9 out-of-band form); anything else is treated as a raw
    /// connection string. A missing env var falls through to the raw value,
    /// so the connection open fails loudly rather than silently picking a
    /// wrong target. Public since the estate evidence wave (2026-07-15): the
    /// fingerprint probe boundary shares this ONE resolution rule rather
    /// than growing a second `env:`/`file:` parser.
    let resolveConn (conn: string) : string =
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
        let connStr = resolveConn conn
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
                                (System.String.Concat(  // LINT-ALLOW: terminal error-message composition at the source-resolution IO boundary; BCL String.Concat primitive
                                    "live source ", conn, ": ", ex.Message)))
            }
        { Identity       = "live:" + conn  // LINT-ALLOW: terminal Source-identity tag string at the resolution boundary; no use-case-specific AST
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
    ///
    /// Scope-bearing entry point — `parameters` is the snapshot scope the
    /// model read runs under (`SnapshotScopeBinding.fromModel` binds it from
    /// the projection.json `model` section), so a scoped consumer (the peer
    /// transfer's contract reads, 2026-07-07) sees the SAME modeled estate
    /// as full-export/publish (`Compose.readConfigModel`). `ofOssys` is the
    /// zero-default sibling (the show-me-everything `defaultParameters`).
    let ofOssysWith (parameters: MetadataSnapshotRunner.SnapshotParameters) (conn: string) : Source =
        { Identity       = "ossys:" + conn  // LINT-ALLOW: terminal Source-identity tag string at the resolution boundary; no use-case-specific AST
          Capabilities   = Set.ofList [ ReadCatalog; Profile; Live ]
          ReadCatalog    =
            (fun () ->
                task {
                    try return! LiveModelRead.fromConnSpecWith parameters conn
                    with ex ->
                        return
                            Result.failureOf
                                (ValidationError.create "source.ossys.readFailed"
                                    (System.String.Concat("ossys source ", conn, ": ", ex.Message)))  // LINT-ALLOW: terminal error-message composition at the source-resolution IO boundary; BCL String.Concat is the right primitive
                })
          // The data-dealbreaker evidence for the readiness gate: profile the
          // env's live data (the OSUSR_* tables) under the OSSYS-read catalog.
          // The Static marks are stripped first (the 4.4 trap — `LiveProfiler`
          // skips static kinds, so a marked catalog profiles to an empty cache).
          AcquireProfile =
            Some (fun catalog ->
                task {
                    try
                        use cnn = new SqlConnection(resolveConn conn)
                        do! cnn.OpenAsync()
                        let profileCatalog = Catalog.stripStaticPopulations catalog
                        return! LiveProfiler.attach cnn profileCatalog Profile.empty
                    with ex ->
                        return
                            Result.failureOf
                                (ValidationError.create "source.ossys.profileFailed"
                                    (System.String.Concat("ossys source ", conn, ": ", ex.Message)))  // LINT-ALLOW: terminal error-message composition at the source-resolution IO boundary; BCL String.Concat is the right primitive
                }) }

    /// Zero-default sibling of `ofOssysWith` — the show-me-everything
    /// stance (`defaultParameters`): all modules, system + inactive
    /// included. The canary/baseline face.
    let ofOssys (conn: string) : Source =
        ofOssysWith MetadataSnapshotRunner.defaultParameters conn

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
