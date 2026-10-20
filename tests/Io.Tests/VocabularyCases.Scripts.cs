using System;
using System.Collections.Generic;
using DbChange.Kernel;
using DbChange.Kernel.Tests;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Io.Tests;

/// <summary>The cases of statements that read no row, of the change's deploy scripts, of the unknown statements, of Temporal and of a
/// Scan of a dropped clustered key.</summary>
public sealed partial class VocabularyCases
{
    /// <summary>v3's case of a changed deploy script that executes a procedure no row reads, beside one a row reads.</summary>
    internal const string ExecutedByAScript = "a procedure a changed post-deploy script executes";

    private const string Executes = "EXEC [dbo].[pFixOrders];\nGO\nEXEC sp_addextendedproperty N'MS_Description', N'Fixed', N'SCHEMA', N'dbo';";

    private static readonly Rename ContactPhoneRenamed = Ok(Rename.Of(Model.ColumnKey("Customer.ContactPhone"), "MobileNumber"));

    /// <summary>Category moved to the archive schema, as ALTER SCHEMA … TRANSFER moves it: the table, and its key and default with
    /// it.</summary>
    private static readonly Rename[] CategoryMoved =
    [
        new(Model.TableKey("Category"), Model.TableKey("archive.Category")),
        new(Key("PrimaryKeyConstraint", "dbo", "PK_Category_Id"), Key("PrimaryKeyConstraint", "archive", "PK_Category_Id")),
        new(Key("DefaultConstraint", "dbo", "DF_Category_IsActive"), Key("DefaultConstraint", "archive", "DF_Category_IsActive")),
    ];

