namespace Projection.Tests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Targets.Data
open Projection.Targets.SSDT
open Projection.Pipeline

// ---------------------------------------------------------------------------
// Chapter 4.1.B slice γ — CDC silence on idempotent redeploy.
//
// V2_DRIVER.md per-axis correctness stakes table places this at the
// HIGHEST stakes: the cutover team must trust that V2's redeploy
// pipeline does not fire spurious CDC capture entries on identical-
// content redeploys, because consuming production features depend on
// CDC for change detection.
//
// V1's MERGE shape unconditionally fires `WHEN MATCHED THEN UPDATE
// SET ...` (`StaticSeedSqlBuilder.cs:237`), which fires CDC capture
// rows even when the row content is unchanged. V2's chapter 4.1.B
// slice β added the change-detection predicate — `WHEN MATCHED AND
// (<per-column-difference-OR-chain>) THEN UPDATE SET ...` — that
// suppresses the UPDATE when source and target are identical.
//
// This canary verifies the property OPERATIONALLY under real SQL
// Server CDC. Sequence:
//
//   1. Deploy schema (via RawTextEmitter.emit)
//   2. Enable CDC on database + table (`sys.sp_cdc_enable_db`,
//      `sys.sp_cdc_enable_table`)
//   3. First seed deploy → INSERTs via the MERGE's `WHEN NOT MATCHED`
//      branch
//   4. Force capture via `sys.sp_cdc_scan` (Agent-free synchronous
//      capture; works in the warm container)
//   5. Capture `cdc.<schema>_<table>_CT` baseline row count
//   6. Second seed deploy of identical content (the property under test)
//   7. Force capture again
//   8. Capture post-redeploy CDC table row count
//   9. ASSERT: post == baseline (zero new capture rows)
//
// If the assertion fails, the change-detection predicate has drifted
// from semantic correctness OR the IR is producing a different MERGE
// shape on second invocation.
//
// **Fixture-lift (slice A.4.7'-prelude.test-fixture-lift, 2026-05-19).**
// xUnit `IClassFixture<IsolatedContainerFixture>` shares one
// ephemeral CDC-suitable container across both tests in this class;
// per-test `WithEphemeralDatabase` lifecycle preserves the per-
// scenario CDC isolation (CDC infrastructure stays at the database
// level, not the master level). The `Docker-SqlServer` collection's
// `DisableParallelization` keeps sibling classes off this container's
// instance.
// ---------------------------------------------------------------------------

