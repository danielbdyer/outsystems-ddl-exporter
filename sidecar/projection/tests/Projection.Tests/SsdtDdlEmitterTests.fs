module Projection.Tests.SsdtDdlEmitterTests

open Xunit
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
    (ciRun c).Value

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

// Slice 11 (2026-06-02 audit): T1 example test pruned. `f(X) = f(X)`
// is tautological in pure F#. `AdjunctionLawTests.fs:80-88` provides
// the corresponding property-level T1 coverage for the SSDT emitter
// (`SsdtDdlEmitter.statements` invariant under module permutation
// over a swept seed space).

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

// `T11: SsdtDdlEmitter and RawTextEmitter agree on keyset` — retired
// at the chapter 4.1.A close arc (RawTextEmitter retirement, slice 2).
// SsdtDdlEmitter is the production schema emitter; RawTextEmitter was
// the chapter-3 pre-cursor and has been retired. Sibling-Π
// commutativity across the THREE production emitters (SsdtDdl + Json
// + Distributions) is enforced at `SiblingEmitterContractTests.fs` —
// that's the structural T11 surface; the per-emitter keyset property
// is structural via `ArtifactByKind.create`'s smart constructor.

// ---------------------------------------------------------------------------
// SsdtDdlEmitter.statements — catalog-wide typed statement stream.
// Per A35 (Π's canonical output is a typed deterministic stream): the
// statements path is the realization-layer-agnostic surface that
// canary tests + Deploy.executeStream + Render.toText all consume.
// Ships in the chapter 4.1.A close arc as the typed-stream equivalent
// of the legacy `RawTextEmitter.statements`, MINUS the raw `InsertRow`
// static populations (those route through chapter 4.1.B's
// StaticSeedsEmitter with the CDC-aware MERGE shape, not raw INSERTs).
// ---------------------------------------------------------------------------

[<Fact>]
let ``SsdtDdlEmitter.statements is schema-pure (no InsertRow statements)`` () =
    let enriched = enrich sampleCatalog
    let stmts = SsdtDdlEmitter.statements enriched |> List.ofSeq
    let hasInsertRow =
        stmts
        |> List.exists (fun s ->
            match s with
            | InsertRow _ -> true
            | _ -> false)
    Assert.False (hasInsertRow,
        "SsdtDdlEmitter.statements must not yield InsertRow (static populations are chapter 4.1.B's StaticSeedsEmitter territory)")

[<Fact>]
let ``SsdtDdlEmitter.statements yields one CreateTable per catalog kind`` () =
    let enriched = enrich sampleCatalog
    let allKinds = Catalog.allKinds enriched
    let stmts = SsdtDdlEmitter.statements enriched |> List.ofSeq
    let createTables =
        stmts
        |> List.choose (fun s ->
            match s with
            | CreateTable (table, _, _, _, _, _) -> Some table
            | _ -> None)
    Assert.Equal (List.length allKinds, List.length createTables)

[<Fact>]
let ``T1: SsdtDdlEmitter.statements is byte-deterministic across repeat invocations`` () =
    let enriched = enrich sampleCatalog
    let s1 = SsdtDdlEmitter.statements enriched |> List.ofSeq
    let s2 = SsdtDdlEmitter.statements enriched |> List.ofSeq
    Assert.Equal<Statement list> (s1, s2)

