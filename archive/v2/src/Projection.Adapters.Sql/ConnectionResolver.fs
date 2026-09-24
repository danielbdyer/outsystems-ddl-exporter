namespace Projection.Adapters.Sql

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// Resolve a `ConnectionRef` (the operator's OOB pointer at where the
/// secret lives) to the concrete connection-string the SqlClient opens.
/// D9: V2 reads the secret here and *only* here. Failures (missing env
/// var, missing file, empty content) surface as `ValidationError` so the
/// CLI maps them to its `connection-config error` exit code.
[<RequireQualifiedAccess>]
module ConnectionResolver =

    let private missing (label: string) (where: string) : ValidationError =
        ValidationError.create
            "transfer.connection.refMissing"
            (sprintf "%s connection: %s not found." label where)

    let private blank (label: string) (where: string) : ValidationError =
        ValidationError.create
            "transfer.connection.refEmpty"
            (sprintf "%s connection: %s resolved to empty content." label where)

    let resolve (label: string) (connRef: ConnectionRef) : Result<string> =
        match connRef with
        | ConnectionRef.EnvVar name ->
            match Environment.GetEnvironmentVariable name with
            | null -> Result.failureOf (missing label (sprintf "env var '%s'" name))
            | value ->
                if String.IsNullOrWhiteSpace value then Result.failureOf (blank label (sprintf "env var '%s'" name))
                else Result.success value
        | ConnectionRef.File path ->
            if not (File.Exists path) then Result.failureOf (missing label (sprintf "file '%s'" path))
            else
                let value = (File.ReadAllText path).Trim()
                if String.IsNullOrWhiteSpace value then Result.failureOf (blank label (sprintf "file '%s'" path))
                else Result.success value
        | ConnectionRef.Raw connStr ->
            // The already-in-memory secret (D9 amendment 2026-07-06): the
            // caller resolved it upstream; never logged, never persisted.
            if String.IsNullOrWhiteSpace connStr then Result.failureOf (blank label "raw connection string")
            else Result.success connStr

    /// A diagnostic label for a substrate — `Role:ENVIRONMENT` — so a
    /// resolution / open failure names which substrate failed.
    let private substrateLabel (substrate: Substrate) : string =
        sprintf "%A:%s" substrate.Role (Environment.name substrate.Environment)

    /// Slice 4.2 — resolve a `Substrate`'s out-of-band `ConnectionRef` and
    /// open the live `SqlConnection`. The single seam through which the
    /// `TransferConnections` apparatus realizes a logical substrate into an
    /// open connection (D9: the secret is read here and only here). The
    /// caller owns disposal of the returned connection.
    let openSubstrate (substrate: Substrate) : Task<Result<SqlConnection>> =
        task {
            let label = substrateLabel substrate
            match resolve label substrate.ConnectionRef with
            | Error es -> return Result.failure es
            | Ok connectionString ->
                try
                    let cnn = new SqlConnection(connectionString)
                    do! cnn.OpenAsync()
                    return Result.success cnn
                with ex ->
                    return
                        Result.failureOf
                            (ValidationError.create
                                "transfer.connection.openFailed"
                                (sprintf "%s connection: failed to open — %s" label ex.Message))
        }
