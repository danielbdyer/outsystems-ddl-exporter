module Projection.Tests.ComparisonTests

open Xunit
open Projection.Cli
open Projection.Tests.Fixtures

/// Masterful base #3 — comparison as a capability. The discriminating
/// predicate lives in the type: `Apply` is present iff the delta is a torsor
/// element (replayable), absent iff it is a lossy quotient.

[<Fact>]
let ``Comparison: catalog carries the torsor action; physicalSchema is a quotient`` () =
    Assert.True(Option.isSome Comparison.catalog.Apply)          // torsor — replayable
    Assert.True(Option.isNone Comparison.physicalSchema.Apply)   // lossy quotient

[<Fact>]
let ``Comparison: Between a a is the identity delta (empty)`` () =
    match Comparison.catalog.Between sampleCatalog sampleCatalog with
    | Ok d    -> Assert.True(Comparison.catalog.IsEmpty d)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison: Apply replays the delta through the abstraction (Weyl flows through)`` () =
    // The full mutated-target Weyl law is proven in CatalogDiffTests; here we
    // assert it flows through the capability's Apply: apply (between a a) a
    // leaves a unchanged (the re-observed delta is empty).
    match Comparison.catalog.Between sampleCatalog sampleCatalog, Comparison.catalog.Apply with
    | Ok d, Some apply ->
        let result = apply d sampleCatalog
        match Comparison.catalog.Between sampleCatalog result with
        | Ok d2   -> Assert.True(Comparison.catalog.IsEmpty d2)
        | Error e -> Assert.Fail e
    | _ -> Assert.Fail "expected a delta and an Apply"

[<Fact>]
let ``Comparison: render projects a diff onto the View substrate (norm visible in json)`` () =
    match Comparison.summary Comparison.catalog sampleCatalog sampleCatalog with
    | Ok v ->
        let j = (View.toJson v).ToJsonString()
        Assert.Contains("catalog", j)   // the panel title "catalog Δ"
        Assert.Contains("norm", j)
    | Error e -> Assert.Fail e
