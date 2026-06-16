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
open Projection.Cli.RunFaces

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
        "                      | data --before <t> --after <t> | ready )"
        "    projection diff <a> <b> [--format json] [--depth N]   change between two refs"
        "    projection explain ( diff <a> <b> [--format json] [--depth N] | policy <a> <b>"
        "                       | node <config> <ssKey> | suggest <config> [--apply <out>] | registry"
        "                       | migrate --to <b> ( --from <a> | --from empty | --store <s> ) [--allow-drops] )"
        "    projection seal ( --store <path> | approve <version> --approver <name> ... )"
        "    projection report <flow>        the on-prem migration-team change bundle"
        "    projection inspect <runId> [<runId>]  a stored run, or what moved between two runs"
        "    projection init                 scaffold a projection.json"
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
        "  --atomic wraps the schema deploy in one transaction (LOCAL full-access DBs"
        "  only — production schema ships via the SSDT/Octopus pipeline, not direct-"
        "  connect). --auto-revert deletes a failed data load's sink-minted rows by"
        "  captured key; without it, --revert-dir <dir> writes the precise revert script."
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
        "table. --pretty / --json force the channel (default AUTO: a TTY gets the panel, a pipe"
        "gets NDJSON). --watch shows the live stage board on a folder-bundle flow run."
        ""
        "Exit codes:"
        "    0  succeeded"
        "    1  argv error (missing input / unknown flow or environment)"
        "    2  parse error (model JSON / spec / config-parse)"
        "    3  execution error (SQL rejected the change; connection open; unbreakable cycle)"
        "    4  Docker unavailable (a docker target; check fidelity)"
        "    5  fidelity divergence (check canary / check drift)"
        "    6  config error (file missing / unparseable / D9; connection-ref resolve)"
        "    7  gate refusal (--go without PROJECTION_ALLOW_EXECUTE=1; permission pre-flight)"
        "    8  data divergence (check data row / null)"
        "    9  refused, fail-loud (undeclared drop; inexpressible ALTER; tightening; verify-failed)"
    ]

