module Projection.Tests.PolicyTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Tests.Fixtures
open Projection.Tests.ProfileFixtures

// ---------------------------------------------------------------------------
// A12 amended (V2 2026-05-09): Policy is composed of FOUR named axes.
// The signature is the test — adding a fifth field would break compilation
// here. The Tightening axis was added under "IR grows under evidence" when
// the NullabilityEvaluator admire surfaced the need (DECISIONS 2026-05-09).
// The original three-axis history is preserved in the AXIOMS amendment;
// these tests are the V2 expectation.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A12 (V2 2026-05-09): Policy.empty pins all four axes at empty defaults`` () =
    Assert.Equal<SelectionPolicy>(IncludeAll,                Policy.empty.Selection)
    Assert.Equal<EmissionPolicy>(EmissionPolicy.schemaOnly,  Policy.empty.Emission)
    Assert.Equal<InsertionPolicy>(SchemaOnly,                Policy.empty.Insertion)
    Assert.Equal<TighteningPolicy>(TighteningPolicy.empty,   Policy.empty.Tightening)

[<Fact>]
let ``A12 (V2 2026-05-09): TighteningPolicy.empty has zero interventions`` () =
    // Per the plugin/intervention refactor (DECISIONS 2026-05-09):
    // empty TighteningPolicy means no interventions registered ⇒
    // no tightening decisions produced. V2 introduces no alterations
    // unless an intervention is explicitly registered.
    Assert.Empty(TighteningPolicy.empty.Interventions)
    Assert.True(TighteningPolicy.isEmpty TighteningPolicy.empty)

// ---------------------------------------------------------------------------
// A12 orthogonality: changing one axis does not alter the helpers of the
// other axes. Property test sweeps every (axis, value) combination.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A12: changing Emission does not affect SelectionPolicy.isSelected`` () =
    let p1 = { Policy.empty with Emission = EmissionPolicy.combined }
    let p2 = { Policy.empty with Emission = EmissionPolicy.dataOnly }
    Assert.Equal(SelectionPolicy.isSelected customerKey p1.Selection,
                 SelectionPolicy.isSelected customerKey p2.Selection)

[<Fact>]
let ``A12: changing Insertion does not affect SelectionPolicy.isSelected`` () =
    let p1 = { Policy.empty with Insertion = InsertNew }
    let p2 = { Policy.empty with Insertion = Merge }
    Assert.Equal(SelectionPolicy.isSelected customerKey p1.Selection,
                 SelectionPolicy.isSelected customerKey p2.Selection)

[<Fact>]
let ``A12: changing Selection does not affect Emission flags`` () =
    let p1 = { Policy.empty with Selection = IncludeOnly (Set.singleton customerKey) }
    let p2 = { Policy.empty with Selection = ExcludeOnly (Set.singleton customerKey) }
    Assert.Equal(p1.Emission, p2.Emission)

// ---------------------------------------------------------------------------
// A12 (V2 2026-05-09): Tightening orthogonality with the other three axes.
// Three new pairs land here. Tightening is exercised via a registered
// nullability intervention rather than a flat config — empty
// TighteningPolicy means no intervention runs.
// ---------------------------------------------------------------------------

let private sampleNullabilityIntervention () : TighteningIntervention =
    let cfg =
        NullabilityTighteningConfig.create 0.1m false []
        |> Result.value
    Nullability ("test-nullability", cfg)

[<Fact>]
let ``A12: changing Tightening does not affect SelectionPolicy.isSelected`` () =
    let withIntervention =
        { Interventions = [ sampleNullabilityIntervention () ] }
    let p1 = { Policy.empty with Tightening = withIntervention }
    let p2 = Policy.empty
    Assert.Equal(SelectionPolicy.isSelected customerKey p1.Selection,
                 SelectionPolicy.isSelected customerKey p2.Selection)

