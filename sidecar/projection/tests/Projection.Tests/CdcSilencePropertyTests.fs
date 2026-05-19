namespace Projection.Tests

// Slice A.4.7'-prelude+pipeline-registry — CDC-silence property
// sweep. Per V2_DRIVER.md the highest-leverage single deliverable
// in the entire chapter sequence: V2's idempotent-redeploy contract
// must hold across realistic catalog shapes, not just one fixture.
//
// `CdcSilenceTests.fs` covers the single-row-shape happy path +
// sensitivity counter-test (proves the canary observes real CDC
// traffic). `CdcSilenceCrossEmitterTests.fs` covers cross-emitter
// composition. This file is the **shape-sweep** — varying row
// counts + column-type mixes + identifier characters — to verify
// the change-detection predicate's MERGE WHEN MATCHED clause holds
// the invariant uniformly.
//
// **The invariant (V2_DRIVER per-axis stakes table, CDC silence):**
// for any catalog C with CDC-tracked static populations, deploying
// C's StaticSeedsEmitter output twice produces the same number of
// CDC capture rows as deploying once. Idempotent-redeploy = zero net
// CDC change events.
//
// **Fixture-lift (slice A.4.7'-prelude.test-fixture-lift, 2026-05-19).**
// xUnit `IClassFixture<EphemeralContainerFixture>` shares one
// ephemeral container across all 3 shape-sweep tests; per-test
// `WithEphemeralDatabase` lifecycle preserves the per-scenario CDC
// isolation. Sibling `CdcSilenceTests` + `CdcSilenceCrossEmitterTests`
// each get their own per-class container (the `Docker-SqlServer`
// collection serializes them across classes).

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.Data
open Projection.Targets.SSDT

