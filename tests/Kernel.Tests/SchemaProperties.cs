using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// Generated schemas for Schema.Of's properties: one to six tables in dbo or sales, each with an Id column (an identity, by chance) and up
/// to four more of the system types DacFx models, the alias dbo.ShortCode, literal and sequence defaults, persisted and virtual computed
/// columns, sparse and ROWGUIDCOL columns; a primary key over NOT NULL stored columns; unique constraints and unique indexes, some
/// filtered; references to any of the tables, itself included, of one or two columns each, each column its own; a check; a trigger;
/// and, by chance, system versioning with its history table. Constraints are named or unnamed, keyed as io/Ssdt.ReadModel keys them.
/// Every reference is optional exactly when it is disabled or one of its columns admits NULL, as Schema.Of reads it.
/// </summary>
internal static class SchemaProperties
{
    public static readonly Name ShortCode = Ok(Name.Of("dbo", "ShortCode"));

    public static readonly Name NoteNumber = Ok(Name.Of("dbo", "NoteNumber"));

    /// <summary>What dbo.ShortCode stores as: TYPE dbo.ShortCode FROM nvarchar(6) NOT NULL.</summary>
    public static readonly ColumnType ShortCodeStores = SystemType("nvarchar", 6);

    private static readonly ColumnType[] Types =
    [
        SystemType("int"), SystemType("bigint"), SystemType("bit"), SystemType("nvarchar", 50), SystemType("nvarchar", isMax: true),
        SystemType("varchar", 20, collation: "Latin1_General_CS_AS"), SystemType("decimal", precision: 18, scale: 2),
        SystemType("datetime2", scale: 7), SystemType("date"), SystemType("uniqueidentifier"), SystemType("varbinary", isMax: true),
        SystemType("float", precision: 53),
    ];

    private static readonly long[] Seeds = [1, 0, 1000, -5, long.MaxValue];

    private static readonly long[] Increments = [1, -5, 10];

    public static readonly Gen<Schema> Schemas = Gen.Int[0, 999].Array[160].Select(Build);

    /// <summary>A system type as DacFx holds a column's facets.</summary>
    public static ColumnType SystemType(string type, long length = 0, bool isMax = false, long precision = 0, long scale = 0,
        string? collation = null) => new(Ok(Name.Of(type)), length, isMax, precision, scale, collation);

    /// <summary>Draws in order from a fixed array of generated numbers, so a failing sample shrinks to a smaller schema.</summary>
    private sealed class Draw(int[] numbers)
    {
        private int next;

        public int Of(int choices) => numbers[next++ % numbers.Length] % choices;

        public bool OneIn(int n) => Of(n) == 0;

        public T Pick<T>(IReadOnlyList<T> items) => items[Of(items.Count)];
    }

    private static Schema Build(int[] numbers)
    {
        var d = new Draw(numbers);
        var names = Enumerable.Range(0, 1 + d.Of(6)).Select(i => Ok(Name.Of(d.OneIn(3) ? "sales" : "dbo", "T" + i))).ToArray();
        var tables = new List<Table>();
        foreach (var name in names)
        {
            tables.AddRange(Tables(d, name, names));
        }

        return new Schema(SortedArray.Of(tables));
    }

