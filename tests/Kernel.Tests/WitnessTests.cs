using System;
using System.Linq;
using DbChange.Tests;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;
using static DbChange.Kernel.Tests.PlanterCopies;
using static DbChange.Kernel.Tests.QuestionCases;

namespace DbChange.Kernel.Tests;

/// <summary>
/// Witness.Plan (V3_MILESTONES.md WP 3.8) where the planter catalog's text form says less than its case: a literal past its type's range
/// or width, presence rows without existing data, a precondition the change renames or creates, the errors, and what a need says. The
/// PLT cases are archive/v2/ssdt-agent/facsimile/cases/20-planter.md's.
/// </summary>
public sealed class WitnessTests
{
    /// <summary>PLT-02, PLT-03, PLT-04, PLT-07, PLT-08, PLT-12, PLT-17 to PLT-19, PLT-24 and PLT-25 at an ordinal the plan's few rows never
    /// reach: tinyint wraps at 256, a time at a day, a string falls back to base 36 and then to its first characters.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("PLT-02", "tinyint", 256, "0")]
    [InlineData("PLT-03", "bit", 2, "0")]
    [InlineData("PLT-04", "decimal(1,0)", 10, "0")]
    [InlineData("PLT-04", "decimal(3,1)", 105, "5.0")]
    [InlineData("PLT-07", "date", 31, "'2000-02-01'")]
    [InlineData("PLT-08", "time(0)", 86_400, "'00:00:00'")]
    [InlineData("PLT-12", "nvarchar(3)", 100, "N'w2s'")]
    [InlineData("PLT-12", "nvarchar(2)", 100, "N'w1'")]
    [InlineData("PLT-12", "varchar(max)", 100, "'w100'")]
    [InlineData("PLT-17", "binary(1)", 256, "0x00")]
    [InlineData("PLT-18", "varbinary(2)", 256, "0x0100")]
    [InlineData("PLT-18", "varbinary(3)", 256, "0x770100")]
    [InlineData("PLT-19", "uniqueidentifier", 255, "'77777777-0000-4000-8000-0000000000FF'")]
    [InlineData("PLT-24", "hierarchyid", 3, "'/3/'")]
    [InlineData("PLT-25", "geography", 181, "'POINT(1 0)'")]
    public void A_literal_is_its_ordinal_wrapped_to_its_type_s_range_and_fitted_to_its_width(string id, string type, long ordinal,
        string literal) => Assert.True(Witness.Literal(Model.Type(type), ordinal)?.ToString() == literal, id);

    /// <summary>PLT-28: a type the planter writes no literal for, NOT NULL, is a need; nullable, it is left NULL (PLT-22).</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_NOT_NULL_column_of_a_type_the_planter_writes_no_literal_for_is_a_need_and_a_nullable_one_is_NULL()
    {
        var polygon = new ColumnType(Ok(Name.Of("Polygon")), 0, false, 0, 0, null);
        Table Shaped(bool nullable) => T("T", "Id int IDENTITY") with
        {
            Columns = [Column(1, "Id int IDENTITY"), new Column.Stored(2, Part("Shape"), nullable, polygon, null, null, null, false, false)],
        };

        var notNull = Plan(Copy(Shaped(nullable: false)), [Rows(TableEmpty("T"))]);
        var nullable = Plan(Copy(Shaped(nullable: true)), [Rows(TableEmpty("T"))]);

        Assert.Null(Witness.Literal(polygon, 1));
        Assert.Empty(notNull.Tables);
        Assert.Equal(new Witness.NeedsRow.Reason.NoLiteral(Part("Shape"), polygon), Assert.Single(notNull.Needs).Why);
        Assert.Equal("[dbo].[T] at 0\n  row 1 (presence): [Id] omitted, [Shape] NULL", nullable.ToString());
    }

