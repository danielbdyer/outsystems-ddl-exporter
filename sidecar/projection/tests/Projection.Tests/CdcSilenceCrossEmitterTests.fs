namespace Projection.Tests

// Chapter 5.13 slice cdc-silence-cross-emitter — Phase 8 blocker #1 (DATA
// axis V2-driver flip) closure. The single highest-leverage deliverable
// in the entire chapter sequence per V2_DRIVER.md per-axis correctness
// stakes table.
//
// **The claim under test.** Given a multi-emitter, multi-kind catalog
// with CDC-tracked tables and at least one cross-emitter FK cycle
// requiring Phase-2 UPDATE statements:
//
//   redeploy(composeRenderedFull(P, C, Profile.withCdc(T)))
//       ⟹ ∀ t ∈ T : CDC.captures(t) = ∅
//
// **Fixture-lift (slice A.4.7'-prelude.test-fixture-lift, 2026-05-19).**
// The 4 Docker-gated tests (C1-C4) share one ephemeral container via
// `IClassFixture<EphemeralContainerFixture>`. C0 (pure-structural;
// no Docker) stays in a separate non-fixture module so the no-Docker
// path doesn't trigger container init.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Targets.Data
open Projection.Targets.SSDT
open Projection.Pipeline

module private CdcSilenceCrossEmitterFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn
                "SKIP %s: Docker daemon not reachable. Set DOCKER_HOST or start the daemon to run CDC canary tests."
                label
            false

    let mustOk r =
        match r with
        | Ok v -> v
        | Error es ->
            let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
            invalidOp (sprintf "fixture: %s" codes)

    let mkKey (parts: string list) : SsKey =
        SsKey.synthesizedComposite "OS_CDCX" parts |> mustOk

    let mkName (s: string) : Name =
        Name.create s |> mustOk

    let mustOkEmit (r: Result<'a, EmitError>) : 'a =
        match r with
        | Ok v -> v
        | Error e -> failwithf "composer error: %A" e

    // ----------------------------------------------------------------
    // Fixture: Country (Static modality) — StaticSeedsEmitter populates.
    // ----------------------------------------------------------------

    let mkCountryKind () : Kind =
        let kindKey = mkKey ["Country"]
        let idKey = mkKey ["Country"; "Id"]
        let codeKey = mkKey ["Country"; "Code"]
        let labelKey = mkKey ["Country"; "Label"]
        let row code label =
            { Identifier = mkKey ["Country"; "Row"; code]
              Values =
                  Map.ofList
                      [ mkName "Id",    code
                        mkName "Code",  code
                        mkName "Label", label ] }
        { SsKey    = kindKey
          Name     = mkName "Country"
          Origin   = Native
          Modality = [ Static [ row "1" "United States"
                                row "2" "Canada" ] ]
          Physical = { Schema = "dbo"; Table = "CDCX_COUNTRY"; Catalog = None }
          Attributes =
              [
                  { Attribute.create idKey (mkName "Id") Integer with Column = { ColumnName = "ID";    IsNullable = false }; IsPrimaryKey = true; IsMandatory = true }
                  { Attribute.create codeKey (mkName "Code") Text with Column = { ColumnName = "CODE";  IsNullable = false }; IsMandatory = true }
                  { Attribute.create labelKey (mkName "Label") Text with Column = { ColumnName = "LABEL"; IsNullable = false }; IsMandatory = true }
              ]
          References = []
          Indexes    = []
          Description = None
          IsActive = true
          Triggers = []
          ColumnChecks = []
          ExtendedProperties = [] }

    // ----------------------------------------------------------------
    // Fixture: LegacyOrder — non-static modality; self-FK on nullable
    // ParentId column (triggers cycle → Phase-2 UPDATE).
    // ----------------------------------------------------------------

    let legacyOrderKindKey = mkKey ["LegacyOrder"]

    let mkLegacyOrderKind () : Kind =
        let idKey = mkKey ["LegacyOrder"; "Id"]
        let parentKey = mkKey ["LegacyOrder"; "ParentId"]
        let refKey = mkKey ["LegacyOrder"; "RefSelf"]
        { SsKey    = legacyOrderKindKey
          Name     = mkName "LegacyOrder"
          Origin   = Native
          Modality = []
          Physical = { Schema = "dbo"; Table = "CDCX_LEGACY_ORDER"; Catalog = None }
          Attributes =
              [
                  { Attribute.create idKey (mkName "Id") Integer with Column = { ColumnName = "ID"; IsNullable = false }; IsPrimaryKey = true; IsMandatory = true }
                  { Attribute.create parentKey (mkName "ParentId") Integer with Column = { ColumnName = "PARENTID"; IsNullable = true } }
              ]
          References = [ Reference.create refKey (mkName "RefSelf") parentKey legacyOrderKindKey ]
          Indexes    = []
          Description = None
          IsActive = true
          Triggers = []
          ColumnChecks = []
          ExtendedProperties = [] }

    let mkCatalog (kinds: Kind list) : Catalog =
        let m : Module =
            { SsKey = mkKey ["TestModule"]
              Name  = mkName "TestModule"
              Kinds = kinds; IsActive = true; ExtendedProperties = [] }
        { Modules = [ m ]; Sequences = [] }

    let policyAllRemaining : Policy =
        { Policy.empty with
            Emission =
                { Policy.empty.Emission with
                    EmitData = true
                    DataComposition = AllRemaining } }

    let migrationCtxFor (legacyKindKey: SsKey) (parentId: string) : MigrationDependencyContext =
        { Rows =
            [ { KindKey = legacyKindKey
                Identifier = mkKey ["LegacyOrder"; "Row"; "1"]
                Values =
                    Map.ofList
                        [ mkName "Id",       "1"
                          mkName "ParentId", parentId ] } ] }

    let executeScalarInt (cnn: SqlConnection) (sql: string) : Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            cmd.CommandTimeout <- 0
            let! result = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt32 result
        }

    let buildSchemaSqlFor (catalog: Catalog) : string =
        // Use SsdtDdlEmitter for typed CREATE TABLE per chapter 4.1.A
        // discipline; concatenate per-kind files with GO between them.
        let artifact =
            match SsdtDdlEmitter.emitSlices catalog with
            | Ok a -> a
            | Error e -> failwithf "SsdtDdlEmitter.emitSlices: %A" e
        artifact
        |> ArtifactByKind.toMap
        |> Map.toList
        |> List.map (fun (_, file) -> file.Body)
        |> String.concat "\nGO\n"  // LINT-ALLOW: terminal SQL-batch joiner across per-kind SsdtFile bodies; segments are typed (each `file.Body` is rendered DDL); same shape as the precedent in CdcSilenceTests.renderArtifacts

    /// Tracked-table coordinates for CDC enable + capture-table counting.
    type CdcTrackedTable =
        {
            Schema : string
            Table  : string
        }

    let cdcAwareTracked (kinds: Kind list) : CdcTrackedTable list =
        kinds
        |> List.map (fun k ->
            { Schema = k.Physical.Schema
              Table  = k.Physical.Table })

    /// CDC-enable every tracked table inside one batch — issuing
    /// sys.sp_cdc_enable_table per pair after sys.sp_cdc_enable_db on
    /// the database.
    let enableCdcOn (cnn: SqlConnection) (tracked: CdcTrackedTable list) : Task<unit> =
        task {
            do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_enable_db;"
            for t in tracked do
                let sql =
                    System.String.Concat(
                        "EXEC sys.sp_cdc_enable_table ",
                        "@source_schema=N'", t.Schema, "', ",
                        "@source_name=N'",  t.Table,  "', ",
                        "@role_name=NULL, ",
                        "@supports_net_changes=0;")
                do! Deploy.executeBatch cnn sql
        }

    /// Sum CDC capture-row counts across all tracked tables.
    let countTotalCaptures (cnn: SqlConnection) (tracked: CdcTrackedTable list) : Task<int> =
        task {
            let mutable total = 0
            for t in tracked do
                let captureTable =
                    System.String.Concat("cdc.[", t.Schema, "_", t.Table, "_CT]")
                let countSql =
                    System.String.Concat("SELECT COUNT(*) FROM ", captureTable, ";")
                let! c = executeScalarInt cnn countSql
                total <- total + c
            return total
        }

