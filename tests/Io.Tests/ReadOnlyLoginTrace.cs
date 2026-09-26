using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// An Extended Events session on one login, made by the fixture's admin identity: every batch (sql_batch_completed) and call
/// (rpc_completed) the login sends while it runs, written to a file and read back when it stops, so a test can hold a read-only
/// principal to sending no DML, no DDL and no EXEC (R14). Disposing it drops the session.
/// </summary>
public sealed class ReadOnlyLoginTrace : IAsyncDisposable
{
    private readonly string master;
    private readonly string session;
    private readonly string files;

    private ReadOnlyLoginTrace(string master, string session, string files) => (this.master, this.session, this.files) = (master, session, files);

    /// <summary>One batch or call the login sent: the event's name, the procedure called (for a call), and the text as the event holds it.</summary>
    public sealed record Sent(string Event, string Object, string Text);

    /// <summary>A session on <paramref name="login"/>, started.</summary>
    public static async Task<ReadOnlyLoginTrace> StartAsync(string login)
    {
        var master = await SqlServerFixture.ServerAsync();
        var session = "dbchange_xe_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var only = "WHERE ([sqlserver].[server_principal_name] = N'" + login + "')";
        await SqlServerFixture.ExecuteAsync(master, "CREATE EVENT SESSION [" + session + "] ON SERVER ADD EVENT sqlserver.sql_batch_completed(" + only + "), "
            + "ADD EVENT sqlserver.rpc_completed(" + only + ") ADD TARGET package0.event_file(SET filename = N'" + session + ".xel') "
            + "WITH (MAX_DISPATCH_LATENCY = 1 SECONDS, EVENT_RETENTION_MODE = NO_EVENT_LOSS); ALTER EVENT SESSION [" + session + "] ON SERVER STATE = START;");
        var file = await Scalar(master, "SELECT CAST(t.target_data AS xml).value('(EventFileTarget/File/@name)[1]', 'nvarchar(400)') FROM sys.dm_xe_session_targets t "
            + "JOIN sys.dm_xe_sessions s ON s.address = t.event_session_address WHERE s.name = @name AND t.target_name = N'event_file';", session);
        return new ReadOnlyLoginTrace(master, session, file[..file.LastIndexOf('_')] + "*.xel");
    }

    /// <summary>The session stopped, and what the login sent while it ran.</summary>
    public async Task<IReadOnlyList<Sent>> StopAsync()
    {
        await SqlServerFixture.ExecuteAsync(master, "ALTER EVENT SESSION [" + session + "] ON SERVER STATE = STOP;");
        return await Events(master, files);
    }

    public async ValueTask DisposeAsync() =>
        await SqlServerFixture.ExecuteAsync(master, "IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'" + session + "') DROP EVENT SESSION [" + session + "] ON SERVER;");

