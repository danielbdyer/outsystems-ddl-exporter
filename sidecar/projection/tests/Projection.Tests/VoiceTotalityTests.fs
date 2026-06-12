module Projection.Tests.VoiceTotalityTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Cli

/// THE VOICE — the `code ⇔ copy` totality test (`THE_VOICE_INTEGRATION.md` §5/§7
/// slice 1; `DECISIONS 2026-06-06`). The sibling of the registry's
/// `registered ⇔ executed` property: every in-scope LIVE code the engine emits
/// has a Voice entry, and every Voice entry maps to a code the engine can emit —
/// so the operator copy cannot drift from the events by construction. Plus a
/// mechanical guard that every voiced string clears the twelve-rule banned list
/// (`THE_VOICE.md` §1 + §2.2): the discipline IS the product here.

// The codes voiced in slice 1 + the slice-2 stage scaffold — the closed in-scope
// set. `Voice.all` must cover exactly this set (bidirectional).
let private inScopeCodes : Set<string> =
    Set.ofList
        [ "canary.diffEmpty"; "canary.divergence"; "summary.runComplete"
          "config.runStart"; "config.connectionResolved"
          "extract.started"; "extract.completed"
          "profile.started"; "profile.completed"
          "emit.started"; "emit.completed"
          "preflight.started"; "deploy.started"; "canary.started"; "load.started"
          "watch.runTitle"; "watch.runDone"; "watch.stageHalted"
          "summary.stageCompleted"
          "config.validationFailed"
          // the run-face verdict codes (`RunFaces` register migration) — the
          // §4/§6 publish-and-load verdict and the §13 durable-record line
          "load.completed"; "episode.recorded"
          // the deploy face: the §3 verdict, the §10 SSDT rejection (server
          // findings demoted to the disclosure), the §13 stage lines, and the
          // §14 Docker requirement
          "deploy.completed"; "deploy.ssdtRejected"
          "container.starting"; "deploy.bundleEmitted"; "docker.unavailable"
          // the canary faces: the §6 CDC-silence proof pair, the §13
          // both-sides-deployed line, and the §14 located source-file finding
          "canary.cdcSilent"; "canary.cdcCaptured"
          "canary.deployed"; "canary.sourceMissing"
          // the drift face: the §6 no-drift verdict and the §5 drift finding
          "drift.none"; "drift.diverged" ]

// The codes the engine can actually emit today (the inventory — the contract the
// totality test holds Voice to). Voicing a code outside this set would be copy for
// a phantom event; this superset includes codes voiced later / by other mechanisms
// (the §4 moves, the diagnostics) so the ⊆ check stays honest as the catalog grows.
let private knownEmittableCodes : Set<string> =
    Set.ofList
        [ // lifecycle spine + stages (LIVE on every full-export run)
          "config.runStart"; "config.connectionResolved"; "config.validationFailed"
          "summary.stageCompleted"; "summary.runComplete"
          "extract.started"; "extract.completed"
          "profile.started"; "profile.completed"
          "emit.started"; "emit.completed"
          // the migrate leg's live stage stream (build → apply → verify) + the
          // data-transfer leg's load stage
          "preflight.started"; "deploy.started"; "canary.started"; "load.started"
          // the live Watch board's render-synthesized frame codes (§13) — the
          // run-title header, the terminal done-frame, and the halted stage line
          // (the R2 Aborted arm). Not LogSink envelopes: the board is a
          // *rendering* of the run, so its frame copy is voiced through the
          // catalog (one register) and consumed at render, never emitted.
          "watch.runTitle"; "watch.runDone"; "watch.stageHalted"
          // round-trip verification verdict
          "canary.diffEmpty"; "canary.divergence"
          // the run-face verdict codes — like the watch frames, these are not
          // LogSink envelopes: a face renders its own verdict through the
          // catalog (`TtyRenderer.renderVoicedTo`), one register, consumed at
          // render. `load.completed` is `full-export --load`'s publish-and-load
          // verdict; `episode.recorded` is the §13 durable-record line.
          "load.completed"; "episode.recorded"
          // the deploy face's verdicts + stage lines + the §14 Docker
          // requirement (shared with the canary faces)
          "deploy.completed"; "deploy.ssdtRejected"
          "container.starting"; "deploy.bundleEmitted"; "docker.unavailable"
          // the canary faces' verdicts + stage line + located source finding
          "canary.cdcSilent"; "canary.cdcCaptured"
          "canary.deployed"; "canary.sourceMissing"
          // the drift face's verdict pair
          "drift.none"; "drift.diverged"
          // emitted but voiced by mechanism-1 / later slices (not in `Voice.all` yet)
          "transform.registered"; "transform.applied"; "transform.declined"
          "transform.lineage"; "transform.diagnostic"; "bench.label" ]

