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
/// estate check drift (WP 1.7, V3_ARCHITECTURE.md §8.8, contract C7): the ref built, the target extracted once, the ref's package planned
/// against it package to package under the pipeline's profile; exit 0 when the deploy plan is empty and 5 naming each object when it is
/// not; law 2′ (M1 exit 3), R13's stamp and window (exit 6), R16's refusals (exit 7), and R14's watch on the read-only principal (exit 8).
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

    /// <summary>M1 exit 6 through the verb: a ledger whose pin the committed DacFx is neither, nor the release before, is exit 6 before anything connects, the stamp naming both.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_committed_DacFx_outside_the_ledger_s_window_is_exit_6_before_anything_connects()
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
            Assert.Equal(("170.5.96", "170.7.2", null), ((string?)answer["dacfx"], (string?)answer["pin"], answer["server"]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Law 2′ (M1 exit 3): the golden project published to a fresh copy matches it, check drift exit 0 and the deploy plan empty; one column
    /// altered on the copy is exit 5 naming its table and that column, the column's Length from the target to the repository.
    /// </summary>
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
            Assert.StartsWith("copy:" + copy.Name + " matches ref:" + estate.Base + " (commit " + estate.Base[..8] + ").\n", output, StringComparison.Ordinal);

            await SqlServerFixture.ExecuteAsync(copy.Connection, "ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(300) NULL;");
            (exit, output) = Drift("copy:" + copy.Name);

            Assert.True(exit == 5, output);
            Assert.StartsWith("copy:" + copy.Name + " differs from ref:" + estate.Base + " (commit " + estate.Base[..8] + "): the deploy plan holds 1 operation.", output, StringComparison.Ordinal);
            Assert.Contains("- warning `drift.alter` Table [dbo].[Customer]: The deploy plan against copy:" + copy.Name + " would alter Table [dbo].[Customer].", output, StringComparison.Ordinal);
            Assert.Equal(["- warning `drift.column` Column [dbo].[Customer].[Email]: Length 300 → 256, from the target to the repository."],
                output.Split('\n').Where(l => l.Contains("`drift.column`", StringComparison.Ordinal)));
        }
        finally
        {
            GitTests.Ok(ScratchServer.Drop(copy));
        }
    }

    /// <summary>
    /// The golden project published to a copy, then a column the project lacks added to dbo.Product with a named default, and a view the
    /// project lacks over it: the plan of the project against the copy drops the column. check drift names the table's alteration and the
    /// default's drop as warnings, quotes DacFx's DataIssue alert, lists any operation DacFx adds for a dependent in one note, and writes no
    /// warning for a refresh.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_column_the_package_drops_is_named_with_DacFx_s_data_issue_and_a_dependent_s_refresh_is_a_note()
    {
        var copy = await Published();
        try
        {
            await SqlServerFixture.ExecuteAsync(copy.Connection, "ALTER TABLE dbo.Product ADD LegacyNote NVARCHAR(40) NOT NULL CONSTRAINT DF_Product_LegacyNote DEFAULT (N'x');");
            await SqlServerFixture.ExecuteAsync(copy.Connection, "CREATE VIEW dbo.ProductNotes AS SELECT Id, LegacyNote FROM dbo.Product;");

            var (exit, output) = Drift("copy:" + copy.Name, "--json");

            var answer = JsonNode.Parse(output)!;
            ScratchEstate.Valid("estate.check.1.schema.json", answer);
            var findings = answer["findings"]!.AsArray().Select(f => (Code: (string)f!["code"]!, Severity: (string)f["severity"]!, Subject: (string)f["subject"]!, Message: (string)f["message"]!)).ToList();
            Console.WriteLine(string.Join('\n', findings));
            Assert.True(exit == 5, output);
            Assert.Contains(("drift.alter", "warning", "Table [dbo].[Product]"), findings.Select(f => (f.Code, f.Severity, f.Subject)));
            Assert.Contains(("drift.drop", "warning", "DefaultConstraint [dbo].[DF_Product_LegacyNote]"), findings.Select(f => (f.Code, f.Severity, f.Subject)));
            Assert.Contains(findings, f => f is { Code: "drift.data-issue", Severity: "warning", Subject: "Table [dbo].[Product]" }
                && f.Message == "The column [dbo].[Product].[LegacyNote] is being dropped, data loss could occur.");
            Assert.DoesNotContain(findings, f => f.Code == "drift.refresh");
            Assert.All(findings.Where(f => f.Code == "drift.consequence"), f => Assert.Equal("note", f.Severity));
            Assert.Contains("`drift.column` dropped Column [dbo].[Product].[LegacyNote]", Drift("copy:" + copy.Name).Output, StringComparison.Ordinal);
        }
        finally
        {
            GitTests.Ok(ScratchServer.Drop(copy));
        }
    }

    /// <summary>
    /// Decision 2.26's measurement (O2): the golden project built under a case-insensitive and under a case-sensitive collation, each
    /// published to a copy created under that collation, then dbo.Customer renamed CUSTOMER on each with sp_rename, which sys.tables
    /// confirms in a binary comparison. The plan of the package against its copy plans nothing under either collation: DacFx matches object
    /// names ignoring case even where the database reads [dbo].[Customer] and [dbo].[CUSTOMER] as two names, so a case-only rename on a
    /// case-sensitive database is a difference no deploy plan will reconcile. estate diff from the copy to the package reads names under the
    /// copy's collation: one case-only pair with its note under the case-insensitive collation, and the table dropped and created under the
    /// case-sensitive one. check drift builds the golden ref, a case-insensitive package: against the case-insensitive copy it exits 0, and
    /// against the case-sensitive copy it is refused as DacFx refuses a live plan of a case-insensitive model against a case-sensitive
    /// database (SQL72030), which the plan package to package does not check: plan.collation at exit 6.
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

            var plan = Planned(dacpac, copy, profile);
            var (exit, output) = Drift("copy:" + copy.Name);
            var (diffExit, diffOutput) = estate.Estate("diff", "--from", "copy:" + copy.Name, "--to", "dacpac:" + dacpac, "--json");

            Assert.True(plan.Report.IsEmpty, collation + " planned:\n" + string.Join('\n', plan.Report.Operations));
            Assert.True(exit == driftExit, output);
            if (driftExit == 6)
            {
                Assert.Contains("`plan.collation`", output, StringComparison.Ordinal);
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
    /// R1 and DECISIONS.md, 2026-09-25: a drift's provenance holds the fingerprint of the target's schema, its elements as the copy extracts,
    /// and of the deploy report, the change the claim is about; neither is the package's. It names the committed DacFx and the copy's server,
    /// with the image's digest where the copy ran in the container, and lacks only the data conditions; the stamp says UNPINNED while the
    /// ledger's row does, and until S7 lands the Octopus step's profile, a note says the profile is not verified against it (§17 item 15).
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "R1")]
    [Trait("Exit", "M1.6")]
    public async Task A_drift_s_provenance_fingerprints_the_target_s_schema_and_the_deploy_report()
    {
        var (copy, dacpac, profile) = await Published(null);
        try
        {
            await SqlServerFixture.ExecuteAsync(copy.Connection, "ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(300) NULL;");
            var (exit, output) = Drift("copy:" + copy.Name, "--json");
            var plan = Planned(dacpac, copy, profile);
            using var extracted = GitTests.Ok(DacFx.Extract(copy));
            using var package = GitTests.Ok(Ssdt.Open(dacpac));
            var (schema, packaged) = (Fingerprint.Of(GitTests.Ok(extracted.Elements).Elements), Fingerprint.Of(GitTests.Ok(package.Elements).Elements));

            var answer = JsonNode.Parse(output)!;
            ScratchEstate.Valid("estate.check.1.schema.json", answer);
            Assert.Equal((5, "differs"), (exit, (string?)answer["outcome"]));
            var provenance = answer["provenance"]!;
            Assert.Equal(("sha256:" + schema, "sha256:" + Fingerprint.Of(plan.Report)), ((string?)provenance["schema"], (string?)provenance["change"]));
            Assert.DoesNotContain("sha256:" + packaged, new[] { (string?)provenance["schema"], (string?)provenance["change"] });
            // The copy ran in the estate-sql container when its server is the one ~/.estate/sql.env names, whether ESTATE_SQL also names it or
            // not; ci/sql.sh up, which the fixture runs, keeps that container on the pinned image, whose digest Docker then reports.
            var container = File.Exists(ScratchServer.SqlEnv) && ScratchServer.ServerName(null, ScratchServer.SqlEnv, localDb: false) is Result<ServerName>.Ok(var inContainer)
                && ScratchServer.ServerName(copy.Connection) is Result<ServerName>.Ok(var made) && made == inContainer;
            Assert.Equal(("170.5.96", container ? Doctor.ImageDigest : null, "UNPINNED"), ((string?)provenance["dacfx"], (string?)provenance["server"]!["image"], (string?)answer["pin"]));
            Assert.Equal(("copy:" + copy.Name, "[\"dataConditions\"]", estate.Base), ((string?)provenance["target"], provenance["lacking"]!.ToJsonString(), (string?)answer["check"]!["commit"]));
            Assert.Equal(answer["server"]!.ToJsonString(), provenance["server"]!.ToJsonString());
            Assert.Contains(answer["findings"]!.AsArray(), f => (string?)f!["code"] == "toolchain.unpinned" && ((string?)f["message"])!.Contains("UNPINNED", StringComparison.Ordinal));
            Assert.Contains(answer["findings"]!.AsArray(), f => (string?)f!["code"] == "profile.unverified" && (string?)f["severity"] == "note"
                && (string?)f["message"] == "The estate commits no copy of the publish profile the Octopus step applies, so " + ScratchEstate.Profile + " is not verified against it.");
        }
        finally
        {
            GitTests.Ok(ScratchServer.Drop(copy));
        }
    }

    /// <summary>
    /// VALUES.md S2, finding NFR-10: a check whose read of the database fails answers that failure, its stamp as far as the work got, and
    /// neither a column line nor matches; the cli once turned the same failure into an empty list of columns.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "S2")]
    public async Task A_check_drift_whose_extract_is_refused_answers_the_refusal_and_no_column_lines()
    {
        var copy = await Published();
        try
        {
            var refusal = new Error("server.failed", "copy:" + copy.Name + " failed the statement: Msg 245.", "Look the number up in SQL Server's error list.");
            var request = new DriftCheck.Request(new Target.RegisteredCopy(copy.Name), GitTests.Ok(GitRef.Of("--at", estate.Base)), ScratchEstate.Profile, null);

            var drift = DriftCheck.Run(new DriftCheck.Estate(estate.Root, estate.Root, estate.Tool.Folder, Cli.Contract.Version), request, SqlServer.QueryLog.Start(estate.Root), _ => refusal);

            Assert.Equal(refusal, Assert.IsType<Result<DriftCheck.Answer>.Failed>(drift.Result).Error);
            Assert.Equal(4, Cli.Contract.Exit(refusal));
            Assert.Equal("UNPINNED", drift.Stamp?.Pin?.ToString());
            Assert.NotNull(drift.Stamp?.Server);
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
    /// M1 exit 8 (R14) and the ruling of 2026-09-25: an Extended Events session on the read-only principal's login, made by the fixture's
    /// admin identity, records every batch and call that login sends through a check drift of a named environment that matches, one that
    /// finds drift, and a read --from env:dev. None is DML, DDL or an EXEC: the plan runs package to package, so DacFx no longer reads the
    /// instance's default file paths (xp_instance_regread), and the test admits no EXEC at all. SQL Server masks the text of one call per read
    /// of the database, which the test cannot read and admits by its form: DacFx sends its catalog queries as one sp_executesql call, and SQL
    /// Server replaces that call's text with *encrypt and dashes because it names the key and credential catalog views (measured through
    /// SqlClient's trace on DacFx 170.5.96: the call holds SELECT statements only).
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Exit", "M1.8")]
    public async Task The_read_only_principal_sends_no_DML_no_DDL_and_no_EXEC_through_check_drift_and_read()
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

            var (matchExit, matching) = estate.EstateAt(root, "check", "drift", "--target", "env:dev", "--at", estate.Base);
            await SqlServerFixture.ExecuteAsync(database.ConnectionString, "ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(300) NULL;");
            var (driftExit, drifted) = estate.EstateAt(root, "check", "drift", "--target", "env:dev", "--at", estate.Base);
            var (readExit, read) = estate.EstateAt(root, "read", "--from", "env:dev");
            await SqlServerFixture.ExecuteAsync(master, "ALTER EVENT SESSION [" + session + "] ON SERVER STATE = STOP;");

            Assert.True(matchExit == 0, matching);
            Assert.StartsWith("env:dev matches ref:" + estate.Base, matching, StringComparison.Ordinal);
            Assert.True(driftExit == 5, drifted);
            Assert.Contains("`drift.column` Column [dbo].[Customer].[Email]: Length 300 → 256", drifted, StringComparison.Ordinal);
            Assert.True(readExit == 0, read);
            var sent = await Events(master, file[..file.LastIndexOf('_')] + "*.xel");
            Assert.Contains(sent, s => s.Text.Contains("HAS_PERMS_BY_NAME", StringComparison.Ordinal));   // the session saw the run: estate's own first statement
            var masked = sent.Where(s => Masked(s.Text)).ToList();
            Assert.Equal(Enumerable.Repeat(("rpc_completed", "sp_executesql"), 3), masked.Select(s => (s.Event, s.Object)));   // one per read: the two checks and the read
            var writes = sent.Where(s => !Masked(s.Text)).SelectMany(s => Writes(s.Text)).ToList();
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
    [InlineData("DECLARE @filepath nvarchar(260); EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE',N'Software\\Microsoft\\MSSQLServer\\MSSQLServer',N'DefaultLog', @filepath output, 'no_output'", true)]
    [InlineData("exec sp_executesql N'DELETE dbo.Customer WHERE Id = @p', N'@p int', @p = 1", true)]
    [InlineData("EXEC dbo.usp_Anything;", true)]
    [InlineData("IF 1 = 1 BEGIN UPDATE dbo.Customer SET Email = NULL; END", true)]
    [InlineData("SELECT * INTO #kept FROM dbo.Customer;", true)]
    [InlineData("CREATE TABLE #t (Id int);", true)]
    [InlineData("GRANT SELECT ON dbo.Customer TO public;", true)]
    public void The_principal_test_finds_every_write_and_passes_every_read(string sent, bool writes) => Assert.Equal(writes, Writes(sent).Any());

    /// <summary>The form SQL Server gives a masked text, an asterisk, the word it matched and dashes, beside texts that only resemble it.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("*encrypt------------------------------", true)]
    [InlineData("*password----------", true)]
    [InlineData("*encrypt", false)]
    [InlineData("SELECT '*encrypt----' AS masked;", false)]
    [InlineData("", false)]
    public void The_principal_test_reads_a_masked_text_only_in_SQL_Server_s_form(string sent, bool masked) => Assert.Equal(masked, Masked(sent));

    /// <summary>Whether SQL Server masked a statement's text in the event: an asterisk, a lower-case word, then dashes to the end.</summary>
    private static bool Masked(string text) => System.Text.RegularExpressions.Regex.IsMatch(text, @"\A\*[a-z_]+-+\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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

    /// <summary>Each batch's text and each call's statement the session wrote to its files, with the event's name and, for a call, the procedure called.</summary>
    private static async Task<List<(string Event, string Object, string Text)>> Events(string master, string files)
    {
        var sent = new List<(string Event, string Object, string Text)>();
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var read = new SqlCommand("SELECT CAST(event_data AS nvarchar(max)) FROM sys.fn_xe_file_target_read_file(@files, NULL, NULL, NULL);", connection);
        read.Parameters.Add(new SqlParameter("@files", System.Data.SqlDbType.NVarChar, 400) { Value = files });
        await using var events = await read.ExecuteReaderAsync();
        while (await events.ReadAsync())
        {
            var @event = XElement.Parse(events.GetString(0));
            var data = @event.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
            sent.Add(((string?)@event.Attribute("name") ?? "", data.GetValueOrDefault("object_name") ?? "", data.GetValueOrDefault("batch_text") ?? data.GetValueOrDefault("statement") ?? ""));
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

    /// <summary>The plan of the package at <paramref name="dacpac"/> against the copy, extracted, package to package, as check drift plans.</summary>
    private static Plan Planned(string dacpac, SqlServer.Copy copy, PublishProfile.Strict profile)
    {
        using var extracted = GitTests.Ok(DacFx.Extract(copy));
        using var package = GitTests.Ok(Ssdt.Open(dacpac));
        return GitTests.Ok(DacFx.Plan(package, extracted, copy.Catalog, profile, []));
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

        var profile = GitTests.Ok(PublishProfiles.Load(Path.Combine(estate.Root, ScratchEstate.Profile)));
        GitTests.Ok(copy.Publish(dacpac, profile));
        return (copy, dacpac, profile);
    }

    private (int Exit, string Output) Drift(string target, params string[] more) =>
        estate.Estate(["check", "drift", "--target", target, "--at", estate.Base, "--profile", ScratchEstate.Profile, .. more]);
}
