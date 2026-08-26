namespace Twin.Core

open System.Text.Json
open Projection.Core

/// THE TWIN — the capture-side drift comparison (Twin.Core, pure).
///
/// Each capture also answers a second question: does this environment's
/// live schema still match the trunk head? The template's schema is
/// always the trunk head — an environment's schema difference is
/// REPORTED as drift, never merged into the model — and the standing
/// drift report is the promotion story's raw material: a column that
/// exists in QA but not in the trunk is a change waiting to be promoted
/// (or retired), and a proof against the template cannot see it until
/// it is.
///
/// The comparison is coordinate-level, one comparator for both
/// renditions: the capture's restrict step already binds each closed-set
/// coordinate to the SOURCE's kind (logical: by physical schema.table;
/// physical: by logical entity name over the OutSystems metamodel), and
/// the same coordinate binds to the TRUNK's kind — so columns compare by
/// their logical names on both sides. A physical-name diff would be
/// wrong by construction for the physical rendition, whose realizations
/// differ from the trunk's on purpose. Compared per column: presence,
/// nullability, declared type with its facets. Renderings carry schema
/// facts only — never a data value.
type DriftEntry = {
    Table  : string
    Column : string option
    /// tableNotInTrunk | columnNotInTrunk | columnMissingInSource |
    /// nullabilityDiffers | typeDiffers
    Kind   : string
    Detail : string
}

type DriftSection = {
    Source  : string
    Entries : DriftEntry list
}

type DriftReport = {
    /// "ok" when the trunk model was acquired; otherwise the named
    /// refusal code — the capture proceeded, the comparison did not.
    Trunk    : string
    Sections : DriftSection list
}

[<RequireQualifiedAccess>]
module EvidenceDrift =

    let private typeText (a: Attribute) : string =
        let facet =
            match a.Length with
            | Some l -> sprintf "(%d)" l
            | None ->
                match a.Precision, a.Scale with
                | Some p, Some s -> sprintf "(%d,%d)" p s
                | Some p, None -> sprintf "(%d)" p
                | _ -> ""
        let nullability = if a.Column.IsNullable then "NULL" else "NOT NULL"
        sprintf "%A%s %s" a.Type facet nullability

    let private columnName (a: Attribute) : string = ColumnRealization.columnNameText a.Column

    /// Compare one bound coordinate: the trunk's kind against the
    /// source's, columns joined by logical column name.
    let private compareKind (table: string) (trunk: Kind) (source: Kind) : DriftEntry list =
        let entries = System.Collections.Generic.List<DriftEntry>()
        let key (a: Attribute) = (columnName a).ToLowerInvariant()
        let trunkCols = trunk.Attributes |> List.map (fun a -> key a, a) |> Map.ofList
        let sourceCols = source.Attributes |> List.map (fun a -> key a, a) |> Map.ofList
        for a in source.Attributes do
            match Map.tryFind (key a) trunkCols with
            | None ->
                entries.Add
                    { Table = table; Column = Some (columnName a); Kind = "columnNotInTrunk"
                      Detail = sprintf "the environment carries %s %s; the trunk does not" (columnName a) (typeText a) }
            | Some t ->
                if t.Column.IsNullable <> a.Column.IsNullable then
                    entries.Add
                        { Table = table; Column = Some (columnName a); Kind = "nullabilityDiffers"
                          Detail = sprintf "trunk %s; environment %s"
                                       (if t.Column.IsNullable then "NULL" else "NOT NULL")
                                       (if a.Column.IsNullable then "NULL" else "NOT NULL") }
                if t.Type <> a.Type || t.Length <> a.Length then
                    entries.Add
                        { Table = table; Column = Some (columnName a); Kind = "typeDiffers"
                          Detail = sprintf "trunk %s; environment %s" (typeText t) (typeText a) }
        for a in trunk.Attributes do
            if not (Map.containsKey (key a) sourceCols) then
                entries.Add
                    { Table = table; Column = Some (columnName a); Kind = "columnMissingInSource"
                      Detail = sprintf "the trunk carries %s %s; the environment does not" (columnName a) (typeText a) }
        List.ofSeq entries

    /// Compare a capture's bound coordinates against the trunk. `bound`
    /// is the restrict step's yield: each closed-set coordinate with the
    /// SOURCE kind it bound to. A coordinate the trunk does not carry is
    /// itself drift (the environment is ahead of the trunk).
    let compare
        (trunkIndex: CatalogIndex)
        (source: string)
        (bound: (string * Kind) list)
        : DriftSection =
        let entries =
            bound
            |> List.sortBy (fun (coordinate, _) -> coordinate.ToLowerInvariant())
            |> List.collect (fun (coordinate, sourceKind) ->
                match TableCoordinate.parse coordinate with
                | Error _ ->
                    [ { Table = coordinate; Column = None; Kind = "tableNotInTrunk"
                        Detail = "the coordinate does not parse against the trunk" } ]
                | Ok coord ->
                    match CatalogIndex.bindKind trunkIndex coord with
                    | Error _ ->
                        [ { Table = coordinate; Column = None; Kind = "tableNotInTrunk"
                            Detail = "the environment carries the table; the trunk does not" } ]
                    | Ok trunkKind -> compareKind coordinate trunkKind sourceKind)
        { Source = source; Entries = entries }

    let entryCount (report: DriftReport) : int =
        report.Sections |> List.sumBy (fun s -> List.length s.Entries)

    let serializeReport (report: DriftReport) : string =
        let options = JsonWriterOptions(Indented = true)
        use stream = new System.IO.MemoryStream()
        (fun () ->
            use writer = new Utf8JsonWriter(stream, options)
            writer.WriteStartObject()
            writer.WriteString("trunk", report.Trunk)
            writer.WriteStartArray "sections"
            for s in report.Sections do
                writer.WriteStartObject()
                writer.WriteString("source", s.Source)
                writer.WriteStartArray "entries"
                for e in s.Entries do
                    writer.WriteStartObject()
                    writer.WriteString("table", e.Table)
                    match e.Column with
                    | Some c -> writer.WriteString("column", c)
                    | None -> ()
                    writer.WriteString("kind", e.Kind)
                    writer.WriteString("detail", e.Detail)
                    writer.WriteEndObject()
                writer.WriteEndArray()
                writer.WriteEndObject()
            writer.WriteEndArray()
            writer.WriteEndObject()) ()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())
