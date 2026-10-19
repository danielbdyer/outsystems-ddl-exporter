namespace Projection.Adapters.Sql

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// The reader leg of the Transfer adjunction — `Projection`'s named peer.
/// Lifts a Source substrate's rows back into `StaticRow`s, per kind, in
/// FK-safe (topological) order: the order a two-phase load consumes them.
/// A thin composition over `ReadSide.readRowsStream` in the Transfer
/// vocabulary. Slice B of the Transfer epic — see `PRESCOPE_TRANSFER.md`
/// §9 seam 2, §10.
[<RequireQualifiedAccess>]
module Ingestion =

    /// Stream one kind's rows from the Source connection (the row-reader
    /// leg, named in Transfer vocabulary). Quanta are positional against
    /// `Kind.rowBasis kind` (Q2 — the in-flight carrier).
    let streamKind (cnn: SqlConnection) (kind: Kind) : AsyncStream<RowQuantum> =
        ReadSide.readRowsStream cnn kind

    /// Stream one kind's rows rebuilt at the IR grain (`StaticRow`, Map +
    /// `READSIDE_ROW` identity minted per row) — the materialized-scale
    /// boundary for consumers that hold whole row sets (reconcile reads,
    /// preview/canary collection). The streaming realization consumes
    /// `streamKind` directly and never pays this conversion.
    let streamKindRows (cnn: SqlConnection) (kind: Kind) : AsyncStream<StaticRow> =
        streamKind cnn kind |> ReadSide.materializeStream kind

    // `streamsInOrder` (per-kind streams in topological order) was deleted
    // here (Q3, 2026-06-12): its single consumer was `collectInOrder`, which
    // now converts at the IR-grain boundary directly, and the streaming
    // realization streams per kind via `streamKind` inside its own
    // chunk loop — zero consumers remained (the dead-algebra precedent,
    // DECISIONS 2026-06-04). Re-introduce per the two-consumer threshold.

    /// Materialize every kind's rows into the `Map<SsKey, StaticRow list>`
    /// that the pure `TransferPlan.build` consumes — the materialized
    /// path's SINGLE conversion point back to the IR grain (Q2: Map +
    /// Identifier minted here, via `streamKindRows`). Reads each kind in
    /// topological order, one open reader at a time (Source-friendly). For
    /// preview / canary scale; the streaming realization consumes
    /// `streamsInOrder` directly without materializing.
    /// Materialize the rows of a SCOPED set of kinds (`owned`) into the
    /// `Map<SsKey, StaticRow list>` shape `DataLoadPlan.build` consumes, in
    /// topological order. The scope is the kinds the caller owns — the
    /// full-export hydration passes its static-marked kind set so it streams
    /// ONLY those (never the mark-everything `ReadSide.read`; survival rule
    /// 8). A key absent from `owned` (or from the catalog) is skipped.
    let collectInOrderFor
        (owned: Set<SsKey>)
        (cnn: SqlConnection)
        (catalog: Catalog)
        (topo: TopologicalOrder)
        : Task<Map<SsKey, StaticRow list>> =
        // Tail-recursive task continuation rather than `for … do let! …` over a
        // mutable accumulator: the loop-with-`let!` shape is not statically
        // compilable under Release optimization (FS3511 → the dynamic, slower
        // state machine), so it is restructured into the statically-compilable
        // recursive form (the codebase's standing posture on FS3511; cf.
        // `Pipeline.runWithConfigCore` / `Preflight`).
        let rec loop
            (acc: Map<SsKey, StaticRow list>)
            (remaining: (SsKey * AsyncStream<StaticRow>) list)
            : Task<Map<SsKey, StaticRow list>> =
            task {
                match remaining with
                | [] -> return acc
                | (key, stream) :: rest ->
                    let! rows = AsyncStream.toList stream
                    return! loop (Map.add key rows acc) rest
            }
        let rowStreams =
            topo.Order
            |> List.choose (fun key ->
                if Set.contains key owned then
                    Catalog.tryFindKind key catalog
                    |> Option.map (fun k -> key, streamKindRows cnn k)
                else None)
        loop Map.empty rowStreams

    let collectInOrder
        (cnn: SqlConnection)
        (catalog: Catalog)
        (topo: TopologicalOrder)
        : Task<Map<SsKey, StaticRow list>> =
        collectInOrderFor (Set.ofList topo.Order) cnn catalog topo

    /// Drain ONE kind's rows on its own connection, gated by the shared
    /// semaphore. Hoisted to module level (never `let rec`/local closures
    /// inside a Release `task { }` — FS3511). Records the actual per-table
    /// row-drain cost — `ingestion.rowDrain` (+ the per-table label) and
    /// `ingestion.rowDrain.rows` — as distinct from the stream-LIFETIME
    /// labels `readside.readRowsStream.*` accumulate (under queuing, a
    /// stream's lifetime includes gate wait; the drain label is the
    /// SQL/read/materialization cost alone).
    /// The connection-owning drain of ONE kind's stream (open → stream →
    /// dispose), generic over the row grain (`streamOf` selects the
    /// carrier: `streamKindRows` for the IR grain, `streamKind` for the
    /// positional quantum grain). Hoisted from the gated wrapper so the
    /// connection's `use` scope closes BEFORE any drain-time projection
    /// runs — a projection computes over materialized rows and must not
    /// hold a pooled connection hostage while it does.
    let private drainKindRows
        (streamOf: SqlConnection -> Kind -> AsyncStream<'row>)
        (openConnection: unit -> Task<Result<SqlConnection>>)
        (kind: Kind)
        : Task<Result<'row list>> =
        task {
            let swOpen = System.Diagnostics.Stopwatch.StartNew()
            match! openConnection () with
            | Error es -> return Result.failure es
            | Ok cnn ->
                swOpen.Stop()
                Bench.recordSample "ingestion.rowDrain.connectionOpen" swOpen.ElapsedMilliseconds
                use cnn = cnn
                let sw = System.Diagnostics.Stopwatch.StartNew()
                let! rows = AsyncStream.toList (streamOf cnn kind)
                sw.Stop()
                Bench.recordSample "ingestion.rowDrain" sw.ElapsedMilliseconds
                Bench.recordSample
                    (System.String.Concat("ingestion.rowDrain.", TableId.schemaText kind.Physical, ".", TableId.tableText kind.Physical))  // LINT-ALLOW: terminal Bench telemetry-label composition (per-table drain label); a label IS a string primitive
                    sw.ElapsedMilliseconds
                Bench.recordSample "ingestion.rowDrain.rows" (int64 (List.length rows))
                return Result.success rows
        }

    let private drainKindGatedWith
        (streamOf: SqlConnection -> Kind -> AsyncStream<'row>)
        (project: Kind -> 'row list -> 'r)
        (gate: System.Threading.SemaphoreSlim)
        (openConnection: unit -> Task<Result<SqlConnection>>)
        (key: SsKey)
        (kind: Kind)
        : Task<Result<SsKey * 'r>> =
        task {
            // Phase labels (the four-phase attribution): gate wait and
            // connection open are OUTSIDE the drain stopwatch, so queue
            // pressure under contention and pool/handshake cost are each
            // separately visible — a 0 sample means uncontended/pooled,
            // not unmeasured. The drain-time projection runs INSIDE the
            // gate (its cost bounds the slot — that is the memory
            // discipline: at most `concurrency` kinds' rows are live at
            // once) but AFTER the connection is returned to the pool
            // (`drainKindRows`' use-scope has closed), so a CPU-heavy
            // projection never starves the pool of a physical connection.
            let swGate = System.Diagnostics.Stopwatch.StartNew()
            do! gate.WaitAsync()
            swGate.Stop()
            Bench.recordSample "ingestion.rowDrain.gateWait" swGate.ElapsedMilliseconds
            try
                match! drainKindRows streamOf openConnection kind with
                | Error es -> return Result.failure es
                | Ok rows ->
                    let swProject = System.Diagnostics.Stopwatch.StartNew()
                    let projected = project kind rows
                    swProject.Stop()
                    Bench.recordSample "ingestion.rowDrain.project" swProject.ElapsedMilliseconds
                    return Result.success (key, projected)
            finally
                gate.Release() |> ignore
        }

    /// The PROJECTED bounded-parallel drain — `collectInOrderForConcurrent`
    /// generalized over what each kind's landed rows become. `project` runs
    /// on the drain worker the moment its kind's rows materialize (inside
    /// the concurrency gate, after the connection is pooled back), so a
    /// per-kind pure computation — a MERGE render, an evidence-cache
    /// derivation — OVERLAPS the remaining kinds' wire time instead of
    /// waiting for the whole estate. Acquisition-only concurrency still
    /// holds: the result `Map` is key-ordered and each projection sees only
    /// its own kind's rows, so nothing downstream can observe completion
    /// order. A projection that retains only its result (dropping the rows)
    /// caps live row memory at `concurrency` kinds rather than the estate.
    let private collectForConcurrentWithCore
        (streamOf: SqlConnection -> Kind -> AsyncStream<'row>)
        (project: Kind -> 'row list -> 'r)
        (concurrency: int)
        (openConnection: unit -> Task<Result<SqlConnection>>)
        (owned: Set<SsKey>)
        (catalog: Catalog)
        (topo: TopologicalOrder)
        : Task<Result<Map<SsKey, 'r>>> =
        task {
            use _ = Bench.scope "ingestion.collect.concurrent"
            let capped = max 1 concurrency
            Bench.recordSample "ingestion.collect.concurrency" (int64 capped)
            let kinds =
                topo.Order
                |> List.choose (fun key ->
                    if Set.contains key owned then
                        Catalog.tryFindKind key catalog |> Option.map (fun k -> key, k)
                    else None)
            use gate = new System.Threading.SemaphoreSlim(capped, capped)
            let drains =
                kinds |> List.map (fun (key, k) -> drainKindGatedWith streamOf project gate openConnection key k)
            let! results = Task.WhenAll(Array.ofList drains)
            return
                results
                |> Array.toList
                |> Result.aggregate
                |> Result.map Map.ofList
        }

    let collectInOrderForConcurrentWith
        (project: Kind -> StaticRow list -> 'r)
        (concurrency: int)
        (openConnection: unit -> Task<Result<SqlConnection>>)
        (owned: Set<SsKey>)
        (catalog: Catalog)
        (topo: TopologicalOrder)
        : Task<Result<Map<SsKey, 'r>>> =
        collectForConcurrentWithCore streamKindRows project concurrency openConnection owned catalog topo

    /// The QUANTUM-grain projected drain — `collectInOrderForConcurrentWith`
    /// over the slim positional carrier (`streamKind`; no per-row Map mint,
    /// no row-identity synthesis — the measured 3.35× drain-side carrier
    /// tax stays unpaid). For projections that consume cells positionally
    /// (`EvidenceCache.cachedKindOfQuanta`,
    /// `StaticSeedsEmitter.renderQuanta`); a projection needing the IR
    /// grain takes the named-row sibling instead.
    let collectQuantaForConcurrentWith
        (project: Kind -> RowQuantum list -> 'r)
        (concurrency: int)
        (openConnection: unit -> Task<Result<SqlConnection>>)
        (owned: Set<SsKey>)
        (catalog: Catalog)
        (topo: TopologicalOrder)
        : Task<Result<Map<SsKey, 'r>>> =
        collectForConcurrentWithCore streamKind project concurrency openConnection owned catalog topo

    /// Bounded-parallel sibling of `collectInOrderFor` — the operator's
    /// `emission.dataReadConcurrency` knob. Each owned kind drains its row
    /// stream on its OWN short-lived connection (`openConnection`; SqlClient
    /// pooling reuses physical connections underneath), at most `concurrency`
    /// in flight. **Acquisition-only concurrency**: the result is the same
    /// keyed `Map` the serial form produces (a `Map` is key-ordered; per-kind
    /// row order is the reader's PK order on a single stream) — topological /
    /// dependency order still governs the rendered load plan downstream, so
    /// emitted semantics never depend on completion order. Any per-kind open
    /// or read failure fails the whole collection loudly. The identity
    /// projection of `collectInOrderForConcurrentWith`.
    let collectInOrderForConcurrent
        (concurrency: int)
        (openConnection: unit -> Task<Result<SqlConnection>>)
        (owned: Set<SsKey>)
        (catalog: Catalog)
        (topo: TopologicalOrder)
        : Task<Result<Map<SsKey, StaticRow list>>> =
        collectInOrderForConcurrentWith (fun _ rows -> rows) concurrency openConnection owned catalog topo

    /// Registry metadata (pillar 9). The ingestion adapter leg classifies
    /// entirely as `DataIntent` — lifting a substrate's rows is observation,
    /// not operator opinion (mirrors the OSSYS `CatalogReader` adapter).
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.adapter "transferIngestion" Data
            [ TransformSite.dataIntent "rowStreamRead"
                "Lift each kind's rows from the Source substrate via ReadSide.readRowsStream, mapping columns positionally onto attribute Names. Observation only — the rows are what the Source holds; no operator opinion enters."
              TransformSite.dataIntent "topologicalStreamOrder"
                "Stream kinds in the precomputed TopologicalOrder (FK-safe, dependency-first) so a two-phase load consumes them in order. The order derives from the catalog's FK graph; no operator-supplied ordering at this site." ]
