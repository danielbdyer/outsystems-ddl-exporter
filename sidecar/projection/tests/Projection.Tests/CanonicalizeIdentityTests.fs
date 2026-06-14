module Projection.Tests.CanonicalizeIdentityTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — `CanonicalizeIdentity.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<Catalog>>`. This per-file shim restores the
// `Lineage<Catalog>` shape so existing assertions keep reading.
let private ciRun (c: Catalog) : Lineage<Catalog> =
    CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)

// ---------------------------------------------------------------------------
// Helpers — perturb a catalog without changing identity, so we can verify
// canonicalization restores order.
// ---------------------------------------------------------------------------

let private reverseAttributes (k: Kind) : Kind =
    { k with Attributes = List.rev k.Attributes }

let private reverseReferences (k: Kind) : Kind =
    { k with References = List.rev k.References }

let private reverseKinds (m: Module) : Module =
    { m with Kinds = List.rev m.Kinds }

let private reverseAllCollections (c: Catalog) : Catalog =
    { IRBuilders.mkCatalog (c.Modules |> List.map (fun m -> { m with Kinds = m.Kinds |> List.map (reverseAttributes >> reverseReferences) }) |> List.map reverseKinds |> List.rev) with
        Sequences = c.Sequences
    }

let private renameKind (key: SsKey) (newName: string) (c: Catalog) : Catalog =
    { IRBuilders.mkCatalog (c.Modules |> List.map (fun m -> { m with Kinds = m.Kinds |> List.map (fun k -> if k.SsKey = key then { k with Name = Name.create newName |> Result.value } else k) })) with
        Sequences = c.Sequences
    }

// ---------------------------------------------------------------------------
// Idempotence (A21 in the small): running the pass twice equals running it
// once. A direct test plus a property test sample over input perturbations.
// ---------------------------------------------------------------------------

[<Fact>]
let ``canonicalizeIdentity: idempotent on the synthetic fixture`` () =
    let once  = (ciRun sampleCatalog).Value
    let twice = (ciRun once).Value
    Assert.Equal(once, twice)

[<Fact>]
let ``canonicalizeIdentity: idempotent on a perturbed catalog`` () =
    let perturbed = reverseAllCollections sampleCatalog
    let once  = (ciRun perturbed).Value
    let twice = (ciRun once).Value
    Assert.Equal(once, twice)

// ---------------------------------------------------------------------------
// Identity-on-well-formed input. After one canonicalization pass the result
// is well-formed; a second pass returns it unchanged. This is the spec's
// "identity transformation on a well-formed catalog" property.
// ---------------------------------------------------------------------------

[<Fact>]
let ``canonicalizeIdentity: identity on already-canonical input`` () =
    let canonical = (ciRun sampleCatalog).Value
    let again     = (ciRun canonical).Value
    Assert.Equal(canonical, again)

// ---------------------------------------------------------------------------
// Normalization-on-malformed input. The spec's "normalization on a
// malformed one" property: a perturbed input becomes canonical after the
// pass. We assert the result equals the canonical of the original (the
// pass is order-insensitive).
// ---------------------------------------------------------------------------

[<Fact>]
let ``canonicalizeIdentity: normalizes a perturbed catalog to the canonical form`` () =
    let canonical = (ciRun sampleCatalog).Value
    let perturbed = reverseAllCollections sampleCatalog
    let normalized = (ciRun perturbed).Value
    Assert.Equal(canonical, normalized)

// ---------------------------------------------------------------------------
// A3 / A4: identity is invariant under rename. Renaming a kind before the
// pass produces an output where the kind has the new name but the same
// SsKey. Cross-references continue to resolve.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A4: canonicalizeIdentity preserves SsKey-keyed lookup across a rename`` () =
    let renamed = renameKind customerKey "RenamedCustomer" sampleCatalog
    let result  = (ciRun renamed).Value
    let found   = Catalog.tryFindKind customerKey result
    Assert.True(Option.isSome found)
    Assert.Equal("RenamedCustomer", Name.value (Option.get found).Name)

[<Fact>]
let ``A4: canonicalizeIdentity preserves the FK target SsKey across a rename`` () =
    // Rename Customer; Order's reference still points at the same SsKey.
    let renamed = renameKind customerKey "RenamedCustomer" sampleCatalog
    let result  = (ciRun renamed).Value
    let foundOrder = Catalog.tryFindKind orderKey result |> Option.get
    let ref = foundOrder.References |> List.exactlyOne
    Assert.Equal(customerKey, ref.TargetKind)

