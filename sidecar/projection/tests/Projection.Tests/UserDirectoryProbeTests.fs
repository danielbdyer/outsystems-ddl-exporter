namespace Projection.Tests

// ---------------------------------------------------------------------------
// G0b (P10) — the user-directory-readability survey probe's Docker witness.
//
// `ReadSide.userDirectoryReadability` reports whether a platform user-directory
// table is SELECT-able and whether it exposes an email-shaped key column — the
// FK target the `golden`/`preview` flows re-key against by email
// (THE_DATA_PRODUCERS §2). It is a NEW FIELD on `CapabilitySurvey
// .EnvironmentReport`, never a `Capability` DU variant (the required ⇔ surveyed
// totality stays untouched; pinned pure in `CapabilitySurveyTests`).
//
// This OFFLINE witness deploys a STAND-IN user table and discriminates the
// three verdicts with exact assertions:
//
//   - email-keyed leg: a stand-in `OSSYS_USER (Id, EMAIL, ...)` ⇒
//     Found = true, EmailKeyed = true. A probe blind to the email column turns
//     this RED.
//   - no-email-key leg: a stand-in `OSSYS_USER (Id, Name)` with no email column
//     ⇒ Found = true, EmailKeyed = false. A probe that reports email-keyed on a
//     table that has none turns this RED.
//   - absent leg: no candidate user table at all ⇒ Found = false (not readable).
//     A probe that hallucinates a directory turns this RED.
//
// RESIDUAL (OPEN-2): the real OutSystems platform user-table identity
// (`OSSYS_USER` vs an app user entity) is instance-specific. This witness proves
// the PROBE SEMANTICS over a stand-in; the candidate set is operator-configurable
// (`userDirectoryReadability` takes the names) so the real-instance identity is a
// config decision, not a code change. Validating against a real instance is the
// residual that pairs with OPEN-2.
//
// Docker test: `Docker-SqlServer` collection + warm-pool `EphemeralContainerFixture`
// (no CDC needed) + `TaskSync.run` for the sync-over-async boundary.
// ---------------------------------------------------------------------------

open Microsoft.Data.SqlClient
open Xunit
open Projection.Adapters.Sql
open Projection.Pipeline

module private UserDirectoryProbeFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn "SKIP %s: Docker daemon not reachable." label
            false

    // A stand-in platform user table named from the conventional candidate set
    // (`OSSYS_USER`) with an email-shaped column — the email-keyed shape.
    let emailKeyedSql : string =
        "CREATE TABLE dbo.OSSYS_USER ( \
           Id INT NOT NULL PRIMARY KEY, \
           Username NVARCHAR(50) NOT NULL, \
           EMAIL NVARCHAR(200) NOT NULL );"

    // A stand-in user table with NO email column — the no-email-key shape.
    let noEmailSql : string =
        "CREATE TABLE dbo.OSSYS_USER ( \
           Id INT NOT NULL PRIMARY KEY, \
           Username NVARCHAR(50) NOT NULL );"

open UserDirectoryProbeFixtures

[<Xunit.Collection("Docker-SqlServer")>]
type UserDirectoryProbeTests(fixture: EphemeralContainerFixture) =

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``G0b P10: a stand-in user table with an email column probes readable + email-keyed`` () =
        if not (skipIfNoDocker "user-dir-email-keyed") then () else

        let probe =
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "UserDirEmailKeyed" (fun cnn _ -> task {
                    do! Deploy.executeBatch cnn emailKeyedSql
                    return! ReadSide.userDirectoryReadability [] cnn
                }))

        Assert.True(probe.Found, "the stand-in user table should be found + SELECT-able")
        Assert.True(probe.EmailKeyed, "the EMAIL column should mark it email-keyed")
        Assert.Equal(Some "dbo.OSSYS_USER", probe.TableName)

    [<Fact>]
    member _.``G0b P10: a stand-in user table with no email column probes readable but not email-keyed`` () =
        if not (skipIfNoDocker "user-dir-no-email") then () else

        let probe =
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "UserDirNoEmail" (fun cnn _ -> task {
                    do! Deploy.executeBatch cnn noEmailSql
                    return! ReadSide.userDirectoryReadability [] cnn
                }))

        Assert.True(probe.Found, "the stand-in user table should be found")
        Assert.False(probe.EmailKeyed, "no email column ⇒ not email-keyed")

    [<Fact>]
    member _.``G0b P10: a database with no candidate user table probes not-readable (absent)`` () =
        if not (skipIfNoDocker "user-dir-absent") then () else

        let probe =
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "UserDirAbsent" (fun cnn _ -> task {
                    // Deploy an UNRELATED table only — no candidate user table.
                    do! Deploy.executeBatch cnn "CREATE TABLE dbo.Widget ( Id INT NOT NULL PRIMARY KEY );"
                    return! ReadSide.userDirectoryReadability [] cnn
                }))

        Assert.False(probe.Found, "no candidate user table ⇒ not found")
        Assert.False(probe.EmailKeyed)
        Assert.Equal(None, probe.TableName)

    [<Fact>]
    member _.``G0b P10: the candidate set is operator-configurable (a custom table name resolves)`` () =
        if not (skipIfNoDocker "user-dir-configurable") then () else

        // OPEN-2 residual: the real instance may name its user entity differently.
        // The probe takes the candidate names, so a custom name resolves — the
        // real-instance identity is config, not a code change.
        let probe =
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "UserDirConfigurable" (fun cnn _ -> task {
                    do! Deploy.executeBatch cnn
                            "CREATE TABLE dbo.OSUSR_ABC_APPUSER ( Id INT NOT NULL PRIMARY KEY, EmailAddress NVARCHAR(200) NOT NULL );"
                    // The conventional set would NOT find this name...
                    let! byConvention = ReadSide.userDirectoryReadability [] cnn
                    // ...but the operator-configured candidate does.
                    let! byConfig = ReadSide.userDirectoryReadability [ "OSUSR_ABC_APPUSER" ] cnn
                    return byConvention, byConfig
                }))

        let byConvention, byConfig = probe
        Assert.False(byConvention.Found, "the conventional candidate set should not match a custom app-user name")
        Assert.True(byConfig.Found, "the operator-configured candidate name should resolve")
        Assert.True(byConfig.EmailKeyed, "the EmailAddress column is email-shaped (LIKE %EMAIL%)")
