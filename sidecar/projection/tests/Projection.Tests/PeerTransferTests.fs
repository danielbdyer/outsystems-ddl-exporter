module Projection.Tests.PeerTransferTests

// The peer (A→A) leg's pure gates (2026-07-06, the partial-transfer
// readiness program; `PeerTransfer.fs`): the SS_KEY-keyed shape gate, the
// escaping-FK detector + strategy proposals, the subset-FK gate's
// refuse/downgrade contract, `Transfer.resolveLoadSet`'s refusal semantics
// (previously only exercised inside docker canaries), and the two new
// `Preflight` classification axes.

open Xunit
open Projection.Core
open Projection.Pipeline

// --- the two espace-variant contracts (same SsKeys, different physical) -----

let private nm (s: string) : Name = Name.create s |> Result.value
let private kKey (s: string) : SsKey = SsKey.synthesizedComposite "PEER_KIND" [ s ] |> Result.value
let private aKey (k: string) (a: string) : SsKey = SsKey.synthesizedComposite "PEER_ATTR" [ k; a ] |> Result.value
let private rKey (k: string) (r: string) : SsKey = SsKey.synthesizedComposite "PEER_REF" [ k; r ] |> Result.value
let private xKey (k: string) (x: string) : SsKey = SsKey.synthesizedComposite "PEER_IDX" [ k; x ] |> Result.value

let private idPk (kind: string) : Attribute =
    { Attribute.create (aKey kind "Id") (nm "Id") Integer with
        Column       = ColumnRealization.create "ID" false |> Result.value
        IsPrimaryKey = true
        IsIdentity   = true
        IsMandatory  = true }

let private textCol (kind: string) (logical: string) (mandatory: bool) : Attribute =
    { Attribute.create (aKey kind logical) (nm logical) Text with
        Column      = ColumnRealization.create (logical.ToUpperInvariant()) (not mandatory) |> Result.value
        Length      = Some 200
        IsMandatory = mandatory }

let private fkCol (kind: string) (logical: string) (mandatory: bool) : Attribute =
    { Attribute.create (aKey kind logical) (nm logical) Integer with
        Column      = ColumnRealization.create (logical.ToUpperInvariant()) (not mandatory) |> Result.value
        IsMandatory = mandatory }

/// One cell of the model at a physical prefix: City (unique NAME — the
/// reconcile candidate), Customer (mandatory CityId FK→City, optional
/// SecondCityId FK→City, unique EMAIL), Order (mandatory CustomerId
/// FK→Customer).
let private cellAt (prefix: string) : Catalog =
    let city =
        { Kind.create (kKey "City") (nm "City")
            (TableId.create "dbo" (prefix + "CITY") |> Result.value)
            [ idPk "City"; textCol "City" "Name" true ] with
            Indexes =
                [ { Index.create (xKey "City" "ByName") (nm "IDX_CITY_NAME")
                      [ IndexColumn.create (aKey "City" "Name") Ascending ] with
                      Uniqueness = Unique } ] }
    let customer =
        { Kind.create (kKey "Customer") (nm "Customer")
            (TableId.create "dbo" (prefix + "CUSTOMER") |> Result.value)
            [ idPk "Customer"
              textCol "Customer" "Email" true
              fkCol "Customer" "CityId" true
              fkCol "Customer" "SecondCityId" false ] with
            References =
                [ Reference.create (rKey "Customer" "City") (nm "CityId") (aKey "Customer" "CityId") (kKey "City")
                  Reference.create (rKey "Customer" "SecondCity") (nm "SecondCityId") (aKey "Customer" "SecondCityId") (kKey "City") ]
            Indexes =
                [ { Index.create (xKey "Customer" "ByEmail") (nm "IDX_CUSTOMER_EMAIL")
                      [ IndexColumn.create (aKey "Customer" "Email") Ascending ] with
                      Uniqueness = Unique } ] }
    let order =
        { Kind.create (kKey "Order") (nm "Order")
            (TableId.create "dbo" (prefix + "ORDER") |> Result.value)
            [ idPk "Order"; fkCol "Order" "CustomerId" true ] with
            References =
                [ Reference.create (rKey "Order" "Customer") (nm "CustomerId") (aKey "Order" "CustomerId") (kKey "Customer") ] }
    let m =
        Module.create (SsKey.synthesizedComposite "PEER_MOD" [ "AppCore" ] |> Result.value)
            (nm "AppCore") [ city; customer; order ] true []
        |> Result.value
    Catalog.create [ m ] [] |> Result.value

