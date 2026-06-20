module Projection.Tests.ComparisonTests

open Xunit
open Projection.Core
open Projection.Cli
open Projection.Tests.Fixtures

/// Masterful base #3 — comparison as a capability. The discriminating
/// predicate lives in the type: `Apply` is present iff the delta is a torsor
/// element (replayable), absent iff it is a lossy quotient.

[<Fact>]
let ``Comparison: catalog carries the torsor action; physicalSchema is a quotient`` () =
    Assert.True(Option.isSome Comparison.catalog.Apply)          // torsor — replayable
    Assert.True(Option.isNone Comparison.physicalSchema.Apply)   // lossy quotient

[<Fact>]
let ``Comparison: Between a a is the identity delta (empty)`` () =
    match Comparison.catalog.Between sampleCatalog sampleCatalog with
    | Ok d    -> Assert.True(Comparison.catalog.IsEmpty d)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison: Apply replays the delta through the abstraction (Weyl flows through)`` () =
    // The full mutated-target Weyl law is proven in CatalogDiffTests; here we
    // assert it flows through the capability's Apply: apply (between a a) a
    // leaves a unchanged (the re-observed delta is empty).
    match Comparison.catalog.Between sampleCatalog sampleCatalog, Comparison.catalog.Apply with
    | Ok d, Some apply ->
        let result = apply d sampleCatalog
        match Comparison.catalog.Between sampleCatalog result with
        | Ok d2   -> Assert.True(Comparison.catalog.IsEmpty d2)
        | Error e -> Assert.Fail e
    | _ -> Assert.Fail "expected a delta and an Apply"

[<Fact>]
let ``Comparison: render projects a diff onto the View substrate (the count visible in json)`` () =
    match Comparison.summary Comparison.catalog sampleCatalog sampleCatalog with
    | Ok v ->
        let j = (View.toJson v).ToJsonString()
        Assert.Contains("changes", j)        // the panel title "changes"
        Assert.Contains("total changes", j)  // the count, in plain words (never `norm`)
    | Error e -> Assert.Fail e

// --- statement-first surface (INSTRUMENT slice 1) --------------------------
// Discriminating predicate: the lead verdict READS the change — a destructive
// change leads amber ("review first"), an additive / no-op change leads calm.
// A naive renderer shows the same panel with no verdict at all.

let private emptyCatalog = IRBuilders.mkCatalog []

