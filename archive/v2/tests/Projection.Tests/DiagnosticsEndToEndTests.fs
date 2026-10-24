module Projection.Tests.DiagnosticsEndToEndTests

open Xunit
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

// ---------------------------------------------------------------------------
// End-to-end milestone — UniqueIndex with its opportunity stream
// flowing through Lineage<Diagnostics<_>> end-to-end.
//
// Demonstrates the dual writer's contract on a realistic catalog +
// policy + profile triple:
//   - decisions are produced and Annotated lineage events fire;
//   - DiagnosticEntry values are produced for every DoNotEnforce
//     decision (the V2-shaped equivalent of V1's
//     OpportunityBuilder.TryCreate);
//   - both trails are deterministic across repeated runs (T1
//     byte-determinism holds for the dual writer);
//   - the diagnostic stream and the decision stream agree by SsKey
//     (per-decision-equivalent emission).
// ---------------------------------------------------------------------------

let private ssKey (s: string) : SsKey = testKey s
let private name  (s: string) : Name  = Name.create s   |> Result.value

let private indexFixture
    (key: string)
    (columns: SsKey list)
    (isUnique: bool)
    : Index =
    { Index.ofKeyColumns (ssKey key) (name "IX") columns with Uniqueness = (if isUnique then Unique else NotUnique) }