// ---------------------------------------------------------------------------
// A4: structural identity equality holds across the pass — every kind in
// the output has a matching SsKey in the input, and vice versa.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A4: canonicalizeIdentity neither invents nor drops identities`` () =
    let inputKeys =
        Catalog.allKinds sampleCatalog
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    let outputKeys =
        Catalog.allKinds (ciRun sampleCatalog).Value
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.Equal<Set<SsKey>>(inputKeys, outputKeys)

// ---------------------------------------------------------------------------
// Determinism (T1): same input ⇒ same output, including lineage trail.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: canonicalizeIdentity is deterministic`` () =
    let r1 = ciRun sampleCatalog
    let r2 = ciRun sampleCatalog
    Assert.Equal(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

// ---------------------------------------------------------------------------
// Sort behavior: collections in the output are ordered by SsKey.
// ---------------------------------------------------------------------------

[<Fact>]
let ``canonicalizeIdentity: kinds within a module are ordered by SsKey`` () =
    let result = (ciRun (reverseAllCollections sampleCatalog)).Value
    let keys = result.Modules |> List.head |> fun m -> m.Kinds |> List.map (fun k -> k.SsKey)
    Assert.Equal<SsKey list>(keys, List.sort keys)

[<Fact>]
let ``canonicalizeIdentity: attributes within a kind are PK-first then SsKey-ordered (Order=None fixture)`` () =
    // WP8 / NM-72 — the sample fixture carries no authored `Order`
    // (every attribute is `Order = None`), so the ordering reduces to
    // (PK first, then SsKey) within each kind. PKs lead; the non-PK body
    // stays SsKey-ordered (the v1 contract within each band).
    let result = (ciRun (reverseAllCollections sampleCatalog)).Value
    for k in Catalog.allKinds result do
        // No attribute in the fixture carries an authored order.
        Assert.All(k.Attributes, fun a -> Assert.True(Option.isNone a.Order))
        // PK rank is monotone non-decreasing: every PK precedes every non-PK.
        let pkRanks = k.Attributes |> List.map (fun a -> if a.IsPrimaryKey then 0 else 1)
        Assert.Equal<int list>(pkRanks, List.sort pkRanks)
        // Within the non-PK band, SsKey order holds.
        let nonPkKeys = k.Attributes |> List.filter (fun a -> not a.IsPrimaryKey) |> List.map (fun a -> a.SsKey)
        Assert.Equal<SsKey list>(nonPkKeys, List.sort nonPkKeys)
        // Within the PK band, SsKey order holds.
        let pkKeys = k.Attributes |> List.filter (fun a -> a.IsPrimaryKey) |> List.map (fun a -> a.SsKey)
        Assert.Equal<SsKey list>(pkKeys, List.sort pkKeys)

[<Fact>]
let ``canonicalizeIdentity: static populations are ordered by Identifier`` () =
    let result = (ciRun (reverseAllCollections sampleCatalog)).Value
    let countryK = Catalog.tryFindKind countryKey result |> Option.get
    match countryK.Modality with
    | [ Static rows ] ->
        let ids = rows |> List.map (fun r -> r.Identifier)
        Assert.Equal<SsKey list>(ids, List.sort ids)
    | other ->
        Assert.Fail(sprintf "Expected exactly one Static modality, got %A" other)

// ---------------------------------------------------------------------------
// Lineage emission — A23, A25.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A25: canonicalizeIdentity emits one Touched event per kind`` () =
    let result = ciRun sampleCatalog
    let kindCount = (Catalog.allKinds result.Value).Length
    Assert.Equal(kindCount, result.Trail.Length)
    Assert.All(result.Trail, fun e ->
        Assert.Equal(Touched, e.TransformKind))

[<Fact>]
let ``A23: canonicalizeIdentity events carry the pass version`` () =
    let result = ciRun sampleCatalog
    Assert.All(result.Trail, fun e ->
        Assert.Equal(CanonicalizeIdentity.version, e.PassVersion)
        Assert.Equal("canonicalizeIdentity", e.PassName))

[<Fact>]
let ``A25: every emitted event references a real catalog SsKey`` () =
    let result = ciRun sampleCatalog
    let kindKeys =
        Catalog.allKinds result.Value
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.All(result.Trail, fun e ->
        Assert.Contains(e.SsKey, kindKeys))

// ---------------------------------------------------------------------------
// Property: idempotence under arbitrary collection reversal. Even if the
// input is perturbed, two passes equal one pass on the output.
// ---------------------------------------------------------------------------

[<Property>]
let ``canonicalizeIdentity: idempotent under collection reversal`` (reverseFlag: bool) =
    let input =
        if reverseFlag then reverseAllCollections sampleCatalog
        else sampleCatalog
    let once  = (ciRun input).Value
    let twice = (ciRun once).Value
    once = twice

// ---------------------------------------------------------------------------
// WP8 / NM-72 — attribute ordering: (PK first, then authored Order
// ascending, then SsKey). These pure tests pin the ordering contract
// without Docker; the OSSYS extraction canary proves the end-to-end path.
// ---------------------------------------------------------------------------

module private OrderFixtures =
    let nm (s: string) : Name = Name.create s |> Result.value
    // Keys are chosen so SsKey order (the old v1 sort) would be A < B <
    // C < D — i.e. alphabetical — making the authored re-order visible.
    let key (n: int) : SsKey =
        SsKey.ossysOriginal (System.Guid(n, 0s, 0s, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy))
    let tableId (schema: string) (table: string) : TableId =
        TableId.create schema table |> Result.value
    let attrWith (n: int) (name: string) (isPk: bool) (order: int option) : Attribute =
        { Attribute.create (key n) (nm name) PrimitiveType.Integer with
            IsPrimaryKey = isPk
            Order = order }

[<Fact>]
let ``canonicalizeIdentity: attributes emit in authored Order, not SsKey/alphabetical order`` () =
    let open' = OrderFixtures.attrWith
    // SsKey/alphabetical order would be: Id, Apple, Banana, Cherry, Date.
    // Authored order (PK first, then Order ascending) is: Id, Date(10),
    // Cherry(20), Banana(30), Apple(40) — the reverse of alphabetical.
    let attrs =
        [ open' 1 "Id"     true  (Some 1)
          open' 2 "Apple"  false (Some 40)
          open' 3 "Banana" false (Some 30)
          open' 4 "Cherry" false (Some 20)
          open' 5 "Date"   false (Some 10) ]
    let kind =
        Kind.create (OrderFixtures.key 100) (OrderFixtures.nm "K")
            (OrderFixtures.tableId "dbo" "K") attrs
    let m =
        { SsKey = OrderFixtures.key 1000; Name = OrderFixtures.nm "M"
          Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
    let catalog = Catalog.create [ m ] [] |> Result.value
    let ordered =
        (ciRun catalog).Value.Modules
        |> List.head |> fun m -> m.Kinds |> List.head
        |> fun k -> k.Attributes |> List.map (fun a -> Name.value a.Name)
    Assert.Equal<string list>([ "Id"; "Date"; "Cherry"; "Banana"; "Apple" ], ordered)

[<Fact>]
let ``canonicalizeIdentity: Order=None falls back to PK-first then SsKey order (determinism preserved)`` () =
    let open' = OrderFixtures.attrWith
    // No authored order anywhere — the result must be the old v1
    // behaviour within each PK band: PK first, then SsKey order.
    let attrs =
        [ open' 4 "Cherry" false None
          open' 2 "Apple"  false None
          open' 1 "Id"     true  None
          open' 3 "Banana" false None ]
    let kind =
        Kind.create (OrderFixtures.key 100) (OrderFixtures.nm "K")
            (OrderFixtures.tableId "dbo" "K") attrs
    let m =
        { SsKey = OrderFixtures.key 1000; Name = OrderFixtures.nm "M"
          Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
    let catalog = Catalog.create [ m ] [] |> Result.value
    let ordered =
        (ciRun catalog).Value.Modules
        |> List.head |> fun m -> m.Kinds |> List.head
        |> fun k -> k.Attributes |> List.map (fun a -> Name.value a.Name)
    // Id is PK (rank 0), then keys 2,3,4 in SsKey order: Apple, Banana, Cherry.
    Assert.Equal<string list>([ "Id"; "Apple"; "Banana"; "Cherry" ], ordered)

[<Fact>]
let ``canonicalizeIdentity: authored attributes precede Order=None ones within the non-PK band`` () =
    let open' = OrderFixtures.attrWith
    // Mixed: some authored, some not. Authored (Order=Some) sort ahead of
    // unauthored (Order=None); within each band the prior tiebreak applies.
    let attrs =
        [ open' 1 "Id"       true  (Some 1)
          open' 2 "Unsorted" false None
          open' 3 "Second"   false (Some 20)
          open' 4 "First"    false (Some 10) ]
    let kind =
        Kind.create (OrderFixtures.key 100) (OrderFixtures.nm "K")
            (OrderFixtures.tableId "dbo" "K") attrs
    let m =
        { SsKey = OrderFixtures.key 1000; Name = OrderFixtures.nm "M"
          Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
    let catalog = Catalog.create [ m ] [] |> Result.value
    let ordered =
        (ciRun catalog).Value.Modules
        |> List.head |> fun m -> m.Kinds |> List.head
        |> fun k -> k.Attributes |> List.map (fun a -> Name.value a.Name)
    Assert.Equal<string list>([ "Id"; "First"; "Second"; "Unsorted" ], ordered)
