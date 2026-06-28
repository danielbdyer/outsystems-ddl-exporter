module Projection.Cli.Faces.Slice

// The data-portability slice verbs (slice-extract / slice-apply / slice-reset /
// slice-run), extracted from the RunFaces wall (recon #3 — the per-verb file
// split). Self-contained: they depend only on the Pipeline run modules
// (SliceExtractRun / SliceApplyRun / SliceFlowRun), the shared CLI helpers
// (OperatorConsole, CliExit), and the config surface — never on a RunFaces-
// internal helper. `Program.runPlan` dispatches the `PlanAction.RunSlice*` arms
// here (the one dispatcher).

open System
open Projection.Core
open Projection.Pipeline
open Projection.Cli
open Projection.Cli.OperatorConsole

/// `projection slice-extract --source <ref> --root <Entity> [--where <sql>]
/// --out <path>` — extract a use-case-scoped, referentially-closed data slice
/// from a live source and write the portable golden dataset (Slice 3). Read-
/// only against the source; the closure census prints to stderr, and a
/// dangling-mandatory-FK warning flags a slice that is not referentially self-
/// contained (the golden is still written — completeness is gated at apply).
let runSliceExtract (args: string list) : int =
    let arr = List.toArray args
    let flagValue (flag: string) : string option =
        arr
        |> Array.tryFindIndex ((=) flag)
        |> Option.bind (fun i -> if i + 1 < arr.Length then Some arr.[i + 1] else None)
    let usage = "usage: projection slice-extract --source <ref> (--slice <file> | --root <Entity> [--where <sql>]) --out <path>"
    let reportResult (out: string) (result: Result<(string * int) list * int>) : int =
        match result with
        | Ok (census, dangling) ->
            eprintfn "Slice golden written to %s." out
            for (entity, n) in census do eprintfn "  %s: %d row(s)" entity n
            if dangling > 0 then
                eprintfn
                    "  WARNING: %d dangling mandatory FK(s) — the slice is NOT referentially self-contained (gated at apply)."
                    dangling
            0
        | Error errors ->
            printErrors Console.Error errors
            CliExit.classify errors
    match flagValue "--source", flagValue "--out" with
    | Some src, Some out ->
        // Config-driven (`--slice <file>`, the multi-root SliceSpec) takes
        // precedence; else the thin single-root `--root [--where]` form.
        let runner =
            match flagValue "--slice" with
            | Some sliceRef ->
                // A NAMED slice declared in projection.json (the config-primary
                // home) wins; else `--slice` is treated as a file path.
                let named =
                    match ProjectionConfig.fromFile "projection.json" with
                    | Ok cfg   -> Map.tryFind sliceRef cfg.Slices
                    | Error _  -> None
                match named with
                | Some spec -> Some (SliceExtractRun.extractSpec src spec out)
                | None      -> Some (SliceExtractRun.extractSpecFromFile src sliceRef out)
            | None ->
                match flagValue "--root" with
                | Some root -> Some (SliceExtractRun.extract src root (flagValue "--where") out)
                | None      -> None
        match runner with
        | Some t ->
            let code = reportResult out (t.GetAwaiter().GetResult())
            dumpBench "slice-extract"
            code
        | None -> eprintfn "%s" usage; 1
    | _ -> eprintfn "%s" usage; 1

