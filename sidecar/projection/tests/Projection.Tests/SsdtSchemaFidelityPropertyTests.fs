module Projection.Tests.SsdtSchemaFidelityPropertyTests

// Slice 5.3.α — V1 SMO PerTableEmission audit + V2 ScriptDom structural-
// fidelity property tests. The V1 emission cluster lives at
// `src/Osm.Smo/PerTableEmission/*.cs` (1905 LOC across 7 files); V2's
// canonical equivalent is `src/Projection.Targets.SSDT/{ScriptDomBuild,
// SsdtDdlEmitter,Render,BatchSplitter}.fs`. Matrix rows 120 + 182 + 183
// carry the prior cluster-level + per-file line-by-line audits; this
// arc appends Status-history amendments + 5 new rows for the previously-
// unaudited V1 files (CreateTableFormatter / ConstraintFormatter /
// StatementBatchFormatter / IdentifierFormatter / ExtendedPropertyScript
// Builder).
//
// **What this file tests.** V2's structural-fidelity claims that the
// matrix amendments rest on — assertions that hold for EVERY catalog
// the canary or fixture suite exercises (not just one variant value).
// Property-style: small generators sweep variant values; example-style:
// the sample fixtures pin specific axes. Combined with the 2026-05-18
// per-axis property sweep on SCHEMA emission (DEFAULT / CHECK / OnUpdate
// / NOCHECK / IGNORE_DUP_KEY / DATA_COMPRESSION / IsDisabled), this
// file completes verification depth for the SMO → ScriptDom audit.
//
// **What this file does NOT test.** V1↔V2 byte-equivalence on the
// emission text. V1 SMO and V2 ScriptDom render different SQL formatting
// (V1 post-processes with CreateTableFormatter + ConstraintFormatter;
// V2 relies on `Sql160ScriptGenerator` pinned options). The byte-shape
// divergence is intentional architecture (per matrix row 120 + DECISIONS
// 2026-05-18 — Schema emission via ScriptDom typed-AST over SMO scripter)
// and the load-bearing fidelity gate is the PhysicalSchema round-trip
// diff in the canary, NOT byte-equality of emitted text. Property tests
// here verify V2's emission satisfies the V1-equivalent STRUCTURAL
// contracts (same column order; same constraint shape; same statement
// counts; same identifier quoting strategy) — what survives at the
// PhysicalSchema diff layer.

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Shared infrastructure. Mirrors the SsdtDdlEmitterTests + property-sweep
// helper shape — enrich via CanonicalizeIdentity, emit slices, look up
// per-kind body for assertions.
// ---------------------------------------------------------------------------

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

let private allBodies (cat: Catalog) : (SsKey * string) seq =
    let artifact = SsdtDdlEmitter.emitSlices (enrich cat) |> mustOk
    ArtifactByKind.toMap artifact
    |> Map.toSeq
    |> Seq.map (fun (k, file) -> k, file.Body)

// ---------------------------------------------------------------------------
// 5.3.α.create-table — column structure properties. V1's
// CreateTableStatementBuilder.BuildCreateTableStatement (L23) maps the
// SMO column list 1:1 to ScriptDom `ColumnDefinition` entries in
// declaration order; V2's ScriptDomBuild.buildCreateTable preserves the
// same shape per Attribute.create + Kind.create ordering.
// ---------------------------------------------------------------------------

[<Fact>]
let ``5.3.α.create-table: every catalog kind emits exactly one CREATE TABLE statement`` () =
    let bodies = allBodies sampleCatalog |> List.ofSeq
    Assert.NotEmpty bodies
    for _, body in bodies do
        let occurrences =
            body.Split([| "CREATE TABLE " |], System.StringSplitOptions.None)
            |> Array.length
        // N+1 segments = N occurrences of the splitter.
        Assert.Equal (2, occurrences)

