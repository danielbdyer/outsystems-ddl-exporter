using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnKind = Microsoft.SqlServer.TransactSql.ScriptDom.ColumnType;
using ColumnType = DbChange.Kernel.ColumnType;

namespace DbChange.Io;

/// <summary>
/// The aggregate queries of WP 2.3 (V3_MILESTONES.md §8): one ScriptDom builder per precondition kind, each asking SQL Server how many of a
/// table's existing rows break the precondition's rule rather than re-deriving SQL Server's answer. TableEmpty reads the table's rows, or
/// sys.partitions' for a login holding VIEW DEFINITION alone (§17 item 21); NotNull, the rows holding NULL; Fits, the rows of each value
/// class (AggregateQueries.Fits.cs); ForeignKey, the orphans, and Unique, the rows of each collation class of duplicate (Keys.cs); Check,
/// the rows that make the check false (Check.cs). All of one table's preconditions are items of one SELECT over the table aliased t, a key
/// or a foreign key joining a grouped derived table that matches each row once, so one statement reads the table (decision 2.9). Every
/// query passes AggregateQuery.Of, its text read again, and runs under the session given; no builder writes CONVERT, CAST or PARSE, only
/// TRY_CONVERT and TRY_CAST, and a check's string that SQL Server would convert with no CONVERT written is read through TRY_CONVERT too
/// (Check.cs, Types.cs), so none raises an error that quotes a value (§18). A table's query that fails all the same, on an overflow, is
/// run again a precondition at a time, and each that fails is reported by its precondition and SQL Server's number, the message withheld
/// for a named environment. A precondition no query the allowlist admits can count reads not predictable read-only, with why, as does one
/// whose columns name a collation Collation.Of refuses, since a query writes a COLLATE's name as it stands. A column the change creates
/// reads, where its name would stand, the value the coordinate rules give every existing row, its default or NULL (kernel/Coordinates.cs).
/// </summary>
public static partial class AggregateQueries
{
    private const string NotPredictable = "not predictable read-only: ";

    /// <summary>A value class of Fits or a collation class of Unique: what one item of a table's query counts beside a precondition's
    /// violating rows (the Facsimile's ARCHITECTURE.md §4).</summary>
    public enum Class
    {
        OverLength,
        OverBytes,
        NotConvertible,
        EmptyString,
        Overflow,
        LossyRoundTrip,
        Exact,
        Case,
        Accent,
        TrailingSpace,
        Nulls,
    }

    /// <summary>A class as an answer writes it: over-length, trailing-space and the rest.</summary>
    public static string Word(Class value) => value switch
    {
        Class.OverLength => "over-length",
        Class.OverBytes => "over-bytes",
        Class.NotConvertible => "not-convertible",
        Class.EmptyString => "empty-string",
        Class.Overflow => "overflow",
        Class.LossyRoundTrip => "lossy-round-trip",
        Class.Exact => "exact",
        Class.Case => "case",
        Class.Accent => "accent",
        Class.TrailingSpace => "trailing-space",
        Class.Nulls => "nulls",
        _ => throw new UnreachableException(),
    };

    /// <summary>An item of a table's query: the precondition it counts for, none for the table's rows; and the class it counts, none for
    /// the precondition's violating rows.</summary>
    public sealed record Item(Precondition? Precondition, Class? Class);

    /// <summary>A precondition with no count, and why: not predictable read-only, or the number SQL Server raised running its
    /// query.</summary>
    public sealed record Unanswered(Precondition Precondition, string Why) : IComparable<Unanswered>
    {
        public int CompareTo(Unanswered? other) =>
            other is null ? 1 : Precondition.CompareTo(other.Precondition) is var p and not 0 ? p : string.CompareOrdinal(Why, other.Why);
    }

