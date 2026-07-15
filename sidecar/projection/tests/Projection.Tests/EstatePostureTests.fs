module Projection.Tests.EstatePostureTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.OperationalDiagnostics
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The interim posture's projections (wave A6 — `EstatePosture` +
// `EstateOverlayEmitter`). The laws under test:
//   - π-COHERENCE: the RELAX-lane PROPOSED findings, the overlay's
//     suggested edits, and the reopen probes carry ONE key set — a
//     relaxation appears in all of them or in none.
//   - THE A44 CIRCLE: every suggested edit's value is EXACTLY the entry
//     shape `TighteningBinding` binds (budget-less nullability `overrides`;
//     foreignKey `referenceOverrides`) — the binder-side half is
//     TighteningBindingTests' A44 enforcement fact.
//   - HONEST PROBES: the orphan probe counts EVERY orphan (the relationship
//     cannot track WITH CHECK until all clear); the empty posture renders
//     the said-empty comment, never an empty file.
// ---------------------------------------------------------------------------

let private agreed : Estate.TargetOperand = Estate.TargetOperand.AgreedEnv "cloud-dev"

let private operand (label: string) (c: Catalog) (p: Profile option) : Compare.Operand =
    { Label = label; Catalog = c; Profile = p }

let private nullEvidence (attrKey: SsKey) (rowCount: int64) (nullCount: int64) : ColumnProfile =
    { AttributeKey = attrKey
      RowCount = rowCount
      NullCount = nullCount
      MaxObservedLength = None
      NullCountProbeStatus = ProbeStatus.observed rowCount }

let private orphanEvidence (refKey: SsKey) (orphans: int64) : ForeignKeyReality =
    { ReferenceKey = refKey
      HasOrphan = orphans > 0L
      OrphanCount = orphans
      IsNoCheck = false
      ProbeStatus = ProbeStatus.observed 1000L }

/// A report whose data plane carries BOTH proposed relaxations: 150,000
/// true orphans on Order→Customer and 200,000 NULLs under Customer.Name —
/// both past the default band.
let private pastBandReport () : Estate.EstateReport =
    let dirty =
        { Profile.empty with
            Columns = [ nullEvidence customerNameKey 500_000L 200_000L ]
            ForeignKeys = [ orphanEvidence orderRefToCustomer 150_000L ] }
    Estate.compute agreed sampleCatalog
        [ "cloud-uat", operand "cloud-uat" sampleCatalog (Some dirty) ]

let private relaxationsOf (report: Estate.EstateReport) : Relaxation list =
    EstatePosture.relaxationsFor (Readiness.toLogicalShape sampleCatalog) report

[<Fact>]
let ``π-coherence: the proposed RELAX findings, the overlay entries, and the probes carry one key set`` () =
    let report = pastBandReport ()
    let proposedKeys =
        report.Findings
        |> List.filter (fun f ->
            f.Kind = EstateFindingKind.DataOrphansPastBand
            || f.Kind = EstateFindingKind.DataNotNullPastBand)
        |> List.map (fun f -> FindingKey.text f.Key)
        |> Set.ofList
    Assert.Equal(2, Set.count proposedKeys)
    let relaxations = relaxationsOf report
    let relaxationKeys = relaxations |> List.map (fun r -> FindingKey.text r.Scope) |> Set.ofList
    Assert.Equal<Set<string>>(proposedKeys, relaxationKeys)
    let overlay = EstateOverlayEmitter.emitOverlay "test overlay" relaxations
    let probes = EstateOverlayEmitter.emitProbes [ "-- header" ] relaxations
    for key in proposedKeys do
        Assert.Contains(sprintf "\"findingKey\": \"%s\"" key, overlay)
        Assert.Contains(sprintf "-- probe %s" key, probes)

[<Fact>]
let ``the A44 circle: the untrack entry carries the binder's exact shape — foreignKey + referenceOverrides + keepUntracked`` () =
    let report = pastBandReport ()
    let untrack =
        relaxationsOf report
        |> List.find (fun r -> match r.Action with RelaxationAction.KeepUntracked _ -> true | _ -> false)
    match untrack.Action with
    | RelaxationAction.KeepUntracked referenceRef ->
        // The three-part logical form, module-rooted — the same token
        // TighteningBinding.resolveAttributeRef resolves at merge time.
        Assert.EndsWith(".Order.CustomerId", referenceRef)
        Assert.Equal(3, referenceRef.Split('.').Length)
    | other -> Assert.Fail(sprintf "expected KeepUntracked, got %A" other)
    let overlay = EstateOverlayEmitter.emitOverlay "test overlay" [ untrack ]
    Assert.Contains("\"kind\": \"foreignKey\"", overlay)
    Assert.Contains("\"referenceOverrides\":", overlay)
    Assert.Contains("\"action\": \"keepUntracked\"", overlay)
    Assert.Contains("\"path\": \"$.policy.tightening.interventions[+]\"", overlay)

