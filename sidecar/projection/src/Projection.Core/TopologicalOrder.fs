namespace Projection.Core

/// The observable outcome of running the topological-order pass.
///
/// `Topological` — the catalog is acyclic (or cycles were resolved by the
/// asymmetric-2-cycle resolver) and the order respects every FK
/// dependency.
///
/// `PartialTopological` — at least one cycle the resolver could not break
/// (every internal edge non-deferrable). Every kind OUTSIDE the unresolved
/// cycles sits in its true dependency position; the members of an
/// unresolved cycle order alphabetically among themselves at the position
/// the cycle occupies in the dependency graph. The unresolved cycles ride
/// `Cycles` with empty `BreakableEdges`. Live data loads still refuse
/// (the load-order gate proves nothing about intra-cycle order); the
/// emitted data lanes stay deploy-correct for the whole acyclic majority
/// (DECISIONS 2026-07-18; #669 B-1 — the whole-catalog alphabetical
/// degrade is retired).
///
/// `Alphabetical` — the defensive last resort (resolver residue after a
/// reduction that should have been acyclic): alphabetical-by-SsKey over
/// the whole catalog. Downstream emitters can either reject this mode
/// (data emission) or accept it (diagnostics). Schema emitters ignore
/// the ordering entirely per A33.
///
/// `JunctionDeferred` — the resolver opted to push junction (bridge)
/// kinds to the end of the order to satisfy the data-emission
/// `DeferJunctions` configuration. Topologically valid for the non-junction
/// kinds; junctions are appended in alphabetical-by-SsKey order.
type OrderingMode =
    | Topological
    | PartialTopological
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


/// HOW the resolver chooses which weak edges to break (v7 slice 4;
/// DECISIONS 2026-07-18). `SchemaMinimal` — the exact minimal feedback
/// set at zero cost (minimum cardinality, lexicographic ties;
/// byte-identical default). `EvidenceWeighted` — the SAME solver at a
/// caller-supplied cost function (`CycleResolution.repairCostOf` at the
/// render binding: the Phase-2 repair's row count, T15's norm).
/// Refusal is resolver-invariant across the family (the A46 lemma) —
/// only the break CHOICE varies, so pre-profile planes (the chain
/// prefix, the drain) stay `SchemaMinimal` soundly.
type ResolutionPolicy =
    | SchemaMinimal
    | EvidenceWeighted of CycleResolution.EdgeCost

/// Combined ordering configuration for the topological-order pass.
/// Bundles the three orthogonal ordering axes — self-loop handling,
/// junction deferral, and break-choice resolution — so callers that
/// need to configure one axis don't have to change the call site for
/// the others (harmonization-via-parameterization per A40). The
/// default config reproduces pre-H-040 behaviour.
type OrderingConfig = {
    SelfLoops        : SelfLoopPolicy
    JunctionDeferral : JunctionDeferralPolicy
    Resolution       : ResolutionPolicy
}

[<RequireQualifiedAccess>]
module OrderingConfig =

    /// Default ordering configuration: treat self-edges as cycles,
    /// emit junction kinds at their topological position, and choose
    /// breaks schema-minimally.
    let defaultConfig : OrderingConfig =
        { SelfLoops        = TreatAsCycle
          JunctionDeferral = EmitInTopologicalOrder
          Resolution       = SchemaMinimal }


/// Per-SCC outcome of cycle resolution — a CLOSED DU (v7; DECISIONS
/// 2026-07-18). The resolved/refused discriminant is the constructor,
/// not a `BreakableEdges = []` convention: the type theorem replaces the
/// sentinel. Members and edges are keyed by `SsKey`; display goes
/// through `CycleDiagnostic.reasonText` — emit it, never parse it.
[<RequireQualifiedAccess>]
type CycleDiagnostic =
    /// The resolver broke a weak feedback set; the SCC's load order is
    /// proven by the re-run. `broken` are FK-orientation edges;
    /// `objective` names HOW the set was chosen (exact minimum, greedy
    /// walk, or the named above-threshold downgrade).
    | Resolved of members: SsKey list * broken: (SsKey * SsKey) list * objective: CycleResolution.BreakObjective
    /// An all-strong cycle exists — `certificate` carries it (unforgeable:
    /// closed + zero Weak by construction) and `relaxation` names the
    /// cheapest strong edges whose columns, made nullable, admit
    /// automatic resolution.
    | Refused of members: SsKey list * certificate: CycleResolution.StrongCycleCertificate * relaxation: (SsKey * SsKey) list
    /// The defensive residue arms (resolver disabled; no cycle found in
    /// a supposed SCC) — unresolved for every consumer, with the note
    /// carrying the legible degradation.
    | Anomalous of members: SsKey list * note: string