    /// <summary>PLT-108: with no existing data every table takes three rows of ordinals 1 to 3, a lookup's among them, which WP 3.5
    /// leaves to its seeded rows where it holds any (PLT-111).</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Without_existing_data_every_table_takes_three_presence_rows()
    {
        var plan = Plan(PgCopy, [], presence: true);

        Assert.Equal(PgCopy.Tables.Select(t => t.Name), plan.Tables.Select(p => p.Table));
        Assert.All(plan.Tables, t => Assert.Equal([1, 2, 3], t.Rows.Select(r => r.Ordinal)));
        Assert.All(plan.Tables.SelectMany(t => t.Rows), r => Assert.Equal([new Witness.Purpose(null, RowClasses.None)], r.Serves));
        Assert.Equal("N'w3'", Cell(plan, "Customer", 3, "Name"));
        Assert.Equal("0", Cell(plan, "Category", 2, "IsActive"));
        Assert.Equal("the largest [Id] of [dbo].[Status] plus 3", Cell(plan, "Status", 3, "Id"));
        Assert.Empty(plan.Needs);
    }

    /// <summary>PLT-109: without existing data the rows of a child spread over its parent's, row n to the parent's row n, and a nullable
    /// foreign key is left NULL.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Without_existing_data_a_child_s_row_links_to_its_parent_s_row_of_its_ordinal_and_a_nullable_reference_is_NULL()
    {
        var copy = PgCopy.With(PgCopy.Find(TableName("Order"))!.References("FK_Order_Customer", "Customer", ("CustomerId", "Id"))
                .References("FK_Order_Status", "Status", ("StatusId", "Id")),
            PgCopy.Find(TableName("OrderLine"))!.References("FK_OrderLine_Order", "Order", ("OrderId", "Id")),
            PgCopy.Find(TableName("Product"))!.References("FK_Product_Category", "Category", ("CategoryId", "Id")));

        var plan = Plan(copy, [], presence: true);

        Assert.Equal("the [Id] of row 2 of [dbo].[Customer]", Cell(plan, "Order", 2, "CustomerId"));
        Assert.Equal("the [Id] of row 2 of [dbo].[Status]", Cell(plan, "Order", 2, "StatusId"));
        Assert.Equal("the [Id] of row 3 of [dbo].[Order]", Cell(plan, "OrderLine", 3, "OrderId"));
        Assert.Equal("NULL", Cell(plan, "Product", 1, "CategoryId"));
        Assert.True(plan.Tables.Select(t => t.Table).ToList() is var placed
            && placed.IndexOf(TableName("Status")) < placed.IndexOf(TableName("Order")));
    }

    /// <summary>PLT-103: a column added changes no other column's values, and takes the literal of its own type at each row's
    /// ordinal.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_column_added_takes_its_own_literal_and_changes_no_other_column_s_value()
    {
        PreconditionState[] states = [Rows(TableEmpty("Customer")), Violating(NotNull("Customer.Email"))];
        var customer = PgCopy.Find(TableName("Customer"))!;
        var added = customer with { Columns = SortedArray.Of(customer.Columns.Append(Column(7, "Nickname nvarchar(30) NULL"))) };

        var before = Plan(PgCopy, states).Tables.Single().Rows.Single();
        var after = Plan(PgCopy.With(added), states).Tables.Single().Rows.Single();

        Assert.Equal(before.Cells, after.Cells.Where(c => c.Column != Part("Nickname")));
        Assert.Equal("N'w1'", after.Cells.Single(c => c.Column == Part("Nickname")).Value.ToString());
    }

