using System;
using System.Collections.Generic;
using System.Linq;
using static DbChange.Kernel.Tests.PlanterCopies;
using static DbChange.Kernel.Tests.QuestionCases;

namespace DbChange.Kernel.Tests;

/// <summary>The planter catalog's cases, by identifier, as the copy, the states and the plan text each names.</summary>
public sealed partial class PlanterCases
{
    private static IReadOnlyDictionary<string, Case>? cases;

    /// <summary>Built on first use, after every field the cases read is set.</summary>
    internal static IReadOnlyDictionary<string, Case> Cases => cases ??= Catalog();

    private static Dictionary<string, Case> Catalog() => new(StringComparer.Ordinal)
    {
        // Types: the literal at the row's ordinal, fitted to the column's width.
        ["PLT-01"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] 1, [b] 1, [c] 1
            """, "a int", "b smallint NULL", "c bigint"),
        ["PLT-02"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [c] 1
            """, "c tinyint"),
        ["PLT-03"] = new(Copy(T("T", "Id int IDENTITY", "c bit", "d bit NULL", "Note nvarchar(20) NULL").Key("Id")),
            [Rows(TableEmpty("T")), Violating(Unique("T", "Note"), RowClasses.Nulls)], """
            [dbo].[T] at 0
              row 1 (presence; Unique on [dbo].[T] ([Note]) Nulls): [Id] omitted, [c] 1, [d] 1, [Note] NULL
              row 2 (presence; Unique on [dbo].[T] ([Note]) Nulls): [Id] omitted, [c] 0, [d] 0, [Note] NULL
            """),
        ["PLT-04"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [c] 1, [d] 1
            """, "c decimal(1,0)", "d numeric(1,0) NULL"),
        ["PLT-05"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] 1.0000000000, [b] 1.00, [c] 0.01
            """, "a decimal(38,10)", "b decimal(18,2) NULL", "c decimal(2,2)"),
        ["PLT-06"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] 1.0000, [b] 1.0000, [c] 1E0, [d] 1E0
            """, "a money", "b smallmoney NULL", "c float", "d real NULL"),
        ["PLT-07"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [c] '2000-01-02', [d] '2000-01-02'
            """, "c date", "d date NULL"),
        ["PLT-08"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] '00:00:01', [b] '00:00:01'
            """, "a time(0)", "b time(7) NULL"),
        ["PLT-09"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] '2000-01-02T00:00:00', [b] '2000-01-02T00:00:00'
            """, "a datetime", "b smalldatetime NULL"),
        ["PLT-10"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] '2000-01-02T00:00:00', [b] '2000-01-02T00:00:00'
            """, "a datetime2(0)", "b datetime2(7) NULL"),
        ["PLT-11"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [c] '2000-01-02T00:00:00+00:00'
            """, "c datetimeoffset(3)"),
        ["PLT-12"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] 'w1', [b] N'w1', [c] N'w1'
            """, "a varchar(10)", "b nvarchar(256) NULL", "c nvarchar(3)"),
        ["PLT-13"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] 'w1', [b] N'w1'
            """, "a char(10)", "b nchar(4) NULL"),
        ["PLT-14"] = new(Copy(T("T", "Id int IDENTITY", "a char(1)", "b nvarchar(1) NULL", "Note nvarchar(20) NULL").Key("Id")),
            [Rows(TableEmpty("T")), Violating(NotNull("T.Note"))], """
            [dbo].[T] at 0
              row 1 (presence; NotNull on [dbo].[T] ([Note])): [Id] omitted, [a] 'w', [b] N'w', [Note] NULL
            """),
        ["PLT-15"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] 'w1', [b] N'w1', [c] 'w1', [d] N'w1'
            """, "a varchar(max)", "b nvarchar(max) NULL", "c text NULL", "d ntext"),
        ["PLT-16"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [Email] N'w1', [WorkEmail] 'w1', [EmailFlag] 1, [Mail] N'w1'
            """, "Email nvarchar(256) NULL", "WorkEmail varchar(30)", "EmailFlag bit", "Mail nvarchar(10) NULL"),
        ["PLT-17"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] 0x77000001, [b] 0x01
            """, "a binary(4)", "b binary(1) NULL"),
        ["PLT-18"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] 0x7701, [b] 0x7701, [c] 0x7701, [d] 0x01
            """, "a varbinary(16)", "b varbinary(max) NULL", "c image NULL", "d varbinary(1) NULL"),
        ["PLT-19"] = new(Copy(T("T", "Id uniqueidentifier", "Ref uniqueidentifier NULL").Key("Id")), [Rows(TableEmpty("T"))], """
            [dbo].[T] at 0
              row 1 (presence): [Id] '77777777-0000-4000-8000-000000000001', [Ref] '77777777-0000-4000-8000-000000000001'
            """),
        ["PLT-20"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [Doc] N'<w n="1"/>', [Extra] omitted
            """, "Doc xml", "Extra xml NULL"),
        ["PLT-23"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [c] N'w1'
            """, "c sql_variant"),
        ["PLT-24"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [Node] '/1/'
            """, "Node hierarchyid"),
        ["PLT-25"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [Place] 'POINT(1 0)', [Spot] 'POINT(1 0)'
            """, "Place geography", "Spot geometry NULL"),
        ["PLT-26"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [c] N'w1', [Ver] omitted
            """, "c nvarchar(20)", "Ver rowversion"),
        ["PLT-27"] = new(Copy(T("T", "Id int IDENTITY", "c nvarchar(6)").Key("Id") is var t
                ? t with { Columns = SortedArray.Of(t.Columns.Select(c => c is Column.Stored { Ordinal: 2 } s
                    ? s with { Alias = SchemaProperties.ShortCode } : c)) } : t),
            [Rows(TableEmpty("T"))], """
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [c] N'w1'
            """),

        // Structure: what SQL Server fills is omitted, and a key past the seeded rows is read from the copy.
        ["PLT-29"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [c] 1
            """, "c int"),
        ["PLT-30"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] 1, [b] omitted, [c] omitted
            """, "a int", "b AS ([a]*(2))", "c AS ([a]+(1)) PERSISTED"),
        ["PLT-32"] = new(Copy(T("T", "Id int IDENTITY", "Status nvarchar(20)", "Created datetime2", "Ref uniqueidentifier", "Flag bit")
                .Key("Id").Defaulted("Status", "(N'')").Defaulted("Created", "(sysutcdatetime())")
                .Defaulted("Ref", "(newsequentialid())").Defaulted("Flag", "((1))")),
            [Rows(TableEmpty("T"))], """
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [Status] N'w1', [Created] '2000-01-02T00:00:00', [Ref] '77777777-0000-4000-8000-000000000001', [Flag] 1
            """),
        ["PLT-33"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [No] omitted, [c] 1
            """, "No int SEQUENCE", "c int"),
        ["PLT-34"] = new(Copy(Temporal), [Rows(TableEmpty("T"))], """
            [dbo].[T] at 0
              row 1 (presence): [Id] the largest [Id] of [dbo].[T] plus 1, [c] N'w1', [ValidFrom] omitted, [ValidTo] omitted
            """),
        ["PLT-35"] = new(Copy(Temporal), [Rows(TableEmpty("T")), Rows(TableEmpty("T_History")),
                Violating(Fits("T_History.c", "nvarchar(20)", "nvarchar(5)"), RowClasses.OverLength)], """
            [dbo].[T] at 0
              row 1 (presence): [Id] the largest [Id] of [dbo].[T] plus 1, [c] N'w1', [ValidFrom] omitted, [ValidTo] omitted
            needs a declared row of [dbo].[T_History]: [dbo].[T_History] is the history table of [dbo].[T], whose rows SQL Server writes only from updates of [dbo].[T].
            """),
        ["PLT-36"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] N'w1', [b] 1, [Props] omitted
            """, "a nvarchar(20) SPARSE NULL", "b int SPARSE NULL", "Props xml NULL"),
        ["PLT-37"] = Types("""
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [Code] N'w1'
            """, "Code nvarchar(20) Latin1_General_CS_AS"),
        ["PLT-40"] = new(PgCopy.With(PgCopy.Find(TableName("Product"))!.Unique("UX_Product_Name", "Name").Unique("UX_Product_Code_Filt", "Code")),
            [Rows(HasProducts), Violating(NarrowCode, RowClasses.OverLength | RowClasses.OverBytes)], """
            [dbo].[Product] at 6
              row 1 (presence; Fits on [dbo].[Product] ([Code]) OverLength): [Id] omitted, [Code] N'w1wwwwwwwww', [Name] N'w1', [LegacyCode] N'w1', [CategoryId] 1
              row 2 (presence; Fits on [dbo].[Product] ([Code]) OverBytes): [Id] omitted, [Code] N'w2         ', [Name] N'w2', [LegacyCode] N'w2', [CategoryId] 2
            """),
        ["PLT-41"] = new(Copy(T("Kind", "Id int", "Label nvarchar(20) NULL").Key("Id")),
            [Rows(TableEmpty("Kind")), Violating(NotNull("Kind.Label"))], """
            [dbo].[Kind] at 0
              row 1 (presence; NotNull on [dbo].[Kind] ([Label])): [Id] the largest [Id] of [dbo].[Kind] plus 1, [Label] NULL
            """),
        ["PLT-42"] = new(Copy(NarrowFlag), [Rows(TableEmpty("T")), Violating(Unique("T", "Note"), RowClasses.Nulls)], """
            needs a declared row of [dbo].[T]: [dbo].[T].[Flag] is a key with room for one planted value, fewer than the rows the plan plants.
            """),

        // Ancestors and cycles: a NOT NULL foreign key is met by a planted parent, or by a seeded one WP 3.5 finds.
        ["PLT-49"] = new(PgCopy.With(PgCopy.Find(TableName("CustomerAddress"))!
                .References("FK_CustomerAddress_Customer", "Customer", ("CustomerId", "Id"))),
            [Rows(TableEmpty("CustomerAddress"))], $"""
            [dbo].[Customer] at 2
              row 1 (presence): {Customer}
            [dbo].[CustomerAddress] at 3
              row 1 (presence): [Id] omitted, [CustomerId] the [Id] of row 1 of [dbo].[Customer], [Line1] N'w1', [City] N'w1', [PostalCode] N'w1'
            """),
        ["PLT-50"] = new(PgCopy.With(Ordering.References("FK_Order_Status", "Status", ("StatusId", "Id")),
                PgCopy.Find(TableName("OrderLine"))!.References("FK_OrderLine_Order", "Order", ("OrderId", "Id"))),
            [Rows(TableEmpty("OrderLine"))], $"""
            [dbo].[Customer] at 2
              row 1 (presence): {Customer}
            [dbo].[Status] at 5
              row 1 (presence): [Id] the largest [Id] of [dbo].[Status] plus 1, [Code] N'w1', [IsActive] 1
            [dbo].[Order] at 6
              row 1 (presence): [Id] omitted, [CustomerId] the [Id] of row 1 of [dbo].[Customer], [StatusId] the [Id] of row 1 of [dbo].[Status], [Total] 1.00, [StatusText] N'w1'
            [dbo].[OrderLine] at 7
              row 1 (presence): [Id] omitted, [OrderId] the [Id] of row 1 of [dbo].[Order], [LineNumber] 1, [Amount] 1.00
            """),
        ["PLT-51"] = new(Copy(Shipment,
                ShipmentNote.References("FK_ShipmentNote_Shipment", "Shipment", ("OrderId", "OrderId"), ("LineNumber", "LineNumber"))),
            [Rows(TableEmpty("ShipmentNote"))], """
            [dbo].[Shipment] at 0
              row 1 (presence): [OrderId] the largest [OrderId] of [dbo].[Shipment] plus 1, [LineNumber] the largest [LineNumber] of [dbo].[Shipment] plus 1
            [dbo].[ShipmentNote] at 1
              row 1 (presence): [Id] omitted, [OrderId] the [OrderId] of row 1 of [dbo].[Shipment], [LineNumber] the [LineNumber] of row 1 of [dbo].[Shipment]
            """),
        ["PLT-52"] = new(PgCopy.With(PgCopy.Find(TableName("Customer"))!.References("FK_Customer_Account", "Account", ("AccountId", "Id"))),
            [Rows(HasCustomers)], """
            [dbo].[Customer] at 2
              row 1 (presence): [Id] omitted, [Name] N'w1', [Email] N'w1', [ContactPhone] N'w1', [Region] N'w1', [AccountId] NULL
            """),
        ["PLT-53"] = new(PgCopy.With(T("Product", "Id int IDENTITY", "Code nvarchar(50)", "Name nvarchar(200) NULL", "LegacyCode nvarchar(40)",
                "CategoryId int").Key("Id").References("FK_Product_Category", "Category", ("CategoryId", "Id"))),
            [Rows(HasProducts)], """
            [dbo].[Category] at 1
              row 1 (presence): [Id] the largest [Id] of [dbo].[Category] plus 1, [Code] N'w1', [IsActive] 1
            [dbo].[Product] at 6
              row 1 (presence): [Id] omitted, [Code] N'w1', [Name] N'w1', [LegacyCode] N'w1', [CategoryId] the [Id] of row 1 of [dbo].[Category]
            """),
        ["PLT-55"] = new(PgCopy.With(Ordering), [Rows(HasOrders), Rows(HasCustomers)], $"""
            [dbo].[Customer] at 2
              row 1 (presence): {Customer}
            [dbo].[Order] at 4
              row 1 (presence): [Id] omitted, [CustomerId] the [Id] of row 1 of [dbo].[Customer], [StatusId] 1, [Total] 1.00, [StatusText] N'w1'
            """),
        ["PLT-56"] = new(PgCopy, [Rows(HasOrders), Rows(ForeignKey("Order", "Customer", ("CustomerId", "Id")))], $"""
            [dbo].[Customer] at 2
              row 1 (presence): {Customer}
            [dbo].[Order] at 4
              row 1 (presence): [Id] omitted, [CustomerId] the [Id] of row 1 of [dbo].[Customer], [StatusId] 1, [Total] 1.00, [StatusText] N'w1'
            """),
        ["PLT-57"] = new(Copy(Employee), [Rows(TableEmpty("Employee"))], """
            needs a declared row of [dbo].[Employee]: [dbo].[Employee] references itself through a NOT NULL foreign key ([dbo].[Employee] → [dbo].[Employee]; foreign key [dbo].[FK_Employee_Manager]), so no order of INSERTs plants a row in it.
            """),
        ["PLT-58"] = new(Copy(T("Dept", "Id int", "HeadId int").Key("Id").References("FK_Dept_Head", "Person", ("HeadId", "Id")),
                T("Person", "Id int", "DeptId int").Key("Id").References("FK_Person_Dept", "Dept", ("DeptId", "Id"))),
            [Rows(TableEmpty("Person"))], """
            needs a declared row of [dbo].[Dept]: [dbo].[Dept] and [dbo].[Person] reference one another through NOT NULL foreign keys ([dbo].[Dept] → [dbo].[Person] → [dbo].[Dept]; foreign keys [dbo].[FK_Dept_Head] and [dbo].[FK_Person_Dept]), so no order of INSERTs plants a row in any of them.
            needs a declared row of [dbo].[Person]: [dbo].[Dept] and [dbo].[Person] reference one another through NOT NULL foreign keys ([dbo].[Dept] → [dbo].[Person] → [dbo].[Dept]; foreign keys [dbo].[FK_Dept_Head] and [dbo].[FK_Person_Dept]), so no order of INSERTs plants a row in any of them.
            """),
        ["PLT-59"] = new(PgCopy.With(Ordering), [Rows(HasOrders), Empty(HasCustomers)], """
            [dbo].[Order] at 4
              row 1 (presence): [Id] omitted, [CustomerId] the largest [Id] of [dbo].[Customer] plus 1000, [StatusId] 1, [Total] 1.00, [StatusText] N'w1'; suspends ForeignKeyConstraint [dbo].[FK_Order_Customer]
            """),

        // Violating rows by class: a valid row in every other column, which serves as presence.
        ["PLT-61"] = new(PgCopy, [Rows(HasCustomers), Violating(NotNull("Customer.Email"))], """
            [dbo].[Customer] at 2
              row 1 (presence; NotNull on [dbo].[Customer] ([Email])): [Id] omitted, [Name] N'w1', [Email] NULL, [ContactPhone] N'w1', [Region] N'w1', [AccountId] 1
            """),
        ["PLT-62"] = new(PgCopy, [Rows(HasProducts), Violating(NarrowCode, RowClasses.OverLength)], """
            [dbo].[Product] at 6
              row 1 (presence; Fits on [dbo].[Product] ([Code]) OverLength): [Id] omitted, [Code] N'w1wwwwwwwww', [Name] N'w1', [LegacyCode] N'w1', [CategoryId] 1
            """),
        ["PLT-63"] = new(PgCopy, [Rows(HasProducts), Holding(NarrowCode, RowClasses.OverBytes)], """
            [dbo].[Product] at 6
              row 1 (presence; Fits on [dbo].[Product] ([Code]) OverBytes): [Id] omitted, [Code] N'w1         ', [Name] N'w1', [LegacyCode] N'w1', [CategoryId] 1
            """),
        ["PLT-64"] = new(Copy(T("T", "Id int IDENTITY", "c nvarchar(20)").Key("Id")),
            [Rows(TableEmpty("T")), Violating(Fits("T.c", "nvarchar(20)", "int"), RowClasses.NotConvertible)], """
            [dbo].[T] at 0
              row 1 (presence; Fits on [dbo].[T] ([c]) NotConvertible): [Id] omitted, [c] N'w1'
            """),
        ["PLT-67"] = new(Copy(T("T", "Id int IDENTITY", "Qty nvarchar(10)").Key("Id")),
            [Rows(TableEmpty("T")), Holding(Fits("T.Qty", "nvarchar(10)", "int"), RowClasses.EmptyString)], """
            [dbo].[T] at 0
              row 1 (presence; Fits on [dbo].[T] ([Qty]) EmptyString): [Id] omitted, [Qty] N''
            """),
        ["PLT-68"] = new(Copy(T("T", "Id int IDENTITY", "Total decimal(18,2)", "Qty int").Key("Id")),
            [Rows(TableEmpty("T")), Violating(Fits("T.Total", "decimal(18,2)", "decimal(5,2)"), RowClasses.Overflow),
                Violating(Fits("T.Qty", "int", "smallint"), RowClasses.Overflow)], """
            [dbo].[T] at 0
              row 1 (presence; Fits on [dbo].[T] ([Total]) Overflow; Fits on [dbo].[T] ([Qty]) Overflow): [Id] omitted, [Total] 1000.00, [Qty] 32768
            """),
        ["PLT-69"] = new(PgCopy, [Rows(HasProducts), Violating(Unique("Product", "Code"), RowClasses.Exact)], """
            [dbo].[Product] at 6
              row 1 (presence; Unique on [dbo].[Product] ([Code]) Exact): [Id] omitted, [Code] N'w1', [Name] N'w1', [LegacyCode] N'w1', [CategoryId] 1
              row 2 (presence; Unique on [dbo].[Product] ([Code]) Exact): [Id] omitted, [Code] N'w1', [Name] N'w2', [LegacyCode] N'w2', [CategoryId] 2
            """),
        ["PLT-70"] = new(Copy(T("T", "Id int IDENTITY", "Code nvarchar(20) Latin1_General_CS_AS").Key("Id").Unique("UX_T_Code", "Code")),
            [Rows(Fits("T.Code", "nvarchar(20) Latin1_General_CS_AS", "nvarchar(20) Latin1_General_CI_AS")),
                Violating(Unique("T", "Code"), RowClasses.Case)], """
            [dbo].[T] at 0
              row 1 (presence; Unique on [dbo].[T] ([Code]) Case): [Id] omitted, [Code] N'w1'
              row 2 (presence; Unique on [dbo].[T] ([Code]) Case): [Id] omitted, [Code] N'W1'
            """),
        ["PLT-71"] = new(Copy(T("T", "Id int IDENTITY", "Name nvarchar(50) Latin1_General_CI_AI").Key("Id")),
            [Rows(TableEmpty("T")), Violating(Unique("T", "Name"), RowClasses.Accent)], """
            [dbo].[T] at 0
              row 1 (presence; Unique on [dbo].[T] ([Name]) Accent): [Id] omitted, [Name] N'w1'
              row 2 (presence; Unique on [dbo].[T] ([Name]) Accent): [Id] omitted, [Name] N'ŵ1'
            """),
        ["PLT-72"] = new(PgCopy, [Rows(HasProducts), Violating(Unique("Product", "Code"), RowClasses.TrailingSpace)], """
            [dbo].[Product] at 6
              row 1 (presence; Unique on [dbo].[Product] ([Code]) TrailingSpace): [Id] omitted, [Code] N'w1', [Name] N'w1', [LegacyCode] N'w1', [CategoryId] 1
              row 2 (presence; Unique on [dbo].[Product] ([Code]) TrailingSpace): [Id] omitted, [Code] N'w1 ', [Name] N'w2', [LegacyCode] N'w2', [CategoryId] 2
            """),
        ["PLT-73"] = new(PgCopy, [Violating(Unique("Customer", "Email"), RowClasses.Nulls)], """
            [dbo].[Customer] at 2
              row 1 (presence; Unique on [dbo].[Customer] ([Email]) Nulls): [Id] omitted, [Name] N'w1', [Email] NULL, [ContactPhone] N'w1', [Region] N'w1', [AccountId] 1
              row 2 (presence; Unique on [dbo].[Customer] ([Email]) Nulls): [Id] omitted, [Name] N'w2', [Email] NULL, [ContactPhone] N'w2', [Region] N'w2', [AccountId] 2
            """),
        ["PLT-74"] = new(PgCopy, [Rows(Unique("Customer", ["Email"], "([Email] IS NOT NULL)", "Email"))], $"""
            [dbo].[Customer] at 2
              row 1 (presence): {Customer}
            """),
        ["PLT-75"] = new(PgCopy, [Rows(HasOrders), Violating(ForeignKey("Order", "Customer", ("CustomerId", "Id")))], """
            [dbo].[Order] at 4
              row 1 (presence; ForeignKey on [dbo].[Order] ([CustomerId])): [Id] omitted, [CustomerId] the largest [Id] of [dbo].[Customer] plus 1000, [StatusId] 1, [Total] 1.00, [StatusText] N'w1'
            """),
        ["PLT-76"] = new(Copy(T("P", "Id uniqueidentifier").Key("Id"), T("C", "Id int IDENTITY", "PRef uniqueidentifier").Key("Id")),
            [Rows(TableEmpty("C")), Violating(ForeignKey("C", "P", ("PRef", "Id")))], """
            [dbo].[C] at 1
              row 1 (presence; ForeignKey on [dbo].[C] ([PRef])): [Id] omitted, [PRef] 'FFFFFFFF-FFFF-4FFF-BFFF-000000000001'
            """),
        ["PLT-77"] = new(Copy(Shipment, ShipmentNote),
            [Rows(TableEmpty("ShipmentNote")),
                Violating(ForeignKey("ShipmentNote", "Shipment", ("OrderId", "OrderId"), ("LineNumber", "LineNumber")))], """
            [dbo].[ShipmentNote] at 1
              row 1 (presence; ForeignKey on [dbo].[ShipmentNote] ([LineNumber], [OrderId])): [Id] omitted, [OrderId] the largest [OrderId] of [dbo].[Shipment] plus 1000, [LineNumber] the largest [LineNumber] of [dbo].[Shipment] plus 1000
            """),
        ["PLT-78"] = new(PgCopy, [Rows(HasOrders), Violating(TotalPositive)], $"""
            [dbo].[Order] at 4
              row 1 (presence): {Order}
            needs a declared row of [dbo].[Order]: [dbo].[Order] holds rows in the environment that make the check [Total]>(0) false, and the planter writes no literal to make a check false, since a check's expression is free-form.
            """),
        ["PLT-79"] = new(PgCopy, [Rows(HasOrders), Violating(TotalPositive)], $"""
            [dbo].[Order] at 4
              row 1 (presence): {Order}
            """, Declared: new([], [TotalPositive])),
        ["PLT-83"] = new(PgCopy.With(Ordering), [Rows(HasOrders), Rows(HasCustomers)], $"""
            [dbo].[Customer] at 2
              row 1 (presence): {Customer}
            """, Declared: new([TableName("Order")], [])),
        ["PLT-84"] = new(PgCopy.With(PgCopy.Find(TableName("Product"))!.Checked("CK_Product_Code", "([Code] like 'ABC%')")),
            [Rows(HasProducts), Violating(NotNull("Product.Name"))], "", Declared: new([TableName("Product")], [])),
        ["PLT-86"] = new(PgCopy,
            [Rows(HasProducts), Violating(NotNull("Product.Name")), Violating(NarrowCode, RowClasses.OverLength | RowClasses.OverBytes)], """
            [dbo].[Product] at 6
              row 1 (presence; NotNull on [dbo].[Product] ([Name]); Fits on [dbo].[Product] ([Code]) OverLength): [Id] omitted, [Code] N'w1wwwwwwwww', [Name] NULL, [LegacyCode] N'w1', [CategoryId] 1
              row 2 (presence; Fits on [dbo].[Product] ([Code]) OverBytes): [Id] omitted, [Code] N'w2         ', [Name] N'w2', [LegacyCode] N'w2', [CategoryId] 2
            """),

        // Trust: the orphan an environment holds under a foreign key enabled and untrusted is planted with the copy's foreign key suspended.
        ["PLT-89"] = new(PgCopy.With(Ordering.Checked("CK_Order_Total", "([Total]>=(0))")),
            [Rows(HasOrders), Violating(ForeignKey("Order", "Customer", ("CustomerId", "Id"))), Violating(TotalNonNegative)], """
            [dbo].[Order] at 4
              row 1 (presence; ForeignKey on [dbo].[Order] ([CustomerId])): [Id] omitted, [CustomerId] the largest [Id] of [dbo].[Customer] plus 1000, [StatusId] 1, [Total] 1.00, [StatusText] N'w1'; suspends ForeignKeyConstraint [dbo].[FK_Order_Customer]
            """, Declared: new([], [TotalNonNegative])),

        // Acceptance: a value of no class, so that a populated but clean table stays clean.
        ["PLT-94"] = new(Copy(T("T", "Id int IDENTITY", "Qty nvarchar(10)").Key("Id").Defaulted("Qty", "(N'')")),
            [Rows(TableEmpty("T")), Rows(Fits("T.Qty", "nvarchar(10)", "int"))], """
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [Qty] N'1'
            """),
        ["PLT-95"] = new(Copy(T("T", "Id int IDENTITY", "a nvarchar(20)", "b nvarchar(20)", "c nvarchar(40)", "d nvarchar(50)").Key("Id")),
            [Rows(Fits("T.a", "nvarchar(20)", "int")), Rows(Fits("T.b", "nvarchar(20)", "date")),
                Rows(Fits("T.c", "nvarchar(40)", "uniqueidentifier")), Rows(Fits("T.d", "nvarchar(50)", "nvarchar(1)"))], """
            [dbo].[T] at 0
              row 1 (presence): [Id] omitted, [a] N'1', [b] N'2000-01-02', [c] N'77777777-0000-4000-8000-000000000001', [d] N'w'
            """),

        // Determinism and minimality: a pure function of its inputs, the largest of a table's needs.
        ["PLT-104"] = new(PgCopy,
            [Violating(NarrowCode, RowClasses.OverLength), Rows(HasProducts), Violating(NotNull("Product.Name")), Rows(HasCustomers)], $"""
            [dbo].[Customer] at 2
              row 1 (presence): {Customer}
            [dbo].[Product] at 6
              row 1 (presence; NotNull on [dbo].[Product] ([Name]); Fits on [dbo].[Product] ([Code]) OverLength): [Id] omitted, [Code] N'w1wwwwwwwww', [Name] NULL, [LegacyCode] N'w1', [CategoryId] 1
            """),
        ["PLT-106"] = new(PgCopy.With(Ordering),
            [Rows(HasCustomers), Violating(NotNull("Customer.Email")), Rows(HasOrders), Rows(HasProducts),
                Violating(Unique("Product", "Code"), RowClasses.Exact)], $"""
            [dbo].[Customer] at 2
              row 1 (presence; NotNull on [dbo].[Customer] ([Email])): [Id] omitted, [Name] N'w1', [Email] NULL, [ContactPhone] N'w1', [Region] N'w1', [AccountId] 1
            [dbo].[Order] at 4
              row 1 (presence): [Id] omitted, [CustomerId] the [Id] of row 1 of [dbo].[Customer], [StatusId] 1, [Total] 1.00, [StatusText] N'w1'
            [dbo].[Product] at 6
              row 1 (presence; Unique on [dbo].[Product] ([Code]) Exact): {Product}
              row 2 (presence; Unique on [dbo].[Product] ([Code]) Exact): [Id] omitted, [Code] N'w1', [Name] N'w2', [LegacyCode] N'w2', [CategoryId] 2
            """),
        ["PLT-107"] = new(PgCopy,
            [Rows(HasCustomers), Violating(NotNull("Customer.Email")), Rows(HasProducts), Violating(Unique("Product", "Code"),
                RowClasses.Exact)], $"""
            [dbo].[Customer] at 2
              row 1 (presence; NotNull on [dbo].[Customer] ([Email])): [Id] omitted, [Name] N'w1', [Email] NULL, [ContactPhone] N'w1', [Region] N'w1', [AccountId] 1
            [dbo].[Product] at 6
              row 1 (presence; Unique on [dbo].[Product] ([Code]) Exact): {Product}
              row 2 (presence; Unique on [dbo].[Product] ([Code]) Exact): [Id] omitted, [Code] N'w1', [Name] N'w2', [LegacyCode] N'w2', [CategoryId] 2
            """),

        // Refusals: every need in one pass, none downgraded.
        ["PLT-114"] = new(Copy(T("T", "Id int IDENTITY", "IsPrimary bit").Key("Id").Unique("UQ_T_IsPrimary", "IsPrimary"),
                T("U", "Id int IDENTITY", "Grade char(1)").Key("Id").Unique("UQ_U_Grade", "Grade")), [], """
            needs a declared row of [dbo].[T]: [dbo].[T].[IsPrimary] is a key with room for 2 planted values, fewer than the rows the plan plants.
            needs a declared row of [dbo].[U]: [dbo].[U].[Grade] is a key with room for one planted value, fewer than the rows the plan plants.
            """, Presence: true),
        ["PLT-118"] = new(PgCopy.With(Employee, NarrowFlag),
            [Rows(TableEmpty("Employee")), Rows(TableEmpty("T")), Violating(Unique("T", "Note"), RowClasses.Nulls), Rows(HasOrders),
                Violating(TotalPositive)], $"""
            [dbo].[Order] at 5
              row 1 (presence): {Order}
            needs a declared row of [dbo].[Employee]: [dbo].[Employee] references itself through a NOT NULL foreign key ([dbo].[Employee] → [dbo].[Employee]; foreign key [dbo].[FK_Employee_Manager]), so no order of INSERTs plants a row in it.
            needs a declared row of [dbo].[Order]: [dbo].[Order] holds rows in the environment that make the check [Total]>(0) false, and the planter writes no literal to make a check false, since a check's expression is free-form.
            needs a declared row of [dbo].[T]: [dbo].[T].[Flag] is a key with room for one planted value, fewer than the rows the plan plants.
            """),
        ["PLT-120"] = new(PgCopy, [Violating(TotalPositive)], $"""
            [dbo].[Order] at 4
              row 1 (presence): {Order}
            needs a declared row of [dbo].[Order]: [dbo].[Order] holds rows in the environment that make the check [Total]>(0) false, and the planter writes no literal to make a check false, since a check's expression is free-form.
            """),
        ["PLT-125"] = new(PgCopy, [Rows(ForeignKey("Customer", "Account", ("AccountId", "Id")))], """
            [dbo].[Account] at 0
              row 1 (presence): [Id] omitted, [Name] N'w1', [Region] N'w1'
            [dbo].[Customer] at 2
              row 1 (presence): [Id] omitted, [Name] N'w1', [Email] N'w1', [ContactPhone] N'w1', [Region] N'w1', [AccountId] the [Id] of row 1 of [dbo].[Account]
            """),
    };

    /// <summary>PLT-34's system-versioned T, its period columns generated, and T_History.</summary>
    private static Table[] Temporal => T("T", "Id int", "c nvarchar(20)", "ValidFrom datetime2 START", "ValidTo datetime2 END").Key("Id")
        .Versioned("T_History", "Id int", "c nvarchar(20)", "ValidFrom datetime2", "ValidTo datetime2");
}
