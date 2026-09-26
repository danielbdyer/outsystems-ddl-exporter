module Projection.Tests.NamingMorphismTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — `NamingMorphism.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<Catalog>>`. This per-file shim restores the
// `Lineage<Catalog>` shape so existing assertions keep reading.
let private nmRun (morphism: NamingMorphism.Morphism) (catalog: Catalog) : Lineage<Catalog> =
    (NamingMorphism.registered morphism).Run catalog |> Lineage.map (fun d -> d.Value)

// ---------------------------------------------------------------------------
// A15 (load-bearing): identity is untouched. For every named node, the
// SsKey before and after the pass is byte-identical regardless of the
// morphism. This is the property that any naming-policy test must defend
// or the algebra is broken.
// ---------------------------------------------------------------------------

let private collectKeys (c: Catalog) : Set<SsKey> =
    let keys = ResizeArray<SsKey>()
    for m in c.Modules do
        keys.Add(m.SsKey)
        for k in m.Kinds do
            keys.Add(k.SsKey)
            for a in k.Attributes do keys.Add(a.SsKey)
            for r in k.References do keys.Add(r.SsKey)
    Set.ofSeq keys

[<Fact>]
let ``A15: toUpper morphism preserves every SsKey in the catalog`` () =
    let result = nmRun NamingMorphism.toUpper sampleCatalog
    Assert.Equal<Set<SsKey>>(collectKeys sampleCatalog, collectKeys result.Value)

[<Fact>]
let ``A15: withPrefix morphism preserves every SsKey in the catalog`` () =
    let result = nmRun (NamingMorphism.withPrefix "X_") sampleCatalog
    Assert.Equal<Set<SsKey>>(collectKeys sampleCatalog, collectKeys result.Value)

[<Property>]
let ``A15: any morphism preserves the catalog's SsKey set`` (s: NonEmptyString) =
    if System.String.IsNullOrWhiteSpace s.Get then true
    else
        let morphism : NamingMorphism.Morphism = fun (Name n) -> Name (s.Get + n)
        let result = nmRun morphism sampleCatalog
        collectKeys sampleCatalog = collectKeys result.Value

// ---------------------------------------------------------------------------
// Identity-keyed lookup survives any morphism (A4 + A15 together).
// ---------------------------------------------------------------------------

[<Fact>]
let ``A4 + A15: Catalog.tryFindKind survives a uppercase rename`` () =
    let result = nmRun NamingMorphism.toUpper sampleCatalog
    let found = Catalog.tryFindKind customerKey result.Value |> Option.get
    // SsKey is preserved; Name is uppercased.
    Assert.Equal(customerKey, found.SsKey)
    Assert.Equal("CUSTOMER", Name.value found.Name)

[<Fact>]
let ``A4 + A15: FK target SsKey survives a rename`` () =
    let result = nmRun NamingMorphism.toUpper sampleCatalog
    let order = Catalog.tryFindKind orderKey result.Value |> Option.get
    let ref = order.References |> List.exactlyOne
    Assert.Equal(customerKey, ref.TargetKind)

// ---------------------------------------------------------------------------
// Identity morphism is the no-op. Output catalog equals input; trail empty.
// ---------------------------------------------------------------------------

[<Fact>]
let ``identity morphism produces the input catalog unchanged`` () =
    let result = nmRun NamingMorphism.identity sampleCatalog
    Assert.Equal(sampleCatalog, result.Value)
    Assert.Empty(result.Trail)

[<Fact>]
let ``a no-op morphism (any morphism returning same name) produces empty trail`` () =
    let noop : NamingMorphism.Morphism = id
    let result = nmRun noop sampleCatalog
    Assert.Empty(result.Trail)

// ---------------------------------------------------------------------------
// Functor / homomorphism law: morphism composition equals run-twice.
//
//   run (g ∘ f) catalog .Value  =  catalog |> run f |> bind (run g) |> .Value
// ---------------------------------------------------------------------------

