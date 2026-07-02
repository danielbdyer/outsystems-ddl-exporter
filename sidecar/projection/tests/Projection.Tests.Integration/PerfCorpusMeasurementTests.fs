namespace Projection.Tests

// ---------------------------------------------------------------------------
// SINGLE_SCAN_PROGRAM measurement corpus — ~480k rows / 240 variegated
// tables, deterministic. Env-gated (`PERF_CORPUS=1`): sized to "hurt a
// little when we're wrong" — it never runs in the normal docker pool or the
// perf-gate. Every optimization leg asserts VALUE IDENTITY against the
// incumbent path before its timing counts. Protocol + running results:
// SINGLE_SCAN_PROGRAM.md.
// ---------------------------------------------------------------------------

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Xunit.Abstractions
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Sql
open Projection.Pipeline

module private PerfCorpus =

    let mustOk r = match r with Ok v -> v | Error es -> failwithf "corpus: %A" es
    let mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_PERF_CORPUS" parts |> mustOk
    let mkName (s: string) : Name = Name.create s |> mustOk

    let enabled () : bool =
        System.Environment.GetEnvironmentVariable "PERF_CORPUS" = "1"

    let skipUnlessEnabled (label: string) : bool =
        if not (enabled ()) then
            printfn "SKIP %s: set PERF_CORPUS=1 to run the ~480k-row measurement corpus." label
            false
        elif Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    // -- shape families -----------------------------------------------------

    type Family = { Prefix: string; Count: int; Rows: int }
    let families =
        [ { Prefix = "NAR"; Count = 200; Rows =   800 }
          { Prefix = "MED"; Count =  30; Rows =  4000 }
          { Prefix = "WID"; Count =  10; Rows = 20000 } ]

    let tableName (fam: Family) (i: int) : string = sprintf "PC_%s_%03d" fam.Prefix i

    /// Deterministic NULL: every 7th nullable cell by row index.
    let inline nullEvery7 (r: int) = r % 7 = 0

    let private attr (kindLabel: string) (name: string) (col: string) (ty: PrimitiveType) (nullable: bool) : Attribute =
        { Attribute.create (mkKey [kindLabel; name]) (mkName name) ty with
            Column = ColumnRealization.create col nullable |> Result.value
            IsMandatory = not nullable }

    let private pkAttr (kindLabel: string) : Attribute =
        { Attribute.create (mkKey [kindLabel; "Id"]) (mkName "Id") Integer with
            Column = ColumnRealization.create "ID" false |> Result.value
            IsPrimaryKey = true
            IsMandatory = true }

    /// (attribute list, DDL column defs, per-row VALUES cell renderers)
    let private shapeOf (fam: Family) (kindLabel: string) : Attribute list * string list * (int -> string) list =
        let a = attr kindLabel
        match fam.Prefix with
        | "NAR" ->
            [ pkAttr kindLabel
              a "Code" "CODE" Text false
              a "Qty" "QTY" Integer true
              a "Active" "ACTIVE" Boolean false
              a "Opened" "OPENED" Date false ],
            [ "[ID] INT NOT NULL PRIMARY KEY"
              "[CODE] NVARCHAR(20) NOT NULL"
              "[QTY] INT NULL"
              "[ACTIVE] BIT NOT NULL"
              "[OPENED] DATE NOT NULL" ],
            [ (fun r -> string r)
              (fun r -> sprintf "N'C%06d'" r)
              (fun r -> if nullEvery7 r then "NULL" else string (r * 3))
              (fun r -> string (r % 2))
              (fun r -> sprintf "'2025-%02d-%02d'" (r % 12 + 1) (r % 28 + 1)) ]
        | "MED" ->
            [ pkAttr kindLabel
              a "Code" "CODE" Text false
              a "Amount" "AMOUNT" Decimal false
              a "Happened" "HAPPENED" DateTime false
              a "Ref" "REF" Guid false
              a "Note" "NOTE" Text true
              a "Rank" "RANK" Integer true
              a "Active" "ACTIVE" Boolean false
              a "Opened" "OPENED" Date false
              a "Qty" "QTY" Integer false ],
            [ "[ID] INT NOT NULL PRIMARY KEY"
              "[CODE] NVARCHAR(20) NOT NULL"
              "[AMOUNT] DECIMAL(18,2) NOT NULL"
              "[HAPPENED] DATETIME2 NOT NULL"
              "[REF] UNIQUEIDENTIFIER NOT NULL"
              "[NOTE] NVARCHAR(100) NULL"
              "[RANK] INT NULL"
              "[ACTIVE] BIT NOT NULL"
              "[OPENED] DATE NOT NULL"
              "[QTY] INT NOT NULL" ],
            [ (fun r -> string r)
              (fun r -> sprintf "N'M%06d'" r)
              (fun r -> sprintf "%d.%02d" (r * 7) (r % 100))
              (fun r -> sprintf "'2025-06-%02dT%02d:%02d:00'" (r % 28 + 1) (r % 24) (r % 60))
              (fun r -> sprintf "'%08x-0000-4000-8000-%012x'" (r % 100000) (r % 100000))
              (fun r -> if nullEvery7 r then "NULL" else sprintf "N'note %d'" r)
              (fun r -> if nullEvery7 (r + 3) then "NULL" else string (r % 50))
              (fun r -> string ((r + 1) % 2))
              (fun r -> sprintf "'2024-%02d-%02d'" (r % 12 + 1) (r % 28 + 1))
              (fun r -> string (r % 997)) ]
        | _ (* WID *) ->
            let intCols  = [ for k in 1 .. 4 -> (sprintf "N%d" k), (sprintf "N%d" k) ]
            let txtCols  = [ for k in 1 .. 4 -> (sprintf "T%d" k), (sprintf "T%d" k) ]
            let decCols  = [ for k in 1 .. 4 -> (sprintf "D%d" k), (sprintf "D%d" k) ]
            let dtCols   = [ for k in 1 .. 4 -> (sprintf "W%d" k), (sprintf "W%d" k) ]
            let attrs =
                [ pkAttr kindLabel ]
                @ [ for (n, c) in intCols -> a n c Integer true ]
                @ [ for (n, c) in txtCols -> a n c Text true ]
                @ [ for (n, c) in decCols -> a n c Decimal false ]
                @ [ for (n, c) in dtCols  -> a n c (if c.EndsWith "1" || c.EndsWith "2" then Date else DateTime) false ]
                @ [ a "B1" "B1" Boolean false; a "B2" "B2" Boolean false
                    a "L1" "L1" Integer false; a "L2" "L2" Integer false
                    a "Ref" "REF" Guid false; a "Ref2" "REF2" Guid true
                    a "Blob" "BLOB" Text false ]
            let ddl =
                [ "[ID] INT NOT NULL PRIMARY KEY" ]
                @ [ for (_, c) in intCols -> sprintf "[%s] INT NULL" c ]
                @ [ for (_, c) in txtCols -> sprintf "[%s] NVARCHAR(200) NULL" c ]
                @ [ for (_, c) in decCols -> sprintf "[%s] DECIMAL(18,4) NOT NULL" c ]
                @ [ for (_, c) in dtCols  -> sprintf "[%s] %s NOT NULL" c (if c.EndsWith "1" || c.EndsWith "2" then "DATE" else "DATETIME2") ]
                @ [ "[B1] BIT NOT NULL"; "[B2] BIT NOT NULL"
                    "[L1] BIGINT NOT NULL"; "[L2] BIGINT NOT NULL"
                    "[REF] UNIQUEIDENTIFIER NOT NULL"; "[REF2] UNIQUEIDENTIFIER NULL"
                    "[BLOB] NVARCHAR(400) NOT NULL" ]
            let cells =
                [ (fun r -> string r) ]
                @ [ for k in 1 .. 4 -> (fun r -> if nullEvery7 (r + k) then "NULL" else string (r * k)) ]
                @ [ for k in 1 .. 4 -> (fun r -> if nullEvery7 (r + k + 4) then "NULL" else sprintf "N'txt%d_%d'" k r) ]
                @ [ for k in 1 .. 4 -> (fun r -> sprintf "%d.%04d" (r + k) (r % 10000)) ]
                @ [ (fun r -> sprintf "'2023-%02d-%02d'" (r % 12 + 1) (r % 28 + 1))
                    (fun r -> sprintf "'2022-%02d-%02d'" (r % 12 + 1) (r % 28 + 1))
                    (fun r -> sprintf "'2025-01-%02dT%02d:%02d:00'" (r % 28 + 1) (r % 24) (r % 60))
                    (fun r -> sprintf "'2025-02-%02dT%02d:%02d:00'" (r % 28 + 1) (r % 24) (r % 60)) ]
                @ [ (fun r -> string (r % 2)); (fun r -> string ((r + 1) % 2))
                    (fun r -> string (int64 r * 1000L)); (fun r -> string (int64 r * 7L))
                    (fun r -> sprintf "'%08x-1111-4111-8111-%012x'" (r % 100000) (r % 100000))
                    (fun r -> if nullEvery7 r then "NULL" else sprintf "'%08x-2222-4222-8222-%012x'" (r % 100000) (r % 100000))
                    (fun r -> sprintf "N'payload %d lorem ipsum dolor sit amet consectetur'" r) ]
            attrs, ddl, cells

    let allTables : (Family * int) list =
        [ for fam in families do for i in 1 .. fam.Count -> fam, i ]

    let kindOf (fam: Family) (i: int) : Kind =
        let label = sprintf "%s%03d" fam.Prefix i
        let attrs, _, _ = shapeOf fam label
        { Kind.create (mkKey [label]) (mkName label)
            (TableId.create "dbo" (tableName fam i) |> Result.value)
            attrs
          with References = []; Indexes = []; ColumnChecks = [] }

    let catalog : Catalog =
        { Modules =
            [ { SsKey = mkKey ["CorpusModule"]
                Name  = mkName "CorpusModule"
                Kinds = [ for fam, i in allTables -> kindOf fam i ]
                IsActive = true
                ExtendedProperties = [] } ]
          Sequences = [] }

    let totalRows : int = families |> List.sumBy (fun f -> f.Count * f.Rows)

    /// Seed one table: CREATE + INSERTs in 1000-row VALUES batches.
    let seedTable (cnn: SqlConnection) (fam: Family) (i: int) : Task<unit> =
        task {
            let label = sprintf "%s%03d" fam.Prefix i
            let _, ddl, cells = shapeOf fam label
            do! Deploy.executeBatch cnn (sprintf "CREATE TABLE [dbo].[%s] (%s);" (tableName fam i) (String.concat ", " ddl))
            let mutable r0 = 1
            while r0 <= fam.Rows do
                let rN = min fam.Rows (r0 + 999)
                let values =
                    [ for r in r0 .. rN ->
                        cells |> List.map (fun c -> c r) |> String.concat ", " |> sprintf "(%s)" ]
                    |> String.concat ", "
                do! Deploy.executeBatch cnn (sprintf "INSERT INTO [dbo].[%s] VALUES %s;" (tableName fam i) values)
                r0 <- rN + 1
        }

    let totalOf (rows: Map<SsKey, StaticRow list>) : int =
        rows |> Map.fold (fun acc _ rs -> acc + List.length rs) 0

    /// Seed every corpus table — hoisted to module level and restructured
    /// as the tail-recursive task continuation (never loop-with-`do!` or a
    /// tuple-pattern `for` inside a Release `task { }` — FS3511; the same
    /// posture as `Ingestion.collectInOrderFor`).
    let rec private seedFrom (cnn: SqlConnection) (remaining: (Family * int) list) : Task<unit> =
        task {
            match remaining with
            | [] -> return ()
            | entry :: rest ->
                do! seedTable cnn (fst entry) (snd entry)
                do! seedFrom cnn rest
        }

    let seedAll (cnn: SqlConnection) : Task<unit> =
        seedFrom cnn allTables

    // -- durable-corpus mode (PERF_CORPUS_DURABLE=1) -------------------------
    //
    // The seed is ~4.5min of a ~6min run; measurement ITERATIONS should not
    // repay it. Durable mode seeds a fixed database on the shared instance
    // once and reuses it across runs: existence is judged by the table count
    // plus the SENTINEL table's exact row count (the LAST table seeded —
    // seeding is strictly ordered, so a complete sentinel implies a complete
    // corpus); any mismatch drops every corpus table and reseeds from
    // scratch. The database is NEVER dropped here — reclaim manually with
    // `DROP DATABASE PerfCorpusDurable` if the warm instance's memory pool
    // degrades (the survival-rule-2 family).

    let durableEnabled () : bool =
        System.Environment.GetEnvironmentVariable "PERF_CORPUS_DURABLE" = "1"

    [<Literal>]
    let private DurableDbName : string = "PerfCorpusDurable"

    /// (sentinel table name, its expected exact row count).
    let private sentinel : string * int =
        let entry = List.last allTables
        tableName (fst entry) (snd entry), (fst entry).Rows

    /// Create the durable database if absent; return its connection string.
    let ensureDurableDb (masterConn: string) : Task<string> =
        task {
            use cnnMaster = new SqlConnection(masterConn)
            do! cnnMaster.OpenAsync()
            do! Deploy.executeBatch cnnMaster
                    (sprintf "IF DB_ID(N'%s') IS NULL CREATE DATABASE [%s];" DurableDbName DurableDbName)
            return Deploy.ConnectionString.buildPerDb masterConn DurableDbName
        }

    let private corpusComplete (cnn: SqlConnection) : Task<bool> =
        task {
            // Two round trips ON PURPOSE: SQL Server name-binds every
            // object in a statement at COMPILE time, so a single CASE
            // guarding the sentinel COUNT still fails with "invalid
            // object name" on a fresh database.
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                sprintf
                    "SELECT CASE WHEN (SELECT COUNT(*) FROM sys.tables WHERE name LIKE 'PC[_]%%') = %d \
                     AND OBJECT_ID(N'dbo.%s') IS NOT NULL THEN 1 ELSE 0 END"
                    (List.length allTables) (fst sentinel)
            let! present = cmd.ExecuteScalarAsync()
            if (present :?> int) <> 1 then return false
            else
                use cmd2 = cnn.CreateCommand()
                cmd2.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM dbo.[%s]" (fst sentinel)
                let! rows = cmd2.ExecuteScalarAsync()
                return (rows :?> int64) = int64 (snd sentinel)
        }

    let private dropAllTables (cnn: SqlConnection) : Task<unit> =
        task {
            let drops =
                allTables
                |> List.map (fun entry -> sprintf "DROP TABLE IF EXISTS [dbo].[%s];" (tableName (fst entry) (snd entry)))
                |> String.concat " "
            do! Deploy.executeBatch cnn drops
        }

    /// Seed-if-absent: reuse a verified-complete corpus, else wipe + reseed.
    /// Works identically in ephemeral mode (verification always fails on a
    /// fresh database → seeds).
    let ensureSeeded (say: string -> unit) (cnn: SqlConnection) : Task<unit> =
        task {
            let! complete = corpusComplete cnn
            if complete then
                say (sprintf "[corpus] seed: reusing durable corpus (%d tables; sentinel %s verified)"
                        (List.length allTables) (fst sentinel))
            else
                let sw = System.Diagnostics.Stopwatch.StartNew()
                do! dropAllTables cnn
                do! seedAll cnn
                sw.Stop()
                say (sprintf "[corpus] seed: %d tables / %d rows in %dms"
                        (List.length allTables) totalRows sw.ElapsedMilliseconds)
        }

    /// P3 probe — drain every kind's RAW quantum stream (positional cells;
    /// no Map mint, no row-identity synthesis) on one connection in
    /// topological order. Same wire, same connection as the materialized
    /// hydrate leg, so the delta between the two IS the row-carrier tax
    /// the program's P3 item slims. Tail-recursive task continuation
    /// (FS3511 posture).
    let rec private drainQuantaFrom (cnn: SqlConnection) (acc: int) (remaining: Kind list) : Task<int> =
        task {
            match remaining with
            | [] -> return acc
            | kind :: rest ->
                let! quanta = AsyncStream.toList (Ingestion.streamKind cnn kind)
                return! drainQuantaFrom cnn (acc + List.length quanta) rest
        }

    let drainQuantaSerial (cnn: SqlConnection) (catalog: Catalog) (topo: TopologicalOrder) : Task<int> =
        let kinds = topo.Order |> List.choose (fun key -> Catalog.tryFindKind key catalog)
        drainQuantaFrom cnn 0 kinds

    /// The tiered leg's identity laws (P4), hoisted out of the task CE
    /// (FS3511): a capped kind keeps EXACT RowCount + NullCounts (the
    /// aggregate is never capped) with Values truncated to the cap; an
    /// uncapped kind is IDENTICAL to the full-scan cache.
    let assertTieredIdentity
        (sampledKeys: Set<SsKey>)
        (cap: int)
        (full: EvidenceCache)
        (tiered: EvidenceCache)
        : unit =
        Assert.Equal(Map.count full.Kinds, Map.count tiered.Kinds)
        for KeyValue(key, t) in tiered.Kinds do
            let f = Map.find key full.Kinds
            if Set.contains key sampledKeys then
                Assert.Equal(f.RowCount, t.RowCount)
                Assert.Equal<Map<SsKey, int64>>(f.NullCounts, t.NullCounts)
                for c in t.Columns do
                    Assert.Equal(cap, c.Values.Length)
            else
                Assert.Equal<CachedKind>(f, t)

    /// The pipelined leg's two identity laws, hoisted OUT of the test's
    /// `task { }` (a tuple-pattern `for` inside a Release task CE is the
    /// FS3511 failure shape — survival rule 5): every kind's drain-rendered
    /// text byte-equals the two-phase artifact, and the drain-derived
    /// evidence equals the live-scan cache.
    let assertPipedIdentity
        (incumbentScripts: Map<SsKey, Projection.Targets.Data.DataInsertScript>)
        (liveKinds: Map<SsKey, CachedKind>)
        (piped: Map<SsKey, Projection.Targets.Data.DataInsertScript * CachedKind option>)
        : unit =
        for KeyValue(key, (script, _)) in piped do
            Assert.Equal((Map.find key incumbentScripts).Rendered, script.Rendered)
        let pipedCache =
            piped
            |> Map.toList
            |> List.choose (fun (_, (_, c)) -> c)
            |> List.map (fun c -> c.KindKey, c)
            |> Map.ofList
        Assert.Equal<Map<SsKey, CachedKind>>(liveKinds, pipedCache)


