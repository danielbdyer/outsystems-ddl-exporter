module Projection.Cli.EstateBoardView
// LINT-ALLOW-FILE: the estate-board terminal view — `String.concat` composes the
//   operator-facing board rows at the console text boundary (the rendered string
//   IS the output); no typed AST applies to free-text terminal narration, and
//   each segment is a pre-rendered typed value. Terminal-render boundary.

// THE ESTATE BOARD, rendered through the `View` engine (wave A8, the live-board
// program — the ease tail of the loop-closing chapter). The pure
// `Estate.EstateReport` (Pipeline) is the ONE substrate; this CLI builder is the
// RICH lens — a Spectre-backed, terminal-responsive projection, sibling to
// `GoBoardView.ofBoard`. The plain lens `Estate.render : EstateReport → string
// list` (heavily tested, the reference projection) stays untouched; this presents
// the SAME report value as widgets — a bordered masthead, disclosable lane
// findings (each opening to its one lever), the environment × plane matrix as a
// responsive table. Both lenses read the SAME load-bearing copy (the exposed
// `Estate.provenanceText` / `laneTitle` / `laneEmptyLine` / `planeToken`, and the
// finding statements + levers that ride the `Finding` itself), so the human lens
// and the machine lens (`View.toJson`, which a `--query` walks) can never drift.
//
// The VERDICT is voiced separately at the face (`estate.unified` /
// `estate.diverged` / `estate.forked` through the Voice catalog), exactly as the
// plain lens leaves it — this board is the regions BELOW the verdict.

open Projection.Core
open Projection.Pipeline
// `View` is opened LAST so its `Status` cases (`Ok` / `Pending` / …) win on any
// name collision — `Projection.Pipeline` also carries an `ApprovalState.Pending`,
// and the board speaks in `View.Status` throughout.
open Projection.Cli.View

// -- status mappings (semantic, never decorative) ----------------------------

/// A lane's ambient status — DECIDE / REPAIR want attention (Warn), RELAX is the
/// interim posture (Pending), WATCH is advisory (Neutral).
let private laneStatus : EstateLane -> Status =
    function
    | EstateLane.Decide -> Warn
    | EstateLane.Repair -> Warn
    | EstateLane.Relax  -> Pending
    | EstateLane.Watch  -> Neutral

/// One finding's status — a fork reds (no single adoption resolves it); otherwise
/// the lane's ambient status carries.
let private findingStatus (f: Estate.Finding) : Status =
    if f.Fork then Bad else laneStatus f.Lane

/// One environment's masthead status — offline / absent evidence is advisory
/// (Warn); live / cached / refreshed evidence reads clean (Ok).
let private provenanceStatus (basis: Estate.EnvBasis) : Status =
    match basis.Provenance with
    | Estate.EvidenceProvenance.Offline _
    | Estate.EvidenceProvenance.Absent -> Warn
    | _ -> Ok

/// The fidelity clause's status — green proves (Ok), a divergence reds (Bad), a
/// missing / stale proof warns (Warn), an unconfigured clause is out of the
/// verdict (Neutral).
let private fidelityStatus : Estate.FidelityClause -> Status =
    function
    | Estate.FidelityClause.NotConfigured -> Neutral
    | Estate.FidelityClause.Green _        -> Ok
    | Estate.FidelityClause.Missing _
    | Estate.FidelityClause.Stale _        -> Warn
    | Estate.FidelityClause.Diverged _     -> Bad

// -- region copy (the presentation headers; the load-bearing finding copy rides
//    the Finding itself, reused verbatim) -----------------------------------

let private fidelityLine : Estate.FidelityClause -> string =
    function
    | Estate.FidelityClause.NotConfigured ->
        "not configured — the verdict stands on the schema and data evidence"
    | Estate.FidelityClause.Green (flow, ageDays) ->
        if ageDays <= 0 then sprintf "green — flow '%s', every row byte-identical (captured today)" flow
        else sprintf "green — flow '%s', every row byte-identical (%d day(s) old)" flow ageDays
    | Estate.FidelityClause.Missing flow ->
        sprintf "flow '%s' has not run — it stands as a ruling below" flow
    | Estate.FidelityClause.Stale (flow, ageDays) ->
        sprintf "flow '%s' is %d day(s) old and predates this run's evidence — a ruling below" flow ageDays
    | Estate.FidelityClause.Diverged (flow, diffs) ->
        sprintf "flow '%s' reports %d differing row(s) — a ruling below" flow diffs

