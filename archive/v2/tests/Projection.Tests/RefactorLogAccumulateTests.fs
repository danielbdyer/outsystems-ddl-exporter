module Projection.Tests.RefactorLogAccumulateTests

open System
open System.Xml
open System.Xml.Linq
open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// AC-P6 (NEITHER->HELD): the real episode clock + accumulate-against-prior
// dedup. Two mechanisms under test:
//   1. `RefactorLogRender.toRefactorLogXmlAt` threads the episode's real
//      `At` into `ChangeDateTime` (retires the pinned `2000-01-01` constant
//      on the threading path - gap N6). Two episodes with different `At`
//      render different timestamps; the pinned-constant impl would produce
//      identical ones (the discriminating input).
//   2. `RefactorLogEmitter.accumulate` merges the prior committed log with
//      the current emission, deduping by `OperationKey`: a rename already in
//      the prior log adds ZERO entries; a new rename is appended.

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

let private nameOf (s: string) : Name = Name.create s |> mustResultOk

// `System.Xml.Linq` nullable-reference coercion helpers (mirrors
// RefactorLogRenderTests.fs).
let private root (doc: XDocument) : XElement = nonNull doc.Root
let private xname (s: string) : XName = nonNull (XName.op_Implicit s)
let private attr (name: string) (el: XElement) : XAttribute = nonNull (el.Attribute(xname name))
let private xnamespace (uri: string) : XNamespace = nonNull (XNamespace.op_Implicit uri)

let private operationsNamespace =
    xnamespace "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02"

let private parseDoc (xml: string) : XDocument =
    XDocument.Parse(xml, LoadOptions.PreserveWhitespace)

// ---------------------------------------------------------------------------
// Rename fixtures. Two DISTINCT renames so the dedup tests can exercise both
// "already present" (re-emit Customer->Patron) and "new" (Order->Receipt).
// Per A1 (identity-survives-rename), renames preserve SsKey.
// ---------------------------------------------------------------------------

// Customer "Customer" -> "Patron"
let private targetCustomerRenamed : Catalog =
    let renamedCustomer = { customer with Name = nameOf "Patron" }
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ renamedCustomer; order; country ] } ]

// Order "Order" -> "Receipt" (a SECOND, distinct rename - distinct OperationKey)
let private targetOrderRenamed : Catalog =
    let renamedOrder = { order with Name = nameOf "Receipt" }
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ customer; renamedOrder; country ] } ]

let private emitRenameEntries (target: Catalog) : RefactorLogEntry list =
    let diff = CatalogDiff.between sampleCatalog target
    RefactorLogEmitter.emit diff |> mustOk |> RefactorLogEmitter.flatten

// ===========================================================================
// (1) REAL CLOCK - episode `At` threads into `ChangeDateTime`.
// ===========================================================================

let private customerArtifact () : ArtifactByKind<RefactorLogEntry list> =
    let diff = CatalogDiff.between sampleCatalog targetCustomerRenamed
    RefactorLogEmitter.emit diff |> mustOk

let private changeDateTimeOf (xml: string) : string =
    let op = (root (parseDoc xml)).Elements(operationsNamespace + "Operation") |> Seq.head
    (attr "ChangeDateTime" op).Value

// Discriminating: against the pinned-constant impl, both renders carry
// "2000-01-01T00:00:00Z" and this assertion FAILS (equal, not distinct).
[<Fact>]
let ``AC-P6 real clock: two episodes with different At render different ChangeDateTime`` () =
    let artifact = customerArtifact ()
    let episodeA = DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero)
    let episodeB = DateTimeOffset(2026, 6, 3, 17, 45, 0, TimeSpan.Zero)
    let stampA = changeDateTimeOf (RefactorLogRender.toRefactorLogXmlAt episodeA artifact)
    let stampB = changeDateTimeOf (RefactorLogRender.toRefactorLogXmlAt episodeB artifact)
    Assert.Equal("2026-01-15T09:30:00Z", stampA)
    Assert.Equal("2026-06-03T17:45:00Z", stampB)
    Assert.NotEqual<string>(stampA, stampB)

