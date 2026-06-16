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
    let diff = CatalogDiff.between a a
    Assert.Equal<Set<SsKey>>(kindKeys a, CatalogDiff.unchanged diff)
    Assert.Empty(CatalogDiff.added diff)
    Assert.Empty(CatalogDiff.removed diff)
    Assert.Empty(CatalogDiff.renamed diff)
    Assert.True(CatalogDiff.isEmpty diff)

[<Fact>]
let ``CatalogDiff.between empty source vs target — every target key in Added`` () =
    let target = sampleCatalog
    let empty = Catalog.create [] [] |> Result.value
    let diff = CatalogDiff.between empty target
    Assert.Equal<Set<SsKey>>(kindKeys target, CatalogDiff.added diff)
    Assert.Empty(CatalogDiff.removed diff)
    Assert.Empty(CatalogDiff.unchanged diff)
    Assert.Empty(CatalogDiff.renamed diff)
    Assert.False(CatalogDiff.isEmpty diff)

[<Fact>]
let ``CatalogDiff.between source vs empty target — every source key in Removed`` () =
    let source = sampleCatalog
    let empty = Catalog.create [] [] |> Result.value
    let diff = CatalogDiff.between source empty
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
    let diff = CatalogDiff.between a b
    let expected =
        let aKeys = kindKeys a
        let bKeys = kindKeys b
        Set.union aKeys bKeys
    Assert.Equal<Set<SsKey>>(expected, CatalogDiff.scope diff)

[<Fact>]
let ``CatalogDiff partitions are pairwise disjoint`` () =
    let diff = CatalogDiff.between sampleCatalog sampleCatalog
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
        [ for _ in 1 .. 10 -> CatalogDiff.between a b ]
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
    let diff = CatalogDiff.between original permuted
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
    let diff = CatalogDiff.between sampleCatalog target

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
        Assert.Equal(1, List.length ad.Reshaped)
        let change = List.head ad.Reshaped
        Assert.Equal(customerNameKey, change.AttributeKey)
        Assert.Contains(AttributeFacet.DataType, change.Facets)

// ---------------------------------------------------------------------------
// NM-17 — the KindFacet diff channel. A kind's OWN facets (modality / triggers
// / CHECKs / activation) are now a real diff channel, retiring the NM-16
// "unreflected" tolerances: the change is reflected, not erased.
// ---------------------------------------------------------------------------

/// Rebuild `sampleCatalog` with the Customer KIND itself transformed.
let private catalogWithCustomerKind (f: Kind -> Kind) : Catalog =
    let m = { salesModule with Kinds = [ f customer; order; country ] }
    Catalog.create [ m ] [] |> Result.value

[<Fact>]
let ``NM-17: a kind IsActive flip surfaces a KindFacet change (kind stays Unchanged; isEmpty honest)`` () =
    let target = catalogWithCustomerKind (fun k -> { k with IsActive = not k.IsActive })
    let diff = CatalogDiff.between sampleCatalog target
    // Kind-level partition preserved: name stable → Unchanged.
    Assert.Contains(customerKey, CatalogDiff.unchanged diff)
    // The kind-facet change is visible and isEmpty is honest (NM-16 erasure closed).
    Assert.False(CatalogDiff.isEmpty diff)
    match CatalogDiff.kindFacetDiffOf customerKey diff with
    | None -> Assert.Fail "expected a KindFacet diff for Customer"
    | Some facets -> Assert.Contains(KindFacet.IsActive, facets)
    // One move on the kind channel (norm = 0 ⟺ isEmpty).
    Assert.Equal(1, CatalogDiff.norm diff)

[<Fact>]
let ``NM-17: a kind Modality change surfaces the Modality facet`` () =
    let target = catalogWithCustomerKind (fun k -> { k with Modality = [ ModalityMark.SystemOwned ] })
    let diff = CatalogDiff.between sampleCatalog target
    match CatalogDiff.kindFacetDiffOf customerKey diff with
    | None -> Assert.Fail "expected a KindFacet diff for Customer"
    | Some facets -> Assert.Contains(KindFacet.Modality, facets)

[<Fact>]
let ``NM-17: applyDiff round-trips a kind-facet change (between B (applyDiff (between A B) A) is empty)`` () =
    let a = sampleCatalog
    let b = catalogWithCustomerKind (fun k -> { k with IsActive = not k.IsActive; Modality = [ ModalityMark.SystemOwned ] })
    let d = CatalogDiff.between a b
    let reconstructed = CatalogDiff.applyDiff a d
    // The round-trip law holds on the kind-facet channel (the NM-17 fixture).
    Assert.True(CatalogDiff.between b reconstructed |> CatalogDiff.isEmpty)

