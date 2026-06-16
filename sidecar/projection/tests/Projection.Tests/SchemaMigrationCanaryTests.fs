module Projection.Tests.SchemaMigrationCanaryTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Pipeline

// 6.A.12 — the LIVE proof that the implied emission differential is real,
// deployable SQL on SQL Server (the operator's actual target). The pure
// tests prove the typed ALTER shape + the rendered text; this canary proves
// SQL Server ACCEPTS the emitted ALTER COLUMN and the in-place change
// PRESERVES existing data (the whole point of a minimum-viable touch vs a
// drop+recreate). Docker-SqlServer collection; blocking wait via TaskSync.

[<Xunit.Collection("Docker-SqlServer")>]
type SchemaMigrationCanaryTests(fixture: EphemeralContainerFixture) =

    static let mkKey (label: string) : SsKey = SsKey.synthesized "MIG" label |> Result.value
    static let nm (s: string) : Name = Name.create s |> Result.value

    /// A single Customer kind: PK Id + Email (Text). `emailLength` widens
    /// 50 → 256 between source and target (NVARCHAR(50) → NVARCHAR(256)).
    static let customerKind (emailLength: int option) : Kind =
        let mkAttr key col typ len isPk nullable =
            { Attribute.create (mkKey key) (nm col) typ with
                Column = ColumnRealization.create (col) (nullable) |> Result.value
                Length = len
                IsPrimaryKey = isPk
                IsMandatory = isPk }
        Kind.create (mkKey "Customer") (nm "Customer")
            (TableId.create "dbo" "MIG_CUSTOMER" |> Result.value)
            [ mkAttr "Customer.Id" "ID" Integer None true false
              mkAttr "Customer.Email" "EMAIL" Text emailLength false true ]

    static let catalogOf (emailLength: int option) : Catalog =
        Catalog.create
            [ { SsKey = mkKey "Mod"; Name = nm "MigMod"; Kinds = [ customerKind emailLength ]
                IsActive = true; ExtendedProperties = [] } ]
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
    member _.``migration canary: a widening ALTER COLUMN executes on SQL Server and preserves data`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "MigWiden" (fun conn _ ->
                task {
                    let source = catalogOf (Some 50)
                    let target = catalogOf (Some 256)

                    // Deploy the SOURCE schema (NVARCHAR(50)) and seed a row.
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements source |> Render.toText)
                    do! Deploy.executeBatch conn
                            "INSERT INTO [dbo].[MIG_CUSTOMER] ([ID],[EMAIL]) VALUES (1, N'alice@example.com');"

                    // The implied emission differential: diff → minimal ALTER.
                    let diff = CatalogDiff.between source target
                    let migration = SchemaMigrationEmitter.emit diff

                    // No fail-loud refusals — a pure widening is emittable.
                    Assert.False(
                        migration.Entries |> List.exists (fun e -> e.Severity = DiagnosticSeverity.Error),
                        sprintf "unexpected refusals: %A" migration.Entries)
                    // It is an ALTER, not a re-CREATE.
                    Assert.True(migration.Value |> List.exists (function Statement.AlterTableAlterColumn _ -> true | _ -> false))
                    Assert.False(migration.Value |> List.exists (function Statement.CreateTable _ -> true | _ -> false))

                    // Execute the migration against the live, populated DB.
                    do! Deploy.executeBatch conn (migration.Value |> Render.toText)

                    // (a) The column actually widened to NVARCHAR(256).
                    let! maxLen =
                        scalarString conn
                            "SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS \
                             WHERE TABLE_NAME = 'MIG_CUSTOMER' AND COLUMN_NAME = 'EMAIL';"
                    Assert.Equal("256", maxLen)

                    // (b) The existing row's data survived the in-place ALTER.
                    let! email = scalarString conn "SELECT [EMAIL] FROM [dbo].[MIG_CUSTOMER] WHERE [ID] = 1;"
                    Assert.Equal("alice@example.com", email)
                }))
