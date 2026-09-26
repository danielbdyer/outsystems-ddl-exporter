module Projection.Tests.AdvisoryTuningTests

open Xunit
open Projection.Core.Passes

// F6 (audit 2026-06-17) — the centralized advisory-tuning defaults. Advisory
// outputs (SchemaComplexity scores / ProfileAnomaly flags / Centrality ranks)
// are diagnostic-only and NOT in any golden corpus, and the pass tests assert
// structural properties (ranges / ordering) rather than absolute values — so
// these defaults are otherwise unpinned. This guard pins them, so a default
// change is a loud, deliberate edit (and stays the byte-identical lift of the
// former per-pass constants until an operator override lands).

[<Fact>]
let ``F6: SchemaComplexity advisory weights + caps hold their documented defaults`` () =
    let s = AdvisoryTuning.defaults.SchemaComplexity
    Assert.Equal(0.20m, s.WeightCyclomatic)
    Assert.Equal(0.20m, s.WeightCoupling)
    Assert.Equal(0.15m, s.WeightCohesion)
    Assert.Equal(0.25m, s.WeightDepth)
    Assert.Equal(0.20m, s.WeightNullability)
    Assert.Equal(500.0m, s.CapCyclomatic)
    Assert.Equal(5.0m, s.CapCoupling)
    Assert.Equal(20.0m, s.CapDepth)
    Assert.Equal(1.0m, s.CapNullability)

[<Fact>]
let ``F6: ProfileAnomaly advisory sigma holds its documented default (2σ)`` () =
    Assert.Equal(2.0m, AdvisoryTuning.defaults.ProfileAnomalySigma)

[<Fact>]
let ``F6: Centrality advisory PageRank tuning holds its documented defaults`` () =
    let c = AdvisoryTuning.defaults.Centrality
    Assert.Equal(0.85m, c.DampingFactor)
    Assert.Equal(0.000001m, c.ConvergenceEps)
    Assert.Equal(100, c.MaxIterations)
