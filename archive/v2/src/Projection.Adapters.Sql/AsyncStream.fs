namespace Projection.Adapters.Sql

// LINT-ALLOW-FILE-MUTATION: Pull-based streaming primitive
//   (AsyncStream<'a> = unit -> Task<'a option>); each combinator
//   carries function-local mutables (state machines, accumulators)
//   reified behind the abstract pull. Per audit Lens-2 Tier-2
//   (justified + reified).

open System.Threading.Tasks
open Projection.Core

/// Pull-based async stream — V2's minimal streaming abstraction. A
/// `next` thunk returns either the next element or `None` to signal
/// end. Pure data: composes via `map` / `filter` / `iter` /
/// `batchesOf` without materializing intermediate state.
///
/// Per session-34 — Π's output stream is sync (`seq<Statement>`)
/// because Core forbids Task; the streaming readside and bulk
/// realization layer is async. `AsyncStream<'a>` is the carrier on
/// the async side of the pipeline. Statement-stream consumers
/// (`Deploy.executeStream`) bridge sync-to-async via `Seq.iter`
/// inside `task { ... }`.
type AsyncStream<'a> = unit -> Task<'a option>

/// Pull-based streaming combinators. The surface is consumer-justified
/// per the two-consumer threshold: `toList` (the eager drain consumed by
/// `ReadSide` / `Ingestion` / `TransferRun`) and `probe` (the
/// bench-instrumented pass-through consumed by `ReadSide.readRowsStream`).
/// The speculative combinators (`empty`/`ofList`/`map`/`mapAsync`/`iter`/
/// `fold`/`bufferUpTo`/`batchesOf`) were retired 2026-05-30 (Wave-0 slice
/// 0.2) — zero consumers across `src/` + `tests/`; re-introduce per the
/// two-consumer threshold when a real second consumer arrives.
[<RequireQualifiedAccess>]
module AsyncStream =

    /// Pull up to `size` elements; `[]` signals upstream EOF. Re-introduced
    /// 2026-06-10 under the streaming-transfer consumer (the Wave-0 slice
    /// 0.2 retirement's named trigger: a real consumer arrived — the
    /// bounded-memory chunk loop in `TransferRun`'s streaming realization).
    let nextBatch (size: int) (next: AsyncStream<'a>) : Task<'a list> =
        task {
            let acc = ResizeArray<'a>()
            let mutable cont = true
            while cont && acc.Count < size do
                match! next () with
                | Some x -> acc.Add x
                | None -> cont <- false
            return List.ofSeq acc
        }

    let toList (next: AsyncStream<'a>) : Task<'a list> =
        task {
            let acc = ResizeArray<'a>()
            let mutable cont = true
            while cont do
                match! next () with
                | Some x -> acc.Add x
                | None -> cont <- false
            return List.ofSeq acc
        }

    /// Bench-instrumented pass-through. Records `<label>` (wall time
    /// across the full enumeration in ms) and `<label>.elements`
    /// (count) at upstream EOF. Per session-34 — first-class
    /// stream observability for async pipelines.
    let probe (label: string) (next: AsyncStream<'a>) : AsyncStream<'a> =
        let sw = System.Diagnostics.Stopwatch.StartNew()
        let mutable count = 0L
        let mutable finished = false
        fun () ->
            task {
                let! x = next ()
                match x with
                | Some _ ->
                    count <- count + 1L
                    return x
                | None ->
                    if not finished then
                        finished <- true
                        sw.Stop()
                        Bench.recordSample label sw.ElapsedMilliseconds
                        Bench.recordSample (sprintf "%s.elements" label) count
                    return None
            }
