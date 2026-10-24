module Projection.Tests.StatisticsTests

open Xunit
open Projection.Core

// Recon #5 — the percentile, extracted from `LiveProfiler` (an SQL adapter) into a
// pure Core `Statistics` module, is now directly unit-testable with NO database (the
// thesis of moving the derivations home). PERCENTILE_CONT semantics over a pre-sorted
// ascending array.

[<Fact>]
let ``percentileCont: empty array yields 0`` () =
    Assert.Equal(0M, Statistics.percentileCont [||] 0.5M)

[<Fact>]
let ``percentileCont: singleton is that value at any p`` () =
    Assert.Equal(7M, Statistics.percentileCont [| 7M |] 0.0M)
    Assert.Equal(7M, Statistics.percentileCont [| 7M |] 0.99M)

[<Fact>]
let ``percentileCont: p=0 is the min, p=1 is the max`` () =
    let xs = [| 0M; 10M; 20M; 30M; 40M |]
    Assert.Equal(0M, Statistics.percentileCont xs 0.0M)
    Assert.Equal(40M, Statistics.percentileCont xs 1.0M)

[<Fact>]
let ``percentileCont: lands on the exact element when (N-1)*p is integral`` () =
    let xs = [| 0M; 10M; 20M; 30M; 40M |]                 // N = 5
    Assert.Equal(10M, Statistics.percentileCont xs 0.25M) // h = 4*0.25 = 1 → xs.[1]
    Assert.Equal(20M, Statistics.percentileCont xs 0.50M) // h = 2       → xs.[2]
    Assert.Equal(30M, Statistics.percentileCont xs 0.75M) // h = 3       → xs.[3]

[<Fact>]
let ``percentileCont: linearly interpolates between neighbours`` () =
    // N = 2, p = 0.5 → h = 1*0.5 = 0.5, lo = 0, frac = 0.5 → 0 + 0.5*(100-0) = 50
    Assert.Equal(50M, Statistics.percentileCont [| 0M; 100M |] 0.5M)
