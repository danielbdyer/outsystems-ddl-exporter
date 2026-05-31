module Projection.Tests.PassChainAdapterComposeTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Chapter A.4.7' slice γ — PassChainAdapter.compose traversal kernel
// witnesses + invariants.
//
// Slice γ ships:
//   - PassChainAdapter.compose : adapters → state →
//     Lineage<Diagnostics<ComposeState>>. Folds the list via
//     LineageDiagnostics.bind; both writer trails compose
//     chronologically (A24).
//
// Slice γ does NOT touch Compose.project (slice δ) or
// runWithSkeleton (slice ε). The kernel is testable in isolation —
// the adapter list is the input; the composed Apply is the output.
// ---------------------------------------------------------------------------

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> failwithf "expected Ok but got Error: %A" es

let private emptyCatalog : Catalog = Catalog.create [] [] |> mustOk

// Sales module (Customer + Order + Country) from `Fixtures.fs`
// exercises kinds with a directional reference (Order → Customer)
// so SymmetricClosure / TopologicalOrderPass / ForeignKeyPass have
// non-trivial input.
let private exerciseCatalog : Catalog = sampleCatalog

let private fixtureEvent : LineageEvent =
    { PassName = "SliceGammaWitness"
      PassVersion = 1
      SsKey = testKey "gammaWitness"
      TransformKind = Touched
      Classification = DataIntent }

let private tellOnly : PassChainAdapter =
    { Name = "SliceGammaTellOnly"
      Apply =
        fun state ->
            LineageDiagnostics.ofValue state
            |> LineageDiagnostics.tellLineage fixtureEvent }

// ---------------------------------------------------------------------------
// Base case: empty adapter list returns ofValue state.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7' slice γ: compose on empty adapter list yields ofValue state with empty trails`` () =
    let state = ComposeState.initial exerciseCatalog
    let result = PassChainAdapter.compose [] state
    Assert.Equal(exerciseCatalog, (LineageDiagnostics.payload result).Catalog)
    Assert.Empty(result.Trail)
    Assert.Empty(LineageDiagnostics.entries result)

// ---------------------------------------------------------------------------
// A24-equivalent: trail order is earliest-first under compose.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A24 (compose): Lineage trail concatenates in adapter-list order`` () =
    let e1 = { fixtureEvent with PassName = "AdapterAlpha" }
    let e2 = { fixtureEvent with PassName = "AdapterBeta" }
    let e3 = { fixtureEvent with PassName = "AdapterGamma" }
    let mk name event : PassChainAdapter =
        { Name = name
          Apply =
            fun state ->
                LineageDiagnostics.ofValue state
                |> LineageDiagnostics.tellLineage event }
    let result =
        PassChainAdapter.compose
            [ mk "AdapterAlpha" e1; mk "AdapterBeta" e2; mk "AdapterGamma" e3 ]
            (ComposeState.initial emptyCatalog)
    Assert.Equal<LineageEvent list>([ e1; e2; e3 ], result.Trail)

// ---------------------------------------------------------------------------
// RegisteredTransforms.allChainSteps is executable end-to-end on a
// representative Catalog: every decision-set Option field becomes
// `Some _`, witnessing that each pass fires through the slice-α
// adapter without type-system or shape mismatch.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7' slice γ: compose RegisteredTransforms.allChainSteps populates every decision-set field`` () =
    let result =
        PassChainAdapter.compose
            RegisteredTransforms.allChainSteps
            (ComposeState.initial exerciseCatalog)
    let final = LineageDiagnostics.payload result
    Assert.True(final.TopologicalOrder.IsSome, "TopologicalOrder must be populated after chain run")
    Assert.True(final.NullabilityDecisions.IsSome, "NullabilityDecisions must be populated")
    Assert.True(final.UniqueIndexDecisions.IsSome, "UniqueIndexDecisions must be populated")
    Assert.True(final.ForeignKeyDecisions.IsSome, "ForeignKeyDecisions must be populated")
    Assert.True(final.CategoricalUniquenessDecisions.IsSome, "CategoricalUniquenessDecisions must be populated")
    Assert.True(final.UserRemap.IsSome, "UserRemap must be populated")

