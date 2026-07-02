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


[<Xunit.Collection("Docker-SqlServer")>]
type PerfCorpusMeasurementTests(fixture: EphemeralContainerFixture, output: ITestOutputHelper) =
    interface IClassFixture<EphemeralContainerFixture>

    member private _.Say (line: string) =
        output.WriteLine line
        printfn "%s" line

    [<Fact>]
    member this.``corpus measurement: hydrate / profile / emit at ~480k rows x 240 tables`` () =
        if not (PerfCorpus.skipUnlessEnabled "perf-corpus") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "PerfCorpus" (fun cnn perDbConn ->
                task {
                    let sw = System.Diagnostics.Stopwatch.StartNew()
                    for fam, i in PerfCorpus.allTables do
                        do! PerfCorpus.seedTable cnn fam i
                    sw.Stop()
                    this.Say (sprintf "[corpus] seed: %d tables / %d rows in %dms"
                                (List.length PerfCorpus.allTables) PerfCorpus.totalRows sw.ElapsedMilliseconds)

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
                }))
