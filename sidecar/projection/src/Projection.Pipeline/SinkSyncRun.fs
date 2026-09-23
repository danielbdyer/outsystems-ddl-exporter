namespace Projection.Pipeline

open System.Threading.Tasks
open Projection.Core
open Projection.Adapters.OssysSql

/// `projection sync <env>` — the sink's naming verb (the data-sink chapter,
/// S6; CHAPTER_SINK_OPEN.md). A forced TOTAL witnessed read plus the
/// displacement report: the acquisition rides the ONE funnel
/// (`LiveModelRead.fromConnectionWith` — never a second acquisition
/// expression), the witness inside the funnel persists the snapshot and
/// journals the displacements, and this run stamps the operator's
/// environment label onto the manifest — **the sync verb is the act that
/// makes an environment addressable by name** (`sink:<env>` resolution
/// scans for that label).
///
/// The standalone-extract deferral's trigger — "a CI lane that materializes
/// the snapshot without emitting" — fires here (DECISIONS Active deferrals;
/// the profile half of that row shipped earlier as `projection profile`).
[<RequireQualifiedAccess>]
module SinkSyncRun =

    /// A49's structural pin: the sync path binds the show-me-everything
    /// shape EXACTLY — no module CSV, `IncludeSystem = true`,
    /// `IncludeInactive = true`, `OnlyActiveAttributes = false`, no entity
    /// filter. The property test asserts this binding equals
    /// `MetadataSnapshotRunner.defaultParameters`; `SinkStore.witness`'s
    /// totality gate enforces it again at the store.
    let acquisitionParameters : MetadataSnapshotRunner.SnapshotParameters =
        MetadataSnapshotRunner.defaultParameters

    /// What one sync observed — the face's rendering substrate.
    [<RequireQualifiedAccess>]
    type SyncOutcome =
        /// A new sync landed (the estate moved): its ordinal and the
        /// journaled displacement count.
        | Witnessed of syncId: SyncOrdinal * displacements: int
        /// The estate matches the latest witnessed state — nothing
        /// journaled (the metadata plane's CDC-silence, rendered as one
        /// quiet line by the face).
        | Silent of syncId: SyncOrdinal

    type SyncReport =
        {
            EnvLabel   : string
            ConnDigest : string
            Outcome    : SyncOutcome
        }

    /// The post-read report: read the store back, stamp the environment
    /// label, and classify the sync (everything after the funnel read is
    /// synchronous store I/O, so this is a plain function — hoisted out of
    /// the task per survival rule 5: the flat CE below is what Release
    /// builds can statically compile, and the report logic needs no await).
    let private reportOf (root: string) (envLabel: string) (digest: string) (before: SinkStore.Manifest option) : Result<SyncReport> =
        match SinkStore.loadManifest root digest with
        | None ->
            Result.failureOf
                (ValidationError.create "sink.writeFailed"
                    "projection sync: the witness persisted nothing (the store was reachable at plan time; the read's notice rollup names the write failure).")
        | Some after ->
            SinkStore.nameEnvironment root digest envLabel |> ignore
            // align-III.1: "no prior witness" is the option — a first
            // witness (before = None) is always a new edition.
            let movedPastBefore =
                match before with
                | None -> true
                | Some b -> after.LatestSyncId > b.LatestSyncId
            if movedPastBefore then
                let displacements =
                    match SinkJournal.load (SinkStore.journalPath root digest) with
                    | Ok lines ->
                        lines
                        |> List.filter (fun l -> l.SyncId = after.LatestSyncId)
                        |> List.length
                    | Error _ -> 0
                Result.success
                    { EnvLabel = envLabel
                      ConnDigest = digest
                      Outcome = SyncOutcome.Witnessed (after.LatestSyncId, displacements) }
            else
                Result.success
                    { EnvLabel = envLabel
                      ConnDigest = digest
                      Outcome = SyncOutcome.Silent after.LatestSyncId }

    /// The sync body over an OPEN connection — hoisted to module level (the
    /// `LiveModelRead.fromConnSpecWith` shape): `use` + `let!` nested inside
    /// a `match!` arm is an FS3511 state machine Release builds refuse
    /// (survival rule 5's family), so `run` owns the open/dispose, this
    /// helper owns the one awaited read, and `reportOf` owns the rest.
    let private syncOpened (root: string) (envLabel: string) (cnn: Microsoft.Data.SqlClient.SqlConnection) : Task<Result<SyncReport>> =
        task {
            let digest = SinkStore.connDigest16 cnn.DataSource cnn.Database
            let before = SinkStore.loadManifest root digest
            let! liveRead = LiveModelRead.fromConnectionWith acquisitionParameters cnn
            return liveRead |> Result.bind (fun _ -> reportOf root envLabel digest before)
        }

    /// Run one sync: open the source (the one `ConnectionSpec.openSpec`
    /// opener, `Source` role), read totally through the funnel (the
    /// witness persists inside it), then read the store back for the
    /// report and stamp the environment label.
    let run (connSpec: string) (envLabel: string) : Task<Result<SyncReport>> =
        task {
            match EstateStoreLocation.storeDir () with
            | None ->
                return
                    Result.failureOf
                        (ValidationError.create "sink.storeDisabled"
                            "projection sync: no sink store is configured for this run — set PROJECTION_ESTATE_DIR (or PROJECTION_LEDGER_DIR) and re-run.")
            | Some root ->
                match! ConnectionSpec.openSpec SubstrateRole.Source "sink-sync-source" connSpec with
                | Error es -> return Result.failure es
                | Ok cnn ->
                    use cnn = cnn
                    return! syncOpened root envLabel cnn
        }
