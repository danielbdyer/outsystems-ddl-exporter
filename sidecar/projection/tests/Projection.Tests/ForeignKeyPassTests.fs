module Projection.Tests.ForeignKeyPassTests

open System
open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Helpers — the ForeignKey pass driver tested at registry-iteration
// granularity, mirroring NullabilityPassTests and UniqueIndexPassTests.
// ---------------------------------------------------------------------------

let private mkConfig
    (enableCreation: bool)
    (allowCrossSchema: bool)
    (allowNoCheckCreation: bool) : ForeignKeyTighteningConfig =
    ForeignKeyTighteningConfig.create
        enableCreation allowCrossSchema true false allowNoCheckCreation

let private mkProbe (rowCount: int64) (outcome: ProbeOutcome) : ProbeStatus =
    ProbeStatus.create DateTimeOffset.UnixEpoch rowCount outcome
    |> Result.value

let private mkReality
    (referenceKey: SsKey)
    (hasOrphan: bool)
    (orphanCount: int64)
    (outcome: ProbeOutcome) : ForeignKeyReality =
    { ReferenceKey = referenceKey
      HasOrphan    = hasOrphan
      OrphanCount  = orphanCount
      IsNoCheck    = false
      ProbeStatus  = mkProbe 100L outcome }

let private policyWithIntervention (id: string) (config: ForeignKeyTighteningConfig) : Policy =
    { Policy.empty with
        Tightening = { Interventions = [ ForeignKey (id, config) ] } }

let private policyWithInterventions (interventions: TighteningIntervention list) : Policy =
    { Policy.empty with Tightening = { Interventions = interventions } }

let private allReferences (c: Catalog) : Reference list =
    Catalog.allKinds c |> List.collect (fun k -> k.References)

let private orderRef : Reference =
    order.References |> List.head

// ---------------------------------------------------------------------------
// Observable identity on empty policy — V2's strict default.
// ---------------------------------------------------------------------------

[<Fact>]
let ``structural commitment: empty Policy yields emptyDecisionSet`` () =
    let lineage = ForeignKeyPass.run sampleCatalog Policy.empty Profile.empty
    Assert.Equal<ForeignKeyDecisionSet>(
        ForeignKeyRules.emptyDecisionSet,
        lineage.Value)

[<Fact>]
let ``structural commitment: empty Policy emits no lineage events`` () =
    let lineage = ForeignKeyPass.run sampleCatalog Policy.empty Profile.empty
    Assert.Empty(lineage.Trail)

[<Fact>]
let ``structural commitment: a policy with only Nullability/UniqueIndex interventions yields empty FK output`` () =
    // The closed-DU dispatcher: ForeignKeyPass filters to its own
    // variant via wildcard pattern. A policy with the other two
    // variants must not produce FK decisions.
    let nullCfg =
        NullabilityTighteningConfig.create 0.0m false []
        |> Result.value
    let uniqCfg = UniqueIndexTighteningConfig.create true true
    let policy =
        policyWithInterventions
            [ Nullability ("null-1", nullCfg)
              UniqueIndex ("uniq-1", uniqCfg) ]
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    Assert.Empty(lineage.Value.Decisions)
    Assert.Empty(lineage.Trail)

[<Fact>]
let ``structural commitment: empty Tightening with non-empty other axes still yields empty result`` () =
    let policy =
        { Policy.empty with
            Selection = IncludeOnly (Set.singleton orderKey)
            Emission  = EmissionPolicy.combined
            Insertion = InsertNew }
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    Assert.Empty(lineage.Value.Decisions)
    Assert.Empty(lineage.Trail)

// ---------------------------------------------------------------------------
// One ForeignKey intervention — the pass emits one decision per
// reference across the catalog.
// ---------------------------------------------------------------------------

[<Fact>]
let ``one intervention: yields one decision per reference across the catalog`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true true)
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    let totalReferences = (allReferences sampleCatalog).Length
    Assert.Equal(totalReferences, lineage.Value.Decisions.Length)

[<Fact>]
let ``one intervention: every decision references its intervention id`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true true)
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    Assert.All(lineage.Value.Decisions, fun d ->
        Assert.Equal("v1-style", d.InterventionId))

