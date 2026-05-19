namespace Projection.Adapters.Sql

/// Defensive-fallback policies for `Microsoft.Data.SqlClient`
/// realization. Slice A.4.7'-prelude.defensive-hardening (2026-05-19):
/// per the defensive-fallback audit, environment-dependent behavior
/// (CommandTimeout, connection-pool sizing) should NOT be hardcoded
/// per-call site — the policy lives here so the four `cmd.CommandTimeout
/// <- 0` sites (LiveProfiler probe, ReadSide stream open, Deploy batch
/// execute, Deploy parallel-segment dispatch) all share the same
/// env-var-respecting default.
///
/// The discipline:
///   1. **Default to a bounded timeout** (300s = 5 minutes) — long
///      enough for the slowest legitimate operation (a multi-MB
///      MERGE deploying to a busy SQL Server) but short enough that
///      a wedged server (transaction-log full; blocking lock;
///      network half-open) fails CI within a debuggable window
///      rather than hanging indefinitely.
///   2. **Honor `PROJECTION_COMMAND_TIMEOUT_SEC`** as an operator
///      escape hatch — set to `0` for the prior unlimited behavior;
///      set to a smaller number for stricter environments
///      (e.g., 60 for unit-test scaffolding); set to a larger
///      number for legitimately-long bulk operations.
///   3. **Never throw on bad env-var input** — invalid values
///      (non-numeric; negative) fall through to the default with
///      a stderr announcement so operators see the bad input.
[<RequireQualifiedAccess>]
module CommandTimeoutPolicy =

    /// Bounded default command timeout in seconds. Matches the
    /// SqlCommand convention (`int` field; 0 = unlimited; >0 = seconds).
    /// 300s = 5 minutes — calibrated for production-scale MERGE
    /// statements (~1.5s mean per segment in the comprehensive
    /// canary; 100 segments × 1.5s + slack = ~200s; 300s gives
    /// headroom for busier-server cases).
    [<Literal>]
    let private DefaultTimeoutSec : int = 300

    /// Resolve the command timeout. Priority order:
    ///   1. `PROJECTION_COMMAND_TIMEOUT_SEC` env var
    ///   2. `DefaultTimeoutSec` (300)
    ///
    /// Validates env-var input (non-empty + parses to non-negative
    /// int); falls back to the default with a stderr announcement
    /// on bad input.
    let resolve () : int =
        match System.Environment.GetEnvironmentVariable "PROJECTION_COMMAND_TIMEOUT_SEC" with
        | s when not (System.String.IsNullOrWhiteSpace s) ->
            match System.Int32.TryParse s with
            | true, n when n >= 0 -> n
            | _ ->
                eprintfn
                    "PROJECTION_COMMAND_TIMEOUT_SEC='%s' is not a non-negative integer; falling back to %d"
                    s DefaultTimeoutSec
                DefaultTimeoutSec
        | _ -> DefaultTimeoutSec