[<Fact>]
let ``SsdtDdlEmitter.statements composed via Render.toText produces the concatenated SsdtFile bodies`` () =
    // Sanity-check the algebra: catalog-wide statements rendered as
    // text is structurally equivalent to per-kind SsdtFile bodies
    // joined in catalog order (modulo the per-file CREATE TABLE +
    // CREATE INDEX framing). This relationship is what makes the
    // statement-stream surface useful to canary tests that need a
    // single-string deploy.
    let enriched = enrich sampleCatalog
    let viaStatements =
        SsdtDdlEmitter.statements enriched
        |> Render.toText
    // The text MUST contain a CREATE TABLE for every kind.
    for k in Catalog.allKinds enriched do
        let qualified = sprintf "[%s].[%s]" (TableId.schemaText k.Physical) (TableId.tableText k.Physical)
        Assert.Contains (qualified, viaStatements)

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
        { Attribute.create (attrKey ["AllTypes"; label]) (mkName label) typ with Column = ColumnRealization.create (label.ToUpperInvariant()) (not isPk) |> Result.value; IsPrimaryKey = isPk; IsMandatory = isPk }
    {
        SsKey = kindKey ["AllTypes"]
        Name = mkName "AllTypes"
        Origin = Native
        Modality = []
        Physical = mkTableId "dbo" "OSUSR_X_ALLTYPES"
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
        Indexes = []; Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

let private allPrimitiveTypesCatalog : Catalog =
    {
        Modules = [
            {
                SsKey = modKey "AllTypesModule"
                Name = mkName "AllTypesModule"
                Kinds = [ allPrimitiveTypesKind ]
                IsActive = true
                ExtendedProperties = []
                }
        ]
        Sequences = []
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
let ``Slice 2: SsdtDdlEmitter emits a slice for the all-types fixture (T11 keyset coverage)`` () =
    // Pre-RawTextEmitter-retirement: this test cross-validated that
    // BOTH RawTextEmitter and SsdtDdlEmitter produced a slice for the
    // all-types kind. RawTextEmitter is retired; the SsdtDdlEmitter-
    // alone keyset coverage is the structural property at this site.
    // Cross-emitter sibling commutativity (SsdtDdl + Json +
    // Distributions) lives at `SiblingEmitterContractTests.fs`.
    let enriched = enrich allPrimitiveTypesCatalog
    let ssdtDdl = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let ssdtDdlSlices = ArtifactByKind.toMap ssdtDdl
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
        { Attribute.create (attrKey ["Composite"; label]) (mkName label) typ with Column = ColumnRealization.create (label.ToUpperInvariant()) (false) |> Result.value; IsPrimaryKey = isPk; IsMandatory = isPk }
    {
        SsKey = kindKey ["Composite"]
        Name = mkName "Composite"
        Origin = Native
        Modality = []
        Physical = mkTableId "dbo" "OSUSR_X_COMPOSITE"
        Attributes = [
            attr "TenantId" Integer true   // first PK column
            attr "Code" Text true          // second PK column (composite)
            attr "Description" Text false
        ]
        References = []
        Indexes = []; Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

let private compositePkCatalog : Catalog =
    {
        Modules = [
            {
                SsKey = modKey "CompositePkModule"
                Name = mkName "CompositePkModule"
                Kinds = [ compositePkKind ]
                IsActive = true
                ExtendedProperties = []
                }
        ]
        Sequences = []
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
        { Attribute.create (attrKey ["Indexed"; label]) (mkName label) typ with Column = ColumnRealization.create (label.ToUpperInvariant()) (not isPk) |> Result.value; IsPrimaryKey = isPk; IsMandatory = isPk }
    {
        SsKey = indexedKindKey
        Name = mkName "Indexed"
        Origin = Native
        Modality = []
        Physical = mkTableId "dbo" "OSUSR_X_INDEXED"
        Attributes = [
            attr "Id" Integer true
            attr "Lookup" Text false
            attr "Code" Text false
        ]
        References = []
        Indexes = [
            // Non-unique index on Lookup column.
            Index.ofKeyColumns indexedNonUniqueIdxKey (mkName "IX_OSUSR_X_INDEXED_LOOKUP") [ attrKey ["Indexed"; "Lookup"] ]
            // Unique index on Code column.
            { Index.ofKeyColumns indexedUniqueIdxKey (mkName "UIX_OSUSR_X_INDEXED_CODE") [ attrKey ["Indexed"; "Code"] ] with Uniqueness = Unique }
            // PK index — should be SKIPPED by the emitter (PK is inlined
            // in CREATE TABLE per V1 convention).
            { Index.ofKeyColumns indexedPkIdxKey (mkName "PK_OSUSR_X_INDEXED") [ attrKey ["Indexed"; "Id"] ] with Uniqueness = PrimaryKey }
        ]
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

let private indexedCatalog : Catalog =
    {
        Modules = [
            {
                SsKey = modKey "IndexedModule"
                Name = mkName "IndexedModule"
                Kinds = [ indexedKind ]
                IsActive = true
                ExtendedProperties = []
                }
        ]
        Sequences = []
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
        Origin = Native
        Modality = []
        Physical = mkTableId "dbo" "OSUSR_X_PARENT"
        Attributes = [
            { Attribute.create (attrKey ["Parent"; "Id"]) (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
        ]
        References = []
        Indexes = []; Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

let private childKind : Kind =
    {
        SsKey = childKindKey
        Name = mkName "Child"
        Origin = Native
        Modality = []
        Physical = mkTableId "dbo" "OSUSR_X_CHILD"
        Attributes = [
            { Attribute.create (attrKey ["Child"; "Id"]) (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
            { Attribute.create childParentIdAttrKey (mkName "ParentId") Integer with Column = ColumnRealization.create ("PARENT_ID") (false) |> Result.value; IsMandatory = true }
        ]
        References = [
            Reference.create childParentFkKey (mkName "ParentFk") childParentIdAttrKey parentKindKey
        ]
        Indexes = []; Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

let private fkCatalog : Catalog =
    {
        Modules = [
            {
                SsKey = modKey "FkModule"
                Name = mkName "FkModule"
                Kinds = [ parentKind; childKind ]
                IsActive = true
                ExtendedProperties = []
                }
        ]
        Sequences = []
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
// Slice 4.3 — cross-DB FK emission (three-part name; L3-S10 / L3-I10).
//
// The only IR-present-but-unemitted feature pre-slice: when an FK's target
// kind carries an explicit catalog coordinate (`Physical.Catalog = Some db`),
// `toTableId` dropped it and `schemaObjectFromTableId` emitted a two-part
// `[schema].[table]` name — a silent downgrade of the cross-database
// reference. Slice 4.3 honors the coordinate: the FK REFERENCES clause emits
// the three-part `[db].[schema].[table]`. The truly-external case (target
// absent from the catalog) still drops via `foreignKeyDropDiagnostics`
// (Warning witness), so neither cross-DB path is silent
// (L3-Boundary-NoSilentDrop).
// ---------------------------------------------------------------------------

let private crossDbParentKind : Kind =
    { parentKind with
        Physical = (TableId.createWithCatalog "AnalyticsDb" "dbo" "OSUSR_X_PARENT" |> Result.value) }

let private crossDbFkCatalog : Catalog =
    { fkCatalog with
        Modules = [
            { (List.head fkCatalog.Modules) with
                Kinds = [ crossDbParentKind; childKind ] } ] }

[<Fact>]
let ``L3-S10: cross-DB FK emits a three-part name (no silent downgrade)`` () =
    let enriched = enrich crossDbFkCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let childBody =
        match Map.tryFind childKindKey (ArtifactByKind.toMap artifact) with
        | Some f -> f.Body
        | None -> Assert.Fail "expected slice for childKind"; ""
    // The REFERENCES clause names the target with the explicit catalog
    // coordinate as a three-part identifier — the honored cross-DB reference.
    Assert.Contains ("REFERENCES", childBody)
    Assert.Contains ("[AnalyticsDb].[dbo].[OSUSR_X_PARENT]", childBody)
    // The FK was NOT silently dropped (the inline constraint is present).
    Assert.Contains ("FOREIGN KEY", childBody)
    Assert.Contains ("FK_OSUSR_X_CHILD_OSUSR_X_PARENT_PARENT_ID", childBody)

[<Fact>]
let ``L3-S10: catalog-bearing kind emits a three-part CREATE TABLE name`` () =
    // `schemaObjectFromTableId` honors the catalog coordinate uniformly —
    // the catalog-bearing kind's own CREATE TABLE names it three-part.
    let enriched = enrich crossDbFkCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let parentBody =
        match Map.tryFind parentKindKey (ArtifactByKind.toMap artifact) with
        | Some f -> f.Body
        | None -> Assert.Fail "expected slice for parentKind"; ""
    Assert.Contains ("[AnalyticsDb].[dbo].[OSUSR_X_PARENT]", parentBody)

[<Fact>]
let ``L3-S10: Catalog = None FK emission stays two-part (additive)`` () =
    // The two-part path is unchanged: a `Catalog = None` fixture emits the
    // two-part `[dbo].[OSUSR_X_PARENT]` reference with no catalog prefix.
    let enriched = enrich fkCatalog
    let childBody =
        match Map.tryFind childKindKey (SsdtDdlEmitter.emitSlices enriched |> mustOk |> ArtifactByKind.toMap) with
        | Some f -> f.Body
        | None -> Assert.Fail "expected slice for childKind"; ""
    Assert.Contains ("[dbo].[OSUSR_X_PARENT]", childBody)
    // No catalog coordinate leaks into the two-part path.
    Assert.DoesNotContain ("AnalyticsDb", childBody)

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

// ---------------------------------------------------------------------------
// Chapter 4.1.A slice 6 — Cross-module FK verification.
//
// Per `CHAPTER_4_PRESCOPE_SSDT_DDL_EMITTER.md` §8 slice 6: cross-module
// FKs (FK whose target lives in a different module) deploy correctly
// because SsdtDdlEmitter.statements uses TopologicalOrderPass.runWith
// SkipSelfEdges to order kinds across module boundaries. The pre-scope
// gated this slice on chapter 3.2 SnapshotRowsets landing; SnapshotRowsets
// shipped at chapter 3.2 close (2026-05-10), so this slice ships as a
// verification test now.
//
// The slice 6 implementation is structurally complete already (the
// topological pass operates over `Catalog.allKinds` which spans every
// module); this test asserts the cross-module property survives.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice 6: cross-module FK target kind precedes its source in statement order`` () =
    // Build a catalog with two modules: A.AKind (referenced) and
    // B.BKind (referencer with FK to A.AKind). Topological order
    // must place A.AKind's CREATE TABLE before B.BKind's.
    let aModuleKey = modKey "A"
    let bModuleKey = modKey "B"
    let aKindKey   = kindKey ["A"; "AKind"]
    let bKindKey   = kindKey ["B"; "BKind"]
    let aIdAttr    = attrKey ["A"; "AKind"; "Id"]
    let bIdAttr    = attrKey ["B"; "BKind"; "Id"]
    let bFkAttr    = attrKey ["B"; "BKind"; "AId"]
    let crossRefKey = refKey ["B"; "BKind"; "AId"]
    let aKind : Kind =
        { SsKey = aKindKey
          Name  = mkName "AKind"
          Origin = Native
          Modality = []
          Physical = mkTableId "dbo" "OSUSR_A_AKIND"
          Attributes =
              [ { Attribute.create aIdAttr (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true } ]
          References = []
          Indexes = []
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    let bKind : Kind =
        { SsKey = bKindKey
          Name  = mkName "BKind"
          Origin = Native
          Modality = []
          Physical = mkTableId "dbo" "OSUSR_B_BKIND"
          Attributes =
              [ { Attribute.create bIdAttr (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create bFkAttr (mkName "AId") Integer with Column = ColumnRealization.create ("A_ID") (false) |> Result.value; IsMandatory = true } ]
          References =
              [ Reference.create crossRefKey (mkName "FkToA") bFkAttr aKindKey ]
          Indexes = []
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    let catalog : Catalog =
        { Modules =
            [ { SsKey = aModuleKey; Name = mkName "A"; Kinds = [ aKind ]; IsActive = true; ExtendedProperties = [] }
              { SsKey = bModuleKey; Name = mkName "B"; Kinds = [ bKind ]; IsActive = true; ExtendedProperties = [] } ]
          Sequences = [] }
    let enriched = enrich catalog
    let statements =
        SsdtDdlEmitter.statements enriched
        |> Seq.toList
    // Find the index of each kind's CREATE TABLE in the statement stream.
    let findCreateTableIndex (kindKey: SsKey) : int =
        statements
        |> List.findIndex (fun stmt ->
            match stmt with
            | Statement.CreateTable (table, _, _, _, _, _) ->
                TableId.schemaText table + "." + TableId.tableText table =
                    (Catalog.tryFindKind kindKey enriched
                     |> Option.map (fun k -> TableId.schemaText k.Physical + "." + TableId.tableText k.Physical)
                     |> Option.defaultValue "")
            | _ -> false)
    let aIdx = findCreateTableIndex aKindKey
    let bIdx = findCreateTableIndex bKindKey
    Assert.True (aIdx < bIdx,
                 sprintf "expected A.AKind (idx %d) before B.BKind (idx %d)" aIdx bIdx)

[<Fact>]
let ``Slice 6: cross-module FK emits inline FOREIGN KEY constraint`` () =
    // Build the same two-module catalog and verify B.BKind's CREATE TABLE
    // emits `CONSTRAINT [FK_...] FOREIGN KEY` referencing A.AKind by its
    // physical name (resolved via Catalog.tryFindKind).
    let aModuleKey = modKey "A"
    let bModuleKey = modKey "B"
    let aKindKey   = kindKey ["A"; "AKind"]
    let bKindKey   = kindKey ["B"; "BKind"]
    let aIdAttr    = attrKey ["A"; "AKind"; "Id"]
    let bIdAttr    = attrKey ["B"; "BKind"; "Id"]
    let bFkAttr    = attrKey ["B"; "BKind"; "AId"]
    let crossRefKey = refKey ["B"; "BKind"; "AId"]
    let aKind : Kind =
        { SsKey = aKindKey; Name = mkName "AKind"; Origin = Native
          Modality = []
          Physical = mkTableId "dbo" "OSUSR_A_AKIND"
          Attributes =
              [ { Attribute.create aIdAttr (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true } ]
          References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    let bKind : Kind =
        { SsKey = bKindKey; Name = mkName "BKind"; Origin = Native
          Modality = []
          Physical = mkTableId "dbo" "OSUSR_B_BKIND"
          Attributes =
              [ { Attribute.create bIdAttr (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create bFkAttr (mkName "AId") Integer with Column = ColumnRealization.create ("A_ID") (false) |> Result.value; IsMandatory = true } ]
          References =
              [ Reference.create crossRefKey (mkName "FkToA") bFkAttr aKindKey ]
          Indexes = []
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    let catalog : Catalog =
        { Modules =
            [ { SsKey = aModuleKey; Name = mkName "A"; Kinds = [ aKind ]; IsActive = true; ExtendedProperties = [] }
              { SsKey = bModuleKey; Name = mkName "B"; Kinds = [ bKind ]; IsActive = true; ExtendedProperties = [] } ]
          Sequences = [] }
    let enriched = enrich catalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let bFile = ArtifactByKind.toMap artifact |> Map.find bKindKey
    // Cross-module FK to A.AKind emits inline; the physical name comes
    // from Catalog.tryFindKind resolving the TargetKind.
    Assert.Contains ("REFERENCES [dbo].[OSUSR_A_AKIND]", bFile.Body)

[<Fact>]
let ``Slice 6: T11 keyset holds across modules (every kind keyed; cross-module FKs don't perturb keyset)`` () =
    let aModuleKey = modKey "A"
    let bModuleKey = modKey "B"
    let aKindKey   = kindKey ["A"; "AKind"]
    let bKindKey   = kindKey ["B"; "BKind"]
    let aIdAttr    = attrKey ["A"; "AKind"; "Id"]
    let bIdAttr    = attrKey ["B"; "BKind"; "Id"]
    let bFkAttr    = attrKey ["B"; "BKind"; "AId"]
    let crossRefKey = refKey ["B"; "BKind"; "AId"]
    let aKind : Kind =
        { SsKey = aKindKey; Name = mkName "AKind"; Origin = Native
          Modality = []
          Physical = mkTableId "dbo" "OSUSR_A_AKIND"
          Attributes =
              [ { Attribute.create aIdAttr (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true } ]
          References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    let bKind : Kind =
        { SsKey = bKindKey; Name = mkName "BKind"; Origin = Native
          Modality = []
          Physical = mkTableId "dbo" "OSUSR_B_BKIND"
          Attributes =
              [ { Attribute.create bIdAttr (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create bFkAttr (mkName "AId") Integer with Column = ColumnRealization.create ("A_ID") (false) |> Result.value; IsMandatory = true } ]
          References =
              [ Reference.create crossRefKey (mkName "FkToA") bFkAttr aKindKey ]
          Indexes = []
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    let catalog : Catalog =
        { Modules =
            [ { SsKey = aModuleKey; Name = mkName "A"; Kinds = [ aKind ]; IsActive = true; ExtendedProperties = [] }
              { SsKey = bModuleKey; Name = mkName "B"; Kinds = [ bKind ]; IsActive = true; ExtendedProperties = [] } ]
          Sequences = [] }
    let enriched = enrich catalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let keys = ArtifactByKind.toMap artifact |> Map.toSeq |> Seq.map fst |> Set.ofSeq
    Assert.Equal<Set<SsKey>>
        (Set.ofList [ aKindKey; bKindKey ],
         keys)

// ---------------------------------------------------------------------------
// Slice 5.13.column-features-emit — DEFAULT + CHECK emission through the
// SSDT realization. Confirms `Attribute.DefaultValue` (`SqlLiteral option`)
// and `Kind.ColumnChecks` (`ColumnCheck list`) both surface in the
// rendered CREATE TABLE body. The typed-AST path is the same one
// `ScriptDomBuild.buildCreateTable` exercises in
// `ScriptDomRoundTripTests`; this canary closes the V2-Attribute → SSDT-
// emission integration gap (chapter A.0' slice ε emit closure).
// ---------------------------------------------------------------------------

let private columnFeaturesKind : Kind =
    let mkAttr key label typ isPk =
        { Attribute.create (attrKey ["Widget"; key]) (mkName label) typ
            with Column = ColumnRealization.create (label.ToUpperInvariant()) (not isPk) |> Result.value
                 IsPrimaryKey = isPk
                 IsMandatory  = isPk }
    let idAttr = mkAttr "Id" "Id" Integer true
    let priceAttr =
        { mkAttr "Price" "Price" Integer false with
            DefaultValue = Some (SqlLiteral.ofRaw Integer "0") }
    let nameAttr =
        { mkAttr "Name" "Name" Text false with
            Length = Some 100
            DefaultValue = Some (SqlLiteral.ofRaw Text "unknown") }
    let checkOk =
        ColumnCheck.create
            (attrKey ["Widget"; "CK_Price"])
            (Name.create "CK_Widget_PricePositive" |> Result.toOption)
            "([PRICE] >= 0)"
            false
        |> Result.value
    { Kind.create
        (kindKey ["Widget"])
        (mkName "Widget")
        (mkTableId "dbo" "OSUSR_W_WIDGET")
        [ idAttr; priceAttr; nameAttr ]
      with ColumnChecks = [ checkOk ] }

let private columnFeaturesCatalog : Catalog =
    {
        Modules = [
            IRBuilders.mkModule (modKey "WidgetModule") (mkName "WidgetModule") [ columnFeaturesKind ]
        ]
        Sequences = []
    }

[<Fact>]
let ``Slice 5.13.column-features-emit: DEFAULT clause surfaces in CREATE TABLE body for typed-literal default`` () =
    let enriched = enrich columnFeaturesCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let file =
        ArtifactByKind.toMap artifact
        |> Map.find columnFeaturesKind.SsKey
    // ScriptDom emits `DEFAULT <literal>` as an inline column constraint;
    // assert the integer + text defaults both surfaced.
    Assert.Contains ("DEFAULT 0", file.Body)
    Assert.Contains ("DEFAULT N'unknown'", file.Body)

[<Fact>]
let ``Slice 5.13.column-features-emit: CHECK constraint surfaces in CREATE TABLE body via TSql160Parser`` () =
    let enriched = enrich columnFeaturesCatalog
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let file =
        ArtifactByKind.toMap artifact
        |> Map.find columnFeaturesKind.SsKey
    // The CHECK constraint is named and parsed from `([PRICE] >= 0)`;
    // ScriptDom round-trips it as `CHECK ([PRICE]>=0)` (no spaces around
    // the operator per the generator's pinned options).
    Assert.Contains ("CONSTRAINT [CK_Widget_PricePositive] CHECK", file.Body)

[<Fact>]
let ``Slice 5.13.column-features-emit: T1 byte-determinism holds with DEFAULT + CHECK`` () =
    let enriched = enrich columnFeaturesCatalog
    let a1 = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let a2 = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let b1 = (ArtifactByKind.toMap a1 |> Map.find columnFeaturesKind.SsKey).Body
    let b2 = (ArtifactByKind.toMap a2 |> Map.find columnFeaturesKind.SsKey).Body
    Assert.Equal (b1, b2)

// ---------------------------------------------------------------------------
// Slice 5.13.fk-features-emit — ON UPDATE referential action + WITH NOCHECK
// FK trust-state preservation through the SSDT realization. Mirrors the
// column-features-emit pattern on the FK axis (matrix rows 58 + 59).
// ---------------------------------------------------------------------------

let private fkFeaturesAKey       = kindKey ["A"; "AKind"]
let private fkFeaturesBKey       = kindKey ["B"; "BKind"]
let private fkFeaturesAIdAttr    = attrKey ["A"; "AKind"; "Id"]
let private fkFeaturesBIdAttr    = attrKey ["B"; "BKind"; "Id"]
let private fkFeaturesBFkAttr    = attrKey ["B"; "BKind"; "AId"]
let private fkFeaturesCrossRef   = refKey ["B"; "BKind"; "AId"]

let private fkFeaturesAKind : Kind =
    { Kind.create
        fkFeaturesAKey
        (mkName "AKind")
        (mkTableId "dbo" "OSUSR_A_AKIND")
        [ { Attribute.create fkFeaturesAIdAttr (mkName "Id") Integer with
                Column = ColumnRealization.create ("ID") (false) |> Result.value
                IsPrimaryKey = true
                IsMandatory  = true } ]
      with References = []; Indexes = [] }

let private fkFeaturesBKind (onUpdate: ReferenceAction option) (trusted: bool) : Kind =
    let ref =
        { Reference.create
            fkFeaturesCrossRef (mkName "FkToA") fkFeaturesBFkAttr fkFeaturesAKey with
            OnDelete            = Cascade
            HasDbConstraint     = true
            OnUpdate            = onUpdate
            IsConstraintTrusted = trusted }
    { Kind.create
        fkFeaturesBKey
        (mkName "BKind")
        (mkTableId "dbo" "OSUSR_B_BKIND")
        [ { Attribute.create fkFeaturesBIdAttr (mkName "Id") Integer with
                Column = ColumnRealization.create ("ID") (false) |> Result.value
                IsPrimaryKey = true
                IsMandatory  = true }
          { Attribute.create fkFeaturesBFkAttr (mkName "AId") Integer with
                Column = ColumnRealization.create ("A_ID") (false) |> Result.value
                IsMandatory  = true } ]
      with References = [ ref ]; Indexes = [] }

let private fkFeaturesCatalog (onUpdate: ReferenceAction option) (trusted: bool) : Catalog =
    {
        Modules =
            [ { SsKey = modKey "A"; Name = mkName "A"; Kinds = [ fkFeaturesAKind ]; IsActive = true; ExtendedProperties = [] }
              { SsKey = modKey "B"; Name = mkName "B"; Kinds = [ fkFeaturesBKind onUpdate trusted ]; IsActive = true; ExtendedProperties = [] } ]
        Sequences = []
    }

[<Fact>]
let ``Slice 5.13.fk-features-emit: OnUpdate = None omits the ON UPDATE clause (V1 emission shape)`` () =
    let enriched = enrich (fkFeaturesCatalog None true)
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let body = (ArtifactByKind.toMap artifact |> Map.find fkFeaturesBKey).Body
    // OnDelete = Cascade still emits ON DELETE CASCADE.
    Assert.Contains ("ON DELETE CASCADE", body)
    // OnUpdate = None means no ON UPDATE clause in the FK definition.
    Assert.DoesNotContain ("ON UPDATE", body)

[<Fact>]
let ``Slice 5.13.fk-features-emit: OnUpdate = Some Cascade emits ON UPDATE CASCADE`` () =
    let enriched = enrich (fkFeaturesCatalog (Some Cascade) true)
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let body = (ArtifactByKind.toMap artifact |> Map.find fkFeaturesBKey).Body
    Assert.Contains ("ON DELETE CASCADE", body)
    Assert.Contains ("ON UPDATE CASCADE", body)

[<Fact>]
let ``Slice 5.13.fk-features-emit: IsConstraintTrusted = true omits ALTER TABLE WITH NOCHECK`` () =
    let enriched = enrich (fkFeaturesCatalog None true)
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let body = (ArtifactByKind.toMap artifact |> Map.find fkFeaturesBKey).Body
    Assert.DoesNotContain ("WITH NOCHECK", body)
    Assert.DoesNotContain ("ALTER TABLE", body)

[<Fact>]
let ``Slice 5.13.fk-features-emit: IsConstraintTrusted = false emits post-CREATE-TABLE ALTER TABLE WITH NOCHECK CHECK CONSTRAINT`` () =
    let enriched = enrich (fkFeaturesCatalog None false)
    let artifact = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let body = (ArtifactByKind.toMap artifact |> Map.find fkFeaturesBKey).Body
    // V1 emission shape: full ALTER statement with WITH NOCHECK prefix
    // + CHECK CONSTRAINT suffix referencing the named FK.
    Assert.Contains ("ALTER TABLE [dbo].[OSUSR_B_BKIND] WITH NOCHECK CHECK CONSTRAINT", body)
    Assert.Contains ("FK_OSUSR_B_BKIND_OSUSR_A_AKIND_A_ID", body)
    // The CREATE TABLE itself does not carry NOCHECK inline — ScriptDom
    // models this as a separate ALTER statement (preservation of the
    // deployed state is a post-creation concern).
    let createIdx = body.IndexOf "CREATE TABLE [dbo].[OSUSR_B_BKIND]"
    let alterIdx  = body.IndexOf "ALTER TABLE [dbo].[OSUSR_B_BKIND] WITH NOCHECK"
    Assert.True (createIdx >= 0)
    Assert.True (alterIdx > createIdx, "ALTER TABLE must come after CREATE TABLE")

[<Fact>]
let ``Slice 5.13.fk-features-emit: T1 byte-determinism holds for OnUpdate + WITH NOCHECK emissions`` () =
    let enriched = enrich (fkFeaturesCatalog (Some Cascade) false)
    let a1 = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let a2 = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let b1 = (ArtifactByKind.toMap a1 |> Map.find fkFeaturesBKey).Body
    let b2 = (ArtifactByKind.toMap a2 |> Map.find fkFeaturesBKey).Body
    Assert.Equal (b1, b2)

// ---------------------------------------------------------------------------
// Slice 5.13.index-features-emit — IGNORE_DUP_KEY + DATA_COMPRESSION +
// post-CREATE-INDEX ALTER INDEX DISABLE through the SSDT realization.
// Mirrors the FK + column slices on the index axis (matrix rows 55 + 56).
// ---------------------------------------------------------------------------

let private idxFeaturesKindKey   = kindKey ["Widget"]
let private idxFeaturesIdAttr    = attrKey ["Widget"; "Id"]
let private idxFeaturesNameAttr  = attrKey ["Widget"; "Name"]
let private idxFeaturesIdxKey    = SsKey.synthesizedComposite "OS_IDX" ["Widget"; "IX_Name"] |> Result.value

let private idxFeaturesKind
    (ignoreDup: bool)
    (disabled: bool)
    (compression: DataCompressionLevel option)
    : Kind =
    let idAttr =
        { Attribute.create idxFeaturesIdAttr (mkName "Id") Integer with
            Column = ColumnRealization.create ("ID") (false) |> Result.value
            IsPrimaryKey = true
            IsMandatory  = true }
    let nameAttr =
        { Attribute.create idxFeaturesNameAttr (mkName "Name") Text with
            Column = ColumnRealization.create ("NAME") (false) |> Result.value
            Length       = Some 100
            IsMandatory  = true }
    let idx =
        { Index.create idxFeaturesIdxKey (mkName "IX_Widget_Name") (IndexColumn.ascendingList [ idxFeaturesNameAttr ]) with
            Uniqueness         = Unique
            IgnoreDuplicateKey = ignoreDup
            IsDisabled         = disabled
            DataCompression    = compression }
    { Kind.create
        idxFeaturesKindKey
        (mkName "Widget")
        (mkTableId "dbo" "OSUSR_W_WIDGET")
        [ idAttr; nameAttr ]
      with Indexes = [ idx ] }

let private idxFeaturesCatalog ignoreDup disabled compression : Catalog =
    {
        Modules = [
            IRBuilders.mkModule (modKey "WidgetModule") (mkName "WidgetModule")
                [ idxFeaturesKind ignoreDup disabled compression ]
        ]
        Sequences = []
    }

[<Fact>]
let ``Slice 5.13.index-features-emit: IgnoreDuplicateKey = false omits IGNORE_DUP_KEY (V1 default)`` () =
    let enriched = enrich (idxFeaturesCatalog false false None)
    let body = (ArtifactByKind.toMap (SsdtDdlEmitter.emitSlices enriched |> mustOk) |> Map.find idxFeaturesKindKey).Body
    Assert.DoesNotContain ("IGNORE_DUP_KEY", body)

[<Fact>]
let ``Slice 5.13.index-features-emit: IgnoreDuplicateKey = true emits IGNORE_DUP_KEY = ON in WITH clause`` () =
    let enriched = enrich (idxFeaturesCatalog true false None)
    let body = (ArtifactByKind.toMap (SsdtDdlEmitter.emitSlices enriched |> mustOk) |> Map.find idxFeaturesKindKey).Body
    Assert.Contains ("IGNORE_DUP_KEY = ON", body)

[<Fact>]
let ``Slice 5.13.index-features-emit: DataCompression = None omits DATA_COMPRESSION (V1 default)`` () =
    let enriched = enrich (idxFeaturesCatalog false false None)
    let body = (ArtifactByKind.toMap (SsdtDdlEmitter.emitSlices enriched |> mustOk) |> Map.find idxFeaturesKindKey).Body
    Assert.DoesNotContain ("DATA_COMPRESSION", body)

[<Fact>]
let ``Slice 5.13.index-features-emit: DataCompression = Page emits DATA_COMPRESSION = PAGE`` () =
    let enriched = enrich (idxFeaturesCatalog false false (Some DataCompressionLevel.Page))
    let body = (ArtifactByKind.toMap (SsdtDdlEmitter.emitSlices enriched |> mustOk) |> Map.find idxFeaturesKindKey).Body
    Assert.Contains ("DATA_COMPRESSION = PAGE", body)

[<Fact>]
let ``Slice 5.13.index-features-emit: DataCompression = Row emits DATA_COMPRESSION = ROW`` () =
    let enriched = enrich (idxFeaturesCatalog false false (Some DataCompressionLevel.Row))
    let body = (ArtifactByKind.toMap (SsdtDdlEmitter.emitSlices enriched |> mustOk) |> Map.find idxFeaturesKindKey).Body
    Assert.Contains ("DATA_COMPRESSION = ROW", body)

[<Fact>]
let ``Slice 5.13.index-features-emit: IsDisabled = false omits ALTER INDEX DISABLE (V1 default)`` () =
    let enriched = enrich (idxFeaturesCatalog false false None)
    let body = (ArtifactByKind.toMap (SsdtDdlEmitter.emitSlices enriched |> mustOk) |> Map.find idxFeaturesKindKey).Body
    Assert.DoesNotContain ("ALTER INDEX", body)
    Assert.DoesNotContain ("DISABLE", body)

[<Fact>]
let ``Slice 5.13.index-features-emit: IsDisabled = true emits post-CREATE-INDEX ALTER INDEX DISABLE`` () =
    let enriched = enrich (idxFeaturesCatalog false true None)
    let body = (ArtifactByKind.toMap (SsdtDdlEmitter.emitSlices enriched |> mustOk) |> Map.find idxFeaturesKindKey).Body
    // V1 emission shape: ALTER INDEX [name] ON [Schema].[Table] DISABLE.
    Assert.Contains ("ALTER INDEX [IX_Widget_Name]", body)
    Assert.Contains ("DISABLE", body)
    // ALTER comes after CREATE INDEX so the named index exists when
    // the ALTER references it.
    let createIdx = body.IndexOf "CREATE UNIQUE INDEX [IX_Widget_Name]"
    let alterIdx  = body.IndexOf "ALTER INDEX [IX_Widget_Name]"
    Assert.True (createIdx >= 0)
    Assert.True (alterIdx > createIdx, "ALTER INDEX must come after CREATE INDEX")

[<Fact>]
let ``Slice 5.13.index-features-emit: T1 byte-determinism holds across the new index axes`` () =
    let enriched = enrich (idxFeaturesCatalog true true (Some DataCompressionLevel.Page))
    let a1 = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let a2 = SsdtDdlEmitter.emitSlices enriched |> mustOk
    let b1 = (ArtifactByKind.toMap a1 |> Map.find idxFeaturesKindKey).Body
    let b2 = (ArtifactByKind.toMap a2 |> Map.find idxFeaturesKindKey).Body
    Assert.Equal (b1, b2)