let private voicedCodes : Set<string> =
    Voice.all |> List.map (fun c -> c.Code) |> Set.ofList

/// Render a `View` to its structured-JSON text so every nested string (statement,
/// substantiation, action) is scannable in one blob — the machine lens carries the
/// full tree (`View.toJson`).
let private viewText (v: View.View) : string =
    (View.toJson v).ToJsonString()

/// A representative payload exercising the filled (non-empty) template branches of
/// every catalog entry, so the banned-word scan covers the real strings, not just
/// the no-payload fallbacks.
let private samplePayload : Voice.Payload =
    Map.ofList
        [ "command",     box "projection full-export"
          "configPath",  box "config.json"
          "outputDir",   box "./out"
          "modelPath",   box "model.json"
          "kind",        box "SnapshotJson"
          "moduleCount", box 300
          "durationMs",  box 1234
          "tableCount",  box 300
          "renderedDiff",box "Customer.Email differs"
          "reason",      box "line 12, 'threshold' must be a number"
          "code",        box "pipeline.config.typeMismatch"
          "stage",       box "extract"
          "outcome",     box "succeeded"
          "followOn",    box "Verification follows."
          "runIdentity", box 11
          "artifactCount", box 7
          "capturedRows",  box 4210
          "episodeCount",  box 3
          "timeline",      box "DEV"
          "purpose",       box "deploy"
          "entryCount",    box 12
          "database",      box "projection_canary_01"
          "serverErrors",  box "Incorrect syntax near 'GO'.\nThe object 'dbo.Order' already exists."
          "sourceTables",  box 300
          "targetTables",  box 300
          "path",          box "model.sql" ]

// ---------------------------------------------------------------------------
// code ⇔ copy totality
// ---------------------------------------------------------------------------

[<Fact>]
let ``Voice totality: every in-scope LIVE code has a copy entry (code → copy)`` () =
    let missing = Set.difference inScopeCodes voicedCodes
    Assert.True(Set.isEmpty missing, sprintf "in-scope codes with no Voice copy: %A" (Set.toList missing))

[<Fact>]
let ``Voice totality: every copy entry maps to an in-scope code (copy → code)`` () =
    let extra = Set.difference voicedCodes inScopeCodes
    Assert.True(Set.isEmpty extra, sprintf "Voice entries with no in-scope code: %A" (Set.toList extra))

[<Fact>]
let ``Voice totality: no copy is authored for a code the engine cannot emit`` () =
    let phantom = Set.difference voicedCodes knownEmittableCodes
    Assert.True(Set.isEmpty phantom, sprintf "Voice entries for non-emittable codes: %A" (Set.toList phantom))

[<Fact>]
let ``Voice totality: codes are distinct`` () =
    let codes = Voice.all |> List.map (fun c -> c.Code)
    Assert.Equal<string list>(List.distinct codes, codes)

[<Fact>]
let ``Voice totality: every entry cites a recognized THE_VOICE section`` () =
    let recognized = Set.ofList [ "§3"; "§5"; "§6"; "§10"; "§13"; "§14" ]
    for c in Voice.all do
        Assert.False(System.String.IsNullOrWhiteSpace c.DocSection, sprintf "%s has no DocSection" c.Code)
        Assert.True(Set.contains c.DocSection recognized, sprintf "%s cites unrecognized section %s" c.Code c.DocSection)

[<Fact>]
let ``Voice totality: lookup is total over the in-scope codes`` () =
    for code in inScopeCodes do
        Assert.True((Voice.lookup code).IsSome, sprintf "lookup returned None for in-scope code %s" code)

// ---------------------------------------------------------------------------
// the twelve-rule banned list (THE_VOICE.md §1 + §2.2), mechanically
// ---------------------------------------------------------------------------

// Lower-cased substrings forbidden anywhere on an operator surface: pronouns
// (rule 1 / rule 7), the antithesis tic (rule 8), euphemism + drama (rule 5),
// figurative terms (§2.2), colloquialism (rule 6), system-shout (§2.2).
let private bannedSubstrings : string list =
    [ "your"; "you "; " i "; " we "        // pronouns
      "not assumed"; "that's real"; ", not " // the antithesis tic
      "cleaned up"; "cleans up"; "destroy"   // euphemism / drama
      "blast radius"; "fatal"                // drama
      "dig"; "diggable"; "green hush"; "jewel" // figurative
      "oops"; "let's"; "hang on"             // colloquialism
      "refused"; "error!"; "failed!" ]       // system-shout as a lead

