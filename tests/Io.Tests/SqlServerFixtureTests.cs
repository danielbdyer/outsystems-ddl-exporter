using System.Threading.Tasks;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// The fixture's promise, proven inside one test rather than by two tests the runner schedules at once: two databases registered at
/// the same time get two names on the one server, each holds a table of its own, both are gone once disposed, and a second disposal
/// is no error.
/// </summary>
public sealed class SqlServerFixtureTests
{
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Two_databases_registered_at_once_get_two_names_each_holds_its_own_table_and_both_are_dropped_when_disposed()
    {
        var registered = await Task.WhenAll(SqlServerFixture.RegisterAsync(), SqlServerFixture.RegisterAsync());
        try
        {
            Assert.NotEqual(registered[0].Name, registered[1].Name);
            foreach (var database in registered)
            {
                await SqlServerFixture.ExecuteAsync(database.ConnectionString, "CREATE TABLE dbo.Proof (Id INT NOT NULL PRIMARY KEY); INSERT dbo.Proof (Id) VALUES (1);");
                Assert.Equal(1, await SqlServerFixture.ScalarAsync(database.ConnectionString, "SELECT COUNT(*) FROM dbo.Proof;"));
            }
        }
        finally
        {
            foreach (var database in registered)
            {
                await database.DisposeAsync();
            }
        }

        foreach (var database in registered)
        {
            Assert.False(await SqlServerFixture.ExistsAsync(database.Name), database.Name + " was not dropped");
            await database.DisposeAsync();
        }
    }
}
