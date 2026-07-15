module Projection.Cli.Faces.Estate
// LINT-ALLOW-FILE: CLI run-face operator-facing prose + Voice payload boxing at the terminal CLI boundary; the structural surface is the typed MovementSpec / Estate report / Voice catalog, BCL primitives only at this terminal text edge.

// The estate-convergence face (`check estate` — CHAPTER_ESTATE_OPEN.md;
// DECISIONS 2026-07-15 "The estate chapter opens"). Read-only: resolves the
// unification target (the agreed environment's OSSYS shape, or the authored
// model under `--against model`) and every confirm environment (OSSYS
// identity — espace-safe; a profile failure degrades to advisory-silent,
// never aborts), rolls the `Estate.EstateReport`, renders the verdict through
// the Voice catalog with the board beneath it, writes `estate.json`, and
// exits 0 (unified) / 5 (diverged) / 6 (an environment could not be read —
// the estate verdict needs every named environment; no partial estate).

open System
open Projection.Core
open Projection.Pipeline
open Projection.Cli
open Projection.Cli.OperatorConsole

/// The §14 unreadable-operand surface: the Voice line names the environment
/// and the cause; the raw errors follow for the substantiation.
let private unreadable (label: string) (errs: ValidationError list) : int =
    let reason =
        match errs with
        | e :: _ -> e.Message
        | [] -> "the read returned no catalog"
    TtyRenderer.renderVoicedTo Console.Error "estate.envUnreadable"
        (Map.ofList [ "env", box label; "reason", box reason ])
    printErrors Console.Error errs
    6

let runCheckEstate (args: CheckEstateArgs) : int =
    // The unification target — the run states which basis it used (the
    // masthead's first line; DECISIONS 2026-07-15).
    let targetOperand =
        match args.Target with
        | EstateTargetSource.AgreedEnv _ -> Estate.TargetOperand.AgreedEnv args.TargetLabel
        | EstateTargetSource.AuthoredModel _ -> Estate.TargetOperand.AuthoredModel args.TargetLabel
    let targetCatalog =
        match args.Target with
        | EstateTargetSource.AgreedEnv connRef ->
            (Source.read (Source.ofOssys connRef)).GetAwaiter().GetResult()
        | EstateTargetSource.AuthoredModel (modelOssys, modelFile) ->
            (ModelResolution.resolveCatalog modelOssys modelFile).GetAwaiter().GetResult()
    match targetCatalog with
    | Error errs -> unreadable args.TargetLabel errs
    | Ok target ->
        // Each confirm environment: its OSSYS catalog (schema) + a profile of
        // its live data (the data-plane evidence). A profile failure degrades
        // to advisory-silent — the schema verdict still leads; the masthead
        // says the data plane observed nothing.
        let resolveEnv (label: string, refStr: string) : string * Result<string * Compare.Operand> =
            let source = Source.ofOssys refStr
            match (Source.read source).GetAwaiter().GetResult() with
            | Error errs -> label, Result.failure errs
            | Ok catalog ->
                let profile =
                    match Source.profile source with
                    | None -> None
                    | Some acquire ->
                        match (acquire catalog).GetAwaiter().GetResult() with
                        | Ok p -> Some p
                        | Error _ -> None
                label, Result.success (label, ({ Label = label; Catalog = catalog; Profile = profile } : Compare.Operand))
        let resolved = args.Confirm |> List.map resolveEnv
        match resolved |> List.tryPick (fun (label, r) -> match r with Error es -> Some (label, es) | Ok _ -> None) with
        | Some (label, errs) -> unreadable label errs
        | None ->
            let envs = resolved |> List.choose (fun (_, r) -> match r with Ok v -> Some v | Error _ -> None)
            let report = Estate.compute targetOperand target envs
            let artifact = Estate.toJsonString report
            if args.AsJson then
                printfn "%s" artifact
            else
                let laneCount lane =
                    Estate.laneCounts report
                    |> List.tryFind (fun (l, _) -> l = lane)
                    |> Option.map snd
                    |> Option.defaultValue 0
                let payload : Voice.Payload =
                    Map.ofList
                        [ "envs",         box (List.length report.Bases)
                          "total",        box (List.length report.Findings)
                          "decide",       box (laneCount EstateLane.Decide)
                          "repair",       box (laneCount EstateLane.Repair)
                          "relax",        box (laneCount EstateLane.Relax)
                          "watch",        box (laneCount EstateLane.Watch)
                          "artifactPath", box "estate.json" ]
                let verdictCode =
                    if Estate.isUnified report then "estate.unified" else "estate.diverged"
                TtyRenderer.renderVoicedTo Console.Out verdictCode payload
                printfn ""
                Estate.render report |> List.iter (fun line -> printfn "%s" line)
            IO.File.WriteAllText("estate.json", artifact)
            if Estate.isUnified report then 0 else 5
