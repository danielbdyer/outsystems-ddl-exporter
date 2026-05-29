namespace Projection.Adapters.Sql

open System
open System.IO
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
