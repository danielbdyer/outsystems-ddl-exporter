module Projection.Tests.EvidenceCacheTests

// THE EVIDENCE CACHE (2026-07-10, the manifest program, slice 2 —
// THE_TRANSFER_MANIFEST.md §4.2-§4.5): pure witnesses over a hand-built
// two-target coupled component. Deal references BOTH Buyer and Tag
// (mandatory), so the component's edges must resolve TOGETHER: a Deal row
// dropped by Buyer's unresolved reference never lands, and Tag's re-key
// count honestly shrinks (§4.3). Tag additionally references Realm, so
// widening Tag SPAWNS the Realm decision (§4.5, the fixpoint).
//
// Every match runs through the same Core `reconcileKindWith` the engine's
// run uses — the single-derivation parity claim (§4.4); these witnesses pin
// its consequences exactly, over full rowsets, never a sample.

open Xunit
open Projection.Core
open Projection.Pipeline

let private nm (s: string) : Name = Name.create s |> Result.value
let private kKey (s: string) : SsKey = SsKey.synthesizedComposite "EC_KIND" [ s ] |> Result.value
let private aKey (k: string) (a: string) : SsKey = SsKey.synthesizedComposite "EC_ATTR" [ k; a ] |> Result.value
let private rKey (k: string) (r: string) : SsKey = SsKey.synthesizedComposite "EC_REF" [ k; r ] |> Result.value
let private xKey (k: string) (x: string) : SsKey = SsKey.synthesizedComposite "EC_IDX" [ k; x ] |> Result.value

let private idPk (kind: string) : Attribute =
    { Attribute.create (aKey kind "Id") (nm "Id") Integer with
        Column       = ColumnRealization.create "ID" false |> Result.value
        IsPrimaryKey = true
        IsIdentity   = true
        IsMandatory  = true }

let private textCol (kind: string) (logical: string) : Attribute =
    { Attribute.create (aKey kind logical) (nm logical) Text with
        Column      = ColumnRealization.create (logical.ToUpperInvariant()) false |> Result.value
        Length      = Some 200
        IsMandatory = true }

let private fkCol (kind: string) (logical: string) : Attribute =
    { Attribute.create (aKey kind logical) (nm logical) Integer with
        Column      = ColumnRealization.create (logical.ToUpperInvariant()) false |> Result.value
        IsMandatory = true }

let private uniqueOn (kind: string) (col: string) : Index =
    { Index.create (xKey kind ("By" + col)) (nm (sprintf "IDX_%s_%s" (kind.ToUpperInvariant()) (col.ToUpperInvariant())))
        [ IndexColumn.create (aKey kind col) Ascending ] with
        Uniqueness = Unique }

/// Realm ← Tag ← Deal → Buyer. Deal is the load set; Buyer and Tag escape;
/// Realm escapes only once Tag is widened in.
let private catalog : Catalog =
    let realm =
        { Kind.create (kKey "Realm") (nm "Realm")
            (TableId.create "dbo" "OSUSR_EC_REALM" |> Result.value)
            [ idPk "Realm"; textCol "Realm" "Code" ] with
            Indexes = [ uniqueOn "Realm" "Code" ] }
    let buyer =
        { Kind.create (kKey "Buyer") (nm "Buyer")
            (TableId.create "dbo" "OSUSR_EC_BUYER" |> Result.value)
            [ idPk "Buyer"; textCol "Buyer" "Email" ] with
            Indexes = [ uniqueOn "Buyer" "Email" ] }
    let tag =
        { Kind.create (kKey "Tag") (nm "Tag")
            (TableId.create "dbo" "OSUSR_EC_TAG" |> Result.value)
            [ idPk "Tag"; textCol "Tag" "Label"; fkCol "Tag" "RealmId" ] with
            References = [ Reference.create (rKey "Tag" "Realm") (nm "RealmId") (aKey "Tag" "RealmId") (kKey "Realm") ]
            Indexes = [ uniqueOn "Tag" "Label" ] }
    let deal =
        { Kind.create (kKey "Deal") (nm "Deal")
            (TableId.create "dbo" "OSUSR_EC_DEAL" |> Result.value)
            [ idPk "Deal"; fkCol "Deal" "BuyerId"; fkCol "Deal" "TagId" ] with
            References =
                [ Reference.create (rKey "Deal" "Buyer") (nm "BuyerId") (aKey "Deal" "BuyerId") (kKey "Buyer")
                  Reference.create (rKey "Deal" "Tag") (nm "TagId") (aKey "Deal" "TagId") (kKey "Tag") ] }
    let m =
        Module.create (SsKey.synthesizedComposite "EC_MOD" [ "Trade" ] |> Result.value)
            (nm "Trade") [ realm; buyer; tag; deal ] true []
        |> Result.value
    Catalog.create [ m ] [] |> Result.value