[<Fact>]
let ``composition law: run (f >> g) = (run f) bind (run g) on values`` () =
    let f = NamingMorphism.withPrefix "X_"
    let g = NamingMorphism.withSuffix "_v2"
    let composed = f >> g
    let directValue = (nmRun composed sampleCatalog).Value
    let stepwiseValue =
        sampleCatalog
        |> nmRun f
        |> Lineage.bind (nmRun g)
        |> fun lineage -> lineage.Value
    Assert.Equal(directValue, stepwiseValue)

// ---------------------------------------------------------------------------
// Lineage events are Renamed and only emitted when the name actually
// changed. A morphism that's a no-op on every name produces an empty
// trail; a non-trivial morphism produces one event per renamed node.
// ---------------------------------------------------------------------------

[<Fact>]
let ``every emitted event is a Renamed event`` () =
    let result = nmRun NamingMorphism.toUpper sampleCatalog
    Assert.NotEmpty(result.Trail)
    Assert.All(result.Trail, fun e ->
        Assert.Equal(Renamed, e.TransformKind))

[<Fact>]
let ``A23: events carry the pass version and name`` () =
    let result = nmRun NamingMorphism.toUpper sampleCatalog
    Assert.All(result.Trail, fun e ->
        Assert.Equal(NamingMorphism.version, e.PassVersion)
        Assert.Equal("namingMorphism", e.PassName))

[<Fact>]
let ``a morphism that only changes some names emits events only for those names`` () =
    // Morphism: rename only "Customer" to "Buyer"; everything else passes
    // through. Two named nodes carry the name "Customer" in the fixture
    // — the Customer kind itself, and Order's "Customer" reference —
    // so two events fire, both keyed by their respective SsKeys.
    let buyerName    = Name.create "Buyer"    |> Result.value
    let customerName = Name.create "Customer" |> Result.value
    let m : NamingMorphism.Morphism =
        fun n -> if n = customerName then buyerName else n
    let result = nmRun m sampleCatalog
    Assert.Equal(2, result.Trail.Length)
    let renamedKeys = result.Trail |> List.map (fun e -> e.SsKey) |> Set.ofList
    Assert.Equal<Set<SsKey>>(Set.ofList [ customerKey; orderRefToCustomer ], renamedKeys)

// ---------------------------------------------------------------------------
// Names actually transform — the morphism is applied, not silently
// skipped.
// ---------------------------------------------------------------------------

[<Fact>]
let ``toUpper transforms every name`` () =
    let result = nmRun NamingMorphism.toUpper sampleCatalog
    let allNames =
        Catalog.allKinds result.Value
        |> List.collect (fun k ->
            Name.value k.Name
            :: (k.Attributes |> List.map (fun a -> Name.value a.Name)))
    for n in allNames do
        Assert.Equal(n.ToUpperInvariant(), n)

[<Fact>]
let ``withPrefix prepends to every name`` () =
    let prefix = "X_"
    let result = nmRun (NamingMorphism.withPrefix prefix) sampleCatalog
    let kindNames = Catalog.allKinds result.Value |> List.map (fun k -> Name.value k.Name)
    for n in kindNames do
        Assert.StartsWith(prefix, n)

// ---------------------------------------------------------------------------
// Determinism (T1) — same morphism + same catalog → byte-identical output
// across repeat invocations.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: namingMorphism is deterministic`` () =
    let r1 = nmRun NamingMorphism.toUpper sampleCatalog
    let r2 = nmRun NamingMorphism.toUpper sampleCatalog
    Assert.Equal(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

// ---------------------------------------------------------------------------
// Catalog structure preserved — same module count, same kind count per
// module, same attribute count per kind, same reference count per kind.
// ---------------------------------------------------------------------------

[<Fact>]
let ``namingMorphism preserves catalog cardinality`` () =
    let result = nmRun NamingMorphism.toUpper sampleCatalog
    Assert.Equal(sampleCatalog.Modules.Length, result.Value.Modules.Length)
    let beforeKinds = Catalog.allKinds sampleCatalog
    let afterKinds  = Catalog.allKinds result.Value
    Assert.Equal(beforeKinds.Length, afterKinds.Length)
    for (before, after) in List.zip beforeKinds afterKinds do
        Assert.Equal(before.Attributes.Length, after.Attributes.Length)
        Assert.Equal(before.References.Length, after.References.Length)
        Assert.Equal(before.Modality.Length,   after.Modality.Length)
