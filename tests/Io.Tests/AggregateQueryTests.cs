using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Estate.Kernel;
using Estate.Tests;
using Microsoft.Data.SqlClient;
using Xunit;
using static Estate.Tests.Expect;

namespace Estate.Io.Tests;

/// <summary>
/// io/SqlServer against a named environment (V3_MILESTONES.md WP 1.4, §18; VALUES.md P2, X2): the golden project's registered
/// database stands in for env:uat, classified real and reached as the read-only principal through a file: reference. SqlServer.Measure
/// runs each admitted aggregate query of the allowlist's corpus there and reads integers back; every statement and its row count go to
/// the run's queries.log; a failed query reports its number and its site and nothing else; Model and Plan read as the same principal;
/// and a login the server refuses, or one that lacks what reading takes, is denied naming the environment and quoting nothing.
/// </summary>
public sealed class AggregateQueryTests(GoldenProject project) : IClassFixture<GoldenProject>, IDisposable
{
    private readonly ScratchFolder root = ScratchFolder.UnderRepository("aggregate-queries-under-test");

    public void Dispose() => root.Dispose();

    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "P2")]
    public void Every_admitted_query_of_the_corpus_returns_integers_as_the_read_only_principal_and_queries_log_holds_each_statement_and_its_row_count()
    {
        var uat = Resolved("uat", project.Reader.ConnectionString);
        var log = SqlServer.QueryLog.Start(root.Path);
        var admitted = AllowlistTests.Cases.Where(c => c.Admitted).Select(c => Value(SqlServer.AggregateQuery.Of(c.Text, c.Label))).ToList();

        var measured = admitted.Select(query => Assert.IsType<SqlServer.Measurement.Answered>(Value(SqlServer.Measure(uat, query, log)))).ToList();

        Assert.All(measured, m => Assert.NotEmpty(m.Rows));
        Assert.Contains(measured, m => m.Rows.Any(row => row.Values.Any(value => value > 0)));
        Assert.StartsWith(Path.Combine(root.Path, ".estate", "runs"), log.Path, StringComparison.Ordinal);
        var entries = Entries(File.ReadAllText(log.Path));
        var (checks, queries) = (entries.Where(e => e.Site.StartsWith("Synonyms: ", StringComparison.Ordinal)).ToList(), entries.Where(e => !e.Site.StartsWith("Synonyms: ", StringComparison.Ordinal)).ToList());
        Assert.Equal(admitted.Select(p => (p.Site, p.Statement)), queries.Select(e => (e.Site, e.Statement)));
        Assert.Equal(measured.Select(m => m.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)), queries.Select(e => e.Outcome));
        Assert.Equal(admitted.Where(p => p.Tables.Count > 0).Select(p => ("Synonyms: " + p.Site, "0")), checks.Select(e => (e.Site, e.Outcome)));
    }

    /// <summary>§18: SQL Server writes the value it fails to convert into Msg 245. Against an environment classified real, the executor records the number and the site, and the value appears in no result, message or line of the log.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "X2")]
    public async Task A_failed_query_against_a_real_environment_reports_only_its_number_and_its_site()
    {
        var planted = PlantedValue.Unique();
        await SqlServerFixture.ExecuteAsync(project.Copy.ConnectionString, "CREATE TABLE dbo.Planted (Id INT NOT NULL PRIMARY KEY, Value NVARCHAR(100) NOT NULL); INSERT dbo.Planted (Id, Value) VALUES (1, @name);", planted.Text);
        const string Statement = "SELECT SUM(CASE WHEN Value > 0 THEN 1 ELSE 0 END) FROM dbo.Planted;";
        Assert.Contains(planted.Text, (await Assert.ThrowsAsync<SqlException>(() => SqlServerFixture.ScalarAsync(project.Copy.ConnectionString, Statement))).Message, StringComparison.Ordinal);
        var uat = Resolved("uat", project.Reader.ConnectionString);
        var log = SqlServer.QueryLog.Start(root.Path);

        var failed = Assert.IsType<SqlServer.Measurement.Failed>(Value(SqlServer.Measure(uat, Value(SqlServer.AggregateQuery.Of(Statement, "dbo.Planted.Value Fits")), log)));

        Assert.Equal((245, "dbo.Planted.Value Fits", (string?)null), (failed.Number, failed.Site, failed.Message));
        Assert.Equal("dbo.Planted.Value Fits: query failed: Msg 245; message withheld", failed.ToString());
        Assert.Equal("failed, Msg 245", Assert.Single(Entries(File.ReadAllText(log.Path)), e => e.Site == "dbo.Planted.Value Fits").Outcome);
        planted.AbsentFrom(failed + File.ReadAllText(log.Path));
    }

    /// <summary>
    /// §1 facts 2, 4 and 5 through io, as the read-only principal: the environment is extracted whole, and the plan of the package it was
    /// published from against it, package to package, is empty. The environment's SQLCMD values reach the plan; the kept script holds the
    /// literal and never the value a reference resolved to, and the run's log holds the one statement estate sent, since the plan connects
    /// to nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "X2")]
    public void The_model_and_the_plan_of_a_named_environment_read_as_the_read_only_principal()
    {
        var (variable, token) = ("ESTATE_TEST_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), PlantedValue.Unique());
        System.Environment.SetEnvironmentVariable(variable, token.Text);
        try
        {
            var uat = Assert.IsType<SqlServer.EnvironmentDatabase>(Resolved("uat", project.Reader.ConnectionString, new()
            {
                ["EnvironmentTag"] = new PostureFile.SqlCmd.Literal("uat"), ["ServiceToken"] = new PostureFile.SqlCmd.Reference("env:" + variable),
            }));
            var log = SqlServer.QueryLog.Start(root.Path);

            Value(SqlServer.Reach(uat, log));
            using var extracted = Value(DacFx.Extract(uat));
            using var package = Value(Ssdt.Open(project.Base));
            var model = Value(extracted.Elements).Elements;
            var plan = Value(DacFx.Plan(package, extracted, uat.Catalog, Value(PublishProfiles.Of(uat.Environment, root.Path)), Value(SqlServer.SqlCmdValues(uat))));

            Assert.Equal(project.Reader.Login, new SqlConnectionStringBuilder(uat.Connection).UserID);
            Assert.Contains(model, e => e.Key.ToString() == "Column [dbo].[Customer].[Email]");
            Assert.True(plan.Report.IsEmpty, "the plan of the package against the environment it was published to has operations:\n" + string.Join('\n', plan.Report.Operations));
            Assert.Contains(":setvar EnvironmentTag \"uat\"", plan.Script, StringComparison.Ordinal);
            token.AbsentFrom(plan.Script + plan.Report + plan);
            Assert.Equal([("VIEW DEFINITION", "1")], Entries(File.ReadAllText(log.Path)).Select(e => (e.Site, e.Outcome)));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(variable, null);
        }
    }

    /// <summary>
    /// The golden project's copy holds the read-only principal's SQL login and a user for it. Read twice as the fixture's admin
    /// identity, who sees the login, and twice as the read-only principal, it fingerprints once per identity: SQL Server never
    /// returns a login's password, DacFx makes a new one up on each load, and Ssdt.Elements leaves that property out.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Law", "3′ the model is complete")]
    [Trait("Exit", "M1.4")]
    public void One_database_read_twice_by_one_identity_fingerprints_equally()
    {
        var (admin, reader) = (Resolved("uat", project.Copy.ConnectionString), Resolved("qa", project.Reader.ConnectionString));

        var (first, second) = (Extracted(admin), Extracted(admin));
        var (third, fourth) = (Extracted(reader), Extracted(reader));

        Assert.Contains(first, e => e.Key.ToString() == "Login [" + project.Reader.Login + "]");
        Assert.Equal(Fingerprint.Of(first), Fingerprint.Of(second));
        Assert.Equal(Fingerprint.Of(third), Fingerprint.Of(fourth));
    }

    /// <summary>R16: a denied login is one sentence naming the environment and saying a lead's prediction will appear on the pull request; it quotes neither the login nor the password.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "X2")]
    [Trait("Exit", "M1.7")]
    public void A_denied_login_names_the_environment_and_quotes_nothing()
    {
        var wrong = new PlantedValue("Wr0ng!planted#7f3a");
        var qa = Resolved("qa", new SqlConnectionStringBuilder(project.Reader.ConnectionString) { Password = wrong.Text }.ConnectionString);

        var errors = new[] { Failed(SqlServer.Reach(qa), "server.denied"), Failed(DacFx.Extract(qa), "server.denied"), Failed(SqlServer.Measure(qa, Value(SqlServer.AggregateQuery.Of("SELECT 1;", "the login")), SqlServer.QueryLog.Start(root.Path)), "server.denied") };

        Assert.All(errors, error =>
        {
            Assert.StartsWith("env:qa ", error.Message, StringComparison.Ordinal);
            Assert.Contains("a lead's prediction will appear on the pull request", error.Message, StringComparison.Ordinal);
            wrong.AbsentFrom(error);
            new PlantedValue(project.Reader.Login).AbsentFrom(error);
        });
    }

    /// <summary>
    /// A login that signs in and lacks VIEW DEFINITION on the database, as a group with CONNECT alone has: the check estate sends before
    /// DacFx runs (HAS_PERMS_BY_NAME) answers 0, and the environment is denied as SQL Server would deny it (Msg 300), before any read.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Exit", "M1.7")]
    public async Task A_login_without_VIEW_DEFINITION_is_denied_before_DacFx_runs()
    {
        var (login, password) = (project.Copy.Name + "_connect", "Co1!" + Guid.NewGuid().ToString("N"));
        await SqlServerFixture.ExecuteAsync(project.Copy.ConnectionString, "DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@name) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') "
            + "+ N', DEFAULT_DATABASE = ' + QUOTENAME(DB_NAME()) + N'; CREATE USER ' + QUOTENAME(@name) + N' FOR LOGIN ' + QUOTENAME(@name) + N';'; EXEC (@sql);", login, ("@password", password));
        try
        {
            var dev = Resolved("dev", new SqlConnectionStringBuilder(project.Reader.ConnectionString) { UserID = login, Password = password }.ConnectionString);

            var error = Failed(SqlServer.Reach(dev), "server.denied");

            Assert.StartsWith("env:dev refused this identity (Msg 300", error.Message, StringComparison.Ordinal);
            new PlantedValue(password).AbsentFrom(error);
        }
        finally
        {
            await SqlServerFixture.ExecuteAsync(project.Copy.ConnectionString, "DECLARE @sql nvarchar(max) = N'DROP USER ' + QUOTENAME(@name) + N'; DROP LOGIN ' + QUOTENAME(@name) + N';'; EXEC (@sql);", login);
        }
    }

    /// <summary>A connection whose Initial Catalog names a database the login cannot open, or that does not exist: SqlClient raises Msg 4060, which is a denial.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    public void A_database_the_login_cannot_open_is_denied_by_its_number()
    {
        var dev = Resolved("dev", new SqlConnectionStringBuilder(project.Reader.ConnectionString) { InitialCatalog = "estate_no_such_database_" + Guid.NewGuid().ToString("N")[..8] }.ConnectionString);

        var error = Failed(SqlServer.Reach(dev), "server.denied");

        Assert.StartsWith("env:dev refused this identity (Msg 4060", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A named environment classified real, its connection the given string in a file outside git, resolved through estate/posture.json under the test's root.</summary>
    private SqlServer.Database Resolved(string name, string connection, System.Collections.Generic.Dictionary<string, PostureFile.SqlCmd>? sqlcmd = null)
    {
        var file = root.File(name + ".connection", connection);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);   // io/SqlServer refuses a connection file others can read
        }

        PostureFile.Of(name, new("file:" + file.Replace('\\', '/'), "localhost", Classification: "real", Sqlcmd: sqlcmd)).WriteTo(root.Path);
        File.Copy(project.Profile, Path.Combine(root.Path, "estate", "profiles", "pipeline.publish.xml"), overwrite: true);
        return Value(SqlServer.Resolve(Value(SqlServer.Target("env:" + name, "--target")), root.Path));
    }

    /// <summary>queries.log as entries: a header line naming the site and the outcome (a row count, or the failure's number), the statement, then GO.</summary>
    private static (string Site, string Statement, string Outcome)[] Entries(string log) => [.. Regex.Matches(log, @"^-- \S+ \S+ (?<site>.+): (?<outcome>\d+|failed, Msg \d+)(?: rows?)?\n(?<statement>(?:(?!GO\n).*\n)+?)GO\n", RegexOptions.Multiline | RegexOptions.CultureInvariant)
        .Select(m => (m.Groups["site"].Value, m.Groups["statement"].Value.TrimEnd('\n'), m.Groups["outcome"].Value))];

    /// <summary>A database's elements, as io reads one: extracted once, as its identity.</summary>
    private static SortedArray<Element> Extracted(SqlServer.Database database)
    {
        using var package = Value(DacFx.Extract(database));
        return Value(package.Elements).Elements;
    }
}
