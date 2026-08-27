namespace Twin.Core

open System.Text.Json
open Projection.Core

/// THE TWIN — the crossover merge (Twin.Core, pure).
///
/// Three environments hold the same tables with different realities. A
/// change promotes Dev → QA → UAT, so the template a developer proves
/// against must block wherever the WORST of the three would block. This
/// module merges per-environment evidence packs into one, statistic by
/// statistic, under one governing rule:
///
///   **an extreme survives the merge; an average never replaces one.**
///
/// The per-axis policy (prior art: `Profile.merge`, Profile.fs:1391 and
/// its per-axis operator table at :1224 — worst-case OR/MAX per axis.
/// One deliberate divergence: `Profile.merge` takes MAX(NullCount) and
/// MAX(RowCount) independently, which understates the worst null RATE —
/// 100 rows / 50 nulls merged with 1,000,000 rows / 10,000 nulls reads
/// as 1% there. The crossover picks the max-RATE environment's pair and
/// scales it to the max row count, so both the worst rate and the worst
/// volume survive):
///
/// | Axis | Operator |
/// |---|---|
/// | table / column `RowCount` | MAX |
/// | null rate | max-rate source's `(NullCount, RowCount)`, scaled to the global max `RowCount` with integer ceiling |
/// | `MaxLength` | MAX (`None` is identity) |
/// | `Truncated` / `HasDuplicates` | OR |
/// | `DistinctCount` | MAX (conservative toward masking: over the threshold anywhere means synthesize) |
/// | `Frequencies` | union by value, counts summed |
/// | `Numeric` | `Min`/`Max` exact envelope; interior percentiles RowCount-weighted (monotone by construction: every source's interior sits inside the merged envelope) |
/// | orphans | per-edge union, `OrphanCount` MAX, per-source counts in the report |
/// | selectivity / joints | the max-`DistinctCount` source's whole vector (rank vectors do not union meaningfully) |
/// | fan-outs | per-edge envelope, deduped (the disjoint `Evidence.merge` concatenates; this path may not) |
///
/// Every decision the merge takes is written to a `MergeReport`: which
/// source supplied each winning extreme, rendered literal-free. The pack
/// itself stays lean — provenance lives in the report, which is also
/// where a downstream block gets its "this is QA's reality" attribution.
type CrossoverStatistic = {
    Table     : string
    Column    : string option
    /// (child table, child column, parent table) for edge statistics.
    Edge      : (string * string * string) option
    Statistic : string
    /// The source label whose value won, or "(union)" for axes that
    /// combine every source rather than picking one.
    Winner    : string
    /// (source label, literal-free rendering) per contributing source.
    PerSource : (string * string) list
}

type CrossoverDriftKind =
    | TableNotInTrunk
    | ColumnNotInTrunk
    | EdgeNotInTrunk

type CrossoverDrift = {
    Coordinate : string
    Kind       : CrossoverDriftKind
    Sources    : string list
}

type CrossoverReport = {
    /// (source label, content hash of the input pack) — the inputs the
    /// merge actually read, hash-named so a report is tied to its bytes.
    Inputs       : (string * string) list
    Tier         : EvidenceTier
    Statistics   : CrossoverStatistic list
    Drift        : CrossoverDrift list
    /// (coordinate, reason) — witnesses the plan declined, because the
    /// trunk already enforces the rule the reality would violate, or the
    /// coordinate cannot host one. Filled by the merge run.
    WitnessSkips : (string * string) list
}

/// The trunk's coordinate universe, lowercased for membership tests. An
/// edge exists as a COORDINATE when its child table, child column, and
/// parent table all exist — whether the trunk carries an enforcing
/// reference is a different question (`Evidence.toProfile` owns it).
type TrunkSets = {
    Tables  : Set<string>
    Columns : Set<string * string>
}

