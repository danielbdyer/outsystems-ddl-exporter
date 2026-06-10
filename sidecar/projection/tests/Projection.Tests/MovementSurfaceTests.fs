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

// -- M1: the `rendition: physical | logical` env-metadata flag --------------

[<Fact>]
let ``config parses an environment with rendition physical (the OSUSR cloud A)`` () =
    let json = """{ "environments": { "cloud-uat": { "access": "direct", "conn": "env:CLOUD_UAT_CONN", "grant": "data", "rendition": "physical" } } }"""
    let e = Map.find "cloud-uat" (ProjectionConfig.parse json |> mustOk).Environments
    Assert.Equal(Some Rendition.Physical, e.Rendition)

[<Fact>]
let ``config parses an environment with rendition logical (the hosted on-prem B)`` () =
    let json = """{ "environments": { "onprem-legacy": { "access": "direct", "conn": "env:ONPREM_LEGACY_CONN", "rendition": "logical" } } }"""
    let e = Map.find "onprem-legacy" (ProjectionConfig.parse json |> mustOk).Environments
    Assert.Equal(Some Rendition.Logical, e.Rendition)

[<Fact>]
let ``config defaults an environment with no rendition to None (the minimal non-breaking default)`` () =
    // The established same-rendition surface never sets it; absent must round-trip
    // as None, not a fabricated Physical/Logical (the env-metadata default).
    let e = Map.find "cloud-dev" (ProjectionConfig.parse envFlowJson |> mustOk).Environments
    Assert.Equal(None, e.Rendition)

[<Fact>]
let ``config refuses an unknown rendition`` () =
    let json = """{ "environments": { "x": { "access": "docker", "rendition": "hybrid" } } }"""
    Assert.Contains("cli.config.envRenditionUnknown", errCodes (ProjectionConfig.parse json))

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

// -- S1: the unified `Shaping` view (THE_CONFIG_CONTROL_PLANE §5) ------------
// `ProjectionConfig.Shaping` is the model-shaping view of the SAME
// `projection.json`, parsed leniently so a movement-only file defaults every
// shaping section (no `modelNoSource`). Nothing CONSUMES it yet at S1.

[<Fact>]
let ``S1: a movement-only config parses with a default-empty Shaping (no modelNoSource)`` () =
    // environments/flows only — no `model`/`overrides`/`policy`/`emission`.
    // The lenient Shaping parse must default every section, not error.
    let cfg = ProjectionConfig.parse envFlowJson |> mustOk
    Assert.Equal(None, cfg.Shaping.Model.Path)
    Assert.Equal(None, cfg.Shaping.Model.Ossys)
    Assert.Empty(cfg.Shaping.Model.Modules)
    Assert.Empty(cfg.Shaping.Overrides.TableRenames)
    Assert.Equal(Config.defaultConfig.Policy.Selection, cfg.Shaping.Policy.Selection)

[<Fact>]
let ``S1: empty-text config carries the default Shaping`` () =
    let cfg = ProjectionConfig.parse "" |> mustOk
    Assert.Equal(None, cfg.Shaping.Model.Path)
    Assert.Empty(cfg.Shaping.Overrides.TableRenames)

[<Fact>]
let ``S1: a unified config populates Shaping.Policy / Overrides / Model.Modules`` () =
    let cfg =
        ProjectionConfig.parse """
        {
          "environments": { "uat": { "access": "bundle", "out": "dist/uat", "grant": "schema+data" } },
          "flows": { "emit": { "from": "model", "to": "uat" } },
          "model":     { "path": "model.json", "modules": ["Sales", { "name": "Ops", "entities": ["Order"] }] },
          "overrides": { "tableRenames": [ { "from": { "module": "Sales", "entity": "Cust" }, "to": { "schema": "dbo", "table": "Customer" } } ] },
          "policy":    { "selection": "IncludeManual" }
        }
        """ |> mustOk
    // model.modules folds into the shaping view (the canonical object form).
    Assert.Equal<Config.ModuleSelector list>(
        [ Config.Whole "Sales"; Config.WithEntities ("Ops", [ "Order" ]) ],
        cfg.Shaping.Model.Modules)
    // overrides.tableRenames folds in.
    match cfg.Shaping.Overrides.TableRenames with
    | [ { From = Config.LogicalSource { Module = "Sales"; Entity = "Cust" }; To = { Schema = "dbo"; Table = "Customer" } } ] -> ()
    | other -> Assert.Fail(sprintf "expected one Sales::Cust -> dbo.Customer rename, got %A" other)
    // policy folds in.
    Assert.Equal("IncludeManual", cfg.Shaping.Policy.Selection)

// -- M3.b: the `legacy` B→A reverse-leg classifier (Command.reverseLegOf) ----
// The clean partial: the rendition flag (M1) drives the recognition of a flow as
// the B→A reverse leg (logical source -> physical sink) — the operator-facing
// face of the LE-2-proven runWithRenames capability. The runner wiring waits on
// the one-model-two-renditions rendering mechanism (the residual; J3 / LE-1).

