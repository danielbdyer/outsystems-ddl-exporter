module Projection.Tests.TopologicalOrderPassTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Acyclic happy path — the synthetic fixture's only FK is Order → Customer,
// so the order is some permutation that keeps Customer before Order.
// Country has no references, so it floats to either end depending on its
// SsKey-sorted position.
// ---------------------------------------------------------------------------

[<Fact>]
let ``acyclic catalog produces Mode = Topological`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    Assert.Equal(Topological, result.Value.Mode)

[<Fact>]
let ``Order is non-empty and includes every kind exactly once`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    let order = result.Value.Order
    let allKindKeys =
        Catalog.allKinds sampleCatalog
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.Equal(allKindKeys.Count, order.Length)
    Assert.Equal<Set<SsKey>>(allKindKeys, Set.ofList order)

[<Fact>]
let ``Customer precedes Order in the output`` () =
    // Order references Customer ⇒ Customer is the parent ⇒ comes first.
    let result = TopologicalOrderPass.run sampleCatalog
    Assert.True(TopologicalOrder.precedes customerKey orderKey result.Value)

// ---------------------------------------------------------------------------
// Property: parent precedes child for every FK edge in the output.
// ---------------------------------------------------------------------------

[<Fact>]
let ``every FK edge has parent before child in the output`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    // Walk the catalog's references; for each (source, target),
    // target should precede source in Order.
    for k in Catalog.allKinds sampleCatalog do
        for r in k.References do
            Assert.True(
                TopologicalOrder.precedes r.TargetKind k.SsKey result.Value,
                sprintf "Expected %A to precede %A" r.TargetKind k.SsKey)

// ---------------------------------------------------------------------------
// Permutation invariance — the V2 contract. The output is byte-identical
// for any permutation of the input modules / kinds / references.
// (Phrased as V2's contract; the diagnostic value of the V1 finding is
// preserved in DECISIONS.md 2026-05-08.)
// ---------------------------------------------------------------------------

let private permuteRefs (k: Kind) : Kind =
    { k with References = List.rev k.References }

let private permuteKinds (m: Module) : Module =
    { m with Kinds = m.Kinds |> List.map permuteRefs |> List.rev }

let private permuteModules (c: Catalog) : Catalog =
    { Modules = c.Modules |> List.map permuteKinds |> List.rev }

[<Fact>]
let ``contract: TopologicalOrder.run is invariant under input permutation`` () =
    let direct  = TopologicalOrderPass.run sampleCatalog
    let permuted = TopologicalOrderPass.run (permuteModules sampleCatalog)
    Assert.Equal(direct.Value, permuted.Value)

[<Property>]
let ``property: output is identical across input permutations`` (reverseModules: bool) (reverseKinds: bool) (reverseRefs: bool) =
    let perturb (c: Catalog) : Catalog =
        let withModules =
            { Modules =
                c.Modules
                |> List.map (fun m ->
                    let withKinds =
                        m.Kinds
                        |> List.map (fun k ->
                            if reverseRefs then { k with References = List.rev k.References } else k)
                    if reverseKinds then { m with Kinds = List.rev withKinds }
                    else { m with Kinds = withKinds }) }
        if reverseModules then { withModules with Modules = List.rev withModules.Modules }
        else withModules
    let direct   = (TopologicalOrderPass.run sampleCatalog).Value
    let permuted = (TopologicalOrderPass.run (perturb sampleCatalog)).Value
    direct = permuted

// ---------------------------------------------------------------------------
// Determinism (T1) — same input ⇒ byte-identical output across repeats,
// trail included.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: TopologicalOrderPass is deterministic`` () =
    let r1 = TopologicalOrderPass.run sampleCatalog
    let r2 = TopologicalOrderPass.run sampleCatalog
    Assert.Equal(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

// ---------------------------------------------------------------------------
// Edges and missing-edges accounting.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Edges record every FK reference (source, target)`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    let expected =
        [ for k in Catalog.allKinds sampleCatalog do
            for r in k.References do
                yield (k.SsKey, r.TargetKind) ]
        |> List.sort
    Assert.Equal<(SsKey * SsKey) list>(expected, result.Value.Edges)

