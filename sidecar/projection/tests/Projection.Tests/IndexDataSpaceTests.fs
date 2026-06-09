module Projection.Tests.IndexDataSpaceTests

// Slice A.4.7'-prelude.row56-dataspace (LR7 closure) — V1 index
// dataspace placement brought in end-to-end: V1 `#AllIdx
// .DataSpaceName/Type/PartitionColumnsJson` source-side data →
// V2 `Index.DataSpace : DataSpace option` IR → ScriptDom
// `CreateIndexStatement.OnFileGroupOrPartitionScheme` emission.
//
// Per pillar 9: pure DataIntent. The new `indexDataSpace` Site
// in `SsdtDdlEmitter.registeredMetadata` is TransformRegistry-
// worthy because it's a structurally distinct emission feature
// (sibling to `indexDataCompression`); per the existing Sites
// enumeration discipline, every distinct V1-emission axis V2
// carries gets its own Site.

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v    -> v
    | FsResult.Error e -> invalidOp (sprintf "expected Ok; got %A" e)

let private enrich (c: Catalog) : Catalog =
    (CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)).Value


let private bodyOf (k: SsKey) (cat: Catalog) : string =
    let artifact = SsdtDdlEmitter.emitSlices (enrich cat) |> mustOk
    (ArtifactByKind.toMap artifact |> Map.find k).Body

// ---------------------------------------------------------------------------
// Fixture: a kind with a single non-PK index; DataSpace varies
// across tests via record update.
// ---------------------------------------------------------------------------

let private dsKindKey   = kindKey ["DataSpaceFixture"]
let private dsIdAttrKey = attrKey ["DataSpaceFixture"; "Id"]
let private dsNameAttrKey = attrKey ["DataSpaceFixture"; "Name"]
let private dsIdxKey =
    SsKey.synthesizedComposite "OS_IDX" ["DataSpaceFixture"; "IX_Name"]
    |> Result.value

let private dsKind (dataSpace: DataSpace option) : Kind =
    let idAttr =
        { Attribute.create dsIdAttrKey (mkName "Id") Integer with
            Column       = ColumnRealization.create "ID" false |> Result.value
            IsPrimaryKey = true
            IsMandatory  = true }
    let nameAttr =
        { Attribute.create dsNameAttrKey (mkName "Name") Text with
            Column      = ColumnRealization.create "NAME" false |> Result.value
            Length      = Some 100
            IsMandatory = true }
    let idx =
        { Index.create dsIdxKey (mkName "IX_DataSpaceFixture_Name")
            (IndexColumn.ascendingList [ dsNameAttrKey ]) with
            Uniqueness = NotUnique
            DataSpace  = dataSpace }
    { Kind.create dsKindKey (mkName "DataSpaceFixture")
        (TableId.create "dbo" "OSUSR_DS_FIXTURE" |> Result.value)
        [ idAttr; nameAttr ]
      with Indexes = [ idx ] }

let private dsCatalog (dataSpace: DataSpace option) : Catalog =
    {
        Modules =
            [ { SsKey = modKey "DataSpaceFixtureMod"
                Name = mkName "DataSpaceFixtureMod"
                Kinds = [ dsKind dataSpace ]
                IsActive = true
                ExtendedProperties = [] } ]
        Sequences = []
    }

// ---------------------------------------------------------------------------
// LR7: Filegroup variant — emit `ON [filegroup]`.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7'-prelude.row56-dataspace LR7 Filegroup: CREATE INDEX emits ON [filegroup]`` () =
    let cat = dsCatalog (Some (DataSpace.Filegroup "INDEX_FG"))
    let body = bodyOf dsKindKey cat
    Assert.Contains ("CREATE INDEX [IX_DataSpaceFixture_Name]", body)
    Assert.Contains ("ON [INDEX_FG]", body)

