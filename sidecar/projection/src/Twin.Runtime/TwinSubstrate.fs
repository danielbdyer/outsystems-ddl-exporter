namespace Twin.Runtime

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Twin.Core

/// The substrate a run resolved: where the twin lives and how to reach
/// it, whichever section configured it.
type ResolvedSubstrate = {
    /// Server-level (master) connection string.
    ServerConnectionString : string
    /// The twin database's connection string.
    TwinConnectionString   : string
    /// The twin database's name on the substrate.
    TwinDatabase           : string
    /// True when the twin manages the container's lifecycle.
    Managed                : bool
}

/// The `twin down` outcome, per substrate.
type DownOutcome =
    | ContainerStopped
    /// An existing server is not the twin's to stop — the named no-op.
    | ExternalServerLeft

/// The `twin reset` outcome, per substrate.
type ResetOutcome =
    | ContainerRemoved
    /// Only the twin database was dropped; the server stands.
    | DatabaseDropped of database: string

/// THE TWIN — the substrate seam (Twin.Runtime).
///
/// One choice, decided by twin.json: a MANAGED CONTAINER (the default —
/// the twin owns its lifecycle: create, start, stop, remove) or an
/// EXISTING SERVER (`server.conn` + `server.database` — LocalDB,
/// Developer edition, any reachable instance; no Docker on the machine).
/// Every verb resolves through this module instead of reaching for the
/// container directly, and the external mode's contract is deliberate:
/// `up` requires the server reachable and never provisions it, `down`
/// is a named no-op, and `reset` drops only the twin database.
[<RequireQualifiedAccess>]
module TwinSubstrate =

    let private unreachable (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.server.unreachable"
            "The configured server did not accept a connection. Start the engine (or fix server.conn), then retry."
            (Map.ofList [ "detail", Some detail ])

    let private connRefUnset (variable: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.server.connUnset"
            "The configured server connection variable is not set. Set it, or point server.conn at a readable file."
            (Map.ofList [ "variable", Some variable ])

    let private resolveConnRef (r: string) : Result<string> =
        if r.StartsWith "env:" then
            match System.Environment.GetEnvironmentVariable (r.Substring 4) with
            | null | "" -> Result.failureOf (connRefUnset (r.Substring 4))
            | v -> Result.success v
        elif r.StartsWith "file:" then
            try Result.success ((System.IO.File.ReadAllText (r.Substring 5)).Trim())
            with ex ->
                Result.failureOf
                    (ValidationError.createWithMetadata
                        "twin.server.connFileUnreadable"
                        "The configured server connection file could not be read."
                        (Map.ofList [ "path", Some (r.Substring 5); "detail", Some ex.Message ]))
        else
            // Unreachable when the config parsed (the reference discipline
            // refuses inline) — defensive for direct callers.
            Result.failureOf (ValidationError.create "twin.server.connInline" "server.conn must be an env: or file: reference.")

    let private external' (server: ServerSection) : Result<ResolvedSubstrate> =
        resolveConnRef server.ConnRef
        |> Result.map (fun conn ->
            let master = SqlConnectionStringBuilder conn
            master.InitialCatalog <- "master"  // LINT-ALLOW: connection-string facet assignment on the ADO.NET builder at the substrate boundary
            let twin = SqlConnectionStringBuilder conn
            twin.InitialCatalog <- server.Database  // LINT-ALLOW: connection-string facet assignment on the ADO.NET builder at the substrate boundary
            { ServerConnectionString = master.ConnectionString
              TwinConnectionString = twin.ConnectionString
              TwinDatabase = server.Database
              Managed = false })

    let private managed (container: ContainerSection) : Result<ResolvedSubstrate> =
        TwinContainer.resolvePassword container.PasswordRef
        |> Result.map (fun password ->
            { ServerConnectionString = TwinContainer.masterConnectionString container password
              TwinConnectionString = TwinContainer.twinConnectionString container password
              TwinDatabase = TwinContainer.TwinDatabaseName
              Managed = true })

    /// Resolve the substrate's connection facts. No I/O beyond the
    /// credential references; reachability is `state`/`ensureReady`.
    let resolve (config: TwinConfig) : Result<ResolvedSubstrate> =
        match config.Server with
        | Some server -> external' server
        | None -> managed config.Container

    let private probe (connStr: string) : Task<Result<unit>> =
        task {
            try
                use cnn = new SqlConnection(connStr)
                do! cnn.OpenAsync()
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- "SELECT 1;"  // LINT-ALLOW: terminal reachability-probe SQL at the command boundary
                let! _ = cmd.ExecuteScalarAsync()
                return Result.success ()
            with ex ->
                return Result.failureOf (unreachable ex.Message)
        }

    /// The substrate's current state. External: a reachability probe —
    /// `Running` when the server answers, `Stopped` when it does not (the
    /// status renderer phrases the difference; an unreachable server is
    /// a fact to report, not a refusal, on the read-only path).
    let state (config: TwinConfig) : Task<Result<TwinContainer.ContainerState>> =
        task {
            match config.Server with
            | None -> return! TwinContainer.state config.Container
            | Some server ->
                match external' server with
                | Error es -> return Result.failure es
                | Ok resolved ->
                    let! probed = probe resolved.ServerConnectionString
                    match probed with
                    | Ok () -> return Result.success TwinContainer.Running
                    | Error _ -> return Result.success TwinContainer.Stopped
        }

    /// Ready the substrate for a write path: managed — create/start the
    /// container and wait for the engine; external — require the server
    /// reachable, never provision it.
    let ensureReady (config: TwinConfig) : Task<Result<ResolvedSubstrate>> =
        task {
            match resolve config with
            | Error es -> return Result.failure es
            | Ok resolved ->
                if resolved.Managed then
                    match TwinContainer.resolvePassword config.Container.PasswordRef with
                    | Error es -> return Result.failure es
                    | Ok password ->
                        let! running = TwinContainer.ensureRunning config.Container password
                        return running |> Result.map (fun () -> resolved)
                else
                    let! probed = probe resolved.ServerConnectionString
                    return probed |> Result.map (fun () -> resolved)
        }

    /// `twin down`: stop the managed container; leave an external server
    /// alone (the named no-op — it is not the twin's to stop).
    let down (config: TwinConfig) : Task<Result<DownOutcome>> =
        task {
            match config.Server with
            | Some _ -> return Result.success ExternalServerLeft
            | None ->
                let! stopped = TwinContainer.stop config.Container
                return stopped |> Result.map (fun () -> ContainerStopped)
        }

    /// `twin reset`: remove the managed container and its data; on an
    /// external server, drop ONLY the twin database — guarded to the
    /// configured name, nothing else on the server is touched.
    let reset (config: TwinConfig) : Task<Result<ResetOutcome>> =
        task {
            match config.Server with
            | None ->
                let! removed = TwinContainer.remove config.Container
                return removed |> Result.map (fun () -> ContainerRemoved)
            | Some server ->
                match external' server with
                | Error es -> return Result.failure es
                | Ok resolved ->
                    try
                        SqlConnection.ClearAllPools()
                        use cnn = new SqlConnection(resolved.ServerConnectionString)
                        do! cnn.OpenAsync()
                        use cmd = cnn.CreateCommand()
                        cmd.CommandText <-  // LINT-ALLOW: terminal reset SQL at the command boundary
                            System.String.Concat(  // LINT-ALLOW: terminal reset SQL; the database identifier passes through the SSDT renderer's quoting, the existence guard is parameterized
                                "IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE ",
                                Projection.Targets.SSDT.Render.quote resolved.TwinDatabase,
                                " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ",
                                Projection.Targets.SSDT.Render.quote resolved.TwinDatabase,
                                "; END;")
                        cmd.Parameters.AddWithValue("@name", resolved.TwinDatabase) |> ignore
                        let! _ = cmd.ExecuteNonQueryAsync()
                        return Result.success (DatabaseDropped resolved.TwinDatabase)
                    with ex ->
                        return Result.failureOf (unreachable ex.Message)
        }
