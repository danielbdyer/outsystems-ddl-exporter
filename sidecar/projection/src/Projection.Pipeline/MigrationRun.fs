namespace Projection.Pipeline

open Projection.Core
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
                let schema = SchemaMigrationEmitter.emit plan.Diff
                let schemaErrors = Diagnostics.entriesAt DiagnosticSeverity.Error schema
                if not (List.isEmpty schemaErrors) then
                    Error (RefusedBySchemaErrors schemaErrors)
                else
                    match RefactorLogEmitter.emit plan.Diff with
                    | Error e -> Error (EmitFailed e)
                    | Ok refactorArtifact ->
                        Ok
                            { Plan = plan
                              SchemaStatements = schema.Value
                              SchemaDiagnostics = schema.Entries
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
