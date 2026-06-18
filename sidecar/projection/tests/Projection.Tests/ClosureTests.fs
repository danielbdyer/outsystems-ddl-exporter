module Projection.Tests.ClosureTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// Slice 1a — the pure use-case-scoped referential-closure engine
// (`Closure.fs`). The walk is exercised here against a `Map`-backed FAKE
// ORACLE — exactly the seam the live `ClosureOracle` (scoped SELECT … WHERE
// <pk> IN (…)) fills in Slice 1b. The oracle filters the in-memory source
// "database" by `RowKeyFetch.KeyColumn`/`Keys`, the same predicate the wire
// renders, so this test pins the algorithm independent of any database.
//
// Fixture: a three-level FK chain (the canonical OutSystems shape — every
// entity an auto-number `Id` PK, parents referenced by an FK column value):
//
//     Country  (PK ID)
//       ▲ COUNTRY_ID
//     User     (PK ID, FK COUNTRY_ID → Country)
//       ▲ USER_ID
//     Order    (PK ID, FK USER_ID → User)
//
// so the UP closure from an Order must pull its User AND that User's Country.

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es -> invalidOp (sprintf "fixture: %s" (es |> List.map (fun e -> e.Code) |> String.concat ", "))

let private mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_TEST_CLOSURE" parts |> mustOk

let private countryKey = mkKey ["Country"]
let private userKey    = mkKey ["User"]
let private orderKey   = mkKey ["Order"]

// -- Country --------------------------------------------------------------
let private countryKind : Kind =
    { SsKey = countryKey
      Name = mkName "Country"
      Origin = Native
      Modality = []
      Physical = mkTableId "dbo" "OSUSR_AAA_COUNTRY"   // note: source physical name (eSpace-hashed)
      Attributes =
        [ { Attribute.create (mkKey ["Country"; "ID"]) (mkName "ID") Integer with
              Column = ColumnRealization.create "ID" false |> Result.value
              IsPrimaryKey = true; IsMandatory = true } ]
      References = []
      Indexes = []
      Description = None
      IsActive = true
      Triggers = []
      ColumnChecks = []
      ExtendedProperties = [] }

// -- User (FK COUNTRY_ID → Country) ---------------------------------------
let private userKind : Kind =
    { SsKey = userKey
      Name = mkName "User"
      Origin = Native
      Modality = []
      Physical = mkTableId "dbo" "OSUSR_AAA_USER"
      Attributes =
        [ { Attribute.create (mkKey ["User"; "ID"]) (mkName "ID") Integer with
              Column = ColumnRealization.create "ID" false |> Result.value
              IsPrimaryKey = true; IsMandatory = true }
          { Attribute.create (mkKey ["User"; "COUNTRY_ID"]) (mkName "COUNTRY_ID") Integer with
              Column = ColumnRealization.create "COUNTRY_ID" false |> Result.value
              IsMandatory = true } ]
      References =
        [ { Reference.create (mkKey ["User"; "CountryRef"]) (mkName "CountryRef") (mkKey ["User"; "COUNTRY_ID"]) countryKey with
              ConstraintState = ConstraintState.TrustedConstraint } ]
      Indexes = []
      Description = None
      IsActive = true
      Triggers = []
      ColumnChecks = []
      ExtendedProperties = [] }

// -- Order (FK USER_ID → User) --------------------------------------------
let private orderKind : Kind =
    { SsKey = orderKey
      Name = mkName "Order"
      Origin = Native
      Modality = []
      Physical = mkTableId "dbo" "OSUSR_AAA_ORDER"
      Attributes =
        [ { Attribute.create (mkKey ["Order"; "ID"]) (mkName "ID") Integer with
              Column = ColumnRealization.create "ID" false |> Result.value
              IsPrimaryKey = true; IsMandatory = true }
          { Attribute.create (mkKey ["Order"; "USER_ID"]) (mkName "USER_ID") Integer with
              Column = ColumnRealization.create "USER_ID" false |> Result.value
              IsMandatory = true } ]
      References =
        [ { Reference.create (mkKey ["Order"; "UserRef"]) (mkName "UserRef") (mkKey ["Order"; "USER_ID"]) userKey with
              ConstraintState = ConstraintState.TrustedConstraint } ]
      Indexes = []
      Description = None
      IsActive = true
      Triggers = []
      ColumnChecks = []
      ExtendedProperties = [] }

let private catalog : Catalog =
    { Modules =
        [ { SsKey = mkKey ["Module"]; Name = mkName "M"
            Kinds = [ countryKind; userKind; orderKind ]; IsActive = true; ExtendedProperties = [] } ]
      Sequences = [] }

// -- rows ------------------------------------------------------------------
let private countryRow (id: string) : StaticRow =
    { Identifier = mkKey ["Country"; "Row"; id]
      Values = Map.ofList [ mkName "ID", id ] }

let private userRow (id: string) (countryId: string) : StaticRow =
    { Identifier = mkKey ["User"; "Row"; id]
      Values = Map.ofList [ mkName "ID", id; mkName "COUNTRY_ID", countryId ] }

