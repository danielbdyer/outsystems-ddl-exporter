namespace Projection.Pipeline

// LINT-ALLOW-FILE-MUTATION: BCL System.Text.Json.Nodes DOM construction (the
//   JsonObject indexer set IS the sanctioned typed-JSON path — the ReportRun
//   precedent) + the task-CE fold accumulators over the async companion runs
//   (FS3511: no recursive binds inside task { }).

open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Targets.SSDT
open Projection.Adapters.Sql

/// The **dynamic bridge-row staging seam** — the registered companion that lets
/// a bridge retarget derive its bridge-row supply from the LIVE estate instead
/// of a pre-authored supplement. For each `overrides.bridgeRowStaging`
/// declaration it:
///
///   1. derives the REFERENCED KEYS (every distinct non-null value the linked
///      retargets' FK columns hold — the exact set the retarget invariant must
///      resolve),
///   2. snapshots the SOURCE and BRIDGE rows for those keys (+ the
///      bridge-wide key soundness aggregates),
///   3. computes the pure per-key delta (`BridgeRowDelta.compute` — already
///      complete / stage insert / fill-only identity update / named block),
///   4. renders the staged changes as the STAGING LANE's typed statements —
///      `InsertRowIfAbsent` (guarded, idempotent) + `Update` with the
///      fill-only `NullGuardColumns` guard — deliberately NOT the
///      migration-dependency MERGE (whose WHEN MATCHED arm would overwrite
///      matched rows and NULL omitted cells; the safety rule this lane
///      exists to make unrepresentable),
///   5. emits the durable audit artifacts (referenced keys, both snapshots,
///      the delta ledger, the staged SQL, the evidence disposition) under
///      `BridgeStaging/<id>/` in the output bundle, and
///   6. derives the PLANNED-STATE retarget evidence
///      (`BridgeRowDelta.plannedEvidence` — withheld entirely when any key's
///      verdict blocks), handed to `BridgeRetargetBinding` so the linked
///      retargets can CLEAR against current-plus-staged bridge rows.
///
/// **Fail-closed at every boundary.** No declarations ⇒ the empty outcome
/// (no acquisition, no artifacts, evidence untouched — byte-identical). A
/// declaration with no live source connection, an unresolvable coordinate, or
/// a failed acquisition is a NAMED refusal — never a silently-skipped staging.
/// A blocked plan stages NOTHING for its entry and derives NO evidence — the
/// linked retargets stay `unproven`, so the retarget does not land; the delta
/// artifact enumerates exactly why, no more and no less.
///
/// **Registered ⇔ executed.** The seam's single companion entry pairs its
/// registry metadata with the execution `run`; `metadata` / `executedNames`
/// project from the SAME list `execute` folds (the `DataCorrectionSeam`
/// discipline), spliced into `RegisteredAllTransforms.all` and covered by the
/// bidirectional totality test.
[<RequireQualifiedAccess>]
module BridgeRowStagingSeam =

    /// The seam's product: the derived planned-state evidence per linked
    /// retarget id (feeds `BridgeRetargetBinding`), and the durable artifact
    /// bodies keyed by bundle-relative path (feeds `Outputs.BridgeStaging`).
    type StagingOutcome =
        { DerivedEvidence : Map<string, BridgeRetargetEvidence>
          Artifacts       : Map<string, string> }

    /// The no-staging identity (declarations empty, or the pipelined arm).
    let emptyOutcome : StagingOutcome =
        { DerivedEvidence = Map.empty; Artifacts = Map.empty }

    let private err (code: string) (message: string) : Result<'a> =
        Result.failureOf (ValidationError.create code message)

    let private jsonOpts = JsonSerializerOptions(WriteIndented = true)

    // ------------------------------------------------------------------
    // Staged-statement assembly (the staging lane's typed shapes).
    // ------------------------------------------------------------------

    /// Build the staging lane's typed statements from the computed plan:
    /// guarded single-row inserts (key cell structural, `IF NOT EXISTS`) and
    /// fill-only identity updates (`NullGuardColumns` = the identity column).
    /// When the bridge key is an IDENTITY column, the insert block is
    /// bracketed with `SET IDENTITY_INSERT ON/OFF` (the MERGE lane's
    /// precedent). Deterministic: the plan's lists are key-sorted.
    let private stagedStatements
        (resolved: BridgeRowStagingBinding.ResolvedStaging)
        (plan: BridgeRowStagingPlan)
        : Statement list =
        let table = TableId.withoutCatalog resolved.BridgeKind.Physical
        let attrByName =
            resolved.BridgeKind.Attributes
            |> List.map (fun a -> a.Name, a)
            |> Map.ofList
        let keyCol = ColumnRealization.columnNameText resolved.BridgeKey.Column
        let identityCol = ColumnRealization.columnNameText resolved.BridgeIdentity.Column
        let inserts =
            plan.Inserts
            |> List.map (fun ins ->
                let keyCell : CellValue =
                    { Column = keyCol; Type = resolved.BridgeKey.Type; Raw = Some ins.Key }
                let otherCells =
                    ins.Cells
                    |> Map.toList
                    |> List.filter (fun (name, _) -> name <> resolved.BridgeKey.Name)
                    |> List.choose (fun (name, raw) ->
                        Map.tryFind name attrByName
                        |> Option.map (fun a ->
                            { Column = ColumnRealization.columnNameText a.Column
                              Type   = a.Type
                              Raw    = raw }))
                Statement.InsertRowIfAbsent (table, keyCell, otherCells))
        let insertBlock =
            if List.isEmpty inserts then []
            elif resolved.BridgeKey.IsIdentity then
                [ Statement.SetIdentityInsert (table, true) ]
                @ inserts
                @ [ Statement.SetIdentityInsert (table, false) ]
            else inserts
        let updates =
            plan.IdentityUpdates
            |> List.map (fun upd ->
                Statement.Update
                    { Target           = table
                      SetCells         = [ identityCol, SqlLiteral.ofRaw resolved.BridgeIdentity.Type (Some upd.Identity) ]
                      WhereCells       = [ keyCol, SqlLiteral.ofRaw resolved.BridgeKey.Type (Some upd.Key) ]
                      CdcAware         = false
                      NullGuardColumns = [ identityCol ] })
        insertBlock @ updates

    // ------------------------------------------------------------------
    // Durable artifacts (typed JSON via JsonNode; A44's audit sextet).
    // ------------------------------------------------------------------

    /// Set `key` on `target` to the cell's JSON value — `None` (SQL NULL)
    /// lands as JSON `null`, so the artifact preserves the codebase's
    /// NULL-vs-empty-string distinction (the `EstateEvidenceStore` indexer
    /// idiom; the JsonObject indexer is null-accepting).
    let private setCell (target: JsonObject) (key: string) (v: string option) : unit =
        match v with
        | Some s -> target[key] <- JsonValue.Create s
        | None   -> target[key] <- null

    let private referencedKeysJson (stagingId: string) (links: BridgeRowStagingBinding.ResolvedRetargetLink list) (keys: string list) : string =
        let node = JsonObject()
        node["stagingId"] <- JsonValue.Create stagingId
        let linkArr = JsonArray()
        for l in links do linkArr.Add(JsonValue.Create l.RetargetId)
        node["linkedRetargets"] <- linkArr
        let keyArr = JsonArray()
        for k in keys do keyArr.Add(JsonValue.Create k)
        node["keys"] <- keyArr
        node.ToJsonString jsonOpts

    let private sourceSnapshotJson (rows: BridgeSourceSnapshotRow list) : string =
        let arr = JsonArray()
        for r in rows |> List.sortWith (fun a b -> System.String.CompareOrdinal(a.Key, b.Key)) do
            let row = JsonObject()
            row["key"] <- JsonValue.Create r.Key
            setCell row "identity" r.Identity
            let cells = JsonObject()
            for KeyValue (name, v) in r.Cells do
                setCell cells (Name.value name) v
            row["cells"] <- cells
            arr.Add row
        let node = JsonObject()
        node["rows"] <- arr
        node.ToJsonString jsonOpts

    let private bridgeSnapshotJson (wide: BridgeWideStats) (rows: BridgeSnapshotRow list) : string =
        let arr = JsonArray()
        for r in rows |> List.sortWith (fun a b -> System.String.CompareOrdinal(a.Key, b.Key)) do
            let row = JsonObject()
            row["key"] <- JsonValue.Create r.Key
            setCell row "identity" r.Identity
            arr.Add row
        let node = JsonObject()
        node["rows"] <- arr
        node["wideKeyNullCount"] <- JsonValue.Create wide.KeyNullCount
        node["wideKeyDuplicateCount"] <- JsonValue.Create wide.KeyDuplicateCount
        node.ToJsonString jsonOpts

    let private deltaJson (plan: BridgeRowStagingPlan) : string =
        let arr = JsonArray()
        for (key, verdict) in plan.Verdicts do
            let row = JsonObject()
            row["key"] <- JsonValue.Create key
            row["verdict"] <- JsonValue.Create (BridgeRowVerdict.name verdict)
            row["blocking"] <- JsonValue.Create (BridgeRowVerdict.isBlocking verdict)
            arr.Add row
        let node = JsonObject()
        node["stagingId"] <- JsonValue.Create plan.StagingId
        node["verdicts"] <- arr
        node["stagedInserts"] <- JsonValue.Create (List.length plan.Inserts)
        node["stagedIdentityUpdates"] <- JsonValue.Create (List.length plan.IdentityUpdates)
        node["blockingCount"] <- JsonValue.Create (List.length (BridgeRowDelta.blockingVerdicts plan))
        node.ToJsonString jsonOpts

    let private evidenceJson
        (links: BridgeRowStagingBinding.ResolvedRetargetLink list)
        (plan: BridgeRowStagingPlan)
        (evidence: BridgeRetargetEvidence option)
        : string =
        let node = JsonObject()
        node["stagingId"] <- JsonValue.Create plan.StagingId
        let linkArr = JsonArray()
        for l in links do linkArr.Add(JsonValue.Create l.RetargetId)
        node["appliesToRetargets"] <- linkArr
        match evidence with
        | Some ev ->
            let e = JsonObject()
            e["unresolvedThroughBridge"] <- JsonValue.Create ev.UnresolvedThroughBridge
            e["brokenOriginalParent"] <- JsonValue.Create ev.BrokenOriginalParent
            e["orphanedBridgeRows"] <- JsonValue.Create ev.OrphanedBridgeRows
            e["payloadConflicts"] <- JsonValue.Create ev.PayloadConflicts
            e["bridgeKeyDuplicates"] <- JsonValue.Create ev.BridgeKeyDuplicates
            e["bridgeKeyNulls"] <- JsonValue.Create ev.BridgeKeyNulls
            e["identityEvidence"] <-
                JsonValue.Create(
                    match ev.IdentityEvidence with
                    | BridgeIdentityEvidence.Present   -> "present"
                    | BridgeIdentityEvidence.Missing   -> "missing"
                    | BridgeIdentityEvidence.Ambiguous -> "ambiguous")
            node["derived"] <- e
        | None ->
            node["derived"] <- null
            let blocked = JsonArray()
            for (key, verdict) in BridgeRowDelta.blockingVerdicts plan do
                let b = JsonObject()
                b["key"] <- JsonValue.Create key
                b["verdict"] <- JsonValue.Create (BridgeRowVerdict.name verdict)
                blocked.Add b
            node["withheldBecause"] <- blocked
        node.ToJsonString jsonOpts

    // ------------------------------------------------------------------
    // The companion execution (acquire → delta → stage → artifacts →
    // evidence) for ONE resolved declaration.
    // ------------------------------------------------------------------

    let private runOne
        (cnn: SqlConnection)
        (resolved: BridgeRowStagingBinding.ResolvedStaging)
        : Task<Map<string, BridgeRetargetEvidence> * Map<string, string>> =
        task {
            use _ = Bench.scope "pipeline.bridgeRowStaging.runOne"
            // 1. Referenced keys — union of each linked FK column's distinct
            //    non-null values.
            let keyAcc = System.Collections.Generic.List<string>()
            for link in resolved.Links do
                let! ks = BridgeSnapshotReader.distinctColumnValues cnn link.ChildKind link.FkAttribute
                keyAcc.AddRange ks
            let referencedKeys = keyAcc |> List.ofSeq |> List.distinct
            // 2. Snapshots (source cells minimized to key + identity + mapped).
            let! sourceStatic =
                BridgeSnapshotReader.snapshotByKeys cnn resolved.SourceKind resolved.SourceKey.Name referencedKeys
            let mappedSourceAttrs = resolved.Mappings |> List.map snd
            let sourceRows =
                sourceStatic
                |> List.choose (fun row ->
                    StaticRow.value resolved.SourceKey.Name row
                    |> Option.map (fun key ->
                        { Key      = key
                          Identity = StaticRow.value resolved.SourceIdentity.Name row
                          Cells    =
                            mappedSourceAttrs
                            |> List.map (fun a -> a.Name, StaticRow.value a.Name row)
                            |> Map.ofList }))
            let! bridgeStatic =
                BridgeSnapshotReader.snapshotByKeys cnn resolved.BridgeKind resolved.BridgeKey.Name referencedKeys
            let bridgeRows =
                bridgeStatic
                |> List.choose (fun row ->
                    StaticRow.value resolved.BridgeKey.Name row
                    |> Option.map (fun key ->
                        { BridgeSnapshotRow.Key = key
                          Identity = StaticRow.value resolved.BridgeIdentity.Name row }))
            let! wide = BridgeSnapshotReader.wideKeyStats cnn resolved.BridgeKind resolved.BridgeKey
            // 3. The pure delta + planned evidence.
            let spec = BridgeRowStagingBinding.toSpec resolved
            let plan = BridgeRowDelta.compute spec referencedKeys sourceRows bridgeRows
            let evidence = BridgeRowDelta.plannedEvidence wide plan
            // 4. The staged SQL. FAIL-CLOSED as a WHOLE: any blocking verdict
            //    stages NOTHING for this entry — a partially-staged supply
            //    would be change without a consumer (the retarget cannot land
            //    over the blocks), the "overdoing" the audit posture forbids.
            //    The delta artifact still enumerates what WOULD have staged.
            //    An empty or blocked plan yields an empty body (the delta
            //    artifact says why; renderDataBatch is never fed an empty
            //    stream).
            let statements =
                if List.isEmpty (BridgeRowDelta.blockingVerdicts plan) then stagedStatements resolved plan
                else []
            let stagedSql =
                if List.isEmpty statements then ""
                else ScriptDomGenerate.renderDataBatch statements
            // 5. The audit sextet under BridgeStaging/<id>/.
            let dir = System.String.Concat("BridgeStaging/", resolved.StagingId, "/")  // LINT-ALLOW: terminal bundle-relative artifact path composition (the DataBundle relPath idiom)
            let at (name: string) = System.String.Concat(dir, name)  // LINT-ALLOW: terminal bundle-relative artifact path composition
            let artifacts =
                Map.ofList
                    [ at "referenced-keys.json", referencedKeysJson resolved.StagingId resolved.Links referencedKeys
                      at "source-snapshot.json", sourceSnapshotJson sourceRows
                      at "bridge-snapshot.json", bridgeSnapshotJson wide bridgeRows
                      at "delta.json", deltaJson plan
                      at "staged.sql", stagedSql
                      at "evidence.json", evidenceJson resolved.Links plan evidence ]
            // 6. The derived evidence per linked retarget — ONLY when the plan
            //    is clean (fail-closed: a blocked plan leaves the linked
            //    retargets unproven).
            let evidenceMap =
                match evidence with
                | None -> Map.empty
                | Some ev ->
                    resolved.Links
                    |> List.map (fun l -> l.RetargetId, ev)
                    |> Map.ofList
            return evidenceMap, artifacts
        }

    /// One registered staging companion: its registry metadata paired with the
    /// execution it drives — the pairing is the load-bearing invariant (the
    /// `DataCorrectionSeam` discipline).
    type private Companion =
        { Metadata : RegisteredTransformMetadata
          Run      : Config.Config -> Catalog -> string -> Task<Result<StagingOutcome>> }

    let private dynamicBridgeRowStaging : Companion =
        { Metadata =
            { Name         = "bridgeRowStaging"
              Domain       = Data
              StageBinding = Pipeline
              Status       = Active
              Sites =
                [ TransformSite.operatorIntent "bridgeRowStaging" Insertion
                    "Derive the bridge-row supply for the declared bridge retargets from the LIVE estate (`overrides.bridgeRowStaging`): referenced keys from the retargeting FK columns, source/bridge snapshots, a pure per-key delta, staged GUARDED inserts + FILL-ONLY identity updates (never the full-row MERGE — an existing non-null bridge cell is structurally unreachable), planned-state retarget evidence, and the durable audit sextet. OperatorIntent Insertion: the staged rows are content the estate gains, driven entirely by operator declaration. Empty ⇒ identity (no acquisition, byte-identical); any block ⇒ the linked retargets stay unproven." ] }
          Run =
            fun cfg catalog sourceConnectionString ->
                task {
                    if List.isEmpty cfg.Overrides.BridgeRowStaging then
                        return Result.success emptyOutcome
                    else
                        match BridgeRowStagingBinding.fromConfig catalog cfg with
                        | Error errs -> return Error errs
                        | Ok resolved ->
                            if System.String.IsNullOrWhiteSpace sourceConnectionString then
                                return
                                    err "pipeline.bridgeRowStaging.noSourceConnection"
                                        "bridge row staging is declared but no live source connection is available (PROJECTION_MSSQL_CONN_STR) — the companion derives from the live estate and cannot run without it"
                            else
                                try
                                    use _ = Bench.scope "pipeline.bridgeRowStaging.execute"
                                    use cnn = new SqlConnection(sourceConnectionString)
                                    do! cnn.OpenAsync()
                                    let mutable evidenceAcc = Map.empty
                                    let mutable artifactAcc = Map.empty
                                    for r in resolved do
                                        // FS3511 (survival rule): bind the single
                                        // value, destructure OUTSIDE the let!.
                                        let! outcome = runOne cnn r
                                        let evidenceMap, artifacts = outcome
                                        evidenceAcc <- Map.fold (fun m k v -> Map.add k v m) evidenceAcc evidenceMap
                                        artifactAcc <- Map.fold (fun m k v -> Map.add k v m) artifactAcc artifacts
                                    return Result.success { DerivedEvidence = evidenceAcc; Artifacts = artifactAcc }
                                with ex ->
                                    return
                                        err "pipeline.bridgeRowStaging.acquisitionFailed"
                                            (System.String.Concat("bridge row staging acquisition failed against the live source: ", ex.Message))  // LINT-ALLOW: terminal operator-diagnostic composition at the adapter-failure boundary
                } }

    /// The registered staging companions, in application order — the SINGLE
    /// source `execute` / `metadata` / `executedNames` all project from.
    let private companions : Companion list = [ dynamicBridgeRowStaging ]

    /// Execute every registered staging companion in order, folding the
    /// outcomes (evidence maps + artifact maps union; a later companion's
    /// claim on the same key would already have been refused at bind).
    /// FAIL-CLOSED: the first companion's named refusal short-circuits.
    let execute
        (cfg: Config.Config)
        (catalog: Catalog)
        (sourceConnectionString: string)
        : Task<Result<StagingOutcome>> =
        task {
            let mutable state = Result.success emptyOutcome
            for c in companions do
                match state with
                | Error _ -> ()
                | Ok acc ->
                    let! next = c.Run cfg catalog sourceConnectionString
                    state <-
                        next
                        |> Result.map (fun o ->
                            { DerivedEvidence = Map.fold (fun m k v -> Map.add k v m) acc.DerivedEvidence o.DerivedEvidence
                              Artifacts       = Map.fold (fun m k v -> Map.add k v m) acc.Artifacts o.Artifacts })
            return state
        }

    /// The seam's registry metadata — projected from the SAME `companions`
    /// that `execute` folds, so `registered ⇔ executed` holds by construction.
    /// Spliced into `RegisteredAllTransforms.all`.
    let metadata : RegisteredTransformMetadata list =
        companions |> List.map (fun c -> c.Metadata)

    /// The executed companion names — the bidirectional test pairs these
    /// against `metadata` and against `RegisteredAllTransforms.all`.
    let executedNames : string list =
        companions |> List.map (fun c -> c.Metadata.Name)
