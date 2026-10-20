using System;
using System.Collections.Generic;
using DbChange.Kernel;
using DbChange.Kernel.Tests;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Io.Tests;

/// <summary>
/// The cases' models and plans: each catalog case's plan as the catalog writes DacFx's script, its models as tests/Kernel.Tests/
/// QuestionCases.cs writes them where the kernel holds the case, and its expectation in v3's words (V3_MILESTONES.md §3.1: HasRows is
/// TableEmpty, Orphan is ForeignKey, CheckFalse is Check, and a statement no row matches is unclassified). Where v3 differs from the
/// catalog, the case follows v3: a rebuild's IF EXISTS asks Rebuild, whose state holds whether the table has rows, and no HasRows (QST-63
/// to QST-65); a Scan is a precondition whose rows are counted, where the catalog writes "measures Rows(T) for Scan(T)"; and a key a table
/// the change creates declares is Unique, answered from the change (QST-45, QST-59, QST-69).
/// </summary>
public sealed partial class VocabularyCases
{
    /// <summary>The case of the KeyLength row the catalog holds none for: a unique constraint whose key passes 1,700 bytes.</summary>
    internal const string UniqueOverTheLimit = "a unique constraint over the key limit";

    /// <summary>The case of a key whose max column sorts before its int column: a max value has no bound, so the key passes the limit
    /// in either order of its columns.</summary>
    internal const string MaxBesideFixed = "an index over a max column beside an int column";

    private const string Guard = "RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127)"
        + " WITH NOWAIT";

    /// <summary>The schema the catalog's cases start from, pg (cases/README.md §2), as tests/Kernel.Tests/QuestionCases.cs writes it.</summary>
    internal static readonly Model Pg = Model.Of(
            "Account.Id int NOT NULL", "Account.Name nvarchar(100) NOT NULL", "Account.Region nvarchar(50) NULL",
            "Category.Id int NOT NULL", "Category.Code nvarchar(30) NOT NULL", "Category.IsActive bit NOT NULL",
            "Customer.Id int NOT NULL", "Customer.Name nvarchar(100) NOT NULL", "Customer.Email nvarchar(256) NULL",
            "Customer.ContactPhone nvarchar(40) NULL", "Customer.Region nvarchar(50) NULL", "Customer.AccountId int NULL",
            "CustomerAddress.Id int NOT NULL", "CustomerAddress.CustomerId int NOT NULL", "CustomerAddress.Line1 nvarchar(120) NOT NULL",
            "CustomerAddress.City nvarchar(80) NULL", "CustomerAddress.PostalCode nvarchar(20) NULL",
            "Order.Id int NOT NULL", "Order.CustomerId int NOT NULL", "Order.StatusId int NOT NULL", "Order.Total decimal(18,2) NOT NULL",
            "Order.StatusText nvarchar(20) NOT NULL",
            "OrderLine.Id int NOT NULL", "OrderLine.OrderId int NOT NULL", "OrderLine.LineNumber int NOT NULL",
            "OrderLine.Amount decimal(18,2) NOT NULL",
            "Product.Id int NOT NULL", "Product.Code nvarchar(50) NOT NULL", "Product.Name nvarchar(200) NULL",
            "Product.LegacyCode nvarchar(40) NOT NULL", "Product.CategoryId int NULL",
            "Status.Id int NOT NULL", "Status.Code nvarchar(20) NOT NULL", "Status.IsActive bit NOT NULL")
        .PrimaryKey("PK_Account_Id", "Account", "Id").PrimaryKey("PK_Category_Id", "Category", "Id")
        .PrimaryKey("PK_Customer_Id", "Customer", "Id").PrimaryKey("PK_CustomerAddress_Id", "CustomerAddress", "Id")
        .PrimaryKey("PK_Order_Id", "Order", "Id").PrimaryKey("PK_OrderLine_Id", "OrderLine", "Id")
        .PrimaryKey("PK_Product_Id", "Product", "Id").PrimaryKey("PK_Status_Id", "Status", "Id")
        .Default("DF_Category_IsActive", "Category", "IsActive", "((1))").Default("DF_Order_Total", "Order", "Total", "((0))")
        .Default("DF_Order_StatusText", "Order", "StatusText", "(N'Pending')")
        .Default("DF_OrderLine_Amount", "OrderLine", "Amount", "((0))")
        .Default("DF_Product_LegacyCode", "Product", "LegacyCode", "(N'LEGACY')")
        .Default("DF_Status_IsActive", "Status", "IsActive", "((1))")
        .UniqueIndex("UIX_Status_Code", "Status", "Code");

