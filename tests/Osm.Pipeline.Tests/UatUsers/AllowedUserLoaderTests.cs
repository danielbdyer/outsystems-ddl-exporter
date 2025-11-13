using System.IO;
using System.Linq;
using Osm.Pipeline.UatUsers;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests.UatUsers;

public sealed class AllowedUserLoaderTests
{
    [Fact]
    public void Load_TreatsCsvUserDdlAsListInput()
    {
        using var temp = new TempDirectory();
        var csvPath = Path.Combine(temp.Path, "dbo.User.csv");
        File.WriteAllLines(csvPath, new[]
        {
            "Id,Name",
            "1,A",
            "2,B"
        });

        var result = AllowedUserLoader.Load(
            ddlPath: csvPath,
            userIdsPath: null,
            userSchema: "dbo",
            userTable: "User",
            userIdColumn: "Id");

        Assert.Equal(new[] { "1", "2" }, result.UserIds.Select(id => id.Value));
        Assert.Equal(0, result.SqlRowCount);
        Assert.Equal(2, result.ListRowCount);
    }

    [Fact]
    public void Load_TreatsSqlSeedAsSql()
    {
        using var temp = new TempDirectory();
        var sqlPath = Path.Combine(temp.Path, "dbo.User.sql");
        File.WriteAllText(sqlPath, @"INSERT INTO [dbo].[User] ([Id], [Name]) VALUES (1, 'Admin');
INSERT INTO [dbo].[User] ([Id], [Name]) VALUES (2, 'Operator');");

        var result = AllowedUserLoader.Load(
            ddlPath: sqlPath,
            userIdsPath: null,
            userSchema: "dbo",
            userTable: "User",
            userIdColumn: "Id");

        Assert.Equal(new[] { "1", "2" }, result.UserIds.Select(id => id.Value));
        Assert.Equal(2, result.SqlRowCount);
        Assert.Equal(0, result.ListRowCount);
    }
}
