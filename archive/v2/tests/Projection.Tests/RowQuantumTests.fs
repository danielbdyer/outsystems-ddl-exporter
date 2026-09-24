module Projection.Tests.RowQuantumTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Pipeline

// CONSTELLATION_BACKLOG card Q1 (the row-quantum foundation, under the
// open H3 gate). `RowBasis` carries a per-stream column basis + a
// precomputed name-sorted ordinal permutation; `RowQuantum` is the
// positional carrier. The load-bearing invariant the whole Q-track rests
// on: hashing a quantum through the permutation is BYTE-IDENTICAL to
// hashing the equivalent (total) `StaticRow` through the Map-sorting
// recipe — so the canary's row hashes do not move when the read carrier
// becomes positional (Q2-Q4). If this byte-identity ever fails, the
// Q-track stops here.

let private mustOk r = match r with Ok v -> v | Error e -> failwithf "fixture: %A" e
let private nm (s: string) : Name = Name.create s |> mustOk
let private anyKey () : SsKey = SsKey.synthesized "OS_TEST_RQ" "row" |> mustOk

/// Build a StaticRow whose Values are total over `cols` (attribute order
/// preserved by the caller), and the matching attribute-order name list.
/// WP-3: every authored cell is PRESENT — including `Some ""`, which the
/// voption quantum now represents faithfully (`ValueSome ""` ≠ `ValueNone`),
/// so the round-trip laws quantify over the full value space.
let private rowOf (cols: (string * string) list) : Name list * StaticRow =
    let names = cols |> List.map (fst >> nm)
    let row =
        { Identifier = anyKey ()
          Values = StaticRow.presentValues (cols |> List.map (fun (c, v) -> nm c, v)) }
    names, row

let private assertByteIdentity (cols: (string * string) list) =
    let names, row = rowOf cols
    let basis = RowBasis.ofNames names
    let quantum = RowQuantum.ofStaticRow basis row
    // The invariant: byte-identical hashes …
    Assert.Equal<byte[]>(RowDigester.hashRowBytes row, RowDigester.hashQuantumBytes basis quantum)
    // … and a lossless round-trip back to the IR-grain value Map.
    Assert.Equal<Map<Name, string option>>(row.Values, RowQuantum.toValues basis quantum)

// -- the permutation must actually do work --------------------------------

[<Fact>]
let ``Q1: hashQuantumBytes equals hashRowBytes — single column`` () =
    assertByteIdentity [ "Id", "42" ]

[<Fact>]
let ``Q1: hashQuantumBytes equals hashRowBytes — attribute order already name-sorted`` () =
    assertByteIdentity [ "Apple", "1"; "Mango", "2"; "Zebra", "3" ]

[<Fact>]
let ``Q1: hashQuantumBytes equals hashRowBytes — attribute order reverse of name-sorted`` () =
    // The permutation is non-trivial here: attribute order [Zebra; Mango;
    // Apple] must hash in name-sorted order [Apple; Mango; Zebra].
    assertByteIdentity [ "Zebra", "3"; "Mango", "2"; "Apple", "1" ]

[<Fact>]
let ``Q1: hashQuantumBytes equals hashRowBytes — values bearing '=' and the RS separator`` () =
    // The RS (\x1e) separator's disambiguation duty must survive the
    // positional rewrite: a value containing '=' or RS cannot be allowed
    // to alias a different column layout.
    assertByteIdentity [ "B", "x=y"; "A", "pq"; "C", "" ]

[<Fact>]
let ``Q1: hashQuantumBytes equals hashRowBytes — twelve columns, interleaved order`` () =
    assertByteIdentity
        [ "C07", "g"; "C01", "a"; "C11", "k"; "C04", "d"; "C09", "i"; "C02", "b"
          "C12", "l"; "C06", "f"; "C03", "c"; "C10", "j"; "C05", "e"; "C08", "h" ]

// -- the permutation is a valid permutation of the basis ------------------

