module Projection.Tests.ReportRunTests

open System
open System.Text.Json
open System.Text.Json.Nodes
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// M18 (THE VECTOR §5.2) — the change report's MACHINE lens. `ReportRun.render`
// surfaces the per-edge displacement as operator prose; `ReportRun.toJson` is
// its byte-deterministic sibling — the contract the SSIS consumer diffs sprint-
// over-sprint. These tests pin the second lens against the same substrate the
// human lens reads (the `ChangeManifestTests` chain: rename customer→Patron at
// E1, back at E2; net displacement zero, path length 2).

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustResultOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> Assert.Fail(sprintf "%A" es); Unchecked.defaultof<'a>

let private mustOk (r: FsResult<'a, 'b>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err -> Assert.Fail(sprintf "%A" err); Unchecked.defaultof<'a>

// -- JSON navigation, null-narrowed (F# nullness: every indexer yields
//    `JsonNode | null`; `nn` is the single assert-non-null gate the rest reuse).
let private nn (n: JsonNode | null) : JsonNode =
    match n with
    | null -> Assert.Fail "unexpected JSON null"; Unchecked.defaultof<JsonNode>
    | x -> x

let private obj (n: JsonNode | null) : JsonObject = (nn n) :?> JsonObject
let private arr (n: JsonNode | null) : JsonArray = (nn n) :?> JsonArray
let private child (o: JsonObject) (key: string) : JsonObject = obj o.[key]
let private getInt (o: JsonObject) (key: string) : int = (nn o.[key]).GetValue<int>()
let private getStr (o: JsonObject) (key: string) : string = (nn o.[key]).GetValue<string>()

let private nameOf (s: string) : Name = Name.create s |> mustResultOk
let private ver (o: int) (l: string) : Version = Version.create o l |> mustResultOk
let private tl (name: string) : Timeline = Timeline.create name |> mustResultOk
let private at (iso: string) : DateTimeOffset = DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture)
let private coord o lbl day = EpisodeCoordinate.create (ver o lbl) Environment.Dev (at (sprintf "2026-06-%02dT09:00:00+00:00" day))

let private renamedKind : Kind = { customer with Name = nameOf "Patron" }
let private renamedModule : Module = { salesModule with Kinds = [ renamedKind; order; country ] }
let private renamedCatalog : Catalog = IRBuilders.mkCatalog [ renamedModule ]

let private e0 = Episode.ofSchema (coord 0 "1.0.0" 1) sampleCatalog
let private e1 =
    Episode.create (coord 1 "1.1.0" 8) renamedCatalog Profile.empty (Some "reflog#1") (DataObservation.create 120 (Some "lsn:0x10"))
let private e2 =
    Episode.create (coord 2 "1.2.0" 15) sampleCatalog Profile.empty (Some "reflog#2") (DataObservation.create 95 None)

let private chain : EpisodicLifecycle =
    EpisodicLifecycle.genesis (tl "dev") e0
    |> (fun lc -> EpisodicLifecycle.append e1 lc |> mustResultOk)
    |> (fun lc -> EpisodicLifecycle.append e2 lc |> mustResultOk)

let private bundle : ReportRun.ReportBundle = ReportRun.fromChain chain |> mustOk
let private root () : JsonObject = ReportRun.toJson bundle

// ===========================================================================
// M18 — the machine lens carries the bundle masthead + one node per edge
// ===========================================================================

[<Fact>]
let ``M18: toJson carries the bundle masthead (timeline, episode count, path length)`` () =
    let o = root ()
    Assert.Equal("dev", getStr o "timeline")
    Assert.Equal(3, getInt o "episodeCount")
    // Path length is the sum of edge norms — rename + rename-back = 2.
    Assert.Equal(2, getInt o "pathLength")

