module Projection.Cli.Faces.Emit

// The emit faces (bundle / skeleton-only / manifest-only) — extracted from the RunFaces wall (recon #3, the per-verb file split).
// Self-contained: depends only on Pipeline run modules + the shared CLI helpers,
// never a RunFaces-internal helper. Verbatim relocation — zero behavior change.

open System
open System.IO
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Cli
open Projection.Cli.OperatorConsole

let runEmit (shaping: Config.Config) (catalog: Catalog) (outputDir: string) : int =
    let exitCode =
        match Compose.runFromCatalogWith shaping catalog outputDir with
        | Ok paths ->
            printfn "%d artifact(s) written to %s." paths.Length outputDir
            paths
            |> List.iter (fun p ->
                let info = FileInfo p
                printfn "  %s (%d bytes)" p info.Length)
            0
        | Error errors ->
            (
                printErrors Console.Error errors
                2
            )
    dumpBench "emit"
    exitCode

/// Chapter A.4.7' slice ζ — `emit --skeleton-only`. Reads V1 JSON,
/// projects through `RegisteredTransforms.skeletonChainSteps` (the
/// four pure-DataIntent passes), writes the resulting bundle.
/// Operator-intent passes (Selection / Emission / Insertion /
/// Tightening / Ordering overlays) are excluded from the emit; the
/// resulting artifacts are the V2 baseline before any operator
/// opinion lands.
let runEmitSkeletonOnly (catalog: Catalog) (outputDir: string) : int =
    let exitCode =
        match Compose.runSkeletonOnlyFromCatalog catalog outputDir with
        | Ok paths ->
            printfn
                "%d skeleton-only artifact(s) written to %s."
                paths.Length
                outputDir
            paths
            |> List.iter (fun p ->
                let info = FileInfo p
                printfn "  %s (%d bytes)" p info.Length)
            0
        | Error errors ->
            (
                printErrors Console.Error errors
                2
            )
    dumpBench "emit-skeleton-only"
    exitCode

/// `shape: manifest` — the applied-transforms manifest alone. The full
/// (shaped) pass chain runs; only `manifest.json` lands in the out dir.
let runEmitManifestOnly (shaping: Config.Config) (catalog: Catalog) (outputDir: string) : int =
    let exitCode =
        match Compose.runManifestOnlyFromCatalogWith shaping catalog outputDir with
        | Ok paths ->
            printfn "%d manifest artifact(s) written to %s." paths.Length outputDir
            paths
            |> List.iter (fun p ->
                let info = FileInfo p
                printfn "  %s (%d bytes)" p info.Length)
            0
        | Error errors ->
            (
                printErrors Console.Error errors
                2
            )
    dumpBench "emit-manifest-only"
    exitCode