[<Fact>]
let ``A12: changing Tightening does not affect Emission flags`` () =
    let withIntervention =
        { Interventions = [ sampleNullabilityIntervention () ] }
    let p1 = { Policy.empty with Tightening = withIntervention }
    let p2 = Policy.empty
    Assert.Equal(p1.Emission, p2.Emission)

[<Fact>]
let ``A12: changing Tightening does not affect Insertion`` () =
    let withIntervention =
        { Interventions = [ sampleNullabilityIntervention () ] }
    let p1 = { Policy.empty with Tightening = withIntervention }
    let p2 = Policy.empty
    Assert.Equal<InsertionPolicy>(p1.Insertion, p2.Insertion)

[<Fact>]
let ``A12: changing Selection does not affect Tightening`` () =
    let p1 = { Policy.empty with Selection = IncludeOnly (Set.singleton customerKey) }
    let p2 = { Policy.empty with Selection = ExcludeOnly (Set.singleton customerKey) }
    Assert.Equal<TighteningPolicy>(p1.Tightening, p2.Tightening)

// ---------------------------------------------------------------------------
// NullabilityTighteningConfig.create — validates NullBudget range.
// ---------------------------------------------------------------------------

[<Fact>]
let ``NullabilityTighteningConfig.create accepts NullBudget in [0, 1]`` () =
    Assert.True(Result.isSuccess (NullabilityTighteningConfig.create 0.0m false []))
    Assert.True(Result.isSuccess (NullabilityTighteningConfig.create 0.5m false []))
    Assert.True(Result.isSuccess (NullabilityTighteningConfig.create 1.0m false []))

[<Fact>]
let ``NullabilityTighteningConfig.create rejects NullBudget below 0`` () =
    Assert.True(Result.isFailure (NullabilityTighteningConfig.create -0.1m false []))

[<Fact>]
let ``NullabilityTighteningConfig.create rejects NullBudget above 1`` () =
    Assert.True(Result.isFailure (NullabilityTighteningConfig.create 1.1m false []))

[<Fact>]
let ``NullabilityTighteningConfig.create captures every field`` () =
    let overrides =
        [ { AttributeKey = customerNameKey; Action = KeepNullable } ]
    let cfg =
        NullabilityTighteningConfig.create 0.05m true overrides
        |> Result.value
    Assert.Equal(0.05m, cfg.NullBudget)
    Assert.True(cfg.AllowMandatoryRelaxation)
    Assert.Equal(1, cfg.Overrides.Length)

// ---------------------------------------------------------------------------
// NullabilityTighteningConfig.shouldKeepNullable — override lookup.
// ---------------------------------------------------------------------------

[<Fact>]
let ``shouldKeepNullable returns true for an attribute with a KeepNullable override`` () =
    let cfg =
        NullabilityTighteningConfig.create 0.0m false
            [ { AttributeKey = customerNameKey; Action = KeepNullable } ]
        |> Result.value
    Assert.True(NullabilityTighteningConfig.shouldKeepNullable customerNameKey cfg)

[<Fact>]
let ``shouldKeepNullable returns false when no overrides registered`` () =
    let cfg =
        NullabilityTighteningConfig.create 0.0m false []
        |> Result.value
    Assert.False(NullabilityTighteningConfig.shouldKeepNullable customerNameKey cfg)

[<Fact>]
let ``shouldKeepNullable returns false for a different attribute's override`` () =
    let cfg =
        NullabilityTighteningConfig.create 0.0m false
            [ { AttributeKey = customerNameKey; Action = KeepNullable } ]
        |> Result.value
    Assert.False(NullabilityTighteningConfig.shouldKeepNullable customerIdAttrKey cfg)

// ---------------------------------------------------------------------------
// TighteningPolicy registry — interventions are named and retrievable.
// ---------------------------------------------------------------------------

[<Fact>]
let ``TighteningPolicy.empty contains no interventions`` () =
    Assert.True(TighteningPolicy.isEmpty TighteningPolicy.empty)
    Assert.Empty(TighteningPolicy.empty.Interventions)
    Assert.Empty(TighteningPolicy.nullabilityInterventions TighteningPolicy.empty)

