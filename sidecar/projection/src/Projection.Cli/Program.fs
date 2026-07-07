module Projection.Cli.Program

// LINT-ALLOW-FILE: CLI dispatcher operator-facing prose. Help/usage and terminal SQL-text at
//   the CLI boundary use string composition; the structural argument surface is the typed
//   MovementSpec / Intent (Projection.Pipeline). Terminal operator-facing text is the allowed exception.

open System
open System.Diagnostics
open System.IO
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Cli.OperatorConsole
open Projection.Cli.Faces.Migrate
open Projection.Cli.Faces.Export
open Projection.Cli.Faces.Deploy
open Projection.Cli.Faces.Transfer
open Projection.Cli.Faces.Synthetic
open Projection.Cli.Faces.Emit
open Projection.Cli.Faces.Canary
open Projection.Cli.Faces.Approve
open Projection.Cli.Faces.Operational
open Projection.Cli.Faces.Explain
open Projection.Cli.Faces.Inspect
open Projection.Cli.Faces.Diff
open Projection.Cli.Faces.Slice

/// Usage lines. Per chapter 3.5 deep audit (2026-05-09): the lines
/// are a typed `string list` carrying the structured help-page
/// content. Emission to the terminal is via per-line BCL
/// `TextWriter.WriteLine` rather than concatenation into a
/// multi-line string. The typed list IS the data; each line is
/// emitted independently; no intermediate concatenation.
let private usageLines : string list =
    [
        "projection — move a model from a source environment to a target (THE_CLI.md)."
        "  The daily act is `projection <flow>`: a flow is a named source→target recipe in"
        "  projection.json (environments + flows). Preview is the default; --go applies a live"
        "  write (and needs PROJECTION_ALLOW_EXECUTE=1). Conn refs are env:/file: only (D9)."
        ""
        "USAGE:"
        "    projection <flow> [--go] [--fresh] [--allow-drops] [--allow-cdc] [--resumable] [--atomic] [--auto-revert]   the daily surface"
        "    projection                                           list flows (name: from → to)"
        "    projection check  ( <source.sql> [--cdc-silence] | drift --model <m> --to <t>"
        "                      | data --before <t> --after <t> | ready | shape | go <flow> )"
        "                      shape = the cross-environment readiness gate (the `readiness` set"
        "                      resolves to one espace-safe shape + zero data dealbreakers)"
        "                      go    = THE GO BOARD for a data flow: every open decision +"
        "                      the dry-run row forecast, red (exit 5) until each decision is"
        "                      resolved, green (exit 0) when --go would execute cleanly"
        "    projection diff <a> <b> [--format json] [--depth N] [--only <channel>] [--module <name>]"
        "                      change between two refs (--only columns|relationships|indexes|"
        "                      sequences|tables scopes the display; --module scopes the diff)"
        "                      refs <a>/<b>: <file> | json:<…> | @<runId> | live:<conn> (physical) |"
        "                      ossys:<conn> (OSSYS native-GUID identity — espace-safe for cross-env)"
        "    projection compare <a> <b> [--format json]    read-only readiness between two refs:"
        "                      schema delta + data dealbreakers (same ref forms as diff) → compare.json"
        "    projection explain ( diff <a> <b> [--format json] [--depth N] | policy <a> <b>"
        "                       | node <config> <ssKey> | suggest <config> [--apply <out>] | registry"
        "                       | migrate --to <b> ( --from <a> | --from empty | --store <s> ) [--allow-drops] )"
        "    projection seal ( --store <path> | approve <version> --approver <name> ... )"
        "    projection report <flow>        the on-prem migration-team change bundle"
        "    projection synth-correct --out <path>   propose a blessed-correction artifact (review/edit/bless)"
        "    projection inspect [<runId> [<runId>]]  a stored run (no id = latest; arrows dig, PgUp/PgDn walk runs)"
        "    projection init                 scaffold a projection.json"
        "    projection revert [--script <p>] --against <env> [--go] [--force]   undo a transfer: run"
        "                      the transfer-undo.sql a successful run wrote (or a failed run's"
        "                      transfer-revert.sql) — preview by default; --go deletes the"
        "                      captured rows in ONE transaction (pre-existing rows untouched);"
        "                      the artifact's provenance header must match the --against"
        "                      database (--force overrides a deliberate rename/restore)"
        "    projection setup [--conn <ref>] read back what is configured (history, writes, board);"
        "                                    --conn also probes a target (reachable + ALTER grant)"
        ""
        "FLOW — the hero. Move a model from `from` to `to`; the target decides the form."
        "  Environments (places) carry access (bundle → SSDT for Octopus | direct → live |"
        "  docker) and grant (schema+data | data — a refusal gate). Flows are named source→"
        "  target recipes (from/to/rekey/tables). A bundle target produces files (always"
        "  safe); a direct target previews until --go (which also needs"
        "  PROJECTION_ALLOW_EXECUTE=1, R6). --fresh wipes-and-loads (the rare from-scratch);"
        "  --allow-drops accepts declared loss; --allow-cdc overrides the CDC-tracked-sink"
        "  pre-flight gate; --resumable routes the data leg through the resumable upsert"
        "  envelope; a schema-from-model flow against a data-only target is refused."
        "  Reconcile rules (flow `reconcile:` / --reconcile) take three forms:"
        "  `Module.Entity:Column` (dynamic match), `Module.Entity:=<key>` (the"
        "  single-owner pin: every reference re-keys to the ONE sink row), and"
        "  `Module.Entity:Column:=<key>` (match first, pinned owner for the rest). A"
        "  flow can DECLARE its execution profile in projection.json (strategy/resumable/"
        "  streaming/journal) — the flags are the per-run override."
        "  --atomic wraps the schema deploy in one transaction (LOCAL full-access DBs"
        "  only — production schema ships via the SSDT/Octopus pipeline, not direct-"
        "  connect). --auto-revert deletes a failed data load's sink-minted rows by"
        "  captured key; without it, --revert-dir <dir> writes the precise revert script."
        "  --correction <ref> overlays a blessed-correction artifact on a synthetic flow"
        "  (file:<path>; PII→Faker realization, fidelity + volume overrides). The"
        "  synthetic policy (preserveCardinalityMax/preserve/synthesize/scale/seed) lives"
        "  in the projection.json `synthetic` block; --seed/--scale override it per run."
        ""
        "CHECK — assert fidelity.  fidelity canary (default; --cdc-silence adds the redeploy"
        "  silence assertion) · drift (deployed vs model) · data (row/null counts) · ready"
        "  (the run-ledger readiness gauge; needs PROJECTION_LEDGER_DIR)."
        ""
        "EXPLAIN — understand before shipping.  diff (two refs) · policy (two configs) · node"
        "  (one node's transforms + findings) · suggest (ranked config edits) · migrate"
        "  (the dry-run plan: two-model or snapshot⊖snapshot)."
        ""
        "SEAL — provenance.  eject (the append-forever package; default) · approve (record a"
        "  policy-version decision)."
        ""
        "Every verb persists a bench snapshot to bench/<verb>/<utc-iso>.json; -v surfaces the"
        "table. --pretty / --json force the channel (default AUTO: a TTY gets the live stage"
        "board + verdict panel, a pipe gets NDJSON). --stat appends the run's event rollup"
        "(category · code · count) to stdout. --query <path> narrows any answer to a"
        "JSONPath-subset slice of its structured form (e.g. --query 'blocks[?status=warn]')."
        "--open <path> force-reveals just that dotted child-index branch of a pretty answer"
        "(e.g. --open 1.0), the rest staying at --depth — the headless half of the dig."
        ""
        "Exit codes:"
        "    0  succeeded"
        "    1  argv error (missing input / unknown flow or environment)"
        "    2  parse error (model JSON / spec / config-parse)"
        "    3  execution error (SQL rejected the change; connection open; unbreakable cycle)"
        "    4  Docker unavailable (a docker target; check fidelity)"
        "    5  fidelity divergence (check canary / check drift; check shape / check go not-ready)"
        "    6  config error (file missing / unparseable / D9; connection-ref resolve; check shape env unreadable)"
        "    7  gate refusal (--go without PROJECTION_ALLOW_EXECUTE=1; permission pre-flight)"
        "    8  data divergence (check data row / null)"
        "    9  refused, fail-loud (undeclared drop; inexpressible ALTER; tightening; verify-failed)"
    ]