// The threaded `At` is normalized to UTC (offset-bearing input -> Z form),
// so the timestamp is the real episode instant, not a fictional constant.
[<Fact>]
let ``AC-P6 real clock: offset-bearing At normalizes to UTC Z in ChangeDateTime`` () =
    let artifact = customerArtifact ()
    // 2026-03-10 12:00 -05:00 == 2026-03-10 17:00 UTC
    let at = DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.FromHours -5.0)
    let stamp = changeDateTimeOf (RefactorLogRender.toRefactorLogXmlAt at artifact)
    Assert.Equal("2026-03-10T17:00:00Z", stamp)

// T1 determinism survives the real-clock change: a fixed `At` is an input,
// so repeat renders are byte-identical.
[<Fact>]
let ``AC-P6 real clock: T1 byte-determinism holds for a fixed At`` () =
    let artifact = customerArtifact ()
    let at = DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero)
    let runs = [ for _ in 1 .. 10 -> RefactorLogRender.toRefactorLogXmlAt at artifact ]
    let head = List.head runs
    Assert.All(runs, fun s -> Assert.Equal(head, s))

// The legacy no-episode overload still pins the constant (back-compat for
// callers with no episode in hand).
[<Fact>]
let ``AC-P6 real clock: legacy toRefactorLogXml still pins 2000-01-01`` () =
    let stamp = changeDateTimeOf (RefactorLogRender.toRefactorLogXml (customerArtifact ()))
    Assert.Equal("2000-01-01T00:00:00Z", stamp)

// ===========================================================================
// (2) ACCUMULATE-AGAINST-PRIOR DEDUP - by OperationKey.
// ===========================================================================

// A rename already present in the prior log adds ZERO new entries.
// Discriminating: a naive `prior @ current` (no dedup) would DOUBLE the
// entry to length 2.
[<Fact>]
let ``AC-P6 dedup: re-emitting a rename already in the prior log adds zero entries`` () =
    let prior = emitRenameEntries targetCustomerRenamed
    Assert.Equal(1, List.length prior)
    let current = emitRenameEntries targetCustomerRenamed
    let merged = RefactorLogEmitter.accumulate prior current
    Assert.Equal(1, List.length merged)
    // The single surviving entry is the original Customer->Patron rename.
    Assert.Equal<Guid list>(
        prior |> List.map (fun e -> e.OperationKey),
        merged |> List.map (fun e -> e.OperationKey))

// A NEW rename (distinct OperationKey) is appended.
[<Fact>]
let ``AC-P6 dedup: a new rename is appended to the prior log`` () =
    let prior = emitRenameEntries targetCustomerRenamed     // Customer->Patron
    let current = emitRenameEntries targetOrderRenamed       // Order->Receipt (new key)
    let merged = RefactorLogEmitter.accumulate prior current
    Assert.Equal(2, List.length merged)
    let mergedKeys = merged |> List.map (fun e -> e.OperationKey) |> Set.ofList
    let priorKey = (List.head prior).OperationKey
    let newKey = (List.head current).OperationKey
    Assert.NotEqual<Guid>(priorKey, newKey)
    Assert.True(Set.contains priorKey mergedKeys)
    Assert.True(Set.contains newKey mergedKeys)

// Mixed: the current emission carries BOTH a repeat and a new rename - only
// the new one is added (dedup is per-key, not all-or-nothing).
[<Fact>]
let ``AC-P6 dedup: only the novel key is added when current mixes repeat and new`` () =
    let prior = emitRenameEntries targetCustomerRenamed     // Customer->Patron
    let repeat = emitRenameEntries targetCustomerRenamed     // same key as prior
    let novel = emitRenameEntries targetOrderRenamed         // Order->Receipt
    let current = repeat @ novel
    let merged = RefactorLogEmitter.accumulate prior current
    // prior (1) + novel (1) = 2; the repeat is deduped out.
    Assert.Equal(2, List.length merged)

// The prior entry wins on a key collision (the prior committed log is the
// source of truth for an already-recorded operation).
[<Fact>]
let ``AC-P6 dedup: prior entry wins on OperationKey collision`` () =
    let prior = emitRenameEntries targetCustomerRenamed
    // A "current" entry with the SAME key but a tampered NewName.
    let tampered = prior |> List.map (fun e -> { e with NewName = "TAMPERED" })
    let merged = RefactorLogEmitter.accumulate prior tampered
    Assert.Equal(1, List.length merged)
    Assert.Equal("Patron", (List.head merged).NewName)

