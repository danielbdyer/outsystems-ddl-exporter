module Projection.Tests.PolicyExprTests

// H-016: PolicyExpr typed combinator DSL.
// H-060: Natural transformation property tests — `PolicyExpr.eval` preserves
//        the expression algebra's structural invariants.

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Sample policies used throughout
// ---------------------------------------------------------------------------

let private withIncludeOnly (keys: SsKey list) (p: Policy) : Policy =
    { p with Selection = IncludeOnly (Set.ofList keys) }

let private withExcludeOnly (keys: SsKey list) (p: Policy) : Policy =
    { p with Selection = ExcludeOnly (Set.ofList keys) }

let private withDataOnly (p: Policy) : Policy =
    { p with Emission = EmissionPolicy.dataOnly }

let private withInsertNew (p: Policy) : Policy =
    { p with Insertion = InsertNew }

let private withNullabilityIntervention (id: string) (p: Policy) : Policy =
    let cfg = NullabilityTighteningConfig.create 0.1m false [] |> Result.value
    { p with Tightening = { Interventions = [ Nullability (id, cfg) ] } }

// ---------------------------------------------------------------------------
// H-016: Atom (identity / lift)
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-016: eval (Atom p) = p`` () =
    Assert.Equal(Policy.empty, PolicyExpr.eval (PolicyExpr.Atom Policy.empty))

[<Fact>]
let ``H-016: eval identity = Policy.empty`` () =
    Assert.Equal(Policy.empty, PolicyExpr.eval PolicyExpr.identity)

[<Fact>]
let ``H-016: ofPolicy round-trips through eval`` () =
    let p = Policy.empty |> withInsertNew
    Assert.Equal(p, PolicyExpr.eval (PolicyExpr.ofPolicy p))

// ---------------------------------------------------------------------------
// H-016: Seq composition
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-016: Seq left identity — eval (Seq identity e) = eval e`` () =
    let e = PolicyExpr.ofPolicy (Policy.empty |> withInsertNew)
    let result = PolicyExpr.eval (PolicyExpr.Seq (PolicyExpr.identity, e))
    Assert.Equal(PolicyExpr.eval e, result)

[<Fact>]
let ``H-016: Seq right side wins on Selection`` () =
    let a = PolicyExpr.ofPolicy (Policy.empty |> withIncludeOnly [customerKey])
    let b = PolicyExpr.ofPolicy (Policy.empty |> withIncludeOnly [orderKey])
    let result = PolicyExpr.eval (PolicyExpr.Seq (a, b))
    Assert.Equal(IncludeOnly (Set.singleton orderKey), result.Selection)

[<Fact>]
let ``H-016: Seq accumulates Tightening interventions`` () =
    let a = PolicyExpr.ofPolicy (Policy.empty |> withNullabilityIntervention "int-a")
    let b = PolicyExpr.ofPolicy (Policy.empty |> withNullabilityIntervention "int-b")
    let result = PolicyExpr.eval (PolicyExpr.Seq (a, b))
    Assert.Equal(2, result.Tightening.Interventions.Length)

[<Fact>]
let ``H-016: Seq Tightening is ordered left-then-right`` () =
    let a = PolicyExpr.ofPolicy (Policy.empty |> withNullabilityIntervention "first")
    let b = PolicyExpr.ofPolicy (Policy.empty |> withNullabilityIntervention "second")
    let result = PolicyExpr.eval (PolicyExpr.Seq (a, b))
    let ids = result.Tightening.Interventions |> List.map TighteningIntervention.id
    Assert.Equal<string list>(["first"; "second"], ids)

// ---------------------------------------------------------------------------
// H-016: And (selection intersection)
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-016: And intersects IncludeOnly sets`` () =
    let a = PolicyExpr.ofPolicy (Policy.empty |> withIncludeOnly [customerKey; orderKey])
    let b = PolicyExpr.ofPolicy (Policy.empty |> withIncludeOnly [orderKey])
    let result = PolicyExpr.eval (PolicyExpr.And (a, b))
    Assert.Equal(IncludeOnly (Set.singleton orderKey), result.Selection)

[<Fact>]
let ``H-016: And with IncludeAll is identity for the other side`` () =
    let a = PolicyExpr.Atom Policy.empty  // IncludeAll
    let b = PolicyExpr.ofPolicy (Policy.empty |> withIncludeOnly [customerKey])
    let result = PolicyExpr.eval (PolicyExpr.And (a, b))
    Assert.Equal(IncludeOnly (Set.singleton customerKey), result.Selection)

