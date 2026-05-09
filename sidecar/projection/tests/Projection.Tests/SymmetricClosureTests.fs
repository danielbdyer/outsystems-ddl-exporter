module Projection.Tests.SymmetricClosureTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private inverseSsKey (originalRefKey: SsKey) : SsKey =
    SsKey.derivedFrom originalRefKey SymmetricClosure.inverseReason |> Result.value

let private allReferences (c: Catalog) : Reference list =
    Catalog.allKinds c
    |> List.collect (fun k -> k.References)

// ---------------------------------------------------------------------------
// Inverse construction — for each directional reference, an inverse exists
// on the target kind with a Derived(_, "inverse") SsKey.
// ---------------------------------------------------------------------------

[<Fact>]
let ``inverse is added on the target kind for each directional reference`` () =
    let result = SymmetricClosure.run sampleCatalog
    // Synthetic fixture: Order.Customer is the only reference. Customer
    // (the target) should now carry one inverse pointing back at Order.
    let customerOut = Catalog.tryFindKind customerKey result.Value |> Option.get
    Assert.Equal(1, customerOut.References.Length)
    let inverse = customerOut.References |> List.exactlyOne
    Assert.Equal(inverseSsKey orderRefToCustomer, inverse.SsKey)
    Assert.Equal(orderKey, inverse.TargetKind)

[<Fact>]
let ``inverse SsKey is Derived with reason "inverse"`` () =
    let result = SymmetricClosure.run sampleCatalog
    let inverse =
        Catalog.tryFindKind customerKey result.Value
        |> Option.get
        |> fun k -> k.References
        |> List.exactlyOne
    match inverse.SsKey with
    | DerivedFrom (_, reason) -> Assert.Equal("inverse", reason)
    | other -> Assert.Fail(sprintf "Expected DerivedFrom inverse, got %A" other)

[<Fact>]
let ``A5: inverse SsKey traces back to the original reference's root`` () =
    let result = SymmetricClosure.run sampleCatalog
    let inverse =
        Catalog.tryFindKind customerKey result.Value
        |> Option.get
        |> fun k -> k.References
        |> List.exactlyOne
    Assert.Equal(SsKey.rootOriginal orderRefToCustomer,
                 SsKey.rootOriginal inverse.SsKey)
    Assert.Equal<string list>([ "inverse" ], SsKey.derivationReasons inverse.SsKey)

[<Fact>]
let ``inverse Name is the source kind's Name`` () =
    let result = SymmetricClosure.run sampleCatalog
    let inverse =
        Catalog.tryFindKind customerKey result.Value
        |> Option.get
        |> fun k -> k.References
        |> List.exactlyOne
    // The source kind of Order.Customer is Order.
    Assert.Equal(order.Name, inverse.Name)

[<Fact>]
let ``inverse SourceAttribute is the target kind's primary key`` () =
    let result = SymmetricClosure.run sampleCatalog
    let inverse =
        Catalog.tryFindKind customerKey result.Value
        |> Option.get
        |> fun k -> k.References
        |> List.exactlyOne
    // The target of Order.Customer is Customer; its PK is customerIdAttrKey.
    Assert.Equal(customerIdAttrKey, inverse.SourceAttribute)

// ---------------------------------------------------------------------------
// Idempotence — running twice does not double-add inverses (A5: derived
// keys are deterministic, so the second run sees the inverse already
// present and skips).
// ---------------------------------------------------------------------------

[<Fact>]
let ``contract: idempotent — running twice equals running once`` () =
    let once  = (SymmetricClosure.run sampleCatalog).Value
    let twice = (SymmetricClosure.run once).Value
    Assert.Equal(once, twice)

[<Fact>]
let ``idempotent: second run emits no Created events`` () =
    let once = SymmetricClosure.run sampleCatalog
    let twice = SymmetricClosure.run once.Value
    let createdInTwice =
        twice.Trail
        |> List.filter (fun e -> e.TransformKind = Created)
    Assert.Empty(createdInTwice)

// ---------------------------------------------------------------------------
// Identity preservation (A3, A4) — original SsKeys, names, modality marks,
// physical realizations are byte-identical on every kind that existed in
// the input.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A4: pass neither invents nor drops any Original kind SsKey`` () =
    let inputKinds =
        Catalog.allKinds sampleCatalog
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    let outputKinds =
        Catalog.allKinds (SymmetricClosure.run sampleCatalog).Value
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.Equal<Set<SsKey>>(inputKinds, outputKinds)

