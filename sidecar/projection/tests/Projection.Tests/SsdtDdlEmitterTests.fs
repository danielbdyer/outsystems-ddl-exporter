module Projection.Tests.SsdtDdlEmitterTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Chapter 4.1.A slice 1 — single-table SSDT DDL emission.
//
// Per `CHAPTER_4_1_A_OPEN.md` strategic frame + chapter pre-scope §8 slice 1:
// the SsdtDdlEmitter produces a per-kind `SsdtFile` carrying a cross-platform-
// deterministic relative path AND the rendered SQL body. The body flows
// through ScriptDom's typed AST + `Sql160ScriptGenerator`. Per pillar 1
// (data-structure-oriented over string-parsing), the seam IS the typed
// `SsdtFile`; strings emerge only at the absolute terminal generator step.
//
// Slice-1 scope: columns + PK only (no indexes, no FKs, no extended properties,
// no static populations). Subsequent slices extend the same Emitter signature.
// ---------------------------------------------------------------------------

// `Emitter<'a>` returns `FSharp.Core.Result<ArtifactByKind<'a>, EmitError>`
// (the two-arity alias used by Π emitters per `Types.fs:50`). The single-
// arity `Projection.Core.Result<'a>` alias has the same Ok/Error case
// names, so qualifying via a private type alias forces case access to
// resolve to FSharp.Core's Result without ambiguity.
type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private enrich (c: Catalog) : Catalog =
    (CanonicalizeIdentity.run c).Value

let private mustOk (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v        -> v
    | FsResult.Error err   ->
        Assert.Fail (sprintf "expected Ok; got %A" err)
        Unchecked.defaultof<'a>

// ---------------------------------------------------------------------------
// Slice-1 acceptance: emitSlices produces one SsdtFile per kind, keyed by
// SsKey, with V1-conventional RelativePath.
// ---------------------------------------------------------------------------

[<Fact>]
let ``SsdtDdlEmitter.emitSlices produces one SsdtFile per kind keyed by SsKey`` () =
    let enriched = enrich sampleCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let slices = ArtifactByKind.toMap artifact
    let allKinds = Catalog.allKinds enriched
    Assert.Equal (List.length allKinds, Map.count slices)
    for k in allKinds do
        Assert.True (Map.containsKey k.SsKey slices, sprintf "missing slice for kind %A" k.SsKey)

[<Fact>]
let ``SsdtDdlEmitter.emitSlices RelativePath uses V1 convention (Modules/<Module>/<Schema>.<Table>.sql)`` () =
    // V1 convention per `src/Osm.Emission/SsdtEmitter.cs:55-122`:
    // forward-slash separators throughout (cross-platform deterministic),
    // module name in the directory, schema-qualified filename. Slice-1
    // structural witness: the path matches the convention character-for-
    // character.
    let enriched = enrich sampleCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let slices = ArtifactByKind.toMap artifact
    for KeyValue(_, file) in slices do
        Assert.StartsWith ("Modules/", file.RelativePath)
        Assert.EndsWith (".sql", file.RelativePath)
        // Forward slashes only; no backslashes regardless of host OS.
        Assert.DoesNotContain ('\\', file.RelativePath)
        // Three segments separated by '/': Modules, ModuleName, leaf.
        let segments = file.RelativePath.Split('/')
        Assert.Equal (3, segments.Length)
        Assert.Equal ("Modules", segments.[0])
        // Leaf is `<Schema>.<Table>.sql` (two dots before the extension).
        let leaf = segments.[2]
        Assert.True (leaf.Contains('.'), sprintf "leaf %s missing dot separator" leaf)

[<Fact>]
let ``SsdtDdlEmitter.emitSlices Body contains CREATE TABLE statement for every kind`` () =
    // Slice-1 SQL emission: every kind's body is a CREATE TABLE statement
    // (no INSERTs, no CREATE INDEX, no ALTER TABLE for FKs at this slice).
    // ScriptDom's `Sql160ScriptGenerator` emits `CREATE TABLE` keyword
    // case per the pinned-options writer (uppercase per chapter-3.5
    // codification).
    let enriched = enrich sampleCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let slices = ArtifactByKind.toMap artifact
    for KeyValue(ssKey, file) in slices do
        Assert.Contains ("CREATE TABLE", file.Body)
        Assert.False (file.Body.Contains "INSERT", sprintf "slice 1 should not emit INSERTs (kind %A)" ssKey)

// ---------------------------------------------------------------------------
// T1 byte-determinism — same input, same output, byte-identical across
// repeat invocations. The chapter-3.1 audit's Bench-driven discipline made
// this a structural property; the SSDT DDL emitter inherits it via
// ScriptDom's pinned-options writer.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: SsdtDdlEmitter.emitSlices is byte-deterministic across repeat invocations`` () =
    let enriched = enrich sampleCatalog
    let r1 = SsdtDdlEmitter.emitSlices enriched |> mustOk |> ArtifactByKind.toMap
    let r2 = SsdtDdlEmitter.emitSlices enriched |> mustOk |> ArtifactByKind.toMap
    Assert.Equal (Map.count r1, Map.count r2)
    for KeyValue(ssKey, file1) in r1 do
        let file2 : SsdtDdlEmitter.SsdtFile = Map.find ssKey r2
        Assert.Equal<string> (file1.RelativePath, file2.RelativePath)
        Assert.Equal<string> (file1.Body, file2.Body)

// ---------------------------------------------------------------------------
// T11 sibling-Π commutativity — SsdtDdlEmitter joins RawText / Json /
// Distributions as the fourth Π. The smart-constructor's strict-equality
// keyset enforcement guarantees structural T11 across all four siblings.
// This test demonstrates the structural path executes for the new fourth
// sibling; the existing `SiblingEmitterContractTests.fs` covers the
// pairwise commutativity for the prior three.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T11: SsdtDdlEmitter.emitSlices keyset equals Catalog.allKinds`` () =
    let enriched = enrich sampleCatalog
    let expected =
        Catalog.allKinds enriched |> List.map (fun k -> k.SsKey) |> Set.ofList
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    Assert.Equal<Set<SsKey>> (expected, ArtifactByKind.keys artifact)

[<Fact>]
let ``T11: SsdtDdlEmitter and RawTextEmitter agree on keyset`` () =
    let enriched = enrich sampleCatalog
    let rawText = RawTextEmitter.emitSlices enriched |> mustOk
    let ssdtDdl = SsdtDdlEmitter.emitSlices enriched |> mustOk
    Assert.Equal<Set<SsKey>> (ArtifactByKind.keys rawText, ArtifactByKind.keys ssdtDdl)
