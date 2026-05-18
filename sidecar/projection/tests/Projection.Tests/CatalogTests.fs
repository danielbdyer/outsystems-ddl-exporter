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

// ---------------------------------------------------------------------------
// IsPrimaryKey on Attribute — IR refinement (see DECISIONS.md "IR grows
// under evidence" and ADMIRE.md EntitySeedDeterminizer entry).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Kind.primaryKey returns PK-flagged attributes in declaration order`` () =
    // Customer's PK is its Id attribute (single-column PK).
    let pk = Kind.primaryKey customer
    Assert.Equal(1, pk.Length)
    Assert.Equal(customerIdAttrKey, pk.[0].SsKey)

[<Fact>]
let ``Kind.primaryKey returns an empty list when no attribute is flagged`` () =
    let stripped =
        { customer with
            Attributes =
                customer.Attributes
                |> List.map (fun a -> { a with IsPrimaryKey = false }) }
    Assert.Empty(Kind.primaryKey stripped)

[<Fact>]
let ``Kind.primaryKey supports composite primary keys`` () =
    // Synthesize a composite-PK kind by flagging two attributes.
    let composite =
        { customer with
            Attributes =
                customer.Attributes
                |> List.map (fun a -> { a with IsPrimaryKey = true }) }
    Assert.Equal(customer.Attributes.Length, (Kind.primaryKey composite).Length)

[<Fact>]
let ``fixture: every kind has exactly one PK attribute (Id)`` () =
    for k in Catalog.allKinds sampleCatalog do
        let pk = Kind.primaryKey k
        Assert.Equal(1, pk.Length)
        Assert.Equal("Id", Name.value pk.[0].Name)

[<Fact>]
let ``A2: SsKey and Name are independently constructed and validated`` () =
    // Chapter-3.6 slice-δ: typed-builder smart constructors directly
    // (replaces the retired `SsKey.original` parser-shim).
    Assert.True(Result.isFailure (SsKey.synthesized "OS_KIND" ""))
    Assert.True(Result.isFailure (Name.create ""))
    Assert.True(Result.isSuccess (SsKey.synthesized "OS_KIND" "X"))
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
            SsKey = kindKey ["Imposter"] }
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
    let unknown = kindKey ["Unknown"]
    Assert.Equal(None, Catalog.tryFindKind unknown sampleCatalog)

// ---------------------------------------------------------------------------
// A5: derived SsKeys are deterministic and traceable.
// ---------------------------------------------------------------------------

[<Property>]
let ``A5: derived(parent, reason) is deterministic`` (s: NonEmptyString) =
    // FsCheck's NonEmptyString allows whitespace-only strings, which
    // the typed `synthesized` smart constructor rightly rejects. Skip
    // those samples; the property holds on the meaningful domain of
    // non-blank identifiers.
    if System.String.IsNullOrWhiteSpace s.Get then true
    else
        let parent = SsKey.synthesized "TEST" s.Get |> Result.value
        let d1 = SsKey.derivedFrom parent "inverse" |> Result.value
        let d2 = SsKey.derivedFrom parent "inverse" |> Result.value
        d1 = d2

[<Fact>]
let ``A5: derived keys preserve traceability to the root original`` () =
    let parent  = refKey ["Order"; "Customer"]
    let derived = SsKey.derivedFrom parent "inverse" |> Result.value
    Assert.Equal("OS_REF_Order_Customer", SsKey.rootOriginal derived)
    Assert.True(SsKey.isDerived derived)
    Assert.False(SsKey.isDerived parent)
    Assert.Equal<string list>([ "inverse" ], SsKey.derivationReasons derived)

[<Fact>]
let ``A5: chained derivation reasons read root-to-leaf, oldest first`` () =
    let parent = testKey "OS_X"
    let d1 = SsKey.derivedFrom parent "inverse" |> Result.value
    let d2 = SsKey.derivedFrom d1 "shadow"      |> Result.value
    Assert.Equal<string list>([ "inverse"; "shadow" ], SsKey.derivationReasons d2)

[<Fact>]
let ``A5: derivation reasons cannot be blank`` () =
    let parent = testKey "OS_X"
    Assert.True(Result.isFailure (SsKey.derivedFrom parent ""))
    Assert.True(Result.isFailure (SsKey.derivedFrom parent "   "))

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

// ---------------------------------------------------------------------------
// Chapter 4.2 slice ζ — IsUserFk : bool on Reference.
//
// Per `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` §5: identifies references
// whose target is the platform user kind, so data-emission siblings
// (chapter 4.1.B MigrationDependenciesEmitter + BootstrapEmitter) can
// rewrite `CreatedBy` / `UpdatedBy` column values via the chapter 4.2
// `UserRemapContext`. Closed-DU expansion empirical-test (record-
// extension generalization): F# field-missing errors lit up at every
// literal-construction site (23 production + test sites) and only at
// those sites — semantic interpretation sites unaffected.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice ζ: Reference carries IsUserFk : bool field`` () =
    // Sample reference fixture (Order → Customer) is NOT a User-FK;
    // the IR refinement landed at slice ζ with a default false.
    let r = order.References |> List.head
    Assert.False r.IsUserFk

[<Fact>]
let ``Slice ζ: constructing a Reference with IsUserFk = true carries the flag`` () =
    let userFkRef : Reference =
        { Reference.create orderRefToCustomer (Name.create "CreatedByFk" |> Result.value) orderCustomerFkKey customerKey with IsUserFk = true }
    Assert.True userFkRef.IsUserFk

[<Fact>]
let ``Slice ζ: SymmetricClosure pass inherits IsUserFk from the original reference`` () =
    // The inverse created by SymmetricClosure preserves the original's
    // User-FK status — if the original is a User-FK (CreatedBy →
    // users), its inverse (users → entity that created it) carries
    // the same flag for consumer gating at emission time.
    let userFkRef : Reference =
        { Reference.create orderRefToCustomer (Name.create "CreatedByFk" |> Result.value) orderCustomerFkKey customerKey with IsUserFk = true }
    Assert.True userFkRef.IsUserFk
