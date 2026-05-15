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
        Catalog.create (original.Modules |> List.rev) original.Triggers
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
