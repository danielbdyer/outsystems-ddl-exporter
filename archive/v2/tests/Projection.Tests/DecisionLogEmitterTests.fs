module Projection.Tests.DecisionLogEmitterTests

open Xunit
open System.Text.Json
open System.Text.Json.Nodes
open Projection.Core
open Projection.Targets.OperationalDiagnostics
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Chapter 4.3 slice α — DecisionLogEmitter.
//
// Per `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md` §1.5 slice 2:
// emit one JSON document per catalog kind, containing the DiagnosticEntry
// records whose SsKey matches the kind. The substrate is the existing
// Diagnostics<'a> writer; this emitter is projection, not new algebra.
//
// Slice α scope: every DiagnosticEntry routes to the decision-log artifact
// (slice β extracts the shared routing table; slice γ filters by Code
// prefix). T1 byte-determinism + T11 keyset coverage are the load-bearing
// properties at this slice.
// ---------------------------------------------------------------------------

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err ->
        Assert.Fail (sprintf "expected Ok; got %A" err)
        Unchecked.defaultof<'a>

/// JsonNode index returns `JsonNode | null` (F# 9 nullness-strict);
/// `requireNode` projects to the typed `JsonNode` after asserting
/// non-null. Same shape as `SiblingEmitterContractTests.fs:124`.
let private requireNode (label: string) (n: JsonNode | null) : JsonNode =
    match Option.ofObj n with
    | Some node -> node
    | None      -> Assert.Fail (sprintf "%s: required JsonNode child was null" label); Unchecked.defaultof<JsonNode>

let private requireArr (label: string) (n: JsonNode | null) : JsonArray =
    (requireNode label n).AsArray()

let private mkEntry
    (severity: DiagnosticSeverity)
    (source: string)
    (code: string)
    (message: string)
    (ssKey: SsKey option)
    : DiagnosticEntry =
    { Source = source
      Severity = severity
      Code = code
      Message = message
      SsKey = ssKey
      Metadata = Map.empty
      SuggestedConfig = None }

let private kindCount (catalog: Catalog) : int =
    Catalog.allKinds catalog |> List.length

// ---------------------------------------------------------------------------
// T11 — every catalog kind appears as a top-level key.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T11: DecisionLogEmitter.emit produces one JSON document per catalog kind`` () =
    let artifact = DecisionLogEmitter.emit sampleCatalog [] |> mustOk
    let slices = ArtifactByKind.toMap artifact
    Assert.Equal (kindCount sampleCatalog, Map.count slices)
    for k in Catalog.allKinds sampleCatalog do
        Assert.True (Map.containsKey k.SsKey slices, sprintf "missing slice for kind %A" k.SsKey)

[<Fact>]
let ``T11: empty entries list still produces a JSON document per kind`` () =
    let artifact = DecisionLogEmitter.emit sampleCatalog [] |> mustOk
    let slices = ArtifactByKind.toMap artifact
    for (KeyValue (_, node)) in slices do
        // Each per-kind doc carries `entries: []` for kinds with no
        // matching diagnostics.
        let entries = requireArr "entries" node["entries"]
        Assert.Equal (0, entries.Count)

// ---------------------------------------------------------------------------
// Per-kind grouping: entries route by SsKey field.
// ---------------------------------------------------------------------------

[<Fact>]
let ``DecisionLogEmitter.emit routes entry to the kind named by its SsKey`` () =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let entry =
        mkEntry
            DiagnosticSeverity.Warning
            "testPass"
            "test.routing.byKind"
            "this entry mentions a specific kind"
            (Some customer.SsKey)
    let artifact = DecisionLogEmitter.emit sampleCatalog [ entry ] |> mustOk
    let customerDoc = ArtifactByKind.toMap artifact |> Map.find customer.SsKey
    let entries = requireArr "entries" customerDoc["entries"]
    Assert.Equal (1, entries.Count)

[<Fact>]
let ``DecisionLogEmitter.emit places entry only on the kind it names (other kinds carry empty entries)`` () =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let otherKinds = Catalog.allKinds sampleCatalog |> List.tail
    let entry =
        mkEntry
            DiagnosticSeverity.Info
            "testPass"
            "test.routing.specific"
            "for customer only"
            (Some customer.SsKey)
    let artifact = DecisionLogEmitter.emit sampleCatalog [ entry ] |> mustOk
    let map = ArtifactByKind.toMap artifact
    for k in otherKinds do
        let doc = Map.find k.SsKey map
        let entries = requireArr "entries" doc["entries"]
        Assert.Equal (0, entries.Count)

