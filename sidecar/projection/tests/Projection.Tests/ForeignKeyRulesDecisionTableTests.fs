module Projection.Tests.ForeignKeyRulesDecisionTableTests

// R3-FK (FORMAL_METHODS.md §2/§3): rung-3 exhaustive cover of
// `ForeignKeyRules.evaluate`.
//
// Where `ForeignKeyRulesTests.fs` witnesses the gate ladder at
// hand-picked points, this suite enumerates the ENTIRE finite decision
// space and verifies at every point — proof by exhaustion.
//
// **The factored space (1024 points).**
//   target exists in catalog        × 2
//   Reference.HasDbConstraint       × 2
//   config.EnableCreation           × 2
//   config.AllowCrossSchema         × 2
//   config.AllowCrossCatalog        × 2   (reserved toggle — proven outcome-inert below)
//   config.TreatMissingDeleteRuleAsIgnore × 2   (reserved toggle — proven outcome-inert below)
//   config.AllowNoCheckCreation     × 2
//   probe class                     × 4   (NoProbe | UnreliableProbe | OrphanReality | CleanReality)
//   FK crosses schema boundaries    × 2
//
// The probe class discretizes the profile evidence into the four
// regions the rule distinguishes (no probe at all / probe ran but is
// unreliable / reliable with orphans / reliable and clean), one
// representative realization per class.
//
// **The oracle** transcribes the documented gate ladder
// (ForeignKeyRules.fs, the "Order of evaluation" block; DECISIONS
// 2026-06-12 reconciliation slice 1). Conformance at all 1024 points
// proves code ⇔ documented precedence. The carve-out, unreachability,
// and inertness laws are quantified over the same enumeration WITHOUT
// consulting the oracle.

open System
open Xunit
open Projection.Core
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// The factored point.
// ---------------------------------------------------------------------------

type private ProbeClass =
    | NoProbe
    | UnreliableProbe
    | OrphanReality
    | CleanReality

type private DecisionPoint = {
    TargetExists     : bool
    HasDbConstraint  : bool
    EnableCreation   : bool
    AllowCrossSchema : bool
    AllowCrossCatalog: bool
    TreatMissingAsIgnore : bool
    AllowNoCheck     : bool
    Probe            : ProbeClass
    CrossesSchema    : bool
}

let private bools = [ false; true ]
let private probeClasses = [ NoProbe; UnreliableProbe; OrphanReality; CleanReality ]

/// The full 2^7 × 4 = 1024-point enumeration.
let private allPoints : DecisionPoint list =
    [ for targetExists in bools do
        for hasDb in bools do
          for enable in bools do
            for allowCross in bools do
              for allowCatalog in bools do
                for treatMissing in bools do
                  for allowNoCheck in bools do
                    for probe in probeClasses do
                      for crosses in bools do
                        yield
                            { TargetExists      = targetExists
                              HasDbConstraint   = hasDb
                              EnableCreation    = enable
                              AllowCrossSchema  = allowCross
                              AllowCrossCatalog = allowCatalog
                              TreatMissingAsIgnore = treatMissing
                              AllowNoCheck      = allowNoCheck
                              Probe             = probe
                              CrossesSchema     = crosses } ]

// ---------------------------------------------------------------------------
// Realization — representative values: orphanCount 7 for the orphan
// class; probe sample size 100 (carried into NoEvidenceObstacle).
// Two catalogs realize the cross-schema factor: the target kind lives
// in `dbo` (same as the `order` source) or in `ext`.
// ---------------------------------------------------------------------------

let private orphanCount = 7L
let private probeSampleSize = 100L

let private customerInExt : Kind =
    { customer with Physical = mkTableId "ext" "OSUSR_S1S_CUSTOMER" }

let private sameSchemaCatalog : Catalog =
    mkCatalog [ mkModule (modKey "SalesSame") (mkName "SalesSame") [ customer; order ] ]

