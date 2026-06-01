module Projection.Tests.MigrationCanaryTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Pipeline

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
            Column = { ColumnName = col; IsNullable = nullable }
            Length = len
            IsPrimaryKey = isPk
            IsMandatory = isPk }

    /// State A: table MIGAB_CUSTOMER, Email NVARCHAR(50), no Loyalty.
    static let catalogA : Catalog =
        let customer =
            Kind.create (mkKey "Customer") (nm "Customer")
                { Schema = "dbo"; Table = "MIGAB_CUSTOMER"; Catalog = None }
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
                { Schema = "dbo"; Table = "MIGAB_PATRON"; Catalog = None }
                [ mkAttr "Customer.Id" "ID" Integer None true false
                  mkAttr "Customer.Email" "EMAIL" Text (Some 256) false true
                  mkAttr "Customer.Loyalty" "LOYALTY" Integer None false true ]
        Catalog.create
            [ { SsKey = mkKey "Mod"; Name = nm "MigMod"; Kinds = [ patron ]; IsActive = true; ExtendedProperties = [] } ]
            []
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
                    let! outcome = MigrationRun.execute false catalogA catalogB conn
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
                        let! rerun = MigrationRun.execute false catalogB catalogB conn
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
                    let! outcome = MigrationRun.execute false catalogB catalogA conn
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