let private srcCell  = cellAt "OSUSR_AAA_"
let private sinkCell = cellAt "OSUSR_BBB_"

let private keys (names: string list) : Set<SsKey> = names |> List.map kKey |> Set.ofList

// --- Transfer.resolveLoadSet (the previously-untested resolver) -------------

[<Fact>]
let ``resolveLoadSet: an empty declaration is None (all kinds)`` () =
    match Transfer.resolveLoadSet srcCell [] with
    | Ok None -> ()
    | other -> Assert.Fail(sprintf "expected Ok None, got %A" other)

[<Fact>]
let ``resolveLoadSet: logical names resolve case-insensitively to SsKeys`` () =
    match Transfer.resolveLoadSet srcCell [ "customer"; "CITY" ] with
    | Ok (Some set) -> Assert.Equal<Set<SsKey>>(keys [ "Customer"; "City" ], set)
    | other -> Assert.Fail(sprintf "expected the two keys, got %A" other)

[<Fact>]
let ``resolveLoadSet: every unknown name is aggregated into one named refusal`` () =
    match Transfer.resolveLoadSet srcCell [ "Customer"; "Ghost"; "Phantom" ] with
    | Error [ e ] ->
        Assert.Equal("transfer.tablesUnknown", e.Code)
        Assert.Contains("Ghost", e.Message)
        Assert.Contains("Phantom", e.Message)
        Assert.DoesNotContain("Customer", e.Message)
    | other -> Assert.Fail(sprintf "expected the aggregated refusal, got %A" other)

[<Fact>]
let ``resolveLoadSet: a duplicate logical name refuses as ambiguous; the Module.Entity form disambiguates`` () =
    // 2026-07-06 (adversarial MEDIUM #6): the prior Map.ofList silently
    // last-won a duplicate name across modules.
    let dupCity =
        { Kind.create (SsKey.synthesizedComposite "PEER_KIND" [ "City2" ] |> Result.value) (nm "City")
            (TableId.create "dbo" "OSUSR_ZZZ_CITY" |> Result.value)
            [ idPk "City2" ] with Attributes = [ idPk "City2" ] }
    let m2 =
        Module.create (SsKey.synthesizedComposite "PEER_MOD" [ "Ops" ] |> Result.value)
            (nm "Ops") [ dupCity ] true []
        |> Result.value
    let twoModules = Catalog.create [ srcCell.Modules.Head; m2 ] [] |> Result.value
    match Transfer.resolveLoadSet twoModules [ "City" ] with
    | Error [ e ] ->
        Assert.Equal("transfer.tablesAmbiguous", e.Code)
        Assert.Contains("AppCore.City", e.Message)
        Assert.Contains("Ops.City", e.Message)
    | other -> Assert.Fail(sprintf "expected the ambiguity refusal, got %A" other)
    match Transfer.resolveLoadSet twoModules [ "AppCore.City" ] with
    | Ok (Some set) -> Assert.Equal<Set<SsKey>>(keys [ "City" ], set)
    | other -> Assert.Fail(sprintf "expected the qualified form to resolve, got %A" other)

// --- the escaping-FK detector ------------------------------------------------

[<Fact>]
let ``escapingFks: a subset whose FK closure is inside it has no escapes`` () =
    Assert.Empty(PeerTransfer.escapingFks srcCell (keys [ "City"; "Customer"; "Order" ]) Set.empty)

