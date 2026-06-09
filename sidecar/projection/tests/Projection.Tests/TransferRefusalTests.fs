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
    { Loads = loads; UnbreakableCycleFks = []; SkippedReferences = [] }

// --- 6.A.2: cyclic AssignedBySink ----------------------------------------

[<Fact>]
let ``6.A.2: an AssignedBySink kind with a deferred FK is flagged cyclic`` () =
    let plan = planOf [ load singleKey IdentityDisposition.AssignedBySink [ "MANAGER_ID" ] ]
    Assert.Equal<SsKey list>([ singleKey ], Transfer.cyclicAssignedBySinkKinds plan)

[<Fact>]
let ``6.A.2: an AssignedBySink kind with no deferred FK is not cyclic`` () =
    let plan = planOf [ load singleKey IdentityDisposition.AssignedBySink [] ]
    Assert.Empty(Transfer.cyclicAssignedBySinkKinds plan)

[<Fact>]
let ``6.A.2: a deferred FK on a PreservedFromSource kind is not a cyclic-AssignedBySink case`` () =
    // The deferred-self-FK two-phase load is correct when the key is not
    // sink-minted — only AssignedBySink loses the source PK.
    let plan = planOf [ load singleKey IdentityDisposition.PreservedFromSource [ "MANAGER_ID" ] ]
    Assert.Empty(Transfer.cyclicAssignedBySinkKinds plan)

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
let ``executeGate: cyclic AssignedBySink refuses with transfer.cyclicAssignedBySink`` () =
    let plan = planOf [ load singleKey IdentityDisposition.AssignedBySink [ "MANAGER_ID" ] ]
    match Transfer.executeGate catalog plan with
    | Some e -> Assert.Equal("transfer.cyclicAssignedBySink", e.Code)
    | None   -> Assert.Fail("expected the cyclic-AssignedBySink refusal")

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

// --- AC-I5: validate-user-map pre-write halt ------------------------------
//
// The orphan drop-set is fully resolved post-reconcile but PRE-write
// (`reconcileAgainstSink` is read-only). `validateUserMap` refuses at Execute
// time so an unmapped identity is a pre-write halt (the Sink stays untouched),
// not a post-write exit-9. These pure witnesses pin the gate the data canary
// runs through with `--allow-drops` (the 6.A.1 same-decision pattern).

let private reconciledWith (unmatched: (SsKey * SourceKey) list) : ReconciledIdentity =
    { Remap = SurrogateRemapContext.empty; Unmatched = unmatched }

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
