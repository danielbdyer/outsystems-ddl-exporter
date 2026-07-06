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

    type Item =
        { /// The axis label (short, stable — the operator scans these).
          Axis   : string
          Status : Status
          /// Optional per-line proof/detail beneath the headline (escaping
          /// edges, divergence lines, unmatched identities…).
          Detail : string list }

    type Board =
        { Flow  : string
          From  : string
          To    : string
          Items : Item list }

    let item (axis: string) (status: Status) : Item = { Axis = axis; Status = status; Detail = [] }
    let itemWith (axis: string) (status: Status) (detail: string list) : Item = { Axis = axis; Status = status; Detail = detail }

    let private isRed (i: Item) = match i.Status with Status.Red _ -> true | _ -> false

    /// GREEN ⇔ zero red items (advisories never block).
    let isGreen (b: Board) : bool = b.Items |> List.forall (fun i -> not (isRed i))

    let redCount (b: Board) : int = b.Items |> List.filter isRed |> List.length

    /// The CI-able exit: 0 green; 5 (the not-ready verdict class `check
    /// shape` also uses) on red.
    let exitCode (b: Board) : int = if isGreen b then 0 else 5

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
              yield sprintf "  VERDICT — GREEN. Every gate passes. Execute with: PROJECTION_ALLOW_EXECUTE=1 projection %s --go" b.Flow
          else
              yield sprintf "  VERDICT — RED. %d open decision(s) / blocking fault(s) — resolve every [STOP] line above, then re-run `projection check go %s` until green." (redCount b) b.Flow ]