let private runPlan (shaping: Config.Config) (surveyAdvisory: string list) (plan: ExecutionPlan) : int =
    for n in plan.Notes do eprintfn "Note — %s" n
    // A7 (no-silent-drop) — the module-filter include flags without a
    // `model.modules` selection are inert; note it on the same channel.
    (match ModuleFilterBinding.inertFlagNote shaping.Model with
     | Some n -> eprintfn "Note — %s" n
     | None -> ())
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
        // --watch + a real TTY → the live stage board (§13), pre-seeded with the
        // pipeline's planned stages so the whole arc is visible from the first frame.
        if Watch.shouldWatch watchMode.Value then Watch.renderWatch Spines.pipeline (Watch.resolveDwellMs ()) run
        else run ()
    | PlanAction.EmitSkeleton (model, modelOssys, dir) ->
        needCatalog modelOssys model (fun cat -> withRun "projection project" (fun () -> runEmitSkeletonOnly cat dir))
    | PlanAction.EmitManifest (model, modelOssys, dir) ->
        needCatalog modelOssys model (fun cat -> withRun "projection project" (fun () -> runEmitManifestOnly shaping cat dir))
    | PlanAction.EmitBundle (model, modelOssys, dir) ->
        needCatalog modelOssys model (fun cat -> withRun "projection project" (fun () -> runEmit shaping cat dir))
    | PlanAction.DeployDocker (model, modelOssys) ->
        needCatalog modelOssys model (fun cat -> withRun "projection project" (fun () -> runDeploy shaping cat))
    | PlanAction.PreviewSchema (model, modelOssys, conn, decl) ->
        needCatalog modelOssys model (fun cat -> withShaped shaping cat (fun shapedCat -> runProjectLivePreview shapedCat conn decl))
    | PlanAction.Transfer (src, sink, opts, execute) ->
        // R1b — the envelope-emitting faces move under `withRun` (the law:
        // a verb that mints envelopes runs bracketed; RI-11's census). The
        // transfer/migrate/synthetic engines emit the staged spines' stage
        // events, so their streams now open with `config.runStart` and
        // close with the §10 terminal — and the run is capturable.
        withRun "projection transfer" (fun () ->
            runTransfer src sink None None opts.Reconcile opts.Rekey execute opts.AllowCdc (opts.Declaration = DeclareAll) opts.Emission opts.Resumable opts.Tables opts.AutoRevert opts.RevertDir surveyAdvisory)
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
                runReverseLegTransfer src sink (CatalogRendition.logical cat) (CatalogRendition.physical cat) opts.Reconcile opts.Rekey execute opts.AllowCdc (opts.Declaration = DeclareAll) opts.Emission opts.Resumable opts.Streaming opts.Journal opts.Tables opts.AutoRevert opts.RevertDir opts.SinkCapability surveyAdvisory))
    | PlanAction.MigrateWithData (model, modelOssys, sink, src, opts) ->
        needCatalog modelOssys model (fun cat -> withShaped shaping cat (fun shapedCat ->
            withRun "projection migrate --with-data" (fun () ->
                runMigrateWithData shapedCat sink src opts.Reconcile opts.Rekey opts.Declaration opts.AllowCdc opts.Store opts.Env opts.SinkCapability)))
    | PlanAction.SynthesizeAndLoad (model, modelOssys, profile, conn, opts, execute, modelSection) ->
        withRun "projection synth-load" (fun () -> runSyntheticLoad model modelOssys profile conn opts execute modelSection)
    | PlanAction.CaptureProfile (conn, out) -> runCaptureProfile conn out
    | PlanAction.PublishAndLoad (c, conn, store, env) ->
        let run () = runFullExportLoad c conn None store env
        // The load flow runs the same publish pipeline, so it streams the same
        // stage arc; --watch shows the live board (§13).
        if Watch.shouldWatch watchMode.Value then Watch.renderWatch Spines.pipeline (Watch.resolveDwellMs ()) run
        else run ()
    | PlanAction.Migrate (model, modelOssys, conn, opts) ->
        needCatalog modelOssys model (fun cat -> withShaped shaping cat (fun shapedCat ->
            withRun "projection migrate" (fun () ->
                runMigrateExecute shapedCat conn opts.Declaration opts.AllowCdc opts.Atomic opts.Store opts.Env)))
    // check --------------------------------------------------------------
    | PlanAction.CheckCanary (ddl, false) -> withRun "projection check" (fun () -> runCanary ddl)
    | PlanAction.CheckCanary (ddl, true)  -> withRun "projection check --cdc-silence" (fun () -> runCanaryCdcSilence ddl)
    | PlanAction.CheckDrift (m, conn)      -> runDrift m conn
    | PlanAction.CheckData (before, after) -> runVerifyData before after
    | PlanAction.CheckReady                -> runReadiness ()
    // explain ------------------------------------------------------------
    | PlanAction.ExplainDiff (a, b, asJson, depthOpt) -> runDiff a b asJson (defaultArg depthOpt View.defaultDepth)
    | PlanAction.Compare (a, b, asJson)      -> runCompare a b asJson
    | PlanAction.ExplainPolicy (a, b)        -> runPolicyDiff a b
    | PlanAction.ExplainNode (c, k, asJson, depthOpt) -> runExplain c k asJson (defaultArg depthOpt View.defaultDepth)
    | PlanAction.ExplainSuggest (c, applyTo) -> runSuggestConfig c applyTo
    | PlanAction.ExplainRegistry ->
        // Self-description (NORTH_STAR "self-describing" leg) — the engine names
        // its own registered transforms (the `registered ⇔ executed` registry).
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
        0
    | PlanAction.ExplainMigratePreview (fromP, toP, decl)   -> runMigratePreview fromP toP decl
    | PlanAction.ExplainMigrateFromStore (store, toP, decl, forceGenesis) -> runMigrateFromStore store toP decl forceGenesis
    // seal ---------------------------------------------------------------
    | PlanAction.SealEject store -> runEject store
    | PlanAction.SealApprove (version, approver, rationale, store) -> runApprove version approver rationale store
    // report -------------------------------------------------------------
    | PlanAction.ReportBundle (store, outputDir) ->
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
            6
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
        // OBJECT (path/ossys/modules — `ossys` is the LIVE primary, `path` the file
        // fallback, ModelResolution.chooseOrigin), plus `overrides`/`emission`/
        // `policy` (defaulted when absent). Flows now bake the shaping into the
        // publish (ConfigFile→PublishBundle/PublishAndLoad) for store-bearing sinks.
        // A SOURCE-only env carries no grant; only a SINK does. The parser ignores
        // unknown keys.
        let scaffold =
            "{\n" +
            "  \"model\": { \"ossys\": \"file:./secrets/ossys.conn\" },\n" +
            "  \"environments\": {\n" +
            "    \"local\":      { \"access\": \"docker\" },\n" +
            "    \"onprem-dev\": { \"access\": \"bundle\", \"out\": \"./dist/onprem-dev\", \"grant\": \"schema+data\", \"rendition\": \"logical\", \"store\": \"./lifecycle/onprem-dev.json\" }\n" +
            "  },\n" +
            "  \"emission\": { \"ssdt\": true, \"dacpac\": true },\n" +
            "  \"flows\": {\n" +
            "    \"try\":      { \"from\": \"model\", \"to\": \"local\" },\n" +
            "    \"skeleton\": { \"from\": \"model\", \"to\": \"local\", \"shape\": \"skeleton\" },\n" +
            "    \"publish\":  { \"from\": \"model\", \"to\": \"onprem-dev\" }\n" +
            "  }\n" +
            "}\n"
        File.WriteAllText(path, scaffold)
        printfn "projection init: wrote %s." path
        printfn "  Next: put your cloud OutSystems connection string in ./secrets/ossys.conn — the"
        printfn "        model is read LIVE from it (the file's contents ARE the connection string; D9,"
        printfn "        gitignored, never committed). The engine reads the file directly — no shell"
        printfn "        export. Then `projection` lists the flows; `projection try` previews into a"
        printfn "        throwaway Docker database; `projection publish` emits the on-prem SSDT bundle."
        printfn "        For the cloud-insertion flows (golden / preview / synth into a data-only cloud"
        printfn "        sink) see examples/projection.sample.json. A live write needs both --go and"
        printfn "        PROJECTION_ALLOW_EXECUTE=1."
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
    | FlowSource.Synthetic None  -> "synthetic"
    | FlowSource.Synthetic (Some p) -> sprintf "synthetic(%s)" p
    | FlowSource.NoData          -> "none"

