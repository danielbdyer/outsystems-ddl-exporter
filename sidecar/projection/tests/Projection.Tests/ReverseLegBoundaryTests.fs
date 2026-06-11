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
            false true false EmissionMode.Incremental false false None [] []
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
            false true false EmissionMode.Incremental false false None [] []
    Assert.Equal(2, exit)

// -- the realization SELECTOR: the best admissible realization, chosen pure --

let private choose emission resumable tables streamingRequested journal =
    Projection.Pipeline.ReverseLegRealization.choose emission resumable tables streamingRequested journal

[<Fact>]
let ``selector: an admissible request streams AUTOMATICALLY — the dominant realization needs no flag`` () =
    match choose EmissionMode.Incremental false [] false None with
    | Ok (Projection.Pipeline.ReverseLegRealization.Streaming None) -> ()
    | other -> Assert.Fail(sprintf "expected auto Streaming, got %A" other)

[<Fact>]
let ``selector: --journal alone rides the auto-selected streaming realization`` () =
    match choose EmissionMode.Incremental false [] false (Some "/j") with
    | Ok (Projection.Pipeline.ReverseLegRealization.Streaming (Some "/j")) -> ()
    | other -> Assert.Fail(sprintf "expected Streaming with journal, got %A" other)

[<Fact>]
let ``selector: a table subset falls back to the materialized path (streaming does not yet support it) — no flag, no refusal`` () =
    match choose EmissionMode.Incremental false [ "Customer" ] false None with
    | Ok Projection.Pipeline.ReverseLegRealization.Materialized -> ()
    | other -> Assert.Fail(sprintf "expected Materialized fallback, got %A" other)

[<Fact>]
let ``selector: WipeAndLoad and --resumable fall back to the materialized path`` () =
    match choose EmissionMode.WipeAndLoad false [] false None with
    | Ok Projection.Pipeline.ReverseLegRealization.Materialized -> ()
    | other -> Assert.Fail(sprintf "expected Materialized for WipeAndLoad, got %A" other)
    match choose EmissionMode.Incremental true [] false None with
    | Ok Projection.Pipeline.ReverseLegRealization.Materialized -> ()
    | other -> Assert.Fail(sprintf "expected Materialized for --resumable, got %A" other)

[<Fact>]
let ``selector: an EXPLICIT --streaming on an inadmissible request refuses BY NAME — never a silent downgrade`` () =
    let codeOf r = match r with Error (es: ValidationError list) -> (List.head es).Code | Ok _ -> "OK"
    Assert.Equal("transfer.reverseLeg.streamingTablesUnsupported", codeOf (choose EmissionMode.Incremental false [ "Customer" ] true None))
    Assert.Equal("transfer.reverseLeg.streamingResumableUnsupported", codeOf (choose EmissionMode.Incremental true [] true None))
    Assert.Equal("transfer.reverseLeg.streamingWipeUnsupported", codeOf (choose EmissionMode.WipeAndLoad false [] true None))

[<Fact>]
let ``selector: --journal on an inadmissible request refuses by name — the ledger belongs to the streaming realization`` () =
    match choose EmissionMode.Incremental false [ "Customer" ] false (Some "/j") with
    | Error es -> Assert.Equal("transfer.reverseLeg.journalRequiresStreaming", (List.head es).Code)
    | Ok other -> Assert.Fail(sprintf "expected the journal refusal, got %A" other)

[<Fact>]
let ``streaming face: an explicit --streaming with --tables refuses at the face with exit 2`` () =
    let model = tinyModel
    let exit =
        RunFaces.runReverseLegTransfer
            "env:L3B_SRC" "env:L3B_SINK"
            (Projection.Pipeline.CatalogRendition.logical model)
            (Projection.Pipeline.CatalogRendition.physical model)
            [] None
            false true false EmissionMode.Incremental false true None [ "Customer" ] []
    Assert.Equal(2, exit)

// -- reserved follow-on contracts (Skip stubs with promotion triggers) --------

[<Fact(Skip = "Reserved: the reconcile ∘ rendition composition — 'User reconciled by email on the up-leg' (the cloud-owns-its-users reverse leg). The CLI face refuses today (transfer.reverseLeg.reconcileUnsupported); promotion trigger: ReconciledByRule threaded through runReverseLegThroughConnections with rendition-aware business-key resolution, then this test seeds the cloud sink's own user inventory and asserts the up-leg re-keys every user FK without re-importing a user row (the PE-3 join witness on the reverse leg).")>]
let ``RESERVED — reverse leg with ReconciledByRule: User reconciled by email on the up-leg, identities re-keyed, never re-imported`` () = ()

[<Fact(Skip = "Reserved: the contract-vs-live-shape preflight — a column the rendered logical contract names but live B lacks dies today inside the ingest SELECT with a raw SqlException (pinned by ReverseLegCanaryTests 'B-drift'). Promotion trigger: a named transfer.sourceShapeDrift refusal lands (compare the rendered contract against the live INFORMATION_SCHEMA before any read); then this test drops a column from live B and asserts the named refusal replaces the raw crash.")>]
let ``RESERVED — B-drift refused by name: the rendered contract is checked against live B's shape before any row is read`` () = ()

[<Fact(Skip = "Reserved: object-scope grant evidence — Preflight.captureGrantEvidence reads sys.fn_my_permissions at DATABASE scope only, so a table-level DENY INSERT passes the preflight and crashes mid-load with a PARTIAL write (pinned by ReverseLegCanaryTests 'NAMED GAP'). Promotion trigger: the survey-gated P1 object-scope refinement lands (per-table fn_my_permissions probes for the planned writes); then this test DENIES INSERT on one table and asserts transfer.insufficientGrant BEFORE any row moves.")>]
let ``RESERVED — object-scope DENY refused by name before any write: the grant probe descends to table scope`` () = ()
