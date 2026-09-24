module Projection.Tests.TransferImpactTests

// The transfer-impact model (2026-07-09, the precise-impact program): graph
// segmentation into connected components, before/after/delta classification
// (add / delete / change / unchanged), and the nested-document denormalization
// (owned children conjoined under a root; reconciled parents inlined). Pure — a
// constructed catalog + in-memory before/after rows, no database.

open Xunit
open Projection.Core
open Projection.Pipeline

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private nm (s: string) : Name = Name.create s |> mustOk
let private kKey (s: string) : SsKey = SsKey.synthesizedComposite "IMPACT_KIND" [ s ] |> mustOk
let private aKey (k: string) (a: string) : SsKey = SsKey.synthesizedComposite "IMPACT_ATTR" [ k; a ] |> mustOk
let private rKey (k: string) (r: string) : SsKey = SsKey.synthesizedComposite "IMPACT_REF" [ k; r ] |> mustOk

let private idPk (kind: string) : Attribute =
    { Attribute.create (aKey kind "Id") (nm "Id") Integer with
        Column = ColumnRealization.create "ID" false |> mustOk
        IsPrimaryKey = true; IsIdentity = true; IsMandatory = true }

let private col (kind: string) (logical: string) (nullable: bool) : Attribute =
    { Attribute.create (aKey kind logical) (nm logical) Text with
        Column = ColumnRealization.create (logical.ToUpperInvariant()) nullable |> mustOk
        IsMandatory = not nullable }

/// Customer → City (ref); Order → Customer (owned child); OrderLine → Order (owned child).
let private catalog : Catalog =
    let city = Kind.create (kKey "City") (nm "City") (TableId.create "dbo" "CITY" |> mustOk) [ idPk "City"; col "City" "Name" false ]
    let customer =
        { Kind.create (kKey "Customer") (nm "Customer") (TableId.create "dbo" "CUSTOMER" |> mustOk)
            [ idPk "Customer"; col "Customer" "Email" false; col "Customer" "CityId" false ] with
            References = [ Reference.create (rKey "Customer" "City") (nm "CustomerCity") (aKey "Customer" "CityId") (kKey "City") ] }
    let order =
        { Kind.create (kKey "Order") (nm "Order") (TableId.create "dbo" "ORDERS" |> mustOk)
            [ idPk "Order"; col "Order" "CustomerId" false; col "Order" "Amount" false ] with
            References = [ Reference.create (rKey "Order" "Customer") (nm "OrderCustomer") (aKey "Order" "CustomerId") (kKey "Customer") ] }
    let line =
        { Kind.create (kKey "OrderLine") (nm "OrderLine") (TableId.create "dbo" "ORDERLINE" |> mustOk)
            [ idPk "OrderLine"; col "OrderLine" "OrderId" false; col "OrderLine" "Item" false ] with
            References = [ Reference.create (rKey "OrderLine" "Order") (nm "LineOrder") (aKey "OrderLine" "OrderId") (kKey "Order") ] }
    Catalog.create [ { SsKey = kKey "M"; Name = nm "M"; Kinds = [ city; customer; order; line ]; IsActive = true; ExtendedProperties = [] } ] []
    |> mustOk

let private row (kind: string) (pk: string) (cells: (string * string) list) : StaticRow =
    { Identifier = kKey (kind + pk)
      Values     = ("Id", pk) :: cells |> List.map (fun (k, v) -> nm k, v) |> StaticRow.presentValues }

let private scope = Set.ofList [ kKey "City"; kKey "Customer"; kKey "Order"; kKey "OrderLine" ]

// -- 1. segmentation --------------------------------------------------------

[<Fact>]
let ``segmentKinds groups the FK-connected kinds into ONE component`` () =
    let comps = TransferImpact.segmentKinds catalog scope
    Assert.Equal(1, List.length comps)
    Assert.Equal(4, List.length (List.head comps))

[<Fact>]
let ``segmentKinds separates an unrelated island`` () =
    let island = kKey "Loner"
    let scope2 = Set.add island scope
    // Loner has no edges — its own component. (Absent from the catalog: no edges.)
    let comps = TransferImpact.segmentKinds catalog scope2
    Assert.Equal(2, List.length comps)

// -- 2. classification ------------------------------------------------------

