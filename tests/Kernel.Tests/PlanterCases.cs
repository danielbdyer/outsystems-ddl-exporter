using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;
using static DbChange.Kernel.Tests.PlanterCopies;
using static DbChange.Kernel.Tests.QuestionCases;

namespace DbChange.Kernel.Tests;

/// <summary>
/// The Facsimile's planter catalog (archive/v2/ssdt-agent/facsimile/cases/20-planter.md) for the six kinds WP 2.1's first slice holds,
/// each case under its identifier: its three pure cases whole, and of each sql or corpus case the plan, the rows and values WP 3.5
/// plants (Witness.Plan). What the copy then answers when counted, and what SQL Server does with the rows, waits on WP 3.5's fixture
/// tests. A case is its copy, its states and the plan as text (Witness.ToString), so a case reads as the catalog writes it. The cases
/// follow v3 where its decisions differ from the catalog's: a nullable xml column no precondition reads is left to its default, since a
/// column set and a typed xml column both read as one (PLT-20, PLT-36); a check is planted in no column of the catalog's own, whose
/// rejection WP 3.5 meets at the INSERT (PLT-43, PLT-44).
/// </summary>
public sealed partial class PlanterCases
{
    /// <summary>A case: the copy's schema, the states the environment answered, the plan the case names; and, where the case says so, no
    /// existing data, the declared witnesses, and the change.</summary>
    internal sealed record Case(Schema Schema, PreconditionState[] States, string Plan, bool Presence = false,
        Witness.Declared? Declared = null);

