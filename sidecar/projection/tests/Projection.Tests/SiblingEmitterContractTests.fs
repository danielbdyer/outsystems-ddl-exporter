module Projection.Tests.SiblingEmitterContractTests

open System.Text.Json.Nodes
open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Targets.Json
open Projection.Targets.Distributions
open Projection.Tests.Fixtures
open Projection.Tests.ProfileFixtures

// ---------------------------------------------------------------------------
// T11 — sibling-Π commutativity, encoded as a structural type theorem.
//
// The discipline retired by these tests: the substring
// `Assert.Contains(SsKey.rootOriginal k.SsKey, output)` enforcement at
// `JsonEmitterTests.fs:96-105` and `RichProfilingEndToEndTests.fs:280-289`.
// That discipline tested a downstream consequence (every catalog kind's
// SsKey root *appears in the rendered text*) by string-search; the
// assertion held but the *coverage property* the assertion was a proxy
// for held only by convention — emitters could in principle stop
// surfacing a kind without breaking the substring test if rendering
// happened to mention the SsKey root for unrelated reasons.
//
// Per chapter 3.5 slice α/β/γ: every Π returns
// `Result<ArtifactByKind<'element>, EmitError>`, and `ArtifactByKind`'s
// smart constructor enforces strict equality between the slice's keyset
// and `Catalog.allKinds`'s SsKey set. T11 is now a *structural*
// consequence of construction:
//
//   - Any two `ArtifactByKind` values built from the same Catalog have
//     equal keysets by construction.
//   - The substring discipline becomes redundant.
//
// These tests demonstrate the structural path executes (the smart
// constructor accepts the slice and returns `Ok`) and confirm the
// keyset is what `Catalog.allKinds` advertises. The *theorem* is
// type-encoded; these are the worked examples.
//
// Companion: `ArtifactByKindTests.fs` covers the smart constructor's
// rejection of missing / extra keys; together those + these worked
// examples close the T11 verification surface.
// ---------------------------------------------------------------------------

let private enrich (c: Catalog) : Catalog =
    (CanonicalizeIdentity.run c).Value

let private expectedKeyset (c: Catalog) : Set<SsKey> =
    Catalog.allKinds c |> List.map (fun k -> k.SsKey) |> Set.ofList

[<Fact>]
let ``T11 (type theorem): RawTextEmitter.emitSlices key-set equals Catalog.allKinds`` () =
    let enriched = enrich sampleCatalog
    let expected = expectedKeyset enriched
    match RawTextEmitter.emitSlices enriched with
    | Ok artifact ->
        Assert.Equal<Set<SsKey>>(expected, ArtifactByKind.keys artifact)
    | Error err ->
        Assert.Fail(sprintf "RawTextEmitter.emitSlices returned %A" err)

[<Fact>]
let ``T11 (type theorem): JsonEmitter.emitSlices key-set equals Catalog.allKinds`` () =
    let enriched = enrich sampleCatalog
    let expected = expectedKeyset enriched
    match JsonEmitter.emitSlices enriched with
    | Ok artifact ->
        Assert.Equal<Set<SsKey>>(expected, ArtifactByKind.keys artifact)
    | Error err ->
        Assert.Fail(sprintf "JsonEmitter.emitSlices returned %A" err)

[<Fact>]
let ``T11 (type theorem): DistributionsEmitter.emitSlices key-set equals Catalog.allKinds`` () =
    let enriched = enrich sampleCatalog
    let expected = expectedKeyset enriched
    match DistributionsEmitter.emitSlices enriched sampleProfile with
    | Ok artifact ->
        Assert.Equal<Set<SsKey>>(expected, ArtifactByKind.keys artifact)
    | Error err ->
        Assert.Fail(sprintf "DistributionsEmitter.emitSlices returned %A" err)

// ---------------------------------------------------------------------------
// T11 sibling commutativity — three Π's converge on the same keyset
// because the ArtifactByKind smart constructor binds each to the
// Catalog's keyset by construction. This is the property the substring
// discipline approximated.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T11 (sibling commutativity): RawText, Json, Distributions key-sets are pairwise equal`` () =
    let enriched = enrich sampleCatalog
    let rawTextKeys =
        match RawTextEmitter.emitSlices enriched with
        | Ok a -> ArtifactByKind.keys a
        | Error err -> Assert.Fail(sprintf "RawText: %A" err); Set.empty
    let jsonKeys =
        match JsonEmitter.emitSlices enriched with
        | Ok a -> ArtifactByKind.keys a
        | Error err -> Assert.Fail(sprintf "Json: %A" err); Set.empty
    let distKeys =
        match DistributionsEmitter.emitSlices enriched sampleProfile with
        | Ok a -> ArtifactByKind.keys a
        | Error err -> Assert.Fail(sprintf "Distributions: %A" err); Set.empty
    Assert.Equal<Set<SsKey>>(rawTextKeys, jsonKeys)
    Assert.Equal<Set<SsKey>>(jsonKeys, distKeys)

// ---------------------------------------------------------------------------
// Typed per-kind value at the Π port surface (chapter-3.7 slice ε;
// audit Tier-1 #7). Chapter 3.5 made T11 structural at the *keyset*
// axis. Slice ε lifts the per-kind value type to `JsonNode` so consumers
// (drift detection, post-write enrichment, structural diff) can query
// the slice without a `JsonNode.Parse(string)` re-parse step. The
// pillar-1 promise — typed values flow through; strings emerge only at
// the absolute terminal BCL writer boundary — is the structural claim
// these tests enforce at runtime.
// ---------------------------------------------------------------------------

