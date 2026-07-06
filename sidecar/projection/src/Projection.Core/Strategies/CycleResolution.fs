namespace Projection.Core

// LINT-ALLOW-FILE: cycle-resolver diagnostic prose. The strategy
// emits human-readable status strings via `sprintf` for operator-
// facing lineage entries (e.g., "SCC of size %d; current resolver
// handles 2-cycles only"). Same allowed-exception class as
// `TopologicalOrderPass.fs` and `Bench.fs` per `DECISIONS
// 2026-05-09 — Built-in obligation`.

/// Domain rules for cycle resolution in topological-sort passes.
///
/// The pure graph algebra (Kahn's algorithm + Tarjan's SCC) lives in
/// `TopologicalOrderPass`. The rules in *this* module are V2's
/// codification of V1's RDBMS-flavored semantic choices about which
/// FK edges are "soft" enough to break, and how to choose among
/// candidates when more than one is breakable. They live here, named
/// explicitly as domain logic, rather than threaded into the algebra
/// they parameterize.
///
/// `EdgeStrength` and `classifyEdge` together form the **classifier** —
/// a function from `(Kind, Reference)` to a strength label. The
/// classifier reads two IR fields (source attribute's `IsNullable`,
/// reference's `OnDelete`) and applies the V1 rule:
///   - `Cascade`  : `OnDelete = Cascade`. Structural — never breakable.
///   - `Weak`     : nullable + `(NoAction | SetNull)`. Breakable
///                  without violating data semantics.
///   - `Other`    : not nullable + `(NoAction | SetNull | Restrict)`.
///                  Breaking would orphan rows; never broken.
///
/// `asymmetric2CycleStrategy` is the **resolver** — V1's choice of
/// what to do with classified edges. For 2-member SCCs with exactly
/// one Weak edge, return that edge; otherwise (0 weak / 2+ weak / SCC
/// size != 2), refuse to resolve.
///
/// Both are V1 carryovers from the `EntityDependencySorter` admire
/// (ADMIRE.md, 2026-05-07). Future strategies (manual cycle overrides,
/// minimum-feedback-arc-set search, deferred-junction handling) live
/// alongside in this module — never inside the pure algebra.
[<RequireQualifiedAccess>]
type EdgeStrength =
    | Weak
    | Cascade
    | Other


[<RequireQualifiedAccess>]
module CycleResolution =

    /// Classify an FK reference by source-attribute nullability and
    /// `OnDelete` action. The classifier reads only IR fields — it does
    /// not need any external context — but the *rule* it applies is
    /// V1's domain policy, not algebra. Replace the classifier wholesale
    /// when V2 admits a non-RDBMS catalog whose edges have a different
    /// breakability semantics.
    let classify (sourceKind: Kind) (r: Reference) : EdgeStrength =
        let sourceAttrIsNullable =
            sourceKind.Attributes
            |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
            |> Option.map (fun a -> a.Column.IsNullable)
            |> Option.defaultValue false
        match r.OnDelete, sourceAttrIsNullable with
        | Cascade, _                     -> EdgeStrength.Cascade
        | (NoAction | SetNull), true     -> EdgeStrength.Weak
        | _                              -> EdgeStrength.Other

    /// The output of a resolver step on a single SCC. `EdgesToBreak`
    /// are FK-orientation edges (source → target) the resolver
    /// authorizes the algebra to remove; `Reason` is a human-readable
    /// diagnostic recorded in `CycleDiagnostic.Reason`.
    type ResolutionStep = {
        EdgesToBreak : (SsKey * SsKey) list
        Reason       : string
    }

    /// A resolver: given an SCC's members and the FK-orientation edges
    /// within the SCC (with their classifier-assigned strengths), return
    /// the edges to break and a reason for the diagnostic.
    type Resolver =
        SsKey list -> ((SsKey * SsKey) * EdgeStrength) list -> ResolutionStep

    /// V1's asymmetric-2-cycle strategy, extended (2026-07-06) with the
    /// self-loop rule. For SCCs of size 2 with exactly one Weak edge,
    /// returns that edge as breakable. For SCCs of size 1 (a
    /// self-referencing kind), a Weak self-edge is breakable FOR
    /// ORDERING: the two-phase load's deferral machinery re-points a
    /// nullable self-FK in phase 2 regardless of order (the F1 lift,
    /// DECISIONS 2026-06-10), and a RESOLVED SCC stays in `Cycles`, so
    /// `cycleMembers`/`deferredFkColumns` still defer the column — only
    /// the ORDER stops degrading. Before this rule, one nullable
    /// self-reference anywhere (Category.Parent, Employee.Manager — the
    /// common OutSystems shapes) refused resolution and dropped the
    /// WHOLE catalog to the alphabetical fallback, re-ordering every
    /// unrelated parent/child pair; a subset transfer then loaded
    /// children before their parents and mass-dropped FK rows (found
    /// 2026-07-06 by the peer-aligned two-environment canary). Still
    /// refuses every other shape — 0 Weak, multiple-Weak 2-cycles,
    /// non-Weak self-edges, larger SCCs — leaving those for the
    /// alphabetical fallback.
    let asymmetric2CycleStrategy : Resolver =
        fun members internalEdges ->
            match members with
            | [ single ] ->
                let weakSelfEdges =
                    internalEdges
                    |> List.filter (fun ((s, t), strength) ->
                        s = single && t = single && strength = EdgeStrength.Weak)
                    |> List.map fst
                match weakSelfEdges with
                | [] ->
                    { EdgesToBreak = []
                      Reason       = "self-referencing SCC has no Weak (nullable) self-edge to defer" }
                | edges ->
                    { EdgesToBreak = edges
                      Reason       = "auto-resolved by deferring the nullable self-reference to phase 2" }
            | [_; _] ->
                // Self-edges are not the 2-cycle's business — a member's
                // self-reference rides its own deferral; only the two
                // INTER-member edges decide asymmetry here (the pre-2026-07-06
                // semantics, preserved now that `internalEdgesOf` reports
                // self-pairs).
                let weakEdges =
                    internalEdges
                    |> List.filter (fun ((s, t), strength) -> s <> t && strength = EdgeStrength.Weak)
                match weakEdges with
                | [ (edge, _) ] ->
                    { EdgesToBreak = [ edge ]
                      Reason       = "auto-resolved by removing weak edge" }
                | [] ->
                    { EdgesToBreak = []
                      Reason       = "SCC has no Weak edge to break" }
                | _ ->
                    { EdgesToBreak = []
                      Reason       = "SCC has multiple Weak edges; resolver refuses to choose" }
            | larger ->
                { EdgesToBreak = []
                  Reason       =
                    sprintf "SCC of size %d; current resolver handles 2-cycles only"
                        larger.Length }

    /// The "never resolve" strategy — refuse to break any cycle.
    /// Useful for callers that prefer the alphabetical fallback over
    /// any heuristic edge-breaking.
    let neverResolve : Resolver =
        fun members _ ->
            { EdgesToBreak = []
              Reason       =
                sprintf "SCC of size %d; resolver disabled (neverResolve)"
                    members.Length }
