using Osm.Smo;
using Osm.Smo.PerTableEmission;
using Xunit;

namespace Osm.Smo.Tests.PerTableEmission;

public class StatementBatchFormatterTests
{
    private readonly StatementBatchFormatter _formatter = new();

    [Fact]
    public void JoinStatements_inserts_go_batches_and_normalizes_whitespace()
    {
        var statements = new[]
        {
            "SELECT 1    ",
            "SELECT 2"
        };

        var script = _formatter.JoinStatements(statements, SmoFormatOptions.Default);

        Assert.Contains("GO", script);
        Assert.DoesNotContain("SELECT 1    ", script);
    }

    [Fact]
    public void NormalizeWhitespace_trims_trailing_spaces()
    {
        var script = "SELECT 1    \nSELECT 2";

        var normalized = _formatter.NormalizeWhitespace(script);

        Assert.Equal("SELECT 1\nSELECT 2", normalized);
    }
}
