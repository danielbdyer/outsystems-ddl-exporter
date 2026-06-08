module Projection.Tests.MovementSurfaceTests

open Xunit
open Projection.Core
open Projection.Pipeline

// Pure tests for the THE_CLI.md (2026-06-08) operator surface: the
// `projection.json` two-layer config parser (environments + flows), the
// flow → MovementSpec resolution + the grant gate, the pure surface→engine
// routing (`planMovement`), and the `projection <flow>` dispatch. The engine
// execution + global-flag strip are the wiring slice; the algebra is here.

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private errCodes r = match r with Ok _ -> [] | Error es -> es |> List.map (fun (e: ValidationError) -> e.Code)

// -- ProjectionConfig.parse: environments + flows --------------------------

let private envFlowJson = """
{
  "environments": {
    "cloud-dev":  { "access": "direct", "conn": "env:CLOUD_DEV_CONN" },
    "onprem-uat": { "access": "bundle", "out": "dist/onprem-uat", "grant": "schema+data" },
    "cloud-uat":  { "access": "direct", "conn": "env:CLOUD_UAT_CONN", "grant": "data" },
    "docker":     { "access": "docker", "grant": "schema+data" }
  },
  "flows": {
    "uat":    { "from": "cloud-dev", "to": "onprem-uat", "rekey": "file:users.csv" },
    "golden": { "from": "cloud-qa",  "to": "cloud-uat",  "tables": ["Customer", "Order"] },
    "synth":  { "from": "synthetic", "to": "cloud-uat",  "profile": "onprem-legacy" },
    "plain":  { "to": "onprem-uat" }
  }
}
"""

[<Fact>]
let ``parse on empty text is the empty config`` () =
    let cfg = ProjectionConfig.parse "" |> mustOk
    Assert.True(Map.isEmpty cfg.Environments)
    Assert.True(Map.isEmpty cfg.Flows)

[<Fact>]
let ``config parses a direct source environment (no grant)`` () =
    let cfg = ProjectionConfig.parse envFlowJson |> mustOk
    let e = Map.find "cloud-dev" cfg.Environments
    Assert.Equal(Access.Direct (ConnectionRef.EnvVar "CLOUD_DEV_CONN"), e.Access)
    Assert.Equal(None, e.Grant)

[<Fact>]
let ``config parses a bundle target with schema+data grant`` () =
    let e = Map.find "onprem-uat" (ProjectionConfig.parse envFlowJson |> mustOk).Environments
    Assert.Equal(Access.Bundle "dist/onprem-uat", e.Access)
    Assert.Equal(Some Grant.SchemaAndData, e.Grant)

[<Fact>]
let ``config parses a direct data-only target`` () =
    let e = Map.find "cloud-uat" (ProjectionConfig.parse envFlowJson |> mustOk).Environments
    Assert.Equal(Some Grant.DataOnly, e.Grant)

[<Fact>]
let ``config parses a docker environment`` () =
    let e = Map.find "docker" (ProjectionConfig.parse envFlowJson |> mustOk).Environments
    Assert.Equal(Access.Docker, e.Access)

[<Fact>]
let ``config refuses an inline secret in an environment conn (D9)`` () =
    let json = """{ "environments": { "x": { "access": "direct", "conn": "Server=h;Password=p" } } }"""
    Assert.Contains("cli.config.envSecretInline", errCodes (ProjectionConfig.parse json))

[<Fact>]
let ``config refuses a bundle environment without out`` () =
    let json = """{ "environments": { "x": { "access": "bundle", "grant": "data" } } }"""
    Assert.Contains("cli.config.envBundleNoOut", errCodes (ProjectionConfig.parse json))

[<Fact>]
let ``config refuses a direct environment without conn`` () =
    let json = """{ "environments": { "x": { "access": "direct" } } }"""
    Assert.Contains("cli.config.envDirectNoConn", errCodes (ProjectionConfig.parse json))

[<Fact>]
let ``config refuses an unknown access`` () =
    let json = """{ "environments": { "x": { "access": "ftp", "out": "d" } } }"""
    Assert.Contains("cli.config.envAccessUnknown", errCodes (ProjectionConfig.parse json))

