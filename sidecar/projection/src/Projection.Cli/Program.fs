module Projection.Cli.Program

open System
open System.IO
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT

/// Usage lines. Per chapter 3.5 deep audit (2026-05-09): the lines
/// are a typed `string list` carrying the structured help-page
/// content. Emission to the terminal is via per-line BCL
/// `TextWriter.WriteLine` rather than concatenation into a
/// multi-line string. The typed list IS the data; each line is
/// emitted independently; no intermediate concatenation.
let private usageLines : string list =
    [
        "projection — V2 sidecar end-to-end pipeline."
        ""
        "USAGE:"
        "    projection emit   <input-osm-model.json> <output-dir>"
        "    projection deploy <input-osm-model.json>"
        "    projection canary <source-ddl-file>"
        ""
        "SUBCOMMANDS:"
        "    emit    Parse V1 JSON, project through three sibling Π's,"
        "            and write SSDT / JSON / Distributions artifacts to"
        "            <output-dir>."
        ""
        "    deploy  Parse V1 JSON, project SSDT, spin up an ephemeral"
        "            SQL Server container, deploy the SSDT, count tables,"
        "            and tear down. Run-level idempotent."
        ""
        "    canary  Run the wide canary against a SQL DDL file. Deploy"
        "            source to one ephemeral DB, read it back, run V2's"
        "            emitter on the reconstruction, deploy that to a"
        "            second DB, read back, compare on the PhysicalSchema"
        "            axis. Per DECISIONS 2026-05-23, this is V2's primary"
        "            wide integration surface."
        ""
        "All commands print a Bench table at exit and persist a JSON"
        "snapshot to bench/<command>/<utc-iso>.json under the current"
        "working directory. Per session-29 framing, this puts the perf"
        "surface in the operator's daily attention so regressions"
        "surface naturally."
        ""
        "Exit codes:"
        "    0  command succeeded"
        "    1  argv error (wrong arg count, missing input)"
        "    2  parse error (V1 JSON did not satisfy V2's adapter contract)"
        "    3  deploy error (SQL Server rejected the SSDT)"
        "    4  Docker unavailable (deploy/canary requires a running daemon)"
        "    5  canary divergence (PhysicalSchema diff non-empty)"
    ]

/// Print each usage line directly to the writer via the BCL
/// `WriteLine` primitive. Per the data-structure-oriented
/// discipline: typed list flows in; per-line writes flow out; no
/// intermediate concatenation.
let private printLines (writer: TextWriter) (lines: string list) : unit =
    for line in lines do writer.WriteLine line

let private die (code: int) (message: string) : int =
    Console.Error.WriteLine message
    code

/// Print one validation-error line directly via per-segment BCL
/// `Write` / `WriteLine` calls. Per chapter 3.5 deep audit
/// (2026-05-09): the prior implementation joined `"  [<code>]
/// <message>"` via `sprintf` + `String.concat "\n"`. Defensive:
/// `Console.Write` writes each typed segment independently;
/// `WriteLine` appends the line terminator. No intermediate
/// concatenated string.
let private printErrorLine (writer: TextWriter) (e: ValidationError) : unit =
    writer.Write "  ["
    writer.Write e.Code
    writer.Write "] "
    writer.WriteLine e.Message

let private printErrors (writer: TextWriter) (errors: ValidationError list) : unit =
    for e in errors do printErrorLine writer e

/// Print the bench table to stdout AND persist a JSON snapshot.
/// Called at the tail of every successful subcommand so the perf
/// surface is in the operator's attention.
let private dumpBench (tag: string) : unit =
    let stats = Bench.snapshot ()
    if not (List.isEmpty stats) then
        printfn ""
        printfn "Bench (sorted by total time):"
        printfn "%s" (Bench.renderTable stats)
        let path = Bench.defaultPath (Directory.GetCurrentDirectory()) tag
        try
            Bench.persistJson path tag stats
            printfn ""
            printfn "  bench snapshot: %s" path
        with ex ->
            eprintfn "  WARNING: failed to persist bench snapshot: %s" ex.Message

