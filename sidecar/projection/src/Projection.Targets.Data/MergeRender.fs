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
        // PERF_HARNESS §3.6 label 2: rows per rendered MERGE — makes rows/sec
        // derivable from the <label>/<label>.rows pair in harness diffs.
        Bench.recordSample (System.String.Concat(bench, ".renderMerge.rows")) (int64 typedRows.Length)  // LINT-ALLOW: terminal Bench telemetry-label composition (per-lane scope prefix); a label IS a string primitive
        let table : TableId =
            { Schema = k.Physical.Schema
              Table  = k.Physical.Table; Catalog = None }
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
        let matchColumns = KindColumns.matchColumnNames deferred k |> Set.ofList
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
                AllColumns = KindColumns.orderedColumnNames k
                // Row identity: true PKs, or the writable-column fallback
                // for an acknowledged no-PK kind (never empty — an empty
                // ON-term list is a hard `foldBool` refusal downstream).
                PkColumns  = KindColumns.matchColumnNames deferred k
                UpdColumns = updColumns
                Rows        = typedRows |> List.map (KindColumns.typedValuesToSqlLiterals deferred (KindColumns.writableAttributes k))
                CdcAware    = cdcAware
                DeleteScope = deleteScope
                RowSource   = MergeRowSource.InlineValues
            }
        if DataStagingPolicy.shouldStage staging (List.length typedRows) then
            Bench.recordSample (System.String.Concat(bench, ".staged")) 1L  // LINT-ALLOW: terminal Bench telemetry-label composition (per-lane scope prefix); a label IS a string primitive
            StagedMerge.renderStagedPhase1 bench verification bracketIdentity
                (DataStagingPolicy.shouldIndex staging (List.length typedRows)) table k
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

    /// Render one Phase-2 UPDATE for a row whose Phase-1 MERGE deferred its
    /// same-SCC FK columns to NULL. The UPDATE scopes by the row's PK
    /// (`whereCells`) and SETs each deferred column to its original value. Per the
    /// Tier-3 hard-requirement, the typed `ScriptDomBuild.buildUpdateStatement`
    /// flows through `ScriptDomGenerate.renderDataBatch`'s `;\nGO\n` framing.
    let renderUpdate
        (bench: string)
        (cdcAware: bool)
        (k: Kind)
        (deferred: Set<Name>)
        (typedValues: Map<Name, SqlLiteral>)
        : string =
        use _ = Bench.scope (System.String.Concat(bench, ".renderUpdate"))  // LINT-ALLOW: terminal Bench telemetry-label composition (per-lane scope prefix); a label IS a string primitive
        let table : TableId =
            { Schema = k.Physical.Schema
              Table  = k.Physical.Table; Catalog = None }
        let cellOf (a: Attribute) : string * SqlLiteral =
            let lit =
                Map.tryFind a.Name typedValues
                |> Option.defaultValue SqlLiteral.NullLit
            ColumnRealization.columnNameText a.Column, lit
        let setCells =
            k.Attributes
            |> List.filter (fun a -> Set.contains a.Name deferred)
            |> List.map cellOf
        // Row scope: true PKs, or the writable-column fallback for an
        // acknowledged no-PK kind. The fallback EXCLUDES the deferred
        // columns so the UPDATE can join back to the Phase-1 row whose
        // deferred columns were intentionally nulled (matching on them
        // would compare the staged real value against the Phase-1 NULL
        // and never find the row).
        let whereCells =
            KindColumns.matchAttributes deferred k
            |> List.map cellOf
        let args : UpdateBuildArgs =
            { Target     = table
              SetCells   = setCells
              WhereCells = whereCells
              CdcAware   = cdcAware }
        ScriptDomGenerate.renderDataBatch [ Statement.Update args ]
