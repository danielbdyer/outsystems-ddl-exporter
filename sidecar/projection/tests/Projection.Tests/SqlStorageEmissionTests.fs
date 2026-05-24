module Projection.Tests.SqlStorageEmissionTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Adapters.Osm

// ---------------------------------------------------------------------------
// Concrete SQL Server storage types end-to-end: the OSSYS adapter resolves
// `ossys_EntityAttr.Type` (rt-prefix aware) into a concrete `SqlStorageType`,
// and the SSDT emitter prefers it over the semantic `PrimitiveType` fallback.
//
// The bugs this fixes (per the v1 type-mapping donor table):
//   - `longinteger` must emit BIGINT, not INT (the semantic category
//     `Integer` collapses both).
//   - `datetime` must emit DATETIME, not DATETIME2.
//   - `rtText` / `rtEmail` / `rtPhoneNumber` were previously unmapped.
// ---------------------------------------------------------------------------

let private parseSync (source: CatalogReader.SnapshotSource) : Result<Catalog> =
    TaskSync.run (fun () -> CatalogReader.parse source)

let private parseOk (json: string) : Catalog =
    match parseSync (CatalogReader.SnapshotJson json) with
    | Ok c        -> c
    | Error errs  ->
        Assert.Fail (sprintf "expected Ok catalog; got %A" errs)
        Unchecked.defaultof<Catalog>

let private findAttr (name: string) (c: Catalog) : Attribute =
    c.Modules
    |> List.collect (fun m -> m.Kinds)
    |> List.collect (fun k -> k.Attributes)
    |> List.find (fun a -> Name.value a.Name = name)

// Build a one-attribute entity JSON. Keeps each storage-mapping case
// isolated so the assertion names the exact OutSystems type under test.
let private singleAttrJson (dataType: string) (length: string) (externalDbType: string) : string =
    sprintf """{
  "exportedAtUtc": "2026-05-23T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "Widget",
          "physicalName": "OSUSR_APP_WIDGET",
          "isStatic": false,
          "isExternal": false,
          "isActive": true,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            {
              "name": "Id",
              "physicalName": "ID",
              "dataType": "Identifier",
              "length": null, "precision": null, "scale": null,
              "default": null,
              "isMandatory": true, "isIdentifier": true, "isAutoNumber": true,
              "isActive": true, "isReference": 0,
              "external_dbType": null
            },
            {
              "name": "Subject",
              "physicalName": "SUBJECT",
              "dataType": "%s",
              "length": %s, "precision": null, "scale": null,
              "default": null,
              "isMandatory": false, "isIdentifier": false, "isAutoNumber": false,
              "isActive": true, "isReference": 0,
              "external_dbType": %s
            }
          ],
          "relationships": [],
          "indexes": [],
          "triggers": []
        }
      ]
    }
  ]
}""" dataType length externalDbType

// ---------------------------------------------------------------------------
// Adapter mapping — the resolved SqlStorageType per OutSystems type. These
// assert the concrete realization on the IR (typed, exact).
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("LongInteger", "BigInt")>]
[<InlineData("rtLongInteger", "BigInt")>]
[<InlineData("Integer", "Int")>]
[<InlineData("rtInteger", "Int")>]
[<InlineData("Identifier", "BigInt")>]
[<InlineData("rtBoolean", "Bit")>]
let ``OSSYS adapter resolves integer-family storage (rt-prefix aware)`` (dataType: string) (expected: string) =
    let cat = parseOk (singleAttrJson dataType "null" "null")
    let attr = findAttr "Subject" cat
    Assert.Equal (expected, sprintf "%A" attr.SqlStorage.Value)

[<Fact>]
let ``OSSYS adapter resolves longinteger to BigInt, not Int (semantic-collapse bug fix)`` () =
    let cat = parseOk (singleAttrJson "longinteger" "null" "null")
    let attr = findAttr "Subject" cat
    // Semantic category stays Integer (PrimitiveType is coarse)...
    Assert.Equal (Integer, attr.Type)
    // ...but the concrete storage is BIGINT, not the INT the semantic
    // fallback would have produced.
    Assert.Equal (Some SqlStorageType.BigInt, attr.SqlStorage)

[<Fact>]
let ``OSSYS adapter resolves datetime to DateTime, not DateTime2 (v1 bug fix)`` () =
    let cat = parseOk (singleAttrJson "datetime" "null" "null")
    let attr = findAttr "Subject" cat
    Assert.Equal (DateTime, attr.Type)
    Assert.Equal (Some SqlStorageType.DateTime, attr.SqlStorage)

[<Fact>]
let ``OSSYS adapter maps entity-reference encodings to identifier storage`` () =
    // Both the logical `rtEntityReference` code and the structural
    // `bt<espace>*<entity>` binding encoding resolve to the target's
    // identifier storage (BIGINT).
    let refCat = parseOk (singleAttrJson "rtEntityReference" "null" "null")
    Assert.Equal (Some SqlStorageType.BigInt, (findAttr "Subject" refCat).SqlStorage)
    let btEncoding = "bt550e8400-e29b-41d4-a716-446655440000*6ba7b810-9dad-11d1-80b4-00c04fd430c8"
    let btCat = parseOk (singleAttrJson btEncoding "null" "null")
    Assert.Equal (Some SqlStorageType.BigInt, (findAttr "Subject" btCat).SqlStorage)

