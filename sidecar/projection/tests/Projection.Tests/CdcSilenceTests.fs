[<Xunit.Collection("Docker-SqlServer")>]
module Projection.Tests.CdcSilenceTests

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
// ---------------------------------------------------------------------------

let private skipIfNoDocker (label: string) : bool =
    if Deploy.Docker.ensureRunning () then true
    else
        printfn "SKIP %s: Docker daemon not reachable." label
        false

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es -> invalidOp (sprintf "fixture: %A" es)

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_TEST_CDC" parts |> mustOk

let private mkName (s: string) : Name =
    Name.create s |> mustOk

/// Single-table fixture: a CDC-tracked Static-modality kind with two
/// rows. Schema `dbo`, table `OSUSR_CDC_COUNTRY`. Three columns:
/// Id (PK, INT), Code (nvarchar), Label (nvarchar). Kept tight so
/// the capture-table name is predictable: `cdc.dbo_OSUSR_CDC_COUNTRY_CT`.
let private buildFixture () : Catalog * Kind =
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
          Origin   = OsNative
          Modality = [ Static [ row "1" "United States"
                                row "2" "Canada" ] ]
          Physical = { Schema = "dbo"; Table = "OSUSR_CDC_COUNTRY" }
          Attributes =
              [
                  { SsKey = idKey;    Name = mkName "Id";    Type = Integer
                    Column = { ColumnName = "ID";    IsNullable = false }
                    IsPrimaryKey = true; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false }
                  { SsKey = codeKey;  Name = mkName "Code";  Type = Text
                    Column = { ColumnName = "CODE";  IsNullable = false }
                    IsPrimaryKey = false; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false }
                  { SsKey = labelKey; Name = mkName "Label"; Type = Text
                    Column = { ColumnName = "LABEL"; IsNullable = false }
                    IsPrimaryKey = false; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false }
              ]
          References = []
          Indexes    = [] }
    let m : Module =
        { SsKey = mkKey ["Module"]
          Name  = mkName "TestModule"
          Kinds = [ kind ] }
    { Modules = [ m ] }, kind

let private executeScalarInt (cnn: SqlConnection) (sql: string) : Task<int> =
    task {
        use cmd = cnn.CreateCommand()
        cmd.CommandText <- sql
        cmd.CommandTimeout <- 0
        let! result = cmd.ExecuteScalarAsync()
        return System.Convert.ToInt32 result
    }

let private dropDatabaseBestEffort (masterConn: string) (dbName: string) : Task<unit> =
    task {
        try
            use cnn = new SqlConnection(masterConn)
            do! cnn.OpenAsync()
            // CDC-enabled DB cannot be dropped while in use; force
            // SINGLE_USER ROLLBACK IMMEDIATE first. The bracket-
            // quoting flows through `Render.quote` (ScriptDom-encoded
            // identifier) so unusual `dbName` characters survive.
            let q = Render.quote dbName
            let sql =
                System.String.Concat(
                    "ALTER DATABASE ", q, " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ",
                    "DROP DATABASE ", q, ";")
            do! Deploy.executeBatch cnn sql
        with _ ->
            // Best-effort cleanup. The warm container persists across
            // tests; orphaned databases will be reaped by the next
            // SessionEnd hook or container restart.
            ()
    }