// Determinism: the merged log is sorted by OperationKey regardless of input
// order, so accumulate is order-independent.
[<Fact>]
let ``AC-P6 dedup: merged log is OperationKey-sorted (order-independent)`` () =
    let a = emitRenameEntries targetCustomerRenamed
    let b = emitRenameEntries targetOrderRenamed
    let mergedForward = RefactorLogEmitter.accumulate a b
    let mergedReverse = RefactorLogEmitter.accumulate b a
    Assert.Equal<Guid list>(
        mergedForward |> List.map (fun e -> e.OperationKey),
        mergedReverse |> List.map (fun e -> e.OperationKey))
    let keys = mergedForward |> List.map (fun e -> e.OperationKey)
    Assert.Equal<Guid list>(List.sort keys, keys)

// Empty prior (genesis): the current emission is the whole log.
[<Fact>]
let ``AC-P6 dedup: empty prior yields the current emission unchanged`` () =
    let current = emitRenameEntries targetCustomerRenamed
    let merged = RefactorLogEmitter.accumulate [] current
    Assert.Equal<Guid list>(
        current |> List.map (fun e -> e.OperationKey) |> List.sort,
        merged |> List.map (fun e -> e.OperationKey))

// The artifact-shaped convenience entry point agrees with the flat form.
[<Fact>]
let ``AC-P6 dedup: accumulateArtifact agrees with accumulate over flatten`` () =
    let prior = emitRenameEntries targetCustomerRenamed
    let diff = CatalogDiff.between sampleCatalog targetOrderRenamed
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let viaArtifact = RefactorLogEmitter.accumulateArtifact prior artifact
    let viaFlat = RefactorLogEmitter.accumulate prior (RefactorLogEmitter.flatten artifact)
    Assert.Equal<Guid list>(
        viaFlat |> List.map (fun e -> e.OperationKey),
        viaArtifact |> List.map (fun e -> e.OperationKey))

// ---------------------------------------------------------------------------
// Full-history accumulation: folding genesis->latest reproduces the complete
// deduped set. A timeline that renames Customer, then Order, then re-touches
// Customer (idempotent re-emit) accumulates to exactly the two distinct
// renames - no duplicates, no loss.
// ---------------------------------------------------------------------------

[<Fact>]
let ``AC-P6 full-history: folding genesis to latest reproduces the complete deduped set`` () =
    // Per-episode emissions along a timeline (each is the current diff slice).
    let episodes : RefactorLogEntry list list =
        [ emitRenameEntries targetCustomerRenamed   // E1: Customer->Patron
          emitRenameEntries targetOrderRenamed      // E2: Order->Receipt
          emitRenameEntries targetCustomerRenamed ] // E3: re-touch Customer (dup)
    let fullHistory =
        episodes |> List.fold RefactorLogEmitter.accumulate []
    // Two DISTINCT operations survive: Customer->Patron and Order->Receipt.
    Assert.Equal(2, List.length fullHistory)
    let expectedKeys =
        (emitRenameEntries targetCustomerRenamed @ emitRenameEntries targetOrderRenamed)
        |> List.map (fun e -> e.OperationKey)
        |> Set.ofList
    Assert.Equal<Set<Guid>>(
        expectedKeys,
        fullHistory |> List.map (fun e -> e.OperationKey) |> Set.ofList)

// Real clock over a multi-operation document: every `<Operation>` carries
// the same threaded episode `At` (the document's emission instant).
[<Fact>]
let ``AC-P6 full-history: every operation in the rendered log carries the episode At`` () =
    // A target with two renames in one diff -> a two-Operation document.
    let twoRenameTarget : Catalog =
        let renamedCustomer = { customer with Name = nameOf "Patron" }
        let renamedOrder = { order with Name = nameOf "Receipt" }
        IRBuilders.mkCatalog [ { salesModule with Kinds = [ renamedCustomer; renamedOrder; country ] } ]
    let artifact =
        RefactorLogEmitter.emit (CatalogDiff.between sampleCatalog twoRenameTarget) |> mustOk
    let at = DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero)
    let xml = RefactorLogRender.toRefactorLogXmlAt at artifact
    let stamps =
        (root (parseDoc xml)).Elements(operationsNamespace + "Operation")
        |> Seq.map (fun op -> (attr "ChangeDateTime" op).Value)
        |> Seq.toList
    Assert.Equal(2, List.length stamps)
    Assert.All(stamps, fun s -> Assert.Equal("2026-06-03T12:00:00Z", s))