[<Fact>]
let ``CatalogDiff: a column widening (length change) names the Length facet (TEXT -> NVARCHAR(256))`` () =
    // The audit's headline scenario: type category stable, declared length
    // changes. Length None -> Some 256 is the IR shape of TEXT -> NVARCHAR(256).
    let target = catalogWithCustomerName (fun a -> { a with Length = Some 256 })
    let diff = CatalogDiff.between sampleCatalog target
    let ad = (CatalogDiff.attributeDiffOf customerKey diff).Value
    let change = List.head ad.Reshaped
    Assert.Contains(AttributeFacet.Length, change.Facets)
    Assert.DoesNotContain(AttributeFacet.DataType, change.Facets)

[<Fact>]
let ``CatalogDiff: a nullability change names the Nullability facet`` () =
    let target =
        catalogWithCustomerName (fun a ->
            { a with Column = { a.Column with IsNullable = true } })
    let diff = CatalogDiff.between sampleCatalog target
    let ad = (CatalogDiff.attributeDiffOf customerKey diff).Value
    Assert.Contains(AttributeFacet.Nullability, (List.head ad.Reshaped).Facets)

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
    let diff = CatalogDiff.between sampleCatalog target
    let ad = (CatalogDiff.attributeDiffOf customerKey diff).Value
    Assert.Contains(customerTenantKey, ad.Removed)
    Assert.False(CatalogDiff.isEmpty diff)

[<Fact>]
let ``CatalogDiff: identical catalogs carry no AttributeDiffs (empty diff is honest)`` () =
    let diff = CatalogDiff.between sampleCatalog sampleCatalog
    Assert.True(Map.isEmpty (CatalogDiff.attributeDiffs diff))
    Assert.True(CatalogDiff.isEmpty diff)

[<Fact>]
let ``T1: attribute-level diff is deterministic across repeat invocations`` () =
    let target = catalogWithCustomerName (fun a -> { a with Type = Integer; Length = Some 64 })
    let runs = [ for _ in 1 .. 10 -> CatalogDiff.attributeDiffs (CatalogDiff.between sampleCatalog target) ]
    let head = List.head runs
    Assert.All(runs, fun r -> Assert.Equal<Map<SsKey, AttributeDiff>>(head, r))

[<Fact>]
let ``S9.19: a DECIMAL(10,2) -> DECIMAL(18,4) change names BOTH the Precision and Scale facets`` () =
    // A DECIMAL widening moves two facets at once: Precision 10 -> 18 AND
    // Scale 2 -> 4, with DataType unchanged. The facet set must carry BOTH —
    // a body that compares only DataType yields an empty facet set (so the
    // attribute would be Unchanged, never Changed), and a body that conflates
    // precision and scale into one comparison would name only one of them.
    let target =
        catalogWithCustomerName (fun a ->
            { a with Precision = Some 18; Scale = Some 4 })
    let source =
        catalogWithCustomerName (fun a ->
            { a with Precision = Some 10; Scale = Some 2 })
    let diff = CatalogDiff.between source target
    let ad = (CatalogDiff.attributeDiffOf customerKey diff).Value
    let change = List.head ad.Reshaped
    Assert.Equal(customerNameKey, change.AttributeKey)
    Assert.Contains(AttributeFacet.Precision, change.Facets)
    Assert.Contains(AttributeFacet.Scale, change.Facets)
    Assert.False(Set.isEmpty change.Facets)   // discriminates a DataType-only body
    Assert.DoesNotContain(AttributeFacet.DataType, change.Facets)

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

    let diff = CatalogDiff.between a b
    let reconstructed = CatalogDiff.applyDiff a diff

    // The round-trip law: the reconstruction has NO diff against B over the
    // captured surface (kinds + names + attribute presence/name/facets).
    let residual = CatalogDiff.between b reconstructed
    Assert.True(CatalogDiff.isEmpty residual, "applyDiff (between A B) A must reproduce B (residual diff was non-empty)")

[<Fact>]
let ``applyDiff (between A A) A = A — the identity diff is identity`` () =
    let a = sampleCatalog
    let diff = CatalogDiff.between a a
    let reconstructed = CatalogDiff.applyDiff a diff
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between a reconstructed))

[<Fact>]
let ``applyDiff: an in-place column type change reconstructs the exact target attribute`` () =
    // A direct structural check (no ordering ambiguity — same kinds, same
    // order, one patched facet): the reconstructed Customer.Name attribute
    // equals B's.
    let a = sampleCatalog
    let b = catalogWithCustomerName (fun at -> { at with Type = Integer })
    let reconstructed = CatalogDiff.applyDiff a (CatalogDiff.between a b)
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
    let diff = CatalogDiff.between a b

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
    let reconstructed = CatalogDiff.applyDiff a (CatalogDiff.between a b)
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
    let d = CatalogDiff.between sampleCatalog sampleCatalog
    Assert.Equal(0, CatalogDiff.norm d)
    Assert.True(CatalogDiff.isEmpty d)