    /// <summary>A table T with an identity Id and the columns written, populated.</summary>
    private static Case Types(string plan, params string[] columns) => new(
        Copy(T("T", ["Id int IDENTITY", .. columns]).Key("Id")), [Rows(TableEmpty("T"))], plan);

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("PLT-78")]
    [InlineData("PLT-104")]
    [InlineData("PLT-120")]
    public void A_pure_case_of_the_Facsimile_s_planter_catalog_plans_the_rows_and_the_needs_it_names(string id) => Plans(id);

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("PLT-01")]
    [InlineData("PLT-02")]
    [InlineData("PLT-03")]
    [InlineData("PLT-04")]
    [InlineData("PLT-05")]
    [InlineData("PLT-06")]
    [InlineData("PLT-07")]
    [InlineData("PLT-08")]
    [InlineData("PLT-09")]
    [InlineData("PLT-10")]
    [InlineData("PLT-11")]
    [InlineData("PLT-12")]
    [InlineData("PLT-13")]
    [InlineData("PLT-14")]
    [InlineData("PLT-15")]
    [InlineData("PLT-16")]
    [InlineData("PLT-17")]
    [InlineData("PLT-18")]
    [InlineData("PLT-19")]
    [InlineData("PLT-20")]
    [InlineData("PLT-23")]
    [InlineData("PLT-24")]
    [InlineData("PLT-25")]
    [InlineData("PLT-26")]
    [InlineData("PLT-27")]
    [InlineData("PLT-29")]
    [InlineData("PLT-30")]
    [InlineData("PLT-32")]
    [InlineData("PLT-33")]
    [InlineData("PLT-34")]
    [InlineData("PLT-35")]
    [InlineData("PLT-36")]
    [InlineData("PLT-37")]
    [InlineData("PLT-40")]
    [InlineData("PLT-41")]
    [InlineData("PLT-42")]
    [InlineData("PLT-49")]
    [InlineData("PLT-50")]
    [InlineData("PLT-51")]
    [InlineData("PLT-52")]
    [InlineData("PLT-53")]
    [InlineData("PLT-55")]
    [InlineData("PLT-56")]
    [InlineData("PLT-57")]
    [InlineData("PLT-58")]
    [InlineData("PLT-59")]
    [InlineData("PLT-61")]
    [InlineData("PLT-62")]
    [InlineData("PLT-63")]
    [InlineData("PLT-64")]
    [InlineData("PLT-67")]
    [InlineData("PLT-68")]
    [InlineData("PLT-69")]
    [InlineData("PLT-70")]
    [InlineData("PLT-71")]
    [InlineData("PLT-72")]
    [InlineData("PLT-73")]
    [InlineData("PLT-74")]
    [InlineData("PLT-75")]
    [InlineData("PLT-76")]
    [InlineData("PLT-77")]
    [InlineData("PLT-79")]
    [InlineData("PLT-83")]
    [InlineData("PLT-84")]
    [InlineData("PLT-86")]
    [InlineData("PLT-94")]
    [InlineData("PLT-95")]
    [InlineData("PLT-106")]
    [InlineData("PLT-107")]
    [InlineData("PLT-114")]
    [InlineData("PLT-118")]
    [InlineData("PLT-125")]
    public void The_plan_of_a_case_of_the_planter_catalog_names_the_rows_and_the_needs_WP_3_5_plants_and_stops_on(string id) => Plans(id);

    /// <summary>
    /// Every pure case the catalog holds is ported here, so the catalog's identifiers survive the port; PLT-120 is ported for its check,
    /// and its computed column, a kind only a plan's statements show, waits on WP 2.1's second slice.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Every_pure_case_of_the_planter_catalog_is_ported()
    {
        var pure = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), "archive/v2/ssdt-agent/facsimile/cases/20-planter.md"))
            .Split("\n### ").Skip(1).Where(c => c.Contains("\nkind:     pure", StringComparison.Ordinal))
            .Select(c => c[..c.IndexOf(" · ", StringComparison.Ordinal)]).ToList();

        Assert.Equal(["PLT-78", "PLT-104", "PLT-120"], pure);
        Assert.All(pure, id => Assert.True(Cases.ContainsKey(id), id + " is not ported"));
    }

    /// <summary>A case plans as it names, and alike from its states given in the opposite order (PLT-104).</summary>
    private static void Plans(string id)
    {
        var c = Cases[id];

        var plan = Plan(c.Schema, c.States, c.Presence, c.Declared);

        Assert.Equal(c.Plan, plan.ToString());
        Assert.Equal(plan, Plan(c.Schema, c.States.Reverse(), c.Presence, c.Declared));
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

    private const string Customer =
        "[Id] omitted, [Name] N'w1', [Email] N'w1', [ContactPhone] N'w1', [Region] N'w1', [AccountId] 1";

    private const string Order = "[Id] omitted, [CustomerId] 1, [StatusId] 1, [Total] 1.00, [StatusText] N'w1'";

    private const string Product = "[Id] omitted, [Code] N'w1', [Name] N'w1', [LegacyCode] N'w1', [CategoryId] 1";

    private static readonly Table Ordering =
        PgCopy.Find(TableName("Order"))!.References("FK_Order_Customer", "Customer", ("CustomerId", "Id"));

    private static readonly Precondition HasOrders = TableEmpty("Order");

    private static readonly Precondition HasCustomers = TableEmpty("Customer");

    private static readonly Precondition HasProducts = TableEmpty("Product");

    private static readonly Precondition TotalPositive = Check("Order", "[Total]>(0)", "Total");

    private static readonly Precondition NarrowCode = Fits("Product.Code", "nvarchar(50)", "nvarchar(10)");

    private static readonly Table NarrowFlag = T("T", "Id int IDENTITY", "Flag char(1)", "Note nvarchar(50) NULL").Key("Id")
        .Unique("UQ_T_Flag", "Flag");

    private static readonly Table Employee =
        T("Employee", "Id int IDENTITY", "ManagerId int").Key("Id").References("FK_Employee_Manager", "Employee", ("ManagerId", "Id"));

    private static readonly Table Shipment = T("Shipment", "OrderId int", "LineNumber int").Key("OrderId", "LineNumber");

    private static readonly Table ShipmentNote = T("ShipmentNote", "Id int IDENTITY", "OrderId int", "LineNumber int").Key("Id");
}
