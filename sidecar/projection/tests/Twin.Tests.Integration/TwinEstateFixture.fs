namespace Twin.Tests.Integration

open System.IO
open Projection.Core
open Twin.Core
open Twin.Runtime

/// THE TWIN — a fixture SSDT estate on disk + a dedicated twin container.
///
/// Each test class owns one: a temp directory holding authored table
/// scripts (Customer / Order / OrderLine, an FK chain with an IDENTITY
/// PK path), a Status lookup with a static-data lane (the estate's own
/// reference data — the K1 provided pool), and a `twin.json`. The
/// container is name+port isolated per fixture and force-removed on
/// both construction and disposal so a crashed prior run never leaks
/// into this one.
type TwinEstateFixture (containerName: string, port: int) =

    let root = Path.Combine(Path.GetTempPath(), "twin-e2e", System.Guid.NewGuid().ToString "N")

    let write (rel: string) (content: string) : unit =
        let full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))
        Directory.CreateDirectory(Path.GetDirectoryName full |> Option.ofObj |> Option.defaultValue root) |> ignore
        File.WriteAllText(full, content)

    let configJson =
        sprintf
            """{
  "estate": {
    "tables": "Tables/*.sql",
    "staticData": ["Data/StaticSeeds.sql"]
  },
  "container": { "name": "%s", "port": %d },
  "seed": 7,
  "defaultRows": 25,
  "scenarios": {
    "default": {},
    "skewed": {
      "tables": {
        "dbo.Order": {
          "rows": 40,
          "columns": {
            "Channel":  { "weights": { "Web": 8, "Store": 2 } },
            "PlacedOn": { "between": ["2026-01-01", "2026-03-31"], "skew": "late" } } },
        "dbo.OrderLine": { "perParent": { "dbo.Order": { "mean": 2.0 } } } },
      "pins": [
        { "table": "dbo.Customer",
          "rows": [ { "Id": 1000, "Name": "Canonical Test Customer", "Email": "canon@example.test",
                      "StatusId": 1, "CreatedOn": "2026-01-15" } ] } ] } }
}
"""
            containerName port

    do
        write "Tables/dbo.Status.sql"
            """CREATE TABLE [dbo].[Status] (
    [Id]   INT           NOT NULL,
    [Name] NVARCHAR(50)  NOT NULL,
    CONSTRAINT [PK_Status] PRIMARY KEY ([Id])
);
"""
        write "Tables/dbo.Customer.sql"
            """CREATE TABLE [dbo].[Customer] (
    [Id]        INT            IDENTITY(1,1) NOT NULL,
    [Name]      NVARCHAR(100)  NOT NULL,
    [Email]     NVARCHAR(250)  NOT NULL,
    [StatusId]  INT            NOT NULL,
    [CreatedOn] DATETIME2      NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""
        write "Tables/dbo.Order.sql"
            """CREATE TABLE [dbo].[Order] (
    [Id]         INT           IDENTITY(1,1) NOT NULL,
    [CustomerId] INT           NOT NULL,
    [StatusId]   INT           NOT NULL,
    [Channel]    NVARCHAR(20)  NOT NULL,
    [Total]      DECIMAL(18,2) NOT NULL,
    [PlacedOn]   DATETIME2     NOT NULL,
    CONSTRAINT [PK_Order] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""
        write "Tables/dbo.OrderLine.sql"
            """CREATE TABLE [dbo].[OrderLine] (
    [Id]       INT           IDENTITY(1,1) NOT NULL,
    [OrderId]  INT           NOT NULL,
    [Sku]      NVARCHAR(64)  NOT NULL,
    [Quantity] INT           NOT NULL,
    [Note]     NVARCHAR(200) NULL,
    CONSTRAINT [PK_OrderLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderLine_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id])
);
"""
        write "Data/StaticSeeds.sql"
            """MERGE INTO [dbo].[Status] AS t
USING (VALUES (1, N'Open'), (2, N'Closed'), (3, N'Pending')) AS s ([Id], [Name])
ON t.[Id] = s.[Id]
WHEN MATCHED AND t.[Name] <> s.[Name] THEN UPDATE SET [Name] = s.[Name]
WHEN NOT MATCHED BY TARGET THEN INSERT ([Id], [Name]) VALUES (s.[Id], s.[Name])
WHEN NOT MATCHED BY SOURCE THEN DELETE;
"""
        write "twin.json" configJson

    let config =
        match TwinConfig.parse configJson with
        | Ok c -> c
        | Error es -> failwithf "fixture twin.json did not parse: %A" (es |> List.map (fun e -> e.Code))

    do
        // A leaked container from a crashed prior run must not shape this one.
        (TwinContainer.remove config.Container).GetAwaiter().GetResult()
        |> ignore

    /// The estate's repository root.
    member _.Root : string = root

    /// The parsed fixture configuration.
    member _.Config : TwinConfig = config

    /// The raw twin.json text (the evidence-loop tests derive variants).
    member _.ConfigJson : string = configJson

    /// The twin-database connection string (documented local default password).
    member _.TwinConnectionString : string =
        TwinContainer.twinConnectionString config.Container TwinContainer.DefaultPassword

    /// Overwrite one estate file (the schema-evolution tests' lever).
    member _.Rewrite (rel: string) (content: string) : unit = write rel content

    interface System.IDisposable with
        member _.Dispose () =
            (TwinContainer.remove config.Container).GetAwaiter().GetResult() |> ignore
            try Directory.Delete(root, true) with _ -> ()

/// The schema-loop fixture (its own container + port).
type TwinSchemaEstateFixture () =
    inherit TwinEstateFixture ("twin-e2e-schema", 21533)

/// The mint-loop fixture (its own container + port).
type TwinMintEstateFixture () =
    inherit TwinEstateFixture ("twin-e2e-mint", 21633)
