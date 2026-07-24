module Projection.Tests.BridgeRowDeltaTests

open Xunit
open Projection.Core

// The pure bridge-row staging delta kernel (BridgeRowDelta.fs). The laws:
//   * each closed verdict case fires on exactly its observed shape;
//   * the SAFETY RULE is structural — an update delta exists ONLY when the
//     bridge identity is NULL; a populated-but-different identity is a
//     conflict BLOCK, never an update; a staged insert exists ONLY when no
//     bridge row exists;
//   * staged changes project from the SAME verdict ledger the audit renders
//     (staged ⇔ decided);
//   * planned evidence is FAIL-CLOSED: any blocking verdict withholds it
//     entirely; a clean plan yields the honest counts with the bridge-WIDE
//     key stats passed through;
//   * determinism: keys dedupe + ordinal-sort.

let private name (s: string) : Name = Name.create s |> Result.value

let private spec : BridgeRowStagingSpec =
    { StagingId               = "st1"
      BridgeKeyAttribute      = name "RefKey"
      BridgeIdentityAttribute = name "ExternalRef"
      Mappings                = [ { BridgeAttribute = name "FullName"; SourceAttribute = name "Name" } ]
      Constants               = [ { BridgeAttribute = name "OriginId"; Value = Some "1" } ] }

let private srcRow (key: string) (identity: string option) : BridgeSourceSnapshotRow =
    { Key = key; Identity = identity; Cells = Map.ofList [ name "Name", Some "N" ] }

let private brRow (key: string) (identity: string option) : BridgeSnapshotRow =
    { Key = key; Identity = identity }

let private cleanWide : BridgeWideStats = { KeyNullCount = 0L; KeyDuplicateCount = 0L }

let private verdictOf (key: string) (plan: BridgeRowStagingPlan) : BridgeRowVerdict =
    plan.Verdicts |> List.find (fun (k, _) -> k = key) |> snd

// ---------------------------------------------------------------------------
// The closed verdict taxonomy — one case per observed shape.
// ---------------------------------------------------------------------------

[<Fact>]
let ``bridge row present with matching identity is AlreadyComplete (verified)`` () =
    let plan = BridgeRowDelta.compute spec [ "1" ] [ srcRow "1" (Some "x") ] [ brRow "1" (Some "x") ]
    Assert.Equal(BridgeRowVerdict.AlreadyComplete true, verdictOf "1" plan)
    Assert.Empty plan.Inserts
    Assert.Empty plan.IdentityUpdates

[<Fact>]
let ``bridge identity present but source identity absent is AlreadyComplete (unverified)`` () =
    let plan = BridgeRowDelta.compute spec [ "1" ] [ srcRow "1" None ] [ brRow "1" (Some "x") ]
    Assert.Equal(BridgeRowVerdict.AlreadyComplete false, verdictOf "1" plan)

[<Fact>]
let ``no bridge row and derivable source stages an INSERT`` () =
    let plan = BridgeRowDelta.compute spec [ "1" ] [ srcRow "1" (Some "x") ] []
    Assert.Equal(BridgeRowVerdict.StageInsert, verdictOf "1" plan)
    let ins = List.exactlyOne plan.Inserts
    Assert.Equal("1", ins.Key)
    // The staged cell map: key + identity + mapping + constant, no more.
    Assert.Equal(Some (Some "1"), Map.tryFind (name "RefKey") ins.Cells)
    Assert.Equal(Some (Some "x"), Map.tryFind (name "ExternalRef") ins.Cells)
    Assert.Equal(Some (Some "N"), Map.tryFind (name "FullName") ins.Cells)
    Assert.Equal(Some (Some "1"), Map.tryFind (name "OriginId") ins.Cells)
    Assert.Equal(4, Map.count ins.Cells)

[<Fact>]
let ``bridge row with NULL identity and derivable source stages a FILL-ONLY update`` () =
    let plan = BridgeRowDelta.compute spec [ "1" ] [ srcRow "1" (Some "x") ] [ brRow "1" None ]
    Assert.Equal(BridgeRowVerdict.StageIdentityUpdate, verdictOf "1" plan)
    let upd = List.exactlyOne plan.IdentityUpdates
    Assert.Equal("1", upd.Key)
    Assert.Equal("x", upd.Identity)
    Assert.Empty plan.Inserts

[<Fact>]
let ``missing source row BLOCKS`` () =
    let plan = BridgeRowDelta.compute spec [ "1" ] [] []
    Assert.Equal(BridgeRowVerdict.SourceRowMissing, verdictOf "1" plan)
    Assert.True(BridgeRowVerdict.isBlocking (verdictOf "1" plan))

[<Fact>]
let ``source identity missing BLOCKS whether or not a bridge row exists`` () =
    let noBridge = BridgeRowDelta.compute spec [ "1" ] [ srcRow "1" None ] []
    Assert.Equal(BridgeRowVerdict.SourceIdentityMissing false, verdictOf "1" noBridge)
    let withBridge = BridgeRowDelta.compute spec [ "1" ] [ srcRow "1" None ] [ brRow "1" None ]
    Assert.Equal(BridgeRowVerdict.SourceIdentityMissing true, verdictOf "1" withBridge)

[<Fact>]
let ``bridge identity that DISAGREES with source is an IdentityConflict BLOCK — never an update`` () =
    // The safety rule's core case: overwriting the existing non-null value is
    // unrepresentable — the vocabulary has no verdict that would do it.
    let plan = BridgeRowDelta.compute spec [ "1" ] [ srcRow "1" (Some "src") ] [ brRow "1" (Some "old") ]
    Assert.Equal(BridgeRowVerdict.IdentityConflict ("old", "src"), verdictOf "1" plan)
    Assert.Empty plan.Inserts
    Assert.Empty plan.IdentityUpdates

