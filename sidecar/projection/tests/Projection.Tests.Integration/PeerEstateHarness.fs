namespace Projection.Tests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.OssysSql
open Projection.Pipeline

/// THE SHARED two-environment peer-estate harness (2026-07-09, T3).
///
/// Two ephemeral OSSYS cells — a SOURCE (`OSUSR_*`) and a SINK (`OSUSR_X*`, the
/// espace-key shift) — bootstrapped, seeded with their metamodels, OSSYS-read
/// into two SsKey-aligned `Catalog` contracts, and wired into
/// `TransferConnections`. Extracted from the pattern `PeerAlignedTransferDockerTests`
/// and `PeerManagedGrantTransferDockerTests` hand-roll inline, so a NEW two-cell
/// witness is one `run2Cell` call: the harness bootstraps + reads the contracts,
/// and the body declares its own source/sink DATA + the transfer + the assertions.
/// The espace-invariance canary proved the two cells READ as one shape; this
/// harness is where a witness MOVES data between them.
module PeerEstateHarness =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    let value (r: Result<'a>) : 'a = Result.value r

    /// Cell A seed: the edge-case OSSYS estate (metamodel + empty `OSUSR_*`).
    let seedSource () : string = MetadataExtractionSql.readEdgeCaseSeed ()

    /// Cell B seed: the sibling espace cell — every `OSUSR_*` physical name
    /// shifted to `OSUSR_X*` (GUIDs / logical names / structure held fixed).
    let seedSink () : string = (seedSource ()) |> OssysSeedBuilder.withEspaceKey "X"

    /// Reseed the sink's IDENTITY ranges away from the source surrogates so a
    /// verbatim FK copy cannot pass — only a genuine remap through sink-minted
    /// keys satisfies the join assertions.
    let reseedSinkIdentities : string =
        "DBCC CHECKIDENT ('[dbo].[OSUSR_XDEF_CITY]', RESEED, 500); \
         DBCC CHECKIDENT ('[dbo].[OSUSR_XABC_CUSTOMER]', RESEED, 900);"

    /// Deterministic SOURCE rows: two cities (surrogates 1/2 via IDENTITY_INSERT)
    /// and two customers pointing at them.
    let sourceRows : string =
        "SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] ON; \
         INSERT INTO [dbo].[OSUSR_DEF_CITY] ([ID],[NAME],[ISACTIVE]) VALUES (1, N'Lisbon', 1), (2, N'Porto', 1); \
         SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] OFF; \
         SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] ON; \
         INSERT INTO [dbo].[OSUSR_ABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID]) VALUES \
             (10, N'alice@x', N'Alice', N'Almeida', 1), (11, N'bob@x', N'Bob', N'Barbosa', 2); \
         SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] OFF;"

    /// The OSSYS-read contract for one cell — the production acquisition path.
    let contractOf (cnn: SqlConnection) : Task<Result<Catalog>> = LiveModelRead.fromConnection cnn

    let countRows (cnn: SqlConnection) (table: string) : Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM %s;" table  // LINT-ALLOW: terminal test-SQL boundary; table is a test literal
            let! scalar = cmd.ExecuteScalarAsync()
            return int (unbox<int64> scalar)
        }

    let kindByLogicalName (contract: Catalog) (name: string) : Kind =
        Catalog.allKinds contract
        |> List.find (fun k -> System.String.Equals(Name.value k.Name, name, System.StringComparison.OrdinalIgnoreCase))

    /// Read a kind's live rows into `StaticRow`s (Values keyed by the LOGICAL
    /// attribute Name, so two SsKey-aligned cells' rows compare directly) — the
    /// shape `TransferImpact.Inputs` consumes, mirroring the go board's
    /// `--impact` read.
    let readKindRows (cnn: SqlConnection) (kind: Kind) : Task<StaticRow list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT * FROM [%s].[%s];" (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)  // LINT-ALLOW: terminal test-SQL boundary; validated TableId coordinates
            use! r = cmd.ExecuteReaderAsync()
            let ord = [ for i in 0 .. r.FieldCount - 1 -> r.GetName i, i ] |> Map.ofList
            let acc = System.Collections.Generic.List<StaticRow>()
            let mutable go = true
            while go do
                let! more = r.ReadAsync()
                if more then
                    let values =
                        kind.Attributes
                        |> List.choose (fun a ->
                            match Map.tryFind (ColumnRealization.columnNameText a.Column) ord with
                            | Some i -> Some (a.Name, (if r.IsDBNull i then "" else string (r.GetValue i)))
                            | None -> None)
                        |> Map.ofList
                    acc.Add { Identifier = kind.SsKey; Values = values }
                else go <- false
            return List.ofSeq acc
        }

    /// The apparatus over the two ephemeral databases (D9 `ConnectionRef.Raw`).
    let throughConnections (srcConnStr: string) (sinkConnStr: string) (reconcile: bool) (body: TransferConnections -> Task<'a>) : Task<'a> =
        task {
            let srcSub : Substrate =
                { Environment = Projection.Core.Environment.Qa; Role = SubstrateRole.Source; ConnectionRef = ConnectionRef.Raw srcConnStr }
            let sinkSub : Substrate =
                { Environment = Projection.Core.Environment.Uat; Role = SubstrateRole.Sink; ConnectionRef = ConnectionRef.Raw sinkConnStr }
            let connections = TransferConnections.create srcSub sinkSub reconcile |> value
            return! body connections
        }

    /// The sink-seed-parameterized combinator: bootstrap the two ephemeral cells,
    /// seed the source OSSYS metamodel and the sink via `sinkSeed` (applied to the
    /// source seed), read both contracts, and hand
    /// `(src, sink, srcConnStr, sinkConnStr, srcContract, sinkContract)` to the
    /// body. `run2Cell` fixes `sinkSeed` to the espace-key shift (SsKey-aligned
    /// renditions); the cloned-module witness passes `asClonedModule` (re-minted
    /// GUIDs — the pair aligns by NAME, not SsKey).
    let run2CellWith
        (fixture: EphemeralContainerFixture)
        (label: string)
        (sinkSeed: string -> string)
        (body: SqlConnection -> SqlConnection -> string -> string -> Catalog -> Catalog -> Task<'a>)
        : 'a =
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase (label + "Src") (fun src srcConnStr ->
                task {
                    do! Deploy.executeBatch src (seedSource ())
                    return!
                        fixture.WithEphemeralDatabase (label + "Sink") (fun sink sinkConnStr ->
                            task {
                                do! Deploy.executeBatch sink (sinkSeed (seedSource ()))
                                let! srcContractR = contractOf src
                                let! sinkContractR = contractOf sink
                                return! body src sink srcConnStr sinkConnStr (value srcContractR) (value sinkContractR)
                            })
                }))

    /// THE shared combinator: bootstrap the two ephemeral cells, seed both OSSYS
    /// metamodels (the sink via the espace-key shift — SsKey-aligned renditions),
    /// read both contracts, and hand them to the body. Source/sink DATA is the
    /// body's job (each witness declares its own).
    let run2Cell
        (fixture: EphemeralContainerFixture)
        (label: string)
        (body: SqlConnection -> SqlConnection -> string -> string -> Catalog -> Catalog -> Task<'a>)
        : 'a =
        run2CellWith fixture label (OssysSeedBuilder.withEspaceKey "X") body
