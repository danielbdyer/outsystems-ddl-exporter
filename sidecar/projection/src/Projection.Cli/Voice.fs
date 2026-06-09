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
        | "pipeline" -> "The pipeline"
        | "extract"  -> "Model read"
        | "profile"  -> "Data check"
        | "emit"     -> "Change build"
        | "deploy"   -> "Deploy"
        | "canary"   -> "Round-trip verification"
        | other      -> other

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

    // §13 — lifecycle & the live run (Watch). Stage names are what they do for the
    //       operator, never the engine verb. The gerund names a live activity in
    //       progress (rule 12 exception); a completed stage is resultative.

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
          // §13 — lifecycle / Watch (the spine + the per-stage stream)
          configRunStart
          configConnectionResolved
          extractStarted
          extractCompleted
          profileStarted
          profileCompleted
          emitStarted
          emitCompleted
          deployStarted
          canaryStarted
          summaryStageCompleted
          // §14 / §10 — config & errors
          configValidationFailed ]

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

    /// The operator-facing frame for an error code's routing prefix
    /// (`THE_VOICE.md` §10 / §14). Returns the statement Hero + the next-move
    /// action. The routing is exhaustive over the known prefixes; an unmatched
    /// code falls through to the generic §10 frame (never a stack trace, never a
    /// system-shout). Public so the totality test asserts the routing is total.
    let errorFrame (code: string) : View.View * View.View option =
        if code.StartsWith "pipeline.config." then
            View.Hero(View.Bad, "The configuration has a problem. Correct it and rerun."),
            Some(View.Action "Correct the configuration and rerun.")
        elif code = "gate.intent" then
            // §5/§7 two-gate consent model: `--go` states intent, the
            // PROJECTION_ALLOW_EXECUTE arming is the second gate. A CLI consent
            // concern, not an engine pre-flight (DECISIONS 2026-06-08 — the flat
            // gate.intent code, not a Preflight.GateLabel variant).
            View.Hero(View.Warn, "A live write requires PROJECTION_ALLOW_EXECUTE=1 in the environment."),
            Some(View.Action "Set PROJECTION_ALLOW_EXECUTE=1, then re-run.")
        elif code.Contains "connection" then
            View.Hero(View.Warn, "The target is unreachable. Check the connection and retry."),
            Some(View.Action "Check the connection and retry.")
        elif code.Contains "insufficientGrant" || code.Contains "grantProbe" || code.Contains "permission" then
            View.Hero(View.Warn, "A required permission is denied. Grant it, then retry."),
            Some(View.Action "Grant the permission, then retry.")
        else
            View.Hero(View.Bad, "Stopped before any change was applied. The cause is shown below."),
            None

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