let private assertClean (label: string) (v: View.View) : unit =
    let lowered = (viewText v).ToLowerInvariant()
    for banned in bannedSubstrings do
        Assert.False(
            lowered.Contains banned,
            sprintf "%s breaks the banned list (THE_VOICE.md §2.2): contains '%s'" label banned)

[<Fact>]
let ``Voice register: every entry's surface clears the banned list (filled payload)`` () =
    for c in Voice.all do
        let surface = Voice.toSurface c samplePayload
        assertClean (sprintf "%s (filled)" c.Code) (Surface.render surface)

[<Fact>]
let ``Voice register: every entry's surface clears the banned list (empty payload)`` () =
    for c in Voice.all do
        let surface = Voice.toSurface c Map.empty
        assertClean (sprintf "%s (empty)" c.Code) (Surface.render surface)

[<Fact>]
let ``Voice register: the error frames clear the banned list`` () =
    let codes =
        [ "pipeline.config.typeMismatch"; "migrate.connectionUnavailable"
          "transfer.insufficientGrant"; "something.unclassified"
          // the intent gate (§5/§7 two-gate consent) — the flat gate.intent code
          // routed through the §10/§14 frame (DECISIONS 2026-06-08).
          "gate.intent"
          // the four-verb surface's coded refusals voice through the generic
          // §10 frame — they must clear the register too (CLI fidelity #3).
          "cli.project.modelMissing"; "cli.project.dataNotLive"
          "cli.project.scopeDataNoSource"; "cli.check.driftArgs"
          "cli.explain.unknown"; "cli.seal.ejectArgs"; "cli.to.unknownTarget" ]
    for code in codes do
        let surface = Voice.errorSurface (ValidationError.create code "a located cause")
        assertClean (sprintf "errorSurface %s" code) (Surface.render surface)

// ---------------------------------------------------------------------------
// the deploy face's §10 rejection — statement in the register, the server's
// findings demoted to the substantiation (the canary.divergence shape)
// ---------------------------------------------------------------------------

[<Fact>]
let ``Voice deploy.ssdtRejected: the statement is the finding and the server lines are the disclosure`` () =
    let payload : Voice.Payload =
        Map.ofList
            [ "database",     box "projection_dpl_01"
              "serverErrors", box "Incorrect syntax near 'GO'.\nThe object 'dbo.Order' already exists." ]
    match Voice.surfaceOf "deploy.ssdtRejected" payload with
    | None -> Assert.Fail "deploy.ssdtRejected is unvoiced"
    | Some surface ->
        // The lead is a plain Bad verdict, never a raw server line.
        match surface.Statement with
        | View.Hero(View.Bad, text) -> Assert.Contains("rejected the change build", text)
        | other -> Assert.Fail(sprintf "statement is not a Bad Hero: %A" other)
        // Every server line is demoted into the disclosure, one Note each.
        let disclosed =
            surface.Substantiation
            |> List.collect (function View.Disclosure(_, _, detail) -> detail | _ -> [])
        Assert.Equal(2, List.length disclosed)
        // The surface ends on the move.
        match surface.Action with
        | Some (View.Action _) -> ()
        | other -> Assert.Fail(sprintf "no imperative next move: %A" other)

[<Fact>]
let ``Voice deploy.ssdtRejected: an empty payload still leads with the finding`` () =
    match Voice.surfaceOf "deploy.ssdtRejected" Map.empty with
    | Some { Statement = View.Hero(View.Bad, _) } -> ()
    | other -> Assert.Fail(sprintf "unexpected empty-payload surface: %A" other)

// ---------------------------------------------------------------------------
// the canary faces' §6 CDC-silence proof pair — the proof and its failure are
// both grounded findings, never a bare count
// ---------------------------------------------------------------------------

[<Fact>]
let ``Voice canary.cdcSilent: the silence proof leads Ok and grounds both zeros`` () =
    match Voice.surfaceOf "canary.cdcSilent" Map.empty with
    | Some { Statement = View.Hero(View.Ok, text); Substantiation = subs } ->
        Assert.Contains("Confirmed idempotent", text)
        Assert.Contains("zero rows captured", text)
        Assert.False(List.isEmpty subs)   // the CDC = 0 evidence rides beneath
    | other -> Assert.Fail(sprintf "unexpected cdcSilent surface: %A" other)

