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
/// where the name is spent. Zero claimants, two claimants, an out-of-range
/// sync ordinal, and an unreadable snapshot are each their own named refusal
/// (`sink.envUnknown` / `sink.envAmbiguous` / `sink.syncNotFound` /
/// `sink.snapshotUnreadable`) — total decisions, never a silent fallback.
[<RequireQualifiedAccess>]
module SinkRead =

    /// The ref-scheme identity (`sink:<env>` / `sink:<env>@<syncId>`) — one
    /// definition shared by `Ref.identity` and `Source.ofSink`.
    let identityOf (env: string) (syncId: int option) : string =
        match syncId with
        | None -> "sink:" + env // LINT-ALLOW: terminal Ref-identity tag; string identity at the boundary
        | Some n -> sprintf "sink:%s@%d" env n // LINT-ALLOW: terminal Ref-identity tag; string identity at the boundary

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
            SyncId        : int
            CapturedAtUtc : DateTimeOffset
        }

    let private fail (code: string) (msg: string) : Result<'a> =
        Result.failureOf (ValidationError.create code msg)

    /// Resolve `sink:<env>[@<syncId>]` to its store coordinate. The scan is
    /// the naming act's inverse: manifests whose `EnvLabel` equals `env`.
    let resolve (env: string) (syncId: int option) : Result<Resolved> =
        match EstateStoreLocation.storeDir () with
        | None ->
            fail "sink.storeDisabled"
                "sink ref: no sink store is configured for this run — set PROJECTION_ESTATE_DIR (or PROJECTION_LEDGER_DIR) and re-run."
        | Some root ->
            let claimants =
                SinkStore.manifests root
                |> List.filter (fun m -> m.EnvLabel = Some env)
            match claimants with
            | [] ->
                fail "sink.envUnknown"
                    (sprintf "sink ref: no witnessed environment is named '%s' — `projection sync %s` against its source stamps the name (the sync verb is the naming act)." env env)
            | _ :: _ :: _ ->
                let digests = claimants |> List.map (fun m -> m.ConnDigest) |> String.concat ", " // LINT-ALLOW: terminal error-message composition at the resolution boundary
                fail "sink.envAmbiguous"
                    (sprintf "sink ref: the name '%s' is claimed by %d witnessed sources (%s) — re-run `projection sync` with a distinct name so each source's label is unique." env (List.length claimants) digests)
            | [ manifest ] ->
                let chosen = syncId |> Option.defaultValue manifest.LatestSyncId
                if chosen < 1 || chosen > manifest.LatestSyncId then
                    fail "sink.syncNotFound"
                        (sprintf "sink ref: '%s' carries witnessed syncs 1..%d; @%d is outside that range." env manifest.LatestSyncId chosen)
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
                          CapturedAtUtc = capturedAt }

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
                    (sprintf "sink ref: witnessed sync %d for digest %s reads as absent (torn or undecodable — the store is fail-closed); re-run `projection sync` to witness a fresh state." r.SyncId r.Digest))
        | Some snapshot ->
            CatalogReader.parse (CatalogReader.SnapshotRowsets (MetadataSnapshotRunner.toBundle snapshot))

    /// `sink:<env>[@<syncId>]` → `Catalog`, in one motion (the `Source.ofSink`
    /// body): resolve the name, then read the witnessed state.
    let readEnv (env: string) (syncId: int option) : Task<Result<Catalog>> =
        match resolve env syncId with
        | Error es -> Task.FromResult (Result.failure es)
        | Ok resolved -> readCatalog resolved
