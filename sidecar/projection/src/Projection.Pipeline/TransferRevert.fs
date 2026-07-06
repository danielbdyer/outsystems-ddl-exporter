namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Targets.SSDT

/// The transfer leg's COMPENSATING-UNDO (revert) — after a partial/failed load,
/// delete exactly the sink-minted rows by their captured assigned PKs
/// (child-first, so an FK never blocks a delete), from either the materialized
/// in-memory `PackedSurrogateRemap` or the streaming `CaptureJournal` (replayed).
/// Lifted out of the `module Transfer` god-module: a self-contained concern over
/// Catalog / DataLoadPlan / the captured remap. `module Transfer` consumes it as
/// `TransferRevert.*`.
[<RequireQualifiedAccess>]
module TransferRevert =

    /// The provenance header stamped into every revert/undo artifact
    /// (2026-07-06, the wrong-sink guard): the SINK the keys were captured
    /// against, so `projection revert` can refuse a script pointed at a
    /// DIFFERENT database (deleting by key in the wrong environment). A
    /// comment line — harmless to any SQL consumer.
    let artifactHeader (artifactKind: string) (sink: SqlConnection) : string =
        sprintf "-- projection:%s server=%s database=%s generated=%s"
            artifactKind sink.DataSource sink.Database (System.DateTime.UtcNow.ToString "o") // LINT-ALLOW: terminal artifact-header text; DataSource/Database are SqlClient-provided identifiers at this terminal file boundary

    /// Build A — the child-first `DELETE`-by-captured-key revert script for a
    /// failed load. For each `AssignedBySink` kind, in the REVERSE of the
    /// parent-first insert order (children first, so an FK never blocks a delete),
    /// delete the sink-minted rows by their captured assigned PKs. Pre-existing
    /// rows are untouched — only minted keys are targeted — so this is a precise
    /// undo, not a wipe. Empty when nothing was captured. The IN list is chunked to
    /// stay within SQL Server's statement limits; integral keys inline, a
    /// non-integral fallback key renders as an escaped `N'…'` literal.
    let buildRevertScript (catalog: Catalog) (plan: DataLoadPlan) (remap: PackedSurrogateRemap) : string list =
        let assignedByKind = PackedSurrogateRemap.assignedKeysByKind remap
        let renderKey (k: string) : string =
            match System.Int64.TryParse k with
            | true, _ -> k
            | false, _ -> System.String.Concat("N'", k.Replace("'", "''"), "'") // LINT-ALLOW: terminal SQL-literal boundary; k is escaped via single-quote-doubling (the SQL-standard escape), BCL String.Concat is the irreducible primitive at this terminal SQL-text site
        plan.Loads
        |> List.rev
        |> List.collect (fun load ->
            if load.Disposition <> IdentityDisposition.AssignedBySink then []
            else
                match Map.tryFind load.Kind assignedByKind, Catalog.tryFindKind load.Kind catalog with
                | Some (_ :: _ as keys), Some kind ->
                    match kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey && a.IsIdentity) with
                    | Some pk ->
                        let table = Render.tableQualified kind.Physical
                        let pkCol = Render.quote (ColumnRealization.columnNameText pk.Column)
                        keys
                        |> List.chunkBySize 1000
                        |> List.map (fun chunk ->
                            sprintf "DELETE FROM %s WHERE %s IN (%s);" table pkCol (chunk |> List.map renderKey |> String.concat ", ")) // LINT-ALLOW: terminal SQL-text boundary; table/pkCol are validated Render output and each key is pre-escaped, sprintf/String.concat are the irreducible primitives for this chunked DELETE statement
                    | None -> []
                | _ -> [])

    /// Build A — act on the revert script after a failed load: always write it to
    /// the artifact dir when one is configured (the operator's reviewable backstop),
    /// and EXECUTE it when `autoRevert` is set (best-effort per statement — the
    /// artifact remains the fallback if a delete itself fails). No-op on an empty
    /// script. The caller re-raises the original failure afterward: the load failed;
    /// this only ensures the partial sink-minted rows are reverted or scripted.
    let runRevert (sink: SqlConnection) (autoRevert: bool) (artifactDir: string option) (script: string list) : Task<unit> =
        task {
            if not (List.isEmpty script) then
                match artifactDir with
                | Some dir ->
                    try
                        System.IO.Directory.CreateDirectory dir |> ignore
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "transfer-revert.sql"), String.concat "\n" (artifactHeader "transfer-revert" sink :: script)) // LINT-ALLOW: terminal file-write boundary; each script entry is already terminal SQL text, String.concat is the irreducible primitive for newline-joining the artifact
                    with _ -> ()
                | None -> ()
                if autoRevert then
                    for stmt in script do
                        try do! Deploy.executeBatch sink stmt
                        with _ -> ()
        }

    /// D — the STREAMING arm's remap source for the data-leg compensating-undo.
    /// The materialized `writePlan` reverts from an in-memory `PackedSurrogateRemap`;
    /// the estate-scale streaming path's durable record of every sink-minted key is
    /// the off-box `CaptureJournal` (NDJSON, only fully-committed chunks appended —
    /// a crashed chunk is neither journaled nor captured). Replay every journaled
    /// chunk's `(source → assigned)` pairs back into a fresh remap, mapping each
    /// record's root-string `Kind` to the catalog's `SsKey` (the inverse of the
    /// `SsKey.rootOriginal` the journal stores). The per-record capture mirrors
    /// `CaptureJournal.spec`'s Apply (the journal grain's effectful remap fold the
    /// resume path uses); reconstructed here over ALL kinds rather than one at a time.
    let private replayJournalToRemap (catalog: Catalog) (journal: CaptureJournal) : PackedSurrogateRemap =
        let remap = PackedSurrogateRemap.create ()
        let rootToKey =
            Catalog.allKinds catalog
            |> List.map (fun k -> SsKey.rootOriginal k.SsKey, k.SsKey)
            |> Map.ofList
        for KeyValue (_, record) in CaptureJournal.load journal do
            match Map.tryFind record.Kind rootToKey with
            | Some ssKey -> record.Pairs |> Array.iter (fun p -> if p.Length = 2 then PackedSurrogateRemap.capture ssKey p[0] p[1] remap)
            | None -> ()
        remap

    /// D — act on the streaming reverse-leg's compensating-undo after a partial
    /// load: reconstruct the remap from the journal, then run the SAME M23
    /// `buildRevertScript` + `runRevert` the materialized arm runs (only the remap
    /// source differs — journal-replayed vs in-memory). A `None` journal is a safe
    /// no-op: with no journal there are no recorded captures to revert (streaming
    /// execute requires `--journal` anyway, so on a real run `journal` is `Some`).
    let runRevertFromJournal (sink: SqlConnection) (catalog: Catalog) (plan: DataLoadPlan) (journal: CaptureJournal option) (autoRevert: bool) (revertDir: string option) : Task<unit> =
        task {
            match journal with
            | Some j -> do! runRevert sink autoRevert revertDir (buildRevertScript catalog plan (replayJournalToRemap catalog j))
            | None   -> ()
        }
