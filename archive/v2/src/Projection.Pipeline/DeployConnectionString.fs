namespace Projection.Pipeline
// LINT-ALLOW-FILE-MUTATION: BCL SqlConnectionStringBuilder property set at the connection-string boundary; sealed within the builder

open Microsoft.Data.SqlClient
open Projection.Core

/// Typed connection-string handling — chapter-3.6 cash-out of
/// audit Top-10 #1 (centralize connection-string validation
/// behind `SqlConnectionStringBuilder`). Wraps the BCL builder
/// in a `Result<_>` so malformed strings surface as
/// `ValidationError` at the validation boundary, not as an
/// opaque `SqlException` at connect time.
///
/// Re-exposed under `Deploy.ConnectionString` via a nested module
/// abbreviation so every existing `Deploy.ConnectionString.*`
/// call site (`buildPerDb`, `parse`) keeps working; this is the
/// concept-named home (deploy connection-string building).
[<RequireQualifiedAccess>]
module DeployConnectionString =

    let private invalidConnectionString (message: string) : ValidationError =
        ValidationError.create "deploy.connectionString.invalid" message

    /// Parse a connection string into a validated typed builder.
    /// `SqlConnectionStringBuilder` throws on malformed input;
    /// we catch and lift to `ValidationError` so the boundary
    /// surfaces structured errors.
    let parse (connStr: string) : Result<SqlConnectionStringBuilder> =
        if System.String.IsNullOrWhiteSpace connStr then
            Result.failureOf
                (invalidConnectionString
                    "Connection string is null, empty, or whitespace.")
        else
            try
                Result.success (SqlConnectionStringBuilder(connStr))
            with
            | :? System.ArgumentException as ex ->
                Result.failureOf (invalidConnectionString ex.Message)
            | :? System.FormatException as ex ->
                Result.failureOf (invalidConnectionString ex.Message)

    /// Build a per-database connection string from a master
    /// connection string. Trusts the `master` argument (callers
    /// flow through `parse` first if validation is needed); the
    /// `dbName` is set as `InitialCatalog`. Identifier escaping
    /// (handling `]` inside dbName) is `SqlConnectionStringBuilder`'s
    /// responsibility — its setter handles SQL-quoting per spec.
    let buildPerDb (master: string) (dbName: string) : string =
        let b = SqlConnectionStringBuilder(master)
        b.InitialCatalog <- dbName
        b.ConnectionString