[<Fact>]
let ``Q1: nameSortedOrder is a permutation of the column indices`` () =
    let names, _ = rowOf [ "Zebra", ""; "Mango", ""; "Apple", ""; "Delta", "" ]
    let basis = RowBasis.ofNames names
    let order = RowBasis.nameSortedOrder basis
    Assert.Equal<int[]>([| 0; 1; 2; 3 |], Array.sort order)
    // Walking the order visits names in ascending string order.
    let visited = order |> Array.map (fun i -> Name.value (RowBasis.names basis).[i])
    Assert.Equal<string[]>(Array.sort visited, visited)

// -- Q2: the IR-grain boundary round-trip ----------------------------------

[<Fact>]
let ``R4: ofQuantum ∘ toQuantum = id — the IR-grain boundary loses nothing over a total row`` () =
    // The Q2 boundary law: a total StaticRow projected onto the basis
    // (toQuantum = RowQuantum.ofStaticRow) and rebuilt at the IR grain
    // (StaticRow.ofQuantum) is the SAME row — Values and Identifier both.
    let names, row =
        rowOf [ "Zebra", "z"; "Mango", "x=y"; "Apple", ""; "Delta", "d" ]
    let basis = RowBasis.ofNames names
    let rebuilt =
        StaticRow.ofQuantum basis row.Identifier (RowQuantum.ofStaticRow basis row)
    Assert.Equal<StaticRow>(row, rebuilt)

// -- Q3: the carrier-equivalence laws --------------------------------------
// The streaming realization's per-row operations moved from the Map-carried
// `StaticRow` to the positional `RowQuantum`. These laws pin that the move
// changed the carrier and nothing else: same kept rows, same re-pointed
// values, same skip diagnostics, same renamed headers.

let private targetA : SsKey = SsKey.synthesized "OS_TEST_RQ" "TargetA" |> mustOk
let private targetB : SsKey = SsKey.synthesized "OS_TEST_RQ" "TargetB" |> mustOk

[<Property>]
let ``Q3: remapQuantumFksWith equals remapRowFksWith over any total rows`` (seeds: (int * int) list) =
    // Columns: Id (never a target), FkA → TargetA, FkB → TargetB. The
    // lookup resolves even-valued surrogates only, so kept/skipped both
    // exercise; FkB is sometimes NULL ("") to exercise the pass-through.
    let names = [ "Id"; "FkA"; "FkB" ] |> List.map nm
    let basis = RowBasis.ofNames names
    let fkTargets = Map.ofList [ nm "FkA", targetA; nm "FkB", targetB ]
    let tryFind (_target: SsKey) (v: string) : string option =
        if (int v) % 2 = 0 then Some ("assigned-" + v) else None
    let rows =
        seeds
        |> List.mapi (fun i (a, b) ->
            { Identifier = anyKey ()
              Values =
                Map.ofList
                    [ nm "Id", Some (string i)
                      nm "FkA", Some (string (abs a % 10))
                      nm "FkB", (if b % 3 = 0 then None else Some (string (abs b % 10))) ] })
    let viaRows = SurrogateRemap.remapRowFksWith tryFind fkTargets rows
    let viaQuanta =
        SurrogateRemap.remapQuantumFksWith
            tryFind
            (SurrogateRemap.fkOrdinalsTargeting basis fkTargets)
            (rows |> List.map (RowQuantum.ofStaticRow basis))
    (viaQuanta.Rows |> List.map (RowQuantum.toValues basis))
        = (viaRows.Rows |> List.map (fun r -> r.Values))
    && viaQuanta.Skipped = viaRows.Skipped

[<Fact>]
let ``Q3: RowBasis.rename — the header rename equals the per-row rename walk`` () =
    // Under a positional carrier a rename is a basis (header) operation
    // done once per stream; this pins it against the Map-carried
    // `RenameProjection.repointRow` it replaces on the streaming path.
    let names = [ "OldA"; "Keep"; "OldB" ] |> List.map nm
    let map = Map.ofList [ nm "OldA", nm "NewA"; nm "OldB", nm "NewB" ]
    let basis = RowBasis.ofNames names
    let renamed = RowBasis.rename map basis
    let row =
        { Identifier = anyKey ()
          Values = StaticRow.presentValues [ nm "OldA", "a"; nm "Keep", "k"; nm "OldB", "b=x" ] }
    let q = RowQuantum.ofStaticRow basis row
    Assert.Equal<Map<Name, string option>>(
        (RenameProjection.repointRow map row).Values,
        RowQuantum.toValues renamed q)
    // Empty map → the same basis (the no-rename stream is byte-identical).
    Assert.Equal<RowBasis>(basis, RowBasis.rename Map.empty basis)