let private runEmit (inputPath: string) (outputDir: string) : int =
    if not (File.Exists inputPath) then
        die 1 (sprintf "projection: input file not found: %s" inputPath)
    else
        let task = Compose.run inputPath outputDir
        let result = task.GetAwaiter().GetResult()
        let exitCode =
            match result with
            | Success paths ->
                printfn "projection: wrote %d artifact(s) to %s" paths.Length outputDir
                paths
                |> List.iter (fun p ->
                    let info = FileInfo p
                    printfn "  %s (%d bytes)" p info.Length)
                0
            | Failure errors ->
                (
                    Console.Error.WriteLine "projection: parse failed:"
                    printErrors Console.Error errors
                    2
                )
        dumpBench "emit"
        exitCode

let private runDeploy (inputPath: string) : int =
    if not (File.Exists inputPath) then
        die 1 (sprintf "projection: input file not found: %s" inputPath)
    elif not (Deploy.Docker.isAvailable ()) then
        die
            4
            "projection: Docker daemon not reachable. Set DOCKER_HOST or start the daemon to run `deploy`."
    else
        printfn "projection: spinning up an ephemeral SQL Server container..."
        let task = Deploy.runFromV1Json inputPath
        let result = task.GetAwaiter().GetResult()
        let exitCode =
            match result with
            | Success (outputs, report) ->
                printfn
                    "projection: emitted %d bytes of SSDT, %d bytes of JSON, %d bytes of distributions"
                    outputs.Sql.Length
                    outputs.Json.Length
                    outputs.Distributions.Length
                if report.Success then
                    printfn
                        "projection: deploy succeeded — database `%s`, %d table(s) landed"
                        report.Database
                        report.TablesCreated
                    0
                else
                    // Per chapter 3.5 deep audit (2026-05-09): CLI
                    // error emission via per-line `Console.Error
                    // .WriteLine`. Typed list flows in; per-segment
                    // writes flow out; no concatenation. Header line
                    // composes via `Console.Error.Write` segments —
                    // each typed value (`report.Database`) emitted
                    // independently.
                    Console.Error.Write "projection: SQL Server rejected the SSDT in database `"
                    Console.Error.Write report.Database
                    Console.Error.WriteLine "`:"
                    for line in report.Errors do
                        Console.Error.Write "  "
                        Console.Error.WriteLine line
                    3
            | Failure errors ->
                (
                    Console.Error.WriteLine "projection: parse failed:"
                    printErrors Console.Error errors
                    2
                )
        dumpBench "deploy"
        exitCode

let private runCanary (sourceDdlPath: string) : int =
    if not (File.Exists sourceDdlPath) then
        die 1 (sprintf "projection: source DDL not found: %s" sourceDdlPath)
    elif not (Deploy.Docker.isAvailable ()) then
        die
            4
            "projection: Docker daemon not reachable. Set DOCKER_HOST or start the daemon to run `canary`."
    else
        let sourceDdl = File.ReadAllText sourceDdlPath
        printfn "projection: spinning up ephemeral SQL Server for the wide canary..."
        let task = Deploy.runWideCanary sourceDdl RawTextEmitter.statements
        let result = task.GetAwaiter().GetResult()
        let exitCode =
            match result with
            | Success report ->
                printfn
                    "projection: source deployed %d table(s); target deployed %d table(s)"
                    report.SourceReport.TablesCreated
                    report.TargetReport.TablesCreated
                if PhysicalSchema.isEqual report.Diff then
                    printfn "projection: canary green — PhysicalSchema diff empty"
                    0
                else
                    eprintfn ""
                    eprintfn
                        "projection: canary RED — PhysicalSchema diff non-empty:\n%s"
                        (PhysicalSchema.renderDiff report.Diff)
                    5
            | Failure errors ->
                (
                    Console.Error.WriteLine "projection: canary failed:"
                    printErrors Console.Error errors
                    2
                )
        dumpBench "canary"
        exitCode

[<EntryPoint>]
let main argv =
    match argv with
    | [| "emit"; inputPath; outputDir |] ->
        runEmit inputPath outputDir
    | [| "deploy"; inputPath |] ->
        runDeploy inputPath
    | [| "canary"; sourceDdlPath |] ->
        runCanary sourceDdlPath
    | [||]
    | [| "--help" |]
    | [| "-h" |] ->
        // Per chapter 3.5 deep audit (2026-05-09): help-page emission
        // via per-line `Console.Out.WriteLine`. Typed list flows in;
        // per-line writes flow out; no intermediate concatenated
        // multi-line string.
        printLines Console.Out usageLines
        0
    | _ ->
        Console.Error.WriteLine "projection: invalid arguments"
        Console.Error.WriteLine ""
        printLines Console.Error usageLines
        1
