module Projection.Cli.Faces.Migrate
// LINT-ALLOW-FILE: CLI run-face operator-facing prose + Voice payload boxing at the terminal CLI boundary; the structural surface is the typed MovementSpec / Intent / Voice catalog, BCL primitives only at this terminal text edge.

// The migrate faces — preview / from-store / execute / with-data — and their
// internal helpers (the inexpressible/stopped/undeclared-drop renderers, the
// preview surface, plannedWritesOf / reportMigrationError / migratePreflights /
// tighteningPreflight), extracted from the RunFaces wall (recon #3). Uses the
// shared `Face` combinator from `Faces.Common`; the migrate helpers stay internal
// to this family. Verbatim relocation — zero behavior change.

// STOPGAP (carried from RunFaces): the tightening-violation `task{}` check trips
// FS3511 (an `await` inside a branch) under Release's static state-machine
// optimization, which TreatWarningsAsErrors promotes to an error. The code is
// correct (dynamic state-machine fallback); suppressed to unblock Release.
#nowarn "3511"

open System
open System.IO
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Cli
open Projection.Cli.OperatorConsole
open Projection.Cli.Faces.Common

/// `projection migrate --from <modelA.json> --to <modelB.json> [--allow-drops]`
/// — the L3 bullseye's dry-run: diff the two states and print the
/// minimum-viable migration plan (the change-manifest of δ) that `migrate A B`
/// would execute. Fail-loud: a destructive drop (without `--allow-drops`) or a
/// non-shape facet change is refused with a non-zero exit and an explanation,
/// never a silent plan. The `--execute` leg (against a live deployed DB) is
/// `MigrationRun.execute`; this surface previews what it would do.

/// The §10 inexpressible-change verdict, voiced (`migrate.inexpressible`): the
/// statement carries the count; each refusing entry is demoted into the
/// disclosure beneath, its code beside its cause. The newline join is data
/// marshalling into the envelope payload, not prose.
let private renderInexpressible (entries: DiagnosticEntry list) : unit =
    TtyRenderer.renderVoicedTo Console.Error "migrate.inexpressible"
        (Map.ofList
            [ "entryCount", box (List.length entries)
              "entries",
              box (entries
                   |> List.map (fun e -> sprintf "%s (%s)" e.Message e.Code)
                   |> String.concat "\n") ])

/// The §10 stop for a `MigrationError` the gates do not own, voiced
/// (`migrate.stopped`): the statement frames the stop; the plain located cause
/// is `Voice.migrationStopDetail` (the catalog's typed projection over the DU).
let private renderMigrateStopped (e: MigrationError) : unit =
    TtyRenderer.renderVoicedTo Console.Error "migrate.stopped"
        (Map.ofList [ "cause", box (Voice.migrationStopDetail e) ])

/// The migrate preview as a §6/§9 minimality Surface — the smallest faithful
/// change, said plain (statement first, the per-move breakdown beneath), never
/// `norm=`. The schema change-manifest of δ: exactly the difference, and no more.
let migratePreviewSurface (artifacts: MigrationArtifacts) : Surface.Surface =
    let p = artifacts.Plan.Preview
    let c = p.Channels
    if Migration.isIdempotent artifacts.Plan then
        { Statement      = View.Hero(View.Ok, "Nothing to apply. The two states are already identical.")
          Substantiation = []
          Action         = None }
    else
        let removed = c.RemovedKinds + c.RemovedAttributes
        let status  = if removed > 0 then View.Warn else View.Ok
        let renames =
            p.RenamedKinds
            |> List.map (fun r -> sprintf "%s → %s" (Name.value r.From) (Name.value r.To))
        let h = Theme.humane
        { Statement      =
            View.Hero(status, sprintf "%s changes to apply — exactly the difference between the two states, and no others." (h p.Norm))
          Substantiation =
            [ View.Field("tables", sprintf "%s added · %s dropped · %s renamed" (h c.AddedKinds) (h c.RemovedKinds) (h c.RenamedKinds), View.Neutral)
              View.Field("columns", sprintf "%s added · %s dropped · %s renamed · %s changed" (h c.AddedAttributes) (h c.RemovedAttributes) (h c.RenamedAttributes) (h c.ChangedAttributes), View.Neutral) ]
            @ (if List.isEmpty renames then [] else [ View.Lane("⟲", "rename", View.Ok, renames) ])
            @ [ View.Field("to run", sprintf "%s statement(s) · %s rename(s) recorded" (h (List.length artifacts.SchemaStatements)) (h (List.length artifacts.RefactorLog)), View.Neutral) ]
          Action         = Some(View.Action "Apply against the target database with --execute.") }