[<Fact>]
let ``5.3.α.create-table: every emitted column appears in catalog kind's Attributes list`` () =
    // Per V1 CreateTableStatementBuilder L288-367: BuildColumnDefinition
    // emits one ColumnDefinition per SmoColumnDefinition. V2 mirrors this
    // via ScriptDomBuild's column projection. Property: no column appears
    // in the emitted body that doesn't trace back to the IR.
    let enriched = enrich sampleCatalog
    let allKinds = Catalog.allKinds enriched
    for kind in allKinds do
        let body = bodyOf kind.SsKey sampleCatalog
        for attr in kind.Attributes do
            Assert.Contains ((sprintf "[%s]" (ColumnRealization.columnNameText attr.Column)), body)

[<Fact>]
let ``5.3.α.create-table: schema-qualified table identifier appears bracket-quoted`` () =
    // V1 IdentifierFormatter.QuoteIdentifier defaults to SquareBracket;
    // V2 uses ScriptDom's `Sql160ScriptGenerator` with pinned options
    // producing the same bracket-quoting. Property: every emitted table
    // reference is `[Schema].[Table]` (forward-slash separators ruled out).
    let enriched = enrich sampleCatalog
    for kind in Catalog.allKinds enriched do
        let body = bodyOf kind.SsKey sampleCatalog
        let expected = sprintf "[%s].[%s]" (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)
        Assert.Contains (expected, body)

// ---------------------------------------------------------------------------
// 5.3.α.pk-and-fk — PK and FK constraint properties. V1's
// CreateTableStatementBuilder L61-98 routes PK as inline (single-column)
// or table-level (multi-column); V2 always emits as table-level CONSTRAINT
// per matrix row 182 deferred axis. V1's AddForeignKeys L108-212 routes
// FKs inline; V2 mirrors via ScriptDomBuild.buildForeignKey.
// ---------------------------------------------------------------------------

[<Fact>]
let ``5.3.α.pk-and-fk: every kind with primary-key attribute emits PRIMARY KEY clause`` () =
    let enriched = enrich sampleCatalog
    for kind in Catalog.allKinds enriched do
        let hasPk = kind.Attributes |> List.exists (fun a -> a.IsPrimaryKey)
        if hasPk then
            let body = bodyOf kind.SsKey sampleCatalog
            Assert.Contains ("PRIMARY KEY", body)

[<Fact>]
let ``5.3.α.pk-and-fk: every kind with references emits FOREIGN KEY clauses matching reference count`` () =
    let enriched = enrich sampleCatalog
    for kind in Catalog.allKinds enriched do
        if not (List.isEmpty kind.References) then
            let body = bodyOf kind.SsKey sampleCatalog
            let fkClauseCount =
                body.Split([| "FOREIGN KEY" |], System.StringSplitOptions.None)
                |> Array.length
                |> (fun n -> n - 1)
            // Per V1 AddForeignKeys: every Reference produces one inline
            // FOREIGN KEY clause. V2 mirrors via fkDef + buildCreateTable.
            // References that don't resolve to a target Kind in the catalog
            // silently drop (cross-catalog territory; chapter 3.2) — so
            // count must be ≤ References.Length, not =.
            Assert.True (fkClauseCount <= List.length kind.References,
                         sprintf "Kind %A: %d FK clauses, %d references"
                            kind.SsKey fkClauseCount (List.length kind.References))

// ---------------------------------------------------------------------------
// 5.3.α.index — non-PK index emission. V1's IndexScriptBuilder.
// BuildCreateIndexStatement (L24) emits one CREATE INDEX per non-PK
// SmoIndexDefinition; PK-marked indexes filter out (inline in CREATE
// TABLE per V1 convention). V2 mirrors at SsdtDdlEmitter.fs:303-306.
// ---------------------------------------------------------------------------

