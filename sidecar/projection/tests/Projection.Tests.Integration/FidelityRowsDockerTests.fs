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
                                    FidelityCompareRun.runWith source target "src" "tgt" "the authored model" model None None 20 None
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
                                    FidelityCompareRun.runWith source target "src" "tgt" "the authored model" model None None 20 None
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
