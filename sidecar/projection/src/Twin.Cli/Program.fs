module Twin.Cli.Program

open Projection.Core
open Twin.Core
open Twin.Runtime

/// THE TWIN — the entry point (Twin.Cli).
///
/// Exit codes share the projection CLI's vocabulary:
///   0 — done; 1 — the arguments did not parse; 4 — Docker unavailable;
///   6 — the configuration did not parse; 9 — refused / did not succeed.
let private exitOk = 0
let private exitArgv = 1
let private exitDocker = 4
let private exitConfig = 6
let private exitRefused = 9

let private emit (lines: string list) : unit =
    for line in lines do System.Console.Out.WriteLine line

let private exitCodeFor (es: ValidationError list) : int =
    if es |> List.exists (fun e -> e.Code = "twin.container.dockerUnavailable") then exitDocker
    elif es |> List.exists (fun e -> e.Code.StartsWith "twin.config.") then exitConfig
    else exitRefused

/// Locate twin.json: `TWIN_CONFIG` when set, else `./twin.json`.
/// Returns (repository root, config path).
let private discoverConfig () : Result<string * string> =
    let fromEnv = System.Environment.GetEnvironmentVariable "TWIN_CONFIG"
    let path =
        match fromEnv with
        | null | "" -> System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "twin.json")
        | p -> System.IO.Path.GetFullPath p
    if System.IO.File.Exists path then
        match System.IO.Path.GetDirectoryName path with
        | null | "" -> Result.success (System.IO.Directory.GetCurrentDirectory(), path)
        | dir -> Result.success (dir, path)
    else
        Result.failureOf
            (ValidationError.createWithMetadata
                "twin.config.missing"
                "twin.json was not found. Run from the repository root, set TWIN_CONFIG, or write a starter with: twin init"
                (Map.ofList [ "path", Some path ]))

let private loadConfig () : Result<string * TwinConfig> =
    discoverConfig ()
    |> Result.bind (fun (root, path) ->
        try
            TwinConfig.parse (System.IO.File.ReadAllText path)
            |> Result.map (fun config -> root, config)
        with ex ->
            Result.failureOf
                (ValidationError.createWithMetadata
                    "twin.config.unreadable"
                    "twin.json could not be read."
                    (Map.ofList [ "path", Some path; "detail", Some ex.Message ])))

/// `--scenario <name>` (default: the baseline scenario).
let private parseScenario (args: string list) : Result<string> =
    let rec walk (remaining: string list) : Result<string option> =
        match remaining with
        | [] -> Result.success None
        | "--scenario" :: name :: _ when not (name.StartsWith "--") -> Result.success (Some name)
        | "--scenario" :: _ ->
            Result.failureOf (ValidationError.create "twin.argv.scenario" "--scenario requires a name.")
        | unknown :: _ when unknown.StartsWith "--" ->
            Result.failureOf
                (ValidationError.createWithMetadata
                    "twin.argv.unknown" "An unrecognized option was passed."
                    (Map.ofList [ "option", Some unknown ]))
        | _ :: rest -> walk rest
    walk args |> Result.map (Option.defaultValue TwinConfig.BaselineScenario)

let private starterConfig =
    """{
  "estate": {
    "tables": "Modules/**/*.sql",
    "schemas": "Schemas/*.sql",
    "staticData": []
  },
  "scenarios": {
    "default": {}
  }
}
"""

let private runInit () : int =
    let path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "twin.json")
    if System.IO.File.Exists path then
        emit (Render.initExists path)
        exitOk
    else
        System.IO.File.WriteAllText(path, starterConfig)
        emit (Render.initScaffolded path)
        exitOk

let private runWithConfig
    (args: string list)
    (body: string -> TwinConfig -> string -> System.Threading.Tasks.Task<Result<string list>>)
    : int =
    match loadConfig (), parseScenario args with
    | Error es, _ | _, Error es ->
        emit (Render.failure es)
        exitCodeFor es
    | Ok (root, config), Ok scenario ->
        let outcome = (body root config scenario).GetAwaiter().GetResult()
        match outcome with
        | Ok lines ->
            emit lines
            exitOk
        | Error es ->
            emit (Render.failure es)
            exitCodeFor es

