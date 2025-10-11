using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Smo;

namespace Osm.Dmm;

public sealed record SmoDmmLensRequest(SmoModel Model, NamingOverrideOptions NamingOverrides);

public sealed class SmoDmmLens : IDmmLens<SmoDmmLensRequest>
{
    public Task<Result<IAsyncEnumerable<DmmTable>>> ProjectAsync(
        SmoDmmLensRequest request,
        CancellationToken cancellationToken = default)
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
        return Task.FromResult(Result<IAsyncEnumerable<DmmTable>>.Success(
            EnumerateTables(request.Model, namingOverrides, cancellationToken)));
    }

    private static async IAsyncEnumerable<DmmTable> EnumerateTables(
        SmoModel model,
        NamingOverrideOptions namingOverrides,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var orderedTables = model.Tables
            .OrderBy(t => t.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var table in orderedTables)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                    column.Nullable,
                    column.Description))
                .ToArray();

            var primaryKey = table.Indexes.FirstOrDefault(index => index.IsPrimaryKey);
            var primaryKeyColumns = primaryKey is null
                ? Array.Empty<string>()
                : primaryKey.Columns
                    .OrderBy(column => column.Ordinal)
                    .Select(column => column.Name)
                    .ToArray();

            var indexes = table.Indexes
                .Where(static index => !index.IsPrimaryKey)
                .Select(MapIndex)
                .OrderBy(index => index.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var foreignKeys = table.ForeignKeys
                .Select(MapForeignKey)
                .OrderBy(fk => fk.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var dmmTable = new DmmTable(
                table.Schema,
                effectiveName,
                columns,
                primaryKeyColumns,
                indexes,
                foreignKeys,
                table.Description);

            yield return dmmTable;
            await Task.Yield();
        }
    }

    private static DmmIndex MapIndex(SmoIndexDefinition index)
    {
        var keyColumns = index.Columns
            .Where(static column => !column.IsIncluded)
            .OrderBy(static column => column.Ordinal)
            .Select(column => new DmmIndexColumn(column.Name, column.IsDescending))
            .ToArray();

        var includedColumns = index.Columns
            .Where(static column => column.IsIncluded)
            .OrderBy(static column => column.Ordinal)
            .Select(column => new DmmIndexColumn(column.Name, column.IsDescending))
            .ToArray();

        var metadata = index.Metadata;
        return new DmmIndex(
            index.Name,
            index.IsUnique,
            keyColumns,
            includedColumns,
            CanonicalizeFilter(metadata.FilterDefinition),
            metadata.IsDisabled,
            new DmmIndexOptions(
                metadata.IsPadded,
                metadata.FillFactor,
                metadata.IgnoreDuplicateKey,
                metadata.AllowRowLocks,
                metadata.AllowPageLocks,
                metadata.StatisticsNoRecompute));
    }

    private static DmmForeignKey MapForeignKey(SmoForeignKeyDefinition foreignKey)
    {
        return new DmmForeignKey(
            foreignKey.Name,
            foreignKey.Column,
            foreignKey.ReferencedSchema,
            foreignKey.ReferencedTable,
            foreignKey.ReferencedColumn,
            foreignKey.DeleteAction.ToString(),
            foreignKey.IsNoCheck);
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

    private static string? CanonicalizeFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var trimmed = filter.Trim();
        trimmed = Regex.Replace(trimmed, "\\s+", " ");
        return trimmed;
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
