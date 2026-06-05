module Projection.Tests.ThemeTests

open Xunit
open Projection.Cli

/// Polish — the design-system primitives (REPORTING_HORIZON).

[<Fact>]
let ``Theme.meter fills proportionally and clamps to total`` () =
    Assert.Equal("▇▇▇▇▇▇▇░░░", Theme.meter 7 10)
    Assert.Equal("▇▇▇▇▇▇▇▇▇▇", Theme.meter 15 10)   // over-fill clamps
    Assert.Equal("░░░", Theme.meter 0 3)

[<Fact>]
let ``Theme.sparkline maps min..max across the bar ramp`` () =
    Assert.Equal("▁█", Theme.sparkline [ 0; 7 ])
    Assert.Equal("", Theme.sparkline [])
    Assert.Equal("▁▁▁", Theme.sparkline [ 5; 5; 5 ])   // flat -> lowest bar

[<Fact>]
let ``Theme.canaryDots renders green filled + red cross, newest last`` () =
    Assert.Equal("●●✕●", Theme.canaryDots [ "green"; "green"; "red"; "green" ])
