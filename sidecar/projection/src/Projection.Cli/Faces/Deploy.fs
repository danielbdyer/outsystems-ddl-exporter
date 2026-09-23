module Projection.Cli.Faces.Deploy
// LINT-ALLOW-FILE: CLI run-face operator-facing prose + Voice payload boxing at the terminal CLI boundary; the structural surface is the typed MovementSpec / Intent / Voice catalog, BCL primitives only at this terminal text edge.

// The deploy face — SSDT compose + Docker deploy, with the stop-channel exit mapping.
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


/// Card S3 — the deploy face's stop channel: what a closed-`failed` deploy
/// stage carries to the exit-code mapping (the `staged { }` Error lane).
type private DeployStop =
    | SsdtRejected of outputs: Compose.Outputs * report: Deploy.Report
    | DeployCatalogInvalid of errors: ValidationError list

let runDeploy (shaping: Config.Config) (catalog: Catalog) : int =
    if not (Deploy.Docker.isAvailable ()) then
        // §14 required-and-missing, voiced by code (`docker.unavailable`).
        TtyRenderer.renderVoicedTo Console.Error "docker.unavailable"
            (Map.ofList [ "purpose", box "deploy" ])
        4
    else
        let runBody () =
            TtyRenderer.renderVoicedTo Console.Out "container.starting"
                (Map.ofList [ "purpose", box "deploy" ])
            // Card S3 — the face rides the spine: the `staged { }` CE owns the
            // deploy stage's bracket (started/completed envelopes + the §10
            // stage table + `Bench.scope "stage.deploy"`); the face maps the
            // disposition to its narration + documented exit codes.
            let verdict =
                (staged Spines.deploy {
                    let! landed =
                        Staged.stage Stages.deploy (fun () ->
                            task {
                                let! result = Deploy.runFromCatalogWith shaping catalog
                                return
                                    match result with
                                    | Ok (outputs, report) when report.Ok -> Ok (outputs, report)
                                    | Ok (outputs, report) -> Error (SsdtRejected (outputs, report))
                                    | Error errors -> Error (DeployCatalogInvalid errors)
                            })
                    return landed
                }).GetAwaiter().GetResult()
            let emittedLine (outputs: Compose.Outputs) =
                // §13 resultative stage line, voiced by code.
                TtyRenderer.renderVoicedTo Console.Out "deploy.bundleEmitted"
                    (Map.ofList [ "entryCount", box (Map.count outputs.SsdtBundle) ])
            match verdict.Disposition with
            | RunCompleted (outputs, report) ->
                emittedLine outputs
                // §3 — the deploy verdict, voiced (`deploy.completed`); the
                // ephemeral database name is the substantiation.
                TtyRenderer.renderVoicedTo Console.Out "deploy.completed"
                    (Map.ofList
                        [ "database",   box report.Database
                          "tableCount", box report.TablesCreated ])
                0
            | RunStopped (SsdtRejected (outputs, report)) ->
                emittedLine outputs
                // §10 — the rejection verdict, voiced (`deploy.ssdtRejected`):
                // the statement is the register's finding; the server's own
                // error lines are demoted into the disclosure beneath, the
                // exact shape `canary.divergence` set. The newline join is
                // data marshalling into the envelope payload, not prose.
                TtyRenderer.renderVoicedTo Console.Error "deploy.ssdtRejected"
                    (Map.ofList
                        [ "database",     box report.Database
                          "serverErrors", box (String.concat "\n" report.Errors) ])
                3
            | RunStopped (DeployCatalogInvalid errors) ->
                printErrors Console.Error errors
                2
            | RunAborted (refusal, cause) ->
                // The spine already closed the books (the stage's bracket
                // closed `aborted` on the wire); the face preserves the
                // verb's pre-spine crash semantics.
                match cause with
                | Some ex -> raise ex
                | None    -> failwith refusal
        // --pretty + a real TTY → a live deploy stage (§13). The schema deploy is
        // one aggregated batch (no per-table count to honestly report), so the
        // board shows the stage going Applying → Deploy complete, not a bar.
        Face.staged "deploy" true Spines.deploy runBody

/// `projection check deploy <bundleDir>` — the deploy-feasibility gate as a
/// verb: apply every `.sql` under the emitted bundle directory to a DISPOSABLE
/// database (the verb's own reaped scratch — read-only toward the estate) and
/// report exactly what the server refused, per batch, at the dependency-blind
/// fixed point. The rehearse-the-deploy loop: emit, `check deploy`, read the
/// findings, fix, re-run. Exit 0 green · 5 findings · 4 Docker unreachable ·
/// 1 bundle directory missing or empty.
let runCheckDeploy (bundleDir: string) : int =
    if not (Directory.Exists bundleDir) then
        eprintfn "check deploy: bundle directory '%s' does not exist." bundleDir
        1
    elif not (Deploy.Docker.ensureRunning ()) then
        eprintfn "check deploy: the Docker daemon is not reachable — the gate applies the bundle to a disposable SQL Server and cannot run without one."
        4
    else
        let files =
            Directory.EnumerateFiles(bundleDir, "*.sql", SearchOption.AllDirectories)
            |> Seq.sort
            |> Seq.map (fun path -> Path.GetRelativePath(bundleDir, path), File.ReadAllText path)
            |> List.ofSeq
        if List.isEmpty files then
            eprintfn "check deploy: no .sql files under '%s' — nothing to prove." bundleDir
            1
        else
            let report =
                (Deploy.withBootstrappedDatabase "check-deploy" "SELECT 1;" (fun cnn ->
                    DeployFeasibility.applyBundle cnn files)).GetAwaiter().GetResult()
            printfn "check deploy: %s, %s applied." (sprintf "%d %s" (List.length files) (if (List.length files) = 1 then "file" else "files")) (sprintf "%d %s" report.BatchesApplied (if report.BatchesApplied = 1 then "batch" else "batches"))
            if DeployFeasibility.Report.green report then
                printfn "check deploy: GREEN — the server accepted the whole bundle."
                0
            else
                printfn "check deploy: %s REFUSED at the fixed point:" (sprintf "%d %s" (List.length report.Findings) (if List.length report.Findings = 1 then "batch" else "batches"))
                for f in report.Findings do
                    printfn "  %s (batch %d): %s" f.File f.BatchOrdinal f.Error
                5
