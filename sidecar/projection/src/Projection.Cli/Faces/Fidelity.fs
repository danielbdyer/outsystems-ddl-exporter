module Projection.Cli.Faces.Fidelity
// LINT-ALLOW-FILE: CLI run-face operator-facing prose + Voice payload boxing at the terminal CLI boundary; the structural surface is the typed MovementSpec / RowFidelityReport / Voice catalog, BCL primitives only at this terminal text edge.

// The row-fidelity face (`check data --rows` — T17, the fidelity chapter;
// DECISIONS 2026-07-15 "The fidelity chapter opens"; wave B2). Read-only:
// resolves the model reference (the alignment basis whose rename map closes
// the physical-to-logical gap), streams both environments' rows in
// primary-key order through the lockstep comparator, renders the proof
// verdict through the Voice catalog with the per-kind lines beneath it,
// writes `fidelity.rows.json`, and exits 0 (byte-identical) / 5 (differing
// rows, named) / 6 (the model or an environment could not be read).

open System
open Projection.Core
open Projection.Pipeline
open Projection.Cli
open Projection.Cli.OperatorConsole

let runCheckDataRows (args: CheckDataRowsArgs) : int =
    match (Ref.resolveCatalog (Ref.parse args.ModelRef)).GetAwaiter().GetResult() with
    | Error errs ->
        printErrors Console.Error errs
        6
    | Ok model ->
        let outcome =
            (FidelityCompareRun.run
                args.BeforeLabel args.BeforeConn
                args.AfterLabel args.AfterConn
                args.ModelRef model
                args.Kind args.Module args.SampleCap args.Interventions).GetAwaiter().GetResult()
        match outcome with
        | Error errs ->
            printErrors Console.Error errs
            6
        | Ok report ->
            let artifact = FidelityCompareRun.toJsonString report
            if args.AsJson then
                printfn "%s" artifact
            else
                let payload : Voice.Payload =
                    Map.ofList
                        [ "rows",  box (RowFidelityReport.rowsCompared report)
                          "kinds", box (List.length report.Kinds)
                          "diffs", box (RowFidelityReport.differenceTotal report)
                          "ledger", box (report.Interventions |> Option.defaultValue "")
                          "tolerances", box (String.concat ", " report.TolerancesInForce)
                          "artifactPath", box "fidelity.rows.json" ]
                let verdictCode =
                    if RowFidelityReport.agrees report then "fidelity.rows.matched" else "fidelity.rows.diverged"
                TtyRenderer.renderVoicedTo Console.Out verdictCode payload
                printfn ""
                FidelityCompareRun.render report |> List.iter (fun line -> printfn "%s" line)
            IO.File.WriteAllText("fidelity.rows.json", artifact)
            if RowFidelityReport.agrees report then 0 else 5
