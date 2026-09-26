using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Osm.Smo;
using Osm.Smo.PerTableEmission;
using Xunit;

namespace Osm.Smo.Tests.PerTableEmission;

public class CreateTableFormatterTests
{
    private readonly ConstraintFormatter _constraintFormatter = new();

    [Fact]
    public void FormatCreateTableScript_reflows_inline_defaults_constraints_and_primary_keys()
    {
        var formatter = new CreateTableFormatter(_constraintFormatter);
        var statement = CreateMinimalCreateTableStatement("Id", "Status", "Code", "CustomerId");

        var script = """
CREATE TABLE [dbo].[Order](
    [Id] INT NOT NULL CONSTRAINT [PK_Order_Id] PRIMARY KEY CLUSTERED ,
    [Status] INT NOT NULL CONSTRAINT [CK_Order_Status] CHECK ([Status] >= (0))  ,
    [Code] NVARCHAR(20) NULL CONSTRAINT [DF_Order_Code] DEFAULT ((N''))   ,
    [CustomerId] INT NOT NULL,
    CONSTRAINT [PK_Order_Id_Status] PRIMARY KEY CLUSTERED ([Id] ASC, [Status] ASC) ,
    CONSTRAINT [FK_Order_Customer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])
);
""".Trim();

        var formatted = formatter.FormatCreateTableScript(script, statement, foreignKeyTrustLookup: null, SmoFormatOptions.Default);

        Assert.Contains("    [Id] INT NOT NULL\n        CONSTRAINT [PK_Order_Id]\n            PRIMARY KEY CLUSTERED,", formatted, StringComparison.Ordinal);
        Assert.Contains("    [Status] INT NOT NULL\n        CONSTRAINT [CK_Order_Status] CHECK ([Status] >= (0)),", formatted, StringComparison.Ordinal);
        Assert.Contains("    [Code] NVARCHAR(20) NULL\n        CONSTRAINT [DF_Order_Code] DEFAULT ((N'')),", formatted, StringComparison.Ordinal);
        Assert.Contains("    CONSTRAINT [PK_Order_Id_Status]\n        PRIMARY KEY CLUSTERED ([Id] ASC, [Status] ASC),", formatted, StringComparison.Ordinal);
        Assert.Contains("    CONSTRAINT [FK_Order_Customer_CustomerId]\n        FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("   ,", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatCreateTableScript_preserves_trust_comment_when_lookup_marks_foreign_key_as_not_trusted()
    {
        var formatter = new CreateTableFormatter(_constraintFormatter);
        var statement = CreateMinimalCreateTableStatement("Id", "CustomerId");
        var lookup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["FK_Order_Customer_CustomerId"] = true,
        };

        var script = """
CREATE TABLE [dbo].[Order](
    [Id] INT NOT NULL,
    [CustomerId] INT NOT NULL,
    CONSTRAINT [FK_Order_Customer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])
);
""".Trim();

        var formatted = formatter.FormatCreateTableScript(script, statement, lookup, SmoFormatOptions.Default);

        Assert.Contains("    CONSTRAINT [FK_Order_Customer_CustomerId]\n        FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])", formatted, StringComparison.Ordinal);
        Assert.Contains("-- Source constraint was not trusted (WITH NOCHECK)", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatCreateTableScript_skips_reflow_when_normalization_is_disabled()
    {
        var formatter = new CreateTableFormatter(_constraintFormatter);
        var statement = CreateMinimalCreateTableStatement("Id", "Status", "Code", "CustomerId");
        var format = SmoFormatOptions.Default.WithWhitespaceNormalization(normalize: false);

        var script = """
CREATE TABLE [dbo].[Order](
    [Id] INT NOT NULL CONSTRAINT [PK_Order_Id] PRIMARY KEY CLUSTERED ,
    [Status] INT NOT NULL CONSTRAINT [CK_Order_Status] CHECK ([Status] >= (0))  ,
    [Code] NVARCHAR(20) NULL CONSTRAINT [DF_Order_Code] DEFAULT ((N''))   ,
    [CustomerId] INT NOT NULL,
    CONSTRAINT [PK_Order_Id_Status] PRIMARY KEY CLUSTERED ([Id] ASC, [Status] ASC) ,
    CONSTRAINT [FK_Order_Customer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])
);
""".Trim();

        var formatted = formatter.FormatCreateTableScript(script, statement, foreignKeyTrustLookup: null, format);

        Assert.Equal(script, formatted);
    }

    private static CreateTableStatement CreateMinimalCreateTableStatement(params string[] columnNames)
    {
        var tableDefinition = new TableDefinition();

        foreach (var columnName in columnNames)
        {
            tableDefinition.ColumnDefinitions.Add(new ColumnDefinition
            {
                ColumnIdentifier = new Identifier { Value = columnName },
                DataType = new SqlDataTypeReference { SqlDataTypeOption = SqlDataTypeOption.Int },
            });
        }

        return new CreateTableStatement
        {
            SchemaObjectName = new SchemaObjectName
            {
                Identifiers =
                {
                    new Identifier { Value = "dbo" },
                    new Identifier { Value = "Order" },
                }
            },
            Definition = tableDefinition,
        };
    }
}