[<Fact>]
let ``config refuses an unknown grant`` () =
    let json = """{ "environments": { "x": { "access": "docker", "grant": "root" } } }"""
    Assert.Contains("cli.config.envGrantUnknown", errCodes (ProjectionConfig.parse json))

[<Fact>]
let ``config parses a flow's source environment and rekey`` () =
    let f = Map.find "uat" (ProjectionConfig.parse envFlowJson |> mustOk).Flows
    Assert.Equal(FlowSource.Env "cloud-dev", f.From)
    Assert.Equal("onprem-uat", f.To)
    Assert.Equal(Some "file:users.csv", f.Rekey)

[<Fact>]
let ``config parses a flow's declared table subset`` () =
    let f = Map.find "golden" (ProjectionConfig.parse envFlowJson |> mustOk).Flows
    Assert.Equal<string list>([ "Customer"; "Order" ], f.Tables)

[<Fact>]
let ``config parses a synthetic flow with a profile`` () =
    let f = Map.find "synth" (ProjectionConfig.parse envFlowJson |> mustOk).Flows
    Assert.Equal(FlowSource.Synthetic (Some "onprem-legacy"), f.From)

[<Fact>]
let ``config defaults a flow with no from to the model`` () =
    let f = Map.find "plain" (ProjectionConfig.parse envFlowJson |> mustOk).Flows
    Assert.Equal(FlowSource.Model, f.From)

[<Fact>]
let ``config refuses a flow without a to`` () =
    let json = """{ "flows": { "x": { "from": "dev" } } }"""
    Assert.Contains("cli.config.flowNoTo", errCodes (ProjectionConfig.parse json))

// -- planMovement routing (the pure surface→engine map) --------------------

// A config whose environments back the data-source aliases the routing tests
// exercise: `qa` (a live read source) and `pub` (a bundle target — not live).
let private routeCfg =
    ProjectionConfig.parse """
    {
      "environments": {
        "qa":  { "access": "direct", "conn": "env:QA_CONN" },
        "pub": { "access": "bundle", "out": "dist/pub", "grant": "schema+data" }
      },
      "model": "model.json"
    }
    """ |> mustOk

let private planOf (spec: MovementSpec) : PlanAction = (Command.planMovement routeCfg spec).Action
let private liveDev = Destination.Live (ConnectionRef.EnvVar "DEV_CONN")
let private baseLive = MovementSpec.forDestination liveDev
let private defaultOpts : LoadOpts =
    { Declaration = DeclareNone; Emission = EmissionMode.Incremental
      Reconcile = []; Rekey = None; AllowCdc = false; Store = None; Env = None; Tables = [] }

[<Fact>]
let ``planMovement: --fresh selects WipeAndLoad on the transfer path`` () =
    match planOf { baseLive with Commit = true; Scope = Scope.Data; Data = DataOrigin.FromTarget "qa"; Strategy = Strategy.Replace } with
    | PlanAction.Transfer (_, _, opts, true) -> Assert.Equal(EmissionMode.WipeAndLoad, opts.Emission)
    | other -> Assert.Fail(sprintf "expected Transfer, got %A" other)

[<Fact>]
let ``planMovement: --fresh on a non-transfer action is noted, never silently ignored`` () =
    let p = Command.planMovement routeCfg { baseLive with Commit = true; Model = ModelSource.ModelFile "m.json"; Strategy = Strategy.Replace }
    Assert.Contains(p.Notes, fun (n: string) -> n.Contains "--fresh")

[<Fact>]
let ``planMovement: folder + config → PublishBundle`` () =
    let s = { MovementSpec.forDestination (Destination.Folder "./o") with Model = ModelSource.ConfigFile "c.json" }
    Assert.Equal(PlanAction.PublishBundle ("c.json", "./o", None, None), planOf s)

[<Fact>]
let ``planMovement: folder + model + shape routes Skeleton vs Bundle`` () =
    let folderModel shape = { MovementSpec.forDestination (Destination.Folder "./o") with Model = ModelSource.ModelFile "m.json"; Shape = shape }
    Assert.Equal(PlanAction.EmitSkeleton ("m.json", "./o"), planOf (folderModel Shape.Skeleton))
    Assert.Equal(PlanAction.EmitBundle ("m.json", "./o"), planOf (folderModel Shape.Bundle))

