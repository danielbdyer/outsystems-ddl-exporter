namespace Twin.Core

open System.Text.Json

/// THE TWIN — the per-environment fidelity audit (Twin.Core, pure).
///
/// After a bake, the surface must DEMONSTRATE its claims against each
/// captured environment rather than assert them. The audit is a pure
/// pack-versus-pack comparison: the environment's captured pack on one
/// side, a pack profiled off the minted template on the other. Two
/// verdict classes per statistic:
///
///   **blocking** — the template must be at least as blocking as this
///     environment: table presence, null rate, maximum length, duplicate
///     reality, recorded orphans, the numeric envelope. A blocking
///     failure fails the bake.
///   **advisory** — the fidelity margins: vocabulary coverage, distinct
///     counts. An advisory miss is reported, never fatal.
///
/// A reality the witness pass lawfully declined (the trunk already
/// enforces the rule) arrives as an EXEMPT coordinate: the audit records
/// the verdict as advisory, because the template cannot carry it and the
/// promotion story owns it. Every rendering is literal-free — counts,
/// rate pairs, lengths, and side names; never a captured value.
type AuditVerdict = {
    Coordinate : string
    Statistic  : string
    Blocking   : bool
    Ok         : bool
    Detail     : string
}

type AuditSection = {
    Source     : string
    Failures   : int
    Advisories : int
    Verdicts   : AuditVerdict list
}

type AuditReport = {
    Sections : AuditSection list
}

