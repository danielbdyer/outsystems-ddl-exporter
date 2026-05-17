module Projection.Tests.ComposeChainAdapterTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> failwithf "expected Ok but got Error: %A" es

// ---------------------------------------------------------------------------
// Chapter A.4.7' slice α — ComposeState + PassChainAdapter type-system
// witnesses.
//
// Slice α ships:
//   - `ComposeState` record carrying Catalog + Option fields for every
//     decision-set producer (TopologicalOrder / NullabilityDecisionSet /
//     UniqueIndexDecisionSet / ForeignKeyDecisionSet /
//     CategoricalUniquenessDecisionSet / UserRemapContext).
//   - `ComposeState.initial` + per-axis `withX` setters.
//   - `PassChainAdapter` record carrying `Name` + uniform `Apply` step.
//   - `PassChainAdapter.liftCatalogPass` / `liftDecisionPass`
//     constructors that wrap typed `RegisteredTransform<Catalog, _>`
//     into the chain-step shape.
//
// Slice α does NOT populate `TransformRegistry.all` or migrate
// `Compose.project`; those land in slices β / δ.
// ---------------------------------------------------------------------------

let private constantCatalogTransform : RegisteredTransform<Catalog, Catalog> =
    { Name = "A47PrimeSliceAlphaConstantCatalogPass"
      Domain = Schema
      StageBinding = Pass
      Sites =
        [ { SiteName = "constant"
            Classification = DataIntent
            Rationale = "slice α witness fixture; identity transformation" } ]
      Run = fun catalog -> LineageDiagnostics.ofValue catalog
      Status = Active }

let private constantDecisionTransform : RegisteredTransform<Catalog, NullabilityDecisionSet> =
    { Name = "A47PrimeSliceAlphaConstantDecisionPass"
      Domain = Schema
      StageBinding = Pass
      Sites =
        [ { SiteName = "constant"
            Classification = DataIntent
            Rationale = "slice α witness fixture; empty decision set" } ]
      Run = fun _catalog -> LineageDiagnostics.ofValue NullabilityRules.emptyDecisionSet
      Status = Active }

let private fixtureCatalog : Catalog =
    Catalog.create [] [] |> mustOk

let private fixtureEvent : LineageEvent =
    { PassName = "A47PrimeSliceAlphaWitness"
      PassVersion = 1
      SsKey = testKey "alphaWitness"
      TransformKind = Touched
      Classification = DataIntent }

let private fixtureDiagnostic : DiagnosticEntry =
    { Source = "A47PrimeSliceAlphaWitness"
      Severity = DiagnosticSeverity.Info
      Code = "slice.alpha.witness"
      Message = "lifted-pass propagation witness"
      SsKey = None
      Metadata = Map.empty }

// ---------------------------------------------------------------------------
// ComposeState shape.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7' slice α: ComposeState.initial carries the catalog and None decisions`` () =
    let state = ComposeState.initial fixtureCatalog
    Assert.Equal(fixtureCatalog, state.Catalog)
    Assert.Equal(None, state.TopologicalOrder)
    Assert.Equal(None, state.NullabilityDecisions)
    Assert.Equal(None, state.UniqueIndexDecisions)
    Assert.Equal(None, state.ForeignKeyDecisions)
    Assert.Equal(None, state.CategoricalUniquenessDecisions)
    Assert.Equal(None, state.UserRemap)

[<Fact>]
let ``A.4.7' slice α: ComposeState.withNullabilityDecisions sets the field idempotently`` () =
    let state = ComposeState.initial fixtureCatalog
    let written =
        state
        |> ComposeState.withNullabilityDecisions NullabilityRules.emptyDecisionSet
        |> ComposeState.withNullabilityDecisions NullabilityRules.emptyDecisionSet
    Assert.Equal(Some NullabilityRules.emptyDecisionSet, written.NullabilityDecisions)
    Assert.Equal(fixtureCatalog, written.Catalog)

// ---------------------------------------------------------------------------
// PassChainAdapter — liftCatalogPass / liftDecisionPass.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7' slice α: liftCatalogPass round-trips the catalog through Apply`` () =
    let adapter = PassChainAdapter.liftCatalogPass constantCatalogTransform
    let state = ComposeState.initial fixtureCatalog
    let result = adapter.Apply state
    Assert.Equal(constantCatalogTransform.Name, adapter.Name)
    Assert.Equal(fixtureCatalog, (LineageDiagnostics.payload result).Catalog)

[<Fact>]
let ``A.4.7' slice α: liftDecisionPass writes back via the provided setter`` () =
    let adapter =
        PassChainAdapter.liftDecisionPass
            constantDecisionTransform
            ComposeState.withNullabilityDecisions
    let state = ComposeState.initial fixtureCatalog
    let result = adapter.Apply state
    let payload = LineageDiagnostics.payload result
    Assert.Equal(constantDecisionTransform.Name, adapter.Name)
    Assert.Equal(fixtureCatalog, payload.Catalog)
    Assert.Equal(Some NullabilityRules.emptyDecisionSet, payload.NullabilityDecisions)

// ---------------------------------------------------------------------------
// Writer-fidelity through Apply — Lineage trail + Diagnostics entries
// produced by the wrapped pass propagate unchanged via `LineageDiagnostics.map`.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7' slice α: liftCatalogPass preserves the Lineage trail emitted by the wrapped pass`` () =
    let trailEmitter : RegisteredTransform<Catalog, Catalog> =
        { constantCatalogTransform with
            Run =
                fun catalog ->
                    LineageDiagnostics.ofValue catalog
                    |> LineageDiagnostics.tellLineage fixtureEvent }
    let adapter = PassChainAdapter.liftCatalogPass trailEmitter
    let result = adapter.Apply (ComposeState.initial fixtureCatalog)
    Assert.Equal<LineageEvent list>([ fixtureEvent ], result.Trail)

[<Fact>]
let ``A.4.7' slice α: liftDecisionPass preserves Diagnostics entries emitted by the wrapped pass`` () =
    let diagnosticEmitter : RegisteredTransform<Catalog, NullabilityDecisionSet> =
        { constantDecisionTransform with
            Run =
                fun _catalog ->
                    LineageDiagnostics.ofValue NullabilityRules.emptyDecisionSet
                    |> LineageDiagnostics.tellDiagnostic fixtureDiagnostic }
    let adapter =
        PassChainAdapter.liftDecisionPass
            diagnosticEmitter
            ComposeState.withNullabilityDecisions
    let result = adapter.Apply (ComposeState.initial fixtureCatalog)
    Assert.Equal<DiagnosticEntry list>([ fixtureDiagnostic ], LineageDiagnostics.entries result)