[<Fact>]
let ``planMovement: docker + model → DeployDocker; no model → Refused`` () =
    Assert.Equal(PlanAction.DeployDocker (ModelSource.ModelFile "m.json"),
                 planOf { MovementSpec.forDestination Destination.Docker with Model = ModelSource.ModelFile "m.json" })
    match planOf (MovementSpec.forDestination Destination.Docker) with
    | PlanAction.Refused (1, _) -> ()
    | other -> Assert.Fail(sprintf "expected Refused 1, got %A" other)

[<Fact>]
let ``planMovement: live preview (no --go) → schema plan, or data plan with a source`` () =
    Assert.Equal(PlanAction.PreviewSchema (ModelSource.ModelFile "model.json", "env:DEV_CONN", DeclareNone),
                 planOf { baseLive with Model = ModelSource.ModelFile "model.json" })
    Assert.Equal(PlanAction.Transfer ("env:QA_CONN", "env:DEV_CONN", defaultOpts, false),
                 planOf { baseLive with Data = DataOrigin.FromTarget "qa" })

[<Fact>]
let ``planMovement: live --go routes migrate / migrate-with-data / transfer / publish-load`` () =
    let go = { baseLive with Commit = true; Model = ModelSource.ModelFile "model.json" }
    Assert.Equal(PlanAction.Migrate (ModelSource.ModelFile "model.json", "env:DEV_CONN", defaultOpts), planOf go)
    Assert.Equal(PlanAction.MigrateWithData (ModelSource.ModelFile "model.json", "env:DEV_CONN", "env:QA_CONN", defaultOpts),
                 planOf { go with Data = DataOrigin.FromTarget "qa" })
    Assert.Equal(PlanAction.Transfer ("env:QA_CONN", "env:DEV_CONN", defaultOpts, true),
                 planOf { go with Data = DataOrigin.FromTarget "qa"; Scope = Scope.Data })
    Assert.Equal(PlanAction.PublishAndLoad ("c.json", "env:DEV_CONN", None, None),
                 planOf { go with Model = ModelSource.ConfigFile "c.json" })

[<Fact>]
let ``planMovement: --scope data --go without a data source is Refused`` () =
    match planOf { baseLive with Commit = true; Scope = Scope.Data; Model = ModelSource.ModelFile "m.json" } with
    | PlanAction.Refused (2, _) -> ()
    | other -> Assert.Fail(sprintf "expected Refused 2, got %A" other)

[<Fact>]
let ``planMovement: a data source that is not a live environment is Refused`` () =
    // "pub" is a bundle target, not live → cannot be a data source.
    match planOf { baseLive with Commit = true; Data = DataOrigin.FromTarget "pub" } with
    | PlanAction.Refused (6, _) -> ()
    | other -> Assert.Fail(sprintf "expected Refused 6, got %A" other)

[<Fact>]
let ``planMovement is TOTAL across the destination × scope × data × model × commit product`` () =
    // No combination throws; every Refused is a named exit code + coded error.
    let destinations = [ Destination.Folder "./o"; Destination.Docker; liveDev ]
    let scopes       = [ Scope.All; Scope.Schema; Scope.Data ]
    let datas        = [ DataOrigin.Model; DataOrigin.Synthetic "file:p.json"; DataOrigin.NoData; DataOrigin.FromTarget "qa"; DataOrigin.FromTarget "pub" ]
    let models       = [ ModelSource.ModelFile "m.json"; ModelSource.ConfigFile "c.json"; ModelSource.Unspecified ]
    let commits      = [ true; false ]
    let mutable n = 0
    for dest in destinations do
      for scope in scopes do
        for data in datas do
          for model in models do
            for commit in commits do
                let spec = { MovementSpec.forDestination dest with Scope = scope; Data = data; Model = model; Commit = commit }
                n <- n + 1
                match (Command.planMovement routeCfg spec).Action with
                | PlanAction.Refused (code, error) ->
                    Assert.Contains(code, [ 1; 2; 6 ])
                    Assert.False(System.String.IsNullOrWhiteSpace error.Message)
                    Assert.False(System.String.IsNullOrWhiteSpace error.Code)
                | _ -> ()
    Assert.Equal(3 * 3 * 5 * 3 * 2, n)

