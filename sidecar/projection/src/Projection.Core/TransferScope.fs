namespace Projection.Core

open Projection.Core.Passes

/// The EFFECTIVE TRANSFER GRAPH (2026-07-07, the go-board scoping
/// program; readiness log Entry 25) — the reified answer to "which kinds
/// does this transfer actually touch," built once from the three inputs
/// every gate already holds and consumed by the engine's pre-write gates
/// AND the go board, so the forecast and the live run cannot evaluate
/// different graphs.
///
/// The rules it carries:
///   - `WriteKinds` — the kinds the load WRITES: the declared subset (or
///     the whole estate when no subset), minus the reconciled kinds
///     (reconciled parents are matched against rows the sink already
///     holds; `reclassifyReconciled` zeroes their writes).
///   - `Nodes` — `WriteKinds` plus the reconciled kinds as ISOLATED
///     graph nodes: they stay in the plan/report (their
///     `ReconciledByRule` lines matter) but no FK edge can pass through
///     them, so a cycle through a reconciled kind correctly breaks.
///   - Ordering/cycle analysis (`topology`) runs on the induced
///     subgraph: FK edges bind only between `WriteKinds` members. An FK
///     leaving the write set is either reconciled (re-keyed; no ordering
///     dependency) or refused by the subset-escape gate — never this
///     graph's problem.
///
/// A full, non-reconciling transfer produces the identity scope (all
/// kinds, all edges) — byte-identical behavior to the unscoped pass.
type TransferScope =
    {
        /// Kinds the load writes (INSERT/UPDATE; DELETE under a wipe).
        WriteKinds : Set<SsKey>
        /// Write kinds ∪ reconciled kinds — everything the plan carries.
        Nodes      : Set<SsKey>
        /// Kinds reconciled against pre-existing sink rows (never written).
        Reconciled : Set<SsKey>
    }

[<RequireQualifiedAccess>]
module TransferScope =

    /// Build the scope from the inputs every transfer gate already holds:
    /// the catalog, the declared load set (`None` = whole estate), and
    /// the reconciled kind set. Unknown keys (a declared or reconciled
    /// key absent from the catalog) are dropped — the resolvers upstream
    /// own naming those.
    let create (catalog: Catalog) (loadSet: Set<SsKey> option) (reconciled: Set<SsKey>) : TransferScope =
        let allKeys =
            Catalog.allKinds catalog |> List.map (fun k -> k.SsKey) |> Set.ofList
        let declared =
            match loadSet with
            | Some s -> Set.intersect s allKeys
            | None -> allKeys
        let reconciledPresent = Set.intersect reconciled allKeys
        let nodes = Set.union declared reconciledPresent
        {
            WriteKinds = Set.difference nodes reconciledPresent
            Nodes      = nodes
            Reconciled = reconciledPresent
        }

    /// The load-ordering topology over the effective transfer graph —
    /// `TopologicalOrderPass.runScopedWith` with this scope's node and
    /// edge sets. The one derivation both the engine's Execute gates and
    /// the go board's load-order item consume.
    let topology (selfLoops: SelfLoopPolicy) (scope: TransferScope) (catalog: Catalog) : TopologicalOrder =
        (TopologicalOrderPass.runScopedWith
            selfLoops
            { Nodes = scope.Nodes; EdgeKinds = scope.WriteKinds }
            catalog).Value
