module Projection.Tests.MovementSurfaceTests

open Xunit
open Projection.Core
open Projection.Pipeline

// Pure tests for the THE_CLI.md operator surface skeleton: the
// `projection.json` target-config parser (the aliasing the operator asked
// for), the `--to` resolver, and the argv → `Intent` dispatch. The engine
// execution + global-flag strip are later wiring slices; the spec algebra
// and the resolver are tested here, without a CLI dependency.

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private errCodes r = match r with Ok _ -> [] | Error es -> es |> List.map (fun (e: ValidationError) -> e.Code)

// -- TargetConfig.parse ----------------------------------------------------

let private sampleJson = """
{
  "targets": {
    "dev":     { "conn": "env:DEV_CONN", "store": "lifecycle/dev.json", "scope": "all" },
    "publish": { "dir": "./publish", "strategy": "fresh" }
  },
  "defaults": { "how": "merge" }
}
"""

[<Fact>]
let ``TargetConfig.parse reads a live target as a connection reference`` () =
    let cfg = TargetConfig.parse sampleJson |> mustOk
    let dev = Map.find "dev" cfg.Targets
    Assert.Equal(TargetAddress.LiveRef (ConnectionRef.EnvVar "DEV_CONN"), dev.Address)
    Assert.Equal(Some "lifecycle/dev.json", dev.Store)
    Assert.Equal(Some Scope.All, dev.Scope)

[<Fact>]
let ``TargetConfig.parse reads a folder target`` () =
    let cfg = TargetConfig.parse sampleJson |> mustOk
    let pub = Map.find "publish" cfg.Targets
    Assert.Equal(TargetAddress.FolderPath "./publish", pub.Address)
    Assert.Equal(Some Strategy.Fresh, pub.Strategy)

[<Fact>]
let ``TargetConfig.parse rejects an inline secret (D9)`` () =
    let json = """{ "targets": { "dev": { "conn": "Server=localhost;Password=hunter2" } } }"""
    Assert.Contains("cli.config.targetSecretInline", errCodes (TargetConfig.parse json))

[<Fact>]
let ``TargetConfig.parse rejects a target with neither conn nor dir`` () =
    let json = """{ "targets": { "dev": { "store": "x.json" } } }"""
    Assert.Contains("cli.config.targetAddressMissing", errCodes (TargetConfig.parse json))

[<Fact>]
let ``TargetConfig.parse on empty text is the empty config`` () =
    let cfg = TargetConfig.parse "" |> mustOk
    Assert.True(Map.isEmpty cfg.Targets)

// -- Surface.resolveTarget -------------------------------------------------

let private cfg = TargetConfig.parse sampleJson |> mustOk

[<Fact>]
let ``resolveTarget reserves docker`` () =
    let r = Surface.resolveTarget cfg "docker" |> mustOk
    Assert.Equal(Destination.Docker, r.Destination)

[<Fact>]
let ``resolveTarget resolves a named live target to Live`` () =
    let r = Surface.resolveTarget cfg "dev" |> mustOk
    Assert.Equal(Destination.Live (ConnectionRef.EnvVar "DEV_CONN"), r.Destination)
    Assert.Equal(Some "lifecycle/dev.json", r.Store)

[<Fact>]
let ``resolveTarget honors the dir scheme prefix over a same-named target`` () =
    // a folder literally named "dev" is reachable via dir: even though "dev" is a target
    let r = Surface.resolveTarget cfg "dir:dev" |> mustOk
    Assert.Equal(Destination.Folder "dev", r.Destination)

[<Fact>]
let ``resolveTarget treats a path-shaped value as a folder`` () =
    let r = Surface.resolveTarget cfg "./out" |> mustOk
    Assert.Equal(Destination.Folder "./out", r.Destination)

[<Fact>]
let ``resolveTarget refuses an unknown bare name`` () =
    Assert.Contains("cli.to.unknownTarget", errCodes (Surface.resolveTarget cfg "staging"))

// -- Surface.parse (argv -> Intent) ----------------------------------------

[<Fact>]
let ``parse project --to dev defaults to all+merge, preview (not committed)`` () =
    match Surface.parse cfg [ "project"; "--to"; "dev" ] |> mustOk with
    | Intent.Project spec ->
        Assert.Equal(Destination.Live (ConnectionRef.EnvVar "DEV_CONN"), spec.Destination)
        Assert.Equal(Scope.All, spec.Scope)
        Assert.Equal(Strategy.Merge, spec.Strategy)
        Assert.False spec.Commit
        Assert.False(MovementSpec.isLiveWrite spec)
    | other -> Assert.Fail(sprintf "expected Project, got %A" other)

