module Projection.Tests.CorrectionCodecTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Targets.Json

// THE_SYNTHETIC_DATA_FUZZING.md §2.4 (slice F0b) — the blessed correction
// artifact's durable round-trip law `∀ c. deserialize (serialize c) = Ok c`
// (the codec discipline over a constructed-valid generator), example anchors
// covering every CorrectionEntry / PiiKind / ValueFidelityMode arm, and the
// A39 decode re-validation (a hand-edited artifact with a conflicting double
// correction is REFUSED on load).

let private value (r: Result<'a>) : 'a = Result.value r
let private aKey (i: int) : SsKey = SsKey.synthesizedComposite "CORR_ATTR" [ string i ] |> value

let private corr (entries: CorrectionEntry list) : Correction = Correction.create entries |> value

let private roundTrips (c: Correction) : bool =
    match CorrectionCodec.deserialize (CorrectionCodec.serialize c) with
    | Ok c' -> c' = c
    | Error _ -> false

// ---------------------------------------------------------------------------
// Example anchors — pinpoint which arm regressed.
// ---------------------------------------------------------------------------

[<Fact>]
let ``round-trip: the empty correction`` () =
    Assert.True(roundTrips Correction.empty)

[<Fact>]
let ``round-trip: every PiiKind arm`` () =
    let kinds =
        [ PiiKind.None; PiiKind.Email; PiiKind.PersonName; PiiKind.Phone
          PiiKind.Address; PiiKind.FreeText; PiiKind.Reference ]
    // distinct column per entry → no fidelity-class conflict.
    let entries = kinds |> List.mapi (fun i k -> CorrectionEntry.Pii (aKey i, k))
    Assert.True(roundTrips (corr entries))

[<Fact>]
let ``round-trip: every ValueFidelityMode arm`` () =
    let entries =
        [ CorrectionEntry.Fidelity (aKey 0, ValueFidelityMode.Preserve)
          CorrectionEntry.Fidelity (aKey 1, ValueFidelityMode.Synthesize) ]
    Assert.True(roundTrips (corr entries))

[<Fact>]
let ``round-trip: a mixed multi-entry correction (Pii + Fidelity, distinct columns)`` () =
    let entries =
        [ CorrectionEntry.Pii (aKey 0, PiiKind.Email)
          CorrectionEntry.Fidelity (aKey 1, ValueFidelityMode.Preserve)
          CorrectionEntry.Pii (aKey 2, PiiKind.Reference) ]
    Assert.True(roundTrips (corr entries))

[<Fact>]
let ``A39: decode REFUSES a hand-edited artifact with a conflicting double-correction`` () =
    // Two pii entries for the SAME column — a valid `serialize` could never
    // produce this (the smart ctor forbids it), but a hand edit can. Decode
    // must re-prove the invariant and refuse.
    let col = SsKey.serialize (aKey 0)
    let json =
        sprintf
            """{"version":1,"entries":[{"entry":"pii","column":"%s","pii":"email"},{"entry":"pii","column":"%s","pii":"phone"}]}"""
            col col
    match CorrectionCodec.deserialize json with
    | Ok _ -> Assert.Fail("expected a synthetic.correction.conflict refusal on decode")
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "synthetic.correction.conflict")

[<Fact>]
let ``decode REFUSES an unknown entry tag (no silent drop)`` () =
    let col = SsKey.serialize (aKey 0)
    let json = sprintf """{"version":1,"entries":[{"entry":"coverage","column":"%s"}]}""" col
    match CorrectionCodec.deserialize json with
    | Ok _ -> Assert.Fail("expected an unknown-entry refusal")
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "correctionCodec.entry.unknown")

// ---------------------------------------------------------------------------
// The universal law over a constructed-valid generator.
// ---------------------------------------------------------------------------

let rec private seqGen (gs: Gen<'a> list) : Gen<'a list> =
    match gs with
    | [] -> Gen.constant []
    | g :: rest -> gen { let! x = g
                         let! xs = seqGen rest
                         return x :: xs }

let private genEntry (i: int) : Gen<CorrectionEntry> =
    let col = aKey i
    Gen.oneof
        [ gen { let! k =
                    Gen.elements
                        [ PiiKind.None; PiiKind.Email; PiiKind.PersonName; PiiKind.Phone
                          PiiKind.Address; PiiKind.FreeText; PiiKind.Reference ]
                return CorrectionEntry.Pii (col, k) }
          gen { let! m = Gen.elements [ ValueFidelityMode.Preserve; ValueFidelityMode.Synthesize ]
                return CorrectionEntry.Fidelity (col, m) } ]

// Distinct column index per entry → conflict-free by construction, so
// `Correction.create` always succeeds (the law is over VALID corrections).
let private genCorrection : Gen<Correction> =
    gen {
        let! n = Gen.choose (0, 8)
        let! entries = seqGen [ for i in 0 .. n - 1 -> genEntry i ]
        return corr entries
    }

type CorrectionArb =
    static member Correction() : Arbitrary<Correction> = Arb.fromGen genCorrection

[<Property(Arbitrary = [| typeof<CorrectionArb> |])>]
let ``law: deserialize (serialize c) = Ok c`` (c: Correction) : bool =
    roundTrips c