[<RequireQualifiedAccess>]
module CycleDiagnostic =

    let members (c: CycleDiagnostic) : SsKey list =
        match c with
        | CycleDiagnostic.Resolved (m, _, _) -> m
        | CycleDiagnostic.Refused (m, _, _) -> m
        | CycleDiagnostic.Anomalous (m, _) -> m

    /// The broken edges — `[]` for `Refused`/`Anomalous` (nothing was
    /// broken; the component's internal order is unproven).
    let breakableEdges (c: CycleDiagnostic) : (SsKey * SsKey) list =
        match c with
        | CycleDiagnostic.Resolved (_, broken, _) -> broken
        | CycleDiagnostic.Refused _ | CycleDiagnostic.Anomalous _ -> []

    let isResolved (c: CycleDiagnostic) : bool =
        match c with
        | CycleDiagnostic.Resolved _ -> true
        | CycleDiagnostic.Refused _ | CycleDiagnostic.Anomalous _ -> false

    /// The display projection — one owner of the diagnostic copy,
    /// delegating to `CycleResolution.describe`'s phrases so the DU
    /// migration changed no operator-visible text.
    let reasonText (c: CycleDiagnostic) : string =
        match c with
        | CycleDiagnostic.Resolved (_, broken, objective) ->
            CycleResolution.describe
                { EdgesToBreak = broken
                  Reason       = CycleResolution.ResolutionReason.AutoResolved objective }
        | CycleDiagnostic.Refused (_, certificate, relaxation) ->
            CycleResolution.describe
                { EdgesToBreak = []
                  Reason       = CycleResolution.ResolutionReason.Refused (certificate, relaxation) }
        | CycleDiagnostic.Anomalous (_, note) -> note


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

/// The quotient of the FK graph by its cycle components — ACYCLIC BY
/// CONSTRUCTION (v7 slice 7; DECISIONS 2026-07-18). Nodes are each
/// `Cycles` member-set plus a singleton per remaining ordered kind;
/// edges are the cross-component projections of `t.Edges`. The private
/// constructor Kahn-checks the induced graph and REFUSES residue, so a
/// value of this type IS a DAG — the type theorem (the ArtifactByKind
/// move). Well-defined off a pass-produced order because the resolver
/// only ever breaks INTRA-component edges: every cross-component edge
/// survives into `Edges`. The emitted `Order` is a LINEAR EXTENSION of
/// this quotient (property-pinned; the containment form — v6's re-run
/// Kahn may interleave outsiders between an unresolved component's
/// members, so node-atomic equality is deliberately NOT claimed).
type Condensation = private {
    CNodes : SsKey list list
    CEdges : (SsKey * SsKey) list
}