[<Fact>]
let ``escapingFks: an in-subset child names every out-of-subset parent, with reconcile candidates`` () =
    let escapes = PeerTransfer.escapingFks srcCell (keys [ "Customer" ]) Set.empty
    Assert.Equal(2, escapes.Length)   // CityId (mandatory) + SecondCityId (optional), both → City
    for e in escapes do
        Assert.Equal("Customer", Name.value e.KindName)
        Assert.Equal("City", Name.value e.TargetName)
        // The proposal: City's single-column unique non-PK index → Name.
        Assert.Equal<string list>([ "Name" ], e.CandidateReconcileColumns |> List.map Name.value)
    let byColumn = escapes |> List.map (fun e -> Name.value e.Column, e.Nullable) |> Map.ofList
    Assert.False(byColumn.["CityId"])        // mandatory FK — a hard escape
    Assert.True(byColumn.["SecondCityId"])   // optional FK — rows with NULL pass

[<Fact>]
let ``escapingFks: a reconciled target is strategized — no escape`` () =
    let escapes = PeerTransfer.escapingFks srcCell (keys [ "Customer" ]) (keys [ "City" ])
    Assert.Empty(escapes)

[<Fact>]
let ``escapingFks: the chain stops at the subset boundary (Order alone escapes to Customer, not City)`` () =
    let escapes = PeerTransfer.escapingFks srcCell (keys [ "Order" ]) Set.empty
    Assert.Equal(1, escapes.Length)
    Assert.Equal("Customer", Name.value escapes.Head.TargetName)
    // Customer's unique non-PK column proposes the re-key.
    Assert.Equal<string list>([ "Email" ], escapes.Head.CandidateReconcileColumns |> List.map Name.value)

// --- the subset-FK gate's refuse/downgrade contract ---------------------------

[<Fact>]
let ``subsetFkGate: a preview never refuses; a live run with escapes refuses by name — --allow-drops does NOT bypass`` () =
    // 2026-07-06 (adversarial CRITICAL #2): the --allow-drops bypass is
    // GONE — nothing on the write plane drops an escaping FK; the rows
    // would land carrying SOURCE-environment surrogates (silent
    // cross-wiring). The gate refuses until the target is reconciled or
    // the subset widened.
    let escapes = PeerTransfer.escapingFks srcCell (keys [ "Customer" ]) Set.empty
    Assert.True(Result.isSuccess (PeerTransfer.subsetFkGate false escapes))
    Assert.True(Result.isSuccess (PeerTransfer.subsetFkGate true []))
    match PeerTransfer.subsetFkGate true escapes with
    | Error [ e ] ->
        Assert.Equal("transfer.peer.subsetFkEscapes", e.Code)
        Assert.Contains("City", e.Message)
        Assert.Contains("SOURCE-environment references", e.Message)
    | other -> Assert.Fail(sprintf "expected the named refusal, got %A" other)

[<Fact>]
let ``narrateEscapes: proposals carry the RESOLVABLE Module.Entity:Column reconcile form`` () =
    // Adversarial MEDIUM #8: the bare logical name resolves by PHYSICAL
    // table only — the proposal must be copy-pasteable.
    let escapes = PeerTransfer.escapingFks srcCell (keys [ "Customer" ]) Set.empty
    let lines = PeerTransfer.narrateEscapes escapes
    Assert.True(lines |> List.exists (fun l -> l.Contains "AppCore.City:Name" && l.Contains "escapes the subset"),
                sprintf "expected the Module.Entity:Column form, got %A" lines)

// --- the shape gate -----------------------------------------------------------

[<Fact>]
let ``shapeGate: two espace-variant cells of one model are one shape (no blocking, no advisory)`` () =
    match PeerTransfer.shapeGate None srcCell sinkCell with
    | Ok [] -> ()
    | other -> Assert.Fail(sprintf "expected a clean pass, got %A" other)