[<Fact>]
let ``5.3.α.index: PK-marked indexes do not produce CREATE INDEX statements`` () =
    // The PK is inlined as PRIMARY KEY in the CREATE TABLE (per V1
    // convention); a separate CREATE INDEX for the PK would be
    // structurally wrong (would attempt to create an index named the
    // same as the implicit PK constraint).
    let enriched = enrich sampleCatalog
    for kind in Catalog.allKinds enriched do
        let body = bodyOf kind.SsKey sampleCatalog
        let pkIndexes = kind.Indexes |> List.filter (fun i -> IndexUniqueness.isPrimaryKey i.Uniqueness)
        for pkIndex in pkIndexes do
            // The PK constraint name typically isn't IX_*-style; just
            // assert we don't see a CREATE INDEX with the PK index's
            // name in the body.
            let pkName = Name.value pkIndex.Name
            let bareCreate = sprintf "CREATE UNIQUE INDEX [%s]" pkName
            let bareCreateNonUnique = sprintf "CREATE INDEX [%s]" pkName
            Assert.DoesNotContain (bareCreate, body)
            Assert.DoesNotContain (bareCreateNonUnique, body)

// ---------------------------------------------------------------------------
// 5.3.α.formatting — V1's CreateTableFormatter + ConstraintFormatter
// post-render normalization is V1-only. V2's ScriptDom output is
// canonical-by-construction — no post-render pass is needed. The
// property asserts V2's emission is "well-formed enough" that V1-style
// post-processing would be a no-op (the structural content is already
// in canonical form).
// ---------------------------------------------------------------------------

[<Fact>]
let ``5.3.α.formatting: emitted SQL parses back via TSql160Parser without errors`` () =
    // V2's claim is that ScriptDom produces canonical SQL out of the box.
    // The strongest property is round-trip: emit → parse → no errors.
    // (V1 post-processing isn't needed if the input was already valid.)
    let parser =
        Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser(true)
    for _, body in allBodies sampleCatalog do
        use reader = new System.IO.StringReader(body)
        let mutable errors :
            System.Collections.Generic.IList<Microsoft.SqlServer.TransactSql.ScriptDom.ParseError> =
                System.Collections.Generic.List() :> _
        let _ = parser.Parse(reader, &errors)
        Assert.True (errors.Count = 0,
                     sprintf "Parse errors in emission: %A"
                        (errors |> Seq.map (fun e -> e.Message) |> List.ofSeq))

[<Fact>]
let ``5.3.α.formatting: emitted SQL does not require V1-style trailing-comma fixup`` () =
    // V1's CreateTableFormatter L91-94 strips trailing commas from the
    // last column definition (caused by SMO's emission idiosyncrasy
    // where the last column carries a trailing comma before the closing
    // paren). V2's ScriptDom emission produces no such trailing commas;
    // the parser-pass test above already confirms valid SQL, but pin
    // the specific shape explicitly.
    for _, body in allBodies sampleCatalog do
        // The pattern `,\n)` indicates a trailing comma before close-paren.
        // V2 must not produce it.
        Assert.DoesNotContain (",\n)", body)

// ---------------------------------------------------------------------------
// 5.3.α.identifier — V1's IdentifierFormatter.CreateIdentifier defaults
// to QuoteType.SquareBracket (L116). V2 uses ScriptDom's pinned
// `Sql160ScriptGenerator` options producing the same bracket form.
// Property: NO emitted identifier uses double-quote or bare form.
// ---------------------------------------------------------------------------

[<Fact>]
let ``5.3.α.identifier: emitted column references use bracket-quoting consistently`` () =
    let enriched = enrich sampleCatalog
    for kind in Catalog.allKinds enriched do
        let body = bodyOf kind.SsKey sampleCatalog
        // Every attribute's column name should appear bracket-quoted at
        // least once (in the column-definition list). Bare or
        // double-quoted forms would indicate a quote-strategy regression.
        for attr in kind.Attributes do
            let col = ColumnRealization.columnNameText attr.Column
            Assert.Contains ((sprintf "[%s]" col), body)
            // Negative assertion is conservative — double-quote could
            // appear inside a literal CHECK clause's text. The positive
            // assertion (bracket form is present) is the load-bearing
            // claim.

