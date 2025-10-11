using System;
using System.Collections.Generic;
using System.Linq;

namespace Osm.Dmm;

public sealed class DmmComparator
{
    public DmmComparisonResult Compare(
        IReadOnlyList<DmmTable> modelTables,
        IReadOnlyList<DmmTable> dmmTables)
    {
        if (modelTables is null)
        {
            throw new ArgumentNullException(nameof(modelTables));
        }

        if (dmmTables is null)
        {
            throw new ArgumentNullException(nameof(dmmTables));
        }

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

            CompareColumns(table, dmmTable, modelDifferences, ssdtDifferences);
            ComparePrimaryKeys(table, dmmTable, modelDifferences, ssdtDifferences);
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
        List<string> ssdtDifferences)
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

    private static string Key(string schema, string name) => $"{schema}.{name}";
}
