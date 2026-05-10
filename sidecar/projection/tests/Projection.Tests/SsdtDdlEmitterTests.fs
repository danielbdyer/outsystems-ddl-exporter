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

// ---------------------------------------------------------------------------
// Chapter 4.1.A slice 3 — indexes (single-column non-unique + unique).
//
// Per chapter pre-scope §8 slice 3: "CREATE INDEX [name] ON [Schema].[Table]
// ([col]) per non-PK index, sorted by SsKey. Fixture: one kind with two
// indexes (one unique, one not). Acceptance: indexes appear in alphabetical-
// by-SsKey order; PK-marked indexes are skipped."
//
// Statement DU extended with `CreateIndex of IndexDef`; ScriptDomBuild
// gains `buildCreateIndex`; SsdtDdlEmitter gains `indexStatements` helper.
// Per V2-driver KPI: schema axis verification depth high; the CREATE INDEX
// statements share the same byte-determinism contract as CREATE TABLE
// (ScriptDom pinned-options writer).
// ---------------------------------------------------------------------------

let private indexedKindKey = kindKey ["Indexed"]
let private indexedNonUniqueIdxKey = idxKey ["Indexed"; "NonUnique"]
let private indexedUniqueIdxKey = idxKey ["Indexed"; "Unique"]
let private indexedPkIdxKey = idxKey ["Indexed"; "PK"]

let private indexedKind : Kind =
    let attr (label: string) (typ: PrimitiveType) (isPk: bool) : Attribute =
        {
            SsKey = attrKey ["Indexed"; label]
            Name = mkName label
            Type = typ
            Column = { ColumnName = label.ToUpperInvariant(); IsNullable = not isPk }
            IsPrimaryKey = isPk
            IsMandatory  = isPk
            Length = None; Precision = None; Scale = None; IsIdentity = false
        }
    {
        SsKey = indexedKindKey
        Name = mkName "Indexed"
        Origin = OsNative
        Modality = []
        Physical = { Schema = "dbo"; Table = "OSUSR_X_INDEXED" }
        Attributes = [
            attr "Id" Integer true
            attr "Lookup" Text false
            attr "Code" Text false
        ]
        References = []
        Indexes = [
            // Non-unique index on Lookup column.
            {
                SsKey = indexedNonUniqueIdxKey
                Name = mkName "IX_OSUSR_X_INDEXED_LOOKUP"
                Columns = [ attrKey ["Indexed"; "Lookup"] ]
                IsUnique = false
                IsPrimaryKey = false
            }
            // Unique index on Code column.
            {
                SsKey = indexedUniqueIdxKey
                Name = mkName "UIX_OSUSR_X_INDEXED_CODE"
                Columns = [ attrKey ["Indexed"; "Code"] ]
                IsUnique = true
                IsPrimaryKey = false
            }
            // PK index — should be SKIPPED by the emitter (PK is inlined
            // in CREATE TABLE per V1 convention).
            {
                SsKey = indexedPkIdxKey
                Name = mkName "PK_OSUSR_X_INDEXED"
                Columns = [ attrKey ["Indexed"; "Id"] ]
                IsUnique = true
                IsPrimaryKey = true
            }
        ]
    }

let private indexedCatalog : Catalog =
    {
        Modules = [
            {
                SsKey = modKey "IndexedModule"
                Name = mkName "IndexedModule"
                Kinds = [ indexedKind ]
            }
        ]
    }

[<Fact>]
let ``Slice 3: SsdtDdlEmitter emits CREATE INDEX for non-PK indexes`` () =
    // Per chapter pre-scope §8 slice 3: non-PK indexes emit as
    // CREATE INDEX statements after the CREATE TABLE; PK-marked
    // indexes are skipped (PK is inlined in CREATE TABLE).
    let enriched = enrich indexedCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let body =
        match Map.tryFind indexedKindKey (ArtifactByKind.toMap artifact) with
        | Some f -> f.Body
        | None ->
            Assert.Fail "expected slice for indexedKind"
            ""
    // Non-unique index name appears in body.
    Assert.Contains ("IX_OSUSR_X_INDEXED_LOOKUP", body)
    // Unique index name appears in body; UNIQUE keyword is present.
    Assert.Contains ("UIX_OSUSR_X_INDEXED_CODE", body)
    Assert.Contains ("UNIQUE", body)
    // PK-marked index is skipped — its name does NOT appear as a
    // CREATE INDEX statement (it's inlined in CREATE TABLE; the
    // V1 convention keeps PK inlined when single-column).
    Assert.False (body.Contains "CREATE.*INDEX.*PK_OSUSR_X_INDEXED",
                  sprintf "PK index should be skipped (not emitted as CREATE INDEX); got body: %s" body)

