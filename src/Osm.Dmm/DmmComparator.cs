using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Osm.Dmm;

public sealed class DmmComparator
{
    private readonly DmmComparisonFeatures _features;

    public DmmComparator()
        : this(DmmComparisonFeatures.Columns | DmmComparisonFeatures.PrimaryKeys)
    {
    }

    public DmmComparator(DmmComparisonFeatures features)
    {
        _features = features;
    }

    public DmmComparisonResult Compare(
        IReadOnlyList<DmmTable> modelTables,
        IReadOnlyList<DmmTable> dmmTables,
        DmmComparisonFeatures? featuresOverride = null)
    {
        if (modelTables is null)
        {
            throw new ArgumentNullException(nameof(modelTables));
        }

        if (dmmTables is null)
        {
            throw new ArgumentNullException(nameof(dmmTables));
        }

        var features = featuresOverride ?? _features;

        var modelDifferences = new List<DmmDifference>();
        var ssdtDifferences = new List<DmmDifference>();
        var dmmLookup = dmmTables.ToDictionary(
            table => Key(table.Schema, table.Name),
            table => table,
            StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in modelTables)
        {
            var key = Key(table.Schema, table.Name);
            if (!dmmLookup.TryGetValue(key, out var dmmTable))
            {
                modelDifferences.Add(Difference.Table(table.Schema, table.Name, "TablePresence", "Present", "Missing"));
                continue;
            }

            seen.Add(key);

            if (features.HasFlag(DmmComparisonFeatures.Columns))
            {
                CompareColumns(table, dmmTable, modelDifferences, ssdtDifferences, features);
            }

            if (features.HasFlag(DmmComparisonFeatures.PrimaryKeys))
            {
                ComparePrimaryKeys(table, dmmTable, modelDifferences, ssdtDifferences);
            }

            if (features.HasFlag(DmmComparisonFeatures.Indexes))
            {
                CompareIndexes(table, dmmTable, modelDifferences, ssdtDifferences);
            }

            if (features.HasFlag(DmmComparisonFeatures.ForeignKeys))
            {
                CompareForeignKeys(table, dmmTable, modelDifferences, ssdtDifferences);
            }

            if (features.HasFlag(DmmComparisonFeatures.ExtendedProperties))
            {
                CompareTableDescription(table, dmmTable, ssdtDifferences);
            }
        }

        foreach (var table in dmmTables)
        {
            var key = Key(table.Schema, table.Name);
            if (!seen.Contains(key))
            {
                ssdtDifferences.Add(Difference.Table(table.Schema, table.Name, "TablePresence", "Missing", "Present"));
            }
        }

        var isMatch = modelDifferences.Count == 0 && ssdtDifferences.Count == 0;
        return new DmmComparisonResult(isMatch, modelDifferences, ssdtDifferences);
    }

