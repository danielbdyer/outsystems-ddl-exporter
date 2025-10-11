using System;
using System.Collections.Generic;
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

        var modelDifferences = new List<string>();
        var ssdtDifferences = new List<string>();
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
                modelDifferences.Add($"missing table {table.Schema}.{table.Name}");
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
                ssdtDifferences.Add($"unexpected table {table.Schema}.{table.Name}");
            }
        }

        var isMatch = modelDifferences.Count == 0 && ssdtDifferences.Count == 0;
        return new DmmComparisonResult(isMatch, modelDifferences, ssdtDifferences);
    }

    private static void CompareColumns(
        DmmTable expected,
        DmmTable actual,
        List<string> modelDifferences,
        List<string> ssdtDifferences,
        DmmComparisonFeatures features)
    {
        var expectedNames = expected.Columns.Select(c => c.Name).ToArray();
        var actualNames = actual.Columns.Select(c => c.Name).ToArray();

        var sequencesMatch = expectedNames.SequenceEqual(actualNames, StringComparer.OrdinalIgnoreCase);
        var expectedSet = new HashSet<string>(expectedNames, StringComparer.OrdinalIgnoreCase);
        var actualSet = new HashSet<string>(actualNames, StringComparer.OrdinalIgnoreCase);

        if (expectedNames.Length != actualNames.Length)
        {
            var message = $"column count mismatch for {expected.Schema}.{expected.Name}: expected {expectedNames.Length}, actual {actualNames.Length}";
            if (expectedNames.Length > actualNames.Length)
            {
                modelDifferences.Add(message);
            }
            else
            {
                ssdtDifferences.Add(message);
            }
        }

        var missingColumns = expectedNames
            .Where(name => !actualSet.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingColumns.Length > 0)
        {
            modelDifferences.Add($"missing columns for {expected.Schema}.{expected.Name}: {string.Join(", ", missingColumns)}");
        }

        var unexpectedColumns = actualNames
            .Where(name => !expectedSet.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unexpectedColumns.Length > 0)
        {
            ssdtDifferences.Add($"unexpected columns for {expected.Schema}.{expected.Name}: {string.Join(", ", unexpectedColumns)}");
        }

        if (!sequencesMatch && missingColumns.Length == 0 && unexpectedColumns.Length == 0)
        {
            ssdtDifferences.Add($"column order mismatch for {expected.Schema}.{expected.Name}: expected [{string.Join(", ", expectedNames)}], actual [{string.Join(", ", actualNames)}]");
        }

        var actualByName = actual.Columns
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var expectedColumn in expected.Columns)
        {
            if (!actualByName.TryGetValue(expectedColumn.Name, out var actualColumn))
            {
                continue;
            }

            if (!string.Equals(expectedColumn.DataType, actualColumn.DataType, StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add($"data type mismatch for {expected.Schema}.{expected.Name}.{expectedColumn.Name}: expected {expectedColumn.DataType}, actual {actualColumn.DataType}");
            }

            if (expectedColumn.IsNullable != actualColumn.IsNullable)
            {
                var expectation = expectedColumn.IsNullable ? "NULL" : "NOT NULL";
                var actualNullability = actualColumn.IsNullable ? "NULL" : "NOT NULL";
                ssdtDifferences.Add($"nullability mismatch for {expected.Schema}.{expected.Name}.{expectedColumn.Name}: expected {expectation}, actual {actualNullability}");
            }

            if (features.HasFlag(DmmComparisonFeatures.ExtendedProperties))
            {
                var expectedDescription = NormalizeDescription(expectedColumn.Description);
                var actualDescription = NormalizeDescription(actualColumn.Description);
                if (!string.Equals(expectedDescription, actualDescription, StringComparison.Ordinal))
                {
                    ssdtDifferences.Add($"extended property mismatch for {expected.Schema}.{expected.Name}.{expectedColumn.Name}: expected '{FormatDescription(expectedDescription)}', actual '{FormatDescription(actualDescription)}'");
                }
            }
        }
    }

    private static void ComparePrimaryKeys(
        DmmTable expected,
        DmmTable actual,
        List<string> modelDifferences,
        List<string> ssdtDifferences)
    {
        var expectedColumns = expected.PrimaryKeyColumns.ToArray();
        var actualColumns = actual.PrimaryKeyColumns.ToArray();

        if (expectedColumns.Length == 0)
        {
            if (actualColumns.Length > 0)
            {
                ssdtDifferences.Add($"unexpected primary key defined in DMM for {expected.Schema}.{expected.Name}");
            }

            return;
        }

        if (expectedColumns.Length != actualColumns.Length)
        {
            var message = $"primary key length mismatch for {expected.Schema}.{expected.Name}: expected {expectedColumns.Length}, actual {actualColumns.Length}";
            if (expectedColumns.Length > actualColumns.Length)
            {
                modelDifferences.Add(message);
            }
            else
            {
                ssdtDifferences.Add(message);
            }

            return;
        }

        for (var i = 0; i < expectedColumns.Length; i++)
        {
            if (!string.Equals(expectedColumns[i], actualColumns[i], StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add($"primary key mismatch for {expected.Schema}.{expected.Name} at ordinal {i + 1}: expected {expectedColumns[i]}, actual {actualColumns[i]}");
            }
        }
    }

    private static void CompareIndexes(
        DmmTable expected,
        DmmTable actual,
        List<string> modelDifferences,
        List<string> ssdtDifferences)
    {
        var actualLookup = actual.Indexes
            .ToDictionary(index => index.Name, index => index, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var expectedIndex in expected.Indexes)
        {
            if (!actualLookup.TryGetValue(expectedIndex.Name, out var actualIndex))
            {
                modelDifferences.Add($"missing index {expected.Schema}.{expected.Name}.{expectedIndex.Name}");
                continue;
            }

            seen.Add(expectedIndex.Name);
            CompareIndex(expected.Schema, expected.Name, expectedIndex, actualIndex, ssdtDifferences);
        }

        foreach (var actualIndex in actual.Indexes)
        {
            if (!seen.Contains(actualIndex.Name))
            {
                ssdtDifferences.Add($"unexpected index {actual.Schema}.{actual.Name}.{actualIndex.Name}");
            }
        }
    }

    private static void CompareIndex(
        string schema,
        string table,
        DmmIndex expected,
        DmmIndex actual,
        List<string> ssdtDifferences)
    {
        if (expected.IsUnique != actual.IsUnique)
        {
            ssdtDifferences.Add($"index uniqueness mismatch for {schema}.{table}.{expected.Name}: expected {(expected.IsUnique ? "UNIQUE" : "NONUNIQUE")}, actual {(actual.IsUnique ? "UNIQUE" : "NONUNIQUE")}");
        }

        CompareIndexColumns(schema, table, expected.Name, "key", expected.KeyColumns, actual.KeyColumns, ssdtDifferences);
        CompareIndexColumns(schema, table, expected.Name, "included", expected.IncludedColumns, actual.IncludedColumns, ssdtDifferences);

        var expectedFilter = NormalizeFilter(expected.FilterDefinition);
        var actualFilter = NormalizeFilter(actual.FilterDefinition);
        if (!string.Equals(expectedFilter, actualFilter, StringComparison.Ordinal))
        {
            ssdtDifferences.Add($"index filter mismatch for {schema}.{table}.{expected.Name}: expected '{FormatDescription(expectedFilter)}', actual '{FormatDescription(actualFilter)}'");
        }

        if (expected.IsDisabled != actual.IsDisabled)
        {
            ssdtDifferences.Add($"index disable state mismatch for {schema}.{table}.{expected.Name}: expected {(expected.IsDisabled ? "DISABLED" : "ENABLED")}, actual {(actual.IsDisabled ? "DISABLED" : "ENABLED")}");
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
                ssdtDifferences.Add($"index option mismatch for {schema}.{table}.{expected.Name}: FILLFACTOR expected {FormatNumericOption(expectedFill)}, actual {FormatNumericOption(actualFill)}");
            }
        }
    }

    private static void CompareIndexColumns(
        string schema,
        string table,
        string index,
        string kind,
        IReadOnlyList<DmmIndexColumn> expected,
        IReadOnlyList<DmmIndexColumn> actual,
        List<string> ssdtDifferences)
    {
        if (expected.Count != actual.Count || !expected.Zip(actual, IndexColumnEquals).All(static result => result))
        {
            var expectedList = string.Join(", ", expected.Select(FormatIndexColumn));
            var actualList = string.Join(", ", actual.Select(FormatIndexColumn));
            ssdtDifferences.Add($"index {kind} columns mismatch for {schema}.{table}.{index}: expected [{expectedList}], actual [{actualList}]");
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
        List<string> ssdtDifferences)
    {
        var expectedValue = expected ?? defaultValue;
        var actualValue = actual ?? defaultValue;
        if (expectedValue != actualValue)
        {
            ssdtDifferences.Add($"index option mismatch for {schema}.{table}.{index}: {option} expected {(expectedValue ? "ON" : "OFF")}, actual {(actualValue ? "ON" : "OFF")}");
        }
    }

    private static string FormatIndexColumn(DmmIndexColumn column)
        => column.IsDescending ? $"{column.Name} DESC" : column.Name;

    private static bool IndexColumnEquals(DmmIndexColumn expected, DmmIndexColumn actual)
        => string.Equals(expected.Name, actual.Name, StringComparison.OrdinalIgnoreCase) && expected.IsDescending == actual.IsDescending;

    private static string FormatNumericOption(int? value)
        => value.HasValue ? value.Value.ToString() : "<unspecified>";

    private static void CompareForeignKeys(
        DmmTable expected,
        DmmTable actual,
        List<string> modelDifferences,
        List<string> ssdtDifferences)
    {
        var actualLookup = actual.ForeignKeys
            .ToDictionary(foreignKey => foreignKey.Name, foreignKey => foreignKey, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var expectedForeignKey in expected.ForeignKeys)
        {
            if (!actualLookup.TryGetValue(expectedForeignKey.Name, out var actualForeignKey))
            {
                modelDifferences.Add($"missing foreign key {expected.Schema}.{expected.Name}.{expectedForeignKey.Name}");
                continue;
            }

            seen.Add(expectedForeignKey.Name);

            if (!string.Equals(expectedForeignKey.Column, actualForeignKey.Column, StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add($"foreign key column mismatch for {expected.Schema}.{expected.Name}.{expectedForeignKey.Name}: expected {expectedForeignKey.Column}, actual {actualForeignKey.Column}");
            }

            if (!string.Equals(expectedForeignKey.ReferencedSchema, actualForeignKey.ReferencedSchema, StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add($"foreign key schema mismatch for {expected.Schema}.{expected.Name}.{expectedForeignKey.Name}: expected {expectedForeignKey.ReferencedSchema}, actual {actualForeignKey.ReferencedSchema}");
            }

            if (!string.Equals(expectedForeignKey.ReferencedTable, actualForeignKey.ReferencedTable, StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add($"foreign key table mismatch for {expected.Schema}.{expected.Name}.{expectedForeignKey.Name}: expected {expectedForeignKey.ReferencedTable}, actual {actualForeignKey.ReferencedTable}");
            }

            if (!string.Equals(expectedForeignKey.ReferencedColumn, actualForeignKey.ReferencedColumn, StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add($"foreign key referenced column mismatch for {expected.Schema}.{expected.Name}.{expectedForeignKey.Name}: expected {expectedForeignKey.ReferencedColumn}, actual {actualForeignKey.ReferencedColumn}");
            }

            if (!string.Equals(expectedForeignKey.DeleteAction, actualForeignKey.DeleteAction, StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add($"foreign key delete action mismatch for {expected.Schema}.{expected.Name}.{expectedForeignKey.Name}: expected {expectedForeignKey.DeleteAction}, actual {actualForeignKey.DeleteAction}");
            }

            if (expectedForeignKey.IsNotTrusted != actualForeignKey.IsNotTrusted)
            {
                ssdtDifferences.Add($"foreign key trust mismatch for {expected.Schema}.{expected.Name}.{expectedForeignKey.Name}: expected {(expectedForeignKey.IsNotTrusted ? "NOT TRUSTED" : "TRUSTED")}, actual {(actualForeignKey.IsNotTrusted ? "NOT TRUSTED" : "TRUSTED")}");
            }
        }

        foreach (var actualForeignKey in actual.ForeignKeys)
        {
            if (!seen.Contains(actualForeignKey.Name))
            {
                ssdtDifferences.Add($"unexpected foreign key {actual.Schema}.{actual.Name}.{actualForeignKey.Name}");
            }
        }
    }

    private static void CompareTableDescription(
        DmmTable expected,
        DmmTable actual,
        List<string> ssdtDifferences)
    {
        var expectedDescription = NormalizeDescription(expected.Description);
        var actualDescription = NormalizeDescription(actual.Description);
        if (!string.Equals(expectedDescription, actualDescription, StringComparison.Ordinal))
        {
            ssdtDifferences.Add($"extended property mismatch for {expected.Schema}.{expected.Name}: expected '{FormatDescription(expectedDescription)}', actual '{FormatDescription(actualDescription)}'");
        }
    }

    private static string NormalizeDescription(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string FormatDescription(string value)
        => string.IsNullOrWhiteSpace(value) ? "<none>" : value;

    private static string NormalizeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, "\\s+", " ");
        return normalized;
    }

    private static string Key(string schema, string name) => $"{schema}.{name}";
}
