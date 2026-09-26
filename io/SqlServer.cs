using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using DbChange.Kernel;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnType = Microsoft.SqlServer.TransactSql.ScriptDom.ColumnType;

namespace DbChange.Io;

/// <summary>
/// A live database, read whole and read only (V3_MILESTONES.md §2.2, WP 1.4), and the one adapter to SQL Server and SqlClient: an
/// argument read as a target (kernel/Target.cs); EnvironmentDatabase, the database of an environment dbchange/environments.json names, and Copy,
/// a database io/LocalServer made, which alone publishes (§2.1 rule 3); Query, the one path for the statements dbchange sends itself;
/// Database.ErrorOf, the one boundary every SqlClient or DacFx failure passes through, reading SQL Server's numbers once; Reach, what
/// this identity may read there, asked before anything builds; and Measure, which runs an aggregate query its closed allowlist admits,
/// every answer an integer. io/DacFx reads a database and plans against it. A resolved connection is never printed, logged or put in an error, and a named
/// environment's SQL Server messages are withheld, since they can quote a row (§18). Objects are keyed by kernel/Name, compared ordinally
/// with case, so two databases whose collations fold case differently read the same schema alike; dbchange sets no collation and no SET
/// option of its own, and DacFx reads each database's own. An error's code names what went wrong; cli/Contract.cs maps its category to
/// the exit.
/// </summary>
public static class SqlServer
{
    /// <summary>A login, a database or a permission refused: the identity cannot read the target.</summary>
    private static readonly HashSet<int> Denials = [18456, 18452, 18470, 18486, 18487, 18488, 4060, 916, 229, 230, 262, 297, 300, 15247, 40532];

    /// <summary>No answer: the network, the instance or a timeout.</summary>
    private static readonly HashSet<int> Silences = [-2, -1, 2, 20, 26, 35, 40, 53, 64, 121, 232, 233, 258, 1225, 10053, 10054, 10060, 10061, 11001, 11004, 17142, 18401, 40613];

    /// <summary>A statement SQL Server refused on an open connection: its number, its message where a copy's may be kept, and whether the command ran past its timeout.</summary>
    internal sealed record StatementFailure(int Number, string? Message, bool TimedOut);

    /// <summary>An exception and each exception inside it, outermost first.</summary>
    private static IEnumerable<Exception> Chain(Exception failure)
    {
        for (var x = failure; x is not null; x = x.InnerException)
        {
            yield return x;
        }
    }

    /// <summary>Whether a failure carries a SqlException, however deep: what Database.ErrorOf reads by its number.</summary>
    private static bool Carries(Exception failure) => Chain(failure).Any(x => x is SqlException);

    /// <summary>
    /// The target an argument names (kernel/Target.cs), read for the argument <paramref name="subject"/>; a literal connection string, which
    /// only SqlClient's grammar tells from a target, is connection.literal at exit 6, and no part of the argument is quoted.
    /// </summary>
    public static Result<Target> Target(string text, string subject) =>
        ConnectionString.IsConnection(text) ? new Error("connection.literal", subject + " is a literal connection string, which no argument carries.",
            "Name the target as env:NAME, an environment whose connection dbchange/environments.json gives as env:VARIABLE or file:path.")
        : Kernel.Target.Parse(text, subject);

    /// <summary>A live database the tool reads: a named environment's or a copy's. Its resolved connection stays inside io, and it prints as its target.</summary>
    public abstract class Database
    {
        private protected Database(Target target, string connection) => (Target, Connection) = (target, connection);

        /// <summary>The target that names it: env:dev, copy:dbchange_host_4242_0a1b2c3d.</summary>
        public Target Target { get; }

        internal string Connection { get; }

        /// <summary>The database's name, as DacFx plans against it.</summary>
        internal string Catalog => ConnectionString.CatalogOf(Connection);

        /// <summary>Whether SQL Server's messages about a failed statement are withheld: a named environment's rows may be real (VALUES.md X2).</summary>
        internal abstract bool Withheld { get; }

        /// <summary>
        /// What a SQL Server error against this database becomes, by its number (M1 exit 7, X2): a login, a database or a permission
        /// refused is server.denied; no answer is server.unreachable; anything else is server.failed. SQL Server's message is kept only for a
        /// copy's failed statement, a copy's rows being generated; one about a connection can name the server or the login, and is withheld.
        /// </summary>
        public Error ErrorOf(int number, string message) => ErrorOf(number, message, fatal: false);

        /// <summary>
        /// The error a SqlClient failure against a target becomes, the one boundary every failure of a statement dbchange sends passes
        /// through: with a SqlException anywhere in the chain, by its number, a severity of 20 or more being a connection lost, and
        /// <paramref name="opened"/> saying the connection had opened; any other failure is server.failed with no number. A DacFx failure
        /// passes through io/DacFx.Failed, which hands this adapter the SqlException or the SQL Server number it finds.
        /// </summary>
        internal Error ErrorOf(Exception failure, bool opened) => Chain(failure).OfType<SqlException>().FirstOrDefault() is { } sql
            ? ErrorOf(sql.Number, sql.Message, fatal: sql.Class >= 20, opened)
            : ErrorOf(0, failure.Message);

        /// <summary>
        /// The error SQL Server's error <paramref name="number"/> against this database becomes, as <see cref="Classify"/> reads it:
        /// server.denied, server.unreachable, server.timed-out or server.failed. <paramref name="fatal"/> is a severity of 20 or more,
        /// which closes the connection; <paramref name="opened"/> says the connection had opened before the statement failed.
        /// </summary>
        internal Error ErrorOf(int number, string message, bool fatal, bool opened = false)
        {
            var msg = number == 0 ? "no SQL Server number" : string.Create(CultureInfo.InvariantCulture, $"Msg {number}");
            return Classify(number, fatal, opened) switch
            {
                Category.Denied => new Error("server.denied", Target + " refused this identity (" + msg + ", SQL Server's message withheld)"
                    + (this is EnvironmentDatabase ? "; a lead's prediction will appear on the pull request." : "."), this is EnvironmentDatabase
                    ? "Ask a lead to predict for " + Target + ", or ask its DBA for VIEW DEFINITION and db_datareader there."
                    : "Check the local server's login in DBCHANGE_SQL or ~/.dbchange/sql.env, then run dbchange doctor."),
                Category.Unreachable => new Error("server.unreachable", Target + " does not answer (" + msg + ", SQL Server's message withheld).", this is EnvironmentDatabase
                    ? "Check the network path to " + Target + "'s server and that it runs, then run dbchange doctor."
                    : "Start the local server with ci/sql.sh up, or ci/sql.ps1 up on Windows, then run dbchange doctor."),
                Category.TimedOut => new Error("server.timed-out", Target + " answered, and the statement ran past its timeout (" + msg + ", SQL Server's message withheld).",
                    "Run the step again when the server is less busy, or ask its DBA what holds the locks the statement waits on."),
                _ => new Error("server.failed", Target + " failed the statement: " + msg + (Withheld ? "; SQL Server's message is withheld, since it can quote a row." : ": " + message),
                    "Look the number up in SQL Server's error list, correct what it names, then run the step again."),
            };
        }

        /// <summary>
        /// The failure of a statement SQL Server refused on an open connection, below severity 20, which a caller records as the
        /// statement's outcome (Measure's failed measurement): its number, SQL Server's message for a copy alone, and whether the command
        /// ran past its timeout. Null for a failure of the connection or the identity, which <see cref="ErrorOf(Exception, bool)"/> maps.
        /// </summary>
        internal StatementFailure? FailedStatement(Exception failure, bool opened) =>
            opened && Chain(failure).OfType<SqlException>().FirstOrDefault() is { Class: < 20 } sql
                ? new StatementFailure(sql.Number, Withheld ? null : sql.Message, Classify(sql.Number, fatal: false, opened) == Category.TimedOut)
                : null;

