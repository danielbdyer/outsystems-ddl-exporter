module Projection.Tests.NullabilityPassTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private mkConfig
    (nullBudget: decimal)
    (allowRelax: bool)
    (overrides: TighteningOverride list) : NullabilityTighteningConfig =
    NullabilityTighteningConfig.create nullBudget allowRelax overrides
    |> Result.value

let private policyWithIntervention (id: string) (config: NullabilityTighteningConfig) : Policy =
    { Policy.empty with
        Tightening = { Interventions = [ Nullability (id, config) ] } }

let private policyWithInterventions (interventions: TighteningIntervention list) : Policy =
    { Policy.empty with Tightening = { Interventions = interventions } }

// ---------------------------------------------------------------------------
// Observable identity on empty policy — the structural commitment
// (DECISIONS 2026-05-09). When the policy has no Nullability
// interventions, the pass produces emptyDecisionSet with no events.
// ---------------------------------------------------------------------------

[<Fact>]
let ``structural commitment: empty Policy yields emptyDecisionSet`` () =
    let lineage = NullabilityPass.run sampleCatalog Policy.empty Profile.empty
    Assert.Equal<NullabilityDecisionSet>(NullabilityRules.emptyDecisionSet, NullabilityPass.decisionsOf lineage)

[<Fact>]
let ``structural commitment: empty Policy emits no lineage events`` () =
    let lineage = NullabilityPass.run sampleCatalog Policy.empty Profile.empty
    Assert.Empty(lineage.Trail)

[<Fact>]
let ``structural commitment: empty Tightening with non-empty other axes still yields empty result`` () =
    // Selection / Emission / Insertion are non-trivial; Tightening is
    // empty. The pass should still produce no decisions.
    let policy =
        { Policy.empty with
            Selection = IncludeOnly (Set.singleton customerKey)
            Emission  = EmissionPolicy.combined
            Insertion = InsertNew }
    let lineage = NullabilityPass.run sampleCatalog policy Profile.empty
    Assert.Empty((LineageDiagnostics.payload lineage).Decisions)
    Assert.Empty(lineage.Trail)

// ---------------------------------------------------------------------------
// One Nullability intervention — the pass emits one decision per
// attribute on every kind, plus one Annotated lineage event per
// decision.
// ---------------------------------------------------------------------------

[<Fact>]
let ``one intervention: yields one decision per attribute on every kind`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig 0.0m false [])
    let lineage = NullabilityPass.run sampleCatalog policy Profile.empty
    let totalAttributes =
        Catalog.allKinds sampleCatalog
        |> List.sumBy (fun k -> k.Attributes.Length)
    Assert.Equal(totalAttributes, (LineageDiagnostics.payload lineage).Decisions.Length)

[<Fact>]
let ``one intervention: every decision references its intervention id`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig 0.0m false [])
    let lineage = NullabilityPass.run sampleCatalog policy Profile.empty
    Assert.All((LineageDiagnostics.payload lineage).Decisions, fun d ->
        Assert.Equal("v1-style", d.InterventionId))