/// The evidence-store basis line.
let private storeLine : Estate.EvidenceStoreBasis -> string =
    function
    | Estate.EvidenceStoreBasis.Enabled dir -> dir
    | Estate.EvidenceStoreBasis.Disabled ->
        "live this run — no store (PROJECTION_ESTATE_DIR, or the ledger's estate child, enables pay-once evidence)"

// -- the finding block: a headline that opens to its one lever ----------------

/// One finding as a disclosure — the statement is the headline (glyph + color by
/// status), the lever (when its artifact exists) is the one child, revealed at the
/// calm default depth. A lever-less WATCH line is a bare status headline.
let private findingBlock (f: Estate.Finding) : View =
    match f.Lever with
    | Some lever -> Disclosure (f.Statement, findingStatus f, [ Action lever ])
    | None       -> Disclosure (f.Statement, findingStatus f, [])

/// A lane's findings, capped with the remainder named (THE_VOICE §12) — the SAME
/// order and cap the plain lens uses, so the two lenses never disagree.
let private laneBlocks (lane: EstateLane) (report: Estate.EstateReport) : View list =
    match Estate.laneFindings lane report with
    | [] -> [ Note ((Estate.laneEmptyLine lane).TrimStart()) ]
    | fs ->
        let shown = fs |> List.truncate Estate.laneCap
        let remainder = List.length fs - List.length shown
        (shown |> List.map findingBlock)
        @ (if remainder > 0
           then [ Note (sprintf "and %d more — environments.json carries every finding." remainder) ]
           else [])

// -- the matrix table --------------------------------------------------------

let private matrixTable (report: Estate.EstateReport) : View =
    let planes = [ EstatePlane.Schema; EstatePlane.Data; EstatePlane.Identity; EstatePlane.Operational ]
    let headers = "environment" :: (planes |> List.map Estate.planeToken)
    let row (basis: Estate.EnvBasis) : (string * Status) list =
        (basis.Env, Neutral)
        :: (planes
            |> List.map (fun plane ->
                let n =
                    report.Findings
                    |> List.filter (fun f -> f.Plane = plane && f.Envs |> List.exists (fun (e, _) -> e = basis.Env))
                    |> List.length
                string n, (if n > 0 then Warn else Neutral)))
    Table (headers, report.Bases |> List.map row)

// -- the burndown ------------------------------------------------------------

let private burndownBlocks (report: Estate.EstateReport) : View list =
    let movement =
        match report.Burndown, report.Evidence with
        | Some b, _ ->
            let since = if b.SinceAgeDays <= 0 then "earlier today" else sprintf "%d day(s) ago" b.SinceAgeDays
            let oldest =
                match b.OldestDays with
                | Some days when b.Remaining + b.Opened > 0 -> sprintf " · oldest open %d day(s)" days
                | _ -> ""
            let st = if b.Remaining + b.Opened = 0 then Ok else Warn
            Field (sprintf "since %s (%s)" b.SinceRunId since,
                   sprintf "%d closed · %d opened · %d remain%s" b.Closed b.Opened b.Remaining oldest, st)
        | None, Estate.EvidenceStoreBasis.Enabled _ ->
            Note "this run is the estate's first recorded reading; movement renders from the next run."
        | None, Estate.EvidenceStoreBasis.Disabled ->
            Note "the estate keeps no memory without a store; PROJECTION_ESTATE_DIR enables the burndown."
    [ yield movement
      if report.Streak > 0 then
          yield Field ("streak", sprintf "%d consecutive unified run(s)" report.Streak, Ok) ]

// -- the artifacts index -----------------------------------------------------

let private artifactBlocks (report: Estate.EstateReport) : View list =
    [ yield Note "environments.json — the full findings record: every board element, machine-readable."
      for file, blocks in report.Remediation do
          yield Note (sprintf "%s — %d prepared repair block(s); the locating SELECT is active, every repair commented." file blocks)
      match report.OverlayEntries with
      | Some entries when entries > 0 ->
          yield Note (sprintf "environments.overlay.json — %d interim change(s) as config edits; each carries its reopen probe." entries)
          yield Note "environments.probes.sql — every reopen probe, runnable as one batch."
      | _ -> () ]

// -- the action --------------------------------------------------------------

/// The one next move — the top DECIDE, else the top REPAIR, else the streak (the
/// plain lens's ACTION region, reconstructed from the public report data).
let private actionOf (report: Estate.EstateReport) : Status * string =
    match Estate.laneFindings EstateLane.Decide report with
    | f :: _ -> Warn, sprintf "Rule the first DECIDE finding — %s" (FindingKey.readableLabel f.Key)
    | [] ->
        match Estate.laneFindings EstateLane.Repair report with
        | f :: _ -> Warn, sprintf "Review the first REPAIR finding — %s" (FindingKey.readableLabel f.Key)
        | [] when report.Streak > 1 ->
            Ok, sprintf "The estate holds — %d consecutive unified run(s); re-run on the publish cadence." report.Streak
        | [] -> Ok, "The estate holds; re-run on the publish cadence."

