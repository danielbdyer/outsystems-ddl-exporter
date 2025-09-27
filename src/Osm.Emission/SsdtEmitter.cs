using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SortOrder = Microsoft.SqlServer.TransactSql.ScriptDom.SortOrder;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Emission;

public sealed class SsdtEmitter
{
    private readonly Sql150ScriptGenerator _scriptGenerator;

    public SsdtEmitter()
    {
        _scriptGenerator = new Sql150ScriptGenerator(new SqlScriptGeneratorOptions
        {
            KeywordCasing = KeywordCasing.Uppercase,
            IncludeSemicolons = true,
            SqlVersion = SqlVersion.Sql150,
        });
    }

    public async Task<SsdtManifest> EmitAsync(
        SmoModel model,
        string outputDirectory,
        SmoBuildOptions options,
        PolicyDecisionReport? decisionReport = null,
        CancellationToken cancellationToken = default)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be provided.", nameof(outputDirectory));
        }

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(outputDirectory);
        var manifestEntries = new List<TableManifestEntry>();

        foreach (var table in model.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var moduleRoot = Path.Combine(outputDirectory, "Modules", table.Module);
            var tablesRoot = Path.Combine(moduleRoot, "Tables");
            var indexesRoot = Path.Combine(moduleRoot, "Indexes");
            var foreignKeysRoot = Path.Combine(moduleRoot, "ForeignKeys");

            Directory.CreateDirectory(tablesRoot);
            Directory.CreateDirectory(indexesRoot);
            Directory.CreateDirectory(foreignKeysRoot);

            var effectiveTableName = options.NamingOverrides.GetEffectiveTableName(table.Schema, table.Name, table.LogicalName);
            var tableStatement = BuildCreateTableStatement(table, effectiveTableName);
            var tableScript = Script(tableStatement);
            var tableFilePath = Path.Combine(tablesRoot, $"{table.Schema}.{effectiveTableName}.sql");
            await WriteAsync(tableFilePath, tableScript, cancellationToken);

            var indexFiles = new List<string>();
            var indexStatements = new List<string>();
            foreach (var index in table.Indexes.Where(i => !i.IsPrimaryKey))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var indexName = ResolveConstraintName(index.Name, table.Name, table.LogicalName, effectiveTableName);
                var indexStatement = BuildCreateIndexStatement(table, index, effectiveTableName, indexName);
                var script = Script(indexStatement);
                var indexFilePath = Path.Combine(indexesRoot, $"{table.Schema}.{effectiveTableName}.{indexName}.sql");
                await WriteAsync(indexFilePath, script, cancellationToken);
                indexFiles.Add(Relativize(indexFilePath, outputDirectory));
                indexStatements.Add(script);
            }

            var foreignKeyFiles = new List<string>();
            var foreignKeyStatements = new List<string>();
            foreach (var foreignKey in table.ForeignKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var referencedTableName = options.NamingOverrides.GetEffectiveTableName(
                    foreignKey.ReferencedSchema,
                    foreignKey.ReferencedTable,
                    foreignKey.ReferencedLogicalTable);
                var foreignKeyName = ResolveConstraintName(foreignKey.Name, table.Name, table.LogicalName, effectiveTableName);
                var fkStatement = BuildForeignKeyStatement(
                    table,
                    foreignKey,
                    effectiveTableName,
                    foreignKeyName,
                    referencedTableName);
                var script = Script(fkStatement);
                var fkFilePath = Path.Combine(foreignKeysRoot, $"{table.Schema}.{effectiveTableName}.{foreignKeyName}.sql");
                await WriteAsync(fkFilePath, script, cancellationToken);
                foreignKeyFiles.Add(Relativize(fkFilePath, outputDirectory));
                foreignKeyStatements.Add(script);
            }

            string? concatenatedFile = null;
            if (options.EmitConcatenatedConstraints)
            {
                var combinedStatements = new List<string> { tableScript };
                combinedStatements.AddRange(indexStatements);
                combinedStatements.AddRange(foreignKeyStatements);
                var combined = string.Join(
                    $"{Environment.NewLine}GO{Environment.NewLine}",
                    combinedStatements.Where(s => !string.IsNullOrWhiteSpace(s)));
                var combinedPath = Path.Combine(tablesRoot, $"{table.Schema}.{effectiveTableName}.full.sql");
                await WriteAsync(combinedPath, combined, cancellationToken);
                concatenatedFile = Relativize(combinedPath, outputDirectory);
            }

            manifestEntries.Add(new TableManifestEntry(
                table.Module,
                table.Schema,
                effectiveTableName,
                Relativize(tableFilePath, outputDirectory),
                indexFiles,
                foreignKeyFiles,
                concatenatedFile));
        }

        SsdtPolicySummary? summary = null;
        if (decisionReport is not null)
        {
            summary = new SsdtPolicySummary(
                decisionReport.ColumnCount,
                decisionReport.TightenedColumnCount,
                decisionReport.RemediationColumnCount,
                decisionReport.UniqueIndexCount,
                decisionReport.UniqueIndexesEnforcedCount,
                decisionReport.UniqueIndexesRequireRemediationCount,
                decisionReport.ForeignKeyCount,
                decisionReport.ForeignKeysCreatedCount,
                decisionReport.ColumnRationaleCounts,
                decisionReport.UniqueIndexRationaleCounts,
                decisionReport.ForeignKeyRationaleCounts);
        }

        var manifest = new SsdtManifest(
            manifestEntries,
            new SsdtManifestOptions(options.IncludePlatformAutoIndexes, options.EmitConcatenatedConstraints),
            summary);

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, manifestJson, Encoding.UTF8, cancellationToken);

        return manifest;
    }

    private CreateTableStatement BuildCreateTableStatement(
        SmoTableDefinition table,
        string effectiveTableName)
    {
        var definition = new TableDefinition();
        foreach (var column in table.Columns)
        {
            definition.ColumnDefinitions.Add(BuildColumnDefinition(column));
        }

        var primaryKey = table.Indexes.FirstOrDefault(i => i.IsPrimaryKey);
        if (primaryKey is not null)
        {
            var constraint = new UniqueConstraintDefinition
            {
                IsPrimaryKey = true,
                Clustered = true,
                ConstraintIdentifier = new Identifier
                {
                    Value = ResolveConstraintName(primaryKey.Name, table.Name, table.LogicalName, effectiveTableName),
                },
            };

            foreach (var column in primaryKey.Columns.OrderBy(c => c.Ordinal))
            {
                constraint.Columns.Add(new ColumnWithSortOrder
                {
                    Column = BuildColumnReference(column.Name),
                    SortOrder = SortOrder.NotSpecified,
                });
            }

            definition.TableConstraints.Add(constraint);
        }

        return new CreateTableStatement
        {
            SchemaObjectName = BuildSchemaObjectName(table.Schema, effectiveTableName),
            Definition = definition,
        };
    }

    private ColumnDefinition BuildColumnDefinition(SmoColumnDefinition column)
    {
        var definition = new ColumnDefinition
        {
            ColumnIdentifier = new Identifier { Value = column.Name },
            DataType = TranslateDataType(column.DataType),
        };

        if (!column.Nullable)
        {
            definition.Constraints.Add(new NullableConstraintDefinition { Nullable = false });
        }

        if (column.IsIdentity)
        {
            definition.IdentityOptions = new IdentityOptions
            {
                IdentitySeed = new IntegerLiteral { Value = column.IdentitySeed.ToString(CultureInfo.InvariantCulture) },
                IdentityIncrement = new IntegerLiteral { Value = column.IdentityIncrement.ToString(CultureInfo.InvariantCulture) },
            };
        }

        return definition;
    }

    private CreateIndexStatement BuildCreateIndexStatement(
        SmoTableDefinition table,
        SmoIndexDefinition index,
        string effectiveTableName,
        string indexName)
    {
        var statement = new CreateIndexStatement
        {
            Name = new Identifier { Value = indexName },
            OnName = BuildSchemaObjectName(table.Schema, effectiveTableName),
            Unique = index.IsUnique,
        };

        foreach (var column in index.Columns.OrderBy(c => c.Ordinal))
        {
            statement.Columns.Add(new ColumnWithSortOrder
            {
                Column = BuildColumnReference(column.Name),
                SortOrder = SortOrder.NotSpecified,
            });
        }

        return statement;
    }

    private AlterTableAddTableElementStatement BuildForeignKeyStatement(
        SmoTableDefinition table,
        SmoForeignKeyDefinition foreignKey,
        string effectiveTableName,
        string foreignKeyName,
        string referencedTableName)
    {
        var constraint = new ForeignKeyConstraintDefinition
        {
            ConstraintIdentifier = new Identifier { Value = foreignKeyName },
            ReferenceTableName = BuildSchemaObjectName(foreignKey.ReferencedSchema, referencedTableName),
            DeleteAction = MapDeleteAction(foreignKey.DeleteAction),
            UpdateAction = DeleteUpdateAction.NoAction,
        };

        constraint.Columns.Add(new Identifier { Value = foreignKey.Column });
        constraint.ReferencedTableColumns.Add(new Identifier { Value = foreignKey.ReferencedColumn });

        var definition = new TableDefinition();
        definition.TableConstraints.Add(constraint);

        return new AlterTableAddTableElementStatement
        {
            SchemaObjectName = BuildSchemaObjectName(table.Schema, effectiveTableName),
            Definition = definition,
        };
    }

    private static string ResolveConstraintName(
        string originalName,
        string originalTableName,
        string logicalTableName,
        string effectiveTableName)
    {
        if (string.IsNullOrWhiteSpace(originalName) ||
            string.Equals(originalTableName, effectiveTableName, StringComparison.OrdinalIgnoreCase))
        {
            return originalName;
        }

        var renamed = ReplaceIgnoreCase(originalName, originalTableName, effectiveTableName);
        renamed = ReplaceIgnoreCase(renamed, logicalTableName, effectiveTableName);
        return renamed;
    }

    private static string ReplaceIgnoreCase(string source, string search, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search))
        {
            return source;
        }

        var result = new StringBuilder();
        var currentIndex = 0;
        var comparison = StringComparison.OrdinalIgnoreCase;

        while (true)
        {
            var matchIndex = source.IndexOf(search, currentIndex, comparison);
            if (matchIndex < 0)
            {
                result.Append(source, currentIndex, source.Length - currentIndex);
                break;
            }

            result.Append(source, currentIndex, matchIndex - currentIndex);
            result.Append(replacement);
            currentIndex = matchIndex + search.Length;
        }

        return result.ToString();
    }

    private SchemaObjectName BuildSchemaObjectName(string schema, string name)
    {
        return new SchemaObjectName
        {
            Identifiers =
            {
                new Identifier { Value = schema },
                new Identifier { Value = name },
            }
        };
    }

    private ColumnReferenceExpression BuildColumnReference(string columnName)
    {
        return new ColumnReferenceExpression
        {
            MultiPartIdentifier = new MultiPartIdentifier
            {
                Identifiers = { new Identifier { Value = columnName } }
            }
        };
    }

    private DataTypeReference TranslateDataType(DataType dataType)
    {
        var sqlType = new SqlDataTypeReference
        {
            SqlDataTypeOption = MapSqlDataType(dataType.SqlDataType),
        };

        if (sqlType.SqlDataTypeOption is SqlDataTypeOption.VarChar or SqlDataTypeOption.NVarChar or SqlDataTypeOption.Char or SqlDataTypeOption.NChar)
        {
            if (dataType.MaximumLength < 0)
            {
                sqlType.Parameters.Add(new MaxLiteral());
            }
            else if (dataType.MaximumLength > 0)
            {
                sqlType.Parameters.Add(new IntegerLiteral { Value = dataType.MaximumLength.ToString(CultureInfo.InvariantCulture) });
            }
        }

        if (sqlType.SqlDataTypeOption is SqlDataTypeOption.Decimal or SqlDataTypeOption.Numeric)
        {
            sqlType.Parameters.Add(new IntegerLiteral { Value = dataType.NumericPrecision.ToString(CultureInfo.InvariantCulture) });
            sqlType.Parameters.Add(new IntegerLiteral { Value = dataType.NumericScale.ToString(CultureInfo.InvariantCulture) });
        }

        return sqlType;
    }

    private static SqlDataTypeOption MapSqlDataType(SqlDataType sqlDataType) => sqlDataType switch
    {
        SqlDataType.BigInt => SqlDataTypeOption.BigInt,
        SqlDataType.Bit => SqlDataTypeOption.Bit,
        SqlDataType.Char => SqlDataTypeOption.Char,
        SqlDataType.Date => SqlDataTypeOption.Date,
        SqlDataType.DateTime => SqlDataTypeOption.DateTime,
        SqlDataType.Decimal => SqlDataTypeOption.Decimal,
        SqlDataType.Int => SqlDataTypeOption.Int,
        SqlDataType.NChar => SqlDataTypeOption.NChar,
        SqlDataType.NVarChar => SqlDataTypeOption.NVarChar,
        SqlDataType.NVarCharMax => SqlDataTypeOption.NVarChar,
        SqlDataType.VarChar => SqlDataTypeOption.VarChar,
        SqlDataType.VarCharMax => SqlDataTypeOption.VarChar,
        _ => SqlDataTypeOption.NVarChar,
    };

    private static DeleteUpdateAction MapDeleteAction(ForeignKeyAction action) => action switch
    {
        ForeignKeyAction.Cascade => DeleteUpdateAction.Cascade,
        ForeignKeyAction.SetNull => DeleteUpdateAction.SetNull,
        _ => DeleteUpdateAction.NoAction,
    };

    private string Script(TSqlStatement statement)
    {
        _scriptGenerator.GenerateScript(statement, out var script);
        return script.Trim();
    }

    private static async Task WriteAsync(string path, string contents, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(path, contents + Environment.NewLine, Encoding.UTF8, cancellationToken);
    }

    private static string Relativize(string path, string root)
    {
        return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    }
}
