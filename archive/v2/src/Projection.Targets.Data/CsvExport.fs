namespace Projection.Targets.Data

// LINT-ALLOW-FILE: terminal CSV / JSON text boundary — the RFC 4180 byte
//   layout is delegated to CsvHelper (quoting, escaping, delimiters, record
//   terminators live in the library, not in hand-rolled string assembly);
//   the manifest rides the System.Text.Json typed DOM. The value plane stays
//   typed upstream (Kind / StaticRow / KindColumns); this module only
//   composes cells functionally and hands them to the writers.

open System.Globalization
open System.Text.Json
open System.Text.Json.Nodes
open CsvHelper
open CsvHelper.Configuration
open Projection.Core

/// THE CSV DATA REALIZATION (2026-07-10, the csv-destination program): the
/// renderer from a kind's rows to RFC 4180 text — one file per table,
/// physical table name, physical column headers — plus the export manifest
/// carrying the logical names, the column mapping, the row counts, and each
/// table's provenance (declared in the flow's `tables`, or pulled in by the
/// referential closure). A realization of the same `(rows, kind)` plane the
/// SQL emitters consume (A35/A36: the plan is realization-neutral); it
/// renders text and never touches a connection.
///
/// Every public function is a VALUE function (`Kind -> StaticRow list ->
/// string`): the cell matrix is composed by pure `List.map` pipelines, and
/// the one imperative seam — CsvHelper's `CsvWriter` over an in-memory
/// `StringWriter` — is sealed inside `csvText`, so callers observe
/// referential transparency.
///
/// NULL vs the empty string (WP-3, F11 — RFC 4180 has no native NULL, so
/// the convention is DELIBERATE and carried in `export-manifest.json`):
/// a SQL NULL renders as a BARE empty field (`a,,b`); a genuine empty
/// string renders as a QUOTED empty field (`a,"",b`). Both are valid
/// RFC 4180; parsers that don't care read both as empty, parsers that do
/// can recover the distinction the read substrate now preserves.
[<RequireQualifiedAccess>]
module CsvExport =

    /// The one writer configuration: RFC 4180 mode, invariant culture, CRLF
    /// record terminators — deterministic bytes from deterministic cells.
    /// The `ShouldQuote` policy widens the library's default by one case: a
    /// field carrying a BARE LF also quotes. Under `NewLine = CRLF` the
    /// library only quotes on its configured newline, yet its own parser (and
    /// every real-world consumer) treats a lone LF as a record break — the
    /// probe that pinned this lives in the round-trip property test.
    let private configuration () : CsvConfiguration =
        let cfg =
            CsvConfiguration(
                CultureInfo.InvariantCulture,
                NewLine = "\r\n",
                Mode = CsvMode.RFC4180)
        cfg.ShouldQuote <-
            fun args ->
                match args.Field with
                | null -> false
                | f -> f.IndexOfAny [| ','; '"'; '\r'; '\n' |] >= 0
        cfg

    /// The terminal seam: a cell matrix to RFC 4180 text through CsvHelper.
    /// Pure at the boundary — same rows in, same string out. A `None` cell
    /// is SQL NULL → a bare empty field (quoting suppressed); a `Some ""`
    /// cell is a genuine empty string → a FORCE-QUOTED `""` field (the
    /// manifest-documented convention); any other value rides the
    /// configured quote policy.
    let private csvText (rows: string option list list) : string =
        use sw = new System.IO.StringWriter()
        use csv = new CsvWriter(sw, configuration ())
        rows
        |> List.iter (fun cells ->
            cells
            |> List.iter (fun cell ->
                match cell with
                | None      -> csv.WriteField("", false)
                | Some ""   -> csv.WriteField("", true)
                | Some v    -> csv.WriteField v)
            csv.NextRecord())
        csv.Flush()
        sw.ToString()

    /// The header cells: the kind's PHYSICAL column names in its canonical
    /// writable order (`KindColumns.orderedColumnNames` — computed columns
    /// excluded, the same vocabulary every SQL emitter writes).
    let headerCells (kind: Kind) : string list =
        KindColumns.orderedColumnNames kind

    /// One record's cells: the writable attributes in canonical order; each
    /// cell is the row's raw cell for that attribute (`None` — NULL — where
    /// absent). No escaping here — cells are VALUES; the byte layout is the
    /// writer's.
    let rowCells (kind: Kind) (row: StaticRow) : string option list =
        KindColumns.writableAttributes kind
        |> List.map (fun a -> StaticRow.value a.Name row)

    /// The whole file: header then records, CRLF-terminated (the final
    /// record included — one canonical choice keeps the bytes deterministic).
    let tableCsv (kind: Kind) (rows: StaticRow list) : string =
        (headerCells kind |> List.map Some) :: List.map (rowCells kind) rows
        |> csvText

    /// The file name: the physical table, `.csv` — `OSUSR_ABC_CUSTOMER.csv`.
    let fileNameFor (kind: Kind) : string =
        TableId.tableText kind.Physical + ".csv"

    /// How a table entered the export: named in the flow's `tables`, or
    /// pulled in because a foreign key of the exported set points at it.
    [<RequireQualifiedAccess>]
    type Provenance =
        | Declared
        | Referenced

    let provenanceLabel (p: Provenance) : string =
        match p with
        | Provenance.Declared   -> "declared"
        | Provenance.Referenced -> "referenced"

    /// One table's manifest record. `Columns` is (physical, logical) pairs
    /// IN COLUMN ORDER — a list, not a map, because the order is part of the
    /// contract (it is the CSV's column order).
    type ManifestEntry =
        { Module        : string
          Entity        : string
          PhysicalTable : string
          Columns       : (string * string) list
          RowCount      : int
          Provenance    : Provenance }

    /// Build one entry from the kind + its row count.
    let manifestEntry (moduleName: Name) (kind: Kind) (rowCount: int) (prov: Provenance) : ManifestEntry =
        { Module        = Name.value moduleName
          Entity        = Name.value kind.Name
          PhysicalTable = TableId.tableText kind.Physical
          Columns       =
            KindColumns.writableAttributes kind
            |> List.map (fun a -> ColumnRealization.columnNameText a.Column, Name.value a.Name)
          RowCount      = rowCount
          Provenance    = prov }

    /// The export-manifest.json body: entries sorted by (module, entity) so
    /// the file is deterministic; no timestamp (determinism is constructed —
    /// the same export produces the same bytes). Columns render as an array
    /// of { "physical": …, "logical": … } objects, in column order.
    let manifestJson (entries: ManifestEntry list) : string =
        let columnNode (physical: string, logical: string) : JsonNode =
            let c = JsonObject()
            c.["physical"] <- JsonValue.Create physical
            c.["logical"] <- JsonValue.Create logical
            c :> JsonNode
        let entryNode (e: ManifestEntry) : JsonNode =
            let o = JsonObject()
            o.["module"] <- JsonValue.Create e.Module
            o.["entity"] <- JsonValue.Create e.Entity
            o.["physicalTable"] <- JsonValue.Create e.PhysicalTable
            o.["file"] <- JsonValue.Create (e.PhysicalTable + ".csv")
            o.["columns"] <- JsonArray(e.Columns |> List.map columnNode |> Array.ofList)
            o.["rowCount"] <- JsonValue.Create e.RowCount
            o.["provenance"] <- JsonValue.Create (provenanceLabel e.Provenance)
            o :> JsonNode
        let root = JsonObject()
        // The NULL encoding contract, carried IN the manifest so external
        // consumers need no side-channel: a bare empty field is SQL NULL;
        // a quoted "" field is a genuine empty string (WP-3, F11).
        root.["nullEncoding"] <- JsonValue.Create "bare empty field = NULL; quoted \"\" = empty string"
        root.["tables"] <-
            JsonArray(entries |> List.sortBy (fun e -> e.Module, e.Entity) |> List.map entryNode |> Array.ofList)
        root.ToJsonString(JsonSerializerOptions(WriteIndented = true))
