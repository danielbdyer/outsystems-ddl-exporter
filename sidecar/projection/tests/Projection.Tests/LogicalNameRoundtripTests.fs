namespace Projection.Tests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT

// ---------------------------------------------------------------------------
// Slice D.1.b — V2.LogicalName extended-property roundtrip.
//
// V2 emits a `V2.LogicalName` extended property on every CREATE TABLE
// and every column. ReadSide queries `sys.extended_properties` for the
// property and hydrates `Kind.Name` / `Attribute.Name` from it.
// Backward-compat fallback: when the property is absent (pre-D.1.b
// deployed schemas; non-V2-emitted schemas), ReadSide falls back to
// `Name.create deployed_name` (the prior behavior).
//
// **Unit-level fixtures** (no Docker) verify the emission shape: the
// SSDT body contains `EXEC sys.sp_addextendedproperty @name =
// 'V2.LogicalName'` statements for every table + column, carrying the
// catalog's logical name value.
//
// **Integration fixtures** (Docker-bound) verify the full roundtrip:
// source → emit → deploy → ReadSide read → recovered catalog has
// `Kind.Name` matching the source's logical name. The test
// deliberately uses a fixture where `Kind.Physical.Table` is preserved
// distinct from `Kind.Name` (via the `Disabled` mode on the
// substitution pass) so the property's recovery role can be observed
// — without the divergence, the test would pass trivially.
// ---------------------------------------------------------------------------

module private LogicalNameRoundtripFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn
                "SKIP %s: Docker daemon not reachable. Set DOCKER_HOST or start the daemon to run roundtrip tests."
                label
            false

    let mustOk r =
        match r with
        | Ok v -> v
        | Error es ->
            let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
            invalidOp (sprintf "fixture: %s" codes)

    let name (s: string) : Name = Name.create s |> mustOk

    let kindKey (parts: string list) : SsKey =
        SsKey.synthesizedComposite "OS_KIND_D1B" parts |> mustOk

    let attrKey (parts: string list) : SsKey =
        SsKey.synthesizedComposite "OS_ATTR_D1B" parts |> mustOk

    let modKey (m: string) : SsKey =
        SsKey.synthesized "OS_MOD_D1B" m |> mustOk

    let tableId (schema: string) (table: string) : TableId =
        TableId.create schema table |> mustOk

    /// Catalog with deliberate logical-vs-physical divergence at both
    /// the kind and the attribute level. The OSSYS shape the slice
    /// targets: logical `Customer` / physical `OSUSR_ABC_CUSTOMER`;
    /// logical `Email` / physical `EMAIL`.
    let divergentCatalog () : Catalog =
        let emailAttr =
            let a = Attribute.create (attrKey ["Sales"; "Customer"; "Email"]) (name "Email") PrimitiveType.Text
            { a with Column = ColumnRealization.create ("EMAIL") (false) |> Result.value }
        let idAttr =
            let a = Attribute.create (attrKey ["Sales"; "Customer"; "Id"]) (name "Id") PrimitiveType.Integer
            { a with
                Column = ColumnRealization.create ("ID") (false) |> Result.value
                IsPrimaryKey = true
                IsMandatory = true }
        let customer =
            Kind.create
                (kindKey ["Sales"; "Customer"])
                (name "Customer")
                (tableId "dbo" "OSUSR_ABC_CUSTOMER")
                [ idAttr; emailAttr ]
        let salesModule =
            { SsKey              = modKey "Sales"
              Name               = name "Sales"
              Kinds              = [ customer ]
              IsActive           = true
              ExtendedProperties = [] }
        { Modules = [ salesModule ]; Sequences = [] }

    /// Render the divergent catalog's SSDT bundle to a single text
    /// body (concat per-kind files with GO). Applies the slice-D.1.a
    /// substitution chain so emission produces logical-shaped CREATE
    /// TABLE plus V2.LogicalName extended properties.
    let renderSsdtWithSubstitution (catalog: Catalog) : string =
        let substituted =
            catalog
            |> (LogicalTableEmission.registered LogicalTableEmission.Enabled).Run
            |> LineageDiagnostics.bind (fun c ->
                (LogicalColumnEmission.registered LogicalColumnEmission.Enabled).Run c)
            |> fun ld -> ld.Value.Value
        let artifact =
            match SsdtDdlEmitter.emitSlices substituted with
            | Ok a -> a
            | Error es -> failwithf "SsdtDdlEmitter.emitSlices: %A" es
        artifact
        |> ArtifactByKind.toMap
        |> Map.toList
        |> List.map (fun (_, f) -> f.Body)
        |> String.concat "\nGO\n"  // LINT-ALLOW: terminal SQL-batch joiner across per-kind SsdtFile bodies; segments are typed (each file.Body is rendered DDL); mirrors the precedent in CdcSilenceCrossEmitterFixtures.renderSsdt

    /// Render the divergent catalog's SSDT bundle WITHOUT the
    /// substitution chain. Emission produces physical-shaped CREATE
    /// TABLE (`[dbo].[OSUSR_ABC_CUSTOMER]`) but still includes
    /// `V2.LogicalName` extended properties carrying the logical
    /// names. Used by the integration roundtrip — ReadSide reads the
    /// physical-named table and recovers `Kind.Name = "Customer"`
    /// from the extended property, so the divergence survives the
    /// roundtrip end-to-end.
    let renderSsdtWithoutSubstitution (catalog: Catalog) : string =
        let artifact =
            match SsdtDdlEmitter.emitSlices catalog with
            | Ok a -> a
            | Error es -> failwithf "SsdtDdlEmitter.emitSlices: %A" es
        artifact
        |> ArtifactByKind.toMap
        |> Map.toList
        |> List.map (fun (_, f) -> f.Body)
        |> String.concat "\nGO\n"  // LINT-ALLOW: terminal SQL-batch joiner; same pattern as renderSsdtWithSubstitution

