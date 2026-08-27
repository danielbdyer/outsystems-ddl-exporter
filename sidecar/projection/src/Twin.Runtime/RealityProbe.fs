namespace Twin.Runtime
// LINT-ALLOW-FILE: the string-reality probe — ADO.NET aggregate queries at the
//   command boundary (terminal probe SQL; identifiers pass through the SSDT
//   renderer's quoting) and the imperative reader/walk state the driver seam
//   requires. Boundary-confined; nothing escapes the module.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Twin.Core

/// THE TWIN — the string-plane reality probe (Twin.Runtime).
///
/// Runs during `twin evidence import` (and over the minted twin for the
/// fidelity audit) with NO configuration: for every text column the
/// capture evidenced, two aggregate queries discover the counts the
/// deploy engine actually trips on — empty strings (distinct from NULL),
/// trailing spaces (the unique-index pad-fold), case collisions under
/// UPPER (what a CI-collation unique add refuses; bounded-length columns
/// only — the indexable ones), and the LEN median/p90. Counts only,
/// never values: the pack stays masked by construction. Full-scan
/// aggregates — one pass each, the same order of cost as the profiler's
/// own exact counts.
[<RequireQualifiedAccess>]
module RealityProbe =

    /// Collision discovery runs only where a unique index could exist:
    /// SQL Server's 1700-byte index-key ceiling puts the practical bound
    /// near NVARCHAR(450), and DISTINCT cannot compare MAX types at all.
    [<Literal>]
    let private CollisionLengthCeiling = 450

    let private probeFailed (table: string) (column: string) (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.probeFailed"
            "The string-reality probe did not complete for a column."
            (Map.ofList [ "table", Some table; "column", Some column; "detail", Some detail ])

    let private quote (s: string) : string = Projection.Targets.SSDT.Render.quote s

    let private probeColumn
        (cnn: SqlConnection)
        (tableSql: string)
        (coordText: string)
        (columnName: string)
        (boundedLength: bool)
        : Task<Result<TextShape option>> =
        task {
            try
                let col = quote columnName
                // The DISTINCT comparisons run under a BINARY collation:
                // a CI database collation would fold the case-variants
                // together and count zero collisions — the very reality
                // this statistic exists to see.
                let collisions =
                    if boundedLength then
                        System.String.Concat(
                            "COUNT_BIG(DISTINCT ", col, " COLLATE Latin1_General_BIN2) - COUNT_BIG(DISTINCT UPPER(",
                            col, ") COLLATE Latin1_General_BIN2)")
                    else "CAST(0 AS BIGINT)"
                use cmd = cnn.CreateCommand()
                cmd.CommandText <-
                    System.String.Concat(
                        "SELECT COUNT_BIG(CASE WHEN DATALENGTH(", col, ") = 0 THEN 1 END), ",
                        "COUNT_BIG(CASE WHEN DATALENGTH(", col, ") <> DATALENGTH(RTRIM(", col, ")) THEN 1 END), ",
                        collisions,
                        " FROM ", tableSql, " WHERE ", col, " IS NOT NULL;")
                use! reader = cmd.ExecuteReaderAsync()
                let! hasRow = reader.ReadAsync()
                if not hasRow then return Result.success None
                else
                    let empty = reader.GetInt64 0
                    let trailing = reader.GetInt64 1
                    let coll = if reader.IsDBNull 2 then 0L else reader.GetInt64 2
                    do! reader.CloseAsync()
                    use quantiles = cnn.CreateCommand()
                    quantiles.CommandText <-
                        System.String.Concat(
                            "SELECT DISTINCT ",
                            "PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY LEN(", col, ")) OVER () AS p50, ",
                            "PERCENTILE_CONT(0.9) WITHIN GROUP (ORDER BY LEN(", col, ")) OVER () AS p90 ",
                            "FROM ", tableSql, " WHERE ", col, " IS NOT NULL;")
                    use! qReader = quantiles.ExecuteReaderAsync()
                    let! hasQ = qReader.ReadAsync()
                    let p50 = if hasQ && not (qReader.IsDBNull 0) then Some (int (System.Math.Round(qReader.GetDouble 0))) else None
                    let p90 = if hasQ && not (qReader.IsDBNull 1) then Some (int (System.Math.Round(qReader.GetDouble 1))) else None
                    if empty = 0L && trailing = 0L && coll = 0L && p50.IsNone && p90.IsNone then
                        return Result.success None
                    else
                        return
                            Result.success
                                (Some
                                    { EmptyCount = empty
                                      TrailingSpaceCount = trailing
                                      CaseCollisions = coll
                                      LengthP50 = p50
                                      LengthP90 = p90 })
            with ex ->
                return Result.failure [ probeFailed coordText columnName ex.Message ]
        }

    /// Enrich a freshly captured pack with the string-plane counts for
    /// every text column it evidences, over the same open connection the
    /// capture used. No configuration: the discovery is total over the
    /// pack's own text columns.
    let enrich
        (cnn: SqlConnection)
        (bound: (string * Kind) list)
        (pack: EvidencePack)
        : Task<Result<EvidencePack>> =
        task {
            let shapes = System.Collections.Generic.Dictionary<string * string, TextShape>()
            let mutable failed : ValidationError list = []
            let mutable remaining = bound
            while not (List.isEmpty remaining) && List.isEmpty failed do  // LINT-ALLOW: the while-walk's cursor — FS3511 forbids a tuple-element `for` with an await in the body
                let entry = List.head remaining
                remaining <- List.tail remaining  // LINT-ALLOW: the while-walk's cursor advance; same FS3511 confinement
                let coordText = fst entry
                let kind = snd entry
                let evidenced =
                    pack.Tables
                    |> List.tryFind (fun t -> System.String.Equals(t.Table, coordText, System.StringComparison.OrdinalIgnoreCase))
                match evidenced with
                | None -> ()
                | Some table ->
                    let tableSql = Projection.Targets.SSDT.Render.tableQualified kind.Physical
                    let mutable attrs =
                        kind.Attributes
                        |> List.filter (fun a ->
                            a.Type = PrimitiveType.Text
                            && table.Columns
                               |> List.exists (fun c ->
                                   System.String.Equals(c.Column, ColumnRealization.columnNameText a.Column, System.StringComparison.OrdinalIgnoreCase)))
                    while not (List.isEmpty attrs) && List.isEmpty failed do  // LINT-ALLOW: the while-walk's cursor; FS3511 confinement as above
                        let attr = List.head attrs
                        attrs <- List.tail attrs  // LINT-ALLOW: the while-walk's cursor advance; same FS3511 confinement
                        let columnName = ColumnRealization.columnNameText attr.Column
                        let bounded =
                            match attr.Length with
                            | Some l -> l <= CollisionLengthCeiling
                            | None -> false
                        let! probed = probeColumn cnn tableSql coordText columnName bounded
                        match probed with
                        | Error es -> failed <- es
                        | Ok None -> ()
                        | Ok (Some shape) ->
                            shapes.[(coordText.ToLowerInvariant(), columnName.ToLowerInvariant())] <- shape
            if not (List.isEmpty failed) then return Result.failure failed
            else
                let enriched =
                    { pack with
                        Tables =
                            pack.Tables
                            |> List.map (fun t ->
                                { t with
                                    Columns =
                                        t.Columns
                                        |> List.map (fun c ->
                                            match shapes.TryGetValue((t.Table.ToLowerInvariant(), c.Column.ToLowerInvariant())) with
                                            | true, shape ->
                                                { c with
                                                    Text = Some shape
                                                    // The kernel's raw-value convention makes
                                                    // `""` the NULL sentinel end to end, so a
                                                    // real empty string cannot ride the
                                                    // vocabulary channel — its count lives in
                                                    // TextShape.EmptyCount and the witness
                                                    // floor re-plants it exactly.
                                                    Frequencies =
                                                        if shape.EmptyCount > 0L then
                                                            c.Frequencies |> List.filter (fun (v, _) -> v <> "")
                                                        else c.Frequencies }
                                            | false, _ -> c) }) }
                return Result.success enriched
        }