[<Fact>]
let ``DecisionLogEmitter.emit groups multiple entries on the same kind into one document`` () =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let entries =
        [ mkEntry DiagnosticSeverity.Info     "pass1" "test.a" "a" (Some customer.SsKey)
          mkEntry DiagnosticSeverity.Warning  "pass2" "test.b" "b" (Some customer.SsKey)
          mkEntry DiagnosticSeverity.Error    "pass3" "test.c" "c" (Some customer.SsKey) ]
    let artifact = DecisionLogEmitter.emit sampleCatalog entries |> mustOk
    let doc = ArtifactByKind.toMap artifact |> Map.find customer.SsKey
    let docEntries = requireArr "entries" doc["entries"]
    Assert.Equal (3, docEntries.Count)

[<Fact>]
let ``DecisionLogEmitter.emit drops entries with SsKey = None (catalog-level entries; slice α scope)`` () =
    let entry =
        mkEntry
            DiagnosticSeverity.Warning
            "boundary"
            "adapter.unscoped"
            "no SsKey"
            None
    let artifact = DecisionLogEmitter.emit sampleCatalog [ entry ] |> mustOk
    let map = ArtifactByKind.toMap artifact
    // Every kind's entries array is empty (the catalog-level entry
    // bucket is slice η surface, not slice α). The artifact bytes are
    // unchanged by NM-23 — the shed is witnessed on the diagnostics
    // channel, not by mutating the per-kind documents.
    for (KeyValue (_, doc)) in map do
        let entries = requireArr "entries" doc["entries"]
        Assert.Equal (0, entries.Count)

// ---------------------------------------------------------------------------
// NM-23 — the catalog-level (SsKey = None) shed is witnessed, not silent.
// The per-kind artifact still cannot carry catalog-level entries (the bytes
// above are unchanged), but `catalogLevelShedWitness` now names the loss so
// the audit channel does not silently drop catalog-level decisions.
// ---------------------------------------------------------------------------

[<Fact>]
let ``NM-23: catalogLevelShedWitness names every shed SsKey = None entry with a count`` () =
    let entries =
        [ mkEntry DiagnosticSeverity.Warning "boundary" "adapter.unscoped.a" "no SsKey a" None
          mkEntry DiagnosticSeverity.Info    "boundary" "adapter.unscoped.b" "no SsKey b" None ]
    match DecisionLogEmitter.catalogLevelShedWitness entries with
    | None -> Assert.Fail "expected a shed witness for two catalog-level entries"
    | Some w ->
        Assert.Equal<string> ("emit.decisionLog.catalogLevelEntriesShed", w.Code)
        Assert.Equal (DiagnosticSeverity.Warning, w.Severity)
        Assert.True (Option.isNone w.SsKey)
        Assert.Equal<string> ("2", Map.find "shedCount" w.Metadata)
        // Codes are sorted + newline-joined for byte-determinism.
        Assert.Equal<string> ("adapter.unscoped.a\nadapter.unscoped.b", Map.find "shedCodes" w.Metadata)

[<Fact>]
let ``NM-23: catalogLevelShedWitness is None when no catalog-level entry is shed`` () =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let entries =
        [ mkEntry DiagnosticSeverity.Warning "p" "test.scoped" "scoped" (Some customer.SsKey) ]
    Assert.True (Option.isNone (DecisionLogEmitter.catalogLevelShedWitness entries))

[<Fact>]
let ``NM-23: catalogLevelShedWitness witness count matches the shed entries dropped from the per-kind artifact`` () =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let scoped = mkEntry DiagnosticSeverity.Info "p" "test.scoped" "scoped" (Some customer.SsKey)
    let unscoped1 = mkEntry DiagnosticSeverity.Warning "b" "adapter.unscoped.x" "no SsKey x" None
    let unscoped2 = mkEntry DiagnosticSeverity.Error   "b" "adapter.unscoped.y" "no SsKey y" None
    let all = [ scoped; unscoped1; unscoped2 ]
    // The scoped entry lands in the per-kind doc; the two unscoped are shed.
    let artifact = DecisionLogEmitter.emit sampleCatalog all |> mustOk
    let customerDoc = ArtifactByKind.toMap artifact |> Map.find customer.SsKey
    Assert.Equal (1, (requireArr "entries" customerDoc["entries"]).Count)
    match DecisionLogEmitter.catalogLevelShedWitness all with
    | None -> Assert.Fail "expected a shed witness"
    | Some w -> Assert.Equal<string> ("2", Map.find "shedCount" w.Metadata)

// ---------------------------------------------------------------------------
// JSON shape: severity / source / code / message / ssKey / metadata.
// ---------------------------------------------------------------------------