let private row (kind: string) (values: (string * string) list) : StaticRow =
    { Identifier = kKey kind; Values = values |> List.map (fun (c, v) -> nm c, v) |> Map.ofList }

/// Source Buyer 2 (bob) has NO sink match; every Tag matches. Deal 11
/// references the unmatched buyer, so it drops and its (resolvable) Tag
/// reference is moot.
let private cache : EvidenceCache.Cache =
    { SourceRows =
        Map.ofList
            [ kKey "Buyer", [ row "Buyer" [ "Id", "1"; "Email", "alice@x" ]; row "Buyer" [ "Id", "2"; "Email", "bob@x" ] ]
              kKey "Tag",   [ row "Tag" [ "Id", "1"; "Label", "red"; "RealmId", "1" ]; row "Tag" [ "Id", "2"; "Label", "blue"; "RealmId", "1" ] ] ]
      SinkRows =
        Map.ofList
            [ kKey "Buyer", [ row "Buyer" [ "Id", "71"; "Email", "alice@x" ]; row "Buyer" [ "Id", "72"; "Email", "carol@x" ] ]
              kKey "Tag",   [ row "Tag" [ "Id", "81"; "Label", "red"; "RealmId", "9" ]; row "Tag" [ "Id", "82"; "Label", "blue"; "RealmId", "9" ] ] ]
      References =
        Map.ofList
            [ (kKey "Deal", nm "BuyerId"), [ "10", "1"; "11", "2"; "12", "1" ]
              (kKey "Deal", nm "TagId"),   [ "10", "1"; "11", "2"; "12", "2" ] ]
      Uniqueness =
        Map.ofList
            [ (kKey "Buyer", nm "Email"), (2L, 2L)
              (kKey "Tag", nm "Label"), (2L, 2L) ] }

let private loadSet = Set.ofList [ kKey "Deal" ]

let private edges = PeerTransfer.escapingFks catalog loadSet Set.empty

let private evidenceFor (selections: (string * EvidenceCache.Answer) list) (target: string) : EvidenceCache.AnswerEvidence =
    let sel = selections |> List.map (fun (k, a) -> kKey k, a) |> Map.ofList
    (EvidenceCache.componentDeltas cache catalog loadSet Set.empty edges sel).[kKey target]

[<Fact>]
let ``evidence: the two escaping edges are one coupled component (Deal references both targets)`` () =
    let components = EvidenceCache.componentsOf catalog loadSet edges
    Assert.Equal(1, List.length components)
    Assert.Equal(2, components |> List.head |> List.length)

[<Fact>]
let ``evidence: a reconcile delta is exact over the full rowsets — matched pairs, unmatched values, and the uniqueness fact`` () =
    let ev = evidenceFor [ "Buyer", EvidenceCache.Answer.Reconcile (nm "Email"); "Tag", EvidenceCache.Answer.Reconcile (nm "Label") ] "Buyer"
    // deals 10 and 12 re-key through alice; deal 11 references the unmatched bob
    Assert.Equal(2, ev.Delta.RowsRekeyed)
    Assert.Equal(1, ev.Delta.RowsDropped)
    Assert.Equal<(string * string) list>([ "alice@x", "71" ], ev.MatchedPairs)
    Assert.Equal<string list>([ "bob@x" ], ev.UnmatchedValues)
    Assert.Equal(Some true, ev.SinkUnique)

[<Fact>]
let ``evidence: the component recomputes as a unit — a sibling's unresolved reference shrinks this target's re-key count (§4.3)`` () =
    // Buyer reconciled: deal 11 drops on the unmatched buyer, so only 2 of
    // Tag's 3 references land.
    let coupled = evidenceFor [ "Buyer", EvidenceCache.Answer.Reconcile (nm "Email"); "Tag", EvidenceCache.Answer.Reconcile (nm "Label") ] "Tag"
    Assert.Equal(2, coupled.Delta.RowsRekeyed)
    Assert.Equal(0, coupled.Delta.RowsDropped)
    // Buyer pinned instead: every deal survives, and Tag re-keys all 3.
    let released = evidenceFor [ "Buyer", EvidenceCache.Answer.Pin None; "Tag", EvidenceCache.Answer.Reconcile (nm "Label") ] "Tag"
    Assert.Equal(3, released.Delta.RowsRekeyed)

