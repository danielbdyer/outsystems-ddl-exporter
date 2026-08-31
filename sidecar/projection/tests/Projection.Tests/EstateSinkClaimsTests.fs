module Projection.Tests.EstateSinkClaimsTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// The sink's claim findings on the estate board (the data-sink chapter,
// S11b): Contested and TombstoneOnly mint DECIDE-lane findings with their
// contract rows (lane/plane/lever from the per-kind functions, statements
// through the strategy's one renderer); Contested carries the FORK witness,
// so `withSinkClaims` turns a Unified estate Forked (exit 5 at the face);
// TombstoneOnly turns it Converging; Unclaimed (S12's residue) mints its
// own DECIDE finding; an adoption over TOMBSTONES proposes the
// cross-cutover identity correspondence (S14 — a DECIDE finding the
// operator rules; the tool never adopts); a CLEAN sole adoption mints
// nothing, and an empty claims list is the identity (a run with no sink
// is byte-identical).

/// align-III.1: expected-ordinal literal for asserts (patterns can't call functions).
let private ord (n: int) : SyncOrdinal =
    match SyncOrdinal.create n with
    | Ok o -> o
    | Error m -> failwith m

let private claim (id: int) (name: string) (active: bool) (ext: bool) (sync: int) : PhysicalClaimRules.PhysicalClaim =
    { EntityId = id
      EntityKey = None
      EntityName = name
      ModuleName = if ext then "FulfillmentExtension" else "Fulfillment"
      IsActive = active
      IsExternalRegistration = ext
      FirstWitnessedSync = PhysicalClaimRules.FirstWitnessedSync.ofAppearance (Some (ord sync)) }

let private setOf (table: string) (claims: PhysicalClaimRules.PhysicalClaim list) : PhysicalClaimRules.ClaimSet =
    { Ref = PhysicalClaimRules.PhysicalTableRef.observed "dbo" table; Claims = claims }

let private adjudicated (table: string) (claims: PhysicalClaimRules.PhysicalClaim list) =
    let s = setOf table claims
    s, PhysicalClaimRules.adjudicate s

let private unifiedReport () =
    Estate.compute
        (Estate.TargetOperand.AuthoredModel "model.json")
        sampleCatalog
        [ "cloud-uat", ({ Label = "cloud-uat"; Catalog = sampleCatalog; Profile = None } : Compare.Operand) ]

[<Fact>]
let ``contested, tombstone-only, and unclaimed mint DECIDE findings with their contract rows; adopted mints nothing`` () =
    let outcomes =
        [ adjudicated "OSUSR_FUL_CARRIER" [ claim 8003 "Carrier" true false 1; claim 9003 "Carrier" true true 1 ]
          adjudicated "OSUSR_FUL_INVOICE" [ claim 8001 "Invoice" false false 1 ]
          adjudicated "OSUSR_FUL_ORDER" [ claim 8000 "Order" true false 1 ]
          adjudicated "OSUSR_FUL_EMPTY" [] ]
    let findings = Estate.sinkClaimFindingsOf "cloud-uat" outcomes
    Assert.Equal(3, List.length findings)
    Assert.True(findings |> List.exists (fun f -> f.Kind = EstateFindingKind.PhysicalUnclaimed))
    Assert.False(findings |> List.exists (fun f -> f.Statement.Contains "OSUSR_FUL_ORDER"))
    let contested = findings |> List.find (fun f -> f.Kind = EstateFindingKind.PhysicalClaimContested)
    Assert.Equal(EstateLane.Decide, contested.Lane)
    Assert.Equal(EstatePlane.Identity, contested.Plane)
    Assert.True(contested.Fork, "two live writers fork the estate")
    Assert.True(contested.Lever.IsSome, "a DECIDE finding carries its ruling imperative")
    Assert.Contains("dbo.OSUSR_FUL_CARRIER", contested.Statement)
    Assert.Contains("Fulfillment.Carrier", contested.Statement)
    Assert.Equal<(string * int64) list>([ "cloud-uat", 2L ], contested.Envs)
    let tombstone = findings |> List.find (fun f -> f.Kind = EstateFindingKind.PhysicalTombstoneOnly)
    Assert.False(tombstone.Fork)
    Assert.Contains("witnessed editions", tombstone.Statement)

[<Fact>]
let ``withSinkClaims re-derives the verdict: contested forks a unified estate; tombstone-only converges it`` () =
    let unified = unifiedReport ()
    Assert.Equal(Estate.Verdict.Unified, unified.Verdict)
    let contested =
        Estate.withSinkClaims
            [ "cloud-uat", [ adjudicated "OSUSR_FUL_CARRIER" [ claim 8003 "Carrier" true false 1; claim 9003 "Carrier" true true 1 ] ] ]
            unified
    Assert.Equal(Estate.Verdict.Forked, contested.Verdict)
    let tombstoned =
        Estate.withSinkClaims
            [ "cloud-uat", [ adjudicated "OSUSR_FUL_INVOICE" [ claim 8001 "Invoice" false false 1 ] ] ]
            unified
    Assert.Equal(Estate.Verdict.Converging, tombstoned.Verdict)

