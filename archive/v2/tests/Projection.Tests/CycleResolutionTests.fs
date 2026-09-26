module Projection.Tests.CycleResolutionTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// classify — V1 RDBMS-flavored rule mapping (IsNullable, OnDelete) to
// EdgeStrength. Pure function of two IR fields; testable in isolation.
// ---------------------------------------------------------------------------

[<Fact>]
let ``classify: non-nullable + NoAction = Other`` () =
    // The synthetic fixture's Order.CustomerId attribute is non-nullable
    // and the reference's OnDelete is NoAction.
    let result = CycleResolution.classify order (List.head order.References)
    Assert.Equal<EdgeStrength>(EdgeStrength.Other, result)

let private mkAttr (key: string) (nullable: bool) : Attribute =
    { Attribute.create (testKey key) (Name.create "Fk" |> Result.value) Integer with Column = ColumnRealization.create ("FK") (nullable) |> Result.value }

let private mkRef (sourceAttrKey: string) (action: ReferenceAction) : Reference =
    { Reference.create (refKey ["x"]) (Name.create "x" |> Result.value) (testKey sourceAttrKey) (kindKey ["target"]) with OnDelete = action }

let private kindWith (a: Attribute) : Kind =
    Kind.create (kindKey ["owner"]) (Name.create "owner" |> Result.value) (mkTableId "dbo" "owner") [ a ]

[<Fact>]
let ``classify: nullable + NoAction = Weak`` () =
    let attr = mkAttr "OS_ATTR_fk" true
    let r = mkRef "OS_ATTR_fk" NoAction
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Weak, CycleResolution.classify k r)

[<Fact>]
let ``classify: nullable + SetNull = Weak`` () =
    let attr = mkAttr "OS_ATTR_fk" true
    let r = mkRef "OS_ATTR_fk" SetNull
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Weak, CycleResolution.classify k r)

[<Fact>]
let ``classify: nullable + Cascade = Cascade (Cascade overrides nullability)`` () =
    let attr = mkAttr "OS_ATTR_fk" true
    let r = mkRef "OS_ATTR_fk" ReferenceAction.Cascade
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Cascade, CycleResolution.classify k r)

[<Fact>]
let ``classify: non-nullable + Cascade = Cascade`` () =
    let attr = mkAttr "OS_ATTR_fk" false
    let r = mkRef "OS_ATTR_fk" ReferenceAction.Cascade
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Cascade, CycleResolution.classify k r)

[<Fact>]
let ``classify: non-nullable + SetNull = Other`` () =
    let attr = mkAttr "OS_ATTR_fk" false
    let r = mkRef "OS_ATTR_fk" SetNull
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Other, CycleResolution.classify k r)

[<Fact>]
let ``classify: non-nullable + Restrict = Other`` () =
    let attr = mkAttr "OS_ATTR_fk" false
    let r = mkRef "OS_ATTR_fk" Restrict
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Other, CycleResolution.classify k r)

[<Fact>]
let ``classify: nullable + Restrict = Other (Restrict not breakable)`` () =
    // Restrict means "refuse to delete the parent"; even with a
    // nullable source, the FK semantics don't permit silent breakage.
    let attr = mkAttr "OS_ATTR_fk" true
    let r = mkRef "OS_ATTR_fk" Restrict
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Other, CycleResolution.classify k r)

// ---------------------------------------------------------------------------
// weakFeedbackStrategy — THE COMPLETE CASE MAP (v5, 2026-07-07; retires
// V1's asymmetric-2-cycle heuristic). Pure function of SCC members +
// classified internal edges. The rows below mirror the resolver's
// docstring table; the property suite at the end pins the invariants
// I1-I5 over generated graphs.
// ---------------------------------------------------------------------------

let private aKey = testKey "A"
let private bKey = testKey "B"
let private cKey = testKey "C"
let private dKey = testKey "D"

