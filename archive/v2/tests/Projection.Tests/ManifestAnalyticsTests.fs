module Projection.Tests.ManifestAnalyticsTests

// H-071/072/073/075/076 + NM-36 — the pass-chain schema-intelligence analytics
// reach their documented consumer, the manifest. `buildFull` threads the composed
// pipeline state; each analytics section renders when present and is OMITTED when
// absent (so a manifest built without state is byte-identical to the pre-analytics
// shape — the backward-compat guard the existing manifest goldens rest on).

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

let private k1 = kindKey ["A"]
let private k2 = kindKey ["B"]

let private stateWithAnalytics : ComposeState =
    { ComposeState.initial sampleCatalog with
        CentralityRanking = Some { Scores = [ { SsKey = k1; Score = 0.7M }; { SsKey = k2; Score = 0.3M } ]; Iterations = 4 }
        BoundedContexts   = Some { Candidates = [ { AnchorKey = k1; Members = [ k1; k2 ]; InternalEdgeCount = 1; ExternalEdgeCount = 0 } ] }
        ProfileAnomalies  = Some { HighNullRateColumns = [ k2, 0.9M ]; HighCvColumns = [] }
        SchemaComplexity  = Some { CyclomaticComplexity = 2; CouplingIndex = 1.5M; CohesionIndex = 0.5M
                                   DepthOfInheritance = 1; NullabilityRatio = 0.25M; OverallScore = 0.8M }
        QueryHints        = Some { FillFactorSuggestions = [ k1, 70 ] }
        CascadeShockZones = Some [ { Root = k1; Reachable = [ k2 ] } ] }

[<Fact>]
let ``buildFull threads the composed analytics into the Manifest`` () =
    let m = ManifestEmitter.buildFull Profile.empty [] None None [] [] (Some stateWithAnalytics) sampleCatalog
    Assert.Equal(4, (Option.get m.Centrality).Iterations)
    Assert.Equal(1, (Option.get m.BoundedContexts).Candidates.Length)
    Assert.Equal(1, (Option.get m.ProfileAnomalies).HighNullRateColumns.Length)
    Assert.Equal(0.8M, (Option.get m.SchemaComplexity).OverallScore)
    Assert.Equal(1, (Option.get m.QueryHints).FillFactorSuggestions.Length)
    Assert.Equal(1, m.CascadeShockZones.Length)

[<Fact>]
let ``toJson renders every analytics section when the state is present; byte-deterministic (T1)`` () =
    let m = ManifestEmitter.buildFull Profile.empty [] None None [] [] (Some stateWithAnalytics) sampleCatalog
    let json = ManifestEmitter.toJson m
    Assert.Contains("\"centrality\"", json)
    Assert.Contains("\"boundedContexts\"", json)
    Assert.Contains("\"profileAnomalies\"", json)
    Assert.Contains("\"schemaComplexity\"", json)
    Assert.Contains("\"queryHints\"", json)
    Assert.Contains("\"cascadeShockZones\"", json)
    Assert.Equal(json, ManifestEmitter.toJson m)

[<Fact>]
let ``a manifest built without composed state omits every analytics section`` () =
    let m = ManifestEmitter.buildFull Profile.empty [] None None [] [] None sampleCatalog
    let json = ManifestEmitter.toJson m
    Assert.DoesNotContain("\"centrality\"", json)
    Assert.DoesNotContain("\"boundedContexts\"", json)
    Assert.DoesNotContain("\"profileAnomalies\"", json)
    Assert.DoesNotContain("\"schemaComplexity\"", json)
    Assert.DoesNotContain("\"queryHints\"", json)
    Assert.DoesNotContain("\"cascadeShockZones\"", json)
    Assert.True(m.Centrality.IsNone && m.CascadeShockZones.IsEmpty)
