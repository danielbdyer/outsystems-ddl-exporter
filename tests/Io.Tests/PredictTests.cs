using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.Data.SqlClient;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// dbchange predict (V3_MILESTONES.md WP 2.5) and M2's exits 1 to 4 on fixture environments: each a database of the local server standing
/// in for Dev, QA or UAT, the golden project's base published to it with its seed's rows, read as the read-only principal through a
/// dbchange/environments.json in a test checkout that names the local server and no real host. The checkout stands as a developer's
/// does: the make-mandatory change, Customer.Email made NOT NULL, on a branch that left main at the golden base.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class PredictTests(ScratchRepository repository) : IClassFixture<ScratchRepository>
{
    /// <summary>
    /// M2 exit 1 (R11, R12): on the branch that makes the populated Customer.Email NOT NULL, a developer's dbchange predict, which names
    /// no environment, predicts Dev: it blocks, BlockOnPossibleDataLoss fires on dbo.Customer, which holds rows, this many rows would
    /// violate NOT NULL on Email, and it ships in two releases; the block is labelled predicted · read-only with its time and DacFx
    /// release. Account.Region dropped from Dev by hand is an operation of Dev's plan the branch does not make, listed as baseline drift.
    /// With Customer's rows deleted, the same predict applies, and the drift is still listed.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Exit", "M2.1")]
    public async Task On_a_branch_that_makes_a_populated_column_NOT_NULL_predict_says_Dev_blocks_and_on_an_empty_table_applies()
    {
        repository.Branched();
        await using var dev = await Stand.Of();
        await dev.Run("ALTER TABLE dbo.Account DROP COLUMN Region;");
        var (rows, nulls) = (await dev.Scalar("SELECT COUNT(*) FROM dbo.Customer;"),
            await dev.Scalar("SELECT COUNT(*) FROM dbo.Customer WHERE Email IS NULL;"));
        var root = repository.Named(("dev", dev.Connection));

        var (exit, answer) = repository.AnswerAt(root, "predict");

        Assert.True(exit == 3, answer.ToJsonString());
        Assert.True(rows > nulls && nulls > 0, rows + " rows, " + nulls + " NULL emails");
        Assert.Equal(("blocks", "block-on-possible-data-loss"), ((string?)answer["outcome"], (string?)answer["blockedBy"]));
        var predicted = Assert.Single(answer["predict"]!["environments"]!.AsArray())!;
        Assert.Equal(("dev", "blocks", "two-releases"),
            ((string?)predicted["environment"], (string?)predicted["outcome"], (string?)predicted["shipping"]));
        Assert.Equal(["NotNull on [dbo].[Customer] ([Email])", "TableEmpty on [dbo].[Customer]"],
            predicted["because"]!.AsArray().Select(b => (string)b!).Order(StringComparer.Ordinal));
        Assert.Equal([("alter", "Table [dbo].[Account]")], Drift(predicted));
        var lines = Lines(answer);
        Assert.Matches(@"^#### predicted · read-only · \d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ · DacFx \d+\.\d+\.\d+$", lines[0]);
        Assert.Contains("- env:dev blocks: BlockOnPossibleDataLoss fires on [dbo].[Customer], which holds " + Rows(rows) + "; " + nulls
            + " of its " + Rows(rows) + " would violate NotNull on [dbo].[Customer] ([Email]); it ships in two releases.", lines);
        Assert.Contains("  - baseline drift, what env:dev's plan does that the change does not: alter Table [dbo].[Account]", lines);

        await dev.Run("DELETE FROM dbo.Customer;");
        (exit, answer) = repository.AnswerAt(root, "predict");

        Assert.True(exit == 0, answer.ToJsonString());
        Assert.Equal(("applies", null), ((string?)answer["outcome"], (string?)answer["blockedBy"]));
        Assert.Equal([("alter", "Table [dbo].[Account]")], Drift(answer["predict"]!["environments"]![0]!));
        Assert.Contains("- env:dev applies.", Lines(answer));
    }

    /// <summary>
    /// M2 exit 2 (R16): a dev lead's dbchange predict --env qa,uat prints the block to paste into the pull request, whose last lines are
    /// the machine block holding QA's and UAT's existing data, counts and provenance, which the gate's parser reads back as the two
    /// predictions; QA holds a check added WITH NOCHECK, which its trust list names untrusted. A developer's run against UAT, whose
    /// login the server refuses, says in one sentence that a lead's prediction will appear on the pull request, alone at exit 4, and
    /// beside Dev, which is still predicted.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Exit", "M2.2")]
    public async Task A_lead_s_predict_for_QA_and_UAT_prints_the_pasteable_block_and_a_developer_s_against_UAT_says_a_lead_s_will_appear()
    {
        repository.Branched();
        await using var qa = await Stand.Of();
        await using var uat = await Stand.Of();
        await qa.Run("ALTER TABLE dbo.Customer WITH NOCHECK ADD CONSTRAINT CK_Customer_Name CHECK (LEN(Name) > 0);");
        var lead = repository.Named(("qa", qa.Connection), ("uat", uat.Connection));

        var (exit, answer) = repository.AnswerAt(lead, "predict", "--env", "qa,uat");

        Assert.True(exit == 3, answer.ToJsonString());
        var block = (string)answer["predict"]!["block"]!;
        var predictions = Expect.Value(PredictionBlock.Read(block));
        Assert.Equal(["qa", "uat"], predictions.Select(p => p.Environment.ToString()));
        Assert.All(predictions, p => Assert.Equal(("env:" + p.Environment, ExistingDataFile.FingerprintOf(p.ExistingData)),
            (p.Provenance.Target.ToString(), p.Provenance.ExistingData!.Value)));
        Assert.All(predictions, p => Assert.Contains(p.ExistingData.States, s => s.Precondition.ToString() == "TableEmpty on [dbo].[Customer]"
            && s.Populated));
        Assert.Equal(["[dbo].[CK_Customer_Name] enabled and untrusted"],
            predictions[0].ExistingData.Trust.Select(t => t.Constraint.Name + (t.Disabled ? " disabled" : " enabled and untrusted")));
        Assert.Empty(predictions[1].ExistingData.Trust);
        var lines = block.TrimEnd('\n').Split('\n');
        Assert.Equal("```", lines[^1]);
        Assert.Equal("```" + PredictionBlock.Schema, lines[^4]);

        var denied = Denied();
        var developer = repository.Named(("dev", uat.Connection), ("uat", denied));
        (exit, answer) = repository.AnswerAt(developer, "predict", "--env", "uat");
        var (alone, printed) = repository.RunAt(developer, "predict", "--env", "uat");
        Assert.Equal((4, 4), (exit, alone));
        Assert.Equal(Sentence, printed.Split('\n')[0]);
        Assert.Equal(Sentence, (string?)answer["message"]);

        (exit, answer) = repository.AnswerAt(developer, "predict", "--env", "dev,uat");
        Assert.True(exit == 3, answer.ToJsonString());
        Assert.Equal(["dev blocks", "uat unread"], answer["predict"]!["environments"]!.AsArray()
            .Select(e => (string)e!["environment"]! + " " + (string)e["outcome"]!));
        Assert.Contains("- env:uat is not predicted: " + Sentence, Lines(answer));
        Assert.Contains(answer["findings"]!.AsArray(), f => (string?)f!["subject"] == "env:uat" && (string?)f["message"] == Sentence);
        Assert.DoesNotContain("planted", answer.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// M2 exit 3 (R14): the query log of a developer's run and of a lead's, each the published dbchange as its own process, holds only
    /// the allowlist's shapes: the check that the identity holds VIEW DEFINITION, the synonym check that precedes each aggregate query,
    /// and aggregate queries the allowlist admits, run after the session's SET LANGUAGE and SET DATEFORMAT. An Extended Events session
    /// on the read-only principal, reused from M1 exit 8, records no DML, no DDL and no EXEC through both runs; each masked text is
    /// DacFx's catalog read of an extract, one an environment at most.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "S7")]
    [Trait("Exit", "M2.3")]
    public async Task The_read_only_principal_sends_no_DML_no_DDL_and_no_EXEC_through_predict_and_its_query_log_holds_the_allowlist_s_shapes()
    {
        repository.Branched();
        await using var dev = await Stand.Of();
        var root = repository.Named(("dev", dev.Connection), ("qa", dev.Connection), ("uat", dev.Connection));
        await using var trace = await ReadOnlyLoginTrace.StartAsync(dev.Reader.Login);

        var (developerExit, developer) = repository.Tool.RunAt(root, "predict");
        var (leadExit, lead) = repository.Tool.RunAt(root, "predict", "--env", "qa,uat");
        var sent = await trace.StopAsync();

        Assert.True((developerExit, leadExit) == (3, 3), developer + "\n" + lead);
        var logs = Directory.GetFiles(Path.Combine(root, ".dbchange", "runs"), "queries.log", SearchOption.AllDirectories);
        Assert.Equal(2, logs.Length);
        foreach (var log in logs)
        {
            var entries = Entries(File.ReadAllText(log));
            Assert.All(entries, e => Assert.True(Admitted(e.Site, e.Statement), "not an allowlist shape: " + e.Site + "\n" + e.Statement));
            Assert.Contains(entries, e => e.Site == "VIEW DEFINITION");
            Assert.Contains(entries, e => e.Site.StartsWith("Synonyms: ", StringComparison.Ordinal));
        }

        Assert.Contains(sent, s => s.Text.Contains("HAS_PERMS_BY_NAME", StringComparison.Ordinal));
        var masked = sent.Where(s => ReadOnlyLoginTrace.Masked(s.Text)).ToList();
        Assert.All(masked, s => Assert.Equal(("rpc_completed", "sp_executesql"), (s.Event, s.Object)));
        Assert.True(masked.Count <= 3, masked.Count + " masked calls for three extracts");
        var writes = sent.Where(s => !ReadOnlyLoginTrace.Masked(s.Text)).SelectMany(s => ReadOnlyLoginTrace.Writes(s.Text)).ToList();
        Assert.True(writes.Count == 0, string.Join("\n----\n", writes));
        Assert.Empty(sent.Where(s => s.Event == "rpc_completed" && s.Object != "sp_executesql").Select(s => s.Object + ": " + s.Text));
    }

    /// <summary>
    /// M2 exit 4, the planted-value scan's first half (R7, R16): an environment classified real holds PlantedValue's value in dbo.Planted,
    /// and the change retypes the column holding it to int, which fails on it, adds a check whose int comparison fails to convert it,
    /// and adds a check that overflows int on its other row's number. predict blocks, and the value is in no output, no queries.log and
    /// no block; the aggregate queries run every conversion through TRY_CONVERT, so none quotes it, and the one that fails, on the
    /// overflow, reports SQL Server's error number alone.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "P2")]
    [Trait("Value", "X2")]
    [Trait("Exit", "M2.4")]
    public async Task A_planted_value_a_check_and_a_retype_fail_to_convert_is_in_no_output_log_or_block_and_failed_queries_report_numbers()
    {
        var planted = PlantedValue.Unique();
        repository.Branched();
        var @base = repository.Committed(new SampleChange("dbo.Planted", [Planted()], []));
        var head = repository.Committed(new SampleChange("dbo.Planted retyped and checked", [Planted(
            "Value INT NOT NULL,\n    Code NVARCHAR(100) NOT NULL CONSTRAINT CK_Planted_Code CHECK (Code > 0),\n"
            + "    N INT NOT NULL CONSTRAINT CK_Planted_N CHECK (N + 1 > 0),")], []));
        await using var uat = await Stand.Of();
        await uat.Run("CREATE TABLE dbo.Planted (Id INT NOT NULL CONSTRAINT PK_Planted_Id PRIMARY KEY CLUSTERED, Value NVARCHAR(100) NOT NULL,"
            + " Code NVARCHAR(100) NOT NULL, N INT NOT NULL);");
        await SqlServerFixture.ExecuteAsync(uat.Database.ConnectionString,
            "INSERT dbo.Planted (Id, Value, Code, N) VALUES (1, @name, @name, 1), (2, N'7', N'7', 2147483647);", planted.Text);
        var root = repository.Named(("uat", uat.Connection));
        string[] arguments = ["predict", "--env", "uat", "--at", head, "--base", @base];

        var (exit, answer, json) = VerbAnswer.Printing(new Checkout(root, root, repository.Tool.Folder, Cli.Contract.Version), arguments);
        var (printedExit, printed) = repository.RunAt(root, arguments);

        Assert.True((exit, printedExit) == (3, 3), json);
        var predicted = answer["predict"]!["environments"]![0]!;
        Assert.Contains("Fits on [dbo].[Planted] ([Value])", predicted["because"]!.AsArray().Select(b => (string)b!));
        var failed = predicted["unanswered"]!.AsArray().Select(u => ((string)u!["precondition"]!, (string)u["why"]!)).ToList();
        Assert.Contains(failed, f => f.Item1.StartsWith("Check on [dbo].[Planted] ([N])", StringComparison.Ordinal)
            && f.Item2 == "SQL Server raised Msg 8115; message withheld");
        var runs = Path.Combine(root, ".dbchange", "runs");
        var logs = string.Concat(Directory.GetFiles(runs, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains("TRY_CONVERT", logs, StringComparison.Ordinal);
        foreach (var output in new[] { json, printed, logs, (string)answer["predict"]!["block"]! })
        {
            planted.AbsentFrom(output);
        }
    }

    /// <summary>
    /// The last of wave 5's review of WP 2.2: BlockOnPossibleDataLoss.Read(plan, target), which predict calls, through an env: target
    /// whose SQLCMD value ServiceToken is a file: reference. The project declares the variable, its pre-deploy script holds a check that
    /// reads it, and the change makes Customer.Email NOT NULL, for which DacFx writes a check of its own. The check that reads the
    /// variable is sqlcmd.sensitive, naming it, and still stands with its table; the plan's other check is read and runs; and neither the
    /// planted value nor the run's queries.log holds the value, which no check's text or site carries either.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "X2")]
    public async Task A_check_reading_a_SQLCMD_value_a_file_reference_gives_is_sqlcmd_sensitive_and_hides_none_of_the_plan_s_other_checks()
    {
        var planted = PlantedValue.Unique();
        await using var uat = await Stand.Of();
        using var root = ScratchFolder.UnderRepository("predict-sqlcmd");
        var token = Owned(root.File("service.token", planted.Text));
        EnvironmentsJson.Of("uat", new("file:" + uat.Connection.Replace('\\', '/'), "localhost", Classification: "real",
                Sqlcmd: new Dictionary<string, EnvironmentsJson.SqlCmd> { ["ServiceToken"] = new EnvironmentsJson.SqlCmd.Reference("file:"
                    + token.Replace('\\', '/')) }))
            .WriteTo(root.Path);
        File.Copy(GoldenProject.Profile, Path.Combine(root.Path, "dbchange", "profiles", "pipeline.publish.xml"), overwrite: true);
        var target = Expect.Value(SqlServer.Resolve(Expect.Value(SqlServer.Target("env:uat", "--target")), root.Path));
        var log = SqlServer.QueryLog.Start(root.Path);
        using var head = Expect.Value(Ssdt.Open(await GoldenProject.Built(new SampleChange("a pre-deploy check reading $(ServiceToken)",
            [.. GoldenProject.Change("make-mandatory").Edits,
                new GoldenEdit("SampleCatalog.sqlproj", "  <ItemGroup>\n    <RefactorLog Include=\"SampleCatalog.refactorlog\" />",
                    "  <ItemGroup>\n    <SqlCmdVariable Include=\"ServiceToken\">\n      <DefaultValue>none</DefaultValue>\n"
                    + "      <Value>$(SqlCmdVar__1)</Value>\n    </SqlCmdVariable>\n  </ItemGroup>\n  <ItemGroup>\n"
                    + "    <RefactorLog Include=\"SampleCatalog.refactorlog\" />"),
                new GoldenEdit("Script.PreDeployment.sql", "PRINT 'Pre-deploy: no backfill active.",
                    "IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer] WHERE [Name] = N'$(ServiceToken)')\n"
                    + "    RAISERROR (N'A row holds the service token.', 16, 127) WITH NOWAIT;\nGO\nPRINT 'Pre-deploy: no backfill active.")],
            []))));
        using var extracted = Expect.Value(DacFx.Extract(target));
        var plan = Expect.Value(DacFx.Plan(head, extracted, "Uat", Expect.Value(PublishProfiles.Load(GoldenProject.Profile)),
            Expect.Value(SqlServer.SqlCmdValues(target))));

        var checks = Expect.Value(BlockOnPossibleDataLoss.Read(plan, target));

        Assert.Equal(["[dbo].[Customer]", "[dbo].[Customer]"], checks.Select(c => c.Table.ToString()));
        var sensitive = Assert.Single(checks, c => c.Query is Result<SqlServer.AggregateQuery>.Failed);
        Assert.Contains("$(ServiceToken)", Expect.Failed(sensitive.Query, "sqlcmd.sensitive").Message, StringComparison.Ordinal);
        var dacfx = Assert.Single(checks, c => c.Query is Result<SqlServer.AggregateQuery>.Ok);
        Assert.Contains("Email", Assert.Single(dacfx.Sites).Statement, StringComparison.Ordinal);
        Assert.Equal((long?)1, Assert.Single(Assert.IsType<SqlServer.Measurement.Answered>(Expect.Value(
            BlockOnPossibleDataLoss.Measure(target, dacfx, log))).Rows).Values.Single());
        planted.AbsentFrom(plan.Script);
        planted.AbsentFrom(string.Join('\n', checks.Select(c => c.Text + "\n" + string.Join('\n', c.Sites)
            + "\n" + c.Query.Match(q => q.Statement, e => e.Message))));
        planted.AbsentFrom(File.ReadAllText(log.Path));
    }

    private const string Sentence =
        "env:uat refused this identity (Msg 18456, SQL Server's message withheld); a lead's prediction will appear on the pull request.";

    /// <summary>dbo.Planted added to the golden project after dbo.Status, its three columns as given; the base's by default.</summary>
    private static GoldenEdit Planted(string columns = "Value NVARCHAR(100) NOT NULL,\n    Code NVARCHAR(100) NOT NULL,\n    N INT NOT NULL,")
        => new("Modules/Status.sql", "CREATE UNIQUE INDEX [UIX_Status_Code] ON dbo.Status (Code);\nGO",
            "CREATE UNIQUE INDEX [UIX_Status_Code] ON dbo.Status (Code);\nGO\n\nCREATE TABLE dbo.Planted\n(\n    Id INT NOT NULL,\n    "
            + columns + "\n    CONSTRAINT PK_Planted_Id PRIMARY KEY CLUSTERED (Id)\n);\nGO");

    /// <summary>A connection file naming a login the server does not hold, which it refuses with Msg 18456.</summary>
    private string Denied() => Owned(Path.Combine(Path.GetDirectoryName(repository.Root)!, "denied-" + Guid.NewGuid().ToString("N")[..8]
        + ".connection"), new SqlConnectionStringBuilder(SqlServerFixture.ServerAsync().GetAwaiter().GetResult())
        {
            InitialCatalog = "master", IntegratedSecurity = false,
            UserID = "dbchange_nobody_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)), Password = "Denied1!planted",
            TrustServerCertificate = true, Pooling = false,
        }.ConnectionString);

    /// <summary>A file written, which only its owner reads, as io/SqlServer requires of a file: reference; its path.</summary>
    private static string Owned(string path, string? text = null)
    {
        if (text is not null)
        {
            File.WriteAllText(path, text);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return path;
    }

    /// <summary>The Markdown block of an answer, line by line.</summary>
    private static string[] Lines(JsonNode answer) => ((string)answer["predict"]!["block"]!).TrimEnd('\n').Split('\n');

    private static IEnumerable<(string, string)> Drift(JsonNode environment) =>
        environment["baselineDrift"]!.AsArray().Select(o => ((string)o!["operation"]!, (string)o["key"]!));

    private static string Rows(int rows) => rows.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
        + (rows == 1 ? " row" : " rows");

    /// <summary>Whether a logged statement is one of the allowlist's shapes: the VIEW DEFINITION check, a synonym check, or an aggregate
    /// query the allowlist admits, after its session's DECLARE and SET lines.</summary>
    private static bool Admitted(string site, string statement)
    {
        var query = string.Join('\n', statement.Split('\n').Where(l => !l.StartsWith("DECLARE @", StringComparison.Ordinal)
            && l is not ("SET LANGUAGE @language;" or "SET DATEFORMAT @dateformat;")));
        return site == "VIEW DEFINITION" ? query == "SELECT HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'VIEW DEFINITION'), "
                + "HAS_PERMS_BY_NAME(NULL, NULL, N'VIEW ANY DEFINITION');"
            : site.StartsWith("Synonyms: ", StringComparison.Ordinal)
                ? Regex.IsMatch(query, @"\ASELECT t\.name FROM \(VALUES \(@t0\)(, \(@t\d+\))*\) AS t\(name\) WHERE OBJECT_ID\(t\.name, N'SN'\) IS NOT NULL;\z")
            : SqlServer.AggregateQuery.Of(query, site) is Result<SqlServer.AggregateQuery>.Ok;
    }

    /// <summary>queries.log as entries: a header line naming the time, the target, the site and, after the line's last colon, the
    /// outcome; the statement; then GO.</summary>
    private static (string Site, string Statement)[] Entries(string log) =>
        [.. Regex.Matches(log, @"^-- \S+ \S+ (?<site>.+): (?<outcome>[^\n]+)\n(?<statement>(?:(?!GO\n).*\n)+?)GO\n",
            RegexOptions.Multiline | RegexOptions.CultureInvariant)
        .Select(m => (m.Groups["site"].Value, m.Groups["statement"].Value.TrimEnd('\n')))];

    /// <summary>
    /// A database of the local server standing in for a named environment: the golden project's base published to it with its seed's rows,
    /// and the read-only principal predict reads it as, through a connection file only the owner reads.
    /// </summary>
    private sealed class Stand(RegisteredDatabase database, ReadOnlyPrincipal reader) : IAsyncDisposable
    {
        public RegisteredDatabase Database => database;

        public ReadOnlyPrincipal Reader => reader;

        public string Connection => Path.Combine(Repository.Root, reader.Reference["file:".Length..]);

        public static async Task<Stand> Of()
        {
            var database = await SqlServerFixture.RegisterAsync();
            GoldenProject.Publish(await GoldenProject.Built(), database, GoldenProject.Pipeline());
            return new Stand(database, await ReadOnlyPrincipal.CreateAsync(database));
        }

        /// <summary>A script run as the fixture's administrator.</summary>
        public Task Run(string sql) => SqlServerFixture.ExecuteAsync(database.ConnectionString, sql);

        public Task<int> Scalar(string sql) => SqlServerFixture.ScalarAsync(database.ConnectionString, sql);

        public ValueTask DisposeAsync() => database.DisposeAsync();
    }
}
