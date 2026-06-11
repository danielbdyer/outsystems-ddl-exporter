module Projection.Tests.RowQuantumTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core

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
let private rowOf (cols: (string * string) list) : Name list * StaticRow =
    let names = cols |> List.map (fst >> nm)
    let row =
        { Identifier = anyKey ()
          Values = cols |> List.map (fun (c, v) -> nm c, v) |> Map.ofList }
    names, row

let private assertByteIdentity (cols: (string * string) list) =
    let names, row = rowOf cols
    let basis = RowBasis.ofNames names
    let quantum = RowQuantum.ofStaticRow basis row
    // The invariant: byte-identical hashes …
    Assert.Equal<byte[]>(RowDigester.hashRowBytes row, RowDigester.hashQuantumBytes basis quantum)
    // … and a lossless round-trip back to the IR-grain value Map.
    Assert.Equal<Map<Name, string>>(row.Values, RowQuantum.toValues basis quantum)

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
