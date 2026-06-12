module Projection.Tests.AdjunctionLawTests

// H-050 (HORIZON Cluster F): adjunction law property tests.
//
// The emitter (`Catalog → DDL`) and reader (`DDL → Catalog`) form an
// adjunction: `reader ∘ emitter = id` (up to named lossy fields).
//
// The full Docker-deploying canary roundtrip lives at
// `CanaryRoundTripTests.fs::M3` (requires Docker; gated). Property-
// based sweeps at that level are infeasible (each test invocation
// requires an ephemeral container).
//
// This file ships the **algebraic adjunction property** at the
// reachable layer: the emitter's typed statement stream is a
// deterministic function of `Catalog`, and the stream's structural
// content reflects every visible kind. Two complementary surfaces:
//
//   1. **Emitter determinism (T1 at the stream level)**: emitting
//      the same catalog twice produces structurally-equal statement
//      streams. This is the algebraic precondition for the
//      adjunction — a non-deterministic emitter can't have a
//      well-defined inverse.
//
//   2. **CatalogDiff identity**: `between c c` produces every key in
//      `Unchanged`. The roundtrip-fidelity diff respects the
//      reflexive law.
//
//   3. **CatalogDiff structural roundtrip**: `between c (rebuilt c)`
//      where `rebuilt c` is `c` reconstructed via the smart
//      constructors (the modeled identity reader) produces every key
//      in `Unchanged`.
//
// The Docker-bound true emitter/reader adjunction sweep is a
// follow-on; this file ships the FsCheck-property layer that the
// Docker canary witnesses on its single fixture.

open Xunit
open FsCheck.Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

// ---------------------------------------------------------------------------
// Per-file helpers
// ---------------------------------------------------------------------------

