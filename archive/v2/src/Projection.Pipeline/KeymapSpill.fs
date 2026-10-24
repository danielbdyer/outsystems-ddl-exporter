namespace Projection.Pipeline

// LINT-ALLOW-FILE-MUTATION: BCL SqlCommand surfaces (CommandText, Parameters)
//   and a function-local StringBuilder accumulator while batching `captureMany`'s
//   INSERT; the mutation is contained per call and the values are PARAMETERIZED
//   (`@k`/`@s`/`@a`) — no string-built value injection. The session-table DDL
//   (`createTable`) and the set-based re-point (`repointJoin`) are now typed
//   ScriptDom statements (Tier-2.2; `ScriptDomBuild.buildKeymapSpillTable` /
//   `buildKeymapRepoint`) rendered via `ScriptDomGenerate`; `@kind` stays a bound
//   parameter on the rendered UPDATE.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Targets.SSDT

/// Slice S — the at-scale keymap SPILL (REVERSE_LEG_WORK_PLAN §3 Slice S;
/// DATABASE_ARCHETYPES.md §1 staging / §3 H3). The reverse leg's `AssignedBySink`
/// keymap (source surrogate → sink-minted surrogate) is RESIDENT today
/// (`PackedSurrogateRemap`, ~40 B/entry). The 2026-06-15 sizing proves it FITS:
/// 75 MB for the estate, ~4–8 GB extrapolated to the 200 M production FK-target
/// count — ≪ the 64 GB host. So this is a **completeness + headroom** build,
/// **ARMED BUT INERT**: the resident path is byte-identical until the operator
/// lowers the threshold (or the estate grows past it). When armed, the keymap
/// spills to a session `#`-temp table (temp tables ARE permitted under the
/// DML-only grant — J5 P5) and the phase-2 FK re-point becomes a set-based
/// server-side `UPDATE…JOIN` instead of the resident per-row `tryFind` + UPDATE.

/// Where the keymap lives — resident (the in-RAM `PackedSurrogateRemap`) or
/// spilled (a session `#`-temp table). Closed so the chooser and every consumer
/// are TOTAL over it (a third backend joins by one DU case).
[<RequireQualifiedAccess>]
type KeymapResidence =
    | Resident
    | Spilled

[<RequireQualifiedAccess>]
module KeymapResidence =

    /// Pure + total — choose residence from the configurable RAM threshold (the
    /// maximum keymap assignments to keep resident) and the estimated assignment
    /// count. `None` threshold = unbounded = ALWAYS `Resident` (the inert default,
    /// byte-identical to every reverse leg to date). A `Some n` threshold spills
    /// once the estimate EXCEEDS it. Testable without a connection — the selector
    /// is deterministic from the request alone (the `ReverseLegRealization.choose`
    /// discipline).
    let choose (threshold: int option) (estimatedAssignments: int) : KeymapResidence =
        match threshold with
        | None   -> KeymapResidence.Resident
        | Some n -> if estimatedAssignments > n then KeymapResidence.Spilled else KeymapResidence.Resident

    /// The operator-facing one-line reason, narrated when the spill arms (never a
    /// silent path change — the no-silent-drop discipline applied to the spill).
    let describe (residence: KeymapResidence) (threshold: int option) (estimate: int) : string =
        match residence, threshold with
        | KeymapResidence.Resident, None      -> sprintf "keymap resident (%d assignments; no spill threshold set)" estimate
        | KeymapResidence.Resident, Some n     -> sprintf "keymap resident (%d assignments ≤ threshold %d)" estimate n
        | KeymapResidence.Spilled,  Some n     -> sprintf "keymap SPILLED to a session #-temp table (%d assignments > threshold %d); phase-2 re-point is a server-side UPDATE…JOIN" estimate n
        | KeymapResidence.Spilled,  None       -> sprintf "keymap SPILLED (%d assignments)" estimate

