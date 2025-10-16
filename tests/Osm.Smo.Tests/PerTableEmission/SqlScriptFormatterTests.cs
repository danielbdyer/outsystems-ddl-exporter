using System.Collections.Generic;
using Osm.Smo;
using Osm.Smo.PerTableEmission;
using Xunit;

namespace Osm.Smo.Tests.PerTableEmission;

public class SqlScriptFormatterTests
{
    [Fact]
    public void FormatForeignKeyConstraints_reflows_segments_and_appends_trust_comment()
    {
        var formatter = new SqlScriptFormatter();
        var script = """
CREATE TABLE [dbo].[Order](
    [Id] INT NOT NULL,
    CONSTRAINT [FK_Order_City] FOREIGN KEY ([CityId]) REFERENCES [dbo].[City]([Id]) ON DELETE CASCADE,
    [Name] NVARCHAR(100)
);
""".Trim();

        var lookup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["FK_Order_City"] = true,
        };

        var formatted = formatter.FormatForeignKeyConstraints(script, lookup);

        Assert.Contains("FOREIGN KEY ([CityId]) REFERENCES", formatted);
        Assert.Contains("ON DELETE CASCADE", formatted);
        Assert.Contains("-- Source constraint was not trusted", formatted);
        Assert.Contains("\n        FOREIGN KEY", formatted);
    }

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