    /// <summary>A table, and its history table when it is system-versioned.</summary>
    private static IEnumerable<Table> Tables(Draw d, Name name, Name[] all)
    {
        var tableKey = Ok(ElementKey.Of("Table", name));
        var used = new HashSet<ElementKey>();
        Name Named(string prefix, int k = 0) => Ok(Name.Of(name.Schema!, prefix + "_" + name.Base + (k == 0 ? "" : "_" + k)));
        ElementKey Under(Name column, string type, string relationship) =>
            Key(Key(tableKey, "Column", column.Base), type, relationship);

        var columns = new List<Column>
        {
            new Column.Stored(1, Ok(Name.Of("Id")), false, SystemType("int"),
                null, d.OneIn(2) ? new Identity(d.Pick(Seeds), d.Pick(Increments)) : null, null, false, false),
        };
        foreach (var k in Enumerable.Range(0, d.Of(5)))
        {
            columns.Add(Extra(d, columns.Count + 1, Ok(Name.Of("c" + k)), Named));
        }

        var references = new List<Reference>();
        foreach (var k in Enumerable.Range(1, d.Of(3)))
        {
            var (legs, disabled) = (1 + d.Of(2), d.OneIn(4));
            var made = Enumerable.Range(0, legs).Select(i => new Column.Stored(columns.Count + 1 + i, Ok(Name.Of("r" + k + "_" + i)),
                d.OneIn(2), SystemType("int"), null, null, null, false, false)).ToArray();
            columns.AddRange(made);
            var named = d.OneIn(2);
            var key = named ? Ok(ElementKey.Of("ForeignKeyConstraint", Named("FK", k)))
                : legs == 1 ? Under(made[0].Name, "ForeignKeyConstraint", "Columns")
                : Key(tableKey, "ForeignKeyConstraint", "Host");
            key = used.Add(key) ? key : Ok(ElementKey.Of("ForeignKeyConstraint", Named("FK", k)));
            references.Add(new Reference(key, key.Parent is null ? key.Name : null, d.Pick(all),
                SortedArray.Of(made.Select((c, i) => new Reference.Leg(i, c.Name, Ok(Name.Of(i == 0 ? "Id" : "c0"))))),
                d.Pick(Actions), d.Pick(Actions), disabled, disabled || made.Any(c => c.Nullable)));
        }

        Versioning? versioning = null;
        var history = Ok(Name.Of(name.Schema!, name.Base + "History"));
        if (d.OneIn(4))
        {
            var (start, end) = (Ok(Name.Of("ValidFrom")), Ok(Name.Of("ValidTo")));
            columns.Add(new Column.Generated(columns.Count + 1, start, false, SystemType("datetime2", scale: 7), GeneratedAs.RowStart));
            columns.Add(new Column.Generated(columns.Count + 1, end, false, SystemType("datetime2", scale: 7), GeneratedAs.RowEnd));
            versioning = new Versioning.Current(history, start, end);
        }

        var stored = columns.OfType<Column.Stored>().ToArray();
        var key0 = d.OneIn(4) ? null : new PrimaryKey(d.OneIn(2) ? Named("PK") : null,
            SortedArray.Of(stored.Where(c => !c.Nullable).Take(1 + d.Of(2)).Select((c, i) => new KeyColumn(i, c.Name, d.OneIn(3)))),
            d.OneIn(2));
        var uniques = Enumerable.Range(1, d.Of(3)).Select(k => UniqueOf(d, k, stored, tableKey, used, Named, Under)).ToArray();
        var checks = new List<Check>();
        if (d.OneIn(2))
        {
            var key = d.OneIn(2) ? Ok(ElementKey.Of("CheckConstraint", Named("CK"))) : Under(columns[0].Name, "CheckConstraint",
                "ExpressionDependencies");
            checks.Add(new Check(key, key.Parent is null ? key.Name : null, new Value.Script("([Id]>(0))"), d.OneIn(4)));
        }

        Trigger[] triggers = d.OneIn(3) ? [new Trigger(Named("trg"), d.OneIn(4), d.OneIn(4))] : [];
        yield return new Table(name, SortedArray.Of(columns), key0, SortedArray.Of(uniques), SortedArray.Of(references),
            SortedArray.Of(checks), SortedArray.Of(triggers), versioning);
        if (versioning is not null)
        {
            yield return new Table(history,
                SortedArray.Of<Column>(
                    new Column.Stored(1, Ok(Name.Of("Id")), false, SystemType("int"), null, null, null, false, false),
                    new Column.Stored(2, Ok(Name.Of("ValidFrom")), false, SystemType("datetime2", scale: 7), null, null, null, false, false),
                    new Column.Stored(3, Ok(Name.Of("ValidTo")), false, SystemType("datetime2", scale: 7), null, null, null, false, false)),
                null, [], [], [], [], new Versioning.History(name));
        }
    }

    private static readonly ReferentialAction[] Actions =
        [ReferentialAction.NoAction, ReferentialAction.Cascade, ReferentialAction.SetNull, ReferentialAction.SetDefault];

    /// <summary>A column after Id: of a system type, of the alias, computed, drawing from a sequence, sparse, or the ROWGUIDCOL.</summary>
    private static Column Extra(Draw d, int ordinal, Name name, Func<string, int, Name> named)
    {
        Default? Literal() => d.OneIn(2) ? null
            : new Default(d.OneIn(2) ? named("DF", ordinal) : null, new Value.Script("((0))"), null);
        return d.Of(7) switch
        {
            0 => new Column.Stored(ordinal, name, d.OneIn(2), ShortCodeStores with { Collation = d.OneIn(2) ? null : "Latin1_General_CS_AS" },
                ShortCode, null, null, false, false),
            1 => new Column.Computed(ordinal, name, d.OneIn(2), new Value.Script("([Id]*(2))"), d.OneIn(2)),
            2 => new Column.Stored(ordinal, name, false, SystemType("int"), null, null,
                new Default(d.OneIn(2) ? named("DF", ordinal) : null, new Value.Script("(NEXT VALUE FOR [dbo].[NoteNumber])"), NoteNumber),
                false, false),
            3 => new Column.Stored(ordinal, name, true, SystemType("int"), null, null, null, true, false),
            4 => new Column.Stored(ordinal, name, false, SystemType("uniqueidentifier"), null, null, Literal(), false, true),
            _ => new Column.Stored(ordinal, name, d.OneIn(2), d.Pick(Types), null, null, Literal(), false, false),
        };
    }

    private static Unique UniqueOf(Draw d, int k, Column.Stored[] stored, ElementKey table, HashSet<ElementKey> used,
        Func<string, int, Name> named, Func<Name, string, string, ElementKey> under)
    {
        var columns = SortedArray.Of(stored.Skip(d.Of(stored.Length)).Take(1 + d.Of(2)).Select((c, i) => new KeyColumn(i, c.Name, d.OneIn(3))));
        var kind = d.Of(3);
        var key = kind switch
        {
            0 => Ok(ElementKey.Of("UniqueConstraint", named("UQ", k))),
            1 => columns.Count == 1 ? under(columns[0].Column, "UniqueConstraint", "Columns") : Key(table, "UniqueConstraint", "Host"),
            _ => Key(table, "Index", "UX_" + table.Name.Base + "_" + k),
        };
        key = used.Add(key) ? key : Ok(ElementKey.Of("UniqueConstraint", named("UQ", k)));
        var index = key.Type == "Index";
        return new Unique(key, key.Parent is null || index ? key.Name : null, columns, index,
            index && d.OneIn(2) ? new Value.Script("([" + columns[0].Column.Base + "] IS NOT NULL)") : null, d.OneIn(5));
    }
}
