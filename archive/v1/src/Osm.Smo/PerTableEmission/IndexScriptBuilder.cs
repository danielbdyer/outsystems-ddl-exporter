using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Osm.Smo;
using ScriptDomSortOrder = Microsoft.SqlServer.TransactSql.ScriptDom.SortOrder;

namespace Osm.Smo.PerTableEmission;

internal sealed class IndexScriptBuilder
{
    private readonly IdentifierFormatter _identifierFormatter;

    public IndexScriptBuilder(IdentifierFormatter identifierFormatter)
    {
        _identifierFormatter = identifierFormatter ?? throw new ArgumentNullException(nameof(identifierFormatter));
    }

    public CreateIndexStatement BuildCreateIndexStatement(
        SmoTableDefinition table,
        SmoIndexDefinition index,
        string effectiveTableName,
        string indexName,
        SmoFormatOptions format)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        if (index is null)
        {
            throw new ArgumentNullException(nameof(index));
        }

        if (effectiveTableName is null)
        {
            throw new ArgumentNullException(nameof(effectiveTableName));
        }

        if (indexName is null)
        {
            throw new ArgumentNullException(nameof(indexName));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        var statement = new CreateIndexStatement
        {
            Name = _identifierFormatter.CreateIdentifier(indexName, format),
            OnName = _identifierFormatter.BuildSchemaObjectName(table.Schema, effectiveTableName, format),
            Unique = index.IsUnique,
        };

        var columnNameMap = BuildColumnNameMap(table);

        foreach (var column in index.Columns.OrderBy(c => c.Ordinal))
        {
            if (column.IsIncluded)
            {
                statement.IncludeColumns.Add(_identifierFormatter.BuildColumnReference(column.Name, format));
                continue;
            }

            var orderedColumn = new ColumnWithSortOrder
            {
                Column = _identifierFormatter.BuildColumnReference(column.Name, format),
            };

            if (column.IsDescending)
            {
                orderedColumn.SortOrder = ScriptDomSortOrder.Descending;
            }

            statement.Columns.Add(orderedColumn);
        }

        ApplyIndexMetadata(statement, index.Metadata, format, columnNameMap);

        return statement;
    }

    public AlterIndexStatement BuildDisableIndexStatement(
        SmoTableDefinition table,
        string effectiveTableName,
        string indexName,
        SmoFormatOptions format)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        if (effectiveTableName is null)
        {
            throw new ArgumentNullException(nameof(effectiveTableName));
        }

        if (indexName is null)
        {
            throw new ArgumentNullException(nameof(indexName));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        return new AlterIndexStatement
        {
            AlterIndexType = AlterIndexType.Disable,
            Name = _identifierFormatter.CreateIdentifier(indexName, format),
            OnName = _identifierFormatter.BuildSchemaObjectName(table.Schema, effectiveTableName, format),
        };
    }

    private void ApplyIndexMetadata(
        CreateIndexStatement statement,
        SmoIndexMetadata metadata,
        SmoFormatOptions format,
        IReadOnlyDictionary<string, string> columnNameMap)
    {
        if (!string.IsNullOrWhiteSpace(metadata.FilterDefinition))
        {
            var predicate = ParsePredicate(metadata.FilterDefinition);
            if (predicate is not null)
            {
                RewriteColumnReferences(predicate, columnNameMap);
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

    private static IReadOnlyDictionary<string, string> BuildColumnNameMap(SmoTableDefinition table)
    {
        if (table.Columns.Length == 0)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.PhysicalName) || string.IsNullOrWhiteSpace(column.Name))
            {
                continue;
            }

            builder[column.PhysicalName] = column.Name;
        }

        return builder.ToImmutable();
    }

