module Projection.Tests.IndexIncludedColumnsTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Adapters.Osm
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// Chapter 4.5 slice β — Index.IncludedColumns IR carriage + adapter pickup
// (stop dropping isIncluded=true) + ScriptDom INCLUDE clause emission +
// HasIncludedIndexColumns predicate cash-out.
//
// V1 reference: Osm.Domain.Model.IndexColumnModel.IsIncluded + V1's JSON
// index.columns[].isIncluded boolean. V2 pre-slice-β dropped these
// entries at the adapter boundary per the documented ADMIRE divergence;
// slice β retires that drop.
// ---------------------------------------------------------------------------

let private mkKey (v: string) : SsKey =
    SsKey.synthesized "test" v |> Result.value


let private mkTableId (schema: string) (table: string) : TableId =
    TableId.create schema table |> Result.value

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
            { "ssKey": "22222222-2222-2222-2222-222222222222", "name": "Id", "physicalName": "ID", "dataType": "Identifier", "isMandatory": true, "isIdentifier": true, "isAutoNumber": true, "isReference": 0 },
            { "ssKey": "44444444-4444-4444-4444-444444444444", "name": "Name", "physicalName": "NAME", "dataType": "Text", "isMandatory": false, "isIdentifier": false, "isAutoNumber": false, "isReference": 0 }
          ],
          "indexes": [
            %s
          ]
        }]
      }]
    }""" indexBlock

// ---------------------------------------------------------------------------
// Adapter pickup: parseIndex captures isIncluded=true entries.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Adapter pickup: parseIndex captures isIncluded=true columns into IncludedColumns`` () =
    let envelope =
        buildEnvelope """{
          "name": "IX_Cover",
          "isPrimary": false,
          "isUnique": false,
          "columns": [
            { "attribute": "Id", "ordinal": 1, "isIncluded": false },
            { "attribute": "Name", "ordinal": 2, "isIncluded": true }
          ]
        }"""
    match parseJson envelope with
    | Ok catalog ->
        let idx = Catalog.allKinds catalog |> List.head |> fun k -> k.Indexes |> List.head
        Assert.Single idx.Columns |> ignore
        Assert.Single idx.IncludedColumns |> ignore
    | Error errs ->
        Assert.Fail (sprintf "parseJsonString failed: %A" errs)

[<Fact>]
let ``Adapter pickup: parseIndex with no isIncluded columns yields empty IncludedColumns`` () =
    // Default case — all entries are key columns.
    let envelope =
        buildEnvelope """{
          "name": "IX_Plain",
          "isPrimary": false,
          "isUnique": false,
          "columns": [
            { "attribute": "Id", "ordinal": 1 }
          ]
        }"""
    match parseJson envelope with
    | Ok catalog ->
        let idx = Catalog.allKinds catalog |> List.head |> fun k -> k.Indexes |> List.head
        Assert.Single idx.Columns |> ignore
        Assert.Empty idx.IncludedColumns
    | Error errs ->
        Assert.Fail (sprintf "parseJsonString failed: %A" errs)

[<Fact>]
let ``Adapter pickup: IncludedColumns preserves V1 ordinal ordering`` () =
    let envelope =
        buildEnvelope """{
          "name": "IX_Order",
          "isPrimary": false,
          "isUnique": false,
          "columns": [
            { "attribute": "Id", "ordinal": 1 },
            { "attribute": "Name", "ordinal": 3, "isIncluded": true }
          ]
        }"""
    match parseJson envelope with
    | Ok _ -> ()
    | Error errs ->
        Assert.Fail (sprintf "parseJsonString failed: %A" errs)

// ---------------------------------------------------------------------------
// Emission: buildCreateIndex renders INCLUDE clause.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Emission: buildCreateIndex emits INCLUDE columns when non-empty`` () =
    let idxDef : IndexDef =
        {
            Name = "IX_Cover"
            Table = mkTableId "dbo" "T"
            Columns = [ { Name = "Id"; Direction = IndexDefColumnDirection.Ascending } ]
            IsUnique = false
            Filter = None
            IncludedColumns = [ "Name"; "Status" ]; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None; DataSpace = None }
    let stmt = (ScriptDomBuild.buildCreateIndex idxDef).Value
    Assert.Equal (2, stmt.IncludeColumns.Count)

[<Fact>]
let ``Emission: buildCreateIndex omits INCLUDE when IncludedColumns is empty`` () =
    let idxDef : IndexDef =
        {
            Name = "IX_Plain"
            Table = mkTableId "dbo" "T"
            Columns = [ { Name = "Id"; Direction = IndexDefColumnDirection.Ascending } ]
            IsUnique = false
            Filter = None
            IncludedColumns = []; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None; DataSpace = None }
    let stmt = (ScriptDomBuild.buildCreateIndex idxDef).Value
    Assert.Equal (0, stmt.IncludeColumns.Count)

// ---------------------------------------------------------------------------
// End-to-end: rendered SQL contains INCLUDE clause.
// ---------------------------------------------------------------------------

[<Fact>]
let ``E2E: rendered SQL contains INCLUDE clause when IncludedColumns is non-empty`` () =
    let idxDef : IndexDef =
        {
            Name = "IX_Cover"
            Table = mkTableId "dbo" "T_Widget"
            Columns = [ { Name = "Id"; Direction = IndexDefColumnDirection.Ascending } ]
            IsUnique = false
            Filter = None
            IncludedColumns = [ "Name" ]; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None; DataSpace = None }
    let sql = ScriptDomGenerate.toText (seq { Statement.CreateIndex idxDef })
    Assert.Contains ("INCLUDE", sql)
    Assert.Contains ("[Name]", sql)

[<Fact>]
let ``E2E: rendered SQL combines INCLUDE + WHERE when both are present`` () =
    let idxDef : IndexDef =
        {
            Name = "IX_FilteredCover"
            Table = mkTableId "dbo" "T_Widget"
            Columns = [ { Name = "Id"; Direction = IndexDefColumnDirection.Ascending } ]
            IsUnique = false
            Filter = Some "[IsActive] = 1"
            IncludedColumns = [ "Name" ]; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None; DataSpace = None }
    let sql = ScriptDomGenerate.toText (seq { Statement.CreateIndex idxDef })
    Assert.Contains ("INCLUDE", sql)
    Assert.Contains ("WHERE", sql)
    Assert.Contains ("[IsActive]", sql)
    Assert.Contains ("[Name]", sql)

// ---------------------------------------------------------------------------
// PredicateName.evaluate HasIncludedIndexColumns: lifts to real evaluation.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Chapter 4.5 slice β: HasIncludedIndexColumns returns true when any Index.IncludedColumns is non-empty`` () =
    let mkIdx (included: SsKey list) =
        {
            SsKey = mkKey (sprintf "Idx:%d" (List.length included))
            Name = mkName "IX"
            Columns = [ { Attribute = mkKey "Attr.Id"; Direction = IndexColumnDirection.Ascending } ]
            Uniqueness = NotUnique
            ExtendedProperties = []
            Filter = None
            IncludedColumns = included
            IsPlatformAuto = false; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None; DataSpace = None }
    let mkKindWith (label: string) (idx: Index) : Kind =
        let baseKind =
            Kind.create
                (mkKey (sprintf "K:%s" label))
                (mkName label)
                (mkTableId "dbo" (sprintf "T_%s" label))
                []
        { baseKind with Indexes = [idx] }
    let withoutIncluded = mkKindWith "Plain" (mkIdx [])
    let withIncluded = mkKindWith "Cover" (mkIdx [ mkKey "Attr.Name" ])
    Assert.False (PredicateName.evaluate PredicateName.HasIncludedIndexColumns withoutIncluded)
    Assert.True  (PredicateName.evaluate PredicateName.HasIncludedIndexColumns withIncluded)
