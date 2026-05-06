namespace Projection.Core

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

    /// V1's asymmetric-2-cycle strategy. For SCCs of size 2 with
    /// exactly one Weak edge, returns that edge as breakable. Refuses
    /// to resolve any other shape — 0 Weak, multiple Weak, or larger
    /// SCCs — leaving the cycle for the alphabetical fallback.
    let asymmetric2CycleStrategy : Resolver =
        fun members internalEdges ->
            match members with
            | [_; _] ->
                let weakEdges =
                    internalEdges
                    |> List.filter (fun (_, s) -> s = EdgeStrength.Weak)
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