let private orderRow (id: string) (userId: string) : StaticRow =
    { Identifier = mkKey ["Order"; "Row"; id]
      Values = Map.ofList [ mkName "ID", id; mkName "USER_ID", userId ] }

/// The full source "database" — what the fake oracle reads.
let private sourceDb : Map<SsKey, StaticRow list> =
    Map.ofList
        [ countryKey, [ countryRow "10"; countryRow "20" ]
          userKey,    [ userRow "100" "10"; userRow "200" "20"; userRow "300" "10" ]
          orderKey,   [ orderRow "1000" "100"; orderRow "1001" "100"; orderRow "1002" "300" ] ]

/// The fake oracle: read `db`'s rows for the requested kind, keep those whose
/// `KeyColumn` value is in the requested `Keys` — i.e. `WHERE KeyColumn IN
/// Keys`, the same predicate `ClosureOracle` renders to SQL.
let private oracleOver (db: Map<SsKey, StaticRow list>) (f: Closure.RowKeyFetch) : Closure.FetchedRows =
    let rows =
        Map.tryFind f.Kind db
        |> Option.defaultValue []
        |> List.filter (fun r -> Set.contains (StaticRow.valueOrEmpty f.KeyColumn r) f.Keys)
    { Kind = f.Kind; Rows = rows }

/// Drive the closure to its fixed point from a set of already-fetched root
/// rows. Returns the closed state. Guards against a runaway loop (the fixed
/// point is the contract under test).
let private runWalk (db: Map<SsKey, StaticRow list>) (roots: Closure.FetchedRows list) : Closure.ClosureState =
    let oracle = oracleOver db
    let rec loop fuel state fetched =
        if fuel <= 0 then failwith "closure did not reach a fixed point (fuel exhausted)"
        let state', fetches = Closure.step catalog state fetched
        if List.isEmpty fetches then state'
        else loop (fuel - 1) state' (fetches |> List.map oracle)
    loop 100 Closure.empty roots

let private rootOrders (ids: string list) : Closure.FetchedRows list =
    [ { Kind = orderKey
        Rows = ids |> List.map (fun id -> sourceDb |> Map.find orderKey |> List.find (fun r -> StaticRow.valueOrEmpty (mkName "ID") r = id)) } ]

let private closedKeys (state: Closure.ClosureState) (kind: SsKey) : Set<string> =
    Closure.materialize state
    |> Map.tryFind kind
    |> Option.defaultValue []
    |> List.map (fun r -> StaticRow.valueOrEmpty (mkName "ID") r)
    |> Set.ofList

// -- the engine's contract ------------------------------------------------

[<Fact>]
let ``Closure follows populated parent FKs transitively to the referential fixed point`` () =
    let state = runWalk sourceDb (rootOrders [ "1000" ])
    Assert.Equal<Set<string>>(Set.ofList [ "1000" ], closedKeys state orderKey)
    Assert.Equal<Set<string>>(Set.ofList [ "100" ],  closedKeys state userKey)
    Assert.Equal<Set<string>>(Set.ofList [ "10" ],   closedKeys state countryKey)

[<Fact>]
let ``Closure de-duplicates a parent reached by multiple paths`` () =
    // Orders 1000 and 1001 both reference user 100 → user 100 (and country 10)
    // appear exactly once.
    let state = runWalk sourceDb (rootOrders [ "1000"; "1001" ])
    Assert.Equal<Set<string>>(Set.ofList [ "1000"; "1001" ], closedKeys state orderKey)
    Assert.Equal<Set<string>>(Set.ofList [ "100" ], closedKeys state userKey)
    Assert.Equal<Set<string>>(Set.ofList [ "10" ],  closedKeys state countryKey)
    Assert.Equal(4, Closure.rowCount state)

[<Fact>]
let ``Closure over all roots is referentially closed — DataLoadPlan.build is clean`` () =
    let state = runWalk sourceDb (rootOrders [ "1000"; "1001"; "1002" ])
    Assert.Equal<Set<string>>(Set.ofList [ "100"; "300" ], closedKeys state userKey)
    Assert.Equal<Set<string>>(Set.ofList [ "10" ], closedKeys state countryKey)
    let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
    let plan = DataLoadPlan.build catalog topo (Closure.materialize state) SurrogateRemapContext.empty
    // A referentially-closed set produces a plan with no unbreakable cycles
    // and — every FK target present in the set — no structural surprises.
    Assert.Empty(plan.UnbreakableCycleFks)

[<Fact>]
let ``Closure terminates when a required parent is missing from the source`` () =
    // Remove every Country: user 100 still requests country 10, the oracle
    // returns nothing, and the walk must STILL terminate (the Requested guard
    // never re-requests a key that returned no row). Slice 2 will NAME the
    // resulting dangling parent; here we only pin termination + shape.
    let dbNoCountry = sourceDb |> Map.add countryKey []
    let state = runWalk dbNoCountry (rootOrders [ "1000" ])
    Assert.Equal<Set<string>>(Set.ofList [ "1000" ], closedKeys state orderKey)
    Assert.Equal<Set<string>>(Set.ofList [ "100" ],  closedKeys state userKey)
    Assert.Empty(closedKeys state countryKey)
