using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Pipeline.SqlExtraction;

/// <summary>
/// Allows callers to weaken the strict metadata contract enforced by the SQL metadata reader
/// by marking specific columns as optional at runtime.
/// </summary>
public sealed class MetadataContractOverrides
{
    private readonly IReadOnlyDictionary<string, HashSet<string>> _optionalColumns;

    public static MetadataContractOverrides Strict { get; } = new();

    public MetadataContractOverrides()
        : this(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase))
    {
    }

    public MetadataContractOverrides(IReadOnlyDictionary<string, IEnumerable<string>> optionalColumns)
        : this(Clone(optionalColumns))
    {
    }

    private MetadataContractOverrides(Dictionary<string, HashSet<string>> optionalColumns)
    {
        _optionalColumns = optionalColumns ?? throw new ArgumentNullException(nameof(optionalColumns));
    }

    public bool HasOverrides => _optionalColumns.Count > 0;

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> OptionalColumns
        => _optionalColumns.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyCollection<string>)pair.Value.ToImmutableArray(),
            StringComparer.OrdinalIgnoreCase);

    public bool IsColumnOptional(string resultSetName, string columnName)
    {
        if (string.IsNullOrWhiteSpace(resultSetName))
        {
            throw new ArgumentException("Result set name must be provided.", nameof(resultSetName));
        }

        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name must be provided.", nameof(columnName));
        }

        return _optionalColumns.TryGetValue(resultSetName, out var columns)
            && columns.Contains(columnName);
    }

    public MetadataContractOverrides WithOptionalColumn(string resultSetName, string columnName)
    {
        if (string.IsNullOrWhiteSpace(resultSetName))
        {
            throw new ArgumentException("Result set name must be provided.", nameof(resultSetName));
        }

        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name must be provided.", nameof(columnName));
        }

        var normalizedResultSet = resultSetName.Trim();
        var normalizedColumn = columnName.Trim();

        var copy = new Dictionary<string, HashSet<string>>(_optionalColumns.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _optionalColumns)
        {
            copy[pair.Key] = new HashSet<string>(pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        if (!copy.TryGetValue(normalizedResultSet, out var columns))
        {
            columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            copy[normalizedResultSet] = columns;
        }

        columns.Add(normalizedColumn);
        return new MetadataContractOverrides(copy);
    }

    public MetadataContractOverrides WithOptionalColumns(string resultSetName, IEnumerable<string> columnNames)
    {
        if (columnNames is null)
        {
            throw new ArgumentNullException(nameof(columnNames));
        }

        var overrides = this;
        foreach (var columnName in columnNames)
        {
            overrides = overrides.WithOptionalColumn(resultSetName, columnName);
        }

        return overrides;
    }

    private static Dictionary<string, HashSet<string>> Clone(IReadOnlyDictionary<string, IEnumerable<string>> optionalColumns)
    {
        if (optionalColumns is null)
        {
            throw new ArgumentNullException(nameof(optionalColumns));
        }

        var copy = new Dictionary<string, HashSet<string>>(optionalColumns.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in optionalColumns)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new ArgumentException("Result set keys must be non-empty.", nameof(optionalColumns));
            }

            var normalizedKey = pair.Key.Trim();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var column in pair.Value ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(column))
                {
                    continue;
                }

                set.Add(column.Trim());
            }

            if (set.Count > 0)
            {
                copy[normalizedKey] = set;
            }
        }

        return copy;
    }
}
