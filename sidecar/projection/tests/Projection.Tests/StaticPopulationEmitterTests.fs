[<Xunit.Collection("Docker-SqlServer")>]
module Projection.Tests.StaticPopulationEmitterTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Targets.Data
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Π_StaticPopulation tests — typed-Statement.InsertRow realization of
// `Modality.Static` populations (sibling to `SsdtDdlEmitter.statements`
// per A35; sibling to `StaticSeedsEmitter.emit` per the V1-shape
// MERGE / typed-stream realization split, chapter 4.1.A close arc).
//
// These tests verify the load-bearing properties an empty diff in
// `Deploy.runWideCanary` rests on:
//   - T1 byte-determinism (same catalog → same statement sequence).
//   - Static-population kinds yield `InsertRow` per row in declared
//     attribute order.
//   - IDENTITY-bearing kinds bracket their `InsertRow` block with
//     `SetIdentityInsert true / false` (so the text-render path
//     deploys explicit PK values; the bulk-copy path tolerates either).
//   - Empty-modality + non-static kinds yield no statements.
//   - Topological ordering matches `SsdtDdlEmitter.statements` so the
//     composed schema-then-data stream is FK-deploy-correct (target
//     rows before referencer rows).
// ---------------------------------------------------------------------------

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_TEST" parts |> mustOk

let private mkName (s: string) : Name =
    Name.create s |> mustOk