    /// <summary>The queries of an environment's counted preconditions, one per table in the order of the tables' names; the preconditions
    /// no query counts; and the notes the answer prints: the session's, and each Fits site's losses that no count shows.</summary>
    public sealed record Queries(IReadOnlyList<TableQuery> Tables, SortedArray<Unanswered> NotPredictable, IReadOnlyList<Finding> Notes);

    /// <summary>The rows of one class of a precondition: for Fits the rows of that value class, for Unique the rows of the duplicate groups
    /// of that collation class.</summary>
    public sealed record ClassCount(Precondition Precondition, Class Class, long Rows) : IComparable<ClassCount>
    {
        public int CompareTo(ClassCount? other) =>
            other is null ? 1
            : Precondition.CompareTo(other.Precondition) is var p and not 0 ? p
            : Class.CompareTo(other.Class) is var c and not 0 ? c
            : Rows.CompareTo(other.Rows);
    }

    /// <summary>What measurements answered: each precondition counted, with its table's rows and its violating rows; its classes' rows;
    /// and each precondition left unanswered, with why.</summary>
    public sealed record Counts(SortedArray<PreconditionCount> Counted, SortedArray<ClassCount> Classes,
        SortedArray<Unanswered> Unanswered);

    /// <summary>
    /// One table's aggregate query: the table as the environment names it, the query, the preconditions it counts, each item of its select
    /// list (the table's rows first), and the classes each precondition counts. A query of several preconditions keeps a query for each
    /// alone, which <see cref="Count"/> runs when the table's fails, so a failure is reported by the precondition whose count raised it.
    /// </summary>
    public sealed class TableQuery
    {
        private readonly IReadOnlyList<Part> parts;
        private readonly Func<IReadOnlyList<long?>, Counts> read;

        private TableQuery(Name table, SqlServer.AggregateQuery query, IReadOnlyList<Part> parts, IReadOnlyList<Item> items,
            IReadOnlyList<TableQuery> alone, Func<IReadOnlyList<long?>, Counts> read) =>
            (Table, Query, this.parts, Items, Alone, this.read) = (table, query, parts, items, alone, read);

        public Name Table { get; }

        public SqlServer.AggregateQuery Query { get; }

        public SortedArray<Precondition> Preconditions => SortedArray.Of(parts.Select(p => p.Precondition));

        public IReadOnlyList<Item> Items { get; }

        internal IReadOnlyList<TableQuery> Alone { get; }

        /// <summary>The classes a precondition counts, in their order, each beside its violating rows.</summary>
        public IReadOnlyList<Class> Classes(Precondition precondition) => parts.Single(p => p.Precondition == precondition).Classes;

        internal Counts Read(IReadOnlyList<long?> values) => read(values);

        /// <summary>
        /// The one SELECT over <paramref name="table"/> aliased t that counts every part: COUNT_BIG(*) first, then each part's items, from
        /// the table and each part's joins; and, for more than one part, the query of each alone.
        /// </summary>
        internal static Result<TableQuery> Of(Name table, IReadOnlyList<Part> parts, SqlServer.Session session)
        {
            var from = parts.SelectMany(p => p.Joins).Aggregate((TableReference)Named(table, "t"), (left, join) => new QualifiedJoin
            {
                QualifiedJoinType = QualifiedJoinType.LeftOuter, FirstTableReference = left, SecondTableReference = join.Table,
                SearchCondition = join.On,
            });
            Item[] items = [new(null, null), .. parts.SelectMany(p => p.Items.Select(i => new Item(p.Precondition, i.Class)))];
            var select = Select([CountRows(), .. parts.SelectMany(p => p.Items.Select(i => i.Expression))], from);
            var site = table + ": " + string.Join("; ", parts.Select(p => p.Precondition));
            return SqlServer.AggregateQuery.Of(select, site, session).Bind(query =>
                (parts.Count == 1 ? Result.Ok<IReadOnlyList<TableQuery>>([]) : Result.All(parts.Select(p => Of(table, [p], session))))
                .Map(alone => new TableQuery(table, query, parts, items, alone, values => Reading(parts, values))));
        }

