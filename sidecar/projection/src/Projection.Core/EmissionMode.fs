namespace Projection.Core

/// **D10 — the emission mode (the wipe-and-load fork, RESOLVED to an explicit
/// named mode; `DECISIONS`/handoff).** How a data realization lands rows into an
/// existing sink:
///   - `Incremental` — the DEFAULT. The MERGE/upsert path: only the actual
///     row-level changes touch the sink, so an idempotent redeploy is
///     CDC-silent (zero captures on unchanged rows). This is the PROD-empty
///     default and the cutover's highest-leverage property.
///   - `WipeAndLoad` — the operator-selected destructive mode: every row is
///     removed (FK-ordered TRUNCATE) and reloaded. Correct when the source is
///     authoritative and a full refresh is wanted, but it costs `2·|rows|` CDC
///     captures (a delete-image for every existing row + an insert-image for
///     every reloaded row), so it is NOT CDC-silent and is gated like other
///     destructive ops.
///
/// A CLOSED DU — a third mode is a compiler event, and the two modes are
/// distinct in the type system (no boolean flag a caller can silently invert).
/// The live TRUNCATE+reload realization (Wave 3) consumes this type; this slice
/// names the mode and its cost. Incremental MERGE stays the default.
type EmissionMode =
    | Incremental
    | WipeAndLoad

[<RequireQualifiedAccess>]
module EmissionMode =

    /// The default: incremental MERGE/upsert (CDC-minimal). Wipe-and-load is
    /// never the default — it is opt-in per the resolved fork (not the
    /// PROD-empty default, not deferred).
    let defaultMode : EmissionMode = Incremental

    /// Whether the mode performs destructive removal before the load (so the
    /// CDC / destructive-op gate must clear it before any write).
    let isDestructive (mode: EmissionMode) : bool =
        match mode with
        | Incremental -> false
        | WipeAndLoad -> true

    /// The CDC capture-cost factor per existing-then-reloaded row. Incremental
    /// captures only changed rows (factor 0 on an idempotent redeploy);
    /// wipe-and-load captures a delete-image + insert-image for every row
    /// (factor 2). Documented per the resolved fork's `2·|table|` cost.
    let cdcCostFactorPerRow (mode: EmissionMode) : int =
        match mode with
        | Incremental -> 0
        | WipeAndLoad -> 2

    /// Stable lower-case token for CLI / manifest surfacing.
    let toToken (mode: EmissionMode) : string =
        match mode with
        | Incremental -> "incremental"
        | WipeAndLoad -> "wipe-and-load"

    /// Parse the operator-supplied token; unknown tokens are rejected (total
    /// over the closed DU, named skip on the miss).
    let ofToken (token: string) : Result<EmissionMode> =
        match token.Trim().ToLowerInvariant() with
        | "incremental" -> Result.success Incremental
        | "wipe-and-load" | "wipeandload" -> Result.success WipeAndLoad
        | other ->
            Result.failureOf
                (ValidationError.create
                    "emissionMode.unknown"
                    (System.String.Concat("Unknown emission mode '", other, "'. Expected 'incremental' or 'wipe-and-load'.")))
