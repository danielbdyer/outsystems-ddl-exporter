module Projection.Tests.LedgerTests

open Xunit
open FsCheck.Xunit
open Projection.Core

// CONSTELLATION_BACKLOG card L1 (the ledger contract, R3, corrected per
// RI-3). The contract under witness: `LedgerSpec` is the pure chain
// algebra (Genesis / Apply / Admission); admission is SPLIT —
// `writeAdmit` mints `Verified<_>` on an external witness, `resumeAdmit`
// mints it on fingerprint recomputation against the live source, and
// `admitChain` mints a WHOLE chain by the spec's declared `ChainAdmission`
// discipline (align-III.2); `replay` is the FTC fold over verified
// entries; `resumePoint` is the first position absent from the chain. The
// journal (L2), the episode store (L3), and the G10 marker (L4)
// instantiate this; these witnesses pin the algebra itself over
// constructed-valid generic instances.

/// The chunk grain's projection — a fingerprint DERIVED from the entry, so
/// "recomputation" is honest: the same entry recomputes the same
/// fingerprint; a drifted entry does not.
let private sumFp (e: int) : string = sprintf "fp:%d" e

/// The constructed-valid `WitnessRecompute` instance: a partial-sum ledger
/// over ints (the capture-journal shape).
let private sumSpec : LedgerSpec<int, int, string> =
    { Genesis = 0
      Apply = (+)
      Admission = ChainAdmission.WitnessRecompute sumFp }

let private admitAll (entries: int list) : Verified<int> list =
    entries
    |> List.map (Ledger.writeAdmit (fun _ -> Ok ()))
    |> List.map (function Ok v -> v | Error e -> failwithf "fixture: witness refused %A" e)

let private chainOf (entries: int list) : LedgerEntry<int, string> list =
    entries |> List.mapi (Ledger.entryOf sumFp)

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
        |> List.map (fun (position, entry) -> Ledger.entryOf sumFp position entry)
    Assert.Equal(2, Ledger.resumePoint recorded)

[<Fact>]
let ``R3: resumeAdmit admits on recomputed = recorded; the token carries the entry`` () =
    let recorded = Ledger.entryOf sumFp 4 42
    match Ledger.resumeAdmit (sumFp 42) recorded with
    | Ok token -> Assert.Equal(42, Verified.value token)
    | Error drift -> failwithf "an unchanged source must admit; drifted at %d" drift.Position

[<Fact>]
let ``R3: fingerprint drift refuses by name — position and both fingerprints on the refusal`` () =
    let recorded = Ledger.entryOf sumFp 7 42
    match Ledger.resumeAdmit (sumFp 43) recorded with
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
    let recorded = Ledger.entryOf sumFp 0 99
    Assert.Equal("fp:99", recorded.Fingerprint)
    match Ledger.resumeAdmit (sumFp 99) recorded with
    | Ok token -> Assert.Equal(99, Verified.value token)
    | Error _ -> failwith "the write-time stamp and the resume-time recomputation are the same function"

// -- align-III.2: admitChain over the declared ChainAdmission discipline ----
// The whole-chain admission the sink journal and episode store reload
// through. Three disciplines, three laws — and the WitnessRecompute grain
// cannot be admitted offline (its fingerprints recompute from the source).

/// A Monotone grain over ints (the episode-store shape): the ordinal IS the
/// entry.
let private monoSpec : LedgerSpec<int, int, int> =
    { Genesis = 0; Apply = (fun _ e -> e); Admission = ChainAdmission.Monotone id }

/// A tiny Linkage grain (the sink-journal shape): entries sharing an `Id`
/// are one group; each group names the prior group as its `Prev`.
type private Link = { Id: int; Prev: int option }

let private linkSpec : LedgerSpec<unit, Link, int> =
    { Genesis = ()
      Apply = (fun _ _ -> ())
      Admission = ChainAdmission.Linkage ((fun l -> l.Id), (fun l -> l.Prev)) }

[<Fact>]
let ``align-III.2: admitChain Monotone admits a strictly increasing chain and refuses a regression by ordinal`` () =
    match Ledger.admitChain monoSpec [ 1; 2; 5 ] with
    | Ok verified -> Assert.Equal<int list>([ 1; 2; 5 ], verified |> List.map Verified.value)
    | Error r -> failwithf "a strictly increasing chain must admit; refused %A" r
    match Ledger.admitChain monoSpec [ 1; 3; 2 ] with
    | Ok _ -> failwith "a regression must never silently admit"
    | Error (ChainRefusal.OrdinalRegression (pos, ordinal, prior)) ->
        Assert.Equal(2, pos); Assert.Equal(2, ordinal); Assert.Equal(3, prior)
    | Error other -> failwithf "expected OrdinalRegression, got %A" other

[<Fact>]
let ``align-III.2: admitChain Linkage admits a grouped chain — each group naming the one before`` () =
    // Two lines at sync 1 (genesis, no predecessor), one at sync 2 (prev 1),
    // one at sync 3 (prev 2): a well-formed sink chain.
    let chain =
        [ { Id = 1; Prev = None }; { Id = 1; Prev = None }
          { Id = 2; Prev = Some 1 }; { Id = 3; Prev = Some 2 } ]
    match Ledger.admitChain linkSpec chain with
    | Ok verified -> Assert.Equal(4, List.length verified)
    | Error r -> failwithf "a linked chain must admit; refused %A" r

[<Fact>]
let ``align-III.2: admitChain Linkage refuses a broken predecessor link by name`` () =
    // Sync 2 claims predecessor 9, but the chain reached 1.
    match Ledger.admitChain linkSpec [ { Id = 1; Prev = None }; { Id = 2; Prev = Some 9 } ] with
    | Ok _ -> failwith "a broken link must never silently admit"
    | Error (ChainRefusal.BrokenLink (pos, claimed, expected)) ->
        Assert.Equal(1, pos); Assert.Equal(Some 9, claimed); Assert.Equal(Some 1, expected)
    | Error other -> failwithf "expected BrokenLink, got %A" other

[<Fact>]
let ``align-III.2: admitChain Linkage refuses a first group that claims a predecessor`` () =
    match Ledger.admitChain linkSpec [ { Id = 1; Prev = Some 5 } ] with
    | Ok _ -> failwith "the first group claims no predecessor"
    | Error (ChainRefusal.BrokenLink (pos, claimed, expected)) ->
        Assert.Equal(0, pos); Assert.Equal(Some 5, claimed); Assert.Equal(None, expected)
    | Error other -> failwithf "expected BrokenLink, got %A" other

[<Fact>]
let ``align-III.2: admitChain refuses a WitnessRecompute chain offline — its fingerprints recompute from the source`` () =
    // The tautology's retirement: a recompute grain has no offline whole-chain
    // admission; it admits per-entry against the live source (resumeAdmit).
    match Ledger.admitChain sumSpec [ 1; 2; 3 ] with
    | Ok _ -> failwith "a recompute chain must not admit offline"
    | Error ChainRefusal.RecomputeRequiresSource -> ()
    | Error other -> failwithf "expected RecomputeRequiresSource, got %A" other