let private mapKind (name: string) (f: Kind -> Kind) (c: Catalog) : Catalog =
    { c with
        Modules =
            c.Modules
            |> List.map (fun m ->
                { m with Kinds = m.Kinds |> List.map (fun k -> if Name.value k.Name = name then f k else k) }) }

let private dropKind (name: string) (c: Catalog) : Catalog =
    { c with
        Modules =
            c.Modules
            |> List.map (fun m -> { m with Kinds = m.Kinds |> List.filter (fun k -> Name.value k.Name <> name) }) }

[<Fact>]
let ``shapeGate: a source kind missing from the sink blocks — inside scope only`` () =
    let sinkNoOrder = dropKind "Order" sinkCell
    match PeerTransfer.shapeGate (Some (keys [ "Order" ])) srcCell sinkNoOrder with
    | Error [ e ] ->
        Assert.Equal("transfer.peer.shapeDivergence", e.Code)
        Assert.Contains("Order", e.Message)
    | other -> Assert.Fail(sprintf "expected the shape refusal, got %A" other)
    // The same divergence OUTSIDE the scoped subset does not block the run.
    match PeerTransfer.shapeGate (Some (keys [ "City"; "Customer" ])) srcCell sinkNoOrder with
    | Ok _ -> ()
    | other -> Assert.Fail(sprintf "expected the scoped gate to pass, got %A" other)

[<Fact>]
let ``shapeGate: a source-only attribute blocks (its values have no landing column)`` () =
    let sinkNoEmail =
        sinkCell
        |> mapKind "Customer" (fun k ->
            { k with
                Attributes = k.Attributes |> List.filter (fun a -> Name.value a.Name <> "Email")
                Indexes = [] })
    match PeerTransfer.shapeGate (Some (keys [ "Customer" ])) srcCell sinkNoEmail with
    | Error [ e ] ->
        Assert.Equal("transfer.peer.shapeDivergence", e.Code)
        Assert.Contains("Email", e.Message)
    | other -> Assert.Fail(sprintf "expected the shape refusal, got %A" other)

[<Fact>]
let ``shapeGate: a data-type divergence blocks; a widened length is advisory`` () =
    let typeChanged =
        sinkCell
        |> mapKind "Customer" (fun k ->
            { k with
                Attributes =
                    k.Attributes
                    |> List.map (fun a -> if Name.value a.Name = "Email" then { a with Type = Integer; Length = None } else a) })
    match PeerTransfer.shapeGate (Some (keys [ "Customer" ])) srcCell typeChanged with
    | Error [ e ] -> Assert.Contains("data type", e.Message)
    | other -> Assert.Fail(sprintf "expected the type refusal, got %A" other)
    let widened =
        sinkCell
        |> mapKind "Customer" (fun k ->
            { k with
                Attributes =
                    k.Attributes
                    |> List.map (fun a -> if Name.value a.Name = "Email" then { a with Length = Some 400 } else a) })
    match PeerTransfer.shapeGate (Some (keys [ "Customer" ])) srcCell widened with
    | Ok advisories -> Assert.True(advisories |> List.exists (fun l -> l.Contains "wider"), sprintf "expected the widening advisory, got %A" advisories)
    | other -> Assert.Fail(sprintf "expected an advisory pass, got %A" other)

