namespace Projection.Core

/// The observable outcome of running the topological-order pass.
///
/// `Topological` — the catalog is acyclic (or cycles were resolved by the
/// asymmetric-2-cycle resolver) and the order respects every FK
/// dependency.
///
/// `Alphabetical` — cycles surfaced and the resolver could not break them
/// without losing edges; the pass falls back to alphabetical-by-SsKey
/// ordering. Downstream emitters can either reject this mode (data
/// emission) or accept it (diagnostics). Schema emitters ignore the
/// ordering entirely per A33.
///
/// `JunctionDeferred` — the resolver opted to push junction (bridge)
/// kinds to the end of the order to satisfy the data-emission
/// `DeferJunctions` configuration. Topologically valid for the non-junction
/// kinds; junctions are appended in alphabetical-by-SsKey order.
type OrderingMode =
    | Topological
    | Alphabetical
    | JunctionDeferred


// `EdgeStrength` lives in `CycleResolution.fs` (V2 audit, 2026-05-08):
// edge-strength classification is a V1-flavored domain rule about which
// FK edges are safe to break, not pure graph algebra. Keeping it
// alongside the classifier and resolver makes the algebra/domain split
// visible at the file level.


/// How `TopologicalOrderPass` treats a kind's reference to itself
/// during dependency-graph construction. Per session-36 audit
/// (Agent 4 #6 — "RawTextEmitter re-implements topological sort"):
/// the emitter and the pass diverged on this axis (the pass treated
/// self-edges as 1-node SCCs; the emitter skipped them since SQL
/// Server allows inline self-FK constraints in CREATE TABLE).
/// Parameterizing the policy harmonizes the two — the pass now
/// produces both views from a single algorithm.
type SelfLoopPolicy =
    /// Self-edges are dependency edges. The kind appears unprocessed
    /// after Kahn's algorithm (its indegree is ≥ 1 from itself);
    /// downstream resolution either breaks the loop or falls back
    /// to alphabetical. Default — preserves the pre-session-36 pass
    /// semantics for existing callers.
    | TreatAsCycle
    /// Self-edges are dropped during graph construction. Used by
    /// emitters whose target syntax (e.g., SQL Server's CREATE
    /// TABLE with inline FK clauses) supports a kind referencing
    /// itself without an out-of-line dependency, so the self-loop
    /// is vacuous for ordering.
    | SkipSelfEdges


/// How `TopologicalOrderPass` handles junction (bridge) kinds in the
/// output ordering. H-040 — JunctionDeferred mode.
///
/// `EmitInTopologicalOrder` (default) places junction kinds at their
/// FK-safe topological position alongside non-junction kinds. This
/// preserves pre-H-040 semantics for all callers.
///
/// `DeferJunctionKinds` pushes junction kinds — those with ≥2 FK
/// references and ≤2 non-PK non-system attributes — to the end of
/// the output order, producing `Mode = JunctionDeferred`. The
/// non-junction prefix is topologically sorted; the deferred suffix
/// is sorted alphabetically by SsKey for determinism.
type JunctionDeferralPolicy =
    | EmitInTopologicalOrder
    | DeferJunctionKinds


/// Combined ordering configuration for the topological-order pass.
/// Bundles the two orthogonal ordering axes — self-loop handling and
/// junction deferral — so callers that need to configure one axis
/// don't have to change the call site for the other
/// (harmonization-via-parameterization per A40). The default config
/// reproduces pre-H-040 behaviour.
type OrderingConfig = {
    SelfLoops        : SelfLoopPolicy
    JunctionDeferral : JunctionDeferralPolicy
}

[<RequireQualifiedAccess>]
module OrderingConfig =

    /// Default ordering configuration: treat self-edges as cycles and
    /// emit junction kinds at their topological position.
    let defaultConfig : OrderingConfig =
        { SelfLoops        = TreatAsCycle
          JunctionDeferral = EmitInTopologicalOrder }


/// Diagnostic for a strongly-connected component the resolver could not
/// break. Members and breakable-edges are keyed by `SsKey` (strongly
/// typed; no name lookup required). The `Reason` field is human-readable
/// — emit it to operator diagnostics, never parse it.
type CycleDiagnostic = {
    Members        : SsKey list
    BreakableEdges : (SsKey * SsKey) list
    Reason         : string
}


/// The output of the topological-order pass — an emitter-consumable
/// value per A32. The catalog itself is **not** restructured; this value
/// carries the ordering metadata for downstream Π's that need it (data
/// emission, diagnostics emission). Schema emission ignores it per A33.
///
/// `Order` is the kinds in FK-safe order (or alphabetical fallback) keyed
/// by `SsKey`. Emitters resolve `SsKey -> Kind` against the catalog.
///
/// `Edges` are the FK edges discovered during graph construction; an
/// edge `(a, b)` reads "kind `a`'s reference points at kind `b`".
///
/// `MissingEdges` records FKs to kinds absent from the catalog. The
/// pass tolerates these (sort proceeds; missing-target kinds are
/// dropped from the dependency graph) but the count is part of the
/// public contract — emitters that require strict referential integrity
/// can reject a non-empty `MissingEdges`.
///
/// `Cycles` records strongly-connected components the resolver did not
/// break. Empty for acyclic catalogs and for catalogs whose cycles all
/// resolved.
///
/// `Diagnostics` carries human-readable narration of the run; emitter
/// consumers may surface it through the (forthcoming) `Diagnostics`
/// writer's operator channel. Returned as a value, never as a side
/// effect (per the V1 admire's "diagnostics as side-effect channel"
/// risk).
type TopologicalOrder = {
    Mode         : OrderingMode
    Order        : SsKey list
    Edges        : (SsKey * SsKey) list
    MissingEdges : (SsKey * SsKey) list
    Cycles       : CycleDiagnostic list
    Diagnostics  : string list
}

