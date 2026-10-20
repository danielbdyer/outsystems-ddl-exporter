using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using DbChange.Kernel;
using Microsoft.Data.SqlClient;

namespace DbChange.Io;

public static partial class SqlServer
{
    /// <summary>
    /// How long SQL Server may run a statement dbchange sends before SqlClient cancels it: SqlClient's own default for a command, named.
    /// </summary>
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long SQL Server may run an aggregate query before SqlClient cancels it: the command timeout.
    /// An aggregate query reads each row of a table once, and thirty seconds covers a scan of the environments' largest tables that S3 and
    /// S8 have not yet measured; a query past it is measured as timed out, not as a server that does not answer (finding ARCH-13). The
    /// profile verb of M3 revisits the figure with the row counts S8 reports; no key of the environments file and no flag sets it before
    /// then.
    /// </summary>
    internal static readonly TimeSpan AggregateQueryTimeout = CommandTimeout;

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
    /// The first statement this process sends a copy is sent once more when the local server does not answer (LocalServer.FirstConnect).
    /// </summary>
    internal static Result<T> Query<T>(Database target, Statement statement, QueryLog log,
        Func<IReadOnlyList<IReadOnlyList<object?>>, T> answer,
        Func<StatementFailure, T>? failed = null) =>
        target is Copy
            ? LocalServer.FirstConnect.OfProcess.Attempt(() => Sent(target, statement, log, answer, failed), LocalServer.FirstConnect.Pause)
            : Sent(target, statement, log, answer, failed);

    private static Result<T> Sent<T>(Database target, Statement statement, QueryLog log,
        Func<IReadOnlyList<IReadOnlyList<object?>>, T> answer, Func<StatementFailure, T>? failed)
    {
        var opened = false;
        var logged = string.Concat(statement.Parameters.Select(p =>
            "DECLARE " + p.Name + " nvarchar(4000) = N'" + p.Value.Replace("'", "''", StringComparison.Ordinal) + "';\n")) + statement.Text;
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
            return log.Add(target, statement.Site, logged, outcome).Map(_ => answer(rows));
        }
        catch (Exception e) when (e is InvalidOperationException || Carries(e))
        {
            if (failed is not null && target.FailedStatement(e, opened) is { } statementFailure)
            {
                var unlogged = log.Add(target, statement.Site, logged, statementFailure.TimedOut
                    ? "timed out after " + statement.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s"
                    : "failed, Msg " + statementFailure.Number.ToString(CultureInfo.InvariantCulture)) as Result<string>.Failed;
                return unlogged is null ? failed(statementFailure) : unlogged.Error;
            }

            var error = target.ErrorOf(e, opened);
            return opened
                && log.Add(target, statement.Site, logged, "failed, " + error.Code) is Result<string>.Failed { Error: var unwritten }
                ? unwritten : error;
        }
    }

    /// <summary>
    /// A run's log of every statement dbchange sends through <see cref="Query{T}"/>, .dbchange/runs/&lt;id&gt;/queries.log: each aggregate
    /// query; the VIEW DEFINITION check Reach sends ahead of an extract, whose catalog queries are DacFx's to answer for; and a copy's
    /// CREATE and DROP DATABASE. Per statement: the time, the target, the site and the row count, the failure's number or code, or the
    /// timeout, then the statement and GO, so the log runs as a script. It holds no value a statement read. Each entry is appended and
    /// flushed to the file through io/Write.Append as it is made, so a run's statements cost their own bytes once (finding ARCH-08), and a
    /// reader following the file sees each.
    /// </summary>
    public sealed class QueryLog
    {
        private readonly Lock gate = new();
        private readonly LocalState state;
        private bool made;

        private QueryLog(LocalState state, string path) => (this.state, Path) = (state, path);

        public string Path { get; }

        /// <summary>Whether the log holds an entry: whether the run sent SQL Server a statement of its own.</summary>
        public bool Written { get; private set; }

        /// <summary>
        /// A new run's log under the repository root, named for the time, the process and a random suffix, so two runs never share one; its
        /// folder is made with its first write.
        /// </summary>
        public static QueryLog Start(string repositoryRoot)
        {
            var state = new LocalState(repositoryRoot);
            return new(state, System.IO.Path.Combine(state.Runs,
                DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-"
                + System.Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "-"
                + Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant(), "queries.log"));
        }

        /// <summary>
        /// The entry appended, the run's folder made first through LocalState, so .dbchange/.gitignore stands beside it: the log's path, or
        /// file.unwritable naming why, which Query answers in place of the statement's result, since R14's record of every statement is not
        /// optional.
        /// </summary>
        internal Result<string> Add(Database target, string site, string statement, string outcome)
        {
            var entry = "-- " + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) + " " + target.Target
                + " " + site + ": " + outcome + "\n"
                + statement + "\nGO\n";
            lock (gate)
            {
                var appended = Folder().Bind(_ => Write.Append(Path, entry));
                Written |= appended is Result<string>.Ok;
                return appended;
            }
        }

        /// <summary>The run's whole answer, answer.json beside its log, which a cut answer names as full.</summary>
        public string Answer => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "answer.json");

        /// <summary>
        /// The whole answer written to <see cref="Answer"/>, the run's folder made as Add makes it: the full path written, or
        /// file.unwritable.
        /// </summary>
        public Result<string> WriteAnswer(string json)
        {
            lock (gate)
            {
                return Folder().Bind(_ => Write.Text(Answer, json));
            }
        }

        /// <summary>The run's folder, made through LocalState on the run's first write alone, which also prunes the oldest runs.</summary>
        private Result<string> Folder()
        {
            var folder = System.IO.Path.GetDirectoryName(Path)!;
            return made ? folder : state.Made(folder).Map(madeNow =>
            {
                made = true;
                return madeNow;
            });
        }
    }
}