[<Fact>]
let ``DecisionLogEmitter.emit JSON entry carries source / severity / code / message / ssKey`` () =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let entry =
        { mkEntry
            DiagnosticSeverity.Warning
            "tighteningPass"
            "tightening.unique.opportunity.byEvidence"
            "duplicates found"
            (Some customer.SsKey) with
            Metadata = Map.ofList [ "rationale", "DuplicateData"; "count", "3" ] }
    let artifact = DecisionLogEmitter.emit sampleCatalog [ entry ] |> mustOk
    let doc = ArtifactByKind.toMap artifact |> Map.find customer.SsKey
    let jsonEntry = requireNode "entries[0]" (requireArr "entries" doc["entries"]).[0]
    let getString (key: string) : string =
        (requireNode key jsonEntry[key]).GetValue<string>()
    Assert.Equal<string> ("tighteningPass",                              getString "source")
    Assert.Equal<string> ("Warning",                                     getString "severity")
    Assert.Equal<string> ("tightening.unique.opportunity.byEvidence",    getString "code")
    Assert.Equal<string> ("duplicates found",                            getString "message")
    let metadata = requireNode "metadata" jsonEntry["metadata"]
    Assert.Equal<string> ("DuplicateData", (requireNode "rationale" metadata["rationale"]).GetValue<string>())
    Assert.Equal<string> ("3",              (requireNode "count"     metadata["count"]).GetValue<string>())

// ---------------------------------------------------------------------------
// T1 byte-determinism.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: DecisionLogEmitter.emit is byte-deterministic across repeat invocations`` () =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let entries =
        [ mkEntry DiagnosticSeverity.Warning "p1" "test.a" "msg-a" (Some customer.SsKey)
          mkEntry DiagnosticSeverity.Error   "p2" "test.b" "msg-b" (Some customer.SsKey) ]
    let r1 = DecisionLogEmitter.emit sampleCatalog entries |> mustOk
    let r2 = DecisionLogEmitter.emit sampleCatalog entries |> mustOk
    let m1 = ArtifactByKind.toMap r1
    let m2 = ArtifactByKind.toMap r2
    Assert.Equal (Map.count m1, Map.count m2)
    for (KeyValue (key, n1)) in m1 do
        let n2 = Map.find key m2
        Assert.Equal<string> (n1.ToJsonString(), n2.ToJsonString())

[<Fact>]
let ``T1: metadata ordering is sorted-key deterministic (no hash-table iteration variance)`` () =
    // If metadata writes followed hash-table iteration order, byte-
    // determinism would not hold across runs (Map equality is by
    // contents, but JSON serialization order matters). Verify that
    // the same metadata in two equivalent maps produces identical
    // JSON.
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let baseEntry = mkEntry DiagnosticSeverity.Info "p" "test" "msg" (Some customer.SsKey)
    let entry1 =
        { baseEntry with Metadata = Map.ofList [ "alpha", "1"; "bravo", "2"; "charlie", "3" ] }
    let entry2 =
        { baseEntry with Metadata = Map.ofList [ "charlie", "3"; "alpha", "1"; "bravo", "2" ] }
    let r1 = DecisionLogEmitter.emit sampleCatalog [ entry1 ] |> mustOk
    let r2 = DecisionLogEmitter.emit sampleCatalog [ entry2 ] |> mustOk
    let d1 = (ArtifactByKind.toMap r1 |> Map.find customer.SsKey).ToJsonString()
    let d2 = (ArtifactByKind.toMap r2 |> Map.find customer.SsKey).ToJsonString()
    Assert.Equal<string> (d1, d2)

// ---------------------------------------------------------------------------
// Severity rendering.
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("Info")>]
[<InlineData("Warning")>]
[<InlineData("Error")>]
let ``DecisionLogEmitter.emit renders each DiagnosticSeverity variant as its label`` (label: string) =
    let customer = Catalog.allKinds sampleCatalog |> List.head
    let severity =
        match label with
        | "Info"    -> DiagnosticSeverity.Info
        | "Warning" -> DiagnosticSeverity.Warning
        | "Error"   -> DiagnosticSeverity.Error
        | other     -> failwithf "unexpected label %s" other
    let entry = mkEntry severity "p" "test" "msg" (Some customer.SsKey)
    let artifact = DecisionLogEmitter.emit sampleCatalog [ entry ] |> mustOk
    let doc = ArtifactByKind.toMap artifact |> Map.find customer.SsKey
    let firstEntry = requireNode "entries[0]" (requireArr "entries" doc["entries"]).[0]
    let actual = (requireNode "severity" firstEntry["severity"]).GetValue<string>()
    Assert.Equal<string> (label, actual)