[<Fact>]
let ``H-016: And ExcludeOnly ∩ ExcludeOnly = ExcludeOnly union`` () =
    let a = PolicyExpr.ofPolicy (Policy.empty |> withExcludeOnly [customerKey])
    let b = PolicyExpr.ofPolicy (Policy.empty |> withExcludeOnly [orderKey])
    let result = PolicyExpr.eval (PolicyExpr.And (a, b))
    Assert.Equal(ExcludeOnly (Set.ofList [customerKey; orderKey]), result.Selection)

// ---------------------------------------------------------------------------
// H-016: Or (selection union)
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-016: Or unions IncludeOnly sets`` () =
    let a = PolicyExpr.ofPolicy (Policy.empty |> withIncludeOnly [customerKey])
    let b = PolicyExpr.ofPolicy (Policy.empty |> withIncludeOnly [orderKey])
    let result = PolicyExpr.eval (PolicyExpr.Or (a, b))
    Assert.Equal(IncludeOnly (Set.ofList [customerKey; orderKey]), result.Selection)

[<Fact>]
let ``H-016: Or with IncludeAll produces IncludeAll`` () =
    let a = PolicyExpr.Atom Policy.empty  // IncludeAll
    let b = PolicyExpr.ofPolicy (Policy.empty |> withIncludeOnly [customerKey])
    let result = PolicyExpr.eval (PolicyExpr.Or (a, b))
    Assert.Equal(IncludeAll, result.Selection)

[<Fact>]
let ``H-016: Or ExcludeOnly ∪ ExcludeOnly = ExcludeOnly intersection`` () =
    let a = PolicyExpr.ofPolicy (Policy.empty |> withExcludeOnly [customerKey; orderKey])
    let b = PolicyExpr.ofPolicy (Policy.empty |> withExcludeOnly [orderKey])
    let result = PolicyExpr.eval (PolicyExpr.Or (a, b))
    Assert.Equal(ExcludeOnly (Set.singleton orderKey), result.Selection)

// ---------------------------------------------------------------------------
// H-016: Override (axis extraction)
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-016: Override Selection extracts only Selection axis`` () =
    let p = Policy.empty |> withIncludeOnly [customerKey] |> withInsertNew
    let expr = PolicyExpr.Override (Selection, PolicyExpr.ofPolicy p)
    let result = PolicyExpr.eval expr
    Assert.Equal(IncludeOnly (Set.singleton customerKey), result.Selection)
    Assert.Equal(Policy.empty.Insertion, result.Insertion)  // other axes at empty

[<Fact>]
let ``H-016: Override Emission extracts only Emission axis`` () =
    let p = Policy.empty |> withDataOnly |> withInsertNew
    let expr = PolicyExpr.Override (Emission, PolicyExpr.ofPolicy p)
    let result = PolicyExpr.eval expr
    Assert.Equal(EmissionPolicy.dataOnly, result.Emission)
    Assert.Equal(Policy.empty.Insertion, result.Insertion)

[<Fact>]
let ``H-016: Override Ordering produces Policy.empty`` () =
    let expr = PolicyExpr.Override (Ordering, PolicyExpr.ofPolicy (Policy.empty |> withInsertNew))
    Assert.Equal(Policy.empty, PolicyExpr.eval expr)

// ---------------------------------------------------------------------------
// H-016: simplify
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-016: simplify removes leading identity in Seq`` () =
    let e = PolicyExpr.ofPolicy (Policy.empty |> withInsertNew)
    let simplified = PolicyExpr.simplify (PolicyExpr.Seq (PolicyExpr.identity, e))
    Assert.Equal(e, simplified)

[<Fact>]
let ``H-016: simplify does NOT remove trailing identity — right-wins means Seq(e, identity) ≠ e`` () =
    // Seq uses right-wins on all axes, so Seq(e, identity) overwrites
    // e's non-empty axes with Policy.empty's values. simplify correctly
    // preserves the trailing-identity form rather than eliding it.
    let e = PolicyExpr.ofPolicy (Policy.empty |> withInsertNew)
    let expr = PolicyExpr.Seq (e, PolicyExpr.identity)
    let simplified = PolicyExpr.simplify expr
    // Semantics preserved (both evaluate to Policy.empty due to right-wins)
    Assert.Equal(PolicyExpr.eval expr, PolicyExpr.eval simplified)

[<Fact>]
let ``H-016: simplify is semantics-preserving`` () =
    let e = PolicyExpr.Seq (PolicyExpr.identity, PolicyExpr.ofPolicy (Policy.empty |> withInsertNew))
    Assert.Equal(PolicyExpr.eval e, PolicyExpr.eval (PolicyExpr.simplify e))

// ---------------------------------------------------------------------------
// H-016: diff
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-016: diff equal expressions returns identity`` () =
    let e = PolicyExpr.ofPolicy (Policy.empty |> withInsertNew)
    let result = PolicyExpr.diff e e
    Assert.Equal(PolicyExpr.identity, result)

