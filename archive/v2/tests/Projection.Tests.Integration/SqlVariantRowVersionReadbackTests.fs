module Projection.Tests.SqlVariantRowVersionReadbackTests

// ============================================================================
// On-prem estates carry DBA-authored `sql_variant` and `rowversion` columns
// (no OutSystems attribute type produces either). Three claims on a REAL
// SQL Server:
//
//   1. Read-back reconstructs both — `sql_variant` → Text + SqlVariant
//      storage; `rowversion` → Binary + RowVersion storage — with no
//      `sqlTypeCorrespondence.unknown` refusal. The catalog views report the
//      legacy name `timestamp` for the rowversion column, so this is also
//      the both-spellings proof against a live INFORMATION_SCHEMA.
//   2. A variant cell reads back CANONICAL: the boxed base-type value
//      (int / nvarchar / datetime) lands in the same invariant raw form the
//      dedicated categories use; NULL stays out-of-band.
//   3. The engine stamps what σ omits: generated rows carry no cell for the
//      engine-stamped column, the bulk projection omits the column, the load
//      lands, and every landed row holds a non-null stamp.
//   4. A VIEW is excluded BEFORE column type mapping: a view exposing a type
//      the mapping does not know (hierarchyid) neither refuses the read-back
//      nor reconstructs as a kind. (A view carries no data; the Twin probes
//      sys.views itself.)
// ============================================================================

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.Sql
open Projection.Tests.Fixtures   // mkTableId
open Projection.Tests.IRBuilders // mkModule, mkCatalog

[<RequireQualifiedAccess>]
module private VariantFixture =

    let mkKey (parts: string list) : SsKey =
        SsKey.synthesizedComposite "OS_VAR" parts |> Result.value

    let name (s: string) : Name = Name.create s |> Result.value

    let attr
        (key: SsKey) (logical: string) (physical: string)
        (ptype: PrimitiveType) (isPk: bool) (nullable: bool)
        (storage: SqlStorageType option) : Attribute =
        { Attribute.create key (name logical) ptype with
            Column = ColumnRealization.create physical nullable |> Result.value
            IsPrimaryKey = isPk
            IsMandatory = not nullable
            SqlStorage = storage }

    let kindKey = mkKey [ "Variant"; "Carriage" ]

    let mintKind () : Kind =
        Kind.create kindKey (name "VariantCarriage") (mkTableId "dbo" "VARIANT_CARRIAGE")
            [ attr (mkKey [ "Variant"; "Id" ])      "Id"      "ID"      Integer true  false None
              attr (mkKey [ "Variant"; "Payload" ]) "Payload" "PAYLOAD" Text    false true  (Some SqlStorageType.SqlVariant)
              attr (mkKey [ "Variant"; "Stamp" ])   "Stamp"   "STAMP"   Binary  false false (Some SqlStorageType.RowVersion) ]

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    let scalar (cnn: SqlConnection) (sql: string) : Task<string> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! v = cmd.ExecuteScalarAsync()
            return string v
        }

    /// Read one column back through the SAME formatter the read-back row
    /// loop uses; NULL is carried out-of-band, as the row loop carries it.
    let readRaw (cnn: SqlConnection) (typ: PrimitiveType) (sql: string) : Task<string list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            use! reader = cmd.ExecuteReaderAsync()
            let acc = ResizeArray<string>()
            let mutable go = true
            while go do
                let! more = reader.ReadAsync()
                if more then
                    if reader.IsDBNull 0 then acc.Add "<null>"
                    else acc.Add (ReadSide.formatRawValue typ (reader.GetValue 0))
                else go <- false
            return List.ofSeq acc
        }

