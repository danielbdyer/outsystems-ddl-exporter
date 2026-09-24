module Projection.Tests.SamplingPolicyTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// Evidence tiering (`SamplingPolicy`) — the operator's per-kind cell-
// evidence caps, and the named-downgrade law: a sampled kind ALWAYS gets a
// diagnostic (`SamplingDiagnostics.emit`), a full-scan policy emits
// nothing. Exactness tiering semantics (exact aggregates under any cap;
// Values truncation; derived-partition exclusion) are pinned end-to-end by
// the corpus harness's tiered leg; these tests pin the pure policy algebra.
// ---------------------------------------------------------------------------

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_TEST_SAMPLING" parts |> mustOk
let private mkName (s: string) : Name = Name.create s |> mustOk

let private kindOf (label: string) : Kind =
    Kind.create (mkKey [label]) (mkName label)
        (TableId.create "dbo" ("OSUSR_" + label) |> mustOk)
        [ { Attribute.create (mkKey [label; "Id"]) (mkName "Id") Integer with IsPrimaryKey = true; IsMandatory = true } ]

let private orders  = kindOf "Orders"
let private events  = kindOf "Events"
let private catalog : Catalog =
    IRBuilders.mkCatalog [ IRBuilders.mkModule (mkKey ["Module"]) (mkName "M") [ orders; events ] ]

[<Fact>]
let ``SamplingPolicy.capFor: the default governs unpinned kinds; a pin overrides it`` () =
    let policy =
        { DefaultMaxRows = Some 50000
          Overrides      = Map.ofList [ events.SsKey, Some 2000 ] }
    Assert.Equal(Some 50000, SamplingPolicy.capFor orders.SsKey policy)
    Assert.Equal(Some 2000,  SamplingPolicy.capFor events.SsKey policy)

[<Fact>]
let ``SamplingPolicy.capFor: an explicit None pin EXEMPTS a kind from the default cap`` () =
    let policy =
        { DefaultMaxRows = Some 50000
          Overrides      = Map.ofList [ orders.SsKey, None ] }
    Assert.Equal(None, SamplingPolicy.capFor orders.SsKey policy)
    Assert.True(SamplingPolicy.isSampled events.SsKey policy)
    Assert.False(SamplingPolicy.isSampled orders.SsKey policy)

[<Fact>]
let ``SamplingPolicy: fullScan samples nothing and uniform None is fullScan`` () =
    Assert.True(SamplingPolicy.isFullScan SamplingPolicy.fullScan)
    Assert.Equal(SamplingPolicy.fullScan, SamplingPolicy.uniform None)
    Assert.False(SamplingPolicy.isSampled orders.SsKey SamplingPolicy.fullScan)
    Assert.False(SamplingPolicy.isFullScan (SamplingPolicy.uniform (Some 10)))

[<Fact>]
let ``SamplingPolicy.isFullScan: all-None overrides with no default is still full scan`` () =
    let policy =
        { DefaultMaxRows = None
          Overrides      = Map.ofList [ orders.SsKey, None ] }
    Assert.True(SamplingPolicy.isFullScan policy)

[<Fact>]
let ``SamplingDiagnostics.emit: a full-scan policy names no downgrades`` () =
    Assert.Empty(SamplingDiagnostics.emit catalog SamplingPolicy.fullScan)

[<Fact>]
let ``SamplingDiagnostics.emit: one named Info downgrade per sampled kind, none for exempted kinds`` () =
    let policy =
        { DefaultMaxRows = Some 50000
          Overrides      = Map.ofList [ orders.SsKey, None ] }
    let diags = SamplingDiagnostics.emit catalog policy
    let d = Assert.Single diags
    Assert.Equal(DiagnosticSeverity.Info, d.Severity)
    Assert.Equal("profiler.evidence.sampled", d.Code)
    Assert.Contains("Events", d.Message)
    Assert.Contains("50000", d.Message)
    Assert.Contains("EXACT", d.Message)