[<Fact>]
let ``parse project --to dev --go is a live write`` () =
    match Surface.parse cfg [ "project"; "--to"; "dev"; "--go" ] |> mustOk with
    | Intent.Project spec ->
        Assert.True spec.Commit
        Assert.True(MovementSpec.isLiveWrite spec)
    | other -> Assert.Fail(sprintf "expected Project, got %A" other)

[<Fact>]
let ``parse project folds --data alias into a transfer ingest`` () =
    match Surface.parse cfg [ "project"; "--to"; "uat-or-path/x"; "--data"; "qa"; "--rekey"; "users.csv" ] |> mustOk with
    | Intent.Project spec ->
        Assert.Equal(DataOrigin.FromTarget "qa", spec.Data)
        Assert.Equal(Some "users.csv", spec.Rekey)
    | other -> Assert.Fail(sprintf "expected Project, got %A" other)

[<Fact>]
let ``parse project collects repeated --reconcile entries`` () =
    match Surface.parse cfg [ "project"; "--to"; "dev"; "--data"; "qa"; "--reconcile"; "User:Email"; "--reconcile"; "Team:Code"; "--go" ] |> mustOk with
    | Intent.Project spec -> Assert.Equal<string list>([ "User:Email"; "Team:Code" ], spec.Reconcile)
    | other -> Assert.Fail(sprintf "expected Project, got %A" other)

[<Fact>]
let ``parse project --scope data is carried for the DML-only route`` () =
    match Surface.parse cfg [ "project"; "--to"; "dev"; "--scope"; "data"; "--data"; "qa"; "--go" ] |> mustOk with
    | Intent.Project spec ->
        Assert.Equal(Scope.Data, spec.Scope)
        Assert.Equal(DataOrigin.FromTarget "qa", spec.Data)
    | other -> Assert.Fail(sprintf "expected Project, got %A" other)

[<Fact>]
let ``parse project --shape skeleton selects the pre-overlay shape`` () =
    match Surface.parse cfg [ "project"; "--to"; "./out"; "--shape"; "skeleton" ] |> mustOk with
    | Intent.Project spec -> Assert.Equal(Shape.Skeleton, spec.Shape)
    | other -> Assert.Fail(sprintf "expected Project, got %A" other)

[<Fact>]
let ``parse project rejects an unknown --how`` () =
    Assert.Contains("cli.strategy.unknown", errCodes (Surface.parse cfg [ "project"; "--to"; "dev"; "--how"; "upsert" ]))

[<Fact>]
let ``CLI --how overrides a target's configured strategy`` () =
    // publish has strategy=fresh in config; an explicit --how merge wins
    match Surface.parse cfg [ "project"; "--to"; "publish"; "--how"; "merge" ] |> mustOk with
    | Intent.Project spec -> Assert.Equal(Strategy.Merge, spec.Strategy)
    | other -> Assert.Fail(sprintf "expected Project, got %A" other)

[<Fact>]
let ``a target's configured strategy fills when the CLI is silent`` () =
    match Surface.parse cfg [ "project"; "--to"; "publish" ] |> mustOk with
    | Intent.Project spec -> Assert.Equal(Strategy.Fresh, spec.Strategy)
    | other -> Assert.Fail(sprintf "expected Project, got %A" other)

[<Fact>]
let ``parse routes check to the proof plane carrying its tail`` () =
    match Surface.parse cfg [ "check"; "drift"; "--to"; "dev" ] |> mustOk with
    | Intent.Check args -> Assert.Equal<string list>([ "drift"; "--to"; "dev" ], args)
    | other -> Assert.Fail(sprintf "expected Check, got %A" other)

[<Fact>]
let ``parse refuses an unknown verb`` () =
    Assert.Contains("cli.verb.unknown", errCodes (Surface.parse cfg [ "deploy"; "model.json" ]))

// -- proteins parse (THE_CLI.md §8 — the documented one-liners stay honest) ---

