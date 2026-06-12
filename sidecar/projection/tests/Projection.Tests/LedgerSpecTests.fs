module Projection.Tests.LedgerSpecTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core

// Card L1 (CONSTELLATION_BACKLOG stage 3) — the partial-sum ledger contract,
// corrected per RI-3: Genesis/Apply/FingerprintOf plus the admission split
// (WriteAdmit external-witness-capable, minting `Verified<_>`; ResumeAdmit
// fingerprint recomputation, drift a named refusal). Pure pool: the spec is
// Core algebra — the boundary instances (the journal, the episode store)
// cut over at L2/L3 against this pinned surface.

/// The constructed-valid instance the properties run over: integer quanta,
/// addition as ⊕, and a fingerprint that depends on the quantum's value
/// (so corruption is observable).
let private intSpec : LedgerSpec<int, int, string> =
    { Genesis       = 0
      Apply         = (+)
      FingerprintOf = fun q -> sprintf "fp:%d" q }

let private admitAll (quanta: int list) : Verified<LedgerEntry<int, string>> list =
    quanta
    |> List.mapi (fun position q ->
        Ledger.admitWrite (fun _ -> Result.success ()) intSpec position q
        |> Result.value)

let private entriesOf (quanta: int list) : LedgerEntry<int, string> list =
    quanta
    |> List.mapi (fun position q ->
        { Position = position; Fingerprint = intSpec.FingerprintOf q; Quantum = q })

// ---------------------------------------------------------------------------
// the FTC — replay = fold ⊕
// ---------------------------------------------------------------------------

[<Property>]
let ``R3: replay = fold ⊕ — the FTC at the contract`` (quanta: int list) =
    Ledger.replay intSpec (admitAll quanta) = List.fold (+) 0 quanta

[<Property>]
let ``R3: replay is position-ordered — entry list order is immaterial`` (quanta: int list) =
    // The chain's order is the recorded positions', not the list's: a
    // reversed (or any permuted) entry list replays to the same state.
    // Addition commutes, so the discriminating quantum is positional:
    // fold with a non-commutative ⊕ (string append of position-stamped
    // quanta) and compare against the in-order fold.
    let stampSpec : LedgerSpec<string, string, string> =
        { Genesis = ""; Apply = (+); FingerprintOf = id }
    let stamped = quanta |> List.map string
    let admitted =
        stamped
        |> List.mapi (fun position q ->
            Ledger.admitWrite (fun _ -> Result.success ()) stampSpec position q
            |> Result.value)
    Ledger.replay stampSpec (List.rev admitted) = List.fold (+) "" stamped

// ---------------------------------------------------------------------------
// WriteAdmit — the witness gates the mint
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3: WriteAdmit — a failing witness admits nothing, by name`` () =
    let refusal = ValidationError.create "ledger.write.unverified" "The chunk's commit was not witnessed."
    let result =
        Ledger.admitWrite (fun _ -> Result.failureOf refusal) intSpec 0 42
    Assert.Equal<string list>(
        [ "ledger.write.unverified" ],
        result |> Result.errors |> List.map (fun e -> e.Code))

[<Fact>]
let ``R3: WriteAdmit — a passing witness mints the fingerprinted entry`` () =
    let verified =
        Ledger.admitWrite (fun _ -> Result.success ()) intSpec 3 42 |> Result.value
    let entry = Verified.value verified
    Assert.Equal(3, entry.Position)
    Assert.Equal(42, entry.Quantum)
    Assert.Equal("fp:42", entry.Fingerprint)

// ---------------------------------------------------------------------------
// ResumeAdmit — crash at k resumes at k; drift refuses by name
// ---------------------------------------------------------------------------

[<Property>]
let ``R3: crash at chunk k resumes at k`` (quanta: int list) =
    // A run that journaled k chunks and crashed resumes at position k —
    // the first absent position — when every recorded fingerprint
    // recomputes (the source is unchanged).
    let recorded = entriesOf quanta
    let recompute (position: int) = intSpec.FingerprintOf quanta.[position]
    Ledger.resumePoint recorded recompute = Ok (List.length quanta)

[<Fact>]
let ``R3: fingerprint drift refuses by name — located at the first drifted position`` () =
    let quanta = [ 10; 20; 30; 40 ]
    let recorded = entriesOf quanta
    // The source changed under positions 2 and 3; the refusal locates the
    // FIRST drift, never silently re-runs over changed data.
    let recompute (position: int) =
        if position >= 2 then "fp:CHANGED" else intSpec.FingerprintOf quanta.[position]
    match Ledger.resumePoint recorded recompute with
    | Error drift ->
        Assert.Equal(2, drift.Position)
        Assert.Equal("fp:30", drift.Recorded)
        Assert.Equal("fp:CHANGED", drift.Recomputed)
    | Ok p -> Assert.Fail(sprintf "expected the drift refusal, got resume at %d" p)

[<Fact>]
let ``R3: an empty chain resumes at genesis`` () =
    Assert.Equal(Ok 0, Ledger.resumePoint [] (fun _ -> "unused"))

[<Fact>]
let ``R3: a duplicated position resolves last-write-wins — the journal index's semantics`` () =
    // CaptureJournal.load indexes lines by (kind, chunkIx) with later lines
    // overwriting earlier ones; the contract's resume honors the same rule.
    let recorded =
        [ { Position = 0; Fingerprint = "fp:old"; Quantum = 1 }
          { Position = 0; Fingerprint = "fp:new"; Quantum = 2 } ]
    Assert.Equal(Ok 1, Ledger.resumePoint recorded (fun _ -> "fp:new"))

[<Fact>]
let ``R3: entries beyond the first gap are ignored — they re-execute past the resume point`` () =
    let recorded =
        [ { Position = 0; Fingerprint = "fp:0"; Quantum = 0 }
          { Position = 2; Fingerprint = "fp:2"; Quantum = 2 } ]   // 1 is missing
    Assert.Equal(Ok 1, Ledger.resumePoint recorded (fun p -> sprintf "fp:%d" p))