[<Fact>]
let ``H-016: diff unequal expressions evaluates to the after policy`` () =
    let a = PolicyExpr.Atom Policy.empty
    let b = PolicyExpr.ofPolicy (Policy.empty |> withInsertNew)
    let result = PolicyExpr.diff a b
    Assert.Equal(PolicyExpr.eval b, PolicyExpr.eval result)

// ---------------------------------------------------------------------------
// H-060: Natural transformation property tests.
// Verify that `PolicyExpr.eval` preserves the expression algebra's structure.
// ---------------------------------------------------------------------------

/// `eval identity = Policy.empty` — the unit law.
[<Fact>]
let ``H-060: eval identity = Policy.empty`` () =
    Assert.Equal(Policy.empty, PolicyExpr.eval PolicyExpr.identity)

/// `eval (Seq identity e) = eval e` — left identity homomorphism.
[<Fact>]
let ``H-060: eval preserves Seq left identity`` () =
    let policies =
        [ Policy.empty
          Policy.empty |> withInsertNew
          Policy.empty |> withIncludeOnly [customerKey]
          Policy.empty |> withDataOnly ]
    for p in policies do
        let e = PolicyExpr.ofPolicy p
        let lhs = PolicyExpr.eval (PolicyExpr.Seq (PolicyExpr.identity, e))
        let rhs = PolicyExpr.eval e
        Assert.Equal(rhs, lhs)

/// `eval` is a homomorphism for And on the Selection lattice:
/// `(eval (And a b)).Selection = intersect (eval a).Selection (eval b).Selection`.
[<Fact>]
let ``H-060: eval And is a Selection-lattice homomorphism`` () =
    let cases =
        [ IncludeAll,                        IncludeAll
          IncludeAll,                        IncludeOnly (Set.singleton customerKey)
          IncludeOnly (Set.singleton customerKey), IncludeOnly (Set.ofList [customerKey; orderKey])
          ExcludeOnly (Set.singleton customerKey), ExcludeOnly (Set.singleton orderKey) ]
    for (s1, s2) in cases do
        let a = PolicyExpr.ofPolicy { Policy.empty with Selection = s1 }
        let b = PolicyExpr.ofPolicy { Policy.empty with Selection = s2 }
        let combined = PolicyExpr.eval (PolicyExpr.And (a, b))
        // Both original selections must be satisfied by the intersection
        let keys = [customerKey; orderKey]
        for key in keys do
            let inA  = SelectionPolicy.isSelected key s1
            let inB  = SelectionPolicy.isSelected key s2
            let inAB = SelectionPolicy.isSelected key combined.Selection
            // Intersection: a kind is in And iff it's in both a and b
            Assert.Equal(inA && inB, inAB)

/// `eval` is a homomorphism for Or on the Selection lattice:
/// `(eval (Or a b)).Selection = union (eval a).Selection (eval b).Selection`.
[<Fact>]
let ``H-060: eval Or is a Selection-lattice homomorphism`` () =
    let cases =
        [ IncludeOnly (Set.singleton customerKey), IncludeOnly (Set.singleton orderKey)
          ExcludeOnly (Set.singleton customerKey), ExcludeOnly (Set.ofList [customerKey; orderKey])
          IncludeAll,                        IncludeOnly (Set.singleton customerKey) ]
    for (s1, s2) in cases do
        let a = PolicyExpr.ofPolicy { Policy.empty with Selection = s1 }
        let b = PolicyExpr.ofPolicy { Policy.empty with Selection = s2 }
        let combined = PolicyExpr.eval (PolicyExpr.Or (a, b))
        let keys = [customerKey; orderKey]
        for key in keys do
            let inA  = SelectionPolicy.isSelected key s1
            let inB  = SelectionPolicy.isSelected key s2
            let inAB = SelectionPolicy.isSelected key combined.Selection
            // Union: a kind is in Or iff it's in either a or b
            Assert.Equal(inA || inB, inAB)

/// `simplify` is semantics-preserving: `eval (simplify e) = eval e`.
/// Tests a left-identity chain with a nested Seq (the case simplify can
/// actually elide). The trailing identity is intentionally absent here
/// because `Seq(e, identity)` is NOT simplified (right-wins means
/// `eval (Seq e identity) ≠ eval e` in general).
[<Property>]
let ``H-060: simplify preserves eval semantics`` (useInsertNew: bool) =
    let inner = if useInsertNew then Policy.empty |> withInsertNew else Policy.empty
    let e = PolicyExpr.Seq (PolicyExpr.identity, PolicyExpr.Seq (PolicyExpr.identity, PolicyExpr.ofPolicy inner))
    PolicyExpr.eval e = PolicyExpr.eval (PolicyExpr.simplify e)