[<RequireQualifiedAccess>]
module Condensation =

    /// Build the quotient from a pass-produced order. `Error` names the
    /// impossible state (a cyclic quotient — the resolver broke only
    /// intra-component edges, so cross-component cycles cannot exist off
    /// a live pass output; a hand-built order CAN produce one, and the
    /// refusal is the named guard).
    let ofOrder (t: TopologicalOrder) : Result<Condensation, string> =
        let components =
            t.Cycles |> List.map (fun c -> CycleDiagnostic.members c |> List.sort)
        let inComponent =
            components
            |> List.indexed
            |> List.collect (fun (i, ms) -> ms |> List.map (fun m -> m, i))
            |> Map.ofList
        let singletons =
            t.Order
            |> List.filter (fun k -> not (Map.containsKey k inComponent))
            |> List.map (fun k -> [ k ])
        let nodes = (components @ singletons) |> List.sortBy List.head
        // A component head represents its node; the projection keys every
        // member to the head.
        let headOf =
            nodes
            |> List.collect (fun ms -> ms |> List.map (fun m -> m, List.head ms))
            |> Map.ofList
        let edges =
            t.Edges
            |> List.choose (fun (s, tgt) ->
                match Map.tryFind s headOf, Map.tryFind tgt headOf with
                | Some hs, Some ht when hs <> ht -> Some (hs, ht)
                | _ -> None)
            |> List.distinct
            |> List.sort
        // The smart ctor's law: the quotient is acyclic (Kahn over the
        // head-keyed edges; residue refuses).
        let heads = nodes |> List.map List.head
        let mutable indegree = heads |> List.map (fun h -> h, 0) |> Map.ofList
        for (_, ht) in edges do
            indegree <- Map.add ht (Map.find ht indegree + 1) indegree
        let mutable ready = heads |> List.filter (fun h -> Map.find h indegree = 0) |> List.sort
        let mutable seen = 0
        let childrenOf =
            edges |> List.groupBy fst |> List.map (fun (h, es) -> h, es |> List.map snd) |> Map.ofList
        while not (List.isEmpty ready) do
            let h = List.head ready
            ready <- List.tail ready
            seen <- seen + 1
            for child in (Map.tryFind h childrenOf |> Option.defaultValue []) do
                let d = Map.find child indegree - 1
                indegree <- Map.add child d indegree
                if d = 0 then ready <- List.sort (child :: ready)
        if seen <> List.length heads then
            Error "the condensation is cyclic — a cross-component cycle survived quotienting; please report"
        else
            Ok { CNodes = nodes; CEdges = edges }

    let nodes (c: Condensation) : SsKey list list = c.CNodes
    let edges (c: Condensation) : (SsKey * SsKey) list = c.CEdges

    /// Kahn-level assignment over the quotient DAG — level k+1 may
    /// depend on level k; within a level, nodes are independent.
    let levels (c: Condensation) : SsKey list list list =
        let headOf (ms: SsKey list) = List.head ms
        let parentsOf =
            c.CEdges
            |> List.groupBy fst
            |> List.map (fun (child, es) -> child, es |> List.map snd)
            |> Map.ofList
        let nodeByHead = c.CNodes |> List.map (fun ms -> headOf ms, ms) |> Map.ofList
        // Longest-path level: 1 + max parent level (0 for roots), computed
        // over the DAG in a topological pass (iterate until fixpoint over
        // the acyclic edges — bounded by node count).
        let mutable levelMap : Map<SsKey, int> = c.CNodes |> List.map (fun ms -> headOf ms, 0) |> Map.ofList
        let mutable changed = true
        while changed do
            changed <- false
            for (child, _) in (parentsOf |> Map.toList) do
                let parents = Map.find child parentsOf
                let want =
                    parents
                    |> List.map (fun p -> Map.tryFind p levelMap |> Option.defaultValue 0)
                    |> List.fold max -1
                    |> (+) 1
                if Map.find child levelMap < want then
                    levelMap <- Map.add child want levelMap
                    changed <- true
        levelMap
        |> Map.toList
        |> List.groupBy snd
        |> List.sortBy fst
        |> List.map (fun (_, pairs) ->
            pairs |> List.map (fun (h, _) -> Map.find h nodeByHead) |> List.sortBy List.head)