    private static void CompareColumns(
        DmmTable expected,
        DmmTable actual,
        List<DmmDifference> modelDifferences,
        List<DmmDifference> ssdtDifferences,
        DmmComparisonFeatures features)
    {
        var expectedColumns = expected.Columns;
        var actualColumns = actual.Columns;

        if (expectedColumns.Count != actualColumns.Count)
        {
            modelDifferences.Add(Difference.Table(
                expected.Schema,
                expected.Name,
                "ColumnCount",
                expectedColumns.Count.ToString(CultureInfo.InvariantCulture),
                actualColumns.Count.ToString(CultureInfo.InvariantCulture)));
        }

        var expectedOrder = expectedColumns.Select(static c => c.Name).ToArray();
        var actualOrder = actualColumns.Select(static c => c.Name).ToArray();

        var missingColumns = expectedOrder
            .Except(actualOrder, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var column in missingColumns)
        {
            modelDifferences.Add(Difference.Column(expected.Schema, expected.Name, column, "Presence", "Present", "Missing"));
        }

        var unexpectedColumns = actualOrder
            .Except(expectedOrder, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var column in unexpectedColumns)
        {
            ssdtDifferences.Add(Difference.Column(actual.Schema, actual.Name, column, "Presence", "Missing", "Present"));
        }

        var canCompareOrder = missingColumns.Length == 0 && unexpectedColumns.Length == 0;
        if (canCompareOrder && !expectedOrder.SequenceEqual(actualOrder, StringComparer.OrdinalIgnoreCase))
        {
            ssdtDifferences.Add(Difference.Table(
                expected.Schema,
                expected.Name,
                "ColumnOrder",
                string.Join(", ", expectedOrder),
                string.Join(", ", actualOrder)));
        }

        var actualByName = actualColumns
            .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var expectedColumn in expectedColumns)
        {
            if (!actualByName.TryGetValue(expectedColumn.Name, out var actualColumn))
            {
                continue;
            }

            if (!string.Equals(expectedColumn.DataType, actualColumn.DataType, StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add(Difference.Column(
                    expected.Schema,
                    expected.Name,
                    expectedColumn.Name,
                    "DataType",
                    expectedColumn.DataType,
                    actualColumn.DataType));
            }

            if (expectedColumn.IsNullable != actualColumn.IsNullable)
            {
                ssdtDifferences.Add(Difference.Column(
                    expected.Schema,
                    expected.Name,
                    expectedColumn.Name,
                    "Nullability",
                    expectedColumn.IsNullable ? "NULL" : "NOT NULL",
                    actualColumn.IsNullable ? "NULL" : "NOT NULL"));
            }

            if (!string.Equals(
                    expectedColumn.DefaultExpression ?? string.Empty,
                    actualColumn.DefaultExpression ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add(Difference.Column(
                    expected.Schema,
                    expected.Name,
                    expectedColumn.Name,
                    "Default",
                    ValueOrNull(expectedColumn.DefaultExpression),
                    ValueOrNull(actualColumn.DefaultExpression)));
            }

            if (!string.Equals(
                    expectedColumn.Collation ?? string.Empty,
                    actualColumn.Collation ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add(Difference.Column(
                    expected.Schema,
                    expected.Name,
                    expectedColumn.Name,
                    "Collation",
                    ValueOrNull(expectedColumn.Collation),
                    ValueOrNull(actualColumn.Collation)));
            }

            if (features.HasFlag(DmmComparisonFeatures.ExtendedProperties))
            {
                var expectedDescription = NormalizeDescription(expectedColumn.Description);
                var actualDescription = NormalizeDescription(actualColumn.Description);
                if (!string.Equals(expectedDescription, actualDescription, StringComparison.Ordinal))
                {
                    ssdtDifferences.Add(Difference.Column(
                        expected.Schema,
                        expected.Name,
                        expectedColumn.Name,
                        "Description",
                        ValueOrNull(expectedDescription),
                        ValueOrNull(actualDescription)));
                }
            }
        }
    }

    private static void ComparePrimaryKeys(
        DmmTable expected,
        DmmTable actual,
        List<DmmDifference> modelDifferences,
        List<DmmDifference> ssdtDifferences)
    {
        var expectedColumns = expected.PrimaryKeyColumns.ToArray();
        var actualColumns = actual.PrimaryKeyColumns.ToArray();

        if (expectedColumns.Length == 0)
        {
            if (actualColumns.Length > 0)
            {
                ssdtDifferences.Add(Difference.Table(expected.Schema, expected.Name, "PrimaryKeyPresence", "Absent", "Present"));
            }

            return;
        }

        if (actualColumns.Length == 0)
        {
            modelDifferences.Add(Difference.Table(expected.Schema, expected.Name, "PrimaryKeyPresence", "Present", "Missing"));
            return;
        }

        if (expectedColumns.Length != actualColumns.Length)
        {
            var difference = Difference.Table(
                expected.Schema,
                expected.Name,
                "PrimaryKeyColumns",
                string.Join(", ", expectedColumns),
                string.Join(", ", actualColumns));

            if (expectedColumns.Length > actualColumns.Length)
            {
                modelDifferences.Add(difference);
            }
            else
            {
                ssdtDifferences.Add(difference);
            }

            return;
        }

        for (var i = 0; i < expectedColumns.Length; i++)
        {
            if (!string.Equals(expectedColumns[i], actualColumns[i], StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add(Difference.Table(
                    expected.Schema,
                    expected.Name,
                    "PrimaryKeyOrdinal",
                    expectedColumns[i],
                    actualColumns[i]));
            }
        }
    }

    private static void CompareIndexes(
        DmmTable expected,
        DmmTable actual,
        List<DmmDifference> modelDifferences,
        List<DmmDifference> ssdtDifferences)
    {
        var actualLookup = actual.Indexes
            .ToDictionary(index => index.Name, index => index, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var expectedIndex in expected.Indexes)
        {
            if (!actualLookup.TryGetValue(expectedIndex.Name, out var actualIndex))
            {
                modelDifferences.Add(Difference.Index(expected.Schema, expected.Name, expectedIndex.Name, "Presence", "Present", "Missing"));
                continue;
            }

            seen.Add(expectedIndex.Name);
            CompareIndex(expected.Schema, expected.Name, expectedIndex, actualIndex, ssdtDifferences);
        }

        foreach (var actualIndex in actual.Indexes)
        {
            if (!seen.Contains(actualIndex.Name))
            {
                ssdtDifferences.Add(Difference.Index(actual.Schema, actual.Name, actualIndex.Name, "Presence", "Missing", "Present"));
            }
        }
    }

    private static void CompareIndex(
        string schema,
        string table,
        DmmIndex expected,
        DmmIndex actual,
        List<DmmDifference> ssdtDifferences)
    {
        if (expected.IsUnique != actual.IsUnique)
        {
            ssdtDifferences.Add(Difference.Index(
                schema,
                table,
                expected.Name,
                "Uniqueness",
                expected.IsUnique ? "UNIQUE" : "NONUNIQUE",
                actual.IsUnique ? "UNIQUE" : "NONUNIQUE"));
        }

        CompareIndexColumns(schema, table, expected.Name, "KeyColumns", expected.KeyColumns, actual.KeyColumns, ssdtDifferences);
        CompareIndexColumns(schema, table, expected.Name, "IncludedColumns", expected.IncludedColumns, actual.IncludedColumns, ssdtDifferences);

        var expectedFilter = NormalizeFilter(expected.FilterDefinition);
        var actualFilter = NormalizeFilter(actual.FilterDefinition);
        if (!string.Equals(expectedFilter ?? string.Empty, actualFilter ?? string.Empty, StringComparison.Ordinal))
        {
            ssdtDifferences.Add(Difference.Index(
                schema,
                table,
                expected.Name,
                "Filter",
                ValueOrNull(expectedFilter),
                ValueOrNull(actualFilter)));
        }

        if (expected.IsDisabled != actual.IsDisabled)
        {
            ssdtDifferences.Add(Difference.Index(
                schema,
                table,
                expected.Name,
                "State",
                expected.IsDisabled ? "DISABLED" : "ENABLED",
                actual.IsDisabled ? "DISABLED" : "ENABLED"));
        }

        CompareBooleanOption(schema, table, expected.Name, "PAD_INDEX", expected.Options.PadIndex, actual.Options.PadIndex, defaultValue: false, ssdtDifferences);
        CompareBooleanOption(schema, table, expected.Name, "IGNORE_DUP_KEY", expected.Options.IgnoreDuplicateKey, actual.Options.IgnoreDuplicateKey, defaultValue: false, ssdtDifferences);
        CompareBooleanOption(schema, table, expected.Name, "ALLOW_ROW_LOCKS", expected.Options.AllowRowLocks, actual.Options.AllowRowLocks, defaultValue: true, ssdtDifferences);
        CompareBooleanOption(schema, table, expected.Name, "ALLOW_PAGE_LOCKS", expected.Options.AllowPageLocks, actual.Options.AllowPageLocks, defaultValue: true, ssdtDifferences);
        CompareBooleanOption(schema, table, expected.Name, "STATISTICS_NORECOMPUTE", expected.Options.StatisticsNoRecompute, actual.Options.StatisticsNoRecompute, defaultValue: false, ssdtDifferences);

        var expectedFill = expected.Options.FillFactor;
        var actualFill = actual.Options.FillFactor;
        if (expectedFill.HasValue || actualFill.HasValue)
        {
            if (expectedFill.GetValueOrDefault() != actualFill.GetValueOrDefault())
            {
                ssdtDifferences.Add(Difference.Index(
                    schema,
                    table,
                    expected.Name,
                    "FILLFACTOR",
                    expectedFill?.ToString(CultureInfo.InvariantCulture),
                    actualFill?.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    private static void CompareIndexColumns(
        string schema,
        string table,
        string index,
        string property,
        IReadOnlyList<DmmIndexColumn> expected,
        IReadOnlyList<DmmIndexColumn> actual,
        List<DmmDifference> ssdtDifferences)
    {
        if (expected.Count != actual.Count || !expected.Zip(actual, IndexColumnEquals).All(static result => result))
        {
            var expectedList = string.Join(", ", expected.Select(FormatIndexColumn));
            var actualList = string.Join(", ", actual.Select(FormatIndexColumn));
            ssdtDifferences.Add(Difference.Index(schema, table, index, property, expectedList, actualList));
        }
    }

    private static void CompareBooleanOption(
        string schema,
        string table,
        string index,
        string option,
        bool? expected,
        bool? actual,
        bool defaultValue,
        List<DmmDifference> ssdtDifferences)
    {
        var expectedValue = expected ?? defaultValue;
        var actualValue = actual ?? defaultValue;
        if (expectedValue != actualValue)
        {
            ssdtDifferences.Add(Difference.Index(
                schema,
                table,
                index,
                option,
                expectedValue ? "ON" : "OFF",
                actualValue ? "ON" : "OFF"));
        }
    }

    private static void CompareForeignKeys(
        DmmTable expected,
        DmmTable actual,
        List<DmmDifference> modelDifferences,
        List<DmmDifference> ssdtDifferences)
    {
        var actualLookup = actual.ForeignKeys
            .ToDictionary(foreignKey => foreignKey.Name, foreignKey => foreignKey, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var expectedForeignKey in expected.ForeignKeys)
        {
            if (!actualLookup.TryGetValue(expectedForeignKey.Name, out var actualForeignKey))
            {
                modelDifferences.Add(Difference.ForeignKey(
                    expected.Schema,
                    expected.Name,
                    expectedForeignKey.Name,
                    "Presence",
                    "Present",
                    "Missing"));
                continue;
            }

            seen.Add(expectedForeignKey.Name);

            if (!string.Equals(expectedForeignKey.ReferencedSchema, actualForeignKey.ReferencedSchema, StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add(Difference.ForeignKey(
                    expected.Schema,
                    expected.Name,
                    expectedForeignKey.Name,
                    "ReferencedSchema",
                    expectedForeignKey.ReferencedSchema,
                    actualForeignKey.ReferencedSchema));
            }

            if (!string.Equals(expectedForeignKey.ReferencedTable, actualForeignKey.ReferencedTable, StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add(Difference.ForeignKey(
                    expected.Schema,
                    expected.Name,
                    expectedForeignKey.Name,
                    "ReferencedTable",
                    expectedForeignKey.ReferencedTable,
                    actualForeignKey.ReferencedTable));
            }

            var expectedColumns = expectedForeignKey.Columns.ToArray();
            var actualColumns = actualForeignKey.Columns.ToArray();

            if (expectedColumns.Length != actualColumns.Length)
            {
                ssdtDifferences.Add(Difference.ForeignKey(
                    expected.Schema,
                    expected.Name,
                    expectedForeignKey.Name,
                    "ColumnCount",
                    expectedColumns.Length.ToString(CultureInfo.InvariantCulture),
                    actualColumns.Length.ToString(CultureInfo.InvariantCulture)));
            }

            if (!expectedColumns.SequenceEqual(actualColumns, ForeignKeyColumnComparer.Instance))
            {
                ssdtDifferences.Add(Difference.ForeignKey(
                    expected.Schema,
                    expected.Name,
                    expectedForeignKey.Name,
                    "Columns",
                    string.Join(", ", expectedColumns.Select(FormatForeignKeyColumn)),
                    string.Join(", ", actualColumns.Select(FormatForeignKeyColumn))));
            }

            if (!string.Equals(expectedForeignKey.DeleteAction, actualForeignKey.DeleteAction, StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add(Difference.ForeignKey(
                    expected.Schema,
                    expected.Name,
                    expectedForeignKey.Name,
                    "DeleteAction",
                    expectedForeignKey.DeleteAction,
                    actualForeignKey.DeleteAction));
            }

            if (expectedForeignKey.IsNotTrusted != actualForeignKey.IsNotTrusted)
            {
                ssdtDifferences.Add(Difference.ForeignKey(
                    expected.Schema,
                    expected.Name,
                    expectedForeignKey.Name,
                    "Trust",
                    expectedForeignKey.IsNotTrusted ? "NOT TRUSTED" : "TRUSTED",
                    actualForeignKey.IsNotTrusted ? "NOT TRUSTED" : "TRUSTED"));
            }
        }

        foreach (var actualForeignKey in actual.ForeignKeys)
        {
            if (!seen.Contains(actualForeignKey.Name))
            {
                ssdtDifferences.Add(Difference.ForeignKey(
                    actual.Schema,
                    actual.Name,
                    actualForeignKey.Name,
                    "Presence",
                    "Missing",
                    "Present"));
            }
        }
    }

    private static void CompareTableDescription(
        DmmTable expected,
        DmmTable actual,
        List<DmmDifference> ssdtDifferences)
    {
        var expectedDescription = NormalizeDescription(expected.Description);
        var actualDescription = NormalizeDescription(actual.Description);
        if (!string.Equals(expectedDescription, actualDescription, StringComparison.Ordinal))
        {
            ssdtDifferences.Add(Difference.Table(
                expected.Schema,
                expected.Name,
                "Description",
                ValueOrNull(expectedDescription),
                ValueOrNull(actualDescription)));
        }
    }

    private static string NormalizeDescription(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string? ValueOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FormatIndexColumn(DmmIndexColumn column)
        => column.IsDescending ? $"{column.Name} DESC" : column.Name;

    private static string FormatForeignKeyColumn(DmmForeignKeyColumn column)
        => $"{column.Column} -> {column.ReferencedColumn}";

    private static bool IndexColumnEquals(DmmIndexColumn expected, DmmIndexColumn actual)
        => string.Equals(expected.Name, actual.Name, StringComparison.OrdinalIgnoreCase) && expected.IsDescending == actual.IsDescending;

    private static string? NormalizeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, "\\s+", " ");
        return normalized;
    }

    private static string Key(string schema, string name) => $"{schema}.{name}";

    private static class Difference
    {
        public static DmmDifference Table(string schema, string table, string property, string? expected, string? actual, string? artifact = null)
            => new(schema, table, property, Expected: ValueOrNull(expected), Actual: ValueOrNull(actual), ArtifactPath: artifact);

        public static DmmDifference Column(string schema, string table, string column, string property, string? expected, string? actual)
            => new(schema, table, property, Column: column, Expected: ValueOrNull(expected), Actual: ValueOrNull(actual));

        public static DmmDifference Index(string schema, string table, string index, string property, string? expected, string? actual)
            => new(schema, table, property, Index: index, Expected: ValueOrNull(expected), Actual: ValueOrNull(actual));

        public static DmmDifference ForeignKey(string schema, string table, string foreignKey, string property, string? expected, string? actual)
            => new(schema, table, property, ForeignKey: foreignKey, Expected: ValueOrNull(expected), Actual: ValueOrNull(actual));
    }

    private sealed class ForeignKeyColumnComparer : IEqualityComparer<DmmForeignKeyColumn>
    {
        public static ForeignKeyColumnComparer Instance { get; } = new();

        public bool Equals(DmmForeignKeyColumn? x, DmmForeignKeyColumn? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.ReferencedColumn, y.ReferencedColumn, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(DmmForeignKeyColumn obj)
        {
            if (obj is null)
            {
                return 0;
            }

            var columnHash = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column ?? string.Empty);
            var referencedHash = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ReferencedColumn ?? string.Empty);
            return HashCode.Combine(columnHash, referencedHash);
        }
    }
}
