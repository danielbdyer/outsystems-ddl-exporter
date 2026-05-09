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
