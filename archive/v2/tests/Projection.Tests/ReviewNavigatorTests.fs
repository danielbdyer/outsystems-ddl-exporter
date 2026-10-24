module Projection.Tests.ReviewNavigatorTests

// THE REVIEW WORKBENCH (2026-07-10, the manifest program, slice 3): pure
// witnesses over the same coupled fixture the EvidenceCache tests pin —
// Deal references Buyer and Tag; Tag references Realm. The laws under test:
// the reducer is TOTAL over every key; Space cycles the decision under the
// cursor and the COUPLED sibling's counts recompute in the rendered rows;
// the paired single-traversal index always addresses real nodes (the
// cursor's meaning cannot drift from what is drawn); the write gesture
// materializes ONLY the vocabulary the engine already honors.

open System
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Cli

let private nm (s: string) : Name = Name.create s |> Result.value
let private kKey (s: string) : SsKey = SsKey.synthesizedComposite "RV_KIND" [ s ] |> Result.value
let private aKey (k: string) (a: string) : SsKey = SsKey.synthesizedComposite "RV_ATTR" [ k; a ] |> Result.value
let private rKey (k: string) (r: string) : SsKey = SsKey.synthesizedComposite "RV_REF" [ k; r ] |> Result.value
let private xKey (k: string) (x: string) : SsKey = SsKey.synthesizedComposite "RV_IDX" [ k; x ] |> Result.value

let private idPk (kind: string) : Attribute =
    { Attribute.create (aKey kind "Id") (nm "Id") Integer with
        Column = ColumnRealization.create "ID" false |> Result.value
        IsPrimaryKey = true; IsIdentity = true; IsMandatory = true }

let private textCol (kind: string) (logical: string) : Attribute =
    { Attribute.create (aKey kind logical) (nm logical) Text with
        Column = ColumnRealization.create (logical.ToUpperInvariant()) false |> Result.value
        Length = Some 200; IsMandatory = true }

let private fkCol (kind: string) (logical: string) : Attribute =
    { Attribute.create (aKey kind logical) (nm logical) Integer with
        Column = ColumnRealization.create (logical.ToUpperInvariant()) false |> Result.value
        IsMandatory = true }

let private uniqueOn (kind: string) (col: string) : Index =
    { Index.create (xKey kind ("By" + col)) (nm (sprintf "IDX_%s_%s" kind col))
        [ IndexColumn.create (aKey kind col) Ascending ] with
        Uniqueness = Unique }

let private catalog : Catalog =
    let realm =
        { Kind.create (kKey "Realm") (nm "Realm") (TableId.create "dbo" "OSUSR_RV_REALM" |> Result.value)
            [ idPk "Realm"; textCol "Realm" "Code" ] with
            Indexes = [ uniqueOn "Realm" "Code" ] }
    let buyer =
        { Kind.create (kKey "Buyer") (nm "Buyer") (TableId.create "dbo" "OSUSR_RV_BUYER" |> Result.value)
            [ idPk "Buyer"; textCol "Buyer" "Email" ] with
            Indexes = [ uniqueOn "Buyer" "Email" ] }
    let tag =
        { Kind.create (kKey "Tag") (nm "Tag") (TableId.create "dbo" "OSUSR_RV_TAG" |> Result.value)
            [ idPk "Tag"; textCol "Tag" "Label"; fkCol "Tag" "RealmId" ] with
            References = [ Reference.create (rKey "Tag" "Realm") (nm "RealmId") (aKey "Tag" "RealmId") (kKey "Realm") ]
            Indexes = [ uniqueOn "Tag" "Label" ] }
    let deal =
        { Kind.create (kKey "Deal") (nm "Deal") (TableId.create "dbo" "OSUSR_RV_DEAL" |> Result.value)
            [ idPk "Deal"; fkCol "Deal" "BuyerId"; fkCol "Deal" "TagId" ] with
            References =
                [ Reference.create (rKey "Deal" "Buyer") (nm "BuyerId") (aKey "Deal" "BuyerId") (kKey "Buyer")
                  Reference.create (rKey "Deal" "Tag") (nm "TagId") (aKey "Deal" "TagId") (kKey "Tag") ] }
    let m =
        Module.create (SsKey.synthesizedComposite "RV_MOD" [ "Trade" ] |> Result.value)
            (nm "Trade") [ realm; buyer; tag; deal ] true []
        |> Result.value
    Catalog.create [ m ] [] |> Result.value

