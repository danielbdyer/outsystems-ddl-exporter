namespace Projection.Tests

// The two digest planes' agreement, per SQL type (T17 wave B3 — DECISIONS
// 2026-07-15, the fidelity chapter opens, entry 3; ADMIRE row M1.8). The
// client-canonical SHA256 plane is AUTHORITATIVE; the server HASHBYTES
// fast-path is a projection of the same form under the model's name
// bridge. The planes never compare values across each other — the law is
// VERDICT co-variance: for every carried SQL type, identical data in the
// two renditions reads EQUAL on both planes, and one flipped cell reads
// DIFFERENT on both planes. Kinds the projection cannot carry descend by
// name (`supportOf` — the pure facts below need no container).
// Serial via the Docker-SqlServer collection.

open Xunit
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.Sql
open Projection.Tests.IRBuilders
open Projection.Tests.Fixtures

[<Xunit.Collection("Docker-SqlServer")>]
module ServerDigestDockerTests =

    let private skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    // -- the pure capability facts (no container) -------------------------------

    let private kindWithPk (ptype: PrimitiveType) : Kind =
        Kind.create
            (kindKey [ "SdCap" ])
            (mkName "SdCap")
            (mkTableId "dbo" "OSUSR_SD_CAP")
            [ { Attribute.create (attrKey [ "SdCap"; "Key" ]) (mkName "Key") ptype with
                  Column = ColumnRealization.create "KEY_COL" false |> Result.value
                  IsPrimaryKey = true } ]

    [<Fact>]
    let ``supportOf: integer and guid keys carry the fast path; text, composite, and absent keys descend by name`` () =
        Assert.Equal(ServerDigestSupport.Supported, ServerDigest.supportOf (kindWithPk Integer))
        Assert.Equal(ServerDigestSupport.Supported, ServerDigest.supportOf (kindWithPk Guid))
        match ServerDigest.supportOf (kindWithPk Text) with
        | ServerDigestSupport.Unsupported reason -> Assert.Contains("canonical plane", reason)
        | other -> Assert.Fail(sprintf "expected Unsupported; got %A" other)
        let composite =
            { kindWithPk Integer with
                Attributes =
                    [ { Attribute.create (attrKey [ "SdCap"; "A" ]) (mkName "A") Integer with IsPrimaryKey = true }
                      { Attribute.create (attrKey [ "SdCap"; "B" ]) (mkName "B") Integer with IsPrimaryKey = true } ] }
        match ServerDigest.supportOf composite with
        | ServerDigestSupport.Unsupported reason -> Assert.Contains("composite", reason)
        | other -> Assert.Fail(sprintf "expected Unsupported; got %A" other)
        let keyless = { kindWithPk Integer with Attributes = [] }
        match ServerDigest.supportOf keyless with
        | ServerDigestSupport.Unsupported reason -> Assert.Contains("no primary key", reason)
        | other -> Assert.Fail(sprintf "expected Unsupported; got %A" other)

    // -- the per-type agreement witness ------------------------------------------

    type private DigestCase =
        { Label   : string
          VType   : PrimitiveType
          SqlType : string
          Value   : string
          Flipped : string }

    let private cases : DigestCase list =
        [ { Label = "Int";      VType = Integer;  SqlType = "INT";              Value = "7";                                        Flipped = "999" }
          { Label = "Dec";      VType = Decimal;  SqlType = "DECIMAL(19,4)";    Value = "12.5000";                                  Flipped = "13.0000" }
          { Label = "Text";     VType = Text;     SqlType = "NVARCHAR(100)";    Value = "N'alpha'";                                 Flipped = "N'omega'" }
          { Label = "Bool";     VType = Boolean;  SqlType = "BIT";              Value = "1";                                        Flipped = "0" }
          { Label = "DateTime"; VType = DateTime; SqlType = "DATETIME2";        Value = "'2026-01-02T03:04:05'";                    Flipped = "'2027-01-02T03:04:05'" }
          { Label = "Date";     VType = Date;     SqlType = "DATE";             Value = "'2026-01-02'";                             Flipped = "'2026-01-03'" }
          { Label = "Time";     VType = Time;     SqlType = "TIME";             Value = "'03:04:05'";                               Flipped = "'03:04:06'" }
          { Label = "Bin";      VType = Binary;   SqlType = "VARBINARY(50)";    Value = "0x0102";                                   Flipped = "0x0103" }
          { Label = "Guid";     VType = Guid;     SqlType = "UNIQUEIDENTIFIER"; Value = "'6F9619FF-8B86-D011-B42D-00C04FC964FF'";   Flipped = "'00000000-0000-0000-0000-000000000001'" } ]

    /// One case's model: kind Sd<Label>, integer PK Id + one V column of the
    /// case's type, in its OSUSR physical realization.
    let private modelOf (case: DigestCase) : Catalog =
        let kindName = "Sd" + case.Label
        let kind =
            Kind.create
                (kindKey [ kindName ])
                (mkName kindName)
                (mkTableId "dbo" ("OSUSR_SD_" + case.Label.ToUpperInvariant()))
                [ { Attribute.create (attrKey [ kindName; "Id" ]) (mkName "Id") Integer with
                      Column = ColumnRealization.create "ID" false |> Result.value
                      IsPrimaryKey = true }
                  { Attribute.create (attrKey [ kindName; "V" ]) (mkName "V") case.VType with
                      Column = ColumnRealization.create "V_COL" false |> Result.value } ]
        mkCatalog [ mkModule (modKey ("SdMod" + case.Label)) (mkName ("SdMod" + case.Label)) [ kind ] ]

    let private tableOf (kind: Kind) : string =
        sprintf "[%s].[%s]" (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)

    let private columnOf (kind: Kind) (logical: string) : string =
        ColumnRealization.columnNameText
            (kind.Attributes |> List.find (fun a -> Name.value a.Name = logical)).Column

    /// One rendition's DDL + two identical-valued rows, derived from the
    /// rendition kind itself (the seed and the digest read one shape).
    let private seedFor (case: DigestCase) (kind: Kind) : string =
        sprintf "CREATE TABLE %s ([%s] INT NOT NULL PRIMARY KEY, [%s] %s NULL); INSERT INTO %s ([%s],[%s]) VALUES (1, %s), (2, %s);"
            (tableOf kind) (columnOf kind "Id") (columnOf kind "V") case.SqlType
            (tableOf kind) (columnOf kind "Id") (columnOf kind "V") case.Value case.Value

    let rec private foldAll
        (basis: RowBasis)
        (pull: AsyncStream<RowQuantum>)
        (state: RowDigestFold.State)
        : System.Threading.Tasks.Task<RowDigestFold.State> =
        task {
            let! head = pull ()
            match head with
            | None -> return state
            | Some q -> return! foldAll basis pull (RowDigestFold.addQuantum basis state q)
        }

    let private clientDigest
        (cnn: SqlConnection)
        (renameMap: Map<Name, Name>)
        (kind: Kind)
        : System.Threading.Tasks.Task<RowDigestFold.TableDigest> =
        task {
            let basis = RowBasis.rename renameMap (Kind.rowBasis kind)
            let! folded = foldAll basis (Ingestion.streamKind cnn kind) RowDigestFold.empty
            return RowDigestFold.finalize folded
        }

    /// One case's whole verdict-co-variance check — module-level (FS3511:
    /// the loop body stays free of `use`/`rec` inside the task `for`).
    let private verifyCase
        (cnn: SqlConnection)
        (case: DigestCase)
        (physicalKind: Kind)
        (logicalKind: Kind)
        (renameMap: Map<Name, Name>)
        : System.Threading.Tasks.Task<unit> =
        task {
            // -- identical data: both planes read EQUAL across the gap
            let! serverPhysR = ServerDigest.digest cnn renameMap physicalKind
            let! serverLogiR = ServerDigest.digest cnn Map.empty logicalKind
            let serverPhys = Result.value serverPhysR
            let serverLogi = Result.value serverLogiR
            Assert.True((serverPhys = serverLogi), sprintf "%s: the server plane split on identical data" case.Label)
            let! clientPhys = clientDigest cnn renameMap physicalKind
            let! clientLogi = clientDigest cnn Map.empty logicalKind
            Assert.True((clientPhys = clientLogi), sprintf "%s: the client plane split on identical data" case.Label)
            // -- one flipped cell: both planes read DIFFERENT
            use flip = cnn.CreateCommand()
            flip.CommandText <-
                sprintf "UPDATE %s SET [%s] = %s WHERE [%s] = 2;"
                    (tableOf logicalKind) (columnOf logicalKind "V") case.Flipped (columnOf logicalKind "Id")
            let! _ = flip.ExecuteNonQueryAsync()
            let! serverLogiFlippedR = ServerDigest.digest cnn Map.empty logicalKind
            let serverLogiFlipped = Result.value serverLogiFlippedR
            Assert.True((serverPhys <> serverLogiFlipped), sprintf "%s: the server plane missed a flipped cell" case.Label)
            let! clientLogiFlipped = clientDigest cnn Map.empty logicalKind
            Assert.True((clientPhys <> clientLogiFlipped), sprintf "%s: the client plane missed a flipped cell" case.Label)
        }

    /// Walk every case sequentially — module-level `rec` (FS3511: the task
    /// `for` over the case list did not statically compile).
    let rec private verifyCases
        (cnn: SqlConnection)
        (entries: (DigestCase * Kind * Kind * Map<Name, Name>) list)
        : System.Threading.Tasks.Task<int> =
        task {
            match entries with
            | [] -> return 0
            | (case, physicalKind, logicalKind, renameMap) :: rest ->
                do! verifyCase cnn case physicalKind logicalKind renameMap
                return! verifyCases cnn rest
        }

    [<Fact>]
    let ``digest planes: per SQL type, identical data across the renditions reads equal on BOTH planes and one flipped cell reads different on BOTH (verdict co-variance)`` () =
        let label = "ServerDigestPlanes"
        if not (skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            task {
                let renditions =
                    cases
                    |> List.map (fun case ->
                        let model = modelOf case
                        let physicalKind = CatalogRendition.physical model |> Catalog.allKinds |> List.head
                        let logicalKind = CatalogRendition.logical model |> Catalog.allKinds |> List.head
                        let renameMap =
                            RenameProjection.forKind logicalKind.SsKey
                                (RenameProjection.renameMapByKind
                                    (RenameProjection.renames
                                        (CatalogDiff.between (CatalogRendition.physical model) (CatalogRendition.logical model))))
                        case, physicalKind, logicalKind, renameMap)
                let seed =
                    renditions
                    |> List.collect (fun (case, physicalKind, logicalKind, _) ->
                        [ seedFor case physicalKind; seedFor case logicalKind ])
                    |> String.concat " "
                let! result =
                    Deploy.withBootstrappedDatabase label seed (fun cnn -> verifyCases cnn renditions)
                Assert.Equal(0, result)
            })

    [<Fact>]
    let ``digest planes: an empty kind reads as the projection's own empty identity`` () =
        let label = "ServerDigestEmpty"
        if not (skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            task {
                let model = modelOf (cases |> List.head)
                let physicalKind = CatalogRendition.physical model |> Catalog.allKinds |> List.head
                let seed =
                    sprintf "CREATE TABLE %s ([%s] INT NOT NULL PRIMARY KEY, [%s] INT NULL);"
                        (tableOf physicalKind) (columnOf physicalKind "Id") (columnOf physicalKind "V")
                let! digestR =
                    Deploy.withBootstrappedDatabase label seed (fun cnn ->
                        ServerDigest.digest cnn Map.empty physicalKind)
                Assert.Equal(ServerDigest.emptyDigest, Result.value digestR)
            })