[<Fact>]
let ``norm: a non-empty diff has norm > 0 (norm d = 0 iff isEmpty d)`` () =
    let target = catalogWithCustomerName (fun a -> { a with Type = Integer })
    let d = CatalogDiff.between sampleCatalog target
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
    let d = CatalogDiff.between sampleCatalog target
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
    let d1 = CatalogDiff.between custA custB
    let d2 = CatalogDiff.between custB custC
    let composed =
        match CatalogDiff.compose d1 d2 with
        | Some c -> c
        | None -> Assert.Fail "expected composable"; Unchecked.defaultof<_>
    let viaCompose = CatalogDiff.applyDiff custA composed
    let viaSequence = CatalogDiff.applyDiff (CatalogDiff.applyDiff custA d1) d2
    // Both reproduce C (over the captured surface).
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between custC viaCompose))
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between custC viaSequence))

[<Fact>]
let ``compose: a non-adjacent pair is None (fail-loud, partial groupoid)`` () =
    let d_ab = CatalogDiff.between custA custB   // target = B
    let d_ac = CatalogDiff.between custA custC   // source = A ≠ B
    Assert.True((CatalogDiff.compose d_ab d_ac).IsNone)

[<Fact>]
let ``compose: associativity — (d1+d2)+d3 reproduces the same state as d1+(d2+d3) (A-Lifecycle-4)`` () =
    let d1 = CatalogDiff.between custA custB
    let d2 = CatalogDiff.between custB custC
    let d3 = CatalogDiff.between custC custD
    let some = function Some v -> v | None -> Assert.Fail "expected composable"; Unchecked.defaultof<_>
    let left = some (CatalogDiff.compose (some (CatalogDiff.compose d1 d2)) d3)
    let right = some (CatalogDiff.compose d1 (some (CatalogDiff.compose d2 d3)))
    // Both are the A → D displacement: applying either to A reproduces D.
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between custD (CatalogDiff.applyDiff custA left)))
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between custD (CatalogDiff.applyDiff custA right)))

// ---------------------------------------------------------------------------
// M11 / M12 — the norm is a metric; the inverse closes the groupoid.
//
// `compose d1 d2` (for adjacent d1 = between A B, d2 = between B C) is the net
// displacement `between A C`. The triangle inequality `‖compose d1 d2‖ ≤
// ‖d1‖ + ‖d2‖` makes `norm` a genuine metric on catalogs: the net distance
// A → C never exceeds the path A → B → C. Equality holds when the two legs
// touch disjoint channels (nothing to cancel); the inequality is STRICT when a
// channel churns and cancels (A → B adds, B → C removes the same thing, so the
// net is silent on that channel while the path paid for both moves).
//
// `inverse d = between (target d) (source d)` is the displacement that returns
// target to source. It witnesses rollback (applying it to `target d`
// reproduces `source d`) and closes the partial groupoid (`compose d
// (inverse d)` is the identity at `source d`).
// ---------------------------------------------------------------------------

