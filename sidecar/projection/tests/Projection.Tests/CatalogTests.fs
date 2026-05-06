module Projection.Tests.CatalogTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// A2: identity is not name. The compiler enforces this at the type level —
// SsKey and Name are distinct types and one cannot be substituted for the
// other. These tests demonstrate the construction surface and that Name
// validation is independent of SsKey validation.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A2: SsKey and Name are independently constructed and validated`` () =
    Assert.True(Result.isFailure (SsKey.original ""))
    Assert.True(Result.isFailure (Name.create ""))
    Assert.True(Result.isSuccess (SsKey.original "OS_X"))
    Assert.True(Result.isSuccess (Name.create "X"))

// ---------------------------------------------------------------------------
// A4: structural equality of kinds is by SsKey only.
//
// Two kinds with identical SsKey but differing names, attribute orderings,
// or any other field are equal at the catalog level. Default F# record
// equality compares all fields; `Kind.byIdentity` encodes the catalog-level
// identity equality the algebra requires.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A4: kinds with same SsKey are identity-equal regardless of names`` () =
    let renamed = { customer with Name = Name.create "Renamed" |> Result.value }
    Assert.True(Kind.byIdentity customer renamed)

[<Fact>]
let ``A4: kinds with same SsKey are identity-equal regardless of attribute order`` () =
    let reordered = { customer with Attributes = List.rev customer.Attributes }
    Assert.True(Kind.byIdentity customer reordered)

[<Fact>]
let ``A4: kinds with same SsKey are identity-equal regardless of modality marks`` () =
    let demoted = { customer with Modality = [] }
    Assert.True(Kind.byIdentity customer demoted)

[<Fact>]
let ``A4: kinds with different SsKey are NOT identity-equal even if all other fields match`` () =
    let imposter =
        { customer with
            SsKey = SsKey.original "OS_KIND_Imposter" |> Result.value }
    Assert.False(Kind.byIdentity customer imposter)

// ---------------------------------------------------------------------------
// A4 lookup invariants — Catalog.tryFindKind is keyed by SsKey, never by
// name. Renaming a kind does not change the result of identity lookup.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A4: Catalog.tryFindKind locates a kind by SsKey`` () =
    let found = Catalog.tryFindKind customerKey sampleCatalog
    Assert.True(Option.isSome found)
    Assert.Equal(customerKey, (Option.get found).SsKey)

[<Fact>]
let ``A4: Catalog.tryFindKind survives a rename (identity is not name)`` () =
    let renamed =
        { salesModule with
            Kinds =
                salesModule.Kinds
                |> List.map (fun k ->
                    if k.SsKey = customerKey then
                        { k with Name = Name.create "Bystander" |> Result.value }
                    else k) }
    let renamedCatalog = { sampleCatalog with Modules = [ renamed ] }
    let found = Catalog.tryFindKind customerKey renamedCatalog
    Assert.True(Option.isSome found)
    Assert.Equal(customerKey, (Option.get found).SsKey)

[<Fact>]
let ``A4: Catalog.tryFindKind returns None for an unknown SsKey`` () =
    let unknown = SsKey.original "OS_KIND_Unknown" |> Result.value
    Assert.Equal(None, Catalog.tryFindKind unknown sampleCatalog)

// ---------------------------------------------------------------------------
// A5: derived SsKeys are deterministic and traceable.
// ---------------------------------------------------------------------------

[<Property>]
let ``A5: derived(parent, reason) is deterministic`` (s: NonEmptyString) =
    let parent = SsKey.original s.Get |> Result.value
    let d1 = SsKey.derived parent "inverse" |> Result.value
    let d2 = SsKey.derived parent "inverse" |> Result.value
    d1 = d2

[<Fact>]
let ``A5: derived keys preserve traceability to the root original`` () =
    let parent  = SsKey.original "OS_REF_Order_Customer" |> Result.value
    let derived = SsKey.derived parent "inverse" |> Result.value
    Assert.Equal("OS_REF_Order_Customer", SsKey.rootOriginal derived)
    Assert.True(SsKey.isDerived derived)
    Assert.False(SsKey.isDerived parent)
    Assert.Equal<string list>([ "inverse" ], SsKey.derivationReasons derived)

[<Fact>]
let ``A5: chained derivation reasons read root-to-leaf, oldest first`` () =
    let parent = SsKey.original "OS_X" |> Result.value
    let d1 = SsKey.derived parent "inverse" |> Result.value
    let d2 = SsKey.derived d1 "shadow"      |> Result.value
    Assert.Equal<string list>([ "inverse"; "shadow" ], SsKey.derivationReasons d2)

[<Fact>]
let ``A5: derivation reasons cannot be blank`` () =
    let parent = SsKey.original "OS_X" |> Result.value
    Assert.True(Result.isFailure (SsKey.derived parent ""))
    Assert.True(Result.isFailure (SsKey.derived parent "   "))

// ---------------------------------------------------------------------------
// Fixture sanity — confirms the synthetic catalog is well-formed and
// consumable by downstream passes / tests in subsequent commits.
// ---------------------------------------------------------------------------

[<Fact>]
let ``fixture: sample catalog has three kinds in one module`` () =
    Assert.Equal(1, sampleCatalog.Modules.Length)
    Assert.Equal(3, (Catalog.allKinds sampleCatalog).Length)

[<Fact>]
let ``fixture: Order references Customer by SsKey`` () =
    let ref = order.References |> List.exactlyOne
    Assert.Equal(customerKey, ref.TargetKind)

[<Fact>]
let ``fixture: Country carries a Static modality with three populations`` () =
    match country.Modality with
    | [ Static rows ] -> Assert.Equal(3, rows.Length)
    | other ->
        Assert.Fail(sprintf "Expected exactly one Static modality, got %A" other)

[<Fact>]
let ``fixture: Customer carries the TenantScoped modality`` () =
    Assert.Contains(TenantScoped, customer.Modality)