[<Fact>]
let ``evidence: pin re-keys every non-blank reference and drops none — exact without choosing the anchor`` () =
    let ev = evidenceFor [ "Buyer", EvidenceCache.Answer.Pin None; "Tag", EvidenceCache.Answer.Reconcile (nm "Label") ] "Buyer"
    Assert.Equal(3, ev.Delta.RowsRekeyed)
    Assert.Equal(0, ev.Delta.RowsDropped)

[<Fact>]
let ``evidence: widen spawns exactly the newly-escaping targets (the fixpoint §4.5)`` () =
    let ev = evidenceFor [ "Buyer", EvidenceCache.Answer.Reconcile (nm "Email"); "Tag", EvidenceCache.Answer.Widen ] "Tag"
    Assert.Equal(2, ev.Delta.RowsEnteringScope)
    Assert.Equal(1, ev.Delta.TablesTouched)
    Assert.Equal<SsKey list>([ kKey "Realm" ], ev.Delta.SpawnedKeys)
    Assert.Equal<SsKey list>([ kKey "Tag" ], ev.Delta.ResolvedKeys)

[<Fact>]
let ``evidence: a blank reference neither re-keys nor drops`` () =
    let withBlank =
        { cache with
            References =
                cache.References
                |> Map.add (kKey "Deal", nm "TagId") [ "10", "1"; "11", "2"; "12", "2"; "13", "" ]
                |> Map.add (kKey "Deal", nm "BuyerId") [ "10", "1"; "11", "2"; "12", "1"; "13", "1" ] }
    let sel = Map.ofList [ kKey "Buyer", EvidenceCache.Answer.Pin None; kKey "Tag", EvidenceCache.Answer.Reconcile (nm "Label") ]
    let ev = (EvidenceCache.componentDeltas withBlank catalog loadSet Set.empty edges sel).[kKey "Tag"]
    // deal 13's blank TagId is neither re-keyed nor dropped; the other 3 land.
    Assert.Equal(3, ev.Delta.RowsRekeyed)
    Assert.Equal(0, ev.Delta.RowsDropped)

[<Fact>]
let ``evidence: perAnswerDeltas carries a real delta for every candidate answer at equal fidelity`` () =
    let per = EvidenceCache.perAnswerDeltas cache catalog loadSet Set.empty edges Map.empty
    let buyerAnswers = per.[kKey "Buyer"]
    // one Reconcile per candidate column, the unchosen Pin, Widen, and the
    // static-lookup twin — all present, none a placeholder.
    Assert.True(buyerAnswers |> Map.containsKey (EvidenceCache.Answer.Reconcile (nm "Email")))
    Assert.True(buyerAnswers |> Map.containsKey (EvidenceCache.Answer.Pin None))
    Assert.True(buyerAnswers |> Map.containsKey EvidenceCache.Answer.Widen)
    Assert.True(buyerAnswers |> Map.containsKey (EvidenceCache.Answer.StaticLookup (nm "Email")))
    // the static-lookup twin matches by the same mechanics: identical counts.
    let rec_ = buyerAnswers.[EvidenceCache.Answer.Reconcile (nm "Email")]
    let stat = buyerAnswers.[EvidenceCache.Answer.StaticLookup (nm "Email")]
    Assert.Equal(rec_.Delta.RowsRekeyed, stat.Delta.RowsRekeyed)
    Assert.Equal(rec_.Delta.RowsDropped, stat.Delta.RowsDropped)

[<Fact>]
let ``evidence: determinism — permuted cache row order yields identical evidence`` () =
    let permuted =
        { cache with
            SourceRows = cache.SourceRows |> Map.map (fun _ rows -> List.rev rows)
            References = cache.References |> Map.map (fun _ pairs -> List.rev pairs) }
    // NOTE: sink rows stay PK-ascending — `reconcileKindWith` documents the
    // oldest-row-wins tiebreaker over PK-ascending sink input; the cache fill
    // reads them in table order and the derivation never re-sorts them.
    let a = EvidenceCache.perAnswerDeltas cache catalog loadSet Set.empty edges Map.empty
    let b = EvidenceCache.perAnswerDeltas permuted catalog loadSet Set.empty edges Map.empty
    let deltasOf (m: Map<SsKey, Map<EvidenceCache.Answer, EvidenceCache.AnswerEvidence>>) =
        m |> Map.map (fun _ answers -> answers |> Map.map (fun _ ev -> ev.Delta))
    Assert.Equal<Map<SsKey, Map<EvidenceCache.Answer, EvidenceCache.ForecastDelta>>>(deltasOf a, deltasOf b)
