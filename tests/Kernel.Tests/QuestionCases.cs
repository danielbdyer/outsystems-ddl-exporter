using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// The pure cases of the Facsimile's question catalog (tests/Cases/10-questions.md) for the kinds the first slice
/// of WP 2.1 holds, each under its identifier. A case gives the environment's model, the model the change brings and the plan's
/// BlockOnPossibleDataLoss sites, which WP 2.2's reader will find in DacFx's script; the preconditions the change asserts are the case's
/// questions in v3's words (V3_MILESTONES.md §3.1: HasRows is TableEmpty, Orphan is ForeignKey, CheckFalse is Check). What else a case
/// expects waits on its work package: the value classes of Fits and Unique, and the answer over a column the change creates (QST-46,
/// QST-62, QST-100) or a parent table it creates (QST-101), on WP 2.3, whose queries read the value the coordinate rules give each
/// existing row (CoordinatesTests) where the column's name would stand; its measures and the kinds only a plan's statements show on
/// WP 2.1's second slice; and the reading of the script's text on WP 2.2. Two of v3's decisions differ from the catalog and the
/// cases follow v3: any change to a column's type is Fits (decision 2.9), where the catalog asks nothing of a widening or a collation
/// alone (QST-19, QST-21, QST-33); and a key a table the change creates declares inline is Unique, answered from the change as any
/// precondition on that table is (QST-45, QST-58, QST-69, QST-101), where the catalog reads only a separate ADD.
/// </summary>
public sealed class QuestionCases
{
    /// <summary>The schema the catalog's cases start from, pg (cases/README.md §2), with its keys and defaults: the golden project's
    /// tables.</summary>
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

    /// <summary>Category moved to the archive schema, as ALTER SCHEMA … TRANSFER moves it: the table, and its key and default with
    /// it.</summary>
    internal static readonly Rename[] CategoryMoved =
    [
        new(Model.TableKey("Category"), Model.TableKey("archive.Category")),
        new(Key("PrimaryKeyConstraint", "dbo", "PK_Category_Id"), Key("PrimaryKeyConstraint", "archive", "PK_Category_Id")),
        new(Key("DefaultConstraint", "dbo", "DF_Category_IsActive"), Key("DefaultConstraint", "archive", "DF_Category_IsActive")),
    ];

    internal static readonly Rename ContactPhoneRenamed = Ok(Rename.Of(Model.ColumnKey("Customer.ContactPhone"), "MobileNumber"));

    private static readonly Model CustomerPreference = Pg.With("CustomerPreference.Id int NOT NULL",
            "CustomerPreference.CustomerId int NOT NULL", "CustomerPreference.PrefKey nvarchar(50) NOT NULL")
        .PrimaryKey("PK_CustomerPreference_Id", "CustomerPreference", "Id");

    /// <summary>A case: the environment's model, the model the change brings, the tables the plan's sites check, the preconditions due,
    /// the refactorlog's renames, and elements the change brings that no rule reads.</summary>
    internal sealed record Case(Model Base, Model Head, string[] Sites, Precondition[] Expected, Rename[]? Renames = null,
        Element[]? Also = null)
    {
        public SortedArray<Element> Before => Base.Elements;

        public SortedArray<Element> After => SortedArray.Of(Head.Elements.Concat(Also ?? []));

        public Change Change => Ok(Kernel.Change.Between(Before, After, SortedArray.Of(Renames ?? [])));

        /// <summary>One site per table named, in order, each protecting a statement of its own.</summary>
        public SortedArray<BlockOnPossibleDataLossSite> Checked =>
            SortedArray.Of(Sites.Select((t, i) => new BlockOnPossibleDataLossSite(Model.TableName(t), "the statement after check " + i)));
    }