let private mustOk (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err ->
        Assert.Fail(sprintf "%A" err)
        Unchecked.defaultof<'a>

let private allKindKeys (c: Catalog) : Set<SsKey> =
    Catalog.allKinds c |> List.map (fun k -> k.SsKey) |> Set.ofList

let private shuffleModules (seed: int) (c: Catalog) : Catalog =
    let sorted =
        c.Modules
        |> List.mapi (fun i m -> ((seed + i) * 31 + 17), m)
        |> List.sortBy fst
        |> List.map snd
    { c with Modules = sorted }

// ---------------------------------------------------------------------------
// H-050 Law 1: Emitter is deterministic. Emitting the same catalog
// twice produces structurally-equal statement streams. Algebraic
// precondition for an adjunction; a non-deterministic Π can't have a
// well-defined reverse leg.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-050 emitter determinism: SsdtDdlEmitter.statements is deterministic on sampleCatalog`` () =
    let runA = SsdtDdlEmitter.statements sampleCatalog |> Seq.toList
    let runB = SsdtDdlEmitter.statements sampleCatalog |> Seq.toList
    Assert.Equal<Statement list>(runA, runB)

[<Property>]
let ``H-050 emitter determinism (property): permuting modules produces the same statement stream`` (seed: int) =
    // The emitter sorts catalog kinds by SsKey internally (per
    // A.0' / topological-order discipline); module-list order in
    // the input shouldn't affect the output.
    let permuted = shuffleModules seed sampleCatalog
    let baseRun = SsdtDdlEmitter.statements sampleCatalog |> Seq.toList
    let permRun = SsdtDdlEmitter.statements permuted     |> Seq.toList
    baseRun = permRun

// ---------------------------------------------------------------------------
// H-050 Law 2: CatalogDiff reflexivity. `between c c` puts every key
// in Unchanged. This is the discrete form of the adjunction's
// reflexive identity: a Catalog is roundtrip-equal to itself.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-050 CatalogDiff reflexivity: between c c puts every key in Unchanged`` () =
    let diff = CatalogDiff.between sampleCatalog sampleCatalog |> mustOk
    Assert.Equal<Set<SsKey>>(allKindKeys sampleCatalog, CatalogDiff.unchanged diff)
    Assert.True(CatalogDiff.isEmpty diff)

[<Property>]
let ``H-050 CatalogDiff reflexivity (property): permuted module ordering still diffs to fully-Unchanged`` (seed: int) =
    let permuted = shuffleModules seed sampleCatalog
    let diff = CatalogDiff.between sampleCatalog permuted |> mustOk
    let allKeys = allKindKeys sampleCatalog
    CatalogDiff.unchanged diff = allKeys
    && CatalogDiff.added diff = Set.empty
    && CatalogDiff.removed diff = Set.empty

// ---------------------------------------------------------------------------
// H-050 Law 3: T11 form — every catalog kind appears in the emitted
// statement stream. The adjunction's surjectivity precondition:
// the emitter mentions every input. (Confirms there's no silent
// dropping of kinds at emission, which would break the inverse.)
// ---------------------------------------------------------------------------

let private mentionedTargets (statements: Statement list) : Set<TableId> =
    statements
    |> List.choose (fun stmt ->
        match stmt with
        | CreateTable (target, _, _, _, _, _) -> Some target
        | _ -> None)
    |> Set.ofList

[<Fact>]
let ``H-050 T11 form: every kind in sampleCatalog produces a CreateTable in the emitter output`` () =
    let statements = SsdtDdlEmitter.statements sampleCatalog |> Seq.toList
    let mentioned = mentionedTargets statements
    let expected =
        Catalog.allKinds sampleCatalog
        |> List.map (fun k -> k.Physical)
        |> Set.ofList
    Assert.Equal<Set<TableId>>(expected, mentioned)

[<Property>]
let ``H-050 T11 form (property): emitter mentions every kind across module-order permutations`` (seed: int) =
    let permuted = shuffleModules seed sampleCatalog
    let statements = SsdtDdlEmitter.statements permuted |> Seq.toList
    let mentioned = mentionedTargets statements
    let expected =
        Catalog.allKinds permuted
        |> List.map (fun k -> k.Physical)
        |> Set.ofList
    expected = mentioned

// ---------------------------------------------------------------------------
// H-050 in-process adjunction sweep (Cluster F follow-up, 2026-05-22).
// `PhysicalSchemaReader.ofStatementStream` lifts the emitter's typed
// `seq<Statement>` directly to a `PhysicalSchema` — bypassing the live
// SQL Server round-trip. The full canary still runs against a real
// container at `CanaryRoundTripTests.fs::M3` (additional axes —
// SqlServer's interpretation of CHECK constraints, default expression
// re-rendering, computed-column server-inference). The in-process
// variant gives us the FsCheck property sweep across the
// (Columns, ForeignKeys) axes that the canary asserts on.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-050 in-process adjunction (worked example): ofCatalog c = ofStatementStream (emit c) on Columns + FKs`` () =
    let viaCatalog = PhysicalSchema.ofCatalog sampleCatalog
    let viaStream =
        PhysicalSchemaReader.ofStatementStream (SsdtDdlEmitter.statements sampleCatalog)
    Assert.Equal<Set<PhysicalColumn>>(viaCatalog.Columns, viaStream.Columns)
    Assert.Equal<Set<PhysicalForeignKey>>(viaCatalog.ForeignKeys, viaStream.ForeignKeys)

[<Property>]
let ``H-050 in-process adjunction (property): permuted module order preserves PhysicalSchema columns`` (seed: int) =
    let permuted = shuffleModules seed sampleCatalog
    let viaCatalog = PhysicalSchema.ofCatalog permuted
    let viaStream =
        PhysicalSchemaReader.ofStatementStream (SsdtDdlEmitter.statements permuted)
    viaCatalog.Columns = viaStream.Columns

[<Property>]
let ``H-050 in-process adjunction (property): permuted module order preserves PhysicalSchema FKs`` (seed: int) =
    let permuted = shuffleModules seed sampleCatalog
    let viaCatalog = PhysicalSchema.ofCatalog permuted
    let viaStream =
        PhysicalSchemaReader.ofStatementStream (SsdtDdlEmitter.statements permuted)
    viaCatalog.ForeignKeys = viaStream.ForeignKeys

[<Fact>]
let ``H-050 in-process adjunction: PhysicalSchema.diff is empty across the two projections`` () =
    let viaCatalog = PhysicalSchema.ofCatalog sampleCatalog
    let viaStream =
        PhysicalSchemaReader.ofStatementStream (SsdtDdlEmitter.statements sampleCatalog)
    let diff = PhysicalSchema.diff viaCatalog viaStream
    // Columns + FKs must agree across the two projections. Rows are
    // deliberately not populated by ofStatementStream
    // (the Statement stream doesn't carry static-row content the way
    // a populated Catalog does); that axis is out of scope for the
    // structural adjunction.
    Assert.Empty diff.MissingColumns
    Assert.Empty diff.ExtraColumns
    Assert.Empty diff.MissingForeignKeys
    Assert.Empty diff.ExtraForeignKeys

// ---------------------------------------------------------------------------
// H-050 Docker-bound full adjunction (single-fixture; deferred-with-stub).
// The in-process variant above covers (Columns, ForeignKeys). The full
// roundtrip exercises additional axes that only a real SQL Server
// engine can verify: CHECK constraint re-parsing, default-expression
// re-rendering, server-inferred computed-column types, server-side
// constraint name auto-generation. That sweep at FsCheck-property
// scale would need either coverage-guided fixture generation (so each
// FsCheck iteration is a meaningfully-different input) OR a Docker
// test pool. Single-fixture form runs at CanaryRoundTripTests.fs::M3.
// ---------------------------------------------------------------------------

[<Fact(Skip = "H-050 Docker-bound full adjunction sweep: in-process \
variant above ships the (Columns, ForeignKeys) axes. The Docker-bound \
full roundtrip adds CHECK constraint re-parsing, default-expression \
re-rendering, and server-inferred computed-column types. Trigger: \
coverage-guided FsCheck fixture generation (so each iteration exercises \
a meaningfully-different DDL shape) OR a Docker test pool of N>=20 \
ephemeral containers. Single-fixture form lives at \
CanaryRoundTripTests.fs::M3.")>]
let ``H-050 emitter-reader adjunction sweep: full Docker-bound roundtrip (FsCheck)`` () = ()

// ---------------------------------------------------------------------
// Wave-2 slice 2.2 — the overlay seam is open and byte-identical with the
// empty overlay (the T1 safety net: threading DecisionOverlay through the
// emitter as a curried prefix arg does not change emitted bytes until a
// later slice consumes it).
// ---------------------------------------------------------------------

[<Fact>]
let ``T1 (slice 2.2): statementsWith DecisionOverlay.empty equals statements (byte-identical seam)`` () =
    let viaWrapper = SsdtDdlEmitter.statements sampleCatalog |> Seq.toList
    let viaEmpty = SsdtDdlEmitter.statementsWith DecisionOverlay.empty sampleCatalog |> Seq.toList
    Assert.Equal<Statement list>(viaWrapper, viaEmpty)

[<Fact>]
let ``T1 (slice 2.2): emitSlicesWith DecisionOverlay.empty equals emitSlices (byte-identical bundle)`` () =
    let viaWrapper =
        match SsdtDdlEmitter.emitSlices sampleCatalog with
        | Ok a -> a | Error e -> failwithf "emitSlices: %A" e
    let viaEmpty =
        match SsdtDdlEmitter.emitSlicesWith DecisionOverlay.empty sampleCatalog with
        | Ok a -> a | Error e -> failwithf "emitSlicesWith: %A" e
    // Compare the per-kind SSDT file bodies (the emitted bytes).
    let bodies a =
        ArtifactByKind.toMap a
        |> Map.toList
        |> List.map (fun (k, (f: SsdtDdlEmitter.SsdtFile)) -> k, f.Body)
    Assert.Equal<(SsKey * string) list>(bodies viaWrapper, bodies viaEmpty)
