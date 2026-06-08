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
          "summary.stageCompleted"
          "config.validationFailed" ]

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
          // round-trip verification verdict
          "canary.diffEmpty"; "canary.divergence"
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
          "outcome",     box "succeeded" ]

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
    let recognized = Set.ofList [ "§3"; "§6"; "§10"; "§13"; "§14" ]
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
          // the four-verb surface's coded refusals voice through the generic
          // §10 frame — they must clear the register too (CLI fidelity #3).
          "cli.project.modelMissing"; "cli.project.dataNotLive"
          "cli.project.scopeDataNoSource"; "cli.check.driftArgs"
          "cli.explain.unknown"; "cli.seal.ejectArgs"; "cli.to.unknownTarget" ]
    for code in codes do
        let surface = Voice.errorSurface (ValidationError.create code "a located cause")
        assertClean (sprintf "errorSurface %s" code) (Surface.render surface)

// ---------------------------------------------------------------------------
// the stage-name mapping (THE_VOICE.md §13 — operator-shaped, never the verb)
// ---------------------------------------------------------------------------

[<Fact>]
let ``Voice stageName: every emitted internal stage maps to an operator name`` () =
    // The stages `LogSink.recordStageEvent` emits today (Pipeline + FullExportRun).
    let emittedStages = [ "pipeline"; "extract"; "profile"; "emit" ]
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
