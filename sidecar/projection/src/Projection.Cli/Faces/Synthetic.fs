module Projection.Cli.Faces.Synthetic

// The synthetic / live-preview faces — project-live preview, synthetic-load, capture-profile, propose-correction.
// Extracted verbatim from the RunFaces wall (recon #3 — per-verb file split);
// zero behavior change. Uses the shared `Face` combinator + `nameOf` from
// `Faces.Common`.

open System
open System.Diagnostics
open System.IO
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Cli
open Projection.Cli.OperatorConsole
open Projection.Cli.Faces.Common
open Projection.Cli.Faces.Migrate
open Projection.Cli.Faces.Transfer

// ----------------------------------------------------------------------
// THE_CLI.md — the four-verb operator surface. `Surface.parse` (Pipeline)
// turns argv into a typed `Intent`; these executors translate the `Intent`
// to the proven engine faces above (the run* functions), so exit codes and
// behavior are preserved by construction. `project` is the hero — every
// emission-family verb is one `MovementSpec` point; deploy / migrate / load /
// export collapse into it, distinguished only by the destination and the
// auto-read baseline A.
// ----------------------------------------------------------------------


/// project --to <live> with no --go — preview the minimal change against the
/// deployed state A, never writing (THE_CLI.md §5). Reads A live, previews
/// B ⊖ A, renders the plan via the shared migrate renderer.
let runProjectLivePreview (target: Catalog) (connSpec: string) (declaration: LossDeclaration) : int =
    let work =
        task {
            match TransferSpec.parseConnectionSpec connSpec with
            | Error es ->
                // A malformed connection SPEC is the parse axis (exit 2), matching
                // `transfer` (spec errors → 2) and `runMigrateExecute`. (The prior 6
                // conflated spec-parse with the connection-reach axis.)
                printErrors Console.Error es
                return 2
            | Ok connRef ->
                let sub : Substrate =
                    { Environment = parseEnvironment "preview" None; Role = SubstrateRole.Sink; ConnectionRef = connRef }
                match! ConnectionResolver.openSubstrate sub with
                | Error es ->
                    // NM-61 (extended) — the connection-reach axis exits 6, matching
                    // transfer + the migrate execute/with-data faces (single-sourced
                    // through `refusalOf`); the prior hardcoded 3 diverged.
                    printErrors Console.Error es
                    return (Preflight.refusalOf es).ExitCode
                | Ok cnn ->
                    use cnn = cnn
                    match! ReadSide.read cnn with
                    | Error es -> return reportMigrationError (SchemaReadFailed es)
                    | Ok sourceA ->
                        return reportPreviewOutcome
                            (sprintf "projection project -> %s  (preview; re-run with --go to apply)" connSpec)
                            (MigrationRun.preview declaration sourceA target)
        }
    work.GetAwaiter().GetResult()

/// Execute a planned `Intent` against the proven `run*` engine faces. Planning
/// is pure + totality-tested (`Command.plan`); this is the single effectful
/// seam — it voices every refusal through `Voice.errorSurface` to stderr
/// (fidelity #1 + #3) and surfaces the unhonored-axis notes (#2 — no silent drop).
/// `from: synthetic` — generate from the durable profile and load (S3 flow
/// front-end). `execute = false` previews (DryRun); `execute = true` writes,
/// gated by `PROJECTION_ALLOW_EXECUTE=1` (R6), and is fail-loud on dropped
/// rows (mirrors `runTransfer`).
let runSyntheticLoad (model: ModelSource) (modelOssys: string option) (profileRef: string) (connSpec: string) (opts: LoadOpts) (execute: bool) (modelSection: Config.ModelSection) (syntheticSection: Config.SyntheticSection) : int =
    let executeGated =
        if execute then System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" = "1" else false
    if execute && not executeGated then
        TtyRenderer.renderVoicedError (ValidationError.create "gate.intent" "PROJECTION_ALLOW_EXECUTE is not set in the environment.")
        dumpBench "synthetic"
        7
    else
    let allowDrops = (opts.Declaration = DeclareAll)
    // The model file is the fallback; `modelOssys` (when set) is the live-OSSYS
    // primary. ModelResolution applies the policy.
    let modelFile =
        match model with
        | ModelSource.ModelFile p | ModelSource.ConfigFile p -> Some p
        | ModelSource.Unspecified -> None
    // §11 — the base SyntheticConfig + seed resolve from the declarative
    // `synthetic` config block (τ / preserve / synthesize / scale / seed), with the
    // per-run `--scale` / `--seed` CLI flags overriding it (config-primary). The
    // blessed `correction` is layered on top inside `SyntheticLoadRun.run`.
    let syntheticConfig = SyntheticLoadRun.resolveConfig syntheticSection opts.Scale
    let seed = SyntheticLoadRun.resolveSeed syntheticSection opts.Seed
    let result =
        (SyntheticLoadRun.run
            modelOssys modelFile profileRef opts.Correction connSpec opts.Emission opts.AllowCdc
            syntheticConfig seed executeGated modelSection)
            .GetAwaiter().GetResult()
    let exitCode =
        match result with
        | Ok report ->
            narrateTransferReport report
            let dropCode = Transfer.exitCodeForReport allowDrops report
            if dropCode <> 0 then
                TtyRenderer.renderVoicedTo Console.Error "transfer.rowsDropped"
                    (Map.ofList [ "droppedCount", box (Transfer.droppedRowCount report) ] : Voice.Payload)
            dropCode
        | Error errors ->
            printErrors Console.Error errors
            CliExit.classify errors
    dumpBench "synthetic"
    exitCode

/// `projection profile <env> --out <path>` — capture the durable Profile
/// artifact (THE_SYNTHETIC_DATA_DESIGN §2.2). Read-only (no execute gate);
/// reads the deployed catalog, profiles it, and writes the serialized form.
let runCaptureProfile (connSpec: string) (outPath: string) : int =
    let result = (ProfileCaptureRun.captureToFile connSpec outPath).GetAwaiter().GetResult()
    let exitCode =
        match result with
        | Ok () ->
            eprintfn "Profile written to %s." outPath
            0
        | Error errors ->
            printErrors Console.Error errors
            CliExit.classify errors
    dumpBench "profile"
    exitCode

/// `projection synth-correct --out <path>` — propose a first-draft blessed
/// -correction artifact from the configured model's catalog (FUZZING §2.2,
/// slice F0c-I/O). Read-only (no execute gate); resolves the catalog (live
/// OSSYS primary, the model file fallback), proposes heuristic PII typing, and
/// writes the serialized form for the operator to review / edit / bless.
let runProposeCorrection (model: ModelSource) (modelOssys: string option) (outPath: string) : int =
    let modelFile =
        match model with
        | ModelSource.ModelFile p | ModelSource.ConfigFile p -> Some p
        | ModelSource.Unspecified -> None
    let result = (CorrectionProposeRun.proposeToFile modelOssys modelFile outPath).GetAwaiter().GetResult()
    let exitCode =
        match result with
        | Ok () ->
            eprintfn "Correction proposal written to %s." outPath
            0
        | Error errors ->
            printErrors Console.Error errors
            CliExit.classify errors
    dumpBench "synth-correct"
    exitCode
