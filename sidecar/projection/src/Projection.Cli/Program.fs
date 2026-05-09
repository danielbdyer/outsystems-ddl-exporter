module Projection.Cli.Program

open System
open System.IO
open Projection.Core
open Projection.Pipeline

let private usage () : string =
    String.concat
        "\n"
        [
            "projection — V2 sidecar end-to-end pipeline."
            ""
            "USAGE:"
            "    projection emit   <input-osm-model.json> <output-dir>"
            "    projection deploy <input-osm-model.json>"
            ""
            "SUBCOMMANDS:"
            "    emit    Parse V1 JSON, project through three sibling Π's,"
            "            and write SSDT / JSON / Distributions artifacts to"
            "            <output-dir>."
            ""
            "    deploy  Parse V1 JSON, project SSDT, spin up an ephemeral"
            "            SQL Server container, deploy the SSDT, count tables,"
            "            and tear down. Each invocation is fully independent"
            "            (run-level idempotency: same input → same outcome,"
            "            no state crosses runs)."
            ""
            "Exit codes:"
            "    0  command succeeded"
            "    1  argv error (wrong arg count, missing input)"
            "    2  parse error (V1 JSON did not satisfy V2's adapter contract)"
            "    3  deploy error (SQL Server rejected the SSDT)"
            "    4  Docker unavailable (deploy mode requires a running daemon)"
        ]

let private die (code: int) (message: string) : int =
    eprintfn "%s" message
    code

let private renderErrors (errors: ValidationError list) : string =
    errors
    |> List.map (fun e -> sprintf "  [%s] %s" e.Code e.Message)
    |> String.concat "\n"

let private runEmit (inputPath: string) (outputDir: string) : int =
    if not (File.Exists inputPath) then
        die 1 (sprintf "projection: input file not found: %s" inputPath)
    else
        let task = Compose.run inputPath outputDir
        let result = task.GetAwaiter().GetResult()
        match result with
        | Success paths ->
            printfn "projection: wrote %d artifact(s) to %s" paths.Length outputDir
            paths
            |> List.iter (fun p ->
                let info = FileInfo p
                printfn "  %s (%d bytes)" p info.Length)
            0
        | Failure errors ->
            die 2 (sprintf "projection: parse failed:\n%s" (renderErrors errors))

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
                let errors = report.Errors |> List.map (sprintf "  %s") |> String.concat "\n"
                die
                    3
                    (sprintf
                        "projection: SQL Server rejected the SSDT in database `%s`:\n%s"
                        report.Database
                        errors)
        | Failure errors ->
            die 2 (sprintf "projection: parse failed:\n%s" (renderErrors errors))

[<EntryPoint>]
let main argv =
    match argv with
    | [| "emit"; inputPath; outputDir |] ->
        runEmit inputPath outputDir
    | [| "deploy"; inputPath |] ->
        runDeploy inputPath
    // Back-compat: bare `projection <input> <output-dir>` keeps the M1 surface
    // working until consumers migrate to the explicit `emit` subcommand.
    | [| inputPath; outputDir |] when not (inputPath = "deploy" || inputPath = "emit") ->
        runEmit inputPath outputDir
    | [||]
    | [| "--help" |]
    | [| "-h" |] ->
        printfn "%s" (usage ())
        0
    | _ ->
        die 1 (sprintf "projection: invalid arguments\n\n%s" (usage ()))