let private baseInputs : TransferImpact.Inputs =
    { Catalog = catalog
      Scope = scope
      Reconciled = Set.singleton (kKey "City")
      Wiped = Set.ofList [ kKey "Customer"; kKey "Order"; kKey "OrderLine" ]
      BusinessKeys = Map.ofList [ kKey "City", nm "Name" ]
      Before = Map.empty
      After = Map.empty
      Ignore = Set.empty
      Roles = Map.empty }

[<Fact>]
let ``classifyKind: a business-keyed kind matches identical rows as Unchanged (surrogate PK excluded)`` () =
    // Same Name, different surrogate Id — the PK is env-specific, never a change.
    let inputs = { baseInputs with
                     Before = Map.ofList [ kKey "City", [ row "City" "501" [ "Name", "Lisbon" ] ] ]
                     After  = Map.ofList [ kKey "City", [ row "City" "1"   [ "Name", "Lisbon" ] ] ] }
    let rows = TransferImpact.classifyKind inputs (kKey "City")
    Assert.Equal(1, List.length rows)
    let (_, ck, _) = List.head rows
    Assert.Equal(TransferImpact.ChangeKind.Unchanged, ck)

[<Fact>]
let ``classifyKind: a business-keyed kind flags a non-key column difference as Changed with a diff`` () =
    let inputs = { baseInputs with
                     Before = Map.ofList [ kKey "City", [ row "City" "501" [ "Name", "Lisbon"; "Region", "EU" ] ] ]
                     After  = Map.ofList [ kKey "City", [ row "City" "1"   [ "Name", "Lisbon"; "Region", "PT" ] ] ] }
    let rows = TransferImpact.classifyKind inputs (kKey "City")
    let (_, ck, diffs) = List.head rows
    Assert.Equal(TransferImpact.ChangeKind.Changed, ck)
    Assert.Equal(1, List.length diffs)
    Assert.Equal("Region", Name.value (List.head diffs).Column)
    Assert.Equal("EU", (List.head diffs).Before)
    Assert.Equal("PT", (List.head diffs).After)

[<Fact>]
let ``classifyKind: a business-keyed kind reports Added and (under wipe) Deleted by key membership`` () =
    let inputs = { baseInputs with
                     Before = Map.ofList [ kKey "City", [ row "City" "501" [ "Name", "Lisbon" ]; row "City" "502" [ "Name", "Porto" ] ] ]
                     After  = Map.ofList [ kKey "City", [ row "City" "1" [ "Name", "Lisbon" ]; row "City" "2" [ "Name", "Braga" ] ] ]
                     Wiped  = Set.add (kKey "City") baseInputs.Wiped }
    let rows = TransferImpact.classifyKind inputs (kKey "City")
    let kinds = rows |> List.map (fun (_, ck, _) -> ck)
    Assert.Contains(TransferImpact.ChangeKind.Added, kinds)     // Braga
    Assert.Contains(TransferImpact.ChangeKind.Deleted, kinds)   // Porto
    Assert.Contains(TransferImpact.ChangeKind.Unchanged, kinds) // Lisbon

[<Fact>]
let ``classifyKind: a NO-business-key wiped kind is delete-all (before) + add-all (after)`` () =
    let inputs = { baseInputs with
                     Before = Map.ofList [ kKey "Customer", [ row "Customer" "77" [ "Email", "old@x"; "CityId", "501" ] ] ]
                     After  = Map.ofList [ kKey "Customer", [ row "Customer" "1" [ "Email", "new@x"; "CityId", "1" ] ] ] }
    let rows = TransferImpact.classifyKind inputs (kKey "Customer")
    let kinds = rows |> List.map (fun (_, ck, _) -> ck) |> List.sort
    Assert.Equal<TransferImpact.ChangeKind list>([ TransferImpact.ChangeKind.Added; TransferImpact.ChangeKind.Deleted ] |> List.sort, kinds)

// -- 3. denormalization (nested documents) ----------------------------------

