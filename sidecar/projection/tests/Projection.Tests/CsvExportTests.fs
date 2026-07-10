module Projection.Tests.CsvExportTests

// THE CSV DATA REALIZATION (2026-07-10, the csv-destination program): pure
// witnesses over the renderer and the referenced-pull vocabulary. The laws
// under test: every cell VALUE round-trips byte-faithfully through the
// LIBRARY's own parser (write with CsvHelper, read with CsvHelper — no
// hand-rolled parsing on either side); the header is the kind's canonical
// physical writable columns; records terminate CRLF including the last;
// NULL/"" renders as an empty field; the manifest is deterministic and
// carries provenance; the referenced pull closes transitively, stops at
// static kinds, dedups by key, and returns exactly closure-minus-declared.

open System.Globalization
open Xunit
open CsvHelper
open CsvHelper.Configuration
open Projection.Core
open Projection.Pipeline
open Projection.Targets.Data

let private nm (s: string) : Name = Name.create s |> Result.value
let private kKey (s: string) : SsKey = SsKey.synthesizedComposite "CSV_KIND" [ s ] |> Result.value
let private aKey (k: string) (a: string) : SsKey = SsKey.synthesizedComposite "CSV_ATTR" [ k; a ] |> Result.value
let private rKey (k: string) (r: string) : SsKey = SsKey.synthesizedComposite "CSV_REF" [ k; r ] |> Result.value

let private idPk (kind: string) : Attribute =
    { Attribute.create (aKey kind "Id") (nm "Id") Integer with
        Column = ColumnRealization.create "ID" false |> Result.value
        IsPrimaryKey = true; IsIdentity = true; IsMandatory = true }

let private textCol (kind: string) (logical: string) (physical: string) : Attribute =
    { Attribute.create (aKey kind logical) (nm logical) Text with
        Column = ColumnRealization.create physical false |> Result.value
        Length = Some 200 }

let private fkCol (kind: string) (logical: string) : Attribute =
    { Attribute.create (aKey kind logical) (nm logical) Integer with
        Column = ColumnRealization.create (logical.ToUpperInvariant()) false |> Result.value }

let private customer : Kind =
    { Kind.create (kKey "Customer") (nm "Customer") (TableId.create "dbo" "OSUSR_CSV_CUSTOMER" |> Result.value)
        [ idPk "Customer"; textCol "Customer" "Email" "EMAIL"; textCol "Customer" "Notes" "NOTES"; fkCol "Customer" "RegionId" ] with
        References = [ Reference.create (rKey "Customer" "Region") (nm "RegionId") (aKey "Customer" "RegionId") (kKey "Region") ] }

let private row (kind: Kind) (values: (string * string) list) : StaticRow =
    { Identifier = kind.SsKey; Values = values |> List.map (fun (c, v) -> nm c, v) |> Map.ofList }

/// Parse csv text back through THE LIBRARY (never by hand): the cell matrix.
let private parseBack (text: string) : string list list =
    let cfg = CsvConfiguration(CultureInfo.InvariantCulture, Mode = CsvMode.RFC4180)
    use reader = new System.IO.StringReader(text)
    use parser = new CsvParser(reader, cfg)
    let rec go acc =
        if parser.Read() then
            let record =
                match parser.Record with
                | null -> []
                | r -> List.ofArray r
            go (record :: acc)
        else List.rev acc
    go []

// -- the renderer ----------------------------------------------------------

[<Fact>]
let ``every hostile cell value round-trips byte-faithfully through the library's own parser`` () =
    let hostile =
        [ "plain"; ""; "  padded  "; "comma,inside"; "quote\"inside"
          "line\nbreak"; "crlf\r\nbreak"; "both\",messy\r\n\"ends"
          "ünïcode✓"; "1.5"; "1,5"; "\t tab" ]
    let rows =
        hostile |> List.mapi (fun i v ->
            row customer [ "Id", string (i + 1); "Email", v; "Notes", "n" + string i; "RegionId", "" ])
    let cells = parseBack (CsvExport.tableCsv customer rows)
    Assert.Equal<string list>(CsvExport.headerCells customer, List.head cells)
    List.zip hostile (List.tail cells)
    |> List.iteri (fun i (expected, record) ->
        Assert.Equal<string list>([ string (i + 1); expected; "n" + string i; "" ], record))

