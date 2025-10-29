using System;
using System.Collections.Generic;
using Osm.Smo.PerTableEmission;
using Xunit;

namespace Osm.Smo.Tests.PerTableEmission;

public class ConstraintFormatterTests
{
    private readonly ConstraintFormatter _formatter = new();

    [Fact]
    public void FormatForeignKeyConstraints_reflows_segments_and_appends_trust_comment()
    {
        var script = """
CREATE TABLE [dbo].[Order](
    [Id] INT NOT NULL,
    CONSTRAINT [FK_Order_City_CityId] FOREIGN KEY ([CityId]) REFERENCES [dbo].[City]([Id]) ON DELETE CASCADE,
    [Name] NVARCHAR(100)
);
""".Trim();

        var lookup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["FK_Order_City_CityId"] = true,
        };

        var formatted = _formatter.FormatForeignKeyConstraints(script, lookup);

        Assert.Contains("FOREIGN KEY ([CityId]) REFERENCES", formatted, StringComparison.Ordinal);
        Assert.Contains("ON DELETE CASCADE", formatted, StringComparison.Ordinal);
        Assert.Contains("-- Source constraint was not trusted", formatted, StringComparison.Ordinal);
        Assert.Contains("\n        FOREIGN KEY", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatPrimaryKeyConstraints_reflows_primary_key_body()
    {
        var script = """
CREATE TABLE [dbo].[Order](
    [Id] INT NOT NULL,
    CONSTRAINT [PK_Order_Id_Status] PRIMARY KEY CLUSTERED ([Id] ASC, [Status] ASC),
    [Status] INT NOT NULL
);
""".Trim();

        var formatted = _formatter.FormatPrimaryKeyConstraints(script);

        Assert.Contains("    CONSTRAINT [PK_Order_Id_Status]\n        PRIMARY KEY CLUSTERED", formatted, StringComparison.Ordinal);
    }
}