[<Fact>]
let ``resolver: a Weak self-edge (nullable self-FK) is broken for ordering — phase 2 re-points it`` () =
    // The 2026-07-06 self-loop rule (the peer-canary finding): a nullable
    // self-reference (Category.Parent / Employee.Manager) must not degrade
    // the whole catalog to the alphabetical fallback — the deferral
    // machinery re-points it in phase 2 regardless of order.
    let step =
        CycleResolution.weakFeedbackStrategy [ aKey ] [ (aKey, aKey), EdgeStrength.Weak ]
    Assert.Equal<(SsKey * SsKey) list>([ (aKey, aKey) ], step.EdgesToBreak)
    Assert.Contains("auto-resolved", CycleResolution.describe step)

[<Fact>]
let ``resolver: a non-Weak self-edge still refuses (a mandatory self-FK cannot defer)`` () =
    let step =
        CycleResolution.weakFeedbackStrategy [ aKey ] [ (aKey, aKey), EdgeStrength.Other ]
    Assert.Empty(step.EdgesToBreak)
    Assert.Contains("non-deferrable", CycleResolution.describe step)

[<Fact>]
let ``resolver: 2-cycle with exactly one Weak edge returns that edge`` () =
    let edges =
        [ (aKey, bKey), EdgeStrength.Weak
          (bKey, aKey), EdgeStrength.Other ]
    let step = CycleResolution.weakFeedbackStrategy [ aKey; bKey ] edges
    Assert.Equal<(SsKey * SsKey) list>([ (aKey, bKey) ], step.EdgesToBreak)
    Assert.Contains("auto-resolved", CycleResolution.describe step)

[<Fact>]
let ``resolver: 2-cycle with no Weak edges returns empty and explains`` () =
    let edges =
        [ (aKey, bKey), EdgeStrength.Other
          (bKey, aKey), EdgeStrength.Other ]
    let step = CycleResolution.weakFeedbackStrategy [ aKey; bKey ] edges
    Assert.Empty(step.EdgesToBreak)
    Assert.Contains("non-deferrable", CycleResolution.describe step)

[<Fact>]
let ``resolver v5: 2-cycle with two Weak edges resolves — exactly one edge broken, the smallest, deterministically`` () =
    let edges =
        [ (aKey, bKey), EdgeStrength.Weak
          (bKey, aKey), EdgeStrength.Weak ]
    let step = CycleResolution.weakFeedbackStrategy [ aKey; bKey ] edges
    Assert.Equal<(SsKey * SsKey) list>([ (aKey, bKey) ], step.EdgesToBreak)
    Assert.Contains("auto-resolved", CycleResolution.describe step)
    // Input-order independence (I4): the reversed edge list breaks the
    // same edge.
    let reversedStep = CycleResolution.weakFeedbackStrategy [ bKey; aKey ] (List.rev edges)
    Assert.Equal<(SsKey * SsKey) list>(step.EdgesToBreak, reversedStep.EdgesToBreak)

[<Fact>]
let ``resolver: 2-cycle with Cascade alongside Weak still uses the Weak edge`` () =
    // Cascade is structural; Weak is the only breakable choice.
    let edges =
        [ (aKey, bKey), EdgeStrength.Weak
          (bKey, aKey), EdgeStrength.Cascade ]
    let step = CycleResolution.weakFeedbackStrategy [ aKey; bKey ] edges
    Assert.Equal<(SsKey * SsKey) list>([ (aKey, bKey) ], step.EdgesToBreak)

[<Fact>]
let ``resolver v5: an all-weak 3-cycle resolves with exactly ONE broken edge (I5 — never a blanket sweep)`` () =
    let edges =
        [ (aKey, bKey), EdgeStrength.Weak
          (bKey, cKey), EdgeStrength.Weak
          (cKey, aKey), EdgeStrength.Weak ]
    let step =
        CycleResolution.weakFeedbackStrategy [ aKey; bKey; cKey ] edges
    Assert.Equal(1, step.EdgesToBreak.Length)
    Assert.Contains("auto-resolved", CycleResolution.describe step)

[<Fact>]
let ``resolver v5: a 3-cycle with one strong edge resolves by breaking weak edges only (the strong edge is honored by order)`` () =
    let edges =
        [ (aKey, bKey), EdgeStrength.Weak
          (bKey, cKey), EdgeStrength.Other
          (cKey, aKey), EdgeStrength.Weak ]
    let step =
        CycleResolution.weakFeedbackStrategy [ aKey; bKey; cKey ] edges
    Assert.Equal(1, step.EdgesToBreak.Length)
    Assert.DoesNotContain((bKey, cKey), step.EdgesToBreak)