[<Fact>]
let ``A3: original references are preserved unchanged`` () =
    let result = SymmetricClosure.run sampleCatalog
    let outOrder = Catalog.tryFindKind orderKey result.Value |> Option.get
    // Order's original reference to Customer is still there, byte-identical.
    Assert.Contains(order.References |> List.exactlyOne, outOrder.References)

// ---------------------------------------------------------------------------
// Determinism (T1) — same input ⇒ same output and trail.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: SymmetricClosure is deterministic`` () =
    let r1 = SymmetricClosure.run sampleCatalog
    let r2 = SymmetricClosure.run sampleCatalog
    Assert.Equal(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

// ---------------------------------------------------------------------------
// Lineage discipline — Created events for new inverses, with pass version.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A23 + A25: Created events are emitted with pass version + name`` () =
    let result = SymmetricClosure.run sampleCatalog
    let createdEvents = result.Trail |> List.filter (fun e -> e.TransformKind = Created)
    Assert.Equal(1, createdEvents.Length)
    Assert.All(createdEvents, fun e ->
        Assert.Equal(SymmetricClosure.version, e.PassVersion)
        Assert.Equal("symmetricClosure", e.PassName))

[<Fact>]
let ``Created event SsKey matches the new inverse reference's SsKey`` () =
    let result = SymmetricClosure.run sampleCatalog
    let createdEvent =
        result.Trail
        |> List.find (fun e -> e.TransformKind = Created)
    let inverse =
        Catalog.tryFindKind customerKey result.Value
        |> Option.get
        |> fun k -> k.References
        |> List.exactlyOne
    Assert.Equal(inverse.SsKey, createdEvent.SsKey)

// ---------------------------------------------------------------------------
// Skipped cases — when the target lacks a primary key, no inverse is
// added and an Annotated event records the skip.
// ---------------------------------------------------------------------------

[<Fact>]
let ``skip when target has no primary key`` () =
    // Strip Customer's PK marker; Order.Customer's inverse should be skipped.
    let strippedCustomer =
        { customer with
            Attributes =
                customer.Attributes
                |> List.map (fun a -> { a with IsPrimaryKey = false }) }
    let strippedCatalog =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey then strippedCustomer else k) }) }
    let result = SymmetricClosure.run strippedCatalog
    // No inverse added.
    let outCustomer = Catalog.tryFindKind customerKey result.Value |> Option.get
    Assert.Empty(outCustomer.References)
    // Skip event recorded — chapter-3.6 slice-γ: typed
    // `SymmetricClosureSkipReason.TargetHasNoPrimaryKey` payload
    // replaces the prior "skipped: target has no primary key" prose.
    let skipEvents =
        result.Trail
        |> List.filter (fun e ->
            match e.TransformKind with
            | Annotated (ClosureSkipped TargetHasNoPrimaryKey) -> true
            | _ -> false)
    Assert.Equal(1, skipEvents.Length)

// ---------------------------------------------------------------------------
// Property: SymmetricClosure is invariant under composition with itself.
// (Idempotence in property form.)
// ---------------------------------------------------------------------------

[<Property>]
let ``property: SymmetricClosure is idempotent on the synthetic catalog`` () =
    let once  = (SymmetricClosure.run sampleCatalog).Value
    let twice = (SymmetricClosure.run once).Value
    once = twice

// ---------------------------------------------------------------------------
// Catalog reference count grows by exactly the number of (resolvable +
// PK-bearing) original references.
// ---------------------------------------------------------------------------

[<Fact>]
let ``reference count grows by exactly the number of inverses added`` () =
    let inputRefCount = (allReferences sampleCatalog).Length
    let result = SymmetricClosure.run sampleCatalog
    let outputRefCount = (allReferences result.Value).Length
    Assert.Equal(inputRefCount + 1, outputRefCount)

// ---------------------------------------------------------------------------
// Composition with canonicalizeIdentity — the inverses end up in
// canonical order regardless of which pass runs first.
// ---------------------------------------------------------------------------

[<Fact>]
let ``composes with canonicalizeIdentity in either order — values converge`` () =
    let aThenB =
        sampleCatalog
        |> SymmetricClosure.run
        |> Lineage.bind CanonicalizeIdentity.run
    let bThenA =
        sampleCatalog
        |> CanonicalizeIdentity.run
        |> Lineage.bind SymmetricClosure.run
        |> Lineage.bind CanonicalizeIdentity.run
    Assert.Equal(aThenB.Value, bThenA.Value)