/// Voice the §5 declared-loss gate for undeclared destructive removals — the
/// consent moment a drop must pass. The exit (9) is unchanged; the §5 statement
/// and the approval lever lead, the named removals ride in the substantiation.
let renderUndeclaredDropGate (violations: SchemaLoss list) : unit =
    let tokens = violations |> List.map Migration.lossToken |> String.concat ", "
    let detail = sprintf "%d removal(s) await approval: %s" (List.length violations) tokens
    TtyRenderer.renderGate "projection migrate"
        (Preflight.refusalOf [ ValidationError.create "migrate.undeclaredDestructiveChange" detail ])

/// Shared dry-run renderer for a `migrate` preview outcome — the change-manifest
/// of δ, or a fail-loud refusal (undeclared losses / inexpressible change).
let reportPreviewOutcome (header: string) (result: Result<MigrationArtifacts, MigrationError>) : int =
    let exitCode =
        match result with
        | Error (RefusedByViolations violations) ->
            renderUndeclaredDropGate violations
            9
        | Error (RefusedBySchemaErrors entries) ->
            renderInexpressible entries
            9
        | Error other ->
            renderMigrateStopped other
            2
        | Ok artifacts ->
            printfn "%s" header
            TtyRenderer.renderAnswer false View.defaultDepth (Surface.render (migratePreviewSurface artifacts))
            0
    dumpBench "migrate"
    exitCode

let runMigratePreview (fromPath: string) (toPath: string) (declaration: LossDeclaration) : int =
    let loaded =
        task {
            let! a = Compose.read fromPath
            let! b = Compose.read toPath
            return a, b
        }
    let a, b = loaded.GetAwaiter().GetResult()
    match a, b with
    | Error errors, _ ->
        printErrors Console.Error errors
        6
    | _, Error errors ->
        printErrors Console.Error errors
        6
    | Ok source, Ok target ->
        reportPreviewOutcome
            (sprintf "projection migrate %s -> %s  (dry-run)" fromPath toPath)
            (MigrationRun.preview declaration source target)

/// `projection migrate --to <modelB.json> --store <lifecycle.json> [--allow-drops
/// | --declare-drop <token>...]` — the **snapshot⊖snapshot** dry-run (6.H). State
/// A is the prior emission's schema, reconstructed from the durable
/// `LifecycleStore`; B is the new authored model. Closes the emission→snapshot→
/// diff loop: the diff basis is provenance, not a second hand-authored model. A
/// missing store is genesis (A = ∅).
let runMigrateFromStore (storePath: string) (toPath: string) (declaration: LossDeclaration) (forceGenesis: bool) : int =
    let bRead = (Compose.read toPath).GetAwaiter().GetResult()
    match bRead with
    | Error errors ->
        printErrors Console.Error errors
        6
    | Ok target ->
        // `--from empty` forces A = ∅ (genesis) against a populated store — the
        // banner names the forced from-scratch basis so the displacement is not
        // mistaken for a store-derived diff.
        let banner =
            if forceGenesis then sprintf "projection migrate (from empty) -> %s  (dry-run, genesis)" toPath
            else sprintf "projection migrate (store:%s) -> %s  (dry-run, snapshot⊖snapshot)" storePath toPath
        reportPreviewOutcome
            banner
            (MigrationRun.previewFromStoreForcing forceGenesis storePath declaration target)

/// Derive the A2 pre-flight's planned-writes from a migration's schema
/// statements: every DDL statement maps to the write it performs at the sink
/// (ALTER on its table; CREATE for new tables/sequences). Drives the permission
/// gate before any mutation.
let plannedWritesOf (stmts: Statement list) : Preflight.PlannedWrite list =
    let alterOn (t: TableId) : Preflight.PlannedWrite =
        { Schema = TableId.schemaText t; Table = TableId.tableText t; Action = Preflight.Alter }
    stmts
    |> List.choose (fun s ->
        match s with
        | AlterTableAddColumn (t, _) | AlterTableAlterColumn (t, _) | AlterTableDropColumn (t, _)
        | AlterTableAddForeignKey (t, _) | AlterTableDropConstraint (t, _)
        | AlterTableNoCheckConstraint (t, _) | AlterTableDisableConstraint (t, _)
        | DropIndex (t, _) -> Some (alterOn t)
        | CreateIndex idx -> Some (alterOn idx.Table)
        | CreateTable (t, _, _, _, _, _) ->
            Some { Schema = TableId.schemaText t; Table = TableId.tableText t; Action = Preflight.CreateTable }
        | CreateSequence seq -> Some { Schema = seq.Schema; Table = Name.value seq.Name; Action = Preflight.CreateTable }
        | DropSequence (schema, name) -> Some { Schema = schema; Table = name; Action = Preflight.CreateTable }
        | _ -> None)
    |> List.distinct

