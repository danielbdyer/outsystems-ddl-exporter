module Projection.Tests.FidelityProofCacheTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.IRBuilders
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The incremental fidelity-proof cache (`FidelityProofCache` — wave B6). The
// laws under test:
//   - ROUND-TRIP: `tryRead` after `write` returns the cached proof (the model
//     hash, the fingerprints, the counts).
//   - FRESHNESS: `isFresh` holds when the model shape AND every source
//     fingerprint are unchanged; a moved fingerprint or a model retype makes it
//     stale (so the expensive proof re-runs).
//   - CLEARABILITY: `clear` removes one flow's entry (the --refresh clear);
//     `clearAll` removes the whole proofs dir; the cache lives in ONE named
//     directory, so an operator clears it trivially.
//   - FAIL-CLOSED: an absent or corrupt entry reads as `None` (a torn cache
//     never blocks a proof, never masquerades as fresh).
//   - CONTAINMENT: a flow name with path separators cannot escape the proofs
//     directory (the flow name is operator-supplied).
// ---------------------------------------------------------------------------

let private fp (kind: SsKey) (rows: int64) (maxPk: string option) (hash: string) : KindFingerprint =
    { Kind = kind; RowCount = rows; MaxPk = maxPk; ContentHash = None; SchemaShapeHash = hash }

let private withTempRoot (f: string -> unit) : unit =
    let root = Path.Combine(Path.GetTempPath(), "proof-cache-" + Guid.NewGuid().ToString "N")
    try f root
    finally if Directory.Exists root then Directory.Delete(root, true)

let private customerKind = kindKey ["Customer"]
let private orderKind    = kindKey ["Order"]

let private sampleProof : CachedProof =
    { ModelHash = "model-hash-1"
      Fingerprints = [ fp customerKind 4200L (Some "4200") "shape-c"; fp orderKind 900L (Some "900") "shape-o" ]
      RowsCompared = 5100L
      DifferenceTotal = 0L
      WrittenAtUtc = DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero) }

// -- round-trip ---------------------------------------------------------------

[<Fact>]
let ``proof cache: tryRead after write returns the cached proof`` () =
    withTempRoot (fun root ->
        FidelityProofCache.write root "uat-load" sampleProof
        match FidelityProofCache.tryRead root "uat-load" with
        | Some got ->
            Assert.Equal("model-hash-1", got.ModelHash)
            Assert.Equal(5100L, got.RowsCompared)
            Assert.Equal(0L, got.DifferenceTotal)
            Assert.Equal<KindFingerprint list>(
                sampleProof.Fingerprints |> List.sortBy (fun f -> SsKey.serialize f.Kind),
                got.Fingerprints |> List.sortBy (fun f -> SsKey.serialize f.Kind))
        | None -> Assert.True(false, "the written proof should read back"))

// -- freshness ----------------------------------------------------------------

[<Fact>]
let ``proof cache: isFresh holds when the model and every fingerprint are unchanged`` () =
    Assert.True(FidelityProofCache.isFresh sampleProof "model-hash-1" sampleProof.Fingerprints)

[<Fact>]
let ``proof cache: a moved fingerprint (a row added) makes the proof stale`` () =
    // Customer gains rows — its (RowCount, MaxPk) fingerprint moves.
    let moved = [ fp customerKind 4300L (Some "4300") "shape-c"; fp orderKind 900L (Some "900") "shape-o" ]
    Assert.False(FidelityProofCache.isFresh sampleProof "model-hash-1" moved)

[<Fact>]
let ``proof cache: a changed model shape makes the proof stale even when fingerprints hold`` () =
    Assert.False(FidelityProofCache.isFresh sampleProof "model-hash-2" sampleProof.Fingerprints)

// -- clearability -------------------------------------------------------------

[<Fact>]
let ``proof cache: clear removes one flow's entry; tryRead then reads absent`` () =
    withTempRoot (fun root ->
        FidelityProofCache.write root "uat-load" sampleProof
        FidelityProofCache.write root "qa-load" sampleProof
        Assert.True(FidelityProofCache.clear root "uat-load")     // a file was removed
        Assert.True(FidelityProofCache.tryRead root "uat-load" |> Option.isNone)
        // clearing one flow leaves the other intact.
        Assert.True(FidelityProofCache.tryRead root "qa-load" |> Option.isSome)
        // clearing an already-clear flow is idempotent (no file removed).
        Assert.False(FidelityProofCache.clear root "uat-load"))

[<Fact>]
let ``proof cache: clearAll removes the whole proofs directory`` () =
    withTempRoot (fun root ->
        FidelityProofCache.write root "uat-load" sampleProof
        FidelityProofCache.write root "qa-load" sampleProof
        Assert.True(FidelityProofCache.clearAll root)
        Assert.True(FidelityProofCache.tryRead root "uat-load" |> Option.isNone)
        Assert.True(FidelityProofCache.tryRead root "qa-load" |> Option.isNone))

// -- fail-closed --------------------------------------------------------------

[<Fact>]
let ``proof cache: an absent entry reads as None`` () =
    withTempRoot (fun root ->
        Assert.True(FidelityProofCache.tryRead root "never-proven" |> Option.isNone))

[<Fact>]
let ``proof cache: a corrupt entry reads as None (fail-closed, never blocks a proof)`` () =
    withTempRoot (fun root ->
        let path = FidelityProofCache.cachePath root "uat-load"
        Directory.CreateDirectory(FidelityProofCache.proofsDir root) |> ignore
        File.WriteAllText(path, "{ this is not valid json ")
        Assert.True(FidelityProofCache.tryRead root "uat-load" |> Option.isNone))

// -- containment --------------------------------------------------------------

[<Fact>]
let ``proof cache: a flow name with path separators cannot escape the proofs directory`` () =
    withTempRoot (fun root ->
        let dodgy = FidelityProofCache.cachePath root "../../etc/passwd"
        let proofs = FidelityProofCache.proofsDir root
        // The resolved cache path stays under the proofs directory.
        Assert.StartsWith(proofs, Path.GetFullPath dodgy))

// -- modelHash ----------------------------------------------------------------

/// Two one-kind catalogs identical but for an attribute's type — the model hash
/// must move (a retype invalidates a proof).
let private oneKindCatalog (attrType: PrimitiveType) : Catalog =
    let k = kindKey ["Thing"]
    let idK = attrKey ["Thing"; "Id"]
    let valK = attrKey ["Thing"; "Value"]
    let kind : Kind =
        Kind.create k (mkName "Thing") (mkTableId "dbo" "OSUSR_X_THING")
            [ { Attribute.create idK (mkName "Id") Integer with
                  Column = ColumnRealization.create "ID" false |> Result.value
                  IsPrimaryKey = true; IsMandatory = true }
              { Attribute.create valK (mkName "Value") attrType with
                  Column = ColumnRealization.create "VALUE" false |> Result.value } ]
    mkCatalog [ mkModule (modKey "M") (mkName "M") [ kind ] ]

[<Fact>]
let ``proof cache: modelHash is deterministic and moves when a kind's shape changes`` () =
    let a = oneKindCatalog Text
    let a2 = oneKindCatalog Text
    let b = oneKindCatalog Integer
    Assert.Equal(FidelityProofCache.modelHash a, FidelityProofCache.modelHash a2)          // deterministic
    Assert.True(FidelityProofCache.modelHash a <> FidelityProofCache.modelHash b, "a retype moves the model hash")