let private row (kind: string) (values: (string * string) list) : StaticRow =
    { Identifier = kKey kind; Values = values |> List.map (fun (c, v) -> nm c, Some v) |> Map.ofList }

let private cache : EvidenceCache.Cache =
    { SourceRows =
        Map.ofList
            [ kKey "Buyer", [ row "Buyer" [ "Id", "1"; "Email", "alice@x" ]; row "Buyer" [ "Id", "2"; "Email", "bob@x" ] ]
              kKey "Tag",   [ row "Tag" [ "Id", "1"; "Label", "red"; "RealmId", "1" ]; row "Tag" [ "Id", "2"; "Label", "blue"; "RealmId", "1" ] ] ]
      SinkRows =
        Map.ofList
            [ kKey "Buyer", [ row "Buyer" [ "Id", "71"; "Email", "alice@x" ] ]
              kKey "Tag",   [ row "Tag" [ "Id", "81"; "Label", "red"; "RealmId", "9" ]; row "Tag" [ "Id", "82"; "Label", "blue"; "RealmId", "9" ] ] ]
      References =
        Map.ofList
            [ (kKey "Deal", nm "BuyerId"), [ "10", "1"; "11", "2"; "12", "1" ]
              (kKey "Deal", nm "TagId"),   [ "10", "1"; "11", "2"; "12", "2" ] ]
      Uniqueness =
        Map.ofList [ (kKey "Buyer", nm "Email"), (1L, 1L); (kKey "Tag", nm "Label"), (2L, 2L) ] }

let private loadSet = Set.ofList [ kKey "Deal" ]
let private edges = PeerTransfer.escapingFks catalog loadSet Set.empty

let private bench : ReviewNavigator.Workbench =
    { Flow = "golden"
      ConfigPath = "projection.json"
      Catalog = catalog
      LoadSet = loadSet
      Reconciled = Set.empty
      Components = EvidenceCache.componentsOf catalog loadSet edges
      Cache = cache
      Tables = [ "Trade.Deal" ]
      Reconcile = []
      SupportingScope = []
      Acts = []
      ModeSignoff = []
      ActSignoff = [] }

let private freshModel () : ReviewNavigator.Model =
    let tree, targets = ReviewNavigator.render bench Map.empty Map.empty
    { Nav = Navigator.init 0 tree
      Bench = bench
      Decisions = Map.empty
      Blessings = Map.empty
      Targets = targets
      Dirty = false
      PendingQuit = false }

let private allKeys : ConsoleKey [] =
    Enum.GetValues(typeof<ConsoleKey>) :?> ConsoleKey []

[<Fact>]
let ``review: the paired traversal's index addresses only real nodes — the cursor's meaning cannot drift from what is drawn`` () =
    let m = freshModel ()
    Assert.Equal(2, m.Targets.Count)   // Buyer and Tag, one decision block each
    for KeyValue (path, _) in m.Targets do
        Assert.True(Navigator.nodeAt m.Nav.Tree path |> Option.isSome,
                    sprintf "index path %A addresses no node in the rendered tree" path)

[<Fact>]
let ``review: step is TOTAL over every ConsoleKey from every reachable state (never throws; the cursor never leaves the tree)`` () =
    let starts =
        [ freshModel ()
          ReviewNavigator.step ConsoleKey.DownArrow (freshModel ())
          ReviewNavigator.step ConsoleKey.Spacebar (ReviewNavigator.step ConsoleKey.DownArrow (freshModel ()))
          { freshModel () with Dirty = true }
          { freshModel () with Dirty = true; PendingQuit = true } ]
    for start in starts do
        for k in allKeys do
            let m = ReviewNavigator.step k start
            Assert.True(Navigator.nodeAt m.Nav.Tree m.Nav.Path |> Option.isSome,
                        sprintf "key %A left the cursor at %A, which addresses no node" k m.Nav.Path)