/// Print a `MigrationError` and map it to an exit code (shared by the
/// schema-only and cross-substrate migrate executors).
let reportMigrationError (e: MigrationError) : int =
    match e with
    | RefusedByViolations violations ->
        renderUndeclaredDropGate violations
        9
    | RefusedBySchemaErrors entries ->
        renderInexpressible entries
        9
    | RefusedByCdc tracked ->
        // §5 CDC gate — the consent surface (consequence as meaning + the one
        // lever), keyed by the closed GateLabel DU; the tracked count and the
        // --allow-cdc lever ride in the located detail. Exit 9 unchanged.
        TtyRenderer.renderGate "projection migrate"
            (Preflight.refusalOf
                [ ValidationError.create "migrate.cdcTrackedSink"
                    (sprintf "%d table(s) are CDC-tracked; --allow-cdc accepts the capture." (List.length tracked)) ])
        9
    | RefusedByCdcUnverifiable msg ->
        // NM-54 — the CDC probe could not run; an unverifiable CDC state is
        // UNSAFE, so the schema change is REFUSED through the same §5 gate
        // surface as an observed-CDC refusal. Exit 9 (a clean named refusal),
        // never a crash.
        TtyRenderer.renderGate "projection migrate"
            (Preflight.refusalOf
                [ ValidationError.create "migrate.cdcStateUnverifiable" msg ])
        9
    | RefusedByTightening msg ->
        // §5 data-compat gate — the same surface the pre-flight probe renders.
        TtyRenderer.renderGate "projection migrate"
            (Preflight.refusalOf [ ValidationError.create "migrate.dataViolatesTightening" msg ])
        9
    | SchemaReadFailed es ->
        // The §10 schema-read frame states the finding; the located causes
        // ride beneath (the raw header line is retired).
        printErrors Console.Error es
        6
    | ExecutionRolledBack (msg, n) ->
        // M21 — the destructive write failed but the compensating-undo arm
        // (M12's groupoid inverse) returned the substrate to A; no changes
        // remain. A clean named refusal on the destructive axis (exit 9), not a
        // corruption — "refuses without damage."
        TtyRenderer.renderGate "projection migrate"
            (Preflight.refusalOf
                [ ValidationError.create "migrate.executionRolledBack"
                    (sprintf "the migration was rolled back to its original state (%d rename(s) reverted): %s" n msg) ])
        9
    | PartialWriteUnrecovered (msg, residual) ->
        // M21 — the loudest honest outcome: a non-rename residual could not be
        // safely auto-inverted (the inverse would be a destructive op the engine
        // refuses by policy). Name the residual; never claim success. Exit 9
        // (destructive axis) with the exact divergence from A surfaced.
        TtyRenderer.renderGate "projection migrate"
            (Preflight.refusalOf
                [ ValidationError.create "migrate.partialWriteUnrecovered" msg ])
        Console.Error.WriteLine(PhysicalSchema.renderDiff residual)
        9
    | other ->
        renderMigrateStopped other
        2

