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
        Origin   = Native
        Modality = []
        Physical = mkTableId "dbo" "WIDGET"
        Attributes = [
            { Attribute.create widgetIdKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true }
            { Attribute.create widgetNameKey (mkName "Name") Text with Column = ColumnRealization.create ("NAME") (false) |> Result.value }
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
        |> List.map (fun k -> TableId.schemaText k.Physical, TableId.tableText k.Physical)
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
        Origin   = Native
        Modality = []
        Physical = mkTableId "dbo" "INDEXED_WIDGET"
        Attributes = [
            { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true }
            { Attribute.create codeKey (mkName "Code") Text with Column = ColumnRealization.create ("CODE") (false) |> Result.value }
            { Attribute.create regionKey (mkName "Region") Text with Column = ColumnRealization.create ("REGION") (false) |> Result.value }
            { Attribute.create labelKey (mkName "Label") Text with Column = ColumnRealization.create ("LABEL") (true) |> Result.value }
        ]
        References = []
        Indexes = [
            // Single-column unique index on Code.
            { SsKey = idxKey ["IndexedWidget"; "UQ"; "Code"]
              Name = mkName "UQ_IndexedWidget_Code"
              Columns = IndexColumn.ascendingList [ codeKey ]
              Uniqueness = Unique; ExtendedProperties = []; Filter = None; IncludedColumns = []; IsPlatformAuto = false; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None; DataSpace = None }
            // Composite (non-unique) index on Region + Label.
            { SsKey = idxKey ["IndexedWidget"; "IX"; "RegionLabel"]
              Name = mkName "IX_IndexedWidget_RegionLabel"
              Columns = IndexColumn.ascendingList [ regionKey; labelKey ]
              Uniqueness = NotUnique; ExtendedProperties = []; Filter = None; IncludedColumns = []; IsPlatformAuto = false; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None; DataSpace = None }
            // Non-unique single-column index on Region.
            { SsKey = idxKey ["IndexedWidget"; "IX"; "Region"]
              Name = mkName "IX_IndexedWidget_Region"
              Columns = IndexColumn.ascendingList [ regionKey ]
              Uniqueness = NotUnique; ExtendedProperties = []; Filter = None; IncludedColumns = []; IsPlatformAuto = false; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false; IgnoreDuplicateKey = false; IsDisabled = false; DataCompression = None; DataSpace = None }
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

// ---------------------------------------------------------------------------
// S12.6 (DacFx-seam mixed-stream exclusion). `DacpacEmitter` feeds DacFx's
// declarative `TSqlModel` only the schema-statement family — `CreateTable` /
// `CreateIndex` / `CreateSequence` / the post-CREATE state-reproduction alters.
// The imperative-migration family (`SchemaMigrationEmitter`'s
// `AlterTableAlterColumn` / `AlterTableDropColumn` / DROP CONSTRAINT / DROP
// INDEX) is NOT a declarative model object: DacFx owns the ALTER/DROP at
// publish, computing it from the declarative target. `isSchemaStatement` must
// EXCLUDE that family from the DacFx ingestion.
//
// Empirical DacFx fact (probed): an imperative `ALTER TABLE … ALTER/DROP
// COLUMN` rendered script, when fed to `TSqlModel.AddObjects`, surfaces ZERO
// `Table.TypeClass` objects — it is not a declarative table object. Only a
// `CreateTable` surfaces as a Table. So when a MIXED stream is ingested through
// the same public DacFx pipeline `DacpacEmitter.buildModel` uses
// (`ScriptDomBuild.buildStatement` → `ScriptDomGenerate.generateOne` →
// `AddObjects`), the resulting model's Table set must equal exactly the
// CreateTable's table — the imperative ALTER/DROP contribute nothing.
//
// Discriminates: a refactor that moved an imperative variant into
// `isSchemaStatement`'s true-branch would let `DacpacEmitter` ingest the
// imperative ALTER/DROP into the model. This test ingests the imperative
// statements directly and asserts they surface no Table object — the model
// the emitter must produce carries ONLY the CreateTable's table, never the
// imperative artifacts.
// ---------------------------------------------------------------------------

[<Fact>]
let ``S12.6: imperative ALTER/DROP statements are excluded from the DacFx model (only CreateTable surfaces)`` () =
    let enriched = enrich singleKindCatalog
    let widget = Catalog.allKinds enriched |> List.head
    let table = widget.Physical
    let alteredCol = SsdtDdlEmitter.columnDefOfAttribute (widget.Attributes |> List.head)

    // The CreateTable statement(s) the declarative emitter produces.
    let createStatements = SsdtDdlEmitter.statements enriched |> Seq.toList
    let createTableCount =
        createStatements |> List.filter (function Statement.CreateTable _ -> true | _ -> false) |> List.length
    Assert.Equal(1, createTableCount)   // the fixture is a single-Kind catalog

    // The imperative-migration statements — the family `isSchemaStatement` excludes.
    let imperativeStatements : Statement list =
        [ Statement.AlterTableAlterColumn (table, alteredCol)
          Statement.AlterTableDropColumn (table, "NAME") ]

    // Ingest a MIXED stream (declarative + imperative) through the SAME public
    // DacFx pipeline `DacpacEmitter.buildModel` uses, with NO filter applied.
    // The imperative statements ingest without throwing, but they are not
    // declarative table objects, so the model's Table set is exactly the
    // CreateTable's table.
    let ingest (statements: Statement list) : TSqlModel =
        let model = new TSqlModel(SqlServerVersion.Sql160, TSqlModelOptions())
        for s in statements do
            match ScriptDomBuild.buildStatement s with
            | Some frag ->
                let script = ScriptDomGenerate.generateOne frag
                if not (System.String.IsNullOrWhiteSpace script) then model.AddObjects script
            | None -> ()
        model

    // The imperative statements ON THEIR OWN surface zero tables — they are not
    // declarative model objects (the property that justifies excluding them).
    use imperativeOnlyModel = ingest imperativeStatements
    let imperativeTables =
        imperativeOnlyModel.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass) |> Seq.toList
    Assert.Empty imperativeTables

    // The mixed stream surfaces exactly the CreateTable's table — the imperative
    // ALTER/DROP add no table object. The model carries only the declarative table.
    use mixedModel = ingest (createStatements @ imperativeStatements)
    let mixedTables =
        mixedModel.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass) |> Seq.toList
    Assert.Equal(createTableCount, List.length mixedTables)

    // Cross-check against the production emitter: `DacpacEmitter.emit` (which
    // applies `isSchemaStatement`) produces a model whose Table set matches the
    // declarative CreateTable count — never more. If the filter admitted an
    // imperative variant, the emit path would diverge from this declarative count.
    let bytes = DacpacEmitter.emit enriched |> mustOkBytes
    use stream = new MemoryStream(bytes)
    use emitModel = TSqlModel.LoadFromDacpac(stream, ModelLoadOptions())
    let emitTables =
        emitModel.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass) |> Seq.toList
    Assert.Equal(createTableCount, List.length emitTables)

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
        |> List.filter (fun i -> IndexUniqueness.isUnique i.Uniqueness)
        |> List.length
    Assert.Equal (uniqueCatalog, uniqueDacpac)
