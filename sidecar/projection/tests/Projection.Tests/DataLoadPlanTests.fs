module Projection.Tests.DataLoadPlanTests

open Xunit
open Projection.Core

// Pure (DB-free) tests for the Slice-B Transfer plan: identity-aware,
// topologically ordered, deferred-FK-selecting, with non-deferrable cycle
// FKs surfaced as diagnostics. `DataLoadPlan.build` takes a precomputed
// `TopologicalOrder`, so these fixtures construct one directly — no pass,
// fully deterministic.

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_TEST_PLAN" parts |> mustOk
let private mkName (s: string) : Name = Name.create s |> mustOk

// --- attribute / kind builders -------------------------------------------

let private pk (ownerParts: string list) (col: string) (isIdentity: bool) : Attribute =
    { Attribute.create (mkKey (ownerParts @ [col])) (mkName col) Integer with
        Column       = ColumnRealization.create col false |> Result.value
        IsPrimaryKey = true
        IsIdentity   = isIdentity
        IsMandatory  = true }

let private fkAttr (ownerParts: string list) (col: string) (nullable: bool) : Attribute =
    { Attribute.create (mkKey (ownerParts @ [col])) (mkName col) Integer with
        Column      = ColumnRealization.create col nullable |> Result.value
        IsMandatory = not nullable }

let private kindOf (parts: string list) (table: string) (attrs: Attribute list) (refs: Reference list) : Kind =
    { Kind.create (mkKey parts) (mkName (List.last parts))
        (TableId.create "dbo" table |> mustOk)
        attrs
      with References = refs; Indexes = []; ColumnChecks = [] }

// --- fixture: Customer (business PK), Invoice (identity PK), A<->B cycle ---

let private customerKey = mkKey ["Customer"]
let private invoiceKey  = mkKey ["Invoice"]
let private aKey        = mkKey ["A"]
let private bKey        = mkKey ["B"]

let private customer = kindOf ["Customer"] "OSUSR_CUSTOMER" [ pk ["Customer"] "ID" false ] []
let private invoice  = kindOf ["Invoice"]  "OSUSR_INVOICE"  [ pk ["Invoice"] "ID" true ] []

// A has a NULLABLE FK to B (deferrable); B has a NON-NULLABLE FK to A
// (cannot defer → diagnostic). Together a 2-cycle.
let private aBRef = { Reference.create (mkKey ["A"; "BRef"]) (mkName "BRef") (mkKey ["A"; "B_ID"]) bKey with ConstraintState = ConstraintState.TrustedConstraint }
let private bARef = { Reference.create (mkKey ["B"; "ARef"]) (mkName "ARef") (mkKey ["B"; "A_ID"]) aKey with ConstraintState = ConstraintState.TrustedConstraint }
let private kindA = kindOf ["A"] "OSUSR_A" [ pk ["A"] "ID" false; fkAttr ["A"] "B_ID" true ]  [ aBRef ]
let private kindB = kindOf ["B"] "OSUSR_B" [ pk ["B"] "ID" false; fkAttr ["B"] "A_ID" false ] [ bARef ]

let private catalog : Catalog =
    IRBuilders.mkCatalog [ IRBuilders.mkModule (mkKey ["Module"]) (mkName "M") [ customer; invoice; kindA; kindB ] ]

/// Topological order constructed directly: linear [Customer; Invoice; A; B]
/// with the A<->B 2-cycle surfaced in `Cycles` (so `cycleMembers` = {A,B}).
let private topo : TopologicalOrder =
    { Mode         = OrderingMode.Topological
      Order        = [ customerKey; invoiceKey; aKey; bKey ]
      Edges        = [ (aKey, bKey); (bKey, aKey) ]
      MissingEdges = []
      Cycles       = [ CycleDiagnostic.Resolved ([ aKey; bKey ], [ (aKey, bKey) ], CycleResolution.BreakObjective.GreedyWalk) ]
      Diagnostics  = [] }

let private rowOf (ident: string) (values: (string * string) list) : StaticRow =
    { Identifier = mkKey [ident]
      Values     = values |> List.map (fun (k, v) -> mkName k, Some v) |> Map.ofList }

let private build (rows: Map<SsKey, StaticRow list>) =
    DataLoadPlan.build catalog topo rows SurrogateRemapContext.empty

let private loadFor (key: SsKey) (plan: DataLoadPlan) : DataLoadKind =
    plan.Loads |> List.find (fun l -> l.Kind = key)

[<Fact>]
let ``DataLoadPlan: Loads follow the topological order`` () =
    let plan = build Map.empty
    Assert.Equal<SsKey list>([ customerKey; invoiceKey; aKey; bKey ], plan.Loads |> List.map (fun l -> l.Kind))

[<Fact>]
let ``DataLoadPlan: identity PK is AssignedBySink, business PK is PreservedFromSource`` () =
    let plan = build Map.empty
    Assert.Equal(IdentityDisposition.PreservedFromSource, (loadFor customerKey plan).Disposition)
    Assert.Equal(IdentityDisposition.AssignedBySink, (loadFor invoiceKey plan).Disposition)

