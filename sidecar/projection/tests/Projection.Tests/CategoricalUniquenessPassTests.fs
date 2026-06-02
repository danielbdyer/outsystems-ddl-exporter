module Projection.Tests.CategoricalUniquenessPassTests

open System
open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — `ForeignKeyPass.run` is private; the
// canonical surface is `.registered.Run`. Shape-compatible — both
// return `Lineage<Diagnostics<ForeignKeyDecisionSet>>`; this shim is
// a pure rename so existing assertions keep reading.
let private fkRun (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<Diagnostics<ForeignKeyDecisionSet>> =
    (ForeignKeyPass.registered policy profile).Run catalog

// Chapter A.4.7' slice η — `UniqueIndexPass.run` is private; the
// canonical surface is `.registered.Run`. Shape-compatible — both
// return `Lineage<Diagnostics<UniqueIndexDecisionSet>>`; this shim is
// a pure rename so existing assertions keep reading.
let private uiRun (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<Diagnostics<UniqueIndexDecisionSet>> =
    (UniqueIndexPass.registered policy profile).Run catalog

// Chapter A.4.7' slice η — `NullabilityPass.run` is private; the
// canonical surface is `.registered.Run`. Shape-compatible — both
// return `Lineage<Diagnostics<NullabilityDecisionSet>>`; this shim is
// a pure rename so existing assertions keep reading.
let private nullRun (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<Diagnostics<NullabilityDecisionSet>> =
    (NullabilityPass.registered policy profile).Run catalog

// Chapter A.4.7' slice η — `CategoricalUniquenessPass.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<CategoricalUniquenessDecisionSet>>`. This per-file
// shim restores the `Lineage<CategoricalUniquenessDecisionSet>` shape so
// existing assertions keep reading.
let private cuRun (catalog: Catalog) (policy: Policy) (profile: Profile) : Lineage<CategoricalUniquenessDecisionSet> =
    (CategoricalUniquenessPass.registered policy profile).Run catalog |> Lineage.map (fun d -> d.Value)

// ---------------------------------------------------------------------------
// Helpers — synthesize Categorical evidence + register the new
// intervention. Mirrors the existing four pass-test fixtures.
// ---------------------------------------------------------------------------

let private mkConfig (minDistinct: int64) : CategoricalUniquenessConfig =
    CategoricalUniquenessConfig.create minDistinct |> Result.value

let private mkProbe (sample: int64) : ProbeStatus =
    ProbeStatus.create DateTimeOffset.UnixEpoch sample Succeeded
    |> Result.value

let private mkCat
    (attrKey: SsKey)
    (frequencies: (string * int64) list)
    (distinctCount: int64) : AttributeDistribution =
    let probe = mkProbe (max 1L distinctCount)
    let cat =
        CategoricalDistribution.create attrKey frequencies distinctCount false probe
        |> Result.value
    AttributeDistribution.Categorical cat

let private policyWithIntervention
    (id: string)
    (config: CategoricalUniquenessConfig) : Policy =
    { Policy.empty with
        Tightening = { Interventions = [ CategoricalUniqueness (id, config) ] } }

let private policyWithInterventions (interventions: TighteningIntervention list) : Policy =
    { Policy.empty with Tightening = { Interventions = interventions } }

// ---------------------------------------------------------------------------
// Observable identity on empty policy.
// ---------------------------------------------------------------------------

[<Fact>]
let ``structural commitment: empty Policy yields emptyDecisionSet`` () =
    let lineage = cuRun sampleCatalog Policy.empty Profile.empty
    Assert.Equal<CategoricalUniquenessDecisionSet>(
        CategoricalUniquenessRules.emptyDecisionSet,
        lineage.Value)

[<Fact>]
let ``structural commitment: empty Policy emits no lineage events`` () =
    let lineage = cuRun sampleCatalog Policy.empty Profile.empty
    Assert.Empty(lineage.Trail)

[<Fact>]
let ``structural commitment: a policy with only the other three interventions yields empty CategoricalUniqueness output`` () =
    // The closed-DU dispatcher: each pass filters to its own variant
    // via wildcard pattern. A policy with only Nullability /
    // UniqueIndex / ForeignKey must not produce CategoricalUniqueness
    // decisions. Tests the four-variant coexistence.
    let nullCfg =
        NullabilityTighteningConfig.create 0.0m false []
        |> Result.value
    let uniqCfg = UniqueIndexTighteningConfig.create true true
    let fkCfg =
        ForeignKeyTighteningConfig.create true true false false true
    let policy =
        policyWithInterventions
            [ Nullability ("null-1", nullCfg)
              UniqueIndex ("uniq-1", uniqCfg)
              ForeignKey  ("fk-1",   fkCfg) ]
    let lineage = cuRun sampleCatalog policy Profile.empty
    Assert.Empty(lineage.Value.Decisions)
    Assert.Empty(lineage.Trail)

// ---------------------------------------------------------------------------
// One CategoricalUniqueness intervention — one decision per attribute.
// ---------------------------------------------------------------------------

[<Fact>]
let ``one intervention: yields one decision per attribute across the catalog`` () =
    let policy = policyWithIntervention "v2-distrib" (mkConfig 2L)
    let lineage = cuRun sampleCatalog policy Profile.empty
    let totalAttributes =
        Catalog.allKinds sampleCatalog |> List.sumBy (fun k -> k.Attributes.Length)
    Assert.Equal(totalAttributes, lineage.Value.Decisions.Length)

[<Fact>]
let ``one intervention: every decision references its intervention id`` () =
    let policy = policyWithIntervention "v2-distrib" (mkConfig 2L)
    let lineage = cuRun sampleCatalog policy Profile.empty
    Assert.All(lineage.Value.Decisions, fun d ->
        Assert.Equal("v2-distrib", d.InterventionId))

[<Fact>]
let ``one intervention: emits one Annotated lineage event per decision`` () =
    let policy = policyWithIntervention "v2-distrib" (mkConfig 2L)
    let lineage = cuRun sampleCatalog policy Profile.empty
    Assert.Equal(lineage.Value.Decisions.Length, lineage.Trail.Length)
    Assert.All(lineage.Trail, fun e ->
        match e.TransformKind with
        | Annotated _ -> ()
        | other -> Assert.Fail(sprintf "Expected Annotated, got %A" other))

[<Fact>]
let ``one intervention: every attribute lacking evidence surfaces as DoNotSuggest(NoCategoricalEvidence)`` () =
    let policy = policyWithIntervention "v2-distrib" (mkConfig 2L)
    // Empty Profile — no Categorical evidence anywhere.
    let lineage = cuRun sampleCatalog policy Profile.empty
    Assert.All(lineage.Value.Decisions, fun d ->
        Assert.Equal(
            CategoricalUniquenessOutcome.DoNotSuggest
                CategoricalUniquenessKeepReason.NoCategoricalEvidence,
            d.Outcome))

[<Fact>]
let ``one intervention: an attribute with distinct vocabulary surfaces as SuggestUnique`` () =
    let policy = policyWithIntervention "v2-distrib" (mkConfig 2L)
    // Country.Code has 4 distinct values, all unique in the sample.
    let evidence =
        mkCat countryCodeKey
            [ "CA", 1L; "MX", 1L; "US", 1L; "FR", 1L ] 4L
    let profile = { Profile.empty with Distributions = [ evidence ] }
    let lineage = cuRun sampleCatalog policy profile
    let countryDecision =
        lineage.Value.Decisions
        |> List.find (fun d -> d.AttributeKey = countryCodeKey)
    Assert.Equal(
        CategoricalUniquenessOutcome.SuggestUnique (EveryValueDistinct (4L, 4L)),
        countryDecision.Outcome)

// ---------------------------------------------------------------------------
// Multi-intervention fan-out (per-(attribute × intervention)).
// ---------------------------------------------------------------------------

[<Fact>]
let ``two interventions: emit decisions for every (attribute, intervention) pair`` () =
    let cfg = mkConfig 2L
    let policy =
        policyWithInterventions
            [ CategoricalUniqueness ("alpha", cfg)
              CategoricalUniqueness ("beta",  cfg) ]
    let lineage = cuRun sampleCatalog policy Profile.empty
    let totalAttributes =
        Catalog.allKinds sampleCatalog |> List.sumBy (fun k -> k.Attributes.Length)
    Assert.Equal(totalAttributes * 2, lineage.Value.Decisions.Length)

[<Fact>]
let ``two interventions: decisions tagged with their producing intervention`` () =
    let cfg = mkConfig 2L
    let policy =
        policyWithInterventions
            [ CategoricalUniqueness ("alpha", cfg)
              CategoricalUniqueness ("beta",  cfg) ]
    let lineage = cuRun sampleCatalog policy Profile.empty
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
// Five-strategy coexistence — closed-DU dispatcher continues to filter
// correctly with five variants registered. The codification's reach
// validated.
// ---------------------------------------------------------------------------

[<Fact>]
let ``coexistence: CategoricalUniquenessPass ignores the four other variants in a mixed policy`` () =
    let nullCfg =
        NullabilityTighteningConfig.create 0.0m false []
        |> Result.value
    let uniqCfg = UniqueIndexTighteningConfig.create true true
    let fkCfg =
        ForeignKeyTighteningConfig.create true true false false true
    let catUniqCfg = mkConfig 2L
    let policy =
        policyWithInterventions
            [ Nullability           ("null-1", nullCfg)
              UniqueIndex           ("uniq-1", uniqCfg)
              ForeignKey            ("fk-1",   fkCfg)
              CategoricalUniqueness ("cu-1",   catUniqCfg) ]
    let lineage = cuRun sampleCatalog policy Profile.empty
    let totalAttributes =
        Catalog.allKinds sampleCatalog |> List.sumBy (fun k -> k.Attributes.Length)
    Assert.Equal(totalAttributes, lineage.Value.Decisions.Length)
    Assert.All(lineage.Value.Decisions, fun d ->
        Assert.Equal("cu-1", d.InterventionId))

[<Fact>]
let ``coexistence: NullabilityPass ignores CategoricalUniqueness interventions`` () =
    let nullCfg =
        NullabilityTighteningConfig.create 0.0m false []
        |> Result.value
    let catUniqCfg = mkConfig 2L
    let policy =
        policyWithInterventions
            [ Nullability           ("null-1", nullCfg)
              CategoricalUniqueness ("cu-1",   catUniqCfg) ]
    let lineage = nullRun sampleCatalog policy Profile.empty
    Assert.All((LineageDiagnostics.payload lineage).Decisions, fun d ->
        Assert.Equal("null-1", d.InterventionId))

[<Fact>]
let ``coexistence: UniqueIndexPass ignores CategoricalUniqueness interventions`` () =
    let uniqCfg = UniqueIndexTighteningConfig.create true true
    let catUniqCfg = mkConfig 2L
    let policy =
        policyWithInterventions
            [ UniqueIndex           ("uniq-1", uniqCfg)
              CategoricalUniqueness ("cu-1",   catUniqCfg) ]
    let lineage = uiRun sampleCatalog policy Profile.empty
    Assert.All((LineageDiagnostics.payload lineage).Decisions, fun d ->
        Assert.Equal("uniq-1", d.InterventionId))

[<Fact>]
let ``coexistence: ForeignKeyPass ignores CategoricalUniqueness interventions`` () =
    let fkCfg =
        ForeignKeyTighteningConfig.create true true false false true
    let catUniqCfg = mkConfig 2L
    let policy =
        policyWithInterventions
            [ ForeignKey            ("fk-1", fkCfg)
              CategoricalUniqueness ("cu-1", catUniqCfg) ]
    let lineage = fkRun sampleCatalog policy Profile.empty
    Assert.All((LineageDiagnostics.payload lineage).Decisions, fun d ->
        Assert.Equal("fk-1", d.InterventionId))

// ---------------------------------------------------------------------------
// T1 / permutation invariance / lineage discipline (mirrors the four
// existing pass-test files).
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: CategoricalUniquenessPass is deterministic on the triple`` () =
    let policy = policyWithIntervention "v2-distrib" (mkConfig 2L)
    let r1 = cuRun sampleCatalog policy Profile.empty
    let r2 = cuRun sampleCatalog policy Profile.empty
    Assert.Equal<CategoricalUniquenessDecisionSet>(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

[<Property>]
let ``contract: CategoricalUniquenessPass is invariant under input permutation``
    (reverseModules: bool) (reverseKinds: bool) (reverseAttrs: bool) =
    let perturb (c: Catalog) : Catalog =
        let withModules =
            { Modules =
                (c.Modules
                 |> List.map (fun m ->
                     let withKinds =
                         m.Kinds
                         |> List.map (fun k ->
                             if reverseAttrs then { k with Attributes = List.rev k.Attributes }
                             else k)
                     if reverseKinds then { m with Kinds = List.rev withKinds }
                     else { m with Kinds = withKinds }))
              Sequences = c.Sequences }
        if reverseModules then { withModules with Modules = List.rev withModules.Modules }
        else withModules
    let policy = policyWithIntervention "v2-distrib" (mkConfig 2L)
    let direct   = (cuRun sampleCatalog policy Profile.empty).Value
    let permuted = (cuRun (perturb sampleCatalog) policy Profile.empty).Value
    direct = permuted

[<Fact>]
let ``A23: events carry pass version and name`` () =
    let policy = policyWithIntervention "v2-distrib" (mkConfig 2L)
    let lineage = cuRun sampleCatalog policy Profile.empty
    Assert.All(lineage.Trail, fun e ->
        Assert.Equal(CategoricalUniquenessPass.version, e.PassVersion)
        Assert.Equal("categoricalUniqueness", e.PassName))

[<Fact>]
let ``A25: every emitted event references a real attribute SsKey`` () =
    let policy = policyWithIntervention "v2-distrib" (mkConfig 2L)
    let lineage = cuRun sampleCatalog policy Profile.empty
    let attributeKeys =
        Catalog.allKinds sampleCatalog
        |> List.collect (fun k -> k.Attributes |> List.map (fun a -> a.SsKey))
        |> Set.ofList
    Assert.All(lineage.Trail, fun e ->
        Assert.Contains(e.SsKey, attributeKeys))

// ---------------------------------------------------------------------------
// Catalog passes through unchanged.
// ---------------------------------------------------------------------------

// Slice 10 (2026-06-02 audit): "catalog passes through unchanged"
// test pruned. The pass return type is `Lineage<DecisionSet>` — the
// signature does not return a transformed catalog, so input
// preservation is a *signature-level* guarantee. The test restated
// the signature rather than checking a contract V2 owns.
