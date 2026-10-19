module Projection.Tests.EmissionModeTests

open Xunit
open Projection.Core

// AC-D10 (type leg) — the wipe-and-load fork, RESOLVED to an explicit named
// EmissionMode. This slice names the mode and its cost in the type system; the
// live TRUNCATE+reload realization is Wave 3. The witnesses pin: the default is
// Incremental; the two modes are distinct; the destructive/CDC-cost facts are
// carried by the type, not a boolean flag.

[<Fact>]
let ``AC-D10: the default emission mode is Incremental (wipe-and-load is opt-in)`` () =
    Assert.Equal(Incremental, EmissionMode.defaultMode)

[<Fact>]
let ``AC-D10: Incremental and WipeAndLoad are distinct in the type system`` () =
    Assert.NotEqual(Incremental, WipeAndLoad)

[<Fact>]
let ``AC-D10: only WipeAndLoad is destructive`` () =
    Assert.False(EmissionMode.isDestructive Incremental)
    Assert.True(EmissionMode.isDestructive WipeAndLoad)

[<Fact>]
let ``AC-D10: the CDC cost factor is 0 for Incremental and 2 per row for WipeAndLoad`` () =
    // Incremental: CDC-minimal — an idempotent redeploy captures nothing.
    Assert.Equal(0, EmissionMode.cdcCostFactorPerRow Incremental)
    // WipeAndLoad: 2·|table| — a delete-image + insert-image per row.
    Assert.Equal(2, EmissionMode.cdcCostFactorPerRow WipeAndLoad)

[<Fact>]
let ``AC-D10: tokens round-trip and unknown tokens are rejected`` () =
    Assert.Equal(Ok Incremental, EmissionMode.ofToken (EmissionMode.toToken Incremental))
    Assert.Equal(Ok WipeAndLoad, EmissionMode.ofToken (EmissionMode.toToken WipeAndLoad))
    Assert.Equal(Ok WipeAndLoad, EmissionMode.ofToken "wipeandload")
    match EmissionMode.ofToken "truncate-everything" with
    | Error _ -> ()
    | Ok m -> Assert.Fail(sprintf "unknown token must be rejected, got %A" m)