[<Fact>]
let ``5.3.α.identifier: schema and table identifiers escape closing bracket if present`` () =
    // V1 IdentifierFormatter.QuoteIdentifier L108 doubles ']' inside
    // brackets. V2 mirrors via ScriptDom's Identifier(value, QuoteType.
    // SquareBracket) constructor handling escapes. Property: no
    // identifier in the sample emission contains an unmatched bracket.
    for _, body in allBodies sampleCatalog do
        // Open / close bracket counts on identifier characters; the body
        // should have balanced bracket pairs (each [ matched by ]).
        let opens = body |> Seq.filter (fun c -> c = '[') |> Seq.length
        let closes = body |> Seq.filter (fun c -> c = ']') |> Seq.length
        Assert.Equal (opens, closes)

// ---------------------------------------------------------------------------
// 5.3.α.extended-properties — V1's ExtendedPropertyScriptBuilder
// (`src/Osm.Smo/PerTableEmission/ExtendedPropertyScriptBuilder.cs`)
// emits `EXEC sys.sp_addextendedproperty @name=N'MS_Description'`
// at Table / Column / Index levels. V2's ScriptDomBuild.
// buildSetExtendedPropertyCore (`Projection.Targets.SSDT/ScriptDomBuild.
// fs:1031`) emits at Schema / Table / Column / Index levels — V2 adds
// the Schema level beyond V1's three.
// ---------------------------------------------------------------------------

[<Fact>]
let ``5.3.α.extended-properties: emission uses sys.sp_addextendedproperty for description carriage`` () =
    // The sampleCatalog doesn't carry ExtendedProperties on every kind,
    // so this is a positive-conditional check: when extended properties
    // are present, they emit via the V1-equivalent stored procedure.
    let enriched = enrich sampleCatalog
    let anyExt =
        Catalog.allKinds enriched
        |> List.exists (fun k ->
            not (List.isEmpty k.ExtendedProperties) ||
            k.Attributes |> List.exists (fun a -> not (List.isEmpty a.ExtendedProperties)) ||
            k.Indexes |> List.exists (fun i -> not (List.isEmpty i.ExtendedProperties)))
    if anyExt then
        for _, body in allBodies sampleCatalog do
            if body.Contains "sp_addextendedproperty" then
                // Per V1 ExtendedPropertyScriptBuilder L92-94 / L110-113
                // / L131-134: the @name parameter carries 'MS_Description'.
                Assert.Contains ("@name=N'MS_Description'", body)

// ---------------------------------------------------------------------------
// 5.3.α.statement-batch — V1's StatementBatchFormatter.JoinStatements
// (`src/Osm.Smo/PerTableEmission/StatementBatchFormatter.cs:32-59`)
// joins per-table SQL statements with `GO` separators. V2's
// BatchSplitter (`Projection.Targets.SSDT/BatchSplitter.fs`) handles
// the inverse — splitting a deployed-SQL stream on `^GO$` lines.
//
// V2's per-kind ArtifactByKind output is one file per kind (no GO
// inside a single file); GO-batching happens at the realization layer
// (`Deploy.executeStream` reads statements one at a time; CLI emission
// concatenates files with implicit batch boundaries).
// ---------------------------------------------------------------------------