[<Fact>]
let ``resolver v5: an all-strong 3-cycle refuses, naming the exact cycle members`` () =
    let edges =
        [ (aKey, bKey), EdgeStrength.Other
          (bKey, cKey), EdgeStrength.Cascade
          (cKey, aKey), EdgeStrength.Other ]
    let step =
        CycleResolution.weakFeedbackStrategy [ aKey; bKey; cKey ] edges
    Assert.Empty(step.EdgesToBreak)
    Assert.Contains("non-deferrable", CycleResolution.describe step)
    Assert.Contains("A", CycleResolution.describe step)
    Assert.Contains("B", CycleResolution.describe step)
    Assert.Contains("C", CycleResolution.describe step)

[<Fact>]
let ``resolver v5: a strong cycle nested in a larger weak SCC refuses — the weak decorations cannot save it (I3)`` () =
    // A <-> B strong (the poison pair) decorated with weak edges through
    // C and D. Whatever weak edges break, the strong 2-cycle survives —
    // refusal names it.
    let edges =
        [ (aKey, bKey), EdgeStrength.Other
          (bKey, aKey), EdgeStrength.Other
          (bKey, cKey), EdgeStrength.Weak
          (cKey, dKey), EdgeStrength.Weak
          (dKey, aKey), EdgeStrength.Weak ]
    let step =
        CycleResolution.weakFeedbackStrategy [ aKey; bKey; cKey; dKey ] edges
    Assert.Empty(step.EdgesToBreak)
    Assert.Contains("non-deferrable", CycleResolution.describe step)

[<Fact>]
let ``resolver v5: two fused weak rings resolve — one break per residual cycle, graph acyclic after`` () =
    // Ring 1: A -> B -> A; Ring 2: B -> C -> B. Sharing member B.
    let edges =
        [ (aKey, bKey), EdgeStrength.Weak
          (bKey, aKey), EdgeStrength.Weak
          (bKey, cKey), EdgeStrength.Weak
          (cKey, bKey), EdgeStrength.Weak ]
    let step =
        CycleResolution.weakFeedbackStrategy [ aKey; bKey; cKey ] edges
    Assert.Equal(2, step.EdgesToBreak.Length)
    Assert.Contains("auto-resolved", CycleResolution.describe step)

[<Fact>]
let ``resolver v5: empty-edges SCC (degenerate) returns empty and degrades legibly`` () =
    let step = CycleResolution.weakFeedbackStrategy [ aKey; bKey ] []
    Assert.Empty(step.EdgesToBreak)
    Assert.Contains("no cycle found", CycleResolution.describe step)

// ---------------------------------------------------------------------------
// neverResolve — opt-out resolver for callers that prefer alphabetical
// fallback over any heuristic edge-breaking.
// ---------------------------------------------------------------------------

[<Fact>]
let ``neverResolve: returns empty for any SCC`` () =
    let edges =
        [ (aKey, bKey), EdgeStrength.Weak
          (bKey, aKey), EdgeStrength.Weak ]
    let step = CycleResolution.neverResolve [ aKey; bKey ] edges
    Assert.Empty(step.EdgesToBreak)
    Assert.Contains("disabled", CycleResolution.describe step)

[<Fact>]
let ``neverResolve: notes the SCC size in the diagnostic`` () =
    let step = CycleResolution.neverResolve [ aKey; bKey; cKey ] []
    Assert.Contains("size 3", CycleResolution.describe step)

// ---------------------------------------------------------------------------
// Resolver type-shape — a Resolver is `SsKey list -> ((SsKey * SsKey) *
// EdgeStrength) list -> ResolutionStep`. Calling sites can pass any
// function of that shape; the algebra in TopologicalOrderPass doesn't
// know which strategy is in use.
// ---------------------------------------------------------------------------