// A config with the four named targets the proteins use + a default model, so
// `project --to dev` needs no model path (THE_CLI.md §8 / §4).
let private proteinCfg =
    TargetConfig.parse """
    {
      "targets": {
        "dev": { "conn": "env:DEV_CONN" }, "qa": { "conn": "env:QA_CONN" },
        "uat": { "conn": "env:UAT_CONN" }, "publish": { "dir": "./publish" }
      },
      "model": "model.json"
    }
    """ |> mustOk

let private proteinProject (argv: string list) : MovementSpec =
    match Surface.parse proteinCfg argv |> mustOk with
    | Intent.Project spec -> spec
    | other -> failwithf "expected Project, got %A" other

[<Fact>]
let ``protein P-1 (Dev load): project --to dev --go`` () =
    let s = proteinProject [ "project"; "--to"; "dev"; "--go" ]
    Assert.Equal(Destination.Live (ConnectionRef.EnvVar "DEV_CONN"), s.Destination)
    Assert.Equal(ModelSource.ModelFile "model.json", s.Model)
    Assert.True s.Commit

[<Fact>]
let ``protein P-3 (UAT re-key): project --to uat --rekey users.csv --go`` () =
    let s = proteinProject [ "project"; "--to"; "uat"; "--rekey"; "users.csv"; "--go" ]
    Assert.Equal(Destination.Live (ConnectionRef.EnvVar "UAT_CONN"), s.Destination)
    Assert.Equal(Some "users.csv", s.Rekey)
    Assert.True s.Commit

[<Fact>]
let ``protein P-4 (SSIS publication): project --to publish`` () =
    let s = proteinProject [ "project"; "--to"; "publish" ]
    Assert.Equal(Destination.Folder "./publish", s.Destination)
    Assert.False s.Commit

[<Fact>]
let ``protein P-5/P-6 (idempotent redeploy / in-place migrate) = the P-1 command`` () =
    Assert.Equal(
        proteinProject [ "project"; "--to"; "dev"; "--go" ],
        proteinProject [ "project"; "--to"; "dev"; "--go" ])

[<Fact>]
let ``protein (Docker one-touch): project --to docker`` () =
    Assert.Equal(Destination.Docker, (proteinProject [ "project"; "--to"; "docker" ]).Destination)

[<Fact>]
let ``protein (Faker schema): project --to docker --data synthetic`` () =
    let s = proteinProject [ "project"; "--to"; "docker"; "--data"; "synthetic" ]
    Assert.Equal(Destination.Docker, s.Destination)
    Assert.Equal(DataOrigin.Synthetic, s.Data)

[<Fact>]
let ``protein (DB to DB transfer): project --to uat --data qa --rekey users.csv --go`` () =
    let s = proteinProject [ "project"; "--to"; "uat"; "--data"; "qa"; "--rekey"; "users.csv"; "--go" ]
    Assert.Equal(Destination.Live (ConnectionRef.EnvVar "UAT_CONN"), s.Destination)
    Assert.Equal(DataOrigin.FromTarget "qa", s.Data)
    Assert.Equal(Some "users.csv", s.Rekey)
    Assert.True s.Commit

[<Fact>]
let ``protein (Skeleton): project --to ./out --shape skeleton`` () =
    let s = proteinProject [ "project"; "--to"; "./out"; "--shape"; "skeleton" ]
    Assert.Equal(Destination.Folder "./out", s.Destination)
    Assert.Equal(Shape.Skeleton, s.Shape)

// -- planProject routing (THE_CLI fidelity #1 — the pure surface→engine map) --

let private planOf (spec: MovementSpec) : PlanAction = (Surface.planProject proteinCfg spec).Action
let private liveDev = Destination.Live (ConnectionRef.EnvVar "DEV_CONN")
let private baseLive = MovementSpec.forDestination liveDev

[<Fact>]
let ``planProject: folder + config → PublishBundle`` () =
    let s = { MovementSpec.forDestination (Destination.Folder "./o") with Model = ModelSource.ConfigFile "c.json" }
    Assert.Equal(PlanAction.PublishBundle ("c.json", "./o"), planOf s)

[<Fact>]
let ``planProject: folder + model + shape routes Skeleton vs Bundle`` () =
    let folderModel shape = { MovementSpec.forDestination (Destination.Folder "./o") with Model = ModelSource.ModelFile "m.json"; Shape = shape }
    Assert.Equal(PlanAction.EmitSkeleton ("m.json", "./o"), planOf (folderModel Shape.Skeleton))
    Assert.Equal(PlanAction.EmitBundle ("m.json", "./o"), planOf (folderModel Shape.Bundle))

