namespace Projection.Adapters.OssysSql

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Polly
open Polly.Retry

/// Polly resilience pipeline for transient SQL errors at the OSSYS-source
/// extraction boundary.
///
/// V1's `Osm.Pipeline.SqlExtraction.MetadataSnapshotRunner` has **no retry
/// policy** — transient handling is left to caller orchestration. V2 owns
/// the policy structurally inside the adapter so V2's canary in dual-track
/// mode (R6 split-brain governance) can tolerate transient SqlExceptions
/// on cloud OSSYS (Azure SQL / managed instance) without false-positive
/// divergence reports. This is the matrix row 34 cash-out: cutover-critical
/// per `V2_DRIVER.md` per-axis correctness stakes.
///
/// **Scope: command-execute only.** The pipeline wraps
/// `command.ExecuteReaderAsync` — the dominant transient surface (connection
/// open + first read of the streaming response). Mid-stream transients on
/// `ReadAsync` / `NextResultAsync` after the reader is open cannot be
/// retried without re-running the full query; that surface is left
/// un-retried for this slice (the predicate plus the at-execute retry
/// catches the common case).
[<RequireQualifiedAccess>]
module Retry =

    /// `SqlException.Number` values that classify as transient and warrant
    /// retry. Sources:
    ///   - **-2** — command timeout.
    ///   - **-1** — connection severed / network drop.
    ///   - **4060** — cannot open database (often transient at warmup or
    ///     during failover).
    ///   - **18452** — login from untrusted domain (auth transient during
    ///     failover).
    ///   - **40197** — Azure SQL service-level error (RECONFIGURE or
    ///     similar transient ops).
    ///   - **40501** — Azure SQL service busy (throttle).
    ///   - **40613** — Azure SQL database currently unavailable.
    let transientSqlNumbers : Set<int> =
        Set.ofList [ -2; -1; 4060; 18452; 40197; 40501; 40613 ]

    /// Pure predicate: does the exception classify as a transient SQL
    /// error per the documented number set? Returns `false` for
    /// non-`SqlException` inputs and for `SqlException` with `Number`
    /// outside the set.
    let isTransientSqlError (ex: exn) : bool =
        match ex with
        | :? SqlException as sql -> transientSqlNumbers.Contains sql.Number
        | _ -> false

    /// Default attempt count for production retries (3 retries + 1 initial
    /// attempt = 4 total tries).
    [<Literal>]
    let DefaultMaxRetryAttempts = 3

    /// Default base delay for production exponential backoff (1s → 2s → 4s
    /// with jitter ±25%).
    let defaultBaseDelay : TimeSpan = TimeSpan.FromSeconds 1.0

    /// Build a Polly resilience pipeline parameterized on the `shouldRetry`
    /// predicate + delay + max-attempts. Tests substitute a custom
    /// predicate + minimal delay; production uses
    /// `isTransientSqlError` + `defaultBaseDelay` via `defaultPipeline`.
    ///
    /// The pipeline is exponential-backoff + jitter — the Polly v8
    /// canonical shape for transient SQL retries. Cancellation propagates
    /// through Polly to the wrapped operation.
    let buildPipeline
            (shouldRetry: exn -> bool)
            (baseDelay: TimeSpan)
            (maxAttempts: int)
            : ResiliencePipeline =
        let opts = RetryStrategyOptions()
        opts.ShouldHandle <-
            PredicateBuilder()
                .Handle<exn>(System.Func<exn, bool> shouldRetry)
        opts.MaxRetryAttempts <- maxAttempts
        opts.BackoffType <- DelayBackoffType.Exponential
        opts.UseJitter <- true
        opts.Delay <- baseDelay
        ResiliencePipelineBuilder()
            .AddRetry(opts)
            .Build()

    /// Default production retry pipeline: transient SQL classification,
    /// 3 retries, 1s base delay with exponential backoff + jitter.
    let defaultPipeline : ResiliencePipeline =
        buildPipeline isTransientSqlError defaultBaseDelay DefaultMaxRetryAttempts

    /// Execute `operation` under `pipeline`. Re-thrown exceptions after
    /// retry exhaustion bubble unchanged so the caller's
    /// `MetadataExtractionError.classify` can lift them to typed errors.
    let runOnPipeline<'T>
            (pipeline: ResiliencePipeline)
            (operation: CancellationToken -> Task<'T>)
            : Task<'T> =
        task {
            let! result =
                pipeline
                    .ExecuteAsync(
                        System.Func<CancellationToken, ValueTask<'T>>(fun ct ->
                            ValueTask<'T>(operation ct)))
            return result
        }
