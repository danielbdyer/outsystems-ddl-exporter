module Projection.Tests.PassRegistrationsTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Chapter A.4.7 slice γ — pass `.registered` exports witnesses.
//
// Slice γ ships `.registered` (or `registered <config>` factory) for
// each of the 12 pass modules. These tests witness the registry
// surface is reachable and correctly populated for each pass:
//
//   - Category A (Catalog → Catalog, no config): CanonicalizeIdentity,
//     NormalizeStaticPopulations, SymmetricClosure.
//   - Category B (config + Catalog → Catalog factory): NamingMorphism
//     (Morphism), VisibilityMask (Mask).
//   - Category C (Catalog → TopologicalOrder with multi-site OverlayAxis.
//     Ordering): TopologicalOrderPass (default + configurable variant
//     `registeredWith`).
//   - Category D (Policy × Profile → Catalog → Lineage<Diagnostics<
//     DecisionSet>> factories): NullabilityPass, UniqueIndexPass,
//     ForeignKeyPass, CategoricalUniquenessPass.
//   - Category E (RenameSpec list factory with Result wrapping):
//     TableRename.
//   - Category F (Policy × Profile → Catalog → Lineage<Diagnostics<
//     UserRemapContext>> factory): UserFkReflowPass.
//
// Slice γ keeps `let run` public for each pass (parallel exposure
// during transition); slice γ.2 (future) makes them private + migrates
// consumers. The witnesses below exercise both the metadata surface
// (via RegisteredTransform.toMetadata) and the Run closure for the
// simply-invoked passes.
// ---------------------------------------------------------------------------

let private firstSite (rt: RegisteredTransform<'In, 'Out>) : TransformSite =
    Assert.NotEmpty rt.Sites
    rt.Sites.[0]

// ---------------------------------------------------------------------------
// Category A — Catalog → Catalog, no config.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice γ: CanonicalizeIdentity.registered carries Identity domain + Pass stage + DataIntent site`` () =
    let rt = CanonicalizeIdentity.registered
    Assert.Equal("canonicalizeIdentity", rt.Name)
    Assert.Equal(Identity, rt.Domain)
    Assert.Equal(Pass, rt.StageBinding)
    Assert.Equal(DataIntent, (firstSite rt).Classification)
    Assert.Equal(Active, rt.Status)

[<Fact>]
let ``A.4.7 slice γ: CanonicalizeIdentity.registered.Run wraps Catalog → Lineage<Diagnostics<Catalog>>`` () =
    let result = CanonicalizeIdentity.registered.Run sampleCatalog
    // Inner value is the canonicalized catalog; trail has Touched events.
    Assert.NotEmpty result.Trail
    Assert.NotEmpty result.Value.Value.Modules

[<Fact>]
let ``A.4.7 slice γ: NormalizeStaticPopulations.registered carries Data domain + DataIntent site`` () =
    let rt = NormalizeStaticPopulations.registered
    Assert.Equal(Data, rt.Domain)
    Assert.Equal(DataIntent, (firstSite rt).Classification)
    Assert.Equal(Active, rt.Status)

[<Fact>]
let ``A.4.7 slice γ: SymmetricClosure.registered carries Schema domain + DataIntent site`` () =
    let rt = SymmetricClosure.registered
    Assert.Equal(Schema, rt.Domain)
    Assert.Equal(DataIntent, (firstSite rt).Classification)
    Assert.Equal(Active, rt.Status)

// ---------------------------------------------------------------------------
// Category B — config + Catalog → Catalog factory.
// ---------------------------------------------------------------------------

let private mkName (s: string) : Name = Name.create s |> Result.value

[<Fact>]
let ``A.4.7 slice γ: NamingMorphism.registered factory produces DataIntent site`` () =
    let appendV (n: Name) : Name =
        Name.create (System.String.Concat (Name.value n, "_v")) |> Result.value
    let rt = NamingMorphism.registered appendV
    Assert.Equal(Identity, rt.Domain)
    Assert.Equal(DataIntent, (firstSite rt).Classification)
    // Run produces lineage with Renamed events.
    let result = rt.Run sampleCatalog
    Assert.NotEmpty result.Trail

[<Fact>]
let ``A.4.7 slice γ: VisibilityMask.registered factory produces OperatorIntent Selection site`` () =
    let mask : VisibilityMask.Mask = { Hide = [ VisibilityMask.hideOrigin Origin.OsNative ] }
    let rt = VisibilityMask.registered mask
    Assert.Equal(Schema, rt.Domain)
    Assert.Equal(OperatorIntent Selection, (firstSite rt).Classification)
    // Run produces lineage with Removed events.
    let result = rt.Run sampleCatalog
    Assert.NotEmpty result.Trail

// ---------------------------------------------------------------------------
// Category C — TopologicalOrderPass: multi-site (DataIntent + OperatorIntent
// Ordering) — chapter A.4.7 Q9-trigger-fires worked example.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice γ: TopologicalOrderPass.registered carries two sites (SortKahn DataIntent + SelfLoopHandling OperatorIntent Ordering)`` () =
    let rt = TopologicalOrderPass.registered
    Assert.Equal(CrossCutting, rt.Domain)
    Assert.Equal(Pass, rt.StageBinding)
    Assert.Equal(2, List.length rt.Sites)
    let sortSite = rt.Sites |> List.find (fun s -> s.SiteName = "sortKahn")
    Assert.Equal(DataIntent, sortSite.Classification)
    let selfLoopSite = rt.Sites |> List.find (fun s -> s.SiteName = "selfLoopHandling")
    Assert.Equal(OperatorIntent Ordering, selfLoopSite.Classification)