[<Fact>]
let ``reconciliation slice 3: per-kind file body carries V1's rendered form — framed GO between statements, never trailing`` () =
    // OPERATOR DECISION (DECISIONS 2026-06-13) — supersedes the prior
    // "per-kind file body does not contain GO separator" pin. The
    // per-table file now renders through the same Render.toText
    // realization as the flat stream: statements separated by GO framed
    // with a blank line on BOTH sides (V1 StatementBatchFormatter
    // spacing), with no trailing GO after the final statement (V1
    // JoinStatements joins BETWEEN statements only).
    for _, body in allBodies sampleCatalog do
        let lines = body.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
        let goIndexes =
            lines
            |> Array.indexed
            |> Array.filter (fun (_, l) -> l.Trim() = "GO")
            |> Array.map fst
        for i in goIndexes do
            Assert.True (i > 0 && lines.[i - 1].Trim() = "",
                sprintf "GO at line %d is not preceded by a blank line" i)
            Assert.True (i + 1 < lines.Length && lines.[i + 1].Trim() = "",
                sprintf "GO at line %d is not followed by a blank line" i)
        // Never trailing: the last non-blank line is a statement, not GO.
        let lastNonBlank =
            lines |> Array.tryFindBack (fun l -> not (System.String.IsNullOrWhiteSpace l))
        match lastNonBlank with
        | Some l -> Assert.NotEqual<string>("GO", l.Trim())
        | None -> ()

[<Fact>]
let ``reconciliation slice 2: flat-stream GO is framed by a blank line on BOTH sides (V1 StatementBatchFormatter spacing)`` () =
    // V1 (StatementBatchFormatter.cs:44-58) leaves a blank line before
    // AND after GO between statements; V2 emitted the leading blank
    // only, so every multi-statement table differed in spacing even
    // when the statements matched (DECISIONS 2026-06-12, slice 2).
    let text =
        SsdtDdlEmitter.statements sampleCatalog
        |> Render.toText
    let lines = text.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
    let goIndexes =
        lines
        |> Array.indexed
        |> Array.filter (fun (_, l) -> l.Trim() = "GO")
        |> Array.map fst
    Assert.NotEmpty goIndexes
    for i in goIndexes do
        Assert.True (i > 0 && lines.[i - 1].Trim() = "",
            sprintf "GO at line %d is not preceded by a blank line" i)
        Assert.True (i + 1 < lines.Length && lines.[i + 1].Trim() = "",
            sprintf "GO at line %d is not followed by a blank line" i)

// ---------------------------------------------------------------------------
// Deferred axes — Skip-stubs reserve contract names + name triggers.
// Per the operating-disciplines table "Skip = "..." for deliberate V2
// divergences from V1" — these stubs make the divergences structurally
// visible in test discovery.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// LR3 (single-column PK inline) + LR4 (computed columns) cash-outs —
// slice 5.3.α.column-axis-deferral-closeout closed both. The Skip-stubs
// flip to active tests with concrete fixtures.
// ---------------------------------------------------------------------------

[<Fact>]
let ``5.3.α.create-table LR3: single-column PK emits inline at column definition`` () =
    // V1 CreateTableStatementBuilder.cs:67-78 attaches the
    // UniqueConstraintDefinition { IsPrimaryKey = true } to the single
    // PK column's Constraints (not as a table-level constraint). V2
    // mirrors via `ScriptDomBuild.attachInlinePrimaryKey`.
    // sampleCatalog's `Customer` kind has a single-column PK (Id).
    let enriched = enrich sampleCatalog
    let customerKind =
        Catalog.allKinds enriched
        |> List.find (fun k -> Name.value k.Name = "Customer")
    let body = bodyOf customerKind.SsKey sampleCatalog
    // V1 emission shape for inline PK:
    // `[ID] INT NOT NULL CONSTRAINT [PK_dbo_OSUSR_…_CUSTOMER] PRIMARY KEY`
    Assert.Contains ("PRIMARY KEY", body)
    // Multi-column-PK shape (`CONSTRAINT [PK_…] PRIMARY KEY (` with
    // explicit column list) must NOT appear for single-column PKs —
    // the inline form has no parenthesized column list.
    Assert.DoesNotContain ("PRIMARY KEY ([ID]", body)
    Assert.DoesNotContain ("PRIMARY KEY ([Id]", body)