[<Xunit.Collection("Docker-SqlServer")>]
type PerfCorpusMeasurementTests(fixture: EphemeralContainerFixture, output: ITestOutputHelper) =
    interface IClassFixture<EphemeralContainerFixture>

    member private _.Say (line: string) =
        output.WriteLine line
        printfn "%s" line

    [<Fact>]
    member this.``corpus measurement: hydrate / profile / emit at ~480k rows x 240 tables`` () =
        if not (PerfCorpus.skipUnlessEnabled "perf-corpus") then () else
        if PerfCorpus.durableEnabled () then
            // Durable mode: fixed database on the shared instance,
            // seed-if-absent, never dropped — measurement iterations
            // skip the ~4.5min seed.
            TaskSync.run (fun () ->
                task {
                    let! perDbConn = PerfCorpus.ensureDurableDb fixture.MasterConnectionString
                    use cnn = new SqlConnection(perDbConn)
                    do! cnn.OpenAsync()
                    do! this.RunLegs cnn perDbConn
                })
        else
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "PerfCorpus" (fun cnn perDbConn ->
                    this.RunLegs cnn perDbConn))

    /// The measurement legs over an already-provisioned corpus database
    /// (ephemeral or durable): seed-if-absent, then the ledger legs.
    member private this.RunLegs (cnn: SqlConnection) (perDbConn: string) : Task<unit> =
                task {
                    do! PerfCorpus.ensureSeeded (fun s -> this.Say s) cnn

                    let catalog = PerfCorpus.catalog
                    let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
                    let owned = topo.Order |> Set.ofList
                    let openConnection () : Task<Result<SqlConnection>> =
                        task {
                            let c = new SqlConnection(perDbConn)
                            do! c.OpenAsync()
                            return Result.success c
                        }

                    // Warmup (unmeasured): one narrow-family drain + one discovery.
                    let firstKind = Catalog.allKinds catalog |> List.head
                    let! _ = AsyncStream.toList (Ingestion.streamKindRows cnn firstKind)

                    // -- hydrate serial (incumbent)
                    let swH = System.Diagnostics.Stopwatch.StartNew()
                    let! serialRows = Ingestion.collectInOrderFor owned cnn catalog topo
                    swH.Stop()
                    let rowTotal = PerfCorpus.totalOf serialRows
                    Assert.Equal(PerfCorpus.totalRows, rowTotal)
                    this.Say (sprintf "[corpus] hydrate serial:        %6dms  (%d rows / %d tables)"
                                swH.ElapsedMilliseconds rowTotal (Map.count serialRows))

                    // -- drain quanta only (P3 probe): the slim positional
                    //    carrier without the IR rebuild; the delta against
                    //    hydrate serial is the measured row-carrier tax.
                    let swQ = System.Diagnostics.Stopwatch.StartNew()
                    let! quantaCount = PerfCorpus.drainQuantaSerial cnn catalog topo
                    swQ.Stop()
                    Assert.Equal(PerfCorpus.totalRows, quantaCount)
                    this.Say (sprintf "[corpus] drain quanta serial:   %6dms  (positional cells, no IR rebuild)"
                                swQ.ElapsedMilliseconds)

                    // -- hydrate concurrent-4
                    let swHC = System.Diagnostics.Stopwatch.StartNew()
                    let! concRowsR = Ingestion.collectInOrderForConcurrent 4 openConnection owned catalog topo
                    swHC.Stop()
                    let concRows = PerfCorpus.mustOk concRowsR
                    Assert.Equal<Map<SsKey, StaticRow list>>(serialRows, concRows)
                    this.Say (sprintf "[corpus] hydrate concurrent-4:  %6dms  (identical row map)" swHC.ElapsedMilliseconds)

                    // -- profile live-scan concurrent-4 (incumbent)
                    let swP = System.Diagnostics.Stopwatch.StartNew()
                    let! liveCacheR =
                        LiveProfiler.captureEvidenceCacheConcurrent
                            { SqlProfilerOptions.defaults with MaxConcurrency = 4 }
                            openConnection catalog
                    swP.Stop()
                    let liveCache = PerfCorpus.mustOk liveCacheR
                    Assert.Equal(Map.count serialRows, Map.count liveCache.Kinds)
                    this.Say (sprintf "[corpus] profile live-scan c4:  %6dms  (%d kinds, 2 queries + full stream each)"
                                swP.ElapsedMilliseconds (Map.count liveCache.Kinds))

                    // -- profile single-scan-derived (P1): evidence derives
                    //    from the hydrated rows; VALUE IDENTITY against the
                    //    live-scan cache is the law that lets the timing
                    //    count (CachedValue.ofRaw's equivalence table,
                    //    exact counts, aligned Values arrays).
                    let swD = System.Diagnostics.Stopwatch.StartNew()
                    let! derivedCacheR =
                        LiveProfiler.captureEvidenceCacheDerived
                            { SqlProfilerOptions.defaults with MaxConcurrency = 4 }
                            openConnection serialRows catalog
                    swD.Stop()
                    let derivedCache = PerfCorpus.mustOk derivedCacheR
                    Assert.Equal(Map.count liveCache.Kinds, Map.count derivedCache.Kinds)
                    Assert.Equal<Map<SsKey, CachedKind>>(liveCache.Kinds, derivedCache.Kinds)
                    this.Say (sprintf "[corpus] profile single-scan:   %6dms  (derived from hydrated rows; identical cache; 1 global query)"
                                swD.ElapsedMilliseconds)

                    // -- emit data lane (grafted corpus)
                    let staticCatalog =
                        catalog
                        |> Catalog.mapKinds (fun k ->
                            match Map.tryFind k.SsKey serialRows with
                            | Some rows -> { k with Modality = [ Static rows ] }
                            | None -> k)
                    let swE = System.Diagnostics.Stopwatch.StartNew()
                    let emitted =
                        Projection.Targets.Data.StaticSeedsEmitter.emit
                            DataEmitOptions.defaults staticCatalog Profile.empty
                    swE.Stop()
                    let emittedLen =
                        match emitted with
                        | Ok artifact ->
                            ArtifactByKind.toMap artifact
                            |> Map.fold (fun acc _ (s: Projection.Targets.Data.DataInsertScript) -> acc + s.Rendered.Length) 0
                        | Error e -> failwithf "emit leg failed: %A" e
                    this.Say (sprintf "[corpus] emit data lane:        %6dms  (%d rows -> %d chars)"
                                swE.ElapsedMilliseconds rowTotal emittedLen)

                    // -- acquire+emit+evidence pipelined single pass (P2 ∘ P1):
                    //    each kind's MERGE renders AND its evidence derives on
                    //    the drain worker the moment its rows land (inside the
                    //    concurrency gate; rows dropped after projection — live
                    //    row memory caps at 4 kinds, not the estate). Laws
                    //    before timing: rendered text byte-equals the two-phase
                    //    artifact per kind; derived evidence equals the
                    //    live-scan cache.
                    let incumbentScripts =
                        match emitted with
                        | Ok artifact -> ArtifactByKind.toMap artifact
                        | Error e -> failwithf "emit leg failed: %A" e
                    let cycleMembers = TopologicalOrder.cycleMembers topo
                    let! nullabilityR = LiveProfiler.reflectNullability openConnection
                    let nullability = PerfCorpus.mustOk nullabilityR
                    let swPipe = System.Diagnostics.Stopwatch.StartNew()
                    let! pipedR =
                        Ingestion.collectInOrderForConcurrentWith
                            (fun kind rows ->
                                let load, _skipped =
                                    DataLoadPlan.loadFor cycleMembers SurrogateRemapContext.empty kind rows
                                let script =
                                    Projection.Targets.Data.StaticSeedsEmitter.renderLoad
                                        DataEmitOptions.defaults Profile.empty.CdcAwareness kind load
                                let derived =
                                    EvidenceCache.cachedKindOfRows
                                        (LiveProfiler.nullabilityFor nullability kind) kind rows
                                script, derived)
                            4 openConnection owned catalog topo
                    swPipe.Stop()
                    let piped = PerfCorpus.mustOk pipedR
                    PerfCorpus.assertPipedIdentity incumbentScripts liveCache.Kinds piped
                    let twoPhaseMs =
                        swHC.ElapsedMilliseconds + swD.ElapsedMilliseconds + swE.ElapsedMilliseconds
                    this.Say (sprintf "[corpus] pipelined single pass: %6dms  (drain+render+derive on 4 workers; vs %dms two-phase sum; byte-identical scripts; identical cache)"
                                swPipe.ElapsedMilliseconds twoPhaseMs)

                    // -- pipelined single pass over QUANTA (P3 ∘ P2 ∘ P1):
                    //    the drain stays on the slim positional carrier —
                    //    no per-row Map mint, no row-identity synthesis on
                    //    the drain path (identities mint inside the render,
                    //    through the same shared formula) — and the render
                    //    + evidence both consume cells positionally. Same
                    //    identity laws as the named-row pass: byte-identical
                    //    scripts, identical evidence cache.
                    let swPipeQ = System.Diagnostics.Stopwatch.StartNew()
                    let! pipedQR =
                        Ingestion.collectQuantaForConcurrentWith
                            (fun kind quanta ->
                                let deferred = TopologicalOrder.deferredFkColumns cycleMembers kind
                                let script =
                                    Projection.Targets.Data.StaticSeedsEmitter.renderQuanta
                                        DataEmitOptions.defaults Profile.empty.CdcAwareness kind deferred quanta
                                let derived =
                                    EvidenceCache.cachedKindOfQuanta
                                        (LiveProfiler.nullabilityFor nullability kind) kind quanta
                                script, derived)
                            4 openConnection owned catalog topo
                    swPipeQ.Stop()
                    let pipedQ = PerfCorpus.mustOk pipedQR
                    PerfCorpus.assertPipedIdentity incumbentScripts liveCache.Kinds pipedQ
                    this.Say (sprintf "[corpus] pipelined quanta pass: %6dms  (positional carrier end-to-end; vs %dms named-row pass; byte-identical scripts; identical cache)"
                                swPipeQ.ElapsedMilliseconds swPipe.ElapsedMilliseconds)

                    // -- profile tiered (P4): the sampling policy caps the 10
                    //    WID kinds at 2,000 rows (their 20,000-row streams are
                    //    the live scan's bulk); NAR/MED stay exhaustive. Laws:
                    //    exact counts preserved under the cap, Values truncated
                    //    to it, uncapped kinds identical, downgrades named
                    //    (one diagnostic per sampled kind), and the derived
                    //    partition excludes exactly the sampled kinds.
                    let widKeys =
                        Catalog.allKinds catalog
                        |> List.filter (fun k -> (TableId.tableText k.Physical).StartsWith "PC_WID")
                        |> List.map (fun k -> k.SsKey)
                    let tieredPolicy =
                        { SamplingPolicy.fullScan with
                            Overrides = widKeys |> List.map (fun k -> k, Some 2000) |> Map.ofList }
                    let tieredDiags = SamplingDiagnostics.emit catalog tieredPolicy
                    Assert.Equal(List.length widKeys, List.length tieredDiags)
                    let swT = System.Diagnostics.Stopwatch.StartNew()
                    let! tieredR =
                        LiveProfiler.captureEvidenceCacheConcurrent
                            { SqlProfilerOptions.defaults with MaxConcurrency = 4; Sampling = tieredPolicy }
                            openConnection catalog
                    swT.Stop()
                    let tiered = PerfCorpus.mustOk tieredR
                    PerfCorpus.assertTieredIdentity (Set.ofList widKeys) 2000 liveCache tiered
                    // Derived + tiered compose: sampled kinds keep the live
                    // capped discovery (deterministic TOP-N under ORDER BY pk),
                    // unsampled hydrated kinds derive — the union equals the
                    // all-live tiered cache exactly.
                    let! derivedTieredR =
                        LiveProfiler.captureEvidenceCacheDerived
                            { SqlProfilerOptions.defaults with MaxConcurrency = 4; Sampling = tieredPolicy }
                            openConnection serialRows catalog
                    let derivedTiered = PerfCorpus.mustOk derivedTieredR
                    Assert.Equal<Map<SsKey, CachedKind>>(tiered.Kinds, derivedTiered.Kinds)
                    this.Say (sprintf "[corpus] profile tiered c4:     %6dms  (%d WID kinds capped at 2000 of 20000; exact counts preserved; %d named downgrades; derived∘tiered identical)"
                                swT.ElapsedMilliseconds (List.length widKeys) (List.length tieredDiags))
                }