// -- WP-3 (F11): the NULL-vs-empty hash distinction -------------------------

[<Fact>]
let ``F11: a NULL cell and an empty-string cell hash DISTINCTLY, on both carriers`` () =
    let names = [ nm "Id"; nm "Body" ]
    let basis = RowBasis.ofNames names
    let withEmpty : StaticRow =
        { Identifier = anyKey (); Values = Map.ofList [ nm "Id", Some "1"; nm "Body", Some "" ] }
    let withNull : StaticRow =
        { Identifier = anyKey (); Values = Map.ofList [ nm "Id", Some "1"; nm "Body", None ] }
    Assert.NotEqual<byte[]>(RowDigester.hashRowBytes withEmpty, RowDigester.hashRowBytes withNull)
    Assert.NotEqual<byte[]>(
        RowDigester.hashQuantumBytes basis (RowQuantum.ofStaticRow basis withEmpty),
        RowDigester.hashQuantumBytes basis (RowQuantum.ofStaticRow basis withNull))
    // And the carriers agree with each other on both rows (Q1 holds).
    Assert.Equal<byte[]>(
        RowDigester.hashRowBytes withNull,
        RowDigester.hashQuantumBytes basis (RowQuantum.ofStaticRow basis withNull))

[<Fact>]
let ``F11: a NULL cell omits its attribute — present-None and absent-key rows hash identically`` () =
    // The ServerDigest rule, mirrored: NULL contributes nothing, so an
    // explicit NULL cell and a wholly absent key are the same content.
    let presentNone : StaticRow =
        { Identifier = anyKey (); Values = Map.ofList [ nm "Id", Some "1"; nm "Body", None ] }
    let absent : StaticRow =
        { Identifier = anyKey (); Values = Map.ofList [ nm "Id", Some "1" ] }
    Assert.Equal<byte[]>(RowDigester.hashRowBytes presentNone, RowDigester.hashRowBytes absent)

[<Fact>]
let ``Q3: cellGetter reads by name through the basis; absent names read as no value`` () =
    let names = [ "Zebra"; "Apple" ] |> List.map nm
    let basis = RowBasis.ofNames names
    let q : RowQuantum = { Cells = [| ValueSome "z"; ValueSome "a" |] }
    Assert.Equal(ValueSome "z", RowQuantum.cellGetter basis (nm "Zebra") q)
    Assert.Equal(ValueSome "a", RowQuantum.cellGetter basis (nm "Apple") q)
    Assert.Equal(ValueNone, RowQuantum.cellGetter basis (nm "Missing") q)

// -- the property: byte-identity over any total row, any column order -----

/// Distinct column names (from a fixed valid pool, shuffled) + arbitrary
/// values: the basis's permutation must always reproduce the Map-sorted
/// hash. Names drawn from a pool keeps every generated `Name` valid and
/// distinct (declarative-valid-input discipline) while FsCheck varies the
/// subset, the order, and the values.
let private namePool = [ "Alpha"; "Bravo"; "Charlie"; "Delta"; "Echo"; "Foxtrot"; "Golf"; "Hotel" ]

[<Property>]
let ``Q1: hashQuantumBytes equals hashRowBytes over any total row`` (picks: int list) (values: string list) =
    // Derive a distinct, ordered column set from the (arbitrary) picks.
    let cols =
        picks
        |> List.map (fun i -> namePool.[((i % namePool.Length) + namePool.Length) % namePool.Length])
        |> List.distinct
    if List.isEmpty cols then true   // RowBasis over zero columns is out of scope
    else
        let paddedValues =
            cols |> List.mapi (fun i c -> c, (values |> List.tryItem i |> Option.defaultValue (sprintf "v%d" i)))
        let names, row = rowOf paddedValues
        let basis = RowBasis.ofNames names
        let quantum = RowQuantum.ofStaticRow basis row
        RowDigester.hashRowBytes row = RowDigester.hashQuantumBytes basis quantum
        && RowQuantum.toValues basis quantum = row.Values