// -- the whole board ---------------------------------------------------------

/// The estate report as one `View` — the masthead panel, the four disposition
/// lanes, the emission audit, the matrix, the burndown, the artifacts index, a
/// compact runbook, and the one next move. Mirrors the plain lens's regions and
/// order; the verdict is voiced separately at the face.
let ofReport (report: Estate.EstateReport) : View =
    let masthead =
        Panel ("environments",
            [ PanelRow.Labeled ("against", Estate.TargetOperand.basisText report.Target, Neutral)
              for basis in report.Bases do
                  PanelRow.Labeled (basis.Env, Estate.provenanceText basis, provenanceStatus basis)
              PanelRow.Labeled ("confidence", Estate.evidenceConfidenceLine report, Neutral)
              PanelRow.Labeled ("evidence", storeLine report.Evidence, Neutral)
              PanelRow.Labeled ("fidelity", fidelityLine report.Fidelity, fidelityStatus report.Fidelity) ])
    let lanes =
        [ for lane in [ EstateLane.Decide; EstateLane.Repair; EstateLane.Relax; EstateLane.Watch ] do
            yield Rule (Some (Estate.laneTitle lane), laneStatus lane)
            yield! laneBlocks lane report ]
    let emission =
        [ let st = if List.isEmpty report.EmissionFindings then Ok else Warn
          yield Rule (Some "EMISSION — the schema this estate would publish, audited against database reality", st)
          if List.isEmpty report.EmissionFindings then
              yield Note "No emission hazards in the checks that run today."
          else
              let shown = report.EmissionFindings |> List.truncate Estate.laneCap
              yield! shown |> List.map findingBlock
              let extra = List.length report.EmissionFindings - List.length shown
              if extra > 0 then yield Note (sprintf "and %d more — environments.json carries every finding." extra)
          // The coverage line is DERIVED from the detector set (the
          // `DetectionStatus` classifier), so the rich board and the text
          // board name the same checks and neither can drift (Estate.render's
          // sibling; DECISIONS 2026-07-18).
          let emissionPhrasesBy status =
              EstateFindingKind.all
              |> List.filter (fun k ->
                  EstateFindingKind.planeOf k = EstatePlane.Emission
                  && EstateFindingKind.detectionStatus k = status)
              |> List.map EstateFindingKind.phrase
          match emissionPhrasesBy DetectionStatus.Active with
          | [] -> ()
          | ps -> yield Note (sprintf "Runs today, each catching one hazard: %s." (String.concat "; " ps))
          match emissionPhrasesBy DetectionStatus.NotYetDetected with
          | [] -> ()
          | ps -> yield Note (sprintf "Named follow-ons, not yet checked: %s." (String.concat "; " ps)) ]
    let matrix =
        [ yield Rule (Some "MATRIX — findings by environment and plane", Neutral)
          if List.isEmpty report.Findings then yield Note "No findings; the matrix is empty."
          else yield matrixTable report ]
    let burndown = Rule (Some "BURNDOWN — movement since the recorded baseline", Neutral) :: burndownBlocks report
    let artifacts = Rule (Some "ARTIFACTS", Neutral) :: artifactBlocks report
    let runbook =
        Disclosure ("RUNBOOK — source estate → target database", Neutral,
            [ Note "1. Confirm readiness (this check) · 2. Publish the schema bundle · 3. Deploy via sqlpackage"
              Note "4. Load the bulk data · 5. Re-trust foreign keys and enable CDC · 6. Verify (drift · rows · CDC-silence)"
              Note "Manual gates before cutover: the bulk-load step is not auto-run; enabling CDC on the target has no verb; the streaming/synthetic legs need a manual FK re-trust sweep." ])
    let actionStatus, actionText = actionOf report
    Doc
        ([ masthead
           Note (Estate.coverageLine report)
           Blank ]
         @ lanes
         @ emission
         @ matrix
         @ burndown
         @ artifacts
         @ [ runbook
             Rule (Some "action", actionStatus)
             Action actionText ])

/// Render the estate board to a writer through the rich lens (a redirected sink
/// gets the plain NoColors lens; a TTY gets color + its real width) — the same
/// report-View render policy the go board uses.
let write (writer: System.IO.TextWriter) (report: Estate.EstateReport) : unit =
    GoBoardView.writeView writer (ofReport report)