let private crossSchemaCatalog : Catalog =
    mkCatalog [ mkModule (modKey "SalesExt") (mkName "SalesExt") [ customerInExt; order ] ]

let private danglingTargetKey = testKey "R3FK_NoSuchKind"

let private mkReference (p: DecisionPoint) : Reference =
    let target = if p.TargetExists then customerKey else danglingTargetKey
    { Reference.create (refKey [ "R3FK"; "Subject" ]) (mkName "Subject") orderCustomerFkKey target with
        ConstraintState = ConstraintState.ofLegacyBooleans p.HasDbConstraint false }

let private mkProbeStatus (outcome: ProbeOutcome) : ProbeStatus =
    ProbeStatus.create DateTimeOffset.UnixEpoch probeSampleSize outcome
    |> Result.value

let private mkProfile (p: DecisionPoint) (referenceKey: SsKey) : Profile =
    let reality (hasOrphan: bool) (orphans: int64) (outcome: ProbeOutcome) : ForeignKeyReality =
        { ReferenceKey = referenceKey
          HasOrphan    = hasOrphan
          OrphanCount  = orphans
          IsNoCheck    = false
          ProbeStatus  = mkProbeStatus outcome }
    match p.Probe with
    | NoProbe         -> Profile.empty
    | UnreliableProbe -> { Profile.empty with ForeignKeys = [ reality false 0L FallbackTimeout ] }
    | OrphanReality   -> { Profile.empty with ForeignKeys = [ reality true orphanCount Succeeded ] }
    | CleanReality    -> { Profile.empty with ForeignKeys = [ reality false 0L Succeeded ] }

let private mkConfig (p: DecisionPoint) : ForeignKeyTighteningConfig =
    ForeignKeyTighteningConfig.create
        p.EnableCreation p.AllowCrossSchema p.AllowCrossCatalog
        p.TreatMissingAsIgnore p.AllowNoCheck

let private decide (p: DecisionPoint) : ForeignKeyOutcome =
    let catalog = if p.CrossesSchema then crossSchemaCatalog else sameSchemaCatalog
    let reference = mkReference p
    let decision =
        ForeignKeyRules.evaluate
            "r3-fk" (mkConfig p) order reference catalog (mkProfile p reference.SsKey)
    decision.Outcome

// ---------------------------------------------------------------------------
// The oracle — the documented gate ladder, transcribed independently.
// ---------------------------------------------------------------------------

let private oracle (p: DecisionPoint) : ForeignKeyOutcome =
    if not p.TargetExists then
        ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.MissingTarget
    elif p.HasDbConstraint then
        ForeignKeyOutcome.EnforceConstraint DatabaseConstraintPresent
    elif not p.EnableCreation then
        ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.PolicyDisabled
    else
        match p.Probe with
        | NoProbe | UnreliableProbe ->
            ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.EvidenceMissing
        | OrphanReality ->
            if p.AllowNoCheck then
                ForeignKeyOutcome.EnforceConstraint (ScriptWithNoCheck orphanCount)
            else
                ForeignKeyOutcome.DoNotEnforce (ForeignKeyKeepReason.DataHasOrphans orphanCount)
        | CleanReality ->
            if not p.AllowCrossSchema && p.CrossesSchema then
                ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.CrossSchemaBlocked
            else
                ForeignKeyOutcome.EnforceConstraint (NoEvidenceObstacle probeSampleSize)

// ---------------------------------------------------------------------------
// R3-FK Law 0 — the enumeration is the space it claims to be.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-FK cardinality: the enumeration covers exactly the 2^7 * 4 * 2 = 1024-point space`` () =
    Assert.Equal(1024, List.length allPoints)
    Assert.Equal(1024, allPoints |> List.distinct |> List.length)