        /// <summary>TableEmpty read from sys.partitions: the partitions the login sees of the table, heap or clustered index, and their
        /// rows.</summary>
        internal static Result<TableQuery> OfPartitions(Name table, Precondition precondition, SqlServer.Session session)
        {
            var from = Join(Join(Named("sys", "partitions", "p"), Named("sys", "objects", "o"), Equal("o", "object_id", "p", "object_id")),
                Named("sys", "schemas", "s"), Equal("s", "schema_id", "o", "schema_id"));
            var where = All([Compare(ColumnRef("s", "name"), BooleanComparisonType.Equals, Unicode(table.Schema ?? "dbo")),
                Compare(ColumnRef("o", "name"), BooleanComparisonType.Equals, Unicode(table.Base)),
                In(ColumnRef("p", "index_id"), Number(0), Number(1))]);
            var select = Select([CountRows(), Call("SUM", ColumnRef("p", "rows"))], from, where);
            return SqlServer.AggregateQuery.Of(select, table + ": TableEmpty from sys.partitions", session).Map(query =>
                new TableQuery(table, query, [Part.Rows(precondition)], [new(null, null), new(precondition, null)], [],
                    values => values[0] is > 0
                        ? new Counts([new PreconditionCount(precondition, values[1] ?? 0, values[1] ?? 0)], [], [])
                        : new Counts([], [], [new(precondition, NotPredictable + "sys.partitions shows this login no partition of " + table
                            + ", and a table it cannot see is never read as empty")])));
        }

        /// <summary>Each part's count from the values its items answered, the table's rows first; a NULL, which SUM gives over no row, is
        /// 0.</summary>
        private static Counts Reading(IReadOnlyList<Part> parts, IReadOnlyList<long?> values)
        {
            var (rows, at, counted, classes) = (values[0] ?? 0, 1, new List<PreconditionCount>(), new List<ClassCount>());
            foreach (var part in parts)
            {
                var (violating, each) = part.Read(rows, [.. values.Skip(at).Take(part.Items.Count).Select(v => v ?? 0)]);
                at += part.Items.Count;
                counted.Add(new PreconditionCount(part.Precondition, rows, violating));
                classes.AddRange(part.Classes.Zip(each, (c, n) => new ClassCount(part.Precondition, c, n)));
            }

            return new Counts(SortedArray.Of(counted), SortedArray.Of(classes), []);
        }
    }

    /// <summary>
    /// What one precondition asks of its table's query: its items, each an expression over the table aliased t and the class it counts,
    /// none for its violating rows; the grouped derived tables it joins; the classes it counts; and how its violating rows and its classes'
    /// rows read from the table's rows and its items' values.
    /// </summary>
    internal sealed record Part(Precondition Precondition, IReadOnlyList<(ScalarExpression Expression, Class? Class)> Items,
        IReadOnlyList<(TableReference Table, BooleanExpression On)> Joins, IReadOnlyList<Class> Classes,
        Func<long, IReadOnlyList<long>, (long Violating, IReadOnlyList<long> Classes)> Read)
    {
        /// <summary>A precondition the table's rows answer: TableEmpty, violated by every row there is.</summary>
        public static Part Rows(Precondition precondition) => new(precondition, [], [], [], (rows, _) => (rows, []));

        /// <summary>A precondition one item counts the violating rows of.</summary>
        public static Part Violating(Precondition precondition, BooleanExpression when,
            params IReadOnlyList<(TableReference Table, BooleanExpression On)> joins) =>
            new(precondition, [(Sum(when), null)], joins, [], (_, values) => (values[0], []));
    }

    /// <summary>A builder's answer: the part a precondition asks of its table's query, or why it reads not predictable read-only.</summary>
    private sealed record Asked(Part? Part, string? Why)
    {
        public static implicit operator Asked(Part part) => new(part, null);