/// `sampleCatalog` with a NEW `Loyalty` attribute appended to Customer — the
/// churn channel: present in one rung of an A → B → C chain, absent in the
/// others, so the path pays an Add + a Remove while the net cancels to zero.
let private customerWithLoyalty : Catalog =
    let loyaltyKey = attrKey ["Customer"; "Loyalty"]
    let customer' =
        { customer with
            Attributes =
                customer.Attributes
                @ [ { Attribute.create loyaltyKey (nm "Loyalty") Integer with
                        Column = ColumnRealization.create ("LOYALTY") (true) |> Result.value } ] }
    catalogOfKinds [ customer'; order; country ]

[<Fact>]
let ``M11 (triangle inequality): ‖compose d1 d2‖ ≤ ‖d1‖ + ‖d2‖ — norm is a metric`` () =
    // For adjacent d1 = between A B, d2 = between B C, compose is ALWAYS Some
    // (target d1 = B = source d2), and compose d1 d2 = between A C.
    let composedNorm (a: Catalog) (b: Catalog) (c: Catalog) : int * int =
        let d1 = CatalogDiff.between a b
        let d2 = CatalogDiff.between b c
        let net =
            match CatalogDiff.compose d1 d2 with
            | Some net -> net
            | None -> Assert.Fail "adjacent diffs must compose (target d1 = source d2)"; Unchecked.defaultof<_>
        CatalogDiff.norm net, CatalogDiff.norm d1 + CatalogDiff.norm d2

    // (1) STRICT — the churn-cancel case. A → B ADDS Customer.Loyalty; B → C
    // REMOVES it. The net A → C is the empty displacement (C = A), so its norm
    // is 0, while the path paid for the add AND the remove (sum ≥ 2). The net
    // is strictly below the path-sum: the metric is not merely additive.
    let netCancel, pathCancel = composedNorm sampleCatalog customerWithLoyalty sampleCatalog
    Assert.Equal(0, netCancel)
    Assert.True(pathCancel >= 2, sprintf "churn path-sum should be ≥ 2, was %d" pathCancel)
    Assert.True(netCancel < pathCancel, "the cancel case must be a STRICT inequality")

    // (2) EQUALITY — two legs over DISJOINT channels. A → B changes Customer.Name
    // (one Changed attribute); B → C additionally flips Customer.Id nullability
    // (a second, distinct Changed attribute). The net A → C carries BOTH changed
    // attributes, so its norm equals the path-sum — nothing cancels.
    let b2 = catalogWithCustomerName (fun a -> { a with Type = Integer })
    let c2 =
        let customer' =
            { customer with
                Attributes =
                    customer.Attributes
                    |> List.map (fun at ->
                        if at.SsKey = customerNameKey then { at with Type = Integer }
                        elif at.SsKey = customerIdAttrKey then { at with Column = { at.Column with IsNullable = true } }
                        else at) }
        catalogOfKinds [ customer'; order; country ]
    let netDisjoint, pathDisjoint = composedNorm sampleCatalog b2 c2
    Assert.Equal(pathDisjoint, netDisjoint)

    // The inequality holds (non-strictly) across both triples.
    Assert.True(netCancel <= pathCancel)
    Assert.True(netDisjoint <= pathDisjoint)

// A tiny menu of independent catalog mutations over `sampleCatalog`, so a
// generated (A, B, C) triple is adjacent by construction (d1 = between A B,
// d2 = between B C) yet rich enough to exercise churn, cancellation, and
// disjoint-channel composition. Each bundle applies a subset of the menu.
type private Mutation =
    { RetypeName : bool          // Customer.Name : Text -> Integer
      NullableId : bool          // Customer.Id : not-null -> nullable
      DropTenant : bool          // drop Customer.TenantId
      AddLoyalty : bool          // append Customer.Loyalty
      RenameCustomer : bool      // kind rename Customer -> Client
      DropOrder : bool }         // drop the whole Order kind

let private applyMutation (m: Mutation) : Catalog =
    let loyaltyKey = attrKey ["Customer"; "Loyalty"]
    let attrs0 =
        customer.Attributes
        |> List.choose (fun at ->
            if m.DropTenant && at.SsKey = customerTenantKey then None
            elif at.SsKey = customerNameKey && m.RetypeName then Some { at with Type = Integer }
            elif at.SsKey = customerIdAttrKey && m.NullableId then Some { at with Column = { at.Column with IsNullable = true } }
            else Some at)
    let attrs =
        if m.AddLoyalty then
            attrs0
            @ [ { Attribute.create loyaltyKey (nm "Loyalty") Integer with
                    Column = ColumnRealization.create ("LOYALTY") (true) |> Result.value } ]
        else attrs0
    let customer' =
        { customer with
            Name = (if m.RenameCustomer then nm "Client" else customer.Name)
            Attributes = attrs }
    let kinds = if m.DropOrder then [ customer'; country ] else [ customer'; order; country ]
    catalogOfKinds kinds

let private genMutation : Gen<Mutation> =
    gen {
        let! b1 = Arb.generate<bool>
        let! b2 = Arb.generate<bool>
        let! b3 = Arb.generate<bool>
        let! b4 = Arb.generate<bool>
        let! b5 = Arb.generate<bool>
        let! b6 = Arb.generate<bool>
        return { RetypeName = b1; NullableId = b2; DropTenant = b3; AddLoyalty = b4; RenameCustomer = b5; DropOrder = b6 }
    }

/// An adjacent triple (A, B, C): A is fixed (`sampleCatalog`), B and C are two
/// independent mutation bundles. d1 = between A B and d2 = between B C are
/// adjacent by construction, so compose is always defined.
let private genAdjacentTriple : Gen<Catalog * Catalog * Catalog> =
    gen {
        let! mb = genMutation
        let! mc = genMutation
        return sampleCatalog, applyMutation mb, applyMutation mc
    }

[<Fact>]
let ``M11 (triangle inequality, swept): ‖between A C‖ ≤ ‖between A B‖ + ‖between B C‖ over adjacent triples`` () =
    Prop.forAll (Arb.fromGen genAdjacentTriple) (fun (a, b, c) ->
        let d1 = CatalogDiff.between a b
        let d2 = CatalogDiff.between b c
        match CatalogDiff.compose d1 d2 with
        | None -> false   // adjacency is structural here — None would be a real bug
        | Some net -> CatalogDiff.norm net <= CatalogDiff.norm d1 + CatalogDiff.norm d2)
    |> Check.QuickThrowOnFailure

[<Fact>]
let ``M12 (groupoid inverse): applyDiff (inverse d) (target d) reproduces source — the rollback witness`` () =
    // A non-vacuous displacement: custA -> custB flips Customer.Name's type, so
    // `d` is not the empty diff (its norm is ≥ 1).
    let d = CatalogDiff.between custA custB
    Assert.True(CatalogDiff.norm d >= 1, "the fixture displacement must be non-empty")
    let inv = CatalogDiff.inverse d
    // inverse runs target -> source: applying it to `target d` (= custB) must
    // reproduce `source d` (= custA), witnessed order-insensitively by an empty
    // residual diff against the source over the captured surface.
    let rolledBack = CatalogDiff.applyDiff (CatalogDiff.target d) inv
    Assert.True(
        CatalogDiff.isEmpty (CatalogDiff.between (CatalogDiff.source d) rolledBack),
        "applyDiff (inverse d) (target d) must reproduce source d (rollback residual was non-empty)")

[<Fact>]
let ``M12 (groupoid law): compose d (inverse d) = identity at source`` () =
    // d and inverse d are adjacent (target d = source (inverse d)), so compose
    // is defined; the round trip source -> target -> source is the identity at
    // source — its norm is 0 and it is the empty displacement.
    let d = CatalogDiff.between custA custB
    Assert.True(CatalogDiff.norm d >= 1, "the fixture displacement must be non-empty")
    let inv = CatalogDiff.inverse d
    Assert.Equal(Some true, CatalogDiff.compose d inv |> Option.map CatalogDiff.isEmpty)
    Assert.Equal(Some 0, CatalogDiff.compose d inv |> Option.map CatalogDiff.norm)

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
    let diff = CatalogDiff.between (rnCatalog [ oldKind ]) (rnCatalog [ newKind ])
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
    let diff = CatalogDiff.between (rnCatalog [ before ]) (rnCatalog [ after ])
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
    let diff = CatalogDiff.between (rnCatalog [ dropped ]) (rnCatalog [ appeared ])
    Assert.Empty(CatalogDiff.synthesizedRenameWarnings diff)

// ---------------------------------------------------------------------------
// S1.2 — a name collision across distinct SsKeys is a drop + add, NOT a
// rename. `between` matches kinds by SsKey, never by Name; a removed K1 and an
// added K2 that happen to share the human Name "Foo" must land in Removed +
// Added with an empty Renamed. (Renamed is reserved for a SAME-SsKey Name
// change — the orthogonal case.)
// ---------------------------------------------------------------------------

[<Fact>]
let ``S1.2: a Name collision across distinct SsKeys is drop + add, not a rename`` () =
    // A: kind K1 named "Foo"; B drops K1 and adds K2 (a DIFFERENT SsKey) also
    // named "Foo". A Name-matching rename-detector would pair K1/K2 into Renamed
    // and leave Removed/Added empty — exactly the bug this discriminates.
    let k1Key = kindKey [ "Foo1" ]
    let k2Key = kindKey [ "Foo2" ]
    let mkFoo (key: SsKey) (table: string) : Kind =
        Kind.create key (nm "Foo") (mkTableId "dbo" table)
            [ { Attribute.create (attrKey [ table; "Id" ]) (nm "Id") Integer with
                  Column = ColumnRealization.create "ID" false |> Result.value
                  IsPrimaryKey = true } ]
    let a = catalogOfKinds [ mkFoo k1Key "T_FOO1" ]
    let b = catalogOfKinds [ mkFoo k2Key "T_FOO2" ]
    let diff = CatalogDiff.between a b
    Assert.Contains(k1Key, CatalogDiff.removed diff)
    Assert.Contains(k2Key, CatalogDiff.added diff)
    Assert.Empty(CatalogDiff.renamed diff)

// ---------------------------------------------------------------------------
// C1 (2026-06-02) — the widened captured surface. Before C1, `between` /
// `applyDiff` captured kind + attribute column-shape only; references,
// indexes, and sequences rode through `applyDiff` unchanged, so `migrate A B`
// silently no-op'd any FK / index / sequence change. These witness the
// round-trip law `applyDiff (between A B) A = B` on the three new channels,
// and that `isEmpty` / `norm` now see them.
// ---------------------------------------------------------------------------

/// `order` with its Customer reference stripped — the A-side for "an FK was added".
let private orderNoRef : Kind = { order with References = [] }

let private seqKey (s: string) : SsKey = kindKey [ "Seq"; s ]

let private orderNumberSeq : Sequence =
    Sequence.create (seqKey "OrderNumber") (nm "OrderNumber") "dbo" "bigint"
        (Some 1m) (Some 1m) (Some 1m) (Some 9999999999m) false SequenceCacheMode.Unspecified None
    |> Result.value

// -- Reference channel ------------------------------------------------------

[<Fact>]
let ``C1: between/applyDiff round-trips an added FK (reference channel)`` () =
    // A: Order has no FK; B: Order references Customer. Before C1 this diff
    // reported isEmpty = true (the FK was invisible).
    let a = catalogOfKinds [ customer; orderNoRef; country ]
    let b = catalogOfKinds [ customer; order; country ]
    let diff = CatalogDiff.between a b
    Assert.False(CatalogDiff.isEmpty diff)
    match CatalogDiff.referenceDiffOf orderKey diff with
    | None -> Assert.Fail "expected a ReferenceDiff for Order"
    | Some rd -> Assert.Contains(orderRefToCustomer, rd.Added)
    let reconstructed = CatalogDiff.applyDiff a diff
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between b reconstructed),
                "applyDiff must reproduce B's reference set")

