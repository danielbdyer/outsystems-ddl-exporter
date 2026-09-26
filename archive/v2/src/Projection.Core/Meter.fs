namespace Projection.Core

/// Pass-grain decoration as a value (`CONSTELLATION.md` §9.8.5 — the
/// decoration collapse). The pipeline's deepest near-identity was prose:
/// "the fold inside `PassChainAdapter.compose` IS `Pass.composeAll` modulo
/// per-step `Bench.scope` decoration." A "modulo" in a law is an unfactored
/// value — this module factors it.
///
/// `Meter.pass` is an endomorphism on Kleisli arrows: **identity on the
/// value plane** (the decorated arrow returns exactly what the bare arrow
/// returns — payload, lineage trail, and diagnostics included; the probe
/// law, §5.2 identity 3), **effect on the meter plane only** (one Bench
/// sample under `label` per invocation).
///
/// Two grains, one decoration: the pass chain brackets each registered
/// transform (`PassChainAdapter.compose`, the first consumer); the run
/// spine's stage bracket (R2, the `staged { }` CE) is the same decoration
/// at stage grain — `Meter.pass` is the pass-grain face of the spine's
/// `Bind` (RI-6: the extraction lands with the spine, card S1).
[<RequireQualifiedAccess>]
module Meter =

    /// Decorate a Kleisli arrow with a Bench scope. The scope opens when
    /// the arrow is invoked and closes once the arrow's value is computed —
    /// `Lineage<Diagnostics<_>>` is eager, so the sample brackets the
    /// pass's whole computation, exactly as the inline `use` form did.
    let pass (label: string) (p: Pass<'a, 'b>) : Pass<'a, 'b> =
        fun a ->
            use _ = Bench.scope label
            p a
