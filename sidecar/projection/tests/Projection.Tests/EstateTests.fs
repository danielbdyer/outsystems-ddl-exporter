module Projection.Tests.EstateTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The estate instrument (`check estate` — CHAPTER_ESTATE_OPEN.md; DECISIONS
// 2026-07-15). Covered here:
//   - A45's pure witness: N espace cells of one model produce ZERO estate
//     findings after the logical-shape normalization (the axiom candidate's
//     promotion; the two-DB Docker canary covers the realization grain).
//   - The finding ⇔ presentation totality seed: every finding kind carries a
//     lane, a plane, and a distinct machine token.
//   - The aggregation: divergences group by key across environments, the
//     environments are named, a strict majority flips the closing clause.
//   - One substrate: the board and estate.json project one report value.
//   - The verb routing (the `estate` planCheck arm): the zero-flag contract,
//     `--against model`, and the named refusals.
// ---------------------------------------------------------------------------

let private emptyCat : Catalog = Catalog.create [] [] |> Result.value

let private operand (label: string) (c: Catalog) : Compare.Operand =
    { Label = label; Catalog = c; Profile = None }

let private agreed : Estate.TargetOperand = Estate.TargetOperand.AgreedEnv "cloud-dev"

// -- A45: espace invariance (the pure witness; promotes the AxiomTests stub) --

/// OutSystems derives default-constraint names from the physical table name,
/// so two espace cells of ONE model differ exactly there (the realization
/// artifacts) while the logical shape is identical.
let private espaceCell (defaultName: string) : Catalog =
    { sampleCatalog with
        Modules =
            sampleCatalog.Modules
            |> List.map (fun m ->
                { m with
                    Kinds =
                        m.Kinds
                        |> List.map (fun k ->
                            { k with Attributes = k.Attributes |> List.map (fun a -> { a with DefaultName = Some (mkName defaultName) }) }) }) }

[<Fact>]
let ``A45: N espace cells of one model produce zero estate findings after toLogicalShape (espace invariance)`` () =
    let cells =
        [ "cloud-dev", operand "cloud-dev" (espaceCell "DF_ABC_CUSTOMER_NAME")
          "cloud-qa",  operand "cloud-qa"  (espaceCell "DF_XYZ_CUSTOMER_NAME")
          "cloud-uat", operand "cloud-uat" (espaceCell "DF_PQR_CUSTOMER_NAME") ]
    let report = Estate.compute agreed (espaceCell "DF_JKL_CUSTOMER_NAME") cells
    Assert.Empty report.Findings
    Assert.Equal(Estate.Verdict.Unified, report.Verdict)
    Assert.True(Estate.isUnified report)

// -- finding ⇔ presentation (the totality seed) -------------------------------

[<Fact>]
let ``presentation: every finding kind carries its lane, plane, and a distinct machine token (finding ⇔ presentation)`` () =
    let kinds = EstateFindingKind.all
    Assert.NotEmpty kinds
    // Tokens are distinct (the projection is injective — keys cannot collide
    // across kinds) and every kind resolves a lane + plane (total matches; the
    // compiler enforces totality, this pins the walkable list's coverage).
    let tokens = kinds |> List.map EstateFindingKind.token
    Assert.Equal(List.length kinds, tokens |> List.distinct |> List.length)
    for kind in kinds do
        EstateFindingKind.laneOf kind |> ignore
        EstateFindingKind.planeOf kind |> ignore

[<Fact>]
let ``presentation: a finding key is stable across mints and carries the kind's token`` () =
    let a = FindingKey.create EstateFindingKind.DataNotNull "Customer.Email"
    let b = FindingKey.create EstateFindingKind.DataNotNull "Customer.Email"
    Assert.Equal(FindingKey.text a, FindingKey.text b)
    Assert.StartsWith("data.notNull:", FindingKey.text a)

// -- the aggregation ----------------------------------------------------------

[<Fact>]
let ``compute: a diverging environment yields DECIDE-lane schema findings naming that environment`` () =
    // cloud-qa is EMPTY against a populated target: every target kind is a
    // presence divergence in cloud-qa; cloud-dev matches and contributes none.
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-dev", operand "cloud-dev" sampleCatalog
              "cloud-qa",  operand "cloud-qa"  emptyCat ]
    Assert.Equal(Estate.Verdict.Converging, report.Verdict)
    Assert.NotEmpty report.Findings
    for f in report.Findings do
        Assert.Equal(EstateLane.Decide, f.Lane)
        Assert.Equal(EstatePlane.Schema, f.Plane)
        Assert.Equal<string list>([ "cloud-qa" ], f.Envs |> List.map fst)
        Assert.Contains("cloud-qa", f.Statement)

