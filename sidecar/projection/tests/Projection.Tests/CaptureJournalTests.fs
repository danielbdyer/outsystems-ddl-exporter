module Projection.Tests.CaptureJournalTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline

// CONSTELLATION_BACKLOG card F4 / plane N5: the chunk-resume journal was
// exercised only through Docker-gated streaming-transfer integration
// suites — zero pure-pool tests of its load/append/fingerprint
// semantics. These pin that surface before R3's L2 re-expresses the
// journal on the `LedgerSpec` contract: load/append round-trip (the
// fingerprint fields included), missing-file = fresh run, the
// (kind, chunkIx) last-wins index, blank/null-line tolerance, the
// digest-keyed filename law, and the malformed-line behavior (pinned as
// observed, not as wished).

let private withTempDir (action: string -> 'a) : 'a =
    let root =
        Path.Combine(
            Path.GetTempPath(),
            sprintf "capture-journal-%s" (Guid.NewGuid().ToString("N").Substring(0, 12)))
    Directory.CreateDirectory root |> ignore
    try action root
    finally
        if Directory.Exists root then
            try Directory.Delete(root, recursive = true) with _ -> ()

let private chunk (kind: string) (ix: int) (firstPk: string) (lastPk: string) (raw: int) (pairs: string[][]) : ChunkRecord =
    { Kind = kind
      ChunkIx = ix
      FirstPk = firstPk
      LastPk = lastPk
      RawCount = raw
      WrittenCount = raw
      Pairs = pairs }

// -- missing file = fresh run ---------------------------------------------

[<Fact>]
let ``load: a never-written journal is an empty index (a fresh run)`` () =
    withTempDir (fun dir ->
        let j = CaptureJournal.create dir "plan-A"
        Assert.False(File.Exists(CaptureJournal.filePath j))
        Assert.Empty(CaptureJournal.load j))

// -- round-trip (fingerprint + pairs preserved) ---------------------------

[<Fact>]
let ``append then load round-trips a ChunkRecord including its fingerprint and pairs`` () =
    withTempDir (fun dir ->
        let j = CaptureJournal.create dir "plan-A"
        let rec0 = chunk "OS_USER" 0 "1" "50000" 50000 [| [| "1"; "9001" |]; [| "2"; "9002" |] |]
        CaptureJournal.append j rec0
        let loaded = CaptureJournal.load j
        Assert.True(loaded.ContainsKey(("OS_USER", 0)))
        let back = loaded[("OS_USER", 0)]
        Assert.Equal("1", back.FirstPk)
        Assert.Equal("50000", back.LastPk)
        Assert.Equal(50000, back.RawCount)
        Assert.Equal(50000, back.WrittenCount)
        Assert.Equal<string[][]>(rec0.Pairs, back.Pairs))

// -- accumulation across appends ------------------------------------------

[<Fact>]
let ``append accumulates distinct chunks; load returns every one`` () =
    withTempDir (fun dir ->
        let j = CaptureJournal.create dir "plan-A"
        CaptureJournal.append j (chunk "OS_USER" 0 "1" "5" 5 [||])
        CaptureJournal.append j (chunk "OS_USER" 1 "6" "10" 5 [||])
        CaptureJournal.append j (chunk "OS_ORDER" 0 "1" "3" 3 [||])
        let loaded = CaptureJournal.load j
        Assert.Equal(3, loaded.Count)
        Assert.True(loaded.ContainsKey(("OS_USER", 1)))
        Assert.True(loaded.ContainsKey(("OS_ORDER", 0))))

// -- the (kind, chunkIx) index: last write wins ---------------------------

[<Fact>]
let ``load: a re-appended (kind, chunkIx) is overwritten — the latest record wins`` () =
    withTempDir (fun dir ->
        let j = CaptureJournal.create dir "plan-A"
        CaptureJournal.append j (chunk "OS_USER" 0 "1" "5" 5 [||])
        CaptureJournal.append j (chunk "OS_USER" 0 "1" "5" 99 [||])
        let loaded = CaptureJournal.load j
        Assert.Equal(1, loaded.Count)
        Assert.Equal(99, loaded[("OS_USER", 0)].RawCount))

// -- blank / null line tolerance ------------------------------------------

[<Fact>]
let ``load: blank and literal-null lines are skipped; valid records still load`` () =
    withTempDir (fun dir ->
        let j = CaptureJournal.create dir "plan-A"
        CaptureJournal.append j (chunk "OS_USER" 0 "1" "5" 5 [||])
        // Inject a blank line and a literal JSON null between valid records.
        File.AppendAllText(CaptureJournal.filePath j, "\n   \nnull\n")
        CaptureJournal.append j (chunk "OS_USER" 1 "6" "10" 5 [||])
        let loaded = CaptureJournal.load j
        Assert.Equal(2, loaded.Count)
        Assert.True(loaded.ContainsKey(("OS_USER", 0)))
        Assert.True(loaded.ContainsKey(("OS_USER", 1))))

// -- the digest-keyed filename law (the private-ctor invariant) -----------

[<Fact>]
let ``create: the same marker yields the same file; different markers differ`` () =
    withTempDir (fun dir ->
        let a1 = CaptureJournal.create dir "plan-A" |> CaptureJournal.filePath
        let a2 = CaptureJournal.create dir "plan-A" |> CaptureJournal.filePath
        let b = CaptureJournal.create dir "plan-B" |> CaptureJournal.filePath
        Assert.Equal(a1, a2)
        Assert.NotEqual<string>(a1, b)
        // The name is digest-derived, never the raw marker (no unaddressed journal).
        Assert.DoesNotContain("plan-A", Path.GetFileName a1)
        Assert.EndsWith(".ndjson", a1))

// -- malformed-line behavior (pinned as OBSERVED) -------------------------
// A truly malformed (non-JSON) line is NOT the literal `null` the load
// loop tolerates: `JsonSerializer.Deserialize<ChunkRecord>` throws on it.
// Pinned here so R3's L2 inherits a KNOWN contract — if the journal is to
// tolerate corruption, that is a deliberate change against this witness,
// not a silent assumption.

[<Fact>]
let ``load: a corrupt (non-JSON) line throws — the resume surface is not silently lossy`` () =
    withTempDir (fun dir ->
        let j = CaptureJournal.create dir "plan-A"
        CaptureJournal.append j (chunk "OS_USER" 0 "1" "5" 5 [||])
        File.AppendAllText(CaptureJournal.filePath j, "this-is-not-json\n")
        Assert.ThrowsAny<exn>(fun () -> CaptureJournal.load j |> ignore) |> ignore)

// -- L2: the journal grain on the ledger contract (R3 / RI-3) --------------
// The contract instance over REAL journal records: the chain form, the
// resume point, drift detection, and the effectful remap fold adapted at
// the instance. The operator-facing named refusal
// (transfer.resume.sourceDrift) and the no-duplicates equivalence ride the
// Docker ReverseLegStreaming witnesses, unchanged.

let private testKind : SsKey =
    SsKey.synthesized "OS_TEST_CJ" "User"
    |> function Ok k -> k | Error e -> failwithf "fixture: %A" e

[<Fact>]
let ``R3: crash at chunk k resumes at k — the journaled kind's chain through Ledger.resumePoint`` () =
    withTempDir (fun dir ->
        let j = CaptureJournal.create dir "plan-A"
        CaptureJournal.append j (chunk "OS_USER" 0 "1" "5" 5 [||])
        CaptureJournal.append j (chunk "OS_USER" 1 "6" "10" 5 [||])
        CaptureJournal.append j (chunk "OS_USER" 2 "11" "15" 5 [||])
        CaptureJournal.append j (chunk "OS_ORDER" 0 "1" "3" 3 [||])
        let userChain =
            CaptureJournal.load j
            |> Seq.filter (fun kv -> fst kv.Key = "OS_USER")
            |> Seq.map (fun kv -> CaptureJournal.toEntry kv.Value)
            |> List.ofSeq
        Assert.Equal(3, Ledger.resumePoint userChain))

[<Fact>]
let ``R3: drift refuses by name — the live slice's recomputed fingerprint disagrees at the chunk`` () =
    let recorded = CaptureJournal.toEntry (chunk "OS_USER" 4 "201" "250" 50 [||])
    match Ledger.resumeAdmit ("201", "250", 49) recorded with
    | Ok _ -> failwith "a drifted source slice must never silently admit"
    | Error drift ->
        Assert.Equal(4, drift.Position)
        Assert.Equal(("201", "250", 50), drift.Recorded)
        Assert.Equal(("201", "250", 49), drift.Recomputed)

[<Fact>]
let ``R3: journaled pairs rebuild the remap through replay — the effectful fold adapted at the instance`` () =
    let remap = PackedSurrogateRemap.create ()
    let records =
        [ chunk "OS_USER" 0 "1" "2" 2 [| [| "1"; "9001" |]; [| "2"; "9002" |] |]
          chunk "OS_USER" 1 "3" "4" 2 [| [| "3"; "9003" |] |] ]
    let admitted =
        records
        |> List.map (fun r ->
            match Ledger.resumeAdmit (CaptureJournal.fingerprintOf r) (CaptureJournal.toEntry r) with
            | Ok token -> token
            | Error drift -> failwithf "fixture: unexpected drift at %d" drift.Position)
    let replayed = Ledger.replay (CaptureJournal.spec testKind remap) admitted
    // Apply folds INTO the shared accumulator (Genesis), never beside it.
    Assert.Same(remap, replayed)
    Assert.Equal(Some "9001", PackedSurrogateRemap.tryFind remap testKind "1")
    Assert.Equal(Some "9002", PackedSurrogateRemap.tryFind remap testKind "2")
    Assert.Equal(Some "9003", PackedSurrogateRemap.tryFind remap testKind "3")
    Assert.Equal(None, PackedSurrogateRemap.tryFind remap testKind "4")
