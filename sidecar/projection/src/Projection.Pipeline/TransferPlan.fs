namespace Projection.Pipeline

// LINT-ALLOW-FILE: the transfer PLAN composes operator-facing decision prose (the
//   guided-wizard "what / alternatives / why / config edit") at a terminal reporting
//   boundary, exactly as `GoBoard` composes the readiness verdict. THE_VOICE register:
//   stative, agentless, evidence beneath, the config edit named. The model is pure —
//   the face supplies the flow's current choices; I/O lives one layer up.

/// THE TRANSFER PLAN (2026-07-08, the guided-wizard program): the DECLARATIVE
/// counterpart to the go board. Where `check go` answers "is this flow executable
/// NOW?" (a verdict + the one next move), the plan answers "what path do I want,
/// and WHY?" — for each transfer decision axis it names the CURRENT choice, the
/// ALTERNATIVES, the tradeoff each carries, and the exact `projection.json` edit
/// that selects it. It surfaces the transfer's strategy space (already expressible
/// in config, A44) as one legible menu, so the operator chooses with the reasons
/// in hand rather than deriving them.
///
/// The model is pure and total (a closed record tree), so the choices + their
/// reasons are testable against THE_VOICE and the machine lens (`toJsonString`)
/// stays a stable `--format json` twin, exactly as `GoBoard.toJsonString`.
[<RequireQualifiedAccess>]
module TransferPlan =

    /// One selectable branch of a decision — its diagnostic `Code`, the
    /// operator-facing `Label`, the `Why` (the tradeoff, stative), the exact
    /// `ConfigEdit` that selects it (empty when the branch is engine-derived, not
    /// operator-set), and whether it is the flow's CURRENT choice.
    type PlanOption =
        { Code       : string
          Label      : string
          Why        : string
          ConfigEdit : string
          Chosen     : bool }

    /// One decision axis — its `Axis` name, the `Current` choice's label, the
    /// `Rationale` (why this axis exists), and the closed set of `Options`.
    type PlanDecision =
        { Axis      : string
          Current   : string
          Rationale : string
          Options   : PlanOption list }

    /// The whole guided plan for one flow.
    type Plan =
        { Flow      : string
          From      : string
          To        : string
          Decisions : PlanDecision list }

    /// The flow's CURRENT settings, as the face resolves them from the `Flow`
    /// config + the resolved `MovementSpec` — the pure builder needs no DB, so the
    /// plan (the strategy explanation) is testable without a live sink.
    type Current =
        { Strategy        : string       // "merge" | "replace" | "fresh"
          Reconciles      : bool         // any reconcile rule declared
          HasSubset       : bool         // a `tables` subset is declared
          SupportingScope : int          // count of supporting-scope entries
          Streaming       : bool
          Staging         : string }      // "auto" | "inline" | "temp"

    let private opt code label why edit chosen : PlanOption =
        { Code = code; Label = label; Why = why; ConfigEdit = edit; Chosen = chosen }

    // -- the axes ------------------------------------------------------------
    // Each builder returns one `PlanDecision`. The WHY prose is authored here (the
    // reporting-boundary exception), stative and evidence-grounded, and pinned by
    // the THE_VOICE banned-word test.

    let private writeStrategy (c: Current) : PlanDecision =
        let opts =
            [ opt "strategy.merge" "merge — upsert-only"
                "Matched rows update in place and new rows insert; no target row is ever deleted. CDC-minimal — an idempotent re-run is byte-silent. The safe default when the sink may hold rows this subset will not re-insert."
                "\"strategy\": \"merge\"" (c.Strategy = "merge")
              opt "strategy.replace" "replace — wipe-and-load"
                "The transferred set is deleted child-first, then reloaded — the sink converges exactly to the source subset, so a target row absent from the source is removed. Costs twice the row count in CDC capture."
                "\"strategy\": \"replace\"" (c.Strategy = "replace")
              opt "strategy.fresh" "fresh — genesis"
                "An empty baseline is assumed: every row inserts, nothing is matched, no wipe runs. For a first load into a target known to be empty."
                "\"strategy\": \"fresh\"" (c.Strategy = "fresh") ]
        { Axis = "write strategy"
          Current = c.Strategy
          Rationale = "How the source rows land against the rows already on the sink — the CDC cost and whether target-only rows survive turn on this."
          Options = opts }

    let private identity (c: Current) : PlanDecision =
        // Without a reconcile rule the default is archetype-derived (the sink's
        // grant decides preserve-vs-mint), so neither is marked chosen — the
        // rationale names that the grant sets it.
        let current = if c.Reconciles then "re-keyed by rule" else "grant-derived (preserve or sink-mint)"
        let opts =
            [ opt "identity.reconcile" "re-key by rule"
                "Source rows match existing target rows by a business key; the target's own surrogate wins, so no key collides and a foreign key resolves against data already present. The Dev→UAT user re-key shape."
                "\"reconcile\": [\"Module.Entity:Column\"]" c.Reconciles
              opt "identity.preserve" "preserve from source"
                "Source surrogate keys are written directly (IDENTITY_INSERT). Faithful and simplest — the full-rights sink default."
                "" false
              opt "identity.sinkMint" "assign by the sink"
                "The sink mints new keys and a client journal records the source→sink map. The managed-DML default where IDENTITY_INSERT is forbidden."
                "" false ]
        { Axis = "identity"
          Current = current
          Rationale = "How a source row's identity is carried to the sink — matched to an existing row, preserved verbatim, or minted fresh. The sink's grant sets the default; a reconcile rule overrides it."
          Options = opts }

    let private scope (c: Current) : PlanDecision =
        let current = if c.HasSubset then "declared subset" else "whole estate"
        let opts =
            [ opt "scope.whole" "whole estate"
                "No subset — every modeled table transfers. No escaping-reference decision to make; the heaviest move."
                "" (not c.HasSubset)
              opt "scope.subset" "declared subset"
                "Only the listed tables transfer. A foreign key that escapes the subset must be reconciled to the target's own rows or the referenced table added — the go board names each escape."
                "\"tables\": [\"Customer\", \"Order\"]" c.HasSubset
              opt "scope.supporting" "supporting scope"
                (sprintf "The non-payload tables the subset needs, declared with business intent (existing-reference / owned-child / …). %d declared. Each is verified against the relationship graph." c.SupportingScope)
                "\"supportingScope\": [ { \"relationship\": \"existing-reference\", \"table\": \"…\", \"key\": \"…\" } ]" (c.SupportingScope > 0) ]
        { Axis = "scope"
          Current = current
          Rationale = "How much of the estate moves — the whole model, or a declared subset with its supporting rows. A subset is lighter but must account for every relationship that leaves it."
          Options = opts }

    let private realization (c: Current) : PlanDecision =
        let current = if c.Streaming then "streaming" else "materialized"
        let opts =
            [ opt "realization.streaming" "streaming"
                "Rows stream through a client journal — it dominates on throughput and memory at estate scale. Admissible only for the whole-estate, non-resumable, merge combination; an explicit request on any other combination refuses by name."
                "\"streaming\": true, \"journal\": \"lifecycle/reverse.ndjson\"" c.Streaming
              opt "realization.materialized" "materialized"
                "Rows stage through the engine — it carries the combinations streaming does not (a declared subset, a resumable checkpoint, a replace-wipe). The engine selects it automatically when streaming is inadmissible."
                "" (not c.Streaming) ]
        { Axis = "realization"
          Current = current
          Rationale = "How the rows physically move — streamed (fastest, whole-estate merge only) or materialized (carries the subset / resumable / replace combinations). The engine chooses; an explicit override that the request forbids refuses, never silently downgrades."
          Options = opts }

    let private staging (c: Current) : PlanDecision =
        let opts =
            [ opt "staging.auto" "auto"
                "A kind above the row threshold stages through a #temp table (no VALUES ceiling); below it, inline. Portable wherever the seed path runs — the default."
                "\"dataStaging\": { \"mode\": \"auto\" }" (c.Staging = "auto")
              opt "staging.inline" "inline"
                "Never stage — accepts the ~30k-row inline ceiling in exchange for needing no #temp rights. The locked-down escape hatch for a managed sink that forbids even baseline temp-table creation."
                "\"dataStaging\": { \"mode\": \"inline\" }" (c.Staging = "inline")
              opt "staging.temp" "temp table"
                "Always stage through a #temp table, at any row count. For a uniform staged path regardless of size."
                "\"dataStaging\": { \"mode\": \"tempTable\" }" (c.Staging = "temp") ]
        { Axis = "staging"
          Current = c.Staging
          Rationale = "How large-kind loads write — inline VALUES (a plan-complexity ceiling near 30k rows) or staged through a #temp table (no ceiling, needs baseline temp rights)."
          Options = opts }

    /// Build the guided plan for a flow from its current choices. Pure; the axes
    /// are always the same closed set, so a new strategy is a new option arm, not
    /// new plumbing.
    let ofCurrent (flow: string) (from: string) (to': string) (c: Current) : Plan =
        { Flow = flow
          From = from
          To = to'
          Decisions = [ writeStrategy c; identity c; scope c; realization c; staging c ] }

    /// Re-mark the write-strategy decision to `word` — the interactive pick
    /// reflected, every other decision untouched — so the face can re-render the
    /// plan with the new choice without re-reading config.
    let reselectStrategy (word: string) (p: Plan) : Plan =
        let remark (d: PlanDecision) : PlanDecision =
            if d.Axis <> "write strategy" then d
            else
                { d with
                    Current = word
                    Options = d.Options |> List.map (fun o -> { o with Chosen = (o.Code = "strategy." + word) }) }
        { p with Decisions = p.Decisions |> List.map remark }

    /// The machine-readable projection (`--format json`) — the CI-consumable twin,
    /// mirroring `GoBoard.toJsonString`. Typed writer, never string concatenation.
    let toJsonString (p: Plan) : string =
        use ms = new System.IO.MemoryStream()
        use w = new System.Text.Json.Utf8JsonWriter(ms, System.Text.Json.JsonWriterOptions(Indented = true))
        w.WriteStartObject()
        w.WriteString("flow", p.Flow)
        w.WriteString("from", p.From)
        w.WriteString("to", p.To)
        w.WriteStartArray "decisions"
        for d in p.Decisions do
            w.WriteStartObject()
            w.WriteString("axis", d.Axis)
            w.WriteString("current", d.Current)
            w.WriteString("rationale", d.Rationale)
            w.WriteStartArray "options"
            for o in d.Options do
                w.WriteStartObject()
                w.WriteString("code", o.Code)
                w.WriteString("label", o.Label)
                w.WriteString("why", o.Why)
                w.WriteString("configEdit", o.ConfigEdit)
                w.WriteBoolean("chosen", o.Chosen)
                w.WriteEndObject()
            w.WriteEndArray()
            w.WriteEndObject()
        w.WriteEndArray()
        w.WriteEndObject()
        w.Flush()
        System.Text.Encoding.UTF8.GetString(ms.ToArray())
