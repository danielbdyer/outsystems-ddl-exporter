namespace Projection.Core

/// A column's physical-schema coordinate — the structural-fidelity
/// axis that survives the deploy → read round-trip. Used by the
/// canary's round-trip property test (M3 onward) to compare two
/// Catalogs produced by different adapters (e.g., OSSYS JSON vs.
/// `Projection.Adapters.Sql.ReadSide`) without false negatives on
/// V2-IR-only metadata or SsKey-synthesis-source differences.
///
/// Per `DECISIONS 2026-05-23 — Source SQL Server with OutSystems
/// semantics is the canary's primary wide integration surface`,
/// `PhysicalSchema` is the comparison primitive both halves of the
/// round-trip use:
///
///   - **Source half.** OutSystems-shaped DDL → deploy → read →
///     `sourceCatalog`. Project to `PhysicalSchema` via
///     `PhysicalSchema.ofCatalog`.
///   - **Target half.** V2 emit → deploy → read → `targetCatalog`.
///     Project to `PhysicalSchema` via the same function.
///   - **Assertion.** `PhysicalSchema.diff source target` returns
///     `(missingInTarget, extraInTarget)` for both columns AND FKs;
///     all four empty means the emitter preserved the source's
///     structural intent.
///
/// **What's compared.**
///   - `Columns`: set of `(schema, table, column, type, nullable,
///     isPrimaryKey)` tuples.
///   - `ForeignKeys`: set of `(srcSchema, srcTable, srcCol,
///     tgtSchema, tgtTable, tgtCol)` tuples (Session B addition).
///
/// **What's NOT compared.** SsKey identity, Module structure,
/// Origin / Modality marks, Indexes (non-PK), static populations,
/// comment metadata. These are V2-IR-only axes that SQL Server's
/// catalog cannot recover. M4's Tolerance taxonomy will name
/// additional comparison flags (e.g., column length / precision;
/// indexes; FK delete-rule semantics).
type PhysicalColumn =
    {
        Schema : string
        Table : string
        Column : string
        Type : PrimitiveType
        Nullable : bool
        IsPrimaryKey : bool
        /// NVARCHAR / VARCHAR / VARBINARY length. None for MAX or
        /// non-applicable types. Per session-32 — the canary's
        /// round-trip property catches NVARCHAR(N) → NVARCHAR(M)
        /// drift when N ≠ M.
        Length : int option
        /// DECIMAL precision. None for non-decimal types. Catches
        /// DECIMAL(P, S) → DECIMAL(P', S') drift.
        Precision : int option
        /// DECIMAL scale. Same.
        Scale : int option
        /// IDENTITY column property. Catches drift in identity-ness
        /// (source had IDENTITY, target dropped it, or vice versa).
        IsIdentity : bool
    }

/// A foreign-key relationship in physical-schema coordinates. Per
/// session-31 Session B, the canary's round-trip property covers
/// FK structural fidelity: the source's FKs should appear in the
/// target after V2's emit + deploy + readback.
///
/// Composite FKs (multi-column references) appear as multiple
/// `PhysicalForeignKey` entries with the same source / target
/// table coordinates and different column pairs. Comparing as a
/// set of column-level entries handles composite cases by
/// construction.
type PhysicalForeignKey =
    {
        SourceSchema : string
        SourceTable : string
        SourceColumn : string
        TargetSchema : string
        TargetTable : string
        TargetColumn : string
    }

/// A row's content fingerprint in physical-schema coordinates.
/// Per session-33 — adds the data-plane axis to the canary's
/// round-trip surface. ReadSide produces one `PhysicalRow` per
/// (schema, table, row) tuple; PhysicalSchema's `Rows` set
/// compares by hash so the round-trip catches missing / extra /
/// mutated rows without retaining full row content in memory.
///
/// `Hash` is a deterministic SHA256 over the row's column values
/// in column-name order. Same rows → same hash; different rows →
/// different hash with overwhelming probability.
type PhysicalRow =
    {
        Schema : string
        Table : string
        Hash : string
    }

/// Structural-fidelity view of a Catalog: columns + FKs + rows.
/// Equality across all three axes is the round-trip property.
type PhysicalSchema =
    {
        Columns : Set<PhysicalColumn>
        ForeignKeys : Set<PhysicalForeignKey>
        Rows : Set<PhysicalRow>
    }