open CdcSilenceCrossEmitterFixtures

// ----------------------------------------------------------------
// C0 — pure SQL-shape diagnostic (no Docker). Lives in a plain
// module so the no-Docker test path doesn't pay any container cost.
// ----------------------------------------------------------------

module CdcSilenceCrossEmitterStructural =

    [<Fact>]
    let ``5.13.cdc-silence-cross-emitter C0: cross-emitter composer output contains cdcAware MERGE predicate for both emitters`` () =
        let country = mkCountryKind ()
        let legacy = mkLegacyOrderKind ()
        let catalog = mkCatalog [ country; legacy ]
        let cdcAware =
            CdcAwareness.create
                (Set.ofList [ country.SsKey; legacy.SsKey ]) Map.empty
        let profile = { Profile.empty with CdcAwareness = cdcAware }
        let migration = migrationCtxFor legacy.SsKey "1"
        let seedSql =
            DataEmissionComposer.composeRenderedFull
                policyAllRemaining catalog profile
                migration UserRemapContext.empty
            |> mustOkEmit
        // Diagnostic: surface the FULL composed text in the test
        // failure output so empirical structural drift is visible.
        let header = "===== COMPOSED CROSS-EMITTER SEED =====\n"
        let footer = "\n===== END SEED =====\n"
        let visibleSeed = System.String.Concat(header, seedSql, footer)
        System.Console.WriteLine visibleSeed
        // Structural claims after the CDC-silence-cross-emitter fix:
        //
        //   (a) Country has non-deferred updatable columns (Code, Label)
        //       AND cdcAware=true → MERGE carries `WHEN MATCHED AND (`
        //       predicate on the non-deferred columns.
        //
        //   (b) LegacyOrder's ONLY updatable column (ParentId) is
        //       deferred (Phase-2 owns it). With the Phase-1 UpdColumns
        //       filter excluding deferred, UpdColumns becomes empty →
        //       MERGE has only WHEN NOT MATCHED INSERT (no WHEN MATCHED
        //       branch at all). This is the structural fix that makes
        //       cross-emitter idempotent redeploy CDC-silent.
        //
        //   (c) LegacyOrder's Phase-2 UPDATE carries the change-detection
        //       predicate in its WHERE clause: `[ID] = 1 AND ([PARENTID]
        //       <> 1 OR [PARENTID] IS NULL)`. The predicate gates the
        //       UPDATE — no-op redeploys filter at the boundary.
        let countryMerge = seedSql.IndexOf "MERGE INTO [dbo].[CDCX_COUNTRY]"
        let legacyMerge = seedSql.IndexOf "MERGE INTO [dbo].[CDCX_LEGACY_ORDER]"
        Assert.True (countryMerge >= 0, "Country MERGE missing")
        Assert.True (legacyMerge >= 0, "LegacyOrder MERGE missing")
        let segmentAfter (start: int) : string =
            let after = seedSql.Substring(start)
            let nextStmtIdx =
                let nm = after.IndexOf("MERGE INTO", 50)
                let nu = after.IndexOf("UPDATE  [", 50)
                match nm, nu with
                | -1, -1 -> -1
                | a, -1 -> a
                | -1, b -> b
                | a, b -> min a b
            if nextStmtIdx > 0 then after.Substring(0, nextStmtIdx)
            else after
        let countrySegment = segmentAfter countryMerge
        let legacySegment = segmentAfter legacyMerge
        // Country: WHEN MATCHED AND predicate present (claim a).
        Assert.True (countrySegment.Contains "WHEN MATCHED AND (",
                     sprintf "Country MERGE missing cdcAware predicate. Segment:\n%s" countrySegment)
        // LegacyOrder: no WHEN MATCHED at all (claim b).
        Assert.False (legacySegment.Contains "WHEN MATCHED",
                      sprintf "LegacyOrder MERGE unexpectedly carries WHEN MATCHED — deferred-column filter regressed. Segment:\n%s" legacySegment)
        // Phase-2 UPDATE for LegacyOrder carries the change-detection
        // predicate (claim c). Look for `[PARENTID] <> 1` in the
        // UPDATE's WHERE clause.
        let phase2Idx = seedSql.IndexOf "UPDATE  [dbo].[CDCX_LEGACY_ORDER]"
        Assert.True (phase2Idx >= 0, "Phase-2 UPDATE for LegacyOrder missing")
        let phase2Segment = seedSql.Substring(phase2Idx)
        Assert.True (phase2Segment.Contains "[PARENTID] <> 1",
                     sprintf "Phase-2 UPDATE missing change-detection predicate. Segment:\n%s"
                         (phase2Segment.Substring(0, min phase2Segment.Length 400)))

