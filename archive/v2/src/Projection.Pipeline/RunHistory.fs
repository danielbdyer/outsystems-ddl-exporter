namespace Projection.Pipeline

/// The temporal base — the operator's **durable run timeline**. A `RunHistory`
/// is the chronological sequence of persisted `Run`s, from which trends, the
/// cutover trajectory, and run-to-run deltas fall out as `fold` / `map` —
/// never bespoke. The discriminating predicate: `trend`, `canaryHistory`, and
/// `readiness` are all projections of the SAME run sequence — the history *is*
/// the integral.
///
/// This is the durable realization the morphology named as missing ("no
/// durable episode to integrate over — the FTC runs only in-memory"); it is
/// distinct from `Core.Episode` (a single state-at-coordinate) and subsumes
/// `RunLedger` (the ledger was a thin index of this). It composes `Run` +
/// (at the verb layer) `Comparison` + `Ref`, so it earns its place now that
/// they exist. It SUPPORTS evolution / promotion / trends without completing
/// any of them.
module RunHistory =

    /// Chronological — oldest run first.
    type RunHistory = { Runs : Run.Run list }

    let ofRuns (runs: Run.Run list) : RunHistory =
        { Runs = runs |> List.sortBy (fun r -> r.Ts) }

    /// Load the whole history from the run store.
    let load (dir: string) : RunHistory = ofRuns (Run.list dir)

    let length (h: RunHistory) : int = List.length h.Runs
    let latest (h: RunHistory) : Run.Run option = List.tryLast h.Runs

    /// The run at a timeline index (a point in time), if it exists.
    let at (index: int) (h: RunHistory) : Run.Run option =
        if index >= 0 && index < List.length h.Runs then Some (List.item index h.Runs) else None

    /// A metric series over the history — the basis for trend sparklines
    /// (`RunHistory.trend (fun r -> r.Declined)` → a declined-over-time line).
    let trend (metric: Run.Run -> int) (h: RunHistory) : int list =
        h.Runs |> List.map metric

    /// The canary verdict history (green / red), oldest first — the dots.
    let canaryHistory (h: RunHistory) : string list =
        h.Runs |> List.choose (fun r -> r.Canary)

    /// R6 readiness over the whole history (reuses the gauge; the history is
    /// the source the ledger was a thin view of).
    let readiness (h: RunHistory) : RunLedger.Readiness =
        h.Runs |> List.map Run.toLedgerEntry |> RunLedger.readiness
