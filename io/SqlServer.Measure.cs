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
    /// checked, with no comment and no batch separator; the site it measures; the tables it reads by name; and the session it runs under,
    /// which io's builders (WP 2.3) give and a query read from text lacks. Only Of makes one, and Of is internal to io (DECISIONS.md,
    /// 2026-09-25), so only io's builders make one for a named environment and no verb or file hands one text; Measure runs nothing else.
    /// </summary>
    public sealed class AggregateQuery
    {
        private AggregateQuery(string text, TSqlStatement statement, string site, Session? session) =>
            (Statement, Site, Tables, Session) = (text, site, TSql.TablesNamed(statement), session);

        public string Statement { get; }

        public string Site { get; }

        /// <summary>
        /// Each table the query reads by name, as QUOTENAME writes it: what Measure asks the target whether a synonym stands for.
        /// </summary>
        internal IReadOnlyList<string> Tables { get; }

        /// <summary>The session Measure sets before the query, or null for a query read from text, which runs under the login's
        /// own.</summary>
        public Session? Session { get; }

        internal static Result<AggregateQuery> Of(string text, string site) =>
            Allowlist.Admitted(text).Map(statement => new AggregateQuery(TSql.Text(statement), statement, site, null));

        /// <summary>
        /// An aggregate query built as a ScriptDom tree, checked as the tree, and run as the text ScriptDom writes it back as, under the
        /// session given. That text is read again and checked as one statement before it is kept, since the generator writes some nodes
        /// as their value with no quoting (an unquoted identifier, a COLLATE's name), so the text is what runs only once it passes too.
        /// </summary>
        internal static Result<AggregateQuery> Of(TSqlStatement tree, string site, Session? session = null) =>
            Allowlist.Admitted(tree, site).Map(admitted => TSql.Text(admitted)).Bind(text => Allowlist.Admitted(text).Match(
                statement => Result.Ok(new AggregateQuery(text, statement, site, session)),
                refused => Result.Fail<AggregateQuery>(new Error("aggregate-query.refused",
                    "The query " + site + ", as ScriptDom writes it, is refused when read again: " + refused.Message,
                    "Report the site to dbchange's maintainers: a builder made a tree whose text is not the query it checked."))));

        public override string ToString() => Site + ": " + Statement;
    }

    /// <summary>The order SET DATEFORMAT reads a date's month, day and year in.</summary>
    public enum DateFormat
    {
        Mdy,
        Dmy,
        Ymd,
        Ydm,
        Myd,
        Dym,
    }

    /// <summary>
    /// The session an aggregate query io's builders make runs under (WP 2.3): its SET LANGUAGE and SET DATEFORMAT, which decide how
    /// TRY_CONVERT reads a date written as a string and how a date is written as one, so a Fits count depends on them as ALTER COLUMN does
    /// under the Octopus login. That login's own pair is unknown until spike S7 reports it, so <see cref="Assumed"/>, us_english and mdy,
    /// is committed, and every answer counted under a session carries <see cref="Note"/>. Measure sends the two values as parameters.
    /// </summary>
    public sealed record Session(string Language, DateFormat DateFormat)
    {
        /// <summary>The Octopus login's language and date order as assumed until S7: us_english, and mdy, us_english's own.</summary>
        public static readonly Session Assumed = new("us_english", DateFormat.Mdy);

        /// <summary>The date order as SET DATEFORMAT spells it: mdy, dmy and the rest.</summary>
        internal string Order => DateFormat.ToString().ToLowerInvariant();

        /// <summary>The note an answer counted under this session carries: neither value is verified against the Octopus step.</summary>
        public Finding Note => Finding.Note("session.unverified", "the aggregate queries' session",
            "Every aggregate query ran under SET LANGUAGE " + Language + " and SET DATEFORMAT " + Order + ", the language and date order "
            + "assumed for the Octopus login until spike S7 reports its own; they are not verified against the Octopus step.");
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
    /// its timeout as timed out; a connection that fails, or an identity refused, is an error. A query with a session runs after its SET
    /// LANGUAGE and SET DATEFORMAT, in the same batch on the same connection, the two values sent as parameters, which the log declares.
    /// </summary>
    public static Result<Measurement> Measure(Database target, AggregateQuery query, QueryLog log) =>
        Measure(target, query, log, AggregateQueryTimeout);

    internal static Result<Measurement> Measure(Database target, AggregateQuery query, QueryLog log, TimeSpan timeout) =>
        NoSynonym(target, query, log).Bind(_ => Query(target, Sent(query) with { Timeout = timeout }, log,
            rows => Result.All(rows.Select(row => Result.All(row.Select(Integer)).Map(values => Row.Of([.. values]))))
                .Map(read => (Measurement)new Measurement.Answered(query.Site, SortedArray.Of(read))),
            failed => Result.Ok<Measurement>(failed.TimedOut ? new Measurement.TimedOut(query.Site, timeout)
                : new Measurement.Raised(query.Site, failed.Number, failed.Message))))
        .Bind(measured => measured);

    /// <summary>The statement Measure sends for a query: the query alone, or its session's two SET statements and then the query.</summary>
    private static Statement Sent(AggregateQuery query) => query.Session is not { } session ? new Statement(query.Site, query.Statement)
        : new Statement(query.Site, "SET LANGUAGE @language;\nSET DATEFORMAT @dateformat;\n" + query.Statement)
        {
            Parameters = [("@language", session.Language), ("@dateformat", session.Order)],
        };

    private static Result<AggregateQuery> NoSynonym(Database target, AggregateQuery query, QueryLog log) =>
        NoSynonym(target, query.Site, query.Tables, log).Map(_ => query);

    /// <summary>
    /// The site, when no table its query reads by name is a synonym on the target (DECISIONS.md, 2026-09-25): a synonym can stand for a
    /// table in another database or on a linked server, which the query's text cannot show, so the target's catalog is asked, in one
    /// statement through <see cref="Query{T}"/>, before the query runs. A synonym is aggregate-query.refused, named as the query writes it.
    /// </summary>
    private static Result<string> NoSynonym(Database target, string site, IReadOnlyList<string> tables, QueryLog log) => tables.Count == 0
        ? site
        : Query(target, new Statement("Synonyms: " + site,
            "SELECT t.name FROM (VALUES "
            + string.Join(", ", tables.Select((_, i) => "(@t" + i.ToString(CultureInfo.InvariantCulture) + ")"))
                + ") AS t(name) WHERE OBJECT_ID(t.name, N'SN') IS NOT NULL;") { Parameters = [.. tables.Select((table, i) =>
                    ("@t" + i.ToString(CultureInfo.InvariantCulture), table))] },
            log, rows => rows.Select(row => (string)row[0]!).ToList())
        .Bind(synonyms => synonyms is [var synonym, ..]
            ? new Error("aggregate-query.refused",
                "The query " + site + " reads " + synonym
                + ", a synonym, which can stand for a table in another database or on a linked server; "
                + "an aggregate query reads the target's own tables.",
                "Name the table the synonym stands for in the query.")
            : Result.Ok(site));

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
