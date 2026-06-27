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
        let d1 = SsKey.derivedFrom parent DerivationReason.Inverse
        let d2 = SsKey.derivedFrom parent DerivationReason.Inverse
        d1 = d2

[<Fact>]
let ``A5: derived keys preserve traceability to the root original`` () =
    let parent  = refKey ["Order"; "Customer"]
    let derived = SsKey.derivedFrom parent DerivationReason.Inverse
    Assert.Equal("OS_REF_Order_Customer", SsKey.rootOriginal derived)
    Assert.True(SsKey.isDerived derived)
    Assert.False(SsKey.isDerived parent)
    Assert.Equal<DerivationReason list>([ DerivationReason.Inverse ], SsKey.derivationReasons derived)

[<Fact>]
let ``A5: chained derivation reasons read root-to-leaf, oldest first`` () =
    let parent = testKey "OS_X"
    let d1 = SsKey.derivedFrom parent DerivationReason.Inverse
    let d2 = SsKey.derivedFrom d1 DerivationReason.Inverse
    Assert.Equal<DerivationReason list>([ DerivationReason.Inverse; DerivationReason.Inverse ], SsKey.derivationReasons d2)

[<Fact>]
let ``A5: the derivation-reason set is closed — an unknown serialized token fails to parse`` () =
    // The blank-reason rejection is now STRUCTURAL: the reason is a closed
    // `DerivationReason` DU, unconstructable from a free string. The codec is the
    // one place an invalid reason can still arrive (a malformed stored key); it
    // fails loud rather than minting a silently-different identity.
    match DerivationReason.parse "inverse" with
    | Ok r    -> Assert.Equal(DerivationReason.Inverse, r)
    | Error _ -> Assert.Fail "expected Ok for the reserved 'inverse' token"
    Assert.True(Result.isFailure (DerivationReason.parse "shadow"))
    Assert.True(Result.isFailure (DerivationReason.parse ""))

// ---------------------------------------------------------------------------
// Fixture sanity — confirms the synthetic catalog is well-formed and
// consumable by downstream passes / tests in subsequent commits.
// ---------------------------------------------------------------------------

[<Fact>]
let ``fixture: sample catalog has three kinds in one module`` () =
    Assert.Equal(1, sampleCatalog.Modules.Length)
    Assert.Equal(3, (Catalog.allKinds sampleCatalog).Length)

// ---------------------------------------------------------------------------
// Chapter-Cluster-B Catalog traversal primitives (2026-05-22).
// Tests for the named compression primitives: allModulesKinds, foldKinds,
// mapKinds, updateKindsWhere.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Catalog.allModulesKinds: pairs every Kind with its owning Module`` () =
    let pairs = Catalog.allModulesKinds sampleCatalog
    Assert.Equal(3, pairs.Length)
    // Every kind's owner is the sample catalog's single module.
    let expectedModule = sampleCatalog.Modules.[0]
    Assert.True(pairs |> List.forall (fun (m, _) -> m = expectedModule))

[<Fact>]
let ``Catalog.foldKinds: counts every kind across modules`` () =
    let kindCount = Catalog.foldKinds (fun _ _ acc -> acc + 1) 0 sampleCatalog
    Assert.Equal(3, kindCount)

[<Fact>]
let ``Catalog.foldKinds: passes owning Module to the accumulator`` () =
    let modulesEncountered =
        Catalog.foldKinds
            (fun m _ acc -> Set.add m.SsKey acc)
            Set.empty
            sampleCatalog
    Assert.Equal(1, Set.count modulesEncountered)  // single-module fixture

[<Fact>]
let ``Catalog.iterKinds: visits every kind once`` () =
    let visited = ResizeArray<SsKey>()
    Catalog.iterKinds (fun _ k -> visited.Add(k.SsKey)) sampleCatalog
    Assert.Equal(3, visited.Count)

[<Fact>]
let ``Catalog.mapKinds: identity transformation preserves the catalog`` () =
    let result = Catalog.mapKinds id sampleCatalog
    Assert.Equal(sampleCatalog, result)

[<Fact>]
let ``Catalog.updateKindsWhere: non-matching kinds pass through unchanged`` () =
    // Update no kinds (predicate matches nothing)
    let result = Catalog.updateKindsWhere (fun _ -> false) (fun k -> k) sampleCatalog
    Assert.Equal(sampleCatalog, result)

