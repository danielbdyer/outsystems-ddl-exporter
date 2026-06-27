namespace Projection.Pipeline

// LINT-ALLOW-FILE: the capture SQL is now built as typed ScriptDom statements
//   (`ScriptDomBuild.buildCapture*` / `buildScopeIdentitySelect`) and rendered
//   through `ScriptDomGenerate` (Tier-2.1 typed-AST refactor). The ONLY terminal
//   text is the explicit `;` appended to the rendered MERGE — ScriptDom's
//   `generateOne` omits the trailing semicolon after a bare MERGE, which SQL
//   Server requires. Reader drains are module-level tail-recursive task
//   continuations (FS3511 posture).

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Targets.SSDT

/// One rung of the surrogate-capture ladder — HOW the sink realizes an
/// `AssignedBySink` chunk under the live capability envelope. The rungs
/// share one semantics (insert with the identity column omitted; return
/// every row's `(source → assigned)` correlation) and differ ONLY in the
/// sink capability they require — the ladder descends when a rung is
/// REFUSED BY THE SINK for a named capability reason, never for a data
/// reason (a constraint violation must surface, not degrade).
[<RequireQualifiedAccess>]
type CaptureLane =
    /// Bulk-stage the chunk into a session table, one `MERGE … OUTPUT`
    /// per chunk (the fastest rung; ~27k rows/sec sustained at the
    /// 2026-06-10 bench). SQL Server REFUSES `OUTPUT` without `INTO`
    /// when the target table carries enabled triggers (error 334) — the
    /// real-OSUSR risk this ladder exists for.
    | StagedMergeOutput
    /// The trigger-proof set-based rung: the same staged MERGE, with
    /// `OUTPUT … INTO` a session keymap table read back afterwards —
    /// legal on triggered targets, one extra round-trip per chunk.
    | StagedMergeOutputInto
    /// The floor: per-row `INSERT` + `SCOPE_IDENTITY()` (trigger-proof —
    /// SCOPE_IDENTITY is immune to identity inserts a trigger performs,
    /// unlike `@@IDENTITY`). O(rows) round-trips (~271 rows/sec measured);
    /// correct everywhere a plain INSERT is.
    | RowwiseScopeIdentity

[<RequireQualifiedAccess>]
module CaptureLane =

    /// The descent order — fastest first; every rung below a failing rung
    /// requires strictly less of the sink.
    let ladder : CaptureLane list =
        [ CaptureLane.StagedMergeOutput
          CaptureLane.StagedMergeOutputInto
          CaptureLane.RowwiseScopeIdentity ]

    let text (lane: CaptureLane) : string =
        match lane with
        | CaptureLane.StagedMergeOutput     -> "staged-merge-output"
        | CaptureLane.StagedMergeOutputInto -> "staged-merge-output-into"
        | CaptureLane.RowwiseScopeIdentity  -> "rowwise-scope-identity"

    /// The descent suffix of the ladder starting AT `preferred` (inclusive) — the
    /// rungs `captureChunkDescending` will try, fastest first. NM-49: TOTAL — a
    /// `preferred` not present in the ladder yields the FULL ladder (the maximally
    /// conservative descent from the head), never the empty tail that the raw
    /// `skipWhile` produced (which the descent loop mislabels "capture ladder
    /// exhausted", masking the unknown-preferred-lane cause). With the closed
    /// `CaptureLane` DU and a complete ladder, the miss does not arise — this
    /// keeps the positioning structural rather than positional regardless.
    let ladderFrom (preferred: CaptureLane) : CaptureLane list =
        match ladder |> List.skipWhile (fun l -> l <> preferred) with
        | []       -> ladder
        | fromHere -> fromHere

/// One recorded rung descent: the kind, the rung the sink refused, the
/// rung that took over, and the SQL error number that named the refusal.
/// Surfaced on the `TransferReport` — a degraded lane is a NAMED outcome,
/// never silence.
type LaneDescent =
    {
        Kind           : SsKey
        From           : CaptureLane
        To             : CaptureLane
        SqlErrorNumber : int
    }