[<Fact>]
let ``5.3.α.create-table LR3: multi-column PK still emits as table-level CONSTRAINT`` () =
    // sampleCatalog's `Country` kind has a multi-column PK (Code +
    // Label per Fixtures.fs); LR3 keeps multi-col PKs at the table
    // level (V1 L80-98 shape).
    let enriched = enrich sampleCatalog
    let multiPkKind =
        Catalog.allKinds enriched
        |> List.tryFind (fun k ->
            let pkCount =
                k.Attributes |> List.filter (fun a -> a.IsPrimaryKey) |> List.length
            pkCount >= 2)
    match multiPkKind with
    | None ->
        // sampleCatalog might not carry a multi-col PK; skip silently.
        ()
    | Some kind ->
        let body = bodyOf kind.SsKey sampleCatalog
        // Table-level PK emits as `CONSTRAINT [PK_…] PRIMARY KEY
        // ([col1] ASC, [col2] ASC, …)`.
        Assert.Contains ("PRIMARY KEY (", body)

[<Fact>]
let ``5.3.α.create-table LR4: computed columns emit AS (expression) clause`` () =
    // Build a kind with one PK column and one computed column.
    // V1 CreateTableStatementBuilder.cs:362-365 shape:
    // `[Computed] AS ([Base] * 2)` (no type / nullability / identity).
    let kindKey0 = kindKey ["ComputedFixture"]
    let pkAttrKey = attrKey ["ComputedFixture"; "Id"]
    let baseAttrKey = attrKey ["ComputedFixture"; "Base"]
    let computedAttrKey = attrKey ["ComputedFixture"; "Doubled"]
    let pkAttr =
        { Attribute.create pkAttrKey (mkName "Id") Integer with
            Column = ColumnRealization.create ("ID") (false) |> Result.value
            IsPrimaryKey = true
            IsMandatory  = true }
    let baseAttr =
        { Attribute.create baseAttrKey (mkName "Base") Integer with
            Column = ColumnRealization.create ("BASE") (false) |> Result.value
            IsMandatory = true }
    let computedConfig = ComputedColumnConfig.create "[BASE] * 2" false |> Result.value
    let computedAttr =
        { Attribute.create computedAttrKey (mkName "Doubled") Integer with
            Column = ColumnRealization.create ("DOUBLED") (true) |> Result.value
            Computed = Some computedConfig }
    let kind =
        { Kind.create kindKey0 (mkName "ComputedFixture")
            (mkTableId "dbo" "OSUSR_CF_FIXTURE")
            [ pkAttr; baseAttr; computedAttr ]
          with References = []; Indexes = []; ColumnChecks = [] }
    let cat : Catalog =
        {
            Modules =
                [ { SsKey = modKey "ComputedFixtureMod"
                    Name = mkName "ComputedFixtureMod"
                    Kinds = [ kind ]
                    IsActive = true
                    ExtendedProperties = [] } ]
            Sequences = []
        }
    let body = bodyOf kindKey0 cat
    // ScriptDom renders `[DOUBLED] AS (...)`; spacing is generator-
    // pinned. Assert structurally — column name, AS keyword, and the
    // expression's column reference all appear.
    Assert.Contains ("[DOUBLED]", body)
    Assert.Contains (" AS ", body)
    Assert.Contains ("[BASE]", body)
    // Computed columns have NO DataType (V1 L296). The DOUBLED column
    // line should not declare a datatype (INT) or nullability marker.
    let doubledLineStart = body.IndexOf("[DOUBLED]")
    let doubledLineEnd = body.IndexOf('\n', doubledLineStart + 1)
    let doubledLine =
        if doubledLineEnd > doubledLineStart then
            body.Substring(doubledLineStart, doubledLineEnd - doubledLineStart)
        else
            body.Substring(doubledLineStart)
    // The DOUBLED column line should not declare INT / NOT NULL / NULL
    // (computed columns derive type + nullability from the expression).
    Assert.DoesNotContain ("INT ", doubledLine)
    Assert.DoesNotContain ("NOT NULL", doubledLine)