/// The A1 connection + A2 permission pre-flights against a sink connection,
/// given the planned schema statements. Returns `Ok ()` to proceed or a printed
/// refusal + exit code. A2's grant capture is database-scope (survey-gated
/// object-scope refinement, OPEN-2 / P1).
let migratePreflights (label: string) (cnn: Microsoft.Data.SqlClient.SqlConnection) (planned: Preflight.PlannedWrite list) : System.Threading.Tasks.Task<Result<unit, int>> =
    // Each pre-flight refusal renders through the §5 Gate surface — the
    // consequence as meaning + the one plain imperative — never a raw header +
    // dump. NM-61: the exit HONORS `classify` (the A1 single-source seam the
    // transfer path routes through at `runCore`) rather than flattening every
    // refusal to 7 — so the connection axis exits 6 (its own axis) while the
    // permission/grant axis stays 7 (`migrate.insufficientGrant` /
    // `migrate.grantProbeFailed` classify to 7). The displayed exit matches the
    // returned one because both come from the one `refusalOf` classification.
    let refuse (es: ValidationError list) : Result<unit, int> =
        let refusal = Preflight.refusalOf es
        TtyRenderer.renderGate "projection migrate" refusal
        Error refusal.ExitCode
    task {
        // G0 (AC-G0) — the migrate pre-flights compose through the ONE mandatory
        // `Preflight.all`, mirroring the transfer Execute path (`runCore`).
        // 2026-07-17 — `Preflight.all` takes THUNKS now, so gate N starts only
        // after gate N-1 completes: the hand-sequencing this site carried
        // ("SqlClient forbids concurrent commands on a connection" — the
        // permission gate awaited the connection gate's hot task) is the
        // structural contract, not a local workaround.
        let connectionGate () : System.Threading.Tasks.Task<Result<unit>> = Preflight.connectionPreflight cnn cnn
        let permissionGate () : System.Threading.Tasks.Task<Result<unit>> =
            task {
                match! Preflight.captureGrantEvidence cnn with
                | Error es -> return Error es
                | Ok grant -> return Preflight.permissionPreflight grant planned
            }
        match! Preflight.all [ connectionGate; permissionGate ] with
        | Error es -> return refuse es
        | Ok () -> return Ok ()
    }

/// G7 — the Decision↔Data tightening pre-flight, wired into the migrate verbs.
/// The operator's response at the §5 tightening gate (the relax-decision the
/// Intervene choice carries): halt, relax just this run, or relax + persist the
/// blessing to projection.json so future headless runs honor it.
type private RelaxDecision =
    | Halt
    | RelaxOnce
    | RelaxAlways

/// Derives the narrowing-to-NOT-NULL overlay from the A→B displacement and, when
/// it is NON-EMPTY, probes the live data source's null counts via
/// `Preflight.tighteningPreflight`, refusing (exit 9 / migrate.dataViolatesTightening)
/// before any write when a tightened column carries NULLs. When the overlay is
/// empty (a non-tightening migration) the probe is SKIPPED entirely — the
/// self-probing pre-flight surveys every kind, a cost a non-tightening migration
/// must not pay. `dataCnn` is the connection whose data is at risk: the in-place
/// `cnn` for MC (state A lives in the sink), the SOURCE for MX.
let tighteningPreflight
    (sourceA: Catalog)
    (target: Catalog)
    (dataCnn: Microsoft.Data.SqlClient.SqlConnection)
    : System.Threading.Tasks.Task<Result<Catalog, int>> =
    task {
        let overlay = Preflight.tighteningOverlay sourceA target
        if Set.isEmpty overlay.EnforceNotNull then return Ok target
        else
            match! Preflight.tighteningViolations dataCnn sourceA overlay with
            | Error es ->
                printErrors Console.Error es
                return Error (Preflight.refusalOf es).ExitCode
            | Ok [] -> return Ok target
            | Ok violations ->
                // §5 Data-compat gate — the live data violates the model's
                // narrowing to NOT NULL. The operator is the STEWARD of the team's
                // model: HALT (the default, and the headless fallback when no
                // blessing exists) remediates the data; RELAX loosens those columns
                // to nullable so the emitted schema fits the data — a NAMED, tracked
                // override, never a silent edit; RELAX-ALWAYS also records the
                // blessing in projection.json so a future HEADLESS run honors it.
                let keys = violations |> List.map (fun v -> v.AttributeKey) |> Set.ofList
                let violationIds = violations |> List.map Preflight.violationKey |> Set.ofList
                // The relaxation, applied + TRACKED (channel 1 / the ledger).
                let applyRelaxation () : Result<Catalog, int> =
                    LogSink.emit (EventProjection.tighteningRelaxedEnvelope keys)
                    // LINT-ALLOW: register-clean operator acknowledgment at the boundary.
                    eprintfn
                        "projection migrate: relaxed %d column(s) to nullable to fit the data; the model still declares NOT NULL — remediate the source and re-tighten."
                        (Set.count keys)
                    Ok (Preflight.relaxTightening keys target)
                let halt () : Result<Catalog, int> =
                    TtyRenderer.renderGate "projection migrate"
                        (Preflight.refusalOf
                            [ ValidationError.create "migrate.dataViolatesTightening" (Preflight.describe violations) ])
                    Error 9
                // Headless honoring (A44): a previously-blessed relaxation covering
                // EVERY violating column lets any run proceed — relaxed + tracked —
                // without prompting. The persisted exception is the reachable
                // equivalent of the interactive choice ("downgrades never silent").
                let configFile = RelaxationStore.configPath ()
                if Set.isSubset violationIds (RelaxationStore.read configFile) then
                    return applyRelaxation ()
                else
                    // LINT-ALLOW: register-clean intervention labels at the CLI boundary
                    // (THE_VOICE §1 — plain imperative, no pronouns, no system-shout).
                    let haltChoice : Intervene.Choice<RelaxDecision> =
                        { Code = "migrate.dataViolatesTightening"
                          Label = "Halt — remediate the data, then re-run"
                          Value = Halt }
                    let relaxOnceChoice : Intervene.Choice<RelaxDecision> =
                        { Code = "migrate.tighteningRelaxed"
                          Label = "Relax once — emit a nullable schema that fits the data"
                          Value = RelaxOnce }
                    let relaxAlwaysChoice : Intervene.Choice<RelaxDecision> =
                        { Code = "migrate.tighteningRelaxed"
                          Label = "Relax always — also record this in projection.json"
                          Value = RelaxAlways }
                    match Intervene.chooseOrDefault
                            (Preflight.describe violations)
                            [ haltChoice; relaxOnceChoice; relaxAlwaysChoice ]
                            haltChoice with
                    | Intervene.Chosen RelaxOnce -> return applyRelaxation ()
                    | Intervene.Chosen RelaxAlways ->
                        // LINT-ALLOW: register-clean boundary acknowledgment.
                        match RelaxationStore.persist configFile violationIds with
                        | Ok () ->
                            eprintfn "projection migrate: recorded the relaxation in %s — future runs honor it without prompting." configFile
                        | Error cause ->
                            eprintfn "projection migrate: could not write %s (%s); relaxed for this run only." configFile cause
                        return applyRelaxation ()
                    | Intervene.Chosen Halt
                    | Intervene.Degraded _ -> return halt ()
    }