/// Open the running twin and hand its read-back catalog to `body` — the
/// classify / bake / evidence-verify precondition.
let private withTwinCatalog
    (root: string)
    (config: TwinConfig)
    (body: Projection.Core.Catalog -> Result<string list>)
    : System.Threading.Tasks.Task<Result<string list>> =
    task {
        match TwinContainer.resolvePassword config.Container.PasswordRef with
        | Error es -> return Result.failure es
        | Ok password ->
            let! state = TwinContainer.state config.Container
            match state with
            | Error es -> return Result.failure es
            | Ok TwinContainer.Running ->
                use cnn = new Microsoft.Data.SqlClient.SqlConnection(TwinContainer.twinConnectionString config.Container password)
                do! cnn.OpenAsync()
                let! catalog = Readback.readSchema cnn
                return catalog |> Result.bind body
            | Ok _ ->
                return
                    Result.failureOf
                        (ValidationError.create "twin.notUp" "The twin is not running. Run: twin up, then retry.")
    }

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | [] | [ "--help" ] | [ "-h" ] | [ "help" ] ->
        emit Render.usage
        exitOk
    | "init" :: _ -> runInit ()
    | "up" :: rest ->
        runWithConfig rest (fun root config scenario ->
            task {
                let! outcome = Runs.up root config scenario false
                return outcome |> Result.map Render.upOutcome
            })
    | "seed" :: rest ->
        runWithConfig rest (fun root config scenario ->
            task {
                let! outcome = Runs.seed root config scenario
                return outcome |> Result.map Render.upOutcome
            })
    | "status" :: rest ->
        runWithConfig rest (fun root config scenario ->
            task {
                let! report = Runs.status root config scenario
                return report |> Result.map Render.status
            })
    | "check" :: rest ->
        runWithConfig rest (fun root config scenario ->
            task {
                let! report = Check.run root config scenario
                return report |> Result.map Render.check
            })
    | "evidence" :: "import" :: _ ->
        runWithConfig [] (fun root config _ ->
            task {
                let! report = EvidenceImport.importAll root config
                return report |> Result.map Render.evidenceImport
            })
    | "evidence" :: "derive" :: _ ->
        runWithConfig [] (fun root config _ ->
            task {
                let! path = EvidenceImport.derive root config
                return path |> Result.map Render.evidenceDerive
            })
    | "evidence" :: "verify" :: _ ->
        runWithConfig [] (fun root config _ ->
            withTwinCatalog root config (fun catalog ->
                Result.success (Render.evidenceVerify (EvidenceImport.verify root config catalog))))
    | "evidence" :: _ ->
        emit
            (Render.failure
                [ ValidationError.create "twin.argv.verb" "evidence takes one of: import, derive, verify." ])
        exitArgv
    | "classify" :: _ ->
        runWithConfig [] (fun root config _ ->
            withTwinCatalog root config (fun catalog ->
                Classify.run root config catalog |> Result.map Render.classify))
    | "bake" :: _ ->
        runWithConfig [] (fun root config _ ->
            withTwinCatalog root config (fun catalog ->
                Bake.run root catalog |> Result.map Render.bake))
    | "down" :: _ ->
        runWithConfig [] (fun _ config _ ->
            task {
                let! outcome = Runs.down config
                return outcome |> Result.map (fun () -> Render.down ())
            })
    | "reset" :: _ ->
        runWithConfig [] (fun _ config _ ->
            task {
                let! outcome = Runs.reset config
                return outcome |> Result.map (fun () -> Render.reset ())
            })
    | verb :: _ ->
        emit
            (Render.failure
                [ ValidationError.createWithMetadata
                    "twin.argv.verb" "The verb is not recognized."
                    (Map.ofList [ "verb", Some verb ]) ])
        emit [ "" ]
        emit Render.usage
        exitArgv