[<Fact>]
let ``C1: an FK trust change (WITH NOCHECK) names the Trust facet and round-trips`` () =
    // The Decision/Schema-coupled facet: a deployed FK flips to WITH NOCHECK.
    // NM-12 — a WITH NOCHECK FK is a REAL constraint that is untrusted
    // (HasDbConstraint=true, IsConstraintTrusted=false); set both sides through
    // the sanctioned normalizer so neither enters the illegal quadrant. The
    // change remains a pure Trust flip.
    let orderTrusted =
        { order with
            References = order.References |> List.map (Reference.withConstraintState true true) }
    let a = catalogOfKinds [ customer; orderTrusted; country ]
    let orderUntrusted =
        { order with
            References = order.References |> List.map (Reference.withConstraintState true false) }
    let b = catalogOfKinds [ customer; orderUntrusted; country ]
    let diff = CatalogDiff.between a b
    let rd = (CatalogDiff.referenceDiffOf orderKey diff).Value
    let change = List.head rd.Reshaped
    Assert.Equal(orderRefToCustomer, change.ReferenceKey)
    Assert.Contains(ReferenceFacet.Trust, change.Facets)
    let reconstructed = CatalogDiff.applyDiff a diff
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between b reconstructed))

[<Fact>]
let ``C1: a dropped FK round-trips (reference channel Removed)`` () =
    let a = catalogOfKinds [ customer; order; country ]
    let b = catalogOfKinds [ customer; orderNoRef; country ]
    let diff = CatalogDiff.between a b
    Assert.Contains(orderRefToCustomer, (CatalogDiff.referenceDiffOf orderKey diff).Value.Removed)
    let reconstructed = CatalogDiff.applyDiff a diff
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between b reconstructed))

