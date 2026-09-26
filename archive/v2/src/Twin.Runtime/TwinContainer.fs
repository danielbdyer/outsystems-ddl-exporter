namespace Twin.Runtime
// LINT-ALLOW-FILE-MUTATION: the Twin's Docker container lifecycle driver —
//   start / stop / poll-until-ready over the `docker` CLI. The mutable locals
//   ARE the imperative bring-up loop's state (elapsed stopwatch, readiness
//   flag, retry counter); the operation is inherently sequential side-effecting
//   I/O against an external daemon whose state is not a value we own, so there
//   is no pure-functional equivalent — the reified-boundary posture Deploy.fs's
//   warm-container driver already carries. Mutation is confined to this module.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Twin.Core

/// THE TWIN — the container manager (Twin.Runtime).
///
/// A PERSISTENT named SQL Server container, managed through the docker
/// CLI — deliberately not Testcontainers, whose containers are ephemeral
/// by design (`WithCleanUp`); the twin outlives the process and survives
/// reboots, exactly like the dev-loop warm container `warm-sql.sh`
/// manages (whose conventions — image, memory caps, wait-ready poll —
/// this module mirrors). Testcontainers remains the right tool for
/// `twin check`'s throwaway proof database and stays untouched in the
/// kernel's `Deploy`.
[<RequireQualifiedAccess>]
module TwinContainer =

    /// The twin database name inside the container. One twin, one
    /// database — a constant, not a knob, until a consumer demands more.
    [<Literal>]
    let TwinDatabaseName = "twin"

    /// The documented local development default. Matches the posture of
    /// `warm-sql.sh` (a checked-in dev-loop default, never a production
    /// secret); override via `container.password: env:<VAR>` in twin.json.
    [<Literal>]
    let DefaultPassword = "Twin@Strong1"

    /// Mirror warm-sql.sh's host-protection caps: SQL Server's buffer
    /// pool capped below the container cap so the host stays responsive.
    [<Literal>]
    let private MemoryLimitMb = "3072"

    [<Literal>]
    let private ContainerMemory = "4g"

    type ContainerState =
        | Absent
        | Stopped
        | Running

    let private dockerUnavailable (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.container.dockerUnavailable"
            "Docker is not reachable. Start Docker, then retry."
            (Map.ofList [ "detail", Some detail ])

    let private dockerFailed (action: string) (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.container.commandFailed"
            "A docker command did not succeed."
            (Map.ofList [ "action", Some action; "detail", Some detail ])

    let private notReady (seconds: int) : ValidationError =
        ValidationError.createWithMetadata
            "twin.container.notReady"
            "SQL Server did not accept a connection within the readiness window. Check the container's logs, then retry."
            (Map.ofList [ "waitedSeconds", Some (string seconds) ])

    /// Run `docker <args>`; exit 0 → trimmed stdout, else the stderr detail.
    let private docker (action: string) (args: string list) : Task<Result<string>> =
        task {
            try
                let psi = System.Diagnostics.ProcessStartInfo(FileName = "docker")
                for a in args do psi.ArgumentList.Add a
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.UseShellExecute <- false
                match System.Diagnostics.Process.Start psi with
                | null -> return Result.failureOf (dockerUnavailable "the docker process did not start")
                | p ->
                    use p = p
                    let! stdout = p.StandardOutput.ReadToEndAsync()
                    let! stderr = p.StandardError.ReadToEndAsync()
                    do! p.WaitForExitAsync()
                    if p.ExitCode = 0 then return Result.success (stdout.Trim())
                    else return Result.failureOf (dockerFailed action (stderr.Trim()))
            with ex ->
                return Result.failureOf (dockerUnavailable ex.Message)
        }

    /// Resolve the SA password: the configured reference, or the
    /// documented local default.
    let resolvePassword (passwordRef: string option) : Result<string> =
        match passwordRef with
        | None -> Result.success DefaultPassword
        | Some r when r.StartsWith "env:" ->
            match System.Environment.GetEnvironmentVariable (r.Substring 4) with
            | null | "" ->
                Result.failureOf
                    (ValidationError.createWithMetadata
                        "twin.container.passwordUnset"
                        "The configured password environment variable is not set. Set it, or remove container.password to use the documented local default."
                        (Map.ofList [ "variable", Some (r.Substring 4) ]))
            | v -> Result.success v
        | Some r when r.StartsWith "file:" ->
            try Result.success ((System.IO.File.ReadAllText (r.Substring 5)).Trim())
            with ex ->
                Result.failureOf
                    (ValidationError.createWithMetadata
                        "twin.container.passwordFileUnreadable"
                        "The configured password file could not be read."
                        (Map.ofList [ "path", Some (r.Substring 5); "detail", Some ex.Message ]))
        | Some _ ->
            // Unreachable when the config parsed (D9 refuses inline) —
            // defensive for direct callers.
            Result.failureOf (ValidationError.create "twin.container.passwordInline" "A password must be an env: or file: reference.")

    /// The master-database connection string for the twin container.
    let masterConnectionString (container: ContainerSection) (password: string) : string =
        let b = SqlConnectionStringBuilder()
        b.DataSource <- System.String.Concat("localhost,", string container.Port)  // LINT-ALLOW: terminal connection-string component; host,port IS the wire format
        b.UserID <- "sa"
        b.Password <- password
        b.InitialCatalog <- "master"
        b.TrustServerCertificate <- true
        b.Encrypt <- SqlConnectionEncryptOption.Optional
        b.ConnectTimeout <- 5
        b.ConnectionString

    /// The twin-database connection string.
    let twinConnectionString (container: ContainerSection) (password: string) : string =
        let b = SqlConnectionStringBuilder(masterConnectionString container password)
        b.InitialCatalog <- TwinDatabaseName
        b.ConnectionString

    /// The container's current state.
    let state (container: ContainerSection) : Task<Result<ContainerState>> =
        task {
            let! inspected = docker "inspect" [ "inspect"; "-f"; "{{.State.Running}}"; container.Name ]
            match inspected with
            | Ok "true" -> return Result.success Running
            | Ok _ -> return Result.success Stopped
            | Error es ->
                // "No such object" is the absent case; a dead daemon is not.
                let detail = es |> List.tryPick (fun e -> e.Metadata |> Map.tryFind "detail" |> Option.flatten) |> Option.defaultValue ""
                if es |> List.exists (fun e -> e.Code = "twin.container.dockerUnavailable") then
                    return Result.failure es
                elif detail.Contains "No such object" || detail.Contains "no such object" then
                    return Result.success Absent
                else
                    return Result.failure es
        }

    /// Poll until SQL Server accepts a connection (the warm-sql.sh
    /// wait-ready discipline, realized as a SqlConnection probe instead
    /// of a sqlcmd exec — no client tooling required in the container).
    let private waitReady (connStr: string) (timeoutSeconds: int) : Task<Result<unit>> =
        task {
            let deadline = System.Diagnostics.Stopwatch.StartNew()
            let mutable ready = false
            while not ready && deadline.Elapsed.TotalSeconds < float timeoutSeconds do
                try
                    use cnn = new SqlConnection(connStr)
                    do! cnn.OpenAsync()
                    use cmd = cnn.CreateCommand()
                    cmd.CommandText <- "SELECT 1;"
                    let! _ = cmd.ExecuteScalarAsync()
                    ready <- true
                with _ ->
                    do! Task.Delay 1000
            if ready then return Result.success ()
            else return Result.failureOf (notReady timeoutSeconds)
        }

    /// Ensure the container exists and is running, and SQL Server is
    /// accepting connections. Creates (docker run) when absent, starts
    /// when stopped, no-ops when running; always ends with the readiness
    /// probe so a green result means "connectable now".
    let ensureRunning (container: ContainerSection) (password: string) : Task<Result<unit>> =
        task {
            let! current = state container
            match current with
            | Error es -> return Result.failure es
            | Ok s ->
                let! started =
                    task {
                        match s with
                        | Running -> return Result.success ""
                        | Stopped -> return! docker "start" [ "start"; container.Name ]
                        | Absent ->
                            return!
                                docker "run"
                                    [ "run"; "-d"
                                      "--name"; container.Name
                                      "-p"; System.String.Concat(string container.Port, ":1433")  // LINT-ALLOW: terminal docker port-mapping argument; host:container IS the CLI format
                                      "-e"; "ACCEPT_EULA=Y"
                                      "-e"; System.String.Concat("MSSQL_SA_PASSWORD=", password)  // LINT-ALLOW: terminal docker env argument; NAME=value IS the CLI format
                                      "-e"; "MSSQL_PID=Developer"
                                      "-e"; System.String.Concat("MSSQL_MEMORY_LIMIT_MB=", MemoryLimitMb)  // LINT-ALLOW: terminal docker env argument; NAME=value IS the CLI format
                                      "--memory"; ContainerMemory
                                      container.Image ]
                    }
                match started with
                | Error es -> return Result.failure es
                | Ok _ -> return! waitReady (masterConnectionString container password) 90
        }

    /// Stop the container (state preserved; `up` restarts it).
    let stop (container: ContainerSection) : Task<Result<unit>> =
        task {
            let! current = state container
            match current with
            | Error es -> return Result.failure es
            | Ok Absent | Ok Stopped -> return Result.success ()
            | Ok Running ->
                let! r = docker "stop" [ "stop"; container.Name ]
                return r |> Result.map ignore
        }

    /// Remove the container entirely — the twin's data with it. The next
    /// `up` starts from nothing.
    let remove (container: ContainerSection) : Task<Result<unit>> =
        task {
            let! current = state container
            match current with
            | Error es -> return Result.failure es
            | Ok Absent -> return Result.success ()
            | Ok _ ->
                let! r = docker "rm" [ "rm"; "-f"; container.Name ]
                return r |> Result.map ignore
        }