[<Fact>]
let ``no missing edges when every FK target is present`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    Assert.Empty(result.Value.MissingEdges)
    Assert.True(TopologicalOrder.isComplete result.Value)

[<Fact>]
let ``MissingEdges are recorded when an FK target is absent`` () =
    // Drop Customer from the catalog; Order's FK to Customer becomes a
    // missing edge.
    let withoutCustomer =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.filter (fun k -> k.SsKey <> customerKey) }) }
    let result = TopologicalOrderPass.run withoutCustomer
    let missing = result.Value.MissingEdges
    Assert.Equal(1, missing.Length)
    Assert.Equal((orderKey, customerKey), missing.[0])
    Assert.False(TopologicalOrder.isComplete result.Value)

// ---------------------------------------------------------------------------
// Cycle behavior (commit-4 placeholder) — when a cycle is present, fall
// back to alphabetical with a generic diagnostic. SCC enumeration is
// the next commit's job.
// ---------------------------------------------------------------------------

let private addReference (sourceKey: SsKey) (targetKey: SsKey) (refKey: SsKey) (refName: string) (sourceAttrKey: SsKey) (c: Catalog) : Catalog =
    { Modules =
        c.Modules
        |> List.map (fun m ->
            { m with
                Kinds =
                    m.Kinds
                    |> List.map (fun k ->
                        if k.SsKey = sourceKey then
                            let newRef : Reference =
                                { SsKey           = refKey
                                  Name            = Name.create refName |> Result.value
                                  SourceAttribute = sourceAttrKey
                                  TargetKind      = targetKey
                                  OnDelete        = NoAction }
                            { k with References = newRef :: k.References }
                        else k) }) }

[<Fact>]
let ``cycle: Mode is Alphabetical when input contains a cycle`` () =
    // Add a reverse reference Customer → Order so we have a 2-cycle.
    let backRefKey = SsKey.original "OS_REF_Customer_Order_back" |> Result.value
    let cyclic =
        sampleCatalog
        |> addReference customerKey orderKey backRefKey "Order_back" customerNameKey
    let result = TopologicalOrderPass.run cyclic
    Assert.Equal(Alphabetical, result.Value.Mode)

[<Fact>]
let ``cycle: every kind still appears in Order under alphabetical fallback`` () =
    let backRefKey = SsKey.original "OS_REF_Customer_Order_back" |> Result.value
    let cyclic =
        sampleCatalog
        |> addReference customerKey orderKey backRefKey "Order_back" customerNameKey
    let result = TopologicalOrderPass.run cyclic
    let allKindKeys =
        Catalog.allKinds cyclic
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.Equal<Set<SsKey>>(allKindKeys, Set.ofList result.Value.Order)

[<Fact>]
let ``cycle: at least one CycleDiagnostic is emitted`` () =
    let backRefKey = SsKey.original "OS_REF_Customer_Order_back" |> Result.value
    let cyclic =
        sampleCatalog
        |> addReference customerKey orderKey backRefKey "Order_back" customerNameKey
    let result = TopologicalOrderPass.run cyclic
    Assert.NotEmpty(result.Value.Cycles)

// ---------------------------------------------------------------------------
// Lineage discipline — A23 + A25.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A25: emits one Touched event per kind scanned`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    let touchedEvents = result.Trail |> List.filter (fun e -> e.TransformKind = Touched)
    let kindCount = (Catalog.allKinds sampleCatalog).Length
    Assert.Equal(kindCount, touchedEvents.Length)

[<Fact>]
let ``A23: events carry pass version and name`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    Assert.All(result.Trail, fun e ->
        Assert.Equal(TopologicalOrderPass.version, e.PassVersion)
        Assert.Equal("topologicalOrder", e.PassName))

// ---------------------------------------------------------------------------
// Empty catalog edge case.
// ---------------------------------------------------------------------------

[<Fact>]
let ``empty catalog produces empty TopologicalOrder`` () =
    let empty : Catalog = { Modules = [] }
    let result = TopologicalOrderPass.run empty
    Assert.Equal(Topological, result.Value.Mode)
    Assert.Empty(result.Value.Order)
    Assert.Empty(result.Value.Edges)
    Assert.Empty(result.Value.MissingEdges)
    Assert.Empty(result.Value.Cycles)

// ---------------------------------------------------------------------------
// Composition with symmetric closure — after symmetric closure adds an
// inverse, the topological order may itself become cyclic (because
// inverses introduce circular FKs). This is the correct V2 behavior:
// symmetric closure is for surface navigation, not for FK-safe data
// emission. Schema emitters apply alphabetical ordering per A33;
// data emitters consume the catalog WITHOUT symmetric closure.
// ---------------------------------------------------------------------------

[<Fact>]
let ``post-symmetric-closure catalog is cyclic; emitters compose correctly`` () =
    let withInverses =
        sampleCatalog
        |> SymmetricClosure.run
        |> fun lineage -> lineage.Value
    let result = TopologicalOrderPass.run withInverses
    // Symmetric closure introduced an inverse on Customer → Order
    // alongside the original Order → Customer; the FK graph is now cyclic.
    Assert.Equal(Alphabetical, result.Value.Mode)
