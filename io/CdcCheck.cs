using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// check cdc as a use case (WP 2.5): which of the tables the project declares at HEAD an environment's Change Data Capture tracks, read
/// from sys.tables.is_tracked_by_cdc, a catalog read, through SqlServer.Measure as the identity the environment's connection names, and
/// the rows dbchange/ledgers/cdc-tracked.md holds for it. Measure answers integers alone, so each aggregate query asks of tables by name,
/// one CASE WHEN EXISTS item a table, the allowlist's form for an existence; no name leaves the environment. With --commit, the ledger
/// is committed on a new branch and pushed for review (io/Git.Push).
/// </summary>
public static class CdcCheck
{
    /// <summary>How many tables one aggregate query asks of: SQL Server takes 4,096 items in a select list, and 500 keep one
    /// short.</summary>
    internal const int PerQuery = 500;

    /// <summary>
    /// What check cdc answers: the environment; the commit whose project's tables were asked of; how many tables it declares; those the
    /// environment tracks; the ledger's text with the environment's rows replaced; the date of the read; the branch and commit pushed
    /// with --commit, or null; and the notes reading the project raised.
    /// </summary>
    public sealed record Answer(EnvironmentName Environment, string Commit, int Declared, SortedArray<Name> Tracked, string Ledger,
        string Measured, Git.Pushed? Pushed, IReadOnlyList<Finding> Notes);

    /// <summary>One aggregate query, and the tables its items ask of, in order.</summary>
    internal sealed record TableQuery(SqlServer.AggregateQuery Query, IReadOnlyList<Name> Tables);

    /// <summary>
    /// check cdc: the standing; the ledger read, so a malformed row is refused before anything connects; the environment resolved and
    /// reached with VIEW DEFINITION; the project at HEAD built and its tables read; each aggregate query measured; and, with
    /// <paramref name="commit"/>, the ledger proposed.
    /// </summary>
    public static Stamped<Answer> Run(Checkout checkout, EnvironmentName environment, string? project, bool commit,
        SqlServer.QueryLog log)
    {
        var standing = Standing.Of(checkout);
        if (standing.Result is Result<Pin>.Failed { Error: var refused })
        {
            return new(standing.Stamp, refused);
        }

        var measured = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new(standing.Stamp, CdcLedger.Read(checkout.Root).Bind(rows =>
            SqlServer.Resolve(new Target.Environment(environment), checkout.Root).Bind(database =>
            SqlServer.Reach(database, log).Bind(_ =>
            Ssdt.Build(checkout.Root, "HEAD", project, checkout.Tool, checkout.WorkingDirectory).Bind(built =>
            Tables(built.Built.Path).Bind(read =>
            Queries(read.Tables).Bind(queries => Measured(database, queries, log)).Bind(tracked =>
            {
                var ledger = CdcLedger.Written(rows, environment, tracked, measured);
                return (commit ? Proposed(checkout.Root, environment, ledger, measured) : Result.Ok<Git.Pushed?>(null))
                    .Map(pushed => new Answer(environment, built.Commit, read.Tables.Count, tracked, ledger, measured, pushed,
                        read.Notes));
            })))))));
    }

    /// <summary>
    /// The aggregate queries for <paramref name="tables"/>, in the order of their names, at most <see cref="PerQuery"/> items each: for
    /// each table, CASE WHEN EXISTS a row of sys.tables joined to sys.schemas whose is_tracked_by_cdc is 1 and whose schema and name are
    /// the table's, written as N'' literals, THEN 1 ELSE 0 END. Each is admitted by the allowlist as the text it runs as; none for no
    /// table.
    /// </summary>
    internal static Result<List<TableQuery>> Queries(IEnumerable<Name> tables) =>
        Result.All(SortedArray.Of(tables.Distinct()).Chunk(PerQuery)
            .Select(chunk => SqlServer.AggregateQuery.Of(
                    "SELECT " + string.Join(", ", chunk.Select(Item)) + ";",
                    "check cdc: sys.tables.is_tracked_by_cdc of " + Counted(chunk.Length))
                .Map(query => new TableQuery(query, chunk))))
        .Map(queries => queries.ToList());