        /// <summary>
        /// What SQL Server's error number means for a statement dbchange sent, the one reading of SQL Server's numbers (R4 lifts it into the
        /// kernel's SqlServerError at M2): a login, a database or a permission refused (<see cref="Denials"/>) is Denied; no answer, a
        /// login timeout among them, or a severity of 20 or more, which closes the connection (<see cref="Silences"/>), is Unreachable; a
        /// command timeout (-2) on a connection that had opened is TimedOut, since the server answered; anything else is the statement's
        /// own Failed, a deadlock (1205) among them.
        /// </summary>
        private static Category Classify(int number, bool fatal, bool opened) =>
            number == -2 && opened && !fatal ? Category.TimedOut
            : Denials.Contains(number) ? Category.Denied
            : fatal || Silences.Contains(number) ? Category.Unreachable
            : Category.Failed;

        private enum Category
        {
            Denied,
            Unreachable,
            TimedOut,
            Failed,
        }

        public sealed override string ToString() => Target.ToString();
    }

    /// <summary>The database of an environment dbchange/environments.json names: read only, and never published to (VALUES.md S7).</summary>
    public sealed class EnvironmentDatabase : Database
    {
        private EnvironmentDatabase(NamedEnvironment environment, string connection, string repositoryRoot)
            : base(environment.Target, connection) => (Environment, Root) = (environment, repositoryRoot);

        public NamedEnvironment Environment { get; }

        internal string Root { get; }

        internal override bool Withheld => true;

        internal static Result<EnvironmentDatabase> Of(NamedEnvironment environment, string repositoryRoot) =>
            Connect(Subject(environment), environment.Connection, repositoryRoot).Map(c => new EnvironmentDatabase(environment, c, repositoryRoot));

        /// <summary>How an error about an environment's connection names it: its environment and its reference, never what the reference resolves to.</summary>
        internal static string Subject(NamedEnvironment environment) => environment.Target + "'s connection, " + environment.Connection + ",";
    }

    /// <summary>
    /// A database io/LocalServer made on the local server and recorded in .dbchange/copies.json (§2.1 rule 3): the one target that
    /// publishes, and the one a Permissive profile is made for. Its constructor is io's, and only io/LocalServer calls it.
    /// </summary>
    public sealed class Copy : Database
    {
        internal Copy(CopyName name, string server, string repositoryRoot)
            : base(new Target.RegisteredCopy(name), ConnectionString.OfDatabase(server, name.ToString())) => (Name, Root) = (name, repositoryRoot);

        public CopyName Name { get; }

        /// <summary>The repository whose registry holds this copy.</summary>
        internal string Root { get; }

        internal override bool Withheld => false;

        /// <summary>The pipeline's profile with BlockOnPossibleDataLoss off (§1 fact 10), made for this copy: the one maker of a Permissive profile.</summary>
        public PublishProfile.Permissive Permissive(PublishProfile.Strict strict) => PublishProfile.Permissive.Of(strict);

        /// <summary>The package at <paramref name="dacpac"/> published to this copy under the profile's options, Strict or this copy's Permissive, through io/DacFx.Publish.</summary>
        public Result<Copy> Publish(string dacpac, PublishProfile profile) => Reached(this, null).Bind(_ => Ssdt.Open(dacpac).Bind(package =>
        {
            using (package)
            {
                return DacFx.Publish(this, package, profile).Map(_ => this);
            }
        }));
    }

    /// <summary>A target as a database, dbchange/environments.json read under the repository root for it.</summary>
    public static Result<Database> Resolve(Target target, string repositoryRoot) => Resolve(target, EnvironmentsFile.Read(repositoryRoot), repositoryRoot);

    /// <summary>
    /// A target as a database, against dbchange/environments.json as the verb read it once (<paramref name="environmentsFile"/>, whose error counts only
    /// for a target that needs the environments file): env: through the environment's connection reference; copy: through .dbchange/copies.json
    /// alone, on a server R15 clears against the environments file (io/LocalServer). A git ref and a package are read as packages, and the
    /// synthetic copy is not in this build.
    /// </summary>
    public static Result<Database> Resolve(Target target, Result<Environments> environmentsFile, string repositoryRoot) => target.Match<Result<Database>>(
        environment => environmentsFile.Bind(environments => environments.Named(environment.Name) is { } named
            ? EnvironmentDatabase.Of(named, repositoryRoot).Map(n => (Database)n)
            : new Error("target.unnamed", environment + " names no environment of " + EnvironmentsFile.Json + ".", environments.All.Count == 0
                ? "Add the environment to " + EnvironmentsFile.Json + " with its host, connection reference and profile."
                : "Name one it holds: " + string.Join(", ", environments.All.Select(e => e.Target)) + ".")),
        copy => LocalServer.Registered(repositoryRoot, copy.Name, environmentsFile).Map(c => (Database)c),
        () => new Error("synthetic-copy.not-built", "synthetic-copy names the synthetic copy, which is not in this build; this build reads env: and copy: databases.",
            "Name an env: or a copy: target; dbchange --help lists what this build runs."),
        reference => NotADatabase(reference),
        dacpac => NotADatabase(dacpac));

    /// <summary>
    /// An aggregate query the allowlist admitted (VALUES.md P2): one statement, as ScriptDom writes it back, so what runs is what was
    /// checked, with no comment and no batch separator; the site it measures; and the tables it reads by name. Only Of makes one, and Of
    /// is internal to io (DECISIONS.md, 2026-09-25), so only io's builders (WP 2.3) make one for a named environment and no verb or
    /// file hands one text; Measure runs nothing else.
    /// </summary>
    public sealed class AggregateQuery
    {
        private AggregateQuery(TSqlStatement statement, string site) => (Statement, Site, Tables) = (TSql.Text(statement), site, TSql.TablesNamed(statement));

        public string Statement { get; }

        public string Site { get; }

        /// <summary>Each table the query reads by name, as QUOTENAME writes it: what Measure asks the target whether a synonym stands for.</summary>
        internal IReadOnlyList<string> Tables { get; }

        internal static Result<AggregateQuery> Of(string text, string site) => Allowlist.Admitted(text).Map(statement => new AggregateQuery(statement, site));

        /// <summary>An aggregate query built as a ScriptDom tree, checked as the tree, and run as the text ScriptDom writes it back as.</summary>
        internal static Result<AggregateQuery> Of(TSqlStatement tree, string site) => Allowlist.Admitted(tree, site).Map(statement => new AggregateQuery(statement, site));

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

        /// <summary>SQL Server raised an error running the query (Msg 245, a conversion that failed): a measurement of its own, which the caller keeps as the site's outcome.</summary>
        public sealed record Raised(string Site, int Number, string? Message) : Measurement
        {
            public override string ToString() =>
                Site + ": SQL Server raised Msg " + Number.ToString(CultureInfo.InvariantCulture) + (Message is null ? "; message withheld" : ": " + Message);
        }

        /// <summary>SQL Server still running the query when its timeout passed, on a connection that stayed open: the server answered, and the measurement is missing, not failed.</summary>
        public sealed record TimedOut(string Site, TimeSpan After) : Measurement
        {
            public override string ToString() => Site + ": query ran past its timeout of " + After.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s";
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
            : values.Zip(other.values, Nullable.Compare).FirstOrDefault(c => c != 0) is var byValue and not 0 ? byValue : values.Length.CompareTo(other.values.Length);

        public override string ToString() => string.Join(", ", values.Select(v => v?.ToString(CultureInfo.InvariantCulture) ?? "NULL"));
    }