[<Fact>]
let ``Slice 3: SsdtDdlEmitter index emission is sorted by SsKey for determinism`` () =
    // A33 (deterministic-ordered schema emission). Per chapter pre-scope
    // §8 slice 3 acceptance: "indexes appear in alphabetical-by-SsKey
    // order." The non-unique IX_*_LOOKUP and unique UIX_*_CODE indexes
    // emit in SsKey order: idxKey ["Indexed";"NonUnique"] vs
    // idxKey ["Indexed";"Unique"] — by string SsKey root, NonUnique
    // sorts before Unique alphabetically.
    let enriched = enrich indexedCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let body =
        match Map.tryFind indexedKindKey (ArtifactByKind.toMap artifact) with
        | Some f -> f.Body
        | None ->
            Assert.Fail "expected slice for indexedKind"
            ""
    let nonUniqueIdx = body.IndexOf "IX_OSUSR_X_INDEXED_LOOKUP"
    let uniqueIdx = body.IndexOf "UIX_OSUSR_X_INDEXED_CODE"
    Assert.True (nonUniqueIdx > 0, "expected IX_*_LOOKUP in body")
    Assert.True (uniqueIdx > 0, "expected UIX_*_CODE in body")
    // Non-unique (idxKey ["Indexed";"NonUnique"]) sorts before
    // unique (idxKey ["Indexed";"Unique"]) by SsKey string-ordering.
    Assert.True (nonUniqueIdx < uniqueIdx,
                 sprintf "expected NonUnique idx before Unique idx; NonUnique@%d, Unique@%d" nonUniqueIdx uniqueIdx)

[<Fact>]
let ``Slice 3: T1 byte-determinism holds for indexed fixture`` () =
    let enriched = enrich indexedCatalog
    let r1 = SsdtDdlEmitter.emitSlices enriched |> mustOk |> ArtifactByKind.toMap
    let r2 = SsdtDdlEmitter.emitSlices enriched |> mustOk |> ArtifactByKind.toMap
    let body1 : SsdtDdlEmitter.SsdtFile = Map.find indexedKindKey r1
    let body2 : SsdtDdlEmitter.SsdtFile = Map.find indexedKindKey r2
    Assert.Equal<string> (body1.Body, body2.Body)

// ---------------------------------------------------------------------------
// Chapter 4.1.A slice 5 — intra-module foreign keys (inline).
//
// Per chapter pre-scope §8 slice 5: "same-module FK constraints inline in
// the CREATE TABLE body. Fixture: two kinds in one module, one with an FK
// to the other. Acceptance: inline FK appears in the owning kind's file;
// target kind's file is unchanged."
//
// SsdtDdlEmitter.fkDef (slice 5) resolves a Reference to a ForeignKeyDef
// with V1 naming convention (`FK_<OwnerTable>_<TargetTable>_<SourceColumn>`
// per V1 ForeignKeyNameFactory.cs:17-60). The chapter-3.5 ScriptDomBuild
// .buildCreateTable already handles the inline FK constraint emission;
// slice 5 wires V2's References through to that path.
// ---------------------------------------------------------------------------

let private parentKindKey = kindKey ["Parent"]
let private childKindKey = kindKey ["Child"]
let private childParentFkKey = refKey ["Child"; "Parent"]
let private childParentIdAttrKey = attrKey ["Child"; "ParentId"]

let private parentKind : Kind =
    {
        SsKey = parentKindKey
        Name = mkName "Parent"
        Origin = OsNative
        Modality = []
        Physical = { Schema = "dbo"; Table = "OSUSR_X_PARENT" }
        Attributes = [
            {
                SsKey = attrKey ["Parent"; "Id"]
                Name = mkName "Id"
                Type = Integer
                Column = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true
                Length = None; Precision = None; Scale = None; IsIdentity = false
            }
        ]
        References = []
        Indexes = []
    }

let private childKind : Kind =
    {
        SsKey = childKindKey
        Name = mkName "Child"
        Origin = OsNative
        Modality = []
        Physical = { Schema = "dbo"; Table = "OSUSR_X_CHILD" }
        Attributes = [
            {
                SsKey = attrKey ["Child"; "Id"]
                Name = mkName "Id"
                Type = Integer
                Column = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true
                Length = None; Precision = None; Scale = None; IsIdentity = false
            }
            {
                SsKey = childParentIdAttrKey
                Name = mkName "ParentId"
                Type = Integer
                Column = { ColumnName = "PARENT_ID"; IsNullable = false }
                IsPrimaryKey = false
                IsMandatory  = true
                Length = None; Precision = None; Scale = None; IsIdentity = false
            }
        ]
        References = [
            {
                SsKey = childParentFkKey
                Name = mkName "ParentFk"
                SourceAttribute = childParentIdAttrKey
                TargetKind = parentKindKey
                OnDelete = NoAction
            }
        ]
        Indexes = []
    }

let private fkCatalog : Catalog =
    {
        Modules = [
            {
                SsKey = modKey "FkModule"
                Name = mkName "FkModule"
                Kinds = [ parentKind; childKind ]
            }
        ]
    }

