namespace Projection.Pipeline

// LINT-ALLOW-FILE: the rolled-up text renderer + the readiness.json codec compose
//   operator-facing report prose (THE_VOICE twelve-rule register) and structured
//   JSON at a terminal reporting boundary. The aggregation core is pure and
//   carries no I/O — the operand resolution (the OSSYS reads + profiling) lives
//   one layer up (the CLI run face), exactly as `Compare.fs` splits.

open System.Text.Json.Nodes
open Projection.Core

/// `projection check shape` — the espace-safe cross-environment readiness gate
/// (`CROSS_ENVIRONMENT_READINESS.md`). Given an AGREED shape (one environment's
/// OSSYS-read catalog) and N environments to CONFIRM, it answers one go/no-go
/// question per environment and rolls them into an estate verdict:
///
///   - **Schema equivalence** — `CatalogDiff` of the env against the agreed
///     shape. Because both are OSSYS-read (native GUID identity) and
///     `CatalogDiff` is physical-agnostic, a same-model env diffs to ZERO
///     regardless of its espace physical names (the espace-invariance law,
///     AXIOMS A1-corollary). A non-zero delta is a REAL logical divergence and
///     BLOCKS — the env is not the agreed shape.
///   - **Data dealbreakers** — the env's data profiled against the agreed
///     schema's constraints (`ModelFidelity`, via `Compare`): NULLs into
///     NOT-NULL, duplicates into UNIQUE, orphaned FKs, width/type overflow. Any
///     ⇒ PAUSED.
///
/// Read-only / advisory — a cutover-readiness gate, not a move. Pure over
/// resolved operands; mirrors `Compare`'s pure-core / I/O-one-layer-up split.
[<RequireQualifiedAccess>]
module Readiness =

    /// The per-environment go/no-go.
    [<RequireQualifiedAccess>]
    type Verdict =
        /// Schema matches the agreed shape; no data dealbreaker.
        | Ready
        /// Schema matches; data dealbreakers block the move.
        | Paused
        /// Schema diverges from the agreed shape (or could not be compared) —
        /// the env is not the shape the migration assumes.
        | Blocked

    /// One confirmed environment's readiness — its `Compare` report against the
    /// agreed shape plus the derived verdict.
    type EnvReadiness =
        { Env     : string
          Verdict : Verdict
          Report  : Compare.CompareReport }

    /// The assembled estate readiness — the agreed shape's label and the
    /// per-environment readiness, in `confirm` order.
    type ReadinessReport =
        { AgreedLabel : string
          Envs        : EnvReadiness list }

    /// The per-env verdict. Readiness is STRICTER than `Compare.isCompatible`:
    /// a non-zero schema delta is a BLOCKER here (the env is supposed to already
    /// BE the agreed shape — that is the whole point of the gate), whereas
    /// `Compare` treats a schema delta as expected downstream work.
    let verdictOf (r: Compare.CompareReport) : Verdict =
        match r.SchemaDelta with
        | None -> Verdict.Blocked
        | Some _ ->
            if Compare.schemaNorm r > 0 then Verdict.Blocked
            elif not (List.isEmpty r.DataDealbreakers) then Verdict.Paused
            else Verdict.Ready

    /// Normalize a catalog to its espace-INVARIANT logical shape for the
    /// cross-environment comparison. `CatalogDiff` already ignores physical
    /// table/column NAMES (the espace-invariance law, AXIOMS A1-corollary) — but
    /// it DOES compare physical-REALIZATION artifacts that embed those names: the
    /// default-constraint name (`Attribute.DefaultName`), triggers, and
    /// table-level column checks. OutSystems generates those names from the
    /// physical table name, so two espace cells of ONE model carry DIFFERENT
    /// names there while the LOGICAL shape is identical (and the engine
    /// regenerates them deterministically from the logical model on emission, so
    /// they do not bear on "same model"). Blanking them makes the readiness
    /// verdict espace-safe at every grain — proven end-to-end by the
    /// `OssysComprehensiveFixtureTests` two-DB canary. The default VALUE is kept
    /// (it IS logical); only the constraint NAME is dropped.
    let toLogicalShape (c: Catalog) : Catalog =
        { c with
            Modules =
                c.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                { k with
                                    Triggers = []
                                    ColumnChecks = []
                                    Attributes = k.Attributes |> List.map (fun a -> { a with DefaultName = None }) }) }) }

    /// Compute the readiness of each confirm environment against the agreed
    /// shape. Each env is the `Compare` SOURCE (its data is checked against the
    /// agreed schema); the agreed shape is the TARGET — so `schemaDelta` reads as
    /// "what the env would have to change to be the agreed shape" and the
    /// dealbreakers read as "what breaks if the env's data lands in the agreed
    /// schema." Both operands' catalogs are normalized to their logical shape
    /// (`toLogicalShape`) first, so the verdict is espace-safe at every grain.
    /// Pure.
    let compute
        (agreedLabel: string)
        (agreed: Compare.Operand)
        (envs: (string * Compare.Operand) list)
        : ReadinessReport =
        let normalize (op: Compare.Operand) : Compare.Operand =
            { op with Catalog = toLogicalShape op.Catalog }
        let agreedOperand = normalize { agreed with Label = agreedLabel }
        let envReadiness (envLabel: string, envOperand: Compare.Operand) : EnvReadiness =
            let report = Compare.compute (normalize { envOperand with Label = envLabel }) agreedOperand
            { Env = envLabel; Verdict = verdictOf report; Report = report }
        { AgreedLabel = agreedLabel
          Envs = envs |> List.map envReadiness }

    /// Estate-level: every confirmed environment is `Ready`.
    let isReady (report: ReadinessReport) : bool =
        report.Envs |> List.forall (fun e -> e.Verdict = Verdict.Ready)

    // ----------------------------------------------------------------------
    // The rolled-up text renderer (THE_VOICE: stative, agentless, count-first;
    // the verdict on top, the proof beneath, the next move named at the close).
    // ----------------------------------------------------------------------

    let private humane (n: int) : string =
        (int64 n).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

    let private categoryLabel (c: ModelFidelity.ViolationCategory) : string =
        match c with
        | ModelFidelity.NotNullCategory  -> "NOT NULL declared, NULLs would land"
        | ModelFidelity.UniqueCategory   -> "UNIQUE/PK declared, duplicates would land"
        | ModelFidelity.OrphanCategory   -> "FK orphans would land"
        | ModelFidelity.OverflowCategory -> "Length / type overflow"

    let private verdictWord (v: Verdict) : string =
        match v with
        | Verdict.Ready   -> "Ready."
        | Verdict.Paused  -> "Paused."
        | Verdict.Blocked -> "Blocked."

    /// Render the estate readiness as operator-facing rolled-up text — the
    /// masthead (which shape), one count-first line per environment, the
    /// dealbreaker drill-down beneath any non-clean env, then the estate verdict.
    let render (report: ReadinessReport) : string list =
        [ yield sprintf "READINESS — the estate against %s's shape" report.AgreedLabel
          yield ""

          for e in report.Envs do
              let r = e.Report
              let schemaN = Compare.schemaNorm r
              let rollup = Compare.dealbreakerRollup r
              let schemaText =
                  match r.SchemaDelta with
                  | None    -> "schema could not be compared"
                  | Some _  ->
                      if schemaN = 0 then "schema matches"
                      else sprintf "%s schema change(s) — not the agreed shape" (humane schemaN)
              let dataText =
                  if not r.DataEvidenceAvailable then "data advisory-silent"
                  elif rollup.Total = 0 then "0 data dealbreakers"
                  else sprintf "%s data dealbreaker(s)" (humane rollup.Total)
              yield sprintf "  %-12s %-8s %s · %s" e.Env (verdictWord e.Verdict) schemaText dataText
              if rollup.Total > 0 then
                  for cat in rollup.Categories do
                      if cat.Count > 0 then
                          yield sprintf "      %-44s %s" (categoryLabel cat.Category) (humane cat.Count)

          yield ""
          let total = List.length report.Envs
          let ready = report.Envs |> List.filter (fun e -> e.Verdict = Verdict.Ready) |> List.length
          if total > 0 && ready = total then
              yield
                  sprintf "  ESTATE — all %s ready. The estate is one shape; the data conforms to %s."
                      (humane total) report.AgreedLabel
          else
              let names (v: Verdict) =
                  report.Envs |> List.filter (fun e -> e.Verdict = v) |> List.map (fun e -> e.Env)
              let blocked = names Verdict.Blocked
              let paused  = names Verdict.Paused
              let parts =
                  [ if not (List.isEmpty blocked) then
                        yield sprintf "%s not the agreed shape (%s)" (humane (List.length blocked)) (String.concat ", " blocked)
                    if not (List.isEmpty paused) then
                        yield sprintf "%s carrying data dealbreaker(s) (%s)" (humane (List.length paused)) (String.concat ", " paused) ]
              yield
                  sprintf "  ESTATE — %s of %s ready. %s; resolve before cutover."
                      (humane ready) (humane total) (String.concat "; " parts) ]

    // ----------------------------------------------------------------------
    // The readiness.json codec (structured, machine-read sibling of the text).
    // ----------------------------------------------------------------------

    let private verdictToken (v: Verdict) : string =
        match v with
        | Verdict.Ready   -> "ready"
        | Verdict.Paused  -> "paused"
        | Verdict.Blocked -> "blocked"

    /// Serialize the report to its `readiness.json` document — the structured,
    /// byte-deterministic sibling of the rolled-up text.
    let toJson (report: ReadinessReport) : JsonObject =
        let root = JsonObject()
        root.["agreed"] <- JsonValue.Create report.AgreedLabel
        root.["ready"] <- JsonValue.Create (isReady report)
        let envs = JsonArray()
        for e in report.Envs do
            let r = e.Report
            let rollup = Compare.dealbreakerRollup r
            let o = JsonObject()
            o.["env"] <- JsonValue.Create e.Env
            o.["verdict"] <- JsonValue.Create (verdictToken e.Verdict)
            let schema = JsonObject()
            schema.["matches"] <- JsonValue.Create (r.SchemaDelta.IsSome && Compare.schemaNorm r = 0)
            schema.["changes"] <- JsonValue.Create (Compare.schemaNorm r)
            o.["schema"] <- schema
            let data = JsonObject()
            data.["evidenceAvailable"] <- JsonValue.Create r.DataEvidenceAvailable
            data.["total"] <- JsonValue.Create rollup.Total
            data.["entities"] <- JsonValue.Create rollup.Entities
            o.["dataDealbreakers"] <- data
            envs.Add(o)
        root.["environments"] <- envs
        root

    /// Serialize to a pretty-printed JSON string (the artifact body).
    let toJsonString (report: ReadinessReport) : string =
        let opts = System.Text.Json.JsonSerializerOptions(WriteIndented = true)
        (toJson report).ToJsonString(opts)
