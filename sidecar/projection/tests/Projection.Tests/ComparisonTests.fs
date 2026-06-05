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

// --- essence-first surface (INSTRUMENT slice 1) ----------------------------
// Discriminating predicate: the lead verdict READS the change — a destructive
// change leads amber ("review first"), an additive / no-op change leads calm.
// A naive renderer shows the same panel with no verdict at all.

let private emptyCatalog = IRBuilders.mkCatalog []

[<Fact>]
let ``Comparison essence: an identical pair leads with a calm identical verdict`` () =
    match Comparison.catalog.Between sampleCatalog sampleCatalog with
    | Ok d ->
        match Comparison.catalogEssence d with
        | View.Hero(View.Ok, text) -> Assert.Contains("identical", text)
        | other -> Assert.Fail(sprintf "expected a calm Ok hero, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison essence: a destructive change leads amber — review first`` () =
    match Comparison.catalog.Between sampleCatalog emptyCatalog with
    | Ok d ->
        match Comparison.catalogEssence d with
        | View.Hero(View.Warn, text) -> Assert.Contains("destroy", text)
        | other -> Assert.Fail(sprintf "expected a Warn hero, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison essence: an additive change leads calm — nothing destroyed`` () =
    match Comparison.catalog.Between emptyCatalog sampleCatalog with
    | Ok d ->
        match Comparison.catalogEssence d with
        | View.Hero(View.Ok, text) -> Assert.Contains("nothing destroyed", text)
        | other -> Assert.Fail(sprintf "expected a calm Ok hero, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison: renderCatalogChange leads with the essence, then the dig`` () =
    match Comparison.catalog.Between sampleCatalog sampleCatalog with
    | Ok d ->
        match Comparison.renderCatalogChange d with
        | View.Doc (View.Hero _ :: _) -> ()        // essence first, dig beneath
        | other -> Assert.Fail(sprintf "expected a Doc led by a Hero, got %A" other)
    | Error e -> Assert.Fail e
