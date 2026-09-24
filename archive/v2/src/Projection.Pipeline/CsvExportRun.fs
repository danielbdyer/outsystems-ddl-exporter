namespace Projection.Pipeline

// LINT-ALLOW-FILE-MUTATION: sealed function-local mutable accumulators at the
//   ingest/write seams (the SliceExtractRun shape); returned immutably.

open System.Threading.Tasks
open Projection.Core
open Projection.Adapters.Sql
open Projection.Adapters.OssysSql
open Projection.Targets.Data

/// `projection <flow>` onto a csv destination (2026-07-10, the csv-destination
/// program) — the read-only CSV DATA EXPORT. Reads the SOURCE contract from
/// the OSSYS metamodel (the peer face's identity basis — never a ReadSide
/// readback, whose everything-is-Static marking would poison the export's
/// static-skip; survival rule 8), resolves the declared table subset, ingests
/// the subset's rows in dependency order, OPTIONALLY closes over the rows the
/// subset's foreign keys reference (transitively, static kinds held out —
/// `CsvReferencedPull`), and writes one CSV per table plus
/// `export-manifest.json` through the `CsvExport` renderer. Read-only against
/// the source; no database is ever written.
[<RequireQualifiedAccess>]
module CsvExportRun =

    /// One exported table, as the face narrates it.
    type TableCensus =
        { Module        : string
          Entity        : string
          PhysicalTable : string
          FileName      : string
          RowCount      : int
          Provenance    : CsvExport.Provenance }

    /// The export's report: the census (sorted module, entity), the manifest
    /// path, and — when references escape the subset and the pull is OFF —
    /// the narration lines naming them. Escapes never refuse an export; the
    /// face prints them with the `withReferenced` remedy.
    type ExportReport =
        { Tables       : TableCensus list
          ManifestPath : string
          EscapeLines  : string list }

    /// Everything the write loop needs per table, resolved pure before any
    /// file IO (survival rule 5: the task body stays a flat spine).
    type private TableWrite =
        { Kind       : Kind
          ModuleName : Name
          Rows       : StaticRow list
          Provenance : CsvExport.Provenance }

    let private censusOf (outDir: string) (w: TableWrite) : TableCensus =
        { Module        = Name.value w.ModuleName
          Entity        = Name.value w.Kind.Name
          PhysicalTable = TableId.tableText w.Kind.Physical
          FileName      = System.IO.Path.Combine(outDir, CsvExport.fileNameFor w.Kind)
          RowCount      = List.length w.Rows
          Provenance    = w.Provenance }

    /// Assemble the per-table writes from the closed row sets — pure. Order:
    /// (module, entity), the manifest's own order.
    let private writesOf
        (contract: Catalog)
        (declared: Set<SsKey>)
        (rows: Map<SsKey, StaticRow list>)
        : TableWrite list =
        let moduleOf : Map<SsKey, Name> =
            Catalog.allModulesKinds contract
            |> List.map (fun (m, k) -> k.SsKey, m.Name)
            |> Map.ofList
        rows
        |> Map.toList
        |> List.choose (fun (key, kindRows) ->
            Catalog.tryFindKind key contract
            |> Option.map (fun kind ->
                { Kind       = kind
                  ModuleName = moduleOf |> Map.tryFind key |> Option.defaultValue kind.Name
                  Rows       = kindRows
                  Provenance =
                    if Set.contains key declared then CsvExport.Provenance.Declared
                    else CsvExport.Provenance.Referenced }))
        |> List.sortBy (fun w -> Name.value w.ModuleName, Name.value w.Kind.Name)

    /// Run the export. `tables` is the flow's declared subset (empty = the
    /// whole modeled estate); `withReferenced` turns the closure pull on.
    let export
        (contractScope: MetadataSnapshotRunner.SnapshotParameters)
        (sourceSpec: string)
        (outDir: string)
        (tables: string list)
        (withReferenced: bool)
        : Task<Result<ExportReport>> =
        task {
            match! Source.read (Source.ofOssysWith contractScope sourceSpec) with
            | Error es -> return Result.failure es
            | Ok contract ->
                match TransferSubset.resolveLoadSet contract tables with
                | Error es -> return Result.failure es
                | Ok loadSet ->
                    // The effective subset: the declared kinds, or (no
                    // `tables`) every modeled kind.
                    let declared =
                        match loadSet with
                        | Some s -> s
                        | None   -> Catalog.allKinds contract |> List.map (fun k -> k.SsKey) |> Set.ofList
                    // Escaping references, narrated only when the pull is OFF
                    // (ON means they are about to be carried).
                    let escapeLines =
                        if withReferenced then []
                        else
                            match loadSet with
                            | None -> []
                            | Some s -> PeerTransfer.narrateEscapes (PeerTransfer.escapingFks contract s Set.empty)
                    let statics = CsvReferencedPull.staticKinds contract
                    match! ConnectionSpec.openSpec SubstrateRole.Source "csv-export-source" sourceSpec with
                    | Error es -> return Result.failure es
                    | Ok cnn ->
                        use cnn = cnn
                        // Ingest the declared subset in dependency order —
                        // whole tables, the same read the transfer leg does.
                        // Cycles are irrelevant for a read-only export, so the
                        // degraded (alphabetical) order is equally fine and no
                        // order gate applies.
                        let scope = TransferScope.create contract loadSet Set.empty
                        let topo = TransferScope.topology TreatAsCycle scope contract
                        let! ingested = Ingestion.collectInOrderFor scope.WriteKinds cnn contract topo
                        // The optional referenced pull: the subset's own rows
                        // seed the closure; in-subset references resolve
                        // against them, so only escaping edges fetch; static
                        // kinds are held out at the fetch filter.
                        let! closedR =
                            if withReferenced then
                                ClosureOracle.walkWhere (CsvReferencedPull.keepFetch statics) cnn contract [] (CsvReferencedPull.rootsOf ingested)
                            else
                                Task.FromResult (Result.success Closure.empty)
                        match closedR with
                        | Error es -> return Result.failure es
                        | Ok closedState ->
                            let allRows =
                                if withReferenced then Closure.materialize closedState
                                else ingested
                            let writes = writesOf contract declared allRows
                            let manifestPath = System.IO.Path.Combine(outDir, "export-manifest.json")
                            try
                                System.IO.Directory.CreateDirectory outDir |> ignore
                                for w in writes do
                                    System.IO.File.WriteAllText(
                                        System.IO.Path.Combine(outDir, CsvExport.fileNameFor w.Kind),
                                        CsvExport.tableCsv w.Kind w.Rows,
                                        System.Text.UTF8Encoding(false))
                                let manifest =
                                    writes
                                    |> List.map (fun w -> CsvExport.manifestEntry w.ModuleName w.Kind (List.length w.Rows) w.Provenance)
                                    |> CsvExport.manifestJson
                                System.IO.File.WriteAllText(manifestPath, manifest, System.Text.UTF8Encoding(false))
                                return
                                    Result.success
                                        { Tables       = writes |> List.map (censusOf outDir)
                                          ManifestPath = manifestPath
                                          EscapeLines  = escapeLines }
                            with ex ->
                                return Result.failureOf (ValidationError.create "csv.export.writeFailed" ex.Message)
        }