[<Fact>]
let ``Comparison statement: an identical pair leads with a calm no-differences verdict`` () =
    match Comparison.catalog.Between sampleCatalog sampleCatalog with
    | Ok d ->
        match Comparison.catalogStatement d with
        | View.Hero(View.Ok, text) -> Assert.Contains("No differences", text)
        | other -> Assert.Fail(sprintf "expected a calm Ok hero, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison statement: a change with removals leads amber with the true verb — review before applying`` () =
    match Comparison.catalog.Between sampleCatalog emptyCatalog with
    | Ok d ->
        match Comparison.catalogStatement d with
        | View.Hero(View.Warn, text) -> Assert.Contains("drops", text)
        | other -> Assert.Fail(sprintf "expected a Warn hero, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison statement: an additive change leads calm — no removals`` () =
    match Comparison.catalog.Between emptyCatalog sampleCatalog with
    | Ok d ->
        match Comparison.catalogStatement d with
        | View.Hero(View.Ok, text) -> Assert.Contains("no removals", text)
        | other -> Assert.Fail(sprintf "expected a calm Ok hero, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison: renderCatalogChange leads with the statement, then the substantiation`` () =
    match Comparison.catalog.Between sampleCatalog sampleCatalog with
    | Ok d ->
        match Comparison.renderCatalogChange d with
        | View.Doc (View.Hero _ :: _) -> ()        // statement first, substantiation beneath
        | other -> Assert.Fail(sprintf "expected a Doc led by a Hero, got %A" other)
    | Error e -> Assert.Fail e

// --- move-typed lanes (INSTRUMENT slice 2) ---------------------------------
// Discriminating predicate: changes group into move-lanes, each badged by
// reversibility — a remove lane is Bad (destroys structure), an add lane is Ok
// (safe). A naive renderer shows one undifferentiated list with no move/badge.

[<Fact>]
let ``Comparison lanes: a removed kind lands in a remove lane badged Bad`` () =
    match Comparison.catalog.Between sampleCatalog emptyCatalog with
    | Ok d ->
        let remove =
            Comparison.renderCatalogLanes d
            |> List.tryPick (function View.Lane(_, "remove", st, items) -> Some(st, items) | _ -> None)
        match remove with
        | Some (View.Bad, items) -> Assert.NotEmpty items
        | other -> Assert.Fail(sprintf "expected a Bad remove lane, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison lanes: an added kind lands in an add lane badged Ok`` () =
    match Comparison.catalog.Between emptyCatalog sampleCatalog with
    | Ok d ->
        let add =
            Comparison.renderCatalogLanes d
            |> List.tryPick (function View.Lane(_, "add", st, items) -> Some(st, items) | _ -> None)
        match add with
        | Some (View.Ok, items) -> Assert.NotEmpty items
        | other -> Assert.Fail(sprintf "expected an Ok add lane, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``Comparison: renderCatalogChange dig carries the move lanes`` () =
    match Comparison.catalog.Between sampleCatalog emptyCatalog with
    | Ok d ->
        match Comparison.renderCatalogChange d with
        | View.Doc blocks ->
            Assert.True(
                blocks |> List.exists (function View.Lane(_, "remove", _, _) -> true | _ -> false),
                "expected a remove lane in the substantiation")
        | other -> Assert.Fail(sprintf "expected a Doc, got %A" other)
    | Error e -> Assert.Fail e

/// Reshape fixture (slice 2b): a Customer.Name facet change, mirroring the
/// CatalogDiff attribute-Changed fixture, built from the shared Fixtures.
let private reshapeTarget (f: Attribute -> Attribute) : Catalog =
    let customer' =
        { customer with
            Attributes = customer.Attributes |> List.map (fun a -> if a.SsKey = customerNameKey then f a else a) }
    Catalog.create [ { salesModule with Kinds = [ customer'; order; country ] } ] [] |> Result.value

[<Fact>]
let ``Comparison lanes: a changed attribute facet lands in a reshape lane badged Warn`` () =
    let target = reshapeTarget (fun a -> { a with Type = Integer })
    match Comparison.catalog.Between sampleCatalog target with
    | Ok d ->
        let reshape =
            Comparison.renderCatalogLanes d
            |> List.tryPick (function View.Lane(_, "reshape", st, items) -> Some(st, items) | _ -> None)
        match reshape with
        | Some (View.Warn, items) -> Assert.NotEmpty items
        | other -> Assert.Fail(sprintf "expected a Warn reshape lane, got %A" other)
    | Error e -> Assert.Fail e

// --- delta-grade: every channel the diff computes reaches the walkable lanes -
// Discriminating predicate: `CatalogDiff` computes the reference / index /
// sequence / attribute-add-remove-rename / kind-facet channels (C1 + NM-17), but
// the pre-delta-grade `renderCatalogLanes` surfaced ONLY kind moves + attribute
// reshapes — every other channel rode through invisibly. These pin each channel
// onto its move-lane, channel-qualified, so the L2 Navigator can dig it live. A
// naive renderer drops the channel (its change is silent in the walkable diff).

/// Swap the `Sales` module's kinds for a target catalog (the move fixture seam).
let private withKinds (kinds: Kind list) : Catalog =
    Catalog.create [ { salesModule with Kinds = kinds } ] [] |> Result.value

let private laneItems (label: string) (d: CatalogDiff) : string list =
    Comparison.renderCatalogLanes d
    |> List.tryPick (function View.Lane(_, l, _, items) when l = label -> Some items | _ -> None)
    |> Option.defaultValue []

[<Fact>]
let ``delta-grade lanes: a reshaped reference surfaces in the reshape lane (the relationship channel)`` () =
    // Order's FK to Customer gains ON DELETE CASCADE — a ReferenceFacet.OnDelete reshape.
    let order' = { order with References = order.References |> List.map (fun r -> { r with OnDelete = Cascade }) }
    match Comparison.catalog.Between sampleCatalog (withKinds [ customer; order'; country ]) with
    | Ok d -> Assert.Contains(laneItems "reshape" d, fun (s: string) -> s.StartsWith "relationship ")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade lanes: an added index surfaces in the add lane (the index channel)`` () =
    let customer' = { customer with Indexes = [ Index.ofKeyColumns (idxKey ["Customer"; "Name"]) (mkName "IX_Customer_Name") [ customerNameKey ] ] }
    match Comparison.catalog.Between sampleCatalog (withKinds [ customer'; order; country ]) with
    | Ok d -> Assert.Contains(laneItems "add" d, fun (s: string) -> s.StartsWith "index ")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade lanes: an added sequence surfaces in the add lane (the sequence channel)`` () =
    let seq_ =
        Sequence.create (testKey "OS_SEQ_Test") (mkName "SEQ_Test") "dbo" "bigint"
            (Some 1M) (Some 1M) (Some 1M) (Some 99M) false NoCache None
        |> Result.value
    let target = Catalog.create [ salesModule ] [ seq_ ] |> Result.value
    match Comparison.catalog.Between sampleCatalog target with
    | Ok d -> Assert.Contains(laneItems "add" d, fun (s: string) -> s.StartsWith "sequence ")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade lanes: an added column surfaces in the add lane (the attribute channel)`` () =
    let customer' = { customer with Attributes = customer.Attributes @ [ Attribute.create (attrKey ["Customer"; "Email"]) (mkName "Email") Text ] }
    match Comparison.catalog.Between sampleCatalog (withKinds [ customer'; order; country ]) with
    | Ok d -> Assert.Contains(laneItems "add" d, fun (s: string) -> s.StartsWith "column ")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade lanes: a removed column surfaces in the remove lane (the attribute channel)`` () =
    let customer' = { customer with Attributes = customer.Attributes |> List.filter (fun a -> a.SsKey <> customerTenantKey) }
    match Comparison.catalog.Between sampleCatalog (withKinds [ customer'; order; country ]) with
    | Ok d -> Assert.Contains(laneItems "remove" d, fun (s: string) -> s.StartsWith "column ")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade lanes: a renamed column surfaces in the rename lane carrying old then new`` () =
    let customer' = { customer with Attributes = customer.Attributes |> List.map (fun a -> if a.SsKey = customerNameKey then { a with Name = mkName "FullName" } else a) }
    match Comparison.catalog.Between sampleCatalog (withKinds [ customer'; order; country ]) with
    | Ok d -> Assert.Contains(laneItems "rename" d, fun (s: string) -> s.StartsWith "column " && s.Contains "→" && s.Contains "FullName")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade lanes: a changed kind-own facet surfaces in the reshape lane (the table itself)`` () =
    // Customer drops its TenantScoped modality — a KindFacet.Modality reshape of the table.
    let customer' = { customer with Modality = [] }
    match Comparison.catalog.Between sampleCatalog (withKinds [ customer'; order; country ]) with
    | Ok d -> Assert.Contains(laneItems "reshape" d, fun (s: string) -> s.StartsWith "table " && s.Contains "modality")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade one-substrate: a surfaced reference reshape rides the json lens too`` () =
    let order' = { order with References = order.References |> List.map (fun r -> { r with OnDelete = Cascade }) }
    match Comparison.catalog.Between sampleCatalog (withKinds [ customer; order'; country ]) with
    | Ok d ->
        let j = (View.toJson (Comparison.renderCatalogChange d)).ToJsonString()
        Assert.Contains("relationship", j)   // the machine lens carries the richer lane item
    | Error e -> Assert.Fail e

// --- delta-grade: before/after EVIDENCE on the reshape lanes -----------------
// Discriminating predicate: an ALTER reshape shows the VALUE it moved between
// (`type text → integer`), resolved from the diff's retained source/target — not
// merely WHICH facet changed. A naive renderer names the facet but drops the
// evidence, so the operator can't read the ALTER from the lane.

[<Fact>]
let ``delta-grade evidence: an attribute type reshape shows before then after (the column ALTER)`` () =
    let target = reshapeTarget (fun a -> { a with Type = Integer })   // Customer.Name: Text → Integer
    match Comparison.catalog.Between sampleCatalog target with
    | Ok d -> Assert.Contains(laneItems "reshape" d, fun (s: string) -> s.Contains "type text → integer")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade evidence: a nullability reshape shows not null then null`` () =
    let target = reshapeTarget (fun a -> { a with Column = ColumnRealization.create "NAME" true |> Result.value })
    match Comparison.catalog.Between sampleCatalog target with
    | Ok d -> Assert.Contains(laneItems "reshape" d, fun (s: string) -> s.Contains "nullability not null → null")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade evidence: a reference on-delete reshape shows the action moved (the FK ALTER)`` () =
    let order' = { order with References = order.References |> List.map (fun r -> { r with OnDelete = Cascade }) }
    match Comparison.catalog.Between sampleCatalog (withKinds [ customer; order'; country ]) with
    | Ok d -> Assert.Contains(laneItems "reshape" d, fun (s: string) -> s.Contains "on delete no action → cascade")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade evidence one-substrate: the before-after value rides the json lens`` () =
    let target = reshapeTarget (fun a -> { a with Type = Integer })
    match Comparison.catalog.Between sampleCatalog target with
    | Ok d ->
        let j = (View.toJson (Comparison.renderCatalogChange d)).ToJsonString()
        Assert.Contains("type text", j)   // the value (not just the facet word) reaches the machine lens
    | Error e -> Assert.Fail e

/// Render the change document through the plain (NoColors) lens, deep enough to
/// reveal every lane's items — the human-lens side of the one-substrate law.
let private renderChangeDeep (d: CatalogDiff) : string =
    use sw = new System.IO.StringWriter()
    let console =
        Spectre.Console.AnsiConsole.Create(
            Spectre.Console.AnsiConsoleSettings(
                Ansi = Spectre.Console.AnsiSupport.No,
                ColorSystem = Spectre.Console.ColorSystemSupport.NoColors,
                Out = Spectre.Console.AnsiConsoleOutput(sw)))
    console.Profile.Width <- 200
    View.writeToDepth console 4 (Comparison.renderCatalogChange d)
    sw.ToString()

[<Fact>]
let ``delta-grade render: a rich change renders every channel by name, with before-after evidence (human lens)`` () =
    let customer' =
        { customer with
            Modality = []   // reshape table: modality
            Attributes =
                (customer.Attributes
                 |> List.choose (fun a ->
                     if a.SsKey = customerTenantKey then None                              // remove column TenantId
                     elif a.SsKey = customerNameKey then Some { a with Type = Integer }     // reshape column Name: type
                     else Some a))
                @ [ Attribute.create (attrKey ["Customer"; "Email"]) (mkName "Email") Text ]  // add column Email
            Indexes = [ Index.ofKeyColumns (idxKey ["Customer"; "Name"]) (mkName "IX_Customer_Name") [ customerNameKey ] ] }
    let order' = { order with References = order.References |> List.map (fun r -> { r with OnDelete = Cascade }) }
    let seq_ =
        Sequence.create (testKey "OS_SEQ_Ticket") (mkName "SEQ_Ticket") "dbo" "bigint"
            (Some 1M) (Some 1M) (Some 1M) (Some 99M) false NoCache None |> Result.value
    let target = Catalog.create [ { salesModule with Kinds = [ customer'; order'; country ] } ] [ seq_ ] |> Result.value
    match Comparison.catalog.Between sampleCatalog target with
    | Ok d ->
        let r = renderChangeDeep d
        Assert.Contains("column Customer.Name: type text → integer", r)                  // attribute evidence, by name
        Assert.Contains("relationship Order.Customer: on delete no action → cascade", r) // FK evidence, by name
        Assert.Contains("column Customer.Email", r)                                      // added column
        Assert.Contains("index Customer.IX_Customer_Name", r)                            // added index
        Assert.Contains("sequence SEQ_Ticket", r)                                        // added sequence
        Assert.Contains("table Customer: modality", r)                                   // kind-facet reshape
        Assert.Contains("column Customer.TenantId", r)                                   // removed column
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade legibility: lanes name entities by their Name, never the raw SsKey`` () =
    let target = reshapeTarget (fun a -> { a with Type = Integer })
    match Comparison.catalog.Between sampleCatalog target with
    | Ok d ->
        let items = laneItems "reshape" d
        Assert.Contains(items, fun (s: string) -> s.StartsWith "column Customer.Name:")   // the clean qualified Name
        Assert.True(items |> List.forall (fun s -> not (s.Contains "OS_ATTR")), "no raw synthesized key leaks into a lane")
    | Error e -> Assert.Fail e

// --- the FK target/source name-wall (delta-grade polish #A) ------------------
// Discriminating predicate: the FK `target` / `source column` reshape facets name
// a CROSS-entity SsKey. For an OssysOriginal key, `rootOriginal` is a bare 32-char
// GUID — so a retarget rendered `target <hex> → <hex>`, illegible exactly where the
// operator must read "this FK now points at a DIFFERENT table." The fix resolves
// those keys through the per-side name index (as the qualifier already does). The
// synthesized-key fixtures above CANNOT catch this (their `rootOriginal` IS a name),
// so this uses OssysOriginal (GUID) keys, where the regression would show.

/// A minimal OssysOriginal-keyed kind (a GUID identity, a readable Name) — the
/// real-estate shape where `rootOriginal` is a hex wall.
let private ossysKind (g: System.Guid) (label: string) : Kind =
    Kind.create (SsKey.ossysOriginal g) (mkName label) (mkTableId "dbo" ("OSUSR_X_" + label))
        [ Attribute.create (attrKey [label; "Id"]) (mkName "Id") Integer ]

let private accountKey = System.Guid("a0000000-0000-0000-0000-000000000001")
let private vendorKey  = System.Guid("b0000000-0000-0000-0000-000000000002")

/// An `Order`-like kind whose one FK points at `target`; the two target kinds are
/// OssysOriginal-keyed (`Account` / `Vendor`) and both present, so a retarget is a
/// single `ReferenceFacet.Target` reshape with both sides resolvable by name.
let private orderTo (target: System.Guid) : Catalog =
    let ord =
        { Kind.create (kindKey ["OrderX"]) (mkName "OrderX") (mkTableId "dbo" "OSUSR_X_ORDER")
            [ Attribute.create (attrKey ["OrderX"; "Id"]) (mkName "Id") Integer
              Attribute.create (attrKey ["OrderX"; "AccountId"]) (mkName "AccountId") Integer ]
            with References = [ Reference.create (refKey ["OrderX"; "Acct"]) (mkName "Acct") (attrKey ["OrderX"; "AccountId"]) (SsKey.ossysOriginal target) ] }
    Catalog.create [ IRBuilders.mkModule (modKey "X") (mkName "X") [ ord; ossysKind accountKey "Account"; ossysKind vendorKey "Vendor" ] ] []
    |> Result.value

[<Fact>]
let ``delta-grade polish: an FK retarget names the tables, never the raw GUID (the name-wall fix)`` () =
    match Comparison.catalog.Between (orderTo accountKey) (orderTo vendorKey) with
    | Ok d ->
        let items = laneItems "reshape" d
        Assert.Contains(items, fun (s: string) -> s.Contains "target Account → Vendor")            // names, the readable ALTER
        Assert.True(items |> List.forall (fun s -> not (s.Contains "a0000000")), "no GUID hex leaks into the FK evidence")
    | Error e -> Assert.Fail e

// --- the structural-channel reshape evidence (delta-grade polish #B) ---------
// Discriminating predicate: the index / sequence / kind-facet reshapes named
// WHICH facet moved but not the VALUE — yet `uniqueness not unique → unique`
// (apply FAILS on existing duplicates), `start 1 → 1000` (a key-space jump), and
// `active yes → no` (a deactivation) are operationally different from their
// reverses. These carry the before → after the merged delta-grade deferred for
// the structural channels.

[<Fact>]
let ``delta-grade polish: an index uniqueness reshape shows not unique then unique`` () =
    let idxWith (u: IndexUniqueness) =
        { Index.ofKeyColumns (idxKey ["Customer"; "Name"]) (mkName "IX_Customer_Name") [ customerNameKey ] with Uniqueness = u }
    let custWith u = { customer with Indexes = [ idxWith u ] }
    match Comparison.catalog.Between (withKinds [ custWith NotUnique; order; country ]) (withKinds [ custWith Unique; order; country ]) with
    | Ok d -> Assert.Contains(laneItems "reshape" d, fun (s: string) -> s.Contains "index Customer.IX_Customer_Name: uniqueness not unique → unique")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade polish: a sequence start reshape shows the value moved`` () =
    let seqWith (start: decimal) =
        Sequence.create (testKey "OS_SEQ_Start") (mkName "SEQ_Start") "dbo" "bigint"
            (Some start) (Some 1M) (Some 1M) (Some 99999M) false NoCache None |> Result.value
    let src = Catalog.create [ salesModule ] [ seqWith 1M ]    |> Result.value
    let tgt = Catalog.create [ salesModule ] [ seqWith 1000M ] |> Result.value
    match Comparison.catalog.Between src tgt with
    | Ok d -> Assert.Contains(laneItems "reshape" d, fun (s: string) -> s.Contains "sequence SEQ_Start: start 1 → 1000")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade polish: a kind activation reshape shows active yes then no`` () =
    let customer' = { customer with IsActive = false }
    match Comparison.catalog.Between sampleCatalog (withKinds [ customer'; order; country ]) with
    | Ok d -> Assert.Contains(laneItems "reshape" d, fun (s: string) -> s.Contains "table Customer: active yes → no")
    | Error e -> Assert.Fail e

// --- the data-risk surface + honest statement (delta-grade polish #C) --------
// Discriminating predicate: a change that can REWRITE or LOSE row data on apply (a
// null → not null, a dropped column, a uniqueness gained) is pulled into a SEPARATE
// "review these first" callout AND counted in the lead statement — so a zero-drop
// migration that adds a NOT NULL column no longer leads CALM. A naive renderer
// buries the risky change in the one amber reshape bucket and leads "no removals".

let private dangerItems (d: CatalogDiff) : string list =
    Comparison.dangerLane d |> List.collect (function View.Lane(_, _, _, items) -> items | _ -> [])

/// Customer.Name made nullable — so a diff TOWARD sampleCatalog is a `null → not
/// null` tightening (the dangerous direction: existing null rows fail / need backfill).
let private nullableName : Catalog =
    reshapeTarget (fun a -> { a with Column = ColumnRealization.create "NAME" true |> Result.value })

[<Fact>]
let ``delta-grade polish: a null to not-null tightening leads amber with may-rewrite, and is in the danger callout`` () =
    match Comparison.catalog.Between nullableName sampleCatalog with
    | Ok d ->
        match Comparison.catalogStatement d with
        | View.Hero(View.Warn, text) -> Assert.Contains("may rewrite or lose data", text)
        | other -> Assert.Fail(sprintf "expected a Warn statement naming the data risk, got %A" other)
        Assert.Contains(dangerItems d, fun (s: string) -> s.Contains "column Customer.Name" && s.Contains "nullability null → not null")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade polish: a dropped column is named data-lost in the danger callout`` () =
    let customer' = { customer with Attributes = customer.Attributes |> List.filter (fun a -> a.SsKey <> customerTenantKey) }
    match Comparison.catalog.Between sampleCatalog (withKinds [ customer'; order; country ]) with
    | Ok d -> Assert.Contains(dangerItems d, fun (s: string) -> s.Contains "column Customer.TenantId" && s.Contains "data lost")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade polish: a purely additive change raises no danger callout and leads calm`` () =
    match Comparison.catalog.Between emptyCatalog sampleCatalog with
    | Ok d ->
        Assert.Empty(Comparison.dangerLane d)                                  // nothing touches data
        match Comparison.catalogStatement d with
        | View.Hero(View.Ok, text) -> Assert.Contains("no removals", text)     // still calm
        | other -> Assert.Fail(sprintf "expected a calm Ok statement, got %A" other)
    | Error e -> Assert.Fail e

// --- the danger callout at SCALE (310 tables ⇒ hundreds of concerns) ---------
// Discriminating predicate: past ~12 data-touching concerns the flat callout would
// bury the risk SHAPE behind "and N more"; instead it groups by category (how many
// dropped / null → not null / cascade / …), each diggable, with a loud total. A
// naive renderer caps at 12 and silently hides the rest — the worst place to.

/// A wide kind whose `n` `C1..Cn` columns are all NULLABLE (so a tighten is a
/// `null → not null` danger), in its own module.
let private wideCatalog (cols: int list) (nullable: bool) : Catalog =
    let attrs =
        cols |> List.map (fun i ->
            { Attribute.create (attrKey ["Wide"; sprintf "C%d" i]) (mkName (sprintf "C%d" i)) Text with
                Column = ColumnRealization.create (sprintf "C%d" i) nullable |> Result.value })
    let wide = Kind.create (kindKey ["Wide"]) (mkName "Wide") (mkTableId "dbo" "OSUSR_W_WIDE") attrs
    Catalog.create [ IRBuilders.mkModule (modKey "W") (mkName "W") [ wide ] ] [] |> Result.value

[<Fact>]
let ``delta-grade polish: at scale the danger callout groups by risk category with a loud total`` () =
    // Source: 20 nullable columns. Target: drop C1..C10, tighten C11..C20 to NOT NULL
    // ⇒ 10 "dropped" + 10 "null → not null" = 20 concerns (> the flat threshold).
    let src = wideCatalog [ 1 .. 20 ] true
    let tgt = wideCatalog [ 11 .. 20 ] false
    match Comparison.catalog.Between src tgt with
    | Ok d ->
        match Comparison.dangerLane d with
        | [ View.Disclosure (headline, View.Bad, detail) ] ->
            Assert.Contains("may rewrite or lose data", headline)
            Assert.Contains("20", headline)                                  // the loud total, not a hidden "and N more"
            let cats = detail |> List.choose (function View.Disclosure (h, _, _) -> Some h | _ -> None)
            Assert.Contains(cats, fun (h: string) -> h.StartsWith "dropped" && h.Contains "10")
            Assert.Contains(cats, fun (h: string) -> h.StartsWith "null → not null" && h.Contains "10")
        | other -> Assert.Fail(sprintf "expected a grouped danger Disclosure at scale, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade polish: a small danger set stays a flat callout lane (below the grouping threshold)`` () =
    // Two concerns ⇒ flat lane, the historical shape (the grouping is a scale affordance).
    match Comparison.catalog.Between nullableName sampleCatalog with
    | Ok d ->
        match Comparison.dangerLane d with
        | [ View.Lane (_, "may rewrite or lose data", View.Bad, _) ] -> ()
        | other -> Assert.Fail(sprintf "expected a flat danger lane for a small set, got %A" other)
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade polish: the danger callout rides the machine lens (one substrate)`` () =
    match Comparison.catalog.Between nullableName sampleCatalog with
    | Ok d ->
        let j = (View.toJson (Comparison.renderCatalogChange d)).ToJsonString()
        Assert.Contains("may rewrite or lose data", j)   // the callout reaches the structured lens
    | Error e -> Assert.Fail e

// --- channel scoping (`diff --only <channel>`) ------------------------------
// Discriminating predicate: `--only columns` keeps ONLY the column items across
// every move-lane (and the danger callout), so an operator reviews one channel of
// a huge diff. A naive renderer shows the whole changeset regardless.

/// Every lane item across a rendered lane list (the danger callout + the move lanes).
let private allLaneItems (views: View.View list) : string list =
    views |> List.collect (function View.Lane(_, _, _, items) -> items | _ -> [])

/// A multi-channel diff: a column reshape (Customer.Name) + an FK reshape (Order→Customer).
let private multiChannel : CatalogDiff =
    let customer' = { customer with Attributes = customer.Attributes |> List.map (fun a -> if a.SsKey = customerNameKey then { a with Type = Integer } else a) }
    let order'    = { order with References = order.References |> List.map (fun r -> { r with OnDelete = Cascade }) }
    match Comparison.catalog.Between sampleCatalog (withKinds [ customer'; order'; country ]) with
    | Ok d -> d
    | Error e -> failwith e

[<Fact>]
let ``delta-grade polish: --only columns keeps only the column items`` () =
    let items = allLaneItems (Comparison.renderCatalogLanesScoped (Some "columns") multiChannel)
    Assert.NotEmpty items
    Assert.True(items |> List.forall (fun (s: string) -> s.StartsWith "column "), "only column items survive --only columns")

[<Fact>]
let ``delta-grade polish: --only relationships keeps only the relationship items`` () =
    let items = allLaneItems (Comparison.renderCatalogLanesScoped (Some "relationships") multiChannel)
    Assert.NotEmpty items
    Assert.True(items |> List.forall (fun (s: string) -> s.StartsWith "relationship "), "only relationship items survive --only relationships")

[<Fact>]
let ``delta-grade polish: --only a channel with no changes yields no lanes`` () =
    // multiChannel touches columns + relationships only — scoping to indexes is empty.
    Assert.Empty(Comparison.renderCatalogLanesScoped (Some "indexes") multiChannel)

[<Fact>]
let ``delta-grade polish: --only scopes the danger callout too`` () =
    // A column rewrite (null → not null) + an FK cascade are both data-touching;
    // --only relationships keeps only the FK danger.
    match Comparison.catalog.Between nullableName (withKinds [ customer; { order with References = order.References |> List.map (fun r -> { r with OnDelete = Cascade }) }; country ]) with
    | Ok d ->
        let danger = allLaneItems (Comparison.dangerLaneScoped (Some "relationships") d)
        Assert.NotEmpty danger                                                          // the FK danger survives
        Assert.True(danger |> List.forall (fun (s: string) -> s.StartsWith "relationship "), "the callout scopes with the lanes")
        // the unscoped callout carries BOTH the column and the FK danger (proves --only narrowed it)
        Assert.Contains(allLaneItems (Comparison.dangerLane d), fun (s: string) -> s.StartsWith "column ")
    | Error e -> Assert.Fail e

// --- the per-module "top movers" rollup (at-scale orientation) --------------
// Discriminating predicate: a diff spanning ≥ 2 modules carries a per-module
// change tally, churn-sorted, so "which module is hot" reads at a glance. A naive
// renderer makes the operator read every lane to find the mass. A single-module
// diff shows no rollup (it needs none).

let private invoice : Kind =
    Kind.create (kindKey ["Invoice"]) (mkName "Invoice") (mkTableId "dbo" "OSUSR_B_INVOICE")
        [ Attribute.create (attrKey ["Invoice"; "Id"]) (mkName "Id") Integer ]

/// A two-module catalog: Sales (the standard kinds) + Billing (one Invoice kind).
let private twoModule (salesKinds: Kind list) (invoiceKind: Kind) : Catalog =
    Catalog.create
        [ { salesModule with Kinds = salesKinds }
          IRBuilders.mkModule (modKey "Billing") (mkName "Billing") [ invoiceKind ] ] []
    |> Result.value

/// The rollup table rows (module, count) from a rendered change, or None.
let private rollupRows (d: CatalogDiff) : string list option =
    match Comparison.renderCatalogChange d with
    | View.Doc blocks ->
        blocks
        |> List.tryPick (function
            | View.Table (h, rows) when h = [ "module"; "changes" ] ->
                Some (rows |> List.map (fun cells -> fst (List.head cells)))
            | _ -> None)
    | _ -> None

[<Fact>]
let ``delta-grade polish: a multi-module diff carries a per-module rollup, churn-sorted`` () =
    // Sales: Customer.Name type change + drop TenantId = 2 changes. Billing: +1 column = 1.
    let salesTgt =
        { customer with
            Attributes =
                customer.Attributes
                |> List.filter (fun a -> a.SsKey <> customerTenantKey)
                |> List.map (fun a -> if a.SsKey = customerNameKey then { a with Type = Integer } else a) }
    let invoiceTgt = { invoice with Attributes = invoice.Attributes @ [ Attribute.create (attrKey ["Invoice"; "Amount"]) (mkName "Amount") Decimal ] }
    let src = twoModule [ customer; order; country ] invoice
    let tgt = twoModule [ salesTgt; order; country ] invoiceTgt
    match Comparison.catalog.Between src tgt with
    | Ok d ->
        match rollupRows d with
        | Some mods -> Assert.Equal<string list>([ "Sales"; "Billing" ], mods)   // hotter module first
        | None -> Assert.Fail "expected a per-module rollup table"
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade polish: a single-module diff shows no per-module rollup`` () =
    match Comparison.catalog.Between sampleCatalog (reshapeTarget (fun a -> { a with Type = Integer })) with
    | Ok d -> Assert.True((rollupRows d).IsNone, "a one-module diff needs no rollup")
    | Error e -> Assert.Fail e

[<Fact>]
let ``delta-grade polish: lane items sort by name, so a capped lane is scannable (not SsKey order)`` () =
    // Two columns added in a deliberately non-alphabetical source order; the lane
    // must present them name-sorted, not in the Set's SsKey order.
    let customer' =
        { customer with
            Attributes =
                customer.Attributes
                @ [ Attribute.create (attrKey ["Customer"; "Zeta"]) (mkName "Zeta") Text
                    Attribute.create (attrKey ["Customer"; "Alpha"]) (mkName "Alpha") Text ] }
    match Comparison.catalog.Between sampleCatalog (withKinds [ customer'; order; country ]) with
    | Ok d ->
        let adds = laneItems "add" d
        let iAlpha = adds |> List.findIndex (fun (s: string) -> s.Contains "Customer.Alpha")
        let iZeta  = adds |> List.findIndex (fun (s: string) -> s.Contains "Customer.Zeta")
        Assert.True(iAlpha < iZeta, "Alpha sorts before Zeta in the add lane")
    | Error e -> Assert.Fail e