[<Fact>]
let ``the A44 circle: the keep-nullable entry carries the binder's exact shape — budget-less nullability + overrides`` () =
    let report = pastBandReport ()
    let keepNullable =
        relaxationsOf report
        |> List.find (fun r -> match r.Action with RelaxationAction.KeepNullable _ -> true | _ -> false)
    match keepNullable.Action with
    | RelaxationAction.KeepNullable attributeRef ->
        Assert.EndsWith(".Customer.Name", attributeRef)
        Assert.Equal(3, attributeRef.Split('.').Length)
    | other -> Assert.Fail(sprintf "expected KeepNullable, got %A" other)
    let overlay = EstateOverlayEmitter.emitOverlay "test overlay" [ keepNullable ]
    Assert.Contains("\"kind\": \"nullability\"", overlay)
    Assert.Contains("\"overrides\":", overlay)
    Assert.Contains("\"action\": \"keepNullable\"", overlay)
    Assert.DoesNotContain("nullBudget", overlay)

[<Fact>]
let ``the reopen probes count every violation and name their retirement`` () =
    let report = pastBandReport ()
    let relaxations = relaxationsOf report
    let orphanProbe =
        relaxations
        |> List.pick (fun r -> match r.Action with RelaxationAction.KeepUntracked _ -> Some r.ReopenProbe | _ -> None)
    Assert.Contains("COUNT_BIG(*)", orphanProbe)
    Assert.Contains("NOT IN (SELECT", orphanProbe)
    Assert.Contains("retires at zero", orphanProbe)
    let nullProbe =
        relaxations
        |> List.pick (fun r -> match r.Action with RelaxationAction.KeepNullable _ -> Some r.ReopenProbe | _ -> None)
    Assert.Contains("IS NULL", nullProbe)
    Assert.Contains("retires at zero", nullProbe)

[<Fact>]
let ``the evidence rides the overlay entry's note — the counts that forced the relaxation`` () =
    let report = pastBandReport ()
    let overlay = EstateOverlayEmitter.emitOverlay "test overlay" (relaxationsOf report)
    Assert.Contains("the evidence that forced it: 150,000 in cloud-uat", overlay)

[<Fact>]
let ``an empty posture renders the said-empty probes comment, never an empty file`` () =
    let probes = EstateOverlayEmitter.emitProbes [ "-- header" ] []
    Assert.Contains("No proposed relaxations this run", probes)

[<Fact>]
let ``activeOf reads exactly the bound posture — relaxation-only nullability overrides and FK keepUntracked overrides`` () =
    let nullCfg =
        NullabilityTighteningConfig.relaxationOnly false
            [ { AttributeKey = customerNameKey; Action = OverrideAction.KeepNullable } ]
    let fkCfg =
        ForeignKeyTighteningConfig.relaxationOnly
            [ { ReferenceKey = orderRefToCustomer; Action = ForeignKeyOverrideAction.KeepUntracked } ]
    let evidenceDrivenNull =
        NullabilityTighteningConfig.create 0.0m false
            [ { AttributeKey = customerTenantKey; Action = OverrideAction.KeepNullable } ]
        |> Result.value
    let refs, attrs =
        EstatePosture.activeOf
            { Interventions =
                [ TighteningIntervention.Nullability ("interim", nullCfg)
                  TighteningIntervention.ForeignKey ("untrack", fkCfg)
                  // An EVIDENCE-DRIVEN intervention's overrides are coercion-
                  // era mute switches, never the standing posture.
                  TighteningIntervention.Nullability ("evidence", evidenceDrivenNull) ] }
    Assert.Equal<Set<SsKey>>(Set.singleton orderRefToCustomer, refs)
    Assert.Equal<Set<SsKey>>(Set.singleton customerNameKey, attrs)
