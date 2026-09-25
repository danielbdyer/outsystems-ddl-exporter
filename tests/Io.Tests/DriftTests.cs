using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;
using Estate.Budgets.Tests;
using Estate.Kernel;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// estate check drift (WP 1.7, V3_ARCHITECTURE.md §8.8): the ref built and planned against the target under the pipeline's profile, exit 0
/// when the deploy report has no operation and 5 naming each object when it has; law 2′ (M1 exit 3), R13's stamp and window (exit 6),
/// R16's refusals (exit 7), and R14's log of the read-only principal (exit 8).
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class DriftTests(ScratchEstate estate) : IClassFixture<ScratchEstate>
{
    /// <summary>M1 exit 7's first half: a literal connection string where a target goes is exit 6, and no part of it is printed.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("check drift --target")]
    [InlineData("read --from")]
    public void A_literal_connection_string_as_a_target_is_exit_6_and_printed_nowhere(string verb)
    {
        var asked = verb.Split(' ');
        var (exit, output) = estate.Estate([.. asked, "Server=db;User ID=sa;Password=planted-7f3a", .. asked[0] == "check" ? ["--at", estate.Base] : Array.Empty<string>(), "--json"]);

        Assert.Equal(6, exit);
        Assert.Equal("connection.literal", (string?)JsonNode.Parse(output)!["findings"]![0]!["code"]);
        Assert.DoesNotContain("planted-7f3a", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=db", output, StringComparison.Ordinal);
    }

    /// <summary>M1 exit 6 through the verb: a ledger whose pin the committed engine is neither, nor the release before, is exit 6 before anything connects, the engine stamped.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_engine_outside_the_ledger_s_window_is_exit_6_before_anything_connects()
    {
        var root = Directory.CreateTempSubdirectory("estate-window-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "estate", "ledgers"));
            File.WriteAllText(Path.Combine(root, "estate", "ledgers", "toolchain.md"), "| Date | estate | Pinned DacFx | Release before |\n|---|---|---|---|\n| 2026-09-25 | 3.0.0 | 170.7.2 | 170.6.10 |\n");

            var (exit, output) = estate.EstateAt(root, "check", "drift", "--target", "copy:estate_nowhere_1_00000000", "--at", "main", "--json");

            var answer = JsonNode.Parse(output)!;
            ScratchEstate.Valid("estate.check.1.schema.json", answer);
            Assert.Equal(6, exit);
            Assert.Equal("toolchain.outside-window", (string?)answer["findings"]![0]!["code"]);
            Assert.Equal(("170.5.96", "170.7.2"), ((string?)answer["engine"]!["dacfx"], (string?)answer["engine"]!["pin"]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Law 2′ (M1 exit 3): the golden project published to a fresh copy converges, check drift exit 0; one column altered on the copy is exit 5 naming its table and the column.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Law", "2′ a published copy converges")]
    public async Task A_published_copy_converges_and_one_column_altered_on_it_is_exit_5_naming_it()
    {
        var copy = await Published();
        try
        {
            var (exit, output) = Drift("copy:" + copy.Name);
            Assert.True(exit == 0, output);
            Assert.StartsWith("copy:" + copy.Name + " matches " + estate.Base + "\n", output, StringComparison.Ordinal);

            await SqlServerFixture.ExecuteAsync(copy.Connection, "ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(300) NULL;");
            (exit, output) = Drift("copy:" + copy.Name);

            Assert.True(exit == 5, output);
            Assert.StartsWith("copy:" + copy.Name + " differs from " + estate.Base, output, StringComparison.Ordinal);
            Assert.Contains("- warn `drift.alter` Table [dbo].[Customer]: ", output, StringComparison.Ordinal);
            Assert.Equal(["- warn `drift.column` Column [dbo].[Customer].[Email]: Length 300 → 256, from the target to the repository."],
                output.Split('\n').Where(l => l.Contains("`drift.column`", StringComparison.Ordinal)));
        }
        finally
        {
            GitTests.Ok(Substrate.Drop(copy));
        }
    }

    /// <summary>
    /// R13's stamp (M1 exit 6): the receipt names the committed engine, the image's digest where the copy ran in the container, and UNPINNED
    /// while the ledger's row is; and, until S7 lands the Octopus step's profile, that its profile is not verified against it (§17 item 15).
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Every_receipt_names_its_engine()
    {
        var copy = await Published();
        try
        {
            var (exit, output) = Drift("copy:" + copy.Name, "--json");

            var answer = JsonNode.Parse(output)!;
            ScratchEstate.Valid("estate.check.1.schema.json", answer);
            Assert.Equal(0, exit);
            var receipt = answer["receipt"]!;
            var container = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ESTATE_SQL")) && File.Exists(Substrate.SqlEnv);
            Assert.Equal(("170.5.96", container ? Doctor.ImageDigest : null, "UNPINNED"),
                ((string?)receipt["engine"]!["dacfx"], (string?)receipt["engine"]!["sqlserver"], (string?)receipt["engine"]!["pin"]));
            Assert.Equal(("copy:" + copy.Name, "dataFacts", estate.Base), ((string?)receipt["where"], (string?)receipt["lacking"], (string?)answer["check"]!["commit"]));
            Assert.Equal(answer["engine"]!.ToJsonString(), receipt["engine"]!.ToJsonString());
            Assert.Contains(answer["findings"]!.AsArray(), f => (string?)f!["code"] == "engine.unpinned" && ((string?)f["message"])!.Contains("UNPINNED", StringComparison.Ordinal));
            Assert.Contains(answer["findings"]!.AsArray(), f => (string?)f!["code"] == "profile.unverified" && (string?)f["severity"] == "note"
                && ((string?)f["message"])!.Contains("profile not verified against the Octopus step", StringComparison.Ordinal));
        }
        finally
        {
            GitTests.Ok(Substrate.Drop(copy));
        }
    }

    /// <summary>M1 exit 7's second half (R16): a denied login prints one sentence naming the environment and saying a lead's prediction will appear on the pull request, before anything builds.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_denied_login_prints_one_sentence_naming_the_environment_and_a_lead_s_prediction()
    {
        var server = new SqlConnectionStringBuilder(await SqlServerFixture.ServerAsync());
        var connection = Path.Combine(Path.GetDirectoryName(estate.Root)!, "denied.connection");
        File.WriteAllText(connection, new SqlConnectionStringBuilder
        {
            DataSource = server.DataSource, InitialCatalog = "master", UserID = "estate_nobody_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)), Password = "Denied1!planted",
            TrustServerCertificate = true, Pooling = false,
        }.ConnectionString);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(connection, UnixFileMode.UserRead | UnixFileMode.UserWrite);   // io/SqlServer refuses a connection file others can read
        }

        var (exit, output) = estate.EstateAt(estate.Named(("uat", connection)), "check", "drift", "--target", "env:uat", "--at", estate.Base);

        Assert.Equal(4, exit);
        var sentence = output.Split('\n')[0];
        Assert.Equal("env:uat refused this identity (Msg 18456, SQL Server's message withheld); a lead's prediction will appear on the pull request.", sentence);
        Assert.DoesNotContain("planted", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// M1 exit 8 (R14): an Extended Events session on the read-only principal's login, made by the fixture's admin identity, records every
    /// batch and call that login sends through a full check drift of a named environment; none is DML, DDL or an EXEC, but the one registry
    /// read of the default file paths DacFx's Script sends, which the exit names (<see cref="DacFxReadsDefaultPath"/>).
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task The_read_only_principal_sends_no_DML_no_DDL_and_no_EXEC_through_a_full_check_drift()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        var dacpac = GitTests.Ok(Ssdt.Build(GitTests.Ok(Git.At(estate.Root, estate.Base)), "project/SampleCatalog.sqlproj", estate.Tool.Folder, Path.Combine(estate.Root, ".estate", "build"))).Path;
        GoldenProject.Publish(dacpac, database, DacProfile.Load(Path.Combine(estate.Root, ScratchEstate.Profile)).DeployOptions);
        var reader = await ReadOnlyPrincipal.CreateAsync(database);
        var master = await SqlServerFixture.ServerAsync();
        var session = "estate_xe_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var only = "WHERE ([sqlserver].[server_principal_name] = N'" + reader.Login + "')";
        await SqlServerFixture.ExecuteAsync(master, "CREATE EVENT SESSION [" + session + "] ON SERVER ADD EVENT sqlserver.sql_batch_completed(" + only + "), "
            + "ADD EVENT sqlserver.rpc_completed(" + only + ") ADD TARGET package0.event_file(SET filename = N'" + session + ".xel') "
            + "WITH (MAX_DISPATCH_LATENCY = 1 SECONDS, EVENT_RETENTION_MODE = NO_EVENT_LOSS); ALTER EVENT SESSION [" + session + "] ON SERVER STATE = START;");
        try
        {
            var file = await Scalar(master, "SELECT CAST(t.target_data AS xml).value('(EventFileTarget/File/@name)[1]', 'nvarchar(400)') FROM sys.dm_xe_session_targets t "
                + "JOIN sys.dm_xe_sessions s ON s.address = t.event_session_address WHERE s.name = @name AND t.target_name = N'event_file';", session);
            var root = estate.Named(("dev", Path.Combine(Repository.Root, reader.Reference["file:".Length..])));

            var (exit, output) = estate.EstateAt(root, "check", "drift", "--target", "env:dev", "--at", estate.Base);
            await SqlServerFixture.ExecuteAsync(master, "ALTER EVENT SESSION [" + session + "] ON SERVER STATE = STOP;");

            Assert.True(exit == 0, output);
            Assert.StartsWith("env:dev matches " + estate.Base, output, StringComparison.Ordinal);
            var sent = await Events(master, file[..file.LastIndexOf('_')] + "*.xel");
            Assert.Contains(sent, s => s.Contains("HAS_PERMS_BY_NAME", StringComparison.Ordinal));   // the session saw the run: estate's own first statement
            var writes = sent.SelectMany(Writes).ToList();
            Assert.True(writes.Count == 0, string.Join("\n----\n", writes));
        }
        finally
        {
            await SqlServerFixture.ExecuteAsync(master, "IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'" + session + "') DROP EVENT SESSION [" + session + "] ON SERVER;");
        }
    }

    /// <summary>The principal test's reading of what a login sent, as minimal pairs: each write it finds, beside the read it passes.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("SELECT name FROM sys.tables;", false)]
    [InlineData("SELECT HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'VIEW DEFINITION');", false)]
    [InlineData("exec sp_executesql N'SELECT 1 WHERE @p = 1', N'@p int', @p = 1", false)]
    [InlineData("DECLARE @filepath nvarchar(260); EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE',N'Software\\Microsoft\\MSSQLServer\\MSSQLServer',N'DefaultLog', @filepath output, 'no_output'", false)]
    [InlineData("exec sp_executesql N'DELETE dbo.Customer WHERE Id = @p', N'@p int', @p = 1", true)]
    [InlineData("DECLARE @v nvarchar(260); EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE',N'Software\\Microsoft\\MSSQLServer\\MSSQLServer',N'BackupDirectory', @v output, 'no_output'", true)]
    [InlineData("EXEC dbo.usp_Anything;", true)]
    [InlineData("IF 1 = 1 BEGIN UPDATE dbo.Customer SET Email = NULL; END", true)]
    [InlineData("SELECT * INTO #kept FROM dbo.Customer;", true)]
    [InlineData("CREATE TABLE #t (Id int);", true)]
    [InlineData("GRANT SELECT ON dbo.Customer TO public;", true)]
    public void The_principal_test_finds_every_write_and_passes_every_read(string sent, bool writes) => Assert.Equal(writes, Writes(sent).Any());

    /// <summary>
    /// The one EXEC DacFx's Script sends itself, measured here: master.dbo.xp_instance_regread of the instance's DefaultData or DefaultLog
    /// registry value, a read of the default file paths its script's header sets, with exactly the arguments DacFx gives. DacFx's own
    /// catalog queries are not estate's to allowlist (V3_MILESTONES.md §7, Watch for); this session is what checks them. M1 exit 8 names
    /// this one EXEC, and DECISIONS.md records it.
    /// </summary>
    private static bool DacFxReadsDefaultPath(ExecutableProcedureReference call) =>
        call.ProcedureReference?.ProcedureReference?.Name is { DatabaseIdentifier.Value: "master", SchemaIdentifier.Value: "dbo", BaseIdentifier.Value: "xp_instance_regread" }
        && call.Parameters.Select(p => (p.ParameterValue as StringLiteral)?.Value ?? (p.ParameterValue as VariableReference)?.Name + (p.IsOutput ? " OUTPUT" : "")).ToArray() is
            ["HKEY_LOCAL_MACHINE", @"Software\Microsoft\MSSQLServer\MSSQLServer", "DefaultData" or "DefaultLog", "@filepath OUTPUT", "no_output"];

    /// <summary>What a batch or a call does beyond reading: each DML, DDL or EXEC statement in it, sp_executesql read through to the statement it carries.</summary>
    private static IEnumerable<string> Writes(string sent)
    {
        var script = new TSql160Parser(initialQuotedIdentifiers: true).Parse(new StringReader(sent), out var errors);
        if (errors.Count > 0)
        {
            return ["unparsed: " + sent];
        }

        var statements = new Statements();
        script.Accept(statements);
        return statements.Found.SelectMany(s => s switch
        {
            ExecuteStatement { ExecuteSpecification.ExecutableEntity: ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value: var name } call }
                when string.Equals(name, "sp_executesql", StringComparison.OrdinalIgnoreCase) && call.Parameters is [{ ParameterValue: StringLiteral inner }, ..] => Writes(inner.Value),
            ExecuteStatement { ExecuteSpecification.ExecutableEntity: ExecutableProcedureReference call } when DacFxReadsDefaultPath(call) => [],
            ExecuteStatement or InsertStatement or UpdateStatement or DeleteStatement or MergeStatement or TruncateTableStatement or BulkInsertStatement => [s.GetType().Name + ": " + sent],
            SelectStatement { Into: not null } => ["SELECT INTO: " + sent],
            _ when s.GetType().Name.StartsWith("Create", StringComparison.Ordinal) || s.GetType().Name.StartsWith("Alter", StringComparison.Ordinal)
                || s.GetType().Name.StartsWith("Drop", StringComparison.Ordinal) || s is GrantStatement or RevokeStatement or DenyStatement => [s.GetType().Name + ": " + sent],
            _ => [],
        });
    }

    /// <summary>Every statement a script holds, nested ones included.</summary>
    private sealed class Statements : TSqlFragmentVisitor
    {
        public List<TSqlStatement> Found { get; } = [];

        public override void Visit(TSqlStatement node) => Found.Add(node);
    }

    /// <summary>Each batch's text and each call's statement the session wrote to its files.</summary>
    private static async Task<List<string>> Events(string master, string files)
    {
        var sent = new List<string>();
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var read = new SqlCommand("SELECT CAST(event_data AS nvarchar(max)) FROM sys.fn_xe_file_target_read_file(@files, NULL, NULL, NULL);", connection);
        read.Parameters.Add(new SqlParameter("@files", System.Data.SqlDbType.NVarChar, 400) { Value = files });
        await using var events = await read.ExecuteReaderAsync();
        while (await events.ReadAsync())
        {
            var data = XElement.Parse(events.GetString(0)).Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
            sent.Add(data.GetValueOrDefault("batch_text") ?? data.GetValueOrDefault("statement") ?? "");
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

    /// <summary>A fresh copy on the run's substrate, registered under the estate's root, with the golden project published to it under the pipeline's profile.</summary>
    private async Task<SqlServer.Copy> Published()
    {
        var copy = GitTests.Ok(Substrate.Create(estate.Root, await SqlServerFixture.ServerAsync()));
        var dacpac = GitTests.Ok(Ssdt.Build(GitTests.Ok(Git.At(estate.Root, estate.Base)), "project/SampleCatalog.sqlproj", estate.Tool.Folder, Path.Combine(estate.Root, ".estate", "build"))).Path;
        GitTests.Ok(copy.Publish(dacpac, GitTests.Ok(Profiles.Load(Path.Combine(estate.Root, ScratchEstate.Profile)))));
        return copy;
    }

    private (int Exit, string Output) Drift(string target, params string[] more) =>
        estate.Estate(["check", "drift", "--target", target, "--at", estate.Base, "--profile", ScratchEstate.Profile, .. more]);
}
