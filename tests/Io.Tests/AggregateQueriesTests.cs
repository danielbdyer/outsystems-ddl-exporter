using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DbChange.Kernel;
using DbChange.Kernel.Tests;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;
using Class = DbChange.Io.AggregateQueries.Class;

namespace DbChange.Io.Tests;

/// <summary>
/// io/AggregateQueries without a server (V3_MILESTONES.md WP 2.3; VALUES.md P2): every builder's query passes the allowlist and converts
/// a value only through TRY_CONVERT and TRY_CAST, so none raises an error that quotes a value; all of one table's preconditions are items
/// of one SELECT; a measurement reads into each precondition's rows and violating rows; and the pure cases of the Facsimile's question
/// catalog (tests/Cases/10-questions.md) for these kinds count, under their identifiers, the rows each kind
/// names and the value classes and collation classes the catalog names. Where v3 decided otherwise the case follows v3: decision 2.9
/// makes any type change Fits, so a widening is counted and finds nothing (QST-19, QST-21, QST-33); a lossy round trip that rounds or
/// substitutes characters is a printed line and no count (QST-22, QST-24, QST-25, QST-28, QST-30, QST-34); a digit string past int's
/// range reads not convertible, as the catalog's QST-23 note says; and a check that converts is counted through its TRY_ form wherever
/// SQL Server converts on every row (QST-106).
/// </summary>
public sealed class AggregateQueriesTests
{
    /// <summary>The environments' collation in these cases, as the catalog's bases name it (QST-35).</summary>
    internal static readonly Collation Database = Value(Collation.Of("SQL_Latin1_General_CP1_CI_AS"));

    /// <summary>The schema the catalog's cases start from (cases/README.md §2), as tests/Kernel.Tests/QuestionCases.cs writes it.</summary>
    private static readonly Model Pg = Model.Of(
            "Account.Id int NOT NULL", "Account.Name nvarchar(100) NOT NULL", "Account.Region nvarchar(50) NULL",
            "Category.Id int NOT NULL", "Category.Code nvarchar(30) NOT NULL",
            "Customer.Id int NOT NULL", "Customer.Name nvarchar(100) NOT NULL", "Customer.Email nvarchar(256) NULL",
            "Customer.ContactPhone nvarchar(40) NULL", "Customer.AccountId int NULL",
            "Order.Id int NOT NULL", "Order.CustomerId int NOT NULL", "Order.StatusId int NOT NULL", "Order.Total decimal(18,2) NOT NULL",
            "OrderLine.Id int NOT NULL", "OrderLine.OrderId int NOT NULL", "OrderLine.LineNumber int NOT NULL",
            "OrderLine.Amount decimal(18,2) NOT NULL",
            "Product.Id int NOT NULL", "Product.Code nvarchar(50) NOT NULL", "Product.Name nvarchar(200) NULL",
            "Status.Id int NOT NULL", "Status.Code nvarchar(20) NOT NULL")
        .PrimaryKey("PK_Account_Id", "Account", "Id").PrimaryKey("PK_Category_Id", "Category", "Id")
        .PrimaryKey("PK_Customer_Id", "Customer", "Id").PrimaryKey("PK_Order_Id", "Order", "Id")
        .PrimaryKey("PK_OrderLine_Id", "OrderLine", "Id").PrimaryKey("PK_Product_Id", "Product", "Id")
        .PrimaryKey("PK_Status_Id", "Status", "Id");

    private static readonly Model T = Pg.With("T.Id int NOT NULL", "T.Ref varchar(20) NULL", "T.Other int NULL");

    private static readonly Model Shipment = Pg.With("Shipment.Id int NOT NULL", "Shipment.OrderId int NULL", "Shipment.LineNumber int NULL")
        .Dropping("PK_OrderLine_Id").PrimaryKey("PK_OrderLine", "OrderLine", "OrderId", "LineNumber");

    private static readonly Rename ContactPhone = Value(Rename.Of(Model.ColumnKey("Customer.ContactPhone"), "MobileNumber"));