// -- flow resolution (THE_CLI.md 2026-06-08; slice F2) ----------------------

let private flowCfg =
    ProjectionConfig.parse """
    {
      "environments": {
        "cloud-dev":  { "access": "direct", "conn": "env:CLOUD_DEV_CONN" },
        "cloud-qa":   { "access": "direct", "conn": "env:CLOUD_QA_CONN" },
        "onprem-uat": { "access": "bundle", "out": "dist/onprem-uat", "grant": "schema+data", "store": "lifecycle/uat.json" },
        "cloud-uat":  { "access": "direct", "conn": "env:CLOUD_UAT_CONN", "grant": "data" },
        "lab":        { "access": "docker", "grant": "schema+data" }
      },
      "flows": {
        "uat":     { "from": "cloud-dev", "to": "onprem-uat", "rekey": "file:users.csv" },
        "golden":  { "from": "cloud-qa",  "to": "cloud-uat",  "tables": ["Customer"] },
        "badsrc":  { "from": "onprem-uat","to": "cloud-uat" },
        "lift-uat":{ "from": "model",     "to": "cloud-uat" },
        "spin":    { "from": "model",     "to": "lab" }
      },
      "model": "model.json"
    }
    """ |> mustOk

let private preview = { Go = false; Fresh = false; AllowDrops = false; AllowCdc = false }
let private commit  = { preview with Go = true }
let private flowOf name = Map.find name flowCfg.Flows
let private specOf name opts = Command.resolveFlowSpec flowCfg (flowOf name) opts
let private actionOf name opts = (Command.planFlow flowCfg (flowOf name) opts).Action

[<Fact>]
let ``flow lift-and-shift to a bundle target resolves schema+data → folder bundle`` () =
    match specOf "uat" preview with
    | Ok s ->
        Assert.Equal(Destination.Folder "dist/onprem-uat", s.Destination)
        Assert.Equal(Scope.All, s.Scope)
        Assert.Equal(Strategy.Merge, s.Strategy)
        Assert.Equal(Some "file:users.csv", s.Rekey)
    | Error es -> Assert.Fail(sprintf "%A" es)
    Assert.Equal(PlanAction.EmitBundle ("model.json", "dist/onprem-uat"), actionOf "uat" preview)

[<Fact>]
let ``flow golden (data-only target, from env) → transfer; --go executes`` () =
    match actionOf "golden" preview with
    | PlanAction.Transfer ("env:CLOUD_QA_CONN", "env:CLOUD_UAT_CONN", _, false) -> ()
    | other -> Assert.Fail(sprintf "expected preview Transfer, got %A" other)
    match actionOf "golden" commit with
    | PlanAction.Transfer ("env:CLOUD_QA_CONN", "env:CLOUD_UAT_CONN", _, true) -> ()
    | other -> Assert.Fail(sprintf "expected executing Transfer, got %A" other)

[<Fact>]
let ``flow golden: the table subset is honored on the transfer opts (item 5)`` () =
    match specOf "golden" preview with
    | Ok s -> Assert.Equal<string list>([ "Customer" ], s.Tables)
    | Error es -> Assert.Fail(sprintf "%A" es)
    match actionOf "golden" commit with
    | PlanAction.Transfer (_, _, opts, _) -> Assert.Equal<string list>([ "Customer" ], opts.Tables)
    | other -> Assert.Fail(sprintf "expected Transfer, got %A" other)
    // honored on the transfer leg → no pending note.
    Assert.DoesNotContain((Command.planFlow flowCfg (flowOf "golden") commit).Notes, fun (n: string) -> n.Contains "tables")

[<Fact>]
let ``flow tables on a non-transfer action is noted (data-transfer leg only)`` () =
    let bt = { Name = "bt"; From = FlowSource.Model; To = "onprem-uat"; Rekey = None; Tables = [ "Customer" ] }
    Assert.Contains((Command.planFlow flowCfg bt preview).Notes, fun (n: string) -> n.Contains "data-transfer leg only")

[<Fact>]
let ``flow grant gate: schema-from-model against a data-only target is Refused (9)`` () =
    match actionOf "lift-uat" commit with
    | PlanAction.Refused (9, e) -> Assert.Equal("cli.flow.grantSchemaRefused", e.Code)
    | other -> Assert.Fail(sprintf "expected grant refusal, got %A" other)

