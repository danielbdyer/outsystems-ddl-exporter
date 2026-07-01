namespace Projection.Tests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Pipeline
open Projection.Targets.SSDT

/// Per-test ephemeral-database lifecycle on a shared master
/// connection — the body both `EphemeralContainerFixture` and
/// `IsolatedContainerFixture` delegate to. Extracted so the two
/// fixtures differ ONLY in how they acquire the master surface
/// (warm-honoring vs. always-isolated), not in per-test DB handling.
[<RequireQualifiedAccess>]
module ContainerFixtureSupport =

    /// Create `<prefix>_<guid>` on `masterConn`, open a connection on
    /// the per-DB connection string, pass BOTH the open connection AND
    /// the per-DB connection string to `body`, and best-effort drop the
    /// database (`SINGLE_USER WITH ROLLBACK IMMEDIATE`) on exit even
    /// when the body raises. Per chapter-4.7 sibling-wrapper discipline:
    /// the body receives the full information-bearing surface; callers
    /// that don't need the string drop it via `_`.
    let withEphemeralDatabase
            (masterConn: string)
            (prefix: string)
            (body: SqlConnection -> string -> Task<'a>)
            : Task<'a> =
        task {
            let dbName =
                System.String.Concat(
                    prefix,
                    "_",
                    System.Guid.NewGuid().ToString("N").Substring(0, 8))
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
                return! body cnnPerDb perDbConn
            finally
                try
                    // Evict the per-DB connection POOL before the drop: the
                    // body's `use` returned its physical connection to the
                    // pool still OPEN, and a SINGLE_USER WITH ROLLBACK
                    // IMMEDIATE that must KILL that idle session costs ~3.0s
                    // per drop (measured 2026-06-12: 3051ms with one idle
                    // session vs 51ms with none — the flat ~3.4s per-test
                    // floor the docker pool's TRX showed). ClearPool closes
                    // it client-side, so the drop pays the cheap path.
                    let cnnEvict = new SqlConnection(perDbConn)
                    SqlConnection.ClearPool cnnEvict
                    cnnEvict.Dispose()
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

/// Per-test-class shared SQL Server container (slice
/// A.4.7'-prelude.test-fixture-lift, 2026-05-19; warm-reuse 2026-06-04).
///
/// **Why this exists.** `Deploy.useEphemeralContainer` owns container
/// lifecycle for one body — callers that invoke it per-test pay the
/// ~5-10s container cold-start every time. This fixture amortizes
/// that cost over a test class: xUnit calls `InitializeAsync` once
/// before the first test in the class, `DisposeAsync` once after the
/// last; all tests in the class share the `MasterConnectionString`.
///
/// **Warm-reuse (dev loop).** Acquisition flows through
/// `Deploy.acquireContainer ()`, which honors `PROJECTION_MSSQL_CONN_STR`:
/// when the warm container env var is set, EVERY class in the run
/// shares ONE long-lived SQL Server (no per-class cold-start; the
/// fixture's `DisposeAsync` is a no-op — the warm container outlives
/// the process). Unset → falls back to a fresh Testcontainers instance
/// per class (CI behavior, unchanged). `scripts/warm-sql.sh` starts the
/// warm container and prints the env var.
///
/// **Per-test isolation preserved.** Each test still creates its own
/// `<prefix>_<guid>` database via `WithEphemeralDatabase`; the shared
/// container/instance provides the master surface, not shared state.
///
/// **CDC and other instance-wide-state tests must use
/// `IsolatedContainerFixture` instead** — they pollute
/// `master.sys.databases.is_cdc_enabled` / server-level config and
/// would contaminate (or livelock against) the shared warm instance.
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
///                 fixture.WithEphemeralDatabase "MyPrefix" (fun cnn _ -> task {
///                     do! Deploy.executeBatch cnn schemaSql
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
                let! h = Deploy.acquireContainer ()
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

    /// Per-test ephemeral database lifecycle on the shared master.
    member this.WithEphemeralDatabase
            (prefix: string)
            (body: SqlConnection -> string -> Task<'a>)
            : Task<'a> =
        ContainerFixtureSupport.withEphemeralDatabase
            this.MasterConnectionString prefix body

/// Per-test-class **always-isolated** SQL Server container — identical
/// per-test-database semantics to `EphemeralContainerFixture` but
/// acquisition ALWAYS cold-starts a fresh Testcontainers instance
/// (`Deploy.acquireEphemeralContainer ()`), bypassing the
/// `PROJECTION_MSSQL_CONN_STR` warm shortcut.
///
/// **Why CDC needs this** (chapter 4.1.B slice δ). `sp_cdc_enable_db` /
/// `sp_cdc_enable_table` set up per-DB capture infrastructure AND flip
/// `master.sys.databases.is_cdc_enabled` on the parent instance;
/// sharing the warm instance across CDC classes would leak that
/// instance-wide state (and, under any concurrency, livelock on
/// `master`-database locks against the CDC scan/capture path). A
/// dedicated container per CDC class keeps that footprint contained —
/// the structural fix the `Docker-SqlServer` collection's
/// `DisableParallelization` complements. CDC test classes take this
/// fixture; everything else takes `EphemeralContainerFixture`.
type IsolatedContainerFixture() =

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
                "IsolatedContainerFixture used before InitializeAsync completed."

    /// Per-test ephemeral database lifecycle on the dedicated master.
    member this.WithEphemeralDatabase
            (prefix: string)
            (body: SqlConnection -> string -> Task<'a>)
            : Task<'a> =
        ContainerFixtureSupport.withEphemeralDatabase
            this.MasterConnectionString prefix body