/// The migrate EXECUTE leg — apply A→B (the durable-record arm with a
/// `--lifecycle-store`, else the CDC-measure arm) and verify. Extracted so the
/// §5 tightening gate's INTERACTIVE prompt runs on plain stderr BEFORE the live
/// board: a Spectre `Live` region and an interactive prompt cannot share the
/// terminal, so only this leg streams under the board (#9).
let private runMigrateExecuteLeg
    (atomic: bool)
    (allowCdc: bool)
    (declaration: LossDeclaration)
    (sourceA: Catalog)
    (target: Catalog)
    (storePath: string option)
    (envLabel: string option)
    (cnn: Microsoft.Data.SqlClient.SqlConnection)
    : System.Threading.Tasks.Task<int> =
    task {
        match storePath with
        | Some store ->
            let env = parseEnvironment "DEV" envLabel
            match Timeline.create (Projection.Core.Environment.name env) with
            | Error es ->
                printErrors Console.Error es
                return 2
            | Ok tl ->
                let at = System.DateTimeOffset.UtcNow
                let! recorded =
                    MigrationRun.executeAndRecord atomic allowCdc declaration sourceA target store tl env at None cnn
                match recorded with
                | Ok (o, Some chain) ->
                    let detail =
                        sprintf "%d statement(s) applied; recorded to %s (%d episode(s) on timeline %s)."
                            (List.length o.Artifacts.SchemaStatements) store
                            (EpisodicLifecycle.episodes chain |> List.length) (Timeline.name tl)
                    TtyRenderer.renderVoicedTo Console.Out "migrate.applied" (Map.ofList [ "detail", box detail ] : Voice.Payload)
                    return 0
                | Ok (_, None) ->
                    TtyRenderer.renderVoicedTo Console.Error "migrate.verificationFailed" Map.empty
                    return 9
                | Error e -> return reportMigrationError e
        | None ->
            let! outcome = MigrationRun.executeAndMeasureCdc atomic allowCdc declaration sourceA target cnn
            match outcome with
            | Ok (o, cdcDelta) when o.Verified ->
                let detail =
                    sprintf "%d statement(s) applied; %d row(s) captured."
                        (List.length o.Artifacts.SchemaStatements) cdcDelta
                TtyRenderer.renderVoicedTo Console.Out "migrate.applied" (Map.ofList [ "detail", box detail ] : Voice.Payload)
                eprintfn "projection migrate: note — no --lifecycle-store supplied; no episode persisted (the next diff has no prior to load)."
                return 0
            | Ok _ ->
                TtyRenderer.renderVoicedTo Console.Error "migrate.verificationFailed" Map.empty
                return 9
            | Error e -> return reportMigrationError e
    }

