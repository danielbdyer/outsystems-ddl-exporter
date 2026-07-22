namespace Twin.Tests.Integration

open System.Threading.Tasks
open Projection.Core
open Twin.Core
open Twin.Runtime

/// THE SAMPLE-PR PROOF SUPPORT — shared by every Twin-proven sample-PR test.
///
/// `SamplePrPublish.strict` republishes the estate CURRENTLY ON DISK to the
/// twin database with the PRODUCTION-FAITHFUL DacFx posture
/// (`EstateModel.publishStrict`: BlockOnPossibleDataLoss = true, no smart-
/// defaults, the twin's bookkeeping left alone). This is the deployment a real
/// environment runs — the one the Twin's own `Runs.up` deliberately relaxes.
///
/// The "tightening blocked by data" archetype (make-mandatory, narrow-over-
/// length, add-unique-dupes) proves the PRODUCTION block with it:
///   1. `Runs.up` materializes the BEFORE estate + real-shaped data (relaxed
///      publish is fine for setup — it just lands the schema and mints rows).
///   2. `fixture.Rewrite` applies the tightening to the on-disk estate.
///   3. `SamplePrPublish.strict fixture.Root fixture.Config` attempts the
///      production-faithful publish and returns the outcome:
///        - `Error [ ValidationError ]` when REFUSED — Code
///          "twin.publish.failed", the DacFx guard/refusal text in
///          Metadata["detail"]. The block is the proof.
///        - `Ok ()` when it APPLIES (e.g. the empty-table contrast).
/// The strict publish does NOT touch the twin's `__state`, so a subsequent
/// `Runs.up`/`Runs.seed` still reads coherent fingerprints — but note it
/// changes the live schema without updating `__state`, so run the relaxed
/// `Runs.up` facts BEFORE the strict facts in a shared session.
[<RequireQualifiedAccess>]
module SamplePrPublish =

    /// Build the on-disk estate's dacpac and strict-publish it to the twin.
    let strict (root: string) (config: TwinConfig) : Task<Result<unit>> =
        task {
            match EstateFiles.resolve root config.Estate with
            | Error es -> return Result.failure es
            | Ok estate ->
                match TwinContainer.resolvePassword config.Container.PasswordRef with
                | Error es -> return Result.failure es
                | Ok password ->
                    match EstateModel.buildDacpac estate with
                    | Error es -> return Result.failure es
                    | Ok dacpac ->
                        let masterConn = TwinContainer.masterConnectionString config.Container password
                        return! EstateModel.publishStrict masterConn dacpac
        }
