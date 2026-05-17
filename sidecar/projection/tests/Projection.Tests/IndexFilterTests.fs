module Projection.Tests.IndexFilterTests

open System.Text.Json
open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Adapters.Osm
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Chapter 4.5 slice α — Index.Filter IR carriage + adapter pickup + emission.
//
// V1 reference: Osm.Domain.Model.IndexOnDiskMetadata.FilterDefinition;
// Osm.Smo/PerTableEmission/IndexScriptBuilder.cs:131-145 (V1 emit path).
// V2 lifts to Index.Filter : string option; ScriptDomBuild.buildCreateIndex
// parses via TSql160Parser at emit time per chapter 4.5 open Q1.
// ---------------------------------------------------------------------------

let private mkKey (v: string) : SsKey =
    SsKey.synthesized "test" v |> Result.value

let private mkName (v: string) : Name = Name.create v |> Result.value

let private mkTableId (schema: string) (table: string) : TableId =
    TableId.create schema table |> Result.value

let private mkAttrK : SsKey = mkKey "Attr.Id"

let private mkIndex (label: string) (filter: string option) : Index =
    {
        SsKey              = mkKey (sprintf "Idx.%s" label)
        Name               = mkName (sprintf "IX_%s" label)
        Columns            = [ { Attribute = mkAttrK; Direction = IndexColumnDirection.Ascending } ]
        IsUnique           = false
        IsPrimaryKey       = false
        ExtendedProperties = []
        Filter             = filter
        IncludedColumns    = []
        IsPlatformAuto = false; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false }

// ---------------------------------------------------------------------------
// Adapter pickup: parseIndex reads filterDefinition from V1 JSON.
// ---------------------------------------------------------------------------

let private parseJson (envelope: string) : Result<Catalog> =
    CatalogReader.parse (CatalogReader.SnapshotSource.SnapshotJson envelope)
    |> fun t -> t.Result

let private buildEnvelope (indexBlock: string) : string =
    sprintf """{
      "modules": [{
        "ssKey": "33333333-3333-3333-3333-333333333333",
        "name": "M",
        "physicalName": "M",
        "isActive": true,
        "entities": [{
          "ssKey": "11111111-1111-1111-1111-111111111111",
          "name": "Widget",
          "physicalName": "OSUSR_X_WIDGET",
          "db_schema": "dbo",
          "isExternal": false,
          "isStatic": false,
          "isActive": true,
          "attributes": [
            { "ssKey": "22222222-2222-2222-2222-222222222222", "name": "Id", "physicalName": "ID", "dataType": "Identifier", "isMandatory": true, "isIdentifier": true, "isAutoNumber": true, "isReference": 0 }
          ],
          "indexes": [
            %s
          ]
        }]
      }]
    }""" indexBlock

[<Fact>]
let ``Adapter pickup: parseIndex captures filterDefinition when present`` () =
    let envelope =
        buildEnvelope """{
          "name": "IX_Widget_Active",
          "isPrimary": false,
          "isUnique": false,
          "filterDefinition": "[IsActive] = 1",
          "columns": [ { "attribute": "Id", "ordinal": 1 } ]
        }"""
    match parseJson envelope with
    | Ok catalog ->
        let allKinds = Catalog.allKinds catalog
        Assert.Single allKinds |> ignore
        let kind = allKinds.[0]
        Assert.Single kind.Indexes |> ignore
        match kind.Indexes.[0].Filter with
        | Some raw -> Assert.Equal ("[IsActive] = 1", raw)
        | None -> Assert.Fail "expected Filter = Some \"[IsActive] = 1\""
    | Error errs ->
        Assert.Fail (sprintf "parseJsonString failed: %A" errs)

[<Fact>]
let ``Adapter pickup: parseIndex defaults Filter to None when filterDefinition absent`` () =
    // sampleCatalog fixtures don't carry filterDefinition; all indexes
    // should have Filter = None.
    let allKinds = Catalog.allKinds sampleCatalog
    let allIndexes = allKinds |> List.collect (fun k -> k.Indexes)
    Assert.All (allIndexes, fun idx -> Assert.True (Option.isNone idx.Filter, sprintf "expected Filter = None for index %A" idx.Name))