[<Fact>]
let ``one intervention: emits one Annotated lineage event per decision`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true true)
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    Assert.Equal(lineage.Value.Decisions.Length, lineage.Trail.Length)
    Assert.All(lineage.Trail, fun e ->
        match e.TransformKind with
        | Annotated _ -> ()
        | other -> Assert.Fail(sprintf "Expected Annotated, got %A" other))

[<Fact>]
let ``one intervention: lineage event detail names the intervention id and an outcome category`` () =
    let policy = policyWithIntervention "v1-style-2026" (mkConfig true true true)
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    let categories = [ "EnforceConstraint"; "DoNotEnforce" ]
    Assert.All(lineage.Trail, fun e ->
        match e.TransformKind with
        | Annotated detail ->
            Assert.Contains("v1-style-2026", detail)
            let mentionsOne =
                categories |> List.exists (fun cat -> detail.Contains(cat))
            Assert.True(
                mentionsOne,
                sprintf "Detail '%s' should mention an outcome category" detail)
        | other ->
            Assert.Fail(sprintf "Expected Annotated, got %A" other))

// ---------------------------------------------------------------------------
// Two ForeignKey interventions — fan-out per (reference × intervention).
// ---------------------------------------------------------------------------

[<Fact>]
let ``two interventions: emit decisions for every (reference, intervention) pair`` () =
    let cfg = mkConfig true true true
    let policy =
        policyWithInterventions
            [ ForeignKey ("alpha", cfg)
              ForeignKey ("beta",  cfg) ]
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    let totalReferences = (allReferences sampleCatalog).Length
    Assert.Equal(totalReferences * 2, lineage.Value.Decisions.Length)

[<Fact>]
let ``two interventions: decisions are tagged with their producing intervention`` () =
    let cfg = mkConfig true true true
    let policy =
        policyWithInterventions
            [ ForeignKey ("alpha", cfg)
              ForeignKey ("beta",  cfg) ]
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    let alphaCount =
        lineage.Value.Decisions
        |> List.filter (fun d -> d.InterventionId = "alpha")
        |> List.length
    let betaCount =
        lineage.Value.Decisions
        |> List.filter (fun d -> d.InterventionId = "beta")
        |> List.length
    Assert.Equal(alphaCount, betaCount)

// ---------------------------------------------------------------------------
// Coexistence with Nullability and UniqueIndex passes — the closed-DU
// dispatcher continues to filter correctly with three variants
// registered.
// ---------------------------------------------------------------------------

[<Fact>]
let ``coexistence: ForeignKeyPass ignores Nullability and UniqueIndex interventions in a mixed policy`` () =
    let nullCfg =
        NullabilityTighteningConfig.create 0.0m false []
        |> Result.value
    let uniqCfg = UniqueIndexTighteningConfig.create true true
    let fkCfg = mkConfig true true true
    let policy =
        policyWithInterventions
            [ Nullability ("null-1", nullCfg)
              UniqueIndex ("uniq-1", uniqCfg)
              ForeignKey  ("fk-1",   fkCfg) ]
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    let totalReferences = (allReferences sampleCatalog).Length
    Assert.Equal(totalReferences, lineage.Value.Decisions.Length)
    Assert.All(lineage.Value.Decisions, fun d ->
        Assert.Equal("fk-1", d.InterventionId))

[<Fact>]
let ``coexistence: NullabilityPass ignores ForeignKey interventions`` () =
    let nullCfg =
        NullabilityTighteningConfig.create 0.0m false []
        |> Result.value
    let fkCfg = mkConfig true true true
    let policy =
        policyWithInterventions
            [ Nullability ("null-1", nullCfg)
              ForeignKey  ("fk-1",   fkCfg) ]
    let lineage = NullabilityPass.run sampleCatalog policy Profile.empty
    Assert.All((LineageDiagnostics.payload lineage).Decisions, fun d ->
        Assert.Equal("null-1", d.InterventionId))

[<Fact>]
let ``coexistence: UniqueIndexPass ignores ForeignKey interventions`` () =
    let uniqCfg = UniqueIndexTighteningConfig.create true true
    let fkCfg = mkConfig true true true
    let policy =
        policyWithInterventions
            [ UniqueIndex ("uniq-1", uniqCfg)
              ForeignKey  ("fk-1",   fkCfg) ]
    // No indexes in sampleCatalog, so empty decisions is expected;
    // but the test still verifies UniqueIndexPass doesn't produce
    // any FK-tagged decisions or fail.
    let lineage = UniqueIndexPass.run sampleCatalog policy Profile.empty
    Assert.All((LineageDiagnostics.payload lineage).Decisions, fun d ->
        Assert.Equal("uniq-1", d.InterventionId))

// ---------------------------------------------------------------------------
// Profile-driven decisions surface through the pass — orphan handling.
// ---------------------------------------------------------------------------

[<Fact>]
let ``orphan handling: profile shows orphans + AllowNoCheckCreation=true ⇒ ScriptWithNoCheck decision`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true true)
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey true 8L Succeeded ] }
    let lineage = ForeignKeyPass.run sampleCatalog policy profile
    let orderDecision =
        lineage.Value.Decisions
        |> List.find (fun d -> d.ReferenceKey = orderRef.SsKey)
    Assert.Equal(
        ForeignKeyOutcome.EnforceConstraint (ScriptWithNoCheck 8L),
        orderDecision.Outcome)