[<Fact>]
let ``build: an added Customer nests its Order and OrderLine and inlines its City ref`` () =
    let inputs =
        { baseInputs with
            Before = Map.ofList [ kKey "City", [ row "City" "1" [ "Name", "Lisbon" ] ] ]
            After  =
                Map.ofList
                    [ kKey "City",      [ row "City" "1" [ "Name", "Lisbon" ] ]
                      kKey "Customer",  [ row "Customer" "10" [ "Email", "bob@x"; "CityId", "1" ] ]
                      kKey "Order",     [ row "Order" "90" [ "CustomerId", "10"; "Amount", "120" ] ]
                      kKey "OrderLine", [ row "OrderLine" "900" [ "OrderId", "90"; "Item", "Widget" ] ] ] }
    let impact = TransferImpact.build "golden" "replace" inputs
    Assert.Equal(1, List.length impact.Segments)
    let seg = List.head impact.Segments
    Assert.Contains(kKey "Customer", seg.Roots)
    // One document (the added Customer), with the Order nested and the City inlined.
    let cust = seg.Documents |> List.find (fun d -> d.Kind = kKey "Customer")
    Assert.Equal(TransferImpact.ChangeKind.Added, cust.Change)
    Assert.NotEmpty cust.Refs                     // City inlined
    let (_, orders) = cust.Children |> List.find (fun (label, _) -> label = "OrderCustomer")
    let order = List.head orders
    Assert.Equal(kKey "Order", order.Kind)
    let (_, lines) = order.Children |> List.find (fun (label, _) -> label = "LineOrder")
    Assert.Equal(kKey "OrderLine", (List.head lines).Kind)   // recursively nested

[<Fact>]
let ``build: totals sum the per-table deltas across segments`` () =
    let inputs =
        { baseInputs with
            Before = Map.ofList [ kKey "City", [ row "City" "1" [ "Name", "Lisbon" ] ] ]
            After  = Map.ofList [ kKey "City", [ row "City" "1" [ "Name", "Lisbon" ]; row "City" "2" [ "Name", "Porto" ] ] ] }
    let impact = TransferImpact.build "golden" "replace" inputs
    Assert.Equal(1, impact.Totals.Added)       // Porto
    Assert.Equal(1, impact.Totals.Unchanged)   // Lisbon

// -- 4. the HTML artifact + JSON twin (TransferImpactView) -------------------

open Projection.Cli

let private richImpact () =
    let inputs =
        { baseInputs with
            Before = Map.ofList [ kKey "City", [ row "City" "1" [ "Name", "Lisbon" ] ] ]
            After  =
                Map.ofList
                    [ kKey "City",      [ row "City" "1" [ "Name", "Lisbon" ] ]
                      kKey "Customer",  [ row "Customer" "10" [ "Email", "bob@x"; "CityId", "1" ] ]
                      kKey "Order",     [ row "Order" "90" [ "CustomerId", "10"; "Amount", "120" ] ]
                      kKey "OrderLine", [ row "OrderLine" "900" [ "OrderId", "90"; "Item", "Widget" ] ] ] }
    TransferImpact.build "golden" "replace" inputs

[<Fact>]
let ``toHtml renders a self-contained page with the nested segment and doc cards`` () =
    let html = TransferImpactView.toHtml catalog (richImpact ())
    Assert.StartsWith("<!doctype html>", html)
    Assert.Contains("<title>Transfer impact — golden</title>", html)
    Assert.Contains("class=\"segment\"", html)
    Assert.Contains("class=\"doc add\"", html)          // the added Customer
    Assert.Contains("owned children", html)              // the nested Order
    Assert.Contains("references", html)                  // the inlined City
    Assert.Contains("rows added", html)                  // the delta tiles
    Assert.DoesNotContain("<script", html)               // no JS; CSP-safe static artifact

[<Fact>]
let ``toJson emits a parseable machine twin with totals and nested children`` () =
    let json = TransferImpactView.toJson catalog (richImpact ())
    let doc = System.Text.Json.JsonDocument.Parse(json)
    let root = doc.RootElement
    Assert.Equal("golden", root.GetProperty("flow").GetString())
    Assert.Equal(3, root.GetProperty("totals").GetProperty("added").GetInt32())   // Customer + Order + OrderLine
    let seg0 = root.GetProperty("segments").[0]
    let cust = seg0.GetProperty("documents").[0]
    Assert.Equal("Customer", cust.GetProperty("kind").GetString())
    Assert.True(cust.GetProperty("children").GetArrayLength() > 0)

// -- 5. the scale surfaces: summary matrix, relational intent, 1:1 confirmation --

