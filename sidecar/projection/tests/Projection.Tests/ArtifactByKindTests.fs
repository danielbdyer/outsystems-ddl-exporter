module Projection.Tests.ArtifactByKindTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

/// FSharp.Core's two-arity `Result<'a, 'b>` case constructors collide
/// with `Projection.Core.DiagnosticSeverity.Error` once `Projection.Core`
/// is opened; qualifying via a private type alias forces case access
/// to resolve to FSharp.Core's Result.Ok / Result.Error without
/// shadowing the single-arity `Result<'a>.Error` case.
type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

/// Stage 0 (S0.B slice 5.1 per
/// `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md`) lands
/// `ArtifactByKind<'element>` with a private constructor and a smart
/// constructor enforcing strict equality between the slice's keyset
/// and `Catalog.allKinds`.
///
/// The strict-equal invariant produces two complementary error
/// variants — `KindNotProduced` (slice missing a kind), `UnexpectedKind`
/// (slice carries a key absent from the Catalog). Both surface bugs;
/// neither is benign. The tests below exercise both halves on a
/// minimal Catalog; the integration property
/// `T11: emitSlices key-set equals Catalog.allKinds` lands per emitter
/// at slices 5.2–5.4.

let private ssKey (s: string) : SsKey = testKey s

let private nm (s: string) : Name =
    match Name.create s with
    | Ok n -> n
    | Error errors ->
        failwithf "fixture: Name.create failed: %A" errors

let private mustOk (r: Result<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error e -> failwithf "fixture: ArtifactByKind.create failed: %A" e

let private mkKind (n: string) : Kind =
    IRBuilders.mkKind (ssKey (sprintf "OS_KIND_%s" n)) (nm n) { Schema = "dbo"; Table = sprintf "OSUSR_S1S_%s" (n.ToUpperInvariant()); Catalog = None } []

let private mkModule (n: string) (kinds: Kind list) : Module =
    IRBuilders.mkModule (ssKey (sprintf "OS_MOD_%s" n)) (nm n) kinds

let private mkCatalog (modules: Module list) : Catalog =
    IRBuilders.mkCatalog modules

let private emptyCatalog () : Catalog =
    mkCatalog []

let private singleKindCatalog (kindName: string) : Catalog * Kind =
    let kind = mkKind kindName
    let m = mkModule "M" [ kind ]
    mkCatalog [ m ], kind

[<Fact>]
let ``S0.B.1: create succeeds when slice keys exactly match Catalog.allKinds`` () =
    let catalog, kind = singleKindCatalog "Customer"
    let slices = Map.ofList [ kind.SsKey, "CREATE TABLE Customer ..." ]
    match ArtifactByKind.create catalog slices with
    | FsResult.Ok artifact ->
        let recovered = ArtifactByKind.toMap artifact
        Assert.Equal<Map<SsKey, string>>(slices, recovered)
    | FsResult.Error e ->
        Assert.Fail(sprintf "expected Ok, got DiagnosticSeverity.Error %A" e)

[<Fact>]
let ``S0.B.1: create succeeds on the empty Catalog with the empty slice map`` () =
    let catalog = emptyCatalog ()
    let slices = Map.empty<SsKey, string>
    match ArtifactByKind.create catalog slices with
    | FsResult.Ok artifact ->
        Assert.True(Map.isEmpty (ArtifactByKind.toMap artifact))
    | FsResult.Error e ->
        Assert.Fail(sprintf "expected Ok, got DiagnosticSeverity.Error %A" e)

[<Fact>]
let ``S0.B.1: create rejects missing kinds with KindNotProduced`` () =
    let catalog, _ = singleKindCatalog "Customer"
    let slices = Map.empty<SsKey, string>
    match ArtifactByKind.create catalog slices with
    | FsResult.Error (KindNotProduced k) ->
        Assert.Equal(ssKey "OS_KIND_Customer", k)
    | other ->
        Assert.Fail(sprintf "expected DiagnosticSeverity.Error (KindNotProduced ...), got %A" other)

[<Fact>]
let ``S0.B.1: create rejects extra keys with UnexpectedKind`` () =
    let catalog = emptyCatalog ()
    let stale = ssKey "OS_KIND_Stale"
    let slices = Map.ofList [ stale, "stale slice" ]
    match ArtifactByKind.create catalog slices with
    | FsResult.Error (UnexpectedKind k) ->
        Assert.Equal(stale, k)
    | other ->
        Assert.Fail(sprintf "expected DiagnosticSeverity.Error (UnexpectedKind ...), got %A" other)

[<Fact>]
let ``S0.B.1: create reports KindNotProduced first when both missing and extra are present`` () =
    let catalog, _ = singleKindCatalog "Customer"
    let stale = ssKey "OS_KIND_Stale"
    let slices = Map.ofList [ stale, "stale slice" ]
    match ArtifactByKind.create catalog slices with
    | FsResult.Error (KindNotProduced _) -> ()
    | other ->
        Assert.Fail(
            sprintf "expected DiagnosticSeverity.Error (KindNotProduced _) on overlapping miss+extra, got %A" other
        )

[<Fact>]
let ``S0.B.1: tryFind returns Some for present keys`` () =
    let catalog, kind = singleKindCatalog "Customer"
    let slices = Map.ofList [ kind.SsKey, "rendered" ]
    let artifact = ArtifactByKind.create catalog slices |> mustOk
    Assert.Equal(Some "rendered", ArtifactByKind.tryFind kind.SsKey artifact)

[<Fact>]
let ``S0.B.1: tryFind returns None for absent keys`` () =
    let catalog, kind = singleKindCatalog "Customer"
    let slices = Map.ofList [ kind.SsKey, "rendered" ]
    let artifact = ArtifactByKind.create catalog slices |> mustOk
    let other = ssKey "OS_KIND_Other"
    Assert.Equal(None, ArtifactByKind.tryFind other artifact)

[<Fact>]
let ``T11: ArtifactByKind.keys equals Catalog.allKinds SsKey set by construction`` () =
    let kindA = mkKind "A"
    let kindB = mkKind "B"
    let catalog = mkCatalog [ mkModule "M" [ kindA; kindB ] ]
    let slices =
        Map.ofList [ kindA.SsKey, "A"; kindB.SsKey, "B" ]
    let artifact = ArtifactByKind.create catalog slices |> mustOk
    let expected =
        Catalog.allKinds catalog
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.Equal<Set<SsKey>>(expected, ArtifactByKind.keys artifact)