let private reverseCfg =
    ProjectionConfig.parse """
    {
      "environments": {
        "onprem-legacy": { "access": "direct", "conn": "env:ONPREM_LEGACY_CONN", "rendition": "logical" },
        "cloud-uat":     { "access": "direct", "conn": "env:CLOUD_UAT_CONN", "grant": "data", "rendition": "physical" },
        "cloud-peer":    { "access": "direct", "conn": "env:CLOUD_PEER_CONN", "rendition": "physical" },
        "cloud-bundle":  { "access": "bundle", "out": "dist/cloud", "grant": "data", "rendition": "physical" },
        "plain-src":     { "access": "direct", "conn": "env:PLAIN_SRC_CONN" }
      },
      "flows": {
        "legacy":  { "from": "onprem-legacy", "to": "cloud-uat" },
        "golden":  { "from": "cloud-peer",    "to": "cloud-uat" },
        "plain":   { "from": "plain-src",     "to": "cloud-uat" },
        "bundled": { "from": "onprem-legacy", "to": "cloud-bundle" }
      }
    }
    """ |> mustOk

[<Fact>]
let ``reverseLegOf: a logical source to a physical sink is recognized as the B->A reverse leg`` () =
    let flow = Map.find "legacy" reverseCfg.Flows
    match Command.reverseLegOf reverseCfg flow with
    | Some leg ->
        Assert.Equal("env:ONPREM_LEGACY_CONN", leg.SourceConn)
        Assert.Equal("env:CLOUD_UAT_CONN", leg.SinkConn)
        Assert.Equal("legacy", leg.Flow.Name)
    | None -> Assert.True(false, "logical→physical should be recognized as the reverse leg")

[<Fact>]
let ``reverseLegOf: a physical source to a physical sink (the peer/golden move) is NOT a reverse leg`` () =
    // The peer/golden cloud→cloud move is same-rendition (physical→physical); it
    // rides the established routing, not the reverse leg.
    let flow = Map.find "golden" reverseCfg.Flows
    Assert.Equal(None, Command.reverseLegOf reverseCfg flow)

[<Fact>]
let ``reverseLegOf: endpoints with no rendition set are NOT a reverse leg (same-rendition default)`` () =
    let flow = Map.find "plain" reverseCfg.Flows
    Assert.Equal(None, Command.reverseLegOf reverseCfg flow)

[<Fact>]
let ``reverseLegOf: a logical source to a non-live (bundle) physical sink is NOT a reverse leg`` () =
    // The reverse leg needs two LIVE endpoints (a Move reads rows and writes
    // them); a bundle sink produces files, so it is not the reverse-leg shape.
    let flow = Map.find "bundled" reverseCfg.Flows
    Assert.Equal(None, Command.reverseLegOf reverseCfg flow)

// -- S4b: the DERIVED MovementDirection (G2) ---------------------------------
// Direction is a BINDING: derived in resolveFlowSpec from (source rendition,
// sink rendition, content origin), never a parsed knob. It reuses the same
// `reverseLegOf` predicate for the B→A leg, so the classifier and the router
// can never drift.

let private previewOpts : FlowRunOpts =
    { Go = false; Fresh = false; AllowDrops = false; AllowCdc = false; Resumable = false; Seed = None; Scale = None }

let private dirOf (cfg: ProjectionConfig) name =
    match Command.resolveFlowSpec cfg (Map.find name cfg.Flows) previewOpts with
    | Ok s -> s.Direction
    | Error es -> failwithf "resolveFlowSpec failed: %A" es

[<Fact>]
let ``direction: a logical source to a physical live sink derives UpLegacy (B->A)`` () =
    Assert.Equal(MovementDirection.UpLegacy, dirOf reverseCfg "legacy")

[<Fact>]
let ``direction: a physical->physical peer (golden) move derives UpPeer (A->A)`` () =
    Assert.Equal(MovementDirection.UpPeer, dirOf reverseCfg "golden")

[<Fact>]
let ``direction: a logical source into a bundle (non-live) sink derives Down (not a reverse leg)`` () =
    // The reverse leg needs two live endpoints; a bundle sink is the down-leg.
    Assert.Equal(MovementDirection.Down, dirOf reverseCfg "bundled")

[<Fact>]
let ``direction: a synthetic mint source derives UpSynthetic`` () =
    let cfg =
        ProjectionConfig.parse """
        {
          "environments": { "cloud-uat": { "access": "direct", "conn": "env:CLOUD_UAT_CONN", "grant": "data" } },
          "flows": { "synth": { "from": "synthetic", "profile": "file:p.json", "to": "cloud-uat" } },
          "model": "model.json"
        }
        """ |> mustOk
    Assert.Equal(MovementDirection.UpSynthetic, dirOf cfg "synth")

[<Fact>]
let ``direction: a model source to a bundle target derives Down (the A->B down-leg)`` () =
    let cfg =
        ProjectionConfig.parse """
        {
          "environments": { "pub": { "access": "bundle", "out": "dist/pub", "grant": "schema+data" } },
          "flows": { "publish": { "from": "model", "to": "pub" } },
          "model": "model.json"
        }
        """ |> mustOk
    Assert.Equal(MovementDirection.Down, dirOf cfg "publish")