[<Fact>]
let ``shapeGate: nullable-in-source but NOT-NULL-in-sink blocks; the reverse is advisory`` () =
    // The verdict judges the COLUMN plane (`ColumnRealization.IsNullable`) —
    // the same plane the Nullability facet fires on (adversarial LOW #12).
    let tightened =
        sinkCell
        |> mapKind "Customer" (fun k ->
            { k with
                Attributes =
                    k.Attributes
                    |> List.map (fun a ->
                        if Name.value a.Name = "SecondCityId" then
                            { a with IsMandatory = true; Column = ColumnRealization.create "SECONDCITYID" false |> Result.value }
                        else a) })
    match PeerTransfer.shapeGate (Some (keys [ "Customer" ])) srcCell tightened with
    | Error [ e ] -> Assert.Contains("NOT NULL in the sink", e.Message)
    | other -> Assert.Fail(sprintf "expected the tightening refusal, got %A" other)
    let loosened =
        sinkCell
        |> mapKind "Customer" (fun k ->
            { k with
                Attributes =
                    k.Attributes
                    |> List.map (fun a ->
                        if Name.value a.Name = "CityId" then
                            { a with IsMandatory = false; Column = ColumnRealization.create "CITYID" true |> Result.value }
                        else a) })
    match PeerTransfer.shapeGate (Some (keys [ "Customer" ])) srcCell loosened with
    | Ok advisories -> Assert.True(advisories |> List.exists (fun l -> l.Contains "permissive"), sprintf "expected the loosening advisory, got %A" advisories)
    | other -> Assert.Fail(sprintf "expected an advisory pass, got %A" other)

// --- the affirmative shape tiers (2026-07-08, the board-clarity program) -----

[<Fact>]
let ``shapeVerdict.Proven: a clean identical pair affirms every tier (entity/column names, definitions, FKs, indexes, facets)`` () =
    let v = PeerTransfer.shapeVerdict (Some (keys [ "City"; "Customer" ])) srcCell sinkCell
    Assert.Empty v.Blocking
    // The whole ladder holds — the operator sees WHAT matched, not just silence.
    for tier in [ "entity names"; "column names"; "column definitions (type/length/precision/nullability)"; "foreign keys"; "indexes"; "entity-level facets" ] do
        Assert.Contains(tier, v.Proven)

[<Fact>]
let ``shapeVerdict.Proven: an index-only difference is advisory, keeps the deep tiers proven, and drops 'indexes' from Proven`` () =
    // Drop the sink City's unique NAME index — a pure index difference.
    let indexDropped = sinkCell |> mapKind "City" (fun k -> { k with Indexes = [] })
    let v = PeerTransfer.shapeVerdict (Some (keys [ "City"; "Customer" ])) srcCell indexDropped
    Assert.Empty v.Blocking
    Assert.NotEmpty v.Advisory
    // The advisory names the performance-only, no-action-needed character.
    Assert.True(v.Advisory |> List.exists (fun l -> l.Contains "indexes differ" && l.Contains "performance-only"), sprintf "expected the index advisory with its action language, got %A" v.Advisory)
    // The affirmative tiers still say the deeper agreement held...
    for tier in [ "entity names"; "column names"; "column definitions (type/length/precision/nullability)"; "foreign keys" ] do
        Assert.Contains(tier, v.Proven)
    // ...but 'indexes' is no longer claimed as matched.
    Assert.DoesNotContain("indexes", v.Proven)

// --- the ENGINE-level subset-escape gate (the parity sweep, 2026-07-06) -------
// The peer FACE narrates rich proposals; this backstop refuses from ANY leg
// (legacy reverse / forward transfer) so the cross-wiring hazard is
// unreachable engine-wide.

[<Fact>]
let ``subsetEscapeGate: a full transfer or a closed/reconciled subset passes; an escaping edge refuses on the leg-neutral code`` () =
    Assert.Equal(None, Transfer.subsetEscapeGate srcCell None Set.empty)
    Assert.Equal(None, Transfer.subsetEscapeGate srcCell (Some (keys [ "City"; "Customer"; "Order" ])) Set.empty)
    Assert.Equal(None, Transfer.subsetEscapeGate srcCell (Some (keys [ "Customer" ])) (keys [ "City" ]))
    match Transfer.subsetEscapeGate srcCell (Some (keys [ "Customer" ])) Set.empty with
    | Some e ->
        Assert.Equal("transfer.subsetFkEscapes", e.Code)
        Assert.Contains("Customer.CityId -> City", e.Message)
        Assert.Contains("SOURCE-environment references", e.Message)
    | None -> Assert.Fail "expected the engine-level escape refusal"
    // The classification rides the SAME axis as the peer face's refusal.
    Assert.Equal((9, Preflight.SubsetFkEscape), Preflight.classify "transfer.subsetFkEscapes")