[<Fact>]
let ``resolver shape: a custom resolver can be passed by callers`` () =
    let alwaysFirst : CycleResolution.Resolver =
        fun _members internalEdges ->
            match internalEdges with
            | (edge, _) :: _ ->
                { EdgesToBreak = [ edge ]
                  Reason       = CycleResolution.ResolutionReason.AutoResolved CycleResolution.BreakObjective.GreedyWalk }
            | [] ->
                { EdgesToBreak = []
                  Reason       = CycleResolution.ResolutionReason.NoCycleFound }
    let edges =
        [ (aKey, bKey), EdgeStrength.Other
          (bKey, aKey), EdgeStrength.Other ]
    let step = alwaysFirst [ aKey; bKey ] edges
    Assert.Equal<(SsKey * SsKey) list>([ (aKey, bKey) ], step.EdgesToBreak)
    Assert.NotEmpty step.EdgesToBreak

// ---------------------------------------------------------------------------
// The invariant property suite (I1–I5 over generated graphs). Members are
// P0..P3; edges are generated from raw byte triples so FsCheck shrinks
// cleanly. Parallel strengths combine through `combineStrength` — the same
// pre-processing the pass performs.
// ---------------------------------------------------------------------------

open FsCheck
open FsCheck.Xunit

let private pKey (i: int) : SsKey = testKey (sprintf "P%d" i)

let private strengthFor (s: int) : EdgeStrength =
    match ((s % 3) + 3) % 3 with
    | 0 -> EdgeStrength.Weak
    | 1 -> EdgeStrength.Other
    | _ -> EdgeStrength.Cascade

let private edgesFrom (raw: (byte * byte * byte) list) : ((SsKey * SsKey) * EdgeStrength) list =
    raw
    |> List.map (fun (a, b, st) ->
        (pKey (int a % 4), pKey (int b % 4)), strengthFor (int st))
    |> List.groupBy fst
    |> List.map (fun (e, xs) -> e, xs |> List.map snd |> List.reduce CycleResolution.combineStrength)

/// Simple acyclicity check by iterative source-elimination (Kahn) over
/// FK-orientation edges — independent of the production algorithm.
let private isAcyclic (edges: (SsKey * SsKey) list) : bool =
    let rec strip (remaining: (SsKey * SsKey) list) : bool =
        if List.isEmpty remaining then true
        else
            let nodes = remaining |> List.collect (fun (s, t) -> [ s; t ]) |> List.distinct
            let withIncoming = remaining |> List.map snd |> Set.ofList
            let sources = nodes |> List.filter (fun n -> not (Set.contains n withIncoming))
            if List.isEmpty sources then false   // every node has an incoming edge → cycle
            else
                let sourceSet = Set.ofList sources
                strip (remaining |> List.filter (fun (s, _) -> not (Set.contains s sourceSet)))
    strip (edges |> List.filter (fun (s, t) -> s <> t))
    && not (edges |> List.exists (fun (s, t) -> s = t))   // any self-edge is a cycle

[<Property>]
let ``I1 soundness: every edge the resolver breaks is Weak (hence nullable, hence phase-2 deferrable)`` (raw: (byte * byte * byte) list) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let step = CycleResolution.weakFeedbackStrategy members edges
    let strengths = Map.ofList edges
    step.EdgesToBreak
    |> List.forall (fun e -> Map.tryFind e strengths = Some EdgeStrength.Weak)

[<Property>]
let ``I2 acyclicity: when the resolver resolves, the SCC minus the broken edges is acyclic`` (raw: (byte * byte * byte) list) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let step = CycleResolution.weakFeedbackStrategy members edges
    List.isEmpty step.EdgesToBreak
    || isAcyclic (edges |> List.map fst |> List.filter (fun e -> not (List.contains e step.EdgesToBreak)))

