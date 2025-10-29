using Osm.Smo;
using Osm.Smo.PerTableEmission;
using Xunit;

namespace Osm.Smo.Tests.PerTableEmission;

public class SqlScriptFormatterFacadeTests
{
    [Fact]
    public void JoinStatements_inserts_go_batches_and_normalizes_whitespace()
    {
        var formatter = new SqlScriptFormatter();
        var statements = new[]
        {
            "SELECT 1    ",
            "SELECT 2"
        };

        var script = formatter.JoinStatements(statements, SmoFormatOptions.Default);

        Assert.Contains("GO", script);
        Assert.DoesNotContain("SELECT 1    ", script);
    }
}
