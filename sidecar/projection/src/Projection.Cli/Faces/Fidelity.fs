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
open System.Threading.Tasks
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Cli
open Projection.Cli.OperatorConsole

/// Write the artifact, translating an I/O failure into a plain advisory
/// rather than an unhandled stack trace (THE_VOICE §14). The verdict
/// stands on the rendered proof; the file is its machine sibling.
let private tryWriteArtifact (path: string) (content: string) : unit =
    try IO.File.WriteAllText(path, content)
    with ex -> eprintfn "  Could not write %s: %s" path ex.Message

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
            tryWriteArtifact "fidelity.rows.json" artifact
            if RowFidelityReport.agrees report then 0 else 5

/// `check fidelity <flow>` — THE CONTAINER PROOF (T17, wave B5; DECISIONS
/// 2026-07-17, the loop-closing program's phase 2). One command: scaffold a
/// per-run database on the local container, stand the TARGET's shape up on
/// it (the model's logical rendition — what the cutover would deploy), load
/// it through the transfer machinery exactly as a cutover load runs
/// (journaled wipe-and-load across the rendition gap, FKs re-trusted —
/// "after applying the FKs"), then prove the load row-faithful against the
/// flow's live source modulo the journal's recorded interventions — and
/// reap the stand-in.
/// The write target is the verb's OWN scratch (created and dropped here), so
/// the run rides the ReadOnly register and no operator gate applies; the
/// Replace greenlight below is the verb's, never the flow's. Exits:
/// 0 proof green · 5 differing rows, named · 6 the model / source / scaffold
/// could not be read or refused · 4 Docker absent.
let runCheckFidelityFlow (model: Catalog) (args: CheckFidelityFlowArgs) : int =
    if not (Deploy.Docker.isAvailable ()) then
        // §14 required-and-missing, voiced by code (the deploy face's shape).
        TtyRenderer.renderVoicedTo Console.Error "docker.unavailable"
            (Map.ofList [ "purpose", box "fidelity proof" ])
        4
    else
    TtyRenderer.renderVoicedTo Console.Out "container.starting"
        (Map.ofList [ "purpose", box "fidelity proof" ])
    // ONE model, two renditions — exactly the comparator's alignment package:
    // the flow's live source carries the PHYSICAL rendition (the estate's
    // OSUSR shape), the stand-in carries the LOGICAL rendition (the target's
    // shape — the model as the cutover would deploy it), and the compare
    // re-bases the physical stream onto the logical names (T17's triangle).
    // Model-rendered contracts on BOTH sides are the proof's precondition:
    // the SsKeys the journal records are the model's own (rendition-
    // invariant), so the ledger-modulated compare can translate them — a
    // live-read contract would synthesize keys the replay could never match.
    let physical = CatalogRendition.physical model
    let logical = CatalogRendition.logical model
    // The proof's journal is an operator artifact — the intervention ledger
    // the verdict cites, and the run's recorded `JournalRef` (so
    // `--interventions @runId` can replay this exact proof later). It lives
    // beside fidelity.rows.json, never inside the reaped scratch.
    let journalDir = IO.Path.Combine("fidelity-proof", args.Flow)
    let work () : Task<Result<Transfer.TransferReport * RowFidelityReport>> =
        Deploy.withScratchDatabase "ProjectionFidelity" (fun scratchConn ->
            task {
                // 1 — THE SCAFFOLD: the target-shape DDL (the logical
                //     rendition), applied whole to the per-run scratch.
                use standIn = new Microsoft.Data.SqlClient.SqlConnection(scratchConn)
                do! standIn.OpenAsync()
                do! Deploy.executeBatch standIn (SsdtDdlEmitter.statements logical |> Render.toText)
                // 2 — THE LOAD: the transfer machinery, journaled wipe-and-load
                //     across the rendition gap (reads with the source's physical
                //     names, writes with the stand-in's logical names — the
                //     rename-aware leg the LE-3 canaries prove, mirrored).
                let srcSub : Substrate =
                    { Environment = Projection.Core.Environment.Named args.FromLabel
                      Role = SubstrateRole.Source
                      ConnectionRef = ConnectionRef.Raw args.SourceConn }
                let sinkSub : Substrate =
                    { Environment = Projection.Core.Environment.Named "container stand-in"
                      Role = SubstrateRole.Sink
                      ConnectionRef = ConnectionRef.Raw scratchConn }
                match TransferConnections.create srcSub sinkSub false with
                | Error es -> return Result.failure es
                | Ok connections ->
                    let! transferR =
                        Transfer.runReverseLegThroughConnectionsWith
                            IdentityPolicy.Structural Transfer.Execute EmissionMode.WipeAndLoad false false false []
                            connections physical logical Map.empty Set.empty Set.empty Set.empty
                            [ WriteSignoff.greenlit WriteSignoff.WriteMode.Replace ] [] false Set.empty Set.empty false None (Some journalDir)
                    match transferR with
                    | Error es -> return Result.failure es
                    | Ok transferReport ->
                        // 3 — THE PROOF: the flow's live source against the
                        //     loaded stand-in, aligned by the model, modulo
                        //     the journal's recorded interventions.
                        let! proof =
                            FidelityCompareRun.run
                                args.FromLabel args.SourceConn
                                (sprintf "%s stand-in" args.Flow) scratchConn
                                "the authored model" model
                                None None args.SampleCap transferReport.JournalPath
                        return proof |> Result.map (fun report -> transferReport, report)
            })
    match (work ()).GetAwaiter().GetResult() with
    | Error errs ->
        printErrors Console.Error errs
        6
    | Ok (transferReport, report) ->
        // The run aggregate records WHERE the proof's ledger lives (wave B4a).
        transferReport.JournalPath
        |> Option.iter (fun p ->
            match CaptureJournal.digestOfFile p with
            | Some digest -> Shell.recordLedgerRef (Run.JournalRef (digest, p))
            | None -> ())
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
            printfn "  The stand-in: a per-run container database carrying the target's shape (the model's logical rendition), loaded by the transfer machinery (wipe-and-load, journaled, FKs re-trusted), reaped after the proof."
            // The load's own named erasures — each dropped row surfaces in
            // the compare as a missing row; the WHY is said here.
            if not (List.isEmpty transferReport.SkippedReferences) then
                printfn "  The load dropped %d row(s) (a relationship pointed at an unmatched record) — each surfaces below as a missing row." (List.length transferReport.SkippedReferences)
            FidelityCompareRun.render report |> List.iter (fun line -> printfn "%s" line)
        tryWriteArtifact "fidelity.rows.json" artifact
        if RowFidelityReport.agrees report then 0 else 5