let private runPlan (shaping: Config.Config) (surveyAdvisory: string list) (plan: ExecutionPlan) : int =
    // The dispatch PROLOGUE (2026-07-02) — every pre-run note renders here,
    // voiced, structurally BEFORE any Live region or bracket: the plan notes,
    // the inert-flag note (A7 no-silent-drop), and the G0c capability-survey
    // advisory (previously raw stderr from inside the transfer faces).
    let note (code: string) (text: string) =
        TtyRenderer.renderVoicedTo Console.Error code (Map.ofList [ "text", box text ])
    for n in plan.Notes do note "plan.note" n
    (match ModuleFilterBinding.inertFlagNote shaping.Model with
     | Some n -> note "plan.note" n
     | None -> ())
    for line in surveyAdvisory do note "survey.advisory" line
    // The shell wrapper for the answer / preview / one-shot verbs (2026-07-02,
    // the A3 sweep — every verb now runs bracketed through the ONE door: run
    // envelopes on the wire for pipes, channel-1 suppression + the framed
    // verdict panel under pretty; see the DECISIONS amendment). The body's
    // stdout answer surface is unchanged.
    let shellRun (command: string) (register: Shell.Register) (body: unit -> int) : int =
        Shell.execute
            { Shell.framed command with Register = register }
            Shell.Bracket.Bracketed
            None
            body
    // Resolve the model to a Catalog under the live-OSSYS-primary / file-
    // fallback policy (ModelResolution), then run the Catalog-accepting face.
    //
    // THE_CONFIG_CONTROL_PLANE §6 (S3) — the SINGLE shared module-filter seam.
    // Every model-bearing flow arm (emit / deploy / preview / migrate) routes
    // the resolved catalog through `Compose.applyModuleFilter` HERE so a
    // `model.modules` scope narrows the bundle and the live/docker/migrate
    // catalogs identically (the riskiest-seam callout). An empty `model.modules`
    // is the all-permissive identity, so the default flow stays byte-identical.
    let needCatalog (modelOssys: string option) (model: ModelSource) (run: Catalog -> int) : int =
        let modelFile =
            match model with
            | ModelSource.ModelFile p | ModelSource.ConfigFile p -> Some p
            | ModelSource.Unspecified -> None
        let resolved =
            (ModelResolution.resolveCatalog modelOssys modelFile).GetAwaiter().GetResult()
            |> Result.bind (Compose.applyModuleFilter shaping)
        match resolved with
        | Ok catalog -> run catalog
        | Error es ->
            for e in es do TtyRenderer.renderVoicedError e
            6
    // THE_CONFIG_CONTROL_PLANE §6 (S3) — apply the shaping catalog overlays
    // (renames + policy tightening) to a module-filtered catalog before the
    // non-bundle destinations (preview / migrate / migrate-with-data) evolve
    // the sink schema toward it. Default shaping is the identity on the catalog.
    let withShaped (shaping: Config.Config) (catalog: Catalog) (run: Catalog -> int) : int =
        match Compose.applyShapingToCatalog shaping catalog with
        | Ok shapedCatalog -> run shapedCatalog
        | Error es ->
            for e in es do TtyRenderer.renderVoicedError e
            6
    match plan.Action with
    // project ------------------------------------------------------------
    | PlanAction.PublishBundle (c, dir, store, env) ->
        let verbosity = if verboseMode.Value then LogSink.Verbosity.Verbose else LogSink.Verbosity.Quiet
        let run () = runFullExport c (Some dir) verbosity Set.empty store env
        // The one door (2026-07-02): --pretty + a real TTY → the live boxed
        // stage board (§13) then the verdict panel; the run now also joins the
        // cross-run ledger (parity with the withRun verbs — the old direct
        // renderWatch path skipped both). Self-bracketed: FullExportRun owns
        // its RunEnvelope. The spine is chosen at dispatch: a store-bearing
        // publish seeds the store leg's line, so the board covers the whole
        // run — an optional seeded stage would hold the done-frame back forever.
        let hasStore = match store with Some s -> not (String.IsNullOrWhiteSpace s) | None -> false
        Shell.execute
            // A bundle emission writes files regardless of --go (the safe
            // default act), so the register is Go even under a preview flow.
            { Shell.framed "projection full-export" with Register = Shell.Go }
            Shell.Bracket.SelfBracketed
            (Some (Spines.publishWith hasStore false))
            run
    // The emit family writes its bundle regardless of --go (a bundle target
    // "produces files (always safe)"), and the docker deploy writes to a
    // throwaway container — so the register pins Go even under a preview
    // flow: a frame that says "nothing will be written" over a file-writing
    // arm would misstate (the PublishBundle precedent).
    | PlanAction.EmitSkeleton (model, modelOssys, dir) ->
        needCatalog modelOssys model (fun cat -> shellRun "projection project" Shell.Go (fun () -> runEmitSkeletonOnly cat dir))
    | PlanAction.EmitManifest (model, modelOssys, dir) ->
        needCatalog modelOssys model (fun cat -> shellRun "projection project" Shell.Go (fun () -> runEmitManifestOnly shaping cat dir))
    | PlanAction.EmitBundle (model, modelOssys, dir) ->
        needCatalog modelOssys model (fun cat -> shellRun "projection project" Shell.Go (fun () -> runEmit shaping cat dir))
    | PlanAction.DeployDocker (model, modelOssys) ->
        needCatalog modelOssys model (fun cat -> shellRun "projection project" Shell.Go (fun () -> runDeploy shaping cat))
    | PlanAction.PreviewSchema (model, modelOssys, conn, decl) ->
        needCatalog modelOssys model (fun cat -> withShaped shaping cat (fun shapedCat ->
            shellRun "projection preview" Shell.Preview (fun () -> runProjectLivePreview shapedCat conn decl)))
    | PlanAction.Transfer (src, sink, opts, execute) ->
        // R1b — the envelope-emitting faces move under `withRun` (the law:
        // a verb that mints envelopes runs bracketed; RI-11's census). The
        // transfer/migrate/synthetic engines emit the staged spines' stage
        // events, so their streams now open with `config.runStart` and
        // close with the §10 terminal — and the run is capturable.
        withRun "projection transfer" (fun () ->
            runTransfer src sink None None opts.Reconcile opts.Rekey execute opts.AllowCdc (opts.Declaration = DeclareAll) opts.Emission opts.Resumable opts.Tables opts.RevertPolicy opts.RevertDir opts.SinkCapability.SinkResidentResume)
    | PlanAction.TransferPeer (src, sink, opts, execute) ->
        // The peer (A→A) leg (2026-07-06): two cells of one model, physical
        // `OSUSR_*` names differing per espace. NO model rides the action —
        // the face reads a contract from EACH side's OSSYS metamodel (native
        // GUID identity), gates the pair (shape / subset-FK), and drives the
        // same contract-pair engine path the reverse leg proved.
        // The contract reads run under the projection.json `model` scope
        // (2026-07-07) — the same modeled estate every other verb reads;
        // an unscoped config binds to the show-me-everything default.
        withRun "projection transfer" (fun () ->
            runPeerTransfer (SnapshotScopeBinding.fromModel shaping.Model) src sink opts.Reconcile opts.Rekey execute opts.AllowCdc (opts.Declaration = DeclareAll) opts.Emission opts.Resumable opts.Streaming opts.Journal opts.Tables opts.RevertPolicy opts.RevertDir opts.SinkCapability)
    | PlanAction.RunReverseLeg (model, modelOssys, src, sink, opts, execute) ->
        // G2 routed the B→A legacy reverse leg distinctly; J3 (the contract
        // source) is CLOSED — the two SsKey-aligned contracts are the ONE
        // authored model rendered at both renditions (`CatalogRendition`).
        // The S3 module filter applies to the model ONCE (`needCatalog`), so
        // both renditions narrow identically. Live reads are NOT used for
        // contracts (ReadSide synthesizes attribute SsKeys, which would never
        // align — the original residual's premise, now honored structurally).
        needCatalog modelOssys model (fun cat ->
            withRun "projection reverse-leg" (fun () ->
                runReverseLegTransfer src sink (CatalogRendition.logical cat) (CatalogRendition.physical cat) opts.Reconcile opts.Rekey execute opts.AllowCdc (opts.Declaration = DeclareAll) opts.Emission opts.Resumable opts.Streaming opts.Journal opts.Tables opts.RevertPolicy opts.RevertDir opts.SinkCapability))
    | PlanAction.MigrateWithData (model, modelOssys, sink, src, opts) ->
        needCatalog modelOssys model (fun cat -> withShaped shaping cat (fun shapedCat ->
            withRun "projection migrate --with-data" (fun () ->
                runMigrateWithData shapedCat sink src opts.Reconcile opts.Rekey opts.Declaration opts.AllowCdc opts.Atomic opts.Store opts.Env opts.SinkCapability)))
    | PlanAction.SynthesizeAndLoad (model, modelOssys, profile, conn, opts, execute, modelSection, syntheticSection) ->
        // The synthetic load emits the transfer spine's load stage — under
        // pretty an EXECUTING run now gets the live board (2026-07-02; it was
        // the one long-running verb with no Watch wiring at all — completely
        // silent until the trailing panel). A preview writes nothing, so its
        // load line would never advance: no spine, the static frame instead.
        Shell.execute
            { Shell.framed "projection synth-load" with
                Register = if execute then Shell.currentFrame.Value.Register else Shell.Preview }
            Shell.Bracket.Bracketed
            (if execute then Some Spines.transfer else None)
            (fun () -> runSyntheticLoad model modelOssys profile conn opts execute modelSection syntheticSection)
    | PlanAction.CaptureProfile (conn, out) -> shellRun "projection capture-profile" Shell.Go (fun () -> runCaptureProfile conn out)
    | PlanAction.ProposeCorrection (model, modelOssys, out) -> shellRun "projection synth-correct" Shell.Go (fun () -> runProposeCorrection model modelOssys out)
    | PlanAction.PublishAndLoad (c, conn, store, env) ->
        let run () = runFullExportLoad c conn None store env
        // The load flow runs the same publish pipeline plus the seed-load leg
        // (2026-07-02 — a declared stage, so the board covers the whole run;
        // the episode record, when a store rides, happens inside that leg).
        Shell.execute
            { Shell.framed "projection full-export --load" with Register = Shell.Go }
            Shell.Bracket.SelfBracketed
            (Some (Spines.publishWith false true))
            run
    | PlanAction.Migrate (model, modelOssys, conn, opts) ->
        needCatalog modelOssys model (fun cat -> withShaped shaping cat (fun shapedCat ->
            withRun "projection migrate" (fun () ->
                runMigrateExecute shapedCat conn opts.Declaration opts.AllowCdc opts.Atomic opts.Store opts.Env)))
    // check --------------------------------------------------------------
    // The canary verbs already emit the canary stage's spine events (the
    // Pipeline `staged Spines.canary` CE) — under pretty they now get the
    // live board those events always deserved (2026-07-02; the events used
    // to flow to the nulled writer with no subscriber).
    | PlanAction.CheckCanary (ddl, false) ->
        Shell.execute
            (Shell.framed "projection check")
            Shell.Bracket.Bracketed (Some Spines.canary) (fun () -> runCanary ddl)
    | PlanAction.CheckCanary (ddl, true)  ->
        Shell.execute
            (Shell.framed "projection check --cdc-silence")
            Shell.Bracket.Bracketed (Some Spines.canary) (fun () -> runCanaryCdcSilence ddl)
    | PlanAction.CheckDrift (m, conn)      -> shellRun "projection check drift" Shell.ReadOnly (fun () -> runDrift m conn)
    | PlanAction.CheckData (before, after) -> shellRun "projection check data" Shell.ReadOnly (fun () -> runVerifyData before after)
    | PlanAction.CheckReady                ->
        // Self-bracketed: `runReadiness` owns its RunEnvelope (the documented
        // no-append contract rides the ReadOnly register).
        Shell.execute
            { Shell.framed "projection check ready" with Register = Shell.ReadOnly }
            Shell.Bracket.SelfBracketed None runReadiness
    | PlanAction.CheckShape (al, ar, confirm, asJson) -> shellRun "projection check shape" Shell.ReadOnly (fun () -> runCheckShape al ar confirm asJson)
    | PlanAction.CheckGo (flowName, fromLabel, toLabel, asJson, planned) -> shellRun "projection check go" Shell.ReadOnly (fun () -> runCheckGo (SnapshotScopeBinding.fromModel shaping.Model) flowName fromLabel toLabel asJson planned)
    | PlanAction.RevertScript (script, envLabel, connSpec, go, force) -> shellRun "projection revert" (if go then Shell.Go else Shell.ReadOnly) (fun () -> runRevertScript script envLabel connSpec go force)
    // explain ------------------------------------------------------------
    | PlanAction.ExplainDiff (a, b, asJson, depthOpt, channel, onlyModule) ->
        shellRun "projection diff" Shell.ReadOnly (fun () -> runDiff a b asJson (defaultArg depthOpt View.defaultDepth) channel onlyModule)
    | PlanAction.Compare (a, b, asJson)      -> shellRun "projection compare" Shell.ReadOnly (fun () -> runCompare a b asJson)
    | PlanAction.ExplainPolicy (a, b)        -> shellRun "projection explain policy" Shell.ReadOnly (fun () -> runPolicyDiff a b)
    | PlanAction.ExplainNode (c, k, asJson, depthOpt) ->
        shellRun "projection explain node" Shell.ReadOnly (fun () -> runExplain c k asJson (defaultArg depthOpt View.defaultDepth))
    | PlanAction.ExplainSuggest (c, applyTo) -> shellRun "projection explain suggest" Shell.ReadOnly (fun () -> runSuggestConfig c applyTo)
    | PlanAction.ExplainRegistry ->
        // Self-description (NORTH_STAR "self-describing" leg) — the engine names
        // its own registered transforms (the `registered ⇔ executed` registry).
        shellRun "projection explain registry" Shell.ReadOnly (fun () ->
            let all = RegisteredAllTransforms.all
            let stageBindingText (s: StageBinding) =
                match s with
                | StageBinding.Adapter        -> "adapter"
                | StageBinding.Pass           -> "pass"
                | StageBinding.OrderingPolicy -> "ordering"
                | StageBinding.Emitter        -> "emitter"
                | StageBinding.Pipeline       -> "pipeline"
            printfn "projection: %d registered transform(s)" (List.length all)
            for rt in all |> List.sortBy (fun r -> stageBindingText r.StageBinding, r.Name) do
                printfn "  %-12s %s" (stageBindingText rt.StageBinding) rt.Name
            0)
    | PlanAction.ExplainMigratePreview (fromP, toP, decl)   -> shellRun "projection explain migrate" Shell.ReadOnly (fun () -> runMigratePreview fromP toP decl)
    | PlanAction.ExplainMigrateFromStore (store, toP, decl, forceGenesis) -> shellRun "projection explain migrate" Shell.ReadOnly (fun () -> runMigrateFromStore store toP decl forceGenesis)
    // seal ---------------------------------------------------------------
    | PlanAction.SealEject store -> shellRun "projection seal" Shell.Go (fun () -> runEject store)
    | PlanAction.SealApprove (version, approver, rationale, store) -> shellRun "projection seal approve" Shell.Go (fun () -> runApprove version approver rationale store)
    // report -------------------------------------------------------------
    | PlanAction.ReportBundle (store, outputDir) ->
        shellRun "projection report" Shell.ReadOnly (fun () ->
        match ReportRun.fromStore store with
        | Ok bundle ->
            printLines Console.Out (ReportRun.render bundle)
            // Surface the per-run Model Fidelity Report when one was recorded —
            // the rolled-up account of the distance between the declared model
            // and the observed source reality. Searched FIRST in the flow target's
            // own bundle `out` folder (where the full-export feeding this timeline
            // wrote it — threaded by `planReport`), then next to the store and in
            // the default output directory; absent until a profiled run emits it
            // (best-effort, additive — the change report stands alone).
            let fidelityCandidates =
                [ match outputDir with
                  | Some dir when dir <> "" -> yield Path.Combine(dir, "fidelity.json")
                  | _ -> ()
                  match Option.ofObj (Path.GetDirectoryName store) with
                  | Some dir when dir <> "" -> yield Path.Combine(dir, "fidelity.json")
                  | _ -> ()
                  yield Path.Combine("out", "fidelity.json")
                  yield "fidelity.json" ]
            match ReportRun.renderFidelity fidelityCandidates with
            | [] -> ()
            | lines ->
                Console.Out.WriteLine ""
                printLines Console.Out lines
            0
        | Error msg ->
            Console.Error.WriteLine (sprintf "projection report: %s" msg)
            6)
    // slice (data portability) -------------------------------------------
    // The slice faces own their bespoke flag parsing + config resolution and
    // their own bench/narration; since 2026-07-02 they run through the shell
    // like every other verb (the A3 sweep) — run envelopes on the wire,
    // channel-1 suppression + the verdict panel under pretty.
    | PlanAction.RunSliceExtract args        -> shellRun "projection slice-extract" Shell.Go (fun () -> runSliceExtract args)
    | PlanAction.RunSliceApply (reset, args) -> shellRun (if reset then "projection slice-reset" else "projection slice-apply") Shell.Go (fun () -> runSliceApply reset args)
    | PlanAction.RunSliceFlow args           -> shellRun "projection slice-run" Shell.Go (fun () -> runSliceFlow args)
    // refused ------------------------------------------------------------
    | PlanAction.Refused (exit, error) -> TtyRenderer.renderVoicedError error; exit

