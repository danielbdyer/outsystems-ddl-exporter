using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
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
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
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
        var manifestEntries = new List<TableManifestEntry>(model.Tables.Length);
        var moduleDirectories = new Dictionary<string, ModuleDirectoryPaths>(StringComparer.Ordinal);

        foreach (var table in model.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var modulePaths = EnsureModuleDirectories(moduleDirectories, outputDirectory, table.Module);
            var tablesRoot = modulePaths.TablesRoot;

            var effectiveTableName = options.NamingOverrides.GetEffectiveTableName(
                table.Schema,
                table.Name,
                table.LogicalName,
                table.OriginalModule);

            var tableStatement = BuildCreateTableStatement(table, effectiveTableName, options);
            var inlineForeignKeys = AddForeignKeys(tableStatement, table, effectiveTableName, options);
            var tableScript = Script(tableStatement);

            var scriptBuilder = new StringBuilder(tableScript.Length + 512);
            AppendStatement(scriptBuilder, tableScript);

            var indexNames = new List<string>();
            if (!options.EmitBareTableOnly)
            {
                foreach (var index in table.Indexes)
                {
                    if (index.IsPrimaryKey)
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    var indexName = ResolveConstraintName(index.Name, table.Name, table.LogicalName, effectiveTableName);
                    var indexStatement = BuildCreateIndexStatement(table, index, effectiveTableName, indexName);
                    var script = Script(indexStatement);
                    AppendStatement(scriptBuilder, script);
                    indexNames.Add(indexName);
                }
            }

            var extendedPropertiesEmitted = false;
            if (!options.EmitBareTableOnly)
            {
                extendedPropertiesEmitted = AppendExtendedProperties(scriptBuilder, table, effectiveTableName);
            }

            var tableFilePath = Path.Combine(tablesRoot, $"{table.Schema}.{effectiveTableName}.sql");
            await WriteAsync(tableFilePath, scriptBuilder.ToString(), cancellationToken);

            manifestEntries.Add(new TableManifestEntry(
                table.Module,
                table.Schema,
                effectiveTableName,
                Relativize(tableFilePath, outputDirectory),
                indexNames,
                inlineForeignKeys,
                extendedPropertiesEmitted));
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
            new SsdtManifestOptions(options.IncludePlatformAutoIndexes, options.EmitBareTableOnly),
            summary);

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, manifestJson, Utf8NoBom, cancellationToken);

        return manifest;
    }

    private static ModuleDirectoryPaths EnsureModuleDirectories(
        IDictionary<string, ModuleDirectoryPaths> cache,
        string outputDirectory,
        string module)
    {
        if (!cache.TryGetValue(module, out var paths))
        {
            var moduleRoot = Path.Combine(outputDirectory, "Modules", module);
            var tablesRoot = Path.Combine(moduleRoot, "Tables");
            Directory.CreateDirectory(tablesRoot);

            paths = new ModuleDirectoryPaths(tablesRoot);
            cache[module] = paths;
        }

        return paths;
    }

    private static void AppendStatement(StringBuilder builder, string statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("GO");
            builder.AppendLine();
        }

        builder.AppendLine(statement.TrimEnd());
    }

    private bool AppendExtendedProperties(StringBuilder builder, SmoTableDefinition table, string effectiveTableName)
    {
        var emitted = false;

        if (!string.IsNullOrWhiteSpace(table.Description))
        {
            var script = BuildTableExtendedPropertyScript(table.Schema, effectiveTableName, table.Description!);
            AppendStatement(builder, script);
            emitted = true;
        }

        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Description))
            {
                continue;
            }

            var script = BuildColumnExtendedPropertyScript(table.Schema, effectiveTableName, column.Name, column.Description!);
            AppendStatement(builder, script);
            emitted = true;
        }

        return emitted;
    }

    private static string BuildTableExtendedPropertyScript(string schema, string table, string description)
    {
        var schemaIdentifier = QuoteIdentifier(schema);
        var tableIdentifier = QuoteIdentifier(table);
        var escapedDescription = EscapeSqlLiteral(description);
        var schemaLiteral = EscapeSqlLiteral(schema);
        var tableLiteral = EscapeSqlLiteral(table);

        return $"""
IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'{schemaIdentifier}.{tableIdentifier}')
      AND minor_id = 0
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'{escapedDescription}',
        @level0type=N'SCHEMA',@level0name=N'{schemaLiteral}',
        @level1type=N'TABLE',@level1name=N'{tableLiteral}';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{escapedDescription}',
        @level0type=N'SCHEMA',@level0name=N'{schemaLiteral}',
        @level1type=N'TABLE',@level1name=N'{tableLiteral}';
""";
    }

    private static string BuildColumnExtendedPropertyScript(string schema, string table, string column, string description)
    {
        var schemaIdentifier = QuoteIdentifier(schema);
        var tableIdentifier = QuoteIdentifier(table);
        var columnLiteral = EscapeSqlLiteral(column);
        var descriptionLiteral = EscapeSqlLiteral(description);
        var schemaLiteral = EscapeSqlLiteral(schema);
        var tableLiteral = EscapeSqlLiteral(table);

        return $"""
IF EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND name = N'MS_Description'
      AND major_id = OBJECT_ID(N'{schemaIdentifier}.{tableIdentifier}')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'{schemaIdentifier}.{tableIdentifier}'), N'{columnLiteral}', 'ColumnId')
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'{descriptionLiteral}',
        @level0type=N'SCHEMA',@level0name=N'{schemaLiteral}',
        @level1type=N'TABLE',@level1name=N'{tableLiteral}',
        @level2type=N'COLUMN',@level2name=N'{columnLiteral}';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{descriptionLiteral}',
        @level0type=N'SCHEMA',@level0name=N'{schemaLiteral}',
        @level1type=N'TABLE',@level1name=N'{tableLiteral}',
        @level2type=N'COLUMN',@level2name=N'{columnLiteral}';
""";
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return "[]";
        }

        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private CreateTableStatement BuildCreateTableStatement(
        SmoTableDefinition table,
        string effectiveTableName,
        SmoBuildOptions options)
    {
        var definition = new TableDefinition();
        var columnLookup = new Dictionary<string, ColumnDefinition>(table.Columns.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var column in table.Columns)
        {
            var columnDefinition = BuildColumnDefinition(column, options);
            definition.ColumnDefinitions.Add(columnDefinition);
            if (!string.IsNullOrWhiteSpace(column.Name))
            {
                columnLookup[column.Name] = columnDefinition;
            }
        }

        var primaryKey = table.Indexes.FirstOrDefault(i => i.IsPrimaryKey);
        if (primaryKey is not null)
        {
            var sortedColumns = primaryKey.Columns.OrderBy(c => c.Ordinal).ToImmutableArray();
            var constraintName = ResolveConstraintName(primaryKey.Name, table.Name, table.LogicalName, effectiveTableName);

            if (sortedColumns.Length == 1 &&
                columnLookup.TryGetValue(sortedColumns[0].Name, out var primaryKeyColumn))
            {
                var inlineConstraint = new UniqueConstraintDefinition
                {
                    IsPrimaryKey = true,
                    Clustered = true,
                    ConstraintIdentifier = new Identifier { Value = constraintName },
                };

                primaryKeyColumn.Constraints.Add(inlineConstraint);
            }
            else
            {
                var tableConstraint = new UniqueConstraintDefinition
                {
                    IsPrimaryKey = true,
                    Clustered = true,
                    ConstraintIdentifier = new Identifier { Value = constraintName },
                };

                foreach (var column in sortedColumns)
                {
                    tableConstraint.Columns.Add(new ColumnWithSortOrder
                    {
                        Column = BuildColumnReference(column.Name),
                        SortOrder = SortOrder.NotSpecified,
                    });
                }

                definition.TableConstraints.Add(tableConstraint);
            }
        }

        return new CreateTableStatement
        {
            SchemaObjectName = BuildSchemaObjectName(table.Schema, effectiveTableName),
            Definition = definition,
        };
    }

    private ColumnDefinition BuildColumnDefinition(SmoColumnDefinition column, SmoBuildOptions options)
    {
        var definition = new ColumnDefinition
        {
            ColumnIdentifier = new Identifier { Value = column.Name },
            DataType = column.IsComputed ? null : TranslateDataType(column.DataType),
        };

        if (!column.Nullable)
        {
            definition.Constraints.Add(new NullableConstraintDefinition { Nullable = false });
        }

        if (column.IsIdentity && !column.IsComputed)
        {
            definition.IdentityOptions = new IdentityOptions
            {
                IdentitySeed = new IntegerLiteral { Value = column.IdentitySeed.ToString(CultureInfo.InvariantCulture) },
                IdentityIncrement = new IntegerLiteral { Value = column.IdentityIncrement.ToString(CultureInfo.InvariantCulture) },
            };
        }

        if (!string.IsNullOrWhiteSpace(column.Collation))
        {
            definition.Collation = new Identifier { Value = column.Collation };
        }

        if (!options.EmitBareTableOnly)
        {
            var defaultExpression = ParseExpression(column.DefaultExpression);
            if (defaultExpression is not null)
            {
                definition.Constraints.Add(new DefaultConstraintDefinition { Expression = defaultExpression });
            }
        }

        if (column.IsComputed)
        {
            var computedExpression = ParseExpression(column.ComputedExpression);
            if (computedExpression is not null)
            {
                definition.ComputedColumnExpression = computedExpression;
            }
        }

        return definition;
    }

    private static ScalarExpression? ParseExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var parser = new TSql150Parser(initialQuotedIdentifiers: false);
        using var reader = new StringReader(expression);
        var result = parser.ParseExpression(reader, out var errors);
        if (result is null || (errors is not null && errors.Count > 0))
        {
            return null;
        }

        return result;
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
            if (column.IsIncluded)
            {
                statement.IncludeColumns.Add(BuildColumnReference(column.Name));
                continue;
            }

            statement.Columns.Add(new ColumnWithSortOrder
            {
                Column = BuildColumnReference(column.Name),
                SortOrder = column.IsDescending ? SortOrder.Descending : SortOrder.Ascending,
            });
        }

        return statement;
    }

    private ImmutableArray<string> AddForeignKeys(
        CreateTableStatement statement,
        SmoTableDefinition table,
        string effectiveTableName,
        SmoBuildOptions options)
    {
        if (options.EmitBareTableOnly || table.ForeignKeys.Length == 0 || statement.Definition is null)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(table.ForeignKeys.Length);
        foreach (var foreignKey in table.ForeignKeys)
        {
            var referencedTableName = options.NamingOverrides.GetEffectiveTableName(
                foreignKey.ReferencedSchema,
                foreignKey.ReferencedTable,
                foreignKey.ReferencedLogicalTable,
                foreignKey.ReferencedModule);

            var foreignKeyName = ResolveConstraintName(foreignKey.Name, table.Name, table.LogicalName, effectiveTableName);
            builder.Add(foreignKeyName);

            var constraint = new ForeignKeyConstraintDefinition
            {
                ConstraintIdentifier = new Identifier { Value = foreignKeyName },
                ReferenceTableName = BuildSchemaObjectName(foreignKey.ReferencedSchema, referencedTableName),
                DeleteAction = MapDeleteAction(foreignKey.DeleteAction),
                UpdateAction = DeleteUpdateAction.NoAction,
            };

            constraint.ReferencedTableColumns.Add(new Identifier { Value = foreignKey.ReferencedColumn });

            var inlineColumn = FindColumnDefinition(statement.Definition, foreignKey.Column);
            if (inlineColumn is not null)
            {
                inlineColumn.Constraints.Add(constraint);
            }
            else
            {
                constraint.Columns.Add(new Identifier { Value = foreignKey.Column });
                statement.Definition.TableConstraints.Add(constraint);
            }
        }

        return builder.ToImmutable();
    }

    private static ColumnDefinition? FindColumnDefinition(TableDefinition definition, string columnName)
    {
        foreach (var column in definition.ColumnDefinitions)
        {
            if (column?.ColumnIdentifier?.Value is null)
            {
                continue;
            }

            if (string.Equals(column.ColumnIdentifier.Value, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        return null;
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
        var trimmed = script.Trim();

        return statement switch
        {
            CreateTableStatement createTable => FormatCreateTableScript(trimmed, createTable),
            _ => trimmed,
        };
    }

    private static string FormatCreateTableScript(string script, CreateTableStatement statement)
    {
        if (statement?.Definition?.ColumnDefinitions is null ||
            statement.Definition.ColumnDefinitions.Count == 0)
        {
            return script;
        }

        var lines = script.Split(Environment.NewLine);
        var builder = new StringBuilder(script.Length + 32);
        var insideColumnBlock = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            var line = lines[i];
            var trimmedLine = line.TrimStart();

            if (!insideColumnBlock)
            {
                builder.Append(line);
                if (trimmedLine.EndsWith("(", StringComparison.Ordinal))
                {
                    insideColumnBlock = true;
                }

                continue;
            }

            if (trimmedLine.StartsWith(")", StringComparison.Ordinal))
            {
                insideColumnBlock = false;
                builder.Append(line);
                continue;
            }

            if (trimmedLine.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase) &&
                !trimmedLine.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(FormatInlineDefault(line));
                continue;
            }

            if (trimmedLine.Contains("CONSTRAINT", StringComparison.OrdinalIgnoreCase) &&
                !trimmedLine.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(FormatInlineConstraint(line));
                continue;
            }

            if (!trimmedLine.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(line);
                continue;
            }
        }

        var withDefaults = builder.ToString();
        var withForeignKeys = FormatForeignKeyConstraints(withDefaults);
        return FormatPrimaryKeyConstraints(withForeignKeys);
    }

    private static string FormatInlineDefault(string line)
    {
        var trimmedLine = line.TrimStart();
        var indentLength = line.Length - trimmedLine.Length;
        var indent = line[..indentLength];
        var extraIndent = indent + new string(' ', 4);

        var working = trimmedLine;
        var trailingComma = string.Empty;

        var trimmedWorking = working.TrimEnd();
        if (trimmedWorking.EndsWith(",", StringComparison.Ordinal))
        {
            trailingComma = ",";
            trimmedWorking = trimmedWorking[..^1];
        }

        working = trimmedWorking;
        var defaultIndex = working.IndexOf(" DEFAULT", StringComparison.OrdinalIgnoreCase);
        if (defaultIndex < 0)
        {
            return line;
        }

        var beforeDefault = working[..defaultIndex].TrimEnd();
        var defaultSegment = working[defaultIndex..].TrimStart();

        string? constraintSegment = null;
        var constraintIndex = beforeDefault.IndexOf(" CONSTRAINT ", StringComparison.OrdinalIgnoreCase);
        if (constraintIndex >= 0)
        {
            constraintSegment = beforeDefault[constraintIndex..].TrimStart();
            beforeDefault = beforeDefault[..constraintIndex].TrimEnd();
        }

        var builder = new StringBuilder(line.Length + 16);
        builder.Append(indent);
        builder.Append(beforeDefault);
        builder.Append(Environment.NewLine);
        builder.Append(extraIndent);
        if (!string.IsNullOrEmpty(constraintSegment))
        {
            builder.Append(constraintSegment);
            builder.Append(' ');
        }

        builder.Append(defaultSegment);
        builder.Append(trailingComma);

        return builder.ToString();
    }

    private static string FormatInlineConstraint(string line)
    {
        var trimmedLine = line.TrimStart();
        var indentLength = line.Length - trimmedLine.Length;
        var indent = line[..indentLength];

        var working = trimmedLine.TrimEnd();
        var trailingComma = string.Empty;

        if (working.EndsWith(",", StringComparison.Ordinal))
        {
            trailingComma = ",";
            working = working[..^1];
        }

        var constraintIndex = working.IndexOf(" CONSTRAINT ", StringComparison.OrdinalIgnoreCase);
        if (constraintIndex < 0)
        {
            return line;
        }

        var columnSegment = working[..constraintIndex].TrimEnd();
        var constraintSegment = working[constraintIndex..].Trim();

        var builder = new StringBuilder(line.Length + 16);
        builder.Append(indent);
        builder.Append(columnSegment);
        builder.AppendLine();
        builder.Append(indent);
        builder.Append(new string(' ', 4));
        builder.Append(constraintSegment);
        builder.Append(trailingComma);

        return builder.ToString();
    }

    private static string FormatForeignKeyConstraints(string script)
    {
        var lines = script.Split(Environment.NewLine);
        var builder = new StringBuilder(script.Length + 64);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("REFERENCES", StringComparison.OrdinalIgnoreCase))
            {
                var indentLength = line.Length - trimmed.Length;
                var indent = line[..indentLength];
                var trailingComma = string.Empty;

                if (trimmed.EndsWith(",", StringComparison.Ordinal))
                {
                    trailingComma = ",";
                    trimmed = trimmed[..^1].TrimEnd();
                }

                var foreignKeyIndex = trimmed.IndexOf("FOREIGN KEY", StringComparison.OrdinalIgnoreCase);
                var referencesIndex = trimmed.IndexOf("REFERENCES", StringComparison.OrdinalIgnoreCase);

                if (foreignKeyIndex <= 0 || referencesIndex <= foreignKeyIndex)
                {
                    builder.AppendLine(line);
                    continue;
                }

                var constraintSegment = trimmed[..foreignKeyIndex].TrimEnd();
                var ownerSegment = trimmed[foreignKeyIndex..referencesIndex].Trim();

                var onDeleteIndex = trimmed.IndexOf(" ON DELETE", StringComparison.OrdinalIgnoreCase);
                var onUpdateIndex = trimmed.IndexOf(" ON UPDATE", StringComparison.OrdinalIgnoreCase);

                var referencesEnd = trimmed.Length;
                if (onDeleteIndex >= 0 && onDeleteIndex < referencesEnd)
                {
                    referencesEnd = onDeleteIndex;
                }
                if (onUpdateIndex >= 0 && onUpdateIndex < referencesEnd)
                {
                    referencesEnd = onUpdateIndex;
                }

                var referencesSegment = trimmed[referencesIndex..referencesEnd].Trim();
                string? onDeleteSegment = null;
                string? onUpdateSegment = null;

                if (onDeleteIndex >= 0)
                {
                    var end = onUpdateIndex > onDeleteIndex ? onUpdateIndex : trimmed.Length;
                    onDeleteSegment = trimmed[onDeleteIndex..end].Trim();
                }

                if (onUpdateIndex >= 0)
                {
                    onUpdateSegment = trimmed[onUpdateIndex..].Trim();
                }

                builder.Append(indent);
                builder.AppendLine(constraintSegment);
                var ownerIndent = indent + new string(' ', 4);
                var clauseIndent = ownerIndent + new string(' ', 4);
                var hasOnDelete = !string.IsNullOrWhiteSpace(onDeleteSegment);
                var hasOnUpdate = !string.IsNullOrWhiteSpace(onUpdateSegment);
                var hasOnClauses = hasOnDelete || hasOnUpdate;

                builder.Append(ownerIndent);
                builder.Append(ownerSegment);
                builder.Append(' ');
                builder.Append(referencesSegment);
                if (hasOnClauses)
                {
                    builder.AppendLine();
                }
                else
                {
                    builder.AppendLine(trailingComma);
                }

                if (hasOnClauses)
                {
                    var segments = new List<string>(capacity: 2);
                    if (hasOnDelete)
                    {
                        segments.Add(onDeleteSegment!);
                    }

                    if (hasOnUpdate)
                    {
                        segments.Add(onUpdateSegment!);
                    }

                    for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                    {
                        var segment = segments[segmentIndex];
                        var isLastSegment = segmentIndex == segments.Count - 1;

                        builder.Append(clauseIndent);
                        if (isLastSegment && !string.IsNullOrEmpty(trailingComma))
                        {
                            builder.Append(segment);
                            builder.AppendLine(trailingComma);
                        }
                        else
                        {
                            builder.AppendLine(segment);
                        }
                    }
                }

                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatPrimaryKeyConstraints(string script)
    {
        var lines = script.Split(Environment.NewLine);
        var builder = new StringBuilder(script.Length + 32);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
            {
                var indentLength = line.Length - trimmed.Length;
                var indent = line[..indentLength];
                var trailingComma = string.Empty;
                var working = trimmed;

                if (working.EndsWith(",", StringComparison.Ordinal))
                {
                    trailingComma = ",";
                    working = working[..^1].TrimEnd();
                }

                var primaryIndex = working.IndexOf("PRIMARY KEY", StringComparison.OrdinalIgnoreCase);
                if (primaryIndex > 0)
                {
                    var constraintSegment = working[..primaryIndex].TrimEnd();
                    var primarySegment = working[primaryIndex..].Trim();

                    builder.Append(indent);
                    builder.AppendLine(constraintSegment);
                    builder.Append(indent);
                    builder.Append(new string(' ', 4));
                    builder.Append(primarySegment);
                    builder.AppendLine(trailingComma);
                    continue;
                }
            }

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private static async Task WriteAsync(string path, string contents, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(path, contents + Environment.NewLine, Utf8NoBom, cancellationToken);
    }

    private static string Relativize(string path, string root)
    {
        return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private sealed record ModuleDirectoryPaths(string TablesRoot);
}
