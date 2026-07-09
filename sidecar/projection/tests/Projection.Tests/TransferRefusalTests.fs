module Projection.Tests.TransferRefusalTests

open Xunit
open Projection.Core
open Projection.Pipeline

// 6.A.2 / 6.A.3 — pure (DB-free) witnesses for the Transfer Execute-time
// surrogate-capture refusals. The data canary drives the SAME decision
// against a live container (the 6.A.1 pattern: a pure decision function the
// canary and the fast pool both witness). These tests pin the two
// `AssignedBySink` shapes the per-row capture path cannot honor and the
// precedence of `executeGate`.

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_TEST_REFUSE" parts |> mustOk
let private mkName (s: string) : Name = Name.create s |> mustOk

// --- catalog builders (composite-PK refusal needs the schema contract) ---

let private pk (ownerParts: string list) (col: string) (isIdentity: bool) : Attribute =
    { Attribute.create (mkKey (ownerParts @ [col])) (mkName col) Integer with
        Column       = ColumnRealization.create (col) (false) |> Result.value
        IsPrimaryKey = true
        IsIdentity   = isIdentity
        IsMandatory  = true }

let private kindOf (parts: string list) (table: string) (attrs: Attribute list) : Kind =
    { Kind.create (mkKey parts) (mkName (List.last parts))
        (TableId.create "dbo" table |> Result.value)
        attrs
      with References = []; Indexes = []; ColumnChecks = [] }

// A composite IDENTITY PK ([ID] identity + [TENANT]); a single IDENTITY PK.
let private compositeKey = mkKey ["Composite"]
let private singleKey    = mkKey ["Single"]

let private compositeKind =
    kindOf ["Composite"] "OSUSR_COMPOSITE"
        [ pk ["Composite"] "ID" true; pk ["Composite"] "TENANT" false ]
let private singleKind =
    kindOf ["Single"] "OSUSR_SINGLE" [ pk ["Single"] "ID" true ]

let private catalog : Catalog =
    IRBuilders.mkCatalog
        [ IRBuilders.mkModule (mkKey ["Module"]) (mkName "M") [ compositeKind; singleKind ] ]

// --- plan builders (the predicates read Disposition + DeferredFkColumns) --

let private load (key: SsKey) (disp: IdentityDisposition) (deferred: string list) : DataLoadKind =
    { Kind              = key
      Disposition       = disp
      DeferredFkColumns = deferred |> List.map mkName |> Set.ofList
      Rows              = [] }

let private planOf (loads: DataLoadKind list) : DataLoadPlan =
    { Loads = loads; UnbreakableCycleFks = []; SkippedReferences = []; DroppedRows = [] }

// --- 6.A.2 LIFTED (operator-authorized 2026-06-10) ------------------------
// A cyclic AssignedBySink shape no longer refuses: Phase 2 re-points the
// deferred FK through the COMPLETED remap and keys its WHERE on the
// ASSIGNED PK the capture supplied. The gate passing is the pure pin of
// the lift; the Docker canary proves the loaded chain by business key.

[<Fact>]
let ``6.A.2 lifted: an AssignedBySink kind with a deferred FK passes the execute gate (phase 2 keys on the assigned PK)`` () =
    let plan = planOf [ load singleKey IdentityDisposition.AssignedBySink [ "MANAGER_ID" ] ]
    Assert.True((Transfer.executeGate catalog plan).IsNone)

// --- 6.A.3: composite-identity AssignedBySink ----------------------------

[<Fact>]
let ``6.A.3: an AssignedBySink kind with a multi-column PK is flagged composite`` () =
    let plan = planOf [ load compositeKey IdentityDisposition.AssignedBySink [] ]
    Assert.Equal<SsKey list>([ compositeKey ], Transfer.compositeAssignedBySinkKinds catalog plan)

[<Fact>]
let ``6.A.3: an AssignedBySink kind with a single-column PK is not composite`` () =
    let plan = planOf [ load singleKey IdentityDisposition.AssignedBySink [] ]
    Assert.Empty(Transfer.compositeAssignedBySinkKinds catalog plan)

