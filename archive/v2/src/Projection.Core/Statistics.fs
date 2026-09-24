namespace Projection.Core

/// Pure statistical aggregates shared across the live-profiling derivations
/// (recon #5 — the thrice-written percentile). A leaf utility: BCL-only, no Core
/// dependency, so it sits early in the compile order and any derivation can reach
/// it.
[<RequireQualifiedAccess>]
module Statistics =

    /// Continuous linear-interpolation percentile — SQL Server `PERCENTILE_CONT`
    /// semantics — over a PRE-SORTED ascending array. `p` ∈ [0, 1]. For length `N`:
    /// `h = (N - 1) · p`; `lo = floor h`; the result interpolates `sorted.[lo]` and
    /// `sorted.[lo + 1]` by the fraction `h - lo`. An empty array yields `0M`; a
    /// singleton yields its sole value.
    ///
    /// This is the one definition of the percentile that `LiveProfiler` previously
    /// inlined twice (the numeric-distribution derivation and the FK-cardinality
    /// derivation — the second's docstring admitted it was "extracted here", i.e.
    /// copied). `Bench`'s percentile stays its own `int64` flavor (different element
    /// type and a 0–100 `p` convention; folding it in would change its rounding).
    let percentileCont (sorted: decimal[]) (p: decimal) : decimal =
        if sorted.Length = 0 then 0M
        elif sorted.Length = 1 then sorted.[0]
        else
            let n = decimal (sorted.Length - 1)
            let h = n * p
            let lo = int h
            let frac = h - decimal lo
            if lo >= sorted.Length - 1 then sorted.[sorted.Length - 1]
            else sorted.[lo] + frac * (sorted.[lo + 1] - sorted.[lo])
