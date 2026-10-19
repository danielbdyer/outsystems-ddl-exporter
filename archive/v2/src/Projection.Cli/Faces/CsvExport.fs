module Projection.Cli.Faces.CsvExport

// LINT-ALLOW-FILE: the csv-export face composes operator-facing narration
//   (the per-table census, the escaping-reference remedy) at the CLI's
//   terminal print boundary — the posture every face file holds.

open System
open Projection.Core
open Projection.Adapters.OssysSql
open Projection.Pipeline
open Projection.Cli
open Projection.Cli.OperatorConsole
open Projection.Cli.Faces.Common

/// The csv-export face (2026-07-10, the csv-destination program): drive
/// `CsvExportRun.export` and narrate the outcome in THE_VOICE register —
/// what was written, where, how many rows, and which tables arrived by
/// declaration versus by reference. A refusal routes through the shared
/// gate surface (`Preflight.refusalOf`), never a bare error wall.
let runCsvExport
    (contractScope: MetadataSnapshotRunner.SnapshotParameters)
    (sourceSpec: string)
    (outDir: string)
    // The flow's resolved options — the export consumes `Tables`; every other
    // axis is inert here by construction (a csv export has no write seam).
    (opts: LoadOpts)
    (withReferenced: bool)
    : int =
    Face.run "csv-export" (fun () ->
        match (CsvExportRun.export contractScope sourceSpec outDir opts.Tables withReferenced).GetAwaiter().GetResult() with
        | Error errors ->
            TtyRenderer.renderGate "projection (csv export)" (Preflight.refusalOf errors)
            (Preflight.refusalOf errors).ExitCode
        | Ok report ->
            let declared, referenced =
                report.Tables |> List.partition (fun t -> t.Provenance = Projection.Targets.Data.CsvExport.Provenance.Declared)
            for t in report.Tables do
                let provenance =
                    match t.Provenance with
                    | Projection.Targets.Data.CsvExport.Provenance.Declared   -> "declared in the flow's tables"
                    | Projection.Targets.Data.CsvExport.Provenance.Referenced -> "pulled in by a reference from the exported set"
                printfn "Wrote %d row(s) of %s.%s to %s (%s)." t.RowCount t.Module t.Entity t.FileName provenance
            printfn ""
            (match referenced with
             | [] -> ()
             | rs ->
                 printfn "%d table(s) arrived by reference: rows the exported set points at, followed transitively; static reference tables are excluded — their content is identical in every environment." rs.Length
                 printfn "")
            // Escaping references with the pull OFF: name each one and the
            // exact lever that carries it — never a refusal (nothing here is
            // destructive), but never silent either.
            (match report.EscapeLines with
             | [] -> ()
             | lines ->
                 printfn "%d relationship(s) point at tables outside the exported set — those foreign-key values will not resolve inside these files:" lines.Length
                 for line in lines do printfn "  %s" line
                 printfn "To include the referenced rows (followed transitively; static reference tables excluded), set \"withReferenced\": true on the flow, or run once with --with-referenced."
                 printfn "")
            printfn "The column mapping (physical name to Service Studio name), the row counts, and each table's provenance are recorded in %s." report.ManifestPath
            printfn "One caveat to read the files with: a database NULL and an empty text value are both written as an empty field — the source read collapses them before any file is composed."
            printfn "Exported %d table(s): %d declared, %d by reference. Nothing was written to any database." report.Tables.Length declared.Length referenced.Length
            0)
