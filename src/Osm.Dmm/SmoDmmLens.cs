using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Smo;

namespace Osm.Dmm;

public sealed record SmoDmmLensRequest(SmoModel Model, NamingOverrideOptions NamingOverrides);

public sealed class SmoDmmLens : IDmmLens<SmoDmmLensRequest>
{
    public Result<IReadOnlyList<DmmTable>> Project(SmoDmmLensRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Model is null)
        {
            throw new ArgumentNullException(nameof(request.Model));
        }

        var namingOverrides = request.NamingOverrides ?? NamingOverrideOptions.Empty;
        var tables = new List<DmmTable>(request.Model.Tables.Length);

        foreach (var table in request.Model.Tables)
        {
            var effectiveName = table.Name;
            if (namingOverrides.TryGetTableOverride(table.Schema, table.Name, out var tableOverride))
            {
                effectiveName = tableOverride;
            }
            else if (namingOverrides.TryGetEntityOverride(table.OriginalModule, table.LogicalName, out var entityOverride))
            {
                effectiveName = entityOverride.Value;
            }

            var columns = table.Columns
                .Select(column => new DmmColumn(
                    column.Name,
                    Canonicalize(column.DataType),
                    column.Nullable))
                .ToArray();

            var primaryKey = table.Indexes.FirstOrDefault(index => index.IsPrimaryKey);
            var primaryKeyColumns = primaryKey is null
                ? Array.Empty<string>()
                : primaryKey.Columns
                    .OrderBy(column => column.Ordinal)
                    .Select(column => column.Name)
                    .ToArray();

            tables.Add(new DmmTable(table.Schema, effectiveName, columns, primaryKeyColumns));
        }

        var orderedTables = tables
            .OrderBy(t => t.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Result<IReadOnlyList<DmmTable>>.Success(orderedTables);
    }

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