[<Fact>]
let ``Voice canary.cdcCaptured: the failed proof carries its measure on the finding`` () =
    let payload : Voice.Payload = Map.ofList [ "capturedRows", box 4210 ]
    match Voice.surfaceOf "canary.cdcCaptured" payload with
    | Some { Statement = View.Hero(View.Bad, text) } ->
        Assert.Contains("4,210", text)   // humane numerals, §12
    | other -> Assert.Fail(sprintf "unexpected cdcCaptured surface: %A" other)

// ---------------------------------------------------------------------------
// the stage-name mapping (THE_VOICE.md §13 — operator-shaped, never the verb)
// ---------------------------------------------------------------------------

[<Fact>]
let ``Voice stageName: every emitted internal stage maps to an operator name`` () =
    // The stages `LogSink.recordStageEvent` emits today (Pipeline + FullExportRun
    // + the migrate leg's deploy / canary phases).
    let emittedStages = [ "pipeline"; "extract"; "profile"; "emit"; "preflight"; "deploy"; "canary"; "load" ]
    for s in emittedStages do
        Assert.NotEqual<string>(s, Voice.stageName s)   // never the internal verb

[<Fact>]
let ``Voice stageName: an unknown stage passes through unchanged`` () =
    Assert.Equal<string>("somethingNew", Voice.stageName "somethingNew")

// ---------------------------------------------------------------------------
// the error frame routing (THE_VOICE.md §10 / §14) is total
// ---------------------------------------------------------------------------

[<Fact>]
let ``Voice errorFrame: routing is total — every code yields a verdict Hero`` () =
    let codes =
        [ "pipeline.config.typeMismatch"; "pipeline.config.fileNotFound"
          "migrate.connectionUnavailable"; "transfer.connectionUnavailable"
          "migrate.insufficientGrant"; "transfer.grantProbeFailed"
          "gate.intent"
          "transfer.connection.specShape"; "transfer.connection.specEmpty"
          "transfer.connection.refMissing"; "transfer.connection.refEmpty"
          "transfer.connection.openFailed"; "timeline.name.empty"
          "adapter.osm.parse"; "something.unclassified" ]
    for code in codes do
        match Voice.errorFrame code with
        | View.Hero _, _ -> ()
        | other, _ -> Assert.Fail(sprintf "errorFrame %s did not lead with a Hero: %A" code other)

[<Fact>]
let ``Voice errorFrame: a config code routes to the located §14 frame`` () =
    match Voice.errorFrame "pipeline.config.typeMismatch" with
    | View.Hero(View.Bad, text), Some _ -> Assert.Contains("configuration", text)
    | other -> Assert.Fail(sprintf "unexpected config frame: %A" other)

[<Fact>]
let ``Voice errorFrame: a connection code routes to the §10 unreachable frame`` () =
    match Voice.errorFrame "migrate.connectionUnavailable" with
    | View.Hero(_, text), Some _ -> Assert.Contains("unreachable", text)
    | other -> Assert.Fail(sprintf "unexpected connection frame: %A" other)

[<Fact>]
let ``Voice errorFrame: a malformed connection reference is an argument finding (§14 set-but-invalid)`` () =
    // The parse-failure class must NOT borrow the reachability frame: the
    // reference's shape is wrong; nothing was probed.
    for code in [ "transfer.connection.specEmpty"; "transfer.connection.specShape"; "transfer.connection.specPrefix" ] do
        match Voice.errorFrame code with
        | View.Hero(View.Bad, text), Some _ ->
            Assert.Contains("malformed", text)
            Assert.DoesNotContain("unreachable", text)
        | other -> Assert.Fail(sprintf "unexpected spec frame for %s: %A" code other)

[<Fact>]
let ``Voice errorFrame: an unresolvable connection reference names the missing secret (§14 required-and-missing)`` () =
    for code in [ "transfer.connection.refMissing"; "transfer.connection.refEmpty" ] do
        match Voice.errorFrame code with
        | View.Hero(View.Bad, text), Some _ ->
            Assert.Contains("does not resolve", text)
        | other -> Assert.Fail(sprintf "unexpected ref frame for %s: %A" code other)

[<Fact>]
let ``Voice errorFrame: a failed connection open still routes to the §10 unreachable frame`` () =
    match Voice.errorFrame "transfer.connection.openFailed" with
    | View.Hero(_, text), Some _ -> Assert.Contains("unreachable", text)
    | other -> Assert.Fail(sprintf "unexpected open frame: %A" other)