    /// <summary>PLT-105: a column widened, retyped, dropped or moved changes only that column's values: each value is keyed by the
    /// column's name and the row's ordinal, never by the column's place.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Region nvarchar(80) NULL", "N'w1'")]
    [InlineData("Region int NULL", "1")]
    [InlineData(null, null)]
    [InlineData("moved", "N'w1'")]
    public void A_column_retyped_dropped_or_moved_changes_only_its_own_value(string? region, string? value)
    {
        var customer = PgCopy.Find(TableName("Customer"))!;
        var columns = customer.Columns.Where(c => c.Name != Part("Region")).ToList();
        if (region == "moved")
        {
            columns.Insert(1, Column(0, "Region nvarchar(50) NULL"));
        }
        else if (region is not null)
        {
            columns.Add(Column(0, region));
        }

        var edited = customer with { Columns = SortedArray.Of(columns.Select((c, i) => c with { Ordinal = i + 1 })) };
        var before = Plan(PgCopy, [Rows(HasCustomers)]).Tables.Single().Rows.Single().Cells;
        var after = Plan(PgCopy.With(edited), [Rows(HasCustomers)]).Tables.Single().Rows.Single().Cells;

        Assert.Equal(before.Where(c => c.Column != Part("Region")).Select(c => (c.Column, c.Value)),
            after.Where(c => c.Column != Part("Region")).Select(c => (c.Column, c.Value)).OrderBy(c => before.Select(b => b.Column)
                .ToList().IndexOf(c.Column)));
        Assert.Equal(value, after.FirstOrDefault(c => c.Column == Part("Region"))?.Value.ToString());
    }

    /// <summary>
    /// PLT-94 and PLT-95 past the catalog's types: a column the change retypes, answered clean, takes a value that converts to the new
    /// type without an error or a loss, or the plan names a need, so the copy never breaks where the environment holds. A date converts
    /// to ten characters, a uniqueidentifier to 36 and decimal(5,2)'s 1.00 to four; SQL Server converts no date to time and no int to
    /// date; a string bound for hierarchyid holds a path.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("date", "varchar(10)", "'2000-01-02'")]
    [InlineData("date", "varchar(8)", null)]
    [InlineData("uniqueidentifier", "nvarchar(20)", null)]
    [InlineData("decimal(5,2)", "varchar(4)", "1.00")]
    [InlineData("decimal(5,2)", "varchar(3)", null)]
    [InlineData("date", "time", null)]
    [InlineData("int", "date", null)]
    [InlineData("int", "datetime", "1")]
    [InlineData("datetime", "date", "'2000-01-02T00:00:00'")]
    [InlineData("nvarchar(20)", "hierarchyid", "N'/1/'")]
    [InlineData("int", "uniqueidentifier", null)]
    [InlineData("int", "sql_variant", "1")]
    public void A_column_the_change_retypes_takes_a_value_of_no_class_or_names_a_need(string before, string after, string? value)
    {
        var plan = Plan(Copy(T("T", "Id int IDENTITY", "c " + before).Key("Id")), [Rows(Fits("T.c", before, after))]);

        if (value is null)
        {
            Assert.Empty(plan.Tables);
            Assert.Equal(new Witness.NeedsRow.Reason.NoCleanValue(Part("c"), Model.Type(before), Model.Type(after)),
                Assert.Single(plan.Needs).Why);
        }
        else
        {
            Assert.Equal(value, Cell(plan, "T", 1, "c"));
            Assert.Empty(plan.Needs);
        }
    }

