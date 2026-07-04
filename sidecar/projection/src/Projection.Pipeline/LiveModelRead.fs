namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql

/// The V1-free **live OSSYS model read** primitive — the shared core behind
/// `ModelResolution` (flow surface) and `Compose.readConfigModel` (full-export
/// surface). The connection-spec decode + open now flow through the one
/// `ConnectionSpec.openSpec` opener (recon #13 — compiled before this), so this
/// module carries no bespoke connection-ref decode of its own.
///
/// Reads the OutSystems model directly from a live OSSYS database: V2's own
/// `MetadataSnapshotRunner` (over V2's carbon-copied rowset SQL) → `RowsetBundle`
/// → `CatalogReader.parse (SnapshotRowsets …)` → `Catalog` with **native GUID
/// SsKey** (A1-stable). No V1 chain, no `osm_model.json`.
[<RequireQualifiedAccess>]
module LiveModelRead =

    /// The rollup envelope's code — the ONE Warn line a model read's divergence
    /// notices condense to on channel 1 (and the live board's notice strip).
    [<Literal>]
    let noticeRollupCode : string = "adapter.ossys.modelRead.noticeRollup"

    let private severityText (s: DiagnosticSeverity) : string =
        match s with
        | DiagnosticSeverity.Info    -> "info"
        | DiagnosticSeverity.Warning -> "warning"
        | DiagnosticSeverity.Error   -> "error"

    let private toNotice (d: DiagnosticEntry) : NoticeSink.Notice =
        { Code = d.Code; Severity = severityText d.Severity; Message = d.Message; Metadata = d.Metadata }

    /// How many underlying FACTS a divergence entry stands for. The nullability
    /// entry is already producer-aggregated (one entry carrying `count` columns
    /// in its metadata); every other entry names one concrete divergence.
    let private factCount (d: DiagnosticEntry) : int =
        match Map.tryFind "count" d.Metadata with
        | Some c ->
            match System.Int32.TryParse c with
            | true, n when n > 0 -> n
            | _ -> 1
        | None -> 1

    /// The rollup payload over a read's divergence entries — pure, so the
    /// constant-size law ("one calm line whether 3 or 3,000 diverge") is
    /// testable without a connection or a console. Family counts are FACT
    /// counts: 180 nullability-divergent columns count as 180, not as the one
    /// producer-aggregated entry that carries them.
    let noticeRollup (artifactPath: string) (entries: DiagnosticEntry list) : Map<string, objnull> =
        let familyOf (d: DiagnosticEntry) : string =
            if d.Code.EndsWith ".nullabilityDivergence" then "nullability"
            elif d.Code.EndsWith ".identityDivergence" then "identity"
            elif d.Code.StartsWith "adapter.ossys.primaryKey" then "primaryKey"
            else "other"
        let counts =
            entries
            |> List.groupBy familyOf
            |> List.map (fun (family, ds) -> family, ds |> List.sumBy factCount)
        let total = counts |> List.sumBy snd
        let samples = entries |> List.truncate 3 |> List.map (fun d -> d.Message)
        Map.ofList
            ([ "total",        box total // LINT-ALLOW: heterogeneous telemetry-metadata Map<string,obj> at the diagnostics boundary; box is the irreducible primitive for the obj-valued map
               "artifactPath", box artifactPath // LINT-ALLOW: heterogeneous telemetry-metadata Map<string,obj> at the diagnostics boundary; box is the irreducible primitive for the obj-valued map
               "samples",      box (String.concat " | " samples) ] // LINT-ALLOW: terminal sample-list join for the notice-rollup metadata Map<string,obj> at the diagnostics boundary; box is the irreducible primitive for the obj-valued map, and String.concat is the terminal display join, no AST applies to a free-text sample preview
             @ (counts |> List.map (fun (family, n) -> family, box n))) // LINT-ALLOW: heterogeneous telemetry-metadata Map<string,obj> at the diagnostics boundary; box is the irreducible primitive for the obj-valued map

    /// Surface a model read's divergence entries (F9 — never silently
    /// discarded; and since 2026-07-02 never a per-item stderr wall over the
    /// live board either): each entry rides channel 1 at Debug (visible under
    /// `-v`/`--debug`), the full detail lands in the run's notice artifact
    /// (`notices/model-read/<runId>.json`, merge-deduped across the run's 2-3
    /// read legs), and ONE Warn rollup envelope names the counts + the path.
    let private surfaceDivergences (entries: DiagnosticEntry list) : unit =
        if not (List.isEmpty entries) then
            for d in entries do
                let payload : Map<string, objnull> =
                    Map.ofList
                        ([ "message", box d.Message ] // LINT-ALLOW: heterogeneous telemetry-metadata Map<string,obj> at the diagnostics boundary; box is the irreducible primitive for the obj-valued map
                         @ (d.Metadata |> Map.toList |> List.map (fun (k, v) -> k, box v))) // LINT-ALLOW: heterogeneous telemetry-metadata Map<string,obj> at the diagnostics boundary; box is the irreducible primitive for the obj-valued map
                LogSink.emit (LogSink.envelope LogSink.Debug LogSink.Extract d.Code payload)
            let artifactPath =
                NoticeSink.runPath (System.IO.Directory.GetCurrentDirectory()) "model-read" (LogSink.runId ())
            let persisted =
                try
                    NoticeSink.append artifactPath (entries |> List.map toNotice) |> ignore
                    LogSink.recordArtifact { Kind = "notices"; Path = artifactPath; SizeBytes = None; FileCount = None }
                    true
                with ex ->
                    // LINT-ALLOW: last-resort stderr — the notice SINK itself
                    // failed, so this warning cannot ride the artifact it is about.
                    eprintfn "  WARNING: failed to persist the notice artifact: %s" ex.Message
                    false
            let path = if persisted then artifactPath else ""
            LogSink.emit (LogSink.envelope LogSink.Warn LogSink.Extract noticeRollupCode (noticeRollup path entries))

    /// Read the model from an already-open OSSYS connection under the
    /// supplied scope parameters: snapshot → bundle → Catalog (native
    /// SsKey). Reconciliation slice 4 (DECISIONS 2026-06-13) — the
    /// scope-bearing face; `SnapshotScopeBinding.fromModel` derives the
    /// parameters from the operator's `model.modules` declaration, and
    /// `ModuleFilter.apply` remains the semantic seam downstream.
    let fromConnectionWith
        (parameters: MetadataSnapshotRunner.SnapshotParameters)
        (cnn: SqlConnection)
        : Task<Result<Catalog>> =
        task {
            // #19-lite (2026-07-02) — the extract leg reports per-rowset
            // progress through the matrix-row-36 seam (`OnRowsetComplete`),
            // so a live board's extract line counts up (`n of 23`) instead of
            // sitting frozen through a long OSSYS read. The callback only
            // emits an envelope — no sleep, no render (the board's drain loop
            // owns pacing).
            let sw = System.Diagnostics.Stopwatch.StartNew()
            let options =
                { MetadataSnapshotRunner.defaultOptions with
                    OnRowsetComplete =
                        fun obs ->
                            LogSink.recordStageProgress "extract"
                                (obs.ResultSetIndex + 1)
                                MetadataSnapshotRunner.ExpectedResultSets
                                sw.ElapsedMilliseconds }
            match! MetadataSnapshotRunner.runAsyncWithOptions cnn parameters options with
            | Error es -> return Result.failure es
            | Ok snapshot ->
                // F9 (audit 2026-06-17) — surface, never silently discard, every
                // logical-vs-deployed `#ColumnReality` divergence the snapshot
                // carries (the adapter keeps the LOGICAL value; the operator is
                // told so they can confirm which source is authoritative), and
                // every attribute-flag-vs-entity-key primary-key contradiction.
                // The carried value is unchanged — diagnostic only, no
                // auto-resolve. Since 2026-07-02 the surface is the notice
                // rollup (one Warn envelope + the detail artifact), never a
                // per-item stderr wall fighting the live board.
                surfaceDivergences
                    (MetadataSnapshotRunner.columnRealityDivergences snapshot
                     @ MetadataSnapshotRunner.primaryKeyDivergences snapshot)
                let bundle = MetadataSnapshotRunner.toBundle snapshot
                // Slice 4 — under a pushed scope, prune reference rows
                // whose target entity the server-side narrowing excluded
                // (the cross-scope edges). `ModuleFilter.apply` applies
                // the SAME semantic in memory (its step 5), which is the
                // pushdown ≡ filter equivalence law. Rows whose
                // `RefEntityId` is unknown (`None`) are kept — a truly
                // dangling one still fails loudly at `Catalog.create`,
                // preserving the corrupt-source posture for full reads.
                let scoped =
                    if List.isEmpty parameters.ModuleNames then bundle
                    else
                        let kindIds =
                            bundle.Kinds
                            |> List.map (fun k -> k.EntityId)
                            |> Set.ofList
                        { bundle with
                            References =
                                bundle.References
                                |> List.filter (fun r ->
                                    match r.RefEntityId with
                                    | Some id -> Set.contains id kindIds
                                    | None    -> true) }
                return! CatalogReader.parse (CatalogReader.SnapshotRowsets scoped)
        }

    /// Read the model from an already-open OSSYS connection: snapshot → bundle
    /// → Catalog (native SsKey). The show-me-everything stance
    /// (`defaultParameters`) — the canary/baseline face.
    let fromConnection (cnn: SqlConnection) : Task<Result<Catalog>> =
        fromConnectionWith MetadataSnapshotRunner.defaultParameters cnn

    /// Read the model live from a connection spec under the supplied scope
    /// parameters: open (Source role, through the one `ConnectionSpec.openSpec`)
    /// → `fromConnectionWith`. Accepts every spec form uniformly (`env:` /
    /// `file:` / `live:` / bare — recon #13, D9 amended 2026-06-28); `env:` /
    /// `file:` remain the recommended out-of-band form.
    let fromConnSpecWith
        (parameters: MetadataSnapshotRunner.SnapshotParameters)
        (connSpec: string)
        : Task<Result<Catalog>> =
        task {
            match! ConnectionSpec.openSpec SubstrateRole.Source "ossys-model-source" connSpec with
            | Error es -> return Result.failure es
            | Ok cnn ->
                use cnn = cnn
                return! fromConnectionWith parameters cnn
        }

    /// Read the model live from a connection reference: parse → open (Source
    /// role) → `fromConnection`.
    let fromConnSpec (connSpec: string) : Task<Result<Catalog>> =
        fromConnSpecWith MetadataSnapshotRunner.defaultParameters connSpec