    private static readonly Model T = Pg.With("T.Id int NOT NULL", "T.Ref varchar(20) NULL");

    private static readonly Model Doc = Pg.With("Doc.Path nvarchar(460) NOT NULL SQL_Latin1_General_CP1_CI_AS",
        "Doc.Area nvarchar(400) NOT NULL SQL_Latin1_General_CP1_CI_AS", "Doc.Slug nvarchar(460) NOT NULL SQL_Latin1_General_CP1_CI_AS",
        "Doc.Body nvarchar(max) NULL SQL_Latin1_General_CP1_CI_AS");

    private static readonly Model Rebuilt = Pg.With("P.Id int NOT NULL").PrimaryKey("PK_P", "P", "Id");

    private static readonly Model Versioned = Pg.With("Customer.ValidFrom datetime2(7) NOT NULL", "Customer.ValidTo datetime2(7) NOT NULL",
            "CustomerHistory.Id int NOT NULL", "CustomerHistory.ValidFrom datetime2(7) NOT NULL", "CustomerHistory.ValidTo datetime2(7) NOT NULL")
        .Period("Customer", "ValidFrom", "ValidTo");

    private const string StatusSeed = """
        MERGE dbo.Status AS target
        USING (VALUES
            (1, N'Pending',   1),
            (2, N'Shipped',   1),
            (3, N'Cancelled', 1)
        ) AS source (Id, Code, IsActive)
            ON target.Id = source.Id
        WHEN NOT MATCHED BY TARGET THEN
            INSERT (Id, Code, IsActive) VALUES (source.Id, source.Code, source.IsActive);
        """;

    private const string Refunded = """
        MERGE dbo.Status AS target
        USING (VALUES
            (1, N'Pending',   1),
            (2, N'Shipped',   1),
            (3, N'Cancelled', 1),
            (4, N'Refunded',  1)
        ) AS source (Id, Code, IsActive)
            ON target.Id = source.Id
        WHEN NOT MATCHED BY TARGET THEN
            INSERT (Id, Code, IsActive) VALUES (source.Id, source.Code, source.IsActive);
        """;

    private const string OrderTypeSeed = """
        MERGE dbo.OrderType AS target USING (VALUES (1, N'Web'), (2, N'Store')) AS source (Id, Code) ON target.Id = source.Id
        WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Code) VALUES (source.Id, source.Code);
        """;

    private const string Backfill = """
        /*
         Pre-deploy: backfill blank emails.
        */
        UPDATE [dbo].[Customer] SET [Email] = N'unknown@example.invalid' WHERE [Email] IS NULL;
        GO
        PRINT 'Pre-deploy complete.';
        """;

    private const string TwoRelease = """
        IF EXISTS (SELECT 1 FROM dbo.Customer WHERE Email IS NULL)
            UPDATE dbo.Customer
                SET Email = N'unknown+' + CAST(Id AS NVARCHAR(20)) + N'@example.invalid'
                WHERE Email IS NULL;
        GO
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Customer')
                   AND name = 'Email' AND is_nullable = 1)
            ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(256) NOT NULL;
        """;

