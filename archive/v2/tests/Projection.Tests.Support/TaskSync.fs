namespace Projection.Tests

open System.Threading.Tasks

/// Drive a `Task`-returning operation to completion from a synchronous
/// test body WITHOUT deadlocking under xUnit's bounded
/// `MaxConcurrencySyncContext`.
///
/// The work thunk is invoked inside `Task.Run`, i.e. on a thread-pool
/// thread whose `SynchronizationContext.Current` is null. The operation's
/// `await` continuations therefore resume on the thread pool rather than
/// being queued back to xUnit's capped sync context — so the blocking
/// wait below cannot starve them. This is the categorical fix for the
/// sync-over-async deadlock (`(task).GetAwaiter().GetResult()` on the
/// xUnit sync-context thread) that made the parallel pure pool hang
/// intermittently. See `DECISIONS 2026-05-24 (Test runner: tiered pools,
/// sync-over-async deadlock, OOM)`.
[<RequireQualifiedAccess>]
module TaskSync =

    /// Run a `Task<'a>`-returning operation and return its result.
    let run (work: unit -> Task<'a>) : 'a =
        Task.Run<'a>(System.Func<Task<'a>>(work)).GetAwaiter().GetResult()

    /// Run a bare `Task`-returning operation to completion.
    let runUnit (work: unit -> Task) : unit =
        Task.Run(System.Func<Task>(work)).GetAwaiter().GetResult()
