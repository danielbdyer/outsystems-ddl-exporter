namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql

/// The one I/O home for "open a live connection from an operator spec string"
/// (recon #13). Folds the `env:` / `file:` / `live:` / bare-connection-string
/// decode — previously copied BYTE-FOR-BYTE across `SliceExtractRun.openSource`
/// and `SliceApplyRun.openTarget` (and with drifting `live:` coverage across the
/// other run modules) — into ONE place. `env:` / `file:` resolve the secret
/// through the canonical `ConnectionResolver` (D9: the secret is read there and
/// only there); `live:<connStr>` and a bare connection string open directly. The
/// caller owns disposal of the returned connection.
///
/// `TransferSpec` (the spec PARSER) stays pure; this is its I/O sibling — the
/// opener — so the decode vocabulary has a single definition the run modules
/// share rather than each re-deciding which spec forms it accepts.
[<RequireQualifiedAccess>]
module ConnectionSpec =

    /// Open a connection to `spec` in the given `role`, labelling the substrate
    /// with `label` (surfaced in a resolution/open failure). All four spec forms
    /// are accepted uniformly.
    let openSpec (role: SubstrateRole) (label: string) (spec: string) : Task<Result<SqlConnection>> =
        task {
            if spec.StartsWith "env:" || spec.StartsWith "file:" then
                match TransferSpec.parseConnectionSpec spec with
                | Error es -> return Result.failure es
                | Ok connRef ->
                    let sub : Substrate =
                        { Environment   = Environment.Named label
                          Role          = role
                          ConnectionRef = connRef }
                    return! ConnectionResolver.openSubstrate sub
            else
                let connStr = if spec.StartsWith "live:" then spec.Substring 5 else spec
                try
                    let cnn = new SqlConnection(connStr)
                    do! cnn.OpenAsync()
                    return Result.success cnn
                with ex ->
                    return Result.failureOf (ValidationError.create "connection.openFailed" ex.Message)
        }