    private const string Rebuild63 = """
        PRINT N'Dropping Foreign Key [dbo].[FK_T_P]...';
        GO
        ALTER TABLE [dbo].[T] DROP CONSTRAINT [FK_T_P];
        GO
        PRINT N'Starting rebuilding table [dbo].[T]...';
        GO
        BEGIN TRANSACTION;
        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
        SET XACT_ABORT ON;
        CREATE TABLE [dbo].[tmp_ms_xx_T] (
            [Id]     INT           NOT NULL,
            [Ref]    VARCHAR (20)  NULL,
            [Code]   NVARCHAR (50) NOT NULL,
            [PId]    INT           NULL,
            [Amt]    INT           NULL,
            [RefNum] AS            (CONVERT (INT, [Ref])) PERSISTED,
            CONSTRAINT [tmp_ms_xx_constraint_PK_T1] PRIMARY KEY CLUSTERED ([Id] ASC)
        );
        IF EXISTS (SELECT TOP 1 1
                   FROM   [dbo].[T])
            BEGIN
                INSERT INTO [dbo].[tmp_ms_xx_T] ([Id], [Code], [Ref], [PId], [Amt])
                SELECT   [Id],
                         [Code],
                         [Ref],
                         [PId],
                         [Amt]
                FROM     [dbo].[T]
                ORDER BY [Id] ASC;
            END
        DROP TABLE [dbo].[T];
        EXECUTE sp_rename N'[dbo].[tmp_ms_xx_T]', N'T';
        EXECUTE sp_rename N'[dbo].[tmp_ms_xx_constraint_PK_T1]', N'PK_T', N'OBJECT';
        COMMIT TRANSACTION;
        SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
        GO
        PRINT N'Creating Index [dbo].[T].[UX_T_Code]...';
        GO
        CREATE UNIQUE NONCLUSTERED INDEX [UX_T_Code]
            ON [dbo].[T]([Code] ASC);
        GO
        ALTER TABLE [dbo].[T] WITH NOCHECK
            ADD CONSTRAINT [FK_T_P] FOREIGN KEY ([PId]) REFERENCES [dbo].[P] ([Id]);
        GO
        ALTER TABLE [dbo].[T] WITH CHECK CHECK CONSTRAINT [FK_T_P];
        GO
        """;

    private const string Rebuild64 = """
        PRINT N'Starting rebuilding table [dbo].[Category]...';
        GO
        BEGIN TRANSACTION;
        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
        SET XACT_ABORT ON;
        CREATE TABLE [dbo].[tmp_ms_xx_Category] (
            [Id]       INT           IDENTITY (1, 1) NOT NULL,
            [Code]     NVARCHAR (30) NOT NULL,
            [IsActive] BIT           NOT NULL,
            CONSTRAINT [tmp_ms_xx_constraint_PK_Category_Id1] PRIMARY KEY CLUSTERED ([Id] ASC)
        );
        IF EXISTS (SELECT TOP 1 1
                   FROM   [dbo].[Category])
            BEGIN
                SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Category] ON;
                INSERT INTO [dbo].[tmp_ms_xx_Category] ([Id], [Code], [IsActive])
                SELECT   [Id],
                         [Code],
                         [IsActive]
                FROM     [dbo].[Category]
                ORDER BY [Id] ASC;
                SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Category] OFF;
            END
        DROP TABLE [dbo].[Category];
        EXECUTE sp_rename N'[dbo].[tmp_ms_xx_Category]', N'Category';
        EXECUTE sp_rename N'[dbo].[tmp_ms_xx_constraint_PK_Category_Id1]', N'PK_Category_Id', N'OBJECT';
        COMMIT TRANSACTION;
        SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
        GO
        """;

