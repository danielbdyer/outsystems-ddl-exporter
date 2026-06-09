module Projection.Tests.ComparisonTests

open Xunit
open Projection.Core
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
let ``Comparison: render projects a diff onto the View substrate (the count visible in json)`` () =
    match Comparison.summary Comparison.catalog sampleCatalog sampleCatalog with
    | Ok v ->
        let j = (View.toJson v).ToJsonString()
        Assert.Contains("changes", j)        // the panel title "changes"
        Assert.Contains("total changes", j)  // the count, in plain words (never `norm`)
    | Error e -> Assert.Fail e

// --- statement-first surface (INSTRUMENT slice 1) --------------------------
// Discriminating predicate: the lead verdict READS the change — a destructive
// change leads amber ("review first"), an additive / no-op change leads calm.
// A naive renderer shows the same panel with no verdict at all.

let private emptyCatalog = IRBuilders.mkCatalog []

[<Fact>]
let ``Comparison statement: an identical pair leads with a calm no-differences verdict`` () =
    match Comparison.catalog.Between sampleCatalog sampleCatalog with
    | Ok d ->
        match Comparison.catalogStatement d with
        | View.Hero(View.Ok, text) -> Assert.Contains("No differences", text)
        | other -> Assert.Fail(sprintf "expected a calm Ok hero, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison statement: a change with removals leads amber with the true verb — review before applying`` () =
    match Comparison.catalog.Between sampleCatalog emptyCatalog with
    | Ok d ->
        match Comparison.catalogStatement d with
        | View.Hero(View.Warn, text) -> Assert.Contains("drops", text)
        | other -> Assert.Fail(sprintf "expected a Warn hero, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison statement: an additive change leads calm — no removals`` () =
    match Comparison.catalog.Between emptyCatalog sampleCatalog with
    | Ok d ->
        match Comparison.catalogStatement d with
        | View.Hero(View.Ok, text) -> Assert.Contains("no removals", text)
        | other -> Assert.Fail(sprintf "expected a calm Ok hero, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison: renderCatalogChange leads with the statement, then the substantiation`` () =
    match Comparison.catalog.Between sampleCatalog sampleCatalog with
    | Ok d ->
        match Comparison.renderCatalogChange d with
        | View.Doc (View.Hero _ :: _) -> ()        // statement first, substantiation beneath
        | other -> Assert.Fail(sprintf "expected a Doc led by a Hero, got %A" other)
    | Error e -> Assert.Fail e

// --- move-typed lanes (INSTRUMENT slice 2) ---------------------------------
// Discriminating predicate: changes group into move-lanes, each badged by
// reversibility — a remove lane is Bad (destroys structure), an add lane is Ok
// (safe). A naive renderer shows one undifferentiated list with no move/badge.

[<Fact>]
let ``Comparison lanes: a removed kind lands in a remove lane badged Bad`` () =
    match Comparison.catalog.Between sampleCatalog emptyCatalog with
    | Ok d ->
        let remove =
            Comparison.renderCatalogLanes d
            |> List.tryPick (function View.Lane(_, "remove", st, items) -> Some(st, items) | _ -> None)
        match remove with
        | Some (View.Bad, items) -> Assert.NotEmpty items
        | other -> Assert.Fail(sprintf "expected a Bad remove lane, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison lanes: an added kind lands in an add lane badged Ok`` () =
    match Comparison.catalog.Between emptyCatalog sampleCatalog with
    | Ok d ->
        let add =
            Comparison.renderCatalogLanes d
            |> List.tryPick (function View.Lane(_, "add", st, items) -> Some(st, items) | _ -> None)
        match add with
        | Some (View.Ok, items) -> Assert.NotEmpty items
        | other -> Assert.Fail(sprintf "expected an Ok add lane, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison: renderCatalogChange dig carries the move lanes`` () =
    match Comparison.catalog.Between sampleCatalog emptyCatalog with
    | Ok d ->
        match Comparison.renderCatalogChange d with
        | View.Doc blocks ->
            Assert.True(
                blocks |> List.exists (function View.Lane(_, "remove", _, _) -> true | _ -> false),
                "expected a remove lane in the substantiation")
        | other -> Assert.Fail(sprintf "expected a Doc, got %A" other)
    | Error e -> Assert.Fail e

/// Reshape fixture (slice 2b): a Customer.Name facet change, mirroring the
/// CatalogDiff attribute-Changed fixture, built from the shared Fixtures.
let private reshapeTarget (f: Attribute -> Attribute) : Catalog =
    let customer' =
        { customer with
            Attributes = customer.Attributes |> List.map (fun a -> if a.SsKey = customerNameKey then f a else a) }
    Catalog.create [ { salesModule with Kinds = [ customer'; order; country ] } ] [] |> Result.value

[<Fact>]
let ``Comparison lanes: a changed attribute facet lands in a reshape lane badged Warn`` () =
    let target = reshapeTarget (fun a -> { a with Type = Integer })
    match Comparison.catalog.Between sampleCatalog target with
    | Ok d ->
        let reshape =
            Comparison.renderCatalogLanes d
            |> List.tryPick (function View.Lane(_, "reshape", st, items) -> Some(st, items) | _ -> None)
        match reshape with
        | Some (View.Warn, items) -> Assert.NotEmpty items
        | other -> Assert.Fail(sprintf "expected a Warn reshape lane, got %A" other)
    | Error e -> Assert.Fail e