// --- THE GO BOARD (pure verdict algebra + render marks) -----------------------

[<Fact>]
let ``GoBoard: green iff zero red items; advisories never block; exit 0/5`` () =
    let g = GoBoard.item "a" (GoBoard.Status.Green "ok")
    let adv = GoBoard.item "b" (GoBoard.Status.Advisory "note")
    let red = GoBoard.item "c" (GoBoard.Status.Red ("broken", "fix it"))
    let board items : GoBoard.Board = { Flow = "golden"; From = "qa"; To = "uat"; Items = items }
    Assert.True(GoBoard.isGreen (board [ g; adv ]))
    Assert.Equal(0, GoBoard.exitCode (board [ g; adv ]))
    Assert.False(GoBoard.isGreen (board [ g; adv; red ]))
    Assert.Equal(5, GoBoard.exitCode (board [ g; adv; red ]))
    Assert.Equal(1, GoBoard.redCount (board [ g; red ]))

[<Fact>]
let ``GoBoard: the render carries the marks, the remedy, the detail, and the verdict with the next move`` () =
    let board : GoBoard.Board =
        { Flow = "golden"; From = "qa"; To = "uat"
          Items =
            [ GoBoard.item "routing" (GoBoard.Status.Green "peer leg")
              GoBoard.itemWith "relationships" (GoBoard.Status.Red ("2 escapes", "add the reconcile")) [ "Customer.CityId -> City" ]
              GoBoard.item "execute gates" (GoBoard.Status.Advisory "two gates at run time") ] }
    let text = GoBoard.render board |> String.concat "\n"
    Assert.Contains("[ GO ] routing", text)
    Assert.Contains("[STOP] relationships", text)
    Assert.Contains("-> add the reconcile", text)
    Assert.Contains("Customer.CityId -> City", text)
    Assert.Contains("[note] execute gates", text)
    Assert.Contains("VERDICT — RED. 1 open decision", text)
    Assert.Contains("check go golden", text)
    let green = { board with Items = board.Items |> List.filter (fun i -> i.Axis <> "relationships") }
    let greenText = GoBoard.render green |> String.concat "\n"
    Assert.Contains("VERDICT — GREEN", greenText)
    Assert.Contains("PROJECTION_ALLOW_EXECUTE=1 projection golden --go", greenText)

[<Fact>]
let ``GoBoard: the JSON projection carries verdict, redCount, and per-item status/remedy/detail`` () =
    let board : GoBoard.Board =
        { Flow = "golden"; From = "qa"; To = "uat"
          Items =
            [ GoBoard.item "routing" (GoBoard.Status.Green "peer leg")
              GoBoard.itemWith "relationships" (GoBoard.Status.Red ("2 escapes", "add the reconcile")) [ "Customer.CityId -> City" ] ] }
    let json = GoBoard.toJsonString board
    use doc = System.Text.Json.JsonDocument.Parse json
    let root = doc.RootElement
    Assert.Equal("red", root.GetProperty("verdict").GetString())
    Assert.Equal(1, root.GetProperty("redCount").GetInt32())
    let items = root.GetProperty("items")
    Assert.Equal(2, items.GetArrayLength())
    let rel = items.[1]
    Assert.Equal("red", rel.GetProperty("status").GetString())
    Assert.Equal("add the reconcile", rel.GetProperty("remedy").GetString())
    Assert.Equal("Customer.CityId -> City", rel.GetProperty("detail").[0].GetString())

// --- the Preflight classification of the two new axes -------------------------

[<Fact>]
let ``classify: the peer gates carry their own exits (shape 5; subset-FK 9; ossys read 6)`` () =
    Assert.Equal((5, Preflight.ShapeDivergence), Preflight.classify "transfer.peer.shapeDivergence")
    Assert.Equal((9, Preflight.SubsetFkEscape), Preflight.classify "transfer.peer.subsetFkEscapes")
    Assert.Equal((6, Preflight.SchemaReadFailed), Preflight.classify "source.ossys.readFailed")

