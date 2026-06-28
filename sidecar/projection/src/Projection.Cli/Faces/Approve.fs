module Projection.Cli.Faces.Approve

// The seal/approve face (record a policy-version sign-off, R6) — extracted from the RunFaces wall (recon #3, the per-verb file split).
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

/// H-086 / Wave-3 slice 3.2: `projection approve <policyVersion> --approver
/// <name> [--rationale <text>] [--store <path>]`. Creates an `ApprovalRecord`
/// for the given policy version and prints it. When `--store <path>` is given,
/// the record is *persisted* (load → `ApprovalRegistry.record` → save) so R6
/// operator sign-off is recorded and consultable across runs — closing the
/// prior "constructs and discards" gap. Without `--store`, prints only
/// (backward-compatible). The policy version string is the hex SHA-256 digest
/// from `VersionedPolicy.digestOf` (or any opaque version the operator tracks).
let runApprove
    (policyVersion: string)
    (approver: string)
    (rationale: string option)
    (store: string option)
    : int =
    // Slice 0 (2026-06-02): Core retired `*Now` wrappers; CLI is the
    // boundary that supplies `DateTimeOffset.UtcNow` per the Episode.fs
    // canonical "boundary-supplied at" pattern. One reading of UtcNow
    // shared across both transitions for a consistent record.
    let now = System.DateTimeOffset.UtcNow
    let record =
        ApprovalWorkflow.pending now policyVersion
        |> ApprovalWorkflow.approve approver rationale now
    printfn "Policy version %s approved." policyVersion
    printfn "  approver  : %s" approver
    printfn "  decision  : Approved"
    printfn "  at        : %s" (record.At.ToString "o")
    match rationale with
    | Some r -> printfn "  rationale : %s" r
    | None   -> ()
    let persisted =
        match store with
        | None -> Ok ()
        | Some path ->
            match ApprovalStore.load path with
            | Error e -> Error (ApprovalStore.describe e)
            | Ok registry ->
                match ApprovalStore.save path (ApprovalRegistry.record record registry) with
                | Ok () -> printfn "  store     : %s (recorded)" path; Ok ()
                | Error e -> Error (ApprovalStore.describe e)
    match persisted with
    | Ok () -> dumpBench "approve"; 0
    | Error msg ->
        Console.Error.WriteLine(sprintf "projection approve: store failed: %s" msg)
        6