[<Fact>]
let ``Voice errorFrame: an empty timeline name is a located --env finding`` () =
    match Voice.errorFrame "timeline.name.empty" with
    | View.Hero(View.Bad, text), Some _ -> Assert.Contains("--env", text)
    | other -> Assert.Fail(sprintf "unexpected timeline frame: %A" other)

[<Fact>]
let ``Voice errorFrame: a model-load failure routes to the §10 model frame`` () =
    for code in [ "adapter.osm.fileReadFailed"; "adapter.osm.parse"; "model.resolution" ] do
        match Voice.errorFrame code with
        | View.Hero(View.Bad, text), Some _ -> Assert.Contains("model failed to load", text)
        | other -> Assert.Fail(sprintf "unexpected model frame for %s: %A" code other)

[<Fact>]
let ``Voice errorFrame: a deployed-schema read failure names the schema, never a generic stop`` () =
    match Voice.errorFrame "readside.query.failed" with
    | View.Hero(View.Bad, text), Some _ -> Assert.Contains("deployed schema could not be read", text)
    | other -> Assert.Fail(sprintf "unexpected readside frame: %A" other)

[<Fact>]
let ``Voice drift.diverged: the finding leads and the rendered difference is the disclosure`` () =
    let payload : Voice.Payload = Map.ofList [ "renderedDiff", box "table dbo.Extra exists only on the server" ]
    match Voice.surfaceOf "drift.diverged" payload with
    | Some surface ->
        (match surface.Statement with
         | View.Hero(View.Warn, text) -> Assert.Contains("diverges from the model", text)
         | other -> Assert.Fail(sprintf "statement is not a Warn Hero: %A" other))
        let disclosed =
            surface.Substantiation
            |> List.collect (function View.Disclosure(_, _, detail) -> detail | _ -> [])
        Assert.False(List.isEmpty disclosed)
        match surface.Action with
        | Some (View.Action _) -> ()
        | other -> Assert.Fail(sprintf "no next move: %A" other)
    | None -> Assert.Fail "drift.diverged is unvoiced"

[<Fact>]
let ``Voice errorFrame: the intent gate names the arming variable and a next move`` () =
    // §5/§7 two-gate consent (DECISIONS 2026-06-08): the flat gate.intent code
    // states the requirement (the arming variable) and hands over the imperative.
    match Voice.errorFrame "gate.intent" with
    | View.Hero(_, text), Some (View.Action move) ->
        Assert.Contains("PROJECTION_ALLOW_EXECUTE", text)
        Assert.Contains("PROJECTION_ALLOW_EXECUTE", move)
    | other -> Assert.Fail(sprintf "unexpected intent-gate frame: %A" other)

// ---------------------------------------------------------------------------
// the gate ⇔ copy totality (the §5 mechanism-1 projection over the closed
// Preflight.GateLabel DU — the closed-DU analog of code ⇔ copy)
// ---------------------------------------------------------------------------

// Every gate label the engine can refuse on — the closed DU, enumerated so the
// test fails if a variant is added without §5 copy.
let private allGateLabels : Preflight.GateLabel list =
    [ Preflight.ConnectionUnavailable
      Preflight.InsufficientGrant
      Preflight.ReconciliationMismatch
      Preflight.UnmappedIdentities
      Preflight.DataViolatesTightening
      Preflight.CdcTrackedSink
      Preflight.SchemaReadFailed
      Preflight.UndeclaredDestructiveChange
      Preflight.UnclassifiedRefusal ]

[<Fact>]
let ``Voice gate: every gate label has a non-empty §5 statement`` () =
    for label in allGateLabels do
        let _, statement, _ = Voice.gateStatement label
        Assert.False(System.String.IsNullOrWhiteSpace statement, sprintf "%A has no statement" label)

[<Fact>]
let ``Voice gate: every actionable gate names a plain imperative next move`` () =
    // Every gate except the generic UnclassifiedRefusal (whose cause is shown
    // below) hands over a plain active imperative — the §5 lever.
    for label in allGateLabels do
        let _, _, action = Voice.gateStatement label
        match label with
        | Preflight.UnclassifiedRefusal -> ()  // ends on the verdict; cause shown below
        | _ ->
            match action with
            | Some (View.Action _) -> ()
            | other -> Assert.Fail(sprintf "%A has no imperative next move: %A" label other)

[<Fact>]
let ``Voice gate: every gate surface clears the banned list`` () =
    for label in allGateLabels do
        let refusal : Preflight.GateRefusal =
            { Error = ValidationError.create "migrate.test" "a located cause"
              ExitCode = 9
              Label = label }
        assertClean (sprintf "gateSurface %A" label) (Surface.render (Voice.gateSurface "projection migrate" refusal))
