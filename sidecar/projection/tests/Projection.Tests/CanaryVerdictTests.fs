module Projection.Tests.CanaryVerdictTests

// align-III.3 — the R6 gate's typed verdict. The laws pin the string→type
// retirement: the wire round-trips byte-identical (NotRun writes NO token, so
// a pre-III.3 ledger is unchanged), an unknown token reads the safe direction
// (NotRun — never a false green/red), and the streak/ran predicates the gauge
// reads are exactly the three producible states.

open Xunit
open Projection.Core

[<Fact>]
let ``align-III.3: tokenOpt / ofTokenOpt round-trip every verdict; NotRun writes no token`` () =
    for v in [ CanaryVerdict.Green; CanaryVerdict.Red; CanaryVerdict.NotRun ] do
        Assert.Equal(v, CanaryVerdict.ofTokenOpt (CanaryVerdict.tokenOpt v))
    // The wire bytes: Green/Red carry their exact token; NotRun carries NONE
    // (absent/null — byte-identical to the prior `string option` None).
    Assert.Equal<string option>(Some "green", CanaryVerdict.tokenOpt CanaryVerdict.Green)
    Assert.Equal<string option>(Some "red", CanaryVerdict.tokenOpt CanaryVerdict.Red)
    Assert.Equal<string option>(None, CanaryVerdict.tokenOpt CanaryVerdict.NotRun)

[<Fact>]
let ``align-III.3: an absent or unknown token reads NotRun — the forward-compatible safe direction`` () =
    Assert.Equal(CanaryVerdict.NotRun, CanaryVerdict.ofTokenOpt None)
    // A future verdict an older reader cannot name is "no verdict I
    // recognize", never a false green/red.
    Assert.Equal(CanaryVerdict.NotRun, CanaryVerdict.ofTokenOpt (Some "aborted"))
    Assert.Equal(CanaryVerdict.NotRun, CanaryVerdict.ofTokenOpt (Some "chartreuse"))

[<Fact>]
let ``align-III.3: only Green counts toward the streak; only NotRun did not run`` () =
    Assert.True(CanaryVerdict.isGreen CanaryVerdict.Green)
    Assert.False(CanaryVerdict.isGreen CanaryVerdict.Red)
    Assert.False(CanaryVerdict.isGreen CanaryVerdict.NotRun)
    Assert.True(CanaryVerdict.ran CanaryVerdict.Green)
    Assert.True(CanaryVerdict.ran CanaryVerdict.Red)
    Assert.False(CanaryVerdict.ran CanaryVerdict.NotRun)

[<Fact>]
let ``align-III.3: display is the token, or the em-dash for NotRun`` () =
    Assert.Equal("green", CanaryVerdict.display CanaryVerdict.Green)
    Assert.Equal("red", CanaryVerdict.display CanaryVerdict.Red)
    Assert.Equal("—", CanaryVerdict.display CanaryVerdict.NotRun)
