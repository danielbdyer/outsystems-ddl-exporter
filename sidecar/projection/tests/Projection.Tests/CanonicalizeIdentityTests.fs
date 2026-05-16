module Projection.Tests.CanonicalizeIdentityTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

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
    { Modules =
        c.Modules
        |> List.map (fun m ->
            { m with
                Kinds =
                    m.Kinds
                    |> List.map (reverseAttributes >> reverseReferences) })
        |> List.map reverseKinds
        |> List.rev
      Sequences = c.Sequences }

let private renameKind (key: SsKey) (newName: string) (c: Catalog) : Catalog =
    { Modules =
        c.Modules
        |> List.map (fun m ->
            { m with
                Kinds =
                    m.Kinds
                    |> List.map (fun k ->
                        if k.SsKey = key then
                            { k with Name = Name.create newName |> Result.value }
                        else k) })
      Sequences = c.Sequences }

// ---------------------------------------------------------------------------
// Idempotence (A21 in the small): running the pass twice equals running it
// once. A direct test plus a property test sample over input perturbations.
// ---------------------------------------------------------------------------

[<Fact>]
let ``canonicalizeIdentity: idempotent on the synthetic fixture`` () =
    let once  = (CanonicalizeIdentity.run sampleCatalog).Value
    let twice = (CanonicalizeIdentity.run once).Value
    Assert.Equal(once, twice)

[<Fact>]
let ``canonicalizeIdentity: idempotent on a perturbed catalog`` () =
    let perturbed = reverseAllCollections sampleCatalog
    let once  = (CanonicalizeIdentity.run perturbed).Value
    let twice = (CanonicalizeIdentity.run once).Value
    Assert.Equal(once, twice)

// ---------------------------------------------------------------------------
// Identity-on-well-formed input. After one canonicalization pass the result
// is well-formed; a second pass returns it unchanged. This is the spec's
// "identity transformation on a well-formed catalog" property.
// ---------------------------------------------------------------------------

[<Fact>]
let ``canonicalizeIdentity: identity on already-canonical input`` () =
    let canonical = (CanonicalizeIdentity.run sampleCatalog).Value
    let again     = (CanonicalizeIdentity.run canonical).Value
    Assert.Equal(canonical, again)

// ---------------------------------------------------------------------------
// Normalization-on-malformed input. The spec's "normalization on a
// malformed one" property: a perturbed input becomes canonical after the
// pass. We assert the result equals the canonical of the original (the
// pass is order-insensitive).
// ---------------------------------------------------------------------------

[<Fact>]
let ``canonicalizeIdentity: normalizes a perturbed catalog to the canonical form`` () =
    let canonical = (CanonicalizeIdentity.run sampleCatalog).Value
    let perturbed = reverseAllCollections sampleCatalog
    let normalized = (CanonicalizeIdentity.run perturbed).Value
    Assert.Equal(canonical, normalized)

// ---------------------------------------------------------------------------
// A3 / A4: identity is invariant under rename. Renaming a kind before the
// pass produces an output where the kind has the new name but the same
// SsKey. Cross-references continue to resolve.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A4: canonicalizeIdentity preserves SsKey-keyed lookup across a rename`` () =
    let renamed = renameKind customerKey "RenamedCustomer" sampleCatalog
    let result  = (CanonicalizeIdentity.run renamed).Value
    let found   = Catalog.tryFindKind customerKey result
    Assert.True(Option.isSome found)
    Assert.Equal("RenamedCustomer", Name.value (Option.get found).Name)

[<Fact>]
let ``A4: canonicalizeIdentity preserves the FK target SsKey across a rename`` () =
    // Rename Customer; Order's reference still points at the same SsKey.
    let renamed = renameKind customerKey "RenamedCustomer" sampleCatalog
    let result  = (CanonicalizeIdentity.run renamed).Value
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
        Catalog.allKinds (CanonicalizeIdentity.run sampleCatalog).Value
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.Equal<Set<SsKey>>(inputKeys, outputKeys)

// ---------------------------------------------------------------------------
// Determinism (T1): same input ⇒ same output, including lineage trail.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: canonicalizeIdentity is deterministic`` () =
    let r1 = CanonicalizeIdentity.run sampleCatalog
    let r2 = CanonicalizeIdentity.run sampleCatalog
    Assert.Equal(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

// ---------------------------------------------------------------------------
// Sort behavior: collections in the output are ordered by SsKey.
// ---------------------------------------------------------------------------

[<Fact>]
let ``canonicalizeIdentity: kinds within a module are ordered by SsKey`` () =
    let result = (CanonicalizeIdentity.run (reverseAllCollections sampleCatalog)).Value
    let keys = result.Modules |> List.head |> fun m -> m.Kinds |> List.map (fun k -> k.SsKey)
    Assert.Equal<SsKey list>(keys, List.sort keys)

[<Fact>]
let ``canonicalizeIdentity: attributes within a kind are ordered by SsKey`` () =
    let result = (CanonicalizeIdentity.run (reverseAllCollections sampleCatalog)).Value
    for k in Catalog.allKinds result do
        let keys = k.Attributes |> List.map (fun a -> a.SsKey)
        Assert.Equal<SsKey list>(keys, List.sort keys)

[<Fact>]
let ``canonicalizeIdentity: static populations are ordered by Identifier`` () =
    let result = (CanonicalizeIdentity.run (reverseAllCollections sampleCatalog)).Value
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
    let result = CanonicalizeIdentity.run sampleCatalog
    let kindCount = (Catalog.allKinds result.Value).Length
    Assert.Equal(kindCount, result.Trail.Length)
    Assert.All(result.Trail, fun e ->
        Assert.Equal(Touched, e.TransformKind))

[<Fact>]
let ``A23: canonicalizeIdentity events carry the pass version`` () =
    let result = CanonicalizeIdentity.run sampleCatalog
    Assert.All(result.Trail, fun e ->
        Assert.Equal(CanonicalizeIdentity.version, e.PassVersion)
        Assert.Equal("canonicalizeIdentity", e.PassName))

[<Fact>]
let ``A25: every emitted event references a real catalog SsKey`` () =
    let result = CanonicalizeIdentity.run sampleCatalog
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
    let once  = (CanonicalizeIdentity.run input).Value
    let twice = (CanonicalizeIdentity.run once).Value
    once = twice
