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

// -- the CLI face's reconcile spec handling (Phase 2: the refusal is LIFTED) ---
//
// The blanket `transfer.reverseLeg.reconcileUnsupported` refusal that stood
// here is gone (DECISIONS 2026-06-15 — reconcile ∘ reverse leg). The face now
// parses + resolves reconcile/user-map specs against the physical sink
// contract exactly as the forward face does. A bad spec still refuses by name
// (arg error, exit 2) BEFORE any connection opens; a good spec is ACCEPTED and
// proceeds to the apparatus (the full re-key witness is the Docker
// `ReverseLegStreamingTests` 'reconcile ∘ streaming' pair). The reverse-leg
// physical rendition of `tinyModel`'s one kind is `OSUSR_B_CUSTOMER([ID])`.

[<Fact>]
let ``reverse-leg face: a MALFORMED reconcile spec refuses by name (arg error, exit 2) before any connection opens`` () =
    let model = tinyModel
    let exit =
        Faces.Transfer.runReverseLegTransfer
            "env:L3B_SRC" "env:L3B_SINK"
            (Projection.Pipeline.CatalogRendition.logical model)
            (Projection.Pipeline.CatalogRendition.physical model)
            [ "Customer" ] None   // no ':' — transfer.reconcile.specShape
            false true false EmissionMode.Incremental false false None [] Projection.Pipeline.RevertPolicy.Script None SinkLoadCapability.structural
    Assert.Equal(2, exit)

[<Fact>]
let ``reverse-leg face: a reconcile spec naming an unknown table refuses by name (exit 2) before any connection opens`` () =
    let model = tinyModel
    let exit =
        Faces.Transfer.runReverseLegTransfer
            "env:L3B_SRC" "env:L3B_SINK"
            (Projection.Pipeline.CatalogRendition.logical model)
            (Projection.Pipeline.CatalogRendition.physical model)
            [ "OSUSR_NOPE:ID" ] None   // table not in the contract — transfer.reconcile.tableNotFound
            false true false EmissionMode.Incremental false false None [] Projection.Pipeline.RevertPolicy.Script None SinkLoadCapability.structural
    Assert.Equal(2, exit)

[<Fact>]
let ``reverse-leg face: a WELL-FORMED resolvable reconcile spec is ACCEPTED (no longer refused) — it passes the spec gate and reaches the apparatus`` () =
    let model = tinyModel
    let exit =
        Faces.Transfer.runReverseLegTransfer
            "env:L3B_SRC" "env:L3B_SINK"
            (Projection.Pipeline.CatalogRendition.logical model)
            (Projection.Pipeline.CatalogRendition.physical model)
            [ "OSUSR_B_CUSTOMER:ID" ] None   // resolves to MatchByColumn (Id)
            false true false EmissionMode.Incremental false false None [] Projection.Pipeline.RevertPolicy.Script None SinkLoadCapability.structural
    // Past the parse/resolve gate the run reaches connection-opening, which
    // fails on the unset env vars — a connection-class exit, NEVER the arg/
    // resolve exit 2. The point: reconcile is no longer refused at the face.
    Assert.NotEqual(2, exit)

// -- the realization SELECTOR: the best admissible realization, chosen pure --

// The selector helper defaults `sinkResidentResumeAvailable = false` (the
// ManagedDml / undeclared sink — byte-identical to the pre-Slice-C cloud shape);
// the FullRights (`true`) fork is pinned by its own Slice-C2 cell below.
let private choose emission resumable tables streamingRequested journal =
    Projection.Pipeline.ReverseLegRealization.choose emission resumable tables streamingRequested journal false

let private chooseOn emission resumable tables streamingRequested journal sinkResidentResume =
    Projection.Pipeline.ReverseLegRealization.choose emission resumable tables streamingRequested journal sinkResidentResume

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
let ``Slice C2: the resume chooser reads the sink archetype — a FullRights sink ADMITS streaming+resumable on the materialized envelope; ManagedDml still refuses`` () =
    let codeOf r = match r with Error (es: ValidationError list) -> (List.head es).Code | Ok _ -> "OK"
    // ManagedDml / undeclared (sinkResidentResume = false): the cloud-shaped
    // refusal stands — the data grant forbids the CREATE TABLE a sink-resident
    // progress table needs (byte-identical to the pre-Slice-C selector).
    Assert.Equal("transfer.reverseLeg.streamingResumableUnsupported", codeOf (chooseOn EmissionMode.Incremental true [] true None false))
    // FullRights (sinkResidentResume = true): the sink CAN host the G10
    // sink-resident progress table, so --resumable is HONORED on the
    // materialized envelope (the path that carries sink-resident resume) — the
    // archetype-correct admission, not the cloud-shaped refusal.
    match chooseOn EmissionMode.Incremental true [] true None true with
    | Ok Projection.Pipeline.ReverseLegRealization.Materialized -> ()
    | other -> Assert.Fail(sprintf "expected Materialized (sink-resident resume admitted), got %A" other)

[<Fact>]
let ``selector: --journal on an inadmissible request refuses by name — the ledger belongs to the streaming realization`` () =
    match choose EmissionMode.Incremental false [ "Customer" ] false (Some "/j") with
    | Error es -> Assert.Equal("transfer.reverseLeg.journalRequiresStreaming", (List.head es).Code)
    | Ok other -> Assert.Fail(sprintf "expected the journal refusal, got %A" other)