    /// <summary>How long SQL Server may run a statement dbchange sends before SqlClient cancels it: SqlClient's own default for a command, named.</summary>
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long SQL Server may run an aggregate query before SqlClient cancels it: the command timeout.
    /// An aggregate query reads each row of a table once, and thirty seconds covers a scan of the environments' largest tables that S3 and S8
    /// have not yet measured; a query past it is measured as timed out, not as a server that does not answer (finding ARCH-13). The
    /// profile verb of M3 revisits the figure with the row counts S8 reports; no key of the environments file and no flag sets it before then.
    /// </summary>
    internal static readonly TimeSpan AggregateQueryTimeout = CommandTimeout;

    /// <summary>
    /// One admitted aggregate query against the target (WP 1.4), through the one statement path, read back as integers and logged
    /// with its row count. A statement SQL Server refuses on the open connection is measured as failed, by its number, and one past
    /// its timeout as timed out; a connection that fails, or an identity refused, is an error.
    /// </summary>
    public static Result<Measurement> Measure(Database target, AggregateQuery query, QueryLog log) => Measure(target, query, log, AggregateQueryTimeout);

    internal static Result<Measurement> Measure(Database target, AggregateQuery query, QueryLog log, TimeSpan timeout) =>
        NoSynonym(target, query, log).Bind(_ => Query(target, new Statement(query.Site, query.Statement) { Timeout = timeout }, log,
            rows => Result.All(rows.Select(row => Result.All(row.Select(Integer)).Map(values => Row.Of([.. values]))))
                .Map(read => (Measurement)new Measurement.Answered(query.Site, SortedArray.Of(read))),
            failed => Result.Ok<Measurement>(failed.TimedOut ? new Measurement.TimedOut(query.Site, timeout) : new Measurement.Raised(query.Site, failed.Number, failed.Message))))
        .Bind(measured => measured);

    /// <summary>
    /// The query, when no table it reads by name is a synonym on the target (DECISIONS.md, 2026-09-25): a synonym can stand for a table
    /// in another database or on a linked server, which the query's text cannot show, so the target's catalog is asked, in one statement
    /// through <see cref="Query{T}"/>, before the query runs. A synonym is aggregate-query.refused, named as the query writes it.
    /// </summary>
    private static Result<AggregateQuery> NoSynonym(Database target, AggregateQuery query, QueryLog log) => query.Tables.Count == 0 ? query
        : Query(target, new Statement("Synonyms: " + query.Site, "SELECT t.name FROM (VALUES " + string.Join(", ", query.Tables.Select((_, i) => "(@t" + i.ToString(CultureInfo.InvariantCulture) + ")"))
                + ") AS t(name) WHERE OBJECT_ID(t.name, N'SN') IS NOT NULL;") { Parameters = [.. query.Tables.Select((table, i) => ("@t" + i.ToString(CultureInfo.InvariantCulture), table))] },
            log, rows => rows.Select(row => (string)row[0]!).ToList())
        .Bind(synonyms => synonyms is [var synonym, ..]
            ? new Error("aggregate-query.refused", "The query " + query.Site + " reads " + synonym + ", a synonym, which can stand for a table in another database or on a linked server; an aggregate query reads the target's own tables.",
                "Name the table the synonym stands for in the query.")
            : Result.Ok(query));

    /// <summary>
    /// A statement dbchange sends itself (R5): its site, which names it in the run's log; its text; how long SQL Server may take over it
    /// before SqlClient cancels it; the database it runs in, the target's own unless another is named; whether its connection may come
    /// from SqlClient's pool; and its parameters, each nvarchar(4000), which holds a quoted two-part name.
    /// </summary>
    internal sealed record Statement(string Site, string Text)
    {
        public TimeSpan Timeout { get; init; } = CommandTimeout;

        public string? Catalog { get; init; }

        public bool Pooled { get; init; } = true;

        public IReadOnlyList<(string Name, string Value)> Parameters { get; init; } = [];
    }

    /// <summary>
    /// The one path for a statement dbchange sends itself (R5): a SqlConnection of its own opened (io/ConnectionString.cs), the statement
    /// run under its timeout, every row of its first result read into the answer, its remaining results read so every error the batch
    /// raises surfaces, its site and row count or failure written to the run's log, and every failure mapped through Database.ErrorOf,
    /// the one boundary. A caller that records a statement's own failure as its outcome (Measure) passes <paramref name="failed"/>,
    /// which receives a failure SQL Server raised on the open connection below severity 20 and the command's timeout. No SqlConnection
    /// serves two statements, so a pooled connection the server broke fails that statement alone and is mapped once. A statement is
    /// logged once the connection opened, since only then was it sent, its parameters declared before it, so the log runs as a script.
    /// </summary>
    internal static Result<T> Query<T>(Database target, Statement statement, QueryLog? log, Func<IReadOnlyList<IReadOnlyList<object?>>, T> answer,
        Func<StatementFailure, T>? failed = null)
    {
        var opened = false;
        var logged = string.Concat(statement.Parameters.Select(p => "DECLARE " + p.Name + " nvarchar(4000) = N'" + p.Value.Replace("'", "''", StringComparison.Ordinal) + "';\n")) + statement.Text;
        try
        {
            using var connection = new SqlConnection(ConnectionString.ForStatement(target.Connection, statement.Catalog, statement.Pooled));
            connection.Open();
            opened = true;
            using var command = new SqlCommand(statement.Text, connection) { CommandTimeout = (int)statement.Timeout.TotalSeconds };
            foreach (var (name, value) in statement.Parameters)
            {
                command.Parameters.Add(new SqlParameter(name, System.Data.SqlDbType.NVarChar, 4000) { Value = value });
            }

            var rows = new List<IReadOnlyList<object?>>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var values = new object[reader.FieldCount];
                    reader.GetValues(values);
                    rows.Add([.. values.Select(v => v is DBNull ? null : (object?)v)]);
                }

                while (reader.NextResult())
                {
                }
            }

