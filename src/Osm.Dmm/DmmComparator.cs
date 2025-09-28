using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
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

        var modelDifferences = new List<string>();
        var ssdtDifferences = new List<string>();
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
        SmoTableDefinition table,
        DmmTable dmmTable,
        List<string> modelDifferences,
        List<string> ssdtDifferences)
    {
        var expectedNames = table.Columns.Select(c => c.Name).ToArray();
        var actualNames = dmmTable.Columns.Select(c => c.Name).ToArray();

        var sequencesMatch = expectedNames.SequenceEqual(actualNames, StringComparer.OrdinalIgnoreCase);
        var expectedSet = new HashSet<string>(expectedNames, StringComparer.OrdinalIgnoreCase);
        var actualSet = new HashSet<string>(actualNames, StringComparer.OrdinalIgnoreCase);

        if (expectedNames.Length != actualNames.Length)
        {
            var message = $"column count mismatch for {table.Schema}.{table.Name}: expected {expectedNames.Length}, actual {actualNames.Length}";
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
            modelDifferences.Add($"missing columns for {table.Schema}.{table.Name}: {string.Join(", ", missingColumns)}");
        }

        var unexpectedColumns = actualNames
            .Where(name => !expectedSet.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unexpectedColumns.Length > 0)
        {
            ssdtDifferences.Add($"unexpected columns for {table.Schema}.{table.Name}: {string.Join(", ", unexpectedColumns)}");
        }

        if (!sequencesMatch && missingColumns.Length == 0 && unexpectedColumns.Length == 0)
        {
            ssdtDifferences.Add($"column order mismatch for {table.Schema}.{table.Name}: expected [{string.Join(", ", expectedNames)}], actual [{string.Join(", ", actualNames)}]");
        }

        var actualByName = dmmTable.Columns
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var expected in table.Columns)
        {
            if (!actualByName.TryGetValue(expected.Name, out var actual))
            {
                continue;
            }

            var expectedType = Canonicalize(expected.DataType);
            if (!string.Equals(expectedType, actual.DataType, StringComparison.OrdinalIgnoreCase))
            {
                ssdtDifferences.Add($"data type mismatch for {table.Schema}.{table.Name}.{expected.Name}: expected {expectedType}, actual {actual.DataType}");
            }

            if (expected.Nullable != actual.IsNullable)
            {
                var expectation = expected.Nullable ? "NULL" : "NOT NULL";
                var actualNullability = actual.IsNullable ? "NULL" : "NOT NULL";
                ssdtDifferences.Add($"nullability mismatch for {table.Schema}.{table.Name}.{expected.Name}: expected {expectation}, actual {actualNullability}");
            }
        }
    }

    private static void ComparePrimaryKeys(
        SmoTableDefinition table,
        DmmTable dmmTable,
        List<string> modelDifferences,
        List<string> ssdtDifferences)
    {
        var expected = table.Indexes.FirstOrDefault(i => i.IsPrimaryKey);
        if (expected is null)
        {
            if (dmmTable.PrimaryKeyColumns.Count > 0)
            {
                ssdtDifferences.Add($"unexpected primary key defined in DMM for {table.Schema}.{table.Name}");
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
            var message = $"primary key length mismatch for {table.Schema}.{table.Name}: expected {expectedColumns.Length}, actual {actualColumns.Length}";
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
                ssdtDifferences.Add($"primary key mismatch for {table.Schema}.{table.Name} at ordinal {i + 1}: expected {expectedColumns[i]}, actual {actualColumns[i]}");
            }
        }
    }

    private static string Key(string schema, string name) => $"{schema}.{name}";

    private static string Canonicalize(DataType dataType)
    {
        return dataType.SqlDataType switch
        {
            SqlDataType.BigInt => "bigint",
            SqlDataType.Int => "int",
            SqlDataType.SmallInt => "smallint",
            SqlDataType.TinyInt => "tinyint",
            SqlDataType.Bit => "bit",
            SqlDataType.Date => "date",
            SqlDataType.DateTime => "datetime",
            SqlDataType.Float => "float",
            SqlDataType.Real => "real",
            SqlDataType.Decimal => FormatDecimal(dataType.NumericPrecision, dataType.NumericScale, "decimal"),
            SqlDataType.Numeric => FormatDecimal(dataType.NumericPrecision, dataType.NumericScale, "decimal"),
            SqlDataType.Money => "money",
            SqlDataType.SmallMoney => "smallmoney",
            SqlDataType.UniqueIdentifier => "uniqueidentifier",
            SqlDataType.VarChar => FormatLengthType("varchar", dataType),
            SqlDataType.NVarChar => FormatLengthType("nvarchar", dataType),
            SqlDataType.Char => FormatLengthType("char", dataType),
            SqlDataType.NChar => FormatLengthType("nchar", dataType),
            SqlDataType.VarBinary => FormatLengthType("varbinary", dataType),
            SqlDataType.Binary => FormatLengthType("binary", dataType),
            SqlDataType.Text => "text",
            SqlDataType.NText => "ntext",
            SqlDataType.Image => "image",
            _ => dataType.SqlDataType.ToString().ToLowerInvariant(),
        };
    }

    private static string FormatDecimal(int numericPrecision, int numericScale, string baseName)
    {
        var precision = numericPrecision <= 0 ? 18 : numericPrecision;
        var scale = numericScale < 0 ? 0 : numericScale;
        return $"{baseName}({precision},{scale})";
    }

    private static string FormatLengthType(string baseName, DataType dataType)
    {
        if (dataType.MaximumLength < 0)
        {
            return $"{baseName}(max)";
        }

        if (dataType.MaximumLength == 0)
        {
            return baseName;
        }

        return $"{baseName}({dataType.MaximumLength})";
    }
}
