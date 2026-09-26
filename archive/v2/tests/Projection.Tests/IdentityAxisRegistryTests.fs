module Projection.Tests.IdentityAxisRegistryTests

// Chapter 5.13 slice identity-axis-closure — cross-axis registry
// view assembled from Core's `RegisteredTransforms.all` +
// `RegisteredDataTransforms.all` (Targets.Data) via the new
// `TransformRegistry.byDomain` + `TransformRegistry.byOverlayAxis`
// filters.
//
// Per pillar 9 + CUTOVER_READINESS_BRIEF axis 3 (IDENTITY): every
// transformation site that touches the User-FK reflow surface
// classifies through the registry. The filters compose across
// projects (UserFkReflowPass.registered in Core; the User-FK Sites
// in Migration / Bootstrap emitters in Targets.Data); this file
// asserts the cross-cutting view holds without a parallel aggregator.

open Xunit
open Projection.Core
open Projection.Targets.Data

/// The full cross-project metadata surface — Core + Data — that
/// any axis-specific view filters from.
let private allMetadata : RegisteredTransformMetadata list =
    RegisteredTransforms.all @ RegisteredDataTransforms.all

[<Fact>]
let ``5.13.identity-axis-closure: byDomain Identity returns the three identity-axis passes`` () =
    // Identity-domain entries:
    //   - canonicalizeIdentity (DataIntent — mechanical identity-form
    //     normalization at adapter→catalog boundary)
    //   - namingMorphism (DataIntent — morphism IS data; pass
    //     applies it mechanically)
    //   - userFkReflow (OperatorIntent Selection — operator selects
    //     which references reroute via Policy.UserMatching)
    //
    // Migration + Bootstrap emitters are Data-domain (they consume
    // UserRemapContext via threading; the Identity domain is the
    // pass that produces the context).
    let identityEntries =
        TransformRegistry.byDomain Identity allMetadata
    let names =
        identityEntries
        |> List.map (fun rt -> rt.Name)
        |> List.sort
    Assert.Equal<string list>(
        [ "canonicalizeIdentity"; "namingMorphism"; "userFkReflow" ],
        names)

[<Fact>]
let ``5.13.identity-axis-closure: byOverlayAxis Insertion cross-cuts Data + Identity domains`` () =
    // Insertion fires on three Data-domain Sites (Migration
    // emitter's migrationRowEmission + userRemapRewrite;
    // Bootstrap emitter's userRemapBootstrap; the composer's
    // migrationContextThreading + userRemapContextThreading).
    // UserFkReflowPass is OperatorIntent Selection (not Insertion);
    // it does NOT appear under byOverlayAxis Insertion.
    let insertionEntries =
        TransformRegistry.byOverlayAxis Insertion allMetadata
    let names =
        insertionEntries
        |> List.map (fun rt -> rt.Name)
        |> Set.ofList
    Assert.Contains("migrationDependenciesEmitter", names)
    Assert.Contains("bootstrapEmitter", names)
    Assert.Contains("dataEmissionComposer", names)
    Assert.DoesNotContain("userFkReflow", names)

[<Fact>]
let ``5.13.identity-axis-closure: byOverlayAxis Selection includes UserFkReflowPass`` () =
    // UserFkReflowPass.registered's site classifies as
    // OperatorIntent Selection — the operator selects which
    // User-FK references reroute via Policy.UserMatching +
    // source/target populations.
    let selectionEntries =
        TransformRegistry.byOverlayAxis Selection allMetadata
    let names =
        selectionEntries
        |> List.map (fun rt -> rt.Name)
        |> Set.ofList
    Assert.Contains("userFkReflow", names)

[<Fact>]
let ``5.13.identity-axis-closure: byDomain Data includes both Core Data passes and Data emitters`` () =
    // Data-domain entries cross both Core (NullabilityPass +
    // CategoricalUniquenessPass + NormalizeStaticPopulations
    // produce Data-axis decisions) and the Targets.Data emitter
    // surfaces (StaticSeedsEmitter + MigrationDependenciesEmitter +
    // BootstrapEmitter + DataEmissionComposer). The four data-emission
    // entries from `RegisteredDataTransforms.all` must each appear in
    // the cross-project byDomain Data result.
    let dataNames =
        TransformRegistry.byDomain Data allMetadata
        |> List.map (fun rt -> rt.Name)
        |> Set.ofList
    Assert.Contains("staticSeedsEmitter", dataNames)
    Assert.Contains("migrationDependenciesEmitter", dataNames)
    Assert.Contains("bootstrapEmitter", dataNames)
    Assert.Contains("dataEmissionComposer", dataNames)
    Assert.Contains("nullability", dataNames)
    Assert.Contains("categoricalUniqueness", dataNames)
    Assert.Contains("normalizeStaticPopulations", dataNames)

[<Fact>]
let ``5.13.identity-axis-closure: byDomain partition is disjoint (each entry in exactly one domain)`` () =
    // Sanity invariant: every entry belongs to exactly one Domain
    // bucket. The filter applied across every Domain value must
    // sum to the total entry count.
    let domains = [ Schema; Data; Identity; Diagnostics; CutoverSafety; CrossCutting ]
    let bucketed =
        domains
        |> List.sumBy (fun d ->
            TransformRegistry.byDomain d allMetadata |> List.length)
    Assert.Equal(List.length allMetadata, bucketed)

[<Fact>]
let ``5.13.identity-axis-closure: full cross-project registry validates via TransformRegistry.create`` () =
    // The cross-project composition (Core + Data) must validate as
    // a complete registry — no duplicate Names, no empty Site
    // rationales, no NotImplementedInV2 with empty rationale.
    // This is the substrate the per-axis filters operate on.
    let result = TransformRegistry.create allMetadata
    Assert.True(
        Result.isSuccess result,
        sprintf "cross-project registry validation failed: %A" (Result.errors result))

[<Fact>]
let ``5.13.identity-axis-closure: byOverlayAxis Tightening lives in Schema + Data domains, not Identity`` () =
    // Tightening overlay fires across the four tightening passes:
    //   - NullabilityPass / CategoricalUniquenessPass → Domain Data
    //   - ForeignKeyPass / UniqueIndexPass → Domain Schema
    // No Identity-domain entry classifies as OperatorIntent
    // Tightening today. The cross-section pins that — accidentally
    // classifying an Identity-axis site as Tightening would surface
    // here.
    let tighteningInIdentity =
        allMetadata
        |> TransformRegistry.byDomain Identity
        |> TransformRegistry.byOverlayAxis Tightening
    Assert.Empty tighteningInIdentity

[<Fact>]
let ``5.13.identity-axis-closure: byDomain Identity ∩ byOverlayAxis Selection = UserFkReflowPass alone`` () =
    // The two filters compose at the consumer level — composing
    // them gives the "OperatorIntent-Selection IDENTITY-axis"
    // surface. UserFkReflowPass is the sole entry: the other two
    // Identity-domain passes (canonicalizeIdentity + namingMorphism)
    // are DataIntent and drop out via the OverlayAxis filter.
    let intersection =
        allMetadata
        |> TransformRegistry.byDomain Identity
        |> TransformRegistry.byOverlayAxis Selection
    Assert.Equal(1, List.length intersection)
    Assert.Equal("userFkReflow", intersection.Head.Name)