/// v7 slice 8 (DECISIONS 2026-07-18) — the certificate's ONE Voice copy.
/// A refused component's narration joins the certificate's edges to the
/// catalog (column name, nullability, delete rule) and closes with the
/// cheapest relaxation as an imperative. Three surfaces consume this one
/// projection — the estate board's advisory, the transfer load gate, and
/// the go board — so the operator reads the same sentence everywhere.
[<RequireQualifiedAccess>]
module CycleNarration =

    let private actionText (a: ReferenceAction) : string =
        match a with
        | NoAction -> "no action"
        | Cascade -> "cascade"
        | SetNull -> "set null"
        | Restrict -> "restrict"

    let certificateText (catalog: Catalog) (diagnostic: CycleDiagnostic) : string option =
        match diagnostic with
        | CycleDiagnostic.Resolved _ -> None
        | CycleDiagnostic.Anomalous _ -> None
        | CycleDiagnostic.Refused (_, certificate, relaxation) ->
            let kindIndex = Catalog.kindIndex catalog
            let kindNameOf (key: SsKey) : string =
                Map.tryFind key kindIndex
                |> Option.map (fun k -> Name.value k.Name)
                |> Option.defaultValue (SsKey.rootOriginal key)
            let edgeDetail ((s, t): SsKey * SsKey) : string =
                Map.tryFind s kindIndex
                |> Option.bind (fun k ->
                    k.References
                    |> List.tryPick (fun r ->
                        if r.TargetKind = t then
                            Kind.tryFindAttribute r.SourceAttribute k
                            |> Option.map (fun a ->
                                sprintf "%s.%s (%s, on delete %s) -> %s"
                                    (kindNameOf s) (Name.value a.Name)
                                    (if a.Column.IsNullable then "nullable" else "NOT NULL")
                                    (actionText r.OnDelete)
                                    (kindNameOf t))
                        else None))
                |> Option.defaultValue (sprintf "%s -> %s" (kindNameOf s) (kindNameOf t))
            let certLine =
                CycleResolution.StrongCycleCertificate.edges certificate
                |> List.map (fst >> edgeDetail)
                |> String.concat "; "
            let relaxationLine =
                match relaxation with
                | [] -> "make one of its FK columns nullable — it then defers to phase 2 automatically"
                | edges ->
                    let columns =
                        edges
                        |> List.choose (fun (s, t) ->
                            Map.tryFind s kindIndex
                            |> Option.bind (fun k ->
                                k.References
                                |> List.tryPick (fun r ->
                                    if r.TargetKind = t then
                                        Kind.tryFindAttribute r.SourceAttribute k
                                        |> Option.map (fun a -> sprintf "%s.%s" (kindNameOf s) (Name.value a.Name))
                                    else None)))
                    match columns with
                    | [] -> "make one of its FK columns nullable — it then defers to phase 2 automatically"
                    | cols ->
                        sprintf "make %s nullable — it then defers to phase 2 automatically"
                            (String.concat " and " cols)
            Some (sprintf "the cycle's edges: %s. Cheapest fix: %s." certLine relaxationLine)

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
        |> List.collect CycleDiagnostic.members
        |> Set.ofList

    /// The per-COMPONENT member sets (v7 slice 3; DECISIONS 2026-07-18) —
    /// one set per diagnostic, resolved and unresolved alike. "Same
    /// cycle" means "same component": the flat union (`cycleMembers`)
    /// deferred columns BETWEEN distinct cycles though the condensation
    /// order proves them, and — worse — flagged a non-nullable FK
    /// between two DISTINCT unresolved cycles as unbreakable: a false
    /// refusal the actual order satisfies. Scope-judged consumers
    /// (`deferredFkColumns`, the plan's unsatisfiability) take THIS.
    let cycleScopes (t: TopologicalOrder) : Set<SsKey> list =
        t.Cycles
        |> List.map (CycleDiagnostic.members >> Set.ofList)

    /// The unresolved components' member sets only — the
    /// unsatisfiability input, per component.
    let unresolvedCycleScopes (t: TopologicalOrder) : Set<SsKey> list =
        t.Cycles
        |> List.filter (CycleDiagnostic.isResolved >> not)
        |> List.map (CycleDiagnostic.members >> Set.ofList)

    /// v7 slice 5 — one component's DEFERRAL input. A RESOLVED component
    /// defers exactly its BROKEN edges' columns: the re-run order proves
    /// every unbroken edge, so deferring a non-broken column was pure
    /// Phase-2 norm inflation. A Refused/Anomalous component carries
    /// `BrokenEdges = None` — its internal order proves nothing, so the
    /// blanket rule stands: every nullable intra-component edge defers
    /// (the best-effort load inside an unproven component).
    type DeferralScope = {
        Members     : Set<SsKey>
        BrokenEdges : Set<SsKey * SsKey> option
    }

    /// The per-component deferral inputs (v7 slice 5) — the exact repair
    /// set's carrier. Replaces the member-set list at every DEFERRAL
    /// judge; the UNSATISFIABILITY judge keeps `unresolvedCycleScopes`
    /// (membership is its whole question).
    let deferralScopes (t: TopologicalOrder) : DeferralScope list =
        t.Cycles
        |> List.map (fun c ->
            match c with
            | CycleDiagnostic.Resolved (members, broken, _) ->
                { Members = Set.ofList members; BrokenEdges = Some (Set.ofList broken) }
            | CycleDiagnostic.Refused (members, _, _)
            | CycleDiagnostic.Anomalous (members, _) ->
                { Members = Set.ofList members; BrokenEdges = None })

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
        |> List.filter (CycleDiagnostic.isResolved >> not)
        |> List.collect CycleDiagnostic.members
        |> Set.ofList

    /// The FK columns of `k` that must be deferred across the two-phase
    /// nulls-then-FKs load: `k` and the FK's target share ONE component
    /// (v7 slice 3 — "same cycle" means "same component", never the
    /// union of all cycles), and the source column is nullable (so
    /// phase 1 can NULL it and phase 2 re-points it). A non-nullable
    /// cycle FK cannot defer — that is the consuming layer's diagnostic,
    /// not represented here. Shared by the forward data emitters
    /// (`StaticSeedsEmitter` / `MigrationDependenciesEmitter`) and the
    /// Transfer plan.
    let deferredFkColumns (scopes: DeferralScope list) (k: Kind) : Set<Name> =
        let owningScopes = scopes |> List.filter (fun s -> Set.contains k.SsKey s.Members)
        if List.isEmpty owningScopes then Set.empty
        else
            k.References
            |> List.choose (fun r ->
                let deferredByScope (s: DeferralScope) : bool =
                    if not (Set.contains r.TargetKind s.Members) then false
                    else
                        match s.BrokenEdges with
                        // v7 slice 5 — resolved: ONLY a broken edge's column
                        // defers; the proven order carries the rest.
                        | Some broken -> Set.contains (k.SsKey, r.TargetKind) broken
                        // Unresolved: the blanket intra-component rule.
                        | None -> true
                if owningScopes |> List.exists deferredByScope then
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
            // The mint refuses to license parallelism it cannot prove:
            // these modes carry no dependency proof at all, so the honest
            // degenerate stays singleton groups in `t.Order` order.
            t.Order |> List.map (fun k -> ParallelSafe [ k ])
        | PartialTopological ->
            // v7 slice 7 (DECISIONS 2026-07-18) — the condensation retires
            // the estate-wide singleton degrade: the quotient DAG is
            // proven, so cross-component parallelism stays licensed. Per
            // condensation level: the level's SINGLETON components form
            // one concurrent-safe group; each multi-member component
            // contributes its members as CONSECUTIVE SINGLETON groups in
            // `t.Order` relative order (their internal precedence is
            // unproven — serialization is exactly as wide as the cycle).
            match Condensation.ofOrder t with
            | Error _ ->
                // The defensive arm: an impossible quotient degrades to
                // the pre-v7 honest singletons.
                t.Order |> List.map (fun k -> ParallelSafe [ k ])
            | Ok condensation ->
                let orderIndex = t.Order |> List.mapi (fun i k -> k, i) |> Map.ofList
                Condensation.levels condensation
                |> List.collect (fun levelNodes ->
                    let singles =
                        levelNodes
                        |> List.filter (fun ms -> List.length ms = 1)
                        |> List.map List.head
                        |> List.sort
                    let multis =
                        levelNodes
                        |> List.filter (fun ms -> List.length ms > 1)
                        |> List.sortBy List.head
                    [ if not (List.isEmpty singles) then yield ParallelSafe singles
                      for ms in multis do
                        for m in ms |> List.sortBy (fun k -> Map.tryFind k orderIndex |> Option.defaultValue 0) do
                            yield ParallelSafe [ m ] ])
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
