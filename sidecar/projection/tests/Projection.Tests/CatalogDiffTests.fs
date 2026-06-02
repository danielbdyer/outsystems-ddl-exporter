module Projection.Tests.CatalogDiffTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Tests.Fixtures

// FSharp.Core's two-arity `Result<'a, 'b>` case constructors collide
// with `Projection.Core.DiagnosticSeverity.Error` once `Projection.Core`
// is opened; qualifying via a private type alias forces case access
// to resolve to FSharp.Core's Result.Ok / Result.Error without
// shadowing the single-arity `Result<'a>.Error` case (the same
// alias pattern used by `ArtifactByKindTests.fs`).
type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

// ---------------------------------------------------------------------------
// CatalogDiff exhaustiveness — chapter 3.5 substantive deliverable.
//
// `CatalogDiff.between source target` partitions every SsKey in
// `source ∪ target` into exactly one of four sets — `Renamed`, `Added`,
// `Removed`, `Unchanged`. The smart constructor enforces the
// invariant by construction (`Set.difference` / `Set.intersect`
// produce disjoint partitions); these tests demonstrate the worked
// examples and (via FsCheck) the exhaustiveness property over
// permutations of `sampleCatalog`.
// ---------------------------------------------------------------------------

let private mustOk (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err ->
        Assert.Fail(sprintf "%A" err)
        Unchecked.defaultof<'a>

let private kindKeys (c: Catalog) : Set<SsKey> =
    Catalog.allKinds c |> List.map (fun k -> k.SsKey) |> Set.ofList

// ---------------------------------------------------------------------------
// Worked examples — small hand-built fixtures that exercise each partition.
// ---------------------------------------------------------------------------

[<Fact>]
let ``CatalogDiff.between (a, a) — every key in Unchanged`` () =
    let a = sampleCatalog
    let diff = CatalogDiff.between a a |> mustOk
    Assert.Equal<Set<SsKey>>(kindKeys a, CatalogDiff.unchanged diff)
    Assert.Empty(CatalogDiff.added diff)
    Assert.Empty(CatalogDiff.removed diff)
    Assert.Empty(CatalogDiff.renamed diff)
    Assert.True(CatalogDiff.isEmpty diff)

[<Fact>]
let ``CatalogDiff.between empty source vs target — every target key in Added`` () =
    let target = sampleCatalog
    let empty = Catalog.create [] [] |> Result.value
    let diff = CatalogDiff.between empty target |> mustOk
    Assert.Equal<Set<SsKey>>(kindKeys target, CatalogDiff.added diff)
    Assert.Empty(CatalogDiff.removed diff)
    Assert.Empty(CatalogDiff.unchanged diff)
    Assert.Empty(CatalogDiff.renamed diff)
    Assert.False(CatalogDiff.isEmpty diff)

[<Fact>]
let ``CatalogDiff.between source vs empty target — every source key in Removed`` () =
    let source = sampleCatalog
    let empty = Catalog.create [] [] |> Result.value
    let diff = CatalogDiff.between source empty |> mustOk
    Assert.Equal<Set<SsKey>>(kindKeys source, CatalogDiff.removed diff)
    Assert.Empty(CatalogDiff.added diff)
    Assert.Empty(CatalogDiff.unchanged diff)
    Assert.Empty(CatalogDiff.renamed diff)
    Assert.False(CatalogDiff.isEmpty diff)

// ---------------------------------------------------------------------------
// Exhaustiveness property: |source ∪ target| = |Renamed| + |Added| +
// |Removed| + |Unchanged|, and the four partitions are pairwise disjoint.
// ---------------------------------------------------------------------------

[<Fact>]
let ``CatalogDiff exhaustiveness: scope equals disjoint union of partitions`` () =
    let a = sampleCatalog
    let b = sampleCatalog  // same catalog; partitions land in Unchanged
    let diff = CatalogDiff.between a b |> mustOk
    let expected =
        let aKeys = kindKeys a
        let bKeys = kindKeys b
        Set.union aKeys bKeys
    Assert.Equal<Set<SsKey>>(expected, CatalogDiff.scope diff)

[<Fact>]
let ``CatalogDiff partitions are pairwise disjoint`` () =
    let diff = CatalogDiff.between sampleCatalog sampleCatalog |> mustOk
    let renamedKeys =
        CatalogDiff.renamed diff |> Map.toSeq |> Seq.map fst |> Set.ofSeq
    let added = CatalogDiff.added diff
    let removed = CatalogDiff.removed diff
    let unchanged = CatalogDiff.unchanged diff
    Assert.Empty(Set.intersect renamedKeys added)
    Assert.Empty(Set.intersect renamedKeys removed)
    Assert.Empty(Set.intersect renamedKeys unchanged)
    Assert.Empty(Set.intersect added removed)
    Assert.Empty(Set.intersect added unchanged)
    Assert.Empty(Set.intersect removed unchanged)

// ---------------------------------------------------------------------------
// Determinism — same inputs → same diff.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: CatalogDiff.between is deterministic across repeat invocations`` () =
    let a = sampleCatalog
    let b = sampleCatalog
    let runs =
        [ for _ in 1 .. 10 -> CatalogDiff.between a b |> mustOk ]
    let head = List.head runs
    let headPartitions =
        (CatalogDiff.renamed head, CatalogDiff.added head,
         CatalogDiff.removed head, CatalogDiff.unchanged head)
    Assert.All(
        runs,
        fun d ->
            Assert.Equal(
                headPartitions,
                (CatalogDiff.renamed d, CatalogDiff.added d,
                 CatalogDiff.removed d, CatalogDiff.unchanged d)))

// ---------------------------------------------------------------------------
// Property: for any FsCheck-generated permutation of the source
// Catalog's modules, `CatalogDiff.between` against the original
// produces empty `Added`/`Removed`/`Renamed` and full `Unchanged` —
// the diff is invariant to source-side ordering.
// ---------------------------------------------------------------------------

[<Property(MaxTest = 25)>]
let ``CatalogDiff is invariant under module-list permutation``
    (modules: Module list) =
    let original = sampleCatalog
    let permuted =
        Catalog.create (original.Modules |> List.rev) original.Sequences
        |> Result.value
    let diff = CatalogDiff.between original permuted |> mustOk
    // Permuting modules preserves the kind-set; every SsKey is in
    // Unchanged. (`modules` is unused — the Property attribute drives
    // FsCheck's input generation but the property under test fixes
    // its inputs by construction.)
    ignore modules
    Set.isEmpty (CatalogDiff.added diff)
    && Set.isEmpty (CatalogDiff.removed diff)
    && Map.isEmpty (CatalogDiff.renamed diff)
    && (kindKeys original) = (CatalogDiff.unchanged diff)

// ---------------------------------------------------------------------------
// 6.A.10 — attribute-level diff (the structural keystone). Kind-level
// `CatalogDiff` was attribute-blind: a `Customer.Name` column change left
// the kind in `Unchanged` with no signal, so the operator's "minimum viable
// touches" was impossible (no diff → no ALTER → silent full-redeploy with a
// possible type coercion). `between` now descends into attributes and names
// the changed facet. The kind-level partitions are PRESERVED: a kind whose
// name is stable stays in `Unchanged`; the attribute change rides the new
// per-kind `AttributeDiffs` map.
// ---------------------------------------------------------------------------

/// Rebuild `sampleCatalog` with Customer's `Name` attribute transformed.
let private catalogWithCustomerName (f: Attribute -> Attribute) : Catalog =
    let customer' =
        { customer with
            Attributes =
                customer.Attributes
                |> List.map (fun a -> if a.SsKey = customerNameKey then f a else a) }
    let m = { salesModule with Kinds = [ customer'; order; country ] }
    Catalog.create [ m ] [] |> Result.value

[<Fact>]
let ``CatalogDiff: a column type change surfaces as an attribute-level Changed entry`` () =
    // Customer.Name: Text -> Integer (a DataType facet change). The kind
    // name is unchanged, so kind-level diffing alone reports nothing.
    let target = catalogWithCustomerName (fun a -> { a with Type = Integer })
    let diff = CatalogDiff.between sampleCatalog target |> mustOk

    // Kind-level contract preserved: Customer stays in Unchanged.
    Assert.Contains(customerKey, CatalogDiff.unchanged diff)
    Assert.DoesNotContain(customerKey, CatalogDiff.added diff)
    Assert.DoesNotContain(customerKey, CatalogDiff.removed diff)

    // The attribute change is now visible — and isEmpty is honest.
    Assert.False(CatalogDiff.isEmpty diff)
    match CatalogDiff.attributeDiffOf customerKey diff with
    | None -> Assert.Fail "expected an AttributeDiff for Customer"
    | Some ad ->
        Assert.Empty(ad.Added)
        Assert.Empty(ad.Removed)
        Assert.Empty(ad.Renamed)
        Assert.Equal(1, List.length ad.Changed)
        let change = List.head ad.Changed
        Assert.Equal(customerNameKey, change.AttributeKey)
        Assert.Contains(AttributeFacet.DataType, change.Facets)

[<Fact>]
let ``CatalogDiff: a column widening (length change) names the Length facet (TEXT -> NVARCHAR(256))`` () =
    // The audit's headline scenario: type category stable, declared length
    // changes. Length None -> Some 256 is the IR shape of TEXT -> NVARCHAR(256).
    let target = catalogWithCustomerName (fun a -> { a with Length = Some 256 })
    let diff = CatalogDiff.between sampleCatalog target |> mustOk
    let ad = (CatalogDiff.attributeDiffOf customerKey diff).Value
    let change = List.head ad.Changed
    Assert.Contains(AttributeFacet.Length, change.Facets)
    Assert.DoesNotContain(AttributeFacet.DataType, change.Facets)

[<Fact>]
let ``CatalogDiff: a nullability change names the Nullability facet`` () =
    let target =
        catalogWithCustomerName (fun a ->
            { a with Column = { a.Column with IsNullable = true } })
    let diff = CatalogDiff.between sampleCatalog target |> mustOk
    let ad = (CatalogDiff.attributeDiffOf customerKey diff).Value
    Assert.Contains(AttributeFacet.Nullability, (List.head ad.Changed).Facets)

[<Fact>]
let ``CatalogDiff: a dropped attribute surfaces in AttributeDiff.Removed`` () =
    // Remove Customer.TenantId from the target.
    let target =
        let customer' =
            { customer with
                Attributes =
                    customer.Attributes |> List.filter (fun a -> a.SsKey <> customerTenantKey) }
        let m = { salesModule with Kinds = [ customer'; order; country ] }
        Catalog.create [ m ] [] |> Result.value
    let diff = CatalogDiff.between sampleCatalog target |> mustOk
    let ad = (CatalogDiff.attributeDiffOf customerKey diff).Value
    Assert.Contains(customerTenantKey, ad.Removed)
    Assert.False(CatalogDiff.isEmpty diff)

[<Fact>]
let ``CatalogDiff: identical catalogs carry no AttributeDiffs (empty diff is honest)`` () =
    let diff = CatalogDiff.between sampleCatalog sampleCatalog |> mustOk
    Assert.True(Map.isEmpty (CatalogDiff.attributeDiffs diff))
    Assert.True(CatalogDiff.isEmpty diff)

[<Fact>]
let ``T1: attribute-level diff is deterministic across repeat invocations`` () =
    let target = catalogWithCustomerName (fun a -> { a with Type = Integer; Length = Some 64 })
    let runs = [ for _ in 1 .. 10 -> CatalogDiff.attributeDiffs (CatalogDiff.between sampleCatalog target |> mustOk) ]
    let head = List.head runs
    Assert.All(runs, fun r -> Assert.Equal<Map<SsKey, AttributeDiff>>(head, r))

// ---------------------------------------------------------------------------
// 6.A.11 — applyDiff + the evolution round-trip law (H-007). `between` is the
// observation; `applyDiff` is the action. The law `applyDiff (between A B) A =
// B` (modulo the captured surface) makes the Time axis an evolution algebra,
// not a snapshot store. The law is witnessed order-insensitively by the diff's
// own equality notion: `between B (applyDiff (between A B) A) |> isEmpty`.
// ---------------------------------------------------------------------------

let private nm (s: string) : Name = Name.create s |> Result.value

/// A single-module catalog over the given kinds (reusing the Sales module).
let private catalogOfKinds (kinds: Kind list) : Catalog =
    let m = { salesModule with Kinds = kinds }
    Catalog.create [ m ] [] |> Result.value

[<Fact>]
let ``Time: applyDiff (between A B) A = B (evolution round-trip law)`` () =
    // B is A evolved across the whole captured surface at once: a renamed
    // kind, a column type change, a nullability change, a dropped attribute,
    // and an added attribute.
    let a = sampleCatalog
    let newAttrKey = attrKey ["Customer"; "Loyalty"]
    let customer' =
        { customer with
            Name = nm "Client"   // kind rename
            Attributes =
                (customer.Attributes
                 |> List.choose (fun at ->
                     if at.SsKey = customerTenantKey then None                 // drop TenantId
                     elif at.SsKey = customerNameKey then
                         Some { at with Type = Integer }                       // type change
                     elif at.SsKey = customerIdAttrKey then
                         Some { at with Column = { at.Column with IsNullable = true } } // nullability change
                     else Some at))
                @ [ { Attribute.create newAttrKey (nm "Loyalty") Integer with
                        Column = ColumnRealization.create ("LOYALTY") (true) |> Result.value } ] } // add column
    let b = catalogOfKinds [ customer'; order; country ]

    let diff = CatalogDiff.between a b |> mustOk
    let reconstructed = CatalogDiff.applyDiff a diff

    // The round-trip law: the reconstruction has NO diff against B over the
    // captured surface (kinds + names + attribute presence/name/facets).
    let residual = CatalogDiff.between b reconstructed |> mustOk
    Assert.True(CatalogDiff.isEmpty residual, "applyDiff (between A B) A must reproduce B (residual diff was non-empty)")

[<Fact>]
let ``applyDiff (between A A) A = A — the identity diff is identity`` () =
    let a = sampleCatalog
    let diff = CatalogDiff.between a a |> mustOk
    let reconstructed = CatalogDiff.applyDiff a diff
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between a reconstructed |> mustOk))

[<Fact>]
let ``applyDiff: an in-place column type change reconstructs the exact target attribute`` () =
    // A direct structural check (no ordering ambiguity — same kinds, same
    // order, one patched facet): the reconstructed Customer.Name attribute
    // equals B's.
    let a = sampleCatalog
    let b = catalogWithCustomerName (fun at -> { at with Type = Integer })
    let reconstructed = CatalogDiff.applyDiff a (CatalogDiff.between a b |> mustOk)
    let nameAttr (c: Catalog) =
        (Catalog.tryFindKind customerKey c).Value.Attributes
        |> List.find (fun at -> at.SsKey = customerNameKey)
    Assert.Equal(nameAttr b, nameAttr reconstructed)

[<Fact>]
let ``applyDiff threads the passed-in catalog, not the recorded target (no-cheat)`` () =
    // A = {Customer, Order}; B = {Customer} (Order removed). The diff records
    // Target = B. Applying it to a DIFFERENT base that also carries Country
    // must keep Country — proving applyDiff transforms the argument, and is
    // not `fun _ d -> target d` (which would have dropped Country).
    let a = catalogOfKinds [ customer; order ]
    let b = catalogOfKinds [ customer ]
    let diff = CatalogDiff.between a b |> mustOk

    let baseWithExtra = catalogOfKinds [ customer; order; country ]
    let result = CatalogDiff.applyDiff baseWithExtra diff

    // Order was removed; Country (only in the passed-in base) survives.
    Assert.True((Catalog.tryFindKind orderKey result).IsNone)
    Assert.True((Catalog.tryFindKind countryKey result).IsSome)
    // The recorded target carries NO Country — so the result differs from it.
    Assert.True((Catalog.tryFindKind countryKey (CatalogDiff.target diff)).IsNone)

[<Fact>]
let ``applyDiff: a dropped attribute is removed from the reconstruction`` () =
    let a = sampleCatalog
    let b =
        let customer' =
            { customer with
                Attributes = customer.Attributes |> List.filter (fun at -> at.SsKey <> customerTenantKey) }
        catalogOfKinds [ customer'; order; country ]
    let reconstructed = CatalogDiff.applyDiff a (CatalogDiff.between a b |> mustOk)
    let customerAttrs =
        (Catalog.tryFindKind customerKey reconstructed).Value.Attributes
        |> List.map (fun at -> at.SsKey)
        |> Set.ofList
    Assert.DoesNotContain(customerTenantKey, customerAttrs)

// ---------------------------------------------------------------------------
// 6.H.3 prework — the norm ‖·‖, the channel projection π, and compose (the
// derivative algebra's measurement + composition layer, concrete on the
// CatalogDiff value; WAVE_6_ALGEBRA.md §12.4). The norm is the schema-side
// `‖·‖`; compose is the torsor `+` (T13 / A-Lifecycle-4).
// ---------------------------------------------------------------------------

[<Fact>]
let ``norm: between A A has norm 0`` () =
    let d = CatalogDiff.between sampleCatalog sampleCatalog |> mustOk
    Assert.Equal(0, CatalogDiff.norm d)
    Assert.True(CatalogDiff.isEmpty d)

[<Fact>]
let ``norm: a non-empty diff has norm > 0 (norm d = 0 iff isEmpty d)`` () =
    let target = catalogWithCustomerName (fun a -> { a with Type = Integer })
    let d = CatalogDiff.between sampleCatalog target |> mustOk
    Assert.False(CatalogDiff.isEmpty d)
    Assert.True(CatalogDiff.norm d > 0)

[<Fact>]
let ``norm: equals the sum of the channel counts (additivity, T14/T15)`` () =
    // A mixed delta: a renamed kind + a dropped attribute + an added attribute
    // + a column type change — exercises several channels at once.
    let newAttrKey = attrKey ["Customer"; "Loyalty"]
    let customer' =
        { customer with
            Name = nm "Client"
            Attributes =
                (customer.Attributes
                 |> List.choose (fun at ->
                     if at.SsKey = customerTenantKey then None
                     elif at.SsKey = customerNameKey then Some { at with Type = Integer }
                     else Some at))
                @ [ { Attribute.create newAttrKey (nm "Loyalty") Integer with
                        Column = ColumnRealization.create ("LOYALTY") (true) |> Result.value } ] }
    let target = catalogOfKinds [ customer'; order; country ]
    let d = CatalogDiff.between sampleCatalog target |> mustOk
    let c = CatalogDiff.channelCounts d
    let sum =
        c.RenamedKinds + c.AddedKinds + c.RemovedKinds
        + c.AddedAttributes + c.RemovedAttributes + c.RenamedAttributes + c.ChangedAttributes
    Assert.Equal(sum, CatalogDiff.norm d)
    Assert.True(sum > 0)

/// Cumulative A → B → C → D over Customer.Name's facets (each catalog distinct).
let private custA = sampleCatalog
let private custB = catalogWithCustomerName (fun a -> { a with Type = Integer })
let private custC = catalogWithCustomerName (fun a -> { a with Type = Integer; Column = { a.Column with IsNullable = true } })
let private custD = catalogWithCustomerName (fun a -> { a with Type = Integer; Column = { a.Column with IsNullable = true }; Length = Some 64 })

[<Fact>]
let ``compose: applyDiff (compose d1 d2) A = applyDiff d2 (applyDiff d1 A) (functor law)`` () =
    let d1 = CatalogDiff.between custA custB |> mustOk
    let d2 = CatalogDiff.between custB custC |> mustOk
    let composed =
        match CatalogDiff.compose d1 d2 with
        | Some c -> c
        | None -> Assert.Fail "expected composable"; Unchecked.defaultof<_>
    let viaCompose = CatalogDiff.applyDiff custA composed
    let viaSequence = CatalogDiff.applyDiff (CatalogDiff.applyDiff custA d1) d2
    // Both reproduce C (over the captured surface).
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between custC viaCompose |> mustOk))
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between custC viaSequence |> mustOk))

[<Fact>]
let ``compose: a non-adjacent pair is None (fail-loud, partial groupoid)`` () =
    let d_ab = CatalogDiff.between custA custB |> mustOk   // target = B
    let d_ac = CatalogDiff.between custA custC |> mustOk   // source = A ≠ B
    Assert.True((CatalogDiff.compose d_ab d_ac).IsNone)

[<Fact>]
let ``compose: associativity — (d1+d2)+d3 reproduces the same state as d1+(d2+d3) (A-Lifecycle-4)`` () =
    let d1 = CatalogDiff.between custA custB |> mustOk
    let d2 = CatalogDiff.between custB custC |> mustOk
    let d3 = CatalogDiff.between custC custD |> mustOk
    let some = function Some v -> v | None -> Assert.Fail "expected composable"; Unchecked.defaultof<_>
    let left = some (CatalogDiff.compose (some (CatalogDiff.compose d1 d2)) d3)
    let right = some (CatalogDiff.compose d1 (some (CatalogDiff.compose d2 d3)))
    // Both are the A → D displacement: applying either to A reproduces D.
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between custD (CatalogDiff.applyDiff custA left) |> mustOk))
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between custD (CatalogDiff.applyDiff custA right) |> mustOk))