open LogicalNameRoundtripFixtures

// ---------------------------------------------------------------------------
// Unit fixtures — verify the V2.LogicalName extended-property emission
// shape. No Docker.
// ---------------------------------------------------------------------------

module LogicalNameRoundtripUnit =

    [<Fact>]
    let ``Slice D.1.b: SSDT body carries V2.LogicalName extended property at the table level`` () =
        let body = renderSsdtWithSubstitution (divergentCatalog ())
        Assert.Contains("@name = N'Projection.LogicalName'", body)
        Assert.Contains("@value = N'Customer'", body)

    [<Fact>]
    let ``Slice D.1.b: SSDT body carries V2.LogicalName for each column`` () =
        let body = renderSsdtWithSubstitution (divergentCatalog ())
        // Both attributes have divergent logical / physical names; both
        // emit a column-level V2.LogicalName entry.
        Assert.Contains("@value = N'Id'", body)
        Assert.Contains("@value = N'Email'", body)
        // At least one column-level V2.LogicalName has the COLUMN level2 scope.
        Assert.Contains("@level2type = N'COLUMN'", body)

    [<Fact>]
    let ``Slice D.1.b: V2.LogicalName emits even when substitution leaves names aligned`` () =
        // When logical = physical from the source, V2.LogicalName still
        // emits — robustness for downstream ReadSide which queries
        // unconditionally and treats absence as the V1-parity fallback.
        let aligned =
            let attr = Attribute.create (attrKey ["X"; "T"; "C"]) (name "C") PrimitiveType.Integer
            let kind = Kind.create (kindKey ["X"; "T"]) (name "T") (tableId "dbo" "T") [ attr ]
            let m =
                { SsKey = modKey "X"; Name = name "X"; Kinds = [ kind ]
                  IsActive = true; ExtendedProperties = [] }
            { Modules = [ m ]; Sequences = [] }
        let body = renderSsdtWithSubstitution aligned
        Assert.Contains("@name = N'Projection.LogicalName'", body)
        Assert.Contains("@value = N'T'", body)
        Assert.Contains("@value = N'C'", body)

