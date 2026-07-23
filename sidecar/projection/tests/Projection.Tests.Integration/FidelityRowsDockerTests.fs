namespace Projection.Tests

// T17's live witness (the fidelity chapter, wave B2 — the promotion trigger
// named on the AxiomTests stub): two databases carry the SAME rows in the
// TWO renditions of one model — the source in the physical (OSUSR) shape,
// the target in the logical shape — and `FidelityCompareRun` proves
// byte-identity across the physical-to-logical gap; then one flipped cell
// names its key and its differing column. The seeds DERIVE from the same
// renditions the run compares (CatalogRendition.physical/logical), so the
// witness cannot drift from the alignment package it certifies.
// Serial via the Docker-SqlServer collection.

open Xunit
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Pipeline
open Projection.Tests.IRBuilders
open Projection.Tests.Fixtures

[<Xunit.Collection("Docker-SqlServer")>]
module FidelityRowsDockerTests =

    let private skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    /// One kind in its authored (physical) realization: Thing, PK Id, two
    /// text columns, OSUSR table + SCREAMING column names.
    let private model : Catalog =
        let thing =
            Kind.create
                (kindKey [ "Thing" ])
                (mkName "Thing")
                (mkTableId "dbo" "OSUSR_FID_THING")
                [ { Attribute.create (attrKey [ "Thing"; "Id" ]) (mkName "Id") Integer with
                      Column = ColumnRealization.create "ID" false |> Result.value
                      IsPrimaryKey = true }
                  { Attribute.create (attrKey [ "Thing"; "Email" ]) (mkName "Email") Text with
                      Column = ColumnRealization.create "EMAIL" false |> Result.value }
                  { Attribute.create (attrKey [ "Thing"; "Name" ]) (mkName "Name") Text with
                      Column = ColumnRealization.create "NAME" false |> Result.value } ]
        mkCatalog [ mkModule (modKey "Fid") (mkName "Fid") [ thing ] ]

    /// DDL + rows for one rendition's kind — derived from the kind itself,
    /// so the seed and the comparator read one shape.
    let private seedFor (kind: Kind) (rows: (int * string * string) list) : string =
        let columnOf (a: Attribute) : string = ColumnRealization.columnNameText a.Column
        let columnDdl (a: Attribute) : string =
            let sqlType = match a.Type with | Integer -> "INT" | _ -> "NVARCHAR(100)"
            let nullability = if a.IsPrimaryKey then "NOT NULL PRIMARY KEY" else "NULL"
            sprintf "[%s] %s %s" (columnOf a) sqlType nullability
        let table =
            sprintf "[%s].[%s]" (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)
        let create =
            sprintf "CREATE TABLE %s (%s);" table
                (kind.Attributes |> List.map columnDdl |> String.concat ", ")
        let columnList =
            kind.Attributes |> List.map (fun a -> sprintf "[%s]" (columnOf a)) |> String.concat ","
        let values =
            rows
            |> List.map (fun (id, email, name) -> sprintf "(%d, N'%s', N'%s')" id email name)
            |> String.concat ", "
        sprintf "%s INSERT INTO %s (%s) VALUES %s;" create table columnList values

    let private rows =
        [ 1041, "a@x.example", "alpha"
          1042, "b@x.example", "bravo"
          1043, "c@x.example", "charlie" ]

    [<Fact>]
    let ``T17 witness: two renditions of one estate prove byte-identical over live SQL; one flipped cell names its key and column`` () =
        let label = "FidRows"
        if not (skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            task {
                let physicalKind = CatalogRendition.physical model |> Catalog.allKinds |> List.head
                let logicalKind = CatalogRendition.logical model |> Catalog.allKinds |> List.head
                let! result =
                    Deploy.withBootstrappedDatabase "FidRowsSrc" (seedFor physicalKind rows) (fun source ->
                        Deploy.withBootstrappedDatabase "FidRowsTgt" (seedFor logicalKind rows) (fun target ->
                            task {
                                // -- identical data in the two renditions → byte-identity across the gap
                                let! firstR =
                                    FidelityCompareRun.runWith source target "src" "tgt" "the authored model" model None None 20 None [] []
                                let first = Result.value firstR
                                Assert.True(RowFidelityReport.agrees first, "identical renditions read as differing")
                                let verdict = first.Kinds |> List.exactlyOne
                                Assert.Equal(3L, verdict.Source.Count)
                                Assert.Equal(verdict.Source, verdict.Target)
                                Assert.Equal("Id", verdict.KeyColumn)

                                // -- one flipped cell on the target names its key and its column
                                use flip = target.CreateCommand()
                                flip.CommandText <-
                                    sprintf "UPDATE [%s].[%s] SET [%s] = N'flipped@x.example' WHERE [%s] = 1042;"
                                        (TableId.schemaText logicalKind.Physical)
                                        (TableId.tableText logicalKind.Physical)
                                        (ColumnRealization.columnNameText (logicalKind.Attributes |> List.find (fun a -> Name.value a.Name = "Email")).Column)
                                        (ColumnRealization.columnNameText (logicalKind.Attributes |> List.find (fun a -> a.IsPrimaryKey)).Column)
                                let! _ = flip.ExecuteNonQueryAsync()
                                let! secondR =
                                    FidelityCompareRun.runWith source target "src" "tgt" "the authored model" model None None 20 None [] []
                                let second = Result.value secondR
                                Assert.False(RowFidelityReport.agrees second)
                                let flipped = second.Kinds |> List.exactlyOne
                                Assert.Equal(1L, flipped.DifferenceTotal)
                                match flipped.Differences with
                                | [ RowDifference.CellsDiffer (key, columns) ] ->
                                    Assert.Equal("1042", key)
                                    Assert.Equal<string list>([ "Email" ], columns |> List.map Name.value)
                                | other -> Assert.True(false, sprintf "expected one CellsDiffer; got %A" other)
                                return 0
                            }))
                Assert.Equal(0, result)
            })

    // P2-S3 — THE OFFLINE RECONCILE. Capture a portable manifest (the SOURCE's
    // per-kind digests), then verify a target the tool did not stage against it
    // with NO live source present (`check fidelity --against`). A byte-identical
    // target reconciles green; a tampered cell reds — both decided from the
    // stored manifest + the target alone, the source never touched.
    [<Fact>]
    let ``P2-S3 offline reconcile: --against proves a target byte-identical to a captured manifest with NO live source; a tampered cell reads exit 5`` () =
        let label = "P2S3Reconcile"
        if not (skipIfNoDocker label) then () else
        let manifestPath =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "p2s3-" + System.Guid.NewGuid().ToString "N" + ".manifest.json")
        TaskSync.run (fun () ->
            task {
                let physicalKind = CatalogRendition.physical model |> Catalog.allKinds |> List.head
                let logicalKind = CatalogRendition.logical model |> Catalog.allKinds |> List.head
                let! result =
                    Deploy.withScratchDatabase "P2S3Src" (fun srcConn ->
                        Deploy.withScratchDatabase "P2S3Tgt" (fun tgtConn ->
                            task {
                                use src = new SqlConnection(srcConn)
                                use tgt = new SqlConnection(tgtConn)
                                do! src.OpenAsync()
                                do! tgt.OpenAsync()
                                do! Deploy.executeBatch src (seedFor physicalKind rows)
                                do! Deploy.executeBatch tgt (seedFor logicalKind rows)
                                // CAPTURE — the SOURCE's per-kind (logical-aligned) digests, to a portable file.
                                let! reportR =
                                    FidelityCompareRun.runWith src tgt "src" "tgt" "the authored model" model None None 20 None [] []
                                let report = Result.value reportR
                                Assert.True(RowFidelityReport.agrees report, "capture precondition: the two renditions must agree")
                                let manifest =
                                    ProofManifest.ofReport
                                        (System.DateTimeOffset(2026, 7, 19, 0, 0, 0, System.TimeSpan.Zero))
                                        (FidelityProofCache.modelHash model)
                                        report
                                match ProofManifest.write manifestPath manifest with
                                | Error es ->
                                    Assert.True(false, sprintf "manifest write failed: %A" es)
                                    return 0
                                | Ok () ->
                                    let againstArgs : CheckFidelityAgainstArgs =
                                        { ManifestPath = manifestPath; TargetLabel = "tgt"; TargetConn = tgtConn; AsJson = false }
                                    try
                                        // GREEN — the target reconciles byte-identical to the manifest;
                                        // `runFidelityAgainst` opens only the target, never the source.
                                        Assert.Equal(0, Projection.Cli.Faces.Fidelity.runFidelityAgainst model againstArgs)
                                        // RED — tamper one target cell; the reconcile detects it OFFLINE (exit 5).
                                        let emailCol = ColumnRealization.columnNameText (logicalKind.Attributes |> List.find (fun a -> Name.value a.Name = "Email")).Column
                                        let pkCol = ColumnRealization.columnNameText (logicalKind.Attributes |> List.find (fun a -> a.IsPrimaryKey)).Column
                                        do! Deploy.executeBatch tgt
                                                (sprintf "UPDATE [%s].[%s] SET [%s] = N'tampered@x.example' WHERE [%s] = 1042;"
                                                    (TableId.schemaText logicalKind.Physical)
                                                    (TableId.tableText logicalKind.Physical)
                                                    emailCol pkCol)
                                        Assert.Equal(5, Projection.Cli.Faces.Fidelity.runFidelityAgainst model againstArgs)
                                        return 0
                                    finally
                                        try System.IO.File.Delete manifestPath with _ -> ()
                                        try System.IO.File.Delete "fidelity.rows.json" with _ -> ()
                            }))
                Assert.Equal(0, result)
            })

    // -- Approved-data-correction SOURCE replay (the fidelity addendum) --------
    // A same-row backfill: the source carries the malformed pre-correction state
    // (Name NULL) and the target the corrected state (Name backfilled from Email,
    // as publish would load it). Replaying the correction onto the source proves
    // byte-identity; the raw source diverges; a tampered receipt count reds by name.

    let private seedNullable (kind: Kind) (rows: (int * string * string option) list) : string =
        let columnOf (a: Attribute) : string = ColumnRealization.columnNameText a.Column
        let columnDdl (a: Attribute) : string =
            let sqlType = match a.Type with | Integer -> "INT" | _ -> "NVARCHAR(100)"
            let nullability = if a.IsPrimaryKey then "NOT NULL PRIMARY KEY" else "NULL"
            sprintf "[%s] %s %s" (columnOf a) sqlType nullability
        let table = sprintf "[%s].[%s]" (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)
        let create = sprintf "CREATE TABLE %s (%s);" table (kind.Attributes |> List.map columnDdl |> String.concat ", ")
        let columnList = kind.Attributes |> List.map (fun a -> sprintf "[%s]" (columnOf a)) |> String.concat ","
        let cell (v: string option) = match v with Some s -> sprintf "N'%s'" s | None -> "NULL"
        let values =
            rows
            |> List.map (fun (id, email, name) -> sprintf "(%d, N'%s', %s)" id email (cell name))
            |> String.concat ", "
        sprintf "%s INSERT INTO %s (%s) VALUES %s;" create table columnList values

    let private backfillCorrection : ApprovedDataCorrection =
        { Id = "backfill-name"
          SourceRemediationId = Some "D1-name"
          Enabled = true
          Subject = AttributeCoordinate.create "Fid" "Thing" "Name"
          Predicate = Some (Predicate.IsNull (mkName "Name"))
          Derivation = DataCorrectionDerivationSpec.SameRowAttribute (AttributeCoordinate.create "Fid" "Thing" "Email")
          Guards = [ DataCorrectionGuard.TargetIsNull; DataCorrectionGuard.SourceIsNotNull ]
          EvidenceColumns = []
          ExpectedCount = None
          ReferencedEntity = None
          ConfiguredProbes = []
          ApprovedBy = Some "operator"
          ApprovedAt = Some "2026-07-23" }

    [<Fact>]
    let ``fidelity replay: a corrected target proves byte-identical when the correction replays onto the source; raw source reds; a tampered receipt count reds by name`` () =
        let label = "FidRowsCorr"
        if not (skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            task {
                let physicalKind = CatalogRendition.physical model |> Catalog.allKinds |> List.head
                let logicalKind = CatalogRendition.logical model |> Catalog.allKinds |> List.head
                let srcSeed = seedNullable physicalKind [ 1, "a@x.example", None; 2, "b@x.example", None ]
                let tgtSeed = seedNullable logicalKind [ 1, "a@x.example", Some "a@x.example"; 2, "b@x.example", Some "b@x.example" ]
                let! result =
                    Deploy.withBootstrappedDatabase "FidCorrSrc" srcSeed (fun source ->
                        Deploy.withBootstrappedDatabase "FidCorrTgt" tgtSeed (fun target ->
                            task {
                                // Raw source (Name NULL) diverges from the corrected target.
                                let! rawR = FidelityCompareRun.runWith source target "src" "tgt" "the authored model" model None None 20 None [] []
                                Assert.False(RowFidelityReport.agrees (Result.value rawR), "raw source should diverge from the corrected target")
                                // The correction replays onto the source → byte-identical, and the ledger names the receipt.
                                let! greenR = FidelityCompareRun.runWith source target "src" "tgt" "the authored model" model None None 20 None [ backfillCorrection ] []
                                let green = Result.value greenR
                                Assert.True(RowFidelityReport.agrees green, "the replayed correction should make the proof byte-identical")
                                let receipt = green.DataCorrectionReceipts |> List.exactlyOne
                                Assert.Equal("backfill-name", receipt.CorrectionId)
                                Assert.Equal(2L, receipt.RowsChanged)
                                // A tampered recorded receipt count reds the proof BY NAME.
                                let tampered = { receipt with RowsChanged = 999L }
                                let! redR = FidelityCompareRun.runWith source target "src" "tgt" "the authored model" model None None 20 None [ backfillCorrection ] [ tampered ]
                                match redR with
                                | Ok _ -> Assert.True(false, "a tampered receipt count should fail the proof")
                                | Error es -> Assert.Equal("dataCorrection.fidelity.receiptMismatch", (List.head es).Code)
                                return 0
                            }))
                Assert.Equal(0, result)
            })