[<Fact>]
let ``one intervention: emits one Annotated lineage event per decision`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig 0.0m false [])
    let lineage = NullabilityPass.run sampleCatalog policy Profile.empty
    Assert.Equal((LineageDiagnostics.payload lineage).Decisions.Length, lineage.Trail.Length)
    Assert.All(lineage.Trail, fun e ->
        match e.TransformKind with
        | Annotated _ -> ()
        | other -> Assert.Fail(sprintf "Expected Annotated, got %A" other))

[<Fact>]
let ``one intervention: lineage event detail names the intervention id and outcome`` () =
    // Chapter-3.6 slice-β: typed `AnnotationDetail.NullabilityDecision`
    // payload replaces the prior `<id> -> <outcome>` built-name string.
    // The test now asserts the typed payload structurally — intervention
    // id is the first tuple element; outcome is structurally one of the
    // three `NullabilityOutcome` variants (compiler-checked
    // exhaustiveness).
    let policy = policyWithIntervention "v1-style-2026" (mkConfig 0.0m false [])
    let lineage = NullabilityPass.run sampleCatalog policy Profile.empty
    Assert.All(lineage.Trail, fun e ->
        match e.TransformKind with
        | Annotated (NullabilityDecision (id, outcome)) ->
            Assert.Equal("v1-style-2026", id)
            // Outcome is one of the three NullabilityOutcome variants.
            match outcome with
            | NullabilityOutcome.EnforceNotNull _
            | NullabilityOutcome.KeepNullable _
            | NullabilityOutcome.RequireOperatorApproval _ -> ()
        | other ->
            Assert.Fail(sprintf "Expected Annotated (NullabilityDecision _), got %A" other))

// ---------------------------------------------------------------------------
// Two Nullability interventions — the pass emits one decision per
// (attribute × intervention) pair.
// ---------------------------------------------------------------------------

[<Fact>]
let ``two interventions: emit decisions for every (attribute, intervention) pair`` () =
    let cfg = mkConfig 0.0m false []
    let policy =
        policyWithInterventions
            [ Nullability ("alpha", cfg)
              Nullability ("beta",  cfg) ]
    let lineage = NullabilityPass.run sampleCatalog policy Profile.empty
    let totalAttributes =
        Catalog.allKinds sampleCatalog
        |> List.sumBy (fun k -> k.Attributes.Length)
    Assert.Equal(totalAttributes * 2, (LineageDiagnostics.payload lineage).Decisions.Length)

[<Fact>]
let ``two interventions: decisions are tagged with their producing intervention`` () =
    let cfg = mkConfig 0.0m false []
    let policy =
        policyWithInterventions
            [ Nullability ("alpha", cfg)
              Nullability ("beta",  cfg) ]
    let lineage = NullabilityPass.run sampleCatalog policy Profile.empty
    let alphaCount =
        (LineageDiagnostics.payload lineage).Decisions
        |> List.filter (fun d -> d.InterventionId = "alpha")
        |> List.length
    let betaCount =
        (LineageDiagnostics.payload lineage).Decisions
        |> List.filter (fun d -> d.InterventionId = "beta")
        |> List.length
    Assert.Equal(alphaCount, betaCount)

// ---------------------------------------------------------------------------
// Override absoluteness preserved through the pass.
// ---------------------------------------------------------------------------

[<Fact>]
let ``overrides survive through the pass: a KeepNullable override produces KeepNullable(OperatorOverride)`` () =
    // Customer's PK attribute would normally enforce NOT NULL via
    // PrimaryKey signal. With an override, it should KeepNullable.
    let pkAttr = customer.Attributes |> List.find (fun a -> a.IsPrimaryKey)
    let cfg =
        mkConfig 0.0m false
            [ { AttributeKey = pkAttr.SsKey; Action = OverrideAction.KeepNullable } ]
    let policy = policyWithIntervention "with-override" cfg
    let lineage = NullabilityPass.run sampleCatalog policy Profile.empty
    let pkDecision =
        (LineageDiagnostics.payload lineage).Decisions
        |> List.find (fun d -> d.AttributeKey = pkAttr.SsKey)
    Assert.Equal(NullabilityOutcome.KeepNullable OperatorOverride, pkDecision.Outcome)

// ---------------------------------------------------------------------------
// T1 determinism — same triple ⇒ identical output (decisions + trail).
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: NullabilityPass is deterministic on the triple`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig 0.0m false [])
    let r1 = NullabilityPass.run sampleCatalog policy Profile.empty
    let r2 = NullabilityPass.run sampleCatalog policy Profile.empty
    Assert.Equal<NullabilityDecisionSet>(NullabilityPass.decisionsOf r1, NullabilityPass.decisionsOf r2)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

// ---------------------------------------------------------------------------
// Permutation invariance — V2's contract from session 4 (Kahn's). The
// pass output is invariant under input-list reordering at every level
// (modules, kinds, attributes).
// ---------------------------------------------------------------------------

[<Property>]
let ``contract: NullabilityPass is invariant under input permutation``
    (reverseModules: bool) (reverseKinds: bool) (reverseAttrs: bool) =
    let perturb (c: Catalog) : Catalog =
        let withModules =
            { Modules =
                c.Modules
                |> List.map (fun m ->
                    let withKinds =
                        m.Kinds
                        |> List.map (fun k ->
                            if reverseAttrs then { k with Attributes = List.rev k.Attributes }
                            else k)
                    if reverseKinds then { m with Kinds = List.rev withKinds }
                    else { m with Kinds = withKinds }) }
        if reverseModules then { withModules with Modules = List.rev withModules.Modules }
        else withModules
    let policy = policyWithIntervention "v1-style" (mkConfig 0.0m false [])
    let direct   = (NullabilityPass.run sampleCatalog policy Profile.empty).Value
    let permuted = (NullabilityPass.run (perturb sampleCatalog) policy Profile.empty).Value
    direct = permuted

// ---------------------------------------------------------------------------
// A23 / A25: lineage events carry pass version + name.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A23: events carry pass version and name`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig 0.0m false [])
    let lineage = NullabilityPass.run sampleCatalog policy Profile.empty
    Assert.All(lineage.Trail, fun e ->
        Assert.Equal(NullabilityPass.version, e.PassVersion)
        Assert.Equal("nullability", e.PassName))

[<Fact>]
let ``A25: every emitted event references a real attribute SsKey`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig 0.0m false [])
    let lineage = NullabilityPass.run sampleCatalog policy Profile.empty
    let attributeKeys =
        Catalog.allKinds sampleCatalog
        |> List.collect (fun k -> k.Attributes |> List.map (fun a -> a.SsKey))
        |> Set.ofList
    Assert.All(lineage.Trail, fun e ->
        Assert.Contains(e.SsKey, attributeKeys))

// ---------------------------------------------------------------------------
// The catalog is unchanged — the pass produces a value, not a
// transformed catalog. This is structural by signature
// (run : Catalog -> Policy -> Profile -> Lineage<NullabilityDecisionSet>),
// but the test makes the property explicit.
// ---------------------------------------------------------------------------

[<Fact>]
let ``catalog passes through unchanged: structural by signature`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig 0.0m false [])
    let _ = NullabilityPass.run sampleCatalog policy Profile.empty
    // sampleCatalog is referenced from the global Fixtures module; if
    // the pass mutated, the fixture would be corrupted across tests.
    Assert.Equal(3, (Catalog.allKinds sampleCatalog).Length)