/// `projection init` — scaffold a `projection.json` so the operator starts from
/// a working surface (first-run ergonomics). Refuses to overwrite an existing
/// file (look-before-overwrite); the conn is a `env:`/`file:` reference (D9).
let private runInit () : int =
    let path = "projection.json"
    if File.Exists path then
        Console.Error.WriteLine (sprintf "projection init: %s already exists; not overwriting." path)
        1
    else
        // LINT-ALLOW: terminal operator-facing scaffold text at the CLI boundary.
        // The shape MUST match `ProjectionConfig.parse` (MovementSurface.fs): the
        // UNIFIED `projection.json` (THE_CONFIG_CONTROL_PLANE) — one document, two
        // views. The movement view is `environments` (access bundle|direct|docker;
        // grant; conn is env:/file:) + `flows` (from/to; opt-in `shape`/`shaping`).
        // The shaping view folds in as sibling namespaces: the canonical `model`
        // OBJECT (env/ossys/path/modules — `env` NAMES the primary environment
        // [resolving to its live OSSYS conn, so the connection is named once, in
        // `environments`], `path` the file fallback, ModelResolution.chooseOrigin),
        // plus `overrides`/`emission`/`policy` (defaulted when absent). The
        // readiness gate's `schema` defaults to `model.env`. Flows now bake the
        // shaping into the publish (ConfigFile→PublishBundle/PublishAndLoad) for
        // store-bearing sinks. A SOURCE-only env carries no grant; only a SINK
        // does. The parser ignores unknown keys.
        let scaffold =
            "{\n" +
            "  \"model\": { \"env\": \"cloud-dev\" },\n" +
            "  \"environments\": {\n" +
            "    \"cloud-dev\":   { \"access\": \"direct\", \"conn\": \"file:./secrets/cloud-dev.conn\", \"rendition\": \"physical\", \"archetype\": \"managed-dml\" },\n" +
            "    \"cloud-qa\":    { \"access\": \"direct\", \"conn\": \"file:./secrets/cloud-qa.conn\",  \"rendition\": \"physical\", \"archetype\": \"managed-dml\" },\n" +
            "    \"local\":       { \"access\": \"docker\" },\n" +
            "    \"on-prem-dev\": { \"access\": \"bundle\", \"out\": \"./dist/on-prem-dev\", \"grant\": \"schema+data\", \"rendition\": \"logical\", \"archetype\": \"full-rights\", \"store\": \"./lifecycle/on-prem-dev.json\" }\n" +
            "  },\n" +
            "  \"readiness\": { \"confirm\": [\"cloud-dev\", \"cloud-qa\"] },\n" +
            "  \"emission\": { \"ssdt\": true, \"dacpac\": true },\n" +
            "  \"flows\": {\n" +
            "    \"try\":      { \"from\": \"cloud-dev\", \"to\": \"local\" },\n" +
            "    \"skeleton\": { \"from\": \"cloud-dev\", \"to\": \"local\", \"shape\": \"skeleton\" },\n" +
            "    \"publish\":  { \"from\": \"cloud-dev\", \"to\": \"on-prem-dev\" }\n" +
            "  }\n" +
            "}\n"
        File.WriteAllText(path, scaffold)
        printfn "projection init: wrote %s." path
        printfn "  Next: put each environment's connection string in ./secrets/<name>.conn (the file's"
        printfn "        contents ARE the connection string; D9, gitignored, never committed) — the model"
        printfn "        is read LIVE from cloud-dev. Then `projection` lists the flows; `projection check"
        printfn "        shape` confirms the cloud cells resolve to one shape; `projection try` previews"
        printfn "        into a throwaway Docker database; `projection publish` emits the on-prem SSDT"
        printfn "        bundle. For the full six-environment estate + the cloud-insertion producers"
        printfn "        (golden / reverse / synth into a data-only cloud sink) see"
        printfn "        examples/projection.sample.json. A live write needs both --go and PROJECTION_ALLOW_EXECUTE=1."
        0

