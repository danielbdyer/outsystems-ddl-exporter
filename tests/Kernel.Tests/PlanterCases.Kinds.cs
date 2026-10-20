using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;
using static DbChange.Kernel.Tests.PlanterCopies;
using static DbChange.Kernel.Tests.QuestionCases;

namespace DbChange.Kernel.Tests;

/// <summary>
/// The planter catalog's cases for the kinds only a plan's statements show (WP 2.1's second slice): KeyLength's key just past its limit,
/// nonclustered and clustered (PLT-80, PLT-81); a rebuild's gapped identity, two rows past the copy's largest id by 2 and by 5 (PLT-82);
/// and a computed column, a need for the declared row whose expression fails until one is declared (PLT-127, and PLT-120's computed
/// half). Beside them, v3's own cases for the kinds the catalog plants nothing for: a Temporal period that ends before it starts, an
/// Authored statement and a SeedDrift, each a need for a declared row, and a Scan, which presence answers. The catalog writes HasRows
/// beside each of PLT-80 to PLT-82, which v3 reads from the kind's own state, populated, so the one state stands for both.
/// </summary>
public sealed partial class PlanterCases
{
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("PLT-80")]
    [InlineData("PLT-81")]
    [InlineData("PLT-82")]
    [InlineData("PLT-127")]
    [InlineData("PLT-127 (declared)")]
    [InlineData("Temporal: a period that ends before it starts")]
    [InlineData("Temporal: a period of columns the planter writes no date for")]
    [InlineData("Authored: a statement of the post-deploy script")]
    [InlineData("SeedDrift: a seed whose code the environment holds under another key")]
    [InlineData("Scan: an index built over the rows")]
    public void A_kind_only_a_plan_s_statements_show_plants_its_witness_or_names_the_declared_row_it_needs(string id) => Plans(id);

    private static readonly Table Noted = T("Product", "Id int IDENTITY", "Code nvarchar(50)", "Name nvarchar(200) NULL",
        "LegacyCode nvarchar(40)", "CategoryId int NULL", "Notes nvarchar(1000) NULL").Key("Id");

    private static readonly Table Cents = T("Order", "Id int IDENTITY", "CustomerId int", "StatusId int", "Total decimal(18,2)",
        "StatusText nvarchar(20)", "TotalCents AS CAST(Total * 100 AS int) PERSISTED").Key("Id");

    private static readonly Precondition TotalCents = new(TableName("Order"), Names(["TotalCents"]),
        new PreconditionKind.Computed("(CAST([Total]*(100) AS [int]))"));

    private static readonly Table Period = T("Booking", "Id int IDENTITY", "ValidFrom datetime2", "ValidTo datetime2").Key("Id");

    private static Precondition Ends(string table, string start, string end) => new(TableName(table), Names([start, end]),
        new PreconditionKind.Temporal(Ok(Name.Of(start)), Ok(Name.Of(end)), null));

    private static Dictionary<string, Case> Kinds() => new(StringComparer.Ordinal)
    {
        ["PLT-80"] = new(PgCopy.With(Noted), [Violating(new Precondition(TableName("Product"), Names(["Notes"]),
            new PreconditionKind.KeyLength(Part("IX_Product_Notes"), false)))], $"""
            [dbo].[Product] at 6
              row 1 (presence; KeyLength on [dbo].[Product] ([Notes]) of [IX_Product_Notes] at most 1700 bytes): {Product}, [Notes] N'w1{new string('w', 849)}'
            """),
        ["PLT-81"] = new(Copy(T("T", "Id int", "Code varchar(1000)")), [Violating(new Precondition(TableName("T"), Names(["Code"]),
            new PreconditionKind.KeyLength(Part("CX_T_Code"), true)))], $"""
            [dbo].[T] at 0
              row 1 (presence; KeyLength on [dbo].[T] ([Code]) of [CX_T_Code] at most 900 bytes): [Id] 1, [Code] 'w1{new string('w', 899)}'
            """),
        ["PLT-82"] = new(PgCopy, [Holding(new Precondition(TableName("Customer"), Names(["Id"]), new PreconditionKind.Rebuild()),
            RowClasses.Gapped)], """
            [dbo].[Customer] at 2
              row 1 (presence; Rebuild on [dbo].[Customer] ([Id]) Gapped): [Id] the largest [Id] of [dbo].[Customer] plus 2, [Name] N'w1', [Email] N'w1', [ContactPhone] N'w1', [Region] N'w1', [AccountId] 1
              row 2 (presence; Rebuild on [dbo].[Customer] ([Id]) Gapped): [Id] the largest [Id] of [dbo].[Customer] plus 5, [Name] N'w2', [Email] N'w2', [ContactPhone] N'w2', [Region] N'w2', [AccountId] 2
            """),
        ["PLT-127"] = new(PgCopy.With(Cents), [Rows(HasOrders)], $"""
            [dbo].[Order] at 4
              row 1 (presence): {Order}, [TotalCents] omitted
            needs a declared row of [dbo].[Order]: [dbo].[Order].[TotalCents] is computed as (CAST([Total]*(100) AS [int])) over every row of the table, and the planter reads no expression, so it writes no value the expression fails on.
            """, Undeclared: [TotalCents]),
        ["PLT-127 (declared)"] = new(PgCopy.With(Cents), [Rows(HasOrders)], $"""
            [dbo].[Order] at 4
              row 1 (presence): {Order}, [TotalCents] omitted
            """, Declared: new([], [TotalCents]), Undeclared: [TotalCents]),
        ["Temporal: a period that ends before it starts"] = new(Copy(Period), [Violating(Ends("Booking", "ValidFrom", "ValidTo"))], """
            [dbo].[Booking] at 0
              row 1 (presence; Temporal on [dbo].[Booking] ([ValidFrom], [ValidTo]) from [ValidFrom] to [ValidTo]): [Id] omitted, [ValidFrom] '2000-01-03T00:00:00', [ValidTo] '2000-01-02T00:00:00'
            """),
        ["Temporal: a period of columns the planter writes no date for"] = new(
            Copy(T("Booking", "Id int IDENTITY", "ValidFrom int", "ValidTo int").Key("Id")), [Violating(Ends("Booking", "ValidFrom", "ValidTo"))],
            """
            needs a declared row of [dbo].[Booking]: The planter writes no value that breaks the precondition into [dbo].[Booking].[ValidFrom].
            """),
        ["Authored: a statement of the post-deploy script"] = new(PgCopy, [Rows(HasOrders)], $"""
            [dbo].[Order] at 4
              row 1 (presence): {Order}
            needs a declared row of [dbo].[Order]: The change's post-deploy script reads [dbo].[Order] at line 12, and only a person who looks at the rows it reads in the environment can declare the shapes it meets.
            """, Undeclared: [new(TableName("Order"), Names(["Total"]), new PreconditionKind.Authored(ScriptPart.PostDeploy, 12))]),
        ["SeedDrift: a seed whose code the environment holds under another key"] = new(PgCopy,
            [Violating(new Precondition(TableName("Status"), Names(["Id"]), new PreconditionKind.SeedDrift(Names(["Code"]),
                ScriptPart.PostDeploy, 3)))], """
            [dbo].[Status] at 7
              row 1 (presence): [Id] the largest [Id] of [dbo].[Status] plus 1, [Code] N'w1', [IsActive] 1
            needs a declared row of [dbo].[Status]: [dbo].[Status] holds rows in the environment whose key or code disagrees with the rows the change's seed at line 3 writes, and the planter writes no value of the seed's under another key.
            """),
        ["Scan: an index built over the rows"] = new(PgCopy, [Rows(new Precondition(TableName("Order"), [], new PreconditionKind.Scan()))],
            $"""
            [dbo].[Order] at 4
              row 1 (presence): {Order}
            """),
    };
}
