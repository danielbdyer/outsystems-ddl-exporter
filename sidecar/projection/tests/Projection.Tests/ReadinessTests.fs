module Projection.Tests.ReadinessTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The N-way readiness aggregator (CROSS_ENVIRONMENT_READINESS.md §3; S3).
//
// `Readiness.compute` rolls one `Compare` per confirm environment (its data +
// schema against the agreed shape) into an estate verdict. The load-bearing
// logic under test:
//   - the per-env verdict (Ready / Paused / Blocked), which is STRICTER than
//     `Compare.isCompatible` — a non-zero schema delta BLOCKS (the env is
//     supposed to already BE the agreed shape);
//   - the estate roll-up (`isReady` iff every env Ready);
//   - the report renders (text + JSON).
// Espace-invariance itself is proven in `CatalogDiffTests`; here the catalogs
// are equal/divergent by construction to exercise the verdict.
// ---------------------------------------------------------------------------

let private emptyCat : Catalog = Catalog.create [] [] |> Result.value

// A schema delta that is the zero delta (schema matches) and one that is a real
// divergence (norm > 0) — built through the real `CatalogDiff.between`.
let private zeroDelta : CatalogDiff = CatalogDiff.between emptyCat emptyCat
let private realDelta : CatalogDiff = CatalogDiff.between emptyCat sampleCatalog

let private mkReport
    (schema: CatalogDiff option)
    (evidence: bool)
    (dealbreakers: ModelFidelity.DataViolation list)
    : Compare.CompareReport =
    { SourceLabel = "env"
      TargetLabel = "agreed"
      SchemaDelta = schema
      DataEvidenceAvailable = evidence
      DataDealbreakers = dealbreakers }

let private aDealbreaker : ModelFidelity.DataViolation =
    let ec : ModelFidelity.EntityColumn = { Entity = "User"; Column = "Email" }
    { Reference = ec; Kind = ModelFidelity.NotNullButNullsPresent 3L }

// -- Per-env verdict ---------------------------------------------------------

[<Fact>]
let ``verdict: schema matches + no dealbreaker ⇒ Ready`` () =
    Assert.Equal(Readiness.Verdict.Ready, Readiness.verdictOf (mkReport (Some zeroDelta) false []))

[<Fact>]
let ``verdict: schema matches + a data dealbreaker ⇒ Paused`` () =
    Assert.Equal(Readiness.Verdict.Paused, Readiness.verdictOf (mkReport (Some zeroDelta) true [ aDealbreaker ]))

[<Fact>]
let ``verdict: a real schema divergence ⇒ Blocked (stricter than Compare.isCompatible)`` () =
    // Compare.isCompatible would call this "ready to receive" (a schema change is
    // expected downstream work); readiness BLOCKS — the env is not the agreed shape.
    Assert.Equal(Readiness.Verdict.Blocked, Readiness.verdictOf (mkReport (Some realDelta) false []))

[<Fact>]
let ``verdict: schema could not be compared ⇒ Blocked`` () =
    Assert.Equal(Readiness.Verdict.Blocked, Readiness.verdictOf (mkReport None false []))

// -- Estate roll-up over real operands --------------------------------------

let private operand (label: string) (c: Catalog) : Compare.Operand =
    { Label = label; Catalog = c; Profile = None }

[<Fact>]
let ``compute: a clean estate (every env the agreed shape) ⇒ all Ready, estate ready`` () =
    let agreed = operand "cloud-dev" sampleCatalog
    let envs =
        [ "cloud-dev", operand "cloud-dev" sampleCatalog
          "cloud-qa",  operand "cloud-qa"  sampleCatalog
          "cloud-uat", operand "cloud-uat" sampleCatalog ]
    let report = Readiness.compute "cloud-dev" agreed envs
    Assert.True(Readiness.isReady report)
    Assert.All(report.Envs, fun e -> Assert.Equal(Readiness.Verdict.Ready, e.Verdict))

[<Fact>]
let ``compute: one env not the agreed shape ⇒ that env Blocked, estate NOT ready`` () =
    let agreed = operand "cloud-dev" sampleCatalog
    let envs =
        [ "cloud-dev", operand "cloud-dev" sampleCatalog        // matches
          "cloud-qa",  operand "cloud-qa"  emptyCat ]           // empty ≠ sample ⇒ divergence
    let report = Readiness.compute "cloud-dev" agreed envs
    Assert.False(Readiness.isReady report)
    let verdictOf env = (report.Envs |> List.find (fun e -> e.Env = env)).Verdict
    Assert.Equal(Readiness.Verdict.Ready,   verdictOf "cloud-dev")
    Assert.Equal(Readiness.Verdict.Blocked, verdictOf "cloud-qa")

// -- Renders -----------------------------------------------------------------

[<Fact>]
let ``render: leads with the masthead and closes with an estate verdict`` () =
    let agreed = operand "cloud-dev" sampleCatalog
    let report = Readiness.compute "cloud-dev" agreed [ "cloud-dev", operand "cloud-dev" sampleCatalog ]
    let lines = Readiness.render report
    Assert.Contains(lines, fun (l: string) -> l.Contains "READINESS — the estate against cloud-dev")
    Assert.Contains(lines, fun (l: string) -> l.Contains "ESTATE — all")

[<Fact>]
let ``toJson: carries the estate ready flag and one node per environment`` () =
    let agreed = operand "cloud-dev" sampleCatalog
    let report =
        Readiness.compute "cloud-dev" agreed
            [ "cloud-qa",  operand "cloud-qa"  sampleCatalog
              "cloud-uat", operand "cloud-uat" sampleCatalog ]
    let json = Readiness.toJsonString report
    Assert.Contains("\"ready\"", json)
    Assert.Contains("cloud-qa", json)
    Assert.Contains("cloud-uat", json)

// -- espace-invariance at the realization grain (the normalization) ----------

let private withDefaultName (name: string) (c: Catalog) : Catalog =
    { c with
        Modules =
            c.Modules
            |> List.map (fun m ->
                { m with
                    Kinds =
                        m.Kinds
                        |> List.map (fun k ->
                            { k with Attributes = k.Attributes |> List.map (fun a -> { a with DefaultName = Some (mkName name) }) }) }) }

[<Fact>]
let ``compute: catalogs differing ONLY in espace-variant default-constraint names are READY`` () =
    // OutSystems derives the default-constraint NAME from the physical table, so
    // two espace cells differ there — but the readiness normalization
    // (toLogicalShape) drops it, leaving ONE shape. Without the normalization the
    // DefaultValue facet would fire on the name difference and block. (Triggers /
    // column-checks are covered by the OssysComprehensiveFixtureTests Docker canary.)
    let a = withDefaultName "DF_ABC_CUSTOMER_NAME" sampleCatalog
    let b = withDefaultName "DF_XYZ_CUSTOMER_NAME" sampleCatalog
    let report = Readiness.compute "agreed" (operand "agreed" a) [ "env", operand "env" b ]
    Assert.True(Readiness.isReady report)
