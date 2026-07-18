namespace Projection.Targets.Data

open Projection.Core
open Projection.Targets.SSDT  // LINT-ALLOW: cross-target dependency for ScriptDom typed-AST MERGE/UPDATE construction (`ScriptDomBuild` / `ScriptDomGenerate`) — the shared render engine both data emitters consume; same architectural shape StaticSeedsEmitter / StagedMerge use

/// The shared Phase-1 MERGE / Phase-2 UPDATE rendering both row-emitting lanes
/// (`StaticSeedsEmitter`, `MigrationDependenciesEmitter`) consume. Extracted at
/// the SECOND consumer per the "verbs extract at the second consumer" discipline:
/// the two emitters' `renderMerge` / `renderUpdate` were byte-identical modulo
/// their Bench-scope label — and had DRIFTED to a swapped `deferred` /
/// `bracketIdentity` argument order, a latent hazard a single signature removes.
///
/// `bench` is the caller's per-lane Bench scope prefix (`"emit.staticSeeds"` /
/// `"emit.migrationDeps"`) so the staged / inline / rows telemetry stays
/// attributable per lane — the labels are byte-for-byte what the per-emitter
/// copies emitted.
[<RequireQualifiedAccess>]
module MergeRender =

    /// Render the Phase-1 MERGE for a kind. Above the operator's
    /// `emission.dataStaging` threshold the rows route through a `#temp` (the SQL
    /// Server error-8623-safe form via the shared `StagedMerge`); below, the
    /// inline `USING (VALUES …)` form stands — byte-identical to the pre-staging
    /// output. `bracketIdentity` brackets the MERGE with `SET IDENTITY_INSERT`;
    /// deferred columns are excluded from the WHEN-MATCHED UPDATE (Phase-2 owns
    /// them — the cross-emitter CDC-silence invariant). `verification` optionally
    /// prepends the symmetric-EXCEPT drift guard (NM-73).
    let renderMerge
        (bench: string)
        (verification: DataVerification)
        (staging: DataStagingPolicy)
        (deleteScope: DeleteScope option)
        (cdcAware: bool)
        (deferred: Set<Name>)
        (bracketIdentity: bool)
        (k: Kind)
        (typedRows: Map<Name, SqlLiteral> list)
        : string =
        use _ = Bench.scope (System.String.Concat(bench, ".renderMerge"))  // LINT-ALLOW: terminal Bench telemetry-label composition (per-lane scope prefix); a label IS a string primitive
        // PL-3 (S19/S38/S20/S59) — the per-kind render vocabulary binds
        // ONCE at the top: row count (was up to three O(n) length walks),
        // writable attributes (was ×3 across the inline + staged paths),
        // and the row-identity match names (was ×2).
        let rowCount = List.length typedRows
        let writable = KindColumns.writableAttributes k
        let matchNames = KindColumns.matchColumnNames deferred k
        // PERF_HARNESS §3.6 label 2: rows per rendered MERGE — makes rows/sec
        // derivable from the <label>/<label>.rows pair in harness diffs.
        Bench.recordSample (System.String.Concat(bench, ".renderMerge.rows")) (int64 rowCount)  // LINT-ALLOW: terminal Bench telemetry-label composition (per-lane scope prefix); a label IS a string primitive
        let table = TableId.withoutCatalog k.Physical
        // Slice 5.13.cdc-silence-cross-emitter: exclude deferred columns from the
        // WHEN MATCHED UPDATE's UpdColumns — they are Phase-2-owned; including
        // them makes an idempotent redeploy set the column to the Phase-1 NULL,
        // then Phase-2 sets it back (4 CDC entries/row). Gap N2: a persisted
        // computed column is never an UPDATE target (a hard SQL error) and never
        // enters the change-detection predicate, so exclude it alongside PK +
        // deferred.
        // The row-identity match columns (true PKs, or the writable-column
        // fallback for an acknowledged no-PK kind). A column that IS the
        // row identity is never an UPDATE target — for a PK kind that is
        // the existing PK exclusion; for the no-PK fallback it empties the
        // WHEN MATCHED arm entirely (matched rows are identical over every
        // matchable column by construction, and `buildMergeStatement`
        // skips the arm when UpdColumns is empty).
        let matchColumns = matchNames |> Set.ofList
        let updColumns =
            k.Attributes
            |> List.filter (fun a -> not a.IsPrimaryKey)
            |> List.filter (fun a -> not (Set.contains a.Name deferred))
            |> List.filter (fun a -> a.Computed = None)
            // An IDENTITY column can never be an UPDATE target (SQL error
            // 8102 — `SET IDENTITY_INSERT` licenses inserts only, never
            // updates); it reaches its value through the INSERT arm.
            |> List.filter (fun a -> not a.IsIdentity)
            |> List.map (fun a -> ColumnRealization.columnNameText a.Column)
            |> List.filter (fun c -> not (Set.contains c matchColumns))
        let args : MergeBuildArgs =
            {
                Target     = table
                AllColumns = writable |> List.map (fun a -> ColumnRealization.columnNameText a.Column)
                // Row identity: true PKs, or the writable-column fallback
                // for an acknowledged no-PK kind (never empty — an empty
                // ON-term list is a hard `foldBool` refusal downstream).
                PkColumns  = matchNames
                UpdColumns = updColumns
                Rows        = typedRows |> List.map (KindColumns.typedValuesToSqlLiterals deferred writable)
                CdcAware    = cdcAware
                DeleteScope = deleteScope
                RowSource   = MergeRowSource.InlineValues
                // WP-17(c) — the comparison-less storage types (`xml` +
                // the legacy LOBs `image`/`text`/`ntext`) have no `<>`
                // operator; their change-detect compare CASTs both sides
                // to the type's legal MAX target (content-level). Empty
                // for kinds without them — the predicate is byte-identical.
                CastCompareColumns =
                    k.Attributes
                    |> List.choose (fun a ->
                        match a.SqlStorage with
                        | Some SqlStorageType.Xml
                        | Some SqlStorageType.NText -> Some (ColumnRealization.columnNameText a.Column, CastToNVarCharMax)
                        | Some SqlStorageType.Text  -> Some (ColumnRealization.columnNameText a.Column, CastToVarCharMax)
                        | Some SqlStorageType.Image -> Some (ColumnRealization.columnNameText a.Column, CastToVarBinaryMax)
                        | _ -> None)
                    |> Map.ofList
            }
        if DataStagingPolicy.shouldStage staging rowCount then
            Bench.recordSample (System.String.Concat(bench, ".staged")) 1L  // LINT-ALLOW: terminal Bench telemetry-label composition (per-lane scope prefix); a label IS a string primitive
            StagedMerge.renderStagedPhase1 bench verification bracketIdentity
                (DataStagingPolicy.shouldIndex staging rowCount) table writable k
                args
        else
        Bench.recordSample (System.String.Concat(bench, ".inline")) 1L  // LINT-ALLOW: terminal Bench telemetry-label composition (per-lane scope prefix); a label IS a string primitive
        // The inline MERGE as a typed `Statement` batch. The terminal `;` + `GO`
        // framing lives in `ScriptDomGenerate.renderDataBatch` (the data lane's
        // ONE terminal-text boundary), not here. An IDENTITY-PK static kind seeds
        // explicit PK values, so the WHEN-NOT-MATCHED INSERT writes the IDENTITY
        // column and SQL Server requires `SET IDENTITY_INSERT [t] ON`; the toggle
        // is SESSION-scoped and the leveled deploy opens a connection per GO, so
        // the bracket MUST stay ONE GO batch.
        let mergeBatch =
            if not bracketIdentity then
                ScriptDomGenerate.renderDataBatch [ Statement.Merge args ]
            else
                ScriptDomGenerate.renderDataBatch
                    [ Statement.SetIdentityInsert (table, true)
                      Statement.Merge args
                      Statement.SetIdentityInsert (table, false) ]
        // NM-73 — prepend the validate-before-apply drift guard as its OWN GO
        // batch before the MERGE. `Standard` is byte-identical; the guard is the
        // typed parse-template render of V1's symmetric-`EXCEPT` THROW.
        match verification with
        | DataVerification.Standard -> mergeBatch
        | DataVerification.ValidateBeforeApply ->
            let guardText =
                ScriptDomGenerate.generateOne (ScriptDomBuild.buildValidateBeforeApplyGuard args)
            System.String.Concat(  // LINT-ALLOW: terminal guard-batch prefix (NM-73); the guard is the typed-AST parse-template render of V1's symmetric-EXCEPT THROW, framed as its own GO batch ahead of the MERGE; the V1 `GO` batch separator is the terminal-text literal; BCL `String.Concat` is the right primitive at this terminal-text boundary
                guardText, "\nGO\n", mergeBatch)

    /// Render the D10 static-content ALIGNMENT MERGE for `check environments`
    /// remediation — bring one environment's static table into agreement with
    /// the model's declared seed, **matched by the business key**. This is the
    /// estate's reconciliation, NOT the deploy lane, and it differs from
    /// `renderMerge` in the three ways the D10/D11 split demands:
    ///   - the ON clause joins on the BUSINESS KEY (`businessKey`), never the
    ///     surrogate PK — the environments each mint their own surrogate (that
    ///     disagreement is D11's ruling, ruled elsewhere), so the surrogate is
    ///     meaningless across the estate and is EXCLUDED from ON / INSERT /
    ///     UPDATE entirely;
    ///   - WHEN NOT MATCHED BY TARGET INSERT writes every writable column
    ///     EXCEPT the surrogate — so the sink mints its own key for the new row
    ///     (no `SET IDENTITY_INSERT`, ever);
    ///   - WHEN MATCHED UPDATE sets every writable non-key column (the business
    ///     key is the match column, never a SET target).
    /// There is deliberately NO `WHEN NOT MATCHED BY SOURCE DELETE`: the seed is
    /// authoritative for what the table SHOULD contain, but a row present in the
    /// environment and absent from the seed may be referenced, so its removal is
    /// a separate operator ruling (the block's note names it) — and the
    /// `DeleteScope` smart constructor rightly refuses an unscoped table-wide
    /// delete. `CdcAware = false`: this is operator-run remediation, not a
    /// CDC-silent redeploy. The whole batch renders through the shared
    /// `ScriptDomGenerate.renderDataBatch` — a real MERGE, not a comment stub —
    /// and the emitter comments it line-by-line (the operator-safety contract).
    /// Empty string when the kind carries no business-key column among its
    /// writables (the caller already skips these by name) or the seed is empty.
    let renderAlignmentMerge
        (bench: string)
        (businessKey: Name)
        (surrogatePk: Name)
        (k: Kind)
        (seedRows: StaticRow list)
        : string =
        use _ = Bench.scope (System.String.Concat(bench, ".renderAlignmentMerge"))  // LINT-ALLOW: terminal Bench telemetry-label composition; a label IS a string primitive
        let colOf (a: Attribute) : string = ColumnRealization.columnNameText a.Column
        // Writable columns MINUS the surrogate PK — the alignment never writes
        // or matches on the surrogate (each environment mints its own; D11's
        // concern). `writableAttributes` already drops computed columns.
        let writable =
            KindColumns.writableAttributes k
            |> List.filter (fun a -> a.Name <> surrogatePk)
        match writable |> List.tryFind (fun a -> a.Name = businessKey) with
        | None -> ""
        | Some _ when List.isEmpty seedRows -> ""
        | Some bkAttr ->
            let bkCol = colOf bkAttr
            let allColumns = writable |> List.map colOf
            // UPDATE targets: every writable non-surrogate column that is NOT the
            // business key (the ON column is never a SET target). Empty ⇒ the
            // builder emits no WHEN-MATCHED arm (a two-column static entity —
            // surrogate + business key — only ever needs the INSERT arm).
            let updColumns =
                writable
                |> List.filter (fun a -> a.Name <> businessKey)
                |> List.map colOf
            let typeLookup = KindColumns.columnTypeLookup k
            let args : MergeBuildArgs =
                {
                    Target      = TableId.withoutCatalog k.Physical
                    AllColumns  = allColumns
                    PkColumns   = [ bkCol ]
                    UpdColumns  = updColumns
                    Rows        =
                        seedRows
                        |> List.map (
                            KindColumns.rowToTypedValues typeLookup writable
                            >> KindColumns.typedValuesToSqlLiterals Set.empty writable)
                    CdcAware    = false
                    DeleteScope = None
                    RowSource   = MergeRowSource.InlineValues
                    CastCompareColumns = Map.empty
                }
            ScriptDomGenerate.renderDataBatch [ Statement.Merge args ]

    /// Render Phase-2 UPDATEs for rows whose Phase-1 MERGE deferred their
    /// same-SCC FK columns to NULL. The UPDATE scopes by the row's PK
    /// (`whereCells`) and SETs each deferred column to its original value. Per the
    /// Tier-3 hard-requirement, the typed `ScriptDomBuild.buildUpdateStatement`
    /// flows through `ScriptDomGenerate.renderDataBatch`'s `;\nGO\n` framing.
    /// PL-3 (S27/S57) — the per-KIND prebound renderer: the Bench label,
    /// deploy target, and the SET/WHERE attribute projections are per-kind
    /// constants (the staged sibling `renderStagedPhase2` already hoists the
    /// same projections); the row loop threads only `typedValues`. The K13
    /// kill bounds the per-row lane at the ≤1000-row staging threshold, so
    /// this is as much the cleaner shape as it is wall-clock.
    let renderUpdateForKind
        (bench: string)
        (cdcAware: bool)
        (k: Kind)
        (deferred: Set<Name>)
        : Map<Name, SqlLiteral> -> string =
        let label = System.String.Concat(bench, ".renderUpdate")  // LINT-ALLOW: terminal Bench telemetry-label composition (per-lane scope prefix); a label IS a string primitive
        let table = TableId.withoutCatalog k.Physical
        let setAttrs =
            k.Attributes
            |> List.filter (fun a -> Set.contains a.Name deferred)
        // Row scope: true PKs, or the writable-column fallback for an
        // acknowledged no-PK kind. The fallback EXCLUDES the deferred
        // columns so the UPDATE can join back to the Phase-1 row whose
        // deferred columns were intentionally nulled (matching on them
        // would compare the staged real value against the Phase-1 NULL
        // and never find the row).
        let whereAttrs = KindColumns.matchAttributes deferred k
        fun (typedValues: Map<Name, SqlLiteral>) ->
            use _ = Bench.scope label
            let cellOf (a: Attribute) : string * SqlLiteral =
                let lit =
                    Map.tryFind a.Name typedValues
                    |> Option.defaultValue SqlLiteral.NullLit
                ColumnRealization.columnNameText a.Column, lit
            let args : UpdateBuildArgs =
                { Target     = table
                  SetCells   = setAttrs |> List.map cellOf
                  WhereCells = whereAttrs |> List.map cellOf
                  CdcAware   = cdcAware }
            ScriptDomGenerate.renderDataBatch [ Statement.Update args ]

    /// The one-row compute-then-delegate form (single-shot callers; the
    /// emitters' row loops prebind `renderUpdateForKind`).
    let renderUpdate
        (bench: string)
        (cdcAware: bool)
        (k: Kind)
        (deferred: Set<Name>)
        (typedValues: Map<Name, SqlLiteral>)
        : string =
        renderUpdateForKind bench cdcAware k deferred typedValues