// ---------------------------------------------------------------------------
// R3-FK Law 1 — conformance: at EVERY point, evaluate agrees with the
// documented gate ladder.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-FK conformance: evaluate matches the documented gate ladder at every point of the space`` () =
    let disagreements =
        allPoints
        |> List.choose (fun p ->
            let actual = decide p
            let expected = oracle p
            if actual = expected then None else Some (p, expected, actual))
    Assert.True(
        List.isEmpty disagreements,
        sprintf "%d/1024 points disagree with the documented gate ladder; first: %A"
            (List.length disagreements) (List.tryHead disagreements))

// ---------------------------------------------------------------------------
// R3-FK Law 2 — the carve-outs, quantified without the oracle.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-FK precedence: a missing target decides at every point regardless of the eight other factors`` () =
    allPoints
    |> List.filter (fun p -> not p.TargetExists)
    |> List.iter (fun p ->
        Assert.Equal(ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.MissingTarget, decide p))

[<Fact>]
let ``R3-FK precedence: the V1 carve-out — a source-backed reference enforces past every gate and every probe class`` () =
    allPoints
    |> List.filter (fun p -> p.TargetExists && p.HasDbConstraint)
    |> List.iter (fun p ->
        Assert.Equal(ForeignKeyOutcome.EnforceConstraint DatabaseConstraintPresent, decide p))

[<Fact>]
let ``R3-FK precedence: EnableCreation=false short-circuits at every remaining point without consulting evidence`` () =
    allPoints
    |> List.filter (fun p -> p.TargetExists && not p.HasDbConstraint && not p.EnableCreation)
    |> List.iter (fun p ->
        Assert.Equal(ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.PolicyDisabled, decide p))

// ---------------------------------------------------------------------------
// R3-FK Law 3 — reserved-variant unreachability: the docstring claims
// `CrossCatalogBlocked` and `DeleteRuleIgnored` are unreachable from
// V2's IR today. Proven over the whole space.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-FK unreachability: the reserved keep-reasons CrossCatalogBlocked and DeleteRuleIgnored occur nowhere in the space`` () =
    allPoints
    |> List.iter (fun p ->
        match decide p with
        | ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.CrossCatalogBlocked
        | ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.DeleteRuleIgnored ->
            Assert.Fail(sprintf "Reserved keep-reason reached at %A" p)
        | _ -> ())

// ---------------------------------------------------------------------------
// R3-FK Law 4 — inert-toggle law: the two V1-parity reserved toggles
// (`AllowCrossCatalog`, `TreatMissingDeleteRuleAsIgnore`) never change
// an outcome anywhere in the space.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-FK inertness: AllowCrossCatalog and TreatMissingDeleteRuleAsIgnore are outcome-inert across the whole space`` () =
    allPoints
    |> List.groupBy (fun p -> { p with AllowCrossCatalog = false; TreatMissingAsIgnore = false })
    |> List.iter (fun (_, group) ->
        Assert.Equal(4, List.length group)
        let outcomes = group |> List.map decide |> List.distinct
        Assert.Equal(1, List.length outcomes))

// ---------------------------------------------------------------------------
// R3-FK Law 5 — evidence gating: the probe class can matter only past
// the three structural gates. Wherever a structural gate decides,
// varying the probe class never changes the outcome.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-FK non-interference: outcome is probe-invariant wherever a structural gate decides`` () =
    allPoints
    |> List.filter (fun p ->
        not p.TargetExists || p.HasDbConstraint || not p.EnableCreation)
    |> List.groupBy (fun p -> { p with Probe = NoProbe })
    |> List.iter (fun (_, group) ->
        let outcomes = group |> List.map decide |> List.distinct
        Assert.Equal(1, List.length outcomes))

// ---------------------------------------------------------------------------
// R3-FK Law 6 — determinism over the whole space.
// ---------------------------------------------------------------------------

[<Fact>]
let ``R3-FK determinism: evaluate is stable at every point of the space`` () =
    allPoints
    |> List.iter (fun p -> Assert.Equal(decide p, decide p))
