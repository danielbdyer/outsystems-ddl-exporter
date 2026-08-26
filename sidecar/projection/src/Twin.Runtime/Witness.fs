namespace Twin.Runtime

open Projection.Core
open Twin.Core

/// THE TWIN — the witness pass (Twin.Core, pure).
///
/// A mint reproduces shapes, and deliberately not individual defects: it
/// produces zero foreign-key orphans by construction, and it does not
/// reach the observed maximum lengths or numeric range edges. The
/// witness pass closes that gap. From the merged pack and the trunk
/// catalog it plans deterministic **UPDATEs of minted rows** — never
/// INSERTs, so no NOT-NULL / identity / row-composition problem — that
/// plant each recorded reality:
///
///   - one synthetic value at the observed maximum length per text column;
///   - the numeric envelope's exact edges on two rows;
///   - the recorded orphans, as child FK values past MAX(parent key);
///   - the recorded duplicates, as a copy of a synthetic value.
///
/// Legality is designed in: a witness that would violate a constraint
/// the TRUNK enforces (an enforced reference, a unique index, a primary
/// key, a declared length) is a named skip in the plan, never an error —
/// the environment's reality then belongs to the drift/promotion story,
/// not to the template. Every emission is seeded and pure: the same pack
/// and seed produce byte-identical SQL.
type WitnessCase =
    | MaxLengthWitness of table: TableCoordinate * column: string * orderBy: string * length: int
    | EnvelopeEdgeWitness of table: TableCoordinate * column: string * orderBy: string * low: decimal * high: decimal * integral: bool
    | OrphanWitness of child: TableCoordinate * column: string * orderBy: string * parent: TableCoordinate * parentKey: string * count: int64
    | DuplicateWitness of table: TableCoordinate * column: string * orderBy: string

type WitnessSkip = {
    Coordinate : string
    Reason     : string
}

type WitnessPlan = {
    Cases   : WitnessCase list
    Sources : string list
}

