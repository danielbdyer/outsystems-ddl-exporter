using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChange.Io;

public static partial class SqlServer
{
    /// <summary>
    /// An aggregate query the allowlist admitted (VALUES.md P2): one statement, as ScriptDom writes it back, so what runs is what was
    /// checked, with no comment and no batch separator; the site it measures; and the tables it reads by name. Only Of makes one, and Of
    /// is internal to io (DECISIONS.md, 2026-09-25), so only io's builders (WP 2.3) make one for a named environment and no verb or
    /// file hands one text; Measure runs nothing else.
    /// </summary>
    public sealed class AggregateQuery
    {
        private AggregateQuery(TSqlStatement statement, string site) =>
            (Statement, Site, Tables) = (TSql.Text(statement), site, TSql.TablesNamed(statement));

        public string Statement { get; }

        public string Site { get; }

        /// <summary>
        /// Each table the query reads by name, as QUOTENAME writes it: what Measure asks the target whether a synonym stands for.
        /// </summary>
        internal IReadOnlyList<string> Tables { get; }

        internal static Result<AggregateQuery> Of(string text, string site) =>
            Allowlist.Admitted(text).Map(statement => new AggregateQuery(statement, site));

        /// <summary>
        /// An aggregate query built as a ScriptDom tree, checked as the tree, and run as the text ScriptDom writes it back as.
        /// </summary>
        internal static Result<AggregateQuery> Of(TSqlStatement tree, string site) =>
            Allowlist.Admitted(tree, site).Map(statement => new AggregateQuery(statement, site));

        public override string ToString() => Site + ": " + Statement;
    }

    /// <summary>
    /// What an aggregate query measured, a value: its rows, every value an integer or null, in the order of their values, so two
    /// measurements of the same rows are equal whatever order SQL Server returned them in; the error SQL Server raised running it, by its
    /// number, SQL Server's message kept for a copy alone; or its timeout. The cases are closed, and Match reads each.
    /// </summary>
    public abstract record Measurement
    {
        private Measurement()
        {
        }

        public T Match<T>(Func<Answered, T> answered, Func<Raised, T> raised, Func<TimedOut, T> timedOut) => this switch
        {
            Answered a => answered(a),
            Raised r => raised(r),
            TimedOut t => timedOut(t),
            _ => throw new System.Diagnostics.UnreachableException(),
        };

        public sealed record Answered(string Site, SortedArray<Row> Rows) : Measurement;

        /// <summary>
        /// SQL Server raised an error running the query (Msg 245, a conversion that failed): a measurement of its own, which the caller
        /// keeps as the site's outcome.
        /// </summary>
        public sealed record Raised(string Site, int Number, string? Message) : Measurement
        {
            public override string ToString() =>
                Site + ": SQL Server raised Msg " + Number.ToString(CultureInfo.InvariantCulture)
                    + (Message is null ? "; message withheld" : ": " + Message);
        }

        /// <summary>
        /// SQL Server still running the query when its timeout passed, on a connection that stayed open: the server answered, and the
        /// measurement is missing, not failed.
        /// </summary>
        public sealed record TimedOut(string Site, TimeSpan After) : Measurement
        {
            public override string ToString() =>
                Site + ": query ran past its timeout of " + After.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s";
        }
    }

    /// <summary>
    /// One row an aggregate query answered: the values of its select list, in order, each an integer or null. Two rows are equal when
    /// their values are, one by one; rows order by their values, a null before any integer and a shorter row before a longer one it begins.
    /// </summary>
    public sealed class Row : IEquatable<Row>, IComparable<Row>
    {
        private readonly long?[] values;

        private Row(long?[] values) => this.values = values;

        public IReadOnlyList<long?> Values => values;

        public static Row Of(params long?[] values) => new([.. values]);

        public bool Equals(Row? other) => other is not null && values.AsSpan().SequenceEqual(other.values);

        public override bool Equals(object? obj) => Equals(obj as Row);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var value in values)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }

        public int CompareTo(Row? other) => other is null ? 1
            : values.Zip(other.values, Nullable.Compare).FirstOrDefault(c => c != 0) is var byValue and not 0 ? byValue
                : values.Length.CompareTo(other.values.Length);

        public override string ToString() => string.Join(", ", values.Select(v => v?.ToString(CultureInfo.InvariantCulture) ?? "NULL"));
    }

    /// <summary>
    /// One admitted aggregate query against the target (WP 1.4), through the one statement path, read back as integers and logged
    /// with its row count. A statement SQL Server refuses on the open connection is measured as failed, by its number, and one past
    /// its timeout as timed out; a connection that fails, or an identity refused, is an error.
    /// </summary>
    public static Result<Measurement> Measure(Database target, AggregateQuery query, QueryLog log) =>
        Measure(target, query, log, AggregateQueryTimeout);

    internal static Result<Measurement> Measure(Database target, AggregateQuery query, QueryLog log, TimeSpan timeout) =>
        NoSynonym(target, query, log).Bind(_ => Query(target, new Statement(query.Site, query.Statement) { Timeout = timeout }, log,
            rows => Result.All(rows.Select(row => Result.All(row.Select(Integer)).Map(values => Row.Of([.. values]))))
                .Map(read => (Measurement)new Measurement.Answered(query.Site, SortedArray.Of(read))),
            failed => Result.Ok<Measurement>(failed.TimedOut ? new Measurement.TimedOut(query.Site, timeout)
                : new Measurement.Raised(query.Site, failed.Number, failed.Message))))
        .Bind(measured => measured);

    /// <summary>
    /// The query, when no table it reads by name is a synonym on the target (DECISIONS.md, 2026-09-25): a synonym can stand for a table
    /// in another database or on a linked server, which the query's text cannot show, so the target's catalog is asked, in one statement
    /// through <see cref="Query{T}"/>, before the query runs. A synonym is aggregate-query.refused, named as the query writes it.
    /// </summary>
    private static Result<AggregateQuery> NoSynonym(Database target, AggregateQuery query, QueryLog log) => query.Tables.Count == 0 ? query
        : Query(target, new Statement("Synonyms: " + query.Site,
            "SELECT t.name FROM (VALUES "
            + string.Join(", ", query.Tables.Select((_, i) => "(@t" + i.ToString(CultureInfo.InvariantCulture) + ")"))
                + ") AS t(name) WHERE OBJECT_ID(t.name, N'SN') IS NOT NULL;") { Parameters = [.. query.Tables.Select((table, i) =>
                    ("@t" + i.ToString(CultureInfo.InvariantCulture), table))] },
            log, rows => rows.Select(row => (string)row[0]!).ToList())
        .Bind(synonyms => synonyms is [var synonym, ..]
            ? new Error("aggregate-query.refused",
                "The query " + query.Site + " reads " + synonym
                + ", a synonym, which can stand for a table in another database or on a linked server; "
                + "an aggregate query reads the target's own tables.",
                "Name the table the synonym stands for in the query.")
            : Result.Ok(query));

    /// <summary>
    /// A value the allowlist admits the type of: an integer of any width, or NULL. Anything else is a defect in the allowlist,
    /// internal.answer-type, named by its type alone and never by the value; it once escaped the adapter as an exception.
    /// </summary>
    internal static Result<long?> Integer(object? value) => value switch
    {
        null => Result.Ok<long?>(null),
        int or long or short or byte => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        _ => new Error("internal.answer-type",
            "An aggregate query answered with a " + value.GetType().Name
            + ", a type no form of the allowlist yields; the value is withheld.",
            "Report this answer and the aggregate query's site to dbchange's maintainers: "
                + "the allowlist admitted a query whose answer is not an integer."),
    };
}
