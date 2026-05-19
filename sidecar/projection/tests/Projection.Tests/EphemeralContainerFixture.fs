namespace Projection.Tests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Pipeline
open Projection.Targets.SSDT

/// Per-test-class shared ephemeral SQL Server container (slice
/// A.4.7'-prelude.test-fixture-lift, 2026-05-19).
///
/// **Why this exists.** `Deploy.useEphemeralContainer` owns container
/// lifecycle for one body — callers that invoke it per-test pay the
/// ~5-10s container cold-start every time. This fixture amortizes
/// that cost over a test class: xUnit calls `InitializeAsync` once
/// before the first test in the class, `DisposeAsync` once after the
/// last; all tests in the class share the `MasterConnectionString`.
///
/// **Per-test isolation preserved.** Each test still creates its
/// own `<prefix>_<guid>` database via `WithEphemeralDatabase`; the
/// shared container provides the master surface, not shared state.
/// CDC tests (which have instance-wide side effects on
/// `master.sys.databases.is_cdc_enabled`) keep absolute isolation
/// because the container is per-class — sibling test classes get
/// their own container per the `Docker-SqlServer` collection's
/// `DisableParallelization = true`.
///
/// **Usage:**
///
/// ```fsharp
/// [<Xunit.Collection("Docker-SqlServer")>]
/// type MyDockerTests(fixture: EphemeralContainerFixture) =
///     interface IClassFixture<EphemeralContainerFixture>
///
///     [<Fact>]
///     member _.``my test`` () =
///         task {
///             let! result =
///                 fixture.WithEphemeralDatabase "MyPrefix" (fun cnn -> task {
///                     do! Deploy.executeBatch cnn schemaSql
///                     // ... per-test work ...
///                     return computed
///                 })
///             Assert.True(result)
///         } |> ignore
/// ```
type EphemeralContainerFixture() =

    let mutable handle : Deploy.EphemeralContainerHandle option = None

    interface IAsyncLifetime with
        member _.InitializeAsync() =
            task {
                let! h = Deploy.acquireEphemeralContainer ()
                handle <- Some h
            } :> Task

        member _.DisposeAsync() =
            task {
                match handle with
                | Some h ->
                    do! h.DisposeAsync()
                    handle <- None
                | None -> ()
            } :> Task

    member _.MasterConnectionString : string =
        match handle with
        | Some h -> h.MasterConnectionString
        | None ->
            invalidOp
                "EphemeralContainerFixture used before InitializeAsync completed."

    /// Per-test ephemeral database lifecycle on the shared container.
    /// Creates `<prefix>_<guid>` via the master connection; opens a
    /// connection on the per-DB connection string and passes it to
    /// `body`; best-effort drops the database (with
    /// `SINGLE_USER WITH ROLLBACK IMMEDIATE`) on exit even when the
    /// body raises.
    member this.WithEphemeralDatabase
            (prefix: string)
            (body: SqlConnection -> Task<'a>)
            : Task<'a> =
        task {
            let dbName =
                System.String.Concat(
                    prefix,
                    "_",
                    System.Guid.NewGuid().ToString("N").Substring(0, 8))
            let masterConn = this.MasterConnectionString
            do! task {
                use cnnMaster = new SqlConnection(masterConn)
                do! cnnMaster.OpenAsync()
                do! Deploy.executeBatch cnnMaster
                        (System.String.Concat(
                            "CREATE DATABASE ", Render.quote dbName, ";"))
            }
            let perDbConn = Deploy.ConnectionString.buildPerDb masterConn dbName
            try
                use cnnPerDb = new SqlConnection(perDbConn)
                do! cnnPerDb.OpenAsync()
                return! body cnnPerDb
            finally
                try
                    use cnnDrop = new SqlConnection(masterConn)
                    cnnDrop.OpenAsync().GetAwaiter().GetResult()
                    let q = Render.quote dbName
                    Deploy.executeBatch cnnDrop
                        (System.String.Concat(
                            "ALTER DATABASE ", q,
                            " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ",
                            "DROP DATABASE ", q, ";"))
                    |> fun t -> t.GetAwaiter().GetResult()
                with _ -> ()
        }
