namespace Projection.Pipeline

open System.Threading.Tasks
open Projection.Core

/// `projection slice-run <name>` — the complete cross-environment data-
/// portability recipe (flow-binding). Extract the named slice from the SOURCE
/// (closure walk → portable golden) and APPLY it to the TARGET, in one command.
/// Chains the proven `SliceExtractRun.extractSpec` → `SliceApplyRun.applyLive`
/// through a temporary golden (the same artifact the standalone verbs produce).
/// `execute = true` (`--go`) lands the rows; otherwise it is a live preview
/// (extract + plan, no write). The whole recipe lives in projection.json's
/// `sliceFlows` block; this just runs it.
[<RequireQualifiedAccess>]
module SliceFlowRun =

    let run
        (sourceSpec: string)
        (sliceSpec: SliceSpec)
        (targetSpec: string)
        (execute: bool)
        (allowCdc: bool)
        : Task<Result<Transfer.TransferReport>> =
        task {
            let tempGolden =
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName())
            let cleanup () = try System.IO.File.Delete tempGolden with _ -> ()
            let! extractR = SliceExtractRun.extractSpec sourceSpec sliceSpec tempGolden
            match extractR with
            | Error es ->
                cleanup ()
                return Error es
            | Ok _ ->
                let! applyR = SliceApplyRun.applyLive targetSpec tempGolden execute allowCdc
                cleanup ()
                return applyR
        }
