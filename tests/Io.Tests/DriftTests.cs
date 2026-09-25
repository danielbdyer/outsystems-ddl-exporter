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

    /// <summary>Law 2′ (M1 exit 3): the golden project published to a fresh copy matches it, check drift exit 0; one column altered on the copy is exit 5 naming its table and the column.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Law", "2′ a published copy matches its package")]
    public async Task A_published_copy_matches_its_package_and_one_column_altered_on_it_is_exit_5_naming_it()
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
            Assert.Contains("- warning `drift.alter` Table [dbo].[Customer]: ", output, StringComparison.Ordinal);
            Assert.Equal(["- warning `drift.column` Column [dbo].[Customer].[Email]: Length 300 → 256, from the target to the repository."],
                output.Split('\n').Where(l => l.Contains("`drift.column`", StringComparison.Ordinal)));
        }
        finally
        {
            GitTests.Ok(ScratchServer.Drop(copy));
        }
    }

    /// <summary>
    /// Decision 2.26's measurement (O2): the golden project built under a case-insensitive and under a case-sensitive collation, each
    /// published to a copy created under that collation, then dbo.Customer renamed CUSTOMER on each with sp_rename, which sys.tables
    /// confirms in a binary comparison. DacServices.Script of the package against its copy plans nothing under either collation: DacFx
    /// matches object names ignoring case even where the database reads [dbo].[Customer] and [dbo].[CUSTOMER] as two names, so a
    /// case-only rename on a case-sensitive database is a difference no deploy plan will reconcile. estate diff from the copy to the
    /// package reads names under the copy's collation: one case-only pair with its note under the case-insensitive collation, and the
    /// table dropped and created under the case-sensitive one. check drift builds the golden ref, a case-insensitive package: against
    /// the case-insensitive copy it exits 0, and against the case-sensitive copy DacFx refuses the plan (SQL72030, a case-insensitive
    /// model deployed to a case-sensitive target), dacfx.failed at exit 6.
    /// </summary>
    [Theory]
    [Trait("Category", "fixture")]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", true, 0)]
    [InlineData("Latin1_General_CS_AS", false, 6)]
    public async Task A_case_only_rename_of_a_table_on_a_copy_plans_nothing_under_either_collation_and_diff_reads_it_under_the_copy_s_collation(string collation, bool caseInsensitive, int driftExit)
    {
        var (copy, dacpac, profile) = await Published(collation);
        try
        {
            await SqlServerFixture.ExecuteAsync(copy.Connection, "EXEC sp_rename 'dbo.Customer', 'CUSTOMER';");
            Assert.Equal(1, await SqlServerFixture.ScalarAsync(copy.Connection, "SELECT COUNT(*) FROM sys.databases WHERE name = @name AND collation_name = N'" + collation + "';", copy.Name.ToString()));
            Assert.Equal((1, 0), (
                await SqlServerFixture.ScalarAsync(copy.Connection, "SELECT COUNT(*) FROM sys.tables WHERE name COLLATE Latin1_General_BIN2 = N'CUSTOMER';"),
                await SqlServerFixture.ScalarAsync(copy.Connection, "SELECT COUNT(*) FROM sys.tables WHERE name COLLATE Latin1_General_BIN2 = N'Customer';")));

            var plan = GitTests.Ok(SqlServer.Plan(dacpac, copy, profile));
            var (exit, output) = Drift("copy:" + copy.Name);
            var (diffExit, diffOutput) = estate.Estate("diff", "--from", "copy:" + copy.Name, "--to", "dacpac:" + dacpac, "--json");

            Console.WriteLine(collation + ": " + (plan.IsEmpty ? "no operation" : string.Join("; ", plan.Items.Select(i => i.Operation + " " + i.Type + " " + i.Name))));
            Assert.True(plan.IsEmpty, collation + " planned:\n" + plan.Report);
            Assert.True(exit == driftExit, output);
            if (driftExit == 6)
            {
                Assert.Contains("`dacfx.failed`", output, StringComparison.Ordinal);
                Assert.Contains("SQL72030", output, StringComparison.Ordinal);
            }

            Assert.True(diffExit == 0, diffOutput);
            var answer = JsonNode.Parse(diffOutput)!;
            ScratchEstate.Valid("estate.diff.1.schema.json", answer);
            var change = answer["diff"]!["change"]!;
            var (dropped, created) = (change["dropped"]!.AsArray().Select(k => (string?)k).ToList(), change["created"]!.AsArray().Select(k => (string?)k).ToList());
            if (caseInsensitive)
            {
                Assert.Equal(["Table [dbo].[CUSTOMER] to Table [dbo].[Customer]"], change["caseOnlyRenamed"]!.AsArray().Select(r => r!["before"] + " to " + r["after"]));
                Assert.Contains(answer["findings"]!.AsArray(), f => (string?)f!["code"] == "diff.case-only-rename" && (string?)f["severity"] == "note"
                    && ((string?)f["message"])!.Contains(collation + " reads as one name; DacFx plans nothing for it", StringComparison.Ordinal));
                Assert.DoesNotContain(dropped.Concat(created), key => key!.StartsWith("Table ", StringComparison.Ordinal));
            }
            else
            {
                Assert.Contains("Table [dbo].[CUSTOMER]", dropped);
                Assert.Contains("Table [dbo].[Customer]", created);
                Assert.Empty(change["caseOnlyRenamed"]!.AsArray());
            }
        }
        finally
        {
            GitTests.Ok(ScratchServer.Drop(copy));
        }
    }

    /// <summary>
    /// Decision 2.25 on a database: a copy of the golden project given a column named by one space and one whose name holds a tab, which
    /// SQL Server admits inside brackets, reads whole at exit 0 naming both; diff from the copy to the package, which lacks them, exits 5
    /// with --fail-on-change and prints each as dropped, the tab escaped as \u0009; and check drift against the golden ref exits 5 naming
    /// both columns under the altered table, escaped the same way. No raw tab reaches either Markdown output.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_copy_holding_a_column_named_by_a_space_and_one_holding_a_tab_is_read_whole_and_each_output_escapes_the_tab()
    {
        var (copy, dacpac, _) = await Published(null);
        try
        {
            await SqlServerFixture.ExecuteAsync(copy.Connection, "ALTER TABLE dbo.Customer ADD [ ] INT NULL, [a\tb] INT NULL;");

            var (readExit, read) = estate.Estate("read", "--from", "copy:" + copy.Name, "--json");
            var (diffExit, diff) = estate.Estate("diff", "--from", "copy:" + copy.Name, "--to", "dacpac:" + dacpac, "--fail-on-change");
            var (driftExit, drift) = Drift("copy:" + copy.Name);

            Assert.True(readExit == 0, read);
            var answer = JsonNode.Parse(read)!;
            var whole = (string?)answer["full"] is { } full ? JsonNode.Parse(File.ReadAllText(Path.Combine(estate.Root, full)))! : answer;
            var keys = whole["read"]!["elements"]!.AsArray().Select(e => (string?)e!["key"]).ToList();
            Assert.Contains("Column [dbo].[Customer].[ ]", keys);
            Assert.Contains("Column [dbo].[Customer].[a\tb]", keys);
            Assert.True(diffExit == 5, diff);
            Assert.Contains("dropped Column [dbo].[Customer].[ ]", diff.Split('\n'));
            Assert.Contains("dropped Column [dbo].[Customer].[a\\u0009b]", diff.Split('\n'));
            Assert.True(driftExit == 5, drift);
            Assert.Contains("- warning `drift.alter` Table [dbo].[Customer]: ", drift, StringComparison.Ordinal);
            Assert.Contains("`drift.column` dropped Column [dbo].[Customer].[ ]", drift, StringComparison.Ordinal);
            Assert.Contains("`drift.column` dropped Column [dbo].[Customer].[a\\u0009b]", drift, StringComparison.Ordinal);
            Assert.DoesNotContain('\t', diff + drift);
        }
        finally
        {
            GitTests.Ok(ScratchServer.Drop(copy));
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
            Assert.Equal((0, "matches"), (exit, (string?)answer["outcome"]));
            var receipt = answer["receipt"]!;
            // The copy ran in the estate-sql container when its server is the one ~/.estate/sql.env names, whether ESTATE_SQL also names it or
            // not; ci/sql.sh up, which the fixture runs, keeps that container on the pinned image, whose digest Docker then reports.
            var container = File.Exists(ScratchServer.SqlEnv) && ScratchServer.ServerName(null, ScratchServer.SqlEnv, localDb: false) is Result<ServerName>.Ok(var inContainer)
                && ScratchServer.ServerName(copy.Connection) is Result<ServerName>.Ok(var made) && made == inContainer;
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
            GitTests.Ok(ScratchServer.Drop(copy));
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

    /// <summary>A fresh copy on the run's scratch server, registered under the estate's root, with the golden project published to it under the pipeline's profile.</summary>
    private async Task<SqlServer.Copy> Published() => (await Published(null)).Copy;

    /// <summary>
    /// A fresh copy, its collation set as given while it is still empty (the server's default when null), with the golden project built
    /// under that collation (its DefaultCollation, the model's; the project as committed when null) published to it under the pipeline's
    /// profile; the package and the profile too, for a plan against it. DacFx refuses a case-insensitive model deployed to a
    /// case-sensitive database (SQL72030), so the package's collation follows the copy's.
    /// </summary>
    private async Task<(SqlServer.Copy Copy, string Dacpac, PublishProfile.Strict Profile)> Published(string? collation)
    {
        var server = await SqlServerFixture.ServerAsync();
        var copy = GitTests.Ok(ScratchServer.Create(estate.Root, server));
        string dacpac;
        if (collation is null)
        {
            dacpac = GitTests.Ok(Ssdt.Build(GitTests.Ok(Git.At(estate.Root, estate.Base)), "project/SampleCatalog.sqlproj", estate.Tool.Folder, Path.Combine(estate.Root, ".estate", "build"))).Path;
        }
        else
        {
            await SqlServerFixture.ExecuteAsync(server, "DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@name) + N' COLLATE " + collation + ";'; EXEC (@sql);", copy.Name.ToString());
            var project = Path.Combine(estate.Root, ".estate", "collation", collation);
            if (!Directory.Exists(project))
            {
                ToolFolderTests.Copy(Path.Combine(GitTests.Ok(Git.At(estate.Root, estate.Base)).Path, "project"), project);   // the golden project as committed, not the head's edit
                var file = Path.Combine(project, "SampleCatalog.sqlproj");
                var text = File.ReadAllText(file);
                File.WriteAllText(file, text.Contains("<DefaultCollation>", StringComparison.Ordinal)
                    ? System.Text.RegularExpressions.Regex.Replace(text, "<DefaultCollation>[^<]*</DefaultCollation>", "<DefaultCollation>" + collation + "</DefaultCollation>")
                    : text.Replace("<PropertyGroup>", "<PropertyGroup>\n    <DefaultCollation>" + collation + "</DefaultCollation>", StringComparison.Ordinal));
            }

            dacpac = GitTests.Ok(Ssdt.Build(Path.Combine(project, "SampleCatalog.sqlproj"), estate.Tool.Folder, Path.Combine(estate.Root, ".estate", "build"))).Path;
        }

        var profile = GitTests.Ok(Profiles.Load(Path.Combine(estate.Root, ScratchEstate.Profile)));
        GitTests.Ok(copy.Publish(dacpac, profile));
        return (copy, dacpac, profile);
    }

    private (int Exit, string Output) Drift(string target, params string[] more) =>
        estate.Estate(["check", "drift", "--target", target, "--at", estate.Base, "--profile", ScratchEstate.Profile, .. more]);
}
