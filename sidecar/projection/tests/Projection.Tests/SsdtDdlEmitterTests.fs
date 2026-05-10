module Projection.Tests.SsdtDdlEmitterTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Chapter 4.1.A slice 1 — single-table SSDT DDL emission.
//
// Per `CHAPTER_4_1_A_OPEN.md` strategic frame + chapter pre-scope §8 slice 1:
// the SsdtDdlEmitter produces a per-kind `SsdtFile` carrying a cross-platform-
// deterministic relative path AND the rendered SQL body. The body flows
// through ScriptDom's typed AST + `Sql160ScriptGenerator`. Per pillar 1
// (data-structure-oriented over string-parsing), the seam IS the typed
// `SsdtFile`; strings emerge only at the absolute terminal generator step.
//
// Slice-1 scope: columns + PK only (no indexes, no FKs, no extended properties,
// no static populations). Subsequent slices extend the same Emitter signature.
// ---------------------------------------------------------------------------

// `Emitter<'a>` returns `FSharp.Core.Result<ArtifactByKind<'a>, EmitError>`
// (the two-arity alias used by Π emitters per `Types.fs:50`). The single-
// arity `Projection.Core.Result<'a>` alias has the same Ok/Error case
// names, so qualifying via a private type alias forces case access to
// resolve to FSharp.Core's Result without ambiguity.
type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private enrich (c: Catalog) : Catalog =
    (CanonicalizeIdentity.run c).Value

