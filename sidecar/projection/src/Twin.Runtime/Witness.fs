namespace Twin.Runtime
// LINT-ALLOW-FILE: the witness emitter — its output IS terminal T-SQL text
//   (the deterministic UPDATE witnesses and their assert probes). Every
//   identifier passes through the SSDT renderer's quoting (`Render.quote` /
//   `tableQualified`) and every count is a formatted int64. The typed
//   `Statement` DU does not model UPDATE (the StaticSeedsEmitter precedent),
//   so `ScriptDomGenerate.toText` does not apply; a witness-SQL AST was
//   considered and rejected as a second SQL surface with one consumer.

open Projection.Core
open Twin.Core

/// THE TWIN — the witness pass (Twin.Runtime, pure).
///
/// A mint reproduces shapes, and deliberately not individual defects: it
/// produces zero foreign-key orphans by construction, it draws NULLs
/// per-row at the observed rate (so a realized count can land under the
/// recorded one), and it does not reach the observed maximum lengths or
/// numeric range edges. The witness pass closes that gap. From the
/// merged pack and the trunk catalog it plans deterministic **UPDATEs of
/// minted rows** — never INSERTs, so no NOT-NULL / identity /
/// row-composition problem — that plant each recorded reality:
///
///   - the null-rate floor: surplus non-null rows set to NULL until at
///     most `RowCount - NullCount` remain, so the minted null count is
///     never under the recorded one whatever the mint's per-row draws
///     realized (a mint that over-nulled is left alone — the audit's
///     verdicts are one-directional);
///   - one synthetic value at the observed maximum length per text column;
///   - the numeric envelope's exact edges on two rows;
///   - the recorded orphans, as child FK values past MAX(parent key);
///   - the recorded duplicates, as a copy of a synthetic value.
///
/// Two disciplines keep co-resident realities from destroying each
/// other. Every value witness ranks only NON-NULL rows (`WHERE column IS
/// NOT NULL`), so planting a value never converts a NULL and the
/// null-rate reality survives the pass; the null-rate witness runs first
/// per column, so the value witnesses rank the final null landscape.
/// And witnesses on one column claim disjoint row windows: the length
/// witness holds row 1, the envelope holds rows 1–2, the duplicate
/// copies row 1 onto the first unclaimed row, and the orphans take the
/// rows after every claim — so an orphan plant never overwrites an
/// envelope edge. The non-null budget (`RowCount - NullCount`) bounds
/// the windows; a witness the budget cannot host is a named skip
/// (`insufficientNonNullRows`), and an orphan plant clamps to the rows
/// available rather than vanishing.
///
/// Legality is designed in: a witness that would violate a constraint
/// the TRUNK enforces (an enforced reference, a unique index, a primary
/// key, a declared length, a NOT NULL column) is a named skip in the
/// plan, never an error — the environment's reality then belongs to the
/// drift/promotion story, not to the template. Every emission is seeded
/// and pure: the same pack and seed produce byte-identical SQL. The pair
/// binds to the evidence-driven mint: a scenario that overrides row
/// counts changes the landscape the windows were budgeted against.
type WitnessCase =
    | NullRateWitness of table: TableCoordinate * column: string * orderBy: string * nullCount: int64 * keepNonNull: int64
    | MaxLengthWitness of table: TableCoordinate * column: string * orderBy: string * length: int
    | EnvelopeEdgeWitness of table: TableCoordinate * column: string * orderBy: string * low: decimal * high: decimal * integral: bool
    | OrphanWitness of child: TableCoordinate * column: string * orderBy: string * parent: TableCoordinate * parentKey: string * count: int64 * offset: int64
    | DuplicateWitness of table: TableCoordinate * column: string * orderBy: string * target: int64
    /// The tail of the non-null space becomes `''` — exact count, above
    /// every bottom-claimed row (the empty-string floor, F1).
    | EmptyStringWitness of table: TableCoordinate * column: string * orderBy: string * count: int64 * floorAbove: int64
    /// One row's value reshaped to carry a trailing space, length-safe
    /// against the declared width (the pad-fold trap made real).
    | TrailingSpaceWitness of table: TableCoordinate * column: string * orderBy: string * target: int64 * declaredLength: int option
    /// Two rows minted as a synthetic case-collision pair (equal under
    /// UPPER, different raw) — what a CI-collation unique add refuses.
    | CaseCollisionWitness of table: TableCoordinate * column: string * orderBy: string * first: int64 * second: int64 * tokenLength: int

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
        // Rows already claimed among a column's non-null rows, keyed by
        // (table, column) lower-cased — the window ledger the orphan pass
        // reads after the per-column pass wrote it.
        let claimed = System.Collections.Generic.Dictionary<string * string, int64>()
        let claimKey (t: string) (c: string) = t.ToLowerInvariant(), c.ToLowerInvariant()
        let bindTable (t: string) : (TableCoordinate * Kind) option =
            match TableCoordinate.parse t with
            | Error _ -> skips.Add(skip t "notInTrunk"); None
            | Ok coord ->
                match CatalogIndex.bindKind index coord with
                | Error _ -> skips.Add(skip t "notInTrunk"); None
                | Ok kind -> Some (coord, kind)
        let columnCoordinate (t: string) (c: string) : string =
            System.String.Concat(t, ".", c)
        // The non-null budget a column's evidence promises the minted
        // table (the mint plants the recorded rate at the recorded
        // volume; the null-rate witness makes the floor exact).
        let nonNullBudget (t: string) (c: string) : int64 option =
            pack.Tables
            |> List.tryFind (fun te -> System.String.Equals(te.Table, t, System.StringComparison.OrdinalIgnoreCase))
            |> Option.bind (fun te ->
                te.Columns
                |> List.tryFind (fun ce -> System.String.Equals(ce.Column, c, System.StringComparison.OrdinalIgnoreCase)))
            |> Option.map (fun ce -> max 0L (ce.RowCount - ce.NullCount))

        for t in pack.Tables do
            match bindTable t.Table with
            | None -> ()
            | Some (coord, kind) ->
                match primaryKeyColumn kind with
                | None ->
                    if t.Columns |> List.exists (fun c -> c.NullCount > 0L || c.MaxLength.IsSome || c.Numeric.IsSome || c.HasDuplicates) then
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
                            if c.NullCount > 0L || c.MaxLength.IsSome || c.Numeric.IsSome || c.HasDuplicates then
                                skips.Add(skip (columnCoordinate t.Table c.Column) "notInTrunk")
                        | Some attr ->
                            let coordinate = columnCoordinate t.Table c.Column
                            let nonNull = max 0L (c.RowCount - c.NullCount)
                            let mutable claimedRows = 0L
                            // The null-rate floor, planned FIRST so every
                            // later witness ranks the final landscape.
                            if c.NullCount > 0L then
                                if attr.IsPrimaryKey then skips.Add(skip coordinate "primaryKeyColumn")
                                elif not attr.Column.IsNullable then skips.Add(skip coordinate "notNullable")
                                elif t.RowCount < 1L then skips.Add(skip coordinate "tooFewRows")
                                else cases.Add(NullRateWitness(coord, c.Column, orderBy, c.NullCount, nonNull))
                            // Max length: one synthetic value at the observed length.
                            match c.MaxLength with
                            | Some length when length >= 1 && attr.Type = PrimitiveType.Text ->
                                if attr.IsPrimaryKey then skips.Add(skip coordinate "primaryKeyColumn")
                                elif isEnforcedFkSource kind attr.SsKey then skips.Add(skip coordinate "enforcedReference")
                                elif (match attr.Length with Some declared -> length > declared | None -> false) then
                                    skips.Add(skip coordinate "exceedsDeclaredLength")
                                elif t.RowCount < 1L then skips.Add(skip coordinate "tooFewRows")
                                elif nonNull < 1L then skips.Add(skip coordinate "insufficientNonNullRows")
                                else
                                    cases.Add(MaxLengthWitness(coord, c.Column, orderBy, length))
                                    claimedRows <- max claimedRows 1L
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
                                elif nonNull < 2L then skips.Add(skip coordinate "insufficientNonNullRows")
                                else
                                    cases.Add(
                                        EnvelopeEdgeWitness(
                                            coord, c.Column, orderBy, shape.Min, shape.Max,
                                            attr.Type = PrimitiveType.Integer))
                                    claimedRows <- max claimedRows 2L
                            | Some _ -> skips.Add(skip coordinate "typeUnsupported")
                            | None -> ()
                            // Duplicates: copy the first row's value onto the
                            // first unclaimed row (never under row 2).
                            if c.HasDuplicates then
                                if attr.IsPrimaryKey then skips.Add(skip coordinate "primaryKeyColumn")
                                elif attr.IsIdentity then skips.Add(skip coordinate "identityColumn")
                                elif hasSingleColumnUnique kind attr.SsKey then skips.Add(skip coordinate "uniqueIndexed")
                                elif t.RowCount < 2L then skips.Add(skip coordinate "tooFewRows")
                                else
                                    let target = max 2L (claimedRows + 1L)
                                    if nonNull < target then skips.Add(skip coordinate "insufficientNonNullRows")
                                    else
                                        cases.Add(DuplicateWitness(coord, c.Column, orderBy, target))
                                        claimedRows <- max claimedRows target
                            // The string-plane realities (F1) — planted only
                            // on text columns, with the same legality
                            // discipline; the empty floor claims the TAIL of
                            // the non-null space, above every bottom claim.
                            match c.Text with
                            | Some ts when attr.Type = PrimitiveType.Text ->
                                if ts.TrailingSpaceCount > 0L then
                                    if attr.IsPrimaryKey then skips.Add(skip coordinate "primaryKeyColumn")
                                    elif isEnforcedFkSource kind attr.SsKey then skips.Add(skip coordinate "enforcedReference")
                                    elif hasSingleColumnUnique kind attr.SsKey then skips.Add(skip coordinate "uniqueIndexed")
                                    else
                                        let target = claimedRows + 1L
                                        if nonNull < target then skips.Add(skip coordinate "insufficientNonNullRows")
                                        else
                                            cases.Add(TrailingSpaceWitness(coord, c.Column, orderBy, target, attr.Length))
                                            claimedRows <- target
                                if ts.CaseCollisions > 0L then
                                    if attr.IsPrimaryKey then skips.Add(skip coordinate "primaryKeyColumn")
                                    elif isEnforcedFkSource kind attr.SsKey then skips.Add(skip coordinate "enforcedReference")
                                    elif hasSingleColumnUnique kind attr.SsKey then skips.Add(skip coordinate "uniqueIndexed")
                                    elif attr.Length.IsNone then skips.Add(skip coordinate "typeUnsupported")
                                    else
                                        let second = claimedRows + 2L
                                        if nonNull < second then skips.Add(skip coordinate "insufficientNonNullRows")
                                        else
                                            // The pair's token stays under BOTH the
                                            // declared width and the observed max —
                                            // the max-length witness's claim must
                                            // survive the collision plant.
                                            let byDeclared = max 0 (attr.Length.Value - 1)
                                            let byObserved =
                                                match c.MaxLength with
                                                | Some m -> max 0 (m - 1)
                                                | None -> 7
                                            let tokenLength = min 7 (min byDeclared byObserved)
                                            cases.Add(CaseCollisionWitness(coord, c.Column, orderBy, claimedRows + 1L, second, tokenLength))
                                            claimedRows <- second
                                if ts.EmptyCount > 0L then
                                    if attr.IsPrimaryKey then skips.Add(skip coordinate "primaryKeyColumn")
                                    elif isEnforcedFkSource kind attr.SsKey then skips.Add(skip coordinate "enforcedReference")
                                    elif hasSingleColumnUnique kind attr.SsKey then skips.Add(skip coordinate "uniqueIndexed")
                                    else
                                        let planted = min ts.EmptyCount (nonNull - claimedRows)
                                        if planted < 1L then skips.Add(skip coordinate "insufficientNonNullRows")
                                        else cases.Add(EmptyStringWitness(coord, c.Column, orderBy, planted, nonNull - planted))
                            | Some _ | None -> ()
                            claimed.[claimKey t.Table c.Column] <- claimedRows

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
                            let offset =
                                match claimed.TryGetValue(claimKey o.ChildTable o.ChildColumn) with
                                | true, n -> n
                                | false, _ -> 0L
                            let recorded = max 1L o.OrphanCount
                            // Clamp to the rows the budget can host; an
                            // edge with no room at all is a named skip.
                            let planted =
                                match nonNullBudget o.ChildTable o.ChildColumn with
                                | Some budget -> min recorded (budget - offset)
                                | None -> recorded
                            if planted < 1L then skips.Add(skip coordinate "insufficientNonNullRows")
                            else
                                cases.Add(
                                    OrphanWitness(
                                        childCoord, o.ChildColumn, childOrder, parentCoord,
                                        ColumnRealization.columnNameText pk.Column, planted, offset))
                                claimed.[claimKey o.ChildTable o.ChildColumn] <- offset + planted
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

    let private n (v: int64) : string =
        v.ToString(System.Globalization.CultureInfo.InvariantCulture)

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
        | NullRateWitness (t, c, _, nulls, _) ->
            System.String.Concat("nullRate ", TableCoordinate.text t, ".", c, " >= ", n nulls, " NULL")
        | MaxLengthWitness (t, c, _, len) ->
            System.String.Concat("maxLength ", TableCoordinate.text t, ".", c, " = ", string len)
        | EnvelopeEdgeWitness (t, c, _, lo, hi, integral) ->
            System.String.Concat("envelope ", TableCoordinate.text t, ".", c, " = [", numberLiteral integral lo, ", ", numberLiteral integral hi, "]")
        | OrphanWitness (child, c, _, parent, _, count, _) ->
            System.String.Concat("orphans ", TableCoordinate.text child, ".", c, " -> ", TableCoordinate.text parent, " x", n count)
        | DuplicateWitness (t, c, _, _) ->
            System.String.Concat("duplicate ", TableCoordinate.text t, ".", c)
        | EmptyStringWitness (t, c, _, count, _) ->
            System.String.Concat("emptyString ", TableCoordinate.text t, ".", c, " x", n count)
        | TrailingSpaceWitness (t, c, _, _, _) ->
            System.String.Concat("trailingSpace ", TableCoordinate.text t, ".", c)
        | CaseCollisionWitness (t, c, _, _, _, _) ->
            System.String.Concat("caseCollision ", TableCoordinate.text t, ".", c)

    /// Rank a column's NON-NULL rows by the primary key — the shared CTE
    /// body. The filter is the null-preservation law: a value witness
    /// updates existing values only, so the null-rate reality survives
    /// every later witness untouched.
    let private rowNumbered (table: TableCoordinate) (column: string) (orderBy: string) : string =
        System.String.Concat(
            "SELECT ", quote column, " AS v, ROW_NUMBER() OVER (ORDER BY ", quote orderBy,
            ") AS rn FROM ", qualified table, " WHERE ", quote column, " IS NOT NULL")

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
            | NullRateWitness (table, column, orderBy, _, keepNonNull) ->
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line (System.String.Concat("UPDATE w SET v = NULL WHERE rn > ", n keepNonNull, ";"))
            | MaxLengthWitness (table, column, orderBy, length) ->
                let value = token seed [ TableCoordinate.text table; column; "maxLength" ] length
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line (System.String.Concat("UPDATE w SET v = N'", value, "' WHERE rn = 1;"))
            | EnvelopeEdgeWitness (table, column, orderBy, low, high, integral) ->
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line (System.String.Concat("UPDATE w SET v = ", numberLiteral integral low, " WHERE rn = 1;"))
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line (System.String.Concat("UPDATE w SET v = ", numberLiteral integral high, " WHERE rn = 2;"))
            | OrphanWitness (child, column, orderBy, parent, parentKey, count, offset) ->
                line (System.String.Concat(";WITH w AS (", rowNumbered child column orderBy, ")"))
                line
                    (System.String.Concat(
                        "UPDATE w SET v = (SELECT ISNULL(MAX(", quote parentKey, "), 0) FROM ",
                        qualified parent, ") + rn WHERE rn > ", n offset,
                        " AND rn <= ", n (offset + count), ";"))
            | DuplicateWitness (table, column, orderBy, target) ->
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line
                    (System.String.Concat(
                        "UPDATE w SET v = (SELECT w2.v FROM (", rowNumbered table column orderBy,
                        ") w2 WHERE w2.rn = 1) WHERE rn = ", n target, ";"))
            | EmptyStringWitness (table, column, orderBy, _, floorAbove) ->
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line (System.String.Concat("UPDATE w SET v = N'' WHERE rn > ", n floorAbove, ";"))
            | TrailingSpaceWitness (table, column, orderBy, target, declared) ->
                let expr =
                    match declared with
                    | Some d when d >= 1 -> System.String.Concat("LEFT(v, ", string (d - 1), ") + N' '")
                    | _ -> "v + N' '"
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line (System.String.Concat("UPDATE w SET v = ", expr, " WHERE rn = ", n target, ";"))
            | CaseCollisionWitness (table, column, orderBy, first, second, tokenLength) ->
                let stem = token seed [ TableCoordinate.text table; column; "caseCollision" ] tokenLength
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line (System.String.Concat("UPDATE w SET v = N'", stem, "a' WHERE rn = ", n first, ";"))
                line (System.String.Concat(";WITH w AS (", rowNumbered table column orderBy, ")"))
                line (System.String.Concat("UPDATE w SET v = N'", stem, "A' WHERE rn = ", n second, ";"))
        sb.ToString()

    let private check (case: WitnessCase) : string =
        let name = (caseName case).Replace("'", "''")
        let ok =
            match case with
            | NullRateWitness (table, column, _, nulls, _) ->
                System.String.Concat(
                    "CASE WHEN (SELECT COUNT_BIG(*) FROM ", qualified table, " WHERE ",
                    quote column, " IS NULL) >= ", n nulls, " THEN 1 ELSE 0 END")
            | MaxLengthWitness (table, column, _, length) ->
                System.String.Concat(
                    "CASE WHEN (SELECT MAX(LEN(", quote column, ")) FROM ", qualified table,
                    ") = ", string length, " THEN 1 ELSE 0 END")
            | EnvelopeEdgeWitness (table, column, _, low, high, integral) ->
                System.String.Concat(
                    "CASE WHEN (SELECT MIN(", quote column, ") FROM ", qualified table, ") = ",
                    numberLiteral integral low, " AND (SELECT MAX(", quote column, ") FROM ",
                    qualified table, ") = ", numberLiteral integral high, " THEN 1 ELSE 0 END")
            | OrphanWitness (child, column, _, parent, parentKey, _, _) ->
                System.String.Concat(
                    "CASE WHEN (SELECT COUNT(*) FROM ", qualified child, " c LEFT JOIN ",
                    qualified parent, " p ON c.", quote column, " = p.", quote parentKey,
                    " WHERE c.", quote column, " IS NOT NULL AND p.", quote parentKey,
                    " IS NULL) >= 1 THEN 1 ELSE 0 END")
            | DuplicateWitness (table, column, _, _) ->
                System.String.Concat(
                    "CASE WHEN EXISTS (SELECT ", quote column, " FROM ", qualified table,
                    " WHERE ", quote column, " IS NOT NULL GROUP BY ", quote column,
                    " HAVING COUNT(*) > 1) THEN 1 ELSE 0 END")
            | EmptyStringWitness (table, column, _, count, _) ->
                System.String.Concat(
                    "CASE WHEN (SELECT COUNT_BIG(*) FROM ", qualified table, " WHERE ",
                    quote column, " IS NOT NULL AND DATALENGTH(", quote column, ") = 0) >= ",
                    n count, " THEN 1 ELSE 0 END")
            | TrailingSpaceWitness (table, column, _, _, _) ->
                System.String.Concat(
                    "CASE WHEN EXISTS (SELECT 1 FROM ", qualified table, " WHERE ",
                    quote column, " IS NOT NULL AND DATALENGTH(", quote column,
                    ") <> DATALENGTH(RTRIM(", quote column, "))) THEN 1 ELSE 0 END")
            | CaseCollisionWitness (table, column, _, _, _, _) ->
                // COUNT(DISTINCT) under a CI collation folds the pair back
                // together — the binary collation keeps the variants apart.
                System.String.Concat(
                    "CASE WHEN EXISTS (SELECT UPPER(", quote column, ") FROM ", qualified table,
                    " WHERE ", quote column, " IS NOT NULL AND DATALENGTH(", quote column,
                    ") > 0 GROUP BY UPPER(", quote column, ") HAVING COUNT(DISTINCT ",
                    quote column, " COLLATE Latin1_General_BIN2) > 1) THEN 1 ELSE 0 END")
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