[<Fact>]
let ``TighteningPolicy.tryFindNullability finds a registered intervention by id`` () =
    let cfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    let policy = { Interventions = [ Nullability ("v1-style", cfg) ] }
    let found = TighteningPolicy.tryFindNullability "v1-style" policy
    Assert.True(Option.isSome found)
    Assert.Equal(0.05m, (Option.get found).NullBudget)

[<Fact>]
let ``TighteningPolicy.tryFindNullability returns None for an unknown id`` () =
    let cfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    let policy = { Interventions = [ Nullability ("v1-style", cfg) ] }
    Assert.Equal(None, TighteningPolicy.tryFindNullability "missing" policy)

[<Fact>]
let ``TighteningPolicy.nullabilityInterventions returns ids in registration order`` () =
    let cfgA = NullabilityTighteningConfig.create 0.0m false [] |> Result.value
    let cfgB = NullabilityTighteningConfig.create 0.1m false [] |> Result.value
    let policy =
        { Interventions =
            [ Nullability ("alpha", cfgA)
              Nullability ("beta",  cfgB) ] }
    let ids =
        TighteningPolicy.nullabilityInterventions policy
        |> List.map fst
    Assert.Equal<string list>([ "alpha"; "beta" ], ids)

[<Fact>]
let ``TighteningIntervention.id returns the intervention's stable identifier`` () =
    let cfg = NullabilityTighteningConfig.create 0.0m false [] |> Result.value
    let intervention = Nullability ("registered-id", cfg)
    Assert.Equal("registered-id", TighteningIntervention.id intervention)

// ---------------------------------------------------------------------------
// SelectionPolicy.isSelected exhaustively tested across the three variants.
// ---------------------------------------------------------------------------

[<Fact>]
let ``SelectionPolicy.IncludeAll selects every kind`` () =
    Assert.True(SelectionPolicy.isSelected customerKey IncludeAll)
    Assert.True(SelectionPolicy.isSelected orderKey    IncludeAll)
    Assert.True(SelectionPolicy.isSelected countryKey  IncludeAll)

[<Fact>]
let ``SelectionPolicy.IncludeOnly selects exactly the named keys`` () =
    let policy = IncludeOnly (Set.ofList [ customerKey; orderKey ])
    Assert.True (SelectionPolicy.isSelected customerKey policy)
    Assert.True (SelectionPolicy.isSelected orderKey    policy)
    Assert.False(SelectionPolicy.isSelected countryKey  policy)

[<Fact>]
let ``SelectionPolicy.ExcludeOnly selects every kind except the named ones`` () =
    let policy = ExcludeOnly (Set.singleton countryKey)
    Assert.True (SelectionPolicy.isSelected customerKey policy)
    Assert.True (SelectionPolicy.isSelected orderKey    policy)
    Assert.False(SelectionPolicy.isSelected countryKey  policy)

[<Property>]
let ``SelectionPolicy.IncludeAll is selectAll under any key`` (s: NonEmptyString) =
    if System.String.IsNullOrWhiteSpace s.Get then true
    else
        let key = SsKey.original s.Get |> Result.value
        SelectionPolicy.isSelected key IncludeAll

// ---------------------------------------------------------------------------
// SelectionPolicy.filterCatalog produces a catalog containing only the
// selected kinds. Identity preservation: surviving kinds are byte-identical
// to their inputs.
// ---------------------------------------------------------------------------

[<Fact>]
let ``filterCatalog with IncludeAll is the identity`` () =
    Assert.Equal(sampleCatalog, SelectionPolicy.filterCatalog IncludeAll sampleCatalog)

[<Fact>]
let ``filterCatalog with IncludeOnly drops unselected kinds`` () =
    let policy = IncludeOnly (Set.singleton customerKey)
    let result = SelectionPolicy.filterCatalog policy sampleCatalog
    Assert.True (Option.isSome (Catalog.tryFindKind customerKey result))
    Assert.Equal(None,  Catalog.tryFindKind orderKey   result)
    Assert.Equal(None,  Catalog.tryFindKind countryKey result)

