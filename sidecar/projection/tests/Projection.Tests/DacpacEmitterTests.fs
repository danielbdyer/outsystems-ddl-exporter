module Projection.Tests.DacpacEmitterTests

open System.IO
open Xunit
open Microsoft.SqlServer.Dac
open Microsoft.SqlServer.Dac.Model
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — `CanonicalizeIdentity.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<Catalog>>`. This per-file shim restores the
// `Lineage<Catalog>` shape so existing assertions keep reading.
let private ciRun (c: Catalog) : Lineage<Catalog> =
    CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)

// ---------------------------------------------------------------------------
// Chapter 3.x slice α — dev-tooling DACPAC emission.
//
// Per `CHAPTER_3_X_OPEN.md` strategic frame + `DECISIONS 2026-05-11 —
// Chapter 3.x DacpacEmitter open`: this emitter ships as the dev-tooling
// sibling Π for one-click local stand-up. Production deploy stays via
// `SsdtDdlEmitter.emitSlices`; `DacpacEmitter.emit` produces a `.dacpac`
// byte stream the dev team consumes via `sqlpackage`, Visual Studio, or
// `DacServices.Deploy`.
//
// T1 amendment (binary emitters): content-equality via DacFx round-trip,
// not byte-equality. DacFx embeds wall-clock timestamps in Origin.xml so
// two emit calls on the same Catalog produce non-byte-identical streams;
// the algebraic claim holds at the DacFx model level.
// ---------------------------------------------------------------------------

let private enrich (c: Catalog) : Catalog =
    (ciRun c).Value

let private mustOkBytes (r: Result<byte[]>) : byte[] =
    match r with
    | Ok v -> v
    | Error errs ->
        Assert.Fail (sprintf "expected Ok; got %A" errs)
        Unchecked.defaultof<byte[]>

let private mkName (s: string) : Name =
    match Name.create s with
    | Ok n -> n
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "DacpacEmitterTests.mkName failed: %s" codes)

/// Single-Kind catalog mirroring the pre-scope §5 minimum slice: one
/// Module, one Kind, two attributes including a PK, no FKs, no
/// indexes, no modality marks. Built inline to keep this test
/// independent of `sampleCatalog`'s fixture evolution.
let private singleKindCatalog : Catalog =
    let widgetKey = kindKey ["Widget"]
    let widgetIdKey = attrKey ["Widget"; "Id"]
    let widgetNameKey = attrKey ["Widget"; "Name"]
    let widget : Kind = {
        SsKey    = widgetKey
        Name     = mkName "Widget"
        Origin   = OsNative
        Modality = []
        Physical = { Schema = "dbo"; Table = "WIDGET"; Catalog = None }
        Attributes = [
            { Attribute.create widgetIdKey (mkName "Id") Integer with Column = { ColumnName = "ID"; IsNullable = false }; IsPrimaryKey = true }
            { Attribute.create widgetNameKey (mkName "Name") Text with Column = { ColumnName = "NAME"; IsNullable = false } }
        ]
        References = []
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }
    let m : Module = {
        SsKey = modKey "Inventory"
        Name  = mkName "Inventory"
        Kinds = [ widget ]
        IsActive = true
        ExtendedProperties = []
        }
    { Modules = [ m ]; Sequences = [] }

// ---------------------------------------------------------------------------
// Slice α acceptance — single-Kind Catalog produces non-empty bytes.
// ---------------------------------------------------------------------------

[<Fact>]
let ``DacpacEmitter.emit on single-Kind Catalog returns non-empty bytes`` () =
    let enriched = enrich singleKindCatalog
    let bytes = DacpacEmitter.emit enriched |> mustOkBytes
    Assert.NotEmpty bytes
    // DACPAC is a ZIP archive — first two bytes are 'PK' (0x50 0x4B).
    // Structural witness that DacFx serialized a real package rather
    // than an empty stream.
    Assert.Equal (byte 0x50, bytes.[0])
    Assert.Equal (byte 0x4B, bytes.[1])

// ---------------------------------------------------------------------------
// Content-equality via DacFx round-trip — slice α T1 amendment for binary
// emitters. Per `DECISIONS 2026-05-11 — Chapter 3.x DacpacEmitter open`
// commitment 3: same Catalog → DacFx model contains same Table objects
// under round-trip. Byte streams differ (Origin.xml timestamps); the
// algebraic claim holds at the model level.
// ---------------------------------------------------------------------------