[<Fact>]
let ``direction: the legacy flow routes through planFlow to RunReverseLeg under --go --scope data`` () =
    // The flow's grant is `data`, so the grant gate passes; the derived UpLegacy
    // direction routes the committed data move to the reverse-leg runner.
    let commitData = { Go = true; Fresh = false; AllowDrops = false; AllowCdc = false; Resumable = false; Seed = None; Scale = None }
    let flow = { Map.find "legacy" reverseCfg.Flows with Scope = Some Scope.Data }
    match (Command.planFlow reverseCfg flow commitData).Action with
    | PlanAction.RunReverseLeg ("env:ONPREM_LEGACY_CONN", "env:CLOUD_UAT_CONN", _, true) -> ()
    | other -> Assert.Fail(sprintf "expected RunReverseLeg, got %A" other)

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
      Reconcile = []; Rekey = None; AllowCdc = false; Resumable = false; Store = None; Env = None; Tables = []; Seed = None; Scale = None }

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
let ``planMovement: --resumable threads onto the transfer LoadOpts (A2)`` () =
    match planOf { baseLive with Commit = true; Scope = Scope.Data; Data = DataOrigin.FromTarget "qa"; Resumable = true } with
    | PlanAction.Transfer (_, _, opts, true) -> Assert.True opts.Resumable
    | other -> Assert.Fail(sprintf "expected Transfer, got %A" other)

[<Fact>]
let ``planMovement: --resumable default off on the transfer LoadOpts (A2 byte-identical)`` () =
    match planOf { baseLive with Commit = true; Scope = Scope.Data; Data = DataOrigin.FromTarget "qa" } with
    | PlanAction.Transfer (_, _, opts, true) -> Assert.False opts.Resumable
    | other -> Assert.Fail(sprintf "expected Transfer, got %A" other)

[<Fact>]
let ``planMovement: --resumable on a non-transfer action is noted, never silently ignored (A2)`` () =
    let p = Command.planMovement routeCfg { baseLive with Commit = true; Model = ModelSource.ModelFile "m.json"; Resumable = true }
    Assert.Contains(p.Notes, fun (n: string) -> n.Contains "--resumable")

[<Fact>]
let ``planMovement: folder + config → PublishBundle`` () =
    let s = { MovementSpec.forDestination (Destination.Folder "./o") with Model = ModelSource.ConfigFile "c.json" }
    Assert.Equal(PlanAction.PublishBundle ("c.json", "./o", None, None), planOf s)

[<Fact>]
let ``planMovement: folder + model + shape routes Skeleton vs Bundle`` () =
    let folderModel shape = { MovementSpec.forDestination (Destination.Folder "./o") with Model = ModelSource.ModelFile "m.json"; Shape = shape }
    Assert.Equal(PlanAction.EmitSkeleton (ModelSource.ModelFile "m.json", None, "./o"), planOf (folderModel Shape.Skeleton))
    Assert.Equal(PlanAction.EmitBundle (ModelSource.ModelFile "m.json", None, "./o"), planOf (folderModel Shape.Bundle))

[<Fact>]
let ``planMovement: folder + model + shape manifest routes to EmitManifest`` () =
    let s = { MovementSpec.forDestination (Destination.Folder "./o") with Model = ModelSource.ModelFile "m.json"; Shape = Shape.Manifest }
    Assert.Equal(PlanAction.EmitManifest (ModelSource.ModelFile "m.json", None, "./o"), planOf s)

[<Fact>]
let ``config parses a flow's manifest shape; an unknown shape names all four tokens`` () =
    let json = """{ "environments": { "out": { "access": "bundle", "out": "d" } }, "flows": { "m": { "to": "out", "shape": "manifest" } } }"""
    let f = Map.find "m" (ProjectionConfig.parse json |> mustOk).Flows
    Assert.Equal(Some Shape.Manifest, f.Shape)
    let bad = """{ "environments": { "out": { "access": "bundle", "out": "d" } }, "flows": { "m": { "to": "out", "shape": "tarball" } } }"""
    Assert.Contains("cli.config.flowShapeUnknown", errCodes (ProjectionConfig.parse bad))

[<Fact>]
let ``planMovement: docker + model → DeployDocker; no model → Refused`` () =
    Assert.Equal(PlanAction.DeployDocker (ModelSource.ModelFile "m.json", None),
                 planOf { MovementSpec.forDestination Destination.Docker with Model = ModelSource.ModelFile "m.json" })
    match planOf (MovementSpec.forDestination Destination.Docker) with
    | PlanAction.Refused (1, _) -> ()
    | other -> Assert.Fail(sprintf "expected Refused 1, got %A" other)

[<Fact>]
let ``planMovement: live preview (no --go) → schema plan, or data plan with a source`` () =
    Assert.Equal(PlanAction.PreviewSchema (ModelSource.ModelFile "model.json", None, "env:DEV_CONN", DeclareNone),
                 planOf { baseLive with Model = ModelSource.ModelFile "model.json" })
    Assert.Equal(PlanAction.Transfer ("env:QA_CONN", "env:DEV_CONN", defaultOpts, false),
                 planOf { baseLive with Data = DataOrigin.FromTarget "qa" })