module private CdcSilenceFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn "SKIP %s: Docker daemon not reachable." label
            false

    let mustOk r =
        match r with
        | Ok v -> v
        | Error es -> invalidOp (sprintf "fixture: %A" es)

    let mkKey (parts: string list) : SsKey =
        SsKey.synthesizedComposite "OS_TEST_CDC" parts |> mustOk

    /// Per slice-5 lift: force-unwrap fixture TableId.
    let mkTableId (schema: string) (table: string) : TableId =
        TableId.create schema table |> mustOk

    let mkName (s: string) : Name =
        Name.create s |> mustOk

    /// Single-table fixture: a CDC-tracked Static-modality kind with two
    /// rows. Schema `dbo`, table `OSUSR_CDC_COUNTRY`. Three columns:
    /// Id (PK, INT), Code (nvarchar), Label (nvarchar). Kept tight so
    /// the capture-table name is predictable: `cdc.dbo_OSUSR_CDC_COUNTRY_CT`.
    let buildFixture () : Catalog * Kind =
        let kindKey = mkKey ["Country"]
        let idKey   = mkKey ["Country"; "Id"]
        let codeKey = mkKey ["Country"; "Code"]
        let labelKey = mkKey ["Country"; "Label"]
        let row code label =
            { Identifier = mkKey ["Country"; "Row"; code]
              Values =
                  Map.ofList
                      [ mkName "Id",    code
                        mkName "Code",  code
                        mkName "Label", label ] }
        let kind : Kind =
            { SsKey    = kindKey
              Name     = mkName "Country"
              Origin   = Native
              Modality = [ Static [ row "1" "United States"
                                    row "2" "Canada" ] ]
              Physical = mkTableId "dbo" "OSUSR_CDC_COUNTRY"
              Attributes =
                  [
                      { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                      { Attribute.create codeKey (mkName "Code") Text with Column = ColumnRealization.create ("CODE") (false) |> Result.value; IsMandatory = true }
                      { Attribute.create labelKey (mkName "Label") Text with Column = ColumnRealization.create ("LABEL") (false) |> Result.value; IsMandatory = true }
                  ]
              References = []
              Indexes    = []
              Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
        let m : Module =
            { SsKey = mkKey ["Module"]
              Name  = mkName "TestModule"
              Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
        { Modules = [ m ]; Sequences = [] }, kind

    let executeScalarInt (cnn: SqlConnection) (sql: string) : Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            cmd.CommandTimeout <- 0
            let! result = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt32 result
        }

    /// Convenience: build the schema SQL + seed SQL for a given catalog +
    /// CDC awareness. Encapsulates the artifact-→-text projection so test
    /// bodies stay focused on the property assertions.
    let renderArtifacts (catalog: Catalog) (kind: Kind) (cdcAwareness: CdcAwareness) : string * string =
        let schemaArtifact =
            match SsdtDdlEmitter.emitSlices catalog with
            | Ok a -> a
            | Error e -> failwithf "SsdtDdlEmitter.emitSlices: %A" e
        let schemaSql =
            schemaArtifact
            |> ArtifactByKind.toMap
            |> Map.toList
            |> List.map (fun (_, file) -> file.Body)
            |> String.concat "\nGO\n"  // LINT-ALLOW: terminal SQL-batch joiner across per-kind SsdtFile bodies; BCL `String.concat` IS the use-case-specific library; segments are typed (each `file.Body` is the rendered CREATE TABLE text from ScriptDomGenerate)
        let profile = { Profile.empty with CdcAwareness = cdcAwareness }
        let seedArtifact =
            match StaticSeedsEmitter.emit DataEmitOptions.defaults catalog profile with
            | Ok a -> a
            | Error e -> failwithf "StaticSeedsEmitter.emit: %A" e
        let seedSql =
            seedArtifact
            |> ArtifactByKind.toMap
            |> Map.find kind.SsKey
            |> fun s -> s.Rendered
        schemaSql, seedSql

    /// Variant fixture: same kind / schema as `buildFixture ()`, but with
    /// row 1's `Label` changed from "United States" → "USA". Used by the
    /// sensitivity test to confirm the canary actually observes UPDATEs
    /// when content changes (proves the property test isn't trivially
    /// passing because CDC is silent for unrelated reasons).
    let buildChangedFixture () : Catalog * Kind =
        let kindKey = mkKey ["Country"]
        let idKey   = mkKey ["Country"; "Id"]
        let codeKey = mkKey ["Country"; "Code"]
        let labelKey = mkKey ["Country"; "Label"]
        let row code label =
            { Identifier = mkKey ["Country"; "Row"; code]
              Values =
                  Map.ofList
                      [ mkName "Id",    code
                        mkName "Code",  code
                        mkName "Label", label ] }
        let kind : Kind =
            { SsKey    = kindKey
              Name     = mkName "Country"
              Origin   = Native
              Modality = [ Static [ row "1" "USA"          // <-- changed
                                    row "2" "Canada" ] ]
              Physical = mkTableId "dbo" "OSUSR_CDC_COUNTRY"
              Attributes =
                  [
                      { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                      { Attribute.create codeKey (mkName "Code") Text with Column = ColumnRealization.create ("CODE") (false) |> Result.value; IsMandatory = true }
                      { Attribute.create labelKey (mkName "Label") Text with Column = ColumnRealization.create ("LABEL") (false) |> Result.value; IsMandatory = true }
                  ]
              References = []
              Indexes    = []
              Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
        let m : Module =
            { SsKey = mkKey ["Module"]
              Name  = mkName "TestModule"
              Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
        { Modules = [ m ]; Sequences = [] }, kind

    // -----------------------------------------------------------------------
    // Track D (AC-D1 / D2.4 / D3.3) — per-type null-state-transition witnesses.
    // The null-safe MERGE predicate is type-agnostic in production but only
    // nvarchar/Text is exercised above; these fixtures add a NULLABLE column of
    // each PrimitiveType. SPIKE finding: the SSDT emitter reads
    // `Column.IsNullable` (not `IsMandatory`), so a nullable column MUST set
    // `ColumnRealization.create (...) (true)`. `SqlLiteral.ofRaw`'s `"" → NullLit`
    // sentinel is the single per-type NULL encoding each case discriminates.
    // -----------------------------------------------------------------------

    let buildNullableFixture (typ: PrimitiveType) (nullableRaw: string) : Catalog * Kind =
        let typeTag =
            match typ with
            | Integer  -> "INT"
            | Decimal  -> "DEC"
            | Boolean  -> "BIT"
            | DateTime -> "DTM"
            | Date     -> "DAT"
            | Time     -> "TIM"
            | Guid     -> "GUID"
            | Binary   -> "BIN"
            | Text     -> "TXT"
        let kindKey = mkKey ["Nullable"; typeTag]
        let idKey   = mkKey ["Nullable"; typeTag; "Id"]
        let valKey  = mkKey ["Nullable"; typeTag; "Val"]
        let row =
            { Identifier = mkKey ["Nullable"; typeTag; "Row"; "1"]
              Values =
                  Map.ofList
                      [ mkName "Id",  "1"
                        mkName "Val", nullableRaw ] }
        let valAttr =
            match typ with
            | Decimal ->
                { Attribute.create valKey (mkName "Val") Decimal with
                    Column = ColumnRealization.create ("VAL") (true) |> Result.value
                    Precision = Some 18; Scale = Some 4
                    IsMandatory = false }
            | Text ->
                { Attribute.create valKey (mkName "Val") Text with
                    Column = ColumnRealization.create ("VAL") (true) |> Result.value
                    Length = Some 100
                    IsMandatory = false }
            | _ ->
                { Attribute.create valKey (mkName "Val") typ with
                    Column = ColumnRealization.create ("VAL") (true) |> Result.value
                    IsMandatory = false }
        let kind : Kind =
            { SsKey    = kindKey
              Name     = mkName (System.String.Concat("Nullable_", typeTag))
              Origin   = Native
              Modality = [ Static [ row ] ]
              Physical = mkTableId "dbo" (System.String.Concat("OSUSR_CDC_NULLABLE_", typeTag))
              Attributes =
                  [
                      { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                      valAttr
                  ]
              References = []
              Indexes    = []
              Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
        let m : Module =
            { SsKey = mkKey ["Module"; typeTag]
              Name  = mkName (System.String.Concat("TestModule_", typeTag))
              Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
        { Modules = [ m ]; Sequences = [] }, kind

    /// `(PrimitiveType, value1, value2)` — two distinct non-NULL raw forms
    /// (canonical `RawValueCodec`) bracketing a NULL. `Time` excluded (no V2
    /// Time witness; bare-Time SSDT realization out of scope).
    let nullableTypeMatrix : (PrimitiveType * string * string) list =
        [ Integer,  "42",                                   "99"
          Decimal,  "1.5000",                               "2.7500"
          Boolean,  "true",                                 "false"
          DateTime, "2026-06-03 12:30:00.0000000",          "2026-06-04 09:15:00.0000000"
          Date,     "2026-06-03",                           "2026-06-04"
          Guid,     "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"
          Binary,   "CAFEBABE",                             "DEADBEEF"
          Text,     "alpha",                                "beta" ]

    // -----------------------------------------------------------------------
    // Track G (Round 4) — k>1 exact-count + MigrationDependencies live-CDC.
    // -----------------------------------------------------------------------

    /// n-row Delta fixture; `labelOf i` supplies row i's Label (so a base
    /// and a mutated variant differ only on the chosen rows).
    let buildDeltaFixture (n: int) (labelOf: int -> string) : Catalog * Kind =
        let kindKey = mkKey ["Delta"]
        let idKey   = mkKey ["Delta"; "Id"]
        let codeKey = mkKey ["Delta"; "Code"]
        let labelKey = mkKey ["Delta"; "Label"]
        let row i =
            { Identifier = mkKey ["Delta"; "Row"; string i]
              Values =
                  Map.ofList
                      [ mkName "Id",    string i
                        mkName "Code",  sprintf "C%02d" i
                        mkName "Label", labelOf i ] }
        let kind : Kind =
            { SsKey    = kindKey
              Name     = mkName "Delta"
              Origin   = Native
              Modality = [ Static [ for i in 1 .. n -> row i ] ]
              Physical = mkTableId "dbo" "OSUSR_CDC_DELTA"
              Attributes =
                  [
                      { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                      { Attribute.create codeKey (mkName "Code") Text with Column = ColumnRealization.create ("CODE") (false) |> Result.value; IsMandatory = true }
                      { Attribute.create labelKey (mkName "Label") Text with Column = ColumnRealization.create ("LABEL") (false) |> Result.value; IsMandatory = true }
                  ]
              References = []
              Indexes    = []
              Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
        let m : Module =
            { SsKey = mkKey ["Module"; "Delta"]
              Name  = mkName "DeltaModule"
              Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
        { Modules = [ m ]; Sequences = [] }, kind

    /// Country kind with no Static modality (Migration-channel populated) —
    /// the MigrationDependenciesEmitter live-CDC witness fixture.
    let mkMigCountryKind () : Kind =
        let kindKey = mkKey ["MigCountry"]
        let idKey   = mkKey ["MigCountry"; "Id"]
        let codeKey = mkKey ["MigCountry"; "Code"]
        let labelKey = mkKey ["MigCountry"; "Label"]
        { SsKey    = kindKey
          Name     = mkName "MigCountry"
          Origin   = Native
          Modality = []   // NOT static — Migration channel supplies rows
          Physical = mkTableId "dbo" "OSUSR_CDC_MIGCOUNTRY"
          Attributes =
              [
                  { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                  { Attribute.create codeKey (mkName "Code") Text with Column = ColumnRealization.create ("CODE") (false) |> Result.value; IsMandatory = true }
                  { Attribute.create labelKey (mkName "Label") Text with Column = ColumnRealization.create ("LABEL") (false) |> Result.value; IsMandatory = true }
              ]
          References = []
          Indexes    = []
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }

    let migCatalog (kind: Kind) : Catalog =
        let m : Module =
            { SsKey = mkKey ["Module"; "Mig"]
              Name  = mkName "MigModule"
              Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
        { Modules = [ m ]; Sequences = [] }

    let migRow (kindKey: SsKey) (id: string) (code: string) (label: string) : MigrationDependencyRow =
        { KindKey    = kindKey
          Identifier = mkKey ["MigCountry"; "Row"; id]
          Values =
              Map.ofList
                  [ mkName "Id",    id
                    mkName "Code",  code
                    mkName "Label", label ] }

    /// Render schema-SQL + MigrationDependencies-emitted seed-SQL — the mirror
    /// of `renderArtifacts` driving `MigrationDependenciesEmitter.emit`.
    let renderMigrationArtifacts
        (catalog: Catalog)
        (kind: Kind)
        (rows: MigrationDependencyRow list)
        (cdcAwareness: CdcAwareness)
        : string * string =
        let schemaArtifact =
            match SsdtDdlEmitter.emitSlices catalog with
            | Ok a -> a
            | Error e -> failwithf "SsdtDdlEmitter.emitSlices: %A" e
        let schemaSql =
            schemaArtifact
            |> ArtifactByKind.toMap
            |> Map.toList
            |> List.map (fun (_, file) -> file.Body)
            |> String.concat "\nGO\n"  // LINT-ALLOW: terminal SQL-batch joiner; mirror of renderArtifacts; segments are typed rendered DDL
        let profile = { Profile.empty with CdcAwareness = cdcAwareness }
        let context : MigrationDependencyContext = { Rows = rows }
        let seedArtifact =
            match MigrationDependenciesEmitter.emit DataEmitOptions.defaults catalog profile context with
            | Ok a -> a
            | Error e -> failwithf "MigrationDependenciesEmitter.emit: %A" e
        let seedSql =
            seedArtifact
            |> ArtifactByKind.toMap
            |> Map.find kind.SsKey
            |> fun s -> s.Rendered
        schemaSql, seedSql

open CdcSilenceFixtures

[<Xunit.Collection("Docker-SqlServer")>]
type CdcSilenceTests(fixture: IsolatedContainerFixture) =

    /// Shared scenario runner. Deploys schema, enables CDC, runs the
    /// `firstSeedSql` to populate (the baseline), then runs the
    /// `secondSeedSql` (the property under test). Returns
    /// `(baselineCount, postCount)`.
    let runScenario (firstSeedSql: string) (secondSeedSql: string) (kind: Kind) (schemaSql: string) : Task<int * int> =
        fixture.WithEphemeralDatabase "CdcSilence" (fun cnn _ -> task {
            // Unwrap the typed coordinate VOs to their raw identifier text.
            // `System.String.Concat` takes `object`, so concatenating the
            // `SchemaName` / `TableName` VOs directly leaks their ToString()
            // form (`SchemaName "dbo"`) into the SQL — `TableId.schemaText` /
            // `tableText` are the boundary-unwrap accessors (per the typed-VO
            // lift 2026-06-02; same pattern as CdcSilenceCrossEmitterTests).
            let schemaText = TableId.schemaText kind.Physical
            let tableText  = TableId.tableText kind.Physical
            do! Deploy.executeBatch cnn schemaSql
            do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_enable_db;"
            let enableTableSql =
                System.String.Concat(
                    "EXEC sys.sp_cdc_enable_table ",
                    "@source_schema=N'", schemaText, "', ",
                    "@source_name=N'", tableText, "', ",
                    "@role_name=NULL, ",
                    "@supports_net_changes=0;")
            do! Deploy.executeBatch cnn enableTableSql

            // Phase 1: first deploy populates rows.
            do! Deploy.executeBatch cnn firstSeedSql
            do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_scan;"

            let captureTable =
                System.String.Concat(
                    "cdc.[",
                    schemaText, "_", tableText,
                    "_CT]")
            let countSql =
                System.String.Concat("SELECT COUNT(*) FROM ", captureTable, ";")
            let! baselineCount = executeScalarInt cnn countSql

            // Phase 2: second deploy. The property under test is whatever
            // this MERGE does to the CDC capture table relative to baseline.
            do! Deploy.executeBatch cnn secondSeedSql
            do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_scan;"

            let! postCount = executeScalarInt cnn countSql
            return baselineCount, postCount
        })

    interface IClassFixture<IsolatedContainerFixture>

    [<Fact>]
    member _.``Slice γ: CDC-silence — V2 change-detection predicate emits zero CDC capture rows on idempotent redeploy`` () =
        if not (skipIfNoDocker "cdc-silence") then () else

        let catalog, kind = buildFixture ()
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql, seedSql = renderArtifacts catalog kind cdcAware

        // Sanity-guard: the MERGE deployed must carry the change-detection
        // predicate. If slice β ever regressed to V1's unconditional WHEN
        // MATCHED, this canary must not silently pass — surface the
        // structural regression in the assertion message.
        Assert.Contains ("WHEN MATCHED AND (", seedSql)

        let baseline, post =
            (runScenario seedSql seedSql kind schemaSql).GetAwaiter().GetResult()

        // Baseline establishes that the initial INSERT phase fired CDC
        // entries (otherwise the test isn't actually exercising CDC).
        // Two rows × one INSERT each = 2 capture entries minimum.
        Assert.True (baseline >= 2,
            sprintf "expected baseline ≥ 2 CDC entries from initial INSERTs; got %d" baseline)

        // THE LOAD-BEARING ASSERTION: idempotent redeploy adds zero new
        // CDC entries. The property holds for two reasons in modern SQL
        // Server (defense-in-depth):
        //   1. V2's change-detection predicate gates UPDATE on actual
        //      column-level differences — primary structural fix.
        //   2. SQL Server 2022's MERGE→CDC pipeline empirically does not
        //      capture no-op UPDATEs even when the predicate is absent
        //      (see counter-test below). Belt-and-suspenders if MS ever
        //      changes that optimization or the cutover targets older
        //      SQL Server versions where it doesn't hold.
        Assert.Equal (baseline, post)

    [<Fact>]
    member _.``OB-D4.2: exactly one changed row produces exactly +2 CDC capture rows`` () =
        if not (skipIfNoDocker "cdc-exact-count") then () else

        // WHY EXACT (not the `post > baseline` inequality of the sensitivity
        // test above): the inequality is *also* satisfied by a V1-shape
        // unconditional `WHEN MATCHED THEN UPDATE SET ...` MERGE, which
        // over-captures by re-UPDATEing rows that didn't change. The
        // inequality therefore cannot tell V2's change-detection predicate
        // apart from V1 over-capture — it stays green under the regression.
        // This exact pin closes that phantom-green: the table is enabled
        // with @supports_net_changes=0, so CDC logs a single-row UPDATE as a
        // delete+insert PAIR = exactly 2 capture rows. One changed row ⇒
        // post = baseline + 2. An over-capturing WHEN MATCHED would touch
        // BOTH rows (4 capture rows) and turn this assertion RED.
        let initialCatalog, kind = buildFixture ()
        let changedCatalog, _ = buildChangedFixture ()
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql, initialSeedSql = renderArtifacts initialCatalog kind cdcAware
        let _, changedSeedSql = renderArtifacts changedCatalog kind cdcAware

        let baseline, post =
            (runScenario initialSeedSql changedSeedSql kind schemaSql).GetAwaiter().GetResult()

        // THE LOAD-BEARING EXACT ASSERTION: exactly one changed row ⇒
        // exactly +2 CDC capture rows (the delete+insert pair under
        // @supports_net_changes=0). The unconditional-WHEN-MATCHED
        // regression makes this RED.
        Assert.Equal (baseline + 2, post)

    [<Fact>]
    member _.``Slice γ sensitivity: changed-content redeploy DOES fire CDC capture rows — proves the canary mechanism is real (not silent for unrelated reasons)`` () =
        if not (skipIfNoDocker "cdc-silence-sensitivity") then () else

        // The positive test above passes because BOTH (a) V2's change-
        // detection predicate works AND (b) SQL Server 2022's MERGE→CDC
        // pipeline doesn't capture no-op UPDATEs even from V1-shape MERGE
        // (empirical surprise discovered while building this canary). Two
        // explanations could keep that test trivially passing if the
        // canary mechanism is broken:
        //   1. CDC isn't actually enabled in the warm container's per-
        //      database setup.
        //   2. `sys.sp_cdc_scan` isn't actually capturing.
        //   3. The capture-table name resolution is wrong.
        //
        // This sensitivity test rules them out: it deploys initial seeds,
        // then redeploys seeds with row 1's Label changed ("United States"
        // → "USA"). The change-detection predicate observes the diff and
        // fires the UPDATE; SQL Server logs it; CDC captures it; our
        // count probes detect it. If post > baseline, the canary
        // mechanism IS observing real CDC traffic when traffic exists.
        let initialCatalog, kind = buildFixture ()
        let changedCatalog, _ = buildChangedFixture ()
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql, initialSeedSql = renderArtifacts initialCatalog kind cdcAware
        let changedSchemaSql, changedSeedSql = renderArtifacts changedCatalog kind cdcAware

        // Sanity-guard the schema is identical across fixtures — the only
        // difference is row content.
        Assert.Equal<string> (schemaSql, changedSchemaSql)
        // The seed SQL MUST differ (one row changed).
        Assert.NotEqual<string> (initialSeedSql, changedSeedSql)

        let baseline, post =
            (runScenario initialSeedSql changedSeedSql kind schemaSql).GetAwaiter().GetResult()

        Assert.True (baseline >= 2,
            sprintf "expected baseline ≥ 2 CDC entries from initial INSERTs; got %d" baseline)

        // THE SENSITIVITY ASSERTION: redeploying with one changed row
        // MUST fire at least one new CDC entry. If post == baseline, the
        // canary's CDC plumbing is silent and the positive test's
        // "Equal(baseline, post)" assertion above is uninformative.
        Assert.True (post > baseline,
            sprintf "expected CDC entries to fire on changed-content redeploy; baseline=%d post=%d (canary mechanism may be broken)" baseline post)

    // -----------------------------------------------------------------------
    // Track D — per-type null-state-transition witnesses (AC-D1, D2.4, D3.3).
    // [<MemberData>] over nullableTypeMatrix. Exact-count contract (OB-D4.2):
    // under @supports_net_changes=0, one updated row ⇒ exactly +2 captures
    // (delete-image + insert-image); a suppressed no-op UPDATE ⇒ +0.
    // -----------------------------------------------------------------------

    static member NullableTypeMatrix : seq<objnull[]> =
        CdcSilenceFixtures.nullableTypeMatrix
        |> List.map (fun (typ, v1, v2) -> [| box typ; box v1; box v2 |])
        |> List.toSeq

    [<Theory>]
    [<MemberData("NullableTypeMatrix")>]
    member _.``Track D / AC-D1 left-null arm: NULL -> value on a nullable column fires exactly +2 CDC captures`` (typ: PrimitiveType) (value1: string) (_value2: string) =
        if not (skipIfNoDocker (sprintf "cdc-nullable-null-to-value-%A" typ)) then () else
        let nullCatalog,  kind = buildNullableFixture typ ""
        let valueCatalog, _    = buildNullableFixture typ value1
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql,     nullSeed  = renderArtifacts nullCatalog  kind cdcAware
        let _,             valueSeed = renderArtifacts valueCatalog kind cdcAware
        Assert.Contains ("WHEN MATCHED AND (", valueSeed)
        Assert.Contains ("NULL", nullSeed)
        let baseline, post =
            (runScenario nullSeed valueSeed kind schemaSql).GetAwaiter().GetResult()
        Assert.True (baseline >= 1, sprintf "expected baseline ≥ 1 INSERT capture; got %d (type %A)" baseline typ)
        Assert.Equal (baseline + 2, post)

    [<Theory>]
    [<MemberData("NullableTypeMatrix")>]
    member _.``Track D / AC-D1 right-null arm: value -> NULL on a nullable column fires exactly +2 CDC captures`` (typ: PrimitiveType) (value1: string) (_value2: string) =
        if not (skipIfNoDocker (sprintf "cdc-nullable-value-to-null-%A" typ)) then () else
        let valueCatalog, kind = buildNullableFixture typ value1
        let nullCatalog,  _    = buildNullableFixture typ ""
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql,     valueSeed = renderArtifacts valueCatalog kind cdcAware
        let _,             nullSeed  = renderArtifacts nullCatalog  kind cdcAware
        Assert.Contains ("WHEN MATCHED AND (", nullSeed)
        Assert.Contains ("NULL", nullSeed)
        let baseline, post =
            (runScenario valueSeed nullSeed kind schemaSql).GetAwaiter().GetResult()
        Assert.True (baseline >= 1, sprintf "expected baseline ≥ 1 INSERT capture; got %d (type %A)" baseline typ)
        Assert.Equal (baseline + 2, post)

    [<Theory>]
    [<MemberData("NullableTypeMatrix")>]
    member _.``Track D / D2.4 nullable-stays-NULL: NULL -> NULL redeploy on a nullable column is CDC-silent (zero new captures)`` (typ: PrimitiveType) (_value1: string) (_value2: string) =
        if not (skipIfNoDocker (sprintf "cdc-nullable-null-to-null-%A" typ)) then () else
        let nullCatalog, kind = buildNullableFixture typ ""
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql, nullSeed = renderArtifacts nullCatalog kind cdcAware
        Assert.Contains ("WHEN MATCHED AND (", nullSeed)
        Assert.Contains ("NULL", nullSeed)
        let baseline, post =
            (runScenario nullSeed nullSeed kind schemaSql).GetAwaiter().GetResult()
        Assert.True (baseline >= 1, sprintf "expected baseline ≥ 1 INSERT capture; got %d (type %A)" baseline typ)
        Assert.Equal (baseline, post)

    // =====================================================================
    // Track G (Round 4) — k>1 EXACT-count CDC arithmetic (D4.3/4.4/4.5) +
    // the MigrationDependenciesEmitter live-CDC witness (D2.5/D3.4).
    // @supports_net_changes=0: INSERT=+1, UPDATE=+2 (before+after image).
    // Exact `Assert.Equal(baseline + N, post)`, never an inequality — a
    // "fire UPDATE for every matched row" impl yields +2n, failing D4.3.
    // =====================================================================

    [<Fact>]
    member _.``OB-D4.3 exact-count: k=3 of 5 changed rows fires exactly +6 CDC captures (2 per changed row)`` () =
        if not (skipIfNoDocker "cdc-d4.3") then () else
        let n = 5
        let k = 3
        let baseLabel i = sprintf "base-%02d" i
        let mutLabel i = if i <= k then sprintf "chg-%02d" i else sprintf "base-%02d" i
        let baseCatalog, kind = buildDeltaFixture n baseLabel
        let mutCatalog, _ = buildDeltaFixture n mutLabel
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql, baseSeed = renderArtifacts baseCatalog kind cdcAware
        let _, mutSeed = renderArtifacts mutCatalog kind cdcAware
        Assert.Contains ("WHEN MATCHED AND (", baseSeed)
        Assert.NotEqual<string> (baseSeed, mutSeed)
        let baseline, post =
            (runScenario baseSeed mutSeed kind schemaSql).GetAwaiter().GetResult()
        Assert.Equal (n, baseline)
        Assert.Equal (baseline + (2 * k), post)   // exactly 2k, never 2n.

    [<Fact>]
    member _.``OB-D4.4 exact-count: all-n changed rows fires exactly +2n CDC captures`` () =
        if not (skipIfNoDocker "cdc-d4.4") then () else
        let n = 4
        let baseLabel i = sprintf "base-%02d" i
        let mutLabel i = sprintf "chg-%02d" i
        let baseCatalog, kind = buildDeltaFixture n baseLabel
        let mutCatalog, _ = buildDeltaFixture n mutLabel
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql, baseSeed = renderArtifacts baseCatalog kind cdcAware
        let _, mutSeed = renderArtifacts mutCatalog kind cdcAware
        Assert.Contains ("WHEN MATCHED AND (", baseSeed)
        let baseline, post =
            (runScenario baseSeed mutSeed kind schemaSql).GetAwaiter().GetResult()
        Assert.Equal (n, baseline)
        Assert.Equal (baseline + (2 * n), post)   // exactly 2n.

    [<Fact>]
    member _.``OB-D4.5 exact-count: k=2 fresh inserts fire exactly +2 CDC captures (one per insert; unchanged rows silent)`` () =
        if not (skipIfNoDocker "cdc-d4.5") then () else
        let baseLabel i = sprintf "base-%02d" i
        let baseCatalog, kind = buildDeltaFixture 4 baseLabel
        let grownCatalog, _ = buildDeltaFixture 6 baseLabel
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql, baseSeed = renderArtifacts baseCatalog kind cdcAware
        let _, grownSeed = renderArtifacts grownCatalog kind cdcAware
        Assert.Contains ("WHEN MATCHED AND (", baseSeed)
        Assert.NotEqual<string> (baseSeed, grownSeed)
        let baseline, post =
            (runScenario baseSeed grownSeed kind schemaSql).GetAwaiter().GetResult()
        Assert.Equal (4, baseline)
        Assert.Equal (baseline + 2, post)   // exactly +2: one per fresh insert.

    [<Fact>]
    member _.``OB-D2.5 MigrationDependencies live-CDC: idempotent redeploy fires zero net CDC captures`` () =
        if not (skipIfNoDocker "cdc-d2.5") then () else
        let kind = mkMigCountryKind ()
        let catalog = migCatalog kind
        let rows =
            [ migRow kind.SsKey "1" "US" "United States"
              migRow kind.SsKey "2" "CA" "Canada" ]
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql, seedSql = renderMigrationArtifacts catalog kind rows cdcAware
        Assert.Contains ("WHEN MATCHED AND (", seedSql)
        let baseline, post =
            (runScenario seedSql seedSql kind schemaSql).GetAwaiter().GetResult()
        Assert.Equal (2, baseline)
        Assert.Equal (baseline, post)     // zero net on idempotent redeploy.

    [<Fact>]
    member _.``OB-D3.4 MigrationDependencies live-CDC: one-row change fires exactly +2 CDC captures`` () =
        if not (skipIfNoDocker "cdc-d3.4") then () else
        let kind = mkMigCountryKind ()
        let catalog = migCatalog kind
        let baseRows =
            [ migRow kind.SsKey "1" "US" "United States"
              migRow kind.SsKey "2" "CA" "Canada" ]
        let changedRows =
            [ migRow kind.SsKey "1" "US" "USA"
              migRow kind.SsKey "2" "CA" "Canada" ]
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql, baseSeed = renderMigrationArtifacts catalog kind baseRows cdcAware
        let _, changedSeed = renderMigrationArtifacts catalog kind changedRows cdcAware
        Assert.NotEqual<string> (baseSeed, changedSeed)
        let baseline, post =
            (runScenario baseSeed changedSeed kind schemaSql).GetAwaiter().GetResult()
        Assert.Equal (2, baseline)
        Assert.Equal (baseline + 2, post)   // exactly +2 for the one change.