/// Discover `projection.json` (or `PROJECTION_CONFIG`) — absent is the empty
/// config (aliasing is opt-in).
let private discoverConfig () : Result<ProjectionConfig> =
    let path =
        match System.Environment.GetEnvironmentVariable "PROJECTION_CONFIG" with
        | null | "" -> "projection.json"
        | p -> p
    ProjectionConfig.fromFile path

/// `projection survey` — the capability survey (prototype;
/// `HANDOFF_CAPABILITY_SURVEY_2026_06_09.md`). Probe every configured
/// environment in parallel and render the declared-vs-actual capability matrix:
/// is every place actually able to do what the pipeline asks of it?
let private runSurvey () : int =
    match discoverConfig () with
    | Error es ->
        for e in es do TtyRenderer.renderVoicedError e
        6
    | Ok cfg ->
        let reports = (CapabilitySurvey.survey cfg).GetAwaiter().GetResult()
        TtyRenderer.renderAnswer false View.defaultDepth (TtyRenderer.buildSurveyView reports)
        // CI gate: non-zero when a connected environment can't do what is asked.
        // The standalone verb HARD-STOPS (exit 7); the in-flow advisory (G0c)
        // reads the SAME `CapabilitySurvey.blocked` predicate but only warns.
        if reports |> List.exists CapabilitySurvey.blocked then 7 else 0