/// Shared scenario runner. Deploys schema, enables CDC, runs the
/// `firstSeedSql` to populate (the baseline), then runs the
/// `secondSeedSql` (the property under test). Returns `(baselineCount,
/// postCount, firstSeedSql)` so the caller can sanity-check the
/// emitted SQL shape and assert on the CDC-row delta.
///
/// **Container isolation** (chapter 4.1.B slice δ observability cash-
/// out): `Deploy.useEphemeralContainer` rather than `Deploy.useContainer`
/// so CDC infrastructure (`sys.sp_cdc_enable_db` + capture-instance
/// state + `master.sys.databases.is_cdc_enabled` flips) lives in a
/// dedicated SQL Server instance that never touches the warm
/// container's `master`. The `Docker-SqlServer` xUnit collection
/// already serializes Docker-touching test classes; this dedicated
/// container is the structural fix that makes the isolation absolute
/// (broad-stroke serialization is the safety net; container-per-CDC
/// is the load-bearing isolation).
let private runScenario (firstSeedSql: string) (secondSeedSql: string) (kind: Kind) (schemaSql: string) : Task<int * int> =
    task {
        return!
            Deploy.useEphemeralContainer (fun masterConn ->
                task {
                    let dbName =
                        System.String.Concat("CdcSilence_", System.Guid.NewGuid().ToString("N").Substring(0, 8))

                    do! task {
                        use cnn = new SqlConnection(masterConn)
                        do! cnn.OpenAsync()
                        do! Deploy.executeBatch cnn (System.String.Concat("CREATE DATABASE ", Render.quote dbName, ";"))
                    }

                    let perDbConn = Deploy.ConnectionString.buildPerDb masterConn dbName

                    try
                        let! result =
                            task {
                                use cnn = new SqlConnection(perDbConn)
                                do! cnn.OpenAsync()
                                do! Deploy.executeBatch cnn schemaSql
                                do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_enable_db;"
                                let enableTableSql =
                                    System.String.Concat(
                                        "EXEC sys.sp_cdc_enable_table ",
                                        "@source_schema=N'", kind.Physical.Schema, "', ",
                                        "@source_name=N'", kind.Physical.Table, "', ",
                                        "@role_name=NULL, ",
                                        "@supports_net_changes=0;")
                                do! Deploy.executeBatch cnn enableTableSql

                                // Phase 1: first deploy populates rows.
                                do! Deploy.executeBatch cnn firstSeedSql
                                do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_scan;"

                                let captureTable =
                                    System.String.Concat(
                                        "cdc.[",
                                        kind.Physical.Schema, "_", kind.Physical.Table,
                                        "_CT]")
                                let countSql =
                                    System.String.Concat("SELECT COUNT(*) FROM ", captureTable, ";")
                                let! baselineCount = executeScalarInt cnn countSql

                                // Phase 2: second deploy. The property under test
                                // is whatever this MERGE does to the CDC capture
                                // table relative to baseline.
                                do! Deploy.executeBatch cnn secondSeedSql
                                do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_scan;"

                                let! postCount = executeScalarInt cnn countSql
                                return baselineCount, postCount
                            }
                        return result
                    finally
                        (dropDatabaseBestEffort masterConn dbName).GetAwaiter().GetResult()
                })
    }

/// Convenience: build the schema SQL + seed SQL for a given catalog +
/// CDC awareness. Encapsulates the artifact-→-text projection so test
/// bodies stay focused on the property assertions.
let private renderArtifacts (catalog: Catalog) (kind: Kind) (cdcAwareness: CdcAwareness) : string * string =
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
        match StaticSeedsEmitter.emit catalog profile with
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
let private buildChangedFixture () : Catalog * Kind =
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
          Origin   = OsNative
          Modality = [ Static [ row "1" "USA"          // <-- changed
                                row "2" "Canada" ] ]
          Physical = { Schema = "dbo"; Table = "OSUSR_CDC_COUNTRY" }
          Attributes =
              [
                  { SsKey = idKey;    Name = mkName "Id";    Type = Integer
                    Column = { ColumnName = "ID";    IsNullable = false }
                    IsPrimaryKey = true; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false }
                  { SsKey = codeKey;  Name = mkName "Code";  Type = Text
                    Column = { ColumnName = "CODE";  IsNullable = false }
                    IsPrimaryKey = false; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false }
                  { SsKey = labelKey; Name = mkName "Label"; Type = Text
                    Column = { ColumnName = "LABEL"; IsNullable = false }
                    IsPrimaryKey = false; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false }
              ]
          References = []
          Indexes    = [] }
    let m : Module =
        { SsKey = mkKey ["Module"]
          Name  = mkName "TestModule"
          Kinds = [ kind ] }
    { Modules = [ m ] }, kind

[<Fact>]
let ``Slice γ: CDC-silence — V2 change-detection predicate emits zero CDC capture rows on idempotent redeploy`` () =
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
let ``Slice γ sensitivity: changed-content redeploy DOES fire CDC capture rows — proves the canary mechanism is real (not silent for unrelated reasons)`` () =
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
