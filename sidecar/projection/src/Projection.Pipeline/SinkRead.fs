namespace Projection.Pipeline
// LINT-ALLOW-FILE: sink-ref resolution at the store boundary (the data-sink chapter, S7) — store-directory file reads and terminal ref-identity/error-message composition; the structural surface is the typed Resolved record + Result channel, mirroring the SinkStore/EstateEvidenceStore file-boundary precedents.

open System
open System.Threading.Tasks
open Projection.Core
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql

/// `sink:<env>[@<syncId>]` resolution + the sink→Catalog read (the data-sink
/// chapter, S7; CHAPTER_SINK_OPEN.md). A sink read is the exact live pipeline
/// minus the wire: witnessed `MetadataSnapshot` → `toBundle` →
/// `CatalogReader.parse (SnapshotRowsets …)` — which is why no new
/// `SnapshotSource` variant exists (the grain ruling) and why the K2 parity
/// law can demand TOTAL `Catalog` equality against the live read it replays.
///
/// Resolution is an env-label scan over the store's manifests (config-free
/// and offline-true — R4): `projection sync <env>` stamped the label; this is
/// where the name is spent. Zero claimants, a currency TIE among several
/// claimants (align-III.15 — a multi-claimant label is otherwise a COMPOSITE
/// that resolves to its current member), an out-of-range sync ordinal, and an
/// unreadable snapshot are each their own named refusal (`sink.envUnknown` /
/// `sink.envAmbiguous` / `sink.syncNotFound` / `sink.snapshotUnreadable`) —
/// total decisions, never a silent fallback.
[<RequireQualifiedAccess>]
module SinkRead =

    /// The ref-scheme identity (`sink:<env>` / `sink:<env>@<syncId>`) — one
    /// definition shared by `Ref.identity` and `Source.ofSink`.
    let identityOf (env: string) (syncId: SyncOrdinal option) : string =
        match syncId with
        | None -> "sink:" + env // LINT-ALLOW: terminal Ref-identity tag; string identity at the boundary
        | Some n -> sprintf "sink:%s@%s" env (SyncOrdinal.text n) // LINT-ALLOW: terminal Ref-identity tag; string identity at the boundary

    /// A resolved sink coordinate: the store root, the source directory's
    /// digest, its manifest, the pinned-or-latest sync ordinal, and when that
    /// state was witnessed (the freshness line's substance — for a pinned
    /// older sync the journal line's capture time wins; the manifest's is the
    /// latest sync's).
    type Resolved =
        {
            Root          : string
            Digest        : string
            Manifest      : SinkStore.Manifest
            SyncId        : SyncOrdinal
            CapturedAtUtc : DateTimeOffset
            /// align-III.15 — the label's OTHER witnessed sources, when the
            /// name is a composite (an environment re-witnessed across
            /// connection changes, or deliberately spanning sources). The
            /// resolution reads the CURRENT member (the unique latest
            /// capture); these are the superseded/other members' digests,
            /// oldest last. Empty for a single-source label — byte-identical
            /// to the pre-III.15 shape everywhere it renders.
            SupersededDigests : string list
        }

    let private fail (code: string) (msg: string) : Result<'a> =
        Result.failureOf (ValidationError.create code msg)

    /// Resolve `sink:<env>[@<syncId>]` to its store coordinate. The scan is
    /// the naming act's inverse: manifests whose `EnvLabel` equals `env`.
    let resolve (env: string) (syncId: SyncOrdinal option) : Result<Resolved> =
        match EstateStoreLocation.storeDir () with
        | None ->
            fail "sink.storeDisabled"
                "sink ref: no sink store is configured for this run — set PROJECTION_ESTATE_DIR (or PROJECTION_LEDGER_DIR) and re-run."
        | Some root ->
            let claimants =
                SinkStore.manifests root
                |> List.filter (fun m -> m.EnvLabel = Some env)
            // align-III.15 — a label claimed by SEVERAL witnessed sources is a
            // COMPOSITE environment (the common shape: one environment
            // re-witnessed across a connection change, each edition its own
            // digest), not automatically an error. The read addresses the
            // CURRENT member — the unique latest capture; the others ride the
            // resolution as its superseded set. GENUINE ambiguity narrows to
            // a currency TIE (two members captured at the same instant —
            // nothing distinguishes which line is the environment's present),
            // and only that still refuses as `sink.envAmbiguous`.
            let resolveOn (manifest: SinkStore.Manifest) (superseded: SinkStore.Manifest list) : Result<Resolved> =
                let chosen = syncId |> Option.defaultValue manifest.LatestSyncId
                // align-III.1: below-1 pins are unrepresentable (the ordinal
                // VO), so the only out-of-range direction left is "past the
                // latest witnessed edition".
                if chosen > manifest.LatestSyncId then
                    fail "sink.syncNotFound"
                        (sprintf "sink ref: '%s' carries witnessed syncs 1..%s; @%s is outside that range." env (SyncOrdinal.text manifest.LatestSyncId) (SyncOrdinal.text chosen))
                else
                    let capturedAt =
                        if chosen = manifest.LatestSyncId then manifest.CapturedAtUtc
                        else
                            // A pinned older edition: the journal line that
                            // witnessed it carries its capture time. The
                            // manifest's time is the honest fallback if the
                            // journal cannot say (the age is advisory; the
                            // read itself stays fail-closed below).
                            match SinkJournal.load (SinkStore.journalPath root manifest.ConnDigest) with
                            | Ok lines ->
                                lines
                                |> List.tryFind (fun l -> l.SyncId = chosen)
                                |> Option.map (fun l -> l.CapturedAtUtc)
                                |> Option.defaultValue manifest.CapturedAtUtc
                            | Error _ -> manifest.CapturedAtUtc
                    Result.success
                        { Root = root
                          Digest = manifest.ConnDigest
                          Manifest = manifest
                          SyncId = chosen
                          CapturedAtUtc = capturedAt
                          SupersededDigests = superseded |> List.map (fun m -> m.ConnDigest) }
            match claimants with
            | [] ->
                fail "sink.envUnknown"
                    (sprintf "sink ref: no witnessed environment is named '%s' — `projection sync %s` against its source stamps the name (the sync verb is the naming act)." env env)
            | [ manifest ] -> resolveOn manifest []
            | many ->
                let ordered = many |> List.sortByDescending (fun m -> m.CapturedAtUtc, m.ConnDigest)
                match ordered with
                | current :: (next :: _ as rest) when current.CapturedAtUtc > next.CapturedAtUtc ->
                    resolveOn current rest
                | _ ->
                    let digests = many |> List.map (fun m -> m.ConnDigest) |> String.concat ", " // LINT-ALLOW: terminal error-message composition at the resolution boundary
                    fail "sink.envAmbiguous"
                        (sprintf "sink ref: the name '%s' is claimed by %d witnessed sources (%s) whose latest captures TIE — nothing distinguishes the environment's current line; re-witness one (`projection sync %s`) or re-run `projection sync` with a distinct name for the other." env (List.length many) digests env)

    /// Resolve a live CONNECTION STRING to its sink coordinate — the
    /// digest-based sibling of the env-label scan (R3's string side: the
    /// digest derives from `SqlConnectionStringBuilder`'s DataSource +
    /// InitialCatalog, the same normalized pair the witness hook reads off
    /// the open connection, so the two sides agree by construction). The
    /// caller resolves `env:`/`file:` refs to the raw string first
    /// (`Source.resolveConn` — Source compiles after this module).
    let resolveByConnectionString (connStr: string) (syncId: SyncOrdinal option) : Result<Resolved> =
        match EstateStoreLocation.storeDir () with
        | None ->
            fail "sink.storeDisabled"
                "sink read: no sink store is configured for this run — set PROJECTION_ESTATE_DIR (or PROJECTION_LEDGER_DIR) and re-run."
        | Some root ->
            let identity =
                try
                    let b = Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr)
                    Some (b.DataSource, b.InitialCatalog)
                with _ -> None
            match identity with
            | None ->
                fail "sink.connUnresolvable"
                    "sink read: the connection string did not parse, so no sink identity can be derived from it."
            | Some (dataSource, database) ->
                let digest = SinkStore.connDigest16 dataSource database
                match SinkStore.loadManifest root digest with
                | None ->
                    fail "sink.noWitnessedState"
                        (sprintf "sink read: no witnessed state exists for this source (digest %s) — a total live read (or `projection sync`) witnesses the first edition." digest)
                | Some manifest ->
                    let chosen = syncId |> Option.defaultValue manifest.LatestSyncId
                    if chosen > manifest.LatestSyncId then
                        fail "sink.syncNotFound"
                            (sprintf "sink read: this source carries witnessed syncs 1..%s; @%s is outside that range." (SyncOrdinal.text manifest.LatestSyncId) (SyncOrdinal.text chosen))
                    else
                        Result.success
                            { Root = root
                              Digest = digest
                              Manifest = manifest
                              SyncId = chosen
                              CapturedAtUtc = manifest.CapturedAtUtc
                              SupersededDigests = [] }

    /// Read the resolved witnessed state as a `Catalog` — the live pipeline
    /// minus the wire. `CatalogReader.parse` applies the same (idempotent)
    /// bundle normalization the live read surfaces notices for; the sink read
    /// is a REPLAY of an acquisition whose notices were already surfaced at
    /// witness time, so the parse is the whole story here.
    let readCatalog (r: Resolved) : Task<Result<Catalog>> =
        match SinkStore.loadSnapshotAt r.Root r.Digest r.SyncId with
        | None ->
            Task.FromResult (
                fail "sink.snapshotUnreadable"
                    (sprintf "sink ref: witnessed sync %s for digest %s reads as absent (torn or undecodable — the store is fail-closed); re-run `projection sync` to witness a fresh state." (SyncOrdinal.text r.SyncId) r.Digest))
        | Some snapshot ->
            // The replay path drops the projection's erasure record by name
            // (align-II.8; the SinkDiffView twin): witness-time notices
            // already surfaced on the live read (S7 raw-at-rest posture).
            CatalogReader.parse (CatalogReader.SnapshotRowsets (fst (MetadataSnapshotRunner.toBundle snapshot)))

    /// `sink:<env>[@<syncId>]` → `Catalog`, in one motion (the `Source.ofSink`
    /// body): resolve the name, then read the witnessed state.
    let readEnv (env: string) (syncId: SyncOrdinal option) : Task<Result<Catalog>> =
        match resolve env syncId with
        | Error es -> Task.FromResult (Result.failure es)
        | Ok resolved -> readCatalog resolved