    /// <summary>The catalog's cases, each as the environment's model, the model the change brings, and the tables the plan's sites
    /// check, the models as tests/Kernel.Tests/QuestionCases.cs writes them.</summary>
    internal static readonly IReadOnlyDictionary<string, Case> Cases = new Dictionary<string, Case>(StringComparer.Ordinal)
    {
        ["QST-01"] = new(Pg, Pg, ["Customer"]),
        ["QST-02"] = new(Pg.With("Customer.Code nvarchar(20) NOT NULL", "Customer.Email nvarchar(100) NULL"),
            Pg.With("Customer.Code nvarchar(10) NOT NULL", "Customer.Email nvarchar(100) NOT NULL")
                .UniqueIndex("UX_Customer_Code", "Customer", "Code")
                .ForeignKey("FK_Order_Customer", "Order", ["CustomerId"], "Customer", ["Id"]),
            ["Customer"]),
        ["QST-08"] = new(Pg, Pg, ["Customer", "Product"]),
        ["QST-09"] = new(Pg, Pg, ["Customer", "Customer"]),
        ["QST-10"] = new(Pg, Pg.With("Customer.Segment nvarchar(20) NOT NULL"), ["Customer"]),
        ["QST-11"] = new(Pg, Pg, ["Order"]),
        ["QST-12"] = new(Pg.With("archive.OrderArchive.Id int NOT NULL"), Pg.With("archive.OrderArchive.Id int NOT NULL"),
            ["archive.OrderArchive"]),
        ["QST-16"] = new(Pg, Pg.With("Customer.Email nvarchar(256) NOT NULL"), ["Customer"]),
        ["QST-17"] = new(Pg, Pg.With("Product.Code nvarchar(10) NOT NULL"), ["Product"]),
        ["QST-18"] = new(Pg, Pg.With("Customer.Email nvarchar(100) NOT NULL"), ["Customer"]),
        ["QST-19"] = new(Pg, Pg.With("Customer.Email nvarchar(400) NULL")),
        ["QST-21"] = new(Pg, Pg.With("OrderLine.LineNumber bigint NOT NULL")),
        ["QST-22"] = new(Pg, Pg.With("Order.Total int NOT NULL"), ["Order"]),
        ["QST-23"] = new(Pg, Pg.With("Product.Code int NOT NULL"), ["Product"]),
        ["QST-24"] = new(T, T.With("T.Ref decimal(9,2) NULL"), ["T"]),
        ["QST-25"] = new(Pg.With("Customer.BirthText nvarchar(20) NULL"), Pg.With("Customer.BirthText date NULL"), ["Customer"]),
        ["QST-26"] = new(Pg.With("Account.ExternalKey nvarchar(36) NULL"), Pg.With("Account.ExternalKey uniqueidentifier NULL"),
            ["Account"]),
        ["QST-27"] = new(Pg, Pg.With("OrderLine.Amount decimal(9,2) NOT NULL"), ["OrderLine"]),
        ["QST-28"] = new(Pg.With("OrderLine.Rate decimal(18,4) NOT NULL"), Pg.With("OrderLine.Rate decimal(18,2) NOT NULL"), ["OrderLine"]),
        ["QST-29"] = new(Pg, Pg.With("OrderLine.LineNumber smallint NOT NULL"), ["OrderLine"]),
        ["QST-30"] = new(Pg, Pg.With("Account.Name varchar(100) NOT NULL"), ["Account"]),
        ["QST-31"] = new(Pg.With("Order.PlacedOn datetime2(7) NOT NULL"), Pg.With("Order.PlacedOn date NOT NULL"), ["Order"]),
        ["QST-32"] = new(Pg, Pg.With("OrderLine.LineNumber nvarchar(5) NOT NULL"), ["OrderLine"]),
        ["QST-33"] = new(
            Pg.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CS_AS", "T.Amt int NULL")
                .UniqueIndex("UX_T_Code", "T", "Code"),
            Pg.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CI_AS", "T.Amt int NULL", "T.Note nvarchar(20) NULL")
                .UniqueIndex("UX_T_Code", "T", "Code").Check("CK_T_Amt", "T", "[Amt]>(0)", ["Amt"])),
        ["QST-34"] = new(Pg.With("T.Id int NOT NULL", "T.Ref varchar(20) NULL Latin1_General_CS_AS"),
            Pg.With("T.Id int NOT NULL", "T.Ref varchar(20) NULL Cyrillic_General_CI_AS")),
        ["QST-35"] = new(Pg, Pg.UniqueIndex("UX_Product_Code", "Product", "Code")),
        ["QST-36"] = new(Pg, Pg.UniqueIndex("UX_Customer_Email", "Customer", "Email")),
        ["QST-37"] = new(Pg, Pg.FilteredIndex("UX_Customer_Email", "Customer", ["Email"], "[Email] IS NOT NULL", "Email")),
        ["QST-38"] = new(Pg.With("Account.Name nvarchar(100) NOT NULL Latin1_General_CI_AI"),
            Pg.With("Account.Name nvarchar(100) NOT NULL Latin1_General_CI_AI").UniqueIndex("UX_Account_Name", "Account", "Name")),
        ["QST-39"] = new(Pg.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CS_AS"),
            Pg.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CS_AS").UniqueIndex("UX_T_Code", "T", "Code")),
        ["QST-40"] = new(Pg, Pg.UniqueIndex("UX_OrderLine_Order_Line", "OrderLine", "OrderId", "LineNumber")),
        ["QST-41"] = new(Pg, Pg.UniqueConstraint("UQ_Product_Code", "Product", "Code")),
        ["QST-42"] = new(Pg.Dropping("PK_OrderLine_Id"),
            Pg.Dropping("PK_OrderLine_Id").PrimaryKey("PK_OrderLine", "OrderLine", "OrderId", "LineNumber")),
        ["QST-43"] = new(Pg,
            Pg.UniqueIndex("UX_Product_Code", "Product", "Code").Changing("UX_Product_Code", r => r with { Included = ["Name"] })),
        ["QST-46"] = new(Pg,
            Pg.With("Customer.ExternalRef nvarchar(40) NULL").UniqueIndex("UX_Customer_ExternalRef", "Customer", "ExternalRef")),
        ["QST-50"] = new(Pg, Pg.ForeignKey("FK_Order_Customer", "Order", ["CustomerId"], "Customer", ["Id"])),
        ["QST-51"] = new(Shipment,
            Shipment.ForeignKey("FK_Shipment_OrderLine", "Shipment", ["OrderId", "LineNumber"], "OrderLine", ["OrderId", "LineNumber"])),
        ["QST-52"] = new(Pg.ForeignKey("FK_Order_Status", "Order", ["StatusId"], "Status", ["Id"]),
            Pg.ForeignKey("FK_Order_Status", "Order", ["StatusId"], "Status", ["Id"], onDelete: "Cascade")),
        ["QST-53"] = new(
            Pg.With("T.Id int NOT NULL", "T.PId int NULL", "P.Id int NOT NULL").PrimaryKey("PK_P", "P", "Id")
                .ForeignKey("FK_T_P", "T", ["PId"], "P", ["Id"], disabled: true),
            Pg.With("T.Id int NOT NULL", "T.PId int NULL", "P.Id int NOT NULL").PrimaryKey("PK_P", "P", "Id")
                .ForeignKey("FK_T_P", "T", ["PId"], "P", ["Id"])),
        ["QST-54"] = new(Pg.With("ref.Status.Id int NOT NULL").PrimaryKey("PK_Status_Id", "ref.Status", "Id"),
            Pg.With("ref.Status.Id int NOT NULL").PrimaryKey("PK_Status_Id", "ref.Status", "Id")
                .ForeignKey("FK_Order_RefStatus", "Order", ["StatusId"], "ref.Status", ["Id"])),
        ["QST-60"] = new(Pg, Pg.Check("CK_Order_Total", "Order", "[Total]>(0)", ["Total"])),
        ["QST-61"] = new(Pg,
            Pg.Check("CK_Order_Total", "Order", "[Total]>(0)", ["Total"]).ForeignKey("FK_Order_Status", "Order", ["StatusId"], "Status", ["Id"])),
        ["QST-62"] = new(Pg,
            Pg.With("Product.Qty int NULL", "Product.Rank int NOT NULL").Default("DF_Product_Rank", "Product", "Rank", "((0))")
                .Check("CK_Product_Qty", "Product", "[Qty]>(0)", ["Qty"]).Check("CK_Product_Rank", "Product", "[Rank]>(0)", ["Rank"])),
        ["QST-73"] = new(Pg.With("Product.LegacyCode nvarchar(40) NOT NULL"), Pg, ["Product"]),
        ["QST-74"] = new(Pg, Pg.WithoutTable("OrderLine"), ["OrderLine"]),
        ["QST-75"] = new(Pg, Pg.WithoutColumn("Customer.ContactPhone").With("Customer.MobileNumber nvarchar(40) NULL"), ["Customer"]),
        ["QST-76"] = new(
            Pg.With("T.Id int NOT NULL", "T.Code nvarchar(50) NOT NULL Latin1_General_CI_AS", "T.Note nvarchar(20) NULL", "T.Amt int NULL")
                .UniqueIndex("UX_T_Code", "T", "Code").Check("CK_T_Amt", "T", "[Amt]>(0)", ["Amt"]),
            Pg.With("T.Id int NOT NULL", "T.Code nvarchar(40) NOT NULL", "T.Amt int NULL").UniqueIndex("UX_T_Code", "T", "Code"),
            ["T"], DatabaseCollation: Value(Collation.Of("Latin1_General_CS_AS"))),
        ["QST-100"] = new(Pg,
            Pg.With("Product.Sku nvarchar(20) NOT NULL").Default("DF_Product_Sku", "Product", "Sku", "(N'NONE')")
                .UniqueIndex("UX_Product_Sku", "Product", "Sku")),
        ["QST-101"] = new(Pg.With("Order.OrderTypeId int NULL"),
            Pg.With("Order.OrderTypeId int NULL", "OrderType.Id int NOT NULL", "OrderType.Code nvarchar(20) NOT NULL")
                .PrimaryKey("PK_OrderType_Id", "OrderType", "Id").ForeignKey("FK_Order_OrderType", "Order", ["OrderTypeId"], "OrderType", ["Id"])),
        ["QST-102"] = new(Pg, Pg.RenameColumn("Customer.ContactPhone", "MobileNumber").With("Customer.MobileNumber nvarchar(40) NOT NULL"),
            ["Customer"], [ContactPhone]),
        ["QST-103"] = new(Pg,
            Pg.MoveTable("Category", "archive").Check("CK_Category_Code", "archive.Category", "LEN([Code])>(0)", ["Code"]), [],
            [new(Model.TableKey("Category"), Model.TableKey("archive.Category")),
                new(Key("PrimaryKeyConstraint", "dbo", "PK_Category_Id"), Key("PrimaryKeyConstraint", "archive", "PK_Category_Id"))]),
        ["QST-106"] = new(T, T.Check("CK_T_RefNum", "T", "CONVERT([int],[Ref])>(0)", ["Ref"])),
    };

    /// <summary>
    /// QST-17 to QST-34: the value classes a retype counts, each its own item, and the lines its site prints for the losses no count shows:
    /// substituted characters into a character type whose code page can lack a character the value holds, and rounded values where the
    /// fractional digits the type holds fall. A number too long for a string (QST-32) is over length, and only a string's trailing spaces
    /// are over bytes.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("QST-02", "Customer.Code", "over-length over-bytes", "")]
    [InlineData("QST-17", "Product.Code", "over-length over-bytes", "")]
    [InlineData("QST-18", "Customer.Email", "over-length over-bytes", "")]
    [InlineData("QST-19", "Customer.Email", "over-length over-bytes", "")]
    [InlineData("QST-21", "OrderLine.LineNumber", "overflow", "")]
    [InlineData("QST-22", "Order.Total", "overflow", "fits.rounded-values")]
    [InlineData("QST-23", "Product.Code", "not-convertible empty-string", "")]
    [InlineData("QST-24", "T.Ref", "not-convertible empty-string overflow", "fits.rounded-values")]
    [InlineData("QST-25", "Customer.BirthText", "not-convertible empty-string", "fits.rounded-values")]
    [InlineData("QST-26", "Account.ExternalKey", "not-convertible empty-string", "")]
    [InlineData("QST-27", "OrderLine.Amount", "overflow", "")]
    [InlineData("QST-28", "OrderLine.Rate", "overflow", "fits.rounded-values")]
    [InlineData("QST-29", "OrderLine.LineNumber", "overflow", "")]
    [InlineData("QST-30", "Account.Name", "over-length over-bytes", "fits.substituted-characters")]
    [InlineData("QST-31", "Order.PlacedOn", "overflow lossy-round-trip", "fits.rounded-values")]
    [InlineData("QST-32", "OrderLine.LineNumber", "over-length", "")]
    [InlineData("QST-33", "T.Code", "over-length over-bytes", "")]
    [InlineData("QST-34", "T.Ref", "over-length over-bytes", "fits.substituted-characters")]
    [InlineData("QST-76", "T.Code", "over-length over-bytes", "")]
    public void A_pure_case_of_the_question_catalog_counts_each_value_class_of_its_retype_and_prints_its_lines(string id, string column,
        string classes, string lines)
    {
        var queries = Cases[id].Queries();
        var fits = Precondition(queries, p => p.Kind is PreconditionKind.Fits && p.Columns[0] == Model.ColumnKey(column).Name);

        Assert.Equal(classes, Words(Table(queries, fits).Classes(fits)));
        Assert.Equal(lines, string.Join(' ', queries.Notes.Where(n => n.Code.StartsWith("fits.", StringComparison.Ordinal))
            .Select(n => n.Code)));
        Assert.All(queries.Notes.Where(n => n.Code.StartsWith("fits.", StringComparison.Ordinal)),
            n => Assert.Equal(Model.ColumnKey(column).Path, n.Subject));
    }

    /// <summary>
    /// A retype into float or real prints that rounded values are not counted where the old type holds digits the new one does not keep,
    /// which ALTER COLUMN rounds without an error (measured on SQL Server 2022: 1234567.89 as decimal(18,2) into real is 1234567.875,
    /// 9007199254740993 as bigint into float 9007199254740992, 16777217 as int into real 16777216, 0.1 as float into real 0.100000001490116):
    /// real keeps about 7 significant digits and float about 15, each holding a decimal fraction as the nearest binary one. Real into float,
    /// int into float, smallint into real, and a decimal with no Scale and no more digits than the new type keeps print no line.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("decimal(18,2)", "real", "fits.rounded-values")]
    [InlineData("decimal(18,2)", "float", "fits.rounded-values")]
    [InlineData("decimal(5,2)", "float", "fits.rounded-values")]
    [InlineData("decimal(9,0)", "real", "fits.rounded-values")]
    [InlineData("decimal(7,0)", "real", "")]
    [InlineData("decimal(16,0)", "float", "fits.rounded-values")]
    [InlineData("decimal(15,0)", "float", "")]
    [InlineData("money", "float", "fits.rounded-values")]
    [InlineData("smallmoney", "real", "fits.rounded-values")]
    [InlineData("bigint", "float", "fits.rounded-values")]
    [InlineData("bigint", "real", "fits.rounded-values")]
    [InlineData("int", "real", "fits.rounded-values")]
    [InlineData("int", "float", "")]
    [InlineData("smallint", "real", "")]
    [InlineData("float", "real", "fits.rounded-values")]
    [InlineData("float", "float(24)", "fits.rounded-values")]
    [InlineData("real", "float", "")]
    [InlineData("varchar(20)", "float", "fits.rounded-values")]
    public void A_retype_into_float_or_real_prints_that_rounded_values_are_not_counted_where_the_old_type_holds_more_digits(string before,
        string after, string lines)
    {
        var queries = new Case(Model.Of("N.Id int NOT NULL", "N.v " + before + " NULL"), Model.Of("N.Id int NOT NULL", "N.v " + after + " NULL"))
            .Queries();

        Assert.Contains(Assert.Single(queries.Tables).Preconditions, p => p.Kind is PreconditionKind.Fits);
        Assert.Equal(lines, string.Join(' ', queries.Notes.Where(n => n.Code.StartsWith("fits.", StringComparison.Ordinal)).Select(n => n.Code)));
        Assert.All(queries.Notes.Where(n => n.Code == "fits.rounded-values"), n => Assert.Contains(
            "keeps about " + (after == "float" ? "15" : "7") + " significant digits", n.Message, StringComparison.Ordinal));
    }

    /// <summary>
    /// QST-33 and QST-35 to QST-46, and QST-100: the collation classes a key counts, under the collation the index is built with: exact and
    /// trailing space under every collation, case under a case-insensitive one, accent under an accent-insensitive one, and two NULLs where
    /// a key column allows NULL and the filter does not leave NULL out. A key over a column the change creates reads the value every
    /// existing row holds: two NULLs (QST-46) or one default (QST-100), so two rows are a duplicate.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("QST-02", "Customer", "exact case trailing-space")]
    [InlineData("QST-33", "T", "exact case trailing-space")]
    [InlineData("QST-35", "Product", "exact case trailing-space")]
    [InlineData("QST-36", "Customer", "exact case trailing-space nulls")]
    [InlineData("QST-37", "Customer", "exact case trailing-space")]
    [InlineData("QST-38", "Account", "exact case accent trailing-space")]
    [InlineData("QST-39", "T", "exact trailing-space")]
    [InlineData("QST-40", "OrderLine", "exact")]
    [InlineData("QST-41", "Product", "exact case trailing-space")]
    [InlineData("QST-42", "OrderLine", "exact")]
    [InlineData("QST-43", "Product", "exact case trailing-space")]
    [InlineData("QST-46", "Customer", "nulls")]
    [InlineData("QST-76", "T", "exact trailing-space")]
    [InlineData("QST-100", "Product", "exact")]
    public void A_pure_case_of_the_question_catalog_counts_each_collation_class_of_its_key(string id, string table, string classes)
    {
        var queries = Cases[id].Queries();
        var unique = Precondition(queries, p => p.Kind is PreconditionKind.Unique && p.Table == Model.TableName(table)
            && (id != "QST-33" || p.Columns.Count == 1));

        Assert.Equal(classes, Words(Table(queries, unique).Classes(unique)));
    }

    /// <summary>
    /// QST-46 and QST-100 read from the table's rows alone: every existing row holds the created column's value, NULL or the default, so
    /// the table's rows are one group, a duplicate from two rows on; and the query names no column the environment lacks.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("QST-46", "ExternalRef", 3, 3, Class.Nulls)]
    [InlineData("QST-46", "ExternalRef", 1, 0, Class.Nulls)]
    [InlineData("QST-100", "Sku", 2, 2, Class.Exact)]
    [InlineData("QST-100", "Sku", 0, 0, Class.Exact)]
    public void A_key_over_a_column_the_change_creates_is_a_duplicate_once_the_table_holds_two_rows(string id, string column, long rows,
        long violating, Class @class)
    {
        var queries = Cases[id].Queries();
        var unique = Precondition(queries, p => p.Kind is PreconditionKind.Unique && p.Columns[0].Base == column);
        var table = Table(queries, unique);

        var counts = AggregateQueries.Read(table, Answered(table, item => item.Precondition is null ? rows : 0));

        Assert.DoesNotContain("[" + column + "]", table.Query.Statement, StringComparison.Ordinal);
        Assert.Equal(new PreconditionCount(unique, rows, violating), counts.Counted.Single(c => c.Precondition == unique));
        Assert.Equal(new[] { new AggregateQueries.ClassCount(unique, @class, violating) }, counts.Classes.Where(c => c.Precondition == unique));
    }

    /// <summary>
    /// A key over a column the change converts so that other values are equal reads not predictable read-only, a unique index, the
    /// primary key it references and the foreign key alike, since the allowlist admits no GROUP BY of a converted value: a Unicode string
    /// into a code page that is no UTF-8 one, which writes a character the code page lacks as '?' or a letter near it; a code page's string
    /// into a Unicode one under a SQL collation, or a collation dbchange does not know to share its code page; float into real; and
    /// datetimeoffset into datetime2, which drops the offset. A conversion that keeps which values are equal is counted: into a UTF-8
    /// collation, into Unicode under a Windows collation of the same code page, from one code page to another, which the grouping's
    /// COLLATE converts as ALTER COLUMN does, real into float, and datetime2 into datetimeoffset. AggregateQueriesCountTests measures each
    /// reason on planted rows.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("nvarchar(10) NULL Latin1_General_CI_AS", "varchar(10) NULL Latin1_General_CI_AS", "as '?' or a letter near it")]
    [InlineData("nchar(10) NULL Latin1_General_CI_AS", "char(10) NULL Latin1_General_CI_AS", "as '?' or a letter near it")]
    [InlineData("nvarchar(10) NULL Latin1_General_CI_AS", "varchar(10) NULL Latin1_General_100_CI_AS_SC_UTF8", "")]
    [InlineData("varchar(10) NULL SQL_Latin1_General_CP1_CI_AS", "nvarchar(10) NULL SQL_Latin1_General_CP1_CI_AS", "the SQL collation")]
    [InlineData("varchar(10) NULL Latin1_General_CI_AS", "nvarchar(10) NULL Cyrillic_General_CI_AS", "not known to share")]
    [InlineData("varchar(10) NULL SQL_Latin1_General_CP1_CI_AS", "nvarchar(10) NULL Latin1_General_CS_AS", "")]
    [InlineData("varchar(10) NULL Latin1_General_CI_AS", "varchar(10) NULL Cyrillic_General_CI_AS", "")]
    [InlineData("float NULL", "real NULL", "to one real")]
    [InlineData("float NULL", "float(24) NULL", "to one real")]
    [InlineData("real NULL", "float NULL", "")]
    [InlineData("datetimeoffset(7) NULL", "datetime2(7) NULL", "offset")]
    [InlineData("datetime2(7) NULL", "datetimeoffset(7) NULL", "")]
    public void A_key_over_a_column_the_change_converts_so_that_other_values_are_equal_reads_not_predictable_read_only(string before,
        string after, string why)
    {
        var environment = Model.Of("X.Id int NOT NULL", "X.c " + before, "P.k " + before).PrimaryKey("PK_P", "P", "k");
        var head = Model.Of("X.Id int NOT NULL", "X.c " + after, "P.k " + after).PrimaryKey("PK_P", "P", "k")
            .UniqueIndex("UX_X_c", "X", "c").ForeignKey("FK_X_P", "X", ["c"], "P", ["k"]);

        var queries = new Case(environment, head).Queries();

        var keys = queries.NotPredictable.Where(u => u.Precondition.Kind is PreconditionKind.Unique or PreconditionKind.ForeignKey).ToList();
        var counted = queries.Tables.SelectMany(t => t.Preconditions)
            .Where(p => p.Kind is PreconditionKind.Unique or PreconditionKind.ForeignKey).ToList();
        Assert.Equal(why.Length == 0 ? (0, 3) : (3, 0), (keys.Count, counted.Count));
        Assert.All(keys, u => Assert.Contains("before the key is validated, which ", u.Why, StringComparison.Ordinal));
        Assert.All(keys, u => Assert.Contains(why, u.Why, StringComparison.Ordinal));
    }

    /// <summary>
    /// A retype from a binary type into a shorter one counts, as over length, the values longer than the new type: where none is, no row
    /// is violating; where any is, the precondition reads not predictable read-only once counted, since ALTER COLUMN drops the bytes past
    /// the new length where they are all 0x00 and refuses any other value (Msg 2628), which no query the allowlist admits tells apart.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("varbinary(10)", "varbinary(2)")]
    [InlineData("binary(8)", "binary(4)")]
    [InlineData("varbinary(10)", "varchar(1)")]
    [InlineData("varbinary(max)", "nvarchar(1)")]
    [InlineData("image", "varbinary(2)")]
    public void A_retype_from_a_binary_type_reads_not_predictable_read_only_once_a_value_longer_than_the_new_type_is_counted(
        string before, string after)
    {
        var table = Assert.Single(new Case(Model.Of("B.Id int NOT NULL", "B.v " + before + " NULL"),
            Model.Of("B.Id int NOT NULL", "B.v " + after + " NULL")).Queries().Tables);
        var fits = table.Preconditions.Single(p => p.Kind is PreconditionKind.Fits);

        var clean = AggregateQueries.Read(table, Answered(table, item => item.Precondition is null ? 5 : 0));
        var longer = AggregateQueries.Read(table, Answered(table, item => item.Precondition is null ? 5 : 2));

        Assert.Equal([Class.OverLength], table.Classes(fits));
        Assert.Equal(new PreconditionCount(fits, 5, 0), Assert.Single(clean.Counted));
        Assert.Equal(new AggregateQueries.ClassCount(fits, Class.OverLength, 0), Assert.Single(clean.Classes));
        Assert.Empty(clean.Unanswered);
        Assert.Empty(longer.Counted);
        Assert.Empty(longer.Classes);
        var unanswered = Assert.Single(longer.Unanswered);
        Assert.Equal(fits, unanswered.Precondition);
        Assert.StartsWith("not predictable read-only: 2 rows hold a value longer than " + after + " takes, ", unanswered.Why,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The catalog's pure cases for TableEmpty, NotNull, ForeignKey and Check, each counted on the table the environment holds: a site's
    /// table by its rows, under its own schema and its bracketed name, once however many sites check it (QST-01, QST-08 to QST-12, QST-73
    /// to QST-75); NotNull by the rows holding NULL (QST-16); a foreign key by the child rows whose key columns are all non-null and that
    /// match no row of the parent's keys, so a NULL in any key column passes (QST-50 to QST-54); and a check by NOT of its expression, so
    /// false counts and unknown does not, a conversion counted through its TRY_ form with the rows on which it fails (QST-60, QST-106).
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("QST-01", "[dbo].[Customer]", "TableEmpty", "SELECT COUNT_BIG(*) FROM [dbo].[Customer] AS [t]")]
    [InlineData("QST-08", "[dbo].[Product]", "TableEmpty", "SELECT COUNT_BIG(*) FROM [dbo].[Product] AS [t]")]
    [InlineData("QST-09", "[dbo].[Customer]", "TableEmpty", "SELECT COUNT_BIG(*) FROM [dbo].[Customer] AS [t]")]
    [InlineData("QST-10", "[dbo].[Customer]", "TableEmpty", "SELECT COUNT_BIG(*) FROM [dbo].[Customer] AS [t]")]
    [InlineData("QST-11", "[dbo].[Order]", "TableEmpty", "SELECT COUNT_BIG(*) FROM [dbo].[Order] AS [t]")]
    [InlineData("QST-12", "[archive].[OrderArchive]", "TableEmpty", "SELECT COUNT_BIG(*) FROM [archive].[OrderArchive] AS [t]")]
    [InlineData("QST-16", "[dbo].[Customer]", "NotNull", "SUM(CASE WHEN [t].[Email] IS NULL THEN 1 ELSE 0 END)")]
    [InlineData("QST-50", "[dbo].[Order]", "ForeignKey", "SUM(CASE WHEN [t].[CustomerId] IS NOT NULL AND [p1].[k1] IS NULL THEN 1 ELSE 0 END)"
        + " FROM [dbo].[Order] AS [t] LEFT OUTER JOIN (SELECT [Id] AS [k1] FROM [dbo].[Customer] GROUP BY [Id]) AS [p1]"
        + " ON [p1].[k1] = [t].[CustomerId]")]
    [InlineData("QST-51", "[dbo].[Shipment]", "ForeignKey",
        "WHEN [t].[LineNumber] IS NOT NULL AND [t].[OrderId] IS NOT NULL AND [p1].[k1] IS NULL THEN 1"
        + " ELSE 0 END) FROM [dbo].[Shipment] AS [t] LEFT OUTER JOIN (SELECT [LineNumber] AS [k1], [OrderId] AS [k2]"
        + " FROM [dbo].[OrderLine] GROUP BY [LineNumber], [OrderId]) AS [p1] ON [p1].[k1] = [t].[LineNumber] AND [p1].[k2] = [t].[OrderId]")]
    [InlineData("QST-52", "[dbo].[Order]", "ForeignKey",
        "(SELECT [Id] AS [k1] FROM [dbo].[Status] GROUP BY [Id]) AS [p1] ON [p1].[k1] = [t].[StatusId]")]
    [InlineData("QST-53", "[dbo].[T]", "ForeignKey", "(SELECT [Id] AS [k1] FROM [dbo].[P] GROUP BY [Id]) AS [p1] ON [p1].[k1] = [t].[PId]")]
    [InlineData("QST-54", "[dbo].[Order]", "ForeignKey",
        "(SELECT [Id] AS [k1] FROM [ref].[Status] GROUP BY [Id]) AS [p1] ON [p1].[k1] = [t].[StatusId]")]
    [InlineData("QST-60", "[dbo].[Order]", "Check", "SUM(CASE WHEN NOT ([t].[Total] > (0)) THEN 1 ELSE 0 END)")]
    [InlineData("QST-73", "[dbo].[Product]", "TableEmpty", "SELECT COUNT_BIG(*) FROM [dbo].[Product] AS [t]")]
    [InlineData("QST-74", "[dbo].[OrderLine]", "TableEmpty", "SELECT COUNT_BIG(*) FROM [dbo].[OrderLine] AS [t]")]
    [InlineData("QST-75", "[dbo].[Customer]", "TableEmpty", "SELECT COUNT_BIG(*) FROM [dbo].[Customer] AS [t]")]
    [InlineData("QST-106", "[dbo].[T]", "Check",
        "SUM(CASE WHEN NOT (TRY_CONVERT (INT, [t].[Ref]) > (0)) OR ([t].[Ref] IS NOT NULL AND TRY_CONVERT (INT, [t].[Ref]) IS NULL)")]
    public void A_pure_case_of_the_question_catalog_counts_the_rows_its_kind_names_on_the_table_the_environment_holds(string id,
        string table, string kind, string rule)
    {
        var queries = Cases[id].Queries();
        var query = Table(queries, table);

        Assert.Empty(queries.NotPredictable);
        Assert.Contains(query.Preconditions, p => p.ToString().StartsWith(kind + " on ", StringComparison.Ordinal));
        Assert.Contains(rule, Regex.Replace(query.Query.Statement, @"\s+", " "), StringComparison.Ordinal);
    }

    /// <summary>
    /// QST-62 and QST-101: a check over a column the change creates reads, where the column would stand, the value every existing row
    /// holds, and a foreign key to a parent the change creates counts the child rows whose key columns are all non-null, with no join to
    /// a table the environment lacks. QST-102 and QST-103: a column the change renames, and a table it moves, are counted under the names
    /// the environment holds.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("QST-62", "[Product]", "[Qty] [Rank]")]
    [InlineData("QST-101", "[OrderTypeId] IS NOT NULL", "[OrderType]")]
    [InlineData("QST-102", "[ContactPhone]", "[MobileNumber]")]
    [InlineData("QST-103", "[dbo].[Category]", "[archive]")]
    public void A_pure_case_of_the_coordinate_rules_is_counted_under_the_names_the_environment_holds(string id, string named, string absent)
    {
        var queries = Cases[id].Queries();

        Assert.Empty(queries.NotPredictable);
        var statements = string.Join('\n', queries.Tables.Select(t => t.Query.Statement));
        Assert.Contains(named, statements, StringComparison.Ordinal);
        Assert.All(absent.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            name => Assert.DoesNotContain(name, statements, StringComparison.Ordinal));
    }

    /// <summary>
    /// QST-106 and its variants: a check that converts is counted through the TRY_ form of each CONVERT or CAST, and of each conversion of
    /// the varchar column Ref that SQL Server makes with no CONVERT written where Ref meets a number, with the rows on which the conversion
    /// fails, wherever SQL Server converts on every row; under OR, CASE or COALESCE SQL Server may skip a conversion, PARSE has no TRY_ form
    /// the allowlist admits, SQL Server converts Ref to two types where BETWEEN's bounds differ in type, NULLIF would answer the converted
    /// value, ABS converts a string to float, and the allowlist refuses two columns set against each other, so those read not predictable
    /// read-only, with the reason. So do a string set against hierarchyid (Hid) by a comparison, COALESCE or ISNULL, which SQL Server
    /// parses with no CONVERT written and whose failure, Msg 6522, quotes the string; a LIKE that reads its escape character from a column
    /// (Esc), which raises Msg 506 quoting a value of more than one character; a COLLATE whose name Collation.Of refuses; and ISNULL's
    /// second argument converted to the type of its first, whose error quotes the row's value (Msg 220 for an int into tinyint), or which
    /// the query cannot convert where the first is a column the change creates (New, NewCode), read as NULL. A string into a string type,
    /// which never fails, and a literal, which reads no row (Tiny is tinyint, Flag bit), are counted where the first is the column as held.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("CONVERT([int],[Ref])>(0)", "")]
    [InlineData("CAST([Ref] AS [int])>(0) AND [Other]>(1)", "")]
    [InlineData("ISNULL(CONVERT([int],[Ref]),(1))>(0)", "")]
    [InlineData("NOT (CONVERT([int],[Ref])<=(0) OR [Other] IS NULL)", "")]
    [InlineData("[Ref]>(0)", "")]
    [InlineData("[Ref] BETWEEN (1) AND (9)", "")]
    [InlineData("[Ref] NOT IN ((1),(2))", "")]
    [InlineData("([Ref]+(1))>(0)", "")]
    [InlineData("UPPER([Ref])>(0)", "")]
    [InlineData("COALESCE([Ref],(0))>(0)", "")]
    [InlineData("ISNULL([Ref],'0')>(0)", "")]
    [InlineData("ISNULL([Tiny],(0))>(0)", "")]
    [InlineData("ISNULL([Flag],(0))=(1)", "")]
    [InlineData("ISNULL([Tiny],'0')>(0)", "")]
    [InlineData("ISNULL([New],(0))>(0)", "ISNULL's second argument")]
    [InlineData("ISNULL(CAST(NULL AS [tinyint]),[Other])>(0)", "ISNULL's second argument")]
    [InlineData("ISNULL([New],[Other])>(0)", "ISNULL's second argument")]
    [InlineData("ISNULL([NewCode],[Ref])<>'x'", "ISNULL's second argument")]
    [InlineData("CONVERT([int],[Ref])>(0) OR [Other] IS NULL", "may skip a conversion")]
    [InlineData("NOT (CONVERT([int],[Ref])<=(0) AND [Other] IS NOT NULL)", "may skip a conversion")]
    [InlineData("COALESCE([Other],CONVERT([int],[Ref]))>(0)", "may skip a conversion")]
    [InlineData("[Ref]>(0) OR [Other] IS NULL", "may skip a conversion")]
    [InlineData("ISNULL([Other],[Ref])>(0)", "may skip a conversion")]
    [InlineData("[Ref] BETWEEN (1) AND (2.5)", "more than one type")]
    [InlineData("NULLIF([Ref],(0)) IS NULL", "NULLIF")]
    [InlineData("ABS([Ref])>(0)", "ABS")]
    [InlineData("PARSE([Ref] AS int)>(0)", "PARSE")]
    [InlineData("[Other]>[Id]", "the allowlist refuses")]
    [InlineData("[Ref]=[Other]", "the allowlist refuses")]
    [InlineData("[Ref]=[Hid]", "parses the string")]
    [InlineData("[Hid]<>'/1/'", "parses the string")]
    [InlineData("COALESCE([Hid],[Ref]) IS NOT NULL", "parses the string")]
    [InlineData("ISNULL([Hid],[Ref]) IS NOT NULL", "parses the string")]
    [InlineData("'abc' LIKE 'a%' ESCAPE [Esc]", "escape character")]
    [InlineData("[Ref] COLLATE Latin1_General_Bogus='x'", "the collation 'Latin1_General_Bogus', which is no SQL Server collation name")]
    public void A_check_that_converts_is_counted_through_its_TRY_form_or_reads_not_predictable_read_only(string expression, string why)
    {
        var model = T.With("T.Hid hierarchyid NULL", "T.Esc varchar(10) NULL", "T.Tiny tinyint NULL", "T.Flag bit NULL");
        var reads = new[] { "Ref", "Other", "Id", "Hid", "Esc", "New", "NewCode", "Tiny", "Flag" }
            .Where(c => expression.Contains("[" + c + "]", StringComparison.Ordinal)).ToArray();
        var head = model.With("T.New tinyint NULL", "T.NewCode varchar(1) NULL");
        var queries = new Case(model, head.Check("CK_T_Probe", "T", expression, reads)).Queries();

        if (why.Length == 0)
        {
            Assert.Empty(queries.NotPredictable);
            Assert.Contains(queries.Tables.SelectMany(t => t.Preconditions), p => p.Kind is PreconditionKind.Check);
            if (expression.Contains("[Ref]", StringComparison.Ordinal))
            {
                Assert.Contains("TRY_", Assert.Single(queries.Tables).Query.Statement, StringComparison.Ordinal);
            }
        }
        else
        {
            var unanswered = Assert.Single(queries.NotPredictable);
            Assert.IsType<PreconditionKind.Check>(unanswered.Precondition.Kind);
            Assert.StartsWith("not predictable read-only: ", unanswered.Why, StringComparison.Ordinal);
            Assert.Contains(why, unanswered.Why, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The conversions SQL Server makes with no CONVERT written, each read as TRY_CONVERT to the type SQL Server 2022 plans
    /// CONVERT_IMPLICIT to (Types.cs): the varchar column Ref to int beside 0, to numeric(2,1) beside 1.5 and to numeric(10,0) beside
    /// 3000000000; and a string literal to the type of the column it meets, a tinyint's own, decimal(5,2)'s own, and time(3) and
    /// datetime2(3) at scale 7. Each conversion's failures are counted beside the false rows.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("[Ref]>(0)", "NOT (TRY_CONVERT (INT, [t].[Ref]) > (0)) OR ([t].[Ref] IS NOT NULL AND TRY_CONVERT (INT, [t].[Ref]) IS NULL)")]
    [InlineData("[Ref]>(1.5)", "TRY_CONVERT (NUMERIC (2, 1), [t].[Ref]) > (1.5)")]
    [InlineData("[Ref]<(3000000000)", "TRY_CONVERT (NUMERIC (10, 0), [t].[Ref]) < (3000000000)")]
    [InlineData("[Small]>'5'", "[t].[Small] > TRY_CONVERT (TINYINT, '5')")]
    [InlineData("[Amount]>='1.5'", "[t].[Amount] >= TRY_CONVERT (DECIMAL (5, 2), '1.5')")]
    [InlineData("[Starts]<'10:00'", "[t].[Starts] < TRY_CONVERT (TIME (7), '10:00')")]
    [InlineData("[Placed]>'20200101'", "[t].[Placed] > TRY_CONVERT (DATETIME2 (7), '20200101')")]
    public void A_string_SQL_Server_converts_with_no_CONVERT_written_is_read_as_TRY_CONVERT_to_the_type_it_converts_to(
        string expression, string read)
    {
        var typed = T.With("T.Small tinyint NULL", "T.Amount decimal(5,2) NULL", "T.Starts time(3) NULL", "T.Placed datetime2(3) NULL");
        var reads = new[] { "Ref", "Small", "Amount", "Starts", "Placed" }
            .Where(c => expression.Contains("[" + c + "]", StringComparison.Ordinal)).ToArray();

        var queries = new Case(typed, typed.Check("CK_T_Probe", "T", expression, reads)).Queries();

        Assert.Empty(queries.NotPredictable);
        Assert.Contains(read, Regex.Replace(Assert.Single(queries.Tables).Query.Statement, @"\s+", " "), StringComparison.Ordinal);
    }

    /// <summary>
    /// A COLLATE the check writes stands on what the query writes in its place, so the check compares under the collation it names: on
    /// the column, on the TRY_ form of a CONVERT or a CAST, and on a literal. A column the change converts from a code page's string into
    /// a Unicode one takes its new collation after the conversion, since ALTER COLUMN converts it from the code page it is held in
    /// (measured on SQL Server 2022: é in a Latin1_General_CI_AS varchar stays é in a Cyrillic_General_CI_AS nvarchar, and a COLLATE
    /// before the conversion would write it as e).
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("[Ref] COLLATE Latin1_General_CS_AS='ABC'", "NOT ([t].[Ref] COLLATE Latin1_General_CS_AS = 'ABC')")]
    [InlineData("CONVERT([varchar](20),[Ref]) COLLATE Latin1_General_CS_AS='ABC'",
        "NOT (TRY_CONVERT (VARCHAR (20), [t].[Ref]) COLLATE Latin1_General_CS_AS = 'ABC')")]
    [InlineData("CAST([Ref] AS [varchar](20)) COLLATE Latin1_General_CS_AS='ABC'",
        "NOT (TRY_CAST ([t].[Ref] AS VARCHAR (20)) COLLATE Latin1_General_CS_AS = 'ABC')")]
    [InlineData("[Ref]='ABC' COLLATE Latin1_General_CS_AS", "NOT ([t].[Ref] = 'ABC' COLLATE Latin1_General_CS_AS)")]
    [InlineData("[Wide]<>N'x'", "NOT (TRY_CONVERT (NVARCHAR (10), [t].[Wide]) COLLATE Cyrillic_General_CI_AS <> N'x')")]
    public void A_COLLATE_the_check_writes_stands_on_what_the_query_writes_in_place_of_its_column_or_its_conversion(string expression,
        string read)
    {
        var (environment, head) = (T.With("T.Wide varchar(10) NULL Latin1_General_CI_AS"), T.With("T.Wide nvarchar(10) NULL Cyrillic_General_CI_AS"));
        var reads = new[] { "Ref", "Wide" }.Where(c => expression.Contains("[" + c + "]", StringComparison.Ordinal)).ToArray();

        var queries = new Case(environment, head.Check("CK_T_Probe", "T", expression, reads)).Queries();

        Assert.Empty(queries.NotPredictable);
        Assert.Contains(read, Regex.Replace(Assert.Single(queries.Tables).Query.Statement, @"\s+", " "), StringComparison.Ordinal);
    }

    /// <summary>
    /// A collation is written into a query as its name stands, unquoted, so one Collation.Of refuses is written by no query: model text
    /// that names, for the column a Fits retype, a key and a check read, a collation closing the query and starting a DELETE leaves every
    /// precondition over the column not predictable read-only, naming the collation, whether the change's model or the environment's
    /// carries it, and no query holds the DELETE.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "P2")]
    [InlineData(true)]
    [InlineData(false)]
    public void A_collation_that_is_no_SQL_Server_collation_name_is_written_into_no_query_and_its_preconditions_read_not_predictable_read_only(bool inHead)
    {
        const string Planted = "Latin1_General_CS_AS)) > (10) THEN 1 ELSE 0 END) FROM [dbo].[X] AS [t]; DELETE FROM [dbo].[X]; --";
        Model Collated(Model model) => model with { Columns = [.. model.Columns.Select(c => c.Table == "X" && c.Name == "c"
            ? c with { Type = c.Type with { Collation = Planted } } : c)] };
        var environment = Pg.With("X.Id int NOT NULL", "X.c varchar(20) NULL Latin1_General_CI_AS");
        var head = Pg.With("X.Id int NOT NULL", "X.c varchar(10) NULL Latin1_General_CI_AS").UniqueIndex("UX_X_c", "X", "c")
            .Check("CK_X_c", "X", "[c]<>'x'", ["c"]);

        var queries = (inHead ? new Case(environment, Collated(head)) : new Case(Collated(environment), head)).Queries();

        Assert.DoesNotContain(queries.Tables, t => t.Alone.Prepend(t).Any(q => q.Query.Statement.Contains("DELETE", StringComparison.Ordinal)));
        Assert.DoesNotContain(queries.Tables.SelectMany(t => t.Preconditions), p => p.Columns.Any(c => c.Base == "c"));
        Assert.Equal(["Check", "Fits", "Unique"], queries.NotPredictable.Select(u => u.Precondition.ToString().Split(' ')[0]).Order());
        Assert.All(queries.NotPredictable, u => Assert.Contains("the collation '" + Planted + "', which is no SQL Server collation name",
            u.Why, StringComparison.Ordinal));
    }

    /// <summary>
    /// The safety the work package states word for word (§18, a value escaping in an error): for every pair of the types below a Fits
    /// precondition can join, each with a key, a foreign key and each check below over the column, every query the builders make, a
    /// table's and each of its preconditions' alone, is one the allowlist admits when read again as text, and none calls CONVERT, CAST or
    /// PARSE, only their TRY_ forms; every precondition is counted by a query or read not predictable read-only; and no check is counted
    /// that SQL Server fails on with an error quoting a row's value: the varchar column s set against hierarchyid or xml, which SQL Server
    /// parses s into with no CONVERT written (Msg 6522 quotes it), read as LIKE's escape character (Msg 506 quotes it), or the int column d
    /// as ISNULL's second argument after a tinyint, which SQL Server converts it to (Msg 220 quotes it). Every case is built, 32 types by 32
    /// by 10 checks.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "P2")]
    public void Every_query_the_builders_make_passes_the_allowlist_and_converts_only_through_a_TRY_form()
    {
        string[] types = ["nvarchar(50)", "nvarchar(10)", "nvarchar(max)", "varchar(20)", "varchar(20) Latin1_General_CS_AS", "char(10)",
            "nchar(10)", "text", "int", "bigint", "smallint", "tinyint", "bit", "decimal(18,2)", "decimal(9,4)", "money", "float", "real", "date",
            "time(3)", "datetime", "smalldatetime", "datetime2(7)", "datetime2(0)", "datetimeoffset(7)", "uniqueidentifier", "varbinary(16)",
            "binary(8)", "varbinary(max)", "xml", "sql_variant", "hierarchyid"];
        const string Parsed = "[c]=[s]";
        const string Escaped = "'abc' LIKE 'a%' ESCAPE [s]";
        const string Narrowed = "ISNULL(CAST(NULL AS [tinyint]),[d])>(0)";
        string[] checks = ["[c] IS NOT NULL", "CONVERT([int],[c])>(0)", "CAST([c] AS [date])<>'20000101' OR [d]>(0)", "PARSE([c] AS int)>(0)",
            "LEN([c])>(2)", "[c]>[d]", "[c]>(0)", Parsed, Escaped, Narrowed];
        var forbidden = new Regex(@"(?<![A-Za-z_])(CONVERT|CAST|PARSE)\s*\(", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // A collation is written after the nullability, as Model reads a column.
        static string Nullable(string type) => type.Split(' ') is [var name, var collation] ? name + " NULL " + collation : type + " NULL";
        string? Unsafe(string before, string after, string check)
        {
            var environment = Pg.With("X.Id int NOT NULL", "X.c " + Nullable(before), "X.d int NULL", "X.s varchar(20) NULL");
            var head = Pg.With("X.Id int NOT NULL", "X.c " + Nullable(after), "X.d int NULL", "X.s varchar(20) NULL")
                .PrimaryKey("PK_X_Id", "X", "Id").UniqueIndex("UX_X_c", "X", "c").ForeignKey("FK_X_Product", "X", ["d"], "Product", ["Id"])
                .Check("CK_X_c", "X", check, ["c", "d", "s"]);
            var drawn = new Case(environment, head, ["X"]);
            var counted = drawn.Counted;
            if (AggregateQueries.Of(counted, head.Elements, environment.Elements, Database, SqlServer.Session.Assumed)
                .Failed(out var queries, out var error))
            {
                return "the builders failed: " + error.Message;
            }

            var statements = queries.Tables.SelectMany(t => t.Alone.Prepend(t)).Select(t => t.Query.Statement).ToList();
            var quoting = check is Escaped or Narrowed || (check == Parsed && after is "hierarchyid" or "xml");
            return statements.FirstOrDefault(s => !AggregateQuery(s)) is { } refused ? "refused when read again: " + refused
                : statements.FirstOrDefault(forbidden.IsMatch) is { } converting ? "converts: " + converting
                : !queries.Tables.SelectMany(t => t.Preconditions).Concat(queries.NotPredictable.Select(u => u.Precondition)).Order()
                    .SequenceEqual(counted.Select(c => c.Precondition).Order()) ? "a precondition is neither counted nor unanswered"
                : quoting && queries.Tables.SelectMany(t => t.Preconditions).Any(p => p.Kind is PreconditionKind.Check)
                    ? "a check SQL Server fails on with an error quoting a row's value is counted"
                : null;
        }

        var cases = (from before in types from after in types from check in checks select (before, after, check)).ToList();
        var failures = cases.AsParallel().Select(c => (c, Why: Unsafe(c.before, c.after, c.check))).Where(f => f.Why is not null)
            .Select(f => f.c.before + " → " + f.c.after + ", " + f.c.check + ": " + f.Why).Order(StringComparer.Ordinal).ToList();

        Assert.True(failures.Count == 0, failures.Count + " of " + cases.Count + " cases:\n" + string.Join('\n', failures.Take(5)));
    }

    /// <summary>
    /// All of one table's preconditions, of every kind, are items of one SELECT (decision 2.9), so each table is read by one statement:
    /// QST-02's change reads Customer once for its site, NotNull, Fits and Unique, and Order once for its foreign key; QST-61 reads Order
    /// once for a check and a foreign key; two sites on two tables read each once (QST-08), and two on one table read it once (QST-09).
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("QST-02", "[dbo].[Customer]:TableEmpty NotNull Fits Unique;[dbo].[Order]:ForeignKey")]
    [InlineData("QST-08", "[dbo].[Customer]:TableEmpty;[dbo].[Product]:TableEmpty")]
    [InlineData("QST-09", "[dbo].[Customer]:TableEmpty")]
    [InlineData("QST-18", "[dbo].[Customer]:TableEmpty NotNull Fits")]
    [InlineData("QST-33", "[dbo].[T]:Fits Unique Check")]
    [InlineData("QST-61", "[dbo].[Order]:ForeignKey Check")]
    [InlineData("QST-76", "[dbo].[T]:TableEmpty Fits Unique")]
    public void All_of_one_table_s_preconditions_are_items_of_one_SELECT(string id, string tables)
    {
        var queries = Cases[id].Queries();

        Assert.Equal(tables, string.Join(';', queries.Tables.Select(t => t.Table + ":"
            + string.Join(' ', t.Preconditions.Select(p => p.ToString().Split(' ')[0])))));
        Assert.All(queries.Tables, t => Assert.All(t.Preconditions.Where(p => p.Kind is not PreconditionKind.TableEmpty),
            p => Assert.Contains(t.Items, i => i.Precondition == p)));
    }

    /// <summary>
    /// Every query a builder makes runs under the session given, SET LANGUAGE and SET DATEFORMAT sent as parameters: us_english and mdy,
    /// the Octopus login's defaults assumed until spike S7 reports them, whose note says they are not verified against the Octopus step.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Every_query_runs_under_the_session_given_and_the_assumed_one_is_said_to_be_unverified()
    {
        var dmy = new SqlServer.Session("British", SqlServer.DateFormat.Dmy);

        var (assumed, british) = (Cases["QST-25"].Queries(), Cases["QST-25"].Queries(dmy));

        Assert.Equal(("us_english", SqlServer.DateFormat.Mdy), (SqlServer.Session.Assumed.Language, SqlServer.Session.Assumed.DateFormat));
        Assert.All(assumed.Tables, t => Assert.Equal(SqlServer.Session.Assumed, t.Query.Session));
        Assert.All(british.Tables, t => Assert.Equal(dmy, t.Query.Session));
        var note = Assert.Single(assumed.Notes, n => n.Code == "session.unverified");
        Assert.Equal(Severity.Note, note.Severity);
        Assert.Contains("SET LANGUAGE us_english and SET DATEFORMAT mdy", note.Message, StringComparison.Ordinal);
        Assert.Contains("not verified against the Octopus step", note.Message, StringComparison.Ordinal);
        Assert.Contains("SET DATEFORMAT dmy", Assert.Single(british.Notes, n => n.Code == "session.unverified").Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A measurement reads into each precondition's rows, the table's, and its violating rows: TableEmpty's are the table's rows; Fits's
    /// the rows its first item counts, those ALTER COLUMN fails on, beside each class's; Unique's the rows of its duplicate groups, beside
    /// each class's; a NULL from SUM over no row reads as 0. A failed measurement leaves each precondition of the query unanswered by SQL
    /// Server's number, the message withheld for an environment, and a timed-out one says so.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_measurement_reads_into_rows_and_violating_rows_and_a_failed_one_leaves_each_precondition_unanswered_by_its_number()
    {
        var customer = Table(Cases["QST-02"].Queries(), "[dbo].[Customer]");
        var (fits, unique, notNull) = (customer.Preconditions.Single(p => p.Kind is PreconditionKind.Fits),
            customer.Preconditions.Single(p => p.Kind is PreconditionKind.Unique),
            customer.Preconditions.Single(p => p.Kind is PreconditionKind.NotNull));

        var counts = AggregateQueries.Read(customer, Answered(customer, item => item.Precondition is null ? 10
            : item.Class is { } c ? (int)c + 1 : item.Precondition == notNull ? 3 : item.Precondition == fits ? 4 : (long?)null));
        var raised = AggregateQueries.Read(customer, new SqlServer.Measurement.Raised(customer.Query.Site, 245, null));
        var late = AggregateQueries.Read(customer, new SqlServer.Measurement.TimedOut(customer.Query.Site, TimeSpan.FromSeconds(30)));

        Assert.Equal(
            SortedArray.Of(new PreconditionCount(customer.Preconditions.Single(p => p.Kind is PreconditionKind.TableEmpty), 10, 10),
                new PreconditionCount(notNull, 10, 3), new PreconditionCount(fits, 10, 4), new PreconditionCount(unique, 10, 0)),
            counts.Counted);
        Assert.Equal(customer.Classes(fits).Concat(customer.Classes(unique)).Select(c => (int)c + 1L),
            counts.Classes.Select(c => c.Rows));
        Assert.Empty(counts.Unanswered);
        Assert.Empty(raised.Counted);
        Assert.Equal(customer.Preconditions, SortedArray.Of(raised.Unanswered.Select(u => u.Precondition)));
        Assert.All(raised.Unanswered, u => Assert.Equal("SQL Server raised Msg 245; message withheld", u.Why));
        Assert.All(late.Unanswered, u => Assert.Equal("the query ran past its timeout of 30 s", u.Why));
    }

    /// <summary>
    /// TableEmpty read from sys.partitions (§17 item 21: a login holding VIEW DEFINITION alone reads it): the query the allowlist's one
    /// entry for it admits, naming the table as the environment spells it, reads its rows; a table no partition shows to the login is
    /// never read as empty.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "P2")]
    public void TableEmpty_read_from_sys_partitions_names_the_table_as_the_environment_spells_it_and_never_reads_a_hidden_one_as_empty()
    {
        var place = new Case(Pg.With("archive.OrderArchive.Id int NOT NULL"), Pg.With("archive.OrderArchive.Id int NOT NULL"),
            ["archive.OrderArchive"]).Counted.Single();

        var partitions = Value(AggregateQueries.Partitions(place, SqlServer.Session.Assumed));
        var rows = AggregateQueries.Read(partitions, Answered(partitions, SqlServer.Row.Of(2, 7)));
        var hidden = AggregateQueries.Read(partitions, Answered(partitions, SqlServer.Row.Of(0, null)));

        Assert.Contains("[sys].[partitions]", partitions.Query.Statement, StringComparison.Ordinal);
        Assert.Contains("N'archive'", partitions.Query.Statement, StringComparison.Ordinal);
        Assert.Contains("N'OrderArchive'", partitions.Query.Statement, StringComparison.Ordinal);
        Assert.True(AggregateQuery(partitions.Query.Statement));
        Assert.Equal(SortedArray.Of(new PreconditionCount(place.Precondition, 7, 7)), rows.Counted);
        Assert.Empty(hidden.Counted);
        Assert.Contains("never read as empty", Assert.Single(hidden.Unanswered).Why, StringComparison.Ordinal);
    }

    /// <summary>A case: the environment's model, the model the change brings, the tables the plan's sites check, the renames, and the
    /// database's collation where the case names one other than <see cref="Database"/>.</summary>
    internal sealed record Case(Model Environment, Model Head, string[]? Sites = null, Rename[]? Renames = null,
        Collation? DatabaseCollation = null)
    {
        /// <summary>The case's preconditions placed in the environment, as WP 2.5 places them, each counted there.</summary>
        public IReadOnlyList<Coordinates.Counted> Counted
        {
            get
            {
                var (before, after) = (Environment.Elements, Head.Elements);
                var change = Value(Change.Between(before, after, SortedArray.Of(Renames ?? [])));
                var sites = SortedArray.Of((Sites ?? []).Select((t, i) =>
                    new BlockOnPossibleDataLossSite(Model.TableName(t), "the statement after check " + i)));
                return [.. Value(Preconditions.Of(before, after, change, sites))
                    .Select(p => Value(Coordinates.Of(p, change, before, DatabaseCollation ?? Database))).OfType<Coordinates.Counted>()];
            }
        }

        /// <summary>The queries the builders make of the case's counted preconditions.</summary>
        public AggregateQueries.Queries Queries(SqlServer.Session? session = null) => Value(AggregateQueries.Of(Counted, Head.Elements,
            Environment.Elements, DatabaseCollation ?? Database, session ?? SqlServer.Session.Assumed));
    }

    /// <summary>A measurement of the table's query answering each item the value given, an item at a time.</summary>
    internal static SqlServer.Measurement Answered(AggregateQueries.TableQuery table, Func<AggregateQueries.Item, long?> value) =>
        Answered(table, SqlServer.Row.Of([.. table.Items.Select(value)]));

    private static SqlServer.Measurement Answered(AggregateQueries.TableQuery table, SqlServer.Row row) =>
        new SqlServer.Measurement.Answered(table.Query.Site, SortedArray.Of(row));

    private static Precondition Precondition(AggregateQueries.Queries queries, Func<Precondition, bool> which) =>
        queries.Tables.SelectMany(t => t.Preconditions).Single(which);

    private static AggregateQueries.TableQuery Table(AggregateQueries.Queries queries, Precondition precondition) =>
        queries.Tables.Single(t => t.Preconditions.Contains(precondition));

    private static AggregateQueries.TableQuery Table(AggregateQueries.Queries queries, string table) =>
        queries.Tables.Single(t => t.Table.ToString() == table);

    private static string Words(IEnumerable<Class> classes) => string.Join(' ', classes.Select(AggregateQueries.Word));

    /// <summary>Whether the allowlist admits the statement as text, as Measure would run it.</summary>
    private static bool AggregateQuery(string statement) => SqlServer.AggregateQuery.Of(statement, "re-read") is Result<SqlServer.AggregateQuery>.Ok;

    private static ElementKey Key(string type, string schema, string name) => KernelProperties.Key(type, schema, name);
}
