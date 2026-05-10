namespace Projection.Targets.Data

open System.Text
open Projection.Core
open Projection.Targets.SSDT  // LINT-ALLOW: provisional cross-target dependency for `Render.formatSqlLiteral` SQL-literal formatter (the IR→SQL boundary primitive); two-consumer N=2 promotion to a concept-shaped `Projection.Core.SqlLiteral` module lands at slice ε when MigrationDependenciesEmitter joins as the third consumer; rationale documented in `Projection.Targets.Data.fsproj` ProjectReference comment

/// Π_StaticSeeds — chapter 4.1.B slice α emitter for static-modality
/// kinds. Consumes the `Catalog`'s `Modality.Static` populations and
/// produces idempotent MERGE statements per V1 trunk's `StaticSeed
/// SqlBuilder.cs:211-260` shape (V1 parity at slice α; the change-
/// detection predicate that closes CDC-noise lands at slice β).
///
/// **A18 amended.** The signature carries `Catalog × Profile`; Profile
/// is reserved for the slice-β `CdcAwareness` field consumption. No
/// `Policy` parameter — DataComposition dispatch happens in the
/// composer (slice η), not here.
///
/// **T11 sibling-Π commutativity.** The emitter produces an
/// `ArtifactByKind<DataInsertScript>` keyed by every catalog kind.
/// Kinds without `Modality.Static` produce a script with empty
/// `Phase1Merges` (no-op artifact) — per the strict-equality T11
/// invariant: every kind appears, no kind is silently absent.
[<RequireQualifiedAccess>]
module StaticSeedsEmitter =

    [<Literal>]
    let version : int = 1

    /// Collect the `StaticRow list` from a kind's `Modality` marks.
    /// A kind may carry multiple `ModalityMark` variants; only `Static`
    /// is consumed here. Returns `[]` for kinds without static
    /// populations.
    let private staticPopulations (k: Kind) : StaticRow list =
        k.Modality
        |> List.tryPick (fun m ->
            match m with
            | Static populations -> Some populations
            | _                  -> None)
        |> Option.defaultValue []

    /// Type-resolution lookup for a kind's columns. Returns the
    /// (column-name, primitive-type) pair for each attribute, so the
    /// renderer can format raw IR values as SQL literals.
    let private columnTypeLookup (k: Kind) : Map<Name, PrimitiveType> =
        k.Attributes
        |> List.map (fun a -> a.Name, a.Type)
        |> Map.ofList

    /// Order columns deterministically (matches V1 + the SSDT emitter).
    /// Per A33 (deterministic-ordered schema emission), sort by the
    /// kind's declared attribute order — which is itself canonical
    /// after `CanonicalizeIdentity`.
    let private orderedColumnNames (k: Kind) : string list =
        k.Attributes |> List.map (fun a -> a.Column.ColumnName)

    /// Primary-key column names in the kind's declared order. The
    /// MERGE's ON-clause joins on these; the WHEN-NOT-MATCHED INSERT
    /// includes them; the WHEN-MATCHED UPDATE excludes them (PK is
    /// stable per row identity).
    let private pkColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> a.IsPrimaryKey)
        |> List.map (fun a -> a.Column.ColumnName)

    /// Non-PK column names (the MERGE's UPDATE-target columns).
    let private updatableColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> not a.IsPrimaryKey)
        |> List.map (fun a -> a.Column.ColumnName)

    /// Format one row's column values into a comma-joined VALUES tuple
    /// like `(1, 'US', 'United States')`. Type resolution flows through
    /// `Render.formatSqlLiteral` (provisional Data → SSDT dependency at
    /// slice α; promotes to `Projection.Core.SqlLiteral` at slice ε per
    /// the project-file rationale).
    let private formatValuesTuple
        (typeLookup: Map<Name, PrimitiveType>)
        (attributes: Attribute list)
        (row: StaticRow)
        : string =
        let formatted =
            attributes
            |> List.map (fun a ->
                let raw =
                    Map.tryFind a.Name row.Values
                    |> Option.defaultValue ""
                let typ =
                    Map.tryFind a.Name typeLookup
                    |> Option.defaultValue PrimitiveType.Text
                Render.formatSqlLiteral typ raw)
        System.String.Concat("(", System.String.Join(", ", formatted), ")")  // LINT-ALLOW: VALUES-tuple terminal text formatting; segments are typed (each `formatted` element is the result of typed `Render.formatSqlLiteral`); BCL `String.Join` is the use-case-specific library

    /// Render a single MERGE statement for a kind with its static
    /// populations. Mirrors V1's `StaticSeedSqlBuilder.AppendMerge
    /// Statement` (`StaticSeedSqlBuilder.cs:211-260`):
    ///
    /// ```sql
    /// MERGE INTO [dbo].[OSUSR_S1S_COUNTRY] AS Target
    /// USING
    /// (
    ///     VALUES
    ///         (1, 'US', 'United States'),
    ///         (2, 'CA', 'Canada')
    /// ) AS Source ([Id], [Code], [Label])
    ///     ON Target.[Id] = Source.[Id]
    /// WHEN MATCHED THEN UPDATE SET
    ///     Target.[Code] = Source.[Code],
    ///     Target.[Label] = Source.[Label]
    /// WHEN NOT MATCHED THEN INSERT ([Id], [Code], [Label])
    ///     VALUES (Source.[Id], Source.[Code], Source.[Label])
    /// ;
    /// ```
    /// Build the change-detection predicate per pre-scope §6 + chapter
    /// 4.1.B slice β. The predicate fires WHEN MATCHED to update only
    /// when at least one non-key column differs between source and
    /// target. Nullable-aware comparator (NULL ≠ NULL in SQL):
    ///
    /// ```sql
    /// WHEN MATCHED AND (
    ///     Target.[col1] <> Source.[col1] OR
    ///     (Target.[col1] IS NULL AND Source.[col1] IS NOT NULL) OR
    ///     (Target.[col1] IS NOT NULL AND Source.[col1] IS NULL) OR
    ///     ...  -- repeat per non-key column
    /// ) THEN UPDATE SET ...
    /// ```
    ///
    /// This predicate IS the structural commitment that closes CDC-
    /// noise on idempotent redeploys: identical content fires no
    /// UPDATE → CDC capture-process emits no row → consuming features
    /// see no spurious change. The slice-γ canary verifies this under
    /// real SQL Server CDC semantics.
    let private buildChangeDetectionPredicate (updCols: string list) : string =
        let perColumn (col: string) : string =
            let q = Render.quote col
            // Three OR-conditions per column: value-mismatch + NULL-asymmetry-each-way.
            System.String.Concat(  // LINT-ALLOW: change-detection predicate fragment composition for one non-key column; segments are typed (Render.quote returns ScriptDom-encoded identifier); future ScriptDom MergeStatement adoption deferred to slice ζ
                "Target.", q, " <> Source.", q, " OR ",
                "(Target.", q, " IS NULL AND Source.", q, " IS NOT NULL) OR ",
                "(Target.", q, " IS NOT NULL AND Source.", q, " IS NULL)")
        updCols
        |> List.map perColumn
        |> String.concat " OR\n        "  // LINT-ALLOW: terminal OR-joiner across per-column change-detection fragments; BCL `String.concat` IS the use-case-specific library (collection-joiner gold-standard); segments are typed

    let private renderMerge
        (cdcAware: bool)
        (k: Kind)
        (rows: StaticRow list)
        : string =
        use _ = Bench.scope "emit.staticSeeds.renderMerge"
        let sb = StringBuilder()
        let table : TableId =
            { Schema = k.Physical.Schema
              Table  = k.Physical.Table }
        let allCols = orderedColumnNames k
        let pkCols = pkColumnNames k
        let updCols = updatableColumnNames k
        let typeLookup = columnTypeLookup k
        let columnList = allCols |> List.map Render.quote |> String.concat ", "  // LINT-ALLOW: terminal SQL-DDL column-list joiner; BCL `String.concat` IS the use-case-specific library (collection-joiner gold-standard); segments are typed (each `Render.quote` returns ScriptDom-encoded identifier text)

        sb.Append("MERGE INTO ").Append(Render.tableQualified table).AppendLine(" AS Target")
            .AppendLine("USING")
            .AppendLine("(")
            .AppendLine("    VALUES")
        |> ignore

        let lastIdx = rows.Length - 1
        rows
        |> List.iteri (fun i row ->
            let tuple = formatValuesTuple typeLookup k.Attributes row
            let sep = if i < lastIdx then "," else ""
            sb.Append("        ").Append(tuple).AppendLine(sep) |> ignore)

        sb.Append(") AS Source (").Append(columnList).AppendLine(")") |> ignore

        let onClause =
            pkCols
            |> List.map (fun c -> System.String.Concat("Target.", Render.quote c, " = Source.", Render.quote c))  // LINT-ALLOW: ON-clause column-equality fragment; segments are typed (Render.quote returns ScriptDom-encoded identifier)
            |> String.concat " AND "  // LINT-ALLOW: terminal AND-joiner across PK column-equality fragments; BCL `String.concat` IS the use-case-specific library (collection joiner gold-standard); segments are typed
        sb.Append("    ON ").AppendLine(onClause) |> ignore

        if not (List.isEmpty updCols) then
            // CDC-aware dispatch per pre-scope §6 + slice β: the change-
            // detection predicate suppresses no-op UPDATEs on identical
            // content, closing the CDC-noise hole. CDC-disabled kinds
            // keep V1's predicate-free WHEN MATCHED (V1 already proven
            // correct in trunk; the CDC-noise path is irrelevant for
            // non-tracked tables).
            if cdcAware then
                let predicate = buildChangeDetectionPredicate updCols
                sb.AppendLine("WHEN MATCHED AND (")
                    .Append("        ").AppendLine(predicate)
                    .AppendLine("    ) THEN UPDATE SET")
                |> ignore
            else
                sb.AppendLine("WHEN MATCHED THEN UPDATE SET") |> ignore
            let lastUpdIdx = updCols.Length - 1
            updCols
            |> List.iteri (fun i c ->
                let sep = if i < lastUpdIdx then "," else ""
                sb.Append("    Target.").Append(Render.quote c)
                    .Append(" = Source.").Append(Render.quote c).AppendLine(sep)
                |> ignore)

        sb.Append("WHEN NOT MATCHED THEN INSERT (").Append(columnList).AppendLine(")") |> ignore
        let valuesTuple =
            allCols
            |> List.map (fun c -> System.String.Concat("Source.", Render.quote c))  // LINT-ALLOW: VALUES Source.<col> reference; segments are typed (Render.quote returns ScriptDom-encoded identifier)
            |> String.concat ", "  // LINT-ALLOW: terminal comma-joiner across VALUES Source.<col> references; BCL `String.concat` IS the use-case-specific library (collection-joiner gold-standard); segments are typed
        sb.Append("    VALUES (").Append(valuesTuple).AppendLine(")")
            .AppendLine(";")
            .AppendLine("GO")
        |> ignore

        sb.ToString()

    /// Build one `DataInsertScript` for a kind. Empty-population kinds
    /// produce a no-op script (empty Phase1Merges, empty Rendered);
    /// per T11 strict-equality keyset, the script is still keyed in
    /// the artifact map. CDC-aware dispatch per slice β: the kind's
    /// `Profile.CdcAwareness.CdcEnabled` membership selects the
    /// change-detection-predicate variant.
    let private kindToScript (cdc: CdcAwareness) (k: Kind) : DataInsertScript =
        let populations = staticPopulations k
        if List.isEmpty populations then
            { Phase1Merges = []; Phase2Updates = []; Rendered = "" }
        else
            let cdcAware = CdcAwareness.isEnabled k.SsKey cdc
            let rendered = renderMerge cdcAware k populations
            let rows =
                populations
                |> List.map (fun row ->
                    { KindKey    = k.SsKey
                      Identifier = row.Identifier
                      Values     = row.Values })
            { Phase1Merges = rows
              Phase2Updates = []
              Rendered     = rendered }

    /// Π_StaticSeeds emit. Per A18 amended (Catalog × Profile, never
    /// Policy) and T11 (every kind in the keyset). Slice β consumes
    /// `Profile.CdcAwareness` for per-kind change-detection-predicate
    /// dispatch (the load-bearing semantic addition that closes
    /// CDC-noise on idempotent redeploys per `V2_DRIVER.md`).
    let emit
        (catalog: Catalog)
        (profile: Profile)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.staticSeeds.emit"
        let cdc = profile.CdcAwareness
        let allKinds = Catalog.allKinds catalog
        let slices =
            allKinds
            |> List.map (fun k -> k.SsKey, kindToScript cdc k)
            |> Map.ofList
        ArtifactByKind.create catalog slices
