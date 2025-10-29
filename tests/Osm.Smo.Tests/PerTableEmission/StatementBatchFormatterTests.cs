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

public class StatementBatchFormatterTests
{
    private readonly ConstraintFormatter _constraintFormatter = new();
    private readonly IdentifierFormatter _identifierFormatter = new();
    private readonly StatementBatchFormatter _formatter;
    private readonly CreateTableStatementBuilder _createTableStatementBuilder;

    public StatementBatchFormatterTests()
    {
        _formatter = new StatementBatchFormatter(_constraintFormatter);
        _createTableStatementBuilder = new CreateTableStatementBuilder(_identifierFormatter, _constraintFormatter);
    }

    [Fact]
    public void JoinStatements_inserts_go_batches_and_normalizes_whitespace()
    {
        var statements = new[]
        {
            "SELECT 1    ",
            "SELECT 2",
        };

        var script = _formatter.JoinStatements(statements, SmoFormatOptions.Default);

        Assert.Contains("GO", script, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT 1    ", script, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatCreateTableScript_reflows_inline_defaults_constraints_and_primary_keys()
    {
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

        var formatted = _formatter.FormatCreateTableScript(script, statement, foreignKeyTrustLookup: null, SmoFormatOptions.Default);

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

        var formatted = _formatter.FormatCreateTableScript(script, statement, lookup, SmoFormatOptions.Default);

        Assert.Contains("    CONSTRAINT [FK_Order_Customer_CustomerId]\n        FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer]([Id])", formatted, StringComparison.Ordinal);
        Assert.Contains("-- Source constraint was not trusted (WITH NOCHECK)", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatCreateTableScript_skips_reflow_when_normalization_is_disabled()
    {
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

        var formatted = _formatter.FormatCreateTableScript(script, statement, foreignKeyTrustLookup: null, format);

        Assert.Equal(script, formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateCreateTableScript_serializes_nvarchar_max_columns_with_max_literal()
    {
        var column = CreateSmoColumnDefinition("Description", DataType.NVarCharMax);

        var script = GenerateCreateTableScript(column);
        var normalized = script.Replace(" ", string.Empty, StringComparison.Ordinal);

        Assert.Contains("NVARCHAR(MAX)", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateCreateTableScript_serializes_varbinary_max_columns_with_max_literal()
    {
        var column = CreateSmoColumnDefinition("Payload", DataType.VarBinaryMax);

        var script = GenerateCreateTableScript(column);
        var normalized = script.Replace(" ", string.Empty, StringComparison.Ordinal);

        Assert.Contains("VARBINARY(MAX)", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateCreateTableScript_emits_null_keyword_for_nullable_columns()
    {
        var column = CreateSmoColumnDefinition("Optional", DataType.NVarChar(50));

        var script = GenerateCreateTableScript(column);

        Assert.Contains("[Optional] NVARCHAR (50) NULL", script, StringComparison.Ordinal);
    }

    private CreateTableStatement CreateMinimalCreateTableStatement(params string[] columnNames)
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

    private string GenerateCreateTableScript(params SmoColumnDefinition[] columns)
    {
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

        var statement = _createTableStatementBuilder.BuildCreateTableStatement(table, table.LogicalName, options);

        var generator = new Sql150ScriptGenerator(new SqlScriptGeneratorOptions
        {
            KeywordCasing = KeywordCasing.Uppercase,
            IncludeSemicolons = true,
            SqlVersion = SqlVersion.Sql150,
        });

        generator.GenerateScript(statement, out var script);
        var trimmed = script.Trim();

        return _formatter.FormatCreateTableScript(trimmed, statement, foreignKeyTrustLookup: null, options.Format);
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
