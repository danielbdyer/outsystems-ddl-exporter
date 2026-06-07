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
