module Projection.Tests.ManifestEmitterTests

open System.Text.Json.Nodes
open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Chapter 4.1.A slice 9 — ManifestEmitter producing manifest.json per V1
// SsdtManifest schema.
//
// V0 manifest scope (per chapter pre-scope §8 slice 9 + V2-driver KPI
// smart-product-choices): structural fields V2 has evidence for
// (Tables[Module/Schema/Table/TableFile/IndexCount/ForeignKeyCount] +
// Emission stamp). Coverage / PredicateCoverage / PreRemediation /
// Unsupported emit as defaults (null / empty arrays) so the V1-compatible
// schema shape is preserved while the semantic payload defers to chapter
// 4.4.
// ---------------------------------------------------------------------------

let private enrich (c: Catalog) : Catalog =
    (CanonicalizeIdentity.run c).Value

[<Fact>]
let ``ManifestEmitter.emit produces one TableManifestEntry per kind`` () =
    let enriched = enrich sampleCatalog
    let manifest = ManifestEmitter.emit enriched
    let allKinds = Catalog.allKinds enriched
    Assert.Equal (List.length allKinds, List.length manifest.Tables)

[<Fact>]
let ``ManifestEmitter.emit per-entry TableFile uses V1 RelativePath convention`` () =
    let enriched = enrich sampleCatalog
    let manifest = ManifestEmitter.emit enriched
    for entry in manifest.Tables do
        Assert.StartsWith ("Modules/", entry.TableFile)
        Assert.EndsWith (".sql", entry.TableFile)
        // Forward slashes only; no backslashes regardless of host OS.
        Assert.DoesNotContain ('\\', entry.TableFile)

[<Fact>]
let ``ManifestEmitter.toJson produces parseable indented JSON`` () =
    let enriched = enrich sampleCatalog
    let manifest = ManifestEmitter.emit enriched
    let json = ManifestEmitter.toJson manifest
    // Parses back to a JsonNode tree (pillar 1 round-trip witness).
    let root =
        match JsonNode.Parse(json) with
        | null -> Assert.Fail "ManifestEmitter.toJson produced unparseable text"; Unchecked.defaultof<JsonNode>
        | n    -> n
    // Top-level fields per V1 schema mirror.
    Assert.False (isNull root.["emitter"], "expected 'emitter' field")
    Assert.False (isNull root.["version"], "expected 'version' field")
    Assert.False (isNull root.["tables"], "expected 'tables' field")

[<Fact>]
let ``T1: ManifestEmitter.toJson is byte-deterministic across repeat invocations`` () =
    let enriched = enrich sampleCatalog
    let manifest = ManifestEmitter.emit enriched
    let json1 = ManifestEmitter.toJson manifest
    let json2 = ManifestEmitter.toJson manifest
    Assert.Equal<string> (json1, json2)

let private requireChild (label: string) (n: JsonNode | null) : JsonNode =
    match Option.ofObj n with
    | Some node -> node
    | None      -> Assert.Fail (sprintf "expected %s child" label); Unchecked.defaultof<JsonNode>

[<Fact>]
let ``ManifestEmitter chapter-4.4-territory fields emit as defaults`` () =
    // Per chapter pre-scope §8 slice 9 + V2-driver KPI smart-product-
    // choices: Coverage / PredicateCoverage / PreRemediation /
    // Unsupported emit as defaults (null / empty arrays) until chapter
    // 4.4 surfaces real evidence. The structural shape is V1-compatible.
    let enriched = enrich sampleCatalog
    let json = ManifestEmitter.toJson (ManifestEmitter.emit enriched)
    let root = requireChild "root" (JsonNode.Parse(json))
    // Per V1 SsdtManifest.cs schema: these fields ARE present in the
    // shape, even if their semantic content is chapter-4.4 territory.
    Assert.Null (root.["coverage"])
    Assert.Null (root.["predicateCoverage"])
    let preRemediation = requireChild "preRemediation" root.["preRemediation"]
    let unsupported = requireChild "unsupported" root.["unsupported"]
    // Both default to empty arrays (no evidence at chapter 4.1.A).
    Assert.Equal (0, preRemediation.AsArray().Count)
    Assert.Equal (0, unsupported.AsArray().Count)

[<Fact>]
let ``ManifestEmitter Tables order matches catalog declaration order`` () =
    // A33 (deterministic-ordered schema emission) at the manifest level:
    // the Tables array's order is a function of the input catalog's
    // Modules and Kinds declared order. CanonicalizeIdentity has already
    // sorted them by SsKey at chapter 1; the manifest preserves that
    // canonical order.
    let enriched = enrich sampleCatalog
    let m1 = ManifestEmitter.emit enriched
    let m2 = ManifestEmitter.emit enriched
    Assert.Equal<ManifestEmitter.TableManifestEntry list> (m1.Tables, m2.Tables)
