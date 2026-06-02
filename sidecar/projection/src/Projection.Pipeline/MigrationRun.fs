namespace Projection.Pipeline

open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Projection.Targets.SSDT

/// The composed artifacts of a planned migration — the schema differential
/// (`ALTER` statements + their diagnostics, `SchemaMigrationEmitter`) and the
/// RefactorLog (data-preserving renames, `RefactorLogEmitter`), both projected
/// from the plan's displacement `B ⊖ A`. This is the dry-run output the operator
/// reviews before executing: every minimum-viable touch made explicit.
type MigrationArtifacts =
    {
        Plan              : MigrationPlan
        SchemaStatements  : Statement list
        SchemaDiagnostics : DiagnosticEntry list
        RefactorLog       : RefactorLogEntry list
    }

/// The fail-loud refusals of `migrate A B`, surfaced **before any write**.
type MigrationError =
    /// `CatalogDiff.between` could not observe the displacement.
    | DiffFailed of EmitError
    /// The plan carries destructive drops and `allowDrops` was not set.
    | RefusedByViolations of MigrationViolation list
    /// The schema emitter raised `Error`-severity refusals (a non-shape facet
    /// change it cannot express as a single `ALTER`).
    | RefusedBySchemaErrors of DiagnosticEntry list
    /// The RefactorLog emitter failed to project the rename channel.
    | EmitFailed of EmitError
    /// Reading the source/target schema back from the live database failed.
    | SchemaReadFailed of ValidationError list
    /// Executing the migration SQL against the live database raised.
    | ExecutionFailed of message: string
    /// The migration executed but B' does not reproduce B at the physical level.
    | VerificationFailed of PhysicalSchemaDiff
    /// The schema migrated but the data transfer (rows source → sink) failed.
    | DataTransferFailed of ValidationError list
    /// 6.A.13 — the migration would emit schema DDL against a CDC-tracked
    /// database and `allowCdc` was not set. An UNCHANGED schema emits zero
    /// DDL, so this never fires for an idempotent redeploy (engine-level
    /// CDC-silence); it guards only a *real* schema change that would churn
    /// CDC. Carries the tracked `[schema].[table]` names. Mirrors the
    /// transfer-side `transfer.cdcTrackedSink` gate.
    | RefusedByCdc of trackedTables: string list

/// The result of a **live** `migrate A B` execution: the plan + artifacts it
/// ran, the schema read back from the database after execution (`B'`), and the
/// verdict — `Verified` iff `B'` reproduces the target `B` at the physical
/// level (the round-trip canary's empty `PhysicalSchema` diff). The master
/// equation T16 realized against real SQL Server, not just in-memory.
type MigrationOutcome =
    {
        Artifacts     : MigrationArtifacts
        Reconstructed : Catalog
        SchemaDiff    : PhysicalSchemaDiff
        Verified      : bool
    }

/// The result of a **live, cross-substrate** `migrate A B` with a data load:
/// the sink's schema migration to B (`Schema`) plus the row transfer from the
/// data source into the sink over B (`Transfer`). The Dev→UAT case — schema +
/// data composed into one operation.
type MigrationDataOutcome =
    {
        Schema   : MigrationOutcome
        Transfer : Transfer.TransferReport
    }

/// The failure modes of recording a migration's episode into the durable store.
type MigrationRecordError =
    | StoreError of LifecycleStoreError
    | NonMonotonic of string