// --- executeGate precedence ----------------------------------------------

[<Fact>]
let ``executeGate: an unbreakable cycle FK refuses before the capture shapes`` () =
    let plan =
        { planOf [ load singleKey IdentityDisposition.AssignedBySink [ "MANAGER_ID" ] ] with
            UnbreakableCycleFks = [ { Kind = singleKey; Column = mkName "X"; Target = singleKey } ] }
    match Transfer.executeGate catalog plan with
    | Some e -> Assert.Equal("transfer.unbreakableCycleFk", e.Code)
    | None   -> Assert.Fail("expected the unsatisfiable-cycle refusal")

[<Fact>]
let ``executeGate: composite-identity AssignedBySink refuses with transfer.compositeSurrogateUnsupported`` () =
    let plan = planOf [ load compositeKey IdentityDisposition.AssignedBySink [] ]
    match Transfer.executeGate catalog plan with
    | Some e -> Assert.Equal("transfer.compositeSurrogateUnsupported", e.Code)
    | None   -> Assert.Fail("expected the composite-surrogate refusal")

[<Fact>]
let ``executeGate: a clean single-PK AssignedBySink plan passes`` () =
    let plan = planOf [ load singleKey IdentityDisposition.AssignedBySink [] ]
    Assert.True((Transfer.executeGate catalog plan).IsNone)

// --- T1.5: the identity-insert gate (PreservedFromSource onto an IDENTITY PK) --
// A load that writes explicit source PKs into an IDENTITY column was gated
// NOWHERE. The detection is plan-derivable (so board and engine gate one fact);
// the gate refuses transfer.writeSignoff.ungreenlit unless the flow greenlights
// `identity-insert`. Fires on ANY Execute (WipeAndLoad OR Incremental/merge).

[<Fact>]
let ``T1.5 identityInsertTables: a PreservedFromSource load onto an IDENTITY-PK kind is identity-insert`` () =
    let plan = planOf [ load singleKey IdentityDisposition.PreservedFromSource [] ]
    Assert.Equal<string list>([ "Single" ], Transfer.identityInsertTables catalog plan)

[<Fact>]
let ``T1.5 identityInsertTables: an AssignedBySink load (the sink mints) is NOT identity-insert`` () =
    let plan = planOf [ load singleKey IdentityDisposition.AssignedBySink [] ]
    Assert.Empty(Transfer.identityInsertTables catalog plan)

[<Fact>]
let ``T1.5 identityInsertTables: a PreservedFromSource load onto a NON-identity (business) PK is NOT identity-insert`` () =
    let bizKey = mkKey ["Biz"]
    let bizKind = kindOf ["Biz"] "OSUSR_BIZ" [ pk ["Biz"] "CODE" false ]
    let bizCatalog = IRBuilders.mkCatalog [ IRBuilders.mkModule (mkKey ["M2"]) (mkName "M2") [ bizKind ] ]
    let plan = planOf [ load bizKey IdentityDisposition.PreservedFromSource [] ]
    Assert.Empty(Transfer.identityInsertTables bizCatalog plan)

[<Fact>]
let ``T1.5 gate: identity-insert with no signoff is Missing (ungreenlit); a greenlight Confirms it`` () =
    let tables = Transfer.identityInsertTables catalog (planOf [ load singleKey IdentityDisposition.PreservedFromSource [] ])
    match WriteSignoff.verify "the flow" [] WriteSignoff.WriteMode.IdentityInsert tables with
    | WriteSignoff.Missing _ -> ()
    | other -> Assert.Fail(sprintf "expected Missing (ungreenlit), got %A" other)
    match WriteSignoff.verify "the flow" [ WriteSignoff.greenlit WriteSignoff.WriteMode.IdentityInsert ] WriteSignoff.WriteMode.IdentityInsert tables with
    | WriteSignoff.Confirmed _ -> ()
    | other -> Assert.Fail(sprintf "expected Confirmed, got %A" other)

