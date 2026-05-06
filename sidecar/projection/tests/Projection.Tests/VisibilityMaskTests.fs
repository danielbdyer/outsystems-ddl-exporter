module Projection.Tests.VisibilityMaskTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Identity behavior — empty mask hides nothing.
// ---------------------------------------------------------------------------

[<Fact>]
let ``empty mask is the identity transformation`` () =
    let result = VisibilityMask.run VisibilityMask.empty sampleCatalog
    Assert.Equal(sampleCatalog, result.Value)
    Assert.Empty(result.Trail)

// ---------------------------------------------------------------------------
// Hide by origin — every OsNative kind is removed; non-matching kinds
// (none in the fixture) survive.
// ---------------------------------------------------------------------------

[<Fact>]
let ``hideOrigin removes every kind with that origin`` () =
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideOrigin OsNative ] }
    let result = VisibilityMask.run mask sampleCatalog
    Assert.Empty(Catalog.allKinds result.Value)

[<Fact>]
let ``hideOrigin emits one Removed event per removed kind`` () =
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideOrigin OsNative ] }
    let result = VisibilityMask.run mask sampleCatalog
    Assert.Equal(3, result.Trail.Length)
    Assert.All(result.Trail, fun e ->
        match e.TransformKind with
        | Removed reason ->
            Assert.Equal("origin=OsNative", reason)
        | other ->
            Assert.Fail(sprintf "Expected Removed, got %A" other))

// ---------------------------------------------------------------------------
// Hide by explicit SsKey list. Lineage events name the rule that fired.
// ---------------------------------------------------------------------------

[<Fact>]
let ``hideKeys removes only the named kinds`` () =
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideKeys [ countryKey ] ] }
    let result = VisibilityMask.run mask sampleCatalog
    let surviving =
        Catalog.allKinds result.Value
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.Equal<Set<SsKey>>(Set.ofList [ customerKey; orderKey ], surviving)

[<Fact>]
let ``hideKeys: lineage event names the explicit-key-list rule`` () =
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideKeys [ countryKey ] ] }
    let result = VisibilityMask.run mask sampleCatalog
    let event = result.Trail |> List.exactlyOne
    Assert.Equal(countryKey, event.SsKey)
    match event.TransformKind with
    | Removed reason -> Assert.Equal("explicit-key-list", reason)
    | other -> Assert.Fail(sprintf "Expected Removed, got %A" other)

// ---------------------------------------------------------------------------
// Hide by modality — drop the static kind; surviving kinds keep their
// references intact.
// ---------------------------------------------------------------------------

[<Fact>]
let ``hideModality removes the static kind from the synthetic fixture`` () =
    // The fixture's only Static modality is on Country with three populations.
    let staticMark = Static countryPopulations
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideModality staticMark ] }
    let result = VisibilityMask.run mask sampleCatalog
    Assert.Equal(None, Catalog.tryFindKind countryKey result.Value)
    Assert.True(Option.isSome (Catalog.tryFindKind customerKey result.Value))
    Assert.True(Option.isSome (Catalog.tryFindKind orderKey result.Value))

// ---------------------------------------------------------------------------
// Predicate ordering — first matching predicate wins for lineage
// attribution. Deterministic when a kind would match multiple predicates.
// ---------------------------------------------------------------------------

[<Fact>]
let ``first matching predicate wins for lineage attribution`` () =
    // Both predicates would match Country: hideKeys [countryKey] and
    // hideModality (Static populations). The hideKeys predicate is
    // declared first, so the lineage names it.
    let mask =
        { VisibilityMask.Hide = [
            VisibilityMask.hideKeys [ countryKey ]
            VisibilityMask.hideModality (Static countryPopulations) ] }
    let result = VisibilityMask.run mask sampleCatalog
    let countryEvent =
        result.Trail
        |> List.find (fun e -> e.SsKey = countryKey)
    match countryEvent.TransformKind with
    | Removed reason -> Assert.Equal("explicit-key-list", reason)
    | other -> Assert.Fail(sprintf "Expected Removed, got %A" other)

[<Fact>]
let ``reordering predicates changes which predicate is named`` () =
    // Same two predicates, opposite order. Now hideModality wins.
    let mask =
        { VisibilityMask.Hide = [
            VisibilityMask.hideModality (Static countryPopulations)
            VisibilityMask.hideKeys [ countryKey ] ] }
    let result = VisibilityMask.run mask sampleCatalog
    let countryEvent =
        result.Trail
        |> List.find (fun e -> e.SsKey = countryKey)
    match countryEvent.TransformKind with
    | Removed reason -> Assert.StartsWith("modality=", reason)
    | other -> Assert.Fail(sprintf "Expected Removed, got %A" other)