[<Property>]
let ``I3 refusal precision: the resolver refuses a cyclic graph exactly when removing every Weak edge still leaves a cycle`` (raw: (byte * byte * byte) list) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let step = CycleResolution.weakFeedbackStrategy members edges
    let allEdges = edges |> List.map fst
    let strongOnly =
        edges |> List.choose (fun (e, st) -> if st <> EdgeStrength.Weak then Some e else None)
    if isAcyclic allEdges then
        // No cycle to resolve — the resolver breaks nothing.
        List.isEmpty step.EdgesToBreak
    elif isAcyclic strongOnly then
        // Every cycle passes through a Weak edge — must resolve.
        not (List.isEmpty step.EdgesToBreak)
    else
        // Some all-strong cycle — must refuse, by name.
        List.isEmpty step.EdgesToBreak && (CycleResolution.describe step).Contains "non-deferrable"

[<Property>]
let ``I4 determinism: the broken set is independent of input edge order`` (raw: (byte * byte * byte) list) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let forward = CycleResolution.weakFeedbackStrategy members edges
    let reversed = CycleResolution.weakFeedbackStrategy (List.rev members) (List.rev edges)
    forward.EdgesToBreak = reversed.EdgesToBreak

[<Property>]
let ``I5 frugality: the resolver never breaks more Weak edges than the graph has residual cycles to break (a pure ring breaks one)`` (n: byte) =
    // A pure weak ring of size 2..4 breaks EXACTLY one edge.
    let size = 2 + int n % 3
    let members = [ 0 .. size - 1 ] |> List.map pKey
    let edges =
        [ 0 .. size - 1 ]
        |> List.map (fun i -> (pKey i, pKey ((i + 1) % size)), EdgeStrength.Weak)
    let step = CycleResolution.weakFeedbackStrategy members edges
    step.EdgesToBreak.Length = 1

// ---------------------------------------------------------------------------
// v7 — the minimal weak feedback set (`minimalFeedbackStrategy` /
// `defaultStrategy`; DECISIONS 2026-07-18, the measured-minimality program).
// I1–I4 restated over the new default; I5′/I6/I7 are the exact-path laws.
// ---------------------------------------------------------------------------

/// Test-side independent optimum: brute-force every weak subset, keep the
/// feasible minimum of (cardinality, lexicographic). Mirrors nothing of the
/// production enumeration beyond the objective's definition.
let private bruteForceMinimum
    (edges: ((SsKey * SsKey) * EdgeStrength) list)
    : (SsKey * SsKey) list option =
    let strong = edges |> List.choose (fun (e, s) -> if s <> EdgeStrength.Weak then Some e else None)
    let weak = edges |> List.choose (fun (e, s) -> if s = EdgeStrength.Weak then Some e else None) |> List.sort
    let n = List.length weak
    let weakArr = List.toArray weak
    let mutable best : (int * (SsKey * SsKey) list) option = None
    for mask in 0 .. (1 <<< n) - 1 do
        let subset = [ for i in 0 .. n - 1 do if mask &&& (1 <<< i) <> 0 then yield weakArr.[i] ]
        let remaining = strong @ (weak |> List.except subset)
        if isAcyclic remaining then
            let candidate = (List.length subset, List.sort subset)
            match best with
            | None -> best <- Some candidate
            | Some cur -> if compare candidate cur < 0 then best <- Some candidate
    best |> Option.map snd

[<Property>]
let ``v7 I1 soundness: every edge the exact resolver breaks is Weak`` (raw: (byte * byte * byte) list) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let step = CycleResolution.defaultStrategy members edges
    let strengths = Map.ofList edges
    step.EdgesToBreak
    |> List.forall (fun e -> Map.tryFind e strengths = Some EdgeStrength.Weak)

[<Property>]
let ``v7 I2 acyclicity: when the exact resolver resolves, the SCC minus the broken edges is acyclic`` (raw: (byte * byte * byte) list) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let step = CycleResolution.defaultStrategy members edges
    List.isEmpty step.EdgesToBreak
    || isAcyclic (edges |> List.map fst |> List.filter (fun e -> not (List.contains e step.EdgesToBreak)))

[<Property>]
let ``v7 I3 refusal precision: the exact resolver refuses exactly when the strong-only subgraph is cyclic`` (raw: (byte * byte * byte) list) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let step = CycleResolution.defaultStrategy members edges
    let allEdges = edges |> List.map fst
    let strongOnly =
        edges |> List.choose (fun (e, st) -> if st <> EdgeStrength.Weak then Some e else None)
    if isAcyclic allEdges then List.isEmpty step.EdgesToBreak
    elif isAcyclic strongOnly then not (List.isEmpty step.EdgesToBreak)
    else List.isEmpty step.EdgesToBreak && (CycleResolution.describe step).Contains "non-deferrable"