// --- AC-I5: validate-user-map pre-write halt ------------------------------
//
// The orphan drop-set is fully resolved post-reconcile but PRE-write
// (`reconcileAgainstSink` is read-only). `validateUserMap` refuses at Execute
// time so an unmapped identity is a pre-write halt (the Sink stays untouched),
// not a post-write exit-9. These pure witnesses pin the gate the data canary
// runs through with `--allow-drops` (the 6.A.1 same-decision pattern).

let private reconciledWith (unmatched: (SsKey * SourceKey) list) : ReconciledIdentity =
    { Remap = SurrogateRemapContext.empty; Unmatched = unmatched; UnmatchedRows = []; Divergences = []; Ambiguous = []; AmbiguousTargetKeys = []; MissingPinnedOwners = [] }

[<Fact>]
let ``AC-I5: an unmatched Source identity refuses pre-write with transfer.unmappedIdentities`` () =
    let reconciled = reconciledWith [ singleKey, SourceKey.ofString "999" ]
    match Transfer.validateUserMap false reconciled with
    | Some e -> Assert.Equal("transfer.unmappedIdentities", e.Code)
    | None   -> Assert.Fail("expected the validate-user-map pre-write halt")

[<Fact>]
let ``AC-I5: a fully-mapped user-map passes the pre-write gate`` () =
    Assert.True((Transfer.validateUserMap false (reconciledWith [])).IsNone)

[<Fact>]
let ``AC-I5: --allow-drops downgrades the orphan to the post-write reported-drop path`` () =
    let reconciled = reconciledWith [ singleKey, SourceKey.ofString "999" ]
    Assert.True((Transfer.validateUserMap true reconciled).IsNone)

// --- A1: cdcTrackedSink refusal routes through the Preflight classify seam ---
//
// `runTransfer`'s refusal branch derives its exit through `Preflight.refusalOf`
// (the single `classify` seam) rather than a hand-rolled if/elif. The CDC
// pre-flight refusal (`transfer.cdcTrackedSink`, raised on the Execute path in
// `TransferRun.runCore`) must therefore exit 9 (`CdcTrackedSink`), not the
// generic `else 3` the prior hand-derivation produced. This witnesses the seam
// the CLI now shares with the gate composition (cf. PreflightAllTests `classify`).

[<Fact>]
let ``A1: a transfer.cdcTrackedSink refusal classifies to exit 9 through Preflight.refusalOf`` () =
    let refusal =
        Preflight.refusalOf
            [ ValidationError.create "transfer.cdcTrackedSink"
                "Sink has CDC-tracked table(s); refusing --execute." ]
    Assert.Equal(9, refusal.ExitCode)
    Assert.Equal(Preflight.CdcTrackedSink, refusal.Label)

// --- PE-1 / P-REKEY: the golden wipe-set (exclude users, never wipe them) ---
//
// `wipeTargets` is the pure core of `wipeFkOrdered`. The golden cloud->cloud
// flow excludes the User family from the copied set: a ReconciledByRule kind's
// sink rows are the sink's OWN (matched by email), so a full-refresh wipe must
// NEVER delete them; and a kind outside the declared subset (LoadSet) is left
// untouched. These pure witnesses pin the WipeAndLoad-safe behavior the data
// canary drives end-to-end against a container (the 6.A.1 same-decision pattern).

let private userKey  = mkKey ["User"]
let private orderKey = mkKey ["Order"]
let private otherKey = mkKey ["Other"]
let private topoOf (keys: SsKey list) : TopologicalOrder = { TopologicalOrder.empty with Order = keys }

[<Fact>]
let ``PE-1/P-REKEY: the wipe never targets a ReconciledByRule kind (golden users are not wiped)`` () =
    let plan =
        planOf
            [ load userKey  IdentityDisposition.ReconciledByRule []
              load orderKey IdentityDisposition.PreservedFromSource [] ]
    let targets = TransferResume.wipeTargets plan (topoOf [ userKey; orderKey ]) None
    Assert.DoesNotContain(userKey, targets)
    Assert.Contains(orderKey, targets)