// ---------------------------------------------------------------------------
// T1 (compose): same input → byte-identical output (referential trail
// + value equality). Operates on the populated allChainSteps to cover
// the production-shape fold.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1 (compose): RegisteredTransforms.allChainSteps is deterministic across two invocations`` () =
    let r1 =
        PassChainAdapter.compose
            RegisteredTransforms.allChainSteps
            (ComposeState.initial exerciseCatalog)
    let r2 =
        PassChainAdapter.compose
            RegisteredTransforms.allChainSteps
            (ComposeState.initial exerciseCatalog)
    Assert.True(Lineage.byValueAndTrail r1 r2, "compose must be byte-deterministic across invocations on identical input")

// ---------------------------------------------------------------------------
// Idempotence-style witness: replaying just the tell-only adapter
// twice produces twice the trail. The trail length grows linearly
// with adapter-list length × per-adapter event count — confirming the
// fold honors the count and order semantics.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7' slice γ: trail length scales linearly with repeated adapter inclusion`` () =
    let result =
        PassChainAdapter.compose
            [ tellOnly; tellOnly; tellOnly ]
            (ComposeState.initial emptyCatalog)
    Assert.Equal(3, List.length result.Trail)
    Assert.Equal<LineageEvent list>([ fixtureEvent; fixtureEvent; fixtureEvent ], result.Trail)

// ---------------------------------------------------------------------------
// Wave-1 slice 1.4 — writeBack-correctness drift-guard.
//
// The hazard: a future `liftDecisionPass X.registered ComposeState.withY`
// registration compiles even when the `writeBack` setter targets the WRONG
// field — the type system cannot tell `withNullabilityDecisions` from
// `withUniqueIndexDecisions` (both are `'set -> ComposeState -> ComposeState`
// shaped after their decision-set argument). Such a mismatch would silently
// route a pass's output into a sibling field; the canary would not catch it
// because both fields are `Option`-shaped evidence.
//
// The structural witness below pins the contract that each `with*` decision
// setter mutates EXACTLY its own field and leaves every other decision field
// untouched. If a setter is ever re-pointed (the drift), the corresponding
// assertion flips: the "own field" check fails (it no longer sets its field)
// or a "no cross-write" check fails (it now also/instead sets another).
//
// Tactic: the five decision-set fields are all `_ option` and all `None` in
// `initial`. We snapshot which fields are `Some` after applying ONE setter to
// a sentinel value; the snapshot must contain exactly the setter's own field.
// The decision sets carry smart-constructor invariants, so we use each
// module's `emptyDecisionSet` (a valid empty value) as the sentinel.
// ---------------------------------------------------------------------------

/// The five decision-set fields of ComposeState, as `Some`/`None` flags, in a
/// fixed order. The drift-guard asserts on this projection so a re-pointed
/// setter surfaces as a changed flag vector.
let private decisionFlags (s: ComposeState) : bool list =
    [ s.NullabilityDecisions.IsSome
      s.UniqueIndexDecisions.IsSome
      s.ForeignKeyDecisions.IsSome
      s.CategoricalUniquenessDecisions.IsSome
      s.UserRemap.IsSome ]

[<Fact>]
let ``1.4 writeBack-guard: each decision setter populates exactly its own ComposeState field`` () =
    let init = ComposeState.initial emptyCatalog
    // Sanity: initial has no decision field set.
    Assert.Equal<bool list>([ false; false; false; false; false ], decisionFlags init)

    // (setter, expected-flag-vector) — exactly one `true`, in field order.
    let cases : (ComposeState -> ComposeState) list * bool list list =
        [ ComposeState.withNullabilityDecisions NullabilityRules.emptyDecisionSet
          ComposeState.withUniqueIndexDecisions UniqueIndexRules.emptyDecisionSet
          ComposeState.withForeignKeyDecisions ForeignKeyRules.emptyDecisionSet
          ComposeState.withCategoricalUniquenessDecisions CategoricalUniquenessRules.emptyDecisionSet
          ComposeState.withUserRemap UserRemapContext.empty ],
        [ [ true;  false; false; false; false ]
          [ false; true;  false; false; false ]
          [ false; false; true;  false; false ]
          [ false; false; false; true;  false ]
          [ false; false; false; false; true  ] ]

    let setters, expected = cases
    List.zip setters expected
    |> List.iteri (fun i (setter, exp) ->
        let after = decisionFlags (setter init)
        let msg =
            sprintf
                "decision setter #%d wrote fields %A but the contract is exactly %A — a writeBack drift (setter re-pointed to the wrong ComposeState field)."
                i after exp
        Assert.True((after = exp), msg))