// --- Slice C1 — the FullRights disposition fork (buildWith PreferPreservedKeys) ---

[<Fact>]
let ``Slice C1: buildWith Structural = build — the byte-identical default`` () =
    let structural = DataLoadPlan.buildWith IdentityPolicy.Structural catalog topo Map.empty SurrogateRemapContext.empty
    Assert.Equal<DataLoadPlan>(build Map.empty, structural)

[<Fact>]
let ``Slice C1: buildWith PreferPreservedKeys flips an IDENTITY PK to PreservedFromSource — no AssignedBySink kind remains (capture/remap entirely skipped)`` () =
    let plan = DataLoadPlan.buildWith IdentityPolicy.PreferPreservedKeys catalog topo Map.empty SurrogateRemapContext.empty
    // The IDENTITY-PK Invoice is now written with its source key preserved
    // (Bulk.copyRows + KeepIdentity — viable on a FullRights sink), not minted.
    Assert.Equal(IdentityDisposition.PreservedFromSource, (loadFor invoiceKey plan).Disposition)
    // The business-PK Customer was already PreservedFromSource (unchanged).
    Assert.Equal(IdentityDisposition.PreservedFromSource, (loadFor customerKey plan).Disposition)
    // The KEY consequence: zero AssignedBySink kinds ⇒ the whole capture +
    // surrogate-remap + FK-repoint machinery (which branches on AssignedBySink)
    // is skipped downstream by construction — the dramatically simpler load.
    Assert.True(plan.Loads |> List.forall (fun l -> l.Disposition <> IdentityDisposition.AssignedBySink))

[<Fact>]
let ``DataLoadPlan: nullable same-cycle FK is deferred, non-nullable is not`` () =
    let plan = build Map.empty
    Assert.Equal<Set<Name>>(Set.singleton (mkName "B_ID"), (loadFor aKey plan).DeferredFkColumns)
    Assert.True(Set.isEmpty (loadFor bKey plan).DeferredFkColumns)

[<Fact>]
let ``DataLoadPlan: a RESOLVED cycle's non-nullable FK is satisfied by the proven order — no unbreakable diagnostic, plan satisfiable`` () =
    // The fixture topo's A<->B SCC is RESOLVED (BreakableEdges = [(A,B)]):
    // the resolver broke A's weak (nullable, deferred) edge, and the order
    // [.. A; B] honors B's strong edge — B loads after A, so B.A_ID is
    // satisfied by ORDER, not deferral. The pre-2026-07-07 computation
    // judged ALL cycle members and flagged B.A_ID unbreakable, refusing a
    // load the proven order satisfies (the resolver-completeness catch).
    let plan = build Map.empty
    Assert.True(DataLoadPlan.isSatisfiable plan)
    Assert.Empty(plan.UnbreakableCycleFks)

/// The same catalog under an UNRESOLVED A<->B cycle (BreakableEdges = [] —
/// the resolved/unresolved discriminant): the alphabetical fallback cannot
/// prove B's strong edge, so the non-nullable FK is genuinely unbreakable.
let private unresolvedTopo : TopologicalOrder =
    { topo with
        Mode   = OrderingMode.Alphabetical
        Cycles = [ CycleDiagnostic.Anomalous ([ aKey; bKey ], "test unresolved 2-cycle") ] }

[<Fact>]
let ``DataLoadPlan: an UNRESOLVED cycle's non-nullable FK surfaces as the unbreakable diagnostic (the nullable side does not)`` () =
    let plan = DataLoadPlan.build catalog unresolvedTopo Map.empty SurrogateRemapContext.empty
    Assert.False(DataLoadPlan.isSatisfiable plan)
    Assert.Contains(
        plan.UnbreakableCycleFks,
        fun (u: UnbreakableCycleFk) -> u.Kind = bKey && u.Column = mkName "A_ID" && u.Target = aKey)
    // The deferrable side does not produce a diagnostic.
    Assert.DoesNotContain(plan.UnbreakableCycleFks, fun (u: UnbreakableCycleFk) -> u.Kind = aKey)

[<Fact>]
let ``DataLoadPlan: ingested rows attach by kind; un-ingested kinds are empty`` () =
    let rows = Map.ofList [ customerKey, [ rowOf "c1" [ "ID", "1" ]; rowOf "c2" [ "ID", "2" ] ] ]
    let plan = build rows
    Assert.Equal(2, (loadFor customerKey plan).Rows.Length)
    Assert.Empty((loadFor invoiceKey plan).Rows)

[<Fact>]
let ``DataLoadPlan: deferredLoads lists only the cycle-broken kinds`` () =
    let plan = build Map.empty
    Assert.Equal<SsKey list>([ aKey ], DataLoadPlan.deferredLoads plan |> List.map (fun l -> l.Kind))