// -- Index channel ----------------------------------------------------------

let private customerEmailIdx : Index =
    { Index.create (idxKey [ "Customer"; "UX_Name" ]) (nm "UX_Customer_Name")
        (IndexColumn.ascendingList [ customerNameKey ]) with Uniqueness = Unique }

[<Fact>]
let ``C1: between/applyDiff round-trips an added UNIQUE index (index channel)`` () =
    let a = catalogOfKinds [ customer; order; country ]
    let customerIdx = { customer with Indexes = [ customerEmailIdx ] }
    let b = catalogOfKinds [ customerIdx; order; country ]
    let diff = CatalogDiff.between a b
    Assert.False(CatalogDiff.isEmpty diff)
    Assert.Contains(customerEmailIdx.SsKey, (CatalogDiff.indexDiffOf customerKey diff).Value.Added)
    let reconstructed = CatalogDiff.applyDiff a diff
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between b reconstructed))

[<Fact>]
let ``C1: an index uniqueness change names the Uniqueness facet and round-trips`` () =
    let a = catalogOfKinds [ { customer with Indexes = [ { customerEmailIdx with Uniqueness = NotUnique } ] }; order; country ]
    let b = catalogOfKinds [ { customer with Indexes = [ customerEmailIdx ] }; order; country ]
    let diff = CatalogDiff.between a b
    let change = List.head (CatalogDiff.indexDiffOf customerKey diff).Value.Reshaped
    Assert.Contains(IndexFacet.Uniqueness, change.Facets)
    let reconstructed = CatalogDiff.applyDiff a diff
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between b reconstructed))

[<Fact>]
let ``C1: an index option change (ALLOW_PAGE_LOCKS) names the Options facet and round-trips`` () =
    // Guards the default-substitution bomb: the Options facet must carry the
    // flipped flag through applyDiff, not silently inherit Index.create's true.
    let a = catalogOfKinds [ { customer with Indexes = [ customerEmailIdx ] }; order; country ]
    let b = catalogOfKinds [ { customer with Indexes = [ { customerEmailIdx with AllowPageLocks = false } ] }; order; country ]
    let diff = CatalogDiff.between a b
    Assert.Contains(IndexFacet.Options, (List.head (CatalogDiff.indexDiffOf customerKey diff).Value.Reshaped).Facets)
    let reconstructed = CatalogDiff.applyDiff a diff
    let rebuiltIdx =
        (Catalog.tryFindKind customerKey reconstructed).Value.Indexes
        |> List.find (fun i -> i.SsKey = customerEmailIdx.SsKey)
    Assert.False(rebuiltIdx.AllowPageLocks)   // the flag survived; no default-substitution
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between b reconstructed))