// ---------------------------------------------------------------------------
// Integration fixtures — full roundtrip through an ephemeral SQL Server.
// Source emission deliberately uses Disabled mode so physical names
// survive in the deployed schema; ReadSide must recover `Kind.Name` /
// `Attribute.Name` from the V2.LogicalName property for the
// divergence to make it back to the reconstructed catalog.
// ---------------------------------------------------------------------------

[<Xunit.Collection("Docker-SqlServer")>]
type LogicalNameRoundtripIntegration(fixture: EphemeralContainerFixture) =

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``Slice D.1.b roundtrip: ReadSide recovers Kind.Name from V2.LogicalName property when deployed physical differs`` () =
        if not (skipIfNoDocker "d1b-roundtrip-kind") then () else
        let source = divergentCatalog ()
        let ssdt = renderSsdtWithoutSubstitution source
        let task =
            fixture.WithEphemeralDatabase "D1bRoundtrip" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn ssdt
                let! recoveredResult = ReadSide.read cnn
                match recoveredResult with
                | Error es -> return failwithf "ReadSide.read: %A" es
                | Ok recovered ->
                    let kinds = Catalog.allKinds recovered
                    let customer =
                        kinds
                        |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_ABC_CUSTOMER")
                    Assert.Equal("Customer", Name.value customer.Name)
                    // Physical preserved (substitution disabled at emit).
                    Assert.Equal("OSUSR_ABC_CUSTOMER", TableId.tableText customer.Physical)
                    return ()
            })
        task.GetAwaiter().GetResult()

    [<Fact>]
    member _.``Slice D.1.b roundtrip: ReadSide recovers Attribute.Name from V2.LogicalName property per column`` () =
        if not (skipIfNoDocker "d1b-roundtrip-column") then () else
        let source = divergentCatalog ()
        let ssdt = renderSsdtWithoutSubstitution source
        let task =
            fixture.WithEphemeralDatabase "D1bRoundtripCol" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn ssdt
                let! recoveredResult = ReadSide.read cnn
                match recoveredResult with
                | Error es -> return failwithf "ReadSide.read: %A" es
                | Ok recovered ->
                    let customer =
                        Catalog.allKinds recovered
                        |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_ABC_CUSTOMER")
                    let emailAttr =
                        customer.Attributes
                        |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "EMAIL")
                    // Logical recovered from extended property; physical
                    // preserved from the deployed column name.
                    Assert.Equal("Email", Name.value emailAttr.Name)
                    Assert.Equal("EMAIL", ColumnRealization.columnNameText emailAttr.Column)
                    let idAttr =
                        customer.Attributes
                        |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "ID")
                    Assert.Equal("Id", Name.value idAttr.Name)
                    Assert.Equal("ID", ColumnRealization.columnNameText idAttr.Column)
                    return ()
            })
        task.GetAwaiter().GetResult()

    [<Fact>]
    member _.``Slice D.1.b roundtrip: backward-compat fallback when V2.LogicalName property absent`` () =
        if not (skipIfNoDocker "d1b-roundtrip-fallback") then () else
        // Deploy a plain CREATE TABLE with no extended properties.
        // ReadSide must fall back to the deployed name as Kind.Name —
        // preserves pre-D.1.b roundtrip semantics for non-V2-emitted
        // schemas.
        let plainSql =
            "CREATE TABLE [dbo].[Legacy] ([Id] INT NOT NULL PRIMARY KEY, [Value] NVARCHAR(MAX) NULL);"
        let task =
            fixture.WithEphemeralDatabase "D1bRoundtripFallback" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn plainSql
                let! recoveredResult = ReadSide.read cnn
                match recoveredResult with
                | Error es -> return failwithf "ReadSide.read: %A" es
                | Ok recovered ->
                    let legacy =
                        Catalog.allKinds recovered
                        |> List.find (fun k -> TableId.tableText k.Physical = "Legacy")
                    // No V2.LogicalName property → Kind.Name = deployed name.
                    Assert.Equal("Legacy", Name.value legacy.Name)
                    let valueAttr =
                        legacy.Attributes
                        |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "Value")
                    Assert.Equal("Value", Name.value valueAttr.Name)
                    return ()
            })
        task.GetAwaiter().GetResult()