    /// <summary>
    /// What an aggregate query measured: the tables whose item answered 1. An error SQL Server raised running it, or its timeout, is
    /// cdc.unread, by the error's number and never its message.
    /// </summary>
    internal static Result<SortedArray<Name>> Tracked(TableQuery asked, SqlServer.Measurement measured) => measured.Match(
        answered => answered.Rows.Count == 1 && answered.Rows[0] is var row && row.Values.Count == asked.Tables.Count
            ? Result.Ok(SortedArray.Of(asked.Tables.Where((_, i) => row.Values[i] == 1)))
            : Unread(asked, "answered " + answered.Rows.Count.ToString(CultureInfo.InvariantCulture) + " rows where it asks one"),
        raised => Unread(asked, "raised Msg " + raised.Number.ToString(CultureInfo.InvariantCulture)),
        late => Unread(asked, "ran past its timeout of " + late.After.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s"));

    /// <summary>
    /// The ledger committed on dbchange/cdc-&lt;environment&gt;-&lt;date&gt; and pushed to the origin: the file written into the working
    /// tree for the commit (Git.CommitAndPush reads the paths from there) and set back as it was however the push ends, so the caller's
    /// branch, index and working tree stay as they were; null, and nothing committed, where the working tree's ledger already reads so.
    /// </summary>
    internal static Result<Git.Pushed?> Proposed(string repositoryRoot, EnvironmentName environment, string ledger, string measured)
    {
        var path = Path.Combine(repositoryRoot, CdcLedger.File);
        var before = File.Exists(path) ? File.ReadAllText(path) : null;
        if (before == ledger)
        {
            return Result.Ok<Git.Pushed?>(null);
        }

        return Git.TopLevel(repositoryRoot).Bind(top => Write.Text(path, ledger).Bind(_ =>
        {
            try
            {
                return Git.CommitAndPush(top, [Path.GetRelativePath(top, path).Replace('\\', '/')],
                        "dbchange check cdc --env " + environment + ": the tables env:" + environment
                            + " tracks by Change Data Capture, read " + measured,
                        "dbchange/cdc-" + environment + "-" + measured)
                    .Map(pushed => (Git.Pushed?)pushed);
            }
            finally
            {
                if (before is null)
                {
                    Write.Discard(path);
                }
                else
                {
                    Write.Text(path, before);
                }
            }
        }));
    }

    /// <summary>Each aggregate query measured in turn, the tables tracked gathered; the first failure ends the read.</summary>
    private static Result<SortedArray<Name>> Measured(SqlServer.Database database, List<TableQuery> queries, SqlServer.QueryLog log) =>
        Result.All(queries.Select(asked => SqlServer.Measure(database, asked.Query, log).Bind(measured => Tracked(asked, measured))))
            .Map(each => SortedArray.Of(each.SelectMany(t => t)));

    /// <summary>The tables a package's model holds, and the notes its model raised.</summary>
    private static Result<(SortedArray<Name> Tables, IReadOnlyList<Finding> Notes)> Tables(string dacpac) =>
        Ssdt.Open(dacpac).Bind(package =>
        {
            using (package)
            {
                return package.Elements.Map(model => (SortedArray.Of(model.Elements.Where(e => e.Key.Type == "Table")
                    .Select(e => e.Key.Name)), (IReadOnlyList<Finding>)[.. model.Notes(package.Source)]));
            }
        });

    /// <summary>One table's item: whether sys.tables marks it tracked, its schema and name as N'' literals.</summary>
    private static string Item(Name table) =>
        "CASE WHEN EXISTS (SELECT 1 FROM [sys].[tables] AS t INNER JOIN [sys].[schemas] AS s ON s.schema_id = t.schema_id"
        + " WHERE t.is_tracked_by_cdc = 1 AND s.name = " + Literal(table.Schema ?? "dbo") + " AND t.name = " + Literal(table.Base)
        + ") THEN 1 ELSE 0 END";

    private static string Literal(string text) => "N'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Counted(int tables) => tables.ToString(CultureInfo.InvariantCulture) + (tables == 1 ? " table" : " tables");

    private static Error Unread(TableQuery asked, string what) => new("cdc.unread",
        "The aggregate query " + asked.Query.Site + " " + what + ", so which of its tables Change Data Capture tracks is not read.",
        "Run dbchange check cdc again, and ask the environment's administrators whether this identity holds VIEW DEFINITION there.");
}