[<Fact>]
let ``flow with a non-direct source is refused`` () =
    Assert.Contains("cli.flow.fromNotDirect", errCodes (specOf "badsrc" preview))

[<Fact>]
let ``flow --fresh selects the wipe-and-load posture and an empty baseline`` () =
    match specOf "spin" { preview with Fresh = true } with
    | Ok s ->
        Assert.Equal(Strategy.Fresh, s.Strategy)
        Assert.Equal(Baseline.Empty, s.Baseline)
    | Error es -> Assert.Fail(sprintf "%A" es)

[<Fact>]
let ``flow to a docker environment → DeployDocker`` () =
    Assert.Equal(PlanAction.DeployDocker (ModelSource.ModelFile "model.json"), actionOf "spin" preview)

[<Fact>]
let ``flow --allow-drops threads the declared-loss acceptance`` () =
    match specOf "spin" { preview with AllowDrops = true } with
    | Ok s -> Assert.True s.AllowDrops
    | Error es -> Assert.Fail(sprintf "%A" es)

[<Fact>]
let ``planFlow is TOTAL across the flows × per-run intents`` () =
    let opts = [ preview; commit; { preview with Fresh = true }; { commit with AllowDrops = true } ]
    let mutable n = 0
    for name in (flowCfg.Flows |> Map.toList |> List.map fst) do
      for o in opts do
        match (Command.planFlow flowCfg (flowOf name) o).Action with
        | PlanAction.Refused (code, error) ->
            Assert.Contains(code, [ 1; 6; 9 ])
            Assert.False(System.String.IsNullOrWhiteSpace error.Message)
            Assert.False(System.String.IsNullOrWhiteSpace error.Code)
        | _ -> ()
        n <- n + 1
    Assert.Equal(5 * 4, n)

// -- synthetic flow wiring (THE_SYNTHETIC_DATA_DESIGN §8 / §11) --------------

let private synthCfg =
    ProjectionConfig.parse """
    {
      "environments": {
        "cloud-uat": { "access": "direct", "conn": "env:CLOUD_UAT_CONN", "grant": "data" },
        "cloud-all": { "access": "direct", "conn": "env:CLOUD_ALL_CONN" }
      },
      "flows": {
        "preview-synth": { "from": "synthetic", "profile": "file:legacy.profile.json", "to": "cloud-uat" },
        "synth-all":     { "from": "synthetic", "profile": "file:legacy.profile.json", "to": "cloud-all" },
        "synth-noprof":  { "from": "synthetic", "to": "cloud-uat" }
      },
      "model": "model.json"
    }
    """ |> mustOk

let private synthAction name opts = (Command.planFlow synthCfg (Map.find name synthCfg.Flows) opts).Action
let private specOfIn cfg name opts = Command.resolveFlowSpec cfg (Map.find name (cfg: ProjectionConfig).Flows) opts

[<Fact>]
let ``synthetic flow (data-only target) → SynthesizeAndLoad; preview vs --go`` () =
    match synthAction "preview-synth" preview with
    | PlanAction.SynthesizeAndLoad (ModelSource.ModelFile "model.json", "file:legacy.profile.json", "env:CLOUD_UAT_CONN", _, false) -> ()
    | other -> Assert.Fail(sprintf "expected preview SynthesizeAndLoad, got %A" other)
    match synthAction "preview-synth" commit with
    | PlanAction.SynthesizeAndLoad (_, "file:legacy.profile.json", "env:CLOUD_UAT_CONN", _, true) -> ()
    | other -> Assert.Fail(sprintf "expected executing SynthesizeAndLoad, got %A" other)

[<Fact>]
let ``synthetic flow carries the profile ref through resolveFlowSpec`` () =
    match specOfIn synthCfg "preview-synth" preview with
    | Ok s -> Assert.Equal(DataOrigin.Synthetic "file:legacy.profile.json", s.Data)
    | Error es -> Assert.Fail(sprintf "%A" es)