    private static Dictionary<string, Case> Scripted() => new(StringComparer.Ordinal)
    {
        // Statements that read no rows ask nothing; a drop asks only through its BlockOnPossibleDataLoss check.
        ["QST-69"] = new(Pg.With("Order.PlacedBy nvarchar(20) NULL"),
            Pg.With("Order.PlacedBy nvarchar(20) NULL", "archive.OrderArchive.Id int NOT NULL", "archive.OrderArchive.CustomerId int NOT NULL",
                    "Customer.MiddleName nvarchar(100) NULL")
                .PrimaryKey("PK_OrderArchive_Id", "archive.OrderArchive", "Id")
                .Default("DF_Order_PlacedBy", "Order", "PlacedBy", "(N'web')"), """
            CREATE SCHEMA [archive]
                AUTHORIZATION [dbo];
            GO
            CREATE TABLE [archive].[OrderArchive] (
                [Id]         INT NOT NULL,
                [CustomerId] INT NOT NULL,
                CONSTRAINT [PK_OrderArchive_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
            );
            GO
            ALTER TABLE [dbo].[Customer]
                ADD [MiddleName] NVARCHAR (100) NULL;
            GO
            ALTER TABLE [dbo].[Order]
                ADD CONSTRAINT [DF_Order_PlacedBy] DEFAULT (N'web') FOR [PlacedBy];
            GO
            CREATE VIEW [dbo].[vOrderOpen]
            AS SELECT Id, CustomerId FROM dbo.[Order] WHERE StatusId = 1;
            GO
            CREATE PROCEDURE [dbo].[pOrderCount]
            AS SELECT COUNT(*) FROM dbo.[Order];
            GO
            GRANT SELECT ON OBJECT::[dbo].[vOrderOpen] TO [app_reader];
            GO
            EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Open orders', @level0type = N'SCHEMA',
                @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'vOrderOpen';
            GO
            """, [Unique("archive.OrderArchive", "Id")]),
        ["QST-70"] = new(Pg,
            Pg.With("Customer.Segment nvarchar(20) NOT NULL").Default("DF_Customer_Segment", "Customer", "Segment", "(N'Standard')"), """
            ALTER TABLE [dbo].[Customer] ADD [Segment] NVARCHAR (20) CONSTRAINT [DF_Customer_Segment] DEFAULT (N'Standard') NOT NULL;
            GO
            """, [Scan("Customer")]),
        ["QST-71"] = new(Pg, Pg.RenameColumn("Customer.ContactPhone", "MobileNumber"), """
            PRINT N'The following operation was generated from a refactoring log file 3f2a1c9e-1111-4a2b-9c3d-0123456789ab';
            PRINT N'Rename [dbo].[Customer].[ContactPhone] to MobileNumber';
            GO
            EXECUTE sp_rename @objname = N'[dbo].[Customer].[ContactPhone]', @newname = N'MobileNumber', @objtype = N'COLUMN';
            GO
            IF OBJECT_ID(N'dbo.__RefactorLog') IS NULL
            BEGIN
                CREATE TABLE [dbo].[__RefactorLog] (OperationKey UNIQUEIDENTIFIER NOT NULL PRIMARY KEY)
                EXEC sp_addextendedproperty N'microsoft_database_tools_support', N'refactoring log', N'schema', N'dbo', N'table',
                    N'__RefactorLog'
            END
            GO
            IF NOT EXISTS (SELECT OperationKey FROM [dbo].[__RefactorLog] WHERE OperationKey = '3f2a1c9e-1111-4a2b-9c3d-0123456789ab')
            INSERT INTO [dbo].[__RefactorLog] (OperationKey) values ('3f2a1c9e-1111-4a2b-9c3d-0123456789ab')
            GO
            """, [], Renames: [ContactPhoneRenamed]),
        ["QST-72"] = new(Pg, Pg.MoveTable("Category", "archive"), """
            ALTER SCHEMA [archive] TRANSFER [dbo].[Category];
            GO
            """, [], Renames: CategoryMoved),
        ["QST-73"] = new(Pg, Pg.WithoutColumn("Product.LegacyCode"), $"""
            /*
            The column [dbo].[Product].[LegacyCode] is being dropped, data loss could occur.
            */
            IF EXISTS (select top 1 1 from [dbo].[Product])
                {Guard}
            GO
            PRINT N'Dropping Default Constraint [dbo].[DF_Product_LegacyCode]...';
            GO
            ALTER TABLE [dbo].[Product] DROP CONSTRAINT [DF_Product_LegacyCode];
            GO
            ALTER TABLE [dbo].[Product] DROP COLUMN [LegacyCode];
            GO
            """, [TableEmpty("Product")]),
        ["QST-74"] = new(Pg, Pg.WithoutTable("OrderLine"), $"""
            /*
            The table [dbo].[OrderLine] is being dropped, data loss could occur.
            */
            IF EXISTS (select top 1 1 from [dbo].[OrderLine])
                {Guard}
            GO
            PRINT N'Dropping Table [dbo].[OrderLine]...';
            GO
            DROP TABLE [dbo].[OrderLine];
            GO
            """, [TableEmpty("OrderLine")]),
        ["QST-76"] = new(
            Pg.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CI_AS", "T.Note nvarchar(20) NULL", "T.Amt int NULL")
                .UniqueIndex("UX_T_Code", "T", "Code").Check("CK_T_Amt", "T", "[Amt]>(0)", ["Amt"]),
            Pg.With("T.Id int NOT NULL", "T.Code nvarchar(40) NOT NULL", "T.Amt int NULL").UniqueIndex("UX_T_Code", "T", "Code"), $"""
            /*
            The column [dbo].[T].[Note] is being dropped, data loss could occur.
            */
            IF EXISTS (select top 1 1 from [dbo].[T])
                {Guard}
            GO
            PRINT N'Dropping Index [dbo].[T].[UX_T_Code]...';
            GO
            DROP INDEX [UX_T_Code]
                ON [dbo].[T];
            GO
            PRINT N'Dropping Check Constraint [dbo].[CK_T_Amt]...';
            GO
            ALTER TABLE [dbo].[T] DROP CONSTRAINT [CK_T_Amt];
            GO
            PRINT N'Altering Table [dbo].[T]...';
            GO
            ALTER TABLE [dbo].[T] DROP COLUMN [Note];
            GO
            ALTER TABLE [dbo].[T] ALTER COLUMN [Code] NVARCHAR (40) NOT NULL;
            GO
            PRINT N'Creating Index [dbo].[T].[UX_T_Code]...';
            GO
            CREATE UNIQUE NONCLUSTERED INDEX [UX_T_Code]
                ON [dbo].[T]([Code] ASC);
            GO
            """, [TableEmpty("T"), Fits("T.Code", "nvarchar(50) Latin1_General_CI_AS", "nvarchar(40)"), Unique("T", "Code")]),

        // Batches, and the change's own deploy scripts, read by their text against the scripts before the change.
        ["QST-77"] = new(Pg, Pg.With("Customer.GoLiveDate date NULL").Index("IX_Customer_GoLiveDate", "Customer", ["GoLiveDate"]), """
            PRINT N'GO';
            GO
            ALTER TABLE [dbo].[Customer]
                ADD [GoLiveDate] DATE NULL;
            go
            CREATE NONCLUSTERED INDEX [IX_Customer_GoLiveDate]
                ON [dbo].[Customer]([GoLiveDate] ASC);
            GO
            """, [Scan("Customer")]),
        ["QST-78"] = new(Pg, Pg.With("Customer.Email nvarchar(256) NOT NULL"), $"""
            GO
            USE [$(DatabaseName)];
            GO
            {Backfill}
            GO
            GO
            IF EXISTS (select top 1 1 from [dbo].[Customer])
                {Guard}
            GO
            ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
            GO
            """, [TableEmpty("Customer"), NotNull("Customer.Email"), Authored("Customer", ScriptPart.PreDeploy, 4, "Email")],
            PreDeploy: Backfill, PreDeployBefore: "/*\n Pre-deploy: backfill blank emails.\n*/\nGO\nPRINT 'Pre-deploy complete.';\n"),
        ["QST-79"] = new(Pg, Pg, $"SET NOCOUNT ON;\nGO\n{StatusSeed}\nGO\n", [], PostDeploy: $"SET NOCOUNT ON;\nGO\n{StatusSeed}",
            PostDeployBefore: $"SET NOCOUNT ON;\nGO\n{StatusSeed}"),
        ["QST-80"] = new(Pg, Pg, Refunded + "\nGO\n", [SeedDrift("Status", ["Id"], ["Code"], ScriptPart.PostDeploy, 1)], PostDeploy: Refunded,
            PostDeployBefore: StatusSeed),
        ["QST-81"] = new(Pg, Pg, $"""
            GO
            USE [$(DatabaseName)];
            GO
            IF EXISTS (select top 1 1 from [dbo].[Customer])
                {Guard}
            GO
            PRINT 'Pre-deploy complete.';
            GO
            GO
            PRINT N'Creating Foreign Key [dbo].[FK_Order_Customer]...';
            GO
            """, [Authored("Customer", ScriptPart.PreDeploy, 1)],
            PreDeploy: $"IF EXISTS (select top 1 1 from [dbo].[Customer])\n    {Guard}\nGO\nPRINT 'Pre-deploy complete.';",
            PreDeployBefore: "PRINT 'Pre-deploy complete.';"),
        ["QST-82"] = new(Pg, Pg, $"""
            GO
            USE [$(DatabaseName)];
            GO
            {TwoRelease}
            GO
            PRINT N'Update complete.';
            GO
            """, [Authored("Customer", ScriptPart.PreDeploy, 1, "Email"), Authored("Customer", ScriptPart.PreDeploy, 6, "Email")],
            PreDeploy: TwoRelease, PreDeployBefore: ""),
        [ExecutedByAScript] = new(Pg, Pg, "PRINT N'Update complete.';\nGO\n" + Executes + "\nGO\n", [], Unclassified: [1],
            PostDeploy: Executes, PostDeployBefore: ""),

        // Unknown statements: one that reads rows and that no row matches is an unclassified change, by its line.
        ["QST-14"] = new(Pg, Pg,
            "GO\nIF EXISTS (select top 1 1 from [dbo].[Customer])\n    " + Guard.Replace("16, 127", "16, 1", StringComparison.Ordinal) + "\nGO\n",
            [], Unclassified: [2]),
        ["QST-84"] = new(Pg, Pg, """
            PRINT N'Switching [dbo].[Order]...';
            GO
            ALTER TABLE [dbo].[Order] SWITCH TO [archive].[Order];
            GO
            """, [], Unclassified: [3]),
        ["QST-85"] = new(Pg, Pg, """
            CREATE FULLTEXT INDEX ON [dbo].[Product]
                ([Name] LANGUAGE 1033)
                KEY INDEX [PK_Product_Id]
                ON [ftCatalog];
            GO
            """, [], Unclassified: [1]),
        ["QST-86"] = new(Pg, Pg, "UPDATE STATISTICS [dbo].[Customer];\nGO\n", [], Unclassified: [1]),
        ["QST-87"] = new(Pg, Pg, """
            INSERT INTO [archive].[OrderArchive] ([Id], [CustomerId])
            SELECT [Id], [CustomerId]
            FROM   [dbo].[Order];
            GO
            """, [], Unclassified: [1]),
        ["QST-88"] = new(Pg, Pg, """
            ALTER INDEX [IX_Order_StatusId]
                ON [dbo].[Order] REBUILD WITH(DATA_COMPRESSION = PAGE);
            GO
            """, [], Unclassified: [1]),
        ["QST-89"] = new(Versioned, Versioned, """
            ALTER TABLE [dbo].[Customer]
                SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE=[dbo].[CustomerHistory], DATA_CONSISTENCY_CHECK=ON));
            GO
            """, [Temporal("CustomerHistory", "ValidFrom", "ValidTo", "Customer")]),
        ["QST-90"] = new(Pg, Pg.With("Customer.Email nvarchar(256) NOT NULL"), """
            UPDATE [dbo].[Customer]
            SET    [Email] = N''
            WHERE  [Email] IS NULL;
            GO
            ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
            GO
            """, [NotNull("Customer.Email")], Unclassified: [1]),
        ["QST-91"] = new(Pg, Pg, """
            ALTER TABLE [dbo].[Order] SWITCH TO [archive].[Order];
            GO
            UPDATE STATISTICS [dbo].[Customer];
            GO
            """, [], Unclassified: [1, 3]),
        ["QST-92"] = new(Pg, Pg, """
            EXECUTE sp_refreshsqlmodule N'[dbo].[vCustomer]';
            GO
            EXECUTE sp_rename @objname = N'[dbo].[Customer].[ContactPhone]', @newname = N'MobileNumber', @objtype = N'COLUMN';
            GO
            EXECUTE [dbo].[pReconcileOrders];
            GO
            """, [], Unclassified: [5]),

        // The plan's three classes, a period over existing or new columns, and a dropped clustered key.
        ["QST-99"] = new(Pg,
            Pg.With("Customer.Email nvarchar(256) NOT NULL", "Product.Note nvarchar(50) NULL").Index("IX_Order_StatusId", "Order", ["StatusId"]),
            $"""
            IF EXISTS (select top 1 1 from [dbo].[Customer])
                {Guard}
            GO
            ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;
            GO
            CREATE NONCLUSTERED INDEX [IX_Order_StatusId]
                ON [dbo].[Order]([StatusId] ASC);
            GO
            ALTER TABLE [dbo].[Product]
                ADD [Note] NVARCHAR (50) NULL;
            GO
            EXECUTE sp_refreshsqlmodule N'[dbo].[vCustomer]';
            GO
            """, [TableEmpty("Customer"), NotNull("Customer.Email"), Scan("Order")]),
        ["QST-105 (a)"] = new(Pg.With("Account.ValidFrom datetime2(7) NOT NULL", "Account.ValidTo datetime2(7) NOT NULL"),
            Pg.With("Account.ValidFrom datetime2(7) NOT NULL", "Account.ValidTo datetime2(7) NOT NULL").Period("Account", "ValidFrom", "ValidTo"),
            """
            ALTER TABLE [dbo].[Account]
                ADD PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]);
            GO
            """, [Temporal("Account", "ValidFrom", "ValidTo", null)]),
        ["QST-105 (b)"] = new(Pg,
            Pg.With("Account.ValidFrom datetime2(7) NOT NULL", "Account.ValidTo datetime2(7) NOT NULL").Period("Account", "ValidFrom", "ValidTo")
                .Default("DF_Account_ValidFrom", "Account", "ValidFrom", "(sysutcdatetime())")
                .Default("DF_Account_ValidTo", "Account", "ValidTo", "('9999-12-31 23:59:59.9999999')"), """
            ALTER TABLE [dbo].[Account]
                ADD [ValidFrom] DATETIME2 (7) CONSTRAINT [DF_Account_ValidFrom] DEFAULT (sysutcdatetime()) NOT NULL,
                    [ValidTo]   DATETIME2 (7) CONSTRAINT [DF_Account_ValidTo] DEFAULT ('9999-12-31 23:59:59.9999999') NOT NULL;
            GO
            ALTER TABLE [dbo].[Account]
                ADD PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]);
            GO
            """, [Scan("Account")]),
        ["QST-107"] = new(Pg.Index("IX_Order_StatusId", "Order", ["StatusId"]),
            Pg.Dropping("PK_Product_Id").Index("IX_Order_StatusId", "Order", ["StatusId", "CustomerId"]), """
            ALTER TABLE [dbo].[Product] DROP CONSTRAINT [PK_Product_Id];
            GO
            DROP INDEX [IX_Order_StatusId]
                ON [dbo].[Order];
            GO
            CREATE NONCLUSTERED INDEX [IX_Order_StatusId]
                ON [dbo].[Order]([StatusId] ASC, [CustomerId] ASC);
            GO
            """, [Scan("Product"), Scan("Order")]),
    };
}
