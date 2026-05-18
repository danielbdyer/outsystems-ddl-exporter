module Projection.Tests.IndexColumnDirectionTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Chapter 4.9 slice γ — `IndexColumnDirection` + `IndexColumn` IR fidelity
// lift. Retires the third A.0'-deferred-out-of-scope concept. Closed DU
// (`Ascending | Descending`); `Ascending` maps to ScriptDom's
// `SortOrder.NotSpecified` (V1 IndexScriptBuilder convention emits the
// DESC keyword only on descending columns).
// ---------------------------------------------------------------------------

let private mkKey (v: string) : SsKey =
    SsKey.synthesized "test" v |> Result.value

let private mkName (v: string) : Name = Name.create v |> Result.value

let private mkTableId (schema: string) (table: string) : TableId =
    TableId.create schema table |> Result.value

// ---------------------------------------------------------------------------
// Emission: ScriptDomBuild.buildCreateIndex sets SortOrder per IndexDefColumn.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice γ: buildCreateIndex sets SortOrder.NotSpecified for Ascending columns`` () =
    let idxDef : IndexDef =
        {
            Name = "IX_Asc"
            Table = mkTableId "dbo" "T"
            Columns = [ { Name = "Id"; Direction = IndexDefColumnDirection.Ascending } ]
            IsUnique = false
            Filter = None
            IncludedColumns = []
            FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None
        }
    let stmt = (ScriptDomBuild.buildCreateIndex idxDef).Value
    let col = stmt.Columns |> Seq.head
    Assert.Equal(Microsoft.SqlServer.TransactSql.ScriptDom.SortOrder.NotSpecified, col.SortOrder)

[<Fact>]
let ``Slice γ: buildCreateIndex sets SortOrder.Descending for Descending columns`` () =
    let idxDef : IndexDef =
        {
            Name = "IX_Desc"
            Table = mkTableId "dbo" "T"
            Columns = [ { Name = "Id"; Direction = IndexDefColumnDirection.Descending } ]
            IsUnique = false
            Filter = None
            IncludedColumns = []
            FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None
        }
    let stmt = (ScriptDomBuild.buildCreateIndex idxDef).Value
    let col = stmt.Columns |> Seq.head
    Assert.Equal(Microsoft.SqlServer.TransactSql.ScriptDom.SortOrder.Descending, col.SortOrder)

[<Fact>]
let ``Slice γ: rendered SQL contains DESC keyword for descending columns`` () =
    let idxDef : IndexDef =
        {
            Name = "IX_DescRender"
            Table = mkTableId "dbo" "T"
            Columns =
                [ { Name = "CreatedAt"; Direction = IndexDefColumnDirection.Descending }
                  { Name = "Id"; Direction = IndexDefColumnDirection.Ascending } ]
            IsUnique = false
            Filter = None
            IncludedColumns = []
            FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None
        }
    let sql = ScriptDomGenerate.toText (seq { Statement.CreateIndex idxDef })
    Assert.Contains("[CreatedAt] DESC", sql)
    // Ascending column has no DESC keyword (SortOrder.NotSpecified
    // renders as bare column name per V1 convention).
    Assert.DoesNotContain("[Id] DESC", sql)
    Assert.DoesNotContain("[Id] ASC", sql)

// ---------------------------------------------------------------------------
// Adapter pickup: parseIndex reads `direction` per-column.
// ---------------------------------------------------------------------------

