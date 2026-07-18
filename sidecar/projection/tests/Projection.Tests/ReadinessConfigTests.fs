module Projection.Tests.ReadinessConfigTests

open Xunit
open Projection.Core
open Projection.Pipeline

// CROSS_ENVIRONMENT_READINESS.md §4 (S4) — the `readiness` config block parse +
// the `check shape` verb routing. The espace-safety + aggregation are proven in
// CatalogDiffTests / ReadinessTests; here we cover the config surface, the
// config → PlanAction resolution, the named refusals, and the A44 round-trip.

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private errCodes r = match r with Ok _ -> [] | Error es -> es |> List.map (fun (e: ValidationError) -> e.Code)

let private estateJson = """
{
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "env:CLOUD_DEV_CONN", "rendition": "physical", "archetype": "managed-dml" },
    "cloud-qa":  { "access": "direct", "conn": "env:CLOUD_QA_CONN",  "rendition": "physical", "archetype": "managed-dml" },
    "cloud-uat": { "access": "direct", "conn": "env:CLOUD_UAT_CONN", "rendition": "physical", "archetype": "managed-dml" }
  },
  "readiness": { "schema": "cloud-dev", "confirm": ["cloud-dev", "cloud-qa", "cloud-uat"] }
}
"""

// -- parse -------------------------------------------------------------------

[<Fact>]
let ``readiness: the block parses to schema + confirm`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match cfg.Readiness with
    | Some rs ->
        Assert.Equal("cloud-dev", rs.Schema)
        Assert.Equal<string list>([ "cloud-dev"; "cloud-qa"; "cloud-uat" ], rs.Confirm)
    | None -> Assert.Fail "expected a readiness block"

[<Fact>]
let ``readiness: an absent block ⇒ None (byte-identical movement-only file)`` () =
    let cfg = ProjectionConfig.parse """{ "environments": {} }""" |> mustOk
    Assert.True(cfg.Readiness.IsNone)

[<Fact>]
let ``readiness: a block without a schema is a NAMED parse failure`` () =
    let json = """{ "readiness": { "confirm": ["cloud-qa"] } }"""
    Assert.Contains("cli.config.parseFailed", errCodes (ProjectionConfig.parse json))

[<Fact>]
let ``readiness: parse ∘ render = id over the block (A44)`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    let round = ProjectionConfig.parse (ProjectionConfig.render cfg) |> mustOk
    Assert.Equal<ReadinessSpec option>(cfg.Readiness, round.Readiness)

[<Fact>]
let ``readiness.estate: the A44 tuning knobs parse and round-trip (decisionFloor / asymmetryFactor)`` () =
    let json = """
    {
      "environments": { "cloud-dev": { "access": "direct", "conn": "env:C", "rendition": "physical", "archetype": "managed-dml" } },
      "readiness": { "schema": "cloud-dev", "confirm": ["cloud-dev"], "estate": { "decisionFloor": 250, "asymmetryFactor": 10 } }
    }
    """
    let cfg = ProjectionConfig.parse json |> mustOk
    match cfg.Readiness with
    | Some rs ->
        Assert.Equal(Some 250L, rs.DecisionFloor)
        Assert.Equal(Some 10L, rs.AsymmetryFactor)
    | None -> Assert.Fail "expected a readiness block"
    let round = ProjectionConfig.parse (ProjectionConfig.render cfg) |> mustOk
    Assert.Equal<ReadinessSpec option>(cfg.Readiness, round.Readiness)

[<Fact>]
let ``readiness.estate: a non-positive decisionFloor is a NAMED parse failure`` () =
    let json = """{ "readiness": { "schema": "cloud-dev", "confirm": ["cloud-dev"], "estate": { "decisionFloor": 0 } } }"""
    Assert.Contains("cli.config.parseFailed", errCodes (ProjectionConfig.parse json))

// -- `check shape` routing ---------------------------------------------------