/// Static-entity fixture: 3 columns, 2 rows. `Id` is IDENTITY-flagged
/// so the emitter SHOULD bracket the InsertRow block with
/// SetIdentityInsert toggles.
let private mkCountryKind () : Kind =
    let kindKey  = mkKey ["TestModule"; "Country"]
    let idKey    = mkKey ["TestModule"; "Country"; "Id"]
    let codeKey  = mkKey ["TestModule"; "Country"; "Code"]
    let labelKey = mkKey ["TestModule"; "Country"; "Label"]
    let row code label =
        { Identifier = mkKey ["TestModule"; "Country"; "Row"; code]
          Values =
              Map.ofList
                  [ mkName "Id",    code
                    mkName "Code",  code
                    mkName "Label", label ] }
    {
        SsKey    = kindKey
        Name     = mkName "Country"
        Origin   = Native
        Modality = [ Static [ row "1" "United States"
                              row "2" "Canada" ] ]
        Physical = mkTableId "dbo" "OSUSR_TEST_COUNTRY"
        Attributes =
            [
                { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true; IsIdentity = true }
                { Attribute.create codeKey (mkName "Code") Text with Column = ColumnRealization.create ("CODE") (false) |> Result.value; IsMandatory = true }
                { Attribute.create labelKey (mkName "Label") Text with Column = ColumnRealization.create ("LABEL") (false) |> Result.value; IsMandatory = true }
            ]
        References = []
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

/// Static-entity fixture WITHOUT IDENTITY (e.g., GUID PK, code PK).
/// Emitter should NOT emit SetIdentityInsert toggles.
let private mkLanguageKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Language"]
    let codeKey = mkKey ["TestModule"; "Language"; "Code"]
    let nameKey = mkKey ["TestModule"; "Language"; "Name"]
    let row code name =
        { Identifier = mkKey ["TestModule"; "Language"; "Row"; code]
          Values =
              Map.ofList
                  [ mkName "Code", code
                    mkName "Name", name ] }
    {
        SsKey    = kindKey
        Name     = mkName "Language"
        Origin   = Native
        Modality = [ Static [ row "EN" "English"; row "FR" "French" ] ]
        Physical = mkTableId "dbo" "OSUSR_TEST_LANGUAGE"
        Attributes =
            [
                { Attribute.create codeKey (mkName "Code") Text with Column = ColumnRealization.create ("CODE") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create nameKey (mkName "Name") Text with Column = ColumnRealization.create ("NAME") (false) |> Result.value; IsMandatory = true }
            ]
        References = []
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

/// Non-static kind. Must contribute zero statements.
let private mkRegularKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Customer"]
    let idKey   = mkKey ["TestModule"; "Customer"; "Id"]
    let nameKey = mkKey ["TestModule"; "Customer"; "Name"]
    {
        SsKey    = kindKey
        Name     = mkName "Customer"
        Origin   = Native
        Modality = []
        Physical = mkTableId "dbo" "OSUSR_TEST_CUSTOMER"
        Attributes =
            [
                { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true; IsIdentity = true }
                { Attribute.create nameKey (mkName "Name") Text with Column = ColumnRealization.create ("NAME") (false) |> Result.value; IsMandatory = true }
            ]
        References = []
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

/// Kind carrying an empty `Static []` modality. Edge case: the
/// modality mark exists but no rows. Must contribute zero
/// statements (no IDENTITY toggle bracket around an empty block).
let private mkEmptyStaticKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Empty"]
    let idKey   = mkKey ["TestModule"; "Empty"; "Id"]
    {
        SsKey    = kindKey
        Name     = mkName "Empty"
        Origin   = Native
        Modality = [ Static [] ]
        Physical = mkTableId "dbo" "OSUSR_TEST_EMPTY"
        Attributes =
            [
                { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true; IsIdentity = true }
            ]
        References = []
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

let private mkCatalog (kinds: Kind list) : Catalog =
    let m : Module =
        { SsKey = mkKey ["TestModule"]
          Name  = mkName "TestModule"
          Kinds = kinds; IsActive = true; ExtendedProperties = [] }
    { Modules = [ m ]; Sequences = [] }

[<Fact>]
let ``T1: StaticPopulationEmitter.statements is byte-deterministic across repeat invocations`` () =
    let catalog = mkCatalog [ mkCountryKind (); mkLanguageKind () ]
    let r1 = StaticPopulationEmitter.statements catalog |> Seq.toList
    let r2 = StaticPopulationEmitter.statements catalog |> Seq.toList
    Assert.Equal<Statement list> (r1, r2)

[<Fact>]
let ``StaticPopulationEmitter.statements yields one InsertRow per static row`` () =
    let catalog = mkCatalog [ mkCountryKind () ]  // 2 rows
    let stmts = StaticPopulationEmitter.statements catalog |> Seq.toList
    let inserts =
        stmts |> List.choose (function InsertRow (t, vs) -> Some (t, vs) | _ -> None)
    Assert.Equal (2, List.length inserts)

[<Fact>]
let ``StaticPopulationEmitter.statements omits non-static kinds entirely`` () =
    let catalog = mkCatalog [ mkRegularKind () ]
    let stmts = StaticPopulationEmitter.statements catalog |> Seq.toList
    Assert.Empty stmts

[<Fact>]
let ``StaticPopulationEmitter.statements omits empty-Static kinds (no IDENTITY toggle bracket around an empty block)`` () =
    let catalog = mkCatalog [ mkEmptyStaticKind () ]
    let stmts = StaticPopulationEmitter.statements catalog |> Seq.toList
    Assert.Empty stmts

[<Fact>]
let ``StaticPopulationEmitter.statements brackets InsertRow block with SetIdentityInsert toggles when kind has IsIdentity`` () =
    let catalog = mkCatalog [ mkCountryKind () ]  // Id has IsIdentity=true
    let stmts = StaticPopulationEmitter.statements catalog |> Seq.toList
    // Expected shape: [SetIdentityInsert ON; InsertRow; InsertRow; SetIdentityInsert OFF]
    Assert.Equal (4, List.length stmts)
    match stmts with
    | [ SetIdentityInsert (_, true)
        InsertRow _
        InsertRow _
        SetIdentityInsert (_, false) ] -> ()
    | other ->
        Assert.Fail (sprintf "expected ON, Insert, Insert, OFF; got %A" other)

[<Fact>]
let ``StaticPopulationEmitter.statements omits SetIdentityInsert when kind has no IsIdentity attribute`` () =
    let catalog = mkCatalog [ mkLanguageKind () ]  // PK is Code (Text), no IsIdentity
    let stmts = StaticPopulationEmitter.statements catalog |> Seq.toList
    let toggles =
        stmts |> List.choose (function SetIdentityInsert _ as s -> Some s | _ -> None)
    Assert.Empty toggles

[<Fact>]
let ``StaticPopulationEmitter.statements emits cell values in declared attribute order`` () =
    let catalog = mkCatalog [ mkCountryKind () ]
    let stmts = StaticPopulationEmitter.statements catalog |> Seq.toList
    let firstRow =
        stmts |> List.tryPick (function InsertRow (_, vs) -> Some vs | _ -> None)
    match firstRow with
    | None -> Assert.Fail "expected at least one InsertRow"
    | Some cells ->
        let cols = cells |> List.map (fun c -> c.Column)
        Assert.Equal<string list> ([ "ID"; "CODE"; "LABEL" ], cols)

[<Fact>]
let ``StaticPopulationEmitter.statements TableId matches kind.Physical`` () =
    let catalog = mkCatalog [ mkCountryKind () ]
    let stmts = StaticPopulationEmitter.statements catalog |> Seq.toList
    let firstTable =
        stmts |> List.tryPick (function InsertRow (t, _) -> Some t | _ -> None)
    match firstTable with
    | None   -> Assert.Fail "expected at least one InsertRow"
    | Some t ->
        Assert.Equal ("dbo", TableId.schemaText t)
        Assert.Equal ("OSUSR_TEST_COUNTRY", TableId.tableText t)

/// Composer property: `SsdtDdlEmitter.statements` and
/// `StaticPopulationEmitter.statements` share `TopologicalOrderPass
/// .runWith SkipSelfEdges` ordering, so the composed schema-then-data
/// stream is FK-deploy-correct: target tables created before
/// referencers, target rows inserted before referencer rows.
[<Fact>]
let ``StaticPopulationEmitter.statements topological-table order matches SsdtDdlEmitter.statements`` () =
    // Two static kinds; emit both DDL and DML; assert the ordering of
    // tables in the DML stream is a subsequence of the DDL stream's
    // table ordering.
    let catalog = mkCatalog [ mkCountryKind (); mkLanguageKind () ]
    let ddlTables =
        SsdtDdlEmitter.statements catalog
        |> Seq.choose (function CreateTable (t, _, _, _, _, _) -> Some t | _ -> None)
        |> Seq.toList
    let dmlTables =
        StaticPopulationEmitter.statements catalog
        |> Seq.choose (function
            | InsertRow (t, _)         -> Some t
            | SetIdentityInsert (t, _) -> Some t
            | _                        -> None)
        |> Seq.distinct
        |> Seq.toList
    // Every DML table should appear in the DDL table list, in the
    // same relative order.
    let isSubsequence (sub: TableId list) (full: TableId list) : bool =
        let rec walk (sub: TableId list) (full: TableId list) =
            match sub, full with
            | [], _                       -> true
            | _,  []                      -> false
            | s :: subRest, f :: fullRest ->
                if s = f then walk subRest fullRest
                else walk sub fullRest
        walk sub full
    Assert.True(
        isSubsequence dmlTables ddlTables,
        sprintf
            "DML table order %A is not a subsequence of DDL table order %A"
            dmlTables
            ddlTables)
