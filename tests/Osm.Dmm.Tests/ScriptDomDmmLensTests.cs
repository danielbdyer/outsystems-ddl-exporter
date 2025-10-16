using System;
using System.IO;
using System.Linq;
using Osm.Dmm;
using Tests.Support;
using Xunit;

namespace Osm.Dmm.Tests;

public class ScriptDomDmmLensTests
{
    [Fact]
    public void Project_includes_unique_constraints_from_create_table_and_alter_table()
    {
        using var stream = FixtureFile.OpenRead("dmm/unique-constraint.dmm.sql");
        using var reader = new StreamReader(stream);
        var lens = new ScriptDomDmmLens();
        var result = lens.Project(reader);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Errors.Select(e => $"{e.Code}:{e.Message}")));

        var tables = result.Value;
        var customers = Assert.Single(tables, t => string.Equals(t.Name, "CUSTOMERS", StringComparison.OrdinalIgnoreCase));
        var customerUnique = Assert.Single(customers.Indexes, index => string.Equals(index.Name, "UQ_Customers_Email", StringComparison.OrdinalIgnoreCase));
        Assert.True(customerUnique.IsUnique);
        Assert.Equal(new[] { "EMAIL" }, customerUnique.KeyColumns.Select(column => column.Name));

        var users = Assert.Single(tables, t => string.Equals(t.Name, "USERS", StringComparison.OrdinalIgnoreCase));
        var userUnique = Assert.Single(users.Indexes, index => string.Equals(index.Name, "UQ_Users_Username", StringComparison.OrdinalIgnoreCase));
        Assert.True(userUnique.IsUnique);
        Assert.Equal(new[] { "USERNAME" }, userUnique.KeyColumns.Select(column => column.Name));
    }
}