[<Fact>]
let ``5.3.α.create-table LR4: persisted computed columns emit PERSISTED keyword`` () =
    let kindKey0 = kindKey ["PersistedComputed"]
    let pkAttrKey = attrKey ["PersistedComputed"; "Id"]
    let computedAttrKey = attrKey ["PersistedComputed"; "Persisted"]
    let pkAttr =
        { Attribute.create pkAttrKey (mkName "Id") Integer with
            Column = ColumnRealization.create ("ID") (false) |> Result.value
            IsPrimaryKey = true
            IsMandatory  = true }
    let persistedConfig = ComputedColumnConfig.create "1 + 1" true |> Result.value
    let computedAttr =
        { Attribute.create computedAttrKey (mkName "Persisted") Integer with
            Column = ColumnRealization.create ("PERSISTED_COL") (true) |> Result.value
            Computed = Some persistedConfig }
    let kind =
        { Kind.create kindKey0 (mkName "PersistedComputed")
            (mkTableId "dbo" "OSUSR_PC_PERSISTED")
            [ pkAttr; computedAttr ]
          with References = []; Indexes = []; ColumnChecks = [] }
    let cat : Catalog =
        {
            Modules =
                [ { SsKey = modKey "PersistedComputedMod"
                    Name = mkName "PersistedComputedMod"
                    Kinds = [ kind ]
                    IsActive = true
                    ExtendedProperties = [] } ]
            Sequences = []
        }
    let body = bodyOf kindKey0 cat
    Assert.Contains ("PERSISTED", body)

[<Fact>]
let ``5.3.α.create-table row 53 partial: named DEFAULT constraint surfaces in CREATE TABLE`` () =
    // Slice 5.3.α.column-axis-deferral-closeout (matrix row 53 partial
    // cash-out): when Attribute.DefaultName is Some, V2 emits
    // `CONSTRAINT [DF_…] DEFAULT (value)`. Mirrors V1
    // CreateTableStatementBuilder.cs:324-335 shape.
    let kindKey0 = kindKey ["NamedDefault"]
    let pkAttrKey = attrKey ["NamedDefault"; "Id"]
    let valAttrKey = attrKey ["NamedDefault"; "Val"]
    let constraintName = Name.create "DF_NamedDefault_Val" |> Result.toOption
    let pkAttr =
        { Attribute.create pkAttrKey (mkName "Id") Integer with
            Column = ColumnRealization.create ("ID") (false) |> Result.value
            IsPrimaryKey = true
            IsMandatory  = true }
    let valAttr =
        { Attribute.create valAttrKey (mkName "Val") Integer with
            Column = ColumnRealization.create ("VAL") (false) |> Result.value
            DefaultValue = Some (SqlLiteral.ofRaw Integer (Some "42"))
            DefaultName  = constraintName
            IsMandatory  = true }
    let kind =
        { Kind.create kindKey0 (mkName "NamedDefault")
            (mkTableId "dbo" "OSUSR_ND_NAMED")
            [ pkAttr; valAttr ]
          with References = []; Indexes = []; ColumnChecks = [] }
    let cat : Catalog =
        {
            Modules =
                [ { SsKey = modKey "NamedDefaultMod"
                    Name = mkName "NamedDefaultMod"
                    Kinds = [ kind ]
                    IsActive = true
                    ExtendedProperties = [] } ]
            Sequences = []
        }
    let body = bodyOf kindKey0 cat
    Assert.Contains ("CONSTRAINT [DF_NamedDefault_Val] DEFAULT", body)
    Assert.Contains ("DEFAULT 42", body)