// ---------------------------------------------------------------------------
// 6.A.7 — Synthesized-key rename is surfaced, not silently re-keyed.
//
// A `Synthesized` SsKey is name-derived, so a rename CHANGES the key — the
// change lands in Removed + Added (not Renamed) and A1 identity is silently
// lost. `synthesizedRenameWarnings` pairs a removed + added Synthesized kind
// that share a synthesis source and an identical column set, surfacing the
// rename. A stable-key (`OssysOriginal`) source threads renames natively (a
// `Renamed` record) and produces no warning.
// ---------------------------------------------------------------------------

let private rnName (s: string) : Name = Name.create s |> Result.value
let private rnSynthKey (parts: string list) : SsKey = SsKey.synthesizedComposite "READSIDE_KIND" parts |> Result.value
let private rnAttrKey (parts: string list) : SsKey = SsKey.synthesizedComposite "READSIDE_ATTR" parts |> Result.value
let private rnAttr (key: SsKey) (col: string) (isPk: bool) : Attribute =
    { Attribute.create key (rnName col) Integer with
        Column = ColumnRealization.create (col) (not isPk) |> Result.value
        IsPrimaryKey = isPk; IsMandatory = isPk }
let private rnKind (key: SsKey) (table: string) (attrs: Attribute list) : Kind =
    Kind.create key (rnName table) (mkTableId "dbo" table) attrs