    private const string Rebuild65 = """
        BEGIN TRANSACTION;
        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
        SET XACT_ABORT ON;
        CREATE TABLE [dbo].[tmp_ms_xx_T] (
            [Id]   INT           NOT NULL,
            [Ref]  VARCHAR (20)  NULL,
            [Code] NVARCHAR (10) NOT NULL,
            CONSTRAINT [tmp_ms_xx_constraint_PK_T1] PRIMARY KEY CLUSTERED ([Id] ASC)
        );
        IF EXISTS (SELECT TOP 1 1
                   FROM   [dbo].[T])
            BEGIN
                INSERT INTO [dbo].[tmp_ms_xx_T] ([Id], [Code], [Ref])
                SELECT   [Id],
                         [Code],
                         [Ref]
                FROM     [dbo].[T]
                ORDER BY [Id] ASC;
            END
        DROP TABLE [dbo].[T];
        EXECUTE sp_rename N'[dbo].[tmp_ms_xx_T]', N'T';
        EXECUTE sp_rename N'[dbo].[tmp_ms_xx_constraint_PK_T1]', N'PK_T', N'OBJECT';
        COMMIT TRANSACTION;
        SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
        GO
        """;

    /// <summary>The cases of DacFx's own statements: its frame, the kinds the property read finds, KeyLength, Rebuild and Computed.</summary>
    private static Dictionary<string, Case> Framed() => new(StringComparer.Ordinal)
    {
        // The header, the Query Store block and the frame around DacFx's statements ask nothing.
        ["QST-01"] = new(Pg, Pg, $"IF EXISTS (select top 1 1 from [dbo].[Customer])\n    {Guard}\nGO\n", [TableEmpty("Customer")]),
        ["QST-02"] = new(
            Pg.With("Customer.Code nvarchar(20) NOT NULL", "Customer.Email nvarchar(100) NULL"),
            Pg.With("Customer.Code nvarchar(10) NOT NULL", "Customer.Email nvarchar(100) NOT NULL")
                .UniqueIndex("UX_Customer_Code", "Customer", "Code")
                .ForeignKey("FK_Order_Customer", "Order", ["CustomerId"], "Customer", ["Id"]),
            $"""
            GO
            USE [$(DatabaseName)];
            GO
            IF EXISTS (SELECT 1
                       FROM   [master].[dbo].[sysdatabases]
                       WHERE  [name] = N'$(DatabaseName)')
                BEGIN
                    ALTER DATABASE [$(DatabaseName)]
                        SET QUERY_STORE (QUERY_CAPTURE_MODE = AUTO, OPERATION_MODE = READ_WRITE, MAX_STORAGE_SIZE_MB = 1000)
                        WITH ROLLBACK IMMEDIATE;
                END
            GO
            /*
            The type for column Code in table [dbo].[Customer] is currently  NVARCHAR (20) NOT NULL but is being changed to
            NVARCHAR (10) NOT NULL. Data loss could occur and deployment may fail if the column contains data that is incompatible
            with type  NVARCHAR (10) NOT NULL.
            */
            IF EXISTS (select top 1 1 from [dbo].[Customer])
                {Guard}
            GO
            PRINT N'Altering Table [dbo].[Customer]...';
            GO
            ALTER TABLE [dbo].[Customer] ALTER COLUMN [Code] NVARCHAR (10) NOT NULL;
            ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (100) NOT NULL;
            GO
            PRINT N'Creating Index [dbo].[Customer].[UX_Customer_Code]...';
            GO
            CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_Code]
                ON [dbo].[Customer]([Code] ASC);
            GO
            PRINT N'Creating Foreign Key [dbo].[FK_Order_Customer]...';
            GO
            ALTER TABLE [dbo].[Order] WITH NOCHECK
                ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]);
            GO
            PRINT N'Refreshing View [dbo].[vCustomer]...';
            GO
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER OFF;
            GO
            EXECUTE sp_refreshsqlmodule N'[dbo].[vCustomer]';
            GO
            SET ANSI_NULLS, QUOTED_IDENTIFIER ON;
            GO
            PRINT N'Checking existing data against newly created constraints';
            GO
            USE [$(DatabaseName)];
            GO
            ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Customer];
            GO
            PRINT N'Update complete.';
            GO
            """,
            [TableEmpty("Customer"), NotNull("Customer.Email"), Fits("Customer.Code", "nvarchar(20)", "nvarchar(10)"),
                Unique("Customer", "Code"), ForeignKey("Order", "Customer", ("CustomerId", "Id"))]),
        ["QST-04"] = new(Pg, Pg, """
            /*
            Deployment script for bd_probe
            This code was generated by a tool.
            Changes to this file may cause incorrect behavior and will be lost if
            the code is regenerated.
            */
            GO
            SET ANSI_NULLS, ANSI_PADDING, ANSI_WARNINGS, ARITHABORT, CONCAT_NULL_YIELDS_NULL, QUOTED_IDENTIFIER ON;
            SET NUMERIC_ROUNDABORT OFF;
            GO
            :setvar DatabaseName "bd_probe"
            :setvar DefaultFilePrefix "bd_probe"
            :setvar DefaultDataPath "/var/opt/mssql/data/"
            :setvar DefaultLogPath "/var/opt/mssql/data/"
            GO
            :on error exit
            GO
            /*
            Detect SQLCMD mode and disable script execution if SQLCMD mode is not supported.
            To re-enable the script after enabling SQLCMD mode, execute the following:
            SET NOEXEC OFF;
            */
            :setvar __IsSqlCmdEnabled "True"
            GO
            IF N'$(__IsSqlCmdEnabled)' NOT LIKE N'True'
                BEGIN
                    PRINT N'SQLCMD mode must be enabled to successfully execute this script.';
                    SET NOEXEC ON;
                END
            GO
            USE [$(DatabaseName)];
            GO
            """, []),
        ["QST-05"] = new(Pg, Pg, """
            IF EXISTS (SELECT 1
                       FROM   [master].[dbo].[sysdatabases]
                       WHERE  [name] = N'$(DatabaseName)')
                BEGIN
                    ALTER DATABASE [$(DatabaseName)]
                        SET QUERY_STORE (QUERY_CAPTURE_MODE = AUTO, OPERATION_MODE = READ_WRITE, MAX_STORAGE_SIZE_MB = 1000)
                        WITH ROLLBACK IMMEDIATE;
                END
            GO
            """, []),
        ["QST-06"] = new(Pg, Pg, """
            IF EXISTS (SELECT 1
                       FROM   [master].[dbo].[sysdatabases]
                       WHERE  [name] = N'$(DatabaseName)')
                BEGIN
                    ALTER DATABASE [$(DatabaseName)]
                        SET ANSI_NULLS ON,
                            ANSI_PADDING ON,
                            QUOTED_IDENTIFIER ON
                        WITH ROLLBACK IMMEDIATE;
                END
            GO
            """, []),
        ["QST-07"] = new(Pg, Pg, """
            /*
            The column [dbo].[Customer].[Email] is being dropped, data loss could occur.
            ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
            */
            -- CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_Email] ON [dbo].[Customer]([Email] ASC);
            PRINT N'Update complete.';
            GO
            """, []),
        ["QST-10"] = new(Pg, Pg.With("Customer.Segment nvarchar(20) NOT NULL"), $"""
            IF EXISTS (select top 1 1 from [dbo].[Customer])
                {Guard}
            GO
            PRINT N'Altering Table [dbo].[Customer]...';
            GO
            ALTER TABLE [dbo].[Customer]
                ADD [Segment] NVARCHAR (20) NOT NULL;
            GO
            """, [TableEmpty("Customer")]),
        ["QST-16"] = new(Pg, Pg.With("Customer.Email nvarchar(256) NOT NULL"), $"""
            IF EXISTS (select top 1 1 from [dbo].[Customer])
                {Guard}
            GO
            ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
            GO
            """, [TableEmpty("Customer"), NotNull("Customer.Email")]),

        // Unique, and KeyLength: a key whose widest value passes 1,700 bytes, or 900 for a clustered one.
        ["QST-35"] = new(Pg, Pg.UniqueIndex("UX_Product_Code", "Product", "Code"), """
            PRINT N'Creating Index [dbo].[Product].[UX_Product_Code]...';
            GO
            CREATE UNIQUE NONCLUSTERED INDEX [UX_Product_Code]
                ON [dbo].[Product]([Code] ASC);
            GO
            """, [Unique("Product", "Code")]),
        ["QST-41"] = new(Pg, Pg.UniqueConstraint("UQ_Product_Code", "Product", "Code"), """
            ALTER TABLE [dbo].[Product]
                ADD CONSTRAINT [UQ_Product_Code] UNIQUE NONCLUSTERED ([Code] ASC);
            GO
            """, [Unique("Product", "Code")]),
        ["QST-42"] = new(Pg.Dropping("PK_OrderLine_Id"),
            Pg.Dropping("PK_OrderLine_Id").PrimaryKey("PK_OrderLine", "OrderLine", "OrderId", "LineNumber"), """
            ALTER TABLE [dbo].[OrderLine]
                ADD CONSTRAINT [PK_OrderLine] PRIMARY KEY CLUSTERED ([OrderId] ASC, [LineNumber] ASC);
            GO
            """, [Unique("OrderLine", "OrderId", "LineNumber")]),
        ["QST-44"] = new(Pg, Pg.Index("IX_Order_StatusId", "Order", ["StatusId"]), """
            PRINT N'Creating Index [dbo].[Order].[IX_Order_StatusId]...';
            GO
            CREATE NONCLUSTERED INDEX [IX_Order_StatusId]
                ON [dbo].[Order]([StatusId] ASC);
            GO
            """, [Scan("Order")]),
        ["QST-45"] = new(Pg,
            Pg.With("CustomerPreference.Id int NOT NULL", "CustomerPreference.CustomerId int NOT NULL",
                    "CustomerPreference.PrefKey nvarchar(50) NOT NULL").Identity("CustomerPreference.Id")
                .PrimaryKey("PK_CustomerPreference_Id", "CustomerPreference", "Id")
                .UniqueIndex("UX_CustomerPreference_Key", "CustomerPreference", "CustomerId", "PrefKey"), """
            CREATE TABLE [dbo].[CustomerPreference] (
                [Id]         INT           IDENTITY (1, 1) NOT NULL,
                [CustomerId] INT           NOT NULL,
                [PrefKey]    NVARCHAR (50) NOT NULL,
                CONSTRAINT [PK_CustomerPreference_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
            );
            GO
            CREATE UNIQUE NONCLUSTERED INDEX [UX_CustomerPreference_Key]
                ON [dbo].[CustomerPreference]([CustomerId] ASC, [PrefKey] ASC);
            GO
            """, [Unique("CustomerPreference", "Id"), Unique("CustomerPreference", "CustomerId", "PrefKey")]),
        ["QST-47"] = new(Pg.With("N.Big nvarchar(1000) NULL"), Pg.With("N.Big nvarchar(1000) NULL").Index("IX_N_Big", "N", ["Big"]), """
            CREATE NONCLUSTERED INDEX [IX_N_Big]
                ON [dbo].[N]([Big] ASC);
            GO
            """, [KeyLength("N", "IX_N_Big", false, "Big")]),
        ["QST-48"] = new(Pg.With("N.A nvarchar(850) NULL", "N.B nvarchar(851) NULL"),
            Pg.With("N.A nvarchar(850) NULL", "N.B nvarchar(851) NULL").Index("IX_N_A", "N", ["A"]).Index("IX_N_B", "N", ["B"]), """
            CREATE NONCLUSTERED INDEX [IX_N_A]
                ON [dbo].[N]([A] ASC);
            GO
            CREATE NONCLUSTERED INDEX [IX_N_B]
                ON [dbo].[N]([B] ASC);
            GO
            """, [KeyLength("N", "IX_N_B", false, "B"), Scan("N")]),
        ["QST-49"] = new(Doc, Doc.PrimaryKey("PK_Doc", "Doc", "Path").UniqueIndex("UX_Doc_Area_Slug", "Doc", "Area", "Slug"), """
            ALTER TABLE [dbo].[Doc]
                ADD CONSTRAINT [PK_Doc] PRIMARY KEY CLUSTERED ([Path] ASC);
            GO
            CREATE UNIQUE NONCLUSTERED INDEX [UX_Doc_Area_Slug]
                ON [dbo].[Doc]([Area] ASC, [Slug] ASC)
                INCLUDE([Body]);
            GO
            """, [Unique("Doc", "Path"), Unique("Doc", "Area", "Slug"), KeyLength("Doc", "PK_Doc", true, "Path"),
                KeyLength("Doc", "UX_Doc_Area_Slug", false, "Area", "Slug")]),
        [UniqueOverTheLimit] = new(Doc.With("Doc.Path nvarchar(900) NOT NULL"),
            Doc.With("Doc.Path nvarchar(900) NOT NULL").UniqueConstraint("UQ_Doc_Path", "Doc", "Path"), """
            ALTER TABLE [dbo].[Doc]
                ADD CONSTRAINT [UQ_Doc_Path] UNIQUE NONCLUSTERED ([Path] ASC);
            GO
            """, [Unique("Doc", "Path"), KeyLength("Doc", "UQ_Doc_Path", false, "Path")]),
        [MaxBesideFixed] = new(Pg.With("N.Big nvarchar(max) NULL", "N.Id int NOT NULL"),
            Pg.With("N.Big nvarchar(max) NULL", "N.Id int NOT NULL").Index("IX_N_Big_Id", "N", ["Big", "Id"]), """
            CREATE NONCLUSTERED INDEX [IX_N_Big_Id]
                ON [dbo].[N]([Big] ASC, [Id] ASC);
            GO
            """, [KeyLength("N", "IX_N_Big_Id", false, "Big", "Id")]),

        // ForeignKey and Check stand where a statement validates them; Authored where a post-deploy seed filled the parent first.
        ["QST-50"] = new(Pg, Pg.ForeignKey("FK_Order_Customer", "Order", ["CustomerId"], "Customer", ["Id"]), """
            PRINT N'Creating Foreign Key [dbo].[FK_Order_Customer]...';
            GO
            ALTER TABLE [dbo].[Order] WITH NOCHECK
                ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]);
            GO
            PRINT N'Checking existing data against newly created constraints';
            GO
            USE [$(DatabaseName)];
            GO
            ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Customer];
            GO
            """, [ForeignKey("Order", "Customer", ("CustomerId", "Id"))]),
        ["QST-55"] = new(Pg, Pg, """
            ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Status];
            GO
            """, [], Unclassified: [1]),
        ["QST-56"] = new(Pg, Pg.ForeignKey("FK_Order_Status", "Order", ["StatusId"], "Status", ["Id"]), """
            ALTER TABLE [dbo].[Order] WITH NOCHECK
                ADD CONSTRAINT [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id]);
            GO
            """, []),
        ["QST-57"] = new(
            Pg.ForeignKey("FK_Order_Status", "Order", ["StatusId"], "Status", ["Id"], disabled: true)
                .ForeignKey("FK_Order_Customer", "Order", ["CustomerId"], "Customer", ["Id"]),
            Pg.ForeignKey("FK_Order_Status", "Order", ["StatusId"], "Status", ["Id"])
                .ForeignKey("FK_Order_Customer", "Order", ["CustomerId"], "Customer", ["Id"], disabled: true), """
            ALTER TABLE [dbo].[Order] CHECK CONSTRAINT [FK_Order_Status];
            GO
            ALTER TABLE [dbo].[Order] NOCHECK CONSTRAINT [FK_Order_Customer];
            GO
            """, []),
        ["QST-59"] = new(Pg.With("Order.OrderTypeId int NULL"),
            Pg.With("Order.OrderTypeId int NULL", "OrderType.Id int NOT NULL", "OrderType.Code nvarchar(20) NOT NULL")
                .PrimaryKey("PK_OrderType_Id", "OrderType", "Id")
                .ForeignKey("FK_Order_OrderType", "Order", ["OrderTypeId"], "OrderType", ["Id"]), $"""
            CREATE TABLE [dbo].[OrderType] (
                [Id]   INT           NOT NULL,
                [Code] NVARCHAR (20) NOT NULL,
                CONSTRAINT [PK_OrderType_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
            );
            GO
            ALTER TABLE [dbo].[Order] WITH NOCHECK
                ADD CONSTRAINT [FK_Order_OrderType] FOREIGN KEY ([OrderTypeId]) REFERENCES [dbo].[OrderType] ([Id]);
            GO
            {OrderTypeSeed}
            GO
            PRINT N'Checking existing data against newly created constraints';
            GO
            ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_OrderType];
            GO
            """, [Unique("OrderType", "Id"), Authored("Order", ScriptPart.PostDeploy, 1, "OrderTypeId")], PostDeploy: OrderTypeSeed),
        ["QST-60"] = new(Pg, Pg.Check("CK_Order_Total", "Order", "[Total]>(0)", ["Total"]), """
            ALTER TABLE [dbo].[Order] WITH NOCHECK
                ADD CONSTRAINT [CK_Order_Total] CHECK ([Total]>(0));
            GO
            PRINT N'Checking existing data against newly created constraints';
            GO
            ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [CK_Order_Total];
            GO
            """, [Check("Order", "[Total]>(0)", "Total")]),

        // Rebuild and Computed: a table copied into tmp_ms_xx_, and an expression computed over every row.
        ["QST-63"] = new(
            Rebuilt.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CS_AS", "T.Ref varchar(20) NULL", "T.PId int NULL",
                    "T.Amt int NULL").PrimaryKey("PK_T", "T", "Id").ForeignKey("FK_T_P", "T", ["PId"], "P", ["Id"]),
            Rebuilt.With("T.Id int NOT NULL", "T.Ref varchar(20) NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CS_AS", "T.PId int NULL",
                    "T.Amt int NULL").Computed("T.RefNum", "(CONVERT (INT, [Ref]))", persisted: true).PrimaryKey("PK_T", "T", "Id")
                .ForeignKey("FK_T_P", "T", ["PId"], "P", ["Id"]).UniqueIndex("UX_T_Code", "T", "Code"),
            Rebuild63, [Unique("T", "Code"), ForeignKey("T", "P", ("PId", "Id")), Rebuild("T"), Computed("T.RefNum", "(CONVERT (INT, [Ref]))")]),
        ["QST-64"] = new(Pg, Pg.Identity("Category.Id"), Rebuild64, [Rebuild("Category", "Id")]),
        ["QST-65"] = new(Pg.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL", "T.Ref varchar(20) NULL").PrimaryKey("PK_T", "T", "Id"),
            Pg.With("T.Id int NOT NULL", "T.Ref varchar(20) NULL", "T.Code nvarchar(10) NOT NULL").PrimaryKey("PK_T", "T", "Id"), Rebuild65,
            [Fits("T.Code", "nvarchar(50)", "nvarchar(10)"), Rebuild("T")]),
        ["QST-66"] = new(T, T.Computed("T.RefNum", "(CONVERT (INT, [Ref]))", persisted: true), """
            PRINT N'Altering Table [dbo].[T]...';
            GO
            ALTER TABLE [dbo].[T]
                ADD [RefNum] AS (CONVERT (INT, [Ref])) PERSISTED;
            GO
            """, [Computed("T.RefNum", "(CONVERT (INT, [Ref]))")]),
        ["QST-67"] = new(T, T.Computed("T.RefNum", "(CONVERT (INT, [Ref]))"), """
            ALTER TABLE [dbo].[T]
                ADD [RefNum] AS (CONVERT (INT, [Ref]));
            GO
            """, []),
        ["QST-68"] = new(T.Computed("T.RefNum", "(CONVERT (INT, [Ref]))"),
            T.Computed("T.RefNum", "(CONVERT (INT, [Ref]))").Index("IX_T_RefNum", "T", ["RefNum"]), """
            CREATE NONCLUSTERED INDEX [IX_T_RefNum]
                ON [dbo].[T]([RefNum] ASC);
            GO
            """, [Computed("T.RefNum", "(CONVERT (INT, [Ref]))")]),
    };
}