/// `projection migrate --to <modelB.json> --conn <env|file:ref> --execute
/// [--allow-drops] [--allow-cdc] [--lifecycle-store <path>] [--env <label>]` —
/// B1, the LIVE in-place L3 execution (Promise 8). Reads the deployed state A,
/// runs the A1 connection + A2 permission pre-flights before any mutation,
/// evolves A→B in place, reads B' back, and verifies. Gated by
/// `PROJECTION_ALLOW_EXECUTE=1` (R6). When `--lifecycle-store` (alias `--store`)
/// is supplied, a verified execute durably records the episode onto the timeline
/// (AC-P8) so the next sprint's diff loads it as the prior; absent, behavior is
/// unchanged and a one-line note says no episode was persisted.
let runMigrateExecute (target: Catalog) (connSpec: string) (declaration: LossDeclaration) (allowCdc: bool) (atomic: bool) (storePath: string option) (envLabel: string option) : int =
    if System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" <> "1" then
        TtyRenderer.renderVoicedError (ValidationError.create "gate.intent" "PROJECTION_ALLOW_EXECUTE is not set in the environment.")
        dumpBench "migrate"
        7
    else
    let work =
        task {
            match TransferSpec.parseConnectionSpec connSpec with
            | Error es ->
                printErrors Console.Error es
                return 2
            | Ok connRef ->
                let sub : Substrate =
                    { Environment = parseEnvironment "migrate-sink" None; Role = SubstrateRole.Sink; ConnectionRef = connRef }
                match! ConnectionResolver.openSubstrate sub with
                | Error es ->
                    // NM-61 (extended) — the connection axis exits 6 on migrate too,
                    // matching `transfer`. `openSubstrate` surfaces `transfer.connection.*`
                    // (ref-resolve + open), which `classify` maps to 6; single-sourcing
                    // through `refusalOf` keeps this short-circuit in step with the
                    // in-flight `migratePreflights` connection gate (also 6) — closing the
                    // residual where the open-failure path hardcoded 3 while transfer's
                    // identical probe returned 6.
                    printErrors Console.Error es
                    return (Preflight.refusalOf es).ExitCode
                | Ok cnn ->
                    use cnn = cnn
                    // Read state A live, then run the pre-flights on the planned writes.
                    let! readA = ReadSide.read cnn
                    match readA with
                    | Error es -> return reportMigrationError (SchemaReadFailed es)
                    | Ok sourceA ->
                        match MigrationRun.preview declaration sourceA target with
                        | Error e -> return reportMigrationError e
                        | Ok artifacts ->
                            match! migratePreflights "sink" cnn (plannedWritesOf artifacts.SchemaStatements) with
                            | Error code -> return code
                            | Ok () ->
                                // G7 — refuse a narrowing-to-NOT-NULL on NULL-bearing
                                // data before any write (probe the in-place cnn).
                                match! tighteningPreflight sourceA target cnn with
                                | Error code -> return code
                                | Ok effectiveTarget ->
                                    // Relax-once (if chosen) loosened the tightening:
                                    // deploy the EFFECTIVE target so the schema fits
                                    // the data. Clean / halt-headless returns `target`.
                                    let target = effectiveTarget
                                    // #9 — the §5 gate prompt (above) ran on plain
                                    // stderr BEFORE the board; ONLY the execute leg
                                    // streams under the live board (a Spectre Live
                                    // region and an interactive prompt cannot share
                                    // the terminal).
                                    let executeBody () =
                                        (runMigrateExecuteLeg atomic allowCdc declaration sourceA target storePath envLabel cnn)
                                            .GetAwaiter().GetResult()
                                    return Face.watchInline true Spines.migrate executeBody
        }
    let code = work.GetAwaiter().GetResult()
    dumpBench "migrate"
    code