[<Fact>]
let ``filterCatalog with ExcludeOnly drops named kinds`` () =
    let policy = ExcludeOnly (Set.singleton countryKey)
    let result = SelectionPolicy.filterCatalog policy sampleCatalog
    Assert.True (Option.isSome (Catalog.tryFindKind customerKey result))
    Assert.True (Option.isSome (Catalog.tryFindKind orderKey   result))
    Assert.Equal(None, Catalog.tryFindKind countryKey result)

// ---------------------------------------------------------------------------
// A6 amended: ProjectionInput bundles the three substantive inputs.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A6: ProjectionInput.ofCatalog builds the minimal triple`` () =
    let input = ProjectionInput.ofCatalog sampleCatalog
    Assert.Equal(sampleCatalog, input.Catalog)
    Assert.Equal(Policy.empty,  input.Policy)
    Assert.True(Profile.isEmpty input.Profile)
    Assert.True(ProjectionInput.isMinimal input)

[<Fact>]
let ``A6: ProjectionInput.isMinimal is false when Policy is non-empty`` () =
    let input =
        { ProjectionInput.ofCatalog sampleCatalog with
            Policy = { Policy.empty with Selection = IncludeOnly Set.empty } }
    Assert.False(ProjectionInput.isMinimal input)

[<Fact>]
let ``A6: ProjectionInput.isMinimal is false when Profile is populated`` () =
    let input = { ProjectionInput.ofCatalog sampleCatalog with Profile = sampleProfile }
    Assert.False(ProjectionInput.isMinimal input)

// ---------------------------------------------------------------------------
// T1 extended (per A17 amended) — `Project = Π ∘ E` is deterministic on
// the triple `(Catalog, Policy, Profile)`. The current passes don't yet
// take the full triple; the property is anchored here through structural
// equality of `ProjectionInput`. When passes that consume the triple
// arrive (NormalizeStaticPopulations + a hypothetical evidence-driven
// pass), the test extends to "same triple ⇒ same Lineage<Catalog>".
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1 extended: equal ProjectionInputs are structurally equal`` () =
    let a = { Catalog = sampleCatalog; Policy = Policy.empty; Profile = sampleProfile }
    let b = { Catalog = sampleCatalog; Policy = Policy.empty; Profile = sampleProfile }
    Assert.Equal(a, b)

[<Fact>]
let ``T1 extended: ProjectionInputs differing in any axis are not equal`` () =
    let baseInput = ProjectionInput.ofCatalog sampleCatalog
    let differentPolicy =
        { baseInput with Policy = { Policy.empty with Insertion = Merge } }
    let differentProfile =
        { baseInput with Profile = sampleProfile }
    Assert.NotEqual<ProjectionInput>(baseInput, differentPolicy)
    Assert.NotEqual<ProjectionInput>(baseInput, differentProfile)

[<Property>]
let ``T1 extended: identity over the triple is preserved`` () =
    // Structural equality is reflexive on records; this property
    // guarantees the F# compiler emits the equality we assume in tests.
    let input = { Catalog = sampleCatalog; Policy = Policy.empty; Profile = sampleProfile }
    input = input

// ---------------------------------------------------------------------------
// EmissionPolicy convenience builders.
// ---------------------------------------------------------------------------

[<Fact>]
let ``EmissionPolicy.dataOnly emits data, withholds schema and diagnostics`` () =
    Assert.False(EmissionPolicy.dataOnly.EmitSchema)
    Assert.True (EmissionPolicy.dataOnly.EmitData)
    Assert.False(EmissionPolicy.dataOnly.EmitDiagnostics)

[<Fact>]
let ``EmissionPolicy.combined emits all three`` () =
    Assert.True(EmissionPolicy.combined.EmitSchema)
    Assert.True(EmissionPolicy.combined.EmitData)
    Assert.True(EmissionPolicy.combined.EmitDiagnostics)
