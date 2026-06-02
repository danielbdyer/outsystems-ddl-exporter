module Projection.Tests.DecisionOverlayTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core

// Wave-2 slice 2.1 — DecisionOverlay acceptance. The overlay is the
// emitter-consumable projection of the three tightening decision sets;
// these tests pin (a) A18-safety as a type witness, (b) observable
// identity on the empty policy, (c) totality of `ofComposeState` over
// decision-set membership, and (d) the worked routing of each outcome
// into the right Set.

let private key (s: string) : SsKey =
    match SsKey.synthesized "TEST" s with
    | Ok k -> k
    | Error _ -> failwithf "test key: %s" s

let private emptyCatalog : Catalog =
    match Catalog.create [] [] with
    | Ok c -> c
    | Error _ -> failwith "emptyCatalog" 

let private nullDecision (k: SsKey) (o: NullabilityOutcome) : NullabilityDecision =
    { AttributeKey = k; Outcome = o; InterventionId = "iv" }

let private uniqueDecision (k: SsKey) (o: UniqueIndexOutcome) : UniqueIndexDecision =
    { IndexKey = k; Outcome = o; InterventionId = "iv" }

let private fkDecision (k: SsKey) (o: ForeignKeyOutcome) : ForeignKeyDecision =
    { ReferenceKey = k; Outcome = o; InterventionId = "iv" }

let private stateWith
    (nulls: NullabilityDecision list)
    (uniques: UniqueIndexDecision list)
    (fks: ForeignKeyDecision list)
    : ComposeState =
    { ComposeState.initial emptyCatalog with
        NullabilityDecisions = Some { Decisions = nulls }
        UniqueIndexDecisions = Some { Decisions = uniques }
        ForeignKeyDecisions = Some { Decisions = fks } }

// ---------------------------------------------------------------------
// (a) A18: DecisionOverlay carries evidence-derived decisions, never Policy.
// The structural witness is the constructor signature: `ofComposeState`
// takes a `ComposeState` (evidence accumulation) and returns a
// `DecisionOverlay`; there is no `Policy` in its type. This test is the
// executable cross-reference — it compiles iff the seam stays A18-safe.
// ---------------------------------------------------------------------

[<Fact>]
let ``A18: DecisionOverlay.ofComposeState consumes ComposeState evidence, never Policy`` () =
    let project : ComposeState -> DecisionOverlay = DecisionOverlay.ofComposeState
    // The type ascription above is the witness: a Policy parameter would
    // fail to compile. Exercise it so the test is not vacuous.
    let overlay = project (ComposeState.initial emptyCatalog)
    Assert.Equal<DecisionOverlay>(DecisionOverlay.empty, overlay)

// ---------------------------------------------------------------------
// (b) Observable identity on the empty policy.
// ---------------------------------------------------------------------

[<Fact>]
let ``DecisionOverlay.empty is observable identity on the initial ComposeState`` () =
    Assert.Equal<DecisionOverlay>(
        DecisionOverlay.empty,
        DecisionOverlay.ofComposeState (ComposeState.initial emptyCatalog))

[<Fact>]
let ``DecisionOverlay: None decision-set fields contribute the empty set`` () =
    // A ComposeState whose decision-set Options are all None projects to empty.
    let state = ComposeState.initial emptyCatalog
    let overlay = DecisionOverlay.ofComposeState state
    Assert.Empty(overlay.EnforceNotNull)
    Assert.Empty(overlay.EnforceUnique)
    Assert.Empty(overlay.DropFk)
    Assert.Empty(overlay.NoCheckFk)

// ---------------------------------------------------------------------
// (d) Worked routing: each outcome lands in exactly the right Set.
// ---------------------------------------------------------------------

[<Fact>]
let ``DecisionOverlay: EnforceNotNull outcomes populate EnforceNotNull; KeepNullable does not`` () =
    let enforced = key "ATTR_enforced"
    let kept = key "ATTR_kept"
    let state =
        stateWith
            [ nullDecision enforced (NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey)
              nullDecision kept (NullabilityOutcome.KeepNullable OperatorOverride) ]
            [] []
    let overlay = DecisionOverlay.ofComposeState state
    Assert.True(overlay.EnforceNotNull.Contains enforced)
    Assert.False(overlay.EnforceNotNull.Contains kept)

[<Fact>]
let ``DecisionOverlay: EnforceUnique outcomes populate EnforceUnique; DoNotEnforce does not`` () =
    let enforced = key "IDX_enforced"
    let kept = key "IDX_kept"
    let state =
        stateWith
            []
            [ uniqueDecision enforced (UniqueIndexOutcome.EnforceUnique UniqueIndexEvidence.AlreadyUnique)
              uniqueDecision kept (UniqueIndexOutcome.DoNotEnforce UniqueIndexKeepReason.EvidenceMissing) ]
            []
    let overlay = DecisionOverlay.ofComposeState state
    Assert.True(overlay.EnforceUnique.Contains enforced)
    Assert.False(overlay.EnforceUnique.Contains kept)

[<Fact>]
let ``DecisionOverlay: FK outcomes partition across DropFk / NoCheckFk, EnforceConstraint(clean) in neither`` () =
    let dropped = key "FK_dropped"
    let noCheck = key "FK_nocheck"
    let clean = key "FK_clean"
    let state =
        stateWith
            [] []
            [ fkDecision dropped (ForeignKeyOutcome.DoNotEnforce ForeignKeyKeepReason.EvidenceMissing)
              fkDecision noCheck (ForeignKeyOutcome.EnforceConstraint (ScriptWithNoCheck 7L))
              fkDecision clean (ForeignKeyOutcome.EnforceConstraint (NoEvidenceObstacle 100L)) ]
    let overlay = DecisionOverlay.ofComposeState state
    Assert.True(overlay.DropFk.Contains dropped)
    Assert.False(overlay.NoCheckFk.Contains dropped)
    Assert.True(overlay.NoCheckFk.Contains noCheck)
    Assert.False(overlay.DropFk.Contains noCheck)
    Assert.False(overlay.DropFk.Contains clean)
    Assert.False(overlay.NoCheckFk.Contains clean)

// ---------------------------------------------------------------------
// (c) FsCheck: ofComposeState is total over decision-set membership.
// For any list of nullability decisions, every EnforceNotNull key appears
// in the overlay and no other; the projection never throws.
// ---------------------------------------------------------------------

[<Property>]
let ``ofComposeState is total: every EnforceNotNull attribute key appears, and only those`` (names: string list) =
    // Build alternating EnforceNotNull / KeepNullable decisions from
    // distinct non-blank keys.
    let distinct =
        names
        |> List.filter (fun s -> not (System.String.IsNullOrWhiteSpace s))
        |> List.distinct
    let decisions =
        distinct
        |> List.mapi (fun i s ->
            let k = key (sprintf "A_%d_%s" i (s.Replace(" ", "_")))
            if i % 2 = 0 then k, nullDecision k (NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey), true
            else k, nullDecision k (NullabilityOutcome.KeepNullable OperatorOverride), false)
    let state = stateWith (decisions |> List.map (fun (_, d, _) -> d)) [] []
    let overlay = DecisionOverlay.ofComposeState state
    let expected = decisions |> List.filter (fun (_, _, e) -> e) |> List.map (fun (k, _, _) -> k) |> Set.ofList
    overlay.EnforceNotNull = expected