/// `projection slice-apply --golden <p> --target <ref> --out <sql>` (additive
/// capture-and-remap MERGE) and `projection slice-reset … --delete-scope
/// COL=VAL[,...] --allow-drops` (the authoritative scoped DELETE, bounded to
/// the root predicate) — Slice 7. Emits the self-contained DML-only T-SQL
/// load/reset artifact from a golden against the target schema.
let runSliceApply (reset: bool) (args: string list) : int =
    let arr = List.toArray args
    let flagValue (flag: string) : string option =
        arr
        |> Array.tryFindIndex ((=) flag)
        |> Option.bind (fun i -> if i + 1 < arr.Length then Some arr.[i + 1] else None)
    let hasFlag (flag: string) : bool = Array.contains flag arr
    let exitForErrors (errors: ValidationError list) : int =
        printErrors Console.Error errors
        CliExit.classify errors
    match flagValue "--golden", flagValue "--target" with
    | Some golden, Some target ->
        if reset && not (hasFlag "--allow-drops") then
            eprintfn "slice-reset performs a scoped DELETE on the target. Re-run with --allow-drops to acknowledge the loss."
            7
        elif (not reset) && hasFlag "--go" then
            // LIVE additive apply — Execute the capture-and-hoist write (the
            // golden lands in the target; no IDENTITY_INSERT).
            let result = (SliceApplyRun.applyLive target golden true (hasFlag "--allow-cdc")).GetAwaiter().GetResult()
            let code =
                match result with
                | Ok report ->
                    let skipped = List.length report.SkippedReferences
                    eprintfn "Slice applied to %s (live)." target
                    if skipped > 0 then
                        eprintfn "  WARNING: %d reference(s) skipped as unresolved orphans." skipped
                        9
                    else 0
                | Error errors -> exitForErrors errors
            dumpBench "slice-apply"
            code
        else
            // EMIT the self-contained T-SQL artifact (additive or reset) to --out.
            match flagValue "--out" with
            | None ->
                let resetFlags = if reset then "--delete-scope COL=VAL[,...] --allow-drops " else ""
                eprintfn
                    "usage: projection %s --golden <path> --target <ref> %s--out <path>%s"
                    (if reset then "slice-reset" else "slice-apply")
                    resetFlags
                    (if reset then "" else "   (or add --go to apply live)")
                1
            | Some out ->
                let deleteScope : DeleteScopePolicy option =
                    if not reset then None
                    else
                        let terms =
                            match flagValue "--delete-scope" with
                            | Some spec ->
                                spec.Split(',')
                                |> Array.toList
                                |> List.choose (fun kv ->
                                    match kv.Split('=') with
                                    | [| c; v |] -> Some ({ Column = c; Value = v } : DeleteScopeTerm)
                                    | _          -> None)
                            | None -> []
                        Some ({ Terms = terms } : DeleteScopePolicy)
                let result = (SliceApplyRun.applyToFile target golden deleteScope out).GetAwaiter().GetResult()
                let code =
                    match result with
                    | Ok n -> eprintfn "Slice %s artifact written to %s (%d row(s))." (if reset then "reset" else "load") out n; 0
                    | Error errors -> exitForErrors errors
                dumpBench (if reset then "slice-reset" else "slice-apply")
                code
    | _ ->
        eprintfn "usage: projection %s --golden <path> --target <ref> [--out <path> | --go]" (if reset then "slice-reset" else "slice-apply")
        1

/// `projection slice-run <name> [--go]` — run a named extract→apply slice flow
/// from projection.json's `sliceFlows` block (flow-binding). Extract the slice
/// from its source and apply it to its target in ONE command. `--go` lands the
/// rows; otherwise a live preview (extract + plan, no write).
let runSliceFlow (args: string list) : int =
    let arr = List.toArray args
    let hasFlag (flag: string) : bool = Array.contains flag arr
    match arr |> Array.tryFind (fun a -> not (a.StartsWith "-")) with
    | None -> eprintfn "usage: projection slice-run <name> [--go]"; 1
    | Some name ->
        match ProjectionConfig.fromFile "projection.json" with
        | Error es -> printErrors Console.Error es; 6
        | Ok cfg ->
            match Map.tryFind name cfg.SliceFlows with
            | None ->
                eprintfn "unknown slice flow '%s' — declare it in projection.json under \"sliceFlows\"." name
                2
            | Some sf ->
                match Map.tryFind sf.Slice cfg.Slices with
                | None ->
                    eprintfn "slice flow '%s' references unknown slice '%s' (add it to \"slices\")." name sf.Slice
                    2
                | Some spec ->
                    // A sliceFlow endpoint is an ENVIRONMENT NAME (resolved to its
                    // live conn — espace-safe, like flow.from / model.env) or a
                    // conn-ref (env:/file:/live:, passed through verbatim).
                    let resolveEndpoint (s: string) : Result<string> =
                        if s.Contains ":" then Result.success s
                        else
                            match Map.tryFind s cfg.Environments with
                            | Some env ->
                                match env.Access with
                                | Access.Direct r -> Result.success (Command.connSpecOf r)
                                | Access.Bundle (_, Some r) -> Result.success (Command.connSpecOf r)
                                | _ -> Result.failureOf (ValidationError.create "cli.sliceFlow.envNotLive" (sprintf "sliceFlow '%s' endpoint env '%s' has no live connection (use access:direct, or add a `conn` to the bundle environment)." name s))
                            | None -> Result.failureOf (ValidationError.create "cli.sliceFlow.endpointUnknown" (sprintf "sliceFlow '%s' endpoint '%s' is neither a known environment nor a conn-ref (env:/file:/live:)." name s))
                    match resolveEndpoint sf.Source, resolveEndpoint sf.Target with
                    | Error es, _ | _, Error es -> printErrors Console.Error es; dumpBench "slice-run"; 6
                    | Ok srcConn, Ok tgtConn ->
                    let execute = hasFlag "--go"
                    let result =
                        (SliceFlowRun.run srcConn spec tgtConn execute (hasFlag "--allow-cdc")).GetAwaiter().GetResult()
                    let code =
                        match result with
                        | Ok report ->
                            let skipped = List.length report.SkippedReferences
                            eprintfn
                                "Slice flow '%s' %s: %s → %s."
                                name (if execute then "applied" else "previewed") sf.Source sf.Target
                            if skipped > 0 then
                                eprintfn "  WARNING: %d reference(s) skipped as unresolved orphans." skipped
                                9
                            else 0
                        | Error errors ->
                            printErrors Console.Error errors
                            CliExit.classify errors
                    dumpBench "slice-run"
                    code
