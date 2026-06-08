namespace Projection.Pipeline

open System.Threading.Tasks
open Projection.Core
open Projection.Adapters.Sql
open Projection.Targets.Json

/// THE_SYNTHETIC_DATA_DESIGN §5 / §8, slice S3 (the flow front-end). The
/// orchestration behind `from: synthetic, profile: file:<path>`: read the
/// durable Profile (the hinge between capture and synthesis), read the target
/// schema B from the model, open the sink, and drive the pure generate-then-
/// load runner (`Transfer.runSynthetic`). The Profile is `σ`'s evidence; the
/// model is the schema it generates *for*; the sink is where it lands.
///
/// File I/O + connection opening live here (the boundary); the algorithmic
/// heart (`SyntheticData.generate`) and the write seam stay pure / reused.
[<RequireQualifiedAccess>]
module SyntheticLoadRun =

    /// The fixed default seed (design §7: "an optional `--seed`, default a
    /// fixed seed"). A `--seed` / `synthetic` config surface is a named
    /// follow-on; until then synthesis is reproducible from this constant.
    [<Literal>]
    let defaultSeed : uint64 = 0x5117_8E5D_0000_0001UL

    /// Resolve a profile reference to a `Profile`. Accepts the durable
    /// `file:<path>` form (design §2.2) and a bare path; reads the file and
    /// decodes through `ProfileCodec` (re-proving every leaf invariant).
    let resolveProfile (profileRef: string) : Result<Profile> =
        let path =
            if profileRef.StartsWith("file:", System.StringComparison.OrdinalIgnoreCase)
            then profileRef.Substring 5
            else profileRef
        if System.String.IsNullOrWhiteSpace path then
            Result.failureOf (ValidationError.create "synthetic.profileRef.blank" "synthetic profile reference is blank.")
        elif not (System.IO.File.Exists path) then
            Result.failureOf
                (ValidationError.create "synthetic.profileRef.missing" (sprintf "synthetic profile file '%s' not found." path))
        else
            try ProfileCodec.deserialize (System.IO.File.ReadAllText path)
            with ex ->
                Result.failureOf (ValidationError.create "synthetic.profileRef.readFailed" ex.Message)

    /// Drive a synthetic load. `execute = false` previews (DryRun, no write);
    /// `execute = true` generates and loads to the sink. The target schema B is
    /// resolved by `ModelResolution` — live OSSYS when `modelOssys` is set
    /// (primary; V1-free), else the `osm_model.json` file (fallback);
    /// `profileRef` the evidence; `connSpec` the sink. Connection / grant / CDC
    /// gates ride `Transfer.runSynthetic`.
    let run
        (modelOssys: string option)
        (modelFile: string option)
        (profileRef: string)
        (connSpec: string)
        (emission: EmissionMode)
        (allowCdc: bool)
        (config: SyntheticConfig)
        (seed: uint64)
        (execute: bool)
        : Task<Result<Transfer.TransferReport>> =
        task {
            match resolveProfile profileRef, TransferSpec.parseConnectionSpec connSpec with
            | Error es, _ -> return Result.failure es
            | _, Error es -> return Result.failure es
            | Ok profile, Ok connRef ->
                match! ModelResolution.resolveCatalog modelOssys modelFile with
                | Error es -> return Result.failure es
                | Ok catalog ->
                    let sub : Substrate =
                        { Environment   = Environment.Named "synthetic-sink"
                          Role          = SubstrateRole.Sink
                          ConnectionRef = connRef }
                    match! ConnectionResolver.openSubstrate sub with
                    | Error es -> return Result.failure es
                    | Ok sink ->
                        use sink = sink
                        let mode = if execute then Transfer.Execute else Transfer.DryRun
                        return! Transfer.runSynthetic mode emission allowCdc sink catalog profile config seed
        }