[<RequireQualifiedAccess>]
module Witness =

    // ------------------------------------------------------------------
    // Planning.
    // ------------------------------------------------------------------

    let private skip (coordinate: string) (reason: string) : WitnessSkip =
        { Coordinate = coordinate; Reason = reason }

    let private primaryKeyColumn (kind: Kind) : string option =
        kind.Attributes
        |> List.tryFind (fun a -> a.IsPrimaryKey)
        |> Option.map (fun a -> ColumnRealization.columnNameText a.Column)

    let private hasSingleColumnUnique (kind: Kind) (attrKey: SsKey) : bool =
        kind.Indexes
        |> List.exists (fun i ->
            (match i.Uniqueness with
             | IndexUniqueness.Unique | IndexUniqueness.PrimaryKey -> true
             | IndexUniqueness.NotUnique -> false)
            && (match i.Columns with
                | [ only ] -> only.Attribute = attrKey
                | _ -> false))

    let private isEnforcedFkSource (kind: Kind) (attrKey: SsKey) : bool =
        kind.References |> List.exists (fun r -> r.SourceAttribute = attrKey)

    let private intMin = -2147483648m
    let private intMax = 2147483647m

    /// Plan the witnesses a merged pack asks for against the trunk
    /// catalog. Coordinates the trunk does not carry become skips (the
    /// pack should arrive clamped; the plan never throws on drift).
    let plan (index: CatalogIndex) (pack: EvidencePack) : WitnessPlan * WitnessSkip list =
        let skips = System.Collections.Generic.List<WitnessSkip>()
        let cases = System.Collections.Generic.List<WitnessCase>()
        let bindTable (t: string) : (TableCoordinate * Kind) option =
            match TableCoordinate.parse t with
            | Error _ -> skips.Add(skip t "notInTrunk"); None
            | Ok coord ->
                match CatalogIndex.bindKind index coord with
                | Error _ -> skips.Add(skip t "notInTrunk"); None
                | Ok kind -> Some (coord, kind)
        let columnCoordinate (t: string) (c: string) : string =
            System.String.Concat(t, ".", c)

        for t in pack.Tables do
            match bindTable t.Table with
            | None -> ()
            | Some (coord, kind) ->
                match primaryKeyColumn kind with
                | None ->
                    if t.Columns |> List.exists (fun c -> c.MaxLength.IsSome || c.Numeric.IsSome || c.HasDuplicates) then
                        skips.Add(skip t.Table "noPrimaryKey")
                | Some orderBy ->
                    for c in t.Columns do
                        let attr =
                            kind.Attributes
                            |> List.tryFind (fun a ->
                                System.String.Equals(
                                    ColumnRealization.columnNameText a.Column, c.Column,
                                    System.StringComparison.OrdinalIgnoreCase))
                        match attr with
                        | None ->
                            if c.MaxLength.IsSome || c.Numeric.IsSome || c.HasDuplicates then
                                skips.Add(skip (columnCoordinate t.Table c.Column) "notInTrunk")
                        | Some attr ->
                            let coordinate = columnCoordinate t.Table c.Column
                            // Max length: one synthetic value at the observed length.
                            match c.MaxLength with
                            | Some length when length >= 1 && attr.Type = PrimitiveType.Text ->
                                if attr.IsPrimaryKey then skips.Add(skip coordinate "primaryKeyColumn")
                                elif isEnforcedFkSource kind attr.SsKey then skips.Add(skip coordinate "enforcedReference")
                                elif (match attr.Length with Some declared -> length > declared | None -> false) then
                                    skips.Add(skip coordinate "exceedsDeclaredLength")
                                elif t.RowCount < 1L then skips.Add(skip coordinate "tooFewRows")
                                else cases.Add(MaxLengthWitness(coord, c.Column, orderBy, length))
                            | Some _ -> ()
                            | None -> ()
                            // Envelope edges: the exact Min and Max on two rows.
                            match c.Numeric with
                            | Some shape when attr.Type = PrimitiveType.Integer || attr.Type = PrimitiveType.Decimal ->
                                if attr.IsPrimaryKey then skips.Add(skip coordinate "primaryKeyColumn")
                                elif attr.IsIdentity then skips.Add(skip coordinate "identityColumn")
                                elif isEnforcedFkSource kind attr.SsKey then skips.Add(skip coordinate "enforcedReference")
                                elif attr.Type = PrimitiveType.Integer
                                     && (shape.Min < intMin || shape.Max > intMax) then
                                    skips.Add(skip coordinate "typeRange")
                                elif t.RowCount < 2L then skips.Add(skip coordinate "tooFewRows")
                                else
                                    cases.Add(
                                        EnvelopeEdgeWitness(
                                            coord, c.Column, orderBy, shape.Min, shape.Max,
                                            attr.Type = PrimitiveType.Integer))
                            | Some _ -> skips.Add(skip coordinate "typeUnsupported")
                            | None -> ()
                            // Duplicates: copy one synthetic value onto a second row.
                            if c.HasDuplicates then
                                if attr.IsPrimaryKey then skips.Add(skip coordinate "primaryKeyColumn")
                                elif attr.IsIdentity then skips.Add(skip coordinate "identityColumn")
                                elif hasSingleColumnUnique kind attr.SsKey then skips.Add(skip coordinate "uniqueIndexed")
                                elif t.RowCount < 2L then skips.Add(skip coordinate "tooFewRows")
                                else cases.Add(DuplicateWitness(coord, c.Column, orderBy))

        for o in pack.Orphans do
            let coordinate =
                System.String.Concat(o.ChildTable, ".", o.ChildColumn, " -> ", o.ParentTable)
            match Evidence.resolveEdge index o.ChildTable o.ChildColumn o.ParentTable with
            | Error _ -> skips.Add(skip coordinate "notInTrunk")
            | Ok (Some _) ->
                // The trunk enforces this edge, so the environment's
                // orphans cannot exist on the template. The promotion
                // story owns them.
                skips.Add(skip coordinate "enforcedReference")
            | Ok None ->
                match bindTable o.ChildTable, bindTable o.ParentTable with
                | Some (childCoord, childKind), Some (parentCoord, parentKind) ->
                    match primaryKeyColumn childKind with
                    | None -> skips.Add(skip coordinate "noPrimaryKey")
                    | Some childOrder ->
                        let parentPk =
                            parentKind.Attributes
                            |> List.tryFind (fun a -> a.IsPrimaryKey && a.Type = PrimitiveType.Integer)
                        match parentPk with
                        | None -> skips.Add(skip coordinate "parentKeyUnsupported")
                        | Some pk ->
                            cases.Add(
                                OrphanWitness(
                                    childCoord, o.ChildColumn, childOrder, parentCoord,
                                    ColumnRealization.columnNameText pk.Column, max 1L o.OrphanCount))
                | _ -> ()

        { Cases = List.ofSeq cases; Sources = pack.Sources }, List.ofSeq skips

    // ------------------------------------------------------------------
    // Emission — deterministic T-SQL, identifiers through the SSDT
    // renderer's quoting, one UPDATE per witness.
    // ------------------------------------------------------------------

    let private quote (s: string) : string = Projection.Targets.SSDT.Render.quote s

    let private qualified (c: TableCoordinate) : string =
        System.String.Concat(quote (SchemaName.value c.Schema), ".", quote (TableName.value c.Table))  // LINT-ALLOW: terminal witness SQL text; identifiers pass through the SSDT renderer's quoting

    let private invariant (d: decimal) : string =
        d.ToString(System.Globalization.CultureInfo.InvariantCulture)

    let private numberLiteral (integral: bool) (d: decimal) : string =
        if integral then (int64 (System.Decimal.Truncate d)).ToString(System.Globalization.CultureInfo.InvariantCulture)
        else invariant d

    /// A seeded synthetic token of an exact length: FNV-1a over the
    /// coordinate, hex-rendered and repeated. Never sourced from any
    /// captured value.
    let private token (seed: uint64) (parts: string list) (length: int) : string =
        let mutable h = 14695981039346656037UL ^^^ seed
        for part in parts do
            for ch in part do
                h <- (h ^^^ uint64 (uint16 ch)) * 1099511628211UL
        let hex = h.ToString "x16"
        let sb = System.Text.StringBuilder()
        while sb.Length < length do sb.Append hex |> ignore
        sb.ToString(0, length)

    let private caseName (case: WitnessCase) : string =
        match case with
        | MaxLengthWitness (t, c, _, n) ->
            System.String.Concat("maxLength ", TableCoordinate.text t, ".", c, " = ", string n)
        | EnvelopeEdgeWitness (t, c, _, lo, hi, integral) ->
            System.String.Concat("envelope ", TableCoordinate.text t, ".", c, " = [", numberLiteral integral lo, ", ", numberLiteral integral hi, "]")
        | OrphanWitness (child, c, _, parent, _, n) ->
            System.String.Concat("orphans ", TableCoordinate.text child, ".", c, " -> ", TableCoordinate.text parent, " x", string n)
        | DuplicateWitness (t, c, _) ->
            System.String.Concat("duplicate ", TableCoordinate.text t, ".", c)

    let private rowNumbered (table: TableCoordinate) (column: string) (orderBy: string) : string =
        System.String.Concat(
            "SELECT ", quote column, " AS v, ROW_NUMBER() OVER (ORDER BY ", quote orderBy,
            ") AS rn FROM ", qualified table)

    /// The witness SQL: one deterministic UPDATE block per planned case,
    /// in plan order. Execute against the minted template, after the
    /// mint, before the backup.
    let emitSql (seed: uint64) (plan: WitnessPlan) : string =
        let sb = System.Text.StringBuilder()
        let line (s: string) = sb.AppendLine s |> ignore
        line "-- The witness pass: recorded realities planted as deterministic"
        line "-- UPDATEs of minted rows. Generated from the merged evidence pack;"
        line "-- every value is synthetic or a recorded boundary. Re-running after"
        line "-- a fresh mint plants the identical state."
        for case in plan.Cases do
            line ""
            line (System.String.Concat("-- ", caseName case))
            match case with
            | MaxLengthWitness (table, column, orderBy, length) ->
                let value = token seed [ TableCoordinate.text table; column; "maxLength" ] length
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line (System.String.Concat("UPDATE w SET v = N'", value, "' WHERE rn = 1;"))
            | EnvelopeEdgeWitness (table, column, orderBy, low, high, integral) ->
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line (System.String.Concat("UPDATE w SET v = ", numberLiteral integral low, " WHERE rn = 1;"))
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line (System.String.Concat("UPDATE w SET v = ", numberLiteral integral high, " WHERE rn = 2;"))
            | OrphanWitness (child, column, orderBy, parent, parentKey, count) ->
                line (System.String.Concat(";WITH w AS (", rowNumbered child column orderBy, ")"))
                line
                    (System.String.Concat(
                        "UPDATE w SET v = (SELECT ISNULL(MAX(", quote parentKey, "), 0) FROM ",
                        qualified parent, ") + rn WHERE rn <= ", string count, ";"))
            | DuplicateWitness (table, column, orderBy) ->
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line
                    (System.String.Concat(
                        "UPDATE w SET v = (SELECT w2.v FROM (", rowNumbered table column orderBy,
                        ") w2 WHERE w2.rn = 1) WHERE rn = 2;"))
        sb.ToString()

    let private check (case: WitnessCase) : string =
        let name = (caseName case).Replace("'", "''")
        let ok =
            match case with
            | MaxLengthWitness (table, column, _, length) ->
                System.String.Concat(
                    "CASE WHEN (SELECT MAX(LEN(", quote column, ")) FROM ", qualified table,
                    ") = ", string length, " THEN 1 ELSE 0 END")
            | EnvelopeEdgeWitness (table, column, _, low, high, integral) ->
                System.String.Concat(
                    "CASE WHEN (SELECT MIN(", quote column, ") FROM ", qualified table, ") = ",
                    numberLiteral integral low, " AND (SELECT MAX(", quote column, ") FROM ",
                    qualified table, ") = ", numberLiteral integral high, " THEN 1 ELSE 0 END")
            | OrphanWitness (child, column, _, parent, parentKey, _) ->
                System.String.Concat(
                    "CASE WHEN (SELECT COUNT(*) FROM ", qualified child, " c LEFT JOIN ",
                    qualified parent, " p ON c.", quote column, " = p.", quote parentKey,
                    " WHERE c.", quote column, " IS NOT NULL AND p.", quote parentKey,
                    " IS NULL) >= 1 THEN 1 ELSE 0 END")
            | DuplicateWitness (table, column, _) ->
                System.String.Concat(
                    "CASE WHEN EXISTS (SELECT ", quote column, " FROM ", qualified table,
                    " GROUP BY ", quote column, " HAVING COUNT(*) > 1) THEN 1 ELSE 0 END")
        System.String.Concat("    SELECT N'", name, "' AS name, ", ok, " AS ok")

    /// The assertion script: one check per planned witness, a detail
    /// result set, and a single `failures` summary the bake gates on.
    let emitAssertSql (plan: WitnessPlan) : string =
        match plan.Cases with
        | [] ->
            "-- No witnesses were planned; nothing to assert.\nSELECT 0 AS failures;\n"
        | cases ->
            let checks = cases |> List.map check |> String.concat "\n    UNION ALL\n"
            System.String.Concat(
                "-- The witness assertions: every recorded reality landed.\n",
                ";WITH checks(name, ok) AS (\n", checks, "\n)\n",
                "SELECT name, ok FROM checks ORDER BY name;\n",
                ";WITH checks(name, ok) AS (\n", checks, "\n)\n",
                "SELECT COUNT(*) AS failures FROM checks WHERE ok = 0;\n")