[<Fact>]
let ``compute: one divergence in two environments groups onto one key and names both`` () =
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-qa",  operand "cloud-qa"  emptyCat
              "cloud-uat", operand "cloud-uat" emptyCat
              "cloud-dev", operand "cloud-dev" sampleCatalog ]
    // Both empty environments miss the same target kinds — ONE finding per
    // kind, carrying both environments' evidence.
    Assert.NotEmpty report.Findings
    for f in report.Findings do
        Assert.Equal<string list>([ "cloud-qa"; "cloud-uat" ], f.Envs |> List.map fst |> List.sort)
        Assert.Contains("cloud-qa", f.Statement)
        Assert.Contains("cloud-uat", f.Statement)

[<Fact>]
let ``compute: a strict majority of diverging environments turns the closing clause on the target`` () =
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-qa",  operand "cloud-qa"  emptyCat
              "cloud-uat", operand "cloud-uat" emptyCat
              "cloud-dev", operand "cloud-dev" sampleCatalog ]
    // 2 of 3 diverge — the statement says the target may be the one behind.
    for f in report.Findings do
        Assert.Contains("the target may be the one behind", f.Statement)

[<Fact>]
let ``compute: a minority divergence names its environment and never blames the target`` () =
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-qa",  operand "cloud-qa"  emptyCat
              "cloud-uat", operand "cloud-uat" sampleCatalog
              "cloud-dev", operand "cloud-dev" sampleCatalog ]
    // 1 of 3 diverges — no majority clause.
    Assert.NotEmpty report.Findings
    for f in report.Findings do
        Assert.DoesNotContain("the target may be the one behind", f.Statement)

// -- one substrate (board ≡ estate.json over one report value) ----------------

[<Fact>]
let ``one substrate: estate.json carries the verdict, every environment, and every finding the board renders`` () =
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-dev", operand "cloud-dev" sampleCatalog
              "cloud-qa",  operand "cloud-qa"  emptyCat ]
    let json = Estate.toJsonString report
    Assert.Contains("\"verdict\": \"converging\"", json)
    Assert.Contains("cloud-dev", json)
    Assert.Contains("cloud-qa", json)
    for f in report.Findings do
        Assert.Contains(FindingKey.text f.Key, json)
        Assert.Contains(EstateFindingKind.token f.Kind, json)

[<Fact>]
let ``board: the empty state is a full surface (masthead, lanes, matrix, artifacts, action)`` () =
    let report = Estate.compute agreed sampleCatalog [ "cloud-dev", operand "cloud-dev" sampleCatalog ]
    let lines = Estate.render report
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "ESTATE — ")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "DECIDE")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "REPAIR")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "RELAX")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "WATCH")
    Assert.Contains(lines, fun (l: string) -> l.Contains "The interim posture is empty.")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "MATRIX")
    Assert.Contains(lines, fun (l: string) -> l.Contains "estate.json — the full findings record")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "Next: ")

[<Fact>]
let ``board: the action names the first DECIDE finding's key when one exists`` () =
    let report =
        Estate.compute agreed sampleCatalog [ "cloud-qa", operand "cloud-qa" emptyCat ]
    let lines = Estate.render report
    let firstDecide = Estate.laneFindings EstateLane.Decide report |> List.head
    Assert.Contains(lines, fun (l: string) ->
        l.StartsWith "Next: rule the first DECIDE finding" && l.Contains (FindingKey.text firstDecide.Key))

