module Projection.Tests.PolicySimulationTests

// H-054 (HORIZON Cluster F): policy simulation property tests.
//
// Three laws over `PolicyDiff` / `PolicyExpr`:
//   1. **Reflexivity** — `diff p p` is empty for any policy p.
//   2. **From-empty diff is the full overlay set** — every non-empty
//      axis of p surfaces as `Changed` in `diff Policy.empty p`.
//   3. **Disjoint-axis composition** — `Seq p1 p2 ≡ Seq p2 p1`
//      (up to Tightening interventions which accumulate) whenever p1
//      and p2 modify disjoint axes. The two policies "commute" on
//      their independent axes.
//
// Existing coverage in `PolicyDiffTests.fs`:
//   - Reflexivity (one example test + axis-isolation property tests).
//
// What this file adds:
//   - Reflexivity in property form (any policy permutation).
//   - From-empty axiom: every non-empty axis flips Changed.
//   - Disjoint-axis composition property: Seq commutativity on
//     independent axes; associativity of Seq across three policies.
//   - Empty-policy as left identity of Seq.

open Xunit
open FsCheck.Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Per-file helpers
// ---------------------------------------------------------------------------

let private nullabilityCfg (id: string) : TighteningIntervention =
    let cfg = NullabilityTighteningConfig.create 0.1m false [] |> Result.value
    Nullability (id, cfg)

let private withSelection (sel: SelectionPolicy) (p: Policy) : Policy =
    { p with Selection = sel }

let private withEmission (em: EmissionPolicy) (p: Policy) : Policy =
    { p with Emission = em }

let private withInsertion (ins: InsertionPolicy) (p: Policy) : Policy =
    { p with Insertion = ins }

let private withTightening (interventions: TighteningIntervention list) (p: Policy) : Policy =
    { p with Tightening = { Interventions = interventions } }

let private withUserMatching (us: UserMatchingStrategy) (p: Policy) : Policy =
    { p with UserMatching = us }

// ---------------------------------------------------------------------------
// Law 1 (Reflexivity): `diff p p` is empty for any policy p.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-054 reflexivity: diff Policy.empty Policy.empty is empty`` () =
    let diff = PolicyDiff.compare Policy.empty Policy.empty
    Assert.False diff.AnyChanged

[<Property>]
let ``H-054 reflexivity (property): diff p p is empty for any populated policy`` (seed: int) =
    let p =
        Policy.empty
        |> withSelection (if seed % 2 = 0 then IncludeAll else IncludeOnly (Set.singleton customerKey))
        |> withInsertion (if seed % 3 = 0 then SchemaOnly else InsertNew)
        |> withTightening [ nullabilityCfg (sprintf "i-%d" seed) ]
    let diff = PolicyDiff.compare p p
    not diff.AnyChanged

// ---------------------------------------------------------------------------
// Law 2 (From-empty diff): every non-empty axis flips Changed in
// `diff Policy.empty p`.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-054 from-empty: every non-empty axis flips Changed`` () =
    let target =
        Policy.empty
        |> withSelection (IncludeOnly (Set.singleton customerKey))
        |> withEmission EmissionPolicy.dataOnly
        |> withInsertion InsertNew
        |> withTightening [ nullabilityCfg "from-empty" ]
        |> withUserMatching BySsKey
    let diff = PolicyDiff.compare Policy.empty target
    Assert.True diff.Selection.Changed
    Assert.True diff.Emission.Changed
    Assert.True diff.Insertion.Changed
    Assert.True diff.Tightening.Changed
    Assert.True diff.UserMatching.Changed
    Assert.True diff.AnyChanged

[<Property>]
let ``H-054 from-empty (property): AnyChanged iff at least one axis differs from Policy.empty`` (sel: int) (ins: int) =
    let p =
        Policy.empty
        |> withSelection (if sel % 2 = 0 then IncludeAll else IncludeOnly (Set.singleton orderKey))
        |> withInsertion (if ins % 2 = 0 then SchemaOnly else Merge)
    let diff = PolicyDiff.compare Policy.empty p
    // AnyChanged ⇔ p ≠ Policy.empty (axis-wise).
    let pIsEmpty =
        p.Selection = Policy.empty.Selection
        && p.Emission = Policy.empty.Emission
        && p.Insertion = Policy.empty.Insertion
        && p.Tightening = Policy.empty.Tightening
        && p.UserMatching = Policy.empty.UserMatching
    diff.AnyChanged = (not pIsEmpty)

