module Projection.Tests.StaticSeedsRemapTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.Data
open Projection.Tests.Fixtures

// The static-artifact emit path realized through the converged data-load
// algebra. `DataLoadPlan.build` applies the operator-supplied
// `SurrogateRemapContext` at plan-construction (the one OperatorIntent
// Insertion site for the entire family); `StaticSeedsEmitter.emitFromPlan`
// consumes the post-substitution plan. Empty remap → identity over rows
// (skeleton-purity); rows whose targeted FK has no matched assigned
// identity are dropped at plan-build and surface in `plan.SkippedReferences`.

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

let private userKey  = mkKey ["User"]
let private orderKey = mkKey ["Order"]

let private userKind : Kind =
    {
        SsKey      = userKey
        Name       = mkName "User"
        Origin     = Native
        Modality   = []
        Physical   = mkTableId "dbo" "OSUSR_SR_USER"
        Attributes =
            [ { Attribute.create (mkKey ["User"; "ID"]) (mkName "ID") Integer with
                  Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true } ]
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
        Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
let private orderUserIdAttr =
    { Attribute.create (mkKey ["Order"; "USER_ID"]) (mkName "USER_ID") Integer with
        Column = ColumnRealization.create ("USER_ID") (false) |> Result.value; IsMandatory = true }

let private orderUserRef =
    { Reference.create (mkKey ["Order"; "UserRef"]) (mkName "UserRef") (mkKey ["Order"; "USER_ID"]) userKey with
        ConstraintState = ConstraintState.TrustedConstraint }

let private mkOrderRow (rowKey: string) (id: string) (userId: string) : StaticRow =
    { Identifier = mkKey ["Order"; "Row"; rowKey]
      Values     = StaticRow.presentValues [ mkName "ID", id; mkName "USER_ID", userId ] }

let private mkOrderKind (rows: StaticRow list) : Kind =
    {
        SsKey      = orderKey
        Name       = mkName "Order"
        Origin     = Native
        Modality   = [ Static rows ]
        Physical   = mkTableId "dbo" "OSUSR_SR_ORDER"
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

let private rawRowsFor (orderRow: StaticRow) : Map<SsKey, StaticRow list> =
    Map.ofList [ orderKey, [ orderRow ] ]

let private planFor (catalog: Catalog) (rows: Map<SsKey, StaticRow list>) (remap: SurrogateRemapContext) : DataLoadPlan =
    DataLoadPlan.build catalog (topoFor catalog) rows remap

// -- the consumer's contract ---------------------------------------------

[<Fact>]
let ``DataLoadPlan.build re-points a targeted FK value at the canonical OperatorIntent Insertion site`` () =
    let catalog = mkCatalog [ userKind; mkOrderKind [] ]
    let remap =
        SurrogateRemapContext.empty
        |> SurrogateRemapContext.capture userKey (SourceKey.ofString "280") (AssignedKey.ofString "18")
        |> mustOk
    let plan = planFor catalog (rawRowsFor (mkOrderRow "r1" "1" "280")) remap
    let artifact = StaticSeedsEmitter.emitFromPlan DataEmitOptions.defaults catalog Profile.empty plan |> mustOkEmit
    let values = orderScript artifact |> singleRowValuesOf
    Assert.Equal("18", userIdLiteralOf values)

[<Fact>]
let ``DataLoadPlan.build with the empty remap is the identity over rows (skeleton-purity)`` () =
    let catalog = mkCatalog [ userKind; mkOrderKind [] ]
    let plan = planFor catalog (rawRowsFor (mkOrderRow "r1" "1" "280")) SurrogateRemapContext.empty
    let artifact = StaticSeedsEmitter.emitFromPlan DataEmitOptions.defaults catalog Profile.empty plan |> mustOkEmit
    let values = orderScript artifact |> singleRowValuesOf
    Assert.Equal("280", userIdLiteralOf values)

[<Fact>]
let ``DataLoadPlan.build drops a row whose targeted FK has no matched assigned identity and surfaces it in SkippedReferences`` () =
    // Remap targets User but only carries 999 → 18; the row references 280 → no match → drop.
    let catalog = mkCatalog [ userKind; mkOrderKind [] ]
    let remap =
        SurrogateRemapContext.empty
        |> SurrogateRemapContext.capture userKey (SourceKey.ofString "999") (AssignedKey.ofString "18")
        |> mustOk
    let plan = planFor catalog (rawRowsFor (mkOrderRow "r1" "1" "280")) remap
    // The plan surfaces the dropped row at the build site.
    Assert.Contains(
        plan.SkippedReferences,
        fun (owner, r: UnresolvedReference) ->
            owner = orderKey
            && r.Target = userKey
            && r.UnresolvedSource = SourceKey.ofString "280")
    // The realization just consumes the plan; no script for the dropped row.
    let artifact = StaticSeedsEmitter.emitFromPlan DataEmitOptions.defaults catalog Profile.empty plan |> mustOkEmit
    let script = orderScript artifact
    Assert.Empty script.Phase1Merges
    Assert.Equal("", script.RenderedPhase1)

[<Fact>]
let ``DataLoadPlan.build leaves rows of non-targeted kinds untouched`` () =
    // Remap targets an unrelated kind; the Order row's USER_ID column targets User,
    // which is NOT in the remap → no substitution applied → value preserved.
    let catalog = mkCatalog [ userKind; mkOrderKind [] ]
    let remap =
        SurrogateRemapContext.empty
        |> SurrogateRemapContext.capture (mkKey ["UnrelatedKind"]) (SourceKey.ofString "0") (AssignedKey.ofString "0")
        |> mustOk
    let plan = planFor catalog (rawRowsFor (mkOrderRow "r1" "1" "280")) remap
    let artifact = StaticSeedsEmitter.emitFromPlan DataEmitOptions.defaults catalog Profile.empty plan |> mustOkEmit
    let values = orderScript artifact |> singleRowValuesOf
    Assert.Equal("280", userIdLiteralOf values)

[<Fact>]
let ``StaticSeedsEmitter.emit (legacy convenience) routes through DataLoadPlan.build with the empty remap`` () =
    // Backward-compatible convenience: extracts rows from Kind.staticPopulations
    // and builds the plan with empty remap. The Order kind's static populations
    // hold the row carrying USER_ID=280; no substitution applied; value preserved.
    let catalog = mkCatalog [ userKind; mkOrderKind [ mkOrderRow "r1" "1" "280" ] ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let values = orderScript artifact |> singleRowValuesOf
    Assert.Equal("280", userIdLiteralOf values)