[<Fact>]
let ``an unclaimed residue table mints a PhysicalUnclaimed DECIDE finding (S12)`` () =
    let findings =
        Estate.sinkClaimFindingsOf "cloud-uat" [ adjudicated "OSUSR_FUL_ARCHIVE" [] ]
    match findings with
    | [ f ] ->
        Assert.Equal(EstateFindingKind.PhysicalUnclaimed, f.Kind)
        Assert.Equal(EstateLane.Decide, f.Lane)
        Assert.Equal(EstatePlane.Schema, f.Plane)
        Assert.False(f.Fork)
        Assert.True(f.Lever.IsSome)
        Assert.Contains("dbo.OSUSR_FUL_ARCHIVE", f.Statement)
        Assert.Contains("no metadata claim", f.Statement)
    | other -> Assert.Fail (sprintf "expected one PhysicalUnclaimed finding, got %A" other)

[<Fact>]
let ``an adoption over tombstones proposes the cutover correspondence — a DECIDE finding the operator rules, never a fork, never an adoption`` () =
    // The cutover pair: the extension re-registration is the sole live
    // claim; the tombstoned original rides outranked — S14's proposal.
    let findings =
        Estate.sinkClaimFindingsOf "cloud-uat"
            [ adjudicated "OSUSR_FUL_SHIPMENT"
                [ claim 8002 "Shipment" false false 1; claim 9002 "Shipment" true true 3 ] ]
    match findings with
    | [ f ] ->
        Assert.Equal(EstateFindingKind.IdentityCutoverCorrespondence, f.Kind)
        Assert.Equal(EstateLane.Decide, f.Lane)
        Assert.Equal(EstatePlane.Identity, f.Plane)
        Assert.False(f.Fork, "a proposal is the happy path's paperwork — it never forks the estate")
        // The structural never-auto-adopted pin: the kind's lever IS the
        // ruling imperative (confirm or reject), and the proposer's type
        // can write nothing (no catalog in, no SsKey out).
        let _ =
            match EstateFindingKind.leverFormOf EstateFindingKind.IdentityCutoverCorrespondence with
            | EstateLeverForm.Ruling imperative -> Assert.Contains("nothing is adopted without the ruling", imperative)
            | other -> Assert.Fail (sprintf "the correspondence lever must be a Ruling, got %A" other)
        Assert.Contains("Fulfillment.Shipment", f.Statement)
        Assert.Contains("FulfillmentExtension.Shipment", f.Statement)
        Assert.Contains("dbo.OSUSR_FUL_SHIPMENT", f.Statement)
        Assert.Contains("confirm or reject", f.Statement)
    | other -> Assert.Fail (sprintf "expected one correspondence proposal, got %A" other)
    // The proposal converges a unified estate (a DECIDE row awaits its
    // ruling) — it never forks it.
    let report =
        Estate.withSinkClaims
            [ "cloud-uat", [ adjudicated "OSUSR_FUL_SHIPMENT" [ claim 8002 "Shipment" false false 1; claim 9002 "Shipment" true true 3 ] ] ]
            (unifiedReport ())
    Assert.Equal(Estate.Verdict.Converging, report.Verdict)

[<Fact>]
let ``no sink claims is the identity — a run with no sink store is byte-identical`` () =
    let unified = unifiedReport ()
    Assert.Equal(unified, Estate.withSinkClaims [] unified)
    // All-adopted outcomes are lineage, not findings — also the identity.
    Assert.Equal(
        unified,
        Estate.withSinkClaims
            [ "cloud-uat", [ adjudicated "OSUSR_FUL_ORDER" [ claim 8000 "Order" true false 1 ] ] ]
            unified)

[<Fact>]
let ``K9 end-to-end (align-II.5): a confirmed S14 correspondence ruling renders on its finding — recorded judgment, no adoption`` () =
    // The S14 proposal joins the board; the recorded ruling (the align-II.1
    // carrier, FindingKey-anchored) renders in the lever's slot. NOTHING
    // else moves: the verdict stays Converging (the finding is answered,
    // not applied — adoption stays the named deferral), and a rejected
    // re-ruling replaces the rendered judgment the same way.
    let report =
        Estate.withSinkClaims
            [ "cloud-uat", [ adjudicated "OSUSR_FUL_SHIPMENT" [ claim 8002 "Shipment" false false 1; claim 9002 "Shipment" true true 3 ] ] ]
            (unifiedReport ())
    let correspondence =
        report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.IdentityCutoverCorrespondence)
    let rulingWith (verdict: RulingVerdict) (why: string) : OperatorRuling<FindingKey> =
        OperatorRuling.create correspondence.Key verdict
            (Some (BasisAnchor.FindingKey correspondence.Key)) "dana"
            (System.DateTimeOffset(2026, 8, 16, 9, 0, 0, System.TimeSpan.Zero)) (Some why) None
        |> Result.value
    let confirmed = report |> Estate.withRulings [ rulingWith RulingVerdict.Confirmed "one entity across the cutover" ]
    let lines = Estate.render confirmed
    Assert.Contains(lines, fun (l: string) ->
        l.Contains "The ruling stands: confirmed by dana on 2026-08-16 — one entity across the cutover.")
    // The answered question's imperative no longer renders.
    Assert.DoesNotContain(lines, fun (l: string) -> l.Contains "Rule the correspondence:")
    // Record + render only: the verdict and the finding stand as computed.
    Assert.Equal(Estate.Verdict.Converging, confirmed.Verdict)
    Assert.Equal<Estate.Finding list>(report.Findings, confirmed.Findings)
    // A re-ruling REPLACES the rendered judgment (the keyed store's law,
    // visible at the board): rejected renders where confirmed did.
    let rejected = report |> Estate.withRulings [ rulingWith RulingVerdict.Rejected "two entities after all" ]
    Assert.Contains(Estate.render rejected, fun (l: string) ->
        l.Contains "The ruling stands: rejected by dana on 2026-08-16 — two entities after all.")