/// A flow's content origin, rendered for the menu (THE_CLI.md §4.4).
let private flowSourceText (s: FlowSource) : string =
    match s with
    | FlowSource.Env e           -> e
    | FlowSource.Model           -> "model"
    | FlowSource.Synthetic (None, _)   -> "synthetic"
    | FlowSource.Synthetic (Some p, Some c) -> sprintf "synthetic(%s + %s)" p c
    | FlowSource.Synthetic (Some p, None)   -> sprintf "synthetic(%s)" p
    | FlowSource.NoData          -> "none"

/// `projection` with no args lists the flows as `name: from → to (spec)` —
/// the config IS the menu (THE_CLI.md §4.4). No flows configured → the help.
let private runList (asJson: bool) : int =
    match discoverConfig () with
    | Error es ->
        Console.Error.WriteLine "projection: projection.json is invalid:"
        printErrors Console.Error es
        6
    | Ok cfg ->
        if Map.isEmpty cfg.Flows then printLines Console.Out usageLines
        else
            // The menu is a `View` (2026-07-02) — pretty / plain / `--format
            // json` / `--query` are the one document's lenses, like every
            // other answer surface.
            let rows =
                [ for KeyValue (name, f) in cfg.Flows ->
                    let extra =
                        [ if Option.isSome f.Rekey then yield "rekey"
                          if not (List.isEmpty f.Tables) then yield sprintf "tables: %s" (String.concat "," f.Tables)
                          // `reconcile` is as load-bearing as `rekey` on the
                          // golden shape (the re-key contract) — surface it so
                          // the menu scan shows which flows carry a strategy.
                          if not (List.isEmpty f.Reconcile) then yield sprintf "reconcile: %s" (String.concat "," f.Reconcile) ]
                    name, flowSourceText f.From, f.To, String.concat "; " extra ]
            TtyRenderer.renderAnswer asJson View.defaultDepth (TtyRenderer.buildFlowMenuView rows)
        0