[<Fact>]
let ``DacpacEmitter.emit round-trip yields one Table per Catalog Kind`` () =
    let enriched = enrich singleKindCatalog
    let bytes = DacpacEmitter.emit enriched |> mustOkBytes
    use stream = new MemoryStream(bytes)
    use package = DacPackage.Load(stream)
    use model = TSqlModel.LoadFromDacpac(stream, ModelLoadOptions())
    let tables =
        model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass)
        |> Seq.toList
    let expectedKindCount = Catalog.allKinds enriched |> List.length
    Assert.Equal (expectedKindCount, List.length tables)

// ---------------------------------------------------------------------------
// T1 (binary-emitter amendment): two emits on the same Catalog produce
// content-identical models (table enumeration matches). Byte equality
// does NOT hold — DacFx embeds wall-clock timestamps in Origin.xml — so
// the algebraic claim flows through DacFx's model API, not the stream.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1 (binary): DacpacEmitter.emit is content-deterministic under DacFx round-trip`` () =
    let enriched = enrich singleKindCatalog
    let bytes1 = DacpacEmitter.emit enriched |> mustOkBytes
    let bytes2 = DacpacEmitter.emit enriched |> mustOkBytes
    let tableNames (bs: byte[]) : Set<string> =
        use stream = new MemoryStream(bs)
        use model = TSqlModel.LoadFromDacpac(stream, ModelLoadOptions())
        model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass)
        |> Seq.map (fun obj -> obj.Name.ToString())
        |> Set.ofSeq
    Assert.Equal<Set<string>> (tableNames bytes1, tableNames bytes2)

// ---------------------------------------------------------------------------
// T11 sibling-Π commutativity (chapter 3.x slice α): SsdtDdlEmitter (the
// production directory bundle) and DacpacEmitter (this chapter's dev-
// tooling binary) agree on the SsKey-root kind-mention set. Same Catalog
// ⇒ same set of Kind names visible at the artifact surface, modulo the
// projection language. Per pre-scope §3 — "the existence of the round-
// trip IS the strongest commutativity guarantee" for binary emitters.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T11: DacpacEmitter and SsdtDdlEmitter agree on kind-mention set`` () =
    // Use sampleCatalog (Customer + Order + Country) so the property
    // exercises multiple kinds + FK across siblings.
    let enriched = enrich sampleCatalog
    // SsdtDdlEmitter side: ArtifactByKind keyset is the SsKey set.
    let ssdtKeys : Set<SsKey> =
        SsdtDdlEmitter.emitSlices enriched
        |> function
            | Ok artifact -> ArtifactByKind.keys artifact
            | Error err ->
                Assert.Fail (sprintf "SsdtDdlEmitter.emitSlices failed: %A" err)
                Set.empty
    // DacpacEmitter side: load the dacpac, enumerate Tables, derive
    // physical names. The kind-mention property is "every Kind's
    // (Schema, Table) physical pair appears as one Table object in
    // the dacpac model."
    let bytes = DacpacEmitter.emit enriched |> mustOkBytes
    use stream = new MemoryStream(bytes)
    use model = TSqlModel.LoadFromDacpac(stream, ModelLoadOptions())
    let dacpacPhysicals : Set<string * string> =
        model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass)
        |> Seq.map (fun obj ->
            let parts = obj.Name.Parts
            // ObjectIdentifier.Parts: [schema; table] for two-part names.
            // DacFx returns [dbo; TABLE] for our fixtures.
            let schema = if parts.Count >= 2 then parts.[0] else "dbo"
            let table  = if parts.Count >= 2 then parts.[1] else parts.[0]
            schema, table)
        |> Set.ofSeq
    let catalogPhysicals : Set<string * string> =
        Catalog.allKinds enriched
        |> List.map (fun k -> k.Physical.Schema, k.Physical.Table)
        |> Set.ofList
    Assert.Equal<Set<string * string>> (catalogPhysicals, dacpacPhysicals)
    // Sanity: keyset cardinality matches across siblings.
    Assert.Equal (Set.count ssdtKeys, Set.count dacpacPhysicals)

// ---------------------------------------------------------------------------
// Slice β — FK round-trip. `sampleCatalog`'s Order kind carries an inline
// FOREIGN KEY to Customer; DacFx ingests the inline constraint via
// `model.AddObjects(CREATE TABLE ...)` and re-exposes it through the
// ForeignKeyConstraint TypeClass enumeration. The round-trip is the
// structural proof: V2's Reference → SsdtDdlEmitter inline FK clause →
// DacFx model → enumerable ForeignKeyConstraint.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice β: FK references round-trip through DacFx model`` () =
    let enriched = enrich sampleCatalog
    let bytes = DacpacEmitter.emit enriched |> mustOkBytes
    use stream = new MemoryStream(bytes)
    use model = TSqlModel.LoadFromDacpac(stream, ModelLoadOptions())
    let fks =
        model.GetObjects(DacQueryScopes.UserDefined, ForeignKeyConstraint.TypeClass)
        |> Seq.toList
    let catalogFkCount =
        Catalog.allKinds enriched
        |> List.sumBy (fun k -> List.length k.References)
    Assert.Equal (catalogFkCount, List.length fks)

