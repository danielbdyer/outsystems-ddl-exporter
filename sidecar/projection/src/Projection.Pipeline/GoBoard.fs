namespace Projection.Pipeline

// LINT-ALLOW-FILE: the board renderer composes operator-facing report prose
//   (THE_VOICE register: verdict on top, proof beneath, the next move named)
//   at a terminal reporting boundary. The assembly core is pure — every probe
//   result arrives as data; I/O lives one layer up (the CLI run face),
//   exactly as `Readiness`/`Compare` split.

open Projection.Core

/// THE GO BOARD (2026-07-06, the preview-engine program): the ONE surface
/// that forecasts a flow's live run and flags every OPEN DECISION — red
/// until each is resolved, green when the flow is genuinely executable.
///
///   - Every "amino acid" of the forecast is an `Item` on one axis:
///     routing, contract acquisition, table subset, reconcile strategy,
///     schema shape, escaping relationships, load order, plan structure,
///     identity matching, CDC posture, grant evidence, re-run semantics,
///     and the execute gates.
///   - `Status.Red` carries the reason AND the exact remedy — the operator
///     never has to derive the next move.
///   - The verdict is total: GREEN ⇔ zero red items. `check go` exits 0 on
///     green and 5 (the not-ready class, same as `check shape`) on red, so
///     the board is CI-able: wire it red, fix the named decisions, watch it
///     turn green.
[<RequireQualifiedAccess>]
module GoBoard =

    [<RequireQualifiedAccess>]
    type Status =
        /// The axis passes — the note says what was proven.
        | Green of note: string
        /// Real information that does not block a live run — surfaced,
        /// never silent.
        | Advisory of note: string
        /// An open decision or blocking fault: what is wrong + the exact
        /// remedy that turns the line green.
        | Red of reason: string * remedy: string

    /// One row of the BEFORE → AFTER data forecast (2026-07-07, the
    /// go-board forecast program): what the sink table holds now, what the
    /// planned run adds / matches / deletes, and what it holds after —
    /// derived from the dry run's plan + live sink counts, never guessed.
    /// 2026-07-08 (the board-clarity program): the row carries BOTH
    /// physical coordinates — the source table read and the sink table
    /// written differ per espace on the peer leg.
    type ForecastLine =
        { /// Source physical coordinate (`schema.table`) — where the rows
          /// are read from. Empty when the kind has no source side (a
          /// wipe-only line).
          Source  : string
          /// Sink physical coordinate (`schema.table`) — where the change
          /// lands; the before/after counts are THIS table's.
          Table   : string
          /// Live sink row count before the run (`None` when the probe
          /// could not run — rendered `?`, never silently 0).
          Before  : int64 option
          /// Rows the run INSERTs (post-drop).
          Adds    : int64
          /// For a reconciled kind: source rows matched to EXISTING sink
          /// rows (no insert). `None` for non-reconciled kinds.
          Matches : int64 option
          /// Rows the run DELETEs first (the wipe, under strategy replace).
          Deletes : int64
          /// The note column: disposition, phase-2 re-points, drops,
          /// brought-along-by edges.
          Note    : string }

    /// One supporting-scope claim, normalized for the hierarchical lens
    /// (2026-07-08, the rendering-elevation program). Primitive-typed (strings
    /// + `Status`) so the pure `GoBoard` model stays free of the
    /// `SupportingScope` types it compiles before; the face maps its verdicts
    /// + join edges + guarantee onto this.
    type ScopeClaim =
        { /// "references" (the payload points at this) | "dependents" (points at the payload).
          Family       : string
          /// The relationship label — `existing-reference` … `blocked-dependent`.
          Relationship : string
          /// The supporting table's logical label (`Module.Entity`).
          Table        : string
          /// The verify verdict (Green confirmed / Red contradicted).
          Status       : Status
          /// The normalized join: the payload columns that point at this table
          /// (`Order.UserId`), or the dependent's inbound edges.
          JoinEdges    : string list
          /// The operator's authored intent, echoed.
          Reason       : string
          /// The invariant a Confirmed claim earns (empty when contradicted).
          Guarantee    : string }

    /// A family grouping of claims (References / Dependents) for the tree.
    type ScopeGroup =
        { Family : string
          Claims : ScopeClaim list }

    /// One answer's row in a decision table (2026-07-10, the manifest
    /// program, slice 3). Primitive-typed — the counts and the qualifying
    /// facts, never engine types — so the pure board model stays free of the
    /// evidence machinery that computes them.
    type DecisionRow =
        { /// The answer, stated ("reconciled by Name", "added to the transfer").
          Label       : string
          /// Whether this answer is the current selection (the workbench's
          /// cursor state; always false on a one-shot board — an escape, by
          /// definition, has no selection yet).
          Selected    : bool
          /// Rows that re-key onto sink identities under this answer.
          Rekeyed     : int
          /// Rows that drop under this answer.
          Dropped     : int
          /// Rows of the target that enter the transfer (widen only).
          Entering    : int
          /// Tables a widening would open this same decision for, by name.
          Opens       : string list
          /// Some true — each match value names exactly one target row;
          /// Some false — the value repeats; None — uniqueness does not apply.
          SinkUnique  : bool option
          /// The full consequence sentence (the plain/JSON twin carries it too).
          Consequence : string }

    /// One escaping reference's decision: the question and its answer rows.
    type DecisionTable =
        { /// The referenced table (`Module.Entity`).
          Target   : string
          /// The question, stated: which column of which table points at it.
          Question : string
          Rows     : DecisionRow list }

    /// The typed body an item carries for the RICH (Spectre `View`) lens
    /// (2026-07-08). Additive: `Plain` is the default, so every existing
    /// `item`/`itemWith` site and the JSON writer are untouched — `Detail`
    /// stays the authoritative plain/JSON path; `Body` is read only by the
    /// CLI `GoBoardView` builder.
    type ItemBody =
        | Plain
        | Forecast  of lines: ForecastLine list * previews: string list
        | Scope     of groups: ScopeGroup list * unaccounted: string list
        /// The decision workbench (2026-07-10): one table per escaping
        /// reference, each row an answer with its exact consequence.
        | Decisions of tables: DecisionTable list

    type Item =
        { /// The axis label (short, stable — the operator scans these).
          Axis   : string
          Status : Status
          /// Optional per-line proof/detail beneath the headline (escaping
          /// edges, divergence lines, unmatched identities…). The plain/JSON
          /// lens renders these; `Body` carries the typed twin for the rich lens.
          Detail : string list
          /// The typed body for the Spectre `View` lens (`Plain` by default).
          Body   : ItemBody
          /// UNVERIFIED (2026-07-10): the axis is not red — no gate failed — but
          /// the board could not verify the fact it reports (a sink probe that
          /// would not run, a reference the engine takes on the flow's word).
          /// Never a fault, so it never reds the board; it downgrades a clean
          /// GREEN verdict to name the count of unverified findings, so a green
          /// is never read as "every fact proven" when some were only reported.
          /// THE_VOICE §"Could not verify": state the fact, name why, name the
          /// next move — the honest edge, surfaced, never silent.
          Unverified : bool }

    type Board =
        { Flow  : string
          From  : string
          To    : string
          Items : Item list }

    let item (axis: string) (status: Status) : Item = { Axis = axis; Status = status; Detail = []; Body = ItemBody.Plain; Unverified = false }
    let itemWith (axis: string) (status: Status) (detail: string list) : Item = { Axis = axis; Status = status; Detail = detail; Body = ItemBody.Plain; Unverified = false }

    /// The forecast axis (2026-07-08): the typed `ForecastLine list` rides in
    /// `Body` for the responsive-table lens; `detail` is the plain/JSON twin
    /// (the aligned strings + wipe-preview lines) — kept byte-identical.
    let forecastItem (axis: string) (status: Status) (lines: ForecastLine list) (previews: string list) (detail: string list) : Item =
        { Axis = axis; Status = status; Detail = detail; Body = ItemBody.Forecast (lines, previews); Unverified = false }

    /// The supporting-scope axis (2026-07-08): the typed `ScopeGroup list` rides
    /// in `Body` for the hierarchical lens; `detail` is the plain/JSON twin.
    let scopeItem (axis: string) (status: Status) (groups: ScopeGroup list) (unaccounted: string list) (detail: string list) : Item =
        { Axis = axis; Status = status; Detail = detail; Body = ItemBody.Scope (groups, unaccounted); Unverified = false }

    /// The decision-workbench axis (2026-07-10, the manifest program,
    /// slice 3): the typed `DecisionTable list` rides in `Body` for the
    /// table lens; `detail` is the plain/JSON twin, byte-identical to the
    /// pre-typed rendering (the Forecast/Scope discipline).
    let decisionsItem (axis: string) (status: Status) (tables: DecisionTable list) (detail: string list) : Item =
        { Axis = axis; Status = status; Detail = detail; Body = ItemBody.Decisions tables; Unverified = false }

    /// Mark an item UNVERIFIED — a fact the board could not check (a probe that
    /// would not run, a reference declared stable but outside the contract). An
    /// unverified item is NOT a fault (it never reds the board); it only names
    /// itself in the verdict so a green is never read as "every fact proven".
    /// Applied to an already-advisory item, so the mark column reads `[note]`.
    let asUnverified (i: Item) : Item = { i with Unverified = true }

    let private isRed (i: Item) = match i.Status with Status.Red _ -> true | _ -> false

    /// GREEN ⇔ zero red items (advisories never block).
    let isGreen (b: Board) : bool = b.Items |> List.forall (fun i -> not (isRed i))

    let redCount (b: Board) : int = b.Items |> List.filter isRed |> List.length

    /// Whether any axis is an unverified finding the board could not check —
    /// surfaced in the verdict so a GREEN is never read as "every fact proven"
    /// when some were only reported.
    let hasUnverified (b: Board) : bool = b.Items |> List.exists (fun i -> i.Unverified)

    let unverifiedCount (b: Board) : int = b.Items |> List.filter (fun i -> i.Unverified) |> List.length

    /// The CI-able exit: 0 green; 5 (the not-ready verdict class `check
    /// shape` also uses) on red. An unverified finding does NOT change the exit —
    /// it is information, not a blocking fault (advisories never block); the
    /// operator reads the named note line(s) and the verdict's unverified count.
    let exitCode (b: Board) : int = if isGreen b then 0 else 5

    /// The machine-readable projection (`--format json`) — the CI-consumable
    /// twin of the exit code: `{flow, from, to, verdict, redCount, items:[
    /// {axis, status, headline, remedy?, detail[]}]}`. Typed writer, never
    /// string concatenation (the house JSON discipline).
    let toJsonString (b: Board) : string =
        use ms = new System.IO.MemoryStream()
        use w = new System.Text.Json.Utf8JsonWriter(ms, System.Text.Json.JsonWriterOptions(Indented = true))
        w.WriteStartObject()
        w.WriteString("flow", b.Flow)
        w.WriteString("from", b.From)
        w.WriteString("to", b.To)
        w.WriteString("verdict", (if isGreen b then (if hasUnverified b then "green-unverified" else "green") else "red"))
        w.WriteNumber("redCount", redCount b)
        w.WriteNumber("unverifiedCount", unverifiedCount b)
        w.WriteStartArray "items"
        for i in b.Items do
            w.WriteStartObject()
            w.WriteString("axis", i.Axis)
            w.WriteBoolean("unverified", i.Unverified)
            (match i.Status with
             | Status.Green note ->
                 w.WriteString("status", "green")
                 w.WriteString("headline", note)
             | Status.Advisory note ->
                 w.WriteString("status", "advisory")
                 w.WriteString("headline", note)
             | Status.Red (reason, remedy) ->
                 w.WriteString("status", "red")
                 w.WriteString("headline", reason)
                 w.WriteString("remedy", remedy))
            w.WriteStartArray "detail"
            for d in i.Detail do w.WriteStringValue d
            w.WriteEndArray()
            w.WriteEndObject()
        w.WriteEndArray()
        w.WriteEndObject()
        w.Flush()
        System.Text.Encoding.UTF8.GetString(ms.ToArray())

    [<RequireQualifiedAccess>]
    module ForecastLine =

        let after (l: ForecastLine) : int64 option =
            l.Before |> Option.map (fun b -> b - l.Deletes + l.Adds)

    /// Render the forecast lines as one aligned BEFORE → AFTER table
    /// (pure; the face supplies probed counts). Numbers right-aligned;
    /// a totals row closes the table; `?` marks an unprobed count.
    let forecastTable (lines: ForecastLine list) : string list =
        if List.isEmpty lines then []
        else
            let num (v: int64 option) = match v with Some n -> string n | None -> "?"
            let widthOf (header: string) (f: ForecastLine -> string) =
                lines |> List.map (fun l -> (f l).Length) |> List.max |> max header.Length
            let srcWidth  = widthOf "source (read)" (fun l -> l.Source)
            let sinkWidth = widthOf "target (written)" (fun l -> l.Table)
            let row (source: string) (table: string) (before: string) (adds: string) (matches: string) (deletes: string) (after: string) (note: string) =
                sprintf "%-*s  %-*s  %10s  %8s  %8s  %8s  %10s  %s" srcWidth source sinkWidth table before adds matches deletes after note
            let total (f: ForecastLine -> int64 option) =
                lines |> List.fold (fun acc l -> match acc, f l with Some a, Some v -> Some (a + v) | _ -> None) (Some 0L)
            [ yield row "source (read)" "target (written)" "before" "+add" "match" "-del" "after" ""
              for l in lines do
                  yield row
                      l.Source
                      l.Table
                      (num l.Before)
                      (string l.Adds)
                      (match l.Matches with Some m -> string m | None -> "-")
                      (string l.Deletes)
                      (num (ForecastLine.after l))
                      l.Note
              yield row
                  ""
                  "TOTAL"
                  (num (total (fun l -> l.Before)))
                  (string (lines |> List.sumBy (fun l -> l.Adds)))
                  (string (lines |> List.sumBy (fun l -> l.Matches |> Option.defaultValue 0L)))
                  (string (lines |> List.sumBy (fun l -> l.Deletes)))
                  (num (total ForecastLine.after))
                  "" ]

    /// Render the board as operator-facing lines: the mark column, the axis,
    /// the headline; detail lines indented beneath; the verdict at the close
    /// with the next move named.
    let render (b: Board) : string list =
        [ yield sprintf "THE GO BOARD — flow '%s' (%s -> %s)" b.Flow b.From b.To
          yield ""
          for i in b.Items do
              let mark, headline =
                  match i.Status with
                  | Status.Green note          -> "[ GO ]", note
                  | Status.Advisory note       -> "[note]", note
                  | Status.Red (reason, _)     -> "[STOP]", reason
              yield sprintf "  %s %-18s %s" mark i.Axis headline
              match i.Status with
              | Status.Red (_, remedy) -> yield sprintf "         %-18s -> %s" "" remedy
              | _ -> ()
              for d in i.Detail do
                  yield sprintf "         %-18s    %s" "" d
          yield ""
          if isGreen b then
              if hasUnverified b then
                  yield sprintf "  VERDICT — GREEN. Every gate passes; %d finding(s) below remain unverified — the board could not read them against the live environments. Read the note line(s) that name what is unverified, then execute with: PROJECTION_ALLOW_EXECUTE=1 projection %s --go" (unverifiedCount b) b.Flow
              else
                  yield sprintf "  VERDICT — GREEN. Every gate passes. Execute with: PROJECTION_ALLOW_EXECUTE=1 projection %s --go" b.Flow
          else
              yield sprintf "  VERDICT — RED. %d open decision(s) / blocking fault(s) — resolve every [STOP] line above, then re-run `projection check go %s` until green." (redCount b) b.Flow ]
