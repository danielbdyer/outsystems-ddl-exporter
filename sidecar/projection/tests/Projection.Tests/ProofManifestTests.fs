module Projection.Tests.ProofManifestTests

open System
open Xunit
open Projection.Core
open Projection.Pipeline

// P2-S1 — the portable proof manifest's codec (`ProofManifest`). The manifest is
// the SOURCE side's per-kind `RowDigestFold` digests + capture provenance,
// written for a later OFFLINE reconcile (`check fidelity --against`, P2-S3)
// against a target the tool did not stage. Its codec is a TOTAL round-trip, its
// bytes are deterministic (kind order-independent), and its version + digest
// plane are GATED — a foreign version or a foreign plane reconciles to NOTHING,
// never a false parse (the fail-closed discipline: a torn manifest is no
// manifest). The GoldenCodec / FidelityProofCache law shape.

let private kKey (name: string) : SsKey = SsKey.synthesized "MANIFEST_KIND" name |> Result.value

let private mkKind (name: string) (agg: string) (count: int64) : ManifestKind =
    { Kind = kKey name; KindName = name; Digest = { Aggregate = agg; Count = count } }

let private sample : ProofManifest =
    { SourceLabel = "cloud-dev"
      CapturedAtUtc = DateTimeOffset(2026, 7, 19, 12, 30, 15, TimeSpan.Zero)
      ModelHash = "ABCDEF0123456789"
      TolerancesInForce = [ "ansi-padding"; "decimal-scale" ]
      Kinds = [ mkKind "Customer" "AA11BB22" 42L; mkKind "Order" "CC33DD44" 7L ] }

[<Fact>]
let ``P2-S1: the manifest codec round-trips (deserialize (serialize m) = Some m, kinds canonically sorted)`` () =
    // toJson emits kinds in SsKey-sorted order (deterministic bytes, T1); the
    // round-trip is identity up to that canonical ordering.
    let canonical = { sample with Kinds = sample.Kinds |> List.sortBy (fun k -> SsKey.serialize k.Kind) }
    Assert.Equal<ProofManifest option>(Some canonical, ProofManifest.tryParse (ProofManifest.toJson sample))

[<Fact>]
let ``P2-S1: manifest JSON is deterministic — kind input order does not move the bytes`` () =
    let reordered = { sample with Kinds = List.rev sample.Kinds }
    Assert.Equal(ProofManifest.toJson sample, ProofManifest.toJson reordered)

[<Fact>]
let ``P2-S1: an empty tolerance set and a zero-count kind round-trip`` () =
    let m = { sample with TolerancesInForce = []; Kinds = [ mkKind "EmptyKind" "00" 0L ] }
    Assert.Equal<ProofManifest option>(Some m, ProofManifest.tryParse (ProofManifest.toJson m))

[<Fact>]
let ``P2-S1: a FOREIGN version fails closed (reconciles to nothing, never a false parse)`` () =
    // If the substitution found no target the json stays valid and tryParse
    // returns Some — so this assertion also guards against a vacuous test.
    let json = (ProofManifest.toJson sample).Replace("\"version\": 1", "\"version\": 999")
    Assert.Equal<ProofManifest option>(None, ProofManifest.tryParse json)

[<Fact>]
let ``P2-S1: a FOREIGN digest plane fails closed`` () =
    let json = (ProofManifest.toJson sample).Replace("rowDigestFold", "serverDigest")
    Assert.Equal<ProofManifest option>(None, ProofManifest.tryParse json)

[<Fact>]
let ``P2-S1: garbage and a missing load-bearing field fail closed`` () =
    Assert.Equal<ProofManifest option>(None, ProofManifest.tryParse "{ not json")
    // A renamed modelHash makes the load-bearing field ABSENT — fail closed.
    let noHash = (ProofManifest.toJson sample).Replace("\"modelHash\"", "\"modelHashX\"")
    Assert.Equal<ProofManifest option>(None, ProofManifest.tryParse noHash)