// -- Sequence channel -------------------------------------------------------

[<Fact>]
let ``C1: between/applyDiff round-trips an added sequence (sequence channel)`` () =
    let a = sampleCatalog
    let b = Catalog.create [ salesModule ] [ orderNumberSeq ] |> Result.value
    let diff = CatalogDiff.between a b
    Assert.False(CatalogDiff.isEmpty diff)
    Assert.Contains(orderNumberSeq.SsKey, (CatalogDiff.sequenceDiff diff).Added)
    let reconstructed = CatalogDiff.applyDiff a diff
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between b reconstructed))

[<Fact>]
let ``C1: a reshaped sequence (increment change) names the Increment facet and round-trips`` () =
    let a = Catalog.create [ salesModule ] [ orderNumberSeq ] |> Result.value
    let b = Catalog.create [ salesModule ] [ { orderNumberSeq with Increment = Some 10m } ] |> Result.value
    let diff = CatalogDiff.between a b
    Assert.Contains(SequenceFacet.Increment, (List.head (CatalogDiff.sequenceDiff diff).Reshaped).Facets)
    let reconstructed = CatalogDiff.applyDiff a diff
    Assert.True(CatalogDiff.isEmpty (CatalogDiff.between b reconstructed))

// -- The integrative witness + isEmpty/norm honesty -------------------------

[<Fact>]
let ``C1: applyDiff (between A B) A = B on the widened surface (FK + index + sequence at once)`` () =
    // B evolves A across all three new channels simultaneously, on top of an
    // attribute change — the full captured surface in one round-trip.
    let a = catalogOfKinds [ customer; orderNoRef; country ]
    let customer' = { customer with Indexes = [ customerEmailIdx ] }                 // + index
    let order'     = order                                                            // + FK (was stripped in A)
    let b0 = Catalog.create [ { salesModule with Kinds = [ customer'; order'; country ] } ] [ orderNumberSeq ] |> Result.value // + sequence
    let diff = CatalogDiff.between a b0
    let reconstructed = CatalogDiff.applyDiff a diff
    let residual = CatalogDiff.between b0 reconstructed
    Assert.True(CatalogDiff.isEmpty residual,
                "applyDiff (between A B) A must reproduce B across references + indexes + sequences")

[<Fact>]
let ``C1: isEmpty is honest — an added FK alone makes the diff non-empty (regression guard)`` () =
    // The exact pre-C1 bug: a kind whose name + attributes are stable but which
    // gained an FK reported isEmpty = true → no ALTER → silent skip.
    let a = catalogOfKinds [ customer; orderNoRef; country ]
    let b = catalogOfKinds [ customer; order; country ]
    Assert.False(CatalogDiff.isEmpty (CatalogDiff.between a b))

