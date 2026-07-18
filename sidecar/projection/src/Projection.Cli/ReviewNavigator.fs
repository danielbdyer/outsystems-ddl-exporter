namespace Projection.Cli
// LINT-ALLOW-FILE: the interactive review navigator — a terminal-UI state machine
//   (cursor / page / selection) driven by keypress events. The mutable locals ARE
//   the imperative navigation state (a UI loop over the terminal-as-world has no
//   pure-value equivalent); `String.concat` composes the rendered lines at the
//   console text boundary. Boundary-confined to the interactive surface.

// THE REVIEW WORKBENCH (2026-07-10, the manifest program, slice 3 —
// THE_TRANSFER_MANIFEST.md §4 / §6): the interactive surface where each
// escaping reference is decided by comparison. The cursor walks the open
// decisions; Space selects the next answer for the decision under the cursor
// and the whole coupled component's consequences recompute — a pure lookup
// over the slice-2 EvidenceCache, so the toggle is instant and honest (no IO
// enters the reducer; there is no second forecast derivation). `w` writes the
// selections to `projection.json` as the config vocabulary the engine already
// honors, so the headless run and the interactive session read one truth.
//
// Composition, not generalization: `Navigator.Model` carries the cursor;
// navigation keys delegate to `Navigator.step` unchanged. The domain state
// (the selections) lives beside it, and the ONE paired traversal that builds
// the `View` also builds the path→decision index — the cursor's meaning
// cannot drift from what is drawn.

open System
open Spectre.Console
open Projection.Core
open Projection.Pipeline

/// The Voice-audited decision rows, shared by the one-shot board (no
/// selection — an escape, by definition, is undecided) and the workbench
/// (live selection). One builder, two consumers: the two surfaces cannot
/// disagree on a count or a sentence.
[<RequireQualifiedAccess>]
module DecisionRows =

    /// The `Module.Entity` label of an edge's target.
    let targetLabel (edges: PeerTransfer.EscapingFk list) (target: SsKey) : string =
        edges
        |> List.tryFind (fun e -> e.Target = target)
        |> Option.map (fun e -> sprintf "%s.%s" (Name.value e.TargetModule) (Name.value e.TargetName))
        |> Option.defaultValue (SsKey.rootOriginal target)

    /// The question, stated: which column of which table points at the target.
    let questionOf (edges: PeerTransfer.EscapingFk list) (target: SsKey) (label: string) : string =
        edges
        |> List.filter (fun e -> e.Target = target)
        |> List.map (fun e -> sprintf "%s.%s points at %s, which is not in the transfer" (Name.value e.KindName) (Name.value e.Column) label)
        |> String.concat "; "

    /// One answer's row: the label, the exact counts, and the consequence
    /// sentence (THE_VOICE: one complete sentence — the condition, the
    /// counted outcome, the qualifying fact).
    let rowFor
        (catalog: Catalog)
        (label: string)
        (selected: bool)
        (answer: EvidenceCache.Answer)
        (ev: EvidenceCache.AnswerEvidence)
        : GoBoard.DecisionRow =
        let d = ev.Delta
        let uniqueness (col: Name) =
            match ev.SinkUnique with
            | Some true -> sprintf " Each %s value names exactly one target row." (Name.value col)
            | Some false -> sprintf " The %s value repeats on the target, so the oldest row is kept and later duplicates are displaced." (Name.value col)
            | None -> ""
        let dropped (col: Name) =
            match d.RowsDropped with
            | 0 -> "none drop"
            | n -> sprintf "%d drop because the %s row each points at has no %s match in the target" n label (Name.value col)
        let spawnedNames =
            d.SpawnedKeys
            |> List.map (fun k ->
                Catalog.tryFindKind k catalog
                |> Option.map (fun kd -> Name.value kd.Name)
                |> Option.defaultValue (SsKey.rootOriginal k))
        let rowLabel, consequence =
            match answer with
            | EvidenceCache.Answer.Reconcile col ->
                sprintf "reconciled by %s" (Name.value col),
                sprintf "consequence: if %s is reconciled by %s, %d row(s) that point at it re-key onto the %s rows the target already holds, and %s.%s"
                    label (Name.value col) d.RowsRekeyed label (dropped col) (uniqueness col)
            | EvidenceCache.Answer.StaticLookup col ->
                sprintf "declared identical, matched by %s" (Name.value col),
                sprintf "consequence: if %s is declared identical in both environments and matched by %s, the same %d row(s) re-key and %s; a live run refuses if any %s row differs between the environments, is missing, or is extra.%s"
                    label (Name.value col) d.RowsRekeyed (dropped col) label (uniqueness col)
            | EvidenceCache.Answer.Pin _ ->
                "re-keyed onto one chosen row",
                sprintf "consequence: if every reference to %s is re-keyed onto one chosen %s row in the target, all %d row(s) that point at it re-key and none drop; the row must be chosen, and must exist in the target."
                    label label d.RowsRekeyed
            | EvidenceCache.Answer.Widen ->
                "added to the transfer",
                (let spawned =
                    match spawnedNames with
                    | [] -> sprintf "%s points at no table outside the transfer, so nothing further needs deciding" label
                    | names -> sprintf "%s itself points at %d table(s) outside the transfer (%s), and each of those will then need this same decision" label names.Length (String.concat ", " names)
                 sprintf "consequence: if %s is added to the transfer, its %d row(s) transfer too — and %s."
                    label d.RowsEnteringScope spawned)
        { Label = rowLabel
          Selected = selected
          Rekeyed = d.RowsRekeyed
          Dropped = d.RowsDropped
          Entering = d.RowsEnteringScope
          Opens = spawnedNames
          SinkUnique = ev.SinkUnique
          Consequence = consequence }

    /// One target's full decision table under the given selection (None on
    /// the one-shot board — undecided; Some on the workbench).
    let tableFor
        (catalog: Catalog)
        (componentEdges: PeerTransfer.EscapingFk list)
        (per: Map<EvidenceCache.Answer, EvidenceCache.AnswerEvidence>)
        (selection: EvidenceCache.Answer option)
        (target: SsKey)
        : GoBoard.DecisionTable =
        let label = targetLabel componentEdges target
        let rows =
            EvidenceCache.candidateAnswers componentEdges target
            |> List.choose (fun a -> per |> Map.tryFind a |> Option.map (fun ev -> a, ev))
            |> List.map (fun (a, ev) -> rowFor catalog label (selection = Some a) a ev)
        { Target = label; Question = questionOf componentEdges target label; Rows = rows }