/// The migrate-with-data EXECUTE leg — apply the sink schema A→B, then load rows
/// from the source over contract B (durable-record arm with a `--lifecycle-store`,
/// else the straight load) and verify. Extracted for the same reason as
/// `runMigrateExecuteLeg` (#9): the §5 gate prompt runs on plain stderr BEFORE
/// the board; only this leg streams under the live board.
let private runMigrateWithDataLeg
    identityPolicy
    (atomic: bool)
    (allowCdc: bool)
    (declaration: LossDeclaration)
    (sinkSourceA: Catalog)
    (target: Catalog)
    reconciliation
    (storePath: string option)
    (envLabel: string option)
    (dataSource: Microsoft.Data.SqlClient.SqlConnection)
    (sink: Microsoft.Data.SqlClient.SqlConnection)
    : System.Threading.Tasks.Task<int> =
    task {
        match storePath with
        | Some store ->
            let env = parseEnvironment "DEV" envLabel
            match Timeline.create (Projection.Core.Environment.name env) with
            | Error es ->
                printErrors Console.Error es
                return 2
            | Ok tl ->
                let at = System.DateTimeOffset.UtcNow
                let! recorded =
                    MigrationRun.executeWithDataAndRecordWith identityPolicy atomic declaration Transfer.Execute allowCdc
                        sinkSourceA target reconciliation store tl env at None dataSource sink
                match recorded with
                | Ok (o, chain) ->
                    printfn "Schema applied and data loaded — %d table(s) transferred; recorded to %s (%d row(s) captured; %d episode(s) on timeline %s)."
                        (List.length o.Transfer.Kinds) store
                        (EpisodicLifecycle.latest chain).Data.CdcCaptureCount
                        (EpisodicLifecycle.episodes chain |> List.length) (Timeline.name tl)
                    return 0
                | Error e -> return reportMigrationError e
        | None ->
            let! outcome =
                MigrationRun.executeWithDataWith identityPolicy atomic declaration Transfer.Execute allowCdc
                    sinkSourceA target reconciliation dataSource sink
            match outcome with
            | Ok o when o.Schema.Verified ->
                printfn "Schema verified and data loaded — %d table(s) transferred."
                    (List.length o.Transfer.Kinds)
                return 0
            | Ok _ ->
                Console.Error.WriteLine "The schema changes were applied, but the read-back does not match the model. The data load was skipped."
                return 9
            | Error e -> return reportMigrationError e
    }

