namespace Projection.Pipeline

// LINT-ALLOW-FILE: terminal SQL/diagnostic text composition at the migration-run boundary;
//   segments are typed and the run output is immutable. `String.concat` is the
//   BCL primitive at this terminal boundary.

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
    /// The plan carries **undeclared** destructive removals — the declared-loss
    /// gate refused (the operator has not accepted these losses by `lossId`).
    | RefusedByViolations of SchemaLoss list
    /// The schema emitter raised `Error`-severity refusals (a non-shape facet
    /// change it cannot express as a single `ALTER`).
    | RefusedBySchemaErrors of DiagnosticEntry list
    /// The RefactorLog emitter failed to project the rename channel.
    | EmitFailed of EmitError
    /// Reading the source/target schema back from the live database failed.
    | SchemaReadFailed of ValidationError list
    /// Executing the migration SQL against the live database raised.
    | ExecutionFailed of message: string
    /// G9 (NEITHER→HELD) — the in-place differential tightens a column to NOT
    /// NULL but the live source data carries NULL rows. Refused by the
    /// `Preflight.tighteningPreflight` probe (`migrate.dataViolatesTightening`)
    /// **before** the `ALTER COLUMN … NOT NULL` is submitted — the named
    /// pre-flight refusal, NOT the post-facto `ExecutionFailed` the bare ALTER
    /// would have surfaced. No DDL runs; the column stays nullable.
    | RefusedByTightening of message: string
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
    /// The durable provenance store (the prior-emission snapshot, state A) could
    /// not be loaded — a malformed/corrupt `LifecycleStore`. Fail-closed: a
    /// snapshot⊖snapshot plan never proceeds on an unreadable prior.
    | StoreReadFailed of message: string

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
    let preview (declaration: LossDeclaration) (source: Catalog) (target: Catalog) : Result<MigrationArtifacts, MigrationError> =
        match Migration.plan declaration source target with
        | Error e -> Error (DiffFailed e)
        | Ok plan ->
            if not (Migration.isSafe plan) then
                Error (RefusedByViolations plan.Violations)
            else
                // The declared-loss gate has cleared every *undeclared* loss (the
                // plan is safe), so the destructive removals that remain are all
                // sanctioned — `permitsDrops` tells the imperative emitter to emit
                // the DROP DDL (DROP COLUMN/CONSTRAINT/INDEX/SEQUENCE + reshape
                // DROP+recreate). With `DeclareNone` there are no drops to emit.
                let schema = SchemaMigrationEmitter.emitWith (LossDeclaration.permitsDrops declaration) plan.Diff
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

    /// **The snapshot⊖snapshot preview** — `migrate → target` against the *prior
    /// emission's* schema, read from the durable `LifecycleStore` (6.H), not from
    /// a live read-back or a second authored model. State A is
    /// `reconstructLatestSchema` over the persisted chain (the FTC fold —
    /// witnessed durable in `LifecycleStoreTests`); the displacement is `B ⊖ A`
    /// for B = `target`. This closes the emission→snapshot→diff loop the
    /// morphology flagged as latent: each emission persists a snapshot (`record`),
    /// and the next reads it back here as the comparison basis. A **missing store
    /// is genesis** — A = ∅, so every kind is `Add` and there are no losses (the
    /// first emission of a timeline). Fail-closed on a malformed store.
    let previewFromStoreForcing
        (forceGenesis: bool)
        (path: string)
        (declaration: LossDeclaration)
        (target: Catalog)
        : Result<MigrationArtifacts, MigrationError> =
        let priorSchema : Result<Catalog, MigrationError> =
            if (not forceGenesis) && System.IO.File.Exists path then
                match LifecycleStore.load path with
                | Error e -> Error (StoreReadFailed (string e))
                | Ok chain ->
                    match EpisodicLifecycle.reconstructLatestSchema chain with
                    | Ok a -> Ok a
                    | Error e -> Error (DiffFailed e)
            else
                // Genesis: no prior emission. A = ∅ (all Add, no Remove).
                match Catalog.create [] [] with
                | Ok empty -> Ok empty
                | Error es -> Error (StoreReadFailed (es |> List.map (fun e -> e.Message) |> String.concat "; "))
        match priorSchema with
        | Error e -> Error e
        | Ok source -> preview declaration source target

    /// The store-derived preview with genesis only on an absent store (the
    /// default — every prior caller's behavior). `previewFromStoreForcing false`
    /// is identical; this is the named no-force form.
    let previewFromStore
        (path: string)
        (declaration: LossDeclaration)
        (target: Catalog)
        : Result<MigrationArtifacts, MigrationError> =
        previewFromStoreForcing false path declaration target

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

    /// The next monotonic `EpisodeCoordinate` for the timeline persisted at
    /// `path`: ordinal 0 for a genesis (no file / unreadable-as-genesis), else
    /// the latest episode's ordinal + 1. The CLI supplies `environment` + `at`
    /// (Core holds no clock); the label is the ordinal's SemVer-ish stamp. This
    /// is the seam that lets the executor record without the operator hand-
    /// authoring a version — the timeline's own head dictates the next ordinal.
    let nextCoordinate
        (path: string)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Result<EpisodeCoordinate, MigrationRecordError> =
        let ordinalResult : Result<int, MigrationRecordError> =
            if System.IO.File.Exists path then
                match LifecycleStore.load path with
                | Error e -> Error (StoreError e)
                | Ok existing ->
                    Ok (Version.ordinal (Episode.version (EpisodicLifecycle.latest existing)) + 1)
            else
                Ok 0
        match ordinalResult with
        | Error e -> Error e
        | Ok ordinal ->
            match Version.create ordinal (sprintf "v%d" ordinal) with
            | Ok version -> Ok (EpisodeCoordinate.create version environment at)
            | Error errs -> Error (NonMonotonic (errs |> List.map (fun e -> e.Message) |> String.concat "; "))

    /// **The record-leg of the composed CLI execute** — persist a *verified*
    /// migration `outcome`'s episode onto the timeline at `path`, deriving the
    /// next monotonic coordinate from the store itself (`nextCoordinate`). Pure
    /// w.r.t. the DB (it takes the already-computed `MigrationOutcome`, no
    /// `SqlConnection`), so it is unit-testable: after it runs against a store
    /// path, the store reloads and `reconstructLatestSchema` reproduces B. An
    /// **unverified** outcome is never recorded — the timeline only carries
    /// episodes whose B' reproduced B. `timeline` opens the store at genesis on
    /// the first record; thereafter it loads-and-appends.
    let recordVerified
        (path: string)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        (refactorLogRef: string option)
        (data: DataObservation)
        (outcome: MigrationOutcome)
        : Result<EpisodicLifecycle, MigrationRecordError> =
        // L3 — the episode grain's WriteAdmit (R3 / RI-3): the B'≡B
        // round-trip is the external witness, checked HERE — the one moment
        // it is checkable — and the `Verified<_>` token carries that the
        // witness held. The grain's ResumeAdmit is ordinal monotonicity
        // (`EpisodicLifecycle.append`, re-run at load by the store's
        // `buildLifecycle`) — named as such, honestly: the store cannot
        // re-verify B'≡B at load, because no B' exists to re-deploy.
        let admitted =
            Ledger.writeAdmit
                (fun (o: MigrationOutcome) ->
                    if o.Verified then Ok ()
                    else Error (NonMonotonic "refusing to record an unverified migration outcome (B' did not reproduce B)"))
                outcome
        match admitted with
        | Error e -> Error e
        | Ok token ->
            match nextCoordinate path environment at with
            | Error e -> Error e
            | Ok coordinate -> record path timeline coordinate refactorLogRef data (Verified.value token).Artifacts

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
                            // WP5 / C1 — rebind the renamed identity property
                            // (`Projection.LogicalName`). Legacy `V2.*`-bearing
                            // deployed schemas are the dual-window migrate edge
                            // (Docker-gated; named in DECISIONS).
                            "EXEC sys.sp_updateextendedproperty @name=N'Projection.LogicalName', @value=N'%s', @level0type=N'SCHEMA', @level0name=N'%s', @level1type=N'TABLE', @level1name=N'%s';"
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
                                    // WP5 / C1 — rebind the renamed identity
                                    // property (`Projection.LogicalName`).
                                    "EXEC sys.sp_updateextendedproperty @name=N'Projection.LogicalName', @value=N'%s', @level0type=N'SCHEMA', @level0name=N'%s', @level1type=N'TABLE', @level1name=N'%s', @level2type=N'COLUMN', @level2name=N'%s';"
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
    /// The migrate engine's safety gates — the declared `preflight` stage's
    /// body (card S4b: real SQL I/O, inside the meter).
    ///
    /// 6.A.13 — schema-side CDC pre-flight. Only a run that would emit DDL
    /// is at risk; an UNCHANGED schema (zero renames + no ALTER) skips the
    /// check entirely — that is engine-level CDC-silence (an idempotent
    /// redeploy churns no CDC). When there IS DDL and the DB is
    /// CDC-tracked, refuse unless `allowCdc` (mirrors the transfer-side
    /// gate).
    ///
    /// G9 (NEITHER→HELD) — the NOT-NULL tightening pre-flight. The
    /// in-place `ALTER COLUMN … NOT NULL` against a column that still
    /// carries NULL rows is REFUSED *before* the ALTER is submitted,
    /// not caught post-facto as `ExecutionFailed`. This is the
    /// DATA-aware last line *after* the schema-blind narrowing
    /// declared-loss gate (G8): even an operator who has DECLARED the
    /// narrowing loss cannot apply NOT NULL while NULL rows remain —
    /// the data physically blocks it. The overlay (Track-F's
    /// `Preflight.tighteningOverlay`) names every nullable→NOT NULL
    /// column the A→B displacement tightens; the probe counts live
    /// NULLs on the SOURCE schema (the DB is at A, where the column is
    /// still nullable). A clean source (or no tightening at all)
    /// passes; a NULL-bearing tightening refuses with
    /// `migrate.dataViolatesTightening` — the column stays nullable,
    /// no DDL runs.
    let private safetyGates
        (allowCdc: bool)
        (hasDdl: bool)
        (source: Catalog)
        (target: Catalog)
        (cnn: SqlConnection)
        : System.Threading.Tasks.Task<Result<unit, MigrationError>> =
        task {
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
                let overlay = Preflight.tighteningOverlay source target
                if Set.isEmpty overlay.EnforceNotNull then return Ok ()
                else
                    match! Preflight.tighteningPreflight cnn source overlay with
                    | Ok () -> return Ok ()
                    | Error es ->
                        let msg = es |> List.map (fun e -> e.Message) |> String.concat "; "
                        return Error (RefusedByTightening msg)
        }

    let execute
        (allowCdc: bool)
        (declaration: LossDeclaration)
        (source: Catalog)
        (target: Catalog)
        (cnn: SqlConnection)
        : System.Threading.Tasks.Task<Result<MigrationOutcome, MigrationError>> =
        task {
            // Card S4b — the migrate engine rides the spine (`Spines.migrate`:
            // build → safety gates → apply → verify). The `staged { }` CE owns
            // every bracket, which closes the RI-2(a) defect by construction:
            // an error inside any stage CLOSES it on the wire (`failed` /
            // `aborted`) instead of leaving the board hanging on an open
            // `.started` — the pre-spine code returned early out of emit,
            // deploy, and canary without ever closing them. The safety gates
            // (CDC + tightening — real SQL) are now the declared `preflight`
            // stage. The FACE-level grant pre-flights (`migratePreflights` in
            // the CLI) remain outside the engine's spine — named residue for
            // S5's ε. The live stage stream (§13) rides the same NDJSON
            // channel as before; when no one is watching they are plain
            // machine events, never operator prose.
            let! verdict =
                staged Spines.migrate {
                    let! built =
                        Staged.stage Stages.emit (fun () ->
                            // The change build: the plan plus the rendered DDL
                            // text (rendering is emit work — attributed here).
                            match preview declaration source target with
                            | Error e -> System.Threading.Tasks.Task.FromResult (Error e)
                            | Ok artifacts ->
                                let renameSql = renameStatements artifacts.Plan.Diff
                                let alterSql = artifacts.SchemaStatements |> Render.toText
                                System.Threading.Tasks.Task.FromResult (Ok (artifacts, renameSql, alterSql)))
                    let artifacts, renameSql, alterSql = built
                    let hasDdl =
                        not (List.isEmpty renameSql)
                        || not (System.String.IsNullOrWhiteSpace alterSql)
                    let! _ =
                        Staged.stage Stages.preflight (fun () ->
                            safetyGates allowCdc hasDdl source target cnn)
                    let! _ =
                        Staged.stage Stages.deploy (fun () ->
                            task {
                                let sw = System.Diagnostics.Stopwatch.StartNew()
                                let hasAlter = not (System.String.IsNullOrWhiteSpace alterSql)
                                let totalWrites = List.length renameSql + (if hasAlter then 1 else 0)
                                try
                                    let mutable applied = 0
                                    for stmt in renameSql do
                                        do! Deploy.executeBatch cnn stmt
                                        applied <- applied + 1
                                        LogSink.recordStageProgress "deploy" applied totalWrites sw.ElapsedMilliseconds
                                    if hasAlter then
                                        do! Deploy.executeBatch cnn alterSql
                                        applied <- applied + 1
                                        LogSink.recordStageProgress "deploy" applied totalWrites sw.ElapsedMilliseconds
                                    return Ok ()
                                with ex -> return Error (ExecutionFailed ex.Message)
                            })
                    let! outcome =
                        Staged.stage Stages.canary (fun () ->
                            task {
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
                            })
                    return outcome
                }
            return
                match verdict.Disposition with
                | RunCompleted outcome -> Ok outcome
                | RunStopped e -> Error e
                | RunAborted (_, Some ex) ->
                    // The spine closed the books (the open stage closed
                    // `aborted` on the wire); the engine's crash semantics
                    // are preserved for the caller.
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw()
                    Unchecked.defaultof<_>
                | RunAborted (refusal, None) -> failwith refusal
        }

    /// **X4 — the in-place migrate's CDC-measure leg.** The criterion
    /// ("redeploy an unchanged model: zero ALTERs AND zero CDC captures, BOTH
    /// measured") asks the engine to *measure* CDC-silence, not merely assert
    /// that no DDL ran. Brackets `execute` with the change-measure ‖·‖
    /// (`Deploy.cdcCaptureTotal`): baseline before, post after; the returned
    /// delta is the captures the migrate produced. An idempotent redeploy
    /// (empty differential, no DML) yields `(outcome, 0)` — both legs of the
    /// criterion measured; the meter is proven live by any interleaved DML
    /// showing nonzero. Additive — `execute` (and its G9 gate) is untouched.
    let executeAndMeasureCdc
        (allowCdc: bool)
        (declaration: LossDeclaration)
        (source: Catalog)
        (target: Catalog)
        (cnn: SqlConnection)
        : System.Threading.Tasks.Task<Result<MigrationOutcome * int, MigrationError>> =
        task {
            let! baseline = Deploy.cdcCaptureTotal cnn
            let! result = execute allowCdc declaration source target cnn
            match result with
            | Error e -> return Error e
            | Ok outcome ->
                let! post = Deploy.cdcCaptureTotal cnn
                return Ok (outcome, post - baseline)
        }

    /// **The composed live execute → record** — the L3 CLI bullseye made
    /// durable (AC-P8). Runs `execute` against the deployed DB; on a **verified**
    /// outcome, persists the episode onto the timeline at `path` via
    /// `recordVerified` (next monotonic coordinate derived from the store). The
    /// returned `chain` is `None` when the outcome did not verify (nothing
    /// recorded — the timeline only carries episodes whose B' reproduced B). A
    /// schema-only in-place execute moves no rows, so the durable
    /// `DataObservation` is `empty` (CDC count 0); the cross-substrate data-load
    /// path records its CDC series separately. A record-leg failure surfaces as
    /// `ExecutionFailed` (the write landed but provenance did not — fail-loud so
    /// the operator knows the episode is unpersisted).
    let executeAndRecord
        (allowCdc: bool)
        (declaration: LossDeclaration)
        (source: Catalog)
        (target: Catalog)
        (path: string)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        (refactorLogRef: string option)
        (cnn: SqlConnection)
        : System.Threading.Tasks.Task<Result<MigrationOutcome * EpisodicLifecycle option, MigrationError>> =
        task {
            let! result = execute allowCdc declaration source target cnn
            match result with
            | Error e -> return Error e
            | Ok outcome ->
                if not outcome.Verified then
                    return Ok (outcome, None)
                else
                    match recordVerified path timeline environment at refactorLogRef DataObservation.empty outcome with
                    | Ok chain -> return Ok (outcome, Some chain)
                    | Error e -> return Error (ExecutionFailed (sprintf "execute verified but recording the episode failed: %A" e))
        }

    /// `execute` for the bootstrap case where the prior schema is **not** known
    /// from a recorded episode: read **A** from the live database first
    /// (`ReadSide.read cnn`), then plan + execute against `target`. Carries the
    /// `ReadSide`-fidelity caveat — the diff is computed from the *reconstructed*
    /// A, so prefer `execute` with the recorded-episode schema when available.
    let executeFromLive
        (allowCdc: bool)
        (declaration: LossDeclaration)
        (target: Catalog)
        (cnn: SqlConnection)
        : System.Threading.Tasks.Task<Result<MigrationOutcome, MigrationError>> =
        task {
            let! readA = ReadSide.read cnn
            match readA with
            | Error es -> return Error (SchemaReadFailed es)
            | Ok source -> return! execute allowCdc declaration source target cnn
        }

    /// **`migrate A B` with a data load** — the cross-substrate composition (the
    /// premise's Dev→UAT case: "produce a full-export whose users have been
    /// rekeyed"). Evolves the **sink**'s schema in place from `sinkSource` (its
    /// known prior schema A) to `target` B, then transfers rows from the
    /// `dataSource` substrate into the sink over the agreed contract B —
    /// reconciling per `reconciliation` (the User re-key; empty = a straight
    /// load). Schema is minimum-viable + fail-loud; data is the existing
    /// `Transfer` engine. The data leg runs **only if** the schema leg verified
    /// (never load into an unverified target).
    ///
    /// **A5 — the data source is at schema A.** In the established Dev→UAT
    /// migrate-with-data flow the `dataSource` holds rows at the OLD schema A
    /// (`sinkSource`), not already at B. The data leg therefore routes through
    /// the rename-aware Transfer (`runWithRenames` / `runReconcilingWithRenames`)
    /// with `sourceContract = sinkSource` (A) and `sinkContract = target` (B), so
    /// rows are re-pointed onto the B names via the A→B `CatalogDiff` renames
    /// (identity-matched, never ordinal) before the write. A no-renames diff
    /// yields an empty rename map ⇒ identity repoint ⇒ byte-identical to the
    /// straight load.
    let executeWithData
        (declaration: LossDeclaration)
        (mode: Transfer.Mode)
        (allowCdc: bool)
        (sinkSource: Catalog)
        (target: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        (dataSource: SqlConnection)
        (sink: SqlConnection)
        : System.Threading.Tasks.Task<Result<MigrationDataOutcome, MigrationError>> =
        task {
            let! schemaResult = execute allowCdc declaration sinkSource target sink
            match schemaResult with
            | Error e -> return Error e
            | Ok schema ->
                if not schema.Verified then
                    return Error (VerificationFailed schema.SchemaDiff)
                else
                    let! transferResult =
                        // A5 — the data source is at A (`sinkSource`); re-point
                        // rows A→B through the rename-aware Transfer. The renameMap
                        // derives from `CatalogDiff.between sinkSource target`; a
                        // no-renames diff repoints by identity (== the straight load).
                        if Map.isEmpty reconciliation then
                            Transfer.runWithRenames mode allowCdc dataSource sink sinkSource target
                        else
                            // allowDrops = false: enforce the AC-I5 pre-write validate-user-map
                            // halt on the reconciling migrate-with-data path (the reconcile+migrate
                            // composition, AC-I7, is the follow-on; --allow-drops flows here then).
                            Transfer.runReconcilingWithRenames mode allowCdc dataSource sink sinkSource target reconciliation
                    match transferResult with
                    | Ok report -> return Ok { Schema = schema; Transfer = report }
                    | Error es -> return Error (DataTransferFailed es)
        }

    /// **X5 — the in-place migrate-with-data, MEASURED and RECORDED.** The
    /// protein P-6 chain is `migrate-schema → Move-data → Measure-CDC →
    /// Record`. `executeWithData` covers the first two; this composes the last
    /// two: it brackets the data transfer with the change-measure ‖·‖
    /// (`Deploy.cdcCaptureTotal`, the production reader) and persists an episode
    /// whose `DataObservation` carries the **measured** capture count — the
    /// durable record of how much data actually moved.
    ///
    /// **Verification under CDC.** A CDC-tracked sink (the UAT the SSIS
    /// consumer reads) puts `cdc.*` objects in the read-back, so the schema
    /// leg's `Verified` round-trip is confounded (it sees tables that aren't in
    /// B). The episode is therefore gated on the schema leg applying without
    /// error (`execute` already refuses schema-error displacements) and the
    /// transfer succeeding — not on the confounded readback flag. Recorded via
    /// `record` (coordinate from `nextCoordinate`), so the timeline carries the
    /// data-plane observation. Additive — `execute` (and its G9 gate) and
    /// `executeWithData` are untouched.
    let executeWithDataAndRecord
        (declaration: LossDeclaration)
        (mode: Transfer.Mode)
        (allowCdc: bool)
        (sinkSource: Catalog)
        (target: Catalog)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
        (path: string)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        (refactorLogRef: string option)
        (dataSource: SqlConnection)
        (sink: SqlConnection)
        : System.Threading.Tasks.Task<Result<MigrationDataOutcome * EpisodicLifecycle, MigrationError>> =
        task {
            let! schemaResult = execute allowCdc declaration sinkSource target sink
            match schemaResult with
            | Error e -> return Error e
            | Ok schema ->
                // Measure the data movement: baseline before the load, post
                // after; the delta is the CDC capture count of the transfer.
                let! baseline = Deploy.cdcCaptureTotal sink
                let! transferResult =
                    // A5 — the data source is at A (`sinkSource`); re-point rows
                    // A→B through the rename-aware Transfer (see `executeWithData`).
                    if Map.isEmpty reconciliation then
                        Transfer.runWithRenames mode allowCdc dataSource sink sinkSource target
                    else
                        Transfer.runReconcilingWithRenames mode allowCdc dataSource sink sinkSource target reconciliation
                match transferResult with
                | Error es -> return Error (DataTransferFailed es)
                | Ok report ->
                    let! post = Deploy.cdcCaptureTotal sink
                    let data = DataObservation.create (post - baseline) None
                    match nextCoordinate path environment at with
                    | Error e -> return Error (ExecutionFailed (sprintf "data load succeeded but deriving the episode coordinate failed: %A" e))
                    | Ok coordinate ->
                        match record path timeline coordinate refactorLogRef data schema.Artifacts with
                        | Ok chain -> return Ok ({ Schema = schema; Transfer = report }, chain)
                        | Error e -> return Error (ExecutionFailed (sprintf "data load succeeded but recording the episode failed: %A" e))
        }
