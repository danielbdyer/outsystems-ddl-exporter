using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// profile as a use case (V3_MILESTONES.md WP 3.7), narrowed to looking, as the Facsimile's shape does. A column's value-free shape, read
/// from a named environment of either classification (§17 item 24): the catalog's own spelling and type of the column, read by
/// parameters; its summary's integers (AggregateQueries.Shape); and its signatures with their counts, the rare ones folded, the one form of
/// the allowlist that answers text (SqlServer.Signatures.cs). --vocabulary, which would read values, is refused for an environment
/// classified real before its connection is read, and is not in this build for one a lead confirmed synthetic. And the row tiers of every
/// table the environment's catalog shows, from the rows sys.partitions keeps, appended to dbchange/ledgers/row-tiers.md (Profile.Ledger.cs)
/// and, with --commit, pushed on a branch of its own. Nothing here writes a value: the ledger holds names, tiers and dates.
/// </summary>
public static partial class Profile
{
    /// <summary>The types a signature reads: the character types, each cast to nvarchar(max) by the form.</summary>
    private static readonly HashSet<string> Characters = new(StringComparer.Ordinal) { "char", "varchar", "nchar", "nvarchar" };

    /// <summary>
    /// A column's summary (the Facsimile's P6): its rows; those holding NULL, the empty string, spaces alone, and a space first or last;
    /// its longest value in characters and in bytes, null over no value; and its distinct values as collated and as bytes, the latter null
    /// where its code page has no binary collation.
    /// </summary>
    public sealed record Summary(long Rows, long Nulls, long Empty, long SpacesOnly, long EdgeSpaces, long? MaxLength, long? MaxBytes,
        long DistinctAsCollated, long? DistinctBinary);

    /// <summary>A column's value-free shape: the environment, the table and the column as its catalog spells them, its type, its summary
    /// and its signatures.</summary>
    public sealed record Shape(Target Environment, Name Table, string Column, string Type, Summary Summary, SqlServer.Signatures Signatures)
    {
        /// <summary>The column as QUOTENAME writes its table's name and its own: [dbo].[Customer].[Email].</summary>
        public string Named => Table + "." + SqlServer.Quoted(Column);
    }

    /// <summary>A table's row tier as measured, null where sys.partitions shows the login no partition of it, and whether the measurement
    /// appended its row to the ledger.</summary>
    public sealed record TableTier(Name Table, string? Tier, bool Appended);

    /// <summary>A measurement of row tiers: the environment, its UTC date, each table's tier, and the branch --commit pushed and its
    /// commit, each null where nothing was pushed.</summary>
    public sealed record Tiers(Target Environment, DateOnly Date, IReadOnlyList<TableTier> Tables, string? Branch, string? Commit);

    /// <summary>
    /// dbchange profile --env &lt;environment&gt; &lt;schema.table.column&gt; [--vocabulary]: the column's value-free shape, after the
    /// identity is found to hold VIEW DEFINITION there; the column named in three parts, and one the catalog does not show, or shows as no
    /// character type, refused before any query reads a row.
    /// </summary>
    public static Result<Shape> Column(Checkout checkout, string environment, string column, bool vocabulary, SqlServer.QueryLog log) =>
        ColumnOf(column).Bind(named => Environment(checkout, environment).Bind(env => vocabulary
            ? Vocabulary(env.Named)
            : SqlServer.Resolve(env.Named.Target, env.File, checkout.Root).Bind(database => SqlServer.Reach(database, log)
                .Bind(_ => SqlServer.ColumnOf(database, named.Table, named.Column, log))
                .Bind(held => held is not { } catalog
                    ? Absent(database.Target, named.Table, named.Column)
                    : !Characters.Contains(catalog.Type) ? NoText(database.Target, catalog)
                    : AggregateQueries.Shape(catalog.Table, catalog.Column, catalog.Type, catalog.Collation)
                        .Bind(summary => SqlServer.Measure(database, summary, log)).Bind(measured => SummaryOf(database, measured))
                        .Bind(summary => SqlServer.SignatureQuery.Of(catalog.Table, catalog.Column)
                            .Bind(query => SqlServer.Measure(database, query, log))
                            .Map(signatures => new Shape(database.Target, catalog.Table, catalog.Column, catalog.Type, summary,
                                signatures)))))));

