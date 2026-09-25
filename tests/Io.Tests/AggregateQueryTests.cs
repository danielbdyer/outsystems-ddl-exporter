using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Estate.Budgets.Tests;
using Estate.Cli;
using Estate.Kernel;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// io/SqlServer against a named environment (V3_MILESTONES.md WP 1.4, M1 exit 7, §18; VALUES.md P2, X2): the golden project's
/// registered database stands in for env:uat, classified real and reached as the read-only principal through a file: reference. SqlServer.Measure
/// runs each admitted aggregate query of the allowlist's corpus there and reads integers back; every statement and its row count go to
/// the run's queries.log; a failed query reports its number and its site and nothing else; Model and Plan read as the same principal;
/// and a denied login names the environment and quotes nothing.
/// </summary>
public sealed class AggregateQueryTests(GoldenProject project) : IClassFixture<GoldenProject>, IDisposable
{
    private readonly string root = Directory.CreateDirectory(Path.Combine(Repository.Root, ".estate", "aggregate-queries-under-test", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    [Trait("Category", "fixture")]
    public void Every_admitted_query_of_the_corpus_returns_integers_as_the_read_only_principal_and_queries_log_holds_each_statement_and_its_row_count()
    {
        var uat = Resolved("uat", project.Reader.ConnectionString);
        var log = SqlServer.QueryLog.Start(root);
        var admitted = AllowlistTests.Cases.Where(c => c.Admitted).Select(c => Made(SqlServer.AggregateQuery.Of(c.Text, c.Label))).ToList();

        var measured = admitted.Select(query => Assert.IsType<SqlServer.Measurement.Answered>(Made(SqlServer.Measure(uat, query, log)))).ToList();

        Assert.All(measured, m => Assert.NotEmpty(m.Rows));
        Assert.Contains(measured, m => m.Rows.Any(row => row.Any(value => value > 0)));
        Assert.StartsWith(Path.Combine(root, ".estate", "runs"), log.Path, StringComparison.Ordinal);
        var entries = Entries(File.ReadAllText(log.Path));
        Assert.Equal(admitted.Select(p => (p.Site, p.Statement)), entries.Select(e => (e.Site, e.Statement)));
        Assert.Equal(measured.Select(m => m.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)), entries.Select(e => e.Outcome));
    }

    /// <summary>§18: SQL Server writes the value it fails to convert into Msg 245. Against an environment classified real, the executor records the number and the site, and the value appears in no result, message or line of the log.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_failed_query_against_a_real_environment_reports_only_its_number_and_its_site()
    {
        var planted = "planted-" + Guid.NewGuid().ToString("N")[..12];
        await SqlServerFixture.ExecuteAsync(project.Copy.ConnectionString, "CREATE TABLE dbo.Planted (Id INT NOT NULL PRIMARY KEY, Value NVARCHAR(100) NOT NULL); INSERT dbo.Planted (Id, Value) VALUES (1, @name);", planted);
        const string Statement = "SELECT SUM(CASE WHEN Value > 0 THEN 1 ELSE 0 END) FROM dbo.Planted;";
        Assert.Contains(planted, (await Assert.ThrowsAsync<SqlException>(() => SqlServerFixture.ScalarAsync(project.Copy.ConnectionString, Statement))).Message, StringComparison.Ordinal);
        var uat = Resolved("uat", project.Reader.ConnectionString);
        var log = SqlServer.QueryLog.Start(root);

        var failed = Assert.IsType<SqlServer.Measurement.Failed>(Made(SqlServer.Measure(uat, Made(SqlServer.AggregateQuery.Of(Statement, "dbo.Planted.Value Fits")), log)));

        Assert.Equal((245, "dbo.Planted.Value Fits", (string?)null), (failed.Number, failed.Site, failed.Message));
        Assert.Equal("dbo.Planted.Value Fits: query failed: Msg 245; message withheld", failed.ToString());
        Assert.Equal("failed, Msg 245", Assert.Single(Entries(File.ReadAllText(log.Path))).Outcome);
        Assert.DoesNotContain(planted, failed + File.ReadAllText(log.Path), StringComparison.Ordinal);
    }

    /// <summary>
    /// §1 facts 2, 4 and 5 through io, as the read-only principal: Model reads the environment whole, and the plan of the package it was
    /// published from is empty. The environment's SQLCMD values reach the plan; the kept script holds the literal and never the value a
    /// reference resolved to, and the run's log holds the one statement estate sent before DacFx's own.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public void The_model_and_the_plan_of_a_named_environment_read_as_the_read_only_principal()
    {
        var (variable, token) = ("ESTATE_TEST_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "planted-token-" + Guid.NewGuid().ToString("N")[..8]);
        System.Environment.SetEnvironmentVariable(variable, token);
        try
        {
            var uat = Assert.IsType<SqlServer.EnvironmentDatabase>(Resolved("uat", project.Reader.ConnectionString,
                ", \"sqlcmd\": { \"EnvironmentTag\": { \"literal\": \"uat\", \"sensitive\": false }, \"ServiceToken\": \"env:" + variable + "\" }"));
            var log = SqlServer.QueryLog.Start(root);

            var model = Made(SqlServer.Model(uat, log));
            var plan = Made(SqlServer.Plan(project.Base, uat, Made(Profiles.Of(uat.Environment, root)), log));

            Assert.Equal(project.Reader.Login, new SqlConnectionStringBuilder(uat.Connection).UserID);
            Assert.Contains(model, e => e.Key.ToString() == "Column [dbo].[Customer].[Email]");
            Assert.True(plan.IsEmpty, "the plan of the package against the environment it was published to has operations:\n" + plan.Report);
            Assert.Contains(":setvar EnvironmentTag \"uat\"", plan.Script, StringComparison.Ordinal);
            Assert.DoesNotContain(token, plan.Script + plan.Report + plan, StringComparison.Ordinal);
            Assert.Equal([("VIEW DEFINITION", "1"), ("VIEW DEFINITION", "1")], Entries(File.ReadAllText(log.Path)).Select(e => (e.Site, e.Outcome)));
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
    public void One_database_read_twice_by_one_identity_fingerprints_equally()
    {
        var (admin, reader) = (Resolved("uat", project.Copy.ConnectionString), Resolved("qa", project.Reader.ConnectionString));

        var (first, second) = (Made(SqlServer.Model(admin)), Made(SqlServer.Model(admin)));
        var (third, fourth) = (Made(SqlServer.Model(reader)), Made(SqlServer.Model(reader)));

        Assert.Contains(first, e => e.Key.ToString() == "Login [" + project.Reader.Login + "]");
        Assert.Equal(Fingerprint.Of(first), Fingerprint.Of(second));
        Assert.Equal(Fingerprint.Of(third), Fingerprint.Of(fourth));
    }

    /// <summary>M1 exit 7, R16: a denied login is one sentence naming the environment and saying a lead's prediction will appear on the pull request, at exit 4; it quotes neither the login nor the password.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    public void A_denied_login_names_the_environment_and_quotes_nothing()
    {
        const string Wrong = "Wr0ng!planted#7f3a";
        var qa = Resolved("qa", new SqlConnectionStringBuilder(project.Reader.ConnectionString) { Password = Wrong }.ConnectionString);

        var errors = new[] { Failed(SqlServer.Model(qa)), Failed(SqlServer.Measure(qa, Made(SqlServer.AggregateQuery.Of("SELECT 1;", "the login")), SqlServer.QueryLog.Start(root))) };

        Assert.All(errors, error =>
        {
            Assert.Equal(("server.denied", 4), (error.Code, Contract.Exit(error)));
            Assert.StartsWith("env:qa ", error.Message, StringComparison.Ordinal);
            Assert.Contains("a lead's prediction will appear on the pull request", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Wrong, error.Message + error.Remedy, StringComparison.Ordinal);
            Assert.DoesNotContain(project.Reader.Login, error.Message + error.Remedy, StringComparison.Ordinal);
        });
    }

    /// <summary>A named environment classified real, its connection the given string in a file outside git, resolved through estate/posture.json under the test's root.</summary>
    private SqlServer.Database Resolved(string name, string connection, string extra = "")
    {
        var file = Path.Combine(root, name + ".connection");
        File.WriteAllText(file, connection);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);   // io/SqlServer refuses a connection file others can read
        }

        Directory.CreateDirectory(Path.Combine(root, "estate", "profiles"));
        File.Copy(project.Profile, Path.Combine(root, "estate", "profiles", "pipeline.publish.xml"), overwrite: true);
        File.WriteAllText(Path.Combine(root, "estate", "posture.json"), "{ \"environments\": { \"" + name + "\": { \"host\": \"localhost\", \"classification\": \"real\", \"connection\": \"file:"
            + file.Replace('\\', '/') + "\", \"profile\": \"estate/profiles/pipeline.publish.xml\"" + extra + " } } }");
        return Made(SqlServer.Resolve(Made(SqlServer.Target("env:" + name, "--target")), root));
    }

    /// <summary>queries.log as entries: a header line naming the site and the outcome (a row count, or the failure's number), the statement, then GO.</summary>
    private static (string Site, string Statement, string Outcome)[] Entries(string log) => [.. Regex.Matches(log, @"^-- \S+ \S+ (?<site>.+): (?<outcome>\d+|failed, Msg \d+)(?: rows?)?\n(?<statement>(?:(?!GO\n).*\n)+?)GO\n", RegexOptions.Multiline | RegexOptions.CultureInvariant)
        .Select(m => (m.Groups["site"].Value, m.Groups["statement"].Value.TrimEnd('\n'), m.Groups["outcome"].Value))];

    private static T Made<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));

    private static Error Failed<T>(Result<T> result) => Assert.IsType<Result<T>.Failed>(result).Error;
}
