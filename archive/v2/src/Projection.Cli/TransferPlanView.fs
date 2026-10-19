module Projection.Cli.TransferPlanView

// THE TRANSFER PLAN, rendered through the `View` engine (2026-07-08, the
// guided-wizard program). The pure `TransferPlan.Plan` (Pipeline) is the ONE
// substrate; this CLI builder is the rich lens — a `Rule`-divided section per
// decision, the alternatives as a fully-expanded `Tree` (each branch → its why +
// its config edit). It dogfoods the widget-elevation cases (`Rule`, `Tree`) and
// shares `GoBoardView.writeView`'s redirected-report width policy, so a piped
// plan prints in full and a TTY reflows.

open Projection.Cli.View
open Projection.Pipeline

/// One branch of a decision as a tree node — the option label (● when it is the
/// flow's current choice, ▸ otherwise), opening into its why and the config edit
/// that selects it.
let private optionNode (o: TransferPlan.PlanOption) : TreeNode =
    let mark = if o.Chosen then "● " else "▸ "
    let st = if o.Chosen then Ok else Neutral
    let children =
        [ yield { Label = o.Why; Status = Neutral; Children = [] }
          if o.ConfigEdit <> "" then
              yield { Label = sprintf "→ set %s" o.ConfigEdit; Status = st; Children = [] } ]
    { Label = mark + o.Label; Status = st; Children = children }

/// One decision as a titled section: the `Rule` divider names the axis, a note
/// states the current choice + why the axis exists, and the `Tree` lays the
/// branches out fully expanded.
let private decisionBlocks (d: TransferPlan.PlanDecision) : View list =
    [ Rule (Some d.Axis, Neutral)
      Note (sprintf "current: %s" d.Current)
      Note d.Rationale
      Tree ("choose", Neutral, d.Options |> List.map optionNode) ]

/// The whole plan as one `View` — a hero, the decision sections, and the closing
/// reminder that every branch is equally reachable by hand-editing the config
/// (the declarative "config is the menu" contract).
let ofPlan (p: TransferPlan.Plan) : View =
    let hero = Hero (Neutral, sprintf "TRANSFER PLAN — flow '%s'   %s → %s" p.Flow p.From p.To)
    Doc
        ([ hero; Blank ]
         @ (p.Decisions |> List.collect decisionBlocks)
         @ [ Blank
             Note "each branch is a config edit — pick one on a terminal, or set it in projection.json yourself." ])

/// Render the plan to a writer through the rich lens (redirected → the wide,
/// un-wrapped report; TTY → color + reflow), reusing the go board's writer.
let write (writer: System.IO.TextWriter) (p: TransferPlan.Plan) : unit =
    GoBoardView.writeView writer (ofPlan p)
