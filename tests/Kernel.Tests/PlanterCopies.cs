using System;
using System.Collections.Generic;
using System.Linq;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// The copies the Facsimile's planter cases plant into, written as the cases write them and built as Schema.Of reads a copy: pg, the
/// golden project's tables (cases/README.md §2), each Id an identity but Category's and Status's; and a table written column by column as
/// "Email nvarchar(256) NULL", NOT NULL where NULL is not written, with IDENTITY, SEQUENCE (a default drawing from dbo.OrderNo), SPARSE,
/// a collation, START or END (a period column), or "AS (expression) [PERSISTED]" for a computed column. A table named in one part is in
/// dbo. The states the cases answer are written beside: populated and clean, violating with the classes the environment holds, or
/// answered empty.
/// </summary>
internal static class PlanterCopies
{
    public static readonly Schema PgCopy = Copy(
        T("Account", "Id int IDENTITY", "Name nvarchar(100)", "Region nvarchar(50) NULL").Key("Id"),
        T("Category", "Id int", "Code nvarchar(30)", "IsActive bit").Key("Id"),
        T("Customer", "Id int IDENTITY", "Name nvarchar(100)", "Email nvarchar(256) NULL", "ContactPhone nvarchar(40) NULL",
            "Region nvarchar(50) NULL", "AccountId int NULL").Key("Id"),
        T("CustomerAddress", "Id int IDENTITY", "CustomerId int", "Line1 nvarchar(120)", "City nvarchar(80) NULL",
            "PostalCode nvarchar(20) NULL").Key("Id"),
        T("Order", "Id int IDENTITY", "CustomerId int", "StatusId int", "Total decimal(18,2)", "StatusText nvarchar(20)").Key("Id"),
        T("OrderLine", "Id int IDENTITY", "OrderId int", "LineNumber int", "Amount decimal(18,2)").Key("Id"),
        T("Product", "Id int IDENTITY", "Code nvarchar(50)", "Name nvarchar(200) NULL", "LegacyCode nvarchar(40)",
            "CategoryId int NULL").Key("Id"),
        T("Status", "Id int", "Code nvarchar(20)", "IsActive bit").Key("Id").Unique("UIX_Status_Code", "Code"));

    public static Name Part(string part) => Ok(Name.Of(part));

    /// <summary>A table's name written as Customer, in dbo, or as sales.Order.</summary>
    public static Name TableName(string written) => Model.TableName(written);

    public static Schema Copy(params Table[] tables) => new(SortedArray.Of(tables));

    /// <summary>The schema with each table given put in place of the table of its name, or added.</summary>
    public static Schema With(this Schema schema, params Table[] tables) =>
        new(SortedArray.Of(schema.Tables.Where(t => tables.All(n => n.Name != t.Name)).Concat(tables)));

    /// <summary>A table and its columns, in the order written.</summary>
    public static Table T(string name, params string[] columns) =>
        new(TableName(name), SortedArray.Of(columns.Select((c, i) => Column(i + 1, c))), null, [], [], [], [], null);

    public static Table Key(this Table table, params string[] columns) => table with
    {
        Key = new PrimaryKey(null, SortedArray.Of(columns.Select((c, i) => new KeyColumn(i, Part(c), false))), true),
    };

    /// <summary>A unique index of that name over the columns written.</summary>
    public static Table Unique(this Table table, string name, params string[] columns) => table with
    {
        Uniques = SortedArray.Of(table.Uniques.Append(new Kernel.Unique(Ok(ElementKey.Of(Ok(ElementKey.Of("Table", table.Name)), "Index",
            Part(name))), Part(name), SortedArray.Of(columns.Select((c, i) => new KeyColumn(i, Part(c), false))), true, null, false))),
    };

    /// <summary>A foreign key of that name in the table's schema, to the parent written, each leg a column and the parent's column; optional
    /// where it is disabled or a leg admits NULL, as Schema.Of reads it.</summary>
    public static Table References(this Table table, string name, string parent, params (string Column, string Parent)[] legs) =>
        table.References(name, parent, false, legs);

    public static Table References(this Table table, string name, string parent, bool disabled,
        params (string Column, string Parent)[] legs) => table with
    {
        References = SortedArray.Of(table.References.Append(new Reference(ForeignKey(table.Name.Schema!, name),
            Ok(Name.Of(table.Name.Schema!, name)), TableName(parent),
            SortedArray.Of(legs.Select((l, i) => new Reference.Leg(i, Part(l.Column), Part(l.Parent)))), ReferentialAction.NoAction,
            ReferentialAction.NoAction, disabled, disabled || legs.Any(l => table.Find(Part(l.Column))!.Nullable)))),
    };

