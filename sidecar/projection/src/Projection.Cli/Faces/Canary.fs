module Projection.Cli.Faces.Canary

// The fidelity-canary faces (round-trip equivalence; +CDC-silence) — extracted from the RunFaces wall (recon #3, the per-verb file split).
// Self-contained: depends only on Pipeline run modules + the shared CLI helpers,
// never a RunFaces-internal helper. Verbatim relocation — zero behavior change.

open System
open System.IO
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Cli
open Projection.Cli.OperatorConsole

/// Card S3 — the canary face's stop channel: a structurally-divergent
/// round-trip (exit 5) or an invalid run (exit 2), carried out of the
/// closed-`failed` canary stage.
type private CanaryStop =
    | CanaryDiverged of report: Deploy.WideCanaryReport
    | CanaryRunInvalid of errors: ValidationError list

let runCanary (sourceDdlPath: string) : int =
    if not (File.Exists sourceDdlPath) then
        // §14 — concrete and located; the path is the substantiation.
        TtyRenderer.renderVoicedTo Console.Error "canary.sourceMissing"
            (Map.ofList [ "path", box sourceDdlPath ])
        1
    elif not (Deploy.Docker.isAvailable ()) then
        TtyRenderer.renderVoicedTo Console.Error "docker.unavailable"
            (Map.ofList [ "purpose", box "round-trip verification" ])
        4
    else
        let sourceDdl = File.ReadAllText sourceDdlPath
        TtyRenderer.renderVoicedTo Console.Out "container.starting"
            (Map.ofList [ "purpose", box "round-trip verification" ])
        // E4 — the production canary deploys the canonical schema-then-data
        // form (DDL + StaticPopulationEmitter's InsertRow realization into the
        // fresh-empty target). Schema-only when the source carries no static
        // populations. See `Deploy.schemaWithStaticPopulation`.
        //
        // Card S3 — the face rides the spine. This closes the slice-7
        // discrepancy: the prior bare `recordStage` fed the §10 stage table
        // but never the live stream; the CE's bracket emits the
        // `canary.started` / `summary.stageCompleted` pair AND the table
        // entry from one site (`recordStageEvent` semantics).
        let verdict =
            (staged Spines.canary {
                let! report =
                    Staged.stage Stages.canary (fun () ->
                        task {
                            let! result = Deploy.runWideCanary sourceDdl Deploy.schemaWithStaticPopulation
                            return
                                match result with
                                | Ok r when PhysicalSchema.isEqual r.Diff -> Ok r
                                | Ok r -> Error (CanaryDiverged r)
                                | Error errors -> Error (CanaryRunInvalid errors)
                        })
                return report
            }).GetAwaiter().GetResult()
        let deployedLine (report: Deploy.WideCanaryReport) =
            // §13 resultative — both sides of the round-trip are in place.
            TtyRenderer.renderVoicedTo Console.Out "canary.deployed"
                (Map.ofList
                    [ "sourceTables", box report.SourceReport.TablesCreated
                      "targetTables", box report.TargetReport.TablesCreated ])
            // Tier-1 reporting (§7.7) — emit the structured fidelity verdict
            // (canary.diffEmpty / canary.divergence) alongside the prose, so
            // CI can gate on it and the run ledger can record it.
            EventProjection.canaryEnvelopes report.TargetReport.TablesCreated report.Diff
            |> List.iter LogSink.emit
        let exitCode =
            match verdict.Disposition with
            | RunCompleted report ->
                deployedLine report
                TtyRenderer.renderVoicedTo Console.Out "canary.diffEmpty"
                    (Map.ofList [ "tableCount", box report.TargetReport.TablesCreated ])
                0
            | RunStopped (CanaryDiverged report) ->
                deployedLine report
                TtyRenderer.renderVoicedTo Console.Error "canary.divergence"
                    (Map.ofList [ "renderedDiff", box (PhysicalSchema.renderDiff report.Diff) ])
                5
            | RunStopped (CanaryRunInvalid errors) ->
                printErrors Console.Error errors
                2
            | RunAborted (refusal, cause) ->
                match cause with
                | Some ex -> raise ex
                | None    -> failwith refusal
        dumpBench "canary"
        exitCode