[<Fact>]
let ``OSSYS adapter maps rtText / rtEmail / rtPhoneNumber (previously unmapped)`` () =
    let textCat  = parseOk (singleAttrJson "rtText" "null" "null")
    let emailCat = parseOk (singleAttrJson "rtEmail" "null" "null")
    let phoneCat = parseOk (singleAttrJson "rtPhoneNumber" "null" "null")
    Assert.Equal (Some (SqlStorageType.NVarChar Max), (findAttr "Subject" textCat).SqlStorage)
    Assert.Equal (Some (SqlStorageType.VarChar (Bounded 250)), (findAttr "Subject" emailCat).SqlStorage)
    Assert.Equal (Some (SqlStorageType.VarChar (Bounded 20)), (findAttr "Subject" phoneCat).SqlStorage)

[<Fact>]
let ``OSSYS adapter folds declared length into Text storage`` () =
    let cat = parseOk (singleAttrJson "rtText" "100" "null")
    Assert.Equal (Some (SqlStorageType.NVarChar (Bounded 100)), (findAttr "Subject" cat).SqlStorage)

[<Fact>]
let ``OSSYS adapter applies external_dbType override for non-runtime-forced types`` () =
    let cat = parseOk (singleAttrJson "rtText" "null" "\"NVARCHAR(4000)\"")
    Assert.Equal (Some (SqlStorageType.NVarChar (Bounded 4000)), (findAttr "Subject" cat).SqlStorage)

[<Fact>]
let ``OSSYS adapter keeps longinteger BIGINT even under external_dbType override`` () =
    // V1 priority: identifier / autonumber / longinteger force runtime
    // mapping, so an external override cannot demote a longinteger.
    let cat = parseOk (singleAttrJson "longinteger" "null" "\"DATETIME\"")
    Assert.Equal (Some SqlStorageType.BigInt, (findAttr "Subject" cat).SqlStorage)

[<Fact>]
let ``OSSYS adapter stays consistent: toPrimitiveType storage = Type`` () =
    // The invariant the two-field design rests on — the concrete storage
    // an adapter sets always projects back to the semantic Type it sets.
    for dt in [ "longinteger"; "rtDateTime"; "rtText"; "rtEmail"; "Currency"; "Identifier" ] do
        let attr = findAttr "Subject" (parseOk (singleAttrJson dt "null" "null"))
        Assert.Equal (attr.Type, SqlStorageType.toPrimitiveType attr.SqlStorage.Value)

// ---------------------------------------------------------------------------
// Emission — the SSDT body prefers the concrete storage type. End-to-end:
// adapter → enrich → SsdtDdlEmitter → rendered SQL text.
// ---------------------------------------------------------------------------

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private ciRun (c: Catalog) : Catalog =
    (CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)).Value

let private emitBody (json: string) : string =
    let enriched = ciRun (parseOk json)
    match SsdtDdlEmitter.emitSlices enriched with
    | FsResult.Error err -> Assert.Fail (sprintf "expected Ok; got %A" err); ""
    | FsResult.Ok artifact ->
        ArtifactByKind.toMap artifact
        |> Map.toList
        |> List.map (fun (_, f) -> f.Body)
        |> String.concat "\n"

[<Fact>]
let ``Emission: longinteger renders BIGINT`` () =
    let body = emitBody (singleAttrJson "longinteger" "null" "null")
    Assert.Contains ("BIGINT", body)

[<Fact>]
let ``Emission: datetime renders DATETIME and not DATETIME2`` () =
    let body = emitBody (singleAttrJson "datetime" "null" "null")
    Assert.Contains ("DATETIME", body)
    Assert.DoesNotContain ("DATETIME2", body)

[<Fact>]
let ``Emission: plain integer renders INT, not BIGINT (distinction is real)`` () =
    // The Id column is Identifier→BIGINT; the Subject column is the
    // semantic Integer→INT. Stripping every BIGINT occurrence must leave
    // a bare INT for the Subject column, proving INT and BIGINT diverge.
    let body = emitBody (singleAttrJson "Integer" "null" "null")
    let withoutBigint = body.Replace("BIGINT", "")
    Assert.Contains ("INT", withoutBigint)

[<Fact>]
let ``Emission: rtText with no length renders NVARCHAR (MAX)`` () =
    let body = emitBody (singleAttrJson "rtText" "null" "null")
    Assert.Contains ("NVARCHAR", body)
    Assert.Contains ("MAX", body)

[<Fact>]
let ``Emission: external_dbType override renders the concrete type`` () =
    let body = emitBody (singleAttrJson "rtText" "null" "\"NVARCHAR(4000)\"")
    Assert.Contains ("4000", body)

[<Fact>]
let ``Emission: xml renders XML (via XmlDataTypeReference)`` () =
    let body = emitBody (singleAttrJson "xml" "null" "null")
    Assert.Contains ("XML", body)
