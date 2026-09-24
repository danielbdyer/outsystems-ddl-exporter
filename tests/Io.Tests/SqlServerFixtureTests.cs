using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>A registered database is named for its host and its process, lower case, in [a-z0-9_] only.</summary>
public sealed class RegisteredDatabaseNameTests
{
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("DANNY-PC", 4242, "0a1b2c3d", "estate_danny_pc_4242_0a1b2c3d")]
    [InlineData("runner.corp.example", 7, "ffffffff", "estate_runner_corp_example_7_ffffffff")]
    [InlineData("ÉTÉ", 1, "00000000", "estate__t__1_00000000")]
    public void A_registered_database_is_named_for_its_host_and_process(string host, int pid, string random, string name) =>
        Assert.Equal(name, SqlServerFixture.DatabaseName(host, pid, random));
}

/// <summary>
/// The fixture's promise, proven by two tests xUnit runs at the same time (each class is its own collection): each gets its
/// own registered database on the one server, each creates a table in it, and each database is dropped after its test.
/// </summary>
internal static class TwoTestsAtOnce
{
    public static readonly TaskCompletionSource<string> First = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static readonly TaskCompletionSource<string> Second = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Shows this test's database to the other test and waits for the other's, then makes a table in its own; returns the other's name.</summary>
    public static async Task<string> Meet(TaskCompletionSource<string> mine, TaskCompletionSource<string> theirs, RegisteredDatabase database)
    {
        mine.SetResult(database.Name);
        var other = await theirs.Task.WaitAsync(TimeSpan.FromMinutes(3));   // a timeout: the other test was not running at the same time

        Assert.NotEqual(database.Name, other);
        await SqlServerFixture.ExecuteAsync(database.ConnectionString, "CREATE TABLE dbo.Proof (Id INT NOT NULL PRIMARY KEY); INSERT dbo.Proof (Id) VALUES (1);");
        Assert.Equal(1, await SqlServerFixture.ScalarAsync(database.ConnectionString, "SELECT COUNT(*) FROM dbo.Proof;"));
        return other;
    }
}

public sealed class FirstOfTwoTestsAtOnce : IAsyncLifetime
{
    private RegisteredDatabase? database;

    public async Task InitializeAsync() => database = await SqlServerFixture.RegisterAsync();

    public async Task DisposeAsync() => await database!.DisposeAsync();

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Two_tests_at_once_get_two_databases_each_makes_a_table_and_each_database_is_dropped_after()
    {
        var other = await TwoTestsAtOnce.Meet(TwoTestsAtOnce.First, TwoTestsAtOnce.Second, database!);

        // The other test's database goes when that test ends; this one's goes by the same disposal, here, so it can be seen gone.
        var waited = Stopwatch.StartNew();
        while (await SqlServerFixture.ExistsAsync(other) && waited.Elapsed < TimeSpan.FromMinutes(2))
        {
            await Task.Delay(250);
        }

        Assert.False(await SqlServerFixture.ExistsAsync(other), other + " was not dropped after its test");
        await database!.DisposeAsync();
        Assert.False(await SqlServerFixture.ExistsAsync(database.Name), database.Name + " was not dropped");
    }
}

public sealed class SecondOfTwoTestsAtOnce : IAsyncLifetime
{
    private RegisteredDatabase? database;

    public async Task InitializeAsync() => database = await SqlServerFixture.RegisterAsync();

    public async Task DisposeAsync() => await database!.DisposeAsync();

    [Fact]
    [Trait("Category", "fixture")]
    public async Task The_second_test_gets_its_own_database_and_makes_a_table_in_it() =>
        await TwoTestsAtOnce.Meet(TwoTestsAtOnce.Second, TwoTestsAtOnce.First, database!);
}