/// `projection migrate --to <modelB.json> --sink-conn <ref> --source-conn <ref>
/// --execute [--reconcile <table>:<match-column>]... [--user-map <csv>]
/// [--allow-drops] [--allow-cdc]` — the cross-substrate composition (AC-X2):
/// evolve the SINK's schema A→B in place, then transfer rows from the SOURCE
/// substrate into the migrated sink over contract B. When `--reconcile` /
/// `--user-map` entries are present the data leg RE-KEYS user FKs (the
/// Dev→UAT re-key), resolved against contract B via the same
/// `TransferSpec.resolveAllReconciliation` the `transfer` verb uses, and the
/// AC-I5 `validate-user-map` pre-write halt gates first; absent, it is a
/// straight load. Schema is fail-loud + minimum-viable; the data leg runs
/// only if the schema verified.
let runMigrateWithData (target: Catalog) (sinkSpec: string) (sourceSpec: string) (reconcileSpecs: string list) (userMapPath: string option) (declaration: LossDeclaration) (allowCdc: bool) (atomic: bool) (storePath: string option) (envLabel: string option) (sinkCapability: SinkLoadCapability) : int =
    if System.Environment.GetEnvironmentVariable "PROJECTION_ALLOW_EXECUTE" <> "1" then
        TtyRenderer.renderVoicedError (ValidationError.create "gate.intent" "PROJECTION_ALLOW_EXECUTE is not set in the environment.")
        dumpBench "migrate"
        7
    else
    // AC-X2 re-key — parse `--reconcile` (repeatable, MatchByColumn) + the
    // optional `--user-map` CSV (ManualOverride), mirroring the `transfer`
    // verb's parsing. The resolved map is threaded to `executeWithData`
    // (non-empty ⇒ `runReconciling` with the AC-I5 pre-write gate).
    let collectErrs = function Ok _ -> [] | Error es -> es
    let parsedReconciles = reconcileSpecs |> List.map TransferSpec.parseReconcileSpec
    let parsedUserMap : Result<TransferSpec.UserMapEntry list> =
        match userMapPath with
        | None -> Result.success []
        | Some path ->
            if not (System.IO.File.Exists path) then
                Result.failureOf
                    (ValidationError.create "transfer.userMap.fileMissing"
                        (sprintf "user-map file '%s' not found." path))
            else TransferSpec.parseUserMapCsv (System.IO.File.ReadAllText path)
    let specErrors = (parsedReconciles |> List.collect collectErrs) @ collectErrs parsedUserMap
    if not (List.isEmpty specErrors) then
        printErrors Console.Error specErrors
        dumpBench "migrate"
        2
    else
    let reconcileEntries = parsedReconciles |> List.choose (function Ok e -> Some e | _ -> None)
    let userMapEntries   = match parsedUserMap with Ok es -> es | _ -> []
    let work =
        task {
            match TransferSpec.parseConnectionSpec sinkSpec, TransferSpec.parseConnectionSpec sourceSpec with
            | Error es, _ ->
                printErrors Console.Error es
                return 2
            | _, Error es ->
                printErrors Console.Error es
                return 2
            | Ok sinkRef, Ok sourceRef ->
                let sinkSub : Substrate = { Environment = parseEnvironment "migrate-sink" None; Role = SubstrateRole.Sink; ConnectionRef = sinkRef }
                let sourceSub : Substrate = { Environment = parseEnvironment "migrate-source" None; Role = SubstrateRole.Source; ConnectionRef = sourceRef }
                match! ConnectionResolver.openSubstrate sinkSub with
                | Error es ->
                    // NM-61 (extended) — connection axis → 6 (matches transfer + the
                    // in-flight gate); single-sourced through `refusalOf`.
                    printErrors Console.Error es
                    return (Preflight.refusalOf es).ExitCode
                | Ok sink ->
                    use sink = sink
                    match! ConnectionResolver.openSubstrate sourceSub with
                    | Error es ->
                        printErrors Console.Error es
                        return (Preflight.refusalOf es).ExitCode
                    | Ok dataSource ->
                        use dataSource = dataSource
                        let! readA = ReadSide.read sink
                        match readA with
                        | Error es -> return reportMigrationError (SchemaReadFailed es)
                        | Ok sinkSourceA ->
                            // Pre-flight the SOURCE (read) + SINK (write) before any mutation.
                            match! Preflight.connectionPreflight dataSource sink with
                            | Error es ->
                                // §5 connection gate. NM-61: HONOR `classify` — a
                                // connection refusal (`migrate.connectionUnavailable`)
                                // is its own axis (exit 6), not the permission/credential
                                // class (7). Single-sourced through `refusalOf` so the
                                // displayed and returned exits agree and match `transfer`.
                                let refusal = Preflight.refusalOf es
                                TtyRenderer.renderGate "projection migrate" refusal
                                return refusal.ExitCode
                            | Ok () ->
                                match MigrationRun.preview declaration sinkSourceA target with
                                | Error e -> return reportMigrationError e
                                | Ok artifacts ->
                                    match! migratePreflights "sink" sink (plannedWritesOf artifacts.SchemaStatements) with
                                    | Error code -> return code
                                    | Ok () ->
                                    // G7 — the rows loaded from the SOURCE must
                                    // satisfy any column the sink schema narrows to
                                    // NOT NULL. Probe the data source before any write.
                                    match! tighteningPreflight sinkSourceA target dataSource with
                                    | Error code -> return code
                                    | Ok effectiveTarget ->
                                      // Relax-once (if chosen) loosened the tightening:
                                      // the EFFECTIVE target (contract B) is what the
                                      // re-key resolves against and the load deploys.
                                      let target = effectiveTarget
                                      // AC-X2 — resolve the re-key map against
                                      // contract B (reuse the transfer verb's
                                      // resolver). Non-empty ⇒ executeWithData
                                      // runs the reconciling load whose AC-I5
                                      // pre-write gate composes first.
                                      match TransferSpec.resolveAllReconciliation target reconcileEntries userMapEntries with
                                      | Error es ->
                                          printErrors Console.Error es
                                          return 2
                                      | Ok reconciliation ->
                                        // #9 — the §5 gate prompt (above) ran on plain
                                        // stderr BEFORE the board; only the data-load
                                        // leg streams under the live board (a Spectre
                                        // Live region and an interactive prompt cannot
                                        // share the terminal).
                                        let executeBody () =
                                            (runMigrateWithDataLeg sinkCapability.IdentityPolicy atomic allowCdc declaration sinkSourceA target reconciliation storePath envLabel dataSource sink)
                                                .GetAwaiter().GetResult()
                                        return Face.watchInline true Spines.migrateData executeBody
        }
    let code = work.GetAwaiter().GetResult()
    dumpBench "migrate"
    code