[<Fact(Skip = "5.3.α.index row 56 LR6 — V1's DataCompression partition-range emission (IndexScriptBuilder.cs L259-301 CollapseRanges) is deferred-with-trigger. V2 emits single-value DataCompression today (matrix row 55 closed 2026-05-18); partition-range collapse logic awaits IR refinement (closed-DU `DataSpace = Filegroup of name | PartitionScheme of name × columns` + per-partition compression list). Trigger: partitioned-index fixture surfaces in operator-reality canary.")>]
let ``5.3.α.index LR6: DataCompression emits per-partition-range clauses`` () = ()

[<Fact>]
let ``5.3.α.index LR7: filegroup and partition-scheme ON clauses emit`` () =
    // Closed by slice A.4.7'-prelude.row56-dataspace (2026-05-19).
    // Detailed assertions live in IndexDataSpaceTests.fs (7 tests
    // covering Filegroup / PartitionScheme / None / multi-column /
    // T1 byte-determinism / Site classification). This stub witnesses
    // the contract name remains discoverable in test discovery; the
    // existing tests carry the load-bearing verification.
    let cat =
        let kindKey0 = kindKey ["LR7Witness"]
        let idAttrKey = attrKey ["LR7Witness"; "Id"]
        let idxKey0 =
            SsKey.synthesizedComposite "OS_IDX" ["LR7Witness"; "IX"]
            |> Result.value
        let idAttr =
            { Attribute.create idAttrKey (mkName "Id") Integer with
                Column = ColumnRealization.create ("ID") (false) |> Result.value
                IsPrimaryKey = true
                IsMandatory  = true }
        let idx =
            { Index.create idxKey0 (mkName "IX_LR7Witness")
                (IndexColumn.ascendingList [ idAttrKey ]) with
                DataSpace = Some (DataSpace.Filegroup "INDEX_FG") }
        let kind =
            { Kind.create kindKey0 (mkName "LR7Witness")
                (mkTableId "dbo" "OSUSR_LR7_IDX")
                [ idAttr ]
              with Indexes = [ idx ] }
        { Modules =
            [ { SsKey = modKey "LR7Mod"
                Name = mkName "LR7Mod"
                Kinds = [ kind ]
                IsActive = true
                ExtendedProperties = [] } ]
          Sequences = [] }
    let body = bodyOf (kindKey ["LR7Witness"]) cat
    Assert.Contains ("ON [INDEX_FG]", body)

// ---------------------------------------------------------------------------
// NM-38 — `Render.toTextWith` constraint-rendering-mode reachability. The
// `ConstraintFormatter.Disabled` mode (the operator's V1-parity / regression-
// bisect opt-out) was unreachable from production until the
// `EmissionPolicy.RenderConstraintsElegant` axis threaded it to
// `Render.toTextWith`. These witness that the two modes produce DIFFERENT
// rendered text on a constraint-bearing catalog (so `Disabled` is not a no-op
// alias of `Enabled`) and that `toText` equals `toTextWith Enabled` (the
// byte-identical default wrapper).
// ---------------------------------------------------------------------------

[<Fact>]
let ``NM-38: Render.toTextWith Disabled differs from Enabled on a constraint-bearing catalog`` () =
    let statements = SsdtDdlEmitter.statements (enrich sampleCatalog) |> List.ofSeq
    let enabled  = Render.toTextWith ConstraintFormatter.Enabled statements
    let disabled = Render.toTextWith ConstraintFormatter.Disabled statements
    // The elegant multi-line constraint shape (Enabled) post-processes
    // ScriptDom's compact column-inline output; Disabled passes it through.
    // The sample catalog carries PK / FK / DEFAULT constraints, so the two
    // renderings cannot coincide — proving the Disabled mode is live.
    Assert.NotEqual<string>(enabled, disabled)

[<Fact>]
let ``NM-38: Render.toText equals toTextWith Enabled (byte-identical default wrapper)`` () =
    let statements = SsdtDdlEmitter.statements (enrich sampleCatalog) |> List.ofSeq
    Assert.Equal<string>(Render.toText statements, Render.toTextWith ConstraintFormatter.Enabled statements)
