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

/// Pull-based streaming combinators. All operations are zero-buffer
/// pass-through except where naming says otherwise (`bufferUpTo`,
/// `toList`, `batchesOf`).
[<RequireQualifiedAccess>]
module AsyncStream =

    let empty<'a> : AsyncStream<'a> = fun () -> Task.FromResult None

    let ofList (xs: 'a list) : AsyncStream<'a> =
        let mutable remaining = xs
        fun () ->
            task {
                match remaining with
                | [] -> return None
                | x :: rest ->
                    remaining <- rest
                    return Some x
            }

    let map (f: 'a -> 'b) (next: AsyncStream<'a>) : AsyncStream<'b> =
        fun () ->
            task {
                let! x = next ()
                return Option.map f x
            }

    let mapAsync (f: 'a -> Task<'b>) (next: AsyncStream<'a>) : AsyncStream<'b> =
        fun () ->
            task {
                match! next () with
                | None -> return None
                | Some x ->
                    let! y = f x
                    return Some y
            }

    let iter (f: 'a -> Task<unit>) (next: AsyncStream<'a>) : Task<unit> =
        task {
            let mutable cont = true
            while cont do
                match! next () with
                | Some x -> do! f x
                | None -> cont <- false
        }

    let fold (f: 's -> 'a -> Task<'s>) (seed: 's) (next: AsyncStream<'a>) : Task<'s> =
        task {
            let mutable state = seed
            let mutable cont = true
            while cont do
                match! next () with
                | Some x ->
                    let! state' = f state x
                    state <- state'
                | None -> cont <- false
            return state
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

    /// Buffer up to `cap` elements. Returns `Some list` if the
    /// upstream produced ≤ cap; `None` if it overflowed (used as
    /// the "small enough to materialize" gate). Drains the stream
    /// either way to keep the underlying reader's lifetime tidy.
    let bufferUpTo (cap: int) (next: AsyncStream<'a>) : Task<'a list option> =
        task {
            let acc = ResizeArray<'a>()
            let mutable cont = true
            let mutable overflowed = false
            while cont do
                match! next () with
                | Some x ->
                    if acc.Count >= cap then
                        overflowed <- true
                    else
                        acc.Add x
                | None -> cont <- false
            return if overflowed then None else Some (List.ofSeq acc)
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

    /// Group consecutive elements into batches of up to `n`.
    /// Each pull yields the next batch (`'a list`) or `None` at
    /// upstream end. Used by `Deploy.executeStream` to fold
    /// `InsertRow` runs into `SqlBulkCopy` chunks without breaking
    /// the streaming flow.
    let batchesOf (n: int) (next: AsyncStream<'a>) : AsyncStream<'a list> =
        if n <= 0 then invalidArg "n" "batch size must be positive"
        let mutable upstreamDone = false
        fun () ->
            task {
                if upstreamDone then return None
                else
                    let buf = ResizeArray<'a>()
                    let mutable cont = true
                    while cont && buf.Count < n do
                        match! next () with
                        | Some x -> buf.Add x
                        | None ->
                            upstreamDone <- true
                            cont <- false
                    if buf.Count = 0 then return None
                    else return Some (List.ofSeq buf)
            }