/// What the cursor can act on — derived per-render, path-keyed, never stored
/// independently of the tree it indexes.
[<RequireQualifiedAccess>]
type ReviewTarget =
    | Edge of target: SsKey
    /// One act in the consent ledger, addressed by its canonical token.
    | Act of token: string
    | Inert

[<RequireQualifiedAccess>]
module ReviewNavigator =

    /// One act in the consent ledger (slice 4a): the canonical token, the
    /// operator-facing statement of what the act does, the fingerprint the
    /// board derived this pass (None when its substrate would not read), and
    /// whether the bless-everything gesture may sweep it.
    type ActRow =
        { Token       : string
          Statement   : string
          Fingerprint : ActConsent.ActFingerprint option
          /// `a` (bless every act) never sweeps an identity-insert — explicit
          /// primary-key writes are blessed one at a time, deliberately.
          Sweepable   : bool }

    /// Everything the workbench holds — pure data only; the connections that
    /// filled the cache are long closed by the time the loop opens.
    type Workbench =
        { Flow            : string
          ConfigPath      : string
          Catalog         : Catalog
          LoadSet         : Set<SsKey>
          Reconciled      : Set<SsKey>
          Components      : PeerTransfer.EscapingFk list list
          Cache           : EvidenceCache.Cache
          /// the flow's CURRENT config lists — `w` appends, never clobbers.
          Tables          : string list
          Reconcile       : string list
          SupportingScope : SupportingScope.SupportingScopeEntry list
          /// The consent ledger (slice 4a): every act the forecast plan
          /// performs, in board order. Empty when the forecast did not run.
          Acts            : ActRow list
          /// The flow's CURRENT mode approvals — a bless write-through
          /// re-writes the whole `signoff` array, so these are preserved.
          ModeSignoff     : WriteSignoff.WriteApproval list
          /// The flow's act blessings on file at bench build.
          ActSignoff      : WriteSignoff.ActBlessing list }

    type Model =
        { Nav         : Navigator.Model
          Bench       : Workbench
          Decisions   : Map<SsKey, EvidenceCache.Answer>
          /// The blessed set as WRITTEN: token → the fingerprint on file.
          /// Initialized from the flow's act signoffs; a bless gesture adds
          /// the exact fingerprint captured at gesture time and writes
          /// through in the same keystroke.
          Blessings   : Map<string, ActConsent.ActFingerprint>
          Targets     : Map<int list, ReviewTarget>
          Dirty       : bool
          PendingQuit : bool }

    /// The stable target order: components in order, targets within a
    /// component sorted by key — the same order every render.
    let private orderedTargets (bench: Workbench) : (PeerTransfer.EscapingFk list * SsKey) list =
        bench.Components
        |> List.collect (fun componentEdges ->
            componentEdges
            |> List.map (fun e -> e.Target)
            |> List.distinct
            |> List.sortBy SsKey.rootOriginal
            |> List.map (fun t -> componentEdges, t))

    /// THE PAIRED SINGLE TRAVERSAL: one pass builds BOTH the `View` and the
    /// path→target index, so the cursor's domain meaning cannot drift from
    /// what is drawn. Pure.
    let render (bench: Workbench) (decisions: Map<SsKey, EvidenceCache.Answer>) (blessings: Map<string, ActConsent.ActFingerprint>) : View.View * Map<int list, ReviewTarget> =
        let targets = orderedTargets bench
        let decided = targets |> List.filter (fun (_, t) -> decisions.ContainsKey t) |> List.length
        // an act is blessed when the fingerprint ON FILE equals the one this
        // pass derived — a changed substrate re-opens it, never silently.
        let isBlessed (a: ActRow) =
            match a.Fingerprint, blessings |> Map.tryFind a.Token with
            | Some fp, Some onFile -> onFile = fp
            | _ -> false
        let blessedCount = bench.Acts |> List.filter isBlessed |> List.length
        let blocks = System.Collections.Generic.List<View.View>()
        let index = System.Collections.Generic.Dictionary<int list, ReviewTarget>()
        blocks.Add (View.Hero (View.Warn, sprintf "THE DECISION WORKBENCH — flow '%s'   %d open decision(s), %d decided" bench.Flow (List.length targets) decided))
        blocks.Add (View.Rule (None, View.Neutral))
        // per-component evidence, computed once per render (pure cache lookups)
        let perByComponent =
            bench.Components
            |> List.map (fun edges -> edges, EvidenceCache.perAnswerDeltas bench.Cache bench.Catalog bench.LoadSet bench.Reconciled edges decisions)
        for (componentEdges, target) in targets do
            let per =
                perByComponent
                |> List.tryFind (fun (edges, _) -> System.Object.ReferenceEquals(edges, componentEdges))
                |> Option.bind (fun (_, m) -> m |> Map.tryFind target)
                |> Option.defaultValue Map.empty
            let selection = decisions |> Map.tryFind target
            let table = DecisionRows.tableFor bench.Catalog componentEdges per selection target
            let headline =
                match selection with
                | Some _ ->
                    let chosen = table.Rows |> List.tryFind (fun r -> r.Selected) |> Option.map (fun r -> r.Label) |> Option.defaultValue ""
                    sprintf "%s — %s" table.Target chosen
                | None -> sprintf "%s — undecided" table.Target
            let status = if Option.isSome selection then View.Ok else View.Warn
            index.[[ blocks.Count ]] <- ReviewTarget.Edge target
            blocks.Add (View.Disclosure (headline, status,
                            View.Field ("decision", table.Question, status)
                            :: GoBoardView.decisionTable table))
        // -- the consent ledger (slice 4a): one block per act, in the same
        // paired traversal — the bless gesture addresses the block under the
        // cursor through the same index the decisions use.
        (if not (List.isEmpty bench.Acts) then
            blocks.Add (View.Rule (Some "acts", (if blessedCount = bench.Acts.Length then View.Ok else View.Warn)))
            for a in bench.Acts do
                let status = if isBlessed a then View.Ok else View.Warn
                let standing =
                    match a.Fingerprint, blessings |> Map.tryFind a.Token with
                    | Some fp, Some onFile when onFile = fp ->
                        sprintf "blessed at %s — the blessing on file matches what this run would do." (ActConsent.fingerprintText fp)
                    | Some fp, Some _ ->
                        sprintf "re-opened: what this act would do has changed since it was blessed. Read the statement above and press d to bless it again at %s." (ActConsent.fingerprintText fp)
                    | Some fp, None ->
                        sprintf "not blessed. Press d to bless it at %s." (ActConsent.fingerprintText fp)
                    | None, _ ->
                        "the substrate this act consumes could not be read this pass, so there is no fingerprint to bind a blessing to — resolve the connection and re-run."
                index.[[ blocks.Count ]] <- ReviewTarget.Act a.Token
                blocks.Add (View.Disclosure ((sprintf "%s — %s" a.Token (if isBlessed a then "blessed" else "open")), status,
                                [ View.Field ("act", a.Statement, status)
                                  View.Field ("standing", standing, status) ])))
        blocks.Add (View.Rule (Some "standing", (if decided = List.length targets && blessedCount = bench.Acts.Length then View.Ok else View.Warn)))
        blocks.Add (View.Panel ("standing",
                        [ yield View.PanelRow.Labeled ("decided", sprintf "%d of %d" decided (List.length targets), (if decided = List.length targets then View.Ok else View.Warn))
                          if not (List.isEmpty bench.Acts) then
                              yield View.PanelRow.Labeled ("blessed", sprintf "%d of %d act(s)" blessedCount bench.Acts.Length, (if blessedCount = bench.Acts.Length then View.Ok else View.Warn))
                          yield View.PanelRow.Next
                                  (if List.isEmpty bench.Acts
                                   then sprintf "Space selects the next answer for the decision under the cursor; w writes the selections to %s; re-run `projection check go %s` to re-verdict." bench.ConfigPath bench.Flow
                                   else sprintf "Space selects the next answer; d blesses the act under the cursor; a blesses every act except identity-insert; w writes the selections to %s; re-run `projection check go %s` to re-verdict." bench.ConfigPath bench.Flow) ]))
        View.Doc (List.ofSeq blocks), (index |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq)

    /// The longest valid prefix of a path in a tree — the cursor's safety
    /// clamp when a rebuild changes the keyset under it.
    let rec private clampPath (tree: View.View) (path: int list) : int list =
        if Option.isSome (Navigator.nodeAt tree path) then path
        else
            match path with
            | [] -> []
            | _ -> clampPath tree (path |> List.rev |> List.tail |> List.rev)

    /// Rebuild the tree + index from the current selections, keeping the
    /// cursor on the longest still-valid prefix of its path.
    let private rebuild (m: Model) : Model =
        let tree, targets = render m.Bench m.Decisions m.Blessings
        let clamped = clampPath tree m.Nav.Path
        { m with Nav = { m.Nav with Tree = tree; Path = clamped }; Targets = targets }

    /// The decision under the cursor: the longest path prefix that addresses
    /// a decision block (the cursor may be dug INTO the disclosure).
    let targetAt (m: Model) : ReviewTarget option =
        let rec walk (path: int list) =
            match m.Targets |> Map.tryFind path with
            | Some t -> Some t
            | None ->
                match path with
                | [] -> None
                | _ -> walk (path |> List.rev |> List.tail |> List.rev)
        walk m.Nav.Path

    /// The next answer in the candidate cycle (undecided → the first).
    let private cycleAnswer (bench: Workbench) (target: SsKey) (decisions: Map<SsKey, EvidenceCache.Answer>) : Map<SsKey, EvidenceCache.Answer> =
        let componentEdges =
            bench.Components
            |> List.tryFind (fun edges -> edges |> List.exists (fun e -> e.Target = target))
            |> Option.defaultValue []
        match EvidenceCache.candidateAnswers componentEdges target with
        | [] -> decisions
        | candidates ->
            let next =
                match decisions |> Map.tryFind target with
                | None -> List.head candidates
                | Some current ->
                    match candidates |> List.tryFindIndex (fun a -> a = current) with
                    | Some i -> candidates.[(i + 1) % candidates.Length]
                    | None -> List.head candidates
            Map.add target next decisions

    /// TOTAL over `ConsoleKey`: Space cycles the decision under the cursor
    /// (the whole component's consequences recompute — pure); q with unsaved
    /// selections asks once, then quits; every other key is navigation,
    /// delegated to `Navigator.step` unchanged.
    let step (key: ConsoleKey) (m: Model) : Model =
        match key with
        | ConsoleKey.Spacebar ->
            match targetAt m with
            | Some (ReviewTarget.Edge target) ->
                rebuild { m with Decisions = cycleAnswer m.Bench target m.Decisions; Dirty = true; PendingQuit = false }
            | _ -> m
        | ConsoleKey.Q | ConsoleKey.Escape when m.Dirty && not m.PendingQuit ->
            { m with PendingQuit = true }
        | _ ->
            { m with Nav = Navigator.step key m.Nav; PendingQuit = false }

    /// BLESS the named acts at the fingerprints derived THIS pass — the exact
    /// set captured at gesture time, never a predicate over future substrate.
    /// Writes the flow's whole `signoff` array through immediately (mode
    /// approvals preserved verbatim; blessings the gesture did not touch
    /// preserved with their audit fields): a blessing is commitment, where a
    /// decision cycle is deliberation. Returns the updated model and the
    /// shell notes. Impure (the one write seam beside `persist`).
    let bless (m: Model) (tokens: string list) : Model * string list =
        let capturable =
            m.Bench.Acts
            |> List.filter (fun a -> List.contains a.Token tokens)
            |> List.choose (fun a -> a.Fingerprint |> Option.map (fun fp -> a.Token, fp))
        if List.isEmpty capturable then
            m, [ "nothing blessed — the act under the cursor has no derived fingerprint to bind a blessing to." ]
        else
            let updated = capturable |> List.fold (fun acc (t, fp) -> Map.add t fp acc) m.Blessings
            let entries =
                updated
                |> Map.toList
                |> List.map (fun (t, fp) ->
                    // an unchanged blessing keeps its audit fields; a re-bless
                    // at a new fingerprint starts a fresh minimal entry.
                    match m.Bench.ActSignoff |> List.tryFind (fun b -> b.Act = t && b.Fingerprint = fp) with
                    | Some existing -> existing
                    | None -> WriteSignoff.blessed t fp)
            match RelaxationStore.setFlowSignoffEntries m.Bench.ConfigPath m.Bench.Flow m.Bench.ModeSignoff entries with
            | Ok () ->
                rebuild { m with Blessings = updated; PendingQuit = false },
                [ sprintf "blessed %s — written to %s." (capturable |> List.map fst |> String.concat ", ") m.Bench.ConfigPath ]
            | Error e ->
                m, [ sprintf "the blessing did not persist — %s. Nothing was recorded; the file is unchanged." e ]

    /// The config edits the current selections materialize into — the SAME
    /// vocabulary the engine already honors (a selection is never a new
    /// grammar). Returns the writable edits and, for what cannot be written
    /// without more information, the exact by-hand instruction (named, never
    /// silent).
    let toConfigEdits (bench: Workbench) (decisions: Map<SsKey, EvidenceCache.Answer>) =
        let allEdges = List.concat bench.Components
        let labelOf t = DecisionRows.targetLabel allEdges t
        let reconciles =
            decisions
            |> Map.toList
            |> List.choose (fun (t, a) ->
                match a with
                | EvidenceCache.Answer.Reconcile col -> Some (sprintf "%s:%s" (labelOf t) (Name.value col))
                | _ -> None)
        let widens =
            decisions
            |> Map.toList
            |> List.choose (fun (t, a) -> match a with EvidenceCache.Answer.Widen -> Some (labelOf t) | _ -> None)
        let statics =
            decisions
            |> Map.toList
            |> List.choose (fun (t, a) ->
                match a with
                | EvidenceCache.Answer.StaticLookup col ->
                    Some ({ Table = labelOf t
                            Relationship = SupportingScope.SupportingRelationship.StaticLookup (Name.value col)
                            Reason = "selected in the review workbench: the datasets are held identical" } : SupportingScope.SupportingScopeEntry)
                | _ -> None)
        let byHand =
            decisions
            |> Map.toList
            |> List.choose (fun (t, a) ->
                match a with
                | EvidenceCache.Answer.Pin _ ->
                    Some (sprintf "%s: a pinned row needs its key — author the reconcile entry \"%s:<column>:=<key>\" in %s by hand." (labelOf t) (labelOf t) bench.ConfigPath)
                | _ -> None)
        reconciles, widens, statics, byHand

    /// Persist the selections (the `w` gesture): append to the flow's
    /// existing `reconcile` / `tables` / `supportingScope` — never clobber —
    /// and name what still needs the operator's hand. Impure; returns the
    /// lines the shell prints.
    let persist (bench: Workbench) (decisions: Map<SsKey, EvidenceCache.Answer>) : string list =
        let reconciles, widens, statics, byHand = toConfigEdits bench decisions
        let write (field: string) (current: string list) (added: string list) =
            if List.isEmpty added then []
            else
                match RelaxationStore.setFlowStrings bench.ConfigPath bench.Flow field (List.distinct (current @ added)) with
                | Ok () -> [ sprintf "wrote %s to %s: %s" field bench.ConfigPath (String.concat ", " added) ]
                | Error e -> [ sprintf "could not write %s to %s: %s" field bench.ConfigPath e ]
        let scopeLines =
            if List.isEmpty statics then []
            else
                let merged =
                    bench.SupportingScope @ (statics |> List.filter (fun s -> bench.SupportingScope |> List.forall (fun e -> e.Table <> s.Table)))
                match RelaxationStore.setFlowSupportingScope bench.ConfigPath bench.Flow merged with
                | Ok () -> [ sprintf "wrote supportingScope to %s: %s" bench.ConfigPath (statics |> List.map (fun s -> s.Table) |> String.concat ", ") ]
                | Error e -> [ sprintf "could not write supportingScope to %s: %s" bench.ConfigPath e ]
        let undecided =
            orderedTargets bench
            |> List.filter (fun (_, t) -> not (decisions.ContainsKey t))
            |> List.map (fun (edges, t) -> DecisionRows.targetLabel edges t)
        write "reconcile" bench.Reconcile reconciles
        @ write "tables" bench.Tables widens
        @ scopeLines
        @ byHand
        @ (match undecided with
           | [] -> []
           | names -> [ sprintf "still undecided: %s — the board's relationships line stays red until each is resolved." (String.concat ", " names) ])

    let private legend = "↑↓ move   →/Enter open   ← back   Space select the next answer   w write the selections   q quit"

    let private legendWithActs = "↑↓ move   →/Enter open   ← back   Space select   d bless the act   a bless all but identity-insert   w write   q quit"

    /// The redraw loop (the one I/O boundary; the terminal bracket restores
    /// cursor + Ctrl-C modes on every exit path).
    let run (bench: Workbench) : int =
        let console = View.consoleTo Console.Out
        let blessingsOnFile =
            bench.ActSignoff |> List.map (fun b -> b.Act, b.Fingerprint) |> Map.ofList
        let tree, targets = render bench Map.empty blessingsOnFile
        let mutable model =
            { Nav = Navigator.init 0 tree
              Bench = bench
              Decisions = Map.empty
              Blessings = blessingsOnFile
              Targets = targets
              Dirty = false
              PendingQuit = false }
        let mutable notes : string list = []
        Navigator.withTerminalBracket (fun () ->
            try
                let mutable go = true
                while go do
                    (try Console.Clear() with _ -> console.WriteLine())
                    View.writeWith { Navigator.project model.Nav with Width = console.Profile.Width } console model.Nav.Tree
                    console.WriteLine()
                    for n in notes do
                        Navigator.safeMarkupLine console (Markup.Escape n) n
                    let warning =
                        if model.PendingQuit then "unsaved selections — w writes them to projection.json; q again discards them.   " else ""
                    let legendLine = warning + (if List.isEmpty bench.Acts then legend else legendWithActs)
                    Navigator.safeMarkupLine console (Markup.Escape legendLine) legendLine
                    let key = Console.ReadKey(true)
                    if key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key = ConsoleKey.C then
                        go <- false
                    elif key.Key = ConsoleKey.W then
                        notes <- persist model.Bench model.Decisions
                        model <- { model with Dirty = false; PendingQuit = false }
                    elif key.Key = ConsoleKey.D && not (List.isEmpty bench.Acts) then
                        // bless the act under the cursor — write-through now.
                        match targetAt model with
                        | Some (ReviewTarget.Act token) ->
                            let m2, n2 = bless model [ token ]
                            model <- m2
                            notes <- n2
                        | _ ->
                            notes <- [ "the cursor is not on an act — move to the acts section (below the decisions) and press d there." ]
                    elif key.Key = ConsoleKey.A && not (List.isEmpty bench.Acts) then
                        // bless every act EXCEPT identity-insert — each at the
                        // exact fingerprint derived this pass, in one write.
                        let sweep = bench.Acts |> List.filter (fun a -> a.Sweepable) |> List.map (fun a -> a.Token)
                        let held = bench.Acts |> List.filter (fun a -> not a.Sweepable) |> List.map (fun a -> a.Token)
                        let m2, n2 = bless model sweep
                        model <- m2
                        notes <-
                            n2 @ (match held with
                                  | [] -> []
                                  | hs -> [ sprintf "not swept: %s — an identity-insert writes explicit primary keys and is blessed one at a time (press d on it)." (String.concat ", " hs) ])
                    else
                        notes <- []
                        model <- step key.Key model
                        if model.Nav.Done then go <- false
                0
            finally
                console.WriteLine())