[<Fact>]
let ``DataLoadPlan.DroppedRows: a row whose FK targets an unmatched remap identity carries its FULL row + the failed reference`` () =
    // A child kind with a mandatory FK to a REMAPPED parent (Customer).
    // The remap covers source customer "1" but not "2"; the child row
    // pointing at "2" drops — and `DroppedRows` must carry that row whole,
    // paired with the reference that failed it.
    let childKey = mkKey ["Child"]
    let childRef = { Reference.create (mkKey ["Child"; "CustRef"]) (mkName "CustId") (mkKey ["Child"; "CUSTID"]) customerKey with ConstraintState = ConstraintState.TrustedConstraint }
    let child = kindOf ["Child"] "OSUSR_CHILD" [ pk ["Child"] "ID" false; fkAttr ["Child"] "CUSTID" false ] [ childRef ]
    let localCatalog = IRBuilders.mkCatalog [ IRBuilders.mkModule (mkKey ["M2"]) (mkName "M2") [ customer; child ] ]
    let localTopo : TopologicalOrder =
        { Mode = OrderingMode.Topological; Order = [ customerKey; childKey ]; Edges = [ (childKey, customerKey) ]; MissingEdges = []; Cycles = []; Diagnostics = [] }
    // Remap: source customer surrogate "1" → assigned "100" (but NOT "2").
    let remap = SurrogateRemapContext.capture customerKey (SourceKey.ofString "1") (AssignedKey.ofString "100") SurrogateRemapContext.empty |> mustOk
    let rows =
        Map.ofList
            [ childKey, [ rowOf "k1" [ "ID", "1"; "CUSTID", "1" ]   // kept — parent "1" is in the remap
                          rowOf "k2" [ "ID", "2"; "CUSTID", "2" ] ] ] // dropped — parent "2" is unmatched
    let plan = DataLoadPlan.build localCatalog localTopo rows remap
    // One skip, and its full row is carried.
    let (dropKind, uref, row) = List.exactlyOne plan.DroppedRows
    Assert.Equal(childKey, dropKind)
    Assert.Equal("CUSTID", Name.value uref.Column)
    Assert.Equal("2", SourceKey.value uref.UnresolvedSource)
    Assert.Equal(Some "2", row.Values.[mkName "ID"])   // the k2 row, whole
    // The kept row survived, re-pointed to the assigned surrogate.
    Assert.Equal(1, (plan.Loads |> List.find (fun l -> l.Kind = childKey)).Rows.Length)

[<Fact>]
let ``DataLoadPlan: reclassifyReconciled overrides only the named kinds to ReconciledByRule`` () =
    let plan = build Map.empty |> DataLoadPlan.reclassifyReconciled (Set.singleton customerKey)
    Assert.Equal(IdentityDisposition.ReconciledByRule, (loadFor customerKey plan).Disposition)
    // ofKind-derived dispositions on the other kinds are untouched.
    Assert.Equal(IdentityDisposition.AssignedBySink, (loadFor invoiceKey plan).Disposition)
    Assert.Equal(IdentityDisposition.PreservedFromSource, (loadFor aKey plan).Disposition)

// ---------------------------------------------------------------------------
// Acquisition-overlap factorization law — the plan factors per kind: no
// load field depends on another kind's rows (order, cycle membership, and
// the remap are fixed before acquisition), so `loadForWith` (the per-kind
// unit) reproduces every batch-built load. This is what lets an overlapped
// realization construct a kind's load the moment its rows land.
// ---------------------------------------------------------------------------

[<Fact>]
let ``DataLoadPlan.loadForWith ≡ buildWith per kind: the per-kind unit reproduces every batch load (both policies)`` () =
    let rows =
        Map.ofList
            [ customerKey, [ rowOf "c1" [ "ID", "1" ]; rowOf "c2" [ "ID", "2" ] ]
              invoiceKey,  [ rowOf "i1" [ "ID", "10" ] ]
              aKey,        [ rowOf "a1" [ "ID", "1"; "B_ID", "2" ] ]
              bKey,        [ rowOf "b1" [ "ID", "2"; "A_ID", "1" ] ] ]
    let members = TopologicalOrder.cycleMembers topo
    for policy in [ IdentityPolicy.Structural; IdentityPolicy.PreferPreservedKeys ] do
        let plan = DataLoadPlan.buildWith policy catalog topo rows SurrogateRemapContext.empty
        for load in plan.Loads do
            let kind = Catalog.tryFindKind load.Kind catalog |> Option.get
            let raw  = Map.tryFind load.Kind rows |> Option.defaultValue []
            let perKind, skipped =
                DataLoadPlan.loadForWith policy members SurrogateRemapContext.empty kind raw
            Assert.Equal<DataLoadKind>(load, perKind)
            // Empty remap ⇒ no skipped references on either path.
            Assert.Empty skipped
    // And the plan's aggregate skip list is the concatenation of the
    // per-kind skips — empty here on both sides.
    let plan = build rows
    Assert.Empty plan.SkippedReferences

[<Fact>]
let ``DataLoadPlan.loadFor: the Structural per-kind default mirrors build (deferred columns included)`` () =
    let plan = build Map.empty
    let members = TopologicalOrder.cycleMembers topo
    for load in plan.Loads do
        let kind = Catalog.tryFindKind load.Kind catalog |> Option.get
        let perKind, _ = DataLoadPlan.loadFor members SurrogateRemapContext.empty kind []
        Assert.Equal<DataLoadKind>(load, perKind)
