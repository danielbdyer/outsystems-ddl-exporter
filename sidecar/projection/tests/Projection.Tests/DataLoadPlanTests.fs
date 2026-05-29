module Projection.Tests.TransferPlanTests

open Xunit
open Projection.Core

// Pure (DB-free) tests for the Slice-B Transfer plan: identity-aware,
// topologically ordered, deferred-FK-selecting, with non-deferrable cycle
// FKs surfaced as diagnostics. `TransferPlan.build` takes a precomputed
// `TopologicalOrder`, so these fixtures construct one directly — no pass,
// fully deterministic.

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_TEST_PLAN" parts |> mustOk
let private mkName (s: string) : Name = Name.create s |> mustOk

// --- attribute / kind builders -------------------------------------------

let private pk (ownerParts: string list) (col: string) (isIdentity: bool) : Attribute =
    { Attribute.create (mkKey (ownerParts @ [col])) (mkName col) Integer with
        Column       = { ColumnName = col; IsNullable = false }
        IsPrimaryKey = true
        IsIdentity   = isIdentity
        IsMandatory  = true }

let private fkAttr (ownerParts: string list) (col: string) (nullable: bool) : Attribute =
    { Attribute.create (mkKey (ownerParts @ [col])) (mkName col) Integer with
        Column      = { ColumnName = col; IsNullable = nullable }
        IsMandatory = not nullable }

let private kindOf (parts: string list) (table: string) (attrs: Attribute list) (refs: Reference list) : Kind =
    { Kind.create (mkKey parts) (mkName (List.last parts))
        { Schema = "dbo"; Table = table; Catalog = None }
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

let private build (rows: Map<SsKey, StaticRow list>) = TransferPlan.build catalog topo rows

let private loadFor (key: SsKey) (plan: TransferPlan) : TransferKindLoad =
    plan.Loads |> List.find (fun l -> l.Kind = key)

[<Fact>]
let ``TransferPlan: Loads follow the topological order`` () =
    let plan = build Map.empty
    Assert.Equal<SsKey list>([ customerKey; invoiceKey; aKey; bKey ], plan.Loads |> List.map (fun l -> l.Kind))

[<Fact>]
let ``TransferPlan: identity PK is AssignedBySink, business PK is PreservedFromSource`` () =
    let plan = build Map.empty
    Assert.Equal(IdentityDisposition.PreservedFromSource, (loadFor customerKey plan).Disposition)
    Assert.Equal(IdentityDisposition.AssignedBySink, (loadFor invoiceKey plan).Disposition)

[<Fact>]
let ``TransferPlan: nullable same-cycle FK is deferred, non-nullable is not`` () =
    let plan = build Map.empty
    Assert.Equal<Set<Name>>(Set.singleton (mkName "B_ID"), (loadFor aKey plan).DeferredFkColumns)
    Assert.True(Set.isEmpty (loadFor bKey plan).DeferredFkColumns)

[<Fact>]
let ``TransferPlan: non-nullable same-cycle FK surfaces as an unbreakable diagnostic`` () =
    let plan = build Map.empty
    Assert.False(TransferPlan.isSatisfiable plan)
    Assert.Contains(
        plan.UnbreakableCycleFks,
        fun (u: UnbreakableCycleFk) -> u.Kind = bKey && u.Column = mkName "A_ID" && u.Target = aKey)
    // The deferrable side does not produce a diagnostic.
    Assert.DoesNotContain(plan.UnbreakableCycleFks, fun (u: UnbreakableCycleFk) -> u.Kind = aKey)

[<Fact>]
let ``TransferPlan: ingested rows attach by kind; un-ingested kinds are empty`` () =
    let rows = Map.ofList [ customerKey, [ rowOf "c1" [ "ID", "1" ]; rowOf "c2" [ "ID", "2" ] ] ]
    let plan = build rows
    Assert.Equal(2, (loadFor customerKey plan).Rows.Length)
    Assert.Empty((loadFor invoiceKey plan).Rows)

[<Fact>]
let ``TransferPlan: deferredLoads lists only the cycle-broken kinds`` () =
    let plan = build Map.empty
    Assert.Equal<SsKey list>([ aKey ], TransferPlan.deferredLoads plan |> List.map (fun l -> l.Kind))

[<Fact>]
let ``TransferPlan: reclassifyReconciled overrides only the named kinds to ReconciledByRule`` () =
    let plan = build Map.empty |> TransferPlan.reclassifyReconciled (Set.singleton customerKey)
    Assert.Equal(IdentityDisposition.ReconciledByRule, (loadFor customerKey plan).Disposition)
    // ofKind-derived dispositions on the other kinds are untouched.
    Assert.Equal(IdentityDisposition.AssignedBySink, (loadFor invoiceKey plan).Disposition)
    Assert.Equal(IdentityDisposition.PreservedFromSource, (loadFor aKey plan).Disposition)
