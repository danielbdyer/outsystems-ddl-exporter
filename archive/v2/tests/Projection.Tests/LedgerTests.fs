module Projection.Tests.LedgerTests

open Xunit
open FsCheck.Xunit
open Projection.Core

// CONSTELLATION_BACKLOG card L1 (the ledger contract, R3, corrected per
// RI-3). The contract under witness: `LedgerSpec` is the pure chain
// algebra (Genesis / Apply / FingerprintOf); admission is SPLIT —
// `writeAdmit` mints `Verified<_>` on an external witness, `resumeAdmit`
// mints it on fingerprint recomputation against the live source;
// `replay` is the FTC fold over verified entries; `resumePoint` is the
// first position absent from the chain. The journal (L2), the episode
// store (L3), and the G10 marker (L4) instantiate this; these witnesses
// pin the algebra itself over a constructed-valid generic instance.

/// The constructed-valid instance: a partial-sum ledger over ints. The
/// fingerprint is derived from the entry, so "recomputation" is honest:
/// the same entry recomputes the same fingerprint; a drifted entry does
/// not.
let private sumSpec : LedgerSpec<int, int, string> =
    { Genesis = 0
      Apply = (+)
      FingerprintOf = fun e -> sprintf "fp:%d" e }

let private admitAll (entries: int list) : Verified<int> list =
    entries
    |> List.map (Ledger.writeAdmit (fun _ -> Ok ()))
    |> List.map (function Ok v -> v | Error e -> failwithf "fixture: witness refused %A" e)

let private chainOf (entries: int list) : LedgerEntry<int, string> list =
    entries |> List.mapi (Ledger.entryOf sumSpec)

// -- the FTC: replay = fold ⊕ ----------------------------------------------

[<Property>]
let ``R3: replay = fold ⊕ — the FTC at the contract grain`` (entries: int list) =
    Ledger.replay sumSpec (admitAll entries) = List.fold (+) 0 entries

[<Property>]
let ``R3: replay over a chain plus one entry = Apply of the replayed prefix — the partial-sum step`` (prefix: int list) (next: int) =
    let viaChain = Ledger.replay sumSpec (admitAll (prefix @ [ next ]))
    let viaStep = sumSpec.Apply (Ledger.replay sumSpec (admitAll prefix)) next
    viaChain = viaStep

// -- resume: crash at k resumes at k; drift refuses by name -----------------

[<Property>]
let ``R3: crash at chunk k resumes at k — the gapless chain's resume point is its length`` (entries: int list) =
    // A run that crashed after journaling k chunks left positions 0..k−1.
    Ledger.resumePoint (chainOf entries) = List.length entries

[<Fact>]
let ``R3: a crash hole resumes at the hole — the chain is an index, not a prefix`` () =
    let recorded =
        [ 0, 10; 1, 11; 3, 13 ]
        |> List.map (fun (position, entry) -> Ledger.entryOf sumSpec position entry)
    Assert.Equal(2, Ledger.resumePoint recorded)

[<Fact>]
let ``R3: resumeAdmit admits on recomputed = recorded; the token carries the entry`` () =
    let recorded = Ledger.entryOf sumSpec 4 42
    match Ledger.resumeAdmit (sumSpec.FingerprintOf 42) recorded with
    | Ok token -> Assert.Equal(42, Verified.value token)
    | Error drift -> failwithf "an unchanged source must admit; drifted at %d" drift.Position

[<Fact>]
let ``R3: fingerprint drift refuses by name — position and both fingerprints on the refusal`` () =
    let recorded = Ledger.entryOf sumSpec 7 42
    match Ledger.resumeAdmit (sumSpec.FingerprintOf 43) recorded with
    | Ok _ -> failwith "a drifted source must never silently admit"
    | Error drift ->
        Assert.Equal(7, drift.Position)
        Assert.Equal("fp:42", drift.Recorded)
        Assert.Equal("fp:43", drift.Recomputed)

// -- write admission: the external witness mints the token ------------------

[<Fact>]
let ``R3: writeAdmit mints the token only on a passing witness`` () =
    match Ledger.writeAdmit (fun _ -> Ok ()) 42 with
    | Ok token -> Assert.Equal(42, Verified.value token)
    | Error (_: string) -> failwith "a passing witness must mint"

[<Fact>]
let ``R3: writeAdmit propagates a refusing witness — no token exists for an unverified entry`` () =
    match Ledger.writeAdmit (fun e -> Error (sprintf "unverified:%d" e)) 42 with
    | Ok _ -> failwith "a refusing witness must never mint"
    | Error message -> Assert.Equal("unverified:42", message)

[<Fact>]
let ``R3: entryOf stamps the spec's fingerprint at write time — resume recomputation closes the loop`` () =
    // Write-time stamps FingerprintOf; resume-time recomputes the same
    // function over the (unchanged) live entry: the loop must close.
    let recorded = Ledger.entryOf sumSpec 0 99
    Assert.Equal("fp:99", recorded.Fingerprint)
    match Ledger.resumeAdmit (sumSpec.FingerprintOf 99) recorded with
    | Ok token -> Assert.Equal(99, Verified.value token)
    | Error _ -> failwith "the write-time stamp and the resume-time recomputation are the same function"
