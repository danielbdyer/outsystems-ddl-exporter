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
let private aBRef = { Reference.create (mkKey ["A"; "BRef"]) (mkName "BRef") (mkKey ["A"; "B_ID"]) bKey with HasDbConstraint = true }
let private bARef = { Reference.create (mkKey ["B"; "ARef"]) (mkName "ARef") (mkKey ["B"; "A_ID"]) aKey with HasDbConstraint = true }
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
      Cycles       = [ { Members = [ aKey; bKey ]; BreakableEdges = [ (aKey, bKey) ]; Reason = "test 2-cycle" } ]
      Diagnostics  = [] }

let private rowOf (ident: string) (values: (string * string) list) : StaticRow =
    { Identifier = mkKey [ident]
      Values     = values |> List.map (fun (k, v) -> mkName k, v) |> Map.ofList }

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
let ``DataLoadPlan: non-nullable same-cycle FK surfaces as an unbreakable diagnostic`` () =
    let plan = build Map.empty
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
let ``DataLoadPlan: reclassifyReconciled overrides only the named kinds to ReconciledByRule`` () =
    let plan = build Map.empty |> DataLoadPlan.reclassifyReconciled (Set.singleton customerKey)
    Assert.Equal(IdentityDisposition.ReconciledByRule, (loadFor customerKey plan).Disposition)
    // ofKind-derived dispositions on the other kinds are untouched.
    Assert.Equal(IdentityDisposition.AssignedBySink, (loadFor invoiceKey plan).Disposition)
    Assert.Equal(IdentityDisposition.PreservedFromSource, (loadFor aKey plan).Disposition)