[<Fact>]
let ``C1: norm counts the reference / index / sequence channels`` () =
    let a = catalogOfKinds [ customer; orderNoRef; country ]
    let customer' = { customer with Indexes = [ customerEmailIdx ] }
    let b = Catalog.create [ { salesModule with Kinds = [ customer'; order; country ] } ] [ orderNumberSeq ] |> Result.value
    let c = CatalogDiff.channelCounts (CatalogDiff.between a b)
    Assert.Equal(1, c.AddedReferences)
    Assert.Equal(1, c.AddedIndexes)
    Assert.Equal(1, c.AddedSequences)
    // norm sums every channel including the three new ones (≥ 3 here).
    Assert.True(CatalogDiff.norm (CatalogDiff.between a b) >= 3)

// ---------------------------------------------------------------------------
// P1.4 / P1.5 / P1.6 — the no-cheat property on the three C1 channels. Each
// mirrors the kind-channel no-cheat test (~line 314): applyDiff must THREAD
// the passed-in base, not source the channel from `target(d)`. We build a diff
// A→B that touches one channel, then apply it to a base equal to A except it
// carries an EXTRA reference / index / sequence the diff never mentions; the
// extra element must SURVIVE. A body that sourced the channel from the recorded
// target would drop it (the target carries no such extra), so each test FAILS
// the `fun _ d -> target d` impl on its channel.
// ---------------------------------------------------------------------------

/// An extra FK on Customer (Customer → Country) the reference diff never names.
let private customerExtraRef : Reference =
    Reference.create (refKey [ "Customer"; "Country" ]) (nm "Country") customerNameKey countryKey

/// An extra index on Country the index diff never names.
let private countryExtraIdx : Index =
    { Index.create (idxKey [ "Country"; "UX_Code" ]) (nm "UX_Country_Code")
        (IndexColumn.ascendingList [ countryCodeKey ]) with Uniqueness = Unique }

/// An extra catalog-level sequence the sequence diff never names.
let private extraSeq : Sequence =
    Sequence.create (seqKey "Audit") (nm "AuditSeq") "dbo" "bigint"
        (Some 1m) (Some 1m) (Some 1m) (Some 9999999999m) false SequenceCacheMode.Unspecified None
    |> Result.value

[<Fact>]
let ``P1.4: applyDiff threads the passed-in base's references, not the recorded target (reference no-cheat)`` () =
    // Diff adds an FK on Order. Base carries an EXTRA FK on Customer (a kind the
    // reference diff never mentions) — it must survive the apply.
    let a = catalogOfKinds [ customer; orderNoRef; country ]
    let b = catalogOfKinds [ customer; order; country ]
    let diff = CatalogDiff.between a b

    let customerWithExtra = { customer with References = [ customerExtraRef ] }
    let baseWithExtra = catalogOfKinds [ customerWithExtra; orderNoRef; country ]
    let result = CatalogDiff.applyDiff baseWithExtra diff

    let customerRefs =
        (Catalog.tryFindKind customerKey result).Value.References
        |> List.map (fun r -> r.SsKey) |> Set.ofList
    Assert.Contains(customerExtraRef.SsKey, customerRefs)
    // The recorded target carries NO such ref — proves the result is threaded, not copied.
    let tgtCustomerRefs =
        (Catalog.tryFindKind customerKey (CatalogDiff.target diff)).Value.References
        |> List.map (fun r -> r.SsKey) |> Set.ofList
    Assert.DoesNotContain(customerExtraRef.SsKey, tgtCustomerRefs)

[<Fact>]
let ``P1.5: applyDiff threads the passed-in base's indexes, not the recorded target (index no-cheat)`` () =
    // Diff adds a UNIQUE index on Customer. Base carries an EXTRA index on
    // Country (a kind the index diff never mentions) — it must survive.
    let a = catalogOfKinds [ customer; order; country ]
    let b = catalogOfKinds [ { customer with Indexes = [ customerEmailIdx ] }; order; country ]
    let diff = CatalogDiff.between a b

    let countryWithExtra = { country with Indexes = [ countryExtraIdx ] }
    let baseWithExtra = catalogOfKinds [ customer; order; countryWithExtra ]
    let result = CatalogDiff.applyDiff baseWithExtra diff

    let countryIdxs =
        (Catalog.tryFindKind countryKey result).Value.Indexes
        |> List.map (fun i -> i.SsKey) |> Set.ofList
    Assert.Contains(countryExtraIdx.SsKey, countryIdxs)
    let tgtCountryIdxs =
        (Catalog.tryFindKind countryKey (CatalogDiff.target diff)).Value.Indexes
        |> List.map (fun i -> i.SsKey) |> Set.ofList
    Assert.DoesNotContain(countryExtraIdx.SsKey, tgtCountryIdxs)

[<Fact>]
let ``P1.6: applyDiff threads the passed-in base's sequences, not the recorded target (sequence no-cheat)`` () =
    // Diff adds a sequence. Base carries an EXTRA sequence the diff never
    // mentions — it must survive (sequences are catalog-level).
    let a = sampleCatalog
    let b = Catalog.create [ salesModule ] [ orderNumberSeq ] |> Result.value
    let diff = CatalogDiff.between a b

    let baseWithExtra = Catalog.create [ salesModule ] [ extraSeq ] |> Result.value
    let result = CatalogDiff.applyDiff baseWithExtra diff

    let seqKeys = result.Sequences |> List.map (fun s -> s.SsKey) |> Set.ofList
    Assert.Contains(extraSeq.SsKey, seqKeys)
    // The recorded target carries only orderNumberSeq, not the extra one.
    let tgtSeqKeys =
        (CatalogDiff.target diff).Sequences |> List.map (fun s -> s.SsKey) |> Set.ofList
    Assert.DoesNotContain(extraSeq.SsKey, tgtSeqKeys)

// ---------------------------------------------------------------------------
// P2.8 — the DECIMAL round-trip on the attribute facet channel. A→B is the
// DECIMAL(10,2) -> DECIMAL(18,4) change (Precision AND Scale move together);
// the round-trip residual `between B (applyDiff (between A B) A)` must be empty,
// witnessing that applyFacet threads BOTH facets end-to-end. A body that
// patched only one of the two facets would leave a residual diff (non-empty).
// ---------------------------------------------------------------------------

[<Fact>]
let ``P2.8: a DECIMAL(10,2) -> DECIMAL(18,4) change round-trips (precision + scale end-to-end)`` () =
    let a = catalogWithCustomerName (fun at -> { at with Precision = Some 10; Scale = Some 2 })
    let b = catalogWithCustomerName (fun at -> { at with Precision = Some 18; Scale = Some 4 })
    let residual = CatalogDiff.between b (CatalogDiff.applyDiff a (CatalogDiff.between a b))
    Assert.True(CatalogDiff.isEmpty residual,
                "applyDiff must reproduce B's DECIMAL precision + scale (round-trip residual was non-empty)")