// ---------------------------------------------------------------------------
// T1 determinism — same triple ⇒ identical output (decisions + trail).
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: ForeignKeyPass is deterministic on the triple`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true true)
    let r1 = ForeignKeyPass.run sampleCatalog policy Profile.empty
    let r2 = ForeignKeyPass.run sampleCatalog policy Profile.empty
    Assert.Equal<ForeignKeyDecisionSet>(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

// ---------------------------------------------------------------------------
// Permutation invariance — sort/order discipline is structural.
// ---------------------------------------------------------------------------

[<Property>]
let ``contract: ForeignKeyPass is invariant under input permutation``
    (reverseModules: bool) (reverseKinds: bool) (reverseRefs: bool) =
    let perturb (c: Catalog) : Catalog =
        let withModules =
            { Modules =
                c.Modules
                |> List.map (fun m ->
                    let withKinds =
                        m.Kinds
                        |> List.map (fun k ->
                            if reverseRefs then { k with References = List.rev k.References }
                            else k)
                    if reverseKinds then { m with Kinds = List.rev withKinds }
                    else { m with Kinds = withKinds }) }
        if reverseModules then { withModules with Modules = List.rev withModules.Modules }
        else withModules
    let policy = policyWithIntervention "v1-style" (mkConfig true true true)
    let direct   = (ForeignKeyPass.run sampleCatalog policy Profile.empty).Value
    let permuted = (ForeignKeyPass.run (perturb sampleCatalog) policy Profile.empty).Value
    direct = permuted

// ---------------------------------------------------------------------------
// A23 / A25: lineage events carry pass version + name and reference real
// reference identities.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A23: events carry pass version and name`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true true)
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    Assert.All(lineage.Trail, fun e ->
        Assert.Equal(ForeignKeyPass.version, e.PassVersion)
        Assert.Equal("foreignKey", e.PassName))

[<Fact>]
let ``A25: every emitted event references a real reference SsKey`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true true)
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    let referenceKeys =
        allReferences sampleCatalog
        |> List.map (fun r -> r.SsKey)
        |> Set.ofList
    Assert.All(lineage.Trail, fun e ->
        Assert.Contains(e.SsKey, referenceKeys))

// ---------------------------------------------------------------------------
// The catalog is unchanged.
// ---------------------------------------------------------------------------

[<Fact>]
let ``catalog passes through unchanged: structural by signature`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true true)
    let _ = ForeignKeyPass.run sampleCatalog policy Profile.empty
    Assert.Equal(3, (Catalog.allKinds sampleCatalog).Length)

// ---------------------------------------------------------------------------
// V1 divergences — explicit skip stubs naming intentional V2 differences
// (CHAPTER_CLOSE.md §2.7; session 13 skip-stub completion).
// ---------------------------------------------------------------------------

[<Fact(Skip = "V1 ForeignKeyEvaluatorTests.TreatMissingDeleteRuleAsIgnore_AllowsCreation asserts that a successful FK decision (CreateConstraint=true) carries the rationale string TighteningRationales.DeleteRuleIgnore alongside PolicyEnableCreation. V2's structured-rationale ForeignKeyOutcome carries no rationale string on a successful decision — by codification (DECISIONS 2026-05-11) lineage events fire only on actual decisions and the outcome variant carries the structured reason, not a free-form string. The V1 audit-trail concern (a successful FK decision noting that delete-rule-missing was tolerated) belongs to the Diagnostics writer when it lands (DECISIONS 2026-05-06).")>]
let ``V1 ForeignKey: DeleteRuleIgnore rationale on successful decision — SKIPPED (V2 divergence)`` () =
    ()