// -- Phase 3: the duplicate-hazard gate + the journal-address-drift guard ------

[<Fact>]
let ``Phase 3 gate: a journal-less streaming EXECUTE refuses by name; DryRun / journal-bearing / materialized pass`` () =
    let gate r e = Projection.Pipeline.ReverseLegRealization.executeJournalGate r e
    // journal-less streaming, gated execute → the named refusal
    match gate (Projection.Pipeline.ReverseLegRealization.Streaming None) true with
    | Some err -> Assert.Equal("transfer.reverseLeg.streamingExecuteRequiresJournal", err.Code)
    | None -> Assert.Fail "expected the streaming-execute-requires-journal refusal"
    // the same shape as a DryRun (not gated) is exempt — nothing is written
    Assert.True((gate (Projection.Pipeline.ReverseLegRealization.Streaming None) false).IsNone)
    // a journal-bearing stream is idempotent-capable → passes
    Assert.True((gate (Projection.Pipeline.ReverseLegRealization.Streaming (Some "/j")) true).IsNone)
    // the materialized arm carries its own G10 envelope → passes
    Assert.True((gate Projection.Pipeline.ReverseLegRealization.Materialized true).IsNone)

// (The face wiring of `executeJournalGate` mirrors the already-face-tested
// `streamingTablesUnsupported` refusal; the gate's logic is proven purely
// above, env-var-free, so the face path is not re-tested with a global
// PROJECTION_ALLOW_EXECUTE mutation that could flake concurrent suites.)

[<Fact>]
let ``Phase 3 address-drift: a sibling journal under a different marker signals drift; own-file-present or empty dir does not`` () =
    let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rl-drift-" + System.Guid.NewGuid().ToString("N").Substring(0, 8))
    try
        let j = Projection.Pipeline.CaptureJournal.create dir "marker-A"
        // a fresh dir holding only this (not-yet-written) journal → no drift
        Assert.Empty(Projection.Pipeline.CaptureJournal.siblingJournalsUnderDrift j)
        // a PRIOR run's journal under a different marker, our file still absent → drift
        let stray = Projection.Pipeline.CaptureJournal.create dir "marker-B"
        System.IO.File.WriteAllText(Projection.Pipeline.CaptureJournal.filePath stray, "")
        Assert.NotEmpty(Projection.Pipeline.CaptureJournal.siblingJournalsUnderDrift j)
        // once our OWN journal exists (a real resume), no drift
        System.IO.File.WriteAllText(Projection.Pipeline.CaptureJournal.filePath j, "")
        Assert.Empty(Projection.Pipeline.CaptureJournal.siblingJournalsUnderDrift j)
    finally
        if System.IO.Directory.Exists dir then System.IO.Directory.Delete(dir, true)

[<Fact>]
let ``streaming face: an explicit --streaming with --tables refuses at the face with exit 2`` () =
    let model = tinyModel
    let exit =
        Faces.Transfer.runReverseLegTransfer
            "env:L3B_SRC" "env:L3B_SINK"
            (Projection.Pipeline.CatalogRendition.logical model)
            (Projection.Pipeline.CatalogRendition.physical model)
            [] None
            false true false EmissionMode.Incremental false true None [ "Customer" ] Projection.Pipeline.RevertPolicy.Script None SinkLoadCapability.structural
    Assert.Equal(2, exit)

// -- reserved follow-on contracts (Skip stubs with promotion triggers) --------

// PROMOTED 2026-06-15 (Phase 2 — reconcile ∘ reverse leg): the reserved
// "User reconciled by email on the up-leg, identities re-keyed, never
// re-imported" contract is now the live Docker witness
// `ReverseLegStreamingTests.``reconcile ∘ streaming: User reconciled by email
// on the up-leg — identities re-keyed, never re-imported``` (+ the
// `validate-user-map pre-write halt` sibling). It moved to the streaming
// suite because it needs the seeded-sink fixture; this pure boundary file
// keeps only the face-level spec-gate refusals above.

[<Fact(Skip = "Reserved: the contract-vs-live-shape preflight — a column the rendered logical contract names but live B lacks dies today inside the ingest SELECT with a raw SqlException (pinned by ReverseLegCanaryTests 'B-drift'). Promotion trigger: a named transfer.sourceShapeDrift refusal lands (compare the rendered contract against the live INFORMATION_SCHEMA before any read); then this test drops a column from live B and asserts the named refusal replaces the raw crash.")>]
let ``RESERVED — B-drift refused by name: the rendered contract is checked against live B's shape before any row is read`` () = ()

[<Fact(Skip = "Reserved: object-scope grant evidence — Preflight.captureGrantEvidence reads sys.fn_my_permissions at DATABASE scope only, so a table-level DENY INSERT passes the preflight and crashes mid-load with a PARTIAL write (pinned by ReverseLegCanaryTests 'NAMED GAP'). Promotion trigger: the survey-gated P1 object-scope refinement lands (per-table fn_my_permissions probes for the planned writes); then this test DENIES INSERT on one table and asserts transfer.insufficientGrant BEFORE any row moves.")>]
let ``RESERVED — object-scope DENY refused by name before any write: the grant probe descends to table scope`` () = ()