[<Fact>]
let ``the header is the kind's canonical physical writable columns, in order`` () =
    Assert.Equal<string list>([ "ID"; "EMAIL"; "NOTES"; "REGIONID" ], CsvExport.headerCells customer)
    let text = CsvExport.tableCsv customer []
    Assert.Equal<string list list>([ [ "ID"; "EMAIL"; "NOTES"; "REGIONID" ] ], parseBack text)

[<Fact>]
let ``records terminate CRLF, the final record included — deterministic bytes`` () =
    let text = CsvExport.tableCsv customer [ row customer [ "Id", "1"; "Email", "a@x"; "Notes", ""; "RegionId", "" ] ]
    Assert.EndsWith("\r\n", text)
    Assert.Equal(2, text.Split("\r\n") |> Array.filter (fun s -> s <> "") |> Array.length)
    // determinism: same rows, same bytes.
    Assert.Equal<string>(text, CsvExport.tableCsv customer [ row customer [ "Id", "1"; "Email", "a@x"; "Notes", ""; "RegionId", "" ] ])

[<Fact>]
let ``an absent value and an empty value both render as an empty field — the NULL convention, documented`` () =
    let withAbsent = { Identifier = customer.SsKey; Values = Map.ofList [ nm "Id", "1" ] }   // Email/Notes/RegionId absent
    let cells = parseBack (CsvExport.tableCsv customer [ withAbsent ])
    Assert.Equal<string list>([ "1"; ""; ""; "" ], cells |> List.item 1)

[<Fact>]
let ``the file is named by the physical table`` () =
    Assert.Equal<string>("OSUSR_CSV_CUSTOMER.csv", CsvExport.fileNameFor customer)

// -- the manifest ------------------------------------------------------------

[<Fact>]
let ``the manifest is deterministic, (module, entity)-sorted, and carries the ordered column mapping + provenance`` () =
    let e1 = CsvExport.manifestEntry (nm "Sales") customer 3 CsvExport.Provenance.Declared
    let e2 = { CsvExport.manifestEntry (nm "Core") customer 2 CsvExport.Provenance.Referenced with Entity = "Region"; PhysicalTable = "OSUSR_CSV_REGION" }
    let a = CsvExport.manifestJson [ e1; e2 ]
    let b = CsvExport.manifestJson [ e2; e1 ]
    Assert.Equal<string>(a, b)                       // order-free input, sorted output
    Assert.DoesNotContain("timestamp", a)
    let doc = System.Text.Json.JsonDocument.Parse a
    let tables = doc.RootElement.GetProperty "tables"
    Assert.Equal("Core", (tables.[0].GetProperty "module").GetString())   // Core sorts before Sales
    Assert.Equal("referenced", (tables.[0].GetProperty "provenance").GetString())
    Assert.Equal("declared", (tables.[1].GetProperty "provenance").GetString())
    Assert.Equal(3, (tables.[1].GetProperty "rowCount").GetInt32())
    let cols = tables.[1].GetProperty "columns"
    Assert.Equal("ID", (cols.[0].GetProperty "physical").GetString())
    Assert.Equal("Id", (cols.[0].GetProperty "logical").GetString())
    Assert.Equal("OSUSR_CSV_CUSTOMER.csv", (tables.[1].GetProperty "file").GetString())

// -- the referenced pull -------------------------------------------------------