[<Fact>]
let ``Adapter pickup: parseIndex normalizes whitespace-only filterDefinition to None`` () =
    let envelope =
        buildEnvelope """{
          "name": "IX_Widget_Blank",
          "isPrimary": false,
          "isUnique": false,
          "filterDefinition": "   ",
          "columns": [ { "attribute": "Id", "ordinal": 1 } ]
        }"""
    match parseJson envelope with
    | Ok catalog ->
        let idx = Catalog.allKinds catalog |> List.head |> fun k -> k.Indexes |> List.head
        Assert.True (Option.isNone idx.Filter)
    | Error errs ->
        Assert.Fail (sprintf "parseJsonString failed: %A" errs)

// ---------------------------------------------------------------------------
// Emission: buildCreateIndex renders WHERE clause when Filter is Some.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Emission: buildCreateIndex emits WHERE clause when Filter is Some`` () =
    let idxDef : IndexDef =
        {
            Name = "IX_Active"
            Table = mkTableId "dbo" "T_Widget"
            Columns = [ { Name = "Id"; Direction = IndexDefColumnDirection.Ascending } ]
            IsUnique = false
            Filter = Some "[IsActive] = 1"
            IncludedColumns = []; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false }
    let stmt = (ScriptDomBuild.buildCreateIndex idxDef).Value
    Assert.NotNull stmt.FilterPredicate

[<Fact>]
let ``Emission: buildCreateIndex omits WHERE clause when Filter is None`` () =
    let idxDef : IndexDef =
        {
            Name = "IX_Plain"
            Table = mkTableId "dbo" "T_Widget"
            Columns = [ { Name = "Id"; Direction = IndexDefColumnDirection.Ascending } ]
            IsUnique = false
            Filter = None
            IncludedColumns = []; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false }
    let stmt = (ScriptDomBuild.buildCreateIndex idxDef).Value
    Assert.Null stmt.FilterPredicate

[<Fact>]
let ``Emission: buildCreateIndex silently skips Filter on parse failure`` () =
    // Chapter 4.5 open Q3 — parse failures emit Diagnostic + skip
    // (Diagnostic emission deferred to a later slice; silent-skip
    // for now).
    let idxDef : IndexDef =
        {
            Name = "IX_BadFilter"
            Table = mkTableId "dbo" "T_Widget"
            Columns = [ { Name = "Id"; Direction = IndexDefColumnDirection.Ascending } ]
            IsUnique = false
            // Intentionally malformed SQL.
            Filter = Some "NOT A VALID FILTER ((("
            IncludedColumns = []; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false }
    let stmt = (ScriptDomBuild.buildCreateIndex idxDef).Value
    Assert.Null stmt.FilterPredicate

[<Fact>]
let ``Emission: T1 determinism — same input yields same FilterPredicate shape`` () =
    let idxDef : IndexDef =
        {
            Name = "IX_Det"
            Table = mkTableId "dbo" "T"
            Columns = [ { Name = "Id"; Direction = IndexDefColumnDirection.Ascending } ]
            IsUnique = true
            Filter = Some "[Status] = N'A'"
            IncludedColumns = []; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false }
    let stmt1 = (ScriptDomBuild.buildCreateIndex idxDef).Value
    let stmt2 = (ScriptDomBuild.buildCreateIndex idxDef).Value
    // Both have non-null FilterPredicate.
    Assert.NotNull stmt1.FilterPredicate
    Assert.NotNull stmt2.FilterPredicate

// ---------------------------------------------------------------------------
// End-to-end: ScriptDomGenerate renders CREATE INDEX with WHERE clause.
// ---------------------------------------------------------------------------

[<Fact>]
let ``E2E: rendered SQL contains WHERE clause when Filter is Some`` () =
    let idxDef : IndexDef =
        {
            Name = "IX_Active"
            Table = mkTableId "dbo" "T_Widget"
            Columns = [ { Name = "Id"; Direction = IndexDefColumnDirection.Ascending } ]
            IsUnique = false
            Filter = Some "[IsActive] = 1"
            IncludedColumns = []; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false }
    let sql = ScriptDomGenerate.toText (seq { Statement.CreateIndex idxDef })
    Assert.Contains ("WHERE", sql)
    Assert.Contains ("[IsActive]", sql)