[<RequireQualifiedAccess>]
module FidelityAudit =

    let private lower (s: string) = s.ToLowerInvariant()

    let private rendered (nulls: int64) (rows: int64) : string =
        System.String.Concat(string nulls, "/", string rows)  // LINT-ALLOW: terminal audit-detail rendering (nulls/rows); the literal-free counts text IS the report artifact

    let private rate (nulls: int64) (rows: int64) : decimal =
        if rows <= 0L then 0m else decimal nulls / decimal rows

    /// Audit one environment's pack against the minted pack.
    let audit (exempt: Set<string>) (source: EvidencePack) (minted: EvidencePack) : AuditSection =
        let label =
            match source.Sources with
            | [] -> "(unlabeled)"
            | sources -> sources |> List.sort |> String.concat "+"  // LINT-ALLOW: the section label is the sorted source names joined; a label IS a string primitive
        let verdicts = System.Collections.Generic.List<AuditVerdict>()
        let verdict coordinate statistic blocking ok detail =
            let blocking = blocking && not (Set.contains coordinate exempt)
            verdicts.Add
                { Coordinate = coordinate; Statistic = statistic
                  Blocking = blocking; Ok = ok; Detail = detail }
        let mintedTables =
            minted.Tables |> List.map (fun t -> lower t.Table, t) |> Map.ofList
        for t in source.Tables do
            match Map.tryFind (lower t.Table) mintedTables with
            | None ->
                if t.RowCount > 0L then
                    verdict t.Table "presence" true false "the template does not carry the table"
            | Some mt ->
                if t.RowCount > 0L then
                    verdict t.Table "presence" true (mt.RowCount > 0L)
                        (System.String.Concat("source ", string t.RowCount, " rows; minted ", string mt.RowCount))  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                let mintedColumns =
                    mt.Columns |> List.map (fun c -> lower c.Column, c) |> Map.ofList
                for c in t.Columns do
                    let coordinate = System.String.Concat(t.Table, ".", c.Column)  // LINT-ALLOW: terminal report-coordinate rendering (table.column); the composite key IS the report's coordinate text
                    match Map.tryFind (lower c.Column) mintedColumns with
                    | None ->
                        verdict coordinate "presence" true false "the template does not carry the column"
                    | Some mc ->
                        if c.RowCount > 0L && c.NullCount > 0L then
                            verdict coordinate "nullRate" true
                                (rate mc.NullCount mc.RowCount >= rate c.NullCount c.RowCount)
                                (System.String.Concat(  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                                    "minted ", rendered mc.NullCount mc.RowCount,
                                    "; source ", rendered c.NullCount c.RowCount))
                        match c.MaxLength with
                        | Some s ->
                            let ok = match mc.MaxLength with Some m -> m >= s | None -> false
                            verdict coordinate "maxLength" true ok
                                (System.String.Concat(  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                                    "minted ", (match mc.MaxLength with Some m -> string m | None -> "-"),
                                    "; source ", string s))
                        | None -> ()
                        if c.HasDuplicates then
                            verdict coordinate "hasDuplicates" true mc.HasDuplicates
                                (if mc.HasDuplicates then "duplicates present" else "no duplicate on the template")
                        match c.Numeric with
                        | Some s ->
                            match mc.Numeric with
                            | Some m ->
                                let minOk = m.Min <= s.Min
                                let maxOk = m.Max >= s.Max
                                verdict coordinate "envelope" true (minOk && maxOk)
                                    (match minOk, maxOk with
                                     | true, true -> "the minted envelope contains the source envelope"
                                     | false, true -> "the minted minimum sits above the source minimum"
                                     | true, false -> "the minted maximum sits below the source maximum"
                                     | false, false -> "both edges sit inside the source envelope")
                            | None ->
                                verdict coordinate "envelope" true false "the template carries no numeric evidence for the column"
                        | None -> ()
                        match c.Frequencies with
                        | [] -> ()
                        | freqs ->
                            let mintedValues =
                                mc.Frequencies |> List.map (fst >> lower) |> Set.ofList
                            let missing =
                                freqs |> List.filter (fun (v, _) -> not (Set.contains (lower v) mintedValues))
                            verdict coordinate "vocabulary" false (List.isEmpty missing)
                                (System.String.Concat(  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                                    "source ", string (List.length freqs), " values; minted ",
                                    string (List.length mc.Frequencies), "; missing ",
                                    string (List.length missing)))
                        match c.DistinctCount with
                        | Some s ->
                            let ok = match mc.DistinctCount with Some m -> m >= s | None -> false
                            verdict coordinate "distinctCount" false ok
                                (System.String.Concat(  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                                    "minted ", (match mc.DistinctCount with Some m -> string m | None -> "-"),
                                    "; source ", string s))
                        | None -> ()
                        // The string-plane realities (F1): presence is
                        // blocking wherever a witness can plant it; the
                        // counts and length quantiles are fidelity margins.
                        match c.Text with
                        | None -> ()
                        | Some ts ->
                            let mts =
                                mc.Text
                                |> Option.defaultValue
                                    { EmptyCount = 0L; TrailingSpaceCount = 0L; CaseCollisions = 0L
                                      LengthP50 = None; LengthP90 = None }
                            if ts.EmptyCount > 0L then
                                verdict coordinate "emptyString" true (mts.EmptyCount >= 1L)
                                    (System.String.Concat("minted ", string mts.EmptyCount, "; source ", string ts.EmptyCount))  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                                verdict coordinate "emptyStringCount" false (mts.EmptyCount >= ts.EmptyCount)
                                    (System.String.Concat("minted ", string mts.EmptyCount, "; source ", string ts.EmptyCount))  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                            if ts.TrailingSpaceCount > 0L then
                                verdict coordinate "trailingSpace" true (mts.TrailingSpaceCount >= 1L)
                                    (System.String.Concat("minted ", string mts.TrailingSpaceCount, "; source ", string ts.TrailingSpaceCount))  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                            if ts.CaseCollisions > 0L then
                                verdict coordinate "caseCollisions" true (mts.CaseCollisions >= 1L)
                                    (System.String.Concat("minted ", string mts.CaseCollisions, "; source ", string ts.CaseCollisions))  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                            match ts.LengthP90 with
                            | Some s90 ->
                                verdict coordinate "lengthP90" false
                                    (match mts.LengthP90 with Some m -> m >= s90 | None -> false)
                                    (System.String.Concat("minted ", (match mts.LengthP90 with Some m -> string m | None -> "-"), "; source ", string s90))  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                            | None -> ()
                            match ts.LengthP50 with
                            | Some s50 ->
                                verdict coordinate "lengthP50" false
                                    (match mts.LengthP50 with Some m -> m >= s50 | None -> false)
                                    (System.String.Concat("minted ", (match mts.LengthP50 with Some m -> string m | None -> "-"), "; source ", string s50))  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                            | None -> ()
                        // Conditional-null structure (F2): a margin — did the
                        // minted copy's own discovery find the same joint?
                        // The detail names only COLUMN names, never values.
                        match c.ConditionalNulls with
                        | Some cn ->
                            let survived =
                                match mc.ConditionalNulls with
                                | Some mcn -> System.String.Equals(mcn.Partner, cn.Partner, System.StringComparison.OrdinalIgnoreCase)
                                | None -> false
                            verdict coordinate "conditionalNulls" false survived
                                (System.String.Concat(  // LINT-ALLOW: terminal audit-detail rendering; column names and counts only — the literal-free text IS the report artifact
                                    "source by ", cn.Partner, " x", string (List.length cn.Rates), "; minted ",
                                    (match mc.ConditionalNulls with Some m -> System.String.Concat("by ", m.Partner) | None -> "-")))  // LINT-ALLOW: terminal audit-detail rendering; column names and counts only — the literal-free text IS the report artifact
                        | None -> ()
        let mintedOrphans =
            minted.Orphans
            |> List.map (fun o -> (lower o.ChildTable, lower o.ChildColumn, lower o.ParentTable), o.OrphanCount)
            |> Map.ofList
        for o in source.Orphans do
            let coordinate =
                System.String.Concat(o.ChildTable, ".", o.ChildColumn, " -> ", o.ParentTable)  // LINT-ALLOW: terminal report-coordinate rendering (edge arrow); the composite key IS the report's coordinate text
            let planted =
                Map.tryFind (lower o.ChildTable, lower o.ChildColumn, lower o.ParentTable) mintedOrphans
                |> Option.defaultValue 0L
            verdict coordinate "orphans" true (planted >= 1L)
                (System.String.Concat("minted ", string planted, "; source ", string o.OrphanCount))  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
        // The hot parent (F2): the template must carry each edge's recorded
        // MAXIMUM fan-out (a max under two is the baseline, not a claim);
        // the 95th percentile stays a margin.
        let mintedFanOuts =
            minted.FanOuts
            |> List.map (fun f -> (lower f.ChildTable, lower f.ChildColumn, lower f.ParentTable), f.Shape)
            |> Map.ofList
        for f in source.FanOuts do
            let coordinate =
                System.String.Concat(f.ChildTable, ".", f.ChildColumn, " -> ", f.ParentTable)  // LINT-ALLOW: terminal report-coordinate rendering (edge arrow); the composite key IS the report's coordinate text
            let recordedMax = System.Decimal.Ceiling f.Shape.Max
            if recordedMax >= 2m then
                match Map.tryFind (lower f.ChildTable, lower f.ChildColumn, lower f.ParentTable) mintedFanOuts with
                | None ->
                    verdict coordinate "fanOutMax" true false
                        (System.String.Concat("minted -; source ", string recordedMax))  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                | Some ms ->
                    verdict coordinate "fanOutMax" true (System.Decimal.Ceiling ms.Max >= recordedMax)
                        (System.String.Concat("minted ", string (System.Decimal.Ceiling ms.Max), "; source ", string recordedMax))  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
                    verdict coordinate "fanOutP95" false (ms.P95 >= f.Shape.P95)
                        (System.String.Concat("minted ", string ms.P95, "; source ", string f.Shape.P95))  // LINT-ALLOW: terminal audit-detail rendering; the literal-free counts text IS the report artifact
        let all = List.ofSeq verdicts |> List.sortBy (fun v -> lower v.Coordinate, v.Statistic)
        { Source = label
          Failures = all |> List.filter (fun v -> v.Blocking && not v.Ok) |> List.length
          Advisories = all |> List.filter (fun v -> not v.Blocking && not v.Ok) |> List.length
          Verdicts = all }

    let auditAll (exempt: Set<string>) (sources: EvidencePack list) (minted: EvidencePack) : AuditReport =
        { Sections =
            sources
            |> List.map (fun s -> audit exempt s minted)
            |> List.sortBy (fun s -> s.Source) }

    let failures (report: AuditReport) : int =
        report.Sections |> List.sumBy (fun s -> s.Failures)

    let serializeReport (report: AuditReport) : string =
        let options = JsonWriterOptions(Indented = true)
        use stream = new System.IO.MemoryStream()
        (fun () ->
            use writer = new Utf8JsonWriter(stream, options)
            writer.WriteStartObject()
            writer.WriteStartArray "sections"
            for s in report.Sections do
                writer.WriteStartObject()
                writer.WriteString("source", s.Source)
                writer.WriteNumber("failures", s.Failures)
                writer.WriteNumber("advisories", s.Advisories)
                writer.WriteStartArray "verdicts"
                for v in s.Verdicts do
                    writer.WriteStartObject()
                    writer.WriteString("coordinate", v.Coordinate)
                    writer.WriteString("statistic", v.Statistic)
                    writer.WriteBoolean("blocking", v.Blocking)
                    writer.WriteBoolean("ok", v.Ok)
                    writer.WriteString("detail", v.Detail)
                    writer.WriteEndObject()
                writer.WriteEndArray()
                writer.WriteEndObject()
            writer.WriteEndArray()
            writer.WriteEndObject()) ()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())