/// AC-X8 — `projection canary <source.sql> --cdc-silence`. The wide canary
/// PLUS the protein P-9 assertion: after the source≈target round-trip, enable
/// CDC on the deployed target and measure an *idempotent redeploy*
/// (`MigrationRun.execute tgt tgt`, an empty differential) with the production
/// reader. Green iff the PhysicalSchema diff is empty AND the redeploy fired
/// ZERO CDC captures — the `V2_DRIVER.md` highest-leverage property surfaced by
/// the canary itself, not a harness.
let runCanaryCdcSilence (sourceDdlPath: string) : int =
    if not (File.Exists sourceDdlPath) then
        TtyRenderer.renderVoicedTo Console.Error "canary.sourceMissing"
            (Map.ofList [ "path", box sourceDdlPath ])
        1
    elif not (Deploy.Docker.isAvailable ()) then
        TtyRenderer.renderVoicedTo Console.Error "docker.unavailable"
            (Map.ofList [ "purpose", box "CDC-silence verification" ])
        4
    else
        let sourceDdl = File.ReadAllText sourceDdlPath
        let enableCdc cnn (cat: Catalog) =
            task {
                do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_enable_db;"
                for k in Catalog.allKinds cat do
                    do! Deploy.executeBatch cnn
                            (String.Concat(   // LINT-ALLOW: terminal SQL-text boundary; sp_cdc_enable_table takes schema/table as N'...' literals
                                "EXEC sys.sp_cdc_enable_table @source_schema=N'", TableId.schemaText k.Physical,
                                "', @source_name=N'", TableId.tableText k.Physical,
                                "', @role_name=NULL, @supports_net_changes=0;"))
            }
        let redeploy cnn (cat: Catalog) =
            task {
                // The idempotent redeploy: migrate the deployed schema against
                // itself — an empty differential, so zero DDL and zero DML.
                let! _ = MigrationRun.execute true DeclareNone cat cat cnn
                return ()
            }
        TtyRenderer.renderVoicedTo Console.Out "container.starting"
            (Map.ofList [ "purpose", box "CDC-silence verification" ])
        let work =
            Deploy.runWideCanaryWithCdcSilence
                (fun cnn -> Deploy.executeBatch cnn sourceDdl)
                SsdtDdlEmitter.statements
                enableCdc
                redeploy
        let result = work.GetAwaiter().GetResult()
        let exitCode =
            match result with
            | Ok (report, cdcDelta) ->
                // §6 — the CDC measure rides channel 1 (structured) so the verdict
                // panel + ledger read the data norm; the console gets the Voice
                // surface below. Sibling of the canary diff projection.
                LogSink.emit (EventProjection.cdcMeasureEnvelope cdcDelta)
                TtyRenderer.renderVoicedTo Console.Out "canary.deployed"
                    (Map.ofList
                        [ "sourceTables", box report.SourceReport.TablesCreated
                          "targetTables", box report.TargetReport.TablesCreated ])
                let schemaOk = PhysicalSchema.isEqual report.Diff
                if not schemaOk then
                    TtyRenderer.renderVoicedTo Console.Error "canary.divergence"
                        (Map.ofList [ "renderedDiff", box (PhysicalSchema.renderDiff report.Diff) ])
                if cdcDelta <> 0 then
                    // §6 — the silence proof failed; the finding carries its measure.
                    TtyRenderer.renderVoicedTo Console.Error "canary.cdcCaptured"
                        (Map.ofList [ "capturedRows", box cdcDelta ])
                if schemaOk && cdcDelta = 0 then
                    // §6 CDC-silence proof — the deepest fidelity finding, said plain.
                    TtyRenderer.renderVoicedTo Console.Out "canary.cdcSilent" Map.empty
                    0
                else 5
            | Error errors ->
                printErrors Console.Error errors
                2
        dumpBench "canary"
        exitCode
