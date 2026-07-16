module Projection.Tests.RowDigestFoldTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The rebuilt aggregate fold (`RowDigestFold` — the 2026-06-12-deleted fold at
// its named trigger; DECISIONS 2026-07-15 "The fidelity chapter opens"; T17's
// ladder rung L1). The laws under test:
//   - COMMUTATIVITY: every permutation of a row set folds to one digest — the
//     aggregate rung needs no cross-side ordering agreement.
//   - IDENTITY: `empty` finalizes to the zero digest over zero rows.
//   - COUNT: the digest carries the row count (sums that alias at different
//     sizes stay distinguishable).
//   - Q-TRACK AGREEMENT: folding quanta against a basis equals folding the
//     equivalent total rows (the Q1 invariant, lifted to the aggregate).
//   - CROSS-RENDITION: a physical-rendition stream folded under a renamed
//     (logical) basis digests identically to the logical rendition's own
//     stream — T17's rows-axis triangle, byte-identity across the
//     physical→logical gap.
// ---------------------------------------------------------------------------

let private cellRow (identifier: int) (values: (string * string) list) : StaticRow =
    { Identifier = SsKey.synthesizedComposite "RDF_ROW" [ string identifier ] |> Result.value
      Values = values |> List.map (fun (n, v) -> mkName n, v) |> Map.ofList }

let private foldRows (rows: StaticRow list) : RowDigestFold.TableDigest =
    rows
    |> List.fold RowDigestFold.addRow RowDigestFold.empty
    |> RowDigestFold.finalize

// -- identity + count ---------------------------------------------------------

[<Fact>]
let ``law: empty finalizes to the zero digest over zero rows`` () =
    let digest = RowDigestFold.finalize RowDigestFold.empty
    Assert.Equal(0L, digest.Count)
    Assert.Equal(String.replicate 64 "0", digest.Aggregate)

[<Fact>]
let ``law: the digest carries the row count`` () =
    let rows = [ for i in 1 .. 5 -> cellRow i [ "Id", string i; "Name", "row" ] ]
    Assert.Equal(5L, (foldRows rows).Count)

// -- commutativity ------------------------------------------------------------

[<Property>]
let ``law: the aggregate fold is commutative — a reversed row list folds to the same digest (ladder L1 needs no ordering agreement)`` (seeds: int list) =
    let rows =
        seeds
        |> List.mapi (fun i seed -> cellRow i [ "Id", string i; "Payload", string seed ])
    foldRows rows = foldRows (List.rev rows)

[<Fact>]
let ``law: every permutation of three distinct rows folds to one digest`` () =
    let a = cellRow 1 [ "Id", "1"; "Name", "alpha" ]
    let b = cellRow 2 [ "Id", "2"; "Name", "bravo" ]
    let c = cellRow 3 [ "Id", "3"; "Name", "charlie" ]
    let expected = foldRows [ a; b; c ]
    for permutation in [ [ a; c; b ]; [ b; a; c ]; [ b; c; a ]; [ c; a; b ]; [ c; b; a ] ] do
        Assert.Equal(expected, foldRows permutation)

[<Fact>]
let ``law: differing row content folds to differing digests at equal counts`` () =
    let left  = foldRows [ cellRow 1 [ "Id", "1"; "Name", "alpha" ] ]
    let right = foldRows [ cellRow 1 [ "Id", "1"; "Name", "bravo" ] ]
    Assert.NotEqual(left, right)

// -- the quantum path agrees with the row path at the aggregate ---------------

[<Fact>]
let ``Q1 lifted: folding quanta against the basis equals folding the equivalent total rows`` () =
    let names = [ "Id"; "Email"; "Name" ]
    let basis = RowBasis.ofNames (names |> List.map mkName)
    let cells =
        [ [| "1"; "a@x.example"; "alpha" |]
          [| "2"; "b@x.example"; "bravo" |] ]
    let viaQuanta =
        cells
        |> List.map (fun c -> ({ Cells = c } : RowQuantum))
        |> List.fold (RowDigestFold.addQuantum basis) RowDigestFold.empty
        |> RowDigestFold.finalize
    let viaRows =
        cells
        |> List.mapi (fun i c -> cellRow i (List.zip names (List.ofArray c)))
        |> foldRows
    Assert.Equal(viaRows.Aggregate, viaQuanta.Aggregate)
    Assert.Equal(viaRows.Count, viaQuanta.Count)

// -- cross-rendition agreement (T17's rows-axis triangle) ----------------------

[<Fact>]
let ``T17 triangle: a physical stream folded under the renamed basis digests identically to the logical stream`` () =
    // The physical rendition's column names (the OSUSR shape) and the logical
    // names one authored model declares for the same positions. The rename
    // map re-bases the physical stream; the logical stream hashes natively.
    // The names are chosen so the NAME-SORTED ORDER differs across the gap
    // (physical: ACOL < ZCOL; logical: Zulu > Alpha ⇒ same; flip one) — the
    // basis's recomputed permutation is part of what the law proves.
    let physicalNames = [ "OSUSR_X_ZFIRST"; "OSUSR_X_ASECOND" ]
    let logicalNames  = [ "Alpha";          "Zulu" ]
    let renameMap =
        List.zip physicalNames logicalNames
        |> List.map (fun (p, l) -> mkName p, mkName l)
        |> Map.ofList
    let physicalBasis = RowBasis.ofNames (physicalNames |> List.map mkName)
    let logicalBasis  = RowBasis.ofNames (logicalNames  |> List.map mkName)
    let rebasedPhysical = RowBasis.rename renameMap physicalBasis
    let cells =
        [ [| "one";   "1" |]
          [| "two";   "2" |]
          [| "three"; "3" |] ]
    let foldUnder (basis: RowBasis) : RowDigestFold.TableDigest =
        cells
        |> List.map (fun c -> ({ Cells = c } : RowQuantum))
        |> List.fold (RowDigestFold.addQuantum basis) RowDigestFold.empty
        |> RowDigestFold.finalize
    Assert.Equal((foldUnder logicalBasis).Aggregate, (foldUnder rebasedPhysical).Aggregate)
    // And the un-renamed physical basis digests DIFFERENTLY — the names are
    // part of the canonical bytes, which is exactly why the re-basis exists.
    Assert.NotEqual<string>((foldUnder logicalBasis).Aggregate, (foldUnder physicalBasis).Aggregate)