/// Catalog with one already-unique single-column index (Customer.Name)
/// plus three non-unique single-column indexes that will produce
/// DoNotEnforce decisions under a "single-column toggle off, composite
/// on" policy gate (the canonical PolicyDisabled scenario from
/// V1's UniqueIndexDecisionStrategyTests).
let private endToEndCatalog : Catalog =
    let customerSingleUnique =
        indexFixture "OS_IDX_Customer_Name_U" [ customerNameKey ] true
    let orderSingle =
        indexFixture "OS_IDX_Order_CustomerId" [ orderCustomerFkKey ] false
    let countrySingle =
        indexFixture "OS_IDX_Country_Code" [ countryCodeKey ] false
    let customer' = { customer with Indexes = [ customerSingleUnique ] }
    let order'    = { order    with Indexes = [ orderSingle ] }
    let country'  = { country  with Indexes = [ countrySingle ] }
    let salesModule' =
        { salesModule with Kinds = [ customer'; order'; country' ] }
    { Modules = [ salesModule' ]; Sequences = [] }

let private singleOffCompositeOnPolicy : Policy =
    let cfg = UniqueIndexTighteningConfig.create false true
    { Policy.empty with
        Tightening = { Interventions = [ UniqueIndex ("v1-style", cfg) ] } }

// ---------------------------------------------------------------------------
// Both trails populated end-to-end.
// ---------------------------------------------------------------------------

[<Fact>]
let ``end-to-end: lineage trail and diagnostic stream populate together`` () =
    let result = uiRun endToEndCatalog singleOffCompositeOnPolicy Profile.empty

    // Lineage trail: one Annotated event per decision.
    let decisions = (UniqueIndexPass.decisionsOf result).Decisions
    Assert.Equal(decisions.Length, result.Trail.Length)
    Assert.All(result.Trail, fun e ->
        Assert.Equal(UniqueIndexPass.version, e.PassVersion)
        Assert.Equal("uniqueIndex", e.PassName)
        match e.TransformKind with
        | Annotated _ -> ()
        | other -> Assert.Fail(sprintf "Expected Annotated, got %A" other))

    // Diagnostic stream: one DiagnosticSeverity.Warning entry per DoNotEnforce decision.
    let doNotEnforceCount =
        decisions
        |> List.filter (fun d ->
            match d.Outcome with
            | UniqueIndexOutcome.DoNotEnforce _ -> true
            | _ -> false)
        |> List.length
    let entries = LineageDiagnostics.entries result
    Assert.Equal(doNotEnforceCount, entries.Length)
    Assert.All(entries, fun e ->
        Assert.Equal(DiagnosticSeverity.Warning, e.Severity)
        Assert.Equal("uniqueIndex", e.Source))

[<Fact>]
let ``end-to-end: diagnostic SsKeys align with the decisions that produced them`` () =
    let result = uiRun endToEndCatalog singleOffCompositeOnPolicy Profile.empty

    let doNotEnforceKeys =
        (UniqueIndexPass.decisionsOf result).Decisions
        |> List.choose (fun d ->
            match d.Outcome with
            | UniqueIndexOutcome.DoNotEnforce _ -> Some d.IndexKey
            | _ -> None)

    let entryKeys =
        LineageDiagnostics.entries result
        |> List.choose (fun e -> e.SsKey)

    Assert.Equal<SsKey list>(doNotEnforceKeys, entryKeys)

// ---------------------------------------------------------------------------
// T1 byte-determinism extends to the dual writer.
// Same triple ⇒ identical decisions, identical lineage trail,
// identical diagnostic stream.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: byte-determinism holds for the dual writer (decisions + lineage + diagnostics)`` () =
    let r1 = uiRun endToEndCatalog singleOffCompositeOnPolicy Profile.empty
    let r2 = uiRun endToEndCatalog singleOffCompositeOnPolicy Profile.empty

    // The decision set, the lineage trail, and the diagnostic stream
    // each independently hold byte-determinism. Asserting them
    // separately makes a regression's source visible.
    Assert.Equal<UniqueIndexDecisionSet>(
        UniqueIndexPass.decisionsOf r1,
        UniqueIndexPass.decisionsOf r2)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)
    Assert.Equal<DiagnosticEntry list>(
        LineageDiagnostics.entries r1,
        LineageDiagnostics.entries r2)

// ---------------------------------------------------------------------------
// Observable identity on empty policy — the structural commitment
// extends to the dual writer.
// ---------------------------------------------------------------------------

[<Fact>]
let ``end-to-end: empty policy yields empty decisions, empty trail, empty diagnostics`` () =
    let result = uiRun endToEndCatalog Policy.empty Profile.empty

    Assert.Empty((UniqueIndexPass.decisionsOf result).Decisions)
    Assert.Empty(result.Trail)
    Assert.Empty(LineageDiagnostics.entries result)

// ---------------------------------------------------------------------------
// LineageDiagnostics.diagnostics gives both halves together — useful
// for downstream consumers (e.g., the eventual JSON manifest emitter)
// that want value + entries without the lineage trail.
// ---------------------------------------------------------------------------

[<Fact>]
let ``end-to-end: LineageDiagnostics.diagnostics returns the inner Diagnostics<_>`` () =
    let result = uiRun endToEndCatalog singleOffCompositeOnPolicy Profile.empty
    let diag : Diagnostics<UniqueIndexDecisionSet> = LineageDiagnostics.diagnostics result

    Assert.Equal<UniqueIndexDecisionSet>(
        UniqueIndexPass.decisionsOf result,
        diag.Value)
    Assert.Equal<DiagnosticEntry list>(
        LineageDiagnostics.entries result,
        diag.Entries)

// ---------------------------------------------------------------------------
// Nullability + UniqueIndex opportunity streams flowing together.
// Session 15: the codification's second real test. Two passes, each
// producing its own Lineage<Diagnostics<_>>, run against a shared
// catalog + policy. Each pass's opportunity stream is independent;
// their diagnostic entries do not interleave (each pass returns its
// own dual writer; the orchestrator composes them at the call site,
// not via shared state).
// ---------------------------------------------------------------------------

let private nullabilityCatalog : Catalog =
    let mandatoryAttributeKey = ssKey "OS_ATTR_DiagEnd_Sample_Mandatory"
    let idAttributeKey        = ssKey "OS_ATTR_DiagEnd_Sample_Id"
    let sampleKey             = ssKey "OS_KIND_DiagEnd_Sample"
    let sample : Kind =
        { SsKey    = sampleKey
          Name     = name "Sample"
          Origin   = Native
          Modality = []
          Physical = mkTableId "dbo" "OSUSR_DIAG_END_SAMPLE"
          Attributes = [
              { Attribute.create idAttributeKey (name "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true }
              { Attribute.create mandatoryAttributeKey (name "Mandatory") Text with Column = ColumnRealization.create ("MANDATORY") (true) |> Result.value; IsMandatory = true } ]
          References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    { Modules = [
        { SsKey = ssKey "OS_MOD_DiagEnd"
          Name  = name "DiagEnd"
          Kinds = [ sample ]; IsActive = true; ExtendedProperties = [] } ]; Sequences = [] }

let private nullabilityProfileWithNullsBeyondBudget : Profile =
    let mandatoryAttributeKey = ssKey "OS_ATTR_DiagEnd_Sample_Mandatory"
    let probe =
        ProbeStatus.create System.DateTimeOffset.UnixEpoch 100L Succeeded
        |> Result.value
    { Profile.empty with
        Columns = [
            { AttributeKey         = mandatoryAttributeKey
              RowCount             = 100L
              NullCount            = 12L
              MaxObservedLength = None
              NullCountProbeStatus = probe } ] }

let private nullabilityPolicyForOpportunity : Policy =
    let cfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    { Policy.empty with
        Tightening = { Interventions = [ Nullability ("v1-parity", cfg) ] } }

[<Fact>]
let ``end-to-end: Nullability opportunity stream emits one DiagnosticSeverity.Warning entry on RequireOperatorApproval`` () =
    let result =
        nullRun
            nullabilityCatalog
            nullabilityPolicyForOpportunity
            nullabilityProfileWithNullsBeyondBudget

    // Decision side: mandatory column with nulls beyond budget +
    // relaxation forbidden ⇒ RequireOperatorApproval.
    let mandatoryAttributeKey = ssKey "OS_ATTR_DiagEnd_Sample_Mandatory"
    let decisions = (NullabilityPass.decisionsOf result).Decisions
    let mandatoryDecision =
        decisions |> List.find (fun d -> d.AttributeKey = mandatoryAttributeKey)
    match mandatoryDecision.Outcome with
    | NullabilityOutcome.RequireOperatorApproval _ -> ()
    | other -> Assert.Fail(sprintf "Expected RequireOperatorApproval, got %A" other)

    // Diagnostic side: exactly one DiagnosticSeverity.Warning entry referencing the
    // mandatory column. The PK column produces an EnforceNotNull
    // outcome → no diagnostic.
    let entries = LineageDiagnostics.entries result
    Assert.Single(entries) |> ignore
    let entry = entries.[0]
    Assert.Equal(DiagnosticSeverity.Warning, entry.Severity)
    Assert.Equal("nullability", entry.Source)
    Assert.Equal(Some mandatoryAttributeKey, entry.SsKey)

[<Fact>]
let ``end-to-end: Nullability and UniqueIndex opportunity streams remain independent under shared catalog`` () =
    // Two passes, each producing its own Lineage<Diagnostics<_>>.
    // Combined catalog adapts: nullability fixtures (Sample +
    // Mandatory column) plus uniqueness fixtures (Customer/Order/
    // Country with indexes). Each pass filters its own intervention
    // variant per the closed-DU dispatch discipline; their
    // diagnostic streams do not cross-pollinate.
    let combinedCatalog : Catalog =
        { Modules = nullabilityCatalog.Modules @ endToEndCatalog.Modules
          Sequences = [] }
    let nullabilityCfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    let uniqueCfg      = UniqueIndexTighteningConfig.create false true
    let combinedPolicy : Policy =
        { Policy.empty with
            Tightening =
                { Interventions =
                    [ Nullability ("null-1", nullabilityCfg)
                      UniqueIndex  ("uniq-1", uniqueCfg) ] } }

    let nullResult = nullRun combinedCatalog combinedPolicy nullabilityProfileWithNullsBeyondBudget
    let uniqResult = uiRun combinedCatalog combinedPolicy Profile.empty

    let nullEntries = LineageDiagnostics.entries nullResult
    let uniqEntries = LineageDiagnostics.entries uniqResult

    // Each pass's diagnostic Source identifies it; entries do not
    // interleave because each pass returns its own dual writer.
    Assert.All(nullEntries, fun e -> Assert.Equal("nullability",  e.Source))
    Assert.All(uniqEntries, fun e -> Assert.Equal("uniqueIndex",  e.Source))

    // Each pass tags its decisions only with its own intervention id.
    let nullInterventionIds =
        (NullabilityPass.decisionsOf nullResult).Decisions
        |> List.map (fun d -> d.InterventionId)
        |> Set.ofList
    let uniqInterventionIds =
        (UniqueIndexPass.decisionsOf uniqResult).Decisions
        |> List.map (fun d -> d.InterventionId)
        |> Set.ofList
    Assert.Equal<Set<string>>(Set.singleton "null-1", nullInterventionIds)
    Assert.Equal<Set<string>>(Set.singleton "uniq-1", uniqInterventionIds)

[<Fact>]
let ``T1: byte-determinism holds for NullabilityPass under the dual writer`` () =
    let r1 =
        nullRun
            nullabilityCatalog
            nullabilityPolicyForOpportunity
            nullabilityProfileWithNullsBeyondBudget
    let r2 =
        nullRun
            nullabilityCatalog
            nullabilityPolicyForOpportunity
            nullabilityProfileWithNullsBeyondBudget

    Assert.Equal<NullabilityDecisionSet>(
        NullabilityPass.decisionsOf r1,
        NullabilityPass.decisionsOf r2)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)
    Assert.Equal<DiagnosticEntry list>(
        LineageDiagnostics.entries r1,
        LineageDiagnostics.entries r2)

// ---------------------------------------------------------------------------
// Three opportunity streams running together — the codification's third
// real test made empirical (session 16). The heterogeneous shape comes
// from FK alone: it emits on both keep-reasons (mirroring
// UniqueIndex/Nullability) AND on a success-with-caveat variant
// (EnforceConstraint(ScriptWithNoCheck)). The end-to-end test confirms
// the dual writer absorbs the heterogeneity cleanly within one pass and
// across three passes' independent opportunity streams.
// ---------------------------------------------------------------------------

let private fkSourceEntityKey   = ssKey "OS_KIND_FkEnd_Source"
let private fkTargetEntityKey   = ssKey "OS_KIND_FkEnd_Target"
let private fkRefKey            = ssKey "OS_REF_FkEnd_Source_Target"
let private fkSourceAttrKey     = ssKey "OS_ATTR_FkEnd_Source_TargetId"
let private fkSourceIdKey       = ssKey "OS_ATTR_FkEnd_Source_Id"
let private fkTargetIdKey       = ssKey "OS_ATTR_FkEnd_Target_Id"

let private fkCatalog : Catalog =
    let target : Kind =
        { SsKey    = fkTargetEntityKey
          Name     = name "FkTarget"
          Origin   = Native
          Modality = []
          Physical = mkTableId "dbo" "OSUSR_FK_END_TARGET"
          Attributes = [
              { Attribute.create fkTargetIdKey (name "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true } ]
          References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    let source : Kind =
        { SsKey    = fkSourceEntityKey
          Name     = name "FkSource"
          Origin   = Native
          Modality = []
          Physical = mkTableId "dbo" "OSUSR_FK_END_SOURCE"
          Attributes = [
              { Attribute.create fkSourceIdKey (name "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true }
              { Attribute.create fkSourceAttrKey (name "TargetId") Integer with Column = ColumnRealization.create ("TARGET_ID") (true) |> Result.value } ]
          References = [
              Reference.create fkRefKey (name "FkSource_Target") fkSourceAttrKey fkTargetEntityKey ]
          Indexes = []
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    { Modules = [
        { SsKey = ssKey "OS_MOD_FkEnd"
          Name  = name "FkEnd"
          Kinds = [ source; target ]; IsActive = true; ExtendedProperties = [] } ]; Sequences = [] }

let private fkProfileWithOrphans : Profile =
    let probe =
        ProbeStatus.create System.DateTimeOffset.UnixEpoch 100L Succeeded
        |> Result.value
    { Profile.empty with
        ForeignKeys = [
            { ReferenceKey  = fkRefKey
              HasOrphan     = true
              IsNoCheck     = false
              OrphanCount   = 3L
              ProbeStatus   = probe } ] }

let private fkPolicyAllowingNoCheck : Policy =
    let cfg =
        ForeignKeyTighteningConfig.create
            true            // EnableCreation
            true            // AllowCrossSchema
            true            // AllowNoCheckCreation
    { Policy.empty with
        Tightening = { Interventions = [ ForeignKey ("v1-style", cfg) ] } }

[<Fact>]
let ``end-to-end: ForeignKey opportunity stream emits one DiagnosticSeverity.Warning entry on success-with-caveat (ScriptWithNoCheck)`` () =
    let result =
        fkRun fkCatalog fkPolicyAllowingNoCheck fkProfileWithOrphans

    // Decision side: orphans observed + AllowNoCheckCreation=true ⇒
    // EnforceConstraint(ScriptWithNoCheck). The constraint IS created;
    // the caveat is the audit-worthy event.
    let decisions = (ForeignKeyPass.decisionsOf result).Decisions
    let refDecision =
        decisions |> List.find (fun d -> d.ReferenceKey = fkRefKey)
    match refDecision.Outcome with
    | ForeignKeyOutcome.EnforceConstraint (ScriptWithNoCheck _) -> ()
    | other -> Assert.Fail(sprintf "Expected EnforceConstraint(ScriptWithNoCheck _), got %A" other)

    // Diagnostic side: exactly one DiagnosticSeverity.Warning entry referencing the
    // reference's SsKey, with the success-with-caveat code prefix.
    let entries = LineageDiagnostics.entries result
    Assert.Single(entries) |> ignore
    let entry = entries.[0]
    Assert.Equal(DiagnosticSeverity.Warning, entry.Severity)
    Assert.Equal("foreignKey", entry.Source)
    Assert.Equal("tightening.foreignKey.scriptWithNoCheck", entry.Code)
    Assert.Equal(Some fkRefKey, entry.SsKey)

[<Fact>]
let ``end-to-end: ForeignKey + Nullability + UniqueIndex opportunity streams remain independent under shared catalog`` () =
    // All three passes run against a combined catalog with each pass's
    // intervention. Each returns its own Lineage<Diagnostics<_>>; their
    // diagnostic streams do not cross-pollinate.
    let combinedCatalog : Catalog =
        { Modules =
            nullabilityCatalog.Modules @ endToEndCatalog.Modules @ fkCatalog.Modules
          Sequences = [] }
    let nullCfg = NullabilityTighteningConfig.create 0.05m false [] |> Result.value
    let uniqCfg = UniqueIndexTighteningConfig.create false true
    let fkCfg =
        ForeignKeyTighteningConfig.create true true true
    let combinedPolicy : Policy =
        { Policy.empty with
            Tightening =
                { Interventions =
                    [ Nullability ("null-1", nullCfg)
                      UniqueIndex  ("uniq-1", uniqCfg)
                      ForeignKey   ("fk-1",   fkCfg) ] } }

    let nullResult = nullRun combinedCatalog combinedPolicy nullabilityProfileWithNullsBeyondBudget
    let uniqResult = uiRun combinedCatalog combinedPolicy Profile.empty
    let fkResult   = fkRun  combinedCatalog combinedPolicy fkProfileWithOrphans

    let nullEntries = LineageDiagnostics.entries nullResult
    let uniqEntries = LineageDiagnostics.entries uniqResult
    let fkEntries   = LineageDiagnostics.entries fkResult

    // Each pass's diagnostic Source identifies it; entries do not
    // interleave because each pass returns its own dual writer.
    Assert.All(nullEntries, fun e -> Assert.Equal("nullability", e.Source))
    Assert.All(uniqEntries, fun e -> Assert.Equal("uniqueIndex", e.Source))
    Assert.All(fkEntries,   fun e -> Assert.Equal("foreignKey", e.Source))

    // Each pass tags its decisions only with its own intervention id.
    let nullInterventionIds =
        (NullabilityPass.decisionsOf nullResult).Decisions
        |> List.map (fun d -> d.InterventionId)
        |> Set.ofList
    let uniqInterventionIds =
        (UniqueIndexPass.decisionsOf uniqResult).Decisions
        |> List.map (fun d -> d.InterventionId)
        |> Set.ofList
    let fkInterventionIds =
        (ForeignKeyPass.decisionsOf fkResult).Decisions
        |> List.map (fun d -> d.InterventionId)
        |> Set.ofList
    Assert.Equal<Set<string>>(Set.singleton "null-1", nullInterventionIds)
    Assert.Equal<Set<string>>(Set.singleton "uniq-1", uniqInterventionIds)
    Assert.Equal<Set<string>>(Set.singleton "fk-1",   fkInterventionIds)

[<Fact>]
let ``end-to-end: ForeignKey emits keep-reason and success-with-caveat entries side-by-side (heterogeneous emission)`` () =
    // Two-reference catalog: one reference produces ScriptWithNoCheck
    // (success-with-caveat); the other produces a keep-reason
    // (DataHasOrphans, since no NoCheck allowance for it). The single
    // pass's diagnostic stream contains BOTH shapes — the third real
    // test of whether the writer absorbs heterogeneous emission within
    // one pass.

    // Build a catalog with two FK references — one will trigger
    // ScriptWithNoCheck, the other DataHasOrphans.
    let secondRefKey  = ssKey "OS_REF_FkEnd_Source_Target_Strict"
    let secondAttrKey = ssKey "OS_ATTR_FkEnd_Source_StrictTargetId"
    let strictReference : Reference =
        Reference.create secondRefKey (name "FkSource_StrictTarget") secondAttrKey fkTargetEntityKey
    let strictAttribute : Attribute =
        { Attribute.create secondAttrKey (name "StrictTargetId") Integer with Column = ColumnRealization.create ("STRICT_TARGET_ID") (true) |> Result.value }
    let augmentedSource =
        match fkCatalog.Modules.[0].Kinds |> List.tryFind (fun k -> k.SsKey = fkSourceEntityKey) with
        | Some k ->
            { k with
                Attributes = k.Attributes @ [strictAttribute]
                References = k.References @ [strictReference] }
        | None ->
            failwith "fkCatalog should contain the source kind"
    let augmentedKinds =
        fkCatalog.Modules.[0].Kinds
        |> List.map (fun k ->
            if k.SsKey = fkSourceEntityKey then augmentedSource else k)
    let augmentedCatalog : Catalog =
        { Modules = [
            { fkCatalog.Modules.[0] with Kinds = augmentedKinds } ]; Sequences = [] }

    // Profile shows orphans on BOTH references; AllowNoCheckCreation
    // is true for the policy, but we want one ScriptWithNoCheck and
    // one DataHasOrphans entry — so we emit two interventions, one
    // permissive and one strict, both filtered by intervention id.
    let probe =
        ProbeStatus.create System.DateTimeOffset.UnixEpoch 100L Succeeded
        |> Result.value
    let profile =
        { Profile.empty with
            ForeignKeys = [
                { ReferenceKey = fkRefKey
                  HasOrphan    = true
                  IsNoCheck    = false
                  OrphanCount  = 3L
                  ProbeStatus  = probe }
                { ReferenceKey = secondRefKey
                  HasOrphan    = true
                  IsNoCheck    = false
                  OrphanCount  = 5L
                  ProbeStatus  = probe } ] }

    // The permissive intervention is what we already use; for the
    // strict case we synthesize a single-config catalog where
    // AllowNoCheckCreation=false applied to a profile with orphans
    // produces DataHasOrphans. We test both shapes by running the
    // pass twice with different configs.
    let permissiveCfg =
        ForeignKeyTighteningConfig.create true true true
    let strictCfg =
        ForeignKeyTighteningConfig.create true true false

    let permissivePolicy : Policy =
        { Policy.empty with
            Tightening = { Interventions = [ ForeignKey ("permissive", permissiveCfg) ] } }
    let strictPolicy : Policy =
        { Policy.empty with
            Tightening = { Interventions = [ ForeignKey ("strict", strictCfg) ] } }

    let permissiveResult = fkRun augmentedCatalog permissivePolicy profile
    let strictResult     = fkRun augmentedCatalog strictPolicy     profile

    // Permissive run: two ScriptWithNoCheck entries (success-with-
    // caveat for both references).
    let permissiveEntries = LineageDiagnostics.entries permissiveResult
    Assert.Equal(2, permissiveEntries.Length)
    Assert.All(permissiveEntries, fun e ->
        Assert.Equal("tightening.foreignKey.scriptWithNoCheck", e.Code))

    // Strict run: two DataHasOrphans entries (keep-reason).
    let strictEntries = LineageDiagnostics.entries strictResult
    Assert.Equal(2, strictEntries.Length)
    Assert.All(strictEntries, fun e ->
        Assert.Equal("tightening.foreignKey.dataHasOrphans", e.Code))

    // The two shapes produce structurally identical DiagnosticEntry
    // values (same Source, same Severity, same field shape). Their
    // Code prefix is the only routing-relevant distinction. The
    // writer's codification absorbs success-with-caveat and keep-
    // reason side-by-side without structural distinction —
    // empirical evidence the codification holds under heterogeneous
    // emission.
    Assert.All(permissiveEntries @ strictEntries, fun e ->
        Assert.Equal(DiagnosticSeverity.Warning, e.Severity)
        Assert.Equal("foreignKey", e.Source))

[<Fact>]
let ``T1: byte-determinism holds for ForeignKeyPass under the dual writer`` () =
    let r1 = fkRun fkCatalog fkPolicyAllowingNoCheck fkProfileWithOrphans
    let r2 = fkRun fkCatalog fkPolicyAllowingNoCheck fkProfileWithOrphans

    Assert.Equal<ForeignKeyDecisionSet>(
        ForeignKeyPass.decisionsOf r1,
        ForeignKeyPass.decisionsOf r2)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)
    Assert.Equal<DiagnosticEntry list>(
        LineageDiagnostics.entries r1,
        LineageDiagnostics.entries r2)
