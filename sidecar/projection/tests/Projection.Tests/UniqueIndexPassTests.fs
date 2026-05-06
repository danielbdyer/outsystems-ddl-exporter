module Projection.Tests.UniqueIndexPassTests

open System
open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Helpers — small named constructors for building catalogs with unique
// indexes. The base sampleCatalog has Indexes = []; tests that need
// indexes synthesize a catalog with them attached.
// ---------------------------------------------------------------------------

let private ssKey (s: string) : SsKey = SsKey.original s |> Result.value
let private name  (s: string) : Name  = Name.create s   |> Result.value

let private mkIndex
    (key: string)
    (columns: SsKey list)
    (isUnique: bool)
    : Index =
    { SsKey        = ssKey key
      Name         = name "IX"
      Columns      = columns
      IsUnique     = isUnique
      IsPrimaryKey = false }

let private mkConfig (single: bool) (composite: bool) : UniqueIndexTighteningConfig =
    UniqueIndexTighteningConfig.create single composite

let private policyWithIntervention
    (id: string)
    (config: UniqueIndexTighteningConfig)
    : Policy =
    { Policy.empty with
        Tightening = { Interventions = [ UniqueIndex (id, config) ] } }

let private policyWithInterventions (interventions: TighteningIntervention list) : Policy =
    { Policy.empty with Tightening = { Interventions = interventions } }