    /// <summary>
    /// dbchange profile --env &lt;environment&gt; --counts [--commit]: every user table's row tier, measured from sys.partitions as one
    /// aggregate query each, dated by the UTC day of <paramref name="now"/>, the one clock this reads; a row appended to the ledger for
    /// each table whose tier changed; and, with <paramref name="commit"/>, the ledger committed on HEAD's tree on a branch named for the
    /// environment and the time and pushed (Git.CommitAndPush), where the measurement appended a row.
    /// </summary>
    public static Result<Tiers> Counts(Checkout checkout, string environment, bool commit, SqlServer.QueryLog log,
        DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        return Environment(checkout, environment).Bind(env => SqlServer.Resolve(env.Named.Target, env.File, checkout.Root)
            .Bind(database => SqlServer.Reach(database, log).Bind(_ => SqlServer.TablesOf(database, log))
                .Bind(tables => Result.All(tables.Select(table => AggregateQueries.PartitionRows(table, table + " rows from sys.partitions",
                        null)
                    .Bind(query => SqlServer.Measure(database, query, log)).Bind(measured => TierOf(database, table, measured))))))
            .Bind(measured => Recorded(checkout.Root, env.Named.Name, DateOf(at), measured).Bind(appended =>
                (commit && appended.Count > 0
                    ? Git.CommitAndPush(checkout.Root, [Ledger], "Row tiers of " + env.Named.Target + " as measured on "
                        + DateOf(at).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        "dbchange/row-tiers-" + env.Named.Name + "-"
                        + at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)).Map(p => (Git.Pushed?)p)
                    : Result.Ok<Git.Pushed?>(null))
                .Map(pushed => new Tiers(env.Named.Target, DateOf(at),
                    [.. measured.Select(m => new TableTier(m.Table, m.Tier, appended.Contains(m.Table)))], pushed?.Branch,
                    pushed?.Commit)))));
    }

    /// <summary>The UTC day of a clock's time: the date a measurement's rows carry (VALUES.md O12).</summary>
    public static DateOnly DateOf(DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);

    /// <summary>A column named in three parts, schema.table.column, as ScriptDom reads a name; arguments.column for any other text, which
    /// is not quoted.</summary>
    internal static Result<(Name Table, string Column)> ColumnOf(string text) =>
        TSql.NameParts(text) is [var schema, var table, var column] && Name.Of(schema, table) is Result<Name>.Ok { Value: var name }
            && Name.Of(column) is Result<Name>.Ok
            ? (name, column)
            : new Error("arguments.column", "The column to profile is not a name of three parts, schema.table.column.",
                "Name the column as schema.table.column, such as dbo.Customer.Email.");

    /// <summary>The environment the argument names, as the environments file names it, with the file as read once.</summary>
    private static Result<(NamedEnvironment Named, Result<Environments> File)> Environment(Checkout checkout, string name) =>
        EnvironmentName.Of("--env", name.StartsWith("env:", StringComparison.Ordinal) ? name[4..] : name).Bind(environment =>
        {
            var file = EnvironmentsFile.Read(checkout.Root);
            return file.Bind(environments => environments.Named(environment) is { } named ? Result.Ok((named, file))
                : Result.Fail<(NamedEnvironment, Result<Environments>)>(
                    SqlServer.Unnamed(new Target.Environment(environment), environments)));
        });

    /// <summary>
    /// --vocabulary, which returns the values a column holds (the Facsimile's P6): refused for an environment classified real, whose
    /// values no query reads (§17 item 2), and not in this build for one a lead confirmed synthetic, since it returns with the shape tier
    /// on §17 item 20's trigger.
    /// </summary>
    internal static Error Vocabulary(NamedEnvironment environment) => environment.Classification.Match(
        _ => new Error("aggregate-query.vocabulary",
            environment.Target + " is classified real, and --vocabulary returns the values a column holds, which no query reads from an"
                + " environment classified real; its signatures and their counts are read without it.",
            "Run dbchange profile without --vocabulary, or have a named lead confirm the environment synthetic in "
                + EnvironmentsFile.Json + "."),
        _ => new Error("verb.not-built", "dbchange profile --vocabulary is not in this build; " + environment.Target
                + "'s signatures and their counts are read without it.",
            "Run dbchange --help to see the verbs this build has."));

    /// <summary>A column the environment's catalog shows this identity nowhere: target.no-column, naming it.</summary>
    internal static Error Absent(Target environment, Name table, string column) => new("target.no-column",
        environment + " holds no column " + table + "." + SqlServer.Quoted(column) + " of a table this identity can see.",
        "Name a column of a table " + environment + " holds, as schema.table.column, such as dbo.Customer.Email.");

    /// <summary>A column of no character type: target.column-type, naming it and its type.</summary>
    internal static Error NoText(Target environment, SqlServer.CatalogColumn column) => new("target.column-type",
        environment + "'s " + column.Table + "." + SqlServer.Quoted(column.Column) + " is " + column.Type
            + ", and profile reads the shape of a char, varchar, nchar or nvarchar column.",
        "Profile a column of a character type; a number's or a date's shape is its type's.");

    /// <summary>The summary's one row read, or the error the measurement's failure is, SQL Server's message withheld for a named
    /// environment.</summary>
    private static Result<Summary> SummaryOf(SqlServer.Database database, SqlServer.Measurement measured) => measured.Match(
        answered => answered.Rows is [{ Values: [{ } rows, var nulls, var empty, var spaces, var edges, var length, var bytes, { } distinct,
            ..] values }]
            ? new Summary(rows, nulls ?? 0, empty ?? 0, spaces ?? 0, edges ?? 0, length, bytes, distinct,
                values.Count > 8 ? values[8] : null)
            : Result.Fail<Summary>(new Error("internal.answer-type",
                "The summary of " + answered.Site + " answered other than one row of its items; the answer is withheld.",
                "Report this answer and the profile command to dbchange's maintainers: the summary is not the query built.")),
        raised => database.ErrorOf(raised.Number, raised.Message ?? ""),
        timedOut => database.ErrorOf(-2, "", fatal: false, opened: true));

    /// <summary>A table's tier from its sys.partitions query: null where the login sees no partition, which is never read as
    /// empty.</summary>
    private static Result<(Name Table, string? Tier)> TierOf(SqlServer.Database database, Name table, SqlServer.Measurement measured) =>
        measured.Match<Result<(Name, string?)>>(
            answered => answered.Rows is [{ Values: [{ } partitions, var rows] }]
                ? (table, partitions > 0 ? TierOf(rows ?? 0) : null)
                : new Error("internal.answer-type", "The rows of " + table + " answered other than one row of two counts.",
                    "Report this answer and the profile command to dbchange's maintainers: the query is not the one built."),
            raised => database.ErrorOf(raised.Number, raised.Message ?? ""),
            timedOut => database.ErrorOf(-2, "", fatal: false, opened: true));
}
