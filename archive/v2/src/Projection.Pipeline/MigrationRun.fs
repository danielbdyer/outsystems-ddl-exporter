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
    /// NM-54 — the CDC integrity probe itself could not run (a transient
    /// `SqlException` / `VIEW DEFINITION` denial reading `sys.tables`). The CDC
    /// state is UNVERIFIABLE, which is UNSAFE: fail-safe means REFUSE the DDL,
    /// not proceed as if no CDC. Carries the named `readside.cdcTrackedProbeFailed`
    /// message. Distinct from `RefusedByCdc` (the gate observed CDC and refused);
    /// here the gate could not observe at all.
    | RefusedByCdcUnverifiable of message: string
    /// The durable provenance store (the prior-emission snapshot, state A) could
    /// not be loaded — a malformed/corrupt `LifecycleStore`. Fail-closed: a
    /// snapshot⊖snapshot plan never proceeds on an unreadable prior.
    | StoreReadFailed of message: string
    /// M21 — the live **compensating-undo** arm (rides M12's `CatalogDiff.inverse`).
    /// A mid-deploy failure left the substrate at a partial B'' between A and B; the
    /// engine realized the inverse of the live displacement (`CatalogDiff.between
    /// B'' A`) on the rename channel and a read-back **confirms it is back at A** —
    /// no changes remain, the data is intact. `failure` is the original execution
    /// error; `renamesUndone` is the count of metadata-only renames reverted. This
    /// is the covenant's "refuses *without damage*": the migration did not complete,
    /// but it corrupted nothing. The Atomic `BEGIN TRAN` envelope (which would make
    /// this `RunStopped` impossible) stays §10-deferred (managed-login grant survey
    /// + P7b throughput); this is the buildable, J5-evidence-backed alternative.
    | ExecutionRolledBack of failure: string * renamesUndone: int
    /// M21 — a mid-deploy failure left an applied **non-rename** residual (an
    /// ALTER/ADD the engine will not auto-invert, because the inverse would be a
    /// destructive op it refuses by policy). Compensation reverted what it safely
    /// could (the rename channel); the **named residual** is the exact divergence
    /// from A that remains. The engine REFUSES to report success and names the
    /// corruption surface rather than attempting an unsafe inverse — "refuse rather
    /// than corrupt" made literal. The operator resumes (re-diff from the partial
    /// state) or repairs the named residual by hand.
    | PartialWriteUnrecovered of failure: string * residual: PhysicalSchemaDiff

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
                                            [ "synthesisSource", RenameSynthesisSource.text w.SynthesisSource
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
    /// PL-10 (S12) — the ONE durable read a record leg pays; `None` is
    /// genesis (no file). The coordinate + append consumers below thread
    /// the loaded chain instead of re-parsing the store.
    let private loadChain (path: string) : Result<EpisodicLifecycle option, MigrationRecordError> =
        if System.IO.File.Exists path then
            match LifecycleStore.load path with
            | Error e -> Error (StoreError e)
            | Ok existing -> Ok (Some existing)
        else Ok None

    let private recordOnChain
        (path: string)
        (timeline: Timeline)
        (chain: EpisodicLifecycle option)
        (coordinate: EpisodeCoordinate)
        (refactorLogRef: string option)
        (data: DataObservation)
        (artifacts: MigrationArtifacts)
        : Result<EpisodicLifecycle, MigrationRecordError> =
        let episode = Migration.toEpisode coordinate refactorLogRef data artifacts.Plan
        let chainResult : Result<EpisodicLifecycle, MigrationRecordError> =
            match chain with
            | Some existing ->
                match EpisodicLifecycle.append episode existing with
                | Ok appended -> Ok appended
                | Error errs -> Error (NonMonotonic (errs |> List.map (fun e -> e.Message) |> String.concat "; "))
            | None ->
                Ok (EpisodicLifecycle.genesis timeline episode)
        match chainResult with
        | Error e -> Error e
        | Ok appended ->
            match LifecycleStore.save path appended with
            | Ok () -> Ok appended
            | Error e -> Error (StoreError e)

    let record
        (path: string)
        (timeline: Timeline)
        (coordinate: EpisodeCoordinate)
        (refactorLogRef: string option)
        (data: DataObservation)
        (artifacts: MigrationArtifacts)
        : Result<EpisodicLifecycle, MigrationRecordError> =
        match loadChain path with
        | Error e -> Error e
        | Ok chain -> recordOnChain path timeline chain coordinate refactorLogRef data artifacts

    /// The next monotonic `EpisodeCoordinate` for the timeline persisted at
    /// `path`: ordinal 0 for a genesis (no file / unreadable-as-genesis), else
    /// the latest episode's ordinal + 1. The CLI supplies `environment` + `at`
    /// (Core holds no clock); the label is the ordinal's SemVer-ish stamp. This
    /// is the seam that lets the executor record without the operator hand-
    /// authoring a version — the timeline's own head dictates the next ordinal.
    let private nextCoordinateOfChain
        (chain: EpisodicLifecycle option)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Result<EpisodeCoordinate, MigrationRecordError> =
        let ordinal =
            match chain with
            | Some existing -> Version.ordinal (Episode.version (EpisodicLifecycle.latest existing)) + 1
            | None -> 0
        match Version.create ordinal (sprintf "v%d" ordinal) with
        | Ok version -> Ok (EpisodeCoordinate.create version environment at)
        | Error errs -> Error (NonMonotonic (errs |> List.map (fun e -> e.Message) |> String.concat "; "))

    let nextCoordinate
        (path: string)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Result<EpisodeCoordinate, MigrationRecordError> =
        match loadChain path with
        | Error e -> Error e
        | Ok chain -> nextCoordinateOfChain chain environment at

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
            // PL-10 (S12) — ONE store load serves both the coordinate
            // derivation and the append (was two full parses per record leg).
            match loadChain path with
            | Error e -> Error e
            | Ok chain ->
                match nextCoordinateOfChain chain environment at with
                | Error e -> Error e
                | Ok coordinate -> recordOnChain path timeline chain coordinate refactorLogRef data (Verified.value token).Artifacts

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
        let byKey (c: Catalog) = Catalog.kindIndex c
        let src = byKey (CatalogDiff.source diff)
        let tgt = byKey (CatalogDiff.target diff)
        // LINT-ALLOW (the `sp_rename` calls only): `sp_rename` is a
        // system-procedure call at a terminal text boundary; ScriptDom has no
        // first-class node for it, and every interpolated value is a validated
        // `TableId` component / `ColumnName` (single-quotes doubled), not free
        // operator text. The logical-name RE-BIND below is now fully typed
        // (`ScriptDomBuild.buildUpdateExtendedProperty` — the same node the SSDT
        // emitter uses for `sp_addextendedproperty`), so it carries no escaper.
        let esc (s: string) : string = s.Replace("'", "''")
        let colOf (k: Kind) (attrKey: SsKey) : string option =
            k.Attributes |> List.tryFind (fun a -> a.SsKey = attrKey) |> Option.map (fun a -> ColumnRealization.columnNameText a.Column)
        let nameOf (k: Kind) (attrKey: SsKey) : Name option =
            k.Attributes |> List.tryFind (fun a -> a.SsKey = attrKey) |> Option.map (fun a -> a.Name)
        // The logical-name re-bind, rendered from the typed `sp_updateextendedproperty`
        // node; `generateOne` omits the trailing `;`, appended for the batch contract.
        let reBindStmt (owner: ExtendedPropertyOwner) (logicalName: string) : string =
            System.String.Concat(
                ScriptDomGenerate.generateOne
                    ((ScriptDomBuild.buildUpdateExtendedProperty owner "Projection.LogicalName" (Some logicalName)).Value
                        :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement),
                ";")

        // (1) Table renames — same SsKey, changed kind Name.
        let tableRenames =
            CatalogDiff.renamed diff
            |> Map.toList
            |> List.sortBy (fun (k, _) -> SsKey.rootOriginal k)
            |> List.collect (fun (key, _) ->
                match Map.tryFind key src, Map.tryFind key tgt with
                | Some s, Some t ->
                    let newTable = TableId.tableText t.Physical
                    let srcSchema = TableId.schemaText s.Physical
                    let srcTable = TableId.tableText s.Physical
                    let spRename =
                        if srcTable <> newTable then
                            [ sprintf "EXEC sp_rename '%s.%s', '%s';" (esc srcSchema) (esc srcTable) (esc newTable) ]
                        else []
                    // WP5 / C1 — rebind the renamed identity property
                    // (`Projection.LogicalName`) on the post-rename table.
                    let reBind = reBindStmt (TableProperty t.Physical) (Name.value t.Name)
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
                            // WP5 / C1 — rebind the renamed identity property
                            // (`Projection.LogicalName`) on the post-rename column.
                            let reBind = reBindStmt (ColumnProperty (tKind.Physical, newCol)) (Name.value newName)
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
    /// **M21 — the live compensating-undo arm (rides M12's groupoid inverse).**
    /// On a deploy-stage failure the substrate sits at a *partial* B'' somewhere
    /// between A and B. This realizes the inverse of the live displacement —
    /// `CatalogDiff.between B'' A`, which is exactly M12's `inverse (between A B'')`
    /// — on the **rename channel only** (the metadata-only, data-preserving,
    /// always-invertible moves: `sp_rename` + the logical-name re-bind). The ALTER
    /// channel is deliberately NOT auto-inverted: the inverse of an applied widen /
    /// add is a narrow / drop, a destructive op the engine refuses by policy, so
    /// inverting it here would *compound* the damage. Instead we read back, diff
    /// against A, and return one of two HONEST verdicts — never a partial silently
    /// claimed as success:
    ///   - `ExecutionRolledBack` — the read-back confirms the substrate is back at A.
    ///   - `PartialWriteUnrecovered residual` — a non-rename residual remains; the
    ///     exact divergence from A is named for the operator.
    /// Best-effort per compensating statement: the read-back, not the apply, is the
    /// judge — a failed undo statement just leaves more residual to name. This is
    /// the buildable alternative to the §10-deferred Atomic `BEGIN TRAN` envelope
    /// (which would make the failed-deploy state unreachable); per the J5 managed-env
    /// evidence (DML-only, AssignedBySink, cleanup-by-captured-key) the compensating
    /// channel — not a giant transaction — is the evidence-backed arm to build now.
    let private compensateToSource
        (failure: string)
        (source: Catalog)
        (cnn: SqlConnection)
        : System.Threading.Tasks.Task<MigrationError> =
        task {
            match! ReadSide.read cnn with
            | Error es ->
                // Cannot even read the partial state back — the worst case; surface
                // the original failure plus the read failure, never claim a rollback.
                let readMsg = es |> List.map (fun e -> e.Message) |> String.concat "; "
                return ExecutionFailed (sprintf "%s; additionally the post-failure read-back failed, so the partial state is unverifiable: %s" failure readMsg)
            | Ok live ->
                let residualBefore =
                    PhysicalSchema.diff (PhysicalSchema.ofCatalog source) (PhysicalSchema.ofCatalog live)
                if PhysicalSchema.isSchemaEqual residualBefore then
                    // Nothing applied, or the failed batch self-rolled-back: already at A.
                    return ExecutionRolledBack (failure, 0)
                else
                    // M12 — the inverse of the live displacement A→B'' is `between B'' A`.
                    // Realize only its rename channel (data-preserving, invertible).
                    let undo = renameStatements (CatalogDiff.between live source)
                    let mutable undone = 0
                    for stmt in undo do
                        try
                            // PL-6 (S14): one GO-free rename — one pre-split segment.
                            do! Deploy.executeSegments cnn [ stmt ]
                            undone <- undone + 1
                        with _ -> ()
                    match! ReadSide.read cnn with
                    | Error _ ->
                        // The compensation ran but we cannot confirm the result — name
                        // the last-known residual rather than claim a clean rollback.
                        return PartialWriteUnrecovered (failure, residualBefore)
                    | Ok live2 ->
                        let residualAfter =
                            PhysicalSchema.diff (PhysicalSchema.ofCatalog source) (PhysicalSchema.ofCatalog live2)
                        if PhysicalSchema.isSchemaEqual residualAfter then
                            return ExecutionRolledBack (failure, undone)
                        else
                            return PartialWriteUnrecovered (failure, residualAfter)
        }

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
                        // NM-54 — unverifiable CDC state is UNSAFE: a probe
                        // failure REFUSES the DDL (RefusedByCdcUnverifiable),
                        // never proceeds and never crashes.
                        match! ReadSide.cdcTrackedTables cnn with
                        | Error es ->
                            let msg = es |> List.map (fun e -> e.Message) |> String.concat "; "
                            return Error (RefusedByCdcUnverifiable msg)
                        | Ok tracked ->
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

    /// **M22 — the Atomic `BEGIN TRAN` envelope (opt-in, LOCAL full-access only).**
    /// When `atomic` is set AND there is DDL to apply, the schema-deploy stage is
    /// wrapped in `SET XACT_ABORT ON; BEGIN TRANSACTION;` … `COMMIT TRANSACTION;`,
    /// so a mid-deploy failure rolls the WHOLE deploy back atomically (`XACT_ABORT
    /// ON` auto-rolls-back on a run-time error; the explicit `IF @@TRANCOUNT > 0
    /// ROLLBACK` is belt-and-suspenders). M21's `compensateToSource` then VERIFIES
    /// the read-back is at A and supplies the verdict (`ExecutionRolledBack` when
    /// the rollback restored A — the common case; `PartialWriteUnrecovered` only if
    /// the rollback somehow left a residual — M21 as the envelope's fallback). The
    /// envelope is the §10 wrapper, fired for the local full-access case only
    /// (`DECISIONS 2026-06-16 M22`; `ATOMIC_ENVELOPE_VALIDATION.md`): production
    /// schema is ADO/Octopus/SSDT-deployed (not direct-connect) and the managed
    /// cloud is DML-only, so this DDL envelope is a LOCAL lever. The data leg's
    /// safety is the separate `--auto-revert` arm, NOT this transaction.
    /// `atomic = false` is byte-identical to the prior `execute` (M21 only).
    let executeWith
        (atomic: bool)
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
                                // M22 — open the Atomic envelope only when opted-in AND
                                // there is DDL to wrap (an idempotent no-DDL run needs no
                                // transaction). `XACT_ABORT ON` makes a run-time error
                                // roll the whole transaction back automatically.
                                let opened = atomic && hasDdl
                                try
                                    // PL-6 (S14): the envelope controls and each
                                    // rename are GO-free typed renders — one
                                    // pre-split segment apiece; `alterSql` keeps
                                    // `executeBatch` (it flows through
                                    // `Render.toText`, which CAN carry `GO` from
                                    // `BatchSeparator`, so the parser split stays
                                    // load-bearing there).
                                    if opened then
                                        do! Deploy.executeSegments cnn [ ScriptDomGenerate.renderAtomicEnvelopeOpen () ]
                                    let mutable applied = 0
                                    for stmt in renameSql do
                                        do! Deploy.executeSegments cnn [ stmt ]
                                        applied <- applied + 1
                                        LogSink.recordStageProgress "deploy" applied totalWrites sw.ElapsedMilliseconds
                                    if hasAlter then
                                        do! Deploy.executeBatch cnn alterSql
                                        applied <- applied + 1
                                        LogSink.recordStageProgress "deploy" applied totalWrites sw.ElapsedMilliseconds
                                    if opened then
                                        do! Deploy.executeSegments cnn [ ScriptDomGenerate.renderCommitTransaction () ]
                                    return Ok ()
                                with ex ->
                                    // M22 — if the envelope is open, roll the whole deploy
                                    // back atomically first (a no-op if XACT_ABORT already
                                    // did). Then M21's compensateToSource VERIFIES the
                                    // read-back is at A and supplies the verdict: a clean
                                    // rollback → ExecutionRolledBack; a residual →
                                    // PartialWriteUnrecovered (M21 as the envelope's
                                    // fallback). For atomic = false this is exactly M21:
                                    // ride the groupoid inverse over the applied rename
                                    // prefix; refuse-don't-corrupt; never a silent partial.
                                    if opened then
                                        try do! Deploy.executeSegments cnn [ ScriptDomGenerate.renderRollbackIfActive () ]
                                        with _ -> ()
                                    let! verdict = compensateToSource ex.Message source cnn
                                    return Error verdict
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
            // The spine closed the books; `StagedVerdict.toResult` preserves the
            // engine's crash semantics (re-raise on abort, failwith on refusal).
            return StagedVerdict.toResult verdict
        }

    /// The non-atomic migrate (M21 only) — byte-identical to the pre-M22 `execute`.
    /// Every existing caller keeps this signature; only the opt-in `--atomic` path
    /// calls `executeWith true`.
    let execute
        (allowCdc: bool)
        (declaration: LossDeclaration)
        (source: Catalog)
        (target: Catalog)
        (cnn: SqlConnection)
        : System.Threading.Tasks.Task<Result<MigrationOutcome, MigrationError>> =
        executeWith false allowCdc declaration source target cnn

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
        (atomic: bool)
        (allowCdc: bool)
        (declaration: LossDeclaration)
        (source: Catalog)
        (target: Catalog)
        (cnn: SqlConnection)
        : System.Threading.Tasks.Task<Result<MigrationOutcome * int, MigrationError>> =
        task {
            // NM-54 — the CDC measure surfaces its probe Error (a refused axis
            // can't be measured) rather than fabricating 0. M22 — `atomic` threads
            // the opt-in `BEGIN TRAN` envelope (LOCAL full-access; see `executeWith`).
            match! Deploy.cdcCaptureTotal cnn with
            | Error es -> return Error (SchemaReadFailed es)
            | Ok baseline ->
                let! result = executeWith atomic allowCdc declaration source target cnn
                match result with
                | Error e -> return Error e
                | Ok outcome ->
                    match! Deploy.cdcCaptureTotal cnn with
                    | Error es -> return Error (SchemaReadFailed es)
                    | Ok post ->
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
        (atomic: bool)
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
            // M22 — `atomic` threads the opt-in `BEGIN TRAN` envelope (see `executeWith`).
            let! result = executeWith atomic allowCdc declaration source target cnn
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
    /// Slice C1 — the policy-bearing migrate-with-data: a `FullRights` sink
    /// threads `IdentityPolicy.PreferPreservedKeys` so the populate preserves
    /// source keys (no capture/remap). `executeWithData` fixes `Structural`
    /// (byte-identical; the existing callers + the ManagedDml shape).
    let executeWithDataWith
        (identityPolicy: IdentityPolicy)
        (atomic: bool)
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
            // M22 (follow-on C) — the schema leg of migrate-with-data honors the
            // derived `--atomic` envelope; the data leg's safety is its own arm.
            let! schemaResult = executeWith atomic allowCdc declaration sinkSource target sink
            match schemaResult with
            | Error e -> return Error e
            | Ok schema ->
                if not schema.Verified then
                    return Error (VerificationFailed schema.SchemaDiff)
                else
                    let! transferResult =
                        // A5 — the data source is at A (`sinkSource`); re-point
                        // rows A→B through the rename-aware Transfer. The renameMap
                        // derives from the A→B `CatalogDiff` the schema leg already
                        // computed (`artifacts.Plan.Diff` over the identical pair —
                        // PL-1/S13, threaded not recomputed); a no-renames diff
                        // repoints by identity (== the straight load).
                        if Map.isEmpty reconciliation then
                            Transfer.runWithRenamesUsing schema.Artifacts.Plan.Diff identityPolicy mode allowCdc dataSource sink sinkSource target
                        else
                            // allowDrops = false: enforce the AC-I5 pre-write validate-user-map
                            // halt on the reconciling migrate-with-data path (the reconcile+migrate
                            // composition, AC-I7, is the follow-on; --allow-drops flows here then).
                            Transfer.runReconcilingWithRenamesUsing schema.Artifacts.Plan.Diff identityPolicy mode allowCdc dataSource sink sinkSource target reconciliation
                    match transferResult with
                    | Ok report -> return Ok { Schema = schema; Transfer = report }
                    | Error es -> return Error (DataTransferFailed es)
        }

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
        executeWithDataWith IdentityPolicy.Structural false declaration mode allowCdc sinkSource target reconciliation dataSource sink

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
    /// The synchronous record tail of `executeWithDataAndRecordWith` —
    /// module-level so the caller's `task { }` stays statically compilable
    /// in Release (FS3511). PL-10 (S12 sibling): ONE store load serves the
    /// coordinate derivation and the append.
    let private recordDataOutcome
        (path: string)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        (refactorLogRef: string option)
        (data: DataObservation)
        (schema: MigrationOutcome)
        (report: Transfer.TransferReport)
        : Result<MigrationDataOutcome * EpisodicLifecycle, MigrationError> =
        match loadChain path with
        | Error e -> Error (ExecutionFailed (sprintf "data load succeeded but reading the episode store failed: %A" e))
        | Ok chain ->
            match nextCoordinateOfChain chain environment at with
            | Error e -> Error (ExecutionFailed (sprintf "data load succeeded but deriving the episode coordinate failed: %A" e))
            | Ok coordinate ->
                match recordOnChain path timeline chain coordinate refactorLogRef data schema.Artifacts with
                | Ok appended -> Ok ({ Schema = schema; Transfer = report }, appended)
                | Error e -> Error (ExecutionFailed (sprintf "data load succeeded but recording the episode failed: %A" e))

    let executeWithDataAndRecordWith
        (identityPolicy: IdentityPolicy)
        (atomic: bool)
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
            // M22 (follow-on C) — atomic schema leg (see `executeWithDataWith`).
            let! schemaResult = executeWith atomic allowCdc declaration sinkSource target sink
            match schemaResult with
            | Error e -> return Error e
            | Ok schema ->
                // Measure the data movement: baseline before the load, post
                // after; the delta is the CDC capture count of the transfer.
                // NM-54 — the CDC measure surfaces its probe Error rather than 0.
                match! Deploy.cdcCaptureTotal sink with
                | Error es -> return Error (SchemaReadFailed es)
                | Ok baseline ->
                let! transferResult =
                    // A5 — the data source is at A (`sinkSource`); re-point rows
                    // A→B through the rename-aware Transfer over the schema leg's
                    // own diff (see `executeWithDataWith` — PL-1/S13).
                    if Map.isEmpty reconciliation then
                        Transfer.runWithRenamesUsing schema.Artifacts.Plan.Diff identityPolicy mode allowCdc dataSource sink sinkSource target
                    else
                        Transfer.runReconcilingWithRenamesUsing schema.Artifacts.Plan.Diff identityPolicy mode allowCdc dataSource sink sinkSource target reconciliation
                match transferResult with
                | Error es -> return Error (DataTransferFailed es)
                | Ok report ->
                    match! Deploy.cdcCaptureTotal sink with
                    | Error es -> return Error (SchemaReadFailed es)
                    | Ok post ->
                    let data = DataObservation.create (post - baseline) None
                    // PL-10 (S12 sibling) — the record tail is one sync call
                    // (module-level: FS3511) paying ONE store load.
                    return recordDataOutcome path timeline environment at refactorLogRef data schema report
        }

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
        executeWithDataAndRecordWith IdentityPolicy.Structural false declaration mode allowCdc sinkSource target reconciliation path timeline environment at refactorLogRef dataSource sink
