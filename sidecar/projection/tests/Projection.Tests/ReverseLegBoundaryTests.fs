module Projection.Tests.ReverseLegBoundaryTests

// LE-3 Tier 5 — the boundary map: every shape the reverse leg refuses or
// defers, pinned live or reserved by a Skip stub with its promotion
// trigger (the house convention). The live refusal codes for the engine
// shapes (unbreakable cycle / cyclic AssignedBySink / composite IDENTITY)
// are pinned by `TransferRefusalTests` (pure), `TransferCanaryTests`
// (Docker), and the refusal-totality property (`ReverseLegPropertyTests`);
// this file pins the CLI face's reverse-leg-specific refusal and reserves
// the named follow-ons.

open Xunit
open Projection.Core
open Projection.Cli

let private nm (s: string) : Name = Name.create s |> Result.value
let private kKey (s: string) : SsKey = SsKey.synthesizedComposite "L3B_KIND" [ s ] |> Result.value
let private aKey (k: string) (a: string) : SsKey = SsKey.synthesizedComposite "L3B_ATTR" [ k; a ] |> Result.value

/// A minimal authored model for face-level argument tests (the face refuses
/// before any connection is opened, so the contracts are never dereferenced).
let private tinyModel : Catalog =
    let kind =
        Kind.create (kKey "Customer") (nm "Customer")
            (TableId.create "dbo" "OSUSR_B_CUSTOMER" |> Result.value)
            [ { Attribute.create (aKey "Customer" "Id") (nm "Id") Integer with
                  Column       = ColumnRealization.create "ID" false |> Result.value
                  IsPrimaryKey = true
                  IsIdentity   = true
                  IsMandatory  = true } ]
    Catalog.create
        [ { SsKey = kKey "Module"; Name = nm "B"; Kinds = [ kind ]
            IsActive = true; ExtendedProperties = [] } ] []
    |> Result.value

// -- the CLI face's named reverse-leg refusal (live) --------------------------

[<Fact>]
let ``reverse-leg face: a reconcile spec is REFUSED BY NAME (transfer.reverseLeg.reconcileUnsupported, exit 2) — never a silent straight-load`` () =
    let model = tinyModel
    let exit =
        RunFaces.runReverseLegTransfer
            "env:L3B_SRC" "env:L3B_SINK"
            (Projection.Pipeline.CatalogRendition.logical model)
            (Projection.Pipeline.CatalogRendition.physical model)
            [ "Customer=Email" ] None
            false true false EmissionMode.Incremental false [] []
    Assert.Equal(2, exit)

[<Fact>]
let ``reverse-leg face: a user-map is REFUSED BY NAME on the reverse leg (the rekey ∘ rendition composition is the named follow-on)`` () =
    let model = tinyModel
    let exit =
        RunFaces.runReverseLegTransfer
            "env:L3B_SRC" "env:L3B_SINK"
            (Projection.Pipeline.CatalogRendition.logical model)
            (Projection.Pipeline.CatalogRendition.physical model)
            [] (Some "user-map.csv")
            false true false EmissionMode.Incremental false [] []
    Assert.Equal(2, exit)

// -- reserved follow-on contracts (Skip stubs with promotion triggers) --------

[<Fact(Skip = "Reserved: the reconcile ∘ rendition composition — 'User reconciled by email on the up-leg' (the cloud-owns-its-users reverse leg). The CLI face refuses today (transfer.reverseLeg.reconcileUnsupported); promotion trigger: ReconciledByRule threaded through runReverseLegThroughConnections with rendition-aware business-key resolution, then this test seeds the cloud sink's own user inventory and asserts the up-leg re-keys every user FK without re-importing a user row (the PE-3 join witness on the reverse leg).")>]
let ``RESERVED — reverse leg with ReconciledByRule: User reconciled by email on the up-leg, identities re-keyed, never re-imported`` () = ()

[<Fact(Skip = "Reserved: lifting transfer.cyclicAssignedBySink — a nullable self-FK on an IDENTITY-PK kind (User.ManagerId, Employee.CreatedBy) refuses the WHOLE load today because phase-2's UPDATE keys on the source PK the sink replaced (TransferRun.fs phase2UpdateSql). The lift is tractable — the SurrogateRemapContext already knows source→assigned, so phase 2 can key the WHERE on the ASSIGNED PK — but it is an engine change for the operator to authorize (see the LE-3 report, finding F1). Promotion trigger: the operator authorizes the phase-2 re-key; then this test loads a self-referencing IDENTITY kind B->A and asserts the manager chain joins by business key.")>]
let ``RESERVED — cyclic AssignedBySink lifted: phase-2 re-points the deferred self-FK keyed on the ASSIGNED PK through the captured remap`` () = ()

[<Fact(Skip = "Reserved: the contract-vs-live-shape preflight — a column the rendered logical contract names but live B lacks dies today inside the ingest SELECT with a raw SqlException (pinned by ReverseLegCanaryTests 'B-drift'). Promotion trigger: a named transfer.sourceShapeDrift refusal lands (compare the rendered contract against the live INFORMATION_SCHEMA before any read); then this test drops a column from live B and asserts the named refusal replaces the raw crash.")>]
let ``RESERVED — B-drift refused by name: the rendered contract is checked against live B's shape before any row is read`` () = ()

[<Fact(Skip = "Reserved: object-scope grant evidence — Preflight.captureGrantEvidence reads sys.fn_my_permissions at DATABASE scope only, so a table-level DENY INSERT passes the preflight and crashes mid-load with a PARTIAL write (pinned by ReverseLegCanaryTests 'NAMED GAP'). Promotion trigger: the survey-gated P1 object-scope refinement lands (per-table fn_my_permissions probes for the planned writes); then this test DENIES INSERT on one table and asserts transfer.insufficientGrant BEFORE any row moves.")>]
let ``RESERVED — object-scope DENY refused by name before any write: the grant probe descends to table scope`` () = ()