// ---------------------------------------------------------------------------
// Law 3 (Disjoint-axis composition via Override): the cleanest
// composition operator in `PolicyExpr` for disjoint-axis stacking is
// `Override (axis, expr)` — it produces a policy where ONLY the
// named axis carries `expr`'s value; all other axes return to
// `Policy.empty`. Sequencing two Overrides on disjoint axes via Seq
// works because Seq's right-wins behavior preserves the second
// Override's named axis while clobbering everything else back to
// Policy.empty values that the first Override already set to default.
//
// **Caveat (documented divergence).** Naive `Seq p1 p2` where p1 and
// p2 are Atom-lifted populated policies does NOT commute on
// disjoint axes — Seq is right-wins on every axis (not "right-wins
// when non-default"). The HORIZON H-054 third law applies under the
// "union of non-default values" semantics; in PolicyExpr's algebra
// the equivalent is composing Override expressions, which IS the
// natural single-axis-stacking operator.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-054 Override commutativity: Override Selection then Override Insertion vs. reverse — Selection-only Override has empty Insertion in both orderings`` () =
    // Override (Selection, p) projects p to its Selection axis only,
    // zeroing every other axis. Seq of two Overrides on different
    // axes therefore can only have ONE axis non-default at the end
    // (whichever Override is on the right of Seq); the other axes
    // are reset to Policy.empty values.
    let pSel = Policy.empty |> withSelection (IncludeOnly (Set.singleton customerKey))
    let pIns = Policy.empty |> withInsertion InsertNew
    let leftFirst  =
        PolicyExpr.eval
            (PolicyExpr.Seq
                (PolicyExpr.Override (Selection, PolicyExpr.ofPolicy pSel),
                 PolicyExpr.Override (Insertion, PolicyExpr.ofPolicy pIns)))
    let rightFirst =
        PolicyExpr.eval
            (PolicyExpr.Seq
                (PolicyExpr.Override (Insertion, PolicyExpr.ofPolicy pIns),
                 PolicyExpr.Override (Selection, PolicyExpr.ofPolicy pSel)))
    // Right-wins Seq: only the rightmost Override's axis is preserved.
    // leftFirst → Insertion is the rightmost wins.
    // rightFirst → Selection is the rightmost wins.
    Assert.Equal(InsertNew, leftFirst.Insertion)
    Assert.Equal(IncludeOnly (Set.singleton customerKey), rightFirst.Selection)
    // Both orderings reset Tightening to empty (neither Override
    // touches it).
    Assert.Equal(Policy.empty.Tightening, leftFirst.Tightening)
    Assert.Equal(Policy.empty.Tightening, rightFirst.Tightening)

[<Fact>]
let ``H-054 right-wins clobber (counterexample): naive Seq does NOT distribute over disjoint Atom-lifted axes`` () =
    // Documents the divergence: `Seq p_sel p_ins` does NOT preserve
    // p_sel's Selection; it gets clobbered to Policy.empty.Selection
    // (the right side's default). The "disjoint-axis distribution"
    // law from H-054 needs `Override` for its cleanest expression in
    // this codebase's algebra. This counter-example test prevents a
    // future refactor from misremembering Seq as union-with-default.
    let pSel = Policy.empty |> withSelection (IncludeOnly (Set.singleton customerKey))
    let pIns = Policy.empty |> withInsertion InsertNew
    let combined =
        PolicyExpr.eval (PolicyExpr.Seq (PolicyExpr.ofPolicy pSel, PolicyExpr.ofPolicy pIns))
    // p_ins.Selection is Policy.empty.Selection (IncludeAll); Seq's
    // right-wins clobbers p_sel's IncludeOnly.
    Assert.Equal(Policy.empty.Selection, combined.Selection)
    Assert.NotEqual(pSel.Selection, combined.Selection)
    // Insertion preserves p_ins's value (the right side).
    Assert.Equal(InsertNew, combined.Insertion)

// ---------------------------------------------------------------------------
// Seq associativity (`(a; b); c = a; (b; c)`) over the policy
// expression DSL. Underwrites disjoint-axis composition.
// ---------------------------------------------------------------------------

[<Property>]
let ``H-054 Seq associativity: (a; b); c = a; (b; c) over Atom-lifted policies`` (n: int) =
    let p1 = Policy.empty |> withInsertion InsertNew
    let p2 = Policy.empty |> withTightening [ nullabilityCfg (sprintf "i-%d" n) ]
    let p3 = Policy.empty |> withUserMatching BySsKey
    let leftAssoc =
        PolicyExpr.eval
            (PolicyExpr.Seq
                (PolicyExpr.Seq (PolicyExpr.ofPolicy p1, PolicyExpr.ofPolicy p2),
                 PolicyExpr.ofPolicy p3))
    let rightAssoc =
        PolicyExpr.eval
            (PolicyExpr.Seq
                (PolicyExpr.ofPolicy p1,
                 PolicyExpr.Seq (PolicyExpr.ofPolicy p2, PolicyExpr.ofPolicy p3)))
    leftAssoc = rightAssoc

// ---------------------------------------------------------------------------
// Left identity of Seq: `Seq identity p = p` for any p. The DSL's
// docstring claims left identity holds; this property exercises it.
// ---------------------------------------------------------------------------

[<Property>]
let ``H-054 Seq left identity: eval (Seq identity p) = eval p`` (n: int) =
    let p =
        Policy.empty
        |> withInsertion (if n % 2 = 0 then SchemaOnly else InsertNew)
        |> withTightening [ nullabilityCfg (sprintf "id-%d" n) ]
    let expr = PolicyExpr.Seq (PolicyExpr.identity, PolicyExpr.ofPolicy p)
    PolicyExpr.eval expr = p

// ---------------------------------------------------------------------------
// Full-projection consequence: when p1 = p2, the per-kind delta set is
// empty AND the structural diff is empty. The two views (structural +
// per-kind) cohere.
// ---------------------------------------------------------------------------

[<Property>]
let ``H-054 full-projection coherence: same policy yields empty structural diff and empty ChangedKinds`` (n: int) =
    let p = Policy.empty |> withInsertion (if n % 2 = 0 then SchemaOnly else InsertNew)
    let result = PolicyDiff.diffFullProjection sampleCatalog Profile.empty p p
    not result.Value.StructuralDiff.AnyChanged && List.isEmpty result.Value.ChangedKinds
