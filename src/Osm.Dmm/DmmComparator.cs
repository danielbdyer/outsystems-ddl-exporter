using System;
using System.Collections.Generic;
using System.Linq;
using Osm.Smo;

namespace Osm.Dmm;

public sealed class DmmComparator
{
    public DmmComparisonResult Compare(SmoModel smoModel, IReadOnlyList<DmmTable> dmmTables)
    {
        if (smoModel is null)
        {
            throw new ArgumentNullException(nameof(smoModel));
        }

        if (dmmTables is null)
        {
            throw new ArgumentNullException(nameof(dmmTables));
        }

        var differences = new List<string>();
        var dmmLookup = dmmTables.ToDictionary(
            table => Key(table.Schema, table.Name),
            table => table,
            StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in smoModel.Tables)
        {
            var key = Key(table.Schema, table.Name);
            if (!dmmLookup.TryGetValue(key, out var dmmTable))
            {
                differences.Add($"missing table {table.Schema}.{table.Name}");
                continue;
            }

            seen.Add(key);

            CompareColumns(table, dmmTable, differences);
            ComparePrimaryKeys(table, dmmTable, differences);
        }

        foreach (var table in dmmTables)
        {
            var key = Key(table.Schema, table.Name);
            if (!seen.Contains(key))
            {
                differences.Add($"unexpected table {table.Schema}.{table.Name}");
            }
        }

        return new DmmComparisonResult(differences.Count == 0, differences);
    }

    private static void CompareColumns(SmoTableDefinition table, DmmTable dmmTable, List<string> differences)
    {
        if (table.Columns.Length != dmmTable.Columns.Count)
        {
            differences.Add($"column count mismatch for {table.Schema}.{table.Name}: expected {table.Columns.Length}, actual {dmmTable.Columns.Count}");
            return;
        }

        for (var i = 0; i < table.Columns.Length; i++)
        {
            var expected = table.Columns[i];
            var actual = dmmTable.Columns[i];
            if (!string.Equals(expected.Name, actual.Name, StringComparison.OrdinalIgnoreCase))
            {
                differences.Add($"column name mismatch at ordinal {i + 1} for {table.Schema}.{table.Name}: expected {expected.Name}, actual {actual.Name}");
            }

            if (expected.Nullable != actual.IsNullable)
            {
                var expectation = expected.Nullable ? "NULL" : "NOT NULL";
                var actualNullability = actual.IsNullable ? "NULL" : "NOT NULL";
                differences.Add($"nullability mismatch for {table.Schema}.{table.Name}.{expected.Name}: expected {expectation}, actual {actualNullability}");
            }
        }
    }

    private static void ComparePrimaryKeys(SmoTableDefinition table, DmmTable dmmTable, List<string> differences)
    {
        var expected = table.Indexes.FirstOrDefault(i => i.IsPrimaryKey);
        if (expected is null)
        {
            if (dmmTable.PrimaryKeyColumns.Count > 0)
            {
                differences.Add($"unexpected primary key defined in DMM for {table.Schema}.{table.Name}");
            }

            return;
        }

        var expectedColumns = expected.Columns
            .OrderBy(c => c.Ordinal)
            .Select(c => c.Name)
            .ToArray();
        var actualColumns = dmmTable.PrimaryKeyColumns.ToArray();

        if (expectedColumns.Length != actualColumns.Length)
        {
            differences.Add($"primary key length mismatch for {table.Schema}.{table.Name}: expected {expectedColumns.Length}, actual {actualColumns.Length}");
            return;
        }

        for (var i = 0; i < expectedColumns.Length; i++)
        {
            if (!string.Equals(expectedColumns[i], actualColumns[i], StringComparison.OrdinalIgnoreCase))
            {
                differences.Add($"primary key mismatch for {table.Schema}.{table.Name} at ordinal {i + 1}: expected {expectedColumns[i]}, actual {actualColumns[i]}");
            }
        }
    }

    private static string Key(string schema, string name) => $"{schema}.{name}";
}
