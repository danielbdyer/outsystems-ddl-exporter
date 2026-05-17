module Projection.Tests.RefactorLogEmitterTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// FSharp.Core's two-arity Result case constructors collide with
// `Projection.Core.DiagnosticSeverity.Error` once `Projection.Core`
// is opened; qualifying via a private alias mirrors the pattern at
// `ArtifactByKindTests.fs` and `CatalogDiffTests.fs`.
type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, 'b>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err ->
        Assert.Fail(sprintf "%A" err)
        Unchecked.defaultof<'a>

let private mustResultOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es ->
        Assert.Fail(sprintf "%A" es)
        Unchecked.defaultof<'a>

let private nameOf (s: string) : Name =
    Name.create s |> mustResultOk

// ---------------------------------------------------------------------------
// Build a "renamed" catalog by rewriting one kind's `Name` while
// preserving its `SsKey`. Per A1 (identity-survives-rename), renames
// preserve SsKey; this is the diff-rename scenario.
// ---------------------------------------------------------------------------

let private renamedCustomerKind : Kind =
    { customer with Name = nameOf "Patron" }

let private renamedSalesModule : Module =
    { salesModule with Kinds = [ renamedCustomerKind; order; country ] }

let private targetCatalog : Catalog =
    IRBuilders.mkCatalog [ renamedSalesModule ]

// ---------------------------------------------------------------------------
// Slice θ acceptance — RefactorLogEmitter realizes EmitterOverDiff
// <RefactorLogEntry list>; T11 (sibling-Π commutativity, structural
// type encoding, extended to diff-typed inputs) holds by construction.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T11 (diff-typed inputs): RefactorLogEmitter.emit key-set equals target Catalog.allKinds`` () =
    let diff = CatalogDiff.between sampleCatalog targetCatalog |> mustOk
    let expected =
        Catalog.allKinds targetCatalog
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    Assert.Equal<Set<SsKey>>(expected, ArtifactByKind.keys artifact)

[<Fact>]
let ``RefactorLogEmitter: identity diff produces empty entries for every kind`` () =
    let diff = CatalogDiff.between sampleCatalog sampleCatalog |> mustOk
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entries = ArtifactByKind.toMap artifact
    Assert.All(
        entries |> Map.toSeq |> Seq.map snd,
        fun list -> Assert.Empty(list))

[<Fact>]
let ``RefactorLogEmitter: one-rename diff produces one SqlTable entry on the renamed kind`` () =
    let diff = CatalogDiff.between sampleCatalog targetCatalog |> mustOk
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entries = ArtifactByKind.toMap artifact
    // Renamed kind: `customer` (SsKey unchanged; Name "Customer" → "Patron")
    let customerEntries = Map.find customerKey entries
    Assert.Equal(1, List.length customerEntries)
    let entry = List.head customerEntries
    Assert.Equal(RenameRefactor, entry.OperationKind)
    Assert.Equal(SqlTable, entry.ElementType)
    Assert.Equal(SqlSchema, entry.ParentElementType)
    Assert.Equal("[dbo].[OSUSR_S1S_CUSTOMER]", entry.ElementName)
    Assert.Equal("[dbo]", entry.ParentElementName)
    Assert.Equal("Patron", entry.NewName)
    Assert.Equal(RefactorLogEmitter.version, entry.PassVersion)

[<Fact>]
let ``RefactorLogEmitter: unrenamed kinds produce empty entries`` () =
    let diff = CatalogDiff.between sampleCatalog targetCatalog |> mustOk
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entries = ArtifactByKind.toMap artifact
    Assert.Empty(Map.find orderKey entries)
    Assert.Empty(Map.find countryKey entries)

// ---------------------------------------------------------------------------
// T1 — determinism. Same CatalogDiff produces the same OperationKey
// across repeat invocations. Per chapter 3.5 prescope §3, this is the
// load-bearing UUIDv5-derivation property: SSDT's GUI generates random
// GUIDs but DacFx accepts any stable Guid; V2 chooses determinism.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: RefactorLogEmitter produces stable OperationKey across repeat invocations`` () =
    let diff = CatalogDiff.between sampleCatalog targetCatalog |> mustOk
    let runs =
        [ for _ in 1 .. 10 -> RefactorLogEmitter.emit diff |> mustOk ]
    let head =
        runs
        |> List.head
        |> ArtifactByKind.toMap
        |> Map.find customerKey
        |> List.head
    Assert.All(
        runs,
        fun artifact ->
            let entry =
                artifact
                |> ArtifactByKind.toMap
                |> Map.find customerKey
                |> List.head
            Assert.Equal(head.OperationKey, entry.OperationKey))

[<Fact>]
let ``RefactorLogEmitter: OperationKey is UUIDv5 (version digit 5)`` () =
    let diff = CatalogDiff.between sampleCatalog targetCatalog |> mustOk
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entry =
        artifact
        |> ArtifactByKind.toMap
        |> Map.find customerKey
        |> List.head
    let dashedForm = entry.OperationKey.ToString("D")
    // Position 14 in the dashed form is the version digit.
    Assert.Equal('5', dashedForm.[14])

// ---------------------------------------------------------------------------
// Empty-diff edge cases (added / removed kinds produce no rename
// entries; the diff classifies them outside `Renamed`).
// ---------------------------------------------------------------------------

[<Fact>]
let ``RefactorLogEmitter: Added kind produces empty entries (it's a CREATE not a rename)`` () =
    let empty = Catalog.create [] [] |> mustResultOk
    let diff = CatalogDiff.between empty sampleCatalog |> mustOk
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entries = ArtifactByKind.toMap artifact
    Assert.All(
        entries |> Map.toSeq |> Seq.map snd,
        fun list -> Assert.Empty(list))

[<Fact>]
let ``RefactorLogEmitter: Removed kind produces no artifact entry (target is empty)`` () =
    let empty = Catalog.create [] [] |> mustResultOk
    let diff = CatalogDiff.between sampleCatalog empty |> mustOk
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    // Target catalog is empty; artifact's keyset is empty per T11.
    Assert.Empty(ArtifactByKind.keys artifact)