/// Region (non-static) referenced by Customer; Country STATIC (the OSSYS
/// flag-only marking: `Static []`), referenced by Region — so a transitive
/// pull must fetch Region and STOP there.
let private region : Kind =
    { Kind.create (kKey "Region") (nm "Region") (TableId.create "dbo" "OSUSR_CSV_REGION" |> Result.value)
        [ idPk "Region"; textCol "Region" "Name" "NAME"; fkCol "Region" "CountryId" ] with
        References = [ Reference.create (rKey "Region" "Country") (nm "CountryId") (aKey "Region" "CountryId") (kKey "Country") ] }

let private country : Kind =
    { Kind.create (kKey "Country") (nm "Country") (TableId.create "dbo" "OSUSR_CSV_COUNTRY" |> Result.value)
        [ idPk "Country"; textCol "Country" "Code" "CODE" ] with
        Modality = [ ModalityMark.Static [] ] }

let private pullCatalog : Catalog =
    let m =
        Module.create (SsKey.synthesizedComposite "CSV_MOD" [ "Trade" ] |> Result.value)
            (nm "Trade") [ customer; region; country ] true []
        |> Result.value
    Catalog.create [ m ] [] |> Result.value

[<Fact>]
let ``staticKinds fires on the OSSYS flag-only marking (Static []) — never on absent populations alone`` () =
    let statics = CsvReferencedPull.staticKinds pullCatalog
    Assert.Equal<Set<SsKey>>(Set.ofList [ kKey "Country" ], statics)
    Assert.False(Set.contains (kKey "Region") statics)   // no Static mark, populations equally absent

[<Fact>]
let ``the pull closes transitively from the subset's own rows, stops at static kinds, and dedups by key`` () =
    // Two customers point at region 7 (dedup: one fetch key), one at region 8;
    // regions point at country 100 — which the filter must hold out.
    let ingested =
        Map.ofList
            [ customer.SsKey,
              [ row customer [ "Id", "1"; "Email", "a@x"; "Notes", ""; "RegionId", "7" ]
                row customer [ "Id", "2"; "Email", "b@x"; "Notes", ""; "RegionId", "7" ]
                row customer [ "Id", "3"; "Email", "c@x"; "Notes", ""; "RegionId", "8" ] ] ]
    let regionRows =
        Map.ofList
            [ "7", row region [ "Id", "7"; "Name", "North"; "CountryId", "100" ]
              "8", row region [ "Id", "8"; "Name", "South"; "CountryId", "100" ] ]
    let statics = CsvReferencedPull.staticKinds pullCatalog
    // A Map-backed fake oracle: the drive is the pure planner + the filter.
    let mutable fetchedKinds : SsKey list = []
    let fakeFetch (f: Closure.RowKeyFetch) : Closure.FetchedRows =
        fetchedKinds <- f.Kind :: fetchedKinds
        { Kind = f.Kind
          Rows = f.Keys |> Set.toList |> List.choose (fun k -> Map.tryFind k regionRows) }
    let rec drive state pending =
        let state', planned = Closure.stepWith [] pullCatalog state pending
        match planned |> List.filter (CsvReferencedPull.keepFetch statics) with
        | []      -> state'
        | fetches -> drive state' (fetches |> List.map fakeFetch)
    let closed = Closure.materialize (drive Closure.empty (CsvReferencedPull.rootsOf ingested))
    // Region was fetched once with both keys deduped; Country never fetched.
    Assert.Equal<SsKey list>([ region.SsKey ], fetchedKinds)
    Assert.False(closed.ContainsKey country.SsKey)
    Assert.Equal(2, closed.[region.SsKey] |> List.length)
    // pulled = closure minus the declared subset.
    let pulled = CsvReferencedPull.pulledRows (Set.ofList [ customer.SsKey ]) closed
    Assert.Equal<Set<SsKey>>(Set.ofList [ region.SsKey ], pulled |> Map.toList |> List.map fst |> Set.ofList)
    Assert.Equal(3, closed.[customer.SsKey] |> List.length)   // the subset itself stayed closed over