[<Fact>]
let ``more than one bridge row for a key BLOCKS (ambiguous resolution)`` () =
    let plan = BridgeRowDelta.compute spec [ "1" ] [ srcRow "1" (Some "x") ] [ brRow "1" (Some "x"); brRow "1" (Some "y") ]
    Assert.Equal(BridgeRowVerdict.AmbiguousBridgeRows 2, verdictOf "1" plan)

[<Fact>]
let ``more than one source row for a key BLOCKS (no single row to derive from)`` () =
    let plan = BridgeRowDelta.compute spec [ "1" ] [ srcRow "1" (Some "x"); srcRow "1" (Some "y") ] []
    Assert.Equal(BridgeRowVerdict.AmbiguousSourceRows 2, verdictOf "1" plan)

// ---------------------------------------------------------------------------
// Determinism + staged ⇔ decided.
// ---------------------------------------------------------------------------

[<Fact>]
let ``referenced keys dedupe and ordinal-sort; every key gets exactly one verdict`` () =
    let plan = BridgeRowDelta.compute spec [ "2"; "1"; "2"; "10" ] [] []
    Assert.Equal<string list>([ "1"; "10"; "2" ], plan.Verdicts |> List.map fst)

[<Fact>]
let ``staged changes project from the verdict ledger (staged equals decided)`` () =
    let plan =
        BridgeRowDelta.compute spec
            [ "1"; "2"; "3" ]
            [ srcRow "1" (Some "a"); srcRow "2" (Some "b"); srcRow "3" (Some "c") ]
            [ brRow "2" None; brRow "3" (Some "c") ]
    // 1 → insert; 2 → update; 3 → complete.
    Assert.Equal<string list>([ "1" ], plan.Inserts |> List.map (fun i -> i.Key))
    Assert.Equal<string list>([ "2" ], plan.IdentityUpdates |> List.map (fun u -> u.Key))
    let staged =
        plan.Verdicts
        |> List.filter (fun (_, v) ->
            match v with
            | BridgeRowVerdict.StageInsert | BridgeRowVerdict.StageIdentityUpdate -> true
            | _ -> false)
        |> List.map fst
    Assert.Equal<string list>([ "1"; "2" ], staged)

// ---------------------------------------------------------------------------
// Planned evidence — fail-closed, honest counts.
// ---------------------------------------------------------------------------

[<Fact>]
let ``a fully-clean plan yields Present evidence with zero counts`` () =
    let plan = BridgeRowDelta.compute spec [ "1" ] [ srcRow "1" (Some "x") ] [ brRow "1" (Some "x") ]
    match BridgeRowDelta.plannedEvidence cleanWide plan with
    | Some ev ->
        Assert.Equal(0L, ev.UnresolvedThroughBridge)
        Assert.Equal(0L, ev.BrokenOriginalParent)
        Assert.Equal(0L, ev.PayloadConflicts)
        Assert.Equal(BridgeIdentityEvidence.Present, ev.IdentityEvidence)
    | None -> Assert.Fail "expected derived evidence for a clean plan"

[<Fact>]
let ``a plan that stages safely still yields evidence (planned post-staging state)`` () =
    let plan =
        BridgeRowDelta.compute spec
            [ "1"; "2" ]
            [ srcRow "1" (Some "a"); srcRow "2" (Some "b") ]
            [ brRow "2" None ]
    Assert.True((BridgeRowDelta.plannedEvidence cleanWide plan).IsSome)

[<Fact>]
let ``ANY blocking verdict withholds the evidence entirely (fail-closed)`` () =
    let plan =
        BridgeRowDelta.compute spec
            [ "1"; "2" ]
            [ srcRow "1" (Some "a") ]   // key 2 has no source row → block
            [ brRow "1" (Some "a") ]
    Assert.Equal(None, BridgeRowDelta.plannedEvidence cleanWide plan)
    Assert.Equal(1, List.length (BridgeRowDelta.blockingVerdicts plan))

[<Fact>]
let ``bridge-WIDE key stats pass through to the evidence (unsoundness outside the subset still gates)`` () =
    let plan = BridgeRowDelta.compute spec [ "1" ] [ srcRow "1" (Some "x") ] [ brRow "1" (Some "x") ]
    match BridgeRowDelta.plannedEvidence { KeyNullCount = 3L; KeyDuplicateCount = 2L } plan with
    | Some ev ->
        Assert.Equal(3L, ev.BridgeKeyNulls)
        Assert.Equal(2L, ev.BridgeKeyDuplicates)
        // ...and evaluating a profile with these facts BLOCKS the retarget.
        let profile =
            { BridgeRetargetProfile.clean "st1" with
                BridgeKeyNullCount = ev.BridgeKeyNulls
                BridgeKeyDuplicateCount = ev.BridgeKeyDuplicates }
        Assert.False(BridgeRetarget.retargetCleared (BridgeRetarget.evaluate profile))
    | None -> Assert.Fail "expected derived evidence"

[<Fact>]
let ``an unverified AlreadyComplete downgrades identity provenance to Ambiguous (warn, not block)`` () =
    let plan = BridgeRowDelta.compute spec [ "1" ] [ srcRow "1" None ] [ brRow "1" (Some "x") ]
    match BridgeRowDelta.plannedEvidence cleanWide plan with
    | Some ev -> Assert.Equal(BridgeIdentityEvidence.Ambiguous, ev.IdentityEvidence)
    | None -> Assert.Fail "expected derived evidence (unverified is a warning, never a block)"