[<Fact>]
let ``Slice 5: SsdtDdlEmitter emits inline FK constraint in owning kind's file`` () =
    let enriched = enrich fkCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let slices = ArtifactByKind.toMap artifact
    // Child kind's body contains the FK constraint (inline in CREATE TABLE
    // per V1 convention + chapter pre-scope §3 / §4).
    let childBody =
        match Map.tryFind childKindKey slices with
        | Some f -> f.Body
        | None -> Assert.Fail "expected slice for childKind"; ""
    Assert.Contains ("FOREIGN KEY", childBody)
    // V1 FK naming convention: `FK_<OwnerTable>_<TargetTable>_<SourceColumn>`.
    Assert.Contains ("FK_OSUSR_X_CHILD_OSUSR_X_PARENT_PARENT_ID", childBody)
    // Target column name (Parent.ID) appears in the REFERENCES clause.
    Assert.Contains ("REFERENCES", childBody)
    Assert.Contains ("[OSUSR_X_PARENT]", childBody)

[<Fact>]
let ``Slice 5: SsdtDdlEmitter — target kind's file is unchanged by the FK`` () =
    // Per chapter pre-scope §8 slice 5 acceptance: "target kind's file
    // is unchanged." The Parent kind has no References, so its body
    // contains a CREATE TABLE without FK clauses.
    let enriched = enrich fkCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let parentBody =
        match Map.tryFind parentKindKey (ArtifactByKind.toMap artifact) with
        | Some f -> f.Body
        | None -> Assert.Fail "expected slice for parentKind"; ""
    Assert.DoesNotContain ("FOREIGN KEY", parentBody)
    Assert.DoesNotContain ("REFERENCES", parentBody)
    // Parent kind still has its CREATE TABLE.
    Assert.Contains ("CREATE TABLE", parentBody)
    Assert.Contains ("[OSUSR_X_PARENT]", parentBody)

[<Fact>]
let ``Slice 5: T1 byte-determinism holds for FK fixture`` () =
    let enriched = enrich fkCatalog
    let r1 = SsdtDdlEmitter.emitSlices enriched |> mustOk |> ArtifactByKind.toMap
    let r2 = SsdtDdlEmitter.emitSlices enriched |> mustOk |> ArtifactByKind.toMap
    let childBody1 : SsdtDdlEmitter.SsdtFile = Map.find childKindKey r1
    let childBody2 : SsdtDdlEmitter.SsdtFile = Map.find childKindKey r2
    Assert.Equal<string> (childBody1.Body, childBody2.Body)

// ---------------------------------------------------------------------------
// Chapter 4.1.A slice 10 — SsdtBundle.compose composition.
//
// V0 composition (per chapter pre-scope §8 slice 10 + V2-driver KPI smart-
// product-choices): SsdtFile per-kind .sql files + manifest.json into one
// Map<RelativePath, string>. RefactorLog conditional integration + post-
// deploy split defer to follow-on slices when chapter 3.5 cross-version
// diff threading + Tolerance taxonomy (M4) land.
//
// Chapter 4.1.A close substantively follows this slice — slices 1+2+3+4+5+9
// + 10 cover the in-flight surface; slices 6+7+8 reopen when chapter 3.2
// SnapshotRowsets surfaces the IR widening they gate on.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice 10: SsdtBundle.compose produces N+1 entries (N SsdtFile per kind + 1 manifest)`` () =
    let enriched = enrich sampleCatalog
    let ssdtFiles = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let manifest = ManifestEmitter.emit enriched
    let bundle = SsdtBundle.compose ssdtFiles manifest
    let allKinds = Catalog.allKinds enriched
    // N SsdtFile entries + 1 manifest.json entry.
    Assert.Equal (List.length allKinds + 1, Map.count bundle)

[<Fact>]
let ``Slice 10: SsdtBundle.compose bundle contains manifest.json at directory root`` () =
    let enriched = enrich sampleCatalog
    let ssdtFiles = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let manifest = ManifestEmitter.emit enriched
    let bundle = SsdtBundle.compose ssdtFiles manifest
    Assert.True (Map.containsKey "manifest.json" bundle)
    let manifestBody = Map.find "manifest.json" bundle
    Assert.Contains ("Projection.Targets.SSDT.ManifestEmitter", manifestBody)

[<Fact>]
let ``Slice 10: SsdtBundle.compose bundle entries match SsdtFile per-kind RelativePath`` () =
    let enriched = enrich sampleCatalog
    let ssdtFiles = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let manifest = ManifestEmitter.emit enriched
    let bundle = SsdtBundle.compose ssdtFiles manifest
    for KeyValue(_, file) in ArtifactByKind.toMap ssdtFiles do
        Assert.True (Map.containsKey file.RelativePath bundle, sprintf "expected %s in bundle" file.RelativePath)
        let bundleBody = Map.find file.RelativePath bundle
        Assert.Equal<string> (file.Body, bundleBody)

[<Fact>]
let ``Slice 10: T1 byte-determinism holds for the composed bundle`` () =
    let enriched = enrich sampleCatalog
    let ssdtFiles1 = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let manifest1 = ManifestEmitter.emit enriched
    let bundle1 = SsdtBundle.compose ssdtFiles1 manifest1
    let ssdtFiles2 = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let manifest2 = ManifestEmitter.emit enriched
    let bundle2 = SsdtBundle.compose ssdtFiles2 manifest2
    Assert.Equal (Map.count bundle1, Map.count bundle2)
    for KeyValue(path, body1) in bundle1 do
        let body2 = Map.find path bundle2
        Assert.Equal<string> (body1, body2)
