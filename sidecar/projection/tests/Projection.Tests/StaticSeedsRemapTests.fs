module Projection.Tests.StaticSeedsRemapTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.Data

// Static-artifact consumer of the generic `SurrogateRemapContext`. The
// full-export emission path can now re-point FK values in static rows
// through an operator-supplied remap, with the same evidentiary shape
// every other consumer reads (Transfer realization, future MERGE re-
// pointing). Empty remap → no-op (skeleton-purity preserved). Rows
// whose targeted FK has no matched assigned identity are dropped.

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mustOkEmit (r: Result<'a, EmitError>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> Assert.Fail (sprintf "expected Ok, got %A" e); Unchecked.defaultof<_>

let private mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_TEST_SR" parts |> mustOk
let private mkName (s: string) : Name = Name.create s |> mustOk

let private userKey  = mkKey ["User"]
let private orderKey = mkKey ["Order"]

let private userKind : Kind =
    {
        SsKey      = userKey
        Name       = mkName "User"
        Origin     = OsNative
        Modality   = []
        Physical   = { Schema = "dbo"; Table = "OSUSR_SR_USER"; Catalog = None }
        Attributes =
            [ { Attribute.create (mkKey ["User"; "ID"]) (mkName "ID") Integer with
                  Column = { ColumnName = "ID"; IsNullable = false }; IsPrimaryKey = true; IsMandatory = true } ]
        References = []
        Indexes    = []
        Description = None
        IsActive   = true
        Triggers   = []
        ColumnChecks = []
        ExtendedProperties = []
    }

let private orderIdAttr =
    { Attribute.create (mkKey ["Order"; "ID"]) (mkName "ID") Integer with
        Column = { ColumnName = "ID"; IsNullable = false }; IsPrimaryKey = true; IsMandatory = true }
let private orderUserIdAttr =
    { Attribute.create (mkKey ["Order"; "USER_ID"]) (mkName "USER_ID") Integer with
        Column = { ColumnName = "USER_ID"; IsNullable = false }; IsMandatory = true }

let private orderUserRef =
    { Reference.create (mkKey ["Order"; "UserRef"]) (mkName "UserRef") (mkKey ["Order"; "USER_ID"]) userKey with
        HasDbConstraint = true }

let private mkOrderRow (rowKey: string) (id: string) (userId: string) : StaticRow =
    { Identifier = mkKey ["Order"; "Row"; rowKey]
      Values     = Map.ofList [ mkName "ID", id; mkName "USER_ID", userId ] }

let private mkOrderKind (rows: StaticRow list) : Kind =
    {
        SsKey      = orderKey
        Name       = mkName "Order"
        Origin     = OsNative
        Modality   = [ Static rows ]
        Physical   = { Schema = "dbo"; Table = "OSUSR_SR_ORDER"; Catalog = None }
        Attributes = [ orderIdAttr; orderUserIdAttr ]
        References = [ orderUserRef ]
        Indexes    = []
        Description = None
        IsActive   = true
        Triggers   = []
        ColumnChecks = []
        ExtendedProperties = []
    }

let private mkCatalog (kinds: Kind list) : Catalog =
    let m : Module =
        { SsKey = mkKey ["Module"]; Name = mkName "M"; Kinds = kinds; IsActive = true; ExtendedProperties = [] }
    { Modules = [ m ]; Sequences = [] }

let private topoFor (catalog: Catalog) : TopologicalOrder =
    (TopologicalOrderPass.runWith TreatAsCycle catalog).Value

let private orderScript (artifact: ArtifactByKind<DataInsertScript>) : DataInsertScript =
    artifact
    |> ArtifactByKind.tryFind orderKey
    |> Option.defaultWith (fun () -> failwith "Order kind missing from emit artifact")

let private singleRowValuesOf (script: DataInsertScript) : Map<Name, SqlLiteral> =
    match script.Phase1Merges with
    | [ row ] -> row.Values
    | rows    -> failwithf "expected exactly 1 Phase1 row, got %d" rows.Length

let private userIdLiteralOf (row: Map<Name, SqlLiteral>) : string =
    Map.tryFind (mkName "USER_ID") row
    |> Option.map SqlLiteral.toString
    |> Option.defaultValue "<missing>"

// -- the consumer's contract ---------------------------------------------

[<Fact>]
let ``emitWithTopoAndRemap re-points a targeted FK value in a static row`` () =
    let catalog = mkCatalog [ userKind; mkOrderKind [ mkOrderRow "r1" "1" "280" ] ]
    let remap =
        SurrogateRemapContext.empty
        |> SurrogateRemapContext.capture userKey (SourceKey.ofString "280") (AssignedKey.ofString "18")
        |> mustOk
    let artifact =
        StaticSeedsEmitter.emitWithTopoAndRemap (topoFor catalog) catalog Profile.empty remap
        |> mustOkEmit
    let values = orderScript artifact |> singleRowValuesOf
    Assert.Equal("18", userIdLiteralOf values)

[<Fact>]
let ``emitWithTopoAndRemap with the empty remap is the identity (skeleton-purity)`` () =
    let catalog = mkCatalog [ userKind; mkOrderKind [ mkOrderRow "r1" "1" "280" ] ]
    let artifact =
        StaticSeedsEmitter.emitWithTopoAndRemap
            (topoFor catalog) catalog Profile.empty SurrogateRemapContext.empty
        |> mustOkEmit
    let values = orderScript artifact |> singleRowValuesOf
    Assert.Equal("280", userIdLiteralOf values)

[<Fact>]
let ``emitWithTopo (no remap) matches emitWithTopoAndRemap with the empty remap`` () =
    let catalog = mkCatalog [ userKind; mkOrderKind [ mkOrderRow "r1" "1" "280" ] ]
    let topo = topoFor catalog
    let viaShim =
        StaticSeedsEmitter.emitWithTopo topo catalog Profile.empty |> mustOkEmit
    let viaExplicit =
        StaticSeedsEmitter.emitWithTopoAndRemap topo catalog Profile.empty SurrogateRemapContext.empty
        |> mustOkEmit
    Assert.Equal((orderScript viaShim).Rendered, (orderScript viaExplicit).Rendered)

[<Fact>]
let ``emitWithTopoAndRemap drops a row whose targeted FK has no matched assigned identity`` () =
    // Remap targets User but only carries 999 → 18; the row references 280 → no match → drop.
    let catalog = mkCatalog [ userKind; mkOrderKind [ mkOrderRow "r1" "1" "280" ] ]
    let remap =
        SurrogateRemapContext.empty
        |> SurrogateRemapContext.capture userKey (SourceKey.ofString "999") (AssignedKey.ofString "18")
        |> mustOk
    let artifact =
        StaticSeedsEmitter.emitWithTopoAndRemap (topoFor catalog) catalog Profile.empty remap
        |> mustOkEmit
    let script = orderScript artifact
    Assert.Empty script.Phase1Merges
    Assert.Equal("", script.RenderedPhase1)

[<Fact>]
let ``emitWithTopoAndRemap leaves rows of non-targeted kinds untouched`` () =
    // Remap targets User, but the row's FK target IS User and resolves — independently,
    // a kind whose FKs don't target anything in the remap set must pass through unchanged.
    // This test exercises the no-FK-target-overlap branch via a second-order kind.
    let catalog = mkCatalog [ userKind; mkOrderKind [ mkOrderRow "r1" "1" "280" ] ]
    let remap =
        SurrogateRemapContext.empty
        |> SurrogateRemapContext.capture (mkKey ["UnrelatedKind"]) (SourceKey.ofString "0") (AssignedKey.ofString "0")
        |> mustOk
    let artifact =
        StaticSeedsEmitter.emitWithTopoAndRemap (topoFor catalog) catalog Profile.empty remap
        |> mustOkEmit
    let values = orderScript artifact |> singleRowValuesOf
    Assert.Equal("280", userIdLiteralOf values)