/// Project a nullable JsonNode through `Option.ofObj` so test
/// assertions can pattern-match without F# 9 nullness flags. The
/// `Option<JsonNode>` form pairs naturally with the `Some node ->`
/// arms below.
let private optNode (n: JsonNode | null) : JsonNode option = Option.ofObj n

let private requireNode (label: string) (n: JsonNode | null) : JsonNode =
    match Option.ofObj n with
    | Some node -> node
    | None      -> Assert.Fail(sprintf "%s: required JsonNode child was null" label); Unchecked.defaultof<JsonNode>

[<Fact>]
let ``JsonEmitter.emitSlices per-kind value is a JsonObject (typed at the seam)`` () =
    let enriched = enrich sampleCatalog
    match JsonEmitter.emitSlices enriched with
    | Ok artifact ->
        let slices = ArtifactByKind.toMap artifact
        for KeyValue(_, node) in slices do
            // Compile-time: `node : JsonNode` (the seam type).
            // Runtime: each kind's writer produces a JsonObject (per
            // `writeKind`'s `WriteStartObject` opener). The structural
            // claim: every per-kind value IS a typed JsonObject.
            Assert.IsAssignableFrom<JsonObject>(node) |> ignore
    | Error err ->
        Assert.Fail(sprintf "JsonEmitter.emitSlices returned %A" err)

[<Fact>]
let ``DistributionsEmitter.emitSlices per-kind value is a JsonObject (typed at the seam)`` () =
    let enriched = enrich sampleCatalog
    match DistributionsEmitter.emitSlices enriched sampleProfile with
    | Ok artifact ->
        let slices = ArtifactByKind.toMap artifact
        for KeyValue(_, node) in slices do
            Assert.IsAssignableFrom<JsonObject>(node) |> ignore
    | Error err ->
        Assert.Fail(sprintf "DistributionsEmitter.emitSlices returned %A" err)

[<Fact>]
let ``JsonEmitter.emitSlices per-kind value carries the SsKey root via the ssKey field (no re-parse)`` () =
    // Pillar 1 promise — typed manipulation works at the seam without
    // a `JsonNode.Parse(string)` step. This test is the structural
    // witness: query the `ssKey` field via JsonNode indexer access and
    // confirm the field's value matches `SsKey.rootOriginal` for the
    // sample kind. If the slice were `string`, indexer access wouldn't
    // compile and the test would have to re-parse.
    let enriched = enrich sampleCatalog
    let allKinds = Catalog.allKinds enriched
    Assert.NotEmpty(allKinds)
    let firstKind = List.head allKinds
    match JsonEmitter.emitSlices enriched with
    | Ok artifact ->
        let slices = ArtifactByKind.toMap artifact
        match Map.tryFind firstKind.SsKey slices with
        | Some node ->
            let ssKeyField = requireNode "slice.ssKey" node.["ssKey"]
            let recovered = ssKeyField.GetValue<string>()
            // `renderSsKey` adds " [derived]" suffix for derived keys;
            // `firstKind.SsKey` is OssysOriginal in `sampleCatalog`, so
            // the suffix doesn't appear.
            Assert.Equal(SsKey.rootOriginal firstKind.SsKey, recovered)
        | None ->
            Assert.Fail "expected slice for first kind"
    | Error err ->
        Assert.Fail(sprintf "JsonEmitter.emitSlices returned %A" err)

[<Fact>]
let ``JsonEmitter.emit doc tree contains every emitSlices kind by ssKey root`` () =
    // The composer (`emit`) writes the doc-level wrapper plus each
    // per-kind JsonNode via `node.WriteTo(writer)` — no re-parse step.
    // This test demonstrates the round-trip: parse the emitted text
    // back to a JsonNode tree, walk to `modules[*].kinds[*].ssKey`,
    // and confirm the slice keys are present at the expected positions.
    let enriched = enrich sampleCatalog
    let docText = JsonEmitter.emit enriched
    let docNode = requireNode "JsonEmitter.emit" (JsonNode.Parse(docText))
    let versionNode = requireNode "doc.version" docNode.["version"]
    Assert.Equal(JsonEmitter.version, versionNode.GetValue<int>())
    let modules = (requireNode "doc.modules" docNode.["modules"]).AsArray()
    Assert.NotEmpty(modules)
    match JsonEmitter.emitSlices enriched with
    | Ok artifact ->
        let sliceKeys = ArtifactByKind.keys artifact
        let docKeys =
            seq {
                for m in modules do
                    match optNode m with
                    | Some moduleNode ->
                        let kinds =
                            (requireNode "module.kinds" moduleNode.["kinds"]).AsArray()
                        for k in kinds do
                            match optNode k with
                            | Some kindNode ->
                                let ssKeyField = requireNode "kind.ssKey" kindNode.["ssKey"]
                                yield ssKeyField.GetValue<string>()
                            | None -> ()
                    | None -> ()
            }
            |> Set.ofSeq
        let expectedKeyStrings =
            sliceKeys |> Set.map SsKey.rootOriginal
        Assert.Equal<Set<string>>(expectedKeyStrings, docKeys)
    | Error err ->
        Assert.Fail(sprintf "JsonEmitter.emitSlices: %A" err)
