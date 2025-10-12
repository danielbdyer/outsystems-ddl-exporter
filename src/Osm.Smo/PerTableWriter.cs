using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SortOrder = Microsoft.SqlServer.TransactSql.ScriptDom.SortOrder;

namespace Osm.Smo;

public sealed class PerTableWriter
{
    private readonly SmoContext _context;

    public PerTableWriter()
        : this(new SmoContext())
    {
    }

    public PerTableWriter(SmoContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public PerTableWriteResult Generate(
        SmoTableDefinition table,
        SmoBuildOptions options,
        IReadOnlyList<PerTableHeaderItem>? tableHeaderItems = null,
        CancellationToken cancellationToken = default)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var effectiveTableName = options.NamingOverrides.GetEffectiveTableName(
            table.Schema,
            table.Name,
            table.LogicalName,
            table.OriginalModule);

        var statement = BuildCreateTableStatement(table, effectiveTableName, options);
        var inlineForeignKeys = AddForeignKeys(statement, table, effectiveTableName, options, out var foreignKeyTrustLookup);
        var tableScript = Script(statement, foreignKeyTrustLookup, options.Format);

        var statements = new List<string> { tableScript };
        var indexNames = ImmutableArray.CreateBuilder<string>();

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
                var indexStatement = BuildCreateIndexStatement(table, index, effectiveTableName, indexName, options.Format);
                var indexScript = Script(indexStatement, format: options.Format);
                statements.Add(indexScript);
                indexNames.Add(indexName);

                if (index.Metadata.IsDisabled)
                {
                    var disableStatement = BuildDisableIndexStatement(table, effectiveTableName, indexName, options.Format);
                    var disableScript = Script(disableStatement, format: options.Format);
                    statements.Add(disableScript);
                }
            }
        }

        var includesExtendedProperties = false;
        if (!options.EmitBareTableOnly)
        {
            var extendedPropertyScripts = BuildExtendedPropertyScripts(table, effectiveTableName, options.Format);
            if (!extendedPropertyScripts.IsDefaultOrEmpty)
            {
                statements.AddRange(extendedPropertyScripts);
                includesExtendedProperties = true;
            }

            var triggerScripts = BuildTriggerScripts(table, effectiveTableName, options.Format);
            if (!triggerScripts.IsDefaultOrEmpty)
            {
                statements.AddRange(triggerScripts);
            }
        }

        var orderedIndexNames = indexNames.ToImmutable();
        if (!orderedIndexNames.IsDefaultOrEmpty)
        {
            orderedIndexNames = orderedIndexNames.Sort(StringComparer.OrdinalIgnoreCase);
        }

        var foreignKeyNames = inlineForeignKeys;
        if (!foreignKeyNames.IsDefaultOrEmpty)
        {
            foreignKeyNames = foreignKeyNames.Sort(StringComparer.OrdinalIgnoreCase);
        }

        var script = JoinStatements(statements, options.Format);
        var header = BuildHeader(options.Header, tableHeaderItems);
        if (!string.IsNullOrEmpty(header))
        {
            script = string.Concat(header, Environment.NewLine, Environment.NewLine, script);
        }

        return new PerTableWriteResult(
            effectiveTableName,
            script,
            orderedIndexNames,
            foreignKeyNames,
            includesExtendedProperties);
    }

    private string JoinStatements(IReadOnlyList<string> statements, SmoFormatOptions format)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < statements.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
                builder.AppendLine("GO");
                builder.AppendLine();
            }

            builder.AppendLine(statements[i]);
        }

        var script = builder.ToString().TrimEnd();
        return format.NormalizeWhitespace ? NormalizeWhitespace(script) : script;
    }

    private static string NormalizeWhitespace(string script)
    {
        var lines = script.Split(Environment.NewLine);
        var builder = new StringBuilder(script.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(lines[i].TrimEnd());
        }

        return builder.ToString();
    }

    private static string IndentBlock(string script, int spaces)
    {
        var indent = new string(' ', spaces);
        var lines = script.Split(Environment.NewLine);
        var builder = new StringBuilder(script.Length + spaces * lines.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            var line = lines[i];
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            builder.Append(indent);
            builder.Append(line);
        }

        return builder.ToString();
    }

    private static string? BuildHeader(PerTableHeaderOptions headerOptions, IReadOnlyList<PerTableHeaderItem>? tableItems)
    {
        if (headerOptions is null || !headerOptions.Enabled)
        {
            return null;
        }

        var items = new List<PerTableHeaderItem>();

        void Add(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            items.Add(PerTableHeaderItem.Create(label, value));
        }

        Add("Source", headerOptions.Source);
        Add("Profile", headerOptions.Profile);
        Add("Decisions", headerOptions.Decisions);

        if (!string.IsNullOrWhiteSpace(headerOptions.FingerprintHash))
        {
            var label = string.IsNullOrWhiteSpace(headerOptions.FingerprintAlgorithm)
                ? "Fingerprint"
                : $"{headerOptions.FingerprintAlgorithm} Fingerprint";
            Add(label, headerOptions.FingerprintHash);
        }

        if (!headerOptions.AdditionalItems.IsDefaultOrEmpty)
        {
            foreach (var item in headerOptions.AdditionalItems)
            {
                if (string.IsNullOrEmpty(item.Label) || string.IsNullOrEmpty(item.Value))
                {
                    continue;
                }

                items.Add(item);
            }
        }

        if (tableItems is { Count: > 0 })
        {
            foreach (var item in tableItems)
            {
                if (item is null || string.IsNullOrEmpty(item.Label) || string.IsNullOrEmpty(item.Value))
                {
                    continue;
                }

                items.Add(item.Normalize());
            }
        }

        if (items.Count == 0)
        {
            return null;
        }

        var ordered = items
            .Select(static item => item.Normalize())
            .OrderBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("/*");
        foreach (var item in ordered)
        {
            builder.Append("    ");
            builder.Append(item.Label);
            builder.Append(": ");
            builder.AppendLine(item.Value);
        }

        builder.Append("*/");
        return builder.ToString();
    }

    private ImmutableArray<string> BuildExtendedPropertyScripts(
        SmoTableDefinition table,
        string effectiveTableName,
        SmoFormatOptions format)
    {
        if (string.IsNullOrWhiteSpace(table.Description) && table.Columns.All(c => string.IsNullOrWhiteSpace(c.Description)))
        {
            return ImmutableArray<string>.Empty;
        }

        var scripts = ImmutableArray.CreateBuilder<string>();

        if (!string.IsNullOrWhiteSpace(table.Description))
        {
            scripts.Add(BuildTableExtendedPropertyScript(table.Schema, effectiveTableName, table.Description!, format));
        }

        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Description))
            {
                continue;
            }

            scripts.Add(BuildColumnExtendedPropertyScript(table.Schema, effectiveTableName, column.Name, column.Description!, format));
        }

        return scripts.ToImmutable();
    }

    private ImmutableArray<string> BuildTriggerScripts(
        SmoTableDefinition table,
        string effectiveTableName,
        SmoFormatOptions format)
    {
        if (table.Triggers.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var scripts = ImmutableArray.CreateBuilder<string>(table.Triggers.Length);
        foreach (var trigger in table.Triggers)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"-- Trigger: {trigger.Name} (disabled: {trigger.IsDisabled.ToString().ToLowerInvariant()})");

            if (!string.IsNullOrWhiteSpace(trigger.Definition))
            {
                builder.AppendLine(trigger.Definition.Trim());
            }

            if (trigger.IsDisabled)
            {
                var schemaIdentifier = QuoteIdentifier(trigger.Schema, format);
                var tableIdentifier = QuoteIdentifier(effectiveTableName, format);
                var triggerIdentifier = QuoteIdentifier(trigger.Name, format);
                builder.AppendLine($"ALTER TABLE {schemaIdentifier}.{tableIdentifier} DISABLE TRIGGER {triggerIdentifier};");
            }

            scripts.Add(builder.ToString().TrimEnd());
        }

        return scripts.ToImmutable();
    }

    private static Identifier CreateIdentifier(string value, SmoFormatOptions format)
    {
        return new Identifier
        {
            Value = value,
            QuoteType = MapQuoteType(format.IdentifierQuoteStrategy),
        };
    }

    private static QuoteType MapQuoteType(IdentifierQuoteStrategy strategy) => strategy switch
    {
        IdentifierQuoteStrategy.DoubleQuote => QuoteType.DoubleQuote,
        IdentifierQuoteStrategy.None => QuoteType.NotQuoted,
        _ => QuoteType.SquareBracket,
    };

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
                    ConstraintIdentifier = CreateIdentifier(constraintName, options.Format),
                };

                primaryKeyColumn.Constraints.Add(inlineConstraint);
            }
            else
            {
                var tableConstraint = new UniqueConstraintDefinition
                {
                    IsPrimaryKey = true,
                    Clustered = true,
                    ConstraintIdentifier = CreateIdentifier(constraintName, options.Format),
                };

                foreach (var column in sortedColumns)
                {
                    tableConstraint.Columns.Add(new ColumnWithSortOrder
                    {
                        Column = BuildColumnReference(column.Name, options.Format),
                        SortOrder = SortOrder.NotSpecified,
                    });
                }

                definition.TableConstraints.Add(tableConstraint);
            }
        }

        return new CreateTableStatement
        {
            SchemaObjectName = BuildSchemaObjectName(table.Schema, effectiveTableName, options.Format),
            Definition = definition,
        };
    }

    private ColumnDefinition BuildColumnDefinition(SmoColumnDefinition column, SmoBuildOptions options)
    {
        var definition = new ColumnDefinition
        {
            ColumnIdentifier = CreateIdentifier(column.Name, options.Format),
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
            definition.Collation = CreateIdentifier(column.Collation!, options.Format);
        }

        if (!options.EmitBareTableOnly)
        {
            var defaultExpression = ParseExpression(column.DefaultExpression);
            if (defaultExpression is not null)
            {
                var defaultConstraintDefinition = new DefaultConstraintDefinition
                {
                    Expression = defaultExpression,
                };

                if (column.DefaultConstraint is { Name: { Length: > 0 } name })
                {
                    defaultConstraintDefinition.ConstraintIdentifier = CreateIdentifier(name, options.Format);
                }

                definition.Constraints.Add(defaultConstraintDefinition);
            }

            if (!column.CheckConstraints.IsDefaultOrEmpty)
            {
                foreach (var checkConstraint in column.CheckConstraints)
                {
                    var predicate = ParsePredicate(checkConstraint.Expression);
                    if (predicate is null)
                    {
                        continue;
                    }

                    var checkDefinition = new CheckConstraintDefinition
                    {
                        CheckCondition = predicate,
                    };

                    if (!string.IsNullOrWhiteSpace(checkConstraint.Name))
                    {
                        checkDefinition.ConstraintIdentifier = CreateIdentifier(checkConstraint.Name!, options.Format);
                    }

                    definition.Constraints.Add(checkDefinition);
                }
            }
        }

        if (column.IsComputed && !string.IsNullOrWhiteSpace(column.ComputedExpression))
        {
            definition.ComputedColumnExpression = ParseExpression(column.ComputedExpression);
        }

        return definition;
    }

    private ImmutableArray<string> AddForeignKeys(
        CreateTableStatement statement,
        SmoTableDefinition table,
        string effectiveTableName,
        SmoBuildOptions options,
        out ImmutableDictionary<string, bool> foreignKeyTrustLookup)
    {
        if (options.EmitBareTableOnly || table.ForeignKeys.Length == 0 || statement.Definition is null)
        {
            foreignKeyTrustLookup = ImmutableDictionary<string, bool>.Empty;
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(table.ForeignKeys.Length);
        var trustBuilder = ImmutableDictionary.CreateBuilder<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var foreignKey in table.ForeignKeys)
        {
            var referencedTableName = options.NamingOverrides.GetEffectiveTableName(
                foreignKey.ReferencedSchema,
                foreignKey.ReferencedTable,
                foreignKey.ReferencedLogicalTable,
                foreignKey.ReferencedModule);

            var foreignKeyName = ResolveConstraintName(foreignKey.Name, table.Name, table.LogicalName, effectiveTableName);
            builder.Add(foreignKeyName);
            trustBuilder[foreignKeyName] = foreignKey.IsNoCheck;

            var deleteAction = MapDeleteAction(foreignKey.DeleteAction);
            var constraint = new ForeignKeyConstraintDefinition
            {
                ConstraintIdentifier = CreateIdentifier(foreignKeyName, options.Format),
                ReferenceTableName = BuildSchemaObjectName(foreignKey.ReferencedSchema, referencedTableName, options.Format),
            };

            if (deleteAction != DeleteUpdateAction.NoAction)
            {
                constraint.DeleteAction = deleteAction;
            }

            constraint.Columns.Add(CreateIdentifier(foreignKey.Column, options.Format));
            constraint.ReferencedTableColumns.Add(CreateIdentifier(foreignKey.ReferencedColumn, options.Format));

            statement.Definition.TableConstraints.Add(constraint);
        }

        foreignKeyTrustLookup = trustBuilder.ToImmutable();
        return builder.ToImmutable();
    }

    private CreateIndexStatement BuildCreateIndexStatement(
        SmoTableDefinition table,
        SmoIndexDefinition index,
        string effectiveTableName,
        string indexName,
        SmoFormatOptions format)
    {
        var statement = new CreateIndexStatement
        {
            Name = CreateIdentifier(indexName, format),
            OnName = BuildSchemaObjectName(table.Schema, effectiveTableName, format),
            Unique = index.IsUnique,
        };

        foreach (var column in index.Columns.OrderBy(c => c.Ordinal))
        {
            if (column.IsIncluded)
            {
                statement.IncludeColumns.Add(BuildColumnReference(column.Name, format));
                continue;
            }

            statement.Columns.Add(new ColumnWithSortOrder
            {
                Column = BuildColumnReference(column.Name, format),
                SortOrder = column.IsDescending ? SortOrder.Descending : SortOrder.Ascending,
            });
        }

        ApplyIndexMetadata(statement, index.Metadata, format);

        return statement;
    }

    private AlterIndexStatement BuildDisableIndexStatement(
        SmoTableDefinition table,
        string effectiveTableName,
        string indexName,
        SmoFormatOptions format)
    {
        return new AlterIndexStatement
        {
            AlterIndexType = AlterIndexType.Disable,
            Name = CreateIdentifier(indexName, format),
            OnName = BuildSchemaObjectName(table.Schema, effectiveTableName, format),
        };
    }

    private void ApplyIndexMetadata(CreateIndexStatement statement, SmoIndexMetadata metadata, SmoFormatOptions format)
    {
        if (!string.IsNullOrWhiteSpace(metadata.FilterDefinition))
        {
            var predicate = ParsePredicate(metadata.FilterDefinition);
            if (predicate is not null)
            {
                BooleanExpression filter = predicate;
                if (filter is not BooleanParenthesisExpression)
                {
                    filter = new BooleanParenthesisExpression { Expression = filter };
                }

                statement.FilterPredicate = filter;
            }
        }

        foreach (var option in BuildIndexOptions(metadata))
        {
            statement.IndexOptions.Add(option);
        }

        var clause = BuildFileGroupOrPartitionScheme(metadata, format);
        if (clause is not null)
        {
            statement.OnFileGroupOrPartitionScheme = clause;
        }
    }

    private static IEnumerable<IndexOption> BuildIndexOptions(SmoIndexMetadata metadata)
    {
        var options = new List<IndexOption>();

        if (metadata.FillFactor is int fillFactor)
        {
            options.Add(new IndexExpressionOption
            {
                OptionKind = IndexOptionKind.FillFactor,
                Expression = new IntegerLiteral { Value = fillFactor.ToString(CultureInfo.InvariantCulture) },
            });
        }

        options.Add(new IndexStateOption
        {
            OptionKind = IndexOptionKind.PadIndex,
            OptionState = metadata.IsPadded ? OptionState.On : OptionState.Off,
        });

        options.Add(new IgnoreDupKeyIndexOption
        {
            OptionKind = IndexOptionKind.IgnoreDupKey,
            OptionState = metadata.IgnoreDuplicateKey ? OptionState.On : OptionState.Off,
        });

        options.Add(new IndexStateOption
        {
            OptionKind = IndexOptionKind.StatisticsNoRecompute,
            OptionState = metadata.StatisticsNoRecompute ? OptionState.On : OptionState.Off,
        });

        options.Add(new IndexStateOption
        {
            OptionKind = IndexOptionKind.AllowRowLocks,
            OptionState = metadata.AllowRowLocks ? OptionState.On : OptionState.Off,
        });

        options.Add(new IndexStateOption
        {
            OptionKind = IndexOptionKind.AllowPageLocks,
            OptionState = metadata.AllowPageLocks ? OptionState.On : OptionState.Off,
        });

        foreach (var compression in BuildCompressionOptions(metadata.DataCompression))
        {
            options.Add(compression);
        }

        return options;
    }

    private static IEnumerable<DataCompressionOption> BuildCompressionOptions(ImmutableArray<SmoIndexCompressionSetting> settings)
    {
        if (settings.IsDefaultOrEmpty)
        {
            yield break;
        }

        foreach (var group in settings.GroupBy(s => s.Compression, StringComparer.OrdinalIgnoreCase))
        {
            var level = MapCompressionLevel(group.Key);
            if (level is null)
            {
                continue;
            }

            var option = new DataCompressionOption
            {
                OptionKind = IndexOptionKind.DataCompression,
                CompressionLevel = level.Value,
            };

            foreach (var range in CollapseRanges(group.Select(s => s.PartitionNumber).OrderBy(n => n)))
            {
                var partitionRange = new CompressionPartitionRange
                {
                    From = new IntegerLiteral { Value = range.Start.ToString(CultureInfo.InvariantCulture) },
                };

                if (range.End > range.Start)
                {
                    partitionRange.To = new IntegerLiteral { Value = range.End.ToString(CultureInfo.InvariantCulture) };
                }

                option.PartitionRanges.Add(partitionRange);
            }

            yield return option;
        }
    }

    private static DataCompressionLevel? MapCompressionLevel(string compression)
    {
        if (string.IsNullOrWhiteSpace(compression))
        {
            return null;
        }

        return compression.Trim().ToUpperInvariant() switch
        {
            "NONE" => DataCompressionLevel.None,
            "ROW" => DataCompressionLevel.Row,
            "PAGE" => DataCompressionLevel.Page,
            "COLUMNSTORE" => DataCompressionLevel.ColumnStore,
            "COLUMNSTORE_ARCHIVE" => DataCompressionLevel.ColumnStoreArchive,
            _ => null,
        };
    }

    private static FileGroupOrPartitionScheme? BuildFileGroupOrPartitionScheme(SmoIndexMetadata metadata, SmoFormatOptions format)
    {
        if (metadata.DataSpace is null)
        {
            return null;
        }

        var clause = new FileGroupOrPartitionScheme
        {
            Name = new IdentifierOrValueExpression
            {
                Identifier = CreateIdentifier(metadata.DataSpace.Name, format),
            }
        };

        if (string.Equals(metadata.DataSpace.Type, "PARTITION_SCHEME", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var column in metadata.PartitionColumns.OrderBy(static c => c.Ordinal))
            {
                clause.PartitionSchemeColumns.Add(CreateIdentifier(column.Name, format));
            }
        }

        return clause;
    }

    private static IEnumerable<(int Start, int End)> CollapseRanges(IEnumerable<int> numbers)
    {
        using var enumerator = numbers.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            yield break;
        }

        var start = enumerator.Current;
        var previous = start;

        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;
            if (current == previous + 1)
            {
                previous = current;
                continue;
            }

            yield return (Start: start, End: previous);
            start = previous = current;
        }

        yield return (Start: start, End: previous);
    }

    private static SchemaObjectName BuildSchemaObjectName(string schema, string name, SmoFormatOptions format)
    {
        return new SchemaObjectName
        {
            Identifiers =
            {
                CreateIdentifier(schema, format),
                CreateIdentifier(name, format),
            }
        };
    }

    private static ColumnReferenceExpression BuildColumnReference(string columnName, SmoFormatOptions format)
    {
        return new ColumnReferenceExpression
        {
            MultiPartIdentifier = new MultiPartIdentifier
            {
                Identifiers = { CreateIdentifier(columnName, format) }
            }
        };
    }

    private static DataTypeReference TranslateDataType(DataType dataType)
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

    private string Script(
        TSqlStatement statement,
        IReadOnlyDictionary<string, bool>? foreignKeyTrustLookup = null,
        SmoFormatOptions? format = null)
    {
        _context.ScriptGenerator.GenerateScript(statement, out var script);
        var trimmed = script.Trim();

        return statement switch
        {
            CreateTableStatement createTable => FormatCreateTableScript(trimmed, createTable, foreignKeyTrustLookup),
            _ => (format?.NormalizeWhitespace ?? true) ? NormalizeWhitespace(trimmed) : trimmed,
        };
    }

    private string Script(TSqlStatement statement, SmoFormatOptions format)
        => Script(statement, null, format);

    public static string FormatCreateTableScript(
        string script,
        CreateTableStatement statement,
        IReadOnlyDictionary<string, bool>? foreignKeyTrustLookup)
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
                builder.Append(FormatTrailingComma(line));
                continue;
            }
        }

        var withDefaults = builder.ToString();
        var withForeignKeys = FormatForeignKeyConstraints(withDefaults, foreignKeyTrustLookup);
        return FormatPrimaryKeyConstraints(withForeignKeys);
    }

    private static string FormatTrailingComma(string line)
    {
        var trimmed = line.TrimEnd();
        if (!trimmed.EndsWith(",", StringComparison.Ordinal))
        {
            return line;
        }

        var indentLength = line.Length - line.TrimStart().Length;
        var indent = line[..indentLength];
        var content = trimmed[..^1].TrimEnd();
        var withoutIndent = content.TrimStart();

        return indent + withoutIndent + ',';
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
            builder.Append(" ");
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

    private static string FormatForeignKeyConstraints(
        string script,
        IReadOnlyDictionary<string, bool>? foreignKeyTrustLookup)
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
                var ownerSegment = trimmed[(foreignKeyIndex + "FOREIGN KEY".Length)..referencesIndex].Trim();
                var referencesSegment = trimmed[referencesIndex..].Trim();

                var onDeleteIndex = referencesSegment.IndexOf("ON DELETE", StringComparison.OrdinalIgnoreCase);
                string? onDeleteSegment = null;
                if (onDeleteIndex >= 0)
                {
                    onDeleteSegment = referencesSegment[onDeleteIndex..].TrimEnd();
                    referencesSegment = referencesSegment[..onDeleteIndex].TrimEnd();
                }

                var onUpdateIndex = referencesSegment.IndexOf("ON UPDATE", StringComparison.OrdinalIgnoreCase);
                string? onUpdateSegment = null;
                if (onUpdateIndex >= 0)
                {
                    onUpdateSegment = referencesSegment[onUpdateIndex..].TrimEnd();
                    referencesSegment = referencesSegment[..onUpdateIndex].TrimEnd();
                }

                var hasOnDelete = !string.IsNullOrEmpty(onDeleteSegment);
                var hasOnUpdate = !string.IsNullOrEmpty(onUpdateSegment);
                var hasOnClauses = hasOnDelete || hasOnUpdate;

                var ownerIndent = indent + new string(' ', 4);
                builder.Append(indent);
                builder.Append(constraintSegment);
                builder.AppendLine();
                builder.Append(ownerIndent);
                builder.Append("FOREIGN KEY ");
                builder.Append(ownerSegment);
                builder.Append(" ");
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
                    var clauseIndent = ownerIndent + new string(' ', 4);
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

                if (foreignKeyTrustLookup is not null)
                {
                    var constraintName = ExtractConstraintName(constraintSegment);
                    if (!string.IsNullOrEmpty(constraintName) &&
                        foreignKeyTrustLookup.TryGetValue(constraintName, out var isNoCheck) &&
                        isNoCheck)
                    {
                        builder.Append(ownerIndent);
                        builder.AppendLine("-- Source constraint was not trusted (WITH NOCHECK)");
                    }
                }

                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private static string ExtractConstraintName(string constraintSegment)
    {
        if (string.IsNullOrWhiteSpace(constraintSegment))
        {
            return string.Empty;
        }

        var working = constraintSegment.Trim();
        if (working.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
        {
            working = working["CONSTRAINT".Length..].Trim();
        }

        if (working.Length == 0)
        {
            return string.Empty;
        }

        if (working.StartsWith("[", StringComparison.Ordinal) && working.EndsWith("]", StringComparison.Ordinal) && working.Length > 2)
        {
            working = working[1..^1];
        }
        else if (working.StartsWith("\"", StringComparison.Ordinal) && working.EndsWith("\"", StringComparison.Ordinal) && working.Length > 2)
        {
            working = working[1..^1];
        }

        return working;
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
                var constraintSegment = working[..primaryIndex].TrimEnd();
                var primarySegment = working[primaryIndex..].Trim();

                builder.Append(indent);
                builder.Append(constraintSegment);
                builder.AppendLine();
                builder.Append(indent);
                builder.Append(new string(' ', 4));
                builder.Append(primarySegment);
                builder.AppendLine(trailingComma);
                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private static ScalarExpression? ParseExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var parser = new TSql150Parser(initialQuotedIdentifiers: false);
        var reader = new StringReader(expression);
        var fragment = parser.ParseExpression(reader, out var errors);
        if (errors is not null && errors.Count > 0)
        {
            return null;
        }

        return fragment;
    }

    private static BooleanExpression? ParsePredicate(string? predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate))
        {
            return null;
        }

        var parser = new TSql150Parser(initialQuotedIdentifiers: false);
        var reader = new StringReader(predicate);
        var fragment = parser.ParseBooleanExpression(reader, out var errors);
        if (fragment is null || (errors is not null && errors.Count > 0))
        {
            return null;
        }

        return fragment;
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

    private static string BuildTableExtendedPropertyScript(
        string schema,
        string table,
        string description,
        SmoFormatOptions format)
    {
        var schemaIdentifier = QuoteIdentifier(schema, format);
        var tableIdentifier = QuoteIdentifier(table, format);
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
""".Trim();
    }

    private static string BuildColumnExtendedPropertyScript(
        string schema,
        string table,
        string column,
        string description,
        SmoFormatOptions format)
    {
        var schemaIdentifier = QuoteIdentifier(schema, format);
        var tableIdentifier = QuoteIdentifier(table, format);
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
""".Trim();
    }

    private static string QuoteIdentifier(string identifier, SmoFormatOptions format)
    {
        return format.IdentifierQuoteStrategy switch
        {
            IdentifierQuoteStrategy.DoubleQuote => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"",
            IdentifierQuoteStrategy.None => identifier,
            _ => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]",
        };
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
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
}

public sealed record PerTableWriteResult(
    string EffectiveTableName,
    string Script,
    ImmutableArray<string> IndexNames,
    ImmutableArray<string> ForeignKeyNames,
    bool IncludesExtendedProperties);