[<Fact>]
let ``check shape: resolves to CheckShape with the agreed + confirm conn-refs`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "shape" ]).Action with
    | PlanAction.CheckShape (agreedLabel, agreedRef, confirm, _) ->
        Assert.Equal("cloud-dev", agreedLabel)
        Assert.Equal("env:CLOUD_DEV_CONN", agreedRef)
        Assert.Equal<string list>([ "cloud-dev"; "cloud-qa"; "cloud-uat" ], confirm |> List.map fst)
    | other -> Assert.Fail(sprintf "expected CheckShape; got %A" other)

[<Fact>]
let ``check shape: no readiness block ⇒ named refusal`` () =
    let cfg = ProjectionConfig.parse """{ "environments": {} }""" |> mustOk
    match (Command.planCheck cfg [ "shape" ]).Action with
    | PlanAction.Refused (_, e) -> Assert.Equal("cli.check.shapeNoBlock", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)

[<Fact>]
let ``check shape: a non-direct (bundle) env in the set ⇒ named refusal (not silently skipped)`` () =
    let json = """
{
  "environments": {
    "cloud-dev":   { "access": "direct", "conn": "env:CLOUD_DEV_CONN" },
    "on-prem-dev": { "access": "bundle", "out": "dist/on-prem-dev", "grant": "schema+data" }
  },
  "readiness": { "schema": "cloud-dev", "confirm": ["cloud-dev", "on-prem-dev"] }
}
"""
    let cfg = ProjectionConfig.parse json |> mustOk
    match (Command.planCheck cfg [ "shape" ]).Action with
    | PlanAction.Refused (_, e) -> Assert.Equal("cli.check.shapeNotDirect", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)

// -- model.env (the schema source as an environment reference) ---------------
// CROSS_ENVIRONMENT_READINESS §4 (model-from-environment) — `model.env` points
// the schema source into the `environments` registry by NAME (like `flow.from`
// / `readiness.schema`), instead of inlining a connection. It resolves to the
// named environment's live OSSYS conn-ref, and the readiness gate's agreed
// shape defaults to it.

let private modelEnvJson = """
{
  "model": { "env": "cloud-dev", "modules": ["Sales"] },
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "file:./secrets/cloud-dev.conn", "rendition": "physical", "archetype": "managed-dml" },
    "cloud-qa":  { "access": "direct", "conn": "file:./secrets/cloud-qa.conn",  "rendition": "physical", "archetype": "managed-dml" },
    "cloud-uat": { "access": "direct", "conn": "file:./secrets/cloud-uat.conn", "rendition": "physical", "archetype": "managed-dml" }
  },
  "readiness": { "confirm": ["cloud-dev", "cloud-qa", "cloud-uat"] }
}
"""

[<Fact>]
let ``model.env: resolves to the named environment's conn as Shaping.Model.Ossys`` () =
    let cfg = ProjectionConfig.parse modelEnvJson |> mustOk
    // The resolution is transparent — `env: "cloud-dev"` yields exactly the
    // `ossys` cloud-dev's `conn` declares, so emission reads the live source
    // unchanged (no behavioural fork from the explicit-`ossys` form).
    Assert.Equal(Some "file:./secrets/cloud-dev.conn", cfg.Shaping.Model.Ossys)

[<Fact>]
let ``model.env: an omitted readiness.schema defaults to model.env`` () =
    let cfg = ProjectionConfig.parse modelEnvJson |> mustOk
    match cfg.Readiness with
    | Some rs ->
        Assert.Equal("cloud-dev", rs.Schema)
        Assert.Equal<string list>([ "cloud-dev"; "cloud-qa"; "cloud-uat" ], rs.Confirm)
    | None -> Assert.Fail "expected a readiness block"

[<Fact>]
let ``model.env: an explicit readiness.schema still wins over the model.env default`` () =
    let json = """
{
  "model": { "env": "cloud-dev" },
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "file:./secrets/cloud-dev.conn" },
    "cloud-qa":  { "access": "direct", "conn": "file:./secrets/cloud-qa.conn" }
  },
  "readiness": { "schema": "cloud-qa", "confirm": ["cloud-dev", "cloud-qa"] }
}
"""
    let cfg = ProjectionConfig.parse json |> mustOk
    match cfg.Readiness with
    | Some rs -> Assert.Equal("cloud-qa", rs.Schema)
    | None -> Assert.Fail "expected a readiness block"

[<Fact>]
let ``model.env: the defaulted readiness.schema is stable across parse ∘ render (A44)`` () =
    let cfg = ProjectionConfig.parse modelEnvJson |> mustOk
    // render emits the RESOLVED schema explicitly; re-parse needs no default,
    // so the config value is preserved (A44 holds at the value, not the text).
    let round = ProjectionConfig.parse (ProjectionConfig.render cfg) |> mustOk
    Assert.Equal<ReadinessSpec option>(cfg.Readiness, round.Readiness)

[<Fact>]
let ``model.env + ossys: two ways to name the live source ⇒ named refusal`` () =
    let json = """
{
  "model": { "env": "cloud-dev", "ossys": "file:./secrets/standalone.conn" },
  "environments": { "cloud-dev": { "access": "direct", "conn": "file:./secrets/cloud-dev.conn" } }
}
"""
    Assert.Contains("cli.config.modelEnvAndOssys", errCodes (ProjectionConfig.parse json))

[<Fact>]
let ``model.env: an unknown environment ⇒ named refusal`` () =
    let json = """
{
  "model": { "env": "cloud-prod" },
  "environments": { "cloud-dev": { "access": "direct", "conn": "file:./secrets/cloud-dev.conn" } }
}
"""
    Assert.Contains("cli.config.modelEnvUnknown", errCodes (ProjectionConfig.parse json))

[<Fact>]
let ``model.env: a non-direct (bundle) environment ⇒ named refusal`` () =
    let json = """
{
  "model": { "env": "on-prem-dev" },
  "environments": { "on-prem-dev": { "access": "bundle", "out": "dist/on-prem-dev", "grant": "schema+data" } }
}
"""
    Assert.Contains("cli.config.modelEnvNotDirect", errCodes (ProjectionConfig.parse json))

// -- the estate knobs (wave A6): readiness.estate.repairBand ------------------

[<Fact>]
let ``readiness.estate: the repairBand knob parses, rides the estate args, and round-trips (A44)`` () =
    let json = """
{
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "env:CLOUD_DEV_CONN" },
    "cloud-qa":  { "access": "direct", "conn": "env:CLOUD_QA_CONN" }
  },
  "readiness": { "schema": "cloud-dev", "confirm": ["cloud-dev", "cloud-qa"], "estate": { "repairBand": 250000 } }
}
"""
    let cfg = ProjectionConfig.parse json |> mustOk
    match cfg.Readiness with
    | Some rs -> Assert.Equal(Some 250_000L, rs.RepairBand)
    | None -> Assert.Fail "expected a readiness block"
    // The knob is CONSUMED in the same wave it parses (A44 — never inert):
    // it rides the estate verb's args.
    match (Command.planCheck cfg [ "estate" ]).Action with
    | PlanAction.CheckEstate args -> Assert.Equal(Some 250_000L, args.RepairBand)
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)
    let round = ProjectionConfig.parse (ProjectionConfig.render cfg) |> mustOk
    Assert.Equal<ReadinessSpec option>(cfg.Readiness, round.Readiness)

[<Fact>]
let ``readiness.estate: the repairBandByEntity map parses, rides the estate args, and round-trips`` () =
    let json = """
{
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "env:CLOUD_DEV_CONN" },
    "cloud-qa":  { "access": "direct", "conn": "env:CLOUD_QA_CONN" }
  },
  "readiness": { "schema": "cloud-dev", "confirm": ["cloud-dev", "cloud-qa"], "estate": { "repairBand": 250000, "repairBandByEntity": { "OrderLine": 1000000, "Country": 10 } } }
}
"""
    let cfg = ProjectionConfig.parse json |> mustOk
    match cfg.Readiness with
    | Some rs ->
        Assert.Equal(Some 1_000_000L, Map.tryFind "OrderLine" rs.RepairBandByEntity)
        Assert.Equal(Some 10L, Map.tryFind "Country" rs.RepairBandByEntity)
    | None -> Assert.Fail "expected a readiness block"
    // Consumed in the same wave (A44): it rides the estate verb's args.
    match (Command.planCheck cfg [ "estate" ]).Action with
    | PlanAction.CheckEstate args -> Assert.Equal(Some 10L, Map.tryFind "Country" args.RepairBandByEntity)
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)
    let round = ProjectionConfig.parse (ProjectionConfig.render cfg) |> mustOk
    Assert.Equal<ReadinessSpec option>(cfg.Readiness, round.Readiness)

[<Fact>]
let ``readiness.estate: an absent estate block leaves the band on the engine default (None), round-tripping to no key`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match cfg.Readiness with
    | Some rs -> Assert.Equal(None, rs.RepairBand)
    | None -> Assert.Fail "expected a readiness block"
    let rendered = ProjectionConfig.render cfg
    Assert.DoesNotContain("repairBand", rendered)

