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
            "    projection <input-osm-model.json> <output-dir>"
            ""
            "Reads a V1 `osm_model.json` (per the OSSYS adapter's"
            "`SnapshotFile` shape), parses it into a V2 Catalog, and"
            "writes three sibling Π artifacts:"
            ""
            "    <output-dir>/projection.sql           (raw .sql text)"
            "    <output-dir>/projection.json          (V2 IR JSON)"
            "    <output-dir>/distributions.json       (Profile-shape JSON)"
            ""
            "Exit codes:"
            "    0  artifacts written"
            "    1  argv error (wrong arg count, missing input)"
            "    2  parse error (V1 JSON did not satisfy V2's adapter contract)"
        ]

let private die (code: int) (message: string) : int =
    eprintfn "%s" message
    code

[<EntryPoint>]
let main argv =
    match argv with
    | [| inputPath; outputDir |] ->
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
                let lines =
                    errors
                    |> List.map (fun e -> sprintf "  [%s] %s" e.Code e.Message)
                    |> String.concat "\n"
                die 2 (sprintf "projection: parse failed:\n%s" lines)
    | [||]
    | [| _ |] ->
        die 1 (usage ())
    | _ ->
        die 1 (sprintf "projection: too many arguments (%d)\n\n%s" argv.Length (usage ()))
