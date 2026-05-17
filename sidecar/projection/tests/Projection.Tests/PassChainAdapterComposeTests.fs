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
