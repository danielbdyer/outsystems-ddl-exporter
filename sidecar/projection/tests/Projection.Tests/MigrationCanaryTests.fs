module Projection.Tests.MigrationCanaryTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Pipeline
open Projection.Tests.Fixtures

// 6.D.1 — the LIVE A→B canary: the master equation T16 realized on real SQL
// Server (the operator's target). The pure tests prove the composition + the
// structural round-trip; this proves `MigrationRun.execute` evolves a deployed
// state-A database to state B with **minimum-viable touches** across THREE
// channels at once — a physical table rename (sp_rename), a widening reshape
// (ALTER COLUMN), and a new column (ADD COLUMN) — that B' reproduces B at the
// PhysicalSchema level, that existing data SURVIVES (the whole point of a
// differential vs a drop+recreate), and that a re-run is idempotent (resumable
// by construction). Docker-SqlServer collection; blocking wait via TaskSync.

[<Xunit.Collection("Docker-SqlServer")>]
type MigrationCanaryTests(fixture: EphemeralContainerFixture) =

    static let mkKey (label: string) : SsKey = SsKey.synthesized "MIGAB" label |> Result.value
    static let nm (s: string) : Name = Name.create s |> Result.value

    static let mkAttr key col typ len isPk nullable : Attribute =
        { Attribute.create (mkKey key) (nm col) typ with
            Column = ColumnRealization.create (col) (nullable) |> Result.value
            Length = len
            IsPrimaryKey = isPk
            IsMandatory = isPk }

    /// State A: table MIGAB_CUSTOMER, Email NVARCHAR(50), no Loyalty.
    static let catalogA : Catalog =
        let customer =
            Kind.create (mkKey "Customer") (nm "Customer")
                (mkTableId "dbo" "MIGAB_CUSTOMER")
                [ mkAttr "Customer.Id" "ID" Integer None true false
                  mkAttr "Customer.Email" "EMAIL" Text (Some 50) false true ]
        Catalog.create
            [ { SsKey = mkKey "Mod"; Name = nm "MigMod"; Kinds = [ customer ]; IsActive = true; ExtendedProperties = [] } ]
            []
        |> Result.value

    /// State B: same kind SsKey, RENAMED to Patron / table MIGAB_PATRON; Email
    /// WIDENED to NVARCHAR(256); a NEW Loyalty column (Integer, nullable) appended.
    static let catalogB : Catalog =
        let patron =
            Kind.create (mkKey "Customer") (nm "Patron")
                (mkTableId "dbo" "MIGAB_PATRON")
                [ mkAttr "Customer.Id" "ID" Integer None true false
                  mkAttr "Customer.Email" "EMAIL" Text (Some 256) false true
                  mkAttr "Customer.Loyalty" "LOYALTY" Integer None false true ]
        Catalog.create
            [ { SsKey = mkKey "Mod"; Name = nm "MigMod"; Kinds = [ patron ]; IsActive = true; ExtendedProperties = [] } ]
            []
        |> Result.value

    /// State A': Customer with EMAIL. State B': EMAIL renamed to CONTACT (logical
    /// Name + physical ColumnName), table unchanged — a pure column rename.
    static let catalogAcol : Catalog =
        let customer =
            Kind.create (mkKey "C2") (nm "Customer")
                (mkTableId "dbo" "MIGAB_COL")
                [ mkAttr "C2.Id" "ID" Integer None true false
                  mkAttr "C2.Email" "EMAIL" Text (Some 100) false true ]
        Catalog.create [ { SsKey = mkKey "Mod2"; Name = nm "MigMod2"; Kinds = [ customer ]; IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    static let catalogBcol : Catalog =
        let customer =
            Kind.create (mkKey "C2") (nm "Customer")
                (mkTableId "dbo" "MIGAB_COL")
                [ mkAttr "C2.Id" "ID" Integer None true false
                  { mkAttr "C2.Email" "CONTACT" Text (Some 100) false true with Name = nm "Contact" } ]
        Catalog.create [ { SsKey = mkKey "Mod2"; Name = nm "MigMod2"; Kinds = [ customer ]; IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    static let scalarString (cnn: Microsoft.Data.SqlClient.SqlConnection) (sql: string) =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! v = cmd.ExecuteScalarAsync()
            return string v
        }

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``migrate A B canary: one execute evolves A→B across three channels; B reproduces B, data survives, re-run is idempotent`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "MigrateAB" (fun conn _ ->
                task {
                    // Deploy state A and seed a row.
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogA |> Render.toText)
                    do! Deploy.executeBatch conn
                            "INSERT INTO [dbo].[MIGAB_CUSTOMER] ([ID],[EMAIL]) VALUES (1, N'alice@example.com');"

                    // One command: migrate A → B against the live deployed DB.
                    let! outcome = MigrationRun.execute true false catalogA catalogB conn
                    match outcome with
                    | Error e -> Assert.Fail(sprintf "migrate execute failed: %A" e)
                    | Ok result ->
                        // (1) B' reproduces B at the physical level — the master equation, live.
                        Assert.True(
                            result.Verified,
                            sprintf "B' did not reproduce B:\n%s" (PhysicalSchema.renderDiff result.SchemaDiff))

                        // (2) Minimum-viable touches: the table was RENAMED (sp_rename),
                        // not dropped+recreated — so the old name is gone, the new exists.
                        let! oldExists =
                            scalarString conn
                                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MIGAB_CUSTOMER';"
                        Assert.Equal("0", oldExists)
                        let! newExists =
                            scalarString conn
                                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MIGAB_PATRON';"
                        Assert.Equal("1", newExists)

                        // (3) The reshape landed — EMAIL widened to 256.
                        let! maxLen =
                            scalarString conn
                                "SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS \
                                 WHERE TABLE_NAME = 'MIGAB_PATRON' AND COLUMN_NAME = 'EMAIL';"
                        Assert.Equal("256", maxLen)

                        // (4) The add landed — LOYALTY column exists.
                        let! loyalty =
                            scalarString conn
                                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS \
                                 WHERE TABLE_NAME = 'MIGAB_PATRON' AND COLUMN_NAME = 'LOYALTY';"
                        Assert.Equal("1", loyalty)

                        // (5) DATA SURVIVED the differential — the seeded row is intact
                        // in the renamed, reshaped table (a drop+recreate would lose it).
                        let! email = scalarString conn "SELECT [EMAIL] FROM [dbo].[MIGAB_PATRON] WHERE [ID] = 1;"
                        Assert.Equal("alice@example.com", email)

                        // (6) Idempotent + resumable by construction: re-running migrate
                        // A→B against the now-migrated DB is a no-op (empty differential).
                        // We re-plan from B (the new current state) → B: zero touches.
                        let! rerun = MigrationRun.execute true false catalogB catalogB conn
                        match rerun with
                        | Error e -> Assert.Fail(sprintf "idempotent re-run failed: %A" e)
                        | Ok r2 ->
                            Assert.True(Migration.isIdempotent r2.Artifacts.Plan)
                            Assert.Empty(r2.Artifacts.SchemaStatements)
                            Assert.True(r2.Verified)
                }))

    [<Fact>]
    member _.``migrate A B canary: a destructive drop refuses before touching the live DB`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "MigrateDrop" (fun conn _ ->
                task {
                    // Deploy B (the richer schema) and seed a row.
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogB |> Render.toText)
                    do! Deploy.executeBatch conn
                            "INSERT INTO [dbo].[MIGAB_PATRON] ([ID],[EMAIL],[LOYALTY]) VALUES (1, N'bob@example.com', 7);"

                    // migrate B → A would DROP the Loyalty column (and rename back).
                    // Without allowDrops it must refuse BEFORE any write.
                    let! outcome = MigrationRun.execute true false catalogB catalogA conn
                    match outcome with
                    | Error (RefusedByViolations _) -> ()
                    | other -> Assert.Fail(sprintf "expected RefusedByViolations, got %A" other)

                    // The live DB is untouched — the row and the Loyalty column survive.
                    let! loyalty =
                        scalarString conn
                            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS \
                             WHERE TABLE_NAME = 'MIGAB_PATRON' AND COLUMN_NAME = 'LOYALTY';"
                    Assert.Equal("1", loyalty)
                    let! email = scalarString conn "SELECT [EMAIL] FROM [dbo].[MIGAB_PATRON] WHERE [ID] = 1;"
                    Assert.Equal("bob@example.com", email)
                }))

    // -- column rename (the second rename channel) ---------------------------

    [<Fact>]
    member _.``migrate canary: a column rename executes via sp_rename COLUMN and preserves data`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "MigrateCol" (fun conn _ ->
                task {
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogAcol |> Render.toText)
                    do! Deploy.executeBatch conn
                            "INSERT INTO [dbo].[MIGAB_COL] ([ID],[EMAIL]) VALUES (1, N'carol@example.com');"

                    let! outcome = MigrationRun.execute true false catalogAcol catalogBcol conn
                    match outcome with
                    | Error e -> Assert.Fail(sprintf "column-rename migrate failed: %A" e)
                    | Ok result ->
                        Assert.True(result.Verified, sprintf "B' did not reproduce B:\n%s" (PhysicalSchema.renderDiff result.SchemaDiff))
                        // The column was renamed (EMAIL gone, CONTACT present) — not dropped+added.
                        let! oldCol =
                            scalarString conn
                                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='MIGAB_COL' AND COLUMN_NAME='EMAIL';"
                        Assert.Equal("0", oldCol)
                        let! newCol =
                            scalarString conn
                                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='MIGAB_COL' AND COLUMN_NAME='CONTACT';"
                        Assert.Equal("1", newCol)
                        // The data survived the rename (a drop+add would lose it).
                        let! contact = scalarString conn "SELECT [CONTACT] FROM [dbo].[MIGAB_COL] WHERE [ID]=1;"
                        Assert.Equal("carol@example.com", contact)
                }))

    // -- the data-transfer composition (cross-substrate: schema + data) ------

    [<Fact>]
    member _.``migrate canary: executeWithData migrates the sink schema then loads rows from the source`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            // Source DB at state B with seeded rows; sink DB at state A (empty).
            fixture.WithEphemeralDatabase "MigrateDataSrc" (fun source _ ->
                task {
                    do! Deploy.executeBatch source (SsdtDdlEmitter.statements catalogB |> Render.toText)
                    do! Deploy.executeBatch source
                            "INSERT INTO [dbo].[MIGAB_PATRON] ([ID],[EMAIL],[LOYALTY]) VALUES (1, N'dave@example.com', 3), (2, N'erin@example.com', 9);"
                    do! fixture.WithEphemeralDatabase "MigrateDataSink" (fun sink _ ->
                        task {
                            do! Deploy.executeBatch sink (SsdtDdlEmitter.statements catalogA |> Render.toText)

                            // One call: migrate the sink A→B, THEN transfer the rows source→sink.
                            let! outcome =
                                MigrationRun.executeWithData false Transfer.Execute true catalogA catalogB Map.empty source sink
                            match outcome with
                            | Error e -> Assert.Fail(sprintf "executeWithData failed: %A" e)
                            | Ok result ->
                                // Schema leg: the sink is now at B.
                                Assert.True(result.Schema.Verified, sprintf "sink schema not at B:\n%s" (PhysicalSchema.renderDiff result.Schema.SchemaDiff))
                                // Data leg: both rows landed in the sink's migrated table.
                                let! count = scalarString sink "SELECT COUNT(*) FROM [dbo].[MIGAB_PATRON];"
                                Assert.Equal("2", count)
                                let! email = scalarString sink "SELECT [EMAIL] FROM [dbo].[MIGAB_PATRON] WHERE [ID]=2;"
                                Assert.Equal("erin@example.com", email)
                        })
                }))

    // 6.A.13 — schema-side CDC pre-flight. A migrate that would emit DDL
    // against a CDC-tracked DB is refused unless --allow-cdc (mirrors the
    // transfer gate). An UNCHANGED schema emits zero DDL, so it is a no-op
    // regardless of CDC — engine-level CDC-silence. Skips gracefully if the
    // container cannot enable CDC.
    [<Fact>]
    member _.``6.A.13: migrate refuses schema DDL against a CDC-tracked DB unless allow-cdc; unchanged is CDC-silent`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "MigrateCdc" (fun conn _ ->
                task {
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogA |> Render.toText)
                    let! enabled =
                        task {
                            try
                                do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_db;"
                                do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_table @source_schema = N'dbo', @source_name = N'MIGAB_CUSTOMER', @role_name = NULL, @supports_net_changes = 0;"
                                use cmd = conn.CreateCommand()
                                cmd.CommandText <- "SELECT COUNT(*) FROM sys.tables WHERE is_tracked_by_cdc = 1 AND name = N'MIGAB_CUSTOMER'"
                                let! c = cmd.ExecuteScalarAsync()
                                return System.Convert.ToInt32 c > 0
                            with _ -> return false
                        }
                    if not enabled then
                        printfn "SKIP 6.A.13: container did not enable CDC (flag not set)"
                    else
                        // Unchanged schema (A→A): zero DDL → CDC-silent, proceeds even with allowCdc=false.
                        let! silent = MigrationRun.execute false false catalogA catalogA conn
                        match silent with
                        | Ok r -> Assert.Empty(r.Artifacts.SchemaStatements)
                        | Error e -> Assert.Fail(sprintf "unchanged schema should be CDC-silent (no DDL), got %A" e)

                        // A real change (A→B) against the CDC-tracked DB refuses unless allow-cdc.
                        let! refused = MigrationRun.execute false false catalogA catalogB conn
                        match refused with
                        | Error (MigrationError.RefusedByCdc tracked) ->
                            Assert.Contains("dbo.MIGAB_CUSTOMER", tracked)
                        | other -> Assert.Fail(sprintf "expected RefusedByCdc, got %A" other)

                        // --allow-cdc overrides → no longer BLOCKED BY THE GATE.
                        // (DDL against a CDC-tracked table may still fail at the
                        // SQL level — that is the unsafe operation the gate guards
                        // — but that is not a RefusedByCdc refusal.)
                        let! allowed = MigrationRun.execute true false catalogA catalogB conn
                        match allowed with
                        | Error (MigrationError.RefusedByCdc _) ->
                            Assert.Fail("allow-cdc must bypass the CDC gate, not refuse")
                        | _ -> ()
                }))