/// City carries a static-lookup role with an identity verdict; the payload kinds
/// take the default role. This is what the scaled artifact renders above the detail.
let private scaledImpact (cityVerdict: string) : TransferImpact.Impact =
    let roles =
        Map.ofList
            [ kKey "City",
              ({ Variety   = "static-lookup"
                 Reason    = "country reference — must be identical in both environments"
                 Guarantee = "matched by natural key; every column identical, no extra or missing rows"
                 Key       = Some "Name"
                 Verdict   = Some cityVerdict } : TransferImpact.RelationalRole) ]
    let inputs =
        { baseInputs with
            Reconciled = Set.singleton (kKey "City")
            Roles = roles
            Before = Map.ofList [ kKey "City", [ row "City" "1" [ "Name", "Lisbon" ] ] ]
            After  =
                Map.ofList
                    [ kKey "City",      [ row "City" "1" [ "Name", "Lisbon" ] ]
                      kKey "Customer",  [ row "Customer" "10" [ "Email", "bob@x"; "CityId", "1" ] ]
                      kKey "Order",     [ row "Order" "90" [ "CustomerId", "10"; "Amount", "120" ] ]
                      kKey "OrderLine", [ row "OrderLine" "900" [ "OrderId", "90"; "Item", "Widget" ] ] ] }
    TransferImpact.build "golden" "replace" inputs

[<Fact>]
let ``build: the summary matrix carries every scope kind, payload sorted before supporting`` () =
    let impact = scaledImpact "identical (1/1 by Name)"
    Assert.Equal(4, List.length impact.Summary)
    // payloadRank 0 sorts ahead of static-lookup's rank 1: the last row is City.
    let last = List.last impact.Summary
    Assert.Equal(kKey "City", last.Kind)
    Assert.Equal("static-lookup", last.Role.Variety)

[<Fact>]
let ``toHtml renders the summary matrix grouped by role and the relational-intent card`` () =
    let html = TransferImpactView.toHtml catalog (scaledImpact "identical (1/1 by Name)")
    Assert.Contains("Every table, by relational role", html)
    Assert.Contains("class=\"matrix\"", html)
    Assert.Contains("class=\"grp\"", html)                          // a role group header
    Assert.Contains("Static lookup", html)                         // the group label
    Assert.Contains("Relational intent", html)                     // the intent section
    Assert.Contains("class=\"intent\"", html)
    Assert.Contains("Why check?", html)                            // the why-1:1 prose

[<Fact>]
let ``toHtml renders the 1:1 confirmation panel, clean when identical`` () =
    let html = TransferImpactView.toHtml catalog (scaledImpact "identical (1/1 by Name)")
    Assert.Contains("the 1:1 confirmation", html)
    Assert.Contains("1 of 1 reference table(s) verified", html)
    Assert.Contains("class=\"verdict v-ok\"", html)                // clean verdict, teal
    Assert.DoesNotContain("class=\"cf drift\"", html)              // no drift row

[<Fact>]
let ``toHtml pulls a drifted static-lookup into the confirmation panel as drift`` () =
    let html = TransferImpactView.toHtml catalog (scaledImpact "drift: 1 col drift on Lisbon")
    Assert.Contains("class=\"verdict v-drift\"", html)             // drift verdict, red
    Assert.Contains("class=\"cf drift\"", html)                    // the drift confirmation cell
    Assert.Contains("0 of 1 reference table(s) verified", html)

[<Fact>]
let ``toJson summary array carries the role, guarantee, and verdict per table`` () =
    let json = TransferImpactView.toJson catalog (scaledImpact "identical (1/1 by Name)")
    let root = (System.Text.Json.JsonDocument.Parse json).RootElement
    let summary = root.GetProperty("summary")
    Assert.Equal(4, summary.GetArrayLength())
    let city = seq { for i in 0 .. summary.GetArrayLength() - 1 -> summary.[i] }
               |> Seq.find (fun e -> e.GetProperty("table").GetString() = "City")
    Assert.Equal("static-lookup", city.GetProperty("role").GetString())
    Assert.Equal("Name", city.GetProperty("matchedBy").GetString())
    Assert.Equal("identical (1/1 by Name)", city.GetProperty("verdict").GetString())
    Assert.False(System.String.IsNullOrEmpty(city.GetProperty("guarantee").GetString()))