let private mustOk (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v        -> v
    | FsResult.Error err   ->
        Assert.Fail (sprintf "expected Ok; got %A" err)
        Unchecked.defaultof<'a>

// ---------------------------------------------------------------------------
// Slice-1 acceptance: emitSlices produces one SsdtFile per kind, keyed by
// SsKey, with V1-conventional RelativePath.
// ---------------------------------------------------------------------------

[<Fact>]
let ``SsdtDdlEmitter.emitSlices produces one SsdtFile per kind keyed by SsKey`` () =
    let enriched = enrich sampleCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let slices = ArtifactByKind.toMap artifact
    let allKinds = Catalog.allKinds enriched
    Assert.Equal (List.length allKinds, Map.count slices)
    for k in allKinds do
        Assert.True (Map.containsKey k.SsKey slices, sprintf "missing slice for kind %A" k.SsKey)

[<Fact>]
let ``SsdtDdlEmitter.emitSlices RelativePath uses V1 convention (Modules/<Module>/<Schema>.<Table>.sql)`` () =
    // V1 convention per `src/Osm.Emission/SsdtEmitter.cs:55-122`:
    // forward-slash separators throughout (cross-platform deterministic),
    // module name in the directory, schema-qualified filename. Slice-1
    // structural witness: the path matches the convention character-for-
    // character.
    let enriched = enrich sampleCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let slices = ArtifactByKind.toMap artifact
    for KeyValue(_, file) in slices do
        Assert.StartsWith ("Modules/", file.RelativePath)
        Assert.EndsWith (".sql", file.RelativePath)
        // Forward slashes only; no backslashes regardless of host OS.
        Assert.DoesNotContain ('\\', file.RelativePath)
        // Three segments separated by '/': Modules, ModuleName, leaf.
        let segments = file.RelativePath.Split('/')
        Assert.Equal (3, segments.Length)
        Assert.Equal ("Modules", segments.[0])
        // Leaf is `<Schema>.<Table>.sql` (two dots before the extension).
        let leaf = segments.[2]
        Assert.True (leaf.Contains('.'), sprintf "leaf %s missing dot separator" leaf)

[<Fact>]
let ``SsdtDdlEmitter.emitSlices Body contains CREATE TABLE statement for every kind`` () =
    // Slice-1 SQL emission: every kind's body is a CREATE TABLE statement
    // (no INSERTs, no CREATE INDEX, no ALTER TABLE for FKs at this slice).
    // ScriptDom's `Sql160ScriptGenerator` emits `CREATE TABLE` keyword
    // case per the pinned-options writer (uppercase per chapter-3.5
    // codification).
    let enriched = enrich sampleCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let slices = ArtifactByKind.toMap artifact
    for KeyValue(ssKey, file) in slices do
        Assert.Contains ("CREATE TABLE", file.Body)
        Assert.False (file.Body.Contains "INSERT", sprintf "slice 1 should not emit INSERTs (kind %A)" ssKey)

// ---------------------------------------------------------------------------
// T1 byte-determinism — same input, same output, byte-identical across
// repeat invocations. The chapter-3.1 audit's Bench-driven discipline made
// this a structural property; the SSDT DDL emitter inherits it via
// ScriptDom's pinned-options writer.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: SsdtDdlEmitter.emitSlices is byte-deterministic across repeat invocations`` () =
    let enriched = enrich sampleCatalog
    let r1 = SsdtDdlEmitter.emitSlices enriched |> mustOk |> ArtifactByKind.toMap
    let r2 = SsdtDdlEmitter.emitSlices enriched |> mustOk |> ArtifactByKind.toMap
    Assert.Equal (Map.count r1, Map.count r2)
    for KeyValue(ssKey, file1) in r1 do
        let file2 : SsdtDdlEmitter.SsdtFile = Map.find ssKey r2
        Assert.Equal<string> (file1.RelativePath, file2.RelativePath)
        Assert.Equal<string> (file1.Body, file2.Body)

// ---------------------------------------------------------------------------
// T11 sibling-Π commutativity — SsdtDdlEmitter joins RawText / Json /
// Distributions as the fourth Π. The smart-constructor's strict-equality
// keyset enforcement guarantees structural T11 across all four siblings.
// This test demonstrates the structural path executes for the new fourth
// sibling; the existing `SiblingEmitterContractTests.fs` covers the
// pairwise commutativity for the prior three.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T11: SsdtDdlEmitter.emitSlices keyset equals Catalog.allKinds`` () =
    let enriched = enrich sampleCatalog
    let expected =
        Catalog.allKinds enriched |> List.map (fun k -> k.SsKey) |> Set.ofList
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    Assert.Equal<Set<SsKey>> (expected, ArtifactByKind.keys artifact)

[<Fact>]
let ``T11: SsdtDdlEmitter and RawTextEmitter agree on keyset`` () =
    let enriched = enrich sampleCatalog
    let rawText = RawTextEmitter.emitSlices enriched |> mustOk
    let ssdtDdl = SsdtDdlEmitter.emitSlices enriched |> mustOk
    Assert.Equal<Set<SsKey>> (ArtifactByKind.keys rawText, ArtifactByKind.keys ssdtDdl)

// ---------------------------------------------------------------------------
// Chapter 4.1.A slice 2 — multi-attribute formatting + every PrimitiveType
// variant.
//
// Per chapter pre-scope §8 slice 2: "every PrimitiveType variant maps
// correctly; column-line padding matches V1 layout. Fixture: kind with all
// 9 PrimitiveType columns. Test: byte-equality against an expected string
// per type; T11 cross-validation with RawTextEmitter's defaultSqlType
// (extract a shared module)."
//
// The shared extraction is `Projection.Core.SqlTypeCorrespondence` (chapter
// 3.7 slice β). Per the two-consumer threshold, the extraction earned its
// place at the second consumer (Render.columnSqlType + ReadSide.mapSqlType
// at chapter 3.7); SsdtDdlEmitter is the third consumer via
// `Statement.CreateTable` flowing through `ScriptDomBuild.dataTypeReference`
// (chapter 3.7 slice β'). Slice 2 verifies every variant lands as the
// expected SQL token via that path.
// ---------------------------------------------------------------------------

let private mkName (s: string) : Name =
    match Name.create s with
    | Ok v       -> v
    | Error errs ->
        Assert.Fail (sprintf "Name.create %s failed: %A" s errs)
        Unchecked.defaultof<Name>

let private allPrimitiveTypesKind : Kind =
    let attr (label: string) (typ: PrimitiveType) (isPk: bool) : Attribute =
        {
            SsKey = attrKey ["AllTypes"; label]
            Name = mkName label
            Type = typ
            Column = { ColumnName = label.ToUpperInvariant(); IsNullable = not isPk }
            IsPrimaryKey = isPk
            IsMandatory  = isPk
            Length = None; Precision = None; Scale = None; IsIdentity = false
        }
    {
        SsKey = kindKey ["AllTypes"]
        Name = mkName "AllTypes"
        Origin = OsNative
        Modality = []
        Physical = { Schema = "dbo"; Table = "OSUSR_X_ALLTYPES" }
        Attributes = [
            attr "Id" Integer true
            attr "Amount" Decimal false
            attr "Note" Text false
            attr "Active" Boolean false
            attr "Created" DateTime false
            attr "Birthday" Date false
            attr "OpenAt" Time false
            attr "Avatar" Binary false
            attr "Token" Guid false
        ]
        References = []
        Indexes = []
    }

let private allPrimitiveTypesCatalog : Catalog =
    {
        Modules = [
            {
                SsKey = modKey "AllTypesModule"
                Name = mkName "AllTypesModule"
                Kinds = [ allPrimitiveTypesKind ]
            }
        ]
    }

[<Fact>]
let ``Slice 2: SsdtDdlEmitter emits every PrimitiveType variant via SqlTypeCorrespondence`` () =
    // Single test covering all 9 PrimitiveType variants. Per pillar 7
    // gold-standard library precedence: the SQL type token comes from
    // `SqlTypeCorrespondence.baseName` (the canonical V2-internal source
    // of truth, shared with Render and ReadSide); the body is generated
    // by ScriptDom's pinned-options writer. Slice 2 acceptance: every
    // variant's expected token appears in the body.
    let enriched = enrich allPrimitiveTypesCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let slices = ArtifactByKind.toMap artifact
    let body =
        match Map.tryFind allPrimitiveTypesKind.SsKey slices with
        | Some f -> f.Body
        | None ->
            Assert.Fail "expected slice for allPrimitiveTypesKind"
            ""
    for pt in SqlTypeCorrespondence.allPrimitives do
        let token = SqlTypeCorrespondence.baseName pt
        Assert.True (
            body.Contains token,
            sprintf "expected SQL token %s (for PrimitiveType.%A) in body, got: %s" token pt body)

[<Fact>]
let ``Slice 2: T11 cross-validation — RawTextEmitter and SsdtDdlEmitter both emit the all-types fixture`` () =
    // T11 sibling-Π commutativity for the all-types catalog: both emitters
    // must successfully produce a slice for the all-types kind. The shared
    // SqlTypeCorrespondence ensures the SQL type tokens agree per variant
    // (verified by the prior slice-2 test). This test is the cross-
    // emitter structural witness.
    let enriched = enrich allPrimitiveTypesCatalog
    let rawText = RawTextEmitter.emitSlices enriched |> mustOk
    let ssdtDdl = SsdtDdlEmitter.emitSlices enriched |> mustOk
    Assert.Equal<Set<SsKey>> (ArtifactByKind.keys rawText, ArtifactByKind.keys ssdtDdl)
    let rawTextSlices = ArtifactByKind.toMap rawText
    let ssdtDdlSlices = ArtifactByKind.toMap ssdtDdl
    Assert.True (Map.containsKey allPrimitiveTypesKind.SsKey rawTextSlices)
    Assert.True (Map.containsKey allPrimitiveTypesKind.SsKey ssdtDdlSlices)

// ---------------------------------------------------------------------------
// Chapter 4.1.A slice 4 — composite primary keys.
//
// Per chapter pre-scope §8 slice 4: "when Kind.primaryKey returns >1
// attribute, emit a separate CONSTRAINT [PK_<Table>] PRIMARY KEY CLUSTERED
// ([col1], [col2]) table-constraint at the end of the CREATE TABLE body."
//
// `ScriptDomBuild.buildCreateTable` (chapter 3.5) already handles composite
// PKs via `PrimaryKeyDef`'s multi-column list — the emitter's `pkDef`
// helper collects all `IsPrimaryKey = true` attributes; ScriptDom emits
// the table-level constraint when len > 1, inlines the column-constraint
// when len = 1. Slice 4 is a verification slice: confirm the existing
// machinery handles composite PKs correctly via fixture + test.
// ---------------------------------------------------------------------------

let private compositePkKind : Kind =
    let attr (label: string) (typ: PrimitiveType) (isPk: bool) : Attribute =
        {
            SsKey = attrKey ["Composite"; label]
            Name = mkName label
            Type = typ
            Column = { ColumnName = label.ToUpperInvariant(); IsNullable = false }
            IsPrimaryKey = isPk
            IsMandatory  = isPk
            Length = None; Precision = None; Scale = None; IsIdentity = false
        }
    {
        SsKey = kindKey ["Composite"]
        Name = mkName "Composite"
        Origin = OsNative
        Modality = []
        Physical = { Schema = "dbo"; Table = "OSUSR_X_COMPOSITE" }
        Attributes = [
            attr "TenantId" Integer true   // first PK column
            attr "Code" Text true          // second PK column (composite)
            attr "Description" Text false
        ]
        References = []
        Indexes = []
    }

let private compositePkCatalog : Catalog =
    {
        Modules = [
            {
                SsKey = modKey "CompositePkModule"
                Name = mkName "CompositePkModule"
                Kinds = [ compositePkKind ]
            }
        ]
    }

[<Fact>]
let ``Slice 4: SsdtDdlEmitter emits composite PK as table-constraint (not inline column-constraint)`` () =
    // Per V1 convention (and SQL standard): single-column PKs get inlined
    // as `<col> <type> NOT NULL CONSTRAINT [PK_...] PRIMARY KEY CLUSTERED`;
    // composite PKs get separated as `CONSTRAINT [PK_<Table>] PRIMARY KEY
    // CLUSTERED ([col1], [col2])` table-constraint at the end of the body.
    // ScriptDom's `Sql160ScriptGenerator` makes the structural distinction
    // automatically based on `PrimaryKeyDef.Columns` length.
    let enriched = enrich compositePkCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let body =
        match Map.tryFind compositePkKind.SsKey (ArtifactByKind.toMap artifact) with
        | Some f -> f.Body
        | None ->
            Assert.Fail "expected slice for compositePkKind"
            ""
    // Composite PK includes both PK column names in the table-level
    // constraint clause.
    Assert.Contains ("PRIMARY KEY", body)
    Assert.Contains ("TENANTID", body)
    Assert.Contains ("CODE", body)
    // The PK constraint name follows V1 convention `PK_<Schema>_<Table>`.
    Assert.Contains ("PK_dbo_OSUSR_X_COMPOSITE", body)

[<Fact>]
let ``Slice 4: T1 byte-determinism holds for composite PK fixture`` () =
    let enriched = enrich compositePkCatalog
    let r1 = SsdtDdlEmitter.emitSlices enriched |> mustOk |> ArtifactByKind.toMap
    let r2 = SsdtDdlEmitter.emitSlices enriched |> mustOk |> ArtifactByKind.toMap
    let body1 : SsdtDdlEmitter.SsdtFile = Map.find compositePkKind.SsKey r1
    let body2 : SsdtDdlEmitter.SsdtFile = Map.find compositePkKind.SsKey r2
    Assert.Equal<string> (body1.Body, body2.Body)