// ---------------------------------------------------------------------------
// Slice γ — Index round-trip. Indexes are emitted as standalone CREATE
// INDEX statements by `SsdtDdlEmitter.statements`; DacFx ingests them via
// per-statement `AddObjects` and re-exposes through the Index TypeClass
// enumeration. The fixture below carries three index variants per pre-
// scope §5 slice 3 (single-column unique; composite; non-unique).
// ---------------------------------------------------------------------------

let private indexedCatalog : Catalog =
    let widgetKey = kindKey ["IndexedWidget"]
    let idKey = attrKey ["IndexedWidget"; "Id"]
    let codeKey = attrKey ["IndexedWidget"; "Code"]
    let regionKey = attrKey ["IndexedWidget"; "Region"]
    let labelKey = attrKey ["IndexedWidget"; "Label"]
    let widget : Kind = {
        SsKey    = widgetKey
        Name     = mkName "IndexedWidget"
        Origin   = OsNative
        Modality = []
        Physical = { Schema = "dbo"; Table = "INDEXED_WIDGET"; Catalog = None }
        Attributes = [
            { Attribute.create idKey (mkName "Id") Integer with Column = { ColumnName = "ID"; IsNullable = false }; IsPrimaryKey = true }
            { Attribute.create codeKey (mkName "Code") Text with Column = { ColumnName = "CODE"; IsNullable = false } }
            { Attribute.create regionKey (mkName "Region") Text with Column = { ColumnName = "REGION"; IsNullable = false } }
            { Attribute.create labelKey (mkName "Label") Text with Column = { ColumnName = "LABEL"; IsNullable = true } }
        ]
        References = []
        Indexes = [
            // Single-column unique index on Code.
            { SsKey = idxKey ["IndexedWidget"; "UQ"; "Code"]
              Name = mkName "UQ_IndexedWidget_Code"
              Columns = IRBuilders.mkIndexColumns [ codeKey ]
              IsUnique = true; IsPrimaryKey = false; ExtendedProperties = []; Filter = None; IncludedColumns = []; IsPlatformAuto = false; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None }
            // Composite (non-unique) index on Region + Label.
            { SsKey = idxKey ["IndexedWidget"; "IX"; "RegionLabel"]
              Name = mkName "IX_IndexedWidget_RegionLabel"
              Columns = IRBuilders.mkIndexColumns [ regionKey; labelKey ]
              IsUnique = false; IsPrimaryKey = false; ExtendedProperties = []; Filter = None; IncludedColumns = []; IsPlatformAuto = false; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None }
            // Non-unique single-column index on Region.
            { SsKey = idxKey ["IndexedWidget"; "IX"; "Region"]
              Name = mkName "IX_IndexedWidget_Region"
              Columns = IRBuilders.mkIndexColumns [ regionKey ]
              IsUnique = false; IsPrimaryKey = false; ExtendedProperties = []; Filter = None; IncludedColumns = []; IsPlatformAuto = false; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None }
        ]
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }
    let m : Module = {
        SsKey = modKey "Inventory"
        Name  = mkName "Inventory"
        Kinds = [ widget ]
        IsActive = true
        ExtendedProperties = []
        }
    { Modules = [ m ]; Sequences = [] }

[<Fact>]
let ``Slice γ: Indexes round-trip through DacFx model`` () =
    let enriched = enrich indexedCatalog
    let bytes = DacpacEmitter.emit enriched |> mustOkBytes
    use stream = new MemoryStream(bytes)
    use model = TSqlModel.LoadFromDacpac(stream, ModelLoadOptions())
    let dacpacIndexes =
        model.GetObjects(DacQueryScopes.UserDefined, Index.TypeClass)
        |> Seq.toList
    let catalogIndexCount =
        Catalog.allKinds enriched
        |> List.sumBy (fun k -> List.length k.Indexes)
    Assert.Equal (catalogIndexCount, List.length dacpacIndexes)
    // Sanity: the unique-vs-non-unique distinction is preserved. DacFx
    // exposes `Index.IsUnique` per `Microsoft.SqlServer.Dac.Model.Index`.
    let uniqueDacpac =
        dacpacIndexes
        |> List.filter (fun obj -> obj.GetProperty<bool>(Index.Unique))
        |> List.length
    let uniqueCatalog =
        Catalog.allKinds enriched
        |> List.collect (fun k -> k.Indexes)
        |> List.filter (fun i -> i.IsUnique)
        |> List.length
    Assert.Equal (uniqueCatalog, uniqueDacpac)