[<Fact>]
let ``planMovement: live --go routes migrate / migrate-with-data / transfer / publish-load`` () =
    let go = { baseLive with Commit = true; Model = ModelSource.ModelFile "model.json" }
    Assert.Equal(PlanAction.Migrate (ModelSource.ModelFile "model.json", None, "env:DEV_CONN", defaultOpts), planOf go)
    Assert.Equal(PlanAction.MigrateWithData (ModelSource.ModelFile "model.json", None, "env:DEV_CONN", "env:QA_CONN", defaultOpts),
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
let ``planMovement is TOTAL across the destination × scope × data × model × commit × direction product`` () =
    // No combination throws; every Refused is a named exit code + coded error.
    // S4b — the new `Direction` field + `RunReverseLeg` arm are in the sweep.
    let destinations = [ Destination.Folder "./o"; Destination.Docker; liveDev ]
    let scopes       = [ Scope.All; Scope.Schema; Scope.Data ]
    let datas        = [ DataOrigin.Model; DataOrigin.Synthetic "file:p.json"; DataOrigin.NoData; DataOrigin.FromTarget "qa"; DataOrigin.FromTarget "pub" ]
    let models       = [ ModelSource.ModelFile "m.json"; ModelSource.ConfigFile "c.json"; ModelSource.Unspecified ]
    let commits      = [ true; false ]
    let directions   = [ MovementDirection.Down; MovementDirection.UpSynthetic; MovementDirection.UpPeer; MovementDirection.UpLegacy ]
    let mutable n = 0
    for dest in destinations do
      for scope in scopes do
        for data in datas do
          for model in models do
            for commit in commits do
              for direction in directions do
                let spec = { MovementSpec.forDestination dest with Scope = scope; Data = data; Model = model; Commit = commit; Direction = direction }
                n <- n + 1
                match (Command.planMovement routeCfg spec).Action with
                | PlanAction.Refused (code, error) ->
                    Assert.Contains(code, [ 1; 2; 6 ])
                    Assert.False(System.String.IsNullOrWhiteSpace error.Message)
                    Assert.False(System.String.IsNullOrWhiteSpace error.Code)
                | _ -> ()
    Assert.Equal(3 * 3 * 5 * 3 * 2 * 4, n)

[<Fact>]
let ``planMovement: a data move with Direction=UpLegacy routes to RunReverseLeg, not Transfer`` () =
    // The B→A legacy reverse leg is routed distinctly; a peer (Down) data move of
    // the SAME DataOrigin keeps the generic Transfer.
    let legacy = { baseLive with Commit = true; Scope = Scope.Data; Data = DataOrigin.FromTarget "qa"; Direction = MovementDirection.UpLegacy }
    match planOf legacy with
    | PlanAction.RunReverseLeg ("env:QA_CONN", "env:DEV_CONN", _, true) -> ()
    | other -> Assert.Fail(sprintf "expected RunReverseLeg, got %A" other)
    let peer = { legacy with Direction = MovementDirection.UpPeer }
    match planOf peer with
    | PlanAction.Transfer ("env:QA_CONN", "env:DEV_CONN", _, true) -> ()
    | other -> Assert.Fail(sprintf "expected Transfer, got %A" other)

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
        "golden":  { "from": "cloud-qa",  "to": "cloud-uat",  "tables": ["Customer"], "reconcile": ["OSUSR_RC_USER:EMAIL"] },
        "badsrc":  { "from": "onprem-uat","to": "cloud-uat" },
        "lift-uat":{ "from": "model",     "to": "cloud-uat" },
        "spin":    { "from": "model",     "to": "lab" }
      },
      "model": "model.json"
    }
    """ |> mustOk

let private preview = { Go = false; Fresh = false; AllowDrops = false; AllowCdc = false; Resumable = false; Seed = None; Scale = None }
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
    Assert.Equal(PlanAction.EmitBundle (ModelSource.ModelFile "model.json", None, "dist/onprem-uat"), actionOf "uat" preview)

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
    let bt = { Name = "bt"; From = FlowSource.Model; To = "onprem-uat"; Rekey = None; Tables = [ "Customer" ]; Reconcile = []; Scope = None; Shape = None; Shaping = None }
    Assert.Contains((Command.planFlow flowCfg bt preview).Notes, fun (n: string) -> n.Contains "data-transfer leg only")

[<Fact>]
let ``flow grant gate: schema-from-model against a data-only target is Refused (9)`` () =
    match actionOf "lift-uat" commit with
    | PlanAction.Refused (9, e) -> Assert.Equal("cli.flow.grantSchemaRefused", e.Code)
    | other -> Assert.Fail(sprintf "expected grant refusal, got %A" other)

// -- S4a: per-flow `scope`, decoupled from `grant` (G1) ----------------------
// The move's PROJECTION (which legs of the T16 square THIS move carries) is now
// an explicit per-flow `scope`, decoupled from the target's `grant` (the refusal
// gate). The schema-only / data-only legs (ontology V14/V15) become reachable
// from config; absent `scope`, the grant-derived default holds (back-compat).

let private scopeCfg =
    ProjectionConfig.parse """
    {
      "environments": {
        "cloud-src":  { "access": "direct", "conn": "env:CLOUD_SRC_CONN" },
        "cloud-full": { "access": "direct", "conn": "env:CLOUD_FULL_CONN", "grant": "schema+data" }
      },
      "flows": {
        "schema-leg": { "from": "cloud-src", "to": "cloud-full", "scope": "schema" },
        "data-leg":   { "from": "cloud-src", "to": "cloud-full", "scope": "data" },
        "both-leg":   { "from": "cloud-src", "to": "cloud-full", "scope": "both" }
      },
      "model": "model.json"
    }
    """ |> mustOk

let private scopeSpecOf name opts =
    Command.resolveFlowSpec scopeCfg (Map.find name scopeCfg.Flows) opts

[<Fact>]
let ``flow scope:schema resolves Scope.Schema regardless of grant (G1)`` () =
    // The target grants schema+data; an explicit scope:"schema" carries the
    // schema leg only — the projection is the flow's, not the grant's.
    match scopeSpecOf "schema-leg" preview with
    | Ok s -> Assert.Equal(Scope.Schema, s.Scope)
    | Error es -> Assert.Fail(sprintf "%A" es)

[<Fact>]
let ``flow scope:data into a schema+data target resolves Scope.Data (G1)`` () =
    // The data-only leg into a full-grant target — previously unreachable: the
    // grant-derived default would have given Scope.All.
    match scopeSpecOf "data-leg" preview with
    | Ok s -> Assert.Equal(Scope.Data, s.Scope)
    | Error es -> Assert.Fail(sprintf "%A" es)

[<Fact>]
let ``flow scope:both resolves Scope.All`` () =
    match scopeSpecOf "both-leg" preview with
    | Ok s -> Assert.Equal(Scope.All, s.Scope)
    | Error es -> Assert.Fail(sprintf "%A" es)

[<Fact>]
let ``flow with no scope keeps the grant-derived default (back-compat)`` () =
    // `golden` (no scope) into a data-granting target → Scope.Data (the grant
    // default); `uat` (no scope) into a schema+data target → Scope.All.
    match specOf "golden" preview with
    | Ok s -> Assert.Equal(Scope.Data, s.Scope)
    | Error es -> Assert.Fail(sprintf "%A" es)
    match specOf "uat" preview with
    | Ok s -> Assert.Equal(Scope.All, s.Scope)
    | Error es -> Assert.Fail(sprintf "%A" es)

[<Fact>]
let ``flow scope unknown value is a named refusal (cli.config.flowScopeUnknown)`` () =
    let json = """{ "flows": { "x": { "to": "e", "scope": "partial" } } }"""
    Assert.Contains("cli.config.flowScopeUnknown", errCodes (ProjectionConfig.parse json))

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
    Assert.Equal(PlanAction.DeployDocker (ModelSource.ModelFile "model.json", None), actionOf "spin" preview)

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
    | PlanAction.SynthesizeAndLoad (ModelSource.ModelFile "model.json", None, "file:legacy.profile.json", "env:CLOUD_UAT_CONN", _, false) -> ()
    | other -> Assert.Fail(sprintf "expected preview SynthesizeAndLoad, got %A" other)
    match synthAction "preview-synth" commit with
    | PlanAction.SynthesizeAndLoad (_, None, "file:legacy.profile.json", "env:CLOUD_UAT_CONN", _, true) -> ()
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
    | PlanAction.SynthesizeAndLoad (_, _, _, "env:CLOUD_ALL_CONN", _, false) -> ()
    | other -> Assert.Fail(sprintf "expected all-scope preview SynthesizeAndLoad, got %A" other)
    match synthAction "synth-all" commit with
    | PlanAction.Refused (2, e) -> Assert.Equal("cli.move.syntheticScope", e.Code)
    | other -> Assert.Fail(sprintf "expected synthetic-scope refusal, got %A" other)

[<Fact>]
let ``synthetic flow threads the live-OSSYS model source (primary) when configured`` () =
    let cfg =
        ProjectionConfig.parse """
        {
          "environments": { "cloud-uat": { "access": "direct", "conn": "env:CLOUD_UAT_CONN", "grant": "data" } },
          "flows": { "preview-synth": { "from": "synthetic", "profile": "file:legacy.profile.json", "to": "cloud-uat" } },
          "model": "model.json",
          "modelOssys": "env:ONPREM_OSSYS_CONN"
        }
        """ |> mustOk
    Assert.Equal(Some "env:ONPREM_OSSYS_CONN", cfg.ModelOssys)
    match (Command.planFlow cfg (Map.find "preview-synth" cfg.Flows) preview).Action with
    // modelOssys (primary) rides in the action; the model file remains the fallback.
    | PlanAction.SynthesizeAndLoad (ModelSource.ModelFile "model.json", Some "env:ONPREM_OSSYS_CONN", _, _, _, false) -> ()
    | other -> Assert.Fail(sprintf "expected SynthesizeAndLoad carrying the live-OSSYS primary, got %A" other)

// -- live-OSSYS model primary across the whole flow surface -----------------

let private ossysFlowCfg =
    ProjectionConfig.parse """
    {
      "environments": {
        "uat-bundle": { "access": "bundle", "out": "dist/uat", "grant": "schema+data" },
        "lab":        { "access": "docker", "grant": "schema+data" },
        "cloud-live": { "access": "direct", "conn": "env:CLOUD_LIVE_CONN" }
      },
      "flows": {
        "emit":    { "from": "model", "to": "uat-bundle" },
        "spin":    { "from": "model", "to": "lab" },
        "preview": { "from": "model", "to": "cloud-live" }
      },
      "model": "model.json",
      "modelOssys": "env:ONPREM_OSSYS_CONN"
    }
    """ |> mustOk
let private ossysAction name opts = (Command.planFlow ossysFlowCfg (Map.find name ossysFlowCfg.Flows) opts).Action

[<Fact>]
let ``modelOssys (primary) threads into emit / docker / preview actions`` () =
    match ossysAction "emit" preview with
    | PlanAction.EmitBundle (ModelSource.ModelFile "model.json", Some "env:ONPREM_OSSYS_CONN", "dist/uat") -> ()
    | other -> Assert.Fail(sprintf "expected EmitBundle carrying modelOssys, got %A" other)
    match ossysAction "spin" preview with
    | PlanAction.DeployDocker (ModelSource.ModelFile "model.json", Some "env:ONPREM_OSSYS_CONN") -> ()
    | other -> Assert.Fail(sprintf "expected DeployDocker carrying modelOssys, got %A" other)
    match ossysAction "preview" preview with
    | PlanAction.PreviewSchema (ModelSource.ModelFile "model.json", Some "env:ONPREM_OSSYS_CONN", "env:CLOUD_LIVE_CONN", _) -> ()
    | other -> Assert.Fail(sprintf "expected PreviewSchema carrying modelOssys, got %A" other)

[<Fact>]
let ``modelOssys-only (no model file) still routes — the file is optional`` () =
    let cfg =
        ProjectionConfig.parse """
        {
          "environments": { "uat-bundle": { "access": "bundle", "out": "dist/uat", "grant": "schema+data" } },
          "flows": { "emit": { "from": "model", "to": "uat-bundle" } },
          "modelOssys": "env:ONPREM_OSSYS_CONN"
        }
        """ |> mustOk
    match (Command.planFlow cfg (Map.find "emit" cfg.Flows) preview).Action with
    | PlanAction.EmitBundle (ModelSource.Unspecified, Some "env:ONPREM_OSSYS_CONN", "dist/uat") -> ()
    | other -> Assert.Fail(sprintf "expected EmitBundle from ossys-only config, got %A" other)

// -- S2: the unified `model` object form vs the legacy top-level forms -------
// The unified `model` OBJECT (path/ossys) is the canonical collision
// reconciliation (THE_CONFIG_CONTROL_PLANE §4): a config carrying
// `"model": { "path": ..., "ossys": ... }` must resolve to the SAME plan as
// the legacy `"model": "<path>"` + top-level `"modelOssys": "<ref>"`.

let private s2Estate flows model =
    sprintf """
    {
      "environments": {
        "uat-bundle": { "access": "bundle", "out": "dist/uat", "grant": "schema+data" },
        "lab":        { "access": "docker", "grant": "schema+data" },
        "cloud-live": { "access": "direct", "conn": "env:CLOUD_LIVE_CONN" }
      },
      "flows": %s,
      %s
    }
    """ flows model

let private s2Flows = """{ "emit": { "from": "model", "to": "uat-bundle" }, "spin": { "from": "model", "to": "lab" }, "preview": { "from": "model", "to": "cloud-live" } }"""

let private s2LegacyCfg =
    ProjectionConfig.parse (s2Estate s2Flows """ "model": "model.json", "modelOssys": "env:ONPREM_OSSYS_CONN" """) |> mustOk
let private s2ObjectCfg =
    ProjectionConfig.parse (s2Estate s2Flows """ "model": { "path": "model.json", "ossys": "env:ONPREM_OSSYS_CONN" } """) |> mustOk

[<Fact>]
let ``S2: the object model form reconciles into Shaping.Model (path + ossys)`` () =
    Assert.Equal(Some "model.json", s2ObjectCfg.Shaping.Model.Path)
    Assert.Equal(Some "env:ONPREM_OSSYS_CONN", s2ObjectCfg.Shaping.Model.Ossys)
    // The legacy top-level forms reconcile to the SAME canonical Shaping.Model.
    Assert.Equal(s2ObjectCfg.Shaping.Model.Path,  s2LegacyCfg.Shaping.Model.Path)
    Assert.Equal(s2ObjectCfg.Shaping.Model.Ossys, s2LegacyCfg.Shaping.Model.Ossys)

[<Fact>]
let ``S2: object-model and legacy-top-level configs resolve to identical plans`` () =
    for name in [ "emit"; "spin"; "preview" ] do
        let legacy = (Command.planFlow s2LegacyCfg (Map.find name s2LegacyCfg.Flows) preview).Action
        let object' = (Command.planFlow s2ObjectCfg (Map.find name s2ObjectCfg.Flows) preview).Action
        Assert.Equal(legacy, object')
    // And the object form carries the model file + ossys into the action,
    // exactly like the legacy form (byte-for-byte parity with :518-527).
    match (Command.planFlow s2ObjectCfg (Map.find "emit" s2ObjectCfg.Flows) preview).Action with
    | PlanAction.EmitBundle (ModelSource.ModelFile "model.json", Some "env:ONPREM_OSSYS_CONN", "dist/uat") -> ()
    | other -> Assert.Fail(sprintf "expected EmitBundle carrying the reconciled model, got %A" other)

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
let ``parse: --resumable sets the per-run intent (A2)`` () =
    let (_, o) = parseFlowIntent [ "golden"; "--resumable" ]
    Assert.True o.Resumable

[<Fact>]
let ``parse: --resumable default off (A2 byte-identical)`` () =
    let (_, o) = parseFlowIntent [ "golden" ]
    Assert.False o.Resumable

[<Fact>]
let ``flow --resumable threads onto the spec (A2)`` () =
    match specOf "spin" { preview with Resumable = true } with
    | Ok s -> Assert.True s.Resumable
    | Error es -> Assert.Fail(sprintf "%A" es)

[<Fact>]
let ``config parses a flow's reconcile rules (J2 — the golden re-key lives in the flow)`` () =
    Assert.Equal<string list>([ "OSUSR_RC_USER:EMAIL" ], (flowOf "golden").Reconcile)

[<Fact>]
let ``config refuses a malformed flow reconcile entry, named (J2)`` () =
    let json = """{ "environments": { "uat": { "access": "docker" } }, "flows": { "g": { "to": "uat", "reconcile": ["OSUSR_RC_USER"] } } }"""
    Assert.Contains("cli.config.flowReconcileShape", errCodes (ProjectionConfig.parse json))

[<Fact>]
let ``flow reconcile threads onto the spec and into the transfer's LoadOpts (J2)`` () =
    match specOf "golden" commit with
    | Ok s -> Assert.Equal<string list>([ "OSUSR_RC_USER:EMAIL" ], s.Reconcile)
    | Error es -> Assert.Fail(sprintf "%A" es)
    match actionOf "golden" commit with
    | PlanAction.Transfer (_, _, opts, true) ->
        Assert.Equal<string list>([ "OSUSR_RC_USER:EMAIL" ], opts.Reconcile)
    | other -> Assert.Fail(sprintf "expected Transfer, got %A" other)

[<Fact>]
let ``render: a flow's reconcile rules round-trip (J2; parse-render = id)`` () =
    let flow = flowOf "golden"
    let cfg = { ProjectionConfig.empty with Flows = Map.ofList [ flow.Name, flow ] }
    match ProjectionConfig.parse (ProjectionConfig.render cfg) with
    | Ok back -> Assert.Equal<string list>([ "OSUSR_RC_USER:EMAIL" ], (Map.find "golden" back.Flows).Reconcile)
    | Error es -> Assert.Fail(sprintf "round-trip failed: %A" es)

[<Fact>]
let ``parse: --seed and --scale set the per-run intent (D8)`` () =
    let (_, o) = parseFlowIntent [ "golden"; "--seed"; "7"; "--scale"; "0.5" ]
    Assert.Equal(Some 7UL, o.Seed)
    Assert.Equal(Some 0.5M, o.Scale)

[<Fact>]
let ``parse: --seed and --scale default absent (D8 byte-identical)`` () =
    let (_, o) = parseFlowIntent [ "golden" ]
    Assert.Equal(None, o.Seed)
    Assert.Equal(None, o.Scale)

[<Fact>]
let ``parse: a malformed --seed is refused, named (D8)`` () =
    match Command.parse flowCfg [ "golden"; "--seed"; "many" ] with
    | Error [ e ] -> Assert.Equal("cli.flow.seedInvalid", e.Code)
    | other -> Assert.Fail(sprintf "expected the seed refusal, got %A" other)

[<Fact>]
let ``parse: a non-positive --scale is refused, named (D8)`` () =
    match Command.parse flowCfg [ "golden"; "--scale"; "0" ] with
    | Error [ e ] -> Assert.Equal("cli.flow.scaleInvalid", e.Code)
    | other -> Assert.Fail(sprintf "expected the scale refusal, got %A" other)

[<Fact>]
let ``planMovement: seed/scale thread into the synthetic load's LoadOpts (D8)`` () =
    let spec =
        { baseLive with
            Data = DataOrigin.Synthetic "file:p.json"
            Seed = Some 7UL
            Scale = Some 0.5M }
    match planOf spec with
    | PlanAction.SynthesizeAndLoad (_, _, _, _, opts, false) ->
        Assert.Equal(Some 7UL, opts.Seed)
        Assert.Equal(Some 0.5M, opts.Scale)
    | other -> Assert.Fail(sprintf "expected SynthesizeAndLoad, got %A" other)

[<Fact>]
let ``planMovement: seed/scale on a non-synthetic action are noted, never silently dropped (D8)`` () =
    let plan = Command.planMovement routeCfg { baseLive with Seed = Some 7UL }
    Assert.Contains(plan.Notes, fun (n: string) -> n.Contains "--seed/--scale accepted")

[<Fact>]
let ``parse: a bare flow routes through plan to its engine face`` () =
    let plan = Command.plan flowCfg (Command.parse flowCfg [ "uat" ] |> mustOk)
    Assert.Equal(PlanAction.EmitBundle (ModelSource.ModelFile "model.json", None, "dist/onprem-uat"), plan.Action)

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
    | PlanAction.ExplainMigrateFromStore (store, modelB, _, forceGenesis) ->
        Assert.Equal("lifecycle/uat.json", store)
        Assert.Equal("model.json", modelB)
        Assert.False forceGenesis
    | other -> Assert.Fail(sprintf "expected ExplainMigrateFromStore, got %A" other)

[<Fact>]
let ``explain migrate --from empty --store forces genesis (D4)`` () =
    match (Command.plan flowCfg (Intent.Explain [ "migrate"; "--to"; "b"; "--from"; "empty"; "--store"; "s.json" ])).Action with
    | PlanAction.ExplainMigrateFromStore (store, toP, _, forceGenesis) ->
        Assert.Equal("s.json", store)
        Assert.Equal("b", toP)
        Assert.True forceGenesis
    | other -> Assert.Fail(sprintf "expected ExplainMigrateFromStore forcing genesis, got %A" other)

[<Fact>]
let ``explain migrate --from <model> (not empty) stays the two-model preview (D4 additive)`` () =
    match (Command.plan flowCfg (Intent.Explain [ "migrate"; "--to"; "b"; "--from"; "a" ])).Action with
    | PlanAction.ExplainMigratePreview ("a", "b", _) -> ()
    | other -> Assert.Fail(sprintf "expected ExplainMigratePreview, got %A" other)

[<Fact>]
let ``explain migrate --store without --from empty does NOT force genesis (D4 default off)`` () =
    match (Command.plan flowCfg (Intent.Explain [ "migrate"; "--to"; "b"; "--store"; "s.json" ])).Action with
    | PlanAction.ExplainMigrateFromStore ("s.json", "b", _, forceGenesis) -> Assert.False forceGenesis
    | other -> Assert.Fail(sprintf "expected ExplainMigrateFromStore (no force), got %A" other)

[<Fact>]
let ``explain <flow> --from empty forces genesis for the flow preview (D4)`` () =
    match (Command.plan flowCfg (Intent.Explain [ "uat"; "--from"; "empty" ])).Action with
    | PlanAction.ExplainMigrateFromStore (_, _, _, forceGenesis) -> Assert.True forceGenesis
    | other -> Assert.Fail(sprintf "expected ExplainMigrateFromStore forcing genesis, got %A" other)

[<Fact>]
let ``explain <flow>: a target with no store is refused (publish + seal once first)`` () =
    match (Command.plan flowCfg (Intent.Explain [ "golden" ])).Action with
    | PlanAction.Refused (6, e) -> Assert.Equal("cli.explain.flowNoStore", e.Code)
    | other -> Assert.Fail(sprintf "expected flowNoStore refusal, got %A" other)

[<Fact>]
let ``parse: an unknown first token names the known flows`` () =
    Assert.Contains("cli.verb.unknown", errCodes (Command.parse flowCfg [ "nope" ]))

[<Fact>]
let ``parse: top-level diff <a> <b> aliases explain diff (A6)`` () =
    // `diff a b` parses to Intent.Explain ["diff"; "a"; "b"] — the SAME tail the
    // `explain diff` form produces, so it routes to the identical ExplainDiff.
    match Command.parse flowCfg [ "diff"; "a"; "b" ] |> mustOk with
    | Intent.Explain [ "diff"; "a"; "b" ] -> ()
    | other -> Assert.Fail(sprintf "expected Explain [diff;a;b], got %A" other)

[<Fact>]
let ``parse: top-level diff routes through plan to ExplainDiff (A6)`` () =
    match (Command.plan flowCfg (Command.parse flowCfg [ "diff"; "a"; "b"; "--format"; "json" ] |> mustOk)).Action with
    | PlanAction.ExplainDiff ("a", "b", true, _) -> ()
    | other -> Assert.Fail(sprintf "expected ExplainDiff, got %A" other)

[<Fact>]
let ``parse: top-level diff and explain diff produce the SAME action (A6 alias)`` () =
    let viaTop  = (Command.plan flowCfg (Command.parse flowCfg [ "diff"; "a"; "b" ] |> mustOk)).Action
    let viaLong = (Command.plan flowCfg (Command.parse flowCfg [ "explain"; "diff"; "a"; "b" ] |> mustOk)).Action
    Assert.Equal(viaLong, viaTop)

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