/// The diff between two `PhysicalSchema` values. All six fields
/// empty means structural-and-data intent matches; anything
/// populated is a canary-blocking divergence under R6.
type PhysicalSchemaDiff =
    {
        MissingColumns : PhysicalColumn list
        ExtraColumns : PhysicalColumn list
        MissingForeignKeys : PhysicalForeignKey list
        ExtraForeignKeys : PhysicalForeignKey list
        MissingRows : PhysicalRow list
        ExtraRows : PhysicalRow list
    }

[<RequireQualifiedAccess>]
module PhysicalSchema =

    let private toPhysicalColumns (k: Kind) : PhysicalColumn list =
        k.Attributes
        |> Bench.iterMap "physicalSchema.attribute" (fun a ->
            {
                Schema = k.Physical.Schema
                Table = k.Physical.Table
                Column = a.Column.ColumnName
                Type = a.Type
                Nullable = a.Column.IsNullable
                IsPrimaryKey = a.IsPrimaryKey
                Length = a.Length
                Precision = a.Precision
                Scale = a.Scale
                IsIdentity = a.IsIdentity
            })

    /// Hash a static row deterministically. Concatenates
    /// `<column-name>=<value>` pairs sorted by column name and
    /// SHA256s the result. Stable across runs given stable inputs.
    let private hashStaticRow (row: StaticRow) : string =
        let parts =
            row.Values
            |> Map.toList
            |> List.sortBy (fun (n, _) -> Name.value n)
            |> List.map (fun (n, v) -> sprintf "%s=%s" (Name.value n) v)
            |> String.concat ""
        let bytes = System.Text.Encoding.UTF8.GetBytes parts
        use sha = System.Security.Cryptography.SHA256.Create()
        let hash = sha.ComputeHash bytes
        System.Convert.ToHexString hash

    let private toPhysicalRows (k: Kind) : PhysicalRow list =
        k.Modality
        |> List.collect (fun m ->
            match m with
            | Static rows ->
                rows
                |> Bench.iterMap "physicalSchema.row" (fun r ->
                    {
                        Schema = k.Physical.Schema
                        Table = k.Physical.Table
                        Hash = hashStaticRow r
                    })
            | _ -> [])

    let private toPhysicalForeignKeys (catalog: Catalog) (k: Kind) : PhysicalForeignKey list =
        k.References
        |> List.choose (fun r ->
            // Resolve the source attribute's column name and the
            // target kind's first PK column. If either is missing,
            // skip the FK — it indicates an incomplete IR (caller
            // problem) rather than a comparison failure.
            let sourceColumn =
                k.Attributes
                |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
                |> Option.map (fun a -> a.Column.ColumnName)
            let targetKind = Catalog.tryFindKind r.TargetKind catalog
            let targetColumn =
                targetKind
                |> Option.bind (fun tk ->
                    tk.Attributes
                    |> List.tryFind (fun a -> a.IsPrimaryKey)
                    |> Option.map (fun a -> a.Column.ColumnName))
            match sourceColumn, targetKind, targetColumn with
            | Some srcCol, Some tk, Some tgtCol ->
                Some
                    {
                        SourceSchema = k.Physical.Schema
                        SourceTable = k.Physical.Table
                        SourceColumn = srcCol
                        TargetSchema = tk.Physical.Schema
                        TargetTable = tk.Physical.Table
                        TargetColumn = tgtCol
                    }
            | _ -> None)

    /// Project a Catalog to its `PhysicalSchema` view — the set of
    /// `(schema, table, column, type, nullable, isPrimaryKey)`
    /// tuples PLUS the set of `(src, tgt)` FK tuples reachable
    /// through every Module's Kinds. Modules, Origin, Modality,
    /// non-PK Indexes are projected out by construction.
    let ofCatalog (c: Catalog) : PhysicalSchema =
        use _ = Bench.scope "physicalSchema.ofCatalog"
        let kinds = c.Modules |> List.collect (fun m -> m.Kinds)
        let columns =
            kinds
            |> Bench.iterMap "physicalSchema.kind" toPhysicalColumns
            |> List.concat
            |> Set.ofList
        let foreignKeys =
            kinds
            |> List.collect (toPhysicalForeignKeys c)
            |> Set.ofList
        let rows =
            kinds
            |> List.collect toPhysicalRows
            |> Set.ofList
        {
            Columns = columns
            ForeignKeys = foreignKeys
            Rows = rows
        }

    /// Diff two `PhysicalSchema` values. All three axes (Columns +
    /// FKs + Rows) surface their `(missing-in-target,
    /// extra-in-target)` deltas.
    let diff (source: PhysicalSchema) (target: PhysicalSchema) : PhysicalSchemaDiff =
        {
            MissingColumns =
                Set.difference source.Columns target.Columns |> Set.toList
            ExtraColumns =
                Set.difference target.Columns source.Columns |> Set.toList
            MissingForeignKeys =
                Set.difference source.ForeignKeys target.ForeignKeys |> Set.toList
            ExtraForeignKeys =
                Set.difference target.ForeignKeys source.ForeignKeys |> Set.toList
            MissingRows =
                Set.difference source.Rows target.Rows |> Set.toList
            ExtraRows =
                Set.difference target.Rows source.Rows |> Set.toList
        }

    /// True iff the diff is empty across all six axes.
    let isEqual (d: PhysicalSchemaDiff) : bool =
        List.isEmpty d.MissingColumns
        && List.isEmpty d.ExtraColumns
        && List.isEmpty d.MissingForeignKeys
        && List.isEmpty d.MissingRows
        && List.isEmpty d.ExtraRows
        && List.isEmpty d.ExtraForeignKeys

    /// Render a diff as a human-readable multi-line string. Used by
    /// canary failure messages so the operator sees exactly which
    /// columns / FKs mismatched, not just "they differ."
    let renderDiff (d: PhysicalSchemaDiff) : string =
        let renderColumn (c: PhysicalColumn) : string =
            let len =
                match c.Length with
                | Some n -> sprintf "(%d)" n
                | None -> ""
            let prec =
                match c.Precision, c.Scale with
                | Some p, Some s -> sprintf "(%d,%d)" p s
                | Some p, None -> sprintf "(%d)" p
                | _ -> ""
            sprintf
                "  [%s].[%s].[%s] %A%s%s nullable=%b pk=%b identity=%b"
                c.Schema
                c.Table
                c.Column
                c.Type
                len
                prec
                c.Nullable
                c.IsPrimaryKey
                c.IsIdentity
        let renderFk (f: PhysicalForeignKey) : string =
            sprintf
                "  [%s].[%s].[%s] -> [%s].[%s].[%s]"
                f.SourceSchema
                f.SourceTable
                f.SourceColumn
                f.TargetSchema
                f.TargetTable
                f.TargetColumn
        let renderRow (r: PhysicalRow) : string =
            sprintf
                "  [%s].[%s] row hash=%s"
                r.Schema
                r.Table
                (r.Hash.Substring(0, min 16 r.Hash.Length))
        let block (label: string) (renderer: 'a -> string) (xs: 'a list) : string =
            if List.isEmpty xs then sprintf "%s:\n  (none)" label
            else
                sprintf "%s:\n%s" label (xs |> List.map renderer |> String.concat "\n")
        // Truncate row diffs to the first 5 entries — at scale a
        // missing-row count of thousands isn't actionable as a
        // human-readable diff; show enough to triangulate.
        let truncateRows (xs: PhysicalRow list) : PhysicalRow list =
            if List.length xs <= 5 then xs
            else List.take 5 xs
        let rowsLine (label: string) (xs: PhysicalRow list) : string =
            let total = List.length xs
            let shown = truncateRows xs
            if total = 0 then sprintf "%s:\n  (none)" label
            elif total <= 5 then
                sprintf "%s:\n%s" label (shown |> List.map renderRow |> String.concat "\n")
            else
                sprintf
                    "%s (%d total; showing first 5):\n%s"
                    label
                    total
                    (shown |> List.map renderRow |> String.concat "\n")
        String.concat
            "\n"
            [
                "PhysicalSchema diff:"
                block "Missing columns in target (source had, target lost)" renderColumn d.MissingColumns
                block "Extra columns in target (target has, source did not)" renderColumn d.ExtraColumns
                block "Missing FKs in target (source had, target lost)" renderFk d.MissingForeignKeys
                block "Extra FKs in target (target has, source did not)" renderFk d.ExtraForeignKeys
                rowsLine "Missing rows in target (source had, target lost)" d.MissingRows
                rowsLine "Extra rows in target (target has, source did not)" d.ExtraRows
            ]