[<Fact>]
let ``planProject: docker + model → DeployDocker; no model → Refused`` () =
    Assert.Equal(PlanAction.DeployDocker (ModelSource.ModelFile "m.json"),
                 planOf { MovementSpec.forDestination Destination.Docker with Model = ModelSource.ModelFile "m.json" })
    match planOf (MovementSpec.forDestination Destination.Docker) with
    | PlanAction.Refused (1, _) -> ()
    | other -> Assert.Fail(sprintf "expected Refused 1, got %A" other)

[<Fact>]
let ``planProject: live preview (no --go) → schema plan, or data plan with --data`` () =
    Assert.Equal(PlanAction.PreviewSchema (ModelSource.ModelFile "model.json", "env:DEV_CONN"),
                 planOf { baseLive with Model = ModelSource.ModelFile "model.json" })
    Assert.Equal(PlanAction.PreviewData ("env:QA_CONN", "env:DEV_CONN"),
                 planOf { baseLive with Data = DataOrigin.FromTarget "qa" })

[<Fact>]
let ``planProject: live --go routes migrate / migrate-with-data / transfer / publish-load`` () =
    let go = { baseLive with Commit = true; Model = ModelSource.ModelFile "model.json" }
    Assert.Equal(PlanAction.Migrate (ModelSource.ModelFile "model.json", "env:DEV_CONN"), planOf go)
    Assert.Equal(PlanAction.MigrateWithData (ModelSource.ModelFile "model.json", "env:DEV_CONN", "env:QA_CONN"),
                 planOf { go with Data = DataOrigin.FromTarget "qa" })
    Assert.Equal(PlanAction.TransferData ("env:QA_CONN", "env:DEV_CONN"),
                 planOf { go with Data = DataOrigin.FromTarget "qa"; Scope = Scope.Data })
    Assert.Equal(PlanAction.PublishAndLoad ("c.json", "env:DEV_CONN"),
                 planOf { go with Model = ModelSource.ConfigFile "c.json" })

[<Fact>]
let ``planProject: --scope data --go without a data source is Refused`` () =
    match planOf { baseLive with Commit = true; Scope = Scope.Data; Model = ModelSource.ModelFile "m.json" } with
    | PlanAction.Refused (2, _) -> ()
    | other -> Assert.Fail(sprintf "expected Refused 2, got %A" other)

[<Fact>]
let ``planProject: a --data alias that is not a live target is Refused`` () =
    // "publish" is a folder target, not live → cannot be a data source.
    match planOf { baseLive with Commit = true; Data = DataOrigin.FromTarget "publish" } with
    | PlanAction.Refused (6, _) -> ()
    | other -> Assert.Fail(sprintf "expected Refused 6, got %A" other)

[<Fact>]
let ``planProject is TOTAL: every axis combination yields a well-formed plan`` () =
    // The whole product of the routing axes. The match is exhaustive (compile-
    // time), so this proves no combination throws and every Refused is named
    // with a known exit code — the registered⇔executed discipline for the CLI.
    let destinations = [ Destination.Folder "./o"; Destination.Docker; liveDev ]
    let scopes       = [ Scope.All; Scope.Schema; Scope.Data ]
    let datas        = [ DataOrigin.Model; DataOrigin.Synthetic; DataOrigin.NoData; DataOrigin.FromTarget "qa"; DataOrigin.FromTarget "publish" ]
    let models       = [ ModelSource.ModelFile "m.json"; ModelSource.ConfigFile "c.json"; ModelSource.Unspecified ]
    let commits      = [ true; false ]
    let mutable n = 0
    for dest in destinations do
      for scope in scopes do
        for data in datas do
          for model in models do
            for commit in commits do
                let spec = { MovementSpec.forDestination dest with Scope = scope; Data = data; Model = model; Commit = commit }
                let plan = Surface.planProject proteinCfg spec
                n <- n + 1
                match plan.Action with
                | PlanAction.Refused (code, msg) ->
                    Assert.Contains(code, [ 1; 2; 6 ])
                    Assert.False(System.String.IsNullOrWhiteSpace msg)
                | _ -> ()  // any engine action is well-formed by construction
    Assert.Equal(3 * 3 * 5 * 3 * 2, n)