        public static Asked Not(string why) => new(null, why);
    }

    /// <summary>
    /// The queries of an environment's counted preconditions (kernel/Coordinates.cs): <paramref name="after"/> is the model the change
    /// brings, <paramref name="environment"/> the environment's own, <paramref name="database"/> the collation a column that names none
    /// takes, and every query runs under <paramref name="session"/>. A precondition whose count the allowlist refuses alone, or which SQL
    /// Server cannot answer read-only, reads not predictable read-only and leaves the rest of its table counted.
    /// </summary>
    public static Result<Queries> Of(IEnumerable<Coordinates.Counted> counted, SortedArray<Element> after, SortedArray<Element> environment,
        Collation database, SqlServer.Session session)
    {
        var context = new Context(Keyed(after), Keyed(environment), database);
        var (tables, unanswered, notes) = (new List<TableQuery>(), new List<Unanswered>(), new List<Finding> { session.Note });
        foreach (var table in counted.GroupBy(TableOf).OrderBy(t => t.Key))
        {
            var parts = new List<Part>();
            foreach (var one in table.OrderBy(c => c.Precondition))
            {
                var asked = Ask(one, context, notes);
                var why = asked.Part is not { } part ? asked.Why
                    : TableQuery.Of(table.Key, [part], session).Failed(out _, out var refused)
                        ? "the allowlist refuses its count: " + refused.Message : null;
                if (why is not null)
                {
                    unanswered.Add(new(one.Precondition, NotPredictable + why));
                    continue;
                }

                parts.Add(asked.Part!);
            }

            if (parts.Count > 0)
            {
                if (TableQuery.Of(table.Key, parts, session).Failed(out var query, out var error))
                {
                    return error;
                }

                tables.Add(query);
            }
        }

        return new Queries(tables, SortedArray.Of(unanswered), notes);
    }

    /// <summary>TableEmpty read from sys.partitions, for a login that holds VIEW DEFINITION and no SELECT on the table (§17 item
    /// 21).</summary>
    public static Result<TableQuery> Partitions(Coordinates.Counted tableEmpty, SqlServer.Session session) =>
        tableEmpty.Precondition.Kind is PreconditionKind.TableEmpty
            ? TableQuery.OfPartitions(TableOf(tableEmpty), tableEmpty.Precondition, session)
            : throw new ArgumentException(tableEmpty.Precondition + " is no TableEmpty precondition.", nameof(tableEmpty));

