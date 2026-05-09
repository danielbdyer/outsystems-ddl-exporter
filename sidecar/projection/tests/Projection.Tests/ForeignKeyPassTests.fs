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
        ForeignKeyPass.decisionsOf lineage)

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
    Assert.Empty((LineageDiagnostics.payload lineage).Decisions)
    Assert.Empty(lineage.Trail)

[<Fact>]
let ``structural commitment: empty Tightening with non-empty other axes still yields empty result`` () =
    let policy =
        { Policy.empty with
            Selection = IncludeOnly (Set.singleton orderKey)
            Emission  = EmissionPolicy.combined
            Insertion = InsertNew }
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    Assert.Empty((LineageDiagnostics.payload lineage).Decisions)
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
    Assert.Equal(totalReferences, (LineageDiagnostics.payload lineage).Decisions.Length)

[<Fact>]
let ``one intervention: every decision references its intervention id`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true true)
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    Assert.All((LineageDiagnostics.payload lineage).Decisions, fun d ->
        Assert.Equal("v1-style", d.InterventionId))

[<Fact>]
let ``one intervention: emits one Annotated lineage event per decision`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true true)
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    Assert.Equal((LineageDiagnostics.payload lineage).Decisions.Length, lineage.Trail.Length)
    Assert.All(lineage.Trail, fun e ->
        match e.TransformKind with
        | Annotated _ -> ()
        | other -> Assert.Fail(sprintf "Expected Annotated, got %A" other))

[<Fact>]
let ``one intervention: lineage event detail names the intervention id and an outcome category`` () =
    // Chapter-3.6 slice-β: typed `AnnotationDetail.ForeignKeyDecision`
    // payload — outcome flows through structurally.
    let policy = policyWithIntervention "v1-style-2026" (mkConfig true true true)
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
    Assert.All(lineage.Trail, fun e ->
        match e.TransformKind with
        | Annotated (ForeignKeyDecision (id, outcome)) ->
            Assert.Equal("v1-style-2026", id)
            // Outcome is one of the two ForeignKeyOutcome variants.
            match outcome with
            | ForeignKeyOutcome.EnforceConstraint _
            | ForeignKeyOutcome.DoNotEnforce _ -> ()
        | other ->
            Assert.Fail(sprintf "Expected Annotated (ForeignKeyDecision _), got %A" other))

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
    Assert.Equal(totalReferences * 2, (LineageDiagnostics.payload lineage).Decisions.Length)

[<Fact>]
let ``two interventions: decisions are tagged with their producing intervention`` () =
    let cfg = mkConfig true true true
    let policy =
        policyWithInterventions
            [ ForeignKey ("alpha", cfg)
              ForeignKey ("beta",  cfg) ]
    let lineage = ForeignKeyPass.run sampleCatalog policy Profile.empty
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
    Assert.Equal(totalReferences, (LineageDiagnostics.payload lineage).Decisions.Length)
    Assert.All((LineageDiagnostics.payload lineage).Decisions, fun d ->
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
        (LineageDiagnostics.payload lineage).Decisions
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
    Assert.Equal<ForeignKeyDecisionSet>(ForeignKeyPass.decisionsOf r1, ForeignKeyPass.decisionsOf r2)
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
    let direct   = ForeignKeyPass.decisionsOf (ForeignKeyPass.run sampleCatalog policy Profile.empty)
    let permuted = ForeignKeyPass.decisionsOf (ForeignKeyPass.run (perturb sampleCatalog) policy Profile.empty)
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
// Activated V1 contract — V1 OpportunityBuilder's audit-trail concern for
// successful FK decisions with a caveat (V1's
// `(CreateConstraint=true, ScriptWithNoCheck=true)` shape). Activation
// per the Skip-to-Behavioral pattern (DECISIONS 2026-05-10) once the
// Diagnostics writer landed (session 14 commit 3) and the pass return-
// type codification reached its third real test (session 16).
//
// **Note on the V1↔V2 mapping:** the session 13 Skip stub was named
// against V1's `TreatMissingDeleteRuleAsIgnore_AllowsCreation` test,
// which has no clean V2 mapping today (V2's `Reference.OnDelete` is a
// closed DU with no missing/null variant, and `isIgnoreRule` always
// returns false). The original Skip rationale assumed a V2 case that
// is unreachable from V2 fixtures. Session 16's activation redirects
// to V2's *actual* success-with-caveat variant —
// `EnforceConstraint(ScriptWithNoCheck(orphanCount))` — which is the
// genuine V2 equivalent of V1's "constraint created with audit-worthy
// caveat" shape. The substantive concern (audit-trail visibility for
// successful-but-caveated decisions) is honored; the V1↔V2 path is
// the one V2's IR actually models.
// ---------------------------------------------------------------------------

[<Fact>]
let ``V1 ForeignKey: success-with-caveat decision (ScriptWithNoCheck) emits a Warning DiagnosticEntry`` () =
    // Profile shows orphans + AllowNoCheckCreation=true ⇒ V2's
    // success-with-caveat case: the constraint IS created (with the
    // NoCheck flag) and the diagnostic notes the toleration.
    let policy = policyWithIntervention "v1-style" (mkConfig true true true)
    let profile =
        { Profile.empty with
            ForeignKeys = [ mkReality orderRef.SsKey true 8L Succeeded ] }
    let lineage = ForeignKeyPass.run sampleCatalog policy profile

    // Decision side: confirm the orderRef decision is
    // EnforceConstraint(ScriptWithNoCheck _).
    let decisions = (ForeignKeyPass.decisionsOf lineage).Decisions
    let orderDecision =
        decisions |> List.find (fun d -> d.ReferenceKey = orderRef.SsKey)
    match orderDecision.Outcome with
    | ForeignKeyOutcome.EnforceConstraint (ScriptWithNoCheck _) -> ()
    | other -> Assert.Fail(sprintf "Expected EnforceConstraint(ScriptWithNoCheck _), got %A" other)

    // Diagnostic side: the orderRef decision produced one Warning
    // entry with the success-with-caveat code prefix.
    let entries = LineageDiagnostics.entries lineage
    let orderEntry =
        entries |> List.find (fun e -> e.SsKey = Some orderRef.SsKey)
    Assert.Equal(Warning, orderEntry.Severity)
    Assert.Equal("foreignKey", orderEntry.Source)
    Assert.Equal("tightening.foreignKey.scriptWithNoCheck", orderEntry.Code)
    Assert.True(orderEntry.Metadata.ContainsKey "interventionId")
    Assert.Equal("v1-style", orderEntry.Metadata.["interventionId"])
    Assert.Contains("NOCHECK", orderEntry.Message)