[<Fact>]
let ``synthetic flow without a profile is Refused (named, not silent)`` () =
    match synthAction "synth-noprof" preview with
    | PlanAction.Refused (6, e) -> Assert.Equal("cli.flow.syntheticNoProfile", e.Code)
    | other -> Assert.Fail(sprintf "expected synthetic-no-profile refusal, got %A" other)

[<Fact>]
let ``synthetic flow preview works under all-scope; --go to a non-data target is Refused`` () =
    match synthAction "synth-all" preview with
    | PlanAction.SynthesizeAndLoad (_, _, "env:CLOUD_ALL_CONN", _, false) -> ()
    | other -> Assert.Fail(sprintf "expected all-scope preview SynthesizeAndLoad, got %A" other)
    match synthAction "synth-all" commit with
    | PlanAction.Refused (2, e) -> Assert.Equal("cli.move.syntheticScope", e.Code)
    | other -> Assert.Fail(sprintf "expected synthetic-scope refusal, got %A" other)

// -- profile capture verb (THE_SYNTHETIC_DATA_DESIGN §2.2) -------------------

let private planArgs cfg argv = (Command.plan cfg (Command.parse cfg argv |> mustOk)).Action

[<Fact>]
let ``profile <env> --out routes to CaptureProfile`` () =
    match planArgs synthCfg [ "profile"; "cloud-uat"; "--out"; "legacy.profile.json" ] with
    | PlanAction.CaptureProfile ("env:CLOUD_UAT_CONN", "legacy.profile.json") -> ()
    | other -> Assert.Fail(sprintf "expected CaptureProfile, got %A" other)

[<Fact>]
let ``profile without --out is Refused (named)`` () =
    match planArgs synthCfg [ "profile"; "cloud-uat" ] with
    | PlanAction.Refused (2, e) -> Assert.Equal("cli.profile.noOut", e.Code)
    | other -> Assert.Fail(sprintf "expected --out refusal, got %A" other)

[<Fact>]
let ``profile without an environment is Refused (named)`` () =
    match planArgs synthCfg [ "profile"; "--out"; "x.json" ] with
    | PlanAction.Refused (2, e) -> Assert.Equal("cli.profile.noEnv", e.Code)
    | other -> Assert.Fail(sprintf "expected no-env refusal, got %A" other)

[<Fact>]
let ``profile against an unknown environment is Refused`` () =
    match planArgs synthCfg [ "profile"; "nope"; "--out"; "x.json" ] with
    | PlanAction.Refused (6, _) -> ()
    | other -> Assert.Fail(sprintf "expected unknown-env refusal, got %A" other)

// -- dispatch (THE_CLI.md 2026-06-08; slice F3) -----------------------------

let private parseFlowIntent argv =
    match Command.parse flowCfg argv |> mustOk with
    | Intent.Flow (f, o) -> (f, o)
    | other -> failwithf "expected Flow, got %A" other

[<Fact>]
let ``parse: a bare flow name dispatches to Intent.Flow (verb implied)`` () =
    let (f, o) = parseFlowIntent [ "uat" ]
    Assert.Equal("uat", f.Name)
    Assert.False o.Go
    Assert.False o.Fresh
    Assert.False o.AllowDrops

[<Fact>]
let ``parse: --go --fresh --allow-drops --allow-cdc set the per-run intent`` () =
    let (_, o) = parseFlowIntent [ "golden"; "--go"; "--fresh"; "--allow-drops"; "--allow-cdc" ]
    Assert.True o.Go
    Assert.True o.Fresh
    Assert.True o.AllowDrops
    Assert.True o.AllowCdc

[<Fact>]
let ``flow --allow-cdc threads the CDC-gate override onto the spec (item 3)`` () =
    match specOf "spin" { preview with AllowCdc = true } with
    | Ok s -> Assert.True s.AllowCdc
    | Error es -> Assert.Fail(sprintf "%A" es)

[<Fact>]
let ``parse: a bare flow routes through plan to its engine face`` () =
    let plan = Command.plan flowCfg (Command.parse flowCfg [ "uat" ] |> mustOk)
    Assert.Equal(PlanAction.EmitBundle ("model.json", "dist/onprem-uat"), plan.Action)

[<Fact>]
let ``parse: report dispatches to Intent.Report`` () =
    match Command.parse flowCfg [ "report"; "uat" ] |> mustOk with
    | Intent.Report _ -> ()
    | other -> Assert.Fail(sprintf "expected Report, got %A" other)

