module Projection.Tests.ActionableDiagnosticsTests

open System.Text.Json.Nodes
open Xunit
open FsCheck.Xunit
open Projection.Core
open Projection.Targets.OperationalDiagnostics

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mustOkEmit (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err ->
        Assert.Fail (sprintf "expected Ok; got %A" err)
        Unchecked.defaultof<'a>

let private requireNode (label: string) (n: JsonNode | null) : JsonNode =
    match Option.ofObj n with
    | Some node -> node
    | None ->
        Assert.Fail (sprintf "%s: required JsonNode child was null" label)
        Unchecked.defaultof<JsonNode>

let private requireArr (label: string) (n: JsonNode | null) : JsonArray =
    (requireNode label n).AsArray()

// ---------------------------------------------------------------------------
// Chapter B.4 slice 6 — operationalize §12 suggestedConfig discipline +
// severity-sort + axis-cluster for navigation. NO occlusion: every input
// entry surfaces in the output.
//
// Tests cover:
//   1. Axis.tryFromCode derivation (top.sub two-segment + single-segment
//      fallback + blank-code rejection).
//   2. ActionableDiagnostics.organize — severity-sort + axis-cluster +
//      no-occlusion invariant + identity property under reorder.
//   3. SuggestedConfig smart constructor (path-blank rejection; note-
//      empty-to-None normalization).
//   4. DiagnosticEntry.create smart constructor (defaults).
//   5. JSON emit path — suggestedConfig surfaces in the per-kind
//      document when Some; absent when None.
// ---------------------------------------------------------------------------

let private mkKey label = SsKey.synthesized "OS_TEST" label |> mustOk

let private mkEntry
    (code: string)
    (severity: DiagnosticSeverity)
    (ssKey: SsKey option)
    : DiagnosticEntry =
    { Source = "testPass"
      Severity = severity
      Code = code
      Message = sprintf "entry with code %s" code
      SsKey = ssKey
      Metadata = Map.empty
      SuggestedConfig = None }

// ---------------------------------------------------------------------------
// Axis.tryFromCode — derivation conventions.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Axis.tryFromCode extracts top.sub from two-segment code`` () =
    Assert.Equal(Some "tightening.nullability",
        Axis.tryFromCode "tightening.nullability.relaxedUnderEvidence")

[<Fact>]
let ``Axis.tryFromCode handles three-or-more-segment codes`` () =
    Assert.Equal(Some "tightening.uniqueIndex",
        Axis.tryFromCode "tightening.uniqueIndex.noCandidate.composite")

[<Fact>]
let ``Axis.tryFromCode uses top.sub two-segment convention even for short codes`` () =
    Assert.Equal(Some "userFkReflow.noEmail", Axis.tryFromCode "userFkReflow.noEmail")

[<Fact>]
let ``Axis.tryFromCode handles single-segment-no-dot code`` () =
    Assert.Equal(Some "alone", Axis.tryFromCode "alone")

[<Fact>]
let ``Axis.tryFromCode rejects blank code`` () =
    Assert.Equal(None, Axis.tryFromCode "")
    Assert.Equal(None, Axis.tryFromCode "   ")

[<Fact>]
let ``Axis.tryFromCode handles tightening profiling adapter prefixes`` () =
    Assert.Equal(Some "tightening.nullability",
        Axis.tryFromCode "tightening.nullability.requireOperatorApproval")
    Assert.Equal(Some "profiling.foreignKey",
        Axis.tryFromCode "profiling.foreignKey.orphanSample")
    Assert.Equal(Some "adapter.OSSYS",
        Axis.tryFromCode "adapter.OSSYS.documentParse")

// ---------------------------------------------------------------------------
// ActionableDiagnostics.organize — severity-sort + axis-cluster, no occlusion.
// ---------------------------------------------------------------------------

[<Fact>]
let ``organize preserves all entries (no occlusion)`` () =
    let entries =
        [ mkEntry "tightening.nullability.foo" DiagnosticSeverity.Warning (Some (mkKey "K1"))
          mkEntry "tightening.nullability.bar" DiagnosticSeverity.Warning (Some (mkKey "K2"))
          mkEntry "tightening.uniqueIndex.baz" DiagnosticSeverity.Info (Some (mkKey "K3")) ]
    let result = ActionableDiagnostics.organize entries
    Assert.Equal(entries.Length, result.Length)

[<Fact>]
let ``organize sorts entries by severity descending within an axis`` () =
    let entries =
        [ mkEntry "tightening.nullability.a" DiagnosticSeverity.Info    (Some (mkKey "K1"))
          mkEntry "tightening.nullability.b" DiagnosticSeverity.Error   (Some (mkKey "K2"))
          mkEntry "tightening.nullability.c" DiagnosticSeverity.Warning (Some (mkKey "K3")) ]
    let result = ActionableDiagnostics.organize entries
    Assert.Equal(DiagnosticSeverity.Error, result.[0].Severity)
    Assert.Equal(DiagnosticSeverity.Warning, result.[1].Severity)
    Assert.Equal(DiagnosticSeverity.Info, result.[2].Severity)

[<Fact>]
let ``organize retains all entries even with hundreds of findings in one axis (no occlusion)`` () =
    // Operator deliberately demands seeing EVERY source defect. The
    // earlier cluster-cap shape dropped entries beyond top-10 per
    // axis; the post-reshape contract retains everything. Source
    // defects (NULLs in NOT NULL columns; orphaned FKs; duplicate
    // unique candidates) must be operator-visible in full.
    let entries =
        [ for i in 1 .. 187 ->
            mkEntry
                (sprintf "tightening.nullability.code%d" i)
                DiagnosticSeverity.Warning
                (Some (mkKey (sprintf "K%03d" i))) ]
    let result = ActionableDiagnostics.organize entries
    Assert.Equal(187, result.Length)
    // No overflow marker is written; the source defects stay
    // first-class entries.
    for e in result do
        Assert.False(Map.containsKey "axisOverflowCount" e.Metadata)
        Assert.False(Map.containsKey "axisRetainedCount" e.Metadata)

[<Fact>]
let ``organize clusters entries by derived axis`` () =
    let entries =
        [ mkEntry "tightening.nullability.a" DiagnosticSeverity.Warning (Some (mkKey "K1"))
          mkEntry "tightening.uniqueIndex.b"  DiagnosticSeverity.Warning (Some (mkKey "K2"))
          mkEntry "tightening.nullability.c" DiagnosticSeverity.Warning (Some (mkKey "K3"))
          mkEntry "tightening.uniqueIndex.d"  DiagnosticSeverity.Warning (Some (mkKey "K4")) ]
    let result = ActionableDiagnostics.organize entries
    // Output ordered by axis name (sorted ASC): nullability first, then uniqueIndex
    let axes = result |> List.map (fun e -> Axis.tryFromCode e.Code)
    Assert.Equal<string option list>(
        [ Some "tightening.nullability"
          Some "tightening.nullability"
          Some "tightening.uniqueIndex"
          Some "tightening.uniqueIndex" ],
        axes)

[<Fact>]
let ``organize places unclustered entries at tail in input order`` () =
    let e1 = mkEntry ""  DiagnosticSeverity.Warning (Some (mkKey "K1"))
    let e2 = mkEntry "   " DiagnosticSeverity.Warning (Some (mkKey "K2"))
    let e3 = mkEntry "tightening.nullability.foo" DiagnosticSeverity.Warning (Some (mkKey "K3"))
    let result = ActionableDiagnostics.organize [ e1; e2; e3 ]
    Assert.Equal(3, result.Length)
    // e3 is clustered (axis = tightening.nullability) and lands first.
    Assert.Equal("tightening.nullability.foo", result.[0].Code)
    // e1 + e2 are unclustered (blank codes) and preserve input order at tail.
    Assert.Equal("", result.[1].Code)
    Assert.Equal("   ", result.[2].Code)

[<Property(MaxTest = 50)>]
let ``organize result has same length as input (no occlusion property)`` (count: int) =
    let n = max 0 (min 100 count)
    let entries =
        [ for i in 1 .. n ->
            mkEntry
                (sprintf "tightening.nullability.code%d" i)
                DiagnosticSeverity.Warning
                (Some (mkKey (sprintf "K%03d" i))) ]
    let result = ActionableDiagnostics.organize entries
    result.Length = entries.Length

[<Property(MaxTest = 25)>]
let ``organize is idempotent`` (count: int) =
    let n = max 1 (min 25 count)
    let entries =
        [ for i in 1 .. n ->
            mkEntry
                (sprintf "tightening.nullability.code%d" i)
                (if i % 2 = 0 then DiagnosticSeverity.Warning else DiagnosticSeverity.Info)
                (Some (mkKey (sprintf "K%03d" i))) ]
    let once = ActionableDiagnostics.organize entries
    let twice = ActionableDiagnostics.organize once
    once = twice

// ---------------------------------------------------------------------------
// SuggestedConfig smart constructor.
// ---------------------------------------------------------------------------

[<Fact>]
let ``SuggestedConfig.create produces typed payload`` () =
    let cfg = SuggestedConfig.create "$.profiling.samplingCap" "100000" |> mustOk
    Assert.Equal("$.profiling.samplingCap", cfg.Path)
    Assert.Equal("100000", cfg.Value)
    Assert.Equal(None, cfg.Note)

[<Fact>]
let ``SuggestedConfig.create rejects blank path`` () =
    let r = SuggestedConfig.create "" "value"
    match r with
    | Ok _ -> Assert.Fail("expected blank-path rejection")
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "suggestedConfig.path.empty")

