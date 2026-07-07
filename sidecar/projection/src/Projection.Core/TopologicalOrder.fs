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

/// R5 / card P1 — a group whose members may execute CONCURRENTLY. The
/// proof-token idiom (`ArtifactByKind`'s move, applied to parallelism):
/// the private constructor is the safety law — a value of this type
/// cannot exist unless the independence proof ran at construction. The
/// ONLY production mint is `TopologicalOrder.levels` (the Kahn-style
/// level assignment: within a level, no member depends on another).
/// The comment-borne "callers MUST deploy levels in order; within a
/// single level segments are independent" contract becomes this type:
/// the MUST dies, the type lives.
///
/// `map` / `choose` are the structure-preserving carriers: per-member
/// projection (kind → that kind's rendered script) and per-member
/// dropping can never merge groups or smuggle a dependent member in —
/// so the token survives the composer's rendering pipeline honestly.
type ParallelSafe<'a> = private ParallelSafe of 'a list

[<RequireQualifiedAccess>]
module ParallelSafe =

    /// The group's members, for consumers that render or count — the
    /// read-only view (a manifest listing, a level walk). Reading never
    /// forges; only construction is guarded.
    let members (ParallelSafe xs) : 'a list = xs

    /// Per-member projection. Structure-preserving: one image per
    /// member, no merging, no reordering across groups.
    let map (f: 'a -> 'b) (ParallelSafe xs) : ParallelSafe<'b> =
        ParallelSafe (List.map f xs)

    /// Per-member projection that may drop members. Dropping a member
    /// never breaks the independence of those that remain.
    let choose (f: 'a -> 'b option) (ParallelSafe xs) : ParallelSafe<'b> =
        ParallelSafe (List.choose f xs)

    let isEmpty (ParallelSafe xs) : bool = List.isEmpty xs

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
    /// The set of kinds participating in ANY dependency cycle — RESOLVED
    /// (the resolver broke weak edges; the SCC stays in `Cycles` for
    /// audit and for exactly this membership) or UNRESOLVED. The
    /// deferral input: a resolved SCC's broken weak edges NEED phase-2
    /// deferral, so `deferredFkColumns` must see resolved members too.
    /// Compute once, then pass to `deferredFkColumns` per kind.
    let cycleMembers (t: TopologicalOrder) : Set<SsKey> =
        t.Cycles
        |> List.collect (fun c -> c.Members)
        |> Set.ofList

    /// The set of kinds participating in an UNRESOLVED cycle only —
    /// `BreakableEdges = []` is the resolved/unresolved discriminant
    /// (a resolved SCC records the edges it broke). The
    /// unsatisfiability input (2026-07-07, the resolver-completeness
    /// program): a non-nullable FK inside a RESOLVED cycle is satisfied
    /// BY THE ORDER the resolver proved (only weak edges were broken;
    /// every strong edge is honored), so `UnbreakableCycleFks` must
    /// judge unresolved members only — the prior all-members
    /// computation flagged every resolved asymmetric 2-cycle's strong
    /// edge as unbreakable and refused a load the order satisfies.
    let unresolvedCycleMembers (t: TopologicalOrder) : Set<SsKey> =
        t.Cycles
        |> List.filter (fun c -> List.isEmpty c.BreakableEdges)
        |> List.collect (fun c -> c.Members)
        |> Set.ofList

    /// The FK columns of `k` that must be deferred across the two-phase
    /// nulls-then-FKs load: `k` is in a cycle, the FK targets a same-cycle
    /// kind, and the source column is nullable (so phase 1 can NULL it and
    /// phase 2 re-points it). A non-nullable cycle FK cannot defer — that
    /// is the consuming layer's diagnostic, not represented here. Shared
    /// by the forward data emitters (`StaticSeedsEmitter` /
    /// `MigrationDependenciesEmitter`) and the Transfer plan.
    let deferredFkColumns (cycleMembers: Set<SsKey>) (k: Kind) : Set<Name> =
        if not (Set.contains k.SsKey cycleMembers) then Set.empty
        else
            k.References
            |> List.choose (fun r ->
                if Set.contains r.TargetKind cycleMembers then
                    Kind.tryFindAttribute r.SourceAttribute k
                    |> Option.bind (fun a ->
                        if a.Column.IsNullable then Some a.Name else None)
                else None)
            |> Set.ofList

    /// Kahn-style level assignment — and the MINT of `ParallelSafe`
    /// (card P1): each returned group's members sit at one dependency
    /// depth, so no member depends on another and the group may deploy
    /// concurrently. Levels themselves stay ordered (level k+1 may
    /// depend on level k); only WITHIN a group is parallelism licensed.
    ///
    /// **The mint refuses to license parallelism it cannot prove (card
    /// P2 finding, 2026-06-12).** The level computation's safety rests
    /// on `t.Order` placing parents before children; only
    /// `Mode = Topological` carries that guarantee. Under
    /// `Alphabetical` (an unresolved cycle anywhere in the catalog) an
    /// FK child can sort BEFORE its parent — the "unknown parent
    /// contributes 0" rule then collapsed a real FK chain into one
    /// "level", minting a group whose members were NOT independent
    /// (one self-FK kind anywhere was enough to flatten everything).
    /// Under `JunctionDeferred` the deferred suffix is likewise
    /// non-topological. For both, the honest degenerate is SINGLETON
    /// groups in `t.Order` order: every group is vacuously
    /// parallel-safe and the leveled deploy equals the sequential one
    /// exactly.
    let levels (t: TopologicalOrder) : ParallelSafe<SsKey> list =
        match t.Mode with
        | Alphabetical
        | JunctionDeferred ->
            t.Order |> List.map (fun k -> ParallelSafe [ k ])
        | Topological ->
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
                // The mint: one dependency depth = one concurrent-safe group.
                ParallelSafe (pairs |> List.map fst |> List.sort))

    /// The undirected FK adjacency derived from `Edges` — each FK edge contributes
    /// a neighbor link in BOTH directions, self-edges dropped, neighbor lists
    /// deduplicated and SsKey-sorted (deterministic).
    ///
    /// The SINGLE canonical form for the structural-coupling graph views —
    /// `BoundedContextPass` (label-propagation community detection) and
    /// `TopologicalOrderPass` island detection. They previously each inlined their
    /// own `addNeighbor` fold and silently diverged: one deduped + sorted +
    /// self-skipped, the other did none of these. The divergence is benign for the
    /// island BFS (dups / order / self-loops don't change weakly-connected
    /// components) but load-bearing for community weighting, so the deduped form is
    /// correct for both — and now there is one of it. (The directed PageRank
    /// adjacency in `CentralityPass` stays on its mutable-`Dictionary` perf
    /// carve-out; the Cascade-filtered adjacency is edge-classified, not pure
    /// topology — neither is this undirected view.)
    let undirectedAdjacency (t: TopologicalOrder) : Map<SsKey, SsKey list> =
        let addNeighbor (m: Map<SsKey, SsKey list>) (a: SsKey) (b: SsKey) =
            let existing = Map.tryFind a m |> Option.defaultValue []
            if List.contains b existing then m else Map.add a (b :: existing) m
        t.Edges
        |> List.fold
            (fun acc (src, tgt) ->
                if src = tgt then acc
                else addNeighbor (addNeighbor acc src tgt) tgt src)
            Map.empty
        |> Map.map (fun _ neighbors -> List.sort neighbors)


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