    public static ElementKey ForeignKey(string schema, string name) => Ok(ElementKey.Of("ForeignKeyConstraint", Ok(Name.Of(schema, name))));

    /// <summary>A check of that name, its expression as SQL Server keeps it.</summary>
    public static Table Checked(this Table table, string name, string expression) => table with
    {
        Checks = SortedArray.Of(table.Checks.Append(new Kernel.Check(Ok(ElementKey.Of("CheckConstraint",
            Ok(Name.Of(table.Name.Schema!, name)))), Ok(Name.Of(table.Name.Schema!, name)), new Value.Script(expression), false))),
    };

    /// <summary>The table with a default on one column: its expression as SQL Server keeps it.</summary>
    public static Table Defaulted(this Table table, string column, string expression) => table with
    {
        Columns = SortedArray.Of(table.Columns.Select(c => c.Name == Part(column) && c is Column.Stored s
            ? s with { Default = new Default(null, new Value.Script(expression), null) } : c)),
    };

    /// <summary>The current table of system versioning, and its history table of the columns written.</summary>
    public static Table[] Versioned(this Table table, string history, params string[] columns)
    {
        var generated = table.Columns.OfType<Column.Generated>().ToArray();
        var current = table with
        {
            Versioning = new Versioning.Current(TableName(history), generated.Single(g => g.As == GeneratedAs.RowStart).Name,
                generated.Single(g => g.As == GeneratedAs.RowEnd).Name),
        };
        return [current, T(history, columns) with { Versioning = new Versioning.History(table.Name) }];
    }

    /// <summary>A column at its ordinal, written as the class summary says.</summary>
    public static Column Column(int ordinal, string written)
    {
        var words = written.Split(' ');
        var name = Part(words[0]);
        if (words[1] == "AS")
        {
            var persisted = words[^1] == "PERSISTED";
            return new Column.Computed(ordinal, name, true, new Value.Script(string.Join(' ', words[2..(persisted ? ^1 : ^0)])), persisted);
        }

        var flags = words[2..].ToHashSet(StringComparer.Ordinal);
        var nullable = flags.Contains("NULL") && !written.Contains("NOT NULL", StringComparison.Ordinal);
        var collation = flags.FirstOrDefault(f => f.Contains('_', StringComparison.Ordinal));
        var type = Model.Type(words[1] + (collation is null ? "" : " " + collation));
        if (flags.Contains("START") || flags.Contains("END"))
        {
            return new Column.Generated(ordinal, name, false, type, flags.Contains("START") ? GeneratedAs.RowStart : GeneratedAs.RowEnd);
        }

        return new Column.Stored(ordinal, name, nullable, type, null, flags.Contains("IDENTITY") ? new Identity(1, 1) : null,
            flags.Contains("SEQUENCE") ? new Default(null, new Value.Script("(NEXT VALUE FOR [dbo].[OrderNo])"),
                Ok(Name.Of("dbo", "OrderNo"))) : null,
            flags.Contains("SPARSE"), false);
    }

    /// <summary>A precondition the environment answered populated and clean.</summary>
    public static PreconditionState Rows(Precondition precondition) => new(precondition, true, false);

    /// <summary>A precondition the environment answered populated with rows that break it, of the classes given.</summary>
    public static PreconditionState Violating(Precondition precondition, RowClasses classes = RowClasses.None) =>
        new(precondition, true, true) { Classes = classes };

    /// <summary>A precondition whose table holds rows of the classes given, none of which ALTER COLUMN refuses.</summary>
    public static PreconditionState Holding(Precondition precondition, RowClasses classes) =>
        new(precondition, true, false) { Classes = classes };

    /// <summary>A precondition whose table the environment answered empty.</summary>
    public static PreconditionState Empty(Precondition precondition) => new(precondition, false, false);

    /// <summary>The plan for a copy at the states given, or with presence rows alone where none is given an existing data.</summary>
    public static Witness Plan(Schema schema, IEnumerable<PreconditionState> states, bool presence = false,
        Witness.Declared? declared = null, Change? change = null)
    {
        var all = states.ToArray();
        return Ok(Witness.Plan(schema, SortedArray.Of(all.Select(s => s.Precondition)), presence ? null : new ExistingData(SortedArray.Of(all)),
            change, declared));
    }
}
