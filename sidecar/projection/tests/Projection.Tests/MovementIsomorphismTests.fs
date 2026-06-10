module Projection.Tests.MovementIsomorphismTests

open Xunit
open FsCheck
open Projection.Core
open Projection.Pipeline

// ============================================================================
// THE A44 ISOMORPHISM CANARY — `expressible ⇔ reachable`
// (THE_CONFIG_CONTROL_PLANE §2, the A44 law + the directional table; §3 G3/G4).
//
// The executable proof that the `projection.json` movement config is a TOTAL,
// FAITHFUL, DIRECTION-DERIVED image of the movement space `Φ = resolveFlowSpec`
// — the movement-space instance of the iso-ladder (where T16's witness is
// `Ingestion ∘ Projection = id` on states, A44's witness is `render ∘ resolve`
// on the config⟷spec pair). The sibling of the capability survey's
// `required ⇔ surveyed` (`CapabilitySurveyTotalityTests.fs`) and the transform
// registry's `registered ⇔ executed`. Three clauses, each its own property:
//
//   1. FAITHFUL — `parse ∘ render = id` on the movement config DOM (the
//      round-trip witness; `renderFlow`/`renderEnvironment` is the inverse Ψ).
//   2. TOTAL / SPANNING — `reachable ⇔ expressible`: every model-bearing
//      `PlanAction` the engine consumes has a flow pre-image, and every
//      flow the parser accepts resolves to a real action or a NAMED refusal.
//   3. DIRECTION DERIVED — `Direction` is a pure function of
//      `(srcRendition, sinkRendition, scope)`, never a stored knob.
// ============================================================================

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es

let private preview : FlowRunOpts = { Go = false; Fresh = false; AllowDrops = false; AllowCdc = false; Resumable = false; Seed = None; Scale = None }
let private commit  : FlowRunOpts = { preview with Go = true }

// ---------------------------------------------------------------------------
// The constructed-valid generator of directional variants (the declarative-
// test-inputs + constructed-valid-generator discipline). We draw
// `(srcRendition, sinkRendition, scope, origin, commit)` from the CLOSED
// alphabets and BUILD a valid `Environment`-set + `Flow` + `ProjectionConfig`,
// rather than generate-and-filter. The sink rendition is drawn consistent with
// the chosen direction so validity is constructed, not rejected.
// ---------------------------------------------------------------------------

let private allRenditions : Rendition list = [ Rendition.Physical; Rendition.Logical ]
let private allRenditionOpts : Rendition option list = None :: (allRenditions |> List.map Some)
let private allScopeOpts : Scope option list = [ None; Some Scope.Schema; Some Scope.Data; Some Scope.All ]
let private allGrantOpts : Grant option list = [ None; Some Grant.SchemaAndData; Some Grant.DataOnly ]

/// The closed `from` alphabet, as constructed-valid origins. `Env` is split out
/// (it needs a live source env) so the generator can wire it consistently.
[<RequireQualifiedAccess>]
type private OriginDraw =
    | Model
    | NoData
    | Synthetic
    | FromEnv

let private allOriginDraws : OriginDraw list =
    [ OriginDraw.Model; OriginDraw.NoData; OriginDraw.Synthetic; OriginDraw.FromEnv ]

/// A direct (live) environment with a chosen rendition — the up-leg endpoints.
let private directEnv (name: string) (grant: Grant option) (rendition: Rendition option) : Environment =
    { Name = name; Access = Access.Direct (ConnectionRef.EnvVar (name + "_CONN"))
      Grant = grant; Store = None; Rendition = rendition }