module private CdcSilencePropertyFixtures =

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
        SsKey.synthesizedComposite "OS_TEST_CDC_PROP" parts |> mustOk

    let mkName (s: string) : Name = Name.create s |> mustOk

    let executeScalarInt (cnn: SqlConnection) (sql: string) : Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            cmd.CommandTimeout <- 0
            let! result = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt32 result
        }

    /// Project a single-kind catalog through the SSDT + StaticSeeds
    /// emitters to produce (schema-SQL, seed-SQL) for the deploy harness.
    let renderArtifacts
        (catalog: Catalog)
        (kind: Kind)
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
            |> String.concat "\nGO\n"  // LINT-ALLOW: per-kind CREATE TABLE bodies joined with GO batch boundary; segments are typed `SsdtFile.Body` strings from ScriptDomGenerate; no use-case-specific library exists for cross-file SQL-batch concatenation
        let profile = { Profile.empty with CdcAwareness = cdcAwareness }
        let seedArtifact =
            match StaticSeedsEmitter.emit catalog profile with
            | Ok a -> a
            | Error e -> failwithf "StaticSeedsEmitter.emit: %A" e
        let seedSql =
            seedArtifact
            |> ArtifactByKind.toMap
            |> Map.find kind.SsKey
            |> fun s -> s.Rendered
        schemaSql, seedSql

    // -----------------------------------------------------------------------
    // Shape variants — each exercises a distinct fixture axis (row count
    // / column type mix / table-name shape). The CDC-silence invariant
    // must hold uniformly: idempotent redeploy → zero net CDC capture.
    // -----------------------------------------------------------------------

    /// Variant 1 — single row, single column. Minimal shape; isolates
    /// the MERGE WHEN MATCHED predicate from row-count interactions.
    let buildSingleRowFixture () : Catalog * Kind =
        let kindKey = mkKey ["SingleRow"]
        let idKey = mkKey ["SingleRow"; "Id"]
        let valKey = mkKey ["SingleRow"; "Val"]
        let row =
            { Identifier = mkKey ["SingleRow"; "Row"; "1"]
              Values =
                  Map.ofList
                      [ mkName "Id",  "1"
                        mkName "Val", "alpha" ] }
        let kind : Kind =
            { SsKey      = kindKey
              Name       = mkName "SingleRow"
              Origin     = OsNative
              Modality   = [ Static [ row ] ]
              Physical   = { Schema = "dbo"; Table = "OSUSR_PCDC_ONE"; Catalog = None }
              Attributes =
                  [ { Attribute.create idKey (mkName "Id") Integer with
                        Column = { ColumnName = "ID"; IsNullable = false }
                        IsPrimaryKey = true
                        IsMandatory  = true }
                    { Attribute.create valKey (mkName "Val") Text with
                        Column = { ColumnName = "VAL"; IsNullable = false }
                        Length = Some 50
                        IsMandatory = true } ]
              References = []; Indexes = []
              Description = None; IsActive = true
              Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
        let m : Module =
            { SsKey = mkKey ["Module"; "Single"]
              Name  = mkName "SingleRowModule"
              Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
        { Modules = [ m ]; Sequences = [] }, kind

    /// Variant 2 — multi-type row mix (Int + Text + Boolean + Decimal).
    /// Exercises the SqlLiteral roundtrip across V2's typed primitive
    /// surface. The change-detection predicate must handle each type
    /// correctly under MERGE WHEN MATCHED.
    let buildMultiTypeFixture () : Catalog * Kind =
        let kindKey = mkKey ["MultiType"]
        let idKey = mkKey ["MultiType"; "Id"]
        let nameKey = mkKey ["MultiType"; "Name"]
        let activeKey = mkKey ["MultiType"; "Active"]
        let priceKey = mkKey ["MultiType"; "Price"]
        let row id name active price =
            { Identifier = mkKey ["MultiType"; "Row"; id]
              Values =
                  Map.ofList
                      [ mkName "Id",     id
                        mkName "Name",   name
                        mkName "Active", active
                        mkName "Price",  price ] }
        let kind : Kind =
            { SsKey      = kindKey
              Name       = mkName "MultiType"
              Origin     = OsNative
              Modality   =
                  [ Static
                        [ row "10" "alpha" "true"  "1.50"
                          row "20" "beta"  "false" "2.75"
                          row "30" "gamma" "true"  "3.00" ] ]
              Physical   = { Schema = "dbo"; Table = "OSUSR_PCDC_MULTITYPE"; Catalog = None }
              Attributes =
                  [ { Attribute.create idKey (mkName "Id") Integer with
                        Column = { ColumnName = "ID"; IsNullable = false }
                        IsPrimaryKey = true
                        IsMandatory  = true }
                    { Attribute.create nameKey (mkName "Name") Text with
                        Column = { ColumnName = "NAME"; IsNullable = false }
                        Length = Some 100
                        IsMandatory = true }
                    { Attribute.create activeKey (mkName "Active") Boolean with
                        Column = { ColumnName = "ACTIVE"; IsNullable = false }
                        IsMandatory = true }
                    { Attribute.create priceKey (mkName "Price") Decimal with
                        Column = { ColumnName = "PRICE"; IsNullable = false }
                        Precision = Some 10
                        Scale     = Some 2
                        IsMandatory = true } ]
              References = []; Indexes = []
              Description = None; IsActive = true
              Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
        let m : Module =
            { SsKey = mkKey ["Module"; "MultiType"]
              Name  = mkName "MultiTypeModule"
              Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
        { Modules = [ m ]; Sequences = [] }, kind

    /// Variant 3 — many rows (10 entries). Stress-tests row-count
    /// interactions; each row contributes one INSERT to the baseline
    /// capture count, and zero captures on idempotent redeploy.
    let buildManyRowsFixture () : Catalog * Kind =
        let kindKey = mkKey ["ManyRows"]
        let idKey = mkKey ["ManyRows"; "Id"]
        let labelKey = mkKey ["ManyRows"; "Label"]
        let row id label =
            { Identifier = mkKey ["ManyRows"; "Row"; id]
              Values =
                  Map.ofList
                      [ mkName "Id",    id
                        mkName "Label", label ] }
        let rows =
            [ for i in 1 .. 10 ->
                row (string i) (sprintf "row-%02d" i) ]
        let kind : Kind =
            { SsKey      = kindKey
              Name       = mkName "ManyRows"
              Origin     = OsNative
              Modality   = [ Static rows ]
              Physical   = { Schema = "dbo"; Table = "OSUSR_PCDC_MANY"; Catalog = None }
              Attributes =
                  [ { Attribute.create idKey (mkName "Id") Integer with
                        Column = { ColumnName = "ID"; IsNullable = false }
                        IsPrimaryKey = true
                        IsMandatory  = true }
                    { Attribute.create labelKey (mkName "Label") Text with
                        Column = { ColumnName = "LABEL"; IsNullable = false }
                        Length = Some 50
                        IsMandatory = true } ]
              References = []; Indexes = []
              Description = None; IsActive = true
              Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
        let m : Module =
            { SsKey = mkKey ["Module"; "Many"]
              Name  = mkName "ManyRowsModule"
              Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
        { Modules = [ m ]; Sequences = [] }, kind

open CdcSilencePropertyFixtures

[<Xunit.Collection("Docker-SqlServer")>]
type CdcSilencePropertyTests(fixture: EphemeralContainerFixture) =

    /// Deploy `schemaSql` + `seedSql` to an ephemeral CDC-enabled DB
    /// twice; return (baseline-capture-count, post-redeploy-capture-count)
    /// for the named kind's capture table. Reusable shape across all
    /// shape-sweep variants.
    let runCdcSilence
        (schemaSql: string)
        (seedSql: string)
        (kind: Kind)
        : Task<int * int> =
        fixture.WithEphemeralDatabase "CdcSilenceProp" (fun cnn _ -> task {
            do! Deploy.executeBatch cnn schemaSql
            do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_enable_db;"
            let enableSql =
                System.String.Concat(
                    "EXEC sys.sp_cdc_enable_table ",
                    "@source_schema=N'", kind.Physical.Schema, "', ",
                    "@source_name=N'", kind.Physical.Table, "', ",
                    "@role_name=NULL, ",
                    "@supports_net_changes=0;")
            do! Deploy.executeBatch cnn enableSql
            do! Deploy.executeBatch cnn seedSql
            do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_scan;"
            let captureTable =
                System.String.Concat(
                    "cdc.[", kind.Physical.Schema, "_",
                    kind.Physical.Table, "_CT]")
            let countSql =
                System.String.Concat("SELECT COUNT(*) FROM ", captureTable, ";")
            let! baseline = executeScalarInt cnn countSql
            do! Deploy.executeBatch cnn seedSql
            do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_scan;"
            let! post = executeScalarInt cnn countSql
            return baseline, post
        })

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``A.4.7'-prelude CDC-silence sweep (single-row variant): idempotent redeploy adds zero CDC capture rows`` () =
        if not (skipIfNoDocker "cdc-silence-single-row") then () else
        let catalog, kind = buildSingleRowFixture ()
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql, seedSql = renderArtifacts catalog kind cdcAware
        Assert.Contains ("WHEN MATCHED AND (", seedSql)
        let baseline, post =
            (runCdcSilence schemaSql seedSql kind).GetAwaiter().GetResult()
        Assert.True(baseline >= 1, sprintf "expected baseline ≥ 1 capture; got %d" baseline)
        Assert.Equal(baseline, post)

    [<Fact>]
    member _.``A.4.7'-prelude CDC-silence sweep (multi-type variant Int/Text/Boolean/Decimal): idempotent redeploy adds zero CDC capture rows`` () =
        if not (skipIfNoDocker "cdc-silence-multi-type") then () else
        let catalog, kind = buildMultiTypeFixture ()
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql, seedSql = renderArtifacts catalog kind cdcAware
        Assert.Contains ("WHEN MATCHED AND (", seedSql)
        let baseline, post =
            (runCdcSilence schemaSql seedSql kind).GetAwaiter().GetResult()
        Assert.True(baseline >= 3, sprintf "expected baseline ≥ 3 captures (one per row); got %d" baseline)
        Assert.Equal(baseline, post)

    [<Fact>]
    member _.``A.4.7'-prelude CDC-silence sweep (10-row variant): idempotent redeploy adds zero CDC capture rows`` () =
        if not (skipIfNoDocker "cdc-silence-many-rows") then () else
        let catalog, kind = buildManyRowsFixture ()
        let cdcAware = CdcAwareness.create (Set.ofList [ kind.SsKey ]) Map.empty
        let schemaSql, seedSql = renderArtifacts catalog kind cdcAware
        Assert.Contains ("WHEN MATCHED AND (", seedSql)
        let baseline, post =
            (runCdcSilence schemaSql seedSql kind).GetAwaiter().GetResult()
        Assert.True(baseline >= 10, sprintf "expected baseline ≥ 10 captures; got %d" baseline)
        Assert.Equal(baseline, post)
