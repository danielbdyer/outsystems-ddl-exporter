namespace Projection.Targets.Data

open Projection.Core
open Projection.Targets.SSDT  // LINT-ALLOW: cross-target dependency for ScriptDom typed-AST construction (`ScriptDomBuild.*` / `ScriptDomGenerate.generateBatch`) — the staged-`#temp` MERGE is built entirely from typed nodes; same architectural shape StaticSeedsEmitter / SsdtDdlEmitter use

/// Shared staged-`#temp` MERGE rendering — the **error-8623-safe form for large
/// kinds**, factored out of `StaticSeedsEmitter` at its SECOND consumer
/// (`MigrationDependenciesEmitter`) per the codebase's "verbs extract at the
/// second consumer" discipline. A single inline `MERGE … USING (VALUES …)`
/// hits SQL Server error 8623 (the optimizer's plan-complexity wall) at
/// ~25-30k rows; above the operator's `emission.dataStaging` threshold the rows
/// stage through a `#temp` and run ONE `MERGE … USING #temp` (and, for cyclic
/// kinds, one set-based `UPDATE … FROM #fk`) inside one atomic `XACT_ABORT`
/// batch — no `VALUES` ceiling, all-or-nothing.
///
/// Generic over the kind/args: the emitters classify the kind, build the
/// `MergeBuildArgs` (with `StagedSource` set to the temp name), and decide
/// whether to stage (`DataStagingPolicy.shouldStage`); this module owns the
/// staged *rendering* both lanes share. Below-threshold callers keep the inline
/// path (byte-identical goldens); the staged form is NOT golden-locked.
[<RequireQualifiedAccess>]
module StagedMerge =

    let private up (stmt: #Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement) : Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement =
        stmt :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement

    /// Deterministic Phase-1 staging-`#temp` name — `#seed_<physical table>`.
    /// `TableId.tableText` extracts the underlying string from the `TableName` VO
    /// (a raw `k.Physical.Table` would stringify the whole value object —
    /// `TableName "X"` — and the embedded space/quotes are a SQL syntax error;
    /// that exact bug shipped once and died only at deploy).
    let stagedTempName (k: Kind) : string =
        System.String.Concat("#seed_", TableId.tableText k.Physical)  // LINT-ALLOW: terminal #temp identifier name; a temp-table name IS a string at the ScriptDom buildCreateTempTable boundary

    /// Deterministic Phase-2 staging-`#temp` name — `#fk_<physical table>`. The
    /// narrow PK + deferred-FK temp for the set-based re-point; distinct from the
    /// `#seed_` Phase-1 temp so both can coexist if a kind stages both phases.
    let fkStagedTempName (k: Kind) : string =
        System.String.Concat("#fk_", TableId.tableText k.Physical)  // LINT-ALLOW: terminal #temp identifier name; a temp-table name IS a string at the ScriptDom buildCreateTempTable boundary

    /// Staging `ColumnDef`s for a set of attributes: the target's SQL types with
    /// every constraint stripped — nullable, no identity / PK / default / computed.
    /// The `#temp` only carries rows (deferred-FK NULLs and empty→NULL values stage
    /// cleanly because all columns are nullable); the MERGE / UPDATE into the real
    /// target enforces its constraints.
    let stagingColumnDefsOf (attrs: Attribute list) : ColumnDef list =
        attrs
        |> List.map (fun a ->
            { Name         = ColumnRealization.columnNameText a.Column
              Type         = a.Type
              SqlStorage   = a.SqlStorage
              Length       = a.Length
              Precision    = a.Precision
              Scale        = a.Scale
              Nullable     = true
              IsIdentity   = false
              IsPrimaryKey = false
              DefaultValue = None
              DefaultName  = None
              Computed     = None
              Collation    = a.Column.Collation
              Identity     = None
              Provenance   = "" })

    /// Render typed inner statements through the shared typed atomic envelope
    /// (`ScriptDomBuild.buildAtomicBatch`) and frame the unit with the terminal
    /// `GO`. `generateBatch` emits the `SET XACT_ABORT ON` + `TRY/CATCH`
    /// scaffolding AND every inner DATA statement as ONE typed ScriptDom batch;
    /// the `;` terminators — including the MERGE's, which `generateOne` drops on a
    /// bare single-statement render — are emitted by the generator for the whole
    /// batch. `GO` is the sqlcmd batch directive ScriptDom does not model; it is
    /// the one terminal-text literal, keeping the `#temp` + transaction on a
    /// single connection/session (the leveled-deploy parallel path opens a
    /// connection per `GO`).
    let renderAtomicBatch (inner: Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement list) : string =
        let rendered = ScriptDomGenerate.generateBatch (ScriptDomBuild.buildAtomicBatch inner)
        System.String.Concat(rendered.TrimEnd('\n'), "\nGO\n")  // LINT-ALLOW: terminal `GO` batch-separator suffix on the fully-typed atomic batch — the only non-AST literal (sqlcmd directive), appended once at the terminal boundary

    /// Assemble the staged phase-1 batch — ONE atomic unit so the `#temp` and
    /// the transaction stay on a single connection/session:
    ///   SET XACT_ABORT ON; BEGIN TRY; BEGIN TRAN;
    ///     drop-if-exists → CREATE #temp → batched INSERTs → [guard]
    ///     → [IDENTITY_INSERT ON] → MERGE USING #temp → [OFF] → DROP;
    ///   COMMIT TRAN; END TRY BEGIN CATCH ROLLBACK; THROW; END CATCH.
    /// Every inner statement is a typed `TSqlStatement`; `renderAtomicBatch`
    /// wraps and renders the whole unit. Atomic (all-or-nothing); the `#temp` is
    /// dropped on success and rolled back (DDL is transactional) on any failure —
    /// never leaked. `args.StagedSource = Some tempName` routes the MERGE + guard
    /// to the `#temp`. `bench` is the caller's Bench scope label prefix (e.g.
    /// `"emit.staticSeeds"` / `"emit.migrationDeps"`) so the staged cost stays
    /// attributable to its lane.
    let renderStagedPhase1
        (bench: string)
        (verification: DataVerification)
        (bracketIdentity: bool)
        (withIndex: bool)
        (table: TableId)
        (k: Kind)
        (args: MergeBuildArgs)
        : string =
        use _ = Bench.scope (System.String.Concat(bench, ".renderStagedPhase1"))  // LINT-ALLOW: terminal Bench telemetry-label composition (per-lane scope prefix); a label IS a string primitive
        // This module OWNS the staged source: the `#temp` name is derived here from
        // `k` (the same derivation the inline path never needs), and the args are
        // re-pointed to `Staged` so the MERGE + guard `USING [#temp]`. The caller
        // hands inline-shaped args; the staged routing is not its concern — so
        // there is no `RowSource` precondition to violate (the old `invalidOp`).
        let tempName = stagedTempName k
        let args = { args with RowSource = MergeRowSource.Staged tempName }
        let guardOpt =
            match verification with
            | DataVerification.ValidateBeforeApply ->
                Some (ScriptDomBuild.buildValidateBeforeApplyGuard args)  // already a TSqlStatement
            | DataVerification.Standard -> None
        let identityOnOpt, identityOffOpt =
            if bracketIdentity then
                Some (up (ScriptDomBuild.buildSetIdentityInsert table true)),
                Some (up (ScriptDomBuild.buildSetIdentityInsert table false))
            else None, None
        // The clustered `#temp`-PK index — built AFTER the bulk-heap INSERTs and
        // before the MERGE, so the MERGE merge-joins target↔`#temp`. Gated above
        // `IndexThreshold` (measured 2026-06-25); dropped WITH the `#temp`.
        let indexOpt =
            if withIndex then
                let pkCols =
                    k.Attributes
                    |> List.filter (fun a -> a.IsPrimaryKey)
                    |> List.map (fun a -> ColumnRealization.columnNameText a.Column)
                match pkCols with
                | [] -> None  // no PK → no index possible (the MERGE has nothing to merge-join on)
                | _  -> Some (up (ScriptDomBuild.buildClusteredTempIndex (System.String.Concat("ix_stg_", TableId.tableText k.Physical)) tempName pkCols))  // LINT-ALLOW: terminal #temp index identifier name at the ScriptDom boundary
            else None
        // Inner statements in deploy order; the optional pieces (guard, identity
        // brackets, index) drop out cleanly via `List.choose id`.
        let inner =
            [ Some (up (ScriptDomBuild.buildDropTableIfExists tempName))
              Some (up (ScriptDomBuild.buildCreateTempTable tempName (stagingColumnDefsOf (KindColumns.writableAttributes k)))) ]
            @ (ScriptDomBuild.buildInsertBatches tempName args.AllColumns args.Rows |> List.map (up >> Some))
            @ [ indexOpt
                guardOpt
                identityOnOpt
                Some (up (ScriptDomBuild.buildMergeStatement args).Value)
                identityOffOpt
                Some (up (ScriptDomBuild.buildDropTable tempName)) ]
            |> List.choose id
        renderAtomicBatch inner

    /// Assemble the staged phase-2 batch — the set-based escalation of the
    /// per-row Phase-2 UPDATEs for a LARGE cyclic kind (deferred FKs AND above
    /// the staging threshold). A NARROW `#fk_<table>` stages the PK + deferred-FK
    /// columns carrying their REAL values (NOT the Phase-1 NULL form), then ONE
    /// `UPDATE … FROM target JOIN #fk` re-points every deferred FK in a single
    /// statement (`ScriptDomBuild.buildUpdateFromTemp`), wrapped in the same
    /// atomic envelope. `narrowRows` projects the typed rows over PK+deferred
    /// directly — bypassing the Phase-1 deferred-NULLing — so the `#fk` temp
    /// holds the resolved FK targets.
    let renderStagedPhase2
        (bench: string)
        (cdcAware: bool)
        (table: TableId)
        (k: Kind)
        (deferred: Set<Name>)
        (typedRows: Map<Name, SqlLiteral> list)
        : string =
        use _ = Bench.scope (System.String.Concat(bench, ".renderStagedPhase2"))  // LINT-ALLOW: terminal Bench telemetry-label composition (per-lane scope prefix); a label IS a string primitive
        let tempName = fkStagedTempName k
        // Row identity via the shared match vocabulary: true PKs, or the
        // writable-column fallback (deferred columns excluded — the join
        // must find the Phase-1 row whose deferred columns are NULL).
        let pkAttrs       = KindColumns.matchAttributes deferred k
        let deferredAttrs = k.Attributes |> List.filter (fun a -> Set.contains a.Name deferred)
        // Match columns first, then the deferred columns — the `#fk` temp's
        // column order; `buildUpdateFromTemp` joins on `pkCols` and SETs
        // `setCols` by name.
        let narrowAttrs = pkAttrs @ deferredAttrs
        let colOf (a: Attribute) = ColumnRealization.columnNameText a.Column
        let pkCols  = pkAttrs       |> List.map colOf
        let setCols = deferredAttrs |> List.map colOf
        let narrowCols = narrowAttrs |> List.map colOf
        // Real values over the narrow column set (no deferred-NULLing — Phase-1
        // owns the NULL form; Phase-2's `#fk` temp carries the resolved targets).
        let narrowRows =
            typedRows
            |> List.map (fun values ->
                narrowAttrs
                |> List.map (fun a ->
                    Map.tryFind a.Name values |> Option.defaultValue SqlLiteral.NullLit))
        let inner =
            [ up (ScriptDomBuild.buildDropTableIfExists tempName)
              up (ScriptDomBuild.buildCreateTempTable tempName (stagingColumnDefsOf narrowAttrs)) ]
            @ (ScriptDomBuild.buildInsertBatches tempName narrowCols narrowRows |> List.map up)
            @ [ ScriptDomBuild.buildUpdateFromTemp table tempName setCols pkCols cdcAware  // already a TSqlStatement
                up (ScriptDomBuild.buildDropTable tempName) ]
        renderAtomicBatch inner