let private genOpt (xs: 'a list) : Gen<'a> = Gen.elements xs

/// Draw a single directional variant and CONSTRUCT the valid config + flow that
/// expresses it. The source env (when origin = FromEnv) bears `srcRendition`;
/// the live sink bears `sinkRendition` — so the `(src, sink, scope)` triple that
/// derives `Direction` is reified in the config, not asserted post-hoc.
let private genVariant : Gen<ProjectionConfig * Flow> =
    gen {
        let! origin       = genOpt allOriginDraws
        let! srcRendition = genOpt allRenditionOpts
        let! sinkRendition = genOpt allRenditionOpts
        let! sinkGrant    = genOpt allGrantOpts
        let! scope        = genOpt allScopeOpts
        let sink = directEnv "sink" sinkGrant sinkRendition
        let src  = directEnv "src" None srcRendition
        let from, envs =
            match origin with
            | OriginDraw.Model     -> FlowSource.Model, [ sink ]
            | OriginDraw.NoData    -> FlowSource.NoData, [ sink ]
            | OriginDraw.Synthetic -> FlowSource.Synthetic (Some "file:p.profile.json"), [ sink ]
            | OriginDraw.FromEnv   -> FlowSource.Env "src", [ src; sink ]
        let flow = { Name = "v"; From = from; To = "sink"; Rekey = None; Tables = []; Reconcile = []; Scope = scope; Shape = None; Shaping = None }
        let cfg =
            { ProjectionConfig.empty with
                Environments = envs |> List.map (fun e -> e.Name, e) |> Map.ofList
                Flows = Map.ofList [ flow.Name, flow ]
                Model = Some "model.json"
                Shaping = { Config.defaultConfig with Model = { Config.defaultConfig.Model with Path = Some "model.json" } } }
        return cfg, flow
    }

// ---------------------------------------------------------------------------
// CLAUSE 1 — FAITHFULNESS: `parse ∘ render = id` on the movement config DOM.
// The round-trip witness over the constructed-valid generator. Compared on the
// PARSED STRUCTURE (re-parse the rendered text), never on text.
// ---------------------------------------------------------------------------

/// A config built to be a parse image (its `Model`/`Shaping` already reconciled),
/// so the round-trip compares like with like.
let private genRenderableConfig : Gen<ProjectionConfig> =
    gen {
        // 0..3 directional environments + a model flow each, drawn valid.
        let! nVariants = Gen.choose (1, 3)
        let! variants = [ for _ in 1 .. nVariants -> genVariant ] |> List.fold (fun acc g -> gen { let! xs = acc in let! v = g in return v :: xs }) (gen { return [] })
        // Merge the per-variant fragments under distinct names so the document
        // carries several environments + flows at once (random nesting).
        let envs =
            variants
            |> List.mapi (fun i (cfg, _) ->
                cfg.Environments |> Map.toList |> List.map (fun (n, e) ->
                    let nn = sprintf "%s%d" n i
                    nn, { e with Name = nn }))
            |> List.concat |> Map.ofList
        let flows =
            variants
            |> List.mapi (fun i (_, f) ->
                let toName = sprintf "sink%d" i
                let fromName =
                    match f.From with
                    | FlowSource.Env _ -> FlowSource.Env (sprintf "src%d" i)
                    | other -> other
                sprintf "flow%d" i, { f with Name = sprintf "flow%d" i; To = toName; From = fromName })
            |> Map.ofList
        let! hasOssys = Gen.elements [ true; false ]
        let ossys = if hasOssys then Some "env:OSSYS_CONN" else None
        return
            { ProjectionConfig.empty with
                Environments = envs
                Flows = flows
                Model = Some "model.json"
                ModelOssys = ossys
                Shaping =
                    { Config.defaultConfig with
                        Model = { Config.defaultConfig.Model with Path = Some "model.json"; Ossys = ossys } } }
    }

[<Fact>]
let ``A44 clause 1 — parse ∘ render = id on the movement config DOM (faithfulness)`` () =
    Prop.forAll (Arb.fromGen genRenderableConfig) (fun cfg ->
        match ProjectionConfig.parse (ProjectionConfig.render cfg) with
        | Ok back -> back = cfg
        | Error _ -> false)
    |> Check.QuickThrowOnFailure

[<Fact>]
let ``A44 clause 1 — renderEnvironment ∘ parseEnvironment = id on every reach × facet`` () =
    // A worked, exhaustive pinning that complements the property: every access
    // form × grant × rendition × store round-trips through the env vocabulary.
    let accesses =
        [ Access.Bundle "dist/out"; Access.Direct (ConnectionRef.EnvVar "E_CONN")
          Access.Direct (ConnectionRef.File "file:./e.conn"); Access.Docker ]
    for access in accesses do
      for grant in allGrantOpts do
        for rendition in allRenditionOpts do
          for store in [ None; Some "lifecycle/e.json" ] do
            let env = { Name = "e"; Access = access; Grant = grant; Store = store; Rendition = rendition }
            let cfg = { ProjectionConfig.empty with Environments = Map.ofList [ "e", env ] }
            match ProjectionConfig.parse (ProjectionConfig.render cfg) with
            | Ok back -> Assert.Equal<Environment>(env, Map.find "e" back.Environments)
            | Error es -> Assert.Fail(sprintf "round-trip failed for %A: %A" env es)

[<Fact>]
let ``A44 clause 1 — renderFlow ∘ parseFlow = id on every from × scope × shape × tables`` () =
    let froms =
        [ FlowSource.Model; FlowSource.NoData
          FlowSource.Synthetic (Some "file:p.json"); FlowSource.Synthetic None
          FlowSource.Env "src" ]
    let tableSets = [ []; [ "Customer" ]; [ "Customer"; "Order" ] ]
    let shapeOpts = [ None; Some Shape.Bundle; Some Shape.Ssdt; Some Shape.Skeleton; Some Shape.Manifest ]
    // `sink` + `src` always present so a `from: env` flow resolves its endpoints.
    let baseEnvs =
        [ directEnv "sink" None None; directEnv "src" None None ]
        |> List.map (fun e -> e.Name, e) |> Map.ofList
    for from in froms do
      for scope in allScopeOpts do
        for shape in shapeOpts do
          for tables in tableSets do
            for rekey in [ None; Some "file:users.csv" ] do
              let flow = { Name = "f"; From = from; To = "sink"; Rekey = rekey; Tables = tables; Reconcile = []; Scope = scope; Shape = shape; Shaping = None }
              let cfg = { ProjectionConfig.empty with Environments = baseEnvs; Flows = Map.ofList [ "f", flow ] }
              match ProjectionConfig.parse (ProjectionConfig.render cfg) with
              | Ok back -> Assert.Equal<Flow>(flow, Map.find "f" back.Flows)
              | Error es -> Assert.Fail(sprintf "round-trip failed for %A: %A" flow es)

// ---------------------------------------------------------------------------
// CLAUSE 3 — DIRECTION DERIVED: `Direction` is a pure function of
// `(srcRendition, sinkRendition, scope/origin)`, never a stored knob. The
// independent oracle below IS the falsifiable spec; the test pins that
// `resolveFlowSpec` agrees with it on every generated variant, locking the
// `directionOf` ⟷ `reverseLegOf` agreement.
// ---------------------------------------------------------------------------

/// The independent direction oracle (THE_CONFIG_CONTROL_PLANE §3 G2): synthetic
/// mint → UpSynthetic; logical-source → physical-live-sink env move → UpLegacy
/// (the reverse leg); physical → physical env move → UpPeer; everything else
/// (model/none down-leg, any other env shape) → Down. A pure function of the
/// flow's origin + the two endpoints' renditions — no stored field consulted.
let private directionOracle (cfg: ProjectionConfig) (flow: Flow) : MovementDirection =
    let renditionOf name =
        Map.tryFind name cfg.Environments |> Option.bind (fun (e: Environment) -> e.Rendition)
    let isDirect name =
        match Map.tryFind name cfg.Environments with
        | Some e -> (match e.Access with Access.Direct _ -> true | _ -> false)
        | None -> false
    match flow.From with
    | FlowSource.Synthetic _ -> MovementDirection.UpSynthetic
    | FlowSource.Model | FlowSource.NoData -> MovementDirection.Down
    | FlowSource.Env src ->
        match renditionOf src, renditionOf flow.To with
        | Some Rendition.Logical, Some Rendition.Physical when isDirect src && isDirect flow.To ->
            MovementDirection.UpLegacy
        | Some Rendition.Physical, Some Rendition.Physical -> MovementDirection.UpPeer
        | _ -> MovementDirection.Down

[<Fact>]
let ``A44 clause 3 — Direction is a pure function of (src, sink, origin), never a stored knob`` () =
    Prop.forAll (Arb.fromGen genVariant) (fun (cfg, flow) ->
        match Command.resolveFlowSpec cfg flow preview with
        | Ok spec -> spec.Direction = directionOracle cfg flow
        // A resolve failure (e.g. synthetic with no profile — none generated) is
        // not a direction claim; the property is vacuously true there.
        | Error _ -> true)
    |> Check.QuickThrowOnFailure

[<Fact>]
let ``A44 clause 3 — the reverse leg (B→A) routes to RunReverseLeg; a peer (A→A) does not`` () =
    // Pins the `directionOf` ⟷ `reverseLegOf` ⟷ `planMovement` agreement at the
    // routing seam: a logical→physical live data move is the reverse leg.
    let mk srcR sinkR =
        let cfg =
            { ProjectionConfig.empty with
                Environments =
                    [ directEnv "src" None (Some srcR); directEnv "sink" (Some Grant.DataOnly) (Some sinkR) ]
                    |> List.map (fun e -> e.Name, e) |> Map.ofList
                Flows = Map.empty
                // J3 — the reverse leg renders its contracts from the authored
                // model, so the legacy variant carries one (plan-time gate).
                Shaping = { ProjectionConfig.empty.Shaping with Model = { ProjectionConfig.empty.Shaping.Model with Path = Some "model.json" } } }
        let flow = { Name = "leg"; From = FlowSource.Env "src"; To = "sink"; Rekey = None; Tables = []; Reconcile = []; Scope = Some Scope.Data; Shape = None; Shaping = None }
        cfg, flow
    let legacyCfg, legacyFlow = mk Rendition.Logical Rendition.Physical
    Assert.Equal(MovementDirection.UpLegacy, (Command.resolveFlowSpec legacyCfg legacyFlow commit |> mustOk).Direction)
    match (Command.planFlow legacyCfg legacyFlow commit).Action with
    | PlanAction.RunReverseLeg _ -> ()
    | other -> Assert.Fail(sprintf "expected RunReverseLeg for B→A, got %A" other)
    let peerCfg, peerFlow = mk Rendition.Physical Rendition.Physical
    Assert.Equal(MovementDirection.UpPeer, (Command.resolveFlowSpec peerCfg peerFlow commit |> mustOk).Direction)
    match (Command.planFlow peerCfg peerFlow commit).Action with
    | PlanAction.Transfer _ -> ()
    | other -> Assert.Fail(sprintf "expected Transfer for A→A peer, got %A" other)

// ---------------------------------------------------------------------------
// CLAUSE 2 — TOTALITY / SPANNING: `reachable ⇔ expressible` (THE forcing
// function, the analog of `required ⇔ surveyed`). Two directions.
// ---------------------------------------------------------------------------

/// A coarse constructor tag for each model-bearing `PlanAction` — the surface
/// the totality quantifies over (the engine-consumable spec family). Names the
/// constructor; equality is structural so the witness-set is a `Set`.
let private actionTag (action: PlanAction) : string =
    match action with
    | PlanAction.PublishBundle _     -> "PublishBundle"
    | PlanAction.EmitSkeleton _      -> "EmitSkeleton"
    | PlanAction.EmitBundle _        -> "EmitBundle"
    | PlanAction.DeployDocker _      -> "DeployDocker"
    | PlanAction.PreviewSchema _     -> "PreviewSchema"
    | PlanAction.Transfer _          -> "Transfer"
    | PlanAction.RunReverseLeg _     -> "RunReverseLeg"
    | PlanAction.SynthesizeAndLoad _ -> "SynthesizeAndLoad"
    | PlanAction.MigrateWithData _   -> "MigrateWithData"
    | PlanAction.PublishAndLoad _    -> "PublishAndLoad"
    | PlanAction.Migrate _           -> "Migrate"
    | _                              -> "(other)"

/// The model-bearing `PlanAction` constructors `planMovement ∘ resolveFlowSpec`
/// must span (THE_CONFIG_CONTROL_PLANE §2 directional table; `MovementSpec.fs`
/// §226-287). After S6.1 (the flow `shape` field → `EmitSkeleton`) and S6.2 (the
/// store-trigger ConfigFile wiring → `PublishBundle`/`PublishAndLoad`), the set
/// is the WHOLE model-bearing surface — `residualActions` is now ∅, the strongest
/// A44 statement (`expressible = reachable`, no excluded arm). The publish arms
/// are harvested over the provenance-bearing `publishCfg`; the rest over the
/// bare-model `spanningCfg`; `allReachable` is their union.
let private mustReachActions : Set<string> =
    Set.ofList
        [ "PublishBundle"; "PublishAndLoad"
          "EmitSkeleton"; "EmitBundle"; "DeployDocker"; "PreviewSchema"
          "Transfer"; "RunReverseLeg"; "SynthesizeAndLoad"; "MigrateWithData"; "Migrate" ]

/// The named A44 residual — the model-bearing arms reachable in the engine
/// (`planMovement`) but NOT via the flow resolver (`resolveFlowSpec`). After
/// S6.1 + S6.2 this is EMPTY: every model-bearing arm now has a flow pre-image
/// (`expressible = reachable`), the strongest A44 statement. Kept as an explicit
/// (empty) set so a future arm that regresses out of reach has a named home
/// rather than silently widening the exclusion.
let private residualActions : Set<string> =
    Set.empty

/// A representative estate spanning every reachable model-bearing action: a
/// down-leg model publish (bundle/docker/live), a synthetic mint, a peer data
/// transfer, a B→A reverse leg, and a migrate-with-data — each a real flow.
let private spanningCfg : ProjectionConfig =
    ProjectionConfig.parse """
    {
      "environments": {
        "src-phys":  { "access": "direct", "conn": "env:SRC_PHYS_CONN",  "rendition": "physical" },
        "src-logi":  { "access": "direct", "conn": "env:SRC_LOGI_CONN",  "rendition": "logical" },
        "sink-phys": { "access": "direct", "conn": "env:SINK_PHYS_CONN", "rendition": "physical", "grant": "data" },
        "sink-full": { "access": "direct", "conn": "env:SINK_FULL_CONN", "grant": "schema+data" },
        "bundle":    { "access": "bundle", "out": "dist/b", "grant": "schema+data" },
        "lab":       { "access": "docker", "grant": "schema+data" }
      },
      "flows": {
        "publish-bundle": { "from": "model",     "to": "bundle" },
        "publish-docker": { "from": "model",     "to": "lab" },
        "publish-live":   { "from": "model",     "to": "sink-full" },
        "skeleton":       { "from": "none",       "to": "bundle", "scope": "schema", "shape": "skeleton" },
        "synth":          { "from": "synthetic", "to": "sink-phys", "profile": "file:p.json" },
        "peer":           { "from": "src-phys",  "to": "sink-phys", "scope": "data" },
        "legacy":         { "from": "src-logi",  "to": "sink-phys", "scope": "data" },
        "migrate-data":   { "from": "src-phys",  "to": "sink-full" }
      },
      "model": "model.json"
    }
    """ |> mustOk

/// The provenance-bearing publish estate (S6.2, operator decision 1 — wire
/// `ModelSource.ConfigFile` into flows). The publish-with-provenance arms
/// (`PublishBundle`/`PublishAndLoad`) fire when the flow targets a place that
/// carries a `store` AND the config has a load provenance (`SourcePath = Some`)
/// AND a model path. Here `bundle`/`sink-full` carry a `store`; `SourcePath` is
/// overlaid below (mirroring `ProjectionConfig.fromFile`). `publish-bundle`
/// (folder, store) → `PublishBundle` under preview; `publish-live` (live, full
/// grant, store) under commit → `PublishAndLoad`.
let private publishCfg : ProjectionConfig =
    let parsed =
        ProjectionConfig.parse """
        {
          "environments": {
            "sink-full": { "access": "direct", "conn": "env:SINK_FULL_CONN", "grant": "schema+data", "store": "lifecycle/full.json" },
            "bundle":    { "access": "bundle", "out": "dist/b", "grant": "schema+data", "store": "lifecycle/bundle.json" }
          },
          "flows": {
            "publish-bundle": { "from": "model", "to": "bundle" },
            "publish-live":   { "from": "model", "to": "sink-full" }
          },
          "model": "model.json"
        }
        """ |> mustOk
    // The load provenance the runner resolves the ConfigFile path from — set the
    // way `fromFile` does (it is load provenance, never a rendered JSON field).
    { parsed with SourcePath = Some "projection.json" }

/// Every action reachable by `planMovement ∘ resolveFlowSpec` over the spanning
/// estate, across preview/commit and `from:none` skeleton shape. The harvested
/// reachable surface.
let private reachableActions (cfg: ProjectionConfig) : Set<string> =
    [ for KeyValue (_, flow) in cfg.Flows do
        for opts in [ preview; commit ] do
            yield actionTag (Command.planFlow cfg flow opts).Action ]
    |> Set.ofList

/// The full reachable surface: the bare-model spanning estate UNION the
/// provenance-bearing publish estate (which routes the ConfigFile publish arms).
let private allReachable : Set<string> =
    Set.union (reachableActions spanningCfg) (reachableActions publishCfg)

// -- reachable ⇒ expressible (the gate that names the residual) --------------

[<Fact>]
let ``A44 clause 2 — reachable ⇒ expressible: every model-bearing PlanAction (minus the named residual) is flow-reachable`` () =
    // The forcing function: each model-bearing constructor in `mustReachActions`
    // must be produced by `planMovement ∘ resolveFlowSpec` for SOME flow. A
    // missing one is a spec the config cannot express — the canary fails loud.
    let reached = allReachable
    let unreached = Set.difference mustReachActions reached
    Assert.True(
        Set.isEmpty unreached,
        sprintf "model-bearing PlanActions no flow can express: %A" (Set.toList unreached))

[<Fact>]
let ``A44 residual — PublishBundle/PublishAndLoad are flow-reachable`` () =
    // RESOLVED (S6.2, operator decision 1 — wire ConfigFile into flows): the
    // publish-with-provenance arms fire when `spec.Model = ModelSource.ConfigFile`,
    // which `resolveFlowSpec` now emits exactly when the flow targets a place that
    // carries a `store` (provenance configured) AND the config has a load
    // provenance (`SourcePath = Some`) AND a model path. `publishCfg` exercises
    // both: a folder store sink (preview → PublishBundle) and a live full-grant
    // store sink (commit → PublishAndLoad). Both are now in `mustReachActions`.
    let reached = reachableActions publishCfg
    Assert.Contains("PublishBundle", reached)
    Assert.Contains("PublishAndLoad", reached)

[<Fact>]
let ``A44 residual — EmitSkeleton is flow-reachable`` () =
    // RESOLVED (S6.1, operator decision 2 — add a flow `shape` field): the flow
    // vocabulary now carries an optional `"shape": "bundle" | "ssdt" | "skeleton"`;
    // `resolveFlowSpec` resolves it to `MovementSpec.Shape`, so a folder flow with
    // `"shape": "skeleton"` lands on `EmitSkeleton`. The `skeleton` flow in
    // `spanningCfg` exercises it; `EmitSkeleton` is now in `mustReachActions`.
    let reached = reachableActions spanningCfg
    Assert.Contains("EmitSkeleton", reached)

[<Fact>]
let ``A44 clause 2 — the residual is ∅: mustReach spans the whole model-bearing surface (the strongest A44 statement)`` () =
    // After S6.1 (the flow `shape` field) and S6.2 (the store-trigger ConfigFile
    // wiring), `residualActions` is EMPTY: every model-bearing arm has a flow
    // pre-image — `expressible = reachable` with no excluded arm, the strongest
    // form of the A44 law. The partition stays meaningful: `mustReach` ⊎ `residual`
    // is the whole model-bearing surface, so a future arm that regresses out of
    // reach forces this test (it would no longer partition) rather than silently
    // widening an (empty) exclusion.
    let allModelBearing =
        Set.ofList
            [ "PublishBundle"; "EmitSkeleton"; "EmitBundle"; "DeployDocker"; "PreviewSchema"
              "Transfer"; "RunReverseLeg"; "SynthesizeAndLoad"; "MigrateWithData"; "PublishAndLoad"; "Migrate" ]
    Assert.True(Set.isEmpty residualActions, "residualActions must be ∅ after S6.1 + S6.2")
    Assert.Equal<Set<string>>(allModelBearing, mustReachActions)
    Assert.Equal<Set<string>>(allModelBearing, Set.union mustReachActions residualActions)
    Assert.Equal<Set<string>>(residualActions, Set.difference allModelBearing mustReachActions)
    Assert.True(Set.isEmpty (Set.intersect mustReachActions residualActions))

// -- expressible ⇒ reachable (no parse-accepted config that crashes) ---------

[<Fact>]
let ``A44 clause 2 — expressible ⇒ reachable: every parse-accepted flow resolves to a real action or a NAMED refusal`` () =
    // Extends the `planFlow is TOTAL` sweep over the generated config space: a
    // flow shape the parser accepts never crashes and never returns an un-coded
    // error — it lands on a real `PlanAction` or a `Refused` carrying a coded
    // `ValidationError` + a named exit.
    Prop.forAll (Arb.fromGen genVariant) (fun (cfg, flow) ->
        let runs = [ preview; commit; { preview with Fresh = true }; { commit with AllowDrops = true } ]
        runs |> List.forall (fun opts ->
            match (Command.planFlow cfg flow opts).Action with
            | PlanAction.Refused (code, error) ->
                List.contains code [ 1; 2; 6; 9 ]
                && not (System.String.IsNullOrWhiteSpace error.Code)
                && not (System.String.IsNullOrWhiteSpace error.Message)
            | _ -> true))
    |> Check.QuickThrowOnFailure