// ----------------------------------------------------------------
// C1–C4 — Docker-gated tests sharing one ephemeral container per class.
// ----------------------------------------------------------------

[<Xunit.Collection("Docker-SqlServer")>]
type CdcSilenceCrossEmitterTests(fixture: EphemeralContainerFixture) =

    let runScenarioMulti
            (firstSeedSql: string)
            (secondSeedSql: string)
            (tracked: CdcTrackedTable list)
            (schemaSql: string)
            : Task<int * int> =
        fixture.WithEphemeralDatabase "CdcSilenceX" (fun cnn _ -> task {
            do! Deploy.executeBatch cnn schemaSql
            do! enableCdcOn cnn tracked
            do! Deploy.executeBatch cnn firstSeedSql
            do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_scan;"
            let! baseline = countTotalCaptures cnn tracked
            do! Deploy.executeBatch cnn secondSeedSql
            do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_scan;"
            let! post = countTotalCaptures cnn tracked
            return baseline, post
        })

    interface IClassFixture<EphemeralContainerFixture>

    // ----------------------------------------------------------------
    // C1 — single-emitter redeploy via composer. Country only, Static
    // modality, no FK cycle. Proves the composer pipeline preserves
    // the per-emitter CDC silence property established by Slice γ.
    // ----------------------------------------------------------------

    [<Fact>]
    member _.``5.13.cdc-silence-cross-emitter C1: composer single-emitter redeploy fires zero CDC captures`` () =
        if not (skipIfNoDocker "cdc-x-c1") then () else
        let country = mkCountryKind ()
        let catalog = mkCatalog [ country ]
        let cdcAware = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
        let profile = { Profile.empty with CdcAwareness = cdcAware }
        let schemaSql = buildSchemaSqlFor catalog
        let seedSql =
            DataEmissionComposer.composeRendered policyAllRemaining catalog profile
            |> mustOkEmit
        // Sanity guard: the composer's output carries the CDC-aware
        // MERGE predicate. If the pipeline ever stops threading
        // `cdcAware` to the emitters, this assertion catches the
        // regression before the canary's CDC count.
        Assert.Contains ("WHEN MATCHED AND (", seedSql)
        let tracked = cdcAwareTracked [ country ]
        let baseline, post =
            (runScenarioMulti seedSql seedSql tracked schemaSql).GetAwaiter().GetResult()
        Assert.True (baseline >= 2,
            sprintf "expected ≥2 CDC entries from initial INSERTs; got %d" baseline)
        Assert.Equal (baseline, post)

    // ----------------------------------------------------------------
    // C2 — cross-emitter redeploy. Country (Static) + LegacyOrder
    // (Migration) populate two disjoint CDC-tracked tables. The Phase-1
    // global ordering at composeRenderedFull interleaves MERGEs across
    // both emitters in topological order. The property: idempotent
    // redeploy of the composed text fires zero CDC across BOTH tables.
    // ----------------------------------------------------------------

    [<Fact>]
    member _.``5.13.cdc-silence-cross-emitter C2: composer cross-emitter (Static + Migration) redeploy fires zero CDC captures`` () =
        if not (skipIfNoDocker "cdc-x-c2") then () else
        let country = mkCountryKind ()
        let legacy = mkLegacyOrderKind ()
        let catalog = mkCatalog [ country; legacy ]
        let cdcAware =
            CdcAwareness.create
                (Set.ofList [ country.SsKey; legacy.SsKey ]) Map.empty
        let profile = { Profile.empty with CdcAwareness = cdcAware }
        // LegacyOrder row references itself via ParentId — this puts
        // LegacyOrder in a self-loop cycle, which triggers Phase-2
        // UPDATE per the deferred-FK logic.
        let migration = migrationCtxFor legacy.SsKey "1"
        let schemaSql = buildSchemaSqlFor catalog
        let seedSql =
            DataEmissionComposer.composeRenderedFull
                policyAllRemaining catalog profile
                migration UserRemapContext.empty
            |> mustOkEmit
        // Sanity guard: both Country (Static) and LegacyOrder (Migration)
        // surfaces appear in the composed output.
        Assert.Contains ("MERGE INTO [dbo].[CDCX_COUNTRY]", seedSql)
        Assert.Contains ("MERGE INTO [dbo].[CDCX_LEGACY_ORDER]", seedSql)
        let tracked = cdcAwareTracked [ country; legacy ]
        let baseline, post =
            (runScenarioMulti seedSql seedSql tracked schemaSql).GetAwaiter().GetResult()
        // Cross-emitter initial INSERT count: Country=2 rows + LegacyOrder=1 row
        // → ≥3 CDC entries from baseline.
        Assert.True (baseline >= 3,
            sprintf "expected ≥3 CDC entries from cross-emitter inserts; got %d" baseline)
        // The load-bearing claim: cross-emitter idempotent redeploy of
        // composeRenderedFull's output produces zero NEW CDC rows.
        Assert.Equal (baseline, post)

    // ----------------------------------------------------------------
    // C3 — Phase-2 UPDATE CDC behavior. This test exists to DISCOVER
    // empirically whether V2's standalone-UPDATE Phase-2 fires CDC on
    // idempotent redeploy. SQL Server 2022's MERGE → CDC pipeline
    // doesn't capture no-op MERGE-MATCHED-UPDATE (per CdcSilenceTests
    // comment); whether the same holds for standalone UPDATE is the
    // open question.
    // ----------------------------------------------------------------

    [<Fact>]
    member _.``5.13.cdc-silence-cross-emitter C3: Phase-2 UPDATE redeploy fires zero NEW CDC captures (discovery)`` () =
        if not (skipIfNoDocker "cdc-x-c3") then () else
        let legacy = mkLegacyOrderKind ()
        let catalog = mkCatalog [ legacy ]
        let cdcAware =
            CdcAwareness.create (Set.ofList [ legacy.SsKey ]) Map.empty
        let profile = { Profile.empty with CdcAwareness = cdcAware }
        // Self-FK cycle → Phase-2 UPDATE on ParentId.
        let migration = migrationCtxFor legacy.SsKey "1"
        let schemaSql = buildSchemaSqlFor catalog
        let seedSql =
            DataEmissionComposer.composeRenderedFull
                policyAllRemaining catalog profile
                migration UserRemapContext.empty
            |> mustOkEmit
        // Sanity: composed output should include a Phase-2 UPDATE
        // (the cycle-broken kind's nullable-FK deferral). ScriptDom
        // renders the standalone UPDATE with two spaces between
        // `UPDATE` and the bracketed table identifier.
        Assert.Contains ("UPDATE  [dbo].[CDCX_LEGACY_ORDER]", seedSql)
        let tracked = cdcAwareTracked [ legacy ]
        let baseline, post =
            (runScenarioMulti seedSql seedSql tracked schemaSql).GetAwaiter().GetResult()
        Assert.True (baseline >= 1,
            sprintf "expected ≥1 CDC entry from initial INSERT; got %d" baseline)
        // Discovery — IF this fails, V2's standalone-UPDATE leaks CDC
        // and the implementation needs `buildUpdateStatement` to emit
        // a change-detection predicate.
        Assert.Equal (baseline, post)

    // ----------------------------------------------------------------
    // C4 — sensitivity: changed-content redeploy DOES fire CDC via
    // composer. Rules out trivial-pass scenarios.
    // ----------------------------------------------------------------

    [<Fact>]
    member _.``5.13.cdc-silence-cross-emitter C4 sensitivity: changed-content composer redeploy DOES fire CDC`` () =
        if not (skipIfNoDocker "cdc-x-c4") then () else
        let country = mkCountryKind ()
        let catalog = mkCatalog [ country ]
        let cdcAware = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
        let profile = { Profile.empty with CdcAwareness = cdcAware }
        let schemaSql = buildSchemaSqlFor catalog

        // First seed: Country row 1 = "United States" / row 2 = "Canada"
        let firstSeed =
            DataEmissionComposer.composeRendered policyAllRemaining catalog profile
            |> mustOkEmit

        // Second seed: row 1's Label changed to "USA". CDC must capture
        // the change-row (proving the canary mechanism is real).
        let mutatedCountry =
            let row code label =
                { Identifier = mkKey ["Country"; "Row"; code]
                  Values =
                      Map.ofList
                          [ mkName "Id",    code
                            mkName "Code",  code
                            mkName "Label", label ] }
            { country with
                Modality = [ Static [ row "1" "USA"
                                      row "2" "Canada" ] ] }
        let mutatedCatalog = mkCatalog [ mutatedCountry ]
        let secondSeed =
            DataEmissionComposer.composeRendered policyAllRemaining mutatedCatalog profile
            |> mustOkEmit
        let tracked = cdcAwareTracked [ country ]
        let baseline, post =
            (runScenarioMulti firstSeed secondSeed tracked schemaSql).GetAwaiter().GetResult()
        Assert.True (baseline >= 2,
            sprintf "expected ≥2 CDC entries from initial INSERTs; got %d" baseline)
        Assert.True (post > baseline,
            sprintf "expected MORE CDC entries after content change (sensitivity check); baseline=%d post=%d" baseline post)
