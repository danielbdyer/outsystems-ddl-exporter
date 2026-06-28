namespace Projection.Pipeline

open System
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql

/// The one connection-acquisition discipline (recon #13). TWO things live here,
/// and ONLY here:
///
///   * `parseConnectionSpec` — the `env:` / `file:` out-of-band decode
///     (`spec -> ConnectionRef`), the single definition `TransferSpec`
///     re-exports and every operator surface shares.
///   * `openSpec` — the one I/O home for "open a live connection from an
///     operator spec string". Folds the `env:` / `file:` / `live:` / bare
///     decode (previously copied byte-for-byte across the slice run modules,
///     and with drifting `live:` coverage across `LiveModelRead` / `Hydration`
///     / `ProfileCaptureRun` / `SyntheticLoadRun`) into ONE place.
///
/// Compiled FIRST in the Pipeline project (depends only on `Projection.Core`
/// and `Projection.Adapters.Sql`, both below it) so every consumer — including
/// the early `LiveModelRead` / `Hydration` — can reach the one opener
/// regardless of its own compile position. The caller owns disposal of the
/// returned connection.
///
/// D9 NOTE (amended 2026-06-28, operator decision — see `DECISIONS.md`): the
/// opener accepts ALL FOUR spec forms uniformly, the OSSYS model source
/// included. `env:` / `file:` (out-of-band references) remain the documented,
/// recommended form; `live:<connStr>` and a bare connection string are an
/// opt-in escape hatch, consistent with `transfer` / `slice`. The prior
/// model-source-only `env:`/`file:`-only refusal (`model.ossys.connRef`) is
/// retired so there is ONE opener, not two policies.
[<RequireQualifiedAccess>]
module ConnectionSpec =

    let private specInvalid (code: string) (message: string) : ValidationError =
        ValidationError.create code message

    /// Parse a `--source-conn` / `--sink-conn` / model spec ("env:NAME" or
    /// "file:PATH") into a `ConnectionRef` — the out-of-band credential pointer
    /// (D9), never the secret. The single home for the decode; `TransferSpec`
    /// re-exports it (so the `transfer.connection.*` error vocabulary every
    /// caller and test depends on is preserved by construction).
    let parseConnectionSpec (spec: string) : Result<ConnectionRef> =
        if String.IsNullOrWhiteSpace spec then
            Result.failureOf (specInvalid "transfer.connection.specEmpty" "connection spec is empty.")
        else
            let trimmed = spec.Trim()
            match trimmed.IndexOf ':' with
            | -1 ->
                Result.failureOf
                    (specInvalid "transfer.connection.specShape"
                        (sprintf "connection spec '%s' missing 'env:' or 'file:' prefix." trimmed))
            | i ->
                let prefix = trimmed.Substring(0, i).ToLowerInvariant()
                let value  = trimmed.Substring(i + 1).Trim()
                if String.IsNullOrWhiteSpace value then
                    Result.failureOf
                        (specInvalid "transfer.connection.specEmptyValue"
                            (sprintf "connection spec '%s' has an empty value after '%s:'." trimmed prefix))
                else
                    match prefix with
                    | "env"  -> Result.success (ConnectionRef.EnvVar value)
                    | "file" -> Result.success (ConnectionRef.File value)
                    | other  ->
                        Result.failureOf
                            (specInvalid "transfer.connection.specPrefix"
                                (sprintf "connection spec '%s' unknown prefix '%s' (expected 'env' or 'file')." trimmed other))

    /// Open a connection to `spec` in the given `role`, labelling the substrate
    /// with `label` (surfaced in a resolution/open failure). All four spec forms
    /// are accepted uniformly: `env:` / `file:` resolve the secret through the
    /// canonical `ConnectionResolver` (D9: the secret is read there and only
    /// there); `live:<connStr>` and a bare connection string open directly.
    let openSpec (role: SubstrateRole) (label: string) (spec: string) : Task<Result<SqlConnection>> =
        task {
            if spec.StartsWith "env:" || spec.StartsWith "file:" then
                match parseConnectionSpec spec with
                | Error es -> return Result.failure es
                | Ok connRef ->
                    return! ConnectionResolver.openSubstrate (Substrate.fromRef role label connRef)
            else
                let connStr = if spec.StartsWith "live:" then spec.Substring 5 else spec
                try
                    let cnn = new SqlConnection(connStr)
                    do! cnn.OpenAsync()
                    return Result.success cnn
                with ex ->
                    return Result.failureOf (ValidationError.create "connection.openFailed" ex.Message)
        }