[<Fact>]
let ``M18: toJson emits one change node per edge, with the displacement + data norm`` () =
    let changes = arr (root ()).["changes"]
    Assert.Equal(2, changes.Count)
    let edge0 = obj changes.[0]
    // The schema displacement: one renamed kind, norm 1.
    Assert.Equal(1, getInt edge0 "schemaNorm")
    Assert.Equal(1, getInt (child edge0 "channels") "renamedKinds")
    // The realized data movement is the To-episode's CDC capture count.
    Assert.Equal(120, getInt edge0 "cdcCaptureCount")
    // The Decision anchor is the To-episode's refactorlog reference.
    Assert.Equal("reflog#1", getStr edge0 "refactorLogRef")
    // The endpoints carry the version coordinates (the SSIS consumer keys on them).
    Assert.Equal(0, getInt (child (child edge0 "from") "version") "ordinal")
    Assert.Equal(1, getInt (child (child edge0 "to") "version") "ordinal")

[<Fact>]
let ``M18: an absent Decision anchor renders as JSON null (a stable key)`` () =
    // Build a bundle whose only edge has no refactorlog reference.
    let e1b = Episode.create (coord 3 "1.3.0" 22) renamedCatalog Profile.empty None (DataObservation.create 0 None)
    let chain2 =
        EpisodicLifecycle.genesis (tl "dev") e1
        |> (fun lc -> EpisodicLifecycle.append e1b lc |> mustResultOk)
    let b2 = ReportRun.fromChain chain2 |> mustOk
    let edge0 = obj (arr (ReportRun.toJson b2).["changes"]).[0]
    let isJsonNull =
        match edge0.["refactorLogRef"] with
        | null -> true
        | n -> n.GetValueKind() = JsonValueKind.Null
    Assert.True(isJsonNull, "refactorLogRef must be present-and-null when the To-episode has no anchor")

// ===========================================================================
// M18 — the second lens surfaces the two provenance planes the prose does
// ===========================================================================

[<Fact>]
let ``M18: toJson surfaces the To-episode tolerance residual + applied-transforms`` () =
    let tolerances =
        Tolerance.strict
        |> Tolerance.withDivergence ToleratedDivergence.HeaderCommentsOmitted
        |> Tolerance.withDivergence ToleratedDivergence.IndexOptionsUnreflected
    let appliedTransforms : (SsKey * OverlayAxis option) list =
        [ customerKey, Some Tightening; orderKey, None ]
    let e1p = e1 |> Episode.withProvenance tolerances appliedTransforms
    let chainP =
        EpisodicLifecycle.genesis (tl "dev") e0
        |> (fun lc -> EpisodicLifecycle.append e1p lc |> mustResultOk)
    let b = ReportRun.fromChain chainP |> mustOk
    let edge0 = obj (arr (ReportRun.toJson b).["changes"]).[0]
    // The residual is name-sorted (the displacement's accepted-equivalence set).
    let residual = arr edge0.["toleranceResidual"] |> Seq.map (fun n -> (nn n).GetValue<string>()) |> List.ofSeq
    Assert.Equal<string list>(
        [ ToleratedDivergence.name ToleratedDivergence.HeaderCommentsOmitted
          ToleratedDivergence.name ToleratedDivergence.IndexOptionsUnreflected ],
        residual)
    // The applied-transforms carry the canonical SsKey + the overlay axis (or null).
    let applied = arr edge0.["appliedTransforms"] |> Seq.map obj |> List.ofSeq
    Assert.Equal(2, List.length applied)
    Assert.Equal(SsKey.serialize customerKey, getStr applied.[0] "ssKey")
    Assert.Equal("Tightening", getStr applied.[0] "axis")
    Assert.True(isNull applied.[1].["axis"], "a skeleton-only row carries a null axis")

// ===========================================================================
// M18 — the machine lens is byte-deterministic (T1 — the artifact body is
// diffable sprint-over-sprint, which is the entire point of the second lens)
// ===========================================================================

[<Fact>]
let ``M18: toJsonString is byte-deterministic across renderings`` () =
    Assert.Equal(ReportRun.toJsonString bundle, ReportRun.toJsonString bundle)