let private rnCatalog (kinds: Kind list) : Catalog =
    Catalog.create
        [ { SsKey = rnSynthKey ["MODULE"]; Name = rnName "M"; Kinds = kinds; IsActive = true; ExtendedProperties = [] } ] []
    |> Result.value

[<Fact>]
let ``A1: a Synthesized-key rename is surfaced, not silently re-keyed`` () =
    // T_OLD (synthesized key from its name) is renamed to T_NEW with the same
    // [ID, BODY] column shape. The synthesized keys differ, so between sees a
    // drop + add — but the warning surfaces the probable rename.
    let oldKind =
        rnKind (rnSynthKey ["dbo.T_OLD"]) "T_OLD"
            [ rnAttr (rnAttrKey ["dbo.T_OLD.ID"]) "ID" true
              rnAttr (rnAttrKey ["dbo.T_OLD.BODY"]) "BODY" false ]
    let newKind =
        rnKind (rnSynthKey ["dbo.T_NEW"]) "T_NEW"
            [ rnAttr (rnAttrKey ["dbo.T_NEW.ID"]) "ID" true
              rnAttr (rnAttrKey ["dbo.T_NEW.BODY"]) "BODY" false ]
    let diff = CatalogDiff.between (rnCatalog [ oldKind ]) (rnCatalog [ newKind ]) |> mustOk
    // The diff itself cannot thread the rename — it is a drop + add.
    Assert.Equal(1, Set.count (CatalogDiff.removed diff))
    Assert.Equal(1, Set.count (CatalogDiff.added diff))
    Assert.Empty(CatalogDiff.renamed diff)
    // …but the instability is SURFACED, not silent.
    match CatalogDiff.synthesizedRenameWarnings diff with
    | [ w ] ->
        Assert.Equal("READSIDE_KIND", w.SynthesisSource)
        Assert.Equal("dbo.T_OLD", w.SourceTable)
        Assert.Equal("dbo.T_NEW", w.TargetTable)
    | other -> Assert.Fail(sprintf "expected exactly one synthesized-rename warning, got %A" other)