/// `migrate A B` — the L3 composition (Promise 8). Orchestrates the change
/// algebra into one operation: observe the displacement (`CatalogDiff.between`,
/// 6.A.10), refuse the unsafe (drops without opt-in; non-shape facet changes),
/// project the minimum-viable schema differential (`diff → ALTER`, 6.A.12) +
/// the data-preserving renames (RefactorLog, 6.F.1), and record the run as a
/// durable `Episode` (6.H). `migrate A B = emit(B ⊖ A)` (T16, the master
/// equation), not `realize(B)` (the full rebuild).
[<RequireQualifiedAccess>]
module MigrationRun =

    let private flattenRefactorLog (artifact: ArtifactByKind<RefactorLogEntry list>) : RefactorLogEntry list =
        artifact
        |> ArtifactByKind.toMap
        |> Map.toList
        |> List.sortBy (fun (k, _) -> SsKey.rootOriginal k)
        |> List.collect snd

    /// The dry-run: plan `A → B`, emit the schema differential + the RefactorLog,
    /// and **fail loud** on (a) destructive drops without `allowDrops`, (b) the
    /// schema emitter's `Error`-severity refusals. Pure — no I/O, no write. The
    /// operator reviews the returned artifacts; only `execute`/`record` touch a
    /// substrate. The empty-displacement case yields empty artifacts (an
    /// idempotent migration — minimum-viable touches = zero).
    let preview (allowDrops: bool) (source: Catalog) (target: Catalog) : Result<MigrationArtifacts, MigrationError> =
        match Migration.plan allowDrops source target with
        | Error e -> Error (DiffFailed e)
        | Ok plan ->
            if not (Migration.isSafe plan) then
                Error (RefusedByViolations plan.Violations)
            else
                // Thread `allowDrops` so the emitter emits the destructive DDL
                // (DROP COLUMN/CONSTRAINT/INDEX/SEQUENCE + reshape DROP+recreate)
                // the operator accepted — `Migration.plan` already cleared the
                // matching violations, so without this the emitter would still
                // refuse them and `migrate --allow-drops` could not proceed.
                let schema = SchemaMigrationEmitter.emitWith allowDrops plan.Diff
                let schemaErrors = Diagnostics.entriesAt DiagnosticSeverity.Error schema
                if not (List.isEmpty schemaErrors) then
                    Error (RefusedBySchemaErrors schemaErrors)
                else
                    match RefactorLogEmitter.emit plan.Diff with
                    | Error e -> Error (EmitFailed e)
                    | Ok refactorArtifact ->
                        // 6.A.7 — surface name-derived (Synthesized) renames the
                        // SsKey-matching diff could not thread (drop + add, not a
                        // Renamed record). For a non-V2 source identity cannot be
                        // threaded across the rename without a reconciliation rule
                        // or persisted V2 SsKeys; name it, don't silently re-key.
                        let renameWarnings =
                            CatalogDiff.synthesizedRenameWarnings plan.Diff
                            |> List.map (fun w ->
                                { DiagnosticEntry.create
                                    "migrate" DiagnosticSeverity.Warning
                                    "identity.synthesizedRenameUnstable"
                                    "A name-derived (Synthesized) identity appears renamed but the diff classifies it as a drop + add, not a rename — identity is not threaded across the rename. Supply a reconciliation rule or persist V2 SsKeys on first import."
                                  with
                                    Metadata =
                                        Map.ofList
                                            [ "synthesisSource", w.SynthesisSource
                                              "sourceTable", w.SourceTable
                                              "targetTable", w.TargetTable ] })
                        Ok
                            { Plan = plan
                              SchemaStatements = schema.Value
                              SchemaDiagnostics = schema.Entries @ renameWarnings
                              RefactorLog = flattenRefactorLog refactorArtifact }

    /// Record a completed migration's episode onto the timeline persisted at
    /// `path` (the durable substrate, 6.H.2). On the first migration of a
    /// timeline (no file yet) it opens at genesis; otherwise it loads the prior
    /// chain and appends. The migration's **target** becomes the new schema
    /// plane; the realized data movement (`DataObservation`) + refactorlog
    /// reference are supplied by the caller once the write lands. Fail-closed:
    /// a malformed store is a `StoreError`; a non-advancing version is
    /// `NonMonotonic` (the timeline never reorders).
    let record
        (path: string)
        (timeline: Timeline)
        (coordinate: EpisodeCoordinate)
        (refactorLogRef: string option)
        (data: DataObservation)
        (artifacts: MigrationArtifacts)
        : Result<EpisodicLifecycle, MigrationRecordError> =
        let episode = Migration.toEpisode coordinate refactorLogRef data artifacts.Plan
        let chainResult : Result<EpisodicLifecycle, MigrationRecordError> =
            if System.IO.File.Exists path then
                match LifecycleStore.load path with
                | Error e -> Error (StoreError e)
                | Ok existing ->
                    match EpisodicLifecycle.append episode existing with
                    | Ok chain -> Ok chain
                    | Error errs -> Error (NonMonotonic (errs |> List.map (fun e -> e.Message) |> String.concat "; "))
            else
                Ok (EpisodicLifecycle.genesis timeline episode)
        match chainResult with
        | Error e -> Error e
        | Ok chain ->
            match LifecycleStore.save path chain with
            | Ok () -> Ok chain
            | Error e -> Error (StoreError e)

    // -- the live-execute leg (direct execution against a deployed DB) --------

    /// Executable rename statements for the renames in a displacement — both
    /// **kind** renames (table; `Renamed`) and **column** renames
    /// (`AttributeDiff.Renamed`). This is the *direct-execution* rename channel —
    /// the data-preserving, metadata-only counterpart to the declarative
    /// `.refactorlog` (which drives the rename at DacFx publish, 6.F.1). Each
    /// rename emits, in order:
    ///   1. `sp_rename` of the physical object — **only if** the physical name
    ///      changed (a logical-only rename leaves the object in place);
    ///   2. `sp_updateextendedproperty` re-binding `V2.LogicalName` to the new
    ///      `Name` on the (post-rename) object — **always**, because `sp_rename`
    ///      renames the *object* but leaves the logical-name extended property
    ///      (V2's A1 identity anchor) pointing at the old name. (Surfaced by the
    ///      live A→B canary's PhysicalSchema diff.)
    /// **Table renames precede column renames**, and both precede the ALTERs, so
    /// every later statement references the post-rename physical names.
    let renameStatements (diff: CatalogDiff) : string list =
        let byKey (c: Catalog) = Catalog.allKinds c |> List.map (fun k -> k.SsKey, k) |> Map.ofList
        let src = byKey (CatalogDiff.source diff)
        let tgt = byKey (CatalogDiff.target diff)
        // LINT-ALLOW (whole function): `sp_rename` / `sp_updateextendedproperty`
        // are system-procedure calls at a terminal text boundary; ScriptDom has
        // no first-class node for them, and every interpolated value is a
        // validated `TableId` component / `ColumnName` / `Name` (single-quotes
        // doubled), not free operator text. Per the LINT-ALLOW substantive-
        // rationale discipline.
        let esc (s: string) : string = s.Replace("'", "''")
        let colOf (k: Kind) (attrKey: SsKey) : string option =
            k.Attributes |> List.tryFind (fun a -> a.SsKey = attrKey) |> Option.map (fun a -> ColumnRealization.columnNameText a.Column)
        let nameOf (k: Kind) (attrKey: SsKey) : Name option =
            k.Attributes |> List.tryFind (fun a -> a.SsKey = attrKey) |> Option.map (fun a -> a.Name)

        // (1) Table renames — same SsKey, changed kind Name.
        let tableRenames =
            CatalogDiff.renamed diff
            |> Map.toList
            |> List.sortBy (fun (k, _) -> SsKey.rootOriginal k)
            |> List.collect (fun (key, _) ->
                match Map.tryFind key src, Map.tryFind key tgt with
                | Some s, Some t ->
                    let schema = TableId.schemaText t.Physical
                    let newTable = TableId.tableText t.Physical
                    let srcSchema = TableId.schemaText s.Physical
                    let srcTable = TableId.tableText s.Physical
                    let spRename =
                        if srcTable <> newTable then
                            [ sprintf "EXEC sp_rename '%s.%s', '%s';" (esc srcSchema) (esc srcTable) (esc newTable) ]
                        else []
                    let reBind =
                        sprintf
                            "EXEC sys.sp_updateextendedproperty @name=N'V2.LogicalName', @value=N'%s', @level0type=N'SCHEMA', @level0name=N'%s', @level1type=N'TABLE', @level1name=N'%s';"
                            (esc (Name.value t.Name)) (esc schema) (esc newTable)
                    spRename @ [ reBind ]
                | _ -> [])

        // (2) Column renames — same attribute SsKey, changed attribute Name —
        // referenced against the post-(table-)rename physical table name.
        let columnRenames =
            CatalogDiff.attributeDiffs diff
            |> Map.toList
            |> List.sortBy (fun (k, _) -> SsKey.rootOriginal k)
            |> List.collect (fun (kindKey, ad) ->
                match Map.tryFind kindKey src, Map.tryFind kindKey tgt with
                | Some sKind, Some tKind ->
                    let schema = TableId.schemaText tKind.Physical
                    let table = TableId.tableText tKind.Physical
                    ad.Renamed
                    |> Map.toList
                    |> List.sortBy (fun (ak, _) -> SsKey.rootOriginal ak)
                    |> List.collect (fun (attrKey, _) ->
                        match colOf sKind attrKey, colOf tKind attrKey, nameOf tKind attrKey with
                        | Some oldCol, Some newCol, Some newName ->
                            let spRename =
                                if oldCol <> newCol then
                                    [ sprintf "EXEC sp_rename '%s.%s.%s', '%s', 'COLUMN';" (esc schema) (esc table) (esc oldCol) (esc newCol) ]
                                else []
                            let reBind =
                                sprintf
                                    "EXEC sys.sp_updateextendedproperty @name=N'V2.LogicalName', @value=N'%s', @level0type=N'SCHEMA', @level0name=N'%s', @level1type=N'TABLE', @level1name=N'%s', @level2type=N'COLUMN', @level2name=N'%s';"
                                    (esc (Name.value newName)) (esc schema) (esc table) (esc newCol)
                            spRename @ [ reBind ]
                        | _ -> [])
                | _ -> [])

        tableRenames @ columnRenames

    /// **Live `migrate A B`** — the L3 bullseye realized against a deployed
    /// database (the master equation T16 on real SQL Server). `cnn` is the
    /// database at state **A**; `source` is the *known* prior schema (the prior
    /// recorded `Episode`, or the authored A — not re-read, so the displacement
    /// is noise-free); `target` is the desired **B**. Steps: plan + refuse
    /// fail-loud **before any write** (drops / non-shape facets); execute the
    /// minimum-viable differential — renames (`sp_rename`) then the `ALTER`
    /// differential (never a re-CREATE) — in physical order; read **B'** back
    /// (`ReadSide.read`) and verify it reproduces **B** at the `PhysicalSchema`
    /// level. Idempotent + resumable **by construction**: re-running re-diffs the
    /// now-current state against B, so a partial run completes on re-run and a
    /// fully-migrated DB is a no-op (empty differential).
    let execute
        (allowCdc: bool)
        (allowDrops: bool)
        (source: Catalog)
        (target: Catalog)
        (cnn: SqlConnection)
        : System.Threading.Tasks.Task<Result<MigrationOutcome, MigrationError>> =
        task {
            match preview allowDrops source target with
            | Error e -> return Error e
            | Ok artifacts ->
                let renameSql = renameStatements artifacts.Plan.Diff
                let alterSql = artifacts.SchemaStatements |> Render.toText
                // 6.A.13 — schema-side CDC pre-flight. Only a run that would
                // emit DDL is at risk; an UNCHANGED schema (zero renames + no
                // ALTER) skips the check entirely — that is engine-level
                // CDC-silence (an idempotent redeploy churns no CDC). When
                // there IS DDL and the DB is CDC-tracked, refuse unless
                // `allowCdc` (mirrors the transfer-side gate).
                let hasDdl =
                    not (List.isEmpty renameSql)
                    || not (System.String.IsNullOrWhiteSpace alterSql)
                let! cdcGate =
                    task {
                        if hasDdl && not allowCdc then
                            let! tracked = ReadSide.cdcTrackedTables cnn
                            if List.isEmpty tracked then return Ok ()
                            else return Error (RefusedByCdc tracked)
                        else return Ok ()
                    }
                match cdcGate with
                | Error e -> return Error e
                | Ok () ->
                let! executed =
                    task {
                        try
                            for stmt in renameSql do
                                do! Deploy.executeBatch cnn stmt
                            if not (System.String.IsNullOrWhiteSpace alterSql) then
                                do! Deploy.executeBatch cnn alterSql
                            return Ok ()
                        with ex -> return Error (ExecutionFailed ex.Message)
                    }
                match executed with
                | Error e -> return Error e
                | Ok () ->
                    let! readBack = ReadSide.read cnn
                    match readBack with
                    | Error es -> return Error (SchemaReadFailed es)
                    | Ok reconstructed ->
                        let sdiff =
                            PhysicalSchema.diff
                                (PhysicalSchema.ofCatalog target)
                                (PhysicalSchema.ofCatalog reconstructed)
                        return
                            Ok
                                { Artifacts = artifacts
                                  Reconstructed = reconstructed
                                  SchemaDiff = sdiff
                                  // Schema-structural verification: B' reproduces B's
                                  // structure; the rows it carries are the preserved data
                                  // (data preservation is asserted separately by the canary).
                                  Verified = PhysicalSchema.isSchemaEqual sdiff }
        }

    /// `execute` for the bootstrap case where the prior schema is **not** known
    /// from a recorded episode: read **A** from the live database first
    /// (`ReadSide.read cnn`), then plan + execute against `target`. Carries the
    /// `ReadSide`-fidelity caveat — the diff is computed from the *reconstructed*
    /// A, so prefer `execute` with the recorded-episode schema when available.
    let executeFromLive
        (allowCdc: bool)
        (allowDrops: bool)
        (target: Catalog)
        (cnn: SqlConnection)
        : System.Threading.Tasks.Task<Result<MigrationOutcome, MigrationError>> =
        task {
            let! readA = ReadSide.read cnn
            match readA with
            | Error es -> return Error (SchemaReadFailed es)
            | Ok source -> return! execute allowCdc allowDrops source target cnn
        }

    /// **`migrate A B` with a data load** — the cross-substrate composition (the
    /// premise's Dev→UAT case: "produce a full-export whose users have been
    /// rekeyed"). Evolves the **sink**'s schema in place from `sinkSource` (its
    /// known prior schema A) to `target` B, then transfers rows from the
    /// `dataSource` substrate into the sink over the agreed contract B —
    /// reconciling per `reconciliation` (the User re-key; empty = a straight
    /// load). Schema is minimum-viable + fail-loud; data is the existing
    /// `Transfer` engine. The data leg runs **only if** the schema leg verified
    /// (never load into an unverified target). Both substrates end at B; the data
    /// source's rows must match the contract B.
    let executeWithData
        (allowDrops: bool)
        (mode: Transfer.Mode)
        (allowCdc: bool)
        (sinkSource: Catalog)
        (target: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        (dataSource: SqlConnection)
        (sink: SqlConnection)
        : System.Threading.Tasks.Task<Result<MigrationDataOutcome, MigrationError>> =
        task {
            let! schemaResult = execute allowCdc allowDrops sinkSource target sink
            match schemaResult with
            | Error e -> return Error e
            | Ok schema ->
                if not schema.Verified then
                    return Error (VerificationFailed schema.SchemaDiff)
                else
                    let! transferResult =
                        if Map.isEmpty reconciliation then
                            Transfer.run mode allowCdc dataSource sink target
                        else
                            Transfer.runReconciling mode allowCdc dataSource sink target reconciliation
                    match transferResult with
                    | Ok report -> return Ok { Schema = schema; Transfer = report }
                    | Error es -> return Error (DataTransferFailed es)
        }
