namespace Projection.Core
// LINT-ALLOW-FILE-MUTATION: the reified imperative fix-point loop (recon #19) — local mutable state is the irreducible primitive for bounded iteration; iterate's returned value plane is pure.

/// The bounded fixed-point recursion scheme — iterate a step to convergence or a
/// max-iteration cap, whichever fires first (recon #19). A leaf utility: BCL-only,
/// no Core dependency, so it sits early in the compile order and any pass can
/// reach it.
///
/// Two graph-analytics passes independently hand-rolled the same `let mutable`
/// convergence driver (PageRank in `CentralityPass`, label-propagation in
/// `BoundedContextPass`), and the Newton sqrt in `ProfileAnomalyPass` is the same
/// scheme with no convergence test (a pure max-iteration cap). Unlike the
/// Tarjan/Kahn perf carve-outs, none of these mutate for a perf reason — they are
/// the recursion scheme wearing a mutable skin. This names it once; the mutation
/// is reified behind a typed surface (the `LineageBuffer` discipline).
[<RequireQualifiedAccess>]
module Fixpoint =

    /// Iterate `step` from `seed` until it reports convergence or `maxIters`
    /// iterations have run, whichever fires first. `step` returns the NEXT state
    /// together with whether that state is converged (so a converging step still
    /// applies its result, then stops). Returns the final state + the number of
    /// iterations actually run. A non-positive `maxIters` runs zero iterations and
    /// returns the seed. A step that never reports convergence runs exactly
    /// `maxIters` times — the fixed-iteration cap (Newton's use).
    let iterate (maxIters: int) (step: 's -> 's * bool) (seed: 's) : 's * int =
        let mutable state = seed
        let mutable iterations = 0
        let mutable converged = false
        while not converged && iterations < maxIters do
            let next, isConverged = step state
            state <- next
            iterations <- iterations + 1
            if isConverged then converged <- true
        state, iterations