    internal static readonly IReadOnlyDictionary<string, Case> Cases = new Dictionary<string, Case>(StringComparer.Ordinal)
    {
        // HasRows: a site is TableEmpty on its table, once per table, in its own schema.
        ["QST-01"] = new(Pg, Pg, ["Customer"], [TableEmpty("Customer")]),
        ["QST-02"] = new(
            Pg.With("Customer.Code nvarchar(20) NOT NULL", "Customer.Email nvarchar(100) NULL"),
            Pg.With("Customer.Code nvarchar(10) NOT NULL", "Customer.Email nvarchar(100) NOT NULL")
                .UniqueIndex("UX_Customer_Code", "Customer", "Code")
                .ForeignKey("FK_Order_Customer", "Order", ["CustomerId"], "Customer", ["Id"]),
            ["Customer"],
            [TableEmpty("Customer"), NotNull("Customer.Email"), Fits("Customer.Code", "nvarchar(20)", "nvarchar(10)"),
                Unique("Customer", "Code"), ForeignKey("Order", "Customer", ("CustomerId", "Id"))]),
        ["QST-08"] = new(Pg, Pg, ["Customer", "Product"], [TableEmpty("Customer"), TableEmpty("Product")]),
        ["QST-09"] = new(Pg, Pg, ["Customer", "Customer"], [TableEmpty("Customer")]),
        ["QST-10"] = new(Pg, Pg.With("Customer.Segment nvarchar(20) NOT NULL"), ["Customer"], [TableEmpty("Customer")]),
        ["QST-11"] = new(Pg, Pg, ["Order"], [TableEmpty("Order")]),
        ["QST-12"] = new(Pg.With("archive.OrderArchive.Id int NOT NULL"), Pg.With("archive.OrderArchive.Id int NOT NULL"),
            ["archive.OrderArchive"], [TableEmpty("archive.OrderArchive")]),

        // NotNull: Nullable from true to false, whatever the type does.
        ["QST-16"] = new(Pg, Pg.With("Customer.Email nvarchar(256) NOT NULL"), ["Customer"],
            [TableEmpty("Customer"), NotNull("Customer.Email")]),
        ["QST-17"] = new(Pg, Pg.With("Product.Code nvarchar(10) NOT NULL"), ["Product"],
            [TableEmpty("Product"), Fits("Product.Code", "nvarchar(50)", "nvarchar(10)")]),
        ["QST-18"] = new(Pg, Pg.With("Customer.Email nvarchar(100) NOT NULL"), ["Customer"],
            [TableEmpty("Customer"), NotNull("Customer.Email"), Fits("Customer.Email", "nvarchar(256)", "nvarchar(100)")]),

        // Fits: any change to the type, its facets or its collation; the aggregate query counts what fails to convert (WP 2.3).
        ["QST-19"] = new(Pg, Pg.With("Customer.Email nvarchar(400) NULL"), [], [Fits("Customer.Email", "nvarchar(256)", "nvarchar(400)")]),
        ["QST-20"] = new(Pg, Pg.With("CustomerAddress.Line1 nvarchar(120) NULL"), [], []),
        ["QST-21"] = new(Pg, Pg.With("OrderLine.LineNumber bigint NOT NULL"), [], [Fits("OrderLine.LineNumber", "int", "bigint")]),
        ["QST-22"] = new(Pg, Pg.With("Order.Total int NOT NULL"), ["Order"],
            [TableEmpty("Order"), Fits("Order.Total", "decimal(18,2)", "int")]),
        ["QST-23"] = new(Pg, Pg.With("Product.Code int NOT NULL"), ["Product"],
            [TableEmpty("Product"), Fits("Product.Code", "nvarchar(50)", "int")]),
        ["QST-24"] = new(Pg.With("T.Id int NOT NULL", "T.Ref varchar(20) NULL"), Pg.With("T.Id int NOT NULL", "T.Ref decimal(9,2) NULL"),
            ["T"], [TableEmpty("T"), Fits("T.Ref", "varchar(20)", "decimal(9,2)")]),
        ["QST-25"] = new(Pg.With("Customer.BirthText nvarchar(20) NULL"), Pg.With("Customer.BirthText date NULL"), ["Customer"],
            [TableEmpty("Customer"), Fits("Customer.BirthText", "nvarchar(20)", "date")]),
        ["QST-26"] = new(Pg.With("Account.ExternalKey nvarchar(36) NULL"), Pg.With("Account.ExternalKey uniqueidentifier NULL"),
            ["Account"], [TableEmpty("Account"), Fits("Account.ExternalKey", "nvarchar(36)", "uniqueidentifier")]),
        ["QST-27"] = new(Pg, Pg.With("OrderLine.Amount decimal(9,2) NOT NULL"), ["OrderLine"],
            [TableEmpty("OrderLine"), Fits("OrderLine.Amount", "decimal(18,2)", "decimal(9,2)")]),
        ["QST-28"] = new(Pg.With("OrderLine.Rate decimal(18,4) NOT NULL"), Pg.With("OrderLine.Rate decimal(18,2) NOT NULL"), ["OrderLine"],
            [TableEmpty("OrderLine"), Fits("OrderLine.Rate", "decimal(18,4)", "decimal(18,2)")]),
        ["QST-29"] = new(Pg, Pg.With("OrderLine.LineNumber smallint NOT NULL"), ["OrderLine"],
            [TableEmpty("OrderLine"), Fits("OrderLine.LineNumber", "int", "smallint")]),
        ["QST-30"] = new(Pg, Pg.With("Account.Name varchar(100) NOT NULL"), ["Account"],
            [TableEmpty("Account"), Fits("Account.Name", "nvarchar(100)", "varchar(100)")]),
        ["QST-31"] = new(Pg.With("Order.PlacedOn datetime2(7) NOT NULL"), Pg.With("Order.PlacedOn date NOT NULL"), ["Order"],
            [TableEmpty("Order"), Fits("Order.PlacedOn", "datetime2(7)", "date")]),
        ["QST-32"] = new(Pg, Pg.With("OrderLine.LineNumber nvarchar(5) NOT NULL"), ["OrderLine"],
            [TableEmpty("OrderLine"), Fits("OrderLine.LineNumber", "int", "nvarchar(5)")]),
        ["QST-33"] = new(
            Pg.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CS_AS", "T.Amt int NULL")
                .UniqueIndex("UX_T_Code", "T", "Code"),
            Pg.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CI_AS", "T.Amt int NULL", "T.Note nvarchar(20) NULL")
                .UniqueIndex("UX_T_Code", "T", "Code").Check("CK_T_Amt", "T", "[Amt]>(0)", ["Amt"]),
            [],
            [Fits("T.Code", "nvarchar(50) Latin1_General_CS_AS", "nvarchar(50) Latin1_General_CI_AS"), Unique("T", "Code"),
                Check("T", "[Amt]>(0)", "Amt")]),
        ["QST-34"] = new(Pg.With("T.Id int NOT NULL", "T.Ref varchar(20) NULL Latin1_General_CS_AS"),
            Pg.With("T.Id int NOT NULL", "T.Ref varchar(20) NULL Cyrillic_General_CI_AS"), [],
            [Fits("T.Ref", "varchar(20) Latin1_General_CS_AS", "varchar(20) Cyrillic_General_CI_AS")]),

        // Unique: a key the change creates or alters, and one it must build again over a column whose type changes.
        ["QST-35"] = new(Pg, Pg.UniqueIndex("UX_Product_Code", "Product", "Code"), [], [Unique("Product", "Code")]),
        ["QST-36"] = new(Pg, Pg.UniqueIndex("UX_Customer_Email", "Customer", "Email"), [], [Unique("Customer", "Email")]),
        ["QST-37"] = new(Pg, Pg.FilteredIndex("UX_Customer_Email", "Customer", ["Email"], "[Email] IS NOT NULL", "Email"), [],
            [Unique("Customer", ["Email"], "[Email] IS NOT NULL", "Email")]),
        ["QST-38"] = new(Pg.With("Account.Name nvarchar(100) NOT NULL Latin1_General_CI_AI"),
            Pg.With("Account.Name nvarchar(100) NOT NULL Latin1_General_CI_AI").UniqueIndex("UX_Account_Name", "Account", "Name"), [],
            [Unique("Account", "Name")]),
        ["QST-39"] = new(Pg.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CS_AS"),
            Pg.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CS_AS").UniqueIndex("UX_T_Code", "T", "Code"), [],
            [Unique("T", "Code")]),
        ["QST-40"] = new(Pg, Pg.UniqueIndex("UX_OrderLine_Order_Line", "OrderLine", "OrderId", "LineNumber"), [],
            [Unique("OrderLine", "OrderId", "LineNumber")]),
        ["QST-41"] = new(Pg, Pg.UniqueConstraint("UQ_Product_Code", "Product", "Code"), [], [Unique("Product", "Code")]),
        ["QST-42"] = new(Pg.Dropping("PK_OrderLine_Id"),
            Pg.Dropping("PK_OrderLine_Id").PrimaryKey("PK_OrderLine", "OrderLine", "OrderId", "LineNumber"), [],
            [Unique("OrderLine", "OrderId", "LineNumber")]),
        ["QST-43"] = new(Pg,
            Pg.UniqueIndex("UX_Product_Code", "Product", "Code").Changing("UX_Product_Code", r => r with { Included = ["Name"] }),
            [], [Unique("Product", "Code")]),
        ["QST-44"] = new(Pg, Pg.Index("IX_Order_StatusId", "Order", ["StatusId"]), [], []),
        ["QST-45"] = new(Pg, CustomerPreference.UniqueIndex("UX_CustomerPreference_Key", "CustomerPreference", "CustomerId", "PrefKey"), [],
            [Unique("CustomerPreference", "Id"), Unique("CustomerPreference", "CustomerId", "PrefKey")]),
        ["QST-46"] = new(Pg,
            Pg.With("Customer.ExternalRef nvarchar(40) NULL").UniqueIndex("UX_Customer_ExternalRef", "Customer", "ExternalRef"), [],
            [Unique("Customer", "ExternalRef")]),

        // Orphan and CheckFalse: a foreign key or a check the change creates or alters, enabled after it.
        ["QST-50"] = new(Pg, Pg.ForeignKey("FK_Order_Customer", "Order", ["CustomerId"], "Customer", ["Id"]), [],
            [ForeignKey("Order", "Customer", ("CustomerId", "Id"))]),
        ["QST-51"] = new(Pg.With("Shipment.Id int NOT NULL", "Shipment.OrderId int NULL", "Shipment.LineNumber int NULL"),
            Pg.With("Shipment.Id int NOT NULL", "Shipment.OrderId int NULL", "Shipment.LineNumber int NULL")
                .ForeignKey("FK_Shipment_OrderLine", "Shipment", ["OrderId", "LineNumber"], "OrderLine", ["OrderId", "LineNumber"]),
            [], [ForeignKey("Shipment", "OrderLine", ("OrderId", "OrderId"), ("LineNumber", "LineNumber"))]),
        ["QST-52"] = new(Pg.ForeignKey("FK_Order_Status", "Order", ["StatusId"], "Status", ["Id"]),
            Pg.ForeignKey("FK_Order_Status", "Order", ["StatusId"], "Status", ["Id"], onDelete: "Cascade"), [],
            [ForeignKey("Order", "Status", ("StatusId", "Id"))]),
        ["QST-53"] = new(
            Pg.With("T.Id int NOT NULL", "T.PId int NULL", "P.Id int NOT NULL").PrimaryKey("PK_P", "P", "Id")
                .ForeignKey("FK_T_P", "T", ["PId"], "P", ["Id"], disabled: true),
            Pg.With("T.Id int NOT NULL", "T.PId int NULL", "P.Id int NOT NULL").PrimaryKey("PK_P", "P", "Id")
                .ForeignKey("FK_T_P", "T", ["PId"], "P", ["Id"]),
            [], [ForeignKey("T", "P", ("PId", "Id"))]),
        ["QST-54"] = new(Pg.With("ref.Status.Id int NOT NULL").PrimaryKey("PK_Status_Id", "ref.Status", "Id"),
            Pg.With("ref.Status.Id int NOT NULL").PrimaryKey("PK_Status_Id", "ref.Status", "Id")
                .ForeignKey("FK_Order_RefStatus", "Order", ["StatusId"], "ref.Status", ["Id"]),
            [], [ForeignKey("Order", "ref.Status", ("StatusId", "Id"))]),
        ["QST-58"] = new(Pg,
            CustomerPreference.ForeignKey("FK_CustomerPreference_Customer", "CustomerPreference", ["CustomerId"], "Customer", ["Id"]), [],
            [Unique("CustomerPreference", "Id"), ForeignKey("CustomerPreference", "Customer", ("CustomerId", "Id"))]),
        ["QST-60"] = new(Pg, Pg.Check("CK_Order_Total", "Order", "[Total]>(0)", ["Total"]), [], [Check("Order", "[Total]>(0)", "Total")]),
        ["QST-61"] = new(Pg,
            Pg.Check("CK_Order_Total", "Order", "[Total]>(0)", ["Total"])
                .ForeignKey("FK_Order_Status", "Order", ["StatusId"], "Status", ["Id"]),
            [], [ForeignKey("Order", "Status", ("StatusId", "Id")), Check("Order", "[Total]>(0)", "Total")]),
        ["QST-62"] = new(Pg,
            Pg.With("Product.Qty int NULL", "Product.Rank int NOT NULL").Default("DF_Product_Rank", "Product", "Rank", "((0))")
                .Check("CK_Product_Qty", "Product", "[Qty]>(0)", ["Qty"]).Check("CK_Product_Rank", "Product", "[Rank]>(0)", ["Rank"]),
            [], [Check("Product", "[Qty]>(0)", "Qty"), Check("Product", "[Rank]>(0)", "Rank")]),
        ["QST-106"] = new(Pg.With("T.Id int NOT NULL", "T.Ref varchar(20) NULL"),
            Pg.With("T.Id int NOT NULL", "T.Ref varchar(20) NULL").Check("CK_T_RefNum", "T", "CONVERT([int],[Ref])>(0)", ["Ref"]), [],
            [Check("T", "CONVERT([int],[Ref])>(0)", "Ref")]),

        // Statements that read no rows ask nothing, and a drop asks only through its site.
        ["QST-69"] = new(Pg.With("Order.PlacedBy nvarchar(20) NULL"),
            Pg.With("Order.PlacedBy nvarchar(20) NULL", "archive.OrderArchive.Id int NOT NULL",
                    "archive.OrderArchive.CustomerId int NOT NULL", "Customer.MiddleName nvarchar(100) NULL")
                .PrimaryKey("PK_OrderArchive_Id", "archive.OrderArchive", "Id")
                .Default("DF_Order_PlacedBy", "Order", "PlacedBy", "(N'web')"),
            [], [Unique("archive.OrderArchive", "Id")],
            Also: [New(Ok(ElementKey.Of("Schema", Ok(Name.Of("archive")))), []),
                New(Key("View", "dbo", "vOrderOpen"), [("SelectStatement", new Value.Script("SELECT Id, CustomerId FROM dbo.[Order]"))]),
                New(Key("Procedure", "dbo", "pOrderCount"), [("Definition", new Value.Script("SELECT COUNT(*) FROM dbo.[Order]"))])]),
        ["QST-70"] = new(Pg,
            Pg.With("Customer.Segment nvarchar(20) NOT NULL").Default("DF_Customer_Segment", "Customer", "Segment", "(N'Standard')"),
            [], []),
        ["QST-71"] = new(Pg, Pg.RenameColumn("Customer.ContactPhone", "MobileNumber"), [], [], [ContactPhoneRenamed]),
        ["QST-72"] = new(Pg, Pg.MoveTable("Category", "archive"), [], [], CategoryMoved),
        ["QST-73"] = new(Pg, Pg.WithoutColumn("Product.LegacyCode"), ["Product"], [TableEmpty("Product")]),
        ["QST-74"] = new(Pg, Pg.WithoutTable("OrderLine"), ["OrderLine"], [TableEmpty("OrderLine")]),
        ["QST-75"] = new(Pg, Pg.WithoutColumn("Customer.ContactPhone").With("Customer.MobileNumber nvarchar(40) NULL"), ["Customer"],
            [TableEmpty("Customer")]),
        ["QST-76"] = new(
            Pg.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CI_AS", "T.Note nvarchar(20) NULL", "T.Amt int NULL")
                .UniqueIndex("UX_T_Code", "T", "Code").Check("CK_T_Amt", "T", "[Amt]>(0)", ["Amt"]),
            Pg.With("T.Id int NOT NULL", "T.Code nvarchar(40) NOT NULL", "T.Amt int NULL").UniqueIndex("UX_T_Code", "T", "Code"),
            ["T"], [TableEmpty("T"), Fits("T.Code", "nvarchar(50) Latin1_General_CI_AS", "nvarchar(40)"), Unique("T", "Code")]),
        ["QST-107"] = new(Pg.Index("IX_Order_StatusId", "Order", ["StatusId"]),
            Pg.Dropping("PK_Product_Id").Index("IX_Order_StatusId", "Order", ["StatusId", "CustomerId"]), [], []),

        // The coordinate rules' cases (CoordinateTests) read these preconditions where an environment holds them.
        ["QST-100"] = new(Pg,
            Pg.With("Product.Sku nvarchar(20) NOT NULL").Default("DF_Product_Sku", "Product", "Sku", "(N'NONE')")
                .UniqueIndex("UX_Product_Sku", "Product", "Sku"),
            [], [Unique("Product", "Sku")]),
        ["QST-101"] = new(Pg.With("Order.OrderTypeId int NULL"),
            Pg.With("Order.OrderTypeId int NULL", "OrderType.Id int NOT NULL", "OrderType.Code nvarchar(20) NOT NULL")
                .PrimaryKey("PK_OrderType_Id", "OrderType", "Id")
                .ForeignKey("FK_Order_OrderType", "Order", ["OrderTypeId"], "OrderType", ["Id"]),
            [], [Unique("OrderType", "Id"), ForeignKey("Order", "OrderType", ("OrderTypeId", "Id"))]),
        ["QST-102"] = new(Pg, Pg.RenameColumn("Customer.ContactPhone", "MobileNumber").With("Customer.MobileNumber nvarchar(40) NOT NULL"),
            ["Customer"], [TableEmpty("Customer"), NotNull("Customer.MobileNumber")], [ContactPhoneRenamed]),
        ["QST-103"] = new(Pg,
            Pg.MoveTable("Category", "archive").Check("CK_Category_Code", "archive.Category", "LEN([Code])>(0)", ["Code"]),
            [], [Check("archive.Category", "LEN([Code])>(0)", "Code")], CategoryMoved),
        ["QST-104"] = new(Pg.With("Customer.Nickname nvarchar(40) NULL"), Pg.With("Customer.Nickname nvarchar(40) NOT NULL"), ["Customer"],
            [TableEmpty("Customer"), NotNull("Customer.Nickname")]),
    };

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("QST-01")]
    [InlineData("QST-02")]
    [InlineData("QST-08")]
    [InlineData("QST-09")]
    [InlineData("QST-10")]
    [InlineData("QST-11")]
    [InlineData("QST-12")]
    [InlineData("QST-16")]
    [InlineData("QST-17")]
    [InlineData("QST-18")]
    [InlineData("QST-19")]
    [InlineData("QST-20")]
    [InlineData("QST-21")]
    [InlineData("QST-22")]
    [InlineData("QST-23")]
    [InlineData("QST-24")]
    [InlineData("QST-25")]
    [InlineData("QST-26")]
    [InlineData("QST-27")]
    [InlineData("QST-28")]
    [InlineData("QST-29")]
    [InlineData("QST-30")]
    [InlineData("QST-31")]
    [InlineData("QST-32")]
    [InlineData("QST-33")]
    [InlineData("QST-34")]
    [InlineData("QST-35")]
    [InlineData("QST-36")]
    [InlineData("QST-37")]
    [InlineData("QST-38")]
    [InlineData("QST-39")]
    [InlineData("QST-40")]
    [InlineData("QST-41")]
    [InlineData("QST-42")]
    [InlineData("QST-43")]
    [InlineData("QST-44")]
    [InlineData("QST-45")]
    [InlineData("QST-46")]
    [InlineData("QST-50")]
    [InlineData("QST-51")]
    [InlineData("QST-52")]
    [InlineData("QST-53")]
    [InlineData("QST-54")]
    [InlineData("QST-58")]
    [InlineData("QST-60")]
    [InlineData("QST-61")]
    [InlineData("QST-62")]
    [InlineData("QST-69")]
    [InlineData("QST-70")]
    [InlineData("QST-71")]
    [InlineData("QST-72")]
    [InlineData("QST-73")]
    [InlineData("QST-74")]
    [InlineData("QST-75")]
    [InlineData("QST-76")]
    [InlineData("QST-100")]
    [InlineData("QST-101")]
    [InlineData("QST-102")]
    [InlineData("QST-103")]
    [InlineData("QST-104")]
    [InlineData("QST-106")]
    [InlineData("QST-107")]
    public void A_pure_case_of_the_Facsimile_s_question_catalog_finds_its_preconditions_in_the_change(string id)
    {
        var c = Cases[id];

        Assert.Equal(SortedArray.Of(c.Expected), Ok(Preconditions.Of(c.Before, c.After, c.Change, c.Checked)));
    }

    /// <summary>
    /// Every pure case the catalog holds is ported here or left, by its identifier, to the work that holds what it asks: the reading of the
    /// script's text (WP 2.2), the kinds and statement shapes only the vocabulary of WP 2.1's second slice reads, and that vocabulary's own
    /// properties; so the catalog's identifiers survive the port, each with its place.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Every_pure_case_of_the_question_catalog_is_ported_or_left_to_the_work_that_holds_it()
    {
        var pure = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), "tests/Cases/10-questions.md"))
            .Split("\n### ").Skip(1).Where(c => c.Contains("\nkind:     pure", StringComparison.Ordinal))
            .Select(c => c[..c.IndexOf(" · ", StringComparison.Ordinal)]).ToList();

        Assert.Empty(Cases.Keys.Intersect(LeftOut.Keys));
        Assert.Equal(pure.Order(StringComparer.Ordinal), Cases.Keys.Concat(LeftOut.Keys).Order(StringComparer.Ordinal));
    }

    /// <summary>The catalog's pure cases this slice leaves, each to the work that holds what it asks.</summary>
    internal static readonly IReadOnlyDictionary<string, string> LeftOut = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["QST-03"] = "the script's text: an offline and a live script of one change (WP 2.2)",
        ["QST-04"] = "the script's text: its header and SQLCMD lines (WP 2.2)",
        ["QST-05"] = "the script's text: a database option's IF EXISTS (WP 2.2)",
        ["QST-06"] = "the script's text: database SET options (WP 2.2)",
        ["QST-07"] = "the script's text: comments (WP 2.2)",
        ["QST-13"] = "the script's text: a check's spelling (WP 2.2)",
        ["QST-14"] = "the script's text: a RAISERROR at another state (WP 2.2)",
        ["QST-15"] = "the script's text: a check written without WITH NOWAIT (WP 2.2)",
        ["QST-47"] = "KeyLength (WP 2.1, second slice)",
        ["QST-48"] = "KeyLength (WP 2.1, second slice)",
        ["QST-49"] = "KeyLength (WP 2.1, second slice)",
        ["QST-55"] = "a statement shape the vocabulary reads (WP 2.1, second slice)",
        ["QST-56"] = "a statement shape the vocabulary reads (WP 2.1, second slice)",
        ["QST-57"] = "a statement shape the vocabulary reads (WP 2.1, second slice)",
        ["QST-59"] = "Authored (WP 2.1, second slice)",
        ["QST-63"] = "Rebuild (WP 2.1, second slice)",
        ["QST-64"] = "Rebuild (WP 2.1, second slice)",
        ["QST-65"] = "Rebuild (WP 2.1, second slice)",
        ["QST-66"] = "Computed (WP 2.1, second slice)",
        ["QST-67"] = "Computed (WP 2.1, second slice)",
        ["QST-68"] = "Computed (WP 2.1, second slice)",
        ["QST-77"] = "the script's text: batch separators (WP 2.2)",
        ["QST-78"] = "Authored (WP 2.1, second slice)",
        ["QST-79"] = "SeedDrift (WP 2.1, second slice)",
        ["QST-80"] = "SeedDrift (WP 2.1, second slice)",
        ["QST-81"] = "Authored (WP 2.1, second slice)",
        ["QST-82"] = "Authored (WP 2.1, second slice)",
        ["QST-83"] = "Authored (WP 2.1, second slice)",
        ["QST-84"] = "an unknown statement (WP 2.1, second slice)",
        ["QST-85"] = "an unknown statement (WP 2.1, second slice)",
        ["QST-86"] = "an unknown statement (WP 2.1, second slice)",
        ["QST-87"] = "an unknown statement (WP 2.1, second slice)",
        ["QST-88"] = "an unknown statement (WP 2.1, second slice)",
        ["QST-89"] = "Temporal (WP 2.1, second slice)",
        ["QST-90"] = "an unknown statement (WP 2.1, second slice)",
        ["QST-91"] = "an unknown statement (WP 2.1, second slice)",
        ["QST-92"] = "an unknown statement (WP 2.1, second slice)",
        ["QST-93"] = "the vocabulary's own properties (WP 2.1, second slice)",
        ["QST-94"] = "the vocabulary's own properties (WP 2.1, second slice)",
        ["QST-95"] = "the vocabulary's own properties (WP 2.1, second slice)",
        ["QST-96"] = "the script reader's determinism (WP 2.2)",
        ["QST-97"] = "the script's text: encodings and line endings (WP 2.2)",
        ["QST-98"] = "the script's text: bracketed names (WP 2.2)",
        ["QST-99"] = "the vocabulary's own properties (WP 2.1, second slice)",
        ["QST-105"] = "Temporal (WP 2.1, second slice)",
    };

    internal static Precondition TableEmpty(string table) => new(Model.TableName(table), [], new PreconditionKind.TableEmpty());

    internal static Precondition NotNull(string column) => On(column, new PreconditionKind.NotNull());

    internal static Precondition Fits(string column, string before, string after) =>
        On(column, new PreconditionKind.Fits(Model.Type(before), Model.Type(after)));

    internal static Precondition Unique(string table, params string[] columns) =>
        new(Model.TableName(table), Names(columns), new PreconditionKind.Unique(null, []));

    internal static Precondition Unique(string table, string[] columns, string filter, params string[] reads) =>
        new(Model.TableName(table), Names(columns), new PreconditionKind.Unique(filter, Names(reads)));

    internal static Precondition ForeignKey(string table, string parent, params (string Column, string Referenced)[] pairs) =>
        new(Model.TableName(table), Names(pairs.Select(p => p.Column)), new PreconditionKind.ForeignKey(Model.TableName(parent),
            SortedArray.Of(pairs.Select(p => new PreconditionKind.ForeignKey.Pair(Ok(Name.Of(p.Column)), Ok(Name.Of(p.Referenced)))))));

    internal static Precondition Check(string table, string expression, params string[] reads) =>
        new(Model.TableName(table), Names(reads), new PreconditionKind.Check(expression));

    internal static SortedArray<Name> Names(IEnumerable<string> names) => SortedArray.Of(names.Select(n => Ok(Name.Of(n))));

    private static Precondition On(string column, PreconditionKind kind)
    {
        var key = Model.ColumnKey(column);
        return new Precondition(key.Parent!.Name, [key.Name], kind);
    }

    private static string Root()
    {
        var at = AppContext.BaseDirectory;
        while (!System.IO.File.Exists(System.IO.Path.Combine(at, "DbChange.sln")))
        {
            at = System.IO.Path.GetDirectoryName(at)
                ?? throw new InvalidOperationException("No DbChange.sln above " + AppContext.BaseDirectory);
        }

        return at;
    }
}
