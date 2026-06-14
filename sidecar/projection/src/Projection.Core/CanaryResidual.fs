namespace Projection.Core

/// The canary's **tolerance-residual collector** — the seam that closes the
/// NM-32 / NM-33 provenance FLAG. The round-trip canary compares a
/// source-deploy readback against the deployed target (or the V2 emit against
/// the V1 emit during the dual-track window); along the way it *consumes*
/// named `ToleratedDivergence`s at scattered application sites — the
/// empty-text → NULL normalization (`SqlLiteral.ofRaw`), the char-ANSI-padding
/// / decimal-scale predicate (`ScriptDomBuild.perColumnChangeDetection`), and
/// the index-options / composite-FK structural residual
/// (`PhysicalSchema.toPhysicalForeignKeys` / `toPhysicalIndexes`). Until now no
/// surface collected *which* of those fired on a given run, so
/// `Episode.withProvenance`'s tolerance set was always the `Tolerance.strict`
/// placeholder and the Model Fidelity Report's ACCEPTED DIVERGENCES section was
/// always empty.
///
/// `CanaryResidual` is that collector: an accumulator the canary threads
/// through its comparison, recording each divergence it observes firing. The
/// run's configured `Tolerance` (per-environment accepted set) then resolves
/// the **residual** — the accepted-AND-fired subset — via
/// `Tolerance.matchedResidual`. A clean round-trip records nothing and resolves
/// to `Tolerance.strict`.
///
/// Pure Core: the collector is a `Set` accumulator with no I/O. The adapter /
/// canary layer calls `record` at each application site; the Pipeline feeds the
/// resolved residual into `Episode.withProvenance` and the report into
/// `ModelFidelity.withAcceptedDivergences`.
[<RequireQualifiedAccess>]
module CanaryResidual =

    /// The observed-divergence accumulator. Opaque set of the
    /// `ToleratedDivergence`s the canary witnessed firing this run.
    type Collector = private Collector of Set<ToleratedDivergence>

    /// The empty collector — no divergence observed yet (the clean-round-trip
    /// base case, which resolves to `Tolerance.strict`).
    let empty : Collector = Collector Set.empty

    /// Record one observed divergence. Idempotent (set semantics): the same
    /// divergence firing on many rows / columns is one witnessed divergence.
    let record (d: ToleratedDivergence) (Collector s) : Collector =
        Collector (Set.add d s)

    /// Record a whole set at once (e.g. a structural-diff residual computed in
    /// one shot). Monotone union.
    let recordMany (ds: Set<ToleratedDivergence>) (Collector s) : Collector =
        Collector (Set.union s ds)

    /// The accumulated observed-divergence set.
    let observed (Collector s) : Set<ToleratedDivergence> = s

    /// True iff nothing was observed — the clean round-trip.
    let isClean (Collector s) : bool = Set.isEmpty s

    /// Resolve the run's tolerance **residual** against its configured
    /// tolerance: the accepted-AND-fired subset (`Tolerance.matchedResidual`).
    /// This is the value `Episode.withProvenance` records and
    /// `ModelFidelity.withAcceptedDivergences` surfaces. A divergence that
    /// fired but is NOT in the configured tolerance would have *failed* the
    /// canary (it blocks), so it is — correctly — not part of the residual.
    let resolve (configured: Tolerance) (Collector s) : Tolerance =
        Tolerance.matchedResidual s configured

    /// The residual as the raw `ToleratedDivergence` list the report consumes
    /// (`ModelFidelity.withAcceptedDivergences` / `compose`). Deterministically
    /// ordered by the canonical name so the report is byte-stable (T1).
    let resolvedDivergences (configured: Tolerance) (collector: Collector) : ToleratedDivergence list =
        resolve configured collector
        |> Tolerance.divergences
        |> Set.toList
        |> List.sortBy ToleratedDivergence.name

    // ------------------------------------------------------------------
    // Value-level firing detectors — the observable application sites the
    // canary calls per cell to decide whether a tolerance fired. Each mirrors
    // the exact rule its named `ToleratedDivergence` documents, so the
    // detection cannot drift from the tolerance's witnessed semantics.
    // ------------------------------------------------------------------

    /// `EmptyTextNormalizedToNull` fires iff projecting `(typ, raw)` through
    /// `SqlLiteral.ofRaw` collapses a NON-empty-intent value to the IR's NULL
    /// sentinel — i.e. the source carried an empty raw string (the universal
    /// NULL sentinel, NM-18) for a length-bearing `Text` / `Binary` column, so
    /// the round-trip can no longer distinguish it from a genuine NULL. Returns
    /// the divergence to record, or `None` when the cell round-trips faithfully.
    let detectEmptyTextNormalization (typ: PrimitiveType) (raw: string) : ToleratedDivergence option =
        match typ with
        | Text | Binary when raw = "" -> Some ToleratedDivergence.EmptyTextNormalizedToNull
        | _                           -> None

    /// Record the empty-text→NULL firing for one cell, if it fired. The canary
    /// folds this over the cells it compares; a clean cell leaves the collector
    /// unchanged.
    let observeCell (typ: PrimitiveType) (raw: string) (collector: Collector) : Collector =
        match detectEmptyTextNormalization typ raw with
        | Some d -> record d collector
        | None   -> collector