/// Catalog with two indexes on Customer: one already-unique single-column,
/// one non-unique composite. Order and Country carry one non-unique
/// single-column index each. Total: four indexes across three kinds.
let private indexedCatalog : Catalog =
    let customerSingleUnique =
        mkIndex "OS_IDX_Customer_Name_U" [ customerNameKey ] true
    let customerComposite =
        mkIndex "OS_IDX_Customer_NameTenant" [ customerNameKey; customerTenantKey ] false
    let orderSingle =
        mkIndex "OS_IDX_Order_CustomerId" [ orderCustomerFkKey ] false
    let countrySingle =
        mkIndex "OS_IDX_Country_Code" [ countryCodeKey ] false
    let customer' = { customer with Indexes = [ customerSingleUnique; customerComposite ] }
    let order'    = { order    with Indexes = [ orderSingle ] }
    let country'  = { country  with Indexes = [ countrySingle ] }
    let salesModule' =
        { salesModule with Kinds = [ customer'; order'; country' ] }
    { Modules = [ salesModule' ] }

let private allIndexes (c: Catalog) : Index list =
    Catalog.allKinds c |> List.collect (fun k -> k.Indexes)

// ---------------------------------------------------------------------------
// Observable identity on empty policy — the structural commitment
// (DECISIONS 2026-05-09). When the policy has no UniqueIndex
// interventions, the pass produces emptyDecisionSet with no events.
// ---------------------------------------------------------------------------

[<Fact>]
let ``structural commitment: empty Policy yields emptyDecisionSet`` () =
    let lineage = UniqueIndexPass.run indexedCatalog Policy.empty Profile.empty
    Assert.Equal<UniqueIndexDecisionSet>(
        UniqueIndexRules.emptyDecisionSet,
        lineage.Value)

[<Fact>]
let ``structural commitment: empty Policy emits no lineage events`` () =
    let lineage = UniqueIndexPass.run indexedCatalog Policy.empty Profile.empty
    Assert.Empty(lineage.Trail)

[<Fact>]
let ``structural commitment: a policy with only Nullability interventions yields empty UniqueIndex output`` () =
    // The closed-DU dispatcher: each pass filters to its own variant.
    // A Nullability-only policy must not produce UniqueIndex decisions.
    let nullCfg =
        NullabilityTighteningConfig.create 0.0m false []
        |> Result.value
    let policy =
        policyWithInterventions [ Nullability ("v1-style", nullCfg) ]
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
    Assert.Empty(lineage.Value.Decisions)
    Assert.Empty(lineage.Trail)

[<Fact>]
let ``structural commitment: empty Tightening with non-empty other axes still yields empty result`` () =
    let policy =
        { Policy.empty with
            Selection = IncludeOnly (Set.singleton customerKey)
            Emission  = EmissionPolicy.combined
            Insertion = InsertNew }
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
    Assert.Empty(lineage.Value.Decisions)
    Assert.Empty(lineage.Trail)

// ---------------------------------------------------------------------------
// One UniqueIndex intervention — the pass emits one decision per
// index on every kind, plus one Annotated lineage event per decision.
// ---------------------------------------------------------------------------

[<Fact>]
let ``one intervention: yields one decision per index across the catalog`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true)
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
    let totalIndexes = (allIndexes indexedCatalog).Length
    Assert.Equal(totalIndexes, lineage.Value.Decisions.Length)

[<Fact>]
let ``one intervention: every decision references its intervention id`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true)
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
    Assert.All(lineage.Value.Decisions, fun d ->
        Assert.Equal("v1-style", d.InterventionId))

[<Fact>]
let ``one intervention: emits one Annotated lineage event per decision`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true)
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
    Assert.Equal(lineage.Value.Decisions.Length, lineage.Trail.Length)
    Assert.All(lineage.Trail, fun e ->
        match e.TransformKind with
        | Annotated _ -> ()
        | other -> Assert.Fail(sprintf "Expected Annotated, got %A" other))

[<Fact>]
let ``one intervention: lineage event detail names the intervention id and an outcome category`` () =
    let policy = policyWithIntervention "v1-style-2026" (mkConfig true true)
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
    let categories = [ "EnforceUnique"; "DoNotEnforce" ]
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

[<Fact>]
let ``one intervention: AlreadyUnique decisions surface for catalog-declared unique indexes`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true)
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
    let alreadyUniqueDecisions =
        lineage.Value.Decisions
        |> List.filter (fun d ->
            match d.Outcome with
            | UniqueIndexOutcome.EnforceUnique AlreadyUnique -> true
            | _ -> false)
    // Exactly one index is catalog-declared unique in the fixture.
    Assert.Equal(1, alreadyUniqueDecisions.Length)

// ---------------------------------------------------------------------------
// Two UniqueIndex interventions — fan-out per (index × intervention).
// ---------------------------------------------------------------------------

[<Fact>]
let ``two interventions: emit decisions for every (index, intervention) pair`` () =
    let cfg = mkConfig true true
    let policy =
        policyWithInterventions
            [ UniqueIndex ("alpha", cfg)
              UniqueIndex ("beta",  cfg) ]
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
    let totalIndexes = (allIndexes indexedCatalog).Length
    Assert.Equal(totalIndexes * 2, lineage.Value.Decisions.Length)

[<Fact>]
let ``two interventions: decisions are tagged with their producing intervention`` () =
    let cfg = mkConfig true true
    let policy =
        policyWithInterventions
            [ UniqueIndex ("alpha", cfg)
              UniqueIndex ("beta",  cfg) ]
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
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
// Coexistence: a policy with both Nullability and UniqueIndex
// interventions — each pass filters to its own variant; the two outputs
// are independent.
// ---------------------------------------------------------------------------

[<Fact>]
let ``coexistence: UniqueIndexPass ignores Nullability interventions in a mixed policy`` () =
    let nullCfg =
        NullabilityTighteningConfig.create 0.0m false []
        |> Result.value
    let uniqCfg = mkConfig true true
    let policy =
        policyWithInterventions
            [ Nullability ("null-1", nullCfg)
              UniqueIndex ("uniq-1", uniqCfg) ]
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
    let totalIndexes = (allIndexes indexedCatalog).Length
    Assert.Equal(totalIndexes, lineage.Value.Decisions.Length)
    Assert.All(lineage.Value.Decisions, fun d ->
        Assert.Equal("uniq-1", d.InterventionId))

[<Fact>]
let ``coexistence: NullabilityPass ignores UniqueIndex interventions in a mixed policy`` () =
    let nullCfg =
        NullabilityTighteningConfig.create 0.0m false []
        |> Result.value
    let uniqCfg = mkConfig true true
    let policy =
        policyWithInterventions
            [ Nullability ("null-1", nullCfg)
              UniqueIndex ("uniq-1", uniqCfg) ]
    let lineage = NullabilityPass.run indexedCatalog policy Profile.empty
    Assert.All(lineage.Value.Decisions, fun d ->
        Assert.Equal("null-1", d.InterventionId))

// ---------------------------------------------------------------------------
// PolicyDisabled gates surface as DoNotEnforce decisions through the pass.
// ---------------------------------------------------------------------------

[<Fact>]
let ``policy gates: single-column toggle off produces PolicyDisabled for non-unique single-column indexes`` () =
    // Single off, composite on.
    let policy = policyWithIntervention "gated" (mkConfig false true)
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
    // Non-unique single-column indexes: orderSingle, countrySingle (two).
    let policyDisabledCount =
        lineage.Value.Decisions
        |> List.filter (fun d ->
            match d.Outcome with
            | UniqueIndexOutcome.DoNotEnforce PolicyDisabled -> true
            | _ -> false)
        |> List.length
    Assert.Equal(2, policyDisabledCount)

// ---------------------------------------------------------------------------
// T1 determinism — same triple ⇒ identical output (decisions + trail).
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: UniqueIndexPass is deterministic on the triple`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true)
    let r1 = UniqueIndexPass.run indexedCatalog policy Profile.empty
    let r2 = UniqueIndexPass.run indexedCatalog policy Profile.empty
    Assert.Equal<UniqueIndexDecisionSet>(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

// ---------------------------------------------------------------------------
// Permutation invariance — sort/order discipline is structural. The pass
// output is invariant under input-list reordering at every level
// (modules, kinds, indexes).
// ---------------------------------------------------------------------------

[<Property>]
let ``contract: UniqueIndexPass is invariant under input permutation``
    (reverseModules: bool) (reverseKinds: bool) (reverseIndexes: bool) =
    let perturb (c: Catalog) : Catalog =
        let withModules =
            { Modules =
                c.Modules
                |> List.map (fun m ->
                    let withKinds =
                        m.Kinds
                        |> List.map (fun k ->
                            if reverseIndexes then { k with Indexes = List.rev k.Indexes }
                            else k)
                    if reverseKinds then { m with Kinds = List.rev withKinds }
                    else { m with Kinds = withKinds }) }
        if reverseModules then { withModules with Modules = List.rev withModules.Modules }
        else withModules
    let policy = policyWithIntervention "v1-style" (mkConfig true true)
    let direct   = (UniqueIndexPass.run indexedCatalog policy Profile.empty).Value
    let permuted = (UniqueIndexPass.run (perturb indexedCatalog) policy Profile.empty).Value
    direct = permuted

// ---------------------------------------------------------------------------
// A23 / A25: lineage events carry pass version + name and reference real
// index identities.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A23: events carry pass version and name`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true)
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
    Assert.All(lineage.Trail, fun e ->
        Assert.Equal(UniqueIndexPass.version, e.PassVersion)
        Assert.Equal("uniqueIndex", e.PassName))

[<Fact>]
let ``A25: every emitted event references a real index SsKey`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true)
    let lineage = UniqueIndexPass.run indexedCatalog policy Profile.empty
    let indexKeys =
        allIndexes indexedCatalog
        |> List.map (fun ix -> ix.SsKey)
        |> Set.ofList
    Assert.All(lineage.Trail, fun e ->
        Assert.Contains(e.SsKey, indexKeys))

// ---------------------------------------------------------------------------
// The catalog is unchanged — the pass produces a value, not a
// transformed catalog. Structural by signature
// (run : Catalog -> Policy -> Profile -> Lineage<UniqueIndexDecisionSet>).
// ---------------------------------------------------------------------------

[<Fact>]
let ``catalog passes through unchanged: structural by signature`` () =
    let policy = policyWithIntervention "v1-style" (mkConfig true true)
    let _ = UniqueIndexPass.run indexedCatalog policy Profile.empty
    // Sanity: indexedCatalog still has its expected shape.
    Assert.Equal(3, (Catalog.allKinds indexedCatalog).Length)
    Assert.Equal(4, (allIndexes indexedCatalog).Length)