[<Fact>]
let ``SuggestedConfig.createWithNote carries note when non-blank`` () =
    let cfg = SuggestedConfig.createWithNote "$.path" "v" "operator rationale" |> mustOk
    Assert.Equal(Some "operator rationale", cfg.Note)

[<Fact>]
let ``SuggestedConfig.createWithNote normalizes blank note to None`` () =
    let cfg = SuggestedConfig.createWithNote "$.path" "v" "   " |> mustOk
    Assert.Equal(None, cfg.Note)

// ---------------------------------------------------------------------------
// DiagnosticEntry.create smart constructor (slice 5.13.smart-constructor-
// lift pattern; absorbs SuggestedConfig field addition at one site).
// ---------------------------------------------------------------------------

[<Fact>]
let ``DiagnosticEntry.create sets minimum-evidence defaults`` () =
    let e = DiagnosticEntry.create "src" DiagnosticSeverity.Warning "test.code" "message"
    Assert.Equal("src", e.Source)
    Assert.Equal(DiagnosticSeverity.Warning, e.Severity)
    Assert.Equal("test.code", e.Code)
    Assert.Equal("message", e.Message)
    Assert.Equal(None, e.SsKey)
    Assert.True(Map.isEmpty e.Metadata)
    Assert.Equal(None, e.SuggestedConfig)