[<Fact>]
let ``A.4.7 slice γ: TopologicalOrderPass.registered.Run produces Lineage<Diagnostics<TopologicalOrder>>`` () =
    let result = TopologicalOrderPass.registered.Run sampleCatalog
    Assert.NotEmpty result.Trail

[<Fact>]
let ``A.4.7 slice γ: TopologicalOrderPass.registeredWith exposes SelfLoopPolicy configurability`` () =
    let rtSkip = TopologicalOrderPass.registeredWith SkipSelfEdges
    // Same metadata (Sites/Domain/etc.); only Run closure differs.
    Assert.Equal(TopologicalOrderPass.registered.Name, rtSkip.Name)
    Assert.Equal(TopologicalOrderPass.registered.Domain, rtSkip.Domain)
    Assert.Equal(2, List.length rtSkip.Sites)

// ---------------------------------------------------------------------------
// Category D — registered-intervention passes (Policy × Profile factories).
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice γ: NullabilityPass.registered factory carries OperatorIntent Tightening site`` () =
    let rt = NullabilityPass.registered Policy.empty Profile.empty
    Assert.Equal(Data, rt.Domain)
    Assert.Equal(OperatorIntent Tightening, (firstSite rt).Classification)
    Assert.Equal(Active, rt.Status)

[<Fact>]
let ``A.4.7 slice γ: UniqueIndexPass.registered factory carries OperatorIntent Tightening site`` () =
    let rt = UniqueIndexPass.registered Policy.empty Profile.empty
    Assert.Equal(Schema, rt.Domain)
    Assert.Equal(OperatorIntent Tightening, (firstSite rt).Classification)

[<Fact>]
let ``A.4.7 slice γ: ForeignKeyPass.registered factory carries OperatorIntent Tightening site`` () =
    let rt = ForeignKeyPass.registered Policy.empty Profile.empty
    Assert.Equal(Schema, rt.Domain)
    Assert.Equal(OperatorIntent Tightening, (firstSite rt).Classification)

[<Fact>]
let ``A.4.7 slice γ: CategoricalUniquenessPass.registered factory carries OperatorIntent Tightening site`` () =
    let rt = CategoricalUniquenessPass.registered Policy.empty Profile.empty
    Assert.Equal(Data, rt.Domain)
    Assert.Equal(OperatorIntent Tightening, (firstSite rt).Classification)

// ---------------------------------------------------------------------------
// Category E — TableRename: Result wrapping. On Error, the catalog
// passes through unchanged and the ValidationErrors surface as
// Diagnostics with Severity = Error.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice γ: TableRename.registered factory carries OperatorIntent Emission site`` () =
    let specs : TableRename.RenameSpec list = [
        { Key    = TableRename.Logical (mkName "Sales", mkName "Customer")
          Target = { Catalog = None; Schema = "renamed"; Table = "customer_v2" } }
    ]
    let rt = TableRename.registered specs
    Assert.Equal(Schema, rt.Domain)
    Assert.Equal(OperatorIntent Emission, (firstSite rt).Classification)

[<Fact>]
let ``A.4.7 slice γ: TableRename.registered.Run on Ok wraps lineage with empty Diagnostics`` () =
    let specs : TableRename.RenameSpec list = [
        { Key    = TableRename.Logical (mkName "Sales", mkName "Customer")
          Target = { Catalog = None; Schema = "renamed"; Table = "customer_v2" } }
    ]
    let result = TableRename.registered specs |> fun rt -> rt.Run sampleCatalog
    // Trail carries PhysicallyRenamed events; Diagnostics is empty.
    Assert.NotEmpty result.Trail
    Assert.True (Diagnostics.isClean result.Value)

[<Fact>]
let ``A.4.7 slice γ: TableRename.registered.Run on Error surfaces ValidationErrors as Diagnostics with Severity Error`` () =
    // Spec targets a kind that doesn't exist in the catalog.
    let specs : TableRename.RenameSpec list = [
        { Key    = TableRename.Logical (mkName "NotAModule", mkName "NotAKind")
          Target = { Catalog = None; Schema = "x"; Table = "y" } }
    ]
    let result = TableRename.registered specs |> fun rt -> rt.Run sampleCatalog
    // Trail is empty (the pass short-circuited on validation); diagnostics
    // carry the error entries.
    let errorEntries =
        Diagnostics.entriesAt DiagnosticSeverity.Error result.Value
    Assert.NotEmpty errorEntries
    // Catalog passes through unchanged.
    Assert.Equal(sampleCatalog, result.Value.Value)

// ---------------------------------------------------------------------------
// Category F — UserFkReflowPass.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice γ: UserFkReflowPass.registered factory carries OperatorIntent Selection site`` () =
    let rt = UserFkReflowPass.registered Policy.empty Profile.empty
    Assert.Equal(Identity, rt.Domain)
    Assert.Equal(OperatorIntent Selection, (firstSite rt).Classification)

// ---------------------------------------------------------------------------
// Metadata projection sanity — RegisteredTransform.toMetadata drops Run
// and preserves the rest for the registry's flat enumeration.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice γ: every registered transform projects to metadata with non-empty Sites + Rationale`` () =
    let allRegistered : RegisteredTransformMetadata list = [
        RegisteredTransform.toMetadata CanonicalizeIdentity.registered
        RegisteredTransform.toMetadata NormalizeStaticPopulations.registered
        RegisteredTransform.toMetadata SymmetricClosure.registered
        RegisteredTransform.toMetadata TopologicalOrderPass.registered
    ]
    for m in allRegistered do
        Assert.NotEmpty m.Sites
        for site in m.Sites do
            Assert.False (System.String.IsNullOrWhiteSpace site.Rationale)