[<Property>]
let ``v7 I4 determinism: the exact break set is independent of input edge order`` (raw: (byte * byte * byte) list) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let forward = CycleResolution.defaultStrategy members edges
    let reversed = CycleResolution.defaultStrategy (List.rev members) (List.rev edges)
    forward.EdgesToBreak = reversed.EdgesToBreak

[<Property>]
let ``v7 I5′+I6: the exact break set IS the brute-force minimum (cardinality, then lexicographic) among feasible weak subsets`` (raw: (byte * byte * byte) list) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let step = CycleResolution.defaultStrategy members edges
    let allEdges = edges |> List.map fst
    let strongOnly =
        edges |> List.choose (fun (e, st) -> if st <> EdgeStrength.Weak then Some e else None)
    // Only the resolution arm makes a minimality claim.
    if isAcyclic allEdges || not (isAcyclic strongOnly) then true
    else
        match bruteForceMinimum edges with
        | Some expected -> step.EdgesToBreak = expected
        | None -> false

[<Property>]
let ``v7 I7: the exact break set is never larger than greedy's on the same graph`` (raw: (byte * byte * byte) list) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let exact = CycleResolution.defaultStrategy members edges
    let greedy = CycleResolution.weakFeedbackStrategy members edges
    exact.EdgesToBreak.Length <= greedy.EdgesToBreak.Length

[<Fact>]
let ``v7: exact beats greedy — one shared weak edge breaks both cycles where greedy broke two`` () =
    // Edges (a,b),(b,c),(c,a) and (b,c),(c,d),(d,b): two rings sharing
    // (b,c). Greedy walks the first found cycle and breaks its smallest
    // weak edge, then the second ring's — two breaks. The exact solver
    // finds the singleton {(b,c)} feasible and minimal.
    let k (s: string) = SsKey.synthesized "V7" s |> Result.value
    let a, b, c, d = k "a", k "b", k "c", k "d"
    let edges =
        [ (a, b); (b, c); (c, a); (c, d); (d, b) ]
        |> List.map (fun e -> e, EdgeStrength.Weak)
    let step = CycleResolution.defaultStrategy [ a; b; c; d ] edges
    Assert.Equal<(SsKey * SsKey) list>([ (b, c) ], step.EdgesToBreak)
    Assert.Contains("auto-resolved: 1 weak", CycleResolution.describe step)

[<Fact>]
let ``v7: above the exact threshold the greedy walk runs and the downgrade is named`` () =
    // A 13-edge pure weak ring exceeds exactWeakEdgeThreshold = 12; the
    // greedy engine resolves it (one break) and the reason names the
    // downgrade (downgrades never silent).
    let size = CycleResolution.exactWeakEdgeThreshold + 1
    let k (i: int) = SsKey.synthesized "V7T" (sprintf "n%02d" i) |> Result.value
    let members = [ 0 .. size - 1 ] |> List.map k
    let edges =
        [ 0 .. size - 1 ]
        |> List.map (fun i -> (k i, k ((i + 1) % size)), EdgeStrength.Weak)
    let step = CycleResolution.defaultStrategy members edges
    Assert.Equal(1, step.EdgesToBreak.Length)
    Assert.Contains("greedy above the exact threshold", CycleResolution.describe step)