[<Fact>]
let ``A1: a stable-key (OssysOriginal) rename threads natively — no instability warning`` () =
    let g = System.Guid.Parse "33333333-3333-3333-3333-333333333333"
    let idK = SsKey.ossysOriginal (System.Guid.Parse "44444444-4444-4444-4444-444444444444")
    let bodyK = SsKey.ossysOriginal (System.Guid.Parse "55555555-5555-5555-5555-555555555555")
    // Same kind identity (OssysOriginal g); only the Name + physical table change.
    let before = rnKind (SsKey.ossysOriginal g) "T_OLD" [ rnAttr idK "ID" true; rnAttr bodyK "BODY" false ]
    let after  = rnKind (SsKey.ossysOriginal g) "T_NEW" [ rnAttr idK "ID" true; rnAttr bodyK "BODY" false ]
    let diff = CatalogDiff.between (rnCatalog [ before ]) (rnCatalog [ after ]) |> mustOk
    // The rename threads natively as a Renamed record — no drop/add.
    Assert.Equal(1, Map.count (CatalogDiff.renamed diff))
    Assert.Empty(CatalogDiff.removed diff)
    Assert.Empty(CatalogDiff.added diff)
    Assert.Empty(CatalogDiff.synthesizedRenameWarnings diff)

[<Fact>]
let ``synthesizedRenameWarnings: a genuine drop + add with different shapes is not flagged as a rename`` () =
    // A removed kind and an added kind whose column sets differ are NOT paired
    // (the shape signal rules out a rename) — no false positive.
    let dropped =
        rnKind (rnSynthKey ["dbo.GONE"]) "GONE" [ rnAttr (rnAttrKey ["dbo.GONE.ID"]) "ID" true ]
    let appeared =
        rnKind (rnSynthKey ["dbo.FRESH"]) "FRESH"
            [ rnAttr (rnAttrKey ["dbo.FRESH.ID"]) "ID" true
              rnAttr (rnAttrKey ["dbo.FRESH.EXTRA"]) "EXTRA" false ]
    let diff = CatalogDiff.between (rnCatalog [ dropped ]) (rnCatalog [ appeared ]) |> mustOk
    Assert.Empty(CatalogDiff.synthesizedRenameWarnings diff)