[<Fact>]
let ``A.4.7'-prelude.row56-dataspace LR7 Filegroup: PRIMARY filegroup name renders explicitly`` () =
    let cat = dsCatalog (Some (DataSpace.Filegroup "PRIMARY"))
    let body = bodyOf dsKindKey cat
    Assert.Contains ("ON [PRIMARY]", body)

[<Fact>]
let ``A.4.7'-prelude.row56-dataspace LR7 None: CREATE INDEX omits ON clause when DataSpace = None`` () =
    let cat = dsCatalog None
    let body = bodyOf dsKindKey cat
    // The ON clause for index target table ALWAYS appears (ON [dbo].[table]);
    // assert no SECOND ON clause for filegroup placement.
    let firstOnIndex = body.IndexOf "ON "
    Assert.True(firstOnIndex > 0)
    let nextOnIndex = body.IndexOf("ON ", firstOnIndex + 1)
    // Default (no DataSpace) — no second "ON " clause for filegroup.
    if nextOnIndex >= 0 then
        // If a second ON exists, it must NOT be a filegroup/scheme reference.
        // V2 emits `ON [schema].[table]` only.
        let snippet = body.Substring(nextOnIndex, min 30 (body.Length - nextOnIndex))
        Assert.True(
            snippet.Contains "[dbo]",
            sprintf "unexpected second ON clause: %s" snippet)

// ---------------------------------------------------------------------------
// LR7: PartitionScheme variant — emit `ON [scheme]([col])`.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7'-prelude.row56-dataspace LR7 PartitionScheme: CREATE INDEX emits ON [scheme] ([col])`` () =
    // ScriptDom renders `ON [name] ([col])` with a space between
    // the scheme name and the column-list parenthesis (pinned
    // generator-option behavior). Filegroup form omits the column
    // list entirely; the space-vs-no-space distinguishes the two
    // visually.
    let cat =
        dsCatalog (Some (DataSpace.PartitionScheme ("PS_MonthlyRange", [ "Name" ])))
    let body = bodyOf dsKindKey cat
    Assert.Contains ("ON [PS_MonthlyRange] ([Name])", body)

[<Fact>]
let ``A.4.7'-prelude.row56-dataspace LR7 PartitionScheme: multi-column partition key emits comma-separated list`` () =
    let cat =
        dsCatalog (Some (DataSpace.PartitionScheme ("PS_Composite", [ "Name"; "Id" ])))
    let body = bodyOf dsKindKey cat
    Assert.Contains ("ON [PS_Composite] ([Name], [Id])", body)

// ---------------------------------------------------------------------------
// T1 byte-determinism on DataSpace emission.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7'-prelude.row56-dataspace LR7: T1 byte-determinism holds across DataSpace variants`` () =
    for ds in
        [ None
          Some (DataSpace.Filegroup "INDEX_FG")
          Some (DataSpace.PartitionScheme ("PS", [ "Name" ])) ] do
        let cat = dsCatalog ds
        let b1 = bodyOf dsKindKey cat
        let b2 = bodyOf dsKindKey cat
        Assert.Equal(b1, b2)

// ---------------------------------------------------------------------------
// TransformRegistry classification: the new indexDataSpace Site is
// DataIntent (emitter projects evidence; A18 amended) and present in
// SsdtDdlEmitter.registeredMetadata's Sites enumeration.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7'-prelude.row56-dataspace: SsdtDdlEmitter.registeredMetadata exposes indexDataSpace Site as DataIntent`` () =
    let site =
        SsdtDdlEmitter.registeredMetadata.Sites
        |> List.tryFind (fun s -> s.SiteName = "indexDataSpace")
    match site with
    | Some s ->
        Assert.Equal(DataIntent, s.Classification)
        Assert.Contains("ON [filegroup]", s.Rationale)
        Assert.Contains("partition_scheme", s.Rationale)
    | None ->
        Assert.Fail("expected indexDataSpace Site in SsdtDdlEmitter.registeredMetadata")