[<Fact>]
let ``v7: the weighted family member prefers the cheap edge — cost breaks the tie cardinality cannot`` () =
    // A symmetric weak 2-cycle: both singletons feasible, equal
    // cardinality. Zero cost picks lexicographically; a cost function
    // making the lexicographically-first edge EXPENSIVE flips the choice
    // — the objective's first component dominates.
    let k (s: string) = SsKey.synthesized "V7W" s |> Result.value
    let x, y = k "x", k "y"
    let edges = [ ((x, y), EdgeStrength.Weak); ((y, x), EdgeStrength.Weak) ]
    let zeroCost = CycleResolution.defaultStrategy [ x; y ] edges
    let costOf (e: SsKey * SsKey) : int64 = if e = List.min [ (x, y); (y, x) ] then 1000L else 1L
    let weighted = CycleResolution.minimalFeedbackStrategy costOf [ x; y ] edges
    Assert.Equal(1, zeroCost.EdgesToBreak.Length)
    Assert.Equal(1, weighted.EdgesToBreak.Length)
    Assert.Equal<(SsKey * SsKey) list>([ List.max [ (x, y); (y, x) ] ], weighted.EdgesToBreak)
    Assert.NotEqual<(SsKey * SsKey) list>(zeroCost.EdgesToBreak, weighted.EdgesToBreak)

// ---------------------------------------------------------------------------
// v7 slice 2 — the typed rationale + the refusal certificate (DECISIONS
// 2026-07-18; cash-out of the Reason-DU deferral). The certificate is
// unforgeable: closed cycle + zero Weak edges, by construction.
// ---------------------------------------------------------------------------

[<Fact>]
let ``certificate: a StrongCycleCertificate cannot carry a Weak edge (type theorem, refused at the ctor)`` () =
    let a, b = pKey 0, pKey 1
    let edges = [ ((a, b), EdgeStrength.Other); ((b, a), EdgeStrength.Weak) ]
    match CycleResolution.StrongCycleCertificate.create edges with
    | Error msg -> Assert.Contains("Weak", msg)
    | Ok _ -> Assert.Fail "a certificate carrying a Weak edge must refuse construction"

[<Fact>]
let ``certificate: an open path is not a certificate (the edges must close)`` () =
    let a, b, c = pKey 0, pKey 1, pKey 2
    let edges = [ ((a, b), EdgeStrength.Other); ((b, c), EdgeStrength.Other) ]
    match CycleResolution.StrongCycleCertificate.create edges with
    | Error msg -> Assert.Contains("closed", msg)
    | Ok _ -> Assert.Fail "an open path must refuse construction"

[<Fact>]
let ``refusal carries the certificate: the all-strong 3-cycle's edges and strengths ride the diagnostic`` () =
    let a, b, c = pKey 0, pKey 1, pKey 2
    let edges =
        [ ((a, b), EdgeStrength.Other)
          ((b, c), EdgeStrength.Other)
          ((c, a), EdgeStrength.Cascade) ]
    let step = CycleResolution.defaultStrategy [ a; b; c ] edges
    Assert.Empty step.EdgesToBreak
    match step.Reason with
    | CycleResolution.ResolutionReason.Refused (cert, _) ->
        let certEdges = CycleResolution.StrongCycleCertificate.edges cert
        Assert.Equal(3, certEdges.Length)
        Assert.True(certEdges |> List.forall (fun (_, s) -> s <> EdgeStrength.Weak))
        Assert.Equal<SsKey list>([ a; b; c ] |> List.sort, CycleResolution.StrongCycleCertificate.members cert)
    | other -> Assert.Fail (sprintf "expected the certified refusal, got %A" other)

[<Fact>]
let ``relaxation: the cheapest strong edges are named, and weakening them admits automatic resolution`` () =
    // A strong 2-cycle (Other both ways): the relaxation names ONE edge;
    // reclassifying exactly that edge Weak flips the resolver to resolved.
    let a, b = pKey 0, pKey 1
    let edges = [ ((a, b), EdgeStrength.Other); ((b, a), EdgeStrength.Other) ]
    let step = CycleResolution.defaultStrategy [ a; b ] edges
    match step.Reason with
    | CycleResolution.ResolutionReason.Refused (_, relaxation) ->
        Assert.Equal(1, relaxation.Length)
        let weakened =
            edges
            |> List.map (fun (e, s) -> if List.contains e relaxation then e, EdgeStrength.Weak else e, s)
        let retried = CycleResolution.defaultStrategy [ a; b ] weakened
        Assert.NotEmpty retried.EdgesToBreak
    | other -> Assert.Fail (sprintf "expected the certified refusal, got %A" other)

