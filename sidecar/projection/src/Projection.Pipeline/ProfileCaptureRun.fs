namespace Projection.Pipeline

open System.Threading.Tasks
open Projection.Core
open Projection.Adapters.Sql
open Projection.Targets.Json

/// THE_SYNTHETIC_DATA_DESIGN §2.2 / §5 — the capture step `π`, the I/O front
/// end that produces the durable Profile artifact: `projection profile <env>
/// --out <path>`. Capture is separated from synthesis by its nature (I/O,
/// slow, once); the serialized Profile is the reviewable, editable hinge the
/// synthetic flow replays.
///
/// Capture = read the deployed catalog (`ReadSide.read`) → profile it
/// (`LiveProfiler.attach`) → serialize (`ProfileCodec`). The one wrinkle:
/// `ReadSide.read` marks every read kind `Modality=[Static rows]` (it lifts
/// live rows for the per-row PhysicalSchema canary) and `LiveProfiler` skips
/// static kinds — so the Static mark is stripped before profiling, since here
/// the data lives in the DB, not the catalog.
[<RequireQualifiedAccess>]
module ProfileCaptureRun =

    /// Capture a full `Profile` from a live environment (read-only). Opens the
    /// connection in the `Source` role through the one `ConnectionSpec.openSpec`
    /// opener (recon #13 — `env:` / `file:` / `live:` / bare, uniform with
    /// `transfer` / `slice`), reconstructs the catalog, strips the Static mark,
    /// and composes every Profile axis via `LiveProfiler.attach`.
    let capture (connSpec: string) : Task<Result<Profile>> =
        task {
            match! ConnectionSpec.openSpec SubstrateRole.Source "profile-source" connSpec with
            | Error es -> return Result.failure es
            | Ok cnn ->
                use cnn = cnn
                match! ReadSide.read cnn with
                | Error es -> return Result.failure es
                | Ok catalog -> return! LiveProfiler.attach cnn (Catalog.stripStaticPopulations catalog) Profile.empty
        }

    /// Capture and write the durable artifact to `outPath` (the `--out`
    /// target). The serialized form round-trips through `ProfileCodec`.
    let captureToFile (connSpec: string) (outPath: string) : Task<Result<unit>> =
        task {
            match! capture connSpec with
            | Error es -> return Result.failure es
            | Ok profile ->
                try
                    System.IO.File.WriteAllText(outPath, ProfileCodec.serialize profile)
                    return Result.success ()
                with ex ->
                    return Result.failureOf (ValidationError.create "profile.writeFailed" ex.Message)
        }
