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
        | Witnessed of syncId: int * displacements: int
        /// The estate matches the latest witnessed state — nothing
        /// journaled (the metadata plane's CDC-silence, rendered as one
        /// quiet line by the face).
        | Silent of syncId: int

    type SyncReport =
        {
            EnvLabel   : string
            ConnDigest : string
            Outcome    : SyncOutcome
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
                    let digest = SinkStore.connDigest16 cnn.DataSource cnn.Database
                    let before = SinkStore.loadManifest root digest
                    match! LiveModelRead.fromConnectionWith acquisitionParameters cnn with
                    | Error es -> return Result.failure es
                    | Ok _catalog ->
                        match SinkStore.loadManifest root digest with
                        | None ->
                            return
                                Result.failureOf
                                    (ValidationError.create "sink.writeFailed"
                                        "projection sync: the witness persisted nothing (the store was reachable at plan time; the read's notice rollup names the write failure).")
                        | Some after ->
                            SinkStore.nameEnvironment root digest envLabel |> ignore
                            let beforeSync = before |> Option.map (fun m -> m.LatestSyncId) |> Option.defaultValue 0
                            if after.LatestSyncId > beforeSync then
                                let displacements =
                                    match SinkJournal.load (SinkStore.journalPath root digest) with
                                    | Ok lines ->
                                        lines
                                        |> List.filter (fun l -> l.SyncId = after.LatestSyncId)
                                        |> List.length
                                    | Error _ -> 0
                                return
                                    Result.success
                                        { EnvLabel = envLabel
                                          ConnDigest = digest
                                          Outcome = SyncOutcome.Witnessed (after.LatestSyncId, displacements) }
                            else
                                return
                                    Result.success
                                        { EnvLabel = envLabel
                                          ConnDigest = digest
                                          Outcome = SyncOutcome.Silent after.LatestSyncId }
        }