[<RequireQualifiedAccess>]
module SurrogateCapture =

    [<Literal>]
    let private StagingTable = "[#__projection_capture]"

    [<Literal>]
    let private KeymapTable = "[#__projection_keymap]"

    // Raw (un-bracketed) session-table names for the typed `ScriptDomBuild.*`
    // builders (which bracket them); the bracketed forms above feed
    // `Bulk.copyRowsSession`'s bulk-load destination.
    [<Literal>]
    let private StagingTempName = "#__projection_capture"

    [<Literal>]
    let private KeymapTempName = "#__projection_keymap"

    /// The physical column name an attribute carries.
    let private colName (a: Attribute) = ColumnRealization.columnNameText a.Column

    let private insertColsOf (kind: Kind) : Attribute list =
        kind.Attributes |> List.filter (fun a -> not (a.IsPrimaryKey && a.IsIdentity))

    /// The staged chunk's cells: the source key first, then every insert
    /// column (deferred cycle columns as NULL — Phase 2 re-points them).
    /// Generic over the row carrier (Q3, A40): `getterOf` is STAGED — the
    /// per-column accessor is resolved once per chunk, then applied per
    /// row (a Map lookup for `StaticRow`, an ordinal index for
    /// `RowQuantum`).
    let private captureCells (getterOf: Attribute -> ('row -> string)) (identityAttr: Attribute) (insertCols: Attribute list) (deferred: Set<Name>) (rows: 'row list) : CellValue list list =
        let idGet = getterOf identityAttr
        let colGets =
            insertCols
            |> List.map (fun a ->
                let get =
                    if Set.contains a.Name deferred then (fun _ -> "")
                    else getterOf a
                ColumnRealization.columnNameText a.Column, a.Type, get)
        rows
        |> List.map (fun row ->
            { Column = "__SRC_KEY"
              Type = identityAttr.Type
              Raw = idGet row }
            :: (colGets
                |> List.map (fun (col, ty, get) ->
                    { Column = col; Type = ty; Raw = get row })))

    /// Clone the staging table's column types FROM THE SINK TABLE
    /// (`SELECT TOP 0 … INTO`; ISNULL strips the IDENTITY property — a
    /// CASE wrapper constant-folds and the property PROPAGATES, silently
    /// minting staging keys; probed live 2026-06-10).
    let private createStaging (sink: SqlConnection) (kind: Kind) (identityAttr: Attribute) (insertCols: Attribute list) : Task<unit> =
        Deploy.executeBatch sink
            (ScriptDomGenerate.generateOne
                (ScriptDomBuild.buildCaptureStaging StagingTempName kind.Physical (colName identityAttr) (insertCols |> List.map colName)))

    let private dropStaging (sink: SqlConnection) : unit =
        try
            (Deploy.executeBatch sink
                (ScriptDomGenerate.generateOne (ScriptDomBuild.buildDropTableIfExists StagingTempName)))
                .GetAwaiter().GetResult()
        with _ -> ()

    /// Drain a two-column `(source, assigned)` reader. Module-level
    /// tail-recursive task continuation (FS3511 posture).
    let rec private readPairs
        (reader: SqlDataReader)
        (acc: (string * string) list)
        : Task<(string * string) list> =
        task {
            let! has = reader.ReadAsync()
            if has then
                let src = if reader.IsDBNull 0 then "" else string (reader.GetValue 0)
                let assigned = if reader.IsDBNull 1 then "" else string (reader.GetValue 1)
                return! readPairs reader ((src, assigned) :: acc)
            else return List.rev acc
        }

    let private mergeOutputChunk
        (sink: SqlConnection) (kind: Kind) (identityAttr: Attribute) (insertCols: Attribute list)
        : Task<(string * string) list> =
        task {
            use cmd = sink.CreateCommand()
            cmd.CommandText <-
                ScriptDomGenerate.generateOne
                    (ScriptDomBuild.buildCaptureMerge kind.Physical StagingTempName (insertCols |> List.map colName) (colName identityAttr) None)
                + ";"
            cmd.CommandTimeout <- 0
            use! reader = cmd.ExecuteReaderAsync()
            return! readPairs reader []
        }

    let private mergeOutputIntoChunk
        (sink: SqlConnection) (kind: Kind) (identityAttr: Attribute) (insertCols: Attribute list)
        : Task<(string * string) list> =
        task {
            do! Deploy.executeBatch sink
                    (ScriptDomGenerate.generateOne
                        (ScriptDomBuild.buildKeymapStaging KeymapTempName kind.Physical (colName identityAttr)))
            try
                do! Deploy.executeBatch sink
                        (ScriptDomGenerate.generateOne
                            (ScriptDomBuild.buildCaptureMerge kind.Physical StagingTempName (insertCols |> List.map colName) (colName identityAttr) (Some KeymapTempName))
                         + ";")
                use cmd = sink.CreateCommand()
                cmd.CommandText <-
                    ScriptDomGenerate.generateOne
                        (ScriptDomBuild.buildSelectColumnsFromTemp [ ScriptDomBuild.captureSrcKeyColumn; ScriptDomBuild.captureAssignedColumn ] KeymapTempName)
                cmd.CommandTimeout <- 0
                use! reader = cmd.ExecuteReaderAsync()
                return! readPairs reader []
            finally
                try
                    (Deploy.executeBatch sink
                        (ScriptDomGenerate.generateOne (ScriptDomBuild.buildDropTableIfExists KeymapTempName)))
                        .GetAwaiter().GetResult()
                with _ -> ()
        }

    /// The floor rung — per row: INSERT (identity omitted, deferred NULLed)
    /// then `SCOPE_IDENTITY()`. Module-level tail-recursive continuation.
    /// `litGets` are the pre-staged per-column literal getters; `idGet`
    /// the source-key getter (Q3 — resolved once per chunk by
    /// `captureChunk`, generic over the row carrier).
    let rec private rowwiseChunk
        (sink: SqlConnection) (kind: Kind) (idGet: 'row -> string) (litGets: (Attribute * ('row -> string)) list)
        (rows: 'row list)
        (acc: (string * string) list)
        : Task<(string * string) list> =
        task {
            match rows with
            | [] -> return List.rev acc
            | row :: rest ->
                let insertStmt : Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement =
                    if List.isEmpty litGets then
                        ScriptDomBuild.buildInsertDefaultValues kind.Physical
                    else
                        let cells =
                            litGets
                            |> List.map (fun (a, get) -> { Column = colName a; Type = a.Type; Raw = get row })
                        ScriptDomBuild.buildInsertRow kind.Physical cells :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement
                // The INSERT then `SELECT CAST(SCOPE_IDENTITY() AS BIGINT)` as one
                // `;`-terminated batch; ExecuteScalar returns the SELECT's scalar
                // (the INSERT yields no result set).
                let insertSql =
                    ScriptDomGenerate.generateBatch [ insertStmt; ScriptDomBuild.buildScopeIdentitySelect () ]
                use cmd = sink.CreateCommand()
                cmd.CommandText <- insertSql
                let! scalar = cmd.ExecuteScalarAsync()
                let assigned =
                    if isNull scalar || scalar = box System.DBNull.Value then ""
                    else string scalar
                let src = idGet row
                return! rowwiseChunk sink kind idGet litGets rest ((src, assigned) :: acc)
        }

    /// Realize one chunk on ONE named rung. Staged rungs share the staging
    /// transport; the rowwise floor needs none.
    let captureChunk
        (sink: SqlConnection)
        (kind: Kind)
        (getterOf: Attribute -> ('row -> string))
        (identityAttr: Attribute)
        (deferred: Set<Name>)
        (lane: CaptureLane)
        (rows: 'row list)
        : Task<(string * string) list> =
        task {
            let insertCols = insertColsOf kind
            match lane with
            | CaptureLane.RowwiseScopeIdentity ->
                let idGet = getterOf identityAttr
                let litGets =
                    insertCols
                    |> List.map (fun a ->
                        a, (if Set.contains a.Name deferred then (fun _ -> "") else getterOf a))
                return! rowwiseChunk sink kind idGet litGets rows []
            | CaptureLane.StagedMergeOutput
            | CaptureLane.StagedMergeOutputInto ->
                do! createStaging sink kind identityAttr insertCols
                try
                    do! Bulk.copyRowsSession sink StagingTable (captureCells getterOf identityAttr insertCols deferred rows)
                    match lane with
                    | CaptureLane.StagedMergeOutput -> return! mergeOutputChunk sink kind identityAttr insertCols
                    | _                             -> return! mergeOutputIntoChunk sink kind identityAttr insertCols
                finally
                    dropStaging sink
        }

    /// The CAPABILITY recognizer — the only errors that descend the
    /// ladder. 334: `OUTPUT` without `INTO` on a target with enabled
    /// triggers. Everything else (constraint violations, conversion
    /// errors, deadlocks) PROPAGATES — degrading on a data error would
    /// mask corruption.
    let private isCapabilityRefusal (ex: SqlException) : bool =
        CapabilityRefusal.isRefusal Capability.OutputWithoutIntoOnTriggeredTarget ex

    /// Realize one chunk starting at `preferred`, descending the ladder on
    /// a named capability refusal. Returns the pairs, the rung that
    /// SUCCEEDED (the caller keeps it sticky for the kind's later chunks),
    /// and every descent taken — each a named outcome for the report.
    let captureChunkDescending
        (sink: SqlConnection)
        (kind: Kind)
        (kindKey: SsKey)
        (getterOf: Attribute -> ('row -> string))
        (identityAttr: Attribute)
        (deferred: Set<Name>)
        (preferred: CaptureLane)
        (rows: 'row list)
        : Task<(string * string) list * CaptureLane * LaneDescent list> =
        let rec attempt (lanes: CaptureLane list) (descents: LaneDescent list) : Task<(string * string) list * CaptureLane * LaneDescent list> =
            task {
                match lanes with
                | [] ->
                    // Unreachable: the ladder's floor requires only INSERT,
                    // and an INSERT failure is not a capability refusal.
                    return invalidOp "capture ladder exhausted"
                | [ last ] ->
                    let! pairs = captureChunk sink kind getterOf identityAttr deferred last rows
                    return pairs, last, List.rev descents
                | lane :: (next :: _ as rest) ->
                    try
                        let! pairs = captureChunk sink kind getterOf identityAttr deferred lane rows
                        return pairs, lane, List.rev descents
                    with :? SqlException as ex when isCapabilityRefusal ex ->
                        return!
                            attempt rest
                                ({ Kind = kindKey; From = lane; To = next; SqlErrorNumber = ex.Number } :: descents)
            }
        // NM-49: position the descent at `preferred` via the TOTAL `ladderFrom`
        // (an unknown preferred lane begins at the ladder head, never the empty
        // tail that `attempt` would mislabel "capture ladder exhausted").
        attempt (CaptureLane.ladderFrom preferred) []
