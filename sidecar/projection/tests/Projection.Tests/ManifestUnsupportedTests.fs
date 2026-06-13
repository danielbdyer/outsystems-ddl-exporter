module Projection.Tests.ManifestUnsupportedTests

open System.Text.Json.Nodes
open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — shim restoring the Lineage<Catalog> shape.
let private ciRun (c: Catalog) : Lineage<Catalog> =
    CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)

let private enrich (c: Catalog) : Catalog = (ciRun c).Value

let private requireChild (label: string) (n: JsonNode | null) : JsonNode =
    match Option.ofObj n with
    | Some node -> node
    | None      -> Assert.Fail (sprintf "expected %s child" label); Unchecked.defaultof<JsonNode>

// ---------------------------------------------------------------------------
// Chapter 4.4 slice γ — Unsupported field renders ToleratedDivergence.allKnown
// as sorted string list. Mirrors V1's `Unsupported : IReadOnlyList<string>`
// field shape per chapter 4.4 open Q3 resolved-at-open decision.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Unsupported.compute returns ToleratedDivergence.allKnown cardinality`` () =
    let result = Unsupported.compute ()
    Assert.Equal (Set.count ToleratedDivergence.allKnown, List.length result)

[<Fact>]
let ``Unsupported.compute returns sorted strings`` () =
    let result = Unsupported.compute ()
    let sorted = result |> List.sort
    Assert.Equal<string list> (sorted, result)

[<Fact>]
let ``Unsupported.compute is deterministic (T1)`` () =
    let r1 = Unsupported.compute ()
    let r2 = Unsupported.compute ()
    Assert.Equal<string list> (r1, r2)

[<Fact>]
let ``Unsupported.compute names match current ToleratedDivergence variants`` () =
    // The empirically-grounded variants. When the DU widens or shrinks,
    // this test surfaces it (closed-DU expansion empirical-test sibling).
    // 6.A.4 (2026-06-02) added EmptyTextNormalizedToNull. AC-D6 added the
    // two representation-only tolerances (Char/Decimal) that do not fire CDC.
    // NM-16 (2026-06-13) added the four kind-facet diff-erasure tolerances.
    let result = Unsupported.compute () |> Set.ofList
    let expected =
        Set.ofList
            [ "CharAnsiPaddingTolerated"
              "DecimalScaleTolerated"
              "EmptyTextNormalizedToNull"
              "HeaderCommentsOmitted"
              "IndexOptionsUnreflected"
              "KindActivationUnreflectedInDiff"
              "KindChecksUnreflectedInDiff"
              "KindModalityUnreflectedInDiff"
              "KindTriggersUnreflectedInDiff"
              "PostDeployForeignKeysSplit"
              "StaticPopulationsUnreflected" ]
    Assert.Equal<Set<string>> (expected, result)

[<Fact>]
let ``Unsupported.compute returns non-empty (V2 always emits with at least one named divergence at chapter 4.4 close)`` () =
    let result = Unsupported.compute ()
    Assert.NotEmpty result

// ---------------------------------------------------------------------------
// Manifest emission: Unsupported flows through to JSON shape.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Manifest unsupported emits JSON array of strings in sorted order`` () =
    let enriched = enrich sampleCatalog
    let json = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let root = requireChild "root" (JsonNode.Parse(json))
    let unsupported = requireChild "unsupported" root.["unsupported"]
    let arr = unsupported.AsArray()
    let actualNames =
        [ for i in 0 .. arr.Count - 1 ->
            let node = requireChild "unsupported.entry" arr.[i]
            node.GetValue<string>() ]
    // Sorted alphabetically; matches Unsupported.compute output.
    Assert.Equal<string list> (Unsupported.compute (), actualNames)

[<Fact>]
let ``T1: Manifest emission with Unsupported is byte-deterministic`` () =
    let enriched = enrich sampleCatalog
    let json1 = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let json2 = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    Assert.Equal<string> (json1, json2)