    /// <summary>
    /// PLT-68 and PLT-72 in a column of a foreign key the row meets (PLT-49): no value of a class is a key of the parent, so the row would
    /// be an orphan its existing data lacks, which SQL Server refuses under the copy's enabled foreign key (Msg 547), and the plan names a
    /// need for the table, for a misfit and for a duplicate alike. Where the environment answered the parent empty, the environment's own
    /// rows hold orphans in that column, so the value stands and the foreign key is suspended to plant it (PLT-59).
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_value_of_a_class_in_a_column_of_a_foreign_key_the_row_meets_is_a_need_unless_the_parent_was_answered_empty()
    {
        var overflow = Violating(Fits("Order.CustomerId", "int", "smallint"), RowClasses.Overflow);
        var coded = Copy(T("P", "Id int IDENTITY", "Code nvarchar(20)").Key("Id").Unique("UX_P_Code", "Code"),
            T("C", "Id int IDENTITY", "PCode nvarchar(20)").Key("Id").References("FK_C_P", "P", ("PCode", "Code")));

        var misfit = Plan(PgCopy.With(Ordering), [Rows(HasOrders), overflow]);
        var duplicate = Plan(coded, [Rows(TableEmpty("C")), Violating(Unique("C", "PCode"), RowClasses.TrailingSpace)]);
        var orphan = Plan(PgCopy.With(Ordering), [Rows(HasOrders), overflow, Empty(HasCustomers)]);

        Assert.DoesNotContain(misfit.Tables, t => t.Table == TableName("Order"));
        Assert.Equal("The planter writes no value of the class Overflow into [dbo].[Order].[CustomerId] that is also a key of"
            + " [dbo].[Customer], the table the column references.", Assert.Single(misfit.Needs).Message);
        Assert.Equal(new Witness.NeedsRow.Reason.NoClassValue(Part("PCode"), RowClasses.TrailingSpace, TableName("P")),
            Assert.Single(duplicate.Needs).Why);
        Assert.Equal("[dbo].[Order] at 4\n  row 1 (presence; Fits on [dbo].[Order] ([CustomerId]) Overflow): [Id] omitted, [CustomerId] 32768,"
            + " [StatusId] 1, [Total] 1.00, [StatusText] N'w1'; suspends ForeignKeyConstraint [dbo].[FK_Order_Customer]", orphan.ToString());
    }

    /// <summary>
    /// The change places each precondition where the copy holds it (the coordinate rules of WP 2.1): a column it renames under the name
    /// it had (the Facsimile's S3), so the NULL a NotNull answered violating asks lands in ContactPhone; a column it creates nowhere, so
    /// a duplicate over one is two rows equal on nothing the copy holds; and a table it creates holds no row.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_change_places_a_precondition_on_a_renamed_column_under_its_old_name_and_one_on_a_created_object_nowhere()
    {
        var head = PgModel.RenameColumn("Customer.ContactPhone", "MobileNumber").With("Customer.MobileNumber nvarchar(40) NOT NULL",
            "Customer.ExternalRef nvarchar(40) NULL", "Ledger.Id int NOT NULL");
        var change = Ok(Change.Between(PgModel.Elements, head.Elements, [ContactPhoneRenamed]));
        PreconditionState[] states = [Violating(NotNull("Customer.MobileNumber")),
            Violating(Unique("Customer", "ExternalRef"), RowClasses.Exact), Rows(TableEmpty("Ledger"))];

        var plan = Plan(PgCopy, states, change: change);

        var rows = Assert.Single(plan.Tables).Rows;
        Assert.Equal(["NULL", "N'w2'"], rows.Select(r => Cell(r, "ContactPhone")));
        Assert.Equal([RowClasses.None, RowClasses.Exact], rows[0].Serves.Where(s => s.Precondition is not null).Select(s => s.Class));
        Assert.Equal([RowClasses.Exact], rows[1].Serves.Where(s => s.Precondition is not null).Select(s => s.Class));
        Assert.DoesNotContain(rows.SelectMany(r => r.Cells), c => c.Column == Part("ExternalRef") || c.Column == Part("MobileNumber"));
    }

    /// <summary>A precondition on a table the copy does not hold and the change does not create is plan.missing-object, and one the
    /// existing data holds no state for is plan.uncounted: neither is ever planted as empty (the Facsimile's S3).</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_precondition_on_an_object_the_copy_lacks_or_one_never_counted_is_an_error()
    {
        var absent = TableEmpty("Ledger");

        var missing = Expect.Failed(Witness.Plan(PgCopy, [absent], new ExistingData([Rows(absent)])), "plan.missing-object");
        var uncounted = Expect.Failed(Witness.Plan(PgCopy, [HasCustomers], new ExistingData([])), "plan.uncounted");

        Assert.Contains("Table [dbo].[Ledger]", missing.Message, StringComparison.Ordinal);
        Assert.Contains("TableEmpty on [dbo].[Customer]", uncounted.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Plan(PgCopy, [Rows(absent)], presence: true, change: new Change(
            [New(Model.TableKey("Ledger"), [])], [], [], [])).Tables, t => t.Table == TableName("Ledger"));
    }

