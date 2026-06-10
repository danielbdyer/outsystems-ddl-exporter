namespace Projection.Pipeline

// LINT-ALLOW-FILE: terminal SQL text over validated TableIds at the
//   capture realization boundary (the same allowed class as TransferRun);
//   reader drains are module-level tail-recursive task continuations
//   (FS3511 posture).

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

    let private quotedCol (a: Attribute) = Render.quote (ColumnRealization.columnNameText a.Column)

    let private insertColsOf (kind: Kind) : Attribute list =
        kind.Attributes |> List.filter (fun a -> not (a.IsPrimaryKey && a.IsIdentity))

    /// The staged chunk's cells: the source key first, then every insert
    /// column (deferred cycle columns as NULL — Phase 2 re-points them).
    let private captureCells (identityAttr: Attribute) (insertCols: Attribute list) (deferred: Set<Name>) (rows: StaticRow list) : CellValue list list =
        rows
        |> List.map (fun row ->
            { Column = "__SRC_KEY"
              Type = identityAttr.Type
              Raw = Map.tryFind identityAttr.Name row.Values |> Option.defaultValue "" }
            :: (insertCols
                |> List.map (fun a ->
                    let raw =
                        if Set.contains a.Name deferred then ""
                        else Map.tryFind a.Name row.Values |> Option.defaultValue ""
                    { Column = ColumnRealization.columnNameText a.Column; Type = a.Type; Raw = raw })))

    /// Clone the staging table's column types FROM THE SINK TABLE
    /// (`SELECT TOP 0 … INTO`; ISNULL strips the IDENTITY property — a
    /// CASE wrapper constant-folds and the property PROPAGATES, silently
    /// minting staging keys; probed live 2026-06-10).
    let private createStaging (sink: SqlConnection) (kind: Kind) (identityAttr: Attribute) (insertCols: Attribute list) : Task<unit> =
        let idCol = quotedCol identityAttr
        Deploy.executeBatch sink
            (sprintf "SELECT TOP 0 ISNULL(%s, %s) AS [__SRC_KEY]%s INTO %s FROM %s;"
                idCol idCol
                (insertCols |> List.map (fun a -> ", " + quotedCol a) |> String.concat "")
                StagingTable
                (Render.tableQualified kind.Physical))

    let private dropStaging (sink: SqlConnection) : unit =
        try
            (Deploy.executeBatch sink (sprintf "IF OBJECT_ID('tempdb..%s') IS NOT NULL DROP TABLE %s;" "#__projection_capture" StagingTable))
                .GetAwaiter().GetResult()
        with _ -> ()

    let private insertArmOf (insertCols: Attribute list) : string =
        if List.isEmpty insertCols then "INSERT DEFAULT VALUES"
        else
            sprintf "INSERT (%s) VALUES (%s)"
                (insertCols |> List.map quotedCol |> String.concat ", ")
                (insertCols |> List.map (fun a -> sprintf "S.%s" (quotedCol a)) |> String.concat ", ")

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
                sprintf "MERGE INTO %s AS T USING %s AS S ON 1 = 0 WHEN NOT MATCHED THEN %s OUTPUT S.[__SRC_KEY], INSERTED.%s;"
                    (Render.tableQualified kind.Physical)
                    StagingTable
                    (insertArmOf insertCols)
                    (quotedCol identityAttr)
            cmd.CommandTimeout <- 0
            use! reader = cmd.ExecuteReaderAsync()
            return! readPairs reader []
        }

    let private mergeOutputIntoChunk
        (sink: SqlConnection) (kind: Kind) (identityAttr: Attribute) (insertCols: Attribute list)
        : Task<(string * string) list> =
        task {
            let idCol = quotedCol identityAttr
            do! Deploy.executeBatch sink
                    (sprintf "SELECT TOP 0 ISNULL(%s, %s) AS [__SRC_KEY], ISNULL(%s, %s) AS [__ASSIGNED] INTO %s FROM %s;"
                        idCol idCol idCol idCol KeymapTable (Render.tableQualified kind.Physical))
            try
                do! Deploy.executeBatch sink
                        (sprintf "MERGE INTO %s AS T USING %s AS S ON 1 = 0 WHEN NOT MATCHED THEN %s OUTPUT S.[__SRC_KEY], INSERTED.%s INTO %s ([__SRC_KEY], [__ASSIGNED]);"
                            (Render.tableQualified kind.Physical)
                            StagingTable
                            (insertArmOf insertCols)
                            (quotedCol identityAttr)
                            KeymapTable)
                use cmd = sink.CreateCommand()
                cmd.CommandText <- sprintf "SELECT [__SRC_KEY], [__ASSIGNED] FROM %s;" KeymapTable
                cmd.CommandTimeout <- 0
                use! reader = cmd.ExecuteReaderAsync()
                return! readPairs reader []
            finally
                try
                    (Deploy.executeBatch sink (sprintf "IF OBJECT_ID('tempdb..%s') IS NOT NULL DROP TABLE %s;" "#__projection_keymap" KeymapTable))
                        .GetAwaiter().GetResult()
                with _ -> ()
        }

    /// The floor rung — per row: INSERT (identity omitted, deferred NULLed)
    /// then `SCOPE_IDENTITY()`. Module-level tail-recursive continuation.
    let rec private rowwiseChunk
        (sink: SqlConnection) (kind: Kind) (identityAttr: Attribute) (insertCols: Attribute list) (deferred: Set<Name>)
        (rows: StaticRow list)
        (acc: (string * string) list)
        : Task<(string * string) list> =
        task {
            match rows with
            | [] -> return List.rev acc
            | row :: rest ->
                let lit (a: Attribute) =
                    let raw =
                        if Set.contains a.Name deferred then ""
                        else Map.tryFind a.Name row.Values |> Option.defaultValue ""
                    raw |> SqlLiteral.ofRaw a.Type |> SqlLiteral.toString
                let insertSql =
                    if List.isEmpty insertCols then
                        sprintf "INSERT INTO %s DEFAULT VALUES; SELECT CAST(SCOPE_IDENTITY() AS BIGINT);"
                            (Render.tableQualified kind.Physical)
                    else
                        sprintf "INSERT INTO %s (%s) VALUES (%s); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);"
                            (Render.tableQualified kind.Physical)
                            (insertCols |> List.map quotedCol |> String.concat ", ")
                            (insertCols |> List.map lit |> String.concat ", ")
                use cmd = sink.CreateCommand()
                cmd.CommandText <- insertSql
                let! scalar = cmd.ExecuteScalarAsync()
                let assigned =
                    if isNull scalar || scalar = box System.DBNull.Value then ""
                    else string scalar
                let src = Map.tryFind identityAttr.Name row.Values |> Option.defaultValue ""
                return! rowwiseChunk sink kind identityAttr insertCols deferred rest ((src, assigned) :: acc)
        }

    /// Realize one chunk on ONE named rung. Staged rungs share the staging
    /// transport; the rowwise floor needs none.
    let captureChunk
        (sink: SqlConnection)
        (kind: Kind)
        (identityAttr: Attribute)
        (deferred: Set<Name>)
        (lane: CaptureLane)
        (rows: StaticRow list)
        : Task<(string * string) list> =
        task {
            let insertCols = insertColsOf kind
            match lane with
            | CaptureLane.RowwiseScopeIdentity ->
                return! rowwiseChunk sink kind identityAttr insertCols deferred rows []
            | CaptureLane.StagedMergeOutput
            | CaptureLane.StagedMergeOutputInto ->
                do! createStaging sink kind identityAttr insertCols
                try
                    do! Bulk.copyRowsSession sink StagingTable (captureCells identityAttr insertCols deferred rows)
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
        ex.Number = 334

    /// Realize one chunk starting at `preferred`, descending the ladder on
    /// a named capability refusal. Returns the pairs, the rung that
    /// SUCCEEDED (the caller keeps it sticky for the kind's later chunks),
    /// and every descent taken — each a named outcome for the report.
    let captureChunkDescending
        (sink: SqlConnection)
        (kind: Kind)
        (kindKey: SsKey)
        (identityAttr: Attribute)
        (deferred: Set<Name>)
        (preferred: CaptureLane)
        (rows: StaticRow list)
        : Task<(string * string) list * CaptureLane * LaneDescent list> =
        let rec attempt (lanes: CaptureLane list) (descents: LaneDescent list) : Task<(string * string) list * CaptureLane * LaneDescent list> =
            task {
                match lanes with
                | [] ->
                    // Unreachable: the ladder's floor requires only INSERT,
                    // and an INSERT failure is not a capability refusal.
                    return invalidOp "capture ladder exhausted"
                | [ last ] ->
                    let! pairs = captureChunk sink kind identityAttr deferred last rows
                    return pairs, last, List.rev descents
                | lane :: (next :: _ as rest) ->
                    try
                        let! pairs = captureChunk sink kind identityAttr deferred lane rows
                        return pairs, lane, List.rev descents
                    with :? SqlException as ex when isCapabilityRefusal ex ->
                        return!
                            attempt rest
                                ({ Kind = kindKey; From = lane; To = next; SqlErrorNumber = ex.Number } :: descents)
            }
        attempt (CaptureLane.ladder |> List.skipWhile (fun l -> l <> preferred)) []