    /// <summary>Whether SQL Server masked a statement's text in the event: an asterisk, a lower-case word, then dashes to the end.</summary>
    public static bool Masked(string text) => System.Text.RegularExpressions.Regex.IsMatch(text, @"\A\*[a-z_]+-+\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// What a batch or a call does beyond reading: each DML, DDL or EXEC statement in it, sp_executesql read through to the statement it
    /// carries. A text ScriptDom cannot parse is read token by token, and each keyword that starts a write counts: the Windows runner's
    /// LocalDB records DacFx's catalog query cut off after 500,000 characters (CI run 36202422744), and nothing past the cut is read.
    /// </summary>
    public static IEnumerable<string> Writes(string sent)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var script = parser.Parse(new StringReader(sent), out var errors);
        if (errors.Count > 0)
        {
            return parser.GetTokenStream(new StringReader(sent), out _).Where(t => WriteKeywords.Contains(t.TokenType))
                .Select(t => t.TokenType + " in a text that does not parse: " + sent[..Math.Min(sent.Length, 200)]);
        }

        var statements = new Statements();
        script.Accept(statements);
        return statements.Found.SelectMany(s => s switch
        {
            ExecuteStatement { ExecuteSpecification.ExecutableEntity: ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value: var name } call }
                when string.Equals(name, "sp_executesql", StringComparison.OrdinalIgnoreCase) && call.Parameters is [{ ParameterValue: StringLiteral inner }, ..] => Writes(inner.Value),
            ExecuteStatement or InsertStatement or UpdateStatement or DeleteStatement or MergeStatement or TruncateTableStatement or BulkInsertStatement => [s.GetType().Name + ": " + sent],
            SelectStatement { Into: not null } => ["SELECT INTO: " + sent],
            _ when s.GetType().Name.StartsWith("Create", StringComparison.Ordinal) || s.GetType().Name.StartsWith("Alter", StringComparison.Ordinal)
                || s.GetType().Name.StartsWith("Drop", StringComparison.Ordinal) || s is GrantStatement or RevokeStatement or DenyStatement => [s.GetType().Name + ": " + sent],
            _ => [],
        });
    }

    /// <summary>The keywords that start a write, or a SELECT … INTO, in a text read token by token.</summary>
    private static readonly HashSet<TSqlTokenType> WriteKeywords =
    [
        TSqlTokenType.Insert, TSqlTokenType.Update, TSqlTokenType.Delete, TSqlTokenType.Merge, TSqlTokenType.Truncate, TSqlTokenType.Bulk,
        TSqlTokenType.Into, TSqlTokenType.Create, TSqlTokenType.Alter, TSqlTokenType.Drop, TSqlTokenType.Exec, TSqlTokenType.Execute,
        TSqlTokenType.Grant, TSqlTokenType.Revoke, TSqlTokenType.Deny,
    ];

    /// <summary>Every statement a script holds, nested ones included.</summary>
    private sealed class Statements : TSqlFragmentVisitor
    {
        public List<TSqlStatement> Found { get; } = [];

        public override void Visit(TSqlStatement node) => Found.Add(node);
    }

    /// <summary>Each batch's text and each call's statement the session wrote to its files, with the event's name and, for a call, the procedure called.</summary>
    private static async Task<IReadOnlyList<Sent>> Events(string master, string files)
    {
        var sent = new List<Sent>();
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var read = new SqlCommand("SELECT CAST(event_data AS nvarchar(max)) FROM sys.fn_xe_file_target_read_file(@files, NULL, NULL, NULL);", connection);
        read.Parameters.Add(new SqlParameter("@files", System.Data.SqlDbType.NVarChar, 400) { Value = files });
        await using var events = await read.ExecuteReaderAsync();
        while (await events.ReadAsync())
        {
            var @event = XElement.Parse(events.GetString(0));
            var data = @event.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
            sent.Add(new Sent((string?)@event.Attribute("name") ?? "", data.GetValueOrDefault("object_name") ?? "", data.GetValueOrDefault("batch_text") ?? data.GetValueOrDefault("statement") ?? ""));
        }

        return sent;
    }

    private static async Task<string> Scalar(string master, string sql, string name)
    {
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 128) { Value = name });
        return (string)(await command.ExecuteScalarAsync())!;
    }
}

/// <summary>ReadOnlyLoginTrace's reading of the texts a login sends.</summary>
public sealed class ReadOnlyLoginTraceTests
{
    /// <summary>The trace's reading of what a login sent, as minimal pairs: each write it finds, beside the read it passes.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("SELECT name FROM sys.tables;", false)]
    [InlineData("SELECT HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'VIEW DEFINITION');", false)]
    [InlineData("exec sp_executesql N'SELECT 1 WHERE @p = 1', N'@p int', @p = 1", false)]
    [InlineData("DECLARE @filepath nvarchar(260); EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE',N'Software\\Microsoft\\MSSQLServer\\MSSQLServer',N'DefaultLog', @filepath output, 'no_output'", true)]
    [InlineData("exec sp_executesql N'DELETE dbo.Customer WHERE Id = @p', N'@p int', @p = 1", true)]
    [InlineData("EXEC dbo.usp_Anything;", true)]
    [InlineData("IF 1 = 1 BEGIN UPDATE dbo.Customer SET Email = NULL; END", true)]
    [InlineData("SELECT * INTO #kept FROM dbo.Customer;", true)]
    [InlineData("CREATE TABLE #t (Id int);", true)]
    [InlineData("GRANT SELECT ON dbo.Customer TO public;", true)]
    [InlineData("SELECT [is_merge_published], create_date FROM sys.databases OPTION (USE HINT('FORCE_LEGACY_CARDINALI", false)]
    [InlineData("SELECT name FROM sys.tables; UPDATE dbo.Customer SET Email = NULL; SELECT name FROM sys.tables WHERE name = N'cut", true)]
    public void The_trace_finds_every_write_and_passes_every_read(string sent, bool writes) => Assert.Equal(writes, ReadOnlyLoginTrace.Writes(sent).Any());

    /// <summary>The form SQL Server gives a masked text, an asterisk, the word it matched and dashes, beside texts that only resemble it.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("*encrypt------------------------------", true)]
    [InlineData("*password----------", true)]
    [InlineData("*encrypt", false)]
    [InlineData("SELECT '*encrypt----' AS masked;", false)]
    [InlineData("", false)]
    public void The_trace_reads_a_masked_text_only_in_SQL_Server_s_form(string sent, bool masked) => Assert.Equal(masked, ReadOnlyLoginTrace.Masked(sent));
}