    /// <summary>
    /// PLT-78 beside PLT-61: a check the environment answered violating is a need for one declared row, and the plan keeps the table's
    /// other rows, so the NULL a NotNull answered violating asks is planted beside the need; without it the copy would read the NotNull
    /// clean where the environment's publish blocks.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_check_answered_violating_is_a_need_beside_the_NULL_row_a_NotNull_answered_violating_asks()
    {
        PreconditionState[] states = [Rows(HasCustomers), Violating(Check("Customer", "len([Name])>(0)", "Name")),
            Violating(NotNull("Customer.Email"))];

        var plan = Plan(PgCopy, states);

        Assert.Equal("""
            [dbo].[Customer] at 2
              row 1 (presence; NotNull on [dbo].[Customer] ([Email])): [Id] omitted, [Name] N'w1', [Email] NULL, [ContactPhone] N'w1', [Region] N'w1', [AccountId] 1
            needs a declared row of [dbo].[Customer]: [dbo].[Customer] holds rows in the environment that make the check len([Name])>(0) false, and the planter writes no literal to make a check false, since a check's expression is free-form.
            """, plan.ToString());
        Assert.Equal(plan, Plan(PgCopy, [.. states.Reverse()]));
    }

    /// <summary>
    /// PLT-44 and PLT-116: a need names its table and why, and says what to declare; a check that refuses a planted row, which WP 3.5
    /// meets at the INSERT, names the check; a check the environment answered violating is answered by one declared row, and every
    /// other reason by the table's rows declared whole.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_need_names_its_table_and_what_a_person_declares()
    {
        var check = new Check(Key("CheckConstraint", "dbo", "CK_Product_Code"), Ok(Name.Of("dbo", "CK_Product_Code")),
            new Value.Script("([Code] like 'ABC%')"), false);
        var rejects = new Witness.NeedsRow(TableName("Product"), new Witness.NeedsRow.Reason.Rejects(check));
        var falseCheck = Assert.Single(Plan(PgCopy, [Violating(TotalPositive)]).Needs);

        Assert.Equal("[dbo].[Product] rejects the planted literals: SQL Server refused the planted row by the check"
            + " [dbo].[CK_Product_Code].", rejects.Message);
        Assert.Equal("Declare the rows of [dbo].[Product] in the change's declared witnesses, which then plant the table whole.",
            rejects.Remedy);
        Assert.True(rejects.Whole);
        Assert.False(falseCheck.Whole);
        Assert.Equal("Declare one row of [dbo].[Order] that makes the check false, as a witness of Check on [dbo].[Order] ([Total]).",
            falseCheck.Remedy);
    }

    private static readonly Model PgModel = QuestionCases.Pg;

    private static readonly Precondition HasCustomers = TableEmpty("Customer");

    private static readonly Precondition HasOrders = TableEmpty("Order");

    private static readonly Precondition TotalPositive = Check("Order", "[Total]>(0)", "Total");

    private static readonly Table Ordering =
        PgCopy.Find(TableName("Order"))!.References("FK_Order_Customer", "Customer", ("CustomerId", "Id"));

    private static Witness Plan(Schema schema, PreconditionState[] states, bool presence = false, Change? change = null) =>
        PlanterCopies.Plan(schema, states, presence, null, change);

    private static string Cell(Witness plan, string table, int row, string column) =>
        Cell(plan.Tables.Single(t => t.Table == TableName(table)).Rows.Single(r => r.Ordinal == row), column);

    private static string Cell(Witness.Row row, string column) => row.Cells.Single(c => c.Column == Part(column)).Value.ToString();
}
