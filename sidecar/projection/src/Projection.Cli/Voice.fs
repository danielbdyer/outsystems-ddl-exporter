namespace Projection.Cli

open Projection.Core
open Projection.Pipeline

/// THE VOICE — the operator-facing copy of the instrument, made structural
/// (`THE_VOICE.md` is this layer's spec; `THE_VOICE_INTEGRATION.md` §5/§8 is the
/// shape; `DECISIONS 2026-06-06`). The **Voice** layer is the projection
/// `Code → operator copy`, keyed centrally — the boundary-side sibling of the
/// engine's ubiquitous language. A site emits a *coded event* (a `Code` + typed
/// payload, no prose); the **copy** is declared here, harvested into `all`,
/// consumed by the renderer (`TtyRenderer`), and held honest by the `code ⇔ copy`
/// totality test (`VoiceTotalityTests` — the sibling of `registered ⇔ executed`).
/// Inline prose welded to control flow (`sprintf` mid-fold) is forbidden; Voice
/// is concern-shaped (a sibling of Lineage / Diagnostics / Bench / Registry) yet
/// has **no runtime write side** — the declarations are static copy data,
/// projected at render.
///
/// **Scope (slice 1 + the slice-2 stage scaffold).** This voices the flat codes
/// that **already** fire — the lifecycle spine (`config.*`, `summary.*`), the
/// round-trip-verification verdict (`canary.*`), the per-stage Watch lines
/// (`extract/profile/emit.started/.completed`, `summary.stageCompleted`), and the
/// `pipeline.config.*` error family — derived from `THE_VOICE.md`, no new events
/// (IR grows under evidence). This is hybrid §5 mechanism 2 — the code-keyed
/// declarative catalog. The payload-shaped move / gate / proof surfaces (the
/// eleven moves §4, the gates §5, the proofs §6) take mechanism 1 (typed `toView`
/// beside their boundary projections in `EventProjection` / `TtyRenderer`) in a
/// later slice; the streaming Watch render path (§13) + the run unification are
/// the rest of slice 2.
///
/// **Pure-Core passes are voiced by a 1:1 projection-layer companion**, not in
/// `Projection.Core` (`DECISIONS 2026-06-06` Core-purity sub-call, resolved
/// 2026-06-06): `Voice.<Pass> ↔ <Pass>`. No such companion is needed yet — the
/// move/gate/proof carriers are all Pipeline/CLI or boundary-projected; the
/// pure-Core pass lift is the deferred slice-5 `DiagnosticEntry.Message` work.
///
/// **The twelve rules govern every string here** (`THE_VOICE.md` §1 + the banned
/// list §2.2): no pronouns; direction by imperative; legible statement with the
/// formal substantiation beneath; verdicts asserted; the true verb; gentle and
/// direct; neutral reference to the estate; every claim grounded in its evidence;
/// ordered by real structure; the exact referent; concrete definite subjects;
/// stative and agentless. A line that breaks one is not finished.
[<RequireQualifiedAccess>]
module Voice =

    /// The envelope payload a copy template fills from — the `LogSink.Envelope`'s
    /// `Payload` shape (`Map<string, objnull>`), passed without taking a Pipeline
    /// dependency (Voice depends only on `Core` + the `View`/`Surface` substrate).
    type Payload = Map<string, objnull>

    /// One voiced code: the operator copy for a `Code`, derived from a named
    /// section of `THE_VOICE.md` (decision 5 — the catalog stays isomorphic to the
    /// doc), as a statement-first `Surface` built from the event's payload. The
    /// `Statement` is the plain lead finding; the `Substantiation` is the formal
    /// proof one level beneath; the `Action` is the next move (`None` when the
    /// surface ends on its verdict).
    type Copy =
        {
            Code           : string
            /// The `THE_VOICE.md` section this copy derives from (e.g. "§3", "§6",
            /// "§10", "§13", "§14"). Asserted non-empty + recognized by the
            /// totality test.
            DocSection     : string
            Statement      : Payload -> View.View
            Substantiation : Payload -> View.View list
            Action         : Payload -> View.View option
        }

    // ------------------------------------------------------------------
    // Payload readers — extract a typed value, defaulting safely. The
    // boundary is `objnull` because the envelope contract is
    // `Map<string, objnull>`; a missing or null key yields `None`.
    // ------------------------------------------------------------------

    let private text (key: string) (p: Payload) : string option =
        match Map.tryFind key p with
        | Some v when not (isNull v) -> Some(string v)
        | _ -> None

    let private textOr (key: string) (fallback: string) (p: Payload) : string =
        match text key p with
        | Some s -> s
        | None   -> fallback

    /// Humane numerals — `2,140`, not `2140` (`THE_VOICE.md` §12: "the number
    /// scales; the sentence does not"). Non-numeric input passes through.
    let private humane (s: string) : string =
        match System.Int64.TryParse s with
        | true, n  -> n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)
        | false, _ -> s

    /// The map a stage's internal name takes to its operator-facing name
    /// (`THE_VOICE.md` §13 — "Stage names are what they do for the operator …
    /// never the internal verb (`Snapshot`, `Profile`, `emit`, `canary`)").
    /// Resultative form (a *completed* stage); the in-progress gerund forms live
    /// on the per-stage `*.started` entries. Public so the totality test asserts
    /// the mapping is exhaustive over the stages the orchestration emits, and so a
    /// streaming Watch (slice 2) reuses the one mapping.
    let stageName (internalStage: string) : string =
        match internalStage with
        | "pipeline"  -> "The pipeline"
        | "extract"   -> "Model read"
        | "profile"   -> "Data check"
        | "emit"      -> "Change build"
        | "preflight" -> "Safety checks"
        | "deploy"    -> "Deploy"
        | "canary"    -> "Round-trip verification"
        | "load"      -> "Data load"
        | "store"     -> "Provenance record"
        | "seed-load" -> "Seed load"
        | other       -> other

    /// The §13 follow-on for a run whose terminal stage is `terminalStage` — "the
    /// rhythm names the next move" (a finished change build offers the verify; a
    /// finished verify offers the record). Stative + agentless: the next phase is
    /// named as a state that follows, never an instruction to the reader. Public so
    /// the Watch board's done-frame reads the one mapping and the totality test can
    /// assert it is total over the terminal stages the board can reach.
    let followOnAfter (terminalStage: string) : string =
        match terminalStage with
        | "extract"   -> "The data check follows."
        | "profile"   -> "The change build follows."
        | "emit"      -> "Verification follows."
        | "preflight" -> "The deploy follows."
        | "deploy"    -> "Verification follows."
        | "canary"    -> "The record follows."
        | "load"      -> "Verification follows."
        | "store"     -> "The bundle is published and the change is recorded."
        | "seed-load" -> "The data is loaded. Verification follows."
        | _           -> "The run is complete."

    /// The §13 follow-on for a run whose terminal stage HALTED — NM-46: a run that
    /// stops at its terminal stage must still close with the next move, never go
    /// silent on the red ✕ (the board's cardinal §13 rule, "never end at 'done'").
    /// The cause already lives on the run's error surface; this follow-on names the
    /// remediation the board points to. Stative + agentless; never a pronoun, never
    /// "failed"/"refused" as a lead (the §2.2 banned register). Public so the board's
    /// done-frame reads the one mapping and the totality test asserts both branches.
    let followOnHalted (terminalStage: string) : string =
        sprintf "%s stopped. The cause is on the error surface above; resolve it and re-run." (stageName terminalStage)

    // ------------------------------------------------------------------
    // The catalog — grouped by `THE_VOICE.md` section (decision 5: the
    // harvested catalog mirrors the doc). Each entry is a separable
    // declarative value (deletable without touching control flow).
    // ------------------------------------------------------------------

    // §3 — the verdict line (the finding: is it safe? did it work? what is next?)
    // §6 — the proofs (fidelity, made plain). The round-trip verification verdict
    //      is the engine's claim about itself, stated as a grounded finding.

    /// `canary.diffEmpty` — the round-trip verification passed (`THE_VOICE.md` §3
    /// "Done and verified" / §6 commuting square). The deepest claim the engine
    /// makes about itself, asserted; the formal proof one level beneath.
    let private canaryDiffEmpty : Copy =
        { Code           = "canary.diffEmpty"
          DocSection     = "§6"
          Statement      = fun _ -> View.Hero(View.Ok, "Verified. The deployed database matches the model.")
          Substantiation =
            fun p ->
                [ View.Field("evidence", "round-trip residual empty · Ingest ∘ Project = id", View.Neutral) ]
                @ (match text "tableCount" p with
                   | Some n -> [ View.Field("tables", sprintf "%s compared" (humane n), View.Neutral) ]
                   | None   -> [])
          Action         = fun _ -> None }

    /// `canary.divergence` — the round-trip verification returned a difference
    /// (`THE_VOICE.md` §6 / §10 "round-trip verification failed"). Candid, names
    /// the next move; the rendered diff is the substantiation, not the lead.
    let private canaryDivergence : Copy =
        { Code           = "canary.divergence"
          DocSection     = "§10"
          Statement      = fun _ -> View.Hero(View.Bad, "The round-trip diverged from the model. It blocks the commit.")
          Substantiation =
            fun p ->
                // The raw read-back diff is demoted into a disclosure — the
                // statement is the finding, the difference opens on demand (§9).
                match text "renderedDiff" p with
                | Some d -> [ View.Disclosure("the difference", View.Neutral, [ View.Note d ]) ]
                | None   -> []
          Action         = fun _ -> Some(View.Action "Resolve the difference, then re-verify.") }

    /// `summary.runComplete` — the terminal verdict (`THE_VOICE.md` §3). The
    /// outcome is asserted; a failure names that the cause is shown below, never a
    /// system-shout. `outcome` ∈ { succeeded · failed · aborted }.
    let private summaryRunComplete : Copy =
        { Code           = "summary.runComplete"
          DocSection     = "§3"
          Statement      =
            fun p ->
                match textOr "outcome" "succeeded" p with
                | "succeeded" -> View.Hero(View.Ok, "The run completed without error.")
                | _           -> View.Hero(View.Bad, "Stopped before completion. The cause is shown below.")
          Substantiation =
            fun p ->
                match text "durationMs" p with
                | Some ms -> [ View.Field("duration", sprintf "%s ms" (humane ms), View.Neutral) ]
                | None    -> []
          Action         = fun _ -> None }

    /// `load.completed` — the publish-and-load verdict (`THE_VOICE.md` §4 data
    /// plane / §6 minimality): the bundle is published and the idempotent seed is
    /// loaded, the data movement grounded in its measure (the CDC capture count =
    /// rows captured, rule 8). A face-verdict code rendered by the run face
    /// (`renderVoicedTo`), the sibling of `canary.diffEmpty`.
    let private loadCompleted : Copy =
        { Code           = "load.completed"
          DocSection     = "§6"
          Statement      =
            fun p ->
                match text "capturedRows" p with
                | Some n -> View.Hero(View.Ok, sprintf "The bundle is published and the seed is loaded — %s rows captured." (humane n))
                | None   -> View.Hero(View.Ok, "The bundle is published and the seed is loaded.")
          Substantiation =
            fun p ->
                (match text "artifactCount" p with
                 | Some n -> [ View.Field("artifacts", sprintf "%s published" (humane n), View.Neutral) ]
                 | None   -> [])
                @ (match text "capturedRows" p with
                   | Some n -> [ View.Field("evidence", sprintf "CDC capture count = %s — the measured data movement" (humane n), View.Neutral) ]
                   | None   -> [])
          Action         = fun _ -> None }

    /// `deploy.completed` — the deploy verdict (`THE_VOICE.md` §3): the change
    /// build landed in SQL Server. Resultative and agentless; the ephemeral
    /// database name is the substantiation, never the lead.
    let private deployCompleted : Copy =
        { Code           = "deploy.completed"
          DocSection     = "§3"
          Statement      =
            fun p ->
                match text "tableCount" p with
                | Some n -> View.Hero(View.Ok, sprintf "Deploy complete — %s tables created." (humane n))
                | None   -> View.Hero(View.Ok, "Deploy complete.")
          Substantiation =
            fun p ->
                match text "database" p with
                | Some db -> [ View.Field("database", db, View.Neutral) ]
                | None    -> []
          Action         = fun _ -> None }

    /// `deploy.ssdtRejected` — SQL Server rejected the emitted SSDT
    /// (`THE_VOICE.md` §10): the statement is the plain finding; the server's
    /// own messages are demoted into a disclosure (the substantiation), exactly
    /// as `canary.divergence` demotes the rendered diff. The database name rides
    /// beneath; the move ends the surface.
    let private deploySsdtRejected : Copy =
        { Code           = "deploy.ssdtRejected"
          DocSection     = "§10"
          Statement      = fun _ -> View.Hero(View.Bad, "SQL Server rejected the change build. The server's findings are shown below.")
          Substantiation =
            fun p ->
                (match text "database" p with
                 | Some db -> [ View.Field("database", db, View.Neutral) ]
                 | None    -> [])
                @ (match text "serverErrors" p with
                   | Some es ->
                       let lines =
                           es.Split '\n'
                           |> Array.toList
                           |> List.filter (fun l -> l.Trim() <> "")
                       [ View.Disclosure("the server's findings", View.Bad, lines |> List.map View.Note) ]
                   | None -> [])
          Action         = fun _ -> Some(View.Action "Resolve the findings, then redeploy.") }

    /// `canary.cdcSilent` — the CDC-silence proof (`THE_VOICE.md` §6): the
    /// deepest fidelity finding, said plain and grounded in both its zeros.
    let private canaryCdcSilent : Copy =
        { Code           = "canary.cdcSilent"
          DocSection     = "§6"
          Statement      = fun _ -> View.Hero(View.Ok, "Confirmed idempotent: zero rows captured, zero schema changes issued.")
          Substantiation = fun _ -> [ View.Field("evidence", "CDC capture count = 0 · zero ALTER statements", View.Neutral) ]
          Action         = fun _ -> None }

    /// `canary.cdcCaptured` — the CDC-silence proof failed (`THE_VOICE.md` §6 /
    /// §10): the redeploy touched rows where the proof requires silence. The
    /// finding is asserted with its measure; the capture detail is in the run's
    /// events. Ends on the verdict — no single lever exists for this finding.
    let private canaryCdcCaptured : Copy =
        { Code           = "canary.cdcCaptured"
          DocSection     = "§6"
          Statement      =
            fun p ->
                match text "capturedRows" p with
                | Some n -> View.Hero(View.Bad, sprintf "The redeploy was not idempotent — %s rows captured where zero were expected." (humane n))
                | None   -> View.Hero(View.Bad, "The redeploy was not idempotent — rows were captured where zero were expected.")
          Substantiation =
            fun p ->
                match text "capturedRows" p with
                | Some n -> [ View.Field("evidence", sprintf "CDC capture count = %s" (humane n), View.Neutral) ]
                | None   -> []
          Action         = fun _ -> None }

    /// `drift.none` — the deployed-vs-model check found no divergence
    /// (`THE_VOICE.md` §6 commuting square, the drift lens). Asserted; the
    /// residual evidence beneath.
    let private driftNone : Copy =
        { Code           = "drift.none"
          DocSection     = "§6"
          Statement      = fun _ -> View.Hero(View.Ok, "Verified. The deployed schema matches the model.")
          Substantiation = fun _ -> [ View.Field("evidence", "deployed ⊖ model residual empty", View.Neutral) ]
          Action         = fun _ -> None }

    /// `drift.diverged` — the deployed schema differs from the model
    /// (`THE_VOICE.md` §5 drift gate / §10): a watchful finding, the rendered
    /// difference demoted into the disclosure, ending on the operator's levers.
    let private driftDiverged : Copy =
        { Code           = "drift.diverged"
          DocSection     = "§5"
          Statement      = fun _ -> View.Hero(View.Warn, "The deployed schema diverges from the model. The difference is shown below.")
          Substantiation =
            fun p ->
                match text "renderedDiff" p with
                | Some d -> [ View.Disclosure("the difference", View.Warn, [ View.Note d ]) ]
                | None   -> []
          Action         = fun _ -> Some(View.Action "Remediate the server, or update the model, then re-run the check.") }

    // §13 — lifecycle & the live run (Watch). Stage names are what they do for the
    //       operator, never the engine verb. The gerund names a live activity in
    //       progress (rule 12 exception); a completed stage is resultative.

    /// `canary.deployed` — both sides of the round-trip verification are in
    /// place (`THE_VOICE.md` §13, resultative): the source schema and the
    /// engine's own emission, each deployed to its ephemeral database.
    let private canaryDeployed : Copy =
        { Code           = "canary.deployed"
          DocSection     = "§13"
          Statement      =
            fun p ->
                match text "sourceTables" p, text "targetTables" p with
                | Some s, Some t ->
                    View.Note(sprintf "Both sides deployed — source %s tables, target %s tables." (humane s) (humane t))
                | _ -> View.Note "Both sides deployed."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `container.starting` — an ephemeral SQL Server container is coming up for
    /// a run that needs one (`THE_VOICE.md` §13). Gerund-in-progress (rule 12
    /// exception); `purpose` names which face the container serves.
    let private containerStarting : Copy =
        { Code           = "container.starting"
          DocSection     = "§13"
          Statement      =
            fun p ->
                match text "purpose" p with
                | Some purpose -> View.Note(sprintf "Starting an ephemeral SQL Server container for the %s." purpose)
                | None         -> View.Note "Starting an ephemeral SQL Server container."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `deploy.bundleEmitted` — the change build is complete and its SSDT bundle
    /// is in hand (`THE_VOICE.md` §13, resultative). The entry count grounds the
    /// line; the emitter internals (the JSON node machinery) stay off the surface.
    let private deployBundleEmitted : Copy =
        { Code           = "deploy.bundleEmitted"
          DocSection     = "§13"
          Statement      =
            fun p ->
                match text "entryCount" p with
                | Some n -> View.Note(sprintf "Change build complete — %s SSDT bundle entries." (humane n))
                | None   -> View.Note "Change build complete."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `eject.packaged` — the provenance package is assembled (`THE_VOICE.md`
    /// §13, resultative; §7 P-7): every episode preserved, the rename records
    /// carried; the timeline named beneath. ("append-forever" and "refactorlog"
    /// stay off the surface — §2.1: the operator's words are the history and
    /// the record.)
    let private ejectPackaged : Copy =
        { Code           = "eject.packaged"
          DocSection     = "§13"
          Statement      =
            fun p ->
                match text "episodeCount" p, text "refactorLogCount" p with
                | Some n, Some r ->
                    View.Note(sprintf "Provenance package assembled — %s episodes preserved, %s rename records carried." (humane n) (humane r))
                | _ -> View.Note "Provenance package assembled — every episode preserved."
          Substantiation =
            fun p ->
                match text "timeline" p with
                | Some tl -> [ View.Field("timeline", tl, View.Neutral) ]
                | None    -> []
          Action         = fun _ -> None }

    /// `episode.recorded` — the run's durable record (`THE_VOICE.md` §13 — "This
    /// run recorded to the history."). Stative and agentless: the record is a
    /// state, named with its episode ordinal and timeline when present.
    let private episodeRecorded : Copy =
        { Code           = "episode.recorded"
          DocSection     = "§13"
          Statement      =
            fun p ->
                match text "episodeCount" p, text "timeline" p with
                | Some n, Some tl ->
                    View.Note(sprintf "This run recorded to the history — episode %s on timeline %s." (humane n) tl)
                | _ -> View.Note "This run recorded to the history."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `config.runStart` — the run opens by reading its configuration
    /// (`THE_VOICE.md` §13 / Act 1 Reading). A gerund-in-progress (rule 12
    /// exception): a live activity is a state, not a deed.
    let private configRunStart : Copy =
        { Code           = "config.runStart"
          DocSection     = "§13"
          Statement      =
            fun p ->
                match text "configPath" p with
                | Some path -> View.Note(sprintf "Reading the configuration — %s." path)
                | None      -> View.Note "Reading the configuration."
          Substantiation =
            fun p ->
                match text "outputDir" p with
                | Some out -> [ View.Field("output", out, View.Neutral) ]
                | None     -> []
          Action         = fun _ -> None }

    /// `config.connectionResolved` — the model source is resolved
    /// (`THE_VOICE.md` §14 / Act 0). Resultative, agentless (rule 12).
    let private configConnectionResolved : Copy =
        { Code           = "config.connectionResolved"
          DocSection     = "§14"
          Statement      =
            fun p ->
                match text "modelPath" p with
                | Some m -> View.Note(sprintf "Model source resolved — %s." m)
                | None   -> View.Note "Model source resolved."
          Substantiation =
            fun p ->
                match text "kind" p with
                | Some k -> [ View.Field("source", k, View.Neutral) ]
                | None   -> []
          Action         = fun _ -> None }

    /// `extract.started` — the model read is in progress (`THE_VOICE.md` §13 /
    /// Act 1). Gerund-in-progress (rule 12 exception).
    let private extractStarted : Copy =
        { Code           = "extract.started"
          DocSection     = "§13"
          Statement      = fun _ -> View.Note "Reading the model."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `extract.completed` — the model read is complete (`THE_VOICE.md` §13).
    /// Resultative, agentless ("Model read complete — N modules").
    let private extractCompleted : Copy =
        { Code           = "extract.completed"
          DocSection     = "§13"
          Statement      =
            fun p ->
                match text "moduleCount" p with
                | Some n -> View.Note(sprintf "Model read complete — %s modules." (humane n))
                | None   -> View.Note "Model read complete."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `profile.started` — the data check is in progress (`THE_VOICE.md` §13 /
    /// Act 2). Gerund-in-progress.
    let private profileStarted : Copy =
        { Code           = "profile.started"
          DocSection     = "§13"
          Statement      = fun _ -> View.Note "Checking the data."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `profile.completed` — the data check is complete (`THE_VOICE.md` §13).
    let private profileCompleted : Copy =
        { Code           = "profile.completed"
          DocSection     = "§13"
          Statement      = fun _ -> View.Note "Data check complete."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `emit.started` — the change build is in progress (`THE_VOICE.md` §13 /
    /// Act 4). Gerund-in-progress.
    let private emitStarted : Copy =
        { Code           = "emit.started"
          DocSection     = "§13"
          Statement      = fun _ -> View.Note "Building the changes."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `emit.completed` — the change build is complete (`THE_VOICE.md` §13).
    let private emitCompleted : Copy =
        { Code           = "emit.completed"
          DocSection     = "§13"
          Statement      = fun _ -> View.Note "Change build complete."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `preflight.started` — the migrate engine's safety gates are running
    /// (the 6.A.13 CDC gate + the G9 tightening gate — card S4b made them a
    /// declared stage). Gerund-in-progress (rule 12 exception).
    let private preflightStarted : Copy =
        { Code           = "preflight.started"
          DocSection     = "§13"
          Statement      = fun _ -> View.Note "Checking the change is safe to apply."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `deploy.started` — the changes are being applied (`THE_VOICE.md` §13 /
    /// Act 4, the migrate leg). Gerund-in-progress (rule 12 exception).
    let private deployStarted : Copy =
        { Code           = "deploy.started"
          DocSection     = "§13"
          Statement      = fun _ -> View.Note "Applying the changes."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `canary.started` — the round-trip is being verified (`THE_VOICE.md` §13 /
    /// §6, the migrate leg's verify phase). Gerund-in-progress.
    let private canaryStarted : Copy =
        { Code           = "canary.started"
          DocSection     = "§13"
          Statement      = fun _ -> View.Note "Verifying the round-trip."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `load.started` — the data is being loaded (`THE_VOICE.md` §13 / Act 4,
    /// the data-transfer leg). Gerund-in-progress; the live board appends the
    /// per-table progress + estimate beside it.
    let private loadStarted : Copy =
        { Code           = "load.started"
          DocSection     = "§13"
          Statement      = fun _ -> View.Note "Loading the data."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `store.started` — the publish store leg is in progress (`THE_VOICE.md`
    /// §13; the 2026-07-02 publish-spine completion). Gerund-in-progress: the
    /// change is measured against the prior emission and the episode recorded.
    let private storeStarted : Copy =
        { Code           = "store.started"
          DocSection     = "§13"
          Statement      = fun _ -> View.Note "Recording the change against the prior emission."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `seed-load.started` — the publish-and-load seed leg is in progress
    /// (`THE_VOICE.md` §13). The idempotent MERGE seed, distinct from the
    /// transfer's data load.
    let private seedLoadStarted : Copy =
        { Code           = "seed-load.started"
          DocSection     = "§13"
          Statement      = fun _ -> View.Note "Loading the seed."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `watch.runTitle` — the live board's run-title header (`THE_VOICE.md` §13 —
    /// "the instrument speaks about its own running"). A neutral, agentless naming
    /// of the run in flight (the command is the subject, never "you"); the board
    /// reuses it as the frame the stage lines fill beneath. A render-synthesized
    /// frame, not a LogSink envelope — the board is a rendering of the run, so the
    /// title is voiced here (one register) rather than authored in the renderer.
    let private watchRunTitle : Copy =
        { Code           = "watch.runTitle"
          DocSection     = "§13"
          Statement      =
            fun p ->
                match text "command" p with
                | Some cmd -> View.Note(sprintf "%s — the run in flight." cmd)
                | None     -> View.Note "The run in flight."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `watch.runDone` — the live board's done-frame (`THE_VOICE.md` §13 — "the
    /// rhythm names the next move … Nothing terminates at 'done'"). When the run
    /// reaches its terminal stage, the board names what follows (`followOn`,
    /// derived from the terminal stage — e.g. "Verification follows" after the
    /// change build) and, when a run identity is present, states the record plainly
    /// ("Recorded as run N"). Stative + agentless; the follow-on is the §13 move.
    let private watchRunDone : Copy =
        { Code           = "watch.runDone"
          DocSection     = "§13"
          Statement      =
            fun p ->
                let followOn = textOr "followOn" "The run is complete." p
                match text "runIdentity" p with
                | Some n -> View.Note(sprintf "%s Recorded as run %s." followOn (humane n))
                | None   -> View.Note followOn
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `watch.stageHalted` — the live board's line for a stage whose bracket
    /// closed without success (`failed` / `aborted` on the wire — the R2 Aborted
    /// arm made visible). Candid and stative: the stage stopped; the run's own
    /// error surface carries the cause. A render-synthesized frame like
    /// `watch.runTitle` — the board is a rendering, so the copy is voiced here,
    /// never authored in the renderer; never a `✓` that misstates (§13).
    let private watchStageHalted : Copy =
        { Code           = "watch.stageHalted"
          DocSection     = "§13"
          Statement      =
            fun p -> View.Note(sprintf "%s stopped." (stageName (textOr "stage" "stage" p)))
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `summary.stageCompleted` — a stage of the run completed (`THE_VOICE.md`
    /// §13). Resultative; the stage name is operator-shaped via `stageName`,
    /// never the internal engine verb.
    let private summaryStageCompleted : Copy =
        { Code           = "summary.stageCompleted"
          DocSection     = "§13"
          Statement      =
            fun p -> View.Note(sprintf "%s complete." (stageName (textOr "stage" "stage" p)))
          Substantiation =
            fun p ->
                match text "durationMs" p with
                | Some ms -> [ View.Field("duration", sprintf "%s ms" (humane ms), View.Neutral) ]
                | None    -> []
          Action         = fun _ -> None }

    // §14 — configuration & setup; §10 — refusals & errors. A thing not configured
    //       is a choice to make, not a failure; a real problem is concrete and
    //       located, never a raw exception.

    /// `config.validationFailed` — the configuration could not be loaded
    /// (`THE_VOICE.md` §14 set-but-invalid / §10). The located cause is the
    /// substantiation (rule 3); the statement is the calm §14 frame.
    let private configValidationFailed : Copy =
        { Code           = "config.validationFailed"
          DocSection     = "§14"
          Statement      = fun _ -> View.Hero(View.Bad, "The configuration has a problem. Correct it and rerun.")
          Substantiation =
            fun p ->
                (match text "reason" p with Some r -> [ View.Field("problem", r, View.Neutral) ] | None -> [])
                @ (match text "code" p with Some c -> [ View.Field("code", c, View.Neutral) ] | None -> [])
          Action         = fun _ -> Some(View.Action "Correct the configuration and rerun.") }

    /// `verifyData.matched` — the post-deploy data-integrity check passed
    /// (`THE_VOICE.md` §6, the data-fidelity complement of the structural
    /// round-trip): asserted, with the compared measures as the evidence.
    let private verifyDataMatched : Copy =
        { Code           = "verifyData.matched"
          DocSection     = "§6"
          Statement      = fun _ -> View.Hero(View.Ok, "Verified. The data matches across both deployments.")
          Substantiation = fun _ -> [ View.Field("evidence", "per-table row counts and per-column null counts equal", View.Neutral) ]
          Action         = fun _ -> None }

    /// `verifyData.diverged` — the data differs between the two deployments
    /// (`THE_VOICE.md` §6 / §10): the finding leads; the per-table deltas are
    /// demoted into disclosures (row counts · null counts · schema differences),
    /// each counted on its headline; the surface ends on the move.
    let private verifyDataDiverged : Copy =
        { Code           = "verifyData.diverged"
          DocSection     = "§6"
          Statement      = fun _ -> View.Hero(View.Bad, "The data diverges between the two deployments. The differences are shown below.")
          Substantiation =
            fun p ->
                let detailLines (v: string) =
                    v.Split '\n' |> Array.toList |> List.filter (fun l -> l.Trim() <> "")
                let block (key: string) (label: string) (status: View.Status) =
                    match text key p with
                    | Some v ->
                        let lines = detailLines v
                        [ View.Disclosure(
                            sprintf "%s — %s differ" label (humane (string (List.length lines))),
                            status, lines |> List.map View.Note) ]
                    | None -> []
                block "rowDeltas" "row counts" View.Bad
                @ block "nullDeltas" "null counts" View.Bad
                @ block "schemaWarnings" "schema differences" View.Warn
          Action         = fun _ -> Some(View.Action "Investigate the listed tables, then re-run the check.") }

    /// `eject.verified` — the freeze's self-verification passed (`THE_VOICE.md`
    /// §6 / §7 P-7): the reconstruction from genesis reproduces the frozen
    /// state. Asserted; the replay evidence beneath.
    let private ejectVerified : Copy =
        { Code           = "eject.verified"
          DocSection     = "§6"
          Statement      = fun _ -> View.Hero(View.Ok, "Verified. The reconstruction reproduces the frozen state from genesis to freeze.")
          Substantiation = fun _ -> [ View.Field("evidence", "replay from genesis = the frozen state", View.Neutral) ]
          Action         = fun _ -> None }

    /// `eject.unverified` — the freeze's self-verification failed
    /// (`THE_VOICE.md` §6 / §10): the reconstruction diverges from the frozen
    /// state, so the package is unverified. Honest without exception — the
    /// finding is stated plainly; no lever exists beyond investigation.
    let private ejectUnverified : Copy =
        { Code           = "eject.unverified"
          DocSection     = "§6"
          Statement      = fun _ -> View.Hero(View.Bad, "The reconstruction does not reproduce the frozen state. The package is unverified.")
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `migrate.inexpressible` — the schema emitter refused changes it cannot
    /// express as a single ALTER (`THE_VOICE.md` §10): the statement carries the
    /// count and states that the database is unchanged; each refusing change is
    /// demoted into the disclosure beneath, its code beside its cause.
    let private migrateInexpressible : Copy =
        { Code           = "migrate.inexpressible"
          DocSection     = "§10"
          Statement      =
            fun p ->
                match text "entryCount" p with
                | Some n -> View.Hero(View.Bad, sprintf "%s change(s) cannot be expressed as a single ALTER. The database is unchanged." (humane n))
                | None   -> View.Hero(View.Bad, "A change cannot be expressed as a single ALTER. The database is unchanged.")
          Substantiation =
            fun p ->
                match text "entries" p with
                | Some es ->
                    let lines =
                        es.Split '\n'
                        |> Array.toList
                        |> List.filter (fun l -> l.Trim() <> "")
                    [ View.Disclosure("the changes", View.Bad, lines |> List.map View.Note) ]
                | None -> []
          Action         = fun _ -> None }

    /// `migrate.stopped` — a migration stop outside the §5 gate vocabulary
    /// (`THE_VOICE.md` §10 real failure): the run stopped; the plain located
    /// cause (the `migrationStopDetail` projection) rides beneath.
    let private migrateStopped : Copy =
        { Code           = "migrate.stopped"
          DocSection     = "§10"
          Statement      = fun _ -> View.Hero(View.Bad, "The migration did not complete. The cause is shown below.")
          Substantiation =
            fun p ->
                match text "cause" p with
                | Some c -> [ View.Field("cause", c, View.Neutral) ]
                | None   -> []
          Action         = fun _ -> None }

    /// `eject.storeUnreadable` — the durable run history could not be loaded for
    /// the freeze (`THE_VOICE.md` §14 set-but-invalid / §10): the plain finding
    /// with the located cause beneath, never a raw store error on the lead.
    let private ejectStoreUnreadable : Copy =
        { Code           = "eject.storeUnreadable"
          DocSection     = "§14"
          Statement      = fun _ -> View.Hero(View.Bad, "The run history could not be read. Check the --store path and retry.")
          Substantiation =
            fun p ->
                match text "cause" p with
                | Some c -> [ View.Field("cause", c, View.Neutral) ]
                | None   -> []
          Action         = fun _ -> Some(View.Action "Check the --store path and retry.") }

    /// `canary.sourceMissing` — the round-trip verification's source DDL file is
    /// absent (`THE_VOICE.md` §14 set-but-invalid: concrete and located — the
    /// path rides beneath the plain statement).
    let private canarySourceMissing : Copy =
        { Code           = "canary.sourceMissing"
          DocSection     = "§14"
          Statement      = fun _ -> View.Hero(View.Bad, "The source DDL file was not found. Check the path and rerun.")
          Substantiation =
            fun p ->
                match text "path" p with
                | Some path -> [ View.Field("path", path, View.Neutral) ]
                | None      -> []
          Action         = fun _ -> Some(View.Action "Check the path and rerun.") }

    /// `docker.unavailable` — a face that needs an ephemeral SQL Server cannot
    /// reach the Docker daemon (`THE_VOICE.md` §14 required-and-missing): the
    /// requirement and how to provide it, stated without scolding. `purpose`
    /// names the face the daemon is required for.
    let private dockerUnavailable : Copy =
        { Code           = "docker.unavailable"
          DocSection     = "§14"
          Statement      =
            fun p ->
                View.Hero(View.Warn,
                    sprintf "A reachable Docker daemon is required for the %s. Start the daemon or set DOCKER_HOST, then retry."
                        (textOr "purpose" "run" p))
          Substantiation = fun _ -> []
          Action         = fun _ -> Some(View.Action "Start the Docker daemon or set DOCKER_HOST, then retry.") }

    // ------------------------------------------------------------------
    // §4 move verdicts — the transfer / migrate faces. These were inline
    // `printfn` prose in the run faces (the register-drift recon #11 names:
    // newer faces routed through the Voice, the older transfer/migrate ones
    // emitted English inline). The load-plan TABLE stays a face-side disclosure
    // (structured, per-kind); only the verdict LEAD and the drop refusal — the
    // operator's finding — are voiced here.
    // ------------------------------------------------------------------

    /// `transfer.previewPlan` — the §4 move DryRun verdict: what would move, with
    /// the no-write guarantee asserted. `rowCount` / `tableCount` ride the lead.
    let private transferPreviewPlan : Copy =
        { Code           = "transfer.previewPlan"
          DocSection     = "§3"
          Statement      =
            fun p ->
                match text "rowCount" p, text "tableCount" p with
                | Some rows, Some tables ->
                    View.Hero(View.Ok, sprintf "Preview — %s row(s) would move across %s table(s), in dependency order. No rows written." (humane rows) (humane tables))
                | _ -> View.Hero(View.Ok, "Preview — rows would move in dependency order. No rows written.")
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `transfer.applied` — the §4 move Execute verdict: rows moved, in dependency
    /// order (the engine's guarantee that a row never lands before its referents).
    let private transferApplied : Copy =
        { Code           = "transfer.applied"
          DocSection     = "§3"
          Statement      =
            fun p ->
                match text "rowCount" p, text "tableCount" p with
                | Some rows, Some tables ->
                    View.Hero(View.Ok, sprintf "%s row(s) moved across %s table(s), in dependency order." (humane rows) (humane tables))
                | _ -> View.Hero(View.Ok, "Rows moved in dependency order.")
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `transfer.rowsDropped` — the §10 fail-loud drop refusal: a successful write
    /// that dropped FK-orphan rows surfaces a distinct non-zero exit unless the
    /// operator declared the loss. ONE definition (the recon's "one violation of
    /// say-prose-once" — the literal was duplicated verbatim across the transfer
    /// and synthetic faces). `droppedCount` rides the lead.
    let private transferRowsDropped : Copy =
        { Code           = "transfer.rowsDropped"
          DocSection     = "§10"
          Statement      =
            fun p ->
                match text "droppedCount" p with
                | Some n -> View.Hero(View.Bad, sprintf "%s row(s) would be dropped — a relationship points to an unmatched record." (humane n))
                | None   -> View.Hero(View.Bad, "Rows would be dropped — a relationship points to an unmatched record.")
          Substantiation = fun _ -> []
          Action         = fun _ -> Some(View.Action "Pass --allow-drops to accept the loss, or resolve the records.") }

    /// `migrate.applied` — the §6 migrate execute verdict: the schema change ran
    /// and the round-trip read-back matched the model (`Ingest ∘ Project = id`).
    /// `detail` (statements applied / rows captured / record) is the proof beneath.
    let private migrateApplied : Copy =
        { Code           = "migrate.applied"
          DocSection     = "§6"
          Statement      = fun _ -> View.Hero(View.Ok, "Applied and verified. The deployed schema now matches the model.")
          Substantiation =
            fun p ->
                match text "detail" p with
                | Some d -> [ View.Field("evidence", d, View.Neutral) ]
                | None   -> []
          Action         = fun _ -> None }

    /// `migrate.verificationFailed` — the §6 migrate post-condition failure: the
    /// changes WERE applied but the read-back does not match the model, so no run
    /// was recorded. A verdict (the change landed) with a warning register (the
    /// proof did not close). `detail` names what diverged / what was skipped.
    let private migrateVerificationFailed : Copy =
        { Code           = "migrate.verificationFailed"
          DocSection     = "§6"
          Statement      = fun _ -> View.Hero(View.Warn, "The changes were applied, but the read-back does not match the model. No run was recorded.")
          Substantiation =
            fun p ->
                match text "detail" p with
                | Some d -> [ View.Field("detail", d, View.Neutral) ]
                | None   -> []
          Action         = fun _ -> None }

    // ------------------------------------------------------------------
    /// `watch.suggestedEdits` — the live board's config-recommendation teaser
    /// (#6 echo-the-fix): envelopes carrying a `suggestedConfig` payload tick
    /// this count as the run happens; the run's closing panel summarizes them,
    /// most-suggested first. Rewritten 2026-07-06 (the full-voice audit): the
    /// prior copy leaked "the verdict panel" (internal surface name) and "the
    /// lever" (figurative, §2.2) and never said the recommendations are
    /// optional (§14).
    let private watchSuggestedEdits : Copy =
        { Code           = "watch.suggestedEdits"
          DocSection     = "§13"
          Statement      =
            fun p ->
                match text "count" p with
                | Some n -> View.Note(sprintf "%s optional configuration recommendation(s) gathered so far. Each is advisory and needs no action to complete this run; a summary follows at the end." (humane n))
                | None   -> View.Note "Optional configuration recommendations gathered. Each is advisory and needs no action to complete this run; a summary follows at the end."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `plan.note` — a dispatch-prologue note (a plan note; the A7 inert-flag
    /// note). The engine authored the text at the plan layer; the Voice frames
    /// it on the note register so the prologue renders one way everywhere.
    let private planNote : Copy =
        { Code           = "plan.note"
          DocSection     = "§14"
          Statement      =
            fun p ->
                match text "text" p with
                | Some t -> View.Note(sprintf "Note — %s" t)
                | None   -> View.Note "Note."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `survey.advisory` — the G0c capability-survey advisory for a live
    /// (`--go`) flow: a connected environment cannot do what the flow asks.
    /// Warns, never gates (R6 — the run's own exit stands).
    let private surveyAdvisory : Copy =
        { Code           = "survey.advisory"
          DocSection     = "§14"
          Statement      =
            fun p ->
                match text "text" p with
                | Some t -> View.Note t
                | None   -> View.Note "A connected environment reported a capability gap."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `shell.previewFrame` — the operator shell's static open for a run that
    /// writes nothing (`THE_VOICE.md` §5's consequence-as-meaning, carried to
    /// the preview register): the frame says so up front, so a gated dry-run
    /// reads as a deliberate preview rather than a dead board.
    let private shellPreviewFrame : Copy =
        { Code           = "shell.previewFrame"
          DocSection     = "§5"
          Statement      =
            fun p ->
                match text "title" p with
                | Some t -> View.Note(sprintf "%s. Nothing will be written." t)
                | None   -> View.Note "A preview. Nothing will be written."
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `adapter.ossys.modelRead.noticeRollup` — a model read's divergence notices,
    /// condensed to one calm line (`THE_VOICE.md` §12 at-scale law: the surface is
    /// a constant size; only the numbers grow). The per-item detail is the run's
    /// notice artifact; the action points there — never a wall of lines.
    /// Rewritten 2026-07-06 (the full-voice audit): "model-reality notices" was
    /// an internal compound the operator could not parse (§2.1 boundary; rule
    /// 10/11), and the line never said no action is required (§14).
    let private modelReadNoticeRollup : Copy =
        { Code           = "adapter.ossys.modelRead.noticeRollup"
          DocSection     = "§12"
          Statement      =
            fun p ->
                let families =
                    [ "nullability", "nullability difference(s)"
                      "identity",    "identity-flag difference(s)"
                      "primaryKey",  "primary-key difference(s)"
                      "other",       "other difference(s)" ]
                    |> List.choose (fun (key, label) ->
                        text key p |> Option.map (fun n -> sprintf "%s %s" (humane n) label))
                let total = humane (textOr "total" "0" p)
                match families with
                | [] -> View.Note(sprintf "The deployed database's column shapes differ from the model's declarations in %s place(s). The model's declared values were kept and the run continues; no action is required." total)
                | fs -> View.Note(sprintf "The deployed database's column shapes differ from the model's declarations in %s place(s) — %s. The model's declared values were kept and the run continues; no action is required." total (String.concat ", " fs)) // LINT-ALLOW: terminal operator-facing copy at the Voice boundary; the family list is a free-text enumeration, String.concat is the irreducible primitive for this comma-joined notice narration
          Substantiation =
            fun p ->
                match text "samples" p with
                | Some s when s <> "" -> [ View.Disclosure("the first differences", View.Neutral, [ View.Note s ]) ]
                | _ -> []
          Action         =
            fun p ->
                text "artifactPath" p
                |> Option.filter (fun s -> s <> "")
                |> Option.map (fun path -> View.Action(sprintf "Review the full list of differences at %s." path)) }

    /// `fidelity.dataViolations` — the data-reality rollup (2026-07-06; the
    /// §12 at-scale law): ONE Warn envelope per run when the profiled source
    /// data contradicts the model's declared constraints. The statement names
    /// the finding, the per-axis breakdown, and the consequence; the action
    /// routes to the remediation script, where every finding carries a
    /// locating query and a commented repair.
    let private fidelityDataViolations : Copy =
        { Code           = "fidelity.dataViolations"
          DocSection     = "§12"
          Statement      =
            fun p ->
                let families =
                    [ "notNull",  "required column(s) with nulls"
                      "unique",   "unique column(s) with duplicates"
                      "orphans",  "relationship(s) with unmatched records"
                      "overflow", "column(s) with values past the declared length" ]
                    |> List.choose (fun (key, label) ->
                        text key p
                        |> Option.filter (fun n -> n <> "0")
                        |> Option.map (fun n -> sprintf "%s %s" (humane n) label))
                let total  = humane (textOr "total" "0" p)
                let tables = humane (textOr "entities" "0" p)
                match families with
                | [] ->
                    View.Note(sprintf "The source data contradicts the declared model in %s place(s) across %s table(s). A data load can fail until the rows are repaired." total tables)
                | fs ->
                    View.Note(sprintf "The source data contradicts the declared model in %s place(s) across %s table(s) — %s. A data load can fail until the rows are repaired." total tables (String.concat ", " fs)) // LINT-ALLOW: terminal operator-facing copy at the Voice boundary; the family list is a free-text enumeration, String.concat is the irreducible primitive for this comma-joined rollup narration
          Substantiation = fun _ -> []
          Action         =
            fun p ->
                text "remediationPath" p
                |> Option.filter (fun s -> s <> "")
                |> Option.map (fun path -> View.Action(sprintf "Review %s — each finding carries a locating query and a commented repair." path)) }

    /// `estate.unified` — the estate verdict, green (`check estate`;
    /// CHAPTER_ESTATE_OPEN.md Appendix A.4). The clauses name exactly what
    /// this run proved — the schema and data planes; the posture and fidelity
    /// clauses join the copy with their waves, so the verdict never claims a
    /// clause that does not yet run.
    let private estateUnified : Copy =
        { Code           = "estate.unified"
          DocSection     = "§3"
          Statement      =
            fun p ->
                View.Hero(View.Ok,
                    sprintf "The estate is one shape. %s environment(s) match the target schema and the data fits every declared constraint."
                        (humane (textOr "envs" "0" p)))
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `estate.diverged` — the estate verdict, amber: the per-lane counts
    /// lead (only the lanes that carry findings are named); the ruling queue
    /// is called first when one exists; the action routes to the machine
    /// record. Count-first, humane, constant-size at any estate scale (§12).
    let private estateDiverged : Copy =
        { Code           = "estate.diverged"
          DocSection     = "§3"
          Statement      =
            fun p ->
                let lanes =
                    [ "decide", "awaiting a ruling"
                      "repair", "on the repair queue"
                      "relax",  "under an interim relaxation"
                      "watch",  "under watch" ]
                    |> List.choose (fun (key, label) ->
                        text key p
                        |> Option.filter (fun n -> n <> "0")
                        |> Option.map (fun n -> sprintf "%s %s" (humane n) label))
                let total = humane (textOr "total" "0" p)
                let envs  = humane (textOr "envs" "0" p)
                let queueClause =
                    match text "decide" p with
                    | Some n when n <> "0" -> " The ruling queue is first below."
                    | _ -> ""
                match lanes with
                | [] ->
                    View.Hero(View.Warn,
                        sprintf "The estate is converging: %s finding(s) remain across %s environment(s).%s" total envs queueClause)
                | ls ->
                    View.Hero(View.Warn,
                        sprintf "The estate is converging: %s finding(s) remain across %s environment(s) — %s.%s" total envs (String.concat ", " ls) queueClause) // LINT-ALLOW: terminal operator-facing copy at the Voice boundary; the lane list is a free-text enumeration, String.concat is the irreducible primitive for this comma-joined verdict narration
          Substantiation = fun _ -> []
          Action         =
            fun p ->
                text "artifactPath" p
                |> Option.filter (fun s -> s <> "")
                |> Option.map (fun path -> View.Action(sprintf "Review %s — every finding with its lane, key, and evidence, machine-readable." path)) }

    /// `estate.envUnreadable` — §14: a named environment could not be read.
    /// The estate verdict needs every named environment (no partial estate —
    /// DECISIONS 2026-07-15); the cause leads, the lever follows.
    let private estateEnvUnreadable : Copy =
        { Code           = "estate.envUnreadable"
          DocSection     = "§14"
          Statement      =
            fun p ->
                View.Hero(View.Bad,
                    sprintf "%s could not be read: %s. The estate verdict needs every named environment."
                        (textOr "env" "an environment" p) (textOr "reason" "the connection did not open" p))
          Substantiation = fun _ -> []
          Action         = fun _ -> Some (View.Action "Restore the connection, or narrow readiness.confirm deliberately.") }

    /// `fidelity.rows.matched` — §6: the row-fidelity proof, green (`check
    /// data --rows`; T17, wave B2). The statement names what this run
    /// proved; the strict-mode clause rides the substantiation until the
    /// intervention ledger joins the copy at its wave (never a claim ahead
    /// of what runs).
    let private fidelityRowsMatched : Copy =
        { Code           = "fidelity.rows.matched"
          DocSection     = "§6"
          Statement      =
            fun p ->
                View.Hero(View.Ok,
                    sprintf "Every compared row is byte-identical across the physical-to-logical gap: %s row(s) across %s kind(s)."
                        (humane (textOr "rows" "0" p)) (humane (textOr "kinds" "0" p)))
          Substantiation =
            fun _ -> [ View.Note "No intervention ledger was supplied — this proof claims strict byte-identity." ]
          Action         =
            fun p ->
                text "artifactPath" p
                |> Option.filter (fun s -> s <> "")
                |> Option.map (fun path -> View.Action(sprintf "%s carries the full proof record, machine-readable." path)) }

    /// `fidelity.rows.diverged` — §6: differing rows exist and the first
    /// are named below by their keys; the totals stay exact at any scale.
    let private fidelityRowsDiverged : Copy =
        { Code           = "fidelity.rows.diverged"
          DocSection     = "§6"
          Statement      =
            fun p ->
                View.Hero(View.Bad,
                    sprintf "The rows differ across the gap: %s difference(s) over %s compared row(s) across %s kind(s). The first differing rows are named below, each by its key."
                        (humane (textOr "diffs" "0" p)) (humane (textOr "rows" "0" p)) (humane (textOr "kinds" "0" p)))
          Substantiation = fun _ -> []
          Action         =
            fun p ->
                text "artifactPath" p
                |> Option.filter (fun s -> s <> "")
                |> Option.map (fun path -> View.Action(sprintf "Review %s — every kind's totals and every named difference, machine-readable." path)) }

    /// The humane capture-age clause the three evidence notices share
    /// ("today" / "N day(s) ago") — mirrors the board masthead's wording so
    /// the notice and the masthead read as one fact.
    let private ageClause (age: string) : string =
        if age = "0" then "today" else sprintf "%s day(s) ago" (humane age)

    /// `estate.evidence.cached` — §5: the run's evidence basis, stated up
    /// front (RT-7 — pay-once evidence says so when it rides; the second
    /// run reuses evidence and the operator reads that it did).
    let private estateEvidenceCached : Copy =
        { Code           = "estate.evidence.cached"
          DocSection     = "§5"
          Statement      =
            fun p ->
                View.Note(
                    sprintf "Evidence for %s rides the store: captured %s, fingerprints clean across %s kind(s)."
                        (textOr "env" "an environment" p)
                        (ageClause (textOr "age" "0" p))
                        (humane (textOr "kinds" "0" p)))
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `estate.evidence.stale` — §5: fingerprints moved; the re-profile is
    /// the consequence, named as it happens (never a silent re-read).
    let private estateEvidenceStale : Copy =
        { Code           = "estate.evidence.stale"
          DocSection     = "§5"
          Statement      =
            fun p ->
                View.Note(
                    sprintf "For %s, %s kind(s) moved since capture — re-profiled this run; the store now carries the fresh evidence."
                        (textOr "env" "an environment" p)
                        (humane (textOr "moved" "0" p)))
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    /// `estate.evidence.offline` — §14: the operator chose the unprobed
    /// basis; every dependent verdict is advisory, said before the verdict.
    let private estateEvidenceOffline : Copy =
        { Code           = "estate.evidence.offline"
          DocSection     = "§14"
          Statement      =
            fun p ->
                View.Note(
                    sprintf "%s reads from offline evidence, captured %s and unprobed — every verdict standing on it is advisory."
                        (textOr "env" "an environment" p)
                        (ageClause (textOr "age" "0" p)))
          Substantiation = fun _ -> []
          Action         = fun _ -> None }

    // ------------------------------------------------------------------
    // The harvest — `all` gathers every declared copy into one catalog.
    // The `code ⇔ copy` totality test reads this (the sibling of the
    // registry's `registered ⇔ executed`), so the copy can't drift from
    // the events by construction.
    // ------------------------------------------------------------------

    /// Every declared copy, harvested. Organized by `THE_VOICE.md` section so the
    /// catalog stays isomorphic to the doc (decision 5).
    let all : Copy list =
        [ // §3 / §6 — verdicts & proofs
          canaryDiffEmpty
          canaryDivergence
          summaryRunComplete
          loadCompleted
          deployCompleted
          deploySsdtRejected
          canaryCdcSilent
          canaryCdcCaptured
          driftNone
          driftDiverged
          verifyDataMatched
          verifyDataDiverged
          ejectVerified
          ejectUnverified
          // §4 — move verdicts (transfer / migrate)
          transferPreviewPlan
          transferApplied
          transferRowsDropped
          migrateApplied
          migrateVerificationFailed
          // §13 — lifecycle / Watch (the spine + the per-stage stream)
          episodeRecorded
          ejectPackaged
          containerStarting
          deployBundleEmitted
          canaryDeployed
          configRunStart
          configConnectionResolved
          extractStarted
          extractCompleted
          profileStarted
          profileCompleted
          emitStarted
          emitCompleted
          preflightStarted
          deployStarted
          canaryStarted
          loadStarted
          storeStarted
          seedLoadStarted
          watchRunTitle
          watchRunDone
          watchStageHalted
          watchSuggestedEdits
          summaryStageCompleted
          // §5 — the operator shell's preview frame
          shellPreviewFrame
          // §14 — the dispatch prologue's notes + the G0c survey advisory
          planNote
          surveyAdvisory
          // §12 — the at-scale rollups (constant-size surfaces over growing counts)
          modelReadNoticeRollup
          fidelityDataViolations
          // §3 / §14 — the estate instrument's verdict pair + unreadable-env
          // finding (check estate; CHAPTER_ESTATE_OPEN.md Appendix A.4)
          estateUnified
          estateDiverged
          estateEnvUnreadable
          // §5 / §14 — the estate evidence-provenance notices (pay-once
          // evidence; DECISIONS 2026-07-15 entry 4)
          estateEvidenceCached
          estateEvidenceStale
          estateEvidenceOffline
          // §6 — the row-fidelity proof pair (check data --rows; T17, B2)
          fidelityRowsMatched
          fidelityRowsDiverged
          // §14 / §10 — config & errors
          configValidationFailed
          canarySourceMissing
          dockerUnavailable
          migrateInexpressible
          migrateStopped
          ejectStoreUnreadable ]

    /// Look a code's copy up. `None` when the code is not yet voiced — the
    /// totality test guarantees every in-scope LIVE code IS voiced, so a `None`
    /// in production is an unvoiced latent surface (IR grows under evidence).
    let lookup (code: string) : Copy option =
        all |> List.tryFind (fun c -> c.Code = code)

    /// Project a voiced copy onto a `Surface` (statement over substantiation,
    /// ending on the move) given the event's payload.
    let toSurface (copy: Copy) (payload: Payload) : Surface.Surface =
        { Statement      = copy.Statement payload
          Substantiation = copy.Substantiation payload
          Action         = copy.Action payload }

    /// Project a code's copy onto a `Surface`, filled from the payload; `None`
    /// when the code is unvoiced.
    let surfaceOf (code: string) (payload: Payload) : Surface.Surface option =
        lookup code |> Option.map (fun c -> toSurface c payload)

    /// The fallback surface for an UNVOICED code (a typo'd or newly-added face
    /// code with no catalog entry) — NM-47: an unvoiced verdict must never render
    /// nothing (an invisible operator verdict). Rather than swallow the code, it is
    /// surfaced as a plain located narration: the raw code leads (a warn `Hero`),
    /// the payload rides beneath as fields, a note names the missing copy. The
    /// totality assertion (`renderVoicedCodesAreAllVoiced`) keeps this arm
    /// unreached in production; it exists so a latent code is loud, not silent.
    let fallbackSurface (code: string) (payload: Payload) : Surface.Surface =
        let payloadFields =
            payload
            |> Map.toList
            |> List.sortBy fst
            |> List.map (fun (k, v) ->
                let rendered = if isNull v then "—" else string v
                View.Field(k, rendered, View.Neutral))
        { Statement      = View.Hero(View.Warn, code)
          Substantiation =
            View.Note("this code has no operator copy yet — an unvoiced verdict; report it")
            :: payloadFields
          Action         = None }

    /// The voiced verdict line (a `Hero`) for a code, or `None` when the code is
    /// unvoiced or its statement is not a verdict (a lifecycle `Note`). The
    /// verdict panel reads this to lead with the finding (`THE_VOICE.md` §3).
    let verdict (code: string) (payload: Payload) : (View.Status * string) option =
        match lookup code with
        | Some c ->
            match c.Statement payload with
            | View.Hero(st, t) -> Some(st, t)
            | _                -> None
        | None -> None

    // ------------------------------------------------------------------
    // The error frame — §10 / §14. The `pipeline.config.*` (and connection /
    // permission) error code space is open-ended (sprintf-built messages), so
    // it is voiced by a routing frame keyed on the code's top-prefix rather
    // than one catalog entry per sprintf subcode. The located detail (the
    // `ValidationError.Message`) is the substantiation; the statement is the
    // calm, register-correct frame. The slice-4 lift wires `printErrors`
    // through `errorSurface`.
    // ------------------------------------------------------------------

    /// The finite set of operator-facing error FRAMES (§10 / §14). The error-code
    /// space is open-ended (sprintf-built subcodes), so a single catalog entry per
    /// subcode is impossible; instead the code's top-prefix routes onto this CLOSED
    /// DU, and `frameCopy` is a TOTAL projection over it — the same closed-DU
    /// discipline `gateStatement` (over `Preflight.GateLabel`) and
    /// `migrationStopDetail` (over `MigrationError`) already use in this file. The
    /// `frame ⇔ copy` totality is then compiler-enforced (exhaustive match) and
    /// test-covered (`VoiceTotalityTests`), closing the gap where the old 12-branch
    /// `if/elif` `errorFrame` carried its copy inline, unreachable by any totality.
    type ErrorFrame =
        | ConfigProblem
        | IntentGate
        | ConnectionMalformed
        | ConnectionUnresolved
        | TimelineNameInvalid
        | ReconcileArgumentInvalid
        | ModelLoadFailed
        | SchemaUnreadable
        | TargetUnreachable
        | PermissionDenied
        | GenericStop

    /// Route an error code's top-prefix onto its `ErrorFrame`. Ordering matters
    /// HERE and only here, for the two genuine subset relationships (documented
    /// inline): the specific `transfer.connection.{spec,ref}` prefixes precede the
    /// generic `connection` substring, and the permission substrings sit last. The
    /// routing is the one place a new prefix is wired; the COPY is total elsewhere.
    let classifyError (code: string) : ErrorFrame =
        if code.StartsWith "pipeline.config." then ConfigProblem
        elif code = "gate.intent" then IntentGate
        // `transfer.connection.{spec,ref}` MUST precede the generic `connection`
        // substring below — a malformed/unresolved reference is an argument
        // finding, not a reachability claim (the prior routing once overstated it).
        elif code.StartsWith "transfer.connection.spec" then ConnectionMalformed
        elif code.StartsWith "transfer.connection.ref" then ConnectionUnresolved
        elif code.StartsWith "timeline.name" then TimelineNameInvalid
        elif code.StartsWith "transfer.reconcile." || code.StartsWith "transfer.userMap." then ReconcileArgumentInvalid
        elif code.StartsWith "adapter.osm." || code.StartsWith "model." then ModelLoadFailed
        elif code.StartsWith "readside." then SchemaUnreadable
        elif code.Contains "connection" then TargetUnreachable
        elif code.Contains "insufficientGrant" || code.Contains "grantProbe" || code.Contains "permission" then PermissionDenied
        else GenericStop

    /// The §10 / §14 copy for an `ErrorFrame` — the statement Hero + the next-move
    /// action. TOTAL over the closed DU (the compiler forbids a frame without
    /// copy). Verbatim the copy the old inline `errorFrame` carried.
    let frameCopy (frame: ErrorFrame) : View.View * View.View option =
        match frame with
        | ConfigProblem ->
            View.Hero(View.Bad, "The configuration has a problem. Correct it and rerun."),
            Some(View.Action "Correct the configuration and rerun.")
        | IntentGate ->
            // §5/§7 two-gate consent model: `--go` states intent, the
            // PROJECTION_ALLOW_EXECUTE arming is the second gate. A CLI consent
            // concern, not an engine pre-flight (DECISIONS 2026-06-08 — the flat
            // gate.intent code, not a Preflight.GateLabel variant).
            View.Hero(View.Warn, "A live write requires PROJECTION_ALLOW_EXECUTE=1 in the environment."),
            Some(View.Action "Set PROJECTION_ALLOW_EXECUTE=1, then re-run.")
        | ConnectionMalformed ->
            // §14 set-but-invalid: the reference's *shape* is wrong — an argument
            // finding, never a reachability claim.
            View.Hero(View.Bad, "The connection reference is malformed — the expected shape is env:NAME or file:PATH. Correct it and rerun."),
            Some(View.Action "Correct the connection reference and rerun.")
        | ConnectionUnresolved ->
            // §14 required-and-missing: the reference is well-formed but the
            // secret it points to (the env var / the file) is absent or empty.
            View.Hero(View.Bad, "The connection reference does not resolve to a connection string. Provide the secret it points to, then retry."),
            Some(View.Action "Provide the connection secret, then retry.")
        | TimelineNameInvalid ->
            // §14 set-but-invalid: the --env label cannot name a timeline.
            View.Hero(View.Bad, "The environment label cannot name a timeline. Correct the --env label and rerun."),
            Some(View.Action "Correct the --env label and rerun.")
        | ReconcileArgumentInvalid ->
            // §14 set-but-invalid — a reconciliation argument the run cannot
            // use; the located cause (the spec / the file) is the substantiation.
            View.Hero(View.Bad, "A reconciliation argument is invalid. Correct the --reconcile / --user-map value and rerun."),
            Some(View.Action "Correct the --reconcile / --user-map value and rerun.")
        | ModelLoadFailed ->
            // §10 invalid input — the model could not be loaded; the located
            // detail (path / line / field) is the substantiation beneath.
            View.Hero(View.Bad, "The model failed to load. Correct it and rerun."),
            Some(View.Action "Correct the model and rerun.")
        | SchemaUnreadable ->
            // §10 — the deployed schema could not be read (the same finding the
            // §5 SchemaReadFailed gate states).
            View.Hero(View.Bad, "The deployed schema could not be read. Check the connection and retry."),
            Some(View.Action "Check the connection and retry.")
        | TargetUnreachable ->
            View.Hero(View.Warn, "The target is unreachable. Check the connection and retry."),
            Some(View.Action "Check the connection and retry.")
        | PermissionDenied ->
            View.Hero(View.Warn, "A required permission is denied. Grant it, then retry."),
            Some(View.Action "Grant the permission, then retry.")
        | GenericStop ->
            View.Hero(View.Bad, "Stopped before any change was applied. The cause is shown below."),
            None

    /// The operator-facing frame for an error code (`THE_VOICE.md` §10 / §14):
    /// classify the code's prefix onto its `ErrorFrame`, then voice it. One
    /// dispatcher now — routing (`classifyError`) split from copy (`frameCopy`,
    /// total over the closed DU), each independently testable.
    let errorFrame (code: string) : View.View * View.View option =
        frameCopy (classifyError code)

    /// Voice a `ValidationError` as a §10/§14 `Surface` — the located cause (the
    /// message) is the substantiation; the code rides beneath, never on the
    /// statement line (rule 3).
    let errorSurface (error: ValidationError) : Surface.Surface =
        let statement, action = errorFrame error.Code
        { Statement      = statement
          Substantiation =
            [ View.Field("problem", error.Message, View.Neutral)
              View.Field("code", error.Code, View.Neutral) ]
          Action         = action }

    /// Voice a `ValidationError list` as one §10/§14 `Surface` — the frame is
    /// chosen by the first (dominant) error; every located cause lists beneath
    /// (rule 3 — the statement is legible, the causes are the substantiation). An
    /// empty list cannot reach here (`Result.failure` forbids it); it degrades to
    /// the generic §10 frame rather than throwing.
    let errorsSurface (errors: ValidationError list) : Surface.Surface =
        match errors with
        | [] ->
            let statement, action = errorFrame ""
            { Statement = statement; Substantiation = []; Action = action }
        | primary :: _ ->
            let statement, action = errorFrame primary.Code
            { Statement      = statement
              Substantiation =
                errors
                |> List.collect (fun e ->
                    [ View.Field("problem", e.Message, View.Neutral)
                      View.Field("code", e.Code, View.Neutral) ])
              Action         = action }

    // ------------------------------------------------------------------
    // The gates — §5 (consent, in plain words). Mechanism 1: a typed
    // projection keyed by the closed `Preflight.GateLabel` DU (the gate⇔copy
    // totality is the closed-DU analog of code⇔copy). The copy law: state the
    // consequence as meaning, name the one lever, hand over a plain active
    // imperative — never a wall of error.
    // ------------------------------------------------------------------

    /// The §5 gate copy for a `Preflight.GateLabel` — the statement (status +
    /// consequence-as-meaning) and the next move (a plain active imperative).
    /// Total over the closed DU; the verb is plain (`Approve · Grant · Map ·
    /// Check · Correct · Allow`), never `Override` / `Declare loss` / `Proceed`.
    let gateStatement (label: Preflight.GateLabel) : View.Status * string * View.View option =
        match label with
        | Preflight.UndeclaredDestructiveChange ->
            View.Bad, "This change drops a database object. Approve the removal, or halt.",
            Some(View.Action "Approve the removal: --allow-drops (accept all) or --declare-drop <token> for each, or halt.")
        | Preflight.MidWriteNotProtected ->
            View.Bad, "The write path is neither atomic nor resumable, so a mid-write failure would leave a half-populated target. The run stopped before any write. Wrap the write path in a transaction, make it resumable, or halt.",
            Some(View.Action "Wrap the write path in a transaction, make it resumable, or halt.")
        | Preflight.DataViolatesTightening ->
            View.Bad, "The existing data violates the tightening. Correct the data, relax the constraint, or halt.",
            Some(View.Action "Correct the data, relax the constraint, or halt.")
        | Preflight.SchemaReadFailed ->
            View.Bad, "The deployed schema could not be read. Check the connection and retry.",
            Some(View.Action "Check the connection and retry.")
        | Preflight.ConnectionUnavailable ->
            View.Warn, "The target is unreachable. Check the connection and retry.",
            Some(View.Action "Check the connection and retry.")
        | Preflight.InsufficientGrant ->
            View.Warn, "A required permission is denied. Grant it, then retry.",
            Some(View.Action "Grant the permission, then retry.")
        | Preflight.ReconciliationMismatch ->
            View.Warn, "The source and the sink do not reconcile. Resolve the mismatch, then retry.",
            Some(View.Action "Resolve the mismatch, then retry.")
        | Preflight.UnmappedIdentities ->
            View.Warn, "Some identities are unmapped. Map them, assign the system user, or halt.",
            Some(View.Action "Map the remaining identities, assign the system user, or halt.")
        | Preflight.CdcTrackedSink ->
            View.Warn, "The sink is CDC-tracked. Allow the capture, or halt.",
            Some(View.Action "Allow CDC capture, or halt.")
        | Preflight.ShapeDivergence ->
            View.Bad, "The source and sink schemas are not one shape over the transferred set. Align the models, narrow the subset, or halt.",
            Some(View.Action "Align the models (deploy the same version to both), narrow the table subset, or halt.")
        | Preflight.SubsetFkEscape ->
            View.Warn, "Some relationships point outside the chosen tables — their rows would keep the source environment's references. Reconcile each against the target's rows, or widen the subset.",
            Some(View.Action "Reconcile each named table against the target's rows (Module.Entity:Column), or widen the subset.")
        | Preflight.ConsentWithheld ->
            View.Warn, "The run would perform a destructive or creative write the flow has not consented to. Read the named acts and bless each one, or halt.",
            Some(View.Action "Read each named act on the go board's consent line, bless it (check go <flow> --review, or the signoff entry the board prints), then authorize the run again.")
        | Preflight.UnclassifiedRefusal ->
            View.Bad, "Stopped before any change was applied. The cause is shown below.", None

    /// Voice a `Preflight.GateRefusal` as a §5 `Surface` — the statement names the
    /// consequence + the lever; the substantiation (the gate axis, the located
    /// detail, the distinct exit code) is one level beneath; the action is the
    /// plain imperative. The structural refusal that otherwise collapses to a flat
    /// error string.
    let gateSurface (_command: string) (refusal: Preflight.GateRefusal) : Surface.Surface =
        // Lead with the consequence as meaning (§5), not "<command> refused —" —
        // a gate *stops to ask*, never a system-shout. The command context is in
        // the surrounding CLI narration; the gate axis is in the substantiation.
        let status, statement, action = gateStatement refusal.Label
        { Statement      = View.Hero(status, statement)
          Substantiation =
            [ View.Disclosure(
                "Details", View.Neutral,
                [ View.Field("gate", Preflight.labelText refusal.Label, status)
                  View.Field("detail", refusal.Error.Message, View.Neutral)
                  View.Field("exit", string refusal.ExitCode, View.Neutral) ]) ]
          Action         = action }

    // ------------------------------------------------------------------
    // The migrate stop channel — §10 (mechanism 1: a typed projection over
    // the closed `MigrationError` DU, the run-stop sibling of the §5 gate
    // projection). The face routes the gate-shaped arms (violations /
    // CDC-tracked / tightening) through `gateSurface`; the inexpressible
    // refusal speaks through `migrate.inexpressible`; every remaining arm
    // states its plain located cause through `migrate.stopped`.
    // ------------------------------------------------------------------

    /// The plain located cause for a `MigrationError` (`THE_VOICE.md` §10) —
    /// the finding in operator words, never a raw DU dump. Total over the
    /// closed DU by exhaustive match. (Moved from `RunFaces.
    /// migrationErrorDetail`, 2026-06-12 register migration — the catalog owns
    /// the copy; the face passes the projection as the `migrate.stopped`
    /// payload's located cause.)
    let migrationStopDetail (e: MigrationError) : string =
        match e with
        | DiffFailed _              -> "the changes could not be computed"
        | RefusedByViolations v     -> sprintf "%d removal(s) are not yet approved" (List.length v)
        | RefusedBySchemaErrors es  -> sprintf "%d change(s) cannot be expressed as a single ALTER" (List.length es)
        | EmitFailed _              -> "the changes could not be built"
        | SchemaReadFailed _        -> "the deployed schema could not be read"
        | ExecutionFailed msg       -> sprintf "the migration could not be applied — %s" msg
        | RefusedByTightening msg   -> sprintf "a column tightening would fail against existing data — %s" msg
        | VerificationFailed _      -> "the round-trip did not match the model"
        | DataTransferFailed _      -> "the data load did not complete"
        | RefusedByCdc t            -> sprintf "the schema change would run against a CDC-tracked database (%d table(s))" (List.length t)
        | RefusedByCdcUnverifiable msg -> sprintf "the CDC state could not be verified, so the schema change did not run — %s" msg
        | StoreReadFailed msg       -> sprintf "the run history could not be read — %s" msg
        | ExecutionRolledBack (msg, n) -> sprintf "the migration could not be applied and was rolled back to its original state (%d rename(s) reverted) — %s" n msg
        | PartialWriteUnrecovered (msg, _) -> sprintf "the migration failed part-way and could not be fully rolled back, so some changes remain — %s" msg