[<RequireQualifiedAccess>]
module Crossover =

    let private lower (s: string) = s.ToLowerInvariant()

    let private labelOf (pack: EvidencePack) : string =
        match pack.Sources with
        | [] -> "(unlabeled)"
        | sources -> sources |> List.sort |> String.concat "+"  // LINT-ALLOW: the attribution label is the sorted source names joined; a label IS a string primitive

    let private contentHash (pack: EvidencePack) : string =
        use sha = System.Security.Cryptography.SHA256.Create()
        Evidence.serialize pack
        |> System.Text.Encoding.UTF8.GetBytes
        |> sha.ComputeHash
        |> Array.map (fun b -> b.ToString "x2")
        |> String.concat ""  // LINT-ALLOW: terminal SHA-256 hex-digest fold; the digest text is the byte-pair join, no typed primitive applies

    // ------------------------------------------------------------------
    // The trunk universe and the clamp.
    // ------------------------------------------------------------------

    let trunkSets (index: CatalogIndex) : TrunkSets =
        let kinds = CatalogIndex.kinds index
        let tables =
            kinds |> List.map (fun (coord, _) -> lower (TableCoordinate.text coord)) |> Set.ofList
        let columns =
            kinds
            |> List.collect (fun (coord, kind) ->
                let t = lower (TableCoordinate.text coord)
                kind.Attributes
                |> List.map (fun a -> t, lower (ColumnRealization.columnNameText a.Column)))
            |> Set.ofList
        { Tables = tables; Columns = columns }

    /// Drop every coordinate the trunk does not carry, and say so. A QA
    /// or UAT schema difference is DRIFT — reported, never merged into
    /// the template's model. Run per input pack, before the merge, so
    /// each drift entry names the environment it came from.
    let clamp (trunk: TrunkSets) (pack: EvidencePack) : EvidencePack * CrossoverDrift list =
        let sources = pack.Sources
        let drift = System.Collections.Generic.List<CrossoverDrift>()
        let tableOk (t: string) = Set.contains (lower t) trunk.Tables
        let columnOk (t: string) (c: string) = Set.contains (lower t, lower c) trunk.Columns
        let edgeOk (child: string) (col: string) (parent: string) =
            tableOk child && tableOk parent && columnOk child col
        let dropTable (t: string) =
            drift.Add { Coordinate = t; Kind = TableNotInTrunk; Sources = sources }
        let dropColumn (t: string) (c: string) =
            drift.Add { Coordinate = System.String.Concat(t, ".", c); Kind = ColumnNotInTrunk; Sources = sources }  // LINT-ALLOW: terminal drift-coordinate rendering (table.column); the composite key IS the report's coordinate text
        let dropEdge (child: string) (col: string) (parent: string) =
            drift.Add
                { Coordinate = System.String.Concat(child, ".", col, " -> ", parent)  // LINT-ALLOW: terminal drift-coordinate rendering (edge arrow); the composite key IS the report's coordinate text
                  Kind = EdgeNotInTrunk; Sources = sources }
        let tables =
            pack.Tables
            |> List.choose (fun t ->
                if not (tableOk t.Table) then dropTable t.Table; None
                else
                    let columns =
                        t.Columns
                        |> List.filter (fun c ->
                            if columnOk t.Table c.Column then true
                            else (dropColumn t.Table c.Column; false))
                    Some { t with Columns = columns })
        let fanOuts =
            pack.FanOuts
            |> List.filter (fun f ->
                if edgeOk f.ChildTable f.ChildColumn f.ParentTable then true
                else (dropEdge f.ChildTable f.ChildColumn f.ParentTable; false))
        let orphans =
            pack.Orphans
            |> List.filter (fun o ->
                if edgeOk o.ChildTable o.ChildColumn o.ParentTable then true
                else (dropEdge o.ChildTable o.ChildColumn o.ParentTable; false))
        let selectivities =
            pack.Selectivities
            |> List.filter (fun s ->
                if edgeOk s.ChildTable s.ChildColumn s.ParentTable then true
                else (dropEdge s.ChildTable s.ChildColumn s.ParentTable; false))
        let joints =
            pack.Joints
            |> List.filter (fun j ->
                let missing = j.Columns |> List.filter (fun c -> not (columnOk j.Table c))
                if tableOk j.Table && List.isEmpty missing then true
                else
                    (if not (tableOk j.Table) then dropTable j.Table
                     else missing |> List.iter (dropColumn j.Table))
                    false)
        { pack with
            Tables = tables; FanOuts = fanOuts
            Orphans = orphans; Selectivities = selectivities; Joints = joints },
        List.ofSeq drift

    // ------------------------------------------------------------------
    // Per-axis combiners.
    // ------------------------------------------------------------------

    let private tierMismatch (tiers: string list) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.crossover.tierMismatch"
            "The crossover merges packs of one tier. Derive every input to the same tier first."
            (Map.ofList [ "tiers", Some (String.concat ", " tiers) ])  // LINT-ALLOW: terminal comma-joined tier list in the refusal's metadata; operator-facing free text

    let private noInputs : ValidationError =
        ValidationError.create
            "twin.evidence.crossover.noInputs"
            "The crossover needs at least one input pack."

    let private tierText (t: EvidenceTier) = match t with ShapeTier -> "shape" | RichTier -> "rich"

    /// Pick the (NullCount, RowCount) pair with the worst rate, compare
    /// by exact cross-multiplication in decimal, then scale that rate to
    /// the merged row count with an integer ceiling — the merged column
    /// carries both the worst rate and the worst volume.
    let private mergeNullRate
        (candidates: (string * int64 * int64) list)
        : (string * int64 * int64) option =
        let rated = candidates |> List.filter (fun (_, _, rc) -> rc > 0L)
        match rated with
        | [] -> None
        | _ ->
            let worst =
                rated
                |> List.maxBy (fun (label, nc, rc) ->
                    decimal nc / decimal rc, rc, label)
            let _, ncW, rcW = worst
            let rcMax = rated |> List.map (fun (_, _, rc) -> rc) |> List.max
            let scaled =
                if rcW = rcMax then ncW
                else
                    let exact = decimal ncW * decimal rcMax / decimal rcW
                    min rcMax (int64 (ceil exact))
            let label, _, _ = worst
            Some (label, scaled, rcMax)

    let private weightedShape (shapes: (int64 * NumericShape) list) : NumericShape =
        let w (f: NumericShape -> decimal) =
            (shapes |> List.sumBy (fun (rows, s) -> decimal (max rows 1L) * f s))
            / decimal (max (shapes |> List.sumBy (fun (rows, _) -> max rows 1L)) 1L)
        { Min = shapes |> List.map (fun (_, s) -> s.Min) |> List.min
          P25 = w (fun s -> s.P25)
          P50 = w (fun s -> s.P50)
          P75 = w (fun s -> s.P75)
          P95 = w (fun s -> s.P95)
          P99 = w (fun s -> s.P99)
          Max = shapes |> List.map (fun (_, s) -> s.Max) |> List.max }

    // ------------------------------------------------------------------
    // The merge.
    // ------------------------------------------------------------------

    let merge (packs: EvidencePack list) : Result<EvidencePack * CrossoverReport> =
        match packs with
        | [] -> Result.failureOf noInputs
        | _ ->
        let tiers = packs |> List.map (fun p -> p.Tier) |> List.distinct
        match tiers with
        | [ tier ] ->
            let labeled = packs |> List.map (fun p -> labelOf p, p)
            let inputs = labeled |> List.map (fun (label, p) -> label, contentHash p)
            let statistics = System.Collections.Generic.List<CrossoverStatistic>()
            let stat table column edge statistic winner perSource =
                // A statistic earns a report entry only when more than one
                // source spoke — the report records decisions, never echoes.
                if List.length perSource > 1 then
                    statistics.Add
                        { Table = table; Column = column; Edge = edge
                          Statistic = statistic; Winner = winner; PerSource = perSource }

            // -------------------- tables and columns --------------------
            let tableGroups =
                labeled
                |> List.collect (fun (label, p) -> p.Tables |> List.map (fun t -> label, t))
                |> List.groupBy (fun (_, t) -> lower t.Table)
            let tables =
                tableGroups
                |> List.map (fun (_, entries) ->
                    let displayTable = (snd (List.head entries)).Table
                    let tableRows =
                        entries |> List.map (fun (label, t) -> label, t.RowCount)
                    let rowWinner = tableRows |> List.maxBy (fun (label, rc) -> rc, label)
                    stat displayTable None None "rowCount" (fst rowWinner)
                        (tableRows |> List.map (fun (l, rc) -> l, string rc))
                    let columnGroups =
                        entries
                        |> List.collect (fun (label, t) ->
                            t.Columns |> List.map (fun c -> label, t.RowCount, c))
                        |> List.groupBy (fun (_, _, c) -> lower c.Column)
                    let columns =
                        columnGroups
                        |> List.map (fun (_, colEntries) ->
                            let (_, _, first) = List.head colEntries
                            let display = first.Column
                            let rendered (nc: int64) (rc: int64) = System.String.Concat(string nc, "/", string rc)  // LINT-ALLOW: terminal report-detail rendering (nulls/rows); the literal-free counts text IS the report artifact
                            let nullMerged =
                                mergeNullRate
                                    (colEntries |> List.map (fun (label, _, c) -> label, c.NullCount, c.RowCount))
                            (match nullMerged with
                             | Some (winner, _, _) ->
                                 stat displayTable (Some display) None "nullRate" winner
                                     (colEntries |> List.map (fun (l, _, c) -> l, rendered c.NullCount c.RowCount))
                             | None -> ())
                            let rowCount =
                                colEntries |> List.map (fun (_, _, c) -> c.RowCount) |> List.max
                            let nullCount, rowCount =
                                match nullMerged with
                                | Some (_, nc, rc) -> nc, max rc rowCount
                                | None -> 0L, rowCount
                            let maxLength =
                                match colEntries |> List.choose (fun (_, _, c) -> c.MaxLength) with
                                | [] -> None
                                | lens -> Some (List.max lens)
                            (match colEntries |> List.choose (fun (l, _, c) -> c.MaxLength |> Option.map (fun v -> l, v)) with
                             | [] -> ()
                             | lens ->
                                 let winner = lens |> List.maxBy (fun (l, v) -> v, l)
                                 stat displayTable (Some display) None "maxLength" (fst winner)
                                     (lens |> List.map (fun (l, v) -> l, string v)))
                            let distinct =
                                match colEntries |> List.choose (fun (_, _, c) -> c.DistinctCount) with
                                | [] -> None
                                | ds -> Some (List.max ds)
                            let hasDuplicates =
                                colEntries |> List.exists (fun (_, _, c) -> c.HasDuplicates)
                            (if hasDuplicates then
                                let claimants =
                                    colEntries
                                    |> List.filter (fun (_, _, c) -> c.HasDuplicates)
                                    |> List.map (fun (l, _, _) -> l, "duplicates")
                                stat displayTable (Some display) None "hasDuplicates"
                                    (claimants |> List.map fst |> List.sort |> String.concat "+")  // LINT-ALLOW: the attribution label is the sorted claimant names joined; a label IS a string primitive
                                    (colEntries |> List.map (fun (l, _, c) ->
                                        l, (if c.HasDuplicates then "duplicates" else "distinct"))))
                            let truncated = colEntries |> List.exists (fun (_, _, c) -> c.Truncated)
                            let frequencies =
                                let union =
                                    colEntries
                                    |> List.collect (fun (_, _, c) -> c.Frequencies)
                                    |> List.groupBy fst
                                    |> List.map (fun (v, xs) -> v, xs |> List.sumBy snd)
                                union |> List.sortBy (fun (v, n) -> -n, v)
                            (if colEntries |> List.exists (fun (_, _, c) -> not (List.isEmpty c.Frequencies)) then
                                stat displayTable (Some display) None "vocabulary" "(union)"
                                    (colEntries |> List.map (fun (l, _, c) ->
                                        l, string (List.length c.Frequencies))))
                            let distinct =
                                match frequencies, truncated with
                                | [], _ -> distinct
                                | fs, false -> Some (max (defaultArg distinct 0L) (int64 (List.length fs)))
                                | fs, true -> Some (max (defaultArg distinct 0L) (int64 (List.length fs)))
                            let numeric =
                                match colEntries |> List.choose (fun (_, rows, c) -> c.Numeric |> Option.map (fun s -> rows, s)) with
                                | [] -> None
                                | shapes ->
                                    (match colEntries |> List.choose (fun (l, _, c) -> c.Numeric |> Option.map (fun s -> l, s)) with
                                     | [] | [ _ ] -> ()
                                     | ls ->
                                         let minWinner = ls |> List.minBy (fun (l, s) -> s.Min, l)
                                         let maxWinner = ls |> List.maxBy (fun (l, s) -> s.Max, l)
                                         stat displayTable (Some display) None "envelopeMin" (fst minWinner)
                                             (ls |> List.map (fun (l, s) -> l, string s.Min))
                                         stat displayTable (Some display) None "envelopeMax" (fst maxWinner)
                                             (ls |> List.map (fun (l, s) -> l, string s.Max)))
                                    Some (weightedShape shapes)
                            // The string-plane counts: empty and trailing
                            // follow the null-rate policy (the worst RATE's
                            // pair rescaled to the merged row count — an
                            // extreme survives, never an average); collision
                            // counts and length quantiles take the MAX.
                            let text =
                                match colEntries |> List.choose (fun (l, _, c) -> c.Text |> Option.map (fun ts -> l, c.RowCount, ts)) with
                                | [] -> None
                                | textEntries ->
                                    // The worst RATE among the sources that carry
                                    // the axis, rescaled to the MERGED row count
                                    // (a pre-F1 source without the axis must not
                                    // shrink the rescale target).
                                    let ratePolicy (statistic: string) (pick: TextShape -> int64) : int64 =
                                        let rated = textEntries |> List.filter (fun (_, colRows, _) -> colRows > 0L)
                                        match rated with
                                        | [] -> 0L
                                        | _ ->
                                            let worst =
                                                rated
                                                |> List.maxBy (fun (l, colRows, ts) ->
                                                    decimal (pick ts) / decimal colRows, colRows, l)
                                            let winner, worstRows, worstShape = worst
                                            let count = pick worstShape
                                            let scaled =
                                                if worstRows = rowCount || count = 0L then count
                                                else min rowCount (int64 (ceil (decimal count * decimal rowCount / decimal worstRows)))
                                            (if textEntries |> List.exists (fun (_, _, ts) -> pick ts > 0L) then
                                                stat displayTable (Some display) None statistic winner
                                                    (textEntries |> List.map (fun (l, _, ts) -> l, string (pick ts))))
                                            scaled
                                    let maxPolicy (pick: TextShape -> int64) : int64 =
                                        textEntries |> List.map (fun (_, _, ts) -> pick ts) |> List.max
                                    let quantile (pick: TextShape -> int option) : int option =
                                        match textEntries |> List.choose (fun (_, _, ts) -> pick ts) with
                                        | [] -> None
                                        | vs -> Some (List.max vs)
                                    let collisions = maxPolicy (fun ts -> ts.CaseCollisions)
                                    (if collisions > 0L then
                                        let winner =
                                            textEntries |> List.maxBy (fun (l, _, ts) -> ts.CaseCollisions, l)
                                        stat displayTable (Some display) None "caseCollisions" (let (l, _, _) = winner in l)
                                            (textEntries |> List.map (fun (l, _, ts) -> l, string ts.CaseCollisions)))
                                    Some
                                        { EmptyCount = ratePolicy "emptyRate" (fun ts -> ts.EmptyCount)
                                          TrailingSpaceCount = ratePolicy "trailingSpaceRate" (fun ts -> ts.TrailingSpaceCount)
                                          CaseCollisions = collisions
                                          LengthP50 = quantile (fun ts -> ts.LengthP50)
                                          LengthP90 = quantile (fun ts -> ts.LengthP90) }
                            { Column = display
                              RowCount = rowCount
                              NullCount = nullCount
                              MaxLength = maxLength
                              DistinctCount = distinct
                              Truncated = truncated
                              HasDuplicates = hasDuplicates
                              Frequencies = frequencies
                              Numeric = numeric
                              Text = text })
                        |> List.sortBy (fun c -> lower c.Column)
                    let tableRowCount =
                        max
                            (tableRows |> List.map snd |> List.max)
                            (columns |> List.map (fun c -> c.RowCount) |> function [] -> 0L | xs -> List.max xs)
                    { Table = displayTable; RowCount = tableRowCount; Columns = columns })
                |> List.sortBy (fun t -> lower t.Table)

            let rowsFor =
                labeled
                |> List.collect (fun (label, p) ->
                    p.Tables |> List.map (fun t -> (label, lower t.Table), t.RowCount))
                |> Map.ofList
            let childRows label table =
                Map.tryFind (label, lower table) rowsFor |> Option.defaultValue 1L

            // -------------------- edges --------------------
            let edgeKey (child: string) (col: string) (parent: string) =
                lower child, lower col, lower parent
            let fanOuts =
                labeled
                |> List.collect (fun (label, p) -> p.FanOuts |> List.map (fun f -> label, f))
                |> List.groupBy (fun (_, f) -> edgeKey f.ChildTable f.ChildColumn f.ParentTable)
                |> List.map (fun (_, entries) ->
                    let (_, first) = List.head entries
                    let shape =
                        weightedShape
                            (entries |> List.map (fun (label, f) -> childRows label f.ChildTable, f.Shape))
                    (if List.length entries > 1 then
                        let maxWinner = entries |> List.maxBy (fun (l, f) -> f.Shape.Max, l)
                        stat first.ChildTable None
                            (Some (first.ChildTable, first.ChildColumn, first.ParentTable))
                            "fanOutMax" (fst maxWinner)
                            (entries |> List.map (fun (l, f) -> l, string f.Shape.Max)))
                    { first with Shape = shape })
                |> List.sortBy (fun f -> lower f.ChildTable, lower f.ChildColumn)
            let orphans =
                labeled
                |> List.collect (fun (label, p) -> p.Orphans |> List.map (fun o -> label, o))
                |> List.groupBy (fun (_, o) -> edgeKey o.ChildTable o.ChildColumn o.ParentTable)
                |> List.map (fun (_, entries) ->
                    let (_, first) = List.head entries
                    let winner = entries |> List.maxBy (fun (l, o) -> o.OrphanCount, l)
                    stat first.ChildTable None
                        (Some (first.ChildTable, first.ChildColumn, first.ParentTable))
                        "orphanCount" (fst winner)
                        (entries |> List.map (fun (l, o) -> l, string o.OrphanCount))
                    { first with OrphanCount = (snd winner).OrphanCount })
                |> List.sortBy (fun o -> lower o.ChildTable, lower o.ChildColumn)
            let selectivities =
                labeled
                |> List.collect (fun (label, p) -> p.Selectivities |> List.map (fun s -> label, s))
                |> List.groupBy (fun (_, s) -> edgeKey s.ChildTable s.ChildColumn s.ParentTable)
                |> List.map (fun (_, entries) ->
                    let winner =
                        entries
                        |> List.maxBy (fun (l, s) -> s.DistinctCount, List.length s.Counts, l)
                    let (_, first) = List.head entries
                    stat first.ChildTable None
                        (Some (first.ChildTable, first.ChildColumn, first.ParentTable))
                        "selectivity" (fst winner)
                        (entries |> List.map (fun (l, s) -> l, string s.DistinctCount))
                    snd winner)
                |> List.sortBy (fun s -> lower s.ChildTable, lower s.ChildColumn)
            let joints =
                labeled
                |> List.collect (fun (label, p) -> p.Joints |> List.map (fun j -> label, j))
                |> List.groupBy (fun (_, j) -> lower j.Table, j.Columns |> List.map lower)
                |> List.map (fun (_, entries) ->
                    let winner = entries |> List.maxBy (fun (l, j) -> j.DistinctCount, l)
                    let (_, first) = List.head entries
                    stat first.Table (Some (String.concat "|" first.Columns)) None  // LINT-ALLOW: the joint's column-list report key; the joined text IS the report's coordinate text
                        "joint" (fst winner)
                        (entries |> List.map (fun (l, j) -> l, string j.DistinctCount))
                    snd winner)
                |> List.sortBy (fun j -> lower j.Table, String.concat "|" j.Columns)  // LINT-ALLOW: deterministic composite sort key over the joint's column list; the joined text is the ordering key, never emitted

            let merged =
                { Tier = tier
                  Sources = labeled |> List.collect (fun (_, p) -> p.Sources) |> List.distinct |> List.sort
                  Tables = tables
                  FanOuts = fanOuts
                  Orphans = orphans
                  Selectivities = selectivities
                  Joints = joints }
            let report =
                { Inputs = inputs
                  Tier = tier
                  Statistics =
                      statistics
                      |> List.ofSeq
                      |> List.sortBy (fun s -> lower s.Table, s.Column |> Option.map lower, s.Statistic)
                  Drift = []
                  WitnessSkips = [] }
            Result.success (merged, report)
        | several ->
            Result.failureOf (tierMismatch (several |> List.map tierText |> List.sort))

    // ------------------------------------------------------------------
    // The report codec — deterministic, no timestamps.
    // ------------------------------------------------------------------

    let private driftKindText (k: CrossoverDriftKind) : string =
        match k with
        | TableNotInTrunk -> "tableNotInTrunk"
        | ColumnNotInTrunk -> "columnNotInTrunk"
        | EdgeNotInTrunk -> "edgeNotInTrunk"

    let serializeReport (report: CrossoverReport) : string =
        let options = JsonWriterOptions(Indented = true)
        use stream = new System.IO.MemoryStream()
        (fun () ->
            use writer = new Utf8JsonWriter(stream, options)
            writer.WriteStartObject()
            writer.WriteString("tier", tierText report.Tier)
            writer.WriteStartArray "inputs"
            for (source, hash) in report.Inputs |> List.sortBy fst do
                writer.WriteStartObject()
                writer.WriteString("source", source)
                writer.WriteString("contentHash", hash)
                writer.WriteEndObject()
            writer.WriteEndArray()
            writer.WriteStartArray "statistics"
            for s in report.Statistics do
                writer.WriteStartObject()
                writer.WriteString("table", s.Table)
                match s.Column with
                | Some c -> writer.WriteString("column", c)
                | None -> ()
                match s.Edge with
                | Some (child, col, parent) ->
                    writer.WriteStartObject "edge"
                    writer.WriteString("child", child)
                    writer.WriteString("column", col)
                    writer.WriteString("parent", parent)
                    writer.WriteEndObject()
                | None -> ()
                writer.WriteString("statistic", s.Statistic)
                writer.WriteString("winner", s.Winner)
                writer.WriteStartArray "perSource"
                for (source, rendering) in s.PerSource |> List.sortBy fst do
                    writer.WriteStartObject()
                    writer.WriteString("source", source)
                    writer.WriteString("value", rendering)
                    writer.WriteEndObject()
                writer.WriteEndArray()
                writer.WriteEndObject()
            writer.WriteEndArray()
            writer.WriteStartArray "drift"
            for d in report.Drift |> List.sortBy (fun d -> driftKindText d.Kind, lower d.Coordinate) do
                writer.WriteStartObject()
                writer.WriteString("coordinate", d.Coordinate)
                writer.WriteString("kind", driftKindText d.Kind)
                writer.WriteStartArray "sources"
                for s in d.Sources |> List.sort do writer.WriteStringValue s
                writer.WriteEndArray()
                writer.WriteEndObject()
            writer.WriteEndArray()
            writer.WriteStartArray "witnessSkips"
            for (coordinate, reason) in report.WitnessSkips |> List.sortBy (fun (c, r) -> lower c, r) do
                writer.WriteStartObject()
                writer.WriteString("coordinate", coordinate)
                writer.WriteString("reason", reason)
                writer.WriteEndObject()
            writer.WriteEndArray()
            writer.WriteEndObject()) ()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())