[<Fact>]
let ``Catalog.updateKindsWhere: matching kind is transformed; siblings preserved`` () =
    let renamed =
        Catalog.updateKindsWhere
            (fun k -> k.SsKey = customerKey)
            (fun k -> { k with Name = Name.create "Customer_Renamed" |> Result.value })
            sampleCatalog
    let renamedKind = Catalog.tryFindKind customerKey renamed |> Option.get
    Assert.Equal("Customer_Renamed", Name.value renamedKind.Name)
    // Other kinds unchanged: SsKey set is preserved (A4).
    let originalKeys = sampleCatalog |> Catalog.allKinds |> List.map (fun k -> k.SsKey) |> Set.ofList
    let updatedKeys = renamed |> Catalog.allKinds |> List.map (fun k -> k.SsKey) |> Set.ofList
    Assert.Equal<Set<SsKey>>(originalKeys, updatedKeys)

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

[<Fact>]
let ``F2: stripStaticPopulations strips Static only — preflight preserves non-Static modality marks`` () =
    // A kind carrying Static alongside every payload-free mark: the strip
    // must remove exactly Static. The prior Preflight form was
    // `Modality = []`, which also erased authored marks — the N2
    // over-erasure (CONSTELLATION_BACKLOG plane N2), closed 2026-06-11 by
    // routing all three strip sites through this one definition site.
    let marked =
        { country with
            Modality = country.Modality @ [ TenantScoped; SoftDeletable; SystemOwned ] }
    let catalog =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds = m.Kinds |> List.map (fun k -> if k.SsKey = marked.SsKey then marked else k) }) }
    let stripped = Catalog.stripStaticPopulations catalog
    for k in Catalog.allKinds stripped do
        Assert.True(
            k.Modality |> List.forall (function Static _ -> false | _ -> true),
            sprintf "Static mark survived the strip on %A" k.Name)
    let strippedCountry = Catalog.allKinds stripped |> List.find (fun k -> k.SsKey = country.SsKey)
    Assert.Equal<ModalityMark list>([ TenantScoped; SoftDeletable; SystemOwned ], strippedCountry.Modality)
    let strippedCustomer = Catalog.allKinds stripped |> List.find (fun k -> k.SsKey = customer.SsKey)
    Assert.Contains(TenantScoped, strippedCustomer.Modality)

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

// ---------------------------------------------------------------------------
// NM-15 — Attribute.create is TOTAL. An over-128-char logical name no longer
// failwithf's mid-IR-build (it was the only non-total smart constructor in the
// file); the default ColumnName is fitted via IdentifierBudget.fit — the same
// deterministic discipline the SSDT emitters use for over-budget generated
// names — yielding a VALID <=128-char identifier, never silent-invalid SQL.
// ---------------------------------------------------------------------------

[<Fact>]
let ``NM-15: Attribute.create on an over-128-char logical name does not throw and fits the default column`` () =
    let longLogical = String.replicate 200 "x"   // 200 chars > 128
    let nm = Name.create longLogical |> Result.value
    // Previously this threw; now it is total.
    let attr = Attribute.create (attrKey ["X"; "Long"]) nm Integer
    let colText = ColumnRealization.columnNameText attr.Column
    // The fitted default is a valid SQL Server identifier (<=128 chars) and
    // exactly matches IdentifierBudget.fit of the logical name (deterministic).
    Assert.True(colText.Length <= 128, "fitted default column name must fit the 128-char budget")
    Assert.Equal(IdentifierBudget.fit longLogical, colText)
    // The logical Name itself is preserved unchanged (only the column is fitted).
    Assert.Equal(longLogical, Name.value attr.Name)

[<Fact>]
let ``NM-15: a short logical name still derives a byte-identical default column (T1 untouched)`` () =
    let nm = Name.create "CustomerId" |> Result.value
    let attr = Attribute.create (attrKey ["X"; "Short"]) nm Integer
    Assert.Equal("CustomerId", ColumnRealization.columnNameText attr.Column)

[<Fact>]
let ``NM-15: an explicit Column override on a long-named attribute is honored (no throw on the discarded default)`` () =
    // The CatalogCodec JSON round-trip shape: `{ Attribute.create k n t with
    // Column = <deserialized> }`. The throwing default used to fire even though
    // the override immediately replaced it; now the total default lets the
    // override stand.
    let longLogical = String.replicate 200 "y"
    let nm = Name.create longLogical |> Result.value
    let explicitCol = ColumnRealization.create "PHYSICAL_COL" false |> Result.value
    let attr = { Attribute.create (attrKey ["X"; "Override"]) nm Integer with Column = explicitCol }
    Assert.Equal("PHYSICAL_COL", ColumnRealization.columnNameText attr.Column)
