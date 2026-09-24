namespace Projection.Pipeline

open System.Threading.Tasks
open Projection.Core
open Projection.Targets.Json

/// FUZZING §2.2 (slice F0c-I/O) — the propose step, the I/O front end that
/// writes a FIRST-DRAFT blessed-correction artifact: `projection synth-correct
/// --out <path>`. The durable sibling of `ProfileCaptureRun` — `profile`
/// captures the fidelity evidence, `synth-correct` proposes the blessed intent;
/// both write a reviewable, editable, version-controllable hinge the operator
/// perfects and BLESSES.
///
/// Propose = resolve the model's catalog (`ModelResolution.resolveCatalog`:
/// live OSSYS primary, model file fallback) → `CorrectionProposer.propose`
/// (heuristic PII typing by attribute name — the proposer needs NAMES, which
/// the `Profile` does NOT carry (`ColumnProfile` keys by `SsKey`), so it reads
/// the catalog, not the profile) → `CorrectionCodec.serialize` → write.
/// Conservative by design: it UNDER-claims (only canonical PII stems classify),
/// so the operator ADDS what the heuristic misses rather than UNDOES
/// over-claims. The blessed artifact is the operator's, never the heuristic's.
[<RequireQualifiedAccess>]
module CorrectionProposeRun =

    /// Resolve the model's catalog and propose a first-draft `Correction`. Pure
    /// after the catalog resolution (`CorrectionProposer.propose` is Core).
    let propose (modelOssys: string option) (modelFile: string option) : Task<Result<Correction>> =
        task {
            match! ModelResolution.resolveCatalog modelOssys modelFile with
            | Error es -> return Result.failure es
            | Ok catalog -> return Result.success (CorrectionProposer.propose catalog)
        }

    /// Propose and write the durable artifact to `outPath` (the `--out` target).
    /// The serialized form round-trips through `CorrectionCodec` (the operator
    /// edits this file, then blesses it as a trusted synthetic-flow input).
    let proposeToFile (modelOssys: string option) (modelFile: string option) (outPath: string) : Task<Result<unit>> =
        task {
            match! propose modelOssys modelFile with
            | Error es -> return Result.failure es
            | Ok correction ->
                try
                    System.IO.File.WriteAllText(outPath, CorrectionCodec.serialize correction)
                    return Result.success ()
                with ex ->
                    return Result.failureOf (ValidationError.create "correction.writeFailed" ex.Message)
        }