[<Fact>]
let ``readiness.estate: the fidelityFlow knob parses, rides the estate args, and round-trips (A44/RT-10)`` () =
    let json = """
{
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "env:CLOUD_DEV_CONN" },
    "cloud-qa":  { "access": "direct", "conn": "env:CLOUD_QA_CONN" }
  },
  "readiness": { "schema": "cloud-dev", "confirm": ["cloud-dev", "cloud-qa"], "estate": { "fidelityFlow": "uat-load" } }
}
"""
    let cfg = ProjectionConfig.parse json |> mustOk
    match cfg.Readiness with
    | Some rs -> Assert.Equal(Some "uat-load", rs.FidelityFlow)
    | None -> Assert.Fail "expected a readiness block"
    // Consumed in the same wave it parses (A44 — never inert): it rides the
    // estate verb's args so the board reads the flow's proof.
    match (Command.planCheck cfg [ "estate" ]).Action with
    | PlanAction.CheckEstate args -> Assert.Equal(Some "uat-load", args.FidelityFlow)
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)
    let round = ProjectionConfig.parse (ProjectionConfig.render cfg) |> mustOk
    Assert.Equal<ReadinessSpec option>(cfg.Readiness, round.Readiness)

[<Fact>]
let ``readiness.estate: an absent fidelityFlow leaves the clause unconfigured (None), round-tripping to no key`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match cfg.Readiness with
    | Some rs -> Assert.Equal(None, rs.FidelityFlow)
    | None -> Assert.Fail "expected a readiness block"
    Assert.DoesNotContain("fidelityFlow", ProjectionConfig.render cfg)