// ---------------------------------------------------------------------------
// Identity preservation (A3, A4): the pass never invents or rekeys.
// Surviving kinds retain identical identity, names, modality, etc.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A4: visibility mask never invents or rekeys identities`` () =
    let inputKeys =
        Catalog.allKinds sampleCatalog
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideKeys [ orderKey ] ] }
    let result = VisibilityMask.run mask sampleCatalog
    let outputKeys =
        Catalog.allKinds result.Value
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    // Output keys are a subset of input keys — no invention, no rekeying.
    Assert.True(Set.isSubset outputKeys inputKeys)
    // The removed key is exactly the difference.
    Assert.Equal<Set<SsKey>>(Set.singleton orderKey, Set.difference inputKeys outputKeys)

[<Fact>]
let ``surviving kinds pass through structurally unchanged`` () =
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideKeys [ orderKey ] ] }
    let result = VisibilityMask.run mask sampleCatalog
    let survivingCustomer = Catalog.tryFindKind customerKey result.Value |> Option.get
    Assert.Equal(customer, survivingCustomer)

// ---------------------------------------------------------------------------
// Determinism (T1) — same mask + same catalog → same output every run.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: visibilityMask is deterministic`` () =
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideKeys [ countryKey ] ] }
    let r1 = VisibilityMask.run mask sampleCatalog
    let r2 = VisibilityMask.run mask sampleCatalog
    Assert.Equal(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

// ---------------------------------------------------------------------------
// A23 / A25 — every event carries pass name and version; events are tied
// to the kinds the pass observed.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A23: visibility events carry the pass version and name`` () =
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideOrigin OsNative ] }
    let result = VisibilityMask.run mask sampleCatalog
    Assert.All(result.Trail, fun e ->
        Assert.Equal(VisibilityMask.version, e.PassVersion)
        Assert.Equal("visibilityMask", e.PassName))

[<Fact>]
let ``A25: emitted events reference SsKeys that existed in the input`` () =
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideOrigin OsNative ] }
    let result = VisibilityMask.run mask sampleCatalog
    let inputKeys =
        Catalog.allKinds sampleCatalog
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.All(result.Trail, fun e ->
        Assert.Contains(e.SsKey, inputKeys))

// ---------------------------------------------------------------------------
// Composition with canonicalizeIdentity. The lineage trail (per A24)
// reads chronologically: visibility events first, then canonicalize's
// touches on the surviving kinds.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A24: composition with canonicalizeIdentity preserves chronological order`` () =
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideKeys [ countryKey ] ] }
    let composed =
        VisibilityMask.run mask sampleCatalog
        |> Lineage.bind CanonicalizeIdentity.run
    // The trail starts with the Removed event and continues with two
    // Touched events (one per surviving kind).
    Assert.Equal(3, composed.Trail.Length)
    match composed.Trail.[0].TransformKind with
    | Removed _ -> ()
    | other -> Assert.Fail(sprintf "Expected first event to be Removed, got %A" other)
    Assert.All(composed.Trail |> List.tail, fun e ->
        Assert.Equal(Touched, e.TransformKind))

// ---------------------------------------------------------------------------
// Property: visibilityMask preserves identity-keyed lookup for unmasked
// kinds. For any subset of keys to hide, the surviving keys still resolve
// via Catalog.tryFindKind.
// ---------------------------------------------------------------------------

[<Property>]
let ``visibilityMask preserves identity-keyed lookup for unmasked kinds``
    (hideCustomer: bool) (hideOrder: bool) (hideCountry: bool) =
    let toHide =
        [ if hideCustomer then yield customerKey
          if hideOrder    then yield orderKey
          if hideCountry  then yield countryKey ]
    let mask = { VisibilityMask.Hide = [ VisibilityMask.hideKeys toHide ] }
    let result = VisibilityMask.run mask sampleCatalog
    let allKeys = [ customerKey; orderKey; countryKey ]
    let hideSet = Set.ofList toHide
    allKeys
    |> List.forall (fun k ->
        let inResult = Option.isSome (Catalog.tryFindKind k result.Value)
        let shouldBeIn = not (Set.contains k hideSet)
        inResult = shouldBeIn)