/// `projection` with no args lists the flows as `name: from → to (spec)` —
/// the config IS the menu (THE_CLI.md §4.4). No flows configured → the help.
let private runList () : int =
    match discoverConfig () with
    | Error es ->
        Console.Error.WriteLine "projection: projection.json is invalid:"
        printErrors Console.Error es
        6
    | Ok cfg ->
        if Map.isEmpty cfg.Flows then printLines Console.Out usageLines
        else
            Console.Out.WriteLine "Flows (projection <flow> [--go] [--fresh] [--allow-drops]):"
            for KeyValue (name, f) in cfg.Flows do
                let extra =
                    [ if Option.isSome f.Rekey then yield "rekey"
                      if not (List.isEmpty f.Tables) then yield sprintf "tables: %s" (String.concat "," f.Tables) ]
                let suffix = if List.isEmpty extra then "" else sprintf "  (%s)" (String.concat "; " extra)
                Console.Out.WriteLine(sprintf "  %-16s %s → %s%s" name (flowSourceText f.From) f.To suffix)
        0

[<EntryPoint>]
let main argv =
    // Polish (REPORTING_HORIZON) — global flags, parsed + stripped before
    // verb dispatch so per-verb argv shapes are unchanged.
    //   --pretty / --json / --no-pretty : force the channel; default AUTO
    //     (a real TTY gets the Spectre panel, a pipe gets clean NDJSON — the
    //     operator never thinks about format).
    //   -v / --verbose : surface depth (the bench table, etc.).
    let has flag = Array.contains flag argv
    verboseMode := has "-v" || has "--verbose"
    watchMode := has "--watch"
    let forceJson = has "--json" || has "--no-pretty"
    let forcePretty = has "--pretty"
    // "operator wants pretty" — explicit, or auto when stderr is a real TTY
    // and NDJSON wasn't forced. `TtyRenderer.shouldRender` re-checks the TTY
    // so a forced --pretty into a pipe still won't spray ANSI.
    prettyMode := forcePretty || (not forceJson && not Console.IsErrorRedirected)
    let globalFlags = set [ "--pretty"; "--json"; "--no-pretty"; "-v"; "--verbose"; "--watch" ]
    let argv = argv |> Array.filter (fun a -> not (Set.contains a globalFlags))
    match argv with
    | [| "--help" |] | [| "-h" |] ->
        printLines Console.Out usageLines
        0
    | [||] -> runList ()
    | [| "init" |] -> runInit ()
    | [| "inspect"; runId |] -> runInspect runId None
    | [| "inspect"; runA; runB |] -> runInspect runA (Some runB)
    | [| "setup" |] -> runSetup None
    | [| "setup"; "--conn"; ref |] -> runSetup (Some ref)
    | [| "survey" |] -> runSurvey ()
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
                runPlan effectiveShaping surveyAdvisory (Command.plan cfg intent)