    private static void RewriteColumnReferences(
        TSqlFragment? fragment,
        IReadOnlyDictionary<string, string> columnNameMap)
    {
        if (fragment is null || columnNameMap.Count == 0)
        {
            return;
        }

        var visitor = new ColumnReferenceRewriteVisitor(columnNameMap);
        fragment.Accept(visitor);
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

        if (metadata.IsPadded)
        {
            options.Add(new IndexStateOption
            {
                OptionKind = IndexOptionKind.PadIndex,
                OptionState = OptionState.On,
            });
        }

        if (metadata.IgnoreDuplicateKey)
        {
            options.Add(new IgnoreDupKeyIndexOption
            {
                OptionKind = IndexOptionKind.IgnoreDupKey,
                OptionState = OptionState.On,
            });
        }

        if (metadata.StatisticsNoRecompute)
        {
            options.Add(new IndexStateOption
            {
                OptionKind = IndexOptionKind.StatisticsNoRecompute,
                OptionState = OptionState.On,
            });
        }

        if (!metadata.AllowRowLocks)
        {
            options.Add(new IndexStateOption
            {
                OptionKind = IndexOptionKind.AllowRowLocks,
                OptionState = OptionState.Off,
            });
        }

        if (!metadata.AllowPageLocks)
        {
            options.Add(new IndexStateOption
            {
                OptionKind = IndexOptionKind.AllowPageLocks,
                OptionState = OptionState.Off,
            });
        }

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

            if (level.Value == DataCompressionLevel.None)
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

    private FileGroupOrPartitionScheme? BuildFileGroupOrPartitionScheme(SmoIndexMetadata metadata, SmoFormatOptions format)
    {
        if (!ShouldEmitDataSpaceClause(metadata, out var dataSpace))
        {
            return null;
        }

        var clause = new FileGroupOrPartitionScheme
        {
            Name = new IdentifierOrValueExpression
            {
                Identifier = _identifierFormatter.CreateIdentifier(dataSpace.Name, format),
            }
        };

        if (string.Equals(dataSpace.Type, "PARTITION_SCHEME", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var column in metadata.PartitionColumns.OrderBy(static c => c.Ordinal))
            {
                clause.PartitionSchemeColumns.Add(_identifierFormatter.CreateIdentifier(column.Name, format));
            }
        }

        return clause;
    }

    private static bool ShouldEmitDataSpaceClause(SmoIndexMetadata metadata, [NotNullWhen(true)] out SmoIndexDataSpace? dataSpace)
    {
        dataSpace = metadata.DataSpace;
        if (dataSpace is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(dataSpace.Name))
        {
            return false;
        }

        if (string.Equals(dataSpace.Name, "PRIMARY", StringComparison.OrdinalIgnoreCase) &&
            metadata.PartitionColumns.IsDefaultOrEmpty)
        {
            return false;
        }

        if (string.Equals(dataSpace.Type, "PARTITION_SCHEME", StringComparison.OrdinalIgnoreCase) &&
            metadata.PartitionColumns.IsDefaultOrEmpty)
        {
            return false;
        }

        return true;
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

    private static BooleanExpression? ParsePredicate(string? predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate))
        {
            return null;
        }

        var parser = new TSql150Parser(initialQuotedIdentifiers: false);
        using var reader = new StringReader(predicate);
        var fragment = parser.ParseBooleanExpression(reader, out var errors);
        if (fragment is null || (errors is not null && errors.Count > 0))
        {
            return null;
        }

        return fragment;
    }

    private sealed class ColumnReferenceRewriteVisitor : TSqlFragmentVisitor
    {
        private readonly IReadOnlyDictionary<string, string> _columnNameMap;

        public ColumnReferenceRewriteVisitor(IReadOnlyDictionary<string, string> columnNameMap)
        {
            _columnNameMap = columnNameMap;
        }

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            if (node?.MultiPartIdentifier is null || node.MultiPartIdentifier.Identifiers.Count == 0)
            {
                return;
            }

            foreach (var identifier in node.MultiPartIdentifier.Identifiers)
            {
                if (identifier is null || string.IsNullOrEmpty(identifier.Value))
                {
                    continue;
                }

                if (_columnNameMap.TryGetValue(identifier.Value, out var replacement))
                {
                    identifier.Value = replacement;
                }
            }
        }
    }
}