/// The spilled keymap: a session `#`-temp table `(KindKey, SourceKey,
/// AssignedKey)` populated as `AssignedBySink` chunks capture, and a server-side
/// `UPDATE…JOIN` re-point. NVARCHAR keys so the store is TOTAL over the same
/// shapes `PackedSurrogateRemap` handles (the integral fast path AND the
/// non-integral fallback — never a dropped capture on an exotic identity raw).
[<RequireQualifiedAccess>]
module SqlKeymap =

    /// The session keymap table name — a `#`-temp (session-scoped, dropped on
    /// connection close). tempdb rights are implicit for every principal, so the
    /// lane fits the DML-only `grant: data` envelope (the cloud `ManagedDml` sink).
    [<Literal>]
    let TableName = "#projection_keymap_spill"

    /// Create the session keymap table (idempotent within a session: a resumed
    /// run re-uses it). PK `(KindKey, SourceKey)` enforces the keep-first
    /// invariant at insert (the `INSERT…WHERE NOT EXISTS` guard below).
    let createTable (cnn: SqlConnection) : Task<unit> =
        Deploy.executeBatch cnn
            (ScriptDomGenerate.generateOne (ScriptDomBuild.buildKeymapSpillTable TableName "PK_keymap_spill"))

    /// Capture a kind's `(source raw → assigned raw)` pairs into the session
    /// table — KEEP-FIRST on a duplicate source (the `PackedSurrogateRemap`
    /// invariant), realized by the `WHERE NOT EXISTS` guard so a re-captured
    /// source keeps its first binding. Batched + parameterized (the `KindKey` /
    /// keys are VALUES, never string-built into the SQL). A blank source/assigned
    /// is skipped (a NULL key is inserted, never captured — mirrors `capture`).
    let captureMany (cnn: SqlConnection) (kindKey: string) (pairs: (string * string) list) : Task<unit> =
        task {
            let valid = pairs |> List.filter (fun (s, a) -> s <> "" && a <> "")
            for batch in valid |> List.chunkBySize 500 do
                if not (List.isEmpty batch) then
                    use cmd = cnn.CreateCommand()
                    let sb = System.Text.StringBuilder()
                    sb.Append(sprintf "INSERT INTO [%s] (KindKey, SourceKey, AssignedKey) SELECT v.SourceKey0, v.SourceKey1, v.SourceKey2 FROM (VALUES " TableName) |> ignore
                    batch |> List.iteri (fun i (src, asg) ->
                        if i > 0 then sb.Append(",") |> ignore
                        sb.Append(sprintf "(@k%d,@s%d,@a%d)" i i i) |> ignore
                        cmd.Parameters.AddWithValue(sprintf "@k%d" i, box kindKey) |> ignore
                        cmd.Parameters.AddWithValue(sprintf "@s%d" i, box src) |> ignore
                        cmd.Parameters.AddWithValue(sprintf "@a%d" i, box asg) |> ignore)
                    sb.Append(sprintf ") AS v(SourceKey0, SourceKey1, SourceKey2) WHERE NOT EXISTS (SELECT 1 FROM [%s] e WHERE e.KindKey = v.SourceKey0 AND e.SourceKey = v.SourceKey1);" TableName) |> ignore
                    cmd.CommandText <- sb.ToString()
                    let! _ = cmd.ExecuteNonQueryAsync()
                    ()
            return ()
        }

    /// The set-based phase-2 re-point — re-point a sink table's FK column from
    /// the SOURCE surrogate it currently holds to the captured ASSIGNED surrogate
    /// in ONE server-side statement (the at-scale replacement for the resident
    /// per-row `tryFind` + per-row UPDATE). The `CONVERT(NVARCHAR…)` matches the
    /// keymap's text keys to the FK column whatever its physical type; the inner
    /// join leaves an unmatched FK value untouched (the resident path's
    /// no-capture row is likewise left as-is, the named phase-2 erasure).
    let repointJoin (cnn: SqlConnection) (sinkTable: TableId) (kindKey: string) (fkColumn: string) : Task<unit> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                ScriptDomGenerate.generateOne
                    (ScriptDomBuild.buildKeymapRepoint sinkTable fkColumn TableName "@kind")
            cmd.Parameters.AddWithValue("@kind", box kindKey) |> ignore
            let! _ = cmd.ExecuteNonQueryAsync()
            return ()
        }