let private v1FixtureWithDirections : string =
    """{
  "exportedAtUtc": "2026-05-17T00:00:00Z",
  "modules": [
    { "name": "AppCore", "isSystem": false, "isActive": true,
      "entities": [
        { "name": "Event", "physicalName": "OSUSR_APPCORE_EVENT",
          "isStatic": false, "isExternal": false, "isActive": true,
          "db_catalog": null, "db_schema": "dbo",
          "attributes": [
            { "name": "Id", "physicalName": "ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": true, "isAutoNumber": true, "isActive": true,
              "isReference": 0, "refEntityId": null, "refEntity_name": null,
              "refEntity_physicalName": null, "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0, "external_dbType": null,
              "physical_isPresentButInactive": 0 },
            { "name": "CreatedAt", "physicalName": "CREATED_AT", "originalName": null,
              "dataType": "Text", "length": null, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": false, "isAutoNumber": false, "isActive": true,
              "isReference": 0, "refEntityId": null, "refEntity_name": null,
              "refEntity_physicalName": null, "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0, "external_dbType": null,
              "physical_isPresentButInactive": 0 }
          ],
          "relationships": [],
          "indexes": [
            { "name": "IX_Event_CreatedAt_DESC", "isPrimary": false, "isUnique": false,
              "columns": [
                { "attribute": "CreatedAt", "ordinal": 0, "direction": "DESC" },
                { "attribute": "Id", "ordinal": 1, "direction": "ASC" }
              ] }
          ],
          "triggers": [] }
      ]
    }
  ]
}"""

let private parseSync (source: CatalogReader.SnapshotSource) : Result<Catalog> =
    (CatalogReader.parse source).GetAwaiter().GetResult()

[<Fact>]
let ``Slice γ: JSON adapter carries IndexColumnDirection.Descending when source declares DESC`` () =
    match parseSync (CatalogReader.SnapshotJson v1FixtureWithDirections) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = catalog.Modules |> List.head |> fun m -> m.Kinds |> List.head
        let idx = kind.Indexes |> List.head
        // Columns sorted by ordinal: [CreatedAt DESC; Id ASC].
        Assert.Equal(2, List.length idx.Columns)
        Assert.Equal(IndexColumnDirection.Descending, (List.item 0 idx.Columns).Direction)
        Assert.Equal(IndexColumnDirection.Ascending, (List.item 1 idx.Columns).Direction)

let private v1FixtureWithoutDirections : string =
    """{
  "exportedAtUtc": "2026-05-17T00:00:00Z",
  "modules": [
    { "name": "AppCore", "isSystem": false, "isActive": true,
      "entities": [
        { "name": "Event", "physicalName": "OSUSR_APPCORE_EVENT",
          "isStatic": false, "isExternal": false, "isActive": true,
          "db_catalog": null, "db_schema": "dbo",
          "attributes": [
            { "name": "Id", "physicalName": "ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": true, "isAutoNumber": true, "isActive": true,
              "isReference": 0, "refEntityId": null, "refEntity_name": null,
              "refEntity_physicalName": null, "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0, "external_dbType": null,
              "physical_isPresentButInactive": 0 },
            { "name": "CreatedAt", "physicalName": "CREATED_AT", "originalName": null,
              "dataType": "Text", "length": null, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": false, "isAutoNumber": false, "isActive": true,
              "isReference": 0, "refEntityId": null, "refEntity_name": null,
              "refEntity_physicalName": null, "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0, "external_dbType": null,
              "physical_isPresentButInactive": 0 }
          ],
          "relationships": [],
          "indexes": [
            { "name": "IX_Event_Plain", "isPrimary": false, "isUnique": false,
              "columns": [
                { "attribute": "CreatedAt", "ordinal": 0 },
                { "attribute": "Id", "ordinal": 1, "direction": null }
              ] }
          ],
          "triggers": [] }
      ]
    }
  ]
}"""

[<Fact>]
let ``Slice γ: JSON adapter defaults to Ascending when direction is absent or null`` () =
    match parseSync (CatalogReader.SnapshotJson v1FixtureWithoutDirections) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let kind = catalog.Modules |> List.head |> fun m -> m.Kinds |> List.head
        let idx = kind.Indexes |> List.head
        Assert.All(idx.Columns, fun c -> Assert.Equal(IndexColumnDirection.Ascending, c.Direction))