// ---------------------------------------------------------------------------
// JSON emit path — suggestedConfig surfaces in the per-kind document.
// ---------------------------------------------------------------------------

let private mkAttr label =
    Attribute.create
        (SsKey.synthesized "OS_TEST" (sprintf "ATTR_%s" label) |> mustOk)
        (Name.create label |> mustOk)
        PrimitiveType.Integer

let private mkKind label =
    Kind.create
        (mkKey label)
        (Name.create label |> mustOk)
        (TableId.create "dbo" label |> mustOk)
        [ mkAttr label ]

let private mkCatalogWithKind kind =
    let m =
        Module.create
            (mkKey "M1")
            (Name.create "M1" |> mustOk)
            [ kind ]
            true
            []
        |> mustOk
    { Modules = [ m ]; Sequences = [] }

[<Fact>]
let ``DecisionLogEmitter writes suggestedConfig when entry carries it`` () =
    let kind = mkKind "K1"
    let cat = mkCatalogWithKind kind
    let entry =
        { DiagnosticEntry.create
            "testPass"
            DiagnosticSeverity.Warning
            "tightening.nullability.relaxedUnderEvidence"
            "warning message"
          with
            SsKey = Some kind.SsKey
            SuggestedConfig =
                Some (SuggestedConfig.createWithNote
                        "$.policy.nullability.budget"
                        "0.05"
                        "raise from 0.01"
                      |> mustOk) }
    let artifact = DecisionLogEmitter.emit cat [ entry ] |> mustOkEmit
    let doc = ArtifactByKind.tryFind kind.SsKey artifact |> Option.get
    let entries = requireArr "entries" doc.["entries"]
    let entryNode = requireNode "entries[0]" entries.[0]
    let suggestedConfig = requireNode "suggestedConfig" entryNode.["suggestedConfig"]
    Assert.Equal("$.policy.nullability.budget",
        (requireNode "path" suggestedConfig.["path"]).GetValue<string>())
    Assert.Equal("0.05",
        (requireNode "value" suggestedConfig.["value"]).GetValue<string>())
    Assert.Equal("raise from 0.01",
        (requireNode "note" suggestedConfig.["note"]).GetValue<string>())

[<Fact>]
let ``DecisionLogEmitter omits suggestedConfig when entry has None`` () =
    let kind = mkKind "K1"
    let cat = mkCatalogWithKind kind
    let entry =
        { DiagnosticEntry.create
            "testPass"
            DiagnosticSeverity.Warning
            "tightening.nullability.relaxedUnderEvidence"
            "warning message"
          with SsKey = Some kind.SsKey }
    let artifact = DecisionLogEmitter.emit cat [ entry ] |> mustOkEmit
    let doc = ArtifactByKind.tryFind kind.SsKey artifact |> Option.get
    let entries = requireArr "entries" doc.["entries"]
    let entryNode = requireNode "entries[0]" entries.[0]
    // `suggestedConfig` property must NOT appear when entry carries None
    // (back-compat with pre-slice-6 consumers).
    Assert.False(entryNode.AsObject().ContainsKey "suggestedConfig")
