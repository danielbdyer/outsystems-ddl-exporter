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
    /// PLT-41 beside PLT-94: an integer key of the copy without identity takes a value past the largest the copy holds, a value the plan
    /// does not read, which converts to the type the change gives the column without an error or a loss only where that type holds
    /// every value of the old one, as bigint, decimal(10,0), float, nvarchar(11) and varbinary(4) hold every int. bit holds 0 and 1,
    /// smallint stops at 32767, decimal(9,0) at 999999999, real holds an integer exactly only up to 16777216, nvarchar(10) holds no
    /// -2147483648 and datetime no day past 9999-12-31, so the plan names a need for those where the copy could read violating a Fits the
    /// environment answered clean.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("bit", false)]
    [InlineData("smallint", false)]
    [InlineData("decimal(9,0)", false)]
    [InlineData("real", false)]
    [InlineData("nvarchar(10)", false)]
    [InlineData("datetime", false)]
    [InlineData("bigint", true)]
    [InlineData("decimal(10,0)", true)]
    [InlineData("float", true)]
    [InlineData("nvarchar(11)", true)]
    [InlineData("varbinary(4)", true)]
    public void An_integer_key_the_change_retypes_takes_a_value_past_the_copy_s_largest_only_where_the_new_type_holds_every_old_value(
        string after, bool widens)
    {
        var plan = Plan(Copy(T("T", "Id int IDENTITY", "c int").Key("Id").Unique("UX_T_c", "c")),
            [Rows(TableEmpty("T")), Rows(Fits("T.c", "int", after))]);

        if (widens)
        {
            Assert.Equal("the largest [c] of [dbo].[T] plus 1", Cell(plan, "T", 1, "c"));
            Assert.Empty(plan.Needs);
        }
        else
        {
            Assert.Empty(plan.Tables);
            var need = Assert.Single(plan.Needs);
            Assert.Equal(new Witness.NeedsRow.Reason.NoCleanValue(Part("c"), Model.Type("int"), Model.Type(after), Key: true), need.Why);
            Assert.Equal("[dbo].[T].[c] is a key of the copy, whose planted value lies past the largest the copy holds, a value the plan does"
                + " not read, and " + after + ", the type the change gives it, does not hold every value of int.", need.Message);
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
    /// PLT-125 and PLT-52 where the environment answered the parent empty: a ForeignKey it answered clean finds no orphan among rows
    /// whose parent table holds none, so each row holds NULL in a column of the key, which MATCH SIMPLE passes without a parent row. The
    /// nullable AccountId is NULL and no foreign key is suspended, whether the change adds FK_Customer_Account or the copy holds it: an
    /// orphan there reads violating in the copy, and where the change adds the key, its WITH CHECK fails on the copy (Msg 547) where it
    /// applies in the environment. Of a composite key, the column that admits NULL is NULL and the NOT NULL one takes its literal.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "11′ a planted set repeats itself, replants only the touched tables and holds no orphan its existing data lacks")]
    public void A_ForeignKey_answered_clean_over_a_parent_answered_empty_leaves_its_nullable_columns_NULL_and_suspends_nothing()
    {
        PreconditionState[] states = [Rows(HasCustomers), Rows(AccountOfCustomer), Empty(HasAccounts)];
        var held = PgCopy.With(PgCopy.Find(TableName("Customer"))!.References("FK_Customer_Account", "Account", ("AccountId", "Id")));
        var note = T("ShipmentNote", "Id int IDENTITY", "OrderId int", "LineNumber int NULL").Key("Id");
        var shipment = T("Shipment", "OrderId int", "LineNumber int").Key("OrderId", "LineNumber");

        var added = Plan(PgCopy, states);
        var kept = Plan(held, states);
        var composite = Plan(Copy(shipment, note), [Rows(TableEmpty("ShipmentNote")), Empty(TableEmpty("Shipment")),
            Rows(ForeignKey("ShipmentNote", "Shipment", ("OrderId", "OrderId"), ("LineNumber", "LineNumber")))]);

        Assert.Equal("""
            [dbo].[Customer] at 2
              row 1 (presence): [Id] omitted, [Name] N'w1', [Email] N'w1', [ContactPhone] N'w1', [Region] N'w1', [AccountId] NULL
            """, added.ToString());
        Assert.Equal(added.ToString(), kept.ToString());
        Assert.All(added.Tables.Concat(kept.Tables).SelectMany(t => t.Rows), r => Assert.Empty(r.Suspends));
        Assert.Equal("""
            [dbo].[ShipmentNote] at 1
              row 1 (presence): [Id] omitted, [OrderId] 1, [LineNumber] NULL
            """, composite.ToString());
    }

    /// <summary>
    /// PLT-52 beside UX_Customer_AccountId, a unique index on the nullable AccountId of FK_Customer_Account: a NULL passes the foreign key
    /// without a parent row, but a unique index without a filter holds one NULL at most, so each planted Customer row links to an Account
    /// row of its own; a filtered one, WHERE [AccountId] IS NOT NULL, leaves its NULLs out of the index, so AccountId stays NULL and no
    /// Account row is planted for it. So it goes with three presence rows and with the two rows a duplicate Email asks, and neither plan
    /// names a need.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void A_nullable_foreign_key_column_a_unique_index_holds_links_each_row_to_a_parent_unless_its_filter_leaves_NULLs_out(
        bool filtered, bool presence)
    {
        string[] linked = ["the [Id] of row 1 of [dbo].[Account]", "the [Id] of row 2 of [dbo].[Account]", "the [Id] of row 3 of [dbo].[Account]"];

        var plan = Plan(AccountKeyed(filtered),
            presence ? [] : [Rows(HasCustomers), Violating(Unique("Customer", "Email"), RowClasses.Exact)], presence);

        var customers = plan.Tables.Single(t => t.Table == TableName("Customer")).Rows;
        Assert.Empty(plan.Needs);
        Assert.Equal((filtered ? ["NULL", "NULL", "NULL"] : linked).Take(presence ? 3 : 2), customers.Select(r => Cell(r, "AccountId")));
        Assert.Equal(presence ? 3 : filtered ? 0 : 2, plan.Tables.SingleOrDefault(t => t.Table == TableName("Account"))?.Rows.Count ?? 0);
    }

    /// <summary>
    /// Where the environment answered Account empty, AccountId, which no precondition reads, is NULL in each Customer row (PLT-59): two
    /// rows, as a duplicate Email asks, would hold two NULLs in UX_Customer_AccountId, which SQL Server refuses in a unique index without a
    /// filter (Msg 2601), so the plan names the NULLs as a need, and no room its type lacks; a filtered index leaves them out, and both rows
    /// are planted.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void NULLs_a_unique_index_without_a_filter_would_hold_twice_are_a_need_that_names_them()
    {
        PreconditionState[] states = [Rows(HasCustomers), Violating(Unique("Customer", "Email"), RowClasses.Exact), Empty(HasAccounts)];

        var unfiltered = Plan(AccountKeyed(filtered: false), states);
        var filtered = Plan(AccountKeyed(filtered: true), states);

        Assert.Empty(unfiltered.Tables);
        Assert.Equal("[dbo].[Customer].[AccountId] is a key without a filter, which holds one NULL at most, and the plan plants NULL in it in"
            + " 2 rows.", Assert.Single(unfiltered.Needs).Message);
        Assert.Equal(["NULL", "NULL"], Assert.Single(filtered.Tables).Rows.Select(r => Cell(r, "AccountId")));
        Assert.Empty(filtered.Needs);
    }

    /// <summary>
    /// A deferred reference in a key without a filter: A.BId, nullable, references B and is the one column of UX_A_BId, and B.AId, a NOT
    /// NULL column, references A, so Order defers A's reference to B, and the first pass writes NULL into BId in each of A's three
    /// presence rows before the second sets B's keys. A key without a filter holds one NULL at most, and SQL Server refuses the second row
    /// (Msg 2601), so the plan names the NULLs of the first pass as a need of A.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void NULLs_a_deferred_reference_s_first_pass_writes_into_a_key_without_a_filter_are_a_need_that_names_them()
    {
        var copy = Copy(T("A", "Id int IDENTITY", "BId int NULL").Key("Id").References("FK_A_B", "B", ("BId", "Id")).Unique("UX_A_BId", "BId"),
            T("B", "Id int IDENTITY", "AId int").Key("Id").References("FK_B_A", "A", ("AId", "Id")));

        var plan = Plan(copy, [], presence: true);

        Assert.DoesNotContain(plan.Tables, t => t.Table == TableName("A"));
        Assert.Equal("[dbo].[A].[BId] is a key without a filter, which holds one NULL at most, and the plan plants NULL in it in 3 rows.",
            Assert.Single(plan.Needs).Message);
    }

    /// <summary>
    /// PLT-75 in a key: where the environment answered Account empty, the NOT NULL AccountId of each Customer row holds an orphan past
    /// Account's largest key, row 1's by PLT-75's 1000 and each later row's by one more, since UX_Customer_AccountId holds each value once
    /// and SQL Server refuses the INSERT of a value it holds (Msg 2601); so too where AccountId is nullable and a NotNull answered clean
    /// reads it, where a NULL would read violating.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(false)]
    [InlineData(true)]
    public void Each_row_s_orphan_of_an_integer_leg_lies_past_the_parent_s_largest_key_by_a_step_of_its_own(bool nullable)
    {
        var customer = T("Customer", "Id int IDENTITY", "Email nvarchar(256) NULL", "AccountId int" + (nullable ? " NULL" : "")).Key("Id")
            .References("FK_Customer_Account", "Account", ("AccountId", "Id")).Unique("UX_Customer_AccountId", "AccountId");
        PreconditionState[] states = [Rows(HasCustomers), Violating(Unique("Customer", "Email"), RowClasses.Exact), Empty(HasAccounts)];

        var plan = Plan(PgCopy.With(customer), nullable ? [.. states, Rows(NotNull("Customer.AccountId"))] : states);

        Assert.Empty(plan.Needs);
        Assert.Equal(["the largest [Id] of [dbo].[Account] plus 1000", "the largest [Id] of [dbo].[Account] plus 1001"],
            Assert.Single(plan.Tables).Rows.Select(r => Cell(r, "AccountId")));
    }

    /// <summary>
    /// PLT-57 where the change adds FK_Employee_Manager, which the copy lacks: a ForeignKey the change validates still orders the rows,
    /// since each row links to a parent row, so the NOT NULL self-reference it closes is a need; the need names the copy's foreign keys on
    /// the path, none here, and never the name the plan gives a foreign key only a precondition declares.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_cycle_a_foreign_key_the_change_adds_closes_is_a_need_that_names_no_key_the_copy_lacks()
    {
        var plan = Plan(Copy(T("Employee", "Id int IDENTITY", "ManagerId int").Key("Id")),
            [Rows(TableEmpty("Employee")), Rows(ForeignKey("Employee", "Employee", ("ManagerId", "Id")))]);

        Assert.Empty(plan.Tables);
        Assert.Equal("[dbo].[Employee] references itself through a NOT NULL foreign key ([dbo].[Employee] → [dbo].[Employee]), so no"
            + " order of INSERTs plants a row in it.", Assert.Single(plan.Needs).Message);
    }

    /// <summary>
    /// PLT-42 and PLT-114: a key's need names the room of the values the planter writes into the column, fewer than the three presence
    /// rows: two for bit, and for a decimal(18,2) the change retypes to bit, whose clean values are 1.00 and 0.00; one for an
    /// nvarchar(50) the change narrows to nvarchar(1), whose clean value is w; never the room of a type the values are not written in.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("bit", null, "2 planted values")]
    [InlineData("decimal(18,2)", "bit", "2 planted values")]
    [InlineData("nvarchar(50)", "nvarchar(1)", "one planted value")]
    public void A_key_s_need_names_the_room_of_the_values_the_planter_writes(string type, string? retyped, string room)
    {
        var copy = Copy(T("T", "Id int IDENTITY", "c " + type).Key("Id").Unique("UX_T_c", "c"));

        var plan = Plan(copy, retyped is null ? [] : [Rows(Fits("T.c", type, retyped))], presence: true);

        Assert.Empty(plan.Tables);
        Assert.Equal("[dbo].[T].[c] is a key with room for " + room + ", fewer than the rows the plan plants.",
            Assert.Single(plan.Needs).Message);
    }

    /// <summary>
    /// PLT-60 where the existing data counts the foreign key: a ForeignKey answered populated and clean over a parent answered empty says
    /// that each row holds NULL in a column of the key, which no row can where every column is NOT NULL, or is one a NotNull answered
    /// clean reads, or holds the value of a class another precondition answered. No rows hold those answers, so a count is wrong, as a
    /// row count read from sys.partitions can be, and the plan is the error plan.contradictory-answers, never a copy that reads
    /// otherwise; so too where the declared witnesses plant the table, since no rows they declare hold the answers either.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("a NOT NULL column")]
    [InlineData("a NOT NULL column of a table the declared witnesses plant")]
    [InlineData("a column a NotNull answered clean reads")]
    [InlineData("a column holding the value of a class")]
    public void Answers_no_rows_hold_are_the_error_plan_contradictory_answers(string column)
    {
        var orderOfCustomer = ForeignKey("Order", "Customer", ("CustomerId", "Id"));
        PreconditionState[] orders = [Rows(HasOrders), Rows(orderOfCustomer), Empty(HasCustomers)];
        (Schema Schema, PreconditionState[] States, Precondition Foreign, string Parent) c = column switch
        {
            "a NOT NULL column" or "a NOT NULL column of a table the declared witnesses plant" => (PgCopy.With(Ordering), orders,
                orderOfCustomer, "[dbo].[Customer]"),
            "a column a NotNull answered clean reads" => (PgCopy,
                [Rows(HasCustomers), Rows(AccountOfCustomer), Rows(NotNull("Customer.AccountId")), Empty(HasAccounts)], AccountOfCustomer,
                "[dbo].[Account]"),
            _ => (PgCopy, [Rows(HasCustomers), Rows(AccountOfCustomer), Empty(HasAccounts),
                Violating(Fits("Customer.AccountId", "int", "smallint"), RowClasses.Overflow)], AccountOfCustomer, "[dbo].[Account]"),
        };
        var declared = column.EndsWith("plant", StringComparison.Ordinal) ? new Witness.Declared([TableName("Order")], []) : null;

        var error = Expect.Failed(Witness.Plan(c.Schema, SortedArray.Of(c.States.Select(s => s.Precondition)),
            new ExistingData(SortedArray.Of(c.States)), declared: declared), "plan.contradictory-answers");

        Assert.Contains(c.Foreign + " clean and " + c.Parent + " empty", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ForeignKey whose parent the change creates, which the copy lacks: the count reads each row with no NULL in the key as an orphan,
    /// since the parent holds no row (io/AggregateQueries.Keys.cs). Where the environment answered it clean, the nullable AccountId is
    /// NULL; where it answered it violating, the orphan takes the ordinal literal, there being no parent's key to read past; and where
    /// the column is NOT NULL, as Order.CustomerId, the clean answer is one no rows hold.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_ForeignKey_to_a_parent_the_change_creates_leaves_its_nullable_columns_NULL_and_its_orphan_takes_the_ordinal_literal()
    {
        var change = new Change([New(Model.TableKey("Ledger"), [])], [], [], []);
        var ledgerOfCustomer = ForeignKey("Customer", "Ledger", ("AccountId", "Id"));
        var ledgerOfOrder = ForeignKey("Order", "Ledger", ("CustomerId", "Id"));

        var clean = Plan(PgCopy, [Rows(HasCustomers), Rows(ledgerOfCustomer)], change: change);
        var orphan = Plan(PgCopy, [Rows(HasCustomers), Violating(ledgerOfCustomer)], change: change);
        var unheld = Expect.Failed(Witness.Plan(PgCopy, [HasOrders, ledgerOfOrder],
            new ExistingData([Rows(HasOrders), Rows(ledgerOfOrder)]), change), "plan.contradictory-answers");

        Assert.Equal("NULL", Cell(clean, "Customer", 1, "AccountId"));
        Assert.Equal("1", Cell(orphan, "Customer", 1, "AccountId"));
        Assert.Contains(ledgerOfOrder + " clean, and [dbo].[Ledger], which the change creates, holds no row", unheld.Message,
            StringComparison.Ordinal);
        Assert.All(clean.Tables.Concat(orphan.Tables).SelectMany(t => t.Rows), r => Assert.Empty(r.Suspends));
    }

    /// <summary>
    /// The states ExistingData.Of gives from counts name no class, so a violating Fits or Unique plants the class that fails between its
    /// two types, or an exact duplicate: a code one character past nvarchar(10), the pair of rows equal on it, and a StatusId one past
    /// smallint's range. Without those rows the copy reads clean where the environment's publish blocks.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_violating_Fits_or_Unique_counted_with_no_class_plants_the_class_that_fails_or_an_exact_duplicate()
    {
        var status = Fits("Order.StatusId", "int", "smallint");
        var code = Fits("Product.Code", "nvarchar(50)", "nvarchar(10)");
        PreconditionCount[] counts = [new(HasProducts, 5, 5), new(code, 5, 2), new(Unique("Product", "Code"), 5, 2), new(HasOrders, 4, 4),
            new(status, 4, 1)];

        var plan = Ok(Witness.Plan(PgCopy, SortedArray.Of(counts.Select(c => c.Precondition)), ExistingData.Of(counts)));

        Assert.Equal("""
            [dbo].[Order] at 4
              row 1 (presence; Fits on [dbo].[Order] ([StatusId]) Overflow): [Id] omitted, [CustomerId] 1, [StatusId] 32768, [Total] 1.00, [StatusText] N'w1'
            [dbo].[Product] at 6
              row 1 (presence; Fits on [dbo].[Product] ([Code]) OverLength): [Id] omitted, [Code] N'w1wwwwwwwww', [Name] N'w1', [LegacyCode] N'w1', [CategoryId] 1
              row 2 (presence; Unique on [dbo].[Product] ([Code]) Exact): [Id] omitted, [Code] N'w2', [Name] N'w2', [LegacyCode] N'w2', [CategoryId] 2
              row 3 (presence; Unique on [dbo].[Product] ([Code]) Exact): [Id] omitted, [Code] N'w2', [Name] N'w3', [LegacyCode] N'w3', [CategoryId] 3
            """, plan.ToString());
    }

    /// <summary>
    /// A violating Fits that names no class plants the first class ALTER COLUMN refuses between its two types (io/AggregateQueries.Fits.cs)
    /// that the planter writes a value of: over length into a string or binary type, else not convertible, then overflow, which a date
    /// and time type takes into datetime or smalldatetime, as TRY_CONVERT gives NULL for a value before 1753 or after 2079-06-06. Where the
    /// planter writes none, as for int into varchar, which ALTER COLUMN writes as '*', or datetime2 into date, which never fails, the
    /// need names no class, since the environment reported none.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("nvarchar(50)", "nvarchar(10)", RowClasses.OverLength, "N'w1wwwwwwwww'")]
    [InlineData("nvarchar(20)", "int", RowClasses.NotConvertible, "N'w1'")]
    [InlineData("int", "smallint", RowClasses.Overflow, "32768")]
    [InlineData("datetime2", "datetime", RowClasses.Overflow, "'1700-01-01T00:00:00'")]
    [InlineData("date", "smalldatetime", RowClasses.Overflow, "'2079-06-07'")]
    [InlineData("datetimeoffset", "datetime", RowClasses.Overflow, "'1700-01-01T00:00:00+00:00'")]
    [InlineData("datetime", "smalldatetime", RowClasses.Overflow, "'2079-06-07T00:00:00'")]
    [InlineData("int", "varchar(5)", RowClasses.None, null)]
    [InlineData("datetime2", "date", RowClasses.None, null)]
    public void A_violating_Fits_that_names_no_class_plants_the_first_class_ALTER_COLUMN_refuses_that_the_planter_writes(string before,
        string after, RowClasses @class, string? value)
    {
        var fits = Fits("T.c", before, after);

        var plan = Plan(Copy(T("T", "Id int IDENTITY", "c " + before).Key("Id")), [Rows(TableEmpty("T")), Violating(fits)]);

        if (value is null)
        {
            Assert.Empty(plan.Tables);
            Assert.Equal("The planter writes no value that breaks the precondition into [dbo].[T].[c].", Assert.Single(plan.Needs).Message);
        }
        else
        {
            var row = Assert.Single(Assert.Single(plan.Tables).Rows);
            Assert.Equal(value, Cell(row, "c"));
            Assert.Contains(new Witness.Purpose(fits, @class), row.Serves);
            Assert.Empty(plan.Needs);
        }
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

    private static readonly Precondition HasProducts = TableEmpty("Product");

    private static readonly Precondition HasAccounts = TableEmpty("Account");

    private static readonly Precondition AccountOfCustomer = ForeignKey("Customer", "Account", ("AccountId", "Id"));

    private static readonly Precondition TotalPositive = Check("Order", "[Total]>(0)", "Total");

    private static readonly Table Ordering =
        PgCopy.Find(TableName("Order"))!.References("FK_Order_Customer", "Customer", ("CustomerId", "Id"));

    private static Witness Plan(Schema schema, PreconditionState[] states, bool presence = false, Change? change = null) =>
        PlanterCopies.Plan(schema, states, presence, null, change);

    /// <summary>PgCopy with FK_Customer_Account on the nullable AccountId, and UX_Customer_AccountId on it, filtered WHERE [AccountId] IS
    /// NOT NULL or not.</summary>
    private static Schema AccountKeyed(bool filtered)
    {
        var customer = PgCopy.Find(TableName("Customer"))!.References("FK_Customer_Account", "Account", ("AccountId", "Id"));
        return PgCopy.With(filtered ? customer.Filtered("UX_Customer_AccountId", "([AccountId] IS NOT NULL)", "AccountId")
            : customer.Unique("UX_Customer_AccountId", "AccountId"));
    }

    private static string Cell(Witness plan, string table, int row, string column) =>
        Cell(plan.Tables.Single(t => t.Table == TableName(table)).Rows.Single(r => r.Ordinal == row), column);

    private static string Cell(Witness.Row row, string column) => row.Cells.Single(c => c.Column == Part(column)).Value.ToString();
}