    /// <summary>
    /// What a measurement of a table's query answered: each precondition's count and its classes'; or, for a query SQL Server failed or
    /// one past its timeout, each precondition unanswered, by SQL Server's number and its message where the measurement kept it.
    /// </summary>
    public static Counts Read(TableQuery query, SqlServer.Measurement measured) => measured.Match(
        answered => answered.Rows is [var row] && row.Values.Count == query.Items.Count ? query.Read(row.Values)
            : throw new UnreachableException("The query " + query.Query.Site + " answered other than one row of its items."),
        raised => Unanswering(query, "SQL Server raised Msg " + raised.Number.ToString(CultureInfo.InvariantCulture)
            + (raised.Message is null ? "; message withheld" : ": " + raised.Message)),
        timedOut => Unanswering(query, "the query ran past its timeout of "
            + timedOut.After.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s"));

    /// <summary>
    /// A table's query measured on the target and read; when SQL Server fails it, each precondition's query alone, so each failure is the
    /// precondition's whose count raised it and every other is still counted. A connection that fails is the error Measure gives.
    /// </summary>
    public static Result<Counts> Count(SqlServer.Database target, TableQuery query, SqlServer.QueryLog log) =>
        SqlServer.Measure(target, query.Query, log).Bind(measured => measured is SqlServer.Measurement.Raised && query.Alone.Count > 0
            ? Result.All(query.Alone.Select(alone => SqlServer.Measure(target, alone.Query, log).Map(each => Read(alone, each))))
                .Map(each => new Counts(SortedArray.Of(each.SelectMany(c => c.Counted)), SortedArray.Of(each.SelectMany(c => c.Classes)),
                    SortedArray.Of(each.SelectMany(c => c.Unanswered))))
            : Result.Ok(Read(query, measured)));

    private static Counts Unanswering(TableQuery query, string why) =>
        new([], [], SortedArray.Of(query.Preconditions.Select(p => new Unanswered(p, why))));

    /// <summary>The part each kind's builder asks of its table's query; none for a precondition whose columns name a collation
    /// Collation.Of refuses, which no query writes.</summary>
    private static Asked Ask(Coordinates.Counted counted, Context context, List<Finding> notes) =>
        context.Refused(counted) is ({ } where, { } name)
            ? Asked.Not("the model gives " + where + " the collation '" + name + "', which is no SQL Server collation name")
            : counted.Precondition.Kind.Match(
                () => Part.Rows(counted.Precondition),
                () => context.Column(counted, counted.Precondition.Table, counted.Precondition.Columns[0]) is { } column
                    && Operand(column, "t") is { } value
                    ? Part.Violating(counted.Precondition, Is(value, isNull: true)) : Asked.Not(Unplaced(counted.Precondition.Columns[0])),
                fits => Fits(counted, fits, context, notes),
                key => ForeignKey(counted, key, context),
                unique => Unique(counted, unique, context),
                check => Check(counted, check, context));

    /// <summary>The table a counted precondition reads, as the environment names it.</summary>
    private static Name TableOf(Coordinates.Counted counted) =>
        counted.Places.First(p => p.After.Parent is null && p.After.Name == counted.Precondition.Table).Environment!.Name;

    private static string Unplaced(Name column) => "it reads " + column + ", which the coordinate rules place nowhere";

    /// <summary>
    /// A column a precondition reads: where the environment holds it, or for a column the change creates the value every existing row
    /// holds; its type after the change and as the environment holds it, each null where the model gives none, as for a computed column;
    /// and whether the environment's allows NULL, which it does where the model does not say.
    /// </summary>
    private sealed record Column(Place Place, ColumnType? After, ColumnType? Before, bool Nullable)
    {
        public string? Name => Place.Environment?.Name.Base;
    }

    /// <summary>What the builders read beside a precondition: both models by key, the collation a column that names none takes, and the
    /// aliases already given to derived tables.</summary>
    private sealed class Context(IReadOnlyDictionary<ElementKey, Element> after, IReadOnlyDictionary<ElementKey, Element> environment,
        Collation database)
    {
        private int aliases;

        public Collation Database => database;

        /// <summary>A derived table's alias no other of its table's query takes.</summary>
        public string Alias(string letter) => letter + (++aliases).ToString(CultureInfo.InvariantCulture);

        /// <summary>The column <paramref name="name"/> of <paramref name="table"/>, as the precondition's places give it; null where they
        /// give none.</summary>
        public Column? Column(Coordinates.Counted counted, Name table, Name name) =>
            counted.Places.FirstOrDefault(p => p.After.Type == "Column" && p.After.Parent?.Name == table && p.After.Name == name)
                is { } place
                ? new Column(place, Stored(after, place.After), place.Environment is { } at ? Stored(environment, at) : null,
                    place.Environment is not { } held
                    || environment.GetValueOrDefault(held)?["Nullable"] is not Value.Boolean { IsTrue: false })
                : null;

        /// <summary>A type as SQL Server stores it, an alias read as its base type, in the model that holds its column.</summary>
        public ColumnType? Stored(ColumnType type, bool inHead) =>
            type.Stored(inHead ? after : environment) is Result<ColumnType>.Ok stored ? stored.Value : null;

        /// <summary>The collation a type's values compare under: its own, or the database's. Ask has read every collation a precondition's
        /// columns name through <see cref="Refused"/>, so each is one Collation.Of admits.</summary>
        public Kernel.Collation Collation(ColumnType? type) => type?.Collation is not { } name ? database
            : Kernel.Collation.Of(name) is Result<Kernel.Collation>.Ok admitted ? admitted.Value
            : throw new UnreachableException("Ask counts no precondition whose columns name a collation Collation.Of refuses.");

        /// <summary>
        /// The first collation a precondition's columns name, in either model, or its own types do, that Collation.Of refuses, since a
        /// query writes a COLLATE's name as it stands: the column's path, or the precondition, and the name; null when there is none.
        /// </summary>
        public (string Where, string Name)? Refused(Coordinates.Counted counted)
        {
            IEnumerable<(string Where, ColumnType? Type)> Named()
            {
                foreach (var place in counted.Places.Where(p => p.After.Type == "Column"))
                {
                    yield return (place.After.Path, Stored(after, place.After));
                    yield return (place.After.Path, place.Environment is { } held ? Stored(environment, held) : null);
                }

                if (counted.Precondition.Kind is PreconditionKind.Fits fits)
                {
                    yield return (counted.Precondition.ToString(), fits.Before);
                    yield return (counted.Precondition.ToString(), fits.After);
                }
            }

            return Named().FirstOrDefault(n => n.Type?.Collation is { } name
                    && Kernel.Collation.Of(name) is Result<Kernel.Collation>.Failed) is ({ } where, { Collation: { } refused })
                ? (where, refused) : null;
        }

        private static ColumnType? Stored(IReadOnlyDictionary<ElementKey, Element> model, ElementKey key) =>
            model.GetValueOrDefault(key) is { } column && ColumnType.Of(column).Bind(t => t.Stored(model)) is Result<ColumnType>.Ok type
                ? type.Value : null;
    }

    /// <summary>
    /// Where a column stands in a query over its table aliased <paramref name="alias"/>: the column as the environment names it; or, for a
    /// column the change creates, NULL, or its default converted to its type by TRY_CONVERT as SQL Server stores it; null for a default
    /// that does not parse.
    /// </summary>
    private static ScalarExpression? Operand(Column column, string? alias) =>
        column.Name is { } name ? ColumnRef(alias, name)
        : column.Place.Default is not { } text ? new NullLiteral()
        : TSql.Scalar(text) is not { } value ? null
        : column.After is { } type && TSql.DataType(Written(type)) is { } declared
            ? new TryConvertCall { DataType = declared, Parameter = value }
        : value;

    /// <summary>
    /// A column's type as a declaration writes it: nvarchar(10), varchar(max), decimal(18,2), datetime2(7), float(53), and an alias by its
    /// two-part name.
    /// </summary>
    internal static string Written(ColumnType type) => type.IsAlias ? type.Type.ToString() : type.Type.Base.ToLowerInvariant() switch
    {
        "char" or "varchar" or "nchar" or "nvarchar" or "binary" or "varbinary" =>
            type.Type.Base + "(" + (type.IsMax ? "max" : Digits(type.Length)) + ")",
        "decimal" or "numeric" => type.Type.Base + "(" + Digits(type.Precision) + "," + Digits(type.Scale) + ")",
        "datetime2" or "time" or "datetimeoffset" => type.Type.Base + "(" + Digits(type.Scale) + ")",
        "float" when type.Precision > 0 => "float(" + Digits(type.Precision) + ")",
        _ => type.Type.Base,
    };

    private static string Digits(long n) => n.ToString(CultureInfo.InvariantCulture);

    private static Dictionary<ElementKey, Element> Keyed(SortedArray<Element> model)
    {
        var keyed = new Dictionary<ElementKey, Element>();
        foreach (var e in model)
        {
            keyed.TryAdd(e.Key, e);
        }

        return keyed;
    }
}