[<Fact>]
let ``review: Space on a decision selects the first answer and marks the model dirty; the selection shows in the rendered rows`` () =
    let m0 = freshModel ()
    let onFirst = ReviewNavigator.step ConsoleKey.DownArrow (ReviewNavigator.step ConsoleKey.DownArrow m0)
    // walk down until the cursor addresses a decision block
    let rec toDecision (m: ReviewNavigator.Model) (fuel: int) =
        if fuel = 0 then m
        elif (ReviewNavigator.targetAt m |> Option.isSome) then m
        else toDecision (ReviewNavigator.step ConsoleKey.DownArrow m) (fuel - 1)
    let atDecision = toDecision onFirst 10
    Assert.True(ReviewNavigator.targetAt atDecision |> Option.isSome, "the cursor never reached a decision block")
    let selected = ReviewNavigator.step ConsoleKey.Spacebar atDecision
    Assert.True(selected.Dirty)
    Assert.Equal(1, selected.Decisions.Count)

[<Fact>]
let ``review: selecting an answer on one edge recomputes its coupled sibling — the workbench never shows a stale count (§4.3)`` () =
    // Undecided: siblings evaluate under the optimistic default, so Tag's
    // reconcile row re-keys all 3 references.
    let tree0, _ = ReviewNavigator.render bench Map.empty Map.empty
    // Buyer reconciled by Email (bob unmatched): deal 11 drops, and Tag's
    // reconcile row must now read 2, not 3.
    let treeSelected, _ = ReviewNavigator.render bench (Map.ofList [ kKey "Buyer", EvidenceCache.Answer.Reconcile (nm "Email") ]) Map.empty
    let textOf (v: View.View) = (View.toJson v).ToJsonString()
    Assert.NotEqual<string>(textOf tree0, textOf treeSelected)
    Assert.Contains("\"2\"", textOf treeSelected)

[<Fact>]
let ``review: q with unsaved selections asks once, then quits; q clean quits at once`` () =
    let dirty = { freshModel () with Dirty = true }
    let asked = ReviewNavigator.step ConsoleKey.Q dirty
    Assert.True(asked.PendingQuit)
    Assert.False(asked.Nav.Done)
    let quit = ReviewNavigator.step ConsoleKey.Q asked
    Assert.True(quit.Nav.Done)
    let clean = freshModel ()
    Assert.True((ReviewNavigator.step ConsoleKey.Q clean).Nav.Done)

[<Fact>]
let ``review: the write gesture materializes only the vocabulary the engine already honors`` () =
    let decisions =
        Map.ofList
            [ kKey "Buyer", EvidenceCache.Answer.Reconcile (nm "Email")
              kKey "Tag", EvidenceCache.Answer.Widen ]
    let reconciles, widens, statics, byHand = ReviewNavigator.toConfigEdits bench decisions
    Assert.Equal<string list>([ "Trade.Buyer:Email" ], reconciles)
    Assert.Equal<string list>([ "Trade.Tag" ], widens)
    Assert.Empty(statics)
    Assert.Empty(byHand)
    // a pinned selection cannot be written without its key: the instruction is
    // named, never silent.
    let withPin = Map.add (kKey "Buyer") (EvidenceCache.Answer.Pin None) decisions
    let _, _, _, byHand2 = ReviewNavigator.toConfigEdits bench withPin
    Assert.Single(byHand2) |> ignore
    Assert.Contains("by hand", List.head byHand2)