[<Fact>]
let ``report <flow>: resolves the target environment's store (F4)`` () =
    // onprem-uat (flow uat's target) carries a store → ReportBundle on it.
    match (Command.plan flowCfg (Intent.Report [ "uat" ])).Action with
    | PlanAction.ReportBundle store -> Assert.Equal("lifecycle/uat.json", store)
    | other -> Assert.Fail(sprintf "expected ReportBundle, got %A" other)

[<Fact>]
let ``report <flow>: a target with no store is refused (named, not silent)`` () =
    // golden's target cloud-uat has no store.
    match (Command.plan flowCfg (Intent.Report [ "golden" ])).Action with
    | PlanAction.Refused (6, e) -> Assert.Equal("cli.report.noStore", e.Code)
    | other -> Assert.Fail(sprintf "expected noStore refusal, got %A" other)

[<Fact>]
let ``report --store <path>: an explicit store overrides`` () =
    match (Command.plan flowCfg (Intent.Report [ "--store"; "x.lifecycle.json" ])).Action with
    | PlanAction.ReportBundle store -> Assert.Equal("x.lifecycle.json", store)
    | other -> Assert.Fail(sprintf "expected ReportBundle, got %A" other)

[<Fact>]
let ``explain <flow>: previews B (the flow model) against A_prior (the target store)`` () =
    // uat → onprem-uat (store "lifecycle/uat.json"); cfg model is "model.json".
    match (Command.plan flowCfg (Intent.Explain [ "uat" ])).Action with
    | PlanAction.ExplainMigrateFromStore (store, modelB, _) ->
        Assert.Equal("lifecycle/uat.json", store)
        Assert.Equal("model.json", modelB)
    | other -> Assert.Fail(sprintf "expected ExplainMigrateFromStore, got %A" other)

[<Fact>]
let ``explain <flow>: a target with no store is refused (publish + seal once first)`` () =
    match (Command.plan flowCfg (Intent.Explain [ "golden" ])).Action with
    | PlanAction.Refused (6, e) -> Assert.Equal("cli.explain.flowNoStore", e.Code)
    | other -> Assert.Fail(sprintf "expected flowNoStore refusal, got %A" other)

[<Fact>]
let ``parse: an unknown first token names the known flows`` () =
    Assert.Contains("cli.verb.unknown", errCodes (Command.parse flowCfg [ "nope" ]))

[<Fact>]
let ``parse: a closed verb still wins over a flow lookup`` () =
    match Command.parse flowCfg [ "check"; "ready" ] |> mustOk with
    | Intent.Check _ -> ()
    | other -> Assert.Fail(sprintf "expected Check, got %A" other)

[<Fact>]
let ``plan is TOTAL across check / explain / seal / report verb tails`` () =
    let tails =
        [ Intent.Check []; Intent.Check [ "drift" ]; Intent.Check [ "drift"; "--model"; "m"; "--to"; "cloud-dev" ]
          Intent.Check [ "data"; "--before"; "cloud-dev"; "--after"; "cloud-qa" ]; Intent.Check [ "ready" ]; Intent.Check [ "x.sql"; "--cdc-silence" ]
          Intent.Explain []; Intent.Explain [ "diff"; "a"; "b" ]; Intent.Explain [ "policy"; "a"; "b" ]
          Intent.Explain [ "node"; "c"; "k" ]; Intent.Explain [ "suggest"; "c" ]; Intent.Explain [ "registry" ]
          Intent.Explain [ "migrate"; "--to"; "b"; "--from"; "a" ]; Intent.Explain [ "bogus" ]
          Intent.Seal []; Intent.Seal [ "--store"; "s" ]; Intent.Seal [ "approve"; "v"; "--approver"; "me" ]; Intent.Seal [ "approve"; "v" ]
          Intent.Report []; Intent.Report [ "uat" ] ]
    for intent in tails do
        match (Command.plan flowCfg intent).Action with
        | PlanAction.Refused (code, error) ->
            Assert.Contains(code, [ 1; 2; 6 ])
            Assert.False(System.String.IsNullOrWhiteSpace error.Message)
        | _ -> ()