[<RequireQualifiedAccess>]
module TopologicalOrder =

    /// The empty topological order — `Mode = Topological` (vacuously),
    /// no kinds, no edges, no diagnostics. The neutral value for
    /// catalogs with zero kinds, and a useful test fixture.
    let empty : TopologicalOrder =
        { Mode         = Topological
          Order        = []
          Edges        = []
          MissingEdges = []
          Cycles       = []
          Diagnostics  = [] }

    /// True iff the run produced a fully topological ordering — i.e.,
    /// every FK is preserved by the order.
    let isAcyclic (t: TopologicalOrder) : bool =
        match t.Mode with
        | Topological -> true
        | _           -> false

    /// True iff the kind appears anywhere in the order.
    let containsKind (key: SsKey) (t: TopologicalOrder) : bool =
        List.contains key t.Order

    /// 0-based position of the kind in the order, or `None` if absent.
    /// Useful for emitters asserting "parent precedes child."
    let positionOf (key: SsKey) (t: TopologicalOrder) : int option =
        t.Order |> List.tryFindIndex (fun k -> k = key)

    /// True iff `parent` precedes `child` in the order. `false` if
    /// either is absent or if their indices are equal (equality
    /// shouldn't occur — catalog SsKeys are unique — but the predicate
    /// is defensive).
    let precedes (parent: SsKey) (child: SsKey) (t: TopologicalOrder) : bool =
        match positionOf parent t, positionOf child t with
        | Some p, Some c when p < c -> true
        | _                          -> false

    /// True iff the run encountered no missing FK targets. Useful for
    /// emitters with strict referential-integrity requirements.
    let isComplete (t: TopologicalOrder) : bool =
        List.isEmpty t.MissingEdges

    /// Kahn-style topological **levels** — outer list ordered by
    /// dependency depth; inner list contains kinds at that level
    /// sorted by `SsKey` for deterministic emission. Level 0 holds
    /// kinds with no FK dependencies; level N holds kinds whose
    /// deepest dependency sits at level N-1.
    ///
    /// **Parallel-safety invariant:** kinds at the same level have
    /// NO directed FK edge between them (in either direction). The
    /// realization layer (`Deploy.executeBatchParallel`) consumes
    /// per-level groups and dispatches within-level segments in
    /// parallel without violating FK constraints. Slice
    /// A.4.7'-prelude.perf-sweep-6 (composer-levels) cash-out.
    ///
    /// **Cycle handling:** members of `t.Cycles` participate at the
    /// level computed by the post-resolution `t.Order` traversal —
    /// edges broken by the cycle resolver are honored as "absent"
    /// for level computation (their FK dependency is restored by
    /// Phase-2 UPDATE in the data-emission triumvirate). The
    /// cycle-broken edge does NOT prevent the cycle-participating
    /// kind from receiving a finite level.
    ///
    /// **Algorithm:** single fold over `t.Order` (which is already
    /// topologically valid post-cycle-resolution). For each kind k,
    /// look up its FK parents in `t.Edges` (each `(child, parent)`
    /// pair contributes one parent entry); take `1 + max(known
    /// parent levels)`. Parents not yet seen — broken edges where
    /// the cycle-resolved arrival of the cycle-participating kind
    /// precedes its broken parent in `t.Order` — contribute 0.
    let levels (t: TopologicalOrder) : SsKey list list =
        let parentsOf =
            t.Edges
            |> List.groupBy fst
            |> List.map (fun (child, edges) -> child, edges |> List.map snd)
            |> Map.ofList
        let computeLevel (levelMap: Map<SsKey, int>) (k: SsKey) : int =
            match Map.tryFind k parentsOf with
            | None -> 0
            | Some parents ->
                let knownLevels =
                    parents |> List.choose (fun p -> Map.tryFind p levelMap)
                if List.isEmpty knownLevels then 0
                else (List.max knownLevels) + 1
        let finalMap =
            t.Order
            |> List.fold
                (fun acc k -> Map.add k (computeLevel acc k) acc)
                Map.empty
        finalMap
        |> Map.toList
        |> List.groupBy snd
        |> List.sortBy fst
        |> List.map (fun (_, pairs) ->
            pairs |> List.map fst |> List.sort)


/// H-037 — result of schema island detection. Each inner list is one
/// weakly-connected component of the undirected FK graph with ≥2
/// members, sorted by SsKey. Components with a single member are not
/// reported (single-kind islands are unremarkable).
type IslandReport = {
    Islands : SsKey list list
}


/// H-039 — one cascade shock zone: the root kind and the set of
/// kinds reachable from it by following Cascade-tagged FK edges
/// depth-first. Zones with |Reachable| ≥ 3 are reported.
type CascadeShockZone = {
    Root      : SsKey
    /// Sorted by SsKey; excludes Root.
    Reachable : SsKey list
}