[<Xunit.Collection("Docker-SqlServer")>]
type SqlVariantRowVersionReadbackTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``sql_variant + rowversion read back typed and canonical; the engine stamps what σ omits`` () =
        if not (VariantFixture.skipIfNoDocker "SqlVariantRowVersionReadback") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "VariantRowVersion" (fun cnn _connStr ->
                task {
                    do! Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[VARIANT_CARRIAGE] (\n"
                             + "  [ID] INT NOT NULL CONSTRAINT [PK_VARIANT_CARRIAGE] PRIMARY KEY,\n"
                             + "  [PAYLOAD] SQL_VARIANT NULL,\n"
                             + "  [STAMP] ROWVERSION NOT NULL);")
                    // One INSERT per row: a multi-row VALUES constructor folds
                    // the column to ONE common type (datetime outranks
                    // nvarchar) BEFORE the sql_variant conversion — separate
                    // statements keep each cell's base type.
                    do! Deploy.executeBatch cnn
                            ("INSERT INTO [dbo].[VARIANT_CARRIAGE] ([ID], [PAYLOAD]) VALUES (1, CAST(42 AS INT));\n"
                             + "INSERT INTO [dbo].[VARIANT_CARRIAGE] ([ID], [PAYLOAD]) VALUES (2, CAST(N'plain' AS NVARCHAR(50)));\n"
                             + "INSERT INTO [dbo].[VARIANT_CARRIAGE] ([ID], [PAYLOAD]) VALUES (3, CAST('2026-07-16T12:30:00' AS DATETIME));\n"
                             + "INSERT INTO [dbo].[VARIANT_CARRIAGE] ([ID], [PAYLOAD]) VALUES (4, NULL);")
                    // A view exposing a type the mapping does not know — it
                    // must be excluded BEFORE column type mapping (claim 4).
                    do! Deploy.executeBatch cnn
                            ("CREATE VIEW [dbo].[VARIANT_VIEW] AS\n"
                             + "  SELECT [ID], CAST(NULL AS HIERARCHYID) AS [Exotic] FROM [dbo].[VARIANT_CARRIAGE];")

                    // 1 — read-back reconstructs BOTH types, no refusal.
                    let! read = ReadSide.read cnn
                    let catalog =
                        match read with
                        | Ok c -> c
                        | Error es -> failwithf "readback refused: %A" es
                    let kind =
                        Catalog.allKinds catalog
                        |> List.find (fun k -> TableId.tableText k.Physical = "VARIANT_CARRIAGE")
                    let attrOf (colName: string) : Attribute =
                        kind.Attributes |> List.find (fun a -> ColumnRealization.columnNameText a.Column = colName)
                    let payload = attrOf "PAYLOAD"
                    Assert.Equal (Text, payload.Type)
                    Assert.Equal (Some SqlStorageType.SqlVariant, payload.SqlStorage)
                    let stamp = attrOf "STAMP"
                    Assert.Equal (Binary, stamp.Type)
                    Assert.Equal (Some SqlStorageType.RowVersion, stamp.SqlStorage)
                    // 4 — the view (with its unmappable hierarchyid column)
                    // neither refused the read-back (the Ok above) nor
                    // reconstructed as a kind.
                    Assert.False (
                        Catalog.allKinds catalog
                        |> List.exists (fun k -> TableId.tableText k.Physical = "VARIANT_VIEW"),
                        "a VIEW must be excluded before column type mapping")

                    // 2 — canonical variant carriage; NULL out-of-band.
                    let! payloadRaws =
                        VariantFixture.readRaw cnn Text
                            "SELECT [PAYLOAD] FROM [dbo].[VARIANT_CARRIAGE] ORDER BY [ID];"
                    Assert.Equal<string list>(
                        [ "42"
                          "plain"
                          RawValueCodec.formatDateTime (System.DateTime(2026, 7, 16, 12, 30, 0))
                          "<null>" ],
                        payloadRaws)
                    let! stampRaws =
                        VariantFixture.readRaw cnn Binary
                            "SELECT [STAMP] FROM [dbo].[VARIANT_CARRIAGE] ORDER BY [ID];"
                    for s in stampRaws do
                        Assert.Equal (16, s.Length)   // 8 engine-stamped bytes, hex carriage

                    // 3 — σ omits the engine-stamped column; the load lands;
                    // the engine stamps every row.
                    do! Deploy.executeBatch cnn "DELETE FROM [dbo].[VARIANT_CARRIAGE];"
                    let mintKind = VariantFixture.mintKind ()
                    let mintCatalog =
                        mkCatalog [ mkModule (VariantFixture.mkKey [ "Variant"; "M" ]) (VariantFixture.name "M") [ mintKind ] ]
                    let cfg =
                        { SyntheticConfig.defaultConfig with
                            VolumeByKind = Map.ofList [ VariantFixture.kindKey, VolumeTarget.Absolute 5 ] }
                    let rows = (SyntheticData.generate mintCatalog Profile.empty cfg 7UL).[VariantFixture.kindKey]
                    Assert.Equal (5, List.length rows)
                    for r in rows do
                        Assert.True (
                            StaticRow.value (VariantFixture.name "Stamp") r |> Option.isNone,
                            "σ must not generate a cell for an engine-stamped column")
                    do! Bulk.copyRows cnn mintKind.Physical (TransferCellShaping.toCellRows mintKind Set.empty rows)
                    let! landed = VariantFixture.scalar cnn "SELECT COUNT(*) FROM [dbo].[VARIANT_CARRIAGE];"
                    Assert.Equal ("5", landed)
                    let! stamped = VariantFixture.scalar cnn "SELECT COUNT(*) FROM [dbo].[VARIANT_CARRIAGE] WHERE [STAMP] IS NOT NULL;"
                    Assert.Equal ("5", stamped)
                }))
