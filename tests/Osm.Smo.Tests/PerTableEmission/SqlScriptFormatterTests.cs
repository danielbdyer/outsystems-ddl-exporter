using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.TransactSql.ScriptDom;
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

    [Fact]
    public void FormatCreateTableScript_reflows_inline_defaults_constraints_and_primary_keys()
    {
        var formatter = new SqlScriptFormatter();
        var statement = CreateMinimalCreateTableStatement("Id", "Status", "Code", "CustomerId");

        var script = """
CREATE TABLE [dbo].[Order](
    [Id] INT NOT NULL CONSTRAINT [PK_Order_Id] PRIMARY KEY CLUSTERED ,
    [Status] INT NOT NULL CONSTRAINT [CK_Order_Status] CHECK ([Status] >= (0))  ,
    [Code] NVARCHAR(20) CONSTRAINT [DF_Order_Code] DEFAULT ((N''))   ,
    [CustomerId] INT NOT NULL,
    CONSTRAINT [PK_Order_Id_Status] PRIMARY KEY CLUSTERED ([Id] ASC, [Status] ASC) ,
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])
);
""".Trim();

        var formatted = formatter.FormatCreateTableScript(script, statement, foreignKeyTrustLookup: null, SmoFormatOptions.Default);

        Assert.Contains("    [Id] INT NOT NULL\n        CONSTRAINT [PK_Order_Id]\n            PRIMARY KEY CLUSTERED,", formatted);
        Assert.Contains("    [Status] INT NOT NULL\n        CONSTRAINT [CK_Order_Status] CHECK ([Status] >= (0)),", formatted);
        Assert.Contains("    [Code] NVARCHAR(20)\n        CONSTRAINT [DF_Order_Code] DEFAULT ((N'')),", formatted);
        Assert.Contains("    CONSTRAINT [PK_Order_Id_Status]\n        PRIMARY KEY CLUSTERED ([Id] ASC, [Status] ASC),", formatted);
        Assert.Contains("    CONSTRAINT [FK_Order_Customer]\n        FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])", formatted);
        Assert.DoesNotContain("   ,", formatted);
    }

    [Fact]
    public void FormatCreateTableScript_preserves_trust_comment_when_lookup_marks_foreign_key_as_not_trusted()
    {
        var formatter = new SqlScriptFormatter();
        var statement = CreateMinimalCreateTableStatement("Id", "CustomerId");
        var lookup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["FK_Order_Customer"] = true,
        };

        var script = """
CREATE TABLE [dbo].[Order](
    [Id] INT NOT NULL,
    [CustomerId] INT NOT NULL,
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])
);
""".Trim();

        var formatted = formatter.FormatCreateTableScript(script, statement, lookup, SmoFormatOptions.Default);

        Assert.Contains("    CONSTRAINT [FK_Order_Customer]\n        FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])", formatted);
        Assert.Contains("-- Source constraint was not trusted (WITH NOCHECK)", formatted);
    }

    [Fact]
    public void FormatCreateTableScript_skips_reflow_when_normalization_is_disabled()
    {
        var formatter = new SqlScriptFormatter();
        var statement = CreateMinimalCreateTableStatement("Id", "Status", "Code", "CustomerId");
        var format = SmoFormatOptions.Default.WithWhitespaceNormalization(normalize: false);

        var script = """
CREATE TABLE [dbo].[Order](
    [Id] INT NOT NULL CONSTRAINT [PK_Order_Id] PRIMARY KEY CLUSTERED ,
    [Status] INT NOT NULL CONSTRAINT [CK_Order_Status] CHECK ([Status] >= (0))  ,
    [Code] NVARCHAR(20) CONSTRAINT [DF_Order_Code] DEFAULT ((N''))   ,
    [CustomerId] INT NOT NULL,
    CONSTRAINT [PK_Order_Id_Status] PRIMARY KEY CLUSTERED ([Id] ASC, [Status] ASC) ,
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])
);
""".Trim();

        var formatted = formatter.FormatCreateTableScript(script, statement, foreignKeyTrustLookup: null, format);

        Assert.Equal(script, formatted);
    }

    [Fact]
    public void FormatCreateTableScript_serializes_nvarchar_max_columns_with_max_literal()
    {
        var formatter = new SqlScriptFormatter();
        var column = CreateSmoColumnDefinition("Description", DataType.NVarCharMax);

        var script = GenerateCreateTableScript(formatter, column);
        var normalized = script.Replace(" ", string.Empty, StringComparison.Ordinal);

        Assert.Contains("NVARCHAR(MAX)", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatCreateTableScript_serializes_varbinary_max_columns_with_max_literal()
    {
        var formatter = new SqlScriptFormatter();
        var column = CreateSmoColumnDefinition("Payload", DataType.VarBinaryMax);

        var script = GenerateCreateTableScript(formatter, column);
        var normalized = script.Replace(" ", string.Empty, StringComparison.Ordinal);

        Assert.Contains("VARBINARY(MAX)", normalized, StringComparison.Ordinal);
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

    private static string GenerateCreateTableScript(SqlScriptFormatter formatter, params SmoColumnDefinition[] columns)
    {
        var builder = new CreateTableStatementBuilder(formatter);
        var options = SmoBuildOptions.Default;

        var table = new SmoTableDefinition(
            Module: "Sales",
            OriginalModule: "Sales",
            Name: "OSUSR_SALES_ORDER",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Order",
            Description: null,
            Columns: columns.ToImmutableArray(),
            Indexes: ImmutableArray<SmoIndexDefinition>.Empty,
            ForeignKeys: ImmutableArray<SmoForeignKeyDefinition>.Empty,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        var statement = builder.BuildCreateTableStatement(table, table.LogicalName, options);

        var generator = new Sql150ScriptGenerator(new SqlScriptGeneratorOptions
        {
            KeywordCasing = KeywordCasing.Uppercase,
            IncludeSemicolons = true,
            SqlVersion = SqlVersion.Sql150,
        });

        generator.GenerateScript(statement, out var script);
        var trimmed = script.Trim();

        return formatter.FormatCreateTableScript(trimmed, statement, foreignKeyTrustLookup: null, options.Format);
    }

    private static SmoColumnDefinition CreateSmoColumnDefinition(string name, DataType dataType)
    {
        return new SmoColumnDefinition(
            PhysicalName: name.ToUpperInvariant(),
            Name: name,
            LogicalName: name,
            DataType: dataType,
            Nullable: true,
            IsIdentity: false,
            IdentitySeed: 0,
            IdentityIncrement: 0,
            IsComputed: false,
            ComputedExpression: null,
            DefaultExpression: null,
            Collation: null,
            Description: null,
            DefaultConstraint: null,
            CheckConstraints: ImmutableArray<SmoCheckConstraintDefinition>.Empty);
    }
}