// --- the go-board FORECAST table + planned-SQL preview + reconcile evidence ---
// (2026-07-07, the go-board forecast program)

[<Fact>]
let ``GoBoard.ForecastLine.after: before - deletes + adds; unprobed stays unprobed`` () =
    let line : GoBoard.ForecastLine =
        { Source = "dbo.S"; Table = "dbo.T"; Before = Some 10L; Adds = 5L; Matches = None; Deletes = 10L; Note = "" }
    Assert.Equal(Some 5L, GoBoard.ForecastLine.after line)
    Assert.Equal(None, GoBoard.ForecastLine.after { line with Before = None })

[<Fact>]
let ``GoBoard.forecastTable: source+target columns, header + rows + TOTAL; ? marks unprobed; - marks non-reconciled match; totals go unprobed when any row is`` () =
    let lines : GoBoard.ForecastLine list =
        [ { Source = "dbo.OSUSR_AAA_CUSTOMER"; Table = "dbo.OSUSR_BBB_CUSTOMER"; Before = Some 10L; Adds = 5L;  Matches = None;    Deletes = 10L; Note = "wiped first" }
          { Source = "dbo.OSUSR_AAA_CITY";     Table = "dbo.OSUSR_BBB_CITY";     Before = Some 3L;  Adds = 0L;  Matches = Some 3L; Deletes = 0L;  Note = "reconciled" } ]
    let t = GoBoard.forecastTable lines
    Assert.Equal(4, List.length t) // header + 2 rows + TOTAL
    let tokens (row: string) = row.Split([|' '|], System.StringSplitOptions.RemoveEmptyEntries)
    Assert.Equal<string[]>([| "source"; "(read)"; "target"; "(written)"; "before"; "+add"; "match"; "-del"; "after" |], tokens t.[0])
    Assert.Equal<string[]>([| "dbo.OSUSR_AAA_CUSTOMER"; "dbo.OSUSR_BBB_CUSTOMER"; "10"; "5"; "-"; "10"; "5"; "wiped"; "first" |], tokens t.[1])
    Assert.Equal<string[]>([| "dbo.OSUSR_AAA_CITY"; "dbo.OSUSR_BBB_CITY"; "3"; "0"; "3"; "0"; "3"; "reconciled" |], tokens t.[2])
    Assert.Equal<string[]>([| "TOTAL"; "13"; "5"; "3"; "10"; "8" |], tokens t.[3])
    // an unprobed row renders ? and poisons the TOTAL's before/after (never a silent 0)
    let withUnprobed = lines @ [ { Source = "dbo.OSUSR_AAA_ORDER"; Table = "dbo.OSUSR_BBB_ORDER"; Before = None; Adds = 7L; Matches = None; Deletes = 0L; Note = "" } ]
    let t2 = GoBoard.forecastTable withUnprobed
    Assert.Equal<string[]>([| "dbo.OSUSR_AAA_ORDER"; "dbo.OSUSR_BBB_ORDER"; "?"; "7"; "-"; "0"; "?" |], tokens t2.[3])
    Assert.Equal<string[]>([| "TOTAL"; "?"; "12"; "3"; "10"; "?" |], tokens t2.[4])
    Assert.Empty(GoBoard.forecastTable [])

