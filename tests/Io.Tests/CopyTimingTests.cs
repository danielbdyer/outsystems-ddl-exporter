using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Xunit;
using Xunit.Abstractions;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// Spike S4 (V3_MILESTONES.md §6): how long io/LocalServer takes to CREATE a copy, publish a 300-table schema to it from scratch and
/// again with nothing to do, BACKUP it into the copy cache, RESTORE the backup into a second copy, and DROP both, on the run's local
/// server: SQL Server 2022 Express on Ubuntu, LocalDB 17.0 on windows-latest. It asserts no time. It prints one line per run, beginning
/// "S4 |", in the test's output, which `dotnet test --logger "console;verbosity=detailed"` shows for a passing test, and appends it as a
/// row of a table to GITHUB_STEP_SUMMARY where CI sets it. The schema is generated here from the golden project's shapes (dbo.Customer and
/// dbo.OrderLine): an IDENTITY key, a required and an optional string, an amount with a default, and a nullable reference to the table
/// before it with its index; every tenth table carries a CHECK. It has no post-deployment seed, so the SSDT repository's MERGEs are not
/// in the publish time; the laptop half against the real schema is a person's (NEXT.md).
/// </summary>
public sealed class CopyTimingTests(ITestOutputHelper output) : IDisposable
{
    private const int Tables = 300;

    private readonly string root = SqlServerFixture.RepositoryRoot(Path.Combine(Repository.Root, ".dbchange", "copies-under-test",
        Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]));

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Spike", "S4")]
    public async Task S4_a_copy_of_three_hundred_tables_is_created_published_backed_up_restored_and_dropped_with_each_step_timed()
    {
        var server = await SqlServerFixture.ServerAsync();
        var strict = Value(PublishProfiles.Load(GoldenProject.Profile));
        var dacpac = Path.Combine(root, "s4.dacpac");
        var key = Fingerprint.Of("S4 " + Guid.NewGuid().ToString("N"));
        var log = SqlServer.QueryLog.Start(root);
        var timings = new List<(string Step, long Ms)>();
        T Timed<T>(string step, Func<T> run)
        {
            var clock = Stopwatch.StartNew();
            var done = run();
            timings.Add((step, clock.ElapsedMilliseconds));
            return done;
        }

        Timed("model and package", () => Packaged(dacpac));
        var published = Timed("create", () => Value(LocalServer.Create(root, server, log)));
        SqlServer.Copy? restored = null;
        var dropped = false;
        try
        {
            Timed("publish", () => Value(published.Publish(dacpac, strict, log)));
            Timed("republish", () => Value(published.Publish(dacpac, strict, log)));
            Timed("backup", () => Value(LocalServer.BackUp(published, key, log)));
            restored = Timed("restore", () => Value(LocalServer.Restore(root, server, key, log)));

            Assert.Equal(Tables, await SqlServerFixture.ScalarAsync(restored.Connection, "SELECT COUNT(*) FROM sys.tables;"));
            Assert.Equal(await SqlServerFixture.ScalarAsync(published.Connection, "SELECT COUNT(*) FROM sys.foreign_keys;"),
                await SqlServerFixture.ScalarAsync(restored.Connection, "SELECT COUNT(*) FROM sys.foreign_keys;"));
            var reported = Value(SqlServer.ServerOf(published, log));
            Timed("drop restored", () => Value(LocalServer.Drop(restored, log)));
            Timed("drop published", () => Value(LocalServer.Drop(published, log)));
            dropped = true;

            Assert.False(await SqlServerFixture.ExistsAsync(restored.Name.ToString())
                || await SqlServerFixture.ExistsAsync(published.Name.ToString()), "a copy outlived its DROP");
            Report(Value(LocalServer.ServerName(server)) + " | SQL Server " + reported.Version + " " + reported.Edition + " | "
                + Tables.ToString(CultureInfo.InvariantCulture) + " tables", timings);
        }
        finally
        {
            if (!dropped)
            {
                // a DROP of a database already gone is no error, so each copy is dropped whatever step failed
                foreach (var left in new[] { restored, published }.OfType<SqlServer.Copy>())
                {
                    LocalServer.Drop(left, log);
                }
            }

            await SqlServerFixture.DeleteBackupsAsync(root);
        }
    }

    /// <summary>The 300 tables built into a Sql160 package at <paramref name="dacpac"/>, through DacFx's model as a build would.</summary>
    private static string Packaged(string dacpac)
    {
        using var model = new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions());
        for (var table = 1; table <= Tables; table++)
        {
            var name = "OSUSR_S4_T" + table.ToString("000", CultureInfo.InvariantCulture);
            var parent = "OSUSR_S4_T" + (table - 1).ToString("000", CultureInfo.InvariantCulture);
            model.AddObjects("CREATE TABLE dbo." + name + " (Id INT IDENTITY(1,1) NOT NULL, Name NVARCHAR(100) NOT NULL, "
                + "Email NVARCHAR(256) NULL, Amount DECIMAL(18, 2) NOT NULL CONSTRAINT DF_" + name + "_Amount DEFAULT (0), "
                + "IsActive BIT NOT NULL CONSTRAINT DF_" + name + "_IsActive DEFAULT (1), ParentId INT NULL, "
                + "CONSTRAINT PK_" + name + " PRIMARY KEY CLUSTERED (Id)"
                + (table > 1 ? ", CONSTRAINT FK_" + name + "_Parent FOREIGN KEY (ParentId) REFERENCES dbo." + parent + " (Id)" : "")
                + (table % 10 == 0 ? ", CONSTRAINT CK_" + name + "_Amount CHECK (Amount >= 0)" : "") + ");");
            model.AddObjects("CREATE INDEX IX_" + name + "_ParentId ON dbo." + name + " (ParentId);");
        }

        DacPackageExtensions.BuildPackage(dacpac, model, new PackageMetadata());
        return dacpac;
    }

    /// <summary>The S4 line in the test's output and, where GitHub Actions gives a step summary, a row of its table.</summary>
    private void Report(string where, IReadOnlyList<(string Step, long Ms)> timings)
    {
        output.WriteLine("S4 | " + where + " | " + string.Join(" | ",
            timings.Select(t => t.Step + " " + t.Ms.ToString(CultureInfo.InvariantCulture) + " ms")));
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
        {
            File.AppendAllText(summary, (File.Exists(summary) && new FileInfo(summary).Length > 0 ? ""
                : "| S4: the local server | " + string.Join(" | ", timings.Select(t => t.Step + " (ms)")) + " |\n"
                    + "|" + string.Concat(Enumerable.Repeat("---|", timings.Count + 1)) + "\n")
                + "| " + where + " | " + string.Join(" | ", timings.Select(t => t.Ms.ToString(CultureInfo.InvariantCulture))) + " |\n");
        }
    }
}