[<Fact>]
let ``review: cycling wraps through every candidate answer and returns to the first`` () =
    let m0 = freshModel ()
    let rec toDecision (m: ReviewNavigator.Model) (fuel: int) =
        if fuel = 0 || (ReviewNavigator.targetAt m |> Option.isSome) then m
        else toDecision (ReviewNavigator.step ConsoleKey.DownArrow m) (fuel - 1)
    let atDecision = toDecision m0 10
    match ReviewNavigator.targetAt atDecision with
    | Some (ReviewTarget.Edge target) ->
        let componentEdges = bench.Components |> List.find (fun es -> es |> List.exists (fun e -> e.Target = target))
        let candidates = EvidenceCache.candidateAnswers componentEdges target
        // from undecided, N presses land on the Nth candidate; one more wraps
        // back to the first.
        let afterLast =
            (atDecision, [ 1 .. candidates.Length ])
            ||> List.fold (fun m _ -> ReviewNavigator.step ConsoleKey.Spacebar m)
        Assert.Equal<EvidenceCache.Answer option>(Some (List.last candidates), afterLast.Decisions |> Map.tryFind target)
        let wrapped = ReviewNavigator.step ConsoleKey.Spacebar afterLast
        Assert.Equal<EvidenceCache.Answer option>(Some (List.head candidates), wrapped.Decisions |> Map.tryFind target)
    | _ -> failwith "the cursor never reached a decision block"
// -- the consent ledger in the workbench (2026-07-10, slice 4a) --------------

let private actFp : ActConsent.ActFingerprint =
    ActConsent.effectFingerprint
        { Token = "match:Trade.Buyer"; Resolution = "reconcile:Email"
          MatchedPairs = [ "alice@x", "71" ]; UnmatchedValues = []
          SinkTotal = 1L; SinkDistinct = 1L; PlannedCount = 1 }

let private benchWithActs : ReviewNavigator.Workbench =
    { bench with
        Acts =
            [ { Token = "wipe:Trade.Deal"
                Statement = "Every row of Trade.Deal on the target is deleted child-first before the reload — a target row absent from the source is removed, not preserved."
                Fingerprint = Some (ActConsent.populationFingerprint "1" "42" 42)
                Sweepable = true }
              { Token = "identity-insert:Trade.Buyer"
                Statement = "Source primary-key values are written directly into Trade.Buyer's identity column under SET IDENTITY_INSERT — a key the target already minted for its own row can collide."
                Fingerprint = Some actFp
                Sweepable = false } ] }

[<Fact>]
let ``review: the paired traversal indexes every act block, and step stays TOTAL with acts present`` () =
    let tree, targets = ReviewNavigator.render benchWithActs Map.empty Map.empty
    let actTargets = targets |> Map.toList |> List.choose (fun (p, t) -> match t with ReviewTarget.Act tok -> Some (p, tok) | _ -> None)
    Assert.Equal(2, actTargets.Length)
    for (path, _) in actTargets do
        Assert.True(Navigator.nodeAt tree path |> Option.isSome, sprintf "act index path %A addresses no node" path)
    let m0 : ReviewNavigator.Model =
        { Nav = Navigator.init 0 tree; Bench = benchWithActs; Decisions = Map.empty
          Blessings = Map.empty; Targets = targets; Dirty = false; PendingQuit = false }
    for k in allKeys do
        let m = ReviewNavigator.step k m0
        Assert.True(Navigator.nodeAt m.Nav.Tree m.Nav.Path |> Option.isSome,
                    sprintf "key %A left the cursor at %A, which addresses no node" k m.Nav.Path)

[<Fact>]
let ``review: an act renders blessed ONLY when the fingerprint on file equals the one derived this pass`` () =
    let textOf (bl: Map<string, ActConsent.ActFingerprint>) =
        let tree, _ = ReviewNavigator.render benchWithActs Map.empty bl
        (View.toJson tree).ToJsonString()
    // nothing on file: open — the standing line says how to bless.
    Assert.Contains("not blessed. Press d to bless it at population:1:42:42", textOf Map.empty)
    // the exact fingerprint on file: blessed.
    let matching = Map.ofList [ "wipe:Trade.Deal", ActConsent.populationFingerprint "1" "42" 42 ]
    Assert.Contains("blessed at population:1:42:42", textOf matching)
    // a DIFFERENT fingerprint on file: re-opened, never silently accepted.
    let drifted = Map.ofList [ "wipe:Trade.Deal", ActConsent.populationFingerprint "1" "43" 43 ]
    Assert.Contains("re-opened: what this act would do has changed since it was blessed", textOf drifted)