[<Fact>]
let ``Transfer.plannedSqlPreview: child-first wipe (loadSet-scoped), then phase-1 in plan order; the header names the contract`` () =
    let topo = (Projection.Core.Passes.TopologicalOrderPass.runWith TreatAsCycle sinkCell).Value
    let rowOf (ident: string) (values: (string * string) list) : StaticRow =
        { Identifier = SsKey.synthesizedComposite "PEER_ROW" [ ident ] |> Result.value
          Values     = values |> List.map (fun (k, v) -> nm k, v) |> Map.ofList }
    let rows =
        Map.ofList
            [ kKey "City",     [ rowOf "c1" [ "Id", "1"; "Name", "Lisbon" ] ]
              kKey "Customer", [ rowOf "u1" [ "Id", "1"; "Email", "a@b.c"; "CityId", "1" ] ] ]
    let plan = DataLoadPlan.build sinkCell topo rows SurrogateRemapContext.empty
    match Transfer.plannedSqlPreview EmissionMode.WipeAndLoad (Some (keys [ "City"; "Customer" ])) sinkCell topo plan with
    | Error es -> Assert.Fail (es |> List.map (fun e -> e.Message) |> String.concat "; ")
    | Ok sql ->
        Assert.Contains("PLANNED SQL PREVIEW", sql)
        // the wipe: child-first (Customer before City), scoped to the loadSet (no Order wipe)
        let delCustomer = sql.IndexOf "DELETE FROM [dbo].[OSUSR_BBB_CUSTOMER]"
        let delCity     = sql.IndexOf "DELETE FROM [dbo].[OSUSR_BBB_CITY]"
        Assert.True(delCustomer >= 0 && delCity >= 0, "both wipe DELETEs present")
        Assert.True(delCustomer < delCity, "the wipe deletes children before parents")
        Assert.DoesNotContain("DELETE FROM [dbo].[OSUSR_BBB_ORDER]", sql)
        // phase 1 follows the wipe, parents before children, rows realized
        let insCity     = sql.IndexOf "OSUSR_BBB_CITY]"
        Assert.Contains("Lisbon", sql)
        Assert.True(delCity < sql.LastIndexOf "OSUSR_BBB_CITY", "the City load renders after the wipe")
        Assert.True(sql.LastIndexOf "OSUSR_BBB_CITY" < sql.LastIndexOf "OSUSR_BBB_CUSTOMER", "phase 1 loads parents before children")
        ignore insCity
    // Incremental renders no wipe
    match Transfer.plannedSqlPreview EmissionMode.Incremental (Some (keys [ "City"; "Customer" ])) sinkCell topo plan with
    | Error es -> Assert.Fail (es |> List.map (fun e -> e.Message) |> String.concat "; ")
    | Ok sql -> Assert.DoesNotContain("DELETE FROM", sql)

[<Fact>]
let ``PeerTransfer.narrateEvidence: the paste-able move leads; strength named; an unprobed candidate carries its reason`` () =
    let ev (col: string) (indexBacked: bool) (verdict: PeerTransfer.EvidenceVerdict) : PeerTransfer.ReconcileEvidence =
        { Target = kKey "City"; TargetRef = "AppCore.City"; Column = nm col; IndexBacked = indexBacked; Verdict = verdict }
    let lines =
        PeerTransfer.narrateEvidence
            [ ev "Name"  true  (PeerTransfer.EvidenceVerdict.Probed (true, 12, 12))
              ev "Label" false (PeerTransfer.EvidenceVerdict.Probed (false, 3, 12))
              ev "Code"  true  (PeerTransfer.EvidenceVerdict.Probed (false, 12, 12))
              ev "Alias" true  (PeerTransfer.EvidenceVerdict.Probed (true, 0, 0))
              ev "Tag"   true  (PeerTransfer.EvidenceVerdict.Unprobed "login failed") ]
    Assert.StartsWith("reconcile 'AppCore.City:Name'", lines.[0])
    Assert.Contains("sink-unique", lines.[0])
    Assert.Contains("12/12", lines.[0])
    Assert.Contains("STRONG", lines.[0])
    Assert.Contains("unique-indexed", lines.[0])
    Assert.Contains("PARTIAL", lines.[1])
    Assert.Contains("name-shaped", lines.[1])
    Assert.Contains("ambiguous", lines.[2])
    Assert.Contains("unproven", lines.[3])
    Assert.Contains("could not run: login failed", lines.[4])
