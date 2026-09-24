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
let ``Theme.spinner cycles through its frames and is total over any phase (#20)`` () =
    // adjacent phases differ (the spinner visibly advances)
    Assert.NotEqual<string>(Theme.spinner 0, Theme.spinner 1)
    Assert.NotEqual<string>(Theme.spinner 0, Theme.spinner 5)
    // it wraps — 10 frames, so phase 10 returns to frame 0
    Assert.Equal(Theme.spinner 0, Theme.spinner 10)
    Assert.Equal(Theme.spinner 3, Theme.spinner 13)
    // total: a frame for any phase (zero, large), never empty / throwing
    Assert.False(System.String.IsNullOrEmpty(Theme.spinner 0))
    Assert.False(System.String.IsNullOrEmpty(Theme.spinner 999999))

[<Fact>]
let ``Theme.canaryDots renders green filled + red cross, newest last`` () =
    Assert.Equal("●●✕●", Theme.canaryDots [ "green"; "green"; "red"; "green" ])

[<Fact>]
let ``Theme.timeline strips dots with a NO_COLOR-safe present marker`` () =
    // green ● / red ✕, newest last; the present cell is flagged with ▸.
    Assert.Equal("●✕●▸", Theme.timeline [ "green"; "red"; "green" ] (Some 2))
    Assert.Equal("●✕●", Theme.timeline [ "green"; "red"; "green" ] None)
    Assert.Equal("●✕●", Theme.timeline [ "green"; "red"; "green" ] (Some 9))   // out-of-range → bare strip