[<Fact>]
let ``PE-1: the wipe respects the LoadSet (a kind outside the declared subset is untouched)`` () =
    let plan =
        planOf
            [ load orderKey IdentityDisposition.PreservedFromSource []
              load otherKey IdentityDisposition.PreservedFromSource [] ]
    let targets = TransferResume.wipeTargets plan (topoOf [ orderKey; otherKey ]) (Some (Set.ofList [ orderKey ]))
    Assert.Equal<SsKey list>([ orderKey ], targets)

[<Fact>]
let ``PE-1: with no LoadSet the wipe targets every non-reconciled loaded kind, child-first`` () =
    let plan =
        planOf
            [ load orderKey IdentityDisposition.PreservedFromSource []
              load otherKey IdentityDisposition.PreservedFromSource [] ]
    // topo [Order; Other] reversed (child-first) = [Other; Order]
    let targets = TransferResume.wipeTargets plan (topoOf [ orderKey; otherKey ]) None
    Assert.Equal<SsKey list>([ otherKey; orderKey ], targets)

// --- T1.7: the revert wrong-sink guard is fail-CLOSED --------------------
// A `revert` deletes BY KEY in whatever --against resolves to. The guard now
// refuses a header-less script (unverifiable) AND a server- or database-
// mismatch — only --force proceeds. Pure `guardVerdict`; the CLI face is thin.

[<Fact>]
let ``revert guard: a header-less undo is REFUSED (revert.headerMissing) unless --force`` () =
    let p = TransferRevert.parseProvenance [ "DELETE FROM [dbo].[X] WHERE [ID] IN (1);" ]
    Assert.False p.HasHeader
    match TransferRevert.guardVerdict false p "srvA" "dbA" with
    | Some e -> Assert.Equal("revert.headerMissing", e.Code)
    | None   -> Assert.Fail "a header-less undo must refuse fail-closed"
    Assert.Equal(None, TransferRevert.guardVerdict true p "srvA" "dbA")   // --force overrides

[<Fact>]
let ``revert guard: a database mismatch refuses (revert.sinkMismatch)`` () =
    let p = TransferRevert.parseProvenance [ "-- projection:undo server=srvA database=dbA generated=t"; "DELETE ...;" ]
    Assert.True p.HasHeader
    match TransferRevert.guardVerdict false p "srvA" "dbB" with
    | Some e -> Assert.Equal("revert.sinkMismatch", e.Code)
    | None   -> Assert.Fail "a different database must refuse"

[<Fact>]
let ``revert guard: a SERVER mismatch refuses even when the database name matches`` () =
    // The prior guard compared database ONLY — a same-named DB on a different
    // server passed. The server is now checked too.
    let p = TransferRevert.parseProvenance [ "-- projection:undo server=srvA database=dbA generated=t" ]
    match TransferRevert.guardVerdict false p "srvB" "dbA" with
    | Some e -> Assert.Equal("revert.sinkMismatch", e.Code)
    | None   -> Assert.Fail "a different server must refuse even on a matching database"

[<Fact>]
let ``revert guard: a matching server+database passes; comparison is case-insensitive`` () =
    let p = TransferRevert.parseProvenance [ "-- projection:undo server=SrvA database=DbA generated=t" ]
    Assert.Equal(None, TransferRevert.guardVerdict false p "srva" "dba")

[<Fact>]
let ``revert guard: a legacy header without server= still verifies on database=`` () =
    let p = TransferRevert.parseProvenance [ "-- projection:undo database=dbA generated=t" ]
    Assert.Equal(None, p.Server)
    Assert.Equal(None, TransferRevert.guardVerdict false p "anySrv" "dbA")   // matching db passes
    Assert.True((TransferRevert.guardVerdict false p "anySrv" "dbB").IsSome)   // mismatching db still refuses