[<Fact>]
let ``relaxation prefers a nullability change over a delete-rule change: Other relaxes before Cascade`` () =
    // The strong 2-cycle mixes Other and Cascade — the relaxation names
    // the Other edge (cost 1) over the Cascade edge (cost 1,000,000).
    let a, b = pKey 0, pKey 1
    let edges = [ ((a, b), EdgeStrength.Cascade); ((b, a), EdgeStrength.Other) ]
    let step = CycleResolution.defaultStrategy [ a; b ] edges
    match step.Reason with
    | CycleResolution.ResolutionReason.Refused (_, relaxation) ->
        Assert.Equal<(SsKey * SsKey) list>([ (b, a) ], relaxation)
    | other -> Assert.Fail (sprintf "expected the certified refusal, got %A" other)

// ---------------------------------------------------------------------------
// v7 slice 4 — the evidence-weighted family member (DECISIONS 2026-07-18).
// Conservative extension, refusal invariance (the A46 lemma), measured
// minimality, and permutation invariance under weights.
// ---------------------------------------------------------------------------

[<Property>]
let ``conservative extension: the weighted strategy at ZERO evidence is byte-equal to the schema-minimal default`` (raw: (byte * byte * byte) list) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let zero = CycleResolution.minimalFeedbackStrategy (fun _ -> 0L) members edges
    let dflt = CycleResolution.defaultStrategy members edges
    zero = dflt

[<Property>]
let ``A46 lemma — refusal invariance: SchemaMinimal and any weighted member refuse exactly the same SCCs`` (raw: (byte * byte * byte) list) (seed: int) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    // A deterministic pseudo-arbitrary cost from the seed — refusal must
    // not depend on it.
    let cost (e: SsKey * SsKey) : int64 = int64 (abs (hash (e, seed)) % 1000)
    let weighted = CycleResolution.minimalFeedbackStrategy cost members edges
    let schema = CycleResolution.defaultStrategy members edges
    List.isEmpty weighted.EdgesToBreak = List.isEmpty schema.EdgesToBreak

[<Property>]
let ``measured minimality: the weighted break set minimizes Σ cost (then cardinality, then lex) among feasible weak subsets`` (raw: (byte * byte * byte) list) (seed: int) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let cost (e: SsKey * SsKey) : int64 = int64 (abs (hash (e, seed)) % 100)
    let step = CycleResolution.minimalFeedbackStrategy cost members edges
    let allEdges = edges |> List.map fst
    let strongOnly = edges |> List.choose (fun (e, st) -> if st <> EdgeStrength.Weak then Some e else None)
    if isAcyclic allEdges || not (isAcyclic strongOnly) then true
    else
        // Brute-force the weighted optimum independently.
        let weak = edges |> List.choose (fun (e, s) -> if s = EdgeStrength.Weak then Some e else None) |> List.sort
        let strong = strongOnly
        let n = List.length weak
        let weakArr = List.toArray weak
        let mutable best : (int64 * int * (SsKey * SsKey) list) option = None
        for mask in 0 .. (1 <<< n) - 1 do
            let subset = [ for i in 0 .. n - 1 do if mask &&& (1 <<< i) <> 0 then yield weakArr.[i] ]
            let remaining = strong @ (weak |> List.except subset)
            if isAcyclic remaining then
                let cand = (subset |> List.sumBy cost, List.length subset, List.sort subset)
                match best with
                | None -> best <- Some cand
                | Some cur -> if compare cand cur < 0 then best <- Some cand
        match best with
        | Some (_, _, expected) -> step.EdgesToBreak = expected
        | None -> false

[<Property>]
let ``permutation invariance under weights: the weighted break set is input-order independent`` (raw: (byte * byte * byte) list) (seed: int) =
    let edges = edgesFrom raw
    let members = [ 0 .. 3 ] |> List.map pKey
    let cost (e: SsKey * SsKey) : int64 = int64 (abs (hash (e, seed)) % 1000)
    let forward = CycleResolution.minimalFeedbackStrategy cost members edges
    let reversed = CycleResolution.minimalFeedbackStrategy cost (List.rev members) (List.rev edges)
    forward.EdgesToBreak = reversed.EdgesToBreak