// -- the verb routing (the `estate` planCheck arm) -----------------------------

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es

let private estateJson = """
{
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "env:CLOUD_DEV_CONN" },
    "cloud-qa":  { "access": "direct", "conn": "env:CLOUD_QA_CONN" },
    "cloud-uat": { "access": "direct", "conn": "env:CLOUD_UAT_CONN" }
  },
  "readiness": { "schema": "cloud-dev", "confirm": ["cloud-dev", "cloud-qa", "cloud-uat"] }
}
"""

[<Fact>]
let ``check estate: the zero-flag contract resolves the target and confirm set from the readiness block`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "estate" ]).Action with
    | PlanAction.CheckEstate args ->
        Assert.Equal("cloud-dev", args.TargetLabel)
        Assert.Equal(EstateTargetSource.AgreedEnv "env:CLOUD_DEV_CONN", args.Target)
        Assert.Equal<string list>([ "cloud-dev"; "cloud-qa"; "cloud-uat" ], args.Confirm |> List.map fst)
        Assert.False args.AsJson
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)

[<Fact>]
let ``check estate: --against model selects the authored model and the run names it`` () =
    let json = """
{
  "model": { "env": "cloud-dev" },
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "file:./secrets/cloud-dev.conn" },
    "cloud-qa":  { "access": "direct", "conn": "file:./secrets/cloud-qa.conn" }
  },
  "readiness": { "confirm": ["cloud-dev", "cloud-qa"] }
}
"""
    let cfg = ProjectionConfig.parse json |> mustOk
    match (Command.planCheck cfg [ "estate"; "--against"; "model" ]).Action with
    | PlanAction.CheckEstate args ->
        Assert.Equal("model", args.TargetLabel)
        match args.Target with
        | EstateTargetSource.AuthoredModel (ossys, _) ->
            Assert.Equal(Some "file:./secrets/cloud-dev.conn", ossys)
        | other -> Assert.Fail(sprintf "expected AuthoredModel; got %A" other)
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)

[<Fact>]
let ``check estate: --format json rides the args record`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "estate"; "--format"; "json" ]).Action with
    | PlanAction.CheckEstate args -> Assert.True args.AsJson
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)

[<Fact>]
let ``check estate: no readiness block ⇒ named refusal, exit 2`` () =
    let cfg = ProjectionConfig.parse """{ "environments": {} }""" |> mustOk
    match (Command.planCheck cfg [ "estate" ]).Action with
    | PlanAction.Refused (exit, e) ->
        Assert.Equal(2, exit)
        Assert.Equal("cli.check.estateNoBlock", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)

[<Fact>]
let ``check estate: --against model with no authored model ⇒ named refusal, exit 2`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "estate"; "--against"; "model" ]).Action with
    | PlanAction.Refused (exit, e) ->
        Assert.Equal(2, exit)
        Assert.Equal("cli.check.estateNoModel", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)

[<Fact>]
let ``check estate: a non-direct environment in the confirm set ⇒ named refusal, exit 6 (never silently skipped)`` () =
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
    match (Command.planCheck cfg [ "estate" ]).Action with
    | PlanAction.Refused (exit, e) ->
        Assert.Equal(6, exit)
        Assert.Equal("cli.check.estateNotDirect", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)

[<Fact>]
let ``check estate: an unknown environment ⇒ named refusal, exit 6`` () =
    let json = """
{
  "environments": { "cloud-dev": { "access": "direct", "conn": "env:CLOUD_DEV_CONN" } },
  "readiness": { "schema": "cloud-dev", "confirm": ["cloud-dev", "cloud-prod"] }
}
"""
    let cfg = ProjectionConfig.parse json |> mustOk
    match (Command.planCheck cfg [ "estate" ]).Action with
    | PlanAction.Refused (exit, e) ->
        Assert.Equal(6, exit)
        Assert.Equal("cli.check.estateUnknownEnv", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)