[<EntryPoint>]
let main argv =
    // Polish (REPORTING_HORIZON) — global flags, parsed + stripped before
    // verb dispatch so per-verb argv shapes are unchanged.
    //   --pretty / --json / --no-pretty : force the channel; default AUTO
    //     (a real TTY gets the Spectre panel, a pipe gets clean NDJSON — the
    //     operator never thinks about format).
    //   -v / --verbose : surface depth (the bench table, etc.).
    // `--query <path>` (#17) — a GLOBAL value flag: narrows any answer surface to a
    // JSONPath-subset slice of its `View.toJson`. Extracted here (flag + its value)
    // before verb dispatch so per-verb argv shapes are unchanged, and set on the
    // renderer's global so every `renderAnswer` honors it (like --pretty/--verbose).
    let queryArg, argv =
        match Array.tryFindIndex ((=) "--query") argv with
        | Some i ->
            let value = if i + 1 < argv.Length then Some argv.[i + 1] else None
            let rest =
                match value with
                | Some _ -> Array.append argv.[.. i - 1] argv.[i + 2 ..]
                | None   -> Array.append argv.[.. i - 1] argv.[i + 1 ..]
            value, rest
        | None -> None, argv
    TtyRenderer.queryPath := queryArg
    // `--open 1.0.2` (#18) — a GLOBAL value flag: force-reveals exactly that dotted
    // child-index branch of the answer (the headless half of the dig), every other
    // branch at the ambient `--depth`. A malformed path is ignored (the answer renders
    // calm rather than failing the run). Same flag+value strip as `--query`.
    let openArg, argv =
        match Array.tryFindIndex ((=) "--open") argv with
        | Some i ->
            let value = if i + 1 < argv.Length then Some argv.[i + 1] else None
            let rest =
                match value with
                | Some _ -> Array.append argv.[.. i - 1] argv.[i + 2 ..]
                | None   -> Array.append argv.[.. i - 1] argv.[i + 1 ..]
            value, rest
        | None -> None, argv
    TtyRenderer.openPath :=
        openArg
        |> Option.bind (fun s ->
            let parsed = s.Split('.') |> Array.map (fun p -> System.Int32.TryParse p)
            // A path is a dotted list of NON-NEGATIVE child indices; every component must
            // parse AND be ≥ 0 (`Split` never yields an empty array, so an empty / non-numeric
            // / negative component all land here as malformed → None → the answer renders calm).
            // The `≥ 0` clamp matters for #23: child indices come from `List.iteri`, so a
            // negative head can never match one — but a future BARE-node answer surface would
            // see `revealed` fire on a `Some [-1]` root; rejecting it at the door keeps `--open`
            // honest (only real coordinates) regardless of what consumes `OpenPath` next.
            if Array.forall (fun (ok, n) -> ok && n >= 0) parsed
            then Some (parsed |> Array.map snd |> Array.toList)
            else None)
    let has flag = Array.contains flag argv
    verboseMode := has "-v" || has "--verbose"
    // `--stat` (2026-07-02) — after the run closes, the §11 aggregates table
    // on stdout (the headless rollup lens).
    Shell.statMode := has "--stat"
    let forceJson = has "--json" || has "--no-pretty"
    let forcePretty = has "--pretty"
    // "operator wants pretty" — explicit, or auto when stderr is a real TTY
    // and NDJSON wasn't forced. `TtyRenderer.shouldRender` re-checks the TTY
    // so a forced --pretty into a pipe still won't spray ANSI. Pretty drives the
    // live stage board (`Watch`) DURING a run and the verdict panel after it.
    prettyMode := forcePretty || (not forceJson && not Console.IsErrorRedirected)
    // `--watch` is DEPRECATED (2026-06-17 — folded into `--pretty`/auto-TTY; the
    // live board is what pretty shows). Still stripped so an old habit doesn't
    // error, but it no longer carries its own behavior.
    let globalFlags = set [ "--pretty"; "--json"; "--no-pretty"; "-v"; "--verbose"; "--watch"; "--stat" ]
    let argv = argv |> Array.filter (fun a -> not (Set.contains a globalFlags))
    match argv with
    | [| "--help" |] | [| "-h" |] ->
        printLines Console.Out usageLines
        0
    | [||] -> runList forceJson
    | [| "init" |] -> runInit ()
    | [| "inspect" |] -> runInspectHistory forceJson
    | [| "inspect"; runId |] -> runInspect runId None forceJson
    | [| "inspect"; runA; runB |] -> runInspect runA (Some runB) forceJson
    | [| "setup" |] -> runSetup None
    | [| "setup"; "--conn"; ref |] -> runSetup (Some ref)
    | [| "survey" |] -> runSurvey ()
    // The data-portability slice verbs (slice-extract / slice-apply / slice-reset
    // / slice-run) are no longer a second `argv.[0]` dispatcher — they flow through
    // the one typed `Command.parse` → `Command.plan` → `runPlan` plane below, like
    // every other verb (recon #3).
    | _ ->
        match discoverConfig () with
        | Error es ->
            Console.Error.WriteLine "projection: projection.json is invalid:"
            printErrors Console.Error es
            6
        | Ok cfg ->
            match Command.parse cfg (List.ofArray argv) with
            | Error es ->
                for e in es do TtyRenderer.renderVoicedError e
                Console.Error.WriteLine ""
                printLines Console.Error usageLines
                1
            | Ok intent ->
                // Pure routing → effectful runner. The surface→engine map is
                // totality-tested (`Command.plan`); `runPlan` executes + voices.
                //
                // G0c — compute the advisory capability survey HERE (the dispatch
                // layer, where `cfg` is in scope; `discoverConfig`/`survey` live
                // below `runTransfer` in this file, so the survey is threaded IN,
                // never fetched inside the runner). Run it only for a live-Execute
                // Flow (a `--go` flow); preview / non-flow verbs carry no advisory
                // (the empty list). The survey is read-only; its findings warn but
                // never gate (R6 — V2 owns no production write path).
                let surveyAdvisory =
                    match intent with
                    | Intent.Flow (_, opts) when opts.Go ->
                        let reports = (CapabilitySurvey.survey cfg).GetAwaiter().GetResult()
                        CapabilitySurvey.advisoryLines reports
                    | _ -> []
                // S6.4 — the effective shaping for THIS run. A flow may carry an
                // opt-in `shaping` override that deep-overlays the global shaping
                // (`Config.overlay`, whole-section granularity) for its own
                // emission; `None` = the global shaping (byte-identical).
                let effectiveShaping =
                    match intent with
                    | Intent.Flow (flow, _) ->
                        match flow.Shaping with
                        | Some flowShaping -> Config.overlay cfg.Shaping flowShaping
                        | None -> cfg.Shaping
                    | _ -> cfg.Shaping
                // The flow frame (2026-07-02) — a flow-dispatched run presents
                // in the daily surface's own words: the box title, the verdict
                // panel, and the ledger record read "publish: cloud-dev →
                // on-prem-dev — preview", never the engine verb the plan chose.
                // Seeded here (the one place the Intent is in scope); the verb
                // arms preserve it via `Shell.framed`.
                (match intent with
                 | Intent.Flow (flow, opts) ->
                     Shell.currentFrame.Value <-
                         { Command  = sprintf "projection %s" flow.Name
                           Flow     = Some { Name = flow.Name; From = flowSourceText flow.From; To = flow.To }
                           Register = if opts.Go then Shell.Go else Shell.Preview }
                 | _ -> ())
                runPlan effectiveShaping surveyAdvisory (Command.plan cfg intent)