            var outcome = rows.Count == 1 ? "1 row" : rows.Count.ToString(CultureInfo.InvariantCulture) + " rows";
            return log is null ? Result.Ok(answer(rows)) : log.Add(target, statement.Site, logged, outcome).Map(_ => answer(rows));
        }
        catch (Exception e) when (e is InvalidOperationException || Carries(e))
        {
            if (failed is not null && target.FailedStatement(e, opened) is { } statementFailure)
            {
                var unlogged = log?.Add(target, statement.Site, logged, statementFailure.TimedOut
                    ? "timed out after " + statement.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s"
                    : "failed, Msg " + statementFailure.Number.ToString(CultureInfo.InvariantCulture)) as Result<string>.Failed;
                return unlogged is null ? failed(statementFailure) : unlogged.Error;
            }

            var error = target.ErrorOf(e, opened);
            return opened && log?.Add(target, statement.Site, logged, "failed, " + error.Code) is Result<string>.Failed { Error: var unwritten } ? unwritten : error;
        }
    }

    /// <summary>
    /// A run's log of every statement dbchange sends through <see cref="Query{T}"/>, .dbchange/runs/&lt;id&gt;/queries.log: each aggregate query; the
    /// VIEW DEFINITION check Reach sends ahead of an extract, whose catalog queries are DacFx's to answer for; and a copy's CREATE
    /// and DROP DATABASE. Per statement: the time, the target, the site and the row count, the failure's number or code, or the timeout,
    /// then the statement and GO, so the log runs as a script. It holds no value a statement read. Each entry is appended and flushed to
    /// the file through io/Write.Append as it is made, so a run's statements cost their own bytes once (finding ARCH-08), and a reader
    /// following the file sees each.
    /// </summary>
    public sealed class QueryLog
    {
        private readonly Lock gate = new();
        private readonly LocalState state;

        private QueryLog(LocalState state, string path) => (this.state, Path) = (state, path);

        public string Path { get; }

        /// <summary>A new run's log under the repository root, named for the time, the process and a random suffix, so two runs never share one; its folder is made with its first write.</summary>
        public static QueryLog Start(string repositoryRoot)
        {
            var state = new LocalState(repositoryRoot);
            return new(state, System.IO.Path.Combine(state.Runs, DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-"
                + System.Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant(), "queries.log"));
        }

        /// <summary>
        /// The entry appended, the run's folder made first through LocalState, so .dbchange/.gitignore stands beside it: the log's path, or
        /// file.unwritable naming why, which Query answers in place of the statement's result, since R14's record of every statement is not
        /// optional.
        /// </summary>
        internal Result<string> Add(Database target, string site, string statement, string outcome)
        {
            var entry = "-- " + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) + " " + target.Target + " " + site + ": " + outcome + "\n"
                + statement + "\nGO\n";
            lock (gate)
            {
                return state.Made(System.IO.Path.GetDirectoryName(Path)!).Bind(_ => Write.Append(Path, entry));
            }
        }

        /// <summary>The run's whole answer, answer.json beside its log, which a cut answer names as full.</summary>
        public string Answer => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "answer.json");

        /// <summary>The whole answer written to <see cref="Answer"/>, the run's folder made as Add makes it: the full path written, or file.unwritable.</summary>
        public Result<string> WriteAnswer(string json) => state.Made(System.IO.Path.GetDirectoryName(Path)!).Bind(_ => Write.Text(Answer, json));
    }

    /// <summary>
    /// A reference's connection (§4 row 14, VALUES.md X1): env:NAME's variable or file:path's text, a relative path read from the repository
    /// root, parsed by SqlClient's own grammar (io/ConnectionString.cs); the caller's integrated identity when it names no other; dbchange
    /// as the application unless it names one. An error names the reference and quotes nothing it read.
    /// </summary>
    internal static Result<string> Connect(string subject, SecretReference reference, string repositoryRoot) => Read(subject, reference, repositoryRoot).Bind(read => read is not { } text
        ? new Error("connection.unresolved", subject + " resolves to nothing here.", "Set the variable, or write the file outside git, that " + reference + " names.")
        : Parsed(subject, reference, text).Bind(connection => connection.InitialCatalog.Length == 0
            ? new Error("connection.malformed", subject + " names no database; every read reads the database the connection names.",
                "Give the connection string an Initial Catalog, in the place " + reference + " names.")
            : Result.Ok(ConnectionString.WithDefaults(connection))));

    /// <summary>An environment's server as R15 reads it, a database named or not: null when its reference resolves to nothing here; an error when SqlClient reads nothing from it.</summary>
    internal static Result<ServerName?> DataSource(NamedEnvironment environment, string repositoryRoot) => Read(EnvironmentDatabase.Subject(environment), environment.Connection, repositoryRoot).Bind(read => read is not { } text
        ? Result.Ok<ServerName?>(null)
        : Parsed(EnvironmentDatabase.Subject(environment), environment.Connection, text).Map(connection => (ServerName?)ConnectionString.ServerOf(connection)));

    /// <summary>A reference's text as SqlClient's own grammar reads it; the error names the reference and quotes nothing it read.</summary>
    private static Result<SqlConnectionStringBuilder> Parsed(string subject, SecretReference reference, string text) =>
        ConnectionString.Parse(subject, text, "Correct the connection string in the place " + reference + " names.");

    /// <summary>
    /// What a reference names: the variable's value, or the file's text trimmed; null when the variable is unset or empty, when the
    /// file system reports that no file or folder is at the path, that a folder is, or that no file can have the path's name
    /// (<see cref="Absent"/>), or when the file holds only white space. A file,
    /// a relative path read from the repository root, is read only when git keeps it out of every commit, ignored or in no repository
    /// while the repository root is in one, and, where files carry a Unix mode, when its owner alone can read it; an error leads with
    /// <paramref name="subject"/>. git is asked about the file by the name its folder lists (<see cref="Listed"/>), and that name is the
    /// one read. Whatever the file system withholds is an error, never null, since the host of a file that may be there is unknown
    /// here and R15 must not leave the environment uncompared as it does one that resolves to nothing: a path whose attributes this
    /// identity cannot read, where File.Exists answers false as it does where no file is, is reference.inaccessible; a file whose
    /// folder it cannot list is reference.unlistable; and one it cannot read is reference.unreadable. The check covers the path the
    /// reference names, since git tracks paths: a hard link to a committed file, or a plain copy of one, under a folder .gitignore
    /// lists such as .dbchange/ is read, though the commit holds what it holds. The attempt, a refused one too, is recorded in the run's
    /// Reads.
    /// </summary>
    internal static Result<string?> Read(string subject, SecretReference reference, string repositoryRoot)
    {
        Reads.Record();
        return reference.Match(
            variable => Result.Ok(System.Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : null),
            file => System.IO.Path.Combine(repositoryRoot, file) is var path && File.Exists(path)
                ? Opened(() => Listed(subject, path), () => Unlistable(subject))
                    .Bind(listed => Opened(() => Kept(subject, repositoryRoot, listed).Map(kept => File.ReadAllText(kept).Trim() is { Length: > 0 } text ? text : null), () => Unreadable(subject)))
                : Absent(subject, path));
    }

    /// <summary>
    /// Null for a path File.Exists does not open, when File.GetAttributes says why: no such file (FileNotFoundException), no such
    /// folder on the way (DirectoryNotFoundException), a name Windows forbids in a file name, such as one holding ? * &lt; &gt; or |
    /// (IOException for ERROR_INVALID_NAME, HResult 0x8007007B), or a folder at the path. File.Exists also answers false for a file
    /// whose attributes this identity cannot read (on Windows, RA denied on the file and RD on its folder; on Linux and macOS, a folder
    /// on the path without search permission); File.GetAttributes then throws UnauthorizedAccessException, or another IOException for
    /// a path it cannot reach, and that is reference.inaccessible.
    /// </summary>
    private static Result<string?> Absent(string subject, string path)
    {
        try
        {
            File.GetAttributes(path);
            return Result.Ok<string?>(null);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException || (e is IOException && e.HResult == InvalidName))
        {
            return Result.Ok<string?>(null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Error("reference.inaccessible", subject + " names a path whose attributes this identity cannot read, or that it cannot reach,"
                + " so whether a file is there, and what it holds, is unknown here; it is not read.",
                "Grant this identity the right to list the file's folder and read the file's attributes (on Linux and macOS, search permission on every folder of the path), or move the file under a folder it can list, such as .dbchange/.");
        }
    }

    /// <summary>The HResult of the IOException .NET throws for Windows' ERROR_INVALID_NAME (123), a name no file on Windows can have.</summary>
    private const int InvalidName = unchecked((int)0x8007007B);

    /// <summary>A step on a file that exists; the error given when the file system refuses the step (IOException, UnauthorizedAccessException).</summary>
    private static Result<T> Opened<T>(Func<Result<T>> step, Func<Error> refused)
    {
        try
        {
            return step();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return refused();
        }
    }

    private static Error Unlistable(string subject) => new Error("reference.unlistable", subject + " is a file whose folder this identity cannot list,"
        + " so git cannot be asked about the file by the name the folder lists, and the file is not read.",
        "Grant this identity the right to list the file's folder, or move the file under a folder it can list, such as .dbchange/.");

    private static Error Unreadable(string subject) => new Error("reference.unreadable", subject + " is a file this identity cannot open for reading,"
        + " for want of the right to read it or while another program holds it open, so what it holds is unknown here.",
        "Grant this identity the right to read the file, and close any program that holds it open.");

    /// <summary>
    /// The refusal of a file a reference names whose Unix mode lets its group or other users read it, or null: Read asks it of the
    /// file's mode on Linux and macOS, and Windows keeps no such mode.
    /// </summary>
    public static Error? ReadableByOthers(string subject, UnixFileMode mode) => (mode & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0 ? null
        : new Error("reference.readable-by-others", subject + " is a file its group or other users can read (mode "
            + Convert.ToString((int)mode & 0b111_111_111, 8).PadLeft(4, '0') + "); a file holding a connection string is read by its owner alone.",
            "Run chmod 600 on the file, so its owner alone reads it.");

    /// <summary>
    /// The full path of the file <paramref name="path"/> opens, spelled as its folder lists it, following a symbolic link to its final
    /// target: git matches .gitignore and its index against that name, and Windows opens a file under other spellings too. On Windows,
    /// Path.GetFullPath drops trailing dots and spaces and expands an 8.3 short name, and the folder's listing gives the name's case
    /// on Windows and macOS. A spelling that names no entry of its folder, such as Windows' name::$DATA for a file's default data
    /// stream, is reference.unlisted, the file unread. Read asks it only of a path File.Exists opens; it is public because the register's
    /// refusal paths ask it directly on Linux and macOS, where name::$DATA opens no file.
    /// </summary>
    public static Result<string> Listed(string subject, string path) => Entry(path) is not { } entry ? Unlisted(subject)
        : entry.ResolveLinkTarget(returnFinalTarget: true) is not { } target ? entry.FullName
        : Entry(target.FullName) is { } final ? final.FullName
        : Unlisted(subject);

    /// <summary>The entry of the path's folder that the path names: the one of its exact name, else, where the file system opens a name whatever its case, the one of its name in another case.</summary>
    private static FileInfo? Entry(string path)
    {
        var named = new FileInfo(System.IO.Path.GetFullPath(path));
        var entries = named.Directory is { Exists: true } folder ? folder.GetFiles() : [];
        return entries.FirstOrDefault(e => e.Name == named.Name)
            ?? (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? entries.FirstOrDefault(e => string.Equals(e.Name, named.Name, StringComparison.OrdinalIgnoreCase)) : null);
    }

    private static Error Unlisted(string subject) => new Error("reference.unlisted", subject + " opens a file by a name its folder does not list, such as name::$DATA, a data stream;"
        + " git matches .gitignore and its index against the name the folder lists, so it cannot say whether a commit holds the file, and the file is not read.",
        "Write the path as dir or ls lists the file, in dbchange/environments.json.");

    /// <summary>
    /// The path of a file a file: reference names, when git keeps it out of every commit and no other user can read it; else the
    /// error, the file unread. git.failed and git.missing lead with <paramref name="subject"/>, then quote io/Git's own message.
    /// </summary>
    private static Result<string> Kept(string subject, string repositoryRoot, string path) => Git.HoldingOf(repositoryRoot, path).Match<Result<string>>(holding => holding switch
    {
        Git.Holding.Tracked => new Error("reference.tracked", subject + " is a file git tracks, so every clone of the repository holds what it holds; a file: reference names a file git keeps out of every commit.",
            "Run git rm --cached on the file, list it in .gitignore, and change the password it held, since the history keeps the commit."),
        Git.Holding.NotIgnored => new Error("reference.not-ignored", subject + " is a file git does not ignore, so the next git add commits it; a file: reference names a file git keeps out of every commit.",
            "List the file in .gitignore, or move it under a folder .gitignore lists, such as .dbchange/."),
        Git.Holding.RootInNoRepository => new Error("reference.no-repository", subject + " names a file, and the repository root " + repositoryRoot
            + " is in no git repository, so git cannot say whether a clone would commit the file; it is not read.",
            "Run dbchange in a clone of the SSDT repository, or give the reference as env:NAME."),
        Git.Holding.Ignored => OwnerOnly(subject, path),
        Git.Holding.InNoRepository => OwnerOnly(subject, path),
        _ => throw new System.Diagnostics.UnreachableException(),
    }, error => new Error(error.Code, subject + " cannot be checked against git: " + error.Message, error.Remedy));

    /// <summary>The path of a file git keeps out of every commit, when no other user can read it: on Linux and macOS by its mode; Windows keeps no such mode.</summary>
    private static Result<string> OwnerOnly(string subject, string path) =>
        !OperatingSystem.IsWindows() && ReadableByOthers(subject, File.GetUnixFileMode(path)) is { } readable ? readable : path;

    /// <summary>
    /// Whether this run has read a connection or other reference of a named environment (VALUES.md X2), whose text an exception's message
    /// can then quote. SqlServer.Read records each read, and every one goes through it: EnvironmentDatabase.Of, which SqlServer.Resolve calls for env:;
    /// R15's read of each environment's connection in io/LocalServer, which copy: and a new copy run; and a named environment's SQLCMD values.
    /// cli/Program.cs begins a run around each command and withholds an unexpected exception's message when the run holds a read. The
    /// record reaches the threads the run's work starts (it is an AsyncLocal); a read outside any run is recorded nowhere, no catch
    /// reading it.
    /// </summary>
    public static class Reads
    {
        private static readonly AsyncLocal<Run?> Current = new();

        /// <summary>A run begun on this thread, the current one until it is disposed, when the run it began inside is current again.</summary>
        public static Run Begin() => Current.Value = new Run(Current.Value);

        internal static void Record() => Current.Value?.Record();

        /// <summary>One command's record: whether it has read a named environment's reference.</summary>
        public sealed class Run : IDisposable
        {
            private readonly Run? outer;
            private int read;

            internal Run(Run? outer) => this.outer = outer;

            /// <summary>Whether a named environment's connection or other reference was read while this run was current.</summary>
            public bool NamedEnvironment => Volatile.Read(ref read) == 1;

            /// <summary>Recorded here and in each run this one began inside.</summary>
            internal void Record()
            {
                Interlocked.Exchange(ref read, 1);
                outer?.Record();
            }

            public void Dispose() => Current.Value = outer;
        }
    }

    private static Error NotADatabase(Target target) => new Error("target.not-a-database",
        target + " is read as a package, and a database is asked for here: env:<name> or copy:<name>.", "Name the database as env:<name> or copy:<name>.");

    /// <summary>
    /// What this identity may read of a database it reached: the database, where it holds VIEW DEFINITION, and whether it also holds VIEW
    /// ANY DEFINITION on the server. SQL Server hides from an identity without it every login but its own, and the other server-scoped
    /// objects, so the model read from the database holds none of them, which the note read.database-scope says.
    /// </summary>
    public sealed record Readable(Database Database, bool ServerScope)
    {
        /// <summary>The note reading the database as a database-scoped identity carries; none for an identity that holds VIEW ANY DEFINITION.</summary>
        public IEnumerable<Finding> Notes => ServerScope ? [] : [Finding.Note("read.database-scope", Database.Target.ToString(), Database.Target
            + " is read as an identity without VIEW ANY DEFINITION on the server, from which SQL Server hides the logins users map to and other server-scoped objects, so the model holds none of them.")];
    }

    /// <summary>The target, when it answers this identity with VIEW DEFINITION, and whether the identity reads the server's scope too: what a verb asks before it builds anything, so a denial arrives first.</summary>
    public static Result<Readable> Reach(Database target, QueryLog? log = null) => Reached(target, log);

    /// <summary>
    /// A copy's SQL Server (R1), which a claim on the copy records: the product version and the copy's compatibility level, read in one
    /// statement through <see cref="Query{T}"/>, and the digest of the image the dbchange-sql container runs, which Docker reports
    /// (LocalServer.Image). A named environment's server is read by S8, and nothing here reads it.
    /// </summary>
    public static Result<Server> ServerOf(Copy copy, QueryLog? log = null) =>
        Query(copy, new Statement("SQL Server", "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)), compatibility_level FROM sys.databases WHERE database_id = DB_ID();"), log,
                rows => rows is [[string version, byte level]] ? (Version: version, Level: (int)level) : (Version: (string?)null, Level: 0))
            .Bind(read => Server.Of(read.Version, read.Level, LocalServer.Image(copy)));

    /// <summary>
    /// Whether the target answers this identity with what reading it takes, before DacFx's own retries begin: a connection opens, and
    /// the identity holds VIEW DEFINITION there (§1 fact 2), whose absence is a denial (Msg 300, SQL Server's number for it); and, in the
    /// same statement, whether it holds VIEW ANY DEFINITION on the server.
    /// </summary>
    private static Result<Readable> Reached(Database target, QueryLog? log) =>
        Query(target, new Statement("VIEW DEFINITION", "SELECT HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'VIEW DEFINITION'), HAS_PERMS_BY_NAME(NULL, NULL, N'VIEW ANY DEFINITION');"), log,
                rows => rows is [[{ } database, var server]]
                    ? (Database: Convert.ToInt32(database, CultureInfo.InvariantCulture) == 1, Server: server is not null && Convert.ToInt32(server, CultureInfo.InvariantCulture) == 1)
                    : (Database: false, Server: false))
            .Bind(held => held.Database ? Result.Ok(new Readable(target, held.Server)) : target.ErrorOf(300, ""));

    /// <summary>
    /// A named environment's own SQLCMD values, which a plan against it sets over the profile's: each literal as the environments file gives it and
    /// each reference resolved in memory, recorded as a read of the environment's; a copy has none.
    /// </summary>
    internal static Result<IReadOnlyList<SqlCmdValue>> SqlCmdValues(Database target) => target is not EnvironmentDatabase named ? Result.Ok<IReadOnlyList<SqlCmdValue>>([])
        : Result.All(named.Environment.SqlCmd.Select(variable => variable.Match(
            literal => Result.Ok(new SqlCmdValue(variable.Name, literal, false)),
            reference => Read(named + "'s " + SqlCmdVariable.Placeholder(variable.Name.ToString()) + ", " + reference + ",", reference, named.Root).Bind(read => read is { } value
                ? Result.Ok(new SqlCmdValue(variable.Name, value, true))
                : new Error("sqlcmd.unresolved", named + "'s " + SqlCmdVariable.Placeholder(variable.Name.ToString()) + " names " + reference + ", which resolves to nothing here.",
                    "Set the variable, or write the file outside git, that " + reference + " names.")))));

    /// <summary>
    /// A value the allowlist admits the type of: an integer of any width, or NULL. Anything else is a defect in the allowlist,
    /// internal.answer-type, named by its type alone and never by the value; it once escaped the adapter as an exception.
    /// </summary>
    internal static Result<long?> Integer(object? value) => value switch
    {
        null => Result.Ok<long?>(null),
        int or long or short or byte => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        _ => new Error("internal.answer-type", "An aggregate query answered with a " + value.GetType().Name + ", a type no form of the allowlist yields; the value is withheld.",
            "Report this answer and the aggregate query's site to dbchange's maintainers: the allowlist admitted a query whose answer is not an integer."),
    };

    /// <summary>
    /// The aggregate-query allowlist, closed (WP 1.4): one SELECT whose outermost select list holds COUNT or COUNT_BIG of * or of DISTINCT a
    /// column, SUM(CASE WHEN … THEN 1 ELSE 0 END), MIN or MAX over LEN or DATALENGTH of a column, CASE WHEN EXISTS (…) THEN 1 ELSE 0 END
    /// or an integer literal; beneath it, names of one or two parts, TRY_ conversions, and the few functions held here. A boundary is a
    /// length or a literal, never read from the data: each predicate, in a join's ON as in a WHERE, reads at most one value from the data
    /// (a column, a length or an aggregate), save = or &lt;&gt; between two columns, an equi-join or an orphan check; and a subquery's
    /// select list carries only columns, literals and the answers above, so a derived column is never two values combined. Every other
    /// form is refused with where it stands and what it is, and none of the query's literals is quoted.
    /// </summary>
    private static class Allowlist
    {
        private const string Forms = "COUNT, COUNT_BIG, SUM(CASE WHEN … THEN 1 ELSE 0 END), MIN or MAX over LEN or DATALENGTH of a column, CASE WHEN EXISTS or an integer literal";

        private const string Boundary = "a boundary read from the data, two values the data holds set against each other; = or <> between two columns is the one comparison of two";

        private static readonly HashSet<string> Aggregates = new(StringComparer.OrdinalIgnoreCase) { "COUNT", "COUNT_BIG", "SUM", "MIN", "MAX", "AVG" };

        private static readonly HashSet<string> Scalars = new(StringComparer.OrdinalIgnoreCase) { "LEN", "DATALENGTH", "UPPER", "LOWER", "LTRIM", "RTRIM", "ISNULL", "ABS" };

        /// <summary>The first form the allowlist does not hold: where it stands, and what it is.</summary>
        private sealed record Offence(TSqlFragment At, string What);

        /// <summary>The one statement <paramref name="text"/> holds, when the allowlist admits it; a refusal names the line and the column of what it refuses.</summary>
        public static Result<TSqlStatement> Admitted(string text)
        {
            var parsed = TSql.Parse(text, out var errors);
            if (errors.Count > 0)
            {
                return new Error("aggregate-query.refused", string.Create(CultureInfo.InvariantCulture, $"The query does not parse at line {errors[0].Line}, column {errors[0].Column}."),
                    "Correct the query's syntax at that place.");
            }

            var statements = ((TSqlScript)parsed).Batches.SelectMany(b => b.Statements).ToList();
            return statements.Count != 1
                ? new Error("aggregate-query.refused", string.Create(CultureInfo.InvariantCulture, $"The query holds {statements.Count} statements; dbchange runs one statement at a time."),
                    "Split it into queries of one SELECT each.")
                : Admitted(statements[0], site: null);
        }

        /// <summary>
        /// A statement as a tree, when the allowlist admits it: what M2's builders make, checked without being written as text and read
        /// again. A tree built in code carries no line and column, so its refusal names <paramref name="site"/> and the kind of node the
        /// allowlist stops at instead.
        /// </summary>
        public static Result<TSqlStatement> Admitted(TSqlStatement statement, string? site) =>
            (statement is SelectStatement select ? Statement(select) : new Offence(statement, Kind(statement))) is not { } offence ? statement
            : new Error("aggregate-query.refused", offence.At.StartLine > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"The query is refused at line {offence.At.StartLine}, column {offence.At.StartColumn}: {offence.What}.")
                    : "The query " + site + " is refused at its " + Kind(offence.At) + ": " + offence.What + ".",
                "Rewrite it so its select list holds only " + Forms + ".");

        private static Offence? Statement(SelectStatement s) =>
            s.Into is not null ? new Offence(s.Into, "SELECT INTO")
            : s.WithCtesAndXmlNamespaces is not null ? new Offence(s.WithCtesAndXmlNamespaces, "a WITH clause")
            : s.ComputeClauses.Count > 0 ? new Offence(s.ComputeClauses[0], "COMPUTE")
            : s.OptimizerHints.Count > 0 ? new Offence(s.OptimizerHints[0], "a query hint")
            : s.On is not null ? new Offence(s.On, "ON a filegroup")
            : s.QueryExpression is QuerySpecification q ? Query(q, outermost: true)
            : new Offence(s.QueryExpression, Kind(s.QueryExpression));

        private static Offence? Query(QuerySpecification q, bool outermost) =>
            q.ForClause is not null ? new Offence(q.ForClause, "FOR XML, FOR JSON or FOR BROWSE")
            : q.OrderByClause is not null ? new Offence(q.OrderByClause, "ORDER BY")
            : q.OffsetClause is not null ? new Offence(q.OffsetClause, "OFFSET")
            : q.WindowClause is not null ? new Offence(q.WindowClause, "a WINDOW clause")
            : q.TopRowFilter is { } top && (outermost || top.Percent || top.WithTies || !Literal(top.Expression))
                ? new Offence(top, outermost ? "TOP in the outermost select" : "TOP of other than an integer literal")
            : outermost && q.UniqueRowFilter == UniqueRowFilter.Distinct ? new Offence(q, "DISTINCT in the outermost select")
            : First(q.SelectElements, e => (e, outermost) switch
            {
                (SelectScalarExpression x, true) => Answer(x.Expression),
                (SelectScalarExpression x, false) => Carried(x.Expression),
                (SelectStarExpression star, false) => star.Qualifier is null || star.Qualifier.Count <= 2 ? null : Parts(star, star.Qualifier.Count),
                (SelectStarExpression star, true) => new Offence(star, "a star in the outermost select"),
                _ => new Offence(e, "a variable assigned"),
            })
            ?? (q.FromClause is null ? null : First(q.FromClause.TableReferences, Table))
            ?? (q.WhereClause is not { } where ? null : where.Cursor is not null ? new Offence(where, "CURRENT OF") : Predicate(where.SearchCondition))
            ?? (q.GroupByClause is not { } group ? null : group.GroupByOption != GroupByOption.None || group.All ? new Offence(group, "GROUP BY ALL, ROLLUP or CUBE")
                : First(group.GroupingSpecifications, g => g is ExpressionGroupingSpecification { Expression: var x } && (Length(x) || (!outermost && x is ColumnReferenceExpression c && Column(c) is null))
                    ? null : new Offence(g, outermost ? "GROUP BY other than LEN or DATALENGTH of a column, which draws its groups from the data" : "GROUP BY other than a column or its length")))
            ?? (q.HavingClause is null ? null : Predicate(q.HavingClause.SearchCondition));

        /// <summary>An item of the outermost select list: one of the forms, each answering an integer.</summary>
        private static Offence? Answer(ScalarExpression x) => x switch
        {
            ParenthesisExpression p => Answer(p.Expression),
            IntegerLiteral or UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative, Expression: IntegerLiteral } =>
                long.TryParse(((x as UnaryExpression)?.Expression as IntegerLiteral ?? (IntegerLiteral)x).Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= int.MaxValue
                    ? null : new Offence(x, "an integer literal SQL Server types as numeric"),
            SearchedCaseExpression c when c.WhenClauses is [{ WhenExpression: ExistsPredicate exists }] => OneOrZero(c) ?? Subquery(exists.Subquery.QueryExpression),
            FunctionCall f when Aggregates.Contains(f.FunctionName.Value) => Aggregate(f),
            FunctionCall f => new Offence(f, f.FunctionName.Value.ToUpperInvariant() + ", which the outermost select list does not hold"),
            ColumnReferenceExpression { ColumnType: ColumnType.Regular } => new Offence(x, "a bare column"),
            _ => new Offence(x, Kind(x) + ", which answers other than an integer"),
        };

        /// <summary>The aggregates the allowlist holds: COUNT or COUNT_BIG of * or of DISTINCT a column; SUM(CASE WHEN … THEN 1 ELSE 0 END); MIN or MAX over LEN or DATALENGTH of a column.</summary>
        private static Offence? Aggregate(FunctionCall f)
        {
            var name = f.FunctionName.Value.ToUpperInvariant();
            return f.CallTarget is not null || f.OverClause is not null || f.WithinGroupClause is not null
                ? new Offence(f, name + " with OVER, WITHIN GROUP or a target")
                : (name, f.UniqueRowFilter, f.Parameters) switch
                {
                    ("COUNT" or "COUNT_BIG", UniqueRowFilter.NotSpecified, [ColumnReferenceExpression { ColumnType: ColumnType.Wildcard }]) => null,
                    ("COUNT" or "COUNT_BIG", UniqueRowFilter.Distinct, [ColumnReferenceExpression { ColumnType: ColumnType.Regular } c]) => Column(c),
                    ("COUNT" or "COUNT_BIG", _, _) => new Offence(f, name + " of other than * or DISTINCT a column"),
                    ("SUM", UniqueRowFilter.NotSpecified, [SearchedCaseExpression { WhenClauses: [var when] } c]) => OneOrZero(c) ?? Predicate(when.WhenExpression),
                    ("SUM", _, _) => new Offence(f, "SUM of other than CASE WHEN … THEN 1 ELSE 0 END"),
                    ("MIN" or "MAX", UniqueRowFilter.NotSpecified, [var length]) when Length(length) => null,
                    (_, _, [ColumnReferenceExpression { ColumnType: ColumnType.Regular }]) => new Offence(f, name + " over a bare column"),
                    _ => new Offence(f, name + " over other than LEN or DATALENGTH of a column"),
                };
        }

        /// <summary>A CASE whose one WHEN answers 1 and whose ELSE answers 0.</summary>
        private static Offence? OneOrZero(SearchedCaseExpression c) =>
            c.WhenClauses is [{ ThenExpression: IntegerLiteral { Value: "1" } }] && c.ElseExpression is IntegerLiteral { Value: "0" } ? null : new Offence(c, "a CASE answering other than 1 or 0");

        /// <summary>A predicate whose comparisons each read one value from the data, save = or &lt;&gt; of two columns: no row's value bounds another's, whether a join, a correlated EXISTS or arithmetic (ABS(x - y) + x - y = 0 is x &lt;= y) meets them.</summary>
        private static Offence? Predicate(BooleanExpression b) => b switch
        {
            BooleanBinaryExpression x => Predicate(x.FirstExpression) ?? Predicate(x.SecondExpression),
            BooleanNotExpression x => Predicate(x.Expression),
            BooleanParenthesisExpression x => Predicate(x.Expression),
            BooleanComparisonExpression x => Scalar(x.FirstExpression) ?? Scalar(x.SecondExpression)
                ?? (x.ComparisonType is BooleanComparisonType.Equals or BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation
                    && Bare(x.FirstExpression) && Bare(x.SecondExpression) ? null : Bounded(x, x.FirstExpression, x.SecondExpression)),
            BooleanIsNullExpression x => Scalar(x.Expression) ?? Bounded(x, x.Expression),
            BooleanTernaryExpression x => Scalar(x.FirstExpression) ?? Scalar(x.SecondExpression) ?? Scalar(x.ThirdExpression) ?? Bounded(x, x.FirstExpression, x.SecondExpression, x.ThirdExpression),
            LikePredicate x => Scalar(x.FirstExpression) ?? Scalar(x.SecondExpression) ?? (x.EscapeExpression is null ? null : Scalar(x.EscapeExpression))
                ?? Bounded(x, x.FirstExpression, x.SecondExpression, x.EscapeExpression),
            InPredicate x => Scalar(x.Expression) ?? (x.Subquery is { } s
                ? Subquery(s.QueryExpression) ?? (Bare(x.Expression) || !Data(x.Expression).Any() ? null : new Offence(x, Boundary))
                : First(x.Values, Scalar) ?? Bounded(x, [x.Expression, .. x.Values])),
            ExistsPredicate x => Subquery(x.Subquery.QueryExpression),
            _ => new Offence(b, Kind(b)),
        };

        /// <summary>A comparison's operands, which may read one value from the data between them and set it against literals alone.</summary>
        private static Offence? Bounded(TSqlFragment at, params ScalarExpression?[] operands) => operands.OfType<ScalarExpression>().SelectMany(Data).Skip(1).Any() ? new Offence(at, Boundary) : null;

        /// <summary>
        /// What an operand reads from the data: each column outside a length or an aggregate, each length, each aggregate; a CASE, what its
        /// THEN and ELSE read, its WHEN being a predicate of its own. A literal reads nothing, and any form not named here counts as one read.
        /// </summary>
        private static IEnumerable<ScalarExpression> Data(ScalarExpression x) => x switch
        {
            _ when Constant(x) => [],
            _ when Length(x) => [x],
            FunctionCall f when Aggregates.Contains(f.FunctionName.Value) => [x],
            UnaryExpression u => Data(u.Expression),
            BinaryExpression y => [.. Data(y.FirstExpression), .. Data(y.SecondExpression)],
            ParenthesisExpression p => Data(p.Expression),
            TryConvertCall t => [.. Data(t.Parameter), .. t.Style is null ? [] : Data(t.Style)],
            TryCastCall t => Data(t.Parameter),
            CoalesceExpression c => c.Expressions.SelectMany(Data),
            NullIfExpression n => [.. Data(n.FirstExpression), .. Data(n.SecondExpression)],
            SearchedCaseExpression s => [.. s.WhenClauses.SelectMany(w => Data(w.ThenExpression)), .. s.ElseExpression is null ? [] : Data(s.ElseExpression)],
            SimpleCaseExpression s => [.. Data(s.InputExpression), .. s.WhenClauses.SelectMany(w => Data(w.WhenExpression).Concat(Data(w.ThenExpression))),
                .. s.ElseExpression is null ? [] : Data(s.ElseExpression)],
            FunctionCall f => f.Parameters.SelectMany(Data),
            _ => [x],
        };

        /// <summary>An item of a subquery's select list: a column, a literal or one of the answers, so what an outer predicate reads as a column is a column's own value, a length or a count.</summary>
        private static Offence? Carried(ScalarExpression x) => x is ParenthesisExpression p ? Carried(p.Expression) : x is ColumnReferenceExpression c ? Column(c)
            : Constant(x) || Answer(x) is null ? null : new Offence(x, "a value computed in a subquery's select list, which an outer predicate would read as a column");

        /// <summary>A column as it stands, perhaps in parentheses: one side of an equi-join.</summary>
        private static bool Bare(ScalarExpression x) => x is ColumnReferenceExpression { ColumnType: ColumnType.Regular } || (x is ParenthesisExpression p && Bare(p.Expression));

        /// <summary>A literal of the kinds an aggregate query may write: a number, a string, a binary value or NULL.</summary>
        private static bool Constant(ScalarExpression x) => x is IntegerLiteral or NumericLiteral or RealLiteral or MoneyLiteral or StringLiteral or BinaryLiteral or NullLiteral;

        private static Offence? Scalar(ScalarExpression x) => x switch
        {
            ColumnReferenceExpression c => Column(c),
            _ when Constant(x) => null,
            UnaryExpression u => Scalar(u.Expression),
            BinaryExpression y => Scalar(y.FirstExpression) ?? Scalar(y.SecondExpression),
            ParenthesisExpression p => Scalar(p.Expression),
            TryConvertCall t => Scalar(t.Parameter) ?? (t.Style is null ? null : Scalar(t.Style)),
            TryCastCall t => Scalar(t.Parameter),
            CoalesceExpression c => First(c.Expressions, Scalar),
            NullIfExpression n => Scalar(n.FirstExpression) ?? Scalar(n.SecondExpression),
            SearchedCaseExpression s => First(s.WhenClauses, w => Predicate(w.WhenExpression) ?? Scalar(w.ThenExpression)) ?? (s.ElseExpression is null ? null : Scalar(s.ElseExpression)),
            SimpleCaseExpression s => Scalar(s.InputExpression) ?? First(s.WhenClauses, w => Scalar(w.WhenExpression) ?? Scalar(w.ThenExpression)) ?? (s.ElseExpression is null ? null : Scalar(s.ElseExpression)),
            FunctionCall f when Aggregates.Contains(f.FunctionName.Value) => Aggregate(f),
            FunctionCall f when Scalars.Contains(f.FunctionName.Value) && f.CallTarget is null && f.OverClause is null && f.UniqueRowFilter == UniqueRowFilter.NotSpecified => First(f.Parameters, Scalar),
            FunctionCall f => new Offence(f, (f.CallTarget is null ? f.FunctionName.Value.ToUpperInvariant() : "a function of the database's own") + ", which the allowlist does not hold"),
            ConvertCall or CastCall => new Offence(x, "CONVERT or CAST, which quotes the value it fails on; TRY_CONVERT and TRY_CAST do not"),
            ScalarSubquery => new Offence(x, "a subquery's value, a boundary read from the data"),
            VariableReference or GlobalVariableExpression => new Offence(x, "a variable"),
            _ => new Offence(x, Kind(x)),
        };

        private static Offence? Table(TableReference t) => t switch
        {
            NamedTableReference n => n.SchemaObject.ServerIdentifier is not null || n.SchemaObject.DatabaseIdentifier is not null ? Parts(n, n.SchemaObject.Count)
                : n.TableHints.Count > 0 ? new Offence(n.TableHints[0], "a table hint")
                : n.TableSampleClause is not null ? new Offence(n.TableSampleClause, "TABLESAMPLE")
                : n.TemporalClause is not null ? new Offence(n.TemporalClause, "FOR SYSTEM_TIME")
                : null,
            QualifiedJoin j => j.JoinHint != JoinHint.None ? new Offence(j, "a join hint") : Table(j.FirstTableReference) ?? Table(j.SecondTableReference) ?? Predicate(j.SearchCondition),
            UnqualifiedJoin j => j.UnqualifiedJoinType == UnqualifiedJoinType.CrossJoin ? Table(j.FirstTableReference) ?? Table(j.SecondTableReference) : new Offence(j, "CROSS APPLY or OUTER APPLY"),
            JoinParenthesisTableReference p => Table(p.Join),
            QueryDerivedTable d => Subquery(d.QueryExpression),
            _ => new Offence(t, Kind(t)),
        };

        private static Offence? Subquery(QueryExpression q) => q switch
        {
            QuerySpecification s => Query(s, outermost: false),
            QueryParenthesisExpression p => Subquery(p.QueryExpression),
            _ => new Offence(q, Kind(q)),
        };

        private static Offence? Column(ColumnReferenceExpression c) =>
            c.ColumnType != ColumnType.Regular ? new Offence(c, c.ColumnType == ColumnType.Wildcard ? "a star" : "a pseudo-column")
            : c.MultiPartIdentifier.Count <= 2 ? null : Parts(c, c.MultiPartIdentifier.Count);

        /// <summary>LEN or DATALENGTH of a column: a length, the one boundary the allowlist reads from a value.</summary>
        private static bool Length(ScalarExpression x) =>
            x is FunctionCall { CallTarget: null, OverClause: null, UniqueRowFilter: UniqueRowFilter.NotSpecified, Parameters: [ColumnReferenceExpression c] } f
            && (string.Equals(f.FunctionName.Value, "LEN", StringComparison.OrdinalIgnoreCase) || string.Equals(f.FunctionName.Value, "DATALENGTH", StringComparison.OrdinalIgnoreCase))
            && Column(c) is null;

        private static bool Literal(ScalarExpression x) => x is IntegerLiteral || x is ParenthesisExpression { Expression: IntegerLiteral };

        private static Offence Parts(TSqlFragment at, int parts) => new(at, string.Create(CultureInfo.InvariantCulture, $"a name of {parts} parts"));

        private static Offence? First<T>(IEnumerable<T> items, Func<T, Offence?> check) => items.Select(check).FirstOrDefault(o => o is not null);

        private static string Kind(TSqlFragment fragment) => fragment.GetType().Name;
    }
}
